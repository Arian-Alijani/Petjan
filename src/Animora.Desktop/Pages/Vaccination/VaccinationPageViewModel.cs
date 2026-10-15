using System.Collections.ObjectModel;
using Animora.Contracts.Appointments;
using Animora.Contracts.Vaccination;
using Animora.Contracts.Diagnostics;
using Animora.Contracts.Inventory;
using Animora.Desktop.Controls;
using Animora.Desktop.Localization;
using Animora.Desktop.Pages.Vaccination;
using Animora.Desktop.Services;
using Animora.Desktop.Shell;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Pages.Vaccination;

/// <summary>
/// ViewModel for the Vaccination screen: the section tabs (تقویم / واکسن‌های پیش رو /
/// انبار واکسن / تنظیمات / تاریخچه), the vaccine catalog grid and the SMS-template
/// panel. The plan calendar and the upcoming list are owned sub-VMs. All data flows through
/// <see cref="IVaccinationService"/> (the single backend swap point, R-18) —
/// no sample data or mock branching leaks into this public shape.
/// </summary>
public sealed partial class VaccinationPageViewModel : PageViewModelBase
{
    private readonly IVaccinationService _service;
    private readonly IStoreReadinessGate _readiness;
    private readonly IDialogService _dialogs;
    private readonly IToastService _toast;
    private readonly IDataChangeNotifier _changes;
    private readonly IAppointmentBookingService _booking;
    private readonly IInventoryQueryService _inventory;

    private readonly List<VaccineCatalogEntry> _catalog = [];
    private readonly List<SmsTemplate> _templates = [];

    public VaccinationPageViewModel(IVaccinationService service, IStoreReadinessGate readiness,
        IDialogService dialogs, IToastService toast, IDataChangeNotifier changes, IHistoryRecordOpener historyOpener,
        IAppointmentBookingService booking, IInventoryQueryService inventory)
    {
        _inventory = inventory;
        _service = service;
        _booking = booking;
        _changes = changes;
        _readiness = readiness;
        _dialogs = dialogs;
        _toast = toast;

        History = new VaccinationHistoryViewModel(service, historyOpener);
        Calendar = new TreatmentCalendarViewModel(service, toast, NotifyVaccinationChanged, OpenPetPlansFromCalendar,
            OpenOccurrenceDetail);
        Upcoming = new UpcomingTreatmentsViewModel(service, toast, OpenOccurrenceDetail, OpenAddVaccine);

        Title = "دارو و واکسن";
        Eyebrow = "مراقبت";
        Breadcrumbs.Add("مراقبت");
        Breadcrumbs.Add("دارو و واکسن");

        UseLoadStatus(ReloadPageAsync);
        // Patients (owners/pets) feed the due list.
        RefreshOnDataChange(changes, () => { _ = ReloadPageAsync(); }, DataDomain.Patients, DataDomain.Vaccination);
    }

    /// <summary>calendar | upcoming | stock | history | settings — «تقویم» is the landing tab.</summary>
    [ObservableProperty]
    private string _activeSection = "calendar";

    public bool IsCalendarSection => ActiveSection == "calendar";
    public bool IsUpcomingSection => ActiveSection == "upcoming";
    public bool IsStockSection => ActiveSection == "stock";
    public bool IsHistorySection => ActiveSection == "history";
    public bool IsSettingsSection => ActiveSection == "settings";

    /// <summary>The «تقویم» month-calendar sub-VM (owned here, lazily loaded on section open).</summary>
    public TreatmentCalendarViewModel Calendar { get; }

    /// <summary>The «واکسن‌های پیش رو» queue-style sub-VM (owned here, lazily loaded on section open).</summary>
    public UpcomingTreatmentsViewModel Upcoming { get; }

    /// <summary>The «تاریخچه» ledger sub-VM (owned here, lazily loaded on section open).</summary>
    public VaccinationHistoryViewModel History { get; }

    public RangeObservableCollection<VaccineCatalogCardViewModel> Catalog { get; } = [];
    public RangeObservableCollection<SmsTemplateRowViewModel> Templates { get; } = [];

    [ObservableProperty]
    private string _catalogCountText = string.Empty;

    // ── «تنظیمات» panel state (reference's SMS automation card) ──────────────
    [ObservableProperty]
    private string _senderNumber = "۵۰۰۰۲۱۰۰۰۰";

    [ObservableProperty]
    private string _dailyTime = "۰۹:۰۰ صبح";

    [ObservableProperty]
    private string _reminderGap = "۳ روز پیش از نوبت";

    [ObservableProperty]
    private string _overdueRepeat = "هر ۷ روز";

    [ObservableProperty]
    private bool _remindBeforeDue = true;

    [ObservableProperty]
    private bool _warnOverdue = true;

    [ObservableProperty]
    private bool _confirmAfterShot;

    [ObservableProperty]
    private bool _sendOnHolidays;

    public IReadOnlyList<string> DailyTimes { get; } = ["۰۹:۰۰ صبح", "۱۱:۰۰ صبح", "۱۷:۰۰ عصر"];

    public IReadOnlyList<string> ReminderGaps { get; } = ["۳ روز پیش از نوبت", "۷ روز پیش از نوبت", "۱ روز پیش از نوبت"];

    public IReadOnlyList<string> OverdueRepeats { get; } = ["هر ۷ روز", "هر ۳ روز", "هر ۱۴ روز"];

