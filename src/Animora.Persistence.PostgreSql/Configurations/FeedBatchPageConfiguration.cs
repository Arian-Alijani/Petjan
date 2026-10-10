using Animora.Persistence.PostgreSql.Replication;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace Animora.Persistence.PostgreSql.Configurations;

public sealed class FeedBatchPageConfiguration : IEntityTypeConfiguration<FeedBatchPageRow>
{
    public void Configure(EntityTypeBuilder<FeedBatchPageRow> b)
    {
        b.ToTable("FeedBatchPage", "public"); b.HasKey(x => new { x.TenantId, x.BatchId, x.PageOrdinal });
        b.Property(x => x.TenantId).HasMaxLength(26); b.Property(x => x.BatchId).HasMaxLength(26);
        b.Property(x => x.PageHash).HasColumnType("character(64)").IsRequired();
    }
}
