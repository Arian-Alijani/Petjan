using Animora.Domain.Identity;
using Animora.Domain.Results;
using Animora.Persistence.PostgreSql.Identity;

namespace Animora.Server.Authorization;

/// <summary>
/// Fail-closed resolution order (S05): primary-key user lookup (tenant not yet known) → stamp →
/// disabled → <b>bind the tenant from the stored user</b> → active membership → tenant status → device.
/// The token's <c>tid</c>/<c>role</c> claims are never inputs. There is deliberately no cache.
/// </summary>
internal sealed class MembershipResolver(
    IUserLookup users,
    RequestTenantContext tenantContext,
    IIdentityRepository repository,
    ITenantDirectory tenants) : IMembershipResolver
{
    /// <inheritdoc />
    public async Task<Result<ResolvedMembership>> ResolveAsync(
        string userId,
        string deviceId,
        string securityStamp,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(securityStamp);

        // 1. The tenant is not known yet, so the main (tenant-filtered) context cannot be used: read the user by
        //    primary key through the keyless lookup — one addressed row, no enumeration.
        UserLookupEntry? user = await users.FindByIdAsync(userId, ct).ConfigureAwait(false);
        if (user is null || !string.Equals(user.SecurityStamp, securityStamp, StringComparison.Ordinal))
        {
            // Unknown user and rotated stamp are indistinguishable.
            return Result<ResolvedMembership>.Fail(ErrorCodes.SessionExpired);
        }

        if (user.Status == UserStatus.Disabled)
        {
            return Result<ResolvedMembership>.Fail(ErrorCodes.UserDisabled);
        }

        // 2. Bind the tenant from the STORED user before any tenant-filtered read (ordering matters: the
        //    DbContext query filter throws on an unbound tenant, which is the intended fail-closed default).
        tenantContext.Bind(user.TenantId);

        StaffMembership? membership = await repository.FindActiveMembershipAsync(user.Id, ct).ConfigureAwait(false);
        if (membership is null || !membership.IsActive
            || !string.Equals(membership.TenantId, user.TenantId, StringComparison.Ordinal))
        {
            return Result<ResolvedMembership>.Fail(ErrorCodes.MembershipNotActive);
        }

        Result<TenantDirectoryEntry> tenant = await tenants.FindByIdAsync(user.TenantId, ct).ConfigureAwait(false);
        if (tenant.IsFailure)
        {
            return Result<ResolvedMembership>.Fail(ErrorCodes.SessionExpired);
        }

        if (tenant.Value!.Status != TenantStatus.Active) // non-null on success
        {
            return Result<ResolvedMembership>.Fail(ErrorCodes.TenantSuspended);
        }

        Device? device = await repository.FindDeviceByIdAsync(deviceId, ct).ConfigureAwait(false);
        if (device is null
            || device.Status == DeviceStatus.Revoked
            || !string.Equals(device.UserId, user.Id, StringComparison.Ordinal))
        {
            // A device that is missing, revoked, or not the caller's own is treated as revoked.
            return Result<ResolvedMembership>.Fail(ErrorCodes.DeviceRevoked);
        }

        return Result<ResolvedMembership>.Ok(
            new ResolvedMembership(user.TenantId, membership.Role, user.Id, device.Id, device.IsUsable));
    }
}
