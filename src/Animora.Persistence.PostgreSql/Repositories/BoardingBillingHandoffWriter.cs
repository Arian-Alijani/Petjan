using Animora.Application.Ports;
using Animora.Domain.Primitives;
using Animora.Persistence.PostgreSql.Context;
using Animora.Persistence.PostgreSql.Boarding;
namespace Animora.Persistence.PostgreSql.Repositories;

public sealed class BoardingBillingHandoffWriter(AnimoraServerDbContext db) : IBoardingBillingHandoffWriter
{
    public void Add(string tenantId, string stayId, string ownerId, string petId,
        DateTimeOffset completedUtc, string linesJson, BoardingHandoffKind kind = BoardingHandoffKind.Final)
    {
        ArgumentNullException.ThrowIfNull(linesJson);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(linesJson.Length, 65536);
        db.Set<BoardingBillingHandoff>().Add(new BoardingBillingHandoff
        {
            Id = IdGenerator.NewId(completedUtc), TenantId = tenantId, StayId = stayId,
            OwnerId = ownerId, PetId = petId, CompletedUtc = completedUtc, LinesJson = linesJson, Kind = kind
        });
    }
}
