using Animora.Domain.Clinical;
using Animora.Domain.Finance;
using Animora.Domain.Inventory;
using Animora.Domain.Sales;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.Sqlite.Context;

public static class AppendOnlyGuard
{
    public static IReadOnlySet<Type> ProtectedTypes { get; } = new HashSet<Type>
    {
        typeof(JournalEntry), typeof(JournalLine), typeof(MedicalHistoryEntry),
        typeof(StockMovement), typeof(Payment), typeof(ChequeEvent)
    };

    public static void Check(DbContext db)
    {
        foreach (var entry in db.ChangeTracker.Entries())
            if (entry.State is EntityState.Modified or EntityState.Deleted &&
                ProtectedTypes.Contains(entry.Metadata.ClrType))
                throw new InvalidOperationException($"Append-only {entry.Metadata.ClrType.Name} {entry.Properties.FirstOrDefault(p => p.Metadata.IsPrimaryKey())?.CurrentValue} cannot be {entry.State}.");
    }
}
