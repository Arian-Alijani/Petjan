using System.Collections.ObjectModel;
using Animora.Contracts.Appointments;
using Animora.Contracts.Vaccination;
using Animora.Desktop.Controls;
using Animora.Desktop.Localization;
using Animora.Desktop.Pages.Appointments;
using Animora.Desktop.Services;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Pages.Vaccination;

/// <summary>
/// The «ثبت واکسن جدید» modal, mirroring the reference's <c>openVaxModal</c>
/// (Animora-UI/assets/vaccination.js): the patient picker, the vaccine
/// type / dose / dates / vet / lot fields and the send-confirmation toggle.
/// The patient picker follows the appointment booking pattern (server-side
/// search over <see cref="IAppointmentBookingService.SearchPatientsAsync"/>: no
/// eager list, minimum two characters, debounced, version-guarded, bounded to
/// 20 rows, species chips and a selected-patient card with owner + phone).
/// The submit stays a mock that shows the reference's confirmation toast
/// (per the phase file's out-of-scope note) but now requires a selected patient.
/// </summary>
public sealed partial class AddVaccineModalViewModel : ObservableObject
{
    private readonly IDialogService _dialogs;
    private readonly IToastService _toast;
    private readonly IAppointmentBookingService _booking;

    private const int MinQueryLength = 2;
    private const int PatientPageSize = 20;
    private const string HintMessage = "برای انتخاب بیمار، حداقل ۲ حرف از نام یا شماره تماس را وارد کنید.";

    // Only the latest settled input reaches the database.
    private readonly Debouncer _queryDebounce = new(TimeSpan.FromMilliseconds(300));
    private int _patientSearchVersion;
    private CancellationTokenSource? _patientSearchCancellation;
    private string _kindFilter = "all";
    private BookingPatient? _selectedPatient;

    public AddVaccineModalViewModel(
        IReadOnlyList<VaccineCatalogEntry> catalog,
        IDialogService dialogs,
        IToastService toast,
        IAppointmentBookingService booking)
    {
        _dialogs = dialogs;
        _toast = toast;
        _booking = booking;
        Catalog = catalog;
        SelectedVaccine = catalog.Count > 0 ? catalog[0] : null;
        BuildKindChips();
    }

    public IReadOnlyList<VaccineCatalogEntry> Catalog { get; }

    public IReadOnlyList<string> Doses { get; } = ["نوبت اول", "یادآور اول", "یادآور دوم", "نوبت سالانه"];

    public IReadOnlyList<string> Vets { get; } = ["دکتر آریا نیک", "دکتر سمانه راد"];

    // ── patient picker (booking pattern) ────────────────────────────────────
    public RangeObservableCollection<BookingPatientOptionViewModel> PatientList { get; } = [];

    public ObservableCollection<BookingKindChipViewModel> KindChips { get; } = [];

    public string PatientSearchPlaceholder => "نام بیمار، مالک یا شماره تماس…";

    [ObservableProperty]
    private string _queryText = string.Empty;

    [ObservableProperty]
    private bool _hasQuery;

    [ObservableProperty]
    private bool _isPatientSearching;

    [ObservableProperty]
    private bool _hasPatientResults;

    [ObservableProperty]
    private string _patientResultCount = string.Empty;

    [ObservableProperty]
    private bool _showPatientSearchMessage = true;

    [ObservableProperty]
    private string _patientSearchMessage = HintMessage;

    [ObservableProperty]
    private bool _isPatientSearchVisible = true;

    [ObservableProperty]
    private bool _isPatientCardVisible;

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
    private IBrush _selectedAvatarBrush = AppointmentsPresentation.AvatarBrush(null);

    partial void OnQueryTextChanged(string value)
    {
        HasQuery = !string.IsNullOrWhiteSpace(value);
        QueuePatientSearch();
    }

