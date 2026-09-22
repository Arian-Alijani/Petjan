# Phase C08 — Cashier & Cheques

**Band:** M · **Track:** C · **Depends on:** C07 · **Satisfies:** R-B01, R-B02, R-B16
**Runnable after this phase:** desktop offline yes · server required no

## Read before starting
- `backend-roadmap/CONVENTIONS.md` §3, §4, §5, §6, §8, §9, §10, §11, §12
- `backend-roadmap/PROGRESS.md` (the **C01 pattern**; C05 stale-index rule; C07 posting/compensation
  design)
- `ARCHITECTURE.md` §11.1 · `PERFORMANCE.md` §5, §6
- `src/Animora.Contracts/Cashier/ICashierService.cs`, `src/Animora.Contracts/Cashier/CashierModels.cs`
- `src/Animora.Contracts/Cheques/IChequesQueryService.cs`,
  `src/Animora.Contracts/Cheques/ChequeModels.cs`
- `src/Animora.Domain/Sales/CashSession.cs`, `src/Animora.Domain/Sales/CashMovement.cs`,
  `src/Animora.Domain/Finance/Cheque.cs`, `src/Animora.Domain/Finance/ChequeEvent.cs`
- `src/Animora.Application/UseCases/Sales/*` (C07 — reused, not duplicated)
- `src/Animora.Application/Ports/ICashSessionRepository.cs`,
  `src/Animora.Application/Ports/IChequeRepository.cs`
- `src/Animora.Desktop/App.axaml.cs` (lines registering `ICashierService`, `IChequesQueryService`)

## Goal
The cashier screen manages a real **cash session** (open/close with a counted drawer and a variance
record) and settles real invoices through the C07 commands; the cheques screen runs the full cheque
lifecycle with bank breakdown, cashflow projection and alerts, all feeding the ledger in C09.

## Design decisions (pre-made — implement, do not re-derive)
- **No duplicated sales logic.** `CashierService` **reuses C07's commands verbatim**
  (`RegisterPaymentCommand`, `PostInvoiceCommand`, `VoidInvoiceCommand`, `RefundInvoiceCommand`,
  `EditInvoiceLineCommand`, `RemoveInvoiceLineCommand`, `AddInvoiceLineCommand`,
  `SetInvoiceDiscountCommand`, `SetInvoiceInsuranceCommand`). This phase adds **no** second invoice
  code path; `CashierMapping` maps the same read models onto the `Cashier*` DTO family. A test
  asserts `Animora.Client.Services/Cashier/**` contains no `IUnitOfWork` or repository usage.
