using System;
using System.Threading.Tasks;
using Animora.Desktop.Pages.Dashboard;
using Animora.Desktop.Services;
using Animora.Desktop.Shell.Search;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;

namespace Animora.Desktop.Shell;

/// <summary>
/// Root shell ViewModel: composes the sidebar and topbar, mirrors the
/// navigation service's current page for the content host to bind to, chooses
/// the startup page, and owns the app-wide page-switch hotkeys (Alt+1..8,
/// Ctrl+Shift+D, Ctrl+K) so they work from any screen.
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    private const double ExpandedSidebarWidth = 288;

    // Keep a compact navigation rail visible when the full menu is collapsed so
    // every page remains reachable from its icon.
    private const double CollapsedSidebarWidth = 76;

    private readonly INavigationService _navigation;
    private readonly IDialogService _dialogs;
    private readonly IServiceProvider _services;

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
        IDialogService dialogs,
        IServiceProvider services,
        SidebarViewModel sidebar,
        TopbarViewModel topbar)
    {
        _navigation = navigation;
        _dialogs = dialogs;
        _services = services;
        Sidebar = sidebar;
        Topbar = topbar;

        _navigation.CurrentViewModelChanged += OnCurrentViewModelChanged;
        // The topbar hosts the single sidebar fold control for the whole shell.
        Topbar.SidebarToggleRequested += OnSidebarToggleRequested;

        // Default landing page: the real Dashboard screen (Phase 06).
        _navigation.NavigateTo<DashboardPageViewModel>();
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

    /// <summary>True while the dashboard is the active page (shows the floating refresh button).</summary>
    public bool IsDashboardPage => CurrentPage is DashboardPageViewModel;

    partial void OnCurrentPageChanged(PageViewModelBase? value) => OnPropertyChanged(nameof(IsDashboardPage));

    [RelayCommand]
    private Task RefreshDashboardAsync() =>
        CurrentPage is DashboardPageViewModel dashboard
            ? dashboard.RefreshCommand.ExecuteAsync(null)
            : Task.CompletedTask;

    // ---- Global page-switch hotkeys -----------------------------------------

    /// <summary>
    /// Navigate to the quick-access section at 1-based <paramref name="index"/>
    /// (Alt+1..8). Uses <see cref="DashboardQuickAction.All"/> as the single
    /// source of truth so the tiles and the hotkeys can never drift, and updates
    /// the sidebar highlight to follow.
    /// </summary>
    [RelayCommand]
    private void NavigateToSection(int index)
    {
        var actions = DashboardQuickAction.All;
        if (index < 1 || index > actions.Count)
        {
            return;
        }

        NavigateTo(actions[index - 1].Target);
    }

    /// <summary>Navigate to the Dashboard («خانه»). Bound to Ctrl+Shift+D.</summary>
    [RelayCommand]
    private void NavigateHome() => NavigateTo(typeof(DashboardPageViewModel));

    /// <summary>Open the Ctrl+K command palette from anywhere in the app.</summary>
    [RelayCommand]
    private void OpenPalette()
    {
        var palette = _services.GetRequiredService<CommandPaletteViewModel>();
        _dialogs.ShowModal(palette, maxWidth: 660);
    }

    // The sidebar highlight follows the navigation state; there is nothing to mirror here.
    private void NavigateTo(Type target) => _navigation.NavigateTo(target);
}
