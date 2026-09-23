using Animora.Domain.Finance;

namespace Animora.Application.Ports;

public interface IPeriodRepository
{
    Task<AccountingPeriod?> FindAsync(string id, CancellationToken ct = default);
    Task<AccountingPeriod> RequireAsync(string id, CancellationToken ct = default);
    void Add(AccountingPeriod entity);
    void AddRange(IEnumerable<AccountingPeriod> entities);
}
