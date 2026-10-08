# Phase D09 — Outbox, workers, resilience

> **PostgreSQL baseline (2026-10-08):** Read `ARCHITECTURE.md` §PG and §16-CT–§18-CT / `STACK.md` §6 before implementation. They govern PostgreSQL 18, Npgsql 10.0.3, UUID Version, tenant journal, snapshot/batch atomicity, pool budgets and deployment-only migrations. This phase is a future instruction, not an implementation claim. Mandatory PostgreSQL CI cannot pass by skipping DB tests.

**Band:** M · **Track:** D · **Depends on:** D07 · **Satisfies:** R-B07, R-B09, R-B25
**Runnable after this phase:** desktop offline yes · server required no

## Read before starting
- `backend-roadmap/CONVENTIONS.md` §2, §3, §4, §5, §8, §9, §12
- `backend-roadmap/PROGRESS.md` (D07 entry — `IServerOutbox`/`NullServerOutbox`,
  `FinanceProjectionHandler`, `IServerStoreMaintenance`, `DailyFinanceFacts`,
  `StockOnHandProjections`; D05 entry — `IServerUnitOfWork`, `IdempotencyRecords` + the lease
  pattern; D04 entry — `ITenantContextSetter`; B02 entry — `OutboxClaim`, `RetryPolicy`,
  `OutboxStatus`)
- `ARCHITECTURE.md` §12 (all), §16.1, §15 · `STACK.md` §5.4, §7
- `A-RULE 025`, `A-RULE 026`, `A-RULE 016`, `A-RULE 034`
- `src/Animora.Persistence.PostgreSql/**`, `src/Animora.Application/Ports/IServerOutbox.cs`

## Goal
Server-side asynchronous work becomes durable and crash-safe: a real transactional outbox written in
the **same transaction** as its business effect, an atomic-claim relay with competing consumers,
idempotent consumers for every effect, bulkheaded background workers that cannot starve request
handling on a 1–3 core box, and a resilience policy set (retry / circuit breaker / timeout /
bulkhead) applied at every boundary.

## Design decisions (pre-made — implement, do not re-derive)
- **`OutboxMessages` table** (`src/Animora.Persistence.PostgreSql/Outbox/ServerOutboxMessage.cs`,
  migration `107_ServerOutbox`) — deliberately the **same shape** as B02's local `OutboxMessage` so
  one mental model covers both sides:
  `Id` (ULID) · `TenantId` · `MessageType` · `PayloadJson` · `Status` (`OutboxStatus`, reused from
  B02) · `CreatedAtUtc` · `ClaimedUntilUtc` · `ClaimToken` · `Attempts` · `NextAttemptUtc` ·
  `LastErrorCode` · `ProcessedAtUtc` · `CorrelationId` · `CausationOperationId`.
  Indexes: `IX_ServerOutboxMessages_Status_NextAttemptUtc`,
  `IX_ServerOutboxMessages_TenantId_MessageType`, `IX_ServerOutboxMessages_CreatedAtUtc`
  (for the retention sweep). The table is in `ServerModel.NonReplicatedLocalTables`' server twin
  (`NonReplicatedProjectionTables`) — never replicated.
- **`ServerOutbox` replaces `NullServerOutbox`** and nothing else changes in D06/D07: the port's
  `EnqueueAsync` still **does not call `SaveChanges`** — it only `Add`s, so the message joins the
  caller's transaction. That single rule is the whole dual-write guarantee (`ARCHITECTURE.md` §12.1);
  `OutboxAtomicityTests` proves that a rolled-back business transaction leaves **zero** outbox rows.
