using System.Security.Claims;
using Animora.Persistence.PostgreSql.Operator;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
namespace Animora.Server.Admin;
/// <summary>Long-lived islands revalidate revoked stamps and absolute session expiry; failures close access.</summary>
internal sealed class OperatorAuthenticationStateProvider(ILoggerFactory logger, IServiceScopeFactory scopes, TimeProvider time)
    : RevalidatingServerAuthenticationStateProvider(logger)
{
    protected override TimeSpan RevalidationInterval => TimeSpan.FromMinutes(1);
    protected override async Task<bool> ValidateAuthenticationStateAsync(AuthenticationState state, CancellationToken cancellationToken)
    {
        var user = state.User;
        if (!user.Identities.Any(i => i.IsAuthenticated && i.AuthenticationType == OperatorCookie.Scheme) || !user.IsInRole("operator") ||
            !long.TryParse(user.FindFirstValue("op_exp"), out var expiry) || time.GetUtcNow().ToUnixTimeSeconds() >= expiry) return false;
        var id = user.FindFirstValue("sub"); var stamp = user.FindFirstValue("ost");
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(stamp)) return false;
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<OperatorReadQueries>().SessionValidAsync(id, stamp, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return false; } // Fail closed without leaking provider exception/connection text into logs.
    }
}
