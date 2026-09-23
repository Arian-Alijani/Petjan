# Phase A11 — Local store hardening

**Band:** M · **Track:** A · **Depends on:** A10 · **Satisfies:** R-B02, R-B09, R-B27, R-B28
**Runnable after this phase:** desktop offline yes · server required no

> **Track A testing policy (see ROADMAP §3.10).** This phase writes **no tests** and runs **no test
> suite**. It ships hardening (keystore, encryption seam, native assertion, backup/quarantine, clocks,
> migration) only. All tests are authored and executed once, in **Phase A12** — which immediately
> follows this phase. The single verification here is a lightweight build (`scripts/sandbox/build.sh
> full` = 0/0); run only if needed.

## Read before starting
- `backend-roadmap/CONVENTIONS.md` §5, §7, §8, §11, §12
- `backend-roadmap/PROGRESS.md`
- `STACK.md` §3.3 (local store technology, native matrix, CVE-2025-6965), §12 (gate K2, native
  assertion), §15 (T-RULE 004, T-RULE 015)
- `ARCHITECTURE.md` §17.3 (client recovery), §17.4 (zero-Internet)
- `src/Animora.Persistence.Sqlite/Context/*` (all), `src/Animora.Persistence.Sqlite/Options/LocalStoreOptions.cs`
- `src/Animora.Infrastructure.Local/README.md`
- `src/Animora.Domain/Time/IClock.cs`, `src/Animora.Domain/Time/IMonotonicClock.cs`

## Goal
Make the local store production-safe: a real `IStoreKeyProvider` backed by the OS keystore behind an
encryption provider seam (with an explicit dev fallback), a loud startup assertion that native SQLite
is ≥ 3.50.2, WAL/checkpoint/busy-timeout tuning, crash-recovery verification, and a
backup/restore + corrupt-database quarantine-and-rebuild path.

## Design decisions (pre-made — implement, do not re-derive)
- **Folders:** `src/Animora.Infrastructure.Local/` → `Security/`, `Time/`, `Storage/`,
  `Startup/`, `Options/`; `src/Animora.Persistence.Sqlite/Maintenance/`.
- `Infrastructure.Local/Security/ISecretStore.cs` — `interface ISecretStore`:
  `Result<byte[]> TryRead(string name)`, `Result Write(string name, byte[] value)`,
  `Result Delete(string name)`. Names are ASCII keys such as `"local-db-key"`.
- Implementations (chosen by `OperatingSystem.IsWindows()/IsMacOS()`, registered once):
  - `Security/DpapiSecretStore.cs` — Windows. `System.Security.Cryptography.ProtectedData` with
    `DataProtectionScope.CurrentUser`; the protected blob is stored at
    `%LOCALAPPDATA%/Animora/secrets/<name>.bin` with file permissions restricted to the user.
  - `Security/KeychainSecretStore.cs` — macOS. `security` CLI invocation
    (`add-generic-password`/`find-generic-password -w`) under service name `Animora`; no P/Invoke,
    no third-party package.
  - `Security/FileFallbackSecretStore.cs` — dev only. Plaintext file under the data directory, used
    **only** when `LocalStoreOptions.AllowInsecureKeyFallback == true`; it logs a warning on every
    read and `LocalStoreDiagnostics.KeySource` reports `InsecureFallback`.
- `Security/LocalDbKeyService.cs` — `sealed class LocalDbKeyService`:
  `Result<string> GetOrCreatePassphrase()`. First run generates 32 bytes from
  `RandomNumberGenerator`, base64-encodes them, writes via `ISecretStore`, and returns them; later
  runs read. A failed read with `EncryptionEnabled == true` and no fallback returns
  `ErrorCodes.StoreKeyUnavailable` and the app **refuses to open the store** (fail closed,
  CONVENTIONS §7).
