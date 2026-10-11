using Animora.Domain.Results;

namespace Animora.Application.Security;

/// <summary>
/// The online (server) session of the signed-in user on this installation: OS-protected bearer tokens plus the identity they belong to.
/// Declared in Application so both the HTTP pipeline (<c>BearerAuthHandler</c> in Sync.Custom) and the identity client
/// (<c>Client.Services</c>) depend on this port without referencing each other.
/// </summary>
/// <param name="AccessToken">Short-lived bearer access token.</param>
/// <param name="AccessExpiresUtc">When <paramref name="AccessToken"/> stops being valid.</param>
/// <param name="RefreshToken">Single-use refresh token (rotated on every refresh).</param>
/// <param name="UserId">Server user id.</param>
/// <param name="TenantId">Server tenant id.</param>
/// <param name="DeviceId">Server device id this installation registered as.</param>
/// <param name="Role">Server-assigned role name (informational; permissions come from the capability snapshot).</param>
/// <param name="SecurityStamp">Server security stamp the session was issued under (rotation invalidates offline capability).</param>
public sealed record ClientSession(
    string AccessToken,
    DateTimeOffset AccessExpiresUtc,
    string RefreshToken,
    string UserId,
    string TenantId,
    string DeviceId,
    string Role,
    string SecurityStamp);

/// <summary>Durable, OS-protected store of the single current <see cref="ClientSession"/>.</summary>
public interface ISessionStore
{
    /// <summary>The stored session, or <see langword="null"/> when nobody is signed in online.</summary>
    Task<Result<ClientSession?>> ReadAsync(CancellationToken ct = default);

    /// <summary>Replaces the stored session atomically (token rotation).</summary>
    Task<Result> WriteAsync(ClientSession session, CancellationToken ct = default);

    /// <summary>Forgets the session (logout, expiry or device revocation).</summary>
    Task<Result> ClearAsync(CancellationToken ct = default);
}
