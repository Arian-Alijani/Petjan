using System;
using Animora.Contracts.Identity;
using Animora.Desktop.Controls;
using Animora.Desktop.Shell;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;

namespace Animora.Desktop.Services;

/// <summary>Central page admission; denied pages are never resolved or configured.</summary>
public sealed partial class NavigationService : ObservableObject, INavigationService
{
    private readonly IServiceProvider _services;
    private readonly ICapabilityGate _gate;
    private readonly IToastService _toast;

    [ObservableProperty]
    private PageViewModelBase? _currentViewModel;

    public NavigationService(IServiceProvider services, ICapabilityGate gate, IToastService toast)
    {
        _services = services;
        _gate = gate;
        _toast = toast;
    }

    public event EventHandler? CurrentViewModelChanged;

    public TViewModel? NavigateTo<TViewModel>() where TViewModel : PageViewModelBase =>
        (TViewModel?)NavigateTo(typeof(TViewModel));

    public TViewModel? NavigateTo<TViewModel>(Action<TViewModel> configure) where TViewModel : PageViewModelBase
    {
        ArgumentNullException.ThrowIfNull(configure);
        if (!Admit(typeof(TViewModel))) return null;
        var viewModel = _services.GetRequiredService<TViewModel>();
        configure(viewModel);
        if (!Admit(typeof(TViewModel))) return null;
        CurrentViewModel = viewModel;
        return viewModel;
    }

    public async Task<TViewModel?> NavigateToAsync<TViewModel>(Func<TViewModel, Task> configure)
        where TViewModel : PageViewModelBase
    {
        ArgumentNullException.ThrowIfNull(configure);
        if (!Admit(typeof(TViewModel))) return null;
        var viewModel = _services.GetRequiredService<TViewModel>();
        await configure(viewModel);
        // Authority can change while record hydration is awaited.
        if (!Admit(typeof(TViewModel))) return null;
        CurrentViewModel = viewModel;
        return viewModel;
    }

    public PageViewModelBase? NavigateTo(Type viewModelType, string? navigationKey = null,
        Action<PageViewModelBase>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(viewModelType);
        if (!Admit(viewModelType, navigationKey)) return null;
        var viewModel = (PageViewModelBase)_services.GetRequiredService(viewModelType);
        configure?.Invoke(viewModel);
        if (!Admit(viewModelType, navigationKey)) return null;
        CurrentViewModel = viewModel;
        return viewModel;
    }

    private bool Admit(Type page, string? navigationKey = null)
    {
        var defaultKey = NavPermissions.KeyFor(page);
        var key = navigationKey ?? defaultKey;
        // The expense and reports entries intentionally share one page type.
        var matches = key == defaultKey || (defaultKey == "reports" && key == "expense");
        if (matches && key is not null && NavPermissions.For(key) is { } code && _gate.Can(code)) return true;
        _toast.Show("اجازه ورود به این بخش را ندارید یا دسترسی شما منقضی شده است.", Tone.Warn);
        return false;
    }

    partial void OnCurrentViewModelChanged(PageViewModelBase? value)
    {
        value?.OnNavigatedTo();
        CurrentViewModelChanged?.Invoke(this, EventArgs.Empty);
    }
}
