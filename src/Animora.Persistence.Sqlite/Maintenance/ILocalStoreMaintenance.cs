using Animora.Domain.Results;

namespace Animora.Persistence.Sqlite.Maintenance;

/// <summary>Explicit local store maintenance operations; each appends a <c>StoreMaintenanceLog</c> row once the DB is usable.</summary>
public interface ILocalStoreMaintenance
{
    /// <summary>Online backup into the backup directory; returns the verified backup path.</summary>
    Task<Result<string>> BackupAsync(CancellationToken ct);

    /// <summary>Replaces the current store with a verified backup; the current store is quarantined, never deleted.</summary>
    Task<Result> RestoreAsync(string backupPath, CancellationToken ct);

    /// <summary>Moves the current store into quarantine (never deletes) and re-initializes an empty store.</summary>
    Task<Result<LocalStoreDiagnostics>> QuarantineAndRebuildAsync(CancellationToken ct);

    /// <summary>VACUUM + WAL truncate; returns the number of pages freed.</summary>
    Task<Result<int>> VacuumAsync(CancellationToken ct);

    /// <summary>
    /// C06: recomputes every <c>StockOnHandSnapshot</c> row of the current tenant from the append-only
    /// <c>StockMovements</c> ledger (the snapshot is a cache with a proof). Returns the number of rows written.
    /// </summary>
    Task<Result<int>> RebuildStockSnapshotsAsync(CancellationToken ct);

    /// <summary>
    /// C09: recomputes the current tenant's finance facts (<c>DailyFinanceFact</c>, <c>ProductSalesFact</c>,
    /// <c>OwnerFinancialSnapshot</c>) from the authoritative invoices/payments/movements and posted expenses.
    /// The facts are a cache with a proof; this reproduces them exactly. Returns the number of rows written.
    /// </summary>
    Task<Result<int>> RebuildFinanceProjectionsAsync(CancellationToken ct);
}
