using System;
using System.Collections.Generic;
using System.Linq;
using Animora.Contracts.Identity;
using Animora.Contracts.Navigation;
using Animora.Desktop.Services;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Shell;

/// <summary>
/// Drives the sidebar: brand header, grouped nav list (with active-item
/// tracking), compact/expanded presentation state, and the bottom user-status
/// card bound to <see cref="ICurrentUserContext"/>.
/// </summary>
/// <remarks>
/// B04c: the nav list is filtered through <see cref="ICapabilityGate"/>. The
/// gate is the only source of authority — the UI role is never consulted. Groups
/// are rebuilt whenever the gate raises <c>CapabilitiesChanged</c>, and the
/// active selection is kept only while the entry remains permitted.
/// </remarks>
public sealed partial class SidebarViewModel : ObservableObject
{
    private readonly INavigationService _navigation;
    private readonly ICapabilityGate _gate;

    [ObservableProperty]
    private NavEntryViewModel? _activeEntry;

    [ObservableProperty]
    private bool _isExpanded = true;

    [ObservableProperty]
    private IReadOnlyList<NavGroupViewModel> _groups;

    public SidebarViewModel(
        INavigationService navigation,
        ICurrentUserContext currentUser,
        ICapabilityGate gate)
    {
        _navigation = navigation;
        CurrentUser = currentUser;
        _gate = gate;

        _groups = BuildGroups();

        _gate.CapabilitiesChanged += OnCapabilitiesChanged;
    }

    public ICurrentUserContext CurrentUser { get; }

    /// <summary>Navigate to the clicked entry's target and update the highlight.</summary>
    [RelayCommand]
    private void Navigate(NavEntryViewModel? entry)
    {
        // The user may click an item that a just-raised change has already revoked.
        if (entry is null || !IsPermitted(entry.Item))
        {
            return;
        }

        var viewModel = _navigation.NavigateTo(entry.Item.ViewModelType);

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
        if (!IsPermitted(key) || FindEntry(key) is not { } entry)
        {
            return;
        }

        ApplyActive(entry);
    }

    private void OnCapabilitiesChanged(object? sender, EventArgs e)
    {
        // The gate may raise this from a background thread (sign-in/refresh); marshal to the UI thread.
        if (Dispatcher.UIThread.CheckAccess())
        {
            ApplyCapabilitiesChanged();
        }
        else
        {
            Dispatcher.UIThread.Post(ApplyCapabilitiesChanged);
        }
    }

    /// <summary>Rebuilds the visible groups and drops an active entry that lost permission.</summary>
    private void ApplyCapabilitiesChanged()
    {
        Groups = BuildGroups();
        if (ActiveEntry is not { } active || IsPermitted(active.Item))
        {
            return;
        }

        if (FindEntry(active.Key) is { } stillVisible)
        {
            ApplyActive(stillVisible); // reselecting the rebuilt instance keeps the highlight bound correctly.
            return;
        }

        active.IsActive = false;
        ActiveEntry = null;
    }

    private IReadOnlyList<NavGroupViewModel> BuildGroups() =>
        NavigationMenu.Items
            .Where(IsPermitted)
            .GroupBy(item => item.GroupName)
            .Select(group => new NavGroupViewModel(
                group.Key,
                group.Select(item => new NavEntryViewModel(item)).ToArray()))
            .ToArray();

    private bool IsPermitted(NavItem item) => IsPermitted(item.Key);

    private bool IsPermitted(string key)
    {
        // An unmapped key fails closed (NavPermissions.For returns null ⇒ Can(string.Empty) is false).
        var code = NavPermissions.For(key) ?? string.Empty;
        return _gate.Can(code);
    }

    private NavEntryViewModel? FindEntry(string key) =>
        Groups.SelectMany(group => group.Entries).FirstOrDefault(entry => entry.Key == key);

    private void ApplyActive(NavEntryViewModel entry)
    {
        if (ReferenceEquals(ActiveEntry, entry))
        {
            entry.IsActive = true;
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
