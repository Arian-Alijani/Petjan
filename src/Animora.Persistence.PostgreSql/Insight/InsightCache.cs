namespace Animora.Persistence.PostgreSql.Insight;
internal readonly record struct InsightCacheKey
{
    public string Tenant { get; }
    public string User { get; }
    public string Resource { get; }
    public string Version { get; }
    public InsightCacheKey(string tenant, string user, string resource, string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenant);
        ArgumentException.ThrowIfNullOrWhiteSpace(user);
        ArgumentException.ThrowIfNullOrWhiteSpace(resource);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        Tenant = tenant; User = user; Resource = resource; Version = version;
    }
}
public sealed class InsightCache(TimeProvider time)
{
    private const int Capacity = 1000;
    private readonly object _lock = new();
    private readonly Dictionary<InsightCacheKey, (InsightPage<InsightRow> Page, DateTimeOffset Expires)> _entries = [];
    internal bool TryGet(InsightCacheKey key, out InsightPage<InsightRow>? page)
    {
        lock (_lock)
        {
            if (_entries.TryGetValue(key, out var entry) && entry.Expires > time.GetUtcNow())
            { page = entry.Page; return true; }
            _entries.Remove(key); page = null; return false;
        }
    }
    internal void Set(InsightCacheKey key, InsightPage<InsightRow> page, int ttl)
    {
        lock (_lock)
        {
            var now = time.GetUtcNow();
            foreach (var expired in _entries.Where(x => x.Value.Expires <= now).Select(x => x.Key).ToArray()) _entries.Remove(expired);
            if (_entries.Count >= Capacity && !_entries.ContainsKey(key))
                _entries.Remove(_entries.MinBy(x => x.Value.Expires).Key);
            _entries[key] = (page, now.AddSeconds(ttl));
        }
    }
}
