using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Animora.Contracts.Cheques;
using Animora.Desktop.Controls;
using Animora.Desktop.Localization;
using Animora.Desktop.Services;
using Animora.Desktop.Shell;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Pages.Cheques;

/// <summary>
/// ViewModel for the Cheques (treasury) screen. Mirrors the reference's page
/// controller (Animora-UI/assets/cheques.js): the KPI strip (<c>renderKpis</c>),
/// the direction tabs + status chips + bank/range selects (<c>renderChips</c>/
/// <c>syncControls</c>), the four render modes (<c>renderTable</c>/<c>pager</c>,
/// <c>renderGrid</c>, <c>renderCalendar</c>, <c>renderTimeline</c>), the side
/// column (<c>renderSide</c>/<c>cardCashflow</c>/<c>cardBanks</c>/<c>cardAlerts</c>),
/// the bulk-actions bar (<c>renderBulk</c>) and the row "more" menu
/// (<c>openRowMenu</c>). All data flows through <see cref="IChequesQueryService"/>
/// (the single backend swap point, R-18) — no sample data or mock branching
/// leaks into this public shape.
/// </summary>
public sealed partial class ChequesPageViewModel : PageViewModelBase
{
    private const int PageSize = 8;
    private const int GridPageSize = 9;

    private readonly IChequesQueryService _service;
    private readonly IToastService _toast;
    private readonly IDialogService _dialogs;

    private readonly List<Cheque> _cheques = [];
    private readonly Dictionary<string, ChequeBank> _banks = new(StringComparer.Ordinal);
    private readonly HashSet<ChequeStatus> _statuses = [];

    private string _tab = "all";
    private string _bank = string.Empty;
    private string _range = "all";
    private string _sortKey = "due";
    private string _sortDir = "asc";
    private int _page = 1;
    private string _today = string.Empty;

    public ChequesPageViewModel(IChequesQueryService service, IToastService toast, IDialogService dialogs)
    {
        _service = service;
        _toast = toast;
        _dialogs = dialogs;

        Title = "مدیریت چک";
        Eyebrow = "خزانه‌داری و اسناد دریافتنی";
        Breadcrumbs.Add("مالی");
        Breadcrumbs.Add("چک‌ها");

        _ = LoadAsync();
    }

    // ── Chrome ───────────────────────────────────────────────────────────

    public string PageTitle => "مدیریت چک";

    public string PageEyebrow => "خزانه‌داری و اسناد دریافتنی";

    public string PageSubtitle => "چک‌های دریافتی و پرداختی، سررسیدها و وضعیت وصول — همه در یک نگاه.";

    public string SearchPlaceholder => "جستجوی شماره چک، صیادی، طرف حساب یا مبلغ… (کلید /)";

    // ── Collections ──────────────────────────────────────────────────────

    public ObservableCollection<ChequeKpiViewModel> Kpis { get; } = [];

    public ObservableCollection<ChequeTabViewModel> Tabs { get; } = [];

    public ObservableCollection<ChequeViewModeViewModel> ViewModes { get; } = [];

    public ObservableCollection<ChequeStatusChipViewModel> StatusChips { get; } = [];

    public RangeObservableCollection<ChequeRowViewModel> TableRows { get; } = [];

    public ObservableCollection<ChequeLeafViewModel> GridLeaves { get; } = [];

    public ObservableCollection<ChequeCalendarCellViewModel> CalendarCells { get; } = [];

    public ObservableCollection<ChequeTimelineGroupViewModel> TimelineGroups { get; } = [];

    public ObservableCollection<ChequeBankRowViewModel> BankRows { get; } = [];

    public ObservableCollection<ChequeAlertViewModel> Alerts { get; } = [];

    public ObservableCollection<ChequeRowViewModel> SelectedCheques { get; } = [];

    public ObservableCollection<ChequeBankOptionViewModel> BankFilterOptions { get; } = [];

    public ObservableCollection<ChequeRangeOptionViewModel> RangeOptions { get; } = [];

    // ── State ────────────────────────────────────────────────────────────

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _hasSearchValue;

    [ObservableProperty]
    private ChequeViewMode _viewMode = ChequeViewMode.Table;

    [ObservableProperty]
    private ChequeBankOptionViewModel? _selectedBankFilter;

    [ObservableProperty]
    private ChequeRangeOptionViewModel? _selectedRangeOption;

    [ObservableProperty]
    private ChequePagerViewModel? _pager;

    [ObservableProperty]
    private ChequeCashflowViewModel? _cashflow;

    [ObservableProperty]
    private string _listHeadSummary = string.Empty;

    [ObservableProperty]
    private bool _hasRows;

    public bool HasGridRows => GridLeaves.Count > 0;

    public bool HasTimelineRows => TimelineGroups.Count > 0;

