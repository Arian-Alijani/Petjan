# Phase S13 — Outbox, background workers & resilience (atomic claim, fenced lease, breaker, bulkhead)

**Band:** M · **Track:** S (server) · **Depends on:** S10 · **Next:** S14
**Satisfies:** R-B07, R-B09, R-B25 · **Runnable after this phase:** desktop offline yes · server required no
**Produced by:** M00 (regenerated 2026-10-08; supersedes the deleted `PHASE-D09` file).

> Baseline verified 2026-10-08: client migration head `031` (`031_TreatmentPlans`), `CommandTypes` = live `CommandTypes.cs` (88 declared today — never freeze), `ErrorCodes` 161, `WireContract.Version` = 1; `src/` projects = `Animora.Domain`, `Animora.Application`, `Animora.Persistence.Sqlite`, `Animora.Infrastructure.Local`, `Animora.Client.Services`, `Animora.Contracts`, `Animora.Contracts.Wire`, `Animora.Desktop`, `Animora.Sync.Abstractions` (+ server-track projects added by earlier S/G phases).
> **Next-free-ordinal rule.** Never hard-code a migration number from this file. List the physical files of the owning `DbContext`'s `Migrations/` folder and take the next free ordinal (server sequence starts at **101**; the client SQLite sequence — head `031` today — is independent and untouched by server phases). Match on the `NNN` token, not a file-name prefix.

---

## ⚡ Zero-exploration contract (read this, then implement top-to-bottom)

> This file is self-contained and pre-verified. Every existing type, signature, path and command you need is in
> **§Reference pack**; every new name is fixed in **§Design decisions**. Do **not** re-open the repo to "understand" it and
> do not re-derive decisions. If — and only if — a compile error proves a snippet diverges from the tree, fix the smallest
> thing and log it in the `PROGRESS.md` deviation log. Names here are the real ones; there is no alias to resolve.
> Implement exactly this scope: nothing from a later phase, nothing "while we're here".

**Environment (do not probe):**
- Build / test / run only through the sandbox scripts (memory-capped ~985 MiB; never raw `dotnet build`):
  `scripts/sandbox/build.sh [fast|full]` (expect 0 warnings / 0 errors), `scripts/sandbox/test.sh [filter]`,
  `scripts/sandbox/run.sh` (desktop smoke → `SMOKE RUN OK`). Run `scripts/sandbox/install-prereqs.sh` once (idempotent).
- `dotnet-ef` lives at `.sandbox/tools/dotnet-ef` — use it; **never** `dotnet tool install`.
- Server stack is locked (`STACK.md` §0/§5/§6/§11): ASP.NET Core 10 + Kestrel, EF Core **10.0.12** line,
  PostgreSQL 18 via `Npgsql.EntityFrameworkCore.PostgreSQL` **10.0.3** / `Npgsql` **10.0.3** (only in `Animora.Persistence.PostgreSql`).
- PostgreSQL integration tests read the connection string from env var `ANIMORA_PG_TEST_CONNECTION`; when unset they
  **skip with a reason** locally (CONVENTIONS §1) and **fail** under `ANIMORA_REQUIRE_PG=1` (mandatory CI/release gate, owned by G07).
- **Forbidden tech (restated, binding):** Redis, message broker, cloud IdP, cloud telemetry, CDN, EF Core 11,
  per-tenant database, hard-coded TLS version (`SslProtocols.None` only), Native AOT with EF Core, `UpdatedAt`/`nextval`/`xmin` sync cursors.

**Offline invariant.** The desktop must keep starting and operating fully offline with no server, no PostgreSQL and no network (Standing principle 1, CONVENTIONS §11). This phase adds nothing the desktop needs at startup.

---

## Read before starting
- `backend-roadmap/PROGRESS.md` — top section only.
- `ARCHITECTURE.md` §12 (all), §PG.4 — only these `§`.
- `STACK.md` §5.4 — only this `§`.
- `src/Animora.Application/Outbox/IOutbox.cs` (reused), `src/Animora.Server/Commands/*` (S06 seams `IServerOutbox`, `IntakeRecoveryService`, `CommandIntakeCompactor`) and `src/Animora.Persistence.PostgreSql/Replication/{FeedRetentionService,IBackgroundAdmission}.cs` (S12).

(Nothing else. `Animora-UI/**`, `Database-Sampel/**`, other phase files and `windows-frontend-roadmap/phases/**` are out of bounds.)

## Goal

