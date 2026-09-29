using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Animora.Contracts.Finance;
using Animora.Contracts.Diagnostics;
using Animora.Desktop.Controls;
using Animora.Desktop.Localization;
using Animora.Desktop.Services;
using Animora.Desktop.Shell;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Pages.Finance;

/// <summary>
/// ViewModel for the shared Finance workspace. One instance serves both sidebar
/// entries — <c>expense</c> (opens on the هزینه‌ها tab) and <c>reports</c> (opens
/// on the نمای کلی tab) — mirroring the reference's two HTML entry points that
/// call <c>initFinancePage({ tab, range })</c> with different starting args.
/// Mirrors Animora-UI/assets/finance-page.js: the tab strip (<c>renderTabs</c>),
/// the range picker (<c>renderRange</c>), the tab panels (<c>PANEL_FN</c>), the
/// product-sort segmented control (<c>PROD_SORTS</c>) and the panel dispatch
/// (<c>renderPanel</c>). All data flows through <see cref="IFinanceQueryService"/>
/// (the single backend swap point, R-18) — no sample data or mock branching
/// leaks into this public shape. Expense/Profit tab bodies are Phase 26.
/// </summary>
public sealed partial class FinancePageViewModel : PageViewModelBase
{
    private readonly IFinanceQueryService _service;
    private readonly IStoreReadinessGate _readiness;
    private readonly IToastService _toast;
    private readonly IDialogService _dialogs;

    private string _range = "30d";
    private string _prodSort = "revenue";

    // The two entry configurations the two nav items ask for.
    private string _entryTab = "overview";
    private string _entryRange = "30d";

    public FinancePageViewModel(IFinanceQueryService service, IStoreReadinessGate readiness, IToastService toast, IDialogService dialogs)
    {
        _service = service;
        _readiness = readiness;
        _toast = toast;
        _dialogs = dialogs;

        Title = "مالی، هزینه‌ها و گزارش فروش";
        Eyebrow = "مرکز مالی و هوش کسب‌وکار";
        Breadcrumbs.Add("کسب و کار");
        Breadcrumbs.Add("مالی و هزینه‌ها");

        _ = LoadAsync();
    }

    // ── Chrome ───────────────────────────────────────────────────────────

    public string PageEyebrow => _entryTab == "expense" ? "مرکز مالی و هوش کسب‌وکار" : "مرکز هوش و بینایی";

    public string PageTitle => _entryTab == "expense" ? "مالی، هزینه‌ها و گزارش فروش" : "آمار و گزارش فروش";

    public string PageSubtitle => _entryTab == "expense"
        ? "تصویر کامل درآمد، هزینه و سود خالص — با نمودارهای تعاملی و تحلیل پرفروش‌ترین‌ها."
        : "عملکرد مالی، خدمات پرفروش، رفتار مراجعبین و روند سود — همه در یک نگاه.";

    public string ExportLabel => _entryTab == "expense" ? "خروجی گزارش" : "خروجی PDF";

    /// <summary>The «کشف کلیدی» highlight copy on the هزینه‌ها / سود و تحلیل tabs is built on load.</summary>
    public string PlaceholderText => string.Empty;

    // ── Entry configuration (called by navigation) ───────────────────────

    /// <summary>
    /// Applies a nav entry's initial tab/range and reloads. Called from the
    /// sidebar's navigation parameter (mirrors the reference's two
    /// <c>initFinancePage</c> calls); safe to call before/after load.
    /// </summary>
    public void Configure(string initialTab, string initialRange)
    {
        if (!string.IsNullOrEmpty(initialTab) && Tabs.Any(t => t.Id == initialTab))
        {
            _entryTab = initialTab;
        }

        if (!string.IsNullOrEmpty(initialRange))
        {
            _entryRange = initialRange;
        }

        _range = _entryRange;
        OnPropertyChanged(nameof(PageEyebrow));
        OnPropertyChanged(nameof(PageTitle));
        OnPropertyChanged(nameof(PageSubtitle));
        OnPropertyChanged(nameof(ExportLabel));
        _ = LoadAsync();
    }

    // ── Collections ──────────────────────────────────────────────────────

    public ObservableCollection<FinanceTabViewModel> Tabs { get; } = [];

    public ObservableCollection<FinanceRangeViewModel> Ranges { get; } = [];

    public ObservableCollection<FinanceSortViewModel> Sorts { get; } = [];

    public RangeObservableCollection<FinanceKpiViewModel> Kpis { get; } = [];

    public RangeObservableCollection<FinanceMiniStatViewModel> MiniStats { get; } = [];

