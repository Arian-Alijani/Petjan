using Animora.Persistence.PostgreSql.Clinical;
using Animora.Persistence.PostgreSql.Conventions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Animora.Persistence.PostgreSql.Configurations;

public sealed class VisitBillingHandoffConfiguration : IEntityTypeConfiguration<VisitBillingHandoff>
{
    public void Configure(EntityTypeBuilder<VisitBillingHandoff> builder)
    {
        builder.ToTable("VisitHandoffLines", "public");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.LinesJson).HasColumnType("jsonb").IsRequired();
        ServerModelConventions.TenantIndex(builder, nameof(VisitBillingHandoff.VisitId)).IsUnique();
        ServerModelConventions.TenantIndex(builder, nameof(VisitBillingHandoff.Status), nameof(VisitBillingHandoff.CompletedUtc));
    }
}
