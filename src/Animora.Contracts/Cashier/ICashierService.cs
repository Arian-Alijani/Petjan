namespace Animora.Contracts.Cashier;

/// <summary>
/// Data seam for the Cashier (invoice &amp; payment management) screen. The
/// desktop ViewModel depends only on this interface; the in-memory
/// <c>MockCashierService</c> is the single swappable implementation a future
/// backend replaces (R-18). The shape mirrors what
/// <c>Animora-UI/assets/cashier.js</c> renders: the status-tab filtered invoice
/// list, the overview stats, and the detail panel (lines/totals/payment
/// panel/payment history/actions).
/// </summary>
public interface ICashierService
{
    /// <summary>
    /// Every invoice, mirroring <c>C().INVOICES</c> (the list <c>renderList</c>
    /// filters by tab). Newest-first ordering is applied by the ViewModel to
    /// match the reference's sort.
    /// </summary>
    Task<IReadOnlyList<CashierInvoice>> GetInvoicesAsync();

    /// <summary>
    /// The full detail of one invoice (mirrors <c>C().invoice(id)</c> plus the
    /// recomputed totals from <c>FLOW.calcInvoice</c>), or <c>null</c> when the
    /// id is unknown.
    /// </summary>
    Task<CashierInvoiceDetail?> GetDetailAsync(string invoiceId);

    /// <summary>
    /// The four overview stat cards above the list (mirrors
    /// <c>renderStats</c>): open receivables, settled total, today's cash flow
    /// and the invoice count.
    /// </summary>
    Task<IReadOnlyList<CashierStat>> GetStatsAsync();

    /// <summary>
    /// The tab strip's live counts (mirrors <c>renderTabs</c>).
    /// </summary>
    Task<IReadOnlyList<CashierTabCount>> GetTabCountsAsync();

    /// <summary>
    /// The insurance orgs offered in the detail's «بیمه» select (mirrors
    /// <c>C().INSURERS</c>).
    /// </summary>
    Task<IReadOnlyList<CashierInsurer>> GetInsurersAsync();

    /// <summary>
    /// The catalog offered in the detail's add-line select (mirrors the
    /// reference's <c>C().SERVICES</c> + <c>C().ITEMS</c>).
    /// </summary>
    Task<IReadOnlyList<CashierCatalogItem>> GetCatalogItemsAsync();

    /// <summary>
    /// Records a payment against an invoice (mirrors <c>FLOW.applyPayment</c>):
    /// caps at the due, appends a payment row, advances the state to
    /// partial/settled. Returns the updated detail plus whether it applied.
    /// </summary>
    Task<CashierActionResult> PayAsync(string invoiceId, CashierPayRequest request);

    /// <summary>
    /// Voids an invoice (mirrors <c>FLOW.voidInvoice</c>): draft/partial only.
    /// </summary>
    Task<CashierActionResult> VoidAsync(string invoiceId);

    /// <summary>
    /// Refunds a paid invoice (mirrors <c>FLOW.refund</c>): subtracts from paid,
    /// appends a negative payment row and moves the state to refunded.
    /// </summary>
    Task<CashierActionResult> RefundAsync(string invoiceId, CashierRefundRequest request);

    /// <summary>
    /// Edits an editable invoice's lines/discount/insurance (mirrors the
    /// reference's inline line-edit + discount/insurance bindings), returning
    /// the recomputed detail.
    /// </summary>
    Task<CashierInvoiceDetail> UpdateLineAsync(string invoiceId, int lineIndex, int quantity, int discountPercent);

    /// <summary>Removes a line from an editable invoice (mirrors <c>data-act="rm"</c>).</summary>
    Task<CashierInvoiceDetail> RemoveLineAsync(string invoiceId, int lineIndex);

    /// <summary>Adds a catalog line to an editable invoice (mirrors <c>#cs-add-line-btn</c>).</summary>
    Task<CashierInvoiceDetail> AddLineAsync(string invoiceId, string catalogItemId);

    /// <summary>Sets the invoice-level discount (mirrors <c>#cs-disc-kind</c>/<c>#cs-disc-val</c>).</summary>
    Task<CashierInvoiceDetail> SetDiscountAsync(string invoiceId, CashierDiscountKind kind, int value);

    /// <summary>Sets (or clears) the invoice's insurance org (mirrors <c>#cs-ins</c>).</summary>
    Task<CashierInvoiceDetail> SetInsuranceAsync(string invoiceId, string? insurerId);

    /// <summary>Recomputes an invoice's cached totals (mirrors <c>data-action="refresh"</c>).</summary>
    Task<CashierInvoiceDetail> RecalculateAsync(string invoiceId);
}
