using Animora.Persistence.PostgreSql.Replication;
using Animora.Server.Tests.Persistence;
using Xunit;
namespace Animora.Server.Tests.Sync;

/// <summary>
/// BUG 2 verification (plan T6): opening a sync batch must return a 200-shaped
/// <see cref="Animora.Contracts.Wire.Sync.OpenBatchResponse"/> — never surface the
/// unhandled exception that produced the old <c>500 UNEXPECTED</c>.
/// Covers both a populated tenant (journal rows present → <c>BatchReady</c>) and an
/// already-current cursor (→ <c>UpToDate</c>), exercising the projector null-guard (T4)
/// and the <c>Decide</c> short-circuit on the healthy path.
/// </summary>
public sealed class SyncBatchesResilienceTests(PgFixture pg) : IClassFixture<PgFixture>
{
    [PgFact]
    public async Task PopulatedTenant_OpenBatch_ReturnsBatchReady_NotFault()
    {
        await using var rig = new SyncPgRig(pg.ConnectionString);
        await rig.Initialize(); // seeds a Tenant row → journal sequence advances to 1

        // {epoch:1, fromSequence:0} is the exact request that used to 500.
        var result = await rig.Freezer.OpenAsync(new(1, 0), false, CancellationToken.None);

        Assert.Equal("BatchReady", result.Kind);
        Assert.NotNull(result.Batch);
        Assert.True(result.Batch!.Watermark >= 1);
        Assert.True(result.Batch.PageCount >= 1);

        // The null-guarded projector must still materialize the seeded Tenant row,
        // proving TryGetProperty(Version/UpdatedAt/Deleted) does not drop valid rows.
        var records = await rig.Download(result.Batch);
        Assert.Contains(records, r => r.Entity == "Tenant" && r.EntityId == rig.Tenant);
    }

    [PgFact]
    public async Task CursorAtWatermark_OpenBatch_ReturnsUpToDate_NotFault()
    {
        await using var rig = new SyncPgRig(pg.ConnectionString);
        await rig.Initialize();

        // Discover the current watermark, then re-open from exactly there.
        var opened = await rig.Freezer.OpenAsync(new(1, 0), false, CancellationToken.None);
        var watermark = opened.Batch!.Watermark;

        var current = await rig.Freezer.OpenAsync(new(opened.Batch.Epoch, watermark), false, CancellationToken.None);

        Assert.Equal("UpToDate", current.Kind);
        Assert.NotNull(current.Current);
        Assert.Equal(watermark, current.Current!.Watermark);
    }
}
