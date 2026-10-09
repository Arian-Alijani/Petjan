using Animora.Domain.Identity;
using Animora.Persistence.PostgreSql.Conventions;
using Animora.Persistence.PostgreSql.Identity;
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

    /// <summary>Tenant staff accounts (S04).</summary>
    public DbSet<User> Users => Set<User>();

    /// <summary>User ↔ tenant staff memberships carrying the role (S04).</summary>
    public DbSet<StaffMembership> StaffMemberships => Set<StaffMembership>();

    /// <summary>Per-tenant role → permission definitions (S04).</summary>
    public DbSet<RoleDefinition> RoleDefinitions => Set<RoleDefinition>();

    /// <summary>Registered client installations (S04).</summary>
    public DbSet<Device> Devices => Set<Device>();

    /// <summary>Server-issued refresh tokens (S04; hash only).</summary>
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    /// <summary>Append-only security audit trail (S04).</summary>
    public DbSet<SecurityAuditEntry> SecurityAudit => Set<SecurityAuditEntry>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AnimoraServerDbContext).Assembly);
        ServerModelConventions.ApplyTenancy(modelBuilder, () => CurrentTenantId);

        // RefreshToken / SecurityAuditEntry are not SyncableEntity, so the convention skips them: scope
        // them to the tenant in context here (accessor stays rooted on this context instance).
        modelBuilder.Entity<RefreshToken>()
            .HasQueryFilter(ServerModelConventions.TenantFilterName, e => e.TenantId == CurrentTenantId);
        modelBuilder.Entity<SecurityAuditEntry>()
            .HasQueryFilter(ServerModelConventions.TenantFilterName, e => e.TenantId == CurrentTenantId);
    }
}
