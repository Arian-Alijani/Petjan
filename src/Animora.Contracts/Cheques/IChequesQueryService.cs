namespace Animora.Contracts.Cheques;

/// <summary>
/// Data seam for the Cheques (treasury) screen. The desktop ViewModel depends
/// only on this interface; the in-memory <c>MockChequesQueryService</c> is the
/// single swappable implementation a future backend replaces (R-18). The shape
/// mirrors what <c>Animora-UI/assets/cheques.js</c> renders: the KPI strip, the
/// four render modes' data, the side cards (cash-flow / bank distribution /
/// alerts) and the bank filter catalog.
/// </summary>
public interface IChequesQueryService
{
    /// <summary>
    /// Every cheque, mirroring the reference's <c>window.CHEQUES</c> list. The
    /// ViewModel applies the tab/status/bank/range/search filters and the sort.
    /// </summary>
    Task<IReadOnlyList<Cheque>> GetAllAsync();

    /// <summary>
    /// The bank catalog backing the toolbar's bank filter and the bank marks
    /// (mirrors <c>window.CH_BANKS</c>).
    /// </summary>
    Task<IReadOnlyList<ChequeBank>> GetBanksAsync();

    /// <summary>
    /// The eight-week open-cheque cash-flow series for the side card (mirrors
    /// <c>cardCashflow</c>; computed over the whole set, not the filtered view).
    /// </summary>
    Task<CashflowSummary> GetCashflowAsync();

    /// <summary>
    /// Bank distribution rows for the side card (mirrors <c>cardBanks</c>). The
    /// ViewModel recomputes the amounts/counts against the filtered set using the
    /// bank metadata carried here.
    /// </summary>
    Task<IReadOnlyList<BankSummary>> GetBankBreakdownAsync();

    /// <summary>
    /// The side column's alerts/actions (mirrors <c>cardAlerts</c>), computed
    /// over the whole set.
    /// </summary>
    Task<IReadOnlyList<ChequeAlert>> GetAlertsAsync();

    /// <summary>
    /// Registers a new cheque (mirrors <c>saveCheque</c>'s insert path) and
    /// returns it. New cheques always start in the «در جریان» state.
    /// </summary>
    Task<Cheque> CreateAsync(NewChequeRequest request);

    /// <summary>
    /// Overwrites an existing cheque's editable fields (mirrors <c>saveCheque</c>'s
    /// edit path). The lifecycle status is preserved.
    /// </summary>
    Task UpdateAsync(string chequeId, UpdateChequeRequest request);

    /// <summary>Deletes a cheque (mirrors the row menu's «حذف چک»).</summary>
    Task DeleteAsync(string chequeId);

    /// <summary>
    /// Sets the lifecycle status of every cheque in <paramref name="ids"/>
    /// (mirrors the reference's bulk «واگذاری به بانک» / «ثبت وصول» actions).
    /// </summary>
    Task BulkUpdateStatusAsync(IReadOnlyList<string> ids, ChequeStatus newStatus);
}
