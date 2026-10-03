using System.Collections.ObjectModel;
using Animora.Contracts.Visits;
using Animora.Contracts.Diagnostics;
using Animora.Desktop.Controls;
using Animora.Desktop.Localization;
using Animora.Desktop.Pages.Patients;
using Animora.Desktop.Services;
using Animora.Desktop.Shell;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Pages.Visits;

/// <summary>
/// ViewModel for the Visits triage gate (Phase 10): the doctor rail, each
/// doctor's booked-appointment list vs. the new walk-in visit form, and the
/// transition into starting an encounter. Mirrors the reference gate screen
/// (Animora-UI/assets/visit.js renderDocRail/renderWorkspace/bookedListHtml/
/// newVisitFormHtml). Data comes only through <see cref="IVisitsTriageService"/>.
/// </summary>
public sealed partial class VisitsTriagePageViewModel : PageViewModelBase
{
    private readonly IVisitsTriageService _service;
    private readonly IStoreReadinessGate _readiness;
    private readonly IToastService _toast;
    private readonly INavigationService _navigation;

    [ObservableProperty]
    private DoctorTileViewModel? _selectedDoctor;

    [ObservableProperty]
    private VisitEntryMode _mode = VisitEntryMode.Booked;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _activeTypeFilter = "all";

    // New-visit form builder (mirrors the reference's S.nb state).
    [ObservableProperty]
    private VisitPatientRef? _pickedPatient;

    [ObservableProperty]
    private VisitTypeOptionViewModel? _selectedType;

    [ObservableProperty]
    private bool _isUrgent;

    [ObservableProperty]
    private string _noteText = string.Empty;

    [ObservableProperty]
    private string _patientSearchText = string.Empty;

    // Inline "new patient (no record)" form (mirrors the reference's #newPatForm).
    [ObservableProperty]
    private bool _isNewPatientFormOpen;

    [ObservableProperty]
    private string _newPatientName = string.Empty;

    [ObservableProperty]
    private string _newPatientKind = string.Empty;

    [ObservableProperty]
    private string _newPatientOwner = string.Empty;

    [ObservableProperty]
    private string _newPatientPhone = string.Empty;

    public VisitsTriagePageViewModel(
        IVisitsTriageService service,
        IStoreReadinessGate readiness,
        IToastService toast,
        INavigationService navigation,
        IDataChangeNotifier changes)
    {
        _service = service;
        _readiness = readiness;
        _toast = toast;
        _navigation = navigation;
        Title = "ویزیت و پرونده پزشکی";
        Eyebrow = "پرونده الکترونیک سلامت";
        Breadcrumbs.Add("مدیریت کلینیک");
        Breadcrumbs.Add("ویزیت");

        foreach (var type in _service.GetVisitTypes())
        {
            Types.Add(new VisitTypeOptionViewModel(type));
        }

        SelectType(Types.FirstOrDefault());
        // Non-blocking init (PERFORMANCE.md §4).
        UseLoadStatus(ReloadPageAsync);
        // Booked tickets and the patient directory change from the appointments and patients pages.
        RefreshOnDataChange(changes, () => _ = ReloadPageAsync(), DataDomain.Appointments, DataDomain.Patients);
        _ = ReloadPageAsync();
    }

    public ObservableCollection<DoctorTileViewModel> Doctors { get; } = [];
    public ObservableCollection<BookedRowViewModel> BookedRows { get; } = [];
    public ObservableCollection<VisitTypeOptionViewModel> Types { get; } = [];
    public ObservableCollection<VisitTypeChipViewModel> Chips { get; } = [];
    public ObservableCollection<VisitPatientRef> PatientMatches { get; } = [];

    private IReadOnlyList<BookedAppointmentRef> _allBooked = [];
    private IReadOnlyList<VisitPatientRef> _allPatients = [];

    public string DoctorCountText => PersianNumberFormatter.ToPersianDigits(Doctors.Count);
    public string TodayText => PersianNumberFormatter.ToPersianDigits(
        JalaliDate.FromDateTime(DateTime.Today).ToString("short"));
    public bool HasSelectedDoctor => SelectedDoctor is not null;
    public bool HasNoDoctor => SelectedDoctor is null;
    public bool IsBookedMode => Mode == VisitEntryMode.Booked;
    public bool IsNewVisitMode => Mode == VisitEntryMode.NewVisit;
    public bool HasBookedRows => BookedRows.Count > 0;
    public bool IsBookedListEmpty => BookedRows.Count == 0;
    public bool IsDoctorBookedEmpty => _allBooked.Count == 0;
    public bool IsAllTypeFilter => ActiveTypeFilter == "all";

    public string BookedTitle =>
        $"نوبت‌های امروز  ·  {PersianNumberFormatter.ToPersianDigits(BookedRows.Count)} از {PersianNumberFormatter.ToPersianDigits(_allBooked.Count)}";

