using Animora.Application.Abstractions;
using Animora.Application.Auditing;
using Animora.Application.Ports;
using Animora.Application.Security;
using Animora.Domain.Identity;
using Animora.Domain.Results;
using Animora.Domain.Time;

namespace Animora.Application.UseCases.Profile;

public sealed record StaffDirectoryRow(User User, StaffMembership Membership);
public sealed record StaffAccessRow(string MembershipId, string UserId, string Name, string Username,
    StaffRole Role, bool IsActive, IReadOnlyList<string> Permissions, string ChangeToken);
public sealed record StaffDirectoryResult(IReadOnlyList<StaffAccessRow> Members,
    IReadOnlyDictionary<StaffRole, IReadOnlyList<string>> Roles, bool CanManageUsers, bool CanManageAccess);

// The directory is authenticated-only; its handler independently gates the sensitive roster.
public sealed record StaffDirectoryQuery : IQuery<StaffDirectoryResult>;
public sealed class StaffDirectoryQueryHandler(IProfileQueries queries, IRoleDefinitionRepository roles,
    IStaffMembershipRepository memberships, IPermissionEvaluator permissions)
    : IQueryHandler<StaffDirectoryQuery, StaffDirectoryResult>
{
    public async Task<Result<StaffDirectoryResult>> HandleAsync(StaffDirectoryQuery query, QueryContext context,
        CancellationToken ct)
    {
        var actor = await memberships.FindActiveAsync(context.TenantId, context.UserId, ct);
        if (actor is null) return Result<StaffDirectoryResult>.Fail(ErrorCodes.PermissionDenied);
        var subject = new AuthorizationSubject(context.TenantId, context.UserId, new HashSet<string>(),
            actor.Role, string.Empty, false);
        var canUsers = (await permissions.EvaluateAsync(PermissionCodes.UsersManage, subject, null, ct)).Allowed;
        var canAccess = (await permissions.EvaluateAsync(PermissionCodes.RolesManage, subject, null, ct)).Allowed;
        var definitions = new Dictionary<StaffRole, IReadOnlyList<string>>();
        if (!canUsers && !canAccess)
            return Result<StaffDirectoryResult>.Ok(new([], definitions, false, false));
        foreach (var role in Enum.GetValues<StaffRole>())
        {
            var stored = await roles.FindByRoleAsync(context.TenantId, role, ct);
            if (stored is not null) definitions[role] = stored.PermissionCodes.Order(StringComparer.Ordinal).ToArray();
        }
        var members = new List<StaffAccessRow>();
        foreach (var row in await queries.StaffAsync(context.TenantId, ct))
        {
            var granted = new SortedSet<string>(definitions.GetValueOrDefault(row.Membership.Role) ?? [],
                StringComparer.Ordinal);
            granted.UnionWith(row.Membership.ExtraPermissionCodes);
            granted.ExceptWith(row.Membership.DeniedPermissionCodes);
            members.Add(new(row.Membership.Id, row.User.Id, row.User.DisplayName, row.User.UserName,
                row.Membership.Role, row.Membership.IsActive && row.User.Status != UserStatus.Disabled,
                granted.ToArray(), StaffChangeToken.For(row.User, row.Membership)));
        }
        return Result<StaffDirectoryResult>.Ok(new(members, definitions, canUsers, canAccess));
    }
}

