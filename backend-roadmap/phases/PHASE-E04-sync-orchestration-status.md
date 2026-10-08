# Phase E04 — Sync orchestration & status

**Band:** M · **Track:** E · **Depends on:** E03 · **Satisfies:** R-B03
**Runnable after this phase:** desktop offline yes · server required no (the loop reports `Offline`)

## Read before starting
- `backend-roadmap/CONVENTIONS.md` §1, §2, §4, §6 (items 2, 14, 15), §8, §9, §12
- `backend-roadmap/PROGRESS.md` (E03 entry — `ISyncEngine`, `SyncScope`, `SyncResult`,
  `SyncOutcome`, `ISyncDiagnostics`, `SyncEntityStatus`, `NullSyncEngine`, `SyncOptions`, the
  extended `SyncCursor` columns; C11 entry — `LocalSyncStatusService` (**replaced here**),
  `SyncCursor`, `PendingCommandSummaryQuery`; B02 entry — `LocalCommandProcessor`,
  `ICommandTransport`, `RetryPolicy`; B05 entry — `IUserNotificationPort`, `AmbientCorrelation`,
  `LoggingSetup`; A02 entry — `IClock`, `IMonotonicClock`)
- `ARCHITECTURE.md` §12.3, §15.2 · `STACK.md` §7.2 · `PERFORMANCE.md` §4, §5, §15
- `A-RULE 011`, `A-RULE 026`, `A-RULE 032`, `T-RULE 006`
- `src/Animora.Contracts/Sync/ISyncStatusService.cs`, `src/Animora.Desktop/App.axaml.cs`

## Goal
A real background sync loop runs inside the desktop: jittered, adaptively backed off, connectivity
aware, circuit-broken into an honest "working offline" state, and resumable after a long gap via
progressive catch-up. `ISyncStatusService` gets its **real** implementation (`SyncOrchestrator`-backed),
replacing C11's placeholder, and per-entity cursor/backlog diagnostics reach the existing UI seam with
zero `.axaml` changes.

## Design decisions (pre-made — implement, do not re-derive)
- **No fixed interval anywhere** (`ARCHITECTURE.md` §12.3). The loop's next-run delay is computed by
  `src/Animora.Client.Services/Sync/SyncSchedule.cs` — a pure, unit-testable function:
  ```
  base:      idle 300 s · recentLocalChange 30 s · catchUp 5 s
  failure:   delay = min(base * 2^consecutiveFailures, 900 s)
  jitter:    delay *= (0.75 + rand.NextDouble() * 0.5)     // ±25%, per-device seed
  ```
  `recentLocalChange` applies when `PendingCommandSummaryQuery` reports unsent work or a replica push
  is queued. `catchUp` applies while `SyncResult.Outcome == PartiallyCompleted` or the backlog
  estimate exceeds `CatchUpThreshold = 500` rows. Jitter uses `Random.Shared` seeded once per process
  from the A11 `LocalInstallation.InstallationId` hash, so a fleet restarting together does not
  converge on the same tick (the reconnect-storm defense, gate I).
- **Progressive catch-up, not one giant pull.** After a long offline period the orchestrator runs
  **narrowing passes**: first `SyncScope` with the 13 reference/catalog entities from
  `SyncContractCatalog.ProjectionOrder`'s head, then the 9 transactional-read entities, then repeats
  while any entity reports `Complete = false`, with a hard cap of `MaxCatchUpPasses = 20` per wake-up
  before yielding back to the schedule (so catch-up can never monopolize the process). Each pass is a
  separate `SynchronizeAsync` call — the engine already commits per entity, so a kill mid-catch-up
  loses at most one entity's page (A-RULE 026).
- **`SyncOrchestrator` (`src/Animora.Client.Services/Sync/SyncOrchestrator.cs`)** — a
  `BackgroundService`-shaped class (`StartAsync`/`StopAsync`, no ASP.NET dependency) owning one
  long-running `Task`:
  1. wait `SyncSchedule.NextDelay(...)` (cancellable);
  2. `IConnectivityProbe.CheckAsync()`;
  3. if the circuit is open and its cooldown has not elapsed → publish `Offline`, continue;
  4. `ISyncEngine.SynchronizeAsync(scope, ct)`;
  5. fold the result into `SyncStateMachine`, persist cursors, notify;
  6. loop.
  **Single-flight:** a `SemaphoreSlim(1,1)` guards the pass, so a manual "sync now" during an
  automatic pass awaits the running pass instead of starting a second one
  (`SyncNowAsync` returns when that pass settles — exactly the contract the topbar expects).
  `StopAsync` cancels and awaits with a **5 s** drain budget; the pass is resumable, so a timeout is
  logged, not fatal.
