using Animora.Application.Commands;
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
    /// <para>
    /// G05: the same configured branch also replaces the offline <c>NullCommandTransport</c> with <see cref="HttpCommandTransport"/> (single + batch
    /// ports share ONE instance) and registers the id-remap service, replica version reader and settlement reconciler. This is folded into the
    /// already-called entry point, so <c>App.axaml.cs</c> needs no extra call; unconfigured ⇒ everything offline stays untouched.
    /// </para>
    /// </summary>
    public static IServiceCollection AddAnimoraHttpSyncEngine(this IServiceCollection services, Action<ServerEndpointOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new ServerEndpointOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);
        services.TryAddSingleton<ISyncApplyStore, BufferingSyncApplyStore>();

        // No AutomaticDecompression: pages are application/gzip payloads whose raw bytes are the hash input.
        var httpClient = services.AddHttpClient(SyncFeedClient.HttpClientName, client =>
        {
            if (options.TryGetBaseUri(out var baseUri)) client.BaseAddress = baseUri;
            client.Timeout = System.Threading.Timeout.InfiniteTimeSpan; // per-request timeouts are enforced by SyncFeedClient
        });

        // Diagnostics: when ANIMORA_NET_LOG=1 the launcher asked for a full server-traffic trace.
        // Register it as the OUTERMOST handler (added before BearerAuthHandler below) so it brackets the
        // whole pipeline — it logs each call and the final response/timing, and its elapsed time spans any
        // 401-refresh retry the auth handler performs internally. Gated so normal runs pay nothing: the
        // handler is not even added to the pipeline when off.
        if (NetworkLoggingHandler.IsEnabled)
        {
            services.AddTransient<NetworkLoggingHandler>();
            httpClient.AddHttpMessageHandler<NetworkLoggingHandler>();
        }
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

            // G06: the ONE named HttpClient carries the bearer + single-flight refresh handler. Offline default session store (no session)
            // keeps the handler resolvable before AddAnimoraOnlineIdentity (Client.Services) replaces it with the OS-protected store.
            services.TryAddSingleton<Animora.Application.Security.ISessionStore, NullSessionStore>();
            services.AddTransient<BearerAuthHandler>();
            httpClient.AddHttpMessageHandler<BearerAuthHandler>();

            // G05: real command transport (replaces the Null default regardless of registration order).
            services.AddSingleton<HttpCommandTransport>();
            services.Replace(ServiceDescriptor.Singleton<ICommandTransport>(sp => sp.GetRequiredService<HttpCommandTransport>()));
            services.TryAddSingleton<ICommandBatchTransport>(sp => sp.GetRequiredService<HttpCommandTransport>());
            services.TryAddSingleton<CommandIdRemapService>();
            services.TryAddSingleton<IReplicaVersionReader, ReplicaVersionReader>();
            services.TryAddScoped<SettlementReconciler>(); // scoped: ICommandJournal is scoped (same lifetime as LocalCommandProcessor)
        }

        return services;
    }
}
