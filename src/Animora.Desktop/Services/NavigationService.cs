using System;
using Animora.Contracts.Identity;
using Animora.Desktop.Controls;
using Animora.Desktop.Diagnostics;
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

    public string? CurrentNavigationKey { get; private set; }

    public event EventHandler? CurrentViewModelChanged;

    public TViewModel? NavigateTo<TViewModel>() where TViewModel : PageViewModelBase =>
        (TViewModel?)NavigateTo(typeof(TViewModel));

    public TViewModel? NavigateTo<TViewModel>(Action<TViewModel> configure) where TViewModel : PageViewModelBase
    {
        ArgumentNullException.ThrowIfNull(configure);
        if (!Admit(typeof(TViewModel))) return null;
        var viewModel = _services.GetRequiredService<TViewModel>();
        ApplyEntry(viewModel, NavPermissions.KeyFor(typeof(TViewModel)));
        configure(viewModel);
        if (!Admit(typeof(TViewModel))) return null;
        Publish(viewModel, NavPermissions.KeyFor(typeof(TViewModel)));
        return viewModel;
    }

    public async Task<TViewModel?> NavigateToAsync<TViewModel>(Func<TViewModel, Task> configure)
        where TViewModel : PageViewModelBase
    {
        ArgumentNullException.ThrowIfNull(configure);
        if (!Admit(typeof(TViewModel))) return null;
        var viewModel = _services.GetRequiredService<TViewModel>();
        ApplyEntry(viewModel, NavPermissions.KeyFor(typeof(TViewModel)));
        await configure(viewModel);
        // Authority can change while record hydration is awaited.
        if (!Admit(typeof(TViewModel))) return null;
        Publish(viewModel, NavPermissions.KeyFor(typeof(TViewModel)));
        return viewModel;
    }

    public PageViewModelBase? NavigateTo(Type viewModelType, string? navigationKey = null,
        Action<PageViewModelBase>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(viewModelType);
        if (!Admit(viewModelType, navigationKey)) return null;
        var viewModel = (PageViewModelBase)_services.GetRequiredService(viewModelType);
        var key = navigationKey ?? NavPermissions.KeyFor(viewModelType);
        ApplyEntry(viewModel, key);
        configure?.Invoke(viewModel);
        if (!Admit(viewModelType, navigationKey)) return null;
        Publish(viewModel, key);
        return viewModel;
    }

    private bool Admit(Type page, string? navigationKey = null)
    {
        var defaultKey = NavPermissions.KeyFor(page);
        var key = navigationKey ?? defaultKey;
        var matches = key == defaultKey;
        if (matches && key is not null && NavPermissions.For(key) is { } code && _gate.Can(code)) return true;
        AppTrace.Warn($"navigation DENIED to '{key ?? page.Name}' (insufficient capability or expired access)", "nav");
        _toast.Show("اجازه ورود به این بخش را ندارید یا دسترسی شما منقضی شده است.", Tone.Warn);
        return false;
    }

    // The semantic entry (InitialTab/InitialRange of the nav entry) is applied for every caller, not only the sidebar.
    private static void ApplyEntry(PageViewModelBase viewModel, string? key)
    {
        if (key is null) return;
        foreach (var item in NavigationMenu.Items)
        {
            if (item.Key != key) continue;
            viewModel.ApplyNavigationEntry(item);
            return;
        }
    }

    // Single publication path for every overload. Activation is decoupled from the property-equality check:
    //  - a different page: the page being replaced is told it is leaving (cancel in-flight work/timers), then the
    //    new page becomes current (OnCurrentViewModelChanged activates it and raises CurrentViewModelChanged);
    //  - the same singleton page: it is simply activated again (e.g. Ctrl+Shift+D on the dashboard refreshes it
    //    once) without a leave/recreate and without a spurious CurrentViewModelChanged.
    private void Publish(PageViewModelBase viewModel, string? key)
    {
        var keyChanged = CurrentNavigationKey != key;
        if (keyChanged) AppTrace.Navigate(CurrentNavigationKey ?? "(start)", key ?? viewModel.GetType().Name);
        CurrentNavigationKey = key;
        if (ReferenceEquals(CurrentViewModel, viewModel))
        {
            viewModel.OnNavigatedTo();
            // Same page, different semantic entry (expense -> reports): the highlight must follow.
            if (keyChanged) CurrentViewModelChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (CurrentViewModel is { } leaving)
        {
            leaving.IsCurrentPage = false;
            leaving.OnNavigatedFrom();
        }

        // Before the assignment: the change handler runs OnNavigatedTo, which may already be notified of writes.
        viewModel.IsCurrentPage = true;
        CurrentViewModel = viewModel;
    }

    partial void OnCurrentViewModelChanged(PageViewModelBase? value)
    {
        value?.OnNavigatedTo();
        CurrentViewModelChanged?.Invoke(this, EventArgs.Empty);
    }
}
