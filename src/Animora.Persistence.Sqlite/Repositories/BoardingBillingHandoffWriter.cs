using Animora.Application.Ports;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Entities;
using AppHandoffKind = Animora.Application.Ports.BoardingHandoffKind;
using EntityHandoffKind = Animora.Persistence.Sqlite.Entities.BoardingHandoffKind;

namespace Animora.Persistence.Sqlite.Repositories;

public sealed class BoardingBillingHandoffWriter(AnimoraLocalDbContext db) : IBoardingBillingHandoffWriter
{
    public void Add(string tenantId, string stayId, string ownerId, string petId,
        DateTimeOffset completedUtc, string linesJson,
        AppHandoffKind kind = AppHandoffKind.Final) =>
        db.Set<BoardingBillingHandoff>().Add(BoardingBillingHandoff.Create(
            tenantId, stayId, ownerId, petId, completedUtc, linesJson,
            kind == AppHandoffKind.Room ? EntityHandoffKind.Room : EntityHandoffKind.Final));
}
