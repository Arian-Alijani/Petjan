using System.Collections.Generic;
using System.Threading.Tasks;
using Animora.Contracts.Purchasing;

namespace Animora.Desktop.Mocks;

/// <summary>
/// Deterministic in-memory implementation of <see cref="IPurchasingQueryService"/>
/// — the single swappable seam a future backend replaces (R-18). Its sample
/// values are re-derived from the static markup the reference
/// (<c>Animora-UI/purchase.html</c>) renders: the three KPI stat cards
/// (۶ active orders, ۱۲۸ م this month, ۳۲ م supplier debt, with their
/// ۱۲٪↑/۴٪↓/۹٪↓ delta chips) and the three purchase orders of its table.
/// </summary>
public sealed class MockPurchasingQueryService : IPurchasingQueryService
{
    private static readonly PurchasingSnapshot Snapshot = new(
        // .stat cards — «سفارش فعال» (truck, brand tone) / «خرید این ماه»
        // (card, info tone) / «بدهی تأمین‌کننده» (dollar, warn tone).
        ActiveOrders: new PurchaseKpi("۶", "سفارش‌ فعال", "۱۲٪↑", true, "IconTruck", "brand"),
        MonthlyPurchases: new PurchaseKpi("۱۲۸ م", "خرید این ماه", "۴٪↓", false, "IconCreditCard", "info"),
        SupplierDebt: new PurchaseKpi("۳۲ م", "بدهی تأمین‌کننده", "۹٪↓", false, "IconDollar", "warn"),

        // tbody rows — same order, columns and values as the reference table.
        Orders: new List<PurchaseOrderSummary>
        {
            new("po-26818", "PO-۲۶۸۱۸", "پخش دارویی سپهر", "۱۴۰۵/۰۸/۰۵", "۳ قلم",
                "۲۸٬۶۵۰٬۰۰۰ تومان", "کارت بانکی", PurchaseOrderStatus.Delivered),
            new("po-27796", "PO-۲۷۷۹۶", "پت‌فود ایران", "۱۴۰۵/۰۸/۰۳", "۵ قلم",
                "۳۱٬۶۰۰٬۰۰۰ تومان", "اعتباری", PurchaseOrderStatus.InProgress),
            new("po-27974", "PO-۲۷۹۷۴", "تجهیز طب آریا", "۱۴۰۵/۰۷/۲۹", "۳ قلم",
                "۱۹٬۹۲۰٬۰۰۰ تومان", "نقدی", PurchaseOrderStatus.Finalized),
        });

    public Task<PurchasingSnapshot> GetSnapshotAsync() => Task.FromResult(Snapshot);
}
