using Animora.Domain.Subscriptions;

namespace Animora.Persistence.PostgreSql.Subscriptions;

public sealed class ProvisioningLedger
{
    public string IdempotencyKey { get; set; } = string.Empty;
    public string RequestFingerprint { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string SubscriptionId { get; set; } = string.Empty;
    public ActorKind ActorKind { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
}
