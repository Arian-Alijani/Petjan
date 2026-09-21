namespace Animora.Contracts.Sales;

/// <summary>
/// The kind of one cashier-history row — the money/lifecycle event recorded
/// against an invoice. Mirrors the «موارد پذیرش شده و پرداخت شده و کنسل شده و
/// …» vocabulary the Sale/POS screen's history tab must surface: the invoice
/// being issued (acceptance), a payment, a full settlement or a refund, and the
/// terminal void/cancel. The View maps each value 1:1 to a Phase 02 semantic
/// tone + icon — this contract stays free of UI resources.
/// </summary>
public enum CashierHistoryKind
{
    /// <summary>«پذیرش» — the invoice was issued/accepted into the till.</summary>
    Accepted,

    /// <summary>«پرداخت» — a (full or partial) payment was recorded.</summary>
    Paid,

    /// <summary>«تسویه» — the invoice became fully settled.</summary>
    Settled,

    /// <summary>«کنسل» — the invoice was voided / cancelled.</summary>
    Cancelled,

    /// <summary>«عودت» — a refund was paid back out.</summary>
    Refunded,
}

/// <summary>
/// The cashier-history result ordering, mirroring the history tab's «مرتب‌سازی»
/// select. The service applies it before paging so the UI only ever receives one
/// ordered page (PERFORMANCE.md §6).
/// </summary>
public enum CashierHistorySort
{
    /// <summary>«جدیدترین» — newest date/time first (default).</summary>
    Newest,

    /// <summary>«قدیمی‌ترین» — oldest date/time first.</summary>
    Oldest,

    /// <summary>«بیشترین مبلغ» — largest amount first.</summary>
    AmountDesc,

    /// <summary>«کمترین مبلغ» — smallest amount first.</summary>
    AmountAsc,

    /// <summary>«نام مالک (الفبا)» — owner name A–Z, then newest.</summary>
    Owner,
}

/// <summary>
/// One row of the cashier history ledger. Deliberately narrow (the columns the
/// table renders) so a large ledger stays cheap to filter/sort server-side and
/// only a single page is ever materialised in the UI (R-18).
/// </summary>
/// <param name="Id">Stable identifier (used for stable ordering / de-duplication).</param>
/// <param name="Kind">The event kind.</param>
/// <param name="KindLabel">The kind's Persian label (resolved by the service so the View stays literal-free).</param>
/// <param name="InvoiceId">The invoice the event belongs to.</param>
/// <param name="OwnerName">The invoice owner, or empty for a walk-in.</param>
/// <param name="PetName">The patient, or empty.</param>
/// <param name="Date">Event date as an ASCII Jalali <c>yyyy/MM/dd</c> string (rendered through Phase 04).</param>
/// <param name="Time">Event time as an ASCII <c>HH:mm</c> string (rendered through Phase 04).</param>
/// <param name="Amount">The absolute toman amount; 0 when the event carries no money.</param>
/// <param name="AmountSign">+1 = inflow, −1 = outflow, 0 = no money movement.</param>
/// <param name="State">The invoice's resulting lifecycle state (drives the status chip).</param>
/// <param name="StateLabel">The state's Persian label (resolved by the service).</param>
/// <param name="Note">A short Persian description line, or empty.</param>
public sealed record CashierHistoryEntry(
    string Id,
    CashierHistoryKind Kind,
    string KindLabel,
    string InvoiceId,
    string OwnerName,
    string PetName,
    string Date,
    string Time,
    long Amount,
    int AmountSign,
    InvoiceState State,
    string StateLabel,
    string Note);

/// <summary>
/// The cashier-history query: a date range (the «رجوع با تاریخ خاص» pair), a
/// free-text search, a kind multi-select, a sort and a page window. Mirrors the
/// history tab's toolbar state. The service filters/sorts/pages this on the
/// caller's behalf so no unbounded result set ever crosses the seam.
/// </summary>
/// <param name="FromDate">Inclusive lower bound as an ASCII <c>yyyy/MM/dd</c> Jalali string; empty = unbounded.</param>
/// <param name="ToDate">Inclusive upper bound as an ASCII <c>yyyy/MM/dd</c> Jalali string; empty = unbounded.</param>
/// <param name="Search">Free-text query, matched against invoice/owner/pet/note.</param>
/// <param name="Kinds">Selected kinds; empty = all.</param>
/// <param name="Sort">The result ordering.</param>
/// <param name="Page">1-based page number.</param>
/// <param name="PageSize">Rows per page.</param>
public sealed record CashierHistoryFilter(
    string FromDate,
    string ToDate,
    string Search,
    IReadOnlyCollection<CashierHistoryKind> Kinds,
    CashierHistorySort Sort,
    int Page,
    int PageSize);

/// <summary>
/// One page of the cashier history. <paramref name="TotalCount"/> and the
/// inflow/outflow sums are computed over the <b>entire</b> filtered set (not just
/// the visible page), so the summary bar stays meaningful while the UI only ever
/// holds <paramref name="Items"/> (PERFORMANCE.md §6).
/// </summary>
/// <param name="Items">The requested page's rows, already ordered.</param>
/// <param name="TotalCount">The number of rows matching the filter.</param>
/// <param name="InflowTotal">The sum of every matching inflow row (+).</param>
/// <param name="OutflowTotal">The sum of every matching outflow row (returned as a positive magnitude).</param>
public sealed record CashierHistoryPage(
    IReadOnlyList<CashierHistoryEntry> Items,
    int TotalCount,
    long InflowTotal,
    long OutflowTotal);
