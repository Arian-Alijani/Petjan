# Phase E05 — Conflict resolution & non-destructive pull

**Band:** M · **Track:** E · **Depends on:** E04 · **Satisfies:** R-B03, R-B17, R-B18
**Runnable after this phase:** desktop offline yes · server required no

## Read before starting
- `backend-roadmap/CONVENTIONS.md` §1, §2, §4, §5, §6 (items 9, 10, 15), §7, §9, §12
- `backend-roadmap/PROGRESS.md` (E03 entry — `SyncConflict`, `ConflictKind`, `ReplicaProjector`'s
  four rules incl. skip-on-pending, `FullResyncAsync`, `SyncContractCatalog.ProjectionOrder`;
  E04 entry — `SyncOrchestrator`, `SyncStateMachine`, `SyncBacklogDto`, `ISyncDiagnosticsQuery`;
  B02 entry — `PendingCommand`, `SettlementState`, `SettlementTransitions`, `CommandJournal`;
  B05 entry — `IUserNotificationPort`, `NotificationSeverity.NeedsAttention`, `AuditWriter`;
  A10 entry — `IUnitOfWork`; C01/C03/C04 entries — the owner/pet, history and appointment
  aggregates these policies act on)
- `ARCHITECTURE.md` §8.5 (the full policy matrix), §8.2 · `STACK.md` §4.4
- `A-RULE 002`, `A-RULE 003`, `A-RULE 004`, `A-RULE 017`, `A-RULE 024`
- `src/Animora.Sync.Datasync/Projection/ReplicaProjector.cs`

## Goal
Every replication conflict is routed through the four-layer pipeline of `ARCHITECTURE.md` §8.5 —
infrastructure detection, generic resolver (UI prefs only), **domain policy per data type**, server
re-validation — with unsent local intent protected during every pull, a durable
quarantine/needs-attention queue the user can act on, and the full-resync procedure available when a
client has fallen outside the retention window.

## Design decisions (pre-made — implement, do not re-derive)
- **The policy matrix is declarative data, transcribed verbatim from `ARCHITECTURE.md` §8.5** into
  `src/Animora.Application/Sync/ConflictPolicyMatrix.cs`:
  ```
  static ConflictPolicy For(string entityName, ConflictKind kind)
  enum ConflictPolicy { LastWriteWins, FieldAwareMerge, HumanReview, PreserveHistory,
                        RejectOnConflict, ImmutableReversalOnly, IdempotentCommandOnly,
                        AppendOnly, ServerAuthoritative }
  ```
  | Entity / data type | Policy |
  |---|---|
  | `UserPreference`, display settings | `LastWriteWins` (**the only generic-resolver case**) |
  | `Owner`/`Pet` non-critical free text (`Notes`, `Address`) | `FieldAwareMerge` |
  | `Owner`/`Pet` demographic identity fields (`FullName`, `NationalId`, `Phone`, `Species`, `Breed`, `BirthDate`) | `HumanReview` |
  | `MedicalHistoryEntry`, `Visit`, `VaccinationRecord` | `PreserveHistory` |
  | `Appointment`, `ReceptionQueueEntry` | `RejectOnConflict` |
  | `Invoice` (draft) | `HumanReview` |
  | `Invoice` (posted), `JournalEntry`, `AuditEntry` | `ImmutableReversalOnly` / `AppendOnly` |
  | `Payment`, `StockMovement`, `CashSession`, `Cheque` | `IdempotentCommandOnly` |
  | `RoleDefinition`, `StaffMembership`, `Tenant`, `Plan`-derived features | `ServerAuthoritative` |
  An entity/kind pair with no row throws `ConflictPolicyMissingException` — **fail closed; a new
  entity must get an explicit policy before it can sync.** `ConflictPolicyMatrixTests` asserts every
  entity in `SyncContractCatalog` has a policy and that no financial entity maps to
  `LastWriteWins` or `FieldAwareMerge` (A-RULE 003).
