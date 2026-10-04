namespace Animora.Contracts.Sales;

/// <summary>
/// Data seam for the Sale/POS invoice workspace. The desktop ViewModel depends
/// only on this interface; the real local-store SalesQueryService supplies the
/// implementation (R-18). The shape
/// mirrors what <c>Animora-UI/assets/sale-page.js</c> renders for the sidebar
/// (ready encounters + all invoices) and the invoice work surface.
/// </summary>
public interface ISalesQueryService
{
    /// <summary>
    /// The «صف ویزیت» list — completed encounters that have not yet been
    /// invoiced (mirrors <c>readyEncounters()</c>).
    /// </summary>
    Task<IReadOnlyList<ReadyEncounterRef>> GetReadyEncountersAsync();

    /// <summary>
    /// Every invoice, newest first (mirrors <c>invoicesAll()</c>).
    /// </summary>
    Task<IReadOnlyList<InvoiceSummary>> GetInvoicesAsync();

    /// <summary>
    /// The invoices registered for one owner (owner file), newest first and
    /// bounded server-side — never the full ledger.
    /// </summary>
    Task<IReadOnlyList<InvoiceSummary>> GetInvoicesForOwnerAsync(string ownerId);

    /// <summary>
    /// The full detail of one invoice (mirrors <c>C().invoice(id)</c> plus the
    /// recomputed totals), or <c>null</c> when the id is unknown.
    /// </summary>
    Task<InvoiceDetail?> GetInvoiceAsync(string invoiceId);

    /// <summary>
    /// Issues a new invoice from a completed encounter and returns its detail
    /// (mirrors <c>FLOW.buildInvoice(enc)</c>).
    /// </summary>
    Task<InvoiceDetail> CreateFromEncounterAsync(string encounterId);

    /// <summary>
    /// Creates a new empty counter-sale invoice — for a walk-in/guest direct sale (mirrors
    /// <c>newBlankInvoice()</c>) — and returns its detail. A blank document is always a sale;
    /// visit/stay invoices come from <see cref="CreateFromEncounterAsync"/> (X15: no ignored kind parameter).
    /// </summary>
    Task<InvoiceDetail> CreateBlankAsync();

    /// <summary>
    /// The insurance orgs offered in the invoice's «پوشش بیمه» select
    /// (mirrors <c>C().INSURERS</c>), used to render the selected org's label.
    /// </summary>
    Task<IReadOnlyList<InsurerRef>> GetInsurersAsync();

    /// <summary>
    /// The three inline mini-KPIs above the workspace (mirrors
    /// <c>renderKPIs()</c>): settled-today sum, open receivables sum + count and
    /// the ready-visit count. The page already holds the invoice list and the ready queue, so it passes them in:
    /// only the settled-today figure needs its own query; open count/due are summed from
    /// <paramref name="invoices"/> and the ready count is <paramref name="readyVisitCount"/> (X28).
    /// </summary>
    Task<SaleKpiSnapshot> GetKpisAsync(IReadOnlyList<InvoiceSummary> invoices, int readyVisitCount);

    // ── Phase 21 — catalog picker, payment, void/refund, owner picker ──────

    /// <summary>
    /// The full catalog snapshot for the picker (mirrors
    /// <c>catalogAllItems()</c> + <c>renderCatFilters</c> + <c>renderCatRecent</c>):
    /// every service/item with its group, the filter chips and the recent strip.
    /// </summary>
    Task<CatalogSnapshot> GetCatalogAsync();

    /// <summary>
    /// Adds a catalog item to an invoice (mirrors <c>addToInvoice</c>): an
    /// existing matching line's quantity is incremented, otherwise a new line is
    /// appended; the item is pushed onto the recent strip. Returns the updated
    /// detail.
    /// </summary>
    Task<InvoiceDetail> AddLineAsync(string invoiceId, CatalogItem item, int quantity);

    /// <summary>
    /// Adds a custom (non-catalog) line (mirrors <c>addCustomLine</c>). Returns
    /// the updated detail.
    /// </summary>
    Task<InvoiceDetail> AddCustomLineAsync(string invoiceId, CustomLineRequest request);

    /// <summary>
    /// Records a payment (mirrors <c>FLOW.applyPayment</c>): caps at the due,
    /// appends a payment row, advances the state to partial/settled.
    /// </summary>
    Task<PaymentResult> TakePaymentAsync(string invoiceId, PaymentRequest request);

