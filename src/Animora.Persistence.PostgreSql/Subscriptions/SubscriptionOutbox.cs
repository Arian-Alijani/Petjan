using System.Text.Json;
using Animora.Contracts.Wire.Commands;
using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Animora.Domain.Time;
using Animora.Persistence.PostgreSql.Operator;
using Animora.Persistence.PostgreSql.Outbox;
namespace Animora.Persistence.PostgreSql.Subscriptions;

/// <summary>IServerOutbox adapter for the vendor-global business transaction; never saves independently.</summary>
public sealed class SubscriptionOutbox(AnimoraOperatorDbContext db, IClock clock) : IServerOutbox
{
    public Task<Result> AppendAsync(CommandEnvelope envelope, SettlementResult settlement, CancellationToken ct = default) =>
        throw new InvalidOperationException("Command feed uses the tenant outbox.");
    public Task<Result> EnqueueAsync(string messageType, string tenantId, string payloadJson, string correlationId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Caller transaction required.");
        if (string.IsNullOrWhiteSpace(tenantId) || tenantId.Length > 26 || string.IsNullOrWhiteSpace(messageType) || messageType.Length > 64
            || string.IsNullOrWhiteSpace(correlationId) || correlationId.Length > 64)
            return Task.FromResult(Result.Fail(ErrorCodes.ValidationFailed));
        try { using var payload = JsonDocument.Parse(payloadJson); }
        catch (JsonException) { return Task.FromResult(Result.Fail(ErrorCodes.ValidationFailed)); }
        var now = clock.UtcNow;
        db.Messages.Add(new OutboxMessage { TenantId = tenantId, Id = IdGenerator.NewId(now), MessageType = messageType,
            PayloadJson = payloadJson, CorrelationId = correlationId, Status = OutboxStatus.Pending, CreatedUtc = now, NextAttemptUtc = now });
        return Task.FromResult(Result.Ok());
    }
}
