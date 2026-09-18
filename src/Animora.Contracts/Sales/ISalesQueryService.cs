namespace Animora.Contracts.Sales;

/// <summary>
/// Data seam for the Sale/POS invoice workspace. The desktop ViewModel depends
/// only on this interface; the in-memory <c>MockSalesQueryService</c> is the
/// single swappable implementation a future backend replaces (R-18). The shape
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
    /// Creates a new empty invoice — for a walk-in/guest direct sale (mirrors
    /// <c>newBlankInvoice()</c>) — and returns its detail.
    /// </summary>
    Task<InvoiceDetail> CreateBlankAsync(InvoiceKind kind);

    /// <summary>
    /// The insurance orgs offered in the invoice's «پوشش بیمه» select
    /// (mirrors <c>C().INSURERS</c>), used to render the selected org's label.
    /// </summary>
    Task<IReadOnlyList<InsurerRef>> GetInsurersAsync();

    /// <summary>
    /// The three inline mini-KPIs above the workspace (mirrors
    /// <c>renderKPIs()</c>): settled-today sum, open receivables sum + count and
    /// the ready-visit count.
    /// </summary>
    Task<SaleKpiSnapshot> GetKpisAsync();
}
