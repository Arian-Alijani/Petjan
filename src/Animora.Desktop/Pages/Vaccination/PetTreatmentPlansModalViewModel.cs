using Animora.Contracts.Vaccination;
using Animora.Desktop.Controls;
using Animora.Desktop.Localization;
using Animora.Desktop.Pages.Appointments;
using Animora.Desktop.Pages.Patients;
using Animora.Desktop.Services;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Pages.Vaccination;

/// <summary>
/// «برنامه‌های دارو و واکسن» of one patient: the bounded plan list (newest first) with progress, plus inline edit of
/// the descriptive fields and cancel of an active plan. The dates of a plan are fixed — to change them cancel and
/// register again (stated in the UI). Every mutation is guarded: failures keep the modal open with a Persian message.
/// </summary>
public sealed partial class PetTreatmentPlansModalViewModel : ObservableObject
{
    private readonly IVaccinationService _service;
    private readonly IDialogService _dialogs;
    private readonly IToastService _toast;
    private readonly Action _onChanged;
    private readonly string _petId;

    private int _loadVersion;

    public PetTreatmentPlansModalViewModel(
        IVaccinationService service, IDialogService dialogs, IToastService toast, string petId, string petName, Action onChanged)
    {
        _service = service;
        _dialogs = dialogs;
        _toast = toast;
        _petId = petId;
        _onChanged = onChanged;
        PetName = petName;

        foreach (var kind in new[] { TreatmentKind.ClinicVisit, TreatmentKind.HomeByOwner, TreatmentKind.BoardingAdminister })
        {
            EditKinds.Add(new TreatmentKindOptionViewModel(kind));
        }

        _ = LoadAsync();
    }

    public string PetName { get; }

    public RangeObservableCollection<PetPlanRowViewModel> Plans { get; } = [];

    public List<TreatmentKindOptionViewModel> EditKinds { get; } = [];

    [ObservableProperty]
    private bool _isLoading = true;

    [ObservableProperty]
    private bool _hasPlans;

    [ObservableProperty]
    private string _loadMessage = string.Empty;

    // ── inline edit ──────────────────────────────────────────────────────────
    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private string _editTitle = string.Empty;

    [ObservableProperty]
    private string _editDosage = string.Empty;

    [ObservableProperty]
    private string _editNotes = string.Empty;

    [ObservableProperty]
    private bool _editNotify;

    [ObservableProperty]
    private bool _canEditNotify;

    [ObservableProperty]
    private string _editError = string.Empty;

    [ObservableProperty]
    private bool _hasEditError;

    [ObservableProperty]
    private bool _isSaving;

    private PetPlanRowViewModel? _editing;
    private TreatmentKind _editKind;

    partial void OnEditErrorChanged(string value) => HasEditError = value.Length > 0;

    private async Task LoadAsync()
    {
        var version = ++_loadVersion;
        IsLoading = true;
        LoadMessage = string.Empty;
        try
        {
            var rows = await _service.GetPlansForPetAsync(_petId);
            if (version != _loadVersion)
            {
                return;
            }

            Plans.ReplaceAll(rows.Select(r => new PetPlanRowViewModel(r)));
            HasPlans = Plans.Count > 0;
            LoadMessage = HasPlans ? string.Empty : "برای این بیمار هنوز دارو یا واکسنی زمان‌بندی نشده است.";
        }
        catch (InvalidOperationException ex)
        {
            if (version != _loadVersion)
            {
                return;
            }

            HasPlans = false;
            LoadMessage = PatientsPresentation.CommandFailureMessage(ex, "بارگذاری برنامه‌ها ناموفق بود؛ دوباره تلاش کنید.");
        }
        finally
        {
            if (version == _loadVersion)
            {
                IsLoading = false;
            }
        }
    }

    [RelayCommand]
    private void Close() => _dialogs.Close();

    [RelayCommand]
    private void BeginEdit(PetPlanRowViewModel? row)
    {
        if (row is null || !row.CanModify)
        {
            return;
        }

        _editing = row;
        EditTitle = row.Plan.Title;
        EditDosage = row.Plan.Dosage ?? string.Empty;
        EditNotes = row.Plan.Notes ?? string.Empty;
        EditNotify = row.Plan.NotifyOwner;
        EditError = string.Empty;
        _editKind = row.Plan.Kind;
        foreach (var option in EditKinds)
        {
            option.IsSelected = option.Kind == _editKind;
        }

        CanEditNotify = _editKind == TreatmentKind.HomeByOwner;
        IsEditing = true;
    }

    [RelayCommand]
    private void SelectEditKind(TreatmentKindOptionViewModel? option)
    {
        if (option is null)
        {
            return;
        }

        foreach (var o in EditKinds)
        {
            o.IsSelected = ReferenceEquals(o, option);
        }

        _editKind = option.Kind;
        CanEditNotify = _editKind == TreatmentKind.HomeByOwner;
    }

    [RelayCommand]
    private void CancelEdit()
    {
        _editing = null;
        IsEditing = false;
        EditError = string.Empty;
    }

