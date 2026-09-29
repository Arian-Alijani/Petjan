using Animora.Application.UseCases.Finance.ReadModels;

namespace Animora.Application.Ports;

/// <summary>
/// Projection-only finance reads (C09). Every method reads the three rebuildable facts
/// (<c>DailyFinanceFact</c>, <c>ProductSalesFact</c>, <c>OwnerFinancialSnapshot</c>) — never the live
/// <c>Invoices</c>/<c>JournalLines</c> path (A-RULE 034). The facts are a cache with a proof: the
/// append-only ledger stays the authority and the facts can always be recomputed. The live
/// <c>Expenses</c>/<c>Payments</c> tables are read only for the small expense/payment-method lists,
/// never for the KPI aggregates.
/// </summary>
public interface IFinanceQueries
{
    /// <summary>The current window's totals plus the same-length previous window and the day series.</summary>
    Task<FinanceAggregateReadModel> GetAggregateAsync(string rangeId, int todayLocalDay, CancellationToken ct);

    /// <summary>The window's revenue grouped by the catalogue channel of each sold product/service.</summary>
    Task<IReadOnlyList<FinanceChannelReadModel>> GetChannelsAsync(string rangeId, int todayLocalDay, CancellationToken ct);

    /// <summary>The window's operating-expense totals grouped by category code.</summary>
    Task<IReadOnlyList<FinanceCategoryReadModel>> GetExpenseCategoriesAsync(string rangeId, int todayLocalDay, CancellationToken ct);

    /// <summary>The window's per-product sales (quantity, gross revenue, weighted-average COGS).</summary>
    Task<IReadOnlyList<ProductSalesRow>> GetProductSalesAsync(string rangeId, int todayLocalDay, CancellationToken ct);

    /// <summary>The window's expense ledger rows, newest first.</summary>
    Task<IReadOnlyList<ExpenseRowReadModel>> GetExpenseRowsAsync(string rangeId, int todayLocalDay, CancellationToken ct);

    /// <summary>The window's collected payments grouped by method (cash-flow donut source).</summary>
    Task<IReadOnlyList<PaymentMethodTotal>> GetPaymentMethodsAsync(string rangeId, int todayLocalDay, CancellationToken ct);

    /// <summary>The latest projected local day (yyyyMMdd) present in the facts; 0 when none. The client
    /// cache versions keys by this so a new fact naturally invalidates (A-RULE 033).</summary>
    Task<int> GetMaxProjectedDayAsync(CancellationToken ct);
}
