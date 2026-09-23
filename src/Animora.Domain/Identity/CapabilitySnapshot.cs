namespace Animora.Domain.Identity;

/// <summary>
/// A signed, device-bound statement of what a user may do while offline
/// (<c>ARCHITECTURE.md</c> §10.4, <b>A-RULE 029</b>).
/// </summary>
/// <remarks>
/// <para>
/// This is <b>not</b> a bearer token: it grants nothing by itself, carries an explicit absolute
/// expiry per capability (derived from <see cref="OfflineCapabilityPolicy"/> at issuance) and
/// fails closed on every doubt — unknown code, elapsed capability window, elapsed snapshot
/// window (<c>CONVENTIONS.md</c> §7).
/// </para>
/// <para>
/// Issuance and signature verification are server-side concerns (D03/E07); the domain only
/// carries <see cref="Signature"/> as an opaque base64 string and evaluates the expiries.
/// <see cref="SecurityStamp"/> must match the user's current stamp, or the snapshot is stale.
/// </para>
/// </remarks>
/// <param name="TenantId">The tenant this authority applies inside (A-RULE 012).</param>
/// <param name="UserId">The user the snapshot was issued for.</param>
/// <param name="DeviceId">The device the snapshot is bound to; a revoked device invalidates it.</param>
/// <param name="Role">The staff role at issuance time.</param>
/// <param name="CapabilityExpiry">Absolute UTC expiry per permission code.</param>
/// <param name="IssuedUtc">Server instant of issuance.</param>
/// <param name="SnapshotExpiryUtc">Absolute UTC expiry of the snapshot as a whole.</param>
/// <param name="SecurityStamp">The user's security stamp at issuance.</param>
/// <param name="Signature">Base64 server signature; produced and verified outside the domain.</param>
public sealed record CapabilitySnapshot(
    string TenantId,
    string UserId,
    string DeviceId,
    StaffRole Role,
    IReadOnlyDictionary<string, DateTimeOffset> CapabilityExpiry,
    DateTimeOffset IssuedUtc,
    DateTimeOffset SnapshotExpiryUtc,
    string SecurityStamp,
    string Signature)
{
    /// <summary>
    /// <see langword="true"/> when <paramref name="permissionCode"/> may be exercised at
    /// <paramref name="nowUtc"/>. Denies when the snapshot expired, when the code is absent
    /// (unknown = deny) or when the capability's own expiry has passed. An expiry equal to
    /// <paramref name="nowUtc"/> is already elapsed — a zero offline window therefore never grants.
    /// </summary>
    public bool Allows(string permissionCode, DateTimeOffset nowUtc)
    {
        if (string.IsNullOrWhiteSpace(permissionCode) || IsExpired(nowUtc))
        {
            return false;
        }

        return CapabilityExpiry.TryGetValue(permissionCode, out var expiry) && expiry > nowUtc;
    }

    /// <summary><see langword="true"/> once the snapshot as a whole is no longer valid.</summary>
    public bool IsExpired(DateTimeOffset nowUtc) => SnapshotExpiryUtc <= nowUtc;

    /// <summary>
    /// Builds the per-capability expiry map for <paramref name="permissionCodes"/> by applying
    /// <see cref="OfflineCapabilityPolicy"/> to each code's risk level. Helper for the issuer
    /// (D03); the domain keeps the rule in one place.
    /// </summary>
    public static IReadOnlyDictionary<string, DateTimeOffset> BuildExpiry(
        IEnumerable<string> permissionCodes,
        DateTimeOffset issuedUtc)
    {
        ArgumentNullException.ThrowIfNull(permissionCodes);

        var map = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        foreach (var code in permissionCodes)
        {
            map[code] = issuedUtc + OfflineCapabilityPolicy.WindowForCode(code);
        }

        return map;
    }
}
