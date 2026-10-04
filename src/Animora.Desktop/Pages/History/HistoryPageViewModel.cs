using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Animora.Contracts.Diagnostics;
using Animora.Contracts.History;
using Animora.Desktop.Controls;
using Animora.Desktop.Localization;
using Animora.Desktop.Services;
using Animora.Desktop.Shell;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Pages.History;

/// <summary>
/// ViewModel for the History (unified ledger) screen. Mirrors the reference's
/// page controller (Animora-UI/assets/history.js): the period chips, the
/// module-kind chips, the summary bar + chronological ledger feed with day
/// grouping and the CSV export.
/// <para>
/// <b>Performance.</b> Every query is <b>server-side</b>: the filter + sort +
/// page window go to <see cref="IHistoryQueryService.GetPageAsync"/>, which
/// returns exactly one bounded page plus the totals computed over the whole
/// filtered set. Keystrokes are debounced (220 ms), each reload carries a
/// generation token so a slow response can never overwrite a newer one, and the
/// day groups are rebuilt through a single <c>ReplaceAll</c> (PERFORMANCE.md §5/§6/§13).
/// </para>
/// </summary>
public sealed partial class HistoryPageViewModel : PageViewModelBase
{
    /// <summary>The page-size choices offered by the toolbar select.</summary>
    private static readonly int[] PageSizes = [25, 50, 100];

    private readonly IHistoryQueryService _service;
    private readonly IStoreReadinessGate _readiness;
    private readonly IToastService _toast;
    private readonly IDialogService _dialogs;
    private readonly Debouncer _searchDebounce = new(TimeSpan.FromMilliseconds(220));

    private readonly HashSet<HistoryEventKind> _selectedKinds = [];

    private HistorySort _sort = HistorySort.Newest;
    private int _pageSize = PageSizes[0];
    private int _page = 1;

    // Set while dates are assigned programmatically (range chips / reset), so the
    // OnFromDateChanged/OnToDateChanged handlers do not fire a second reload.
    private bool _suppressDateReload;

    // Stale-response guard: bumped on every reload; a response whose token no
    // longer matches is discarded (a newer query has superseded it).
    private int _generation;

    public HistoryPageViewModel(IHistoryQueryService service, IStoreReadinessGate readiness, IToastService toast, IDialogService dialogs)
    {
        _service = service;
        _readiness = readiness;
        _toast = toast;
        _dialogs = dialogs;

        Title = "تاریخچه";
        Eyebrow = "دفتر رویدادهای مدیریتی";
        Breadcrumbs.Add("سیستم");
        Breadcrumbs.Add("تاریخچه");

        BuildRangeChips();
        BuildCategoryChips();
        UseLoadStatus(ReloadPageAsync);
    }

    // Singleton page: reload on every visit, keeping the user's filters and page.
    public override void OnNavigatedTo()
    {
        if (!IsLoadInFlight) _ = ReloadPageAsync();
    }

    private Task ReloadPageAsync() => TrackLoadAsync(() => ReloadAsync(resetPage: false));

    // ── Chrome ───────────────────────────────────────────────────────────

    public string PageTitle => "تاریخچه";

    public string PageEyebrow => "دفتر رویدادهای مدیریتی";

    public string PageSubtitle =>
        "تمام عملیات کلینیک — فروش، نوبت، واکسن، هزینه، انبار و چک — در یک دفتر یکپارچه.";

    public string SearchPlaceholder => "جستجو در رویدادها…";

    // ── Collections ──────────────────────────────────────────────────────

    public RangeObservableCollection<HistoryRangeViewModel> RangeChips { get; } = [];

    public RangeObservableCollection<HistoryCategoryChipViewModel> CategoryChips { get; } = [];

    public RangeObservableCollection<HistoryDayGroupViewModel> DayGroups { get; } = [];

    // ── State ────────────────────────────────────────────────────────────

    [ObservableProperty]
    private string _fromDate = string.Empty;

    [ObservableProperty]
    private string _toDate = string.Empty;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _hasSearchValue;

    [ObservableProperty]
    private bool _areAllKindsActive;

    [ObservableProperty]
    private bool _hasEvents;

    [ObservableProperty]
    private bool _isEmpty;

    [ObservableProperty]
    private string _summaryCountText = string.Empty;

    [ObservableProperty]
    private string _summaryIncomeText = string.Empty;

    [ObservableProperty]
    private string _summaryExpenseText = string.Empty;

    [ObservableProperty]
    private string _summaryNetText = string.Empty;

    [ObservableProperty]
    private IBrush _summaryNetBrush = HistoryPresentation.Fill("#027a48");

    [ObservableProperty]
    private string _pageText = "۰ / ۰";

    [ObservableProperty]
    private bool _hasPrev;

    [ObservableProperty]
    private bool _hasNext;

