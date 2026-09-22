# Phase D02 — Server data layer & tenancy

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
- `src/Animora.Persistence.SqlServer/**` (D01 skeleton)

## Goal
`AnimoraServerDbContext` exists with the full business model mapped for SQL Server 2025:
`rowversion` concurrency, **separately named** tenant and soft-delete query filters, the sync
metadata columns every replicated entity needs, a deliberate index plan, and a tenant-seeding path —
with isolation that holds **with RLS off**.

## Design decisions (pre-made — implement, do not re-derive)
- **One shared database, one shared schema, `TenantId` discriminator** (`ARCHITECTURE.md` §9.1).
  No per-tenant database, ever. `TenantId` is `nvarchar(26)` (ULID) and is the **leading column of
  every tenant-scoped index** — no exceptions.
- **`AnimoraServerDbContext`** in `src/Animora.Persistence.SqlServer/Context/`:
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
    `UpdatedAt` = `ISystemClock.UtcNow`, `Deleted` on delete (soft), and lets SQL Server maintain
    `Version` (`rowversion`).
- **Concurrency:** `Version` is `byte[]` mapped as `rowversion` with `IsRowVersion()` on the server
  side, while the sync wire contract exposes it as a **base64 opaque string** (`Animora.Contracts.Wire`
  already models `Version` as `string`). Conversion lives in `RowVersionCodec`
  (`Animora.Persistence.SqlServer/Context/RowVersionCodec.cs`). A-RULE 010: it is concurrency
  metadata only and is never used as a business sequence.
- **Sync metadata on every replicated entity:** `Id` `nvarchar(26)` PK, `TenantId`, `UpdatedAt`
  `datetimeoffset(7)` (server-stamped, **indexed**), `Version` `rowversion`, `Deleted` `bit`.
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
  `AccountingPeriod`, `Cheque`, `Expense`). Owned collections map to the **same table names** as the
  local store so the two schemas stay mentally aligned.
- **Type mapping table (binding):**
  | Concept | SQL Server type |
  |---|---|
  | Id / TenantId / any ULID FK | `nvarchar(26)`, `ASCII`-only values |
  | `Money` | `<Prop>Minor` `bigint` + `<Prop>Currency` `char(3)` |
  | Timestamps | `datetimeoffset(7)`, always UTC |
  | Enums | `int` with explicit converter |
  | Free text (notes) | `nvarchar(max)` only where genuinely unbounded; otherwise `FieldLengths` |
  | Version | `rowversion` |
  | JSON payloads | `nvarchar(max)` |
  Decimal money is **forbidden** (R-B24), matching the local store.
- **Index plan:** every local index from A09/A10/C0x is reproduced with `TenantId` prepended, plus
  three server-only ones required by replication and reporting:
  `IX_<Table>_TenantId_UpdatedAt` on **every** replicated table (the delta-pull cursor — without it
  D08 table-scans),
  `IX_Invoices_TenantId_UpdatedAt_Deleted`,
  `IX_StockMovements_TenantId_UpdatedAt`.
  Page compression (`DATA_COMPRESSION = PAGE`) is applied to the three highest-row tables
  (`StockMovements`, `JournalLines`, `MedicalHistoryEntries`) via a raw-SQL step in the migration,
  guarded so it is skipped on editions that do not support it (Express supports it since 2016; the
  guard keeps the migration portable).
- **Migration `102_ServerCoreSchema`** creates the whole model in one migration (the server has no
  installed base yet, so a single baseline is correct and simpler to review than five). Later
  phases add `103+` incrementally and never edit this one.
