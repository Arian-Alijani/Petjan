using System;
using System.Collections.Generic;
using Animora.Desktop.Shell;

namespace Animora.Desktop.Pages.Dashboard;

/// <summary>
/// One quick-access tile, mirroring one entry of the reference's TILES array
/// (Animora-UI/assets/dashboard.js). All targets besides Dashboard itself
/// still resolve to <see cref="PlaceholderPageViewModel"/> until their own
/// phase runs (expected/correct at this point in the roadmap).
/// </summary>
public sealed record DashboardQuickAction(string Label, string Subtitle, string IconKey, Type Target)
{
    public static IReadOnlyList<DashboardQuickAction> All { get; } = new[]
    {
        new DashboardQuickAction("نوبت جدید", "ثبت در تقویم", "IconCalendar", typeof(PlaceholderPageViewModel)),
        new DashboardQuickAction("پرونده بیمار", "جستجو و ثبت", "IconUsers", typeof(PlaceholderPageViewModel)),
        new DashboardQuickAction("شروع ویزیت", "ثبت معاینه", "IconClipboard", typeof(PlaceholderPageViewModel)),
        new DashboardQuickAction("واکسیناسیون", "۶ یادآور امروز", "IconSyringe", typeof(PlaceholderPageViewModel)),
        new DashboardQuickAction("فروش و صندوق", "صدور فاکتور", "IconCart", typeof(PlaceholderPageViewModel)),
        new DashboardQuickAction("انبار و کالا", "کنترل موجودی", "IconBox", typeof(PlaceholderPageViewModel)),
        // Reference TILES[6] is "خرید و تأمین" (truck icon); Phase 03's 23-icon
        // set has no truck geometry, so IconReceipt stands in for the tile's
        // icon while label/subtitle/target stay faithful to dashboard.js.
        new DashboardQuickAction("خرید و تأمین", "سفارش تأمین", "IconReceipt", typeof(PlaceholderPageViewModel)),
        new DashboardQuickAction("گزارش‌ها", "تحلیل عملکرد", "IconChart", typeof(PlaceholderPageViewModel)),
    };
}
