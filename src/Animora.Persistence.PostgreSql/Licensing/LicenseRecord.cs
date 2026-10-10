namespace Animora.Persistence.PostgreSql.Licensing;

public enum LicenseRecordStatus : short { Active, Superseded, Revoked }
/// <summary>Only payload digest and signing key id are persisted; never the signed token.</summary>
public sealed class LicenseRecord
{
    public string TenantId { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public string InstallationIdHash { get; set; } = string.Empty;
    public long Sequence { get; set; }
    public DateTimeOffset IssuedUtc { get; set; }
    public DateTimeOffset ExpiresUtc { get; set; }
    public DateTimeOffset OfflineGraceUntilUtc { get; set; }
    public string KeyId { get; set; } = string.Empty;
    public string PayloadSha256 { get; set; } = string.Empty;
    public LicenseRecordStatus Status { get; set; }
    public DateTimeOffset? RevokedUtc { get; set; }
    public string? RevokedByActor { get; set; }
    public string? RevokeReason { get; set; }
}
