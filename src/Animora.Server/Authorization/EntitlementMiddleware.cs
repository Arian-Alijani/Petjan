using Animora.Contracts.Wire.Errors;
using Animora.Domain.Results;
using Animora.Server.Hosting;

namespace Animora.Server.Authorization;

/// <summary>
/// Entitlement plane (S05 slot, S16 fills it). Runs <b>after</b> <c>UseAuthorization()</c>, so a permission
/// denial is always reported before an entitlement problem. Only requests the tenant middleware resolved are
/// gated; anonymous routes (health, login, refresh, keys) pass through untouched.
/// </summary>
internal sealed class EntitlementMiddleware(RequestDelegate next)
{
    /// <summary>Runs the stage.</summary>
    public async Task InvokeAsync(HttpContext context, IRequestAuthorizationState state, IServerEntitlementGate gate)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Request.Path.StartsWithSegments(ServerRoutes.Operator, StringComparison.OrdinalIgnoreCase)
            || context.Request.Path.StartsWithSegments(ServerRoutes.Provisioning, StringComparison.OrdinalIgnoreCase)
            || state.Membership is not { } membership)
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        Result result = await gate
            .EvaluateAsync(new TenantEntitlementContext(membership.TenantId, membership.UserId, CommandType: null), context.RequestAborted)
            .ConfigureAwait(false);
        if (result.IsSuccess)
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        string correlationId = CorrelationHeader.Resolve(context);
        CorrelationHeader.Echo(context, correlationId);

        // Any non-success is coerced to the one stable entitlement code (the gate's own detail is not exposed).
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(
            new WireApiError(ErrorCodes.EntitlementRequired.Code, null, [], correlationId),
            context.RequestAborted).ConfigureAwait(false);
    }
}
