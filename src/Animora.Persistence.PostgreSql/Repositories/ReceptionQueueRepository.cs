using Animora.Application.Ports;
using Animora.Domain.Scheduling;
using Animora.Persistence.PostgreSql.Context;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.PostgreSql.Repositories;

public sealed class ReceptionQueueRepository(AnimoraServerDbContext db) : TenantRepository<ReceptionQueueEntry>(db), IReceptionQueueRepository
{
    public Task<ReceptionQueueEntry?> FindAsync(string id, CancellationToken ct = default) =>
        Context.Set<ReceptionQueueEntry>().AsTracking().FirstOrDefaultAsync(e => e.Id == id, ct);

    public async Task<ReceptionQueueEntry> RequireAsync(string id, CancellationToken ct = default) =>
        await FindAsync(id, ct).ConfigureAwait(false) ?? throw new InvalidOperationException($"ReceptionQueueEntry {id} not found.");

    public Task<ReceptionQueueEntry?> FindByAppointmentAsync(string appointmentId, CancellationToken ct = default) =>
        Context.Set<ReceptionQueueEntry>().AsTracking()
            .FirstOrDefaultAsync(e => e.AppointmentId == appointmentId, ct);

    public Task<ReceptionQueueEntry?> FindByVisitAsync(string visitId, CancellationToken ct = default) =>
        Context.Set<ReceptionQueueEntry>().AsTracking()
            .FirstOrDefaultAsync(e => e.VisitId == visitId, ct);

    public Task<int> CountForDayAsync(DateTimeOffset startUtc, DateTimeOffset endUtc, CancellationToken ct = default) =>
        Context.Set<ReceptionQueueEntry>()
            .CountAsync(e => e.EnqueuedUtc >= startUtc && e.EnqueuedUtc < endUtc, ct);

    public new void Add(ReceptionQueueEntry entity) => Context.Set<ReceptionQueueEntry>().Add(entity);
    public void AddRange(IEnumerable<ReceptionQueueEntry> entities) => Context.Set<ReceptionQueueEntry>().AddRange(entities);
}
