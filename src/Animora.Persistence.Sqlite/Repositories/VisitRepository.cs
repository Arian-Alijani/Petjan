using Animora.Application.Ports;
using Animora.Domain.Clinical;
using Animora.Persistence.Sqlite.Context;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.Sqlite.Repositories;

public sealed class VisitRepository(AnimoraLocalDbContext db) : IVisitRepository
{
    public Task<Visit?> FindAsync(string id, CancellationToken ct = default) =>
        db.Set<Visit>().AsTracking().FirstOrDefaultAsync(e => e.Id == id, ct);

    public async Task<Visit> RequireAsync(string id, CancellationToken ct = default) =>
        await FindAsync(id, ct) ?? throw new InvalidOperationException($"Visit {id} not found.");

    public void Add(Visit entity) => db.Set<Visit>().Add(entity);
    public void AddRange(IEnumerable<Visit> entities) => db.Set<Visit>().AddRange(entities);
}
