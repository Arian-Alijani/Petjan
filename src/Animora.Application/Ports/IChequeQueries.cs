using Animora.Application.Abstractions;
using Animora.Domain.Finance;

namespace Animora.Application.Ports;

public sealed record ChequeRow(string Id, ChequeDirection Direction, string Number, string? Sayad,
    string? BankKey, string? Branch, string Party, long AmountMinor, DateTimeOffset IssuedUtc,
    DateTimeOffset DueUtc, ChequeState State, string? InvoiceId, string? Account, string? Note);
public sealed record ChequeCashflowWeek(DateOnly WeekStart, long IncomingMinor, long OutgoingMinor);
public sealed record ChequeBankRow(string BankKey, long AmountMinor, int Count);
public sealed record ChequeAlertRow(string Id, string Kind, DateOnly DueDay, long AmountMinor);

public interface IChequeQueries
{
    Task<PagedResult<ChequeRow>> GetListAsync(int page, int size, CancellationToken ct);
    Task<IReadOnlyList<ChequeCashflowWeek>> GetCashflowAsync(int weeks, DateOnly today, CancellationToken ct);
    Task<IReadOnlyList<ChequeBankRow>> GetBankBreakdownAsync(CancellationToken ct);
    Task<IReadOnlyList<ChequeAlertRow>> GetAlertsAsync(DateOnly today, CancellationToken ct);
}
