using Animora.Application.Ports;
using Animora.Domain.Boarding;
using Animora.Persistence.PostgreSql.Context;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.PostgreSql.Repositories;

public sealed class RoomRepository(AnimoraServerDbContext db) : TenantRepository<Room>(db), IRoomRepository
{
    public Task<Room?> FindAsync(string id, CancellationToken ct = default) =>
        db.Set<Room>().AsTracking().FirstOrDefaultAsync(e => e.Id == id, ct);

    public Task<bool> CodeExistsAsync(string code, string? exceptId, CancellationToken ct = default) =>
        db.Set<Room>().AsNoTracking().AnyAsync(e => e.Code == code && e.Id != exceptId, ct);

    public new void Add(Room entity) => db.Set<Room>().Add(entity);
}
