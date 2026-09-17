using System;
using Animora.Contracts.Identity;
using Animora.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Shell;

/// <summary>
/// Drives the topbar: the page title/breadcrumb block (tracked from the
/// navigation service's current page), a visual-only search box, an inert
/// notification bell, and the user chip bound to <see cref="ICurrentUserContext"/>.
/// </summary>
public sealed partial class TopbarViewModel : ObservableObject
{
    private readonly INavigationService _navigation;
    private readonly IToastService _toast;

    [ObservableProperty]
    private PageViewModelBase? _currentPage;

    [ObservableProperty]
    private bool _isSidebarOpen = true;

    // Chevron rotation: 0° = arrow pointing toward the sidebar (collapse hint)
    // while expanded; 180° flips it to point away (expand hint) while collapsed.
    // The view tweens between the two angles for a smooth direction flip.
    [ObservableProperty]
    private double _toggleIconRotation;

    [ObservableProperty]
    private string _sidebarToggleToolTip = "جمع کردن منو";

    public TopbarViewModel(
        INavigationService navigation,
        IToastService toast,
        ICurrentUserContext currentUser)
    {
        _navigation = navigation;
        _toast = toast;
        CurrentUser = currentUser;

        _navigation.CurrentViewModelChanged += OnCurrentViewModelChanged;
        CurrentPage = _navigation.CurrentViewModel;
    }

    public ICurrentUserContext CurrentUser { get; }

    private void OnCurrentViewModelChanged(object? sender, EventArgs e) =>
        CurrentPage = _navigation.CurrentViewModel;

    [RelayCommand]
    private void OpenSearch() =>
        _toast.Show("جستجوی سراسری در این نسخه فعال نیست.");

    [RelayCommand]
    private void OpenNotifications() =>
        _toast.Show("۵ اعلان تازه دارید.");

    [RelayCommand]
    private void CreateNew() =>
        _toast.Show("ایجاد سریع در این نسخه فعال نیست.");

    /// <summary>
    /// Raised when the fold button is pressed. The shell (MainWindowViewModel)
    /// listens to this to collapse/expand the sidebar, keeping the toggle state
    /// out of the topbar itself.
    /// </summary>
    public event EventHandler? SidebarToggleRequested;

    [RelayCommand]
    private void ToggleSidebar()
    {
        IsSidebarOpen = !IsSidebarOpen;
        ToggleIconRotation = IsSidebarOpen ? 0 : 180;
        SidebarToggleToolTip = IsSidebarOpen ? "جمع کردن منو" : "باز کردن منو";
        SidebarToggleRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void ToggleTheme() =>
        _toast.Show("تم تیره در این نسخه فعال نیست.");
}
