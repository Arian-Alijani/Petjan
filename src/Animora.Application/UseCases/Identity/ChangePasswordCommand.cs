using Animora.Application.Abstractions;
using Animora.Application.Auditing;
using Animora.Application.Commands;
using Animora.Application.Ports;
using Animora.Application.Security;
using Animora.Application.Validation;
using Animora.Domain.Results;
using Animora.Domain.Time;

namespace Animora.Application.UseCases.Identity;

/// <summary>
/// Changes the signed-in user's password; rotates the security stamp (CONVENTIONS §7).
/// Deliberately NOT <c>[RequiresPermission]</c>: the catalog has no self-password code. The gate requires an active,
/// unlocked, unexpired session matching <c>context.UserId</c>, and the handler only ever targets that user (B04a).
/// </summary>
[Auditable(AuditCategory.Security, "identity.password.change")]
public sealed record ChangePasswordCommand(string Current, string New) : ICommand<Unit>, ISelfTransactedCommand
{
    public override string ToString() => $"{nameof(ChangePasswordCommand)} {{ Current = ***, New = *** }}";
}

public sealed class ChangePasswordCommandValidator : IValidator<ChangePasswordCommand>
{
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(ChangePasswordCommand instance, CancellationToken ct) =>
        IdentityValidation.Collect(
            IdentityValidation.RequiredMax(instance.Current, IdentityValidation.PasswordMax, nameof(instance.Current)),
            IdentityValidation.RequiredMax(instance.New, IdentityValidation.PasswordMax, nameof(instance.New)));
}

/// <summary>
/// Policy on <c>New</c> → verify <c>Current</c> (a wrong one counts as a failed attempt; the 5th locks the account
/// and ends the session) → <see cref="Domain.Identity.User.SetPasswordHash"/>. Self-transacted so a failed attempt
/// still persists its counter.
/// </summary>
public sealed class ChangePasswordCommandHandler(
    IIdentityRepository users,
    IPasswordHasher hasher,
    ISessionService sessions,
    SessionContextCache cache,
    IUnitOfWork uow,
    IClock clock) : ICommandHandler<ChangePasswordCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(ChangePasswordCommand command, CommandContext context, CancellationToken ct)
    {
        var policy = PasswordPolicy.Validate(command.New);
        if (policy.IsFailure) return Result<Unit>.Fail(policy.Error, policy.Detail);

        var user = await users.FindAsync(context.UserId, ct).ConfigureAwait(false);
        if (user is null) return Result<Unit>.Fail(ErrorCodes.SessionExpired);
        if (user.IsLockedOut(clock.UtcNow)) return Result<Unit>.Fail(ErrorCodes.UserLockedOut);

        var outcome = user.PasswordHash is null
            ? PasswordVerificationOutcome.Failed
            : hasher.Verify(user.PasswordHash, command.Current);
        Result saved;
        if (outcome == PasswordVerificationOutcome.Failed)
        {
            var lockedNow = user.RegisterFailedAttempt(clock);
            saved = await uow.SaveAsync(ct).ConfigureAwait(false);
            if (saved.IsFailure) return Result<Unit>.Fail(saved.Error, saved.Detail);
            if (!lockedNow) return Result<Unit>.Fail(ErrorCodes.CredentialsInvalid);

            await sessions.EndAsync(ErrorCodes.UserLockedOut.Code, ct).ConfigureAwait(false);
            cache.Set(SessionSnapshot.SignedOut(isFirstRun: false));
            return Result<Unit>.Fail(ErrorCodes.UserLockedOut);
        }

        user.SetPasswordHash(hasher.Hash(command.New));
        user.ClearFailures();
        saved = await uow.SaveAsync(ct).ConfigureAwait(false);
        return saved.IsFailure ? Result<Unit>.Fail(saved.Error, saved.Detail) : Result<Unit>.Ok(Unit.Value);
    }
}
