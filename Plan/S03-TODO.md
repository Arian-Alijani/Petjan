# S03 — Server data layer & tenancy II — execution TODO (self-contained)

> **Read this file top-to-bottom, then implement. Do NOT re-open the repo to "understand" it** —
> every signature, path, name and decision you need is already resolved and pasted below (verified
> against the tree 2026-10-09). Only re-open a file if a compile error proves a snippet here diverges.
> Source phase: `backend-roadmap/phases/PHASE-S03-server-data-layer-tenancy-ii.md` (deleted by the
> last task of this list). Baseline: S01+S02 done; branch `main`; no migration in this phase; no table.

## 0. GIT RULE (LOCKED — non-negotiable)
After **each** task below is complete, run from repo root:
```
scripts/git/push.sh "feat(server): S03 Txx — <short summary>"
```
(If it exits code 3 = no creds → call `setup_github_environment` tool once, re-run.)
**If a single task produces a large/heavy code block or a non-trivial fix, push it immediately with a
clear message** (do not batch it into a later push). Never leave work uncommitted.

## Goal (one paragraph)
Complete the PostgreSQL tenancy mechanism so a later business phase can persist aggregates without
re-deciding transaction/isolation policy. Add, in project `Animora.Persistence.PostgreSql`: a scoped
`IUnitOfWork` (`ServerUnitOfWork`) with an execution-strategy-wrapped transaction + serialization-failure
retry; the RLS-ready-but-OFF `ITenantSessionInitializer` hook (+ `NoopTenantSessionInitializer`); a generic
`TenantRepository<T>` base (reads are `AsNoTracking` + paged by default); an `OltpReadContext` read factory;
and the cross-tenant **isolation test suite** proving isolation with RLS OFF (ARCH gate L). No table, no migration.

---

## §A — PRE-RESOLVED FACTS (verified from the tree — do not re-discover)

### Projects & layering
- Target project: `src/Animora.Persistence.PostgreSql/` — the ONLY project that may reference Npgsql / `UseNpgsql`.
- Its csproj already references `Animora.Domain`, `Animora.Application`, `Animora.Contracts.Wire`, packages
  `Npgsql.EntityFrameworkCore.PostgreSQL`, `Npgsql`, EF Core + Relational + Design, DataProtection.EFCore, and
  has `<InternalsVisibleTo Include="Animora.Server.Tests" />`. **No csproj edit needed.**
- Test project: `tests/Animora.Server.Tests/` — references `Animora.Server` + `Animora.Persistence.PostgreSql`;
  packages xunit, Microsoft.NET.Test.Sdk, xunit.runner.visualstudio, Mvc.Testing. New test files go in
  `tests/Animora.Server.Tests/Persistence/`. **No test-csproj edit needed.**

### Result / ErrorCode (namespace `Animora.Domain.Results`)
- `Result` = `readonly record struct`; `Result.Ok()`, `Result.Fail(ErrorCode error, string? detail=null)`,
  `.IsSuccess`, `.IsFailure`, `.Error`, `.Detail`.
- `Result<T>` = `readonly record struct`; `Result<T>.Ok(value)`, `Result<T>.Fail(ErrorCode, string?)`,
  `.IsSuccess`, `.Value`, `.ToResult()`.
- `ErrorCode` = `readonly record struct(string Code, string Category)`.
- `ErrorCodes.ConcurrencyConflict` = `new("CONCURRENCY_CONFLICT", ErrorCode.Conflict)` — **use this** for
  `DbUpdateConcurrencyException` and for opted-in unique-violation mapping.

### IUnitOfWork (namespace `Animora.Application.Abstractions`)
```csharp
public interface IUnitOfWork {
    Task<Result> SaveAsync(CancellationToken ct = default);
    Task<Result<T>> ExecuteAsync<T>(Func<CancellationToken, Task<Result<T>>> work, CancellationToken ct = default);
}
```
(`Result`/`Result<T>` from `Animora.Domain.Results`.)

