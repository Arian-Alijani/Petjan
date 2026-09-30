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

    public async Task<TViewModel> NavigateToAsync<TViewModel>(Func<TViewModel, Task> configure)
        where TViewModel : PageViewModelBase
    {
        var viewModel = (TViewModel)_services.GetRequiredService(typeof(TViewModel));
        // Await the configure step (the drill-in record load) before switching the
        // shell, so the page is fully hydrated on first paint and the UI thread is
        // never blocked (PERFORMANCE.md §4).
        await configure(viewModel);
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

    partial void OnCurrentViewModelChanged(PageViewModelBase? value)
    {
        // Singleton page ViewModels only load once (in their constructor), so
        // signal every activation to let the page refresh data it may share
        // with other screens (doctors, appointments, …). Kept in the property
        // setter so it fires for all NavigateTo overloads uniformly.
        value?.OnNavigatedTo();
        CurrentViewModelChanged?.Invoke(this, EventArgs.Empty);
    }
}
