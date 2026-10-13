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
/// IMP-05: the gate shed this caller (queue full or wait budget exceeded). Request-path callers map it to
/// <c>503 SERVER_BUSY + Retry-After</c> instead of blocking a request thread indefinitely.
/// </summary>
public sealed class AdmissionSaturatedException(string gate, string reason)
    : Exception($"Admission gate '{gate}' saturated ({reason}).")
{
    /// <summary>Gate label (<see cref="AdmissionMetrics.WorkerGate"/> | <see cref="AdmissionMetrics.SyncGate"/>).</summary>
    public string Gate { get; } = gate;

    /// <summary><c>queue_full</c> | <c>wait_timeout</c>.</summary>
    public string Reason { get; } = reason;
}

/// <summary>
/// Shared mechanics of both admission gates: a counting semaphore plus (IMP-05) an OPTIONAL bounded queue and wait
/// budget. Background workers use the unbounded form (blocking a worker is harmless); the request-path sync gate is
/// bounded so overload sheds cleanly.
/// </summary>
internal sealed class AdmissionGate(string name, int concurrency, TimeSpan? maxWait, int maxQueue) : IDisposable
{
    private readonly SemaphoreSlim _semaphore = new(Math.Max(1, concurrency), Math.Max(1, concurrency));
    private int _waiting;

    public ValueTask<IAsyncDisposable> EnterAsync(CancellationToken ct) =>
        AdmissionMetrics.EnterAsync(name, _semaphore, maxWait, maxQueue, ref _waiting, ct);

    public void Dispose() => _semaphore.Dispose();
}

/// <summary>
/// IMP-04: low-cardinality scaling signals of the two admission gates (meter <see cref="MeterName"/>). The gate name is the
/// only label (<c>worker</c> | <c>sync</c>); no tenant/user identifiers ever become labels.
/// <list type="bullet">
/// <item><c>animora.admission.wait.seconds</c> (histogram) - time a caller waited to enter the gate; the first signal that turns
/// red when the gate (not the database) is the bottleneck.</item>
/// <item><c>animora.admission.in_use</c> / <c>animora.admission.waiting</c> (gauges) - held permits and queued callers.</item>
/// <item><c>animora.admission.rejected</c> (counter, IMP-05) - callers shed because the queue was full or the wait budget expired.</item>
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
    private static readonly Counter<long> Rejected = Meter.CreateCounter<long>("animora.admission.rejected");
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

    internal static async ValueTask<IAsyncDisposable> EnterAsync(
        string gate, SemaphoreSlim semaphore, TimeSpan? maxWait, int maxQueue, ref int instanceWaiting, CancellationToken ct)
    {
        GateState state = Gates.GetOrAdd(gate, static _ => new GateState());
        long started = Stopwatch.GetTimestamp();

        // Fast path: a free permit never queues, so a full queue only rejects callers that would really have to wait.
        if (!semaphore.Wait(0))
        {
            if (Interlocked.Increment(ref instanceWaiting) > maxQueue)
            {
                Interlocked.Decrement(ref instanceWaiting);
                Rejected.Add(1, new KeyValuePair<string, object?>("gate", gate), new KeyValuePair<string, object?>("reason", "queue_full"));
                throw new AdmissionSaturatedException(gate, "queue_full");
            }

            Interlocked.Increment(ref state.Waiting);
            bool entered;
            try
            {
                entered = maxWait is { } budget
                    ? await semaphore.WaitAsync(budget, ct).ConfigureAwait(false)
                    : await semaphore.WaitAsync(Timeout.InfiniteTimeSpan, ct).ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Decrement(ref state.Waiting);
                Interlocked.Decrement(ref instanceWaiting);
                Wait.Record(Stopwatch.GetElapsedTime(started).TotalSeconds, new KeyValuePair<string, object?>("gate", gate));
            }

            if (!entered)
            {
                Rejected.Add(1, new KeyValuePair<string, object?>("gate", gate), new KeyValuePair<string, object?>("reason", "wait_timeout"));
                throw new AdmissionSaturatedException(gate, "wait_timeout");
            }
        }
        else
        {
            Wait.Record(0, new KeyValuePair<string, object?>("gate", gate));
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

/// <summary>Worker-gate implementation (<see cref="AdmissionMetrics.WorkerGate"/>): unbounded wait - a background tick may queue.</summary>
public sealed class SemaphoreBackgroundAdmission(int concurrency = 1) : IBackgroundAdmission, IDisposable
{
    private readonly AdmissionGate _gate = new(AdmissionMetrics.WorkerGate, concurrency, null, int.MaxValue);

    /// <inheritdoc />
    public ValueTask<IAsyncDisposable> EnterAsync(CancellationToken ct = default) => _gate.EnterAsync(ct);

    /// <inheritdoc />
    public void Dispose() => _gate.Dispose();
}

/// <summary>
/// Concrete request-path sync gate (<see cref="AdmissionMetrics.SyncGate"/>). IMP-05: BOUNDED - at most
/// <c>maxQueue</c> callers may wait and each waits at most <c>maxWait</c>; beyond that it throws
/// <see cref="AdmissionSaturatedException"/> (503 + Retry-After) instead of pinning request threads under overload.
/// Defaults (<c>maxWait</c> null, unbounded queue) preserve the original blocking behaviour for direct construction.
/// </summary>
public sealed class SemaphoreSyncAdmission(int concurrency = 1, TimeSpan? maxWait = null, int maxQueue = int.MaxValue) : ISyncAdmission, IDisposable
{
    private readonly AdmissionGate _gate = new(AdmissionMetrics.SyncGate, concurrency, maxWait, Math.Max(0, maxQueue));

    /// <inheritdoc />
    public ValueTask<IAsyncDisposable> EnterAsync(CancellationToken ct = default) => _gate.EnterAsync(ct);

    /// <inheritdoc />
    public void Dispose() => _gate.Dispose();
}
