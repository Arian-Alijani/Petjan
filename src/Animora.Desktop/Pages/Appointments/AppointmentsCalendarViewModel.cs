using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Animora.Contracts.Appointments;
using Animora.Desktop.Controls;
using Animora.Desktop.Localization;
using Animora.Desktop.Services;
using Animora.Desktop.Shell;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Pages.Appointments;

/// <summary>
/// ViewModel for the Appointments calendar. Mirrors the reference's page
/// controller (Animora-UI/assets/appointments.js): the three primary tabs
/// (تقویم / نوبت‌های پیش رو / تاریخچه), the day/week/month view switch with its
/// prev/next/today date chrome, the type + doctor + hide-cancelled filters, the
/// time grid whose event cards are packed into overlap lanes, the live "now"
/// line, and the queue + history sub-tabs. All data flows through
/// <see cref="IAppointmentsQueryService"/> (the single backend swap point,
/// R-18) — no sample data or mock branching leaks into this public shape.
/// </summary>
public sealed partial class AppointmentsCalendarViewModel : PageViewModelBase
{
    private readonly IAppointmentsQueryService _service;
    private readonly IToastService _toast;
    private readonly IAppointmentBookingService _booking;
    private readonly IDialogService _dialogs;

    private readonly List<AppointmentDoctor> _doctors = [];
    private readonly List<AppointmentType> _types = [];
    private readonly List<AppointmentEvent> _all = [];

    // Fast lookup indexes rebuilt whenever the underlying collections change.
    // These replace repeated O(n) LINQ scans (First/Where) that made every
    // render pass scale with the total number of appointments.
    private readonly Dictionary<string, AppointmentType> _typeById = [];
    private readonly Dictionary<string, AppointmentDoctor> _doctorById = [];
    private readonly Dictionary<DateOnly, List<AppointmentEvent>> _byDate = [];

    // Precomputed haystack for search, keyed by appointment id, so Matches()
    // does not rebuild + re-normalize strings for every event on every render.
    private readonly Dictionary<string, string> _searchHaystack = [];
    private string _normalizedQuery = string.Empty;

    // Reference page state: S.tab / S.view / S.cursor / S.types / S.docs / S.hideCancel / S.q.
    private string _activeTab = "cal";
    private string _viewMode = "week";
    private DateOnly _cursor;
    private readonly HashSet<string> _activeTypeIds = [];
    private readonly HashSet<string> _activeDoctorIds = [];
    private bool _hideCancelled;
    private string _searchQuery = string.Empty;

    // Queue state: Q.day / Q.off / Q.showCancel.
    private DateOnly _queueDay;
    private int _queueOffset;
    private bool _queueShowCancelled;

    // History state (HS.*).
    private string _historyScope = "30";
    private string _historyStatus = "all";
    private string _historyDoctor = "all";
    private string _historyType = "all";
    private string _historyInvoice = "all";
    private DateOnly? _historyRangeStart;
    private DateOnly? _historyRangeEnd;
    private DateOnly _historyCalendarCursor;
    private bool _historyCalendarOpen;

    public AppointmentsCalendarViewModel(
        IAppointmentsQueryService service,
        IToastService toast,
        IAppointmentBookingService booking,
        IDialogService dialogs)
    {
        _service = service;
        _toast = toast;
        _booking = booking;
        _dialogs = dialogs;

        Title = "تقویم نوبت‌ها";
        Eyebrow = "برنامه‌ریزی هوشمند";
        Breadcrumbs.Add("مرکز فرمان");
        Breadcrumbs.Add("نوبت‌ها");

        var today = DateOnly.FromDateTime(DateTime.Today);
        _cursor = today;
        _queueDay = today;
        _historyCalendarCursor = today;

        _ = LoadAsync();
    }

    // ── bindable collections ─────────────────────────────────────────────────
    public ObservableCollection<AppointmentTypeChipViewModel> TypeChips { get; } = [];
    public ObservableCollection<AppointmentDoctorChipViewModel> DoctorChips { get; } = [];

    /// <summary>Grid columns (day/week). Each column carries its own header.</summary>
    public ObservableCollection<AppointmentColumnViewModel> Columns { get; } = [];

    /// <summary>Month-grid cells (6×7).</summary>
    public ObservableCollection<MonthCellViewModel> MonthCells { get; } = [];

    /// <summary>The 7-month-grid weekday header labels.</summary>
    public IReadOnlyList<string> MonthWeekdayHeaders { get; } = AppointmentsCalendarMath.DayNames;

    /// <summary>Hour labels for the time-grid gutter (07:00 … 21:00). Populated
    /// once during load; the gutter binds it declaratively.</summary>
    public ObservableCollection<string> HourLabels { get; } = [];

    // Queue tab
    public ObservableCollection<QueueDayViewModel> QueueDays { get; } = [];
    public ObservableCollection<QueueDoctorColumnViewModel> QueueColumns { get; } = [];
    public ObservableCollection<QueueStatViewModel> QueueStats { get; } = [];

    // History tab
    public ObservableCollection<HistoryScopeChipViewModel> HistoryScopeChips { get; } = [];
    public ObservableCollection<HistoryStatusChipViewModel> HistoryStatusChips { get; } = [];
    public ObservableCollection<HistoryStatViewModel> HistoryStats { get; } = [];
    public ObservableCollection<HistoryDayGroupViewModel> HistoryGroups { get; } = [];
    public ObservableCollection<HistoryCalendarCellViewModel> HistoryCalendarDays { get; } = [];

    public IReadOnlyList<HistorySelectOption> HistoryDoctorOptions { get; private set; } = [];
    public IReadOnlyList<HistorySelectOption> HistoryTypeOptions { get; private set; } = [];
    public IReadOnlyList<HistorySelectOption> HistoryInvoiceOptions { get; private set; } = [];

