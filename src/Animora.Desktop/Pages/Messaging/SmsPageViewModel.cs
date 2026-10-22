using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Animora.Contracts.Diagnostics;
using Animora.Contracts.Messaging;
using Animora.Desktop.Controls;
using Animora.Desktop.Services;
using Animora.Desktop.Shell;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Pages.Messaging;

/// <summary>One segmented tab of the «مدیریت پیامک» page.</summary>
public sealed partial class SmsTabViewModel(SmsSection section, string label, string iconKey) : ObservableObject
{
    public SmsSection Section { get; } = section;

    public string Label { get; } = label;

    /// <summary>Icons theme key (resolved by <c>IconKeyConverter</c>) shown before the tab label.</summary>
    public string IconKey { get; } = iconKey;

    [ObservableProperty]
    private bool _isActive;
}

/// <summary>One preset of the report/usage window (۷ / ۳۰ / ۹۰ روز / از ابتدای سال).</summary>
public sealed partial class SmsRangeChipViewModel(string id, string label) : ObservableObject
{
    public string Id { get; } = id;

    public string Label { get; } = label;

    [ObservableProperty]
    private bool _isActive;
}

/// <summary>
/// ViewModel of the «مدیریت پیامک» page: a segmented switcher over three sections — overview (credit + usage charts),
/// sent-SMS report, and per-event settings — sharing one <see cref="SmsReportRange"/>. Only the visible section loads;
/// a section re-queries only when the range changed or a refresh is forced, and a superseded load never overwrites a
/// newer one. Depends only on the <see cref="ISmsService"/> seam (through its sections), which is a design stub today.
/// </summary>
public sealed partial class SmsPageViewModel : PageViewModelBase
{
    private static readonly (string Id, string Label)[] Ranges =
    [
        ("7d", "۷ روز گذشته"),
        ("30d", "۳۰ روز گذشته"),
        ("90d", "۹۰ روز گذشته"),
        ("ytd", "از ابتدای سال"),
    ];

    private readonly IStoreReadinessGate _readiness;
    private readonly IToastService _toast;
    private readonly Dictionary<SmsSection, SmsSectionViewModel> _sections;
    private CancellationTokenSource? _loadCts;
    private int _generation;
    private SmsReportRange _range = SmsReportRange.Default;

    public SmsPageViewModel(IStoreReadinessGate readiness, IToastService toast,
        SmsOverviewViewModel overview, SmsReportViewModel report, SmsSettingsViewModel settings)
    {
        _readiness = readiness;
        _toast = toast;

        Title = "مدیریت پیامک";
        Eyebrow = "اعتبار، گزارش و تنظیمات پیامک";
        Breadcrumbs.Add("سیستم");
        Breadcrumbs.Add("مدیریت پیامک");

        Overview = overview;
        Report = report;
        Settings = settings;
        var all = new SmsSectionViewModel[] { overview, report, settings };
        _sections = all.ToDictionary(s => s.Section);
        Tabs.AddRange(all.Select(s => new SmsTabViewModel(s.Section, s.Label, s.IconKey)));
        RangeChips.AddRange(Ranges.Select(r => new SmsRangeChipViewModel(r.Id, r.Label)));
        MarkActiveChip();
        SelectSection(SmsSection.Overview, load: false);
        UseLoadStatus(() => ReloadAsync(force: true));
    }

    public SmsOverviewViewModel Overview { get; }

    public SmsReportViewModel Report { get; }

    public SmsSettingsViewModel Settings { get; }

    public RangeObservableCollection<SmsTabViewModel> Tabs { get; } = [];

    public RangeObservableCollection<SmsRangeChipViewModel> RangeChips { get; } = [];

    /// <summary>The section currently shown (resolved to its view by the shell's view locator).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsRange))]
    private SmsSectionViewModel _currentSection = null!;

    /// <summary>The range chips only matter for the overview/report sections.</summary>
    public bool ShowsRange => CurrentSection?.UsesRange == true;

    /// <summary>The page-wide window the overview and report read.</summary>
    public SmsReportRange Range => _range;

    // Singleton page: re-check on every visit; a section already current for the range is not re-queried.
    public override void OnNavigatedTo()
    {
        if (!IsLoadInFlight) _ = ReloadAsync(force: false);
    }

    // Leaving cancels any load and drops per-visit data (report rows, unsaved settings edits are re-read next visit).
    public override void OnNavigatedFrom()
    {
        CancelPending();
        foreach (var section in _sections.Values) section.Evict();
    }

    [RelayCommand]
    private void SelectRange(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id == _range.RangeId) return;
        _range = new SmsReportRange(id);
        OnPropertyChanged(nameof(Range));
        MarkActiveChip();
        _ = ReloadAsync(force: false);
    }

    [RelayCommand]
    private void ShowSection(SmsSection section)
    {
        if (CurrentSection.Section == section) return;
        SelectSection(section, load: true);
    }

    private void MarkActiveChip()
    {
        foreach (var chip in RangeChips) chip.IsActive = chip.Id == _range.RangeId;
    }

    private void SelectSection(SmsSection section, bool load)
    {
        CurrentSection = _sections[section];
        foreach (var tab in Tabs) tab.IsActive = tab.Section == section;
        if (load) _ = ReloadAsync(force: false);
    }

    private Task ReloadAsync(bool force) => TrackLoadResultAsync(() => LoadCurrentAsync(force));

    private async Task<bool> LoadCurrentAsync(bool force)
    {
        CancelPending();
        var cts = _loadCts = new CancellationTokenSource();
        var token = ++_generation;
        var section = CurrentSection;
        if (force)
            foreach (var other in _sections.Values.Where(s => s != section)) other.Invalidate();

        try
        {
            await _readiness.WaitAsync(cts.Token);
            var ok = await section.LoadAsync(_range, force, cts.Token);
            // A newer reload superseded this one: its result owns the page state.
            if (token != _generation) return true;
            if (!ok && !section.HasData) return false;
            if (!ok) _toast.Show(section.ErrorMessage, Tone.Danger);
            return ok;
        }
        catch (OperationCanceledException)
        {
            return true;
        }
    }

    private void CancelPending()
    {
        var cts = _loadCts;
        _loadCts = null;
        if (cts is null) return;
        cts.Cancel();
        cts.Dispose();
    }
}
