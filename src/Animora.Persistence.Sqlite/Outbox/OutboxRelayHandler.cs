using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
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
    // F-08: the finance/inventory events the projection worker consumes resolve through the
    // source-generated context (reflection-free); every other domain event the relay also enqueues keeps
    // working via the chained reflection resolver. The context uses default naming, so the stored JSON
    // shape is unchanged. Cached once — the resolver chain is immutable and thread-safe.
    private static readonly JsonSerializerOptions Options = new()
    {
        TypeInfoResolver = JsonTypeInfoResolver.Combine(OutboxEventJsonContext.Default, new DefaultJsonTypeInfoResolver()),
    };

    public static string Serialize(object payload) => JsonSerializer.Serialize(payload, payload.GetType(), Options);
}
