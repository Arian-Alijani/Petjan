using Animora.Application.Ports;
using Animora.Domain.Clinical;
using Animora.Persistence.Sqlite.Context;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.Sqlite.Repositories;

public sealed class VaccinationRepository(AnimoraLocalDbContext db) : IVaccinationRepository
{
    public Task<VaccinationRecord?> FindAsync(string id, CancellationToken ct = default) =>
        db.Set<VaccinationRecord>().AsTracking().FirstOrDefaultAsync(e => e.Id == id, ct);

    public async Task<VaccinationRecord> RequireAsync(string id, CancellationToken ct = default) =>
        await FindAsync(id, ct) ?? throw new InvalidOperationException($"VaccinationRecord {id} not found.");

    public void Add(VaccinationRecord entity) => db.Set<VaccinationRecord>().Add(entity);
    public void AddRange(IEnumerable<VaccinationRecord> entities) => db.Set<VaccinationRecord>().AddRange(entities);
}
