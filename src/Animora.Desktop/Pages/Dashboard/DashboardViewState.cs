using System.Linq;
using Animora.Contracts.Dashboard;

namespace Animora.Desktop.Pages.Dashboard;

/// <summary>User-visible load state of the dashboard; a real zero is only possible in Ready/Empty/Refreshing/Stale.</summary>
public enum DashboardViewState
{
    /// <summary>Nothing has been received yet (startup pending or first query running).</summary>
    InitialLoading,
    /// <summary>A snapshot is shown and a newer one is being fetched.</summary>
    Refreshing,
    /// <summary>A successful snapshot with data is shown.</summary>
    Ready,
    /// <summary>A successful snapshot was received and the store has nothing to list.</summary>
    Empty,
    /// <summary>The last refresh failed; the previous successful snapshot is still shown but is not current.</summary>
    Stale,
    /// <summary>The first load failed; there is no data to show.</summary>
    Error,
}

/// <summary>Avalonia-free state resolution for the dashboard (unit-testable without a UI).</summary>
public static class DashboardViewStateResolver
{
    /// <summary>
    /// <paramref name="hasData"/>: a real snapshot (not the cleared placeholder) is published.
    /// An error with data is Stale; an error without data is Error; busy without data is InitialLoading.
    /// </summary>
    public static DashboardViewState Resolve(bool hasData, bool isBusy, bool hasError, DashboardSnapshot? snapshot)
    {
        if (!hasData) return hasError && !isBusy ? DashboardViewState.Error : DashboardViewState.InitialLoading;
        if (hasError && !isBusy) return DashboardViewState.Stale;
        if (isBusy) return DashboardViewState.Refreshing;
        return IsEmpty(snapshot) ? DashboardViewState.Empty : DashboardViewState.Ready;
    }

    /// <summary>Empty = both operational lists and the optional rich widgets have no rows (counters may be zero).</summary>
    public static bool IsEmpty(DashboardSnapshot? snapshot) =>
        snapshot is not null
        && snapshot.PrimaryList.Count == 0
        && snapshot.SecondaryList.Count == 0
        && (snapshot.DoctorLoads is null || snapshot.DoctorLoads.Count == 0)
        && (snapshot.Species is null || snapshot.Species.Count == 0);

    public static string Describe(DashboardViewState state) => state switch
    {
        DashboardViewState.InitialLoading => "در حال بارگذاری داشبورد…",
        DashboardViewState.Refreshing => "در حال به‌روزرسانی…",
        DashboardViewState.Empty => "هنوز داده‌ای برای نمایش ثبت نشده است.",
        DashboardViewState.Stale => "به‌روزرسانی ناموفق بود؛ آخرین داده موفق نمایش داده می‌شود و ممکن است قدیمی باشد.",
        DashboardViewState.Error => "بارگذاری داشبورد ناموفق بود.",
        _ => string.Empty,
    };
}
