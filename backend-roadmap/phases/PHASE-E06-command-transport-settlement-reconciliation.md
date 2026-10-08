# Phase E06 — Command transport & settlement reconciliation

**Band:** M · **Track:** E · **Depends on:** E05, D07 · **Satisfies:** R-B03, R-B09, R-B16
**Runnable after this phase:** desktop offline yes · server required no (commands stay
`PendingSettlement` until a server exists)

## Read before starting
- `backend-roadmap/CONVENTIONS.md` §1, §2, §4, §6 (items 1, 4, 5, 6, 7, 8), §7, §9, §12
- `backend-roadmap/PROGRESS.md` (B02 entry — `CommandEnvelope`, `SettlementResult`,
  `SettlementOutcome`, `FinancialEffect`, `SettlementState`, `SettlementTransitions`,
  `PendingCommands`, `CommandFingerprint`, `LocalSequenceAllocator`, `LocalCommandProcessor`,
  `ICommandTransport`/`NullCommandTransport` (**replaced here**), `RetryPolicy`,
  `UnsettledCommandListQuery`; D05 entry — `POST /api/commands`, `CommandBatchRequestV1`,
  `CommandBatchResultV1`, the idempotency + fingerprint rules, `DependencyResolver`,
  `ResponsibilityMap`, `BATCH_TOO_LARGE`, `DEPENDENCY_UNSETTLED`; D07 entry —
  `FinancialEffectResolver`; E03 entry — `SyncPreflight`, `ISyncCredentialProvider`,
  `ContractVersionHandler`; E04 entry — `SyncOrchestrator`, `SyncCircuit`, `SyncBacklogDto`;
  E05 entry — `ConflictQueueEntry`, `RequireResubmit`/`RequireReversal`, `ConflictPolicyMatrix`;
  B05 entry — `IUserNotificationPort`, `AuditWriter`)
- `ARCHITECTURE.md` §8.3, §8.4, §8.7 · `STACK.md` §7.2
- `A-RULE 005`, `A-RULE 019`, `A-RULE 021`, `A-RULE 022`, `A-RULE 024`, `A-RULE 025`
- `src/Animora.Application/Commands/LocalCommandProcessor.cs`

## Goal
The local command journal reaches the server: a real `ICommandTransport` posts
`CommandBatchRequestV1` to `POST /api/commands`, **ahead of routine replication on every reconnect**,
always reusing the original `OperationId`; every `SettlementResult` is applied to local state through
the B02 state machine; and the sync-failure UX contract (what failed / why / financial effect / who must
act) is delivered through the existing notification and dialog services.

## Design decisions (pre-made — implement, do not re-derive)
- **`HttpCommandTransport`** (`src/Animora.Sync.Datasync/Transport/HttpCommandTransport.cs` — it
  lives beside the sync transport because it shares the `HttpClient`, handlers and preflight, but it
  is **not** the sync engine and touches no Datasync type):
  `Task<Result<IReadOnlyList<SettlementResult>>> SubmitAsync(IReadOnlyList<CommandEnvelope> batch, CancellationToken ct)`.
  It reuses E03's named `HttpClient` (so `ContractVersionHandler` + `BearerTokenHandler` apply
  unchanged), posts `CommandBatchRequestV1`, and maps `CommandBatchResultV1` back to
  `SettlementResult` per `OperationId`. Registered as `ICommandTransport`, **replacing
  `NullCommandTransport`** in the composition root when `SyncOptions.ServerBaseUrl` is set.
