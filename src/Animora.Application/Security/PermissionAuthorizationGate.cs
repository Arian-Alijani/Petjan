using System.Collections.Frozen;
using Animora.Application.Abstractions;
using Animora.Application.Dispatch;
using Animora.Application.Ports;
using Animora.Application.UseCases.Identity;
using Animora.Domain.Results;
using Animora.Domain.Time;

namespace Animora.Application.Security;

/// <summary>
/// The real authorization plane at the dispatcher (replaces allow-all). Per request:
/// <list type="number">
/// <item>resolve the registration-time permission code from <see cref="HandlerRegistry"/> (no attribute reads here);
///       an unregistered request is denied;</item>
/// <item>the pre-authentication requests (<see cref="SignInCommand"/>, <see cref="CompleteFirstRunSetupCommand"/>, <see cref="ProvisionLocalMirrorUserCommand"/>,
///       <see cref="UnlockSessionCommand"/>, <see cref="CurrentSessionQuery"/>) pass ONLY with the anonymous identity
///       of the local installation tenant (and device, for commands);</item>
/// <item>everything else needs an active, unlocked, unexpired session whose user equals the context user, on the
///       installation tenant/device;</item>
/// <item>unannotated → authenticated-only success; annotated → <see cref="IPermissionEvaluator"/>.</item>
/// </list>
/// No allow-all fallback.
/// </summary>
public sealed class PermissionAuthorizationGate(
    HandlerRegistry registry,
    SessionContextCache sessions,
    ILocalInstallationIdentity installation,
    IPermissionEvaluator evaluator,
    IStaffMembershipRepository memberships,
    IClock clock) : IAuthorizationGate
{
    public ValueTask<Result> EvaluateAsync(object command, CommandContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        return EvaluateCoreAsync(command, context.TenantId, context.UserId, context.DeviceId, ct);
    }

    public ValueTask<Result> EvaluateAsync(object query, QueryContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        // Queries carry no device: the device identity is the local installation's.
        return EvaluateCoreAsync(query, context.TenantId, context.UserId, installation.DeviceId, ct);
    }

    private async ValueTask<Result> EvaluateCoreAsync(object request, string tenantId, string userId, string deviceId,
        CancellationToken ct)
    {
        if (request is null) return Result.Fail(ErrorCodes.PermissionDenied, "No request.");
        var requestType = request.GetType();
        if (!registry.TryGet(requestType, out var entry))
            return Result.Fail(ErrorCodes.PermissionDenied, "Unregistered request.");

        var onInstallation = Same(tenantId, installation.TenantId) && Same(deviceId, installation.DeviceId);
        if (!onInstallation) return Result.Fail(ErrorCodes.TenantMismatch);

        if (PreAuthenticationRequests.Types.Contains(requestType)
            && string.Equals(userId, ICurrentUserProvider.AnonymousUserId, StringComparison.Ordinal))
            return Result.Ok();

        var session = sessions.Current;
        if (session?.UserId is not { Length: > 0 } sessionUser
            || session.ExpiresAtUtc is not { } expires || clock.UtcNow >= expires)
            return Result.Fail(ErrorCodes.SessionExpired);
        if (session.IsLocked) return Result.Fail(ErrorCodes.PermissionDenied, "Session is locked.");
        if (!Same(sessionUser, userId)) return Result.Fail(ErrorCodes.PermissionDenied, "Session user mismatch.");

        // Sign-out must remain possible after administrative suspension. Every other
        // authenticated-only request also needs a live membership, not merely an old session.
        if (request is SignOutCommand) return Result.Ok();
        if (entry.PermissionCode is not { } code)
        {
            var member = await memberships.FindActiveAsync(tenantId, userId, ct).ConfigureAwait(false);
            return member is { IsActive: true } ? Result.Ok() : Result.Fail(ErrorCodes.PermissionDenied);
        }
        if (session.Role is not { } role) return Result.Fail(ErrorCodes.PermissionDenied, PermissionEvaluator.RbacRule);

        var subject = new AuthorizationSubject(tenantId, userId, FrozenSet<string>.Empty, role, deviceId, IsOffline: false);
        var decision = await evaluator.EvaluateAsync(code, subject, resource: null, ct).ConfigureAwait(false);
        if (decision.Allowed) return Result.Ok();
        return string.Equals(decision.DeniedByRule, PermissionEvaluator.UnknownRule, StringComparison.Ordinal)
            ? Result.Fail(ErrorCodes.PermissionUnknown, code)
            : Result.Fail(ErrorCodes.PermissionDenied, decision.DeniedByRule);
    }

    private static bool Same(string? a, string? b) =>
        !string.IsNullOrEmpty(a) && string.Equals(a, b, StringComparison.Ordinal);
}
