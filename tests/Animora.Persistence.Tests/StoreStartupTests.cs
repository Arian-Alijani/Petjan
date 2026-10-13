using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Animora.Domain.Results;
using Animora.Infrastructure.Local.Startup;
using Animora.Persistence.Sqlite.Context;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Animora.Persistence.Tests;

/// <summary>
/// A11d's 8-step local store startup: the steps run in the exact order, the session pragmas hold
/// after init, and the store fails closed when an encrypted store has no key or K2 is not approved.
/// </summary>
public sealed class StoreStartupTests
{
    private static readonly string[] ExpectedSteps =
    [
        LocalStoreStartup.StepDataDirectory,
        LocalStoreStartup.StepOptionsAndKey,
        LocalStoreStartup.StepOpenAndAssertNative,
        LocalStoreStartup.StepIntegrityCheck,
        LocalStoreStartup.StepMigrate,
        LocalStoreStartup.StepCheckpoint,
        LocalStoreStartup.StepBootstrapAndSeed,
        LocalStoreStartup.StepDiagnostics,
    ];

    [Fact]
    public async Task Initialize_RunsTheEightStepsInOrder()
    {
        using var fixture = new LocalStoreTestFixture();

        var (result, steps) = await fixture.InitializeWithStepsAsync();

        Assert.True(result.IsSuccess, result.Detail);
        Assert.Equal(ExpectedSteps, steps);
    }

    [Fact]
    public async Task Initialize_LeavesTheSessionPragmasInForce()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.InitializeAsync();

        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        await db.Database.OpenConnectionAsync(); // via EF: the connection interceptor applies the pragmas.
        var connection = (SqliteConnection)db.Database.GetDbConnection();

        Assert.Equal("wal", Scalar(connection, "PRAGMA journal_mode;"));
        Assert.Equal("1", Scalar(connection, "PRAGMA synchronous;")); // NORMAL.
        Assert.Equal("5000", Scalar(connection, "PRAGMA busy_timeout;"));
        Assert.Equal("1", Scalar(connection, "PRAGMA foreign_keys;"));

