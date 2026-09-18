namespace Animora.Contracts.Finance;

/// <summary>
/// One selectable timeframe of the Finance toolbar (mirrors the reference's
/// <c>D.RANGES</c> in Animora-UI/assets/finance-data.js): a day count and the
/// chart bucket granularity (day/week) used to group the daily rows.
/// </summary>
public sealed record FinanceRange(string Id, string Label, int Days, string Bucket);

/// <summary>
/// The period-over-period deltas shown on the KPI cards (mirrors the
/// reference <c>aggregate()</c>'s <c>delta</c> object). Revenue/expense/profit
/// are percent changes; margin is an absolute point difference.
/// </summary>
public sealed record FinanceDeltas(double Revenue, double Expense, double Profit, double Margin);

/// <summary>
/// One bucketed point of the main trend chart (mirrors <c>bucketSeries</c>):
/// the short axis label, the full tooltip label, and the revenue/expense/profit
/// totals for the bucket.
/// </summary>
public sealed record FinanceSeriesPoint(string Label, string Full, long Revenue, long Expense, long Profit);

/// <summary>
/// A revenue channel (mirrors <c>REVENUE_CHANNELS</c> + its aggregated
/// <c>value</c>): display label, icon key and swatch color.
/// </summary>
public sealed record FinanceChannel(string Id, string Label, string Icon, string Color, long Value);

/// <summary>
/// An expense category (mirrors <c>EXPENSE_CATEGORIES</c> + its aggregated
/// <c>value</c>/<c>delta</c>): label, icon, swatch color, the CSS tone class and
/// the percent change vs. the previous window.
/// </summary>
public sealed record FinanceCategory(
    string Id, string Label, string Icon, string Color, string Tone, long Value, double Delta);

/// <summary>
/// A top product/service row (mirrors <c>TOP_PRODUCTS</c>): its kind
/// (service/product), category, icon, tone, quantity, revenue and cost — with
/// profit and margin derived exactly like the reference.
/// </summary>
public sealed record FinanceProduct(
    string Name, string Category, string Kind, string Icon, string Tone, int Qty, long Revenue, long Cost)
{
    /// <summary>Gross profit (<c>revenue - cost</c>).</summary>
    public long Profit => Revenue - Cost;

    /// <summary>Gross margin percent (<c>profit / revenue * 100</c>).</summary>
    public double Margin => Revenue == 0 ? 0 : (double)Profit / Revenue * 100;
}

/// <summary>
/// One side of the «خدمات در برابر کالا» split (mirrors <c>serviceVsProduct()</c>):
/// the two buckets services vs. products, each with revenue/profit/qty and a
/// derived margin.
/// </summary>
public sealed record FinanceServiceSplit(
    string Key, string Label, string Icon, string Color, int Qty, long Revenue, long Profit)
{
    /// <summary>Margin percent for the bucket.</summary>
    public double Margin => Revenue == 0 ? 0 : (double)Profit / Revenue * 100;
}

/// <summary>
/// One income-tab channel-performance bar (mirrors <c>channelBarsCard</c>): the
/// channel plus its share of total revenue.
/// </summary>
public sealed record FinanceChannelBar(
    string Id, string Label, string Icon, string Color, long Value, double SharePercent);

/// <summary>
/// A text insight card (mirrors <c>INSIGHTS</c>): icon key, tone class and the
/// Persian text.
/// </summary>
public sealed record FinanceInsight(string Icon, string Tone, string Text);

/// <summary>
/// A species bar row (mirrors <c>SPECIES</c>): label, visit count and color.
/// </summary>
public sealed record FinanceSpecies(string Label, long Value, string Color);

/// <summary>
/// One payment-method share of the cash-flow donut (mirrors <c>PAYMENT_SPLIT</c>
/// joined to <c>PAYMENT_METHODS</c>): the method label/color and its percent.
/// </summary>
public sealed record FinancePaymentSplit(string Id, string Label, string Color, int Value);

