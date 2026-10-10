using Animora.Domain.Identity;
using Animora.Domain.Primitives;
using Animora.Domain.Subscriptions;
using Animora.Persistence.PostgreSql.Configurations;
using Animora.Persistence.PostgreSql.Conventions;
using Animora.Persistence.PostgreSql.Outbox;
using Animora.Persistence.PostgreSql.Subscriptions;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.PostgreSql.Operator;

/// <summary>Narrow vendor-global context. No tenant filters; never owns migrations.</summary>
public sealed class AnimoraOperatorDbContext(DbContextOptions<AnimoraOperatorDbContext> options) : DbContext(options)
{
    public DbSet<Animora.Persistence.PostgreSql.Files.FileObject> FileObjects => Set<Animora.Persistence.PostgreSql.Files.FileObject>();
    public DbSet<Animora.Persistence.PostgreSql.Identity.SecurityAuditEntry> SecurityAudit => Set<Animora.Persistence.PostgreSql.Identity.SecurityAuditEntry>();
    public DbSet<Plan> Plans => Set<Plan>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<SubscriptionEvent> SubscriptionEvents => Set<SubscriptionEvent>();
    public DbSet<OperatorAccount> Accounts => Set<OperatorAccount>();
    public DbSet<OperatorAudit> Audit => Set<OperatorAudit>();
    public DbSet<ProvisioningLedger> ProvisioningLedger => Set<ProvisioningLedger>();
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<StaffMembership> StaffMemberships => Set<StaffMembership>();
    public DbSet<OutboxMessage> Messages => Set<OutboxMessage>();

    public DbSet<Device> Devices => Set<Device>();
    public DbSet<Animora.Persistence.PostgreSql.Licensing.LicenseRecord> Licenses => Set<Animora.Persistence.PostgreSql.Licensing.LicenseRecord>();
    public DbSet<Animora.Persistence.PostgreSql.Licensing.LicenseGrant> LicenseGrants => Set<Animora.Persistence.PostgreSql.Licensing.LicenseGrant>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.ApplyConfiguration(new SecurityAuditConfiguration());
        b.ApplyConfiguration(new FileObjectConfiguration());
        b.ApplyConfiguration(new LicenseGrantConfiguration());
        b.ApplyConfiguration(new LicenseRecordConfiguration());
        b.ApplyConfiguration(new DeviceConfiguration());
        b.ApplyConfiguration(new PlanConfiguration());
        b.ApplyConfiguration(new SubscriptionConfiguration());
        b.ApplyConfiguration(new BillingPeriodConfiguration());
        b.ApplyConfiguration(new SubscriptionEventConfiguration());
        b.ApplyConfiguration(new OperatorAccountConfiguration());
        b.ApplyConfiguration(new OperatorAuditConfiguration());
        b.ApplyConfiguration(new ProvisioningLedgerConfiguration());
        b.ApplyConfiguration(new TenantConfiguration());
        b.ApplyConfiguration(new StaffMembershipConfiguration());
        b.ApplyConfiguration(new OutboxMessageConfiguration());
        b.ApplyConfiguration(new WebsiteClientConfiguration());
        b.ApplyConfiguration(new ProvisioningIntentConfiguration());
        ServerModelConventions.ApplyColumnsOnly(b);
    }

    private void EnforceReach()
    {
        ChangeTracker.DetectChanges();
        foreach (var entry in ChangeTracker.Entries<SyncableEntity>())
        {
            if (entry.State is EntityState.Unchanged or EntityState.Detached) continue;
            if (entry.Entity is not Tenant || entry.State != EntityState.Modified ||
                entry.Properties.Any(p => p.IsModified && p.Metadata.Name != nameof(Tenant.Status)))
                throw new InvalidOperationException("Operator context may write only Tenant.Status.");
        }
        foreach (var entry in ChangeTracker.Entries())
            if (entry.Entity is OperatorAudit or SubscriptionEvent or BillingPeriod or Animora.Persistence.PostgreSql.Identity.SecurityAuditEntry &&
                entry.State is EntityState.Modified or EntityState.Deleted)
                throw new InvalidOperationException("Operator history is append-only.");
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnforceReach(); return base.SaveChanges(acceptAllChangesOnSuccess);
    }
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken ct = default)
    {
        EnforceReach(); return base.SaveChangesAsync(acceptAllChangesOnSuccess, ct);
    }
}
