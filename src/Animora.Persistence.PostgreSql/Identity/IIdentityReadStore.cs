using Animora.Domain.Identity;
using Animora.Persistence.PostgreSql.Context;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.PostgreSql.Identity;

/// <summary>
/// PERF-AUTHZ-01: the two READ-ONLY lookups the per-request membership resolution performs, served from the
/// <c>oltp-read</c> pool with no change tracking so they stop consuming write-pool connections. The tenant must
/// already be bound on the scope (the read context carries the same tenant query filter as the write context).
/// </summary>
public interface IIdentityReadStore
{
    /// <summary>The user's active staff membership, or <see langword="null"/>.</summary>
    Task<StaffMembership?> FindActiveMembershipAsync(string userId, CancellationToken ct = default);

    /// <summary>A device by id (tenant-filtered), or <see langword="null"/>.</summary>
    Task<Device?> FindDeviceByIdAsync(string deviceId, CancellationToken ct = default);
}

/// <inheritdoc cref="IIdentityReadStore" />
public sealed class IdentityReadStore(IServerReadContextFactory factory) : IIdentityReadStore
{
    /// <inheritdoc />
    public async Task<StaffMembership?> FindActiveMembershipAsync(string userId, CancellationToken ct = default)
    {
        await using AnimoraServerDbContext db = factory.Create();
        return await db.StaffMemberships
            .Where(m => m.UserId == userId && m.Status == MembershipStatus.Active && m.EndedUtc == null)
            .OrderBy(m => m.JoinedUtc)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Device?> FindDeviceByIdAsync(string deviceId, CancellationToken ct = default)
    {
        await using AnimoraServerDbContext db = factory.Create();
        return await db.Devices.FirstOrDefaultAsync(d => d.Id == deviceId, ct).ConfigureAwait(false);
    }
}
