# Phase A08 — Domain: Finance ledger, Cheques, Expenses

**Band:** M · **Track:** A · **Depends on:** A07 · **Satisfies:** R-B01, R-B17, R-B22
**Runnable after this phase:** desktop offline yes · server required no

## Read before starting
- `backend-roadmap/CONVENTIONS.md` §3, §4, §6 (item 9), §9, §11, §12
- `backend-roadmap/PROGRESS.md`
- `ARCHITECTURE.md` §11.1 (append-only ledger), §8.5 (ledger = append-only forever), §21 (A-RULE 002/003)
- `src/Animora.Domain/ValueObjects/Money.cs`, `src/Animora.Domain/Sales/*`,
  `src/Animora.Domain/Events/SalesEvents.cs`, `src/Animora.Domain/Events/InventoryEvents.cs`,
  `src/Animora.Domain/Results/ErrorCodes.cs`
- `src/Animora.Contracts/Finance/FinanceModels.cs`, `src/Animora.Contracts/Cheques/ChequeModels.cs`

## Goal
Model the accounting backbone: a chart of accounts, an append-only balanced double-entry journal, the
posting rules that translate sales/purchasing/cashier events into journal entries, the cheque lifecycle,
expenses, and the accounting-period close rule with reversal/credit-note mechanics.

## Design decisions (pre-made — implement, do not re-derive)
- **Folder:** `src/Animora.Domain/Finance/`.
- `Finance/LedgerAccount.cs` — `sealed class LedgerAccount : AggregateRoot`:
  `Code` (numeric string, unique per tenant), `Name`, `AccountType Type`,
  `string? ParentAccountId`, `bool IsPostable`, `bool IsActive`, `string? Description`.
  `enum AccountType { Asset, Liability, Equity, Revenue, Expense }`.
  `NormalBalance` is derived: `Asset`/`Expense` → Debit; `Liability`/`Equity`/`Revenue` → Credit.
- `Finance/ChartOfAccounts.cs` — static seed of the **authoritative** default chart (code → name, type,
  postable). Exact codes (later phases reference these constants, never literals):
  ```
  1000 Cash on Hand            Asset    postable   AccountCodes.CashOnHand
  1010 Bank Account            Asset    postable   AccountCodes.Bank
  1020 Cheques Receivable      Asset    postable   AccountCodes.ChequesReceivable
  1100 Accounts Receivable     Asset    postable   AccountCodes.AccountsReceivable
  1110 Insurance Receivable    Asset    postable   AccountCodes.InsuranceReceivable
  1200 Inventory               Asset    postable   AccountCodes.Inventory
  1900 Cash Variance           Expense  postable   AccountCodes.CashVariance
  2000 Accounts Payable        Liability postable  AccountCodes.AccountsPayable
  2010 Cheques Payable         Liability postable  AccountCodes.ChequesPayable
  2100 Customer Prepayments    Liability postable  AccountCodes.CustomerPrepayments
  2200 Tax Payable             Liability postable  AccountCodes.TaxPayable
  3000 Owner Equity            Equity   postable   AccountCodes.OwnerEquity
  4000 Service Revenue         Revenue  postable   AccountCodes.ServiceRevenue
  4010 Product Revenue         Revenue  postable   AccountCodes.ProductRevenue
  4020 Boarding Revenue        Revenue  postable   AccountCodes.BoardingRevenue
  4900 Sales Discounts         Expense  postable   AccountCodes.SalesDiscounts
  5000 Cost of Goods Sold      Expense  postable   AccountCodes.Cogs
  6000 Operating Expense       Expense  postable   AccountCodes.OperatingExpense
  6100 Salary Expense          Expense  postable   AccountCodes.SalaryExpense
  6200 Inventory Loss          Expense  postable   AccountCodes.InventoryLoss
  ```
- `Finance/JournalEntry.cs` — `sealed class JournalEntry : AggregateRoot`
  (**append-only forever: no mutator, no delete, no soft-delete for business reasons**):
  `EntryNumber` (per-tenant sequential, assigned by the Application layer), `DateTimeOffset PostedUtc`,
  `DateTimeOffset EffectiveUtc`, `string PeriodId`, `JournalSource Source`,
  `string? SourceEntityType`, `string? SourceEntityId`, `string? OperationId`,
  `string Memo`, `string PostedByUserId`, `string? ReversesEntryId`,
  `IReadOnlyList<JournalLine> Lines`.
  `enum JournalSource { Sales, Payment, Refund, Purchase, Receipt, Inventory, Cashier, Cheque,
  Expense, PeriodClose, Manual, Reversal }`.
  **[INVARIANT] Balanced double entry:** `Σ Debit == Σ Credit`, at least 2 lines, every line amount
  `> 0`, all lines in one currency — validated by the factory
  `JournalEntry.Create(...)` → `Result<JournalEntry>` (`JOURNAL_UNBALANCED`, `JOURNAL_TOO_FEW_LINES`,
  `JOURNAL_LINE_AMOUNT_INVALID`, `JOURNAL_CURRENCY_MIXED`).
  **A correction is `CreateReversal(JournalEntry original, string reason, IClock)`** producing a new
  entry with debits/credits swapped and `ReversesEntryId` set. There is no edit path.
