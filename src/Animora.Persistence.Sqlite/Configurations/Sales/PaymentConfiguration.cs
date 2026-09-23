using Animora.Domain.Sales;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Animora.Persistence.Sqlite.Conventions;

namespace Animora.Persistence.Sqlite.Configurations.Sales;

public sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => new { e.TenantId, e.InvoiceId }).HasDatabaseName("IX_Payments_TenantId_InvoiceId");
        builder.HasIndex(e => new { e.TenantId, e.ReceivedUtc, e.Method }).HasDatabaseName("IX_Payments_TenantId_ReceivedUtc_Method");
    }
}
