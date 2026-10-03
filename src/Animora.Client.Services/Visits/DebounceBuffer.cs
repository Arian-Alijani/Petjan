using Animora.Domain.Clinical;

namespace Animora.Client.Services.Visits;

/// <summary>
/// Trailing-edge note buffer. Flush awaits both pending and already-running writes and <b>propagates</b> the first
/// write failure (after attempting every remaining write) so the caller can report it instead of showing a false
/// "saved" (V13). A failed background write stays tracked until the next flush observes it.
/// </summary>
public sealed class DebounceBuffer : IAsyncDisposable
{
    private sealed record Pending(Func<CancellationToken, Task> Write, CancellationTokenSource Delay);

    private readonly Dictionary<(string EncounterId, VisitNoteField Field), Pending> _pending = [];
    private readonly List<Task> _running = [];
    private readonly Lock _gate = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private bool _disposed;

    public void Enqueue(string encounterId, VisitNoteField field, Func<CancellationToken, Task> write)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(encounterId);
        ArgumentNullException.ThrowIfNull(write);
        var key = (encounterId, field);
        Pending pending;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_pending.Remove(key, out var previous)) previous.Delay.Cancel();
            if (_pending.Count >= 32) throw new InvalidOperationException("Debounce buffer is full; flush before adding more notes.");
            pending = new Pending(write, new CancellationTokenSource());
            _pending.Add(key, pending);
            _running.Add(RunDelayedAsync(key, pending));
            _running.RemoveAll(t => t.IsCompletedSuccessfully || t.IsCanceled);
        }
    }

    private async Task RunDelayedAsync((string, VisitNoteField) key, Pending pending)
    {
        try { await Task.Delay(600, pending.Delay.Token); }
        catch (OperationCanceledException) { return; }
        lock (_gate)
        {
            if (!_pending.TryGetValue(key, out var current) || !ReferenceEquals(current, pending)) return;
            _pending.Remove(key);
        }
        await WriteAsync(pending.Write, CancellationToken.None);
    }

    private async Task WriteAsync(Func<CancellationToken, Task> write, CancellationToken ct)
    {
        await _writeGate.WaitAsync(ct);
        try { await write(ct); }
        finally { _writeGate.Release(); }
    }

    public async Task FlushAsync(CancellationToken ct = default)
    {
        Pending[] pending;
        Task[] running;
        lock (_gate)
        {
            pending = _pending.Values.ToArray();
            _pending.Clear();
            foreach (var item in pending) item.Delay.Cancel();
            running = _running.ToArray();
            _running.Clear();
        }

        // Observe every outcome — one failed write must not stop the others from being attempted.
        List<Exception>? failures = null;
        foreach (var task in running)
        {
            try { await task.WaitAsync(ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) { (failures ??= []).Add(ex); }
        }

        foreach (var item in pending)
        {
            try { await WriteAsync(item.Write, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) { (failures ??= []).Add(ex); }
        }

        if (failures is { Count: > 0 })
        {
            // Rethrow the original (first) exception with its type intact so seam-level
            // InvalidOperationException(code) keeps flowing into the ViewModel guards.
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
        }
    }

    public async ValueTask DisposeAsync()
    {
        lock (_gate) _disposed = true;
        try { await FlushAsync(); }
        catch (Exception) { /* shutdown flush is best-effort; interactive flushes already surfaced failures */ }
        _writeGate.Dispose();
    }
}
