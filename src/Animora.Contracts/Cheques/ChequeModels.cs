namespace Animora.Contracts.Cheques;

/// <summary>Which way a cheque moves (mirrors the domain enum so Contracts has no Domain dependency).</summary>
public enum ChequeDirectionDto
{
    /// <summary>دریافتی — received from an owner.</summary>
    Inbound = 0,

    /// <summary>پرداختی — written to a supplier.</summary>
    Outbound = 1,
}

/// <summary>Lifecycle state (mirrors the domain state machine).</summary>
public enum ChequeStateDto
{
    Registered = 0,
    InPortfolio = 1,
    Deposited = 2,
    Cleared = 3,
    Bounced = 4,
    Returned = 5,
    Cancelled = 6,
    Settled = 7,
}

/// <summary>The lifecycle slice a list shows.</summary>
public enum ChequeScopeDto
{
    /// <summary>Every cheque.</summary>
    All = 0,

    /// <summary>Still moving: registered, in portfolio or deposited.</summary>
    Open = 1,

    /// <summary>Bounced — needs follow-up.</summary>
    Attention = 2,

    /// <summary>Finished: cleared, returned, cancelled or settled.</summary>
    Closed = 3,
}

/// <summary>A lifecycle action the user can request on a cheque.</summary>
public enum ChequeActionDto
{
    MoveToPortfolio = 0,
    Deposit = 1,
    MarkCleared = 2,
    MarkBounced = 3,
    Return = 4,
    Cancel = 5,
    SettleInCash = 6,
}

/// <summary>
/// One cheque row. Dates are ASCII Jalali <c>yyyy/MM/dd</c> strings in the clinic zone; <c>AmountMinor</c> is IRT (تومان).
/// <b>Actions</b> are exactly the transitions the domain state machine allows from <b>State</b> (never guessed by the UI);
/// <b>CanEdit</b>/<b>CanDelete</b> are true only while the cheque is still <c>Registered</c>.
/// </summary>
public sealed record ChequeItem(
    string Id,
    ChequeDirectionDto Direction,
    string Number,
    string? SayadId,
    string? Bank,
    string? Branch,
    string? Account,
    string Party,
    long AmountMinor,
    string IssueDate,
    string DueDate,
    int DaysUntilDue,
    ChequeStateDto State,
    string? Note,
    IReadOnlyList<ChequeActionDto> Actions,
    bool CanEdit);

/// <summary>Server-side filter of the cheque list.</summary>
public sealed record ChequeListFilter(
    ChequeDirectionDto? Direction = null,
    ChequeScopeDto Scope = ChequeScopeDto.All,
    string? Search = null,
    int Page = 1,
    int PageSize = 50);

public sealed record ChequePage(IReadOnlyList<ChequeItem> Items, int TotalCount);

/// <summary>Open-portfolio totals shown above the list (open = registered, in portfolio or deposited).</summary>
public sealed record ChequeSummary(
    long InboundOpenMinor,
    int InboundOpenCount,
    long OutboundOpenMinor,
    int OutboundOpenCount,
    long BouncedMinor,
    int BouncedCount);

/// <summary>One append-only lifecycle entry: who moved the cheque from/to which state, when (Jalali) and why.</summary>
public sealed record ChequeHistoryEntry(
    ChequeStateDto From,
    ChequeStateDto To,
    string Date,
    string Time,
    string Actor,
    string? Reason);

public sealed record ChequeDetail(ChequeItem Cheque, IReadOnlyList<ChequeHistoryEntry> History);

/// <summary>An owner (inbound) or supplier (outbound) pickable as the cheque's counterparty.</summary>
public sealed record ChequeParty(string Id, string Name, string? Phone);

/// <summary>
/// «ثبت چک جدید» request. <b>PartyId</b> is the owner id when inbound and the supplier id when outbound
/// (mandatory either way). Dates are Jalali <c>yyyy/MM/dd</c>; <b>AmountMinor</b> is IRT (تومان).
/// </summary>
public sealed record NewChequeRequest(
    ChequeDirectionDto Direction,
    string Number,
    long AmountMinor,
    string IssueDate,
    string DueDate,
    string PartyId,
    string? Bank = null,
    string? Branch = null,
    string? Account = null,
    string? SayadId = null,
    string? Note = null);

/// <summary>Edit of a still-<c>Registered</c> cheque's descriptive fields.</summary>
public sealed record UpdateChequeRequest(
    string Number,
    string DueDate,
    string? Bank = null,
    string? Branch = null,
    string? Account = null,
    string? SayadId = null,
    string? Note = null);
