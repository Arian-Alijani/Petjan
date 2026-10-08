# Phase S03 — Server data layer & tenancy II — PostgreSQL unit of work, repository base, isolation guard

**Band:** S · **Track:** S (server) · **Depends on:** S02 · **Next:** S04
**Satisfies:** R-B11, R-B19 · **Runnable after this phase:** desktop offline yes · server required no
**Produced by:** M00 (regenerated 2026-10-08; supersedes the deleted `PHASE-D02 (half 2)` file).

> Baseline verified 2026-10-08: client migration head `031` (`031_TreatmentPlans`), `CommandTypes` = live `CommandTypes.cs` (88 declared today — never freeze), `ErrorCodes` 161, `WireContract.Version` = 1; `src/` projects = `Animora.Domain`, `Animora.Application`, `Animora.Persistence.Sqlite`, `Animora.Infrastructure.Local`, `Animora.Client.Services`, `Animora.Contracts`, `Animora.Contracts.Wire`, `Animora.Desktop`, `Animora.Sync.Abstractions` (+ server-track projects added by earlier S/G phases).
> No migration in this phase.

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
- `ARCHITECTURE.md` §9.2 and §PG.4 (bounded snapshot lifetime/serialization retry) — only these `§`.
- `src/Animora.Persistence.PostgreSql/Context/AnimoraServerDbContext.cs`, `Tenancy/*.cs` (S02 output; edited here).

(Nothing else. `Animora-UI/**`, `Database-Sampel/**`, other phase files and `windows-frontend-roadmap/phases/**` are out of bounds.)

## Goal

Complete the tenancy mechanism so a business phase can persist aggregates without re-deciding transaction/isolation policy: a PostgreSQL `IUnitOfWork` (`ServerUnitOfWork`) with an execution-strategy-wrapped transaction and serialization-failure retry, the RLS-ready-but-off `ITenantSessionInitializer` hook, a generic `TenantRepository<T>` base (all reads `AsNoTracking`+paged by default), an `OltpReadContext` factory for read-only access, and the **cross-tenant isolation test suite** that proves isolation with RLS off (ARCH gate L). It adds no table.

---

## §Reference pack

```csharp
namespace Animora.Application.Abstractions;   // (B01) — the handler seam a server handler implements
public interface ICommand<TResult>;
public interface ICommandHandler<in TCommand, TResult> where TCommand : ICommand<TResult>
{ Task<Result<TResult>> HandleAsync(TCommand command, CommandContext context, CancellationToken ct); }
public sealed record CommandContext(string TenantId, string UserId, string DeviceId, string OperationId, string CorrelationId,
    string? CausationId, string? DependsOnOperationId, long? ExpectedAggregateVersion, DateTimeOffset CreatedAtUtc);
public interface IUnitOfWork { Task<Result> SaveAsync(CancellationToken ct = default);
    Task<Result<T>> ExecuteAsync<T>(Func<CancellationToken, Task<Result<T>>> work, CancellationToken ct = default); }
public interface IDispatcher { Task<Result<TResult>> SendAsync<TResult>(ICommand<TResult> command, CommandContext ctx, CancellationToken ct = default);
                               Task<Result<TResult>> QueryAsync<TResult>(IQuery<TResult> query, QueryContext ctx, CancellationToken ct = default); }
namespace Animora.Application.Dispatch;
public sealed class ApplicationRegistrationBuilder {   // used inside AddAnimoraApplication(configure)
    ApplicationRegistrationBuilder AddCommand<TCommand,TResult,THandler>(); AddQuery<TQuery,TResult,THandler>(); AddValidator<T,TValidator>();
    RequireCommand<TCommand,TResult>(); RequireQuery<TQuery,TResult>(); }
public static class Dispatcher.Chain => [Correlation, ExceptionMapping, Validation, Authorization, Entitlement, Transaction, CommandJournal, Audit];
//   TransactionBehavior resolves IUnitOfWork from DI and wraps commands (skips ISelfTransactedCommand and queries).
//   CommandJournalBehavior runs only for IJournaledCommand and resolves ICommandJournal + ILocalSequenceAllocator (CLIENT-side, SQLite).
```

