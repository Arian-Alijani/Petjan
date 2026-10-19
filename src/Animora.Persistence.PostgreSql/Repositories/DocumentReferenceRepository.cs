using Animora.Application.Ports;
using Animora.Domain.Patients;
using Animora.Persistence.PostgreSql.Context;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.PostgreSql.Repositories;

public sealed class DocumentReferenceRepository(AnimoraServerDbContext db)
    : TenantRepository<DocumentReference>(db), IDocumentReferenceRepository
{
    public new void Add(DocumentReference entity) => Context.Set<DocumentReference>().Add(entity);
}
