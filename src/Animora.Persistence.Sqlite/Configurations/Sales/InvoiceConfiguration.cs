using Animora.Domain.Sales;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Animora.Persistence.Sqlite.Conventions;

namespace Animora.Persistence.Sqlite.Configurations.Sales;

public sealed class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => new { e.TenantId, e.State, e.IssuedUtc }).HasDatabaseName("IX_Invoices_TenantId_State_IssuedUtc");
        builder.HasIndex(e => new { e.TenantId, e.InvoiceNumber }).HasDatabaseName("IX_Invoices_TenantId_Number").IsUnique();
        builder.HasIndex(e => new { e.TenantId, e.OwnerId }).HasDatabaseName("IX_Invoices_TenantId_OwnerId");
        builder.HasIndex(e => new { e.TenantId, e.VisitId }).HasDatabaseName("IX_Invoices_TenantId_VisitId");
        OwnedRows.Map(builder, e => e.Lines);
        OwnedRows.Map(builder, e => e.Adjustments);
        JsonFields.Map<Invoice, string>(builder, "_appliedPaymentOperationIds", "AppliedPaymentOperationIds");
    }
}

/// <summary>Child rows of <see cref="InvoiceLine"/>'s aggregate, table <c>InvoiceLines</c>.</summary>
public sealed class InvoiceLineConfiguration : IEntityTypeConfiguration<InvoiceLine>
{
    public void Configure(EntityTypeBuilder<InvoiceLine> builder)
    {
        OwnedRows.Row(builder, "InvoiceLines", e => e.LineId);
        OwnedRows.Quantity(builder, e => e.Quantity, summable: true);
        OwnedRows.Money(builder, e => e.UnitPrice);
    }
}

/// <summary>Child rows of <see cref="InvoiceAdjustment"/>'s aggregate, table <c>InvoiceAdjustments</c>.</summary>
public sealed class InvoiceAdjustmentConfiguration : IEntityTypeConfiguration<InvoiceAdjustment>
{
    public void Configure(EntityTypeBuilder<InvoiceAdjustment> builder)
    {
        OwnedRows.Row(builder, "InvoiceAdjustments", e => e.AdjustmentId);
        OwnedRows.Money(builder, e => e.Amount);
    }
}