### Domain primitives (namespace `Animora.Domain.Primitives`)
- `SyncableEntity` (abstract): `string Id {get;init;}`, `string TenantId {get;set;}`,
  `DateTimeOffset UpdatedAt {get;set;}`, `string? Version {get;set;}`, `bool Deleted {get;set;}`.
- `AggregateRoot : SyncableEntity`. `IdGenerator.NewId()` → 26-char ULID.

### S02 surface this phase builds on (namespace `Animora.Persistence.PostgreSql.*`)
- `Context.AnimoraServerDbContext : DbContext` — ctor `(DbContextOptions<AnimoraServerDbContext>, ITenantContext)`;
  `DbSet<Tenant> Tenants`; `string CurrentTenantId => tenantContext.TenantId ?? throw new TenantContextMissingException();`
  `OnModelCreating` calls `ServerModelConventions.ApplyTenancy(modelBuilder, () => CurrentTenantId)`.
- `Tenancy.ITenantContext` — `string? TenantId {get;}`, `bool IsOperator {get;}`.
- `Tenancy.NullTenantContext` — returns null tenant / not operator.
- `Tenancy.TenantContextMissingException : Exception` (NOTE: derives from `Exception`, **not**
  `InvalidOperationException` as the phase reference-pack text loosely said — the real base is `Exception`).
  Ctors: `()`, `(string)`, `(string, Exception)`.
- `Tenancy.TenantMismatchException : Exception`. Ctors: `(string? contextTenant, string entityTenant)`,
  `(string)`, `(string, Exception)`. Props `ContextTenant`, `EntityTenant`.
- `Tenancy.TenantStampingInterceptor(ITenantContext) : SaveChangesInterceptor` — stamps TenantId from context on
  Added/Modified; throws `TenantMismatchException` on foreign tenant, `TenantContextMissingException` when both empty.
  Has `internal void Stamp(DbContext?)` for unit driving.
- `Tenancy.NoUnnamedIgnoreFiltersInterceptor(ITenantContext) : IQueryExpressionInterceptor` — bans unnamed
  `IgnoreQueryFilters()`; bans named `["Tenant"]` for non-operators.
- `Conventions.ServerModelConventions` — `const string TenantFilterName = "Tenant"`,
  `const string SoftDeleteFilterName = "SoftDelete"`; `ApplyTenancy(ModelBuilder, Expression<Func<string>>)`;
  `TenantIndex(EntityTypeBuilder, params string[])`. Applies named filters `"Tenant"` + `"SoftDelete"` to every
  `SyncableEntity` root, maps Id/TenantId as `varchar(26)`, Version as uuid concurrency token (`gen_random_uuid()`),
  UpdatedAt as `timestamptz(6)`, enums→int.
- `DependencyInjection.PersistenceServiceCollectionExtensions` — static class; method
  `AddAnimoraPostgreSql(this IServiceCollection, IConfiguration)`. Public const keys:
  `OltpWriteKey="oltp-write"`, `OltpReadKey="oltp-read"`, `KeyRingKey="keyring"`. Already registers:
  `ITenantContext`→`NullTenantContext` (singleton, TryAdd), `TenantStampingInterceptor` + `NoUnnamedIgnoreFiltersInterceptor`
  (scoped, TryAdd), 3 keyed `NpgsqlDataSource` singletons, `AnimoraServerDbContext` (UseNpgsql oltp-write + 2 interceptors),
  `AnimoraKeyRingDbContext`. **This phase ADDS registrations into this same method** (UoW, session initializer, read factory).

### Test infra (namespace `Animora.Server.Tests.Persistence`)
- `TestSupport.CreateContext(ITenantContext? tenant=null, string? connection=null)` → `AnimoraServerDbContext`
  over `UseNpgsql` (uses `DummyConnection` const when none — model inspected, connection never opened).
- `FixedTenantContext(string? tenantId, bool isOperator=false) : ITenantContext`.
- `PgFixture : IAsyncLifetime` — creates a private DB per run from `ANIMORA_PG_TEST_CONNECTION`; `.ConnectionString`.
- `[PgFact]` attribute — auto-skips with reason when `ANIMORA_PG_TEST_CONNECTION` unset (and not `ANIMORA_REQUIRE_PG=1`).
- Existing PG-gated class pattern: `public sealed class XxxTests(PgFixture pg) : IClassFixture<PgFixture>`.

