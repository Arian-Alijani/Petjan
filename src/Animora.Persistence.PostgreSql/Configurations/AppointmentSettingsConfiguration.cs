using Animora.Persistence.PostgreSql.Scheduling;
using Animora.Persistence.PostgreSql.Conventions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace Animora.Persistence.PostgreSql.Configurations;

public sealed class AppointmentSettingsConfiguration : IEntityTypeConfiguration<AppointmentSettingsEntity>
{
    public void Configure(EntityTypeBuilder<AppointmentSettingsEntity> builder)
    {
        builder.ToTable("AppointmentSettings", "public");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.SettingsJson).HasColumnType("jsonb").IsRequired();
        ServerModelConventions.TenantIndex(builder).IsUnique();
    }
}