    /// <summary>The today badge fill (brand-600, the reference's <c>.cal-cell.today .cal-d</c>).</summary>
    public Avalonia.Media.IBrush TodayBadgeBrush => ChequesPresentation.Fill("#0d9488");

    [ObservableProperty]
    private int _calendarYear = 1405;

    [ObservableProperty]
    private int _calendarMonth = 5;

    public bool IsTableView => ViewMode == ChequeViewMode.Table;

    public bool IsGridView => ViewMode == ChequeViewMode.Grid;

    public bool IsCalendarView => ViewMode == ChequeViewMode.Calendar;

    public bool IsTimelineView => ViewMode == ChequeViewMode.Timeline;

    /// <summary>The Saturday-first weekday headers of the calendar grid (J.DAYS).</summary>
    public IReadOnlyList<string> CalendarDayHeaders { get; } =
        ["شنبه", "یک‌شنبه", "دوشنبه", "سه‌شنبه", "چهارشنبه", "پنج‌شنبه", "جمعه"];

    public string CalendarTitle => $"{PersianMonthNames.Get(CalendarMonth)} {ChequesPresentation.Fa(CalendarYear)}";

    [ObservableProperty]
    private string _calendarMonthTotal = string.Empty;

    public bool HasSelection => SelectedCheques.Count > 0;

    public string BulkCountText => $"{ChequesPresentation.Fa(SelectedCheques.Count)} چک انتخاب شده";

    public string BulkSumText => $"مجموع {ChequesPresentation.Money(SelectedCheques.Sum(r => r.Amount))} تومان";

    public bool ShowResetChip => _statuses.Count > 0 || _bank.Length > 0 || HasSearchValue || _range != "all";

    partial void OnViewModeChanged(ChequeViewMode value)
    {
        OnPropertyChanged(nameof(IsTableView));
        OnPropertyChanged(nameof(IsGridView));
        OnPropertyChanged(nameof(IsCalendarView));
        OnPropertyChanged(nameof(IsTimelineView));
        RenderAll();
    }

    partial void OnSearchTextChanged(string value)
    {
        HasSearchValue = value.Trim().Length > 0;
        _page = 1;
        RenderAll();
    }

    partial void OnCalendarYearChanged(int value) => OnPropertyChanged(nameof(CalendarTitle));

    partial void OnCalendarMonthChanged(int value) => OnPropertyChanged(nameof(CalendarTitle));

    // ── Loading ─────────────────────────────────────────────────────────

    private async Task LoadAsync()
    {
        IsBusy = true;

        _today = JalaliCalendar.Today;
        var today = JalaliCalendar.Parse(_today);
        CalendarYear = today.Year;
        CalendarMonth = today.Month;

        BuildTabs();
        BuildViewModes();
        BuildRanges();

        _cheques.Clear();
        _cheques.AddRange(await _service.GetAllAsync());

        foreach (var bank in await _service.GetBanksAsync())
        {
            _banks[bank.Key] = bank;
        }

        BankFilterOptions.Clear();
        BankFilterOptions.Add(new ChequeBankOptionViewModel(string.Empty, "همه بانک‌ها"));
        foreach (var bank in _banks.Values)
        {
            BankFilterOptions.Add(new ChequeBankOptionViewModel(bank.Key, bank.Name));
        }

        SelectedBankFilter = BankFilterOptions[0];

        Cashflow = new ChequeCashflowViewModel(await _service.GetCashflowAsync());

        await RefreshAlertsAsync();
        RenderKpis();
        RenderAll();

        IsBusy = false;
    }

    private void BuildTabs()
    {
        Tabs.Clear();
        Tabs.Add(new ChequeTabViewModel("all", "همه چک‌ها", null));
        Tabs.Add(new ChequeTabViewModel("in", "دریافتی", "IconArrowDownLeft"));
        Tabs.Add(new ChequeTabViewModel("out", "پرداختی", "IconArrowUpRight"));
    }

    private void BuildViewModes()
    {
        ViewModes.Clear();
        ViewModes.Add(new ChequeViewModeViewModel(ChequeViewMode.Table, "IconRows", "نمای جدول"));
        ViewModes.Add(new ChequeViewModeViewModel(ChequeViewMode.Grid, "IconGrid", "نمای برگ چک"));
        ViewModes.Add(new ChequeViewModeViewModel(ChequeViewMode.Calendar, "IconCalendar", "تقویم سررسید"));
        ViewModes.Add(new ChequeViewModeViewModel(ChequeViewMode.Timeline, "IconTimeline", "نوار زمانی سررسید"));
    }

