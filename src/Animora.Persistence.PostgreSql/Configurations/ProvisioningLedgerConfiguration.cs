using Animora.Persistence.PostgreSql.Subscriptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Animora.Persistence.PostgreSql.Configurations;

public sealed class ProvisioningLedgerConfiguration : IEntityTypeConfiguration<ProvisioningLedger>
{
    public void Configure(EntityTypeBuilder<ProvisioningLedger> b)
    {
        b.ToTable("ProvisioningLedger", "public"); b.HasKey(x => x.IdempotencyKey);
        b.Property(x => x.IdempotencyKey).HasMaxLength(128); b.Property(x => x.RequestFingerprint).HasColumnType("character(64)").IsRequired();
        b.Property(x => x.TenantId).HasMaxLength(26).IsRequired(); b.Property(x => x.SubscriptionId).HasMaxLength(26).IsRequired();
        b.Property(x => x.ActorKind).HasConversion<short>(); b.Property(x => x.CreatedUtc).HasColumnType("timestamp(6) with time zone");
    }
}
