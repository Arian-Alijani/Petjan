using Animora.Persistence.Sqlite.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Animora.Persistence.Sqlite.Configurations.Activity;

/// <summary>
/// <see cref="DashboardGeneration"/>: one row per tenant keyed by the tenant itself, so the cache-version probe is a
/// primary-key lookup whatever the history size. The row is maintained by triggers, not by EF.
/// </summary>
public sealed class DashboardGenerationConfiguration : IEntityTypeConfiguration<DashboardGeneration>
{
    public void Configure(EntityTypeBuilder<DashboardGeneration> builder)
    {
        builder.HasKey(e => e.TenantId);
        builder.Property(e => e.Value).HasColumnType("INTEGER");
    }
}
