using Animora.Application.Ports;
using Animora.Domain.Clinical;
using Animora.Persistence.PostgreSql.Context;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.PostgreSql.Repositories;

public sealed class VaccinationRepository(AnimoraServerDbContext db) : TenantRepository<VaccinationRecord>(db), IVaccinationRepository
{
    public Task<VaccinationRecord?> FindAsync(string id, CancellationToken ct = default) =>
        Context.Set<VaccinationRecord>().AsTracking().FirstOrDefaultAsync(e => e.Id == id, ct);

    public async Task<VaccinationRecord> RequireAsync(string id, CancellationToken ct = default) =>
        await FindAsync(id, ct).ConfigureAwait(false) ?? throw new InvalidOperationException($"VaccinationRecord {id} not found.");

    public new void Add(VaccinationRecord entity) => Context.Set<VaccinationRecord>().Add(entity);
    public void AddRange(IEnumerable<VaccinationRecord> entities) => Context.Set<VaccinationRecord>().AddRange(entities);
}
