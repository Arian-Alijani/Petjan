using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Animora.Contracts.Navigation;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Animora.Desktop.Shell;

/// <summary>
/// Base for every screen ViewModel (CONVENTIONS.md §3). Exposes the topbar
/// title block (Title / Eyebrow / Breadcrumbs) and a shared IsBusy flag so
/// the shell can bind its chrome once, independent of which page is current.
/// </summary>
public abstract partial class PageViewModelBase : ObservableObject
{
    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _eyebrow = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>
    /// Breadcrumb trail rendered in the topbar (last item is emphasized).
    /// </summary>
    public ObservableCollection<string> Breadcrumbs { get; } = new();

    /// <summary>
    /// Called by the navigation service every time this ViewModel becomes the
    /// current page. Page ViewModels are registered as singletons, so their
    /// constructor-time load runs only once; overriding this lets a page pull
    /// fresh data on every visit (e.g. a doctor registered on the Profile page,
    /// or an appointment just booked, must appear when the user returns here).
    /// The default is a no-op so pages that hold static content stay cheap.
    /// </summary>
    public virtual void OnNavigatedTo()
    {
    }

    /// <summary>
    /// Called when another page replaces this one as the current page (never when navigation to this page is
    /// denied, and never when the destination is this same page). Pages that run background work, timers or
    /// in-flight loads for their visible lifetime stop them here; the default is a no-op.
    /// </summary>
    public virtual void OnNavigatedFrom()
    {
    }

    /// <summary>
    /// Called by the navigation service, after admission and before the page is published, with the semantic
    /// navigation entry the visit came through (the same page type can back several entries, e.g. Finance:
    /// expenses / reports). Every entry point - sidebar, tile, hotkey, palette - passes through here, so the
    /// destination state never depends on which caller navigated. The default is a no-op.
    /// </summary>
    public virtual void ApplyNavigationEntry(NavItem entry)
    {
    }

    /// <summary>
    /// Standard load state (loading / failed / stale) with a manual refresh command; null for pages that render
    /// their own (the dashboard). The shell shows it once above the page and in the topbar refresh button.
    /// </summary>
    public PageLoadStatus? LoadStatus { get; private set; }

    /// <summary>Opts the page in to <see cref="LoadStatus"/>; <paramref name="refresh"/> is the page's reload.</summary>
    protected void UseLoadStatus(Func<Task> refresh) => LoadStatus = new PageLoadStatus(refresh);

    /// <summary>True while a tracked load is running; entry hooks use it to avoid a duplicate parallel load.</summary>
    protected bool IsLoadInFlight => LoadStatus?.IsBusy == true;

    /// <summary>
    /// Runs a page load and records its outcome. A failure never escapes the fire-and-forget callers: it is shown
    /// as the page's error/stale state with a retry, and a stuck <see cref="IsBusy"/> is released.
    /// </summary>
    protected Task TrackLoadAsync(Func<Task> load) => TrackLoadResultAsync(async () =>
    {
        await load();
        return true;
    });

    /// <summary>
    /// Same as <see cref="TrackLoadAsync"/> for loads that already catch and report their own failure: the load
    /// returns <c>false</c> when it failed, so the page still shows the error/stale state.
    /// </summary>
    protected async Task TrackLoadResultAsync(Func<Task<bool>> load)
    {
        var status = LoadStatus;
        status?.Begin();
        bool ok;
        try
        {
            ok = await load();
        }
        catch (Exception)
        {
            ok = false;
        }

        if (!ok) IsBusy = false;
        if (ok) status?.Succeeded();
        else status?.Failed();
    }
}
