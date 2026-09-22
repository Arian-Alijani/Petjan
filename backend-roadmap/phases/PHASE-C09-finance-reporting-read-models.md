# Phase C09 — Finance & reporting read models

**Band:** M · **Track:** C · **Depends on:** C08 · **Satisfies:** R-B01, R-B08, R-B22, R-B35
**Runnable after this phase:** desktop offline yes · server required no

## Read before starting
- `backend-roadmap/CONVENTIONS.md` §3, §4, §5, §8, §9, §10, §11, §12
- `backend-roadmap/PROGRESS.md` (C06 `StockOnHandSnapshot` + `UnitCostMinor`; C07 `InvoicePostedEvent`
  / `InvoiceReversedEvent`; C08 cheque/cash events)
- `ARCHITECTURE.md` §11.1 · `A-RULE 034`, `A-RULE 018` · `PERFORMANCE.md` §5, §6, §15
- `src/Animora.Contracts/Finance/IFinanceQueryService.cs`,
  `src/Animora.Contracts/Finance/FinanceModels.cs`
- `src/Animora.Domain/Finance/JournalEntry.cs`, `src/Animora.Domain/Finance/PostingRules.cs`,
  `src/Animora.Domain/Finance/ChartOfAccounts.cs`, `src/Animora.Domain/Finance/AccountingPeriod.cs`,
  `src/Animora.Domain/Finance/Expense.cs`
- `src/Animora.Application/Abstractions/IDomainEventHandler.cs`,
  `src/Animora.Application/Ports/ILedgerRepository.cs`,
  `src/Animora.Application/Ports/IPeriodRepository.cs`
- `src/Animora.Desktop/App.axaml.cs` (line registering `IFinanceQueryService`)

## Goal
The finance screen is driven by a **real, balanced, append-only double-entry ledger** that is
written by exactly one component — the posting handlers in this phase — and read through
**projected read models** that never ride the OLTP path. Expenses become real, and the owner-total
placeholder C01 left behind is filled.

