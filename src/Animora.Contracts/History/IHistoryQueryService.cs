using System.Collections.Generic;
using System.Threading.Tasks;

namespace Animora.Contracts.History;

/// <summary>
/// Data seam for the History (unified ledger) screen. The desktop ViewModel
/// depends only on this interface; the in-memory
/// <c>MockHistoryQueryService</c> is the single swappable implementation a
/// future backend/reporting layer replaces (R-18). The shape mirrors what
/// <c>Animora-UI/assets/history.js</c> renders: one chronological feed of
/// cross-module events plus a CSV export action.
/// </summary>
public interface IHistoryQueryService
{
    /// <summary>
    /// Every unified-ledger event, newest-first, mirroring the reference's
    /// <c>ALL_EVENTS</c> aggregate. The ViewModel applies the period /
    /// module-kind / search filters on top of this set.
    /// </summary>
    Task<IReadOnlyList<HistoryEvent>> GetEventsAsync(HistoryFilter filter);

    /// <summary>
    /// Renders the filtered ledger as CSV text (mirroring the reference's
    /// <c>HIST.exportCSV</c> column set). The actual file-save dialog is a thin
    /// platform call in the View/command, not this service.
    /// </summary>
    Task<string> ExportCsvAsync(HistoryFilter filter);
}
