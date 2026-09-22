using Animora.Contracts.Identity;

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

    /// <summary>«حذف شده» — soft-deleted / cancelled record (muted-danger tone).</summary>
    Deleted,
}

/// <summary>
/// The kind of one invoice audit event — the non-payment lifecycle/editing
/// operations recorded against an invoice. Mirrors the «پذیرش / ویرایش ردیف /
/// تخفیف / بیمه / ابطال / حذف» vocabulary the cashier ledger + the invoice
/// viewer must surface with full detail. The View maps each value 1:1 to a
/// Phase 02 semantic tone + icon — this contract stays free of UI resources.
/// </summary>
public enum InvoiceAuditKind
{
    /// <summary>«ایجاد» — the invoice was created / accepted into the till.</summary>
    Created,

    /// <summary>«افزودن ردیف» — a line was added.</summary>
    ItemAdded,

    /// <summary>«ویرایش ردیف» — a line's quantity/discount was edited.</summary>
    ItemEdited,

    /// <summary>«حذف ردیف» — a line was removed.</summary>
    ItemRemoved,

    /// <summary>«تغییر تخفیف» — the invoice-level discount was changed.</summary>
    DiscountChanged,

    /// <summary>«تغییر بیمه» — the invoice's insurance org was changed.</summary>
    InsuranceChanged,

    /// <summary>«ابطال» — the invoice was voided.</summary>
    Voided,

    /// <summary>«حذف فاکتور» — the invoice was soft-deleted.</summary>
    Deleted,

    /// <summary>«برگشت کالا» — a single line was returned/refunded from a settled invoice.</summary>
    LineReturned,

    /// <summary>«پرداخت» — a payment was recorded (money-moving; also in Payments).</summary>
    PaymentTaken,

    /// <summary>«عودت» — a refund was paid out (money-moving; also in Payments).</summary>
    Refunded,
}

/// <summary>
/// One row of an invoice's audit trail (shown in the invoice viewer's
/// «تاریخچه تغییرات» and folded into the cashier ledger): a timestamp, the
/// event kind, a short Persian action label and a Persian detail describing
/// exactly what changed (so edits/deletes are recorded with their full detail).
/// </summary>
/// <param name="Kind">The audit event kind.</param>
/// <param name="At">Event time as an ASCII <c>HH:mm</c> string (rendered through Phase 04).</param>
/// <param name="Action">The short Persian action label (resolved by the service).</param>
/// <param name="Detail">The Persian description of exactly what changed.</param>
/// <param name="Actor">The user who performed the recorded action (audit "who").</param>
public sealed record InvoiceAuditEntry(
    InvoiceAuditKind Kind,
    string At,
    string Action,
    string Detail,
    ActorRef Actor);

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
/// <paramref name="ReceiptNo"/> is a stable receipt number assigned once the
/// invoice first reaches <see cref="InvoiceState.Settled"/>, empty until then.
/// </summary>
public sealed record InvoiceDetail(
    string Id,
    string ReceiptNo,
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
    bool HasDiscount,
    IReadOnlyList<InvoiceAuditEntry> Audit);

/// <summary>
/// One catalog entry offered by the picker, mirroring the reference's
/// <c>catalogAllItems()</c> rows: a service or physical item with its kind,
/// category group (label + key) and price. The icon is resolved by the mock
/// (mirrors the reference's <c>CAT_ICON</c> map) so the View stays free of
/// catalog literals.
/// </summary>
public sealed record CatalogItem(
    string Id,
    string Kind,
    string Title,
    string GroupLabel,
    string GroupKey,
    long Price,
    string IconKey);

/// <summary>
/// The catalog's category filter chips, mirroring <c>renderCatFilters</c>:
/// «همه» / «خدمات» / «دارو و کالا» plus the top groups, each with its count.
/// </summary>
public sealed record CatalogFilterRef(
    string Key,
    string Label,
    int Count);

/// <summary>
/// A recently-used catalog item chip (mirrors <c>renderCatRecent</c> / the
/// reference's <c>state.recent</c> strip).
/// </summary>
public sealed record RecentItemRef(
    string Kind,
    string Ref);

/// <summary>
/// The full catalog snapshot for the picker: every item, the filter chips and
/// the recent-items strip. Bundled so the modal opens in one round-trip.
/// </summary>
public sealed record CatalogSnapshot(
    IReadOnlyList<CatalogItem> Items,
    IReadOnlyList<CatalogFilterRef> Filters,
    IReadOnlyList<RecentItemRef> Recent);

/// <summary>
/// A custom (non-catalog) line-item request, mirroring the reference's
/// <c>addCustomLine(title, price, qty)</c> — goods/services not in the ready
/// list.
/// </summary>
public sealed record CustomLineRequest(
    string Title,
    long UnitPrice,
    int Quantity);

/// <summary>
/// A payment request, mirroring <c>doPayment</c>'s payload: the chosen method,
/// the amount and an optional note.
/// </summary>
public sealed record PaymentRequest(
    PaymentMethod Method,
    long Amount,
    string Note);

/// <summary>
/// The result of <see cref="ISalesQueryService.TakePaymentAsync"/>, mirroring
/// <c>FLOW.applyPayment</c>'s <c>{ ok, error, entity }</c> return: whether it
/// applied, an optional error, the capped amount actually recorded and the
/// updated invoice detail.
/// </summary>
public sealed record PaymentResult(
    bool Ok,
    string? Error,
    long Applied,
    bool Settled,
    InvoiceDetail Invoice);

/// <summary>
/// A refund request, mirroring <c>openRefundModal</c>/<c>FLOW.refund</c>: the
/// amount to return.
/// </summary>
public sealed record RefundRequest(long Amount);

/// <summary>
/// The result of <see cref="ISalesQueryService.RefundAsync"/>, mirroring
/// <c>FLOW.refund</c>'s <c>{ ok, error, entity }</c> return.
/// </summary>
public sealed record RefundResult(
    bool Ok,
    string? Error,
    InvoiceDetail Invoice);

/// <summary>
/// A single-line return request: the line to return and how many units of it
/// (mirrors «برگشت زدن فقط یک جنس از کل فاکتور»). The service caps the quantity
/// at the line's quantity and refunds the matching share of the paid amount.
/// </summary>
public sealed record LineReturnRequest(int LineIndex, int Quantity);

/// <summary>
/// The result of <see cref="ISalesQueryService.ReturnLineAsync"/>: whether it
/// applied, an optional error, the refunded amount and the updated detail.
/// </summary>
public sealed record LineReturnResult(bool Ok, string? Error, long Refunded, InvoiceDetail Invoice);

/// <summary>An invoice-level discount change (mirrors «تغییر تخفیف»).</summary>
public sealed record DiscountChangeRequest(DiscountKind Kind, int Value);

/// <summary>
/// One owner of the «انتخاب مشتری» picker directory, mirroring the reference's
/// <c>window.OWNERS</c> rows: name, file code, mobile, avatar tone and balance.
/// </summary>
public sealed record OwnerRef(
    string Id,
    string FirstName,
    string LastName,
    string FileCode,
    string Mobile,
    string Tone,
    long Balance,
    string PetName);

/// <summary>
/// The result of <see cref="ISalesQueryService.PickOwnerAsync"/>: either a
/// chosen owner (with an id) or a guest selection (empty id).
/// </summary>
public sealed record OwnerPickResult(
    bool IsGuest,
    string OwnerId,
    string OwnerName,
    string PetName,
    string Phone);

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
