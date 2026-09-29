using Animora.Domain.Primitives;

namespace Animora.Persistence.Sqlite.Entities;

/// <summary>Rebuildable device-local finance totals for one clinic-local calendar day.</summary>
[LocalOnly]
public sealed class DailyFinanceFact
{
    private DailyFinanceFact() { }
    public string Id { get; private set; } = string.Empty;
    public string TenantId { get; private set; } = string.Empty;
    public int LocalDay { get; private set; }
    public long RevenueMinor { get; private set; }
    public long CogsMinor { get; private set; }
    public long ExpenseMinor { get; private set; }
    public long DiscountMinor { get; private set; }
    public long InsuranceMinor { get; private set; }
    public long RefundMinor { get; private set; }
    public int InvoiceCount { get; private set; }
    public int PaidInvoiceCount { get; private set; }

    public static DailyFinanceFact Create(string tenantId, int localDay)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        if (localDay < 10101 || localDay > 99991231) throw new ArgumentOutOfRangeException(nameof(localDay));
        return new() { Id = IdGenerator.NewId(), TenantId = tenantId, LocalDay = localDay };
    }

    public void Apply(long revenue = 0, long cogs = 0, long expense = 0,
        long discount = 0, long insurance = 0, long refund = 0,
        int invoices = 0, int paidInvoices = 0)
    {
        RevenueMinor = checked(RevenueMinor + revenue);
        CogsMinor = checked(CogsMinor + cogs);
        ExpenseMinor = checked(ExpenseMinor + expense);
        DiscountMinor = checked(DiscountMinor + discount);
        InsuranceMinor = checked(InsuranceMinor + insurance);
        RefundMinor = checked(RefundMinor + refund);
        InvoiceCount = checked(InvoiceCount + invoices);
        PaidInvoiceCount = checked(PaidInvoiceCount + paidInvoices);
    }
}
