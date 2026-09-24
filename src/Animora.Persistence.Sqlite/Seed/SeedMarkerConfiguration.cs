using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Animora.Persistence.Sqlite.Seed;

public sealed class SeedMarkerConfiguration : IEntityTypeConfiguration<SeedMarker>
{
    public void Configure(EntityTypeBuilder<SeedMarker> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.SeedKey).HasMaxLength(64);
        builder.HasIndex(e => e.SeedKey).HasDatabaseName("IX_SeedMarkers_SeedKey").IsUnique();
    }
}
