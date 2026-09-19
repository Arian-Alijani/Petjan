using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Animora.Contracts.Visits;
using Animora.Desktop.Controls;
using Animora.Desktop.Localization;
using Animora.Desktop.Services;
using Animora.Desktop.Shell;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Pages.Visits;

/// <summary>
/// The wizard's six visible stages, mirroring the reference's scene swap
/// (Animora-UI/assets/visit.js <c>renderScene</c>: wizard steps 0..4 then the
/// separate done scene).
/// </summary>
public enum EncounterStep
{
    Vitals,
    History,
    Plan,
    Rx,
    Review,
    Done,
}

/// <summary>
/// ViewModel for the clinical-encounter wizard (Phase 11) that Phase 10 hands
/// off into: vitals → history → plan → services/rx → review → done, ending in
/// a hand-off to Sale/POS. Mirrors the reference's <c>renderWizard</c>
/// (ribbon/stepper/body/foot + patient sidebar) and its step renderers
/// (<c>renderVitals</c>/<c>renderHistory</c>/<c>renderPlan</c>/<c>renderRx</c>/
/// <c>renderReview</c>/<c>renderDone</c>). Data comes only through
/// <see cref="IVisitEncounterService"/>.
/// </summary>
public sealed partial class VisitEncounterViewModel : PageViewModelBase
{
    /// <summary>The reference's <c>STEPS</c> array — key, full label, short label.</summary>
    internal static readonly (string Key, string Label, string Short)[] StepDefinitions =
    [
        ("vitals", "علائم حیاتی", "علائم حیاتی"),
        ("history", "شرح‌حال و معاینه", "شرح‌حال"),
        ("plan", "تشخیص و طرح درمان", "تشخیص"),
        ("rx", "خدمات و نسخه", "خدمات و نسخه"),
        ("review", "صدور فاکتور", "فاکتور"),
    ];

    // Reference renderReview «مالیات (۹٪)» — the single 9% tax line.
    private const decimal TaxRate = 0.09m;

    private readonly IVisitEncounterService _service;
    private readonly INavigationService _navigation;
    private readonly IToastService _toast;

    private EncounterDraft _draft = EmptyDraft();
    private string _encounterId = string.Empty;

    [ObservableProperty]
    private EncounterStep _currentStep = EncounterStep.Vitals;

    // ---- Step 1: vitals (reference renderVitals) ----
    [ObservableProperty]
    private string _weightText = string.Empty;

    [ObservableProperty]
    private string _temperatureText = string.Empty;

    [ObservableProperty]
    private string _pulseText = string.Empty;

    [ObservableProperty]
    private string _respirationText = string.Empty;

    [ObservableProperty]
    private string _vitalsNoteText = string.Empty;

    // ---- Step 2: history & exam (reference renderHistory) ----
    [ObservableProperty]
    private string _historyText = string.Empty;

    [ObservableProperty]
    private string _examText = string.Empty;

    // ---- Step 3: diagnosis & plan (reference renderPlan) ----
    [ObservableProperty]
    private string _diagnosisText = string.Empty;

    [ObservableProperty]
    private string _planText = string.Empty;

    // ---- Step 4: rx add-pickers (reference renderRx) ----
    [ObservableProperty]
    private RxCatalogOptionViewModel? _selectedServiceToAdd;

    [ObservableProperty]
    private RxCatalogOptionViewModel? _selectedItemToAdd;

    // ---- Step 6: done summary (reference renderDone) ----
    [ObservableProperty]
    private string _donePatientName = string.Empty;

    [ObservableProperty]
    private string _doneDoctorShort = string.Empty;

    [ObservableProperty]
    private string _doneTotalText = string.Empty;

    private long _subtotal;

    public VisitEncounterViewModel(
        IVisitEncounterService service,
        INavigationService navigation,
        IToastService toast)
    {
        _service = service;
        _navigation = navigation;
        _toast = toast;

        Title = "ویزیت و پرونده پزشکی";
        Eyebrow = "پرونده الکترونیک سلامت";
        Breadcrumbs.Add("مدیریت کلینیک");
        Breadcrumbs.Add("ویزیت");

        for (var i = 0; i < StepDefinitions.Length; i++)
        {
            Steps.Add(new EncounterStepViewModel(i, StepDefinitions[i].Short));
        }
    }

    // ---- collections ----
    public ObservableCollection<EncounterStepViewModel> Steps { get; } = [];

    public ObservableCollection<RxCatalogOptionViewModel> ServiceCatalog { get; } = [];

    public ObservableCollection<RxCatalogOptionViewModel> ItemCatalog { get; } = [];

