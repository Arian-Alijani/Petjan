# Phase E03 — ISyncEngine + Datasync client adapter

**Band:** M · **Track:** E · **Depends on:** D08 · **Satisfies:** R-B03, R-B13, R-B31
**Runnable after this phase:** desktop offline yes · server required no (the engine degrades to
`Offline` with no server reachable)

## Read before starting
- `backend-roadmap/CONVENTIONS.md` §1, §2, §3, §4, §5, §6 (all 15 items), §7, §8, §9, §12
- `backend-roadmap/PROGRESS.md` (D08 entry — the 25 `<Entity>SyncContract` records,
  `SyncContractCatalog`, the binding replicated / non-replicated split, the **three** client-writable
  entities, `X-Animora-Contract-Version` + the two `409` actions + the header echo; A01 entry —
  `WireContract`; A02 entry — `IdGenerator`, `IClock`, `Result<T>`, `ErrorCodes`; A09 entry —
  `AnimoraLocalDbContext`, the `[LocalOnly]` marker, value converters; A11 entry — `ISecretStore`,
  `NativeSqliteAssertion`, `LocalStoreOptions`; C11 entry — `SyncCursor` + migration `019_SyncCursor`
  — note PROGRESS.md's deliberate `018→019` bump because `018_UserPreferences` was already taken)
- `ARCHITECTURE.md` §8.1, §8.2, §8.6 · `STACK.md` §4.1, §4.2, §4.3, §4.5
- `A-RULE 001`, `A-RULE 009`, `A-RULE 010`, `A-RULE 022`, `T-RULE 001`, `T-RULE 006`, `T-RULE 007`
- `src/Animora.Sync.Abstractions/` (placeholder to fill), `src/Animora.Persistence.Sqlite/Context/**`

## Goal
`Animora.Sync.Abstractions` becomes the project-owned replication port (`ISyncEngine`, `SyncScope`,
`SyncResult`, `SyncConflict`, `ISyncDiagnostics`) with **zero third-party types**, and
`Animora.Sync.Datasync` implements it over `CommunityToolkit.Datasync.Client` pinned exactly `10.1.2`.
This is the only project in the solution that may name a Datasync type. Replication pulls the 22 D08
contracts into the local store and pushes the three client-writable ones — it carries **no business
intent** (that is E06).

## Design decisions (pre-made — implement, do not re-derive)
- **The port (`Animora.Sync.Abstractions`, no package references at all):**
  ```
  ISyncEngine:
    Task<Result<SyncResult>> SynchronizeAsync(SyncScope scope, CancellationToken ct);
    Task<Result<SyncResult>> FullResyncAsync(SyncScope scope, string reason, CancellationToken ct);
  ```
  - `sealed record SyncScope(SyncDirection Direction, IReadOnlyList<string> EntityNames, bool IsCatchUp)`
    + `static SyncScope All(SyncDirection d)`; `enum SyncDirection { PullOnly, PushOnly, Both }`.
    An empty `EntityNames` means "every entity in `SyncContractCatalog`".
  - `sealed record SyncResult(SyncOutcome Outcome, int PulledCount, int PushedCount,
    IReadOnlyList<SyncEntityResult> PerEntity, IReadOnlyList<SyncConflict> Conflicts,
    ErrorCode? Error, TimeSpan Duration, DateTimeOffset CompletedAtUtc)`;
    `enum SyncOutcome { Completed, PartiallyCompleted, Offline, Rejected, Failed, ContractUpgradeRequired }`;
    `sealed record SyncEntityResult(string EntityName, int Pulled, int Pushed, bool Complete,
    string? CursorAfter, ErrorCode? Error)`.
  - `sealed record SyncConflict(string EntityName, string EntityId, ConflictKind Kind,
    string? LocalVersion, string? ServerVersion, string PayloadJson)`;
    `enum ConflictKind { VersionMismatch, DeletedOnServer, DeletedLocally, Rejected }`.
    **E03 only detects and reports conflicts; deciding is E05** — nothing is auto-resolved here.
  - `ISyncDiagnostics`: `Task<IReadOnlyList<SyncEntityStatus>> GetStatusAsync(CancellationToken ct)`;
    `sealed record SyncEntityStatus(string EntityName, DateTimeOffset? LastPulledAtUtc,
    string? Cursor, int PendingPushCount, ErrorCode? LastError, int ConsecutiveFailures)`.
  - `NullSyncEngine` (same project) returns `SyncOutcome.Offline`, so `Animora.Desktop` can resolve
    the port with no adapter registered and stay runnable offline.
- **Package pin:** `CommunityToolkit.Datasync.Client` **exactly `10.1.2`** in
  `Directory.Packages.props`, consumed **only** by `src/Animora.Sync.Datasync/`
  (`LayeringRulesTests` gains the fact; T-RULE 001 / A-RULE 001).
  **Pin verification (2026-10-07, closes Phase-Problem §7.1):** the NuGet flat-container index lists
  `CommunityToolkit.Datasync.Client` `10.0.0 … 10.1.2` (alongside `9.0.5`), and the `10.1.2` nuspec
  targets `net10.0` with `Microsoft.EntityFrameworkCore(.Sqlite/.Relational) 10.0.9`. So the client pin
  exists and matches the .NET 10 / EF Core 10 baseline (the upstream `…Server.OpenApi 9.0.5` ↔
  ASP.NET Core 10 incompatibility concerns a different package, which this solution does not use).
  **Execution-time guard (step 2 below):** `dotnet restore` must resolve exactly `10.1.2` for the client
  with **no** `NU1xxx` warning-as-error and no transitive downgrade; if it does not, stop and record an
  ADR in the decision log before touching the adapter — never silently float the version.
- **Project:** `src/Animora.Sync.Datasync/Animora.Sync.Datasync.csproj` references
  `Animora.Sync.Abstractions`, `Animora.Application`, `Animora.Persistence.Sqlite`,
  `Animora.Contracts.Wire`. `Animora.Desktop` references it from the composition root only.
  It **never** references `Animora.Contracts` (no UI seam here) and is never referenced by
  `Animora.Application` or `Animora.Domain`.
- **Local replica context — separate from the business context, by design.**
  `src/Animora.Sync.Datasync/Store/AnimoraOfflineDbContext.cs` derives from Datasync's
  `OfflineDbContext` and maps **only** the 25 `<Entity>SyncContract` types as EF entities, in the
  **same SQLite file** as `AnimoraLocalDbContext` but on its **own table set** prefixed `repl_`
  (e.g. `repl_Pets`), plus the library's own `DatasyncOperationsQueue` / `DatasyncDeltaTokens`
  tables. A migration named **`ReplicaTables`** is generated **from this offline context, numbered
  within the offline context's *own* migration sequence** (it starts fresh, so `001_ReplicaTables`
  under `src/Animora.Sync.Datasync/Migrations/`, with its own `__EFMigrationsHistory` table).
  `AnimoraLocalDbContext` is untouched by it — no phase edits a prior migration, and a migration
  generated from the offline context can never alter a table owned by the local context.
  **Rationale, binding:** business tables are domain entities with converters and invariants; replica
  tables are flat transport rows. Mixing them would put `UpdatedAt`/`Version` on domain entities and
  invite a business query to order by a transport column (A-RULE 009/010). Both contexts share the
  connection string and the A11 encryption provider.
