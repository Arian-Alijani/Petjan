using System;
using Animora.Desktop.Shell;

namespace Animora.Desktop.Services;

/// <summary>
/// Single navigation seam for the app (CONVENTIONS.md §3). Screens navigate
/// via <see cref="NavigateTo{TViewModel}"/> / <see cref="NavigateTo(Type)"/>,
/// never via direct Window/Control references. The shell binds its content
/// host to <see cref="CurrentViewModel"/>.
/// </summary>
public interface INavigationService
{
    // All navigation overloads return null on admission denial; the current page
    // remains unchanged and the central service presents a safe explanation.
    /// <summary>The page ViewModel currently hosted in the shell content area.</summary>
    PageViewModelBase? CurrentViewModel { get; }

    /// <summary>Raised after <see cref="CurrentViewModel"/> changes.</summary>
    event EventHandler? CurrentViewModelChanged;

    /// <summary>Resolve <typeparamref name="TViewModel"/> from DI and make it current.</summary>
    TViewModel? NavigateTo<TViewModel>() where TViewModel : PageViewModelBase;

    /// <summary>
    /// Resolve <typeparamref name="TViewModel"/> from DI, run <paramref name="configure"/>
    /// on it (to pass a drill-in id/context before it is shown), then make it current.
    /// </summary>
    TViewModel? NavigateTo<TViewModel>(Action<TViewModel> configure) where TViewModel : PageViewModelBase;

    /// <summary>
    /// Resolve <typeparamref name="TViewModel"/> from DI, <c>await</c>
    /// <paramref name="configure"/> (to asynchronously load a drill-in record before
    /// it is shown — never blocking the UI thread, PERFORMANCE.md §4), then make it
    /// current. The shell stays on the previous page until the load completes.
    /// </summary>
    Task<TViewModel?> NavigateToAsync<TViewModel>(Func<TViewModel, Task> configure) where TViewModel : PageViewModelBase;

    /// <summary>Resolve <paramref name="viewModelType"/> from DI and make it current.</summary>
    PageViewModelBase? NavigateTo(Type viewModelType, string? navigationKey = null,
        Action<PageViewModelBase>? configure = null);
}
