using Animora.Persistence.Sqlite.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Animora.Persistence.Sqlite.Configurations.Boarding;

public sealed class BoardingBillingHandoffConfiguration : IEntityTypeConfiguration<BoardingBillingHandoff>
{
    public void Configure(EntityTypeBuilder<BoardingBillingHandoff> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.LinesJson).HasMaxLength(BoardingBillingHandoff.LinesJsonMaxLength).IsRequired();
        builder.HasIndex(e => new { e.TenantId, e.Status, e.CompletedUtc })
            .HasDatabaseName("IX_BoardingBillingHandoffs_TenantId_Status_CompletedUtc");
        builder.HasIndex(e => e.StayId).IsUnique()
            .HasDatabaseName("IX_BoardingBillingHandoffs_StayId");
    }
}
