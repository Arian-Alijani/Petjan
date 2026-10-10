namespace Animora.Domain.Subscriptions;

public sealed record SubscriptionEvent(string SubscriptionId, int Sequence, string Type,
    DateTimeOffset OccurredUtc, ActorKind ActorKind, string? ActorId, string PayloadJson);
