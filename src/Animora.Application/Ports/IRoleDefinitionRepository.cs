using Animora.Domain.Identity;

namespace Animora.Application.Ports;

public interface IRoleDefinitionRepository
{
    /// <summary>The tenant's stored definition for <paramref name="role"/>, if seeded.</summary>
    Task<RoleDefinition?> FindByRoleAsync(string tenantId, StaffRole role, CancellationToken ct = default);

    /// <summary>
    /// Every seeded role definition of the tenant keyed by role (SCALE-N1-01). The default implementation falls back to
    /// one <see cref="FindByRoleAsync"/> per role so existing implementers keep working unchanged; set-based stores
    /// (the PostgreSQL server repository) override it with a single query.
    /// </summary>
    async Task<IReadOnlyDictionary<StaffRole, RoleDefinition>> ListByTenantAsync(string tenantId, CancellationToken ct = default)
    {
        var all = new Dictionary<StaffRole, RoleDefinition>();
        foreach (StaffRole role in Enum.GetValues<StaffRole>())
        {
            RoleDefinition? stored = await FindByRoleAsync(tenantId, role, ct).ConfigureAwait(false);
            if (stored is not null)
            {
                all[role] = stored;
            }
        }

        return all;
    }
}
