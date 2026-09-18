namespace Animora.Contracts.Cheques;

/// <summary>
/// A cheque's cash direction, mirroring the reference's <c>CH_DIR</c> vocabulary
/// in <c>Animora-UI/assets/cheques-data.js</c> (in = a cheque we received,
/// out = a cheque we issued).
/// </summary>
public enum ChequeDirection
{
    /// <summary>«دریافتی» — a cheque received from a party (ok tone).</summary>
    In,

    /// <summary>«پرداختی» — a cheque issued by us (peach tone).</summary>
    Out,
}

/// <summary>
/// The cheque lifecycle status as the screen sees it, mirroring the reference's
/// <c>CH_STATUS</c> keys (inhand / atbank / cleared / bounced / spent / void).
/// The View maps each value 1:1 to a Phase 02 semantic tone — this contract
/// stays free of UI resources.
/// </summary>
public enum ChequeStatus
{
    /// <summary>«در جریان» — in hand / issued (info tone).</summary>
    InHand,

    /// <summary>«واگذار به بانک» — handed to the bank for collection (warn tone).</summary>
    AtBank,

    /// <summary>«وصول شده» — cleared / passed (ok tone).</summary>
    Cleared,

    /// <summary>«برگشتی» — bounced, unpaid (danger tone).</summary>
    Bounced,

    /// <summary>«خرج شده» — endorsed to a third party (lilac tone).</summary>
    Spent,

    /// <summary>«باطل شده» — voided (mute tone).</summary>
    Void,
}

/// <summary>
/// The kind of a side-column alert, mirroring the items <c>cardAlerts</c>
/// (Animora-UI/assets/cheques.js) builds: today / overdue / soon-out /
/// bounced / sms. Drives the tile tone + icon and the click-through filter.
/// </summary>
public enum ChequeAlertKind
{
    /// <summary>A cheque is due today.</summary>
    Today,

    /// <summary>One or more open cheques are past due.</summary>
    Overdue,

    /// <summary>Outgoing cheques come due within five days (funding).</summary>
    SoonOut,

    /// <summary>Bounced cheques need follow-up.</summary>
    Bounced,

    /// <summary>Send an SMS reminder to next week's parties.</summary>
    Sms,
}

/// <summary>
/// A bank in the reference's <c>CH_BANKS</c> catalog: its display name/short
/// label and the two gradient stops used for the bank mark. Kept in the
/// contract so the View never hardcodes bank colours.
/// </summary>
public sealed record ChequeBank(
    string Key,
    string Name,
    string Short,
    string ColorFrom,
    string ColorTo);

/// <summary>
/// One cheque, mirroring the shape the reference's <c>CHEQUES</c> entries expose
/// (Animora-UI/assets/cheques-data.js). Dates are Jalali <c>yyyy/MM/dd</c>
/// strings (the reference's own representation); the desktop derives day
/// differences / calendar placement through the Phase 04 Jalali helpers rather
/// than storing them twice.
/// </summary>
public sealed record Cheque(
    string Id,
    ChequeDirection Direction,
    string Number,
    string Sayad,
    string BankKey,
    string Branch,
    string Party,
    string Role,
    string Phone,
    long Amount,
    string IssueDate,
    string DueDate,
    ChequeStatus Status,
    string Subject,
    string Reference,
    string Account,
    string Note);

/// <summary>
/// One week bucket of the side column's cash-flow card (mirrors the reference's
/// <c>cardCashflow</c> eight-week series), carrying the medium Jalali label of
/// its first day plus the open in/out totals falling inside the week.
/// </summary>
public sealed record CashflowWeek(string Label, long In, long Out);

/// <summary>
/// The eight-week cheque cash-flow summary for the side column's
/// «جریان نقدی چک‌ها» card (<c>cardCashflow</c>).
/// </summary>
public sealed record CashflowSummary(IReadOnlyList<CashflowWeek> Weeks);

/// <summary>
/// One bank row of the side column's «توزیع بانکی» card (<c>cardBanks</c>):
/// the bank's metadata plus its aggregated amount and cheque count.
/// </summary>
public sealed record BankSummary(
    string BankKey,
    string Name,
    string Short,
    string ColorFrom,
    string ColorTo,
    long Amount,
    int Count);

/// <summary>
/// One side-column alert / action item (<c>cardAlerts</c>) — its kind, headline
/// and detail line. The View maps the kind to a tone + icon and the click-through
/// filter.
/// </summary>
public sealed record ChequeAlert(ChequeAlertKind Kind, string Title, string Detail);
