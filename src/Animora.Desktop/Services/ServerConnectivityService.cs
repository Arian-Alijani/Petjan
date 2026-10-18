using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Animora.Desktop.Diagnostics;
using Animora.Sync.Custom;

namespace Animora.Desktop.Services;

/// <summary>
/// Default <see cref="IServerConnectivityService"/>: issues a short <c>GET /health/live</c> against the
/// configured server over the shared named client (so it uses the same base address as every real call).
/// The liveness endpoint is public and tiny, so the probe is cheap and needs no authentication. Any
/// transport fault or timeout is reported as <see cref="ServerConnectivityState.Offline"/>, never thrown.
/// </summary>
public sealed class ServerConnectivityService(IHttpClientFactory httpFactory, ServerEndpointOptions options)
    : IServerConnectivityService
{
    /// <summary>The server's public liveness route (ServerRoutes.HealthLive), kept relative so the client's base address applies.</summary>
    private const string HealthLivePath = "health/live";

    /// <summary>A probe is a background check: keep it short so the indicator stays responsive even when the server hangs.</summary>
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);

    public async Task<ServerConnectivityState> CheckAsync(CancellationToken ct = default)
    {
        if (!options.IsConfigured) return ServerConnectivityState.NotConfigured;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(ProbeTimeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, HealthLivePath);
            var http = httpFactory.CreateClient(SyncFeedClient.HttpClientName);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);
            // Any definitive HTTP answer proves the server is reachable; liveness returns 200 when healthy.
            AppTrace.Debug($"[connectivity] GET /health/live → {(int)response.StatusCode}; online={response.IsSuccessStatusCode}.", "connectivity");
            return response.IsSuccessStatusCode ? ServerConnectivityState.Online : ServerConnectivityState.Offline;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // caller cancellation propagates; only the probe timeout is swallowed below.
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or InvalidOperationException)
        {
            AppTrace.Debug($"[connectivity] GET /health/live probe failed ({ex.GetType().Name}: {ex.Message}) ⇒ Offline.", "connectivity");
            return ServerConnectivityState.Offline;
        }
    }
}
