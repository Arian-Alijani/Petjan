using System.Text.Json;
using Animora.Contracts.Identity;
using Animora.Desktop.Pages.Login;
using Animora.Domain.Clinical;
using Animora.Domain.Patients;
using Animora.Domain.Sales;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Seed;
using Microsoft.EntityFrameworkCore;
using Animora.Contracts.Patients;
using Animora.Desktop;
using Animora.Infrastructure.Local.Security;
using Animora.Persistence.Sqlite.Maintenance;
using Animora.Persistence.Sqlite.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Animora.Domain.Tests;

public sealed class LocalTestProfileTests
{
    [Fact]
    public void NormalLaunch_DoesNotSelectTestStorage()
    {
        Assert.Null(LocalTestProfile.Parse([]));
        Assert.Null(LocalTestProfile.Parse(["--some-avalonia-option"]));
        var services = new ServiceCollection();
        App.ConfigureServices(services);
        using var provider = services.BuildServiceProvider();
        Assert.Equal(new LocalStoreOptions().DatabasePath,
            provider.GetRequiredService<IOptions<LocalStoreOptions>>().Value.DatabasePath);
        var options = provider.GetRequiredService<IOptions<LocalStoreOptions>>().Value;
        Assert.Equal(new LocalStoreOptions().SeedSampleData, options.SeedSampleData);
        Assert.Null(options.ClinicBakPreviewPath); // normal startup never imports or auto-authenticates a preview
    }

    [Fact]
    public void DebugDefault_ReusesManualProfileButExplicitClinicPreservesNormalStorage()
    {
        var root = Path.GetTempPath();
        var defaultProfile = LocalTestProfile.Parse([], root, useDefaultTestProfile: true)!;
        Assert.Equal("manual", defaultProfile.Name);
        Assert.Equal(LocalTestProfile.Parse(["--local-test"], root)!.DataDirectory,
            defaultProfile.DataDirectory);
        Assert.Equal(defaultProfile.DataDirectory,
            LocalTestProfile.Parse(["--some-avalonia-option"], root, useDefaultTestProfile: true)!.DataDirectory);
        Assert.Null(LocalTestProfile.Parse(["--clinic"], root, useDefaultTestProfile: true));
        Assert.Null(LocalTestProfile.Parse(["--clinic"], root));
        Assert.Equal("sales", LocalTestProfile.Parse(["--local-test=sales"], root,
            useDefaultTestProfile: true)!.Name);
        Assert.StartsWith("fresh-", LocalTestProfile.Parse(["--local-test-fresh"], root,
            useDefaultTestProfile: true)!.Name);
    }

    [Theory]
    [InlineData("--local-test")]
    [InlineData("--local-test=manual")]
    [InlineData("--local-test-fresh")]
    public void ClinicAndTestFlags_AreRejectedWithoutFallingBack(string testArg) =>
        Assert.Throws<ArgumentException>(() => LocalTestProfile.Parse(
            ["--clinic", testArg], useDefaultTestProfile: true));

    [Fact]
    public void NamedProfiles_AreStableAndNeverShareNormalOrOtherTestStorage()
    {
        var root = Path.GetTempPath();
        var first = LocalTestProfile.Parse(["--local-test"], root)!;
        var repeated = LocalTestProfile.Parse(["--local-test=manual"], root)!;
        var other = LocalTestProfile.Parse(["--local-test=sales"], root)!;
        Assert.Equal(first.DataDirectory, repeated.DataDirectory);
        Assert.NotEqual(first.DataDirectory, other.DataDirectory);
        var options = new LocalStoreOptions();
        var normalPath = options.DatabasePath;
        first.ConfigureStore(options);
        Assert.NotEqual(normalPath, options.DatabasePath);
        Assert.StartsWith(Path.Combine(root, "Animora.LocalTests", "manual"), options.DatabasePath);
        Assert.Equal(Path.Combine(first.DataDirectory, "backups"), options.BackupDirectory);
        Assert.False(options.SeedSampleData); // real owner setup must not be blocked by fixture users
        Assert.False(options.EncryptionEnabled);
        Assert.True(options.Validate().IsSuccess);
    }

