using Animora.Domain.Results;
using Animora.Sync.Abstractions;

namespace Animora.Client.Services.Sync;

/// <summary>
/// Periodic state-pull loop (interval + jitter, capped exponential backoff on network/busy faults).
/// Dormant — no loop, no socket — when the engine is unconfigured or scheduling is disabled.
/// Modelled on <c>MaintenanceScheduler</c> (explicit <see cref="Start"/> / <see cref="DisposeAsync"/>) because the desktop
/// has no generic host; it can be wrapped by one unchanged.
/// </summary>
public sealed class SyncSchedulerHostedService : IAsyncDisposable
{
    private readonly IStateSyncEngine _engine;
    private readonly SyncOrchestrator _orchestrator;
    private readonly SyncScheduleOptions _options;
    private readonly IDelayProvider _delay;
    private readonly Random _random;
    private readonly CancellationTokenSource _stop = new();
    private Task? _loop;

    /// <summary>Creates the scheduler; the single jitter <see cref="Random"/> is seeded once here.</summary>
    public SyncSchedulerHostedService(
        IStateSyncEngine engine, SyncOrchestrator orchestrator, SyncScheduleOptions options, IDelayProvider delay)
    {
        _engine = engine;
        _orchestrator = orchestrator;
        _options = options;
        _delay = delay;
        _random = new Random(unchecked(Environment.TickCount * 397) ^ Environment.ProcessId);
    }

    /// <summary>Starts the background loop once; a no-op when dormant or already started.</summary>
    public void Start()
    {
        if (!_engine.IsConfigured || !_options.Enabled) return;
        _loop ??= Task.Run(() => ExecuteAsync(_stop.Token));
    }

    /// <summary>The loop body: returns immediately when dormant, otherwise runs until <paramref name="stoppingToken"/> is cancelled.</summary>
    public async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_engine.IsConfigured || !_options.Enabled) return;

        var baseWait = Max(_options.Interval, _options.MinInterval);
        var backoff = baseWait;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                Result<SyncOutcome> pull;
                try
                {
                    pull = await _orchestrator.PullAllAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception)
                {
                    // An unexpected fault must not kill the loop; treat it like a transient infrastructure fault.
                    pull = Result<SyncOutcome>.Fail(ErrorCodes.NetworkUnavailable);
                }

                TimeSpan wait;
                if (pull.IsFailure && IsTransient(pull.Error))
                {
                    backoff = Min(Double(backoff), _options.MaxBackoff);
                    wait = backoff;
                }
                else
                {
                    backoff = baseWait;
                    // Held back (no session yet): poll briefly so the first pull follows sign-in promptly.
                    wait = pull.IsSuccess && pull.Value == SyncOutcome.Deferred
                        ? Max(_options.DeferredRetryInterval, TimeSpan.FromSeconds(1))
                        : baseWait;
                }

                await _delay.Delay(Jitter(wait), stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private static bool IsTransient(ErrorCode error) =>
        error == ErrorCodes.NetworkUnavailable || error == ErrorCodes.ServerBusy;

    private TimeSpan Jitter(TimeSpan wait)
    {
        var fraction = Math.Clamp(_options.JitterFraction, 0d, 1d);
        var factor = 1d + ((_random.NextDouble() * 2d) - 1d) * fraction;
        return Max(TimeSpan.FromTicks((long)(wait.Ticks * factor)), TimeSpan.Zero);
    }

    private static TimeSpan Double(TimeSpan value) => TimeSpan.FromTicks(value.Ticks * 2);
    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;
    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;

    /// <summary>Stops the loop and waits for it to exit.</summary>
    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        if (_loop is not null)
        {
            try { await _loop.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        _stop.Dispose();
    }
}
