# Phase D07 — Server command handlers II — money & stock

> **PostgreSQL baseline (2026-10-08):** Read `ARCHITECTURE.md` §PG and §16-CT–§18-CT / `STACK.md` §6 before implementation. They govern PostgreSQL 18, Npgsql 10.0.3, UUID Version, tenant journal, snapshot/batch atomicity, pool budgets and deployment-only migrations. This phase is a future instruction, not an implementation claim. Mandatory PostgreSQL CI cannot pass by skipping DB tests.

**Band:** M · **Track:** D · **Depends on:** D06 · **Satisfies:** R-B01, R-B17, R-B22, R-B23
**Runnable after this phase:** desktop offline yes · server required no

## Read before starting
- `backend-roadmap/CONVENTIONS.md` §2, §3, §4, §5, §6 (items 2, 9), §7, §8, §9, §12
- `backend-roadmap/PROGRESS.md` (D05 entry — the handler seam and `FinancialEffect`; D06 entry — the
  handler skeleton, payload namespace, `IServerOutbox`, billing handoff rows; C06/C07/C08/C09
  entries — client command names, `InvoiceNumberAllocator`, `JournalNumberAllocator`,
  `ChartOfAccounts`, `StockOnHandSnapshot`, `StockSnapshotUpdater`, `AdjustmentReasons`;
  A06/A07/A08 entries — inventory, sales and ledger domain invariants)
- `ARCHITECTURE.md` §8.5 (posted invoice / payment / ledger / inventory rows), §11.1, §11.2, §11.4
- `A-RULE 002`, `A-RULE 003`, `A-RULE 006`, `A-RULE 018`
- `src/Animora.Domain/Inventory/**`, `src/Animora.Domain/Sales/**`, `src/Animora.Domain/Finance/**`

## Goal
Every **financial and stock** command settles authoritatively on the server: invoices post
immutably, payments apply idempotently, stock moves only by movement rows, the double-entry ledger
stays append-only and balanced, cash sessions and cheques follow their domain workflows, and periods
close. This is the phase where "the server is the single global authority for settlement" becomes
literally true for money.

