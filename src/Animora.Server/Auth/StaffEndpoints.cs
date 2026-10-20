using Animora.Application.Security;
using Animora.Contracts.Wire.Auth;
using Animora.Contracts.Wire.Errors;
using Animora.Domain.Identity;
using Animora.Domain.Results;
using Animora.Server.Authorization;
using Animora.Server.Hosting;

namespace Animora.Server.Auth;

/// <summary>
/// Online staff-management endpoints under <see cref="ServerRoutes.Auth"/>: <c>GET /auth/staff</c> and
/// <c>POST /auth/staff</c>, <c>POST /auth/staff/{id}/active</c> and <c>POST /auth/staff/{id}/password</c> (policy <c>users.manage</c>) and
/// <c>POST /auth/staff/access</c> (policy <c>roles.manage</c>). The tenant is always the authenticated caller's.
/// </summary>
internal static class StaffEndpoints
{
    /// <summary>Maps the staff-management endpoints.</summary>
    public static IEndpointRouteBuilder MapStaffEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // These are authenticated operations, not password attempts. Keep editing/polling out of the
        // IP-wide 10/min login bucket so one active clinic cannot lock out its own staff or saves.
        RouteGroupBuilder group = app.MapGroup(ServerRoutes.Auth).RequireRateLimiting(RateLimitPolicies.Commands);

        // ListAsync independently checks users.manage OR roles.manage. Requiring users.manage here
        // prevented delegated access administrators from loading/reloading the editor after a successful save.
        group.MapGet("/staff", ListAsync).RequireAuthorization(CoarsePolicies.TenantMember);
        group.MapPost("/staff", CreateAsync).RequireAuthorization(PermissionCodes.UsersManage);
        group.MapPost("/staff/{id}/active", SetActiveAsync).RequireAuthorization(PermissionCodes.UsersManage);
        group.MapPost("/staff/{id}/password", SetPasswordAsync)
            .RequireAuthorization(PermissionCodes.UsersManage)
            .RequireAuthorization(CoarsePolicies.DeviceActive);
        group.MapPost("/staff/access", SaveAccessAsync).RequireAuthorization(PermissionCodes.RolesManage);
        app.MapGet(ServerRoutes.Auth + "/access", CurrentAccessAsync)
            .RequireAuthorization(CoarsePolicies.DeviceActive).RequireRateLimiting(RateLimitPolicies.Sync);

        return app;
    }

    private static int StatusFor(ErrorCode error)
    {
        string code = error.Code;
        if (code == ErrorCodes.ValidationFailed.Code
            || code == ErrorCodes.PasswordPolicyViolation.Code
            || code == ErrorCodes.PermissionUnknown.Code)
        {
            return StatusCodes.Status400BadRequest;
        }

        if (code == ErrorCodes.UsernameExists.Code || code == ErrorCodes.ConcurrencyConflict.Code)
        {
            return StatusCodes.Status409Conflict;
        }

        if (code == ErrorCodes.NotFound.Code)
        {
            return StatusCodes.Status404NotFound;
        }

        if (code == ErrorCodes.PermissionDenied.Code)
        {
            return StatusCodes.Status403Forbidden;
        }

        return AuthEndpoints.StatusFor(error);
    }

    private static IResult Fail(HttpContext http, ErrorCode error)
    {
        string correlationId = CorrelationHeader.Resolve(http);
        CorrelationHeader.Echo(http, correlationId);
        return Results.Json(new WireApiError(error.Code, null, [], correlationId, Category: error.Category), statusCode: StatusFor(error));
    }

    private static IResult NoContent(HttpContext http)
    {
        CorrelationHeader.Echo(http, CorrelationHeader.Resolve(http));
        http.Response.Headers.CacheControl = "no-store";
        return Results.NoContent();
    }

    private static async Task<IResult> CurrentAccessAsync(HttpContext http, IRequestAuthorizationState state, StaffProvisioningService service)
    {
        if (state.Membership is not { } actor) return Fail(http, ErrorCodes.SessionExpired);
        var result = await service.CurrentAccessAsync(actor, http.RequestAborted).ConfigureAwait(false);
        if (result.IsFailure) return Fail(http, result.Error);
        http.Response.Headers.CacheControl = "no-store";
        CorrelationHeader.Echo(http, CorrelationHeader.Resolve(http));
        return Results.Json(result.Value!);
    }

    private static async Task<IResult> ListAsync(HttpContext http, IRequestAuthorizationState state, StaffProvisioningService service)
    {
        if (state.Membership is not { } actor || state.Subject is not { } subject)
        {
            return Fail(http, ErrorCodes.SessionExpired);
        }

        Result<StaffDirectoryWire> result = await service.ListAsync(actor, subject, http.RequestAborted).ConfigureAwait(false);
        if (result.IsFailure)
        {
            return Fail(http, result.Error);
        }

        CorrelationHeader.Echo(http, CorrelationHeader.Resolve(http));
        http.Response.Headers.CacheControl = "no-store";
        return Results.Json(result.Value!);
    }

    private static async Task<IResult> CreateAsync(
        HttpContext http,
        CreateStaffServerRequest? request,
        IRequestAuthorizationState state,
        StaffProvisioningService service)
    {
        if (state.Membership is not { } actor || state.Subject is not { } subject)
        {
            return Fail(http, ErrorCodes.SessionExpired);
        }

        Result result = await service
            .CreateAsync(actor, subject, request, CorrelationHeader.Resolve(http), http.RequestAborted)
            .ConfigureAwait(false);
        return result.IsSuccess ? NoContent(http) : Fail(http, result.Error);
    }

    private static async Task<IResult> SetActiveAsync(
        HttpContext http,
        string id,
        StaffActiveServerRequest? request,
        IRequestAuthorizationState state,
        StaffProvisioningService service)
    {
        if (state.Membership is not { } actor)
        {
            return Fail(http, ErrorCodes.SessionExpired);
        }

        Result result = await service
            .SetActiveAsync(actor, id, request, CorrelationHeader.Resolve(http), http.RequestAborted)
            .ConfigureAwait(false);
        return result.IsSuccess ? NoContent(http) : Fail(http, result.Error);
    }

    private static async Task<IResult> SetPasswordAsync(
        HttpContext http,
        string id,
        SetStaffPasswordServerRequest? request,
        IRequestAuthorizationState state,
        StaffProvisioningService service)
    {
        if (state.Membership is not { } actor || state.Subject is not { } subject)
        {
            return Fail(http, ErrorCodes.SessionExpired);
        }

        Result result = await service
            .SetPasswordAsync(actor, subject, id, request, CorrelationHeader.Resolve(http), http.RequestAborted)
            .ConfigureAwait(false);
        return result.IsSuccess ? NoContent(http) : Fail(http, result.Error);
    }

    private static async Task<IResult> SaveAccessAsync(
        HttpContext http,
        StaffAccessServerRequest? request,
        IRequestAuthorizationState state,
        StaffProvisioningService service)
    {
        if (state.Membership is not { } actor)
        {
            return Fail(http, ErrorCodes.SessionExpired);
        }

        Result result = await service
            .SaveAccessAsync(actor, request, CorrelationHeader.Resolve(http), http.RequestAborted)
            .ConfigureAwait(false);
        return result.IsSuccess ? NoContent(http) : Fail(http, result.Error);
    }
}
