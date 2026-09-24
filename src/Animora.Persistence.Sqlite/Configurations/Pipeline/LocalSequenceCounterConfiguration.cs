using Animora.Persistence.Sqlite.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Animora.Persistence.Sqlite.Configurations.Pipeline;

public sealed class LocalSequenceCounterConfiguration : IEntityTypeConfiguration<LocalSequenceCounter>
{
    public void Configure(EntityTypeBuilder<LocalSequenceCounter> builder)
    {
        builder.HasKey(e => e.Name);
        builder.Property(e => e.Name).ValueGeneratedNever();
    }
}