```csharp
namespace Animora.Domain.Time;  public interface IClock { DateTimeOffset UtcNow { get; } /* + monotonic helpers per A02 */ }
namespace Animora.Domain.Primitives;
public abstract class SyncableEntity { string Id { get; init; }   /* ULID */  string TenantId { get; set; }  DateTimeOffset UpdatedAt { get; set; }  string? Version { get; set; }  bool Deleted { get; set; } }
public abstract class AggregateRoot : SyncableEntity, IHasDomainEvents { IReadOnlyList<DomainEvent> DomainEvents; void ClearDomainEvents(); protected void Raise(DomainEvent e); }
public static class IdGenerator { public static string NewId(); /* 26-char ULID */ }
```
```csharp
// S02 (this track) — exact public surface you build on
namespace Animora.Persistence.PostgreSql.Tenancy;
public interface ITenantContext { string? TenantId { get; } bool IsOperator { get; } }
public sealed class TenantContextMissingException : InvalidOperationException;
public sealed class TenantMismatchException : InvalidOperationException;
namespace Animora.Persistence.PostgreSql.Context;
public sealed class AnimoraServerDbContext : DbContext { /* ctor(DbContextOptions<AnimoraServerDbContext>, ITenantContext) ; DbSet<Tenant> Tenants */ }
// keyed singletons: "oltp-write" (writes), "oltp-read" (no-tracking reads), "keyring"
```

## §Design decisions

- **`ServerUnitOfWork : IUnitOfWork`** (scoped, `Animora.Persistence.PostgreSql.Transactions`): `SaveAsync` = `SaveChangesAsync`; `ExecuteAsync<T>(work, ct)` runs `ctx.Database.CreateExecutionStrategy().ExecuteAsync(...)` wrapping `BeginTransactionAsync(IsolationLevel.ReadCommitted)` → `ITenantSessionInitializer.InitializeAsync(connection, tenantId)` → `work(ct)` → on `Result.IsSuccess` commit else rollback; **returns the handler's failure `Result` without throwing**. A `PostgresException` with SQLSTATE `40001`/`40P01` (serialization/deadlock) is retried up to **3 times** with jittered back-off (50–250 ms) inside the strategy; `23505` unique-violation is surfaced as `Result.Fail(ErrorCodes.ConcurrencyConflict, "unique")` **only** when the caller opts in via `UniqueViolationMapping`; `DbUpdateConcurrencyException` → `Result.Fail(ErrorCodes.ConcurrencyConflict)`. Cancellation is never swallowed.
- **`ITenantSessionInitializer`** (`Tenancy`): `ValueTask InitializeAsync(DbConnection c, string tenantId, CancellationToken ct)`; default `NoopTenantSessionInitializer` (RLS **off**). A future `RlsTenantSessionInitializer` (gated, not built) must execute `SELECT set_config('animora.tenant_id', @t, true)` parameterized **on every transaction begin and every retry**; the contract is documented in XML-doc and asserted by an interface test only.
- **`TenantRepository<TEntity>`** (`Repositories`, abstract, `where TEntity : SyncableEntity`): protected `DbSet<TEntity> Set`; `GetAsync(id)` (tracked, for update), `ReadAsync(id)` (no-tracking), `Page(IQueryable<TEntity>, page, pageSize)` clamps `pageSize` to **200** and `page ≥ 1`; `Add(entity)`; `SoftDelete(entity)` sets `Deleted=true` (hard delete is not exposed). No `ToListAsync()` overload without paging is exposed. All reads go through filtered sets — the base never uses `IgnoreQueryFilters`.
- **Read factory:** `IServerReadContextFactory.Create()` returns an `AnimoraServerDbContext` configured with the `oltp-read` data source, `QueryTrackingBehavior.NoTracking`, same tenant filter. Reporting/insight (S20) will get its own `Reporting` budget; not here.
- **Isolation guard (the deliverable test class `TenantIsolationTests`, PG-gated):** seeds tenants A and B (+ a second `SyncableEntity` test table created by the test fixture only, never in a product migration — a fixture-local `IsolationProbeEntity`); asserts (1) A cannot read B's rows by id or list; (2) a write with B's `TenantId` under context A throws `TenantMismatchException`; (3) a query under a null context throws `TenantContextMissingException`; (4) `IgnoreQueryFilters(["SoftDelete"])` still hides B; (5) a required-navigation query does not drop parent rows (navigation modelled optional); (6) switching `ITenantContext` A→B→A on the **same pooled connection** returns correct rows each time. All run with `NoopTenantSessionInitializer` (RLS off) — this is the "holds with hardening OFF" proof.
- **Layer 3 note:** the replication data-view filter is exercised in S12; this phase only guarantees layers 1 (S05 supplies membership) and 2 + write stamping.

## §Implementation plan

