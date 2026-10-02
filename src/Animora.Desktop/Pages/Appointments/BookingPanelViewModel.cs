using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Animora.Contracts.Appointments;
using Animora.Contracts.Diagnostics;
using Animora.Contracts.Patients;
using Animora.Desktop.Controls;
using Animora.Desktop.Localization;
using Animora.Desktop.Pages.Patients;
using Animora.Desktop.Services;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Pages.Appointments;

/// <summary>
/// ViewModel for the booking/detail side panel, mirroring the reference's
/// booking-sheet controller (Animora-UI/assets/appointments-panel.js). Holds the
/// step-like state (patient → type → doctor → date/slot → extras) plus the
/// running summary sidebar, and drives create/edit/cancel through
/// <see cref="IAppointmentBookingService"/> (the single backend swap point,
/// R-18) — no sample data or mock branching leaks into this public shape.
/// </summary>
public sealed partial class BookingPanelViewModel : ObservableObject
{
    private readonly IAppointmentBookingService _booking;
    private readonly IStoreReadinessGate _readiness;
    private readonly IAppointmentsQueryService _catalog;
    private readonly IToastService _toast;
    private readonly IDialogService _dialogs;
    private readonly IAppointmentSettingsService _settings;
    private readonly IPatientsQueryService _patients;

    /// <summary>Raised after a successful save so the calendar can close + refresh.</summary>
    public event Action? Saved;

    private readonly List<AppointmentDoctor> _doctors = [];
    private readonly List<AppointmentType> _types = [];
    private const int PatientPageSize = 20;
    private readonly Task _initialLoad;
    private CancellationTokenSource? _patientSearchCancellation;
    private int _patientSearchVersion;
    private List<TimeSlot> _slots = [];
    private List<AppointmentEvent> _conflicts = [];

    // Reference B.* state.
    private string _mode = "new";
    private string? _id;
    private string _type = "visit";
    private BookingPatient? _selectedPatient;
    private string _doc = "d1";
    private DateOnly _date;
    private int _start = 570;
    private int _dur = 30;
    private DateOnly _originalDate;
    private string _query = string.Empty;
    private string _kindFilter = "all";
    private bool _picking = true;

    public BookingPanelViewModel(
        IAppointmentBookingService booking,
        IStoreReadinessGate readiness,
        IAppointmentsQueryService catalog,
        IToastService toast,
        IDialogService dialogs,
        IAppointmentSettingsService settings,
        IPatientsQueryService patients)
    {
        _settings = settings;
        _patients = patients;
        _booking = booking;
        _readiness = readiness;
        _catalog = catalog;
        _toast = toast;
        _dialogs = dialogs;
        _date = DateOnly.FromDateTime(DateTime.Today);
        _initialLoad = LoadAsync();
    }

    // ── collections ──────────────────────────────────────────────────────────
    public ObservableCollection<BookingTypeOptionViewModel> TypeOptions { get; } = [];
    public ObservableCollection<BookingDoctorOptionViewModel> DoctorOptions { get; } = [];
    public RangeObservableCollection<BookingPatientOptionViewModel> PatientList { get; } = [];
    public ObservableCollection<BookingKindChipViewModel> KindChips { get; } = [];
    public ObservableCollection<BookingDurationOptionViewModel> DurationOptions { get; } = [];
    public ObservableCollection<BookingCalendarDayViewModel> CalendarDays { get; } = [];
    public ObservableCollection<BookingSlotGroupViewModel> SlotGroups { get; } = [];
    public ObservableCollection<BookingSummaryRowViewModel> SummaryRows { get; } = [];

    /// <summary>Fixed duration choices above the slot grid (reference: 15/20/30/45/60/90).</summary>
    public static IReadOnlyList<int> DurationChoices { get; } = [15, 20, 30, 45, 60, 90];

    // ── header / titles ──────────────────────────────────────────────────────
    public string EyebrowText => "برنامه‌ریزی مراجعه";

    [ObservableProperty]
    private string _title = "ثبت نوبت جدید";

    /// <summary>
    /// A02: editing an existing appointment is an explicit reschedule — only date / start / duration are
    /// persisted by the backend (<c>RescheduleAppointmentCommand</c>). Type, doctor, patient and note are
    /// therefore read-only in edit mode instead of silently discarding the user's changes.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdentityEditable))]
    private bool _isEditMode;

    /// <summary>True while the type / patient / doctor / note fields may be changed (new bookings only).</summary>
    public bool IsIdentityEditable => !IsEditMode;

    public string EditLockedHint => "در جابه‌جایی نوبت فقط تاریخ، ساعت و مدت قابل تغییر است؛ نوع، پزشک، بیمار و یادداشت قابل‌ویرایش نیست.";

