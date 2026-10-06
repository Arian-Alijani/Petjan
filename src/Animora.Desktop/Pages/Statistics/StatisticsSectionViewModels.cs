using System.Threading;
using System.Threading.Tasks;
using Animora.Contracts.Statistics;

namespace Animora.Desktop.Pages.Statistics;

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
