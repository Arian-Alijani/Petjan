using System.Data;
using Animora.Persistence.PostgreSql.Options;
using Animora.Persistence.PostgreSql.Replication;
using Animora.Server.Observability;
using Animora.Server.Workers;
using Animora.Server.Tests.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;
namespace Animora.Server.Tests.Observability;

public sealed class SnapshotAgeMonitorTests(PgFixture pg) : IClassFixture<PgFixture>
{
    [PgFact] public async Task OldSnapshot_ReportsDegraded()
    {
        await using(var db=TestSupport.CreateContext(connection:pg.ConnectionString)) await db.Database.MigrateAsync();
        await using var source=NpgsqlDataSource.Create(pg.ConnectionString);
        await using var connection=await source.OpenConnectionAsync(); await using var tx=await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead);
        await using(var set=new NpgsqlCommand("SET LOCAL application_name='Animora.Server/feed'; SELECT 1",connection,tx)) await set.ExecuteNonQueryAsync();
        await Task.Delay(20); using var metrics=new ServerMetrics(); using var admission=new SemaphoreBackgroundAdmission();
        using var monitor=new SnapshotAgeMonitor(new ServerDatabaseStatisticsReader(source),metrics,Options.Create(new FeedStorageOptions { MaxSnapshotLifetime=TimeSpan.FromTicks(1) }),admission,
            TimeProvider.System,new WorkerHeartbeat(TimeProvider.System),NullLogger<SnapshotAgeMonitor>.Instance);
        await monitor.RunOnceAsync(); Assert.False((await monitor.CheckAsync(default)).IsHealthy); Assert.True(metrics.SnapshotAge>0);
    }
}
