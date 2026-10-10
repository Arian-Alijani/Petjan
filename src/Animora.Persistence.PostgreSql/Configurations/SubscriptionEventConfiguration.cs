using Animora.Domain.Subscriptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Animora.Persistence.PostgreSql.Configurations;

public sealed class SubscriptionEventConfiguration : IEntityTypeConfiguration<SubscriptionEvent>
{
    public void Configure(EntityTypeBuilder<SubscriptionEvent> b)
    {
        b.ToTable("SubscriptionEvents", "public"); b.HasKey(x => new { x.SubscriptionId, x.Sequence });
        b.Property(x => x.SubscriptionId).HasMaxLength(26); b.Property(x => x.Type).HasMaxLength(48).IsRequired();
        b.Property(x => x.ActorKind).HasConversion<short>(); b.Property(x => x.ActorId).HasMaxLength(26);
        b.Property(x => x.PayloadJson).HasColumnType("jsonb").IsRequired();
        b.Property(x => x.OccurredUtc).HasColumnType("timestamp(6) with time zone");
    }
}
