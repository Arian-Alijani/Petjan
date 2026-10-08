# Phase D02 — Server data layer & tenancy

> **PostgreSQL baseline (2026-10-08):** Read `ARCHITECTURE.md` §PG and §16-CT–§18-CT / `STACK.md` §6 before implementation. They govern PostgreSQL 18, Npgsql 10.0.3, UUID Version, tenant journal, snapshot/batch atomicity, pool budgets and deployment-only migrations. This phase is a future instruction, not an implementation claim. Mandatory PostgreSQL CI cannot pass by skipping DB tests.

**Band:** M · **Track:** D · **Depends on:** D01 · **Satisfies:** R-B03, R-B11, R-B19
**Runnable after this phase:** desktop offline yes · server required no

## Read before starting
- `backend-roadmap/CONVENTIONS.md` §2, §3, §5, §6, §7, §8, §9, §12
- `backend-roadmap/PROGRESS.md` (D01 entry; A09/A10 local mapping decisions — the server mirrors
  their shapes, it does not copy their code)
- `ARCHITECTURE.md` §9 (all), §11 · `STACK.md` §6.1, §6.2, §6.3, §6.4
- `A-RULE 012`, `A-RULE 013`, `A-RULE 014`, `A-RULE 018`
- `src/Animora.Domain/**` (entity shapes only), `src/Animora.Persistence.Sqlite/Configurations/**`
  (as the reference for column shapes — **do not** reference the project)
- `src/Animora.Persistence.PostgreSql/**` (D01 skeleton)

## Goal
`AnimoraServerDbContext` exists with the full business model mapped for PostgreSQL 18:
`entity-version token` concurrency, **separately named** tenant and soft-delete query filters, the sync
metadata columns every replicated entity needs, a deliberate index plan, and a tenant-seeding path —
with isolation that holds **with RLS off**.

## Design decisions (pre-made — implement, do not re-derive)
- **One shared database, one shared schema, `TenantId` discriminator** (`ARCHITECTURE.md` §9.1).
  No per-tenant database, ever. `TenantId` is `varchar(26)` (ULID) and is the **leading column of
  every tenant-scoped index** — no exceptions.
- **`AnimoraServerDbContext`** in `src/Animora.Persistence.PostgreSql/Context/`:
  - Two **named** query filters registered per tenant-scoped entity (EF Core 10 named filters,
    STACK.md §6.4): `"Tenant"` → `e.TenantId == _tenantContext.TenantId`, and `"SoftDelete"` →
    `!e.Deleted`. A maintenance path may call `IgnoreQueryFilters("SoftDelete")` **only**;
    `IgnoreQueryFilters()` with no argument is banned and detected by
    `ServerQueryFilterTests` (a source scan plus a runtime interceptor that throws when a query
    executes with the `Tenant` filter disabled outside an explicitly allow-listed maintenance scope).
  - `ITenantContext` (`Animora.Application/Ports/ITenantContext.cs`, **new port**) supplies the
    ambient `TenantId`; a query executed with `TenantId == null` **throws**
    `TenantContextMissingException` — never silently returns cross-tenant rows (A-RULE 013).
  - `SaveChangesAsync` stamps, server-side and unconditionally: `TenantId` (from the context, never
    from the incoming entity — a mismatch raises `TENANT_MISMATCH` and writes a security audit),
    `UpdatedAt` is DB-trigger-stamped UTC (application clocks remain for policies/audit), `Deleted` on delete (soft), and lets PostgreSQL maintain
    `Version` (DB-trigger-generated `uuid`).
- **Concurrency:** `Version` maps existing Domain `string?` through a canonical UUID-string↔Guid converter
  to PostgreSQL `uuid` (persistence-only models can use Guid), generated
  by a BEFORE INSERT/UPDATE trigger and configured `ValueGeneratedOnAddOrUpdate()` +
  `IsConcurrencyToken()` (not `IsRowVersion()` / xmin). RETURNING retrieves the generated value.
  Wire Version stays `string`, encoded as canonical lowercase hyphenated UUID via
  `EntityVersionCodec`. Client payloads cannot set it. Local domain Version is unchanged.
  D02 creates `sync.TenantFeedState` per tenant BEFORE provisioning replicated rows and provides
  gate-first `ServerUnitOfWork` seam; D08 installs journal capture/manifest. Version triggers cover
  ALL mutable server models including internal rows. TenantId/Id changes fail in DB and Application.