- **Relay state machine, atomic claim mandatory** (§12.1 verbatim):
  `Pending → ClaimedUntil (atomic conditional claim) → Processing → Succeeded | Retry(NextAttemptAt) → DeadLettered`.
  `ServerOutboxClaim` performs ONE PostgreSQL CTE command in a short transaction:
  ```sql
  WITH due AS (
    SELECT "TenantId", "Id" FROM "OutboxMessages"
    WHERE "Status" IN (@pending, @retry, @claimed, @processing)
      AND ("ClaimedUntilUtc" IS NULL OR "ClaimedUntilUtc" < clock_timestamp())
      AND ("NextAttemptUtc" IS NULL OR "NextAttemptUtc" <= clock_timestamp())
    ORDER BY "CreatedAtUtc", "TenantId", "Id"
    LIMIT @max FOR UPDATE SKIP LOCKED
  )
  UPDATE "OutboxMessages" AS o
  SET "Status"=@claimed, "ClaimToken"=@token,
      "ClaimedUntilUtc"=clock_timestamp()+@lease
  FROM due WHERE o."TenantId"=due."TenantId" AND o."Id"=due."Id"
  RETURNING o.*;
  ```
  Status values/lease are typed parameters. Eligibility includes expired Claimed/Processing work,
  not only pending/retry. Completion/renew/retry/dead-letter CAS includes current ClaimToken,
  owner and unexpired lease; paused former owners cannot complete. Claim commits before external
  processing; state-feed extraction never uses SKIP LOCKED. Competing queues need fairness/aging
  and durable predecessor checks per OrderingKey: SKIP LOCKED alone does not prove no starvation.
  Effects+consumer inbox marker commit atomically; external effects need remote stable dedup keys.

- **Consumers are idempotent by construction** (A-RULE 025). `IOutboxConsumer`
  (`src/Animora.Application/Ports/IOutboxConsumer.cs`):
  `string MessageType { get; }` +
  `Task<Result> ConsumeAsync(OutboxMessageRecord message, CancellationToken ct)`.
  Every consumer keys its effect on `message.Id` and records completion in a
  `ConsumedMessages` table (`(ConsumerName, MessageId)` PK, migration `107_ServerOutbox`), checked
  **inside** the consumer's own transaction — so an at-least-once redelivery is a no-op, not a
  second effect. Registered consumers in this phase:
  | Consumer | Consumes | Effect |
  |---|---|---|
  | `FinanceProjectionConsumer` | `finance.posted`, `payment.registered`, `expense.recorded`, `cheque.moved` | drives D07's `FinanceProjectionHandler` → `DailyFinanceFacts` |
  | `StockProjectionConsumer` | `inventory.moved` | recomputes the affected `StockOnHandProjections` row from movements |
  | `AuditMirrorConsumer` | `security.*`, `finance.*` | ensures every outbox-relayed security/financial event has its `ServerAuditEntry` (R-B29) |
  | `AlertConsumer` | `cash.variance.exceeded`, `stock.negative`, `integrity.violation` | writes an `OperatorAlert` row (a new table, consumed by E01) |
  Projection work therefore rides the **outbox**, never the OLTP transaction of a sale (A-RULE 034).
- **Workers** (`src/Animora.Server/Workers/`, all `BackgroundService` — `STACK.md` §5.4, never
  Native AOT):
  | Worker | Interval | Job |
  |---|---|---|
  | `OutboxRelayWorker` | 2 s (idle back-off to 30 s) | claim → dispatch to consumers → settle |
  | `IdempotencyLeaseReaperWorker` | 60 s | releases D05 intake leases whose `ClaimedUntilUtc` has passed and whose row is still `InProgress`, so a killed request never strands a command |
  | `RetentionWorker` | daily (configurable hour) | deletes `Succeeded` outbox rows and `ConsumedMessages` older than `RetentionDays` (default 30); **never** touches `ServerAuditEntries`, `JournalEntries` or any append-only table (a test asserts the allow-list) |
  | `ProjectionRebuildWorker` | on demand only | runs `IServerStoreMaintenance` rebuilds when an `OperatorAlert` of type `projection.drift` exists or an operator requests it (D10 surfaces the request) |
  Every worker step is **resumable**: it claims, does one unit, settles, and repeats — no
  in-memory state survives a restart, and killing the process mid-step leaves the claim to expire
  (A-RULE 026).
