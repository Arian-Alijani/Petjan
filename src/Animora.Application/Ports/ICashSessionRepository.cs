using Animora.Domain.Cashier;

namespace Animora.Application.Ports;

public interface ICashSessionRepository
{
    Task<CashSession?> FindAsync(string id, CancellationToken ct = default);
    Task<CashSession> RequireAsync(string id, CancellationToken ct = default);
    void Add(CashSession entity);
    void AddRange(IEnumerable<CashSession> entities);
}
