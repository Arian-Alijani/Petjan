namespace Animora.Contracts.Purchasing;

/// <summary>
/// The purchase-order status, mirroring the state pills the reference renders
/// in its orders table (<c>Animora-UI/purchase.html</c>): «تحویل شد» / «در حین»
/// / «ثبت نهایی». The View maps each value 1:1 to a Phase 02 semantic tone
/// (ok / warn / …) — this contract stays free of UI resources.
/// </summary>
public enum PurchaseOrderStatus
{
    /// <summary>«تحویل شد» — delivered / received (ok tone).</summary>
    Delivered,

    /// <summary>«در حین» — in transit / in progress (warn tone).</summary>
    InProgress,

    /// <summary>«ثبت نهایی» — finalized / booked (ok tone).</summary>
    Finalized,
}

/// <summary>
/// One purchase order, mirroring the columns the reference's orders table
/// renders (<c>Animora-UI/purchase.html</c> lines 68–99): its Persian order
/// number (شماره), supplier (تأمین‌کننده), Jalali order date (تاریخ), item
/// count (اقلام), total amount (مبلغ کل), settlement method (تسویه) and
/// <see cref="Status"/>. Numeric values are pre-formatted Persian strings
/// exactly as the reference carries them so the View renders them verbatim.
/// </summary>
public sealed record PurchaseOrderSummary(
    string Id,
    string Number,
    string Supplier,
    string OrderDate,
    string ItemCountText,
    string AmountText,
    string Settlement,
    PurchaseOrderStatus Status);

/// <summary>
/// The whole Purchase &amp; Suppliers screen snapshot, mirroring what
/// <c>Animora-UI/purchase.html</c> statically renders: the three KPI values of
/// the stat row (active orders / this-month purchases / supplier debt) plus the
/// purchase-orders list. KPI values are pre-formatted Persian strings; the
/// delta chip text and its up/down direction match the reference's
/// ۱۲٪↑ / ۴٪↓ / ۹٪↓ pattern.
/// </summary>
public sealed record PurchasingSnapshot(
    PurchaseKpi ActiveOrders,
    PurchaseKpi MonthlyPurchases,
    PurchaseKpi SupplierDebt,
    IReadOnlyList<PurchaseOrderSummary> Orders);

/// <summary>
/// One KPI stat card's data, mirroring a reference <c>.stat</c> block: its
/// large <see cref="Value"/> (e.g. «۶»), its <see cref="Label"/> («سفارش فعال»)
/// and the delta chip (<see cref="DeltaText"/> + <see cref="IsDeltaPositive"/>,
/// matching the reference's ۱۲٪↑ / ۴٪↓ / ۹٪↓). <see cref="IconKey"/> and
/// <see cref="Tone"/> drive the tinted icon tile, resolved by the View.
/// </summary>
public sealed record PurchaseKpi(
    string Value,
    string Label,
    string DeltaText,
    bool IsDeltaPositive,
    string IconKey,
    string Tone);
