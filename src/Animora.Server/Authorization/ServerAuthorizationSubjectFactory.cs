using System.Collections.Frozen;
using Animora.Application.Ports;
using Animora.Application.Security;
using Animora.Domain.Identity;

namespace Animora.Server.Authorization;

/// <summary>
/// Builds the <see cref="AuthorizationSubject"/> of a request purely from <b>stored</b> facts:
/// <c>grants = RoleDefinition.PermissionCodes ∪ membership.Extra − membership.Denied</c> (ordinal; deny wins).
/// The principal's <c>role</c> claim is never read. The <see cref="IPermissionEvaluator"/> recomputes the same
/// grants from the store on every evaluation, so the set carried here is advisory input only.
/// </summary>
internal sealed class ServerAuthorizationSubjectFactory(
    IStaffMembershipRepository memberships,
    IRoleDefinitionRepository roles)
{
    /// <summary>Builds the subject for an accepted request.</summary>
    public async Task<AuthorizationSubject> BuildAsync(ResolvedMembership resolved, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(resolved);

        IReadOnlySet<string> grants = await GrantsAsync(resolved.TenantId, resolved.UserId, resolved.Role, ct).ConfigureAwait(false);
        return new AuthorizationSubject(
            resolved.TenantId,
            resolved.UserId,
            grants,
            resolved.Role,
            resolved.DeviceId,
            IsOffline: false);
    }

    /// <summary>The effective stored grants of a user (empty when no active membership / role definition exists).</summary>
    public async Task<IReadOnlySet<string>> GrantsAsync(string tenantId, string userId, StaffRole role, CancellationToken ct = default)
    {
        StaffMembership? membership = await memberships.FindActiveAsync(tenantId, userId, ct).ConfigureAwait(false);
        RoleDefinition? definition = membership is null
            ? null
            : await roles.FindByRoleAsync(tenantId, membership.Role, ct).ConfigureAwait(false);

        if (membership is null || definition is null || membership.Role != role)
        {
            return FrozenSet<string>.Empty;
        }

        return ComputeGrants(definition.PermissionCodes, membership.ExtraPermissionCodes, membership.DeniedPermissionCodes);
    }

    /// <summary>Role ∪ extra − denied, ordinal. Pure, so the "deny wins" rule is directly testable.</summary>
    public static IReadOnlySet<string> ComputeGrants(
        IEnumerable<string> role,
        IEnumerable<string> extra,
        IEnumerable<string> denied)
    {
        ArgumentNullException.ThrowIfNull(role);
        ArgumentNullException.ThrowIfNull(extra);
        ArgumentNullException.ThrowIfNull(denied);

        var grants = new HashSet<string>(role, StringComparer.Ordinal);
        grants.UnionWith(extra);
        grants.ExceptWith(denied);
        return grants.ToFrozenSet(StringComparer.Ordinal);
    }
}
