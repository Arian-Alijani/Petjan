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
    /// <summary>The page ViewModel currently hosted in the shell content area.</summary>
    PageViewModelBase? CurrentViewModel { get; }

    /// <summary>Raised after <see cref="CurrentViewModel"/> changes.</summary>
    event EventHandler? CurrentViewModelChanged;

    /// <summary>Resolve <typeparamref name="TViewModel"/> from DI and make it current.</summary>
    TViewModel NavigateTo<TViewModel>() where TViewModel : PageViewModelBase;

    /// <summary>Resolve <paramref name="viewModelType"/> from DI and make it current.</summary>
    PageViewModelBase NavigateTo(Type viewModelType);
}
