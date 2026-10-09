using Microsoft.AspNetCore.DataProtection;
using System.Security.Claims;
using Animora.Server.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Animora.Server.Tests.Identity;

public sealed class TokenClaimsTests
{
    private static (BearerAccessTokenIssuer Issuer, BearerTokenOptions Options) Create()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection().SetApplicationName("Animora.Server.Tests");
        services.AddAuthentication(IdentityConstants.BearerScheme).AddBearerToken(IdentityConstants.BearerScheme, o => o.BearerTokenExpiration = AuthServiceCollectionExtensions.AccessTokenLifetime);
        ServiceProvider sp = services.BuildServiceProvider();

        var monitor = sp.GetRequiredService<IOptionsMonitor<BearerTokenOptions>>();
        var clock = new TestClock(new DateTimeOffset(2026, 10, 9, 8, 0, 0, TimeSpan.Zero));
        return (new BearerAccessTokenIssuer(monitor, clock), monitor.Get(IdentityConstants.BearerScheme));
    }

    [Fact]
    public void AccessToken_IsOpaque_NotJwt_AndCarriesIdentificationClaims()
    {
        (BearerAccessTokenIssuer issuer, BearerTokenOptions options) = Create();

        IssuedAccessToken token = issuer.Issue(new AccessTokenClaims("U1", "T1", "D1", "STAMP1", "Manager"));

        Assert.Equal(900, token.ExpiresInSeconds);
        Assert.DoesNotContain('.', token.Token); // a JWT always has three dot-separated segments

        AuthenticationTicket? ticket = options.BearerTokenProtector.Unprotect(token.Token);
        Assert.NotNull(ticket);
        ClaimsPrincipal principal = ticket.Principal;
        Assert.Equal("U1", principal.FindFirstValue(AuthClaimTypes.Sub));
        Assert.Equal("T1", principal.FindFirstValue(AuthClaimTypes.Tid));
        Assert.Equal("D1", principal.FindFirstValue(AuthClaimTypes.Did));
        Assert.Equal("STAMP1", principal.FindFirstValue(AuthClaimTypes.Sst));
        Assert.Equal("Manager", principal.FindFirstValue(AuthClaimTypes.Role));

        AuthenticatedCaller? caller = AuthenticatedCaller.TryRead(principal);
        Assert.Equal(new AuthenticatedCaller("U1", "T1", "D1", "STAMP1"), caller);
    }

    [Fact]
    public void NoRoleTrustedFromClaim_Marker()
    {
        (BearerAccessTokenIssuer issuer, BearerTokenOptions options) = Create();
        IssuedAccessToken token = issuer.Issue(new AccessTokenClaims("U1", "T1", "D1", "STAMP1", "Manager"));
        ClaimsPrincipal principal = options.BearerTokenProtector.Unprotect(token.Token)!.Principal;

        // The "role" claim is informational: it is not the framework role claim type, so IsInRole can
        // never be satisfied from a token. S05 re-reads the stored membership for every decision.
        Assert.False(principal.IsInRole("Manager"));
        Assert.Equal("role", AuthClaimTypes.Role);
        Assert.NotEqual(ClaimTypes.Role, AuthClaimTypes.Role);
        Assert.Equal(
            ["UserId", "TenantId", "DeviceId", "SecurityStamp"],
            typeof(AuthenticatedCaller).GetProperties().Where(p => p.Name != "EqualityContract").Select(p => p.Name).ToArray());
    }

    [Fact]
    public void Caller_IsNull_ForAnonymous_OrIncompleteClaims()
    {
        Assert.Null(AuthenticatedCaller.TryRead(null));
        Assert.Null(AuthenticatedCaller.TryRead(new ClaimsPrincipal(new ClaimsIdentity())));
        var partial = new ClaimsIdentity([new Claim(AuthClaimTypes.Sub, "U1")], "test");
        Assert.Null(AuthenticatedCaller.TryRead(new ClaimsPrincipal(partial)));
    }
}
