using Animora.Application.Ports;
using Animora.Domain.Patients;
using Animora.Persistence.PostgreSql.Context;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.PostgreSql.Repositories;

public sealed class OwnerRepository(AnimoraServerDbContext db) : TenantRepository<Owner>(db), IOwnerRepository
{
    public Task<Owner?> FindAsync(string id, CancellationToken ct = default) =>
        Context.Set<Owner>().AsTracking().FirstOrDefaultAsync(e => e.Id == id, ct);

    public async Task<Owner> RequireAsync(string id, CancellationToken ct = default) =>
        await FindAsync(id, ct).ConfigureAwait(false) ?? throw new InvalidOperationException($"Owner {id} not found.");

    public Task<Owner?> FindByPhoneAsync(string normalizedPhone, CancellationToken ct = default) =>
        Context.Set<Owner>().AsTracking().FirstOrDefaultAsync(e => e.PhonePrimary == normalizedPhone, ct);

    public new void Add(Owner entity) => Context.Set<Owner>().Add(entity);
    public void AddRange(IEnumerable<Owner> entities) => Context.Set<Owner>().AddRange(entities);
}
