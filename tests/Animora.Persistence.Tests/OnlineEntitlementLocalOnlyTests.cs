using System.Linq;
using System.Threading.Tasks;
using Animora.Domain.Primitives;
using Animora.Persistence.Sqlite.Conventions;
using Animora.Persistence.Sqlite.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Animora.Persistence.Tests;

/// <summary>G06: <c>ClientSessions</c> / <c>SealedEntitlements</c> are [LocalOnly] — never synced, never carrying Version/Deleted — and are created by the migration.</summary>
public sealed class OnlineEntitlementLocalOnlyTests
{
    [Theory]
    [InlineData(typeof(ClientSessionRow), "ClientSessions")]
    [InlineData(typeof(SealedEntitlementRow), "SealedEntitlements")]
    public async Task OnlineEntitlementTables_AreLocalOnly_NotSyncable_AndHaveNoVersionOrDeleted(System.Type clr, string table)
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();
        using var db = fixture.CreateContext(fixture.TenantId);

        var entity = db.Model.FindEntityType(clr);
        Assert.NotNull(entity);
        Assert.Equal(table, entity!.GetTableName());
        Assert.True(clr.IsDefined(typeof(LocalOnlyAttribute), inherit: false));
        Assert.False(typeof(SyncableEntity).IsAssignableFrom(clr), "a local-only table must not be replicated");
        Assert.Contains(clr.Name, ModelConventions.LocalOnlyEntityTypes);

        var columns = entity.GetProperties().Select(p => p.Name).ToList();
        Assert.DoesNotContain("Version", columns);
        Assert.DoesNotContain("Deleted", columns);
        Assert.DoesNotContain("IsDeleted", columns);

        // The migration really created the table.
        var exists = await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS Value FROM sqlite_master WHERE type = 'table' AND name = {table}").SingleAsync();
        Assert.Equal(1, exists);
    }

    [Fact]
    public async Task ClientSessions_AreUniquePerTenantUserDevice()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();
        using var db = fixture.CreateContext(fixture.TenantId);

        var entity = db.Model.FindEntityType(typeof(ClientSessionRow))!;
        var unique = entity.GetIndexes().Single(i => i.IsUnique);

        Assert.Equal(["TenantId", "UserId", "DeviceId"], unique.Properties.Select(p => p.Name).ToArray());
        Assert.Equal(["TenantId"], db.Model.FindEntityType(typeof(SealedEntitlementRow))!.FindPrimaryKey()!.Properties.Select(p => p.Name).ToArray());
    }
}
