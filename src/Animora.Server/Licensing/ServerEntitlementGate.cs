using Animora.Domain.Results;
using Animora.Domain.Time;
using Animora.Persistence.PostgreSql.Licensing;
using Animora.Server.Authorization;
using Animora.Server.Hosting;
using Microsoft.EntityFrameworkCore;
namespace Animora.Server.Licensing;

/// <summary>
/// Licence/entitlement verdict on the READ pool. PERF-AUTHZ-01: a POSITIVE verdict is remembered per
/// tenant + device for <c>Server:AuthCache:TtlSeconds</c> (absolute TTL); denials are never cached, so a newly
/// issued licence is honoured at once while a revocation is observed within the TTL.
/// </summary>
internal sealed class ServerEntitlementGate(AnimoraEntitlementReadContext db, IClock clock, IHttpContextAccessor http,
    AuthzDecisionCache cache, AuthzOperationGuard guard, Animora.Server.Observability.ServerMetrics? metrics = null) : IServerEntitlementGate
{
    /// <summary>Exempt routes (single source: <see cref="ServerSurfacePolicy"/>).</summary>
    internal static bool IsExempt(PathString path) => ServerSurfacePolicy.IsEntitlementExempt(path);

    public async ValueTask<Result> EvaluateAsync(TenantEntitlementContext context, CancellationToken ct = default)
    {
        var request = http.HttpContext;
        if (request is not null && IsExempt(request.Request.Path)) return Result.Ok();
        var deviceId = request?.User.FindFirst("did")?.Value;
        if (string.IsNullOrEmpty(deviceId)) return Result.Fail(ErrorCodes.EntitlementRequired, "license_not_active");
        if (cache.IsEntitled(context.TenantId, deviceId)) return Result.Ok();
        // IMP-04: only the uncached (database) verdict is counted as an authZ query.
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        try { return await EvaluateFromStoreAsync(context, deviceId, ct).ConfigureAwait(false); }
        finally { metrics?.RecordAuthzQuery(System.Diagnostics.Stopwatch.GetElapsedTime(started)); }
    }

    private async ValueTask<Result> EvaluateFromStoreAsync(TenantEntitlementContext context, string deviceId, CancellationToken ct)
    {
        // IMP-06: each store call has its own bounded budget.
        var grant = await guard.RunAsync(t => db.Grants.AsNoTracking().Where(x => x.TenantId == context.TenantId)
            .Select(x => new { x.Status, x.ValidUntilUtc }).SingleOrDefaultAsync(t), ct).ConfigureAwait(false);
        if (grant?.Status == LicenseGrantStatus.Revoked) return Result.Fail(ErrorCodes.LicenseRevoked);
        if (grant is null || grant.Status != LicenseGrantStatus.Active || clock.UtcNow > grant.ValidUntilUtc)
            return Result.Fail(ErrorCodes.EntitlementRequired, "license_not_active");
        var active = await guard.RunAsync(t => db.Licenses.AsNoTracking().Where(x => x.TenantId == context.TenantId && x.DeviceId == deviceId
                && x.Status == LicenseRecordStatus.Active).Select(x => new { x.ExpiresUtc })
            .SingleOrDefaultAsync(t), ct).ConfigureAwait(false);
        if (active is not null && clock.UtcNow <= active.ExpiresUtc)
        {
            cache.SetEntitled(context.TenantId, deviceId);
            return Result.Ok();
        }
        if (await guard.RunAsync(t => db.Licenses.AsNoTracking().AnyAsync(x => x.TenantId == context.TenantId && x.DeviceId == deviceId
            && x.Status == LicenseRecordStatus.Revoked, t), ct).ConfigureAwait(false)) return Result.Fail(ErrorCodes.LicenseRevoked);
        return Result.Fail(ErrorCodes.EntitlementRequired, "license_not_active");
    }
}
