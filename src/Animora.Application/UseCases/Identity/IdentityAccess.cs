using Animora.Application.Ports;
using Animora.Domain.Identity;

namespace Animora.Application.UseCases.Identity;

/// <summary>Resolves a membership's effective permission codes (data only — B04 decides, A-RULE 023).</summary>
internal static class IdentityAccess
{
    public static async Task<IReadOnlyList<string>> PermissionsAsync(IRoleDefinitionRepository roles,
        StaffMembership membership, CancellationToken ct)
    {
        var stored = await roles.FindByRoleAsync(membership.TenantId, membership.Role, ct).ConfigureAwait(false);
        var codes = new SortedSet<string>(stored?.PermissionCodes ?? DefaultRolePermissions.For(membership.Role),
            StringComparer.Ordinal);
        codes.UnionWith(membership.ExtraPermissionCodes);
        codes.ExceptWith(membership.DeniedPermissionCodes); // denies win (fail closed)
        return codes.ToArray();
    }
}