- **Tenant seeding:** `TenantProvisioner`
  (`Animora.Persistence.SqlServer/Provisioning/TenantProvisioner.cs`):
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
| `src/Animora.Persistence.SqlServer/Context/AnimoraServerDbContext.cs` | create | model, named filters, stamping |
| `src/Animora.Persistence.SqlServer/Context/RowVersionCodec.cs` | create | `rowversion` ↔ opaque base64 |
| `src/Animora.Persistence.SqlServer/Context/ServerModelConventions.cs` | create | naming, lengths, enum/money/time converters |
| `src/Animora.Persistence.SqlServer/Context/ServerModel.cs` | create | replicated-entity list + non-replicated list |
| `src/Animora.Persistence.SqlServer/Configurations/**` | create | ~34 `<Entity>Configuration` classes |
| `src/Animora.Persistence.SqlServer/Migrations/102_ServerCoreSchema*` | create | baseline migration + compression step |
| `src/Animora.Persistence.SqlServer/Provisioning/TenantProvisioner.cs` | create | idempotent tenant bootstrap |
| `src/Animora.Persistence.SqlServer/ServiceCollectionExtensions.cs` | create | `AddAnimoraServerStore(...)` |
| `src/Animora.Server/Program.cs` | modify | register the store; `/health/ready` gains a migration check |
| `src/Animora.Server/Configuration/DatabaseOptions.cs` | modify | add `ReadConnectionStringName` |
| `tests/Animora.Server.Tests/ServerQueryFilterTests.cs` | create | named filters, banned global ignore, missing tenant throws |
| `tests/Animora.Server.Tests/ServerSchemaTests.cs` | create | migration applies, index + type plan, no decimal money |
| `tests/Animora.Server.Tests/TenantIsolationTests.cs` | create | cross-tenant read/write denied with RLS off |
| `tests/Animora.Server.Tests/TenantProvisionerTests.cs` | create | idempotency, seeded reference data |

## Implementation steps
1. Add `ITenantContext`; implement a test double now (the real middleware arrives in D04).
2. Write `ServerModelConventions` (naming, `FieldLengths`, money/time/enum converters) and
   `RowVersionCodec`.
3. Write the ~34 configurations mirroring the local column shapes, with `TenantId`-leading indexes
   and the `UpdatedAt` delta index on every replicated table.
4. Write `AnimoraServerDbContext`: named `Tenant` + `SoftDelete` filters, server-side stamping,
   tenant-mismatch rejection, `ReadOnlySet<T>()`.
5. Generate migration `102_ServerCoreSchema`; add the guarded page-compression step.
6. Write `TenantProvisioner` (idempotent, seeds roles/accounts/reference data).
7. Register the store in `Program.cs`; add the "migrations applied" readiness check.
8. Write the four test classes, all gated by `SqlServerAvailability` (skip with reason when absent).
9. Verify against a real SQL Server (Developer or Express): migration applies from empty; a
   cross-tenant query returns zero rows and a cross-tenant write throws; provisioning twice yields
   one tenant.
10. `build.sh full` = 0/0; `test.sh` = `TESTS OK`; desktop `run.sh` = `SMOKE RUN OK`.

## Contracts / schema added
- Application: `ITenantContext`.
- Persistence.SqlServer: `AnimoraServerDbContext`, `RowVersionCodec`, `ServerModelConventions`,
  `ServerModel`, ~34 configurations, migration `102_ServerCoreSchema`, `TenantProvisioner`,
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
- **R-B24** — money is `bigint` minor units + `char(3)`; time is `datetimeoffset(7)` UTC.
- **A-RULE 010** — `rowversion` is concurrency metadata only.

## Out of scope
- Identity tables/stores → D03 (they extend this context).
- Tenant resolution middleware and the real `ITenantContext` implementation → D04.
- `TableController` mapping and access control → D08.
- Outbox and idempotency tables → D05/D09 (migrations `103`/`105`).
- Subscription/licensing tables → D10/D11.

## Acceptance criteria
- Build: `scripts/sandbox/build.sh full` = 0 warnings / 0 errors.
- `ServerSchemaTests` green (or skipped with reason if no SQL Server): `102_ServerCoreSchema` applies
  to an empty database; every replicated table has `IX_<Table>_TenantId_UpdatedAt`; no column is
  `decimal`/`money`; every tenant-scoped index starts with `TenantId`.
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
implements it**), `RowVersionCodec` (**D08/E03 use it**), `ServerModel` replicated/non-replicated
lists, the type-mapping table, the `TenantId`-leading + `UpdatedAt` index plan (**D08 depends on
it**), migration `102_ServerCoreSchema`, `TenantProvisioner` (**D03/D10 call it**), the
`ReadOnlySet<T>` reporting seam, the explicit "RLS stays off" decision, the 4 test classes,
deviations.