    [Fact]
    public void FreshProfiles_AreUniqueWithoutDeletingOrReusingExistingData()
    {
        var first = LocalTestProfile.Parse(["--local-test-fresh"], Path.GetTempPath())!;
        var second = LocalTestProfile.Parse(["--local-test-fresh"], Path.GetTempPath())!;
        Assert.NotEqual(first.DataDirectory, second.DataDirectory);
        Assert.StartsWith("fresh-", first.Name);
        Assert.Equal(first.DataDirectory,
            LocalTestProfile.Parse([$"--local-test={first.Name}"], Path.GetTempPath())!.DataDirectory);
        Assert.False(Directory.Exists(first.DataDirectory)); // parsing performs no filesystem writes
    }

    [Theory]
    [InlineData("--local-test=")]
    [InlineData("--local-test=../Animora")]
    [InlineData("--local-test=/Animora")]
    [InlineData("--local-test=..\\Animora")]
    [InlineData("--local-test=UPPER")]
    [InlineData("--local-test=profile with spaces")]
    [InlineData("--local-test-typo")]
    [InlineData("--local-test=" + "abcdefghijklmnopqrstuvwxyzabcdefghijklmnopqrstuvwxyz")]
    public void UnsafeOrMistypedFlags_FailBeforeNormalStorageCanOpen(string arg) =>
        Assert.Throws<ArgumentException>(() => LocalTestProfile.Parse([arg]));

    [Fact]
    public void ConflictingFlags_AreRejected()
    {
        Assert.Throws<ArgumentException>(() => LocalTestProfile.Parse(["--local-test", "--local-test=sales"]));
        Assert.Throws<ArgumentException>(() => LocalTestProfile.Parse(["--local-test", "--local-test-fresh"]));
    }

    [Fact]
    public void TestComposition_StillUsesRealAuthenticationAndBusinessServices()
    {
        var profile = LocalTestProfile.Parse(["--local-test"], Path.GetTempPath())!;
        var services = new ServiceCollection();
        App.ConfigureServices(services, profile);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        Assert.IsType<Animora.Client.Services.Identity.AuthenticationService>(
            provider.GetRequiredService<IAuthenticationService>());
        Assert.IsType<Animora.Client.Services.Patients.PatientsService>(
            provider.GetRequiredService<IPatientsQueryService>());
        Assert.Equal(profile.DataDirectory,
            provider.GetRequiredService<IOptions<LocalStoreOptions>>().Value.DataDirectory);
    }

