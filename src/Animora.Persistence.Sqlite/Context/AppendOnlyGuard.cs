using Animora.Domain.Clinical;
using Animora.Domain.Finance;
using Animora.Domain.Inventory;
using Animora.Domain.Sales;
using Animora.Persistence.Sqlite.Entities;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.Sqlite.Context;

/// <summary>
/// Blocks updates and deletes of append-only records. New rows (including new child rows under a
/// modified mutable owner, e.g. a cheque's next event) are allowed; corrections are new rows
/// (<c>JournalEntry.CreateReversal</c>, reversing stock movements).
/// </summary>
public static class AppendOnlyGuard
{
    public static IReadOnlySet<Type> ProtectedTypes { get; } = new HashSet<Type>
    {
        typeof(JournalEntry), typeof(JournalLine), typeof(MedicalHistoryEntry),
        typeof(StockMovement), typeof(Payment), typeof(ChequeEvent), typeof(StoreMaintenanceLog),
        typeof(LoginAttemptLog)
    };

    public static void Check(DbContext db)
    {
        // Entries() runs DetectChanges, so owned/dependent child rows are included with their real state.
        foreach (var entry in db.ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Modified or EntityState.Deleted) ||
                !ProtectedTypes.Contains(entry.Metadata.ClrType)) continue;
            var key = string.Join(", ", entry.Properties.Where(p => p.Metadata.IsPrimaryKey())
                .Select(p => $"{p.Metadata.Name}={p.OriginalValue}"));
            throw new InvalidOperationException(
                $"Append-only {entry.Metadata.ClrType.Name} [{key}] cannot be {entry.State}; record a correction instead.");
        }
    }
}
