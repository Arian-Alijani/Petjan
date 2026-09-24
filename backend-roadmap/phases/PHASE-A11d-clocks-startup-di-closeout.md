# Phase A11d — Clocks, 8-step LocalStoreStartup, final DI, A11 close-out (session 4/4 of A11)

**Track:** A · **Depends on:** A11c ✅ · **Next:** A12

## Session rules
- Read ONLY this file + "Read only" files. No other phase files, UI, mocks, tests.
- Rules: A-RULE 011 (high-water never decreases), PERFORMANCE §12 (init off the first-frame path), A-RULE 016 (no network).
- **No tests / no `test.sh`. No migration.** No DI/`App.axaml.cs` change in Desktop; desktop stays on mocks.

## Read only
`Domain/Time/{IClock,IMonotonicClock}.cs`, `Entities/{ClockHighWater,LocalInstallation}.cs`, `Context/LocalInstallationBootstrap.cs`, `Maintenance/{ILocalStoreInitializer,ILocalStoreMaintenance,LocalStoreDiagnostics}.cs`, `Seed/LocalSeeder.cs` (signature), `Security/{LocalDbKeyService,InstallationIdentity}.cs` (signatures), `Startup/NativeSqliteAssertion.cs`, both DI extension files, `Context/LocalDbContextFactory.cs` (has a private `DesignTimeClock`; leave it, design-time only).

## Tasks
1. `Infrastructure.Local/Time/SystemClock.cs` — `sealed class SystemClock(TimeProvider? provider = null) : IClock` over `TimeProvider.System`.
2. `Infrastructure.Local/Time/MonotonicClock.cs` — `sealed class MonotonicClock : IMonotonicClock`: `ElapsedTicks` = `Stopwatch.GetTimestamp()`; `HighWaterUtc` loaded lazily from the single `ClockHighWater` row via a short-lived context from `IDbContextFactory<AnimoraLocalDbContext>`; `Observe(serverUtc)` updates memory + row **only when greater** (conditional `ExecuteUpdate … WHERE HighWaterUtc < @v` or insert-if-missing) — an OS clock rollback can never lower it; thread-safe; singleton.
3. `Infrastructure.Local/Startup/LocalStoreStartup.cs` — `sealed class LocalStoreStartup : ILocalStoreInitializer`, `InitializeAsync(ct)` executing exactly, in order (record each step name in an internal step log for A12):
   1. ensure data directory exists with user-only permissions (Unix mode 0700 / Windows current-user ACL); record `RecoveredFromCrash` = a non-empty `-wal` exists before open;
   2. `options.Validate()`; when `EncryptionEnabled`: `LocalDbKeyService.GetOrCreatePassphrase()` → failure returns `StoreKeyUnavailable` **before any file is created**;
   3. open a connection (interceptor applies key + pragmas) → `NativeSqliteAssertion.Assert` (before any migration; failure returned as-is — the desktop must show a blocking fatal dialog and exit, wired in C01/B05, never a silent degrade);
   4. `PRAGMA quick_check` ≠ `ok` (or `SqliteException` code 11/26) → `ILocalStoreMaintenance.QuarantineAndRebuildAsync` and return its result (skip when re-entered from quarantine);
   5. `Database.MigrateAsync()`;
   6. `PRAGMA wal_checkpoint(TRUNCATE)` when `-wal` size / page_size > `WalCheckpointPages` (log `Checkpoint`); log `CrashRecovery` when step 1 flagged it;
   7. `LocalInstallationBootstrap.EnsureAsync(InstallationIdentity.ComputeMachineFingerprintHash())`, then `LocalSeeder.SeedAsync`;
   8. return `LocalStoreDiagnostics` (path, native version, encryption active, `KeySource`, latest migration id, recovered, quarantined, db bytes, last backup time from newest `StoreMaintenanceLog` Backup row).
   Uses a fresh DI scope internally; never called on the UI thread (desktop invokes it as a background task in C01 — not in this phase).
4. **Final DI** (`AddAnimoraLocalInfrastructure`): `IClock`→`SystemClock` singleton (replace any placeholder; keep `TryAdd` semantics so tests can override), `IMonotonicClock`→`MonotonicClock` singleton, `LocalStoreStartup` scoped + as `ILocalStoreInitializer`. Verify no captive-dependency/lifetime errors by building a provider with `ValidateScopes = true, ValidateOnBuild = true` in a throwaway local check (do not commit a test).
5. Update `Infrastructure.Local/README.md` (what now exists) — ≤ 10 lines.

## Gate (A11 acceptance)
- `build.sh full` = 0/0; `scripts/sandbox/run.sh` = `SMOKE RUN OK` (desktop still on mocks).
- `grep -rn "Http\|Socket\|WebClient" src/Animora.Infrastructure.Local` → nothing.
- `dotnet ef migrations list` still 001, 002, 003 with no pending model changes.

## Handoff
PROGRESS: rows A11d ✅ **and A11 ✅**; Next → `A12`. Append an A11 "Repo state" entry: keystore impls, `LocalDbKeyService`, encryption seam + `K2Approved` gate, native assertion, the 8 startup steps, `LocalStoreDiagnostics`, maintenance/backup/quarantine, `MonotonicClock` high-water, `LocalFileStore`, 3 local-only entities, migration 003, ErrorCodes +6, no Desktop DI change, no mock deleted, deviations (incl. diagnostics/KeySource placed in Persistence, `AddAnimoraLocalInfrastructure` entry point, ProtectedData pin). Append **A12 test debt**:
- `NativeSqliteVersionTests`: runtime version ≥ 3.50.2; `Parse`/`Assert` → `NATIVE_SQLITE_TOO_OLD` for a lower version.
- `StoreStartupTests`: 8 steps in order (step log); after init `journal_mode=wal`, `synchronous=NORMAL(1)`, `busy_timeout=5000`, `foreign_keys=1`; `EncryptionEnabled` + no key + no fallback → `STORE_KEY_UNAVAILABLE` and no `animora.db`; `EncryptionEnabled` + `!K2Approved` → `Validate()` = `STORE_ENCRYPTION_NOT_APPROVED`.
- `BackupRestoreTests`: backup → quick_check ok → restore → pre-backup rows intact; retention keeps exactly `BackupRetentionCount`; one `StoreMaintenanceLog` row per action.
- `QuarantineRebuildTests`: header-corrupted DB moved (not deleted) with `-wal`/`-shm` into `quarantine/<timestamp>/`; fresh migrated + seeded DB; `Quarantined = true`.
- `MonotonicClockTests`: earlier `Observe` never lowers `HighWaterUtc`; value survives a new context.
- `LocalFileStoreTests`: put/open/delete round trip, dedupe, invalid hash rejected.
Commit `feat(local): clocks, local store startup and final registration (A11d)`. Final answer: done / validation actually run / deviations / blockers.
