using Animora.Domain.Identity;

namespace Animora.Application.Ports;

public interface IRoleDefinitionRepository
{
    /// <summary>The tenant's stored definition for <paramref name="role"/>, if seeded.</summary>
    Task<RoleDefinition?> FindByRoleAsync(string tenantId, StaffRole role, CancellationToken ct = default);
}