/// <summary>
/// The combined Overview-tab dataset (mirrors the fields <c>panelOverview</c>
/// consumes through <c>D.aggregate</c> + the shared <c>FinanceData</c> tables):
/// the chart series, summary KPIs, channel/category breakdowns, product list,
/// service-vs-product split, insight cards, species bars, payment split and the
/// visit heatmap.
/// </summary>
public sealed record FinanceOverview(
    string RangeLabel,
    int DayCount,
    long RevTotal,
    long ExpTotal,
    long Profit,
    double Margin,
    FinanceDeltas Delta,
    double AvgDailyProfit,
    IReadOnlyList<FinanceSeriesPoint> Series,
    IReadOnlyList<FinanceChannel> ByChannel,
    IReadOnlyList<FinanceCategory> ByCategory,
    IReadOnlyList<FinanceProduct> Products,
    IReadOnlyList<FinanceServiceSplit> Services,
    IReadOnlyList<FinanceInsight> Insights,
    IReadOnlyList<FinanceSpecies> Species,
    IReadOnlyList<FinancePaymentSplit> Payments,
    IReadOnlyList<IReadOnlyList<int>> Heatmap);

/// <summary>
/// The Income-tab dataset (mirrors the fields <c>panelIncome</c> consumes):
/// the shared KPIs/series plus the income-specific channel bars, payment split,
/// species bars and visit heatmap.
/// </summary>
public sealed record FinanceIncome(
    string RangeLabel,
    int DayCount,
    long RevTotal,
    long ExpTotal,
    long Profit,
    double Margin,
    FinanceDeltas Delta,
    double AvgDailyProfit,
    IReadOnlyList<FinanceSeriesPoint> Series,
    IReadOnlyList<FinanceChannel> ByChannel,
    IReadOnlyList<FinanceCategory> ByCategory,
    IReadOnlyList<FinanceChannelBar> ChannelBars,
    IReadOnlyList<FinanceProduct> Products,
    IReadOnlyList<FinanceServiceSplit> Services,
    IReadOnlyList<FinanceInsight> Insights,
    IReadOnlyList<FinanceSpecies> Species,
    IReadOnlyList<FinancePaymentSplit> Payments,
    IReadOnlyList<IReadOnlyList<int>> Heatmap);

/// <summary>
/// One option of the expense modal's «دسته‌بندی» select (mirrors the
/// <c>EXPENSE_CATEGORIES</c> id/label/color the reference's modal renders).
/// </summary>
public sealed record FinanceCategoryOption(string Id, string Label, string Color);

/// <summary>
/// One option of the expense modal's «روش پرداخت» select (mirrors
/// <c>PAYMENT_METHODS</c>).
/// </summary>
public sealed record FinancePaymentMethod(string Id, string Label);

/// <summary>
/// One row of the Expense-tab table (mirrors the reference's <c>EXPENSE_LOG</c>
/// entry joined to its category + payment method, as rendered by
/// <c>expenseTableCard</c>).
/// </summary>
public sealed record FinanceExpenseRow(
    string Id,
    string Description,
    string CategoryLabel,
    string CategoryColor,
    string Date,
    string MethodLabel,
    long Amount,
    string By);

/// <summary>
/// The Expense-tab dataset (mirrors the fields <c>panelExpense</c> consumes):
/// the shared KPIs/series/categories plus the expense ledger rows for the
/// <c>expenseTableCard</c>.
/// </summary>
public sealed record FinanceExpense(
    string RangeLabel,
    int DayCount,
    long RevTotal,
    long ExpTotal,
    FinanceDeltas Delta,
    IReadOnlyList<FinanceSeriesPoint> Series,
    IReadOnlyList<FinanceCategory> ByCategory,
    IReadOnlyList<FinanceExpenseRow> Rows);

/// <summary>
/// A request to record a new expense (mirrors the reference's
/// <c>addExpense(rec)</c> payload from the modal's form fields).
/// </summary>
public sealed record NewExpenseRequest(
    string Description, string CategoryId, string MethodId, long Amount, string Date);

/// <summary>
/// The newly recorded expense, as returned by <c>AddExpenseAsync</c> (mirrors
/// the object <c>D.addExpense</c> returns and the table re-renders).
/// </summary>
public sealed record ExpenseEntry(
    string Id, string Description, string CategoryId, string MethodId, long Amount, string Date, string By);

/// <summary>
/// The Profit-tab dataset (mirrors the fields <c>panelProfit</c> consumes):
/// the margin gauge inputs, the shared KPIs/series/categories, the
/// most-profitable products and the insight cards.
/// </summary>
public sealed record FinanceProfit(
    string RangeLabel,
    int DayCount,
    long RevTotal,
    long ExpTotal,
    long Profit,
    double Margin,
    FinanceDeltas Delta,
    double AvgDailyProfit,
    IReadOnlyList<FinanceSeriesPoint> Series,
    IReadOnlyList<FinanceCategory> ByCategory,
    IReadOnlyList<FinanceProduct> Products,
    IReadOnlyList<FinanceInsight> Insights);
