using System.Text.Json;
using Animora.Application.Abstractions;
using Animora.Application.Outbox;
using Animora.Domain.Events;
using Animora.Domain.Results;

namespace Animora.Persistence.Sqlite.Outbox;

/// <summary>
/// The A10 dispatcher's relay handler: writes every raised domain event to the outbox, in the same transaction
/// as the state change that produced it (ARCHITECTURE.md §12.1). This is what lets a local projection consumer
/// be added later without touching any aggregate.
/// </summary>
public sealed class OutboxRelayHandler(IOutbox outbox) : IDomainEventHandler<DomainEvent>
{
    /// <summary>Outbox message type for a relayed domain event — the event's own type name.</summary>
    public static string MessageTypeFor(DomainEvent domainEvent) => domainEvent.GetType().Name;

    public Task<Result> HandleAsync(DomainEvent e, CancellationToken ct) =>
        outbox.EnqueueAsync(MessageTypeFor(e), e, ct);
}

/// <summary>Serializes an outbox payload for storage; kept here so the relay shape is testable in isolation.</summary>
internal static class OutboxPayload
{
    public static string Serialize(object payload) => JsonSerializer.Serialize(payload, payload.GetType());
}
