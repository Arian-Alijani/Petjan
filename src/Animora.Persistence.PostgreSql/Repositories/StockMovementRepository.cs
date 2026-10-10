using Animora.Application.Ports;
using Animora.Domain.Inventory;
using Animora.Persistence.PostgreSql.Context;
namespace Animora.Persistence.PostgreSql.Repositories;
public sealed class StockMovementRepository(AnimoraServerDbContext db, StockMovementWriter writer)
    : TenantRepository<StockMovement>(db), IStockMovementRepository
{
    public Task<StockMovement?> FindAsync(string id, CancellationToken ct = default) => writer.FindAsync(id, ct);
    public Task<StockMovement> RequireAsync(string id, CancellationToken ct = default) => writer.RequireAsync(id, ct);
    public new void Add(StockMovement entity) => writer.Append(entity);
    public void AddRange(IEnumerable<StockMovement> entities) { foreach (var entity in entities) writer.Append(entity); }
}
