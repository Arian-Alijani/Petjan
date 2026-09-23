using Animora.Domain.Patients;

namespace Animora.Application.Ports;

public interface IOwnerRepository
{
    Task<Owner?> FindAsync(string id, CancellationToken ct = default);
    Task<Owner> RequireAsync(string id, CancellationToken ct = default);
    void Add(Owner entity);
    void AddRange(IEnumerable<Owner> entities);
}