- **Sync metadata on every replicated entity:** `Id` `varchar(26) COLLATE "C"`, tenant-qualified `(TenantId,Id)` keys/FKs, `UpdatedAt`
  `timestamptz(6)` (DB-stamped display/audit), `Version uuid`, `Deleted boolean`.
  Local-only client tables (`PendingCommand`, `ActivityEvent`, facts, snapshots, preferences…) have
  **no** server counterpart — listed explicitly in `ServerModel.NonReplicatedLocalTables` as
  documentation and asserted by a test so no one accidentally adds them.
- **Entity coverage:** every `Animora.Domain` aggregate that C01–C09 persist locally, i.e. Identity
  (`Tenant`, `User`, `RoleDefinition`, `Permission`, `StaffMembership`, `Device`), Patients
  (`Owner`, `Pet`, `WeightEntry`, `DocumentReference`), Clinical (`Visit`, `VaccinationRecord`,
  `MedicalHistoryEntry`), Scheduling (`Appointment`, `ReceptionQueueEntry`), Boarding (`Room`,
  `BoardingServiceDef`, `BoardingStay`), Inventory (`UnitOfMeasure`, `ProductCategory`, `Product`,
  `StockMovement`), Purchasing (`Supplier`, `PurchaseOrder`, `GoodsReceipt`), Sales (`Invoice`,
  `Payment`, `CashSession`, `Insurer`), Finance (`LedgerAccount`, `JournalEntry`,
  `AccountingPeriod`, `Cheque`, `Expense`), **plus the post-C11 aggregates (migrations `029`/`031`):
  Clinical `TreatmentPlan`, `TreatmentOccurrence`, `VaccineCatalogItem`; Finance `RecurringExpense`,
  `RecurringExpensePayment`** (D08 classifies them: the three clinical ones replicated read-only, the
  two finance ones not replicated). **Coverage is derived from the live `Animora.Domain` aggregate
  set at execution time, not from this list.** Owned collections map to the **same table names** as the
  local store so the two schemas stay mentally aligned.
- **Type mapping table (binding):**
  | Concept | PostgreSQL type |
  |---|---|
  | Id / TenantId / any ULID FK | `varchar(26)`, `ASCII`-only values |
  | `Money` | `<Prop>Minor` `bigint` + `<Prop>Currency` `varchar(3)` |
  | Timestamps | `timestamptz(6)`, always UTC |
  | Enums | `int` with explicit converter |
  | Free text (notes) | `text` only where genuinely unbounded; otherwise `FieldLengths` |
  | Version | DB-generated `uuid` |
  | JSON payloads | `jsonb` for structural data; canonical fingerprint/receipt bytes stay `text`/`bytea` |
  Decimal money is **forbidden** (R-B24), matching the local store.
- **Index plan:** tenant-qualified PK/unique/FK constraints and tenant-leading business indexes
  chosen from measured predicates/order. Index referencing FKs explicitly. D08 adds
  `sync.ChangeJournal(TenantId,Sequence)` as the critical delta-range index; `UpdatedAt` is NOT a
  critical cursor and requires no blanket delta index. Projection reads use base-table indexes.
  No PAGE compression/edition checks/SQL Server DDL. Bounded text/TOAST is normal PostgreSQL behavior;
  optional storage/index optimizations require evidence. Identifier names stay <=63 UTF8 bytes.

- **Migration `102_ServerCoreSchema`** creates the initial business model, UUID version triggers and tenant feed-state in one migration (the server has no
  installed base yet, so a single baseline is correct and simpler to review than five). Later
  phases add `103+` incrementally and never edit this one.
- **Tenant seeding:** `TenantProvisioner`
  (`Animora.Persistence.PostgreSql/Provisioning/TenantProvisioner.cs`):
  `Task<Result<Tenant>> ProvisionAsync(TenantProvisionRequest request, CancellationToken ct)` —
  creates the `Tenant`, the owner `User` + `StaffMembership` (role `Manager`), the default
  `RoleDefinition` set from `DefaultRolePermissions` (A03), the chart of accounts from
  `ChartOfAccounts` (A08), units/categories/banks/insurers reference data, and an opening
  `AccountingPeriod`. Idempotent on `(TenantId)` — re-running returns the existing tenant.
  D10 calls it when a subscription is created; D03 calls it for the first tenant.