### Build / test / run (sandbox scripts ONLY — never raw dotnet; memory-capped ~985 MiB)
- `scripts/sandbox/build.sh full` → expect **0 warnings / 0 errors**.
- `scripts/sandbox/test.sh Server` → expect green; PG facts **skipped locally with reason**.
- `scripts/sandbox/run.sh` → expect `SMOKE RUN OK`.
- `scripts/sandbox/build.sh fast` for a quick compile check during iteration (optional). No full-suite runs beyond the above.

---

## §B — DESIGN DECISIONS ALREADY MADE (implement exactly; do not re-derive)

### B1. `ServerUnitOfWork : IUnitOfWork` (namespace `Animora.Persistence.PostgreSql.Transactions`, scoped)
- Ctor deps: `AnimoraServerDbContext db`, `ITenantContext tenantContext`, `ITenantSessionInitializer sessionInitializer`,
  `ITransactionRunner runner`. (Runner seam lets retry logic be unit-tested without a DB — see B2.)
- `SaveAsync(ct)` ⇒ wraps `db.SaveChangesAsync(ct)` in the store-exception → `Result` mapping (B4) and returns `Result`.
- `ExecuteAsync<T>(work, ct)` ⇒ delegates to `runner.RunAsync(...)` which performs:
  `db.Database.CreateExecutionStrategy().ExecuteAsync(...)` wrapping:
  `BeginTransactionAsync(IsolationLevel.ReadCommitted)` → `sessionInitializer.InitializeAsync(connection, tenantId, ct)`
  → `work(ct)` → if `Result.IsSuccess` commit else rollback. **Returns the handler's failure `Result` WITHOUT throwing.**
  `tenantId` passed to the initializer = `tenantContext.TenantId ?? ""` (Noop ignores it anyway).
  Use `System.Data.IsolationLevel.ReadCommitted`. Connection for the initializer =
  `db.Database.GetDbConnection()` (a `System.Data.Common.DbConnection`).
- Retry (inside the execution strategy / runner): a `PostgresException` with SQLSTATE `40001` (serialization_failure)
  or `40P01` (deadlock_detected) is retried up to **3 times** with jittered back-off **50–250 ms**; after 3 failed
  attempts the last failure surfaces. `23505` (unique_violation) is **not** retried.
- `23505` → `Result.Fail(ErrorCodes.ConcurrencyConflict, "unique")` **ONLY when the caller opted in** via
  `UniqueViolationMapping` (B3); otherwise it is **not** mapped here (let it propagate per default).
- `DbUpdateConcurrencyException` → `Result.Fail(ErrorCodes.ConcurrencyConflict)`.
- **Cancellation is never swallowed** — `OperationCanceledException`/`TaskCanceledException` must propagate (rethrow),
  never become a `Result.Fail`. (This is asserted by `RetryPolicyTests.Cancellation_IsNeverSwallowed`.)
- Every async method: `CancellationToken ct = default`; `ConfigureAwait(false)` on every await (library project).
- `sealed`. No `.Result`/`.Wait()`.

### B2. `ITransactionRunner` + default impl (same `Transactions` namespace)
- Purpose: make the retry/back-off state machine unit-testable without a real DbContext/connection.
- Interface:
  ```csharp
  internal interface ITransactionRunner {
      Task<Result<T>> RunAsync<T>(Func<CancellationToken, Task<Result<T>>> work, CancellationToken ct);
  }
  ```
  (`internal` is fine — `InternalsVisibleTo Animora.Server.Tests` is set, so tests can implement/drive it.)
- Default impl `ServerTransactionRunner` wraps `AnimoraServerDbContext` + `ITenantSessionInitializer` + `ITenantContext`
  and does the real `CreateExecutionStrategy().ExecuteAsync` + begin/init/commit/rollback described in B1.