    private void BuildRanges()
    {
        RangeOptions.Clear();
        RangeOptions.Add(new ChequeRangeOptionViewModel("all", "همه سررسیدها"));
        RangeOptions.Add(new ChequeRangeOptionViewModel("overdue", "سررسید گذشته"));
        RangeOptions.Add(new ChequeRangeOptionViewModel("week", "۷ روز آینده"));
        RangeOptions.Add(new ChequeRangeOptionViewModel("month", "۳۰ روز آینده"));
        RangeOptions.Add(new ChequeRangeOptionViewModel("quarter", "۹۰ روز آینده"));
        SelectedRangeOption = RangeOptions[0];
    }

    private ChequeBank BankOf(string key) =>
        _banks.TryGetValue(key, out var bank)
            ? bank
            : new ChequeBank(key, "—", "—", "#7a8b89", "#9aa8a5");

    // ── Filtering + sorting (filtered()) ─────────────────────────────────

    private static bool IsOpen(Cheque c) => c.Status is ChequeStatus.InHand or ChequeStatus.AtBank;

    private int DaysLeft(Cheque c) => JalaliCalendar.Diff(c.DueDate, _today);

    private List<Cheque> Filtered()
    {
        var q = PersianText.Normalize(ChequesPresentation.FaText(SearchText.Trim().ToLowerInvariant()));
        var list = _cheques.Where(c =>
        {
            if (_tab != "all" && (c.Direction == ChequeDirection.In ? "in" : "out") != _tab)
            {
                return false;
            }

            if (_statuses.Count > 0 && !_statuses.Contains(c.Status))
            {
                return false;
            }

            if (_bank.Length > 0 && c.BankKey != _bank)
            {
                return false;
            }

            if (_range != "all")
            {
                var d = DaysLeft(c);
                if (_range == "overdue" && !(d < 0 && IsOpen(c)))
                {
                    return false;
                }

                if (_range == "week" && !(d >= 0 && d <= 7))
                {
                    return false;
                }

                if (_range == "month" && !(d >= 0 && d <= 30))
                {
                    return false;
                }

                if (_range == "quarter" && !(d >= 0 && d <= 90))
                {
                    return false;
                }
            }

            if (q.Length > 0)
            {
                var hay = string.Join(' ', c.Number, c.Sayad, c.Party, c.Subject, c.Reference, c.Id,
                    BankOf(c.BankKey).Name, c.Branch, c.Amount.ToString()).ToLowerInvariant();
                if (!hay.Contains(q, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }).ToList();

        var sign = _sortDir == "asc" ? 1 : -1;
        return _sortKey switch
        {
            "amount" => list.OrderBy(c => (c.Amount * sign)).ToList(),
            "party" => (sign == 1
                ? list.OrderBy(c => c.Party, StringComparer.CurrentCulture)
                : list.OrderByDescending(c => c.Party, StringComparer.CurrentCulture)).ToList(),
            "status" => (sign == 1
                ? list.OrderBy(c => c.Status.ToString(), StringComparer.Ordinal)
                : list.OrderByDescending(c => c.Status.ToString(), StringComparer.Ordinal)).ToList(),
            "no" => list.OrderBy(c => double.Parse(c.Number) * sign).ToList(),
            _ => list.OrderBy(c => JalaliCalendar.ToDay(c.DueDate) * sign).ToList(),
        };
    }

    private static long Sum(IEnumerable<Cheque> list) => list.Sum(c => c.Amount);

    // ── Render chip counts + KPI strip ───────────────────────────────────

    private void RenderKpis()
    {
        var all = _cheques;
        var inOpen = all.Where(c => c.Direction == ChequeDirection.In && IsOpen(c)).ToList();
        var outOpen = all.Where(c => c.Direction == ChequeDirection.Out && IsOpen(c)).ToList();
        var soon = all.Where(c => IsOpen(c) && DaysLeft(c) >= 0 && DaysLeft(c) <= 7).ToList();
        var bounced = all.Where(c => c.Status == ChequeStatus.Bounced).ToList();
        var cleared = all.Where(c => c.Status == ChequeStatus.Cleared).ToList();
        var overdue = all.Where(c => IsOpen(c) && DaysLeft(c) < 0).ToList();

        Kpis.Clear();

        Kpis.Add(new ChequeKpiViewModel(
            0, "IconArrowDownLeft", "چک‌های دریافتی در جریان", inOpen.Count, Sum(inOpen),
            "", "+۱۲٪", " نسبت به ماه گذشته", "#027a48",
            [12, 18, 15, 22, 19, 26, 24, 31]));

        var sixty = JalaliCalendar.Medium(JalaliCalendar.FromDay(JalaliCalendar.ToDay(_today) + 60));
        Kpis.Add(new ChequeKpiViewModel(
            1, "IconArrowUpRight", "چک‌های پرداختی در جریان", outOpen.Count, Sum(outOpen),
            "تعهد پیش‌رو تا " + sixty, string.Empty, string.Empty, "#d98032",
            [22, 19, 24, 20, 26, 23, 28, 25]));

        if (overdue.Count > 0)
        {
            Kpis.Add(new ChequeKpiViewModel(
                2, "IconHourglass", "سررسید ۷ روز آینده", soon.Count, Sum(soon),
                string.Empty, ChequesPresentation.Fa(overdue.Count) + " فقره", " سررسید گذشته", "#b54708",
                [8, 12, 10, 16, 14, 20, 18, 22]));
        }
        else
        {
            Kpis.Add(new ChequeKpiViewModel(
                2, "IconHourglass", "سررسید ۷ روز آینده", soon.Count, Sum(soon),
                "بدون سررسید عقب‌افتاده", string.Empty, string.Empty, "#b54708",
                [8, 12, 10, 16, 14, 20, 18, 22]));
        }

        var rate = cleared.Count + bounced.Count == 0
            ? 0
            : (int)Math.Round(cleared.Count / (double)(cleared.Count + bounced.Count) * 100);
        Kpis.Add(new ChequeKpiViewModel(
            3, "IconAlert", "چک‌های برگشتی", bounced.Count, Sum(bounced),
            "نرخ وصول ", ChequesPresentation.Fa(rate) + "٪", string.Empty, "#027a48",
            [6, 5, 7, 4, 6, 3, 5, 2]));
    }

    private void RenderChips()
    {
        var baseList = _cheques.Where(c => _tab == "all" || (c.Direction == ChequeDirection.In ? "in" : "out") == _tab).ToList();

        foreach (var tab in Tabs)
        {
            tab.IsActive = tab.Key == _tab;
            var n = tab.Key == "all" ? _cheques.Count : _cheques.Count(c => (c.Direction == ChequeDirection.In ? "in" : "out") == tab.Key);
            tab.CountText = ChequesPresentation.Fa(n);
        }

        StatusChips.Clear();
        foreach (var status in new[]
                 {
                     ChequeStatus.InHand, ChequeStatus.AtBank, ChequeStatus.Cleared,
                     ChequeStatus.Bounced, ChequeStatus.Spent, ChequeStatus.Void,
                 })
        {
            var n = baseList.Count(c => c.Status == status);
            StatusChips.Add(new ChequeStatusChipViewModel(
                status,
                ChequesPresentation.StatusLabel(status),
                ChequesPresentation.StatusDesc(status),
                ChequesPresentation.StatusColor(status))
            {
                IsActive = _statuses.Contains(status),
                CountText = ChequesPresentation.Fa(n),
            });
        }

        foreach (var mode in ViewModes)
        {
            mode.IsActive = mode.Mode == ViewMode;
        }

        SelectedRangeOption = RangeOptions.FirstOrDefault(r => r.Key == _range) ?? RangeOptions[0];
        SelectedBankFilter = null;

        OnPropertyChanged(nameof(ShowResetChip));
    }

    // ── Render all (renderAll) ───────────────────────────────────────────

    private void RenderAll()
    {
        var list = Filtered();
        RenderChips();
        RenderBulk();

        switch (ViewMode)
        {
            case ChequeViewMode.Grid:
                RenderGrid(list);
                break;
            case ChequeViewMode.Calendar:
                RenderCalendar(list);
                break;
            case ChequeViewMode.Timeline:
                RenderTimeline(list);
                break;
            default:
                RenderTable(list);
                break;
        }

        RenderSide(list);
    }

    // ── Table (renderTable) ──────────────────────────────────────────────

    private void RenderTable(List<Cheque> list)
    {
        var pages = Math.Max(1, (int)Math.Ceiling(list.Count / (double)PageSize));
        if (_page > pages)
        {
            _page = pages;
        }

        var start = (_page - 1) * PageSize;
        var rows = list.Skip(start).Take(PageSize).ToList();

        ListHeadSummary = $"{ChequesPresentation.Fa(list.Count)} فقره · مجموع {ChequesPresentation.MoneyShort(Sum(list))} تومان";

        TableRows.ReplaceAll(rows.Select(c => new ChequeRowViewModel(c, BankOf(c.BankKey), DaysLeft(c))
        {
            IsSelected = SelectedCheques.Any(r => r.Id == c.Id),
        }));

        HasRows = TableRows.Count > 0;
        Pager = BuildPager(list.Count, PageSize);
    }

    private ChequePagerViewModel BuildPager(int total, int per)
    {
        var pages = Math.Max(1, (int)Math.Ceiling(total / (double)per));
        if (_page > pages)
        {
            _page = pages;
        }

        var buttons = new List<ChequePagerButtonViewModel>();
        for (var i = 1; i <= pages; i++)
        {
            if (pages > 7 && i > 2 && i < pages - 1 && Math.Abs(i - _page) > 1)
            {
                if (buttons.Count == 0 || !buttons[^1].IsEllipsis)
                {
                    buttons.Add(new ChequePagerButtonViewModel("…", 0, false, true, false));
                }

                continue;
            }

            buttons.Add(new ChequePagerButtonViewModel(ChequesPresentation.Fa(i), i, i == _page, false, true));
        }

        var shownStart = total == 0 ? 0 : (_page - 1) * per + 1;
        var shownEnd = Math.Min(_page * per, total);
        var text = $"نمایش {ChequesPresentation.Fa(shownStart)}–{ChequesPresentation.Fa(shownEnd)} از {ChequesPresentation.Fa(total)} فقره";

        return new ChequePagerViewModel(text, buttons, _page > 1, _page < pages, _page - 1, _page + 1);
    }

    // ── Grid (renderGrid) ────────────────────────────────────────────────

    private void RenderGrid(List<Cheque> list)
    {
        var start = (_page - 1) * GridPageSize;
        var rows = list.Skip(start).Take(GridPageSize).ToList();

        GridLeaves.Clear();
        foreach (var c in rows)
        {
            GridLeaves.Add(new ChequeLeafViewModel(c, BankOf(c.BankKey), DaysLeft(c)));
        }

        Pager = list.Count == 0 ? null : BuildPager(list.Count, GridPageSize);
    }

    // ── Calendar (renderCalendar) ────────────────────────────────────────

    private void RenderCalendar(List<Cheque> list)
    {
        var len = JalaliCalendar.MonthLen(CalendarYear, CalendarMonth);
        var first = $"{CalendarYear:D4}/{CalendarMonth:D2}/01";
        var startDow = JalaliCalendar.Weekday(first);
        var prev = JalaliCalendar.ShiftMonth(CalendarYear, CalendarMonth, -1);
        var prevLen = JalaliCalendar.MonthLen(prev.Year, prev.Month);

        var byDay = new Dictionary<int, List<Cheque>>();
        foreach (var c in list)
        {
            var p = JalaliCalendar.Parse(c.DueDate);
            if (p.Year == CalendarYear && p.Month == CalendarMonth)
            {
                if (!byDay.TryGetValue(p.Day, out var bucket))
                {
                    bucket = [];
                    byDay[p.Day] = bucket;
                }

                bucket.Add(c);
            }
        }

        var monthTotal = Sum(list.Where(c =>
        {
            var p = JalaliCalendar.Parse(c.DueDate);
            return p.Year == CalendarYear && p.Month == CalendarMonth;
        }));
        CalendarMonthTotal = $"مجموع سررسیدهای این ماه: {ChequesPresentation.MoneyShort(monthTotal)} تومان";

        var today = JalaliCalendar.Parse(_today);
        var isThis = today.Year == CalendarYear && today.Month == CalendarMonth;

        var cells = new List<(int Day, bool Off)>();
        for (var i = 0; i < startDow; i++)
        {
            cells.Add((prevLen - startDow + i + 1, true));
        }

        for (var d = 1; d <= len; d++)
        {
            cells.Add((d, false));
        }

        while (cells.Count % 7 != 0)
        {
            cells.Add((cells.Count - len - startDow + 1, true));
        }

        CalendarCells.Clear();
        for (var i = 0; i < cells.Count; i++)
        {
            var (day, off) = cells[i];
            var evs = off ? [] : byDay.TryGetValue(day, out var b) ? b : [];
            var dayTotal = Sum(evs);
            var events = evs.Select(e => new ChequeCalendarEventViewModel(e)).ToList();
            CalendarCells.Add(new ChequeCalendarCellViewModel(
                day, off, !off && isThis && day == today.Day, i % 7 == 6, dayTotal, events));
        }
    }

    // ── Timeline (renderTimeline) ────────────────────────────────────────

    private void RenderTimeline(List<Cheque> list)
    {
        (string Key, string Title, string Tone, Func<Cheque, bool> Pred)[] buckets =
        [
            ("over", "سررسید گذشته", "danger", c => IsOpen(c) && DaysLeft(c) < 0),
            ("today", "امروز", "danger", c => DaysLeft(c) == 0),
            ("week", "تا ۷ روز آینده", "warn", c => DaysLeft(c) > 0 && DaysLeft(c) <= 7),
            ("m", "۸ تا ۳۰ روز آینده", "", c => DaysLeft(c) > 7 && DaysLeft(c) <= 30),
            ("q", "بیش از ۳۰ روز آینده", "", c => DaysLeft(c) > 30),
            ("done", "بسته‌شده (وصول/برگشت/ابطال)", "", c => !IsOpen(c) && DaysLeft(c) <= 0),
        ];

        TimelineGroups.Clear();
        foreach (var b in buckets)
        {
            var items = list.Where(b.Pred).ToList();
            if (items.Count == 0)
            {
                continue;
            }

            var mapped = items.Select(c => new ChequeTimelineItemViewModel(c, BankOf(c.BankKey), DaysLeft(c))).ToList();
            TimelineGroups.Add(new ChequeTimelineGroupViewModel(b.Title, b.Tone, Sum(items), mapped));
        }
    }

    // ── Side column (renderSide) ─────────────────────────────────────────

    private void RenderSide(List<Cheque> list)
    {
        // Bank distribution — recomputed against the filtered list (cardBanks).
        var map = new Dictionary<string, long>();
        foreach (var c in list)
        {
            map[c.BankKey] = map.GetValueOrDefault(c.BankKey) + c.Amount;
        }

        var rows = map.OrderByDescending(kv => kv.Value).Take(5).ToList();
        var max = rows.Count > 0 ? rows[0].Value : 1;

        BankRows.Clear();
        foreach (var (key, value) in rows)
        {
            var bank = BankOf(key);
            var count = list.Count(c => c.BankKey == key);
            BankRows.Add(new ChequeBankRowViewModel(bank, value, count, max)
            {
                IsActive = _bank == key,
            });
        }
    }

    private async Task RefreshAlertsAsync()
    {
        Alerts.Clear();
        foreach (var alert in await _service.GetAlertsAsync())
        {
            Alerts.Add(new ChequeAlertViewModel(alert));
        }
    }

    // ── Bulk bar (renderBulk) ────────────────────────────────────────────

    private void RenderBulk()
    {
        SelectedCheques.Clear();
        // Preserve selections that still exist in the current data set.
        foreach (var id in _selectedIds.OrderBy(x => x))
        {
            var c = _cheques.FirstOrDefault(x => x.Id == id);
            if (c is not null)
            {
                SelectedCheques.Add(new ChequeRowViewModel(c, BankOf(c.BankKey), DaysLeft(c)));
            }
        }

        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(BulkCountText));
        OnPropertyChanged(nameof(BulkSumText));
    }

    private readonly HashSet<string> _selectedIds = new(StringComparer.Ordinal);

    // ── Commands ─────────────────────────────────────────────────────────

    [RelayCommand]
    private void SetTab(string? key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return;
        }

        _tab = key;
        _page = 1;
        _selectedIds.Clear();
        RenderKpis();
        RenderAll();
    }

    [RelayCommand]
    private void SetViewMode(ChequeViewMode mode)
    {
        ViewMode = mode;
        _page = 1;
    }

    [RelayCommand]
    private void ToggleStatusChip(ChequeStatus status)
    {
        if (!_statuses.Remove(status))
        {
            _statuses.Add(status);
        }

        _page = 1;
        RenderAll();
    }

    [RelayCommand]
    private void ResetFilters()
    {
        _statuses.Clear();
        _bank = string.Empty;
        SearchText = string.Empty;
        HasSearchValue = false;
        _range = "all";
        _page = 1;
        RenderAll();
    }

    [RelayCommand]
    private void ClearFilters()
    {
        _statuses.Clear();
        _bank = string.Empty;
        SearchText = string.Empty;
        HasSearchValue = false;
        _range = "all";
        _tab = "all";
        _page = 1;
        RenderAll();
    }

    [RelayCommand]
    private void SetRange(string? key)
    {
        _range = string.IsNullOrEmpty(key) ? "all" : key;
        _page = 1;
        RenderAll();
    }

    [RelayCommand]
    private void SetBankFilter(string? key)
    {
        _bank = string.IsNullOrEmpty(key) ? string.Empty : key;
        _page = 1;
        RenderAll();
    }

    [RelayCommand]
    private void ToggleBankFilter(string key)
    {
        _bank = _bank == key ? string.Empty : key;
        _page = 1;
        RenderAll();
    }

    [RelayCommand]
    private void ApplyKpiFilter(int index)
    {
        _range = "all";
        switch (index)
        {
            case 0:
                _tab = "in";
                _statuses.Clear();
                _statuses.Add(ChequeStatus.InHand);
                _statuses.Add(ChequeStatus.AtBank);
                break;
            case 1:
                _tab = "out";
                _statuses.Clear();
                _statuses.Add(ChequeStatus.InHand);
                _statuses.Add(ChequeStatus.AtBank);
                break;
            case 2:
                _tab = "all";
                _statuses.Clear();
                _range = "week";
                break;
            default:
                _tab = "all";
                _statuses.Clear();
                _statuses.Add(ChequeStatus.Bounced);
                break;
        }

        _page = 1;
        _selectedIds.Clear();
        RenderAll();
    }

    [RelayCommand]
    private void ApplyAlert(ChequeAlertKind kind)
    {
        switch (kind)
        {
            case ChequeAlertKind.Today:
            case ChequeAlertKind.Overdue:
                ClearFiltersSilently();
                _range = kind == ChequeAlertKind.Today ? "week" : "overdue";
                break;
            case ChequeAlertKind.SoonOut:
                ClearFiltersSilently();
                _tab = "out";
                _range = "week";
                break;
            case ChequeAlertKind.Bounced:
                ClearFiltersSilently();
                _statuses.Add(ChequeStatus.Bounced);
                break;
            default:
                _toast.Show("پیامک یادآوری برای ۶ نفر در صف ارسال قرار گرفت", Tone.Info);
                return;
        }

        _page = 1;
        RenderAll();
    }

    private void ClearFiltersSilently()
    {
        _statuses.Clear();
        _bank = string.Empty;
        SearchText = string.Empty;
        HasSearchValue = false;
        _range = "all";
        _tab = "all";
    }

    // ── Sorting + pager ──────────────────────────────────────────────────

    [RelayCommand]
    private void SortBy(string? key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return;
        }

        if (_sortKey == key)
        {
            _sortDir = _sortDir == "asc" ? "desc" : "asc";
        }
        else
        {
            _sortKey = key;
            _sortDir = key == "amount" ? "desc" : "asc";
        }

        RenderAll();
    }

