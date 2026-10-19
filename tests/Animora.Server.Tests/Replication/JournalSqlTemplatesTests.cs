using Animora.Persistence.PostgreSql.Migrations.Server;
using Animora.Persistence.PostgreSql.Replication;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;
namespace Animora.Server.Tests.Replication;

public sealed class JournalSqlTemplatesTests
{
    [Fact]
    public void DeferredTrigger_IsDeferrable_InitiallyDeferred() =>
        Assert.Contains("DEFERRABLE INITIALLY DEFERRED", JournalSqlTemplates.DeferredTrigger);

    [Fact]
    public void Migration120_UpgradesEveryChildTrigger_WithoutDeferringFeedAssignmentEarly()
    {
        var migration = new _120_AggregateChildConcurrency();
        var up = string.Join("\n", migration.UpOperations.OfType<SqlOperation>().Select(x => x.Sql));
        var down = string.Join("\n", migration.DownOperations.OfType<SqlOperation>().Select(x => x.Sql));
        foreach (var descriptor in SyncContractCatalog.ProjectionOrder)
        foreach (var child in descriptor.Children)
        {
            Assert.Contains("CREATE CONSTRAINT TRIGGER \"trg_" + child.Table + "_journal\"", up);
            Assert.Contains("CREATE TRIGGER \"trg_" + child.Table + "_journal\"", down);
        }
        Assert.Contains("DEFERRABLE INITIALLY IMMEDIATE", up);
        Assert.DoesNotContain("TenantJournal_assign", up);
        Assert.DoesNotContain("INITIALLY DEFERRED", up);
    }

}
public sealed class JournalCoverageTests
{
    [Fact]
    public void EveryReplicatedTable_HasCaptureTrigger_InMigration()
    {
        var sql = string.Join("\n", new _109_ChangeJournal().UpOperations.OfType<SqlOperation>().Select(x => x.Sql));
        foreach (var d in SyncContractCatalog.ProjectionOrder)
            foreach (var table in d.ChildTables.Append(d.Table))
                Assert.Contains("trg_" + table + "_journal", sql);
    }
}
