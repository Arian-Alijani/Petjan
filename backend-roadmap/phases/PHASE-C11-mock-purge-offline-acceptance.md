# Phase C11 — Mock purge & offline acceptance

**Band:** M · **Track:** C · **Depends on:** C04, C10 · **Satisfies:** R-B01, R-B08, R-B10, R-B34
**Runnable after this phase:** desktop offline yes · server required no

## Read before starting
- `backend-roadmap/CONVENTIONS.md` §2, §8, §9, §10, §11, §12
- `backend-roadmap/PROGRESS.md` (every Track C "Repo state" entry — this phase audits them)
- `backend-roadmap/00-REQUIREMENTS-LOCK.md` (R-B34 definition)
- `PERFORMANCE.md` §3, §4, §6, §12, §15
- `src/Animora.Desktop/App.axaml.cs` (the whole DI section)
- `src/Animora.Desktop/Mocks/` (the directory listing only — do not read mock bodies)
- `src/Animora.Contracts/Sync/ISyncStatusService.cs`
- `scripts/sandbox/build.sh`, `scripts/sandbox/run.sh`, `scripts/sandbox/test.sh`, `scripts/sandbox/bench.sh`

## Goal
Every mock is gone, the DI container references nothing under `Mocks/`, and the offline product is
**proven** end to end by a scripted scenario plus measured startup/memory/latency budgets. This is
the gate that declares "the desktop is a real offline product" before the server track starts.

## Design decisions (pre-made — implement, do not re-derive)
- **The only mock left after C10 is `MockSyncStatusService`.** It is replaced here by
  `LocalSyncStatusService` (`Animora.Client.Services/Sync/LocalSyncStatusService.cs`) — a **real,
  server-free implementation**: it reports `SyncState.Offline` when no `ICommandTransport` endpoint
  is configured, `SyncState.Pending` when B02's `PendingCommandSummaryQuery` shows unsettled
  commands, `SyncState.Synced` with `LastSyncedAt` from a `SyncCursor` row otherwise, and
  `SyncNowAsync` runs one `LocalCommandProcessor.RunPassAsync` against the configured transport
  (today the `NullCommandTransport`, so the pass is a legitimate no-op that still updates the
  summary). E04 replaces the internals, **not** the registration. `MockSyncStatusService.cs` is
  deleted here.
- **`Mocks/` directory is removed entirely** (the folder must not exist), and a build-time guard is
  added: `LayeringRulesTests.NoMockNamespaceExists` asserts no type in `Animora.Desktop` lives in a
  `*.Mocks` namespace and that `App.axaml.cs` contains no identifier starting with `Mock`.
  This is the machine-checkable form of R-B34.
