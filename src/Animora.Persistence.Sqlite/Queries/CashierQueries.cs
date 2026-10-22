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
        var invoices = FinanceInvoiceProjection.Query(db);
        invoices = tab switch
        {
            CashierInvoiceTab.Open => invoices.Where(i => i.State == InvoiceState.Draft || i.State == InvoiceState.Posted || i.State == InvoiceState.PartiallyPaid),
            // «فاکتورهای اخیر» — every invoice regardless of state; the newest-first ordering below surfaces the recent ones.
            CashierInvoiceTab.Settled => invoices,
            CashierInvoiceTab.Void => invoices.Where(i => i.State == InvoiceState.Void || i.State == InvoiceState.Refunded),
            _ => invoices,
        };
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            var like = "%" + term + "%";
            var ownerIds = db.Set<Owner>().Where(o => EF.Functions.Like(o.SearchKey, "%" + PersianText.Normalize(term) + "%")).Select(o => o.Id);
            invoices = invoices.Where(i => EF.Functions.Like(i.Id, like) || EF.Functions.Like(i.Number, like)
                || (i.OwnerId != null && ownerIds.Contains(i.OwnerId))
                || EF.Functions.Like(i.PetName, like) || EF.Functions.Like(i.OwnerName, like));
        }
        // «فاکتورهای اخیر» orders purely by recency; every other tab keeps the state-grouped ordering.
        var ordered = tab == CashierInvoiceTab.Settled
            ? invoices.OrderByDescending(i => i.IssuedUtc).ThenByDescending(i => i.Id)
            : invoices.OrderBy(i => i.State == InvoiceState.Draft || i.State == InvoiceState.Posted ? 0
                    : i.State == InvoiceState.PartiallyPaid ? 1 : i.State == InvoiceState.Paid ? 2 : i.State == InvoiceState.Void ? 3 : 4)
                .ThenByDescending(i => i.IssuedUtc).ThenByDescending(i => i.Id);
        return FinanceInvoiceProjection.Summaries(ordered).ToPagedAsync(page, pageSize, options.Value, ct);
    }

    public async Task<CashierStatsReadModel> GetStatsAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct)
    {
        var row = await FinanceInvoiceProjection.Query(db).GroupBy(i => 1).Select(g => new
        {
            Outstanding = g.Sum(i => i.State != InvoiceState.Void && i.State != InvoiceState.Refunded ? i.DueMinor : 0),
            Refunded = g.Sum(i => i.State == InvoiceState.Refunded ? i.PaidMinor : 0), Count = g.Count(),
        }).FirstOrDefaultAsync(ct);
        var collected = await FinancePaymentProjection.Query(db).Where(p => p.ReceivedUtc >= fromUtc && p.ReceivedUtc < toUtc
            && (p.Method == PaymentMethod.Cash || p.Method == PaymentMethod.Card || p.Method == PaymentMethod.Transfer
                || p.Method == PaymentMethod.Cheque || p.Method == PaymentMethod.Wallet || p.Method == PaymentMethod.Insurance))
            .SumAsync(p => (long?)p.AmountMinor, ct) ?? 0;
        // Drawer variance remains device-local: the finance read endpoint does not claim a global drawer ledger.
        var variance = await db.Set<CashSession>().Where(s => s.ClosedUtc >= fromUtc && s.ClosedUtc < toUtc)
            .SumAsync(s => (long?)s.Variance.Minor, ct) ?? 0;
        return new CashierStatsReadModel(collected, row?.Outstanding ?? 0, row?.Refunded ?? 0, variance, row?.Count ?? 0);
    }

    public async Task<CashierTabCountsReadModel> GetTabCountsAsync(CancellationToken ct)
    {
        var row = await FinanceInvoiceProjection.Query(db).GroupBy(i => 1).Select(g => new
        {
            Open = g.Count(i => i.State == InvoiceState.Draft || i.State == InvoiceState.Posted || i.State == InvoiceState.PartiallyPaid),
            Settled = g.Count(i => i.State == InvoiceState.Paid),
            Void = g.Count(i => i.State == InvoiceState.Void || i.State == InvoiceState.Refunded), All = g.Count(),
        }).FirstOrDefaultAsync(ct);
        return new CashierTabCountsReadModel(row?.Open ?? 0, row?.Settled ?? 0, row?.Void ?? 0, row?.All ?? 0);
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