## Design decisions (pre-made — implement, do not re-derive)
- **Scope = exactly the money/stock command types registered in `CommandTypes`
  (`inventory.*`, `purchasing.*`, `sales.*`, `cashier.*`, `finance.*`)**, one
  `sealed class <Verb><Aggregate>ServerHandler` per row, in `src/Animora.Server/Handlers/Inventory/`,
  `.../Sales/`, `.../Finance/`. The table is **transcribed verbatim from
  `src/Animora.Contracts.Wire/Commands/CommandTypes.cs` as of 2026-10-07 (the single source of
  truth)** — **47** command types. At execution time, re-slice the live `CommandTypes` by these five
  prefixes and reconcile the count:
  | Group | `CommandType` prefix | Commands (verbatim suffixes) | # |
  |---|---|---|---|
  | Inventory | `inventory.` | `category.create`, `category.rename`, `category.delete`, `product.create`, `product.update`, `product.delete`, `variant.add`, `receipt.post`, `stock.set`, `stock.adjust` | 10 |
  | Purchasing | `purchasing.` | `supplier.create`, `order.create`, `order.submit`, `order.cancel`, `receipt.record` | 5 |
  | Sales / POS | `sales.` | `invoice.create`, `invoice.delete`, `invoice.owner.set`, `invoice.line.add`, `invoice.line.edit`, `invoice.line.remove`, `invoice.line.return`, `invoice.discount.set`, `invoice.insurance.set`, `invoice.post`, `invoice.void`, `invoice.refund`, `payment.register`, `adjustment.add`, `insurer.create`, `insurer.update`, `insurer.delete` | 17 |
  | Cashier | `cashier.` | `session.open`, `session.close`, `movement.record` | 3 |
  | Finance (cheques, expenses, recurring, periods) | `finance.` | `cheque.create`, `cheque.update`, `cheque.status`, `cheque.transition`, `cheque.delete`, `expense.post`, `expense.reverse`, `recurring.add`, `recurring.edit`, `recurring.toggle`, `recurring.pay`, `period.close` | 12 |

  **Total: 47.** The real prefix for cheques is **`finance.cheque.*`** (there is no `cheques.*`
  prefix). The `finance.recurring.*` family (aggregate `src/Animora.Domain/Finance/RecurringExpense.cs`,
  migration `029`) post-dates the original C-track roadmap and is included deliberately — see the
  post-C11 note in `PROGRESS.md`.
  **Server settlement + ledger posting for `finance.recurring.*` (audit 2026-10-08 · RM-H-03, PH-09):**
  `RecurringExpense` is a *template*, not a ledger row — `add`/`edit`/`toggle` carry
  `FinancialEffect.None`, while **`finance.recurring.pay` is a money command**: it settles by posting
  an ordinary `Expense` (same path as `finance.expense.post`, i.e. `ExpenseRules.CreateAndPostAsync`)
  and writing a `RecurringExpensePayment` row.
  | Command | Server invariant | Rejects with | Effect |
  |---|---|---|---|
  | `finance.recurring.add` | `RecurringExpense.Create` — category ∈ `ExpenseCategories`, title non-empty, amount > 0 (`Money.Irt`), `DayOfMonth` 1–31 | `EXPENSE_CATEGORY_INVALID`, `VALIDATION_FAILED` | `None` |
  | `finance.recurring.edit` | `RecurringExpense.Edit` — same validation; already-posted payments untouched | `RECURRING_EXPENSE_NOT_FOUND`, `EXPENSE_CATEGORY_INVALID`, `VALIDATION_FAILED` | `None` |
  | `finance.recurring.toggle` | `Activate`/`Deactivate` — idempotent | `RECURRING_EXPENSE_NOT_FOUND` | `None` |
  | `finance.recurring.pay` | template exists + `IsActive`; period key (`yyyyMM`, derived by the clinic calendar) **not already paid**; `PeriodGuard.EnsureOpen` for the posting date; posts the expense and inserts the `RecurringExpensePayment` in **one** `ExecuteAsync` | `RECURRING_EXPENSE_NOT_FOUND`, `RECURRING_EXPENSE_INACTIVE`, `RECURRING_EXPENSE_ALREADY_PAID`, `PERIOD_CLOSED` | `Applied` / `NotApplied` per the truthfulness rule below |
  **`LedgerPostingService` extension (PH-09):** `RecurringExpense` emits expense-like events, so the
  single-writer ledger must consume them through the **same** posting path as `Expense` — the
  `ExpensePosted` outbox message produced by a recurring payment is indistinguishable from a one-off
  expense's (debit = the `ChartOfAccounts` account resolved from `RecurringExpense.CategoryCode`,
  credit = cash/bank by `SettledMethod`, `EnsureBalanced` per currency). There is **no** second
  posting route for recurring payments and no direct `JournalEntry` write in the recurring handlers;
  `DailyFinanceFacts.Expenses` therefore includes recurring payments automatically. Uniqueness
  `(TenantId, RecurringExpenseId, PeriodCode)` is the DB backstop against a concurrent double-pay
  (a unique-violation maps to `RECURRING_EXPENSE_ALREADY_PAID`, `NotApplied`). Add
  `Money/RecurringExpenseSettlementTests` (pay twice same period → one expense + one payment row;
  pay inside a closed period → `PERIOD_CLOSED`; recurring payment balances in the ledger and appears in
  `DailyFinanceFacts`; inactive template refused).
  **Command-group → payload-folder mapping** (closes the handler-count arithmetic): `Inventory/` =
  inventory 10 + purchasing 5 = **15**; `Sales/` = sales **17**; `Finance/` = cashier 3 + finance 12 =
  **15**; 15 + 17 + 15 = **47**.
  Each gets its `CommandPermissions` entry and its `ResponsibilityMap` row in the same commit. **No
  new `CommandTypes` constant is coined here** — a missing name is a C-phase defect to log, not a name
  to coin. Acceptance: **handler set == the five-prefix slice of `CommandTypes`;
  `PayloadContractTests` round-trips all 47.**
