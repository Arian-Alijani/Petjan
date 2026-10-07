namespace Animora.Contracts.Purchasing;

/// <summary>A supplier of the clinic's directory (picker row).</summary>
public sealed record SupplierRef(string Id, string Code, string Name, string? Phone, int PaymentTermDays);

/// <summary>
/// One purchased line of a supplier invoice. <paramref name="Quantity"/> is whole units (the service converts to the
/// store's ×1000 quantity scale); <paramref name="UnitCost"/> is the purchase price per unit in تومان and becomes the
/// variant's purchase price once the invoice is recorded. <paramref name="ExpiryJalali"/> is Jalali <c>yyyy/MM/dd</c>.
/// </summary>
public sealed record PurchaseLineInput(
    string ProductId,
    string VariantId,
    int Quantity,
    long UnitCost,
    string? Lot = null,
    string? ExpiryJalali = null);

/// <summary>
/// Records a supplier purchase invoice (a direct goods receipt without a purchase order). The supplier is either an
/// existing <paramref name="SupplierId"/> or, when blank, <paramref name="NewSupplierName"/> which is
/// get-or-created by name first.
/// </summary>
public sealed record RecordPurchaseRequest(
    string? SupplierId,
    string? NewSupplierName,
    IReadOnlyList<PurchaseLineInput> Lines,
    string? InvoiceReference = null);

/// <summary>Outcome of <see cref="IPurchasingService.RecordPurchaseAsync"/>; <see cref="Error"/> is a stable backend code.</summary>
public sealed record PurchaseRecordResult(bool Ok, string? Error, string ReceiptId);

/// <summary>One recorded supplier purchase invoice of the purchases table (pre-formatted for the View).</summary>
public sealed record PurchaseListItem(
    string Id,
    string Number,
    string SupplierName,
    string DateJalali,
    int LineCount,
    long Total,
    string InvoiceReference,
    string StateLabel);

/// <summary>The purchases tab payload: KPI counters plus the most recent invoices (newest first).</summary>
public sealed record PurchasingOverview(
    int ActiveOrders,
    long MonthSpend,
    long SupplierDebt,
    int ReceiptCount,
    int SupplierCount,
    IReadOnlyList<PurchaseListItem> Receipts);