- **Encryption provider seam** (gated by STACK.md §12 K2 — the phase ships the seam, not the verdict):
  `Persistence.Sqlite/Context/IStoreEncryptionProvider.cs` —
  `void Apply(SqliteConnection connection, string passphrase)`.
  - `Context/NoEncryptionProvider.cs` — no-op; the default while `EncryptionEnabled == false`.
  - `Context/MultipleCiphersEncryptionProvider.cs` — executes
    `PRAGMA key = '<passphrase>'` then `PRAGMA cipher_page_size` immediately after open, and verifies
    with `SELECT count(*) FROM sqlite_master` (a wrong key fails here). **No package pin is added in
    this phase** — `SQLitePCLRaw.bundle_e_sqlite3mc` is pinned only by the phase that records a
    passing K2 (E08). Until then the provider is compiled but unreachable because
    `EncryptionEnabled` defaults to `false` and `LocalStoreOptions.Validate()` rejects
    `EncryptionEnabled == true` while `K2Approved == false`.
  - `LocalStoreOptions` gains: `bool AllowInsecureKeyFallback` (default `false`),
    `bool K2Approved` (default `false`), `int WalCheckpointPages` (default `1000`),
    `string BackupDirectory` (default `<data>/backups`), `int BackupRetentionCount` (default `5`).
- `Startup/NativeSqliteAssertion.cs` — `static Result Assert(SqliteConnection open)`:
  executes `SELECT sqlite_version();`, parses `major.minor.patch`, and returns
  `ErrorCodes.NativeSqliteTooOld` (detail = the actual version) when below **3.50.2**. Called by
  `LocalStoreStartup` **before** any migration. A failure is **loud**: the desktop shows a blocking
  fatal dialog and exits — never a silent degrade (T-RULE 004).
- `Startup/LocalStoreStartup.cs` — `sealed class LocalStoreStartup` with
  `Task<Result<LocalStoreDiagnostics>> InitializeAsync(CancellationToken ct)` executing, in order:
  1. ensure the data directory exists with user-only permissions;
  2. resolve the passphrase (when `EncryptionEnabled`);
  3. open a connection (provider applies the key), run `NativeSqliteAssertion.Assert`;
  4. `PRAGMA quick_check` → on failure jump to the quarantine path (below);
  5. `MigrateAsync()`;
  6. `PRAGMA wal_checkpoint(TRUNCATE)` when the `-wal` file exceeds `WalCheckpointPages` pages;
  7. `LocalSeeder.SeedAsync`;
  8. return `LocalStoreDiagnostics`.
  **It never blocks first frame**: the desktop calls it as a background task and gates only
  data-bound screens on its completion (PERFORMANCE.md §12; the gate itself is consumed from C01).
- `Startup/LocalStoreDiagnostics.cs` — `sealed record LocalStoreDiagnostics(string DatabasePath,
  string NativeSqliteVersion, bool EncryptionActive, KeySource KeySource, string SchemaVersion,
  bool RecoveredFromCrash, bool Quarantined, long DatabaseBytes, DateTimeOffset? LastBackupUtc)`.
  `enum KeySource { None, OsKeystore, InsecureFallback }`.
- `Maintenance/ILocalStoreMaintenance.cs` + `Maintenance/LocalStoreMaintenance.cs`:
  - `Task<Result<string>> BackupAsync(CancellationToken ct)` — uses SQLite's online backup API
    (`SqliteConnection.BackupDatabase`) into
    `<BackupDirectory>/animora-<yyyyMMdd-HHmmss>.db`, then `quick_check`s the copy and prunes to
    `BackupRetentionCount`. A backup of an encrypted DB stays encrypted.
  - `Task<Result> RestoreAsync(string backupPath, CancellationToken ct)` — validates the file,
    closes all contexts, moves the current DB to `quarantine/`, copies the backup into place,
    re-runs `InitializeAsync`.
  - `Task<Result<LocalStoreDiagnostics>> QuarantineAndRebuildAsync(CancellationToken ct)` —
    the corrupt-DB path: move `animora.db`, `-wal`, `-shm` into
    `<data>/quarantine/<timestamp>/`, create a fresh database, migrate, seed, and report
    `Quarantined = true`. **Never deletes the corrupt file** (it may be recoverable by support).
  - `Task<Result<int>> VacuumAsync(CancellationToken ct)` — `VACUUM` + `wal_checkpoint(TRUNCATE)`,
    to be scheduled off the startup path.
