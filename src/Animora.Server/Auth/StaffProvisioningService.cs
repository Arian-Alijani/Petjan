using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Animora.Application.Ports;
using Animora.Application.Security;
using Animora.Contracts.Wire.Auth;
using Animora.Domain.Identity;
using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Animora.Domain.Time;
using Animora.Persistence.PostgreSql.Identity;
using IIdentityRepository = Animora.Persistence.PostgreSql.Identity.IIdentityRepository;
using Animora.Server.Authorization;
using Microsoft.AspNetCore.Identity;

namespace Animora.Server.Auth;

/// <summary>
/// Online staff (clinic user) provisioning: create a user + membership, enable/disable a membership, replace a
/// membership's permission overrides, and read the roster. Callers are already authenticated, tenant-bound and
/// permission-gated by the endpoint pipeline; the tenant is ALWAYS the caller's resolved tenant (never a request
/// value). Business rules mirror the local <c>StaffManagement</c> command handlers so behaviour is identical online
/// and offline. Every mutation commits together with an append-only audit row in one transaction. Failures are
/// <see cref="Result"/> values, never exceptions. The password is hashed with the same server hasher/adapter shape
/// <see cref="LoginService"/> verifies against, so a created user can sign in immediately.
/// </summary>
internal sealed class StaffProvisioningService(
    IIdentityRepository repository,
    IRoleDefinitionRepository roles,
    IPermissionEvaluator permissions,
    IPasswordHasher<ServerIdentityUser> hasher,
    ISecurityAuditWriter audit,
    IClock clock)
{
    /// <summary>Audit action: a staff user was created.</summary>
    public const string StaffCreatedAction = "auth.staff.created";

    /// <summary>Audit action: a staff membership was enabled or disabled.</summary>
    public const string StaffStatusAction = "auth.staff.status";

    /// <summary>Audit action: a staff membership's permission overrides changed.</summary>
    public const string StaffPermissionsAction = "auth.staff.permissions";

    private const int MaxName = 200;
    private const int MaxUserName = 128;
    private const int MaxPassword = 256;
    private const int MaxPhone = 30;

    /// <summary>Creates a user and an active membership in the actor's tenant.</summary>
    public async Task<Result> CreateAsync(
        ResolvedMembership actor,
        AuthorizationSubject subject,
        CreateStaffServerRequest? request,
        string correlationId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(subject);

        if (request is null
            || string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > MaxName
            || string.IsNullOrWhiteSpace(request.Username) || request.Username.Length > MaxUserName
            || request.Password is null || request.Password.Length > MaxPassword
            || request.Phone is null || request.Phone.Length > MaxPhone
            || !Enum.TryParse(request.Role, ignoreCase: true, out StaffRole role) || !Enum.IsDefined(role))
        {
            return Result.Fail(ErrorCodes.ValidationFailed);
        }

        Result policy = PasswordPolicy.Validate(request.Password);
        if (policy.IsFailure)
        {
            return Result.Fail(policy.Error, policy.Detail);
        }

        string userName = request.Username.Trim().ToLowerInvariant();
        if (await repository.FindUserByNameAsync(userName, ct).ConfigureAwait(false) is not null)
        {
            return Result.Fail(ErrorCodes.UsernameExists);
        }

        if (await roles.FindByRoleAsync(actor.TenantId, role, ct).ConfigureAwait(false) is null)
        {
            return Result.Fail(ErrorCodes.PermissionDenied);
        }

        // Creating another administrator also requires permission to grant authority.
        if (role == StaffRole.Manager)
        {
            AuthorizationDecision grant = await permissions
                .EvaluateAsync(PermissionCodes.RolesManage, subject, null, ct).ConfigureAwait(false);
            if (!grant.Allowed)
            {
                return Result.Fail(ErrorCodes.PermissionDenied);
            }
        }

        var user = User.Create(actor.TenantId, userName, request.Name, clock);
        user.SetPasswordHash(hasher.HashPassword(ServerUserStore.ToAdapter(user), request.Password));
        repository.AddUser(user);
        repository.AddMembership(StaffMembership.Create(actor.TenantId, user.Id, role, clock));

        // NOTE: the contact phone is a client-side profile preference; the server keeps no preference store,
        // so it is intentionally not persisted here.
        return await AuditAsync(actor, StaffCreatedAction, correlationId, user.Id, ct).ConfigureAwait(false);
    }

    /// <summary>Enables or disables a staff membership (and its user).</summary>
    public async Task<Result> SetActiveAsync(
        ResolvedMembership actor,
        string membershipId,
        StaffActiveServerRequest? request,
        string correlationId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(actor);

        if (request is null
            || string.IsNullOrWhiteSpace(membershipId)
            || (!string.IsNullOrEmpty(request.MembershipId) && request.MembershipId != membershipId))
        {
            return Result.Fail(ErrorCodes.ValidationFailed);
        }

        StaffMembership? member = await repository.FindMembershipByIdAsync(membershipId, ct).ConfigureAwait(false);
        if (member is null || member.TenantId != actor.TenantId || member.Status == MembershipStatus.Ended)
        {
            return Result.Fail(ErrorCodes.NotFound);
        }

        // Preserve administrator recovery and prohibit self-disable; historical references are never deleted.
        if (member.Role == StaffRole.Manager || member.UserId == actor.UserId)
        {
            return Result.Fail(ErrorCodes.PermissionDenied);
        }

        User? user = await repository.FindUserByIdAsync(member.UserId, ct).ConfigureAwait(false);
        if (user is null || user.TenantId != actor.TenantId)
        {
            return Result.Fail(ErrorCodes.NotFound);
        }

        if (request.ExpectedChangeToken is { } token && token != ChangeToken(user, member))
        {
            return Result.Fail(ErrorCodes.ConcurrencyConflict);
        }

        if (request.Active)
        {
            member.Reinstate();
            user.Enable();
        }
        else
        {
            member.Suspend();
            user.Disable();
        }

        return await AuditAsync(actor, StaffStatusAction, correlationId, user.Id, ct).ConfigureAwait(false);
    }

    /// <summary>Replaces a membership's effective permission set by recording grant/deny overrides over the role.</summary>
    public async Task<Result> SaveAccessAsync(
        ResolvedMembership actor,
        StaffAccessServerRequest? request,
        string correlationId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(actor);

        if (request is null || string.IsNullOrWhiteSpace(request.MembershipId))
        {
            return Result.Fail(ErrorCodes.ValidationFailed);
        }

        if (request.PermissionCodes is null
            || request.PermissionCodes.Count > PermissionCodes.All.Count
            || request.PermissionCodes.Any(code => !PermissionCodes.Exists(code)))
        {
            return Result.Fail(ErrorCodes.PermissionUnknown);
        }

        StaffMembership? member = await repository.FindMembershipByIdAsync(request.MembershipId, ct).ConfigureAwait(false);
        if (member is null || member.TenantId != actor.TenantId)
        {
            return Result.Fail(ErrorCodes.NotFound);
        }

        if (member.Role == StaffRole.Manager || !member.IsActive || member.UserId == actor.UserId)
        {
            return Result.Fail(ErrorCodes.PermissionDenied);
        }

        User? user = await repository.FindUserByIdAsync(member.UserId, ct).ConfigureAwait(false);
        RoleDefinition? role = await roles.FindByRoleAsync(actor.TenantId, member.Role, ct).ConfigureAwait(false);
        if (user is null || user.TenantId != actor.TenantId || user.Status == UserStatus.Disabled || role is null)
        {
            return Result.Fail(ErrorCodes.PermissionDenied);
        }

        if (request.ExpectedChangeToken is { } token && token != ChangeToken(user, member))
        {
            return Result.Fail(ErrorCodes.ConcurrencyConflict);
        }

        var granted = new HashSet<string>(request.PermissionCodes, StringComparer.Ordinal);
        foreach (string code in PermissionCodes.All)
        {
            member.ClearOverride(code);
            if (granted.Contains(code) && !role.Has(code))
            {
                member.GrantExtra(code);
            }
            else if (!granted.Contains(code) && role.Has(code))
            {
                member.Deny(code);
            }
        }

        // Invalidate every outstanding token/capability snapshot so the new access applies on the next sign-in.
        user.RotateSecurityStamp();
        return await AuditAsync(actor, StaffPermissionsAction, correlationId, user.Id, ct).ConfigureAwait(false);
    }

    /// <summary>Reads the tenant roster, role catalogue and the caller's management capabilities.</summary>
    public async Task<Result<StaffDirectoryWire>> ListAsync(
        ResolvedMembership actor,
        AuthorizationSubject subject,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(subject);

        bool canUsers = (await permissions.EvaluateAsync(PermissionCodes.UsersManage, subject, null, ct).ConfigureAwait(false)).Allowed;
        bool canAccess = (await permissions.EvaluateAsync(PermissionCodes.RolesManage, subject, null, ct).ConfigureAwait(false)).Allowed;
        bool canDevices = (await permissions.EvaluateAsync(PermissionCodes.DevicesManage, subject, null, ct).ConfigureAwait(false)).Allowed;
        if (!canUsers && !canAccess)
        {
            return Result<StaffDirectoryWire>.Ok(new StaffDirectoryWire([], [], false, false));
        }

        // The device roster is what lets a Manager approve a staff member's Pending device (TASK-3); it is only disclosed to devices.manage holders.
        StaffDeviceWire[] devices = canDevices
            ? (await repository.ListDevicesAsync(actor.TenantId, ct).ConfigureAwait(false))
                .Select(d => new StaffDeviceWire(d.Id, d.UserId, d.DeviceName, d.Platform, d.Status.ToString()))
                .ToArray()
            : [];

        var definitions = new Dictionary<StaffRole, string[]>();
        foreach (StaffRole role in Enum.GetValues<StaffRole>())
        {
            RoleDefinition? stored = await roles.FindByRoleAsync(actor.TenantId, role, ct).ConfigureAwait(false);
            if (stored is not null)
            {
                definitions[role] = stored.PermissionCodes.Order(StringComparer.Ordinal).ToArray();
            }
        }

        var members = new List<StaffAccountWire>();
        foreach ((User user, StaffMembership membership) in await repository.ListStaffAsync(actor.TenantId, ct).ConfigureAwait(false))
        {
            var effective = new SortedSet<string>(
                definitions.GetValueOrDefault(membership.Role) ?? [],
                StringComparer.Ordinal);
            effective.UnionWith(membership.ExtraPermissionCodes);
            effective.ExceptWith(membership.DeniedPermissionCodes);

            members.Add(new StaffAccountWire(
                membership.Id,
                user.Id,
                user.DisplayName,
                user.UserName,
                membership.Role.ToString(),
                membership.IsActive && user.Status != UserStatus.Disabled,
                membership.Role == StaffRole.Manager || user.Id == actor.UserId,
                effective.ToArray(),
                ChangeToken(user, membership)));
        }

        StaffRoleOptionWire[] options = definitions
            .OrderBy(pair => pair.Key)
            .Select(pair => new StaffRoleOptionWire(pair.Key.ToString(), pair.Value))
            .ToArray();
        return Result<StaffDirectoryWire>.Ok(new StaffDirectoryWire(members, options, canUsers, canAccess, devices, canDevices));
    }

    /// <summary>Opaque edit precondition over security facts (same construction as the local roster's token).</summary>
    internal static string ChangeToken(User user, StaffMembership membership)
    {
        string facts = JsonSerializer.Serialize(new
        {
            membership.Id,
            membership.Role,
            MembershipStatus = membership.Status,
            UserStatus = user.Status,
            user.SecurityStamp,
            Extra = membership.ExtraPermissionCodes.Order(StringComparer.Ordinal).ToArray(),
            Denied = membership.DeniedPermissionCodes.Order(StringComparer.Ordinal).ToArray(),
        });
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(facts)));
    }

    private Task<Result> AuditAsync(ResolvedMembership actor, string action, string correlationId, string targetUserId, CancellationToken ct)
    {
        SecurityAuditEntry entry = SecurityAuditEntry.Create(
            IdGenerator.NewId(clock.UtcNow),
            actor.TenantId,
            clock.UtcNow,
            SecurityAuditActions.Category,
            action,
            SecurityAuditActions.OutcomeSuccess,
            correlationId.Length <= 64 ? correlationId : correlationId[..64],
            actor.UserId,
            actor.DeviceId,
            SecurityAuditDetail.Create(("target", targetUserId)));

        // Commits every change staged on the unit of work (user / membership / stamp) together with the audit row.
        return audit.WriteAsync(entry, ct);
    }
}