    public IReadOnlyList<HistorySelectOption> HistoryScopeOptions { get; private set; } = [];

    // ── observable state ─────────────────────────────────────────────────────
    [ObservableProperty]
    private string _rangeMainText = string.Empty;

    [ObservableProperty]
    private string _rangeSubText = string.Empty;

    [ObservableProperty]
    private string _searchQueryText = string.Empty;

    [ObservableProperty]
    private bool _hasSearchValue;

    [ObservableProperty]
    private bool _hasGridEvents;

    [ObservableProperty]
    private bool _hasMonthEvents;

    [ObservableProperty]
    private bool _isMonthView;

    // Queue header state
    [ObservableProperty]
    private string _queueDateText = string.Empty;

    [ObservableProperty]
    private bool _queueIsToday;

    // History header state
    [ObservableProperty]
    private string _historyRangeChipText = "انتخاب بازه از تقویم";

    [ObservableProperty]
    private string _historyCalendarLabel = string.Empty;

    [ObservableProperty]
    private string _historyCalendarHint = "برای انتخاب بازه، روی روز شروع و سپس روز پایان کلیک کنید";

    [ObservableProperty]
    private bool _historyCalendarCanApply;

    [ObservableProperty]
    private string _historyTotalText = string.Empty;

    [ObservableProperty]
    private bool _hasHistoryRows;

    partial void OnIsMonthViewChanged(bool value) => OnPropertyChanged(nameof(IsTimeGridView));

    partial void OnHasGridEventsChanged(bool value) => OnPropertyChanged(nameof(ShowGridEmptyState));

    partial void OnHasMonthEventsChanged(bool value) => OnPropertyChanged(nameof(ShowMonthEmptyState));

    /// <summary>Show the time-grid empty state when there are no visible events.</summary>
    public bool ShowGridEmptyState => !HasGridEvents;

    /// <summary>Show the month-grid empty state when there are no visible events.</summary>
    public bool ShowMonthEmptyState => !HasMonthEvents;

    // ── derived view flags ───────────────────────────────────────────────────
    public bool IsCalendarTab => _activeTab == "cal";
    public bool IsQueueTab => _activeTab == "queue";
    public bool IsHistoryTab => _activeTab == "hist";
    public bool ShowCalendarChrome => _activeTab == "cal";
    public bool IsTimeGridView => !IsMonthView;

    public bool IsDayView => _viewMode == "day";
    public bool IsWeekView => _viewMode == "week";
    public bool IsMonthViewMode => _viewMode == "month";

    /// <summary>Active top-level tab key ("cal"/"queue"/"hist"), bound to the
    /// <see cref="Controls.LazyTabHost.ActiveKey"/> so only the selected tab's
    /// subtree is realised instead of building all three behind IsVisible.</summary>
    public string ActiveTab => _activeTab;

    /// <summary>Active calendar view key ("day"/"week"/"month"), bound to the
    /// inner calendar <see cref="Controls.LazyTabHost"/> so only the visible
    /// grid is realised.</summary>
    public string ViewMode => _viewMode;

    public bool IsQueueCancelledShown => _queueShowCancelled;

    // ── load ─────────────────────────────────────────────────────────────────
    private async Task LoadAsync()
    {
        IsBusy = true;

        _doctors.Clear();
        _doctors.AddRange(await _service.GetDoctorsAsync());
        _types.Clear();
        _types.AddRange(await _service.GetTypesAsync());
        _all.Clear();
        _all.AddRange(await _service.GetForRangeAsync(
            DateOnly.FromDateTime(DateTime.Today).AddDays(-40),
            DateOnly.FromDateTime(DateTime.Today).AddDays(40)));

        RebuildIndexes();

        foreach (var t in _types)
        {
            _activeTypeIds.Add(t.Id);
        }

        foreach (var d in _doctors)
        {
            _activeDoctorIds.Add(d.Id);
        }

        for (var h = 7; h < 22; h++)
        {
            HourLabels.Add(AppointmentsPresentation.HourLabel(h * 60));
        }

        HistoryDoctorOptions = BuildSelectOptions("همه پزشکان", _doctors.Select(d => (d.Id, d.ShortName)));
        HistoryTypeOptions = BuildSelectOptions("همه انواع نوبت", _types.Select(t => (t.Id, t.Label)));
        HistoryInvoiceOptions =
        [
            new("all", "همه فاکتورها"),
            new("paid", "پرداخت شده"),
            new("unpaid", "پرداخت نشده"),
            new("partial", "پرداخت جزئی"),
            new("none", "بدون فاکتور"),
        ];

        IsBusy = false;
        Render();
        RenderQueue();
        RenderHistory();
    }

    private static IReadOnlyList<HistorySelectOption> BuildSelectOptions(
        string allLabel, IEnumerable<(string Id, string Label)> items)
    {
        var list = new List<HistorySelectOption> { new("all", allLabel) };
        list.AddRange(items.Select(i => new HistorySelectOption(i.Id, i.Label)));
        return list;
    }

    // ── lookup helpers (typeOf / docOf) ──────────────────────────────────────
    private AppointmentType TypeOf(string id) =>
        _typeById.TryGetValue(id, out var t) ? t : _types.First(x => x.Id == id);

    private AppointmentDoctor DocOf(string id) =>
        _doctorById.TryGetValue(id, out var d) ? d : _doctors.First(x => x.Id == id);

