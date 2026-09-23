using Animora.Domain.Purchasing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Animora.Persistence.Sqlite.Conventions;

namespace Animora.Persistence.Sqlite.Configurations.Purchasing;

public sealed class GoodsReceiptConfiguration : IEntityTypeConfiguration<GoodsReceipt>
{
    public void Configure(EntityTypeBuilder<GoodsReceipt> builder)
    {
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => new { e.TenantId, e.PurchaseOrderId }).HasDatabaseName("IX_GoodsReceipts_TenantId_PurchaseOrderId");
        OwnedRows.Map(builder, e => e.Lines, "_lines", "GoodsReceiptLines", "LineId");
    }
}
