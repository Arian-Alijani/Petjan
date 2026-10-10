using Animora.Contracts.Wire.Licensing;
using Animora.Domain.Results;
using Animora.Domain.Time;
using Animora.Persistence.PostgreSql.Licensing;
using Animora.Server.Auth;
using Animora.Server.Authorization;
using Animora.Server.Hosting;
using Animora.Server.Operator;
using Microsoft.EntityFrameworkCore;
namespace Animora.Server.Licensing;

internal static class LicenseEndpoints
{
    public static IEndpointRouteBuilder MapLicenseEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(ServerRoutes.Licensing).RequireRateLimiting(RateLimitPolicies.Auth);
        group.MapGet("/keys", async (LicenseKeyManager keys, CancellationToken ct) => Results.Ok(await keys.PublicAsync(ct).ConfigureAwait(false))).AllowAnonymous();
        var tenant = group.MapGroup(string.Empty).RequireAuthorization(CoarsePolicies.TenantMember, CoarsePolicies.DeviceActive);
        foreach (var kind in new[] { IssueKind.Activate, IssueKind.Renew })
        {
            var action = kind;
            tenant.MapPost("/" + action.ToString().ToLowerInvariant(), async (LicenseRequest r, LicenseIssuer issuer,
                IRequestAuthorizationState state, HttpContext http, CancellationToken ct) =>
            {
                var caller = AuthenticatedCaller.TryRead(http.User);
                if (caller is null || state.Membership is not { } membership) return Results.Unauthorized();
                http.Response.Headers.CacheControl = "no-store";
                var result = await issuer.IssueAsync(action, membership.TenantId, membership.UserId, caller.DeviceId, r,
                    CorrelationHeader.Resolve(http), ct).ConfigureAwait(false);
                return result.IsSuccess ? Results.Ok(result.Value) : OperatorEndpoints.Failure(result.Error, http);
            });
        }
        tenant.MapGet("/status", async (LicenseStore db, IRequestAuthorizationState state, IClock clock, CancellationToken ct) =>
        {
            if (state.Membership is not { } membership) return Results.Unauthorized();
            var grant = await db.Grants.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == membership.TenantId, ct).ConfigureAwait(false);
            var count = await db.Licenses.AsNoTracking().CountAsync(x => x.TenantId == membership.TenantId && x.Status == LicenseRecordStatus.Active, ct).ConfigureAwait(false);
            return Results.Ok(new LicenseStatusResponse(grant?.Status.ToString() ?? "NotGranted", grant?.ValidUntilUtc,
                grant?.PlanCode, grant?.SeatLimit, grant?.MaxDevices, count, clock.UtcNow));
        });
        return app;
    }
}
