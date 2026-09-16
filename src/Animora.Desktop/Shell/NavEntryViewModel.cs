using Animora.Contracts.Navigation;
using Animora.Desktop.Localization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Animora.Desktop.Shell;

/// <summary>
/// Bindable wrapper around one <see cref="NavItem"/> for the sidebar list:
/// carries the source item plus an observable <see cref="IsActive"/> flag the
/// active-item highlight binds to.
/// </summary>
public sealed partial class NavEntryViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isActive;

    public NavEntryViewModel(NavItem item)
    {
        Item = item;
        BadgeText = item.BadgeCount is { } count
            ? PersianNumberFormatter.ToPersianDigits(count)
            : null;
    }

    public NavItem Item { get; }

    public string Key => Item.Key;

    public string Label => Item.Label;

    public string IconKey => Item.IconKey;

    public bool HasBadge => Item.BadgeCount is not null;

    /// <summary>Badge count pre-formatted to Persian digits, or null.</summary>
    public string? BadgeText { get; }
}
