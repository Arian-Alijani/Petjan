using Animora.Persistence.Sqlite.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Animora.Persistence.Sqlite.Configurations.Finance;

// D04b [LocalOnly] peer finance cache. Table names (PeerInvoices / PeerPayments / PeerFinanceCursors) and scalar bounds
// come from ModelConventions; these configurations only declare keys and the lookup indexes.

/// <summary>Maps <see cref="PeerInvoice"/>: server invoice id key, listed newest-first per tenant.</summary>
public sealed class PeerInvoiceConfiguration : IEntityTypeConfiguration<PeerInvoice>
{
    public void Configure(EntityTypeBuilder<PeerInvoice> builder)
    {
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => new { e.TenantId, e.IssuedUtc }).HasDatabaseName("IX_PeerInvoices_TenantId_IssuedUtc");
    }
}

/// <summary>Maps <see cref="PeerPayment"/>: server payment id key, looked up by invoice.</summary>
public sealed class PeerPaymentConfiguration : IEntityTypeConfiguration<PeerPayment>
{
    public void Configure(EntityTypeBuilder<PeerPayment> builder)
    {
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => e.InvoiceId).HasDatabaseName("IX_PeerPayments_InvoiceId");
    }
}

/// <summary>Maps <see cref="PeerFinanceCursor"/>: one row per tenant.</summary>
public sealed class PeerFinanceCursorConfiguration : IEntityTypeConfiguration<PeerFinanceCursor>
{
    public void Configure(EntityTypeBuilder<PeerFinanceCursor> builder) => builder.HasKey(e => e.TenantId);
}
