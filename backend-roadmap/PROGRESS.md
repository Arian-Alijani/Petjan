# Progress & Project State — Animora Backend & Server

> **Read this file first, every session. Update it at the end of every phase.** It is the single source
> of truth for "what already exists" and "what to do next". Never skip the update step.

## Standing principles (in force in EVERY phase, never traded off)

1. **Offline-first correctness.** The desktop must keep full functionality with the server unreachable,
   on a real local database. Local state may be provisional; it is never lost, never silently
   overwritten, and never invented. The server is the single global authority for settlement.
2. **Backend-ready clean layering.** Domain ← Application ← Infrastructure/Presentation, dependencies
   inward only. No EF Core / Datasync / SQL / HTTP type ever appears in a Desktop View or ViewModel. No
   client code in a server project or vice versa. (`CONVENTIONS.md` §1–§2.)
3. **Runnability.** Through the end of Track C, `scripts/sandbox/build.sh full` = 0 warnings / 0 errors
   and `scripts/sandbox/run.sh` = SMOKE RUN OK on a machine with no server, no SQL Server and no
   network. From Track D on, the desktop must still start and operate offline. (`CONVENTIONS.md` §11.)
4. **No mock left behind.** Every `Animora.Contracts` interface ends this roadmap with a real
   implementation; every `Mock*` file is deleted in the phase that replaces it. No bounded context ever
   keeps both. (`CONVENTIONS.md` §10; audited in C11.)

> These restate — never replace — `CONVENTIONS.md` and `ROADMAP.md`. If wording differs, those govern.

## Current state (authoring-time snapshot — no phase executed yet)

| Area | State |
|---|---|
| `src/Animora.Desktop/` | **Complete.** 17 screens, ~300 `.cs` files, shell/nav/dialogs/toasts/theme/localization. `App.axaml.cs` is the composition root (DI). Builds 0 warnings / 0 errors. |
| `src/Animora.Contracts/` | 20 interfaces + DTOs, one folder per bounded context. The frontend↔backend seam. |
| `src/Animora.Desktop/Mocks/` | 22 in-memory files (20 `Mock*` services + `AppointmentStore` + `VisitEncounterStore`) — the only implementations today. All sample data lives here. |
| `src/Animora.Domain/`, `src/Animora.Application/`, `src/Animora.Sync.Abstractions/` | Empty placeholders (csproj + README only). |
| Server / SQL Server / sync / auth / licensing / admin web | **Do not exist.** |
| Login | `src/Animora.Desktop/Pages/Login/LoginViewModel.cs` — hardcoded `admin`/`admin`; `TryAuthenticate` is the documented swap seam (replaced in B03). |
| `Directory.Packages.props` | Avalonia 12.1.2 (+Desktop/Fluent/Inter/DataGrid), CommunityToolkit.Mvvm 8.4.2, Microsoft.Extensions.DependencyInjection 10.0.12. `ManagePackageVersionsCentrally=true`. |
| `Directory.Build.props` | `net10.0`, `LangVersion 14.0`, `Nullable enable`, `ImplicitUsings enable`. |
| Solution | `Animora.sln` contains Contracts, Domain, Application, Sync.Abstractions, Desktop. |
| Scripts | `scripts/sandbox/build.sh [fast\|full\|release\|restore]`, `run.sh` (Xvfb smoke, prints `SMOKE RUN OK`), `install-prereqs.sh`, `doctor.sh`, `bench.sh`. `test.sh` is added by A01. |
| Frontend history | `windows-frontend-roadmap/PROGRESS.md` (phases 01–29 done). Its `phases/**` must never be read. |

## Next phase

**Next phase: `A12`** — `backend-roadmap/phases/PHASE-A12-*.md`

## Phase status

