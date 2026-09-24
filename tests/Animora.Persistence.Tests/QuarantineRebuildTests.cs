using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Maintenance;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Animora.Persistence.Tests;

/// <summary>
/// A11c quarantine: a header-corrupted store is moved (never deleted) together with its
/// <c>-wal</c>/<c>-shm</c> into <c>quarantine/&lt;stamp&gt;/</c> and a fresh migrated + seeded store
/// is built in its place.
/// </summary>
public sealed class QuarantineRebuildTests
{
    private static void CorruptHeader(string path)
    {
        SqliteConnection.ClearAllPools();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None);
        var garbage = new byte[64];
        Array.Fill(garbage, (byte)0xAB);
        stream.Write(garbage);
        stream.Flush(true);
    }

    [Fact]
    public async Task CorruptStore_IsQuarantinedNotDeleted_AndAFreshStoreIsBuilt()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.InitializeAsync();
        var original = fixture.DatabasePath;
        Assert.True(File.Exists(original));

        CorruptHeader(original);

        using (var scope = fixture.CreateScope())
        {
            var maintenance = scope.ServiceProvider.GetRequiredService<ILocalStoreMaintenance>();
            var result = await maintenance.QuarantineAndRebuildAsync(default);
            Assert.True(result.IsSuccess, result.Detail);
            Assert.True(result.Value!.Quarantined);
        }

        // The original files were moved, never deleted: the quarantine folder holds them.
        var quarantineRoot = Path.Combine(fixture.Options.DataDirectory, "quarantine");
        var quarantined = Directory.GetFiles(quarantineRoot, "*", SearchOption.AllDirectories);
        Assert.Contains(quarantined, f => string.Equals(Path.GetFileName(f), "animora.db", StringComparison.Ordinal));

        // A fresh, migrated + seeded database exists in its place.
        Assert.True(File.Exists(original));
        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            Assert.True(await db.Set<Animora.Persistence.Sqlite.Seed.SeedMarker>().AnyAsync());
        }
    }
}