    [ObservableProperty]
    private string _headTypeLabel = string.Empty;

    [ObservableProperty]
    private string _headDateText = string.Empty;

    [ObservableProperty]
    private string _headRangeText = string.Empty;

    [ObservableProperty]
    private string _headTypeClass = "visit";

    // ── calendar mode (settings) ─────────────────────────────────────────────
    /// <summary>
    /// True when the clinic books by whole day (no clock time). The slot grid, duration picker and
    /// conflict check are hidden; the saved start/duration are conventional values (see
    /// <see cref="ResolveDayOnlySlotAsync"/>), not something the user picks.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTimed))]
    [NotifyPropertyChangedFor(nameof(DateSectionTitle))]
    [NotifyPropertyChangedFor(nameof(DateSectionHint))]
    private bool _isDayOnly;

    /// <summary>The inverse of <see cref="IsDayOnly"/>, for XAML bindings.</summary>
    public bool IsTimed => !IsDayOnly;

    public string DateSectionTitle => IsDayOnly ? "تاریخ نوبت" : "تاریخ و ساعت";

    public string DateSectionHint => IsDayOnly
        ? "فقط روز نوبت را انتخاب کنید؛ ساعت مشخص نمی‌شود"
        : "ابتدا روز، سپس مدت و ساعت شروع را انتخاب کنید";

    /// <summary>Day-only info card: how many appointments the chosen doctor already has that day.</summary>
    [ObservableProperty]
    private string _dayOnlyInfoText = string.Empty;

    [ObservableProperty]
    private string _summaryDayCountText = string.Empty;

    // ── section 1 · type ─────────────────────────────────────────────────────
    public string TypeHint => "مدت پیش‌فرض هر نوع به‌صورت خودکار اعمال می‌شود";

    // ── section 4 · calendar + slots ─────────────────────────────────────────
    [ObservableProperty]
    private string _calendarLabel = string.Empty;

    [ObservableProperty]
    private string _freeSlotsText = string.Empty;

    [ObservableProperty]
    private string _firstFreeText = string.Empty;

    [ObservableProperty]
    private string _firstFreeLinkText = string.Empty;

    [ObservableProperty]
    private bool _hasFirstFree;

    [ObservableProperty]
    private int _firstFreeMinutes;

    // ── section 2 · patient picker ───────────────────────────────────────────
    public string PatientSearchPlaceholder => "نام بیمار، مالک، شماره تماس یا شناسه پرونده…";

    [ObservableProperty]
    private bool _isPatientSearching;

    [ObservableProperty]
    private bool _showPatientSearchMessage = true;

    [ObservableProperty]
    private string _patientSearchMessage = "برای انتخاب بیمار، حداقل ۲ حرف از نام یا شماره تماس را وارد کنید.";

    [ObservableProperty]
    private string _patientResultCount = string.Empty;

    [ObservableProperty]
    private bool _hasPatientResults;

    [ObservableProperty]
    private bool _isPatientSearchVisible = true;

    [ObservableProperty]
    private bool _isPatientCardVisible;

    [ObservableProperty]
    private bool _hasQuery;

    /// <summary>Two-way bound to the picker's search box (mirrors the reference's <c>bkQ</c>).</summary>
    [ObservableProperty]
    private string _queryText = string.Empty;

    // Only the latest settled input reaches the database.
    private readonly Debouncer _queryDebounce = new(TimeSpan.FromMilliseconds(300));

    partial void OnQueryTextChanged(string value)
    {
        _query = value ?? string.Empty;
        HasQuery = !string.IsNullOrWhiteSpace(_query);
        QueuePatientSearch();
    }

    // selected patient card fields
    [ObservableProperty]
    private string _selectedName = string.Empty;

    [ObservableProperty]
    private string _selectedKind = string.Empty;

    [ObservableProperty]
    private string _selectedOwner = string.Empty;

    [ObservableProperty]
    private string _selectedPhone = string.Empty;

    [ObservableProperty]
    private string _selectedIconKey = "IconPaw";

    [ObservableProperty]
    private IBrush _selectedAvatarBrush = Brushes.Transparent;

    // ── section 5 · extras ───────────────────────────────────────────────────
    [ObservableProperty]
    private string _noteText = string.Empty;

    [ObservableProperty]
    private bool _sendSms = true;

    [ObservableProperty]
    private bool _sendReminder = true;

    // ── summary sidebar ──────────────────────────────────────────────────────
    [ObservableProperty]
    private bool _hasSelectedPatient;

    [ObservableProperty]
    private string _summaryPetName = string.Empty;

    [ObservableProperty]
    private string _summaryPetSub = string.Empty;

    [ObservableProperty]
    private IBrush _summaryAvatarBrush = Brushes.Transparent;