    /// <summary>
    /// Rebuilds the id/date lookup indexes and the per-event search haystack.
    /// Call once whenever <see cref="_all"/>, <see cref="_types"/> or
    /// <see cref="_doctors"/> change — never inside the render loop.
    /// </summary>
    private void RebuildIndexes()
    {
        _typeById.Clear();
        foreach (var t in _types)
        {
            _typeById[t.Id] = t;
        }

        _doctorById.Clear();
        foreach (var d in _doctors)
        {
            _doctorById[d.Id] = d;
        }

        _byDate.Clear();
        _searchHaystack.Clear();
        foreach (var a in _all)
        {
            if (!_byDate.TryGetValue(a.Date, out var bucket))
            {
                bucket = [];
                _byDate[a.Date] = bucket;
            }

            bucket.Add(a);

            var doc = _doctorById.TryGetValue(a.DoctorId, out var dd) ? dd.Name : string.Empty;
            var type = _typeById.TryGetValue(a.TypeId, out var tt) ? tt.Label : string.Empty;
            _searchHaystack[a.Id] =
                PersianText.Normalize($"{a.PetName} {a.OwnerName} {a.PetKind} {doc} {type}");
        }

        // Keep each day's bucket pre-sorted so OnDate() can skip re-sorting.
        foreach (var bucket in _byDate.Values)
        {
            bucket.Sort((x, y) => x.StartMinutes.CompareTo(y.StartMinutes));
        }
    }

    private bool Matches(AppointmentEvent a)
    {
        if (_normalizedQuery.Length == 0)
        {
            return true;
        }

        return _searchHaystack.TryGetValue(a.Id, out var hay)
            && hay.Contains(_normalizedQuery, StringComparison.Ordinal);
    }

    private bool Visible(AppointmentEvent a)
    {
        if (!_activeTypeIds.Contains(a.TypeId) || !_activeDoctorIds.Contains(a.DoctorId))
        {
            return false;
        }

        if (_hideCancelled && a.Status == AppointmentStatus.Cancelled)
        {
            return false;
        }

        return true;
    }

    private List<AppointmentEvent> OnDate(DateOnly d)
    {
        // Buckets are already sorted by StartMinutes in RebuildIndexes.
        if (!_byDate.TryGetValue(d, out var bucket))
        {
            return [];
        }

        var result = new List<AppointmentEvent>(bucket.Count);
        foreach (var a in bucket)
        {
            if (Visible(a))
            {
                result.Add(a);
            }
        }

        return result;
    }

    // ── render dispatcher ────────────────────────────────────────────────────
    private void Render()
    {
        RenderChrome();
        if (IsMonthViewMode)
        {
            RenderMonth();
        }
        else
        {
            RenderTimeGrid();
        }
    }

    private void RenderChrome()
    {
        // label()
        if (_viewMode == "day")
        {
            RangeMainText = AppointmentsCalendarMath.Full(_cursor);
            var sub = AppointmentsCalendarMath.DayNames[AppointmentsCalendarMath.WeekIndex(_cursor)];
            if (_cursor == DateOnly.FromDateTime(DateTime.Today))
            {
                sub += " · امروز";
            }

            RangeSubText = sub;
        }
        else if (_viewMode == "week")
        {
            var a = AppointmentsCalendarMath.WeekStart(_cursor);
            var b = a.AddDays(6);
            RangeMainText = AppointmentsCalendarMath.Medium(a) + " – " + AppointmentsCalendarMath.Medium(b);
            RangeSubText = "هفته " + AppointmentsCalendarMath.YearText(a);
        }
        else
        {
            var ms = AppointmentsCalendarMath.MonthStart(_cursor);
            RangeMainText = AppointmentsCalendarMath.MonthName(ms) + " " + AppointmentsCalendarMath.YearText(ms);
            RangeSubText = "نمای ماهانه";
        }

        // counters over the visible scope — single pass over the scoped days
        // (via the date index) tallying per-type and per-doctor counts, instead
        // of one full O(events) scan per type and per doctor.
        var scope = ScopeDates();
        var typeCounts = new Dictionary<string, int>();
        var doctorCounts = new Dictionary<string, int>();
        foreach (var day in scope)
        {
            if (!_byDate.TryGetValue(day, out var bucket))
            {
                continue;
            }

            foreach (var a in bucket)
            {
                typeCounts[a.TypeId] = typeCounts.GetValueOrDefault(a.TypeId) + 1;
                doctorCounts[a.DoctorId] = doctorCounts.GetValueOrDefault(a.DoctorId) + 1;
            }
        }

        TypeChips.Clear();
        foreach (var t in _types)
        {
            TypeChips.Add(new AppointmentTypeChipViewModel(t, typeCounts.GetValueOrDefault(t.Id))
            {
                IsActive = _activeTypeIds.Contains(t.Id),
            });
        }

        DoctorChips.Clear();
        foreach (var d in _doctors)
        {
            DoctorChips.Add(new AppointmentDoctorChipViewModel(d, doctorCounts.GetValueOrDefault(d.Id))
            {
                IsActive = _activeDoctorIds.Contains(d.Id),
            });
        }
    }

    private HashSet<DateOnly> ScopeDates()
    {
        var set = new HashSet<DateOnly>();
        if (_viewMode == "day")
        {
            set.Add(_cursor);
        }
        else if (_viewMode == "week")
        {
            var a = AppointmentsCalendarMath.WeekStart(_cursor);
            for (var i = 0; i < 7; i++)
            {
                set.Add(a.AddDays(i));
            }
        }
        else
        {
            var ms = AppointmentsCalendarMath.MonthStart(_cursor);
            var m = AppointmentsCalendarMath.Month(ms);
            var d = ms;
            while (AppointmentsCalendarMath.Month(d) == m)
            {
                set.Add(d);
                d = d.AddDays(1);
            }
        }

        return set;
    }

