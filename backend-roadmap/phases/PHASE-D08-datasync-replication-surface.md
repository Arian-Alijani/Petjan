# Phase D08 — Datasync replication surface

**Band:** M · **Track:** D · **Depends on:** D05 · **Satisfies:** R-B03, R-B11, R-B31
**Runnable after this phase:** desktop offline yes · server required no (the client adapter is E03)

## Read before starting
- `backend-roadmap/CONVENTIONS.md` §1, §2, §3, §5, §6 (items 1, 3, 11, 12, 13), §7, §8, §9, §12
- `backend-roadmap/PROGRESS.md` (D02 entry — `ServerModel` replicated / non-replicated lists,
  `RowVersionCodec`, the `IX_<Table>_TenantId_UpdatedAt` index plan, `ITenantContext`; D04 entry —
  tenant resolution and the `X-Animora-Tenant` selector; A01 entry — `WireContract`)
- `ARCHITECTURE.md` §8.1, §8.2, §8.6, §9.2 (isolation layer 3) · `STACK.md` §4.2, §4.3, §5.1, §5.2
- `A-RULE 001`, `A-RULE 009`, `A-RULE 010`, `A-RULE 014`, `A-RULE 022`, `T-RULE 002`, `T-RULE 007`
- `src/Animora.Persistence.SqlServer/Context/**`, `src/Animora.Contracts.Wire/**`

## Goal
The server exposes `/tables/*` — one `TableController<T>` per replicated entity over
`EntityTableRepository<T>` — with a tenant-scoped `IAccessControlProvider<T>` that is **isolation
layer 3**, explicit per-entity wire contracts (never server write models), soft-delete enabled,
conservative paging, and a contract-version gate that makes a mixed-version fleet deterministic.

## Design decisions (pre-made — implement, do not re-derive)
- **Packages pinned EXACTLY `10.1.2`** (`STACK.md` §4, T-RULE): `CommunityToolkit.Datasync.Server`,
  `CommunityToolkit.Datasync.Server.EntityFrameworkCore`,
  `CommunityToolkit.Datasync.Server.NSwag`. Consumed **only** by `Animora.Server` and
  `Animora.Persistence.SqlServer` (`CONVENTIONS.md` §2). No client Datasync package is added in this
  phase — that is E03's, and only `Animora.Sync.Datasync` may ever reference it.
