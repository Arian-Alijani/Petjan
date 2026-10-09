using System.Security.Claims;
using Animora.Domain.Time;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Animora.Server.Auth;

/// <summary>Identification data baked into an access token.</summary>
internal sealed record AccessTokenClaims(string UserId, string TenantId, string DeviceId, string SecurityStamp, string Role);

/// <summary>An issued opaque access token and its lifetime.</summary>
internal sealed record IssuedAccessToken(string Token, int ExpiresInSeconds);

/// <summary>Seam over the opaque bearer-token protector so sign-in logic is testable without Data Protection.</summary>
internal interface IAccessTokenIssuer
{
    /// <summary>Issues a short-lived opaque access token for <paramref name="claims"/>.</summary>
    IssuedAccessToken Issue(AccessTokenClaims claims);
}

/// <summary>
/// Issues access tokens with the framework bearer handler's own Data-Protection-protected ticket format
/// (key ring in PostgreSQL, S02) — opaque, no JWT. The same protector validates them on each request.
/// </summary>
internal sealed class BearerAccessTokenIssuer(IOptionsMonitor<BearerTokenOptions> options, IClock clock) : IAccessTokenIssuer
{
    /// <inheritdoc />
    public IssuedAccessToken Issue(AccessTokenClaims claims)
    {
        ArgumentNullException.ThrowIfNull(claims);

        BearerTokenOptions bearer = options.Get(IdentityConstants.BearerScheme);

        // Name claim = sub; role claim type is the default ClaimTypes.Role, which "role" deliberately is
        // NOT — so IsInRole() can never be satisfied from a token claim.
        var identity = new ClaimsIdentity(IdentityConstants.BearerScheme, AuthClaimTypes.Sub, ClaimTypes.Role);
        identity.AddClaim(new Claim(AuthClaimTypes.Sub, claims.UserId));
        identity.AddClaim(new Claim(AuthClaimTypes.Tid, claims.TenantId));
        identity.AddClaim(new Claim(AuthClaimTypes.Did, claims.DeviceId));
        identity.AddClaim(new Claim(AuthClaimTypes.Sst, claims.SecurityStamp));
        identity.AddClaim(new Claim(AuthClaimTypes.Role, claims.Role));

        DateTimeOffset now = clock.UtcNow;
        var properties = new AuthenticationProperties
        {
            IssuedUtc = now,
            ExpiresUtc = now + bearer.BearerTokenExpiration,
        };

        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), properties, IdentityConstants.BearerScheme);
        return new IssuedAccessToken(bearer.BearerTokenProtector.Protect(ticket), (int)bearer.BearerTokenExpiration.TotalSeconds);
    }
}
