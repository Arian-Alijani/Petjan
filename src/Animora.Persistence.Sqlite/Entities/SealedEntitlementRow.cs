using Animora.Domain.Primitives;

namespace Animora.Persistence.Sqlite.Entities;

/// <summary>
/// G06: the sealed local licence cache — one row per tenant, the latest signed licence token plus the forward-only high-water clock and
/// highest sequence ever seen. <see cref="LocalOnlyAttribute">Local-only</see> (no <c>Version</c>/<c>Deleted</c>); lives in the encrypted
/// local store. The token is server-signed, so it is re-verified on every read: editing this row cannot forge entitlement.
/// </summary>
[LocalOnly]
public sealed class SealedEntitlementRow
{
    /// <summary>Max length of a licence token (matches the verifier's own ceiling).</summary>
    public const int TokenMaxLength = 4096;

    /// <summary>Length of the lowercase-hex SHA-256 installation hash.</summary>
    public const int InstallationHashLength = 64;

    private SealedEntitlementRow() { }

    /// <summary>Owning tenant (primary key — one cache per tenant).</summary>
    public string TenantId { get; private set; } = string.Empty;

    /// <summary>The signed licence token.</summary>
    public string LicenseToken { get; private set; } = string.Empty;

    /// <summary>Forward-only high-water instant, Unix milliseconds.</summary>
    public long HighWaterUnixMs { get; private set; }

    /// <summary>Highest licence sequence ever sealed.</summary>
    public long HighestSequenceSeen { get; private set; }

    /// <summary>Installation the licence is bound to.</summary>
    public string InstallationIdHash { get; private set; } = string.Empty;

    /// <summary>Last write instant, Unix milliseconds (forward-only).</summary>
    public long WrittenUnixMs { get; private set; }

    /// <summary>Creates a row for a tenant.</summary>
    public static SealedEntitlementRow Create(string tenantId, string licenseToken, long highWaterUnixMs, long highestSequenceSeen,
        string installationIdHash, long writtenUnixMs)
    {
        var row = new SealedEntitlementRow { TenantId = tenantId };
        row.Replace(licenseToken, highWaterUnixMs, highestSequenceSeen, installationIdHash, writtenUnixMs);
        return row;
    }

    /// <summary>Overwrites the cache with an already-merged (forward-only) value.</summary>
    public void Replace(string licenseToken, long highWaterUnixMs, long highestSequenceSeen, string installationIdHash, long writtenUnixMs)
    {
        LicenseToken = licenseToken;
        HighWaterUnixMs = highWaterUnixMs;
        HighestSequenceSeen = highestSequenceSeen;
        InstallationIdHash = installationIdHash;
        WrittenUnixMs = writtenUnixMs;
    }
}
