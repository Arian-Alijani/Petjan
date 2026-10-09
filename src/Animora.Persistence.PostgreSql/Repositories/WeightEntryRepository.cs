using Animora.Application.Ports;
using Animora.Domain.Patients;
using Animora.Persistence.PostgreSql.Context;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.PostgreSql.Repositories;

public sealed class WeightEntryRepository(AnimoraServerDbContext db) : TenantRepository<WeightEntry>(db), IWeightEntryRepository
{
    public new void Add(WeightEntry entry) => db.Set<WeightEntry>().Add(entry);
}
