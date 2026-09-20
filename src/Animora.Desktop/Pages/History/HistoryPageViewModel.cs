using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Animora.Contracts.History;
using Animora.Desktop.Controls;
using Animora.Desktop.Services;
using Animora.Desktop.Shell;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Pages.History;

/// <summary>
/// ViewModel for the History (unified ledger) screen. Mirrors the reference's
/// page controller (Animora-UI/assets/history.js): the KPI bar (<c>renderKpi</c>),
/// the period chips (<c>renderPeriodChips</c>), the module-kind chips
/// (<c>renderCatChips</c>), the summary bar + chronological ledger feed with day
/// grouping and "load more" (<c>renderFeed</c>), and the CSV export
/// (<c>exportCSV</c>). All data flows through <see cref="IHistoryQueryService"/>
/// (the single backend swap point, R-18) — no sample data or mock branching
/// leaks into this public shape.
/// </summary>
public sealed partial class HistoryPageViewModel : PageViewModelBase
{
    private const int PageSize = 40;

    private readonly IHistoryQueryService _service;
    private readonly IToastService _toast;
    private readonly IDialogService _dialogs;

    private int _page = 1;
    private int _totalCount;

    public HistoryPageViewModel(IHistoryQueryService service, IToastService toast, IDialogService dialogs)
    {
        _service = service;
        _toast = toast;
        _dialogs = dialogs;

        Title = "تاریخچه";
        Eyebrow = "دفتر رویدادهای مدیریتی";
        Breadcrumbs.Add("سیستم");
        Breadcrumbs.Add("تاریخچه");

        BuildPeriodChips();
        BuildCategoryChips();
        _ = ReloadAsync();
    }

    // ── Chrome ───────────────────────────────────────────────────────────

    public string PageTitle => "تاریخچه";

    public string PageEyebrow => "دفتر رویدادهای مدیریتی";

    public string PageSubtitle =>
        "تمام عملیات کلینیک — فروش، نوبت، واکسن، هزینه، انبار و چک — در یک دفتر یکپارچه.";

    public string SearchPlaceholder => "جستجو در رویدادها…";

    // ── Collections ──────────────────────────────────────────────────────

    public ObservableCollection<HistoryKpiViewModel> Kpis { get; } = [];

    public ObservableCollection<HistoryPeriodChipViewModel> PeriodChips { get; } = [];

    public RangeObservableCollection<HistoryCategoryChipViewModel> CategoryChips { get; } = [];

    public ObservableCollection<HistoryDayGroupViewModel> DayGroups { get; } = [];

    // ── State ────────────────────────────────────────────────────────────

    [ObservableProperty]
    private string _period = "30";

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _hasSearchValue;

    [ObservableProperty]
    private bool _hasEvents;

    [ObservableProperty]
    private bool _isEmpty;

    [ObservableProperty]
    private bool _hasMore;

    [ObservableProperty]
    private string _summaryCountText = string.Empty;

    [ObservableProperty]
    private string _summaryIncomeText = string.Empty;

    [ObservableProperty]
    private string _summaryExpenseText = string.Empty;

    [ObservableProperty]
    private string _summaryNetText = string.Empty;

    [ObservableProperty]
    private Avalonia.Media.IBrush _summaryNetBrush = HistoryPresentation.Fill("#027a48");

    [ObservableProperty]
    private string _loadMoreText = string.Empty;

    private readonly HashSet<HistoryEventKind> _selectedKinds = [];

    // ── Loading / pipeline ───────────────────────────────────────────────

