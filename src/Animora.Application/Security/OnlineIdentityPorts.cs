using Animora.Domain.Results;

namespace Animora.Application.Security;

/// <summary>
/// Refreshes the online session (rotates the single-use refresh token). Declared in Application so the HTTP pipeline
/// (<c>BearerAuthHandler</c> in Sync.Custom) can trigger a refresh without referencing the identity client in Client.Services.
/// </summary>
public interface ISessionRefresher
{
    /// <summary>Exchanges the stored refresh token for a new session; <c>SESSION_EXPIRED</c> clears the session.</summary>
    Task<Result> RefreshAsync(CancellationToken ct = default);
}

/// <summary>
/// The credential mirror written at the last successful online login: who signed in, the device they signed in on, and a one-way
/// verifier (never the password) that lets the same user start cold while the server is unreachable.
/// </summary>
/// <param name="TenantId">Server tenant id.</param>
/// <param name="UserId">Server user id.</param>
/// <param name="DeviceId">Server device id of this installation.</param>
/// <param name="Verifier">One-way <see cref="IPasswordHasher"/> output over <c>tenantCode ␟ userName ␟ password</c>.</param>
/// <param name="SecurityStamp">Server security stamp the mirror was written under.</param>
/// <param name="Role">Server role name (informational).</param>
public sealed record OfflineCredentialMirror(string TenantId, string UserId, string DeviceId, string Verifier, string SecurityStamp, string Role);

/// <summary>Durable store of the single <see cref="OfflineCredentialMirror"/> of this installation (inside the encrypted local store).</summary>
public interface IOfflineCredentialMirrorStore
{
    /// <summary>The mirror, or <see langword="null"/> when no online login ever completed here (or it was wiped).</summary>
    Task<Result<OfflineCredentialMirror?>> ReadAsync(CancellationToken ct = default);

    /// <summary>Stores <paramref name="verifier"/> for an existing session row (the session is written first).</summary>
    Task<Result> WriteVerifierAsync(string tenantId, string userId, string deviceId, string verifier, CancellationToken ct = default);
}

/// <summary>
/// Removes every piece of offline authority (session, credential mirror, sealed licence, capability snapshot). Invoked when the server
/// reports the device revoked: from the next contact on this installation can no longer act offline.
/// </summary>
public interface IOfflineAuthorityWiper
{
    /// <summary>Wipes all offline authority; safe to call repeatedly.</summary>
    Task<Result> WipeAsync(CancellationToken ct = default);
}
