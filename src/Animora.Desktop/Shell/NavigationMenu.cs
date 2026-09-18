using System.Collections.Generic;
using Animora.Contracts.Navigation;
using Animora.Desktop.Pages.Appointments;
using Animora.Desktop.Pages.Boarding;
using Animora.Desktop.Pages.Cheques;
using Animora.Desktop.Pages.Dashboard;
using Animora.Desktop.Pages.Inventory;
using Animora.Desktop.Pages.Patients;
using Animora.Desktop.Pages.Sales;
using Animora.Desktop.Pages.Vaccination;
using Animora.Desktop.Pages.Visits;

namespace Animora.Desktop.Shell;

/// <summary>
/// Static sidebar definition mirroring the reference's NAV_ITEMS
/// (Animora-UI/assets/layout.js) exactly — same groups, order, labels,
/// icons and badge counts. This is layout/config (it changes only when the
/// sidebar itself changes), so hardcoding it here matches the reference's
/// own hardcoded NAV_ITEMS. Every item currently targets
/// <see cref="PlaceholderPageViewModel"/>; each screen phase (06+) swaps one
/// item's target for its real ViewModel.
/// </summary>
public static class NavigationMenu
{
    // Icon keys map to the StreamGeometry resources in Resources/Theme/Icons.axaml.
    public static IReadOnlyList<NavItem> Items { get; } = new NavItem[]
    {
        new("dashboard", "داشبورد", "IconDashboard", typeof(DashboardPageViewModel), "مرکز فرمان"),
        new("appointments", "نوبت‌ها", "IconCalendar", typeof(AppointmentsCalendarViewModel), "مرکز فرمان", BadgeCount: 4),

        new("patients", "بیماران و مالکین", "IconUsers", typeof(PatientsPageViewModel), "مدیریت کلینیک"),
        new("visits", "ویزیت و پرونده", "IconClipboard", typeof(VisitsTriagePageViewModel), "مدیریت کلینیک"),
        new("vaccine", "واکسیناسیون", "IconSyringe", typeof(VaccinationPageViewModel), "مدیریت کلینیک"),
        new("boarding", "پانسیون", "IconHome", typeof(BoardingPageViewModel), "مدیریت کلینیک", BadgeCount: 3),
        new("sale", "فروش و صندوق", "IconCart", typeof(SalePageViewModel), "مدیریت کلینیک"),

        new("inventory", "انبار و کالا", "IconBox", typeof(InventoryPageViewModel), "کسب و کار", BadgeCount: 2),
        new("expense", "هزینه‌ها", "IconReceipt", typeof(PlaceholderPageViewModel), "کسب و کار"),
        new("reports", "آمار و گزارش فروش", "IconChart", typeof(PlaceholderPageViewModel), "کسب و کار"),

        new("cheques", "مدیریت چک", "IconCheque", typeof(ChequesPageViewModel), "مالی و خزانه", BadgeCount: 3),

        new("history", "تاریخچه", "IconClock", typeof(PlaceholderPageViewModel), "سیستم"),
        new("profile", "پروفایل و تنظیمات", "IconShield", typeof(PlaceholderPageViewModel), "سیستم"),
    };
}