- **Projection into the business store is an explicit, ordered step — not EF magic.**
  `src/Animora.Sync.Datasync/Projection/ReplicaProjector.cs`: after a successful pull, for each
  entity in dependency order (`SyncContractCatalog.ProjectionOrder`, a static list that puts
  `Tenant`, `RoleDefinition`, `UnitOfMeasure`, `ProductCategory`, `Owner`, `Supplier`, `Insurer`,
  `LedgerAccount`, `AccountingPeriod`, `Room`, `BoardingServiceDef`, `Product`, `StaffMembership`
  before `Pet`, `WeightEntry`, `DocumentReference`, `Visit`, `VaccinationRecord`,
  `MedicalHistoryEntry`, `Appointment`, `ReceptionQueueEntry`, `BoardingStay`), it maps
  `repl_*` rows → domain entities through
  `src/Animora.Sync.Datasync/Projection/ContractToEntityMappings.cs` (explicit per-entity
  `static` mapping methods, no reflection, no AutoMapper — PERFORMANCE.md §3) and applies them via
  `IUnitOfWork` in batches of **200** rows per `SaveChanges`.
  Projection rules, binding: (a) `Deleted = true` becomes a **soft delete** on the domain entity,
  never a physical delete; (b) a row whose domain aggregate has an **unsent `PendingCommand`** on the
  same `AggregateId` is **skipped and recorded** as `ConflictKind.VersionMismatch` — the A-RULE 017
  guarantee's first line, with E05 owning the full policy; (c) projection is idempotent; (d)
  `UpdatedAt`/`Version` stay **only** on the `repl_` row, never copied to a domain column.
