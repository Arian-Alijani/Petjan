using Animora.Domain.Finance;

namespace Animora.Application.Ports;

public interface IChequeRepository
{
    Task<Cheque?> FindAsync(string id, CancellationToken ct = default);
    Task<Cheque> RequireAsync(string id, CancellationToken ct = default);
    void Add(Cheque entity);
    void AddRange(IEnumerable<Cheque> entities);
}
