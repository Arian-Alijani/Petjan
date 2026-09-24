using Animora.Application.Ports;
using Animora.Domain.Identity;
using Animora.Persistence.Sqlite.Context;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.Sqlite.Repositories;

public sealed class RoleDefinitionRepository(AnimoraLocalDbContext db) : IRoleDefinitionRepository
{
    public Task<RoleDefinition?> FindByRoleAsync(string tenantId, StaffRole role, CancellationToken ct = default) =>
        db.Set<RoleDefinition>().FirstOrDefaultAsync(e => e.TenantId == tenantId && e.Role == role, ct);
}
