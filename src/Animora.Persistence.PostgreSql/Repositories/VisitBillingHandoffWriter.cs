using Animora.Application.Ports;
using Animora.Domain.Primitives;
using Animora.Persistence.PostgreSql.Context;
using Animora.Persistence.PostgreSql.Clinical;

namespace Animora.Persistence.PostgreSql.Repositories;

public sealed class VisitBillingHandoffWriter(AnimoraServerDbContext db) : IVisitBillingHandoffWriter
{
    public void Add(string tenantId, string visitId, string ownerId, string petId, string doctorId,
        DateTimeOffset completedUtc, string linesJson)
    {
        ArgumentNullException.ThrowIfNull(linesJson);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(linesJson.Length, 65536);
        db.Set<VisitBillingHandoff>().Add(new VisitBillingHandoff
        {
            Id = IdGenerator.NewId(completedUtc), TenantId = tenantId, VisitId = visitId,
            OwnerId = ownerId, PetId = petId, DoctorId = doctorId,
            CompletedUtc = completedUtc, LinesJson = linesJson
        });
    }
}
