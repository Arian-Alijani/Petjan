using Animora.Application.Ports;
using Animora.Domain.Boarding;
using Animora.Persistence.Sqlite.Context;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.Sqlite.Repositories;

public sealed class BoardingServiceRepository(AnimoraLocalDbContext db) : IBoardingServiceRepository
{
    public Task<BoardingServiceDef?> FindAsync(string id, CancellationToken ct = default) =>
        db.Set<BoardingServiceDef>().AsTracking().FirstOrDefaultAsync(e => e.Id == id, ct);

    public void Add(BoardingServiceDef entity) => db.Set<BoardingServiceDef>().Add(entity);
}
