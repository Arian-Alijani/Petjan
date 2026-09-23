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
        OwnedRows.Map(builder, e => e.Lines, "_lines", "InvoiceLines", "LineId");
        OwnedRows.Map(builder, e => e.Adjustments, "_adjustments", "InvoiceAdjustments", "AdjustmentId");
        JsonFields.Map<Invoice, string>(builder, "_appliedPaymentOperationIds", "AppliedPaymentOperationIds");
    }
}
