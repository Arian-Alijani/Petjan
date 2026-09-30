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
        var product = await db.Set<Product>().AsNoTracking().Where(p => p.IsActive)
            .SelectMany(p => p.Variants.Where(v => v.IsActive && (v.VariantId == id || p.Id == id)),
                (p, v) => new SalesCatalogEntry(v.VariantId,
                    p.Kind == ProductKind.Service ? null : p.Id,
                    p.Kind == ProductKind.Service ? null : v.VariantId,
                    p.Name, p.Kind == ProductKind.Service ? InvoiceLineKind.Service : InvoiceLineKind.Product,
                    v.SalePrice.Minor, p.UnitCode)).FirstOrDefaultAsync(ct);
        if (product is not null) return product;
        return await db.Set<BoardingServiceDef>().AsNoTracking().Where(s => s.Id == id && s.IsActive)
            .Select(s => new SalesCatalogEntry(s.Id, null, null, s.Title,
                InvoiceLineKind.Boarding, s.UnitPrice.Minor, "PCS")).FirstOrDefaultAsync(ct);
    }
}
