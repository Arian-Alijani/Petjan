namespace Animora.Persistence.PostgreSql.Replication;

public interface IBackgroundAdmission
{
    ValueTask<IAsyncDisposable> EnterAsync(CancellationToken ct = default);
}
public sealed class SemaphoreBackgroundAdmission(int concurrency = 1) : IBackgroundAdmission, IDisposable
{
    private readonly SemaphoreSlim _gate = new(concurrency, concurrency);
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