    [RelayCommand]
    private void GoToPage(int page)
    {
        if (page < 1)
        {
            return;
        }

        _page = page;
        RenderAll();
    }

    [RelayCommand]
    private void ChangeMonth(string? param)
    {
        // The XAML passes string literals ("-1"/"0"/"1"); a RelayCommand<int>
        // would reject them at runtime (ArgumentException during layout).
        if (!int.TryParse(PersianNumberFormatter.ToAsciiDigits(param), out var delta))
        {
            delta = 0;
        }

        if (delta == 0)
        {
            var t = JalaliCalendar.Parse(_today);
            CalendarYear = t.Year;
            CalendarMonth = t.Month;
        }
        else
        {
            var (y, m) = JalaliCalendar.ShiftMonth(CalendarYear, CalendarMonth, delta);
            CalendarYear = y;
            CalendarMonth = m;
        }

        RenderAll();
    }

    [RelayCommand]
    private void ShowTimelineForDay()
    {
        _range = "all";
        ViewMode = ChequeViewMode.Timeline;
        _toast.Show("نمای نوار زمانی برای مشاهده همه سررسیدها فعال شد", Tone.Info);
    }

    [RelayCommand]
    private void Refresh()
    {
        _toast.Show("اطلاعات بروزرسانی شد", Tone.Ok);
        RenderAll();
    }

