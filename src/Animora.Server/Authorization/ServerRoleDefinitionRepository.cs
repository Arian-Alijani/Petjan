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
}
