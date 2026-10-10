using Animora.Domain.Sales;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Animora.Persistence.PostgreSql.Conventions;

namespace Animora.Persistence.PostgreSql.Configurations;

public sealed class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.ToTable("Invoices", "public");
        builder.HasKey(e => e.Id);
        OwnedRows.Money(builder, e => e.Subtotal);
        OwnedRows.Money(builder, e => e.DiscountTotal);
        OwnedRows.Money(builder, e => e.TaxTotal);
        OwnedRows.Money(builder, e => e.InsuranceCoverage);
        OwnedRows.Money(builder, e => e.GrandTotal);
        OwnedRows.Money(builder, e => e.PaidTotal);
        OwnedRows.Money(builder, e => e.DueTotal);
        builder.Ignore(e => e.IsImmutable);
        builder.Ignore(e => e.Currency);
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
        OwnedRows.Quantity(builder, e => e.Quantity);
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
