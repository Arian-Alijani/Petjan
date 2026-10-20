namespace Animora.Client.Services.Caching;

/// <summary>
/// A small LRU for non-sensitive read projections. The tenant and user IDs are <b>structural</b> parts of every key
/// (INF-02): the public API only accepts <c>(tenantId, userId, subKey)</c> and composes the real key internally, so an entry
/// can never be stored or fetched without a tenant+user component and cross-user bleed is impossible by construction.
/// </summary>
public sealed class TenantScopedCache<TSubKey, TValue> where TSubKey : notnull
{
    private readonly record struct Key(string TenantId, string UserId, TSubKey SubKey);

    private readonly int _capacity;
    private readonly Dictionary<Key, LinkedListNode<(Key Key, TValue Value)>> _entries = new();
    private readonly LinkedList<(Key Key, TValue Value)> _recent = new();
    private readonly Lock _gate = new();

    public TenantScopedCache(int capacity = 16)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _capacity = capacity;
    }

    public bool TryGet(string tenantId, string userId, TSubKey subKey, out TValue value)
    {
        var key = Compose(tenantId, userId, subKey);
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var node))
            {
                _recent.Remove(node);
                _recent.AddFirst(node);
                value = node.Value.Value;
                return true;
            }
        }
        value = default!;
        return false;
    }

    public void Set(string tenantId, string userId, TSubKey subKey, TValue value)
    {
        var key = Compose(tenantId, userId, subKey);
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var existing))
            {
                _recent.Remove(existing);
                _entries.Remove(key);
            }
            var node = _recent.AddFirst((key, value));
            _entries.Add(key, node);
            if (_entries.Count > _capacity)
            {
                var oldest = _recent.Last!;
                _entries.Remove(oldest.Value.Key);
                _recent.RemoveLast();
            }
        }
    }

    public void Invalidate(string tenantId, string userId, TSubKey subKey)
    {
        var key = Compose(tenantId, userId, subKey);
        lock (_gate)
        {
            if (!_entries.Remove(key, out var node)) return;
            _recent.Remove(node);
        }
    }

    private static Key Compose(string tenantId, string userId, TSubKey subKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        return new Key(tenantId, userId, subKey);
    }

    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
            _recent.Clear();
        }
    }
}