- **`IConnectivityProbe` (`src/Animora.Client.Services/Sync/ConnectivityProbe.cs`)** —
  `Task<Connectivity> CheckAsync(CancellationToken ct)`;
  `enum Connectivity { Unknown, NoNetwork, ServerUnreachable, Reachable }`. Implementation:
  `NetworkInterface.GetIsNetworkAvailable()` first (free), then a `GET {ServerBaseUrl}/health/live`
  with a **2 s** timeout, result cached for **15 s**. It never probes an external host — a DNS or
  Internet check would be a hidden Internet dependency (A-RULE 016 / R-B27).
  `NetworkChange.NetworkAddressChanged` triggers an immediate re-probe and, if `Reachable`, shortens
  the pending delay to `catchUp` — reconnect is event-driven, not poll-driven.
- **Circuit breaker (`src/Animora.Client.Services/Sync/SyncCircuit.cs`)**: opens after
  **5** consecutive failed passes (or a single `SyncOutcome.Rejected` carrying
  `CONTRACT_VERSION_UNSUPPORTED`), cooldown **min(2^(openCount) × 60 s, 1800 s)**, half-opens with a
  single trial pass. While open the app is in a *declared* offline mode: `SyncState.Offline`, the
  local DB stays fully writable, and pending work keeps accumulating. Open/close transitions write
  one structured log line and one `AuditEntry` (`audit.sync.circuit`).
