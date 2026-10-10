using Animora.Application.Ports;
using Animora.Domain.Boarding;
using Animora.Persistence.PostgreSql.Context;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.PostgreSql.Repositories;

public sealed class BoardingServiceRepository(AnimoraServerDbContext db) : TenantRepository<BoardingServiceDef>(db), IBoardingServiceRepository
{
    public Task<BoardingServiceDef?> FindAsync(string id, CancellationToken ct = default) =>
        Context.Set<BoardingServiceDef>().AsTracking().FirstOrDefaultAsync(e => e.Id == id, ct);

    public new void Add(BoardingServiceDef entity) => Context.Set<BoardingServiceDef>().Add(entity);
}
