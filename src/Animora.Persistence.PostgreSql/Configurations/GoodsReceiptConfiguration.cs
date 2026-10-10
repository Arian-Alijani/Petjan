using Animora.Domain.Purchasing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Animora.Persistence.PostgreSql.Conventions;

namespace Animora.Persistence.PostgreSql.Configurations;

public sealed class GoodsReceiptConfiguration : IEntityTypeConfiguration<GoodsReceipt>
{
    public void Configure(EntityTypeBuilder<GoodsReceipt> builder)
    {
        builder.ToTable("GoodsReceipts", "public");
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => new { e.TenantId, e.PurchaseOrderId }).HasDatabaseName("IX_GoodsReceipts_TenantId_PurchaseOrderId");
        OwnedRows.Map(builder, e => e.Lines);
    }
}

/// <summary>Child rows of <see cref="GoodsReceiptLine"/>'s aggregate, table <c>GoodsReceiptLines</c>.</summary>
public sealed class GoodsReceiptLineConfiguration : IEntityTypeConfiguration<GoodsReceiptLine>
{
    public void Configure(EntityTypeBuilder<GoodsReceiptLine> builder)
    {
        OwnedRows.Row(builder, "GoodsReceiptLines", e => e.LineId);
        OwnedRows.Quantity(builder, e => e.Quantity);
        OwnedRows.Money(builder, e => e.UnitCost);
    }
}
