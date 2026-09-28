using Animora.Persistence.Sqlite.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Animora.Persistence.Sqlite.Configurations.Clinical;

public sealed class VisitBillingHandoffConfiguration : IEntityTypeConfiguration<VisitBillingHandoff>
{
    public void Configure(EntityTypeBuilder<VisitBillingHandoff> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.LinesJson).HasMaxLength(VisitBillingHandoff.LinesJsonMaxLength).IsRequired();
        builder.HasIndex(e => new { e.TenantId, e.Status, e.CompletedUtc })
            .HasDatabaseName("IX_VisitBillingHandoffs_TenantId_Status_CompletedUtc");
        builder.HasIndex(e => e.VisitId).IsUnique()
            .HasDatabaseName("IX_VisitBillingHandoffs_VisitId");
    }
}
