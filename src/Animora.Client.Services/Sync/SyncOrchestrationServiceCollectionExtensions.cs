using Animora.Sync.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Animora.Client.Services.Sync;

/// <summary>DI entry point for client sync orchestration (scheduler + orchestrator + status inputs).</summary>
public static class SyncOrchestrationServiceCollectionExtensions
{
    /// <summary>
    /// Registers the orchestrator and the (dormant-until-configured) scheduler. The resync handler defaults to the no-op
    /// <see cref="NullSyncResyncHandler"/> via <c>TryAdd</c>, so the real handler can be registered before or after this call.
    /// The scheduler is started explicitly by the composition root (no generic host in the desktop).
    /// </summary>
    public static IServiceCollection AddAnimoraSyncOrchestration(this IServiceCollection services, Action<SyncScheduleOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new SyncScheduleOptions();
        configure?.Invoke(options);

        services.TryAddSingleton(options);
        services.TryAddSingleton<IDelayProvider, TimeProviderDelayProvider>();
        services.TryAddSingleton<ISyncResyncHandler, NullSyncResyncHandler>();
        services.TryAddSingleton<SyncOrchestrator>();
        services.TryAddSingleton<SyncSchedulerHostedService>();
        return services;
    }
}
