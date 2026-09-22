# Phase B02 — Local command pipeline & outbox

**Band:** M · **Track:** B · **Depends on:** B01 · **Satisfies:** R-B02, R-B09, R-B13, R-B14, R-B15, R-B16, R-B25
**Runnable after this phase:** desktop offline yes · server required no

## Read before starting
- `backend-roadmap/CONVENTIONS.md` §5, §6 (items 1–8), §7, §9, §11, §12
- `backend-roadmap/PROGRESS.md`
- `ARCHITECTURE.md` §8.3 (business command architecture), §8.4 (two transports, dependency ordering),
  §12.1 (transactional outbox), §21 (A-RULE 005/006/019/021/022/024/025/026)
- `src/Animora.Application/Abstractions/*`, `src/Animora.Application/Dispatch/Dispatcher.cs`
- `src/Animora.Persistence.Sqlite/Context/AnimoraLocalDbContext.cs`,
  `src/Animora.Persistence.Sqlite/Repositories/LocalUnitOfWork.cs`
- `src/Animora.Contracts.Wire/WireContract.cs`

## Goal
Give every business intent a durable, idempotent, crash-safe local life: a `PendingCommand` table with
a fingerprint and a settlement state machine, a local domain-event outbox with atomic claim, a
resumable processor, and the wire command envelope both sides will speak. Written **in the same
transaction** as the business change, so a kill at any instant loses nothing and duplicates nothing.

## Design decisions (pre-made — implement, do not re-derive)
- **Wire envelope** in `src/Animora.Contracts.Wire/Commands/`:
  - `CommandEnvelope.cs` — `sealed record CommandEnvelope(int ContractVersion, string OperationId,
    string TenantId, string UserId, string DeviceId, string CommandType, string AggregateType,
    string? AggregateId, string PayloadJson, string CommandFingerprint,
    DateTimeOffset CreatedAtUtc, long LocalSequence, string? CausationId,
    string? DependsOnOperationId, long? ExpectedAggregateVersion)`.
    `ContractVersion` defaults to `WireContract.Version`.
  - `SettlementResult.cs` — `sealed record SettlementResult(string OperationId,
    SettlementOutcome Outcome, string? ErrorCode, string? Detail, string? ServerEntityId,
    string? ServerVersion, DateTimeOffset SettledAtUtc, FinancialEffect FinancialEffect,
    string? ResponsibleRole)`.
    `enum SettlementOutcome { Accepted, Rejected, Conflict, Deferred }`.
    `enum FinancialEffect { None, Applied, NotApplied, Unknown }`.
    The last three fields are the machine-readable half of the sync-failure UX contract (A-RULE 024).
  - `WireApiError.cs` — `sealed record WireApiError(string Code, string? Detail,
    IReadOnlyList<WireFieldError> Fields, string CorrelationId)`; `WireFieldError(string Field,
    string Code)`. This is the **only** error shape crossing the wire.
  - `CommandTypes.cs` — `const string` catalog of every command type name
    (`"sales.invoice.post"`, `"sales.payment.register"`, `"inventory.stock.adjust"`, …). A later phase
    **adds** entries; none renames one. Format: `<context>.<aggregate>.<verb>`, lowercase, dot-separated.
- **Local tables** (all `[LocalOnly]`, in `src/Animora.Persistence.Sqlite/Entities/`):
  - `PendingCommand` — `Id` (ULID), `OperationId` (ULID, unique), `TenantId`, `UserId`, `DeviceId`,
    `CommandType`, `AggregateType`, `AggregateId`, `PayloadJson`, `CommandFingerprint`
    (SHA-256 hex of `CommandType + '\u001f' + canonical payload JSON`), `LocalSequence` (`long`,
    monotonic per installation from `LocalSequenceAllocator`), `Status` (`SettlementState`),
    `CausationId`, `DependsOnOperationId`, `ExpectedAggregateVersion`, `CreatedAtUtc`,
    `LastAttemptUtc`, `Attempts`, `NextAttemptUtc`, `LastErrorCode`, `LastErrorDetail`,
    `FinancialEffect`, `ResponsibleRole`, `SettledAtUtc`, `ServerEntityId`, `ServerVersion`,
    `ClaimedUntilUtc`, `ClaimToken`.
    Indexes: `IX_PendingCommands_OperationId` (unique),
    `IX_PendingCommands_Status_NextAttemptUtc`, `IX_PendingCommands_LocalSequence` (unique),
    `IX_PendingCommands_AggregateType_AggregateId`, `IX_PendingCommands_DependsOnOperationId`.
  - `OutboxMessage` — `Id`, `MessageType`, `PayloadJson`, `Status` (`OutboxStatus`),
    `CreatedAtUtc`, `ClaimedUntilUtc`, `ClaimToken`, `Attempts`, `NextAttemptUtc`,
    `LastErrorCode`, `ProcessedAtUtc`, `CorrelationId`.
    Indexes: `IX_OutboxMessages_Status_NextAttemptUtc`, `IX_OutboxMessages_MessageType`.
  - `LocalSequenceCounter` — single row (`Name`, `Value`) used by `LocalSequenceAllocator`.
  - `enum SettlementState { Draft, LocallyPosted, PendingSettlement, Settled, Rejected, Conflict,
    Reversed, NeedsAttention }` — in `Animora.Domain/Primitives/SettlementState.cs` (the domain owns
    the vocabulary; the table stores it as `int`).
  - `enum OutboxStatus { Pending, Claimed, Processing, Succeeded, Retry, DeadLettered }`.
  - Migration `004_CommandPipeline`.