- **Wire contracts are separate types, one per replicated entity** (A-RULE 014, §8.2 invariant), in
  `src/Animora.Contracts.Wire/Sync/`, named `<Entity>SyncContract` per `CONVENTIONS.md` §3. Each
  carries exactly the four replication fields plus its business payload:
  `string Id`, `DateTimeOffset? UpdatedAt`, `string? Version` (opaque base64 via `RowVersionCodec`),
  `bool Deleted`, `int ContractVersion` — and **no** `TenantId` (the client never needs it; the
  server's data view already scoped the rows, and shipping it would invite a client to trust it).
  Mapping between the server entity and the contract lives in
  `src/Animora.Persistence.SqlServer/Sync/SyncContractMappings.cs` as **explicit `Select`
  projections** (no AutoMapper, no reflection — PERFORMANCE.md §3).
- **Replicated entity set (binding, 25 entities).** Derived from `ServerModel.ReplicatedEntities`
  (D02) minus everything a client must never hold or must obtain only through the command pipeline:
  | Replicated (client needs local reads of these) | Why |
  |---|---|
  | `Owner`, `Pet`, `WeightEntry`, `DocumentReference` | patient directory, offline reads |
  | `Visit`, `VaccinationRecord`, `MedicalHistoryEntry` | clinical history |
  | `TreatmentPlan`, `TreatmentOccurrence`, `VaccineCatalogItem` | treatment plans + vaccine catalog (post-C11, migration `031`) — **read-only**, clinical history like `VaccinationRecord` |
  | `Appointment`, `ReceptionQueueEntry` | calendar + queue |
  | `Room`, `BoardingServiceDef`, `BoardingStay` | boarding board |
  | `UnitOfMeasure`, `ProductCategory`, `Product` | catalog / POS lookups |
  | `Supplier`, `Insurer`, `LedgerAccount`, `AccountingPeriod` | reference data |
  | `Tenant`, `RoleDefinition`, `StaffMembership` | tenant + role display |
  | **Not replicated** | **Why** |
  | `Invoice`, `Payment`, `CashSession`, `JournalEntry`, `Cheque`, `Expense`, `RecurringExpense`, `RecurringExpensePayment`, `StockMovement`, `PurchaseOrder`, `GoodsReceipt` | financial/stock facts are **settled by command**, not replicated as state (R-B13); the client learns them from its own settled commands and from server-computed read models |
  | `User`, `Permission`, `Device`, Identity tables | credentials/authorization material never leaves the server as replicable rows |
  | `IdempotencyRecords`, `ServerAuditEntries`, `StockOnHandProjections`, `DailyFinanceFacts`, `ServerBootstrapState`, `TenantSettings` | server-internal (D02/D05/D07 lists) |
  **DECISION (2026-10-07, closes Phase-Problem §6.1):** the two aggregates that post-date C01–C09 are
  classified by the same rule as their siblings — `TreatmentPlan`/`TreatmentOccurrence`/`VaccineCatalogItem`
  are clinical history ⇒ **replicated, read-only** (writes only via `vaccination.treatment.*` /
  `vaccination.catalog.add` commands, D06); `RecurringExpense`/`RecurringExpensePayment` are financial
  facts ⇒ **not replicated** (settled by `finance.recurring.*` commands, D07, exactly like `Expense`).
  Net: replicated **25** (3 client-writable + **22** read-only). Client-local columns that have no wire
  counterpart — e.g. E05's `MedicalHistoryEntry.IsConflictPreservedCopy` — are **excluded from every
  `<Entity>SyncContract`** and are never pushed or pulled; the `MedicalHistoryEntry` contract carries
  no such field, so the entity stays purely read-only on the wire.
  This split **is** the "replication ≠ intent" line drawn in data. `SyncSurfaceTests` asserts the
  two sets exactly, so no later phase can quietly replicate a financial table.
- **One controller per entity**, `src/Animora.Server/Tables/<Entity>TableController.cs`:
  `[Route("tables/<plural-lower>")] sealed class <Entity>TableController : TableController<<Entity>SyncContract>`.
  Repository: `EntityTableRepository<T>` over `AnimoraServerDbContext` — but over a **projected
  keyless-free view**: because Datasync requires an EF entity type, each contract is mapped as a
  **database view** (`vw_Sync<Entity>`) created in migration `106_SyncViews`, selecting the
  projection columns plus `UpdatedAt`/`Version`/`Deleted`, with `INSTEAD OF` triggers deliberately
  **absent** — the views are read-only, and client writes travel through the command pipeline or
  through the controller's own `ITableRepository` write path for the three genuinely
  client-writable entities (`Owner`, `Pet`, `WeightEntry`). Every other controller sets
  `TableControllerOptions.DisableClientSideWrites` semantics by overriding the write methods to
  return `405` with `REPLICATION_READ_ONLY`. A test enumerates the controllers and asserts exactly
  those three accept writes.
- **`TableControllerOptions` (binding values, `STACK.md` §5.2):**
  `PageSize = 50`, `MaxTop = 200` (matches `CONVENTIONS.md` §8's page cap),
  `EnableSoftDelete = true`, `UnsafeEntityLogging = false` (never log a financial/PII row).
  Configured once in `src/Animora.Server/Tables/TableOptionsFactory.cs`, not per controller.
- **`IAccessControlProvider<T>` is isolation layer 3** (`ARCHITECTURE.md` §9.2):
  `src/Animora.Server/Tables/TenantAccessControlProvider.cs`, one generic implementation registered
  for every contract:
  - `GetDataView()` returns `e => e.TenantId == _tenantContext.TenantId` — evaluated against the
    **view's** tenant column; a null tenant context throws `TenantContextMissingException`
    (never an unfiltered view).
  - `IsAuthorizedAsync(TableOperation op, T? entity, ...)` checks the D04 permission for the
    entity's read/write operation via `IPermissionEvaluator`, and for writes additionally
    re-verifies `entity.TenantId == ambient`.
  - `PreCommitHookAsync` **stamps the server-side fields** — `TenantId` from the context (never from
    the payload), `UpdatedAt` from `ISystemClock` — which is the "server-side write stamping" half of
    layer 3.
  This provider is the only place replication authorization lives; controllers stay empty of logic.
- **Contract-version gate, deterministic across N/N-1/N-2** (R-B31, §8.6):
  `src/Animora.Server/Tables/ContractVersionMiddleware.cs` (scoped to `/tables/*`) reads
  `WireContract.HeaderName` (`X-Animora-Contract-Version`, A01):
  - header absent → treated as `WireContract.MinSupportedVersion` (an old client that predates the
    header still works, deterministically).
  - value inside `WireContract.SupportedVersions` (D05) → served.
  - value **below** the window → `409` `CONTRACT_VERSION_UNSUPPORTED` with
    `{ minSupported, current, action = "resync-after-upgrade" }` so the client can trigger a
    controlled upgrade/resync instead of mis-mapping.
  - value **above** current → `409` with `action = "server-upgrade-required"`.
  Each response also **echoes** the served version in the same header, so a client can detect a
  silent downgrade. `WireContract.Version` **is bumped to `2`** only if a contract shape here differs
  from what A01 assumed; since these are new types, it stays `1` and the phase file records that
  explicitly (ROADMAP §3.8 expected this phase *might* bump it — it does not, and that is the
  decision).
- **Delta-cursor safety is the pinned library's own algorithm** (`STACK.md` §4.3) — max `UpdatedAt`
  per entity/query. Two guards are added here because `STACK.md` §12 Gate G1 is not yet proven:
  1. `UpdatedAt` is stamped with **strictly monotonic** values per tenant/table by
     `MonotonicStampService` (`src/Animora.Server/Tables/MonotonicStampService.cs`): it takes the
     greater of `now` and `lastStamp + 1 tick` from an in-memory per-(tenant,table) high-water
     seeded from the table's max on first use. Same-millisecond bursts therefore cannot collapse
     onto one cursor value — the single largest G1 risk, removed by construction.
  2. `SyncSurfaceTests` includes a **same-tick burst** fact (100 inserts stamped inside one
     millisecond, then a paged pull) asserting zero skipped and zero duplicated rows. The full G1
     matrix is E08's gate; this is the in-phase smoke version.
  `UpdatedAt` and `Version` remain **transport bookkeeping only** (A-RULE 009/010) — a test asserts
  no business query orders by `UpdatedAt`.
- **OpenAPI:** NSwag documents `/tables/*` only (`STACK.md` §5.1) at
  `/openapi/tables.json`, gated by the same `ExposeOpenApi` flag as D01's `business` document. The
  first-party document is **not** extended to `/tables/*`, and no business client may depend on the
  OData semantics it describes (T-RULE 002/007) — `LayeringRulesTests` gains a fact that no project
  other than `Animora.Server`/`Animora.Persistence.SqlServer` references a Datasync assembly.
- **Web-client seam (R-B11, structure only, no feature):** the contracts and controllers are
  transport-agnostic and the read projections live in `SyncContractMappings`, which a future online
  Blazor clinic client can query directly through the Business API without any change here. No
  endpoint is added for it.
- **`/health/ready` gains a `replication` check**: every registered contract has a corresponding
  view and a `TenantId`+`UpdatedAt` index (the check reads the catalog once at startup and caches
  the result).

## Files to create / modify
| Path | Action | Contents (one line) |
|---|---|---|
| `src/Animora.Contracts.Wire/Sync/*.cs` | create | 25 `<Entity>SyncContract` records |
| `src/Animora.Contracts.Wire/Sync/SyncContractCatalog.cs` | create | name → contract + version table |
| `src/Animora.Persistence.SqlServer/Sync/SyncContractMappings.cs` | create | explicit projections |
| `src/Animora.Persistence.SqlServer/Sync/SyncViewConfigurations.cs` | create | 25 view mappings |
| `src/Animora.Persistence.SqlServer/Migrations/106_SyncViews*` | create | `vw_Sync<Entity>` views + indexes check |
| `src/Animora.Server/Tables/*TableController.cs` | create | 25 thin controllers |
| `src/Animora.Server/Tables/TableOptionsFactory.cs` | create | the binding `TableControllerOptions` |
| `src/Animora.Server/Tables/TenantAccessControlProvider.cs` | create | isolation layer 3 |
| `src/Animora.Server/Tables/ContractVersionMiddleware.cs` | create | N/N-1/N-2 gate + echo header |
| `src/Animora.Server/Tables/MonotonicStampService.cs` | create | strictly-increasing `UpdatedAt` |
| `src/Animora.Server/Program.cs` | modify | `AddDatasyncServices()`, NSwag tables doc, middleware, readiness check |
| `Directory.Packages.props` | modify | pin the 3 Datasync server packages at `10.1.2` |
| `tests/Animora.Server.Tests/SyncSurfaceTests.cs` | create | replicated set, read-only set, same-tick burst |
| `tests/Animora.Server.Tests/SyncAccessControlTests.cs` | create | data view, cross-tenant pull, write stamping |
| `tests/Animora.Server.Tests/ContractVersionTests.cs` | create | below/above/absent/echo behavior |
| `tests/Animora.Domain.Tests/LayeringRulesTests.cs` | modify | only server projects reference Datasync |

## Implementation steps
1. Pin the three Datasync server packages at exactly `10.1.2`.
2. Write the 25 sync contracts + `SyncContractCatalog`; write `SyncContractMappings` and
   `SyncViewConfigurations`; generate migration `106_SyncViews`.
3. Write `MonotonicStampService` and `TableOptionsFactory`.
4. Write `TenantAccessControlProvider` (data view, authorization, pre-commit stamping).
5. Write the 25 controllers; override the write methods on the 22 read-only ones.
6. Write `ContractVersionMiddleware`; scope it to `/tables/*`; echo the served version.
7. Register `AddDatasyncServices()`, the NSwag `tables` document, the middleware and the
   `replication` readiness check.
8. Extend `LayeringRulesTests`; write the three server test classes.
9. Verify against a real SQL Server: a paged pull for tenant A never returns a tenant B row; a
   forged `TenantId` in a pushed payload is overwritten by the pre-commit stamp; a write to a
   read-only table is `405`; an old contract version gets the controlled-resync instruction.
10. `build.sh full` = 0/0; `test.sh` = `TESTS OK`; desktop `run.sh` = `SMOKE RUN OK`.

## Contracts / schema added
- Wire: 25 `<Entity>SyncContract` records + `SyncContractCatalog`. `WireContract.Version` stays `1`
  (explicit decision; no existing shape changed).
- Persistence.SqlServer: `SyncContractMappings`, `SyncViewConfigurations`, 25 `vw_Sync<Entity>`
  views, migration `106_SyncViews`.
- Server: 25 `TableController`s, `TableOptionsFactory`, `TenantAccessControlProvider`,
  `ContractVersionMiddleware`, `MonotonicStampService`, the `tables` OpenAPI document, the
  `replication` readiness check.
- `ErrorCodes` += `REPLICATION_READ_ONLY` (`CONTRACT_VERSION_UNSUPPORTED` exists from D05 — reuse).
- Packages: 3 Datasync server pins at `10.1.2`.
- No `Animora.Contracts` change, no `CommandTypes` entry, no DI change in `App.axaml.cs`.

## Rules that apply here
- **A-RULE 001 / T-RULE 002 / T-RULE 007** — Datasync types exist only in the two server projects;
  OData is an implementation detail of `/tables/*` and nothing else depends on it.
- **A-RULE 014 / §8.2** — clients receive `<Entity>SyncContract`, never a server write model; the
  contracts deliberately omit `TenantId`.
- **A-RULE 009 / 010** — `UpdatedAt` is a delta cursor and `Version` is opaque concurrency metadata;
  neither is a business sequence, asserted by a test.
- **R-B19 / §9.2 layer 3** — the data view plus pre-commit server-side stamping are the replication
  isolation layer, independent of D02's query filter and D04's authorization.
- **R-B31 / §8.6** — the supported-version window is explicit, the response echoes the served
  version, and both out-of-window directions get a deterministic instruction.
- **R-B13** — financial and stock tables are deliberately **not** replicated; the split is asserted.
- **CONVENTIONS §8** — `PageSize 50` / `MaxTop 200`; `UnsafeEntityLogging = false`.

## Out of scope
- The client-side `ISyncEngine` implementation and the Datasync **client** package → E03.
- Sync orchestration, scheduling, status UI → E04.
- Conflict resolution and non-destructive pull on the client → E05.
- The full G1 delta-correctness matrix as a recorded gate → E08 (only the smoke burst test here).
- Server-computed read models exposed for pull → E02 (the reporting seam).

## Acceptance criteria
- Build: `scripts/sandbox/build.sh full` = 0 warnings / 0 errors; the Datasync pins resolve to
  exactly `10.1.2` (a CI assertion fails on any other version).
- `SyncSurfaceTests` green (or skipped with reason): the replicated set equals the 25 listed
  entities and the non-replicated set contains every financial/stock/identity/internal table; a
  write attempt on any of the 22 read-only controllers is `405` `REPLICATION_READ_ONLY`; a
  100-insert same-millisecond burst followed by a paged pull yields **zero skipped and zero
  duplicated** rows (`MonotonicStampService` proven).
- `SyncAccessControlTests` green: a tenant-A token pulling `/tables/pets` never sees a tenant-B row;
  a pushed `Owner` carrying a foreign `TenantId` is stamped to the ambient tenant, not rejected
  silently and not honored; a pull with no tenant context throws rather than returning rows; a
  caller lacking the read permission gets `403`.
- `ContractVersionTests` green: absent header behaves as `MinSupportedVersion`; a below-window value
  returns `409` with `action = "resync-after-upgrade"`; an above-window value returns `409` with
  `action = "server-upgrade-required"`; every 200 response echoes the served version.
- `LayeringRulesTests` green: no project other than `Animora.Server` /
  `Animora.Persistence.SqlServer` references a `CommunityToolkit.Datasync*` assembly.
- `/health/ready` reports the `replication` check; `/openapi/tables.json` is served only when
  `ExposeOpenApi` is on.
- Desktop unaffected: `scripts/sandbox/run.sh` = `SMOKE RUN OK`.

## PROGRESS.md update
Mark D08 done; advance "Next phase" to `D09`; append a "Repo state" entry listing: the 25
`<Entity>SyncContract` types + `SyncContractCatalog` (**E03 maps to them**), the **binding
replicated / non-replicated split** and its test, the three client-writable entities, the
`vw_Sync<Entity>` views + migration `106_SyncViews`, `TableControllerOptions` values,
`TenantAccessControlProvider` as isolation layer 3 (**E08 gate E/J re-tests it**),
`ContractVersionMiddleware` + the header echo + the two 409 actions (**E03/E09 depend on them**),
`MonotonicStampService` (**the G1 mitigation; E08 proves the full matrix**), the Datasync `10.1.2`
pins, the `tables` OpenAPI document, the explicit decision that `WireContract.Version` stays `1`,
the extended layering fact, deviations.
