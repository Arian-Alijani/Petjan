using System.Collections.Generic;
using Animora.Contracts.Identity;

namespace Animora.Contracts.History;

/// <summary>
/// The module/kind tag of a unified-ledger event, mirroring the reference's
/// <c>CATEGORIES</c> registry keys (Animora-UI/assets/history.js). Each value
/// maps 1:1 to a Phase 02 semantic tone + icon in the View — this contract
/// stays free of UI resources.
/// </summary>
public enum HistoryEventKind
{
    /// <summary>«فروش / درآمد» — sales & daily revenue (ok tone).</summary>
    Sale,

    /// <summary>«نوبت» — appointments (info tone).</summary>
    Appointment,

    /// <summary>«ثبت بیمار» — patient registration (lilac tone).</summary>
    Patient,

    /// <summary>«واکسن» — vaccination (ok tone).</summary>
    Vaccine,

    /// <summary>«هزینه» — expenses (danger tone).</summary>
    Expense,

    /// <summary>«انبار / کالا» — inventory events (warn tone).</summary>
    Inventory,

    /// <summary>«چک» — cheques (muted tone).</summary>
    Cheque,

    /// <summary>«پانسیون» — boarding stays (lilac tone).</summary>
    Boarding,

    /// <summary>«ویزیت» — clinical visits (info tone).</summary>
    Visit,

    /// <summary>«خرید / تأمین» — purchases / restock (warn tone).</summary>
    Purchase,
}

/// <summary>
/// The lifecycle/state chip of an event row, mirroring the reference's
/// <c>STATUS</c> map (Animora-UI/assets/history.js). Each value carries the
/// reference's exact Persian label and Phase 02 tone in the View.
/// </summary>
public enum HistoryEventStatus
{
    /// <summary>«پرداخت شد» (ok).</summary>
    Paid,

    /// <summary>«نیمه‌پرداخت» (warn).</summary>
    Partial,

    /// <summary>«پرداخت نشده» (danger).</summary>
    Unpaid,

    /// <summary>«انجام شد» (ok).</summary>
    Done,

    /// <summary>«لغو شده» (danger).</summary>
    Cancelled,

    /// <summary>«تأیید شده» (ok).</summary>
    Confirmed,

    /// <summary>«در انتظار» (warn).</summary>
    Pending,

    /// <summary>«جدید» (info).</summary>
    New,

    /// <summary>«معوق» (danger).</summary>
    Overdue,

    /// <summary>«تسویه شد» (ok).</summary>
    Cleared,

    /// <summary>«برگشت خورد» (danger).</summary>
    Bounced,

    /// <summary>«موجودی کم» (warn).</summary>
    LowStock,

    /// <summary>«عادی» (mute).</summary>
    Normal,

    /// <summary>«ترخیص» (ok).</summary>
    Discharge,

    /// <summary>«ورود» (info).</summary>
    CheckIn,

    /// <summary>«حذف شد» (danger) — a record was deleted.</summary>
    Removed,
}

/// <summary>
/// A single row of the unified chronological ledger, mirroring the row shape
/// the reference's renderer builds from its per-source event builders
/// (Animora-UI/assets/history.js <c>build*Events</c>): a timestamp, a kind
/// tag, a title + detail line, an entity (owner / doctor / party), a signed
/// amount and a status. The View binds only to this contract (R-18).
/// </summary>
/// <param name="Id">Stable identifier (used for de-duplication, mirroring the reference's <c>id</c>).</param>
/// <param name="Kind">The module/kind tag.</param>
/// <param name="Date">Event date as an ASCII Jalali <c>yyyy/MM/dd</c> string (rendered through Phase 04).</param>
/// <param name="Time">Optional <c>HH:mm</c> time, or empty when the event has no clock part.</param>
/// <param name="Title">The primary description line (Persian).</param>
/// <param name="Detail">The secondary description line, or empty.</param>
/// <param name="Entity">The entity / doctor / party column value, or empty.</param>
/// <param name="Amount">The absolute amount (toman); 0 when the event carries no money.</param>
/// <param name="AmountSign">+1 = income, -1 = expense, 0 = no amount (the reference's <c>amtSign</c>).</param>
/// <param name="Status">The status chip value.</param>
/// <param name="Actor">The user who performed the recorded action (audit "who").</param>
public sealed record HistoryEvent(
    string Id,
    HistoryEventKind Kind,
    string Date,
    string Time,
    string Title,
    string Detail,
    string Entity,
    long Amount,
    int AmountSign,
    HistoryEventStatus Status,
    ActorRef Actor);

/// <summary>
/// The unified-ledger result ordering, mirroring the toolbar's «مرتب‌سازی»
/// select. The service applies it before paging so the UI only ever receives one
/// ordered page (PERFORMANCE.md §6).
/// </summary>
public enum HistorySort
{
    /// <summary>«جدیدترین» — newest date/time first (default).</summary>
    Newest,

    /// <summary>«قدیمی‌ترین» — oldest date/time first.</summary>
    Oldest,

    /// <summary>«بیشترین مبلغ» — largest amount first.</summary>
    AmountDesc,

    /// <summary>«کمترین مبلغ» — smallest amount first.</summary>
    AmountAsc,
}

/// <summary>
/// The filter applied to the ledger, mirroring the toolbar state: a date range
/// (the «رجوع با تاریخ خاص» pair + preset chips), a module-kind multi-select, a
/// free-text search, a sort and a page window. The service filters/sorts/pages
/// this on the caller's behalf so no unbounded result set ever crosses the seam
/// (PERFORMANCE.md §6).
/// </summary>
/// <param name="FromDate">Inclusive lower bound as an ASCII <c>yyyy/MM/dd</c> Jalali string; empty = unbounded.</param>
/// <param name="ToDate">Inclusive upper bound as an ASCII <c>yyyy/MM/dd</c> Jalali string; empty = unbounded.</param>
/// <param name="Kinds">Selected module kinds; empty = all (the reference's <c>cats</c> set).</param>
/// <param name="Search">Free-text query, matched against title/detail/entity/actor.</param>
/// <param name="Sort">The result ordering.</param>
/// <param name="Page">1-based page number.</param>
/// <param name="PageSize">Rows per page.</param>
public sealed record HistoryFilter(
    string FromDate,
    string ToDate,
    IReadOnlyCollection<HistoryEventKind> Kinds,
    string Search,
    HistorySort Sort,
    int Page,
    int PageSize);

/// <summary>
/// One page of the unified ledger. <paramref name="TotalCount"/> and the
/// income/expense sums are computed over the <b>entire</b> filtered set (not just
/// the visible page), so the summary bar stays meaningful while the UI only ever
/// holds <paramref name="Items"/> (PERFORMANCE.md §6).
/// </summary>
/// <param name="Items">The requested page's rows, already ordered.</param>
/// <param name="TotalCount">The number of rows matching the filter.</param>
/// <param name="IncomeTotal">The sum of every matching income row (+).</param>
/// <param name="ExpenseTotal">The sum of every matching expense row (returned as a positive magnitude).</param>
/// <param name="DayNets">
/// Net (income − expense) per Jalali day (<c>yyyy/MM/dd</c>) for each day present on the page, computed over the
/// <b>whole</b> filtered set so a day split across pages shows the same full-day net on every page.
/// </param>
public sealed record HistoryPage(
    IReadOnlyList<HistoryEvent> Items,
    int TotalCount,
    long IncomeTotal,
    long ExpenseTotal,
    IReadOnlyDictionary<string, long>? DayNets = null);
