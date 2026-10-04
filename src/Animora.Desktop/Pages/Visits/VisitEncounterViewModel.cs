using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Animora.Contracts.Visits;
using Animora.Contracts.Identity;
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
/// The wizard's six visible stages, mirroring the reference's scene swap
/// (single-page encounter form, then the separate done scene).
/// </summary>
public enum EncounterStep
{
    /// <summary>The single-page encounter form (vitals, history, plan, services, invoice summary).</summary>
    Form,
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
    // Reference renderReview «مالیات (۹٪)» — the single 9% tax line.
    private const decimal TaxRate = 0.09m;

    private readonly IVisitEncounterService _service;
    private readonly IStoreReadinessGate _readiness;
    private readonly INavigationService _navigation;
    private readonly IToastService _toast;
    private readonly IDataChangeNotifier _changes;
    private readonly ICapabilityGate _capabilities;

    public bool CanEditVisit => _capabilities.Can("visits.write");
    private bool CanFinishVisit() => _capabilities.Can("visits.complete");
    // The permission dimension is enforced at run time (CompleteAsync toasts the reason) so the button is
    // never an unexplained dead control; only the diagnosis gate disables it (V02/V09).
    private bool CanAdvance() => !string.IsNullOrWhiteSpace(DiagnosisText);

    private EncounterDraft _draft = EmptyDraft();
    private string _encounterId = string.Empty;

