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

    private readonly INavigationService _navigation;

    [ObservableProperty]
    private PageViewModelBase? _currentPage;

    public MainWindowViewModel(
        INavigationService navigation,
        SidebarViewModel sidebar,
        TopbarViewModel topbar)
    {
        _navigation = navigation;
        Sidebar = sidebar;
        Topbar = topbar;

        _navigation.CurrentViewModelChanged += OnCurrentViewModelChanged;

        // Default landing page: the real Dashboard screen (Phase 06).
        _navigation.NavigateTo<DashboardPageViewModel>();
        Sidebar.SetActive(StartupKey);
    }

    public SidebarViewModel Sidebar { get; }

    public TopbarViewModel Topbar { get; }

    private void OnCurrentViewModelChanged(object? sender, EventArgs e) =>
        CurrentPage = _navigation.CurrentViewModel;
}
