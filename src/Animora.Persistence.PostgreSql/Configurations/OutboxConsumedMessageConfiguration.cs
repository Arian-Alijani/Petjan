using Animora.Persistence.PostgreSql.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace Animora.Persistence.PostgreSql.Configurations;

public sealed class OutboxConsumedMessageConfiguration : IEntityTypeConfiguration<ConsumedMessage>
{
    public void Configure(EntityTypeBuilder<ConsumedMessage> b)
    {
        b.ToTable("ConsumedMessages", "public"); b.HasKey(x => new { x.ConsumerName, x.TenantId, x.MessageId });
        b.Property(x => x.ConsumerName).HasMaxLength(48); b.Property(x => x.TenantId).HasMaxLength(26);
        b.Property(x => x.MessageId).HasMaxLength(26); b.Property(x => x.ConsumedUtc).HasColumnType("timestamp(6) with time zone");
    }
}
