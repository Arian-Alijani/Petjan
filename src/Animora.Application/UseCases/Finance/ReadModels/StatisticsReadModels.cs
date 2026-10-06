using Animora.Domain.Finance;
using Animora.Domain.Sales;

namespace Animora.Application.UseCases.Finance.ReadModels;

/// <summary>Finance totals of one window, all in minor units, sourced from the daily finance facts.</summary>
/// <param name="GrossRevenueMinor">Invoice subtotals posted in the window (before discount).</param>
/// <param name="DiscountMinor">Invoice discounts granted in the window.</param>
/// <param name="RefundMinor">Money given back in the window.</param>
/// <param name="CogsMinor">Cost of the goods sold in the window.</param>
/// <param name="ExpenseMinor">Posted operating expenses (reversals already netted).</param>
/// <param name="InvoiceCount">Posted invoices, net of voids.</param>
/// <param name="PaidInvoiceCount">Invoices that became fully paid in the window.</param>
/// <param name="VisitCount">Non-cancelled visits started in the window.</param>
public sealed record FinanceTotalsReadModel(long GrossRevenueMinor, long DiscountMinor, long RefundMinor,
    long CogsMinor, long ExpenseMinor, int InvoiceCount, int PaidInvoiceCount, int VisitCount)
{
    /// <summary>Net revenue: gross − discount − refunds.</summary>
    public long RevenueMinor => GrossRevenueMinor - DiscountMinor - RefundMinor;

    /// <summary>Net (operating) profit: revenue − COGS − expenses. Labelled «سود خالص».</summary>
    public long NetMinor => RevenueMinor - CogsMinor - ExpenseMinor;

    /// <summary>Average net revenue per invoice; zero when no invoice was posted.</summary>
    public long AverageInvoiceMinor => InvoiceCount <= 0 ? 0 : RevenueMinor / InvoiceCount;

    public static FinanceTotalsReadModel Empty { get; } = new(0, 0, 0, 0, 0, 0, 0, 0);
}

/// <summary>The window, the previous comparable window of equal length, and both windows' totals.</summary>
public sealed record StatisticsOverviewReadModel(FinanceDayRange Range, FinanceDayRange PreviousRange,
    FinanceTotalsReadModel Current, FinanceTotalsReadModel Previous, FinanceTotalsReadModel Today);

/// <summary>One chart bucket (day / week / Jalali month) starting on <paramref name="Start"/>.</summary>
public sealed record StatisticsSeriesPoint(DateOnly Start, long RevenueMinor, long ExpenseMinor,
    long CogsMinor, int InvoiceCount, int VisitCount);

/// <summary>Revenue split of the window by what was sold: visit services, products, boarding, other.</summary>
public sealed record RevenueSourceReadModel(string Source, long AmountMinor, int LineCount);

/// <summary>One expense ledger row.</summary>
public sealed record ExpenseRowReadModel(string Id, string Number, string CategoryCode, string Title,
    long AmountMinor, DateTimeOffset IncurredUtc, ExpenseState State, PaymentMethod? SettledMethod,
    string? SupplierName, string? Note, string? ReversesExpenseId);

/// <summary>One category's net spend in the window and in the previous comparable window.</summary>
public sealed record ExpenseCategoryTotalReadModel(string CategoryCode, long AmountMinor, long PreviousAmountMinor, int Count);

/// <summary>Expense analytics: totals, per-category breakdown and the per-bucket trend.</summary>
public sealed record ExpenseStatsReadModel(long TotalMinor, long PreviousTotalMinor,
    IReadOnlyList<ExpenseCategoryTotalReadModel> Categories, IReadOnlyList<StatisticsSeriesPoint> Trend);

/// <summary>A best-selling product of the window.</summary>
public sealed record TopProductReadModel(string ProductId, string Name, decimal Quantity, long RevenueMinor, long CogsMinor);

/// <summary>A top client by lifetime spend.</summary>
public sealed record TopClientReadModel(string OwnerId, string FullName, string Phone, long TotalSpendMinor,
    int InvoiceCount, DateTimeOffset? LastInvoiceUtc);

/// <summary>Active patients of one species.</summary>
public sealed record SpeciesSliceReadModel(string Species, int Count);

/// <summary>Client mix of the window: new registrations and the new / returning split of paying clients.</summary>
public sealed record ClientStatsReadModel(int NewOwners, int ActiveOwners, int ReturningOwners, int TotalOwners);
