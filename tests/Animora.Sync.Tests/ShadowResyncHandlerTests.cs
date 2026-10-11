using Animora.Contracts.Wire.Sync;
using Animora.Domain.Results;
using Animora.Sync.Abstractions;
using Animora.Sync.Custom;
using Xunit;
using static Animora.Sync.Tests.ReplicaTestSupport;

namespace Animora.Sync.Tests;

/// <summary>G04: full-resync shadow generation — build, validate, atomic switch; the live state survives any failure.</summary>
public sealed class ShadowResyncHandlerTests
{
    private static FakeFeedHandler Feed(BatchReady batch, SyncPage[] pages, long epoch = Epoch)
    {
        var gz = pages.Select(FeedFixtures.Gzip).ToArray();
        return new FakeFeedHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/sync/manifest") return FeedFixtures.JsonResponse(new SyncManifest(2, epoch, 1, ["Pet"], 262144));
            if (path == "/sync/batches") return FeedFixtures.JsonResponse(new OpenBatchResponse("BatchReady", Batch: batch));
            return FeedFixtures.PageResponse(gz[int.Parse(path[(path.LastIndexOf('/') + 1)..])]);
        });
    }

    private static string ContentHash(SyncPage[] pages) =>
        PageIntegrity.ComputeContentHash(pages.Select(p => PageIntegrity.ComputePageHash(FeedFixtures.Gzip(p))).ToList());

    private static ShadowResyncHandler Handler(FakeFeedHandler feed, Animora.Sync.Custom.Replica.SqliteSyncApplyStore store) =>
        new(FeedFixtures.Client(feed), store, FeedFixtures.Options());

    [Fact]
    public async Task BuildsShadow_PreservesOverlay_SwitchesAtomically()
    {
        using var fixture = await NewFixtureAsync();
        var store = NewStore(fixture);
        Assert.True((await ApplyAsync(store, Batch("old", 1, 0, 10), Page("old", 0, 1, Rec("Pet", "stale", "v1", "old")))).IsSuccess);
        await EnqueueOverlayAsync(fixture);
        var overlayBefore = await OverlayAsync(fixture);

        SyncPage[] pages = [Page("snap", 0, 2, Rec("Pet", "p1", "v5", "a")), Page("snap", 1, 2, Rec("Pet", "p2", "v5", "b"))];
        var batch = new BatchReady("snap", Epoch + 1, 2, 0, 99, 2, ContentHash(pages));
        var handler = Handler(Feed(batch, pages, Epoch + 1), store);

        var result = await handler.HandleAsync(SyncScope.Default, "EPOCH_CHANGED");

        Assert.True(result.IsSuccess, result.Detail);
        Assert.Equal((Epoch + 1, 99L, 2L), await CheckpointAsync(fixture));
        var rows = await BaseRowsAsync(fixture);
        Assert.Equal(["p1", "p2"], rows.Select(r => r.EntityId).ToArray());
        Assert.All(rows, r => Assert.Equal(2, r.Generation)); // the old generation is gone only after the switch
        Assert.Equal(overlayBefore, await OverlayAsync(fixture));
        Assert.Equal(0, await StagingCountAsync(fixture));
    }

    [Fact]
    public async Task ShadowValidationFailure_LeavesLiveStateUntouched()
    {
        using var fixture = await NewFixtureAsync();
        var store = NewStore(fixture);
        Assert.True((await ApplyAsync(store, Batch("old", 1, 0, 10), Page("old", 0, 1, Rec("Pet", "live", "v1", "live")))).IsSuccess);
        await EnqueueOverlayAsync(fixture);
        var overlayBefore = await OverlayAsync(fixture);

        SyncPage[] pages = [Page("snap", 0, 1, Rec("Pet", "p1", "v5", "a"))];
        var badHash = new BatchReady("snap", Epoch, 2, 0, 99, 1, new string('0', 64));
        var handler = Handler(Feed(badHash, pages), store);

        var result = await handler.HandleAsync(SyncScope.Default, "RESYNC");

        Assert.Equal(ErrorCodes.SyncShadowValidationFailed, result.Error);
        Assert.Equal((Epoch, 10L, 1L), await CheckpointAsync(fixture));
        var row = Assert.Single(await BaseRowsAsync(fixture));
        Assert.Equal("live", row.EntityId);
        Assert.Equal(overlayBefore, await OverlayAsync(fixture));
        Assert.Equal(0, await StagingCountAsync(fixture));
    }

    [Fact]
    public async Task NonSnapshotBatch_IsRejected()
    {
        using var fixture = await NewFixtureAsync();
        var store = NewStore(fixture);
        SyncPage[] pages = [Page("d", 0, 1, Rec("Pet", "p1", "v5", "a"))];
        var delta = new BatchReady("d", Epoch, 1, 5, 99, 1, ContentHash(pages));

        var result = await Handler(Feed(delta, pages), store).HandleAsync(SyncScope.Default, "RESYNC");

        Assert.Equal(ErrorCodes.SyncShadowValidationFailed, result.Error);
        Assert.Equal((0L, 0L, 0L), await CheckpointAsync(fixture));
    }
}