    public string BookedCountHint =>
        $"{SelectedDoctor?.Specialty} · {PersianNumberFormatter.ToPersianDigits(_allBooked.Count)} نوبت رزرو شده برای امروز";

    public string ModeApptCountText =>
        $"{PersianNumberFormatter.ToPersianDigits(_allBooked.Count)} نوبت آمادهٔ ویزیت";

    public bool HasPickedPatient => PickedPatient is not null;
    public bool HasNoPickedPatient => PickedPatient is null;
    public bool CanStartNew => PickedPatient is not null
        && !string.IsNullOrWhiteSpace(PickedPatient.Phone)
        && !string.IsNullOrWhiteSpace(PickedPatient.OwnerName);
    public bool HasPatientMatches => PatientMatches.Count > 0;
    public bool HasPatientSearch => PatientSearchText.Trim().Length > 0;

    public string PickedPatientName => PickedPatient?.Name ?? string.Empty;
    public string PickedPatientKind => PickedPatient?.Kind ?? string.Empty;
    public string PickedPatientOwner => PickedPatient?.OwnerName is { Length: > 0 } o ? o : "—";
    public string PickedPatientPhone => PickedPatient?.Phone ?? string.Empty;
    public string PickedPatientIconKey => VisitsPresentation.PetIconKey(PickedPatient?.IconKey);
    public IBrush PickedPatientGlyph => VisitsPresentation.PatientPickedGlyph(PickedPatient?.IconKey);

    public string EmptyListTitle => IsDoctorBookedEmpty
        ? "هیچ نوبت رزرو شده‌ای برای این پزشک در امروز پیدا نشد"
        : "با این فیلتر نتیجه‌ای یافت نشد";

    public string EmptyListSubtitle => IsDoctorBookedEmpty
        ? "می‌توانید بدون نوبت هم ویزیت جدید ثبت کنید."
        : "فیلترها را تغییر دهید یا از حالت ویزیت جدید استفاده کنید.";

    /// <summary>
    /// Re-loads the doctor rail on every visit (this page is a singleton, so
    /// its constructor load fires only once). A doctor registered on the
    /// Profile page after this page was first shown must appear when the user
    /// returns, so we reload here for every visit past the first.
    /// </summary>
    public override void OnNavigatedTo()
    {
        // The constructor's initial load is still in flight on the very first visit.
        if (IsLoadInFlight)
        {
            return;
        }

        _ = ReloadPageAsync();
    }

    private Task ReloadPageAsync() => TrackLoadAsync(LoadAsync);

    private bool _isLoading;

    private async Task LoadAsync()
    {
        if (_isLoading)
        {
            return;
        }

        _isLoading = true;
        try
        {
            await _readiness.WaitAsync();
            // Fetch everything BEFORE touching the bound collection, then swap the rail in one synchronous
            // pass: there is no await between Clear and the re-add, so the UI never renders an empty rail
            // or a lost selection mid-refresh (V17, PERFORMANCE.md §5).
            var doctors = await _service.GetDoctorsAsync();
            var patients = await _service.GetPatientsAsync();
            var selectedId = SelectedDoctor?.Doctor.Id;

            var tiles = doctors.Select(d => new DoctorTileViewModel(d)).ToList();
            var restored = selectedId is null ? null : tiles.FirstOrDefault(t => t.Doctor.Id == selectedId);
            if (restored is not null)
            {
                restored.IsSelected = true;
            }

            Doctors.Clear();
            foreach (var tile in tiles)
            {
                Doctors.Add(tile);
            }

            // Preserve the current selection across a reload so a background
            // refresh does not reset the doctor the user is working with.
            if (selectedId is not null)
            {
                SelectedDoctor = restored;
            }

            _allPatients = patients;
            NotifyState();
        }
        finally
        {
            _isLoading = false;
        }
    }

    partial void OnSelectedDoctorChanged(DoctorTileViewModel? value) => NotifyState();

    partial void OnModeChanged(VisitEntryMode value) => NotifyState();

    private readonly Debouncer _searchDebounce = new(System.TimeSpan.FromMilliseconds(160));

    partial void OnSearchTextChanged(string value) => _searchDebounce.Debounce(BuildBookedRows);

    partial void OnActiveTypeFilterChanged(string value)
    {
        OnPropertyChanged(nameof(IsAllTypeFilter));
        BuildChips();
        BuildBookedRows();
    }

    partial void OnPatientSearchTextChanged(string value) => BuildPatientMatches();

