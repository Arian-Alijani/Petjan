using System.Security.Cryptography;
using System.Text;
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
/// One-time owner setup. Does <b>not</b> create the tenant (seeded by <c>LocalSeeder</c> as
/// <see cref="Tenant.LocalDefaultId"/>); creates the owner user, its Manager membership and the device row,
/// issues the owner's signed offline capability snapshot (B04b) in the same unit-of-work transaction, then starts
/// the session (separate scope/transaction owned by <see cref="ISessionService"/>). Dispatched with an anonymous context.
/// </summary>
[Auditable(AuditCategory.Configuration, "identity.firstrun")]
public sealed record CompleteFirstRunSetupCommand(
    string ClinicName, string OwnerFullName, string Username, string Password, string? ServerUserId = null)
    : ICommand<SignInResult>, ISelfTransactedCommand
{
    public override string ToString() =>
        $"{nameof(CompleteFirstRunSetupCommand)} {{ ClinicName = {ClinicName}, Username = {Username}, Password = *** }}";
}

public sealed class CompleteFirstRunSetupCommandValidator : IValidator<CompleteFirstRunSetupCommand>
{
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(CompleteFirstRunSetupCommand instance,
        CancellationToken ct) =>
        IdentityValidation.Collect(
            IdentityValidation.RequiredMax(instance.ClinicName, IdentityValidation.NameMax, nameof(instance.ClinicName)),
            IdentityValidation.RequiredMax(instance.OwnerFullName, IdentityValidation.NameMax, nameof(instance.OwnerFullName)),
            IdentityValidation.RequiredMax(instance.Username, IdentityValidation.UsernameMax, nameof(instance.Username)),
            IdentityValidation.RequiredMax(instance.Password, IdentityValidation.PasswordMax, nameof(instance.Password)));
}

public sealed class CompleteFirstRunSetupCommandHandler(
    IIdentityRepository users,
    IStaffMembershipRepository memberships,
    IDeviceRepository devices,
    IRoleDefinitionRepository roles,
    ILoginAttemptLog attempts,
    IPasswordHasher hasher,
    CapabilitySnapshotIssuer capabilities,
    ISessionService sessions,
    SessionContextCache cache,
    IUnitOfWork uow,
    IClock clock) : ICommandHandler<CompleteFirstRunSetupCommand, SignInResult>
{
    public async Task<Result<SignInResult>> HandleAsync(CompleteFirstRunSetupCommand command, CommandContext context,
        CancellationToken ct)
    {
        if (await users.AnyUserAsync(ct).ConfigureAwait(false))
            return Result<SignInResult>.Fail(ErrorCodes.FirstRunAlreadyCompleted);

        var policy = PasswordPolicy.Validate(command.Password);
        if (policy.IsFailure) return Result<SignInResult>.Fail(policy.Error, policy.Detail);

        var tenantId = context.TenantId;
        var tenant = await users.FindTenantAsync(tenantId, ct).ConfigureAwait(false);
        tenant?.Rename(command.ClinicName);

        // D01: an owner provisioned from a verified online sign-in keeps the server's user id.
        var owner = string.IsNullOrWhiteSpace(command.ServerUserId)
            ? User.Create(tenantId, command.Username, command.OwnerFullName, clock)
            : User.CreateWithId(command.ServerUserId, tenantId, command.Username, command.OwnerFullName, clock);
        owner.SetPasswordHash(hasher.Hash(command.Password));
        owner.RegisterSuccessfulLogin(clock);
        users.Add(owner);

        var membership = StaffMembership.Create(tenantId, owner.Id, StaffRole.Manager, clock);
        memberships.Add(membership);

        var device = Device.Register(tenantId, owner.Id, Environment.MachineName is { Length: > 0 } m ? m : "local",
            Sha256Hex(context.DeviceId), PlatformMoniker(), clock);
        device.Approve();
        devices.Add(device);

        attempts.Append(command.Username, ILoginAttemptLog.SuccessOutcome, context.DeviceId, clock.UtcNow);
        // Owner, membership, device, audit and the snapshot commit (or roll back) together; without a snapshot the
        // Manager shell opened after first run would be denied offline (B04c gate).
        var saved = await uow.ExecuteAsync(async token =>
        {
            var issued = await capabilities.IssueAsync(owner, membership, context.DeviceId, token).ConfigureAwait(false);
            return issued.IsFailure ? Result<bool>.Fail(issued.Error, issued.Detail) : Result<bool>.Ok(true);
        }, ct).ConfigureAwait(false);
        if (saved.IsFailure) return Result<SignInResult>.Fail(saved.Error, saved.Detail);

        var session = await sessions.StartAsync(owner.Id, context.DeviceId, ct).ConfigureAwait(false);
        if (session.IsFailure) return Result<SignInResult>.Fail(session.Error, session.Detail);
        cache.Set(session.Value);

        var permissions = await IdentityAccess.PermissionsAsync(roles, membership, ct).ConfigureAwait(false);
        return Result<SignInResult>.Ok(new SignInResult(owner.Id, owner.DisplayName, membership.Role, permissions,
            MustChangePassword: false, session.Value!.ExpiresAtUtc!.Value));
    }

    private static string Sha256Hex(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string PlatformMoniker() =>
        OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : "linux";
}