    [ObservableProperty]
    private string _summaryIconKey = "IconPaw";

    [ObservableProperty]
    private string _summaryTypeLabel = string.Empty;

    [ObservableProperty]
    private string _summaryTypeClass = "visit";

    [ObservableProperty]
    private IBrush _summaryTypeBackground = Brushes.Transparent;

    [ObservableProperty]
    private IBrush _summaryTypeForeground = Brushes.Black;

    [ObservableProperty]
    private string _summaryDoctorShort = string.Empty;

    [ObservableProperty]
    private string _summaryDateText = string.Empty;

    [ObservableProperty]
    private string _summaryTimeText = string.Empty;

    [ObservableProperty]
    private string _summaryDurationText = string.Empty;

    [ObservableProperty]
    private bool _hasConflict;

    [ObservableProperty]
    private string _conflictTitle = string.Empty;

    [ObservableProperty]
    private string _conflictSub = string.Empty;

    [ObservableProperty]
    private bool _showFreeOk;

    [ObservableProperty]
    private string _freeOkText = string.Empty;

    [ObservableProperty]
    private bool _canSubmit;

    [ObservableProperty]
    private string _submitLabel = "ثبت نوبت";

    // ── load ─────────────────────────────────────────────────────────────────
    private async Task LoadAsync()
    {
        await _readiness.WaitAsync();
        _types.Clear();
        _types.AddRange(await _catalog.GetTypesAsync());
        _doctors.Clear();
        _doctors.AddRange(await _catalog.GetDoctorsAsync());

        _type = _types.Count > 0 ? _types[0].Id : "visit";
        _dur = TypeOf(_type).DurationMinutes;
        _doc = _doctors.Count > 0 ? _doctors[0].Id : "d1";

        await ApplySettingsAsync();
        BuildKindChips();
        // A21: no availability read here - the form is not seeded yet; LoadForNew/LoadForEdit seed it and load once.
    }

    // A08: every availability read is an immutable request (doctor/date/duration/start/edit id/mode) with a
    // generation. A newer request supersedes (cancels) the older one and only the current generation may apply,
    // so rapid doctor/date/duration changes can never paint a stale slot grid. Submit stays disabled while a
    // result is pending or has failed.
    private int _availabilityGeneration;
    private CancellationTokenSource? _availabilityCts;
    private bool _availabilityPending;
    private bool _availabilityFailed;
    private bool _isSubmitting;

    private sealed record AvailabilityRequest(
        string DoctorId, DateOnly Date, int Duration, int Start, string? ExceptId, bool DayOnly,
        IReadOnlyList<string> DoctorIds);

    // A21: each availability dependency is cached against the inputs it was computed for, so a change reloads ONLY
    // what it invalidates: a start-only click re-checks conflicts; a doctor/duration/day change reloads the slot grid;
    // a day change refreshes the per-doctor tallies; a month change refreshes the (bounded) month aggregates.
    private (string Doctor, DateOnly Date, int Duration)? _slotKey;
    private DateOnly? _busyDate;
    private readonly Dictionary<DateOnly, IReadOnlyDictionary<DateOnly, int>> _monthCounts = [];
    private const int MaxCachedMonths = 6;

    /// <summary>Fetches only the invalidated availability parts into locals, then paints everything if still current.</summary>
    private async Task ReloadAvailableAsync()
    {
        var request = new AvailabilityRequest(_doc, _date, _dur, _start, _id, IsDayOnly,
            _doctors.Select(d => d.Id).ToArray());
        var generation = Interlocked.Increment(ref _availabilityGeneration);
        _availabilityCts?.Cancel();
        var cts = _availabilityCts = new CancellationTokenSource();
        _availabilityPending = true;
        _availabilityFailed = false;
        CanSubmit = false;

        try
        {
            var slotKey = (request.DoctorId, request.Date, request.Duration);
            var slots = _slotKey == slotKey
                ? _slots
                : (await _booking.GetAvailableSlotsAsync(request.DoctorId, request.Date, request.Duration)).ToList();
            cts.Token.ThrowIfCancellationRequested();

            // Clock overlaps are meaningless when booking by day only.
            var conflicts = request.DayOnly
                ? []
                : (await _booking.GetConflictsAsync(request.Date, request.DoctorId, request.Start, request.Duration,
                    request.ExceptId)).ToList();
            cts.Token.ThrowIfCancellationRequested();

            Dictionary<string, int>? busy = null;
            if (_busyDate != request.Date)
            {
                busy = new Dictionary<string, int>();
                foreach (var id in request.DoctorIds)
                {
                    busy[id] = await _booking.GetDoctorBusyCountAsync(id, request.Date);
                    cts.Token.ThrowIfCancellationRequested();
                }
            }

            var monthStart = AppointmentsCalendarMath.MonthStart(request.Date);
            if (!_monthCounts.TryGetValue(monthStart, out var counts))
            {
                var first = AppointmentsCalendarMath.WeekStart(monthStart);
                counts = await _booking.GetDayCountsAsync(first, first.AddDays(41));
                cts.Token.ThrowIfCancellationRequested();
            }

            if (generation != _availabilityGeneration)
            {
                return;
            }

            _slots = slots;
            _slotKey = slotKey;
            _conflicts = conflicts;
            if (busy is not null)
            {
                _busyByDoctor.Clear();
                foreach (var (id, count) in busy)
                {
                    _busyByDoctor[id] = count;
                }

                _busyDate = request.Date;
            }

            if (!_monthCounts.ContainsKey(monthStart))
            {
                if (_monthCounts.Count >= MaxCachedMonths)
                {
                    _monthCounts.Remove(_monthCounts.Keys.OrderBy(k => Math.Abs(k.DayNumber - monthStart.DayNumber))
                        .Last());
                }

                _monthCounts[monthStart] = counts;
            }

            _availabilityPending = false;
            RenderCalendar(counts);
            Render();
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer request; that request owns the state now.
        }
        catch (Exception ex)
        {
            if (generation == _availabilityGeneration)
            {
                _availabilityPending = false;
                _availabilityFailed = true;
                CanSubmit = false;
                _toast.Show(AppointmentErrorText.For(ex), Tone.Danger);
            }
        }
    }

