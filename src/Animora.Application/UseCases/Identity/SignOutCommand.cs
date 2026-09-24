using Animora.Application.Abstractions;
using Animora.Application.Commands;
using Animora.Application.Security;
using Animora.Domain.Results;

namespace Animora.Application.UseCases.Identity;

/// <summary>Ends the active local session. Idempotent.</summary>
public sealed record SignOutCommand(string Reason) : ICommand<Unit>, ISelfTransactedCommand;

public sealed class SignOutCommandHandler(ISessionService sessions, SessionContextCache cache)
    : ICommandHandler<SignOutCommand, Unit>
{
    public const string DefaultReason = "SIGN_OUT";

    public async Task<Result<Unit>> HandleAsync(SignOutCommand command, CommandContext context, CancellationToken ct)
    {
        var reason = string.IsNullOrWhiteSpace(command.Reason) ? DefaultReason : command.Reason.Trim();
        var ended = await sessions.EndAsync(reason, ct).ConfigureAwait(false);
        if (ended.IsFailure) return Result<Unit>.Fail(ended.Error, ended.Detail);
        cache.Set(SessionSnapshot.SignedOut(isFirstRun: false));
        return Result<Unit>.Ok(Unit.Value);
    }
}
