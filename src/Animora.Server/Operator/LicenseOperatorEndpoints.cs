using System.Globalization;
using Animora.Persistence.PostgreSql.Licensing;
using Animora.Server.Authorization;
using Animora.Server.Hosting;
using Microsoft.EntityFrameworkCore;
namespace Animora.Server.Operator;

internal sealed record LicenseRevokeRequest(string Reason);
internal static class LicenseOperatorEndpoints
{
    public static IEndpointRouteBuilder MapLicenseOperatorEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(ServerRoutes.Operator).RequireAuthorization(CoarsePolicies.OperatorOnly)
            .RequireRateLimiting(RateLimitPolicies.Operator);
        group.MapGet("/tenants/{id}/licenses", async (string id, LicenseAdministration administration,
            LicenseStore db, HttpContext http, CancellationToken ct) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            var grantSequence = await db.Grants.AsNoTracking().Where(x => x.TenantId == id)
                .Select(x => (long?)x.Sequence).SingleOrDefaultAsync(ct).ConfigureAwait(false);
            if (grantSequence is { } sequence) http.Response.Headers.ETag = '"' + sequence.ToString(CultureInfo.InvariantCulture) + '"';
            return Results.Ok(await administration.ListAsync(id, OperatorEndpoints.Number(http, "page", 1, int.MaxValue / 200),
                OperatorEndpoints.Number(http, "pageSize", 50, 200), OperatorEndpoints.Actor(http), CorrelationHeader.Resolve(http), ct).ConfigureAwait(false));
        });
        group.MapPost("/tenants/{id}/licenses/{licenseId}/revoke", async (string id, string licenseId, LicenseRevokeRequest r,
            LicenseAdministration administration, HttpContext http, CancellationToken ct) => OperatorEndpoints.ToHttp(
                await administration.RevokeAsync(id, licenseId, http.Request.Headers.IfMatch.ToString(), r.Reason,
                    OperatorEndpoints.Actor(http), CorrelationHeader.Resolve(http), ct).ConfigureAwait(false), http));
        group.MapPost("/tenants/{id}/licenses/recheck", async (string id, LicenseAdministration administration,
            HttpContext http, CancellationToken ct) => OperatorEndpoints.ToHttp(await administration.RecheckAsync(id,
                http.Request.Headers.IfMatch.ToString(), OperatorEndpoints.Actor(http), CorrelationHeader.Resolve(http), ct).ConfigureAwait(false), http));
        group.MapPost("/licensing/keys/rotate", async (LicenseKeyManager keys, HttpContext http, CancellationToken ct) =>
        {
            var result = await keys.RotateAsync(http.Request.Headers.IfMatch.ToString(), OperatorEndpoints.Actor(http), ct).ConfigureAwait(false);
            if (result.IsFailure) return OperatorEndpoints.Failure(result.Error, http);
            http.Response.Headers.ETag = '"' + result.Value + '"';
            return Results.Ok(new { keyId = result.Value });
        });
        return app;
    }
}