    [ObservableProperty]
    private EncounterStep _currentStep = EncounterStep.Form;

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
        IStoreReadinessGate readiness,
        INavigationService navigation,
        IToastService toast,
        ICapabilityGate capabilities,
        IDataChangeNotifier changes)
    {
        _service = service;
        _changes = changes;
        _readiness = readiness;
        _navigation = navigation;
        _toast = toast;
        _capabilities = capabilities;

        Title = "ویزیت و پرونده پزشکی";
        Eyebrow = "پرونده الکترونیک سلامت";
        Breadcrumbs.Add("مدیریت کلینیک");
        Breadcrumbs.Add("ویزیت");

    }

    // ---- collections ----
    public ObservableCollection<RxCatalogOptionViewModel> ServiceCatalog { get; } = [];

    public ObservableCollection<RxCatalogOptionViewModel> ItemCatalog { get; } = [];

    public ObservableCollection<RxLineViewModel> ServiceLines { get; } = [];

    public ObservableCollection<RxLineViewModel> ItemLines { get; } = [];

    // ---- step visibility ----
    public bool IsDoneStep => CurrentStep == EncounterStep.Done;
    public bool IsWorkflowVisible => CurrentStep != EncounterStep.Done;

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
    /// <summary>True on the plan step while the mandatory diagnosis is still empty (V02).</summary>
    public bool IsDiagnosisMissing => string.IsNullOrWhiteSpace(DiagnosisText);

    public string DiagnosisDisplay => string.IsNullOrWhiteSpace(DiagnosisText) ? "—" : DiagnosisText;
    public string PlanDisplay => string.IsNullOrWhiteSpace(PlanText) ? "—" : PlanText;

    /// <summary>
    /// Loads the wizard for the encounter Phase 10 just started, hydrating the
    /// context draft and the billable catalog through the service seam. Awaited
    /// by the navigation callback before the page becomes current — never
    /// blocked on the UI thread (PERFORMANCE.md §4).
    /// </summary>
    public async Task LoadAsync(EncounterDraft draft)
    {
        await _readiness.WaitAsync();
        ArgumentNullException.ThrowIfNull(draft);

        _draft = await _service.GetAsync(draft.Id);
        _encounterId = _draft.Id;

        // Reference buildEncounter seeds history from the ticket note; every
        // other editable field starts empty.
        HistoryText = _draft.Note;

        var catalog = await _service.GetCatalogAsync();
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

        CurrentStep = EncounterStep.Form;
        RecomputeSubtotal();
        NotifyState();
    }

    partial void OnCurrentStepChanged(EncounterStep value)
    {
        NotifyState();
    }

    partial void OnDiagnosisTextChanged(string value)
    {
        OnPropertyChanged(nameof(DiagnosisDisplay));
        OnPropertyChanged(nameof(IsDiagnosisMissing));
        CompleteVisitCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedServiceToAddChanged(RxCatalogOptionViewModel? value)
    {
        if (value is null) return;
        try { AddPickedLine(value.Item); }
        finally { SelectedServiceToAdd = null; }
    }

    partial void OnSelectedItemToAddChanged(RxCatalogOptionViewModel? value)
    {
        if (value is null) return;
        try { AddPickedLine(value.Item); }
        finally { SelectedItemToAdd = null; }
    }

    /// <summary>
    /// Adds the line a catalog picker just chose. The <c>[ObservableProperty]</c>
    /// change hook is a synchronous UI event and cannot itself be <c>async</c>, so
    /// the async add is started here and faults are observed + surfaced instead of
    /// escaping on the fire-and-forget path (PERFORMANCE.md §4: never block the UI
    /// thread).
    /// </summary>
    private void AddPickedLine(RxCatalogItem item)
    {
        if (!CanEditVisit)
        {
            _toast.Show("اجازهٔ ویرایش ویزیت را ندارید.", Tone.Danger);
            return;
        }

        _ = AddPickedLineAsync(item);
    }

    private async Task AddPickedLineAsync(RxCatalogItem item)
    {
        try
        {
            await AddLineAsync(item);
        }
        catch (Exception ex)
        {
            _toast.Show($"افزودن «{item.Title}» ناموفق بود: {ex.Message}", Tone.Danger);
        }
    }

    /// <summary>
    /// Runs an awaited seam call inside a single guard so a backend <see cref="Result"/> failure
    /// (surfaced by the Client.Services seam as <see cref="InvalidOperationException"/> carrying the
    /// error code) becomes a <see cref="Tone.Danger"/> toast instead of escaping onto the UI
    /// dispatcher, where <c>GlobalExceptionHandler</c> would treat it as a process crash
    /// (CRASH_RECOVERY_REQUIRED). Mirrors the Patients page convention (V01).
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

    /// <summary>Saves the whole single-page form and completes the visit (invoice hand-off).</summary>
    [RelayCommand(CanExecute = nameof(CanAdvance))]
    private Task CompleteVisitAsync() => RunGuardedAsync(
        CompleteAsync, "ثبت نهایی ویزیت ناموفق بود؛ دوباره تلاش کنید");

    [RelayCommand(CanExecute = nameof(CanEditVisit))]
    private Task SaveDraftAsync() => RunGuardedAsync(async () =>
    {
        if (!await PersistAsync())
        {
            return;
        }

        await _service.FlushAsync();
        _toast.Show("پیش‌نویس ویزیت ذخیره شد.", Tone.Ok);
    }, "ذخیرهٔ پیش‌نویس ناموفق بود؛ دوباره تلاش کنید");

    [RelayCommand]
    private Task BackToGate() => RunGuardedAsync(async () =>
    {
        await _service.FlushAsync();
        _navigation.NavigateTo<VisitsTriagePageViewModel>();
    }, "بازگشت به صفحهٔ ویزیت ناموفق بود؛ دوباره تلاش کنید");

    [RelayCommand(CanExecute = nameof(CanEditVisit))]
    private Task RemoveRxLineAsync(RxLineViewModel? line) => RunGuardedAsync(async () =>
    {
        if (line is null)
        {
            return;
        }

        await _service.RemoveRxLineAsync(_encounterId, line.Id);
        if (line.IsService)
        {
            ServiceLines.Remove(line);
        }
        else
        {
            ItemLines.Remove(line);
        }

        RecomputeSubtotal();
    }, "حذف ردیف ناموفق بود؛ دوباره تلاش کنید");

    [RelayCommand]
    private Task NextVisit() => RunGuardedAsync(async () =>
    {
        await _service.FlushAsync();
        _navigation.NavigateTo<VisitsTriagePageViewModel>();
    }, "رفتن به ویزیت بعدی ناموفق بود؛ دوباره تلاش کنید");

    [RelayCommand]
    private Task HandoffToCashierAsync() => RunGuardedAsync(async () =>
    {
        // The completed encounter id is handed to SalePageViewModel, which issues the invoice from it
        // (sale-page.js buildFromEncounter parity). The awaitable path means a failed hand-off toasts
        // on the Sales page rather than escaping from a fire-and-forget command.
        await _navigation.NavigateToAsync<Animora.Desktop.Pages.Sales.SalePageViewModel>(
            vm => vm.BuildFromEncounterAsync(_encounterId));
    }, "انتقال به صندوق ناموفق بود؛ دوباره تلاش کنید");

    private async Task CompleteAsync()
    {
        if (!CanFinishVisit())
        {
            _toast.Show("اجازهٔ تکمیل ویزیت را ندارید.", Tone.Danger);
            return;
        }

        if (!await PersistAsync())
        {
            return;
        }

        var summary = await _service.CompleteAsync(_encounterId);

        // A completed encounter feeds the sales/cashier queues, the history ledger and the patient's file.
        _changes.Notify(
            DataDomain.Visits, DataDomain.Sales, DataDomain.Cashier, DataDomain.History,
            DataDomain.Patients, DataDomain.Dashboard);

        DonePatientName = summary.PatientName;
        DoneDoctorShort = summary.DoctorShortName;
        DoneTotalText = VisitsPresentation.Money(summary.Total);

        CurrentStep = EncounterStep.Done;
    }

    private async Task AddLineAsync(RxCatalogItem item)
    {
        var line = new RxLine(
            id: $"ln-{Guid.NewGuid():N}"[..10],
            kind: item.Kind,
            catalogId: item.Id,
            title: item.Title,
            unitPrice: item.Price,
            quantity: 1,
            note: string.Empty);

        await _service.AddRxLineAsync(_encounterId, line);

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

    /// <summary>
    /// Saves every wizard field through the seam. Returns <see langword="false"/> (after a toast) when a vitals
    /// field is filled but not a valid non-negative number, so nothing is silently stored as 0 (V12).
    /// </summary>
    private async Task<bool> PersistAsync()
    {
        if (!CanEditVisit) return true;
        if (!TryBuildVitals(out var vitals))
        {
            _toast.Show("مقدار علائم حیاتی نامعتبر است؛ فقط عدد غیرمنفی وارد کنید.", Tone.Danger);
            return false;
        }

        await _service.SaveVitalsAsync(_encounterId, vitals);
        await _service.SaveHistoryNoteAsync(_encounterId, HistoryText);
        await _service.SaveExamNoteAsync(_encounterId, ExamText);
        await _service.SaveDiagnosisAsync(_encounterId, DiagnosisText);
        await _service.SavePlanAsync(_encounterId, PlanText);
        return true;
    }

    private bool TryBuildVitals(out Vitals vitals)
    {
        var ok = TryParseNullable(WeightText, out var weight)
            & TryParseNullable(TemperatureText, out var temperature)
            & TryParseNullable(PulseText, out var pulse)
            & TryParseNullable(RespirationText, out var respiration)
            & FitsInt(pulse) & FitsInt(respiration);
        vitals = ok
            ? new Vitals(weight, temperature, pulse is { } p ? (int)Math.Round(p) : null,
                respiration is { } r ? (int)Math.Round(r) : null)
            : Vitals.Empty;
        return ok;
    }

    private static bool FitsInt(double? value) => value is null || value <= int.MaxValue;

    private void RecomputeSubtotal()
    {
        long total = 0;
        try
        {
            // Same checked arithmetic as the backend (EncounterCommands) so UI and invoice never disagree.
            checked
            {
                foreach (var line in ServiceLines)
                {
                    total += line.LineTotal;
                }

                foreach (var line in ItemLines)
                {
                    total += line.LineTotal;
                }

                _ = total + TaxOf(total);
            }
        }
        catch (OverflowException)
        {
            _toast.Show("مبلغ ردیف‌ها از حد مجاز بیشتر است؛ تعداد یا قیمت را اصلاح کنید.", Tone.Danger);
            return;
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
        OnPropertyChanged(nameof(CanEditVisit));
        CompleteVisitCommand.NotifyCanExecuteChanged();
        SaveDraftCommand.NotifyCanExecuteChanged();
        RemoveRxLineCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(IsDiagnosisMissing));
        OnPropertyChanged(nameof(IsDoneStep));
        OnPropertyChanged(nameof(IsWorkflowVisible));
        NotifyLineState();
    }

    private static long TaxOf(long subtotal) =>
        (long)Math.Round(subtotal * TaxRate, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Empty text → <see langword="null"/> (not recorded, valid). Otherwise the value must be a finite,
    /// non-negative number; anything else is invalid (returns <see langword="false"/>).
    /// </summary>
    private static bool TryParseNullable(string text, out double? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        var ascii = PersianNumberFormatter.ToAsciiDigits(text).Trim().Replace(',', '.').Replace('٫', '.');
        if (double.TryParse(ascii, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            && double.IsFinite(parsed) && parsed >= 0)
        {
            value = parsed;
            return true;
        }

        return false;
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