- **The resolver pipeline** (`src/Animora.Application/Sync/ConflictResolutionService.cs`),
  `Task<Result<ConflictDecision>> ResolveAsync(SyncConflict conflict, CancellationToken ct)`:
  1. **Detection** already happened (E03 returns `SyncConflict`). Nothing is overwritten before this
     point.
  2. **Generic resolver**: only if `ConflictPolicy.LastWriteWins` → apply the server row.
     Implemented with the library's `ServerWinsConflictResolver` **registered per entity type, never
     globally** (`STACK.md` §4.4): registration lives in
     `src/Animora.Sync.Datasync/Conflicts/ResolverRegistration.cs` and covers **only** preference
     entities. `ResolverRegistrationTests` asserts no other type has a generic resolver.
  3. **Domain policy** → `ConflictDecision`:
     `sealed record ConflictDecision(ConflictAction Action, string? MergedPayloadJson, ErrorCode? Error, string ReasonKey)`;
     `enum ConflictAction { ApplyServer, KeepLocal, ApplyMerged, Quarantine, RequireReversal, RequireResubmit }`.
     - `FieldAwareMerge` → `src/Animora.Application/Sync/FieldAwareMerger.cs`: per-entity
       **allow-list of mergeable fields** (declared, not reflective); a server change to a
       non-listed field while the local copy also changed it escalates to `Quarantine`.
     - `HumanReview` → `Quarantine`.
     - `PreserveHistory` → `ApplyServer` **plus** an append of the local variant as a new
       `MedicalHistoryEntry` marked `IsConflictPreservedCopy = true` (a new bool column, migration
       `032_ConflictQueue`) — the local clinical text is never lost.
     - `RejectOnConflict` → `KeepLocal` + `Quarantine` entry so the user re-books; the server row is
       applied only after the user acknowledges.
     - `ImmutableReversalOnly` → `RequireReversal` (the posted row is applied from the server; the
       local deviation becomes a reversal/credit-note task, never an overwrite).
     - `IdempotentCommandOnly` → `RequireResubmit` (the state is settled by command, so the fix is a
       retry of the original `OperationId`, which E06 performs).
     - `AppendOnly` → `ApplyServer` and append; never update in place.
     - `ServerAuthoritative` → `ApplyServer`.
  4. **Server re-validation** is not re-implemented here: for `RequireResubmit`/`RequireReversal` the
     decision is recorded and E06's transport re-submits, letting the server decide Accept/Reject/
     Conflict (A-RULE 004: policy decides *locally what to do with the row*; the server remains the
     authority on the business fact).