    public RangeObservableCollection<FinanceCategoryRowViewModel> CategoryRows { get; } = [];

    public RangeObservableCollection<FinanceProductRowViewModel> ProductRows { get; } = [];

    public RangeObservableCollection<FinanceServiceSplitViewModel> ServiceSplits { get; } = [];

    public RangeObservableCollection<FinanceInsightViewModel> Insights { get; } = [];

    public RangeObservableCollection<FinanceSpeciesViewModel> Species { get; } = [];

    public RangeObservableCollection<FinanceChannelBarViewModel> ChannelBars { get; } = [];

    public RangeObservableCollection<FinanceHeatRowViewModel> HeatRows { get; } = [];

    /// <summary>Expense tab ledger rows (finance.js <c>expenseTableCard</c>).</summary>
    public RangeObservableCollection<FinanceExpenseRowViewModel> ExpenseRows { get; } = [];

    /// <summary>Profit tab «سودآورترین‌ها» rows (finance.js <c>marginLeadersCard</c>).</summary>
    public RangeObservableCollection<FinanceMarginLeaderViewModel> MarginLeaders { get; } = [];

    /// <summary>The heatmap's 12 hour header labels (۹..۲۰).</summary>
    public IReadOnlyList<string> HeatHours { get; } =
        Enumerable.Range(9, 12).Select(h => FinancePresentation.Fa(h)).ToArray();

    /// <summary>The 6-step heatmap legend scale (کم → زیاد).</summary>
    public IReadOnlyList<Avalonia.Media.IBrush> HeatScale { get; } = FinancePresentation.HeatScale();

    // ── State ────────────────────────────────────────────────────────────

    [ObservableProperty]
    private string _activeTab = "overview";

    [ObservableProperty]
    private bool _isOverview;

    [ObservableProperty]
    private bool _isIncome;

    [ObservableProperty]
    private bool _isExpense;

    [ObservableProperty]
    private bool _isProfit;

    [ObservableProperty]
    private bool _hasProducts;

    [ObservableProperty]
    private bool _hasChannels;

    [ObservableProperty]
    private FinanceDonutViewModel? _channelDonut;

    [ObservableProperty]
    private FinanceDonutViewModel? _paymentDonut;

    [ObservableProperty]
    private FinanceHighlightViewModel? _highlight;

    [ObservableProperty]
    private IReadOnlyList<TrendChartPoint> _trendPoints = [];

    // Chart card titles (mirror mainChartCard's per-tab overrides).
    [ObservableProperty]
    private string _chartEyebrow = "درآمد · هزینه · سود";

    [ObservableProperty]
    private string _chartTitle = "روند مالی";

    // ── Derived header texts ─────────────────────────────────────────────

    public string ChartHeading => $"{ChartTitle} — {RangeLabel}";

    public string ChannelsChipText => $"{FinancePresentation.Fa(ChannelCount)} کانال";

    public string CategoriesChipText => $"{FinancePresentation.Fa(CategoryCount)} دسته";

    public string ChannelBarsChipText => $"{FinancePresentation.Fa(ChannelCount)} واحد";

    public string SpeciesChipText => SpeciesTotalText;

    public int ChannelCount { get; private set; }

    public int CategoryCount { get; private set; }

    private string RangeLabel { get; set; } = string.Empty;

    private string SpeciesTotalText { get; set; } = string.Empty;

    // ── Loading ──────────────────────────────────────────────────────────

    private async Task LoadAsync()
    {
        IsBusy = true;
        await _readiness.WaitAsync();

        var ranges = await _service.GetRangesAsync();
        var sorts = await _service.GetProductSortsAsync();

        if (Tabs.Count == 0)
        {
            Tabs.Add(new FinanceTabViewModel("overview", "نمای کلی", "IconDashboard"));
            Tabs.Add(new FinanceTabViewModel("income", "درآمد و فروش", "IconTrendUp"));
            Tabs.Add(new FinanceTabViewModel("expense", "هزینه‌ها", "IconReceipt"));
            Tabs.Add(new FinanceTabViewModel("profit", "سود و تحلیل", "IconPiggy"));

            foreach (var r in ranges)
            {
                Ranges.Add(new FinanceRangeViewModel(r.Id, r.Label));
            }

            foreach (var s in sorts)
            {
                Sorts.Add(new FinanceSortViewModel(s.Id, s.Label));
            }
        }

        SyncChrome();
        await LoadPanelAsync();

        IsBusy = false;
    }

