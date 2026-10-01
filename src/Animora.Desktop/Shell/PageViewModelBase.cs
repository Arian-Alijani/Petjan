using System.Collections.ObjectModel;
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
}