    /// <summary>Reads the clinic's appointment preferences: calendar mode + default notification preferences.</summary>
    private async Task ApplySettingsAsync()
    {
        var settings = await _settings.GetAsync();
        IsDayOnly = settings.CalendarMode == CalendarMode.DayOnly;
        if (_mode == "new")
        {
            SendSms = settings.Sms.SendOnBooking;
            SendReminder = settings.Sms.SendReminder;
        }
    }

    private void BuildKindChips()
    {
        KindChips.Clear();
        var chips = new (string Id, string Label)[]
        {
            ("all", "همه"), ("dog", "سگ"), ("cat", "گربه"), ("bird", "پرنده"), ("other", "سایر"),
        };
        foreach (var c in chips)
        {
            KindChips.Add(new BookingKindChipViewModel(c.Id, c.Label, _kindFilter == c.Id));
        }
    }

    private AppointmentType TypeOf(string id) =>
        _types.FirstOrDefault(t => t.Id == id) ?? _types.FirstOrDefault() ?? new AppointmentType(id, "—", id, "#98a2b3", "#eef2f0", "#536b68", "IconPaw", 30);

    private AppointmentDoctor DocOf(string id) =>
        _doctors.FirstOrDefault(d => d.Id == id) ?? _doctors.FirstOrDefault() ?? new AppointmentDoctor(id, "—", "—", string.Empty, "—", "#98a2b3");

    // ── render orchestration (reference renderBook + painters) ────────────────
    private void Render()
    {
        IsEditMode = _mode == "edit";
        Title = IsEditMode ? "جابه‌جایی نوبت" : "ثبت نوبت جدید";
        SubmitLabel = IsEditMode ? "ذخیره زمان جدید" : "ثبت نوبت";
        RenderType();
        RenderPatient();
        RenderDoctors();
        RenderSlots();
        RenderSummary();
        RenderHead();
    }

    private void RenderHead()
    {
        var t = TypeOf(_type);
        HeadTypeClass = t.ClassKey;
        HeadTypeLabel = t.Label;
        HeadDateText = AppointmentsCalendarMath.Medium(_date);
        HeadRangeText = IsDayOnly ? "زمان به‌صورت خودکار تخصیص می‌یابد" : AppointmentsPresentation.Range(_start, _start + _dur);
    }

    // ---------------- 1 · type ----------------
    private void RenderType()
    {
        TypeOptions.Clear();
        foreach (var t in _types)
        {
            TypeOptions.Add(new BookingTypeOptionViewModel(t, t.Id == _type, t.DurationMinutes));
        }
    }

    // ---------------- 2 · patient ----------------
    /// <summary>Cancels both queued and in-flight work when the panel closes.</summary>
    public void StopPatientSearch()
    {
        _patientSearchVersion++;
        _queryDebounce.Cancel();
        _patientSearchCancellation?.Cancel();
        _patientSearchCancellation = null;
        IsPatientSearching = false;
    }

    private void QueuePatientSearch()
    {
        StopPatientSearch();
        PatientList.ReplaceAll([]);
        HasPatientResults = false;
        PatientResultCount = string.Empty;
        ShowPatientSearchMessage = true;
        var canSearch = PersianText.Normalize(_query).Length >= 2 && _picking;
        PatientSearchMessage = canSearch ? "در حال جست‌وجو…" :
            "برای انتخاب بیمار، حداقل ۲ حرف از نام یا شماره تماس را وارد کنید.";
        if (canSearch) _queryDebounce.Debounce(() => _ = SearchPatientsAsync());
    }