- `Time/SystemClock.cs` — `sealed class SystemClock : IClock` over `TimeProvider.System`.
  `Time/MonotonicClock.cs` — `sealed class MonotonicClock : IMonotonicClock`: `ElapsedTicks` from
  `Stopwatch.GetTimestamp()`; `HighWaterUtc` persisted in a `[LocalOnly] ClockHighWater` row
  (single row) and updated only when `Observe(serverUtc)` receives a **greater** value —
  a rollback of the OS clock can never lower it (A-RULE 011; consumed by D11/E07).
- `Storage/LocalFileStore.cs` — `sealed class LocalFileStore : ILocalFileStore` (port in
  `Application/Ports`): content-addressed storage under `<data>/files/<sha256[0..2]>/<sha256>`,
  `Task<Result<string>> PutAsync(Stream, CancellationToken)` returning the hash,
  `Task<Result<Stream>> OpenAsync(string hash, ...)`, `Task<Result> DeleteAsync(string hash, ...)`.
  Metadata rows are `DocumentReference` (A04). The server-side lifecycle is D12's.
- **New local-only entities + migration `003_LocalHardening`:**
  `[LocalOnly] LocalInstallation` (`InstallationId` ULID, `TenantId`, `MachineFingerprintHash`,
  `CreatedAtUtc`, `SchemaVersion`) — one row, created on first run; `SingleTenantContext` reads its
  `TenantId` from here. `[LocalOnly] ClockHighWater` (`HighWaterUtc`, `UpdatedAtUtc`).
  `[LocalOnly] StoreMaintenanceLog` (`Id`, `Kind`, `OccurredAtUtc`, `Detail`) — append-only record of
  backup/restore/quarantine/vacuum events.
  `MachineFingerprintHash` = SHA-256 of `machine name + OS install marker + primary volume serial`,
  computed by `Security/InstallationIdentity.cs`; it is the anti-cloning anchor D11/E07 bind to.
- **New error codes:** `STORE_KEY_UNAVAILABLE`, `NATIVE_SQLITE_TOO_OLD`, `STORE_CORRUPT`,
  `STORE_BACKUP_FAILED`, `STORE_RESTORE_FAILED`, `STORE_ENCRYPTION_NOT_APPROVED`.
- **Zero-Internet:** nothing in this phase performs a network call. The macOS keychain path shells out
  to a local binary only.

## Files to create / modify
| Path | Action | Contents (one line) |
|---|---|---|
| `src/Animora.Infrastructure.Local/Security/ISecretStore.cs` | create | keystore port |
| `src/Animora.Infrastructure.Local/Security/DpapiSecretStore.cs` | create | Windows DPAPI implementation |
| `src/Animora.Infrastructure.Local/Security/KeychainSecretStore.cs` | create | macOS keychain implementation |
| `src/Animora.Infrastructure.Local/Security/FileFallbackSecretStore.cs` | create | gated dev fallback |
| `src/Animora.Infrastructure.Local/Security/LocalDbKeyService.cs` | create | passphrase create/read, fail closed |
| `src/Animora.Infrastructure.Local/Security/InstallationIdentity.cs` | create | machine fingerprint hash |
| `src/Animora.Infrastructure.Local/Time/SystemClock.cs` | create | `IClock` implementation |
| `src/Animora.Infrastructure.Local/Time/MonotonicClock.cs` | create | monotonic + persisted high-water |
| `src/Animora.Infrastructure.Local/Storage/LocalFileStore.cs` | create | content-addressed local files |
| `src/Animora.Infrastructure.Local/Startup/NativeSqliteAssertion.cs` | create | `sqlite_version() >= 3.50.2` |
| `src/Animora.Infrastructure.Local/Startup/LocalStoreStartup.cs` | create | 8-step initialization |
| `src/Animora.Infrastructure.Local/Startup/LocalStoreDiagnostics.cs` | create | diagnostics record + `KeySource` |
| `src/Animora.Persistence.Sqlite/Context/IStoreEncryptionProvider.cs` | create | encryption seam |
| `src/Animora.Persistence.Sqlite/Context/NoEncryptionProvider.cs` | create | default no-op provider |
| `src/Animora.Persistence.Sqlite/Context/MultipleCiphersEncryptionProvider.cs` | create | K2-gated cipher provider |
| `src/Animora.Persistence.Sqlite/Context/SqliteConnectionInterceptor.cs` | modify | call the real key provider + provider |
| `src/Animora.Persistence.Sqlite/Options/LocalStoreOptions.cs` | modify | 5 new options + `Validate()` |
| `src/Animora.Persistence.Sqlite/Maintenance/ILocalStoreMaintenance.cs` | create | backup/restore/quarantine/vacuum port |
| `src/Animora.Persistence.Sqlite/Maintenance/LocalStoreMaintenance.cs` | create | implementation over the backup API |
| `src/Animora.Persistence.Sqlite/Entities/LocalInstallation.cs` | create | local-only installation row |
| `src/Animora.Persistence.Sqlite/Entities/ClockHighWater.cs` | create | monotonic high-water row |
| `src/Animora.Persistence.Sqlite/Entities/StoreMaintenanceLog.cs` | create | append-only maintenance log |
| `src/Animora.Persistence.Sqlite/Context/SingleTenantContext.cs` | modify | read `TenantId` from `LocalInstallation` |
| `src/Animora.Persistence.Sqlite/Migrations/*` | create | `003_LocalHardening` + snapshot |
| `src/Animora.Domain/Results/ErrorCodes.cs` | modify | append the 6 codes |
| `src/Animora.Application/Ports/ILocalFileStore.cs` | create | file-store port |
| `src/Animora.Persistence.Sqlite/Context/LocalStoreServiceCollectionExtensions.cs` | modify | register keystore/clocks/startup/maintenance/providers |