- **`SyncStateMachine` (`src/Animora.Client.Services/Sync/SyncStateMachine.cs`)** maps engine results
  onto the existing `SyncState` enum (`Animora.Contracts.Sync`) — **no Contracts change**:
  | Input | `SyncState` |
  |---|---|
  | pass in flight | `Syncing` |
  | `Completed`, no pending work | `Synced` |
  | `Completed`/`PartiallyCompleted` with pending commands or incomplete entities | `Pending` |
  | `Offline`, `Connectivity != Reachable`, or circuit open | `Offline` |
  | `Failed`, `Rejected`, `ContractUpgradeRequired` | `Error` |
  | **`Completed`/`PartiallyCompleted` but ≥1 command parked** (`NeedsAttention`/permanent `Rejected`) | **`AttentionRequired`** |
  `LastSyncedAt` = the newest `SyncCursor.LastPulledAtUtc`, read from the local DB, so it survives
  restart (the mock's in-memory value did not).
- **`SyncState.AttentionRequired` — a state the circuit cannot represent (audit 2026-10-08 ·
  SYNC-H-03, IMP-04).** The circuit only opens on *transport* failure, so a pass that "replicates fine
  but has a parked financial command" otherwise shows a healthy `Synced`/`Pending` — hiding a command
  permanently stuck in `NeedsAttention` behind green, which violates the §8.3 sync-failure UX contract
  ("who must act" must be surfaced). Add a distinct `AttentionRequired` so a non-zero
  `NeedsAttentionCommands` count (from the E06 backlog DTO) is visually separate from a clean `Synced`.
  Implementation note: this is the **one** permitted `Animora.Contracts.Sync.SyncState` addition in
  this phase (append-only enum member; no reorder); if a Contracts change must be avoided, surface the
  E06 topbar `NeedsAttentionCommands` counter as the distinguishing signal instead — but the mapping
  table above assumes the enum member.
- **Real `ISyncStatusService`:** `src/Animora.Client.Services/Sync/SyncStatusService.cs` implements
  the existing interface over `SyncOrchestrator` + `SyncStateMachine`. `StateChanged` is raised on
  the **UI thread** through the existing dispatcher abstraction already used by
  `ToastNotificationAdapter` (B05) — never from the loop's thread (PERFORMANCE.md §5: no cross-thread
  UI mutation, no UI-thread blocking). `SyncNowAsync` awaits the single-flight pass.
  **`LocalSyncStatusService.cs` is deleted in this phase** — one implementation per contract
  (`CONVENTIONS.md` §10 spirit; C11's audit test is updated to expect the new type).
- **Diagnostics surfaced without touching a View.** `Animora.Contracts` **addition** (additive, no
  member reshaped): `ISyncDiagnosticsQuery` in `Animora.Contracts/Sync/` →
  `Task<IReadOnlyList<SyncEntityStatusDto>> GetEntityStatusAsync(CancellationToken ct)` and
  `Task<SyncBacklogDto> GetBacklogAsync(CancellationToken ct)`;
  `sealed record SyncEntityStatusDto(string EntityName, string DisplayKey, DateTimeOffset? LastPulledAtUtc, int PendingPushCount, string? LastErrorCode, int ConsecutiveFailures)`;
  `sealed record SyncBacklogDto(int UnsentCommands, int OldestUnsentAgeMinutes, int IncompleteEntities, bool CircuitOpen, string? CircuitReopensInSeconds)`.
  Implemented by `SyncDiagnosticsQueryService` over `ISyncDiagnostics` +
  `PendingCommandSummaryQuery`. `DisplayKey` is a **localization key**, not a Persian string — the
  existing Desktop resource layer resolves it, so no Persian text enters a backend project.
- **UI wiring is additive and minimal.** The topbar's existing sync box already binds to
  `ISyncStatusService`; it gains a tooltip/flyout bound to `ISyncDiagnosticsQuery`, implemented by
  modifying **only** `src/Animora.Desktop/Shell/TopBarViewModel.cs` (add two observable properties +
  a refresh command that runs off the UI thread) and adding the localization keys to the existing
  resource file. **Zero `.axaml` changes** beyond binding the already-present flyout content control
  — the phase file lists the exact VM members added, and `CONVENTIONS.md` §10 item 6 applies.
- **Lifecycle.** `SyncOrchestrator.StartAsync` is called from `App.axaml.cs` **after** the A11 local
  store startup and B03 sign-in completes (no sync before an authenticated session — there is no
  token otherwise), and `StopAsync` from the existing shutdown hook B05 registered. Sign-out stops
  the loop and clears the cursor cache in memory (not the DB rows).
- **Options** (`SyncOptions` extension, E03's file): `AutoSyncEnabled = true`,
  `IdleIntervalSeconds = 300`, `ActiveIntervalSeconds = 30`, `CatchUpIntervalSeconds = 5`,
  `MaxBackoffSeconds = 900`, `CircuitFailureThreshold = 5`, `MaxCircuitCooldownSeconds = 1800`,
  `CatchUpThresholdRows = 500`, `MaxCatchUpPasses = 20`, `ConnectivityCacheSeconds = 15`.
  `Validate()` rejects a zero/negative interval and `ActiveInterval > IdleInterval`.
- **Logging/metrics budget (A-RULE 032, PERFORMANCE.md §15):** exactly **one** structured log line
  per pass (outcome, counts, duration, next-delay) plus one per circuit transition. No per-entity,
  per-row or per-tick logging. A local counter set (`SyncTelemetry`, in-process only, exposed through
  the B05 diagnostics bundle): passes, failures, rows pulled/pushed, longest pass, circuit opens.
  Nothing leaves the machine.

## Files to create / modify
| Path | Action | Contents (one line) |
|---|---|---|
| `src/Animora.Client.Services/Sync/SyncSchedule.cs` | create | pure next-delay function + jitter |
| `src/Animora.Client.Services/Sync/ConnectivityProbe.cs` | create | `IConnectivityProbe` + local-only probe |
| `src/Animora.Client.Services/Sync/SyncCircuit.cs` | create | breaker with cooldown + half-open |
| `src/Animora.Client.Services/Sync/SyncStateMachine.cs` | create | `SyncResult` → `SyncState` mapping |
| `src/Animora.Client.Services/Sync/SyncOrchestrator.cs` | create | the single-flight background loop |
| `src/Animora.Client.Services/Sync/SyncStatusService.cs` | create | real `ISyncStatusService` |
| `src/Animora.Client.Services/Sync/SyncDiagnosticsQueryService.cs` | create | real `ISyncDiagnosticsQuery` |
| `src/Animora.Client.Services/Sync/SyncTelemetry.cs` | create | in-process counters for the bundle |
| `src/Animora.Client.Services/Sync/LocalSyncStatusService.cs` | delete | replaced by `SyncStatusService` |
| `src/Animora.Contracts/Sync/ISyncDiagnosticsQuery.cs` | create | Contracts **addition** + 2 DTOs |
| `src/Animora.Sync.Datasync/SyncOptions.cs` | modify | the 10 orchestration options + `Validate()` |
| `src/Animora.Desktop/Shell/TopBarViewModel.cs` | modify | 2 properties + refresh command (off UI thread) |
| `src/Animora.Desktop/Resources/*.resx`-equivalent | modify | sync diagnostic localization keys |
| `src/Animora.Desktop/App.axaml.cs` | modify | register orchestrator + `ISyncStatusService` swap + start/stop hooks |
| `src/Animora.Infrastructure.Local/Diagnostics/DiagnosticsBundleBuilder.cs` | modify | include `SyncTelemetry` |
| `tests/Animora.Sync.Tests/SyncScheduleTests.cs` | create | base/backoff/jitter bounds, no fixed interval |
| `tests/Animora.Sync.Tests/SyncCircuitTests.cs` | create | open/cooldown/half-open/close |
| `tests/Animora.Sync.Tests/SyncStateMachineTests.cs` | create | every input → expected `SyncState` |
| `tests/Animora.Sync.Tests/SyncOrchestratorTests.cs` | create | single-flight, catch-up cap, cancellation |
| `tests/Animora.Application.Tests/SyncDiagnosticsQueryTests.cs` | create | backlog/status projection |

## Implementation steps
1. Extend `SyncOptions` with the 10 orchestration values + `Validate()`.
2. Write `SyncSchedule` as a pure function; write its tests first (the delay policy is the phase's
   riskiest logic).
3. Write `ConnectivityProbe` (network-availability → `/health/live`, 2 s timeout, 15 s cache,
   `NetworkAddressChanged` hook) and `SyncCircuit`.
4. Write `SyncStateMachine` and its exhaustive mapping test.
5. Write `SyncOrchestrator`: single-flight semaphore, the 6-step loop, progressive catch-up with the
   pass cap, graceful `StopAsync` drain.
6. Write `SyncStatusService` (UI-thread event marshalling, `SyncNowAsync` joins the in-flight pass)
   and delete `LocalSyncStatusService.cs`.
7. Add `ISyncDiagnosticsQuery` + DTOs to `Animora.Contracts`; implement
   `SyncDiagnosticsQueryService`.
8. Add `SyncTelemetry` and include it in the B05 diagnostics bundle.
9. Wire DI in `App.axaml.cs`: orchestrator singleton, `ISyncStatusService` → `SyncStatusService`,
   `ISyncDiagnosticsQuery` → `SyncDiagnosticsQueryService`, start after sign-in, stop on shutdown.
10. Add the two `TopBarViewModel` properties + refresh command and the localization keys; change no
    `.axaml` structure.
11. Write the five test classes; update C11's DI-audit test to expect `SyncStatusService`.
12. Verify manually with a server: the box shows `Syncing → Synced`; stopping the server yields
    `Offline` after the probe, then the circuit opens; restarting the server half-opens and returns
    to `Synced`; "sync now" during an automatic pass does not start a second pass;
    `LastSyncedAt` survives an app restart.
13. `build.sh full` = 0/0; `test.sh` = `TESTS OK`; with no `ServerBaseUrl`, `run.sh` = `SMOKE RUN OK`
    and the box shows `Offline` without error spam.

## Contracts / schema added
- `Animora.Contracts` **addition**: `ISyncDiagnosticsQuery`, `SyncEntityStatusDto`,
  `SyncBacklogDto` (additive; no existing member reshaped).
- Client services: `SyncSchedule`, `IConnectivityProbe`/`ConnectivityProbe`, `Connectivity`,
  `SyncCircuit`, `SyncStateMachine`, `SyncOrchestrator`, `SyncStatusService`,
  `SyncDiagnosticsQueryService`, `SyncTelemetry`.
- `SyncOptions` += 10 orchestration options.
- Desktop: `TopBarViewModel` gains 2 properties + 1 command; new localization keys.
- DI: `ISyncStatusService` → `SyncStatusService` (**replacing `LocalSyncStatusService`, which is
  deleted**), `ISyncDiagnosticsQuery` → `SyncDiagnosticsQueryService`, orchestrator start/stop hooks.
- `ErrorCodes` += `SYNC_CIRCUIT_OPEN`, `SYNC_PASS_TIMEOUT`.
- **No migration** (E03's `SyncCursor` columns are sufficient), no new wire contract, no
  `CommandTypes` entry. `WireContract.Version` stays `1`.

## Rules that apply here
- **`ARCHITECTURE.md` §12.3 / R-B03** — jittered, adaptive, progressive catch-up; no fixed interval
  exists in the code (asserted by `SyncScheduleTests`).
- **A-RULE 011** — the schedule and the circuit use `IMonotonicClock` for elapsed time; wall-clock
  changes cannot shorten a cooldown or fake a `LastSyncedAt`.
- **A-RULE 016 / R-B27** — connectivity is probed against the configured server's `/health/live`
  only; no DNS lookup or external host is used as a liveness signal.
- **A-RULE 026** — every pass is resumable: cancellation mid-pass leaves committed entity results
  intact and the next pass continues from the persisted cursors.
- **A-RULE 032 / PERFORMANCE.md §15** — one log line per pass, in-process counters only, everything
  reachable through the local diagnostics bundle.
- **PERFORMANCE.md §5** — the loop never touches the UI thread; `StateChanged` is marshalled; the
  topbar's refresh command awaits off-thread work.
- **CONVENTIONS §6.2** — the loop replicates state only; it never submits a `PendingCommand`
  (E06 owns that and will hook into this orchestrator).
- **CONVENTIONS §10 item 6** — no `.axaml` change; the single ViewModel change is listed explicitly.

## Out of scope
- Conflict resolution, quarantine and the domain policy matrix → E05 (conflicts are counted in
  `SyncBacklogDto` and surfaced, not resolved).
- Priority submission of `PendingCommand`s on reconnect and settlement reconciliation → E06.
- Token acquisition/refresh (`ISyncCredentialProvider` still returns `AUTH_REQUIRED` without E07) →
  E07. Until then a configured server yields `Rejected`/`Offline`, which the state machine renders
  honestly.
- Server-side sync diagnostics endpoints → already D13; this phase consumes none of them.
- Reconnect-storm gate I as a recorded result → E08 (the jitter mechanism lands here).

## Acceptance criteria
- Build: `scripts/sandbox/build.sh full` = 0 warnings / 0 errors.
- `SyncScheduleTests` green: idle/active/catch-up bases applied per input; backoff doubles and caps
  at 900 s; jitter always within ±25 %; two different installation ids produce different delays for
  identical inputs; no code path returns a constant delay.
- `SyncCircuitTests` green: opens on the 5th consecutive failure and immediately on
  `CONTRACT_VERSION_UNSUPPORTED`; cooldown doubles and caps at 1800 s; half-open runs exactly one
  trial pass; success closes and resets the counter; every transition logs and audits once.
- `SyncStateMachineTests` green: the full input matrix maps to the documented `SyncState`, including
  `Pending` when a pass completed but unsent commands remain, and (audit 2026-10-08 · SYNC-H-03,
  IMP-04) **`AttentionRequired`** when a pass completed/partially-completed but ≥1 command is parked
  (`NeedsAttention`/permanent `Rejected`) — a parked integrity error must not map to `Synced`.
- `SyncOrchestratorTests` green: two concurrent `SyncNowAsync` calls cause exactly one engine pass
  and both await it; catch-up stops at `MaxCatchUpPasses`; `StopAsync` cancels within the 5 s drain
  and a subsequent start resumes from the persisted cursors.
- `SyncDiagnosticsQueryTests` green: `SyncBacklogDto` reports unsent count, oldest age, incomplete
  entity count and circuit state; `DisplayKey` values are localization keys (no Persian literal in
  any backend project — grep assertion).
- Manual (real server): `Syncing → Synced`; server stopped → `Offline`, circuit opens after 5 passes;
  server restarted → half-open → `Synced`; `LastSyncedAt` survives restart; the topbar flyout shows
  per-entity cursors.
- Offline runnability: with no `ServerBaseUrl`, `scripts/sandbox/run.sh` = `SMOKE RUN OK`, the box
  reads `Offline`, and the log contains no repeated error spam (one line per pass).
- `src/Animora.Desktop/Mocks/` still does not exist; C11's DI-audit test remains green with the new
  registration.

## PROGRESS.md update
Mark E04 done; advance "Next phase" to `E05`; append a "Repo state" entry listing: `SyncSchedule`'s
delay policy and jitter seed, `IConnectivityProbe` + the local-only probe rule, `SyncCircuit`'s
thresholds/cooldowns, `SyncStateMachine`'s mapping table, `SyncOrchestrator`'s single-flight + catch-up
cap + start/stop hooks (**E05 and E06 extend this loop, they do not add a second one**), the real
`SyncStatusService` and the **deletion of `LocalSyncStatusService`**, the `Animora.Contracts` addition
`ISyncDiagnosticsQuery` + its 2 DTOs (**E06 extends the backlog DTO**), `SyncTelemetry` in the
diagnostics bundle, the 10 new `SyncOptions` values, the single `TopBarViewModel` change + new
localization keys, the 2 new error codes, deviations.