## Design decisions (pre-made — implement, do not re-derive)
- **One ledger writer.** `LedgerPostingHandler`
  (`Animora.Application/Finance/LedgerPostingHandler.cs`) implements
  `IDomainEventHandler<T>` for `InvoicePostedEvent`, `InvoiceReversedEvent`, `PaymentRegisteredEvent`,
  `GoodsReceivedEvent`, `ChequeClearedEvent`, `ChequeBouncedEvent`, `CashSessionClosedEvent`,
  `ExpenseRecordedEvent`. For each it calls `PostingRules` (A08) to build a `JournalEntry` and
  appends it **inside the raising transaction** (A10's dispatcher already runs handlers there). No
  other code in the solution may construct a `JournalEntry` — asserted by
  `LedgerSingleWriterTests` (a source/IL scan of `Animora.Application` + `Animora.Client.Services`).
- **Balance invariant is enforced twice:** `JournalEntry.EnsureBalanced()` (A08 domain invariant) and
  `AppendOnlyGuard` (A10). A `JournalEntry` whose debits ≠ credits cannot be saved
  (`LEDGER_UNBALANCED`).
- **Entry numbering:** `JournalEntry.EntryNumber` comes from the B02 counter table, name
  `journal:<tenantId>:<periodCode>`, format `{periodCode}-{seq:00000}`, allocated by
  `JournalNumberAllocator` (mirrors C07's `InvoiceNumberAllocator`, same concurrency guarantees).
- **Period close:** `CloseAccountingPeriodCommand(string PeriodCode)` validates every source document
  in the period is settled or explicitly acknowledged, stamps `PeriodState.Closed`, and thereafter
  `LedgerPostingHandler` refuses to post into it (`PERIOD_CLOSED`) — a late event posts into the
  **next open period** with `BackdatedFromUtc` recorded, never into the closed one.
  `ReopenAccountingPeriodCommand` exists and requires `finance.period.reopen` plus a mandatory
  reason; it is audited as a high-risk action.
- **Read models are projections, not live aggregates (A-RULE 034).** Three `[LocalOnly]` projection
  tables, maintained by `FinanceProjectionHandler` from the same events, **outside** the OLTP
  critical section via B02's outbox (`OutboxRelayHandler` → `FinanceProjectionWorker`):
  - `DailyFinanceFact` — `TenantId`, `LocalDay` (`int` yyyymmdd in clinic time),
    `RevenueMinor`, `CogsMinor`, `ExpenseMinor`, `DiscountMinor`, `InsuranceMinor`, `RefundMinor`,
    `InvoiceCount`, `PaidInvoiceCount`. Unique `IX_DailyFinanceFacts_TenantId_LocalDay`.
  - `ProductSalesFact` — `TenantId`, `LocalDay`, `ProductId`, `VariantId`, `QuantitySold`,
    `RevenueMinor`, `CogsMinor`. Unique `IX_ProductSalesFacts_TenantId_LocalDay_ProductId_VariantId`.
  - `OwnerFinancialSnapshot` — `TenantId`, `OwnerId` (unique), `TotalSpendMinor`, `OpenBalanceMinor`,
    `LastInvoiceUtc`, `InvoiceCount`. **This fills C01's deferred `TotalSpend`** — C01's
    `OwnerSummaryReadModel` mapping is updated in this phase to read it (the only C01 file touched).
  Migration `015_FinanceProjections`.
  **Rebuild path:** `ILocalStoreMaintenance.RebuildFinanceProjectionsAsync(CancellationToken)`
  recomputes all three from the ledger + invoices; `FinanceProjectionIntegrityTests` asserts
  maintained == recomputed after a randomized event sequence (same discipline as C06's stock
  snapshot). Projections are a cache with a proof; the ledger is the truth.
- **COGS basis:** each `MovementKind.Sale` movement carries `UnitCostMinor` (written by C07 from the
  latest receipt), so COGS is a sum over movements — no moving-average recomputation, no valuation
  method choice left open. Documented as **FIFO-by-latest-receipt-cost** in `PROGRESS.md`.
- **Queries** (`UseCases/Finance/`) — every one reads facts, never invoices:
  - `FinanceRangeCatalogQuery()` → the `FinanceRange` list (`today`, `7d`, `30d`, `90d`, `ytd`,
    `custom`) from `FinanceRanges` (`Animora.Domain/Finance/FinanceRanges.cs`, resolved to a
    `(fromDay, toDay)` pair by `ClinicCalendar`).
  - `FinanceOverviewQuery(string Range)`, `FinanceIncomeQuery(string Range)`,
    `FinanceExpenseQuery(string Range)`, `FinanceProfitQuery(string Range)` — each a **single**
    grouped statement over `DailyFinanceFact` (+ `ProductSalesFact` for the product block), with
    deltas computed against the immediately preceding window of equal length.
  - `ExpenseCategoriesQuery()`, `PaymentMethodsQuery()` from the A08 statics.
  - Results are cached in `TenantScopedCache` (C03) with key
    `tenant|user|finance:<queryName>:<range>|v<maxLocalDayInRange>` — the version component is the
    latest fact day, so a new fact invalidates naturally (A-RULE 033).
- **Expenses:** `RecordExpenseCommand(category, Money amount, occurredUtc, PaymentMethod, note)` ⟶
  journaled, `[RequiresPermission("finance.expense.record")]`,
  `[Auditable(AuditCategory.Financial, "finance.expense.record")]`, raising
  `ExpenseRecordedEvent` → ledger + facts. `CommandTypes` += `finance.expense.record`,
  `finance.period.close`, `finance.period.reopen` (3).
- **Sample data:** none new — the facts are **derived** from C05–C08's sample data by running
  `RebuildFinanceProjectionsAsync` at the end of seeding. This proves the rebuild path on every
  fresh install.
- **ViewModel changes (exhaustive):** `FinancePageViewModel` gains the readiness-gate line.
- **New error codes:** `LEDGER_UNBALANCED`, `PERIOD_CLOSED`, `PERIOD_NOT_FOUND`,
  `PERIOD_REOPEN_REASON_REQUIRED`, `EXPENSE_CATEGORY_INVALID`, `FINANCE_RANGE_INVALID`.

## Files to create / modify
| Path | Action | Contents (one line) |
|---|---|---|
| `src/Animora.Domain/Finance/FinanceRanges.cs` | create | range catalog + window resolution |
| `src/Animora.Application/Finance/LedgerPostingHandler.cs` | create | the single ledger writer (8 events) |
| `src/Animora.Application/Finance/FinanceProjectionHandler.cs` | create | fact maintenance from outbox |
| `src/Animora.Application/UseCases/Finance/*.cs` | create | 7 queries + 3 commands + handlers/validators |
| `src/Animora.Application/UseCases/Finance/ReadModels/*.cs` | create | 5 read models |
| `src/Animora.Persistence.Sqlite/Entities/DailyFinanceFact.cs` | create | `[LocalOnly]` daily fact |
| `src/Animora.Persistence.Sqlite/Entities/ProductSalesFact.cs` | create | `[LocalOnly]` product fact |
| `src/Animora.Persistence.Sqlite/Entities/OwnerFinancialSnapshot.cs` | create | `[LocalOnly]` owner totals |
| `src/Animora.Persistence.Sqlite/Configurations/Finance/*.cs` | create | 3 configurations + unique indexes |
| `src/Animora.Persistence.Sqlite/Migrations/*` | create | `015_FinanceProjections` + snapshot |
| `src/Animora.Persistence.Sqlite/Finance/JournalNumberAllocator.cs` | create | per-period entry numbering |
| `src/Animora.Persistence.Sqlite/Queries/FinanceQueries.cs` | create | 4 grouped fact projections |
| `src/Animora.Persistence.Sqlite/Maintenance/LocalStoreMaintenance.cs` | modify | add `RebuildFinanceProjectionsAsync` |
| `src/Animora.Persistence.Sqlite/Seed/LocalSeeder.cs` | modify | run the rebuild after sample seeding |
| `src/Animora.Infrastructure.Local/Workers/FinanceProjectionWorker.cs` | create | drains the outbox off the OLTP path |
| `src/Animora.Client.Services/Finance/FinanceQueryService.cs` | create | real `IFinanceQueryService` |
| `src/Animora.Client.Services/Finance/FinanceMapping.cs` | create | read models ↔ Contracts DTOs |
| `src/Animora.Client.Services/Patients/PatientsMapping.cs` | modify | read `TotalSpend` from the snapshot (C01 debt) |
| `src/Animora.Contracts.Wire/Commands/CommandTypes.cs` | modify | add 3 command types |
| `src/Animora.Desktop/App.axaml.cs` | modify | swap `IFinanceQueryService`; register the projection worker |
| `src/Animora.Desktop/Pages/Finance/FinancePageViewModel.cs` | modify | readiness-gate line |
| `src/Animora.Desktop/Mocks/MockFinanceQueryService.cs` | delete | replaced |
| `src/Animora.Domain/Results/ErrorCodes.cs` | modify | append 6 codes |
| `tests/Animora.Domain.Tests/PostingRulesTests.cs` | create | every event's entry is balanced and correct |
| `tests/Animora.Application.Tests/LedgerSingleWriterTests.cs` | create | only the handler constructs `JournalEntry` |
| `tests/Animora.Persistence.Tests/FinanceProjectionIntegrityTests.cs` | create | maintained == recomputed; rebuild |
| `tests/Animora.Application.Tests/PeriodCloseTests.cs` | create | close, late event, reopen reason |

## Implementation steps
1. Add `FinanceRanges`, `JournalNumberAllocator`, the 6 error codes.
2. Add the three fact entities + configurations + migration `015_FinanceProjections`.
3. Write `LedgerPostingHandler` for all 8 events, using `PostingRules` only; add
   `LedgerSingleWriterTests` immediately so the invariant cannot regress.
4. Write `FinanceProjectionHandler` + `FinanceProjectionWorker` draining the B02 outbox with bounded
   concurrency (1 worker, batch 100, idle backoff) — never inside a request path.
5. Add `RebuildFinanceProjectionsAsync` and call it at the end of seeding.
6. Write `FinanceQueries` (4 grouped statements) and the 7 queries with the tenant-safe cache keys.
7. Write `RecordExpenseCommand`, `CloseAccountingPeriodCommand`, `ReopenAccountingPeriodCommand`,
   including the late-event "post into the next open period" rule.
8. Write `FinanceMapping`/`FinanceQueryService`; update `PatientsMapping` to read
   `OwnerFinancialSnapshot.TotalSpendMinor` (closing C01's deferred item).
9. Swap the DI line, register the worker, add the ViewModel line, delete the mock.
10. Write the four test classes.
11. Verify manually: post an invoice → the ledger gains a balanced entry, the day's fact updates,
    the finance overview numbers move; record an expense → profit drops accordingly; close a period
    → posting into it is refused and a late event lands in the next period; the owner sheet now
    shows a real total spend.
12. `build.sh full` = 0/0; `test.sh` = `TESTS OK`; `run.sh` = `SMOKE RUN OK`.

## Contracts / schema added
- Domain: `FinanceRanges`.
- Application: `LedgerPostingHandler`, `FinanceProjectionHandler`, 7 finance queries, 3 finance
  commands, 5 read models.
- Persistence: `DailyFinanceFact`, `ProductSalesFact`, `OwnerFinancialSnapshot`,
  `JournalNumberAllocator`, `FinanceQueries`, `RebuildFinanceProjectionsAsync`, migration
  `015_FinanceProjections`.
- Infrastructure: `FinanceProjectionWorker`.
- Client services: `FinanceQueryService`, `FinanceMapping`; `PatientsMapping` updated.
- Wire: 3 `CommandTypes` entries. `ErrorCodes` += 6. No `Animora.Contracts` change.
- DI: `IFinanceQueryService` → `FinanceQueryService`; `FinanceProjectionWorker` registered.
  **Mock deleted: `MockFinanceQueryService.cs`.**

## Rules that apply here
- **A-RULE 002 / R-B22** — the ledger is append-only and balanced; corrections are compensating
  entries produced by C07's credit notes, never edits.
- **A-RULE 034 / R-B35** — reporting reads facts maintained off the OLTP path by a bulkheaded worker;
  no finance query touches `Invoices` or `JournalLines` directly.
- **A-RULE 033** — every finance cache key carries tenant + user + resource + a version component.
- **A-RULE 018** — facts are denormalized read models; `JournalEntry` remains the normalized truth.
- **PERFORMANCE.md §15** — `TenantScopedCache` is capacity-bounded.
- **CONVENTIONS §5** — day bucketing happens in the fact table (`LocalDay` int), so no query needs a
  time-zone-aware SQL function.

## Out of scope
- Dashboard, history, search and profile surfaces → C10 (they read these same facts).
- Cross-tenant or operator-side reporting → E02 (seam only).
- Server-side ledger posting → D07 (it re-derives from the same `PostingRules`).
- Tax computation beyond the invoice-level tax A07 already models → not a product requirement.

## Acceptance criteria
- Build: `scripts/sandbox/build.sh full` = 0 warnings / 0 errors.
- `src/Animora.Desktop/Mocks/MockFinanceQueryService.cs` does not exist; no DI line references it.
- `PostingRulesTests` green: every one of the 8 events produces a balanced entry with the expected
  account codes and signs; an unbalanced construction returns `LEDGER_UNBALANCED`.
- `LedgerSingleWriterTests` green: `JournalEntry` is constructed in exactly one production type.
- `FinanceProjectionIntegrityTests` green: after 300 randomized business events the three fact tables
  equal their recomputation; `RebuildFinanceProjectionsAsync` repairs a deliberately corrupted fact.
- `PeriodCloseTests` green: posting into a closed period returns `PERIOD_CLOSED`; a late event lands
  in the next open period with `BackdatedFromUtc` set; reopening without a reason returns
  `PERIOD_REOPEN_REASON_REQUIRED`.
- Finance overview/income/expense/profit numbers reconcile exactly with the ledger for the seeded
  data (a test asserts revenue from facts == sum of posted invoice totals).
- The owner sheet's total spend is non-zero for owners with invoices (C01's deferred item closed).
- Runnability (`CONVENTIONS.md` §11): `scripts/sandbox/run.sh` = `SMOKE RUN OK`.

## PROGRESS.md update
Mark C09 done; advance "Next phase" to `C10`; append a "Repo state" entry listing: the **single
ledger writer** rule, `JournalNumberAllocator` format, the three fact tables + migration
`015_FinanceProjections` (**C10 and E02 read them**), `FinanceProjectionWorker` + the outbox-driven
maintenance, `RebuildFinanceProjectionsAsync`, the FIFO-by-latest-receipt COGS basis, period-close
and late-event rules, the 10 use cases, `FinanceQueryService`/`FinanceMapping`, **C01's deferred
`TotalSpend` now closed**, the DI swap, **mock deleted: `MockFinanceQueryService.cs`**,
`CommandTypes` += 3, `ErrorCodes` += 6, the 4 test classes, deviations.
