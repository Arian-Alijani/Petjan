namespace Animora.Persistence.PostgreSql.Outbox;

public enum OutboxStatus : short { Pending, Claimed, Done, DeadLettered }
public sealed class OutboxMessage
{
    public string TenantId { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public string MessageType { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";
    public OutboxStatus Status { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset NextAttemptUtc { get; set; }
    public string? ClaimToken { get; set; }
    public DateTimeOffset? ClaimedUntilUtc { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset? CompletedUtc { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
}
