using Animora.Application.Diagnostics;
using Animora.Application.Ports;
using Animora.Infrastructure.Local.Diagnostics;
using Animora.Infrastructure.Local.Options;
using Animora.Domain.Results;
using Animora.Domain.Time;
using Animora.Infrastructure.Local.Startup;
using Animora.Infrastructure.Local.Time;
using Animora.Infrastructure.Local.Storage;
using Animora.Infrastructure.Local.Workers;
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

        // B05b — local-only buffered file logging and redacted diagnostics export.
        services.AddOptions<LocalLoggingOptions>()
            .Configure<IOptions<LocalStoreOptions>>((logging, store) => logging.DataDirectory = store.Value.DataDirectory);
        services.TryAddSingleton<ILoggerFactory>(provider => LoggingSetup.ConfigureLocalLogging(
            provider.GetRequiredService<IOptions<LocalLoggingOptions>>().Value));
        services.TryAddSingleton(typeof(ILogger<>), typeof(Logger<>));
        services.TryAddScoped<IDiagnosticsBundleBuilder, DiagnosticsBundleBuilder>();

        // A11a — keystore, key service, installation identity.
        services.TryAddSingleton(CreateSecretStore);
        services.TryAddSingleton<LocalDbKeyService>();
        services.Replace(ServiceDescriptor.Singleton<IStoreKeyProvider>(p => p.GetRequiredService<LocalDbKeyService>()));
        services.TryAddSingleton<InstallationIdentity>();
        services.TryAddSingleton<ILocalInstallationIdentity, LocalInstallationIdentity>(); // B01 — read by SessionCurrentUserProvider.

        // A11c — content-addressed local file store.
        services.TryAddSingleton<ILocalFileStore, LocalFileStore>();

        // A11d — clocks (TryAdd: tests may override) and the 8-step startup.
        services.TryAddSingleton<IClock>(_ => new SystemClock());
        services.TryAddSingleton<IMonotonicClock, MonotonicClock>();
        services.TryAddSingleton<IClinicTimeZoneProvider, ClinicTimeZoneProvider>();
        services.TryAddSingleton<FinanceProjectionWorker>();
        services.TryAddScoped<LocalStoreStartup>();
        services.TryAddScoped<ILocalStoreInitializer>(p => p.GetRequiredService<LocalStoreStartup>());

        // B03a — password hashing and the durable local session (app-wide; scoped DB access inside).
        services.AddOptions<LocalSessionOptions>();
        services.TryAddSingleton<Application.Security.IPasswordHasher, PasswordHashingService>();
        services.TryAddSingleton<Application.Security.ISessionService, LocalSessionService>();

        // B04b — capability snapshot signer (singleton: only the singleton ISecretStore; key cached in memory).
        services.TryAddSingleton<Application.Security.ICapabilitySnapshotSigner, CapabilitySnapshotSigner>();
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
