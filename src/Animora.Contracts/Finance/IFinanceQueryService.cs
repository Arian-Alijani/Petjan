namespace Animora.Contracts.Finance;

/// <summary>
/// Data seam for the Finance workspace (mirrors the reference's shared
/// <c>finance-page.js</c> + <c>finance-data.js</c> aggregate engine). The
/// desktop ViewModel depends only on this interface; the in-memory
/// <c>MockFinanceQueryService</c> is the single swappable implementation a
/// future backend replaces (R-18). One screen serves both the <c>expense</c>
/// and <c>reports</c> nav entries — the same interface backs both tabs.
/// </summary>
public interface IFinanceQueryService
{
    /// <summary>
    /// The Overview-tab aggregate for the given range id (the reference's
    /// <c>aggregate(range)</c> as consumed by <c>panelOverview</c>). Range ids
    /// mirror the reference's <c>RANGES</c> vocabulary: <c>7d</c>/<c>30d</c>/
    /// <c>90d</c>/<c>ytd</c>.
    /// </summary>
    Task<FinanceOverview> GetOverviewAsync(string range);

    /// <summary>
    /// The Income-tab aggregate for the given range id (the reference's
    /// <c>aggregate(range)</c> as consumed by <c>panelIncome</c>, plus the
    /// income-only channel bars).
    /// </summary>
    Task<FinanceIncome> GetIncomeAsync(string range);

    /// <summary>
    /// The timeframe options for the toolbar (mirrors <c>D.RANGES</c>).
    /// </summary>
    Task<IReadOnlyList<FinanceRange>> GetRangesAsync();

    /// <summary>
    /// The sort vocabulary of the «پرفروش‌ترین کالا و خدمات» card (mirrors
    /// <c>PROD_SORTS</c>: پردرآمدترین/پرفروش‌ترین/پرسودترین). The ViewModel
    /// re-sorts the product list by the chosen key.
    /// </summary>
    Task<IReadOnlyList<FinanceProductSort>> GetProductSortsAsync();
}

/// <summary>
/// One «پرفروش‌ترین» sort option (mirrors <c>PROD_SORTS</c>): the id used by the
/// segmented control and its Persian label.
/// </summary>
public sealed record FinanceProductSort(string Id, string Label);
