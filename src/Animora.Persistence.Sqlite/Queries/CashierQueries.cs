using Animora.Application.Abstractions;
using Animora.Application.Ports;
using Animora.Application.Text;
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

internal static class InvoiceOpenQuery
{
    internal static IQueryable<Invoice> OpenInvoices(this IQueryable<Invoice> invoices) => invoices.Where(i =>
        i.State == InvoiceState.Draft || i.State == InvoiceState.Posted || i.State == InvoiceState.PartiallyPaid);
}

public sealed class CashierQueries(AnimoraLocalDbContext db, IOptions<LocalStoreOptions> options) : ICashierQueries
{
    public Task<PagedResult<InvoiceSummaryReadModel>> GetInvoicesAsync(
        CashierInvoiceTab tab, string? search, int page, int pageSize, CancellationToken ct)
    {
        var invoices = db.Set<Invoice>().AsNoTracking();
        invoices = tab switch
        {
            CashierInvoiceTab.Open => invoices.OpenInvoices(),
            CashierInvoiceTab.Settled => invoices.Where(i => i.State == InvoiceState.Paid),
            CashierInvoiceTab.Void => invoices.Where(i => i.State == InvoiceState.Void || i.State == InvoiceState.Refunded),
            _ => invoices,
        };
        if (!string.IsNullOrWhiteSpace(search))
        {
            // Search runs in SQL so the screen never has to hold the whole table to filter it (X29).
            var term = search.Trim();
            var like = "%" + term + "%";
            var ownerIds = db.Set<Owner>().AsNoTracking()
                .Where(o => EF.Functions.Like(o.SearchKey, "%" + PersianText.Normalize(term) + "%"))
                .Select(o => o.Id);
            var petIds = db.Set<Pet>().AsNoTracking().Where(p => EF.Functions.Like(p.Name, like)).Select(p => p.Id);
            invoices = invoices.Where(i => EF.Functions.Like(i.Id, like) || EF.Functions.Like(i.InvoiceNumber, like)
                || (i.OwnerId != null && ownerIds.Contains(i.OwnerId))
                || (i.PetId != null && petIds.Contains(i.PetId))
                || (i.GuestName != null && EF.Functions.Like(i.GuestName, like)));
        }

        // Open states first (draft/posted, partial, paid, void, refunded), then newest first — the order the
        // screen used to apply client-side after pulling every invoice.
        var projected = from i in invoices
            join o in db.Set<Owner>().AsNoTracking() on i.OwnerId equals o.Id into owners
            from o in owners.DefaultIfEmpty()
            join p in db.Set<Pet>().AsNoTracking() on i.PetId equals p.Id into pets
            from p in pets.DefaultIfEmpty()
            orderby (i.State == InvoiceState.Draft || i.State == InvoiceState.Posted ? 0
                : i.State == InvoiceState.PartiallyPaid ? 1
                : i.State == InvoiceState.Paid ? 2
                : i.State == InvoiceState.Void ? 3 : 4), i.IssuedUtc descending, i.Id descending
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
