using Animora.Domain.Scheduling;

namespace Animora.Application.Ports;

public interface IReceptionQueueRepository
{
    Task<ReceptionQueueEntry?> FindAsync(string id, CancellationToken ct = default);
    Task<ReceptionQueueEntry> RequireAsync(string id, CancellationToken ct = default);
    void Add(ReceptionQueueEntry entity);
    void AddRange(IEnumerable<ReceptionQueueEntry> entities);
}
