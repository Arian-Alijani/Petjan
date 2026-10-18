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
/// get-or-created by name first (storing <paramref name="NewSupplierPhone"/> when it is brand-new).
/// </summary>
public sealed record RecordPurchaseRequest(
    string? SupplierId,
    string? NewSupplierName,
    IReadOnlyList<PurchaseLineInput> Lines,
    string? InvoiceReference = null,
    string? NewSupplierPhone = null);

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

/// <summary>
/// One row of the «تأمین‌کنندگان» register: a supplier of the clinic's directory with its purchase
/// relationship at a glance — receipt count, total purchased, the last purchase date and any still-open
/// purchase orders. All figures are pre-computed by the service (raw numbers for money / dates; the View
/// formats them through the phase-04 Persian helpers).
/// </summary>
/// <param name="Id">The supplier id.</param>
/// <param name="Code">The supplier code (unique per tenant).</param>
/// <param name="Name">The supplier name.</param>
/// <param name="Phone">The contact phone number, or empty when none is recorded.</param>
/// <param name="PaymentTermDays">The agreed payment term in days (<c>0</c> = cash on delivery).</param>
/// <param name="ReceiptCount">How many purchase invoices have been recorded from this supplier.</param>
/// <param name="TotalPurchased">The total value bought from this supplier, in تومان.</param>
/// <param name="LastPurchaseJalali">The last purchase date as an ASCII Jalali <c>yyyy/MM/dd</c> string, or empty when never.</param>
/// <param name="OpenOrderCount">How many purchase orders are still open (goods expected).</param>
public sealed record SupplierSummary(
    string Id,
    string Code,
    string Name,
    string Phone,
    int PaymentTermDays,
    int ReceiptCount,
    long TotalPurchased,
    string LastPurchaseJalali,
    int OpenOrderCount);

/// <summary>
/// One completed purchase (a posted goods receipt) in a supplier's history — the invoice number, date,
/// reference, line count and total. Pre-formatted for the View.
/// </summary>
public sealed record SupplierPurchaseRecord(
    string ReceiptId,
    string Number,
    string DateJalali,
    int LineCount,
    long Total,
    string InvoiceReference);

/// <summary>
/// One supplier's full profile: its register row (<paramref name="Summary"/>) plus the completed
/// purchase history (newest first).
/// </summary>
public sealed record SupplierProfile(
    SupplierSummary Summary,
    IReadOnlyList<SupplierPurchaseRecord> Purchases);
