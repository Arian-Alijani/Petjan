using Animora.Domain.Results;

namespace Animora.Client.Services.Identity;

/// <summary>Who is signed in after <see cref="IOnlineAuthClient.LoginAsync"/>, and whether the server vouched for it just now.</summary>
/// <param name="TenantId">Server tenant id.</param>
/// <param name="UserId">Server user id.</param>
/// <param name="DeviceId">Server device id of this installation.</param>
/// <param name="Role">Server role name (informational).</param>
/// <param name="IsOffline"><see langword="true"/> when the server was unreachable and the credential mirror authorised the start.</param>
/// <param name="MustChangePassword">The server demands a new password before other actions (always <see langword="false"/> offline).</param>
public sealed record OnlineLoginOutcome(string TenantId, string UserId, string DeviceId, string Role, bool IsOffline, bool MustChangePassword);

/// <summary>Online leg of the identity plane: login / refresh / logout against the central server, with a fail-closed offline fallback.</summary>
public interface IOnlineAuthClient
{
    /// <summary>
    /// Signs in online (persisting the session, registering the device, mirroring capability snapshot + sealed licence + a one-way password
    /// verifier). A definitive server refusal is returned as-is (<c>CREDENTIALS_INVALID</c>, <c>USER_LOCKED_OUT</c>, <c>DEVICE_REVOKED</c>…); when
    /// the server is unreachable the credential mirror may authorise an offline start, otherwise <c>NETWORK_UNAVAILABLE</c> /
    /// <c>OFFLINE_SESSION_UNAVAILABLE</c> / <c>OFFLINE_CREDENTIAL_MISMATCH</c>.
    /// <para><paramref name="preserveUnsyncedOnSwitch"/>: the user explicitly confirmed entering another clinic while the previous one still has
    /// unsynced commands; they are then parked in a per-clinic stash (restored when that clinic signs in again) instead of the login failing
    /// with <c>SYNC_PENDING_CHANGES</c>.</para>
    /// </summary>
    Task<Result<OnlineLoginOutcome>> LoginAsync(string tenantCode, string userName, string password, CancellationToken ct = default,
        bool preserveUnsyncedOnSwitch = false);

    /// <summary>Rotates the stored refresh token; <c>SESSION_EXPIRED</c> clears the session.</summary>
    Task<Result> RefreshAsync(CancellationToken ct = default);

    /// <summary>Ends the server session (best effort) and always clears the local one.</summary>
    Task<Result> LogoutAsync(CancellationToken ct = default);
}