| ID | Status | Date | Notes |
|---|---|---|---|
| A01 | ✅ Done | 2026-09-23 | Backend track scaffold: 4 src + 5 test projects, 10 pins, WireContract v1, test.sh, LayeringRulesTests. Build 0/0, TESTS OK, SMOKE RUN OK. |
| A02 | ✅ Done | 2026-09-23 | Domain kernel: 15 files (primitives, Money/Quantity, ULID IdGenerator, clock ports, Result/ErrorCodes seed 11). 4 test classes, 24 domain tests. Build 0/0, TESTS OK, SMOKE RUN OK. |
| A03 | ✅ Done | 2026-09-23 | Identity spine: 11 files in `Domain/Identity` (Tenant, User, StaffRole, Permission, PermissionCodes 42, RoleDefinition, DefaultRolePermissions, StaffMembership, Device, CapabilitySnapshot, OfflineCapabilityPolicy). ErrorCodes += 6. 3 test classes (+21 facts → 45). Build 0/0, TESTS OK, SMOKE RUN OK. |
| A04 | ✅ Done | 2026-09-23 | Owners/patients/clinical core: 12 files (`Domain/Patients` 4, `Domain/Clinical` 7, `Events/ClinicalEvents.cs`). Visit state machine + invoice-handoff lines, derived vaccination due dates, append-only medical history. ErrorCodes += 7 (→ 24). 4 test classes (+45 facts → 90). Build 0/0, TESTS OK, SMOKE RUN OK. |
| A05 | ✅ Done | 2026-09-23 | Scheduling & boarding domain: 12 files (`Domain/Scheduling` 5, `Domain/Boarding` 5, `Events/SchedulingEvents.cs`, `Events/BoardingEvents.cs`). Appointment state machine + `SlotConflictPolicy` (pure, `for`-loop), reception derived-queue, boarding stay state machine + `BillableDays` + invoice-required checkout. ErrorCodes += 11 (→ 35). 8 events. **No tests (owed to A12).** Build: Domain 0/0 (solution-wide OOM only in Avalonia Desktop XAML AOT — sandbox RAM cap, not code). |
| A06 | ✅ Done | 2026-09-23 | Inventory & purchasing domain: 13 files (`Domain/Inventory` 6, `Domain/Purchasing` 5, `Events/InventoryEvents.cs`, ErrorCodes). **Movement-based stock only** — `Product` has no on-hand property; `StockCalculator` is the single derivation (signed sum + moving weighted average); `StockMovement` is append-only with a factory-enforced sign rule; `StockLevel` is a rebuildable projection. PO state machine + `RecalculateTotals`/`ApplyReceipt`; posted `GoodsReceipt` immutable (A-RULE 002). ErrorCodes += 11 (→ 46). 4 events. **No tests (owed to A12).** Build full 0/0, SMOKE RUN OK. |
| A07 | ✅ Done | 2026-09-23 | Sales & cashier domain: 9 new files (`Domain/Sales` 6, `Domain/Cashier` 2, `Events/SalesEvents.cs`) + ErrorCodes. `Invoice` aggregate with the 6-state machine and **immutability after posting** (all 5 mutators → `INVOICE_IMMUTABLE`, A-RULE 002); `InvoiceTotals` is the single totals algorithm (invoice discount pro-rated by weighted largest-remainder before tax, `Money.ApplyPercent` the only rounding); `Payment` append-only with factory-validated method references and `OperationId` idempotency (`PAYMENT_DUPLICATE_OPERATION`, A-RULE 006); `InvoiceAdjustment` the only post-posting economic change path; `SettlementStatus` (8 states, §8.3) defined once; `CashSession` open/close invariants + signed `CashMovement` factory. ErrorCodes += 14 (→ 60). 6 events. **No tests (owed to A12).** Build full 0/0, SMOKE RUN OK. |
| A08 | ✅ Done | 2026-09-23 | Finance domain: 11 new files (`Domain/Finance` 10, `Events/FinanceEvents.cs`) + ErrorCodes. Append-only balanced `JournalEntry` (**no mutator at all** — every property `private init`; corrections only via `CreateReversal`, A-RULE 002/003 · R-B22); `ChartOfAccounts` seeds the 20 authoritative accounts behind `AccountCodes` constants (no code literal anywhere else); `PostingRules` is the single event → journal translation (10 `For<Event>` methods covering all 14 tabled rows), every one delegating to `JournalEntry.Create` so balance is structural; `AccountingPeriod` forward-only `Open→Closing→Closed→Locked` with 3 close blockers + `EnsurePostable`; `Cheque` 8-state machine with append-only `ChequeEvent` history and the counterparty invariant; `Expense` one-way `Post` + 9 seeded categories → 6000/6100. ErrorCodes += 12 (→ 72). 6 events. **No tests (owed to A12).** Build full 0/0, SMOKE RUN OK. |
| A09 | ✅ Done | 2026-09-23 | Local SQLite context, conventions, converters, 18 mappings + migration `001_LocalCoreSchema`; named filters; no tests (A12). |
| A10 | ✅ Done | 2026-09-24 | A10a–A10d complete; see Repo state log "A10". |
| A10a | ✅ Done | 2026-09-24 | 8 child rows → relational tables (PK OwnerId+childKey, cascade FK, field nav, AutoInclude) via reflection-free `OwnedRows.Map/Row` + `ApplyOwnedScalarConventions`; Money→`<N>Minor`/`<N>Currency`, Quantity→`<N>Unit` + `<N>Scaled` INTEGER×1000 (`FixedScaleQuantityConverter`; StockMovement, PO/GR/Invoice lines) or `<N>Value` TEXT (ReorderLevel). Indexes: JournalEntries EntryNumber unique, Products Sku unique filtered, +IX_JournalLines_AccountCode; no category-name lookup → no index. StockLevel ignored. Deviation: children are EF dependent entities (EF10 owned types can't hold complex props); domain: private parameterless materialization ctor on 7 child records. dotnet-ef 10.0.12 at `.sandbox/tools`. No migration (A10d). |
| A10b | ✅ Done | 2026-09-24 | Guard: `AppendOnlyGuard.Check` runs first in `PrepareSave()` shared by `SaveChanges(bool)`/`SaveChangesAsync(bool,ct)` overrides (covers all overloads), before tenant check/stamping; blocks Modified/Deleted of the 6 types incl. child rows, diagnostics = type + PK original values; Added children allowed. UoW: `SaveAsync` = own transaction when none active (inside `ExecuteAsync` only flushes); `ExecuteAsync` = one tx, nesting throws; events snapshotted before save, ordered by `OccurredAtUtc` (stable), dispatched in-tx, handler writes saved, commit, then `ClearDomainEvents`; any failure → rollback (tx dispose), events kept, `ChangeTracker.Clear()`. `DbUpdateConcurrencyException`→ConcurrencyConflict, Sqlite/DbUpdate(Sqlite)→StoreUnavailable on begin/save/commit; OCE/other exceptions propagate. Dispatcher: no `dynamic`; `services.AddDomainEventHandler<TEvent,THandler>()` → singleton `DomainEventHandlerRegistration` → singleton `DomainEventHandlerMap` (Type→invoker[]), handlers resolved from scope in order, short-circuit. DI: 18 repos + UoW + dispatcher scoped, idempotent; `AddDbContextFactory(..., ServiceLifetime.Scoped)` is the single config source (also registers the scoped context) — scoped factory so the scoped interceptor/tenant are never captured by a singleton. No repo lookups added. |
| A10c | ✅ Done | 2026-09-24 | `Seed/`: `[LocalOnly] SeedMarker` (SeedKey unique `IX_SeedMarkers_SeedKey`, SchemaVersion=1) + `LocalSeeder.SeedAsync` — per block: indexed marker check → rows + marker in one `IUnitOfWork.ExecuteAsync` tx; concurrent loser rolls back and succeeds if the winner's marker exists; failed factory `Result`s propagate (all-or-nothing per block). Marker keys/blocks: `identity.tenant` (T-LOCAL-DEFAULT), `identity.permissions` (full `PermissionCodes.CreateCatalog()`), `identity.roles` (5 `RoleDefinition.CreateSystem` from `DefaultRolePermissions`), `finance.chart` (20 `ChartOfAccounts.Default`), `finance.period.initial` (1 open period), `inventory.units` (PCS BOX ML MG G KG DOSE), `inventory.categories` (root MED VAC FOOD ACC SRV), `boarding.rooms` (R-01…R-08, cap 1; R-07 Isolation, R-08 Vip). Tenant bootstrap: new narrow `Tenant.CreateLocalDefault(displayName)` + `Tenant.LocalDefaultId` (reserved exception: Id=TenantId=sentinel); seeder refuses unless tenant context = sentinel. Catalog decision: `ExpenseCategories`/`AppointmentTypeCatalog`/`VaccineSchedule` stay **static** (domain is sole authority, referenced by opaque code, no FK) — no DB rows. Period code: `ISeedPeriodCodeProvider` → `PersianMonthPeriodCodeProvider` (singleton; `PersianCalendar` month in Asia/Tehran, code `yyyy-MM` Jalali, real UTC bounds from injected `IClock`; fallback fixed +03:30 zone). `LocalSeeder` scoped in `AddAnimoraLocalStore`; not called from desktop. Gate: build 0/0, `dotnet ef dbcontext info` OK (SeedMarkers in model). No migration (A10d), no tests. |
| A10d | ✅ Done | 2026-09-24 | `Queries/`: `QueryPaging.ToPagedAsync` (page≥1, size clamped `[1,min(MaxPageSize,200)]`, requires OrderBy, COUNT + bounded select; skips select past end), `StockOnHandQuery` (GROUP BY ProductId,VariantId SUM `QuantityScaled`), `InvoiceBalanceQuery` (one SELECT: invoice + correlated SUM payments/adjustments + currency-mismatch counts → throws), `LedgerBalanceQuery` (Entries⨝Lines GROUP BY PeriodId,AccountCode,Currency; debit/credit by `Side`). Bounded id inputs (500/500/60), AsNoTracking, tenant filter kept; 3 queries scoped in DI. Migration `20260924054447_002_LocalBusinessSchema` (24 CreateTable, 39 CreateIndex, no Alter/Drop in Up); 001 untouched. |
| A11 | ✅ Done | 2026-09-24 | A11a–A11d complete; see Repo state log "A11". |
| A11a | ✅ Done | 2026-09-24 | `Infrastructure.Local/Security`: `ISecretStore` + DPAPI (win) / Keychain via `/usr/bin/security` (mac) / dev `FileFallbackSecretStore` (opt-in, warns every read); no OS store + no fallback → fail-closed store. Pin `System.Security.Cryptography.ProtectedData` 10.0.12. `LocalDbKeyService` (fail-closed: non-NotFound read failure → `STORE_KEY_UNAVAILABLE`; `GetKey()` throws) replaces `NullStoreKeyProvider`. `KeySource` in `Persistence.Sqlite/Maintenance`. `InstallationIdentity`. 6 store error codes; `LocalStoreOptions` += fallback/K2/WAL/backup + `Validate()`/`DataDirectory`. DI: `AddAnimoraLocalInfrastructure`. build full 0/0; no tests (A12). |
| A11b | ✅ Done | 2026-09-24 | Cipher seam `IStoreEncryptionProvider`: `NoEncryptionProvider` (default) / `MultipleCiphersEncryptionProvider` (`PRAGMA key` escaped, `cipher_page_size=4096`, `sqlite_master` verify → `STORE_KEY_UNAVAILABLE`), selected only when `EncryptionEnabled && K2Approved`; no cipher package pin (E08). `SqliteConnectionInterceptor` rewritten sync+async: (a) key via `IStoreKeyProvider` → `Apply` when encryption on, missing key → throw (fail closed); (b) WAL/NORMAL/clamped busy_timeout/foreign_keys. Interceptor + deps now singletons. `AddAnimoraLocalStore`: `options.Validate()` at context-options build → throw; `IStoreKeyProvider` Null via TryAdd. Entities (`Entities/`, `[LocalOnly]`, not tenant-filtered — installation-level, commented): `LocalInstallation` (PK InstallationId ULID, fingerprint hash TEXT 64), `ClockHighWater` (fixed Id=1), `StoreMaintenanceLog` (Kind int enum, Detail ≤400; added to `AppendOnlyGuard`). `LocalInstallationBootstrap.EnsureAsync` creates row (TenantId `T-LOCAL-DEFAULT`) → singleton `LocalInstallationState`; `SingleTenantContext` reads cache, sentinel fallback, no DB call. Migration `003_LocalHardening` (3 tables + IX_StoreMaintenanceLogs_OccurredAtUtc); 001/002 byte-identical. Gate: ef list 001/002/003, no pending changes, build full 0/0. |
| A11c | ✅ Done | 2026-09-24 | `Infrastructure.Local/Startup/NativeSqliteAssertion`: `Assert(open)` runs `SELECT sqlite_version()`, `Parse` (strict `major.minor.patch`) — < 3.50.2 or unparsable → `NATIVE_SQLITE_TOO_OLD` (detail = actual version); T-RULE 004. `LocalStoreDiagnostics` record + `ILocalStoreInitializer` live in `Persistence.Sqlite/Maintenance/` (avoid Persistence↔Infrastructure cycle; initializer implemented in A11d, resolved lazily via `IServiceProvider`). `ILocalStoreMaintenance`/`LocalStoreMaintenance` (scoped, `AddAnimoraLocalStore`): fresh unpooled connections through new `SqliteConnectionInterceptor.Prepare` (same key + pragma path). Backup → `<BackupDirectory>/animora-<yyyyMMdd-HHmmss>[-n].db` via `BackupDatabase` (destination keyed ⇒ stays encrypted), `quick_check` (bad copy deleted → `STORE_BACKUP_FAILED`), prune to `BackupRetentionCount`; Restore validates exists+quick_check, quarantines current, copies, `InitializeAsync` → `STORE_RESTORE_FAILED` with original kept; QuarantineAndRebuild: `ClearAllPools`, move db/-wal/-shm to `<data>/quarantine/<stamp>/` (**never deletes**), re-entry flag (second corrupt → `STORE_CORRUPT`, no loop), `Quarantined=true`; Vacuum: `VACUUM` + `wal_checkpoint(TRUNCATE)`, returns page_count delta. Log kinds written: Backup/Restore/Quarantine/Vacuum. SqliteException(11/26)→`STORE_CORRUPT`, key failure→`STORE_KEY_UNAVAILABLE`, else op code; cancellation propagates. `Application/Ports/ILocalFileStore` + `Infrastructure.Local/Storage/LocalFileStore` (singleton): `<data>/files/<sha[0..2]>/<sha256>`, stream-to-temp (`files/.tmp`) while hashing, atomic move, dedupe; hash `^[0-9a-f]{64}$` else `VALIDATION_FAILED`; missing → `NOT_FOUND`. Gate: build full 0/0; network grep clean in sources (only implicit `System.Net.Http` global using in `obj/`). |
| A11d | ✅ Done | 2026-09-24 | `Infrastructure.Local/Time/SystemClock` (over `TimeProvider`, default System) + `MonotonicClock` (singleton; `Stopwatch.GetTimestamp`; lazy high-water from `ClockHighWaters` via own scope + `IDbContextFactory`; `Observe` under lock, memory + single-statement upsert `ON CONFLICT … WHERE HighWaterUtc < excluded` — never lowers; store-not-ready tolerated, retried). `Startup/LocalStoreStartup : ILocalStoreInitializer` (scoped) — 8 steps with internal `Steps` log (`InternalsVisibleTo Animora.Persistence.Tests`): dir 0700 / Windows current-user protected ACL + crash flag; `Validate` + passphrase before any file; open + `NativeSqliteAssertion`; `quick_check`/SqliteException 11/26 → `QuarantineAndRebuildAsync` (AsyncLocal re-entry guard → `STORE_CORRUPT`); `MigrateAsync`; WAL TRUNCATE when pages > `WalCheckpointPages` (log `Checkpoint`) + `CrashRecovery` log; bootstrap + seed; diagnostics. DI: `IClock`→`SystemClock`, `IMonotonicClock`, `LocalStoreStartup` + `ILocalStoreInitializer` (all TryAdd). Gate: build full 0/0, SMOKE RUN OK, network grep clean, ef list 001/002/003 + no pending changes; throwaway ValidateScopes/ValidateOnBuild provider + real init OK (native 3.50.4, dir mode 700, high-water not lowered). |
| B01 | ⬜ Not started | | |
| B02 | ⬜ Not started | | |
| B03 | ⬜ Not started | | |
| B04 | ⬜ Not started | | |
| B05 | ⬜ Not started | | |
| C01 | ⬜ Not started | | |
| C02 | ⬜ Not started | | |
| C03 | ⬜ Not started | | |
| C04 | ⬜ Not started | | |
| C05 | ⬜ Not started | | |
| C06 | ⬜ Not started | | |
| C07 | ⬜ Not started | | |
| C08 | ⬜ Not started | | |
| C09 | ⬜ Not started | | |
| C10 | ⬜ Not started | | |
| C11 | ⬜ Not started | | |
| D01 | ⬜ Not started | | |
| D02 | ⬜ Not started | | |
| D03 | ⬜ Not started | | |
| D04 | ⬜ Not started | | |
| D05 | ⬜ Not started | | |
| D06 | ⬜ Not started | | |
| D07 | ⬜ Not started | | |
| D08 | ⬜ Not started | | |
| D09 | ⬜ Not started | | |
| D10 | ⬜ Not started | | |
| D11 | ⬜ Not started | | |
| D12 | ⬜ Not started | | |
| D13 | ⬜ Not started | | |
| E01 | ⬜ Not started | | |
| E02 | ⬜ Not started | | |
| E03 | ⬜ Not started | | |
| E04 | ⬜ Not started | | |
| E05 | ⬜ Not started | | |
| E06 | ⬜ Not started | | |
| E07 | ⬜ Not started | | |
| E08 | ⬜ Not started | | |
| E09 | ⬜ Not started | | |

Status values: `⬜ Not started` · `🟨 In progress` · `✅ Done` · `⛔ Blocked (reason)`.

## Repo state log

### A11 — Local hardening: keystore, encryption seam, native assertion, startup, maintenance, clocks (2026-09-24)

- **Keystore (A11a):** `ISecretStore` → `DpapiSecretStore` (win), `KeychainSecretStore` (`/usr/bin/security`, mac), opt-in dev `FileFallbackSecretStore` (warns every read), fail-closed `UnavailableSecretStore` otherwise. `LocalDbKeyService` (32-byte random passphrase; non-NotFound read failure → `STORE_KEY_UNAVAILABLE`; `GetKey()` throws) replaces `NullStoreKeyProvider`. `InstallationIdentity` (SHA-256 fingerprint hash).
- **Encryption seam (A11b):** `IStoreEncryptionProvider` — `NoEncryptionProvider` default; `MultipleCiphersEncryptionProvider` only when `EncryptionEnabled && K2Approved` (else `Validate()` → `STORE_ENCRYPTION_NOT_APPROVED`). Interceptor applies key then WAL/NORMAL/busy_timeout/foreign_keys, sync + async + `Prepare`.
- **Native assertion (A11c):** `NativeSqliteAssertion` (≥ 3.50.2, else `NATIVE_SQLITE_TOO_OLD`, detail = actual).
- **Startup (A11d):** `LocalStoreStartup` 8 steps — (1) private data dir + crash flag, (2) `Validate` + key before any file, (3) open + native assert, (4) `quick_check` → quarantine/rebuild, (5) migrate, (6) WAL checkpoint/crash log, (7) installation bootstrap + seed, (8) `LocalStoreDiagnostics` (path, native version, encryption, `KeySource`, latest migration, recovered, quarantined, bytes, last backup). Never on UI thread; desktop wiring is C01/B05.
- **Maintenance (A11c):** backup (keyed, quick_check, retention), restore (quarantine current, re-init), quarantine-and-rebuild (move, never delete; no loop), vacuum; one `StoreMaintenanceLog` row per action.
- **Clocks (A11d):** `SystemClock`; `MonotonicClock` high-water persisted in `ClockHighWaters`, never decreases (A-RULE 011).
- **Files:** `LocalFileStore` (content-addressed sha256, atomic, dedupe) behind `Application/Ports/ILocalFileStore`.
- **Schema:** 3 local-only entities (`LocalInstallation`, `ClockHighWater`, `StoreMaintenanceLog`), migration `003_LocalHardening`; 001/002 unchanged. ErrorCodes +6 store/native codes.
- **DI:** `AddAnimoraLocalInfrastructure` (wraps `AddAnimoraLocalStore`) is the single entry point. **No Desktop DI change; no mock deleted.**
- **Deviations:** `LocalStoreDiagnostics`/`KeySource`/`ILocalStoreInitializer` live in `Persistence.Sqlite/Maintenance` (avoid Persistence↔Infrastructure cycle; initializer resolved lazily); entry point named `AddAnimoraLocalInfrastructure`; `System.Security.Cryptography.ProtectedData` pinned 10.0.12; `LocalStoreStartup` writes Checkpoint/CrashRecovery log rows via parameterized SQL (entity factory is internal to Persistence); `LocalDbContextFactory.DesignTimeClock` kept (design-time only).
- **A12 test debt:**
  - `NativeSqliteVersionTests`: runtime version ≥ 3.50.2; `Parse`/`Assert` → `NATIVE_SQLITE_TOO_OLD` for a lower version.
  - `StoreStartupTests`: 8 steps in order (step log); after init `journal_mode=wal`, `synchronous=NORMAL(1)`, `busy_timeout=5000`, `foreign_keys=1`; `EncryptionEnabled` + no key + no fallback → `STORE_KEY_UNAVAILABLE` and no `animora.db`; `EncryptionEnabled` + `!K2Approved` → `Validate()` = `STORE_ENCRYPTION_NOT_APPROVED`.
  - `BackupRestoreTests`: backup → quick_check ok → restore → pre-backup rows intact; retention keeps exactly `BackupRetentionCount`; one `StoreMaintenanceLog` row per action.
  - `QuarantineRebuildTests`: header-corrupted DB moved (not deleted) with `-wal`/`-shm` into `quarantine/<timestamp>/`; fresh migrated + seeded DB; `Quarantined = true`.
  - `MonotonicClockTests`: earlier `Observe` never lowers `HighWaterUtc`; value survives a new context.
  - `LocalFileStoreTests`: put/open/delete round trip, dedupe, invalid hash rejected.

### A10 — Local persistence II: business mappings, UoW, seed, read helpers, migration 002 (2026-09-24)

- **Files:** `Persistence.Sqlite/Conventions/OwnedRows.cs`, `Converters/FixedScaleQuantityConverter.cs`, `Context/AppendOnlyGuard.cs`, `Repositories/*` (18 repos, `LocalUnitOfWork`, `DomainEventDispatcher`), `Seed/*` (`LocalSeeder`, `SeedMarker`, `PersianMonthPeriodCodeProvider`), `Queries/{QueryPaging,QueryGuards,StockOnHandQuery,InvoiceBalanceQuery,LedgerBalanceQuery}.cs`, `Migrations/20260924054447_002_LocalBusinessSchema{,.Designer}.cs` + snapshot.
- **Contracts (Track C):** `QueryPaging.ToPagedAsync<T>(IQueryable<T>, page, pageSize, LocalStoreOptions, ct) → PagedResult<T>`; `StockOnHandQuery.GetAsync(ids) → IReadOnlyDictionary<(ProductId, VariantId), decimal>`; `InvoiceBalanceQuery.GetAsync(ids) → IReadOnlyDictionary<string, InvoiceBalance(InvoiceId, Currency, GrandTotalMinor, AdjustmentsMinor, PaidMinor, DueMinor)>` (Due = Grand − Paid − Adjustments); `LedgerBalanceQuery.GetAsync(periodIds) → IReadOnlyList<LedgerBalance(PeriodId, AccountCode, Currency, DebitMinor, CreditMinor, BalanceMinor)>`.
- **DI (`AddAnimoraLocalStore`):** scoped context factory + context, 18 repos, UoW, dispatcher (+ singleton handler map), `LocalSeeder`, singleton period-code provider, scoped `StockOnHandQuery`/`InvoiceBalanceQuery`/`LedgerBalanceQuery`.
- **Migration 002:** additive only — 16 aggregate tables (Accounting/Cash/Cheques/Expenses/GoodsReceipts/Invoices/Journal/Ledger/Payments/Categories/Products/POs/SeedMarkers/StockMovements/Suppliers/Units) + 8 owned-row tables (CashMovements, ChequeEvents, GoodsReceiptLines, InvoiceAdjustments, InvoiceLines, JournalLines, ProductVariants, PurchaseOrderLines), 39 indexes. Money = `<N>Minor INTEGER` + `<N>Currency TEXT`; summable quantities `<N>Scaled INTEGER`; no JSON money. `001_LocalCoreSchema` + Designer byte-identical.
- **Verification:** `dotnet ef migrations list` → 001, 002; `has-pending-model-changes` → none; `build.sh full` 0/0; `run.sh` SMOKE RUN OK (desktop still on mocks). One-off throwaway console (not committed) migrated a fresh DB and executed the 3 aggregates (SQL translation, no client eval).
- **Deviations:** InvoiceBalance is one SELECT with correlated sub-aggregates (payments + adjustments + currency checks) rather than a standalone GROUP BY, to keep one command per invoice batch; ledger groups also by currency (multi-currency safety). Paging skips the item query when the page is past the end (1 command).
- **No tests** (by phase rule). **A12 test debt:** schema/owned-row tables/indexes/round trips + no JSON money; append-only update/delete protection per protected type; transaction + handler-failure rollback, exactly-once in-transaction dispatch, mapped concurrency error; stock negative adjustment, signed partial payments/refunds, ledger debit−credit each from one SQL aggregate; paging bounds/count; idempotent double seed, tenant match, 20 accounts, all permissions/5 roles, 7 units, 5 categories, 8 rooms, 1 open period, catalog values.

### A09 — Local persistence I: DbContext and core mappings (2026-09-23)

- **Files:** `src/Animora.Persistence.Sqlite/{Options,Context,Converters,Conventions,Configurations/{Identity,Patients,Clinical,Scheduling,Boarding},Migrations}`; 18 entity configurations, the initial `001_LocalCoreSchema` migration and snapshot. Options, scoped DbContext, tenant/soft-delete named filters, UTC-ticks and invariant decimal converters, exact minor-unit Money columns, delta indexes, key-provider seam, and SQLite pragma interceptor are in place.
- **No tests written or run** in A09: migration application, 18 tables/indexes, no REAL/NUMERIC, converter round-trips, named-filter isolation, and foreign-tenant SaveChanges guard are owed to A12. No desktop DI changes; no mock deleted.
- **Deviation D-A09-1:** Immutable positional child records (`StayItem`, `VisitServiceLine`, etc.) have constructor parameters of complex value-object types (`Money`/`Quantity`), which EF Core cannot bind as owned JSON entities during model validation. They are instead persisted to the same aggregate JSON columns using field-backed converters and structural value comparers. A12 must verify child-collection materialization/change tracking. Role permission sets use JSON converters with structural comparers for the same reason. The `AppointmentSlot` property is derived and not stored separately; `StartUtc`/`DurationMinutes` are the persisted columns. Due-date index uses the domain's `NextDueUtc` column under the roadmap's requested `DueUtc` index name.


> Append one entry per completed phase, newest first. Required fields:
> **files added**, **contracts added**, **DI changes**, **mocks deleted**, **migrations created**,
> **tests added**, **verification** (build/test/smoke results), **deviations**.

### A08 — Domain: finance ledger, cheques, expenses (2026-09-23)

- **Files added (src, 11):**
  - `Finance/LedgerAccount.cs` — `sealed class LedgerAccount : AggregateRoot` (`Code`, `Name`,
    `AccountType Type`, `ParentAccountId?`, `IsPostable`, `IsActive`, `Description?`) +
    `enum AccountType { Asset, Liability, Equity, Revenue, Expense }`. `NormalBalance` is **derived**
    from `Type` (`Asset`/`Expense` → Debit, else Credit) through `static EntrySide
    NormalBalanceOf(AccountType)` — never a stored column that could drift. `Create` validates that
    the code is a numeric string (indexed `for`, no regex/LINQ).
  - `Finance/ChartOfAccounts.cs` — `static class AccountCodes` with the **20 `const string` codes**
    exactly as tabled (1000/1010/1020/1100/1110/1200/1900 · 2000/2010/2100/2200 · 3000 ·
    4000/4010/4020/4900 · 5000/6000/6100/6200) and `static class ChartOfAccounts` holding the
    authoritative seed as `ChartOfAccountsEntry(Code, Name, Type, IsPostable)` rows with
    `Default` / `Find` / `Contains` / `IsPostable` (ordinal dictionary lookup). Every later
    reference uses an `AccountCodes` constant; a numeric literal appears nowhere else.
  - `Finance/JournalLine.cs` — `sealed record JournalLine(LineId, AccountCode, EntrySide Side,
    Money Amount, Memo?, CostCenter?)` + `enum EntrySide { Debit, Credit }`. Direction lives in
    `Side`, **never** in the sign of `Amount`; `SignedMinor` and `Mirror(lineId)` are the balance
    check and the reversal building block.
  - `Finance/JournalEntry.cs` — `sealed class JournalEntry : AggregateRoot`, **append-only forever**:
    every property is `private init`, there is **no mutator, no delete, no business soft-delete**
    (A-RULE 002 / R-B22, ARCHITECTURE §8.5 — a ledger row therefore cannot produce a sync conflict
    by construction). `Create(...)` is the only constructor path and validates the whole double
    entry in **one `for` pass** (PERFORMANCE §2): `< 2` lines → `JOURNAL_TOO_FEW_LINES`, non-positive
    amount → `JOURNAL_LINE_AMOUNT_INVALID`, second currency → `JOURNAL_CURRENCY_MIXED`, empty
    account → `ACCOUNT_UNKNOWN`, default header account → `ACCOUNT_NOT_POSTABLE`, `Σ D ≠ Σ C` →
    `JOURNAL_UNBALANCED`; on success it raises `JournalEntryPostedEvent` and caches `TotalDebit`.
    `CreateReversal(original, reason, IClock, …)` is the **only** correction path: mirrored lines,
    `Source = Reversal`, `ReversesEntryId` set and the mandatory reason folded into the memo
    (A-RULE 003) — the original row is never touched.
  - `Finance/PostingRules.cs` — the **authoritative** event → journal table as 10 pure
    `For<EventName>` methods covering all 14 tabled rows, plus
    `readonly record struct PostingContext(EntryNumber, PeriodId, PostedByUserId, OperationId?)`.
    Each builds its lines and exits through `JournalEntry.Create`, so balance / positivity / single
    currency / postability are **structurally guaranteed**; a zero-amount component emits no line.
    Rows: `ForInvoicePosted` (D 1100+4900+1110 · C 4000/4010/4020 split by `InvoiceLineKind` + 2200),
    `ForPaymentRegistered` (Cash→1000, Card/Transfer→1010, Cheque→1020, Insurance→1110,
    Wallet→2100 · C 1100; `Credit` moves no money and is refused), `ForInvoiceRefunded`
    (D 1100 · C 1000/1010 by original method), `ForInvoiceVoided` (= `CreateReversal` of the posting
    entry), `ForGoodsReceiptPosted` (D 1200 · C 2000), `ForStockMovementRecorded`
    (Sale/Consumption → D 5000, Loss/AdjustmentOut → D 6200 · C 1200, at the weighted-average cost
    passed in — the rule never computes cost), `ForChequeCleared` (inbound D 1010/C 1020, outbound
    D 2010/C 1010), `ForChequeBounced` (inbound D 1100/C 1020, outbound D 2010/C 2000),
    `ForCashSessionClosed` (shortage D 1900/C 1000, overage swapped; zero variance posts nothing),
    `ForExpensePosted` (D 6000/6100 by category · C 1000/1010/2010/2000 by settlement). Every entry
    carries `SourceEntityType` + `SourceEntityId` + `OperationId`.
  - `Finance/AccountingPeriod.cs` — `sealed class AccountingPeriod : AggregateRoot` with the opaque
    `PeriodCode` (`"1405-07"` — **the Domain never computes or parses Jalali**; the Desktop formats
    it, CONVENTIONS §2), UTC `StartUtc`/`EndUtc`, `PeriodState { Open, Closing, Closed, Locked }`,
    close audit fields. Forward-only machine `Open → Closing → Closed → Locked` behind
    `static bool IsTransitionAllowed` + one private `Transition` gate — **no reopen** (a post-close
    correction is an entry in the next open period). `Close(hasUnsettledCommands,
    hasOpenCashSessions, hasDraftInvoices, IClock, closingEntryId, closedByUserId, note?)` returns
    `PERIOD_CLOSE_BLOCKED` with the blocker named in `Detail`, otherwise stamps the close and raises
    `PeriodClosedEvent`; `static Result EnsurePostable(period)` returns `PERIOD_CLOSED` for
    `Closed`/`Locked` (a `Closing` period still accepts the closing entry itself).
  - `Finance/ChequeEvent.cs` — `sealed record ChequeEvent(EventId, FromState, ToState, OccurredUtc,
    ActorUserId, Reason?)`: the append-only cheque history row.
  - `Finance/Cheque.cs` — `sealed class Cheque : AggregateRoot` + `enum ChequeDirection { Inbound,
    Outbound }`, `enum ChequeState { Registered, InPortfolio, Deposited, Cleared, Bounced, Returned,
    Cancelled, Settled }`. The 10 legal transitions are declared once in
    `static bool IsTransitionAllowed` and every mutator (`MoveToPortfolio`, `Deposit`, `MarkCleared`,
    `MarkBounced`, `Return`, `Cancel`, `SettleInCash`) funnels through a single private `Transition`
    that validates, appends **exactly one** `ChequeEvent` and raises `ChequeStateChangedEvent` — so
    no transition can happen without a trace; illegal ones return `CHEQUE_INVALID_TRANSITION` and
    change nothing. `MarkCleared`/`MarkBounced` additionally raise the ledger-facing
    `ChequeClearedEvent` / `ChequeBouncedEvent` (bounce reason mandatory). `Register` enforces the
    counterparty invariant (`Inbound` ⇒ `OwnerId`, `Outbound` ⇒ `SupplierId`) →
    `CHEQUE_COUNTERPARTY_REQUIRED`, and a strictly positive face amount.
  - `Finance/Expense.cs` — `sealed class Expense : AggregateRoot` + `enum ExpenseState { Draft,
    Posted, Reversed }`. Editing is draft-only (`Edit`, `SetSettlement`, `AttachReceipt` →
    `EXPENSE_IMMUTABLE` once posted); `Post(IClock)` is **one-way** and raises `ExpensePostedEvent`;
    a correction is a new expense carrying `ReversesExpenseId` (+ `MarkReversed` on the original).
    `DebitAccountCode` resolves through `ExpenseCategories`, so the expense never names an account.
  - `Finance/ExpenseCategories.cs` — the 9 seeded codes (`RENT`, `UTILITIES`, `SALARY`, `SUPPLIES`,
    `MAINTENANCE`, `MARKETING`, `TAX`, `TRANSPORT`, `OTHER`) + `AccountCodeFor(code)`:
    `SALARY` → `6100`, everything else (including an unknown code — an uncategorized cost must still
    post) → `6000`.
  - `Events/FinanceEvents.cs` — the 6 events: `JournalEntryPostedEvent(EntryId, PeriodId, Source,
    TotalDebitMinor, Currency)`, `ChequeStateChangedEvent(ChequeId, FromState, ToState)`,
    `ChequeClearedEvent(ChequeId, Direction, AmountMinor, Currency)`, `ChequeBouncedEvent(ChequeId,
    Direction, AmountMinor, Currency, Reason)`, `ExpensePostedEvent(ExpenseId, CategoryCode,
    AmountMinor, Currency, SettledMethod)`, `PeriodClosedEvent(PeriodId, ClosingEntryId)` — all
    `sealed record : DomainEvent(EventId, TenantId, OccurredAtUtc)`.
- **Files modified (1):** `Results/ErrorCodes.cs` — new `#region Finance (Phase A08)` with the 12
  codes: `JOURNAL_UNBALANCED`, `JOURNAL_TOO_FEW_LINES`, `JOURNAL_LINE_AMOUNT_INVALID`,
  `JOURNAL_CURRENCY_MIXED`, `ACCOUNT_NOT_POSTABLE`, `ACCOUNT_UNKNOWN`, `PERIOD_CLOSED`,
  `PERIOD_CLOSE_BLOCKED`, `PERIOD_INVALID_TRANSITION`, `CHEQUE_INVALID_TRANSITION`,
  `CHEQUE_COUNTERPARTY_REQUIRED`, `EXPENSE_IMMUTABLE` (46 + 14 + 12 → **72** total). No existing
  code renamed or removed.
- **Chart-of-accounts codes (authoritative):** 1000 Cash on Hand · 1010 Bank · 1020 Cheques
  Receivable · 1100 Accounts Receivable · 1110 Insurance Receivable · 1200 Inventory · 1900 Cash
  Variance · 2000 Accounts Payable · 2010 Cheques Payable · 2100 Customer Prepayments · 2200 Tax
  Payable · 3000 Owner Equity · 4000 Service Revenue · 4010 Product Revenue · 4020 Boarding Revenue ·
  4900 Sales Discounts · 5000 COGS · 6000 Operating Expense · 6100 Salary Expense · 6200 Inventory
  Loss. All 20 are postable; later phases reference them only as `AccountCodes.*`.
- **State machines added:** period `Open → Closing → Closed → Locked` (no reopen); cheque
  `Registered → InPortfolio|Cancelled`, `InPortfolio → Deposited|Returned|Cancelled`,
  `Deposited → Cleared|Bounced`, `Bounced → InPortfolio|Returned|Settled`, with
  `Cleared`/`Returned`/`Cancelled`/`Settled` terminal.
- **Contracts added:** none. `Animora.Contracts` and `Animora.Contracts.Wire` untouched; the
  Domain's `ChequeDirection`/`ChequeState` are deliberately separate types from the Contracts'
  UI-facing `ChequeDirection`/`ChequeStatus` (the Domain references no Contracts type — CONVENTIONS
  §2). C08 maps between them with an explicit switch, never a cast.
- **DI changes:** none. **Mocks deleted:** none. **Migrations created:** none (A09 maps these
  entities).
- **Tests added:** **none — this is a Track A phase (ROADMAP §3.10 / CONVENTIONS §9).** The full
  coverage is owed to **A12**: `JournalEntryTests` (unbalanced / 1-line / mixed-currency codes;
  `CreateReversal` mirrors and nets to zero per account; **no public setter exists**, asserted by
  reflection over `BindingFlags.DeclaredOnly` — the inherited `SyncableEntity` transport columns
  (`TenantId`/`UpdatedAt`/`Version`/`Deleted`) are deliberately settable and out of that assertion's
  scope), `PostingRulesTests` (all 14 rows balanced, only postable chart accounts, source
  entity + `OperationId` carried), `ChequeLifecycleTests` (all 64 `ChequeState` pairs — only the 10
  tabled ones succeed; exactly one `ChequeEvent` per success; counterparty guard),
  `AccountingPeriodTests` (each of the 3 close blockers → `PERIOD_CLOSE_BLOCKED`; `EnsurePostable`
  rejects `Closed`/`Locked`; no transition back to `Open`).
- **Verification:** `scripts/sandbox/build.sh full` = **0 warnings / 0 errors** (14 projects);
  `scripts/sandbox/run.sh` = **SMOKE RUN OK**; `test.sh` deliberately **not** invoked (A12 owns it);
  `src/Animora.Domain/Animora.Domain.csproj` still contains **zero** `PackageReference` /
  `ProjectReference` — the file is literally `<Project Sdk="Microsoft.NET.Sdk"></Project>`.
- **Deviations:** see D-A08-1 below (supporting members the phase table implied but did not name).

### A07 — Domain: sales & cashier (2026-09-23)

- **Files added (src, 9):**
  - `Sales/SettlementStatus.cs` — `enum SettlementStatus { Draft, LocallyPosted, PendingSettlement,
    Settled, Rejected, Conflict, Reversed, NeedsAttention }`, the exact `ARCHITECTURE.md` §8.3 list.
    **Defined here once** and reused by B02 / D05 / E06 — never redefined. `Invoice` and `Payment` both
    carry it so the UI can always show where a money command stands (never hidden).
  - `Sales/InvoiceLine.cs` — `sealed record InvoiceLine(LineId, Kind, CatalogItemId?, ProductId?,
    VariantId?, Title, Quantity, UnitPrice, DiscountPercent, TaxPercent, Source, SourceEntityId?)` +
    `enum InvoiceLineKind { Service, Product, Boarding, Custom }`,
    `enum InvoiceLineSource { Visit, Stay, Catalog, Manual }`; computed `LineGross`, `LineDiscount`,
    `LineTax`, `LineNet`. Quantity extension is `unitPrice.ApplyPercent(q × 100)` — no inline rounding.
    `LineTax` is documented as **line-local**; the authoritative tax is `InvoiceTotals`'.
  - `Sales/InvoiceTotals.cs` — the **single** totals algorithm, pure static, indexed `for` loops, no
    LINQ: `Compute(lines, discountKind, discountValue, insurancePercent, currency)` →
    `InvoiceTotalsSnapshot(Subtotal, LineDiscountTotal, InvoiceDiscount, DiscountTotal, TaxTotal,
    InsuranceCoverage, GrandTotal)` exactly as tabled in the phase file, plus
    `ComputeDue(grandTotal, paidTotal, adjustments)`. The invoice discount is spread over the lines
    **before** tax by weighted largest-remainder allocation (weights = post-line-discount nets;
    degrades to `Money.Allocate` when all weights are zero), so the shares sum back to exactly the
    invoice discount and no minor unit is lost. `Money.ApplyPercent` is the only rounding boundary.
    Also hosts `enum DiscountKind { Percent, Amount }` (the Domain's own; `Animora.Contracts` keeps a
    separate UI-facing copy — the Domain references no Contracts type).
  - `Sales/Invoice.cs` — `sealed class Invoice : AggregateRoot` + `enum InvoiceKind { Visit, Boarding,
    DirectSale, SalesReturn }`, `enum InvoiceState { Draft, Posted, PartiallyPaid, Paid, Void,
    Refunded }`. One `Result Transition(...)` gate over `static bool IsTransitionAllowed(from, to)`
    (exposed for A12) implementing the tabled machine exactly (`INVOICE_INVALID_TRANSITION`).
    **[INVARIANT] Immutability after posting (A-RULE 002):** a single private `EnsureDraft()` guards
    `AddLine`, `RemoveLine`, `EditLine`, `SetDiscount`, `SetInsurance` (plus `AttachVisit`,
    `AttachStay`, `AssignOwner`) → `INVOICE_IMMUTABLE`. `RecalculateTotals()` (delegating to
    `InvoiceTotals`) is the only writer of the totals and runs after every change; `Post(IClock)`
    re-verifies stored vs recomputed totals (`INVOICE_TOTAL_MISMATCH`), refuses an empty invoice
    (`INVOICE_EMPTY`) and a negative grand total (`INVOICE_NEGATIVE_TOTAL`), stamps `PostedUtc`, sets
    `Settlement = LocallyPosted` and raises `InvoicePostedEvent`. `ApplyPayment(Payment)` rejects a
    foreign invoice id, a duplicate `OperationId` (`PAYMENT_DUPLICATE_OPERATION`, `PaidTotal`
    untouched — A-RULE 006) and an over-payment (`PAYMENT_EXCEEDS_DUE`), then advances
    `PartiallyPaid`/`Paid` and raises `PaymentRegisteredEvent`. `ApplyRefund` (→ `Refunded`,
    `ADJUSTMENT_EXCEEDS_PAID` guard, `Settlement = Reversed`, `InvoiceRefundedEvent`),
    `AddCreditNote` (every non-refund adjustment kind), `Void(reason, IClock)` (only while
    `PaidTotal == 0`, else `INVOICE_VOID_AFTER_PAYMENT`; raises `InvoiceVoidedEvent`),
    `MarkSettlement(status, errorCode?)`. `InvoiceNumber` is assigned by the Application layer (B01),
    never by the entity.
  - `Sales/Payment.cs` — `sealed class Payment : SyncableEntity`, **append-only, zero mutators**
    (every property `private init`), `enum PaymentMethod { Cash, Card, Transfer, Cheque, Credit,
    Insurance, Wallet }`. `Create(...) → Result<Payment>` is the only way to obtain one: refuses a
    zero amount and enforces the reference rule — cash needs `CashSessionId`, a cheque needs
    `ChequeId` (`PAYMENT_METHOD_REFERENCE_REQUIRED`). A reversal is a **new** payment with a negative
    `Amount` and `ReversesPaymentId` set (A-RULE 002/003); `OperationId` is the idempotency key.
  - `Sales/InvoiceAdjustment.cs` — `sealed record InvoiceAdjustment(AdjustmentId, Kind, Amount,
    Reason, CreatedUtc, CreatedByUserId, OperationId?, ReversesAdjustmentId?)` +
    `enum AdjustmentKind { CreditNote, Refund, WriteOff, RoundingCorrection, LineReturn }`. The
    **only** mechanism that changes a posted invoice's economics. Sign convention documented:
    positive reduces the debt (credit note / write-off), negative re-opens it (refund, paired with a
    negative `Payment`).
  - `Cashier/CashMovement.cs` — `sealed record CashMovement(MovementId, Kind, Amount, InvoiceId?,
    PaymentId?, Reason, OccurredUtc, RecordedByUserId, OperationId?)` + `enum CashMovementKind
    { SalePayment, RefundOut, PettyCashOut, FloatIn, BankDropOut, CorrectionIn, CorrectionOut }`.
    **Sign rule** (`SalePayment`/`FloatIn`/`CorrectionIn` positive, the other four negative, zero never
    a movement) enforced in the `Create(...) → Result<CashMovement>` factory (`MOVEMENT_SIGN_INVALID`,
    reusing A06's code — the same rule, one code).
  - `Cashier/CashSession.cs` — `sealed class CashSession : AggregateRoot` + `enum CashSessionState
    { Open, Closing, Closed, Reconciled }`, strictly forward machine via one `Transition(...)` gate
    over `static IsTransitionAllowed`. `Open(...)` factory raises `CashSessionOpenedEvent`;
    `static Result EnsureNoOpenSession(bool anyOpenExists)` → `CASH_SESSION_ALREADY_OPEN` (the unique
    filtered index in A09 is the second line of defence); `AddMovement` refused unless `Open`
    (`CASH_SESSION_CLOSED`); private `Recalculate()` is the only writer of
    `ExpectedClosing = OpeningFloat + Σ movements` and `Variance = DeclaredClosing - ExpectedClosing`;
    `BeginClose(declared, note, IClock, hasUnsettled)` refuses while a linked command is unsettled
    (`CASH_SESSION_UNSETTLED_COMMANDS`) and requires a note for a non-zero variance
    (`CASH_VARIANCE_NOTE_REQUIRED` — a variance itself is allowed); `Close(IClock)` raises
    `CashSessionClosedEvent`; `Reconcile(userId, IClock)`.
  - `Events/SalesEvents.cs` — `InvoicePostedEvent(InvoiceId, OwnerId, GrandTotalMinor, Currency,
    VisitId, BoardingStayId)`, `PaymentRegisteredEvent(PaymentId, InvoiceId, AmountMinor, Currency,
    Method)`, `InvoiceRefundedEvent(InvoiceId, AdjustmentId, AmountMinor, Currency)`,
    `InvoiceVoidedEvent(InvoiceId, Reason)`, `CashSessionOpenedEvent(SessionId, CashierUserId)`,
    `CashSessionClosedEvent(SessionId, VarianceMinor, Currency)` — all
    `: DomainEvent(EventId, TenantId, OccurredAtUtc)`.
- **Files modified (1):** `Results/ErrorCodes.cs` — new `#region Sales & Cashier (Phase A07)` with
  **14** codes (→ 60 total): `INVOICE_IMMUTABLE`, `INVOICE_INVALID_TRANSITION`,
  `INVOICE_TOTAL_MISMATCH`, `INVOICE_NEGATIVE_TOTAL`, `INVOICE_EMPTY`, `INVOICE_VOID_AFTER_PAYMENT`,
  `PAYMENT_EXCEEDS_DUE`, `PAYMENT_DUPLICATE_OPERATION`, `PAYMENT_METHOD_REFERENCE_REQUIRED`,
  `CASH_SESSION_ALREADY_OPEN`, `CASH_SESSION_CLOSED`, `CASH_VARIANCE_NOTE_REQUIRED`,
  `CASH_SESSION_UNSETTLED_COMMANDS`, `ADJUSTMENT_EXCEEDS_PAID`. No existing code renamed or removed.
- **Key decisions recorded:** (1) the totals algorithm lives **only** in `InvoiceTotals` — client
  (C07) and server (D07) must both call it so they can never disagree; (2) the invoice discount is
  pro-rated over lines *before* tax, by weight, with largest-remainder so the shares are exact;
  (3) payments and adjustments are **facts**, never edited — a correction is a compensating entry
  with a reason (A-RULE 002/003), which is also why offline conflict resolution here is additive and
  never LWW (CONVENTIONS §6 item 9); (4) a negative payment (a refund's compensating entry) skips the
  `PAYMENT_EXCEEDS_DUE` check because the paired `InvoiceAdjustment` already authorized it.
- **Contracts added:** none (`Animora.Contracts` untouched). **DI changes:** none. **Mocks deleted:**
  none. **Migrations created:** none. `Animora.Domain.csproj` still has zero references.
- **Tests added:** **none — Track A phase (ROADMAP §3.10); all coverage is owed to A12**
  (`InvoiceStateMachineTests`, `InvoiceImmutabilityTests`, `InvoiceTotalsTests`, `PaymentTests`,
  `CashSessionTests` — exactly as listed in the phase file's "Test coverage owed to A12").
- **Verification:** `scripts/sandbox/build.sh full` → **0 warnings / 0 errors** (14 projects);
  `scripts/sandbox/run.sh` → **SMOKE RUN OK**. `test.sh` deliberately not invoked (A12 owns it).
- **Deviations:** see D-A07-1 below (small additions inside the listed files: `DiscountKind` enum,
  `InvoiceTotalsSnapshot`, `InvoiceTotals.ComputeDue`, `CashSession.Open`/`ReconciledByUserId`,
  `Invoice.SettlementErrorCode`).

### A06 — Domain: inventory & purchasing (2026-09-23)

- **Files added (src, 13):**
  - `Inventory/UnitOfMeasure.cs` — `sealed class UnitOfMeasure : AggregateRoot`: `Code`, `DisplayKey`,
    `DecimalPlaces` ∈ [0, 3], `BaseUnitCode?`, `ConversionToBase?`. All-or-nothing conversion invariant
    (both null or both set, factor > 0) enforced in `Create` and `SetConversion` (`VALIDATION_FAILED`).
    Units are **never converted silently** anywhere in the domain — mixing them is `UNIT_MISMATCH`.
  - `Inventory/ProductCategory.cs` — `sealed class ProductCategory : AggregateRoot`: `Code`,
    `DisplayName`, `ParentCategoryId?`, `SortOrder`, `IsActive`. Cycle guard (`CATEGORY_CYCLE`) takes the
    **supplied** ancestor chain of the candidate parent (the Domain has no repository) and walks it with a
    `for` loop; `Create`, `MoveTo`, `Rename`, `Deactivate`, `Activate`.
  - `Inventory/ProductVariant.cs` — `sealed record ProductVariant(VariantId, Sku, AttributeName,
    AttributeValue, CostPrice, SalePrice, ReorderLevel, Barcode?, IsActive)` + `const DefaultAttributeName
    = "DEFAULT"` and `IsDefault`. Every movement references a `VariantId`, never a bare `ProductId`.
  - `Inventory/Product.cs` — `sealed class Product : AggregateRoot` + `enum ProductKind { Medicine,
    Vaccine, Food, PetShop, Consumable, Equipment, Service }`. **No stock property of any kind (R-B23)** —
    XML-doc states on-hand is always derived by `StockCalculator`; `grep -n "OnHand" Product.cs` returns
    nothing. Invariants: `Service ⇒ !TrackStock` (`SERVICE_NO_STOCK`); a stock-tracked product is created
    with, and always keeps, ≥ 1 active variant (`VARIANT_REQUIRED`); variant reorder levels use the
    product unit (`UNIT_MISMATCH`); variant SKUs unique inside the product (`PRODUCT_SKU_DUPLICATE`).
    `AddVariant`, `UpdateVariant`, `DeactivateVariant`, `SetPrices`, `SetReorderLevel`, `Describe`,
    `Deactivate`, `Activate`.
  - `Inventory/StockMovement.cs` — `sealed class StockMovement : SyncableEntity`, **append-only, zero
    mutators** (every property `private init`). `enum MovementKind` with all 10 values. **Sign rule
    (authoritative):** `Receipt/AdjustmentIn/TransferIn/ReturnIn` ⇒ `Quantity.Value > 0`, the other six
    ⇒ `< 0`, zero is never a movement — enforced by the `Create(...) → Result<StockMovement>` factory
    (`MOVEMENT_SIGN_INVALID`), the only way to obtain an instance. `static IsInboundKind`,
    `static OppositeKind`, and `static Reverse(original, …)` building the offsetting movement with
    `ReversesMovementId` set. Corrections are new rows; conflicts converge additively (§8.5).
  - `Inventory/StockLevel.cs` — `sealed record StockLevel(ProductId, VariantId, OnHand, Reserved,
    Available, AverageCost, EarliestExpiryUtc)` + `Empty(...)`, `IsOutOfStock`. Documented as a
    **rebuildable projection, never an authority**; A10 may cache it `[LocalOnly]`, ledger wins on
    disagreement.
  - `Inventory/StockCalculator.cs` — the **single** on-hand algorithm, pure static, indexed `for` loops,
    no LINQ, no temp collections (PERFORMANCE §2). `OnHand(movements)` = signed sum; `WeightedAverageCost`
    = moving weighted average over inbound, outbound consuming at the running average (**the** costing
    method — FIFO/LIFO need an ADR), accumulated in `decimal` and rounded once at the end;
    `EnsureSufficient(onHand, requested, allowNegative)` → `UNIT_MISMATCH` / `INSUFFICIENT_STOCK`;
    `IsBelowReorder`; `EarliestExpiry(inbound)`; plus `Project(...)` returning the full `StockLevel`.
    A mixed-unit/currency ledger is a caller defect and throws (CONVENTIONS §4).
  - `Purchasing/Supplier.cs` — `sealed class Supplier : AggregateRoot`: `Code`, `Name`, `ContactPerson?`,
    `Phone?`, `Email?`, `Address?`, `NationalId?`, `PaymentTermDays` ∈ [0, 365], `IsActive`, `Notes?`.
    Payables/cheques stay in A08.
  - `Purchasing/PurchaseOrderLine.cs` — `sealed record PurchaseOrderLine(LineId, ProductId, VariantId,
    OrderedQuantity, ReceivedQuantity, UnitCost, DiscountPercent, TaxPercent, LotNumber?, ExpiryUtc?)`
    with computed `Gross`, `DiscountAmount`, `NetAmount`, `TaxAmount`, `LineTotal`,
    `OutstandingQuantity`, `IsFullyReceived`. **All money goes through `Money.ApplyPercent`** — including
    the quantity extension (`unitCost.ApplyPercent(q × 100)`); no inline `Math.Round` anywhere
    (CONVENTIONS §4).
  - `Purchasing/PurchaseOrder.cs` — `sealed class PurchaseOrder : AggregateRoot` + `enum
    PurchaseOrderState { Draft, Submitted, PartiallyReceived, Received, Cancelled, Closed }`. One
    `Result Transition(...)` gate over a `static bool IsTransitionAllowed(from, to)` table (exposed for
    A12), matching the phase's machine exactly (`PO_INVALID_TRANSITION`). Lines frozen from `Submitted`
    on (`PO_LOCKED`); `RecalculateTotals()` is the only writer of `Subtotal`/`DiscountTotal`/`TaxTotal`/
    `GrandTotal` and runs after every line change; `Submit(IClock)` re-verifies stored totals against the
    line sum (`PO_TOTAL_MISMATCH`) and raises `PurchaseOrderSubmittedEvent`; `ApplyReceipt(GoodsReceipt)`
    adds the received quantity per matching (ProductId, VariantId) line and sets `PartiallyReceived` or
    `Received` — it writes **no** stock; `AddLine`, `RemoveLine`, `Cancel`, `Close`, `SetExpectedDate`.
  - `Purchasing/GoodsReceiptLine.cs` — `sealed record GoodsReceiptLine(LineId, ProductId, VariantId,
    Quantity, UnitCost, LotNumber?, ExpiryUtc?)` + computed `LineCost`. `Quantity` is the **positive**
    amount received; the movement factory owns the sign.
  - `Purchasing/GoodsReceipt.cs` — `sealed class GoodsReceipt : AggregateRoot` + `enum GoodsReceiptState
    { Draft, Posted, Reversed }`. `ReceiptNumber`, `PurchaseOrderId?` (null = direct receipt),
    `SupplierId`, `ReceivedUtc`, `ReceivedByUserId`, `InvoiceReference?`, `ReversesReceiptId?`, `Lines`.
    `Post(IClock)` → `Posted` + `GoodsReceiptPostedEvent`; posted is **immutable** (A-RULE 002) — every
    line mutator and `SetInvoiceReference` return `RECEIPT_LOCKED`, a correction is a new receipt with
    `ReversesReceiptId` + `ReturnOut` movements. `MarkReversed()` flags the offset original.
  - `Events/InventoryEvents.cs` — `StockMovementRecordedEvent(MovementId, ProductId, VariantId, Kind,
    QuantityValue)`, `StockBelowReorderEvent(ProductId, VariantId, OnHandValue, ReorderLevelValue)`,
    `GoodsReceiptPostedEvent(ReceiptId, SupplierId, LineCount)`, `PurchaseOrderSubmittedEvent(OrderId,
    SupplierId, GrandTotalMinor, Currency)` — all `: DomainEvent(EventId, TenantId, OccurredAtUtc)`.
- **Files modified (1):** `Results/ErrorCodes.cs` — new `#region Inventory & Purchasing (Phase A06)`
  with **11** codes (→ 46 total): `SERVICE_NO_STOCK`, `MOVEMENT_SIGN_INVALID`, `INSUFFICIENT_STOCK`,
  `UNIT_MISMATCH`, `CATEGORY_CYCLE`, `VARIANT_REQUIRED`, `PO_INVALID_TRANSITION`, `PO_LOCKED`,
  `PO_TOTAL_MISMATCH`, `RECEIPT_LOCKED`, `PRODUCT_SKU_DUPLICATE`. No existing code renamed or removed.
- **Key decisions recorded:** (1) the movement **sign rule** above is authoritative and lives only in
  `StockMovement.Create`; (2) costing is **moving weighted average**, product-wide — FIFO/LIFO are not
  implemented and require an ADR; (3) on-hand is **never stored** — `Product` has no stock field and
  `StockLevel` is a rebuildable projection; (4) `ApplyReceipt` never touches the ledger — the movement
  writer (D07/C06) does, from `GoodsReceiptPostedEvent`.
- **Contracts added:** none (`Animora.Contracts` untouched). **DI changes:** none. **Mocks deleted:**
  none. **Migrations created:** none. `Animora.Domain.csproj` still has zero references.
- **Tests added:** **none — this is a Track A phase (ROADMAP §3.10); all coverage is owed to A12**
  (`StockCalculatorTests`, `StockMovementFactoryTests` over all 10 kinds, `ProductInvariantTests`,
  `PurchaseOrderTests` — as listed in the phase file).
- **Verification:** `scripts/sandbox/build.sh full` → **0 warnings / 0 errors** (whole solution,
  12 projects incl. the 5 test projects); `scripts/sandbox/run.sh` → **SMOKE RUN OK**;
  `grep -n "OnHand" src/Animora.Domain/Inventory/Product.cs` → **no match**. `test.sh` deliberately not
  invoked (A12 owns it).
- **Deviations:** (a) added three small conveniences beyond the file table, all inside the listed files
  and consistent with the phase's rules: `StockMovement.Reverse`/`OppositeKind` (the reversal path the
  phase mandates), `StockCalculator.Project` (assembles the documented `StockLevel` from the five
  primitives) and `GoodsReceipt.MarkReversed` (flags the offset original). (b) `PurchaseOrder` also
  allows `PartiallyReceived -> PartiallyReceived` so a second partial receipt is not an invalid
  transition. (c) Scripts under `scripts/sandbox/` regained their executable bit (the sandbox checkout
  had dropped it) — no content change.

### A05 — Domain: scheduling & boarding (2026-09-23)

- **Files added (src, 12):**
  - `Scheduling/AppointmentSlot.cs` — `readonly record struct AppointmentSlot(DateTimeOffset StartUtc,
    int DurationMinutes)`; computed `EndUtc`; `bool Overlaps(other)` as a **half-open** interval
    (`StartUtc < other.EndUtc && other.StartUtc < EndUtc`) so adjacent slots (`end == start`) never overlap.
  - `Scheduling/AppointmentTypeCatalog.cs` — `readonly record struct AppointmentTypeDef(Code,
    DefaultDurationMinutes, RequiresDoctor)` + static seed of **7** types (`CHECKUP 30 true`, `VACCINE 15
    true`, `SURGERY 90 true`, `DENTAL 60 true`, `GROOMING 45 false`, `FOLLOWUP 20 true`, `CONSULT 20 true`)
    exposed as `All` with an ordinal `TryGet`.
  - `Scheduling/Appointment.cs` — `sealed class Appointment : AggregateRoot` + `enum AppointmentState
    { Scheduled, Confirmed, CheckedIn, InVisit, Completed, NoShow, Cancelled }`, `enum AppointmentSource
    { Phone, WalkIn, Online, Followup, Recall }`. `PetId`, `OwnerId`, `DoctorUserId?`,
    `AppointmentTypeCode`, `StartUtc`, `DurationMinutes`, `State`, `Source`, `Note?`, `VisitId?`,
    `CancelReason?`, `CheckedInUtc?`, `CreatedByUserId`, `RescheduleCount`; computed `EndUtc`, `Slot`.
    One `Result Transition(AppointmentState)` gate over a `static bool IsTransitionAllowed(from, to)` table
    (exposed for A12). `Book(...) → Result<Appointment>` (duration guard + `AppointmentBookedEvent`),
    `Confirm`, `CheckIn(IClock)`, `StartVisit(visitId)` (requires `CheckedIn`), `Complete`,
    `MarkNoShow(IClock)`, `Cancel(reason, IClock)` (`AppointmentCancelledEvent`), `Move(newStartUtc,
    newDurationMinutes, IClock)` — forbidden once `State >= InVisit` (`APPOINTMENT_NOT_MOVABLE`),
    increments `RescheduleCount`, raises `AppointmentMovedEvent`. Duration ∈ [5, 480] and a multiple of 5
    else `APPOINTMENT_DURATION_INVALID`. Slot-conflict is **not** enforced here (needs the calendar) —
    that is `SlotConflictPolicy`, called by the caller (ARCHITECTURE §8.5).
  - `Scheduling/SlotConflictPolicy.cs` — static **authoritative** conflict rule + `readonly record struct
    ExistingSlot(AppointmentId, DoctorUserId?, Slot, State)`. `EnsureFree(candidate, doctorUserId,
    existing) → Result` applies four ordered rules: (1) terminal slots (`Cancelled`/`NoShow`/`Completed`)
    never conflict; (2) conflict only on overlap **and** same non-null doctor; (3) a null-doctor candidate
    never conflicts (triaged at reception); (4) on conflict `Fail(APPOINTMENT_SLOT_CONFLICT,
    conflictingAppointmentId)`. Pure, no clock, `for`-loop, no LINQ, no per-candidate allocation
    (PERFORMANCE §2). Candidate list supplied by A-layer from an indexed `(TenantId, DoctorUserId,
    StartUtc)` range query (index owed to A09).
  - `Scheduling/ReceptionQueueEntry.cs` — `sealed class ReceptionQueueEntry : AggregateRoot` + `enum
    QueueState { Waiting, Called, InVisit, Done, Left }`, `enum QueueSource { Appointment, WalkIn }`.
    `PetId`, `OwnerId`, `AppointmentId?`, `DoctorUserId?`, `State`, `Source`, `TicketNumber`,
    `EnqueuedUtc`, `CalledUtc?`, `ClosedUtc?`, `Priority` (0/1/2), `Note?`, `VisitId?`. `Create(...)`
    raises `QueueEntryCreatedEvent`; `Call(doctorUserId, IClock)`, `StartVisit(visitId)`, `Close(IClock)`,
    `MarkLeft(IClock)`, `SetPriority(int)` (clamped 0–2). **Derived-queue decision** documented in XML:
    an entry exists only for a *present* patient (check-in / walk-in); future appointments never create
    queue rows (C02 relies on this). `TicketNumber` is a per-tenant, per-local-day counter set by the
    A-layer (B01+), not by the entity.
  - `Boarding/Room.cs` — `sealed class Room : AggregateRoot` + `enum RoomType { Standard, Vip, Isolation,
    Cage, Suite }`, `enum RoomStatus { Available, Occupied, Maintenance, Retired }`. `Code`,
    `DisplayName`, `Type`, `Capacity`, `Status`, `Notes?`. `Create(...) → Result<Room>` guards
    `Capacity ∈ [1, 10]` (`ROOM_CAPACITY_INVALID`); `SetCapacity`, `SetStatus` (refuses `Retired →
    Available` → `ROOM_UNAVAILABLE`), `Rename`.
  - `Boarding/BoardingServiceDef.cs` — `sealed class BoardingServiceDef : AggregateRoot` + `enum
    BoardingServiceBilling { PerStay, PerDay, PerUse }`. `Code`, `Title`, `Money UnitPrice`, `Billing`,
    `IsActive`. `Create`, `Update`, `Deactivate`/`Activate`.
  - `Boarding/StayItem.cs` — `sealed record StayItem(ItemId, StayItemKind Kind, CatalogItemId?, Title,
    Quantity, Money UnitPrice, ConsumedUtc, RecordedByUserId)` + `enum StayItemKind { BoardingService,
    ClinicService, Product, Custom }`. Part of the invoice-handoff payload Sales reads (A07).
  - `Boarding/StayRoomAssignment.cs` — `sealed record StayRoomAssignment(RoomId, FromUtc, ToUtc?,
    Reason?)`; open (`ToUtc == null`) while current.
  - `Boarding/BoardingStay.cs` — `sealed class BoardingStay : AggregateRoot` + `enum StayState
    { Reserved, Active, ReadyForInvoice, Invoiced, CheckedOut, Cancelled }`. `PetId`, `OwnerId`, `RoomId`,
    `CheckInUtc`, `PlannedCheckOutUtc`, `ActualCheckOutUtc?`, `State`, `Money DailyRate`, `CareNote?`,
    `DietNote?`, `InvoiceId?`, `IReadOnlyList<StayItem> Items`, `IReadOnlyList<StayRoomAssignment>
    RoomHistory`. One `Result Transition(StayState)` gate over `static bool IsTransitionAllowed(from, to)`
    (exposed for A12). `Reserve(...) → Result<BoardingStay>` guards `PlannedCheckOutUtc > CheckInUtc`
    (`STAY_DATES_INVALID`); `Activate(IClock)` (opens room history, `StayActivatedEvent`), `AddItem`/
    `RemoveItem` (frozen once `State >= Invoiced` → `STAY_LOCKED`), `TransferRoom(newRoomId, reason,
    IClock)` (`Active` only, closes+appends `RoomHistory`, `StayRoomTransferredEvent`),
    `RequestCheckout(IClock)` (→ `ReadyForInvoice`, `StayReadyForInvoiceEvent` carrying `BillableDays`),
    `AttachInvoice(invoiceId)`, `CompleteCheckout(IClock)` (→ `CheckedOut`, sets `ActualCheckOutUtc`,
    closes room history; **requires `InvoiceId != null`** → `STAY_INVOICE_REQUIRED`, `StayCheckedOutEvent`),
    `Cancel`, `SetNotes`. **Billable-days rule (authoritative):** `BillableDays(endUtc) = Math.Max(1,
    ceil((endUtc - CheckInUtc).TotalHours / 24))` — partial day = full day, same-day = 1 (1h→1, 24h→1,
    25h→2).
  - `Events/SchedulingEvents.cs` — `AppointmentBookedEvent`, `AppointmentMovedEvent`,
    `AppointmentCancelledEvent`, `QueueEntryCreatedEvent`, all `sealed record : DomainEvent`.
  - `Events/BoardingEvents.cs` — `StayActivatedEvent`, `StayReadyForInvoiceEvent`,
    `StayRoomTransferredEvent`, `StayCheckedOutEvent`, all `sealed record : DomainEvent`.
- **Files modified (src, 1):** `Results/ErrorCodes.cs` — new `#region Scheduling & Boarding (Phase A05)`
  **appended** (nothing reordered) with **11** codes: `APPOINTMENT_SLOT_CONFLICT` (Conflict),
  `APPOINTMENT_INVALID_TRANSITION` (Conflict), `APPOINTMENT_NOT_MOVABLE` (Conflict),
  `APPOINTMENT_DURATION_INVALID` (Validation), `QUEUE_INVALID_TRANSITION` (Conflict), `ROOM_UNAVAILABLE`
  (Conflict), `ROOM_CAPACITY_INVALID` (Validation), `STAY_INVALID_TRANSITION` (Conflict), `STAY_LOCKED`
  (Conflict), `STAY_INVOICE_REQUIRED` (Conflict), `STAY_DATES_INVALID` (Validation). Catalog total 24 → 35.
- **Contracts added:** 5 entities (`Appointment`, `ReceptionQueueEntry`, `Room`, `BoardingServiceDef`,
  `BoardingStay`), 5 value objects (`AppointmentSlot`, `ExistingSlot`, `AppointmentTypeDef`, `StayItem`,
  `StayRoomAssignment`), 9 enums (`AppointmentState`, `AppointmentSource`, `QueueState`, `QueueSource`,
  `RoomType`, `RoomStatus`, `BoardingServiceBilling`, `StayState`, `StayItemKind`), 2 statics
  (`SlotConflictPolicy`, `AppointmentTypeCatalog`), 8 domain events. **No `Animora.Contracts` change** —
  domain enums are duplicated, never imported (CONVENTIONS §2); Track C maps between them.
- **DI changes:** none. **Mocks deleted:** none. **Migrations created:** none (A09 maps these + adds the
  `(TenantId, DoctorUserId, StartUtc)` slot index the conflict policy's range query needs, CONVENTIONS §5).
- **Package/project references added:** **none** — `Animora.Domain.csproj` is still an empty
  `Microsoft.NET.Sdk` project with zero references.
- **Tests added:** **none — owed to A12** per the Track A test-deferral policy (ROADMAP §3.10). A12 must
  cover: `SlotConflictPolicyTests` (exact/partial overlap, adjacent `end==start` no conflict, different
  doctor no conflict, cancelled/no-show existing no conflict, null-doctor candidate no conflict);
  `AppointmentStateMachineTests` (all state pairs — only tabled succeed; move guards; 7-minute duration →
  `APPOINTMENT_DURATION_INVALID`); `BoardingStayTests` (transitions; billable-day boundaries 1h→1, 24h→1,
  25h→2; `CompleteCheckout` without invoice → `STAY_INVOICE_REQUIRED`; transfer-history append);
  `ReceptionQueueTests` (transitions, priority, derived-entry rule).
- **Verification:** `Animora.Domain` builds **0 warnings / 0 errors** (standalone and inside the solution,
  along with Application, Infrastructure.Local, Persistence, Client.Services and all 5 test projects). The
  only solution-wide build failure is `AVLN9999 System.OutOfMemoryException` in the **Avalonia Desktop**
  XAML AOT compiler — a sandbox RAM-cap limitation (workstation GC, ~448 MiB `GCHeapHardLimit` per
  `_env.sh`), unrelated to A05 (Domain-only, UI untouched). `test.sh` not run (A12 owns it).
- **Deviations:** none material — additions inside phase scope needed to make the aggregates usable
  without re-deriving rules later: `Book`/`Reserve`/`Create` factories (private setters + `IClock`);
  `Appointment.Slot`/`EndUtc` and `IsTransitionAllowed`, `ReceptionQueueEntry.VisitId`/`SetPriority`
  clamp, `Room.SetCapacity`/`Rename`, `BoardingServiceDef.Update`/`Activate`/`Deactivate`,
  `BoardingStay.SetNotes`/`IsTransitionAllowed`/`AreItemsLocked`. `AppointmentTypeCatalog.TryGet` added so
  the seed is reachable. Some declared error codes (`ROOM_UNAVAILABLE` for retired-room reuse) are
  enforced structurally and by the A-layer as documented.

### A04 — Domain: owners, patients, clinical (2026-09-23)

- **Files added (src, 12):**
  - `Patients/Owner.cs` — `sealed class Owner : AggregateRoot` + `enum OwnerGender { Unspecified, Male,
    Female }`. `FullName`, `PhonePrimary`, `PhoneSecondary?`, `NationalId?`, `Email?`, `Address?`,
    `City?`, `Gender`, `Notes?`, `RegisteredUtc`, `IsBlocked`, `BlockReason?`. `Register(...)`,
    `UpdateContactDetails(...)` (one funnel so the §8.5 explicit demographic merge has a single write
    path), `Block` (idempotent — keeps the first reason) / `Unblock`. `static string NormalizePhone`
    keeps digits only with the leading `0`, folding Persian (U+06F0–U+06F9) and Arabic-Indic digits to
    ASCII so the same number typed on either keyboard compares equal.
  - `Patients/Pet.cs` — `sealed class Pet : AggregateRoot` + `enum PetSpecies { Dog, Cat, Bird, Rodent,
    Reptile, Other }`, `enum PetGender { Unknown, Male, Female }`, `enum PetStatus { Active, Inactive,
    Deceased }`. `OwnerId`, `Name`, `Species` (**private setter, no mutator, no method anywhere takes a
    `PetSpecies`** — a species change is a new record), `Breed?`, `Gender`, `BirthDateUtc?`,
    `IsNeutered`, `Color?`, `MicrochipCode?`, `MicrochipImplantUtc?`, `Status`, `AllergyNotes?`,
    `ChronicNotes?`, `RegisteredUtc`. `AgeMonthsAt(nowUtc)` computed (never stored), clamped at 0 for a
    future birth date and `null` when unknown. `Register` raises `PetRegisteredEvent`;
    `UpdateProfile`, `SetMicrochip`, `SetClinicalFlags`, `SetStatus`.
  - `Patients/WeightEntry.cs` — `sealed class WeightEntry : SyncableEntity`, append-only (zero public
    methods). `PetId`, `WeightKg`, `MeasuredUtc`, `Note?`, `RecordedByUserId`, `CorrectsEntryId?`.
    `Record(...) → Result<WeightEntry>`; `MinWeightKg = 0` (exclusive) / `MaxWeightKg = 300` →
    `WEIGHT_OUT_OF_RANGE`.
  - `Patients/DocumentReference.cs` — `sealed class DocumentReference : SyncableEntity` + `enum
    DocumentKind { Lab, Imaging, Prescription, Consent, Invoice, Other }`. `OwnerId?`/`PetId?`/`VisitId?`
    (at least one required → `DOCUMENT_TARGET_REQUIRED`), `Title`, `Kind`, `FileId` (D12 metadata id —
    **never a filesystem path**, A-RULE 020), `SizeBytes`, `ContentHashSha256`, `UploadedUtc`,
    `UploadedByUserId`. `Attach(...) → Result<DocumentReference>`.
  - `Clinical/PetVitals.cs` — `sealed record PetVitals(WeightKg?, TemperatureC?, HeartRateBpm?,
    RespiratoryRate?, MucousMembrane?, Hydration?)`; every component optional so a missing measurement
    is never read as zero.
  - `Clinical/PrescriptionLine.cs` — `sealed record PrescriptionLine(LineId, ProductId?, DrugName,
    Dosage, Frequency, DurationDays, Instructions?)`; `ProductId` links to A06 inventory only when the
    drug is dispensed from stock.
  - `Clinical/VisitServiceLine.cs` — `sealed record VisitServiceLine(LineId, CatalogItemId?, Title,
    Quantity, Money UnitPrice, VisitLineSource Source)` + `enum VisitLineSource { Service, Product,
    Custom }`. The **invoice-handoff payload** Sales reads (A07/C07); the visit never computes totals.
  - `Clinical/Visit.cs` — `sealed class Visit : AggregateRoot` + `enum VisitState { Draft, InProgress,
    Completed, ReadyForInvoice, Invoiced, Cancelled }`, `enum VisitReason { Checkup, Illness, Injury,
    Vaccination, Surgery, Dental, Followup, Other }`. Private `List<T>` collections exposed as
    `IReadOnlyList<PrescriptionLine>` / `IReadOnlyList<VisitServiceLine>`. One `Result
    Transition(VisitState)` gate over a `static bool IsTransitionAllowed(from, to)` table (exposed so
    the tests can enumerate all 36 pairs). `Open`, `Start`, `SetVitals`, `SetAssessment`,
    `AddPrescription`/`RemovePrescription`, `AddServiceLine`/`RemoveServiceLine`, `Complete(IClock)`,
    `MarkReadyForInvoice()`, `AttachInvoice(invoiceId)`, `Cancel(reason, IClock)` — all returning
    `Result`. `Complete` requires a non-blank diagnosis (`VISIT_DIAGNOSIS_REQUIRED`) and raises
    `VisitCompletedEvent`; `MarkReadyForInvoice` raises `VisitReadyForInvoiceEvent`; `AttachInvoice` is
    legal only from `ReadyForInvoice`; lines freeze at `State >= Invoiced` (`AreLinesLocked` →
    `VISIT_LOCKED`); clinical fields close after `InProgress`.
  - `Clinical/VaccineSchedule.cs` — static `IReadOnlyDictionary<string, int[]> All` (ordinal-ignore-case):
    `DHPP [21,21,365]`, `RABIES [365]`, `FVRCP [21,21,365]`, `FELV [21,365]`, `LEPTO [21,365]`,
    `BORDETELLA [365]`. `IntervalFor(code, dose)` clamps past the series end and returns
    `AnnualBoosterDays = 365` for an unknown code (never "no due date"); `TotalDosesFor(code)`.
  - `Clinical/VaccinationRecord.cs` — `sealed class VaccinationRecord : AggregateRoot` + `enum
    VaccinationState { Administered, DueSoon, Overdue, Skipped }`. `Administer(...) →
    Result<VaccinationRecord>` derives `NextDueUtc = AdministeredUtc + IntervalFor(code, dose)`,
    uppercases the code, defaults `TotalDoses` from the schedule, validates the dose
    (`VACCINE_DOSE_INVALID`) and raises `VaccinationAdministeredEvent`. `StateAt(nowUtc)` is the derived
    read (`DueSoonWindow = 30 d`; `Overdue` when `NextDueUtc < nowUtc`); the stored `State` only
    separates a live series from a `Skip(...)`-ed one.
  - `Clinical/MedicalHistoryEntry.cs` — `sealed class MedicalHistoryEntry : SyncableEntity` + `enum
    MedicalHistoryKind { Visit, Vaccination, Surgery, Lab, Weight, Prescription, Boarding, Note }`.
    Public constructor only — **zero methods, zero public setters** (ARCHITECTURE §8.5: history is
    preserved in full; a correction is a new row). `PetId`, `Kind`, `Title`, `Detail?`, `OccurredUtc`,
    `SourceEntityId?`, `SourceEntityType?`, `RecordedByUserId`.
  - `Events/ClinicalEvents.cs` — `PetRegisteredEvent`, `VisitCompletedEvent`,
    `VisitReadyForInvoiceEvent`, `VaccinationAdministeredEvent`, all `sealed record : DomainEvent`
    (EventId/TenantId/OccurredAtUtc + payload). Dispatch to `MedicalHistoryEntry` stays in A10.
- **Files modified (src, 1):** `Results/ErrorCodes.cs` — new `#region Patients & Clinical (Phase A04)`
  **appended** (nothing reordered) with 7 codes: `PET_SPECIES_IMMUTABLE` (Integrity),
  `VISIT_INVALID_TRANSITION` (Conflict), `VISIT_DIAGNOSIS_REQUIRED` (Validation), `VISIT_LOCKED`
  (Conflict), `WEIGHT_OUT_OF_RANGE` (Validation), `DOCUMENT_TARGET_REQUIRED` (Validation),
  `VACCINE_DOSE_INVALID` (Validation). Catalog total 17 → 24.
- **Contracts added:** 7 entities (`Owner`, `Pet`, `WeightEntry`, `DocumentReference`, `Visit`,
  `VaccinationRecord`, `MedicalHistoryEntry`), 3 value objects (`PetVitals`, `PrescriptionLine`,
  `VisitServiceLine`), 10 enums (`OwnerGender`, `PetSpecies`, `PetGender`, `PetStatus`, `DocumentKind`,
  `VisitState`, `VisitReason`, `VisitLineSource`, `VaccinationState`, `MedicalHistoryKind`), 1 static
  (`VaccineSchedule`), 4 domain events. **No `Animora.Contracts` change** — domain enums are duplicated,
  never imported (CONVENTIONS §2); Track C maps between them.
- **DI changes:** none. **Mocks deleted:** none. **Migrations created:** none (A09 maps these).
- **Package/project references added:** **none** — `Animora.Domain.csproj` is still an empty
  `Microsoft.NET.Sdk` project with zero references; `LayeringRulesTests` still green.
- **Tests added (4 classes, 45 facts; `Animora.Domain.Tests` 45 → 90):**
  - `VisitStateMachineTests` (7) — all **36** `VisitState` pairs checked against a literal allowed-set
    (only the 6 tabled ones pass); the full Draft→Invoiced happy path with both events in order; cancel
    legal from `Draft` and `InProgress`; both terminal states refuse every further transition;
    `AttachInvoice` before the handoff is refused. Hosts the shared advancing `TestClock : IClock`.
  - `VisitInvariantTests` (4) — `Complete` without (and with a whitespace-only) diagnosis returns
    `VISIT_DIAGNOSIS_REQUIRED` and raises nothing; after `Invoiced`, add/remove of service and
    prescription lines all return `VISIT_LOCKED`; lines stay editable through `ReadyForInvoice` and a
    removal of a missing line is `NOT_FOUND`; vitals/assessment are closed once the encounter ends.
  - `VaccinationDueDateTests` (18 cases) — the per-dose interval and resulting `NextDueUtc` for all six
    seeded codes; clamp past the series end and unknown code → 365 d; the event carries the derived due
    date; `StateAt` boundaries exact to ±1 second (`due+1s` Overdue, `due` and `due−30 d` DueSoon,
    `due−30 d−1 s` Administered); `Skip` clears the due date permanently; invalid dose numbers.
  - `PatientInvariantTests` (16 cases) — species has no public setter and no method accepting a
    `PetSpecies` (reflection) and registration raises `PetRegisteredEvent`; `AgeMonthsAt` boundaries and
    the null case; weight range rejections and the append-only shape; document target requirement plus
    all three single-target forms; `NormalizePhone` over spaced/`+`-prefixed/Persian-digit/parenthesised
    input; owner trim + idempotent block/unblock; `MedicalHistoryEntry` asserted by reflection to have
    **no declared public method and no public setter**.
- **Verification:** `scripts/sandbox/build.sh full` = **0 warnings / 0 errors**;
  `scripts/sandbox/test.sh` = **TESTS OK** (Domain 90/90, other 4 projects 1/1 each);
  `scripts/sandbox/run.sh` = **SMOKE RUN OK**.
- **Deviations:** none material. Additions inside phase scope, needed to make the aggregates usable
  without re-deriving rules later: factories (`Owner.Register`, `Pet.Register`, `Visit.Open`) because
  every aggregate has private setters and an `IClock`-supplied instant; `Visit.SetAssessment`
  (diagnosis/plan/notes have to be writable for `Complete`'s invariant to be reachable),
  `Visit.IsTransitionAllowed`/`AreLinesLocked`/`CancellationReason`; `Owner.UpdateContactDetails`/
  `Block`/`Unblock`; `Pet.UpdateProfile`/`SetMicrochip`/`SetClinicalFlags`/`SetStatus`;
  `VaccineSchedule.TotalDosesFor`; `VaccinationRecord.Skip` (the `Skipped` enum member is otherwise
  unreachable). `PET_SPECIES_IMMUTABLE` is declared but not thrown by the domain: immutability is
  enforced structurally (no mutator exists), so the code is reserved for the A-layer command that
  rejects a species-change request — asserted by reflection instead of by a runtime guard.

### A03 — Domain: identity, tenant, staff, device (2026-09-23)

- **Files added (src, 11 — all in `src/Animora.Domain/Identity/`; the A02 `.gitkeep` placeholder removed):**
  - `Tenant.cs` — `sealed class Tenant : AggregateRoot` + `enum TenantStatus { Active, Suspended, Closed }`.
    `DisplayName`, `LegalName?`, `Timezone` (IANA, default `Asia/Tehran` via `DefaultTimezone` const),
    `DefaultCurrency` (default `CurrencyScale.Irt` = `"IRT"`), `Status`. `Create(...)` sets
    `TenantId == Id` so the tenant row is covered by the same uniform tenant filter; `SetStatus`, `Rename`.
  - `User.cs` — `sealed class User : AggregateRoot` + `enum UserStatus { Active, Disabled, Locked }`.
    `UserName` (lowercase invariant, unique per tenant), `DisplayName`, `PasswordHash?`, `SecurityStamp`
    (ULID), `Status`, `LastLoginUtc?`, `FailedAttemptCount`, `LockoutEndUtc?`. Policy constants
    `MaxFailedAttempts = 5`, `LockoutDuration = 15 min`, `DefaultFailureWindow = 15 min`. Methods
    `RegisterFailedAttempt(IClock, threshold, window)` (returns whether it triggered the lockout; a streak
    older than the window is discarded), `ClearFailures()` (never re-activates a `Disabled` account),
    `IsLockedOut(nowUtc)` (fail-closed when `LockoutEndUtc` is null), `RegisterSuccessfulLogin(IClock)`,
    `RotateSecurityStamp()`, `SetPasswordHash(string)` (rotates the stamp), `Disable()`, `Enable()`.
  - `StaffRole.cs` — `enum StaffRole { Manager, Doctor, Receptionist, Cashier, Accountant }`.
    Mapping to `Animora.Contracts.Navigation.UserRole` deferred to C10 (Contracts untouched).
  - `Permission.cs` — `sealed class Permission` (`Code` PK, `Category`, `RiskLevel Risk`) +
    `enum RiskLevel { Low, Normal, High, Critical }`; `Create(code, risk, category?)`, `CategoryOf(code)`
    (first dotted segment).
  - `PermissionCodes.cs` — **42 seed codes** as `const string` members in 5 regions (patients/reception,
    clinical, appointments/boarding, inventory/purchasing, sales/payments, finance/reporting,
    administration), exactly the strings listed in the phase file. `All` (ordered `IReadOnlyList<string>`),
    `Exists(code)`, `RiskOf(code)` (Critical set of 7 → High set of 6 → `*.read` = Low → Normal),
    `CreateCatalog()`.
  - `RoleDefinition.cs` — `sealed class RoleDefinition : AggregateRoot`: `Role`, `DisplayKey`
    (`role.<lowercase>`), `IsSystem`, `PermissionCodes` (`IReadOnlySet<string>` over a private ordinal
    `HashSet`). `CreateSystem(tenantId, role, IClock)` seeds from `DefaultRolePermissions`;
    `Grant`/`Revoke` throw `InvalidOperationException` for the system `Manager` definition and
    `ArgumentException` for an undeclared code; `Has(code)`.
  - `DefaultRolePermissions.cs` — the role→permission matrix exactly as tabled, built **only** from
    `PermissionCodes` constants (`using P = ...` alias) so a rename is a compile error. Manager = all 42;
    Doctor 16; Receptionist 14; Cashier 12; Accountant 14. `All`, `For(role)` (empty set = fail closed).
  - `StaffMembership.cs` — `sealed class StaffMembership : AggregateRoot` + `enum MembershipStatus
    { Active, Suspended, Ended }`: `UserId`, `Role`, `Status`, `JoinedUtc`, `EndedUtc?`,
    `ExtraPermissionCodes`, `DeniedPermissionCodes`. `Create`, `IsActive`, `ChangeRole`, `Suspend`,
    `Reinstate` (an ended membership is never reopened), `End(IClock)` (idempotent, keeps the first
    instant), `GrantExtra`/`Deny` (mutually exclusive — deny wins), `ClearOverride`. Documented as the
    **only** `(User, Tenant)` link; `TenantId` is resolved from it server-side (A-RULE 012).
  - `Device.cs` — `sealed class Device : AggregateRoot` + `enum DeviceStatus { Pending, Active, Revoked }`:
    `UserId`, `DeviceName`, `InstallationIdHash` (SHA-256 hex; the identity itself never leaves the device),
    `Platform`, `Status`, `RegisteredUtc`, `LastContactUtc?`, `RevokedUtc?`, `RevocationReason?`.
    `Register(...)` → `Pending`; `Approve()` (throws for a revoked device), `IsUsable`,
    `RecordContact(IClock)`, `Revoke(reason, IClock)` **idempotent** — a second call keeps the first
    instant and reason so the audit trail stays truthful.
  - `CapabilitySnapshot.cs` — `sealed record CapabilitySnapshot(TenantId, UserId, DeviceId, Role,
    CapabilityExpiry, IssuedUtc, SnapshotExpiryUtc, SecurityStamp, Signature)`. `Allows(code, nowUtc)`
    **fails closed**: blank/unknown code → false, snapshot expired → false, capability expiry `<= nowUtc`
    → false (so a zero window never grants). `IsExpired(nowUtc)`. Helper
    `BuildExpiry(codes, issuedUtc)` applies `OfflineCapabilityPolicy` per code — signing/verification
    stays server-side (D03/E07); `Signature` is an opaque base64 string here.
  - `OfflineCapabilityPolicy.cs` — the authoritative windows: `Low = 30 d`, `Normal = 14 d`,
    `High = 72 h`, `Critical = 0`; `WindowFor(RiskLevel)` (unknown level → `TimeSpan.Zero`),
    `WindowForCode(code)`. Documented as ADR-gated values.
- **Files modified (src, 1):** `Results/ErrorCodes.cs` — new `#region Identity (Phase A03)` **appended**
  (no existing entry reordered) with 6 codes, all category `Forbidden`: `TENANT_SUSPENDED`,
  `USER_DISABLED`, `USER_LOCKED_OUT`, `MEMBERSHIP_NOT_ACTIVE`, `DEVICE_REVOKED`,
  `CAPABILITY_NOT_GRANTED`. Catalog total 11 → 17.
- **Contracts added:** 6 entities (`Tenant`, `User`, `Permission`, `RoleDefinition`, `StaffMembership`,
  `Device`), 6 enums (`TenantStatus`, `UserStatus`, `StaffRole`, `RiskLevel`, `MembershipStatus`,
  `DeviceStatus`), 1 value object (`CapabilitySnapshot`), 3 statics (`PermissionCodes`,
  `DefaultRolePermissions`, `OfflineCapabilityPolicy`). **No `Animora.Contracts` change**, no wire DTO,
  no EF configuration, no persistence.
- **DI changes:** none. **Mocks deleted:** none. **Migrations created:** none.
- **Package/project references added:** **none** — `Animora.Domain.csproj` is still an empty
  `Microsoft.NET.Sdk` project with zero `PackageReference`/`ProjectReference` (CONVENTIONS §2);
  `LayeringRulesTests` still green.
- **Tests added (3 classes, 21 facts; `Animora.Domain.Tests` 24 → 45):**
  - `UserLockoutTests` (5) — the 5th failure sets `Locked` + `LockoutEndUtc = now + 15 min`; attempts
    outside the window restart the streak; a successful login clears status/count/end and stamps
    `LastLoginUtc`; `ClearFailures` does not revive a `Disabled` account; `SetPasswordHash` normalizes
    the user name and rotates the stamp. Uses a local advancing `TestClock : IClock` — no wall clock.
  - `RolePermissionMatrixTests` (8 methods / 10 cases) — every string in every role's set resolves to a declared code;
    the catalog has no duplicates and `CreateCatalog` risk-matches `RiskOf`; the risk table is asserted
    literally (7 Critical, 6 High, every `*.read` Low, `patients.write` Normal); Manager == `All`;
    Doctor/Receptionist/Cashier hold **no** Critical code; Accountant's Critical subset is exactly
    `{finance.period.close, payments.reverse}`; the system Manager definition rejects `Grant`/`Revoke`
    and a non-Manager one accepts idempotent grant/revoke but rejects an unknown code; membership carries
    the only `(User, Tenant)` link, deny beats extra, and `End` is idempotent.
  - `CapabilitySnapshotTests` (6) — an expired snapshot denies even a `Low` read; an unknown/ungranted/
    blank code is denied; a `Critical` code is denied at `IssuedUtc` because its window is zero;
    a `High` code is allowed at +71 h and denied at +73 h while the snapshot itself is still valid
    (capability expiry enforced independently); the four offline windows are the published numbers;
    device revocation is idempotent and a revoked device cannot be re-approved.
- **Verification:** `scripts/sandbox/build.sh full` = **0 warnings / 0 errors**;
  `scripts/sandbox/test.sh` = **TESTS OK** (Domain 45/45, other 4 projects 1/1 each);
  `scripts/sandbox/run.sh` = **SMOKE RUN OK**.
- **Deviations:** none material. Additions beyond the literal file list, all inside phase scope and
  needed to keep the types usable without re-deriving rules later: factory methods (`Create`/`Register`/
  `CreateSystem`) because every aggregate has private setters and an `IClock`-supplied instant;
  `User.IsLockedOut`/`RegisterSuccessfulLogin`/`Enable`/`Disable`; `Permission.CategoryOf`;
  `PermissionCodes.Exists`/`RiskOf`/`CreateCatalog`; `DefaultRolePermissions.For`;
  `StaffMembership` override/lifecycle helpers; `Device.Approve`/`RecordContact`/`IsUsable`;
  `CapabilitySnapshot.BuildExpiry`; `OfflineCapabilityPolicy.WindowForCode`. `RoleDefinition.Grant`
  additionally rejects undeclared codes (fail-closed, CONVENTIONS §7). No `IPermissionEvaluator`,
  no hashing, no signing, no EF mapping — those stay in B03/B04/D03/A09 as scoped.

### A02 — Domain kernel (2026-09-23)

- **Files added (src, 15 — all in `src/Animora.Domain/`, all new folders):**
  - `Primitives/SyncableEntity.cs` — syncable shape base: `Id` (init, ULID), `TenantId`,
    `UpdatedAt`, `Version` (nullable, server-issued), `Deleted`; protected EF ctor; **no EF attribute**.
    XML docs state `UpdatedAt`/`Version` are transport bookkeeping only (A-RULE 009/010).
  - `Primitives/LocalOnlyEntity.cs` — `Id` + `CreatedAtUtc`; never replicated.
  - `Primitives/LocalOnlyAttribute.cs` — `[LocalOnly]` class marker (consumed by A09 model builder, E03 scope).
  - `Primitives/IHasDomainEvents.cs` — `IReadOnlyList<DomainEvent> DomainEvents` + `ClearDomainEvents()`.
  - `Primitives/AggregateRoot.cs` — `SyncableEntity` + private `List<DomainEvent>` + `protected Raise(...)`;
    buffer exposed read-only.
  - `Primitives/IdGenerator.cs` — monotonic 26-char Crockford base32 ULID (48-bit ms + 80-bit
    `RandomNumberGenerator` randomness), lock-guarded same-ms increment; `NewId()` additionally clamps a
    backwards OS-clock jump to the last emitted ms so process-wide ordering holds; `NewId(DateTimeOffset)`
    honours the caller's instant verbatim (tests).
  - `Events/DomainEvent.cs` — `abstract record DomainEvent(string EventId, string TenantId,
    DateTimeOffset OccurredAtUtc)`; no `INotification`, no MediatR.
  - `ValueObjects/Money.cs` — `readonly record struct Money(long Minor, string Currency)`: `ZeroIrt`,
    `Irt(long)`, `Of(long,string)` (3-letter uppercase validation), `Zero(string)`, `Scale`, `IsZero`,
    `IsNegative`, `Abs`; `+ - unary- *` (int/long, both operand orders) with `checked` arithmetic and named
    alternatives (`Add`/`Subtract`/`Negate`/`Multiply`); `IComparable<Money>`/`IComparable` + `< <= > >=`;
    currency mismatch throws `InvalidOperationException` (defect, not control flow);
    `Allocate(int)` largest-remainder (sign-aware, conserves every minor unit);
    `ApplyPercent(decimal)` with `MidpointRounding.AwayFromZero` — the product's single rounding site.
  - `ValueObjects/CurrencyScale.cs` — `Of(currency)`: `IRT` → 0, `USD`/`EUR` → 2, default 2; `Irt` const.
  - `ValueObjects/Quantity.cs` — `readonly record struct Quantity(decimal Value, string UnitCode)`:
    `Zero(unit)`, `IsZero`, `IsNegative`, `Abs`, `+ - unary-` with ordinal unit-code check (throws on
    mismatch). Negative values intentionally legal (movements are signed).
  - `Time/IClock.cs` — `DateTimeOffset UtcNow`. `Time/IMonotonicClock.cs` — `ElapsedTicks`,
    `HighWaterUtc` (never decreases), `Observe(DateTimeOffset serverUtc)`; A-RULE 011 documented.
    Implementations deferred to A11 as specified.
  - `Results/ErrorCode.cs` — `readonly record struct ErrorCode(string Code, string Category)` + the 7
    category consts (`Validation`, `NotFound`, `Conflict`, `Forbidden`, `Integrity`, `Infrastructure`,
    `Entitlement`) + `None`/`IsNone`.
  - `Results/ErrorCodes.cs` — central catalog seeded with the 11 cross-cutting codes:
    `VALIDATION_FAILED`, `NOT_FOUND`, `CONCURRENCY_CONFLICT`, `PERMISSION_DENIED`, `TENANT_MISMATCH`,
    `IDEMPOTENCY_FINGERPRINT_MISMATCH`, `DEPENDENCY_UNSETTLED`, `ENTITLEMENT_REQUIRED`,
    `OFFLINE_CAPABILITY_EXPIRED`, `STORE_UNAVAILABLE`, `UNEXPECTED`. Append-only by contract.
  - `Results/Result.cs` — `Result` and `Result<T>` (`IsSuccess`/`IsFailure`/`Value`/`Error`/`Detail`),
    `Ok`/`Fail` factories, implicit `T → Result<T>` (+ named `FromValue`), `Map`, `Bind` (all arities),
    `ToResult`, `OrElse`. `Detail` documented as a developer string, never localized text.
  - `Identity/` created empty (`.gitkeep`) — filled by A03.
- **Files modified (src, 1):** `src/Animora.Domain/README.md` — folder-layout table + the zero-dependency,
  no-Guid, no-system-clock, no-decimal-money, append-only-ErrorCodes rules.
- **Contracts added:** domain primitives + value objects + clock ports + results listed above.
  No `Animora.Contracts` change, no `Animora.Contracts.Wire` change, no DbContext, no wire DTO.
- **DI changes:** none (Domain has no composition root; clock implementations and registration are A11).
- **Mocks deleted:** none (Track C protocol not yet in effect).
- **Migrations created:** none.
- **Package/project references added:** **none** — `Animora.Domain.csproj` remains an empty
  `Microsoft.NET.Sdk` project with zero `PackageReference` and zero `ProjectReference` (CONVENTIONS §2).
- **Tests added (4 classes, 24 facts total in `Animora.Domain.Tests`):**
  - `MoneyTests` — exact minor-unit arithmetic/comparison; currency mismatch throws on `+`/`-`/`CompareTo`
    and bad-code `Of` throws; `Allocate` conserves the sum and keeps shares within one unit (incl. a
    negative-amount case); `Allocate(3)` of 100 IRT = `34/33/33`; `ApplyPercent` away-from-zero
    (`105 × 50% → 53`, `-105 × 50% → -53`).
  - `QuantityTests` — add/sub keep unit and allow negatives; unit mismatch throws.
  - `IdGeneratorTests` — 26 chars, Crockford alphabet only, cross-ms sortability, same-ms strict
    monotonicity with identical timestamp prefix, and the 10 000-id loop asserting zero duplicates and
    strictly ascending ordinal order.
  - `ResultTests` — Ok/Fail state + code/category/detail, implicit conversion + `FromValue`/`OrElse`,
    `Map` transform/propagate, `Bind` chain/short-circuit, `ToResult`.
  - `LayeringRulesTests` unchanged and still green (Domain still references no `Animora.*` assembly).
- **Verification:** `scripts/sandbox/build.sh full` = **0 warnings / 0 errors**;
  `scripts/sandbox/test.sh` = `TESTS OK` (Domain 24 passed, other 4 projects 1 placeholder each);
  `scripts/sandbox/run.sh` = `SMOKE RUN OK` (desktop untouched);
  `grep -rn "DateTime.Now\|DateTimeOffset.UtcNow\|Guid.NewGuid" src/Animora.Domain` returns nothing.
- **Deviations:**
  - **D-A02-1** — The phase file placed the category consts in `Results/ErrorCode.cs` without naming a
    holder type. They are declared as consts **on `ErrorCode` itself** (`ErrorCode.Validation`, …) rather
    than in a second public type, to keep one public type per file (CONVENTIONS §3). No file count change.
  - **D-A02-2** — `IdGenerator.NewId()` obtains the instant from `TimeProvider.System`; `IClock` cannot be
    injected into a static generator and lives in the same assembly. This is an id prefix only, never a
    business sequence or licensing authority (A-RULE 011). The forbidden-call grep stays clean.
  - **D-A02-3** — Added conveniences beyond the phase list, all additive and non-breaking: `Money.Of`,
    `Money.Zero`, `Abs`, `IsZero`/`IsNegative`, named operator alternatives (CA2225 compliance),
    `Quantity.Abs`/`Negate`, `Result<T>.FromValue`/`ToResult`/`OrElse`.
  - **D-A02-4** — `src/Animora.Domain/Identity/` is tracked via a `.gitkeep` (git cannot store an empty
    directory); A03 removes it when it adds the first identity entity.

### A01 — Backend track scaffold (2026-09-23)

- **Files added (src, 4 new projects):**
  - `src/Animora.Contracts.Wire/` — `Animora.Contracts.Wire.csproj` (no refs) + `WireContract.cs`.
  - `src/Animora.Persistence.Sqlite/` — csproj (refs Domain + Application; packages EF Core Sqlite/Design,
    SQLitePCLRaw.bundle_e_sqlite3, SourceGear.sqlite3) + `README.md` (filled by A09).
  - `src/Animora.Infrastructure.Local/` — csproj (refs Domain + Application; packages Logging.Abstractions,
    Options) + `README.md` (owning phases A02 clocks, A11 keystore/encryption, B05 logging).
  - `src/Animora.Client.Services/` — csproj (refs Application, Contracts, Persistence.Sqlite,
    Infrastructure.Local) + `README.md` (Track C DI swap target).
  - Anchor types `src/Animora.Domain/AssemblyMarker.cs` and `src/Animora.Application/AssemblyMarker.cs`
    (see deviation D-A01-1) so the layering test can load empty placeholder assemblies deterministically.
- **Files modified (src):**
  - `src/Animora.Application/Animora.Application.csproj` — now refs Domain + Contracts + Contracts.Wire,
    and packages Logging.Abstractions + Options.
  - `src/Animora.Sync.Abstractions/Animora.Sync.Abstractions.csproj` — now refs Domain + Contracts.Wire.
- **Files added (tests, 5 new projects):** `tests/Animora.Domain.Tests` (xunit; refs Domain + Application +
  Contracts so LayeringRulesTests can load them), `tests/Animora.Application.Tests`,
  `tests/Animora.Persistence.Tests`, `tests/Animora.Sync.Tests`, `tests/Animora.Server.Tests` (xunit only,
  no subject ref until D01). Each has a placeholder test; Server.Tests keeps only the placeholder.
- **Build/infra files modified:** `Directory.Packages.props` (10 backend pins added), `Directory.Build.props`
  (`NuGetAuditMode=all`, `EnableNETAnalyzers=true`, `TreatWarningsAsErrors=false`), `Animora.sln`
  (4 src projects under `src` folder + 5 test projects under a new `tests` folder), `.gitignore`
  (`**/TestResults/`), `scripts/sandbox/test.sh` added (env + `dotnet test Animora.sln`, prints `TESTS OK`,
  optional `<project-name-filter>` arg).
- **Package pins added (exact, no ranges — T-RULE 010):** `Microsoft.EntityFrameworkCore 10.0.12`,
  `Microsoft.EntityFrameworkCore.Sqlite 10.0.12`, `Microsoft.EntityFrameworkCore.Design 10.0.12`,
  `Microsoft.Extensions.Logging.Abstractions 10.0.12`, `Microsoft.Extensions.Options 10.0.12`,
  `SQLitePCLRaw.bundle_e_sqlite3 3.0.3`, `SourceGear.sqlite3 3.50.4.5`, `xunit 2.9.2`,
  `xunit.runner.visualstudio 2.8.2`, `Microsoft.NET.Test.Sdk 17.12.0`. No Datasync / SQL Server /
  ASP.NET Core / encryption-bundle pins (owned by later phases).
- **Contracts added:** `Animora.Contracts.Wire.WireContract` (`Version = 1`, `HeaderName =
  "X-Animora-Contract-Version"`). No `Animora.Contracts` change, no entity, no DbContext.
- **DI changes:** none. `Animora.Desktop` still references only its existing projects and runs entirely on
  mocks; it does NOT reference `Animora.Client.Services` (that is C01).
- **Mocks deleted:** none.
- **Migrations created:** none.
- **Tests added:** `LayeringRulesTests` (3 facts: Domain has no forbidden refs; Application has no EF/
  Datasync refs; Contracts references no `Animora.*`) + 4 placeholder tests (Application/Persistence/Sync/
  Server). Domain.Tests runs 3 facts, total 7 tests across the suite.
- **Verification:** `scripts/sandbox/build.sh full --clean-restore` = 0 warnings / 0 errors (14 projects);
  `scripts/sandbox/test.sh` = `TESTS OK`, all 5 test projects discovered and green;
  `scripts/sandbox/run.sh` = `SMOKE RUN OK`; `dotnet sln list` = 9 src + 5 test projects; layering test
  confirmed RED when an EF Core reference was temporarily added to `Animora.Domain`, then reverted;
  `grep -r "CommunityToolkit.Datasync\|EntityFrameworkCore" src/Animora.Domain src/Animora.Application`
  returns nothing in source (`.cs`/`.csproj`).
- **Deviations:** see D-A01-1 below (anchor `AssemblyMarker` types added to Domain/Application).

## Decision & deviation log

> Append an entry whenever an executing session deviates from its phase file, or makes a decision the
> phase file did not pre-make. Format:

```
### <PhaseID> — <short title>            (date: YYYY-MM-DD)
Context:    what forced the decision
Decision:   what was done instead
Rule check: which A-RULE / T-RULE / CONVENTIONS § was consulted and why this complies
Impact:     phases affected + what a later session must know
```

### A01 — anchor AssemblyMarker types in Domain & Application            (date: 2026-09-23)
Context:    `Animora.Domain` and `Animora.Application` are still empty placeholders (no types). The phase
            requires `LayeringRulesTests` to "force assembly loading with a `typeof(...)` touch of one type
            per asserted assembly". With no type to touch, the assemblies could not be loaded/anchored and
            the C# compiler would omit their (unused) project references from the manifest.
Decision:   Added a single empty `public static class AssemblyMarker` to `src/Animora.Domain` and
            `src/Animora.Application` purely as a `typeof(...)` anchor. No behavior, no data, no dependency
            direction change. (`Animora.Contracts` already has real types, so it uses
            `typeof(Contracts.Navigation.UserRole)`.)
Rule check: CONVENTIONS §1 (no TFM/Nullable in the marker's project — inherited; unchanged), §2 (adds no
            reference — Domain still depends on nothing), §4 (sealed/`static` type). The phase file itself
            mandates the `typeof(...)` touch, so this realizes rather than contradicts its intent.
Impact:     A02 onward may keep or replace these markers when real Domain/Application types appear; the
            layering facts key off assembly reference sets, not the marker, so removing them later is safe
            as long as another loadable type exists in each assembly.

### A08 — supporting members the phase table implied but did not name            (date: 2026-09-23)
Context:    The phase file specifies the posting rules as "pure functions taking the event payload +
            an `IClock` + a `periodId`", but a `JournalEntry` also needs an `EntryNumber`, a
            `PostedByUserId` and an `OperationId` — none of which any of the events carries, and all
            of which `JournalEntry.Create` requires. Three rules additionally need data the event
            payload does not carry at all: `InvoicePostedEvent` has only the grand total (not the
            per-line-kind revenue split, the discount or the tax), `GoodsReceiptPostedEvent` has only
            a `LineCount` (not the cost), and `StockMovementRecordedEvent` has only a quantity (the
            weighted-average cost is `StockCalculator`'s, per R-B23). The file also names the cheque
            mutators and `Expense.Post` but no actor parameter, no creator for `Cheque`/`Expense`,
            and no way to record the `JournalEntryId` the two entities declare.
Decision:   Added, all inside the eleven files the phase table already lists and with no new file:
            (1) `readonly record struct PostingContext(EntryNumber, PeriodId, PostedByUserId,
                OperationId?)` in `PostingRules.cs` — the bookkeeping the Application layer owns,
                passed as one argument instead of four, so the rules stay pure;
            (2) `ForInvoicePosted` additionally takes the read-only `Invoice`, `ForGoodsReceiptPosted`
                the `GoodsReceipt` + currency, and `ForStockMovementRecorded` the already-derived
                `Money cost`, and `ForInvoiceRefunded` the original `PaymentMethod` — the rules read
                these, never compute or store them, so they remain pure translations;
            (3) `ForInvoiceVoided` takes the original posting `JournalEntry` because the tabled rule
                *is* "reversal of the posting entry" and a reversal needs the entry it mirrors;
            (4) `Cheque.Register(...)` and `Expense.Create(...)` factories (the file lists mutators
                but no creator), mirroring `GoodsReceipt.Create` from A06;
            (5) an `actorUserId` parameter on every cheque transition — `ChequeEvent` declares an
                `ActorUserId` field the phase file itself specifies, which nothing else could fill;
            (6) `Cheque.LinkJournalEntry` / `Expense.LinkJournalEntry` and `Expense.MarkReversed`,
                the only writers of the `JournalEntryId` / `Reversed` state both entities declare;
            (7) `Expense.Edit` / `SetSettlement` / `AttachReceipt`, draft-only, so a `Draft` expense
                is actually editable before the one-way `Post` (otherwise `ExpenseState.Draft` and
                `EXPENSE_IMMUTABLE` would both be unreachable);
            (8) `LedgerAccount.Create`/`Rename`/`Deactivate`/`Activate` and
                `AccountingPeriod.Create`/`BeginClose`/`Lock`, without which the two entities could
                never be instantiated or reach the states their own enums declare;
            (9) `ForPaymentRegistered` maps the three methods the phase table does not list:
                `Insurance` → 1110, `Wallet` → 2100, and `Credit` → refused (it moves no money, so
                there is nothing to post) — the tabled Cash/Card/Transfer/Cheque rows are unchanged;
            (10) `PostingRules.Add` skips a zero-amount component rather than emitting a zero line,
                which is what keeps `ForInvoicePosted` valid for an invoice with no discount, no tax
                or no insurance (a zero line would otherwise trip `JOURNAL_LINE_AMOUNT_INVALID`).
Rule check: CONVENTIONS §2 (`Animora.Domain.csproj` still has zero references — verified), §3
            (naming), §4 (`sealed`/`record`/`Result` returns/`IClock` injected, never ambient), §11
            (build 0/0, smoke OK). A-RULE 002 is strengthened, not relaxed: none of these additions
            is a mutator on `JournalEntry`, which still has no setter of any kind, and `Expense`'s
            new mutators are all draft-only. A-RULE 003 holds: `MarkBounced`, `Return`, `Cancel` and
            `CreateReversal` each require a reason. PERFORMANCE §2: the `Create` validation and the
            `ForInvoicePosted` revenue split are single indexed `for` passes, no LINQ.
Impact:     A10 constructs a `PostingContext` per dispatched event (entry-number allocator + resolved
            period + actor + the envelope's `OperationId`) and loads the one aggregate the three
            data-hungry rules need; it must call `AccountingPeriod.EnsurePostable` before every
            `JournalEntry.Create`. A09 maps `Cheque.History` and `JournalEntry.Lines` as owned
            collections and gives `JournalEntry` **no** update path. A12 asserts the reflection
            "no public setter" fact against `JournalEntry` exactly as written above.

### A07 — supporting types the phase table implied but did not name            (date: 2026-09-23)
Context:    The phase file specifies `Invoice.DiscountKind DiscountKind`, an `InvoiceTotals` static that
            returns "the totals", `Invoice.MarkSettlement(SettlementStatus, string? errorCode)` and a
            `CashSession` aggregate, but the Domain layer owns no `DiscountKind` type (the existing one
            lives in `Animora.Contracts`, which `Animora.Domain` may never reference — CONVENTIONS §2),
            names no return type for the totals function, no storage for the settlement error code, and
            no constructor for a cash session.
Decision:   Added, all inside the nine files the phase table already lists and with no new file:
            (1) `enum DiscountKind { Percent, Amount }` in `Sales/InvoiceTotals.cs` — the Domain's own
            copy, member-for-member identical to the Contracts one, so no layering edge is created;
            (2) `readonly record struct InvoiceTotalsSnapshot` as `InvoiceTotals.Compute`'s return type;
            (3) `InvoiceTotals.ComputeDue(...)` so the documented `DueTotal` formula also lives in the
            single totals module instead of being re-implemented on the aggregate;
            (4) `Invoice.SettlementErrorCode` to actually hold `MarkSettlement`'s `errorCode` argument;
            (5) `CashSession.Open(...)` factory (the phase lists the four mutators but no creator) and
            `ReconciledByUserId` to hold `Reconcile`'s argument;
            (6) `Invoice.Create(...)` factory, mirroring `PurchaseOrder.Create` from A06.
            `CashMovement.Create` reuses A06's `MOVEMENT_SIGN_INVALID` rather than adding a 15th code —
            it is literally the same rule (kind fixes sign), and §3 forbids duplicate codes for one
            condition.
Rule check: CONVENTIONS §2 (Domain still references nothing — verified: `Animora.Domain.csproj` is
            empty), §3 (naming), §4 (sealed/record/`Result` returns/no ambient clock), A-RULE 002/003/006
            unaffected (all additions are read-only or factory paths). ARCHITECTURE §8.3's state list is
            reproduced exactly.
Impact:     A09 maps `Invoice.SettlementErrorCode` as a plain nullable string column; C07/C08 map the
            Domain `DiscountKind` to the Contracts one explicitly (a one-line switch, not a cast) —
            the two enums are deliberately separate types.
