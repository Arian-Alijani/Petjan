using Animora.Application.Ports;
using Animora.Application.UseCases.Visits;
using Animora.Domain.Inventory;
using Animora.Persistence.Sqlite.Context;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.Sqlite.Queries;

/// <summary>Bounded catalog projection; service products are non-stock-tracked catalog entries.</summary>
public sealed class RxCatalogQueries(AnimoraLocalDbContext db) : IRxCatalogQueries
{
    public async Task<IReadOnlyList<RxCatalogItemReadModel>> GetAsync(CancellationToken ct = default) =>
        await db.Set<Product>().AsNoTracking()
            .Where(p => p.IsActive && (p.Kind == ProductKind.Service || p.Kind == ProductKind.Medicine ||
                p.Kind == ProductKind.Consumable))
            .OrderBy(p => p.Name).ThenBy(p => p.Id).Take(200)
            .Select(p => new RxCatalogItemReadModel(
                p.Kind == ProductKind.Service ? "Service" : "Item", p.Id, p.Name,
                p.DefaultSalePrice.Minor)).ToListAsync(ct);
}
