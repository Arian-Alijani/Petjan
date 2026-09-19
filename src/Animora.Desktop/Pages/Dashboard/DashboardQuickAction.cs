using System;
using System.Collections.Generic;
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
/// (Animora-UI/assets/dashboard.js). Every target resolves to its real
/// <see cref="PageViewModelBase"/>-derived screen type (final wiring pass,
/// Phase 29) — none constructs <see cref="PlaceholderPageViewModel"/>.
/// </summary>
public sealed record DashboardQuickAction(string Label, string Subtitle, string IconKey, Type Target)
{
    public static IReadOnlyList<DashboardQuickAction> All { get; } = new[]
    {
        new DashboardQuickAction("نوبت جدید", "ثبت در تقویم", "IconCalendar", typeof(AppointmentsCalendarViewModel)),
        new DashboardQuickAction("پرونده بیمار", "جستجو و ثبت", "IconUsers", typeof(PatientsPageViewModel)),
        new DashboardQuickAction("شروع ویزیت", "ثبت معاینه", "IconClipboard", typeof(VisitsTriagePageViewModel)),
        new DashboardQuickAction("واکسیناسیون", "۶ یادآور امروز", "IconSyringe", typeof(VaccinationPageViewModel)),
        new DashboardQuickAction("فروش و صندوق", "صدور فاکتور", "IconCart", typeof(SalePageViewModel)),
        new DashboardQuickAction("انبار و کالا", "کنترل موجودی", "IconBox", typeof(InventoryPageViewModel)),
        new DashboardQuickAction("خرید و تأمین", "سفارش تأمین", "IconTruck", typeof(PurchasingPageViewModel)),
        new DashboardQuickAction("گزارش‌ها", "تحلیل عملکرد", "IconChart", typeof(FinancePageViewModel)),
    };
}
