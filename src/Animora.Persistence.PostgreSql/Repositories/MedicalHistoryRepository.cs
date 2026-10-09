using Animora.Application.Ports;
using Animora.Domain.Clinical;
using Animora.Persistence.PostgreSql.Context;

namespace Animora.Persistence.PostgreSql.Repositories;

public sealed class MedicalHistoryRepository(AnimoraServerDbContext db) : TenantRepository<MedicalHistoryEntry>(db), IMedicalHistoryRepository
{
    public new void Add(MedicalHistoryEntry entry) => db.Set<MedicalHistoryEntry>().Add(entry);
}
