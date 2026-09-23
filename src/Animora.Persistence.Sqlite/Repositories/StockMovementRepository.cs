using Animora.Application.Ports;
using Animora.Domain.Inventory;
using Animora.Persistence.Sqlite.Context;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.Sqlite.Repositories;

public sealed class StockMovementRepository(AnimoraLocalDbContext db) : IStockMovementRepository
{
    public Task<StockMovement?> FindAsync(string id, CancellationToken ct = default) =>
        db.Set<StockMovement>().AsTracking().FirstOrDefaultAsync(e => e.Id == id, ct);

    public async Task<StockMovement> RequireAsync(string id, CancellationToken ct = default) =>
        await FindAsync(id, ct) ?? throw new InvalidOperationException($"StockMovement {id} not found.");

    public void Add(StockMovement entity) => db.Set<StockMovement>().Add(entity);
    public void AddRange(IEnumerable<StockMovement> entities) => db.Set<StockMovement>().AddRange(entities);
}