    private async Task LoadPanelAsync()
    {
        if (ActiveTab == "expense")
        {
            await LoadExpenseAsync();
            return;
        }

        if (ActiveTab == "profit")
        {
            await LoadProfitAsync();
            return;
        }

        var overview = await _service.GetOverviewAsync(_range);
        var income = await _service.GetIncomeAsync(_range);
        var data = overview; // Overview carries every field the shared cards need.

        RangeLabel = data.RangeLabel;

        TrendPoints = data.Series
            .Select(p => new TrendChartPoint(p.Revenue, p.Expense, p.Profit, p.Label, p.Full))
            .Reverse()
            .ToArray();

        ChannelCount = data.ByChannel.Count;
        CategoryCount = data.ByCategory.Count;

        BuildKpis(data);
        BuildCategoryRows(data.ByCategory);
        BuildProducts(data.Products);
        BuildDonuts(data);
        BuildServiceSplit(data.Services);
        BuildSpecies(data.Species);
        BuildInsights(data.Insights);

        if (ActiveTab == "income")
        {
            BuildIncomeExtras(income);
            BuildChart("تحلیل فروش", "روند درآمد");
        }
        else
        {
            BuildHighlight(data);
            BuildChart("درآمد · هزینه · سود", "روند مالی");
        }

        OnPropertyChanged(nameof(ChartHeading));
        OnPropertyChanged(nameof(ChannelsChipText));
        OnPropertyChanged(nameof(CategoriesChipText));
        OnPropertyChanged(nameof(ChannelBarsChipText));
        OnPropertyChanged(nameof(SpeciesChipText));
    }

    private async Task LoadExpenseAsync()
    {
        var data = await _service.GetExpenseAsync(_range);
        RangeLabel = data.RangeLabel;
        CategoryCount = data.ByCategory.Count;

        TrendPoints = data.Series
            .Select(p => new TrendChartPoint(p.Revenue, p.Expense, p.Profit, p.Label, p.Full))
            .Reverse()
            .ToArray();

        // Shared trend chart + category breakdown + highlight (mirrors panelExpense).
        BuildCategoryRows(data.ByCategory);
        BuildExpenseRows(data.Rows);
        BuildExpenseMiniStrip(data);
        BuildExpenseHighlight(data);
        BuildChart("کنترل مخارج", "روند هزینه");

        OnPropertyChanged(nameof(ChartHeading));
        OnPropertyChanged(nameof(CategoriesChipText));
        OnPropertyChanged(nameof(ExpenseChipText));
    }

    /// <summary>The expense table's «N ردیف» head chip text.</summary>
    public string ExpenseChipText => $"{FinancePresentation.Fa(ExpenseRows.Count)} ردیف";

    private void BuildExpenseRows(IReadOnlyList<FinanceExpenseRow> rows)
    {
        ExpenseRows.ReplaceAll(rows.Select(r => new FinanceExpenseRowViewModel(
            r.Id, r.Description, r.CategoryLabel, r.CategoryColor,
            PersianNumberFormatter.ToPersianDigits(r.Date), r.MethodLabel,
            FinancePresentation.Money(r.Amount), r.By)));
    }

    private void BuildExpenseMiniStrip(FinanceExpense data)
    {
        var topCat = data.ByCategory.FirstOrDefault();
        MiniStats.ReplaceAll(
        [
            new FinanceMiniStatViewModel("expense", "IconReceipt", "هزینه کل",
                FinancePresentation.FaDec(data.ExpTotal / 1e6, 1), "م تومان",
                FinancePresentation.SignPct(data.Delta.Expense),
                data.Delta.Expense > 0 ? "bad" : "good"),
            new FinanceMiniStatViewModel("expense", "IconCoins", "میانگین روزانه",
                FinancePresentation.FaDec(data.ExpTotal / (double)data.DayCount / 1e6, 1), "م تومان"),
            new FinanceMiniStatViewModel("expense", "IconScale", "نسبت هزینه به درآمد",
                FinancePresentation.Fa(data.RevTotal == 0 ? 0 : (long)Math.Round(data.ExpTotal / (double)data.RevTotal * 100)),
                "٪"),
            new FinanceMiniStatViewModel("expense", "IconTag", "بزرگ‌ترین دسته",
                topCat?.Label ?? "—", null),
        ]);
    }

