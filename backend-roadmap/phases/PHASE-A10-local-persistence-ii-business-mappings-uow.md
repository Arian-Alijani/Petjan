# Phase A10 — Local persistence II: business mappings + Unit of Work

**Band:** M · **Track:** A · **Depends on:** A08, A09 · **Satisfies:** R-B02, R-B08
**Runnable after this phase:** desktop offline yes · server required no

> **Track A testing policy (see ROADMAP §3.10).** This phase writes **no tests** and runs **no test
> suite**. It ships the business mappings, repositories, unit of work, derived queries, seeding and
> migration only. All tests are authored and executed once, in **Phase A12**. The single verification
> here is a lightweight build (`scripts/sandbox/build.sh full` = 0/0) plus the non-test
> `dotnet ef migrations list` check; run only if needed.

## Read before starting
- `backend-roadmap/CONVENTIONS.md` §1, §2, §4, §5, §8, §11, §12
- `backend-roadmap/PROGRESS.md`
- `ARCHITECTURE.md` §11.2 (movement-based stock), §11.3 (read vs write models), §12.1 (outbox intent)
- `PERFORMANCE.md` §4, §6, §12, §15
- `src/Animora.Persistence.Sqlite/Context/AnimoraLocalDbContext.cs`,
  `src/Animora.Persistence.Sqlite/Conventions/ModelConventions.cs`
- `src/Animora.Domain/Inventory/*`, `src/Animora.Domain/Purchasing/*`, `src/Animora.Domain/Sales/*`,
  `src/Animora.Domain/Cashier/*`, `src/Animora.Domain/Finance/*`
- `src/Animora.Domain/Primitives/IHasDomainEvents.cs`, `src/Animora.Domain/Events/*`

## Goal
Finish the local schema (Inventory, Purchasing, Sales, Cashier, Finance), add the repository ports and
their implementations, a transactional `IUnitOfWork` that dispatches domain events, the paged read-model
projection helpers every grid will use, and the first-run seeding module. The local database is now a
complete, writable business store.

## Design decisions (pre-made — implement, do not re-derive)
- **New folders:** `Configurations/Inventory/`, `Configurations/Purchasing/`, `Configurations/Sales/`,
  `Configurations/Cashier/`, `Configurations/Finance/`, `Repositories/`, `Queries/`, `Seed/`,
  plus `src/Animora.Application/Ports/` and `src/Animora.Application/Abstractions/`.
- **Configurations added** (conventions from A09 apply automatically):
  - Inventory: `UnitOfMeasure`, `ProductCategory`, `Product`, `StockMovement`.
    `ProductVariant` is an owned collection **as rows** (not JSON) in table `ProductVariants` —
    variants are queried and priced individually. `StockLevel` is **never persisted** (derived).
    Indexes: `IX_Products_TenantId_Name`, `IX_Products_TenantId_CategoryId`,
    `IX_Products_TenantId_Sku` (unique, filtered on non-null),
    `IX_StockMovements_TenantId_ProductId_VariantId_OccurredUtc`,
    `IX_StockMovements_TenantId_Kind_OccurredUtc`.
  - Purchasing: `Supplier`, `PurchaseOrder`, `GoodsReceipt`. `PurchaseOrderLine` and
    `GoodsReceiptLine` are owned collections as rows (`PurchaseOrderLines`, `GoodsReceiptLines`).
    Indexes: `IX_PurchaseOrders_TenantId_State_OrderedUtc`, `IX_GoodsReceipts_TenantId_PurchaseOrderId`,
    `IX_Suppliers_TenantId_Name`.
  - Sales/Cashier: `Invoice`, `Payment`, `CashSession`. `InvoiceLine` owned as rows (`InvoiceLines`),
    `InvoiceAdjustment` owned as rows (`InvoiceAdjustments`), `CashMovement` owned as rows
    (`CashMovements`). Indexes: `IX_Invoices_TenantId_State_IssuedUtc`,
    `IX_Invoices_TenantId_Number` (unique), `IX_Invoices_TenantId_OwnerId`,
    `IX_Invoices_TenantId_VisitId`, `IX_Payments_TenantId_InvoiceId`,
    `IX_Payments_TenantId_ReceivedUtc_Method`, `IX_CashSessions_TenantId_State_OpenedUtc`.
  - Finance: `LedgerAccount`, `JournalEntry`, `AccountingPeriod`, `Cheque`, `Expense`.
    `JournalLine` owned as rows (`JournalLines`); `ChequeEvent` owned as rows (`ChequeEvents`).
    `JournalEntry` and `JournalLine` are configured **append-only**: A10 adds
    `AppendOnlyGuard` (below) rather than an EF-level trick.
    Indexes: `IX_JournalEntries_TenantId_PeriodId_PostedUtc`,
    `IX_JournalEntries_TenantId_SourceEntityType_SourceEntityId`,
    `IX_JournalEntries_TenantId_EntryNumber` (unique), `IX_JournalLines_AccountCode`,
    `IX_Cheques_TenantId_State_DueUtc`, `IX_Expenses_TenantId_IncurredUtc_CategoryCode`,
    `IX_AccountingPeriods_TenantId_PeriodCode` (unique),
    `IX_LedgerAccounts_TenantId_Code` (unique).
