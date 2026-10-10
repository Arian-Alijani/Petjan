using Animora.Persistence.PostgreSql.Replication;
using Animora.Server.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;
namespace Animora.Server.Tests.Sync;

public sealed class BatchFreezerTests(PgFixture pg) : IClassFixture<PgFixture>
{
    [PgFact]
    public async Task WatermarkAndRows_FromOneSnapshot()
    {
        await using var rig = new SyncPgRig(pg.ConnectionString); await rig.Initialize();
        var result = await rig.Freezer.OpenAsync(new(1, 0), false, CancellationToken.None);
        Assert.NotNull(result.Batch); Assert.Equal(1, result.Batch!.Watermark);
        Assert.Single(await rig.Download(result.Batch), r => r.Entity == "Tenant");
    }
    [PgFact]
    public async Task SnapshotCommitted_BeforePageMaterialization()
    {
        await using var rig = new SyncPgRig(pg.ConnectionString); await rig.Initialize();
        var result = await rig.Freezer.OpenAsync(new(1, 0), false, CancellationToken.None);
        await using var c = await rig.Source.OpenConnectionAsync();
        await using var command = new Npgsql.NpgsqlCommand("SELECT count(*) FROM pg_stat_activity WHERE datname=current_database() AND state='idle in transaction'", c);
        Assert.Equal(0L, await command.ExecuteScalarAsync()); Assert.NotNull(result.Batch);
    }
    [PgFact]
    public async Task SameRange_SameBatchId_ByteStable()
    {
        await using var rig = new SyncPgRig(pg.ConnectionString); await rig.Initialize();
        var first = (await rig.Freezer.OpenAsync(new(1, 0), false, CancellationToken.None)).Batch!;
        var again = (await rig.Freezer.OpenAsync(new(1, 0), false, CancellationToken.None)).Batch!;
        Assert.Equal(first, again);
        Assert.Equal(first.ContentHash, again.ContentHash);
    }
    [PgFact]
    public async Task SnapshotOverMaxLifetime_AbortsAndRetries()
    {
        await using var rig = new SyncPgRig(pg.ConnectionString); await rig.Initialize();
        rig.Settings.MaxSnapshotLifetime = TimeSpan.FromTicks(1);
        await Assert.ThrowsAsync<SnapshotLifetimeExceededException>(() => rig.Freezer.OpenAsync(new(1, 0), false, CancellationToken.None));
        Assert.Empty(Directory.EnumerateDirectories(Path.Combine(rig.Writer.FeedRoot, "_staging")));
    }
}
public sealed class GcTests(PgFixture pg) : IClassFixture<PgFixture>
{
    [PgFact]
    public async Task JournalDeletion_AdvancesFloor_SameTransaction()
    {
        await using var rig = new SyncPgRig(pg.ConnectionString); await rig.Initialize();
        await using var db = rig.Factory.Create();
        await db.Set<TenantJournalRow>().ExecuteUpdateAsync(s => s.SetProperty(j => j.CapturedUtc, DateTimeOffset.UtcNow.AddDays(-121)));
        await new FeedRetentionService(rig.Factory, rig.Writer, rig.Admission).RunAsync(CancellationToken.None);
        Assert.Empty(await db.Set<TenantJournalRow>().ToArrayAsync());
        Assert.Equal(1, (await db.Set<TenantFeedStateRow>().SingleAsync()).OldestRetainedSequence);
    }
    [PgFact]
    public async Task CrashAfterFileDelete_BeforeRowDelete_Recovers()
    {
        await using var rig = new SyncPgRig(pg.ConnectionString); await rig.Initialize();
        var batch = (await rig.Freezer.OpenAsync(new(1, 0), false, CancellationToken.None)).Batch!;
        await using var db = rig.Factory.Create();
        await db.Set<FeedBatchRow>().ExecuteUpdateAsync(s => s.SetProperty(b => b.State, FeedBatchState.Retired).SetProperty(b => b.RetiredUtc, DateTimeOffset.UtcNow));
        Directory.Delete(rig.Writer.BatchDirectory(rig.Tenant, batch.BatchId), true);
        var gc = new FeedRetentionService(rig.Factory, rig.Writer, rig.Admission);
        await gc.RunAsync(CancellationToken.None); await gc.RunAsync(CancellationToken.None);
        Assert.Empty(await db.Set<FeedBatchRow>().ToArrayAsync()); Assert.Empty(await db.Set<FeedBatchPageRow>().ToArrayAsync());
    }
}
public sealed class SyncIsolationTests(PgFixture pg) : IClassFixture<PgFixture>
{
    [PgFact]
    public async Task TenantA_Batch_ContainsNoTenantB_Rows()
    {
        await using var a = new SyncPgRig(pg.ConnectionString); await a.Initialize();
        await using var b = new SyncPgRig(pg.ConnectionString); await b.Initialize();
        var batch = (await a.Freezer.OpenAsync(new(0, 0), true, CancellationToken.None)).Batch!;
        var records = await a.Download(batch);
        Assert.Contains(records, r => r.EntityId == a.Tenant); Assert.DoesNotContain(records, r => r.EntityId == b.Tenant);
        Assert.Null(await new FeedPageReader(b.Factory, b.Writer).OpenAsync(batch.BatchId, 0, CancellationToken.None));
    }
}
public sealed class SyncFlowTests(PgFixture pg) : IClassFixture<PgFixture>
{
    [PgFact]
    public async Task OpenDownloadResume_FromLastPage()
    {
        await using var rig = new SyncPgRig(pg.ConnectionString); await rig.Initialize();
        var batch = (await rig.Freezer.OpenAsync(new(0, 0), true, CancellationToken.None)).Batch!;
        var first = await rig.Download(batch); var again = await rig.Download(batch);
        Assert.Equal(first.Select(r => r.EntityId), again.Select(r => r.EntityId));
        Assert.Equal("UpToDate", (await rig.Freezer.OpenAsync(new(batch.Epoch, batch.Watermark), false, CancellationToken.None)).Kind);
    }
}
