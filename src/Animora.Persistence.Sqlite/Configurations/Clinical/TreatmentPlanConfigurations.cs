using Animora.Domain.Clinical;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Animora.Persistence.Sqlite.Configurations.Clinical;

public sealed class TreatmentPlanConfiguration : IEntityTypeConfiguration<TreatmentPlan>
{
    public void Configure(EntityTypeBuilder<TreatmentPlan> builder)
    {
        builder.HasKey(e => e.Id);
        // «Plans of this patient» list (newest first, active ones surfaced by state).
        builder.HasIndex(e => new { e.TenantId, e.PetId, e.State }).HasDatabaseName("IX_TreatmentPlans_TenantId_PetId_State");
    }
}

public sealed class TreatmentOccurrenceConfiguration : IEntityTypeConfiguration<TreatmentOccurrence>
{
    public void Configure(EntityTypeBuilder<TreatmentOccurrence> builder)
    {
        builder.HasKey(e => e.Id);
        // DueDay is a DateOnly → TEXT yyyy-MM-dd: lexical order == chronological order, so range scans use this index.
        builder.HasIndex(e => new { e.TenantId, e.DueDay, e.State }).HasDatabaseName("IX_TreatmentOccurrences_TenantId_DueDay_State");
        // Loading / counting the occurrences of one plan.
        builder.HasIndex(e => new { e.TenantId, e.PlanId }).HasDatabaseName("IX_TreatmentOccurrences_TenantId_PlanId");
    }
}

public sealed class VaccineCatalogItemConfiguration : IEntityTypeConfiguration<VaccineCatalogItem>
{
    public void Configure(EntityTypeBuilder<VaccineCatalogItem> builder)
    {
        builder.HasKey(e => e.Id);
        // A clinic code identifies one catalog item.
        builder.HasIndex(e => new { e.TenantId, e.Code }).IsUnique().HasDatabaseName("IX_VaccineCatalogItems_TenantId_Code");
    }
}
