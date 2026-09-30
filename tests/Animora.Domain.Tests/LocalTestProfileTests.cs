using Animora.Contracts.Identity;
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
        Assert.True(provider.GetRequiredService<IOptions<LocalStoreOptions>>().Value.SeedSampleData);
    }

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
