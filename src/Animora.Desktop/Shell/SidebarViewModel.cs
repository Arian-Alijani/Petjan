using System;
using System.Collections.Generic;
using System.Linq;
using Animora.Contracts.Identity;
using Animora.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Shell;

/// <summary>
/// Drives the sidebar: brand header, grouped nav list (with active-item
/// tracking), compact/expanded presentation state, and the bottom user-status
/// card bound to <see cref="ICurrentUserContext"/>.
/// </summary>
public sealed partial class SidebarViewModel : ObservableObject
{
    private readonly INavigationService _navigation;
    private readonly Dictionary<string, NavEntryViewModel> _entriesByKey;

    [ObservableProperty]
    private NavEntryViewModel? _activeEntry;

    [ObservableProperty]
    private bool _isExpanded = true;

    // 0° keeps the double-chevron pointing toward the sidebar's leading (right,
    // RTL) edge — a "collapse" hint — while expanded; 180° flips it to point
    // away as an "expand" hint while collapsed. The view tweens between them.
    [ObservableProperty]
    private double _toggleIconRotation;

    [ObservableProperty]
    private string _toggleToolTip = "جمع کردن منو";

    /// <summary>
    /// Raised when the in-sidebar fold button is pressed. The shell
    /// (MainWindowViewModel) listens to this to collapse/expand the rail,
    /// keeping the width/animation state out of the sidebar itself.
    /// </summary>
    public event EventHandler? ToggleRequested;

    public SidebarViewModel(
        INavigationService navigation,
        ICurrentUserContext currentUser)
    {
        _navigation = navigation;
        CurrentUser = currentUser;

        Groups = NavigationMenu.Items
            .GroupBy(item => item.GroupName)
            .Select(group => new NavGroupViewModel(
                group.Key,
                group.Select(item => new NavEntryViewModel(item)).ToArray()))
            .ToArray();

        _entriesByKey = Groups
            .SelectMany(group => group.Entries)
            .ToDictionary(entry => entry.Key);
    }

    public IReadOnlyList<NavGroupViewModel> Groups { get; }

    public ICurrentUserContext CurrentUser { get; }

    /// <summary>Request the shell to flip the rail between expanded and compact.</summary>
    [RelayCommand]
    private void Toggle() => ToggleRequested?.Invoke(this, EventArgs.Empty);

    // Keep the fold-button glyph + tooltip in sync with the presentation state,
    // whether the toggle came from the sidebar itself or the topbar.
    partial void OnIsExpandedChanged(bool value)
    {
        ToggleIconRotation = value ? 0 : 180;
        ToggleToolTip = value ? "جمع کردن منو" : "باز کردن منو";
    }

    /// <summary>Navigate to the clicked entry's target and update the highlight.</summary>
    [RelayCommand]
    private void Navigate(NavEntryViewModel? entry)
    {
        if (entry is null)
        {
            return;
        }

        var viewModel = _navigation.NavigateTo(entry.Item.ViewModelType);

        // Placeholder pages are a single shared instance reused across items;
        // retarget it so the topbar/content reflect the clicked label.
        if (viewModel is PlaceholderPageViewModel placeholder)
        {
            placeholder.ConfigureFor(entry.Label);
        }

        SetActive(entry.Key);
    }

    /// <summary>Highlight the entry with <paramref name="key"/> (used at startup too).</summary>
    public void SetActive(string key)
    {
        if (!_entriesByKey.TryGetValue(key, out var entry))
        {
            return;
        }

        if (ActiveEntry is { } previous)
        {
            previous.IsActive = false;
        }

        entry.IsActive = true;
        ActiveEntry = entry;
    }
}
