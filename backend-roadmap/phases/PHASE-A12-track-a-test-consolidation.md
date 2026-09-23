# Phase A12 — Track A test consolidation (write + build + test)

**Band:** M · **Track:** A · **Depends on:** A11 · **Satisfies:** R-B01, R-B02, R-B24 (verification)
**Runnable after this phase:** desktop offline yes · server required no

> **What makes this phase different.** Every Track A phase (A01–A11) shipped behavior with **no tests**.
> This phase is the *single* place Track A tests are authored and the *first* place `scripts/sandbox/
> test.sh` is run for real. It writes no production code and changes no domain/persistence behavior — if
> a test reveals a defect, the fix is a **minimal, surgical correction** to the offending A0x/A1x code,
> recorded in the decision log. Its whole job is: author the owed tests, get a green build, get a green
> test run, fix what the tests catch.
>
> **Token/scope discipline.** The exact assertions are already specified — each source phase file ends
> with a **"Test coverage owed to A12"** section. This phase does **not** restate them; it points at
> them. Read those sections, translate each bullet into xUnit facts, done. Do not invent coverage beyond
> what those sections and `CONVENTIONS.md §9` require, and do not gold-plate.

## Read before starting
- `backend-roadmap/CONVENTIONS.md` §1 (test project layout), §4 (C# style), §9 (**testing policy —
  the authority for what each project owns**), §12 (Definition of Done)
- `backend-roadmap/PROGRESS.md` (state + every A0x/A1x "Repo state" entry — the inventory of what to test)
- The **"Test coverage owed to A12"** section of **each** of `PHASE-A05` … `PHASE-A11`
  (`SlotConflictPolicy`, stock/movement, invoice/payment/cash, ledger/cheque/period, migrations,
  converters, query filters, unit of work, derived queries, seeding, startup, backup/quarantine, clock).
- `src/Animora.Domain/**`, `src/Animora.Persistence.Sqlite/**` — the code under test (read only what a
  given test file needs; do not browse exhaustively).
- The existing test projects created in A01: `tests/Animora.Domain.Tests/**`,
  `tests/Animora.Persistence.Tests/**` (A01–A04 tests already live here — extend, never duplicate).

## Goal
Bring Track A to the state `CONVENTIONS.md §9` requires: every domain invariant, state machine,
money/quantity/ledger/movement calculation, EF mapping/converter/filter/migration, unit-of-work,
derived query, seeding and store-hardening behavior introduced in A05–A11 has a green xUnit test; the
whole solution builds 0/0; `scripts/sandbox/test.sh` prints `TESTS OK`; any defect a test uncovers is
fixed minimally.

## Design decisions (pre-made — implement, do not re-derive)
- **No new production files.** This phase adds only files under `tests/`. The one exception is a
  bug-fix: if a test proves an A0x/A1x defect, edit that phase's source file(s) minimally, keeping the
  behavior the source phase specified. Never edit a committed migration; if a mapping is genuinely
  wrong, that is a defect to escalate in the decision log, not to silently re-migrate.
- **Project ownership is fixed by `CONVENTIONS.md §9`** — put each test in the project that owns its
  subject:
  | Subject introduced in | Test project |
  |---|---|
  | A05–A08 domain (scheduling, boarding, inventory, purchasing, sales, cashier, finance) | `Animora.Domain.Tests` |
  | A09 mappings/converters/filters/migration `001` | `Animora.Persistence.Tests` |
  | A10 business mappings, `AppendOnlyGuard`, UoW, dispatcher, derived queries, seeding, migration `002` | `Animora.Persistence.Tests` |
  | A11 native assertion, startup, backup/restore, quarantine, monotonic clock, migration `003` | `Animora.Persistence.Tests` |
- **Test classes are exactly the ones named** in the source phases' "Test coverage owed to A12"
  sections (`SlotConflictPolicyTests`, `AppointmentStateMachineTests`, `BoardingStayTests`,
  `ReceptionQueueTests`, `StockCalculatorTests`, `StockMovementFactoryTests`, `ProductInvariantTests`,
  `PurchaseOrderTests`, `InvoiceStateMachineTests`, `InvoiceImmutabilityTests`, `InvoiceTotalsTests`,
  `PaymentTests`, `CashSessionTests`, `JournalEntryTests`, `PostingRulesTests`, `ChequeLifecycleTests`,
  `AccountingPeriodTests`, `LocalStoreTestFixture`, `LocalMigrationTests`, `ConverterTests`,
  `QueryFilterTests`, `BusinessMappingTests`, `AppendOnlyGuardTests`, `UnitOfWorkTests`,
  `DerivedQueryTests`, `SeedingTests`, `NativeSqliteVersionTests`, `StoreStartupTests`,
  `BackupRestoreTests`, `QuarantineRebuildTests`, `MonotonicClockTests`). Same names, same file paths as
  the source phases listed — so the roadmap's naming stays traceable.
