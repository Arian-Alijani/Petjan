using Animora.Application.Security;
using Animora.Client.Services.Security;
using Animora.Infrastructure.Local.Identity;
using Animora.Server.Licensing.Verification;
using Animora.Sync.Custom;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Animora.Client.Services.Identity;

/// <summary>DI entry point for the online legs of the identity planes (G06).</summary>
public static class OnlineIdentityServiceCollectionExtensions
{
    /// <summary>
    /// Wires online auth, entitlement and device lifecycle <b>only when a valid server endpoint is configured</b> — the
    /// <see cref="ServerEndpointOptions"/> registered by <c>AddAnimoraHttpSyncEngine</c> (call it first, or pass the same options).
    /// Configured ⇒ <see cref="IEntitlementGate"/> is replaced by <see cref="OnlineEntitlementGate"/> (sealed-cache decisions, never a server call),
    /// the OS-protected session store replaces the offline <c>NullSessionStore</c> behind the shared <see cref="BearerAuthHandler"/>, and the
    /// online auth client / device lifecycle / sealed-entitlement store are registered. Unconfigured ⇒ nothing changes: the offline
    /// <see cref="AllowAllEntitlementGate"/> stays and no handler is added. <c>Replace</c> is unconditional so registration order does not matter.
    /// </summary>
    public static IServiceCollection AddAnimoraOnlineIdentity(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = services.Where(d => d.ServiceType == typeof(ServerEndpointOptions)).Select(d => d.ImplementationInstance)
            .OfType<ServerEndpointOptions>().LastOrDefault();
        if (options is null || !options.IsConfigured) return services;

        // Stores (singletons; scoped DB access happens inside via IServiceScopeFactory).
        services.AddSingleton<ProtectedSessionStore>();
        services.Replace(ServiceDescriptor.Singleton<ISessionStore>(sp => sp.GetRequiredService<ProtectedSessionStore>()));
        services.TryAddSingleton<IOfflineCredentialMirrorStore>(sp => sp.GetRequiredService<ProtectedSessionStore>());
        services.AddSingleton<SealedEntitlementStore>();
        services.TryAddSingleton<ISealedEntitlementStore>(sp => sp.GetRequiredService<SealedEntitlementStore>());
        services.TryAddSingleton<IOfflineAuthorityWiper, OfflineAuthorityWiper>();
        services.TryAddSingleton<IDeviceFingerprintSource, DeviceFingerprintSource>();

        // Online clients over the shared named HttpClient.
        services.TryAddSingleton<OnlineApi>();
        services.TryAddSingleton<LicenseKeyCache>();
        services.TryAddSingleton<CapabilityMirrorWriter>();
        services.TryAddSingleton<EntitlementSealer>();
        services.TryAddSingleton<OfflineSessionAuthorizer>();
        services.TryAddSingleton<DeviceLifecycleService>();
        services.TryAddSingleton<OnlineAuthClient>();
        services.TryAddSingleton<IOnlineAuthClient>(sp => sp.GetRequiredService<OnlineAuthClient>());
        services.TryAddSingleton<ISessionRefresher>(sp => sp.GetRequiredService<OnlineAuthClient>());

        // Entitlement plane: sealed-cache decisions only.
        services.AddSingleton<OnlineEntitlementGate>();
        services.Replace(ServiceDescriptor.Singleton<IEntitlementGate>(sp => sp.GetRequiredService<OnlineEntitlementGate>()));

        // Clinic account status (subscription / licence) for the Profile page — reads GET /licensing/status.
        // Only wired when the server is configured; offline-only builds leave IClinicAccountStatusSource unregistered
        // (the Profile page then shows "اطلاعات در دسترس نیست" instead of a wrong number).
        services.TryAddSingleton<Animora.Contracts.Profile.IClinicAccountStatusSource, Animora.Client.Services.Profile.ServerClinicAccountStatusSource>();
        return services;
    }
}
