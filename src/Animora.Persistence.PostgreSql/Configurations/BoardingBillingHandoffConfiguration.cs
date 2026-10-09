using Animora.Persistence.PostgreSql.Boarding;
using Animora.Persistence.PostgreSql.Conventions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace Animora.Persistence.PostgreSql.Configurations;

public sealed class BoardingBillingHandoffConfiguration : IEntityTypeConfiguration<BoardingBillingHandoff>
{
    public void Configure(EntityTypeBuilder<BoardingBillingHandoff> builder)
    {
        builder.ToTable("BoardingHandoffLines", "public");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.LinesJson).HasColumnType("jsonb").IsRequired();
        ServerModelConventions.TenantIndex(builder, nameof(BoardingBillingHandoff.StayId));
        ServerModelConventions.TenantIndex(builder, nameof(BoardingBillingHandoff.Status), nameof(BoardingBillingHandoff.CompletedUtc));
    }
}