    private void BuildExpenseHighlight(FinanceExpense data)
    {
        var topCat = data.ByCategory.FirstOrDefault();
        if (topCat is null)
        {
            Highlight = null;
            return;
        }

        var ratio = data.RevTotal == 0 ? 0 : (long)Math.Round(data.ExpTotal / (double)data.RevTotal * 100);
        Highlight = new FinanceHighlightViewModel("warn", "IconAlert", "کشف کلیدی",
        [
            new("بزرگ‌ترین سرفصل هزینه ", false),
            new(topCat.Label, true),
            new(" با ", false),
            new(FinancePresentation.MoneyShort(topCat.Value), true),
            new(" است — نسبت هزینه به درآمد ", false),
            new(FinancePresentation.Fa(ratio) + "٪", true),
            new(".", false),
        ]);
    }

    private async Task LoadProfitAsync()
    {
        var data = await _service.GetProfitAsync(_range);
        RangeLabel = data.RangeLabel;
        CategoryCount = data.ByCategory.Count;

        TrendPoints = data.Series
            .Select(p => new TrendChartPoint(p.Revenue, p.Expense, p.Profit, p.Label, p.Full))
            .Reverse()
            .ToArray();

        MarginPercent = Math.Clamp(data.Margin, 0, 100);
        MarginValueText = FinancePresentation.FaDec(data.Margin, 1);
        MarginChipText = FinancePresentation.FaDec(Math.Abs(data.Delta.Margin), 1) + " واحد";
        MarginChipTone = data.Delta.Margin >= 0 ? "good" : "bad";
        MarginChipIcon = data.Delta.Margin >= 0 ? "IconArrowUpRight" : "IconTrendDown";
        GaugeLegend =
        [
            new FinanceGaugeLegendViewModel("درآمد", FinancePresentation.MoneyShort(data.RevTotal),
                FinancePresentation.Fill(FinancePresentation.IncomeColor)),
            new FinanceGaugeLegendViewModel("هزینه", FinancePresentation.MoneyShort(data.ExpTotal),
                FinancePresentation.Fill(FinancePresentation.ExpenseColor)),
            new FinanceGaugeLegendViewModel("سود خالص", FinancePresentation.MoneyShort(data.Profit),
                FinancePresentation.Fill(FinancePresentation.ProfitColor)),
        ];

        BuildCategoryRows(data.ByCategory);
        BuildMarginLeaders(data.Products);
        BuildProfitMiniStrip(data);
        BuildProfitHighlight(data);
        BuildInsights(data.Insights);
        BuildChart("تحلیل سودآوری", "روند سود");

        OnPropertyChanged(nameof(ChartHeading));
        OnPropertyChanged(nameof(MarginLeadersChipText));
    }

    /// <summary>The profit margin-leaders «N قلم» head chip text.</summary>
    public string MarginLeadersChipText => $"{FinancePresentation.Fa(MarginLeaders.Count)} قلم";

    private void BuildMarginLeaders(IReadOnlyList<FinanceProduct> products)
    {
        MarginLeaders.ReplaceAll(products.OrderByDescending(p => p.Margin).Select(p =>
            new FinanceMarginLeaderViewModel(
                p.Icon, p.Tone, p.Name,
                FinancePresentation.MoneyShort(p.Profit),
                p.Margin / 100.0,
                FinancePresentation.FaPercent(p.Margin))));
    }

    private void BuildProfitMiniStrip(FinanceProfit data)
    {
        MiniStats.ReplaceAll(
        [
            new FinanceMiniStatViewModel("profit", "IconPiggy", "سود خالص",
                FinancePresentation.FaDec(data.Profit / 1e6, 1), "م تومان",
                FinancePresentation.SignPct(data.Delta.Profit), data.Delta.Profit >= 0 ? "good" : "bad",
                data.Delta.Profit >= 0 ? "IconArrowUpRight" : "IconTrendDown"),
            new FinanceMiniStatViewModel("profit", "IconPercent", "حاشیه سود",
                FinancePresentation.FaDec(data.Margin, 1), "٪",
                FinancePresentation.SignPct(data.Delta.Margin), data.Delta.Margin >= 0 ? "good" : "bad",
                data.Delta.Margin >= 0 ? "IconArrowUpRight" : "IconTrendDown"),
            new FinanceMiniStatViewModel("profit", "IconCoins", "سود روزانه",
                FinancePresentation.FaDec(data.AvgDailyProfit / 1e6, 1), "م تومان"),
            new FinanceMiniStatViewModel("profit", "IconWallet", "بازده هر تومان هزینه",
                FinancePresentation.FaDec(data.ExpTotal == 0 ? 0 : (double)data.Profit / data.ExpTotal, 2), "تومان"),
        ]);
    }