- **The four money invariants, enforced in the handler layer and covered by a dedicated test class
  each — these are the reason this phase exists:**
  1. **Posted invoice is immutable** (A-RULE 002, §8.5). `invoice.post` transitions
     `Draft → Posted`, stamps `PostedAtUtc` + the allocated invoice number, and writes the ledger
     entries. Any command that would mutate a `Posted` invoice's lines, totals or discount returns
     `Rejected` + `INVOICE_ALREADY_POSTED`. Correction happens **only** through the append-only adjustment commands —
     `sales.invoice.void` (unpaid), `sales.invoice.refund` (paid / partially paid),
     `sales.invoice.line.return` and `sales.adjustment.add` — each writing a new `InvoiceAdjustment`
     row (the live domain model, `src/Animora.Domain/Sales/Invoice.cs`) and never editing the posted
     rows. (C07's roadmap text mentions a `ReversesInvoiceId` link; it does **not** exist in the live
     domain — do not introduce it here without a C07 decision-log entry.) D02's
     `AppendOnlyGuard` server counterpart, `ServerAppendOnlyGuard`, is added in this phase and
     intercepts `SaveChanges` to **throw** if a tracked entity of a protected type is in `Modified`
     or `Deleted` state (`Invoice` when `Posted`, `JournalEntry`, `JournalLine`, `StockMovement`,
     `Payment`, `ServerAuditEntry`).
  2. **Payment is an idempotent command, never overwritten** (§8.5, A-RULE 006). `payment.register`
     is keyed by the envelope's `OperationId`; D05's idempotency layer already guarantees one effect,
     and the handler additionally refuses to register a payment that would exceed the invoice's
     open balance (`PAYMENT_EXCEEDS_BALANCE`) — over-payment is a domain rejection, not a clamp.
     A payment correction is a **compensating (negative) payment row** written through the refund /
     adjustment path (`sales.invoice.refund`); there is no `payment.reverse` command, and the original
     payment is never deleted or edited.
  3. **Ledger is append-only and balanced** (R-B22, §11.1). `LedgerPostingService`
     (`src/Animora.Server/Handlers/Finance/LedgerPostingService.cs`) is the **only** writer of
     `JournalEntry`/`JournalLine` on the server; every posting goes through
     `EnsureBalanced(entry)` which fails with `LEDGER_UNBALANCED` when
     `Σ debit minor ≠ Σ credit minor` **per currency**, and the transaction aborts. Account
     resolution uses C09's `ChartOfAccounts` codes — never a hard-coded account id. Corrections are
     compensating entries only.
  4. **Inventory is movement-based** (R-B23, §11.2). Every stock change writes a `StockMovement` row
     with a signed quantity and a reason from `AdjustmentReasons`; **no handler ever writes an
     absolute on-hand value.** The server's `StockOnHandProjection` (a derived table, see below) is
     recomputed from movements and is explicitly marked as a cache, not a source of truth. A sale
     below zero is refused with `INSUFFICIENT_STOCK` unless the tenant setting
     `AllowNegativeStockOnSale` (C07's `LocalStoreOptions` flag, mirrored server-side in a new
     `TenantSettings` row) is on — in which case the movement is still written and an
     `ActivityEvent`-equivalent outbox message flags it.
- **Server-side derived tables** (migration `105_ServerProjections`, additive):
  - `StockOnHandProjections` — `(TenantId, ProductId)` PK, `QuantityOnHand`, `LastMovementId`,
    `RecomputedAtUtc`. Updated **inside** the same transaction as the movement (so a client's pull
    never sees a movement without its projection), and fully rebuildable by
    `IServerStoreMaintenance.RebuildStockProjectionsAsync` — the server counterpart of C06's
    `RebuildStockSnapshotsAsync`.
  - `DailyFinanceFacts` — the server twin of C09's local fact table, `(TenantId, Date)` PK, revenue /
    payments / discounts / expenses / cheque movements in minor units. Projected by
    **`FinanceProjectionHandler` consuming outbox messages** (D09 runs the relay; until then the
    `NullServerOutbox` makes the projection a no-op and a maintenance rebuild fills it) — never
    computed on the OLTP request path (A-RULE 034, R-B35).
  Both tables are **not replicated** and are added to `ServerModel.NonReplicatedProjectionTables`
  (a new list; the client has its own equivalents from C06/C09).
