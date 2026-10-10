using Animora.Domain.Time;
using Animora.Sync.Abstractions;
using Xunit;

namespace Animora.Sync.Tests;

/// <summary>Facts for the offline <see cref="NullSyncEngine"/>.</summary>
public sealed class NullSyncEngineTests
{
    private sealed class FakeClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }

    private static readonly DateTimeOffset Fixed = new(2026, 10, 10, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task Pull_ReturnsDeferredSuccess_NotConfigured()
    {
        var engine = new NullSyncEngine(new FakeClock(Fixed));
        Assert.False(engine.IsConfigured);

        var result = await engine.PullAsync(SyncScope.Default);

        Assert.True(result.IsSuccess);
        Assert.Equal(SyncOutcome.Deferred, result.Value!.Outcome);
    }

    [Fact]
    public async Task Pull_PreCancelledToken_Throws()
    {
        var engine = new NullSyncEngine(new FakeClock(Fixed));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => engine.PullAsync(SyncScope.Default, new CancellationToken(true)));
    }

    [Fact]
    public async Task Pull_UsesInjectedClock()
    {
        var engine = new NullSyncEngine(new FakeClock(Fixed));
        var result = await engine.PullAsync(SyncScope.Default);
        Assert.Equal(Fixed, result.Value!.CompletedAtUtc);
    }
}