- `Context/AppendOnlyGuard.cs` — invoked from `AnimoraLocalDbContext.SaveChangesAsync` **before**
  stamping: any `Modified` or `Deleted` entry whose CLR type is in
  `AppendOnlyGuard.ProtectedTypes` (`JournalEntry`, `JournalLine`, `MedicalHistoryEntry`,
  `StockMovement`, `Payment`, `ChequeEvent`, `AuditEntry` (added B05)) throws
  `InvalidOperationException` naming the type and key. This is the structural enforcement of
  A-RULE 002.
- **Repository ports** in `src/Animora.Application/Ports/` (interfaces only; Application owns them):
  `IOwnerRepository`, `IPetRepository`, `IVisitRepository`, `IVaccinationRepository`,
  `IAppointmentRepository`, `IReceptionQueueRepository`, `IBoardingRepository`,
  `IProductRepository`, `IStockMovementRepository`, `IPurchasingRepository`,
  `IInvoiceRepository`, `IPaymentRepository`, `ICashSessionRepository`,
  `ILedgerRepository`, `IChequeRepository`, `IExpenseRepository`, `IPeriodRepository`,
  `IIdentityRepository` (users/roles/memberships/devices).
  Uniform shape per aggregate: `Task<T?> FindAsync(string id, CancellationToken ct = default)`,
  `Task<T> RequireAsync(...)` (throws on missing — defect), `void Add(T)`, `void AddRange(...)`,
  plus **only** the aggregate-specific lookups a Track C phase needs (documented per port).
  **No `IQueryable` leaves a repository** — reads for grids go through `Queries/` projections.
- `src/Animora.Application/Abstractions/IUnitOfWork.cs`:
  ```
  Task<Result> SaveAsync(CancellationToken ct = default);
  Task<Result<T>> ExecuteAsync<T>(Func<CancellationToken, Task<Result<T>>> work, CancellationToken ct = default);
  ```
  `ExecuteAsync` opens one `IDbContextTransaction`, runs `work`, and on success calls `SaveAsync`
  then commits; on a failed `Result` it rolls back and returns the failure (no exception).
- `Repositories/LocalUnitOfWork.cs` — `sealed class LocalUnitOfWork : IUnitOfWork`. `SaveAsync`:
  1. collect domain events from every tracked `IHasDomainEvents` **before** `SaveChangesAsync`;
  2. `SaveChangesAsync`;
  3. hand the collected events to `IDomainEventDispatcher` **inside the same transaction**;
  4. `ClearDomainEvents()`;
  5. map `DbUpdateConcurrencyException` → `Result.Fail(ErrorCodes.ConcurrencyConflict)` and
     `SqliteException` → `Result.Fail(ErrorCodes.StoreUnavailable)`; nothing else is caught.
- `src/Animora.Application/Abstractions/IDomainEventDispatcher.cs` +
  `IDomainEventHandler<TEvent>` (`Task<Result> HandleAsync(TEvent e, CancellationToken ct)`).
  `Repositories/DomainEventDispatcher.cs` resolves handlers from a **compile-time registered**
  `Dictionary<Type, Func<IServiceProvider, IReadOnlyList<object>>>` built by
  `AddAnimoraLocalStore` — no runtime assembly scanning (PERFORMANCE.md §3). Zero handlers exist in
  this phase; A10 only ships the dispatch mechanism plus a test double.
