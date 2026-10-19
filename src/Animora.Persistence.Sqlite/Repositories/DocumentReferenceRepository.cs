using Animora.Application.Ports;
using Animora.Domain.Patients;
using Animora.Persistence.Sqlite.Context;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.Sqlite.Repositories;

public sealed class DocumentReferenceRepository(AnimoraLocalDbContext db) : IDocumentReferenceRepository
{
    public void Add(DocumentReference entity) => db.Set<DocumentReference>().Add(entity);
}
