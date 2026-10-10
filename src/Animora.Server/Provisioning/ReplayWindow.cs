namespace Animora.Server.Provisioning;

/// <summary>Single-host, fail-closed bounded replay window. Live entries are never evicted to admit traffic.</summary>
internal sealed class ReplayWindow
{
    public const int Capacity = 4096;
    private readonly Dictionary<string, DateTimeOffset> _entries = new(StringComparer.Ordinal);
    public bool TryAccept(string keyId, string signature, DateTimeOffset now)
    {
        lock (_entries)
        {
            foreach (var expired in _entries.Where(x => x.Value <= now).Select(x => x.Key).ToArray()) _entries.Remove(expired);
            var key = keyId + ":" + signature.ToLowerInvariant();
            if (_entries.ContainsKey(key) || _entries.Count >= Capacity) return false;
            _entries[key] = now.AddMinutes(5); return true;
        }
    }
    internal int Count { get { lock (_entries) return _entries.Count; } }
}