- **Non-destructive pull — the enforcement point (A-RULE 017, R-B18).**
  `src/Animora.Application/Sync/LocalIntentGuard.cs`:
  `Task<IntentLock> AcquireAsync(IReadOnlyList<string> aggregateIds, CancellationToken ct)` returns
  the subset of aggregates with an **unsettled** `PendingCommand`
  (`SettlementState ∈ { Draft, LocallyPosted, PendingSettlement, Conflict, NeedsAttention }`).
  `ReplicaProjector` (E03) is modified to call it **once per batch** and to skip every protected
  aggregate, emitting a `SyncConflict` with `ConflictKind.VersionMismatch`. Binding guarantees,
  each with a test:
  - a pull can never change a row whose aggregate has unsent intent;
  - skipping is recorded, never silent;
  - the replica row stays in `repl_*` so the next pass retries after settlement (no data is dropped
    on the floor);
  - `FullResyncAsync` keeps refusing while unsent intent exists (E03's rule, re-asserted here).
- **Quarantine queue — durable, user-visible, actionable.** Local table `ConflictQueueEntries`
  (`[LocalOnly]`, migration **`032_ConflictQueue`** *(audit 2026-10-08 · PH-03: the parenthetical ordinal is indicative only — at execution read the physical head of the owning context and take the **next free ordinal** per the PROGRESS.md Post-C11 rule; never trust the literal)* — this is the **next free ordinal on
  `AnimoraLocalDbContext` as of 2026-10-07 (physical head `031_TreatmentPlans`); re-verify the
  physical head at execution time and bump if more migrations have landed**):
  `Id` (ULID), `EntityName`, `EntityId`, `AggregateId`, `Kind`, `Policy`, `Status`
  (`enum ConflictQueueStatus { Open, Resolved, Dismissed, Escalated }`), `LocalPayloadJson`,
  `ServerPayloadJson`, `MergedPayloadJson`, `ReasonKey`, `ErrorCode`, `HasFinancialEffect` (bool),
  `ResponsibleRole` (from B02's `ResponsibilityMap` client mirror), `DetectedAtUtc`,
  `ResolvedAtUtc`, `ResolvedByUserId`, `AttemptCount`. Indexes:
  `IX_ConflictQueueEntries_Status_DetectedAtUtc`, unique
  `UX_ConflictQueueEntries_Entity_Open` on `(EntityName, EntityId)` filtered to `Status = Open`
  (one open entry per row — a repeating conflict increments `AttemptCount` instead of piling up).
  The table is append-mostly: resolution updates `Status`/`ResolvedAt*` only; the two payload
  snapshots are never edited.
- **Resolution use cases** (`src/Animora.Application/UseCases/Sync/`), all journaled through B02
  where they change business data:
  `ResolveConflictKeepLocalCommand`, `ResolveConflictAcceptServerCommand`,
  `ResolveConflictApplyMergeCommand` (payload = the field-level choices),
  `DismissConflictCommand` (non-business entities only; rejects with `CONFLICT_NOT_DISMISSABLE` when
  `HasFinancialEffect`), and query `OpenConflictListQuery` (paged, 25) +
  `ConflictDetailQuery` (field-by-field local/server/merged view).
  A conflict with `HasFinancialEffect = true` can **only** be closed by `RequireReversal`'s reversal
  command or by the server settling a resubmit — never by "dismiss" (A-RULE 002).
- **UI seam, additive.** `Animora.Contracts` **addition**: `IConflictQueueService` in
  `Animora.Contracts/Sync/` — `GetOpenAsync`, `GetDetailAsync`, `KeepLocalAsync`,
  `AcceptServerAsync`, `ApplyMergeAsync`, `DismissAsync`, plus `ConflictSummaryDto`,
  `ConflictDetailDto`, `ConflictFieldDto(string FieldKey, string? LocalValue, string? ServerValue, bool Mergeable)`.
  Implemented by `src/Animora.Client.Services/Sync/ConflictQueueService.cs`.
  Presentation: the **existing** B05 `IUserNotificationPort` raises one
  `NotificationSeverity.NeedsAttention` notification per new open conflict with code
  `SYNC_CONFLICT_NEEDS_ATTENTION`, `entityRef = EntityName/EntityId` and
  `NotificationAction("open-conflicts", "/settings/sync")` — the existing toast/dialog services
  render it. `SyncBacklogDto` (E04) gains `OpenConflicts int`. **No `.axaml` change**; the only
  ViewModel change is two properties on the existing sync flyout VM, listed below.
- **Full-resync path for a retention-window overrun.** `SyncOrchestrator` (E04) is modified: when a
  pull returns `SyncOutcome.Rejected` with the server's retention error, or
  `SyncCursor.ConsecutiveFailures >= 10` on the same entity, it raises a
  `NeedsAttention` notification with action `run-full-resync` — it **never** self-triggers a resync.
  The user (or a diagnostic screen) calls `IConflictQueueService`-adjacent
  `RunFullResyncCommand`, which refuses while unsent intent exists, audits, and delegates to E03's
  `FullResyncAsync`. Automatic destructive recovery is forbidden.
- **Audit.** Every `ConflictDecision` and every resolution command writes one `AuditEntry`
  (`audit.sync.conflict.<action>`) with `EntityName`, `EntityId`, `Policy`, `ReasonKey`,
  `HasFinancialEffect`, `CorrelationId`. Conflicts are a security-relevant class of event
  (R-B29).

## Files to create / modify
| Path | Action | Contents (one line) |
|---|---|---|
| `src/Animora.Application/Sync/ConflictPolicyMatrix.cs` | create | the §8.5 matrix as data + fail-closed lookup |
| `src/Animora.Application/Sync/ConflictResolutionService.cs` | create | the 4-layer pipeline |
| `src/Animora.Application/Sync/FieldAwareMerger.cs` | create | per-entity mergeable-field allow-list |
| `src/Animora.Application/Sync/LocalIntentGuard.cs` | create | unsent-intent lock set |
| `src/Animora.Application/Sync/ConflictDecision.cs` | create | decision record + `ConflictAction` |
| `src/Animora.Application/UseCases/Sync/*.cs` | create | 4 resolution commands + 2 queries + `RunFullResyncCommand` |
| `src/Animora.Persistence.Sqlite/Entities/ConflictQueueEntry.cs` | create | the quarantine row |
| `src/Animora.Persistence.Sqlite/Configurations/Sync/ConflictQueueEntryConfiguration.cs` | create | mapping + filtered unique index |
| `src/Animora.Persistence.Sqlite/Sync/ConflictQueries.cs` | create | paged open list + detail projection |
| `src/Animora.Persistence.Sqlite/Migrations/*` | create | `032_ConflictQueue` (+ `IsConflictPreservedCopy`) |
| `src/Animora.Sync.Datasync/Conflicts/ResolverRegistration.cs` | create | generic resolver for preferences only |
| `src/Animora.Sync.Datasync/Projection/ReplicaProjector.cs` | modify | call `LocalIntentGuard`; route conflicts to the service |
| `src/Animora.Contracts/Sync/IConflictQueueService.cs` | create | Contracts **addition** + 3 DTOs |
| `src/Animora.Contracts/Sync/ISyncDiagnosticsQuery.cs` | modify | `SyncBacklogDto.OpenConflicts` |
| `src/Animora.Client.Services/Sync/ConflictQueueService.cs` | create | the Contracts implementation |
| `src/Animora.Client.Services/Sync/SyncOrchestrator.cs` | modify | retention/failure → `run-full-resync` notification |
| `src/Animora.Desktop/Shell/TopBarViewModel.cs` | modify | 2 properties (open-conflict count + open command) |
| `src/Animora.Desktop/App.axaml.cs` | modify | register `IConflictQueueService` + the service graph |
| `tests/Animora.Application.Tests/ConflictPolicyMatrixTests.cs` | create | full coverage + no LWW on money |
| `tests/Animora.Application.Tests/ConflictResolutionTests.cs` | create | each policy → expected action |
| `tests/Animora.Sync.Tests/NonDestructivePullTests.cs` | create | unsent intent never overwritten |
| `tests/Animora.Persistence.Tests/ConflictQueueTests.cs` | create | one-open-per-row, status transitions |

## Implementation steps
1. Write `ConflictPolicyMatrix` from `ARCHITECTURE.md` §8.5 with the fail-closed lookup; write its
   coverage test immediately.
2. Add `ConflictQueueEntry` + configuration + the `MedicalHistoryEntry.IsConflictPreservedCopy`
   column; generate migration `032_ConflictQueue`; write `ConflictQueries`.
3. Write `LocalIntentGuard` over `CommandJournal`'s unsettled states.
4. Write `FieldAwareMerger` with the per-entity allow-lists (Owner: `Notes`, `Address`;
   Pet: `Notes`, `Color`; nothing else).
5. Write `ConflictResolutionService` implementing the four layers and producing `ConflictDecision`
   plus the quarantine row.
6. Write `ResolverRegistration` (preferences only) and modify `ReplicaProjector` to call
   `LocalIntentGuard` per batch and route every `SyncConflict` into the service.
7. Write the four resolution commands, the two queries and `RunFullResyncCommand` (refuse on unsent
   intent → audit → `FullResyncAsync`).
8. Add `IConflictQueueService` + DTOs to Contracts; implement `ConflictQueueService`; extend
   `SyncBacklogDto`.
9. Modify `SyncOrchestrator` to raise the `run-full-resync` notification and never self-trigger.
10. Add the two `TopBarViewModel` properties; register everything in `App.axaml.cs`.
11. Write the four test classes.
12. Verify with two clients against one server: edit the same `Pet.Notes` on both → field merge;
    edit `Pet.FullName` on both → quarantine, both values visible, user picks one; edit an
    `Appointment` while another device moved it → local kept, quarantine entry, re-book works; a
    locally posted invoice against a server-posted one → `RequireReversal`, no overwrite; a pull
    while an unsent payment exists → the aggregate is skipped and reported.
13. `build.sh full` = 0/0; `test.sh` = `TESTS OK`; `run.sh` = `SMOKE RUN OK`.

## Contracts / schema added
- Application: `ConflictPolicyMatrix`, `ConflictPolicy`, `ConflictResolutionService`,
  `ConflictDecision`, `ConflictAction`, `FieldAwareMerger`, `LocalIntentGuard`, `IntentLock`,
  `ConflictPolicyMissingException`, 4 resolution commands, `RunFullResyncCommand`,
  `OpenConflictListQuery`, `ConflictDetailQuery`.
- Persistence: `ConflictQueueEntry`, `ConflictQueueStatus`, `ConflictQueries`,
  `MedicalHistoryEntry.IsConflictPreservedCopy`, migration `032_ConflictQueue`.
- Sync.Datasync: `ResolverRegistration`; `ReplicaProjector` now intent-guarded.
- `Animora.Contracts` **additions**: `IConflictQueueService`, `ConflictSummaryDto`,
  `ConflictDetailDto`, `ConflictFieldDto`; `SyncBacklogDto.OpenConflicts` (additive member).
- Client services: `ConflictQueueService`.
- `ErrorCodes` += `CONFLICT_POLICY_MISSING`, `CONFLICT_NOT_DISMISSABLE`,
  `CONFLICT_MERGE_FIELD_NOT_ALLOWED`, `CONFLICT_ALREADY_RESOLVED`
  (`FULL_RESYNC_BLOCKED` exists from E03 — reuse).
- DI: `IConflictQueueService` registered. `WireContract.Version` stays `1` (client-side only).
- No `CommandTypes` addition for dismiss/accept-server (local-only state changes); the reversal and
  resubmit paths reuse the existing Sales/Finance command types.

## Rules that apply here
- **A-RULE 004 / R-B17** — version mismatch only detects; `ConflictPolicyMatrix` decides; the server
  re-validates on resubmit. No layer is skipped and no layer is merged into another.
- **A-RULE 003** — the generic resolver is registered for preference entities only, asserted by
  `ResolverRegistrationTests`; no money/stock/scheduling/ledger type has one.
- **A-RULE 002 / R-B22** — posted invoices and ledger rows are `ImmutableReversalOnly`/`AppendOnly`;
  a financial conflict is never dismissible and never overwritten.
- **A-RULE 017 / R-B18** — `LocalIntentGuard` is called on every projection batch; the
  non-destructive guarantee is proven by `NonDestructivePullTests`.
- **A-RULE 024 / R-B16** — every quarantine entry carries what/why/financial-effect/responsible
  role, and raises a `NeedsAttention` notification through the existing port.
- **CONVENTIONS §6.15** — full resync is explicit, refuses while unsent intent exists, and is
  audited; nothing self-triggers it.
- **CONVENTIONS §5** — the filtered unique index enforces one open conflict per row; migration
  `032_ConflictQueue` is additive and edits no prior migration.

## Out of scope
- Priority command submission, retry with the original `OperationId`, and applying server
  Accept/Reject/Conflict to local state → E06 (this phase records `RequireResubmit`; E06 acts on it).
- Server-side conflict handling and re-validation → already D06/D07.
- A dedicated conflict-resolution screen design → the existing sync flyout + dialogs render the
  queue; a richer screen is not required by any requirement.
- Gates A (patient conflict) and O (sync-failure UX) as recorded results → E08.
- Cross-device preference sync beyond `LastWriteWins` → not a requirement.

## Acceptance criteria
- Build: `scripts/sandbox/build.sh full` = 0 warnings / 0 errors.
- **Preserved copies stay local (audit 2026-10-08 · SYNC-H-01):** `SyncSurfaceTests` (D08) asserts by reflection that the
  `MedicalHistoryEntry` sync contract has no `IsConflictPreservedCopy` member, and `ReplicaProjector` never pushes a row where
  `IsConflictPreservedCopy = true`.
- **Blocking-debt gates (audit 2026-10-08):** (CODE-02) new conflict query/service interfaces take `ct = default` from the start;
  (CODE-05) `ConfigureAwait(false)` in `Animora.Sync.*`; (CODE-06) the new conflict-queue view-models are `IDisposable` and unsubscribe;
  (CODE-07) `AllowNegativeStockOnSale = true` is the shipped default — a server-side negative-stock rejection (`INSUFFICIENT_STOCK`) must
  surface through the **conflict path** (a `ConflictQueueEntry`), not be swallowed or shown as a generic failure.
- `ConflictPolicyMatrixTests` green: every entity in `SyncContractCatalog` has a policy for every
  `ConflictKind`; an unknown pair throws `ConflictPolicyMissingException`; **no** entity in the
  financial/stock/scheduling/ledger set maps to `LastWriteWins` or `FieldAwareMerge`.
- `ConflictResolutionTests` green: each policy produces its documented `ConflictAction`;
  `PreserveHistory` applies the server row **and** appends the local variant with
  `IsConflictPreservedCopy = true`; a merge touching a non-allow-listed field escalates to
  `Quarantine` with `CONFLICT_MERGE_FIELD_NOT_ALLOWED`; a `HasFinancialEffect` entry rejects
  `DismissConflictCommand` with `CONFLICT_NOT_DISMISSABLE`.
- `NonDestructivePullTests` green: with an unsettled `PendingCommand` on an aggregate, a pull
  carrying a newer server row changes **no** domain column, produces one `SyncConflict`, leaves the
  `repl_` row in place, and the next pass after settlement applies it; `FullResyncAsync` is refused
  in the same condition.
- `ConflictQueueTests` green: a repeated conflict on the same row increments `AttemptCount` and
  keeps exactly one `Open` entry; `Open → Resolved/Dismissed/Escalated` transitions persist with
  actor and timestamp; payload snapshots are never mutated.
- Manual (two clients, one server): the five scenarios in step 12 behave as specified; each produced
  one `NeedsAttention` notification and one `audit.sync.conflict.*` entry with the same
  `CorrelationId`; the topbar shows the open-conflict count.
- `ResolverRegistrationTests` green: exactly the preference entity types have a generic resolver.
- Runnability: `scripts/sandbox/run.sh` = `SMOKE RUN OK`; every screen still works offline.
- **Encrypted conflict queue (audit 2026-10-08 · SYNC-C-02):** `ConflictQueueEntries` (which stores
  `LocalPayloadJson`/`ServerPayloadJson` snapshots of PII/clinical rows) lives inside the encrypted
  container when the encryption profile is active (enabled in E07). A test opens the table through the
  store connection and asserts `PRAGMA cipher_version` is non-empty.

## PROGRESS.md update
Mark E05 done; advance "Next phase" to `E06`; append a "Repo state" entry listing:
`ConflictPolicyMatrix` (**the single source of conflict policy — later phases read it, never branch
around it**), `ConflictResolutionService`'s four layers, `FieldAwareMerger`'s allow-lists,
`LocalIntentGuard` + the modified `ReplicaProjector` (**the A-RULE 017 enforcement point**),
`ConflictQueueEntries` + migration `032_ConflictQueue` + the filtered unique index,
`MedicalHistoryEntry.IsConflictPreservedCopy`, the 4 resolution commands + `RunFullResyncCommand`
(**E06 consumes `RequireResubmit`/`RequireReversal`**), the `Animora.Contracts` additions
(`IConflictQueueService`, 3 DTOs, `SyncBacklogDto.OpenConflicts`), the `NeedsAttention` notification
code + action, the orchestrator's never-self-resync rule, the 4 new error codes, deviations.
