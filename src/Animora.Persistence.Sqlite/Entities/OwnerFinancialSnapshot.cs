using Animora.Domain.Primitives;

namespace Animora.Persistence.Sqlite.Entities;

/// <summary>Rebuildable lifetime financial summary for an owner; never synced.</summary>
[LocalOnly]
public sealed class OwnerFinancialSnapshot
{
    private OwnerFinancialSnapshot() { }
    public string Id { get; private set; } = string.Empty;
    public string TenantId { get; private set; } = string.Empty;
    public string OwnerId { get; private set; } = string.Empty;
    public long TotalSpendMinor { get; private set; }
    public DateTimeOffset? LastInvoiceUtc { get; private set; }
    public int InvoiceCount { get; private set; }

    public static OwnerFinancialSnapshot Create(string tenantId, string ownerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        return new() { Id = IdGenerator.NewId(), TenantId = tenantId, OwnerId = ownerId };
    }

    public void Apply(long spend = 0, DateTimeOffset? invoiceUtc = null, int invoices = 0)
    {
        TotalSpendMinor = checked(TotalSpendMinor + spend);
        InvoiceCount = checked(InvoiceCount + invoices);
        if (invoiceUtc is not null && (LastInvoiceUtc is null || invoiceUtc > LastInvoiceUtc))
            LastInvoiceUtc = invoiceUtc;
    }
}