- `Finance/JournalLine.cs` — `sealed record JournalLine(string LineId, string AccountCode,
  EntrySide Side, Money Amount, string? Memo, string? CostCenter)`.
  `enum EntrySide { Debit, Credit }`.
- `Finance/PostingRules.cs` — static, the **authoritative** event → journal translation. Pure functions
  taking the event payload + an `IClock` + a `periodId`, returning `Result<JournalEntry>`:
  | Trigger | Debit | Credit |
  |---|---|---|
  | `InvoicePostedEvent` | `1100` AR (grand total) + `4900` discounts (discount total) + `1110` insurance receivable (coverage) | `4000`/`4010`/`4020` revenue split by line kind + `2200` tax |
  | `PaymentRegisteredEvent` (Cash) | `1000` cash | `1100` AR |
  | `PaymentRegisteredEvent` (Card/Transfer) | `1010` bank | `1100` AR |
  | `PaymentRegisteredEvent` (Cheque) | `1020` cheques receivable | `1100` AR |
  | `InvoiceRefundedEvent` | `1100` AR | `1000`/`1010` (by original method) |
  | `InvoiceVoidedEvent` | reversal of the posting entry | (swapped) |
  | `GoodsReceiptPostedEvent` | `1200` inventory | `2000` AP |
  | `StockMovementRecordedEvent` (`Sale`/`Consumption`) | `5000` COGS (at weighted-average cost) | `1200` inventory |
  | `StockMovementRecordedEvent` (`Loss`/`AdjustmentOut`) | `6200` inventory loss | `1200` inventory |
  | `ChequeClearedEvent` (inbound) | `1010` bank | `1020` cheques receivable |
  | `ChequeBouncedEvent` (inbound) | `1100` AR | `1020` cheques receivable |
  | `ChequeClearedEvent` (outbound) | `2010` cheques payable | `1010` bank |
  | `CashSessionClosedEvent` (variance ≠ 0) | `1900` cash variance (shortage) | `1000` cash — swapped for an overage |
  | `ExpensePostedEvent` | `6000`/`6100` (by category) | `1000`/`1010`/`2000` (by settlement) |
  Each rule is one method named `For<EventName>`. A rule never posts to a non-postable account and never
  emits an unbalanced entry (its own `Create` call guarantees this).
- `Finance/AccountingPeriod.cs` — `sealed class AccountingPeriod : AggregateRoot`:
  `PeriodCode` (e.g. `"1405-07"` — the Jalali year-month **as an opaque string key**, formatted by the
  Desktop; Domain never computes Jalali), `DateTimeOffset StartUtc`, `DateTimeOffset EndUtc`,
  `PeriodState State`, `DateTimeOffset? ClosedUtc`, `string? ClosedByUserId`,
  `string? ClosingEntryId`, `string? Note`.
  `enum PeriodState { Open, Closing, Closed, Locked }`.
  **State machine:** `Open -> Closing -> Closed -> Locked`; no reopen (a post-close correction is an
  entry in the next open period).
  **Close rule:** `Close(bool hasUnsettledCommands, bool hasOpenCashSessions, bool hasDraftInvoices,
  IClock)` returns `PERIOD_CLOSE_BLOCKED` with a detail code when any flag is true; otherwise moves to
  `Closed` and requires a `ClosingEntryId`. `static Result EnsurePostable(AccountingPeriod period)`
  returns `PERIOD_CLOSED` for `Closed`/`Locked` — **every** `JournalEntry` write path calls this.
- `Finance/Cheque.cs` — `sealed class Cheque : AggregateRoot`:
  `ChequeNumber`, `ChequeDirection Direction`, `string? BankName`, `string? BankBranch`,
  `string? AccountNumber`, `string? SayadId`, `Money Amount`, `DateTimeOffset DueUtc`,
  `DateTimeOffset IssuedUtc`, `ChequeState State`, `string? OwnerId`, `string? SupplierId`,
  `string? InvoiceId`, `string? PaymentId`, `string? JournalEntryId`,
  `string? Note`, `IReadOnlyList<ChequeEvent> History`.
  `enum ChequeDirection { Inbound, Outbound }`.
  `enum ChequeState { Registered, InPortfolio, Deposited, Cleared, Bounced, Returned, Cancelled,
  Settled }`.
  **State machine:**
  ```
  Registered  -> InPortfolio | Cancelled
  InPortfolio -> Deposited | Returned | Cancelled
  Deposited   -> Cleared | Bounced
  Bounced     -> InPortfolio | Returned | Settled
  Cleared / Returned / Cancelled / Settled -> terminal
  ```
  Every transition appends a `ChequeEvent` (append-only history) and raises the matching domain event.
  Methods: `MoveToPortfolio`, `Deposit`, `MarkCleared(IClock)`, `MarkBounced(string reason, IClock)`,
  `Return(string reason, IClock)`, `Cancel(string reason, IClock)`, `SettleInCash(IClock)`.
  Invariant: `Direction == Inbound` requires `OwnerId`; `Outbound` requires `SupplierId`
  (`CHEQUE_COUNTERPARTY_REQUIRED`).