    private async Task SearchPatientsAsync()
    {
        var version = _patientSearchVersion;
        using var cancellation = new CancellationTokenSource();
        _patientSearchCancellation = cancellation;
        IsPatientSearching = true;
        try
        {
            await _readiness.WaitAsync(cancellation.Token);
            var rows = await _booking.SearchPatientsAsync(_query.Trim(), _kindFilter, cancellation.Token);
            if (version != _patientSearchVersion || cancellation.IsCancellationRequested) return;
            PatientList.ReplaceAll(rows.Take(PatientPageSize).Select(p =>
                new BookingPatientOptionViewModel(p, p.Id == _selectedPatient?.Id, false)));
            HasPatientResults = PatientList.Count > 0;
            PatientResultCount = AppointmentsPresentation.Fa(PatientList.Count) + " نتیجه";
            var hasMore = rows.Count > PatientPageSize;
            ShowPatientSearchMessage = !HasPatientResults || hasMore;
            PatientSearchMessage = hasMore ? "۲۰ نتیجه اول نمایش داده شد؛ برای یافتن بیمار، جست‌وجو را دقیق‌تر کنید." :
                "بیماری با این مشخصات پیدا نشد؛ نام، شماره تماس یا فیلتر گونه را بررسی کنید.";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception)
        {
            if (version != _patientSearchVersion) return;
            ShowPatientSearchMessage = true;
            PatientSearchMessage = "جست‌وجو انجام نشد. دوباره جست‌وجو کنید.";
        }
        finally
        {
            if (version == _patientSearchVersion)
            {
                IsPatientSearching = false;
                _patientSearchCancellation = null;
            }
        }
    }

    private void RenderPatient()
    {
        var selected = _selectedPatient;
        IsPatientCardVisible = !_picking && selected is not null;
        IsPatientSearchVisible = !IsPatientCardVisible;

        if (IsPatientCardVisible && selected is not null)
        {
            SelectedName = selected.Name;
            SelectedKind = selected.Kind;
            SelectedOwner = selected.OwnerName;
            SelectedPhone = selected.Phone;
            SelectedIconKey = selected.SpeciesIconKey;
            SelectedAvatarBrush = AppointmentsPresentation.AvatarBrush(selected.AvatarTone);
        }

        HasQuery = !string.IsNullOrEmpty(_query);
    }

    // ---------------- 3 · doctor ----------------
    private void RenderDoctors()
    {
        DoctorOptions.Clear();
        var freeCount = _slots.Count(s => s.IsFree);
        foreach (var d in _doctors)
        {
            DoctorOptions.Add(new BookingDoctorOptionViewModel(
                d,
                d.Id == _doc,
                _busyByDoctor.GetValueOrDefault(d.Id, 0),
                freeCount));
        }
    }

    private readonly Dictionary<string, int> _busyByDoctor = [];

    // ---------------- 4 · calendar + slots ----------------
    private void RenderCalendar(IReadOnlyDictionary<DateOnly, int> counts)
    {
        var ps = AppointmentsCalendarMath.MonthStart(_date);
        var first = AppointmentsCalendarMath.WeekStart(ps);
        var month = AppointmentsCalendarMath.Month(ps);

        CalendarDays.Clear();
        for (var i = 0; i < 42; i++)
        {
            var d = first.AddDays(i);
            var isOut = AppointmentsCalendarMath.Month(d) != month;
            if (i >= 35 && isOut)
            {
                break;
            }

            CalendarDays.Add(new BookingCalendarDayViewModel(
                d,
                isOut,
                isSelected: d == _date,
                isToday: d == DateOnly.FromDateTime(DateTime.Today),
                isFriday: AppointmentsCalendarMath.IsFriday(d),
                count: counts.GetValueOrDefault(d, 0)));
        }

        CalendarLabel = AppointmentsCalendarMath.MonthName(ps) + " " + AppointmentsCalendarMath.YearText(ps);
    }

