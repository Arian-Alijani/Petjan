using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Animora.Contracts.Appointments;
using Animora.Desktop.Controls;
using Animora.Desktop.Localization;
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
    private readonly IAppointmentsQueryService _catalog;
    private readonly IToastService _toast;
    private readonly IDialogService _dialogs;

    /// <summary>Raised after a successful save so the calendar can close + refresh.</summary>
    public event Action? Saved;

    private readonly List<AppointmentDoctor> _doctors = [];
    private readonly List<AppointmentType> _types = [];
    private readonly List<BookingPatient> _patients = [];
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
    private bool _sms = true;
    private bool _remind = true;
    private string _note = string.Empty;
    private string _query = string.Empty;
    private string _kindFilter = "all";
    private int _highlightIndex;
    private bool _picking = true;
    private bool _adding;

    public BookingPanelViewModel(
        IAppointmentBookingService booking,
        IAppointmentsQueryService catalog,
        IToastService toast,
        IDialogService dialogs)
    {
        _booking = booking;
        _catalog = catalog;
        _toast = toast;
        _dialogs = dialogs;
        _date = DateOnly.FromDateTime(DateTime.Today);
        _ = LoadAsync();
    }

    // ── collections ──────────────────────────────────────────────────────────
    public ObservableCollection<BookingTypeOptionViewModel> TypeOptions { get; } = [];
    public ObservableCollection<BookingDoctorOptionViewModel> DoctorOptions { get; } = [];
    public ObservableCollection<BookingPatientOptionViewModel> PatientList { get; } = [];
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

    [ObservableProperty]
    private string _headTypeLabel = string.Empty;

    [ObservableProperty]
    private string _headDateText = string.Empty;

    [ObservableProperty]
    private string _headRangeText = string.Empty;

    [ObservableProperty]
    private string _headTypeClass = "visit";

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
    public string PatientSearchPlaceholder => "نام حیوان، مالک، نژاد یا شماره تماس…";

    [ObservableProperty]
    private string _patientResultCount = string.Empty;

    [ObservableProperty]
    private bool _hasPatientResults = true;

    [ObservableProperty]
    private bool _isPatientSearchVisible = true;

    [ObservableProperty]
    private bool _isPatientCardVisible;

    [ObservableProperty]
    private bool _isAddFormVisible;

    [ObservableProperty]
    private bool _hasQuery;

    /// <summary>Two-way bound to the picker's search box (mirrors the reference's <c>bkQ</c>).</summary>
    [ObservableProperty]
    private string _queryText = string.Empty;

    partial void OnQueryTextChanged(string value)
    {
        _query = value ?? string.Empty;
        _highlightIndex = 0;
        RenderPatient();
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

    // new-patient form fields
    [ObservableProperty]
    private string _newName = string.Empty;

    [ObservableProperty]
    private string _newKind = "سگ";

    [ObservableProperty]
    private string _newOwner = string.Empty;

    [ObservableProperty]
    private string _newPhone = string.Empty;

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
    private string _summaryPriceText = string.Empty;

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
        _types.Clear();
        _types.AddRange(await _catalog.GetTypesAsync());
        _doctors.Clear();
        _doctors.AddRange(await _catalog.GetDoctorsAsync());
        _patients.Clear();
        _patients.AddRange(await _booking.GetPatientsAsync());

        _type = _types.Count > 0 ? _types[0].Id : "visit";
        _dur = TypeOf(_type).DurationMinutes;
        _doc = _doctors.Count > 0 ? _doctors[0].Id : "d1";

        BuildKindChips();
        await ReloadAvailableAsync();
    }

    /// <summary>Fetches the doctor/day slot grid + conflicts, then paints everything.</summary>
    private async Task ReloadAvailableAsync()
    {
        _slots = (await _booking.GetAvailableSlotsAsync(_doc, _date, _dur)).ToList();
        _conflicts = (await _booking.GetConflictsAsync(_date, _doc, _start, _dur, _id)).ToList();
        _busyByDoctor.Clear();
        foreach (var d in _doctors)
        {
            _busyByDoctor[d.Id] = await _booking.GetDoctorBusyCountAsync(d.Id, _date);
        }

        await RenderCalendarAsync();
        Render();
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
        Title = _mode == "edit" ? "ویرایش نوبت" : "ثبت نوبت جدید";
        SubmitLabel = _mode == "edit" ? "ذخیره تغییرات" : "ثبت نوبت";
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
        HeadRangeText = AppointmentsPresentation.Range(_start, _start + _dur);
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
    private IEnumerable<BookingPatient> FilteredPatients()
    {
        var q = PersianText.Normalize(_query);
        return _patients.Where(p =>
        {
            if (_kindFilter != "all")
            {
                var isOther = p.SpeciesKey is not ("dog" or "cat" or "bird");
                if (_kindFilter == "other" ? !isOther : p.SpeciesKey != _kindFilter)
                {
                    return false;
                }
            }

            if (string.IsNullOrEmpty(q))
            {
                return true;
            }

            var hay = PersianText.Normalize($"{p.Name} {p.OwnerName} {p.Kind} {p.Phone}");
            return hay.Contains(q, StringComparison.Ordinal);
        });
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

        if (IsPatientSearchVisible)
        {
            var list = FilteredPatients().ToList();
            PatientList.Clear();
            for (var k = 0; k < list.Count; k++)
            {
                var p = list[k];
                PatientList.Add(new BookingPatientOptionViewModel(
                    p,
                    isSelected: selected is not null && ReferenceEquals(p, selected),
                    isHighlighted: _highlightIndex == k));
            }

            HasPatientResults = list.Count > 0;
            PatientResultCount = AppointmentsPresentation.Fa(list.Count) + " نتیجه";
        }

        IsAddFormVisible = _adding;
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
    private async Task RenderCalendarAsync()
    {
        var ps = AppointmentsCalendarMath.MonthStart(_date);
        var first = AppointmentsCalendarMath.WeekStart(ps);
        var month = AppointmentsCalendarMath.Month(ps);

        var counts = await _booking.GetDayCountsAsync(first, first.AddDays(41));

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
        SummaryTypeBackground = AppointmentsPresentation.TypeBackground(t.ClassKey);
        SummaryTypeForeground = AppointmentsPresentation.TypeForeground(t.ClassKey);
        SummaryDoctorShort = d.ShortName;
        SummaryDateText = AppointmentsCalendarMath.DayNames[AppointmentsCalendarMath.WeekIndex(_date)] +
                          "، " + AppointmentsCalendarMath.Full(_date);
        SummaryTimeText = AppointmentsPresentation.Range(_start, _start + _dur);
        SummaryDurationText = AppointmentsPresentation.Fa(_dur) + " دقیقه";
        SummaryPriceText = PersianNumberFormatter.FormatGrouped(_dur * 12000L) + " تومان";

        HasConflict = HasSelectedPatient && _conflicts.Count > 0;
        ShowFreeOk = HasSelectedPatient && _conflicts.Count == 0;
        if (HasConflict)
        {
            ConflictTitle = "تداخل با " + AppointmentsPresentation.Fa(_conflicts.Count) + " نوبت";
            ConflictSub = string.Join("، ", _conflicts.Select(c =>
                $"{c.PetName} ({AppointmentsPresentation.Time(c.StartMinutes)})"));
        }

        FreeOkText = $"این بازه برای {d.ShortName} آزاد است.";
        CanSubmit = HasSelectedPatient;
    }

    // ── commands ─────────────────────────────────────────────────────────────
    [RelayCommand]
    private async Task SelectType(string? id)
    {
        if (string.IsNullOrEmpty(id) || _types.All(t => t.Id != id))
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
        if (string.IsNullOrEmpty(id) || _doctors.All(d => d.Id != id))
        {
            return;
        }

        _doc = id;
        await ReloadAvailableAsync();
    }

    [RelayCommand]
    private async Task SelectDuration(string? minutes)
    {
        if (!int.TryParse(minutes, out var m))
        {
            return;
        }

        _dur = m;
        await ReloadAvailableAsync();
    }

    [RelayCommand]
    private async Task SelectSlot(string? minutes)
    {
        if (int.TryParse(minutes, out var m))
        {
            _start = m;
            await ReloadAvailableAsync();
        }
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
        _highlightIndex = 0;
        BuildKindChips();
        RenderPatient();
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
    private void CancelClose() => _dialogs.Close();

    [RelayCommand]
    private void SelectPatient(BookingPatientOptionViewModel? option)
    {
        if (option is null)
        {
            return;
        }

        _selectedPatient = option.Patient;
        _picking = false;
        _adding = false;
        _query = string.Empty;
        RenderPatient();
        RenderSummary();
    }

    [RelayCommand]
    private void ChangePatient()
    {
        _picking = true;
        _highlightIndex = 0;
        RenderPatient();
    }

    [RelayCommand]
    private void BeginAddPatient()
    {
        _adding = true;
        _picking = true;
        if (!string.IsNullOrEmpty(_query) && !char.IsDigit(_query[0]))
        {
            NewName = _query;
        }

        RenderPatient();
    }

    [RelayCommand]
    private void CancelAddPatient()
    {
        _adding = false;
        RenderPatient();
    }

    [RelayCommand]
    private async Task SavePatient()
    {
        if (string.IsNullOrWhiteSpace(NewName) || string.IsNullOrWhiteSpace(NewOwner))
        {
            _toast.Show("نام حیوان و نام مالک الزامی است.", Tone.Danger);
            return;
        }

        var created = await _booking.CreatePatientAsync(new NewPatientRequest(
            NewName.Trim(),
            string.IsNullOrWhiteSpace(NewKind) ? "نامشخص" : NewKind.Trim(),
            NewOwner.Trim(),
            NewPhone.Trim()));

        _patients.Clear();
        _patients.AddRange(await _booking.GetPatientsAsync());
        _selectedPatient = _patients.FirstOrDefault(p => p.Id == created.Id) ?? created;
        _adding = false;
        _picking = false;
        _query = string.Empty;
        NewName = string.Empty;
        NewOwner = string.Empty;
        NewPhone = string.Empty;
        NewKind = "سگ";
        RenderPatient();
        RenderSummary();
        _toast.Show($"پروندهٔ «{created.Name}» ساخته و برای این نوبت انتخاب شد.", Tone.Info);
    }

    [RelayCommand]
    private async Task Submit()
    {
        if (_selectedPatient is null)
        {
            _toast.Show("لطفاً ابتدا بیمار را انتخاب کنید.", Tone.Danger);
            _picking = true;
            RenderPatient();
            return;
        }

        var p = _selectedPatient;
        if (_mode == "edit" && _id is not null)
        {
            await _booking.UpdateAsync(_id, new UpdateAppointmentRequest(
                _date, _start, _dur, _type, _doc,
                p.Name, p.Kind, p.OwnerName, p.Phone, p.AvatarTone, p.SpeciesIconKey,
                _note, _sms, _remind));
            _toast.Show("تغییرات نوبت ذخیره شد.", Tone.Ok);
        }
        else
        {
            await _booking.CreateAsync(new NewAppointmentRequest(
                _date, _start, _dur, _type, _doc,
                p.Name, p.Kind, p.OwnerName, p.Phone, p.AvatarTone, p.SpeciesIconKey,
                _note, _sms, _remind));
            _toast.Show(_sms
                ? $"نوبت {p.Name} ثبت و پیامک تأیید ارسال شد."
                : $"نوبت {p.Name} ثبت شد.", Tone.Ok);
        }

        _dialogs.Close();
        Saved?.Invoke();
    }

    /// <summary>
    /// Opens the panel pre-filled for an existing appointment (the reference's
    /// <c>S.book({ edit })</c>).
    /// </summary>
    public async Task LoadForEditAsync(AppointmentEvent ev)
    {
        _mode = "edit";
        _id = ev.Id;
        _type = ev.TypeId;
        _doc = ev.DoctorId;
        _date = ev.Date;
        _start = ev.StartMinutes;
        _dur = ev.DurationMinutes;
        _note = ev.Note;
        NoteText = ev.Note;
        _selectedPatient = _patients.FirstOrDefault(p => p.Name == ev.PetName && p.OwnerName == ev.OwnerName);
        _picking = _selectedPatient is null;
        _sms = true;
        _remind = true;
        await ReloadAvailableAsync();
    }

    /// <summary>
    /// Opens the panel for a new booking at an optional date/slot/doctor seed
    /// (the reference's <c>S.book({ date, start, dur, doc })</c>).
    /// </summary>
    public async Task LoadForNewAsync(DateOnly? date, int? start, int? dur, string? doctorId)
    {
        _mode = "new";
        _id = null;
        _type = _types.Count > 0 ? _types[0].Id : "visit";
        _dur = dur ?? TypeOf(_type).DurationMinutes;
        _selectedPatient = null;
        _picking = true;
        _adding = false;
        _note = string.Empty;
        NoteText = string.Empty;
        _query = string.Empty;
        _kindFilter = "all";
        _highlightIndex = 0;
        _date = date ?? DateOnly.FromDateTime(DateTime.Today);
        _start = start ?? 570;
        _doc = doctorId ?? (_doctors.Count > 0 ? _doctors[0].Id : "d1");
        BuildKindChips();
        await ReloadAvailableAsync();
    }
}
