using Animora.Persistence.PostgreSql.Provisioning;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace Animora.Persistence.PostgreSql.Configurations;

public sealed class ProvisioningIntentConfiguration : IEntityTypeConfiguration<ProvisioningIntent>
{
    public void Configure(EntityTypeBuilder<ProvisioningIntent> b)
    {
        b.ToTable("ProvisioningIntents", "public"); b.HasKey(x => x.Id); b.Property(x => x.Id).HasMaxLength(26);
        b.Property(x => x.IdempotencyKey).HasMaxLength(128).IsRequired(); b.HasIndex(x => x.IdempotencyKey).IsUnique();
        b.Property(x => x.ClientId).HasMaxLength(26).IsRequired(); b.Property(x => x.RequestFingerprint).HasColumnType("character(64)").IsRequired();
        b.Property(x => x.PayloadProtected).HasColumnType("bytea"); b.Property(x => x.ProtectedTempPassword).HasColumnType("bytea");
        b.Property(x => x.Status).HasConversion<short>(); b.Property(x => x.ClaimToken).HasMaxLength(26); b.Property(x => x.ErrorCode).HasMaxLength(64);
        b.Property(x => x.ResultJson).HasColumnType("jsonb");
        foreach (var name in new[] { "NextAttemptUtc", "ClaimedUntilUtc", "TempPasswordExpiresUtc", "ReceivedUtc", "CompletedUtc" })
            b.Property(name).HasColumnType("timestamp(6) with time zone");
        b.HasIndex(x => new { x.Status, x.NextAttemptUtc }).HasDatabaseName("IX_ProvisioningIntents_Status_NextAttemptUtc");
    }
}