- `Finance/ChequeEvent.cs` — `sealed record ChequeEvent(string EventId, ChequeState FromState,
  ChequeState ToState, DateTimeOffset OccurredUtc, string ActorUserId, string? Reason)`.
- `Finance/Expense.cs` — `sealed class Expense : AggregateRoot`:
  `ExpenseNumber`, `string CategoryCode`, `string Title`, `Money Amount`,
  `DateTimeOffset IncurredUtc`, `ExpenseState State`, `PaymentMethod? SettledMethod`,
  `string? SupplierId`, `string? ChequeId`, `string? ReceiptFileId`, `string? JournalEntryId`,
  `string CreatedByUserId`, `string? Note`.
  `enum ExpenseState { Draft, Posted, Reversed }`. `Post` is one-way and raises `ExpensePostedEvent`;
  a correction is a reversing expense (`ReversesExpenseId`).
- `Finance/ExpenseCategories.cs` — static seed: `RENT`, `UTILITIES`, `SALARY`, `SUPPLIES`,
  `MAINTENANCE`, `MARKETING`, `TAX`, `TRANSPORT`, `OTHER`, each mapped to its ledger account code
  (`SALARY` → `6100`, everything else → `6000`).
- **Domain events** in `src/Animora.Domain/Events/FinanceEvents.cs`:
  `JournalEntryPostedEvent(EntryId, PeriodId, Source, TotalDebitMinor, Currency)`,
  `ChequeStateChangedEvent(ChequeId, FromState, ToState)`, `ChequeClearedEvent(ChequeId, Direction,
  AmountMinor, Currency)`, `ChequeBouncedEvent(ChequeId, Direction, AmountMinor, Currency, Reason)`,
  `ExpensePostedEvent(ExpenseId, CategoryCode, AmountMinor, Currency, SettledMethod)`,
  `PeriodClosedEvent(PeriodId, ClosingEntryId)`.
- **New error codes:** `JOURNAL_UNBALANCED`, `JOURNAL_TOO_FEW_LINES`, `JOURNAL_LINE_AMOUNT_INVALID`,
  `JOURNAL_CURRENCY_MIXED`, `ACCOUNT_NOT_POSTABLE`, `ACCOUNT_UNKNOWN`, `PERIOD_CLOSED`,
  `PERIOD_CLOSE_BLOCKED`, `PERIOD_INVALID_TRANSITION`, `CHEQUE_INVALID_TRANSITION`,
  `CHEQUE_COUNTERPARTY_REQUIRED`, `EXPENSE_IMMUTABLE`.

## Files to create / modify
| Path | Action | Contents (one line) |
|---|---|---|
| `src/Animora.Domain/Finance/LedgerAccount.cs` | create | account entity + normal-balance derivation |
| `src/Animora.Domain/Finance/ChartOfAccounts.cs` | create | the 20 seeded accounts + `AccountCodes` constants |
| `src/Animora.Domain/Finance/JournalEntry.cs` | create | append-only balanced entry + reversal factory |
| `src/Animora.Domain/Finance/JournalLine.cs` | create | debit/credit line value object |
| `src/Animora.Domain/Finance/PostingRules.cs` | create | the event → journal translation table |
| `src/Animora.Domain/Finance/AccountingPeriod.cs` | create | period entity + close rule + `EnsurePostable` |
| `src/Animora.Domain/Finance/Cheque.cs` | create | cheque aggregate + state machine + history |
| `src/Animora.Domain/Finance/ChequeEvent.cs` | create | append-only cheque history record |
| `src/Animora.Domain/Finance/Expense.cs` | create | expense aggregate + one-way post |
| `src/Animora.Domain/Finance/ExpenseCategories.cs` | create | category → account mapping |
| `src/Animora.Domain/Events/FinanceEvents.cs` | create | 6 finance events |
| `src/Animora.Domain/Results/ErrorCodes.cs` | modify | append the 12 codes |
| `tests/Animora.Domain.Tests/JournalEntryTests.cs` | create | balance/line/currency validation + reversal symmetry |
| `tests/Animora.Domain.Tests/PostingRulesTests.cs` | create | one balanced-entry assertion per rule in the table |
| `tests/Animora.Domain.Tests/ChequeLifecycleTests.cs` | create | all transitions + history append + counterparty guard |
| `tests/Animora.Domain.Tests/AccountingPeriodTests.cs` | create | close blockers, `EnsurePostable`, no-reopen |