- **Settlement state machine** — `Animora.Domain/Primitives/SettlementTransitions.cs`,
  a static allow-table (the only authority; both client and server consult it):
  ```
  Draft            -> LocallyPosted | Rejected
  LocallyPosted    -> PendingSettlement | Rejected | Reversed
  PendingSettlement-> Settled | Rejected | Conflict | NeedsAttention | PendingSettlement (retry)
  Conflict         -> NeedsAttention | Reversed | Settled
  Rejected         -> NeedsAttention | Reversed
  NeedsAttention   -> Reversed | Settled | Rejected
  Settled          -> Reversed
  Reversed         -> terminal
  ```
  `static Result EnsureAllowed(SettlementState from, SettlementState to)` →
  `SETTLEMENT_INVALID_TRANSITION`.
- `Application/Commands/ICommandJournal.cs` (port) + `Persistence.Sqlite/Commands/CommandJournal.cs`:
  - `Task<Result<PendingCommandRecord>> EnqueueAsync(CommandEnvelope envelope, CancellationToken ct)`
    — **does not call SaveChanges**; it only `Add`s, so the enqueue joins the caller's business
    transaction (this is the crash-safety guarantee, ARCHITECTURE.md §12.1).
  - `Task<Result<PendingCommandRecord?>> FindByOperationIdAsync(string operationId, ...)`.
  - `Task<Result> ApplySettlementAsync(SettlementResult result, CancellationToken ct)` — validates the
    transition, stores outcome/effect/role, and on `Accepted` records `ServerEntityId`/`ServerVersion`.
  - `Task<Result<IReadOnlyList<PendingCommandRecord>>> ClaimBatchAsync(int max, TimeSpan lease,
    CancellationToken ct)` — the **atomic claim**: a single
    `UPDATE PendingCommands SET ClaimToken=@t, ClaimedUntilUtc=@until, Status=<PendingSettlement>
     WHERE Id IN (SELECT Id FROM PendingCommands WHERE Status IN (...) AND (ClaimedUntilUtc IS NULL OR
     ClaimedUntilUtc < @now) AND (NextAttemptUtc IS NULL OR NextAttemptUtc <= @now)
     ORDER BY LocalSequence LIMIT @max)` executed via `ExecuteSqlInterpolatedAsync`, then a read of the
    claimed token's rows. An expired lease is reclaimable — a crashed processor never strands work
    (A-RULE 026).
  - `Task<Result> ReleaseAsync(string operationId, ErrorCode error, TimeSpan backoff, ...)`.
  - `Task<Result<IReadOnlyList<PendingCommandRecord>>> GetUnsettledForAggregateAsync(string
    aggregateType, string aggregateId, ...)` — the guard E05 uses to protect unsent intent.
  - `PendingCommandRecord` is a `sealed record` projection (never the EF entity) in
    `Animora.Application/Commands/`.
- **Idempotency & fingerprint:** `Application/Commands/CommandFingerprint.cs` —
  `static string Compute(string commandType, string payloadJson)` over a **canonical** JSON form
  (`JsonSerializerOptions` with sorted property names, no indentation, invariant culture, `Money`
  written as `{minor,currency}`). `EnqueueAsync` with an existing `OperationId`:
  identical fingerprint → returns the stored record (replay, success);
  different fingerprint → `Result.Fail(IDEMPOTENCY_FINGERPRINT_MISMATCH)` **and** an audit hook call
  (`IIntegrityAlertSink`, implemented in B05) — A-RULE 019.
