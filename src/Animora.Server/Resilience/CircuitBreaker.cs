namespace Animora.Server.Resilience;

internal enum CircuitState { Closed, Open, HalfOpen }
/// <summary>TimeProvider-driven, thread-safe, bounded rolling failures with one fenced half-open probe.</summary>
internal sealed class CircuitBreaker(TimeProvider clock)
{
    private readonly object _gate = new();
    private readonly Queue<DateTimeOffset> _failures = new();
    private CircuitState _state;
    private DateTimeOffset _opened;
    private long _generation;
    private bool _probe;
    public CircuitState State { get { lock (_gate) return _state; } }
    public bool TryEnter(out Permit? permit)
    {
        lock (_gate)
        {
            if (_state == CircuitState.Open)
            {
                if (clock.GetUtcNow() - _opened < TimeSpan.FromSeconds(30)) { permit = null; return false; }
                _state = CircuitState.HalfOpen;
            }
            bool probe = _state == CircuitState.HalfOpen;
            if (probe && _probe) { permit = null; return false; }
            if (probe) _probe = true;
            permit = new(this, _generation, probe); return true;
        }
    }
    private void Complete(long generation, bool probe, bool success)
    {
        lock (_gate)
        {
            if (generation != _generation) return;
            var now = clock.GetUtcNow();
            if (probe)
            {
                _probe = false; _generation++; _failures.Clear();
                _state = success ? CircuitState.Closed : CircuitState.Open; _opened = now;
                return;
            }
            if (_state != CircuitState.Closed || success) return;
            while (_failures.TryPeek(out var at) && now - at > TimeSpan.FromSeconds(60)) _failures.Dequeue();
            _failures.Enqueue(now);
            if (_failures.Count >= 5) { _state = CircuitState.Open; _opened = now; _generation++; _failures.Clear(); }
        }
    }
    internal sealed class Permit(CircuitBreaker owner, long generation, bool probe) : IDisposable
    {
        private int _complete;
        public void Succeed() { if (Interlocked.Exchange(ref _complete, 1) == 0) owner.Complete(generation, probe, true); }
        public void Fail() { if (Interlocked.Exchange(ref _complete, 1) == 0) owner.Complete(generation, probe, false); }
        public void Dispose() => Fail();
    }
}