    public ObservableCollection<RxLineViewModel> ServiceLines { get; } = [];

    public ObservableCollection<RxLineViewModel> ItemLines { get; } = [];

    // ---- step visibility ----
    public bool IsVitalsStep => CurrentStep == EncounterStep.Vitals;
    public bool IsHistoryStep => CurrentStep == EncounterStep.History;
    public bool IsPlanStep => CurrentStep == EncounterStep.Plan;
    public bool IsRxStep => CurrentStep == EncounterStep.Rx;
    public bool IsReviewStep => CurrentStep == EncounterStep.Review;
    public bool IsDoneStep => CurrentStep == EncounterStep.Done;
    public bool IsWorkflowVisible => CurrentStep != EncounterStep.Done;
    public bool IsFirstStep => CurrentStep == EncounterStep.Vitals;
    public bool IsLastStep => CurrentStep == EncounterStep.Review;
    public bool CanGoPrevious => CurrentStep != EncounterStep.Vitals;

    public string StepInfoText =>
        $"مرحلهٔ {PersianNumberFormatter.ToPersianDigits((int)CurrentStep + 1)} از "
        + $"{PersianNumberFormatter.ToPersianDigits(StepDefinitions.Length)} · "
        + StepDefinitions[Math.Min((int)CurrentStep, StepDefinitions.Length - 1)].Label;

    /// <summary>The foot's primary-button caption (reference renderFoot).</summary>
    public string NextButtonText => IsLastStep ? "صدور فاکتور و پایان ویزیت" : "مرحلهٔ بعد";

    /// <summary>The foot's primary-button trailing icon key (check on the last step).</summary>
    public string NextButtonIcon => IsLastStep ? "IconCheck" : "IconChevronLeft";

    // ---- ribbon (reference renderRibbon) ----
    public string RibbonTitle => $"ویزیت {_draft.PatientName} · {_draft.TypeLabel}";
    public string RibbonStatusText => _draft.IsSynthetic ? "بدون نوبت" : "نوبت رزرو شده";
    public IBrush RibbonStatusDot => _draft.IsSynthetic
        ? VisitsPresentation.Fill("#b54708")
        : VisitsPresentation.Fill("#14b8a6");
    public string RibbonDoctorShort => _draft.DoctorShortName;
    public IBrush RibbonDoctorDot => VisitsPresentation.Fill(_draft.DoctorAccent);

    // ---- patient sidebar (reference renderPatientSidebar) ----
    public string PatientName => _draft.PatientName;
    public string PatientKind => _draft.PatientKind;
    public string PatientIconKey => VisitsPresentation.PetIconKey(_draft.PatientIconKey);
    public IBrush PatientAvatarBackground => VisitsPresentation.WizardAvatar(_draft.PatientIconKey).Background;
    public IBrush PatientAvatarForeground => VisitsPresentation.WizardAvatar(_draft.PatientIconKey).Foreground;
    public string TypePillLabel => _draft.TypeLabel;
    public IBrush TypePillBackground => VisitsPresentation.FillAlpha(_draft.TypeAccent, 0x22);
    public IBrush TypePillForeground => VisitsPresentation.Fill(_draft.TypeAccent);
    public bool IsUrgent => _draft.IsUrgent;
    public string OwnerName => _draft.OwnerName;
    public string OwnerPhone => _draft.OwnerPhone;
    public string VisitKindText => _draft.IsSynthetic ? "بدون نوبت" : "نوبت رزرو شده";
    public string DurationText =>
        $"{PersianNumberFormatter.ToPersianDigits(_draft.DurationMinutes)} دقیقه";
    public string TimeText
    {
        get
        {
            var hours = _draft.AtMinutes / 60;
            var minutes = _draft.AtMinutes % 60;
            var formatted = PersianNumberFormatter.ToPersianDigits($"{hours:00}:{minutes:00}");
            return $"زمان: {formatted}";
        }
    }

    public string DoctorName => _draft.DoctorName;
    public string DoctorSpecialty => _draft.DoctorSpecialty;
    public string DoctorInitials => _draft.DoctorInitials;
    public IBrush DoctorAccent => VisitsPresentation.Fill(_draft.DoctorAccent);

    // ---- review totals (reference renderReview) ----
    public string SubtotalText => VisitsPresentation.Money(_subtotal);
    public string TaxText => VisitsPresentation.Money(TaxOf(_subtotal));
    public string TotalText => VisitsPresentation.Money(_subtotal + TaxOf(_subtotal));
    public bool HasRxLines => ServiceLines.Count + ItemLines.Count > 0;
    public bool HasNoRxLines => !HasRxLines;
    public bool IsServicesEmpty => ServiceLines.Count == 0;
    public bool IsItemsEmpty => ItemLines.Count == 0;