- **Unit-testable retry core:** put the "classify SQLSTATE, count attempts, jittered delay, stop at 3" logic in a small
  pure helper so `RetryPolicyTests` can exercise it directly. Decision: expose a static/internal
  `RetryPolicy` with:
  ```csharp
  internal static class RetryPolicy {
      public const int MaxAttempts = 3;
      public static bool IsTransient(string sqlState); // true for "40001"/"40P01", false for "23505" etc.
      public static TimeSpan BackoffFor(int attempt, Func<double> rng); // jitter in [50ms,250ms]; rng() in [0,1)
      // Drives an attempt-delegate up to MaxAttempts, retrying only transient failures,
      // never retrying on cancellation (rethrows), returning the final outcome/exception.
      public static Task<Result<T>> ExecuteAsync<T>(
          Func<int, CancellationToken, Task<Result<T>>> attempt,  // receives 1-based attempt number
          Func<Exception, bool> isTransient,                      // classify thrown PostgresException
          Func<double> rng, Func<TimeSpan, CancellationToken, Task> delay, CancellationToken ct);
  }
  ```
  `ServerTransactionRunner` uses `RetryPolicy.ExecuteAsync` with a real `Task.Delay` + `Random.Shared.NextDouble`.
  `RetryPolicyTests` uses a fake attempt-delegate (no DB), a deterministic `rng`, and a no-op `delay` to assert
  exactly 3 attempts on a thrown `40001`, 1 attempt (no retry) on `23505`, and cancellation propagation.
- Classify thrown exceptions: `Npgsql.PostgresException { SqlState: "40001" or "40P01" }` ⇒ transient.
  Access SQLSTATE via `PostgresException.SqlState` (string). Deadlock = `"40P01"`, serialization = `"40001"`,
  unique = `"23505"`.

### B3. `UniqueViolationMapping` (same `Transactions` namespace)
- An **opt-in** marker the caller passes so a `23505` becomes `Result.Fail(ErrorCodes.ConcurrencyConflict,"unique")`.
  Decision (keep minimal, no over-engineering): a tiny immutable struct/record carrying an on/off flag, e.g.
  ```csharp
  public readonly record struct UniqueViolationMapping(bool MapToConcurrencyConflict) {
      public static readonly UniqueViolationMapping None = new(false);
      public static readonly UniqueViolationMapping Enabled = new(true);
  }
  ```
  The default `ExecuteAsync` path uses `None` (do not map). Document in XML-doc that a future overload / handler opts in.
  Since the `IUnitOfWork.ExecuteAsync<T>` signature is fixed (no mapping param), the mapping is consulted **only**
  when a caller explicitly uses an overload/property that opts in — for S03, ship the type + the "when enabled, map
  23505→ConcurrencyConflict/'unique'" branch in the runner's catch, wired to a `UniqueViolationMapping` field that
  defaults to `None`. No public API beyond the type + one internal switch is required this phase.
- `sealed`/`readonly record struct`. Keep it ~15 lines; don't invent extra config.

### B4. Store-exception → Result mapping (shared helper inside `ServerUnitOfWork`/runner)
- `DbUpdateConcurrencyException` ⇒ `Result.Fail(ErrorCodes.ConcurrencyConflict)`.
- `DbUpdateException` whose inner is `PostgresException{SqlState:"23505"}` AND mapping enabled ⇒
  `Result.Fail(ErrorCodes.ConcurrencyConflict,"unique")`; if mapping disabled ⇒ rethrow (let it surface).
- `OperationCanceledException` ⇒ rethrow (never swallow).
- Mirror the style of `src/Animora.Persistence.Sqlite/Repositories/LocalUnitOfWork.cs` `StoreAsync` (reference only).

### B5. `ITenantSessionInitializer` + `NoopTenantSessionInitializer` (namespace `...Tenancy`)
```csharp
public interface ITenantSessionInitializer {
    ValueTask InitializeAsync(DbConnection connection, string tenantId, CancellationToken ct = default);
}
```
(`DbConnection` = `System.Data.Common.DbConnection`.)
- `NoopTenantSessionInitializer : ITenantSessionInitializer` — RLS **OFF**: does nothing, touches no connection,
  returns `ValueTask.CompletedTask`. Registered as the default (singleton).
