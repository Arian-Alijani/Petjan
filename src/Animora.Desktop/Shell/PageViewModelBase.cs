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
}