## Implementation steps
1. Create `Finance/` and add `LedgerAccount`, `AccountType`, then `ChartOfAccounts` with an
   `AccountCodes` static class of `const string` fields for all 20 codes.
2. Add `JournalLine`/`EntrySide`, then `JournalEntry` with the validating `Create` factory,
   `CreateReversal`, and **zero** mutators.
3. Add `AccountingPeriod` with its transition helper, `Close` blockers and `EnsurePostable`.
4. Implement `PostingRules` — one `For<EventName>` method per table row, each building its lines and
   delegating to `JournalEntry.Create` so balance is structurally guaranteed.
5. Add `ChequeEvent`, then `Cheque` with the state machine, history append and counterparty invariant.
6. Add `ExpenseCategories`, then `Expense` with the one-way `Post`.
7. Add `Events/FinanceEvents.cs` and append the 12 error codes.
8. Write the four test classes. `PostingRulesTests` must assert, for **every** rule in the table, that
   the produced entry is balanced, uses only postable accounts from `ChartOfAccounts`, and carries the
   source entity + `OperationId`. `ChequeLifecycleTests` enumerates all `ChequeState` pairs.
9. `build.sh full` = 0/0; `test.sh` = `TESTS OK`; `run.sh` = `SMOKE RUN OK`.

## Contracts / schema added
- Entities: `LedgerAccount`, `JournalEntry`, `AccountingPeriod`, `Cheque`, `Expense`.
- Value objects: `JournalLine`, `ChequeEvent`.
- Enums: `AccountType`, `EntrySide`, `JournalSource`, `PeriodState`, `ChequeDirection`, `ChequeState`,
  `ExpenseState`.
- Statics: `ChartOfAccounts` + `AccountCodes`, `PostingRules`, `ExpenseCategories`.
- Events: 6 new records. `ErrorCodes` += 12. No migration, no `Animora.Contracts` change.

## Rules that apply here
- **A-RULE 002 / R-B22** — `JournalEntry` is append-only with no mutator; corrections are reversals.
- **ARCHITECTURE.md §8.5** — ledger conflicts cannot occur by construction (append-only); the conflict
  policy for a ledger row is "append-only forever".
- **A-RULE 003** — a financial correction always carries a reason and an audit trail
  (`ReversesEntryId` + `Memo`), never a silent overwrite.
- **CONVENTIONS §4** — every amount is `Money`; percentages never appear in the ledger (they are
  resolved upstream by `InvoiceTotals`).
- **CONVENTIONS §2** — `Animora.Domain` gains no reference; Jalali period codes are opaque strings.
- **PERFORMANCE.md §2** — `JournalEntry.Create` validates with a single `for` pass over the lines.

## Out of scope
- Persisting journal entries and the read-model projections → A10, C09.
- Dispatching domain events into `PostingRules` → A10 (local) and D07/D09 (server).
- Period-close command + UI → C09. Cheque UI wiring → C08. Expense UI wiring → C09.
- Multi-currency ledgers — out of scope for the product; `JOURNAL_CURRENCY_MIXED` rejects mixing.
- Financial reports/aggregates → C09.

## Acceptance criteria
- Build: `scripts/sandbox/build.sh full` = 0 warnings / 0 errors.
- `JournalEntryTests` green: an unbalanced entry returns `JOURNAL_UNBALANCED`; a 1-line entry returns
  `JOURNAL_TOO_FEW_LINES`; `CreateReversal` produces a mirrored, balanced entry whose combined effect
  with the original nets to zero per account.
- `PostingRulesTests` green for all 14 table rows; each entry is balanced and uses postable accounts.
- `ChequeLifecycleTests` covers all `ChequeState` pairs; only the tabled transitions succeed; each
  successful transition appends exactly one `ChequeEvent`.
- `AccountingPeriodTests` green: each of the three close blockers returns `PERIOD_CLOSE_BLOCKED`;
  `EnsurePostable` rejects `Closed` and `Locked`; no transition back to `Open` exists.
- `JournalEntry` exposes no public setter (reflection assertion).
- Runnability (`CONVENTIONS.md` §11): `scripts/sandbox/run.sh` = `SMOKE RUN OK`.

## PROGRESS.md update
Mark A08 done; advance "Next phase" to `A09`; append a "Repo state" entry listing: the 11 new files, the
chart-of-accounts codes, the posting-rule table, the cheque/period state machines, `ErrorCodes` += 12,
the 4 test classes, no DI change, no mock deleted, no migration, deviations.