    /// <summary>Cancels queued and in-flight lookups (also called when the modal closes).</summary>
    private void StopPatientSearch()
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
        var canSearch = PersianText.Normalize(QueryText ?? string.Empty).Length >= MinQueryLength;
        PatientSearchMessage = canSearch ? "در حال جست‌وجو…" : HintMessage;
        if (canSearch)
        {
            _queryDebounce.Debounce(() => _ = SearchPatientsAsync());
        }
    }

    private async Task SearchPatientsAsync()
    {
        var version = _patientSearchVersion;
        using var cancellation = new CancellationTokenSource();
        _patientSearchCancellation = cancellation;
        IsPatientSearching = true;
        try
        {
            var rows = await _booking.SearchPatientsAsync(QueryText.Trim(), _kindFilter, cancellation.Token);
            if (version != _patientSearchVersion || cancellation.IsCancellationRequested)
            {
                return;
            }

            PatientList.ReplaceAll(rows.Take(PatientPageSize).Select(p =>
                new BookingPatientOptionViewModel(p, p.Id == _selectedPatient?.Id, false)));
            HasPatientResults = PatientList.Count > 0;
            PatientResultCount = AppointmentsPresentation.Fa(PatientList.Count) + " نتیجه";
            var hasMore = rows.Count > PatientPageSize;
            ShowPatientSearchMessage = !HasPatientResults || hasMore;
            PatientSearchMessage = hasMore
                ? "۲۰ نتیجه اول نمایش داده شد؛ برای یافتن بیمار، جست‌وجو را دقیق‌تر کنید."
                : "بیماری با این مشخصات پیدا نشد؛ نام، شماره تماس یا فیلتر گونه را بررسی کنید.";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            if (version != _patientSearchVersion)
            {
                return;
            }

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

    private void BuildKindChips()
    {
        KindChips.Clear();
        var chips = new (string Id, string Label)[]
        {
            ("all", "همه"), ("dog", "سگ"), ("cat", "گربه"), ("bird", "پرنده"), ("other", "سایر"),
        };
        foreach (var chip in chips)
        {
            KindChips.Add(new BookingKindChipViewModel(chip.Id, chip.Label, _kindFilter == chip.Id));
        }
    }

    [RelayCommand]
    private void SelectKind(string? id)
    {
        if (string.IsNullOrEmpty(id) || _kindFilter == id)
        {
            return;
        }

        _kindFilter = id;
        BuildKindChips();
        QueuePatientSearch();
    }

    [RelayCommand]
    private void ClearQuery() => QueryText = string.Empty;

    [RelayCommand]
    private void SelectPatient(BookingPatientOptionViewModel? option)
    {
        if (option is null)
        {
            return;
        }

        StopPatientSearch();
        var patient = option.Patient;
        _selectedPatient = patient;
        SelectedName = patient.Name;
        SelectedKind = patient.Kind;
        SelectedOwner = patient.OwnerName;
        SelectedPhone = patient.Phone;
        SelectedIconKey = patient.SpeciesIconKey;
        SelectedAvatarBrush = AppointmentsPresentation.AvatarBrush(patient.AvatarTone);
        IsPatientCardVisible = true;
        IsPatientSearchVisible = false;
    }

    [RelayCommand]
    private void ChangePatient()
    {
        _selectedPatient = null;
        IsPatientCardVisible = false;
        IsPatientSearchVisible = true;
        if (QueryText.Length == 0)
        {
            QueuePatientSearch();
        }
        else
        {
            QueryText = string.Empty;
        }
    }

    [ObservableProperty]
    private VaccineCatalogEntry? _selectedVaccine;

    [ObservableProperty]
    private string _selectedDose = "نوبت اول";

    [ObservableProperty]
    private string _injectionDate = string.Empty;

    [ObservableProperty]
    private string _nextDueDate = string.Empty;

    [ObservableProperty]
    private string _selectedVet = "دکتر آریا نیک";

    [ObservableProperty]
    private string _lot = string.Empty;

    [ObservableProperty]
    private bool _sendConfirmation = true;

    [RelayCommand]
    private void Cancel()
    {
        StopPatientSearch();
        _dialogs.Close();
    }

    [RelayCommand]
    private void Submit()
    {
        if (_selectedPatient is null)
        {
            _toast.Show("ابتدا بیمار را انتخاب کنید", Tone.Danger);
            return;
        }

        StopPatientSearch();
        _dialogs.Close();
        _toast.Show("واکسن ثبت شد (نمونه طراحی)");
    }
}