    [Fact]
    public async Task RealLocalStore_SetupSignInRestartAndFreshProfile_AreIsolated()
    {
        var root = Path.Combine(Path.GetTempPath(), "animora-local-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        // A stand-in for existing clinic data. It must remain byte-for-byte unchanged.
        var normal = Path.Combine(root, "clinic.db");
        await File.WriteAllTextAsync(normal, "existing clinic data must not change");
        try
        {
            var profile = LocalTestProfile.Parse(["--local-test=manual"], root)!;
            await using (var provider = Build(profile))
            {
                await Initialize(provider);
                var auth = provider.GetRequiredService<IAuthenticationService>();
                Assert.True(await auth.IsFirstRunAsync());
                var setup = await auth.CompleteFirstRunAsync(
                    new FirstRunRequest("Test Clinic", "Test Manager", "tester", "Secret123"));
                Assert.True(setup.Succeeded, setup.ErrorCode);
                await auth.SignOutAsync("local-test-regression");
                Assert.False((await auth.SignInAsync("tester", "Wrong123", false)).Succeeded);
                Assert.True((await auth.SignInAsync("tester", "Secret123", false)).Succeeded);
                Assert.False(await auth.IsFirstRunAsync());
                await auth.SignOutAsync("local-test-restart");
            }
            // A new process/container reads the same durable credentials and data.
            await using (var reopened = Build(profile))
            {
                await Initialize(reopened);
                var auth = reopened.GetRequiredService<IAuthenticationService>();
                Assert.False(await auth.IsFirstRunAsync());
                Assert.True((await auth.SignInAsync("tester", "Secret123", false)).Succeeded);
                await auth.SignOutAsync("local-test-reopen");
            }
            var fresh = LocalTestProfile.Parse(["--local-test-fresh"], root)!;
            await using (var provider = Build(fresh))
            {
                await Initialize(provider);
                Assert.True(await provider.GetRequiredService<IAuthenticationService>().IsFirstRunAsync());
            }
            Assert.Equal("existing clinic data must not change", await File.ReadAllTextAsync(normal));
            Assert.True(File.Exists(Path.Combine(profile.DataDirectory, "animora.db")));
            Assert.True(File.Exists(Path.Combine(fresh.DataDirectory, "animora.db")));
        }
        finally
        {
            // Pooled SQLite handles prevent deleting the isolated fixture directory on Windows.
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("--clinic")]
    [InlineData("--local-test")]
    [InlineData("--local-test-fresh")]
    public void ClinicPreview_RejectsMixedSelectorsBeforeReadingFiles(string selector) =>
        Assert.Throws<ArgumentException>(() => LocalTestProfile.Parse(["--clinic-bak=missing.json", selector]));

    [Fact]
    public void ClinicPreview_RejectsMissingTypoAndReservedManualProfiles()
    {
        Assert.Throws<ArgumentException>(() => LocalTestProfile.Parse(["--clinic-bak="]));
        Assert.Throws<ArgumentException>(() => LocalTestProfile.Parse(["--clinic-bak-typo"]));
        Assert.Throws<ArgumentException>(() => LocalTestProfile.Parse(["--clinic-bak=missing.json"]));
        Assert.Throws<ArgumentException>(() => LocalTestProfile.Parse(["--local-test=clinic-bak-existing"]));
    }

    [Fact]
    public async Task ClinicPreview_RealImportAutoLoginRestartAndNormalDataSafety()
    {
        var root = Path.Combine(Path.GetTempPath(), "animora-clinic-preview-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var sentinel = Path.Combine(root, "clinic.db");
        await File.WriteAllTextAsync(sentinel, "original clinic bytes");
        try
        {
            var input = WritePreview(root);
            // Explicit opt-in for the full extracted snapshot; ordinary CI uses the tiny fixture.
            var actual = Environment.GetEnvironmentVariable("ANIMORA_CLINIC_PREVIEW");
            if (!string.IsNullOrEmpty(actual)) File.Copy(actual, input, overwrite: true);
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(input));
            var owners = document.RootElement.GetProperty("owners").EnumerateArray().ToArray();
            var petCount = owners.Sum(o => o.GetProperty("pets").GetArrayLength());
            var expectedHistory = owners.Sum(o => o.GetProperty("pets").EnumerateArray()
                .Sum(p => p.GetProperty("history").GetArrayLength()));
            var profile = LocalTestProfile.Parse([$"--clinic-bak={input}"], root, useDefaultTestProfile: true)!;
            var options = new LocalStoreOptions();
            profile.ConfigureStore(options);
            Assert.True(options.Validate().IsSuccess);
            Assert.False(options.SeedSampleData);
            Assert.NotNull(options.ClinicBakPreviewPath);
            Assert.Equal(profile.DataDirectory, LocalTestProfile.Parse([$"--clinic-bak={input}"], root)!.DataDirectory);
            await using (var provider = Build(profile))
            {
                await Initialize(provider);
                var auth = provider.GetRequiredService<IAuthenticationService>();
                Assert.True(await auth.IsFirstRunAsync()); // importer never seeds credential-less staff
                var login = provider.GetRequiredService<LoginViewModel>();
                var opened = 0;
                login.LoginSucceeded += (_, _) => opened++;
                await login.InitializeAsync(storeReady: true);
                Assert.Equal(1, opened);
                Assert.False(login.HasError, login.ErrorMessage);
                Assert.False(await auth.IsFirstRunAsync());
                await using var scope = provider.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
                Assert.Equal(owners.Length, await db.Set<Owner>().CountAsync());
                Assert.Equal(petCount, await db.Set<Pet>().CountAsync());
                Assert.Equal(expectedHistory, await db.Set<MedicalHistoryEntry>()
                    .CountAsync(h => h.SourceEntityType != null && h.SourceEntityType.StartsWith("LegacyClinic:")));
                Assert.Equal(0, await db.Set<Invoice>().CountAsync());
                Assert.Equal(0, await db.Set<VaccinationRecord>().CountAsync());
                var first = owners[0].GetProperty("pets")[0];
                var pet = await db.Set<Pet>().FirstAsync(p => p.Name == first.GetProperty("name").GetString());
                Assert.Equal((Animora.Domain.Patients.PetSpecies)first.GetProperty("species").GetInt32(), pet.Species);
                Assert.NotNull(pet.BirthDateUtc);
                Assert.True(pet.BirthDateUtc < pet.RegisteredUtc);
                await auth.SignOutAsync("preview-test");
            }
            // Restart uses the same marker and authenticates, not duplicate-import or bypass.
            await using (var reopened = Build(profile))
            {
                await Initialize(reopened);
                var login = reopened.GetRequiredService<LoginViewModel>();
                var opened = false;
                login.LoginSucceeded += (_, _) => opened = true;
                await login.InitializeAsync(true);
                Assert.True(opened);
                await using var scope = reopened.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
                Assert.Equal(petCount, await db.Set<Pet>().CountAsync());
                Assert.Equal(1, await db.Set<SeedMarker>().CountAsync(m => m.SeedKey == ClinicBakPreviewData.SeedKey));
            }
            Assert.Equal("original clinic bytes", await File.ReadAllTextAsync(sentinel));
            // A changed input selects a new folder without touching or deleting the first.
            await File.AppendAllTextAsync(input, " ");
            Assert.NotEqual(profile.DataDirectory, LocalTestProfile.Parse([$"--clinic-bak={input}"], root)!.DataDirectory);
            Assert.True(File.Exists(Path.Combine(profile.DataDirectory, "animora.db")));
            Assert.True(options.Validate().IsFailure); // content/profile mismatch fails closed
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ClinicPreview_RefusesNonemptyStoreAndDoesNotCreateImportMarker()
    {
        var root = Path.Combine(Path.GetTempPath(), "animora-preview-refusal-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var input = WritePreview(root);
            var profile = LocalTestProfile.Parse([$"--clinic-bak={input}"], root)!;
            var services = new ServiceCollection();
            App.ConfigureServices(services, profile);
            // Create existing business data at the same isolated path, without import enabled.
            services.PostConfigure<LocalStoreOptions>(o => o.ClinicBakPreviewPath = null);
            await using (var provider = services.BuildServiceProvider())
            {
                await Initialize(provider);
                await using var scope = provider.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
                db.Add(Owner.Register("T-LOCAL-DEFAULT", "Existing owner", "09120000000", new TestClock()));
                await db.SaveChangesAsync();
            }
            await using (var provider = Build(profile))
            {
                await using var scope = provider.CreateAsyncScope();
                var result = await scope.ServiceProvider.GetRequiredService<ILocalStoreInitializer>().InitializeAsync(CancellationToken.None);
                Assert.True(result.IsFailure);
                var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
                Assert.Equal(1, await db.Set<Owner>().CountAsync());
                Assert.Equal(0, await db.Set<Pet>().CountAsync());
                Assert.False(await db.Set<SeedMarker>().AnyAsync(m => m.SeedKey == ClinicBakPreviewData.SeedKey));
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    private static string WritePreview(string root)
    {
        var path = Path.Combine(root, "preview.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            formatVersion = 1, sourceSha256 = ClinicBakPreviewData.SourceSha256,
            owners = new[] { new { fullName = "Test owner", phone = "", registeredDate = "1402/12/14",
                pets = new[] { new { code = "1034", name = "Test cat", species = 1, breed = "Source breed",
                    birthDate = "1401/07/01", registeredDate = "1402/12/14", isNeutered = true,
                    history = new[] { new { sourceTable = "tblPatient", sourceId = "1034", date = "1402/12/14",
                        title = "Original record", detail = "Legacy metadata preserved" } } } } } }
        }));
        return path;
    }

    private sealed class TestClock : Animora.Domain.Time.IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }

    private static ServiceProvider Build(LocalTestProfile profile)
    {
        var services = new ServiceCollection();
        App.ConfigureServices(services, profile);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private static async Task Initialize(ServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<ILocalStoreInitializer>().InitializeAsync(CancellationToken.None);
        Assert.True(result.IsSuccess, result.Detail);
    }
}