- **Command-emitting behavior:** `Application/Behaviors/CommandJournalBehavior.cs`, inserted into the
  B01 chain **between** `TransactionBehavior` and the handler, active only for commands implementing
  `IJournaledCommand` (`string CommandTypeName { get; }`, `string AggregateTypeName { get; }`,
  `string? AggregateIdOrNull { get; }`). It builds the envelope from `CommandContext` + the serialized
  command, calls `EnqueueAsync`, and lets the handler run; both writes commit together.
  A command **not** marked `IJournaledCommand` (pure local concerns: UI prefs, profile settings) never
  touches the journal — this is where "replication ≠ intent" is enforced at the code level.
- **Outbox:** `Application/Outbox/IOutbox.cs` (`Task<Result> EnqueueAsync(string messageType,
  object payload, CancellationToken ct)`, same no-SaveChanges rule) +
  `Persistence.Sqlite/Outbox/LocalOutbox.cs` + `Outbox/OutboxClaim.cs` (same atomic-claim SQL shape as
  above, `lease` default 60 s). `DomainEventDispatcher` (A10) gains an `OutboxRelayHandler` that writes
  every raised domain event to `OutboxMessage` — so a local projection consumer can be added later
  without touching aggregates.
- **Processor:** `Application/Commands/LocalCommandProcessor.cs` — `sealed class` with
  `Task<Result<CommandProcessorPass>> RunPassAsync(ICommandTransport transport, CancellationToken ct)`:
  claim → order by `LocalSequence` → for each, if `DependsOnOperationId` is unsettled, skip with
  `DEPENDENCY_UNSETTLED` (never reorder locally — the server decides, A-RULE 021/022) → submit via
  `ICommandTransport` → `ApplySettlementAsync`. `ICommandTransport` is a port with a
  `NullCommandTransport` default that returns `Deferred` for everything (so the whole pipeline is
  testable and correct offline today; E06 supplies the real one).
  Backoff: `NextAttemptUtc = now + min(2^Attempts seconds, 15 min) ± 20% jitter`
  (`RetryPolicy.NextDelay(int attempts)` in `Application/Commands/RetryPolicy.cs`).
  After 10 attempts → `NeedsAttention` with the last error preserved (never silently dropped).
- **Query surface for the UI seam:** `Application/UseCases/Sync/PendingCommandSummaryQuery.cs` →
  `PendingCommandSummary(int Total, int PendingSettlement, int Rejected, int Conflict,
  int NeedsAttention, DateTimeOffset? OldestCreatedUtc)`; and
  `UnsettledCommandListQuery` returning a `PagedResult<PendingCommandRecord>`. C07/C08/E06 bind to
  these; nothing in the UI reads the table directly.
- **New error codes:** `SETTLEMENT_INVALID_TRANSITION`, `COMMAND_ALREADY_SETTLED`,
  `COMMAND_CLAIM_LOST`, `OUTBOX_DEAD_LETTERED`, `DEPENDENCY_UNSETTLED` (exists from A02 — reuse).

