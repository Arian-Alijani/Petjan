using Animora.Desktop.Pages.Appointments;
using Animora.Desktop.Pages.Finance;
using Animora.Desktop.Pages.Inventory;
using Animora.Desktop.Pages.Patients;
using Animora.Desktop.Pages.Purchasing;
using Animora.Desktop.Pages.Sales;
using Animora.Desktop.Pages.Vaccination;
using Animora.Desktop.Pages.Visits;
using Animora.Desktop.Shell;

namespace Animora.Desktop.Pages.Dashboard;

/// <summary>
/// One quick-access tile, mirroring one entry of the reference's TILES array
/// (Animora-UI/assets/dashboard.js) including its per-tile accent (<see cref="AccentHex"/>),
/// soft background (<see cref="SoftHex"/>), optional badge and Alt+N keyboard hint.
/// Every target resolves to its real <see cref="PageViewModelBase"/>-derived screen
/// type — none constructs <see cref="PlaceholderPageViewModel"/>.
/// </summary>
/// <param name="Label">Tile title (e.g. «نوبت جدید»).</param>
/// <param name="Subtitle">Tile sub-line (e.g. «ثبت در تقویم»).</param>
/// <param name="IconKey">Resource key of the icon geometry (e.g. "IconCalendar").</param>
/// <param name="Target">The screen ViewModel type the tile navigates to.</param>
/// <param name="AccentHex">Saturated per-tile accent colour (hover border/icon chip).</param>
/// <param name="SoftHex">Soft per-tile background colour (icon chip rest state).</param>
/// <param name="AltKey">1-based index used for the «Alt+N» corner hint and the global hotkey.</param>
/// <param name="Badge">Optional badge count (Persian numeral); null hides the badge.</param>
public sealed record DashboardQuickAction(
    string Label,
    string Subtitle,
    string IconKey,
    Type Target,
    string AccentHex,
    string SoftHex,
    int AltKey,
    string? Badge = null)
{
    /// <summary>
    /// Canonical ordered list of the eight main sections. This is the SINGLE
    /// source of truth shared by the dashboard tiles and the shell's global
    /// Alt+1..8 hotkeys, so the two can never drift.
    /// </summary>
    public static IReadOnlyList<DashboardQuickAction> All { get; } = new[]
    {
        new DashboardQuickAction("نوبت جدید", "ثبت در تقویم", "IconCalendar", typeof(AppointmentsCalendarViewModel), "#0d9488", "#f0fdfa", 1),
        new DashboardQuickAction("پرونده بیمار", "جستجو و ثبت", "IconUsers", typeof(PatientsPageViewModel), "#7b5cc4", "#ece5fb", 2),
        new DashboardQuickAction("شروع ویزیت", "ثبت معاینه", "IconClipboard", typeof(VisitsTriagePageViewModel), "#1570cd", "#eff8ff", 3),
        new DashboardQuickAction("واکسیناسیون", "۶ یادآور امروز", "IconSyringe", typeof(VaccinationPageViewModel), "#027a48", "#ecfdf3", 4),
        new DashboardQuickAction("فروش و صندوق", "صدور فاکتور", "IconCart", typeof(SalePageViewModel), "#d9803f", "#fbe7d6", 5),
        new DashboardQuickAction("انبار و کالا", "کنترل موجودی", "IconBox", typeof(InventoryPageViewModel), "#b54708", "#fffaeb", 6, Badge: "۳"),
        new DashboardQuickAction("خرید و تأمین", "سفارش تأمین", "IconTruck", typeof(PurchasingPageViewModel), "#14b8a6", "#e5f4f0", 7),
        new DashboardQuickAction("گزارش‌ها", "تحلیل عملکرد", "IconChart", typeof(FinancePageViewModel), "#2f6f8f", "#e2eff5", 8),
    };

    /// <summary>The «Alt+N» corner hint with a Persian digit (e.g. «Alt+۱»).</summary>
    public string AltHint => "Alt+" + Localization.PersianNumberFormatter.ToPersianDigits(AltKey);

    /// <summary>Shared, cached accent brush for the tile.</summary>
    public Avalonia.Media.IBrush AccentBrush => Controls.BrushCache.Solid(AccentHex);

    /// <summary>Shared, cached soft-background brush for the tile's icon chip.</summary>
    public Avalonia.Media.IBrush SoftBrush => Controls.BrushCache.Solid(SoftHex);
}
