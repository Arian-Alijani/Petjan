using Animora.Domain.Clinical;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Animora.Persistence.PostgreSql.Configurations;

public sealed class VaccinationRecordConfiguration : IEntityTypeConfiguration<VaccinationRecord>
{
    public void Configure(EntityTypeBuilder<VaccinationRecord> builder)
    {
        builder.ToTable("VaccinationRecords", "public");
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => new { e.TenantId, e.NextDueUtc, e.State }).HasDatabaseName("IX_VaccinationRecords_TenantId_DueUtc_State");
        // D23: doses administered within one clinic day.
        builder.HasIndex(e => new { e.TenantId, e.AdministeredUtc }).HasDatabaseName("IX_VaccinationRecords_TenantId_AdministeredUtc");
    }
}
