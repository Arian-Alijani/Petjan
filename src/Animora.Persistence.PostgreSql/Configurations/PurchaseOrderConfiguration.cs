using Animora.Domain.Purchasing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Animora.Persistence.PostgreSql.Conventions;

namespace Animora.Persistence.PostgreSql.Configurations;

public sealed class PurchaseOrderConfiguration : IEntityTypeConfiguration<PurchaseOrder>
{
    public void Configure(EntityTypeBuilder<PurchaseOrder> builder)
    {
        builder.ToTable("PurchaseOrders", "public");
        OwnedRows.Money(builder, e => e.Subtotal);
        OwnedRows.Money(builder, e => e.DiscountTotal);
        OwnedRows.Money(builder, e => e.TaxTotal);
        OwnedRows.Money(builder, e => e.GrandTotal);
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => new { e.TenantId, e.State, e.OrderedUtc }).HasDatabaseName("IX_PurchaseOrders_TenantId_State_OrderedUtc");
        OwnedRows.Map(builder, e => e.Lines);
    }
}

/// <summary>Child rows of <see cref="PurchaseOrderLine"/>'s aggregate, table <c>PurchaseOrderLines</c>.</summary>
public sealed class PurchaseOrderLineConfiguration : IEntityTypeConfiguration<PurchaseOrderLine>
{
    public void Configure(EntityTypeBuilder<PurchaseOrderLine> builder)
    {
        OwnedRows.Row(builder, "PurchaseOrderLines", e => e.LineId);
        OwnedRows.Quantity(builder, e => e.OrderedQuantity);
        OwnedRows.Quantity(builder, e => e.ReceivedQuantity);
        OwnedRows.Money(builder, e => e.UnitCost);
    }
}