    // ── time grid (day/week) ─────────────────────────────────────────────────
    private void RenderTimeGrid()
    {
        IsMonthView = false;
        var today = DateOnly.FromDateTime(DateTime.Today);
        var nowMinutes = DateTime.Now.Hour * 60 + DateTime.Now.Minute;

        Columns.Clear();

        if (_viewMode == "day")
        {
            var docs = _doctors.Where(d => _activeDoctorIds.Contains(d.Id)).ToList();
            var cols = docs.Count > 0 ? docs : _doctors.ToList();
            var list = OnDate(_cursor);
            var total = 0;

            foreach (var c in cols)
            {
                var evs = Pack(list.Where(a => a.DoctorId == c.Id).ToList());
                total += evs.Count;
                Columns.Add(new AppointmentColumnViewModel(
                    new AppointmentColumnHeaderViewModel(c, evs.Count),
                    evs,
                    isClosed: false,
                    nowTop: _cursor == today ? NowTop(nowMinutes) : null,
                    isFirst: Columns.Count == 0));
            }

            HasGridEvents = total > 0;
        }
        else
        {
            var ws = AppointmentsCalendarMath.WeekStart(_cursor);
            var days = Enumerable.Range(0, 7).Select(i => ws.AddDays(i)).ToList();
            var total = 0;

            foreach (var d in days)
            {
                var list = OnDate(d);
                total += list.Count;
                Columns.Add(new AppointmentColumnViewModel(
                    new AppointmentColumnHeaderViewModel(
                        d,
                        isToday: d == today,
                        isFriday: AppointmentsCalendarMath.IsFriday(d)),
                    Pack(list),
                    isClosed: AppointmentsCalendarMath.IsFriday(d),
                    nowTop: d == today ? NowTop(nowMinutes) : null,
                    isFirst: Columns.Count == 0));
            }

            HasGridEvents = total > 0;
        }
    }

    private static double? NowTop(int nowMinutes)
    {
        if (nowMinutes < AppointmentsLayout.DayStartMinutes || nowMinutes > AppointmentsLayout.DayEndMinutes)
        {
            return null;
        }

        return AppointmentsLayout.MinuteToPx(nowMinutes - AppointmentsLayout.DayStartMinutes);
    }

    // ── month grid ───────────────────────────────────────────────────────────
    private void RenderMonth()
    {
        IsMonthView = true;
        var today = DateOnly.FromDateTime(DateTime.Today);
        var ms = AppointmentsCalendarMath.MonthStart(_cursor);
        var first = AppointmentsCalendarMath.WeekStart(ms);
        var mNum = AppointmentsCalendarMath.Month(ms);
        var total = 0;

        MonthCells.Clear();
        for (var i = 0; i < 42; i++)
        {
            var d = first.AddDays(i);
            var isOut = AppointmentsCalendarMath.Month(d) != mNum;
            if (i >= 35 && isOut)
            {
                break;
            }

            var list = OnDate(d).Where(Matches).ToList();
            if (!isOut)
            {
                total += list.Count;
            }

            var shown = list.Take(3).Select(a => new MonthEventViewModel(a, TypeOf(a.TypeId))).ToList();
            MonthCells.Add(new MonthCellViewModel(
                d,
                isOut,
                isToday: d == today,
                isFriday: AppointmentsCalendarMath.IsFriday(d),
                shown,
                hiddenCount: Math.Max(0, list.Count - 3),
                isFirstInRow: i % 7 == 0));
        }

        HasMonthEvents = total > 0;
    }

    // ── overlap packing (reference's pack()) ─────────────────────────────────
    private List<AppointmentEventViewModel> Pack(List<AppointmentEvent> list)
    {
        var result = new List<AppointmentEventViewModel>();
        var cluster = new List<AppointmentEvent>();
        var end = -1;

        void Flush()
        {
            if (cluster.Count == 0)
            {
                return;
            }

            var lanes = new List<int>();
            var laneOf = new Dictionary<string, int>();
            foreach (var a in cluster)
            {
                var li = lanes.FindIndex(e => e <= a.StartMinutes);
                if (li == -1)
                {
                    lanes.Add(a.EndMinutes);
                    li = lanes.Count - 1;
                }
                else
                {
                    lanes[li] = a.EndMinutes;
                }

                laneOf[a.Id] = li;
            }

            var laneCount = lanes.Count;
            foreach (var a in cluster)
            {
                var vm = new AppointmentEventViewModel(a, TypeOf(a.TypeId), DocOf(a.DoctorId), !Matches(a))
                {
                    Lane = laneOf[a.Id],
                    Lanes = laneCount,
                };
                result.Add(vm);
            }

            cluster.Clear();
            end = -1;
        }

        foreach (var a in list
                     .OrderBy(a => a.StartMinutes)
                     .ThenByDescending(a => a.DurationMinutes))
        {
            if (a.StartMinutes >= end)
            {
                Flush();
            }

            cluster.Add(a);
            end = Math.Max(end, a.EndMinutes);
        }

        Flush();
        return result;
    }

    // ── queue tab ────────────────────────────────────────────────────────────
    private void RenderQueue()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var nowMinutes = DateTime.Now.Hour * 60 + DateTime.Now.Minute;
        var tk = today;

        QueueDays.Clear();
        var days = Enumerable.Range(0, 14).Select(i => today.AddDays(_queueOffset * 7 + i)).ToList();
        foreach (var d in days)
        {
            var n = _byDate.TryGetValue(d, out var dayBucket)
                ? dayBucket.Count(a => a.Status != AppointmentStatus.Cancelled)
                : 0;
            QueueDays.Add(new QueueDayViewModel(
                d,
                isToday: d == tk,
                isClosed: AppointmentsCalendarMath.IsFriday(d),
                count: n)
            {
                IsActive = d == _queueDay,
            });
        }

