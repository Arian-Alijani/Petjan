namespace Animora.Desktop.Services;

/// <summary>دامنه‌های داده‌ای که نوشتن‌ها آن‌ها را کهنه می‌کنند.</summary>
public enum DataDomain
{
    Patients,
    Visits,
    Vaccination,
    Appointments,
    Boarding,
    Inventory,
    Purchasing,
    Sales,
    Cashier,
    Cheques,
    Finance,
    History,
    Dashboard,

    /// <summary>پیام‌ها و اعلان‌های کاربر (ساخت/انتشار/خوانده‌شدن).</summary>
    Notifications,
}

/// <summary>ناقل سبک «فلان دامنه عوض شد» بین صفحات زندهٔ شل (UI thread).</summary>
public interface IDataChangeNotifier
{
    /// <summary>اعلام تغییر در یک یا چند دامنه (پس از یک نوشتن موفق).</summary>
    void Notify(params DataDomain[] domains);

    /// <summary>اشتراک یک شنونده؛ خروجی یک IDisposable برای لغو اشتراک.</summary>
    IDisposable Subscribe(Action<DataDomain> handler);
}
