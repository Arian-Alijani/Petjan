using Animora.Domain.Boarding;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Animora.Persistence.PostgreSql.Configurations;

public sealed class BoardingServiceDefConfiguration : IEntityTypeConfiguration<BoardingServiceDef>
{
    public void Configure(EntityTypeBuilder<BoardingServiceDef> builder)
    {
        builder.ToTable("BoardingServiceDefs", "public");
        builder.HasKey(e => e.Id);
        Animora.Persistence.PostgreSql.Conventions.OwnedRows.Money(builder, e => e.UnitPrice);
        builder.HasIndex(e => new { e.TenantId, e.Code }).IsUnique();
    }
}
