using Animora.Domain.Finance;

namespace Animora.Application.Ports;

public interface IPeriodRepository
{
    Task<AccountingPeriod?> FindAsync(string id, CancellationToken ct = default);
    Task<AccountingPeriod> RequireAsync(string id, CancellationToken ct = default);
    Task<AccountingPeriod?> FindByCodeAsync(string periodCode, CancellationToken ct = default);
    Task<AccountingPeriod?> FindContainingAsync(DateTimeOffset occurredUtc, CancellationToken ct = default);
    Task<AccountingPeriod?> FindNextOpenAsync(DateTimeOffset afterUtc, CancellationToken ct = default);
    void Add(AccountingPeriod entity);
    void AddRange(IEnumerable<AccountingPeriod> entities);
}
