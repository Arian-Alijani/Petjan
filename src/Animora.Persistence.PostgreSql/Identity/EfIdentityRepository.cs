using Animora.Domain.Identity;
using Animora.Domain.Results;
using Animora.Persistence.PostgreSql.Context;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.PostgreSql.Identity;

/// <summary>EF Core implementation of <see cref="IIdentityRepository"/> over <see cref="AnimoraServerDbContext"/>.</summary>
public sealed class EfIdentityRepository(AnimoraServerDbContext context) : IIdentityRepository
{
    /// <inheritdoc />
    public Task<User?> FindUserByNameAsync(string normalizedUserName, CancellationToken ct = default) =>
        context.Users.FirstOrDefaultAsync(u => u.UserName == normalizedUserName, ct);

    /// <inheritdoc />
    public Task<User?> FindUserByIdAsync(string userId, CancellationToken ct = default) =>
        context.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);

    /// <inheritdoc />
    public Task<StaffMembership?> FindActiveMembershipAsync(string userId, CancellationToken ct = default) =>
        context.StaffMemberships
            .Where(m => m.UserId == userId && m.Status == MembershipStatus.Active && m.EndedUtc == null)
            .OrderBy(m => m.JoinedUtc)
            .FirstOrDefaultAsync(ct);

    /// <inheritdoc />
    public Task<Device?> FindDeviceByInstallationAsync(string userId, string installationIdHash, CancellationToken ct = default) =>
        context.Devices.FirstOrDefaultAsync(d => d.UserId == userId && d.InstallationIdHash == installationIdHash, ct);

    /// <inheritdoc />
    public Task<Device?> FindDeviceByIdAsync(string deviceId, CancellationToken ct = default) =>
        context.Devices.FirstOrDefaultAsync(d => d.Id == deviceId, ct);

    /// <inheritdoc />
    public Task<StaffMembership?> FindMembershipByIdAsync(string membershipId, CancellationToken ct = default) =>
        context.StaffMemberships.FirstOrDefaultAsync(m => m.Id == membershipId, ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<(User User, StaffMembership Membership)>> ListStaffAsync(
        string tenantId, CancellationToken ct = default)
    {
        // Tenant-filtered join of users and their memberships. The DbContext already applies the bound-tenant
        // filter; the explicit tenantId predicate is defence-in-depth so a misbound context cannot leak rows.
        var rows = await context.StaffMemberships
            .AsNoTracking()
            .Where(m => m.TenantId == tenantId)
            .Join(
                context.Users.AsNoTracking().Where(u => u.TenantId == tenantId),
                m => m.UserId,
                u => u.Id,
                (m, u) => new { User = u, Membership = m })
            .OrderBy(x => x.Membership.JoinedUtc)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return rows.Select(x => (x.User, x.Membership)).ToArray();
    }

    /// <inheritdoc />
    public void AddDevice(Device device)
    {
        ArgumentNullException.ThrowIfNull(device);
        context.Devices.Add(device);
    }

    /// <inheritdoc />
    public void AddUser(User user)
    {
        ArgumentNullException.ThrowIfNull(user);
        context.Users.Add(user);
    }

    /// <inheritdoc />
    public void AddMembership(StaffMembership membership)
    {
        ArgumentNullException.ThrowIfNull(membership);
        context.StaffMemberships.Add(membership);
    }

    /// <inheritdoc />
    public async Task<Result> SaveAsync(CancellationToken ct = default)
    {
        try
        {
            await context.SaveChangesAsync(ct).ConfigureAwait(false);
            return Result.Ok();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Fail(ErrorCodes.ConcurrencyConflict);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        {
            return Result.Fail(ErrorCodes.ConcurrencyConflict, "unique");
        }
    }
}
