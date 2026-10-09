using Animora.Domain.Patients;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Animora.Persistence.PostgreSql.Configurations;

public sealed class WeightEntryConfiguration : IEntityTypeConfiguration<WeightEntry>
{
    public void Configure(EntityTypeBuilder<WeightEntry> builder)
    {
        builder.ToTable("WeightEntries", "public");
        builder.HasKey(e => e.Id);

        // The Patients directory projects each pet's latest weight with a correlated subquery that
        // filters by PetId and orders by MeasuredUtc DESC (PatientDirectoryQueries.GetPetsAsync).
        // This composite turns that per-row subquery into an index seek + top-1 instead of a scan.
        builder.HasIndex(e => new { e.TenantId, e.PetId, e.MeasuredUtc })
            .HasDatabaseName("IX_WeightEntries_TenantId_PetId_MeasuredUtc");
    }
}
