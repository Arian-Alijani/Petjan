namespace Animora.Contracts.Wire.Auth;

/// <summary>
/// Credentials presented to <c>POST /auth/login</c> (S04). Login is tenant-scoped: the tenant is
/// resolved from <paramref name="TenantCode"/> before any user lookup. A wrong tenant, user or
/// password all return the same <c>CREDENTIALS_INVALID</c> (no user enumeration).
/// </summary>
/// <param name="TenantCode">Short tenant slug (resolved via the tenant directory).</param>
/// <param name="UserName">Login name, unique per tenant.</param>
/// <param name="Password">Plaintext password (verified against the stored hash; never logged).</param>
/// <param name="DeviceInstallationIdHash">Opaque hash binding this login to a registered device.</param>
public sealed record LoginRequest(
    string TenantCode,
    string UserName,
    string Password,
    string DeviceInstallationIdHash);
