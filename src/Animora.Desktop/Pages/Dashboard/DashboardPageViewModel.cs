using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Animora.Contracts.Dashboard;
using Animora.Contracts.Identity;
using Animora.Contracts.Navigation;
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

    [ObservableProperty]
    private string _heroTitle = string.Empty;

    [ObservableProperty]
    private string _heroSubtitle = string.Empty;

    [ObservableProperty]
    private string _primaryListTitle = string.Empty;

    [ObservableProperty]
    private string _secondaryListTitle = string.Empty;

    public DashboardPageViewModel(IDashboardQueryService dashboard, ICurrentUserContext currentUser, INavigationService navigation)
    {
        _navigation = navigation;
        Role = currentUser.Role;

        Title = "داشبورد";
        Eyebrow = "مرکز فرمان";
        Breadcrumbs.Add("داشبورد");

        // The mock resolves synchronously (Task.FromResult); a future
        // Datasync-backed implementation would call this from an async
        // OnLoaded hook instead — no ViewModel shape change needed then.
        var snapshot = dashboard.GetSnapshotAsync(Role).GetAwaiter().GetResult();
        Apply(snapshot);
    }

    public UserRole Role { get; }

    public bool IsManager => Role == UserRole.Manager;

    public bool IsCashier => Role == UserRole.Cashier;

    public bool IsDoctor => Role == UserRole.Doctor;

    public ObservableCollection<DashboardStat> Stats { get; } = new();

    public ObservableCollection<DashboardRowViewModel> PrimaryList { get; } = new();

    public ObservableCollection<DashboardRowViewModel> SecondaryList { get; } = new();

    /// <summary>Manager-only: the doctor-workload rows.</summary>
    public ObservableCollection<DashboardDoctorLoadViewModel> DoctorLoads { get; } = new();

    /// <summary>Manager-only: the species donut legend rows.</summary>
    public ObservableCollection<DashboardSpeciesViewModel> Species { get; } = new();

    [ObservableProperty]
    private DashboardRevenuePulse? _revenuePulse;

    /// <summary>Filled area under the revenue line (chart background).</summary>
    [ObservableProperty]
    private Avalonia.Media.Geometry? _revenueAreaGeometry;

    /// <summary>The revenue trend line itself.</summary>
    [ObservableProperty]
    private Avalonia.Media.Geometry? _revenueLineGeometry;

    /// <summary>Center label of the species donut — total active patients.</summary>
    [ObservableProperty]
    private string _speciesTotal = string.Empty;

    /// <summary>Geometry describing the donut ring's coloured slices.</summary>
    [ObservableProperty]
    private System.Collections.Generic.IReadOnlyList<DonutSliceViewModel> _speciesSlices =
        System.Array.Empty<DonutSliceViewModel>();

    public IReadOnlyList<DashboardQuickAction> QuickActions { get; } = DashboardQuickAction.All;

    /// <summary>Hero CTA target — mirrors the reference's most prominent quick tile ("شروع ویزیت").</summary>
    public DashboardQuickAction PrimaryAction => QuickActions[2];

    [RelayCommand]
    private void OpenQuickAction(DashboardQuickAction? action)
    {
        if (action is null)
        {
            return;
        }

        var page = _navigation.NavigateTo(action.Target);
        if (page is PlaceholderPageViewModel placeholder)
        {
            placeholder.ConfigureFor(action.Label);
        }
    }

    private void Apply(DashboardSnapshot snapshot)
    {
        HeroTitle = snapshot.HeroTitle;
        HeroSubtitle = snapshot.HeroSubtitle;
        PrimaryListTitle = snapshot.PrimaryListTitle;
        SecondaryListTitle = snapshot.SecondaryListTitle;

        Stats.Clear();
        foreach (var stat in snapshot.Stats)
        {
            Stats.Add(stat);
        }

        PrimaryList.Clear();
        foreach (var item in snapshot.PrimaryList.Select(x => new DashboardRowViewModel(x)))
        {
            PrimaryList.Add(item);
        }

        SecondaryList.Clear();
        foreach (var item in snapshot.SecondaryList.Select(x => new DashboardRowViewModel(x)))
        {
            SecondaryList.Add(item);
        }

        RevenuePulse = snapshot.RevenuePulse;
        BuildRevenueChart(snapshot.RevenuePulse?.Points);

        DoctorLoads.Clear();
        foreach (var doc in snapshot.DoctorLoads ?? System.Array.Empty<DashboardDoctorLoad>())
        {
            DoctorLoads.Add(new DashboardDoctorLoadViewModel(doc));
        }

        ApplySpecies(snapshot.Species ?? System.Array.Empty<DashboardSpecies>());
    }

    // Builds the area + line geometry from the normalized 0..1 pulse series,
    // over a 500×180 canvas matching the reference pulseChart viewBox.
    private void BuildRevenueChart(IReadOnlyList<double>? points)
    {
        if (points is null || points.Count < 2)
        {
            RevenueAreaGeometry = null;
            RevenueLineGeometry = null;
            return;
        }

        const double width = 500;
        const double height = 180;
        const double top = 12;
        const double bottom = height - 12;

        var step = width / (points.Count - 1);
        var pts = new Avalonia.Point[points.Count];
        for (var i = 0; i < points.Count; i++)
        {
            var clamped = points[i] < 0 ? 0 : points[i] > 1 ? 1 : points[i];
            var y = bottom - clamped * (bottom - top);
            pts[i] = new Avalonia.Point(i * step, y);
        }

        var line = new Avalonia.Media.PathGeometry();
        var lineFig = new Avalonia.Media.PathFigure
        {
            StartPoint = pts[0],
            IsClosed = false,
            IsFilled = false,
        };
        for (var i = 1; i < pts.Length; i++)
        {
            lineFig.Segments!.Add(new Avalonia.Media.LineSegment { Point = pts[i] });
        }

        line.Figures!.Add(lineFig);
        RevenueLineGeometry = line;

        var area = new Avalonia.Media.PathGeometry();
        var areaFig = new Avalonia.Media.PathFigure
        {
            StartPoint = new Avalonia.Point(0, bottom),
            IsClosed = true,
            IsFilled = true,
        };
        foreach (var p in pts)
        {
            areaFig.Segments!.Add(new Avalonia.Media.LineSegment { Point = p });
        }

        areaFig.Segments!.Add(new Avalonia.Media.LineSegment { Point = new Avalonia.Point(width, bottom) });
        area.Figures!.Add(areaFig);
        RevenueAreaGeometry = area;
    }

    private void ApplySpecies(IReadOnlyList<DashboardSpecies> species)
    {
        Species.Clear();
        var total = species.Sum(s => s.Count);
        foreach (var s in species)
        {
            var pct = total <= 0 ? 0 : (double)s.Count / total;
            Species.Add(new DashboardSpeciesViewModel(s, pct));
        }

        SpeciesTotal = Localization.PersianNumberFormatter.FormatGrouped(total);
        SpeciesSlices = DonutSliceViewModel.Build(species);
    }
}
