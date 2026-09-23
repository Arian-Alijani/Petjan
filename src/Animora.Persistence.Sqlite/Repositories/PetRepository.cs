using Animora.Application.Ports;
using Animora.Domain.Patients;
using Animora.Persistence.Sqlite.Context;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.Sqlite.Repositories;

public sealed class PetRepository(AnimoraLocalDbContext db) : IPetRepository
{
    public Task<Pet?> FindAsync(string id, CancellationToken ct = default) =>
        db.Set<Pet>().AsTracking().FirstOrDefaultAsync(e => e.Id == id, ct);

    public async Task<Pet> RequireAsync(string id, CancellationToken ct = default) =>
        await FindAsync(id, ct) ?? throw new InvalidOperationException($"Pet {id} not found.");

    public void Add(Pet entity) => db.Set<Pet>().Add(entity);
    public void AddRange(IEnumerable<Pet> entities) => db.Set<Pet>().AddRange(entities);
}
