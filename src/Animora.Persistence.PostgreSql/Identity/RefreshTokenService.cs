using System.Security.Cryptography;
using System.Text;
using Animora.Domain.Primitives;
using Animora.Domain.Time;

namespace Animora.Persistence.PostgreSql.Identity;

/// <summary>Outcome class of presenting a refresh token.</summary>
public enum RotationStatus
{
    /// <summary>The token was current and has been rotated into a new one.</summary>
    Rotated,

    /// <summary>Unknown, malformed, expired or revoked token.</summary>
    Invalid,

    /// <summary>An already-rotated token was replayed: the whole family has been revoked.</summary>
    Reused,
}

/// <summary>Result of <see cref="RefreshTokenService.RotateAsync"/>.</summary>
/// <param name="Status">What happened.</param>
/// <param name="Previous">The presented row (when it was found).</param>
/// <param name="WireToken">The new opaque token to hand to the client (only when rotated).</param>
/// <param name="Current">The newly issued row (only when rotated).</param>
public sealed record RotationOutcome(RotationStatus Status, RefreshToken? Previous, string? WireToken, RefreshToken? Current);

/// <summary>
/// Issues and rotates opaque refresh tokens. A wire token is <c>{tenantId}.{256-bit base64url}</c>: the
/// tenant prefix lets the unauthenticated <c>/auth/refresh</c> bind the tenant before the (tenant-filtered)
/// lookup, and only the SHA-256 hex of the full token is stored. Lifetime is 30 days, sliding disabled.
/// </summary>
public sealed class RefreshTokenService(IRefreshTokenStore store, IClock clock)
{
    /// <summary>Refresh-token lifetime (absolute, never extended).</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(30);

    private const int RandomBytes = 32;
    private const int TenantIdLength = 26;

    /// <summary>SHA-256 (lowercase hex, 64 chars) of a wire token.</summary>
    public static string Hash(string wireToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(wireToken);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(wireToken)));
    }

    /// <summary>Extracts the tenant prefix of a wire token, or <see langword="null"/> when malformed.</summary>
    public static string? TryReadTenantId(string? wireToken)
    {
        if (wireToken is null || wireToken.Length <= TenantIdLength + 1 || wireToken[TenantIdLength] != '.')
        {
            return null;
        }

        for (int i = 0; i < TenantIdLength; i++)
        {
            if (!char.IsAsciiLetterOrDigit(wireToken[i]))
            {
                return null;
            }
        }

        return wireToken[..TenantIdLength];
    }

    /// <summary>
    /// Creates a fresh token row and stages it on the store (committed by the caller's next save), returning
    /// the wire token. <paramref name="familyId"/> is kept across rotations; a new family starts when omitted.
    /// </summary>
    public (string WireToken, RefreshToken Row) Issue(string tenantId, string userId, string deviceId, string? familyId = null)
    {
        (string wire, RefreshToken row) = Create(tenantId, userId, deviceId, familyId);
        store.Add(row);
        return (wire, row);
    }

    /// <summary>Reads (without rotating) the row behind a presented wire token; <see langword="null"/> when unknown.</summary>
    public Task<RefreshToken?> FindAsync(string presentedToken, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(presentedToken);
        return store.FindByHashAsync(Hash(presentedToken), ct);
    }

    /// <summary>
    /// Rotates <paramref name="presentedToken"/> (tenant already bound). Replaying an already-rotated token
    /// revokes the whole family here; rotating the security stamp and auditing is the caller's job.
    /// </summary>
    public async Task<RotationOutcome> RotateAsync(string presentedToken, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(presentedToken);

        DateTimeOffset now = clock.UtcNow;
        RefreshToken? old = await store.FindByHashAsync(Hash(presentedToken), ct).ConfigureAwait(false);

        if (old is null)
        {
            return new RotationOutcome(RotationStatus.Invalid, null, null, null);
        }

        if (old.RotatedToId is not null)
        {
            await store.RevokeFamilyAsync(old.FamilyId, now, ct).ConfigureAwait(false);
            return new RotationOutcome(RotationStatus.Reused, old, null, null);
        }

        if (!old.IsActive(now))
        {
            return new RotationOutcome(RotationStatus.Invalid, old, null, null);
        }

        (string wire, RefreshToken fresh) = Create(old.TenantId, old.UserId, old.DeviceId, old.FamilyId);

        if (!await store.TryRotateAsync(old.Id, fresh, now, ct).ConfigureAwait(false))
        {
            // Lost a race with a concurrent rotation of the same token: that is reuse.
            await store.RevokeFamilyAsync(old.FamilyId, now, ct).ConfigureAwait(false);
            return new RotationOutcome(RotationStatus.Reused, old, null, null);
        }

        return new RotationOutcome(RotationStatus.Rotated, old, wire, fresh);
    }

    /// <summary>Revokes every still-open token of a rotation family. Returns the number revoked.</summary>
    public Task<int> RevokeFamilyAsync(string familyId, CancellationToken ct = default) =>
        store.RevokeFamilyAsync(familyId, clock.UtcNow, ct);

    /// <summary>Revokes every still-open token a user holds on one device (logout). Returns the number revoked.</summary>
    public Task<int> RevokeDeviceTokensAsync(string userId, string deviceId, CancellationToken ct = default) =>
        store.RevokeDeviceTokensAsync(userId, deviceId, clock.UtcNow, ct);

    private (string WireToken, RefreshToken Row) Create(string tenantId, string userId, string deviceId, string? familyId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        DateTimeOffset now = clock.UtcNow;
        string wire = tenantId + "." + Base64Url(RandomNumberGenerator.GetBytes(RandomBytes));
        RefreshToken row = RefreshToken.Issue(
            IdGenerator.NewId(now),
            tenantId,
            userId,
            deviceId,
            Hash(wire),
            familyId ?? IdGenerator.NewId(now),
            now,
            now + Lifetime);
        return (wire, row);
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
