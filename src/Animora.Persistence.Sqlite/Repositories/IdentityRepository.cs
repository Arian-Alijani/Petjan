using Animora.Application.Ports;
using Animora.Domain.Identity;
using Animora.Persistence.Sqlite.Context;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.Sqlite.Repositories;

public sealed class IdentityRepository(AnimoraLocalDbContext db) : IIdentityRepository
{
    public Task<User?> FindAsync(string id, CancellationToken ct = default) =>
        db.Set<User>().AsTracking().FirstOrDefaultAsync(e => e.Id == id, ct);

    public async Task<User> RequireAsync(string id, CancellationToken ct = default) =>
        await FindAsync(id, ct) ?? throw new InvalidOperationException($"User {id} not found.");

    public void Add(User entity) => db.Set<User>().Add(entity);
    public void AddRange(IEnumerable<User> entities) => db.Set<User>().AddRange(entities);
}