## Files to create / modify
| Path | Action | Contents (one line) |
|---|---|---|
| `src/Animora.Contracts.Wire/Commands/CommandEnvelope.cs` | create | the wire command envelope |
| `src/Animora.Contracts.Wire/Commands/SettlementResult.cs` | create | outcome + financial effect + responsible role |
| `src/Animora.Contracts.Wire/Commands/CommandTypes.cs` | create | command-type name catalog |
| `src/Animora.Contracts.Wire/Errors/WireApiError.cs` | create | the single wire error shape |
| `src/Animora.Domain/Primitives/SettlementState.cs` | create | the 8 settlement states |
| `src/Animora.Domain/Primitives/SettlementTransitions.cs` | create | allow-table + `EnsureAllowed` |
| `src/Animora.Persistence.Sqlite/Entities/PendingCommand.cs` | create | durable command row |
| `src/Animora.Persistence.Sqlite/Entities/OutboxMessage.cs` | create | outbox row |
| `src/Animora.Persistence.Sqlite/Entities/LocalSequenceCounter.cs` | create | monotonic sequence row |
| `src/Animora.Persistence.Sqlite/Configurations/Pipeline/*.cs` | create | 3 configurations + indexes |
| `src/Animora.Persistence.Sqlite/Commands/CommandJournal.cs` | create | enqueue/claim/settle/release |
| `src/Animora.Persistence.Sqlite/Commands/LocalSequenceAllocator.cs` | create | atomic counter increment |
| `src/Animora.Persistence.Sqlite/Outbox/LocalOutbox.cs` | create | outbox enqueue |
| `src/Animora.Persistence.Sqlite/Outbox/OutboxClaim.cs` | create | atomic claim SQL |
| `src/Animora.Persistence.Sqlite/Outbox/OutboxRelayHandler.cs` | create | domain event → outbox row |
| `src/Animora.Application/Commands/ICommandJournal.cs` | create | journal port |
| `src/Animora.Application/Commands/PendingCommandRecord.cs` | create | projection record |
| `src/Animora.Application/Commands/CommandFingerprint.cs` | create | canonical-JSON SHA-256 |
| `src/Animora.Application/Commands/IJournaledCommand.cs` | create | marker for intent-carrying commands |
| `src/Animora.Application/Commands/ICommandTransport.cs` | create | transport port + `NullCommandTransport` |
| `src/Animora.Application/Commands/LocalCommandProcessor.cs` | create | claim → submit → settle pass |
| `src/Animora.Application/Commands/RetryPolicy.cs` | create | exponential backoff + jitter |
| `src/Animora.Application/Outbox/IOutbox.cs` | create | outbox port |
| `src/Animora.Application/Behaviors/CommandJournalBehavior.cs` | create | in-transaction enqueue behavior |
| `src/Animora.Application/UseCases/Sync/PendingCommandSummaryQuery.cs` | create | summary query + handler |
| `src/Animora.Application/UseCases/Sync/UnsettledCommandListQuery.cs` | create | paged list query + handler |
| `src/Animora.Application/Dispatch/Dispatcher.cs` | modify | insert `CommandJournalBehavior` in the chain |
| `src/Animora.Persistence.Sqlite/Migrations/*` | create | `004_CommandPipeline` + snapshot |
| `src/Animora.Domain/Results/ErrorCodes.cs` | modify | append 4 codes |
| `tests/Animora.Application.Tests/IdempotencyTests.cs` | create | replay returns stored result; mismatch = integrity error |
| `tests/Animora.Application.Tests/SettlementTransitionTests.cs` | create | every state pair against the allow-table |
| `tests/Animora.Persistence.Tests/CommandJournalTests.cs` | create | atomic claim, lease expiry reclaim, in-transaction enqueue |
| `tests/Animora.Persistence.Tests/OutboxClaimTests.cs` | create | concurrent claims never overlap; dead-letter after max attempts |
| `tests/Animora.Application.Tests/CommandProcessorTests.cs` | create | ordering by `LocalSequence`, dependency skip, backoff, `NeedsAttention` |

## Implementation steps
1. Add the wire envelope, settlement result, error shape and `CommandTypes` catalog.
2. Add `SettlementState` + `SettlementTransitions` to the Domain.
3. Add the three local-only entities, their configurations and migration `004_CommandPipeline`.
4. Implement `LocalSequenceAllocator` (`UPDATE … SET Value = Value + 1 RETURNING Value` in one
   statement) and `CommandFingerprint` with the canonical serializer options.
5. Implement `CommandJournal`: `EnqueueAsync` (no `SaveChanges`), replay/mismatch logic,
   `ApplySettlementAsync` via `SettlementTransitions`, `ClaimBatchAsync` with the single atomic
   `UPDATE`, `ReleaseAsync`, `GetUnsettledForAggregateAsync`.
6. Implement `LocalOutbox` + `OutboxClaim` + `OutboxRelayHandler`, and register the relay handler in
   the A10 dispatcher map.
7. Add `IJournaledCommand`, `CommandJournalBehavior`, and insert it into the chain **inside** the
   transaction behavior. Update `BehaviorPipelineTests` (B01) to expect the new position.
8. Implement `RetryPolicy`, `ICommandTransport`/`NullCommandTransport`, `LocalCommandProcessor`.
9. Add the two UI-seam queries and register them.
10. Append the error codes; write the five test classes.
11. `CommandJournalTests` must prove crash safety: enqueue + business write in one transaction, then
    abort → **neither** row exists; commit → **both** exist.
12. `build.sh full` = 0/0; `test.sh` = `TESTS OK`; `run.sh` = `SMOKE RUN OK`.

## Contracts / schema added
- Wire: `CommandEnvelope`, `SettlementResult`, `SettlementOutcome`, `FinancialEffect`, `WireApiError`,
  `WireFieldError`, `CommandTypes`.
