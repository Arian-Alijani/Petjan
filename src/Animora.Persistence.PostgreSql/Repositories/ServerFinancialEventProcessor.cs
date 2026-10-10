using Animora.Application.Abstractions;
using Animora.Application.Correlation;
using Animora.Domain.Events;
using Animora.Domain.Primitives;
using Animora.Domain.Inventory;
using Animora.Domain.Finance;
using Animora.Domain.Results;
using Animora.Persistence.PostgreSql.Context;
using Microsoft.EntityFrameworkCore;
namespace Animora.Persistence.PostgreSql.Repositories;

public interface IServerFinancialEventProcessor
{
    Task<Result> ProcessAsync(CommandContext command, CancellationToken ct);
}

/// <summary>Expands source events into journal inserts before effect-id capture and the single save.</summary>
public sealed class ServerFinancialEventProcessor(AnimoraServerDbContext db,
    IDomainEventDispatcher dispatcher, ICorrelationContext correlation) : IServerFinancialEventProcessor
{
    public async Task<Result> ProcessAsync(CommandContext command, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null)
            return Result.Fail(ErrorCodes.DispatchFailed, "Financial events require the intake transaction.");
        using var scope = correlation.Begin(new AmbientCorrelation(command.CorrelationId, command.TenantId,
            command.UserId, command.DeviceId, command.OperationId));
        var roots = db.ChangeTracker.Entries<AggregateRoot>().Select(e => e.Entity).ToArray();
        var events = roots.SelectMany(r => r.DomainEvents).ToList();
        // Movement entities have no own event buffer; synthesize exactly one event per staged append.
        foreach (var row in db.ChangeTracker.Entries<StockMovement>().Where(e => e.State == EntityState.Added).ToArray())
        {
            var m = row.Entity;
            events.Add(new StockMovementRecordedEvent(IdGenerator.NewId(command.CreatedAtUtc), m.TenantId,
                m.OccurredUtc, m.Id, m.ProductId, m.VariantId, m.Kind, m.Quantity.Value));
        }
        foreach (var domainEvent in events)
        {
            if (domainEvent.TenantId != command.TenantId) return Result.Fail(ErrorCodes.PermissionDenied);
            var result = await dispatcher.DispatchAsync(domainEvent, ct);
            if (result.IsFailure) return result;
        }
        foreach (var entry in db.ChangeTracker.Entries<JournalEntry>().Where(e => e.State == EntityState.Added))
        {
            // JournalEntry.Create is the authoritative validation boundary; verify persisted lines
            // still represent its immutable, balanced result before saving.
            var balance = 0L;
            foreach (var line in entry.Entity.Lines) balance = checked(balance + line.SignedMinor);
            if (balance != 0) return Result.Fail(ErrorCodes.JournalUnbalanced);
        }
        foreach (var root in roots) root.ClearDomainEvents();
        return Result.Ok();
    }
}
