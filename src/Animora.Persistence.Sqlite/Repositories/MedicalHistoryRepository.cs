using Animora.Application.Ports;
using Animora.Domain.Clinical;
using Animora.Persistence.Sqlite.Context;

namespace Animora.Persistence.Sqlite.Repositories;

public sealed class MedicalHistoryRepository(AnimoraLocalDbContext db) : IMedicalHistoryRepository
{
    public void Add(MedicalHistoryEntry entry) => db.Set<MedicalHistoryEntry>().Add(entry);
}
