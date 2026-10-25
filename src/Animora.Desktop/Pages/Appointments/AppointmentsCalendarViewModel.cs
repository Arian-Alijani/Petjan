using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Animora.Contracts.Appointments;
using Animora.Contracts.Diagnostics;
using Animora.Contracts.Identity;
using Animora.Contracts.Patients;
using Animora.Contracts.Profile;
using Animora.Contracts.Visits;
using Animora.Domain.Identity;
using Animora.Desktop.Controls;
using Animora.Desktop.Localization;
using Animora.Desktop.Pages.Appointments.Export;
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
    private readonly IStoreReadinessGate _readiness;
    private readonly IToastService _toast;
    private readonly IDataChangeNotifier _changes;
    private readonly IAppointmentBookingService _booking;
    private readonly IDialogService _dialogs;
    private readonly IAppointmentSettingsService _settings;
    private readonly IPatientsQueryService _patients;
    private readonly IPrintService _print;
    private readonly IUserProfileService _profile;
    private readonly ICurrentUserContext _user;
    private readonly ICapabilityGate _capabilities;
    private readonly INavigationService _navigation;
    private readonly IRecordSheetPresenter _presenter;
    private readonly Animora.Contracts.Vaccination.IVaccinationService _vaccination;
    private readonly IVisitEncounterService _encounters;
    private readonly IClinicClock _clock;
    private readonly Avalonia.Threading.DispatcherTimer _clockTimer = new() { Interval = TimeSpan.FromMinutes(1) };
    private ClinicMoment _lastMoment;
    private TimeGridWindow _window = TimeGridWindow.Default;

    private readonly List<AppointmentDoctor> _doctors = [];
    private readonly List<AppointmentType> _types = [];
    private readonly List<AppointmentEvent> _all = [];

    // A01: events are cached per fixed-size date chunk and fetched on demand for whatever range is on screen,
    // so nothing depends on a preloaded window around today. Each fetch stays inside the backend's 63-day guard.
    private const int ChunkDays = 28;
    private const int ChunksPerRequest = 2; // 56 days per request, below the 63-inclusive-day range guard
    private const int MaxCachedChunks = 30; // ~840 days; farther chunks are evicted when the cache grows
    private readonly Dictionary<int, List<AppointmentEvent>> _chunks = [];
    private int _rangeGeneration;

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
    private bool _isLoading;
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

    // History archive state (A01): rows come page by page from the backend, with scope-wide totals.
    private readonly List<AppointmentEvent> _historyRows = [];
    private AppointmentHistoryTotals _historyTotals = AppointmentHistoryTotals.Empty;
    private int _historyPage;
    private int _historyGeneration;
    private CancellationTokenSource? _historyCts;
    private bool _historyStale = true;

    public AppointmentsCalendarViewModel(
        IAppointmentsQueryService service,
        IStoreReadinessGate readiness,
        IToastService toast,
        IAppointmentBookingService booking,
        IDialogService dialogs,
        IDataChangeNotifier changes,
        IAppointmentSettingsService settings,
        IPatientsQueryService patients,
        IPrintService print,
        IUserProfileService profile,
        ICurrentUserContext user,
        ICapabilityGate capabilities,
        INavigationService navigation,
        IVisitEncounterService encounters,
        IClinicClock clock,
        IRecordSheetPresenter presenter,
        Animora.Contracts.Vaccination.IVaccinationService vaccination)
    {
        _presenter = presenter;
        _vaccination = vaccination;
        _clock = clock;
        _lastMoment = clock.Now;
        _clockTimer.Tick += OnClockTick;
        _navigation = navigation;
        _encounters = encounters;
        _capabilities = capabilities;
        _capabilities.CapabilitiesChanged += OnCapabilitiesChanged;
        _service = service;
        _print = print;
        _profile = profile;
        _user = user;
        _settings = settings;
        _patients = patients;
        _changes = changes;
        _readiness = readiness;
        _toast = toast;
        _booking = booking;
        _dialogs = dialogs;

        Title = "تقویم نوبت‌ها";
        Eyebrow = "برنامه‌ریزی هوشمند";
        Breadcrumbs.Add("مرکز فرمان");
        Breadcrumbs.Add("نوبت‌ها");

        var today = _clock.Today;
        _cursor = today;
        _queueDay = today;
        _historyCalendarCursor = today;

        UseLoadStatus(ReloadPageAsync);
        // A completed encounter closes its appointment; patient edits change the names shown.
        // Vaccination: a recorded dose / next-due date changes the booster entries; Sales: a payment changes the
        // invoice state shown on a card.
        RefreshOnDataChange(changes, RequestRefresh, DataDomain.Visits, DataDomain.Patients, DataDomain.Vaccination,
            DataDomain.Sales);
        _ = ReloadPageAsync();
    }

    // ── bindable collections ─────────────────────────────────────────────────
    // Every bound collection here is rebuilt wholesale on tab/view/filter/date
    // changes, so they use RangeObservableCollection and commit via ReplaceAll:
    // one Reset per rebuild instead of an O(n) notification storm (PERFORMANCE.md §5).
    public RangeObservableCollection<AppointmentTypeChipViewModel> TypeChips { get; } = [];
    public RangeObservableCollection<AppointmentDoctorChipViewModel> DoctorChips { get; } = [];

    /// <summary>Grid columns (day/week). Each column carries its own header.</summary>
    public RangeObservableCollection<AppointmentColumnViewModel> Columns { get; } = [];

    /// <summary>Month-grid cells (6×7).</summary>
    public RangeObservableCollection<MonthCellViewModel> MonthCells { get; } = [];

    /// <summary>The 7-month-grid weekday header labels.</summary>
    public IReadOnlyList<string> MonthWeekdayHeaders { get; } = AppointmentsCalendarMath.DayNames;

    /// <summary>Hour rows of the time grid (gutter label + separator), rebuilt from the visible window on every render.</summary>
    public RangeObservableCollection<HourRowViewModel> HourRows { get; } = [];

    // Queue tab
    public RangeObservableCollection<QueueDayViewModel> QueueDays { get; } = [];
    public RangeObservableCollection<QueueDoctorColumnViewModel> QueueColumns { get; } = [];
    public RangeObservableCollection<QueueStatViewModel> QueueStats { get; } = [];

    /// <summary>Doctor columns per row in the queue tab (A25).</summary>
    [ObservableProperty]
    private int _queueColumnCount = 1;

    // History tab
    public RangeObservableCollection<HistoryScopeChipViewModel> HistoryScopeChips { get; } = [];
    public RangeObservableCollection<HistoryStatusChipViewModel> HistoryStatusChips { get; } = [];
    public RangeObservableCollection<HistoryStatViewModel> HistoryStats { get; } = [];
    public RangeObservableCollection<HistoryDayGroupViewModel> HistoryGroups { get; } = [];
    public RangeObservableCollection<HistoryCalendarCellViewModel> HistoryCalendarDays { get; } = [];

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
    public bool ShowGridEmptyState => !HasGridEvents && !IsRangeUnsettled;

    /// <summary>Show the month-grid empty state when there are no visible events.</summary>
    public bool ShowMonthEmptyState => !HasMonthEvents && !IsRangeUnsettled;

    /// <summary>True while the visible range is still being fetched: grids must not claim "no appointments".</summary>
    [ObservableProperty]
    private bool _isRangeUnsettled;

    partial void OnIsRangeUnsettledChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowGridEmptyState));
        OnPropertyChanged(nameof(ShowMonthEmptyState));
    }

    // ── derived view flags ───────────────────────────────────────────────────
    public bool IsCalendarTab => _activeTab == "cal";
    public bool IsQueueTab => _activeTab == "queue";
    public bool IsHistoryTab => _activeTab == "hist";
    public bool IsSettingsTab => _activeTab == "settings";
    public bool ShowCalendarChrome => _activeTab == "cal";
    public bool IsTimeGridView => !IsMonthView;

    public bool IsDayView => _viewMode == "day";
    public bool IsWeekView => _viewMode == "week";
    public bool IsMonthViewMode => _viewMode == "month";

    /// <summary>Active top-level tab key ("cal"/"queue"/"hist"/"settings"), bound to the
    /// <see cref="Controls.LazyTabHost.ActiveKey"/> so only the selected tab's
    /// subtree is realised instead of building all three behind IsVisible.</summary>
    public string ActiveTab => _activeTab;

    /// <summary>Active calendar view key ("day"/"week"/"month"), bound to the
    /// inner calendar <see cref="Controls.LazyTabHost"/> so only the visible
    /// grid is realised.</summary>
    public string ViewMode => _viewMode;

    public bool IsQueueCancelledShown => _queueShowCancelled;

    /// <summary>
    /// Re-loads the page each time it is navigated to (it is a singleton, so
    /// the constructor load runs only once). The first visit is already covered
    /// by the constructor, so this only fires the reload on subsequent visits,
    /// picking up doctors/appointments created elsewhere in the meantime.
    /// </summary>
    public override void OnNavigatedTo()
    {
        // A24: the minute timer runs only while the page is visible.
        _clockTimer.Start();

        // The constructor's initial load is still in flight on the very first visit.
        if (IsLoadInFlight)
        {
            return;
        }

        _ = ReloadPageAsync();
    }

    /// <summary>Leaving the page stops the minute timer; it is restarted on the next visit (A24).</summary>
    public override void OnNavigatedFrom()
    {
        _clockTimer.Stop();
    }

    // A24: advances the time-derived state (now line, now/next badges, "today" markers) without user interaction.
    // Only the clinic-local minute/day is compared, so a device-timezone change never moves the clinic day.
    private void OnClockTick(object? sender, EventArgs e)
    {
        if (_isLoading)
        {
            return;
        }

        var previous = _lastMoment;
        var moment = _clock.Now;
        if (moment == previous)
        {
            return;
        }

        _lastMoment = moment;
        if (moment.Today != previous.Today)
        {
            // Views parked on yesterday's "today" follow the clinic into the new day.
            if (_cursor == previous.Today) _cursor = moment.Today;
            if (_queueDay == previous.Today) _queueDay = moment.Today;
            _historyCalendarCursor = moment.Today;
            _historyStale = true;
            ShowRange(RenderActiveTabForClock, ActiveRanges());
            return;
        }

        RenderActiveTabForClock();
    }

    private void RenderActiveTabForClock()
    {
        switch (_activeTab)
        {
            case "cal":
                Render();
                break;
            case "queue":
                RenderQueue();
                break;
        }
    }

    private (DateOnly Start, DateOnly End)[] ActiveRanges() => _activeTab switch
    {
        "queue" => QueueRanges(),
        _ => [CalendarRange()],
    };

    // The first load resolves the clinic zone; the constructor may have seeded the cursors with the fallback zone's day.
    private void SyncClinicDay()
    {
        var today = _clock.Today;
        var seeded = _lastMoment.Today;
        _lastMoment = _clock.Now;
        if (today == seeded)
        {
            return;
        }

        if (_cursor == seeded) _cursor = today;
        if (_queueDay == seeded) _queueDay = today;
        if (_historyCalendarCursor == seeded) _historyCalendarCursor = today;
    }

    // LoadAsync has no cleanup of its own: a failure must release the re-entrancy guard so a retry can run.
    private Task ReloadPageAsync() => TrackLoadAsync(async () =>
    {
        try
        {
            await LoadAsync();
        }
        catch
        {
            _isLoading = false;
            throw;
        }
    });

    // ── load ─────────────────────────────────────────────────────────────────
    private async Task LoadAsync()
    {
        if (_isLoading)
        {
            return;
        }

        _isLoading = true;
        IsBusy = true;

        // Yield once before the fetch + render pipeline runs so the click that opens this page paints first.
        await Task.Yield();
        await _readiness.WaitAsync();

        // A23/A24: resolve the clinic timezone off the UI path once; today/now below are clinic-local from here on.
        await _clock.HydrateAsync();
        SyncClinicDay();

        // Everything is fetched into locals first and swapped in only when the whole load succeeded, so a failure
        // keeps the previous snapshot instead of leaving the page half-cleared.
        var doctors = await _service.GetDoctorsAsync();
        var types = await _service.GetTypesAsync();
        ApplySettings(await _settings.GetAsync());
        LockViewForCalendarMode();
        var fresh = await FetchChunksAsync(NeededChunks());

        _doctors.Clear();
        _doctors.AddRange(doctors);
        _types.Clear();
        _types.AddRange(types);
        ReplaceChunks(fresh);
        _historyStale = true;

        RebuildIndexes();

        // Rebuild the active filter sets from scratch so a doctor/type added
        // since the last load (e.g. registered on the Profile page) is included
        // and its appointments pass the calendar's visibility filter.
        _activeTypeIds.Clear();
        foreach (var t in _types)
        {
            _activeTypeIds.Add(t.Id);
        }

        _activeDoctorIds.Clear();
        foreach (var d in _doctors)
        {
            _activeDoctorIds.Add(d.Id);
        }

        HistoryDoctorOptions = BuildSelectOptions("همه پزشکان", _doctors.Select(d => (d.Id, d.ShortName)));
        HistoryTypeOptions = BuildSelectOptions("همه انواع نوبت", _types.Where(t => !AppointmentTypeIds.IsReadOnlyProjection(t.Id)).Select(t => (t.Id, t.Label)));
        HistoryInvoiceOptions =
        [
            new("all", "همه فاکتورها"),
            new("paid", "پرداخت شده"),
            new("unpaid", "پرداخت نشده"),
            new("partial", "پرداخت جزئی"),
            new("none", "بدون فاکتور"),
        ];

        IsBusy = false;
        _isLoading = false;

        // Render only the active tab (mirrors ApplySearch/SelectTab): Queue
        // and History are behind a LazyTabHost and rebuilt on first activation
        // via SelectTab, so eagerly rendering all three tabs here tripled the
        // work of every initial load/reload for no visible benefit — the
        // calendar tab is the only one on screen at first paint.
        RenderActiveTab();
    }

    /// <summary>Renders whichever of the three tabs is currently active, so
    /// load/refresh/search never do more work than what is on screen.</summary>
    private void RenderActiveTab()
    {
        switch (_activeTab)
        {
            case "queue":
                RenderQueue();
                break;
            case "hist":
                RenderHistory();
                RefreshHistory();
                break;
            default:
                Render();
                break;
        }
    }

    // ── range loading (A01) ──────────────────────────────────────────────────
    private static int ChunkOf(DateOnly day) => day.DayNumber / ChunkDays;

    private static DateOnly ChunkStart(int chunk) => DateOnly.FromDayNumber(chunk * ChunkDays);

    private (DateOnly Start, DateOnly End) CalendarRange()
    {
        switch (_viewMode)
        {
            case "day":
                return (_cursor, _cursor);
            case "week":
                var ws = AppointmentsCalendarMath.WeekStart(_cursor);
                return (ws, ws.AddDays(6));
            default:
                // The month grid paints the out-of-month leading/trailing days too (6 weeks at most).
                var first = AppointmentsCalendarMath.WeekStart(AppointmentsCalendarMath.MonthStart(_cursor));
                return (first, first.AddDays(41));
        }
    }

    private (DateOnly Start, DateOnly End)[] QueueRanges()
    {
        var first = _clock.Today.AddDays(_queueOffset * 7);
        return [(first, first.AddDays(13)), (_queueDay, _queueDay)];
    }

    private (DateOnly Start, DateOnly End) HistoryCalendarRange()
    {
        var first = AppointmentsCalendarMath.WeekStart(AppointmentsCalendarMath.MonthStart(_historyCalendarCursor));
        return (first, first.AddDays(41));
    }

    /// <summary>Every range the visible tabs need right now; a full reload refreshes exactly these.</summary>
    private List<(DateOnly Start, DateOnly End)> NeededRanges()
    {
        var ranges = new List<(DateOnly, DateOnly)> { CalendarRange() };
        ranges.AddRange(QueueRanges());
        if (_historyCalendarOpen)
        {
            ranges.Add(HistoryCalendarRange());
        }

        return ranges;
    }

    private HashSet<int> NeededChunks() => ChunksOf(NeededRanges());

    private static HashSet<int> ChunksOf(IEnumerable<(DateOnly Start, DateOnly End)> ranges)
    {
        var set = new HashSet<int>();
        foreach (var (start, end) in ranges)
        {
            for (var c = ChunkOf(start); c <= ChunkOf(end); c++)
            {
                set.Add(c);
            }
        }

        return set;
    }

    /// <summary>
    /// Fetches the given chunks from the backend in contiguous runs (each run within the 63-day guard).
    /// Pure with respect to the cache: the caller decides when to publish the result.
    /// </summary>
    private async Task<Dictionary<int, List<AppointmentEvent>>> FetchChunksAsync(IEnumerable<int> wanted)
    {
        var result = new Dictionary<int, List<AppointmentEvent>>();
        var ordered = wanted.Distinct().OrderBy(c => c).ToList();
        var i = 0;
        while (i < ordered.Count)
        {
            var run = new List<int> { ordered[i++] };
            while (i < ordered.Count && ordered[i] == run[^1] + 1 && run.Count < ChunksPerRequest)
            {
                run.Add(ordered[i++]);
            }

            foreach (var c in run)
            {
                result[c] = [];
            }

            var events = await _service.GetForRangeAsync(ChunkStart(run[0]), ChunkStart(run[^1] + 1).AddDays(-1));
            foreach (var e in events)
            {
                if (result.TryGetValue(ChunkOf(e.Date), out var bucket))
                {
                    bucket.Add(e);
                }
            }
        }

        return result;
    }

    /// <summary>Replaces the whole cache with a freshly fetched set (reload / after a write).</summary>
    private void ReplaceChunks(Dictionary<int, List<AppointmentEvent>> fresh)
    {
        _chunks.Clear();
        foreach (var (key, value) in fresh)
        {
            _chunks[key] = value;
        }
    }

    private void MergeChunks(Dictionary<int, List<AppointmentEvent>> fetched, HashSet<int> keep)
    {
        foreach (var (key, value) in fetched)
        {
            _chunks[key] = value;
        }

        if (_chunks.Count <= MaxCachedChunks)
        {
            return;
        }

        // Bounded cache: drop the chunks farthest from what is on screen.
        var center = keep.Count > 0 ? (int)keep.Average() : ChunkOf(_cursor);
        foreach (var key in _chunks.Keys.Where(k => !keep.Contains(k))
                     .OrderByDescending(k => Math.Abs(k - center)).ToList())
        {
            if (_chunks.Count <= MaxCachedChunks)
            {
                break;
            }

            _chunks.Remove(key);
        }
    }

    /// <summary>
    /// Paints <paramref name="render"/> for the ranges now on screen, fetching any chunk that is not cached yet.
    /// While a fetch is outstanding the grids show neither an empty-state nor stale data as final
    /// (<see cref="IsRangeUnsettled"/>); a failure surfaces through the page's load status with a retry.
    /// </summary>
    private void ShowRange(Action render, params (DateOnly Start, DateOnly End)[] ranges)
    {
        var needed = ChunksOf(ranges);
        render();
        if (needed.All(_chunks.ContainsKey))
        {
            IsRangeUnsettled = false;
            return;
        }

        IsRangeUnsettled = true;
        _ = TrackLoadResultAsync(() => LoadMissingAsync(needed, render));
    }

    private async Task<bool> LoadMissingAsync(HashSet<int> needed, Action render)
    {
        var generation = Interlocked.Increment(ref _rangeGeneration);
        try
        {
            var fetched = await FetchChunksAsync(needed.Where(c => !_chunks.ContainsKey(c)));
            MergeChunks(fetched, needed);
            RebuildIndexes();
        }
        catch (Exception)
        {
            if (generation == _rangeGeneration)
            {
                IsRangeUnsettled = false;
            }

            return false;
        }

        // A newer navigation owns the screen now; its own load paints it.
        if (generation == _rangeGeneration)
        {
            IsRangeUnsettled = false;
            render();
        }

        return true;
    }

    private static IReadOnlyList<HistorySelectOption> BuildSelectOptions(
        string allLabel, IEnumerable<(string Id, string Label)> items)
    {
        var list = new List<HistorySelectOption> { new("all", allLabel) };
        list.AddRange(items.Select(i => new HistorySelectOption(i.Id, i.Label)));
        return list;
    }

    // ── lookup helpers (typeOf / docOf) ──────────────────────────────────────
    // An appointment can outlive its type (a custom type removed in Settings): show it as a neutral
    // «نوع حذف‌شده» instead of failing the whole render.
    private static readonly AppointmentType RemovedType =
        new("removed", "نوع حذف‌شده", "removed", "#98a2b3", "#eef1f8", "#536b68", "calendar", 30);

    private AppointmentType TypeOf(string id) =>
        _typeById.TryGetValue(id, out var t) ? t : _types.FirstOrDefault(x => x.Id == id) ?? RemovedType;

    // A05: an event keeps its own doctor identity. A doctor missing from the active roster (unassigned,
    // deactivated, removed) gets a neutral stand-in built from the event's snapshot — never another doctor.
    private AppointmentDoctor DocOf(AppointmentEvent a) =>
        _doctorById.TryGetValue(a.DoctorId, out var d) ? d : AppointmentsPresentation.OrphanDoctor(a);

    /// <summary>Doctor columns for a day: the roster plus one neutral column per orphan doctor that has events.</summary>
    private List<AppointmentDoctor> OrphanColumns(IEnumerable<AppointmentEvent> list) =>
        list.Where(a => !_doctorById.ContainsKey(a.DoctorId))
            .GroupBy(a => a.DoctorId)
            .Select(g => AppointmentsPresentation.OrphanDoctor(g.First()))
            .OrderBy(d => d.Id.Length == 0 ? 1 : 0)
            .ThenBy(d => d.Name, StringComparer.Ordinal)
            .ToList();

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

        _all.Clear();
        foreach (var chunk in _chunks.Values)
        {
            _all.AddRange(chunk);
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

            var doc = _doctorById.TryGetValue(a.DoctorId, out var dd) ? dd.Name : a.DoctorName ?? string.Empty;
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
        // Appointments of a removed type have no filter chip, so they must stay visible rather than vanish.
        var typeVisible = !_typeById.ContainsKey(a.TypeId) || _activeTypeIds.Contains(a.TypeId);
        // Orphan doctors (unassigned/inactive) have no filter chip, so they stay visible like removed types.
        var doctorVisible = !_doctorById.ContainsKey(a.DoctorId) || _activeDoctorIds.Contains(a.DoctorId);
        if (!typeVisible || !doctorVisible)
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
            if (_cursor == _clock.Today)
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

        TypeChips.ReplaceAll(_types.Select(t =>
            new AppointmentTypeChipViewModel(t, typeCounts.GetValueOrDefault(t.Id))
            {
                IsActive = _activeTypeIds.Contains(t.Id),
            }));

        DoctorChips.ReplaceAll(_doctors.Select(d =>
            new AppointmentDoctorChipViewModel(d, doctorCounts.GetValueOrDefault(d.Id))
            {
                IsActive = _activeDoctorIds.Contains(d.Id),
            }));
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
        var today = _clock.Today;
        var nowMinutes = _clock.Now.Minutes;

        // A25: the visible hours follow the doctors' schedules and whatever is booked in this scope, so an early or
        // late appointment can never sit outside the grid.
        var scopeEvents = _viewMode == "day"
            ? OnDate(_cursor)
            : Enumerable.Range(0, 7).SelectMany(i => OnDate(AppointmentsCalendarMath.WeekStart(_cursor).AddDays(i))).ToList();
        var hourRows = ApplyWindow(AppointmentsLayout.Fit(_doctors, scopeEvents));

        // Build the whole column set first, then commit in a single Reset
        // (PERFORMANCE.md §5). The packing/lane logic is unchanged — only the
        // way the result lands in the bound collection differs.
        var built = new List<AppointmentColumnViewModel>();
        var total = 0;

        if (_viewMode == "day")
        {
            var docs = _doctors.Where(d => _activeDoctorIds.Contains(d.Id)).ToList();
            var cols = docs.Count > 0 ? docs : _doctors.ToList();
            var list = OnDate(_cursor);
            cols.AddRange(OrphanColumns(list));

            foreach (var c in cols)
            {
                var evs = Pack(list.Where(a => a.DoctorId == c.Id).ToList());
                total += evs.Count;
                built.Add(new AppointmentColumnViewModel(
                    new AppointmentColumnHeaderViewModel(c, evs.Count),
                    evs,
                    isClosed: false,
                    nowTop: _cursor == today ? NowTop(nowMinutes) : null,
                    _window,
                    hourRows,
                    date: _cursor,
                    doctorId: c.Id,
                    isFirst: built.Count == 0));
            }
        }
        else
        {
            var ws = AppointmentsCalendarMath.WeekStart(_cursor);
            var days = Enumerable.Range(0, 7).Select(i => ws.AddDays(i)).ToList();

            foreach (var d in days)
            {
                var list = OnDate(d);
                total += list.Count;
                built.Add(new AppointmentColumnViewModel(
                    new AppointmentColumnHeaderViewModel(
                        d,
                        isToday: d == today,
                        isOffDay: IsOffDay(d)),
                    Pack(list),
                    isClosed: IsOffDay(d),
                    nowTop: d == today ? NowTop(nowMinutes) : null,
                    _window,
                    hourRows,
                    date: d,
                    doctorId: null,
                    isFirst: built.Count == 0));
            }
        }

        Columns.ReplaceAll(built);
        OnPropertyChanged(nameof(GridMinWidth));
        HasGridEvents = total > 0;
    }

    private double? NowTop(int nowMinutes) => _window.Contains(nowMinutes) ? _window.OffsetOf(nowMinutes) : null;

    // The grid's single geometry source (A25): hour rows, column height and minimum width all derive from it.
    private IReadOnlyList<HourRowViewModel> ApplyWindow(TimeGridWindow window)
    {
        _window = window;
        var rows = Enumerable.Range(0, window.HourCount).Select(i =>
            new HourRowViewModel(AppointmentsPresentation.HourLabel(window.StartMinutes + i * 60), i == 0)).ToList();
        HourRows.ReplaceAll(rows);
        return rows;
    }

    /// <summary>Narrowest the time grid may get before it scrolls horizontally instead of squeezing columns.</summary>
    public double GridMinWidth => AppointmentsLayout.GutterWidth + (Columns.Count * AppointmentsLayout.MinColumnWidth);

    // A25: a day is off only when no doctor's schedule covers it.
    private bool IsOffDay(DateOnly day) => AppointmentsSchedule.IsOffDay(_doctors, day);

    // ── month grid ───────────────────────────────────────────────────────────
    private void RenderMonth()
    {
        IsMonthView = true;
        var today = _clock.Today;
        var ms = AppointmentsCalendarMath.MonthStart(_cursor);
        var first = AppointmentsCalendarMath.WeekStart(ms);
        var mNum = AppointmentsCalendarMath.Month(ms);
        var total = 0;

        // Build the cell list first (with the same early-break behaviour), then
        // commit in a single Reset (PERFORMANCE.md §5). Cell computation is
        // unchanged.
        var cells = new List<MonthCellViewModel>();
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

            var shown = list.Take(3).Select(a => new MonthEventViewModel(a, TypeOf(a.TypeId), showTime: true)).ToList();
            cells.Add(new MonthCellViewModel(
                d,
                isOut,
                isToday: d == today,
                isOffDay: IsOffDay(d),
                shown,
                hiddenCount: Math.Max(0, list.Count - 3),
                isFirstInRow: i % 7 == 0));
        }

        MonthCells.ReplaceAll(cells);
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
                var vm = new AppointmentEventViewModel(a, TypeOf(a.TypeId), DocOf(a), !Matches(a), _window)
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
        var today = _clock.Today;
        var nowMinutes = _clock.Now.Minutes;
        var tk = today;

        var days = Enumerable.Range(0, 14).Select(i => today.AddDays(_queueOffset * 7 + i)).ToList();
        QueueDays.ReplaceAll(days.Select(d =>
        {
            var n = _byDate.TryGetValue(d, out var dayBucket)
                ? dayBucket.Count(a => a.Status != AppointmentStatus.Cancelled)
                : 0;
            return new QueueDayViewModel(
                d,
                isToday: d == tk,
                isClosed: IsOffDay(d),
                count: n)
            {
                IsActive = d == _queueDay,
            };
        }));

        var list = _all.Where(a => a.Date == _queueDay && Visible(a) && Matches(a)).ToList();
        QueueStats.ReplaceAll(
        [
            new QueueStatViewModel("کل", AppointmentsPresentation.Fa(list.Count), Tone.Neutral),
            new QueueStatViewModel(
                "در انتظار تأیید", AppointmentsPresentation.Fa(list.Count(a => a.Lifecycle == AppointmentLifecycle.Scheduled)), Tone.Warn),
            new QueueStatViewModel(
                "پذیرش‌شده", AppointmentsPresentation.Fa(list.Count(a => a.IsCheckedIn)), Tone.Ok),
            new QueueStatViewModel(
                "لغوشده", AppointmentsPresentation.Fa(list.Count(a => a.Status == AppointmentStatus.Cancelled)), Tone.Danger),
        ]);

        var isToday = _queueDay == tk;
        QueueIsToday = isToday;
        QueueDateText = AppointmentsCalendarMath.DayNames[AppointmentsCalendarMath.WeekIndex(_queueDay)] +
                        "، " + AppointmentsCalendarMath.Full(_queueDay);

        var columns = new List<QueueDoctorColumnViewModel>();
        foreach (var d in _doctors.Concat(OrphanColumns(list)))
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
                    .Where(a => !a.IsTerminal)
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

            columns.Add(new QueueDoctorColumnViewModel(d, cards));
        }

        QueueColumns.ReplaceAll(columns);
        // A25: one column per doctor up to four across; a larger roster wraps into further rows instead of squeezing
        // every column below a readable width.
        QueueColumnCount = Math.Clamp(columns.Count, 1, 4);
    }

    // ── history tab ──────────────────────────────────────────────────────────
    // A01: the archive is a backend query (range / status / doctor / type / invoice / search, paged, with
    // scope-wide totals) - never derived from the calendar's cached window.
    [ObservableProperty]
    private bool _isHistoryLoading;

    [ObservableProperty]
    private bool _historyLoadFailed;

    [ObservableProperty]
    private bool _historyHasMore;

    /// <summary>Loaded + nothing matched: distinct from loading and from a failed load.</summary>
    public bool ShowHistoryEmpty => !HasHistoryRows && !IsHistoryLoading && !HistoryLoadFailed;

    public bool ShowHistoryLoadingOnly => !HasHistoryRows && IsHistoryLoading;

    public bool ShowHistoryFailureOnly => !HasHistoryRows && HistoryLoadFailed && !IsHistoryLoading;

    partial void OnIsHistoryLoadingChanged(bool value) => NotifyHistoryStateChanged();

    partial void OnHistoryLoadFailedChanged(bool value) => NotifyHistoryStateChanged();

    partial void OnHasHistoryRowsChanged(bool value) => NotifyHistoryStateChanged();

    private void NotifyHistoryStateChanged()
    {
        OnPropertyChanged(nameof(ShowHistoryEmpty));
        OnPropertyChanged(nameof(ShowHistoryLoadingOnly));
        OnPropertyChanged(nameof(ShowHistoryFailureOnly));
    }

    private (DateOnly? From, DateOnly? To) HistoryDateScope()
    {
        var today = _clock.Today;
        switch (_historyScope)
        {
            case "today":
                return (today, today);
            case "7":
                return (today.AddDays(-7), today);
            case "30":
                return (today.AddDays(-30), today);
            case "month":
                return (AppointmentsCalendarMath.MonthStart(today), today);
            case "range" when _historyRangeStart is not null && _historyRangeEnd is not null:
                var lo = _historyRangeStart.Value <= _historyRangeEnd.Value ? _historyRangeStart.Value : _historyRangeEnd.Value;
                var hi = _historyRangeStart.Value <= _historyRangeEnd.Value ? _historyRangeEnd.Value : _historyRangeStart.Value;
                return (lo, hi);
            default:
                return (null, null);
        }
    }

    private AppointmentHistoryFilter BuildHistoryFilter(int page)
    {
        var (from, to) = HistoryDateScope();
        return new AppointmentHistoryFilter(
            from,
            to,
            _historyStatus switch
            {
                "done" => AppointmentHistoryStatusFilter.Completed,
                "noshow" => AppointmentHistoryStatusFilter.NoShow,
                "cancel" => AppointmentHistoryStatusFilter.Cancelled,
                _ => AppointmentHistoryStatusFilter.All,
            },
            _historyDoctor == "all" ? null : _historyDoctor,
            _historyType == "all" ? null : _historyType,
            _historyInvoice switch
            {
                "paid" => AppointmentInvoiceFilter.Paid,
                "unpaid" => AppointmentInvoiceFilter.Unpaid,
                "partial" => AppointmentInvoiceFilter.Partial,
                "none" => AppointmentInvoiceFilter.None,
                _ => AppointmentInvoiceFilter.All,
            },
            _searchQuery.Length == 0 ? null : _searchQuery,
            page);
    }

    /// <summary>Reloads the archive from page 0 for the current filters; a newer call supersedes an older one.</summary>
    private void RefreshHistory() => _ = TrackLoadResultAsync(() => LoadHistoryAsync(0));

    /// <summary>A scope/status/doctor/type/invoice change: repaint the chips now, reload rows + totals from the backend.</summary>
    private void ApplyHistoryFilters()
    {
        RenderHistory();
        RefreshHistory();
    }

    [RelayCommand]
    private void RetryHistory() => RefreshHistory();

    [RelayCommand]
    private void LoadMoreHistory()
    {
        if (HistoryHasMore && !IsHistoryLoading)
        {
            _ = TrackLoadResultAsync(() => LoadHistoryAsync(_historyPage + 1));
        }
    }

    private async Task<bool> LoadHistoryAsync(int page)
    {
        var generation = Interlocked.Increment(ref _historyGeneration);
        _historyCts?.Cancel();
        var cts = _historyCts = new CancellationTokenSource();
        IsHistoryLoading = true;
        try
        {
            var result = await _service.GetHistoryAsync(BuildHistoryFilter(page), cts.Token);
            if (generation != _historyGeneration)
            {
                return true; // superseded: the newer request owns the screen
            }

            if (page == 0)
            {
                _historyRows.Clear();
            }

            _historyRows.AddRange(result.Items);
            _historyTotals = result.Totals;
            _historyPage = page;
            HistoryHasMore = result.HasMore;
            HistoryLoadFailed = false;
            _historyStale = false;
            IsHistoryLoading = false;
            RenderHistory();
            return true;
        }
        catch (OperationCanceledException)
        {
            return true;
        }
        catch (Exception)
        {
            if (generation == _historyGeneration)
            {
                // Keep whatever was loaded; surface a retryable failure instead of an empty archive.
                HistoryLoadFailed = true;
                IsHistoryLoading = false;
            }

            return false;
        }
    }

    private void RenderHistory()
    {
        var today = _clock.Today;

        var list = _historyRows;
        var perDay = list.GroupBy(a => a.Date).ToDictionary(g => g.Key, g => g.Count());
        var totals = _historyTotals;

        var scopes = new (string Id, string Label)[]
        {
            ("today", "امروز"), ("7", "۷ روز اخیر"), ("30", "۳۰ روز اخیر"), ("month", "ماه جاری"), ("all", "همه"),
        };
        HistoryScopeChips.ReplaceAll(scopes.Select(s =>
            new HistoryScopeChipViewModel(s.Id, s.Label) { IsActive = _historyScope == s.Id }));

        var statuses = new (string Id, string Label)[]
        {
            ("all", "همه وضعیت‌ها"), ("done", "پذیرش شده"), ("noshow", "عدم مراجعه"), ("cancel", "لغو شده"),
        };
        HistoryStatusChips.ReplaceAll(statuses.Select(s =>
            new HistoryStatusChipViewModel(s.Id, s.Label) { IsActive = _historyStatus == s.Id }));

        HistoryStats.ReplaceAll(
        [
            new HistoryStatViewModel("IconRows", AppointmentsPresentation.Fa(totals.Total), "کل نوبت‌ها", Tone.Neutral),
            new HistoryStatViewModel("IconCheck", AppointmentsPresentation.Fa(totals.Completed), "پذیرش شده", Tone.Ok),
            new HistoryStatViewModel("IconX", AppointmentsPresentation.Fa(totals.Cancelled), "لغو شده", Tone.Danger),
            new HistoryStatViewModel("IconAlert", AppointmentsPresentation.Fa(totals.NoShow), "عدم مراجعه", Tone.Warn),
            new HistoryStatViewModel(
                "IconWallet", PersianNumberFormatter.FormatGrouped(totals.PaidAmount), "درآمد وصولی (تومان)", Tone.Ok),
            new HistoryStatViewModel(
                "IconAlert", PersianNumberFormatter.FormatGrouped(totals.OwedAmount), "مطالبات معوق (تومان)", Tone.Danger),
        ]);

        HistoryTotalText = AppointmentsPresentation.Fa(totals.Total) + " نوبت" +
                           (string.IsNullOrEmpty(_searchQuery) ? string.Empty : $" · نتیجهٔ «{_searchQuery}»");

        var groups = new List<HistoryDayGroupViewModel>();
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
                    groups.Add(current);
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

            rows.Add(new HistoryRowViewModel(a, TypeOf(a.TypeId), DocOf(a)));
        }

        if (current is not null)
        {
            groups.Add(current);
        }

        HistoryGroups.ReplaceAll(groups);
        HasHistoryRows = list.Count > 0;

        RenderHistoryCalendar();
    }

    private void RenderHistoryCalendar()
    {
        var today = _clock.Today;

        var monthStart = AppointmentsCalendarMath.MonthStart(_historyCalendarCursor);
        var mNum = AppointmentsCalendarMath.Month(monthStart);
        var yNum = AppointmentsCalendarMath.Year(monthStart);
        var gridStart = monthStart.AddDays(-AppointmentsCalendarMath.WeekIndex(monthStart));

        var histDates = _all.Where(a => a.Date <= today && a.IsTerminal).Select(a => a.Date).ToHashSet();
        var lo = _historyRangeStart is not null && _historyRangeEnd is not null
            ? (_historyRangeStart.Value <= _historyRangeEnd.Value ? _historyRangeStart.Value : _historyRangeEnd.Value) : (DateOnly?)null;
        var hi = _historyRangeStart is not null && _historyRangeEnd is not null
            ? (_historyRangeStart.Value <= _historyRangeEnd.Value ? _historyRangeEnd.Value : _historyRangeStart.Value) : (DateOnly?)null;

        var calendarCells = new List<HistoryCalendarCellViewModel>(42);
        for (var i = 0; i < 42; i++)
        {
            var d = gridStart.AddDays(i);
            var isOut = AppointmentsCalendarMath.Month(d) != mNum;
            var inRange = lo is not null && hi is not null
                ? d >= lo && d <= hi
                : _historyRangeStart is not null && _historyRangeEnd is null && d == _historyRangeStart;
            var edge = d == _historyRangeStart || d == _historyRangeEnd;

            calendarCells.Add(new HistoryCalendarCellViewModel(
                d,
                isOut,
                isToday: d == today,
                hasHistory: histDates.Contains(d) && !isOut,
                isOffDay: IsOffDay(d),
                isPast: d <= today,
                inRange,
                edge));
        }

        HistoryCalendarDays.ReplaceAll(calendarCells);

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

    // ── settings tab ─────────────────────────────────────────────────────────
    /// <summary>Rows of the «انواع نوبت» list (catalog defaults first, then clinic-created types).</summary>
    public RangeObservableCollection<AppointmentSettingTypeRowViewModel> SettingTypes { get; } = [];

    /// <summary>Duration choices (minutes) for a new appointment type.</summary>
    public IReadOnlyList<SettingNumberOption> TypeDurationOptions { get; } =
        new[] { 15, 20, 30, 45, 60, 90, 120 }.Select(m => new SettingNumberOption(m, AppointmentsPresentation.Fa(m) + " دقیقه")).ToArray();

    /// <summary>Reminder lead-time choices (hours before the appointment).</summary>
    public IReadOnlyList<SettingNumberOption> ReminderLeadOptions { get; } =
        new[] { 1, 2, 3, 6, 12, 24, 48, 72 }.Select(h => new SettingNumberOption(h, AppointmentsPresentation.Fa(h) + " ساعت قبل")).ToArray();

    private CalendarMode _calendarMode = CalendarMode.TimeBased;
    private bool _smsOnBooking = true;
    private bool _smsReminder = true;
    private int _smsReminderLeadHours = 24;
    private string _newTypeLabel = string.Empty;
    private int _newTypeDuration = 30;
    private bool _newTypeRequiresDoctor = true;
    private bool _applyingSettings;

    /// <summary>
    /// A17: presentation hint only — whether the signed-in user may change clinic-wide appointment settings. The
    /// authoritative check is the <c>settings.write</c> permission on every settings use case in Application.
    /// </summary>
    public bool CanEditSettings => _capabilities.Can(PermissionCodes.SettingsWrite);

    public bool IsSettingsReadOnly => !CanEditSettings;

    /// <summary>Set when the stored settings file was unreadable and has been quarantined (A26).</summary>
    public string? SettingsRecoveryNotice { get; private set; }

    public bool HasSettingsRecoveryNotice => !string.IsNullOrEmpty(SettingsRecoveryNotice);

    private void OnCapabilitiesChanged(object? sender, EventArgs e)
    {
        void Raise()
        {
            OnPropertyChanged(nameof(CanEditSettings));
            OnPropertyChanged(nameof(IsSettingsReadOnly));
        }

        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            Raise();
        }
        else
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(Raise);
        }
    }

    /// <summary>Runs one settings write; a denial/failure is reported instead of escaping as an unhandled fault.</summary>
    private async Task<bool> TryWriteSettingsAsync(Func<Task> write)
    {
        try
        {
            await write();
            return true;
        }
        catch (InvalidOperationException ex)
        {
            _toast.Show(ex.Message is "PERMISSION_DENIED" or "SESSION_EXPIRED"
                ? "شما اجازهٔ تغییر تنظیمات نوبت را ندارید."
                : "ذخیرهٔ تنظیمات انجام نشد؛ دوباره تلاش کنید.", Tone.Danger);
            return false;
        }
    }

    public bool CalendarModeIsDayOnly => _calendarMode == CalendarMode.DayOnly;
    public bool CalendarModeIsTimeBased => _calendarMode == CalendarMode.TimeBased;

    public bool SmsOnBooking
    {
        get => _smsOnBooking;
        set
        {
            if (SetProperty(ref _smsOnBooking, value))
            {
                PersistSms();
            }
        }
    }

    public bool SmsReminder
    {
        get => _smsReminder;
        set
        {
            if (SetProperty(ref _smsReminder, value))
            {
                PersistSms();
            }
        }
    }

    public int SmsReminderLeadHours
    {
        get => _smsReminderLeadHours;
        set
        {
            if (SetProperty(ref _smsReminderLeadHours, value))
            {
                OnPropertyChanged(nameof(SmsReminderHintText));
                PersistSms();
            }
        }
    }

    public string SmsReminderHintText =>
        $"ترجیح ثبت‌شده: {AppointmentsPresentation.Fa(_smsReminderLeadHours)} ساعت پیش از نوبت (ارسال پیامک در این نسخه فعال نیست)";

    public string NewTypeLabel
    {
        get => _newTypeLabel;
        set
        {
            if (SetProperty(ref _newTypeLabel, value))
            {
                OnPropertyChanged(nameof(CanAddType));
            }
        }
    }

    public int NewTypeDuration
    {
        get => _newTypeDuration;
        set => SetProperty(ref _newTypeDuration, value);
    }

    public bool NewTypeRequiresDoctor
    {
        get => _newTypeRequiresDoctor;
        set => SetProperty(ref _newTypeRequiresDoctor, value);
    }

    public bool CanAddType => !string.IsNullOrWhiteSpace(_newTypeLabel);

    /// <summary>Pushes persisted settings into the bindable state without writing them back.</summary>
    private void ApplySettings(AppointmentSettings settings)
    {
        _applyingSettings = true;
        try
        {
            SettingsRecoveryNotice = settings.RecoveryNotice;
            OnPropertyChanged(nameof(SettingsRecoveryNotice));
            OnPropertyChanged(nameof(HasSettingsRecoveryNotice));
            _calendarMode = settings.CalendarMode;
            _smsOnBooking = settings.Sms.SendOnBooking;
            _smsReminder = settings.Sms.SendReminder;
            _smsReminderLeadHours = settings.Sms.ReminderLeadHours;
            OnPropertyChanged(nameof(CalendarModeIsDayOnly));
            OnPropertyChanged(nameof(CalendarModeIsTimeBased));
            OnPropertyChanged(nameof(SmsOnBooking));
            OnPropertyChanged(nameof(SmsReminder));
            OnPropertyChanged(nameof(SmsReminderLeadHours));
            OnPropertyChanged(nameof(SmsReminderHintText));

            var customById = new Dictionary<string, CustomAppointmentType>();
            foreach (var custom in settings.CustomTypes)
            {
                customById[custom.Id] = custom;
            }

            var rows = new List<AppointmentSettingTypeRowViewModel>(_types.Count + customById.Count);
            // Read-only vaccination projections (booster / treatment-plan days) are not configurable appointment types.
            foreach (var type in _types.Where(t => !AppointmentTypeIds.IsReadOnlyProjection(t.Id)))
            {
                var isCustom = customById.TryGetValue(type.Id, out var c);
                rows.Add(new AppointmentSettingTypeRowViewModel(
                    type.Id, type.Label, type.Accent, type.DurationMinutes, isCustom, isCustom ? c!.RequiresDoctor : null));
            }

            // Custom types the catalog merge has not surfaced yet are still listed so they can be removed.
            foreach (var custom in settings.CustomTypes)
            {
                if (!_types.Exists(t => t.Id == custom.Id))
                {
                    rows.Add(new AppointmentSettingTypeRowViewModel(
                        custom.Id, custom.Label, custom.ColorHex, custom.DurationMinutes, true, custom.RequiresDoctor));
                }
            }

            SettingTypes.ReplaceAll(rows);
        }
        finally
        {
            _applyingSettings = false;
        }
    }

    /// <summary>
    /// Day-only mode has no time grid: the calendar is forced to the month view (whose cells are whole
    /// days) and the day/week switch is hidden. Time-based mode leaves the chosen view untouched.
    /// </summary>
    private void LockViewForCalendarMode()
    {
        if (!CalendarModeIsDayOnly || _viewMode == "month")
        {
            return;
        }

        _viewMode = "month";
        OnPropertyChanged(nameof(ViewMode));
        OnPropertyChanged(nameof(IsDayView));
        OnPropertyChanged(nameof(IsWeekView));
        OnPropertyChanged(nameof(IsMonthViewMode));
    }

    private void PersistSms()
    {
        if (_applyingSettings)
        {
            return;
        }

        _ = PersistSmsAsync(new AppointmentSmsSettings(_smsOnBooking, _smsReminder, _smsReminderLeadHours));
    }

    private async Task PersistSmsAsync(AppointmentSmsSettings sms)
    {
        if (!await TryWriteSettingsAsync(() => _settings.SetSmsAsync(sms)))
        {
            // Roll the toggles back to what is actually stored.
            ApplySettings(await _settings.GetAsync());
        }
    }

    [RelayCommand]
    private async Task SetCalendarMode(string? key)
    {
        var mode = key == "day" ? CalendarMode.DayOnly : CalendarMode.TimeBased;
        if (mode == _calendarMode || !CanEditSettings)
        {
            return;
        }

        if (!await TryWriteSettingsAsync(() => _settings.SetCalendarModeAsync(mode)))
        {
            return;
        }

        _calendarMode = mode;
        OnPropertyChanged(nameof(CalendarModeIsDayOnly));
        OnPropertyChanged(nameof(CalendarModeIsTimeBased));
        LockViewForCalendarMode();
        // The calendar tab is cached by its LazyTabHost: repaint it now so it is correct on return.
        Render();
        _toast.Show(mode == CalendarMode.DayOnly ? "حالت تقویم: تخصیص خودکار زمان" : "حالت تقویم: بر اساس ساعت", Tone.Ok);
    }

    [RelayCommand]
    private async Task AddAppointmentType()
    {
        var label = _newTypeLabel.Trim();
        if (label.Length == 0)
        {
            return;
        }

        if (SettingTypes.Any(t => string.Equals(t.Label, label, StringComparison.CurrentCultureIgnoreCase)))
        {
            _toast.Show("نوعی با این نام از قبل وجود دارد.", Tone.Warn);
            return;
        }

        if (!CanEditSettings ||
            !await TryWriteSettingsAsync(() => _settings.AddTypeAsync(label, _newTypeDuration, _newTypeRequiresDoctor)))
        {
            return;
        }

        NewTypeLabel = string.Empty;
        _toast.Show($"نوع «{label}» اضافه شد.", Tone.Ok);
        await ReloadTypesAsync();
    }

    [RelayCommand]
    private async Task RemoveAppointmentType(AppointmentSettingTypeRowViewModel? row)
    {
        if (row is null || !row.CanRemove)
        {
            return;
        }

        if (!CanEditSettings || !await TryWriteSettingsAsync(() => _settings.RemoveTypeAsync(row.Id)))
        {
            return;
        }

        _toast.Show($"نوع «{row.Label}» حذف شد.", Tone.Info);
        await ReloadTypesAsync();
    }

    /// <summary>
    /// Re-reads the type catalog + settings after an add/remove so the toolbar chips, filters and
    /// the booking panel's type menu agree immediately, then tells other consumers to refresh.
    /// </summary>
    private async Task ReloadTypesAsync()
    {
        var types = await _service.GetTypesAsync();
        _types.Clear();
        _types.AddRange(types);

        // Keep only filters that still exist; newly added types start active so their events show.
        var known = new HashSet<string>(_types.Select(t => t.Id));
        _activeTypeIds.RemoveWhere(id => !known.Contains(id));
        foreach (var t in _types)
        {
            if (!_typeById.ContainsKey(t.Id))
            {
                _activeTypeIds.Add(t.Id);
            }
        }

        if (_activeTypeIds.Count == 0)
        {
            foreach (var t in _types)
            {
                _activeTypeIds.Add(t.Id);
            }
        }

        RebuildIndexes();
        HistoryTypeOptions = BuildSelectOptions("همه انواع نوبت", _types.Where(t => !AppointmentTypeIds.IsReadOnlyProjection(t.Id)).Select(t => (t.Id, t.Label)));
        OnPropertyChanged(nameof(HistoryTypeOptions));
        ApplySettings(await _settings.GetAsync());
        RenderActiveTab();
        _changes.Notify(DataDomain.Appointments);
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
        OnPropertyChanged(nameof(IsSettingsTab));
        OnPropertyChanged(nameof(ShowCalendarChrome));

        // A07: tab views are rebuilt lazily, so a write made while another tab was active must show up the moment
        // this tab is activated again - always render the target tab instead of trusting its cached subtree.
        if (key == "cal")
        {
            ShowRange(Render, CalendarRange());
        }
        else if (key == "queue")
        {
            ShowRange(RenderQueue, QueueRanges());
        }
        else if (key == "hist")
        {
            RenderHistory();
            if (_historyStale)
            {
                RefreshHistory();
            }
        }
    }

    [RelayCommand]
    private void SetView(string? key)
    {
        if (string.IsNullOrEmpty(key) || (CalendarModeIsDayOnly && key != "month"))
        {
            return;
        }

        _viewMode = key;
        OnPropertyChanged(nameof(ViewMode));
        OnPropertyChanged(nameof(IsDayView));
        OnPropertyChanged(nameof(IsWeekView));
        OnPropertyChanged(nameof(IsMonthViewMode));
        ShowRange(Render, CalendarRange());
    }

    [RelayCommand]
    private void GoToday()
    {
        _cursor = _clock.Today;
        ShowRange(Render, CalendarRange());
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

        ShowRange(Render, CalendarRange());
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

    // Search debounce (~160ms), moved out of the code-behind into the VM.
    private readonly Debouncer _searchDebounce = new(TimeSpan.FromMilliseconds(160));

    partial void OnSearchQueryTextChanged(string value)
    {
        HasSearchValue = value.Trim().Length > 0;
        _searchDebounce.Debounce(() => ApplySearch(SearchQueryText));
    }

    public void ApplySearch(string? text)
    {
        _searchQuery = text?.Trim() ?? string.Empty;
        _normalizedQuery = _searchQuery.Length == 0
            ? string.Empty
            : PersianText.Normalize(_searchQuery);
        _historyStale = true;
        RenderActiveTab();
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

        // Boosters and treatment-plan days are read-only projections of vaccination data: they have no booking
        // detail, so they open the vaccine detail popup (falling back to the pet file when it cannot be resolved).
        if (ev is { IsReadOnlyProjection: true, PetId: { Length: > 0 } petId })
        {
            await ShowVaccinationDetailAsync(ev, petId);
            return;
        }

        if (ev is not null)
        {
            await ShowDetailAsync(ev);
            return;
        }

        await OpenNewBookingAsync(null);
    }

    /// <summary>
    /// Day-only mode: clicking a day cell books that day directly (no clock time to pick).
    /// Ignored in time-based mode, where booking happens through the toolbar button.
    /// </summary>
    [RelayCommand]
    private async Task OpenDayBooking(DateOnly day)
    {
        if (!CalendarModeIsDayOnly)
        {
            return;
        }

        await OpenNewBookingAsync(day);
    }

    private async Task OpenNewBookingAsync(DateOnly? date)
    {
        var panel = new BookingPanelViewModel(_booking, _readiness, _service, _toast, _dialogs, _settings, _patients, _clock);
        panel.Saved += RefreshAfterWrite;
        await panel.LoadForNewAsync(date, null, null, null);
        _dialogs.ShowSheet(new BookingPanelView { DataContext = panel }, width: 1080, height: 864);
    }

    // ── direct-manipulation on the time grid (drag-to-create / drag-to-move) ──────
    //
    // The grid view owns the pointer gestures and the ghost visuals; the ViewModel
    // owns everything that touches data. These two seams keep the View free of any
    // booking/update logic (and free of Jalali/geometry math duplication).

    /// <summary>The visible window's first minute (grid top) — the View's single minute↔pixel origin for drag math.</summary>
    public int GridWindowStartMinutes => _window.StartMinutes;

    /// <summary>The visible window's last minute (grid bottom).</summary>
    public int GridWindowEndMinutes => _window.EndMinutes;

    /// <summary>Pixels per hour on the time grid — shared with the View so a dragged box maps pixels to minutes exactly.</summary>
    public double GridPixelsPerHour => AppointmentsLayout.PixelsPerHour;

    /// <summary>The snap granularity (minutes) a dragged start/box rounds to. 15 min mirrors the booking sheet's grid.</summary>
    public int GridSnapMinutes => 15;

    /// <summary>The default duration (minutes) a freshly dragged box starts at before the user grows it.</summary>
    public int GridDefaultDurationMinutes => _types.Count > 0 ? _types[0].DurationMinutes : 30;

    /// <summary>Time-based drag gestures only make sense in the day/week time grid, never the month/day-only views.</summary>
    public bool SupportsTimeGridDrag => !IsMonthView && !CalendarModeIsDayOnly;

    /// <summary>
    /// Opens the booking sheet for a range the user dragged out on the grid: the day + doctor come from the
    /// column, the start + duration from the box geometry (already snapped by the View). Mirrors a grid-cell
    /// click but with the dragged time pre-filled.
    /// </summary>
    public async Task CreateBookingFromDragAsync(DateOnly date, string? doctorId, int startMinutes, int durationMinutes)
    {
        if (!SupportsTimeGridDrag)
        {
            return;
        }

        var start = Math.Clamp(startMinutes, _window.StartMinutes, Math.Max(_window.StartMinutes, _window.EndMinutes - GridSnapMinutes));
        var duration = Math.Clamp(durationMinutes, GridSnapMinutes, _window.EndMinutes - start);

        var panel = new BookingPanelViewModel(_booking, _readiness, _service, _toast, _dialogs, _settings, _patients, _clock);
        panel.Saved += RefreshAfterWrite;
        await panel.LoadForNewAsync(date, start, duration, string.IsNullOrEmpty(doctorId) ? null : doctorId);
        _dialogs.ShowSheet(new BookingPanelView { DataContext = panel }, width: 1080, height: 864);
    }

    /// <summary>
    /// Persists an appointment the user dragged to a new slot: same patient/type/note, new day + start (+ doctor in
    /// day view). Read-only projections and locked (in-visit/terminal) rows cannot move; a no-op move is skipped so
    /// an accidental tiny drag never issues a needless write. Any backend rejection surfaces as a toast and the page
    /// reloads to its last good state.
    /// </summary>
    public async Task MoveAppointmentFromDragAsync(
        AppointmentEventViewModel card, DateOnly newDate, string? newDoctorId, int newStartMinutes)
    {
        if (card is null)
        {
            return;
        }

        var ev = card.Model;
        if (ev.IsReadOnlyProjection || !ev.CanMove)
        {
            _toast.Show("این نوبت قابل جابه‌جایی نیست.", Tone.Info);
            return;
        }

        var start = Math.Clamp(newStartMinutes, _window.StartMinutes, Math.Max(_window.StartMinutes, _window.EndMinutes - ev.DurationMinutes));
        // Week view keeps the event's own doctor (columns are days there); day view may reassign the doctor.
        var doctorId = string.IsNullOrEmpty(newDoctorId) ? ev.DoctorId : newDoctorId;

        if (newDate == ev.Date && doctorId == ev.DoctorId && start == ev.StartMinutes)
        {
            return; // nothing actually changed
        }

        try
        {
            // Re-read the detail so the move carries the concurrency token the backend expects (A29).
            var detail = await _booking.GetDetailAsync(ev.Id);
            var version = detail?.Version;

            await _booking.UpdateAsync(ev.Id, new UpdateAppointmentRequest(
                newDate, start, ev.DurationMinutes, ev.TypeId, doctorId,
                ev.PetName, ev.PetKind, ev.OwnerName, ev.OwnerPhone, ev.AvatarTone, ev.SpeciesIconKey,
                ev.Note, SendSms: false, SendReminder: false, version));

            _toast.Show("زمان جدید نوبت ذخیره شد.", Tone.Ok);
            RefreshAfterWrite();
        }
        catch (Exception ex) when (!AppointmentErrorText.IsCancellation(ex))
        {
            if (ex is AppointmentOperationException { WriteCommitted: true })
            {
                _toast.Show(AppointmentErrorText.For(ex), Tone.Info);
                RefreshAfterWrite();
                return;
            }

            _toast.Show(AppointmentErrorText.For(ex), Tone.Danger);
            RequestRefresh(); // snap the card back to its stored slot
        }
    }

    /// <summary>
    /// Opens the vaccine/medication detail popup of a calendar projection: the shared treatment-day popup for a plan
    /// day, the vaccination-record sheet for a recurring booster. The source row is re-read so the popup always
    /// reflects the stored state; when it is gone (or access was lost) the pet file opens instead.
    /// </summary>
    private async Task ShowVaccinationDetailAsync(AppointmentEvent ev, string petId)
    {
        try
        {
            if (ev.TreatmentOccurrenceId is { } occurrenceId)
            {
                var rows = await _vaccination.GetCalendarAsync(ev.Date, ev.Date, new Animora.Contracts.Vaccination.CalendarFilter(PetId: petId));
                if (rows.FirstOrDefault(o => o.OccurrenceId == occurrenceId) is { } occurrence)
                {
                    _dialogs.ShowModal(new Pages.Vaccination.TreatmentOccurrenceDetailViewModel(
                        _vaccination, _dialogs, _toast, occurrence, OnVaccinationChanged, _presenter.OpenPetSheetAsync));
                    return;
                }
            }
            else if (ev.IsVaccineBooster)
            {
                var record = (await _vaccination.GetRecordsAsync()).FirstOrDefault(r => r.Id == ev.Code);
                if (record is not null)
                {
                    var vaccine = (await _vaccination.GetCatalogAsync()).FirstOrDefault(v => v.Id == record.VaccineId);
                    _dialogs.ShowModal(new Pages.Vaccination.VaccinationRecordDetailViewModel(record, vaccine, _dialogs));
                    return;
                }
            }
        }
        catch (InvalidOperationException)
        {
            // Fall through to the pet file — it carries the vaccination history too.
        }

        await _presenter.OpenPetSheetAsync(petId);
    }

    private void OnVaccinationChanged()
    {
        _changes.Notify(DataDomain.Vaccination, DataDomain.History, DataDomain.Patients, DataDomain.Dashboard);
        RequestRefresh();
    }

    private async Task ShowDetailAsync(AppointmentEvent ev)
    {
        var detail = new BookingDetailViewModel(
            _booking,
            _service,
            _toast,
            _dialogs,
            _clock,
            onChanged: RefreshAfterWrite,
            onEdit: (editEv, version) => _ = OpenEditAsync(editEv, version),
            onOpenVisit: OpenLinkedVisitAsync);
        if (await detail.LoadAsync(ev.Id))
        {
            // Every appointment item opens its details as a small CENTERED dialog
            // (not a side drawer), so clicking any card/row on the queue, calendar,
            // month or history behaves the same. The view is 420 wide; cap the modal
            // a touch above that so it never stretches.
            _dialogs.ShowModal(new BookingDetailView { DataContext = detail }, maxWidth: 440);
        }
    }

    /// <summary>A13: opens the visit linked to an appointment through the existing visit-encounter navigation seam.</summary>
    private async Task OpenLinkedVisitAsync(AppointmentEvent ev)
    {
        if (string.IsNullOrEmpty(ev.VisitId))
        {
            _toast.Show("برای این نوبت پروندهٔ ویزیتی ثبت نشده است.", Tone.Info);
            return;
        }

        try
        {
            var draft = await _encounters.GetAsync(ev.VisitId);
            _dialogs.Close();
            await _navigation.NavigateToAsync<Visits.VisitEncounterViewModel>(vm => vm.LoadAsync(draft));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _toast.Show("پروندهٔ ویزیت باز نشد؛ دوباره تلاش کنید.", Tone.Danger);
        }
    }

    private async Task OpenEditAsync(AppointmentEvent ev, string? version)
    {
        var panel = new BookingPanelViewModel(_booking, _readiness, _service, _toast, _dialogs, _settings, _patients, _clock);
        panel.Saved += RefreshAfterWrite;
        await panel.LoadForEditAsync(ev, version);
        _dialogs.ShowSheet(new BookingPanelView { DataContext = panel }, width: 1080, height: 864);
    }

    /// <summary>
    /// A write made from this page's own sheets/drawer. It only announces the change: the page's own subscription
    /// (<see cref="RefreshOnDataChange"/>) turns it - together with any simultaneous Visits/Patients notification -
    /// into exactly one coalesced reload (A06). There is deliberately no second, untracked refetch path.
    /// </summary>
    private void RefreshAfterWrite()
    {
        _changes.Notify(DataDomain.Appointments, DataDomain.Visits, DataDomain.Patients, DataDomain.Dashboard);

        // A page that is not the current one ignores notifications; make sure the write is still reflected.
        if (!IsCurrentPage)
        {
            RequestRefresh();
        }
    }

    // A06: every refresh trigger (external notifications, local writes) funnels through here. Triggers raised in the
    // same dispatch tick collapse into ONE queued reload; the reload builds a complete snapshot from locals and
    // commits it once (LoadAsync), keeping the last good snapshot when any read fails.
    private int _refreshQueued;

    private void RequestRefresh()
    {
        if (Interlocked.Exchange(ref _refreshQueued, 1) == 1)
        {
            return;
        }

        _ = RunQueuedRefreshAsync();
    }

    private async Task RunQueuedRefreshAsync()
    {
        // Let every notification of the current tick arrive before the single reload starts.
        await Task.Yield();
        Interlocked.Exchange(ref _refreshQueued, 0);

        // A load already running may predate this write: wait for it (bounded) so the single reload below is
        // never swallowed by the re-entrancy guard of LoadAsync.
        for (var i = 0; i < 100 && (IsLoadInFlight || _isLoading); i++)
        {
            await Task.Delay(50);
        }

        await ReloadPageAsync();
    }

    /// <summary>
    /// «خروجی»: opens the print/export options dialog, pre-seeded with the date
    /// span the calendar is currently showing (day / week / month view).
    /// </summary>
    [RelayCommand]
    private void ExportCalendar()
    {
        var scope = ScopeDates();
        var (from, to) = scope.Count > 0 ? (scope.Min(), scope.Max()) : (_cursor, _cursor);
        var vm = new AppointmentExportViewModel(
            _service, _print, _dialogs, _toast, _profile, _user, _clock,
            _doctors.ToList(), _types.ToList(), dayOnly: false, from, to);
        _dialogs.ShowModal(new AppointmentExportView { DataContext = vm }, 980);
    }

    // ── queue commands ───────────────────────────────────────────────────────
    [RelayCommand]
    private void SelectQueueDay(QueueDayViewModel? day)
    {
        if (day is null)
        {
            return;
        }

        _queueDay = day.Day;
        ShowRange(RenderQueue, QueueRanges());
    }

    [RelayCommand]
    private void ShiftQueueWeek(string? dir)
    {
        _queueOffset = Math.Max(0, _queueOffset + (dir == "-1" ? -1 : 1));
        ShowRange(RenderQueue, QueueRanges());
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
        _queueDay = _clock.Today;
        ShowRange(RenderQueue, QueueRanges());
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
        ApplyHistoryFilters();
    }

    [RelayCommand]
    private void SetHistoryStatus(HistoryStatusChipViewModel? chip)
    {
        if (chip is null)
        {
            return;
        }

        _historyStatus = chip.Id;
        ApplyHistoryFilters();
    }

    [RelayCommand]
    private void ToggleHistoryCalendar()
    {
        _historyCalendarOpen = !_historyCalendarOpen;
        if (_historyCalendarOpen && _historyRangeStart is not null && _historyRangeEnd is null)
        {
            _historyRangeStart = null;
        }

        ShowRange(RenderHistory, HistoryCalendarRange());
    }

    [RelayCommand]
    private void HistoryCalendarStep(string? dir)
    {
        var d = AppointmentsCalendarMath.MonthStart(_historyCalendarCursor);
        _historyCalendarCursor = dir == "-1" ? AppointmentsCalendarMath.MonthStart(d.AddDays(-1))
            : AppointmentsCalendarMath.MonthStart(d.AddDays(35));
        ShowRange(RenderHistoryCalendar, HistoryCalendarRange());
    }

    [RelayCommand]
    private void HistoryCalendarToday()
    {
        _historyCalendarCursor = _clock.Today;
        ShowRange(RenderHistoryCalendar, HistoryCalendarRange());
    }

    [RelayCommand]
    private void HistoryCalendarClear()
    {
        _historyRangeStart = null;
        _historyRangeEnd = null;
        if (_historyScope == "range")
        {
            _historyScope = "30";
            ApplyHistoryFilters();
            return;
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
            ApplyHistoryFilters();
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

        ApplyHistoryFilters();
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
        ShowRange(Render, CalendarRange());
    }

    public string SelectedHistoryDoctor
    {
        get => _historyDoctor;
        set
        {
            if (SetProperty(ref _historyDoctor, value))
            {
                ApplyHistoryFilters();
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
                ApplyHistoryFilters();
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
                ApplyHistoryFilters();
            }
        }
    }

    public bool IsHistoryCalendarOpen => _historyCalendarOpen;
}