- **Bulkhead — the weak-server rule made real** (`ARCHITECTURE.md` §12.3, §16.1, R-B07):
  `src/Animora.Server/Workers/WorkerBulkhead.cs` — a single `SemaphoreSlim` sized
  `max(1, Environment.ProcessorCount / 2 - 1)` (so on the 1–3-core launch profiles exactly one worker unit runs,
  shared with report/background admission … configurable via `WorkerOptions.MaxConcurrency`) that **all** workers
  acquire before doing work, plus a bounded in-process queue (`BoundedChannel`, capacity 256) for
  relay batches. Background work can therefore never occupy every core while requests wait. A
  measured proof is E08's job; a functional proof (concurrency never exceeds the limit) is a test
  here.
- **Resilience policies, one place** (`src/Animora.Server/Resilience/ServerResilience.cs`) using
  `Microsoft.Extensions.Http.Resilience`/`Polly`-style pipelines built **by hand from
  `System.Threading.RateLimiting` + explicit code** — no new package outside `STACK.md` §11:
  | Boundary | Policy |
  |---|---|
  | Consumer dispatch | timeout 30 s → retry with `RetryPolicy.NextDelay` (B02's jittered exponential, reused) → circuit breaker **per `MessageType`** (5 consecutive failures → open 60 s) |
  | Database command | §11.4.1 bounded whole-unit PostgreSQL retry/SQLSTATE classification; no global ORM buffering or stale-token replay |
  | Worker loop | timeout + `CancellationToken` honoring host shutdown within 5 s |
  Circuit state is per message type so one chronically failing effect cannot stall the others
  (§12.3 verbatim). After `MaxAttempts` (10) a message is `DeadLettered` with its last error
  preserved and an `OperatorAlert` raised — **never** silently dropped.
- **Graceful degradation is defined, not emergent** (§12.4): `WorkerOptions.BacklogThreshold`
  (default 5 000 pending outbox rows) → the relay switches to a larger batch size and the
  `/health/ready` payload reports `outbox: degraded`; above `BacklogCriticalThreshold` (20 000) the
  `RetentionWorker` is suspended and an `OperatorAlert` is raised. Every overload path has exactly
  one defined outcome.
- **Tenant context in workers:** a worker processes one message at a time and calls
  `ITenantContextSetter.Set(message.TenantId)` for the duration of that message, resetting
  afterwards in a `finally`. A consumer that touches a tenant-scoped table without a set context
  hits D02's `TenantContextMissingException` — the failure is loud by design.
- **Observability hooks (no external sink, A-RULE 016/032):** each worker logs one structured line
  per pass (claimed / succeeded / retried / dead-lettered counts) with the correlation ids, and
  exposes counters through a local `OutboxDiagnostics` snapshot type that D13's metrics endpoint and
  E01's operator panel read. No metric leaves the box.
- **New error codes:** `OUTBOX_CONSUMER_FAILED`, `OUTBOX_CIRCUIT_OPEN`, `WORKER_BACKLOG_CRITICAL`
  (`OUTBOX_DEAD_LETTERED` exists from B02 — reuse).

## Files to create / modify
| Path | Action | Contents (one line) |
|---|---|---|
| `src/Animora.Application/Ports/IOutboxConsumer.cs` | create | consumer port + `OutboxMessageRecord` |
| `src/Animora.Persistence.PostgreSql/Outbox/ServerOutboxMessage.cs` | create | outbox entity |
| `src/Animora.Persistence.PostgreSql/Outbox/ConsumedMessage.cs` | create | idempotent-consumer ledger |
| `src/Animora.Persistence.PostgreSql/Outbox/ServerOutbox.cs` | create | real `IServerOutbox` (no SaveChanges) |
| `src/Animora.Persistence.PostgreSql/Outbox/ServerOutboxClaim.cs` | create | atomic SKIP LOCKED claim |
| `src/Animora.Persistence.PostgreSql/Outbox/OperatorAlert.cs` | create | alert row (E01 reads it) |
| `src/Animora.Persistence.PostgreSql/Migrations/107_ServerOutbox*` | create | 3 tables + indexes |
| `src/Animora.Server/Consumers/FinanceProjectionConsumer.cs` | create | facts projection |
| `src/Animora.Server/Consumers/StockProjectionConsumer.cs` | create | on-hand recompute |
| `src/Animora.Server/Consumers/AuditMirrorConsumer.cs` | create | audit completeness |
| `src/Animora.Server/Consumers/AlertConsumer.cs` | create | operator alerts |
| `src/Animora.Server/Workers/OutboxRelayWorker.cs` | create | claim → dispatch → settle |
| `src/Animora.Server/Workers/IdempotencyLeaseReaperWorker.cs` | create | releases stranded intake leases |
| `src/Animora.Server/Workers/RetentionWorker.cs` | create | bounded retention, append-only safe |
| `src/Animora.Server/Workers/ProjectionRebuildWorker.cs` | create | on-demand rebuild |
| `src/Animora.Server/Workers/WorkerBulkhead.cs` | create | semaphore + bounded channel |
| `src/Animora.Server/Configuration/WorkerOptions.cs` | create | intervals, thresholds, concurrency |
| `src/Animora.Server/Resilience/ServerResilience.cs` | create | timeout/retry/breaker per boundary |
| `src/Animora.Server/Diagnostics/OutboxDiagnostics.cs` | create | local counters snapshot |
| `src/Animora.Server/Program.cs` | modify | register outbox, consumers, workers, readiness `outbox` check |
| `tests/Animora.Server.Tests/OutboxAtomicityTests.cs` | create | rollback leaves no message |
| `tests/Animora.Server.Tests/OutboxRelayTests.cs` | create | atomic claim, competing consumers, crash mid-step |
| `tests/Animora.Server.Tests/IdempotentConsumerTests.cs` | create | redelivery is a no-op |
| `tests/Animora.Server.Tests/WorkerResilienceTests.cs` | create | breaker per type, dead-letter, bulkhead limit |
| `tests/Animora.Server.Tests/RetentionSafetyTests.cs` | create | append-only tables untouched |

## Implementation steps
1. Add `IOutboxConsumer` + `OutboxMessageRecord`.
2. Add the three tables + `OperatorAlert` and migration `107_ServerOutbox`.
3. Write `ServerOutbox` (no `SaveChanges`) and `ServerOutboxClaim` (the atomic `SKIP LOCKED` claim);
   swap the `NullServerOutbox` registration for it — **no handler file is edited**.
4. Write the four consumers, each with its `ConsumedMessages` guard.
5. Write `WorkerBulkhead`, `WorkerOptions`, `ServerResilience`.
6. Write the four workers; wire the bulkhead, the resilience pipelines and the tenant-context
   set/reset.
7. Add `OutboxDiagnostics` and the `outbox` readiness/degradation reporting.
8. Write the five test classes; the crash test kills the relay mid-`Processing` and asserts the
   claim expires and the message completes exactly once on the next pass.
9. Verify against a real PostgreSQL: a posted invoice produces its finance fact asynchronously; two
   relay instances process a 500-message backlog with **zero** duplicates; a failing consumer opens
   its own circuit without affecting the others; a dead-lettered message raises an alert.
10. `build.sh full` = 0/0; `test.sh` = `TESTS OK`; desktop `run.sh` = `SMOKE RUN OK`.

## Contracts / schema added
- Application: `IOutboxConsumer`, `OutboxMessageRecord`.
- Persistence.PostgreSql: `ServerOutboxMessage`, `ConsumedMessage`, `OperatorAlert`, `ServerOutbox`,
  `ServerOutboxClaim`, migration `107_ServerOutbox`.
- Server: 4 consumers, 4 workers, `WorkerBulkhead`, `WorkerOptions`, `ServerResilience`,
  `OutboxDiagnostics`, the `outbox` readiness check + degradation states.
- `ErrorCodes` += `OUTBOX_CONSUMER_FAILED`, `OUTBOX_CIRCUIT_OPEN`, `WORKER_BACKLOG_CRITICAL`.
- DI: `IServerOutbox` → `ServerOutbox` (the only registration change; D06/D07 handlers untouched).
- No `Animora.Contracts` change, no `CommandTypes` entry, no DI change in `App.axaml.cs`.

## Rules that apply here
- **§12.1 / R-B25** — the message and the business change commit in one transaction; the claim is
  atomic; "dispatched" never means "delivered exactly once".
- **A-RULE 025** — every consumer is idempotent, keyed on the message id, with a persisted
  `ConsumedMessages` guard checked inside its own transaction.
- **A-RULE 026 / R-B09** — every worker step is resumable; a crash mid-step leaves an expiring claim
  and no duplicated effect; the lease reaper recovers stranded intake rows.
- **A-RULE 034 / R-B35** — projections are built off the outbox, never on the OLTP request path.
- **§12.3 / R-B07** — retry + circuit breaker (per message type) + timeout + bulkhead at every
  boundary; background work cannot starve request handling.
- **§12.4** — every overload path has one defined outcome (degraded / suspended / alerted), never
  silent exhaustion.
- **A-RULE 016 / 032** — all diagnostics are local; nothing is exported off the box.

## Out of scope
- Metrics endpoints, rate limiting and the measured capacity harness → D13 (`OutboxDiagnostics` is
  only the data source).
- The operator UI that reads `OperatorAlert` → E01.
- Client-side outbox and command processor → already B02; nothing here touches it.
- File-store reconciliation (its own job) → D12.
- Reopening periods / operator-initiated rebuild requests → D10.

## Acceptance criteria
- Build: `scripts/sandbox/build.sh full` = 0 warnings / 0 errors.
- **Batch intake + outbox index (audit 2026-10-08 · OPT-03, OPT-04):** the outbox relay drains in bounded batches, and the
  `OutboxMessages` table carries a filtered/covering index on `(TenantId, ProcessedAtUtc, Id)` for the pending-scan so the relay's poll
  stays an index seek as the table grows; a test asserts the plan seeks, not scans.
- `OutboxAtomicityTests` green (or skipped with reason): **gate G** — a business transaction that
  rolls back leaves zero outbox rows; one that commits leaves exactly the expected messages; killing
  the process between commit and relay loses nothing.
- `OutboxRelayTests` green: two concurrent relay instances draining 500 messages produce **zero**
  duplicate effects and zero starvation (`SKIP LOCKED` claim proven); a message whose `Processing` step
  is killed is re-claimed after its lease expires and completes exactly once.
- `IdempotentConsumerTests` green: redelivering the same message to each of the four consumers
  produces no second effect and exactly one `ConsumedMessages` row.
- `WorkerResilienceTests` green: a consumer failing 5 times opens **its own** circuit while other
  message types keep flowing; after 10 attempts the message is `DeadLettered` with its last error
  and an `OperatorAlert`; concurrent worker execution never exceeds `WorkerOptions.MaxConcurrency`.
- `RetentionSafetyTests` green: the retention sweep deletes only `Succeeded` outbox rows and old
  `ConsumedMessages`, and leaves `ServerAuditEntries`, `JournalEntries`, `JournalLines`,
  `StockMovements`, `Payments` and `Invoices` untouched.
- `/health/ready` reports `outbox: healthy | degraded | critical` per the thresholds.
- Desktop unaffected: `scripts/sandbox/run.sh` = `SMOKE RUN OK`.

## PROGRESS.md update
Mark D09 done; advance "Next phase" to `D10`; append a "Repo state" entry listing: `OutboxMessages`
+ `ConsumedMessages` + `OperatorAlerts` (migration `107_ServerOutbox`), the real `ServerOutbox` and
the **no-SaveChanges** enqueue rule, `ServerOutboxClaim`'s atomic `SKIP LOCKED` claim, the four
consumers and the `ConsumedMessages` idempotency guard (**D12 adds a consumer the same way**), the
four workers + intervals, `WorkerBulkhead` + `WorkerOptions.MaxConcurrency` (**D13/E08 measure
it**), `ServerResilience`'s per-boundary policies and the per-`MessageType` circuit breaker, the
defined degradation thresholds, `OutboxDiagnostics` (**D13/E01 read it**), the 3 new error codes,
the gate G test name, deviations.

## Inbox retention safety
Outbox retention and consumer dedup retention are not interchangeable. Retain ConsumedMessages
through the maximum supported producer/relay/operator replay horizon plus safety margin; never
blindly prune inbox markers merely because a 30-day succeeded-outbox row expired. Replay outside
that horizon is explicitly blocked or uses permanent effect dedup evidence. Test delayed/operator
replay and stale lease completion without duplicate ledger/projection/external effects.
