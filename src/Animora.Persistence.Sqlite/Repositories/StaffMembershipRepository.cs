using Animora.Application.Ports;
using Animora.Domain.Identity;
using Animora.Persistence.Sqlite.Context;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.Sqlite.Repositories;

public sealed class StaffMembershipRepository(AnimoraLocalDbContext db) : IStaffMembershipRepository
{
    public Task<StaffMembership?> FindAsync(string id, CancellationToken ct = default) =>
        db.Set<StaffMembership>().AsTracking().FirstOrDefaultAsync(e => e.Id == id, ct);

    public Task<StaffMembership?> FindActiveAsync(string tenantId, string userId, CancellationToken ct = default) =>
        db.Set<StaffMembership>().FirstOrDefaultAsync(e => e.TenantId == tenantId && e.UserId == userId
            && e.Status == MembershipStatus.Active && e.EndedUtc == null, ct);

    public void Add(StaffMembership entity) => db.Set<StaffMembership>().Add(entity);
}