[RequiresPermission(PermissionCodes.UsersManage)]
[Auditable(AuditCategory.Configuration, "identity.staff.create")]
public sealed record CreateStaffCommand(string Name, string Username, string Password, StaffRole Role, string Phone)
    : ICommand<Unit>
{
    public override string ToString() => $"{nameof(CreateStaffCommand)} {{ Username = {Username}, Password = *** }}";
}
public sealed class CreateStaffCommandHandler(IIdentityRepository users, IStaffMembershipRepository memberships,
    IRoleDefinitionRepository roles, IProfilePreferenceWriter preferences, IPasswordHasher hasher, IClock clock)
    : ICommandHandler<CreateStaffCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(CreateStaffCommand command, CommandContext context, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command.Name) || command.Name.Length > 200
            || string.IsNullOrWhiteSpace(command.Username) || command.Username.Length > 128
            || command.Password is null || command.Password.Length > 256 || command.Phone is null || command.Phone.Length > 30
            || !Enum.IsDefined(command.Role))
            return Result<Unit>.Fail(ErrorCodes.ValidationFailed);
        var policy = PasswordPolicy.Validate(command.Password);
        if (policy.IsFailure) return Result<Unit>.Fail(policy.Error, policy.Detail);
        if (await users.FindByUsernameAsync(context.TenantId, command.Username, ct) is not null)
            return Result<Unit>.Fail(ErrorCodes.UsernameExists);
        if (await roles.FindByRoleAsync(context.TenantId, command.Role, ct) is null)
            return Result<Unit>.Fail(ErrorCodes.PermissionDenied);
        // Creating another administrator also requires permission to grant authority.
        if (command.Role == StaffRole.Manager)
        {
            var actor = await memberships.FindActiveAsync(context.TenantId, context.UserId, ct);
            var role = actor is null ? null : await roles.FindByRoleAsync(context.TenantId, actor.Role, ct);
            if (actor is null || role is null || actor.DeniedPermissionCodes.Contains(PermissionCodes.RolesManage)
                || (!role.Has(PermissionCodes.RolesManage) && !actor.ExtraPermissionCodes.Contains(PermissionCodes.RolesManage)))
                return Result<Unit>.Fail(ErrorCodes.PermissionDenied);
        }
        var user = User.Create(context.TenantId, command.Username, command.Name, clock);
        user.SetPasswordHash(hasher.Hash(command.Password));
        users.Add(user);
        memberships.Add(StaffMembership.Create(context.TenantId, user.Id, command.Role, clock));
        await preferences.UpsertAsync(context.TenantId, user.Id, AppSettingsKeys.ProfilePhone, command.Phone.Trim(), ct);
        return Result<Unit>.Ok(Unit.Value); // TransactionBehavior commits identity, membership and contact atomically.
    }
}

[RequiresPermission(PermissionCodes.UsersManage)]
[Auditable(AuditCategory.Configuration, "identity.staff.status")]
public sealed record SetStaffActiveCommand(string MembershipId, bool Active, string? ExpectedChangeToken = null) : ICommand<Unit>;
public sealed class SetStaffActiveCommandHandler(IIdentityRepository users, IStaffMembershipRepository memberships)
    : ICommandHandler<SetStaffActiveCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(SetStaffActiveCommand command, CommandContext context, CancellationToken ct)
    {
        var member = await memberships.FindAsync(command.MembershipId, ct);
        if (member is null || member.TenantId != context.TenantId || member.Status == MembershipStatus.Ended)
            return Result<Unit>.Fail(ErrorCodes.NotFound);
        // Preserve administrator recovery and prohibit self-disable; never delete historical clinical references.
        if (member.Role == StaffRole.Manager || member.UserId == context.UserId)
            return Result<Unit>.Fail(ErrorCodes.PermissionDenied);
        var user = await users.FindAsync(member.UserId, ct);
        if (user is null || user.TenantId != context.TenantId) return Result<Unit>.Fail(ErrorCodes.NotFound);
        if (command.ExpectedChangeToken is { } token && token != StaffChangeToken.For(user, member))
            return Result<Unit>.Fail(ErrorCodes.ConcurrencyConflict);
        if (command.Active) { member.Reinstate(); user.Enable(); }
        else { member.Suspend(); user.Disable(); }
        return Result<Unit>.Ok(Unit.Value);
    }
}