    /// <summary>
    /// Voids an invoice (mirrors <c>FLOW.voidInvoice</c>). Returns the updated
    /// detail, or <c>null</c> when the state forbids it.
    /// </summary>
    Task<InvoiceDetail?> VoidAsync(string invoiceId);

    // ── Invoice viewer — line editing + soft delete ───────────────────────

    /// <summary>
    /// Edits an editable invoice line's quantity and per-line discount
    /// (mirrors the reference's inline line editors). Records a full
    /// «ویرایش ردیف» audit entry so the change appears in the invoice's change
    /// log and the cashier ledger. <paramref name="lineId"/> is the id of the line the user saw: when the
    /// invoice changed in between (the line moved or is gone) the call fails with
    /// <c>INVOICE_LINE_INDEX_STALE</c> instead of editing a different line. Returns the recomputed detail, or
    /// <c>null</c> when the invoice is unknown or not editable.
    /// </summary>
    Task<InvoiceDetail?> EditLineAsync(string invoiceId, int lineIndex, string lineId, int quantity, int discountPercent);

    /// <summary>
    /// Removes a line from an editable invoice and records a «حذف ردیف» audit
    /// entry. <paramref name="lineId"/> identifies the line the user saw; a stale index fails with
    /// <c>INVOICE_LINE_INDEX_STALE</c>. Returns the recomputed detail, or <c>null</c> when the invoice is
    /// unknown or not editable.
    /// </summary>
    Task<InvoiceDetail?> RemoveLineAsync(string invoiceId, int lineIndex, string lineId);

    /// <summary>
    /// Soft-deletes an invoice (records a «حذف فاکتور» audit entry and moves the
    /// state to <see cref="InvoiceState.Deleted"/>) without discarding its data,
    /// so the history ledger keeps the full record. Returns the updated detail,
    /// or <c>null</c> when the invoice is unknown.
    /// </summary>
    Task<InvoiceDetail?> DeleteInvoiceAsync(string invoiceId);

    /// <summary>
    /// Refunds a paid invoice (mirrors <c>FLOW.refund</c>): subtracts from paid,
    /// appends a negative payment row and moves the state to refunded.
    /// </summary>
    Task<RefundResult> RefundAsync(string invoiceId, RefundRequest request);

    /// <summary>Returns a single line (or part of it) from a paid invoice, refunding its share.</summary>
    Task<LineReturnResult> ReturnLineAsync(string invoiceId, LineReturnRequest request);

    /// <summary>Changes an editable invoice's invoice-level discount (records «تغییر تخفیف»).</summary>
    Task<InvoiceDetail?> SetDiscountAsync(string invoiceId, DiscountChangeRequest request);

    /// <summary>Changes an editable invoice's insurance org, or clears it (records «تغییر بیمه»).</summary>
    Task<InvoiceDetail?> SetInsuranceAsync(string invoiceId, string? insurerId);

    /// <summary>
    /// The owner directory for «انتخاب مشتری» (mirrors <c>openPicker</c>'s
    /// <c>window.OWNERS</c>), optionally filtered by <paramref name="query"/>.
    /// </summary>
    Task<IReadOnlyList<OwnerRef>> GetOwnersAsync(string query);

    /// <summary>
    /// Assigns an owner (or a guest) to an invoice (mirrors
    /// <c>selectOwner</c>/<c>selectGuest</c>). Returns the updated detail.
    /// </summary>
    Task<InvoiceDetail> PickOwnerAsync(string invoiceId, OwnerPickResult pick);

    // ── Cashier history — the «تاریخچه صندوق» ledger ───────────────────────

    /// <summary>
    /// One page of the cashier-sales ledger (accepted / paid / settled /
    /// cancelled / refunded events). The filter carries the date range, search,
    /// kind selection, sort and page window; the <b>service</b> applies all of
    /// them — including the sort and the paging — so the UI only ever receives
    /// one bounded page while the total count and the inflow/outflow sums stay
    /// computed over the whole filtered set (PERFORMANCE.md §6). This keeps the
    /// history tab fast even when the ledger grows to tens of thousands of rows.
    /// </summary>
    Task<CashierHistoryPage> GetHistoryAsync(CashierHistoryFilter filter);
}
