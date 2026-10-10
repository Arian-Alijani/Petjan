using Animora.Domain.Identity;
using Animora.Domain.Results;
using Animora.Persistence.PostgreSql.Tenancy;
using Animora.Server.Operator;
using Microsoft.AspNetCore.Authorization;

namespace Animora.Server.Authorization;

/// <summary>Names of the coarse (non-permission) authorization policies.</summary>
internal static class CoarsePolicies
{
    /// <summary>Authenticated and bound to a tenant by the tenant middleware.</summary>
    public const string TenantMember = "TenantMember";

    /// <summary>Stored role is <see cref="StaffRole.Manager"/>.</summary>
    public const string ManagerOnly = "ManagerOnly";

    /// <summary>
    /// Vendor operator. <b>Denies everyone</b> until S14 introduces an operator identity: no operator principal is
    /// ever issued, so the requirement can never be satisfied.
    /// </summary>
    public const string OperatorOnly = "OperatorOnly";

    /// <summary>The caller's device is <c>Active</c> (a <c>Pending</c> device is rejected).</summary>
    public const string DeviceActive = "DeviceActive";

    /// <summary>All coarse names.</summary>
    public static readonly IReadOnlyList<string> All = [TenantMember, ManagerOnly, OperatorOnly, DeviceActive];
}

/// <summary>Requires the stored role of the active membership to be Manager.</summary>
internal sealed record ManagerOnlyRequirement : IAuthorizationRequirement;

/// <summary>Requires the caller's device to be Active.</summary>
internal sealed record DeviceActiveRequirement : IAuthorizationRequirement;

/// <summary>Requires a vendor-operator identity (none exists before S14 → never satisfied).</summary>
internal sealed record OperatorOnlyRequirement : IAuthorizationRequirement;

/// <summary>Evaluates <see cref="ManagerOnlyRequirement"/> from the stored role (never the <c>role</c> claim).</summary>
internal sealed class ManagerOnlyHandler(IRequestAuthorizationState state) : AuthorizationHandler<ManagerOnlyRequirement>
{
    /// <inheritdoc />
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, ManagerOnlyRequirement requirement)
    {
        if (state.Membership is { Role: StaffRole.Manager })
        {
            context.Succeed(requirement);
        }
        else
        {
            context.Fail(new AuthorizationFailureReason(this, PermissionAuthorizationHandler.ReasonPrefix + ErrorCodes.PermissionDenied.Code));
        }

        return Task.CompletedTask;
    }
}

/// <summary>Evaluates <see cref="DeviceActiveRequirement"/> from the resolved device state.</summary>
internal sealed class DeviceActiveHandler(IRequestAuthorizationState state) : AuthorizationHandler<DeviceActiveRequirement>
{
    /// <inheritdoc />
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, DeviceActiveRequirement requirement)
    {
        if (state.Membership is { DeviceActive: true })
        {
            context.Succeed(requirement);
        }
        else
        {
            context.Fail(new AuthorizationFailureReason(this, PermissionAuthorizationHandler.ReasonPrefix + ErrorCodes.DeviceRevoked.Code));
        }

        return Task.CompletedTask;
    }
}

/// <summary>Requires the separate operator identity and a validated operator request context.</summary>
internal sealed class OperatorOnlyHandler(ITenantContext tenantContext) : AuthorizationHandler<OperatorOnlyRequirement>
{
    /// <inheritdoc />
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, OperatorOnlyRequirement requirement)
    {
        if (tenantContext.IsOperator && context.User.Identities.Any(i => i.IsAuthenticated && i.AuthenticationType == OperatorAuth.Scheme)
            && context.User.IsInRole("operator")) context.Succeed(requirement);
        else context.Fail(new AuthorizationFailureReason(this, PermissionAuthorizationHandler.ReasonPrefix + ErrorCodes.PermissionDenied.Code));
        return Task.CompletedTask;
    }
}
