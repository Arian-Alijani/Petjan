using Animora.Persistence.PostgreSql.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Animora.Persistence.PostgreSql.Configurations;

public sealed class DocumentCounterConfiguration : IEntityTypeConfiguration<DocumentCounter>
{
    public void Configure(EntityTypeBuilder<DocumentCounter> builder)
    {
        builder.ToTable("DocumentCounters", "public");
        builder.HasKey(e => new { e.TenantId, e.Kind, e.Period });
        builder.Property(e => e.TenantId).HasMaxLength(26);
        builder.Property(e => e.Kind).HasMaxLength(32);
        builder.Property(e => e.Period).HasMaxLength(32);
        builder.Property(e => e.Next).HasColumnType("bigint");
    }
}
