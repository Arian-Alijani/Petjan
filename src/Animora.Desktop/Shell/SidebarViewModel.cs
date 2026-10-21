using System;
using System.Collections.Generic;
using System.Linq;
using Animora.Contracts.Identity;
using Animora.Contracts.Navigation;
using Animora.Desktop.Controls;
using Animora.Desktop.Diagnostics;
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
    private readonly IToastService _toast;
    private readonly NotificationBadgeState _badge;

    /// <summary>
    /// The nav keys that were visible after the previous rebuild. Diffing against it lets a live grant surface a
    /// toast for a section the manager just opened — the mirror image of the revocation warning the navigation
    /// service already shows. Seeded from the first build so the initial post-login layout is never announced.
    /// </summary>
    private HashSet<string> _permittedKeys;

    [ObservableProperty]
    private NavEntryViewModel? _activeEntry;

    [ObservableProperty]
    private bool _isExpanded = true;

    [ObservableProperty]
    private IReadOnlyList<NavGroupViewModel> _groups;

    public SidebarViewModel(
        INavigationService navigation,
        ICurrentUserContext currentUser,
        ICapabilityGate gate,
        IToastService toast,
        NotificationBadgeState badge)
    {
        _navigation = navigation;
        CurrentUser = currentUser;
        _gate = gate;
        _toast = toast;
        _badge = badge;

        _groups = BuildGroups();
        // The first layout is the baseline, not an "opened" event — record it without announcing anything.
        _permittedKeys = CurrentPermittedKeys();
        AppTrace.Info($"[sidebar] INITIAL build: groups={_groups.Count} entries={CountEntries(_groups)} permittedKeys={_permittedKeys.Count} " +
            $"(keys: {string.Join(",", _permittedKeys)}).", "sidebar");

        _gate.CapabilitiesChanged += OnCapabilitiesChanged;
        // The unread badge of the notifications entry follows the shared state (UI-thread event, no polling).
        _badge.Changed += (_, _) => ApplyBadge();
        _ = _badge.RefreshAsync();
        // The highlight is derived from the navigation state - the one place every entry point passes through.
        // The sidebar is an app-lifetime singleton, so this single subscription is never removed.
        _navigation.CurrentViewModelChanged += OnNavigationChanged;
        SyncActiveFromNavigation();
    }

    private void OnNavigationChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.UIThread.CheckAccess()) SyncActiveFromNavigation();
        else Dispatcher.UIThread.Post(SyncActiveFromNavigation);
    }

    /// <summary>
    /// Highlights the entry of the current navigation key, or clears the highlight when the shown page has no
    /// (permitted) sidebar entry, so a page such as Reception never leaves a stale item selected.
    /// </summary>
    public void SyncActiveFromNavigation()
    {
        var key = _navigation.CurrentNavigationKey;
        if (key is not null && IsPermitted(key) && FindEntry(key) is { } entry)
        {
            ApplyActive(entry);
            return;
        }

        if (ActiveEntry is { } active)
        {
            active.IsActive = false;
            ActiveEntry = null;
        }
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

        // Admission, the entry's tab/range and the highlight are all owned by the navigation service/state.
        _navigation.NavigateTo(entry.Item.ViewModelType, entry.Key);
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

    /// <summary>Rebuilds the visible groups, announces newly-opened sections, and drops an active entry that lost permission.</summary>
    private void ApplyCapabilitiesChanged()
    {
        var before = CountEntries(Groups);
        Groups = BuildGroups();
        var after = CountEntries(Groups);
        var emptyNote = after == 0
            ? " ⇒ SIDEBAR IS NOW EMPTY (every nav entry gated off — the read-only shell symptom)."
            : string.Empty;
        AppTrace.Info($"[sidebar] ApplyCapabilitiesChanged: entries {before}→{after}, groups={Groups.Count}, " +
            $"permittedKeys={CurrentPermittedKeys().Count}{emptyNote}", "sidebar");
        AnnounceNewlyOpened();
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

    private static int CountEntries(IReadOnlyList<NavGroupViewModel> groups)
    {
        var n = 0;
        foreach (var g in groups) n += g.Entries.Count;
        return n;
    }

    private IReadOnlyList<NavGroupViewModel> BuildGroups() =>
        NavigationMenu.Items
            .Where(IsPermitted)
            .GroupBy(item => item.GroupName)
            .Select(group => new NavGroupViewModel(
                group.Key,
                group.Select(CreateEntry).ToArray()))
            .ToArray();

    private NavEntryViewModel CreateEntry(NavItem item)
    {
        var entry = new NavEntryViewModel(item);
        if (item.Key == NotificationsKey) ApplyBadge(entry);
        return entry;
    }

    private const string NotificationsKey = "notifications";

    private void ApplyBadge()
    {
        foreach (var group in Groups)
        {
            foreach (var entry in group.Entries)
            {
                if (entry.Key == NotificationsKey) ApplyBadge(entry);
            }
        }
    }

    private void ApplyBadge(NavEntryViewModel entry)
    {
        entry.BadgeCount = _badge.Count;
        entry.BadgeText = _badge.Count > 0 ? _badge.Text : string.Empty;
    }

    /// <summary>The keys of every nav item currently permitted, in sidebar order.</summary>
    private HashSet<string> CurrentPermittedKeys() =>
        NavigationMenu.Items.Where(IsPermitted).Select(item => item.Key).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// After a rebuild, toasts the sections that just became visible (a manager opened access the client did not
    /// have a moment ago), then updates the baseline. Only additions are announced; a removal that affects the
    /// open page is already reported by the navigation service.
    /// </summary>
    private void AnnounceNewlyOpened()
    {
        var current = CurrentPermittedKeys();
        var opened = NavigationMenu.Items
            .Where(item => current.Contains(item.Key) && !_permittedKeys.Contains(item.Key))
            .Select(item => item.Label)
            .ToArray();
        _permittedKeys = current;
        if (opened.Length == 0) return;
        var sections = string.Join("، ", opened);
        _toast.Show(opened.Length == 1
            ? $"دسترسی بخش «{sections}» برای شما باز شد."
            : $"دسترسی بخش‌های جدید باز شد: {sections}", Tone.Ok);
    }

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
