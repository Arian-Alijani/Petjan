using Animora.Domain.Primitives;

namespace Animora.Persistence.Sqlite.Entities;

/// <summary>
/// G06: the single online (server) session of one user on this installation, plus the one-way offline credential verifier mirrored
/// at the last successful online login. <see cref="LocalOnlyAttribute">Local-only</see> — never replicated, no <c>Version</c>/<c>Deleted</c>.
/// <para>
/// <see cref="AccessToken"/> / <see cref="RefreshToken"/> hold <b>OS-protected ciphertext</b> (AES-GCM under a key that lives only in the
/// OS keystore), never the raw bearer token; the whole file is additionally the encrypted local store. Unique per
/// <c>(TenantId, UserId, DeviceId)</c>.
/// </para>
/// </summary>
[LocalOnly]
public sealed class ClientSessionRow
{
    /// <summary>Max length of a protected token column (ciphertext + nonce + tag, base64).</summary>
    public const int TokenMaxLength = 8192;

    /// <summary>Max length of the offline credential verifier (a password-hasher output).</summary>
    public const int VerifierMaxLength = 512;

    private ClientSessionRow() { }

    /// <summary>ULID primary key.</summary>
    public string Id { get; private set; } = string.Empty;

    /// <summary>Server tenant id.</summary>
    public string TenantId { get; private set; } = string.Empty;

    /// <summary>Server user id.</summary>
    public string UserId { get; private set; } = string.Empty;

    /// <summary>Server device id this installation registered as.</summary>
    public string DeviceId { get; private set; } = string.Empty;

    /// <summary>Protected bearer access token.</summary>
    public string AccessToken { get; private set; } = string.Empty;

    /// <summary>When the access token expires.</summary>
    public DateTimeOffset AccessExpiresUtc { get; private set; }

    /// <summary>Protected single-use refresh token.</summary>
    public string RefreshToken { get; private set; } = string.Empty;

    /// <summary>Server role name (informational).</summary>
    public string Role { get; private set; } = string.Empty;

    /// <summary>Server security stamp the session was issued under.</summary>
    public string SecurityStamp { get; private set; } = string.Empty;

    /// <summary>One-way verifier of the user's password for a cold offline start; <see langword="null"/> until mirrored.</summary>
    public string? OfflineCredentialVerifier { get; private set; }

    /// <summary>Last write instant.</summary>
    public DateTimeOffset UpdatedUtc { get; private set; }

    /// <summary>Creates a new row.</summary>
    public static ClientSessionRow Create(string tenantId, string userId, string deviceId, string protectedAccess,
        DateTimeOffset accessExpiresUtc, string protectedRefresh, string role, string securityStamp,
        string? offlineCredentialVerifier, DateTimeOffset nowUtc)
    {
        var row = new ClientSessionRow { Id = IdGenerator.NewId(), TenantId = tenantId, UserId = userId, DeviceId = deviceId };
        row.Replace(protectedAccess, accessExpiresUtc, protectedRefresh, role, securityStamp, offlineCredentialVerifier, nowUtc);
        return row;
    }

    /// <summary>
    /// Overwrites the mutable columns (token rotation / re-login). A <see langword="null"/> verifier keeps the existing one, so a refresh
    /// never wipes the offline credential mirror.
    /// </summary>
    public void Replace(string protectedAccess, DateTimeOffset accessExpiresUtc, string protectedRefresh, string role,
        string securityStamp, string? offlineCredentialVerifier, DateTimeOffset nowUtc)
    {
        AccessToken = protectedAccess;
        AccessExpiresUtc = accessExpiresUtc.ToUniversalTime();
        RefreshToken = protectedRefresh;
        Role = role;
        SecurityStamp = securityStamp;
        if (offlineCredentialVerifier is not null) OfflineCredentialVerifier = offlineCredentialVerifier;
        UpdatedUtc = nowUtc.ToUniversalTime();
    }
}
