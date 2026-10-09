using System.Security.Claims;

namespace Animora.Server.Auth;

/// <summary>The identification claims of an authenticated bearer principal.</summary>
/// <param name="UserId">User ULID.</param>
/// <param name="TenantId">Tenant ULID.</param>
/// <param name="DeviceId">Device ULID.</param>
/// <param name="SecurityStamp">Security stamp the token was issued under.</param>
internal sealed record AuthenticatedCaller(string UserId, string TenantId, string DeviceId, string SecurityStamp)
{
    /// <summary>Reads the caller from <paramref name="principal"/>; <see langword="null"/> when anonymous or incomplete.</summary>
    public static AuthenticatedCaller? TryRead(ClaimsPrincipal? principal)
    {
        if (principal?.Identity is not { IsAuthenticated: true })
        {
            return null;
        }

        string? sub = principal.FindFirstValue(AuthClaimTypes.Sub);
        string? tid = principal.FindFirstValue(AuthClaimTypes.Tid);
        string? did = principal.FindFirstValue(AuthClaimTypes.Did);
        string? sst = principal.FindFirstValue(AuthClaimTypes.Sst);

        return string.IsNullOrEmpty(sub) || string.IsNullOrEmpty(tid) || string.IsNullOrEmpty(did) || string.IsNullOrEmpty(sst)
            ? null
            : new AuthenticatedCaller(sub, tid, did, sst);
    }
}
