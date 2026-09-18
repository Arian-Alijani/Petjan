namespace Animora.Contracts.Sales;

/// <summary>
/// The source of an invoice, mirroring the reference's <c>src</c> field
/// (<c>Animora-UI/assets/clinic-data.js</c>): issued from a visit/encounter or
/// a direct walk-in sale.
/// </summary>
public enum InvoiceKind
{
    /// <summary>«از ویزیت» — built from a completed encounter.</summary>
    Visit,

    /// <summary>«فروش» — a direct / over-the-counter sale (guest).</summary>
    Sale,
}

/// <summary>
/// The invoice lifecycle state, mirroring the reference's
/// <c>INV_STATES</c> enum: draft / partial / settled / void / refunded. The View
/// maps each value 1:1 to a Phase 02 semantic tone — this contract stays free
/// of UI resources.
/// </summary>
public enum InvoiceState
{
    /// <summary>«پیش‌نویس» — draft (info tone).</summary>
    Draft,

    /// <summary>«تسویه جزئی» — partially paid (warn tone).</summary>
    Partial,

    /// <summary>«تسویه کامل» — fully settled (ok tone).</summary>
    Settled,

    /// <summary>«ابطال شده» — voided (danger tone).</summary>
    Void,

    /// <summary>«مسترد شده» — refunded (muted tone).</summary>
    Refunded,
}

/// <summary>
/// A line item's kind — a clinic service or a physical item, mirroring the
/// reference's <c>kind</c> field. Drives the line tag tone (brand vs. lilac).
/// </summary>
public enum InvoiceLineKind
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
public enum DiscountKind
{
    /// <summary>«درصد» — percentage.</summary>
    Percent,

    /// <summary>«مبلغ» — fixed amount.</summary>
    Amount,
}

/// <summary>One payment method, mirroring the reference's payment-method enum.</summary>
public enum PaymentMethod
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
/// One completed-but-uninvoiced encounter of the «صف ویزیت» list, mirroring
/// what <c>renderItemEnc</c> renders: the encounter id, its patient/owner, the
/// attending doctor, the previewed line count and previewed total, and the
/// avatar tone key.
/// </summary>
public sealed record ReadyEncounterRef(
    string Id,
    string TicketId,
    string OwnerName,
    string PetName,
    string PetKind,
    string DoctorShort,
    string DoctorName,
    string CompletedAt,
    int LineCount,
    long PreviewTotal,
    string AvatarTone,
    string PetInitial);

/// <summary>
/// One invoice row of the «فاکتورها» sidebar list, mirroring what
/// <c>renderItemInv</c> renders: its id, source pill kind, owner/pet/phone,
/// total, due and state.
/// </summary>
public sealed record InvoiceSummary(
    string Id,
    InvoiceKind Kind,
    InvoiceState State,
    string OwnerName,
    string PetName,
    string Phone,
    long Total,
    long Due,
    string AvatarTone,
    string PetInitial);

/// <summary>
/// One line of an invoice, mirroring what <c>renderLine</c> renders: its title,
/// kind, unit price, quantity, per-line discount percent, whether it is a
/// custom (non-catalog) line, its catalog ref key and the resolved icon key.
/// </summary>
public sealed record InvoiceLine(
    int Index,
    string Title,
    InvoiceLineKind Kind,
    long UnitPrice,
    int Quantity,
    int DiscountPercent,
    bool IsCustom,
    string Ref,
    string IconKey,
    string KindLabel);

/// <summary>
/// One historical payment of an invoice, mirroring what <c>renderHistory</c>
/// renders: its method, note, timestamp and signed amount (negative = refund).
/// </summary>
public sealed record InvoicePayment(
    PaymentMethod Method,
    string MethodLabel,
    string Note,
    string At,
    long Amount);

/// <summary>An insurance org offered in the invoice's «پوشش بیمه» select.</summary>
public sealed record InsurerRef(
    string Id,
    string Name,
    int Percent);

/// <summary>
/// The full detail of one invoice, mirroring what <c>renderInvoiceContent</c>
/// renders plus the recomputed totals from <c>FLOW.calcInvoice</c> (§8.2 order:
/// base → discount → tax-on-services → insurance share → total → due). Numeric
/// values are pre-formatted Persian strings so the View renders them verbatim;
/// the raw lengths feed layout decisions only.
/// </summary>
public sealed record InvoiceDetail(
    string Id,
    InvoiceKind Kind,
    InvoiceState State,
    string StateLabel,
    string SourceLabel,
    string OwnerName,
    string PetName,
    string Phone,
    bool IsGuest,
    bool IsLocked,
    string AvatarTone,
    string PetInitial,
    bool HasVisitContext,
    string VisitDoctorName,
    string VisitCompletedAt,
    string InsuranceOrgId,
    string InsuranceLabel,
    IReadOnlyList<InvoiceLine> Lines,
    DiscountKind DiscountKind,
    int DiscountValue,
    IReadOnlyList<InvoicePayment> Payments,
    long Base,
    long DiscountAmount,
    int TaxPercent,
    long Tax,
    long InsuranceShare,
    long Total,
    long Paid,
    long Due,
    bool HasInsurance,
    bool HasDiscount);

/// <summary>
/// A snapshot of the three inline mini-KPIs above the invoice workspace,
/// mirroring <c>renderKPIs()</c>: settled-today sum, open receivables sum + open
/// count and the ready-visit count. Numeric values stay raw; the View formats
/// them through the Phase 04 Persian helpers and applies the reference's badge
/// rules (open-count badge when &gt; 0, ready-visit «در انتظار» badge when &gt; 0).
/// </summary>
public sealed record SaleKpiSnapshot(
    long SettledTodayTotal,
    long OpenDueTotal,
    int OpenCount,
    int ReadyVisitCount);
