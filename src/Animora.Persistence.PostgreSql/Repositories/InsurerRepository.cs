using Animora.Application.Ports;
using Animora.Domain.Sales;
using Animora.Persistence.PostgreSql.Context;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.PostgreSql.Repositories;

public sealed class InsurerRepository(AnimoraServerDbContext db) : TenantRepository<Insurer>(db), IInsurerRepository
{
    public Task<Insurer?> FindAsync(string id, CancellationToken ct = default) =>
        db.Set<Insurer>().AsTracking().FirstOrDefaultAsync(e => e.Id == id, ct);

    public new void Add(Insurer entity) => db.Set<Insurer>().Add(entity);

    public async Task<bool> NameExistsAsync(string name, string? excludeId, CancellationToken ct = default)
    {
        var target = name.Trim().ToUpperInvariant();
        return await db.Set<Insurer>().AsNoTracking().AnyAsync(i =>
            (excludeId == null || i.Id != excludeId) && i.Name.Trim().ToUpper() == target, ct);
    }
}
