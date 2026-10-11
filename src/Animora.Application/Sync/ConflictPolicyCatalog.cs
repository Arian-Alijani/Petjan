namespace Animora.Application.Sync;

/// <summary>
/// Single source mapping a replicated wire entity name (<c>SyncRecord.Entity</c>) to its <see cref="ConflictClass"/> (ARCHITECTURE.md §8.5).
/// <para>
/// <b>Default for an unknown entity: <see cref="ConflictClass.Demographics"/></b> — the safest policy: the inbound server row is applied to
/// the replica base, locally pending intent stays pending, and no whole-row last-write-wins and no money/stock mutation ever happens by accident.
/// </para>
/// </summary>
public static class ConflictPolicyCatalog
{
    /// <summary>The class used for an entity that is not listed.</summary>
    public const ConflictClass DefaultClass = ConflictClass.Demographics;

    private static readonly Dictionary<string, ConflictClass> Map = new(StringComparer.Ordinal)
    {
        // UI preferences / display settings
        ["UserPreference"] = ConflictClass.UiPreference,
        ["UiPreference"] = ConflictClass.UiPreference,
        ["DisplaySetting"] = ConflictClass.UiPreference,

        // Posted financial facts (immutable)
        ["Invoice"] = ConflictClass.PostedFinancial,
        ["InvoiceLine"] = ConflictClass.PostedFinancial,
        ["Payment"] = ConflictClass.PostedFinancial,
        ["CreditNote"] = ConflictClass.PostedFinancial,
        ["Refund"] = ConflictClass.PostedFinancial,
        ["Cheque"] = ConflictClass.PostedFinancial,
        ["CashSession"] = ConflictClass.PostedFinancial,
        ["Expense"] = ConflictClass.PostedFinancial,
        ["GoodsReceipt"] = ConflictClass.PostedFinancial,

        // Append-only ledger and audit
        ["LedgerEntry"] = ConflictClass.AppendOnlyLedger,
        ["LedgerLine"] = ConflictClass.AppendOnlyLedger,
        ["JournalEntry"] = ConflictClass.AppendOnlyLedger,
        ["JournalLine"] = ConflictClass.AppendOnlyLedger,
        ["AuditEntry"] = ConflictClass.AppendOnlyLedger,

        // Inventory movements (additive)
        ["StockMovement"] = ConflictClass.InventoryMovement,
        ["StockAdjustment"] = ConflictClass.InventoryMovement,

        // Appointment slots
        ["Appointment"] = ConflictClass.AppointmentSlot,

        // Owner / pet demographics (field-level merge)
        ["Owner"] = ConflictClass.Demographics,
        ["Pet"] = ConflictClass.Demographics,
    };

    /// <summary>Returns the conflict class of <paramref name="entity"/>, or <see cref="DefaultClass"/> when it is not listed.</summary>
    public static ConflictClass ClassOf(string entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        return Map.GetValueOrDefault(entity, DefaultClass);
    }

    /// <summary>True when <paramref name="entity"/> has an explicit entry (as opposed to the safe default).</summary>
    public static bool IsKnown(string entity) => entity is not null && Map.ContainsKey(entity);
}
