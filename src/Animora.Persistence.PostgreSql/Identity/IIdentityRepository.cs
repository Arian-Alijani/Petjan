using Animora.Domain.Identity;
using Animora.Domain.Results;

namespace Animora.Persistence.PostgreSql.Identity;

/// <summary>
/// The tenant-scoped persistence seam of the authentication plane. Every method operates on the tenant
/// bound in the current scope (<see cref="AuthTenantContext"/>); reads return <b>tracked</b> aggregates so
/// the sign-in flow mutates them (lockout counters, last-contact) and commits through
/// <see cref="ISecurityAuditWriter"/> in one transaction. Implemented over EF by <see cref="EfIdentityRepository"/>;
/// unit tests substitute an in-memory fake.
/// </summary>
public interface IIdentityRepository
{
    /// <summary>Finds a user by normalized (lowercase) login name in the bound tenant.</summary>
    Task<User?> FindUserByNameAsync(string normalizedUserName, CancellationToken ct = default);

    /// <summary>Finds a user by id in the bound tenant.</summary>
    Task<User?> FindUserByIdAsync(string userId, CancellationToken ct = default);

    /// <summary>The user's active staff membership, or <see langword="null"/> when none is active.</summary>
    Task<StaffMembership?> FindActiveMembershipAsync(string userId, CancellationToken ct = default);

    /// <summary>Finds the user's device by installation-id hash.</summary>
    Task<Device?> FindDeviceByInstallationAsync(string userId, string installationIdHash, CancellationToken ct = default);

    /// <summary>Finds a device by id.</summary>
    Task<Device?> FindDeviceByIdAsync(string deviceId, CancellationToken ct = default);

    /// <summary>Finds a staff membership by id (tenant-filtered by the bound tenant context).</summary>
    Task<StaffMembership?> FindMembershipByIdAsync(string membershipId, CancellationToken ct = default);

    /// <summary>
    /// The full staff roster (user + membership pairs) of the bound tenant, ordered by join date. Used by the
    /// online staff-management plane so every admin reads the same server-authoritative list.
    /// </summary>
    Task<IReadOnlyList<(User User, StaffMembership Membership)>> ListStaffAsync(string tenantId, CancellationToken ct = default);

    /// <summary>Stages a new device (committed by the next save).</summary>
    void AddDevice(Device device);

    /// <summary>Stages a new user (committed by the next save). Used by the staff-provisioning plane.</summary>
    void AddUser(User user);

    /// <summary>Stages a new staff membership (committed by the next save). Used by the staff-provisioning plane.</summary>
    void AddMembership(StaffMembership membership);

    /// <summary>Commits staged changes; concurrency / duplicate-key faults map to <c>ConcurrencyConflict</c>.</summary>
    Task<Result> SaveAsync(CancellationToken ct = default);
}
