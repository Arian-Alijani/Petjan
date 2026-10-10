namespace Animora.Domain.Subscriptions;

public sealed record BillingPeriod(string SubscriptionId, int Sequence, DateTimeOffset StartUtc,
    DateTimeOffset EndUtc, long AmountMinor, string Currency);
