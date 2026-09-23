using Animora.Application.Ports;
using Animora.Domain.Scheduling;
using Animora.Persistence.Sqlite.Context;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.Sqlite.Repositories;

public sealed class ReceptionQueueRepository(AnimoraLocalDbContext db) : IReceptionQueueRepository
{
    public Task<ReceptionQueueEntry?> FindAsync(string id, CancellationToken ct = default) =>
        db.Set<ReceptionQueueEntry>().AsTracking().FirstOrDefaultAsync(e => e.Id == id, ct);

    public async Task<ReceptionQueueEntry> RequireAsync(string id, CancellationToken ct = default) =>
        await FindAsync(id, ct) ?? throw new InvalidOperationException($"ReceptionQueueEntry {id} not found.");

    public void Add(ReceptionQueueEntry entity) => db.Set<ReceptionQueueEntry>().Add(entity);
    public void AddRange(IEnumerable<ReceptionQueueEntry> entities) => db.Set<ReceptionQueueEntry>().AddRange(entities);
}
