using Animora.Application.Abstractions;
using Animora.Application.Commands;
using Animora.Application.Ports;
using Animora.Application.Security;
using Animora.Application.Validation;
using Animora.Domain.Identity;
using Animora.Domain.Results;
using Animora.Domain.Time;

namespace Animora.Application.UseCases.Identity;

/// <summary>Local sign-in with lockout. Not journaled (a sign-in is not business intent).</summary>
public sealed record SignInCommand(string Username, string Password, bool RememberDevice)
    : ICommand<SignInResult>, ISelfTransactedCommand
{
    public override string ToString() => $"{nameof(SignInCommand)} {{ Username = {Username}, Password = *** }}";
}

public sealed record SignInResult(string UserId, string DisplayName, StaffRole Role,
    IReadOnlyList<string> Permissions, bool MustChangePassword, DateTimeOffset ExpiresAtUtc);

public sealed class SignInCommandValidator : IValidator<SignInCommand>
{
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(SignInCommand instance, CancellationToken ct) =>
        IdentityValidation.Collect(
            IdentityValidation.RequiredMax(instance.Username, IdentityValidation.UsernameMax, nameof(instance.Username)),
            IdentityValidation.RequiredMax(instance.Password, IdentityValidation.PasswordMax, nameof(instance.Password)));
}

/// <summary>
/// Order (B03b D2): load by username → lockout/disabled gate (no hash comparison) → verify → on failure count
/// the attempt, audit, <c>CREDENTIALS_INVALID</c> (5th failure → <c>USER_LOCKED_OUT</c>) → on success rehash if
/// needed, clear failures, audit <c>OK</c>, start the session. An unknown user still costs one hash
/// verification so timing does not reveal existence (CONVENTIONS §7).
/// </summary>
public sealed class SignInCommandHandler(
    IIdentityRepository users,
    IStaffMembershipRepository memberships,
    IRoleDefinitionRepository roles,
    ILoginAttemptLog attempts,
    IPasswordHasher hasher,
    ISessionService sessions,
    SessionContextCache cache,
    IUnitOfWork uow,
    IClock clock) : ICommandHandler<SignInCommand, SignInResult>
{
    private static string? _dummyHash;

    public async Task<Result<SignInResult>> HandleAsync(SignInCommand command, CommandContext context, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var user = await users.FindByUsernameAsync(context.TenantId, command.Username, ct).ConfigureAwait(false);

        if (user is null)
        {
            hasher.Verify(_dummyHash ??= hasher.Hash("animora-timing-equalizer-0"), command.Password);
            return await FailAsync(command.Username, ErrorCodes.CredentialsInvalid, context, now, ct).ConfigureAwait(false);
        }

        if (user.IsLockedOut(now))
            return await FailAsync(command.Username, ErrorCodes.UserLockedOut, context, now, ct).ConfigureAwait(false);
        if (user.Status == UserStatus.Disabled)
            return await FailAsync(command.Username, ErrorCodes.UserDisabled, context, now, ct).ConfigureAwait(false);

        var outcome = user.PasswordHash is null
            ? PasswordVerificationOutcome.Failed
            : hasher.Verify(user.PasswordHash, command.Password);
        if (outcome == PasswordVerificationOutcome.Failed)
        {
            var code = user.RegisterFailedAttempt(clock) ? ErrorCodes.UserLockedOut : ErrorCodes.CredentialsInvalid;
            return await FailAsync(command.Username, code, context, now, ct).ConfigureAwait(false);
        }

        var membership = await memberships.FindActiveAsync(user.TenantId, user.Id, ct).ConfigureAwait(false);
        if (membership is null)
            return await FailAsync(command.Username, ErrorCodes.UserDisabled, context, now, ct).ConfigureAwait(false);

        if (outcome == PasswordVerificationOutcome.SuccessRehashNeeded)
        {
            var mustChange = user.MustChangePassword;
            user.SetPasswordHash(hasher.Hash(command.Password));
            if (mustChange) user.RequirePasswordChange();
        }

        user.RegisterSuccessfulLogin(clock);
        attempts.Append(command.Username, ILoginAttemptLog.SuccessOutcome, context.DeviceId, now);
        var saved = await uow.SaveAsync(ct).ConfigureAwait(false);
        if (saved.IsFailure) return Result<SignInResult>.Fail(saved.Error, saved.Detail);

        var session = await sessions.StartAsync(user.Id, context.DeviceId, ct).ConfigureAwait(false);
        if (session.IsFailure) return Result<SignInResult>.Fail(session.Error, session.Detail);
        cache.Set(session.Value);

        var permissions = await IdentityAccess.PermissionsAsync(roles, membership, ct).ConfigureAwait(false);
        return Result<SignInResult>.Ok(new SignInResult(user.Id, user.DisplayName, membership.Role, permissions,
            user.MustChangePassword, session.Value!.ExpiresAtUtc!.Value));
    }

    /// <summary>Audits the attempt and persists it together with any failed-attempt counter on the tracked user.</summary>
    private async Task<Result<SignInResult>> FailAsync(string username, ErrorCode code,
        CommandContext context, DateTimeOffset now, CancellationToken ct)
    {
        attempts.Append(username, code.Code, context.DeviceId, now);
        var saved = await uow.SaveAsync(ct).ConfigureAwait(false);
        return saved.IsFailure ? Result<SignInResult>.Fail(saved.Error, saved.Detail) : Result<SignInResult>.Fail(code);
    }
}
