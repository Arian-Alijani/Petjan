using Animora.Domain.Boarding;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Animora.Persistence.Sqlite.Configurations.Boarding;

public sealed class BoardingServiceDefConfiguration : IEntityTypeConfiguration<BoardingServiceDef>
{
    public void Configure(EntityTypeBuilder<BoardingServiceDef> builder)
    {
        builder.HasKey(e => e.Id);
    }
}
