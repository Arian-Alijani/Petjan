using Animora.Contracts.Wire.Auth;
using Animora.Contracts.Wire.Errors;
using Animora.Domain.Identity;
using Animora.Domain.Results;
using Animora.Server.Authorization;
using Animora.Server.Hosting;
using Microsoft.AspNetCore.Authorization;

namespace Animora.Server.Auth;

/// <summary>
/// S05 device lifecycle + offline-capability endpoints under <see cref="ServerRoutes.Auth"/>:
/// <c>POST /auth/device/{id}/approve|revoke</c> (policy <c>devices.manage</c>), <c>POST /auth/capabilities</c>
/// (policy <see cref="CoarsePolicies.DeviceActive"/>) and the anonymous public <c>GET /auth/keys</c>.
/// </summary>
internal static class DeviceEndpoints
{
    /// <summary>Maps the S05 endpoints.</summary>
    public static IEndpointRouteBuilder MapDeviceEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        RouteGroupBuilder group = app.MapGroup(ServerRoutes.Auth).RequireRateLimiting(RateLimitPolicies.Auth);

        group.MapPost("/device/{id}/approve", ApproveAsync).RequireAuthorization(PermissionCodes.DevicesManage);
        group.MapPost("/device/{id}/revoke", RevokeAsync).RequireAuthorization(PermissionCodes.DevicesManage);
        group.MapPost("/capabilities", CapabilitiesAsync).RequireAuthorization(CoarsePolicies.DeviceActive);

        // Public key set: anonymous by design (clients verify snapshots offline) and exempt from tenant binding.
        group.MapGet("/keys", KeysAsync).AllowAnonymous();

        return app;
    }

    private static int StatusFor(ErrorCode error) =>
        error.Code == ErrorCodes.NotFound.Code ? StatusCodes.Status404NotFound : AuthEndpoints.StatusFor(error);

    private static IResult Fail(HttpContext http, ErrorCode error)
    {
        string correlationId = CorrelationHeader.Resolve(http);
        CorrelationHeader.Echo(http, correlationId);
        return Results.Json(new WireApiError(error.Code, null, [], correlationId), statusCode: StatusFor(error));
    }

    private static IResult Ok<T>(HttpContext http, T body)
    {
        CorrelationHeader.Echo(http, CorrelationHeader.Resolve(http));
        http.Response.Headers.CacheControl = "no-store";
        return Results.Json(body);
    }

    private static async Task<IResult> ApproveAsync(HttpContext http, string id, IRequestAuthorizationState state, DeviceAdminService service)
    {
        if (state.Membership is not { } actor)
        {
            return Fail(http, ErrorCodes.SessionExpired);
        }

        Result<DeviceRegisterResponse> result = await service
            .ApproveAsync(actor, id, CorrelationHeader.Resolve(http), http.RequestAborted)
            .ConfigureAwait(false);
        return result.IsSuccess ? Ok(http, result.Value!) : Fail(http, result.Error);
    }

    private static async Task<IResult> RevokeAsync(
        HttpContext http,
        string id,
        DeviceRevokeRequest? request,
        IRequestAuthorizationState state,
        DeviceAdminService service)
    {
        if (state.Membership is not { } actor)
        {
            return Fail(http, ErrorCodes.SessionExpired);
        }

        Result<DeviceRegisterResponse> result = await service
            .RevokeAsync(actor, id, request, CorrelationHeader.Resolve(http), http.RequestAborted)
            .ConfigureAwait(false);
        return result.IsSuccess ? Ok(http, result.Value!) : Fail(http, result.Error);
    }

    private static async Task<IResult> CapabilitiesAsync(HttpContext http, IRequestAuthorizationState state, DeviceAdminService service)
    {
        AuthenticatedCaller? caller = AuthenticatedCaller.TryRead(http.User);
        if (caller is null || state.Membership is not { } actor)
        {
            return Fail(http, ErrorCodes.SessionExpired);
        }

        Result<CapabilitySnapshotResponse> result = await service
            .IssueCapabilitiesAsync(actor, caller, CorrelationHeader.Resolve(http), http.RequestAborted)
            .ConfigureAwait(false);
        return result.IsSuccess ? Ok(http, result.Value!) : Fail(http, result.Error);
    }

    private static async Task<IResult> KeysAsync(HttpContext http, DeviceAdminService service)
    {
        PublicKeySet set = await service.ListPublicKeysAsync(http.RequestAborted).ConfigureAwait(false);
        CorrelationHeader.Echo(http, CorrelationHeader.Resolve(http));
        http.Response.Headers.CacheControl = "public, max-age=300";
        return Results.Json(set);
    }
}
