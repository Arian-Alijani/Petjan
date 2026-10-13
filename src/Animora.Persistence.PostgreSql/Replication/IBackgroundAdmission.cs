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

public sealed class SemaphoreBackgroundAdmission(int concurrency = 1) : IBackgroundAdmission, IDisposable
{
    private readonly SemaphoreSlim _gate = new(Math.Max(1, concurrency), Math.Max(1, concurrency));
    public async ValueTask<IAsyncDisposable> EnterAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        return new Lease(_gate);
    }
    private sealed class Lease(SemaphoreSlim gate) : IAsyncDisposable
    {
        private SemaphoreSlim? _gate = gate;
        public ValueTask DisposeAsync() { Interlocked.Exchange(ref _gate, null)?.Release(); return ValueTask.CompletedTask; }
    }
    public void Dispose() => _gate.Dispose();
}

/// <summary>Concrete request-path sync gate. Same mechanics as
/// <see cref="SemaphoreBackgroundAdmission"/> but a distinct type so DI can register and size it
/// independently of the worker gate.</summary>
public sealed class SemaphoreSyncAdmission(int concurrency = 1) : ISyncAdmission, IDisposable
{
    private readonly SemaphoreSlim _gate = new(Math.Max(1, concurrency), Math.Max(1, concurrency));
    public async ValueTask<IAsyncDisposable> EnterAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        return new Lease(_gate);
    }
    private sealed class Lease(SemaphoreSlim gate) : IAsyncDisposable
    {
        private SemaphoreSlim? _gate = gate;
        public ValueTask DisposeAsync() { Interlocked.Exchange(ref _gate, null)?.Release(); return ValueTask.CompletedTask; }
    }
    public void Dispose() => _gate.Dispose();
}
