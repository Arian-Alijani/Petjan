using CommunityToolkit.Mvvm.ComponentModel;

namespace Animora.Desktop.Shell;

/// <summary>
/// Throwaway page shown for every nav item that has no real screen yet.
/// Each screen phase (06+) replaces exactly one nav item's target with its
/// real ViewModel, retiring this placeholder for that route.
/// </summary>
public sealed partial class PlaceholderPageViewModel : PageViewModelBase
{
    [ObservableProperty]
    private string _message = "این صفحه هنوز ساخته نشده است.";

    public PlaceholderPageViewModel()
    {
        Title = "وت‌پلاس";
        Eyebrow = "به‌زودی";
        Breadcrumbs.Add("وت‌پلاس");
    }

    /// <summary>
    /// Retarget the placeholder to describe which sidebar item opened it, so
    /// the single shared instance reads correctly for whatever was clicked.
    /// </summary>
    public void ConfigureFor(string label)
    {
        Title = label;
        Message = $"صفحه «{label}» هنوز ساخته نشده است — به‌زودی.";
        Breadcrumbs.Clear();
        Breadcrumbs.Add("وت‌پلاس");
        Breadcrumbs.Add(label);
    }
}
