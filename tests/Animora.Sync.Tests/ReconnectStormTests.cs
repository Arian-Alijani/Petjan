using System.Reflection;
using Animora.Application.Ports;
using Animora.Client.Services.Sync;
using Animora.Domain.Results;
using Animora.Sync.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Animora.Sync.Tests;

/// <summary>
/// ARCH §19 gate I / §12.3 (G08): a fleet restarting together must NOT converge into a reconnect storm. N simulated
/// devices all become "due" at the same instant t0 and run the REAL <see cref="SyncSchedulerHostedService"/> (±20% jitter,
/// capped exponential backoff). Time is virtual: a fake <see cref="IDelayProvider"/> records the requested wait and
/// returns instantly — no real sleep, fully deterministic.
/// </summary>
public sealed class ReconnectStormTests
{
    private const int Devices = 1000;
    private static readonly DateTimeOffset T0 = new(2026, 10, 11, 8, 0, 0, TimeSpan.Zero);

    /// <summary>Records every requested wait; cancels the loop after <c>stopAfter</c> waits.</summary>
    private sealed class RecordingDelay(CancellationTokenSource stop, int stopAfter) : IDelayProvider
    {
        public List<TimeSpan> Delays { get; } = [];

        public Task Delay(TimeSpan delay, CancellationToken ct = default)
        {
            Delays.Add(delay);
            if (Delays.Count >= stopAfter)
            {
                stop.Cancel();
            }

            ct.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    private sealed class ScriptedEngine(bool fail) : IStateSyncEngine
    {
        public bool IsConfigured => true;

        public Task<Result<SyncResult>> PullAsync(SyncScope scope, CancellationToken ct = default) =>
            Task.FromResult(fail
                ? Result<SyncResult>.Fail(ErrorCodes.NetworkUnavailable)
                : Result<SyncResult>.Ok(SyncResult.UpToDate(5, T0)));
    }

    private sealed class MemoryCursor : ISyncCursorStore
    {
        public Task<DateTimeOffset?> ReadAsync(CancellationToken ct = default) => Task.FromResult<DateTimeOffset?>(null);

        public Task MarkSyncedAsync(DateTimeOffset whenUtc, CancellationToken ct = default) => Task.CompletedTask;
    }

    private static readonly FieldInfo RandomField = typeof(SyncSchedulerHostedService)
        .GetField("_random", BindingFlags.Instance | BindingFlags.NonPublic)!;

    /// <summary>
    /// Runs one device to its <paramref name="waits"/>-th wait and returns the cumulative virtual time of its next attempt.
    /// A real device draws its jitter seed from (boot tick × 397) ^ processId, which differs per device; one process
    /// cannot vary the process id, so each simulated device is given a distinct seed in the same place the production seed lands.
    /// </summary>
    private static async Task<TimeSpan> NextAttemptAfterAsync(int device, SyncScheduleOptions options, bool fail, int waits, IServiceScopeFactory scopes)
    {
        using var stop = new CancellationTokenSource();
        var delay = new RecordingDelay(stop, waits);
        var engine = new ScriptedEngine(fail);
        var orchestrator = new SyncOrchestrator(engine, scopes, options, new NullSyncResyncHandler());
        await using var scheduler = new SyncSchedulerHostedService(engine, orchestrator, options, delay);
        RandomField.SetValue(scheduler, new Random(Mix(device)));

        await scheduler.ExecuteAsync(stop.Token);

        Assert.Equal(waits, delay.Delays.Count);
        return delay.Delays.Aggregate(TimeSpan.Zero, (sum, d) => sum + d);
    }

    /// <summary>SplitMix64 finaliser: de-correlates consecutive device numbers (System.Random's seeded sequence is correlated for neighbouring seeds).</summary>
    private static int Mix(int device)
    {
        unchecked
        {
            ulong z = (ulong)device + 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return (int)((z ^ (z >> 31)) & 0x7FFFFFFF);
        }
    }

    private static IServiceScopeFactory Scopes() =>
        new ServiceCollection().AddSingleton<ISyncCursorStore, MemoryCursor>().BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();

    /// <summary>Bucket occupancy of the fleet's next-attempt instants.</summary>
    private static int[] Histogram(IEnumerable<TimeSpan> nextAttempts, TimeSpan bucket)
    {
        var counts = new SortedDictionary<long, int>();
        foreach (TimeSpan at in nextAttempts)
        {
            long b = at.Ticks / bucket.Ticks;
            counts[b] = counts.GetValueOrDefault(b) + 1;
        }

        return counts.Values.ToArray();
    }

    [Fact]
    public async Task FleetRestart_DoesNotConverge_JitterSpreadsReconnects()
    {
        var options = new SyncScheduleOptions { Interval = TimeSpan.FromMinutes(5), JitterFraction = 0.2 };
        IServiceScopeFactory scopes = Scopes();

        var next = new List<TimeSpan>(Devices);
        for (int device = 0; device < Devices; device++)
        {
            next.Add(await NextAttemptAfterAsync(device, options, fail: false, waits: 1, scopes));
        }

        // Every device stays inside the ±20% window around the 5-minute interval (never earlier/later than configured).
        Assert.All(next, t => Assert.InRange(t, TimeSpan.FromMinutes(4), TimeSpan.FromMinutes(6)));

        // Spread histogram: 10-second buckets across the 120-second window → ~8% each if uniform.
        int[] buckets = Histogram(next, TimeSpan.FromSeconds(10));
        Assert.True(buckets.Length >= 10, $"reconnects occupy only {buckets.Length} buckets");
        Assert.True(buckets.Max() <= Devices / 5, $"largest bucket holds {buckets.Max()} of {Devices} devices (> 20%): the fleet converges");
        Assert.True(next.Distinct().Count() > Devices * 0.95, "near-identical wake-up instants across devices");
    }

    [Fact]
    public async Task OutageRecovery_CappedBackoff_StillSpreadsTheFleet()
    {
        // Server outage: every pull fails (NetworkUnavailable) → exponential backoff to the cap. Devices that fail together
        // must not retry together even once all of them sit on the 15-minute cap.
        var options = new SyncScheduleOptions
        {
            Interval = TimeSpan.FromMinutes(1),
            MinInterval = TimeSpan.FromSeconds(30),
            MaxBackoff = TimeSpan.FromMinutes(15),
            JitterFraction = 0.2,
        };
        IServiceScopeFactory scopes = Scopes();

        var next = new List<TimeSpan>(Devices);
        for (int device = 0; device < Devices; device++)
        {
            next.Add(await NextAttemptAfterAsync(device, options, fail: true, waits: 6, scopes));
        }

        int[] buckets = Histogram(next, TimeSpan.FromSeconds(30));
        Assert.True(buckets.Max() <= Devices * 15 / 100, $"after repeated backoff the largest 30 s bucket holds {buckets.Max()} of {Devices}");

        // The cap holds: no single wait exceeds MaxBackoff by more than the jitter fraction.
        using var stop = new CancellationTokenSource();
        var delay = new RecordingDelay(stop, 8);
        var engine = new ScriptedEngine(fail: true);
        await using var scheduler = new SyncSchedulerHostedService(engine, new SyncOrchestrator(engine, scopes, options, new NullSyncResyncHandler()), options, delay);
        await scheduler.ExecuteAsync(stop.Token);
        Assert.All(delay.Delays, d => Assert.True(d <= TimeSpan.FromMinutes(15) * 1.2 + TimeSpan.FromTicks(1)));
        Assert.True(delay.Delays.Zip(delay.Delays.Skip(1), (a, b) => b >= a * 0.6).All(x => x), "backoff must not collapse");
    }

    [Fact]
    public async Task NegativeControl_WithoutJitter_TheFleetDoesConverge()
    {
        // Proves the detector has teeth: with jitter disabled every device wakes at the same instant (100% in one bucket).
        var options = new SyncScheduleOptions { Interval = TimeSpan.FromMinutes(5), JitterFraction = 0 };
        IServiceScopeFactory scopes = Scopes();

        var next = new List<TimeSpan>(Devices);
        for (int device = 0; device < Devices; device++)
        {
            next.Add(await NextAttemptAfterAsync(device, options, fail: false, waits: 1, scopes));
        }

        Assert.Equal(Devices, Histogram(next, TimeSpan.FromSeconds(10)).Max());
    }
}
