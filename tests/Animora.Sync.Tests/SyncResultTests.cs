using Animora.Sync.Abstractions;
using Xunit;

namespace Animora.Sync.Tests;

/// <summary>Facts for the <see cref="SyncResult"/> factories.</summary>
public sealed class SyncResultTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Factories_SetOutcomeAndWatermark()
    {
        var upToDate = SyncResult.UpToDate(42, Now);
        Assert.Equal(SyncOutcome.UpToDate, upToDate.Outcome);
        Assert.Equal(42, upToDate.Watermark);

        var applied = SyncResult.Applied(99, 2, 17, Now);
        Assert.Equal(SyncOutcome.Applied, applied.Outcome);
        Assert.Equal(99, applied.Watermark);
        Assert.Equal(2, applied.BatchesApplied);
        Assert.Equal(17, applied.RecordsApplied);

        var resync = SyncResult.ResyncRequired("EPOCH_CHANGED", Now);
        Assert.Equal(SyncOutcome.ResyncRequired, resync.Outcome);
        Assert.Equal("EPOCH_CHANGED", resync.Detail);
    }

    [Fact]
    public void Deferred_HasNoWatermark()
    {
        var deferred = SyncResult.Deferred(Now, "offline");
        Assert.Equal(SyncOutcome.Deferred, deferred.Outcome);
        Assert.Null(deferred.Watermark);
        Assert.Equal(Now, deferred.CompletedAtUtc);
    }
}