    private void BuildProfitHighlight(FinanceProfit data)
    {
        var topMarginProd = data.Products.OrderByDescending(p => p.Margin).FirstOrDefault();
        if (topMarginProd is null)
        {
            Highlight = null;
            return;
        }

        Highlight = new FinanceHighlightViewModel("info", "IconPiggy", "کشف کلیدی",
        [
            new("سودآورترین آیتم ", false),
            new(topMarginProd.Name, true),
            new(" با حاشیه سود ", false),
            new(FinancePresentation.FaPercent(topMarginProd.Margin), true),
            new(" است؛ حاشیه سود کل ", false),
            new(FinancePresentation.FaDec(data.Margin, 1) + "٪", true),
            new(".", false),
        ]);
    }

    /// <summary>The profit margin gauge fill percent (finance.css .fin-gauge-val).</summary>
    [ObservableProperty]
    private double _marginPercent;

    [ObservableProperty]
    private string _marginValueText = string.Empty;

    [ObservableProperty]
    private string _marginChipText = string.Empty;

    [ObservableProperty]
    private string _marginChipTone = "good";

    [ObservableProperty]
    private string _marginChipIcon = "IconArrowUpRight";

    /// <summary>Always true once the profit tab loads (the chart card's delta chip).</summary>
    public bool HasMarginChip => !string.IsNullOrEmpty(MarginChipText);

    public Avalonia.Media.IBrush MarginChipBrush => FinancePresentation.HeadChipBackground(MarginChipTone);

    public Avalonia.Media.IBrush MarginChipForeground => FinancePresentation.HeadChipForeground(MarginChipTone);

    [ObservableProperty]
    private IReadOnlyList<FinanceGaugeLegendViewModel> _gaugeLegend = [];

    private void BuildKpis(FinanceOverview agg)
    {
        var d = agg.Delta;

        Kpis.ReplaceAll(
        [
            new FinanceKpiViewModel(
                true, "income", "IconTrendUp",
                d.Revenue >= 0 ? "IconArrowUpRight" : "IconTrendDown", FinancePresentation.SignPct(d.Revenue), "good",
                FinancePresentation.FaDec(agg.RevTotal / 1e6, 1), "م تومان", null,
                "درآمد کل", "نسبت به بازه قبل"),
            new FinanceKpiViewModel(
                false, "expense", "IconReceipt",
                d.Expense >= 0 ? "IconArrowUpRight" : "IconTrendDown", FinancePresentation.SignPct(d.Expense),
                d.Expense > 0 ? "bad" : "good",
                FinancePresentation.FaDec(agg.ExpTotal / 1e6, 1), "م تومان", null,
                "هزینه کل", "نسبت به بازه قبل"),
            new FinanceKpiViewModel(
                false, "profit", "IconPiggy",
                d.Profit >= 0 ? "IconArrowUpRight" : "IconTrendDown", FinancePresentation.SignPct(d.Profit),
                d.Profit >= 0 ? "good" : "bad",
                FinancePresentation.FaDec(agg.Profit / 1e6, 1), "م تومان", FinancePresentation.ProfitColor,
                "سود خالص", "درآمد منهای هزینه"),
            new FinanceKpiViewModel(
                false, "income", "IconPercent",
                d.Margin >= 0 ? "IconArrowUpRight" : "IconTrendDown",
                FinancePresentation.FaDec(Math.Abs(d.Margin), 1) + " واحد",
                d.Margin >= 0 ? "good" : "bad",
                FinancePresentation.FaDec(agg.Margin, 1), "٪", null,
                "حاشیه سود", "سهم سود از درآمد"),
        ]);
    }

    private void BuildCategoryRows(IReadOnlyList<FinanceCategory> cats)
    {
        var max = cats.Count == 0 ? 1d : cats.Max(c => (double)c.Value);
        CategoryRows.ReplaceAll(cats.Select(c => new FinanceCategoryRowViewModel(
            c.Icon, c.Tone, c.Label, FinancePresentation.MoneyShort(c.Value), c.Color,
            max == 0 ? 0 : c.Value / max,
            FinancePresentation.SignPct(c.Delta),
            c.Delta > 0 ? "bad" : "good")));
    }

    private void BuildProducts(IReadOnlyList<FinanceProduct> products)
    {
        var sort = Sorts.FirstOrDefault(s => s.Id == _prodSort)?.Id ?? "revenue";
        double Key(FinanceProduct p) => sort switch
        {
            "qty" => p.Qty,
            "profit" => p.Profit,
            _ => p.Revenue,
        };

        var sorted = products.OrderByDescending(Key).ToArray();
        var max = sorted.Length == 0 ? 1d : sorted.Max(Key);

        ProductRows.ReplaceAll(sorted.Select((p, i) =>
        {
            var pr = Primary(p, sort);
            return new FinanceProductRowViewModel(
                i + 1, p.Icon, p.Tone, p.Name, p.Kind == "service",
                $"{FinancePresentation.Fa(p.Qty)} فروش · حاشیه سود {FinancePresentation.FaPercent(p.Margin)}",
                max == 0 ? 0 : Key(p) / max, pr.color, pr.main, pr.sub);
        }));

        HasProducts = ProductRows.Count > 0;
    }

