using Animora.Persistence.PostgreSql.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace Animora.Persistence.PostgreSql.Configurations;

public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> b)
    {
        b.ToTable("ServerOutbox", "public"); b.HasKey(x => new { x.TenantId, x.Id });
        b.Property(x => x.TenantId).HasMaxLength(26); b.Property(x => x.Id).HasMaxLength(26);
        b.Property(x => x.MessageType).HasMaxLength(64).IsRequired(); b.Property(x => x.PayloadJson).HasColumnType("jsonb").IsRequired();
        b.Property(x => x.Status).HasConversion<short>().HasColumnType("smallint");
        b.Property(x => x.ClaimToken).HasMaxLength(26); b.Property(x => x.LastError).HasMaxLength(512);
        b.Property(x => x.CorrelationId).HasMaxLength(64).IsRequired();
        b.Property(x => x.CreatedUtc).HasColumnType("timestamp(6) with time zone");
        b.Property(x => x.NextAttemptUtc).HasColumnType("timestamp(6) with time zone");
        b.Property(x => x.ClaimedUntilUtc).HasColumnType("timestamp(6) with time zone");
        b.Property(x => x.CompletedUtc).HasColumnType("timestamp(6) with time zone");
        // Sole documented global-index exception: relay distributes work across tenants.
        b.HasIndex(x => new { x.Status, x.NextAttemptUtc }).HasFilter("\"Status\" IN (0,1)")
            .HasDatabaseName("IX_ServerOutbox_Status_NextAttemptUtc");
        b.HasIndex(x => new { x.TenantId, x.CreatedUtc });
    }
}
