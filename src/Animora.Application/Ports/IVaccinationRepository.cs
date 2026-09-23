using Animora.Domain.Clinical;

namespace Animora.Application.Ports;

public interface IVaccinationRepository
{
    Task<VaccinationRecord?> FindAsync(string id, CancellationToken ct = default);
    Task<VaccinationRecord> RequireAsync(string id, CancellationToken ct = default);
    void Add(VaccinationRecord entity);
    void AddRange(IEnumerable<VaccinationRecord> entities);
}
