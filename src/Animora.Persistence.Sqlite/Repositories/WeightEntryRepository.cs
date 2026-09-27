using Animora.Application.Ports;
using Animora.Domain.Patients;
using Animora.Persistence.Sqlite.Context;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.Sqlite.Repositories;

public sealed class WeightEntryRepository(AnimoraLocalDbContext db) : IWeightEntryRepository
{
    public void Add(WeightEntry entry) => db.Set<WeightEntry>().Add(entry);
}
