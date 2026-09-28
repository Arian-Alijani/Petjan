namespace Animora.Client.Services.Caching;

/// <summary>A small LRU for non-sensitive read projections. A key MUST include tenant and user IDs.</summary>
public sealed class TenantScopedCache<TKey, TValue> where TKey : notnull
{
    private readonly int _capacity;
    private readonly Dictionary<TKey, LinkedListNode<(TKey Key, TValue Value)>> _entries = new();
    private readonly LinkedList<(TKey Key, TValue Value)> _recent = new();
    private readonly Lock _gate = new();

    public TenantScopedCache(int capacity = 16)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _capacity = capacity;
    }

    public bool TryGet(TKey key, out TValue value)
    {
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

    public void Set(TKey key, TValue value)
    {
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

    public void Invalidate(TKey key)
    {
        lock (_gate)
        {
            if (!_entries.Remove(key, out var node)) return;
            _recent.Remove(node);
        }
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
