using Animora.Domain.Primitives;

namespace Animora.Persistence.PostgreSql.Identity;

/// <summary>
/// A server-issued refresh token (S04). <b>Not</b> a <see cref="SyncableEntity"/> — it never
/// replicates to a client and carries no <c>Version</c>/<c>Deleted</c>. Append-only except the two
/// nullable rotation columns (<see cref="RotatedToId"/>, <see cref="RevokedUtc"/>). Only the SHA-256
/// of the opaque wire token is stored (<see cref="TokenHash"/>); the token itself is never persisted.
/// </summary>
/// <remarks>
/// Rotation: each refresh issues a new row keeping the same <see cref="FamilyId"/> and marks the old
/// row's <see cref="RotatedToId"/>. Presenting an already-rotated token means reuse: the whole family
/// is revoked (reuse detection).
/// </remarks>
public sealed class RefreshToken
{
    private RefreshToken()
    {
    }

    /// <summary>Token row ULID.</summary>
    public string Id { get; private set; } = string.Empty;

    /// <summary>Owning tenant ULID (tenant-filtered).</summary>
    public string TenantId { get; private set; } = string.Empty;

    /// <summary>User ULID this token authenticates.</summary>
    public string UserId { get; private set; } = string.Empty;

    /// <summary>Device ULID this token is bound to.</summary>
    public string DeviceId { get; private set; } = string.Empty;

    /// <summary>SHA-256 (hex) of the opaque wire token. The raw token is never stored.</summary>
    public string TokenHash { get; private set; } = string.Empty;

    /// <summary>Rotation family id — constant across a chain of rotated tokens.</summary>
    public string FamilyId { get; private set; } = string.Empty;

    /// <summary>When this token was issued, UTC.</summary>
    public DateTimeOffset IssuedUtc { get; private set; }

    /// <summary>When this token expires, UTC (30 days after issue; sliding disabled).</summary>
    public DateTimeOffset ExpiresUtc { get; private set; }

    /// <summary>The successor token id once this one is rotated; <see langword="null"/> while current.</summary>
    public string? RotatedToId { get; private set; }

    /// <summary>When this token was revoked, UTC; <see langword="null"/> while usable.</summary>
    public DateTimeOffset? RevokedUtc { get; private set; }

    /// <summary><see langword="true"/> when the token can still be presented at <paramref name="nowUtc"/>.</summary>
    public bool IsActive(DateTimeOffset nowUtc) =>
        RevokedUtc is null && RotatedToId is null && ExpiresUtc > nowUtc;

    /// <summary>Creates a fresh refresh-token row. <paramref name="familyId"/> is kept across rotations.</summary>
    public static RefreshToken Issue(
        string id,
        string tenantId,
        string userId,
        string deviceId,
        string tokenHash,
        string familyId,
        DateTimeOffset issuedUtc,
        DateTimeOffset expiresUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(familyId);

        return new RefreshToken
        {
            Id = id,
            TenantId = tenantId,
            UserId = userId,
            DeviceId = deviceId,
            TokenHash = tokenHash,
            FamilyId = familyId,
            IssuedUtc = issuedUtc,
            ExpiresUtc = expiresUtc,
        };
    }

    /// <summary>Marks this token rotated into <paramref name="successorId"/>. Idempotent.</summary>
    public void MarkRotated(string successorId, DateTimeOffset nowUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(successorId);
        RotatedToId ??= successorId;
        RevokedUtc ??= nowUtc;
    }

    /// <summary>Revokes this token. Idempotent: keeps the original instant.</summary>
    public void Revoke(DateTimeOffset nowUtc) => RevokedUtc ??= nowUtc;
}
