# Phase D05 — Business API — command intake & idempotency

> **PostgreSQL baseline (2026-10-08):** Read `ARCHITECTURE.md` §PG and §16-CT–§18-CT / `STACK.md` §6 before implementation. They govern PostgreSQL 18, Npgsql 10.0.3, UUID Version, tenant journal, snapshot/batch atomicity, pool budgets and deployment-only migrations. This phase is a future instruction, not an implementation claim. Mandatory PostgreSQL CI cannot pass by skipping DB tests.

**Band:** M · **Track:** D · **Depends on:** D04 · **Satisfies:** R-B13, R-B14, R-B15, R-B29
**Runnable after this phase:** desktop offline yes · server required no

## Read before starting
- `backend-roadmap/CONVENTIONS.md` §2, §3, §4, §5, §6 (items 1, 4, 5, 6, 13), §7, §8, §9, §12
- `backend-roadmap/PROGRESS.md` (B02 entry — `CommandEnvelope`, `SettlementResult`,
  `SettlementOutcome`, `FinancialEffect`, `CommandTypes`, `CommandFingerprint`,
  `SettlementTransitions`, `WireApiError`; D04 entry — `AnimoraPolicies`, `HttpCurrentUserProvider`,
  `ITenantContextSetter`; D03 entry — `IServerAuditWriter`)
- `ARCHITECTURE.md` §8.3, §8.4, §8.6, §15 · `STACK.md` §5.1
- `A-RULE 005`, `A-RULE 006`, `A-RULE 014`, `A-RULE 019`, `A-RULE 021`, `A-RULE 022`, `A-RULE 024`
- `src/Animora.Contracts.Wire/Commands/**`, `src/Animora.Application/Abstractions/**`

## Goal
One endpoint — `POST /api/commands` — durably accepts a `CommandEnvelope`, enforces idempotency on
`(TenantId, OperationId)`, resolves explicit dependencies **server-side only**, dispatches to a
registered server handler, and returns a `SettlementResult` that answers the four sync-failure UX
questions. This phase builds the **intake machinery**; the business handlers themselves are D06/D07.

## Design decisions (pre-made — implement, do not re-derive)
- **Endpoint contract** (`src/Animora.Server/Endpoints/CommandEndpoints.cs`):
  | Method + route | Body | Returns | Auth |
  |---|---|---|---|
  | `POST /api/commands` | `CommandEnvelope` | `SettlementResult` (200 on **every** settled outcome, including `Rejected`/`Conflict`) | authenticated + per-command policy |
  | `POST /api/commands/batch` | `CommandBatchRequestV1(IReadOnlyList<CommandEnvelope> Envelopes)` | `CommandBatchResultV1(IReadOnlyList<SettlementResult> Results)` | same |
  | `GET /api/commands/{operationId}` | — | `SettlementResult` of a prior command | authenticated |
  **HTTP status policy (binding):** a *settled* outcome — accepted, rejected or conflicted — is a
  successful conversation and returns **200** with the outcome inside `SettlementResult`. Only
  transport/auth/shape failures use 4xx/5xx with a `WireApiError`. A rejected invoice is a business
  answer, not an HTTP error; conflating the two is what makes clients guess (A-RULE 024).
  The batch endpoint is **not** a transaction: each envelope settles independently, results are
  returned in **request order**, and the batch never reorders (A-RULE 022). Max batch size 50,
  enforced and returned as `BATCH_TOO_LARGE`.
