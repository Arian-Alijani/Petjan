using Animora.Application.Abstractions;
using Animora.Application.Auditing;
using Animora.Application.Commands;
using Animora.Application.Ports;
using Animora.Application.Security;
using Animora.Application.Validation;
using Animora.Domain.Identity;
using Animora.Domain.Results;
using Animora.Domain.Time;

namespace Animora.Application.UseCases.Identity;

/// <summary>
/// Creates or refreshes the local credential mirror of a user the <b>central server</b> has just authenticated
/// (TASK-1 / RC-A). Staff are created on the server (<c>/auth/staff</c>), so the local SQLite store has no row for
/// them; without this step a server-created non-owner can pass the online sign-in yet always fail the local one with
/// <c>CREDENTIALS_INVALID</c>. The command is idempotent: an existing user is re-synchronised (password hash only when
/// the stored one no longer verifies, account re-enabled, failed-attempt streak cleared, active membership aligned to
/// the server role), a missing user is created together with an active membership.
///
/// <para><b>Tenant alignment (explicit decision).</b> The local store is a single-tenant replica whose tenant is the
/// installation tenant (<c>context.TenantId</c> = <see cref="Tenant.LocalDefaultId"/> until bootstrap) — the very tenant
/// the local <see cref="SignInCommand"/> and every query resolve against. The mirror is therefore written under
/// <c>context.TenantId</c>, never the server tenant ULID, so the local lookup always matches; the server tenant stays
/// authoritative in the stored online session (<see cref="ISessionStore"/>) and the sync cursor.</para>
///
/// <para>Pre-authentication request (anonymous context) — callers MUST have completed a successful online login first;
/// <c>AuthenticationService</c> enforces this against the stored online session before dispatching.</para>
/// </summary>
[Auditable(AuditCategory.Security, "identity.mirror.provision")]
public sealed record ProvisionLocalMirrorUserCommand(string Username, string DisplayName, string Password, StaffRole Role)
    : ICommand<Unit>, ISelfTransactedCommand
{
    public override string ToString() =>
        $"{nameof(ProvisionLocalMirrorUserCommand)} {{ Username = {Username}, Role = {Role}, Password = *** }}";
}

public sealed class ProvisionLocalMirrorUserCommandValidator : IValidator<ProvisionLocalMirrorUserCommand>
{
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(ProvisionLocalMirrorUserCommand instance,
        CancellationToken ct) =>
        IdentityValidation.Collect(
            IdentityValidation.RequiredMax(instance.Username, IdentityValidation.UsernameMax, nameof(instance.Username)),
            IdentityValidation.RequiredMax(instance.DisplayName, IdentityValidation.NameMax, nameof(instance.DisplayName)),
            IdentityValidation.RequiredMax(instance.Password, IdentityValidation.PasswordMax, nameof(instance.Password)),
            Enum.IsDefined(instance.Role) ? null : new ValidationFailure(nameof(instance.Role), Rules.ValidEnumCode));
}

public sealed class ProvisionLocalMirrorUserCommandHandler(
    IIdentityRepository users,
    IStaffMembershipRepository memberships,
    IRoleDefinitionRepository roles,
    IPasswordHasher hasher,
    IUnitOfWork uow,
    IClock clock) : ICommandHandler<ProvisionLocalMirrorUserCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(ProvisionLocalMirrorUserCommand command, CommandContext context,
        CancellationToken ct)
    {
        // The server already verified the password; the mirror only has to store a verifier of it. The policy is NOT
        // re-applied (a legacy server password must still be mirrorable), but the stored role definition must exist so
        // the capability snapshot issued by the local sign-in cannot fail.
        if (await roles.FindByRoleAsync(context.TenantId, command.Role, ct).ConfigureAwait(false) is null)
            return Result<Unit>.Fail(ErrorCodes.PermissionDenied, "Role definition missing in the local store.");

        var user = await users.FindByUsernameAsync(context.TenantId, command.Username, ct).ConfigureAwait(false);
        if (user is null)
        {
            user = User.Create(context.TenantId, command.Username, command.DisplayName, clock);
            user.SetPasswordHash(hasher.Hash(command.Password));
            users.Add(user);
            memberships.Add(StaffMembership.Create(context.TenantId, user.Id, command.Role, clock));
        }
        else
        {
            RefreshUser(user, command.Password);
            var active = await memberships.FindActiveAsync(user.TenantId, user.Id, ct).ConfigureAwait(false);
            if (active is null)
            {
                memberships.Add(StaffMembership.Create(user.TenantId, user.Id, command.Role, clock));
            }
            else if (active.Role != command.Role)
            {
                // FindActiveAsync is a no-tracking read; re-load by id so the role change is persisted.
                var tracked = await memberships.FindAsync(active.Id, ct).ConfigureAwait(false);
                tracked?.ChangeRole(command.Role);
                user.RotateSecurityStamp(); // a role change invalidates every outstanding capability snapshot.
            }
        }

        var saved = await uow.SaveAsync(ct).ConfigureAwait(false);
        return saved.IsFailure ? Result<Unit>.Fail(saved.Error, saved.Detail) : Result<Unit>.Ok(Unit.Value);
    }

    private void RefreshUser(User user, string password)
    {
        if (user.PasswordHash is null || hasher.Verify(user.PasswordHash, password) == PasswordVerificationOutcome.Failed)
            user.SetPasswordHash(hasher.Hash(password));
        // The server just accepted this user: it is the authority on enablement and lockout.
        if (user.Status != UserStatus.Active || user.IsLockedOut(clock.UtcNow)) user.Enable();
        user.ClearFailures();
    }
}
