namespace Animora.Persistence.PostgreSql.Outbox;

public sealed class ConsumedMessage
{
    public string ConsumerName { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string MessageId { get; set; } = string.Empty;
    public DateTimeOffset ConsumedUtc { get; set; }
}
