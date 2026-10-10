using Animora.Persistence.PostgreSql.Replication;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace Animora.Persistence.PostgreSql.Configurations;

public sealed class TenantFeedStateConfiguration : IEntityTypeConfiguration<TenantFeedStateRow>
{
    public void Configure(EntityTypeBuilder<TenantFeedStateRow> b)
    {
        b.ToTable("TenantFeedState", "public");
        b.HasKey(x => x.TenantId);
        b.Property(x => x.TenantId).HasMaxLength(26);
        b.Property(x => x.Epoch).HasDefaultValue(1L);
        b.Property(x => x.Generation).HasDefaultValue(1L);
        b.Property(x => x.LastSequence).HasDefaultValue(0L);
        b.Property(x => x.OldestRetainedSequence).HasDefaultValue(0L);
        b.Property(x => x.UpdatedUtc).HasColumnType("timestamp(6) with time zone").HasDefaultValueSql("clock_timestamp()");
    }
}
