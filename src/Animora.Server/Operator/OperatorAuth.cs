using System.Security.Claims;
using Animora.Persistence.PostgreSql.Operator;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.Extensions.Options;

namespace Animora.Server.Operator;

internal static class OperatorAuth
{
    public const string Scheme = "Animora.Operator";
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);
    public static object Issue(OperatorAccount account, DateTimeOffset now, IOptionsMonitor<BearerTokenOptions> options)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim("sub", account.Id), new Claim("ost", account.SecurityStamp), new Claim(ClaimTypes.Role, "operator"),
        ], Scheme));
        var ticket = new AuthenticationTicket(principal, new AuthenticationProperties
        { IssuedUtc = now, ExpiresUtc = now + Lifetime }, Scheme);
        return new { tokenType = "Bearer", accessToken = options.Get(Scheme).BearerTokenProtector.Protect(ticket),
            expiresIn = (int)Lifetime.TotalSeconds, account.MustChangePassword };
        // No refresh token is issued or accepted by this plane.
    }
}
