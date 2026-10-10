using Animora.Domain.Sales;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Animora.Persistence.PostgreSql.Conventions;

namespace Animora.Persistence.PostgreSql.Configurations;

public sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("Payments", "public");
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => new { e.TenantId, e.OperationId }).IsUnique()
            .HasFilter("\"OperationId\" IS NOT NULL AND NOT \"Deleted\"")
            .HasDatabaseName("IX_Payments_TenantId_OperationId");
        OwnedRows.Money(builder, e => e.Amount);
        builder.Ignore(e => e.IsRefund);
        builder.Ignore(e => e.IsReversal);
        builder.HasIndex(e => new { e.TenantId, e.InvoiceId }).HasDatabaseName("IX_Payments_TenantId_InvoiceId");
        builder.HasIndex(e => new { e.TenantId, e.CashSessionId }).HasDatabaseName("IX_Payments_TenantId_CashSessionId");
        builder.HasIndex(e => new { e.TenantId, e.ReceivedUtc, e.Method }).HasDatabaseName("IX_Payments_TenantId_ReceivedUtc_Method");
    }
}