- XML-doc the future `RlsTenantSessionInitializer` contract (NOT built this phase): it must run
  `SELECT set_config('animora.tenant_id', @t, true)` **parameterized**, on **every** transaction begin AND every retry.
  This contract is asserted only by `SessionInitializerContractTests.Noop_DoesNotTouchConnection` (see §C T7).

### B6. `TenantRepository<TEntity>` (namespace `Animora.Persistence.PostgreSql.Repositories`, abstract,
`where TEntity : SyncableEntity`)
- Ctor dep: `AnimoraServerDbContext context` (store protected). `protected DbSet<TEntity> Set => context.Set<TEntity>();`
- `protected Task<TEntity?> GetAsync(string id, CancellationToken ct=default)` — **tracked** (for update); `FindAsync`
  or `Set.FirstOrDefaultAsync(e => e.Id == id, ct)` (tracked). Honors filters (no IgnoreQueryFilters).
- `protected Task<TEntity?> ReadAsync(string id, CancellationToken ct=default)` — `Set.AsNoTracking().FirstOrDefaultAsync(...)`.
- `protected static IQueryable<TEntity> Page(IQueryable<TEntity> query, int page, int pageSize)` — uses `PageRules`
  (B7) to clamp, then `.Skip((page-1)*pageSize).Take(pageSize)`.
