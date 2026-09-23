using Animora.Domain.Scheduling;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Animora.Persistence.Sqlite.Configurations.Scheduling;

public sealed class AppointmentConfiguration : IEntityTypeConfiguration<Appointment>
{
    public void Configure(EntityTypeBuilder<Appointment> builder)
    {
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => new { e.TenantId, e.StartUtc, e.DoctorUserId }).HasDatabaseName("IX_Appointments_TenantId_StartUtc_DoctorUserId");
        builder.HasIndex(e => new { e.TenantId, e.State, e.StartUtc }).HasDatabaseName("IX_Appointments_TenantId_State_StartUtc");
        builder.HasIndex(e => new { e.TenantId, e.PetId }).HasDatabaseName("IX_Appointments_TenantId_PetId");
        builder.Ignore(e => e.Slot); // Derived from StartUtc + DurationMinutes.
    }
}