    private async Task ReloadAsync()
    {
        IsBusy = true;

        try
        {
            var filter = new HistoryFilter(Period, _selectedKinds.ToArray(), SearchText.Trim());
            var events = await _service.GetEventsAsync(filter);

            _totalCount = events.Count;

            // KPI bar is computed over the full filtered set (not just the visible page).
            RenderKpi(events);

            var visible = events.Take(PageSize).ToList();

            RenderSummary(visible, _totalCount);
            RenderFeed(visible);

            HasMore = visible.Count < _totalCount;
            LoadMoreText = HasMore
                ? $"نمایش رویدادهای بیشتر ({HistoryPresentation.Fa(_totalCount - visible.Count)} رویداد دیگر)"
                : string.Empty;
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ── KPI bar (the reference's computeKpi + renderKpi) ─────────────────

    private void RenderKpi(IReadOnlyList<HistoryEvent> events)
    {
        long income = 0, expense = 0;
        var apt = 0;
        var pat = 0;

        foreach (var ev in events)
        {
            if (ev.AmountSign == 1)
            {
                income += ev.Amount;
            }
            else if (ev.AmountSign == -1)
            {
                expense += ev.Amount;
            }

            if (ev.Kind == HistoryEventKind.Appointment)
            {
                apt++;
            }
            else if (ev.Kind == HistoryEventKind.Patient)
            {
                pat++;
            }
        }

        Kpis.Clear();
        Kpis.Add(new HistoryKpiViewModel("جمع رویدادها", HistoryPresentation.Fa(events.Count), "در بازه انتخاب‌شده", "IconReceipt", "#eff8ff", "#1570cd"));
        Kpis.Add(new HistoryKpiViewModel("درآمد کل", HistoryPresentation.Money(income) + " ت", "نوبت + فروش + واکسن + …", "IconWallet", "#ecfdf3", "#027a48"));
        Kpis.Add(new HistoryKpiViewModel("هزینه‌ها", HistoryPresentation.Money(expense) + " ت", "هزینه + خرید + …", "IconReceipt", "#fef3f2", "#d92d20"));
        Kpis.Add(new HistoryKpiViewModel("خالص", HistoryPresentation.Money(income - expense) + " ت", "درآمد − هزینه", "IconChart", "#f0fdfa", "#0d9488"));
        Kpis.Add(new HistoryKpiViewModel("نوبت‌ها", HistoryPresentation.Fa(apt), "در بازه", "IconCalendar", "#eff8ff", "#1570cd"));
        Kpis.Add(new HistoryKpiViewModel("بیمار جدید", HistoryPresentation.Fa(pat), "ثبت‌شده", "IconPaw", "#f4f3ff", "#a48afb"));
    }

    // ── Summary bar (the reference's summaryHtml) ────────────────────────

    private void RenderSummary(IReadOnlyList<HistoryEvent> visible, int total)
    {
        var income = visible.Where(e => e.AmountSign == 1).Sum(e => e.Amount);
        var expense = visible.Where(e => e.AmountSign == -1).Sum(e => e.Amount);
        var net = income - expense;

        SummaryCountText = $"نمایش {HistoryPresentation.Fa(visible.Count)} از {HistoryPresentation.Fa(total)} رویداد";
        SummaryIncomeText = "+" + HistoryPresentation.Money(income) + " ت";
        SummaryExpenseText = "−" + HistoryPresentation.Money(expense) + " ت";
        SummaryNetText = (net >= 0 ? "+" : "−") + HistoryPresentation.Money(Math.Abs(net)) + " ت";
        SummaryNetBrush = HistoryPresentation.Fill(net >= 0 ? "#027a48" : "#d92d20");
    }

    // ── Ledger feed (the reference's renderFeed) ─────────────────────────

    private void RenderFeed(IReadOnlyList<HistoryEvent> visible)
    {
        DayGroups.Clear();

        HasEvents = visible.Count > 0;
        IsEmpty = visible.Count == 0;

        if (visible.Count == 0)
        {
            return;
        }

        // Group by day (the reference's groupByDay), keeping the newest-first order.
        foreach (var group in visible.GroupBy(e => e.Date))
        {
            var rows = group.Select(e => new HistoryEventRowViewModel(e)).ToList();
            var net = group.Where(e => e.AmountSign == 1).Sum(e => e.Amount)
                      - group.Where(e => e.AmountSign == -1).Sum(e => e.Amount);

            DayGroups.Add(new HistoryDayGroupViewModel(
                group.Key,
                HistoryPresentation.DayLabel(group.Key),
                rows.Count,
                net,
                rows));
        }
    }

    // ── Filter chips ─────────────────────────────────────────────────────

    private void BuildPeriodChips()
    {
        PeriodChips.Clear();
        PeriodChips.Add(new HistoryPeriodChipViewModel("today", "امروز") { IsActive = Period == "today" });
        PeriodChips.Add(new HistoryPeriodChipViewModel("7", "۷ روز اخیر") { IsActive = Period == "7" });
        PeriodChips.Add(new HistoryPeriodChipViewModel("30", "۳۰ روز اخیر") { IsActive = Period == "30" });
        PeriodChips.Add(new HistoryPeriodChipViewModel("all", "همه") { IsActive = Period == "all" });
    }

    private void SyncPeriodChips()
    {
        foreach (var chip in PeriodChips)
        {
            chip.IsActive = chip.Key == Period;
        }
    }

    private void BuildCategoryChips()
    {
        CategoryChips.ReplaceAll(HistoryPresentation.AllKinds.Select(kind =>
            new HistoryCategoryChipViewModel(kind) { IsActive = _selectedKinds.Contains(kind) }));

        SyncCategoryChips();
    }

    private void SyncCategoryChips()
    {
        foreach (var chip in CategoryChips)
        {
            chip.IsActive = _selectedKinds.Contains(chip.Kind);
        }
    }

    // ── Commands ─────────────────────────────────────────────────────────

    [RelayCommand]
    private void SetPeriod(string? key)
    {
        if (string.IsNullOrEmpty(key) || key == Period)
        {
            return;
        }

        Period = key;
        SyncPeriodChips();
        _page = 1;
        _ = ReloadAsync();
    }

    /// <summary>«همه» chip — clears the kind selection (the reference's <c>setCat('all')</c>).</summary>
    [RelayCommand]
    private void SetCategoryAll()
    {
        if (_selectedKinds.Count == 0)
        {
            return;
        }

        _selectedKinds.Clear();
        SyncCategoryChips();
        _page = 1;
        _ = ReloadAsync();
    }

    [RelayCommand]
    private void ToggleCategory(HistoryEventKind kind)
    {
        if (!_selectedKinds.Add(kind))
        {
            _selectedKinds.Remove(kind);
        }

        // Selecting every kind is equivalent to "all" (the reference's guard).
        if (_selectedKinds.Count == HistoryPresentation.AllKinds.Count)
        {
            _selectedKinds.Clear();
        }

        SyncCategoryChips();
        _page = 1;
        _ = ReloadAsync();
    }

    [RelayCommand]
    private void ApplySearch()
    {
        _page = 1;
        _ = ReloadAsync();
    }

    [RelayCommand]
    private void ClearSearch()
    {
        SearchText = string.Empty;
        HasSearchValue = false;
        _page = 1;
        _ = ReloadAsync();
    }

    [RelayCommand]
    private void LoadMore()
    {
        _page++;
        _ = LoadMoreAsync();
    }

    private async Task LoadMoreAsync()
    {
        var filter = new HistoryFilter(Period, _selectedKinds.ToArray(), SearchText.Trim());
        var events = await _service.GetEventsAsync(filter);
        var visible = events.Take(_page * PageSize).ToList();

        RenderSummary(visible, _totalCount);
        RenderFeed(visible);

        HasMore = visible.Count < _totalCount;
        LoadMoreText = HasMore
            ? $"نمایش رویدادهای بیشتر ({HistoryPresentation.Fa(_totalCount - visible.Count)} رویداد دیگر)"
            : string.Empty;
    }

    /// <summary>
    /// The reference's <c>exportCSV</c>: build the filtered CSV through the
    /// service, then hand it to the OS save dialog. The save flow is a thin
    /// platform call here (View/command), never inside the service (per the
    /// phase's contract note).
    /// </summary>
    [RelayCommand]
    private async Task ExportCsvAsync()
    {
        var filter = new HistoryFilter(Period, _selectedKinds.ToArray(), SearchText.Trim());
        var csv = await _service.ExportCsvAsync(filter);

        var suggested = "history-" + Animora.Desktop.Localization.JalaliCalendar.Today.Replace('/', '-') + ".csv";
        var saved = await _dialogs.SaveTextFileAsync(suggested, csv, "csv");

        _toast.Show(saved ? "خروجی CSV ذخیره شد" : "خروجی CSV آماده شد", saved ? Tone.Ok : Tone.Info);
    }
}
