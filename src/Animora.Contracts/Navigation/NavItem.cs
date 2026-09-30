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
/// <param name="InitialTab">Optional initial tab a target ViewModel should open on (e.g. Finance).</param>
/// <param name="InitialRange">Optional initial range a target ViewModel should open on (e.g. Finance).</param>
/// <param name="RequiredPermission">
/// Optional permission code (B04c) the shell gates this entry on. Added LAST so every existing positional
/// construction stays source-compatible. Kept <see langword="null"/> for the static <c>NavigationMenu</c>
/// items: their gating lives in the Desktop's ordinal <c>NavPermissions</c> map, so the shared navigation
/// definition is not rewritten.
/// </param>
public sealed record NavItem(
    string Key,
    string Label,
    string IconKey,
    Type ViewModelType,
    string GroupName,
    string? InitialTab = null,
    string? InitialRange = null,
    string? RequiredPermission = null);
