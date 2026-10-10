using Animora.Application.Security;
using Animora.Contracts.Wire.Errors;
using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Animora.Domain.Time;
using Animora.Persistence.PostgreSql.Identity;
using Animora.Server.Auth;
using Animora.Server.Hosting;

namespace Animora.Server.Authorization;

/// <summary>
/// Tenant-binding plane (S05), the second stage after authentication. Binds every protected request to a
/// tenant <b>only</b> through the server-stored active membership (<see cref="IMembershipResolver"/>); the
/// token's tenant claim is compared and audited, never trusted for authority.
/// </summary>
internal sealed class TenantContextMiddleware(RequestDelegate next)
{
    /// <summary>Audit category of tenant-binding observations.</summary>
    public const string AuditCategory = "security";

    /// <summary>Audit action: a caller supplied a tenant hint that differs from the resolved tenant.</summary>
    public const string TenantHintIgnoredAction = "security.tenant_hint_ignored";

    /// <summary>Inbound header some clients send as a tenant hint.</summary>
    public const string TenantHintHeader = "X-Tenant-Id";

    private const string TenantHintQuery = "tenantId";

    /// <summary>Paths that never run tenant binding.</summary>
    internal static bool IsSkipped(PathString path) =>
        path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase)
        || path.StartsWithSegments(ServerRoutes.Operator, StringComparison.OrdinalIgnoreCase)
        || path.StartsWithSegments(ServerRoutes.Provisioning, StringComparison.OrdinalIgnoreCase)
        || path.Equals(ServerRoutes.Auth + "/login", StringComparison.OrdinalIgnoreCase)
        || path.Equals(ServerRoutes.Auth + "/refresh", StringComparison.OrdinalIgnoreCase)
        || path.Equals(ServerRoutes.Auth + "/keys", StringComparison.OrdinalIgnoreCase)
        || path.StartsWithSegments(ServerRoutes.Licensing + "/keys", StringComparison.OrdinalIgnoreCase);

    /// <summary>Runs the stage.</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Anonymous requests continue: the endpoint's own authorization answers 401 (authN before everything).
        if (IsSkipped(context.Request.Path) || context.User.Identity is not { IsAuthenticated: true })
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        AuthenticatedCaller? caller = AuthenticatedCaller.TryRead(context.User);
        if (caller is null)
        {
            await WriteFailureAsync(context, ErrorCodes.SessionExpired).ConfigureAwait(false);
            return;
        }

        // Resolved only now, never as InvokeAsync parameters: those are built for EVERY request, so a skipped route
        // (health, login, refresh, keys) would otherwise need the database just to be bypassed.
        IServiceProvider services = context.RequestServices;
        IMembershipResolver resolver = services.GetRequiredService<IMembershipResolver>();
        ServerAuthorizationSubjectFactory subjects = services.GetRequiredService<ServerAuthorizationSubjectFactory>();
        IRequestAuthorizationState state = services.GetRequiredService<IRequestAuthorizationState>();
        ISecurityAuditWriter audit = services.GetRequiredService<ISecurityAuditWriter>();
        IClock clock = services.GetRequiredService<IClock>();

        Result<ResolvedMembership> resolved = await resolver
            .ResolveAsync(caller.UserId, caller.DeviceId, caller.SecurityStamp, context.RequestAborted)
            .ConfigureAwait(false);
        if (resolved.IsFailure)
        {
            await WriteFailureAsync(context, resolved.Error).ConfigureAwait(false);
            return;
        }

        ResolvedMembership membership = resolved.Value!; // non-null on success
        state.Set(membership, await subjects.BuildAsync(membership, context.RequestAborted).ConfigureAwait(false));

        await AuditTenantHintAsync(context, caller, membership, audit, clock).ConfigureAwait(false);

        await next(context).ConfigureAwait(false);
    }

    private static async Task AuditTenantHintAsync(
        HttpContext context,
        AuthenticatedCaller caller,
        ResolvedMembership membership,
        ISecurityAuditWriter audit,
        IClock clock)
    {
        // The token's tenant claim and any header/query hint are only COMPARED with the resolved tenant.
        string? claimed = null;
        if (!string.Equals(caller.TenantId, membership.TenantId, StringComparison.Ordinal))
        {
            claimed = "token";
        }
        else if (context.Request.Headers.TryGetValue(TenantHintHeader, out var header)
            && header.Count > 0
            && !string.Equals(header[0], membership.TenantId, StringComparison.Ordinal))
        {
            claimed = "header";
        }
        else if (context.Request.Query.TryGetValue(TenantHintQuery, out var query)
            && query.Count > 0
            && !string.Equals(query[0], membership.TenantId, StringComparison.Ordinal))
        {
            claimed = "query";
        }

        if (claimed is null)
        {
            return;
        }

        string correlationId = CorrelationHeader.Resolve(context);
        SecurityAuditEntry entry = SecurityAuditEntry.Create(
            IdGenerator.NewId(clock.UtcNow),
            membership.TenantId,
            clock.UtcNow,
            AuditCategory,
            TenantHintIgnoredAction,
            SecurityAuditActions.OutcomeSuccess,
            correlationId.Length <= 64 ? correlationId : correlationId[..64],
            membership.UserId,
            membership.DeviceId,
            SecurityAuditDetail.Create(("source", claimed)));

        // Best effort: a failed audit write must never turn an ignored hint into a rejection.
        await audit.WriteAsync(entry, context.RequestAborted).ConfigureAwait(false);
    }

    private static Task WriteFailureAsync(HttpContext context, ErrorCode error)
    {
        string correlationId = CorrelationHeader.Resolve(context);
        CorrelationHeader.Echo(context, correlationId);
        context.Response.StatusCode = AuthEndpoints.StatusFor(error);

        // Only the stable code is exposed; detail text could distinguish "wrong tenant" from "wrong user".
        return context.Response.WriteAsJsonAsync(
            new WireApiError(error.Code, null, [], correlationId),
            context.RequestAborted);
    }
}
