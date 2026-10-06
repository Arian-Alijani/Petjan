using System.Threading;
using System.Threading.Tasks;
using Animora.Contracts.Statistics;

namespace Animora.Desktop.Pages.Statistics;

/// <summary>«نمای کلی» — KPIs, revenue trend and revenue-by-source (content: Task 6).</summary>
public sealed class StatisticsOverviewViewModel(IStatisticsQueryService service) : StatisticsSectionViewModel
{
    public override StatisticsSection Section => StatisticsSection.Overview;

    public override string Label => "نمای کلی";

    protected override async Task LoadCoreAsync(StatisticsRange range, CancellationToken ct)
    {
        var overview = await service.GetOverviewAsync(range, ct);
        IsEmpty = overview.Current.InvoiceCount == 0 && overview.Current.ExpenseMinor == 0 && overview.Current.VisitCount == 0;
    }
}

/// <summary>«مراجعین» — species mix, new vs returning clients, top clients (content: Task 7).</summary>
public sealed class StatisticsClientsViewModel(IStatisticsQueryService service) : StatisticsSectionViewModel
{
    public override StatisticsSection Section => StatisticsSection.Clients;

    public override string Label => "مراجعین";

    protected override async Task LoadCoreAsync(StatisticsRange range, CancellationToken ct)
    {
        var stats = await service.GetClientStatsAsync(range, ct);
        IsEmpty = stats.TotalOwners == 0;
    }
}

/// <summary>«درآمد و فروش» — sales per day, best-selling products, source comparison (content: Task 7).</summary>
public sealed class StatisticsSalesViewModel(IStatisticsQueryService service) : StatisticsSectionViewModel
{
    public override StatisticsSection Section => StatisticsSection.Sales;

    public override string Label => "درآمد و فروش";

    protected override async Task LoadCoreAsync(StatisticsRange range, CancellationToken ct)
    {
        var series = await service.GetRevenueSeriesAsync(range, ct);
        IsEmpty = series.Count == 0;
    }
}

/// <summary>«هزینه‌ها» — expense ledger, per-category stats and fixed expenses (content: Task 8).</summary>
public sealed class StatisticsExpensesViewModel(IStatisticsQueryService service) : StatisticsSectionViewModel
{
    public override StatisticsSection Section => StatisticsSection.Expenses;

    public override string Label => "هزینه‌ها";

    protected override async Task LoadCoreAsync(StatisticsRange range, CancellationToken ct)
    {
        var stats = await service.GetExpenseStatsAsync(range, ct);
        IsEmpty = stats.TotalMinor == 0 && stats.PreviousTotalMinor == 0;
    }
}
