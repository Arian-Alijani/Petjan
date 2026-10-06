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
        // A stay can have two handoffs over its life (prepaid room + final). Posting always acts on the
        // single Ready one; if none is Ready, fall back to the most recent so an already-invoiced handoff
        // still reports AlreadyInvoiced rather than NotFound.
        var ready = await db.Set<BoardingBillingHandoff>().AsNoTracking()
            .Where(h => h.StayId == sourceId && h.Status == HandoffStatus.Ready)
            .Select(h => (HandoffStatus?)h.Status).FirstOrDefaultAsync(ct);
        if (ready is not null) return Map(ready);
        var latest = await db.Set<BoardingBillingHandoff>().AsNoTracking()
            .Where(h => h.StayId == sourceId).OrderByDescending(h => h.CompletedUtc).ThenByDescending(h => h.Id)
            .Select(h => (HandoffStatus?)h.Status).FirstOrDefaultAsync(ct);
        return Map(latest);
    }

    public async Task<Application.Ports.BoardingHandoffKind?> GetReadyBoardingHandoffKindAsync(
        string stayId, CancellationToken ct)
    {
        var kind = await db.Set<BoardingBillingHandoff>().AsNoTracking()
            .Where(h => h.StayId == stayId && h.Status == HandoffStatus.Ready)
            .Select(h => (Entities.BoardingHandoffKind?)h.Kind).FirstOrDefaultAsync(ct);
        return kind switch
        {
            Entities.BoardingHandoffKind.Room => Application.Ports.BoardingHandoffKind.Room,
            Entities.BoardingHandoffKind.Final => Application.Ports.BoardingHandoffKind.Final,
            _ => null,
        };
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
            // Mark the single Ready handoff for this stay (there may also be an older, already-invoiced one).
            var handoff = await db.Set<BoardingBillingHandoff>().AsTracking()
                .Where(h => h.StayId == sourceId && h.Status == HandoffStatus.Ready)
                .FirstOrDefaultAsync(ct);
            if (handoff is null) return HandoffPostStatus.NotFound;
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