- `protected void Add(TEntity entity)` ⇒ `Set.Add(entity)`.
- `protected void SoftDelete(TEntity entity)` ⇒ `entity.Deleted = true;` (no hard delete exposed — never call `Remove`).
- **NO** `ToListAsync()`-without-paging overload. **NO** `IgnoreQueryFilters` anywhere (acceptance grep #2).
- All reads go through the filtered `Set`. `ConfigureAwait(false)`. `abstract` (concrete repos come in S07–S10).

### B7. `PageRules` (namespace `Animora.Persistence.PostgreSql.Repositories`, static)
```csharp
public static class PageRules {
    public const int MaxPageSize = 200;
    public static int ClampPageSize(int pageSize); // pageSize<1 -> ... decide; >200 -> 200
    public static int NormalizePage(int page);     // page<1 -> 1
}
```
- `MaxPageSize = 200`. `ClampPageSize`: clamp to `[1,200]` (so `>200 ⇒ 200`; `<1 ⇒ 1`). `NormalizePage`: `page<1 ⇒ 1`.
- Must satisfy tests: `PageRulesTests.Clamps_To200` (e.g. 500→200) and `PageRulesTests.Page_Below1_Becomes1` (0→1, -5→1).

### B8. Read factory (namespace `Animora.Persistence.PostgreSql.Context`)
```csharp
public interface IServerReadContextFactory { AnimoraServerDbContext Create(); }
```
- `ServerReadContextFactory : IServerReadContextFactory` (singleton). Ctor deps:
  `[FromKeyedServices(OltpReadKey)] NpgsqlDataSource readDataSource`, `ITenantContext tenantContext`.
  (Inject the keyed data source via `IServiceProvider.GetRequiredKeyedService` in the DI lambda, or constructor keyed
  attribute — simplest is to build it in the DI registration lambda, see B9.)
- `Create()` returns a NEW `AnimoraServerDbContext` configured with: the **`oltp-read`** data source,
  `optionsBuilder.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)`, same `ITenantContext` (same tenant filter),
  and **the same two interceptors** (`TenantStampingInterceptor`, `NoUnnamedIgnoreFiltersInterceptor`) so the ban/stamp
  invariants still apply. Build `DbContextOptions<AnimoraServerDbContext>` manually with `new DbContextOptionsBuilder<...>()`.
  Caller disposes it. Do NOT add a `UseNpgsql` retry strategy here (reads use the same strategy model; keep it simple —
  no retry needed for read factory).
  Reporting/insight budget is S20 — not here.

### B9. DI registrations — edit `PersistenceServiceCollectionExtensions.AddAnimoraPostgreSql`
Add (TryAdd where appropriate):
- `services.TryAddScoped<IUnitOfWork, ServerUnitOfWork>();` (namespace `Animora.Application.Abstractions.IUnitOfWork`).
- `services.TryAddScoped<ITransactionRunner, ServerTransactionRunner>();`
- `services.TryAddSingleton<ITenantSessionInitializer, NoopTenantSessionInitializer>();`
- `services.TryAddSingleton<IServerReadContextFactory>(sp => new ServerReadContextFactory(
      sp.GetRequiredKeyedService<NpgsqlDataSource>(OltpReadKey),
      sp.GetRequiredService<ITenantContext>(),
      sp.GetRequiredService<TenantStampingInterceptor>(),       // or resolve per-create; decide at impl
      sp.GetRequiredService<NoUnnamedIgnoreFiltersInterceptor>()));`
  NOTE: the two interceptors are registered **scoped**; a singleton factory must not capture scoped instances.
  **Decision:** make `ServerReadContextFactory` take the `IServiceProvider` (or an `ObjectFactory`) and resolve the
  interceptors fresh inside `Create()` from a new scope, OR register the interceptors as transient/singleton-safe.
  Simplest safe path: register `ServerReadContextFactory` as **singleton** but have it depend only on the keyed
  `NpgsqlDataSource` (singleton) + a `Func<ITenantContext>` / the root `IServiceProvider`, resolving `ITenantContext`
  and the two interceptors from the provider on each `Create()`. Implement `Create()` to `using var scope =
  _provider.CreateScope();` → resolve interceptors → build options → return context. (Keep the data source captured.)
  Keep imports: `Microsoft.Extensions.DependencyInjection` (for `GetRequiredKeyedService`, `CreateScope`).
- Keep all additions inside the existing method; do not change its signature or the existing registrations/order.

### B10. Isolation test suite `TenantIsolationTests` (PG-gated) — see §C T8 for the exact 6 facts.

---

## §C — TASK LIST (do in order; push after EACH)

- [x] **T0 — Setup.** Create `Plan/` folder (done) and this file. Confirm branch `main`, git creds ready.
      Push: `feat(server): S03 T0 — add Plan/S03-TODO.md execution plan`.

- [x] **T1 — `ITenantSessionInitializer` + `NoopTenantSessionInitializer`.**
      Create `src/Animora.Persistence.PostgreSql/Tenancy/ITenantSessionInitializer.cs` (interface per B5) and
      `Tenancy/NoopTenantSessionInitializer.cs` (no-op, touches no connection). XML-doc the future RLS contract.
      `fast` compile optional. Push `feat(server): S03 T1 — tenant session initializer hook (RLS off noop)`.

- [x] **T2 — `PageRules`.** Create `src/Animora.Persistence.PostgreSql/Repositories/PageRules.cs` per B7
      (`MaxPageSize=200`, `ClampPageSize`, `NormalizePage`). Push `feat(server): S03 T2 — PageRules paging clamp (max 200)`.

- [x] **T3 — `TenantRepository<T>`.** Create `src/Animora.Persistence.PostgreSql/Repositories/TenantRepository.cs`
      per B6 (abstract, no-tracking reads, `Page` via PageRules, `Add`/`SoftDelete`, NO `IgnoreQueryFilters`, NO
      unpaged `ToListAsync`). Push `feat(server): S03 T3 — TenantRepository<T> base (no-track, paged, soft-delete)`.

- [x] **T4 — Retry core + `UniqueViolationMapping` + `ITransactionRunner`.**
      Create `Transactions/UniqueViolationMapping.cs` (B3), `Transactions/ITransactionRunner.cs` (B2 interface +
      `RetryPolicy` pure helper). This is the unit-testable retry seam — keep `RetryPolicy` free of EF/Db types.
      Push `feat(server): S03 T4 — transaction runner seam + retry policy + unique-violation mapping`.

- [ ] **T5 — `ServerUnitOfWork` + `ServerTransactionRunner`.**
      Create `Transactions/ServerUnitOfWork.cs` (B1, implements `IUnitOfWork`) and the default
      `ServerTransactionRunner` (B2) doing the real execution-strategy + begin/init/commit/rollback using
      `RetryPolicy` + store-exception mapping (B4). Cancellation never swallowed.
      Push `feat(server): S03 T5 — ServerUnitOfWork + ServerTransactionRunner (exec-strategy, serialization retry)`.

- [ ] **T6 — Read factory.** Create `Context/IServerReadContextFactory.cs` + `Context/ServerReadContextFactory.cs`
      per B8 (oltp-read data source, NoTracking, same tenant filter + interceptors). Push
      `feat(server): S03 T6 — OltpReadContext factory (no-tracking read context)`.

- [ ] **T7 — DI registrations.** Edit `DependencyInjection/PersistenceServiceCollectionExtensions.cs`
      (`AddAnimoraPostgreSql`) per B9: register `IUnitOfWork`→`ServerUnitOfWork` (scoped),
      `ITransactionRunner`→`ServerTransactionRunner` (scoped), `ITenantSessionInitializer`→`Noop…` (singleton),
      `IServerReadContextFactory`→`ServerReadContextFactory` (singleton, scope-resolves interceptors+tenant per Create).
      Run `scripts/sandbox/build.sh fast` to confirm it compiles. Push
      `feat(server): S03 T7 — register UoW, session initializer, read factory in AddAnimoraPostgreSql`.

- [ ] **T8 — Tests.** In `tests/Animora.Server.Tests/Persistence/`:
      - `PageRulesTests.cs` — facts: `Clamps_To200` (e.g. 500→200, 200→200), `Page_Below1_Becomes1` (0→1,-5→1).
      - `RetryPolicyTests.cs` — pure, no DB, drive `RetryPolicy`/runner retry core with fake attempt-delegate +
        deterministic rng + no-op delay. Facts: `Retries40001_Max3_ThenFails` (transient thrown each time ⇒ exactly
        3 attempts then surfaces failure), `DoesNotRetry23505_Unmapped` (23505 ⇒ 1 attempt, not retried),
        `Cancellation_IsNeverSwallowed` (an `OperationCanceledException` propagates, is not turned into Result.Fail).
      - `SessionInitializerContractTests.cs` — fact `Noop_DoesNotTouchConnection`: call
        `NoopTenantSessionInitializer.InitializeAsync` with a connection spy / a connection whose state is asserted
        unchanged (e.g. a `DbConnection` test double or assert it is never opened); verify no interaction.
      - `TenantIsolationTests.cs` — **PG-gated** (`[PgFact]`, `IClassFixture<PgFixture>`). A fixture-local
        `IsolationProbeEntity : SyncableEntity` (never a product migration) + a test-only DbContext subclass OR a
        test-only model extension so the probe table exists (create it via the fixture, `EnsureCreated` allowed in a
        **test fixture only** per EF §5). Seed tenants A and B. The 6 facts (names fixed by acceptance #4):
        1. `A_CannotRead_B_ById` — under context A, read B's row by id ⇒ not found.
        2. `A_CannotRead_B_List` — under context A, list ⇒ excludes B's rows.
        3. `Write_WithForeignTenant_Throws_TenantMismatch` — write entity with B's TenantId under context A ⇒
           `TenantMismatchException`.
        4. `NullContext_Throws_TenantContextMissing` — query under null context ⇒ `TenantContextMissingException`.
        5. `SoftDeleteLift_StillHidesOtherTenant` — `IgnoreQueryFilters(["SoftDelete"])` still hides B's rows
           (Tenant filter stays on).
        6. `PooledConnection_A_B_A_NoLeak` — switch `ITenantContext` A→B→A on the **same pooled connection** ⇒
           correct rows each time, no leak.
        All run with `NoopTenantSessionInitializer` (RLS OFF) — the "holds with hardening OFF" proof.
        Model the probe's navigation (if any) as **optional** so fact (5)-style required-navigation queries don't drop
        parents (phase §Design "required-navigation query does not drop parent rows").
      - If a large/heavy test fixture block is written, push it on its own with a clear message.
      Push `test(server): S03 T8 — PageRules/RetryPolicy/SessionInitializer + PG-gated TenantIsolation tests`.

- [ ] **T9 — Build + test + smoke (acceptance).**
      `scripts/sandbox/build.sh full` ⇒ **0 warnings / 0 errors**.
      `scripts/sandbox/test.sh Server` ⇒ green (PG facts skipped locally with reason).
      `scripts/sandbox/run.sh` ⇒ `SMOKE RUN OK`.
      Verify acceptance greps:
      - `grep -rn "ToListAsync()" src/Animora.Persistence.PostgreSql/Repositories` ⇒ nothing.
      - `grep -rn "IgnoreQueryFilters" src/Animora.Persistence.PostgreSql/Repositories` ⇒ nothing.
      - `git diff --stat -- src/Animora.Domain src/Animora.Persistence.Sqlite src/Animora.Desktop` ⇒ empty.
      - `ls src/Animora.Persistence.PostgreSql/Migrations/Server` unchanged vs S02 (no new migration).
      Fix anything red (push heavy fixes separately with a clear message).
      Push `chore(server): S03 T9 — full build 0/0, Server tests green, SMOKE RUN OK, acceptance greps`.

- [ ] **T10 — PROGRESS.md.** Edit `backend-roadmap/PROGRESS.md`:
      set `S03` row in "Phase status" → `✅ Done` (+ date 2026-10-09 + one-line summary); advance **"Next phase" → S04**;
      append ONE `### S03` entry to "Repo state log" (what exists now: new files/seams — `ServerUnitOfWork`,
      `ITransactionRunner`/`ServerTransactionRunner`, `RetryPolicy`, `UniqueViolationMapping`, `ITenantSessionInitializer`+
      `NoopTenantSessionInitializer`, `TenantRepository<T>`, `PageRules` (MaxPageSize=200), `IServerReadContextFactory`+
      `ServerReadContextFactory`; test classes `PageRulesTests`/`RetryPolicyTests`/`SessionInitializerContractTests`/
      `TenantIsolationTests`; no schema/migration added; no error codes added); log any deviation (e.g. the
      `TenantContextMissingException`/`TenantMismatchException` base-type note) in the "Decision & deviation log".
      Do NOT rewrite/shorten older entries. Push `docs(server): S03 T10 — PROGRESS.md status Done, Next S04, repo-state log`.

- [ ] **T11 — Delete the source phase file.** `rm backend-roadmap/phases/PHASE-S03-server-data-layer-tenancy-ii.md`
      (phase recipe step; the phase is complete). Do NOT touch any other roadmap/phase file. Push
      `chore(server): S03 T11 — remove completed S03 phase file`.

- [ ] **T12 — Delete THIS plan file (final task).** `rm Plan/S03-TODO.md`. (Leave the empty `Plan/` folder or remove
      if empty — removing the file is the required final step; do not touch anything else.) Push
      `chore: S03 T12 — remove completed Plan todo file`.

---

## §D — GUARDRAILS (read once)
- Implement **only** S03 scope: nothing from a later phase, nothing "while we're here".
- `sealed` by default; `sealed record`/`readonly record struct` for DTOs/value types; nullable honored (no `!` without a
  why-comment); every async public lib method takes `CancellationToken ct=default` + `ConfigureAwait(false)`; no
  `.Result`/`.Wait()`; expected failures return `Result`/`Result<T>` with an `ErrorCode`, never exceptions.
- EF: EF Core 10; reads `AsNoTracking`; named filters `"Tenant"`/`"SoftDelete"` kept separable; paging mandatory (max 200);
  NO `IgnoreQueryFilters` in Repositories; `EnsureCreated` only inside test fixtures; no new/edited migration.
- Layering: only `Animora.Persistence.PostgreSql` references Npgsql; do not reference client/Sqlite/Desktop projects.
- NEVER raw `dotnet build/test` — use `scripts/sandbox/*` only. No full-suite run beyond the 3 acceptance commands.
- After EVERY task: `scripts/git/push.sh "<msg>"`. Heavy fix mid-task ⇒ push it immediately with a clear message.
