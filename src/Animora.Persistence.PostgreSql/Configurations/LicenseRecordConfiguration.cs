using Animora.Persistence.PostgreSql.Licensing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace Animora.Persistence.PostgreSql.Configurations;

public sealed class LicenseRecordConfiguration : IEntityTypeConfiguration<LicenseRecord>
{
    public void Configure(EntityTypeBuilder<LicenseRecord> b)
    {
        b.ToTable("Licenses", "public"); b.HasKey(x => new { x.TenantId, x.Id });
        b.Property(x => x.TenantId).HasMaxLength(26); b.Property(x => x.Id).HasMaxLength(26);
        b.Property(x => x.DeviceId).HasMaxLength(26).IsRequired(); b.Property(x => x.KeyId).HasMaxLength(26).IsRequired();
        b.Property(x => x.InstallationIdHash).HasColumnType("character(64)").IsRequired();
        b.Property(x => x.PayloadSha256).HasColumnType("character(64)").IsRequired();
        b.Property(x => x.Status).HasConversion<short>();
        b.Property(x => x.RevokedByActor).HasMaxLength(26); b.Property(x => x.RevokeReason).HasMaxLength(200);
        b.Property(x => x.IssuedUtc).HasPrecision(6); b.Property(x => x.ExpiresUtc).HasPrecision(6);
        b.Property(x => x.OfflineGraceUntilUtc).HasPrecision(6); b.Property(x => x.RevokedUtc).HasPrecision(6);
        b.HasIndex(x => new { x.TenantId, x.DeviceId }).IsUnique().HasFilter("\"Status\"=0")
            .HasDatabaseName("UX_Licenses_Tenant_Device_Active");
        b.HasIndex(x => new { x.TenantId, x.Status }).HasDatabaseName("IX_Licenses_TenantId_Status");
    }
}
