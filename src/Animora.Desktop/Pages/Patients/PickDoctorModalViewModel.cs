using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Animora.Contracts.Visits;
using Animora.Desktop.Localization;
using Animora.Desktop.Pages.Visits;
using Animora.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Pages.Patients;

/// <summary>
/// Backs the «ثبت ویزیت» doctor-picker sheet: a professional RTL modal that lists the
/// clinic's doctors as selectable cards (avatar initials + name + specialty + today's
/// appointment count) and, once a doctor is chosen, starts a walk-in visit for the
/// pet the caller passed in. On confirm it hands the started <see cref="EncounterDraft"/>
/// back through <c>onStarted</c> so the pet file can close its sheet and navigate into
/// the encounter wizard pre-bound to this exact pet (Task 2).
/// </summary>
public sealed partial class PickDoctorModalViewModel : ObservableObject
{
    private readonly IVisitsTriageService _triage;
    private readonly IDialogService _dialogs;
    private readonly NewVisitRequest _baseRequest;
    private readonly Func<EncounterDraft, Task> _onStarted;

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isStarting;
    [ObservableProperty] private DoctorTileViewModel? _selectedDoctor;
    [ObservableProperty] private string _errorText = string.Empty;

    public bool HasError => !string.IsNullOrEmpty(ErrorText);

    partial void OnErrorTextChanged(string value) => OnPropertyChanged(nameof(HasError));

    public PickDoctorModalViewModel(
        IVisitsTriageService triage,
        IDialogService dialogs,
        string petName,
        NewVisitRequest baseRequest,
        Func<EncounterDraft, Task> onStarted)
    {
        _triage = triage;
        _dialogs = dialogs;
        _baseRequest = baseRequest;
        _onStarted = onStarted;
        PetName = petName;
    }

    /// <summary>The patient this visit is being started for (shown in the sheet header).</summary>
    public string PetName { get; }

    public ObservableCollection<DoctorTileViewModel> Doctors { get; } = [];

    public bool HasDoctors => Doctors.Count > 0;

    public bool HasNoDoctors => !IsLoading && Doctors.Count == 0;

    public string ConfirmText => IsStarting ? "در حال آغاز…" : "شروع ویزیت";

    public bool CanConfirm => SelectedDoctor is not null && !IsStarting && !IsLoading;

    /// <summary>Loads the doctor rail once, when the sheet opens.</summary>
    public async Task LoadAsync()
    {
        IsLoading = true;
        ErrorText = string.Empty;
        try
        {
            var doctors = await _triage.GetDoctorsAsync();
            Doctors.Clear();
            foreach (var doctor in doctors)
            {
                Doctors.Add(new DoctorTileViewModel(doctor));
            }
        }
        catch (InvalidOperationException ex)
        {
            ErrorText = PatientsPresentation.CommandFailureMessage(ex, "بارگذاری فهرست پزشکان ناموفق بود؛ دوباره تلاش کنید");
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(HasDoctors));
            OnPropertyChanged(nameof(HasNoDoctors));
            OnPropertyChanged(nameof(CanConfirm));
        }
    }

    [RelayCommand]
    private void SelectDoctor(DoctorTileViewModel? tile)
    {
        if (tile is null || !Doctors.Contains(tile))
        {
            return;
        }

        foreach (var doctor in Doctors)
        {
            doctor.IsSelected = ReferenceEquals(doctor, tile);
        }

        SelectedDoctor = tile;
        OnPropertyChanged(nameof(CanConfirm));
    }

    [RelayCommand]
    private async Task ConfirmAsync()
    {
        if (SelectedDoctor is null || IsStarting)
        {
            return;
        }

        IsStarting = true;
        ErrorText = string.Empty;
        OnPropertyChanged(nameof(ConfirmText));
        OnPropertyChanged(nameof(CanConfirm));
        try
        {
            var request = _baseRequest with { DoctorId = SelectedDoctor.Doctor.Id };
            var draft = await _triage.StartNewAsync(request);
            _dialogs.Close();
            await _onStarted(draft);
        }
        catch (InvalidOperationException ex)
        {
            ErrorText = PatientsPresentation.CommandFailureMessage(ex, "شروع ویزیت ناموفق بود؛ دوباره تلاش کنید");
            IsStarting = false;
            OnPropertyChanged(nameof(ConfirmText));
            OnPropertyChanged(nameof(CanConfirm));
        }
    }

    [RelayCommand]
    private void Cancel() => _dialogs.Close();
}
