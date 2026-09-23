using Animora.Domain.Patients;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Animora.Persistence.Sqlite.Configurations.Patients;

public sealed class WeightEntryConfiguration : IEntityTypeConfiguration<WeightEntry>
{
    public void Configure(EntityTypeBuilder<WeightEntry> builder)
    {
        builder.HasKey(e => e.Id);
    }
}
