using Animora.Domain.Clinical;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Animora.Persistence.Sqlite.Configurations.Clinical;

public sealed class VisitConfiguration : IEntityTypeConfiguration<Visit>
{
    public void Configure(EntityTypeBuilder<Visit> builder)
    {
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => new { e.TenantId, e.PetId, e.StartedUtc }).HasDatabaseName("IX_Visits_TenantId_PetId_StartedUtc");
        builder.HasIndex(e => new { e.TenantId, e.State, e.StartedUtc }).HasDatabaseName("IX_Visits_TenantId_State_StartedUtc");
        // D23: the doctor's completed-visit count for one clinic day.
        builder.HasIndex(e => new { e.TenantId, e.DoctorUserId, e.CompletedUtc }).HasDatabaseName("IX_Visits_TenantId_DoctorUserId_CompletedUtc");
        builder.OwnsOne(e => e.Vitals, v =>
        {
            v.Property(x => x.WeightKg).HasConversion(new Animora.Persistence.Sqlite.Converters.DecimalStringConverter()).HasColumnType("TEXT");
            v.Property(x => x.TemperatureC).HasConversion(new Animora.Persistence.Sqlite.Converters.DecimalStringConverter()).HasColumnType("TEXT");
        });
        builder.Navigation(e => e.Vitals).IsRequired(false);
        builder.Ignore(e => e.Prescriptions);
        Animora.Persistence.Sqlite.Conventions.JsonFields.Map<Visit, PrescriptionLine>(builder, "_prescriptions", "Prescriptions");
        builder.Ignore(e => e.ServiceLines);
        Animora.Persistence.Sqlite.Conventions.JsonFields.Map<Visit, VisitServiceLine>(builder, "_serviceLines", "ServiceLines");
        // M036: radiology studies + lab result sheets are JSON lists on the visit row (replicated with it).
        builder.Ignore(e => e.Radiology);
        Animora.Persistence.Sqlite.Conventions.JsonFields.Map<Visit, RadiologyRecord>(builder, "_radiology", "Radiology");
        builder.Ignore(e => e.LabResults);
        Animora.Persistence.Sqlite.Conventions.JsonFields.Map<Visit, LabResult>(builder, "_labResults", "LabResults");
    }
}
