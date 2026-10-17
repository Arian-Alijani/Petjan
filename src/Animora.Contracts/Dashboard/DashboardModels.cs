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
/// StatCard actually displays (icon/value/label/delta/secondary line/sparkline) — no unrendered data.
/// </summary>
public sealed record DashboardStat(
    string IconKey,
    string Label,
    string Value,
    string? Delta,
    bool IsDeltaPositive,
    DashboardTone IconTone = DashboardTone.Brand,
    string? Foot = null)
{
    private IReadOnlyList<double>? _spark;

    /// <summary>
    /// Optional sparkline series normalized to 0..1 (oldest first). Copied into an immutable array on assignment so a
    /// cached snapshot can never be mutated through the caller's list; <c>null</c> means the card has no chart.
    /// </summary>
    public IReadOnlyList<double>? Spark
    {
        get => _spark;
        init => _spark = value is null ? null : DashboardCollections.Freeze(value);
    }

    /// <summary>Navigation target key opened when the card is clicked (see <see cref="DashboardStatTargets"/>); <c>null</c> = default (statistics).</summary>
    public string? NavTarget { get; init; }

    /// <summary>Second information line under the value (e.g. "N قلم · درآمد Z تومان"); <c>null</c> hides it.</summary>
    public string? Secondary { get; init; }
}

/// <summary>Well-known <see cref="DashboardStat.NavTarget"/> keys (Contracts stays free of Desktop view-model types).</summary>
public static class DashboardStatTargets
{
    public const string Appointments = "Appointments";
    public const string Statistics = "Statistics";
}

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
/// <remarks>
/// <b>Key</b> is the stable, language-independent species identifier (Dog, Cat, ...). Presentation derives the
/// Persian name, glyph and palette from it; <b>Name</b>/<b>ColorHex</b> remain only as a compatibility fallback.
/// </remarks>
public sealed record DashboardSpecies(
    string Name,
    int Count,
    string ColorHex,
    string Key = "");

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

    /// <summary>
    /// Positions in 0..1 inside the signed domain [min(0,min), max(0,max)] — a negative day is placed below
    /// the zero level and is never clamped to zero.
    /// </summary>
    public IReadOnlyList<double> Points { get => _points; init => _points = DashboardCollections.Freeze(value); }

    /// <summary>Position of zero in the same 0..1 space as <see cref="Points"/> (0 when every value is zero/positive-free).</summary>
    public double ZeroLevel { get; init; }

    private IReadOnlyList<string> _dayLabels = [];

    /// <summary>Localized calendar label of every plotted day (same order as <see cref="Points"/>), for tooltips.</summary>
    public IReadOnlyList<string> DayLabels { get => _dayLabels; init => _dayLabels = DashboardCollections.Freeze(value); }

    /// <summary>Short textual equivalent of the chart (range, lowest and highest day) for tooltip/automation.</summary>
    public string Summary { get; init; } = string.Empty;

    private IReadOnlyList<long> _values = [];

    /// <summary>The real signed minor-unit amount of every plotted day, same order as <see cref="Points"/>.</summary>
    public IReadOnlyList<long> Values { get => _values; init => _values = DashboardCollections.Freeze(value); }
}

/// <summary>
/// One row in a dashboard content list (live queue / alerts / open invoices /
/// vaccination reminders, depending on role) — a generic avatar+title+
/// subtitle+tag shape re-derived from what each DASH_VIEW_* row visually
/// shows in Animora-UI/assets/dashboard-views.js.
/// </summary>
/// <remarks>
/// <b>Kind</b> (Appointment, Invoice, Vaccine, Visit, Sale, ...) and <b>Status</b> (the entity's own state name)
/// are stable, language-independent identifiers that drive icons and tone in presentation.
/// <b>TitleKey</b> is an optional translation key (<c>history.*</c>); <b>Title</b> is always a safe,
/// human-readable fallback and is never a technical key or an internal id.
/// </remarks>
public sealed record DashboardListItem(
    string AvatarText,
    string Title,
    string Subtitle,
    string TagText,
    DashboardTone TagTone,
    string? IconKey = null,
    string Kind = "",
    string Status = "",
    string? TitleKey = null);

/// <summary>
/// When a snapshot's data was read and how far the derived finance facts lag behind the authoritative records.
/// <b>PendingProjectionCount</b> is the number of committed events the projection worker has not applied yet (the
/// finance widgets may be behind by that much); <b>ProjectionFailed</b> means at least one was dead-lettered, so the
/// finance figures must not be presented as ready/current. Null on a snapshot without finance data.
/// </summary>
public sealed record DashboardFreshness(DateTimeOffset GeneratedAtUtc, int PendingProjectionCount, bool ProjectionFailed)
{
    public bool IsProjectionCurrent => PendingProjectionCount == 0 && !ProjectionFailed;
}

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
    IReadOnlyList<DashboardSpecies>? Species = null,
    DashboardFreshness? Freshness = null,
    // Urgent cross-section alerts (cheques, recorded/recurring expenses) shown in the dedicated always-visible box,
    // already permission-gated and urgency-ordered. AlertOverflow = urgent items beyond the bounded list.
    IReadOnlyList<DashboardListItem>? Alerts = null,
    int AlertOverflow = 0)
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
    private readonly IReadOnlyList<DashboardListItem> _alerts = DashboardCollections.Freeze(Alerts ?? []);
    public IReadOnlyList<DashboardListItem>? Alerts { get => _alerts; init => _alerts = DashboardCollections.Freeze(value ?? []); }
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

/// <summary>Safe failure categories across the Desktop data seam; cancellation stays OperationCanceledException.</summary>
public enum DashboardFailureKind { SessionExpired, PermissionDenied, StoreUnavailable, Unexpected }

public sealed class DashboardQueryException : InvalidOperationException
{
    public DashboardFailureKind Kind { get; }
    public string CorrelationId { get; }
    public bool CanRetry => Kind == DashboardFailureKind.StoreUnavailable;
    public string UserMessage => Kind switch
    {
        DashboardFailureKind.SessionExpired => "نشست شما منقضی شده است؛ دوباره وارد شوید.",
        DashboardFailureKind.PermissionDenied => "اجازه مشاهده این اطلاعات را ندارید.",
        DashboardFailureKind.StoreUnavailable => "پایگاه داده در دسترس نیست؛ دوباره تلاش کنید.",
        _ => "خطای غیرمنتظره در داشبورد؛ شناسه پیگیری را به پشتیبانی بدهید.",
    };

    public DashboardQueryException(DashboardFailureKind kind, string correlationId) : base(kind.ToString())
    {
        Kind = kind;
        CorrelationId = correlationId;
    }

    public static DashboardFailureKind Classify(string code) => code switch
    {
        "SESSION_EXPIRED" or "UNAUTHENTICATED" or "OFFLINE_CAPABILITY_EXPIRED" => DashboardFailureKind.SessionExpired,
        "PERMISSION_DENIED" or "PERMISSION_UNKNOWN" or "MEMBERSHIP_NOT_ACTIVE" or "TENANT_SUSPENDED"
            or "USER_LOCKED_OUT" or "CAPABILITY_NOT_GRANTED" or "CAPABILITY_SNAPSHOT_MISSING"
            or "CAPABILITY_SNAPSHOT_TAMPERED" => DashboardFailureKind.PermissionDenied,
        "STORE_UNAVAILABLE" or "STORE_KEY_UNAVAILABLE" or "STORE_CORRUPT" => DashboardFailureKind.StoreUnavailable,
        _ => DashboardFailureKind.Unexpected,
    };
}
