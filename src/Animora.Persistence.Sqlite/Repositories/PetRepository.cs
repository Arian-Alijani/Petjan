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

    public Task<Pet?> FindByOwnerAndNameAsync(string ownerId, string name, CancellationToken ct = default)
    {
        var trimmed = name.Trim();
        return db.Set<Pet>().AsTracking()
            .FirstOrDefaultAsync(e => e.OwnerId == ownerId && e.Name == trimmed, ct);
    }

    public async Task<IReadOnlyList<Pet>> ListByOwnerAndNameAsync(string ownerId, string name, CancellationToken ct = default)
    {
        var trimmed = name.Trim();
        return await db.Set<Pet>().AsTracking()
            .Where(e => e.OwnerId == ownerId && e.Name == trimmed)
            .OrderBy(e => e.Id).ToListAsync(ct);
    }

    // Tracked so the cascade soft-delete flips persist; the global SoftDelete filter already
    // excludes tombstoned rows.
    public async Task<IReadOnlyList<Pet>> ListByOwnerAsync(string ownerId, CancellationToken ct = default) =>
        await db.Set<Pet>().AsTracking()
            .Where(e => e.OwnerId == ownerId)
            .OrderBy(e => e.Id).ToListAsync(ct);

    public void Add(Pet entity) => db.Set<Pet>().Add(entity);
    public void AddRange(IEnumerable<Pet> entities) => db.Set<Pet>().AddRange(entities);
}
