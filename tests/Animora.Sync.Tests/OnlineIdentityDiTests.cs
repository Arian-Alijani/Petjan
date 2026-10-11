using Animora.Application.Security;
using Animora.Client.Services.Identity;
using Animora.Client.Services.Security;
using Animora.Domain.Time;
using Animora.Infrastructure.Local.Identity;
using Animora.Persistence.Tests;
using Animora.Server.Licensing.Verification;
using Animora.Sync.Custom;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Animora.Sync.Tests;

/// <summary>G06: the online identity plane is wired only when a server endpoint is configured — registration order does not matter.</summary>
public sealed class OnlineIdentityDiTests
{
    private static ServiceCollection Base(Action<ServerEndpointOptions>? configure, bool identityFirst)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IClock>(new FixedClock(FeedFixtures.Now));
        services.AddSingleton<IMonotonicClock>(new FakeMonotonicClock(FeedFixtures.Now));
        services.AddSingleton<Animora.Infrastructure.Local.Security.ISecretStore>(new MemorySecretStore());
        services.AddSingleton<IPasswordHasher>(new PlainPasswordHasher());
        services.AddSingleton<ICapabilitySnapshotSigner, Animora.Infrastructure.Local.Security.CapabilitySnapshotSigner>();
        services.TryAddSingleton<IEntitlementGate, AllowAllEntitlementGate>();
        if (identityFirst) services.AddAnimoraOnlineIdentity();
        services.AddAnimoraHttpSyncEngine(configure);
        if (!identityFirst) services.AddAnimoraOnlineIdentity();
        return services;
    }

    [Fact]
    public void Configured_ReplacesAllowAllGate()
    {
        using var provider = Base(o => o.BaseAddress = "https://animora.example/", identityFirst: false).BuildServiceProvider(validateScopes: false);

        Assert.IsType<OnlineEntitlementGate>(provider.GetRequiredService<IEntitlementGate>());
        Assert.IsType<ProtectedSessionStore>(provider.GetRequiredService<ISessionStore>());
        Assert.NotNull(provider.GetRequiredService<IOnlineAuthClient>());
        Assert.Same(provider.GetRequiredService<IOnlineAuthClient>(), provider.GetRequiredService<ISessionRefresher>());
        Assert.NotNull(provider.GetRequiredService<DeviceLifecycleService>());
        Assert.NotNull(provider.GetRequiredService<ISealedEntitlementStore>());
        Assert.NotNull(provider.GetRequiredService<IDeviceFingerprintSource>());
    }

    [Fact]
    public void Configured_BearerHandlerResolves_AndIdentityBeforeEndpointStaysOffline()
    {
        // Documented order: AddAnimoraHttpSyncEngine first (it registers the endpoint options + handler), then AddAnimoraOnlineIdentity.
        using var wired = Base(o => o.BaseAddress = "https://animora.example/", identityFirst: false).BuildServiceProvider(validateScopes: false);
        Assert.NotNull(wired.GetRequiredService<BearerAuthHandler>());

        // Called too early, the identity extension cannot see an endpoint yet and safely does nothing (never throws, never half-wires).
        using var early = Base(o => o.BaseAddress = "https://animora.example/", identityFirst: true).BuildServiceProvider(validateScopes: false);
        Assert.IsType<AllowAllEntitlementGate>(early.GetRequiredService<IEntitlementGate>());
        Assert.NotNull(early.GetRequiredService<BearerAuthHandler>());
    }

    [Fact]
    public void Unconfigured_KeepsAllowAllGate()
    {
        using var provider = Base(null, identityFirst: false).BuildServiceProvider(validateScopes: false);

        Assert.IsType<AllowAllEntitlementGate>(provider.GetRequiredService<IEntitlementGate>());
        Assert.Null(provider.GetService<IOnlineAuthClient>());
        Assert.Null(provider.GetService<ISessionStore>());
        Assert.Null(provider.GetService<BearerAuthHandler>());
        Assert.Null(provider.GetService<ISessionRefresher>());
    }

    [Fact]
    public void InvalidAddress_IsTreatedAsUnconfigured()
    {
        using var provider = Base(o => o.BaseAddress = "not a url", identityFirst: false).BuildServiceProvider(validateScopes: false);

        Assert.IsType<AllowAllEntitlementGate>(provider.GetRequiredService<IEntitlementGate>());
    }
}
