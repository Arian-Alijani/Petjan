using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Animora.Contracts.Diagnostics;
using Animora.Contracts.Identity;
using Animora.Contracts.Statistics;
using Animora.Desktop.Controls;
using Animora.Desktop.Services;
using Animora.Desktop.Shell;
using Animora.Domain.Identity;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Pages.Statistics;

/// <summary>One segmented tab of the page.</summary>
public sealed partial class StatisticsTabViewModel(StatisticsSection section, string label) : ObservableObject
{
    public StatisticsSection Section { get; } = section;

    public string Label { get; } = label;

    [ObservableProperty]
    private bool _isActive;
}

/// <summary>
/// ViewModel of the «آمار» page: an internal section switcher (overview / clients / revenue &amp; sales / expenses) over one
/// shared <see cref="StatisticsRange"/>. Only the visible section loads; a section re-queries only when the range or its
/// data actually changed, and a stale response (older generation) never overwrites a newer one. Expenses are shown only
/// to a viewer with <c>finance.read</c>.
/// </summary>
public sealed partial class StatisticsPageViewModel : PageViewModelBase
{
    private readonly IStoreReadinessGate _readiness;
    private readonly IToastService _toast;
    private readonly Dictionary<StatisticsSection, StatisticsSectionViewModel> _sections;
    private CancellationTokenSource? _loadCts;
    private int _generation;
    private StatisticsRange _range = new();

    public StatisticsPageViewModel(IStoreReadinessGate readiness, IToastService toast, IDataChangeNotifier changes,
        ICapabilityGate capabilities, StatisticsOverviewViewModel overview, StatisticsClientsViewModel clients,
        StatisticsSalesViewModel sales, StatisticsExpensesViewModel expenses)
    {
        _readiness = readiness;
        _toast = toast;

        Title = "آمار";
        Eyebrow = "تحلیل کسب و کار";
        Breadcrumbs.Add("کسب و کار");
        Breadcrumbs.Add("آمار");

        Overview = overview;
        Clients = clients;
        Sales = sales;
        Expenses = expenses;
        _sections = new StatisticsSectionViewModel[] { overview, clients, sales, expenses }.ToDictionary(s => s.Section);

        // «هزینه‌ها» reads the ledger, which needs finance.read on top of the page's reports.read gate.
        var visible = new List<StatisticsSectionViewModel> { overview, clients, sales };
        if (capabilities.Can(PermissionCodes.FinanceRead)) visible.Add(expenses);
        Tabs.AddRange(visible.Select(s => new StatisticsTabViewModel(s.Section, s.Label)));

        SelectSection(StatisticsSection.Overview, load: false);
        UseLoadStatus(() => ReloadAsync(force: true));
        // Any write that moves revenue / expenses / clients announces its domain: a shown page re-reads at once, a
        // hidden one on its next visit (OnNavigatedTo always re-checks the current section).
        RefreshOnDataChange(changes, () => _ = ReloadAsync(force: true),
            DataDomain.Sales, DataDomain.Cashier, DataDomain.Finance, DataDomain.Patients, DataDomain.Visits,
            DataDomain.Boarding, DataDomain.Cheques);
    }

    public StatisticsOverviewViewModel Overview { get; }

    public StatisticsClientsViewModel Clients { get; }

    public StatisticsSalesViewModel Sales { get; }

    public StatisticsExpensesViewModel Expenses { get; }

    public RangeObservableCollection<StatisticsTabViewModel> Tabs { get; } = [];

    /// <summary>The section currently shown (resolved to its view by the shell's view locator).</summary>
    [ObservableProperty]
    private StatisticsSectionViewModel _currentSection = null!;

    /// <summary>The page-wide window every section reads.</summary>
    public StatisticsRange Range => _range;

    // Singleton page: re-check on every visit; a section already current for the range is not re-queried.
    public override void OnNavigatedTo()
    {
        if (!IsLoadInFlight) _ = ReloadAsync(force: false);
    }

    public override void OnNavigatedFrom() => CancelPending();

    /// <summary>Applies a new window; sections other than the visible one reload lazily when opened.</summary>
    public void SetRange(StatisticsRange range)
    {
        ArgumentNullException.ThrowIfNull(range);
        if (range == _range) return;
        _range = range;
        OnPropertyChanged(nameof(Range));
        _ = ReloadAsync(force: false);
    }

    [RelayCommand]
    private void ShowSection(StatisticsSection section)
    {
        if (CurrentSection.Section == section) return;
        SelectSection(section, load: true);
    }

    /// <summary>Retry of the visible section after a failure (the error banner's button).</summary>
    [RelayCommand]
    private Task RetryAsync() => ReloadAsync(force: true);

    private void SelectSection(StatisticsSection section, bool load)
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
            // A newer reload superseded this one: its result owns the page state, so this one reports success quietly.
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
