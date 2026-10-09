using Animora.Application.UseCases.Visits.ReadModels;
using Animora.Contracts.Wire.Commands;
using Animora.Domain.Primitives;
using Animora.Application.UseCases.Reception;
using Animora.Persistence.PostgreSql.Commands;
using Animora.Persistence.PostgreSql.Context;
using Microsoft.EntityFrameworkCore;
namespace Animora.Server.Commands;

internal sealed record CapturedCommandEffectIds(string? PrimaryEntityId, IReadOnlyList<CommandIdMapItem> CreatedIds);
internal interface ICommandEffectIdCollector
{
    CapturedCommandEffectIds Capture(CommandEnvelope envelope, object? value);
}

/// <summary>Snapshots every generated syncable id before SaveChanges accepts tracker state.</summary>
internal sealed class ServerCommandEffectIdCollector(AnimoraServerDbContext db) : ICommandEffectIdCollector
{
    public CapturedCommandEffectIds Capture(CommandEnvelope envelope, object? value)
    {
        string? primary = CommandEffectIdentity.Primary(envelope, value);
        var rows = db.ChangeTracker.Entries<SyncableEntity>().Where(e => e.State == EntityState.Added)
            .Select(e => new { Name = e.Metadata.ClrType.Name, e.Entity.Id })
            .OrderBy(e => e.Name, StringComparer.Ordinal).ThenBy(e => e.Id, StringComparer.Ordinal).ToArray();
        var ids = new List<CommandIdMapItem>();
        if (envelope.AggregateId is null && primary is not null)
            ids.Add(new CommandIdMapItem(0, envelope.AggregateType, primary));
        short slot = 1;
        foreach (var row in rows)
        {
            if (row.Name == envelope.AggregateType && row.Id == primary) continue;
            ids.Add(new CommandIdMapItem(slot, row.Name, row.Id)); slot = checked((short)(slot + 1));
        }
        return new CapturedCommandEffectIds(primary, ids);
    }
}
internal static class CommandEffectIdentity
{
    public static string? Primary(CommandEnvelope envelope, object? value)
    {
        if (envelope.AggregateId is not null) return envelope.AggregateId;
        if (value is EncounterDraftReadModel visit) return visit.VisitId;
        if (value is not string id) return null;
        // The unchanged walk-in handler returns a UI ticket ref, not a database ULID.
        if (envelope.CommandType == CommandTypes.ReceptionWalkInCreate && TicketRef.TryParse(id, out var ticket)) return ticket.Id;
        return id;
    }
}
