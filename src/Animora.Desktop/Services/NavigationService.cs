using System;
using Animora.Desktop.Shell;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;

namespace Animora.Desktop.Services;

/// <summary>
/// Default <see cref="INavigationService"/>: resolves page ViewModels from
/// the DI container so each navigation gets a fully-injected instance.
/// Registered as a singleton in the composition root.
/// </summary>
public sealed partial class NavigationService : ObservableObject, INavigationService
{
    private readonly IServiceProvider _services;

    [ObservableProperty]
    private PageViewModelBase? _currentViewModel;

    public NavigationService(IServiceProvider services) => _services = services;

    public event EventHandler? CurrentViewModelChanged;

    public TViewModel NavigateTo<TViewModel>() where TViewModel : PageViewModelBase =>
        (TViewModel)NavigateTo(typeof(TViewModel));

    public TViewModel NavigateTo<TViewModel>(Action<TViewModel> configure) where TViewModel : PageViewModelBase
    {
        var viewModel = (TViewModel)_services.GetRequiredService(typeof(TViewModel));
        configure(viewModel);
        CurrentViewModel = viewModel;
        return viewModel;
    }

    public PageViewModelBase NavigateTo(Type viewModelType)
    {
        ArgumentNullException.ThrowIfNull(viewModelType);

        var viewModel = (PageViewModelBase)_services.GetRequiredService(viewModelType);
        CurrentViewModel = viewModel;
        return viewModel;
    }

    partial void OnCurrentViewModelChanged(PageViewModelBase? value) =>
        CurrentViewModelChanged?.Invoke(this, EventArgs.Empty);
}
