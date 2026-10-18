namespace Animora.Contracts.Purchasing;

/// <summary>
/// Data seam for the Inventory «خرید و تأمین» tab and the purchase-invoice modal. Picking existing products and
/// registering brand-new ones go through <c>IInventoryQueryService</c> (not duplicated here); recording the invoice
/// writes the goods receipt, the stock ledger (one Receipt movement per line, carrying its unit cost) and the
/// variant purchase price in one atomic command, and shows up in the main History and the per-product history.
/// <para>
/// The supplier directory is owned here too: a supplier is created (or fetched) by name with an optional phone, and
/// the «تأمین‌کنندگان» tab reads the register (each supplier with its purchase statistics) plus one supplier's
/// completed-purchase history, so the whole supplier relationship lives on this one screen.
/// </para>
/// </summary>
public interface IPurchasingService
{
    /// <summary>KPI counters + the recent purchase invoices.</summary>
    Task<PurchasingOverview> GetOverviewAsync();

    /// <summary>Active suppliers matching <paramref name="search"/> (name / code / phone); everything when blank.</summary>
    Task<IReadOnlyList<SupplierRef>> FindSuppliersAsync(string? search);

    /// <summary>
    /// The supplier register («تأمین‌کنندگان» tab): every active supplier with its purchase statistics
    /// (receipt count, total purchased, last purchase date, open orders), optionally filtered by
    /// <paramref name="search"/> (name / code / phone); everything when blank.
    /// </summary>
    Task<IReadOnlyList<SupplierSummary>> GetSuppliersAsync(string? search);

    /// <summary>
    /// One supplier's full profile — its <see cref="SupplierSummary"/> plus the completed-purchase history
    /// (newest first), or <c>null</c> when the id is unknown.
    /// </summary>
    Task<SupplierProfile?> GetSupplierProfileAsync(string supplierId);

    /// <summary>
    /// Adds a supplier by name (or returns the existing one with the same name — never duplicates), storing the
    /// optional <paramref name="phone"/> number when it is a brand-new supplier.
    /// </summary>
    Task<SupplierRef> CreateSupplierAsync(string name, string? phone = null);

    /// <summary>Records a supplier purchase invoice (direct receipt). Never throws for a business failure — see the result.</summary>
    Task<PurchaseRecordResult> RecordPurchaseAsync(RecordPurchaseRequest request);
}
