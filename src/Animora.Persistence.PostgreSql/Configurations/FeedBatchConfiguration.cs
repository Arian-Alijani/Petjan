using Animora.Persistence.PostgreSql.Replication;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace Animora.Persistence.PostgreSql.Configurations;

public sealed class FeedBatchConfiguration : IEntityTypeConfiguration<FeedBatchRow>
{
    public void Configure(EntityTypeBuilder<FeedBatchRow> b)
    {
        b.ToTable("FeedBatch", "public"); b.HasKey(x => new { x.TenantId, x.BatchId });
        b.Property(x => x.TenantId).HasMaxLength(26); b.Property(x => x.BatchId).HasMaxLength(26);
        b.Property(x => x.State).HasConversion<short>().HasColumnType("smallint");
        b.Property(x => x.ContentHash).HasColumnType("character(64)").IsRequired();
        b.Property(x => x.CreatedUtc).HasColumnType("timestamp(6) with time zone");
        b.Property(x => x.RetiredUtc).HasColumnType("timestamp(6) with time zone");
        b.HasIndex(x => new { x.TenantId, x.Epoch, x.FromSequenceExclusive, x.Watermark, x.IsFullSnapshot }).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.State, x.CreatedUtc });
    }
}
