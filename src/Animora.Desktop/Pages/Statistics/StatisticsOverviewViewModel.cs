using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Animora.Contracts.Statistics;
using Animora.Desktop.Controls;
using Animora.Desktop.Shell;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Animora.Desktop.Pages.Statistics;

/// <summary>One KPI card of the overview (icon key resolved by the shared icon converter).</summary>
public sealed record StatisticsKpi(string IconKey, string Label, string Value, string Foot, string Delta,
    bool IsDeltaPositive, Tone Tone);

/// <summary>One legend row of the revenue-by-source donut.</summary>
public sealed record StatisticsSourceRow(string Name, string Amount, string Share, IBrush Color);

/// <summary>
/// «نمای کلی»: five KPI cards (today's revenue, period revenue, net profit, visits, average invoice) with deltas against
/// the previous comparable window, the revenue/expense/profit trend, and the revenue-by-source donut. The three reads run
/// concurrently and are published together in one batch, so the section never shows a half-updated state.
/// </summary>
public sealed partial class StatisticsOverviewViewModel(IStatisticsQueryService service) : StatisticsSectionViewModel
{
    public override StatisticsSection Section => StatisticsSection.Overview;

    public override string Label => "نمای کلی";

    public RangeObservableCollection<StatisticsKpi> Kpis { get; } = [];

    public RangeObservableCollection<StatisticsSourceRow> Sources { get; } = [];

    [ObservableProperty]
    private IReadOnlyList<TrendChartPoint> _trend = [];

    [ObservableProperty]
    private IReadOnlyList<DonutSlice> _sourceSlices = [];

    [ObservableProperty]
    private bool _hasTrend;

    [ObservableProperty]
    private bool _hasSources;

    [ObservableProperty]
    private string _sourceTotal = string.Empty;

    [ObservableProperty]
    private string _trendSummary = string.Empty;

    protected override async Task LoadCoreAsync(StatisticsRange range, CancellationToken ct)
    {
        var overviewTask = service.GetOverviewAsync(range, ct);
        var seriesTask = service.GetRevenueSeriesAsync(range, ct);
        var sourcesTask = service.GetRevenueBySourceAsync(range, ct);
        await Task.WhenAll(overviewTask, seriesTask, sourcesTask);

        var overview = overviewTask.Result;
        var series = seriesTask.Result;
        var sources = sourcesTask.Result;

        await PublishOnUiAsync(() =>
        {
            Kpis.ReplaceAll(BuildKpis(overview));
            PublishTrend(series);
            PublishSources(sources);
            IsEmpty = overview.Current.InvoiceCount == 0 && overview.Current.ExpenseMinor == 0 && overview.Current.VisitCount == 0
                      && series.All(p => p.RevenueMinor == 0 && p.ExpenseMinor == 0);
        });
    }

    internal static IEnumerable<StatisticsKpi> BuildKpis(StatisticsOverview o)
    {
        var (revDelta, revUp) = StatisticsFormat.Delta(o.Current.RevenueMinor, o.Previous.RevenueMinor);
        var (netDelta, netUp) = StatisticsFormat.Delta(o.Current.NetMinor, o.Previous.NetMinor);
        var (visitDelta, visitUp) = StatisticsFormat.Delta(o.Current.VisitCount, o.Previous.VisitCount);
        var (avgDelta, avgUp) = StatisticsFormat.Delta(o.Current.AverageInvoiceMinor, o.Previous.AverageInvoiceMinor);
        const string vs = "نسبت به بازهٔ قبل";

        yield return new("IconArrowUpRight", "درآمد امروز", StatisticsFormat.Money(o.Today.RevenueMinor),
            $"{StatisticsFormat.Count(o.Today.InvoiceCount)} فاکتور امروز", string.Empty, true, Tone.Brand);
        yield return new("IconWallet", "درآمد بازه", StatisticsFormat.Money(o.Current.RevenueMinor),
            vs, revDelta, revUp, Tone.Ok);
        yield return new("IconChart", "سود خالص", StatisticsFormat.Money(o.Current.NetMinor),
            $"هزینه‌ها: {StatisticsFormat.Money(o.Current.ExpenseMinor)}", netDelta, netUp,
            o.Current.NetMinor >= 0 ? Tone.Info : Tone.Danger);
        yield return new("IconUsers", "تعداد مراجعین", StatisticsFormat.Count(o.Current.VisitCount),
            vs, visitDelta, visitUp, Tone.Lilac);
        yield return new("IconReceipt", "میانگین فاکتور", StatisticsFormat.Money(o.Current.AverageInvoiceMinor),
            $"{StatisticsFormat.Count(o.Current.InvoiceCount)} فاکتور در بازه", avgDelta, avgUp, Tone.Peach);
    }

    private void PublishTrend(IReadOnlyList<StatisticsSeriesPoint> series)
    {
        var monthly = StatisticsFormat.IsMonthly(series);
        Trend = series.Select(p => new TrendChartPoint(p.RevenueMinor, p.ExpenseMinor,
            p.RevenueMinor - p.CogsMinor - p.ExpenseMinor, StatisticsFormat.BucketLabel(p.Start, monthly),
            $"{StatisticsFormat.BucketLabel(p.Start, monthly)} · درآمد {StatisticsFormat.Money(p.RevenueMinor)}"
            + $" · هزینه {StatisticsFormat.Money(p.ExpenseMinor)}")).ToArray();
        HasTrend = series.Count >= 2 && series.Any(p => p.RevenueMinor != 0 || p.ExpenseMinor != 0);
        TrendSummary = series.Count == 0 ? string.Empty
            : $"{StatisticsFormat.JalaliShort(series[0].Start)} تا {StatisticsFormat.JalaliShort(series[^1].Start)}";
    }

    private void PublishSources(IReadOnlyList<RevenueSourceSlice> sources)
    {
        var positive = sources.Where(s => s.AmountMinor > 0).OrderByDescending(s => s.AmountMinor).ToArray();
        var total = positive.Sum(s => s.AmountMinor);
        SourceSlices = positive.Select(s => new DonutSlice(s.AmountMinor, StatisticsFormat.SourceColor(s.Source))).ToArray();
        Sources.ReplaceAll(positive.Select(s => new StatisticsSourceRow(StatisticsFormat.SourceName(s.Source),
            StatisticsFormat.Money(s.AmountMinor), StatisticsFormat.Percent(s.AmountMinor, total),
            StatisticsFormat.Brush(StatisticsFormat.SourceColor(s.Source)))));
        HasSources = positive.Length > 0;
        SourceTotal = StatisticsFormat.Money(total);
    }
}
