using Animora.Domain.Scheduling;

namespace Animora.Application.Ports;

public interface IAppointmentRepository
{
    Task<Appointment?> FindAsync(string id, CancellationToken ct = default);
    Task<Appointment> RequireAsync(string id, CancellationToken ct = default);
    void Add(Appointment entity);
    void AddRange(IEnumerable<Appointment> entities);
}