    /// <summary>Active sort select index (0..3), mirroring the toolbar's options.</summary>
    public int SortIndex
    {
        get => (int)_sort;
        set
        {
            var clamped = Math.Clamp(value, 0, 3);
            if ((int)_sort != clamped)
            {
                _sort = (HistorySort)clamped;
                OnPropertyChanged();
                _ = ReloadAsync(resetPage: true);
            }
        }
    }

    /// <summary>Active page-size select index (0..2).</summary>
    public int PageSizeIndex
    {
        get => Array.IndexOf(PageSizes, _pageSize);
        set
        {
            var clamped = Math.Clamp(value, 0, PageSizes.Length - 1);
            var size = PageSizes[clamped];
            if (_pageSize != size)
            {
                _pageSize = size;
                OnPropertyChanged();
                _ = ReloadAsync(resetPage: true);
            }
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        HasSearchValue = !string.IsNullOrWhiteSpace(value);
        _searchDebounce.Debounce(() => _ = ReloadAsync(resetPage: true));
    }

    partial void OnFromDateChanged(string value) => OnDateRangeEdited();

    partial void OnToDateChanged(string value) => OnDateRangeEdited();

    private void OnDateRangeEdited()
    {
        if (_suppressDateReload)
        {
            return;
        }

        SyncRangeChips();
        _ = ReloadAsync(resetPage: true);
    }

    // ── Chip builders ────────────────────────────────────────────────────

    private void BuildRangeChips()
    {
        var today = JalaliCalendar.Today;
        RangeChips.ReplaceAll(
        [
            new HistoryRangeViewModel("today", "امروز", today, today),
            new HistoryRangeViewModel("7", "۷ روز اخیر", JalaliCalendar.AddDays(today, -6), today),
            new HistoryRangeViewModel("30", "۳۰ روز اخیر", JalaliCalendar.AddDays(today, -29), today),
            new HistoryRangeViewModel("all", "همه", string.Empty, string.Empty),
        ]);

        // Default to the last 30 days (mirrors the reference's initial period).
        _suppressDateReload = true;
        FromDate = JalaliCalendar.AddDays(today, -29);
        ToDate = today;
        _suppressDateReload = false;
        SyncRangeChips();
    }

    private void BuildCategoryChips()
    {
        CategoryChips.ReplaceAll(HistoryPresentation.AllKinds.Select(kind =>
            new HistoryCategoryChipViewModel(kind) { IsActive = _selectedKinds.Contains(kind) }));
        SyncAllKindsFlag();
    }

    private void SyncAllKindsFlag() =>
        AreAllKindsActive = CategoryChips.Count > 0 && _selectedKinds.Count == CategoryChips.Count;

    private void SyncRangeChips()
    {
        foreach (var chip in RangeChips)
        {
            chip.IsActive = chip.From == FromDate && chip.To == ToDate;
        }
    }

    // ── Commands ─────────────────────────────────────────────────────────

    [RelayCommand]
    private void SetRange(string? key)
    {
        var chip = RangeChips.FirstOrDefault(c => c.Key == key);
        if (chip is null)
        {
            return;
        }

        _suppressDateReload = true;
        FromDate = chip.From;
        ToDate = chip.To;
        _suppressDateReload = false;

        SyncRangeChips();
        _ = ReloadAsync(resetPage: true);
    }

    [RelayCommand]
    private void ClearRange()
    {
        _suppressDateReload = true;
        FromDate = string.Empty;
        ToDate = string.Empty;
        _suppressDateReload = false;

        SyncRangeChips();
        _ = ReloadAsync(resetPage: true);
    }

    [RelayCommand]
    private void ToggleCategory(HistoryEventKind kind)
    {
        if (!_selectedKinds.Add(kind))
        {
            _selectedKinds.Remove(kind);
        }

        foreach (var chip in CategoryChips)
        {
            chip.IsActive = _selectedKinds.Contains(chip.Kind);
        }

        SyncAllKindsFlag();
        _ = ReloadAsync(resetPage: true);
    }

    /// <summary>«همه» — activate every kind filter at once (or clear them when all are already on).</summary>
    [RelayCommand]
    private void SelectAllKinds()
    {
        var selectAll = !AreAllKindsActive;

        _selectedKinds.Clear();
        foreach (var chip in CategoryChips)
        {
            chip.IsActive = selectAll;
            if (selectAll)
            {
                _selectedKinds.Add(chip.Kind);
            }
        }

        SyncAllKindsFlag();
        _ = ReloadAsync(resetPage: true);
    }

    [RelayCommand]
    private void ClearSearch() => SearchText = string.Empty;

    [RelayCommand]
    private void ResetFilters()
    {
        _selectedKinds.Clear();
        _searchDebounce.Cancel();

        _suppressDateReload = true;
        SearchText = string.Empty;
        FromDate = string.Empty;
        ToDate = string.Empty;
        _suppressDateReload = false;

        HasSearchValue = false;
        foreach (var chip in CategoryChips)
        {
            chip.IsActive = false;
        }

        SyncAllKindsFlag();
        SyncRangeChips();
        _ = ReloadAsync(resetPage: true);
    }

    [RelayCommand]
    private void PrevPage()
    {
        if (_page <= 1)
        {
            return;
        }

        _page--;
        _ = ReloadAsync(resetPage: false);
    }

    [RelayCommand]
    private void NextPage()
    {
        if (!HasNext)
        {
            return;
        }

        _page++;
        _ = ReloadAsync(resetPage: false);
    }

    // ── Loading ──────────────────────────────────────────────────────────

    private async Task ReloadAsync(bool resetPage)
    {
        await _readiness.WaitAsync();
        if (resetPage)
        {
            _page = 1;
        }

        var token = ++_generation;
        IsBusy = true;

        try
        {
            var filter = new HistoryFilter(
                FromDate, ToDate, _selectedKinds.ToArray(), SearchText.Trim(), _sort, _page, _pageSize);

            var page = await _service.GetPageAsync(filter);

            if (token != _generation)
            {
                return; // a newer reload superseded this one — drop the stale result
            }

            ApplyPage(page);
        }
        finally
        {
            if (token == _generation)
            {
                IsBusy = false;
            }
        }
    }

    private void ApplyPage(HistoryPage page)
    {
        var totalPages = Math.Max(1, (int)Math.Ceiling(page.TotalCount / (double)_pageSize));

        if (_page > totalPages)
        {
            _page = totalPages;
            _ = ReloadAsync(resetPage: false);
            return;
        }

        RenderFeed(page.Items, page.DayNets);

        HasEvents = page.TotalCount > 0;
        IsEmpty = page.TotalCount == 0;

        var income = page.IncomeTotal;
        var expense = page.ExpenseTotal;
        var net = income - expense;

        var first = page.TotalCount == 0 ? 0 : ((_page - 1) * _pageSize) + 1;
        var last = (_page - 1) * _pageSize + page.Items.Count;

        SummaryCountText = page.TotalCount == 0
            ? "بدون نتیجه"
            : $"نمایش {HistoryPresentation.Fa(first)}–{HistoryPresentation.Fa(last)} از {HistoryPresentation.Fa(page.TotalCount)} رویداد";
        SummaryIncomeText = "+" + HistoryPresentation.Money(income) + " ت";
        SummaryExpenseText = "−" + HistoryPresentation.Money(expense) + " ت";
        SummaryNetText = (net >= 0 ? "+" : "−") + HistoryPresentation.Money(Math.Abs(net)) + " ت";
        SummaryNetBrush = HistoryPresentation.Fill(net >= 0 ? "#027a48" : "#d92d20");

        PageText = $"{HistoryPresentation.Fa(_page)} / {HistoryPresentation.Fa(totalPages)}";
        HasPrev = _page > 1;
        HasNext = _page < totalPages;

        PrevPageCommand.NotifyCanExecuteChanged();
        NextPageCommand.NotifyCanExecuteChanged();
    }

    // ── Ledger feed (day-grouped; each day net is the full-day figure computed server-side) ─────────────

    private void RenderFeed(IReadOnlyList<HistoryEvent> items, IReadOnlyDictionary<string, long>? dayNets)
    {
        if (items.Count == 0)
        {
            DayGroups.ReplaceAll([]);
            return;
        }

        var groups = new List<HistoryDayGroupViewModel>();
        foreach (var group in items.GroupBy(e => e.Date))
        {
            var rows = group.Select(e => new HistoryEventRowViewModel(e with
            {
                Title = HistoryText.Title(e.Title),
                Detail = HistoryText.Detail(e.Detail),
            })).ToList();
            var net = dayNets is not null && dayNets.TryGetValue(group.Key, out var fullDayNet) ? fullDayNet : 0;

            groups.Add(new HistoryDayGroupViewModel(
                group.Key,
                HistoryPresentation.DayLabel(group.Key),
                rows.Count,
                net,
                rows));
        }

        DayGroups.ReplaceAll(groups);
    }

    // ── CSV export (over the whole filtered set) ─────────────────────────

    private static readonly HistoryCsvLabels _csvLabels = new(
        ["تاریخ", "زمان", "عنوان", "جزئیات", "موجودیت", "مبلغ", "علامت", "وضعیت", "کاربر"],
        HistoryText.Title,
        HistoryText.Detail,
        HistoryPresentation.StatusLabel);

    [RelayCommand]
    private async Task ExportCsvAsync()
    {
        var filter = new HistoryFilter(
            FromDate, ToDate, _selectedKinds.ToArray(), SearchText.Trim(), _sort, 1, _pageSize);
        var csv = await _service.ExportCsvAsync(filter, _csvLabels);

        var suggested = "history-" + JalaliCalendar.Today.Replace('/', '-') + ".csv";
        var saved = await _dialogs.SaveTextFileAsync(suggested, csv, "csv");

        _toast.Show(saved ? "خروجی CSV ذخیره شد" : "خروجی CSV آماده شد", saved ? Tone.Ok : Tone.Info);
    }
}
