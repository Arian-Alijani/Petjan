using System.Text.Json;
using Animora.Contracts.Wire.Commands;
using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Animora.Persistence.PostgreSql.Context;
using Animora.Persistence.PostgreSql.Replication;
using Microsoft.EntityFrameworkCore;
namespace Animora.Persistence.PostgreSql.Outbox;

/// <summary>Persistence-owned seam: the server may depend inward on it, never the reverse.</summary>
public interface IServerOutbox
{
    Task<Result> AppendAsync(CommandEnvelope envelope, SettlementResult settlement, CancellationToken ct = default);
}
public sealed class ServerOutbox(AnimoraServerDbContext context) : IServerOutbox
{
    public async Task<Result> AppendAsync(CommandEnvelope envelope, SettlementResult settlement, CancellationToken ct = default)
    {
        if (settlement.Outcome != SettlementOutcome.Accepted) return Result.Ok();
        if (context.Database.CurrentTransaction is null) throw new InvalidOperationException("Outbox requires the business transaction.");
        // Trigger inserts are not EF-tracked. Read pending rows visible in our own transaction;
        // every other transaction's committed rows already have a sequence.
        if (!await context.Set<TenantJournalRow>().AnyAsync(j => j.ChangeSequence == null, ct).ConfigureAwait(false)) return Result.Ok();
        if (context.ChangeTracker.Entries<OutboxMessage>().Any(e => e.Entity.MessageType == OutboxMessageTypes.FeedAdvanced &&
            e.Entity.TenantId == context.CurrentTenantId)) return Result.Ok();
        var now = DateTimeOffset.UtcNow;
        context.Set<OutboxMessage>().Add(new() { TenantId = context.CurrentTenantId, Id = IdGenerator.NewId(now),
            MessageType = OutboxMessageTypes.FeedAdvanced, PayloadJson = JsonSerializer.Serialize(new { tenantId = context.CurrentTenantId, lastSequence = 0L }),
            Status = OutboxStatus.Pending, CreatedUtc = now, NextAttemptUtc = now, CorrelationId = envelope.OperationId });
        // The S13 deferred outbox trigger fills lastSequence AFTER deferred journal events.
        // Enqueue must never save or commit: intake's audit/UoW persists this together with its effect.
        return Result.Ok();
    }
}
