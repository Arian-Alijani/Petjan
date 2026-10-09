using Animora.Application.Ports;
using Animora.Domain.Scheduling;
using Animora.Persistence.PostgreSql.Context;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.PostgreSql.Repositories;

public sealed class AppointmentRepository(AnimoraServerDbContext db) : TenantRepository<Appointment>(db), IAppointmentRepository
{
    public Task<Appointment?> FindAsync(string id, CancellationToken ct = default) =>
        db.Set<Appointment>().AsTracking().FirstOrDefaultAsync(e => e.Id == id, ct);

    public async Task<Appointment> RequireAsync(string id, CancellationToken ct = default) =>
        await FindAsync(id, ct).ConfigureAwait(false) ?? throw new InvalidOperationException($"Appointment {id} not found.");

    public new void Add(Appointment entity) => db.Set<Appointment>().Add(entity);
    public void AddRange(IEnumerable<Appointment> entities) => db.Set<Appointment>().AddRange(entities);
}
