# Phase S10 — Server handlers II-b — inventory, purchasing, finance (incl. RecurringExpense, cheques, ledger)

**Band:** M · **Track:** S (server) · **Depends on:** S09 · **Next:** S11
**Satisfies:** R-B01, R-B17, R-B22, R-B23 · **Runnable after this phase:** desktop offline yes · server required no
**Produced by:** M00 (regenerated 2026-10-08; supersedes the deleted `PHASE-D07 (half 2)` file).

> Baseline verified 2026-10-08: client migration head `031` (`031_TreatmentPlans`), `CommandTypes` = live `CommandTypes.cs` (88 declared today — never freeze), `ErrorCodes` 161, `WireContract.Version` = 1; `src/` projects = `Animora.Domain`, `Animora.Application`, `Animora.Persistence.Sqlite`, `Animora.Infrastructure.Local`, `Animora.Client.Services`, `Animora.Contracts`, `Animora.Contracts.Wire`, `Animora.Desktop`, `Animora.Sync.Abstractions` (+ server-track projects added by earlier S/G phases).
> **Next-free-ordinal rule.** Never hard-code a migration number from this file. List the physical files of the owning `DbContext`'s `Migrations/` folder and take the next free ordinal (server sequence starts at **101**; the client SQLite sequence — head `031` today — is independent and untouched by server phases). Match on the `NNN` token, not a file-name prefix.

---

## ⚡ Zero-exploration contract (read this, then implement top-to-bottom)

> This file is self-contained and pre-verified. Every existing type, signature, path and command you need is in
> **§Reference pack**; every new name is fixed in **§Design decisions**. Do **not** re-open the repo to "understand" it and
> do not re-derive decisions. If — and only if — a compile error proves a snippet diverges from the tree, fix the smallest
> thing and log it in the `PROGRESS.md` deviation log. Names here are the real ones; there is no alias to resolve.
> Implement exactly this scope: nothing from a later phase, nothing "while we're here".

**Environment (do not probe):**
- Build / test / run only through the sandbox scripts (memory-capped ~985 MiB; never raw `dotnet build`):
  `scripts/sandbox/build.sh [fast|full]` (expect 0 warnings / 0 errors), `scripts/sandbox/test.sh [filter]`,
  `scripts/sandbox/run.sh` (desktop smoke → `SMOKE RUN OK`). Run `scripts/sandbox/install-prereqs.sh` once (idempotent).
- `dotnet-ef` lives at `.sandbox/tools/dotnet-ef` — use it; **never** `dotnet tool install`.
- Server stack is locked (`STACK.md` §0/§5/§6/§11): ASP.NET Core 10 + Kestrel, EF Core **10.0.12** line,
  PostgreSQL 18 via `Npgsql.EntityFrameworkCore.PostgreSQL` **10.0.3** / `Npgsql` **10.0.3** (only in `Animora.Persistence.PostgreSql`).
- PostgreSQL integration tests read the connection string from env var `ANIMORA_PG_TEST_CONNECTION`; when unset they
  **skip with a reason** locally (CONVENTIONS §1) and **fail** under `ANIMORA_REQUIRE_PG=1` (mandatory CI/release gate, owned by G07).
- **Forbidden tech (restated, binding):** Redis, message broker, cloud IdP, cloud telemetry, CDN, EF Core 11,
  per-tenant database, hard-coded TLS version (`SslProtocols.None` only), Native AOT with EF Core, `UpdatedAt`/`nextval`/`xmin` sync cursors.

**Offline invariant.** The desktop must keep starting and operating fully offline with no server, no PostgreSQL and no network (Standing principle 1, CONVENTIONS §11). This phase adds nothing the desktop needs at startup.

---

## Read before starting
- `backend-roadmap/PROGRESS.md` — top section only.
- `ARCHITECTURE.md` §5, §8.5, §11 (only the invariants named in Design decisions) — only these `§`.
- `src/Animora.Contracts.Wire/Commands/CommandTypes.cs` (**the** inventory source — read the families listed here).
- `src/Animora.Application/UseCases/Inventory|Purchasing|Finance/*.cs` — commands + handlers (work list).
- `src/Animora.Application/Finance/LedgerPostingHandler.cs` + `FinanceProjectionHandler.cs` (event handlers reused).
- `src/Animora.Domain/Finance/PostingRules.cs`, `Inventory/StockCalculator.cs` (reused unchanged).

(Nothing else. `Animora-UI/**`, `Database-Sampel/**`, other phase files and `windows-frontend-roadmap/phases/**` are out of bounds.)

