using Animora.Persistence.Sqlite.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Animora.Persistence.Sqlite.Configurations.Finance;

public sealed class DailyFinanceFactConfiguration : IEntityTypeConfiguration<DailyFinanceFact>
{
    public void Configure(EntityTypeBuilder<DailyFinanceFact> builder)
    {
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => new { e.TenantId, e.LocalDay }).IsUnique()
            .HasDatabaseName("IX_DailyFinanceFacts_TenantId_LocalDay");
    }
}

public sealed class ProductSalesFactConfiguration : IEntityTypeConfiguration<ProductSalesFact>
{
    public void Configure(EntityTypeBuilder<ProductSalesFact> builder)
    {
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => new { e.TenantId, e.LocalDay, e.ProductId, e.VariantId }).IsUnique()
            .HasDatabaseName("IX_ProductSalesFacts_TenantId_LocalDay_ProductId_VariantId");
    }
}

public sealed class OwnerFinancialSnapshotConfiguration : IEntityTypeConfiguration<OwnerFinancialSnapshot>
{
    public void Configure(EntityTypeBuilder<OwnerFinancialSnapshot> builder)
    {
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => new { e.TenantId, e.OwnerId }).IsUnique()
            .HasDatabaseName("IX_OwnerFinancialSnapshots_TenantId_OwnerId");
        // Balance was removed from the projection/API, not from existing databases. Retain the
        // legacy column as storage-only metadata so the model still matches migration 016 onward.
        // No CLR property or business use: do not drop data or suppress PendingModelChangesWarning.
        builder.Property<long>("OpenBalanceMinor").HasColumnType("INTEGER");
    }
}
