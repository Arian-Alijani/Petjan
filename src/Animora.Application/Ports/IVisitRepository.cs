using Animora.Domain.Clinical;

namespace Animora.Application.Ports;

public interface IVisitRepository
{
    Task<Visit?> FindAsync(string id, CancellationToken ct = default);
    Task<Visit> RequireAsync(string id, CancellationToken ct = default);
    void Add(Visit entity);
    void AddRange(IEnumerable<Visit> entities);
}
