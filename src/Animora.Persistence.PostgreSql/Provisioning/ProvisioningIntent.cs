namespace Animora.Persistence.PostgreSql.Provisioning;

public enum ProvisioningIntentStatus : short { Received = 0, Processing = 1, Completed = 2, Failed = 3, Dead = 4 }
public sealed class ProvisioningIntent
{
    public string Id { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string RequestFingerprint { get; set; } = string.Empty;
    public byte[]? PayloadProtected { get; set; }
    public ProvisioningIntentStatus Status { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset NextAttemptUtc { get; set; }
    public string? ClaimToken { get; set; }
    public DateTimeOffset? ClaimedUntilUtc { get; set; }
    public string? ResultJson { get; set; }
    public byte[]? ProtectedTempPassword { get; set; }
    public DateTimeOffset? TempPasswordExpiresUtc { get; set; }
    public string? ErrorCode { get; set; }
    public DateTimeOffset ReceivedUtc { get; set; }
    public DateTimeOffset? CompletedUtc { get; set; }
}
