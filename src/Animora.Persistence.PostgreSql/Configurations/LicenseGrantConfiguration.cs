using Animora.Persistence.PostgreSql.Licensing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace Animora.Persistence.PostgreSql.Configurations;

public sealed class LicenseGrantConfiguration : IEntityTypeConfiguration<LicenseGrant>
{
    public void Configure(EntityTypeBuilder<LicenseGrant> b)
    {
        b.ToTable("LicenseGrants", "public"); b.HasKey(x => x.TenantId);
        b.Property(x => x.TenantId).HasMaxLength(26);
        b.Property(x => x.SubscriptionId).HasMaxLength(26).IsRequired();
        b.Property(x => x.PlanCode).HasMaxLength(32).IsRequired();
        b.Property(x => x.Status).HasConversion<short>();
        b.Property(x => x.ValidUntilUtc).HasPrecision(6);
        b.Property(x => x.UpdatedUtc).HasPrecision(6);
    }
}
