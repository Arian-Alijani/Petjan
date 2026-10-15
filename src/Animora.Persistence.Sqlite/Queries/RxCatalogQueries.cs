using Animora.Application.Ports;
using Animora.Application.UseCases.Visits;
using Animora.Domain.Inventory;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Entities;
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
/// <remarks>
/// Services (the rows added from the Sales «نرخ خدمات» / cashier screen, all stored as stock-free
/// <see cref="ProductKind.Service"/> products) are ordered <b>first</b>, so they can never be pushed out of the
/// bounded 1000-row window by a large goods catalogue — a service added in Sales always loads here and stays
/// addable during a visit.
/// </remarks>
public sealed class RxCatalogQueries(AnimoraLocalDbContext db) : IRxCatalogQueries
{
    public async Task<IReadOnlyList<RxCatalogItemReadModel>> GetAsync(CancellationToken ct = default)
    {
        // Priced like the Sales catalog: one row per ACTIVE variant (v.SalePrice) — multi-variant goods are stored
        // with Product.DefaultSalePrice = 0, so reading the product-level price made them look free in the visit.
        var variantRows = await (from p in db.Set<Product>().AsNoTracking()
            where p.IsActive
            from v in p.Variants
            where v.IsActive
            orderby p.Kind == ProductKind.Service descending, p.Name, v.VariantId
            select new RxCatalogItemReadModel(p.Kind == ProductKind.Service ? "Service" : "Item", v.VariantId,
                p.Name, v.SalePrice.Minor,
                p.TrackStock && p.Kind != ProductKind.Service
                    ? (db.Set<StockOnHandSnapshot>().Where(s => s.ProductId == p.Id && s.VariantId == v.VariantId)
                        .Select(s => (long?)s.OnHandScaled).FirstOrDefault() ?? 0L) : (long?)null))
            .Take(1000).ToListAsync(ct);
        // Product-level fallback for active products without any active variant (e.g. services from the rate editor).
        var variantless = await db.Set<Product>().AsNoTracking()
            .Where(p => p.IsActive && !p.Variants.Any(v => v.IsActive))
            .OrderByDescending(p => p.Kind == ProductKind.Service)
            .ThenBy(p => p.Name).ThenBy(p => p.Id).Take(500)
            .Select(p => new RxCatalogItemReadModel(p.Kind == ProductKind.Service ? "Service" : "Item", p.Id,
                p.Name, p.DefaultSalePrice.Minor,
                p.TrackStock && p.Kind != ProductKind.Service ? 0L : (long?)null)).ToListAsync(ct);
        // Services first so a large goods catalogue can never push them out of the bounded window.
        return variantRows.Concat(variantless)
            .OrderBy(r => r.Kind == "Service" ? 0 : 1)
            .ThenBy(r => r.Title, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal).ToList();
    }
}