- **Idempotency store** — `src/Animora.Persistence.PostgreSql/Commands/IdempotencyRecord.cs`, table
  `IdempotencyRecords`, migration `104_CommandIntake`:
  | Column | Type | Note |
  |---|---|---|
  | `TenantId` + `OperationId` | `varchar(26)` ×2 | **composite PK** — the uniqueness key (`ARCHITECTURE.md` §8.3) |
  | `CommandType` | `varchar(64)` | |
  | `CommandFingerprint` | `char(64)` | SHA-256 hex, computed by B02's `CommandFingerprint` |
  | `Status` | `int` | `IntakeStatus { Received, InProgress, Settled, Failed }` |
  | `Outcome` | `int?` | `SettlementOutcome` once settled |
  | `ResultJson` | `text` | the **serialized `SettlementResult`** — replay returns this verbatim |
  | `ResultHash` | `char(64)` | integrity check on `ResultJson` |
  | `AggregateType`, `AggregateId` | `varchar(n)` | for the dependency + version checks |
  | `AppliedServerVersion` | `uuid?` | opaque server token after effect; canonical string on wire, never local bigint revision |
  | `FirstSeenAtUtc`, `LastAttemptAtUtc`, `SettledAtUtc` | `timestamptz(6)` | |
  | `AttemptCount` | `int` | |
  | `LastErrorCode` | `varchar(64)?` | |
  | `DeviceId`, `UserId`, `CorrelationId`, `CausationId`, `DependsOnOperationId` | `varchar(26)?` | correlation (R-B29) |
  | `ClaimedUntilUtc`, `ClaimToken` | | the in-progress lease, so a crashed request is recoverable |
  Indexes: PK `(TenantId, OperationId)`; `IX_IdempotencyRecords_TenantId_Status_ClaimedUntilUtc`;
  `IX_IdempotencyRecords_TenantId_AggregateType_AggregateId`;
  `IX_IdempotencyRecords_TenantId_DependsOnOperationId`.
