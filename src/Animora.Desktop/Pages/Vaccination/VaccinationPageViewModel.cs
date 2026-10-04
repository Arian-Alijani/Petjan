using System.Collections.ObjectModel;
using Animora.Contracts.Vaccination;
using Animora.Contracts.Diagnostics;
using Animora.Desktop.Controls;
using Animora.Desktop.Localization;
using Animora.Desktop.Pages.Vaccination;
using Animora.Desktop.Services;
using Animora.Desktop.Shell;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Pages.Vaccination;

/// <summary>
/// ViewModel for the Vaccination screen. Mirrors the reference's page
/// controller (Animora-UI/assets/vaccination.js): the three primary section
/// tabs (نوبت / انبار واکسن / تنظیمات), the status-segment filter + search +
/// species filter toolbar, the records table with pager and per-row actions,
/// the vaccine catalog grid and the SMS-template panel. All data flows through
/// <see cref="IVaccinationService"/> (the single backend swap point, R-18) —
/// no sample data or mock branching leaks into this public shape.
/// </summary>
public sealed partial class VaccinationPageViewModel : PageViewModelBase
{
    private const int PageSize = 8;

    private readonly IVaccinationService _service;
    private readonly IStoreReadinessGate _readiness;
    private readonly IDialogService _dialogs;
    private readonly IToastService _toast;
    private readonly IDataChangeNotifier _changes;

    private readonly List<VaccinationRecord> _records = [];
    private readonly List<VaccineCatalogEntry> _catalog = [];

    // Vaccine lookup by id, rebuilt whenever _catalog is repopulated. Avoids the
    // per-row O(catalog) linear scan (PERFORMANCE.md §2): row builds and filters
    // resolve the vaccine via a single O(1) dictionary hit instead.
    private readonly Dictionary<string, VaccineCatalogEntry> _catalogById = new();
    private readonly List<SmsTemplate> _templates = [];

    // Reference page state: S.seg, S.q, S.species, S.page.
    private string _segment = "all";
    private string _searchQuery = string.Empty;
    private string _species = string.Empty;
    private int _page = 1;
    private int _pageCount = 1;

    public VaccinationPageViewModel(IVaccinationService service, IStoreReadinessGate readiness,
        IDialogService dialogs, IToastService toast, IDataChangeNotifier changes, IHistoryRecordOpener historyOpener)
    {
        _service = service;
        _changes = changes;
        _readiness = readiness;
        _dialogs = dialogs;
        _toast = toast;

        History = new VaccinationHistoryViewModel(service, historyOpener);

        Title = "واکسیناسیون";
        Eyebrow = "مراقبت";
        Breadcrumbs.Add("مراقبت");
        Breadcrumbs.Add("واکسیناسیون");

        foreach (var option in new[]
                 {
                     new SpeciesFilterOption(string.Empty, "همه گونه‌ها"),
                     new SpeciesFilterOption("cat", "گربه"),
                     new SpeciesFilterOption("dog", "سگ"),
                     new SpeciesFilterOption("bird", "پرنده"),
                     new SpeciesFilterOption("rabbit", "خرگوش"),
                     new SpeciesFilterOption("rodent", "جونده"),
                 })
        {
            SpeciesOptions.Add(option);
        }

        SelectedSpeciesOption = SpeciesOptions[0];

        UseLoadStatus(ReloadPageAsync);
        // Patients (owners/pets) feed the due list.
        RefreshOnDataChange(changes, () => _ = ReloadPageAsync(), DataDomain.Patients);
    }

    /// <summary>visits | stock | history | settings — the reference's <c>#vxMainSeg</c> keys.</summary>
    [ObservableProperty]
    private string _activeSection = "visits";

    public bool IsVisitsSection => ActiveSection == "visits";
    public bool IsStockSection => ActiveSection == "stock";
    public bool IsHistorySection => ActiveSection == "history";
    public bool IsSettingsSection => ActiveSection == "settings";
    public bool ShowVisitsTools => ActiveSection == "visits";

    /// <summary>The «تاریخچه» ledger sub-VM (owned here, lazily loaded on section open).</summary>
    public VaccinationHistoryViewModel History { get; }

    public RangeObservableCollection<VaccinationSegmentViewModel> Segments { get; } = [];
    public RangeObservableCollection<VaccinationRecordRowViewModel> FilteredRecords { get; } = [];
    public RangeObservableCollection<VaccineCatalogCardViewModel> Catalog { get; } = [];
    public RangeObservableCollection<SmsTemplateRowViewModel> Templates { get; } = [];
    public ObservableCollection<SpeciesFilterOption> SpeciesOptions { get; } = [];
    public RangeObservableCollection<PagerButtonViewModel> Pages { get; } = [];

