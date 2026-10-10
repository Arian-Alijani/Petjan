using Animora.Domain.Identity;
using Animora.Persistence.PostgreSql.Operator;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.PostgreSql.Subscriptions;

public sealed class SeatUsageReader(AnimoraOperatorDbContext context) : ISeatUsageReader
{
    public Task<int> CountAsync(string tenantId, CancellationToken ct = default) =>
        context.StaffMemberships.AsNoTracking().Where(x => x.TenantId == tenantId && !x.Deleted &&
            x.Status == MembershipStatus.Active && x.EndedUtc == null).Select(x => x.UserId).Distinct().CountAsync(ct);
}
