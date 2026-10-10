using Animora.Contracts.Wire.Errors;
using Animora.Domain.Results;
using Animora.Server.Hosting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace Animora.Server.Authorization;

/// <summary>
/// Renders every authorization outcome as the single <see cref="WireApiError"/> shape: an unauthenticated caller is
/// <b>401</b> <c>SESSION_EXPIRED</c> (authN precedes any permission check); a denied policy is <b>403</b> with the
/// stable code the failing handler reported (<c>PERMISSION_DENIED</c> / <c>PERMISSION_UNKNOWN</c> / …).
/// </summary>
internal sealed class ServerAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    /// <inheritdoc />
    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(context);

        if (authorizeResult.Succeeded)
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        if(context.Request.Path.StartsWithSegments(ServerRoutes.Admin))
        {
            await _default.HandleAsync(next,context,policy,authorizeResult).ConfigureAwait(false); return;
        }
        if (authorizeResult.Challenged)
        {
            await WriteAsync(context, ErrorCodes.SessionExpired).ConfigureAwait(false);
            return;
        }

        if (authorizeResult.Forbidden)
        {
            await WriteAsync(context, ResolveCode(authorizeResult)).ConfigureAwait(false);
            return;
        }

        await _default.HandleAsync(next, context, policy, authorizeResult).ConfigureAwait(false);
    }

    private static ErrorCode ResolveCode(PolicyAuthorizationResult result)
    {
        foreach (AuthorizationFailureReason reason in result.AuthorizationFailure?.FailureReasons ?? [])
        {
            if (reason.Message.StartsWith(PermissionAuthorizationHandler.ReasonPrefix, StringComparison.Ordinal))
            {
                string code = reason.Message[PermissionAuthorizationHandler.ReasonPrefix.Length..];
                foreach (ErrorCode known in new[]
                {
                    ErrorCodes.PermissionUnknown, ErrorCodes.PermissionDenied, ErrorCodes.DeviceRevoked, ErrorCodes.SessionExpired,
                })
                {
                    if (string.Equals(known.Code, code, StringComparison.Ordinal))
                    {
                        return known;
                    }
                }
            }
        }

        return ErrorCodes.PermissionDenied;
    }

    private static Task WriteAsync(HttpContext context, ErrorCode error)
    {
        string correlationId = CorrelationHeader.Resolve(context);
        CorrelationHeader.Echo(context, correlationId);
        context.Response.StatusCode = error.Code == ErrorCodes.SessionExpired.Code
            ? StatusCodes.Status401Unauthorized
            : StatusCodes.Status403Forbidden;
        return context.Response.WriteAsJsonAsync(new WireApiError(error.Code, null, [], correlationId), context.RequestAborted);
    }
}
