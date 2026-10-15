using Animora.Application.Ports;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Entities;

namespace Animora.Persistence.Sqlite.Repositories;

/// <summary>Shares the command's scoped DbContext and transaction with the visit and history writes.</summary>
public sealed class VisitBillingHandoffWriter(AnimoraLocalDbContext db) : IVisitBillingHandoffWriter
{
    public void Add(string id, string tenantId, string visitId, string ownerId, string petId, string doctorId,
        DateTimeOffset completedUtc, string linesJson) =>
        db.Set<VisitBillingHandoff>().Add(VisitBillingHandoff.Create(id, tenantId, visitId, ownerId,
            petId, doctorId, completedUtc, linesJson));
}
