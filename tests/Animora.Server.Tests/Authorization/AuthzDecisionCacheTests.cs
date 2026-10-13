using Animora.Domain.Identity;
using Animora.Server.Authorization;
using Animora.Server.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace Animora.Server.Tests.Authorization;

/// <summary>PERF-AUTHZ-01: the verdict cache is short-lived, stamp-keyed, invalidatable and can be disabled.</summary>
public sealed class AuthzDecisionCacheTests
{
    private sealed class ManualTime(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }

    private static AuthzDecisionCache Create(ManualTime time, int ttl = 10, int max = 100) =>
        new(Options.Create(new ServerOptions { AuthCache = new AuthCacheSettings { TtlSeconds = ttl, MaxEntries = max } }), time);

    private static ResolvedMembership Member(string user = "u") => new("t", StaffRole.Manager, user, "d", true);

    [Fact]
    public void Membership_HitUntilAbsoluteTtl_ThenMiss()
    {
        var time = new ManualTime(DateTimeOffset.UnixEpoch);
        var cache = Create(time);
        cache.SetMembership("u", "d", "stamp", Member());
        Assert.True(cache.TryGetMembership("u", "d", "stamp", out var hit));
        Assert.Equal("t", hit!.TenantId);
        time.Advance(TimeSpan.FromSeconds(11));
        Assert.False(cache.TryGetMembership("u", "d", "stamp", out _));
    }

    [Fact]
    public void RotatedStamp_OrOtherDevice_IsAlwaysAMiss()
    {
        var cache = Create(new ManualTime(DateTimeOffset.UnixEpoch));
        cache.SetMembership("u", "d", "stamp-1", Member());
        Assert.False(cache.TryGetMembership("u", "d", "stamp-2", out _));
        Assert.False(cache.TryGetMembership("u", "other-device", "stamp-1", out _));
    }

    [Fact]
    public void InvalidateUser_DropsOnlyThatUser()
    {
        var cache = Create(new ManualTime(DateTimeOffset.UnixEpoch));
        cache.SetMembership("u1", "d", "s", Member("u1"));
        cache.SetMembership("u2", "d", "s", Member("u2"));
        cache.InvalidateUser("u1");
        Assert.False(cache.TryGetMembership("u1", "d", "s", out _));
        Assert.True(cache.TryGetMembership("u2", "d", "s", out _));
    }

    [Fact]
    public void ZeroTtl_DisablesCaching_AndEntitlementExpires()
    {
        var off = Create(new ManualTime(DateTimeOffset.UnixEpoch), ttl: 0);
        off.SetMembership("u", "d", "s", Member()); off.SetEntitled("t", "d");
        Assert.False(off.Enabled);
        Assert.False(off.TryGetMembership("u", "d", "s", out _));
        Assert.False(off.IsEntitled("t", "d"));

        var time = new ManualTime(DateTimeOffset.UnixEpoch);
        var on = Create(time);
        on.SetEntitled("t", "d");
        Assert.True(on.IsEntitled("t", "d"));
        time.Advance(TimeSpan.FromSeconds(10));
        Assert.False(on.IsEntitled("t", "d"));
    }

    [Fact]
    public void Ttl_IsClampedToSixtySeconds_EvenIfOptionsExceedTheRange()
    {
        // §2.1 defense-in-depth: an out-of-range TTL (validator is bypassed when constructing directly) must not
        // produce a verdict that outlives the documented 0..60s ceiling.
        var time = new ManualTime(DateTimeOffset.UnixEpoch);
        var cache = Create(time, ttl: 999);
        cache.SetMembership("u", "d", "stamp", Member());
        Assert.True(cache.TryGetMembership("u", "d", "stamp", out _));
        time.Advance(TimeSpan.FromSeconds(61));
        Assert.False(cache.TryGetMembership("u", "d", "stamp", out _));
    }

    [Fact]
    public void Capacity_IsBounded()
    {
        var cache = Create(new ManualTime(DateTimeOffset.UnixEpoch), max: 3);
        for (var i = 0; i < 50; i++) cache.SetMembership("u" + i, "d", "s", Member("u" + i));
        var live = Enumerable.Range(0, 50).Count(i => cache.TryGetMembership("u" + i, "d", "s", out _));
        Assert.InRange(live, 1, 3);
    }
}