- **`RecalculateAsync`** re-runs `InvoiceDetailQuery` (totals are always computed by
  `InvoiceTotals`, never stored on the DTO's behalf) — it dispatches no command.
- **Cash session** (`UseCases/Cashier/`):
  - `OpenCashSessionCommand(Money OpeningFloat)` — refuses a second open session per
    `(TenantId, UserId)` (`CASH_SESSION_ALREADY_OPEN`).
  - `CloseCashSessionCommand(Money CountedCash, string? Note)` — computes
    `ExpectedCash = OpeningFloat + Σ(cash payments in session) − Σ(cash refunds/payouts)` via
    `CashSessionQuery`, records `VarianceMinor = Counted − Expected` on the session (**never
    silently corrected**), transitions to `CashSessionState.Closed`, and raises
    `CashSessionClosedEvent`. A variance beyond `CashVarianceThresholds.RequiresNote` (5 000 000
    minor units, `Animora.Domain/Sales/CashVarianceThresholds.cs`) without a note returns
    `CASH_VARIANCE_NOTE_REQUIRED`.
  - `RecordCashMovementCommand(CashMovementKind Kind, Money Amount, string Reason)` for payouts /
    top-ups (`PayIn`, `PayOut`), append-only.
  - Every payment registered while a session is open is stamped with `CashSessionId` in
    `RegisterPaymentCommand` through a new `ICashSessionContext` port
    (`Animora.Application/Ports/ICashSessionContext.cs`, resolved from the current user's open
    session; `null` when none — a cash payment with no open session returns
    `CASH_SESSION_REQUIRED`, non-cash methods are unaffected). **This is the only C07 change in this
    phase** and is additive.
  - `CommandTypes` += `cashier.session.open`, `cashier.session.close`, `cashier.movement.record` (3).
    Permission `cashier.session.manage`.
- **Cashier queries** (`UseCases/Cashier/`): `CashierInvoiceListQuery(CashierTab Tab)`,
  `CashierStatsQuery()` (today's collected / outstanding / refunded / session variance — one grouped
  statement), `CashierTabCountsQuery()` (one grouped `COUNT` statement, never one per tab),
  `OpenCashSessionQuery()`.
- **Cheque lifecycle** is the domain `Cheque` state machine from A08; the Contracts `ChequeStatus`
  maps 1-to-1 in `ChequesMapping`. Transitions are commands, each appending a `ChequeEvent`
  (append-only): `CreateChequeCommand`, `UpdateChequeCommand` (metadata only, refused once the
  cheque left `ChequeState.Registered` → `CHEQUE_NOT_EDITABLE`), `DeleteChequeCommand` (soft delete,
  only from `Registered`), `ChangeChequeStatusCommand(ChequeId, ChequeState Target)` validated
  against the domain transition table, and `BulkChangeChequeStatusCommand(IReadOnlyList<string> Ids,
  ChequeState Target)` which executes the per-cheque command in **one** unit of work and returns a
  per-id result list (partial success is reported, never silently swallowed).
  `CommandTypes` += `finance.cheque.create`, `finance.cheque.update`, `finance.cheque.delete`,
  `finance.cheque.status` (4). Permission `finance.cheque.manage`.
- **Cheque → ledger effect (the C09 seam):** clearing an incoming cheque (`ChequeState.Cleared`) and
  bouncing one (`ChequeState.Bounced`) raise `ChequeClearedEvent` / `ChequeBouncedEvent` (A08). C09's
  `PostingRules` turn them into journal entries. This phase raises the events and writes **no**
  journal entry itself — the ledger has exactly one writer (C09).
- **Cheque read models:** `ChequeCashflowQuery(int Weeks = 8)` projects `CashflowWeek` rows by
  bucketing `DueUtc` into clinic-local ISO weeks via `ClinicCalendar` (C04) — one statement, bucketed
  in memory over at most 8×2 groups. `ChequeBankBreakdownQuery()` is one `GROUP BY BankKey`.
  `ChequeAlertsQuery()` produces `ChequeAlert` rows from three rules in
  `Animora.Domain/Finance/ChequeAlertRules.cs`: `DueWithin3Days`, `OverdueUncleared`,
  `BouncedUnresolved` — thresholds are constants, not magic numbers in SQL.
- **Banks** are reference data (`Animora.Domain/Finance/BankDirectory.cs`, 18 Iranian banks with
  `Key`, `Name`) seeded by `LocalSeeder`; `GetBanksAsync` reads them, no DB round-trip per call
  (static list, `IReadOnlyList<ChequeBank>` built once).
- **Sample data:** `Seed/FinanceSampleData.cs` — 24 cheques (both directions, all states, spread
  across the next 8 weeks and the last 4), plus 2 closed cash sessions and 1 open one.
- **ViewModel changes (exhaustive):** `CashierPageViewModel` and `ChequesPageViewModel` each gain the
  readiness-gate line.
- **New error codes:** `CASH_SESSION_ALREADY_OPEN`, `CASH_SESSION_REQUIRED`,
  `CASH_SESSION_NOT_OPEN`, `CASH_VARIANCE_NOTE_REQUIRED`, `CHEQUE_NOT_FOUND`,
  `CHEQUE_NOT_EDITABLE`, `CHEQUE_TRANSITION_INVALID`.

## Files to create / modify
| Path | Action | Contents (one line) |
|---|---|---|
| `src/Animora.Domain/Sales/CashVarianceThresholds.cs` | create | variance constants |
| `src/Animora.Domain/Finance/ChequeAlertRules.cs` | create | 3 alert rules + thresholds |
| `src/Animora.Domain/Finance/BankDirectory.cs` | create | 18-bank reference list |
| `src/Animora.Application/Ports/ICashSessionContext.cs` | create | current open-session resolution |
| `src/Animora.Application/UseCases/Cashier/*.cs` | create | 4 queries + 3 commands + handlers/validators |
| `src/Animora.Application/UseCases/Finance/Cheques/*.cs` | create | 4 queries + 5 commands + handlers/validators |
| `src/Animora.Application/UseCases/Sales/RegisterPaymentCommand.cs` | modify | stamp `CashSessionId`, enforce `CASH_SESSION_REQUIRED` |
| `src/Animora.Persistence.Sqlite/Cashier/CashSessionContext.cs` | create | port implementation |
| `src/Animora.Persistence.Sqlite/Queries/CashierQueries.cs` | create | list/stats/tab-count/session projections |
| `src/Animora.Persistence.Sqlite/Queries/ChequeQueries.cs` | create | list/cashflow/breakdown/alerts projections |
| `src/Animora.Persistence.Sqlite/Configurations/Sales/PaymentConfiguration.cs` | modify | `CashSessionId` + index |
| `src/Animora.Persistence.Sqlite/Migrations/*` | create | `014_CashSessionLink` + snapshot |
| `src/Animora.Persistence.Sqlite/Seed/LocalSeeder.cs` | modify | seed bank directory |
| `src/Animora.Persistence.Sqlite/Seed/FinanceSampleData.cs` | create | cheques + cash sessions |
| `src/Animora.Client.Services/Cashier/CashierService.cs` | create | real `ICashierService` (15 members, reuses C07) |
| `src/Animora.Client.Services/Cashier/CashierMapping.cs` | create | sales read models ↔ `Cashier*` DTOs |
| `src/Animora.Client.Services/Cheques/ChequesQueryService.cs` | create | real `IChequesQueryService` |
| `src/Animora.Client.Services/Cheques/ChequesMapping.cs` | create | read models ↔ cheque DTOs |
| `src/Animora.Contracts.Wire/Commands/CommandTypes.cs` | modify | add 7 command types |
| `src/Animora.Desktop/App.axaml.cs` | modify | swap 2 DI lines |
| `src/Animora.Desktop/Pages/Cashier/CashierPageViewModel.cs` | modify | readiness-gate line |
| `src/Animora.Desktop/Pages/Cheques/ChequesPageViewModel.cs` | modify | readiness-gate line |
| `src/Animora.Desktop/Mocks/MockCashierService.cs` | delete | replaced |
| `src/Animora.Desktop/Mocks/MockChequesQueryService.cs` | delete | replaced |
| `src/Animora.Domain/Results/ErrorCodes.cs` | modify | append 7 codes |
| `tests/Animora.Application.Tests/CashSessionUseCaseTests.cs` | create | open/close, variance, payment stamping |
| `tests/Animora.Application.Tests/ChequeLifecycleTests.cs` | create | transitions, bulk partial success, events |
| `tests/Animora.Application.Tests/CashierReusesSalesTests.cs` | create | asserts no duplicate invoice path |

## Implementation steps
1. Add the three domain statics and the 7 error codes.
2. Add `ICashSessionContext` + `CashSessionContext`, the `Payment.CashSessionId` column and
   migration `014_CashSessionLink`; modify `RegisterPaymentCommand` to stamp it and enforce
   `CASH_SESSION_REQUIRED` for cash payments only.
3. Write `CashierQueries` (one grouped statement for stats, one for tab counts) and the 4 cashier
   queries.
4. Write the 3 cash-session commands with the variance rule.
5. Write `ChequeQueries` and the 4 cheque queries (cashflow buckets via `ClinicCalendar`).
6. Write the 5 cheque commands, appending a `ChequeEvent` per transition and raising the cleared /
   bounced events; implement bulk update as one unit of work with a per-id result list.
7. Seed the bank directory; add `FinanceSampleData` behind `SeedSampleData`.
8. Write `CashierMapping`/`CashierService` (delegating to C07 commands only) and
   `ChequesMapping`/`ChequesQueryService`.
9. Swap the two DI lines, add the two ViewModel lines, delete both mocks.
10. Write the three test classes.
11. Verify manually: open a session, take cash payments, close with a counted amount and see the
    variance; tab counts and stats match; create a cheque, walk it through its lifecycle, bulk-change
    three at once with one invalid id and see the partial result; alerts and the 8-week cashflow chart
    match the seeded data.
12. `build.sh full` = 0/0; `test.sh` = `TESTS OK`; `run.sh` = `SMOKE RUN OK`.

## Contracts / schema added
- Domain: `CashVarianceThresholds`, `ChequeAlertRules`, `BankDirectory`.
- Application: `ICashSessionContext`, 8 queries, 8 commands, their read models;
  `RegisterPaymentCommand` gains session stamping.
- Persistence: `CashSessionContext`, `CashierQueries`, `ChequeQueries`, `Payment.CashSessionId` +
  index, migration `014_CashSessionLink`, `FinanceSampleData`, seeded banks.
- Client services: `CashierService`, `CashierMapping`, `ChequesQueryService`, `ChequesMapping`.
- Wire: 7 `CommandTypes` entries. `ErrorCodes` += 7. No `Animora.Contracts` change.
- DI: `ICashierService` → `CashierService`; `IChequesQueryService` → `ChequesQueryService`.
  **Mocks deleted: `MockCashierService.cs`, `MockChequesQueryService.cs`.**

## Rules that apply here
- **A-RULE 002** — `Payment`, `CashMovement` and `ChequeEvent` are append-only; a cash variance is
  recorded, never corrected away.
- **A-RULE 005 / 006** — session and cheque transitions are journaled commands; a bulk update issues
  one `OperationId` per cheque so a partial retry cannot double-apply.
- **A-RULE 024 / R-B16** — the bulk result reports per-id outcomes; no silent swallow.
- **CONVENTIONS §5 / PERFORMANCE.md §6** — stats, tab counts and bank breakdown are single grouped
  statements; the invoice list is paged.
- **CONVENTIONS §2** — the cashier context adds no persistence code of its own; it composes C07.
- **R-B24** — cashflow weeks are bucketed through `ClinicCalendar`, never a SQL date function.

## Out of scope
- Journal entries from cheque and cash-session events → C09 (single ledger writer).
- Invoice creation/editing semantics → C07 (reused here, not redefined).
- Bank integration or Sayad validation against any external service → never (A-RULE 016).
- Server-side settlement of these commands → D07.

## Acceptance criteria
- Build: `scripts/sandbox/build.sh full` = 0 warnings / 0 errors.
- `MockCashierService.cs` and `MockChequesQueryService.cs` do not exist; no DI line references them.
- `CashSessionUseCaseTests` green: a second open returns `CASH_SESSION_ALREADY_OPEN`; a cash payment
  with no open session returns `CASH_SESSION_REQUIRED` while a card payment succeeds; closing with a
  counted amount records the exact variance; a large variance without a note returns
  `CASH_VARIANCE_NOTE_REQUIRED`.
- `ChequeLifecycleTests` green: every allowed transition succeeds and appends exactly one
  `ChequeEvent`; a disallowed one returns `CHEQUE_TRANSITION_INVALID`; editing a cleared cheque
  returns `CHEQUE_NOT_EDITABLE`; a bulk update of 3 ids where 1 is invalid applies 2 and reports 1
  failure; clearing raises exactly one `ChequeClearedEvent`.
- `CashierReusesSalesTests` green: no type under `Animora.Client.Services/Cashier` references
  `IUnitOfWork`, a repository port, or `AnimoraLocalDbContext`.
- Manual: cashier and cheques screens work offline and survive restart.
- Runnability (`CONVENTIONS.md` §11): `scripts/sandbox/run.sh` = `SMOKE RUN OK`.

## PROGRESS.md update
Mark C08 done; advance "Next phase" to `C09`; append a "Repo state" entry listing: the
"cashier reuses C07, no second invoice path" rule, `ICashSessionContext` + `Payment.CashSessionId` +
migration `014_CashSessionLink`, the variance rule and thresholds, the cheque lifecycle commands and
the **events C09 must consume** (`ChequeClearedEvent`, `ChequeBouncedEvent`, `CashSessionClosedEvent`),
`BankDirectory`, `ChequeAlertRules`, the 16 use cases, the 4 client services/mappings, the 2 DI
swaps, **mocks deleted: `MockCashierService.cs`, `MockChequesQueryService.cs`**, `CommandTypes` += 7,
`ErrorCodes` += 7, the 3 test classes, deviations.
