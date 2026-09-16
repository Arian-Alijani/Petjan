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
    bool IsDeltaPositive);

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
    DashboardTone TagTone);

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
    IReadOnlyList<DashboardListItem> SecondaryList);