- `Queries/` — static projection helpers, the only read path for grids:
  - `PagedResult<T>` — `sealed record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page,
    int PageSize)` in `src/Animora.Application/Abstractions/`.
  - `QueryPaging.cs` — `static Task<PagedResult<T>> ToPagedAsync<T>(IQueryable<T> source, int page,
    int pageSize, CancellationToken ct)`: clamps `pageSize` to `LocalStoreOptions.MaxPageSize`,
    issues a `CountAsync` + a `Skip/Take` `ToListAsync`, both `AsNoTracking` by context default.
  - `StockOnHandQuery.cs` — derives on-hand per `(ProductId, VariantId)` by summing
    `StockMovement.SignedQuantity` in **one** grouped SQL query; never stores a total
    (ARCHITECTURE.md §11.2). Returns `IReadOnlyDictionary<(string, string?), decimal>`.
  - `InvoiceBalanceQuery.cs` — paid/due per invoice from `Payment` sums in one grouped query.
  - `LedgerBalanceQuery.cs` — balance per `AccountCode` for a period from `JournalLines` in one
    grouped query (the C09 read model builds on this).
- `Seed/LocalSeeder.cs` — `sealed class LocalSeeder` with
  `Task<Result> SeedAsync(CancellationToken ct)`, idempotent (each block checks
  `AnyAsync()` first), seeding **reference data only**:
  `Tenant` (`T-LOCAL-DEFAULT`), the `Permission` catalog + `RoleDefinition`s from
  `DefaultRolePermissions`, `ChartOfAccounts` → `LedgerAccount` rows, `ExpenseCategories`,
  `AppointmentTypeCatalog`, `VaccineSchedule` vaccine list, `UnitOfMeasure` base units
  (`PCS`, `BOX`, `ML`, `MG`, `G`, `KG`, `DOSE`), a small `ProductCategory` set
  (`MED`, `VAC`, `FOOD`, `ACC`, `SRV`), the `Room` set for boarding (`R-01`…`R-08`), and the
  current `AccountingPeriod` (open). **No demo owners/pets/invoices** — sample business data is a
  Track C decision per phase, never seeded globally.
  `Seed/SeedMarker.cs` — `[LocalOnly] sealed class SeedMarker : LocalOnlyEntity` with
  `string SeedKey`, `int SchemaVersion`; each seed block writes its marker so re-seeding is O(1).
- `Migrations/002_LocalBusinessSchema` — one migration for everything above (including
  `SeedMarkers`). **Never edited by a later phase.**
- `AddAnimoraLocalStore` gains: all repository registrations (scoped), `IUnitOfWork` (scoped),
  `IDomainEventDispatcher` (scoped), `LocalSeeder` (scoped), the handler map (singleton), and
  `IDbContextFactory<AnimoraLocalDbContext>` via `AddDbContextFactory` so a background/query path can
  open its own short-lived context without fighting the scoped one.

