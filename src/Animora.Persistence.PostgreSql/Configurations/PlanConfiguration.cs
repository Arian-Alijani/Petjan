using Animora.Domain.Subscriptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Animora.Persistence.PostgreSql.Configurations;

public sealed class PlanConfiguration : IEntityTypeConfiguration<Plan>
{
    public void Configure(EntityTypeBuilder<Plan> b)
    {
        b.ToTable("Plans", "public"); b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasMaxLength(26); b.Property(x => x.Code).HasMaxLength(32).IsRequired();
        b.HasIndex(x => x.Code).IsUnique(); b.Property(x => x.Name).HasMaxLength(100).IsRequired();
        b.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        b.Property(x => x.CreatedUtc).HasColumnType("timestamp(6) with time zone");
    }
}
