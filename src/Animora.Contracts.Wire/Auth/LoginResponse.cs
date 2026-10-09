namespace Animora.Contracts.Wire.Auth;

/// <summary>
/// Successful result of <c>POST /auth/login</c> and <c>POST /auth/refresh</c> (same shape, S04).
/// The access token is opaque (Data-Protection-protected, not a JWT). The role is carried for
/// client UX only — authorization (S05) always re-reads the stored membership/role per request.
/// </summary>
/// <param name="AccessToken">Opaque bearer token (lifetime <see cref="ExpiresInSeconds"/>).</param>
/// <param name="ExpiresInSeconds">Access-token lifetime in seconds (900 = 15 min).</param>
/// <param name="RefreshToken">Opaque refresh token (30-day lifetime; only its hash is stored).</param>
/// <param name="UserId">Authenticated user's ULID.</param>
/// <param name="TenantId">Resolved tenant's ULID.</param>
/// <param name="DeviceId">Bound device's ULID.</param>
/// <param name="Role">Active staff role name (identification/UX only).</param>
/// <param name="MustChangePassword">The user must set a new password before other actions.</param>
public sealed record LoginResponse(
    string AccessToken,
    int ExpiresInSeconds,
    string RefreshToken,
    string UserId,
    string TenantId,
    string DeviceId,
    string Role,
    bool MustChangePassword);
