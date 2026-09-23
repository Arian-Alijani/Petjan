using Animora.Domain.Patients;

namespace Animora.Application.Ports;

public interface IPetRepository
{
    Task<Pet?> FindAsync(string id, CancellationToken ct = default);
    Task<Pet> RequireAsync(string id, CancellationToken ct = default);
    void Add(Pet entity);
    void AddRange(IEnumerable<Pet> entities);
}