- **The DI audit** also asserts, in the same test class:
  - every interface in `Animora.Contracts` (discovered by reflection over the Contracts assembly)
    has exactly one registration in the composition root, and the implementation type's assembly is
    `Animora.Client.Services` (allowed exceptions, listed explicitly: `INavigationService`,
    `IToastService`, `IDialogService`, `IRecordSheetPresenter` — pure UI services that legitimately
    live in `Animora.Desktop`);
  - no `Animora.Desktop` type references `Microsoft.EntityFrameworkCore`, `System.Data.Common`,
    `System.Net.Http`, or a Datasync type (extends CONVENTIONS §2's layering test).
- **The end-to-end offline scenario** lives in
  `tests/Animora.Application.Tests/Scenarios/OfflineClinicDayScenarioTests.cs` and runs against a
  **real temp-file SQLite DB with migrations applied** (not in-memory), executing one continuous
  flow with a controlled `IClock`:
  1. first-run setup creates the tenant + owner/admin (B03);
  2. register an owner + pet (C01);
  3. book an appointment for tomorrow, then check it in (C04);
  4. the ticket appears in the reception queue; admit and enter visit (C02);
  5. record vitals, a diagnosis and two Rx lines; complete the visit (C03);
  6. the handoff appears in "ready to bill"; create an invoice from it, add a product line (C07);
  7. open a cash session; take a partial cash payment, then the rest (C08, C07);
  8. assert: invoice `Paid`, two `Payment` rows, one `PendingCommand` per business mutation, all in
     `LocallyPosted`/`PendingSettlement`;
  9. assert: stock for the sold product dropped by exactly the sold quantity (C06 snapshot **and**
     the movement recomputation agree);
  10. assert: the ledger has balanced entries whose revenue total equals the invoice total (C09);
  11. assert: the activity feed contains the expected 9 events in order (C10);
  12. close the cash session with an exact count → variance 0;
  13. **kill-and-reopen simulation**: dispose the context, reopen the same DB file, and re-assert
      every number above (crash resilience of the local store, R-B09);
  14. boarding variant: create a stay, add items, check out, invoice it from the boarding handoff
      (C05) and assert the same ledger/stock/feed consequences.
  The scenario asserts **only** on codes, ids and numbers — never on a Persian string
  (CONVENTIONS §9).
- **Budget measurement** is added to `scripts/sandbox/bench.sh` as a new `offline` mode that runs
  `Animora.Desktop` under Xvfb with `ANIMORA_BENCH=1` and records, into
  `backend-roadmap/measurements/C11-offline-budget.md` (a new folder, the only non-phase file this
  package gains):
  | Metric | Budget | How measured |
  |---|---|---|
  | Time to first frame | ≤ 1200 ms | stopwatch from `Main` to `MainWindow.Opened` |
  | Time to dashboard data | ≤ 2500 ms | first frame → snapshot rendered |
  | Working set after 60 s idle | ≤ 300 MB | `Process.WorkingSet64` |
  | Patients list page (200 rows) | ≤ 120 ms | query stopwatch, warm |
  | Global search keystroke | ≤ 80 ms | query stopwatch, warm |
  | Post invoice (10 lines) | ≤ 250 ms | command stopwatch, warm |
  Numbers are **recorded, not asserted** in CI (hardware varies), but a regression beyond 2× the
  budget fails `bench.sh offline` with a non-zero exit (A-RULE 035: measured, never asserted from
  specs).
- **Startup path check (PERFORMANCE.md §12):** a test asserts that `App.axaml.cs`'s startup does not
  `await` migration, seeding, or projection rebuild before the first frame — those run behind
  `LocalStoreStartup` with `IStoreReadinessGate` (C01). If a violation is found, the fix is to move
  the work into the gate, never to relax the test.
- **No new feature work.** This phase adds no command, no table, no Contracts member beyond the
  sync replacement.

## Files to create / modify
| Path | Action | Contents (one line) |
|---|---|---|
| `src/Animora.Client.Services/Sync/LocalSyncStatusService.cs` | create | real server-free `ISyncStatusService` |
| `src/Animora.Persistence.Sqlite/Entities/SyncCursor.cs` | create | `[LocalOnly]` last-synced marker (E03 extends it) |
| `src/Animora.Persistence.Sqlite/Configurations/Sync/SyncCursorConfiguration.cs` | create | mapping + unique key |
| `src/Animora.Persistence.Sqlite/Migrations/*` | create | `018_SyncCursor` + snapshot |
| `src/Animora.Desktop/App.axaml.cs` | modify | swap `ISyncStatusService`; remove the last `Mocks` using |
| `src/Animora.Desktop/Mocks/MockSyncStatusService.cs` | delete | replaced |
| `src/Animora.Desktop/Mocks/` | delete | directory removed entirely |
| `tests/Animora.Domain.Tests/LayeringRulesTests.cs` | modify | add mock-purge + DI-audit + forbidden-reference facts |
| `tests/Animora.Application.Tests/Scenarios/OfflineClinicDayScenarioTests.cs` | create | the 14-step end-to-end scenario |
| `tests/Animora.Application.Tests/StartupBudgetTests.cs` | create | no blocking work before first frame |
| `scripts/sandbox/bench.sh` | modify | add the `offline` mode + budget capture |
| `backend-roadmap/measurements/C11-offline-budget.md` | create | recorded numbers + hardware description |

## Implementation steps
1. Add `SyncCursor` + configuration + migration `018_SyncCursor`.
2. Write `LocalSyncStatusService` over `PendingCommandSummaryQuery` + `LocalCommandProcessor` +
   `SyncCursor`; swap the DI line; delete `MockSyncStatusService.cs`.
3. Delete the `Mocks/` directory and remove its `using` from `App.axaml.cs`; build.
4. Extend `LayeringRulesTests` with the three new facts (no mock namespace, one registration per
   Contracts interface from `Animora.Client.Services`, no EF/HTTP/Datasync reference in Desktop).
   Fix any violation the audit surfaces **in this phase** and record it in `PROGRESS.md`.
5. Write `OfflineClinicDayScenarioTests` exactly as the 14 steps, against a temp-file DB with
   migrations applied and an injected `IClock`.
6. Write `StartupBudgetTests`.
7. Add the `offline` mode to `bench.sh`; run it; write the measured table into
   `backend-roadmap/measurements/C11-offline-budget.md` with the hardware description.
8. Run the full verification set and record every result in `PROGRESS.md`.

## Contracts / schema added
- Persistence: `SyncCursor` + migration `018_SyncCursor`.
- Client services: `LocalSyncStatusService`.
- Tests: mock-purge/DI-audit facts, the end-to-end scenario, the startup-budget test.
- Scripts: `bench.sh offline`. Docs: `backend-roadmap/measurements/C11-offline-budget.md`.
- DI: `ISyncStatusService` → `LocalSyncStatusService`. **Mock deleted:
  `MockSyncStatusService.cs`; the entire `src/Animora.Desktop/Mocks/` directory removed.**
- No `Animora.Contracts` change, no new `CommandTypes`, no new `ErrorCodes`.

## Rules that apply here
- **R-B34 / CONVENTIONS §10** — the purge is machine-verified, not eyeballed.
- **R-B10 / CONVENTIONS §2** — the layering audit is extended, not relaxed, to cover the finished
  client stack.
- **A-RULE 035 / R-B08** — budgets are measured on described hardware and recorded; no capacity
  claim is asserted from specs.
- **PERFORMANCE.md §12** — no blocking work before first frame; the readiness gate is the only
  sanctioned deferral mechanism.
- **R-B09** — the scenario's kill-and-reopen step is the local crash-resilience proof.
- **CONVENTIONS §9** — the scenario asserts on codes and numbers, never on Persian strings.

## Out of scope
- Any server behavior, sync transport, or online auth → Tracks D/E.
- Replacing `LocalSyncStatusService`'s internals with the real engine → E04 (registration unchanged).
- Full validation-gate execution (`ARCHITECTURE.md` §19 A–V) → E08; this phase proves only the
  offline subset it can prove without a server.

## Acceptance criteria
- Build: `scripts/sandbox/build.sh full` = 0 warnings / 0 errors.
- `src/Animora.Desktop/Mocks/` **does not exist**; `grep -R "Mock" src/Animora.Desktop --include=*.cs`
  returns nothing outside comments.
- `LayeringRulesTests` green including the three new facts: every `Animora.Contracts` interface has
  exactly one registration, implemented in `Animora.Client.Services` except the four listed UI
  services; `Animora.Desktop` references no EF Core, `HttpClient`, `DbConnection` or Datasync type.
- `OfflineClinicDayScenarioTests` green: all 14 steps, including the reopen re-assertions and the
  boarding variant.
- `StartupBudgetTests` green.
- `scripts/sandbox/test.sh` = `TESTS OK` across all five test projects.
- `scripts/sandbox/run.sh` = `SMOKE RUN OK` with **no network interface required** (verified by
  running with networking disabled).
- `scripts/sandbox/bench.sh offline` completes and
  `backend-roadmap/measurements/C11-offline-budget.md` contains every metric with a number, the
  budget, pass/fail and the hardware description.
- A manual pass of the 17 screens shows real data everywhere and no empty-state caused by a missing
  implementation.

## PROGRESS.md update
Mark C11 done; advance "Next phase" to `D01`; append a "Repo state" entry listing:
`LocalSyncStatusService` + `SyncCursor` + migration `018_SyncCursor` (**E04 replaces the internals,
not the registration**), the removal of `src/Animora.Desktop/Mocks/`, the three new
`LayeringRulesTests` facts, the end-to-end scenario and its 14 assertions, the startup-budget test,
`bench.sh offline`, the recorded budget table (with the measured numbers inlined into the entry),
any violation found and fixed by the DI audit, **mock deleted: `MockSyncStatusService.cs` —
Track C mock purge complete (22 of 22)**, deviations.
Also add a one-line "Track C complete" marker so Track D sessions can trust the offline baseline.
