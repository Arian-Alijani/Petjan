using Animora.Domain.Results;
using Animora.Sync.Abstractions;
using Xunit;
using static Animora.Sync.Tests.ReplicaTestSupport;

namespace Animora.Sync.Tests;

/// <summary>G04: the atomic base+checkpoint apply over a real file SQLite database.</summary>
public sealed class SqliteSyncApplyStoreTests
{
    [Fact]
    public async Task ApplyBatch_AdvancesBaseAndCheckpoint_Atomically()
    {
        using var fixture = await NewFixtureAsync();
        var store = NewStore(fixture);

        var result = await ApplyAsync(store, Batch("b1", 2, 0, 10),
            Page("b1", 0, 2, Rec("Pet", "p1", "v1", "rex"), Rec("Pet", "p2", "v1", "tom")),
            Page("b1", 1, 2, Rec("Owner", "o1", "v1", "ali")));

        Assert.True(result.IsSuccess, result.Detail);
        Assert.Equal((Epoch, 10L, 1L), await CheckpointAsync(fixture));
        Assert.Equal(3, (await BaseRowsAsync(fixture)).Count);
        Assert.Equal(0, await StagingCountAsync(fixture));
        var read = await store.ReadCheckpointAsync(SyncScope.Default);
        Assert.Equal(new Animora.Sync.Custom.SyncCheckpoint(Epoch, 10), read.Value);

        // A delta then upserts one row, tombstones another and advances the watermark on the same generation.
        var delta = await ApplyAsync(store, Batch("b2", 1, 10, 25),
            Page("b2", 0, 1, Rec("Pet", "p1", "v2", "rex2"), Rec("Pet", "p2", "v2", "", 'D')));
        Assert.True(delta.IsSuccess, delta.Detail);
        Assert.Equal((Epoch, 25L, 1L), await CheckpointAsync(fixture));
        var rows = await BaseRowsAsync(fixture);
        Assert.Equal(3, rows.Count);
        Assert.Equal("v2", rows.Single(r => r.EntityId == "p1").Version);
        var tomb = rows.Single(r => r.EntityId == "p2");
        Assert.True(tomb.Deleted);
        Assert.Null(tomb.Payload);
    }

    [Fact]
    public async Task CrashBeforeCommit_LeavesBaseAndCheckpointUnchanged()
    {
        using var fixture = await NewFixtureAsync();
        var store = NewStore(fixture);
        Assert.True((await ApplyAsync(store, Batch("b1", 1, 0, 10), Page("b1", 0, 1, Rec("Pet", "p1", "v1", "rex")))).IsSuccess);

        // Pages are staged, then the session is dropped without COMMIT (a crash/cancel).
        var begun = await store.BeginAsync(SyncScope.Default, Batch("b2", 1, 10, 30));
        var session = begun.Value!;
        Assert.True((await session.AcceptPageAsync(Page("b2", 0, 1, Rec("Pet", "p1", "v9", "changed")))).IsSuccess);
        Assert.Equal(1, await StagingCountAsync(fixture));
        await session.DisposeAsync();

        Assert.Equal((Epoch, 10L, 1L), await CheckpointAsync(fixture));
        var row = Assert.Single(await BaseRowsAsync(fixture));
        Assert.Equal("v1", row.Version);
        Assert.Equal(0, await StagingCountAsync(fixture));
    }

    [Fact]
    public async Task PartialPage_NeverAdvancesCheckpoint()
    {
        using var fixture = await NewFixtureAsync();
        var store = NewStore(fixture);

        var begun = await store.BeginAsync(SyncScope.Default, Batch("b1", 2, 0, 10));
        await using var session = begun.Value!;
        Assert.True((await session.AcceptPageAsync(Page("b1", 0, 2, Rec("Pet", "p1", "v1", "rex")))).IsSuccess);

        var outOfOrder = await session.AcceptPageAsync(Page("b1", 5, 2, Rec("Pet", "p2", "v1", "tom")));
        Assert.Equal(ErrorCodes.SyncProtocolViolation, outOfOrder.Error);

        var commit = await session.CommitAsync(); // only 1 of 2 pages accepted
        Assert.Equal(ErrorCodes.SyncProtocolViolation, commit.Error);
        Assert.Equal((0L, 0L, 0L), await CheckpointAsync(fixture));
        Assert.Empty(await BaseRowsAsync(fixture));
    }

    [Fact]
    public async Task DeltaThatDoesNotContinueCheckpoint_IsRejected_NothingApplied()
    {
        using var fixture = await NewFixtureAsync();
        var store = NewStore(fixture);
        Assert.True((await ApplyAsync(store, Batch("b1", 1, 0, 10), Page("b1", 0, 1, Rec("Pet", "p1", "v1", "rex")))).IsSuccess);

        var gap = await ApplyAsync(store, Batch("b2", 1, 50, 60), Page("b2", 0, 1, Rec("Pet", "p9", "v1", "gap")));

        Assert.Equal(ErrorCodes.SyncApplyConflict, gap.Error);
        Assert.Equal((Epoch, 10L, 1L), await CheckpointAsync(fixture));
        Assert.Single(await BaseRowsAsync(fixture));
    }

    [Fact]
    public async Task Pull_DoesNotTouchPendingOverlay()
    {
        using var fixture = await NewFixtureAsync();
        var operationId = await EnqueueOverlayAsync(fixture);
        var before = await OverlayAsync(fixture);
        var store = NewStore(fixture);

        var result = await ApplyAsync(store, Batch("b1", 1, 0, 10), Page("b1", 0, 1, Rec("Invoice", "INV-1", "v1", "server")));
        Assert.True(result.IsSuccess, result.Detail);

        var after = await OverlayAsync(fixture);
        Assert.Equal(before, after);
        Assert.Equal(operationId, Assert.Single(after).Id);
    }
}