        await db.Database.CloseConnectionAsync();
    }

    [Fact]
    public async Task Initialize_ReportsDiagnostics()
    {
        using var fixture = new LocalStoreTestFixture();
        var diagnostics = await fixture.InitializeAsync();

        Assert.Equal(Path.GetFullPath(fixture.DatabasePath), diagnostics.DatabasePath);
        Assert.False(string.IsNullOrWhiteSpace(diagnostics.NativeSqliteVersion));
        Assert.False(diagnostics.Quarantined);
        Assert.True(diagnostics.DatabaseBytes > 0);
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        Assert.Equal(db.Database.GetMigrations().Last(), diagnostics.SchemaVersion);
    }

    [Fact]
    public async Task EmptyTestProfile_InitializesAndReopensWithoutDefaultUsers()
    {
        using var fixture = new LocalStoreTestFixture(configureOptions: options => options.SeedSampleData = false);
        var first = await fixture.InitializeAsync();
        Assert.False(first.Quarantined);

        // Reopen the same isolated file through a fresh provider, never resetting any profile.
        using var reopened = fixture.BuildProvider();
        using var scope = reopened.CreateScope();
        var initializer = scope.ServiceProvider
            .GetRequiredService<Animora.Persistence.Sqlite.Maintenance.ILocalStoreInitializer>();
        var result = await initializer.InitializeAsync(default);
        Assert.True(result.IsSuccess, result.Detail);
        Assert.False(result.Value!.Quarantined);
        Assert.Equal(first.DatabasePath, result.Value.DatabasePath);
        var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.Empty(await db.Set<Animora.Domain.Identity.User>().ToListAsync());
        Assert.Empty(await db.Set<Animora.Domain.Patients.Owner>().ToListAsync());
    }

    [Fact]
    public async Task EncryptedStoreWithNoKey_FailsClosedAndCreatesNoDatabase()
    {
        using var fixture = new LocalStoreTestFixture(configureOptions: options =>
        {
            options.EncryptionEnabled = true;
            options.K2Approved = true;
            options.AllowInsecureKeyFallback = false; // Linux has no OS keystore -> UnavailableSecretStore.
        });

        var result = await fixture.TryInitializeAsync();

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCodes.StoreKeyUnavailable, result.Error);
        Assert.False(File.Exists(fixture.DatabasePath));
    }

    [Fact]
    public void EncryptionWithoutK2Approval_IsRefusedByValidate()
    {
        using var fixture = new LocalStoreTestFixture(configureOptions: options =>
        {
            options.EncryptionEnabled = true;
            options.K2Approved = false;
        });

        var result = fixture.Options.Validate();

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCodes.StoreEncryptionNotApproved, result.Error);
    }

    [Fact]
    public void TenantIsolation_PathsAreStableSafeAndServerScoped()
    {
        var root = Path.Combine(Path.GetTempPath(), "tenant-path-only");
        var a = new Animora.Infrastructure.Local.Storage.ActiveTenantStore(root, "https://server.test/api/", " clinic-A ");
        var repeated = new Animora.Infrastructure.Local.Storage.ActiveTenantStore(root, "https://server.test/api", "CLINIC-A");
        var b = new Animora.Infrastructure.Local.Storage.ActiveTenantStore(root, "https://server.test/api/", "clinic-B");
        var otherServer = new Animora.Infrastructure.Local.Storage.ActiveTenantStore(root, "https://other.test/api/", "clinic-A");
        var traversal = new Animora.Infrastructure.Local.Storage.ActiveTenantStore(root, "https://server.test/", "../../secret");
        Assert.Equal(a.DatabasePath, repeated.DatabasePath);
        Assert.NotEqual(a.DatabasePath, b.DatabasePath);
        Assert.NotEqual(a.DatabasePath, otherServer.DatabasePath);
        Assert.Matches("^[0-9a-f]{64}$", traversal.Slug);
        Assert.Equal(Path.Combine(root, "tenants", traversal.Slug), traversal.DataDirectory);
        var options = new Animora.Persistence.Sqlite.Options.LocalStoreOptions();
        var localPath = options.DatabasePath;
        a.ConfigureStore(options);
        Assert.NotEqual(localPath, options.DatabasePath);
        Assert.Equal(Path.Combine(a.DataDirectory, "backups"), options.BackupDirectory);
        Assert.False(options.SeedSampleData);
    }

    [Fact]
    public void TenantIsolation_BindingSurvivesProviderRecreationAndRejectsReassignedClinicCode()
    {
        var secrets = new MemorySecretStore();
        var first = new Animora.Infrastructure.Local.Storage.TenantSessionBinding(secrets);
        Assert.False(first.CanSync);
        Assert.True(first.Bind("SERVER-A").IsSuccess);
        first.Activate();
        Assert.True(first.CanSync);
        first.Suspend();
        Assert.False(first.CanSync);
        var reopened = new Animora.Infrastructure.Local.Storage.TenantSessionBinding(secrets);
        Assert.True(reopened.Matches("SERVER-A"));
        Assert.False(reopened.Matches("SERVER-B"));
        Assert.True(reopened.Bind("SERVER-B").IsFailure);
        Assert.True(reopened.Matches("SERVER-A")); // original binding/data are never erased on mismatch
    }

    [Fact]
    public async Task TenantIsolation_AtoBtoA_PreservesOwnersOutboxAndIndependentSecrets()
    {
        using var root = new LocalStoreTestFixture();
        var a = new Animora.Infrastructure.Local.Storage.ActiveTenantStore(root.Directory, "https://server.test/", "A");
        var b = new Animora.Infrastructure.Local.Storage.ActiveTenantStore(root.Directory, "https://server.test/", "B");
        using var clinicA = new LocalStoreTestFixture(configureOptions: a.ConfigureStore);
        using var clinicB = new LocalStoreTestFixture(configureOptions: b.ConfigureStore);
        await clinicA.InitializeAsync();
        await clinicB.InitializeAsync();
        Assert.NotSame(clinicA.Services.GetRequiredService<LocalInstallationState>(), clinicB.Services.GetRequiredService<LocalInstallationState>());
        var owner = Animora.Domain.Patients.Owner.Register(LocalStoreTestFixture.DefaultTenant, "Owner A", "09120000001", clinicA.Clock);
        using (var scope = clinicA.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            db.Add(owner);
            var envelope = Animora.Contracts.Wire.Commands.CommandEnvelope.Create("OFFLINE-A", LocalStoreTestFixture.DefaultTenant,
                "U-A", "D-A", "clinic.pet.update", "Pet", "P-A", "{}", "fp-A", clinicA.Clock.Now, 1);
            Assert.True((await scope.ServiceProvider.GetRequiredService<Animora.Application.Commands.ICommandJournal>().EnqueueAsync(envelope)).IsSuccess);
            await db.SaveChangesAsync();
        }
        using (var scope = clinicB.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            Assert.Empty(await db.Set<Animora.Domain.Patients.Owner>().ToListAsync());
            var journal = scope.ServiceProvider.GetRequiredService<Animora.Application.Commands.ICommandJournal>();
            Assert.Null((await journal.FindByOperationIdAsync("OFFLINE-A")).Value);
        }
        using var reopened = clinicA.BuildProvider();
        using (var scope = reopened.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            Assert.Equal(owner.Id, Assert.Single(await db.Set<Animora.Domain.Patients.Owner>().ToListAsync()).Id);
            Assert.NotNull((await scope.ServiceProvider.GetRequiredService<Animora.Application.Commands.ICommandJournal>()
                .FindByOperationIdAsync("OFFLINE-A")).Value);
        }
        var secretsA = new MemorySecretStore();
        var secretsB = new MemorySecretStore();
        var sessionA = new Animora.Infrastructure.Local.Identity.ProtectedSessionStore(
            clinicA.Services.GetRequiredService<IServiceScopeFactory>(), secretsA, clinicA.Clock,
            new Animora.Infrastructure.Local.Storage.TenantSessionBinding(secretsA));
        var saved = new Animora.Application.Security.ClientSession("access-A", clinicA.Clock.Now.AddHours(1), "refresh-A", "U-A", "SERVER-A", "D-A", "Manager", "stamp");
        Assert.True((await sessionA.WriteAsync(saved)).IsSuccess);
        var wrongKeys = new Animora.Infrastructure.Local.Identity.ProtectedSessionStore(
            clinicA.Services.GetRequiredService<IServiceScopeFactory>(), secretsB, clinicA.Clock);
        Assert.Null((await wrongKeys.ReadAsync()).Value);
        Assert.Equal("access-A", (await sessionA.ReadAsync()).Value!.AccessToken);
        Assert.True((await sessionA.WriteAsync(saved with { TenantId = "SERVER-B" })).IsFailure);
        await sessionA.ClearAsync();
        Assert.Null((await sessionA.ReadAsync()).Value);
        using var scopeA = clinicA.CreateScope();
        Assert.NotNull((await scopeA.ServiceProvider.GetRequiredService<Animora.Application.Commands.ICommandJournal>()
            .FindByOperationIdAsync("OFFLINE-A")).Value); // logout never deletes unsent work
    }

    [Fact]
    public async Task TenantIsolation_SameTimestampLoginReadsOnlyCurrentUser()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();
        var store = new Animora.Infrastructure.Local.Identity.ProtectedSessionStore(
            fixture.Services.GetRequiredService<IServiceScopeFactory>(), new MemorySecretStore(), fixture.Clock);
        var session = new Animora.Application.Security.ClientSession("old", fixture.Clock.Now.AddHours(1), "r", "old-user", "A", "D", "Doctor", "s");
        Assert.True((await store.WriteAsync(session)).IsSuccess);
        Assert.True((await store.WriteAsync(session with { UserId = "new-user", AccessToken = "new" })).IsSuccess);
        Assert.Equal("new-user", (await store.ReadAsync()).Value!.UserId);
    }

    private static string Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(command.ExecuteScalar()) ?? string.Empty;
    }
}