    private static (string main, string sub, string color) Primary(FinanceProduct p, string sort) => sort switch
    {
        "qty" => (FinancePresentation.Fa(p.Qty), "مورد فروش", FinancePresentation.IncomeColor),
        "profit" => (FinancePresentation.MoneyShort(p.Profit), "سود ناخالص", FinancePresentation.ProfitColor),
        _ => (FinancePresentation.MoneyShort(p.Revenue), "درآمد", FinancePresentation.IncomeColor),
    };

    private void BuildDonuts(FinanceOverview agg)
    {
        ChannelDonut = BuildChannelDonut(agg);
        PaymentDonut = BuildPaymentDonut(agg);
        HasChannels = agg.ByChannel.Count > 0;
    }

    private static FinanceDonutViewModel BuildChannelDonut(FinanceOverview agg)
    {
        var total = agg.ByChannel.Sum(c => (double)c.Value);
        var slices = agg.ByChannel
            .Select(c => new DonutSlice(c.Value, c.Color))
            .ToArray();
        var legend = agg.ByChannel
            .Select((c, i) => new FinanceLegendRowViewModel(
                i, c.Label, FinancePresentation.MoneyShort(c.Value),
                FinancePresentation.FaPercent(total == 0 ? 0 : c.Value / total * 100),
                c.Color, total == 0 ? 0 : Math.Max(0.08, c.Value / total)))
            .ToArray();
        return new FinanceDonutViewModel(slices, FinancePresentation.MoneyShort(agg.RevTotal), "کل درآمد", legend);
    }

    private static FinanceDonutViewModel BuildPaymentDonut(FinanceOverview agg)
    {
        var total = agg.Payments.Sum(p => (double)p.Value);
        var slices = agg.Payments
            .Select(p => new DonutSlice(p.Value, p.Color))
            .ToArray();
        var legend = agg.Payments
            .Select((p, i) => new FinanceLegendRowViewModel(
                i, p.Label, FinancePresentation.Fa(p.Value) + "٪",
                FinancePresentation.FaPercent(total == 0 ? 0 : p.Value / total * 100),
                p.Color, total == 0 ? 0 : Math.Max(0.08, p.Value / total)))
            .ToArray();
        return new FinanceDonutViewModel(slices, OrderPersian(100) + "٪", "کل تراکنش", legend);
    }

    private void BuildServiceSplit(IReadOnlyList<FinanceServiceSplit> parts)
    {
        var total = parts.Sum(p => (double)p.Revenue);
        if (total == 0)
        {
            ServiceSplits.ReplaceAll([]);
            return;
        }

        ServiceSplits.ReplaceAll(parts.Select(p => new FinanceServiceSplitViewModel(
            p.Label, p.Icon, p.Color,
            FinancePresentation.FaPercent(p.Revenue / total * 100),
            FinancePresentation.MoneyShort(p.Revenue),
            FinancePresentation.Fa(p.Qty),
            FinancePresentation.FaPercent(p.Margin),
            p.Revenue / total)));

        SplitSegments = ServiceSplits
            .Select(s => new BarSegment(s.Fraction, s.ShareBrush))
            .ToArray();
    }

    /// <summary>The «خدمات در برابر کالا» split bar segments (finance.css <c>.svp-bar</c>).</summary>
    [ObservableProperty]
    private IReadOnlyList<BarSegment> _splitSegments = [];

    private void BuildSpecies(IReadOnlyList<FinanceSpecies> species)
    {
        var max = species.Count == 0 ? 1d : species.Max(s => (double)s.Value);
        Species.ReplaceAll(species.Select(s => new FinanceSpeciesViewModel(
            s.Label, FinancePresentation.Fa(s.Value), s.Color, max == 0 ? 0 : s.Value / max)));

        SpeciesTotalText = FinancePresentation.Fa((long)species.Sum(s => s.Value));
    }

    private void BuildInsights(IReadOnlyList<FinanceInsight> insights)
    {
        Insights.ReplaceAll(insights.Select(i =>
            new FinanceInsightViewModel(FinancePresentation.InsightIcon(i.Icon), i.Tone, i.Text)));
    }

