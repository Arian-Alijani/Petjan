using Animora.Domain.Results;

namespace Animora.Persistence.Sqlite.Maintenance;

/// <summary>Explicit local store maintenance operations; each appends a <c>StoreMaintenanceLog</c> row once the DB is usable.</summary>
public interface ILocalStoreMaintenance
{
    /// <summary>Atomically clears the previous clinic and binds this installation; refuses to discard unsettled commands.</summary>
    Task<Result> EnsureBoundToAsync(string tenantId, CancellationToken ct);

    /// <summary>
    /// Binds this installation to <paramref name="tenantId"/> while, on a genuine clinic switch, isolating and preserving the
    /// OUTGOING clinic's unsynced data instead of refusing. With <paramref name="preserveOutgoingUnsynced"/> the outgoing store
    /// is hot-copied to a per-tenant stash (<c>&lt;data&gt;/clinics/&lt;outgoingTenantId&gt;/animora.db</c>) before the switch, and
    /// binding TO a tenant that has a stash restores it (so returning to that clinic brings back its unsynced commands and local
    /// rows to finish syncing). A stash is keyed strictly by tenant id and never crosses clinics. With the flag off this behaves
    /// exactly like <see cref="EnsureBoundToAsync(string, CancellationToken)"/> (fails <c>SYNC_PENDING_CHANGES</c> when the
    /// outgoing clinic has unsettled commands; the failure detail carries the count as
    /// <c>tenant_switch_requires_settlement:&lt;N&gt;</c> so the UI can offer an explicit choice).
    /// </summary>
    Task<Result> EnsureBoundToAsync(string tenantId, bool preserveOutgoingUnsynced, CancellationToken ct);

    /// <summary>Online backup into the backup directory; returns the verified backup path.</summary>
    Task<Result<string>> BackupAsync(CancellationToken ct);

    /// <summary>Replaces the current store with a verified backup; the current store is quarantined, never deleted.</summary>
    Task<Result> RestoreAsync(string backupPath, CancellationToken ct);

    /// <summary>Moves the current store into quarantine (never deletes) and re-initializes an empty store.</summary>
    Task<Result<LocalStoreDiagnostics>> QuarantineAndRebuildAsync(CancellationToken ct);

    /// <summary>
    /// Full local reset for a from-scratch re-sync. Destructively clears EVERY business table of the current clinic as well as
    /// the replica base/checkpoint/staging and the sync cursor, keeping only the installation identity and the global permission
    /// catalog, then rebinds the installation to the SAME current tenant and re-seeds just the Tenant + RoleDefinitions. Unlike a
    /// clinic switch this never stashes: it is an explicit, operator-confirmed wipe whose intent is to re-pull everything from the
    /// server (a full snapshot). Fails <c>STORE_UNAVAILABLE</c> when no real clinic is bound yet (nothing to re-sync). The caller
    /// is responsible for triggering the subsequent full-snapshot pull.
    /// </summary>
    Task<Result> ResetForResyncAsync(CancellationToken ct);

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

    /// <summary>Replays supported outbox domain events through the live mapper for this tenant.</summary>
    Task<Result<int>> RebuildActivityFeedAsync(CancellationToken ct);
}
