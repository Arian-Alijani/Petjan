namespace Animora.Desktop.Shell;

/// <summary>User-visible load state of a page; a real zero is only possible in Ready/Empty/Refreshing/Stale.</summary>
public enum PageLoadState
{
    /// <summary>Nothing has been received yet (startup pending or first query running).</summary>
    InitialLoading,
    /// <summary>Data is shown and a newer copy is being fetched.</summary>
    Refreshing,
    /// <summary>A successful load with data is shown.</summary>
    Ready,
    /// <summary>A successful load was received and the store has nothing to list.</summary>
    Empty,
    /// <summary>The last refresh failed; the previous successful data is still shown but is not current.</summary>
    Stale,
    /// <summary>The first load failed; there is no data to show.</summary>
    Error,
}

/// <summary>Avalonia-free state resolution shared by pages (unit-testable without a UI).</summary>
public static class PageLoadStateResolver
{
    /// <summary>
    /// <paramref name="hasData"/>: a real load result (not the cleared placeholder) is published.
    /// An error with data is Stale; an error without data is Error; busy without data is InitialLoading.
    /// <paramref name="isEmpty"/> is computed by the page (it knows what "nothing to list" means).
    /// </summary>
    public static PageLoadState Resolve(bool hasData, bool isBusy, bool hasError, bool isEmpty)
    {
        if (!hasData) return hasError && !isBusy ? PageLoadState.Error : PageLoadState.InitialLoading;
        if (hasError && !isBusy) return PageLoadState.Stale;
        if (isBusy) return PageLoadState.Refreshing;
        return isEmpty ? PageLoadState.Empty : PageLoadState.Ready;
    }

    public static string Describe(PageLoadState state) => state switch
    {
        PageLoadState.InitialLoading => "در حال بارگذاری…",
        PageLoadState.Refreshing => "در حال به‌روزرسانی…",
        PageLoadState.Empty => "هنوز داده‌ای برای نمایش ثبت نشده است.",
        PageLoadState.Stale => "به‌روزرسانی ناموفق بود؛ آخرین داده نمایش داده می‌شود و ممکن است قدیمی باشد.",
        PageLoadState.Error => "بارگذاری انجام نشد.",
        _ => string.Empty,
    };
}
