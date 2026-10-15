using Animora.Application.Ports;
using Animora.Persistence.PostgreSql.Context;
using Animora.Persistence.PostgreSql.Clinical;

namespace Animora.Persistence.PostgreSql.Repositories;

public sealed class VisitBillingHandoffWriter(AnimoraServerDbContext db) : IVisitBillingHandoffWriter
{
    public void Add(string id, string tenantId, string visitId, string ownerId, string petId, string doctorId,
        DateTimeOffset completedUtc, string linesJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(linesJson);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(linesJson.Length, 65536);
        db.Set<VisitBillingHandoff>().Add(new VisitBillingHandoff
        {
            Id = id, TenantId = tenantId, VisitId = visitId,
            OwnerId = ownerId, PetId = petId, DoctorId = doctorId,
            CompletedUtc = completedUtc, LinesJson = linesJson
        });
    }
}