Add the server reliability backbone: a transactional **server outbox** written in the same transaction as the state change, an **atomic claim** relay (`UPDATE … RETURNING` over `FOR UPDATE SKIP LOCKED` with a fenced lease token and due/expired eligibility), idempotent consumers, capped-exponential back-off with jitter, an in-house circuit breaker + timeouts, and a bounded-concurrency **worker host** that runs the recovery/retention/GC jobs left as seams by S06/S12 plus the in-process SignalR "feed advanced" hint. Workers can never starve request handling (bulkhead).

---

## §Reference pack

```csharp
namespace Animora.Application.Outbox;
public interface IOutbox { Task<Result> EnqueueAsync(string messageType, object payload, CancellationToken ct = default); }   // (B02) client LocalOutbox implements it; the server gets its OWN implementation below
// S06 seams this phase implements/uses:  interface IServerOutbox (no-op registered by S06) ; IntakeRecoveryService.RecoverAsync(row) ; CommandIntakeCompactor.CompactAsync(cutoff) ; ICommandIntakeStore.ListExpiredLeasesAsync(limit ≤ 50)
// S12 seams: IBackgroundAdmission (SemaphoreSlim(1)) ; FeedRetentionService.RunAsync(ct) ; RetentionPolicy (JournalRetentionDays = MaxOfflineDays + 30)
// Client reference implementation to MIRROR semantically (read ONLY if a detail is unclear): src/Animora.Persistence.Sqlite/Outbox/{LocalOutbox,OutboxClaim,OutboxRelayHandler}.cs  (SQLite claim state machine: Pending→Claimed→Done|DeadLettered, ClaimToken, ClaimedUntilUtc, RetryPolicy)
// Application.Commands.RetryPolicy (B02) exists for the client; the server uses its own ServerRetryPolicy below (no cross-dependency on SQLite types).
```
```csharp
// Animora.Domain.Results (A02) — reuse, never redefine
public readonly record struct ErrorCode(string Code, string Category)   // categories: Validation|NotFound|Conflict|Forbidden|Integrity|Infrastructure|Entitlement
{ public const string Validation="Validation", NotFound="NotFound", Conflict="Conflict", Forbidden="Forbidden", Integrity="Integrity", Infrastructure="Infrastructure", Entitlement="Entitlement";
  public static readonly ErrorCode None; public bool IsNone { get; } }
public readonly record struct Result      { bool IsSuccess, IsFailure; ErrorCode Error; string? Detail; static Result Ok(); static Result Fail(ErrorCode e, string? detail = null); }
public readonly record struct Result<T>   { bool IsSuccess, IsFailure; T? Value; ErrorCode Error; string? Detail; static Result<T> Ok(T v); static Result<T> Fail(ErrorCode e, string? detail = null); Map/Bind/OrElse; implicit operator Result<T>(T) }
// ErrorCodes (161 today): `public static readonly ErrorCode X = new("X_CODE", ErrorCode.<Kind>);` — inside `#region <context>`. Server phases ADD a new #region; never rename/remove.
// Already declared & reusable: ValidationFailed, NotFound, ConcurrencyConflict("CONCURRENCY_CONFLICT",Conflict), PermissionDenied, PermissionUnknown, TenantMismatch("TENANT_MISMATCH",Forbidden),
//   IdempotencyFingerprintMismatch(Integrity), DependencyUnsettled("DEPENDENCY_UNSETTLED",Conflict), EntitlementRequired(Entitlement), TenantSuspended, UserDisabled, UserLockedOut, DeviceRevoked,
//   CredentialsInvalid, SessionExpired, PasswordPolicyViolation, SettlementInvalidTransition, CommandAlreadySettled, HandlerNotRegistered, DispatchFailed, Unexpected
```

## §Design decisions

- **Table (migration `<next free ordinal>_ServerOutbox`):** `ServerOutbox(TenantId varchar(26), Id varchar(26), MessageType varchar(64), PayloadJson jsonb, Status smallint (Pending=0, Claimed=1, Done=2, DeadLettered=3), Attempts int, NextAttemptUtc timestamptz(6) NOT NULL, ClaimToken varchar(26)?, ClaimedUntilUtc?, LastError varchar(512)?, CreatedUtc, CompletedUtc?, CorrelationId varchar(64), PRIMARY KEY (TenantId, Id))`; indexes `IX_ServerOutbox_Status_NextAttemptUtc` (partial `WHERE Status IN (0,1)`; **not** tenant-led because the relay is a global worker claiming across tenants — the one documented exception, the claim query itself is bounded `LIMIT n`), `IX_ServerOutbox_TenantId_CreatedUtc`. `ConsumedMessages(ConsumerName varchar(48), TenantId, MessageId, ConsumedUtc, PRIMARY KEY(ConsumerName, TenantId, MessageId))` — the idempotent-consumer marker. Both non-syncable; the relay uses a **no-tenant-filter dedicated context** (`OutboxDbContext`, own narrow model: only these two tables; it never sees business tables) — never `IgnoreQueryFilters`.
- **Enqueue:** `ServerOutbox : IServerOutbox` (S06 seam, scoped) inserts through the **same** `AnimoraServerDbContext` transaction as the command's effect (so S06's "Settled ⇔ effect ⇔ outbox row"). Message types (constants in `OutboxMessageTypes`): `feed.advanced` (payload tenantId, lastSequence — emitted once per committing transaction by the intake after Settled), `audit.flush` reserved, `license.recheck` (S16), `subscription.expiry` (S14). Unknown types are dead-lettered, never dropped.
- **Atomic claim (ARCH §12.2) — single statement:** `WITH due AS (SELECT "TenantId","Id" FROM "ServerOutbox" WHERE ("Status" = 0 AND "NextAttemptUtc" <= @now) OR ("Status" = 1 AND "ClaimedUntilUtc" <= @now) ORDER BY "NextAttemptUtc" LIMIT @n FOR UPDATE SKIP LOCKED) UPDATE "ServerOutbox" o SET "Status" = 1, "ClaimToken" = @token, "ClaimedUntilUtc" = @now + @lease, "Attempts" = o."Attempts" + 1 FROM due WHERE o."TenantId" = due."TenantId" AND o."Id" = due."Id" RETURNING o.*` — **the eligibility predicate explicitly handles a NULL lease** (`"ClaimedUntilUtc" IS NULL` rows have `Status=0`, so the `Status=1 AND ClaimedUntilUtc <= now` branch can't accidentally exclude them; a test covers a Status=1 row with NULL lease → eligible). Lease default 60 s; completion/failure updates are **fenced** `WHERE "ClaimToken" = @token` — a worker that lost its lease gets 0 rows ⇒ `ErrorCodes.CommandClaimLost`-style `OutboxClaimLost` log + no effect. `SKIP LOCKED` is for **work distribution only**, never the state-feed cursor.
- **Retry / dead-letter:** `ServerRetryPolicy`: base 2 s, factor 2, cap 15 min, **full jitter** (`random(0, delay)` from `Random.Shared`), max attempts 8 → `DeadLettered` (+ `OutboxDeadLettered` error code already exists in the catalog) and an audit row. Delay is a pure function `ServerRetryPolicy.NextDelay(attempt, random)` (unit-testable, no clocks).
- **Resilience envelope (in-house, no new package):** `CircuitBreaker` (`Closed/Open/HalfOpen`, failure threshold 5 in 60 s, open 30 s, single half-open probe, `TimeProvider`-driven) around each **outbound** consumer call; `OutboundTimeout` (default 10 s via linked `CancellationTokenSource`); bulkhead = `BoundedWorkerQueue` (`Channel<T>` bounded, `FullMode=Wait`, capacity 64) + `MaxConcurrentConsumers = 2`; all long jobs take `IBackgroundAdmission` (shared `SemaphoreSlim(1)`) so projection/export/GC/recovery never run simultaneously and **never take the OLTP request pool** (they use the dedicated `worker` data source budget; S02 `DatabaseOptions` gains `WorkerMax` default 2 — **update the pool-sum validator: write+read+keyring+worker ≤ 12 (2 GiB) / 20 (4 GiB)**; defaults rebalanced to 6/3/1/2).
- **Idempotent consumers:** each consumer inserts `(ConsumerName, TenantId, MessageId)` into `ConsumedMessages` and performs its effect in **one transaction**; unique violation ⇒ already done ⇒ ack. Consumer registry `IOutboxConsumer { string MessageType; string Name; Task<Result> ConsumeAsync(OutboxMessage m, CancellationToken ct); }` (first consumer: `FeedAdvancedHintConsumer`).
- **Workers (`BackgroundService`, JIT — **never Native AOT with EF Core**, STACK §5.4), all registered in `ServerWorkersServiceCollectionExtensions.AddAnimoraWorkers()`:** `OutboxRelayWorker` (claims ≤ 20 every 1 s idle-backoff to 15 s), `IntakeRecoveryWorker` (30 s, ≤ 50 rows, S06), `CommandIntakeRetentionWorker` (daily, `CommandIntakeCompactor.CompactAsync(now − FullRecordDays)`), `FeedRetentionWorker` (hourly, `FeedRetentionService.RunAsync`), `OutboxCleanupWorker` (deletes `Done` older than 7 d). Each loop: `PeriodicTimer`, catches everything except cancellation, logs once per failure class (rate-limited), exposes `WorkerHeartbeat` (last success UTC) to `/health/ready` as **non-fatal** probe `workers` (stale > 5× period ⇒ degraded, reported by name only). Graceful stop: finish the current claimed item within 10 s or let the lease expire.
- **SignalR hint (in-process, no backplane):** `SyncHintHub` mapped at `/sync/hub` (authorized, group = tenant id from S05 context — **server-chosen, a client cannot join another group**); `FeedAdvancedHintConsumer` sends `FeedAdvanced(lastSequence)` to the tenant group. It is a **hint only**: clients must still pull; losing a hint loses nothing. Redis/backplane is forbidden (ROADMAP §5).
- **Crash resilience:** claims are leases, so a killed process's claimed rows are re-claimable after `ClaimedUntilUtc`; provisional state is never lost (intake `InProgress` rows recovered by the S06 worker).
- **New ErrorCodes:** none (reuses `OutboxDeadLettered`, `CommandClaimLost`).

## §Implementation plan

1. Persistence: entities `OutboxMessage`, `ConsumedMessage`; `OutboxDbContext` (own narrow model; the tables are created by the main context's migration (same history), `OutboxDbContext` only maps them for read/write and is never used for migrations); `ServerOutbox` (enqueue), `OutboxClaimStore` (claim SQL via `FromSqlInterpolated`/raw `NpgsqlCommand` returning rows), `ServerRetryPolicy`, `CircuitBreaker`, `OutboundTimeout`, `BoundedWorkerQueue`, `OutboxMessageTypes`, `IOutboxConsumer`.
2. Migration: `.sandbox/tools/dotnet-ef migrations add ServerOutbox --project src/Animora.Persistence.PostgreSql --startup-project src/Animora.Persistence.PostgreSql --context AnimoraServerDbContext --output-dir Migrations/Server`; rename to the next free ordinal; update `ServerSchemaVersion.ExpectedHead`. Edit S02's `DatabaseOptions` + validator + keyed data sources for `WorkerMax`.
3. Server: `Workers/*.cs` (the five workers + heartbeat probe), `Hints/SyncHintHub.cs`, `Hints/FeedAdvancedHintConsumer.cs`, DI `AddAnimoraWorkers()`; replace S06's no-op `IServerOutbox` registration; intake emits `feed.advanced` on Accepted outcomes that touched replicated tables (flag from the unit of work: any `TenantJournal` insert in the transaction ⇒ enqueue one message).
4. Tests (below). `scripts/sandbox/build.sh full` → 0/0; `scripts/sandbox/test.sh Server` → green; `scripts/sandbox/run.sh` → `SMOKE RUN OK`.
5. `PROGRESS.md` per recipe; commit.

## Files to create / modify

| Path | Contents |
|---|---|
| `src/Animora.Persistence.PostgreSql/Outbox/*.cs`, `Configurations/Outbox*.cs`, `Migrations/Server/*ServerOutbox*` | outbox + claim + consumed markers + migration |
| `src/Animora.Persistence.PostgreSql/Options/DatabaseOptions.cs`, validator, DI (edit) | `WorkerMax`, rebalanced pool sum |
| `src/Animora.Server/Resilience/{ServerRetryPolicy,CircuitBreaker,OutboundTimeout,BoundedWorkerQueue}.cs` | resilience envelope |
| `src/Animora.Server/Workers/*.cs`, `Hints/*.cs` | workers, heartbeat probe, SignalR hint |
| `src/Animora.Server/Commands/*` (edit) | emit `feed.advanced` |
| `tests/Animora.Server.Tests/Outbox/*.cs`, `Workers/*.cs`, `Resilience/*.cs` | tests |

## Contracts / schema added

Tables `ServerOutbox`, `ConsumedMessages`. Hub `/sync/hub` (`FeedAdvanced(long lastSequence)`). Seams: `IOutboxConsumer`, `OutboxMessageTypes`, `IBackgroundAdmission` (implemented), `WorkerHeartbeat`. Pool profile gains `WorkerMax`.

## Rules that apply here

- **Layering (CONVENTIONS §2, `ARCHITECTURE.md` §6):** `Animora.Domain` references nothing; `Animora.Application` → Domain + Contracts + Contracts.Wire;
  only `Animora.Persistence.PostgreSql` may reference Npgsql/`UseNpgsql`; `Animora.Server` must never reference `Animora.Client.Services`,
  `Animora.Persistence.Sqlite`, `Animora.Infrastructure.Local`, `Animora.Sync.Custom` or `Animora.Desktop`; the client never references a server project.
- **C# style (§4):** `sealed` by default; `sealed record` DTOs; nullable honored (no `!` without a why-comment); every async public method takes
  `CancellationToken ct = default` and `ConfigureAwait(false)` in library projects; no `.Result`/`.Wait()`; expected failures return `Result`/`Result<T>`
  with an `ErrorCode`, never exceptions; `IClock`/`TimeProvider` injected (no `DateTime.Now`/`UtcNow`); ids are ULID strings via `IdGenerator`, never `Guid` in business code.
- **EF (§5):** EF Core 10 only; no lazy loading/proxies; reads `AsNoTracking()` + projections; named filters `"Tenant"` and `"SoftDelete"` kept separable;
  paging mandatory (max page 200); migrations additive and never edited later; `EnsureCreated` forbidden outside test fixtures.
- **Security (§7):** Authn → Authz → Entitlement in that order, never merged; `TenantId` resolved server-side from membership only; secrets never in config
  files/logs; defaults fail closed; no custom crypto; append-only audit for every security/financial action.

## Out of scope (owning phase)

- Subscription-expiry/licensing consumers (`subscription.expiry`, `license.recheck`) → **S14/S16** (they register `IOutboxConsumer`s). File reconciliation job → **S17**.
- Telemetry/metrics export of worker health → **S18**. Client reconnect jitter → **G03**.
- Message broker / Redis backplane → forbidden / deferred (ROADMAP §5).

## Acceptance criteria (mechanical)

1. `scripts/sandbox/build.sh full` = 0/0; `scripts/sandbox/run.sh` = `SMOKE RUN OK`; client sources unchanged.
2. `grep -rn "StackExchange\|Redis\|RabbitMq\|ServiceBus\|Kafka\|Polly" src/Animora.Server src/Animora.Persistence.PostgreSql` returns nothing; `grep -rn "PublishAot" src` returns nothing.
3. Pure tests pass: `ServerRetryPolicyTests.NextDelay_CappedExponential_FullJitter_Bounds`, `CircuitBreakerTests.OpensAfter5In60s_HalfOpenSingleProbe_Closes`, `OutboundTimeoutTests.Cancels_AfterTimeout`, `BoundedWorkerQueueTests.Backpressure_DoesNotDropOrGrow`, `OutboxClaimSqlTests.Predicate_HandlesNullLease_And_ExpiredLease`, `ConsumerIdempotencyTests.Redelivery_IsNoOp`, `WorkerLoopTests.Exception_DoesNotKillLoop_RateLimitedLog`, `WorkerHeartbeatTests.Stale_ReportsDegradedByNameOnly`, `PoolBudgetTests.SumIncludesWorker_Within12_And20`, `HintHubTests.GroupIsServerChosen_ClientCannotJoinOtherTenant`.
4. PG-gated (**mandatory G07**): `OutboxClaimTests.TwoWorkers_NeverClaimSameRow_SkipLocked`, `OutboxClaimTests.ExpiredLease_Reclaimable_FencedCompletionRejected`, `OutboxClaimTests.EnqueueRollsBackWithBusinessTransaction`, `WorkerRecoveryTests.KilledWorker_LeaseExpires_RowProcessed`.
5. `ls src/Animora.Persistence.PostgreSql/Migrations/Server | grep -c ServerOutbox` ≥ 1 (next free ordinal).
6. `grep -rn "IgnoreQueryFilters" src/Animora.Server/Workers src/Animora.Persistence.PostgreSql/Outbox` returns nothing.

## PROGRESS.md update

At the end of this phase: set the `S13` row in "Phase status" to `✅ Done` (add date + one-line summary), advance **"Next phase" → `S14`**,
append one `### S13` entry to "Repo state log" (what exists now: projects, tables/migration ordinal, error codes added, test classes), log any
deviation in the "Decision & deviation log", then commit `feat(server): S13 outbox, background workers & resilience (atomic claim, fenced lease, breaker, bulkhead)` and sync per the repo git workflow.
