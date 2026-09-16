using System.Collections.Generic;
using System.Linq;
using Animora.Contracts.Identity;
using Animora.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Shell;

/// <summary>
/// Drives the sidebar: brand header, grouped nav list (with active-item
/// tracking), the inert upsell CTA, and the bottom user-status card bound to
/// <see cref="ICurrentUserContext"/>.
/// </summary>
public sealed partial class SidebarViewModel : ObservableObject
{
    private readonly INavigationService _navigation;
    private readonly IToastService _toast;
    private readonly Dictionary<string, NavEntryViewModel> _entriesByKey;

    [ObservableProperty]
    private NavEntryViewModel? _activeEntry;

    public SidebarViewModel(
        INavigationService navigation,
        IToastService toast,
        ICurrentUserContext currentUser)
    {
        _navigation = navigation;
        _toast = toast;
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

    [RelayCommand]
    private void OpenUpsell() =>
        _toast.Show("نسخه حرفه‌ای به‌زودی در دسترس خواهد بود.");

    [RelayCommand]
    private void ToggleCollapse() =>
        _toast.Show("جمع/باز کردن منو در این نسخه فعال نیست.");
}
