using System.Collections.Generic;
using Animora.Contracts.Identity;

namespace Animora.Contracts.Vaccination;

/// <summary>
/// The kind of one vaccination-history row — the lifecycle event recorded against
/// a vaccination record: a shot was administered, a next dose was scheduled, an
/// SMS reminder was sent or a record was rescheduled. The View maps each value
/// 1:1 to a semantic tone + icon — this contract stays free of UI resources.
/// </summary>
public enum VaccinationHistoryKind
{
    /// <summary>«تزریق شد» — a vaccination was administered (MarkDone).</summary>
    Administered,

    /// <summary>«زمان‌بندی شد» — a next dose was scheduled.</summary>
    Scheduled,

    /// <summary>«پیامک ارسال شد» — an SMS reminder was sent (SendSms).</summary>
    SmsSent,

    /// <summary>«باززمان‌بندی شد» — a record's due date was moved.</summary>
    Rescheduled,
}

/// <summary>
/// The vaccination-history result ordering, mirroring the ledger toolbar's
/// «مرتب‌سازی» select. The service applies it before paging so the UI only ever
/// receives one ordered page (PERFORMANCE.md §6).
/// </summary>
public enum VaccinationHistorySort
{
    /// <summary>«جدیدترین» — newest date/time first (default).</summary>
    Newest,

    /// <summary>«قدیمی‌ترین» — oldest date/time first.</summary>
    Oldest,
}

/// <summary>
/// One row of the vaccination history ledger. Deliberately narrow (the columns
/// the table renders) so a large ledger stays cheap to filter/sort server-side
/// and only a single page is ever materialised in the UI (R-18).
/// </summary>
/// <param name="Id">Stable identifier (used for stable ordering / de-duplication).</param>
/// <param name="Kind">The event kind.</param>
/// <param name="KindLabel">The kind's Persian label (resolved by the service so the View stays literal-free).</param>
/// <param name="RecordId">The vaccination record the event belongs to.</param>
/// <param name="PetName">The vaccinated pet.</param>
/// <param name="Species">Species key (drives the icon + label).</param>
/// <param name="OwnerName">The pet's owner.</param>
/// <param name="VaccineName">The vaccine product's display name.</param>
/// <param name="Dose">The dose label (e.g. «نوبت سالانه»).</param>
/// <param name="Date">Event date as an ASCII Jalali <c>yyyy/MM/dd</c> string (rendered through Phase 04).</param>
/// <param name="Time">Event time as an ASCII <c>HH:mm</c> string (rendered through Phase 04).</param>
/// <param name="Note">A short Persian description line, or empty.</param>
/// <param name="Actor">The user who performed the recorded action (audit "who").</param>
public sealed record VaccinationHistoryEntry(
    string Id,
    VaccinationHistoryKind Kind,
    string KindLabel,
    string RecordId,
    string PetName,
    string Species,
    string OwnerName,
    string VaccineName,
    string Dose,
    string Date,
    string Time,
    string Note,
    ActorRef Actor);

/// <summary>
/// The vaccination-history query: a date range, a free-text search, a kind
/// multi-select, a sort and a page window. Mirrors the history tab's toolbar
/// state. The service filters/sorts/pages this on the caller's behalf so no
/// unbounded result set ever crosses the seam.
/// </summary>
/// <param name="FromDate">Inclusive lower bound as an ASCII <c>yyyy/MM/dd</c> Jalali string; empty = unbounded.</param>
/// <param name="ToDate">Inclusive upper bound as an ASCII <c>yyyy/MM/dd</c> Jalali string; empty = unbounded.</param>
/// <param name="Search">Free-text query, matched against pet/owner/vaccine/dose/note.</param>
/// <param name="Kinds">Selected kinds; empty = all.</param>
/// <param name="Sort">The result ordering.</param>
/// <param name="Page">1-based page number.</param>
/// <param name="PageSize">Rows per page.</param>
public sealed record VaccinationHistoryFilter(
    string FromDate,
    string ToDate,
    string Search,
    IReadOnlyCollection<VaccinationHistoryKind> Kinds,
    VaccinationHistorySort Sort,
    int Page,
    int PageSize);

/// <summary>
/// One page of the vaccination history. <paramref name="TotalCount"/> is computed
/// over the <b>entire</b> filtered set (not just the visible page), so the summary
/// bar stays meaningful while the UI only ever holds <paramref name="Items"/>
/// (PERFORMANCE.md §6).
/// </summary>
/// <param name="Items">The requested page's rows, already ordered.</param>
/// <param name="TotalCount">The number of rows matching the filter.</param>
public sealed record VaccinationHistoryPage(
    IReadOnlyList<VaccinationHistoryEntry> Items,
    int TotalCount);
