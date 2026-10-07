using Animora.Contracts.Vaccination;
using Animora.Desktop.Controls;
using Animora.Desktop.Pages.Appointments;
using Animora.Desktop.Pages.Patients;
using Animora.Desktop.Services;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Pages.Vaccination;

/// <summary>
/// The «جزئیات دارو / واکسن» popup of one scheduled treatment day — shared by the vaccination calendar and the main
/// appointments calendar. Shows the day (patient, item, kind, dosage, state) and, once loaded, its plan (schedule,
/// progress, next day, notes). Actions: mark the day done, edit the plan (opens the patient's plan editor on it) and,
/// when the host supplies it, open the patient file. Every mutation is guarded; failures keep the popup open.
/// </summary>
public sealed partial class TreatmentOccurrenceDetailViewModel : ObservableObject
{
    private readonly IVaccinationService _service;
    private readonly IDialogService _dialogs;
    private readonly IToastService _toast;
    private readonly Action _onChanged;
    private readonly Func<string, Task>? _openPetFile;

    public TreatmentOccurrenceDetailViewModel(
        IVaccinationService service,
        IDialogService dialogs,
        IToastService toast,
        CalendarOccurrence occurrence,
        Action onChanged,
        Func<string, Task>? openPetFile = null)
    {
        _service = service;
        _dialogs = dialogs;
        _toast = toast;
        _onChanged = onChanged;
        _openPetFile = openPetFile;
        Occurrence = occurrence;

        Title = occurrence.Title;
        PetName = occurrence.PetName;
        OwnerName = occurrence.OwnerName;
        SpeciesText = VaccinationPresentation.SpeciesLabel(occurrence.Species.ToLowerInvariant());
        ItemTypeLabel = VaccinationPresentation.ItemTypeLabel(occurrence.ItemType);
        ItemTypeIconKey = VaccinationPresentation.ItemTypeIconKey(occurrence.ItemType);
        KindLabel = VaccinationPresentation.KindLabel(occurrence.Kind);
        KindIconKey = VaccinationPresentation.KindIconKey(occurrence.Kind);
        Brush = VaccinationPresentation.KindBrush(occurrence.Kind);
        Wash = VaccinationPresentation.KindWash(occurrence.Kind);
        DayText = AppointmentsCalendarMath.Full(occurrence.Day);
        DosageText = string.IsNullOrWhiteSpace(occurrence.Dosage) ? "—" : occurrence.Dosage;
        ShowsOwnerReminder = occurrence.NotifyOwner && occurrence.Kind == TreatmentKind.HomeByOwner;
        CanOpenPetFile = openPetFile is not null;
        IsDone = occurrence.State == TreatmentOccurrenceState.Done;

        _ = LoadPlanAsync();
    }

    public CalendarOccurrence Occurrence { get; }
    public string Title { get; }
    public string PetName { get; }
    public string OwnerName { get; }
    public string SpeciesText { get; }
    public string ItemTypeLabel { get; }
    public string ItemTypeIconKey { get; }
    public string KindLabel { get; }
    public string KindIconKey { get; }
    public IBrush Brush { get; }
    public IBrush Wash { get; }
    public string DayText { get; }
    public string DosageText { get; }
    public bool ShowsOwnerReminder { get; }
    public bool CanOpenPetFile { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanMarkDone), nameof(StateText), nameof(StateTone))]
    private bool _isDone;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanMarkDone))]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isPlanLoaded;

    [ObservableProperty]
    private bool _canEditPlan;

    [ObservableProperty]
    private string _scheduleText = "—";

    [ObservableProperty]
    private string _rangeText = "—";

    [ObservableProperty]
    private string _progressText = "—";

    [ObservableProperty]
    private double _progressPercent;

    [ObservableProperty]
    private string _nextText = "—";

    [ObservableProperty]
    private string _planStatusText = string.Empty;

    [ObservableProperty]
    private Tone _planStatusTone = Tone.Neutral;

    [ObservableProperty]
    private string _notesText = string.Empty;

    [ObservableProperty]
    private bool _hasNotes;

    [ObservableProperty]
    private string _vaccineCodeText = string.Empty;

    [ObservableProperty]
    private bool _hasVaccineCode;

    /// <summary>«انجام شد» is offered only while the day is pending and its plan is still active.</summary>
    public bool CanMarkDone => !IsDone && !IsBusy && _planActive;

    public string StateText => IsDone ? "انجام‌شده" : "در انتظار انجام";

    public Tone StateTone => IsDone ? Tone.Ok : Tone.Warn;

    private bool _planActive = true;

    private TreatmentPlanSummary? _plan;

    private async Task LoadPlanAsync()
    {
        try
        {
            var plans = await _service.GetPlansForPetAsync(Occurrence.PetId);
            _plan = plans.FirstOrDefault(p => p.PlanId == Occurrence.PlanId);
        }
        catch (InvalidOperationException)
        {
            _plan = null;
        }

        if (_plan is null)
        {
            return;
        }

        var row = new PetPlanRowViewModel(_plan);
        ScheduleText = row.ScheduleText;
        RangeText = row.RangeText;
        ProgressText = row.ProgressText;
        ProgressPercent = row.ProgressPercent;
        NextText = _plan.NextPendingDay is { } next ? AppointmentsCalendarMath.Full(next) : "—";
        PlanStatusText = row.StatusText;
        PlanStatusTone = row.StatusTone;
        NotesText = _plan.Notes ?? string.Empty;
        HasNotes = !string.IsNullOrWhiteSpace(_plan.Notes);
        VaccineCodeText = _plan.VaccineCode ?? string.Empty;
        HasVaccineCode = !string.IsNullOrWhiteSpace(_plan.VaccineCode);
        _planActive = _plan.Status == TreatmentPlanStatus.Active;
        CanEditPlan = row.CanModify;
        IsPlanLoaded = true;
        OnPropertyChanged(nameof(CanMarkDone));
    }

    [RelayCommand]
    private void Close() => _dialogs.Close();

    [RelayCommand]
    private async Task MarkDoneAsync()
    {
        if (!CanMarkDone)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await _service.MarkOccurrenceDoneAsync(Occurrence.OccurrenceId);
        }
        catch (InvalidOperationException ex)
        {
            _toast.Show(PatientsPresentation.CommandFailureMessage(ex, "ثبت انجام ناموفق بود؛ دوباره تلاش کنید."), Tone.Danger);
            return;
        }
        finally
        {
            IsBusy = false;
        }

        IsDone = true;
        _toast.Show("این روز به‌عنوان انجام‌شده ثبت شد");
        _onChanged();
        await LoadPlanAsync();
    }

    /// <summary>Swaps this popup for the patient's plan editor, opened directly on this plan.</summary>
    [RelayCommand]
    private void EditPlan()
    {
        _dialogs.Close();
        _dialogs.ShowModal(
            new PetTreatmentPlansModalViewModel(
                _service, _dialogs, _toast, Occurrence.PetId, Occurrence.PetName, _onChanged, Occurrence.PlanId),
            maxWidth: 600);
    }

    [RelayCommand]
    private async Task OpenPetFileAsync()
    {
        if (_openPetFile is null)
        {
            return;
        }

        _dialogs.Close();
        await _openPetFile(Occurrence.PetId);
    }
}
