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

    public NavEntryViewModel(NavItem item) => Item = item;

    public NavItem Item { get; }

    public string Key => Item.Key;

    public string Label => Item.Label;

    public string IconKey => Item.IconKey;

}