    private void BuildChart(string eyebrow, string title)
    {
        ChartEyebrow = eyebrow;
        ChartTitle = title;
    }

    private void BuildHighlight(FinanceOverview agg)
    {
        var topChannel = agg.ByChannel.OrderByDescending(c => c.Value).FirstOrDefault();
        var topRevProd = agg.Products.OrderByDescending(p => p.Revenue).FirstOrDefault();
        if (topChannel is null || topRevProd is null)
        {
            Highlight = null;
            return;
        }

        var share = agg.RevTotal == 0 ? 0 : Math.Round((double)topChannel.Value / agg.RevTotal * 100);
        var spans = new List<RichSpan>
        {
            new("بیشترین درآمد از ", false),
            new(topChannel.Label, true),
            new($" ({FinancePresentation.Fa((long)share)}٪ کل) و پردرآمدترین آیتم ", false),
            new(topRevProd.Name, true),
            new(" با ", false),
            new(FinancePresentation.MoneyShort(topRevProd.Revenue), true),
            new(" است.", false),
        };

        Highlight = new FinanceHighlightViewModel("ok", "IconSpark", "کشف کلیدی", spans);
    }

    private void BuildIncomeExtras(FinanceIncome inc)
    {
        // Income mini strip
        var topChannel = inc.ByChannel.OrderByDescending(c => c.Value).FirstOrDefault();
        MiniStats.ReplaceAll(
        [
            new FinanceMiniStatViewModel("income", "IconTrendUp", "درآمد کل",
                FinancePresentation.FaDec(inc.RevTotal / 1e6, 1), "م تومان",
                FinancePresentation.SignPct(inc.Delta.Revenue), inc.Delta.Revenue >= 0 ? "good" : "bad"),
            new FinanceMiniStatViewModel("income", "IconCoins", "میانگین روزانه",
                FinancePresentation.FaDec(inc.RevTotal / (double)inc.DayCount / 1e6, 1), "م تومان"),
            new FinanceMiniStatViewModel("income", "IconCart", "تراکنش‌های فروش",
                FinancePresentation.Fa(inc.Products.Sum(p => (long)p.Qty)), "مورد"),
            new FinanceMiniStatViewModel("income", "IconTarget", "کانال برتر",
                topChannel?.Label ?? "—", null),
        ]);

        // Channel bars
        var max = inc.ChannelBars.Count == 0 ? 1d : inc.ChannelBars.Max(c => (double)c.Value);
        ChannelBars.ReplaceAll(inc.ChannelBars.Select(c => new FinanceChannelBarViewModel(
            c.Icon, c.Color, c.Label,
            FinancePresentation.FaPercent(c.SharePercent) + " از درآمد کل",
            max == 0 ? 0 : c.Value / max,
            FinancePresentation.MoneyShort(c.Value))));

        // Income highlight
        var parts = inc.Services;
        var svc = parts.FirstOrDefault(p => p.Key == "service");
        var prod = parts.FirstOrDefault(p => p.Key == "product");
        var topQty = inc.Products.OrderByDescending(p => p.Qty).FirstOrDefault();
        if (svc is not null && prod is not null && topQty is not null)
        {
            var totalRev = svc.Revenue + prod.Revenue;
            var svcShare = totalRev == 0 ? 0 : Math.Round((double)svc.Revenue / totalRev * 100);
            Highlight = new FinanceHighlightViewModel("ok", "IconTrendUp", "کشف کلیدی",
            [
                new(FinancePresentation.Fa((long)svcShare) + "٪", true),
                new(" درآمد از ", false),
                new("خدمات و ویزیت", true),
                new(" می‌آید؛ پرفروش‌ترین آیتم ", false),
                new(topQty.Name, true),
                new(" با ", false),
                new(FinancePresentation.Fa(topQty.Qty), true),
                new(" فروش.", false),
            ]);
        }

        // Heatmap rows
        var heatRows = new List<FinanceHeatRowViewModel>(inc.Heatmap.Count);
        for (var d = 0; d < inc.Heatmap.Count; d++)
        {
            var cells = new List<FinanceHeatCellViewModel>();
            var row = inc.Heatmap[d];
            for (var h = 0; h < row.Count; h++)
            {
                var hour = 9 + h;
                cells.Add(new FinanceHeatCellViewModel(row[h],
                    $"{FinancePresentation.HeatDays[d]} — شدت مراجعه {FinancePresentation.Fa(row[h])} ساعت {FinancePresentation.Fa(hour)}"));
            }

            heatRows.Add(new FinanceHeatRowViewModel(FinancePresentation.HeatDays[d], cells));
        }

        HeatRows.ReplaceAll(heatRows);
    }

