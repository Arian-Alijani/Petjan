using Animora.Contracts.Wire.Sync;
using Animora.Persistence.PostgreSql.Replication;
using Xunit;
namespace Animora.Server.Tests.Sync;

public sealed class ContractsTests
{
    [Fact]
    public void NoSyncContract_HasTenantIdProperty()
    {
        foreach (var type in typeof(SyncRecord).Assembly.GetTypes().Where(t => t.Namespace?.StartsWith("Animora.Contracts.Wire.Sync", StringComparison.Ordinal) == true))
            Assert.DoesNotContain(type.GetProperties(), p => p.Name == "TenantId");
    }
    [Fact]
    public void EveryCatalogEntry_HasExactlyOneSyncContract()
    {
        var types = typeof(SyncRecord).Assembly.GetTypes().Where(t => t.Name.EndsWith("SyncContract", StringComparison.Ordinal)).ToArray();
        Assert.Equal(SyncContractCatalog.ProjectionOrder.Count, types.Length);
        foreach (var entry in SyncContractCatalog.ProjectionOrder) Assert.Single(types, t => t.Name == entry.Name + "SyncContract");
    }
}
