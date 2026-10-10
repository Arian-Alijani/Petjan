using Animora.Domain.Results;
using Animora.Persistence.PostgreSql.Outbox;
using Animora.Persistence.PostgreSql.Subscriptions;

namespace Animora.Server.Subscriptions;

internal sealed class SubscriptionExpiryConsumer(SubscriptionExpiryProcessor processor) : IOutboxConsumer
{
    public string MessageType => OutboxMessageTypes.SubscriptionExpiry;
    public string Name => "subscription-expiry";
    public Task<Result> ConsumeAsync(OutboxMessage message, CancellationToken ct) => processor.ConsumeAsync(message, ct);
}
