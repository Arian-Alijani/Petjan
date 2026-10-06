using Animora.Application.Abstractions;
using Animora.Domain.Finance;

namespace Animora.Application.Ports;

/// <summary>One cheque row; <c>Party</c> is the counterparty's display name (owner when inbound, supplier when outbound).</summary>
public sealed record ChequeRow(string Id, ChequeDirection Direction, string Number, string? Sayad,
    string? BankKey, string? Branch, string Party, long AmountMinor, DateTimeOffset IssuedUtc,
    DateTimeOffset DueUtc, ChequeState State, string? InvoiceId, string? Account, string? Note);
public sealed record ChequeCashflowWeek(DateOnly WeekStart, long IncomingMinor, long OutgoingMinor);
public sealed record ChequeBankRow(string BankKey, long AmountMinor, int Count);
public sealed record ChequeAlertRow(string Id, string Kind, DateOnly DueDay, long AmountMinor);

/// <summary>Which slice of the lifecycle a list shows: <c>Open</c> = still moving (registered/portfolio/deposited), <c>Attention</c> = bounced, <c>Closed</c> = terminal.</summary>
public enum ChequeListScope { All = 0, Open = 1, Attention = 2, Closed = 3 }

/// <summary>Server-side list filter: direction, lifecycle scope and a free-text search over number, Sayad id and counterparty name.</summary>
public sealed record ChequeListCriteria(ChequeDirection? Direction = null, ChequeListScope Scope = ChequeListScope.All,
    string? Search = null, int Page = 1, int PageSize = 50);

/// <summary>Open-portfolio totals for the screen header; <c>Open</c> = registered, in portfolio or deposited.</summary>
public sealed record ChequeSummaryRow(long InboundOpenMinor, int InboundOpenCount, long OutboundOpenMinor,
    int OutboundOpenCount, long BouncedMinor, int BouncedCount);

/// <summary>One append-only lifecycle entry with the acting user's display name.</summary>
public sealed record ChequeHistoryRow(string EventId, ChequeState From, ChequeState To, DateTimeOffset OccurredUtc,
    string ActorName, string? Reason);

public sealed record ChequeDetailRow(ChequeRow Row, IReadOnlyList<ChequeHistoryRow> History);

/// <summary>An owner / supplier the register form can pick as the cheque's counterparty.</summary>
public sealed record ChequePartyRow(string Id, string Name, string? Phone);

public interface IChequeQueries
{
    Task<PagedResult<ChequeRow>> GetListAsync(ChequeListCriteria criteria, CancellationToken ct);
    Task<ChequeDetailRow?> GetDetailAsync(string id, CancellationToken ct);
    Task<ChequeSummaryRow> GetSummaryAsync(CancellationToken ct);
    Task<IReadOnlyList<ChequePartyRow>> FindOwnersAsync(string? search, CancellationToken ct);
    Task<IReadOnlyList<ChequePartyRow>> FindSuppliersAsync(string? search, CancellationToken ct);
    Task<IReadOnlyList<ChequeCashflowWeek>> GetCashflowAsync(int weeks, DateOnly today, CancellationToken ct);
    Task<IReadOnlyList<ChequeBankRow>> GetBankBreakdownAsync(CancellationToken ct);
    Task<IReadOnlyList<ChequeAlertRow>> GetAlertsAsync(DateOnly today, CancellationToken ct);
}
