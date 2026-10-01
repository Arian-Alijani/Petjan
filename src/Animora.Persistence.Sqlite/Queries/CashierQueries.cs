using Animora.Application.Abstractions;
using Animora.Application.Ports;
using Animora.Application.UseCases.Sales.ReadModels;
using Animora.Domain.Cashier;
using Animora.Domain.Patients;
using Animora.Domain.Sales;
using Animora.Domain.ValueObjects;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Animora.Persistence.Sqlite.Queries;

/// <summary>
/// Recorded non-credit settlement, not drawer cash: cheque includes uncleared cheques;
/// wallet is prepaid balance usage; insurance records insurer settlement. Unknown methods fail closed.
/// All aggregates use the product currency (toman), with signed reversing payments retained.
/// </summary>
internal static class PaymentReceiptQuery
{
    internal static IQueryable<Payment> NonCreditSettlements(this IQueryable<Payment> payments,
        DateTimeOffset fromUtc, DateTimeOffset toUtc) => payments.Where(p =>
        p.ReceivedUtc >= fromUtc && p.ReceivedUtc < toUtc && p.Amount.Currency == CurrencyScale.Irt
        && (p.Method == PaymentMethod.Cash || p.Method == PaymentMethod.Card || p.Method == PaymentMethod.Transfer
            || p.Method == PaymentMethod.Cheque || p.Method == PaymentMethod.Wallet || p.Method == PaymentMethod.Insurance));
}

public sealed class CashierQueries(AnimoraLocalDbContext db, IOptions<LocalStoreOptions> options) : ICashierQueries
{
    public Task<PagedResult<InvoiceSummaryReadModel>> GetInvoicesAsync(CashierInvoiceTab tab, int page, int pageSize, CancellationToken ct)
    {
        var invoices = db.Set<Invoice>().AsNoTracking();
        invoices = tab switch
        {
            CashierInvoiceTab.Open => invoices.Where(i => i.State == InvoiceState.Draft || i.State == InvoiceState.Posted || i.State == InvoiceState.PartiallyPaid),
            CashierInvoiceTab.Settled => invoices.Where(i => i.State == InvoiceState.Paid),
            CashierInvoiceTab.Void => invoices.Where(i => i.State == InvoiceState.Void || i.State == InvoiceState.Refunded),
            _ => invoices,
        };
        var projected = from i in invoices
            join o in db.Set<Owner>().AsNoTracking() on i.OwnerId equals o.Id into owners
            from o in owners.DefaultIfEmpty()
            join p in db.Set<Pet>().AsNoTracking() on i.PetId equals p.Id into pets
            from p in pets.DefaultIfEmpty()
            orderby i.IssuedUtc descending, i.Id descending
            select new InvoiceSummaryReadModel(i.Id, i.InvoiceNumber, i.Kind, i.State, i.OwnerId,
                o == null ? i.GuestName ?? "" : o.FullName, p == null ? "" : p.Name,
                o == null ? "" : o.PhonePrimary, i.IssuedUtc, i.GrandTotal.Minor, i.DueTotal.Minor, i.Settlement);
        return projected.ToPagedAsync(page, pageSize, options.Value, ct);
    }

    public async Task<CashierStatsReadModel> GetStatsAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct)
    {
        // One SQL statement, including the empty-store case (the UNION yields no row then).
        var row = await db.Set<Invoice>().AsNoTracking().Select(i => 1)
            .Concat(db.Set<Payment>().AsNoTracking().Select(p => 1))
            .Concat(db.Set<CashSession>().AsNoTracking().Select(s => 1)).Distinct()
            .Select(_ => new
            {
                Outstanding = db.Set<Invoice>().Where(i => i.State != InvoiceState.Void && i.State != InvoiceState.Refunded)
                    .Sum(i => (long?)i.DueTotal.Minor) ?? 0,
                Refunded = db.Set<Invoice>().Where(i => i.State == InvoiceState.Refunded)
                    .Sum(i => (long?)i.PaidTotal.Minor) ?? 0,
                Count = db.Set<Invoice>().Count(),
                Collected = db.Set<Payment>().AsNoTracking().NonCreditSettlements(fromUtc, toUtc)
                    .Sum(p => (long?)p.Amount.Minor) ?? 0,
                Variance = db.Set<CashSession>().Where(s => s.ClosedUtc >= fromUtc && s.ClosedUtc < toUtc)
                    .Sum(s => (long?)s.Variance.Minor) ?? 0,
            }).FirstOrDefaultAsync(ct);
        return row is null ? new CashierStatsReadModel(0, 0, 0, 0, 0) :
            new CashierStatsReadModel(row.Collected, row.Outstanding, row.Refunded, row.Variance, row.Count);
    }

    public async Task<CashierTabCountsReadModel> GetTabCountsAsync(CancellationToken ct)
    {
        var row = await db.Set<Invoice>().AsNoTracking().GroupBy(i => 1).Select(g => new
        {
            Open = g.Count(i => i.State == InvoiceState.Draft || i.State == InvoiceState.Posted || i.State == InvoiceState.PartiallyPaid),
            Settled = g.Count(i => i.State == InvoiceState.Paid),
            Void = g.Count(i => i.State == InvoiceState.Void || i.State == InvoiceState.Refunded),
            All = g.Count(),
        }).FirstOrDefaultAsync(ct);
        return row is null ? new CashierTabCountsReadModel(0, 0, 0, 0) :
            new CashierTabCountsReadModel(row.Open, row.Settled, row.Void, row.All);
    }

    public Task<OpenCashSessionReadModel?> GetOpenSessionAsync(string userId, CancellationToken ct) =>
        db.Set<CashSession>().AsNoTracking().Where(s => s.CashierUserId == userId && s.State == CashSessionState.Open)
            .Select(s => new OpenCashSessionReadModel(s.Id, s.SessionNumber, s.OpeningFloat.Minor,
                s.ExpectedClosing.Minor, s.OpenedUtc)).FirstOrDefaultAsync(ct);

    public Task<bool> HasUnsettledPaymentsAsync(string sessionId, CancellationToken ct) =>
        (from p in db.Set<Payment>().AsNoTracking()
         join i in db.Set<Invoice>().AsNoTracking() on p.InvoiceId equals i.Id
         where p.CashSessionId == sessionId && i.Settlement == SettlementStatus.PendingSettlement
         select i.Id).AnyAsync(ct);
}
