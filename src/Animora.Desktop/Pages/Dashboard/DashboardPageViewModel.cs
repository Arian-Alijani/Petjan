using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Animora.Contracts.Dashboard;
using Animora.Contracts.Identity;
using Animora.Contracts.Navigation;
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
        _dashboard = dashboard;
        Role = currentUser.Role;

        Title = "داشبورد";
        Eyebrow = "مرکز فرمان";
        Breadcrumbs.Add("داشبورد");

        // Non-blocking init: never call .Result/.GetAwaiter().GetResult() on the
        // UI thread (PERFORMANCE.md §4). The mock resolves synchronously today; a
        // future Datasync-backed service returns a real Task and this stays
        // responsive. Set IsBusy until the snapshot lands so the shell reflects
        // the in-flight state.
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            Apply(await _dashboard.GetSnapshotAsync(Role));
        }
        finally
        {
            IsBusy = false;
        }
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
    private System.Collections.Generic.IReadOnlyList<DonutSliceViewModel> _speciesSlices =
        System.Array.Empty<DonutSliceViewModel>();

    /// <summary>The species donut's default center readout, restored on blur.</summary>
    private string _speciesBaseTotal = string.Empty;

    private string _speciesBaseSub = string.Empty;

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
        if (index < 0 || index >= Species.Count)
        {
            return;
        }

        IsSpeciesDimmed = true;
        for (var i = 0; i < Species.Count; i++)
        {
            Species[i].IsActive = i == index;
        }

        foreach (var slice in SpeciesSlices)
        {
            slice.IsActive = slice.Index == index;
            slice.IsDimmed = true;
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
        IsSpeciesDimmed = false;
        foreach (var row in Species)
        {
            row.IsActive = false;
        }

        foreach (var slice in SpeciesSlices)
        {
            slice.IsActive = false;
            slice.IsDimmed = false;
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
        for (var i = 0; i < species.Count; i++)
        {
            var s = species[i];
            var pct = total <= 0 ? 0 : (double)s.Count / total;
            Species.Add(new DashboardSpeciesViewModel(i, s, pct));
        }

        _speciesBaseTotal = Localization.PersianNumberFormatter.FormatGrouped(total);
        _speciesBaseSub = Localization.PersianNumberFormatter.ToPersianDigits(
            species.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)) + " گونه ثبت‌شده";

        SpeciesTotal = _speciesBaseTotal;
        SpeciesTotalLabel = "کل بیماران فعال";
        SpeciesTotalSub = _speciesBaseSub;
        SpeciesCenterBrush = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#101828"));
        IsSpeciesDimmed = false;
        SpeciesSlices = DonutSliceViewModel.Build(species);
    }
}
