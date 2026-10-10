using Microsoft.Extensions.Caching.Memory;
namespace Animora.Server.Observability;

internal sealed record TenantCacheKey
{
    public string TenantId { get; } public string UserId { get; } public string Resource { get; } public string Version { get; }
    public TenantCacheKey(string tenantId,string userId,string resource,string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId); ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(resource); ArgumentException.ThrowIfNullOrWhiteSpace(version);
        TenantId=tenantId; UserId=userId; Resource=resource; Version=version;
    }
}
internal sealed class TenantScopedMemoryCache<TKey,TValue> : IDisposable where TKey:notnull
{
    private readonly MemoryCache _cache;
    private readonly object _sync=new();
    private readonly Dictionary<(TenantCacheKey,TKey),LinkedListNode<(TenantCacheKey,TKey)>> _entries=[];
    private readonly LinkedList<(TenantCacheKey,TKey)> _lru=[];
    private readonly int _capacity;
    public TenantScopedMemoryCache(int capacity=1000)
    { ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity); _capacity=capacity; _cache=new(new MemoryCacheOptions { SizeLimit=capacity }); }
    public void Set(TenantCacheKey scope,TKey key,TValue value)
    {
        ArgumentNullException.ThrowIfNull(scope); lock(_sync)
        {
            var composite=(scope,key); Remove(composite);
            while(_entries.Count>=_capacity) Remove(_lru.First!.Value);
            var node=_lru.AddLast(composite); _entries.Add(composite,node);
            _cache.Set(composite,value,new MemoryCacheEntryOptions { Size=1,SlidingExpiration=TimeSpan.FromSeconds(60) });
        }
    }
    public bool TryGet(TenantCacheKey scope,TKey key,out TValue? value)
    {
        ArgumentNullException.ThrowIfNull(scope); lock(_sync)
        {
            var composite=(scope,key);
            if(!_cache.TryGetValue(composite,out value)) { Remove(composite); return false; }
            if(_entries.TryGetValue(composite,out var node)) { _lru.Remove(node); _lru.AddLast(node); } return true;
        }
    }
    private void Remove((TenantCacheKey,TKey) key)
    { _cache.Remove(key); if(_entries.Remove(key,out var node)) _lru.Remove(node); }
    public void Dispose()=>_cache.Dispose();
}