    // ---- review note panels (reference renderReview's two «—»-fallback cards) ----
    public string DiagnosisDisplay => string.IsNullOrWhiteSpace(DiagnosisText) ? "—" : DiagnosisText;
    public string PlanDisplay => string.IsNullOrWhiteSpace(PlanText) ? "—" : PlanText;

    /// <summary>
    /// Loads the wizard for the encounter Phase 10 just started, hydrating the
    /// context draft and the billable catalog through the service seam. Called
    /// by the navigation configure callback before the page becomes current.
    /// </summary>
    public void Load(EncounterDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        _draft = _service.GetAsync(draft.Id).GetAwaiter().GetResult();
        _encounterId = _draft.Id;

        // Reference buildEncounter seeds history from the ticket note; every
        // other editable field starts empty.
        HistoryText = _draft.Note;

        var catalog = _service.GetCatalogAsync().GetAwaiter().GetResult();
        ServiceCatalog.Clear();
        ItemCatalog.Clear();
        foreach (var item in catalog.Where(c => c.Kind == RxCatalogKind.Service))
        {
            ServiceCatalog.Add(new RxCatalogOptionViewModel(item));
        }

        foreach (var item in catalog.Where(c => c.Kind == RxCatalogKind.Item))
        {
            ItemCatalog.Add(new RxCatalogOptionViewModel(item));
        }

        CurrentStep = EncounterStep.Vitals;
        RecomputeSubtotal();
        NotifyState();
    }

    partial void OnCurrentStepChanged(EncounterStep value)
    {
        for (var i = 0; i < Steps.Count; i++)
        {
            Steps[i].IsActive = i == (int)value && value != EncounterStep.Done;
            Steps[i].IsDone = i < (int)value;
        }

        NotifyState();
    }

    partial void OnSelectedServiceToAddChanged(RxCatalogOptionViewModel? value)
    {
        if (value is not null)
        {
            AddLine(value.Item);
            SelectedServiceToAdd = null;
        }
    }

    partial void OnSelectedItemToAddChanged(RxCatalogOptionViewModel? value)
    {
        if (value is not null)
        {
            AddLine(value.Item);
            SelectedItemToAdd = null;
        }
    }

    [RelayCommand]
    private async Task NextStepAsync()
    {
        if (CurrentStep == EncounterStep.Review)
        {
            await CompleteAsync();
            return;
        }

        await PersistAsync();
        CurrentStep = (EncounterStep)((int)CurrentStep + 1);
    }

    [RelayCommand]
    private async Task PreviousStepAsync()
    {
        if (CurrentStep == EncounterStep.Vitals)
        {
            return;
        }

        await PersistAsync();
        CurrentStep = (EncounterStep)((int)CurrentStep - 1);
    }

    [RelayCommand]
    private void GoToStep(EncounterStepViewModel? step)
    {
        // Reference stepper: only already-visited stages are clickable.
        if (step is null || step.Index >= (int)CurrentStep || CurrentStep == EncounterStep.Done)
        {
            return;
        }

        CurrentStep = (EncounterStep)step.Index;
    }

    [RelayCommand]
    private void SaveDraft() => _toast.Show("پیش‌نویس ذخیره شد (نمونه طراحی).", Tone.Ok);

    [RelayCommand]
    private void BackToGate() => _navigation.NavigateTo<VisitsTriagePageViewModel>();

    [RelayCommand]
    private void RemoveRxLine(RxLineViewModel? line)
    {
        if (line is null)
        {
            return;
        }

        _service.RemoveRxLineAsync(_encounterId, line.Id).GetAwaiter().GetResult();
        if (line.IsService)
        {
            ServiceLines.Remove(line);
        }
        else
        {
            ItemLines.Remove(line);
        }

        RecomputeSubtotal();
    }

    [RelayCommand]
    private void NextVisit() => _navigation.NavigateTo<VisitsTriagePageViewModel>();

    [RelayCommand]
    private void HandoffToCashier()
    {
        // Reference (visit.js #doneToCashierBtn): toast then navigate to the
        // cashier/sale screen. Per the roadmap's decision log this product flow
        // targets Sale/POS (sale-page.js buildFromEncounter is the real
        // encounter→invoice function), so the completed encounter id is handed
        // to SalePageViewModel which issues the invoice from it (Phase 29 wiring).
        _toast.Show("انتقال به صندوق (نمونه طراحی).", Tone.Ok);
        _navigation.NavigateTo<Animora.Desktop.Pages.Sales.SalePageViewModel>(
            vm => vm.BuildFromEncounterCommand.Execute(_encounterId));
    }

