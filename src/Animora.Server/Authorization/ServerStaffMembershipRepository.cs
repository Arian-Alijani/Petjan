using Animora.Application.Ports;
using Animora.Domain.Identity;
using Animora.Persistence.PostgreSql.Context;
using Microsoft.EntityFrameworkCore;

namespace Animora.Server.Authorization;

/// <summary>
/// Read-only EF-over-PostgreSQL implementation of <see cref="IStaffMembershipRepository"/> for the server
/// authorization path. The Sqlite implementation lives in a project the server must never reference
/// (<c>ServerLayeringTests</c>), so the server owns its own. Reads are tenant-filtered by the request-bound
/// tenant context of <see cref="AnimoraServerDbContext"/>; the authorization path never adds memberships.
/// </summary>
internal sealed class ServerStaffMembershipRepository(AnimoraServerDbContext context) : IStaffMembershipRepository
{
    /// <inheritdoc />
    public Task<StaffMembership?> FindAsync(string id, CancellationToken ct = default) =>
        context.StaffMemberships.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id, ct);

    /// <inheritdoc />
    public Task<StaffMembership?> FindActiveAsync(string tenantId, string userId, CancellationToken ct = default) =>
        context.StaffMemberships
            .AsNoTracking()
            .Where(m => m.TenantId == tenantId
                && m.UserId == userId
                && m.Status == MembershipStatus.Active
                && m.EndedUtc == null)
            .OrderBy(m => m.JoinedUtc)
            .FirstOrDefaultAsync(ct);

    /// <summary>Not supported: memberships are provisioned by the identity plane, never by authorization.</summary>
    /// <exception cref="NotSupportedException">Always.</exception>
    public void Add(StaffMembership entity) =>
        throw new NotSupportedException("The server authorization path is read-only; memberships are provisioned by the identity plane.");
}
