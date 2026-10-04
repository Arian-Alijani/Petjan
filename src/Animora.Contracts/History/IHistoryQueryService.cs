using System.Threading.Tasks;

namespace Animora.Contracts.History;

/// <summary>
/// Data seam for the History (unified ledger) screen. The desktop ViewModel
/// depends only on this interface; the in-memory
/// <c>MockHistoryQueryService</c> is the single swappable implementation a
/// future backend/reporting layer replaces (R-18). The shape mirrors what
/// <c>Animora-UI/assets/history.js</c> renders: one chronological feed of
/// cross-module events plus a CSV export action.
/// <para>
/// <b>Performance.</b> Tens of thousands of ledger rows are expected over time,
/// so the query is <b>server-side</b>: the filter + sort + page window go to
/// <see cref="GetPageAsync"/>, which returns exactly one bounded page plus the
/// totals. The UI therefore never materialises the whole ledger
/// (PERFORMANCE.md §6).
/// </para>
/// </summary>
public interface IHistoryQueryService
{
    /// <summary>
    /// One ordered page of the unified ledger plus the totals computed over the
    /// <b>whole</b> filtered set, mirroring the reference's aggregate-then-filter
    /// pipeline (Animora-UI/assets/history.js).
    /// </summary>
    Task<HistoryPage> GetPageAsync(HistoryFilter filter);

    /// <summary>
    /// Renders the <b>whole</b> filtered ledger as CSV text (mirroring the
    /// reference's <c>HIST.exportCSV</c> column set — export is not paged). The
    /// actual file-save dialog is a thin platform call in the View/command, not
    /// this service. Dates are clinic-local Jalali and every text column is
    /// localized through <paramref name="labels"/> so the file matches the
    /// on-screen ledger row-for-row.
    /// </summary>
    Task<string> ExportCsvAsync(HistoryFilter filter, HistoryCsvLabels labels);
}

/// <summary>
/// Presentation hooks the export needs from the UI layer (the localization tables live in
/// the Desktop project). The service formats every row once through these delegates, so the
/// exported ledger is identical to what the screen renders — no post-hoc string patching.
/// </summary>
/// <param name="Header">Localized column headers, in column order (Date, Time, Title, Detail, Entity, Amount, Sign, Status, Actor).</param>
/// <param name="Title">Maps a stable title key to its Persian title.</param>
/// <param name="Detail">Maps the resolved detail text written at feed-write time to its Persian rendering.</param>
/// <param name="Status">Maps a status to its Persian chip label.</param>
public sealed record HistoryCsvLabels(
    IReadOnlyList<string> Header,
    Func<string, string> Title,
    Func<string, string> Detail,
    Func<HistoryEventStatus, string> Status);