    [ObservableProperty]
    private SpeciesFilterOption? _selectedSpeciesOption;

    [ObservableProperty]
    private string _searchQueryText = string.Empty;

    [ObservableProperty]
    private bool _hasSearchValue;

    [ObservableProperty]
    private bool _hasRecords;

    [ObservableProperty]
    private bool _hasPages;

    [ObservableProperty]
    private string _pagerShownText = string.Empty;

    [ObservableProperty]
    private string _pagerTotalText = string.Empty;

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

    partial void OnSelectedSpeciesOptionChanged(SpeciesFilterOption? value)
    {
        _species = value?.Value ?? string.Empty;
        _page = 1;
        RefreshRows();
    }

    partial void OnActiveSectionChanged(string value)
    {
        OnPropertyChanged(nameof(IsVisitsSection));
        OnPropertyChanged(nameof(IsStockSection));
        OnPropertyChanged(nameof(IsHistorySection));
        OnPropertyChanged(nameof(IsSettingsSection));
        OnPropertyChanged(nameof(ShowVisitsTools));

        if (value == "history")
        {
            History.EnsureLoaded();
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

        _records.Clear();
        _records.AddRange(await _service.GetRecordsAsync());
        _catalog.Clear();
        _catalog.AddRange(await _service.GetCatalogAsync());
        _catalogById.Clear();
        foreach (var entry in _catalog)
        {
            _catalogById[entry.Id] = entry;
        }

        _templates.Clear();
        _templates.AddRange(await _service.GetSmsTemplatesAsync());

        Catalog.ReplaceAll(_catalog.Select(entry => new VaccineCatalogCardViewModel(entry)));

        CatalogCountText = $"{VaccinationPresentation.Fa(_catalog.Count)} قلم";

        Templates.ReplaceAll(_templates.Select(template => new SmsTemplateRowViewModel(template)));

        IsBusy = false;
        Refresh();
    }

    private void Refresh()
    {
        RefreshSegments();
        RefreshRows();
    }

    // Reference renderSeg: counts derive from ALL records (vaxCounts()).
    private void RefreshSegments()
    {
        var counts = new Dictionary<string, int>
        {
            ["all"] = _records.Count,
            ["due"] = 0,
            ["overdue"] = 0,
            ["scheduled"] = 0,
            ["done"] = 0,
        };

        foreach (var record in _records)
        {
            var key = Key(record.Status);
            counts[key] = counts.GetValueOrDefault(key) + 1;
        }

        var defs = new (string Key, string Label)[]
        {
            ("all", "همه"),
            ("due", "نزدیک"),
            ("overdue", "عقب‌افتاده"),
            ("scheduled", "آتی"),
            ("done", "انجام‌شده"),
        };

        Segments.ReplaceAll(defs.Select(d => new VaccinationSegmentViewModel(d.Key, d.Label, counts[d.Key])
        {
            IsActive = _segment == d.Key,
        }));
    }

    private List<VaccinationRecord> Filtered()
    {
        var query = PersianText.Normalize(_searchQuery);
        return _records.Where(record =>
        {
            if (_segment != "all" && Key(record.Status) != _segment)
            {
                return false;
            }

            if (!string.IsNullOrEmpty(_species) && record.Species != _species)
            {
                return false;
            }

            if (query.Length > 0)
            {
                var vaccine = _catalogById.GetValueOrDefault(record.VaccineId);
                var hay = PersianText.Normalize(
                    $"{record.PetName} {record.OwnerName} {vaccine?.Name} {vaccine?.Short} {record.Dose}");
                if (!hay.Contains(query, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }).ToList();
    }

    // Reference renderRows: 8 rows per page, page clamped to the last page.
    private void RefreshRows()
    {
        var list = Filtered();
        _pageCount = Math.Max(1, (int)Math.Ceiling(list.Count / (double)PageSize));
        if (_page > _pageCount)
        {
            _page = _pageCount;
        }

        var slice = list.Skip((_page - 1) * PageSize).Take(PageSize).ToList();

        // Batched rebuild (PERFORMANCE.md §5): one Reset instead of one
        // CollectionChanged (and layout pass) per row.
        FilteredRecords.ReplaceAll(slice.Select(record =>
            new VaccinationRecordRowViewModel(
                record,
                _catalogById.GetValueOrDefault(record.VaccineId))));

        HasRecords = list.Count > 0;

        Pages.ReplaceAll(Enumerable.Range(1, _pageCount).Select(i => new PagerButtonViewModel(i, VaccinationPresentation.Fa(i))
        {
            IsActive = i == _page,
        }));

        HasPages = _pageCount > 1;
        PagerShownText = VaccinationPresentation.Fa(slice.Count);
        PagerTotalText = VaccinationPresentation.Fa(list.Count);
    }

    private static string Key(VaccinationStatus status) => status switch
    {
        VaccinationStatus.Due => "due",
        VaccinationStatus.Overdue => "overdue",
        VaccinationStatus.Scheduled => "scheduled",
        VaccinationStatus.Done => "done",
        _ => "all",
    };

    [RelayCommand]
    private void SetSection(string? key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return;
        }

        ActiveSection = key;
    }

    [RelayCommand]
    private void SetSegment(string? key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return;
        }

        _segment = key;
        _page = 1;
        Refresh();
    }

    [RelayCommand]
    private void SetSpecies(SpeciesFilterOption? option)
    {
        _species = option?.Value ?? string.Empty;
        _page = 1;
        RefreshRows();
    }

    [RelayCommand]
    private void SetPage(int page)
    {
        _page = page;
        RefreshRows();
    }

    // Search debounce (~160ms), moved out of the code-behind into the VM.
    private readonly Debouncer _searchDebounce = new(System.TimeSpan.FromMilliseconds(160));

    partial void OnSearchQueryTextChanged(string value)
    {
        HasSearchValue = value.Trim().Length > 0;
        _searchDebounce.Debounce(() => ApplySearch(SearchQueryText));
    }

    public void ApplySearch(string? text)
    {
        _searchQuery = text?.Trim() ?? string.Empty;
        _page = 1;
        RefreshRows();
    }

    [RelayCommand]
    private void ClearSearch()
    {
        SearchQueryText = string.Empty;
        ApplySearch(string.Empty);
    }

    [RelayCommand]
    private async Task MarkDoneAsync(VaccinationRecordRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        await _service.MarkDoneAsync(row.Record.Id);
        NotifyVaccinationChanged();
        _records[_records.FindIndex(r => r.Id == row.Record.Id)] =
            _records.First(r => r.Id == row.Record.Id) with { Status = VaccinationStatus.Done };

        Refresh();
        _toast.Show("واکسن به‌عنوان انجام‌شده ثبت شد (نمونه طراحی)");
    }

    [RelayCommand]
    private void OpenSmsCompose(VaccinationRecordRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        _dialogs.ShowModal(new SmsComposeViewModel(_service, row.Record, _templates, _dialogs, _toast, OnSmsSent));
    }

    [RelayCommand]
    private void OpenRecordDetail(VaccinationRecordRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        var vaccine = _catalogById.GetValueOrDefault(row.Record.VaccineId);
        _dialogs.ShowModal(new VaccinationRecordDetailViewModel(
            row.Record,
            vaccine,
            _dialogs,
            onSendSms: record => OpenSmsComposeById(record),
            onMarkDone: record => _ = MarkDoneByIdAsync(record)));
    }

    // Re-opens the compose drawer for a record chosen from the detail sheet
    // (mirrors the reference's `openSmsCompose` fired from inside the drawer).
    private void OpenSmsComposeById(VaccinationRecord record) =>
        _dialogs.ShowModal(new SmsComposeViewModel(_service, record, _templates, _dialogs, _toast, OnSmsSent));

    // Marks a record done when triggered from the detail sheet (mirrors the
    // reference's `vaxMarkDone` fired from inside the drawer).
    // A sent reminder is a persisted fact shown in the owner file and the activity feed.
    private void OnSmsSent()
    {
        NotifyVaccinationChanged();
        Refresh();
    }

    private void NotifyVaccinationChanged() =>
        _changes.Notify(DataDomain.Vaccination, DataDomain.History, DataDomain.Patients, DataDomain.Dashboard);

    private async Task MarkDoneByIdAsync(VaccinationRecord record)
    {
        await _service.MarkDoneAsync(record.Id);
        NotifyVaccinationChanged();
        var index = _records.FindIndex(r => r.Id == record.Id);
        if (index >= 0)
        {
            _records[index] = _records[index] with { Status = VaccinationStatus.Done };
        }

        Refresh();
        _toast.Show("واکسن به‌عنوان انجام‌شده ثبت شد (نمونه طراحی)");
    }

    [RelayCommand]
    private void OpenAddVaccine()
    {
        _dialogs.ShowModal(new AddVaccineModalViewModel(_catalog, _dialogs, _toast));
    }

    [RelayCommand]
    private void BulkSms()
    {
        var count = Filtered().Count(r =>
            r.Status is VaccinationStatus.Due or VaccinationStatus.Overdue);
        _toast.Show($"یادآور برای {VaccinationPresentation.Fa(count)} نوبت زمان‌بندی شد (نمونه طراحی)");
    }

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
