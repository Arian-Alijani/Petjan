using Animora.Application.Ports;
using Animora.Domain.Boarding;
using Animora.Domain.Inventory;
using Animora.Domain.Sales;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Entities;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.Sqlite.Repositories;

/// <summary>Reads reference data and handoffs in the same scoped transaction as draft commands.</summary>
public sealed class SalesDraftData(AnimoraLocalDbContext db) : ISalesDraftData
{
    public async Task<SalesHandoff?> FindHandoffAsync(string source, string id, CancellationToken ct)
    {
        if (source == "Visit")
        {
            return await db.Set<VisitBillingHandoff>().AsNoTracking().Where(h => h.Id == id)
                .Select(h => new SalesHandoff(h.VisitId, h.OwnerId, h.PetId, h.LinesJson,
                    h.Status == HandoffStatus.Invoiced, h.Status == HandoffStatus.Ready)).FirstOrDefaultAsync(ct);
        }
        if (source == "Boarding")
        {
            return await db.Set<BoardingBillingHandoff>().AsNoTracking().Where(h => h.Id == id)
                .Select(h => new SalesHandoff(h.StayId, h.OwnerId, h.PetId, h.LinesJson,
                    h.Status == HandoffStatus.Invoiced, h.Status == HandoffStatus.Ready)).FirstOrDefaultAsync(ct);
        }
        return null;
    }

    public Task<Insurer?> FindInsurerAsync(string id, CancellationToken ct) =>
        db.Set<Insurer>().AsNoTracking().FirstOrDefaultAsync(i => i.Id == id, ct);

    public async Task<SalesCatalogEntry?> FindCatalogEntryAsync(string id, CancellationToken ct)
    {
        // Flatten the navigation before applying the correlated predicate: SQLite has
        // no APPLY operator for a filtered collection selector that references its parent.
        var product = await (from p in db.Set<Product>().AsNoTracking()
            from v in p.Variants
            where p.IsActive && v.IsActive && (v.VariantId == id || p.Id == id)
            orderby v.VariantId
            select new SalesCatalogEntry(v.VariantId,
                p.Kind == ProductKind.Service ? null : p.Id,
                p.Kind == ProductKind.Service ? null : v.VariantId,
                p.Name, p.Kind == ProductKind.Service ? InvoiceLineKind.Service : InvoiceLineKind.Product,
                v.SalePrice.Minor, p.UnitCode,
                // Only a non-service, stock-tracked product carries a stock cap; its on-hand is the
                // variant's snapshot figure (same source the catalog picker shows).
                p.TrackStock && p.Kind != ProductKind.Service,
                p.TrackStock && p.Kind != ProductKind.Service
                    ? (db.Set<StockOnHandSnapshot>()
                        .Where(s => s.ProductId == p.Id && s.VariantId == v.VariantId)
                        .Select(s => (long?)s.OnHandScaled).FirstOrDefault() ?? 0L)
                    : 0L)).FirstOrDefaultAsync(ct);
        if (product is not null) return product;
        // Product-level fallback: an active product with no active variant is sold at its default price
        // (mirrors the catalog's variant-less rows). No variant means no stock movement is posted for it.
        var bare = await db.Set<Product>().AsNoTracking()
            .Where(p => p.IsActive && p.Id == id && !p.Variants.Any(v => v.IsActive))
            .Select(p => new SalesCatalogEntry(p.Id,
                p.Kind == ProductKind.Service ? null : p.Id, null,
                p.Name, p.Kind == ProductKind.Service ? InvoiceLineKind.Service : InvoiceLineKind.Product,
                p.DefaultSalePrice.Minor, p.UnitCode)).FirstOrDefaultAsync(ct);
        if (bare is not null) return bare;
        return await db.Set<BoardingServiceDef>().AsNoTracking().Where(s => s.Id == id && s.IsActive)
            .Select(s => new SalesCatalogEntry(s.Id, null, null, s.Title,
                InvoiceLineKind.Boarding, s.UnitPrice.Minor, "PCS")).FirstOrDefaultAsync(ct);
    }
}