- **Read/write split seam (R-B11, no feature yet):** `AnimoraServerDbContext` exposes
  `IQueryable<T> ReadOnlySet<T>()` returning `Set<T>().AsNoTracking()` with
  `QueryTrackingBehavior.NoTracking` and a **separate connection string name**
  (`DatabaseOptions.ReadConnectionStringName`, defaulting to the same value). This is the seam a
  future read replica or reporting database plugs into (ROADMAP §5) — no behavior change today.
- **RLS stays OFF** (`STACK.md` §6.3): the migration contains **no** security policy. The decision
  is revisited only in E08's gate. A comment in the context states this explicitly so no session
  "adds it while we're here".

## Files to create / modify
| Path | Action | Contents (one line) |
|---|---|---|
| `src/Animora.Application/Ports/ITenantContext.cs` | create | ambient tenant port (server + tests) |
| `src/Animora.Persistence.PostgreSql/Context/AnimoraServerDbContext.cs` | create | model, named filters, stamping |
| `src/Animora.Persistence.PostgreSql/Context/EntityVersionCodec.cs` | create | `uuid` ↔ canonical opaque UUID string |
| `src/Animora.Persistence.PostgreSql/Context/ServerModelConventions.cs` | create | naming, lengths, enum/money/time converters |
| `src/Animora.Persistence.PostgreSql/Context/ServerModel.cs` | create | replicated-entity list + non-replicated list |
| `src/Animora.Persistence.PostgreSql/Configurations/**` | create | ~39 `<Entity>Configuration` classes (re-derive from the live domain at execution time — the count is indicative) |
| `src/Animora.Persistence.PostgreSql/Migrations/102_ServerCoreSchema*` | create | baseline migration + UUID triggers/feed-state |
| `src/Animora.Persistence.PostgreSql/Provisioning/TenantProvisioner.cs` | create | idempotent tenant bootstrap |
| `src/Animora.Persistence.PostgreSql/ServiceCollectionExtensions.cs` | create | `AddAnimoraServerStore(...)` |
| `src/Animora.Server/Program.cs` | modify | register the store; `/health/ready` gains a migration check |
| `src/Animora.Server/Configuration/DatabaseOptions.cs` | modify | add `ReadConnectionStringName` |
| `tests/Animora.Server.Tests/ServerQueryFilterTests.cs` | create | named filters, banned global ignore, missing tenant throws |
| `tests/Animora.Server.Tests/ServerSchemaTests.cs` | create | migration applies, index + type plan, no decimal money |
| `tests/Animora.Server.Tests/TenantIsolationTests.cs` | create | cross-tenant read/write denied with RLS off |
| `tests/Animora.Server.Tests/TenantProvisionerTests.cs` | create | idempotency, seeded reference data |

## Implementation steps
1. Add `ITenantContext`; implement a test double now (the real middleware arrives in D04).
2. Write `ServerModelConventions` (naming, `FieldLengths`, money/time/enum converters) and
   `EntityVersionCodec`.
3. Write the ~39 configurations mirroring the local column shapes, with `TenantId`-leading indexes
   and the query-driven business indexes and tenant-qualified keys.
4. Write `AnimoraServerDbContext`: named `Tenant` + `SoftDelete` filters, server-side stamping,
   tenant-mismatch rejection, `ReadOnlySet<T>()`.
5. Generate migration `102_ServerCoreSchema`; add the reviewed UUID version triggers + tenant feed-state provisioning.
6. Write `TenantProvisioner` (idempotent, seeds roles/accounts/reference data).
7. Register the store in `Program.cs`; add the "migrations applied" readiness check.
8. Write the four test classes, all gated by `PostgreSqlAvailability` (skip with reason when absent).
9. Verify against a real PostgreSQL 18 instance: migration applies from empty; a
   cross-tenant query returns zero rows and a cross-tenant write throws; provisioning twice yields
   one tenant.
10. `build.sh full` = 0/0; `test.sh` = `TESTS OK`; desktop `run.sh` = `SMOKE RUN OK`.

## Contracts / schema added
- Application: `ITenantContext`.
- Persistence.PostgreSql: `AnimoraServerDbContext`, `EntityVersionCodec`, `ServerModelConventions`,
  `ServerModel`, ~39 configurations, migration `102_ServerCoreSchema`, `TenantProvisioner`,
  `AddAnimoraServerStore`.
