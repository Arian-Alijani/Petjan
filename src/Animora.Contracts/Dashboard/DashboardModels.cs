using System.Collections.Generic;
using Animora.Contracts.Navigation;

namespace Animora.Contracts.Dashboard;

/// <summary>
/// Semantic tone for a dashboard list row's tag pill, mirroring
/// Animora.Desktop.Controls.Tone (Neutral/Danger/Warn/Info/Ok) — kept as a
/// separate enum here so Contracts has no dependency on the Desktop project.
/// </summary>
public enum DashboardTone
{
    Neutral,
    Danger,
    Warn,
    Info,
    Ok,
    Brand,
    Lilac,
    Peach,
}

/// <summary>
/// One KPI card, rendered by Phase 03's StatCard. Fields are limited to what
/// StatCard actually displays (icon/value/label/delta) — no unrendered data.
/// </summary>
public sealed record DashboardStat(
    string IconKey,
    string Label,
    string Value,
    string? Delta,
    bool IsDeltaPositive,
    DashboardTone IconTone = DashboardTone.Brand,
    string? Foot = null);

/// <summary>
/// One doctor's workload row in the manager dashboard's "بار کاری پزشکان
/// امروز" card: name, specialty, completed/total appointment counts, and an
/// accent colour hex for the avatar and progress bar
/// (mirrors DASH_VIEW_MANAGER's db-docload rows).
/// </summary>
public sealed record DashboardDoctorLoad(
    string Initial,
    string Name,
    string Specialty,
    int Done,
    int Total,
    string ColorHex)
{
    public double Ratio => Total <= 0 ? 0 : (double)Done / Total;
}

/// <summary>
/// One species slice in the "بیماران بر اساس گونه" donut/legend: display
/// name, patient count and slice colour hex (mirrors SPECIES in
/// Animora-UI/assets/dashboard.js).
/// </summary>
public sealed record DashboardSpecies(
    string Name,
    int Count,
    string ColorHex);

/// <summary>
/// The manager "نبض مالی کلینیک" (clinic financial pulse) card data: a big
/// formatted value, a delta caption, and the normalized 0..1 series driving
/// the area/line chart (mirrors the pulseChart in DASH_VIEW_MANAGER).
/// </summary>
public sealed record DashboardRevenuePulse(
    string Value,
    string Caption,
    IReadOnlyList<double> Points)
{
    private readonly IReadOnlyList<double> _points = DashboardCollections.Freeze(Points);
    public IReadOnlyList<double> Points { get => _points; init => _points = DashboardCollections.Freeze(value); }
}

/// <summary>
/// One row in a dashboard content list (live queue / alerts / open invoices /
/// vaccination reminders, depending on role) — a generic avatar+title+
/// subtitle+tag shape re-derived from what each DASH_VIEW_* row visually
/// shows in Animora-UI/assets/dashboard-views.js.
/// </summary>
public sealed record DashboardListItem(
    string AvatarText,
    string Title,
    string Subtitle,
    string TagText,
    DashboardTone TagTone,
    string? IconKey = null);

/// <summary>
/// Full data set for one role's dashboard render. Two labeled content lists
/// cover each role's two main sections (manager: live queue + alerts;
/// cashier: open invoices + ready-to-bill; doctor: live queue + vaccination
/// reminders) — shaped to the three DASH_VIEW_* functions, not a generic
/// catch-all.
/// </summary>
public sealed record DashboardSnapshot(
    UserRole Role,
    string HeroTitle,
    string HeroSubtitle,
    IReadOnlyList<DashboardStat> Stats,
    string PrimaryListTitle,
    IReadOnlyList<DashboardListItem> PrimaryList,
    string SecondaryListTitle,
    IReadOnlyList<DashboardListItem> SecondaryList,
    // Manager-only rich widgets (empty for other roles): the revenue pulse
    // chart, the doctor-workload card and the species donut/legend.
    DashboardRevenuePulse? RevenuePulse = null,
    IReadOnlyList<DashboardDoctorLoad>? DoctorLoads = null,
    IReadOnlyList<DashboardSpecies>? Species = null)
{
    // Copy once at construction, never at cache-hit. Elements are immutable records.
    private readonly IReadOnlyList<DashboardStat> _stats = DashboardCollections.Freeze(Stats);
    public IReadOnlyList<DashboardStat> Stats { get => _stats; init => _stats = DashboardCollections.Freeze(value); }
    private readonly IReadOnlyList<DashboardListItem> _primaryList = DashboardCollections.Freeze(PrimaryList);
    public IReadOnlyList<DashboardListItem> PrimaryList { get => _primaryList; init => _primaryList = DashboardCollections.Freeze(value); }
    private readonly IReadOnlyList<DashboardListItem> _secondaryList = DashboardCollections.Freeze(SecondaryList);
    public IReadOnlyList<DashboardListItem> SecondaryList { get => _secondaryList; init => _secondaryList = DashboardCollections.Freeze(value); }
    private readonly IReadOnlyList<DashboardDoctorLoad>? _doctorLoads = DoctorLoads is null ? null : DashboardCollections.Freeze(DoctorLoads);
    public IReadOnlyList<DashboardDoctorLoad>? DoctorLoads { get => _doctorLoads; init => _doctorLoads = value is null ? null : DashboardCollections.Freeze(value); }
    private readonly IReadOnlyList<DashboardSpecies>? _species = Species is null ? null : DashboardCollections.Freeze(Species);
    public IReadOnlyList<DashboardSpecies>? Species { get => _species; init => _species = value is null ? null : DashboardCollections.Freeze(value); }
}

internal static class DashboardCollections
{
    internal static IReadOnlyList<T> Freeze<T>(IReadOnlyList<T> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var copy = new T[source.Count];
        for (var i = 0; i < copy.Length; i++) copy[i] = source[i];
        return Array.AsReadOnly(copy);
    }
}