    private void RenderSlots()
    {
        var groups = new List<BookingSlotGroupViewModel>();
        var bands = new (string Label, Func<int, bool> Match)[]
        {
            ("صبح", m => m < 12 * 60),
            ("ظهر", m => m >= 12 * 60 && m < 16 * 60),
            ("عصر", m => m >= 16 * 60 && m < 19 * 60),
            ("شب", m => m >= 19 * 60),
        };

        foreach (var (label, match) in bands)
        {
            var slots = _slots
                .Where(s => match(s.Minutes))
                .Select(s => new BookingSlotViewModel(s, s.Minutes == _start))
                .ToList();
            if (slots.Count > 0)
            {
                groups.Add(new BookingSlotGroupViewModel(label, slots));
            }
        }

        SlotGroups.Clear();
        foreach (var g in groups)
        {
            SlotGroups.Add(g);
        }

        var freeCount = _slots.Count(s => s.IsFree);
        var firstFree = _slots.FirstOrDefault(s => s.IsFree);
        FreeSlotsText = AppointmentsPresentation.Fa(freeCount) + " زمان آزاد";
        HasFirstFree = firstFree is not null;
        FirstFreeText = firstFree is not null ? AppointmentsPresentation.Time(firstFree.Minutes) : string.Empty;
        FirstFreeLinkText = firstFree is not null ? "اولین زمان آزاد " + AppointmentsPresentation.Time(firstFree.Minutes) : string.Empty;
        FirstFreeMinutes = firstFree?.Minutes ?? 0;

        DurationOptions.Clear();
        foreach (var m in DurationChoices)
        {
            DurationOptions.Add(new BookingDurationOptionViewModel(m, m == _dur));
        }
    }

    // ---------------- summary aside ----------------
    private void RenderSummary()
    {
        var t = TypeOf(_type);
        var d = DocOf(_doc);

        HasSelectedPatient = _selectedPatient is not null;
        if (_selectedPatient is { } p)
        {
            SummaryPetName = p.Name;
            SummaryPetSub = $"{p.Kind} · {p.OwnerName}";
            SummaryAvatarBrush = AppointmentsPresentation.AvatarBrush(p.AvatarTone);
            SummaryIconKey = p.SpeciesIconKey;
        }

        SummaryTypeLabel = t.Label;
        SummaryTypeClass = t.ClassKey;
        SummaryTypeBackground = AppointmentsPresentation.TypeBackground(t);
        SummaryTypeForeground = AppointmentsPresentation.TypeForeground(t);
        SummaryDoctorShort = d.ShortName;
        SummaryDateText = AppointmentsCalendarMath.DayNames[AppointmentsCalendarMath.WeekIndex(_date)] +
                          "، " + AppointmentsCalendarMath.Full(_date);
        SummaryTimeText = IsDayOnly ? "تخصیص خودکار" : AppointmentsPresentation.Range(_start, _start + _dur);
        SummaryDurationText = AppointmentsPresentation.Fa(_dur) + " دقیقه";

        var dayCount = _busyByDoctor.GetValueOrDefault(_doc, 0);
        SummaryDayCountText = AppointmentsPresentation.Fa(dayCount) + " نوبت";
        DayOnlyInfoText = dayCount == 0
            ? $"برای {d.ShortName} در این روز هنوز نوبتی ثبت نشده است."
            : $"برای {d.ShortName} در این روز {AppointmentsPresentation.Fa(dayCount)} نوبت ثبت شده است.";

        HasConflict = HasSelectedPatient && _conflicts.Count > 0;
        ShowFreeOk = HasSelectedPatient && !IsDayOnly && _conflicts.Count == 0;
        if (HasConflict)
        {
            ConflictTitle = "تداخل با " + AppointmentsPresentation.Fa(_conflicts.Count) + " نوبت";
            ConflictSub = string.Join("، ", _conflicts.Select(c =>
                $"{c.PetName} ({AppointmentsPresentation.Time(c.StartMinutes)})"));
        }


        FreeOkText = $"این بازه برای {d.ShortName} آزاد است.";
        CanSubmit = HasSelectedPatient && !_availabilityPending && !_availabilityFailed && !_isSubmitting;
    }

    // ── commands ─────────────────────────────────────────────────────────────
    [RelayCommand]
    private async Task SelectType(string? id)
    {
        if (IsEditMode || string.IsNullOrEmpty(id) || _types.All(t => t.Id != id))
        {
            return;
        }

        _type = id;
        _dur = TypeOf(id).DurationMinutes;
        await ReloadAvailableAsync();
    }

    [RelayCommand]
    private async Task SelectDoctor(string? id)
    {
        if (IsEditMode || string.IsNullOrEmpty(id) || _doctors.All(d => d.Id != id))
        {
            return;
        }

        _doc = id;
        await ReloadAvailableAsync();
    }

    [RelayCommand]
    private async Task SelectDuration(int minutes)
    {
        if (minutes <= 0)
        {
            return;
        }

        _dur = minutes;
        await ReloadAvailableAsync();
    }

    [RelayCommand]
    private async Task SelectSlot(int minutes)
    {
        _start = minutes;
        await ReloadAvailableAsync();
    }

