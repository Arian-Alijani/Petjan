using System.Collections.Generic;
using Animora.Contracts.Identity;

namespace Animora.Contracts.Boarding;

/// <summary>
/// The kind of one boarding-history row — the lifecycle event recorded against a
/// stay. Mirrors the mutations that already exist on
/// <see cref="IBoardingQueryService"/>: a new stay is checked in, extra items
/// are added/removed, a stay's invoice is transferred and finally the stay is
/// checked out. The View maps each value 1:1 to a semantic tone + icon — this
/// contract stays free of UI resources.
/// </summary>
public enum BoardingHistoryKind
{
    /// <summary>«پذیرش» — a new stay was checked in.</summary>
    CheckIn,

    /// <summary>«افزودن مورد» — a consumed service/good was added.</summary>
    ItemAdded,

    /// <summary>«حذف مورد» — a consumed service/good was removed.</summary>
    ItemRemoved,

    /// <summary>«انتقال به صندوق» — the stay's invoice was transferred.</summary>
    Transferred,

    /// <summary>«تخلیه» — the stay was checked out.</summary>
    CheckOut,
}

/// <summary>
/// The boarding-history result ordering, mirroring the ledger toolbar's
/// «مرتب‌سازی» select. The service applies it before paging so the UI only ever
/// receives one ordered page (PERFORMANCE.md §6).
/// </summary>
public enum BoardingHistorySort
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
/// One row of the boarding history ledger. Deliberately narrow (the columns the
/// table renders) so a large ledger stays cheap to filter/sort server-side and
/// only a single page is ever materialised in the UI (R-18).
/// </summary>
/// <param name="Id">Stable identifier (used for stable ordering / de-duplication).</param>
/// <param name="Kind">The event kind.</param>
/// <param name="KindLabel">The kind's Persian label (resolved by the service so the View stays literal-free).</param>
/// <param name="StayId">The stay the event belongs to.</param>
/// <param name="PetName">The boarded pet.</param>
/// <param name="Species">Species enum used for the icon key and label.</param>
/// <param name="OwnerName">The stay owner.</param>
/// <param name="RoomLabel">Room code / label.</param>
/// <param name="Date">Event date as an ASCII Jalali <c>yyyy/MM/dd</c> string (rendered through Phase 04).</param>
/// <param name="Time">Event time as an ASCII <c>HH:mm</c> string (rendered through Phase 04).</param>
/// <param name="Nights">The stay's night count at the time of the event (0 when not applicable).</param>
/// <param name="Amount">The absolute amount in Rial; 0 when the event carries no money.</param>
/// <param name="AmountSign">+1 = inflow, −1 = outflow, 0 = no money movement.</param>
/// <param name="Note">A short Persian description line, or empty.</param>
/// <param name="Actor">The user who performed the recorded action (audit "who").</param>
public sealed record BoardingHistoryEntry(
    string Id,
    BoardingHistoryKind Kind,
    string KindLabel,
    string StayId,
    string PetName,
    BoardingSpecies Species,
    string OwnerName,
    string RoomLabel,
    string Date,
    string Time,
    int Nights,
    long Amount,
    int AmountSign,
    string Note,
    ActorRef Actor);

/// <summary>
/// The boarding-history query: a date range, a free-text search, a kind
/// multi-select, the boarding-specific species filter, a sort and a page window.
/// Mirrors the history tab's toolbar state. The service filters/sorts/pages this
/// on the caller's behalf so no unbounded result set ever crosses the seam.
/// </summary>
/// <param name="FromDate">Inclusive lower bound as an ASCII <c>yyyy/MM/dd</c> Jalali string; empty = unbounded.</param>
/// <param name="ToDate">Inclusive upper bound as an ASCII <c>yyyy/MM/dd</c> Jalali string; empty = unbounded.</param>
/// <param name="Search">Free-text query, matched against pet/owner/room/note.</param>
/// <param name="Kinds">Selected kinds; empty = all.</param>
/// <param name="Species">Optional species filter (boarding-specific); null = all.</param>
/// <param name="Sort">The result ordering.</param>
/// <param name="Page">1-based page number.</param>
/// <param name="PageSize">Rows per page.</param>
public sealed record BoardingHistoryFilter(
    string FromDate,
    string ToDate,
    string Search,
    IReadOnlyCollection<BoardingHistoryKind> Kinds,
    BoardingSpecies? Species,
    BoardingHistorySort Sort,
    int Page,
    int PageSize);

/// <summary>
/// One page of the boarding history. <paramref name="TotalCount"/> and the
/// inflow/outflow sums are computed over the <b>entire</b> filtered set (not just
/// the visible page), so the summary bar stays meaningful while the UI only ever
/// holds <paramref name="Items"/> (PERFORMANCE.md §6).
/// </summary>
/// <param name="Items">The requested page's rows, already ordered.</param>
/// <param name="TotalCount">The number of rows matching the filter.</param>
/// <param name="InflowTotal">The sum of every matching inflow row (+).</param>
/// <param name="OutflowTotal">The sum of every matching outflow row (returned as a positive magnitude).</param>
public sealed record BoardingHistoryPage(
    IReadOnlyList<BoardingHistoryEntry> Items,
    int TotalCount,
    long InflowTotal,
    long OutflowTotal);
