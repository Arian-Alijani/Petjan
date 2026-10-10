using Animora.Domain.Subscriptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Animora.Persistence.PostgreSql.Configurations;

public sealed class BillingPeriodConfiguration : IEntityTypeConfiguration<BillingPeriod>
{
    public void Configure(EntityTypeBuilder<BillingPeriod> b)
    {
        b.ToTable("BillingPeriods", "public"); b.HasKey(x => new { x.SubscriptionId, x.Sequence });
        b.Property(x => x.SubscriptionId).HasMaxLength(26); b.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        b.Property(x => x.StartUtc).HasColumnType("timestamp(6) with time zone");
        b.Property(x => x.EndUtc).HasColumnType("timestamp(6) with time zone");
    }
}
