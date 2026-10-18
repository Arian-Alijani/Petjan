namespace Animora.Application.UseCases.Purchasing.ReadModels;

/// <summary>One purchase-order row for the orders table.</summary>
public sealed record PurchaseOrderRowReadModel(
    string Id,
    string OrderNumber,
    string SupplierName,
    DateTimeOffset OrderedUtc,
    int LineCount,
    long GrandTotalMinor,
    string Currency,
    int PaymentTermDays,
    int State);

/// <summary>
/// The whole Purchase &amp; Suppliers snapshot, computed by <c>IPurchasingQueries</c> in ≤3 SELECTs:
/// the KPI aggregates (active-order count, month-to-date spend, outstanding supplier debt) plus the
/// purchase-order list.
/// </summary>
public sealed record PurchasingSnapshotReadModel(
    int ActiveOrderCount,
    long MonthToDateSpendMinor,
    long SupplierDebtMinor,
    string Currency,
    IReadOnlyList<PurchaseOrderRowReadModel> Orders);

/// <summary>One supplier of the directory picker.</summary>
public sealed record SupplierRowReadModel(string Id, string Code, string Name, string? Phone, int PaymentTermDays);

/// <summary>
/// One row of the supplier register («تأمین‌کنندگان»): a supplier with its purchase statistics —
/// receipt count, total purchased, last purchase time and open-order count — in a bounded read.
/// </summary>
public sealed record SupplierSummaryReadModel(
    string Id,
    string Code,
    string Name,
    string? Phone,
    int PaymentTermDays,
    int ReceiptCount,
    long TotalPurchasedMinor,
    DateTimeOffset? LastPurchaseUtc,
    int OpenOrderCount);

/// <summary>One completed purchase (posted goods receipt) of a supplier's history.</summary>
public sealed record SupplierPurchaseRecordReadModel(
    string ReceiptId,
    string Number,
    DateTimeOffset ReceivedUtc,
    int LineCount,
    long TotalMinor,
    string? InvoiceReference);

/// <summary>One supplier's full profile: its register row plus the completed-purchase history (newest first).</summary>
public sealed record SupplierProfileReadModel(
    SupplierSummaryReadModel Summary,
    IReadOnlyList<SupplierPurchaseRecordReadModel> Purchases);

/// <summary>One recorded goods receipt (supplier purchase invoice) of the purchases table.</summary>
public sealed record GoodsReceiptRowReadModel(
    string Id,
    string ReceiptNumber,
    string SupplierName,
    DateTimeOffset ReceivedUtc,
    int LineCount,
    long TotalMinor,
    string? InvoiceReference,
    int State);

/// <summary>The purchases tab payload: counters plus the recent receipts.</summary>
public sealed record PurchaseReceiptListReadModel(
    int ReceiptCount,
    int SupplierCount,
    IReadOnlyList<GoodsReceiptRowReadModel> Receipts);
