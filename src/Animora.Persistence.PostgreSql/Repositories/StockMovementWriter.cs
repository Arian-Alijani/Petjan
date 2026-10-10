using Animora.Application.Ports;
using Animora.Domain.Inventory;
using Animora.Persistence.PostgreSql.Context;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.PostgreSql.Repositories;

// Sales already writes stock through IStockMovementRepository; using a no-op would lose those writes.
// Tables arrive in 108, but this transaction-bound writer can be wired without connecting to PostgreSQL.
public sealed class StockMovementWriter(AnimoraServerDbContext db)
    : TenantRepository<StockMovement>(db), IStockMovementWriter, IStockMovementRepository
{
    public void Append(StockMovement movement) => Context.Set<StockMovement>().Add(movement);
    public new void Add(StockMovement entity) => Append(entity);
    public void AddRange(IEnumerable<StockMovement> entities) => Context.Set<StockMovement>().AddRange(entities);
    public Task<StockMovement?> FindAsync(string id, CancellationToken ct = default) =>
        Context.Set<StockMovement>().Local.FirstOrDefault(m => m.Id == id) is { } local
            ? Task.FromResult<StockMovement?>(local) : Context.Set<StockMovement>().AsNoTracking().FirstOrDefaultAsync(m => m.Id == id, ct);
    public async Task<StockMovement> RequireAsync(string id, CancellationToken ct = default) =>
        await FindAsync(id, ct) ?? throw new InvalidOperationException($"StockMovement {id} not found.");
}