[RequiresPermission(Animora.Domain.Identity.PermissionCodes.RolesManage)]
[Auditable(AuditCategory.Configuration, "identity.staff.permissions")]
public sealed record SaveStaffAccessCommand(string MembershipId, IReadOnlyList<string> PermissionCodes, string? ExpectedChangeToken = null) : ICommand<Unit>;
public sealed class SaveStaffAccessCommandHandler(IIdentityRepository users, IStaffMembershipRepository memberships,
    IRoleDefinitionRepository roles, CapabilitySnapshotIssuer capabilities, IAuditWriter audit, IClock clock)
    : ICommandHandler<SaveStaffAccessCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(SaveStaffAccessCommand command, CommandContext context, CancellationToken ct)
    {
        if (command.PermissionCodes is null || command.PermissionCodes.Count > PermissionCodes.All.Count
            || command.PermissionCodes.Any(code => string.IsNullOrWhiteSpace(code) || !PermissionCodes.Exists(code)))
            return Result<Unit>.Fail(ErrorCodes.PermissionUnknown);
        var member = await memberships.FindAsync(command.MembershipId, ct);
        if (member is null || member.TenantId != context.TenantId) return Result<Unit>.Fail(ErrorCodes.NotFound);
        if (member.Role == StaffRole.Manager || !member.IsActive || member.UserId == context.UserId)
            return Result<Unit>.Fail(ErrorCodes.PermissionDenied);
        var user = await users.FindAsync(member.UserId, ct);
        var role = await roles.FindByRoleAsync(context.TenantId, member.Role, ct);
        if (user is null || user.TenantId != context.TenantId || user.Status == UserStatus.Disabled || role is null)
            return Result<Unit>.Fail(ErrorCodes.PermissionDenied);
        if (command.ExpectedChangeToken is { } token && token != StaffChangeToken.For(user, member))
            return Result<Unit>.Fail(ErrorCodes.ConcurrencyConflict);
        var before = new SortedSet<string>(role.PermissionCodes, StringComparer.Ordinal);
        before.UnionWith(member.ExtraPermissionCodes);
        before.ExceptWith(member.DeniedPermissionCodes);
        var granted = new HashSet<string>(command.PermissionCodes, StringComparer.Ordinal);
        foreach (var code in PermissionCodes.All)
        {
            member.ClearOverride(code);
            if (granted.Contains(code) && !role.Has(code)) member.GrantExtra(code);
            else if (!granted.Contains(code) && role.Has(code)) member.Deny(code);
        }
        // Authorization changed, not credentials: keep the active local session valid. Stored RBAC denies
        // removed grants immediately; replace the snapshot in the same transaction for the active device.
        var issued = await capabilities.IssueAsync(user, member, context.DeviceId, ct);
        if (issued.IsFailure) return Result<Unit>.Fail(issued.Error, issued.Detail);
        audit.Enlist(new AuditRecord(context.TenantId, context.UserId, context.DeviceId, clock.UtcNow,
            AuditCategory.Configuration, "identity.staff.permissions.changed", nameof(StaffMembership), member.Id,
            context.OperationId, context.CorrelationId, AuditOutcome.Success, null, null, null, null,
            System.Text.Json.JsonSerializer.Serialize(new
            {
                TargetUserId = user.Id, Before = before.ToArray(), After = granted.Order(StringComparer.Ordinal).ToArray(),
                Granted = granted.Except(before).Order(StringComparer.Ordinal).ToArray(),
                Removed = before.Except(granted).Order(StringComparer.Ordinal).ToArray(),
            })));
        return Result<Unit>.Ok(Unit.Value);
    }
}

/// <summary>Opaque edit precondition over security facts. Does not reuse the server-only sync Version field.</summary>
internal static class StaffChangeToken
{
    internal static string For(User user, StaffMembership membership)
    {
        var facts = System.Text.Json.JsonSerializer.Serialize(new
        {
            membership.Id, membership.Role, MembershipStatus = membership.Status, UserStatus = user.Status, user.SecurityStamp,
            Extra = membership.ExtraPermissionCodes.Order(StringComparer.Ordinal).ToArray(),
            Denied = membership.DeniedPermissionCodes.Order(StringComparer.Ordinal).ToArray(),
        });
        return Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(facts)));
    }
}
