using System.Collections.ObjectModel;
namespace Animora.Persistence.PostgreSql.Replication;

public static class SyncExclusions
{
    public static IReadOnlyDictionary<string, string> Reasons { get; } =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Invoice"] = "Command-settled money, ledger or stock state.",
            ["Payment"] = "Command-settled money, ledger or stock state.",
            ["CashSession"] = "Command-settled money, ledger or stock state.",
            ["JournalEntry"] = "Command-settled money, ledger or stock state.",
            ["Cheque"] = "Command-settled money, ledger or stock state.",
            ["Expense"] = "Command-settled money, ledger or stock state.",
            ["RecurringExpense"] = "Command-settled money, ledger or stock state.",
            ["RecurringExpensePayment"] = "Command-settled money, ledger or stock state.",
            ["StockMovement"] = "Command-settled money, ledger or stock state.",
            ["PurchaseOrder"] = "Command-settled money, ledger or stock state.",
            ["GoodsReceipt"] = "Command-settled money, ledger or stock state.",
            ["User"] = "Security, command bookkeeping or singleton settings; not a replication contract.",
            ["Device"] = "Security, command bookkeeping or singleton settings; not a replication contract.",
            ["RefreshToken"] = "Security, command bookkeeping or singleton settings; not a replication contract.",
            ["SecurityAuditEntry"] = "Security, command bookkeeping or singleton settings; not a replication contract.",
            ["SigningKey"] = "Security, command bookkeeping or singleton settings; not a replication contract.",
            ["CommandIntakeRecord"] = "Security, command bookkeeping or singleton settings; not a replication contract.",
            ["CommandIdMapEntry"] = "Security, command bookkeeping or singleton settings; not a replication contract.",
            ["DocumentCounter"] = "Security, command bookkeeping or singleton settings; not a replication contract.",
            ["AppointmentSettingsEntity"] = "Security, command bookkeeping or singleton settings; not a replication contract.",
            ["VisitBillingHandoff"] = "Child of Visit; captured and exported with its root.",
            ["BoardingBillingHandoff"] = "Child of BoardingStay; captured and exported with its root.",
        });
}
