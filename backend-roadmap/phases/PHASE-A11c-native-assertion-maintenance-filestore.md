# Phase A11c — Native SQLite assertion, diagnostics, maintenance (backup/restore/quarantine/vacuum), file store (session 3/4 of A11)

**Track:** A · **Depends on:** A11b ✅ · **Next:** A11d

## Session rules
- Read ONLY this file + "Read only" files. No other phase files, UI, mocks, tests.
- Rules: T-RULE 004 — native SQLite version is asserted at runtime (a managed package version is never proof); failure is fatal and visible. ARCHITECTURE §17.3 only if recovery semantics are unclear.
- **No tests / no `test.sh`. No migration. Zero network.** No scheduling of backup/vacuum (B05), no UI (C10).

## Read only
`Options/LocalStoreOptions.cs`, `Entities/StoreMaintenanceLog.cs`, `Maintenance/KeySource.cs`, `Context/{IStoreEncryptionProvider,IStoreKeyProvider,LocalStoreServiceCollectionExtensions}.cs`, `Domain/Patients/DocumentReference.cs` (name only), `Infrastructure.Local/LocalInfrastructureServiceCollectionExtensions.cs`.

## Pre-made decisions (avoid the Persistence ↔ Infrastructure cycle)
- `LocalStoreDiagnostics` lives in **`Persistence.Sqlite/Maintenance/LocalStoreDiagnostics.cs`** (not Infrastructure.Local) because maintenance returns it: `sealed record LocalStoreDiagnostics(string DatabasePath, string NativeSqliteVersion, bool EncryptionActive, KeySource KeySource, string SchemaVersion, bool RecoveredFromCrash, bool Quarantined, long DatabaseBytes, DateTimeOffset? LastBackupUtc)`.
- `Persistence.Sqlite/Maintenance/ILocalStoreInitializer.cs` — `Task<Result<LocalStoreDiagnostics>> InitializeAsync(CancellationToken ct)`; implemented by `LocalStoreStartup` in A11d. Maintenance resolves it lazily (`IServiceProvider`/`Lazy<>`) to avoid a DI cycle.
- `NativeSqliteAssertion` lives in `Infrastructure.Local/Startup/` (per original spec) — only startup uses it.

## Tasks
1. `Infrastructure.Local/Startup/NativeSqliteAssertion.cs` — `static Result Assert(SqliteConnection open)` + `static Result<Version> Parse(string)` (testable seam): `SELECT sqlite_version();`, parse `major.minor.patch`, below **3.50.2** → `NativeSqliteTooOld` with detail = actual version; unparsable → same code.
2. `Maintenance/ILocalStoreMaintenance.cs` + `Maintenance/LocalStoreMaintenance.cs` (all via fresh `SqliteConnection`s with the same key/pragmas path; every action appends a `StoreMaintenanceLog` row once the DB is usable):
   - `Task<Result<string>> BackupAsync(ct)` — `SqliteConnection.BackupDatabase` into `<BackupDirectory>/animora-<yyyyMMdd-HHmmss>.db` (UTC from `IClock`, collision-safe), `PRAGMA quick_check` on the copy (≠ `ok` → delete that bad copy, `StoreBackupFailed`), prune oldest to `BackupRetentionCount`. Encrypted source ⇒ destination keyed the same (backup stays encrypted).
   - `Task<Result> RestoreAsync(string backupPath, ct)` — validate exists + `quick_check`; `SqliteConnection.ClearAllPools()`; move current db/-wal/-shm to `<data>/quarantine/<timestamp>/`; copy backup into place; `ILocalStoreInitializer.InitializeAsync`; failures → `StoreRestoreFailed` (original kept in quarantine).
   - `Task<Result<LocalStoreDiagnostics>> QuarantineAndRebuildAsync(ct)` — `ClearAllPools()`, move `animora.db`, `-wal`, `-shm` into `<data>/quarantine/<timestamp>/` (**never delete**), then `InitializeAsync` with a re-entry flag so a second corrupt check cannot loop; return diagnostics with `Quarantined = true`; log `Quarantine`.
   - `Task<Result<int>> VacuumAsync(ct)` — `VACUUM` + `PRAGMA wal_checkpoint(TRUNCATE)`; return pages freed (page_count delta). Off the startup path.
   - Map `SqliteException`/`IOException` to the specific codes; never swallow cancellation.
3. `Application/Ports/ILocalFileStore.cs` + `Infrastructure.Local/Storage/LocalFileStore.cs` — content-addressed `<data>/files/<sha256[0..2]>/<sha256>`: `Task<Result<string>> PutAsync(Stream, ct)` (stream to temp file while hashing, atomic move, dedupe), `Task<Result<Stream>> OpenAsync(string hash, ct)`, `Task<Result> DeleteAsync(string hash, ct)`; validate hash `^[0-9a-f]{64}$` (no path traversal). Metadata stays in `DocumentReference` (A04).
4. **DI**: `ILocalStoreMaintenance` scoped in `AddAnimoraLocalStore`; `ILocalFileStore` singleton in `AddAnimoraLocalInfrastructure`.

## Gate
- `build.sh full` = 0/0; `grep -rn "Http\|Socket\|WebClient" src/Animora.Infrastructure.Local` → nothing.

## Handoff
PROGRESS: row A11c ✅ + note (assertion, diagnostics location, maintenance ops + log kinds, quarantine never deletes, file store layout); Next → `A11d`. Commit `feat(store): native sqlite assertion, maintenance paths and local file store (A11c)`. Final answer: done / deviations / blockers.