    partial void OnPickedPatientChanged(VisitPatientRef? value)
    {
        OnPropertyChanged(nameof(HasPickedPatient));
        OnPropertyChanged(nameof(HasNoPickedPatient));
        OnPropertyChanged(nameof(CanStartNew));
        OnPropertyChanged(nameof(PickedPatientName));
        OnPropertyChanged(nameof(PickedPatientKind));
        OnPropertyChanged(nameof(PickedPatientOwner));
        OnPropertyChanged(nameof(PickedPatientPhone));
        OnPropertyChanged(nameof(PickedPatientIconKey));
        OnPropertyChanged(nameof(PickedPatientGlyph));
    }

    /// <summary>
    /// Runs an awaited seam call inside a single guard so a backend <see cref="Result"/> failure
    /// (surfaced by the Client.Services seam as <see cref="InvalidOperationException"/> carrying the
    /// error code) becomes a <see cref="Tone.Danger"/> toast instead of escaping onto the UI
    /// dispatcher, where it would be treated as a process crash. Mirrors Patients (V01).
    /// </summary>
    private async Task RunGuardedAsync(Func<Task> action, string fallbackMessage)
    {
        try
        {
            await action();
        }
        catch (InvalidOperationException ex)
        {
            _toast.Show(PatientsPresentation.CommandFailureMessage(ex, fallbackMessage), Tone.Danger);
        }
    }

    [RelayCommand]
    private Task SelectDoctorAsync(DoctorTileViewModel? tile) => RunGuardedAsync(async () =>
    {
        if (tile is null || ReferenceEquals(tile, SelectedDoctor))
        {
            return;
        }

        SearchText = string.Empty;
        ActiveTypeFilter = "all";
        foreach (var doctor in Doctors)
        {
            doctor.IsSelected = ReferenceEquals(doctor, tile);
        }

        SelectedDoctor = tile;
        _allBooked = await _service.GetBookedForDoctorAsync(tile.Doctor.Id);
        BuildChips();
        BuildBookedRows();
        NotifyState();
    }, "بارگذاری نوبت‌های پزشک ناموفق بود؛ دوباره تلاش کنید");

    [RelayCommand]
    private void SetMode(string? mode)
    {
        Mode = mode == "new" ? VisitEntryMode.NewVisit : VisitEntryMode.Booked;
        SearchText = string.Empty;
        ActiveTypeFilter = "all";
        BuildBookedRows();
        NotifyState();
    }

    [RelayCommand]
    private void SetTypeFilter(string? filter) => ActiveTypeFilter = filter ?? "all";

    [RelayCommand]
    private void SetType(VisitTypeOptionViewModel? option) => SelectType(option);

    [RelayCommand]
    private void SetPriority(string? priority) => IsUrgent = priority == "urgent";

    [RelayCommand]
    private void PickPatient(VisitPatientRef? patient)
    {
        PickedPatient = patient;
        PatientSearchText = string.Empty;
        PatientMatches.Clear();
        OnPropertyChanged(nameof(HasPatientMatches));
        OnPropertyChanged(nameof(HasPatientSearch));
    }

    [RelayCommand]
    private void ChangePatient()
    {
        PickedPatient = null;
        PatientSearchText = string.Empty;
    }

    [RelayCommand]
    private void RegisterNewPatient() => IsNewPatientFormOpen = !IsNewPatientFormOpen;

    [RelayCommand]
    private void SaveNewPatient()
    {
        // The backend rejects a walk-in without an owner name/phone (WalkInPhoneRequired), so block it here.
        if (string.IsNullOrWhiteSpace(NewPatientName) || string.IsNullOrWhiteSpace(NewPatientKind)
            || string.IsNullOrWhiteSpace(NewPatientOwner) || string.IsNullOrWhiteSpace(NewPatientPhone))
        {
            _toast.Show("نام، نژاد، نام صاحب و شمارهٔ تماس الزامی است.", Tone.Danger);
            return;
        }

        PickedPatient = new VisitPatientRef(
            Id: $"np-{Guid.NewGuid():N}"[..10],
            Name: NewPatientName.Trim(),
            Kind: NewPatientKind.Trim(),
            OwnerName: NewPatientOwner.Trim(),
            Phone: NewPatientPhone.Trim(),
            IconKey: "dog");

        IsNewPatientFormOpen = false;
        NewPatientName = string.Empty;
        NewPatientKind = string.Empty;
        NewPatientOwner = string.Empty;
        NewPatientPhone = string.Empty;
        PatientMatches.Clear();
        OnPropertyChanged(nameof(HasPatientMatches));
        OnPropertyChanged(nameof(HasPatientSearch));
    }

    [RelayCommand]
    private Task StartFromTicketAsync(BookedRowViewModel? row) => RunGuardedAsync(async () =>
    {
        if (row is null)
        {
            return;
        }

        var draft = await _service.StartFromTicketAsync(row.TicketId);
        _toast.Show($"ویزیت « {draft.PatientName} » آغاز شد (نمونه طراحی)", Tone.Ok);
        await _navigation.NavigateToAsync<VisitEncounterViewModel>(vm => vm.LoadAsync(draft));
    }, "شروع ویزیت از نوبت ناموفق بود؛ دوباره تلاش کنید");