**No test files in this phase.** All tests are authored in **A12**; the "Test coverage owed to A12"
section below records exactly what A12 must cover.

## Implementation steps
1. Add `ISecretStore` + the three implementations + `LocalDbKeyService` + `InstallationIdentity`.
2. Add `SystemClock` and `MonotonicClock` (high-water persisted through a short-lived context from
   `IDbContextFactory`).
3. Add the encryption seam: port, no-op provider, cipher provider, and `LocalStoreOptions.Validate()`
   rejecting `EncryptionEnabled && !K2Approved` with `STORE_ENCRYPTION_NOT_APPROVED`.
4. Rewrite `SqliteConnectionInterceptor` to apply, in order: key (via provider) → pragmas.
5. Add the three local-only entities + their configurations; generate `003_LocalHardening`.
6. Point `SingleTenantContext` at `LocalInstallation`, creating the row on first run.
7. Add `NativeSqliteAssertion`, then `LocalStoreStartup` implementing the 8 steps exactly, writing a
   `StoreMaintenanceLog` row for every recovery/quarantine/backup action.
8. Add `ILocalStoreMaintenance` + `LocalStoreMaintenance` (backup, restore, quarantine, vacuum) and
   `LocalFileStore` + its port.
9. Extend `AddAnimoraLocalStore` with every new registration; the OS-specific `ISecretStore` is chosen
   at registration time.
10. Append the 6 error codes.
11. **No tests here.** Record the coverage owed to A12 (below).
12. Lightweight build check only, if needed: `build.sh full` = 0/0. Do **not** run `test.sh`.
    `run.sh` = `SMOKE RUN OK`.

## Test coverage owed to A12 (author + run in A12, not here)
- `NativeSqliteVersionTests`: `SELECT sqlite_version()` returns ≥ 3.50.2 in the test host;
  `NativeSqliteAssertion.Assert` returns `NATIVE_SQLITE_TOO_OLD` for a stubbed lower version.
- `StoreStartupTests`: the eight steps execute in order (step log); after init `journal_mode=wal`,
  `synchronous=NORMAL`, `busy_timeout=5000`, `foreign_keys=ON`; `EncryptionEnabled=true` + no key + no
  fallback → `STORE_KEY_UNAVAILABLE` and no `animora.db` created; `EncryptionEnabled=true` +
  `K2Approved=false` → `Validate()` returns `STORE_ENCRYPTION_NOT_APPROVED`.
- `BackupRestoreTests`: backup → `quick_check` OK → restore → pre-backup row set intact; retention
  keeps exactly `BackupRetentionCount` files; a `StoreMaintenanceLog` row per action.
