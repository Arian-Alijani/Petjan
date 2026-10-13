using System.Diagnostics;
using System.Diagnostics.Metrics;
namespace Animora.Persistence.PostgreSql.Replication;

/// <summary>
/// Admission gate for <b>background workers</b> (outbox relay, sweeps, recovery,
/// retention, provisioning, reconciliation, licensing bootstrap, …). Kept small so
/// maintenance work cannot swamp the instance. SCALE-ADM-01: this gate is deliberately
/// <b>separate</b> from <see cref="ISyncAdmission"/> so background ticks never block the
/// user-facing sync snapshot build (and vice-versa).
/// </summary>
public interface IBackgroundAdmission
{
    ValueTask<IAsyncDisposable> EnterAsync(CancellationToken ct = default);
}

/// <summary>
/// Admission gate for the <b>request-path sync snapshot build</b> (<see cref="BatchFreezer"/>,
/// serving <c>/sync/batches</c> and <c>/sync/snapshots</c>). SCALE-ADM-01: sized to the write
/// pool minus headroom so several tenants' sync pulls can proceed concurrently instead of
/// serializing through a single process-wide permit shared with background workers.
/// </summary>
public interface ISyncAdmission : IBackgroundAdmission
{
}

/// <summary>
/// IMP-04: low-cardinality scaling signals of the two admission gates (meter <see cref="MeterName"/>). The gate name is the
/// only label (<c>worker</c> | <c>sync</c>); no tenant/user identifiers ever become labels.
/// <list type="bullet">
/// <item><c>animora.admission.wait.seconds</c> (histogram) - time a caller waited to enter the gate; the first signal that turns
/// red when the gate (not the database) is the bottleneck.</item>
/// <item><c>animora.admission.in_use</c> / <c>animora.admission.waiting</c> (gauges) - held permits and queued callers.</item>
/// </list>
/// </summary>
public static class AdmissionMetrics
{
    /// <summary>OpenTelemetry meter name (registered in the server telemetry setup).</summary>
    public const string MeterName = "Animora.Admission";

    /// <summary>Gate label of <see cref="IBackgroundAdmission"/>.</summary>
    public const string WorkerGate = "worker";

    /// <summary>Gate label of <see cref="ISyncAdmission"/>.</summary>
    public const string SyncGate = "sync";

    private static readonly Meter Meter = new(MeterName);
    private static readonly Histogram<double> Wait = Meter.CreateHistogram<double>("animora.admission.wait.seconds", "s");
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, GateState> Gates = new(StringComparer.Ordinal);

    private sealed class GateState
    {
        public int InUse;
        public int Waiting;
    }

    static AdmissionMetrics()
    {
        Meter.CreateObservableGauge("animora.admission.in_use", () => Gates.Select(g =>
            new Measurement<int>(Volatile.Read(ref g.Value.InUse), new KeyValuePair<string, object?>("gate", g.Key))));
        Meter.CreateObservableGauge("animora.admission.waiting", () => Gates.Select(g =>
            new Measurement<int>(Volatile.Read(ref g.Value.Waiting), new KeyValuePair<string, object?>("gate", g.Key))));
    }

    internal static async ValueTask<IAsyncDisposable> EnterAsync(string gate, SemaphoreSlim semaphore, CancellationToken ct)
    {
        GateState state = Gates.GetOrAdd(gate, static _ => new GateState());
        long started = Stopwatch.GetTimestamp();
        Interlocked.Increment(ref state.Waiting);
        try
        {
            await semaphore.WaitAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Decrement(ref state.Waiting);
            Wait.Record(Stopwatch.GetElapsedTime(started).TotalSeconds, new KeyValuePair<string, object?>("gate", gate));
        }

        Interlocked.Increment(ref state.InUse);
        return new Lease(semaphore, state);
    }

    private sealed class Lease(SemaphoreSlim gate, GateState state) : IAsyncDisposable
    {
        private SemaphoreSlim? _gate = gate;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _gate, null) is { } g)
            {
                Interlocked.Decrement(ref state.InUse);
                g.Release();
            }

            return ValueTask.CompletedTask;
        }
    }
}

/// <summary>Worker-gate implementation (<see cref="AdmissionMetrics.WorkerGate"/>).</summary>
public sealed class SemaphoreBackgroundAdmission(int concurrency = 1) : IBackgroundAdmission, IDisposable
{
    private readonly SemaphoreSlim _gate = new(Math.Max(1, concurrency), Math.Max(1, concurrency));

    /// <inheritdoc />
    public ValueTask<IAsyncDisposable> EnterAsync(CancellationToken ct = default) =>
        AdmissionMetrics.EnterAsync(AdmissionMetrics.WorkerGate, _gate, ct);

    /// <inheritdoc />
    public void Dispose() => _gate.Dispose();
}

/// <summary>Concrete request-path sync gate (<see cref="AdmissionMetrics.SyncGate"/>). Same mechanics as
/// <see cref="SemaphoreBackgroundAdmission"/> but a distinct type so DI can register and size it
/// independently of the worker gate.</summary>
public sealed class SemaphoreSyncAdmission(int concurrency = 1) : ISyncAdmission, IDisposable
{
    private readonly SemaphoreSlim _gate = new(Math.Max(1, concurrency), Math.Max(1, concurrency));

    /// <inheritdoc />
    public ValueTask<IAsyncDisposable> EnterAsync(CancellationToken ct = default) =>
        AdmissionMetrics.EnterAsync(AdmissionMetrics.SyncGate, _gate, ct);

    /// <inheritdoc />
    public void Dispose() => _gate.Dispose();
}
