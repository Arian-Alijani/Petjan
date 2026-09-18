namespace Animora.Contracts.Navigation;

/// <summary>
/// A single sidebar navigation entry, mirroring one item of the reference's
/// NAV_ITEMS (Animora-UI/assets/layout.js). Layout/config data, not a data
/// contract fulfilled by a backend: the target is a ViewModel type resolved
/// via the DI container by the navigation service.
/// </summary>
/// <param name="Key">Stable identifier (e.g. "dashboard"), matches the reference key.</param>
/// <param name="Label">Display label (Persian), shown in the sidebar.</param>
/// <param name="IconKey">Resource key of the icon geometry (e.g. "IconDashboard").</param>
/// <param name="ViewModelType">The <c>PageViewModelBase</c>-derived type this item navigates to.</param>
/// <param name="GroupName">The nav section this item belongs to.</param>
/// <param name="BadgeCount">Optional badge count pill; null hides the pill.</param>
/// <param name="InitialTab">Optional initial tab a target ViewModel should open on (e.g. Finance).</param>
/// <param name="InitialRange">Optional initial range a target ViewModel should open on (e.g. Finance).</param>
public sealed record NavItem(
    string Key,
    string Label,
    string IconKey,
    Type ViewModelType,
    string GroupName,
    int? BadgeCount = null,
    string? InitialTab = null,
    string? InitialRange = null);
