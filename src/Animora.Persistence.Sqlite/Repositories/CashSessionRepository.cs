using Animora.Application.Ports;
using Animora.Domain.Cashier;
using Animora.Persistence.Sqlite.Context;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.Sqlite.Repositories;

public sealed class CashSessionRepository(AnimoraLocalDbContext db) : ICashSessionRepository
{
    public Task<CashSession?> FindAsync(string id, CancellationToken ct = default) =>
        db.Set<CashSession>().AsTracking().FirstOrDefaultAsync(e => e.Id == id, ct);

    public async Task<CashSession> RequireAsync(string id, CancellationToken ct = default) =>
        await FindAsync(id, ct) ?? throw new InvalidOperationException($"CashSession {id} not found.");

    public void Add(CashSession entity) => db.Set<CashSession>().Add(entity);
    public void AddRange(IEnumerable<CashSession> entities) => db.Set<CashSession>().AddRange(entities);
}
