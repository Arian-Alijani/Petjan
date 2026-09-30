using Animora.Application.Ports;
using Animora.Domain.Inventory;
using Animora.Domain.ValueObjects;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Entities;
using Animora.Persistence.Sqlite.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Animora.Persistence.Sqlite.Repositories;

/// <summary>All reads and handoff transitions join the journal's ambient scoped transaction.</summary>
public sealed class SalesPostingData(AnimoraLocalDbContext db, IOptions<LocalStoreOptions> options) : ISalesPostingData
{
    public bool AllowNegativeStock => options.Value.AllowNegativeStockOnSale;

    public async Task<long> GetOnHandScaledAsync(string productId, string variantId, CancellationToken ct) =>
        await db.Set<StockOnHandSnapshot>().AsNoTracking()
            .Where(s => s.ProductId == productId && s.VariantId == variantId)
            .Select(s => (long?)s.OnHandScaled).FirstOrDefaultAsync(ct) ?? 0;

    public async Task<Money> GetLatestReceiptCostAsync(string productId, string variantId, CancellationToken ct)
    {
        var cost = await db.Set<StockMovement>().AsNoTracking()
            .Where(m => m.ProductId == productId && m.VariantId == variantId && m.Kind == MovementKind.Receipt)
            .OrderByDescending(m => m.OccurredUtc).ThenByDescending(m => m.Id)
            .Select(m => (long?)m.UnitCost.Minor).FirstOrDefaultAsync(ct);
        return Money.Irt(cost ?? 0);
    }

    public async Task<HandoffPostStatus> GetHandoffStatusAsync(string source, string sourceId, CancellationToken ct)
    {
        if (source == "Visit")
        {
            var status = await db.Set<VisitBillingHandoff>().AsNoTracking()
                .Where(h => h.VisitId == sourceId).Select(h => (HandoffStatus?)h.Status).FirstOrDefaultAsync(ct);
            return Map(status);
        }
        var stayStatus = await db.Set<BoardingBillingHandoff>().AsNoTracking()
            .Where(h => h.StayId == sourceId).Select(h => (HandoffStatus?)h.Status).FirstOrDefaultAsync(ct);
        return Map(stayStatus);
    }

    public async Task<HandoffPostStatus> MarkHandoffInvoicedAsync(string source, string sourceId,
        string invoiceId, CancellationToken ct)
    {
        // Rows are tracked so status + invoice link are flushed with invoice, movements and journal.
        if (source == "Visit")
        {
            var handoff = await db.Set<VisitBillingHandoff>().AsTracking().FirstOrDefaultAsync(h => h.VisitId == sourceId, ct);
            if (handoff is null) return HandoffPostStatus.NotFound;
            if (handoff.Status != HandoffStatus.Ready) return Map(handoff.Status);
            handoff.MarkInvoiced(invoiceId);
        }
        else
        {
            var handoff = await db.Set<BoardingBillingHandoff>().AsTracking().FirstOrDefaultAsync(h => h.StayId == sourceId, ct);
            if (handoff is null) return HandoffPostStatus.NotFound;
            if (handoff.Status != HandoffStatus.Ready) return Map(handoff.Status);
            handoff.MarkInvoiced(invoiceId);
        }
        return HandoffPostStatus.Ready;
    }

    private static HandoffPostStatus Map(HandoffStatus? status) => status switch
    {
        HandoffStatus.Ready => HandoffPostStatus.Ready,
        HandoffStatus.Invoiced => HandoffPostStatus.AlreadyInvoiced,
        _ => HandoffPostStatus.NotFound,
    };
}
