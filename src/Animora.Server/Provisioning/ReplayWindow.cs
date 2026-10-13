namespace Animora.Server.Provisioning;

/// <summary>
/// Single-host bounded replay window (SCALE-STATE-01). At capacity it SHEDS the oldest live entry instead of
/// denying every new request (the previous fail-closed behaviour let a burst of &gt;Capacity distinct signatures
/// within the window self-DoS all provisioning). Replay exposure after a shed is still bounded by the HMAC
/// timestamp tolerance in <see cref="RequestSignatureVerifier"/> (+/- 5 min).
/// <para>
/// PER-PROCESS BY DESIGN. Single-instance components that must be replaced or explicitly accepted before running
/// more than one server instance: this window, <c>AdminStatusCache</c>, <c>CircuitBreaker</c>,
/// <c>WorkerFailureLogGate</c>, <c>QueryBudget</c> and the in-memory rate-limit partitions.
/// TODO(multi-instance): back this with a shared store (DB table keyed by (keyId, signature) with
/// INSERT .. ON CONFLICT DO NOTHING and a TTL prune, or Redis SET NX EX 300) for exactly-once across instances.
/// </para>
/// </summary>
internal sealed class ReplayWindow
{
    public const int Capacity = 4096;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(5);
    private readonly Dictionary<string, DateTimeOffset> _entries = new(StringComparer.Ordinal);
    private readonly Queue<(string Key, DateTimeOffset Expires)> _order = new();
    private long _shed;

    public bool TryAccept(string keyId, string signature, DateTimeOffset now)
    {
        lock (_entries)
        {
            // Entries are inserted with monotonically non-decreasing expiry, so the queue head is always the oldest.
            while (_order.Count > 0 && _order.Peek().Expires <= now) Forget(_order.Dequeue());
            var key = keyId + ":" + signature.ToLowerInvariant();
            if (_entries.ContainsKey(key)) return false;
            while (_entries.Count >= Capacity && _order.Count > 0) { Forget(_order.Dequeue()); _shed++; }
            var expires = now.Add(Window);
            _entries[key] = expires; _order.Enqueue((key, expires)); return true;
        }
    }

    private void Forget((string Key, DateTimeOffset Expires) item)
    {
        // A key may have been re-accepted later with a newer expiry; only drop the matching generation.
        if (_entries.TryGetValue(item.Key, out var current) && current == item.Expires) _entries.Remove(item.Key);
    }

    internal int Count { get { lock (_entries) return _entries.Count; } }
    /// <summary>Live entries evicted early because the window was full (observability / tests).</summary>
    internal long ShedCount { get { lock (_entries) return _shed; } }
}