    [RelayCommand]
    private Task StartNewAsync() => RunGuardedAsync(async () =>
    {
        if (SelectedDoctor is null || PickedPatient is null)
        {
            _toast.Show("اطلاعات ناقص است.", Tone.Danger);
            return;
        }

        var request = new NewVisitRequest(
            DoctorId: SelectedDoctor.Doctor.Id,
            PatientName: PickedPatient.Name,
            PatientKind: PickedPatient.Kind,
            OwnerName: PickedPatient.OwnerName,
            OwnerPhone: PickedPatient.Phone,
            PatientIconKey: PickedPatient.IconKey,
            IsNewPatient: false,
            TypeId: SelectedType?.Type.Id ?? "visit",
            IsUrgent: IsUrgent,
            Note: NoteText);

        var draft = await _service.StartNewAsync(request);
        _toast.Show($"ویزیت جدید بدون نوبت برای « {PickedPatient.Name} » آغاز شد (نمونه طراحی)", Tone.Ok);
        await _navigation.NavigateToAsync<VisitEncounterViewModel>(vm => vm.LoadAsync(draft));
    }, "شروع ویزیت جدید ناموفق بود؛ دوباره تلاش کنید");

    private void SelectType(VisitTypeOptionViewModel? option)
    {
        foreach (var type in Types)
        {
            type.IsSelected = ReferenceEquals(type, option);
        }

        SelectedType = option;
    }

    private void BuildChips()
    {
        Chips.Clear();
        Chips.Add(new VisitTypeChipViewModel(
            "all", "همه", PersianNumberFormatter.ToPersianDigits(_allBooked.Count), IsAllTypeFilter));

        foreach (var type in Types)
        {
            var count = _allBooked.Count(row => row.TypeId == type.Type.Id);
            if (count == 0)
            {
                continue;
            }

            Chips.Add(new VisitTypeChipViewModel(
                type.Type.Id,
                type.Label,
                PersianNumberFormatter.ToPersianDigits(count),
                ActiveTypeFilter == type.Type.Id));
        }
    }

    private void BuildBookedRows()
    {
        BookedRows.Clear();
        var query = PersianText.Normalize(SearchText);
        var filtered = _allBooked
            .Where(row => ActiveTypeFilter == "all" || row.TypeId == ActiveTypeFilter)
            .Where(row =>
            {
                if (query.Length == 0)
                {
                    return true;
                }

                var hay = PersianText.Normalize(
                    $"{row.PatientName} {row.OwnerName} {row.PatientKind} {row.OwnerPhone}");
                return hay.Contains(query, StringComparison.Ordinal);
            })
            .OrderBy(row => row.AtMinutes)
            .Select(row => new BookedRowViewModel(row, ResolveType(row.TypeId)));

        foreach (var row in filtered)
        {
            BookedRows.Add(row);
        }

        OnPropertyChanged(nameof(HasBookedRows));
        OnPropertyChanged(nameof(IsBookedListEmpty));
        OnPropertyChanged(nameof(IsDoctorBookedEmpty));
        OnPropertyChanged(nameof(BookedTitle));
        OnPropertyChanged(nameof(EmptyListTitle));
        OnPropertyChanged(nameof(EmptyListSubtitle));
        OnPropertyChanged(nameof(ModeApptCountText));
    }

    private void BuildPatientMatches()
    {
        PatientMatches.Clear();
        var query = PersianText.Normalize(PatientSearchText);
        if (query.Length > 0)
        {
            foreach (var patient in _allPatients
                         .Where(p => PersianText.Normalize(
                                 $"{p.Name} {p.OwnerName} {p.Kind} {p.Phone}")
                             .Contains(query, StringComparison.Ordinal))
                         .Take(6))
            {
                PatientMatches.Add(patient);
            }
        }

        OnPropertyChanged(nameof(HasPatientMatches));
        OnPropertyChanged(nameof(HasPatientSearch));
    }

    private VisitTypeInfo ResolveType(string typeId) =>
        Types.FirstOrDefault(t => t.Type.Id == typeId)?.Type
        ?? Types.FirstOrDefault()?.Type
        ?? new VisitTypeInfo("visit", "ویزیت عمومی", "IconBriefcase", "#0d9488", 30);

    private void NotifyState()
    {
        OnPropertyChanged(nameof(HasSelectedDoctor));
        OnPropertyChanged(nameof(HasNoDoctor));
        OnPropertyChanged(nameof(IsBookedMode));
        OnPropertyChanged(nameof(IsNewVisitMode));
        OnPropertyChanged(nameof(DoctorCountText));
        OnPropertyChanged(nameof(TodayText));
        OnPropertyChanged(nameof(BookedCountHint));
    }
}
