namespace Animora.LiveTest.Capacity;

/// <summary>
/// Background state-feed poll that every logged-in virtual desktop runs while the measured profile works, at the
/// cadence the production client actually uses (<c>LiveAccessRefreshService</c> polls <c>/sync/pull</c> every 4–6 s).
/// </summary>
/// <remarks>
/// Without this, a capacity run models a desktop that only ever talks when the operator clicks — so the server sees
/// roughly one tenth of the traffic a real clinic generates, and the measured ceiling is optimistic. The poll is
/// best-effort (a failed poll is simply skipped, exactly like the real client) and shares the session's
/// <see cref="PoolClient.PullLock"/> so it never races the replica.
/// </remarks>
public sealed class AmbientPoller : IDisposable
{
    private readonly IReadOnlyList<PoolClient> _pool;
    private readonly TimeSpan _interval;
    private readonly CancellationTokenSource _stop;
    private readonly List<Task> _loops = [];
    private long _pulls;
    private long _failures;
    private int _active;

    public AmbientPoller(IReadOnlyList<PoolClient> pool, int intervalSeconds, CancellationToken ct)
    {
        _pool = pool;
        _interval = TimeSpan.FromSeconds(Math.Max(1, intervalSeconds));
        _stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
    }

    /// <summary>
    /// How many of the pooled desktops are "switched on" right now. The ramp raises this with the concurrency level so
    /// the ambient traffic matches the number of simulated desktops — otherwise 64 idle sessions would poll during a
    /// 5-user level and make the levels incomparable.
    /// </summary>
    public void SetActive(int desktops) => Volatile.Write(ref _active, Math.Clamp(desktops, 0, _pool.Count));

    /// <summary>Successful background pulls so far (reported so the reader knows the ambient load really ran).</summary>
    public long Pulls => Interlocked.Read(ref _pulls);

    /// <summary>Failed background pulls so far (best-effort; a real client drops these silently too).</summary>
    public long Failures => Interlocked.Read(ref _failures);

    public void Start()
    {
        for (var index = 0; index < _pool.Count; index++)
        {
            var slot = index;
            var client = _pool[slot];
            _loops.Add(Task.Run(() => LoopAsync(client, slot)));
        }
    }

    private async Task LoopAsync(PoolClient client, int index)
    {
        var token = _stop.Token;
        try
        {
            while (!token.IsCancellationRequested)
            {
                // Jittered 5 s ± 15 %: real clients are not synchronised on a shared tick, and a perfectly aligned
                // poll would create an artificial thundering herd the production system never produces.
                var wait = _interval.TotalMilliseconds * (0.85 + Random.Shared.NextDouble() * 0.30);
                await Task.Delay(TimeSpan.FromMilliseconds(wait), token).ConfigureAwait(false);

                if (index >= Volatile.Read(ref _active))
                {
                    continue; // this desktop is not part of the current level
                }

                try
                {
                    await client.PullLock.WaitAsync(token).ConfigureAwait(false);
                    try
                    {
                        await client.Client.PullAsync(token).ConfigureAwait(false);
                    }
                    finally
                    {
                        client.PullLock.Release();
                    }

                    Interlocked.Increment(ref _pulls);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception)
                {
                    Interlocked.Increment(ref _failures);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }

    public void Dispose()
    {
        try
        {
            _stop.Cancel();
            Task.WaitAll(_loops.ToArray(), TimeSpan.FromSeconds(5));
        }
        catch (Exception)
        {
            // Best effort: the run is over, ambient polls must never surface as a failure.
        }
        finally
        {
            _stop.Dispose();
        }
    }
}
