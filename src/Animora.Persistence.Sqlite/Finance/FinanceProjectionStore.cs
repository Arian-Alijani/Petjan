using Animora.Application.Ports;
using Animora.Domain.Events;
using Animora.Domain.Inventory;
using Animora.Domain.Sales;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Entities;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.Sqlite.Finance;

/// <summary>Stages changes to local-only facts. Outbox completion saves the facts and ack atomically.</summary>
public sealed class FinanceProjectionStore(AnimoraLocalDbContext db) : IFinanceProjectionStore
{
    public async Task ApplyAsync(DomainEvent e, int day, CancellationToken ct)
    {
        switch (e)
        {
            case InvoicePostedEvent posted:
            {
                var invoice = await db.Set<Invoice>().FirstOrDefaultAsync(i => i.Id == posted.InvoiceId, ct)
                    ?? throw new InvalidOperationException($"Missing invoice {posted.InvoiceId}");
                var fact = await Daily(e.TenantId, day, ct);
                fact.Apply(revenue: invoice.Subtotal.Minor, discount: invoice.DiscountTotal.Minor,
                    insurance: invoice.InsuranceCoverage.Minor, invoices: 1);
                foreach (var line in invoice.Lines.Where(l => l.Kind == InvoiceLineKind.Product && l.ProductId is not null))
                {
                    var product = await Product(e.TenantId, day, line.ProductId!, line.VariantId ?? string.Empty, ct);
                    product.Apply(line.Quantity.Value, line.LineGross.Minor);
                }
                if (posted.OwnerId is not null)
                    (await Owner(e.TenantId, posted.OwnerId, ct)).Apply(
                        spend: posted.GrandTotalMinor,
                        invoiceUtc: posted.OccurredAtUtc, invoices: 1);
                break;
            }
            case InvoiceVoidedEvent voided:
            {
                var invoice = await db.Set<Invoice>().FirstOrDefaultAsync(i => i.Id == voided.InvoiceId, ct)
                    ?? throw new InvalidOperationException($"Missing invoice {voided.InvoiceId}");
                (await Daily(e.TenantId, day, ct)).Apply(revenue: -invoice.Subtotal.Minor,
                    discount: -invoice.DiscountTotal.Minor, insurance: -invoice.InsuranceCoverage.Minor,
                    refund: invoice.GrandTotal.Minor, invoices: -1);
                foreach (var line in invoice.Lines.Where(l => l.Kind == InvoiceLineKind.Product && l.ProductId is not null))
                    (await Product(e.TenantId, day, line.ProductId!, line.VariantId ?? string.Empty, ct))
                        .Apply(-line.Quantity.Value, -line.LineGross.Minor);
                if (invoice.OwnerId is not null)
                    (await Owner(e.TenantId, invoice.OwnerId, ct)).Apply(
                        spend: -invoice.GrandTotal.Minor, invoices: -1);
                break;
            }
            case PaymentRegisteredEvent payment when payment.AmountMinor != 0 && payment.Method != PaymentMethod.Credit:
            {
                var invoice = await db.Set<Invoice>().FirstOrDefaultAsync(i => i.Id == payment.InvoiceId, ct)
                    ?? throw new InvalidOperationException($"Missing invoice {payment.InvoiceId}");
                if (payment.AmountMinor > 0 && invoice.DueTotal.Minor <= 0)
                    (await Daily(e.TenantId, day, ct)).Apply(paidInvoices: 1);
                break;
            }
            case InvoiceRefundedEvent refund:
                (await Daily(e.TenantId, day, ct)).Apply(refund: Math.Abs(refund.AmountMinor));
                break;
            case StockMovementRecordedEvent movement when movement.Kind == MovementKind.Sale:
            {
                var stock = await db.Set<StockMovement>().FirstOrDefaultAsync(m => m.Id == movement.MovementId, ct)
                    ?? throw new InvalidOperationException($"Missing movement {movement.MovementId}");
                var cost = checked((long)decimal.Round(Math.Abs(stock.Quantity.Value) * stock.UnitCost.Minor,
                    0, MidpointRounding.AwayFromZero));
                (await Daily(e.TenantId, day, ct)).Apply(cogs: cost);
                (await Product(e.TenantId, day, movement.ProductId, movement.VariantId, ct))
                    .Apply(cogs: cost);
                break;
            }
            case ExpensePostedEvent expense:
                (await Daily(e.TenantId, day, ct)).Apply(expense: expense.IsReversal
                    ? -Math.Abs(expense.AmountMinor) : Math.Abs(expense.AmountMinor));
                break;
            // Receipts, cheque settlement and drawer closure move balance-sheet accounts only.
        }
    }

    private async Task<DailyFinanceFact> Daily(string tenant, int day, CancellationToken ct)
    {
        var fact = db.Set<DailyFinanceFact>().Local.FirstOrDefault(f => f.TenantId == tenant && f.LocalDay == day)
            ?? await db.Set<DailyFinanceFact>().AsTracking()
                .FirstOrDefaultAsync(f => f.TenantId == tenant && f.LocalDay == day, ct);
        if (fact is not null) return fact;
        fact = DailyFinanceFact.Create(tenant, day);
        db.Add(fact);
        return fact;
    }

    private async Task<ProductSalesFact> Product(string tenant, int day, string productId, string variantId,
        CancellationToken ct)
    {
        var fact = db.Set<ProductSalesFact>().Local.FirstOrDefault(f => f.TenantId == tenant && f.LocalDay == day &&
            f.ProductId == productId && f.VariantId == variantId)
            ?? await db.Set<ProductSalesFact>().AsTracking().FirstOrDefaultAsync(f => f.TenantId == tenant &&
                f.LocalDay == day && f.ProductId == productId && f.VariantId == variantId, ct);
        if (fact is not null) return fact;
        fact = ProductSalesFact.Create(tenant, day, productId, variantId);
        db.Add(fact);
        return fact;
    }

    private async Task<OwnerFinancialSnapshot> Owner(string tenant, string ownerId, CancellationToken ct)
    {
        var fact = db.Set<OwnerFinancialSnapshot>().Local.FirstOrDefault(f => f.TenantId == tenant && f.OwnerId == ownerId)
            ?? await db.Set<OwnerFinancialSnapshot>().AsTracking()
                .FirstOrDefaultAsync(f => f.TenantId == tenant && f.OwnerId == ownerId, ct);
        if (fact is not null) return fact;
        fact = OwnerFinancialSnapshot.Create(tenant, ownerId);
        db.Add(fact);
        return fact;
    }
}
