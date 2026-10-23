using System.Collections.ObjectModel;
using System.IO;
using System.Globalization;
using System.Linq;
using Animora.Contracts.Visits;
using Animora.Contracts.Identity;
using Animora.Contracts.Patients;
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
    private readonly IDialogService _dialogs;
    private readonly ILabTestSettingsService _labSettings;
    private readonly IFileLauncher _files;
    private readonly IPatientsQueryService _patients;

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
        IDataChangeNotifier changes,
        IDialogService dialogs,
        ILabTestSettingsService labSettings,
        IFileLauncher files,
        IPatientsQueryService patients)
    {
        _service = service;
        _changes = changes;
        _readiness = readiness;
        _navigation = navigation;
        _toast = toast;
        _capabilities = capabilities;
        _dialogs = dialogs;
        _labSettings = labSettings;
        _files = files;
        _patients = patients;

        Title = "ویزیت و پرونده پزشکی";
        Eyebrow = "پرونده الکترونیک سلامت";
        Breadcrumbs.Add("مدیریت کلینیک");
        Breadcrumbs.Add("ویزیت");

    }

    // ---- collections ----
    // The billable catalog, kept as the raw contract rows so the shared Sales
    // catalog picker (CatalogModalViewModel) can be fed a snapshot and the chosen
    // row can be mapped back to an RxCatalogItem for the encounter's rx list.
    private readonly List<RxCatalogItem> _catalog = [];

    public ObservableCollection<RxLineViewModel> ServiceLines { get; } = [];

    public ObservableCollection<RxLineViewModel> ItemLines { get; } = [];

    /// <summary>Radiology studies recorded on this visit («رادیولوژی»), oldest first.</summary>
    public ObservableCollection<RadiologyEntryViewModel> RadiologyEntries { get; } = [];

    /// <summary>Laboratory result sheets recorded on this visit («آزمایشات»), oldest first.</summary>
    public ObservableCollection<LabEntryViewModel> LabEntries { get; } = [];

    /// <summary>Row-wise clinical alerts (allergies) edited during the encounter (Task 3).</summary>
    public ObservableCollection<AlertRowEditViewModel> EditAllergies { get; } = [];

    /// <summary>Row-wise clinical alerts (chronic conditions) edited during the encounter (Task 3).</summary>
    public ObservableCollection<AlertRowEditViewModel> EditChronic { get; } = [];

    public bool IsRadiologyEmpty => RadiologyEntries.Count == 0;

    public bool IsLabEmpty => LabEntries.Count == 0;

    // ---- step visibility ----
    public bool IsDoneStep => CurrentStep == EncounterStep.Done;
    public bool IsWorkflowVisible => CurrentStep != EncounterStep.Done;

    // ---- ribbon (reference renderRibbon) ----
    public string RibbonTitle => $"ویزیت {_draft.PatientName} · {_draft.TypeLabel}";
    public string RibbonStatusText => _draft.IsSynthetic ? "بدون نوبت" : "نوبت رزرو شده";
    public IBrush RibbonStatusDot => _draft.IsSynthetic
        ? VisitsPresentation.Fill("#b54708")
        : VisitsPresentation.Fill("#5b7cfa");
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

        await RefreshCatalogAsync();
        await LoadParaclinicAsync();
        await LoadClinicalAlertsAsync();

        CurrentStep = EncounterStep.Form;
        RecomputeSubtotal();
        NotifyState();
    }

    /// <summary>
    /// Re-reads the billable catalog so goods/services created after the wizard opened (Inventory, Sales rates)
    /// are visible the next time the picker is shown — the picker never serves a stale load-time snapshot.
    /// </summary>
    private async Task RefreshCatalogAsync()
    {
        var catalog = await _service.GetCatalogAsync();
        _catalog.Clear();
        _catalog.AddRange(catalog);
    }

    /// <summary>
    /// Hydrates the radiology and lab lists from the persisted visit (so a visit pulled from another client renders
    /// them too). A backend failure empties the lists and toasts instead of blocking the whole encounter from opening.
    /// </summary>
    private async Task LoadParaclinicAsync()
    {
        try
        {
            var radiology = await _service.GetRadiologyAsync(_encounterId);
            var labs = await _service.GetLabsAsync(_encounterId);
            RadiologyEntries.Clear();
            foreach (var entry in radiology)
            {
                RadiologyEntries.Add(new RadiologyEntryViewModel(entry));
            }

            LabEntries.Clear();
            foreach (var entry in labs)
            {
                LabEntries.Add(new LabEntryViewModel(entry));
            }
        }
        catch (InvalidOperationException ex)
        {
            RadiologyEntries.Clear();
            LabEntries.Clear();
            _toast.Show(
                PatientsPresentation.CommandFailureMessage(ex, "بارگذاری رادیولوژی و آزمایشات ویزیت ناموفق بود"),
                Tone.Danger);
        }

        NotifyParaclinicState();
    }

    private void NotifyParaclinicState()
    {
        OnPropertyChanged(nameof(IsRadiologyEmpty));
        OnPropertyChanged(nameof(IsLabEmpty));
    }

    /// <summary>
    /// Loads the pet's current clinical alerts (allergies + chronic conditions) so the encounter's
    /// clinical-context section can add/remove them; a failure simply leaves the rows empty.
    /// </summary>
    private async Task LoadClinicalAlertsAsync()
    {
        EditAllergies.Clear();
        EditChronic.Clear();
        if (string.IsNullOrWhiteSpace(_encounterId))
        {
            return;
        }

        try
        {
            var pet = await _patients.GetPetAsync(_draft.PetId);
            if (pet is null)
            {
                return;
            }

            foreach (var a in pet.Allergies)
            {
                EditAllergies.Add(new AlertRowEditViewModel(a, r => EditAllergies.Remove(r)));
            }

            foreach (var c in pet.ChronicConditions)
            {
                EditChronic.Add(new AlertRowEditViewModel(c, r => EditChronic.Remove(r)));
            }
        }
        catch (InvalidOperationException)
        {
            // Permission/session failure — leave the alerts empty rather than blocking the encounter.
        }
    }

    /// <summary>Adds an empty allergy row in the encounter's clinical-context section (Task 3).</summary>
    [RelayCommand]
    private void AddVisitAllergy() => EditAllergies.Add(new AlertRowEditViewModel(string.Empty, r => EditAllergies.Remove(r)));

    /// <summary>Adds an empty chronic-condition row in the encounter's clinical-context section (Task 3).</summary>
    [RelayCommand]
    private void AddVisitChronic() => EditChronic.Add(new AlertRowEditViewModel(string.Empty, r => EditChronic.Remove(r)));

    /// <summary>Opens the radiology form sheet over the encounter («رادیولوژی»).</summary>
    [RelayCommand]
    private void OpenRadiology()
    {
        if (!EnsureCanEdit())
        {
            return;
        }

        _dialogs.ShowSheet(new RadiologySheetViewModel(_service, _dialogs, SaveRadiologyAsync), width: 760, height: 720);
    }

    /// <summary>Opens the lab-result form sheet over the encounter («آزمایشات»).</summary>
    [RelayCommand]
    private void OpenLab()
    {
        if (!EnsureCanEdit())
        {
            return;
        }

        _dialogs.ShowSheet(new LabSheetViewModel(_labSettings, _dialogs, SaveLabAsync), width: 820, height: 720);
    }

    private bool EnsureCanEdit()
    {
        if (CanEditVisit)
        {
            return true;
        }

        _toast.Show("اجازهٔ ویرایش ویزیت را ندارید.", Tone.Danger);
        return false;
    }

    // The sheet keeps its own inline error display, so failures propagate to it (InvalidOperationException);
    // only a success reaches the list + change notification.
    private async Task SaveRadiologyAsync(RadiologyEntry entry)
    {
        var id = await _service.AddRadiologyAsync(_encounterId, entry);
        RadiologyEntries.Add(new RadiologyEntryViewModel(entry with { Id = id }));
        NotifyParaclinicState();
        _changes.Notify(DataDomain.Visits, DataDomain.History, DataDomain.Patients);
        _toast.Show("رادیولوژی ثبت شد.", Tone.Ok);
    }

    private async Task SaveLabAsync(LabEntry entry)
    {
        var id = await _service.AddLabAsync(_encounterId, entry);
        LabEntries.Add(new LabEntryViewModel(entry with { Id = id }));
        NotifyParaclinicState();
        _changes.Notify(DataDomain.Visits, DataDomain.History, DataDomain.Patients);
        _toast.Show("آزمایش ثبت شد.", Tone.Ok);
    }

    [RelayCommand(CanExecute = nameof(CanEditVisit))]
    private void RemoveRadiology(RadiologyEntryViewModel? entry)
    {
        if (entry is null)
        {
            return;
        }

        _dialogs.ShowModal(new Animora.Desktop.Pages.Sales.Shared.ConfirmDialogViewModel(
            _dialogs, "danger", "IconTrash", "حذف رادیولوژی", "رادیولوژی ", entry.BodyPart, " حذف شود؟",
            "فایل‌های پیوست از این ویزیت جدا می‌شوند.", "حذف", "انصراف",
            () => _ = RunGuardedAsync(async () =>
            {
                await _service.RemoveRadiologyAsync(_encounterId, entry.Id);
                RadiologyEntries.Remove(entry);
                NotifyParaclinicState();
                _changes.Notify(DataDomain.Visits, DataDomain.History, DataDomain.Patients);
            }, "حذف رادیولوژی ناموفق بود؛ دوباره تلاش کنید")), 480);
    }

    [RelayCommand(CanExecute = nameof(CanEditVisit))]
    private void RemoveLab(LabEntryViewModel? entry)
    {
        if (entry is null)
        {
            return;
        }

        _dialogs.ShowModal(new Animora.Desktop.Pages.Sales.Shared.ConfirmDialogViewModel(
            _dialogs, "danger", "IconTrash", "حذف آزمایش", "آزمایش ", entry.DateText, " حذف شود؟",
            "این عمل قابل بازگشت نیست.", "حذف", "انصراف",
            () => _ = RunGuardedAsync(async () =>
            {
                await _service.RemoveLabAsync(_encounterId, entry.Id);
                LabEntries.Remove(entry);
                NotifyParaclinicState();
                _changes.Notify(DataDomain.Visits, DataDomain.History, DataDomain.Patients);
            }, "حذف آزمایش ناموفق بود؛ دوباره تلاش کنید")), 480);
    }

    /// <summary>
    /// Opens a stored attachment with the OS viewer: the bytes are copied from the content-addressed store into a
    /// temp file, and only passive image / document types are ever launched (attachments can arrive through sync).
    /// </summary>
    [RelayCommand]
    private Task OpenAttachmentAsync(RadiologyFileViewModel? file) => RunGuardedAsync(async () =>
    {
        if (file is null)
        {
            return;
        }

        if (!ParaclinicPresentation.IsViewable(file.Name))
        {
            _toast.Show("این نوع فایل از داخل برنامه باز نمی‌شود.", Tone.Danger);
            return;
        }

        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "Animora", "Attachments");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"{file.Hash[..Math.Min(12, file.Hash.Length)]}-{ParaclinicPresentation.SafeFileName(file.Name)}");
            if (!File.Exists(path))
            {
                await using var source = await _service.OpenAttachmentAsync(file.Hash);
                await using var target = File.Create(path);
                await source.CopyToAsync(target);
            }

            if (!_files.Open(path))
            {
                _toast.Show("برنامه‌ای برای باز کردن این فایل پیدا نشد.", Tone.Danger);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _toast.Show("باز کردن فایل پیوست ممکن نشد.", Tone.Danger);
        }
    }, "فایل پیوست پیدا نشد یا قابل خواندن نیست");

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

    /// <summary>
    /// Opens the shared Sales catalog picker (<see cref="Animora.Desktop.Pages.Sales.CatalogModalViewModel"/>)
    /// pre-filtered to services — the exact same drawer the Sale/Cashier «افزودن آیتم»
    /// opens — so services are added to the encounter the same way they are added to an invoice.
    /// </summary>
    [RelayCommand]
    private void AddService() => OpenCatalogPicker("svc");

    /// <summary>Opens the shared catalog picker pre-filtered to goods («افزودن کالا»).</summary>
    [RelayCommand]
    private void AddItem() => OpenCatalogPicker("item");

    private void OpenCatalogPicker(string initialFilter)
    {
        if (!CanEditVisit)
        {
            _toast.Show("اجازهٔ ویرایش ویزیت را ندارید.", Tone.Danger);
            return;
        }

        _dialogs.ShowModal(
            new Animora.Desktop.Pages.Sales.CatalogModalViewModel(
                loadCatalog: async () =>
                {
                    await RefreshCatalogAsync();
                    return BuildCatalogSnapshot();
                },
                addItem: AddCatalogSelectionAsync,
                toast: _toast,
                dialogs: _dialogs,
                onChanged: null,
                initialFilter: initialFilter),
            Animora.Desktop.Pages.Sales.CatalogModalViewModel.DrawerWidth);
    }

    /// <summary>
    /// Projects the encounter's billable catalog onto the Sales
    /// <see cref="Animora.Contracts.Sales.CatalogSnapshot"/> the shared picker renders: every
    /// service/item row, the «همه / خدمات / دارو و کالا» filter chips with counts, and no recent strip
    /// (the encounter catalog has no recency store).
    /// </summary>
    private Animora.Contracts.Sales.CatalogSnapshot BuildCatalogSnapshot()
    {
        var items = _catalog
            .Select(c => new Animora.Contracts.Sales.CatalogItem(
                Id: c.Id,
                Kind: c.Kind == RxCatalogKind.Service ? "svc" : "item",
                Title: c.Title,
                GroupLabel: c.Kind == RxCatalogKind.Service ? "خدمت" : "دارو و کالا",
                GroupKey: c.Kind == RxCatalogKind.Service ? "svc" : "item",
                Price: c.Price,
                IconKey: c.Kind == RxCatalogKind.Service ? "IconClipboard" : "IconBox",
                OnHandScaled: c.OnHandScaled))
            .ToList();

        var serviceCount = items.Count(i => i.Kind == "svc");
        var itemCount = items.Count - serviceCount;
        var filters = new List<Animora.Contracts.Sales.CatalogFilterRef>
        {
            new("all", "همه", items.Count),
            new("svc", "خدمات", serviceCount),
            new("item", "دارو و کالا", itemCount),
        };

        return new Animora.Contracts.Sales.CatalogSnapshot(items, filters, []);
    }

    /// <summary>
    /// Maps a row chosen in the shared picker back to the encounter's
    /// <see cref="RxCatalogItem"/> and appends it to the rx list through the visit seam.
    /// </summary>
    private Task AddCatalogSelectionAsync(Animora.Contracts.Sales.CatalogItem selection)
    {
        var item = _catalog.FirstOrDefault(c => c.Id == selection.Id);
        if (item is null)
        {
            return Task.CompletedTask;
        }

        return AddLineAsync(item);
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
    private Task HandoffToCashierAsync() => RunGuardedAsync(() =>
    {
        // Completing the visit already queued it as «آمادهٔ صدور». Invoice issuing is the cashier's
        // job (SalesDraft permission), so this only opens the cashier page on that ready queue —
        // it never issues an invoice from the doctor's account.
        _navigation.NavigateTo<Animora.Desktop.Pages.Sales.SalePageViewModel>(vm => vm.ShowReadyQueue());
        return Task.CompletedTask;
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
        // Persist the clinical alerts the doctor added/edited during the encounter (Task 3).
        await _patients.SavePetAlertsAsync(_draft.PetId,
            PetFormViewModel.AlertTexts(EditAllergies), PetFormViewModel.AlertTexts(EditChronic));
        _changes.Notify(DataDomain.Patients);
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
        RemoveRadiologyCommand.NotifyCanExecuteChanged();
        RemoveLabCommand.NotifyCanExecuteChanged();
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
        DoctorAccent: "#4a6cf7",
        PatientName: string.Empty,
        PatientKind: string.Empty,
        PatientIconKey: "paw",
        OwnerName: string.Empty,
        OwnerPhone: string.Empty,
        TypeId: "visit",
        TypeLabel: "ویزیت عمومی",
        TypeAccent: "#4a6cf7",
        IsUrgent: false,
        IsSynthetic: false,
        AtMinutes: 0,
        DurationMinutes: 0,
        Note: string.Empty,
        SourceTicketId: string.Empty,
        PetId: string.Empty);
}
