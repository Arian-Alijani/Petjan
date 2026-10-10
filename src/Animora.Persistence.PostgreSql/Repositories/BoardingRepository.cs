using Animora.Application.Ports;
using Animora.Domain.Boarding;
using Animora.Persistence.PostgreSql.Context;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.PostgreSql.Repositories;

public sealed class BoardingRepository(AnimoraServerDbContext db) : TenantRepository<BoardingStay>(db), IBoardingRepository
{
    public Task<BoardingStay?> FindAsync(string id, CancellationToken ct = default) =>
        Context.Set<BoardingStay>().AsTracking().FirstOrDefaultAsync(e => e.Id == id, ct);

    public async Task<BoardingStay> RequireAsync(string id, CancellationToken ct = default) =>
        await FindAsync(id, ct).ConfigureAwait(false) ?? throw new InvalidOperationException($"BoardingStay {id} not found.");

    public new void Add(BoardingStay entity) => Context.Set<BoardingStay>().Add(entity);
    public void AddRange(IEnumerable<BoardingStay> entities) => Context.Set<BoardingStay>().AddRange(entities);
}
