namespace Animora.Contracts.Wire.Auth;

// Staff (clinic user) management wire contracts. These back the online, authenticated staff-management
// endpoints under /auth/staff (create user, enable/disable membership, set permission overrides, list roster).
// The tenant a new user belongs to is ALWAYS taken from the authenticated caller's token server-side — it is
// never carried in any request body here, so an admin can only provision users inside their own clinic.
// Permission *labels* / risk text stay on the client (ProfileMapping); the wire carries permission *codes* only.

/// <summary>
/// Body of <c>POST /auth/staff</c>: create a new clinic user + active staff membership. Validated server-side
/// with the same rules as the local <c>CreateStaffCommand</c> (length caps, password policy, unique username,
/// role must exist, creating a Manager needs <c>roles.manage</c>).
/// </summary>
/// <param name="Name">Display name (≤ 200 chars).</param>
/// <param name="Username">Login name, unique per tenant (≤ 128 chars).</param>
/// <param name="Password">Plaintext password (≤ 256 chars; hashed server-side, never logged).</param>
/// <param name="Role">Staff role name (parsed to the <c>StaffRole</c> enum server-side).</param>
/// <param name="Phone">Contact phone stored as a profile preference (≤ 30 chars; may be empty).</param>
public sealed record CreateStaffServerRequest(
    string Name,
    string Username,
    string Password,
    string Role,
    string Phone)
{
    public override string ToString() => $"{nameof(CreateStaffServerRequest)} {{ Username = {Username}, Password = *** }}";
}

/// <summary>Body of <c>POST /auth/staff/{id}/active</c>: enable or disable a staff membership.</summary>
/// <param name="MembershipId">Target membership ULID (the <c>{id}</c> route value echoes this).</param>
/// <param name="Active"><see langword="true"/> to reinstate + enable, <see langword="false"/> to suspend + disable.</param>
/// <param name="ExpectedChangeToken">Optional optimistic-concurrency token from the last roster read.</param>
public sealed record StaffActiveServerRequest(
    string MembershipId,
    bool Active,
    string? ExpectedChangeToken);

/// <summary>Body of <c>POST /auth/staff/access</c>: replace a membership's effective permission set.</summary>
/// <param name="MembershipId">Target membership ULID.</param>
/// <param name="PermissionCodes">The full set of permission codes the membership should end up with.</param>
/// <param name="ExpectedChangeToken">Optional optimistic-concurrency token from the last roster read.</param>
public sealed record StaffAccessServerRequest(
    string MembershipId,
    IReadOnlyList<string> PermissionCodes,
    string? ExpectedChangeToken);

/// <summary>One row of the server staff roster returned by <c>GET /auth/staff</c>.</summary>
/// <param name="MembershipId">Membership ULID.</param>
/// <param name="UserId">User ULID.</param>
/// <param name="Name">Display name.</param>
/// <param name="Username">Login name.</param>
/// <param name="Role">Staff role name.</param>
/// <param name="IsActive">Membership active AND user not disabled.</param>
/// <param name="IsProtected"><see langword="true"/> for a Manager or the caller themselves (cannot be disabled/edited).</param>
/// <param name="Permissions">Effective permission codes (role defaults ∪ extras − denied).</param>
/// <param name="ChangeToken">Opaque optimistic-concurrency token over the membership's security facts.</param>
public sealed record StaffAccountWire(
    string MembershipId,
    string UserId,
    string Name,
    string Username,
    string Role,
    bool IsActive,
    bool IsProtected,
    IReadOnlyList<string> Permissions,
    string ChangeToken);

/// <summary>
/// Current caller's server-authoritative access, including online-only permissions. Never an offline grant.
/// Contains no roster/credentials; clients refresh this small read while keeping their existing session.
/// </summary>
public sealed record CurrentAccessWire(string TenantId, string UserId, string DeviceId, string Role,
    IReadOnlyList<string> Permissions, string Revision);

/// <summary>A selectable role plus the permission codes it grants by default.</summary>
/// <param name="RoleId">Staff role name.</param>
/// <param name="DefaultPermissions">Permission codes the role's stored definition grants.</param>
public sealed record StaffRoleOptionWire(
    string RoleId,
    IReadOnlyList<string> DefaultPermissions);

/// <summary>Body of <c>GET /auth/staff</c>: the clinic's staff roster plus the role catalogue and caller capabilities.</summary>
/// <param name="Members">Every staff member of the caller's tenant.</param>
/// <param name="Roles">Selectable roles with their default permission codes.</param>
/// <param name="CanManageUsers">Whether the caller may create/enable/disable users.</param>
/// <param name="CanManageAccess">Whether the caller may edit permission overrides.</param>
/// <param name="Devices">Registered devices of the tenant's staff; only populated for a caller holding <c>devices.manage</c>.</param>
/// <param name="CanManageDevices">Whether the caller may approve / revoke staff devices.</param>
public sealed record StaffDirectoryWire(
    IReadOnlyList<StaffAccountWire> Members,
    IReadOnlyList<StaffRoleOptionWire> Roles,
    bool CanManageUsers,
    bool CanManageAccess,
    IReadOnlyList<StaffDeviceWire>? Devices = null,
    bool CanManageDevices = false);

/// <summary>One registered device of a staff member, shown so a manager can approve a pending one or revoke an active one.</summary>
/// <param name="DeviceId">Device ULID (the <c>{id}</c> of <c>/auth/device/{id}/approve|revoke</c>).</param>
/// <param name="UserId">Owning user ULID.</param>
/// <param name="DeviceName">Operator-visible device name.</param>
/// <param name="Platform">Platform moniker.</param>
/// <param name="Status">Device status name: <c>Pending</c>, <c>Active</c> or <c>Revoked</c>.</param>
public sealed record StaffDeviceWire(
    string DeviceId,
    string UserId,
    string DeviceName,
    string Platform,
    string Status);