- **Cash session & cheque workflows** follow their domain state machines exactly (§8.5 "Cash
  session: domain workflow"): `session.open` refuses a second open session for the same user/device
  (`SESSION_ALREADY_OPEN`); `session.close` requires a recorded count and computes the variance
  against C08's `CashVarianceThresholds`, returning `Accepted` with the variance in
  `ServerCommandEffect` and raising an outbox alert above the threshold; cheque transitions follow
  `Registered → Deposited → Cleared | Bounced` and refuse any other transition
  (`CHEQUE_STATE_INVALID`).
- **`period.close`** (`finance.period.close`) sets `AccountingPeriod.ClosedAtUtc` and, from that
  moment, **every** handler that would post into a closed period returns `Rejected` +
  `PERIOD_CLOSED`. The check lives in one place — `PeriodGuard.EnsureOpen(tenantId, postingDate)` —
  called by `LedgerPostingService` and by the invoice/payment/expense handlers. A closed period is
  reopened only by an operator action (out of scope here; D10 owns operator actions).
- **`FinancialEffect` is reported truthfully, per outcome** (A-RULE 024, R-B16):
  `Accepted` → `Applied`; a domain rejection **before** any write → `NotApplied`; a
  `DbUpdateConcurrencyException` or an aborted transaction → `NotApplied` (the transaction rolled
  back, so nothing applied); an infrastructure failure **after** commit but before the response →
  the idempotency row is already `Settled`, so the retry returns `Applied`. `Unknown` is reserved
  for the single case where the transaction outcome genuinely cannot be determined, and a test
  asserts no handler returns it on a deterministic path.
- **Handler skeleton is D06's, plus two additions:** (a) `PeriodGuard.EnsureOpen` before any
  posting; (b) the effect's `FinancialEffect` is computed by `FinancialEffectResolver` rather than
  hand-written per handler, so the truthfulness rule cannot drift.
- **Tests organized by flow and by invariant:**
  `tests/Animora.Server.Tests/Flows/SalesFlowTests.cs` (handoff → invoice → lines → post → payment →
  cashier session → close, with ledger and stock assertions),
  `Flows/PurchasingFlowTests.cs`, `Money/ImmutableInvoiceTests.cs`,
  `Money/LedgerBalanceTests.cs`, `Money/PaymentIdempotencyTests.cs`,
  `Money/MovementBasedStockTests.cs`, `Money/PeriodCloseTests.cs`.
- **New error codes** (added only if the owning C-phase did not define them):
  `INVOICE_ALREADY_POSTED`, `PAYMENT_EXCEEDS_BALANCE`, `LEDGER_UNBALANCED`, `INSUFFICIENT_STOCK`,
  `SESSION_ALREADY_OPEN`, `CHEQUE_STATE_INVALID`, `PERIOD_CLOSED`, `APPEND_ONLY_VIOLATION`.

## Files to create / modify
| Path | Action | Contents (one line) |
|---|---|---|
| `src/Animora.Contracts.Wire/Payloads/Inventory/*.cs` | create | 15 payload records (inventory 10 + purchasing 5) |
| `src/Animora.Contracts.Wire/Payloads/Sales/*.cs` | create | 17 payload records (sales) |
| `src/Animora.Contracts.Wire/Payloads/Finance/*.cs` | create | 15 payload records (cashier 3 + finance 12) |
| `src/Animora.Server/Handlers/Inventory/*.cs` | create | 15 handlers (inventory 10 + purchasing 5) |
| `src/Animora.Server/Handlers/Sales/*.cs` | create | 17 handlers (sales) |
| `src/Animora.Server/Handlers/Finance/*.cs` | create | 15 handlers (cashier 3 + finance 12) |
| `src/Animora.Server/Handlers/Finance/LedgerPostingService.cs` | create | the sole ledger writer + `EnsureBalanced` |
| `src/Animora.Server/Handlers/Finance/PeriodGuard.cs` | create | one-place closed-period check |
| `src/Animora.Server/Handlers/Inventory/StockMovementWriter.cs` | create | movement + projection, one transaction |
| `src/Animora.Server/Commands/FinancialEffectResolver.cs` | create | truthful effect per outcome |
| `src/Animora.Persistence.PostgreSql/Context/ServerAppendOnlyGuard.cs` | create | `SaveChanges` interceptor |
| `src/Animora.Persistence.PostgreSql/Projections/StockOnHandProjection.cs` | create | derived stock table |
| `src/Animora.Persistence.PostgreSql/Projections/DailyFinanceFact.cs` | create | derived finance fact table |
| `src/Animora.Persistence.PostgreSql/Projections/FinanceProjectionHandler.cs` | create | outbox-driven projector |
| `src/Animora.Persistence.PostgreSql/Maintenance/IServerStoreMaintenance.cs` | create | rebuild ports + impl |
| `src/Animora.Persistence.PostgreSql/Identity/TenantSettings.cs` | create | per-tenant server settings row |
| `src/Animora.Persistence.PostgreSql/Migrations/105_ServerProjections*` | create | 3 tables + indexes |
| `src/Animora.Server/Handlers/MoneyHandlerRegistration.cs` | create | explicit registry lines |
| `src/Animora.Server/Commands/CommandPermissions.cs` | modify | one entry per new command type |
| `src/Animora.Server/Commands/SettlementResultFactory.cs` | modify | `ResponsibilityMap` rows |
| `src/Animora.Server/Program.cs` | modify | register handlers, guard, maintenance |
| `tests/Animora.Server.Tests/Flows/SalesFlowTests.cs` | create | full POS chain, server-side |
| `tests/Animora.Server.Tests/Flows/PurchasingFlowTests.cs` | create | order → receipt → stock → ledger |
| `tests/Animora.Server.Tests/Money/*.cs` | create | 5 invariant test classes |

## Implementation steps
1. Check `ErrorCodes` for the eight codes; add only the missing ones.
2. Write the 47 wire payload records.
3. Add `ServerAppendOnlyGuard` and register it as a `SaveChangesInterceptor`; add `TenantSettings`,
   the two projection tables and migration `105_ServerProjections`.
4. Write `LedgerPostingService` (+ `EnsureBalanced`), `PeriodGuard`, `StockMovementWriter`,
   `FinancialEffectResolver`, `IServerStoreMaintenance` and `FinanceProjectionHandler`.
5. Write the 47 handlers on the D06 skeleton + the two additions, each calling its domain method.
6. Write `MoneyHandlerRegistration`; extend `CommandPermissions` and `ResponsibilityMap`.
7. Write the two flow tests and the five invariant test classes.
8. Verify against a real PostgreSQL: the POS chain settles; the ledger balances to the cent; the
   stock projection equals the movement sum; a second `payment.register` with the same
   `OperationId` produces exactly one payment; editing a posted invoice is refused by both the
   handler **and** the interceptor; posting into a closed period is refused.
9. Re-run D05's `CommandPermissionMapTests` and `SettlementContractTests`.
10. `build.sh full` = 0/0; `test.sh` = `TESTS OK`; desktop `run.sh` = `SMOKE RUN OK`.

## Contracts / schema added
- Wire: 47 `<Name>PayloadV1` records under `Payloads/Inventory|Sales|Finance/`.
  `WireContract.Version` retains D05 **v2** (§PG.7; compatibility DTO suffix V1 does not reset the envelope version).
- Persistence.PostgreSql: `ServerAppendOnlyGuard`, `StockOnHandProjection`, `DailyFinanceFact`,
  `FinanceProjectionHandler`, `IServerStoreMaintenance` + impl, `TenantSettings`,
  `ServerModel.NonReplicatedProjectionTables`, migration `105_ServerProjections`.
- Server: 47 handlers, `LedgerPostingService`, `PeriodGuard`, `StockMovementWriter`,
  `FinancialEffectResolver`, `MoneyHandlerRegistration`; `CommandPermissions` and
  `ResponsibilityMap` extended.
- `ErrorCodes` += up to 8 (only those not already defined by the C06–C09 phases).
- No `Animora.Contracts` change, no new `CommandTypes` constant, no DI change in `App.axaml.cs`.

## Rules that apply here
- **A-RULE 002 / R-B22** — posted invoices, payments, journal entries/lines, stock movements and
  audit rows are append-only; the `ServerAppendOnlyGuard` interceptor makes a violation a thrown
  defect, not a silent update. Corrections are compensating documents.
- **A-RULE 003 / R-B17** — no financial conflict is ever resolved by client-wins/server-wins; a
  conflict produces a rejection or a compensating entry with a reason and an audit trail.
- **A-RULE 006** — one financial effect per `OperationId`, guaranteed by D05's store and re-proven
  here for payments specifically.
- **R-B23** — on-hand is derived from movements; `StockOnHandProjections` is explicitly a cache and
  is rebuildable.
- **A-RULE 018 / A-RULE 034 / R-B35** — `DailyFinanceFacts` is a denormalized read model projected
  off the outbox, never computed inside the OLTP transaction of a sale.
- **A-RULE 024 / R-B16** — `FinancialEffect` is resolved by one component and is truthful for every
  outcome; `Unknown` never appears on a deterministic path.
- **R-B24** — every amount is integer minor units + currency; `EnsureBalanced` compares per currency.

## Out of scope
- The outbox table, relay worker and the workers that drive `FinanceProjectionHandler` → D09.
- Replicating invoices/payments/movements to clients → D08.
- Reporting queries and read-model endpoints for the admin site → E02.
- Reopening a closed period, and every other operator action → D10.
- Client-side submission and settlement reconciliation → E06.

## Acceptance criteria
- Build: `scripts/sandbox/build.sh full` = 0 warnings / 0 errors.
- `SalesFlowTests` green (or skipped with reason): visit handoff → invoice → 3 lines (2 products,
  1 service) → discount → post → partial payment → final payment → session close; the invoice is
  `Paid`, the ledger entries balance and their revenue total equals the invoice total, the stock
  projection dropped by exactly the sold quantities, and every command's idempotency row is
  `Settled`/`Accepted`.
- `PurchasingFlowTests` green: order → submit → receipt → stock increased by the received quantity →
  a payable ledger entry exists and balances.
- `ImmutableInvoiceTests` green: `invoice.line.add` on a posted invoice → `Rejected` +
  `INVOICE_ALREADY_POSTED`; a direct EF mutation of a posted invoice throws
  `APPEND_ONLY_VIOLATION` from the interceptor; `sales.invoice.refund` appends an `InvoiceAdjustment` and
  leaves the original posted rows byte-identical.
- `LedgerBalanceTests` green: an unbalanced posting attempt returns `LEDGER_UNBALANCED` and the
  transaction rolls back with **zero** rows written; a correction is a compensating entry, never an
  update.
- `PaymentIdempotencyTests` green: the same `payment.register` envelope twice → one `Payment` row,
  two identical `SettlementResult`s with `FinancialEffect.Applied`; an over-payment →
  `PAYMENT_EXCEEDS_BALANCE` with `FinancialEffect.NotApplied`.
- `MovementBasedStockTests` green: no handler writes an absolute on-hand value (asserted by
  recomputing from movements after every flow); a sale below zero is refused unless the tenant
  setting allows it, and when allowed the movement is still written plus an outbox alert.
- `RecurringExpenseSettlementTests` green (audit 2026-10-08 · RM-H-03/PH-09): `finance.recurring.pay` posts exactly one
  balanced expense through `LedgerPostingService` and one `RecurringExpensePayment`; a second pay for the same period
  → `RECURRING_EXPENSE_ALREADY_PAID` + `NotApplied`; a pay inside a closed period → `PERIOD_CLOSED`.
- `PeriodCloseTests` green: after `finance.period.close`, an invoice post, a payment and an expense
  dated inside the period are each `Rejected` + `PERIOD_CLOSED`, while a later-dated one succeeds.
- Desktop unaffected: `scripts/sandbox/run.sh` = `SMOKE RUN OK`.

## PROGRESS.md update
Mark D07 done; advance "Next phase" to `D08`; append a "Repo state" entry listing: the 47 handled
`CommandTypes`, the four money invariants and where each is enforced, `ServerAppendOnlyGuard` +
its protected type list (**never weakened by a later phase**), `LedgerPostingService` as the sole
ledger writer + `EnsureBalanced`, `PeriodGuard` (**every later posting path must call it**),
`StockMovementWriter` + `StockOnHandProjections` as a rebuildable cache, `DailyFinanceFacts` +
`FinanceProjectionHandler` (**D09 drives it via the outbox**), `IServerStoreMaintenance`,
`TenantSettings`, migration `105_ServerProjections`, `FinancialEffectResolver` (**the only producer
of `FinancialEffect`**), the new error codes, the flow/invariant test names, deviations.
