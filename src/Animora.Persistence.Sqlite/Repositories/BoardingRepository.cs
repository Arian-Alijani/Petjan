using Animora.Application.Ports;
using Animora.Domain.Boarding;
using Animora.Persistence.Sqlite.Context;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.Sqlite.Repositories;

public sealed class BoardingRepository(AnimoraLocalDbContext db) : IBoardingRepository
{
    public Task<BoardingStay?> FindAsync(string id, CancellationToken ct = default) =>
        db.Set<BoardingStay>().AsTracking().FirstOrDefaultAsync(e => e.Id == id, ct);

    public async Task<BoardingStay> RequireAsync(string id, CancellationToken ct = default) =>
        await FindAsync(id, ct) ?? throw new InvalidOperationException($"BoardingStay {id} not found.");

    public void Add(BoardingStay entity) => db.Set<BoardingStay>().Add(entity);
    public void AddRange(IEnumerable<BoardingStay> entities) => db.Set<BoardingStay>().AddRange(entities);
}
