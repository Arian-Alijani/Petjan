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