    // Mirror the reference's `vaxToggle(input, name)`: every switch reports
    // «{name} فعال/غیرفعال شد (نمونه طراحی)» when flipped.
    partial void OnRemindBeforeDueChanged(bool value) =>
        ToggleToast("یادآوری پیش از نوبت", value);

    partial void OnWarnOverdueChanged(bool value) =>
        ToggleToast("اخطار عقب‌افتاده", value);

    partial void OnConfirmAfterShotChanged(bool value) =>
        ToggleToast("تأیید تزریق", value);

    partial void OnSendOnHolidaysChanged(bool value) =>
        ToggleToast("ارسال در تعطیلات", value);

    private void ToggleToast(string name, bool enabled) =>
        _toast.Show($"{name} {(enabled ? "فعال" : "غیرفعال")} شد (نمونه طراحی)");

    partial void OnActiveSectionChanged(string value)
    {
        OnPropertyChanged(nameof(IsCalendarSection));
        OnPropertyChanged(nameof(IsUpcomingSection));
        OnPropertyChanged(nameof(IsStockSection));
        OnPropertyChanged(nameof(IsHistorySection));
        OnPropertyChanged(nameof(IsSettingsSection));

        EnsureActiveSectionLoaded();
    }

    private void EnsureActiveSectionLoaded()
    {
        if (IsHistorySection)
        {
            History.EnsureLoaded();
        }
        else if (IsCalendarSection)
        {
            Calendar.EnsureLoaded();
        }
        else if (IsUpcomingSection)
        {
            Upcoming.EnsureLoaded();
        }
    }


    // Singleton page: reload on every visit (not only at construction) so data changed elsewhere is never stale.
    // Failures surface in the shell's load-state block, which also offers the manual refresh.
    public override void OnNavigatedTo()
    {
        if (!IsLoadInFlight) _ = ReloadPageAsync();
    }

    private Task ReloadPageAsync() => TrackLoadAsync(LoadAsync);

    private async Task LoadAsync()
    {
        IsBusy = true;
        await _readiness.WaitAsync();

        // The default tab does not raise OnActiveSectionChanged. Refresh cached sections
        // on entry/data changes, then load the visible section even on its first visit.
        Calendar.Invalidate();
        Upcoming.Invalidate();
        History.Refresh();
        EnsureActiveSectionLoaded();

        _catalog.Clear();
        _catalog.AddRange(await _service.GetCatalogAsync());

        _templates.Clear();
        _templates.AddRange(await _service.GetSmsTemplatesAsync());

        Catalog.ReplaceAll(_catalog.Select(entry => new VaccineCatalogCardViewModel(entry)));

        CatalogCountText = $"{VaccinationPresentation.Fa(_catalog.Count)} قلم";

        Templates.ReplaceAll(_templates.Select(template => new SmsTemplateRowViewModel(template)));

        IsBusy = false;
    }

    [RelayCommand]
    private void SetSection(string? key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return;
        }

        ActiveSection = key;
    }

    /// <summary>
    /// One fan-out point for every vaccination write: the plan/record is a persisted fact shown in the
    /// owner file, the activity feed and the dashboard counters, so all four domains are invalidated together.
    /// </summary>
    private void NotifyVaccinationChanged() =>
        _changes.Notify(DataDomain.Vaccination, DataDomain.History, DataDomain.Patients, DataDomain.Dashboard);

    [RelayCommand]
    private void OpenAddVaccine()
    {
        _dialogs.ShowModal(
            new AddVaccineModalViewModel(_service, _catalog, _dialogs, _toast, _booking, _inventory, OnPlanCreated),
            maxWidth: 580);
    }

    /// <summary>A new plan feeds the calendar (and the dashboard/history counters) — refresh and jump to the calendar tab.</summary>
    private void OnPlanCreated()
    {
        ActiveSection = "calendar";
        NotifyVaccinationChanged();
    }

    [RelayCommand]
    private void OpenAddCatalogVaccine() =>
        _dialogs.ShowModal(new AddCatalogVaccineModalViewModel(_service, _dialogs, _toast, OnCatalogVaccineAdded));

    private void OnCatalogVaccineAdded()
    {
        NotifyVaccinationChanged();
    }

    private void OpenPetPlansFromCalendar(CalendarEventViewModel item) =>
        _dialogs.ShowModal(
            new PetTreatmentPlansModalViewModel(
                _service, _dialogs, _toast, item.Occurrence.PetId, item.PetName, OnPlansEdited),
            maxWidth: 600);

    private void OpenOccurrenceDetail(CalendarEventViewModel item) =>
        _dialogs.ShowModal(new TreatmentOccurrenceDetailViewModel(
            _service, _dialogs, _toast, item.Occurrence, OnPlansEdited));

    private void OnPlansEdited() => NotifyVaccinationChanged();

    [RelayCommand]
    private void UseTemplate(SmsTemplateRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        _toast.Show($"قالب «{row.Name}» انتخاب شد (نمونه طراحی)");
    }

    /// <summary>
    /// Mirrors the reference settings panel's save button
    /// (<c>vaxToast('تنظیمات پیامک ذخیره شد (نمونه طراحی)','check')</c>): the
    /// automation values are design-only for now, so this just confirms.
    /// </summary>
    [RelayCommand]
    private void SaveSettings() =>
        _toast.Show("تنظیمات پیامک ذخیره شد (نمونه طراحی)");
}
