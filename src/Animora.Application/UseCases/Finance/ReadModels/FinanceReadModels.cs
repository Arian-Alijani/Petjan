using Animora.Domain.Finance;
using Animora.Domain.Sales;

namespace Animora.Application.UseCases.Finance.ReadModels;

/// <summary>
/// One finance window's totals, summed from the projected <c>DailyFinanceFact</c> rows (C09).
/// A reporting read never touches the live <c>Invoices</c>/<c>JournalLines</c> path (A-RULE 034):
/// the append-only ledger is the truth and these totals are its rebuildable projection.
/// </summary>
public sealed record FinanceTotals(
    long RevenueMinor,
    long CogsMinor,
    long ExpenseMinor,
    long DiscountMinor,
    long InsuranceMinor,
    long RefundMinor,
    int InvoiceCount,
    int PaidInvoiceCount)
{
    /// <summary>An all-zero window (no fact rows in range).</summary>
    public static readonly FinanceTotals Empty = new(0, 0, 0, 0, 0, 0, 0, 0);

    /// <summary>Net profit = revenue − cost of goods sold − operating expenses.</summary>
    public long ProfitMinor => checked(RevenueMinor - CogsMinor - ExpenseMinor);
}

/// <summary>One projected day of the trend series, straight from <c>DailyFinanceFact</c>.</summary>
public sealed record DailyFinanceRow(
    int LocalDay, long RevenueMinor, long CogsMinor, long ExpenseMinor, long RefundMinor);

/// <summary>
/// A per-product/service aggregate over the window, built from <c>ProductSalesFact</c> joined to the
/// catalogue for its display identity. Revenue is gross line revenue; cost is the weighted-average COGS.
/// </summary>
public sealed record ProductSalesRow(
    string ProductId,
    string VariantId,
    string CategoryId,
    string Name,
    string Kind,
    decimal QuantitySold,
    long RevenueMinor,
    long CogsMinor);

/// <summary>One expense category's total over the window (Expense records, non-draft only).</summary>
public sealed record ExpenseCategoryTotal(string CategoryCode, long AmountMinor);

/// <summary>One expense row of the Expense-tab table.</summary>
public sealed record ExpenseRowReadModel(
    string Id,
    string Title,
    string CategoryCode,
    PaymentMethod? Method,
    long AmountMinor,
    DateTimeOffset IncurredUtc,
    string CreatedByUserId);

/// <summary>One payment method's collected total over the window (payments received, positive only).</summary>
public sealed record PaymentMethodTotal(PaymentMethod Method, long AmountMinor);

// ── Query outputs (the shape the Application queries return; the Client maps them to Contracts) ──

/// <summary>Trend + KPI bundle shared by the Overview/Income/Profit tabs.</summary>
public sealed record FinanceAggregateReadModel(
    string RangeId,
    string RangeLabel,
    int Days,
    string Bucket,
    FinanceDayRange CurrentRange,
    FinanceTotals Current,
    FinanceTotals Previous,
    IReadOnlyList<DailyFinanceRow> DailyRows)
{
    /// <summary>Series points bucketed by day or week, exactly like the reference <c>bucketSeries</c>.</summary>
    public IReadOnlyList<FinanceSeriesPointReadModel> Series => FinanceSeries.Bucket(DailyRows, Bucket);
}

/// <summary>One bucketed chart point (day or week) in the trend series.</summary>
public sealed record FinanceSeriesPointReadModel(
    int FromDay, int ToDay, string FromLabel, string ToLabel, long RevenueMinor, long ExpenseMinor, long ProfitMinor);

/// <summary>The full Overview-tab read model.</summary>
public sealed record FinanceOverviewReadModel(
    FinanceAggregateReadModel Aggregate,
    IReadOnlyList<FinanceChannelReadModel> ByChannel,
    IReadOnlyList<FinanceCategoryReadModel> ByCategory,
    IReadOnlyList<ProductSalesRow> Products);

/// <summary>The Income-tab read model: the aggregate plus the revenue-channel breakdown.</summary>
public sealed record FinanceIncomeReadModel(
    FinanceAggregateReadModel Aggregate,
    IReadOnlyList<FinanceChannelReadModel> ByChannel);

/// <summary>The Expense-tab read model: the aggregate plus the category totals and the ledger rows.</summary>
public sealed record FinanceExpenseReadModel(
    FinanceAggregateReadModel Aggregate,
    IReadOnlyList<FinanceCategoryReadModel> ByCategory,
    IReadOnlyList<ExpenseRowReadModel> Rows,
    IReadOnlyList<PaymentMethodTotal> Payments);

/// <summary>The Profit-tab read model: the aggregate plus the most-profitable products.</summary>
public sealed record FinanceProfitReadModel(
    FinanceAggregateReadModel Aggregate,
    IReadOnlyList<ProductSalesRow> Products);

/// <summary>One revenue channel (a catalogue-derived grouping of the window's product revenue).</summary>
public sealed record FinanceChannelReadModel(string Id, long RevenueMinor);

/// <summary>One expense category's total over the window.</summary>
public sealed record FinanceCategoryReadModel(string CategoryCode, long AmountMinor);

/// <summary>
/// Local-day bucketing for the trend series. Day buckets emit one point per projected day; week
/// buckets group consecutive days in sevens (mirrors the reference <c>bucketSeries</c>).
/// </summary>
internal static class FinanceSeries
{
    public static IReadOnlyList<FinanceSeriesPointReadModel> Bucket(
        IReadOnlyList<DailyFinanceRow> days, string bucket)
    {
        if (bucket == "day")
        {
            var points = new List<FinanceSeriesPointReadModel>(days.Count);
            for (var i = 0; i < days.Count; i++)
            {
                var d = days[i];
                points.Add(new FinanceSeriesPointReadModel(
                    d.LocalDay, d.LocalDay, string.Empty, string.Empty,
                    d.RevenueMinor, checked(d.CogsMinor + d.ExpenseMinor), checked(d.RevenueMinor - d.CogsMinor - d.ExpenseMinor)));
            }
            return points;
        }

        var weeks = new List<FinanceSeriesPointReadModel>((days.Count + 6) / 7);
        for (var i = 0; i < days.Count; i += 7)
        {
            var count = Math.Min(7, days.Count - i);
            long revenue = 0, expense = 0;
            for (var j = 0; j < count; j++)
            {
                revenue += days[i + j].RevenueMinor;
                expense += days[i + j].CogsMinor + days[i + j].ExpenseMinor;
            }
            weeks.Add(new FinanceSeriesPointReadModel(
                days[i].LocalDay, days[i + count - 1].LocalDay, string.Empty, string.Empty,
                revenue, expense, checked(revenue - expense)));
        }
        return weeks;
    }
}
