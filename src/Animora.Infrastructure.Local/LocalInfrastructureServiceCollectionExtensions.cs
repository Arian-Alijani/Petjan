using Animora.Application.Ports;
using Animora.Domain.Results;
using Animora.Domain.Time;
using Animora.Infrastructure.Local.Startup;
using Animora.Infrastructure.Local.Time;
using Animora.Infrastructure.Local.Storage;
using Animora.Infrastructure.Local.Security;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Maintenance;
using Animora.Persistence.Sqlite.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Animora.Infrastructure.Local;

/// <summary>Single DI entry point for local infrastructure (A11a–d append here).</summary>
public static class LocalInfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddAnimoraLocalInfrastructure(this IServiceCollection services,
        Action<LocalStoreOptions>? configure = null)
    {
        services.AddAnimoraLocalStore(configure);

        // A11a — keystore, key service, installation identity.
        services.TryAddSingleton(CreateSecretStore);
        services.TryAddSingleton<LocalDbKeyService>();
        services.Replace(ServiceDescriptor.Singleton<IStoreKeyProvider>(p => p.GetRequiredService<LocalDbKeyService>()));
        services.TryAddSingleton<InstallationIdentity>();
        services.TryAddSingleton<ILocalInstallationIdentity, LocalInstallationIdentity>(); // B01 — read by SingleUserProvider.

        // A11c — content-addressed local file store.
        services.TryAddSingleton<ILocalFileStore, LocalFileStore>();

        // A11d — clocks (TryAdd: tests may override) and the 8-step startup.
        services.TryAddSingleton<IClock>(_ => new SystemClock());
        services.TryAddSingleton<IMonotonicClock, MonotonicClock>();
        services.TryAddScoped<LocalStoreStartup>();
        services.TryAddScoped<ILocalStoreInitializer>(p => p.GetRequiredService<LocalStoreStartup>());
        return services;
    }

    private static ISecretStore CreateSecretStore(IServiceProvider provider)
    {
        if (OperatingSystem.IsWindows()) return new DpapiSecretStore();
        if (OperatingSystem.IsMacOS() && File.Exists("/usr/bin/security")) return new KeychainSecretStore();

        var options = provider.GetRequiredService<IOptions<LocalStoreOptions>>();
        return options.Value.AllowInsecureKeyFallback
            ? new FileFallbackSecretStore(options, provider.GetRequiredService<ILogger<FileFallbackSecretStore>>())
            : new UnavailableSecretStore();
    }

    /// <summary>No OS keystore and fallback not allowed: every operation fails closed.</summary>
    private sealed class UnavailableSecretStore : ISecretStore
    {
        private static readonly ErrorCode Code = ErrorCodes.StoreKeyUnavailable;
        private const string Detail = "No OS keystore on this platform and insecure fallback is disabled.";
        public Result<byte[]> TryRead(string name) => Result<byte[]>.Fail(Code, Detail);
        public Result Write(string name, byte[] value) => Result.Fail(Code, Detail);
        public Result Delete(string name) => Result.Fail(Code, Detail);
    }
}
