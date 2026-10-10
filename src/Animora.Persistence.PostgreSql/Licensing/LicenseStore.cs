using Animora.Domain.Identity;
using Animora.Persistence.PostgreSql.Configurations;
using Animora.Persistence.PostgreSql.Conventions;
using Animora.Persistence.PostgreSql.Identity;
using Animora.Persistence.PostgreSql.Operator;
using Animora.Persistence.PostgreSql.Security;
using Animora.Persistence.PostgreSql.Outbox;
using Microsoft.EntityFrameworkCore;
namespace Animora.Persistence.PostgreSql.Licensing;

/// <summary>Licensing-only writer. Every tenant read/write carries an explicit tenant predicate.</summary>
public sealed class LicenseStore(DbContextOptions<LicenseStore> options) : DbContext(options)
{
    public DbSet<LicenseGrant> Grants => Set<LicenseGrant>();
    public DbSet<LicenseRecord> Licenses => Set<LicenseRecord>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<SigningKey> Keys => Set<SigningKey>();
    public DbSet<SecurityAuditEntry> SecurityAudit => Set<SecurityAuditEntry>();
    public DbSet<OperatorAudit> Audit => Set<OperatorAudit>();
    public DbSet<OutboxMessage> Messages => Set<OutboxMessage>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        b.ApplyConfiguration(new LicenseGrantConfiguration()); b.ApplyConfiguration(new LicenseRecordConfiguration());
        b.ApplyConfiguration(new DeviceConfiguration()); b.ApplyConfiguration(new TenantConfiguration());
        b.ApplyConfiguration(new SigningKeyConfiguration()); b.ApplyConfiguration(new SecurityAuditConfiguration());
        b.ApplyConfiguration(new OperatorAuditConfiguration()); b.ApplyConfiguration(new OutboxMessageConfiguration());
        ServerModelConventions.ApplyColumnsOnly(b);
    }
    private void GuardWrites()
    {
        ChangeTracker.DetectChanges();
        foreach (var e in ChangeTracker.Entries())
        {
            if (e.State is EntityState.Unchanged or EntityState.Detached) continue;
            if (e.Entity is Device or Tenant || e.Entity is SecurityAuditEntry or OperatorAudit && e.State != EntityState.Added)
                throw new InvalidOperationException("Licensing cannot mutate identity or audit history.");
        }
    }
    public override int SaveChanges(bool acceptAllChangesOnSuccess) { GuardWrites(); return base.SaveChanges(acceptAllChangesOnSuccess); }
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken ct = default)
    { GuardWrites(); return base.SaveChangesAsync(acceptAllChangesOnSuccess, ct); }
}
