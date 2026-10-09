using Animora.Application.Security;
using Animora.Domain.Results;
using Microsoft.AspNetCore.Authorization;

namespace Animora.Server.Authorization;

/// <summary>
/// Bridges <see cref="PermissionRequirement"/> to the Application <see cref="IPermissionEvaluator"/>. The subject
/// is the one the tenant middleware resolved from the stored membership; the evaluator recomputes grants from the
/// store, so neither token claims nor the subject's advisory permission set decide anything. Fails closed.
/// </summary>
internal sealed class PermissionAuthorizationHandler(
    IPermissionEvaluator evaluator,
    IRequestAuthorizationState state) : AuthorizationHandler<PermissionRequirement>
{
    /// <summary>Failure-reason prefix carrying the stable <c>ErrorCodes.*</c> value to the result handler.</summary>
    internal const string ReasonPrefix = "code:";

    /// <inheritdoc />
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        AuthorizationSubject? subject = state.Subject;
        if (subject is null)
        {
            context.Fail(new AuthorizationFailureReason(this, ReasonPrefix + ErrorCodes.SessionExpired.Code));
            return;
        }

        CancellationToken ct = (context.Resource as HttpContext)?.RequestAborted ?? CancellationToken.None;
        AuthorizationDecision decision = await evaluator
            .EvaluateAsync(requirement.PermissionCode, subject, resource: null, ct)
            .ConfigureAwait(false);

        if (decision.Allowed)
        {
            context.Succeed(requirement);
            return;
        }

        ErrorCode error = decision.DeniedByRule == PermissionEvaluator.UnknownRule
            ? ErrorCodes.PermissionUnknown
            : ErrorCodes.PermissionDenied;
        context.Fail(new AuthorizationFailureReason(this, ReasonPrefix + error.Code));
    }
}
