using Animora.Domain.Identity;
using Animora.Domain.Results;
using Animora.Persistence.PostgreSql.Identity;

namespace Animora.Server.Authorization;

/// <summary>
/// Fail-closed resolution order (S05): primary-key user lookup (tenant not yet known) → stamp →
/// disabled → <b>bind the tenant from the stored user</b> → active membership → tenant status → device.
/// The token's <c>tid</c>/<c>role</c> claims are never inputs.
/// <para>
/// PERF-AUTHZ-01: the membership + device lookups run on the READ pool (<see cref="IIdentityReadStore"/>) and a
/// successful resolution is remembered for <c>Server:AuthCache:TtlSeconds</c> (absolute TTL, keyed by
/// user + device + token stamp; failures are never cached). The tenant is still bound from the stored user on a
/// cache hit, so the tenant-filtered DbContexts keep failing closed.
/// </para>
/// </summary>
internal sealed class MembershipResolver(
    IUserLookup users,
    RequestTenantContext tenantContext,
    IIdentityReadStore repository,
    ITenantDirectory tenants,
    AuthzDecisionCache cache,
    AuthzOperationGuard guard,
    Animora.Server.Observability.ServerMetrics? metrics = null) : IMembershipResolver
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

        if (cache.TryGetMembership(userId, deviceId, securityStamp, out ResolvedMembership? cached) && cached is not null)
        {
            // The cached verdict was produced from the stored user/membership/tenant/device; bind the same stored tenant.
            tenantContext.Bind(cached.TenantId);
            return Result<ResolvedMembership>.Ok(cached);
        }

        // IMP-04: only the uncached (database) path is counted - cache hits cost no query.
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            return await ResolveFromStoreAsync(userId, deviceId, securityStamp, ct).ConfigureAwait(false);
        }
        finally
        {
            metrics?.RecordAuthzQuery(System.Diagnostics.Stopwatch.GetElapsedTime(started));
        }
    }

    private async Task<Result<ResolvedMembership>> ResolveFromStoreAsync(string userId, string deviceId, string securityStamp, CancellationToken ct)
    {
        // 1. The tenant is not known yet, so the main (tenant-filtered) context cannot be used: read the user by
        //    primary key through the keyless lookup — one addressed row, no enumeration.
        UserLookupEntry? user = await guard.RunAsync(t => users.FindByIdAsync(userId, t), ct).ConfigureAwait(false);
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

        StaffMembership? membership = await guard.RunAsync(t => repository.FindActiveMembershipAsync(user.Id, t), ct).ConfigureAwait(false);
        if (membership is null || !membership.IsActive
            || !string.Equals(membership.TenantId, user.TenantId, StringComparison.Ordinal))
        {
            return Result<ResolvedMembership>.Fail(ErrorCodes.MembershipNotActive);
        }

        Result<TenantDirectoryEntry> tenant = await guard.RunAsync(t => tenants.FindByIdAsync(user.TenantId, t), ct).ConfigureAwait(false);
        if (tenant.IsFailure)
        {
            return Result<ResolvedMembership>.Fail(ErrorCodes.SessionExpired);
        }

        if (tenant.Value!.Status != TenantStatus.Active) // non-null on success
        {
            return Result<ResolvedMembership>.Fail(ErrorCodes.TenantSuspended);
        }

        Device? device = await guard.RunAsync(t => repository.FindDeviceByIdAsync(deviceId, t), ct).ConfigureAwait(false);
        if (device is null
            || device.Status == DeviceStatus.Revoked
            || !string.Equals(device.UserId, user.Id, StringComparison.Ordinal))
        {
            // A device that is missing, revoked, or not the caller's own is treated as revoked.
            return Result<ResolvedMembership>.Fail(ErrorCodes.DeviceRevoked);
        }

        var resolved = new ResolvedMembership(user.TenantId, membership.Role, user.Id, device.Id, device.IsUsable);
        cache.SetMembership(userId, deviceId, securityStamp, resolved);
        return Result<ResolvedMembership>.Ok(resolved);
    }
}
