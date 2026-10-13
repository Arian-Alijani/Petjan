using Animora.Application.Ports;
using Animora.Domain.Identity;
using Animora.Persistence.PostgreSql.Context;
using Microsoft.EntityFrameworkCore;

namespace Animora.Server.Authorization;

/// <summary>
/// Read-only EF-over-PostgreSQL implementation of <see cref="IRoleDefinitionRepository"/> for the server
/// authorization path (the Sqlite one is in a forbidden project). The tenant filter of
/// <see cref="AnimoraServerDbContext"/> scopes the read to the request-bound tenant.
/// </summary>
internal sealed class ServerRoleDefinitionRepository(AnimoraServerDbContext context) : IRoleDefinitionRepository
{
    /// <inheritdoc />
    public Task<RoleDefinition?> FindByRoleAsync(string tenantId, StaffRole role, CancellationToken ct = default) =>
        context.RoleDefinitions
            .AsNoTracking()
            .Where(r => r.TenantId == tenantId && r.Role == role)
            .FirstOrDefaultAsync(ct);

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<StaffRole, RoleDefinition>> ListByTenantAsync(string tenantId, CancellationToken ct = default)
    {
        // SCALE-N1-01: ONE set-based query for the whole tenant catalogue (was one query per StaffRole value).
        List<RoleDefinition> rows = await context.RoleDefinitions
            .AsNoTracking()
            .Where(r => r.TenantId == tenantId)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        // (TenantId, Role) is unique; first-wins keeps the lookup total even if a bad row ever slipped in.
        var byRole = new Dictionary<StaffRole, RoleDefinition>();
        foreach (RoleDefinition row in rows)
        {
            byRole.TryAdd(row.Role, row);
        }

        return byRole;
    }
}
