using Animora.Domain.Identity;
using Animora.Persistence.PostgreSql.Conventions;
using Animora.Persistence.PostgreSql.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.PostgreSql.Context;

/// <summary>
/// The server's main OLTP context. Tenancy is enforced three ways: named query filters
/// (<see cref="CurrentTenantId"/>), the stamping interceptor on save, and the
/// unnamed-<c>IgnoreQueryFilters</c> ban. The running role has no DDL rights; schema changes
/// ship only as migrations.
/// </summary>
public sealed class AnimoraServerDbContext(DbContextOptions<AnimoraServerDbContext> options, ITenantContext tenantContext)
    : DbContext(options)
{
    /// <summary>
    /// Filter accessor re-bound by EF per query. Throws when no tenant is bound so a missing
    /// tenant can never degrade into a cross-tenant read.
    /// </summary>
    public string CurrentTenantId => tenantContext.TenantId ?? throw new TenantContextMissingException();

    /// <summary>Clinics (tenants).</summary>
    public DbSet<Tenant> Tenants => Set<Tenant>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AnimoraServerDbContext).Assembly);
        ServerModelConventions.ApplyTenancy(modelBuilder, () => CurrentTenantId);
    }
}
