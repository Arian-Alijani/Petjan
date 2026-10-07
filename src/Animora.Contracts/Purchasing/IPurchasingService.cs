namespace Animora.Contracts.Purchasing;

/// <summary>
/// Data seam for the Inventory «خرید و تأمین» tab and the purchase-invoice modal. Picking existing products and
/// registering brand-new ones go through <c>IInventoryQueryService</c> (not duplicated here); recording the invoice
/// writes the goods receipt, the stock ledger (one Receipt movement per line, carrying its unit cost) and the
/// variant purchase price in one atomic command, and shows up in the main History and the per-product history.
/// </summary>
public interface IPurchasingService
{
    /// <summary>KPI counters + the recent purchase invoices.</summary>
    Task<PurchasingOverview> GetOverviewAsync();

    /// <summary>Active suppliers matching <paramref name="search"/> (name / code / phone); everything when blank.</summary>
    Task<IReadOnlyList<SupplierRef>> FindSuppliersAsync(string? search);

    /// <summary>Adds a supplier by name, or returns the existing one with the same name (never duplicates).</summary>
    Task<SupplierRef> CreateSupplierAsync(string name);

    /// <summary>Records a supplier purchase invoice (direct receipt). Never throws for a business failure — see the result.</summary>
    Task<PurchaseRecordResult> RecordPurchaseAsync(RecordPurchaseRequest request);
}