## Files to create / modify
| Path | Action | Contents (one line) |
|---|---|---|
| `src/Animora.Persistence.Sqlite/Configurations/Inventory/*.cs` | create | 4 configurations + `ProductVariants` table |
| `src/Animora.Persistence.Sqlite/Configurations/Purchasing/*.cs` | create | 3 configurations + line tables |
| `src/Animora.Persistence.Sqlite/Configurations/Sales/*.cs` | create | `Invoice`, `Payment` + line/adjustment tables |
| `src/Animora.Persistence.Sqlite/Configurations/Cashier/CashSessionConfiguration.cs` | create | session + `CashMovements` |
| `src/Animora.Persistence.Sqlite/Configurations/Finance/*.cs` | create | 5 configurations + `JournalLines`/`ChequeEvents` |
| `src/Animora.Persistence.Sqlite/Context/AppendOnlyGuard.cs` | create | protected-type write guard |
| `src/Animora.Persistence.Sqlite/Context/AnimoraLocalDbContext.cs` | modify | call `AppendOnlyGuard` in `SaveChangesAsync` |
| `src/Animora.Application/Ports/*.cs` | create | 18 repository port interfaces |
| `src/Animora.Application/Abstractions/IUnitOfWork.cs` | create | save + transactional execute |
| `src/Animora.Application/Abstractions/IDomainEventDispatcher.cs` | create | dispatcher + `IDomainEventHandler<T>` |
| `src/Animora.Application/Abstractions/PagedResult.cs` | create | paged read shape |
| `src/Animora.Persistence.Sqlite/Repositories/*.cs` | create | 18 repository implementations (thin, `DbSet`-backed) |
| `src/Animora.Persistence.Sqlite/Repositories/LocalUnitOfWork.cs` | create | transaction + event dispatch + error mapping |
| `src/Animora.Persistence.Sqlite/Repositories/DomainEventDispatcher.cs` | create | compile-time handler map dispatch |
| `src/Animora.Persistence.Sqlite/Queries/QueryPaging.cs` | create | clamped paging helper |
| `src/Animora.Persistence.Sqlite/Queries/StockOnHandQuery.cs` | create | derived on-hand, one grouped query |
| `src/Animora.Persistence.Sqlite/Queries/InvoiceBalanceQuery.cs` | create | paid/due aggregation |
| `src/Animora.Persistence.Sqlite/Queries/LedgerBalanceQuery.cs` | create | per-account period balance |
| `src/Animora.Persistence.Sqlite/Seed/LocalSeeder.cs` | create | idempotent reference-data seeding |
| `src/Animora.Persistence.Sqlite/Seed/SeedMarker.cs` | create | local-only seed marker entity |
| `src/Animora.Persistence.Sqlite/Migrations/*` | create | `002_LocalBusinessSchema` + snapshot |
| `src/Animora.Persistence.Sqlite/Context/LocalStoreServiceCollectionExtensions.cs` | modify | register repos/UoW/dispatcher/seeder/context factory |

**No test files in this phase.** All tests are authored in **A12**; the "Test coverage owed to A12"
section below records exactly what A12 must cover.

## Implementation steps
1. Write the 15 new configurations; keep every amount a `Money` complex property and every owned
   collection that is queried individually as **rows**, not JSON.
2. Add `AppendOnlyGuard` and wire it as the first step of `SaveChangesAsync`.
3. Add the 18 repository ports in `Animora.Application/Ports/`, then their thin implementations.
   A repository exposes no `IQueryable` and no EF type.
4. Add `PagedResult<T>`, `IUnitOfWork`, `IDomainEventDispatcher`, `IDomainEventHandler<TEvent>`.
5. Implement `LocalUnitOfWork` (collect → save → dispatch → clear, single transaction) and
   `DomainEventDispatcher` over the compile-time map.
6. Implement the four query helpers. Verify each produces **one** SQL statement (assert via
   `ToQueryString()` in the test, not by inspection).
7. Implement `SeedMarker`, `LocalSeeder` and every seed block; each block is guarded by its marker.
8. Generate `002_LocalBusinessSchema`; commit the migration and the snapshot.
9. Extend `AddAnimoraLocalStore` with the new registrations plus `AddDbContextFactory`.
10. **No tests here.** Record the coverage owed to A12 (below). Note that the derived-query helpers must
    each still produce exactly **one** SQL statement — A12 asserts this via `ToQueryString()`; design
    them so that property holds.
11. Lightweight verification only, if needed: `build.sh full` = 0/0; `dotnet ef migrations list` shows
    `001_LocalCoreSchema`, `002_LocalBusinessSchema` in order (design-time, not a test). Do **not** run
    `test.sh`. `run.sh` = `SMOKE RUN OK`.

## Test coverage owed to A12 (author + run in A12, not here)
- `BusinessMappingTests`: every new table and named index exists; `ProductVariants`, `InvoiceLines`,
  `JournalLines`, `ChequeEvents`, `CashMovements`, `PurchaseOrderLines`, `GoodsReceiptLines` are real
  tables (not JSON columns).
- `AppendOnlyGuardTests`: an update **and** a delete on each protected type throws and leaves the
  database unchanged.
