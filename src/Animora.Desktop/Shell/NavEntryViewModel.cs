using Animora.Contracts.Navigation;
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

    /// <summary>Unread/attention count shown as a small red badge on the icon; 0 hides it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBadge))]
    private int _badgeCount;

    /// <summary>Text inside the badge (Persian digits, «۹۹+» cap).</summary>
    [ObservableProperty]
    private string _badgeText = string.Empty;

    public bool HasBadge => BadgeCount > 0;

    public NavEntryViewModel(NavItem item) => Item = item;

    public NavItem Item { get; }

    public string Key => Item.Key;

    public string Label => Item.Label;

    public string IconKey => Item.IconKey;

}
