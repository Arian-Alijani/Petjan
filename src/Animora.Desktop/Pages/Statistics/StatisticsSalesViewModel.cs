using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Animora.Contracts.Statistics;
using Animora.Desktop.Controls;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Animora.Desktop.Pages.Statistics;

/// <summary>One row of the best-selling products table.</summary>
public sealed record StatisticsProductRow(string Rank, string Name, string Quantity, string Revenue, string Margin,
    double Fraction);

/// <summary>One bar of the revenue-by-source comparison.</summary>
public sealed record StatisticsSourceBarRow(string Name, string Amount, string Share, double Fraction, IBrush Color);

/// <summary>
/// «درآمد و فروش»: revenue per day, best-selling products (server-capped, one batch), source comparison and the
/// paid-vs-unpaid invoice ratio. The reads run concurrently and publish together.
/// </summary>
public sealed partial class StatisticsSalesViewModel(IStatisticsQueryService service) : StatisticsSectionViewModel
{
    private const int TopProductCount = 10;

    public override StatisticsSection Section => StatisticsSection.Sales;

    public override string Label => "درآمد و فروش";

    public RangeObservableCollection<StatisticsProductRow> Products { get; } = [];

    public RangeObservableCollection<StatisticsSourceBarRow> SourceBars { get; } = [];

    [ObservableProperty]
    private IReadOnlyList<BarChartPoint> _salesTrend = [];

    [ObservableProperty]
    private bool _hasSales;

    [ObservableProperty]
    private bool _hasProducts;

    [ObservableProperty]
    private bool _hasSources;

    [ObservableProperty]
    private string _salesSummary = string.Empty;

    [ObservableProperty]
    private string _invoiceTotal = string.Empty;

    [ObservableProperty]
    private string _paidInvoices = string.Empty;

    [ObservableProperty]
    private string _unpaidInvoices = string.Empty;

    [ObservableProperty]
    private string _paidShare = string.Empty;

    [ObservableProperty]
    private bool _hasInvoices;

    [ObservableProperty]
    private IReadOnlyList<BarSegment> _paidSegments = [];

    protected override async Task LoadCoreAsync(StatisticsRange range, CancellationToken ct)
    {
        var seriesTask = service.GetRevenueSeriesAsync(range, ct);
        var productsTask = service.GetTopProductsAsync(range, TopProductCount, ct);
        var sourcesTask = service.GetRevenueBySourceAsync(range, ct);
        var overviewTask = service.GetOverviewAsync(range, ct);
        await Task.WhenAll(seriesTask, productsTask, sourcesTask, overviewTask);

        var series = seriesTask.Result;
        PublishSales(series);
        PublishProducts(productsTask.Result);
        PublishSources(sourcesTask.Result);
        PublishInvoices(overviewTask.Result.Current);
        IsEmpty = !HasSales && !HasProducts && !HasSources && !HasInvoices;
    }

    private void PublishSales(IReadOnlyList<StatisticsSeriesPoint> series)
    {
        var monthly = StatisticsFormat.IsMonthly(series);
        SalesTrend = series.Select(p => new BarChartPoint(p.RevenueMinor, StatisticsFormat.BucketLabel(p.Start, monthly),
            $"{StatisticsFormat.BucketLabel(p.Start, monthly)} · {StatisticsFormat.Money(p.RevenueMinor)}"
            + $" · {StatisticsFormat.Count(p.InvoiceCount)} فاکتور")).ToArray();
        HasSales = series.Any(p => p.RevenueMinor != 0);
        SalesSummary = series.Count == 0 ? string.Empty
            : $"{StatisticsFormat.JalaliShort(series[0].Start)} تا {StatisticsFormat.JalaliShort(series[^1].Start)}";
    }

    private void PublishProducts(IReadOnlyList<TopProduct> products)
    {
        var max = products.Count == 0 ? 0 : products.Max(p => p.RevenueMinor);
        Products.ReplaceAll(products.Select((p, i) => new StatisticsProductRow(StatisticsFormat.Count(i + 1), p.Name,
            StatisticsFormat.Quantity(p.Quantity), StatisticsFormat.Money(p.RevenueMinor),
            p.RevenueMinor <= 0 ? "—" : StatisticsFormat.Percent(p.RevenueMinor - p.CogsMinor, p.RevenueMinor),
            max <= 0 ? 0 : p.RevenueMinor / (double)max)));
        HasProducts = products.Count > 0;
    }

    private void PublishSources(IReadOnlyList<RevenueSourceSlice> sources)
    {
        var positive = sources.Where(s => s.AmountMinor > 0).OrderByDescending(s => s.AmountMinor).ToArray();
        var total = positive.Sum(s => s.AmountMinor);
        SourceBars.ReplaceAll(positive.Select(s => new StatisticsSourceBarRow(StatisticsFormat.SourceName(s.Source),
            StatisticsFormat.Money(s.AmountMinor), StatisticsFormat.Percent(s.AmountMinor, total),
            total == 0 ? 0 : s.AmountMinor / (double)total, StatisticsFormat.Brush(StatisticsFormat.SourceColor(s.Source)))));
        HasSources = positive.Length > 0;
    }

    private void PublishInvoices(StatisticsTotals totals)
    {
        var paid = Math.Clamp(totals.PaidInvoiceCount, 0, totals.InvoiceCount);
        var unpaid = totals.InvoiceCount - paid;
        HasInvoices = totals.InvoiceCount > 0;
        InvoiceTotal = StatisticsFormat.Count(totals.InvoiceCount);
        PaidInvoices = StatisticsFormat.Count(paid);
        UnpaidInvoices = StatisticsFormat.Count(unpaid);
        PaidShare = StatisticsFormat.Percent(paid, totals.InvoiceCount);
        var fraction = totals.InvoiceCount <= 0 ? 0 : paid / (double)totals.InvoiceCount;
        PaidSegments = HasInvoices
            ?
            [
                new BarSegment(fraction, StatisticsFormat.Brush("#0d9488")),
                new BarSegment(1 - fraction, StatisticsFormat.Brush("#f79009")),
            ]
            : [];
    }
}
