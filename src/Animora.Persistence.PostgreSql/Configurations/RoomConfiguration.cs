using Animora.Domain.Boarding;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Animora.Persistence.PostgreSql.Configurations;

public sealed class RoomConfiguration : IEntityTypeConfiguration<Room>
{
    public void Configure(EntityTypeBuilder<Room> builder)
    {
        builder.ToTable("Rooms", "public");
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => new { e.TenantId, e.Code }).HasDatabaseName("IX_Rooms_TenantId_Code").IsUnique();
    }
}