- **Intake algorithm** (`src/Animora.Server/Commands/CommandIntakeService.cs`) — the exact order,
  because every step is an invariant:
  1. **Contract version gate.** `envelope.ContractVersion` must be within
    `[WireContract.MinSupportedVersion, WireContract.Version]` (`MinSupportedVersion` is **added to
    `WireContract` in this phase**, value `1`, giving the N/N-1/N-2 window a home). Outside →
    `CONTRACT_VERSION_UNSUPPORTED` with the supported range in `Detail` (`ARCHITECTURE.md` §8.6);
    never a silent mis-map.
  2. **Envelope validation.** Required fields present, ULID shapes valid, `CommandType` exists in
    `CommandTypes`, payload parses. Failure → `WireApiError` 400 (a malformed envelope is a
    transport fault, not a business rejection).
  3. **Tenant/actor consistency.** `envelope.TenantId` must equal the ambient `ITenantContext`
    value, `envelope.UserId` the authenticated user, `envelope.DeviceId` a registered non-revoked
    device. Mismatch → `TENANT_MISMATCH` / `ACTOR_MISMATCH` / `DEVICE_REVOKED` **and** a security
    audit entry (never a 500, never a silent pass — A-RULE 012).
  4. **Fingerprint recomputation.** The server recomputes `CommandFingerprint` from
    `CommandType` + canonical payload with B02's **same** algorithm and compares it to the envelope's
    value. Mismatch → `COMMAND_FINGERPRINT_INVALID` + security audit. The client's claimed
    fingerprint is never trusted as the stored one.
  5. **Idempotency decision** — PostgreSQL atomic `INSERT ... ON CONFLICT (TenantId,OperationId) DO NOTHING RETURNING`
    claim. If no row returned, perform a subsequent READ COMMITTED lookup to see the winning
    committed row; no same-snapshot CTE fallback that can miss a concurrent winner. Then
    conditional lease reclaim/settlement uses ClaimToken and DB UTC. Acquire tenant write gate
    before business locks in settlement (never hold it across receipt/network waits):
    - no row → row inserted `InProgress` with a lease → continue.
    - row exists, `Settled`, **same fingerprint** → return the stored `SettlementResult` verbatim
      (the replay path; no handler runs, no second effect — A-RULE 006). `AttemptCount` increments.
    - row exists, **different fingerprint** → `IDEMPOTENCY_FINGERPRINT_MISMATCH`, `403`, security
      audit with both fingerprints referenced (not the payloads) — this is an integrity error, not
      a replay (A-RULE 019).
    - row exists, `InProgress` with a **live** lease → `SettlementOutcome.Deferred` +
      `COMMAND_IN_PROGRESS` (the client retries with the same `OperationId`).
    - row exists, `InProgress` with an **expired** lease → re-claim and continue (a crashed request
      never strands a command — A-RULE 026).
  6. **Dependency resolution, server-side only** (`src/Animora.Server/Commands/DependencyResolver.cs`,
     A-RULE 021 / §8.4):
     `DependsOnOperationId` is `null` → proceed.
     Prerequisite `Settled`+`Accepted` → proceed.
     Prerequisite `Received`/`InProgress`/absent → `Deferred` + `DEPENDENCY_UNSETTLED` (a
     deterministic, retriable delay — never an optimistic accept).
     Prerequisite `Rejected`/`Conflict`/reversed → `Rejected` + `DEPENDENCY_REJECTED` (its
     precondition never became true).
     `ExpectedServerVersion` (canonical UUID from §PG.7 V2 envelope) present and ≠ current server token → `Conflict` +
     `AGGREGATE_VERSION_CONFLICT`. **Arrival order is never used as ordering** — only the
     dependency graph and the version check.
  7. **Authorization.** The per-`CommandType` permission comes from a single static map
     `CommandPermissions` (`src/Animora.Server/Commands/CommandPermissions.cs`):
     `CommandType → PermissionCodes` constant, **one entry per `CommandTypes` member**, with a test
     that fails if any `CommandTypes` constant is missing from the map (fail-closed: an unmapped
     command is **denied**, never allowed). Then D04's `ResourceAuthorizationService` runs.
  8. **Dispatch.** `IServerCommandDispatcher` resolves the handler from
     `ServerCommandRegistry` — the same reflection-free `FrozenDictionary<string, ...>` shape B01
     uses (PERFORMANCE.md §3), keyed by `CommandType` **string** (the wire name), with a pre-compiled
     delegate per entry. A `CommandType` with no registered handler → `Deferred` +
     `HANDLER_NOT_AVAILABLE`, audited — so a newer client's command is parked for a server upgrade
     instead of being wrongly rejected (mixed-fleet determinism, R-B31).
  9. **Settlement persistence.** Handler result → `SettlementResult`, stored in the idempotency row
     **inside the same transaction as the business effect** (`IServerUnitOfWork.ExecuteAsync`, added
     here as the server counterpart of A10's `IUnitOfWork`). Commit, then return. A crash between
     commit and response leaves the row `Settled` — the retry hits the replay path and returns the
     same result (gate D, `ARCHITECTURE.md` §19).
- **Server handler seam** (`src/Animora.Application/Commands/IServerCommandHandler.cs`):
  `interface IServerCommandHandler<TPayload>` with
  `Task<Result<ServerCommandEffect>> HandleAsync(TPayload payload, ServerCommandContext context, CancellationToken ct)`;
  `sealed record ServerCommandEffect(string? EntityId, string? EntityVersion, FinancialEffect FinancialEffect, string? AppliedServerVersion)`;
  `sealed record ServerCommandContext(string TenantId, string UserId, string DeviceId, string OperationId, string CorrelationId, DateTimeOffset ReceivedAtUtc)`.
  D06/D07 implement this interface only; they never see `HttpContext`, never touch the idempotency
  table and never build a `SettlementResult` (A-RULE 014 — write models stay server-side; the wire
  shape is produced in one place).
- **Sync-failure UX contract is produced here, once** (A-RULE 024): `SettlementResultFactory`
  (`src/Animora.Server/Commands/SettlementResultFactory.cs`) fills all four answers for every
  non-accepted outcome — `ErrorCode` (what), `Detail` (why, machine-readable + non-localized),
  `FinancialEffect` (`Applied` / `NotApplied` / `Unknown`), `ResponsibleRole` (who must act, from a
  static `ErrorCode → StaffRole` map `ResponsibilityMap`). A `SettlementResult` with
  `Outcome != Accepted` and a null `ResponsibleRole` is a **test failure**.
- **`WireContract` gains `MinSupportedVersion = 1`** and `SupportedVersions` (an
  `IReadOnlyList<int>`), so D08/E03/E09 have one place to widen the window. `Version` is bumped to
  **2** for additive ExpectedServerVersion semantics (§PG.7); retain V1 local payloads, explicit
  compatibility handlers and supported-window tests. MinSupportedVersion=1 does not make
  numeric-only V1 edits safe: stale-sensitive writes require an authorized server token or
  controlled upgrade/conflict, never automatic current-token substitution.
- **Audit:** every intake decision writes one `ServerAuditEntry` through D03's `IServerAuditWriter`
  with the full correlation set (`TenantId`, `OperationId`, `DeviceId`, `RequestId`, `CausationId`)
  — accepted, rejected, conflicted, deferred and every integrity error (R-B29).
- **OpenAPI:** the three endpoints are documented in the `business` document created in D01,
  including the "200 for settled rejection" policy as an explicit description so a client author
  cannot misread it.

## Files to create / modify
| Path | Action | Contents (one line) |
|---|---|---|
| `src/Animora.Contracts.Wire/WireContract.cs` | modify | `MinSupportedVersion`, `SupportedVersions` |
| `src/Animora.Contracts.Wire/Commands/CommandBatchRequestV1.cs` | create | batch request/result records |
| `src/Animora.Application/Commands/IServerCommandHandler.cs` | create | handler seam + effect + context |
| `src/Animora.Application/Abstractions/IServerUnitOfWork.cs` | create | server transaction port |
| `src/Animora.Persistence.PostgreSql/Commands/IdempotencyRecord.cs` | create | entity + `IntakeStatus` |
| `src/Animora.Persistence.PostgreSql/Commands/IdempotencyStore.cs` | create | atomic claim / replay / lease |
| `src/Animora.Persistence.PostgreSql/Commands/ServerUnitOfWork.cs` | create | `IServerUnitOfWork` impl |
| `src/Animora.Persistence.PostgreSql/Migrations/104_CommandIntake*` | create | `IdempotencyRecords` + indexes |
| `src/Animora.Server/Commands/CommandIntakeService.cs` | create | the 9-step algorithm |
| `src/Animora.Server/Commands/DependencyResolver.cs` | create | dependency + version resolution |
| `src/Animora.Server/Commands/CommandPermissions.cs` | create | `CommandType` → permission map |
| `src/Animora.Server/Commands/ServerCommandRegistry.cs` | create | reflection-free handler map |
| `src/Animora.Server/Commands/IServerCommandDispatcher.cs` | create | dispatch port + impl |
| `src/Animora.Server/Commands/SettlementResultFactory.cs` | create | the four UX answers + `ResponsibilityMap` |
| `src/Animora.Server/Endpoints/CommandEndpoints.cs` | create | the 3 endpoints + policies |
| `src/Animora.Server/Program.cs` | modify | register intake, registry, endpoints |
| `tests/Animora.Server.Tests/CommandIntakeTests.cs` | create | the 9 steps, in order, each failure path |
| `tests/Animora.Server.Tests/IdempotencyTests.cs` | create | replay, fingerprint mismatch, lost response, lease recovery |
| `tests/Animora.Server.Tests/DependencyOrderingTests.cs` | create | every interleaving resolves deterministically |
| `tests/Animora.Server.Tests/CommandPermissionMapTests.cs` | create | every `CommandTypes` member mapped |
| `tests/Animora.Server.Tests/SettlementContractTests.cs` | create | four UX answers always present |

## Implementation steps
1. Extend `WireContract`; add the batch wire records.
2. Add `IServerCommandHandler`, `ServerCommandEffect`, `ServerCommandContext`,
   `IServerUnitOfWork`.
3. Add `IdempotencyRecord` + `IdempotencyStore` (atomic conditional insert, lease, replay) +
   `ServerUnitOfWork`; generate migration `104_CommandIntake`.
4. Write `DependencyResolver` with the five outcomes and the version check.
5. Write `CommandPermissions` covering **every** `CommandTypes` constant; write
   `ServerCommandRegistry` + dispatcher with the pre-compiled delegate map.
6. Write `SettlementResultFactory` + `ResponsibilityMap`.
7. Write `CommandIntakeService` implementing steps 1–9 exactly in order.
8. Map the three endpoints with `AnimoraPolicies` + the batch size limit; document them in the
   `business` OpenAPI document.
9. Register everything; add the audit calls.
10. Write the five test classes; use a **stub** handler registered for a test-only `CommandType` so
    intake is provable before D06/D07 exist.
11. Verify gates C (duplicate command), D (lost response), P (two-queue dependency ordering) and Q
    (fingerprint integrity) behave as specified — the tests are named after them.
12. `build.sh full` = 0/0; `test.sh` = `TESTS OK`; desktop `run.sh` = `SMOKE RUN OK`.

## Contracts / schema added
- Wire: `WireContract.MinSupportedVersion`, `WireContract.SupportedVersions`,
  `CommandBatchRequestV1`, `CommandBatchResultV1`. `WireContract.Version` retains D05 **v2** (§PG.7; compatibility DTO suffix V1 does not reset the envelope version).
- Application: `IServerCommandHandler<TPayload>`, `ServerCommandEffect`, `ServerCommandContext`,
  `IServerUnitOfWork`.
- Persistence.PostgreSql: `IdempotencyRecord`, `IntakeStatus`, `IdempotencyStore`,
  `ServerUnitOfWork`, migration `104_CommandIntake`.
- Server: `CommandIntakeService`, `DependencyResolver`, `CommandPermissions`,
  `ServerCommandRegistry`, `IServerCommandDispatcher`, `SettlementResultFactory`,
  `ResponsibilityMap`, `CommandEndpoints`.
- `ErrorCodes` += `CONTRACT_VERSION_UNSUPPORTED`, `ACTOR_MISMATCH`,
  `COMMAND_FINGERPRINT_INVALID`, `COMMAND_IN_PROGRESS`, `DEPENDENCY_REJECTED`,
  `AGGREGATE_VERSION_CONFLICT`, `HANDLER_NOT_AVAILABLE`, `BATCH_TOO_LARGE`
  (`IDEMPOTENCY_FINGERPRINT_MISMATCH`, `DEPENDENCY_UNSETTLED`, `TENANT_MISMATCH`, `DEVICE_REVOKED`
  already exist — reuse).
- No `Animora.Contracts` change, no new `CommandTypes` entry, no DI change in `App.axaml.cs`.

## Rules that apply here
- **A-RULE 005 / 006 / R-B14** — the `OperationId` is the client's and is never regenerated; the
  same `OperationId` can never produce a second financial effect; the stored result is returned
  verbatim on replay.
- **A-RULE 019 / R-B14** — same `OperationId` + different fingerprint is a `403` integrity error
  with an audit entry, never a replay.
- **A-RULE 021 / 022 / R-B15** — idempotency and ordering are separate; dependency is explicit and
  resolved **only** server-side; arrival order is never ordering; the batch endpoint never reorders.
- **A-RULE 024 / R-B16** — every non-accepted `SettlementResult` carries what / why /
  financial-effect / who-must-act; a missing `ResponsibleRole` fails a test.
- **A-RULE 014** — handlers never build wire shapes and never see `HttpContext`.
- **R-B13** — this is the *command* pipeline; nothing here touches replication (D08).
- **R-B31 / `ARCHITECTURE.md` §8.6** — the supported-version window is explicit; an unsupported
  version is refused with the range, an unknown-but-supported command type is deferred.
- **R-B29** — every intake decision is audited with the full correlation set.

## Out of scope
- All business command handlers → D06 (clinical/scheduling) and D07 (money/stock).
- The outbox and its relay worker → D09.
- `/sync/*` replication → D08.
- The client-side transport that calls this endpoint → E06.
- Rate limiting and per-tenant throttling → D13.

## Acceptance criteria
- Build: `scripts/sandbox/build.sh full` = 0 warnings / 0 errors.
- **Batch intake (audit 2026-10-08 · OPT-03):** the intake accepts a batch of envelopes per request (bounded, e.g. ≤ 50) and settles them
  in one round-trip, each with its own idempotency row and per-command `SettlementResult` — a weak/slow link never needs one HTTP call
  per command.
- `CommandIntakeTests` green (or skipped with reason): each of the nine steps is provably reached in
  order, and each failure path returns the specified code — unsupported contract version, malformed
  envelope, tenant/actor/device mismatch, invalid fingerprint, unmapped command type.
- `IdempotencyTests` green: **gate C** — the same envelope sent twice produces exactly one effect
  and two identical `SettlementResult`s; **gate D** — a commit whose response is dropped, then
  retried, returns the stored result with no second effect; **gate Q** — the same `OperationId` with
  a different payload returns `IDEMPOTENCY_FINGERPRINT_MISMATCH` + one audit row; an expired
  `InProgress` lease is re-claimable while a live one returns `COMMAND_IN_PROGRESS`.
- `DependencyOrderingTests` green: **gate P** — for a dependent pair, all arrival interleavings
  (A→B, B→A, B only, A rejected then B, A conflicted then B) reach the **same** deterministic
  outcome; `ExpectedServerVersion` mismatch → `Conflict`/`AGGREGATE_VERSION_CONFLICT`.
- `CommandPermissionMapTests` green: every `CommandTypes` constant has a `CommandPermissions` entry;
  an unmapped type is denied.
- `SettlementContractTests` green: **gate O** — no `SettlementResult` with
  `Outcome != Accepted` has a null `ErrorCode`, `FinancialEffect == Unknown` without justification,
  or a null `ResponsibleRole`.
- A settled rejection returns HTTP **200** with the rejection inside the body; only transport/shape
  faults return 4xx/5xx with a `WireApiError`.
- Desktop unaffected: `scripts/sandbox/run.sh` = `SMOKE RUN OK`.

## PROGRESS.md update
Mark D05 done; advance "Next phase" to `D06`; append a "Repo state" entry listing: the
`POST /api/commands` + `/batch` + `GET /{operationId}` contract and the **200-for-settled-rejection**
policy (**E06 depends on it**), `IdempotencyRecords` + composite PK + the atomic claim/lease
(migration `104_CommandIntake`), the 9-step intake order, `DependencyResolver`'s five outcomes
(**E06 surfaces them**), `CommandPermissions` fail-closed map (**D06/D07 add one entry per new
command type**), `ServerCommandRegistry` + `IServerCommandHandler<TPayload>` (**D06/D07 implement
only this**), `IServerUnitOfWork`, `SettlementResultFactory` + `ResponsibilityMap` (**the single
producer of the UX contract**), `WireContract.MinSupportedVersion`/`SupportedVersions` (**D08/E03/E09
widen it**), the 8 new error codes, the gate C/D/P/Q/O test names, deviations.

## Existing numeric wire seam — required migration
Implement ARCHITECTURE §PG.7: additive ExpectedServerVersion string?, WireContract v2, explicit
V1 compatibility/no-token policy, UUID AppliedServerVersion receipts, unchanged local numeric
revision/sequence semantics and preserved V1 fingerprints/journal payloads. V1→V2 upgrade and
dependency tests block server integration; no numeric-to-UUID cast or silent overwrite.
