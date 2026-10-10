using Animora.Application.Ports;
using Animora.Domain.Boarding;
using Animora.Domain.Inventory;
using Animora.Domain.Sales;
using Animora.Persistence.PostgreSql.Context;
using Animora.Persistence.PostgreSql.Clinical;
using Animora.Persistence.PostgreSql.Boarding;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.PostgreSql.Repositories;

/// <summary>Reads reference data and handoffs in the same scoped transaction as draft commands.</summary>
public sealed class SalesDraftData(AnimoraServerDbContext db) : ISalesDraftData
{
    public async Task<SalesHandoff?> FindHandoffAsync(string source, string id, CancellationToken ct)
    {
        if (source == "Visit")
        {
            return await db.Set<VisitBillingHandoff>().AsNoTracking().Where(h => h.Id == id)
                .Select(h => new SalesHandoff(h.VisitId, h.OwnerId, h.PetId, h.LinesJson,
                    h.Status == 1, h.Status == 0)).FirstOrDefaultAsync(ct);
        }
        if (source == "Boarding")
        {
            return await db.Set<BoardingBillingHandoff>().AsNoTracking().Where(h => h.Id == id)
                .Select(h => new SalesHandoff(h.StayId, h.OwnerId, h.PetId, h.LinesJson,
                    h.Status == 1, h.Status == 0)).FirstOrDefaultAsync(ct);
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
                v.SalePrice.Minor, p.UnitCode)).FirstOrDefaultAsync(ct);
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