    [RelayCommand]
    private async Task SelectDate(string? dayKey)
    {
        if (DateOnly.TryParseExact(dayKey, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var d)
            || DateOnly.TryParse(dayKey, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out d))
        {
            _date = d;
            await ReloadAvailableAsync();
        }
    }

    [RelayCommand]
    private async Task StepMonth(string? dir)
    {
        var d = AppointmentsCalendarMath.MonthStart(_date);
        _date = dir == "-1" ? d.AddDays(-1) : d.AddDays(35);
        await ReloadAvailableAsync();
    }

    [RelayCommand]
    private async Task SelectKind(string? id)
    {
        if (string.IsNullOrEmpty(id) || _kindFilter == id)
        {
            return;
        }

        _kindFilter = id;
        BuildKindChips();
        QueuePatientSearch();
        await Task.CompletedTask;
    }

    [RelayCommand]
    private void ClearQuery() => QueryText = string.Empty;

    [RelayCommand]
    private async Task SelectFirstFree()
    {
        if (HasFirstFree)
        {
            _start = FirstFreeMinutes;
            await ReloadAvailableAsync();
        }
    }

    [RelayCommand]
    private async Task GoToday()
    {
        _date = DateOnly.FromDateTime(DateTime.Today);
        await ReloadAvailableAsync();
    }

    [RelayCommand]
    private async Task GoTomorrow()
    {
        _date = DateOnly.FromDateTime(DateTime.Today).AddDays(1);
        await ReloadAvailableAsync();
    }

    [RelayCommand]
    private async Task GoNextWeek()
    {
        _date = DateOnly.FromDateTime(DateTime.Today).AddDays(7);
        await ReloadAvailableAsync();
    }

    [RelayCommand]
    private void CancelClose()
    {
        StopPatientSearch();
        _dialogs.Close();
    }

    [RelayCommand]
    private void SelectPatient(BookingPatientOptionViewModel? option)
    {
        if (option is null || IsEditMode)
        {
            return;
        }

        ApplySelectedPatient(option.Patient);
    }

    /// <summary>Makes <paramref name="patient"/> the booking's patient and repaints the card + summary.</summary>
    private void ApplySelectedPatient(BookingPatient patient)
    {
        StopPatientSearch();
        _selectedPatient = patient;
        _picking = false;
        QueryText = string.Empty;
        RenderPatient();
        RenderSummary();
    }

    [RelayCommand]
    private void ChangePatient()
    {
        if (IsEditMode)
        {
            return;
        }

        _picking = true;
        QueuePatientSearch();
        RenderPatient();
    }

    /// <summary>
    /// Opens the main patient-registration wizard on top of this panel. Only the modal layer is ever closed,
    /// so everything already chosen here (type, doctor, date, time, note, SMS switches) is preserved; the new
    /// patient is then selected automatically.
    /// </summary>
    [RelayCommand]
    private void BeginAddPatient()
    {
        if (IsEditMode)
        {
            return;
        }

        var wizard = new AddPatientWizardViewModel(
            _patients, _dialogs, result => _ = SelectCreatedPatientAsync(result), closeModalOnly: true);
        _dialogs.ShowModal(wizard, maxWidth: 760);
    }

    private async Task SelectCreatedPatientAsync(AddPatientWizardResult result)
    {
        if (result.PetId is null || await _booking.GetPatientAsync(result.PetId) is not { } patient)
        {
            _toast.Show("پرونده ساخته شد؛ بیمار را از فهرست جست‌وجو و انتخاب کنید.", Tone.Info);
            return;
        }

        ApplySelectedPatient(patient);
        _toast.Show($"پروندهٔ «{patient.Name}» ساخته و برای این نوبت انتخاب شد.", Tone.Info);
    }

    /// <summary>
    /// Day-only bookings carry no user-chosen clock time, but the backend still stores a start and a
    /// duration (and enforces working hours / double-booking). So the panel quietly assigns the doctor's
    /// first free slot of that day — the data model is unchanged. Returns null (after telling the user)
    /// when the day is full for that doctor.
    /// </summary>
    private async Task<(int Start, int Duration)?> ResolveDayOnlySlotAsync()
    {
        // Editing without changing the day keeps the appointment exactly where it already is.
        if (_mode == "edit" && _date == _originalDate)
        {
            return (_start, _dur);
        }

        var duration = _mode == "edit" ? _dur : TypeOf(_type).DurationMinutes;
        var slots = await _booking.GetAvailableSlotsAsync(_doc, _date, duration);
        var free = slots.FirstOrDefault(s => s.IsFree);
        if (free is null)
        {
            _toast.Show("ظرفیت این روز برای پزشک انتخاب‌شده تکمیل است؛ روز یا پزشک دیگری را انتخاب کنید.", Tone.Danger);
            return null;
        }

        return (free.Minutes, duration);
    }