    [RelayCommand]
    private void PrintList() => _toast.Show("فایل چاپ فهرست آماده شد (نمونه طراحی)", Tone.Info);

    [RelayCommand]
    private void Export() => _toast.Show("خروجی اکسل چک‌ها آماده شد (نمونه طراحی)", Tone.Info);

    [RelayCommand]
    private void NewCheque() => ShowForm(null);

    /// <summary>Opens the add/edit form for <paramref name="editing"/> (null = create).</summary>
    private void ShowForm(Cheque? editing)
    {
        var banks = _banks.Values.ToList();
        var vm = new ChequeDrawerViewModel(
            _service, _toast, _dialogs, banks, editing,
            onSaved: () => _ = ReloadAsync());

        // The reference's `.ch-modal` is a centred 720px dialog (cheques.css
        // .ch-modal), not a side drawer — so it opens through ShowModal.
        _dialogs.ShowModal(vm, 720);
    }

    /// <summary>Opens the details drawer for a cheque (the reference's <c>openCheque</c>).</summary>
    [RelayCommand]
    private void OpenDetail(string? id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return;
        }

        var c = _cheques.FirstOrDefault(x => x.Id == id);
        if (c is null)
        {
            return;
        }

        OpenDetailFor(c);
    }

    private void OpenDetailFor(Cheque c)
    {
        var vm = new ChequeDetailViewModel(
            _service, _toast, _dialogs, c, BankOf(c.BankKey),
            onChanged: () => _ = ReloadAsync(),
            onEdit: edit =>
            {
                _dialogs.Close();
                ShowForm(edit);
            });
        _dialogs.ShowDrawer(vm, 480);
    }

    /// <summary>Reloads the whole screen from the service (after any mutation).</summary>
    private async Task ReloadAsync()
    {
        _cheques.Clear();
        _cheques.AddRange(await _service.GetAllAsync());
        await RefreshAlertsAsync();
        RenderKpis();
        RenderAll();
    }

    // ── Selection (bulk) ─────────────────────────────────────────────────

    [RelayCommand]
    private void ToggleSelect(string? id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return;
        }

        if (!_selectedIds.Remove(id))
        {
            _selectedIds.Add(id);
        }

        RenderBulk();
        SyncRowSelection();
    }

    [RelayCommand]
    private void SelectAll()
    {
        var list = Filtered().Skip((_page - 1) * PageSize).Take(PageSize).ToList();
        var allSelected = list.All(c => _selectedIds.Contains(c.Id));
        foreach (var c in list)
        {
            if (allSelected)
            {
                _selectedIds.Remove(c.Id);
            }
            else
            {
                _selectedIds.Add(c.Id);
            }
        }

        RenderBulk();
        SyncRowSelection();
    }

    private void SyncRowSelection()
    {
        foreach (var row in TableRows)
        {
            row.IsSelected = _selectedIds.Contains(row.Id);
        }

        foreach (var row in SelectedCheques)
        {
            row.IsSelected = true;
        }
    }

    [RelayCommand]
    private void ClearSelection()
    {
        _selectedIds.Clear();
        RenderBulk();
        SyncRowSelection();
    }

    [RelayCommand]
    private async Task BulkActionAsync(string? action)
    {
        var ids = _selectedIds.ToList();
        switch (action)
        {
            case "bank":
                await _service.BulkUpdateStatusAsync(ids, ChequeStatus.AtBank);
                _toast.Show("دسته چک انتخابی به بانک واگذار شد (نمونه طراحی)", Tone.Ok);
                break;
            case "clear":
                await _service.BulkUpdateStatusAsync(ids, ChequeStatus.Cleared);
                _toast.Show("وصول چک‌های انتخابی ثبت شد (نمونه طراحی)", Tone.Ok);
                break;
            case "sms":
                _toast.Show("پیامک یادآوری برای طرف‌حساب‌ها ارسال شد (نمونه طراحی)", Tone.Ok);
                break;
            case "print":
                _toast.Show("فایل چاپ فهرست آماده شد (نمونه طراحی)", Tone.Ok);
                break;
        }

        _selectedIds.Clear();
        await ReloadAsync();
    }

    // ── Row menu (openRowMenu) ───────────────────────────────────────────

    [RelayCommand]
    private void ToggleRowMenu(ChequeRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        var target = !row.IsMenuOpen;
        foreach (var r in TableRows)
        {
            r.IsMenuOpen = false;
        }

        row.IsMenuOpen = target;
    }

    [RelayCommand]
    private void CloseRowMenu()
    {
        foreach (var r in TableRows)
        {
            r.IsMenuOpen = false;
        }
    }

    [RelayCommand]
    private async Task RunRowActionAsync(ChequeMenuActionViewModel? action)
    {
        CloseRowMenu();
        if (action is null)
        {
            return;
        }

        var id = action.ChequeId;
        var c = _cheques.FirstOrDefault(x => x.Id == id);
        if (c is null)
        {
            return;
        }

        switch (action.ActionKey)
        {
            case "open":
                OpenDetailFor(c);
                return;
            case "edit":
                ShowForm(c);
                return;
            case "copy":
                _toast.Show("شناسه صیادی کپی شد", Tone.Ok);
                return;
            case "print":
                _toast.Show("رسید چک برای چاپ آماده شد (نمونه طراحی)", Tone.Info);
                return;
            case "sms":
                _toast.Show($"پیامک یادآوری برای {c.Party} ارسال شد", Tone.Ok);
                return;
            case "del":
                await _service.DeleteAsync(id);
                _selectedIds.Remove(id);
                _toast.Show("چک حذف شد (نمونه طراحی)", Tone.Danger);
                await ReloadAsync();
                return;
        }

        // bank | cleared | bounced | spent → status change (setStatus)
        var st = action.ActionKey switch
        {
            "bank" => ChequeStatus.AtBank,
            "cleared" => ChequeStatus.Cleared,
            "bounced" => ChequeStatus.Bounced,
            "spent" => ChequeStatus.Spent,
            _ => c.Status,
        };

        await _service.BulkUpdateStatusAsync([id], st);
        _toast.Show($"وضعیت چک {ChequesPresentation.FaText(c.Number)} به «{ChequesPresentation.StatusLabel(st)}» تغییر کرد", Tone.Ok);
        await ReloadAsync();
    }
}

/// <summary>One option of the toolbar's due-range select (<c>#chRange</c>).</summary>
public sealed class ChequeRangeOptionViewModel
{
    public ChequeRangeOptionViewModel(string key, string label)
    {
        Key = key;
        Label = label;
    }

    public string Key { get; }

    public string Label { get; }
}

/// <summary>One option of the toolbar's bank select (<c>#chBank</c>).</summary>
public sealed class ChequeBankOptionViewModel
{
    public ChequeBankOptionViewModel(string key, string label)
    {
        Key = key;
        Label = label;
    }

    /// <summary>Empty string for the «همه بانک‌ها» option.</summary>
    public string Key { get; }

    public string Label { get; }
}