- `QuarantineRebuildTests`: a corrupt DB (header bytes overwritten) is moved (not deleted) into
  `quarantine/<timestamp>/` with its `-wal`/`-shm`; a fresh migrated+seeded DB is created; diagnostics
  report `Quarantined = true`.
- `MonotonicClockTests`: `Observe` with an earlier timestamp does not lower `HighWaterUtc`; the value
  survives a context restart.

## Contracts / schema added
- Ports: `ISecretStore`, `IStoreEncryptionProvider`, `ILocalStoreMaintenance`, `ILocalFileStore`.
- Implementations: `DpapiSecretStore`, `KeychainSecretStore`, `FileFallbackSecretStore`,
  `LocalDbKeyService`, `InstallationIdentity`, `SystemClock`, `MonotonicClock`, `LocalFileStore`,
  `NoEncryptionProvider`, `MultipleCiphersEncryptionProvider`, `LocalStoreMaintenance`.
- Startup: `NativeSqliteAssertion`, `LocalStoreStartup`, `LocalStoreDiagnostics`, `KeySource`.
- Entities: `LocalInstallation`, `ClockHighWater`, `StoreMaintenanceLog` (all `[LocalOnly]`).
- Migration `003_LocalHardening`. `ErrorCodes` += 6. `LocalStoreOptions` += 5 options + `Validate()`.
- No `Animora.Contracts` change, no DI change in `App.axaml.cs`. **No test files** (owed to A12).

## Rules that apply here
- **T-RULE 004 / STACK.md §12** — the native SQLite version is asserted at runtime; a managed package
  version is never accepted as proof, and a failure is fatal and visible.
- **STACK.md §12 K2** — encryption is behind a provider seam and cannot be switched on until the gate
  is recorded (`K2Approved`), with an explicit unencrypted dev fallback flag.
- **CONVENTIONS §7** — the key lives in the OS keystore; the insecure fallback is opt-in, logged, and
  reported in diagnostics; a missing key fails closed.
- **A-RULE 011** — `MonotonicClock.HighWaterUtc` never decreases; it is the clock-rollback defense
  D11/E07 rely on.
- **A-RULE 016 / R-B27** — no network call exists in any path added here.
- **PERFORMANCE.md §12** — `InitializeAsync` runs off the first-frame path.
- **T-RULE 015** — if a `NuGetAuditSuppress` is unavoidable for the SQLite native line, it is dated,
  justified and owned; never permanent.

## Out of scope
- **All tests for this phase's behavior → A12** (this phase writes and runs none). A12 is the next
  phase and consolidates every Track A test.
- Recording the K2 verdict and pinning the encrypted bundle → E08 (gates) and its follow-up.
- Server-side key material and Data Protection → D01.
- License/entitlement use of `HighWaterUtc` and `MachineFingerprintHash` → D11, E07.
- The server file lifecycle and valet keys → D12.
- Scheduling `VacuumAsync`/`BackupAsync` in the app → B05 (diagnostics/maintenance hooks).
- Showing diagnostics in the UI → C10 (Profile) and B05 (diagnostics bundle).

## Acceptance criteria
- Build (lightweight, only if run): `scripts/sandbox/build.sh full` = 0 warnings / 0 errors.
- **No test suite is run in this phase** (`test.sh` is A12's; do not invoke it here).
- The "Test coverage owed to A12" section is present and names every behavior A12 must assert.
- `grep -rn "Http\|Socket\|WebClient" src/Animora.Infrastructure.Local` returns nothing.
- Runnability (`CONVENTIONS.md` §11): `scripts/sandbox/run.sh` = `SMOKE RUN OK`; the desktop still runs
  on mocks.

## PROGRESS.md update
Mark A11 done; advance "Next phase" to `A12`; append a "Repo state" entry listing: the keystore
implementations, `LocalDbKeyService`, the encryption seam + `K2Approved` gate, the native-SQLite
assertion, `LocalStoreStartup`'s 8 steps, `LocalStoreDiagnostics`, maintenance/backup/quarantine paths,
`MonotonicClock` high-water, `LocalFileStore`, the 3 local-only entities, migration
`003_LocalHardening`, `ErrorCodes` += 6, **no tests written (owed to A12)**, no DI change, no mock
deleted, deviations.
