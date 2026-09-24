# Phase A11b — Encryption seam, connection interceptor, local-only entities, migration 003 (session 2/4 of A11)

**Track:** A · **Depends on:** A11a ✅ · **Next:** A11c

## Session rules
- Read ONLY this file + "Read only" files. No other phase files, UI, mocks, tests.
- Rules: STACK.md §12 K2 summarized here — encryption ships as a seam only; it cannot be switched on until K2 is recorded (E08).
- **No tests / no `test.sh`.** **No package pin** for `SQLitePCLRaw.bundle_e_sqlite3mc` (E08 only). Never edit migrations 001/002.

## Read only
`Persistence.Sqlite/Context/{SqliteConnectionInterceptor,SingleTenantContext,ILocalTenantContext,LocalStoreServiceCollectionExtensions,AnimoraLocalDbContext,LocalDbContextFactory}.cs`, `Options/LocalStoreOptions.cs`, `Seed/SeedMarker.cs` (as a pattern for a `[LocalOnly]` entity + config), `Domain/Primitives/LocalOnlyEntity.cs`.

## Tasks
1. `Context/IStoreEncryptionProvider.cs` — `void Apply(SqliteConnection connection, string passphrase)`.
2. `Context/NoEncryptionProvider.cs` — no-op; default while `EncryptionEnabled == false`.
3. `Context/MultipleCiphersEncryptionProvider.cs` — right after open: `PRAGMA key = '<passphrase escaped: ' → ''>'`, `PRAGMA cipher_page_size = 4096`, then verify `SELECT count(*) FROM sqlite_master` (wrong key fails → throw with `STORE_KEY_UNAVAILABLE`). Compiled but unreachable: selected only when `EncryptionEnabled && K2Approved`.
4. **Rewrite `SqliteConnectionInterceptor`** (sync `ConnectionOpened` + async): order = (a) when `EncryptionEnabled`: key from `IStoreKeyProvider` → `IStoreEncryptionProvider.Apply`; (b) pragmas `journal_mode=WAL`, `synchronous=NORMAL`, `busy_timeout=<clamped BusyTimeoutMs>`, `foreign_keys=ON`. No key available → throw (fail closed, never an unkeyed open). Make the interceptor and its dependencies **singleton-safe** (no scoped deps) so it fits the A10b `IDbContextFactory`.
5. In `AddAnimoraLocalStore`: call `options.Validate()` at context-options build time and throw on failure (so `EncryptionEnabled && !K2Approved` can never open a store); register `IStoreEncryptionProvider` = `NoEncryptionProvider` unless `EncryptionEnabled && K2Approved`.
6. **Entities** in `Persistence.Sqlite/Entities/` + configurations (all `[LocalOnly]`, UTC ticks, bounded strings):
   - `LocalInstallation` — `InstallationId` (ULID, key), `TenantId`, `MachineFingerprintHash` (TEXT 64), `CreatedAtUtc`, `SchemaVersion`; single row.
   - `ClockHighWater` — fixed key (single row), `HighWaterUtc`, `UpdatedAtUtc`.
   - `StoreMaintenanceLog` — `Id` (ULID), `Kind` (int enum `Backup, Restore, Quarantine, Vacuum, CrashRecovery, Checkpoint`), `OccurredAtUtc`, `Detail` (bounded TEXT). Append-only: add it to `AppendOnlyGuard` protected types.
   - These rows are not tenant-filtered (installation-level) — exclude from the Tenant filter explicitly and say why in a comment.
7. **SingleTenantContext** reads `TenantId` from `LocalInstallation`: add `Persistence.Sqlite/Context/LocalInstallationBootstrap.cs` (`Task<Result<LocalInstallation>> EnsureAsync(string fingerprintHash, CancellationToken)`) that creates the row on first run with `TenantId = "T-LOCAL-DEFAULT"` and caches it in a singleton `LocalInstallationState`; `SingleTenantContext.TenantId` returns the cached value, falling back to the sentinel before bootstrap (seeding + filter must keep matching). No DB call inside the `TenantId` getter.
8. **Migration** `003_LocalHardening` via EF tooling (design-time factory) + snapshot; `001`/`002` files byte-identical.

## Gate
- `dotnet ef migrations list` → 001, 002, 003; `has-pending-model-changes` → none.
- `build.sh full` = 0/0.

## Handoff
PROGRESS: row A11b ✅ + note (seam, interceptor order, Validate wiring, 3 entities, bootstrap/tenant caching, migration 003); Next → `A11c`. Commit `feat(store): encryption seam, local hardening entities and 003_LocalHardening (A11b)`. Final answer: done / deviations / blockers.
