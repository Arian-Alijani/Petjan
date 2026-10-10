using System.Collections.ObjectModel;
using Animora.Contracts.Patients;
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
    private readonly IPatientsQueryService _patients;
    private readonly IDialogService _dialogs;
    private readonly IDataChangeNotifier _changes;

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

    public VisitsTriagePageViewModel(
        IVisitsTriageService service,
        IStoreReadinessGate readiness,
        IToastService toast,
        INavigationService navigation,
        IDataChangeNotifier changes,
        IPatientsQueryService patients,
        IDialogService dialogs)
    {
        _service = service;
        _readiness = readiness;
        _toast = toast;
        _navigation = navigation;
        _patients = patients;
        _dialogs = dialogs;
        _changes = changes;
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

    [ObservableProperty]
    private bool _isPatientSearching;

    [ObservableProperty]
    private string _patientResultCount = string.Empty;

    [ObservableProperty]
    private bool _showPatientSearchMessage = true;

    [ObservableProperty]
    private string _patientSearchMessage = PatientHintMessage;

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
            var visitTypes = await _service.GetVisitTypesAsync();
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

            MergeVisitTypes(visitTypes);
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

    partial void OnPatientSearchTextChanged(string value) => QueuePatientSearch();

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
        StopPatientSearch();
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

    /// <summary>
    /// Opens the standard add-owner/add-pet wizard — the exact same modal the Patients page launches
    /// (<see cref="AddPatientWizardViewModel"/>, 760-wide) — instead of the old inline «بدون سابقه» form.
    /// The wizard is opened in nested mode (<c>closeModalOnly: true</c>): it persists a real owner/pet
    /// record and, on success, hands back the created ids so this page can immediately pick that patient
    /// for the new visit (no re-search needed).
    /// </summary>
    [RelayCommand]
    private void RegisterNewPatient() =>
        _dialogs.ShowModal(
            new AddPatientWizardViewModel(
                _patients,
                _dialogs,
                result => _ = OnPatientCreatedAsync(result),
                closeModalOnly: true,
                _toast),
            maxWidth: 760);

    /// <summary>
    /// Wizard-completion handler: fetches the freshly-created pet (and its owner) and selects it as the
    /// visit's patient, then fans the change out to the directory-backed pages so their lists refresh.
    /// </summary>
    private async Task OnPatientCreatedAsync(AddPatientWizardResult result)
    {
        // A new patient directory entry affects the dashboard, appointments and this page's own patient list.
        _changes.Notify(DataDomain.Dashboard, DataDomain.Appointments, DataDomain.Patients, DataDomain.Visits);

        await RunGuardedAsync(async () =>
        {
            if (string.IsNullOrEmpty(result.PetId))
            {
                return;
            }

            var pet = await _patients.GetPetAsync(result.PetId);
            if (pet is null)
            {
                return;
            }

            var owner = await _patients.GetOwnerAsync(result.OwnerId);
            var ownerName = owner is null ? string.Empty : $"{owner.FirstName} {owner.LastName}".Trim();
            var ownerPhone = owner?.Mobile1 ?? string.Empty;
            var kind = string.IsNullOrWhiteSpace(pet.Breed) ? pet.Species.ToString() : pet.Breed.Trim();

            PickedPatient = new VisitPatientRef(
                Id: pet.Id,
                Name: pet.Name,
                Kind: kind,
                OwnerName: ownerName,
                Phone: ownerPhone,
                IconKey: VisitsPresentation.IconKeyForKind(kind));

            PatientSearchText = string.Empty;
            PatientMatches.Clear();
            OnPropertyChanged(nameof(HasPatientMatches));
            OnPropertyChanged(nameof(HasPatientSearch));
            _toast.Show($"بیمار « {pet.Name} » ثبت و برای ویزیت انتخاب شد.", Tone.Ok);
        }, "ثبت بیمار جدید انجام شد اما انتخاب خودکار ناموفق بود؛ از جستجو انتخاب کنید");
    }

    [RelayCommand]
    private Task StartFromTicketAsync(BookedRowViewModel? row) => RunGuardedAsync(async () =>
    {
        if (row is null)
        {
            return;
        }

        var draft = await _service.StartFromTicketAsync(row.TicketId);
        _toast.Show($"ویزیت « {draft.PatientName} » آغاز شد.", Tone.Ok);
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
        _toast.Show($"ویزیت جدید بدون نوبت برای « {PickedPatient.Name} » آغاز شد.", Tone.Ok);
        await _navigation.NavigateToAsync<VisitEncounterViewModel>(vm => vm.LoadAsync(draft));
    }, "شروع ویزیت جدید ناموفق بود؛ دوباره تلاش کنید");

    /// <summary>Adds custom types created in Settings → Appointments; built-ins already present are kept.</summary>
    private void MergeVisitTypes(IReadOnlyList<VisitTypeInfo> latest)
    {
        var changed = false;
        foreach (var type in latest)
        {
            if (Types.Any(t => t.Type.Id == type.Id))
            {
                continue;
            }

            Types.Add(new VisitTypeOptionViewModel(type));
            changed = true;
        }

        for (var i = Types.Count - 1; i >= 0; i--)
        {
            if (latest.All(t => t.Id != Types[i].Type.Id))
            {
                // A custom type removed from settings disappears from the form/filter (unless selected).
                if (ReferenceEquals(Types[i], SelectedType))
                {
                    continue;
                }

                Types.RemoveAt(i);
                changed = true;
            }
        }

        if (changed)
        {
            BuildChips();
        }
    }

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

    // ── server-side patient search (booking pattern: no eager load, min 2 chars, debounced, version-guarded) ──
    private const int PatientMinQueryLength = 2;
    private const int PatientPageSize = 20;
    private const string PatientHintMessage = "برای انتخاب بیمار، حداقل ۲ حرف از نام بیمار، سرپرست یا شماره تماس را وارد کنید.";

    private readonly Debouncer _patientSearchDebounce = new(System.TimeSpan.FromMilliseconds(300));
    private int _patientSearchVersion;

    private void ResetPatientResults(string message)
    {
        PatientMatches.Clear();
        PatientResultCount = string.Empty;
        ShowPatientSearchMessage = true;
        PatientSearchMessage = message;
        OnPropertyChanged(nameof(HasPatientMatches));
        OnPropertyChanged(nameof(HasPatientSearch));
    }

    /// <summary>Invalidates queued and in-flight lookups so a slow earlier response never overwrites newer rows.</summary>
    private void StopPatientSearch()
    {
        _patientSearchVersion++;
        _patientSearchDebounce.Cancel();
        IsPatientSearching = false;
    }

    private void QueuePatientSearch()
    {
        StopPatientSearch();
        var canSearch = PersianText.Normalize(PatientSearchText).Length >= PatientMinQueryLength;
        ResetPatientResults(canSearch ? "در حال جست‌وجو…" : PatientHintMessage);
        if (canSearch)
        {
            _patientSearchDebounce.Debounce(() => _ = SearchPatientsAsync());
        }
    }

    private async Task SearchPatientsAsync()
    {
        var version = _patientSearchVersion;
        var term = PatientSearchText.Trim();
        IsPatientSearching = true;
        try
        {
            await _readiness.WaitAsync();
            var rows = await _service.GetPatientsAsync(term);
            if (version != _patientSearchVersion)
            {
                return;
            }

            PatientMatches.Clear();
            foreach (var patient in rows.Take(PatientPageSize))
            {
                PatientMatches.Add(patient);
            }

            var hasMore = rows.Count > PatientPageSize;
            PatientResultCount = PersianNumberFormatter.ToPersianDigits(PatientMatches.Count) + " نتیجه";
            ShowPatientSearchMessage = PatientMatches.Count == 0 || hasMore;
            PatientSearchMessage = hasMore
                ? "۲۰ نتیجه اول نمایش داده شد؛ برای یافتن بیمار، جست‌وجو را دقیق‌تر کنید."
                : "بیماری با این مشخصات پیدا نشد؛ نام بیمار، سرپرست یا شماره تماس را بررسی کنید.";
            OnPropertyChanged(nameof(HasPatientMatches));
        }
        catch (Exception)
        {
            if (version != _patientSearchVersion)
            {
                return;
            }

            ResetPatientResults("جست‌وجو انجام نشد. دوباره جست‌وجو کنید.");
        }
        finally
        {
            if (version == _patientSearchVersion)
            {
                IsPatientSearching = false;
            }
        }
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
