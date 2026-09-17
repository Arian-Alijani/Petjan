using System;
using Animora.Desktop.Pages.Dashboard;
using Animora.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Animora.Desktop.Shell;

/// <summary>
/// Root shell ViewModel: composes the sidebar and topbar, mirrors the
/// navigation service's current page for the content host to bind to, and
/// chooses/highlights the startup page.
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    private const string StartupKey = "dashboard";

    private const double ExpandedSidebarWidth = 288;

    // Keep a compact navigation rail visible when the full menu is collapsed so
    // every page remains reachable from its icon.
    private const double CollapsedSidebarWidth = 76;

    private readonly INavigationService _navigation;

    [ObservableProperty]
    private PageViewModelBase? _currentPage;

    /// <summary>Whether the sidebar is expanded. Drives <see cref="SidebarWidth"/>.</summary>
    [ObservableProperty]
    private bool _isSidebarOpen = true;

    /// <summary>
    /// Target width for the animated sidebar host: full menu when open and a
    /// compact icon rail when collapsed. The view tweens between the two widths.
    /// </summary>
    [ObservableProperty]
    private double _sidebarWidth = ExpandedSidebarWidth;

    public MainWindowViewModel(
        INavigationService navigation,
        SidebarViewModel sidebar,
        TopbarViewModel topbar)
    {
        _navigation = navigation;
        Sidebar = sidebar;
        Topbar = topbar;

        _navigation.CurrentViewModelChanged += OnCurrentViewModelChanged;
        // The topbar hosts the single sidebar fold control for the whole shell.
        Topbar.SidebarToggleRequested += OnSidebarToggleRequested;

        // Default landing page: the real Dashboard screen (Phase 06).
        _navigation.NavigateTo<DashboardPageViewModel>();
        Sidebar.SetActive(StartupKey);
    }

    private void OnSidebarToggleRequested(object? sender, EventArgs e)
    {
        IsSidebarOpen = !IsSidebarOpen;
        Sidebar.IsExpanded = IsSidebarOpen;
        SidebarWidth = IsSidebarOpen ? ExpandedSidebarWidth : CollapsedSidebarWidth;

        // Keep the topbar's fold control in sync no matter which toggle
        // (topbar or in-sidebar) initiated the change.
        Topbar.SyncSidebarState(IsSidebarOpen);
    }

    public SidebarViewModel Sidebar { get; }

    public TopbarViewModel Topbar { get; }

    private void OnCurrentViewModelChanged(object? sender, EventArgs e) =>
        CurrentPage = _navigation.CurrentViewModel;
}
