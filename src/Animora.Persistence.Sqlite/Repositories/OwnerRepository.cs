using Animora.Application.Ports;
using Animora.Domain.Patients;
using Animora.Persistence.Sqlite.Context;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.Sqlite.Repositories;

public sealed class OwnerRepository(AnimoraLocalDbContext db) : IOwnerRepository
{
    public Task<Owner?> FindAsync(string id, CancellationToken ct = default) =>
        db.Set<Owner>().AsTracking().FirstOrDefaultAsync(e => e.Id == id, ct);

    public async Task<Owner> RequireAsync(string id, CancellationToken ct = default) =>
        await FindAsync(id, ct) ?? throw new InvalidOperationException($"Owner {id} not found.");

    public void Add(Owner entity) => db.Set<Owner>().Add(entity);
    public void AddRange(IEnumerable<Owner> entities) => db.Set<Owner>().AddRange(entities);
}
