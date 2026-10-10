using Animora.Server.Resilience;
using Xunit;
namespace Animora.Server.Tests.Resilience;

internal sealed class MutableTimeProvider : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 10, 10, 0, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Now;
    public void Advance(TimeSpan elapsed) => Now += elapsed;
}
public sealed class ServerRetryPolicyTests
{
    [Fact]
    public void NextDelay_CappedExponential_FullJitter_Bounds()
    {
        for (int attempt = 1; attempt < 40; attempt++)
        {
            double cap = Math.Min(900, 2 * Math.Pow(2, attempt - 1));
            Assert.Equal(TimeSpan.Zero, ServerRetryPolicy.NextDelay(attempt, () => 0));
            Assert.Equal(TimeSpan.FromSeconds(cap * .5), ServerRetryPolicy.NextDelay(attempt, () => .5));
            Assert.InRange(ServerRetryPolicy.NextDelay(attempt, () => .999999).TotalSeconds, 0, cap);
        }
        Assert.Equal(8, ServerRetryPolicy.MaxAttempts);
    }
}
public sealed class CircuitBreakerTests
{
    [Fact]
    public void OpensAfter5In60s_HalfOpenSingleProbe_Closes()
    {
        var time = new MutableTimeProvider(); var breaker = new CircuitBreaker(time);
        for (int i = 0; i < 5; i++) { Assert.True(breaker.TryEnter(out var p)); p!.Fail(); }
        Assert.Equal(CircuitState.Open, breaker.State); Assert.False(breaker.TryEnter(out _));
        time.Advance(TimeSpan.FromSeconds(30));
        Assert.True(breaker.TryEnter(out var probe)); Assert.False(breaker.TryEnter(out _));
        probe!.Succeed(); Assert.Equal(CircuitState.Closed, breaker.State);
        Assert.True(breaker.TryEnter(out var closed)); closed!.Succeed();
    }
    [Fact]
    public void LateClosedPermit_CannotCloseAnOpenCircuit()
    {
        var time = new MutableTimeProvider(); var breaker = new CircuitBreaker(time);
        Assert.True(breaker.TryEnter(out var late));
        for (int i = 0; i < 5; i++) { breaker.TryEnter(out var p); p!.Fail(); }
        late!.Succeed(); Assert.Equal(CircuitState.Open, breaker.State);
    }
}
public sealed class OutboundTimeoutTests
{
    [Fact]
    public async Task Cancels_AfterTimeout()
    {
        var timeout = new OutboundTimeout(TimeProvider.System);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => timeout.RunAsync(async ct =>
            { await Task.Delay(Timeout.InfiniteTimeSpan, ct); return true; }, CancellationToken.None, TimeSpan.FromMilliseconds(10)));
    }
}
public sealed class BoundedWorkerQueueTests
{
    [Fact]
    public async Task Backpressure_DoesNotDropOrGrow()
    {
        var queue = new BoundedWorkerQueue<int>();
        for (int i = 0; i < BoundedWorkerQueue<int>.Capacity; i++) await queue.WriteAsync(i, CancellationToken.None);
        var blocked = queue.WriteAsync(BoundedWorkerQueue<int>.Capacity, CancellationToken.None).AsTask();
        Assert.False(blocked.IsCompleted);
        await using var reader = queue.ReadAllAsync(CancellationToken.None).GetAsyncEnumerator();
        Assert.True(await reader.MoveNextAsync()); Assert.Equal(0, reader.Current);
        await blocked; queue.Complete(); var values = new List<int> { reader.Current };
        while (await reader.MoveNextAsync()) values.Add(reader.Current);
        Assert.Equal(Enumerable.Range(0, BoundedWorkerQueue<int>.Capacity + 1), values);
    }
}