- **Persistence tests use temp *file* DBs**, never `:memory:` (CONVENTIONS §1). `LocalStoreTestFixture`
  (owed by A09) is the shared fixture; write it first — every other persistence test class consumes it.
- **`IClock` is injected**, never wall-clock; no test asserts on a Persian UI string; every derived
  query test asserts one `SELECT` via `ToQueryString()` where the source phase requires it
  (CONVENTIONS §9, A10's owed coverage).
- **A01–A04 tests already exist and stay green** — do not rewrite them. This phase only *runs* them as
  part of the full suite and adds the A05–A11 coverage on top.
- **`test.sh` and the test projects are unchanged in shape** — A01 already created them; this phase only
  adds `.cs` files. No new package pin, no csproj change beyond files being picked up by the glob.

## Files to create / modify
| Path | Action | Contents (one line) |
|---|---|---|
| `tests/Animora.Domain.Tests/SlotConflictPolicyTests.cs` | create | A05 slot-conflict coverage |
| `tests/Animora.Domain.Tests/AppointmentStateMachineTests.cs` | create | A05 appointment transitions + move/duration |
| `tests/Animora.Domain.Tests/BoardingStayTests.cs` | create | A05 stay transitions, billable days, invoice guard |
| `tests/Animora.Domain.Tests/ReceptionQueueTests.cs` | create | A05 queue transitions + priority |
| `tests/Animora.Domain.Tests/StockCalculatorTests.cs` | create | A06 on-hand/avg-cost/sufficiency/expiry |
| `tests/Animora.Domain.Tests/StockMovementFactoryTests.cs` | create | A06 all 10 kinds' sign + reversal |
| `tests/Animora.Domain.Tests/ProductInvariantTests.cs` | create | A06 service-no-stock, variant, prices |
| `tests/Animora.Domain.Tests/PurchaseOrderTests.cs` | create | A06 PO transitions/lock/total/partial receipt |
| `tests/Animora.Domain.Tests/InvoiceStateMachineTests.cs` | create | A07 invoice transitions + void-after-payment |
| `tests/Animora.Domain.Tests/InvoiceImmutabilityTests.cs` | create | A07 all mutators locked after Post |
| `tests/Animora.Domain.Tests/InvoiceTotalsTests.cs` | create | A07 discount/tax/insurance allocation |
| `tests/Animora.Domain.Tests/PaymentTests.cs` | create | A07 over-pay, duplicate op, reversal, references |
| `tests/Animora.Domain.Tests/CashSessionTests.cs` | create | A07 variance/note/unsettled/closed |
| `tests/Animora.Domain.Tests/JournalEntryTests.cs` | create | A08 balance/line/currency + reversal symmetry |
| `tests/Animora.Domain.Tests/PostingRulesTests.cs` | create | A08 one balanced entry per rule (all 14) |
| `tests/Animora.Domain.Tests/ChequeLifecycleTests.cs` | create | A08 all cheque transitions + history + counterparty |
| `tests/Animora.Domain.Tests/AccountingPeriodTests.cs` | create | A08 close blockers, EnsurePostable, no-reopen |
| `tests/Animora.Persistence.Tests/LocalStoreTestFixture.cs` | create | A09 shared temp-file DB fixture |
| `tests/Animora.Persistence.Tests/LocalMigrationTests.cs` | create | A09 migration `001` tables/indexes/no REAL |
| `tests/Animora.Persistence.Tests/ConverterTests.cs` | create | A09 UTC ticks / Money / enum round-trips |
| `tests/Animora.Persistence.Tests/QueryFilterTests.cs` | create | A09 named filters + selective ignore + tenant guard |
| `tests/Animora.Persistence.Tests/BusinessMappingTests.cs` | create | A10 tables/indexes, owned-as-rows |
| `tests/Animora.Persistence.Tests/AppendOnlyGuardTests.cs` | create | A10 protected-type update/delete throws |
| `tests/Animora.Persistence.Tests/UnitOfWorkTests.cs` | create | A10 rollback, dispatch-once, concurrency → Result |
| `tests/Animora.Persistence.Tests/DerivedQueryTests.cs` | create | A10 on-hand/balance/ledger + one-SELECT |
| `tests/Animora.Persistence.Tests/SeedingTests.cs` | create | A10 idempotent + complete seeding |
| `tests/Animora.Persistence.Tests/NativeSqliteVersionTests.cs` | create | A11 version ≥ 3.50.2 + stub-too-old |
| `tests/Animora.Persistence.Tests/StoreStartupTests.cs` | create | A11 8-step order, fail-closed key, pragmas |
| `tests/Animora.Persistence.Tests/BackupRestoreTests.cs` | create | A11 backup→restore intact + retention |
| `tests/Animora.Persistence.Tests/QuarantineRebuildTests.cs` | create | A11 corrupt quarantined, fresh DB, nothing deleted |
| `tests/Animora.Persistence.Tests/MonotonicClockTests.cs` | create | A11 high-water never decreases across restart |
| *(only if a test finds a defect)* an A05–A11 source file | modify | minimal, surgical bug fix; logged |

## Implementation steps
1. **Persistence fixture first.** Write `LocalStoreTestFixture` (A09's owed item): temp directory,
   unique file name, `MigrateAsync`, dispose deletes the DB and its `-wal`/`-shm`. Every persistence
   class below uses it.
2. **Author domain tests A05→A08** in `Animora.Domain.Tests`, one class per row above, translating each
   bullet in the source phase's "Test coverage owed to A12" section into `[Fact]`/`[Theory]` cases. Use
   `IdGenerator.NewId(at)` and an injected fake `IClock` for time; never `DateTime.Now`.
3. **Author persistence tests A09→A11** in `Animora.Persistence.Tests`, one class per row, over the
   fixture. For migrations assert via `PRAGMA index_list`/`table_info`; for derived queries assert one
   `SELECT` via `ToQueryString()`; for startup record and assert the 8-step order.
4. **Build:** `scripts/sandbox/build.sh full` = 0 warnings / 0 errors.
5. **Run the suite for the first time:** `scripts/sandbox/test.sh` → must print `TESTS OK`.
6. **Fix what the tests catch.** For each red test, decide: test bug (fix the test) or code defect (fix
   the A0x/A1x source minimally, never edit a committed migration, log the fix). Re-run to green.
7. **Smoke run:** `scripts/sandbox/run.sh` = `SMOKE RUN OK` (desktop still on mocks, untouched).

## Contracts / schema added
- **None.** No production type, no entity, no port, no migration, no `Animora.Contracts` change, no DI
  change in `App.axaml.cs`. This phase adds test files only (plus any logged minimal bug-fix).
- Test surface: the ~31 test classes above, in the two existing test projects, all green.

## Rules that apply here
- **CONVENTIONS §9** — this phase realizes the testing policy for Track A; each test lives in the
  project that section assigns, and the mandatory targets (domain invariants, idempotency/ordering/
  conflict paths, append-only guards, named filters, concurrency) are all covered.
- **CONVENTIONS §1** — persistence tests run against temp **file** DBs, never `:memory:`.
- **CONVENTIONS §4** — `IClock` injected; no wall-clock; nullable honored; no Persian-string assertions.
- **CONVENTIONS §5** — a committed migration is never edited; a mapping defect is escalated, not
  silently re-migrated.
- **ROADMAP §3.10** — Track A wrote no tests during A01–A11 by design; A12 is the mandated consolidation
  point and the only Track A phase that runs `test.sh`.
- **PERFORMANCE.md §3** — derived-query tests confirm one SQL statement; no test forces runtime
  assembly scanning.

## Out of scope
- Any new domain/persistence behavior — belongs to the phase that owns it (already shipped in A01–A11).
- Application-layer / command-pipeline / login / authorization tests → their Track B phases.
- Mock retirement, `Animora.Contracts` implementations, DI swaps → Track C.
- Server, sync, licensing tests → Tracks D/E.
- Rewriting A01–A04's existing tests — they stay as-is and simply run in the suite.

## Acceptance criteria
- Build: `scripts/sandbox/build.sh full` = **0 warnings / 0 errors**.
- `scripts/sandbox/test.sh` prints `TESTS OK`; every test class named above exists and is green, and the
  pre-existing A01–A04 tests remain green in the same run.
- Every bullet in each A05–A11 "Test coverage owed to A12" section maps to at least one green fact
  (traceability: the class names match one-to-one).
- No production code changed except minimal, logged bug-fixes; `git diff --stat` outside `tests/` is
  empty or contains only such fixes; no committed migration edited.
- Runnability (`CONVENTIONS.md` §11): `scripts/sandbox/run.sh` = `SMOKE RUN OK`; the desktop still runs
  entirely on mocks.

## PROGRESS.md update
Mark A12 done; advance "Next phase" to `B01`; append a "Repo state" entry listing: the ~31 test classes
added (grouped Domain vs Persistence), the total test count now green, `LocalStoreTestFixture` added,
any A0x/A1x bug-fix made (with the defect and the minimal change), no production type/migration/DI/mock
change otherwise, deviations. This is the first phase whose entry records `test.sh = TESTS OK` for the
full Track A suite.
