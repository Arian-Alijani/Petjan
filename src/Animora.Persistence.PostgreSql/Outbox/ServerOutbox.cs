using System.Text.Json;
using Animora.Contracts.Wire.Commands;
using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Animora.Domain.Time;
using Animora.Persistence.PostgreSql.Context;
using Animora.Persistence.PostgreSql.Replication;
using Microsoft.EntityFrameworkCore;
namespace Animora.Persistence.PostgreSql.Outbox;

/// <summary>Persistence-owned seam: the server may depend inward on it, never the reverse.</summary>
public interface IServerOutbox
{
    Task<Result> AppendAsync(CommandEnvelope envelope, SettlementResult settlement, CancellationToken ct = default);
    Task<Result> EnqueueAsync(string messageType, string tenantId, string payloadJson, string correlationId,
        CancellationToken ct = default);
}
public sealed class ServerOutbox(AnimoraServerDbContext context, IClock clock) : IServerOutbox
{
    /// <summary>Stages a tenant-addressed message without saving. The caller owns the transaction.</summary>
    public Task<Result> EnqueueAsync(string messageType, string tenantId, string payloadJson, string correlationId,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Outbox requires the business transaction.");
        if (string.IsNullOrWhiteSpace(messageType) || messageType.Length > 64 ||
            string.IsNullOrWhiteSpace(tenantId) || tenantId.Length > 26 ||
            string.IsNullOrWhiteSpace(correlationId) || correlationId.Length > 64)
            return Task.FromResult(Result.Fail(ErrorCodes.ValidationFailed, "invalid_outbox_address"));
        try { using var document = JsonDocument.Parse(payloadJson); }
        catch (JsonException) { return Task.FromResult(Result.Fail(ErrorCodes.ValidationFailed, "invalid_outbox_payload")); }
        var now = clock.UtcNow;
        context.Set<OutboxMessage>().Add(new OutboxMessage
        {
            TenantId = tenantId, Id = IdGenerator.NewId(now), MessageType = messageType,
            PayloadJson = payloadJson, Status = OutboxStatus.Pending, CreatedUtc = now,
            NextAttemptUtc = now, CorrelationId = correlationId,
        });
        return Task.FromResult(Result.Ok());
    }

    public async Task<Result> AppendAsync(CommandEnvelope envelope, SettlementResult settlement, CancellationToken ct = default)
    {
        if (settlement.Outcome != SettlementOutcome.Accepted) return Result.Ok();
        if (context.Database.CurrentTransaction is null) throw new InvalidOperationException("Outbox requires the business transaction.");
        // Trigger inserts are not EF-tracked. Read pending rows visible in our own transaction;
        // every other transaction's committed rows already have a sequence.
        if (!await context.Set<TenantJournalRow>().AnyAsync(j => j.ChangeSequence == null, ct).ConfigureAwait(false)) return Result.Ok();
        if (context.ChangeTracker.Entries<OutboxMessage>().Any(e => e.Entity.MessageType == OutboxMessageTypes.FeedAdvanced &&
            e.Entity.TenantId == context.CurrentTenantId)) return Result.Ok();
        var now = clock.UtcNow;
        context.Set<OutboxMessage>().Add(new() { TenantId = context.CurrentTenantId, Id = IdGenerator.NewId(now),
            MessageType = OutboxMessageTypes.FeedAdvanced, PayloadJson = JsonSerializer.Serialize(new { tenantId = context.CurrentTenantId, lastSequence = 0L }),
            Status = OutboxStatus.Pending, CreatedUtc = now, NextAttemptUtc = now, CorrelationId = envelope.OperationId });
        // The S13 deferred outbox trigger fills lastSequence AFTER deferred journal events.
        // Enqueue must never save or commit: intake's audit/UoW persists this together with its effect.
        return Result.Ok();
    }
}