        var list = _all.Where(a => a.Date == _queueDay && Visible(a) && Matches(a)).ToList();
        QueueStats.Clear();
        QueueStats.Add(new QueueStatViewModel("کل", AppointmentsPresentation.Fa(list.Count), Tone.Neutral));
        QueueStats.Add(new QueueStatViewModel(
            "در انتظار تأیید", AppointmentsPresentation.Fa(list.Count(a => a.Status == AppointmentStatus.Pending)), Tone.Warn));
        QueueStats.Add(new QueueStatViewModel(
            "پذیرش‌شده", AppointmentsPresentation.Fa(list.Count(a => a.Status == AppointmentStatus.Done)), Tone.Ok));
        QueueStats.Add(new QueueStatViewModel(
            "لغوشده", AppointmentsPresentation.Fa(list.Count(a => a.Status == AppointmentStatus.Cancelled)), Tone.Danger));

        var isToday = _queueDay == tk;
        QueueIsToday = isToday;
        QueueDateText = AppointmentsCalendarMath.DayNames[AppointmentsCalendarMath.WeekIndex(_queueDay)] +
                        "، " + AppointmentsCalendarMath.Full(_queueDay);

        QueueColumns.Clear();
        foreach (var d in _doctors)
        {
            var items = list
                .Where(a => a.DoctorId == d.Id && (_queueShowCancelled || a.Status != AppointmentStatus.Cancelled))
                .OrderBy(a => a.StartMinutes)
                .ToList();

            string? nowId = null;
            string? nextId = null;
            if (isToday)
            {
                var live = items
                    .Where(a => a.Status != AppointmentStatus.Cancelled && a.Status != AppointmentStatus.Done)
                    .ToList();
                nowId = live.FirstOrDefault(a => a.StartMinutes <= nowMinutes && nowMinutes < a.EndMinutes)?.Id;
                if (nowId is null)
                {
                    nextId = live.FirstOrDefault(a => a.StartMinutes > nowMinutes)?.Id;
                }
            }

            var cards = items.Select((a, i) => new QueueCardViewModel(
                a,
                TypeOf(a.TypeId),
                d,
                i + 1,
                a.Id == nowId ? QueueLiveBadge.Now : a.Id == nextId ? QueueLiveBadge.Next : QueueLiveBadge.None)).ToList();

            QueueColumns.Add(new QueueDoctorColumnViewModel(d, cards));
        }
    }

    // ── history tab ──────────────────────────────────────────────────────────
    private void RenderHistory()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var tk = today;

        var list = HistoryList();
        var perDay = list.GroupBy(a => a.Date).ToDictionary(g => g.Key, g => g.Count());
        var done = list.Count(a => a.Status == AppointmentStatus.Done);
        var cancel = list.Count(a => a.Status == AppointmentStatus.Cancelled);
        var noShow = list.Count(a => a.Visit == AppointmentVisitOutcome.NoShow);
        var revPaid = list.Where(a => a.Invoice == AppointmentInvoiceState.Paid).Sum(a => (long)a.Total);
        var revOwed = list.Where(a => a.Invoice == AppointmentInvoiceState.Unpaid).Sum(a => (long)a.Total)
                      + list.Where(a => a.Invoice == AppointmentInvoiceState.Partial).Sum(a => (long)Math.Round(a.Total / 2.0));

        HistoryScopeChips.Clear();
        var scopes = new (string Id, string Label)[]
        {
            ("today", "امروز"), ("7", "۷ روز اخیر"), ("30", "۳۰ روز اخیر"), ("month", "ماه جاری"), ("all", "همه"),
        };
        foreach (var s in scopes)
        {
            HistoryScopeChips.Add(new HistoryScopeChipViewModel(s.Id, s.Label) { IsActive = _historyScope == s.Id });
        }

        HistoryStatusChips.Clear();
        var statuses = new (string Id, string Label)[]
        {
            ("all", "همه وضعیت‌ها"), ("done", "پذیرش شده"), ("cancel", "لغو شده"),
        };
        foreach (var s in statuses)
        {
            HistoryStatusChips.Add(new HistoryStatusChipViewModel(s.Id, s.Label) { IsActive = _historyStatus == s.Id });
        }

        HistoryStats.Clear();
        HistoryStats.Add(new HistoryStatViewModel("IconRows", AppointmentsPresentation.Fa(list.Count), "کل نوبت‌ها", Tone.Neutral));
        HistoryStats.Add(new HistoryStatViewModel("IconCheck", AppointmentsPresentation.Fa(done), "پذیرش شده", Tone.Ok));
        HistoryStats.Add(new HistoryStatViewModel("IconX", AppointmentsPresentation.Fa(cancel), "لغو شده", Tone.Danger));
        HistoryStats.Add(new HistoryStatViewModel("IconAlert", AppointmentsPresentation.Fa(noShow), "عدم مراجعه", Tone.Warn));
        HistoryStats.Add(new HistoryStatViewModel(
            "IconWallet", PersianNumberFormatter.FormatGrouped(revPaid), "درآمد وصولی (تومان)", Tone.Ok));
        HistoryStats.Add(new HistoryStatViewModel(
            "IconAlert", PersianNumberFormatter.FormatGrouped(revOwed), "مطالبات معوق (تومان)", Tone.Danger));

        HistoryTotalText = AppointmentsPresentation.Fa(list.Count) + " نوبت" +
                           (string.IsNullOrEmpty(_searchQuery) ? string.Empty : $" · نتیجهٔ «{_searchQuery}»");

        HistoryGroups.Clear();
        HistoryDayGroupViewModel? current = null;
        var rows = new List<HistoryRowViewModel>();
        DateOnly lastDay = default;
        var firstGroup = true;

        foreach (var a in list)
        {
            if (firstGroup || a.Date != lastDay)
            {
                if (current is not null)
                {
                    HistoryGroups.Add(current);
                }

                lastDay = a.Date;
                firstGroup = false;
                rows = [];
                current = new HistoryDayGroupViewModel(
                    a.Date,
                    a.Date == today,
                    perDay.GetValueOrDefault(a.Date),
                    rows);
            }

            rows.Add(new HistoryRowViewModel(a, TypeOf(a.TypeId), DocOf(a.DoctorId)));
        }

        if (current is not null)
        {
            HistoryGroups.Add(current);
        }

        HasHistoryRows = list.Count > 0;

        RenderHistoryCalendar();
    }

    private List<AppointmentEvent> HistoryList()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var tk = today;
        var list = _all.Where(a => Visible(a) && Matches(a) &&
            (a.Date < tk || (a.Date == tk && (a.Status == AppointmentStatus.Done || a.Status == AppointmentStatus.Cancelled))))
            .ToList();

        switch (_historyScope)
        {
            case "today":
                list = list.Where(a => a.Date == tk).ToList();
                break;
            case "7":
                list = list.Where(a => a.Date >= tk.AddDays(-7)).ToList();
                break;
            case "30":
                list = list.Where(a => a.Date >= tk.AddDays(-30)).ToList();
                break;
            case "month":
                var m = AppointmentsCalendarMath.Month(tk);
                list = list.Where(a => AppointmentsCalendarMath.Month(a.Date) == m).ToList();
                break;
            case "range" when _historyRangeStart is not null && _historyRangeEnd is not null:
                var lo = _historyRangeStart.Value <= _historyRangeEnd.Value ? _historyRangeStart.Value : _historyRangeEnd.Value;
                var hi = _historyRangeStart.Value <= _historyRangeEnd.Value ? _historyRangeEnd.Value : _historyRangeStart.Value;
                list = list.Where(a => a.Date >= lo && a.Date <= hi).ToList();
                break;
        }

        if (_historyStatus != "all")
        {
            list = list.Where(a => AppointmentsPresentation.StatusClassKey(a.Status) ==
                (_historyStatus == "done" ? "done" : "cancel")).ToList();
        }

        if (_historyDoctor != "all")
        {
            list = list.Where(a => a.DoctorId == _historyDoctor).ToList();
        }

        if (_historyType != "all")
        {
            list = list.Where(a => a.TypeId == _historyType).ToList();
        }

        if (_historyInvoice != "all")
        {
            list = _historyInvoice == "none"
                ? list.Where(a => a.Invoice == AppointmentInvoiceState.None).ToList()
                : list.Where(a => InvoiceKey(a.Invoice) == _historyInvoice).ToList();
        }

        return list
            .OrderByDescending(a => a.Date)
            .ThenByDescending(a => a.StartMinutes)
            .ToList();
    }

    private static string InvoiceKey(AppointmentInvoiceState state) => state switch
    {
        AppointmentInvoiceState.Paid => "paid",
        AppointmentInvoiceState.Unpaid => "unpaid",
        AppointmentInvoiceState.Partial => "partial",
        _ => "none",
    };

    private void RenderHistoryCalendar()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        HistoryCalendarDays.Clear();

        var monthStart = AppointmentsCalendarMath.MonthStart(_historyCalendarCursor);
        var mNum = AppointmentsCalendarMath.Month(monthStart);
        var yNum = AppointmentsCalendarMath.Year(monthStart);
        var gridStart = monthStart.AddDays(-AppointmentsCalendarMath.WeekIndex(monthStart));

        var histDates = _all.Where(a => a.Date <= today).Select(a => a.Date).ToHashSet();
        var lo = _historyRangeStart is not null && _historyRangeEnd is not null
            ? (_historyRangeStart.Value <= _historyRangeEnd.Value ? _historyRangeStart.Value : _historyRangeEnd.Value) : (DateOnly?)null;
        var hi = _historyRangeStart is not null && _historyRangeEnd is not null
            ? (_historyRangeStart.Value <= _historyRangeEnd.Value ? _historyRangeEnd.Value : _historyRangeStart.Value) : (DateOnly?)null;

        for (var i = 0; i < 42; i++)
        {
            var d = gridStart.AddDays(i);
            var isOut = AppointmentsCalendarMath.Month(d) != mNum;
            var inRange = lo is not null && hi is not null
                ? d >= lo && d <= hi
                : _historyRangeStart is not null && _historyRangeEnd is null && d == _historyRangeStart;
            var edge = d == _historyRangeStart || d == _historyRangeEnd;

            HistoryCalendarDays.Add(new HistoryCalendarCellViewModel(
                d,
                isOut,
                isToday: d == today,
                hasHistory: histDates.Contains(d) && !isOut,
                isFriday: AppointmentsCalendarMath.IsFriday(d),
                isPast: d <= today,
                inRange,
                edge));
        }

        HistoryCalendarLabel = AppointmentsCalendarMath.MonthName(monthStart) + " " +
                               PersianNumberFormatter.ToPersianDigits(yNum);

        HistoryCalendarHint = _historyRangeStart is not null && _historyRangeEnd is null
            ? "حالا روز پایان بازه را انتخاب کنید"
            : _historyRangeStart is not null && _historyRangeEnd is not null
                ? $"بازه انتخاب‌شده: {AppointmentsCalendarMath.Full(_historyRangeStart.Value)} تا {AppointmentsCalendarMath.Full(_historyRangeEnd.Value)}"
                : "برای انتخاب بازه، روی روز شروع و سپس روز پایان کلیک کنید";

        HistoryCalendarCanApply = _historyRangeStart is not null && _historyRangeEnd is not null;

        HistoryRangeChipText = _historyScope == "range" && _historyRangeStart is not null && _historyRangeEnd is not null
            ? AppointmentsCalendarMath.Full(_historyRangeStart.Value) + " – " + AppointmentsCalendarMath.Full(_historyRangeEnd.Value)
            : "انتخاب بازه از تقویم";
    }

    // ── commands ─────────────────────────────────────────────────────────────
    [RelayCommand]
    private void SelectTab(string? key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return;
        }

        _activeTab = key;
        OnPropertyChanged(nameof(ActiveTab));
        OnPropertyChanged(nameof(IsCalendarTab));
        OnPropertyChanged(nameof(IsQueueTab));
        OnPropertyChanged(nameof(IsHistoryTab));
        OnPropertyChanged(nameof(ShowCalendarChrome));

        if (key == "queue")
        {
            RenderQueue();
        }
        else if (key == "hist")
        {
            RenderHistory();
        }
    }

    [RelayCommand]
    private void SetView(string? key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return;
        }

        _viewMode = key;
        OnPropertyChanged(nameof(ViewMode));
        OnPropertyChanged(nameof(IsDayView));
        OnPropertyChanged(nameof(IsWeekView));
        OnPropertyChanged(nameof(IsMonthViewMode));
        Render();
    }

    [RelayCommand]
    private void GoToday()
    {
        _cursor = DateOnly.FromDateTime(DateTime.Today);
        Render();
    }

    [RelayCommand]
    private void StepPrevious() => Step(-1);

    [RelayCommand]
    private void StepNext() => Step(1);

    private void Step(int dir)
    {
        if (_viewMode == "day")
        {
            _cursor = _cursor.AddDays(dir);
        }
        else if (_viewMode == "week")
        {
            _cursor = _cursor.AddDays(dir * 7);
        }
        else
        {
            var d = AppointmentsCalendarMath.MonthStart(_cursor);
            _cursor = dir > 0
                ? AppointmentsCalendarMath.MonthStart(d.AddDays(35))
                : AppointmentsCalendarMath.MonthStart(d.AddDays(-1));
        }

        Render();
    }

    [RelayCommand]
    private void ToggleType(AppointmentTypeChipViewModel? chip)
    {
        if (chip is null)
        {
            return;
        }

        if (!_activeTypeIds.Remove(chip.Type.Id))
        {
            _activeTypeIds.Add(chip.Type.Id);
        }

        if (_activeTypeIds.Count == 0)
        {
            foreach (var t in _types)
            {
                _activeTypeIds.Add(t.Id);
            }
        }

        Render();
    }

    [RelayCommand]
    private void ToggleDoctor(AppointmentDoctorChipViewModel? chip)
    {
        if (chip is null)
        {
            return;
        }

        if (!_activeDoctorIds.Remove(chip.Doctor.Id))
        {
            _activeDoctorIds.Add(chip.Doctor.Id);
        }

        if (_activeDoctorIds.Count == 0)
        {
            foreach (var d in _doctors)
            {
                _activeDoctorIds.Add(d.Id);
            }
        }

        Render();
    }

    [RelayCommand]
    private void ToggleHideCancel()
    {
        _hideCancelled = !_hideCancelled;
        Render();
    }

    public void ApplySearch(string? text)
    {
        _searchQuery = text?.Trim() ?? string.Empty;
        _normalizedQuery = _searchQuery.Length == 0
            ? string.Empty
            : PersianText.Normalize(_searchQuery);
        Render();
        if (_activeTab == "queue")
        {
            RenderQueue();
        }
        else if (_activeTab == "hist")
        {
            RenderHistory();
        }
    }

    [RelayCommand]
    private void ClearSearch()
    {
        SearchQueryText = string.Empty;
        HasSearchValue = false;
        ApplySearch(string.Empty);
    }

    [RelayCommand]
    private async Task OpenBooking(object? target)
    {
        // An appointment event → open the detail drawer; anything else (the
        // toolbar «ثبت نوبت جدید» button, a grid-cell click) → open the booking
        // sheet in new mode, mirroring the reference's SCH.book().
        AppointmentEvent? ev = target switch
        {
            AppointmentEvent e => e,
            AppointmentEventViewModel vm => vm.Model,
            QueueCardViewModel q => q.Model,
            HistoryRowViewModel h => h.Model,
            MonthEventViewModel m => m.Model,
            _ => null,
        };

        if (ev is not null)
        {
            await ShowDetailAsync(ev);
            return;
        }

        var panel = new BookingPanelViewModel(_booking, _service, _toast, _dialogs);
        panel.Saved += () => _ = RefreshAfterChangeAsync();
        await panel.LoadForNewAsync(null, null, null, null);
        _dialogs.ShowDrawer(new BookingPanelView { DataContext = panel });
    }

    private async Task ShowDetailAsync(AppointmentEvent ev)
    {
        var detail = new BookingDetailViewModel(
            _booking,
            _service,
            _toast,
            _dialogs,
            onChanged: () => _ = RefreshAfterChangeAsync(),
            onEdit: editEv => _ = OpenEditAsync(editEv));
        if (await detail.LoadAsync(ev.Id))
        {
            _dialogs.ShowDrawer(new BookingDetailView { DataContext = detail });
        }
    }

    private async Task OpenEditAsync(AppointmentEvent ev)
    {
        var panel = new BookingPanelViewModel(_booking, _service, _toast, _dialogs);
        panel.Saved += () => _ = RefreshAfterChangeAsync();
        await panel.LoadForEditAsync(ev);
        _dialogs.ShowDrawer(new BookingPanelView { DataContext = panel });
    }

    private async Task RefreshAfterChangeAsync()
    {
        _all.Clear();
        _all.AddRange(await _service.GetForRangeAsync(
            DateOnly.FromDateTime(DateTime.Today).AddDays(-40),
            DateOnly.FromDateTime(DateTime.Today).AddDays(40)));
        RebuildIndexes();
        Render();
        RenderQueue();
        RenderHistory();
    }

    [RelayCommand]
    private void ExportCalendar() =>
        _toast.Show("خروجی تقویم آماده دانلود است.", Tone.Info);

    // ── queue commands ───────────────────────────────────────────────────────
    [RelayCommand]
    private void SelectQueueDay(QueueDayViewModel? day)
    {
        if (day is null)
        {
            return;
        }

        _queueDay = day.Day;
        RenderQueue();
    }

    [RelayCommand]
    private void ShiftQueueWeek(string? dir)
    {
        _queueOffset = Math.Max(0, _queueOffset + (dir == "-1" ? -1 : 1));
        RenderQueue();
    }

    [RelayCommand]
    private void ToggleQueueCancel()
    {
        _queueShowCancelled = !_queueShowCancelled;
        OnPropertyChanged(nameof(IsQueueCancelledShown));
        RenderQueue();
    }

    [RelayCommand]
    private void QueueGoToday()
    {
        _queueDay = DateOnly.FromDateTime(DateTime.Today);
        RenderQueue();
    }

    // ── history commands ─────────────────────────────────────────────────────
    [RelayCommand]
    private void SetHistoryScope(HistoryScopeChipViewModel? chip)
    {
        if (chip is null)
        {
            return;
        }

        _historyScope = chip.Id;
        _historyCalendarOpen = false;
        RenderHistory();
    }

    [RelayCommand]
    private void SetHistoryStatus(HistoryStatusChipViewModel? chip)
    {
        if (chip is null)
        {
            return;
        }

        _historyStatus = chip.Id;
        RenderHistory();
    }

    [RelayCommand]
    private void ToggleHistoryCalendar()
    {
        _historyCalendarOpen = !_historyCalendarOpen;
        if (_historyCalendarOpen && _historyRangeStart is not null && _historyRangeEnd is null)
        {
            _historyRangeStart = null;
        }

        RenderHistory();
    }

    [RelayCommand]
    private void HistoryCalendarStep(string? dir)
    {
        var d = AppointmentsCalendarMath.MonthStart(_historyCalendarCursor);
        _historyCalendarCursor = dir == "-1" ? AppointmentsCalendarMath.MonthStart(d.AddDays(-1))
            : AppointmentsCalendarMath.MonthStart(d.AddDays(35));
        RenderHistory();
    }

    [RelayCommand]
    private void HistoryCalendarToday()
    {
        _historyCalendarCursor = DateOnly.FromDateTime(DateTime.Today);
        RenderHistory();
    }

    [RelayCommand]
    private void HistoryCalendarClear()
    {
        _historyRangeStart = null;
        _historyRangeEnd = null;
        if (_historyScope == "range")
        {
            _historyScope = "30";
        }

        RenderHistory();
    }

    [RelayCommand]
    private void HistoryCalendarPick(HistoryCalendarCellViewModel? cell)
    {
        if (cell is null || !cell.IsPast)
        {
            return;
        }

        if (_historyRangeStart is null || (_historyRangeStart is not null && _historyRangeEnd is not null))
        {
            _historyRangeStart = cell.Day;
            _historyRangeEnd = null;
        }
        else
        {
            _historyRangeEnd = cell.Day;
        }

        RenderHistory();
    }

    [RelayCommand]
    private void ApplyHistoryRange()
    {
        if (_historyRangeStart is not null && _historyRangeEnd is not null)
        {
            _historyScope = "range";
            _historyCalendarOpen = false;
            RenderHistory();
        }
    }

    [RelayCommand]
    private void HistorySelectChanged(string? key)
    {
        // Selects (doc/type/invoice) carry the target field in the parameter's prefix.
        if (string.IsNullOrEmpty(key))
        {
            return;
        }

        RenderHistory();
    }

    [RelayCommand]
    private void ViewDayInCalendar(HistoryDayGroupViewModel? group)
    {
        if (group is null)
        {
            return;
        }

        _cursor = group.Day;
        _viewMode = "day";
        OnPropertyChanged(nameof(ViewMode));
        OnPropertyChanged(nameof(IsDayView));
        OnPropertyChanged(nameof(IsWeekView));
        OnPropertyChanged(nameof(IsMonthViewMode));
        _activeTab = "cal";
        OnPropertyChanged(nameof(ActiveTab));
        OnPropertyChanged(nameof(IsCalendarTab));
        OnPropertyChanged(nameof(IsQueueTab));
        OnPropertyChanged(nameof(IsHistoryTab));
        OnPropertyChanged(nameof(ShowCalendarChrome));
        Render();
    }

    public string SelectedHistoryDoctor
    {
        get => _historyDoctor;
        set
        {
            if (SetProperty(ref _historyDoctor, value))
            {
                RenderHistory();
            }
        }
    }

    public string SelectedHistoryType
    {
        get => _historyType;
        set
        {
            if (SetProperty(ref _historyType, value))
            {
                RenderHistory();
            }
        }
    }

    public string SelectedHistoryInvoice
    {
        get => _historyInvoice;
        set
        {
            if (SetProperty(ref _historyInvoice, value))
            {
                RenderHistory();
            }
        }
    }

    public bool IsHistoryCalendarOpen => _historyCalendarOpen;
}