- Server: store registration, readiness migration check, `DatabaseOptions.ReadConnectionStringName`.
- No `Animora.Contracts` change, no `CommandTypes`, no `ErrorCodes` beyond `TENANT_MISMATCH`
  (add it here if A03 did not already define it).

## Rules that apply here
- **A-RULE 012 / 013 / R-B19** — `TenantId` comes only from `ITenantContext`; a null context throws;
  every tenant-scoped query is filtered unconditionally at the data-access layer.
- **A-RULE 014** — these are server write models; nothing here is ever returned to a client
  (D05/D08 project to `Animora.Contracts.Wire`).
- **STACK.md §6.3 / R-B19** — RLS is **off**; isolation must hold without it, proven by
  `TenantIsolationTests`.
- **STACK.md §6.4 / CONVENTIONS §5** — two separately named filters; required navigations into a
  filtered entity are optional or filtered on both sides.
- **R-B24** — money is `bigint` minor units + `varchar(3)`; time is `timestamptz(6)` UTC.
- **A-RULE 010** — `entity-version token` is concurrency metadata only.

## Out of scope
- Identity tables/stores → D03 (they extend this context).
- Tenant resolution middleware and the real `ITenantContext` implementation → D04.
- `TableController` mapping and access control → D08.
- Outbox and idempotency tables → D05/D09 (migrations `103`/`105`).
- Subscription/licensing tables → D10/D11.

## Acceptance criteria
- Build: `scripts/sandbox/build.sh full` = 0 warnings / 0 errors.
- `ServerSchemaTests` green on real PostgreSQL (local missing-instance skip is not CI acceptance):
  baseline applies from empty; tenant-qualified PK/FK/unique constraints reject foreign parents;
  UTF8/canonical ULIDs, bigint money, UUID generated Version and UTC microsecond round trips pass;
  all mutable rows have version triggers; provisioning creates feed state first; referencing FK
  indexes and measured business access plans exist. No money/float storage or SQL Server SQL.
  Critical feed journal/index/trigger coverage is D08's release-blocking invariant.

- **Configuration-count assertion (audit 2026-10-08 · PH-06):** the "~39 configurations" figure is indicative only. At execution, re-derive the EF configuration count from the live domain and assert `configuration count == replicated entity count + local-only entity count` (both read from `ServerModel`, no magic number); a new aggregate without a configuration, or a configuration without an aggregate, fails the test.
- `ServerQueryFilterTests` green: both filters exist by name; `IgnoreQueryFilters("SoftDelete")`
  works while a parameterless `IgnoreQueryFilters()` outside the maintenance scope throws; a query
  with a null tenant context throws `TenantContextMissingException`.
- `TenantIsolationTests` green **with RLS off**: tenant A cannot read or update a tenant B row by id;
  saving an entity whose `TenantId` differs from the ambient one raises `TENANT_MISMATCH` and writes
  a security audit record.
- `TenantProvisionerTests` green: provisioning creates roles, chart of accounts and reference data;
  re-provisioning the same tenant id changes nothing and returns the existing tenant.
- Desktop unaffected: `scripts/sandbox/run.sh` = `SMOKE RUN OK`.

## PROGRESS.md update
Mark D02 done; advance "Next phase" to `D03`; append a "Repo state" entry listing:
`AnimoraServerDbContext` + the two named filters + the stamping rules, `ITenantContext` (**D04
implements it**), `EntityVersionCodec` (**D08/E03 use it**), `ServerModel` replicated/non-replicated
lists, the type-mapping table, the tenant-qualified constraints + measured business index plan (**D08 depends on
it**), migration `102_ServerCoreSchema`, `TenantProvisioner` (**D03/D10 call it**), the
`ReadOnlySet<T>` reporting seam, the explicit "RLS stays off" decision, the 4 test classes,
deviations.

## Bootstrap ordering with replicated Tenant root
Provision tenant feed-state before captured Tenant/root DML in the same transaction. If feed-state
references Tenants, use a reviewed DEFERRABLE INITIALLY DEFERRED FK for this bootstrap seam: create
state/gate, then Tenant root, children/reference data, and validate at COMMIT. Missing root/state
rolls back everything. No temporary disabling of capture/RLS or untracked bootstrap row is allowed.