## Goal

Make the server settle every inventory, purchasing and finance command type by reusing the Application handlers over PostgreSQL: map the `Product` (+ variants), `ProductCategory`, `UnitOfMeasure`, `StockMovement` (append-only), `PurchaseOrder` (+ lines), `GoodsReceipt`, `Supplier`, `LedgerAccount`, `JournalEntry` (+ lines, append-only), `AccountingPeriod`, `Cheque` (+ `ChequeEvent`), `Expense`, **post-C11** `RecurringExpense` (+ `RecurringExpensePayment`; **command-settled, not replicated** — ARCH §8.2), implement the Application ports those handlers use (`IProductRepository`, `IProductCategoryRepository`, `IStockMovementRepository`, `IInventoryQueries`, `IPurchasingRepository`, `IPurchasingQueries`, `ISupplierRepository`, `ILedgerRepository`, `IPeriodRepository`, `IJournalNumberAllocator`, `IChequeRepository`, `IExpenseRepository`, `IExpenseNumberAllocator`, `IRecurringExpenseRepository`, + the `IStockMovementWriter` seam declared by S09), register the handler family + payload-catalog entries + permission-table rows, and prove each command through the S06 intake. **Session split (ROADMAP §6 convention, no phase added).** This phase may be executed as two sessions to hold weight: **S10-a** = persistence mappings + migration (+ their pure/PG tests); **S10-b** = repository/query port implementations + handler registration + permission-table rows + handler tests + gate. Tick progress inside the `PROGRESS.md` row notes (`S10-a done`).

---

## §Reference pack

```csharp
namespace Animora.Application.Abstractions;   // (B01) — the handler seam a server handler implements
public interface ICommand<TResult>;
public interface ICommandHandler<in TCommand, TResult> where TCommand : ICommand<TResult>
{ Task<Result<TResult>> HandleAsync(TCommand command, CommandContext context, CancellationToken ct); }
public sealed record CommandContext(string TenantId, string UserId, string DeviceId, string OperationId, string CorrelationId,
    string? CausationId, string? DependsOnOperationId, long? ExpectedAggregateVersion, DateTimeOffset CreatedAtUtc);
public interface IUnitOfWork { Task<Result> SaveAsync(CancellationToken ct = default);
    Task<Result<T>> ExecuteAsync<T>(Func<CancellationToken, Task<Result<T>>> work, CancellationToken ct = default); }
public interface IDispatcher { Task<Result<TResult>> SendAsync<TResult>(ICommand<TResult> command, CommandContext ctx, CancellationToken ct = default);
                               Task<Result<TResult>> QueryAsync<TResult>(IQuery<TResult> query, QueryContext ctx, CancellationToken ct = default); }
namespace Animora.Application.Dispatch;
public sealed class ApplicationRegistrationBuilder {   // used inside AddAnimoraApplication(configure)
    ApplicationRegistrationBuilder AddCommand<TCommand,TResult,THandler>(); AddQuery<TQuery,TResult,THandler>(); AddValidator<T,TValidator>();
    RequireCommand<TCommand,TResult>(); RequireQuery<TQuery,TResult>(); }
public static class Dispatcher.Chain => [Correlation, ExceptionMapping, Validation, Authorization, Entitlement, Transaction, CommandJournal, Audit];
//   TransactionBehavior resolves IUnitOfWork from DI and wraps commands (skips ISelfTransactedCommand and queries).
//   CommandJournalBehavior runs only for IJournaledCommand and resolves ICommandJournal + ILocalSequenceAllocator (CLIENT-side, SQLite).
```

