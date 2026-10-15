using Animora.Application.Ports;
using Animora.Persistence.PostgreSql.Context;
using Animora.Persistence.PostgreSql.Boarding;
namespace Animora.Persistence.PostgreSql.Repositories;

public sealed class BoardingBillingHandoffWriter(AnimoraServerDbContext db) : IBoardingBillingHandoffWriter
{
    public void Add(string id, string tenantId, string stayId, string ownerId, string petId,
        DateTimeOffset completedUtc, string linesJson, BoardingHandoffKind kind = BoardingHandoffKind.Final)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(linesJson);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(linesJson.Length, 65536);
        db.Set<BoardingBillingHandoff>().Add(new BoardingBillingHandoff
        {
            Id = id, TenantId = tenantId, StayId = stayId,
            OwnerId = ownerId, PetId = petId, CompletedUtc = completedUtc, LinesJson = linesJson, Kind = kind
        });
    }
}