- **Batching (must match D05's server limits):** `CommandBatchSize = 20` envelopes,
  `MaxBatchPayloadBytes = 512 KB`. A batch that would exceed either is split before sending; a
  `BATCH_TOO_LARGE` response halves the batch size for the remainder of the pass (down to 1) and
  logs once — the client adapts instead of failing the queue.
- **Priority submission on reconnect (`ARCHITECTURE.md` §8.7) — the ordering rule of this phase.**
  `SyncOrchestrator` (E04) is modified so every pass is:
  ```
  1. preflight  2. SUBMIT COMMANDS (LocalCommandProcessor.RunPassAsync)  3. replication push
  4. replication pull  5. projection/conflicts  6. state + notify
  ```
  Step 2 runs to completion (or to its own failure) **before** step 3. If step 2 reports a
  **transport failure** (`Deferred` / `SERVER_UNREACHABLE` / in-flight submit error), steps 3–4 are
  skipped for that pass — pulling server state while a transport is down and local intent is actively
  stuck would maximize the conflict surface. `CommandsBeforeReplicationTests` asserts the call order
  and the skip.
  **Parked ≠ transport-down (audit 2026-10-08 · SYNC-C-04 — a severe silent data-staleness bug if
  conflated):** a command that is **parked** — permanently `Rejected` / `NeedsAttention` / permanent
  `IntegrityError`, i.e. no longer being *submitted* — must **NOT** skip steps 3–4. Only an *in-flight
  transport* failure skips the pull. Otherwise one unrecoverable command freezes all inbound
  replication forever while the user sees `Pending`/`Synced`, not `Offline`. Step 2 therefore reports
  two distinct signals: *transport failure* (skip pull) vs *per-command settlement failure on a parked
  command* (does not skip pull). `ReplicationNotStarvedByParkedCommandTests` asserts: with one
  `NeedsAttention` command parked, the replication pull **still runs** the next pass. (IMP-08 standing
  fitness function — keep it as a permanent regression guard.)
- **Retry always reuses the original `OperationId` and fingerprint (A-RULE 005/019).**
  `LocalCommandProcessor` already stores both; the transport **never** regenerates them.
  `RetryIdentityTests` asserts that after 3 failed attempts the 4th request carries the identical
  `OperationId` **and** `CommandFingerprint` bytes. A local edit that would change the payload of an
  unsent command is rejected at the use-case level with `COMMAND_PAYLOAD_IMMUTABLE` — the user must
  reverse/resubmit, because mutating a sent-or-sending command is exactly the fingerprint-mismatch
  integrity error the server audits.
- **Settlement application (`src/Animora.Application/Commands/SettlementReconciler.cs`)**,
  `Task<Result> ApplyAsync(SettlementResult result, CancellationToken ct)` — one transaction per
  result, driven by `SettlementOutcome`:
  | Outcome | Local effect |
  |---|---|
  | `Settled` | `PendingCommand.Status → Settled`; apply the server's returned aggregate version/ids; mark the local business row `Settled`; clear `LastError` |
  | `Duplicate` (idempotent replay) | treated exactly as `Settled` using the stored result — **no second effect** |
  | `Rejected` | `Status → Rejected`, store `ErrorCode` + `ResponsibleRole`, raise the UX contract; the local business row keeps its `LocallyPosted` marker and becomes read-only pending user action |
  | `Conflict` | `Status → Conflict`, create an E05 `ConflictQueueEntry` with `HasFinancialEffect` from the result, policy from `ConflictPolicyMatrix` |
  | `DependencyUnsettled` | `Status → PendingSettlement`, `AttemptCount` unchanged, hold until the dependency settles (see below) |
  | `IntegrityError` (fingerprint mismatch) | `Status → NeedsAttention`, write a **security** audit entry, notify with severity `NeedsAttention`, and stop submitting that command permanently until a user acts |
  Reconciliation is **idempotent**: applying the same `SettlementResult` twice is a no-op
  (guarded by `SettlementTransitions` plus a `SettledAtUtc` null-check), because a response may
  arrive twice after a retry.
- **Dependency ordering: client hints only, server decides (A-RULE 021/022).**
  The client sends `DependsOnOperationId` and `ExpectedAggregateVersion` as B02 recorded them, and
  orders its **own** batch by `LocalSequence` with dependencies first
  (`src/Animora.Application/Commands/DependencyHintSorter.cs` — a topological sort over
  `DependsOnOperationId` within the batch, ties broken by `LocalSequence`).
  Binding rules: a cycle is impossible by construction (a dependency always has a lower
  `LocalSequence`; a violation throws `COMMAND_DEPENDENCY_CYCLE` and quarantines the batch); a
  dependency that is **not** in this batch and not yet `Settled` causes the dependent command to be
  **held locally**, not sent; `DEPENDENCY_UNSETTLED` from the server is honored without reordering
  anything locally. `DependencyHintSorterTests` proves the client never asserts a global order.
- **Sync-failure UX contract (A-RULE 024 / R-B16) — a declared mapping, not ad-hoc strings.**
  `src/Animora.Application/Commands/SettlementFailureContract.cs`:
  ```
  sealed record SettlementFailureNotice(string OperationId, string CommandType, ErrorCode Code,
      string WhatFailedKey, string WhyKey, FinancialEffect Effect, string ResponsibleRoleKey,
      string? EntityRef, string ActionCode);
  ```
  Built for every `Rejected`/`Conflict`/`IntegrityError` result from: the command type, the server's
  `ErrorCode`, `FinancialEffect` (from the result, resolved server-side by D07's
  `FinancialEffectResolver`), and `ResponsibilityMap`'s client mirror
  (`src/Animora.Application/Commands/ClientResponsibilityMap.cs` — the same role table D05 uses,
  duplicated as *keys only*, asserted identical by a test against `CommandTypes`).
  Delivered through B05's `IUserNotificationPort` with `NotificationAction(ActionCode, TargetRoute)`
  where `ActionCode ∈ { open-unsettled, open-conflicts, retry-command, reverse-invoice, contact-admin }`.
  **A notice with an empty `WhatFailedKey`, `WhyKey`, `Effect` or `ResponsibleRoleKey` fails a
  test** — "sync failed" alone is a defect.
- **Unsettled-work surface, additive to Contracts:** `Animora.Contracts` **addition**
  `IUnsettledWorkService` (`Animora.Contracts/Sync/`) — `GetUnsettledAsync` (paged 25),
  `RetryAsync(operationId)`, `AbandonAsync(operationId, reason)` and `GetNoticeAsync(operationId)`;
  DTOs `UnsettledCommandDto(string OperationId, string CommandTypeKey, string StateKey, string? EntityRef, bool HasFinancialEffect, string? ErrorCode, string? ResponsibleRoleKey, DateTimeOffset CreatedAtUtc, int AttemptCount)`
  and `SettlementNoticeDto`. Implemented by
  `src/Animora.Client.Services/Sync/UnsettledWorkService.cs` over `UnsettledCommandListQuery` +
  `LocalCommandProcessor`. `AbandonAsync` is **refused** (`COMMAND_ABANDON_BLOCKED`) when
  `FinancialEffect != None` — an abandoned financial command would leave the ledger inconsistent;
  the only exit is settlement or a reversal command.
- **`SyncBacklogDto` (E04) gains** `RejectedCommands int` and `NeedsAttentionCommands int`; the
  existing topbar flyout binds them (two more properties on the same ViewModel, no `.axaml` change).
- **Circuit interaction.** A transport failure counts toward E04's `SyncCircuit` exactly like a
  replication failure, so a dead server does not spin the command queue. While the circuit is open,
  commands stay queued and the UI reads `Offline` — never `Error`, because nothing is wrong locally.
  An `IntegrityError` never opens the circuit (it is a per-command integrity fault, not a transport
  fault).
- **Crash safety (R-B09).** Submission and reconciliation never share a transaction with the HTTP
  call: the journal row moves to `PendingSettlement` **before** the request, and the settlement is
  applied in a separate transaction **after** the response. A kill between the two leaves the command
  in `PendingSettlement`; the next pass resubmits with the same `OperationId` and the server returns
  the stored result (`Duplicate`) — exactly one financial effect. `CrashDuringSubmitTests` simulates
  the kill in all three windows (before send, after send/before response, after response/before
  apply).
- **Audit.** One `AuditEntry` per settlement application (`audit.command.settlement`) with
  `OperationId`, `CommandType`, `Outcome`, `ErrorCode`, `FinancialEffect`, `CorrelationId`; plus a
  distinct `audit.command.integrity` entry for `IntegrityError`.

## Files to create / modify
| Path | Action | Contents (one line) |
|---|---|---|
| `src/Animora.Sync.Datasync/Transport/HttpCommandTransport.cs` | create | real `ICommandTransport` over `POST /api/commands` |
| `src/Animora.Application/Commands/SettlementReconciler.cs` | create | outcome → local state, idempotent |
| `src/Animora.Application/Commands/DependencyHintSorter.cs` | create | in-batch topological sort, hints only |
| `src/Animora.Application/Commands/SettlementFailureContract.cs` | create | the what/why/effect/who notice |
| `src/Animora.Application/Commands/ClientResponsibilityMap.cs` | create | command type → responsible role keys |
| `src/Animora.Application/Commands/LocalCommandProcessor.cs` | modify | batching, split-on-`BATCH_TOO_LARGE`, reconciler call |
| `src/Animora.Contracts/Sync/IUnsettledWorkService.cs` | create | Contracts **addition** + 2 DTOs |
| `src/Animora.Contracts/Sync/ISyncDiagnosticsQuery.cs` | modify | `SyncBacklogDto` += 2 members |
| `src/Animora.Client.Services/Sync/UnsettledWorkService.cs` | create | retry/abandon/list/notice |
| `src/Animora.Client.Services/Sync/SyncOrchestrator.cs` | modify | commands-before-replication order + skip rule |
| `src/Animora.Desktop/Shell/TopBarViewModel.cs` | modify | 2 properties (rejected / needs-attention counts) |
| `src/Animora.Desktop/App.axaml.cs` | modify | `ICommandTransport` → `HttpCommandTransport`, register `IUnsettledWorkService` |
| `tests/Animora.Sync.Tests/CommandsBeforeReplicationTests.cs` | create | order + skip-replication-on-failure |
| `tests/Animora.Sync.Tests/RetryIdentityTests.cs` | create | same `OperationId` + fingerprint on every retry |
| `tests/Animora.Application.Tests/SettlementReconcilerTests.cs` | create | all 6 outcomes + double-apply no-op |
| `tests/Animora.Application.Tests/SettlementFailureContractTests.cs` | create | no field may be empty |
| `tests/Animora.Application.Tests/DependencyHintSorterTests.cs` | create | order, hold-off-batch, cycle guard |
| `tests/Animora.Persistence.Tests/CrashDuringSubmitTests.cs` | create | the three kill windows |

## Implementation steps
1. Write `ClientResponsibilityMap` + its equality test against `CommandTypes` and D05's map.
2. Write `SettlementFailureContract` and its non-empty-field test.
3. Write `SettlementReconciler` with the six-outcome table, idempotent application, and the audit
   entries.
4. Write `DependencyHintSorter` (in-batch topological sort, hold-off-batch, cycle guard).
5. Write `HttpCommandTransport` on E03's named `HttpClient`; implement the batch-size adaptation.
6. Modify `LocalCommandProcessor`: build batches (20 / 512 KB), sort by hints, submit, reconcile each
   result, update `RetryPolicy` state.
7. Modify `SyncOrchestrator` to run commands before replication and skip replication on transport
   failure; count transport failures into `SyncCircuit`.
8. Add `IUnsettledWorkService` + DTOs to Contracts; implement `UnsettledWorkService` with the
   financial-abandon refusal; extend `SyncBacklogDto`.
9. Swap `ICommandTransport` in `App.axaml.cs`; add the two `TopBarViewModel` properties.
10. Write the six test classes.
11. Verify against a running server: an offline-created invoice + payment settle in dependency order
    after reconnect; killing the app between send and response yields `Duplicate` and one ledger
    effect; a rejected command shows what/why/effect/who and offers the right action; a
    fingerprint-mismatch response produces a security audit entry and stops resubmission.
12. `build.sh full` = 0/0; `test.sh` = `TESTS OK`; with no server, `run.sh` = `SMOKE RUN OK` and
    commands stay `PendingSettlement`.

## Contracts / schema added
- Application: `SettlementReconciler`, `DependencyHintSorter`, `SettlementFailureContract`,
  `SettlementFailureNotice`, `ClientResponsibilityMap`; `LocalCommandProcessor` extended.
- Sync.Datasync: `HttpCommandTransport`.
- `Animora.Contracts` **additions**: `IUnsettledWorkService`, `UnsettledCommandDto`,
  `SettlementNoticeDto`; `SyncBacklogDto` += `RejectedCommands`, `NeedsAttentionCommands`.
- Client services: `UnsettledWorkService`; `SyncOrchestrator` reordered.
- `ErrorCodes` += `COMMAND_PAYLOAD_IMMUTABLE`, `COMMAND_DEPENDENCY_CYCLE`,
  `COMMAND_ABANDON_BLOCKED`, `COMMAND_TRANSPORT_FAILED`
  (`BATCH_TOO_LARGE`, `DEPENDENCY_UNSETTLED`, `IDEMPOTENCY_FINGERPRINT_MISMATCH` exist from D05 —
  reuse).
- DI: `ICommandTransport` → `HttpCommandTransport` (**`NullCommandTransport` remains the fallback
  when no server is configured**), `IUnsettledWorkService` registered.
- **No migration** (B02's `PendingCommands` columns suffice), no new wire contract
  (`CommandBatchRequestV1`/`CommandBatchResultV1` exist from D05). `WireContract.Version` stays `1`.

## Rules that apply here
- **A-RULE 005 / A-RULE 019 / R-B14** — retries reuse the original `OperationId` and fingerprint; a
  mismatch response is an audited integrity error that halts resubmission, never a silent retry.
- **A-RULE 021 / A-RULE 022 / R-B15** — the client sends dependency *hints* and orders only within
  its batch; the server decides; `DEPENDENCY_UNSETTLED` is honored without local reordering.
- **A-RULE 024 / R-B16** — every non-settled command yields a `SettlementFailureNotice` with all
  four required facts and an action; an empty field fails a test.
- **A-RULE 025** — reconciliation is an idempotent consumer: the same `SettlementResult` applied
  twice changes nothing.
- **R-B09** — journal transitions and HTTP calls never share a transaction; all three crash windows
  are covered by tests and resolve to exactly one financial effect.
- **R-B13 / CONVENTIONS §6.1** — commands and replication remain separate pipelines; they share only
  the `HttpClient` and the pass ordering, never a queue or a cursor.
- **CONVENTIONS §8** — batch caps (20 / 512 KB), 25-row paging on the unsettled list, one log line
  per batch.

## Out of scope
- Server-side command handling, idempotency storage and dependency resolution → already D05–D07.
- Token acquisition, refresh, device revocation handling → E07 (until then a configured server
  returns `401` and the pass reports `Rejected`/`AUTH_REQUIRED`).
- Conflict policy and the quarantine table → already E05 (this phase only creates entries).
- Gates B/C/D/H/P/O as recorded results → E08.
- A dedicated "pending work" screen design → the existing sync flyout, toasts and dialogs render the
  notices; `IUnsettledWorkService` is the seam if a screen is added later.

## Acceptance criteria
- Build: `scripts/sandbox/build.sh full` = 0 warnings / 0 errors.
- **Blocking-debt gates (audit 2026-10-08):** (CODE-02) new transport/settlement interfaces take `ct = default`; (CODE-05)
  `ConfigureAwait(false)` in `Animora.Sync.*`; (CODE-06) new sync view-models are `IDisposable` and unsubscribe; (CODE-07) the
  negative-stock server rejection reaches the user via the conflict path; (OPT-07) command batches are gzip-compressed on the wire when
  larger than a small threshold (`Content-Encoding: gzip`, server `RequestDecompression`) — a weak-link win measured in E08.
- **Drain progress (audit 2026-10-08 · SYNC-M-03):** the backlog DTO exposes the counters the E04 "draining N of M" line needs
  (unsent commands + `IncompleteEntities`), so a long serial drain is reported honestly rather than as a hang.
- `CommandsBeforeReplicationTests` green: in every pass the transport submit precedes the
  replication push/pull; a **transport failure** skips replication for that pass; the order holds on a
  manual `SyncNowAsync` too.
- `ReplicationNotStarvedByParkedCommandTests` green (audit 2026-10-08 · SYNC-C-04, IMP-08): with one
  command **parked** (`NeedsAttention`/permanent `Rejected`, no longer submitted) and the transport
  reachable, the replication pull **still runs** the next pass — a parked command never freezes
  inbound replication. Standing regression guard.
- `RetryIdentityTests` green: attempts 1–4 of the same command carry byte-identical `OperationId`
  and `CommandFingerprint`; a local attempt to edit an unsent command's payload is refused with
  `COMMAND_PAYLOAD_IMMUTABLE`.
- `SettlementReconcilerTests` green: all six outcomes produce the documented local effect;
  `Duplicate` produces no second effect; applying the same result twice is a no-op;
  `IntegrityError` writes the security audit entry and permanently halts resubmission of that
  command.
- `DependencyHintSorterTests` green: dependencies precede dependents in the batch; a dependency
  outside the batch and not `Settled` holds the dependent locally; a constructed cycle throws
  `COMMAND_DEPENDENCY_CYCLE` and quarantines the batch; the client never claims global ordering.
- `SettlementFailureContractTests` green: every `Rejected`/`Conflict`/`IntegrityError` notice has a
  non-empty what/why/effect/responsible-role and a valid `ActionCode`; `ClientResponsibilityMap`
  matches `CommandTypes` exactly.
- `CrashDuringSubmitTests` green: kills before send, after send/before response, and after
  response/before apply all converge to one settled command and one financial effect after the next
  pass.
- Manual (real server): the offline invoice+payment scenario settles in order; a rejected payment
  surfaces "what/why/financial effect/who must act" plus an action; `AbandonAsync` on a financial
  command is refused.
- Runnability: with no `ServerBaseUrl`, `run.sh` = `SMOKE RUN OK`, commands accumulate as
  `PendingSettlement`, and the topbar reports the backlog honestly.

## PROGRESS.md update
Mark E06 done; advance "Next phase" to `E07`; append a "Repo state" entry listing:
`HttpCommandTransport` (**replacing `NullCommandTransport` when a server is configured**), the batch
caps + adaptation rule, the **commands-before-replication** pass order in `SyncOrchestrator` (**E07/E08
must preserve it**), `SettlementReconciler`'s six-outcome table + idempotency guard,
`DependencyHintSorter`'s hints-only rule, `SettlementFailureContract` + `ClientResponsibilityMap`
(**the A-RULE 024 contract E08 gate O verifies**), the `Animora.Contracts` additions
(`IUnsettledWorkService`, 2 DTOs, `SyncBacklogDto` += 2), the financial-abandon refusal, the three
crash windows proven, the 4 new error codes, the DI swap, deviations.