- **Cursor storage reuses C11's `SyncCursor`,** which is owned by `AnimoraLocalDbContext`. Because it
  lives on a different context than the `repl_*` tables, its new columns ship as a **separate
  local-context migration** — `SyncCursorReplicaColumns`, numbered at the **next free ordinal on the
  local context at execution time** (verify the physical head; it is `031` as of 2026-10-07, so this
  would be `032` unless more migrations land first). It adds
  `EntityName nvarchar(64)`, `Cursor nvarchar(64)` (the library's delta-token value, opaque to us),
  `LastPulledAtUtc`, `ConsecutiveFailures int`, `LastErrorCode nvarchar(64)`, unique
  `UX_SyncCursors_EntityName`. The library keeps its own `DatasyncDeltaTokens`; our row is the
  **diagnostic mirror** `ISyncDiagnostics` reads, so the UI never queries a library table
  (T-RULE 006).
- **The adapter (`src/Animora.Sync.Datasync/DatasyncSyncEngine.cs`), pass algorithm:**
  1. `ISyncPreflight` check (below). If it fails → `SyncOutcome.Offline` or
     `ContractUpgradeRequired`; nothing is attempted.
  2. `PushOnly`/`Both`: `await context.PushAsync(entityTypes, ct)` for the **three** client-writable
     entities only (`Owner`, `Pet`, `WeightEntry`); `ParallelOperations` stays at the default **1**
     (`STACK.md` §4.3 — raising it may reorder operations server-side).
  3. `PullOnly`/`Both`: `await context.PullAsync(options, ct)` per entity in
     `ProjectionOrder`, `PageSize = 50` (matching D08's server `PageSize`), one entity at a time.
  4. `ReplicaProjector.ProjectAsync(...)`.
  5. Update `SyncCursor` rows; build `SyncResult`.
  Every step is wrapped so a single entity's failure yields `PartiallyCompleted` with that entity's
  `ErrorCode` and the others' results intact — **a sync pass is never all-or-nothing**.
- **`ISyncPreflight` (`src/Animora.Sync.Datasync/Transport/SyncPreflight.cs`):** before any call it
  (a) resolves the base address from `SyncOptions.ServerBaseUrl` (null ⇒ `Offline`), (b) sends the
  `WireContract.HeaderName` value on **every** request via a delegating handler
  `ContractVersionHandler`, and (c) maps D08's responses: `409 CONTRACT_VERSION_UNSUPPORTED` with
  `action = "resync-after-upgrade"` → `SyncOutcome.ContractUpgradeRequired` (and the engine performs
  **no** partial pull, so a stale client cannot mis-map), `action = "server-upgrade-required"` →
  `SyncOutcome.Rejected` with `CONTRACT_VERSION_UNSUPPORTED`. A response whose echoed header differs
  from the sent value is treated as a silent downgrade → `Rejected` + audit. `401`/`403` →
  `SyncOutcome.Rejected` with the D03/D04 error code (E07 owns re-authentication).
- **Full resync (`FullResyncAsync`), the explicit, audited path (`CONVENTIONS.md` §6.15):**
  (1) refuse with `FULL_RESYNC_BLOCKED` if any `PendingCommand` is unsettled — unsent intent is never
  discarded (E06 exposes the drain); (2) write an `AuditEntry` `audit.sync.fullresync` with the
  caller's `reason`; (3) in one transaction delete the library's pending ops + delta tokens for the
  scope and clear the `repl_` tables and matching `SyncCursor` rows; (4) pull, project, then rebuild
  derived state via C10's `RebuildActivityFeedAsync`; (5) return the `SyncResult`.
  The background loop never calls it (E04 calls it only on explicit user/diagnostic request or after
  a `ContractUpgradeRequired` upgrade).
- **`SyncOptions`** (`src/Animora.Sync.Datasync/SyncOptions.cs`, bound from the desktop config):
  `ServerBaseUrl` (nullable), `PageSize = 50`, `HttpTimeoutSeconds = 30`,
  `MaxEntitiesPerPass = 25`, `ProjectionBatchSize = 200`, `AllowInsecureHttp = false`
  (dev-only flag, refused when `ServerBaseUrl` is not `localhost`). `Validate()` fails loud.
- **Token supply is a port, not an implementation here.**
  `Animora.Sync.Abstractions/ISyncCredentialProvider.cs` →
  `Task<Result<string>> GetAccessTokenAsync(CancellationToken ct)`, with an
  `AnonymousSyncCredentialProvider` default returning `AUTH_REQUIRED`. E07 supplies the real one.
  The adapter attaches the token in a `BearerTokenHandler`; it never reads a keystore itself.
- **DI:** `src/Animora.Sync.Datasync/DependencyInjection/AddAnimoraDatasync.cs` registers the
  offline context, `HttpClient` (one named client, `ContractVersionHandler` + `BearerTokenHandler`),
  `ISyncEngine → DatasyncSyncEngine`, `ISyncDiagnostics → DatasyncSyncDiagnostics`.
  `App.axaml.cs` calls `AddAnimoraDatasync()` **only when `SyncOptions.ServerBaseUrl` is set**;
  otherwise `ISyncEngine → NullSyncEngine`. `ISyncStatusService` keeps resolving to C11's
  `LocalSyncStatusService` in this phase — E04 replaces it.

## Files to create / modify
| Path | Action | Contents (one line) |
|---|---|---|
| `src/Animora.Sync.Abstractions/ISyncEngine.cs` | create | the port + `NullSyncEngine` |
| `src/Animora.Sync.Abstractions/SyncScope.cs` · `SyncResult.cs` · `SyncConflict.cs` | create | scope/result/conflict records + enums |
| `src/Animora.Sync.Abstractions/ISyncDiagnostics.cs` | create | diagnostics port + `SyncEntityStatus` |
| `src/Animora.Sync.Abstractions/ISyncCredentialProvider.cs` | create | token port + anonymous default |
| `src/Animora.Sync.Datasync/Animora.Sync.Datasync.csproj` | create | the only Datasync-client consumer |
| `src/Animora.Sync.Datasync/Store/AnimoraOfflineDbContext.cs` | create | `OfflineDbContext` over the 25 contracts |
| `src/Animora.Sync.Datasync/Store/ReplicaConfigurations.cs` | create | `repl_*` table mappings |
| `src/Animora.Sync.Datasync/Migrations/*` | create | `001_ReplicaTables` on the **offline** context's own sequence |
| `src/Animora.Persistence.Sqlite/Migrations/*` | create | `SyncCursorReplicaColumns` on the **local** context, next free ordinal (≈`032`) |
| `src/Animora.Sync.Datasync/Projection/ContractToEntityMappings.cs` | create | explicit per-entity mapping |
| `src/Animora.Sync.Datasync/Projection/ReplicaProjector.cs` | create | ordered, batched, idempotent projection |
| `src/Animora.Sync.Datasync/Transport/SyncPreflight.cs` | create | reachability + contract-version gate |
| `src/Animora.Sync.Datasync/Transport/ContractVersionHandler.cs` | create | sends + verifies the echoed header |
| `src/Animora.Sync.Datasync/Transport/BearerTokenHandler.cs` | create | attaches the token from the port |
| `src/Animora.Sync.Datasync/DatasyncSyncEngine.cs` | create | the 5-step pass + full resync |
| `src/Animora.Sync.Datasync/DatasyncSyncDiagnostics.cs` | create | `SyncCursor` → `SyncEntityStatus` |
| `src/Animora.Sync.Datasync/SyncOptions.cs` | create | options + `Validate()` |
| `src/Animora.Sync.Datasync/DependencyInjection/AddAnimoraDatasync.cs` | create | one registration entry point |
| `Directory.Packages.props` | modify | `CommunityToolkit.Datasync.Client` `10.1.2` |
| `Animora.sln` | modify | add `Animora.Sync.Datasync` |
| `src/Animora.Desktop/App.axaml.cs` | modify | conditional `AddAnimoraDatasync()` / `NullSyncEngine` |
| `tests/Animora.Sync.Tests/SyncPortContractTests.cs` | create | port purity: no third-party type |
| `tests/Animora.Sync.Tests/ReplicaProjectionTests.cs` | create | order, batching, idempotency, soft delete, skip-on-pending |
| `tests/Animora.Sync.Tests/SyncPreflightTests.cs` | create | offline, version 409s, header echo mismatch |
| `tests/Animora.Sync.Tests/FullResyncTests.cs` | create | blocked by pending, audited, rebuild invoked |
| `tests/Animora.Domain.Tests/LayeringRulesTests.cs` | modify | only `Animora.Sync.Datasync` references the client |

## Implementation steps
1. Fill `Animora.Sync.Abstractions` with the port, records, enums, `NullSyncEngine` and the
   credential port. Confirm the project still has **zero** package references.
2. Pin `CommunityToolkit.Datasync.Client 10.1.2`; create `Animora.Sync.Datasync`; add both to the
   solution.
3. Write `AnimoraOfflineDbContext` + `ReplicaConfigurations` (`repl_` prefix) and generate
   `001_ReplicaTables` **on the offline context's own migration sequence**. Separately, generate
   `SyncCursorReplicaColumns` on `AnimoraLocalDbContext` at the next free local ordinal (verify the
   physical head) for the new `SyncCursor` columns — the two migrations live on two contexts and must
   not be merged.
4. Write `ContractToEntityMappings` (one explicit method per entity) and
   `SyncContractCatalog.ProjectionOrder` (extend D08's catalog — additive only).
5. Write `ReplicaProjector` with the ordered, 200-row-batched, idempotent algorithm and the
   skip-on-unsent-`PendingCommand` rule that emits a `SyncConflict`.
6. Write `SyncPreflight`, `ContractVersionHandler`, `BearerTokenHandler`, `SyncOptions`.
7. Write `DatasyncSyncEngine` (5-step pass, per-entity isolation, `PartiallyCompleted`) and
   `FullResyncAsync` (refuse-on-pending → audit → clear → pull → project → rebuild).
8. Write `DatasyncSyncDiagnostics` over `SyncCursor`.
9. Write `AddAnimoraDatasync` and the conditional composition-root wiring; keep
   `LocalSyncStatusService` registered.
10. Write the four test classes; extend `LayeringRulesTests`.
11. Verify against a running server: a first pull populates the 25 `repl_` tables and projects into
    the business tables; a second pull transfers only changed rows; pushing an edited `Owner`
    succeeds; a pull that would overwrite an aggregate with an unsent command is skipped and
    reported.
12. `build.sh full` = 0/0; `test.sh` = `TESTS OK`; desktop `run.sh` = `SMOKE RUN OK` **with no
    server configured** (engine resolves to `NullSyncEngine`).

## Contracts / schema added
- `Animora.Sync.Abstractions`: `ISyncEngine`, `NullSyncEngine`, `SyncScope`, `SyncDirection`,
  `SyncResult`, `SyncOutcome`, `SyncEntityResult`, `SyncConflict`, `ConflictKind`,
  `ISyncDiagnostics`, `SyncEntityStatus`, `ISyncCredentialProvider`,
  `AnonymousSyncCredentialProvider`.
- `Animora.Sync.Datasync`: `AnimoraOfflineDbContext`, `ReplicaConfigurations` (25 `repl_*` tables),
  migration `001_ReplicaTables` (offline context) + `SyncCursorReplicaColumns` (local context),
  `ContractToEntityMappings`, `ReplicaProjector`, `SyncPreflight`,
  `ContractVersionHandler`, `BearerTokenHandler`, `DatasyncSyncEngine`, `DatasyncSyncDiagnostics`,
  `SyncOptions`, `AddAnimoraDatasync`.
- `SyncContractCatalog.ProjectionOrder` (additive extension of D08's catalog).
- `SyncCursor` += `EntityName`, `Cursor`, `LastPulledAtUtc`, `ConsecutiveFailures`, `LastErrorCode`.
- `ErrorCodes` += `SYNC_OFFLINE`, `SYNC_PARTIAL`, `SYNC_PROJECTION_FAILED`,
  `FULL_RESYNC_BLOCKED`, `CONTRACT_VERSION_ECHO_MISMATCH` (`CONTRACT_VERSION_UNSUPPORTED` and
  `AUTH_REQUIRED` already exist — reuse).
- Packages: `CommunityToolkit.Datasync.Client 10.1.2`.
- DI in `App.axaml.cs`: `ISyncEngine` + `ISyncDiagnostics` registered (conditional).
  `WireContract.Version` stays `1` — the client maps D08's existing contracts, changing no shape.
- No `Animora.Contracts` change, no `CommandTypes` entry, no mock deleted.

## Rules that apply here
- **A-RULE 001 / T-RULE 001** — `CommunityToolkit.Datasync.Client` appears only in
  `Animora.Sync.Datasync`; `SyncPortContractTests` proves the abstractions assembly exposes no
  third-party type in any public signature.
- **A-RULE 009 / A-RULE 010 / T-RULE 006** — `UpdatedAt`, `Version` and `DatasyncOperationId` live
  only on `repl_*` rows and library tables; no domain column or business query touches them.
- **A-RULE 017 / R-B18** — projection skips any aggregate with an unsent `PendingCommand` and
  reports it as a conflict; full resync refuses while unsent intent exists.
- **A-RULE 022 / R-B13** — this engine replicates state only; it never submits or settles a command,
  and the two pipelines share nothing but the HTTP client.
- **R-B31 / §8.6** — the contract-version header is sent on every request, the echo is verified, and
  an out-of-window response aborts the pass instead of partially pulling.
- **T-RULE 007** — no OData type or query string leaks past the adapter; the port speaks only in
  entity names.
- **CONVENTIONS §8 / PERFORMANCE.md §3** — `PageSize 50`, one entity at a time,
  `ParallelOperations = 1`, 200-row projection batches, explicit mappings, no reflection.
- **CONVENTIONS §11 (Track D–E form)** — with no `ServerBaseUrl`, `NullSyncEngine` keeps the desktop
  fully functional offline.

## Out of scope
- The background sync loop, scheduling, connectivity detection and the real `ISyncStatusService`
  → E04.
- Conflict **resolution**, the quarantine/needs-attention queue and the domain policy matrix → E05
  (this phase only detects and reports).
- Pushing `PendingCommand`s / command transport → E06 (`ICommandTransport` stays
  `NullCommandTransport` here).
- Real token acquisition, refresh and keystore storage → E07 (`ISyncCredentialProvider` default
  returns `AUTH_REQUIRED`).
- The full G1 delta-correctness gate matrix → E08.

## Acceptance criteria
- Build: `scripts/sandbox/build.sh full` = 0 warnings / 0 errors; the Datasync client pin resolves to
  exactly `10.1.2`.
- `SyncPortContractTests` green: every public type in `Animora.Sync.Abstractions` is defined in
  `Animora.Sync.Abstractions`, `Animora.Domain`, `Animora.Contracts.Wire` or the BCL; the project has
  zero `PackageReference`.
- `LayeringRulesTests` green: no project other than `Animora.Sync.Datasync` references
  `CommunityToolkit.Datasync.Client`; `Animora.Application`/`Animora.Domain` do not reference
  `Animora.Sync.Datasync`.
- `ReplicaProjectionTests` green: projection follows `ProjectionOrder` (a `Pet` never lands before
  its `Owner`); a `Deleted = true` contract row soft-deletes the domain entity and never removes it;
  re-running projection on the same replica set produces zero changes; an aggregate with an unsent
  `PendingCommand` is skipped and returned as `ConflictKind.VersionMismatch`; batches commit every
  200 rows and a mid-projection failure leaves earlier batches committed with
  `SyncOutcome.PartiallyCompleted`.
- `SyncPreflightTests` green: null `ServerBaseUrl` → `Offline` with zero HTTP calls; a
  `409 resync-after-upgrade` → `ContractUpgradeRequired` and **no rows written**; a
  `409 server-upgrade-required` → `Rejected`; a mismatched echoed header →
  `CONTRACT_VERSION_ECHO_MISMATCH`; `401` → `Rejected` with `AUTH_REQUIRED`.
- `FullResyncTests` green: refused with `FULL_RESYNC_BLOCKED` while an unsettled `PendingCommand`
  exists; otherwise writes one `audit.sync.fullresync` entry with the reason, clears tokens +
  `repl_*` + cursors in one transaction, and invokes `RebuildActivityFeedAsync`.
- Manual (real server): first pull populates all 25 `repl_` tables and the business tables; a second
  pull transfers only changed rows (row counts logged); an edited `Owner` pushes successfully; a
  write attempt on a non-writable entity is refused locally before any HTTP call.
- Runnability: with no `ServerBaseUrl` configured, `scripts/sandbox/run.sh` = `SMOKE RUN OK` and
  every screen still works against the local DB.
- **Encrypted replica tables (audit 2026-10-08 · SYNC-C-02):** the `repl_*` tables are created
  **inside the encrypted container** when the release/encryption profile is active (enablement itself
  lands in E07). A test opens a `repl_*` table through the store connection and asserts
  `PRAGMA cipher_version` is non-empty — the replicated PII must never be a plaintext surface.

## PROGRESS.md update
Mark E03 done; advance "Next phase" to `E04`; append a "Repo state" entry listing: the
`Animora.Sync.Abstractions` port surface (**E04/E05/E06/E07 all program against these exact types**),
`NullSyncEngine`, `ISyncCredentialProvider` + its anonymous default (**E07 replaces it**),
`Animora.Sync.Datasync` + the `10.1.2` client pin + the layering fact, `AnimoraOfflineDbContext` and the
`repl_*` table set + migration `001_ReplicaTables` (offline context), `SyncContractCatalog.ProjectionOrder`,
`ReplicaProjector`'s four binding rules (soft delete, skip-on-pending, idempotent, 200-row batches)
(**E05 builds its policy on top**), `SyncPreflight` + the contract-version outcomes,
`DatasyncSyncEngine`'s 5-step pass and `PartiallyCompleted` semantics, `FullResyncAsync`'s refuse/audit/
rebuild sequence (**E05 and E08 invoke it**), `SyncOptions` values, the extended `SyncCursor` columns
(**E04's status UI reads them**), the 5 new error codes, the conditional DI wiring, deviations.