    private async Task CompleteAsync()
    {
        await PersistAsync();
        var summary = await _service.CompleteAsync(_encounterId);

        DonePatientName = summary.PatientName;
        DoneDoctorShort = summary.DoctorShortName;
        DoneTotalText = VisitsPresentation.Money(summary.Total);

        CurrentStep = EncounterStep.Done;
    }

    private void AddLine(RxCatalogItem item)
    {
        var line = new RxLine(
            id: $"ln-{Guid.NewGuid():N}"[..10],
            kind: item.Kind,
            catalogId: item.Id,
            title: item.Title,
            unitPrice: item.Price,
            quantity: 1,
            note: string.Empty);

        _service.AddRxLineAsync(_encounterId, line).GetAwaiter().GetResult();

        var viewModel = new RxLineViewModel(line, RecomputeSubtotal);
        if (item.Kind == RxCatalogKind.Service)
        {
            ServiceLines.Add(viewModel);
        }
        else
        {
            ItemLines.Add(viewModel);
        }

        RecomputeSubtotal();
    }

    private async Task PersistAsync()
    {
        await _service.SaveVitalsAsync(_encounterId, BuildVitals());
        await _service.SaveHistoryNoteAsync(_encounterId, HistoryText);
        await _service.SaveExamNoteAsync(_encounterId, ExamText);
        await _service.SaveDiagnosisAsync(_encounterId, DiagnosisText);
        await _service.SavePlanAsync(_encounterId, PlanText);
    }

    private Vitals BuildVitals() => new(
        WeightKg: ParseDouble(WeightText),
        TemperatureCelsius: ParseDouble(TemperatureText),
        PulseBpm: (int)ParseDouble(PulseText),
        RespirationRate: (int)ParseDouble(RespirationText));

    private void RecomputeSubtotal()
    {
        long total = 0;
        foreach (var line in ServiceLines)
        {
            total += line.LineTotal;
        }

        foreach (var line in ItemLines)
        {
            total += line.LineTotal;
        }

        _subtotal = total;

        OnPropertyChanged(nameof(SubtotalText));
        OnPropertyChanged(nameof(TaxText));
        OnPropertyChanged(nameof(TotalText));
        NotifyLineState();
    }

    private void NotifyLineState()
    {
        OnPropertyChanged(nameof(HasRxLines));
        OnPropertyChanged(nameof(HasNoRxLines));
        OnPropertyChanged(nameof(IsServicesEmpty));
        OnPropertyChanged(nameof(IsItemsEmpty));
    }

    private void NotifyState()
    {
        OnPropertyChanged(nameof(IsVitalsStep));
        OnPropertyChanged(nameof(IsHistoryStep));
        OnPropertyChanged(nameof(IsPlanStep));
        OnPropertyChanged(nameof(IsRxStep));
        OnPropertyChanged(nameof(IsReviewStep));
        OnPropertyChanged(nameof(IsDoneStep));
        OnPropertyChanged(nameof(IsWorkflowVisible));
        OnPropertyChanged(nameof(IsFirstStep));
        OnPropertyChanged(nameof(IsLastStep));
        OnPropertyChanged(nameof(CanGoPrevious));
        OnPropertyChanged(nameof(StepInfoText));
        NotifyLineState();
    }

    private static long TaxOf(long subtotal) =>
        (long)Math.Round(subtotal * TaxRate, MidpointRounding.AwayFromZero);

    private static double ParseDouble(string text)
    {
        var ascii = PersianNumberFormatter.ToAsciiDigits(text).Replace(',', '.');
        return double.TryParse(ascii, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;
    }

    private static EncounterDraft EmptyDraft() => new(
        Id: string.Empty,
        TicketId: string.Empty,
        DoctorId: string.Empty,
        DoctorName: string.Empty,
        DoctorShortName: string.Empty,
        DoctorSpecialty: string.Empty,
        DoctorInitials: string.Empty,
        DoctorAccent: "#0d9488",
        PatientName: string.Empty,
        PatientKind: string.Empty,
        PatientIconKey: "paw",
        OwnerName: string.Empty,
        OwnerPhone: string.Empty,
        TypeId: "visit",
        TypeLabel: "ویزیت عمومی",
        TypeAccent: "#0d9488",
        IsUrgent: false,
        IsSynthetic: false,
        AtMinutes: 0,
        DurationMinutes: 0,
        Note: string.Empty,
        SourceTicketId: string.Empty);
}
