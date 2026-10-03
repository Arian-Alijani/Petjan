namespace Animora.Contracts.Cashier;

/// <summary>
/// The cashier's invoice status filter, mirroring the reference's
/// <c>state.tab</c> vocabulary in <c>Animora-UI/assets/cashier.js</c>
/// (<c>tabMatches</c>): open (draft/partial), settled, void (void/refunded) and
/// all.
/// </summary>
public enum CashierTab
{
    /// <summary>«باز» — draft + partial invoices.</summary>
    Open,

    /// <summary>«تسویه‌شده» — settled invoices.</summary>
    Settled,

    /// <summary>«ابطال‌شده» — void + refunded invoices.</summary>
    Void,

    /// <summary>«همه» — every invoice.</summary>
    All,
}

/// <summary>
/// The invoice lifecycle state as the cashier screen sees it, mirroring the
/// reference's <c>STATE_META</c> vocabulary (draft / partial / settled / void /
/// refunded). The View maps each value 1:1 to a Phase 02 semantic tone — this
/// contract stays free of UI resources. Defined locally (rather than reusing
/// <c>Animora.Contracts.Sales.InvoiceState</c>) to respect the Cashier/Sales
/// bounded-context boundary (ARCHITECTURE.md §6).
/// </summary>
public enum CashierInvoiceState
{
    /// <summary>«پیش‌نویس» — draft (info tone).</summary>
    Draft,

    /// <summary>«تسویه جزئی» — partially paid (warn tone).</summary>
    Partial,

    /// <summary>«تسویه شد» — fully settled (ok tone).</summary>
    Settled,

    /// <summary>«ابطال» — voided (danger tone).</summary>
    Void,

    /// <summary>«عودت» — refunded (mute tone).</summary>
    Refunded,
}

/// <summary>
/// A line's kind in the cashier detail, mirroring the reference's <c>kind</c>
/// field: a service (خدمت) or a physical item (کالا). Drives the line tag tone
/// (brand vs. lilac).
/// </summary>
public enum CashierLineKind
{
    /// <summary>A service (خدمت) — brand-toned tag.</summary>
    Service,

    /// <summary>A physical item (کالا) — lilac-toned tag.</summary>
    Item,
}

/// <summary>
/// The invoice-level discount mode, mirroring the reference's
/// <c>discount.kind</c> — a percentage or a fixed amount.
/// </summary>
public enum CashierDiscountKind
{
    /// <summary>«درصد» — percentage.</summary>
    Percent,

    /// <summary>«مبلغ» — fixed amount.</summary>
    Amount,
}

/// <summary>One payment method, mirroring the reference's <c>METHOD_META</c>.</summary>
public enum CashierPaymentMethod
{
    /// <summary>«نقدی» — cash.</summary>
    Cash,

    /// <summary>«کارت» — card.</summary>
    Card,

    /// <summary>«چک» — cheque.</summary>
    Cheque,

    /// <summary>«نسیه» — credit.</summary>
    Credit,

    /// <summary>«ترکیبی» — mixed.</summary>
    Mixed,
}

/// <summary>
/// One invoice row of the left-hand list, mirroring what <c>renderList</c>
/// renders: its id, state, owner/pet, date, total, paid and due. Numeric values
/// stay raw; the View formats them through the Phase 04 Persian helpers.
/// </summary>
public sealed record CashierInvoice(
    string Id,
    CashierInvoiceState State,
    string StateLabel,
    string OwnerName,
    string PetName,
    string Date,
    long Total,
    long Paid,
    long Due);

/// <summary>
/// One line of an invoice's detail, mirroring what <c>renderLines</c> renders:
/// its kind, title, unit price, quantity, per-line discount percent and the
/// derived subtotal (<c>qty × price × (100 − discPct) / 100</c>).
/// </summary>
public sealed record CashierInvoiceLine(
    int Index,
    CashierLineKind Kind,
    string Title,
    long UnitPrice,
    int Quantity,
    int DiscountPercent,
    long Subtotal = 0);

/// <summary>
/// One historical payment of an invoice, mirroring what
/// <c>renderPaymentHistory</c> renders: its method, reference, note, timestamp
/// and signed amount (negative = refund).
/// </summary>
public sealed record CashierPayment(
    CashierPaymentMethod Method,
    string MethodLabel,
    string Reference,
    string Note,
    string At,
    long Amount);

/// <summary>An insurance org offered in the detail's «بیمه» select.</summary>
public sealed record CashierInsurer(
    string Id,
    string Name,
    int Percent);

/// <summary>
/// One catalog entry offered by the detail's add-line select, mirroring what
/// <c>renderLines</c>'s add-row lists (the reference's SERVICES + ITEMS): a
/// service or item with its kind, title and price.
/// </summary>
public sealed record CashierCatalogItem(
    string Id,
    CashierLineKind Kind,
    string Title,
    long Price);

/// <summary>
/// The full detail of one invoice, mirroring what <c>renderDetail</c> renders
/// plus the recomputed totals from <c>FLOW.calcInvoice</c> (§8.2 order: base →
/// discount → tax-on-services → insurance share → total → due). Numeric values
/// stay raw; the View formats them through the Phase 04 Persian helpers.
/// </summary>
public sealed record CashierInvoiceDetail(
    string Id,
    CashierInvoiceState State,
    string StateLabel,
    string OwnerName,
    string PetName,
    string Phone,
    string Date,
    bool IsEditable,
    string InsuranceOrgId,
    CashierDiscountKind DiscountKind,
    int DiscountValue,
    IReadOnlyList<CashierInvoiceLine> Lines,
    IReadOnlyList<CashierPayment> Payments,
    long Base,
    long DiscountAmount,
    long AfterDiscount,
    int TaxPercent,
    long Tax,
    long InsuranceShare,
    long Total,
    long Paid,
    long Due);

/// <summary>
/// One overview stat card, mirroring what <c>renderStats</c> renders: a tone,
/// icon key, label, formatted value and footnote. The icon key is resolved by
/// the mock so the View stays free of screen literals.
/// </summary>
public sealed record CashierStat(
    string Tone,
    string IconKey,
    string Label,
    long Value,
    string Foot,
    bool IsMoney);

/// <summary>
/// One tab's live count, mirroring <c>renderTabs</c>: the tab key, its Persian
/// label and the invoice count in that tab.
/// </summary>
public sealed record CashierTabCount(
    CashierTab Tab,
    string Label,
    int Count);

/// <summary>
/// A payment request, mirroring <c>doPay</c>'s payload: the chosen method, the
/// amount and an optional reference/note.
/// </summary>
public sealed record CashierPayRequest(
    CashierPaymentMethod Method,
    long Amount,
    string Reference,
    string Note);

/// <summary>
/// A refund request, mirroring <c>doRefund</c>: the amount to return.
/// </summary>
public sealed record CashierRefundRequest(long Amount);

/// <summary>
/// The result of <see cref="ICashierService.RefundAsync"/> / a void, mirroring
/// the reference flow's <c>{ ok, error, entity }</c> return.
/// </summary>
/// <param name="Ok">Whether the action was applied.</param>
/// <param name="Error">Machine-readable error code when <paramref name="Ok"/> is false.</param>
/// <param name="Invoice">The invoice as it is after the action (null when it no longer exists).</param>
/// <param name="Applied">The money actually recorded by a pay/refund (after clamping to due/paid), 0 otherwise.</param>
public sealed record CashierActionResult(
    bool Ok,
    string? Error,
    CashierInvoiceDetail? Invoice,
    long Applied = 0);
