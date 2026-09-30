using System;
using System.Collections.Generic;
using System.Globalization;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Entities;
using Animora.Persistence.Sqlite.Seed;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Animora.Persistence.Tests;

/// <summary>
/// Migration <c>001_LocalCoreSchema</c>: applies cleanly from empty, re-applying is a no-op, every
/// core table exists, the named indexes from the A09 design decisions are present, and no column
/// leaks a <c>REAL</c>/<c>NUMERIC</c> type (money and time are exact).
/// </summary>
public sealed class LocalMigrationTests
{
    private static readonly string[] CoreTables =
    [
        "Appointments", "BoardingServiceDefs", "BoardingStays", "Devices", "DocumentReferences",
        "MedicalHistoryEntries", "Owners", "Permissions", "Pets", "ReceptionQueueEntries",
        "RoleDefinitions", "Rooms", "StaffMemberships", "Tenants", "Users", "VaccinationRecords",
        "Visits", "WeightEntries",
    ];

    private static readonly string[] CoreIndexes =
    [
        "IX_Appointments_TenantId_StartUtc_DoctorUserId",
        "IX_Owners_TenantId_Phone",
        "IX_Pets_TenantId_OwnerId",
        "IX_Users_TenantId_Username",
        "IX_Rooms_TenantId_Code",
        "IX_Visits_TenantId_State_StartedUtc",
    ];

    [Fact]
    public async Task AppliesFromEmpty_AndReapplyingIsANoOp()
    {
        using var fixture = new LocalStoreTestFixture();

        await fixture.MigrateAsync();

        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Animora.Persistence.Sqlite.Context.AnimoraLocalDbContext>();
        var applied = await db.Database.GetAppliedMigrationsAsync();
        Assert.NotEmpty(applied);
        Assert.Equal(db.Database.GetMigrations(), applied);

        // Re-applying is a no-op: no new migration, no error.
        await db.Database.MigrateAsync();
        var again = await db.Database.GetAppliedMigrationsAsync();
        Assert.Equal(applied.OrderBy(x => x), again.OrderBy(x => x));
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("fa-IR")]
    [InlineData("tr-TR")]
    public void ModelMatchesSnapshot_InEachCulture(string cultureName)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            using var fixture = new LocalStoreTestFixture();
            // Do not reuse a model cached by an earlier culture/provider.
            var options = new DbContextOptionsBuilder<AnimoraLocalDbContext>()
                .UseSqlite($"Data Source={fixture.DatabasePath}")
                .EnableServiceProviderCaching(false).Options;
            using var db = new AnimoraLocalDbContext(options,
                new FixedTenantContext(fixture.TenantId), fixture.Clock);
            Assert.False(db.Database.HasPendingModelChanges());
            Assert.False(System.IO.File.Exists(fixture.DatabasePath)); // Model-only check never opens storage.
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public async Task UpgradeFromPreviousMigration_PreservesExistingRows()
    {
        using var fixture = new LocalStoreTestFixture();
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        var migrations = db.Database.GetMigrations().ToArray();
        Assert.True(migrations.Length >= 2);
        await db.GetService<IMigrator>().MigrateAsync(migrations[^2]);
        var marker = SeedMarker.Create("regression.preserve", 1, fixture.Clock.UtcNow);
        db.Add(marker);
        var ownerSnapshot = OwnerFinancialSnapshot.Create(fixture.TenantId, "regression-owner");
        db.Add(ownerSnapshot);
        db.Entry(ownerSnapshot).Property<long>("OpenBalanceMinor").CurrentValue = 12345;
        await db.SaveChangesAsync();

        await db.Database.MigrateAsync();
        db.ChangeTracker.Clear();
        var saved = await db.Set<SeedMarker>().SingleAsync(e => e.SeedKey == "regression.preserve");
        Assert.Equal(marker.Id, saved.Id);
        Assert.Equal(marker.SchemaVersion, saved.SchemaVersion);
        Assert.Equal(marker.CreatedAtUtc, saved.CreatedAtUtc);
        var savedSnapshot = await db.Set<OwnerFinancialSnapshot>().AsTracking()
            .SingleAsync(e => e.Id == ownerSnapshot.Id);
        Assert.Equal(12345L, db.Entry(savedSnapshot).Property<long>("OpenBalanceMinor").CurrentValue);
        Assert.Null(typeof(OwnerFinancialSnapshot).GetProperty("OpenBalanceMinor"));
        Assert.Equal(migrations, await db.Database.GetAppliedMigrationsAsync());
    }

    [Fact]
    public async Task EveryCoreTableExists()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();

        var tables = await QueryStrings(fixture, "SELECT name FROM sqlite_master WHERE type='table';");
        foreach (var table in CoreTables)
        {
            Assert.Contains(table, tables);
        }
    }

    [Fact]
    public async Task NamedCoreIndexesExist()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();

        var indexes = await QueryStrings(fixture, "SELECT name FROM sqlite_master WHERE type='index';");
        foreach (var index in CoreIndexes)
        {
            Assert.Contains(index, indexes);
        }
    }

    [Fact]
    public async Task NoColumnUsesRealOrNumericType()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();

        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Animora.Persistence.Sqlite.Context.AnimoraLocalDbContext>();
        var connection = (SqliteConnection)db.Database.GetDbConnection();
        await connection.OpenAsync();

        var tables = await QueryStrings(fixture, "SELECT name FROM sqlite_master WHERE type='table';");
        foreach (var table in tables.Where(t => !t.StartsWith("sqlite_", StringComparison.Ordinal)))
        {
            var types = await ColumnTypes(connection, table);
            foreach (var (column, type) in types)
            {
                Assert.False(
                    type.Contains("REAL", StringComparison.OrdinalIgnoreCase)
                    || type.Contains("NUMERIC", StringComparison.OrdinalIgnoreCase),
                    $"{table}.{column} leaked type '{type}'.");
            }
        }

        await connection.CloseAsync();
    }

    private static async Task<IReadOnlyList<string>> QueryStrings(LocalStoreTestFixture fixture, string sql)
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Animora.Persistence.Sqlite.Context.AnimoraLocalDbContext>();
        var connection = (SqliteConnection)db.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var rows = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(reader.GetString(0));
        }

        await connection.CloseAsync();
        return rows;
    }

    private static async Task<IReadOnlyList<(string Column, string Type)>> ColumnTypes(SqliteConnection connection, string table)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info(\"{table}\");";
        var rows = new List<(string, string)>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add((reader.GetString(1), reader.IsDBNull(2) ? string.Empty : reader.GetString(2)));
        }

        return rows;
    }
}
