using Animora.Application.Ports;
using Animora.Domain.Clinical;
using Animora.Persistence.PostgreSql.Context;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.PostgreSql.Repositories;

public sealed class VisitRepository(AnimoraServerDbContext db) : TenantRepository<Visit>(db), IVisitRepository
{
    public Task<Visit?> FindAsync(string id, CancellationToken ct = default) =>
        db.Set<Visit>().AsTracking().FirstOrDefaultAsync(e => e.Id == id, ct);

    public async Task<Visit> RequireAsync(string id, CancellationToken ct = default) =>
        await FindAsync(id, ct).ConfigureAwait(false) ?? throw new InvalidOperationException($"Visit {id} not found.");

    public new void Add(Visit entity) => db.Set<Visit>().Add(entity);
    public void AddRange(IEnumerable<Visit> entities) => db.Set<Visit>().AddRange(entities);
}
