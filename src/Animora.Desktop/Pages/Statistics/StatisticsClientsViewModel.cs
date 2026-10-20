using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Animora.Contracts.Statistics;
using Animora.Desktop.Controls;
using Animora.Desktop.Localization;
using Animora.Desktop.Pages.Dashboard;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Animora.Desktop.Pages.Statistics;

/// <summary>One legend row of the species donut.</summary>
public sealed record StatisticsSpeciesRow(string Name, string IconKey, string Count, string Share, double Fraction, IBrush Color);

/// <summary>One ranked client row (spend leaderboard).</summary>
public sealed record StatisticsTopClientRow(string Rank, string Name, string Phone, string Spend, string Invoices,
    string LastVisit, double Fraction);

/// <summary>
/// «مراجعین»: species mix donut, new vs returning split of the paying clients, the visits-per-day trend and the top
/// clients by lifetime spend. The four reads run concurrently and publish together in one batch.
/// </summary>
public sealed partial class StatisticsClientsViewModel(IStatisticsQueryService service) : StatisticsSectionViewModel
{
    private const int TopClientCount = 10;

    public override StatisticsSection Section => StatisticsSection.Clients;

    public override string Label => "مراجعین";

    public RangeObservableCollection<StatisticsSpeciesRow> Species { get; } = [];

    public RangeObservableCollection<StatisticsTopClientRow> TopClients { get; } = [];

    [ObservableProperty]
    private IReadOnlyList<DonutSlice> _speciesSlices = [];

    [ObservableProperty]
    private IReadOnlyList<BarChartPoint> _visitsTrend = [];

    [ObservableProperty]
    private bool _hasSpecies;

    [ObservableProperty]
    private bool _hasVisits;

    [ObservableProperty]
    private bool _hasTopClients;

    [ObservableProperty]
    private string _speciesTotal = string.Empty;

    [ObservableProperty]
    private string _totalOwners = string.Empty;

    [ObservableProperty]
    private string _newOwners = string.Empty;

    [ObservableProperty]
    private string _activeOwners = string.Empty;

    [ObservableProperty]
    private string _returningOwners = string.Empty;

    [ObservableProperty]
    private string _returningShare = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<BarSegment> _splitSegments = [];

    [ObservableProperty]
    private bool _hasSplit;

    protected override async Task LoadCoreAsync(StatisticsRange range, CancellationToken ct)
    {
        var statsTask = service.GetClientStatsAsync(range, ct);
        var speciesTask = service.GetSpeciesDistributionAsync(ct);
        var topTask = service.GetTopClientsAsync(TopClientCount, ct);
        var seriesTask = service.GetRevenueSeriesAsync(range, ct);
        await Task.WhenAll(statsTask, speciesTask, topTask, seriesTask);

        var stats = statsTask.Result;
        var series = seriesTask.Result;
        await PublishOnUiAsync(() =>
        {
            PublishStats(stats);
            PublishSpecies(speciesTask.Result);
            PublishTopClients(topTask.Result);
            PublishVisits(series);
            IsEmpty = stats.TotalOwners == 0 && speciesTask.Result.Count == 0 && topTask.Result.Count == 0;
        });
    }

    // UI-01: free the per-visit lists when the singleton page is left; the next visit re-queries.
    protected override void ReleaseHeavyData()
    {
        Species.Clear();
        TopClients.Clear();
    }

    private void PublishStats(ClientStats stats)
    {
        TotalOwners = StatisticsFormat.Count(stats.TotalOwners);
        NewOwners = StatisticsFormat.Count(stats.NewOwners);
        ActiveOwners = StatisticsFormat.Count(stats.ActiveOwners);
        ReturningOwners = StatisticsFormat.Count(stats.ReturningOwners);
        ReturningShare = StatisticsFormat.Percent(stats.ReturningOwners, stats.ActiveOwners);
        HasSplit = stats.ActiveOwners > 0;
        var returning = stats.ActiveOwners <= 0 ? 0 : Math.Clamp(stats.ReturningOwners / (double)stats.ActiveOwners, 0, 1);
        SplitSegments = HasSplit
            ?
            [
                new BarSegment(returning, StatisticsFormat.Brush("#0d9488")),
                new BarSegment(1 - returning, StatisticsFormat.Brush("#1570cd")),
            ]
            : [];
    }

    private void PublishSpecies(IReadOnlyList<SpeciesSlice> species)
    {
        var rows = species.Where(s => s.Count > 0).OrderByDescending(s => s.Count).ToArray();
        var total = rows.Sum(s => (long)s.Count);
        var looks = rows.Select(s => (Slice: s, Look: SpeciesPresentation.For(s.Species, s.Species))).ToArray();
        SpeciesSlices = looks.Select(x => new DonutSlice(x.Slice.Count, x.Look.ColorHex)).ToArray();
        Species.ReplaceAll(looks.Select(x => new StatisticsSpeciesRow(x.Look.Name, x.Look.IconKey,
            StatisticsFormat.Count(x.Slice.Count), StatisticsFormat.Percent(x.Slice.Count, total),
            total == 0 ? 0 : x.Slice.Count / (double)total, StatisticsFormat.Brush(x.Look.ColorHex))));
        HasSpecies = rows.Length > 0;
        SpeciesTotal = StatisticsFormat.Number(total);
    }

    private void PublishTopClients(IReadOnlyList<TopClient> clients)
    {
        var max = clients.Count == 0 ? 0 : clients.Max(c => c.TotalSpendMinor);
        TopClients.ReplaceAll(clients.Select((c, i) => new StatisticsTopClientRow(
            StatisticsFormat.Count(i + 1), c.FullName,
            string.IsNullOrWhiteSpace(c.Phone) ? "—" : PersianNumberFormatter.ToPersianDigits(c.Phone),
            StatisticsFormat.Money(c.TotalSpendMinor), $"{StatisticsFormat.Count(c.InvoiceCount)} فاکتور",
            c.LastInvoiceUtc is { } last ? StatisticsFormat.JalaliShort(last) : "—",
            max <= 0 ? 0 : c.TotalSpendMinor / (double)max)));
        HasTopClients = clients.Count > 0;
    }

    private void PublishVisits(IReadOnlyList<StatisticsSeriesPoint> series)
    {
        var monthly = StatisticsFormat.IsMonthly(series);
        VisitsTrend = series.Select(p => new BarChartPoint(p.VisitCount, StatisticsFormat.BucketLabel(p.Start, monthly),
            $"{StatisticsFormat.BucketLabel(p.Start, monthly)} · {StatisticsFormat.Count(p.VisitCount)} مراجعه")).ToArray();
        HasVisits = series.Any(p => p.VisitCount > 0);
    }
}
