using Animora.Application.Sync;
using Animora.Sync.Abstractions;
using Animora.Sync.Custom.Replica;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Animora.Sync.Custom;

/// <summary>DI entry point for the immutable-batch HTTP sync adapter.</summary>
public static class SyncCustomServiceCollectionExtensions
{
    /// <summary>
    /// Registers the feed client and apply-store seam; replaces the offline <c>NullSyncEngine</c> with <see cref="HttpStateSyncEngine"/>
    /// only when the supplied options carry a valid server address — otherwise the offline engine stays (register it first).
    /// </summary>
    public static IServiceCollection AddAnimoraHttpSyncEngine(this IServiceCollection services, Action<ServerEndpointOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new ServerEndpointOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);
        services.TryAddSingleton<ISyncApplyStore, BufferingSyncApplyStore>();

        // No AutomaticDecompression: pages are application/gzip payloads whose raw bytes are the hash input.
        services.AddHttpClient(SyncFeedClient.HttpClientName, client =>
        {
            if (options.TryGetBaseUri(out var baseUri)) client.BaseAddress = baseUri;
            client.Timeout = System.Threading.Timeout.InfiniteTimeSpan; // per-request timeouts are enforced by SyncFeedClient
        });
        services.AddSingleton(sp => new SyncFeedClient(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(SyncFeedClient.HttpClientName),
            sp.GetRequiredService<ServerEndpointOptions>()));

        if (options.IsConfigured)
        {
            // G04: a configured endpoint gets the real atomic replica store and the non-destructive full-resync handler. Replace is
            // unconditional so the result is the same whether AddAnimoraSyncOrchestration (Null resync default, TryAdd) ran before or after.
            services.Replace(ServiceDescriptor.Singleton<ISyncApplyStore, SqliteSyncApplyStore>());
            services.Replace(ServiceDescriptor.Singleton<ISyncResyncHandler, ShadowResyncHandler>());
            services.TryAddSingleton<ConflictResolver>();
            services.AddSingleton<IStateSyncEngine, HttpStateSyncEngine>();
        }

        return services;
    }
}
