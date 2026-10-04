using Animora.Application.Ports;
using Animora.Application.UseCases.Visits;
using Animora.Domain.Inventory;
using Animora.Persistence.Sqlite.Context;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.Sqlite.Queries;

/// <summary>
/// Bounded catalog projection for the visit encounter's «خدمات، اقلام و نسخه» pickers. Every active product is
/// billable from a visit: a <see cref="ProductKind.Service"/> is surfaced as a «Service» (non-stock catalogue
/// entry), and every other active product kind — medicines, consumables, food, pet-shop goods, equipment and
/// vaccines — is surfaced as an «Item». Previously only Service/Medicine/Consumable were returned, so the pet-shop
/// products created from the Inventory «کالای جدید» form (all stored as <see cref="ProductKind.PetShop"/>) never
/// appeared in the item picker and the popup looked empty.
/// </summary>
public sealed class RxCatalogQueries(AnimoraLocalDbContext db) : IRxCatalogQueries
{
    public async Task<IReadOnlyList<RxCatalogItemReadModel>> GetAsync(CancellationToken ct = default) =>
        await db.Set<Product>().AsNoTracking()
            .Where(p => p.IsActive)
            .OrderBy(p => p.Name).ThenBy(p => p.Id).Take(1000)
            .Select(p => new RxCatalogItemReadModel(
                p.Kind == ProductKind.Service ? "Service" : "Item", p.Id, p.Name,
                p.DefaultSalePrice.Minor)).ToListAsync(ct);
}
