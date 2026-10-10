using Animora.Application.Ports;
using Animora.Domain.Cashier;
using Animora.Persistence.PostgreSql.Context;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.PostgreSql.Repositories;

public sealed class CashSessionRepository(AnimoraServerDbContext db) : TenantRepository<CashSession>(db), ICashSessionRepository
{
    public Task<CashSession?> FindAsync(string id, CancellationToken ct = default) =>
        Context.Set<CashSession>().AsTracking().FirstOrDefaultAsync(e => e.Id == id, ct);

    public async Task<CashSession> RequireAsync(string id, CancellationToken ct = default) =>
        await FindAsync(id, ct) ?? throw new InvalidOperationException($"CashSession {id} not found.");

    public new void Add(CashSession entity) => Context.Set<CashSession>().Add(entity);
    public void AddRange(IEnumerable<CashSession> entities) => Context.Set<CashSession>().AddRange(entities);
}
