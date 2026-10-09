using System.Collections.Frozen;
using Animora.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Animora.Server.Authorization;

/// <summary>
/// Resolves a policy whose name is a <c>PermissionCodes.*</c> value to a single <see cref="PermissionRequirement"/>
/// built on demand (so <c>RequireAuthorization(PermissionCodes.DevicesManage)</c> just works). Everything else — the
/// coarse named policies and framework policies — falls back to the default provider. An unknown name yields no
/// policy (the framework then fails the request closed).
/// </summary>
internal sealed class ServerPermissionPolicyProvider(IOptions<AuthorizationOptions> options) : IAuthorizationPolicyProvider
{
    private static readonly FrozenSet<string> Known = PermissionCodes.All.ToFrozenSet(StringComparer.Ordinal);

    private readonly DefaultAuthorizationPolicyProvider _fallback = new(options);

    /// <inheritdoc />
    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _fallback.GetDefaultPolicyAsync();

    /// <inheritdoc />
    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _fallback.GetFallbackPolicyAsync();

    /// <inheritdoc />
    public async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (policyName is not null && Known.Contains(policyName))
        {
            return new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new PermissionRequirement(policyName))
                .Build();
        }

        return await _fallback.GetPolicyAsync(policyName!).ConfigureAwait(false);
    }
}