```csharp
// Animora.Domain.Results (A02) — reuse, never redefine
public readonly record struct ErrorCode(string Code, string Category)   // categories: Validation|NotFound|Conflict|Forbidden|Integrity|Infrastructure|Entitlement
{ public const string Validation="Validation", NotFound="NotFound", Conflict="Conflict", Forbidden="Forbidden", Integrity="Integrity", Infrastructure="Infrastructure", Entitlement="Entitlement";
  public static readonly ErrorCode None; public bool IsNone { get; } }
public readonly record struct Result      { bool IsSuccess, IsFailure; ErrorCode Error; string? Detail; static Result Ok(); static Result Fail(ErrorCode e, string? detail = null); }
public readonly record struct Result<T>   { bool IsSuccess, IsFailure; T? Value; ErrorCode Error; string? Detail; static Result<T> Ok(T v); static Result<T> Fail(ErrorCode e, string? detail = null); Map/Bind/OrElse; implicit operator Result<T>(T) }
// ErrorCodes (161 today): `public static readonly ErrorCode X = new("X_CODE", ErrorCode.<Kind>);` — inside `#region <context>`. Server phases ADD a new #region; never rename/remove.
// Already declared & reusable: ValidationFailed, NotFound, ConcurrencyConflict("CONCURRENCY_CONFLICT",Conflict), PermissionDenied, PermissionUnknown, TenantMismatch("TENANT_MISMATCH",Forbidden),
//   IdempotencyFingerprintMismatch(Integrity), DependencyUnsettled("DEPENDENCY_UNSETTLED",Conflict), EntitlementRequired(Entitlement), TenantSuspended, UserDisabled, UserLockedOut, DeviceRevoked,
//   CredentialsInvalid, SessionExpired, PasswordPolicyViolation, SettlementInvalidTransition, CommandAlreadySettled, HandlerNotRegistered, DispatchFailed, Unexpected
```

```csharp
// Server handler reuse model (decision D-S06-1): the server runs the SAME Application command handlers as the client.
// A handler = `ICommandHandler<TCommand,TResult>` registered through the existing per-context registration module
// (e.g. `PatientsRegistration`, `SalesRegistration`, `FinanceRegistration` — each exposes `AddXxx(this ApplicationRegistrationBuilder)` used inside
// `AddAnimoraApplication(configure)`); Application commands are `sealed record … : ICommand<TResult>, IJournaledCommand` with
//   public string CommandTypeName => CommandTypes.<Const>;  public string AggregateTypeName => "<Aggregate>";  public string? AggregateIdOrNull => …;
// and `[RequiresPermission(PermissionCodes.X)]` + `[Auditable(AuditCategory.<C>, "<action>")]`.
// What the server adds per handler family: (1) EF mappings of the Domain aggregates in Animora.Persistence.PostgreSql,
// (2) PostgreSQL implementations of the *same* Application ports (I<Aggregate>Repository / I<X>Queries) used by that family's handlers,
// (3) a `CommandPayloadCatalog` entry (CommandType → payload record type) so S06's intake can deserialize PayloadJson → the command record,
// (4) rows in `CommandPermissionTable` (S05) mapping CommandType → PermissionCodes.*.
```
Copy-me pattern for a PG repository: `public sealed class OwnerRepository(AnimoraServerDbContext db) : TenantRepository<Owner>(db), IOwnerRepository { … }` — implement the port members exactly as declared in `src/Animora.Application/Ports/I<Aggregate>Repository.cs` (read **that one file** per repository; the port is the contract). Copy-me pattern for the EF mapping: mirror `src/Animora.Persistence.Sqlite/Configurations/<Aggregate>Configuration.cs` replacing SQLite converters with PostgreSQL types (`timestamptz(6)`, `bigint`, `boolean`, `uuid Version` + `VersionTrigger.Apply`), `TenantId` first in every index (`ServerModelConventions.TenantIndex`).


## §Design decisions

- **Handler inventory is derived from the live `CommandTypes.cs` at execution time — never from a frozen count.** This phase owns exactly the command types whose prefix is in **`inventory.*`, `purchasing.*`, `finance.*`** (today: inventory 10, purchasing 5, finance 12 (incl. the 4 post-C11 `finance.recurring.*`)). At execution start run `grep -o '"(inventory|purchasing|finance)\.[^"]*"' src/Animora.Contracts.Wire/Commands/CommandTypes.cs` and treat that list as the work list; any newly added type with these prefixes is in scope, any removed one is dropped. Record the executed count in `PROGRESS.md`.
- **Aggregates mapped (Domain classes, no server fork):** `Product` (+ variants), `ProductCategory`, `UnitOfMeasure`, `StockMovement` (append-only), `PurchaseOrder` (+ lines), `GoodsReceipt`, `Supplier`, `LedgerAccount`, `JournalEntry` (+ lines, append-only), `AccountingPeriod`, `Cheque` (+ `ChequeEvent`), `Expense`, **post-C11** `RecurringExpense` (+ `RecurringExpensePayment`; **command-settled, not replicated** — ARCH §8.2). Tables (`Products`, `ProductVariants`, `ProductCategories`, `UnitsOfMeasure`, `StockMovements`, `PurchaseOrders` (+`PurchaseOrderLines`), `GoodsReceipts` (+lines), `Suppliers`, `LedgerAccounts`, `JournalEntries` (+`JournalLines`), `AccountingPeriods`, `Cheques` (+`ChequeEvents`), `Expenses`, `RecurringExpenses` (+`RecurringExpensePayments`)). Every `SyncableEntity` table: `Id/TenantId varchar(26)`, `UpdatedAt timestamptz(6)`, uuid `Version` + `VersionTrigger.Apply`, `Deleted`; `TenantId` first in every index; child collections as dependent tables (composite PK `(OwnerId, childKey)`, cascade FK) exactly like the SQLite configurations; `Money` as `<N>Minor bigint` + `<N>Currency varchar(3)`; `Quantity` as scaled `bigint` (×1000) + unit; enums `integer`.
- **Ports implemented here (PostgreSQL, same Application interfaces):** `IProductRepository`, `IProductCategoryRepository`, `IStockMovementRepository`, `IInventoryQueries`, `IPurchasingRepository`, `IPurchasingQueries`, `ISupplierRepository`, `ILedgerRepository`, `IPeriodRepository`, `IJournalNumberAllocator`, `IChequeRepository`, `IExpenseRepository`, `IExpenseNumberAllocator`, `IRecurringExpenseRepository`, + the `IStockMovementWriter` seam declared by S09. Each is `sealed`, derives `TenantRepository<T>` where it is a repository, reads `AsNoTracking()` + paged, and never uses `IgnoreQueryFilters`.
- **Registration:** `ServerHandlerModuleS10.AddXxx(CommandPayloadCatalogBuilder)` calls the existing Application `…Registration.Add…` module for each context in scope, then `catalog.Add<TCommand>(CommandTypes.<Const>)` per command and appends `CommandPermissionTable` rows (CommandType → the `[RequiresPermission]` code of that command record). The permission code is taken **from the Application command attribute**, not invented.
- **Re-derive, never trust (ARCH §5):** handlers already recompute totals/stock/ledger from Domain rules; the server must not accept any client-supplied computed field as authority — add a table-driven test `ServerS10RederiveTests` per rule listed in the scope bullet below.
- **Reserved wire names in this family (S06 D-S06-5):** `inventory.receipt.post`, `purchasing.order.submit`, `finance.period.close`, `finance.cheque.status` have **no Application command record today** → they stay in `ReservedCommandTypes` and are rejected `HANDLER_NOT_REGISTERED`; goods receipts flow through `purchasing.receipt.record`, period close is evaluated by Domain `AccountingPeriod` inside ledger posting, cheque status changes go through `finance.cheque.transition`. If a client phase later adds a command for one of them, S10's coverage test forces the server handler in the same change.
- **Movement-based stock (A-RULE / R-B23):** `StockMovement` is append-only (DB trigger `trg_StockMovements_immutable` raising on UPDATE/DELETE); on-hand is **derived** via `StockCalculator` (signed sum + moving weighted average) — **no absolute on-hand column is ever written**; `inventory.stock.set` is translated by the handler into a signed adjustment movement; `STOCK_WOULD_GO_NEGATIVE`/`INSUFFICIENT_STOCK` evaluated on the derived value inside the transaction. Two concurrent movements **both apply** (additive convergence).
- **Ledger (R-B22):** `JournalEntry` has **no UPDATE path** (trigger `trg_JournalEntries_immutable` + lines immutable); corrections only through `CreateReversal`; balance is structural (`JournalEntry.Create`); `LedgerPostingHandler` is registered as the server's `IDomainEventHandler<…>` set (10 event types) — the server posts journals **itself** from raised events inside the command transaction (the client's local journal is provisional and replaced on pull). Entry numbers via S09's `DocumentCounters` (`Kind='journal'`). Period state machine `Open→Closing→Closed→Locked` with the 3 close blockers; a late event posts to the next open period (as the Domain handler does).
- **Finance projections are NOT a server concern at launch:** `FinanceProjectionHandler`/read models (`DailyFinanceFact` etc.) are client read-model projections (C09); the server exposes no reporting here (insight seam = S20). Do not register the projection handler on the server.
- **Cheques:** 8-state machine + append-only `ChequeEvent`; `ChequeClearedEvent`/`ChequeBouncedEvent` post ledger entries. **Recurring expenses (post-C11):** `finance.recurring.add|edit|toggle|pay` mapped with `RecurringExpense` + `RecurringExpensePayment`; `pay` is idempotent per (recurring, period) (`RECURRING_EXPENSE_ALREADY_PAID`) and posts an `Expense` through the same path as `finance.expense.post`.
- **Purchasing:** PO state machine; `purchasing.receipt.record` creates an immutable `GoodsReceipt` (A-RULE 002) + stock movements + `GoodsReceiptPostedEvent` ledger posting in one transaction; `RECEIPT_EXCEEDS_ORDER`.
- **Closing the S05/S06 gates:** this phase **removes the `Skip`** on `CommandPermissionCatalogTests.EveryCommandType_HasARequiredPermission` and `CommandPayloadCatalogTests.CoversEveryCommandTypesAll_ExceptDeclaredReserved` — after S10 every type in `CommandTypes.All` must have a handler, a payload-catalog entry and a permission row, or the build's test run fails. Also adds `DomainEventHandlerCoverageTests.EveryRaisedEvent_HasServerHandlerWhereClientHas`.
- **Conflict policy (ARCH §8.5), per class:** version mismatch **detects** (S06 precondition) — the handler/domain decides: demographics = field-level merge through the single update command (never whole-row LWW), appointment slot collisions rejected (`APPOINTMENT_SLOT_CONFLICT`), posted invoice immutable (reversal only), inventory movements additive, ledger append-only. Each family's tests assert the applicable rows.
- **Settlement of financial effect:** commands with money/stock effect implement `IFinancialEffectReporter` (interface added by **S09**; S07/S08 families have `FinancialEffect.None`) so `SettlementResult.FinancialEffect` is `Applied` exactly when a journal/stock movement was committed in the same transaction.
- **`IAggregateVersionReader` + `IEffectCommittedProbe` (S06 seams):** register one reader per aggregate type in scope (reads the current uuid `Version` by id, tenant-filtered) so `ExpectedServerVersion` works; `NoProbe` is acceptable (recovery re-runs idempotent handlers).

## §Implementation plan

1. List `src/Animora.Persistence.PostgreSql/Migrations/Server` and take the next free ordinal for this phase's migration (name `<ordinal>_MoneyStock`).
2. **S10-a:** add entity configurations + `DbSet`s for the aggregates; generate the migration: `.sandbox/tools/dotnet-ef migrations add MoneyStock --project src/Animora.Persistence.PostgreSql --startup-project src/Animora.Persistence.PostgreSql --context AnimoraServerDbContext --output-dir Migrations/Server`; rename to the next free ordinal; add `VersionTrigger.Apply(migrationBuilder, "<Table>")` for **every** `SyncableEntity` table; update `ServerSchemaVersion.ExpectedHead`. Model tests (pure): every new table has TenantId-first indexes, named filters, uuid concurrency token.
3. **S10-b:** implement the ports; write `ServerHandlerModuleS10`; register into `AddAnimoraCommandPlane()`; add `CommandPermissionTable` rows; add per-command intake tests driving `CommandIntakeService` with a real server container over an in-memory fake store **and** PG-gated end-to-end tests.
4. `scripts/sandbox/build.sh full` → 0/0; `scripts/sandbox/test.sh Server` → green; `scripts/sandbox/run.sh` → `SMOKE RUN OK`.
5. `PROGRESS.md` per recipe (executed command count, migration ordinal, tables); commit.

## Files to create / modify

| Path | Contents |
|---|---|
| `src/Animora.Persistence.PostgreSql/Configurations/*` (Product, StockMovement, PurchaseOrder, JournalEntry, Cheque, Expense, RecurringExpense configurations) | EF mappings for the aggregates |
| `src/Animora.Persistence.PostgreSql/Repositories/*` | PostgreSQL implementations of the ports |
| `src/Animora.Persistence.PostgreSql/Migrations/Server/*<ordinal>_MoneyStock*` | migration |
| `src/Animora.Server/Handlers/ServerHandlerModuleS10.cs` | handler registration + catalog + permission rows |
| `src/Animora.Server/Authorization/CommandPermissionTable.cs` (edit) | rows for this family |
| `tests/Animora.Server.Tests/HandlersS10/*.cs` | per-command + re-derive + model tests |

## Contracts / schema added

Tables: `Products`, `ProductVariants`, `ProductCategories`, `UnitsOfMeasure`, `StockMovements`, `PurchaseOrders` (+`PurchaseOrderLines`), `GoodsReceipts` (+lines), `Suppliers`, `LedgerAccounts`, `JournalEntries` (+`JournalLines`), `AccountingPeriods`, `Cheques` (+`ChequeEvents`), `Expenses`, `RecurringExpenses` (+`RecurringExpensePayments`). No wire change. No new `CommandTypes` (inventory is the live file). 

## Rules that apply here

- **Layering (CONVENTIONS §2, `ARCHITECTURE.md` §6):** `Animora.Domain` references nothing; `Animora.Application` → Domain + Contracts + Contracts.Wire;
  only `Animora.Persistence.PostgreSql` may reference Npgsql/`UseNpgsql`; `Animora.Server` must never reference `Animora.Client.Services`,
  `Animora.Persistence.Sqlite`, `Animora.Infrastructure.Local`, `Animora.Sync.Custom` or `Animora.Desktop`; the client never references a server project.
- **C# style (§4):** `sealed` by default; `sealed record` DTOs; nullable honored (no `!` without a why-comment); every async public method takes
  `CancellationToken ct = default` and `ConfigureAwait(false)` in library projects; no `.Result`/`.Wait()`; expected failures return `Result`/`Result<T>`
  with an `ErrorCode`, never exceptions; `IClock`/`TimeProvider` injected (no `DateTime.Now`/`UtcNow`); ids are ULID strings via `IdGenerator`, never `Guid` in business code.
- **EF (§5):** EF Core 10 only; no lazy loading/proxies; reads `AsNoTracking()` + projections; named filters `"Tenant"` and `"SoftDelete"` kept separable;
  paging mandatory (max page 200); migrations additive and never edited later; `EnsureCreated` forbidden outside test fixtures.
- **Security (§7):** Authn → Authz → Entitlement in that order, never merged; `TenantId` resolved server-side from membership only; secrets never in config
  files/logs; defaults fail closed; no custom crypto; append-only audit for every security/financial action.

## Out of scope (owning phase)

- Reporting/aggregation/projections → client read models (C09, done) and **S20** insight seam; analytics features deferred (ROADMAP §5). Pull/replication of these tables → **S11/S12**.
- Journal/replication capture of these tables → **S11** (triggers added by S11 on the tables this phase creates).
- Client id remap / reconciliation → **G05**; pulling these aggregates to the client → **G03/G04**.

## Acceptance criteria (mechanical)

1. `scripts/sandbox/build.sh full` = 0 warnings / 0 errors; `scripts/sandbox/run.sh` = `SMOKE RUN OK`; `git diff --stat -- src/Animora.Desktop src/Animora.Persistence.Sqlite src/Animora.Client.Services` empty.
2. **Coverage test (mechanical):** `HandlerCoverageTests.EveryS10CommandType_HasHandler_CatalogEntry_AndPermissionRow` iterates `CommandTypes.All` filtered by the prefixes `inventory.*`, `purchasing.*`, `finance.*` and asserts registry + `CommandPayloadCatalog` (shape-resolved, see S06 D-S06-4) + `CommandPermissionTable` all contain it — **except** types listed in `ReservedCommandTypes` (S06 D-S06-5), which must instead be rejected `HANDLER_NOT_REGISTERED`. This is the phase that makes the catalog-coverage tests **mandatory** (remove both `Skip` attributes).
3. Per-command intake tests exist and pass: one `Settles_<CommandName>` fact per type (Accepted on valid payload; `Rejected` with the Domain `ErrorCode` on the family's primary invariant; idempotent replay returns the stored result).
4. `StockImmutabilityTests.Update_Delete_Raise` (PG-gated) and pure `StockDerivationTests.OnHand_IsDerived_NeverStored` pass; `grep -rn "OnHand" src/Animora.Persistence.PostgreSql/Configurations` shows no persisted on-hand column; `LedgerImmutabilityTests.JournalEntries_NoUpdatePath` passes.
5. `ls src/Animora.Persistence.PostgreSql/Migrations/Server | grep -c MoneyStock` ≥ 1; `grep -rn "IgnoreQueryFilters" src/Animora.Persistence.PostgreSql/Repositories` returns nothing.
6. PG-gated (G07-mandatory): `S10MigrationTests.Applies_OnTopOfPreviousHead`, `S10EndToEndTests.CommandsRoundTrip_ThroughIntake_WithTenantIsolation`.

## PROGRESS.md update

At the end of this phase: set the `S10` row in "Phase status" to `✅ Done` (add date + one-line summary), advance **"Next phase" → `S11`**,
append one `### S10` entry to "Repo state log" (what exists now: projects, tables/migration ordinal, error codes added, test classes), log any
deviation in the "Decision & deviation log", then commit `feat(server): S10 server handlers ii-b` and sync per the repo git workflow.