- `UnitOfWorkTests`: a failing `Result` inside `ExecuteAsync` rolls everything back; a raised domain
  event reaches its handler exactly once inside the transaction; a simulated concurrency failure
  returns `CONCURRENCY_CONFLICT` as a `Result`, never an exception.
- `DerivedQueryTests`: on-hand equals the signed movement sum (incl. a negative adjustment);
  `InvoiceBalanceQuery` due for a partially-paid invoice; `LedgerBalanceQuery` debit − credit per
  account; each helper's `ToQueryString()` contains exactly one `SELECT`.
- `SeedingTests`: a second `SeedAsync` adds zero rows; all 20 ledger accounts, every permission, every
  role, 7 units, 5 categories, 8 rooms and one open period exist.

## Contracts / schema added
- Ports: the 18 `I*Repository` interfaces, `IUnitOfWork`, `IDomainEventDispatcher`,
  `IDomainEventHandler<TEvent>`, `PagedResult<T>`.
- Persistence: 15 configurations, `AppendOnlyGuard` (+ `ProtectedTypes`), `LocalUnitOfWork`,
  `DomainEventDispatcher`, `QueryPaging.ToPagedAsync`, `StockOnHandQuery`, `InvoiceBalanceQuery`,
  `LedgerBalanceQuery`, `LocalSeeder`, `SeedMarker`.
- Migration `002_LocalBusinessSchema`. No `Animora.Contracts` change, no DI change in `App.axaml.cs`.
- **No test files** (owed to A12).

## Rules that apply here
- **A-RULE 002 / R-B22** — `AppendOnlyGuard` makes mutation of ledger/movement/payment rows
  structurally impossible.
- **ARCHITECTURE.md §11.2 / R-B23** — on-hand is a derived query; no absolute stock column exists.
- **PERFORMANCE.md §6 / CONVENTIONS §8** — every grid read goes through `ToPagedAsync` with the
  clamped page size; no unbounded `ToListAsync` on a business table.
- **PERFORMANCE.md §3** — event dispatch uses the compile-time handler map, never runtime scanning.
- **CONVENTIONS §2** — repository ports live in Application; implementations in Persistence; no EF type
  crosses the port boundary.
- **CONVENTIONS §5** — migrations are additive; `001_LocalCoreSchema` is not touched.
- **ARCHITECTURE.md §12.1** — the outbox itself is B02's; A10 only guarantees domain events dispatch
  inside the business transaction, which is what makes B02's atomic enqueue possible.

## Out of scope
- **All tests for this phase's behavior → A12** (this phase writes and runs none).
- Encryption/keystore/version assertion/backup → A11.
- Command pipeline, `PendingCommand`, settlement states, outbox table → B02.
- Any command/query handler and the dispatcher pipeline → B01.
- Any `Animora.Contracts` implementation, DI swap or mock deletion → Track C.
- Business/demo data seeding per screen → the relevant Track C phase.

## Acceptance criteria
- Build (lightweight, only if run): `scripts/sandbox/build.sh full` = 0 warnings / 0 errors.
- **No test suite is run in this phase** (`test.sh` is A12's; do not invoke it here).
- `dotnet ef migrations list` (design-time, not a test) shows `001_LocalCoreSchema`,
  `002_LocalBusinessSchema` in that order; `001` is byte-identical to A09's output.
- The "Test coverage owed to A12" section is present and names every behavior A12 must assert,
  including the one-`SELECT` property of the derived queries.
- Runnability (`CONVENTIONS.md` §11): `scripts/sandbox/run.sh` = `SMOKE RUN OK`; the desktop still runs
  on mocks and never opens the database.

## PROGRESS.md update
Mark A10 done; advance "Next phase" to `A11`; append a "Repo state" entry listing: the 15
configurations, `AppendOnlyGuard.ProtectedTypes`, the 18 repository ports + implementations,
`IUnitOfWork`/`LocalUnitOfWork`, the dispatcher, the 4 derived queries, `LocalSeeder` contents,
migration `002_LocalBusinessSchema`, **no tests written (owed to A12)**, no DI change, no mock deleted,
deviations.