    private static string OrderPersian(int value) => FinancePresentation.Fa(value);

    // ── Command handling ─────────────────────────────────────────────────

    [RelayCommand]
    private async Task SetTab(string id)
    {
        if (string.IsNullOrEmpty(id) || id == ActiveTab)
        {
            return;
        }

        ActiveTab = id;
        SyncChrome();
        await LoadPanelAsync();
    }

    [RelayCommand]
    private void ToggleTab(FinanceTabViewModel? tab)
    {
        if (tab is not null)
        {
            _ = SetTab(tab.Id);
        }
    }

    [RelayCommand]
    private void ToggleRange(FinanceRangeViewModel? range)
    {
        if (range is not null)
        {
            _ = SetRange(range.Id);
        }
    }

    [RelayCommand]
    private void ToggleSort(FinanceSortViewModel? sort)
    {
        if (sort is not null)
        {
            SetSort(sort.Id);
        }
    }

    [RelayCommand]
    private async Task SetRange(string id)
    {
        if (string.IsNullOrEmpty(id) || id == _range)
        {
            return;
        }

        _range = id;
        SyncChrome();
        await LoadPanelAsync();
    }

    [RelayCommand]
    private void SetSort(string id)
    {
        if (string.IsNullOrEmpty(id) || id == _prodSort)
        {
            return;
        }

        _prodSort = id;
        foreach (var s in Sorts)
        {
            s.IsActive = s.Id == _prodSort;
        }
    }

    [RelayCommand]
    private void Export() =>
        _toast.Show(
            _entryTab == "expense" ? "گزارش آماده دانلود شد (نمونه طراحی)" : "خروجی PDF آماده شد (نمونه طراحی)",
            Tone.Ok);

    /// <summary>Opens the «ثبت هزینه جدید» modal (finance.js <c>openExpenseModal</c>).</summary>
    [RelayCommand]
    private void AddExpense() =>
        _dialogs.ShowModal(new AddExpenseModalViewModel(
            _service, _toast, _dialogs, JalaliCalendar.Today, HighlightNewExpense), 520);

    /// <summary>Prepends the newly recorded expense to the table and flashes its new-row highlight.</summary>
    private void HighlightNewExpense(Contracts.Finance.ExpenseEntry entry)
    {
        var row = new FinanceExpenseRowViewModel(
            entry.Id, entry.Description, CategoryLabel(entry.CategoryId), CategoryColor(entry.CategoryId),
            PersianNumberFormatter.ToPersianDigits(entry.Date), MethodLabel(entry.MethodId),
            FinancePresentation.Money(entry.Amount), entry.By)
        {
            IsNew = true,
        };

        ExpenseRows.Insert(0, row);
        OnPropertyChanged(nameof(ExpenseChipText));
        _ = FlashNewRowAsync(row);
    }

    private static async Task FlashNewRowAsync(FinanceExpenseRowViewModel row)
    {
        await Task.Delay(1600);
        row.IsNew = false;
    }

    private static string CategoryLabel(string id) => id switch
    {
        "salary" => "حقوق و دستمزد",
        "rent" => "اجاره و قبوض",
        "supply" => "خرید دارو و کالا",
        "marketing" => "بازاریابی",
        "repair" => "تعمیر و نگهداری",
        "tax" => "مالیات و بیمه",
        "other" => "متفرقه",
        _ => id,
    };

    private static string CategoryColor(string id) => id switch
    {
        "salary" => "#0d9488",
        "rent" => "#e0a95b",
        "supply" => "#5c8ad6",
        "marketing" => "#b8a2e6",
        "repair" => "#e08a5c",
        "tax" => "#7a8b89",
        "other" => "#b54708",
        _ => "#7a8b89",
    };

    private static string MethodLabel(string id) => id switch
    {
        "pos" => "کارتخوان",
        "cash" => "نقدی",
        "transfer" => "انتقال",
        "credit" => "اعتباری",
        _ => id,
    };

    private void SyncChrome()
    {
        foreach (var t in Tabs)
        {
            t.IsActive = t.Id == ActiveTab;
        }

        foreach (var r in Ranges)
        {
            r.IsActive = r.Id == _range;
        }

        foreach (var s in Sorts)
        {
            s.IsActive = s.Id == _prodSort;
        }

        IsOverview = ActiveTab == "overview";
        IsIncome = ActiveTab == "income";
        IsExpense = ActiveTab == "expense";
        IsProfit = ActiveTab == "profit";
    }

    partial void OnActiveTabChanged(string value) => SyncChrome();
}
