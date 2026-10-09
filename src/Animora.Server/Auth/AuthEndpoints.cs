using Animora.Contracts.Wire.Auth;
using Animora.Contracts.Wire.Errors;
using Animora.Domain.Results;
using Animora.Server.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Animora.Server.Auth;

/// <summary>
/// The four S04 authentication endpoints under <see cref="ServerRoutes.Auth"/>, all behind the <c>auth</c>
/// rate-limit policy. Every failure is a <see cref="WireApiError"/> carrying the correlation id; lockout is
/// HTTP 423, revoked device / disabled user / suspended tenant are 403, bad credentials / session are 401.
/// Authorization policies are S05 — these handlers only <i>authenticate</i> (read the bearer principal).
/// </summary>
internal static class AuthEndpoints
{
    private static readonly IReadOnlyList<WireFieldError> NoFields = [];

    /// <summary>Maps <c>/auth/login</c>, <c>/auth/refresh</c>, <c>/auth/logout</c>, <c>/auth/device/register</c>.</summary>
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        RouteGroupBuilder group = app.MapGroup(ServerRoutes.Auth).RequireRateLimiting(RateLimitPolicies.Auth);

        group.MapPost("/login", LoginAsync);
        group.MapPost("/refresh", RefreshAsync);
        group.MapPost("/logout", LogoutAsync);
        group.MapPost("/device/register", RegisterDeviceAsync);

        return app;
    }

    /// <summary>Maps an auth failure to its HTTP status.</summary>
    internal static int StatusFor(ErrorCode error)
    {
        string code = error.Code;
        if (code == ErrorCodes.CredentialsInvalid.Code || code == ErrorCodes.SessionExpired.Code)
        {
            return StatusCodes.Status401Unauthorized;
        }

        if (code == ErrorCodes.UserLockedOut.Code)
        {
            return StatusCodes.Status423Locked;
        }

        if (code == ErrorCodes.UserDisabled.Code
            || code == ErrorCodes.DeviceRevoked.Code
            || code == ErrorCodes.TenantSuspended.Code
            || code == ErrorCodes.MembershipNotActive.Code)
        {
            return StatusCodes.Status403Forbidden;
        }

        if (code == ErrorCodes.ValidationFailed.Code)
        {
            return StatusCodes.Status400BadRequest;
        }

        if (code == ErrorCodes.ConcurrencyConflict.Code)
        {
            return StatusCodes.Status409Conflict;
        }

        return StatusCodes.Status500InternalServerError;
    }

    internal static IResult Fail(HttpContext http, ErrorCode error)
    {
        string correlationId = CorrelationHeader.Resolve(http);
        CorrelationHeader.Echo(http, correlationId);

        // Only the stable error code is exposed: detail text could differentiate "wrong tenant" from "wrong user".
        var body = new WireApiError(error.Code, null, NoFields, correlationId);
        return Results.Json(body, statusCode: StatusFor(error));
    }

    private static async Task<IResult> LoginAsync(HttpContext http, LoginRequest? request, LoginService service)
    {
        Result<LoginResponse> result = await service
            .LoginAsync(request, CorrelationHeader.Resolve(http), http.RequestAborted)
            .ConfigureAwait(false);
        return result.IsSuccess ? Ok(http, result.Value!) : Fail(http, result.Error);
    }

    private static async Task<IResult> RefreshAsync(HttpContext http, RefreshRequest? request, LoginService service)
    {
        Result<LoginResponse> result = await service
            .RefreshAsync(request, CorrelationHeader.Resolve(http), http.RequestAborted)
            .ConfigureAwait(false);
        return result.IsSuccess ? Ok(http, result.Value!) : Fail(http, result.Error);
    }

    private static async Task<IResult> LogoutAsync(HttpContext http, LoginService service)
    {
        AuthenticatedCaller? caller = AuthenticatedCaller.TryRead(http.User);
        if (caller is null)
        {
            return Fail(http, ErrorCodes.SessionExpired);
        }

        Result result = await service
            .LogoutAsync(caller, CorrelationHeader.Resolve(http), http.RequestAborted)
            .ConfigureAwait(false);
        return result.IsSuccess ? Results.NoContent() : Fail(http, result.Error);
    }

    private static async Task<IResult> RegisterDeviceAsync(HttpContext http, DeviceRegisterRequest? request, LoginService service)
    {
        AuthenticatedCaller? caller = AuthenticatedCaller.TryRead(http.User);
        if (caller is null)
        {
            return Fail(http, ErrorCodes.SessionExpired);
        }

        Result<DeviceRegisterResponse> result = await service
            .RegisterDeviceAsync(caller, request, CorrelationHeader.Resolve(http), http.RequestAborted)
            .ConfigureAwait(false);
        return result.IsSuccess ? Ok(http, result.Value!) : Fail(http, result.Error);
    }

    private static IResult Ok<T>(HttpContext http, T body)
    {
        CorrelationHeader.Echo(http, CorrelationHeader.Resolve(http));

        // Tokens must never be cached by an intermediary.
        http.Response.Headers.CacheControl = "no-store";
        return Results.Json(body);
    }
}
