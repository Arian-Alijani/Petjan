using Animora.Application.Abstractions;
using Animora.Application.UseCases.Finance.ReadModels;
using Animora.Domain.Finance;

namespace Animora.Application.Ports;

/// <summary>
/// Read side of the «آمار» screen. Every member is a bounded, tenant-filtered projection over the
/// pre-aggregated finance facts (<c>DailyFinanceFact</c>, <c>ProductSalesFact</c>,
/// <c>OwnerFinancialSnapshot</c>) or an indexed source table; windows are clinic-local Gregorian
/// <c>yyyyMMdd</c> days (<see cref="FinanceDayRange"/>), bucketed with the clinic calendar.
/// </summary>
public interface IStatisticsQueries
{
    /// <summary>Totals of <paramref name="range"/>, of the previous window of equal length and of today.</summary>
    Task<StatisticsOverviewReadModel> GetOverviewAsync(FinanceDayRange range, int todayLocalDay, CancellationToken ct);

    /// <summary>Gap-filled series; <paramref name="bucket"/> is <c>day</c>, <c>week</c> or <c>month</c> (Jalali month).</summary>
    Task<IReadOnlyList<StatisticsSeriesPoint>> GetRevenueSeriesAsync(FinanceDayRange range, string bucket, CancellationToken ct);

    /// <summary>Net line revenue of the window split into <c>visit</c>/<c>product</c>/<c>boarding</c>/<c>other</c>.</summary>
    Task<IReadOnlyList<RevenueSourceReadModel>> GetRevenueBySourceAsync(FinanceDayRange range, CancellationToken ct);

    /// <summary>One page of non-draft expenses, newest first (indexed on <c>IncurredUtc</c>).</summary>
    Task<PagedResult<ExpenseRowReadModel>> GetExpensesAsync(FinanceDayRange range, string? categoryCode,
        int page, int pageSize, CancellationToken ct);

    /// <summary>Per-category totals and trend of the window plus the previous-window comparison.</summary>
    Task<ExpenseStatsReadModel> GetExpenseStatsAsync(FinanceDayRange range, string bucket, CancellationToken ct);

    /// <summary>The <paramref name="take"/> best-selling products of the window by revenue.</summary>
    Task<IReadOnlyList<TopProductReadModel>> GetTopProductsAsync(FinanceDayRange range, int take, CancellationToken ct);

    /// <summary>The <paramref name="take"/> clients with the highest lifetime spend.</summary>
    Task<IReadOnlyList<TopClientReadModel>> GetTopClientsAsync(int take, CancellationToken ct);

    /// <summary>Active patients grouped by species (same rule as the dashboard donut).</summary>
    Task<IReadOnlyList<SpeciesSliceReadModel>> GetSpeciesDistributionAsync(CancellationToken ct);

    /// <summary>New vs returning paying clients of the window.</summary>
    Task<ClientStatsReadModel> GetClientStatsAsync(FinanceDayRange range, CancellationToken ct);

    // ── باشگاه مشتریان (Customer Club) ─────────────────────────────────────────
    /// <summary>The <paramref name="take"/> clients with the most invoices (visit frequency leaderboard).</summary>
    Task<IReadOnlyList<TopClientReadModel>> GetTopVisitorsAsync(int take, CancellationToken ct);

    /// <summary>Active patients whose birthday is the clinic-local day <paramref name="todayLocalDay"/> (<c>yyyyMMdd</c>).</summary>
    Task<IReadOnlyList<BirthdayPetReadModel>> GetTodaysBirthdaysAsync(int todayLocalDay, CancellationToken ct);
}