1. Read nothing else. Create `Transactions/ServerUnitOfWork.cs`, `Transactions/UniqueViolationMapping.cs`, `Tenancy/ITenantSessionInitializer.cs` (+ `NoopTenantSessionInitializer`), `Repositories/TenantRepository.cs`, `Repositories/PageRules.cs`, `Context/IServerReadContextFactory.cs` + `ServerReadContextFactory.cs`.
2. Register in `PersistenceServiceCollectionExtensions.AddAnimoraPostgreSql`: `IUnitOfWork`→`ServerUnitOfWork` (scoped), `ITenantSessionInitializer`→`NoopTenantSessionInitializer` (singleton), `IServerReadContextFactory` (singleton).
3. Write tests in `tests/Animora.Server.Tests/Persistence/`: pure — `PageRulesTests`, `ServerUnitOfWorkTests` (uses a fake `DbContext`-free strategy seam: extract `ITransactionRunner` so retry logic is unit-testable: `RetryPolicyTests.Retries40001_Max3_ThenFails`, `…DoesNotRetry23505_Unmapped`), `SessionInitializerContractTests`; PG-gated — `TenantIsolationTests` (6 facts above).
4. `scripts/sandbox/build.sh full` → 0/0; `scripts/sandbox/test.sh Server` → green (PG facts skipped locally with reason). `scripts/sandbox/run.sh` → `SMOKE RUN OK`.
5. `PROGRESS.md` per recipe; commit.

## Files to create / modify

| Path | Contents |
|---|---|
| `src/Animora.Persistence.PostgreSql/Transactions/ServerUnitOfWork.cs`, `ITransactionRunner.cs`, `UniqueViolationMapping.cs` | PG `IUnitOfWork` + retry seam |
| `.../Tenancy/ITenantSessionInitializer.cs`, `NoopTenantSessionInitializer.cs` | RLS-ready hook (off) |
| `.../Repositories/TenantRepository.cs`, `PageRules.cs` | repository base + paging clamp |
| `.../Context/IServerReadContextFactory.cs`, `ServerReadContextFactory.cs` | no-tracking read context |
| `.../DependencyInjection/PersistenceServiceCollectionExtensions.cs` (edit) | registrations |
| `tests/Animora.Server.Tests/Persistence/PageRulesTests.cs`, `RetryPolicyTests.cs`, `SessionInitializerContractTests.cs`, `TenantIsolationTests.cs` | tests |

## Contracts / schema added

No schema. Public seams: `ServerUnitOfWork` (the `IUnitOfWork` every server handler transaction uses), `TenantRepository<T>`, `PageRules.MaxPageSize = 200`, `ITenantSessionInitializer`, `IServerReadContextFactory`.

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

- Business repositories for concrete aggregates (patients, invoices…) → **S07–S10** (each derives from `TenantRepository<T>`).
- Enabling RLS (`RlsTenantSessionInitializer`) → only after **G07** passes the RLS gate (ROADMAP §5 deferred).
- Command idempotency store → **S06**; outbox claim queries → **S13**; journal capture → **S11**.

## Acceptance criteria (mechanical)

1. `scripts/sandbox/build.sh full` = 0 warnings / 0 errors; `scripts/sandbox/run.sh` = `SMOKE RUN OK`.
2. `grep -rn "ToListAsync()" src/Animora.Persistence.PostgreSql/Repositories` returns nothing (paged only); `grep -rn "IgnoreQueryFilters" src/Animora.Persistence.PostgreSql/Repositories` returns nothing.
3. Named passing tests (pure): `PageRulesTests.Clamps_To200`, `PageRulesTests.Page_Below1_Becomes1`, `RetryPolicyTests.Retries40001_Max3_ThenFails`, `RetryPolicyTests.DoesNotRetry23505_Unmapped`, `RetryPolicyTests.Cancellation_IsNeverSwallowed`, `SessionInitializerContractTests.Noop_DoesNotTouchConnection`.
4. PG-gated (skipped locally, **must pass in G07**): `TenantIsolationTests.A_CannotRead_B_ById`, `…_List`, `…Write_WithForeignTenant_Throws_TenantMismatch`, `…NullContext_Throws_TenantContextMissing`, `…SoftDeleteLift_StillHidesOtherTenant`, `…PooledConnection_A_B_A_NoLeak`.
5. `git diff --stat -- src/Animora.Domain src/Animora.Persistence.Sqlite src/Animora.Desktop` empty; no new migration file (`ls src/Animora.Persistence.PostgreSql/Migrations/Server` unchanged vs S02).

## PROGRESS.md update

At the end of this phase: set the `S03` row in "Phase status" to `✅ Done` (add date + one-line summary), advance **"Next phase" → `S04`**,
append one `### S03` entry to "Repo state log" (what exists now: projects, tables/migration ordinal, error codes added, test classes), log any
deviation in the "Decision & deviation log", then commit `feat(server): S03 server data layer & tenancy ii` and sync per the repo git workflow.
