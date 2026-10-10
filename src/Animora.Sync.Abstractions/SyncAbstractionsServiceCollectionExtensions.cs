using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Animora.Sync.Abstractions;

/// <summary>DI entry point for the state-sync seam.</summary>
public static class SyncAbstractionsServiceCollectionExtensions
{
    /// <summary>Registers the offline NullSyncEngine fallback (TryAdd — a real adapter registered later with AddSingleton/Replace wins).</summary>
    public static IServiceCollection AddAnimoraSyncEngine(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IStateSyncEngine, NullSyncEngine>();
        return services;
    }
}
