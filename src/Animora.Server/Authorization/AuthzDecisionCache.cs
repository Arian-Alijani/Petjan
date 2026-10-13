using System.Collections.Concurrent;
using Animora.Server.Hosting;
using Microsoft.Extensions.Options;

namespace Animora.Server.Authorization;

/// <summary>
/// PERF-AUTHZ-01: short, <b>absolute</b>-TTL cache of the two per-request identity verdicts that otherwise cost
/// 5-7 serialized DB round-trips on every authenticated request: the stored membership resolution
/// (<see cref="MembershipResolver"/>) and the entitlement verdict (<c>ServerEntitlementGate</c>).
/// <para>
/// Safety properties: only SUCCESSFUL verdicts are cached (a fix/approval is never hidden behind a cached denial);
/// the membership key includes the token's security stamp, so a rotated stamp is always a miss; staff/device
/// mutations in this process call <see cref="InvalidateUser"/>; every other change (tenant suspension, licence
/// revocation, multi-instance writes) converges within <c>Server:AuthCache:TtlSeconds</c> (default 10 s, 0 disables).
/// The TTL is absolute (not sliding) on purpose — a busy user can never keep a stale verdict alive.
/// </para>
/// Per-process by design (see the single-instance component list on <c>ReplayWindow</c>).
/// </summary>
internal sealed class AuthzDecisionCache
{
    private readonly record struct MembershipKey(string UserId, string DeviceId, string SecurityStamp);

    private readonly record struct Expiring<T>(T Value, long ExpiresTicks);

    private readonly ConcurrentDictionary<MembershipKey, Expiring<ResolvedMembership>> _memberships = new();
    private readonly ConcurrentDictionary<(string TenantId, string DeviceId), long> _entitlements = new();
    private readonly TimeProvider _time;
    private readonly long _ttlTicks;
    private readonly int _maxEntries;

    /// <summary>Creates the cache from <c>Server:AuthCache</c>.</summary>
    public AuthzDecisionCache(IOptions<ServerOptions> options, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(time);
        AuthCacheSettings settings = options.Value.AuthCache;
        _time = time;
        _ttlTicks = TimeSpan.FromSeconds(Math.Max(0, settings.TtlSeconds)).Ticks;
        _maxEntries = Math.Max(1, settings.MaxEntries);
    }

    /// <summary><see langword="false"/> when the TTL is 0 (every request hits the store — used by revocation-sensitive tests).</summary>
    public bool Enabled => _ttlTicks > 0;

    /// <summary>Looks up a previously resolved membership for exactly this user + device + token stamp.</summary>
    public bool TryGetMembership(string userId, string deviceId, string securityStamp, out ResolvedMembership? membership)
    {
        membership = null;
        if (!Enabled) return false;
        var key = new MembershipKey(userId, deviceId, securityStamp);
        if (!_memberships.TryGetValue(key, out var entry)) return false;
        if (entry.ExpiresTicks <= Now())
        {
            _memberships.TryRemove(key, out _);
            return false;
        }

        membership = entry.Value;
        return true;
    }

    /// <summary>Remembers a SUCCESSFUL resolution.</summary>
    public void SetMembership(string userId, string deviceId, string securityStamp, ResolvedMembership membership)
    {
        if (!Enabled) return;
        MakeRoom(_memberships.Count);
        _memberships[new MembershipKey(userId, deviceId, securityStamp)] = new(membership, Now() + _ttlTicks);
    }

    /// <summary>Whether a positive entitlement verdict for this tenant + device is still fresh.</summary>
    public bool IsEntitled(string tenantId, string deviceId)
    {
        if (!Enabled) return false;
        var key = (tenantId, deviceId);
        if (!_entitlements.TryGetValue(key, out long expires)) return false;
        if (expires > Now()) return true;
        _entitlements.TryRemove(key, out _);
        return false;
    }

    /// <summary>Remembers a POSITIVE entitlement verdict.</summary>
    public void SetEntitled(string tenantId, string deviceId)
    {
        if (!Enabled) return;
        MakeRoom(_entitlements.Count);
        _entitlements[(tenantId, deviceId)] = Now() + _ttlTicks;
    }

    /// <summary>Drops every cached membership of <paramref name="userId"/> (stamp rotation, status/permission/device change).</summary>
    public void InvalidateUser(string userId)
    {
        foreach (MembershipKey key in _memberships.Keys)
        {
            if (string.Equals(key.UserId, userId, StringComparison.Ordinal)) _memberships.TryRemove(key, out _);
        }
    }

    /// <summary>Drops every cached verdict of a tenant (operator suspension, subscription or licence mutation).</summary>
    public void InvalidateTenant(string tenantId)
    {
        foreach (var pair in _memberships)
        {
            if (string.Equals(pair.Value.Value.TenantId, tenantId, StringComparison.Ordinal)) _memberships.TryRemove(pair.Key, out _);
        }

        foreach (var key in _entitlements.Keys)
        {
            if (string.Equals(key.TenantId, tenantId, StringComparison.Ordinal)) _entitlements.TryRemove(key, out _);
        }
    }

    private long Now() => _time.GetUtcNow().UtcTicks;

    // Bounded memory: sweep expired entries first; if the map is still full, start over (a cache miss is always safe).
    private void MakeRoom(int count)
    {
        if (count < _maxEntries) return;
        long now = Now();
        foreach (var pair in _memberships)
        {
            if (pair.Value.ExpiresTicks <= now) _memberships.TryRemove(pair.Key, out _);
        }

        foreach (var pair in _entitlements)
        {
            if (pair.Value <= now) _entitlements.TryRemove(pair.Key, out _);
        }

        if (_memberships.Count >= _maxEntries) _memberships.Clear();
        if (_entitlements.Count >= _maxEntries) _entitlements.Clear();
    }
}