- Domain: `SettlementState`, `SettlementTransitions`.
- Local tables: `PendingCommands`, `OutboxMessages`, `LocalSequenceCounters` (migration
  `004_CommandPipeline`).
- Ports/impl: `ICommandJournal`/`CommandJournal`, `IOutbox`/`LocalOutbox`, `OutboxClaim`,
  `OutboxRelayHandler`, `ICommandTransport`/`NullCommandTransport`, `LocalCommandProcessor`,
  `RetryPolicy`, `CommandFingerprint`, `IJournaledCommand`, `CommandJournalBehavior`,
  `PendingCommandRecord`, `LocalSequenceAllocator`.
- Queries: `PendingCommandSummaryQuery`, `UnsettledCommandListQuery`. `ErrorCodes` += 4.
- No `Animora.Contracts` change, no DI change in `App.axaml.cs`. `WireContract.Version` stays `1`.

## Rules that apply here
- **A-RULE 005/006 / R-B14** — every journaled command carries an `OperationId`; a replay returns the
  stored record and never produces a second effect.
- **A-RULE 019** — same `OperationId` + different fingerprint = `IDEMPOTENCY_FINGERPRINT_MISMATCH`,
  raised to the integrity sink, never treated as a replay.
- **A-RULE 021/022 / R-B15** — the processor never reorders; an unsettled dependency defers with
  `DEPENDENCY_UNSETTLED`, and final ordering is the server's decision.
- **A-RULE 024 / R-B16** — `SettlementResult` carries `FinancialEffect` + `ResponsibleRole`; a bare
  failure is impossible by shape.
- **A-RULE 025/026 / R-B25** — the claim is atomic with a lease, so a crash mid-pass is resumable and
  never duplicates an effect.
- **ARCHITECTURE.md §12.1** — enqueue never calls `SaveChanges`; it rides the business transaction.
- **CONVENTIONS §6 item 1** — only `IJournaledCommand`s enter the journal; replication never carries
  intent.

## Out of scope
- Real transport to the server → E06. Server-side intake and idempotency store → D05.
- Real login / user identity for the envelope's `UserId` → B03.
- Audit entries and the integrity-alert implementation → B05.
- Surfacing settlement states in specific screens → C07, C08, E06.
- Background scheduling of `RunPassAsync` → E04 (sync orchestration).

## Acceptance criteria
- Build: `scripts/sandbox/build.sh full` = 0 warnings / 0 errors.
- `dotnet ef migrations list` ends with `004_CommandPipeline`; earlier migrations untouched.
- `IdempotencyTests` green: re-enqueueing the same `OperationId` + payload returns the stored record
  and adds no row; a changed payload returns `IDEMPOTENCY_FINGERPRINT_MISMATCH` and calls the
  integrity sink exactly once.
- `SettlementTransitionTests` green: all 64 state pairs evaluated; only the tabled ones succeed.
- `CommandJournalTests` green: enqueue + business write share one transaction (abort loses both,
  commit persists both); `ClaimBatchAsync` under two concurrent callers returns disjoint sets; an
  expired lease is reclaimed by the next pass.
- `OutboxClaimTests` green: concurrent claims never overlap; a message dead-letters after the max
  attempts with its last error preserved.
- `CommandProcessorTests` green: commands are submitted in `LocalSequence` order; a command whose
  `DependsOnOperationId` is unsettled is skipped with `DEPENDENCY_UNSETTLED`; backoff grows
  exponentially with jitter within ±20 %; attempt 11 moves the row to `NeedsAttention`.
- With `NullCommandTransport`, a full pass leaves every command in `PendingSettlement` and reports
  `Deferred` — the desktop remains fully functional offline.
- Runnability (`CONVENTIONS.md` §11): `scripts/sandbox/run.sh` = `SMOKE RUN OK`.

## PROGRESS.md update
Mark B02 done; advance "Next phase" to `B03`; append a "Repo state" entry listing: the wire envelope +
settlement result + `CommandTypes`, `SettlementState`/`SettlementTransitions`, the three local tables
and migration `004_CommandPipeline`, `CommandJournal` (atomic claim + fingerprint rules), the outbox and
relay handler, `LocalCommandProcessor` + `RetryPolicy`, `CommandJournalBehavior`'s chain position, the
two UI-seam queries, `ErrorCodes` += 4, the 5 test classes, no DI change, no mock deleted, deviations.
