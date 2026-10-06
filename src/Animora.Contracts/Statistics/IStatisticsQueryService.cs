using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Animora.Contracts.Statistics;

/// <summary>
/// Data seam for the «آمار» screen (overview, clients, revenue/sales, expenses + fixed expenses). The production
/// implementation (Animora.Client.Services) reads the local SQLite store through the authorized query pipeline.
/// Every method maps to exactly one dispatcher query/command and takes a trailing <see cref="CancellationToken"/>;
/// a cancelled call throws <see cref="System.OperationCanceledException"/>, any other failure throws
/// <see cref="StatisticsQueryException"/>.
/// </summary>
public interface IStatisticsQueryService
{
    // ── Overview / revenue ───────────────────────────────────────────────────────────
    Task<StatisticsOverview> GetOverviewAsync(StatisticsRange range, CancellationToken ct = default);

    Task<IReadOnlyList<StatisticsSeriesPoint>> GetRevenueSeriesAsync(StatisticsRange range, CancellationToken ct = default);

    Task<IReadOnlyList<RevenueSourceSlice>> GetRevenueBySourceAsync(StatisticsRange range, CancellationToken ct = default);

    Task<IReadOnlyList<TopProduct>> GetTopProductsAsync(StatisticsRange range, int take, CancellationToken ct = default);

    // ── Clients ──────────────────────────────────────────────────────────────────────
    Task<IReadOnlyList<TopClient>> GetTopClientsAsync(int take, CancellationToken ct = default);

    Task<IReadOnlyList<SpeciesSlice>> GetSpeciesDistributionAsync(CancellationToken ct = default);

    Task<ClientStats> GetClientStatsAsync(StatisticsRange range, CancellationToken ct = default);

    // ── Expenses ─────────────────────────────────────────────────────────────────────
    /// <summary>One page (1-based) of the ledger, newest first; <paramref name="categoryCode"/> filters to one category.</summary>
    Task<ExpensePage> GetExpensesAsync(StatisticsRange range, string? categoryCode, int page, int pageSize,
        CancellationToken ct = default);

    Task<ExpenseStats> GetExpenseStatsAsync(StatisticsRange range, CancellationToken ct = default);

    /// <summary>Records and posts a one-off expense; returns its id.</summary>
    Task<string> AddExpenseAsync(NewExpense expense, CancellationToken ct = default);

    /// <summary>Corrects a posted expense with a reversing expense (never an edit); returns the reversing expense id.</summary>
    Task<string> ReverseExpenseAsync(string expenseId, string reason, CancellationToken ct = default);

    // ── Fixed (recurring) expenses ───────────────────────────────────────────────────
    Task<RecurringExpenseList> GetRecurringExpensesAsync(CancellationToken ct = default);

    Task<string> AddRecurringExpenseAsync(RecurringExpenseInput input, CancellationToken ct = default);

    Task<string> EditRecurringExpenseAsync(string recurringExpenseId, RecurringExpenseInput input, CancellationToken ct = default);

    Task SetRecurringExpenseActiveAsync(string recurringExpenseId, bool isActive, CancellationToken ct = default);

    /// <summary>Pays the fixed expense for the current Jalali period (posts a real expense); returns the expense id.</summary>
    Task<string> PayRecurringExpenseAsync(string recurringExpenseId, ExpenseSettlement settlement = ExpenseSettlement.Cash,
        CancellationToken ct = default);
}
