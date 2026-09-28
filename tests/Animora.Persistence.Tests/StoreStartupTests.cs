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
        Assert.Contains("014", diagnostics.SchemaVersion); // latest migration = 014_SalesInsurers.
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

    private static string Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(command.ExecuteScalar()) ?? string.Empty;
    }
}
