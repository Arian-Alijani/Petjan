using Animora.Application.Abstractions;
using Animora.Application.Commands;
using Animora.Application.Security;
using Animora.Application.Validation;
using Animora.Domain.Results;

namespace Animora.Application.UseCases.Identity;

/// <summary>Re-authenticates the locked session's user. Dispatched with an anonymous context.</summary>
public sealed record UnlockSessionCommand(string Password) : ICommand<SessionSnapshot>, ISelfTransactedCommand
{
    public override string ToString() => $"{nameof(UnlockSessionCommand)} {{ Password = *** }}";
}

public sealed class UnlockSessionCommandValidator : IValidator<UnlockSessionCommand>
{
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(UnlockSessionCommand instance, CancellationToken ct) =>
        IdentityValidation.Collect(
            IdentityValidation.RequiredMax(instance.Password, IdentityValidation.PasswordMax, nameof(instance.Password)));
}

public sealed class UnlockSessionCommandHandler(ISessionService sessions, SessionContextCache cache)
    : ICommandHandler<UnlockSessionCommand, SessionSnapshot>
{
    public async Task<Result<SessionSnapshot>> HandleAsync(UnlockSessionCommand command, CommandContext context,
        CancellationToken ct)
    {
        var unlocked = await sessions.UnlockAsync(command.Password, ct).ConfigureAwait(false);
        if (unlocked.IsSuccess)
        {
            cache.Set(unlocked.Value);
        }
        else if (unlocked.Error == ErrorCodes.SessionExpired || unlocked.Error == ErrorCodes.UserLockedOut
                 || unlocked.Error == ErrorCodes.UserDisabled || unlocked.Error == ErrorCodes.NotFound)
        {
            cache.Set(SessionSnapshot.SignedOut(isFirstRun: false)); // the session service ended (or lost) the session
        }

        return unlocked;
    }
}