    [RelayCommand]
    private async Task Submit()
    {
        if (_isSubmitting)
        {
            return;
        }

        if (_selectedPatient is null)
        {
            _toast.Show("لطفاً ابتدا بیمار را انتخاب کنید.", Tone.Danger);
            _picking = true;
            RenderPatient();
            return;
        }

        if (_availabilityPending || _availabilityFailed)
        {
            _toast.Show("وضعیت زمان‌های آزاد هنوز مشخص نیست؛ لحظه‌ای بعد دوباره تلاش کنید.", Tone.Info);
            return;
        }

        var p = _selectedPatient;
        _isSubmitting = true;
        CanSubmit = false;
        try
        {
            var start = _start;
            var duration = _dur;
            if (IsDayOnly)
            {
                if (await ResolveDayOnlySlotAsync() is not { } resolved)
                {
                    return;
                }

                (start, duration) = resolved;
            }

            var note = (NoteText ?? string.Empty).Trim();
            if (_mode == "edit" && _id is not null)
            {
                await _booking.UpdateAsync(_id, new UpdateAppointmentRequest(
                    _date, start, duration, _type, _doc,
                    p.Name, p.Kind, p.OwnerName, p.Phone, p.AvatarTone, p.SpeciesIconKey,
                    note, SendSms, SendReminder));
                _toast.Show("زمان جدید نوبت ذخیره شد.", Tone.Ok);
            }
            else
            {
                await _booking.CreateAsync(new NewAppointmentRequest(
                    _date, start, duration, _type, _doc,
                    p.Name, p.Kind, p.OwnerName, p.Phone, p.AvatarTone, p.SpeciesIconKey,
                    note, SendSms, SendReminder, p.Id));
                _toast.Show($"نوبت {p.Name} ثبت شد.", Tone.Ok);
            }
        }
        catch (Exception ex) when (!AppointmentErrorText.IsCancellation(ex))
        {
            if (ex is AppointmentOperationException { WriteCommitted: true })
            {
                // The write is durable: report it, close the panel and let the page re-read.
                _toast.Show(AppointmentErrorText.For(ex), Tone.Info);
                StopPatientSearch();
                _dialogs.Close();
                Saved?.Invoke();
                return;
            }

            // Business rejection / store failure: keep the form open with all input intact.
            _toast.Show(AppointmentErrorText.For(ex), Tone.Danger);
            if (ex is AppointmentOperationException { Category: "Conflict" })
            {
                // Someone else took the slot: every cached availability part is stale.
                _slotKey = null;
                _busyDate = null;
                _monthCounts.Clear();
                _ = ReloadAvailableAsync();
            }

            return;
        }
        finally
        {
            _isSubmitting = false;
            CanSubmit = HasSelectedPatient && !_availabilityPending && !_availabilityFailed;
        }

        StopPatientSearch();
        _dialogs.Close();
        Saved?.Invoke();
    }

    /// <summary>
    /// Opens the panel pre-filled for an existing appointment (the reference's
    /// <c>S.book({ edit })</c>).
    /// </summary>
    public async Task LoadForEditAsync(AppointmentEvent ev)
    {
        await _initialLoad;
        StopPatientSearch();
        _mode = "edit";
        IsEditMode = true;
        _id = ev.Id;
        _type = ev.TypeId;
        _doc = ev.DoctorId;
        _date = ev.Date;
        _start = ev.StartMinutes;
        _dur = ev.DurationMinutes;
        _originalDate = ev.Date;
        NoteText = ev.Note;
        _selectedPatient = await _booking.GetAppointmentPatientAsync(ev.Id);
        _picking = _selectedPatient is null;
        await ApplySettingsAsync();
        await ReloadAvailableAsync();
    }

    /// <summary>
    /// Opens the panel for a new booking at an optional date/slot/doctor seed
    /// (the reference's <c>S.book({ date, start, dur, doc })</c>).
    /// </summary>
    public async Task LoadForNewAsync(DateOnly? date, int? start, int? dur, string? doctorId)
    {
        await _initialLoad;
        _mode = "new";
        IsEditMode = false;
        _id = null;
        _type = _types.Count > 0 ? _types[0].Id : "visit";
        _dur = dur ?? TypeOf(_type).DurationMinutes;
        _selectedPatient = null;
        _picking = true;
        NoteText = string.Empty;
        QueryText = string.Empty;
        QueuePatientSearch();
        _kindFilter = "all";
        _date = date ?? DateOnly.FromDateTime(DateTime.Today);
        _start = start ?? 570;
        _doc = doctorId ?? (_doctors.Count > 0 ? _doctors[0].Id : "d1");
        await ApplySettingsAsync();
        BuildKindChips();
        await ReloadAvailableAsync();
    }
}
