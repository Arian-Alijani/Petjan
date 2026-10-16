using Animora.Domain.Clinical;
using Animora.Persistence.PostgreSql.Conventions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Animora.Persistence.PostgreSql.Configurations;

public sealed class VisitConfiguration : IEntityTypeConfiguration<Visit>
{
    public void Configure(EntityTypeBuilder<Visit> builder)
    {
        builder.ToTable("Visits", "public");
        builder.HasKey(e => e.Id);
        ServerModelConventions.TenantIndex(builder, nameof(Visit.PetId), nameof(Visit.StartedUtc));
        ServerModelConventions.TenantIndex(builder, nameof(Visit.State), nameof(Visit.StartedUtc));
        ServerModelConventions.TenantIndex(builder, nameof(Visit.DoctorUserId), nameof(Visit.CompletedUtc));
        builder.Ignore(e => e.AreLinesLocked);
        builder.OwnsOne(e => e.Vitals, v =>
        {
            v.Property(x => x.WeightKg).HasColumnType("numeric");
            v.Property(x => x.TemperatureC).HasColumnType("numeric");
        });
        builder.Navigation(e => e.Vitals).IsRequired(false);
        builder.HasMany(e => e.Prescriptions).WithOne().HasForeignKey("OwnerId").OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(e => e.Prescriptions).HasField("_prescriptions").UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();
        // Service lines have no materialization constructor; keep their immutable snapshot as jsonb.
        builder.Ignore(e => e.ServiceLines);
        JsonFields.Map<Visit, VisitServiceLine>(builder, "_serviceLines", "ServiceLines");
        // Radiology studies + lab result sheets: immutable JSON lists on the visit row (replicated with it).
        builder.Ignore(e => e.Radiology);
        JsonFields.Map<Visit, RadiologyRecord>(builder, "_radiology", "Radiology");
        builder.Ignore(e => e.LabResults);
        JsonFields.Map<Visit, LabResult>(builder, "_labResults", "LabResults");
    }
}

public sealed class PrescriptionLineConfiguration : IEntityTypeConfiguration<PrescriptionLine>
{
    public void Configure(EntityTypeBuilder<PrescriptionLine> builder)
    {
        builder.ToTable("VisitRxLines", "public");
        builder.Property<string>("OwnerId").HasMaxLength(26).HasColumnType("character varying(26)");
        builder.Property(e => e.LineId).HasMaxLength(26).ValueGeneratedNever();
        builder.HasKey("OwnerId", nameof(PrescriptionLine.LineId));
    }
}
