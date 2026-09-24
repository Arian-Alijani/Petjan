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
}
