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

    [ObservableProperty]
    private string _primaryListTitle = string.Empty;

    [ObservableProperty]
    private string _secondaryListTitle = string.Empty;

    public DashboardPageViewModel(IDashboardQueryService dashboard, IStoreReadinessGate readiness, ICurrentUserContext currentUser, INavigationService navigation)
    {
        _navigation = navigation;
        _dashboard = dashboard;
        _readiness = readiness;
        _currentUser = currentUser;
        Role = currentUser.Role;
        _identityKey = IdentityKey(currentUser);
        _refresh = new DashboardRefreshCoordinator<DashboardSnapshot>(FetchAsync, snapshot => snapshot, Apply, OnRefreshFailed);
        _refresh.StateChanged += OnRefreshStateChanged;
        _refreshTimer.Tick += OnRefreshTick;

        Title = "داشبورد";
        Eyebrow = "مرکز فرمان";
        Breadcrumbs.Add("داشبورد");

        // Load on activation, not DI construction; singleton pages also refresh on return.
        // Database work is awaited without blocking the dispatcher (PERFORMANCE.md §4/§12).

    }

    [ObservableProperty]
    private string? _loadError;

    private readonly DashboardRefreshCoordinator<DashboardSnapshot> _refresh;

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
        Stats.ReplaceAll([]);
        PrimaryList.ReplaceAll([]);
        SecondaryList.ReplaceAll([]);
        DoctorLoads.ReplaceAll([]);
        Species.ReplaceAll([]);
        PrimaryListTitle = string.Empty;
        SecondaryListTitle = string.Empty;
        HasDoctorWorkload = false;
        HasSpecies = false;
        RevenuePulse = null;
        RevenueAreaGeometry = null;
        RevenueLineGeometry = null;
        RevenueZeroLineGeometry = null;
        RevenueTooltip = string.Empty;
        SpeciesSlices = System.Array.Empty<Animora.Desktop.Controls.DonutSlice>();
        SpeciesTotal = string.Empty;
        SpeciesTotalSub = string.Empty;
        SpeciesActiveIndex = -1;
        IsSpeciesDimmed = false;
        _speciesBaseTotal = string.Empty;
        _speciesBaseSub = string.Empty;
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
        if (IsBusy) LoadError = null;
    }

    private void OnRefreshFailed(Exception exception) =>
        LoadError = "بارگذاری داشبورد انجام نشد؛ دوباره تلاش کنید";

    /// <summary>Presentation layout role of the current identity; changes (and notifies) when the identity changes.</summary>
    [ObservableProperty]
    private UserRole _role;

    public RangeObservableCollection<DashboardStat> Stats { get; } = new();

    public RangeObservableCollection<DashboardRowViewModel> PrimaryList { get; } = new();

    public RangeObservableCollection<DashboardRowViewModel> SecondaryList { get; } = new();

    /// <summary>Manager-only: the doctor-workload rows.</summary>
    public RangeObservableCollection<DashboardDoctorLoadViewModel> DoctorLoads { get; } = new();

    /// <summary>Manager-only: the species donut legend rows.</summary>
    public RangeObservableCollection<DashboardSpeciesViewModel> Species { get; } = new();

    [ObservableProperty]
    private DashboardRevenuePulse? _revenuePulse;

    [ObservableProperty]
    private bool _hasDoctorWorkload;

    [ObservableProperty]
    private bool _hasSpecies;

    /// <summary>Filled area under the revenue line (chart background).</summary>
    [ObservableProperty]
    private Avalonia.Media.Geometry? _revenueAreaGeometry;

    /// <summary>Explicit zero baseline so negative days are visible against it.</summary>
    [ObservableProperty]
    private Avalonia.Media.Geometry? _revenueZeroLineGeometry;

    /// <summary>One line per plotted day: Jalali date and the real signed amount (chart tooltip).</summary>
    [ObservableProperty]
    private string _revenueTooltip = string.Empty;

    /// <summary>The revenue trend line itself.</summary>
    [ObservableProperty]
    private Avalonia.Media.Geometry? _revenueLineGeometry;

    /// <summary>Center value of the species donut — total active patients.</summary>
    [ObservableProperty]
    private string _speciesTotal = string.Empty;

    /// <summary>Center caption of the species donut — "کل بیماران فعال" by default.</summary>
    [ObservableProperty]
    private string _speciesTotalLabel = "کل بیماران فعال";

    /// <summary>Center sub-caption of the species donut — the registered species count.</summary>
    [ObservableProperty]
    private string _speciesTotalSub = string.Empty;

    /// <summary>Accent colour of the center value; reverts to ink on blur.</summary>
    [ObservableProperty]
    private Avalonia.Media.IBrush? _speciesCenterBrush;

    /// <summary>Dims the ring/legend while a slice is focused (reference .donut-wrap.dim).</summary>
    [ObservableProperty]
    private bool _isSpeciesDimmed;

    /// <summary>Geometry describing the donut ring's coloured slices.</summary>
    [ObservableProperty]
    private System.Collections.Generic.IReadOnlyList<Animora.Desktop.Controls.DonutSlice> _speciesSlices =
        System.Array.Empty<Animora.Desktop.Controls.DonutSlice>();

    /// <summary>Index of the hovered slice (-1 = none), for the ring's focus state.</summary>
    [ObservableProperty]
    private int _speciesActiveIndex = -1;

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

    private void Apply(DashboardSnapshot snapshot)
    {
        PrimaryListTitle = snapshot.PrimaryListTitle;
        SecondaryListTitle = snapshot.SecondaryListTitle;

        Stats.ReplaceAll(snapshot.Stats);

        PrimaryList.ReplaceAll(
            snapshot.PrimaryList.Select(x => new DashboardRowViewModel(x)));

        SecondaryList.ReplaceAll(
            snapshot.SecondaryList.Select(x => new DashboardRowViewModel(x)));

        HasDoctorWorkload = snapshot.DoctorLoads is not null;
        HasSpecies = snapshot.Species is not null;
        RevenuePulse = snapshot.RevenuePulse;
        BuildRevenueChart(snapshot.RevenuePulse);
        RevenueTooltip = RevenueTooltipText(snapshot.RevenuePulse);

        DoctorLoads.ReplaceAll(
            (snapshot.DoctorLoads ?? System.Array.Empty<DashboardDoctorLoad>())
                .Select(doc => new DashboardDoctorLoadViewModel(doc)));

        ApplySpecies(snapshot.Species ?? System.Array.Empty<DashboardSpecies>());
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
    private void BuildRevenueChart(DashboardRevenuePulse? pulse)
    {
        var layout = RevenueChartMath.Build(pulse?.Points, pulse?.ZeroLevel ?? 0);
        if (layout is not { } chart)
        {
            RevenueAreaGeometry = null;
            RevenueLineGeometry = null;
            RevenueZeroLineGeometry = null;
            return;
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
        RevenueLineGeometry = line;

        var area = new Avalonia.Media.StreamGeometry();
        using (var context = area.Open())
        {
            context.BeginFigure(new Avalonia.Point(chart.X[0], chart.ZeroY), true);
            for (var i = 0; i < count; i++) context.LineTo(new Avalonia.Point(chart.X[i], chart.Y[i]));
            context.LineTo(new Avalonia.Point(chart.X[count - 1], chart.ZeroY));
            context.EndFigure(true);
        }
        RevenueAreaGeometry = area;

        var zero = new Avalonia.Media.StreamGeometry();
        using (var context = zero.Open())
        {
            context.BeginFigure(new Avalonia.Point(0, chart.ZeroY), false);
            context.LineTo(new Avalonia.Point(RevenueChartMath.Width, chart.ZeroY));
            context.EndFigure(false);
        }
        RevenueZeroLineGeometry = zero;
    }

    private void ApplySpecies(IReadOnlyList<DashboardSpecies> species)
    {
        var rows = new DashboardSpeciesViewModel[species.Count];
        var total = species.Sum(s => s.Count);
        for (var i = 0; i < species.Count; i++)
        {
            var s = species[i];
            var pct = total <= 0 ? 0 : (double)s.Count / total;
            rows[i] = new DashboardSpeciesViewModel(i, s, pct);
        }
        Species.ReplaceAll(rows);

        _speciesBaseTotal = Localization.PersianNumberFormatter.FormatGrouped(total);
        _speciesBaseSub = Localization.PersianNumberFormatter.ToPersianDigits(
            species.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)) + " گونه ثبت‌شده";

        SpeciesTotal = _speciesBaseTotal;
        SpeciesTotalLabel = "کل بیماران فعال";
        SpeciesTotalSub = _speciesBaseSub;
        SpeciesCenterBrush = Controls.BrushCache.Solid("#101828");
        IsSpeciesDimmed = false;
        SpeciesActiveIndex = -1;
        SpeciesSlices = BuildSpeciesSlices(species);
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
