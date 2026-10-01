using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Animora.Contracts.Dashboard;
using Animora.Contracts.Diagnostics;
using Animora.Contracts.Identity;
using Animora.Contracts.Navigation;
using Animora.Desktop.Controls;
using Animora.Desktop.Pages.Patients;
using Animora.Desktop.Services;
using Animora.Desktop.Shell;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Pages.Dashboard;

/// <summary>
/// Dashboard screen ViewModel (Phase 06). Loads the current user's
/// role-specific snapshot from <see cref="IDashboardQueryService"/> and
/// exposes it as bindable collections shared by the three role sub-views
/// (Manager/Cashier/Doctor) — a single ViewModel, per CONVENTIONS.md §3/§5.
/// </summary>
public sealed partial class DashboardPageViewModel : PageViewModelBase
{
    private readonly INavigationService _navigation;
    private readonly IDashboardQueryService _dashboard;
    private readonly IStoreReadinessGate _readiness;
    private readonly ICurrentUserContext _currentUser;
    private readonly Avalonia.Threading.DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromMinutes(1) };
    private string _identityKey;

    private string _primaryListTitle = string.Empty;
    public string PrimaryListTitle { get => _primaryListTitle; private set => SetProperty(ref _primaryListTitle, value); }

    private string _secondaryListTitle = string.Empty;
    public string SecondaryListTitle { get => _secondaryListTitle; private set => SetProperty(ref _secondaryListTitle, value); }

    public DashboardPageViewModel(IDashboardQueryService dashboard, IStoreReadinessGate readiness, ICurrentUserContext currentUser, INavigationService navigation)
    {
        _navigation = navigation;
        _dashboard = dashboard;
        _readiness = readiness;
        _currentUser = currentUser;
        Role = currentUser.Role;
        _identityKey = IdentityKey(currentUser);
        _refresh = new DashboardRefreshCoordinator<PreparedDashboard>(FetchAsync, Prepare, Publish, OnRefreshFailed, OnRefreshSucceeded);
        _refresh.StateChanged += OnRefreshStateChanged;
        _refreshTimer.Tick += OnRefreshTick;

        Title = "داشبورد";
        Eyebrow = "مرکز فرمان";
        Breadcrumbs.Add("داشبورد");

        // Load on activation, not DI construction; singleton pages also refresh on return.
        // Database work is awaited without blocking the dispatcher (PERFORMANCE.md §4/§12).

    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ViewState), nameof(StateText), nameof(IsStateVisible), nameof(IsStale), nameof(IsInitialLoading))]
    private string? _loadError;

    // Explicit, mutually exclusive load state (D28). Zero values only appear once HasData is true.
    public bool HasData => _presentation is { IsPlaceholder: false };

    public DashboardViewState ViewState => DashboardViewStateResolver.Resolve(
        HasData, _refresh.IsBusy, !string.IsNullOrEmpty(LoadError), _presentation?.Snapshot);

    public string StateText => DashboardViewStateResolver.Describe(ViewState);
    public bool IsStateVisible => ViewState != DashboardViewState.Ready;
    public bool IsStale => ViewState == DashboardViewState.Stale;
    public bool IsInitialLoading => ViewState == DashboardViewState.InitialLoading;
    public bool ShowPrimaryEmpty => HasData && PrimaryList.Count == 0;
    public bool ShowSecondaryEmpty => HasData && SecondaryList.Count == 0;
    public bool ShowSpeciesEmpty => HasData && Species.Count == 0;
    public bool ShowDoctorLoadsEmpty => HasData && DoctorLoads.Count == 0;
    public string EmptyListText => "موردی برای نمایش وجود ندارد.";

    private void NotifyViewState()
    {
        OnPropertyChanged(nameof(ViewState));
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(IsStateVisible));
        OnPropertyChanged(nameof(IsStale));
        OnPropertyChanged(nameof(IsInitialLoading));
    }

    private readonly DashboardRefreshCoordinator<PreparedDashboard> _refresh;

    // Visit lifecycle: activation supersedes any in-flight load and always ends in a refresh; leaving the page
    // cancels the load through the token down to the database, and its late result is never applied.
    public override void OnNavigatedTo()
    {
        SyncIdentity();
        _ = _refresh.ActivateAsync();
        _refreshTimer.Start();
    }

    private static string IdentityKey(ICurrentUserContext user) => $"{user.UserId}|{user.Role}";

    // The page is a singleton, so the signed-in identity can change underneath it (sign-out/in as another user).
    // A different identity/role resets every piece of state of the previous one before anything is loaded.
    private void SyncIdentity()
    {
        var key = IdentityKey(_currentUser);
        if (key == _identityKey) return;
        _identityKey = key;
        _refresh.Reset();
        ClearPresentation();
        Role = _currentUser.Role;
    }

    private void ClearPresentation()
    {
        LoadError = null;
        Publish(Prepare(new DashboardSnapshot(Role, "", "", [], "", [], "", [])) with { IsPlaceholder = true });
    }

    public override void OnNavigatedFrom()
    {
        _refreshTimer.Stop();
        _refresh.Deactivate();
    }

    private void OnRefreshTick(object? sender, EventArgs args) =>
        _ = _refresh.TickAsync(DateTimeOffset.UtcNow);

    [RelayCommand]
    private Task RefreshAsync() => _refresh.RefreshAsync(force: true);

    private async Task<DashboardSnapshot> FetchAsync(CancellationToken ct, bool force)
    {
        await _readiness.WaitAsync(ct);
        return force ? await _dashboard.RefreshSnapshotAsync(Role, ct) : await _dashboard.GetSnapshotAsync(Role, ct);
    }

    public string LastUpdatedText => _refresh.LastUpdated is { } at
        ? "آخرین دریافت: " + Localization.PersianNumberFormatter.ToPersianDigits(at.ToLocalTime().ToString("HH:mm"))
        : "اطلاعات هنوز دریافت نشده است";

    private void OnRefreshStateChanged()
    {
        IsBusy = _refresh.IsBusy;
        OnPropertyChanged(nameof(LastUpdatedText));
        if (IsBusy && !HasData) LoadError = null; // with data, the failure stays visible until the retry succeeds
        NotifyViewState();
    }

    // A successful load (including a cache hit that republishes nothing) ends a stale/error condition.
    private void OnRefreshSucceeded()
    {
        LoadError = null;
        NotifyViewState();
    }

    public bool CanRetry { get; private set; } = true;

    private void OnRefreshFailed(Exception exception)
    {
        var failure = exception as DashboardQueryException;
        if (failure?.Kind is DashboardFailureKind.SessionExpired or DashboardFailureKind.PermissionDenied)
        {
            _refresh.Reset();
            ClearPresentation(); // Revocation must not leave previously authorized data visible.
        }
        CanRetry = failure?.CanRetry ?? false;
        OnPropertyChanged(nameof(CanRetry));
        LoadError = failure is null ? "خطای غیرمنتظره در داشبورد؛ با پشتیبانی تماس بگیرید."
            : failure.UserMessage + " شناسه پیگیری: " + failure.CorrelationId;
    }

    /// <summary>Presentation layout role of the current identity; changes (and notifies) when the identity changes.</summary>
    [ObservableProperty]
    private UserRole _role;

    public IReadOnlyList<DashboardStat> Stats => _presentation?.Stats ?? Array.Empty<DashboardStat>();

    public IReadOnlyList<DashboardRowViewModel> PrimaryList => _presentation?.PrimaryList ?? Array.Empty<DashboardRowViewModel>();

    public IReadOnlyList<DashboardRowViewModel> SecondaryList => _presentation?.SecondaryList ?? Array.Empty<DashboardRowViewModel>();

    /// <summary>Manager-only: the doctor-workload rows.</summary>
    public IReadOnlyList<DashboardDoctorLoadViewModel> DoctorLoads => _presentation?.DoctorLoads ?? Array.Empty<DashboardDoctorLoadViewModel>();

    /// <summary>Manager-only: the species donut legend rows.</summary>
    public IReadOnlyList<DashboardSpeciesViewModel> Species => _presentation?.Species ?? Array.Empty<DashboardSpeciesViewModel>();

    private DashboardRevenuePulse? _revenuePulse;
    public DashboardRevenuePulse? RevenuePulse { get => _revenuePulse; private set => SetProperty(ref _revenuePulse, value); }

    private bool _hasDoctorWorkload;
    public bool HasDoctorWorkload { get => _hasDoctorWorkload; private set => SetProperty(ref _hasDoctorWorkload, value); }

    private bool _hasSpecies;
    public bool HasSpecies { get => _hasSpecies; private set => SetProperty(ref _hasSpecies, value); }

    /// <summary>Filled area under the revenue line (chart background).</summary>
    private Avalonia.Media.Geometry? _revenueAreaGeometry;
    public Avalonia.Media.Geometry? RevenueAreaGeometry { get => _revenueAreaGeometry; private set => SetProperty(ref _revenueAreaGeometry, value); }

    /// <summary>Explicit zero baseline so negative days are visible against it.</summary>
    private Avalonia.Media.Geometry? _revenueZeroLineGeometry;
    public Avalonia.Media.Geometry? RevenueZeroLineGeometry { get => _revenueZeroLineGeometry; private set => SetProperty(ref _revenueZeroLineGeometry, value); }

    /// <summary>One line per plotted day: Jalali date and the real signed amount (chart tooltip).</summary>
    private string _revenueTooltip = string.Empty;
    public string RevenueTooltip { get => _revenueTooltip; private set => SetProperty(ref _revenueTooltip, value); }

    /// <summary>The revenue trend line itself.</summary>
    private Avalonia.Media.Geometry? _revenueLineGeometry;
    public Avalonia.Media.Geometry? RevenueLineGeometry { get => _revenueLineGeometry; private set => SetProperty(ref _revenueLineGeometry, value); }

    /// <summary>Center value of the species donut — total active patients.</summary>
    private string _speciesTotal = string.Empty;
    public string SpeciesTotal { get => _speciesTotal; private set => SetProperty(ref _speciesTotal, value); }

    /// <summary>Center caption of the species donut — "کل بیماران فعال" by default.</summary>
    private string _speciesTotalLabel = "کل بیماران فعال";
    public string SpeciesTotalLabel { get => _speciesTotalLabel; private set => SetProperty(ref _speciesTotalLabel, value); }

    /// <summary>Center sub-caption of the species donut — the registered species count.</summary>
    private string _speciesTotalSub = string.Empty;
    public string SpeciesTotalSub { get => _speciesTotalSub; private set => SetProperty(ref _speciesTotalSub, value); }

    /// <summary>Accent colour of the center value; reverts to ink on blur.</summary>
    private Avalonia.Media.IBrush? _speciesCenterBrush;
    public Avalonia.Media.IBrush? SpeciesCenterBrush { get => _speciesCenterBrush; private set => SetProperty(ref _speciesCenterBrush, value); }

    /// <summary>Dims the ring/legend while a slice is focused (reference .donut-wrap.dim).</summary>
    private bool _isSpeciesDimmed;
    public bool IsSpeciesDimmed { get => _isSpeciesDimmed; private set => SetProperty(ref _isSpeciesDimmed, value); }

    /// <summary>Geometry describing the donut ring's coloured slices.</summary>
    private System.Collections.Generic.IReadOnlyList<Animora.Desktop.Controls.DonutSlice> _speciesSlices =
        System.Array.Empty<Animora.Desktop.Controls.DonutSlice>();
    public System.Collections.Generic.IReadOnlyList<Animora.Desktop.Controls.DonutSlice> SpeciesSlices { get => _speciesSlices; private set => SetProperty(ref _speciesSlices, value); }

    /// <summary>Index of the hovered slice (-1 = none), for the ring's focus state.</summary>
    private int _speciesActiveIndex = -1;
    public int SpeciesActiveIndex { get => _speciesActiveIndex; private set => SetProperty(ref _speciesActiveIndex, value); }

    /// <summary>The species donut's default center readout, restored on blur.</summary>
    private string _speciesBaseTotal = string.Empty;

    private string _speciesBaseSub = string.Empty;

    public IReadOnlyList<DashboardQuickAction> QuickActions { get; } = DashboardQuickAction.All;

    [RelayCommand]
    private void OpenQuickAction(DashboardQuickAction? action)
    {
        if (action is null)
        {
            return;
        }

        _navigation.NavigateTo(action.Target);
    }

    /// <summary>
    /// Focus a species slice (hover over the ring or its legend row): dims the
    /// other slices and swaps the center readout, mirroring the reference's
    /// <c>focus()</c> in Animora-UI/assets/dashboard.js.
    /// </summary>
    [RelayCommand]
    private void FocusSpecies(int index)
    {
        if (index < 0 || index >= Species.Count || SpeciesActiveIndex == index)
        {
            return;
        }

        IsSpeciesDimmed = true;
        SpeciesActiveIndex = index;
        for (var i = 0; i < Species.Count; i++)
        {
            Species[i].IsActive = i == index;
        }

        var row = Species[index];
        SpeciesTotal = row.CountText;
        SpeciesTotalLabel = row.Name;
        SpeciesTotalSub = row.PercentText + " از کل بیماران";
        SpeciesCenterBrush = row.SwatchBrush;
    }

    /// <summary>Restore the donut's default readout when hover leaves.</summary>
    [RelayCommand]
    private void BlurSpecies()
    {
        if (!IsSpeciesDimmed && SpeciesActiveIndex == -1) return;
        IsSpeciesDimmed = false;
        SpeciesActiveIndex = -1;
        foreach (var row in Species)
        {
            row.IsActive = false;
        }

        SpeciesTotal = _speciesBaseTotal;
        SpeciesTotalLabel = "کل بیماران فعال";
        SpeciesTotalSub = _speciesBaseSub;
        SpeciesCenterBrush = null;
    }

    /// <summary>Navigate to the patients directory (donut drill-in).</summary>
    [RelayCommand]
    private void OpenSpeciesPatients() => _navigation.NavigateTo<PatientsPageViewModel>();

    // All allocations, parsing and formatting happen before any observable state is touched.
    private sealed record PreparedDashboard(DashboardSnapshot Snapshot, IReadOnlyList<DashboardStat> Stats,
        IReadOnlyList<DashboardRowViewModel> PrimaryList, IReadOnlyList<DashboardRowViewModel> SecondaryList,
        IReadOnlyList<DashboardDoctorLoadViewModel> DoctorLoads, IReadOnlyList<DashboardSpeciesViewModel> Species,
        ChartGeometry Chart, string Tooltip, string Total, string Sub,
        IReadOnlyList<DonutSlice> Slices)
    {
        /// <summary>True for the blank state published on identity reset/revocation: not data, so no zeros are shown.</summary>
        public bool IsPlaceholder { get; init; }
    }
    private sealed record ChartGeometry(Avalonia.Media.Geometry? Area, Avalonia.Media.Geometry? Line,
        Avalonia.Media.Geometry? Zero);
    private PreparedDashboard? _presentation;

    private static PreparedDashboard Prepare(DashboardSnapshot snapshot)
    {
        var species = snapshot.Species ?? Array.Empty<DashboardSpecies>();
        var total = species.Sum(s => s.Count);
        var rows = species.Select((s, i) => new DashboardSpeciesViewModel(i, s,
            total <= 0 ? 0 : (double)s.Count / total)).ToArray();
        return new PreparedDashboard(snapshot, snapshot.Stats,
            snapshot.PrimaryList.Select(x => new DashboardRowViewModel(x)).ToArray(),
            snapshot.SecondaryList.Select(x => new DashboardRowViewModel(x)).ToArray(),
            (snapshot.DoctorLoads ?? Array.Empty<DashboardDoctorLoad>()).Select(x => new DashboardDoctorLoadViewModel(x)).ToArray(),
            rows, BuildRevenueChart(snapshot.RevenuePulse), RevenueTooltipText(snapshot.RevenuePulse),
            Localization.PersianNumberFormatter.FormatGrouped(total),
            Localization.PersianNumberFormatter.ToPersianDigits(species.Count.ToString(System.Globalization.CultureInfo.InvariantCulture))
                + " گونه ثبت‌شده", BuildSpeciesSlices(species));
    }

    private void Publish(PreparedDashboard next)
    {
        // The coordinator resumes on the UI dispatcher. Update backing state first, then ONE reset notification:
        // every observer reads a complete new state, never a mix of old rows and new chart/species.
        _presentation = next;
        _primaryListTitle = next.Snapshot.PrimaryListTitle;
        _secondaryListTitle = next.Snapshot.SecondaryListTitle;
        _hasDoctorWorkload = next.Snapshot.DoctorLoads is not null;
        _hasSpecies = next.Snapshot.Species is not null;
        _revenuePulse = next.Snapshot.RevenuePulse;
        _revenueAreaGeometry = next.Chart.Area;
        _revenueLineGeometry = next.Chart.Line;
        _revenueZeroLineGeometry = next.Chart.Zero;
        _revenueTooltip = next.Tooltip;
        _speciesBaseTotal = _speciesTotal = next.Total;
        _speciesBaseSub = _speciesTotalSub = next.Sub;
        _speciesTotalLabel = "کل بیماران فعال";
        _speciesCenterBrush = null;
        _isSpeciesDimmed = false;
        _speciesActiveIndex = -1;
        _speciesSlices = next.Slices;
        OnPropertyChanged(string.Empty);
    }

    private static string RevenueTooltipText(DashboardRevenuePulse? pulse)
    {
        if (pulse is null || pulse.Values.Count == 0 || pulse.Values.Count != pulse.DayLabels.Count) return string.Empty;
        var lines = new string[pulse.Values.Count];
        for (var i = 0; i < lines.Length; i++)
            lines[i] = Localization.PersianNumberFormatter.ToPersianDigits(
                pulse.DayLabels[i] + ": " + pulse.Values[i].ToString("N0", System.Globalization.CultureInfo.InvariantCulture)) + " تومان";
        return string.Join('\n', lines);
    }

    // Builds the area + line + zero-line geometry from the signed, zero-containing pulse domain. The area closes
    // to the zero baseline (not the canvas bottom), so negative days visibly fall below it.
    private static ChartGeometry BuildRevenueChart(DashboardRevenuePulse? pulse)
    {
        var layout = RevenueChartMath.Build(pulse?.Points, pulse?.ZeroLevel ?? 0);
        if (layout is not { } chart)
        {
            return new(null, null, null);
        }

        var count = chart.X.Length;
        // Packed immutable paths avoid a managed PathFigure/LineSegment object per point (§11).
        var line = new Avalonia.Media.StreamGeometry();
        using (var context = line.Open())
        {
            context.BeginFigure(new Avalonia.Point(chart.X[0], chart.Y[0]), false);
            for (var i = 1; i < count; i++) context.LineTo(new Avalonia.Point(chart.X[i], chart.Y[i]));
            context.EndFigure(false);
        }


        var area = new Avalonia.Media.StreamGeometry();
        using (var context = area.Open())
        {
            context.BeginFigure(new Avalonia.Point(chart.X[0], chart.ZeroY), true);
            for (var i = 0; i < count; i++) context.LineTo(new Avalonia.Point(chart.X[i], chart.Y[i]));
            context.LineTo(new Avalonia.Point(chart.X[count - 1], chart.ZeroY));
            context.EndFigure(true);
        }


        var zero = new Avalonia.Media.StreamGeometry();
        using (var context = zero.Open())
        {
            context.BeginFigure(new Avalonia.Point(0, chart.ZeroY), false);
            context.LineTo(new Avalonia.Point(RevenueChartMath.Width, chart.ZeroY));
            context.EndFigure(false);
        }
        return new(area, line, zero);
    }

    // Maps the species snapshot to the immutable slice records the ring control
    // consumes (value + HTML hex colour), preserving the snapshot order.
    private static System.Collections.Generic.IReadOnlyList<Animora.Desktop.Controls.DonutSlice> BuildSpeciesSlices(
        IReadOnlyList<DashboardSpecies> species)
    {
        var slices = new System.Collections.Generic.List<Animora.Desktop.Controls.DonutSlice>(species.Count);
        foreach (var s in species)
        {
            slices.Add(new Animora.Desktop.Controls.DonutSlice(s.Count,
                string.IsNullOrEmpty(s.Key) ? s.ColorHex : SpeciesPresentation.For(s.Key, s.Name).ColorHex));
        }

        return slices;
    }
}
