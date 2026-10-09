namespace Animora.Server.Auth;

/// <summary>
/// Claim names carried by the opaque access token. Claims are <b>identification only</b>: the stored
/// role/permissions are re-read server-side on every authorization decision (S05) — a role claim is
/// never trusted as proof of a permission.
/// </summary>
internal static class AuthClaimTypes
{
    /// <summary>User ULID.</summary>
    public const string Sub = "sub";

    /// <summary>Tenant ULID.</summary>
    public const string Tid = "tid";

    /// <summary>Device ULID.</summary>
    public const string Did = "did";

    /// <summary>Security stamp at issue time; a rotated stamp invalidates outstanding access tokens.</summary>
    public const string Sst = "sst";

    /// <summary>Staff role name at issue time (informational only).</summary>
    public const string Role = "role";
}