    [RelayCommand]
    private async Task SaveEditAsync()
    {
        if (_editing is null || IsSaving)
        {
            return;
        }

        var title = EditTitle.Trim();
        if (title.Length == 0)
        {
            EditError = "عنوان نمی‌تواند خالی باشد.";
            return;
        }

        IsSaving = true;
        try
        {
            await _service.UpdateTreatmentPlanAsync(new TreatmentPlanUpdate(
                _editing.Plan.PlanId,
                _editKind,
                title,
                string.IsNullOrWhiteSpace(EditDosage) ? null : EditDosage.Trim(),
                string.IsNullOrWhiteSpace(EditNotes) ? null : EditNotes.Trim(),
                _editKind == TreatmentKind.HomeByOwner && EditNotify));
        }
        catch (InvalidOperationException ex)
        {
            EditError = PatientsPresentation.CommandFailureMessage(ex, "ذخیرهٔ تغییرات ناموفق بود؛ دوباره تلاش کنید.");
            return;
        }
        finally
        {
            IsSaving = false;
        }

        CancelEdit();
        _toast.Show("تغییرات برنامه ذخیره شد");
        _onChanged();
        await LoadAsync();
    }

    [RelayCommand]
    private async Task CancelPlanAsync(PetPlanRowViewModel? row)
    {
        if (row is null || !row.CanModify)
        {
            return;
        }

        try
        {
            await _service.CancelTreatmentPlanAsync(row.Plan.PlanId);
        }
        catch (InvalidOperationException ex)
        {
            _toast.Show(PatientsPresentation.CommandFailureMessage(ex, "لغو برنامه ناموفق بود؛ دوباره تلاش کنید."), Tone.Danger);
            await LoadAsync();
            return;
        }

        if (ReferenceEquals(_editing, row))
        {
            CancelEdit();
        }

        _toast.Show("برنامه لغو شد؛ روزهای انجام‌نشده از تقویم حذف شدند");
        _onChanged();
        await LoadAsync();
    }
}

/// <summary>One plan of the patient: title, kind accent, progress and the modify permissions (active plans only).</summary>
public sealed class PetPlanRowViewModel
{
    public PetPlanRowViewModel(TreatmentPlanSummary plan)
    {
        Plan = plan;
        Title = plan.Title;
        ItemTypeLabel = VaccinationPresentation.ItemTypeLabel(plan.ItemType);
        ItemTypeIconKey = VaccinationPresentation.ItemTypeIconKey(plan.ItemType);
        KindLabel = VaccinationPresentation.KindLabel(plan.Kind);
        KindIconKey = VaccinationPresentation.KindIconKey(plan.Kind);
        Brush = VaccinationPresentation.KindBrush(plan.Kind);
        Wash = VaccinationPresentation.KindWash(plan.Kind);
        ScheduleText = VaccinationPresentation.ScheduleLabel(plan.ScheduleType);
        DosageText = plan.Dosage ?? string.Empty;
        HasDosage = !string.IsNullOrWhiteSpace(plan.Dosage);
        ProgressText = $"{VaccinationPresentation.Fa(plan.DoneCount)} از {VaccinationPresentation.Fa(plan.TotalCount)} انجام شده";
        ProgressPercent = plan.TotalCount > 0 ? plan.DoneCount * 100d / plan.TotalCount : 0;
        RangeText = plan.FirstDay == plan.LastDay
            ? AppointmentsCalendarMath.Full(plan.FirstDay)
            : $"{AppointmentsCalendarMath.Medium(plan.FirstDay)} تا {AppointmentsCalendarMath.Full(plan.LastDay)}";
        NextText = plan.NextPendingDay is { } next ? $"نوبت بعدی: {AppointmentsCalendarMath.Full(next)}" : string.Empty;
        HasNext = plan.NextPendingDay is not null && plan.Status == TreatmentPlanStatus.Active;
        CanModify = plan.Status == TreatmentPlanStatus.Active;
        (StatusText, StatusTone) = plan.Status switch
        {
            TreatmentPlanStatus.Completed => ("تکمیل‌شده", Tone.Ok),
            TreatmentPlanStatus.Cancelled => ("لغوشده", Tone.Neutral),
            _ => ("فعال", Tone.Info),
        };
    }

    public TreatmentPlanSummary Plan { get; }
    public string Title { get; }
    public string ItemTypeLabel { get; }
    public string ItemTypeIconKey { get; }
    public string KindLabel { get; }
    public string KindIconKey { get; }
    public IBrush Brush { get; }
    public IBrush Wash { get; }
    public string ScheduleText { get; }
    public string DosageText { get; }
    public bool HasDosage { get; }
    public string ProgressText { get; }
    public double ProgressPercent { get; }
    public string RangeText { get; }
    public string NextText { get; }
    public bool HasNext { get; }
    public bool CanModify { get; }
    public string StatusText { get; }
    public Tone StatusTone { get; }
}
