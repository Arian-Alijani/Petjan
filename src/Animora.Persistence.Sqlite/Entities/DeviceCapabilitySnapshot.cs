using Animora.Application.Security;
using Animora.Domain.Primitives;

namespace Animora.Persistence.Sqlite.Entities;

/// <summary>
/// Installation-local offline capability snapshot row (B04b): exactly one current row per
/// <c>(TenantId, UserId, DeviceId)</c>, mirroring <see cref="StoredCapabilitySnapshot"/>. Signed with a keystore key
/// that is never stored here. Never replicated.
/// </summary>
[LocalOnly]
public sealed class DeviceCapabilitySnapshot
{
    public const int PermissionsJsonMaxLength = 8192;
    public const int ExpiryJsonMaxLength = 16384;
    public const int SignatureMaxLength = 64;

    private DeviceCapabilitySnapshot()
    {
    }

    public string Id { get; private set; } = string.Empty;
    public string TenantId { get; private set; } = string.Empty;
    public string UserId { get; private set; } = string.Empty;
    public string DeviceId { get; private set; } = string.Empty;
    public string PermissionsJson { get; private set; } = string.Empty;
    public DateTimeOffset IssuedAtUtc { get; private set; }
    public string PerCapabilityExpiryJson { get; private set; } = string.Empty;
    public DateTimeOffset AbsoluteExpiryUtc { get; private set; }
    public string SnapshotSignature { get; private set; } = string.Empty;
    public string SecurityStamp { get; private set; } = string.Empty;
    public DateTimeOffset? RevokedAtUtc { get; private set; }

    internal static DeviceCapabilitySnapshot From(StoredCapabilitySnapshot s) => new()
    {
        Id = s.Id,
        TenantId = s.TenantId,
        UserId = s.UserId,
        DeviceId = s.DeviceId,
        PermissionsJson = s.PermissionsJson,
        IssuedAtUtc = s.IssuedAtUtc,
        PerCapabilityExpiryJson = s.PerCapabilityExpiryJson,
        AbsoluteExpiryUtc = s.AbsoluteExpiryUtc,
        SnapshotSignature = s.SnapshotSignature,
        SecurityStamp = s.SecurityStamp,
        RevokedAtUtc = s.RevokedAtUtc,
    };

    internal StoredCapabilitySnapshot ToStored() => new(Id, TenantId, UserId, DeviceId, PermissionsJson, IssuedAtUtc,
        PerCapabilityExpiryJson, AbsoluteExpiryUtc, SnapshotSignature, SecurityStamp, RevokedAtUtc);

    /// <summary>Applies a revocation whose signature was computed over the revoked state.</summary>
    internal void ApplyRevocation(DateTimeOffset revokedAtUtc, string signature)
    {
        RevokedAtUtc = revokedAtUtc;
        SnapshotSignature = signature;
    }
}
