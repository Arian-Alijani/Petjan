using System.Globalization;
using Animora.Application.Ports;
using Animora.Contracts.Wire.Errors;
using Animora.Domain.Identity;
using Animora.Server.Hosting;

namespace Animora.Server.Sync;

/// <summary>
/// D04 — the authoritative financial read path. <c>GET /finance/ledger?since={ticks}&amp;take={n}</c> returns the
/// tenant's invoices/payments that changed at or after a UTC-tick cursor so a peer device can display cross-device
/// transactions the generic state feed deliberately excludes (command-settled money is never replicated as feed
/// state). Read-only: nothing here crosses back into the command pipeline.
/// <para>Gated like the sync feed (<see cref="CoarsePolicies.TenantMember"/> + <see cref="CoarsePolicies.DeviceActive"/>)
/// plus the <c>sales.read</c> permission, and guarded by <see cref="SyncTenantBoundFilter"/> so a request that reaches
/// the handler without a bound tenant fails closed as a typed 401 rather than letting the tenant-filtered DbContext
/// throw a 500.</para>
/// </summary>
internal static class FinanceEndpoints
{
    /// <summary>Default window when the caller does not specify <c>take</c>.</summary>
    private const int DefaultTake = 200;

    public static IEndpointRouteBuilder MapFinanceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints.MapGroup(ServerRoutes.Finance)
            .RequireAuthorization(CoarsePolicies.TenantMember, CoarsePolicies.DeviceActive)
            .RequireRateLimiting(RateLimitPolicies.Sync)
            .AddEndpointFilter<SyncTenantBoundFilter>();

        group.MapGet("/ledger", LedgerAsync).RequireAuthorization(PermissionCodes.SalesRead);
        return endpoints;
    }

    internal static async Task<IResult> LedgerAsync(
        HttpContext http, IFinanceLedgerReader reader, CancellationToken ct)
    {
        long since = 0;
        if (http.Request.Query.TryGetValue("since", out var sinceRaw)
            && long.TryParse(sinceRaw.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedSince)
            && parsedSince > 0)
        {
            since = parsedSince;
        }

        int take = DefaultTake;
        if (http.Request.Query.TryGetValue("take", out var takeRaw)
            && int.TryParse(takeRaw.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedTake)
            && parsedTake > 0)
        {
            take = parsedTake;
        }

        try
        {
            var page = await reader.GetLedgerSinceAsync(since, take, ct).ConfigureAwait(false);
            return Results.Json(page);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Mirror the sync-batch handler: log with the correlation id, then rethrow so GlobalExceptionMiddleware
            // shapes the 500 WireApiError. Never swallowed into an empty page (that would masquerade as "no data").
            http.RequestServices.GetService<ILoggerFactory>()?.CreateLogger("Animora.Server.Sync.Finance")
                .LogError(ex, "Finance ledger read failed ({FailureClass}) for request {CorrelationId}.",
                    ex.GetType().FullName, CorrelationHeader.Resolve(http));
            throw;
        }
    }
}
