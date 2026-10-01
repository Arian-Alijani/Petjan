using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Shell;

/// <summary>
/// Avalonia-free load state of one page, rendered once by the shell (not per view). Overlapping loads are
/// counted, so an older load finishing never clears the busy flag of a newer one. The block is shown for
/// the initial load and for failures (<see cref="PageLoadState.Error"/> / <see cref="PageLoadState.Stale"/>);
/// quiet background refreshes of already-shown data do not flash it.
/// </summary>
public sealed class PageLoadStatus : ObservableObject
{
    private readonly Func<Task> _refresh;
    private int _active;
    private bool _hasData;
    private bool _hasError;

    public PageLoadStatus(Func<Task> refresh)
    {
        _refresh = refresh ?? throw new ArgumentNullException(nameof(refresh));
        RefreshCommand = new AsyncRelayCommand(() => _refresh());
    }

    /// <summary>Manual refresh / retry (the fallback to the automatic refresh on entry).</summary>
    public IAsyncRelayCommand RefreshCommand { get; }

    public bool IsBusy => _active > 0;

    public PageLoadState State => PageLoadStateResolver.Resolve(_hasData, IsBusy, _hasError, isEmpty: false);

    /// <summary>Visible while there is nothing to show yet, or the last load failed.</summary>
    public bool IsVisible => State is PageLoadState.InitialLoading or PageLoadState.Error or PageLoadState.Stale;

    public bool CanRetry => State is PageLoadState.Error or PageLoadState.Stale;

    public string Text => PageLoadStateResolver.Describe(State);

    public void Begin()
    {
        _active++;
        // With data on screen a failure stays visible until a load succeeds; without data it restarts.
        if (!_hasData) _hasError = false;
        Notify();
    }

    public void Succeeded()
    {
        _hasData = true;
        _hasError = false;
        End();
    }

    public void Failed()
    {
        _hasError = true;
        End();
    }

    private void End()
    {
        if (_active > 0) _active--;
        Notify();
    }

    private void Notify()
    {
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(IsVisible));
        OnPropertyChanged(nameof(CanRetry));
        OnPropertyChanged(nameof(Text));
    }
}
