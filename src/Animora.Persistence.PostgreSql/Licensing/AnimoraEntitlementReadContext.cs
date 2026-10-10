using Animora.Persistence.PostgreSql.Configurations;
using Microsoft.EntityFrameworkCore;
namespace Animora.Persistence.PostgreSql.Licensing;

/// <summary>Only the licensing gate may resolve this unfiltered, explicit-tenant read model.</summary>
public sealed class AnimoraEntitlementReadContext(DbContextOptions<AnimoraEntitlementReadContext> options) : DbContext(options)
{
    public DbSet<LicenseGrant> Grants => Set<LicenseGrant>();
    public DbSet<LicenseRecord> Licenses => Set<LicenseRecord>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        b.ApplyConfiguration(new LicenseGrantConfiguration()); b.ApplyConfiguration(new LicenseRecordConfiguration());
    }
    public override int SaveChanges(bool acceptAllChangesOnSuccess) => throw new InvalidOperationException("Read-only context.");
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken ct = default) =>
        throw new InvalidOperationException("Read-only context.");
}
