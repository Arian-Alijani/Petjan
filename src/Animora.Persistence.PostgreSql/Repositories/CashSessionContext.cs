using Animora.Application.Ports;
using Animora.Domain.Cashier;
using Animora.Persistence.PostgreSql.Context;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.PostgreSql.Repositories;

/// <summary>Tracked aggregate: a payment and its drawer movement commit in the same unit of work.</summary>
public sealed class CashSessionContext(AnimoraServerDbContext db) : ICashSessionContext
{
    public Task<CashSession?> GetOpenSessionAsync(string tenantId, string userId, CancellationToken ct) =>
        db.Set<CashSession>().AsTracking()
            .SingleOrDefaultAsync(s => s.TenantId == tenantId && s.CashierUserId == userId &&
                s.State == CashSessionState.Open, ct);
}
