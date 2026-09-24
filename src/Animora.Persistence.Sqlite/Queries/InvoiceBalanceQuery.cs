using Animora.Domain.Sales;
using Animora.Persistence.Sqlite.Context;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.Sqlite.Queries;

/// <summary>Paid / due balance of one invoice, derived from signed payments and adjustments (minor units).</summary>
/// <param name="InvoiceId">Invoice id.</param>
/// <param name="Currency">Invoice currency (ISO code); every payment and adjustment is verified against it.</param>
/// <param name="GrandTotalMinor">Posted grand total.</param>
/// <param name="AdjustmentsMinor">Σ adjustments (positive reduces debt, negative re-opens it).</param>
/// <param name="PaidMinor">Σ signed payments (refunds are negative).</param>
/// <param name="DueMinor"><c>GrandTotal − Paid − Adjustments</c>.</param>
public sealed record InvoiceBalance(
    string InvoiceId, string Currency, long GrandTotalMinor, long AdjustmentsMinor, long PaidMinor, long DueMinor);

/// <summary>Derives invoice balances from the append-only payment ledger.</summary>
public sealed class InvoiceBalanceQuery(AnimoraLocalDbContext db)
{
    /// <summary>Maximum invoice ids accepted per call.</summary>
    public const int MaxInvoiceIds = 500;

    /// <summary>
    /// One SQL command: tenant-filtered invoices with correlated <c>SUM(AmountMinor)</c> over payments grouped by
    /// <c>InvoiceId</c> and over adjustment rows, plus currency-mismatch counts.
    /// </summary>
    /// <param name="invoiceIds">Invoices to include (1…<see cref="MaxInvoiceIds"/>, distinct).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Balance per invoice id; unknown or other-tenant ids are absent.</returns>
    /// <exception cref="InvalidOperationException">A payment or adjustment currency differs from the invoice currency.</exception>
    public async Task<IReadOnlyDictionary<string, InvoiceBalance>> GetAsync(
        IReadOnlyCollection<string> invoiceIds, CancellationToken ct = default)
    {
        var ids = QueryGuards.Ids(invoiceIds, MaxInvoiceIds, nameof(invoiceIds));
        var payments = db.Set<Payment>().AsNoTracking();
        var rows = await db.Set<Invoice>().AsNoTracking()
            .Where(i => ids.Contains(i.Id))
            .Select(i => new
            {
                i.Id,
                i.GrandTotal.Minor,
                i.GrandTotal.Currency,
                Adjustments = i.Adjustments.Sum(a => (long?)a.Amount.Minor) ?? 0L,
                BadAdjustments = i.Adjustments.Count(a => a.Amount.Currency != i.GrandTotal.Currency),
                Paid = payments.Where(p => p.InvoiceId == i.Id && p.TenantId == i.TenantId)
                    .Sum(p => (long?)p.Amount.Minor) ?? 0L,
                BadPayments = payments.Count(p => p.InvoiceId == i.Id && p.TenantId == i.TenantId
                                                  && p.Amount.Currency != i.GrandTotal.Currency),
            })
            .ToListAsync(ct);

        var result = new Dictionary<string, InvoiceBalance>(rows.Count, StringComparer.Ordinal);
        foreach (var r in rows)
        {
            if (r.BadAdjustments != 0 || r.BadPayments != 0)
                throw new InvalidOperationException($"Invoice '{r.Id}' has payments/adjustments not in {r.Currency}.");
            result[r.Id] = new InvoiceBalance(r.Id, r.Currency, r.Minor, r.Adjustments, r.Paid,
                checked(r.Minor - r.Paid - r.Adjustments));
        }
        return result;
    }
}
