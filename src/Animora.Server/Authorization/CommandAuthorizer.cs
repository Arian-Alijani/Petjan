using Animora.Application.Security;
using Animora.Domain.Results;

namespace Animora.Server.Authorization;

/// <summary>Authorizes a wire command type against the stored RBAC of the caller.</summary>
internal interface ICommandAuthorizer
{
    /// <summary>
    /// <c>Ok</c> when the subject holds the permission mapped to <paramref name="commandType"/>; otherwise a
    /// <c>PermissionDenied</c> / <c>PermissionUnknown</c> failure.
    /// </summary>
    Task<Result> AuthorizeAsync(string commandType, AuthorizationSubject subject, CancellationToken ct = default);
}

/// <inheritdoc cref="ICommandAuthorizer" />
internal sealed class CommandAuthorizer(IPermissionEvaluator evaluator) : ICommandAuthorizer
{
    /// <inheritdoc />
    public async Task<Result> AuthorizeAsync(string commandType, AuthorizationSubject subject, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(subject);

        string? required = CommandPermissionTable.RequiredFor(commandType);
        if (required is null)
        {
            // Fail closed: a command type that has no declared permission is never executable.
            return Result.Fail(ErrorCodes.PermissionUnknown, "command_unmapped");
        }

        AuthorizationDecision decision = await evaluator.EvaluateAsync(required, subject, resource: null, ct).ConfigureAwait(false);
        if (decision.Allowed)
        {
            return Result.Ok();
        }

        return decision.DeniedByRule == PermissionEvaluator.UnknownRule
            ? Result.Fail(ErrorCodes.PermissionUnknown)
            : Result.Fail(ErrorCodes.PermissionDenied);
    }
}
