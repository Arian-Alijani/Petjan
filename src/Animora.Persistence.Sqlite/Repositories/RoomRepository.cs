using Animora.Application.Ports;
using Animora.Domain.Boarding;
using Animora.Persistence.Sqlite.Context;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.Sqlite.Repositories;

public sealed class RoomRepository(AnimoraLocalDbContext db) : IRoomRepository
{
    public Task<Room?> FindAsync(string id, CancellationToken ct = default) =>
        db.Set<Room>().AsTracking().FirstOrDefaultAsync(e => e.Id == id, ct);

    public Task<bool> CodeExistsAsync(string code, string? exceptId, CancellationToken ct = default) =>
        db.Set<Room>().AsNoTracking().AnyAsync(e => e.Code == code && e.Id != exceptId, ct);

    public void Add(Room entity) => db.Set<Room>().Add(entity);
}
