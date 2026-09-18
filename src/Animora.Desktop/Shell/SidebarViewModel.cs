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

        // Phase 25: the shared Finance screen backs two nav entries; pass each
        // entry's initial tab/range (mirrors the reference's two
        // initFinancePage({ tab, range }) calls from expense.html / reports.html).
        if (viewModel is Pages.Finance.FinancePageViewModel finance
            && (entry.Item.InitialTab is not null || entry.Item.InitialRange is not null))
        {
            finance.Configure(entry.Item.InitialTab ?? "overview", entry.Item.InitialRange ?? "30d");
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
