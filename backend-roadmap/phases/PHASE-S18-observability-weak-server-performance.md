# Phase S18 — Server observability & weak-server performance (local-only telemetry, bounded budgets)

**Band:** M · **Track:** S (server) · **Depends on:** S13 · **Next:** S19
**Satisfies:** R-B07, R-B27, R-B35 · **Runnable after this phase:** desktop offline yes · server required no
**Produced by:** M00 (regenerated 2026-10-08; supersedes the deleted `PHASE-D13` file).

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
- `ARCHITECTURE.md` §15, §16, §PG.4, §17.4 — only these `§`.
- `STACK.md` §9, §10, §5.4 — only these `§`.
- `src/Animora.Server/Program.cs`, `src/Animora.Server/Hosting/*`, `src/Animora.Persistence.PostgreSql/Options/DatabaseOptions.cs` (edited here).

(Nothing else. `Animora-UI/**`, `Database-Sampel/**`, other phase files and `windows-frontend-roadmap/phases/**` are out of bounds.)

## Goal

Make the server operable on a 1–3 core / 2–4 GiB host **without any external service**: structured, redacted, local rolling-file logging with correlation ids end to end; OpenTelemetry metrics/traces that export **only to a loopback (or explicitly allowed LAN) collector and are fully optional**; the OLTP-vs-Reporting `QueryBudget`; Kestrel/request limits and admission control; PostgreSQL starting profiles for 2 GiB and 4 GiB as reviewed artifacts; the metrics catalogue the other server phases already named; and a bounded tenant-safe cache primitive. It claims **no capacity number** — the harness is G08.

---

## §Reference pack

```csharp
namespace Animora.Contracts.Wire.Errors;   // (A01)
public sealed record WireApiError(string Code, string? Detail, IReadOnlyList<WireFieldError> Fields, string CorrelationId);
public sealed record WireFieldError(string Field, string Code);
namespace Animora.Contracts.Wire;
public static class WireContract { public const int Version = 1; public const string HeaderName = "X-Animora-Contract-Version"; }
```
```csharp
// S02: DatabaseOptions { ConnectionString (env only), OltpWriteMax, OltpReadMax, KeyRingMax, WorkerMax (S13), ProfileGiB (2|4) } — pool sum validated ≤ 12 (2 GiB) / ≤ 20 (4 GiB); keyed NpgsqlDataSources "oltp-write"|"oltp-read"|"keyring"|"worker"; MinPoolSize=0 forced.
// S01: GlobalExceptionMiddleware (correlation id header `X-Correlation-Id`), IReadinessProbe, ServerOptions.DataRootPath, RateLimitPolicies.
// S13: IBackgroundAdmission, WorkerHeartbeat ; S12: metrics names reserved: animora.sync.batch.build.duration|pages|bytes, animora.pg.snapshot.age.seconds ; S17: animora.files.reconcile.*
// Existing client logging reference (NOT reused): Animora.Infrastructure.Local (Serilog 4.2.0, Serilog.Extensions.Logging 9.0.0, Serilog.Sinks.File 6.0.0 pins exist in Directory.Packages.props and ARE reused by the server).
```

## §Design decisions

- **Logging (ARCH §15.1):** Serilog over `Microsoft.Extensions.Logging` (**reuse the existing pins** `Serilog 4.2.0`, `Serilog.Extensions.Logging 9.0.0`, `Serilog.Sinks.File 6.0.0`; add `Serilog.AspNetCore` **only if** it resolves on the existing pins — otherwise wire `AddSerilog` through `Serilog.Extensions.Logging` manually; record the choice). Sinks: **rolling file only** under `DataRootPath/logs` (daily roll, 14 files, 50 MiB cap/file, shared=false, buffered with 1 s flush); **console sink only when `IsDevelopment()`**; no network/cloud sink exists in the build (test: `grep` for `Seq|Splunk|ApplicationInsights|Datadog|Loki|Elasticsearch`). Request logging samples at 100% for ≥ 400 and 5% for success, skips `/health/*`. **Redaction:** `RedactingEnricher` + destructuring policy removes properties named `password|token|refreshToken|authorization|valet|x-animora-valet|connectionString|secret|licenseKey|payloadJson|detailJson` and masks values of header names on that list; request/response bodies are **never** logged; `UnsafeEntityLogging=false` equivalent: entities are logged by id only. Enriched on every event: `TenantId` (from S05 context when present), `OperationId` (command intake), `DeviceId`, `RequestId`, `CorrelationId`.
- **Metrics/traces (STACK §9):** built-in `System.Diagnostics.Metrics` meters (`Meter("Animora.Server")`) are **always on and free**; the OpenTelemetry SDK is **optional**: packages (**new exact pins, resolved once at execution and recorded** — use the latest stable `1.x` of `OpenTelemetry.Extensions.Hosting`, `OpenTelemetry.Instrumentation.AspNetCore`, `OpenTelemetry.Instrumentation.Runtime`, `OpenTelemetry.Exporter.OpenTelemetryProtocol`, verified to target net10.0; record the four exact versions in `Directory.Packages.props` and `PROGRESS.md`). `TelemetryOptions { Enabled=false, OtlpEndpoint="http://127.0.0.1:4317", AllowLanCollector=false }` validated: host must be **loopback** unless `AllowLanCollector=true`, in which case it must be a private RFC1918/ULA address — **public hosts are rejected at startup** (`ValidateOnStart`); no default exporter is configured when `Enabled=false`; **removing the collector never breaks the app** (exporter failures are swallowed with a rate-limited warning; test `TelemetryTests.UnreachableCollector_DoesNotAffectRequests`). Instrumentation: ASP.NET Core + runtime + custom meters; **no EF/Npgsql command text with parameters in traces** (`SetDbStatementForText=false`).
- **Metrics catalogue (all declared in `ServerMetrics`, one place):** `animora.http.requests`/`duration` (via instrumentation), `animora.commands.settled` (counter tagged `outcome`, `commandType` family only — **no tenant id label**; cardinality bounded), `animora.commands.in_progress`, `animora.outbox.pending`/`dead_lettered`, `animora.worker.last_success_age.seconds`, `animora.pg.pool.in_use` per data source, `animora.pg.snapshot.age.seconds`, `animora.sync.batch.*`, `animora.files.reconcile.*`, `animora.ratelimit.rejected`. Tenant dimension is **never** a metric label (A-RULE 033 spirit + cardinality); per-tenant detail lives in logs.
- **`QueryBudget` (A-RULE 034):** `enum QueryClass { Oltp, Reporting }` + `IQueryBudget.Acquire(QueryClass, ct)` → disposable lease: `Oltp` unlimited-by-this-gate (bounded by the pool), `Reporting` limited to **1 concurrent**, **5 s statement timeout** (`SET LOCAL statement_timeout` on its connection), max 10 000 rows, executed on the dedicated `worker` data source (never the OLTP pool). S20's insight queries must acquire `Reporting`; test `QueryBudgetTests.Reporting_Concurrency1_Timeout5s_RowCap`.
- **Kestrel & admission:** `MaxConcurrentConnections = 256` (2 GiB) / 512 (4 GiB) from `ProfileGiB`, `MaxConcurrentUpgradedConnections = 64` (SignalR), `Limits.MaxRequestBodySize = 2 MiB` default with per-endpoint overrides (`/files` chunk 1 MiB + overhead; `/api/commands/batch` 1 MiB), `RequestHeadersTimeout 15 s`, `KeepAliveTimeout 60 s`, `MinRequestBodyDataRate` 240 B/s grace 10 s; `Server GC` **off** (workstation concurrent GC) with `GCHeapHardLimitPercent = 70` documented in `runtimeconfig` (`System.GC.ConserveMemory=5`); `TieredPGO` on, `ReadyToRun` **off** for the server host (JIT; AOT forbidden with EF). Response compression: only for JSON API responses (Brotli fastest, size ≥ 1 KiB), **never** `/sync` pages (already gzip) or `/files`.
- **PostgreSQL profile artifacts (reviewed, not runtime config):** `deploy/postgresql/animora-2gib.conf` and `animora-4gib.conf` (starting values per ARCH §PG.4: `shared_buffers` 256 MB / 768 MB, `work_mem` 4 MB / 8 MB (per-operation), `maintenance_work_mem` 64 MB / 128 MB, `effective_cache_size` 768 MB / 2 GB, `max_connections` 30 / 40 (= pools + admin/backup reserve ≥ 8), `wal_compression=on`, `synchronous_commit=on`, `full_page_writes=on`, `autovacuum=on` with `autovacuum_naptime=30s`, `idle_in_transaction_session_timeout=30s`, `lock_timeout=5s`, `statement_timeout=30s` for the app role, `log_min_duration_statement=500ms`, `track_io_timing=on`) **plus** `deploy/postgresql/README.md` stating every value is a **starting point proven only by G07/G08**, never a guarantee. The server's `SchemaProbe`/`DatabaseProbe` additionally warns (readiness *degraded*, name only) when `SHOW max_connections` < pool sum + 8.
- **Snapshot-age monitor:** `SnapshotAgeMonitor` (worker, 15 s) reads `pg_stat_activity` for the oldest `xact_start` of the server's own `RepeatableRead` sessions (application_name `Animora.Server/feed`) → gauge + degraded readiness at > `MaxSnapshotLifetime × 3`.
- **Bounded tenant-safe cache primitive:** `TenantScopedMemoryCache<TKey,TValue>` (server mirror of the client `TenantScopedCache`): key = `(TenantId, UserId, Resource, Version)` mandatory (constructor throws if any part empty), bounded by entry count (default 1 000) + sliding 60 s TTL, LRU eviction, `MemoryCache` with `SizeLimit`; used by later phases (S14/S16/S20) — **no cache exists on the OLTP command path at launch**. Test `TenantScopedCacheTests.MissingKeyPart_Throws`, `…TenantsNeverShareEntries`, `…BoundedByCount`.
- **No new ErrorCodes.**

## §Implementation plan

1. Pins: add the four OpenTelemetry packages (resolve exact versions once: `dotnet package search OpenTelemetry.Extensions.Hosting --exact-match --format json` under the sandbox env if the feed is reachable; otherwise use the versions already restored in `~/.nuget/packages/opentelemetry*` and record "offline-resolved"). Reference from `Animora.Server` only.
2. Server: `Observability/{LoggingSetup,RedactingEnricher,ServerMetrics,TelemetryOptions(+Validator),TelemetrySetup,SnapshotAgeMonitor,QueryBudget,KestrelProfile,TenantScopedMemoryCache}.cs`; edit `Program.cs` (steps 1–2 logging/options, step 7 pipeline: request logging with sampling) and `ServerOptions` as needed; compression middleware config.
3. Edit `DatabaseOptions` validator: `max_connections` advisory check hook (`IDatabaseCapabilityProbe`); add the `worker` data source consumption by `QueryBudget`.
4. Create `deploy/postgresql/animora-2gib.conf`, `animora-4gib.conf`, `README.md`.
5. Tests (below); `scripts/sandbox/build.sh full` → 0/0; `scripts/sandbox/test.sh Server` → green; `scripts/sandbox/run.sh` → `SMOKE RUN OK`.
6. `PROGRESS.md` per recipe (record the four OTel versions + whether logging uses `Serilog.AspNetCore`); commit.

## Files to create / modify

| Path | Contents |
|---|---|
| `Directory.Packages.props`, `src/Animora.Server/Animora.Server.csproj` (edit) | pins |
| `src/Animora.Server/Observability/*.cs`, `Program.cs` (edit) | logging, telemetry, metrics, budgets, Kestrel profile, cache |
| `src/Animora.Persistence.PostgreSql/Options/*` (edit) | capability probe hook |
| `deploy/postgresql/{animora-2gib.conf,animora-4gib.conf,README.md}` | reviewed PG starting profiles |
| `tests/Animora.Server.Tests/Observability/*.cs` | tests |

## Contracts / schema added

Meter `Animora.Server` + catalogue in `ServerMetrics`; `TelemetryOptions`; `QueryBudget`/`QueryClass`; `TenantScopedMemoryCache`; PG profile files. No schema, no wire, no ErrorCodes.

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

- Load harness, capacity numbers, restore drill, performance proof → **G07/G08**. Install-time PostgreSQL role/grant setup and PITR → **G09**.
- Operator-visible dashboards → **S19** (reads in-process counters only). Cross-tenant analytics → deferred (ROADMAP §5).
- Any external APM/log service — forbidden (A-RULE 016/032).

## Acceptance criteria (mechanical)

1. `scripts/sandbox/build.sh full` = 0/0; `scripts/sandbox/run.sh` = `SMOKE RUN OK`.
2. `grep -rniE "Seq|Splunk|ApplicationInsights|Datadog|Loki|Elasticsearch|NewRelic" src --include=*.cs --include=*.csproj` returns nothing; `grep -rn "SslProtocols\|PublishAot\|ServerGarbageCollector>true" src/Animora.Server` returns nothing.
3. Pure tests pass: `TelemetryOptionsTests.PublicHost_Rejected_Loopback_Allowed_LanOnlyWithOptIn`, `TelemetryTests.Disabled_ConfiguresNoExporter`, `TelemetryTests.UnreachableCollector_DoesNotAffectRequests`, `RedactionTests.SecretPropertiesAndHeaders_NeverLogged`, `RedactionTests.BodiesNeverLogged`, `LoggingTests.ConsoleSink_OnlyInDevelopment`, `LoggingTests.RollingFile_Limits`, `MetricsTests.NoTenantLabelOnAnyInstrument`, `QueryBudgetTests.Reporting_Concurrency1_Timeout5s_RowCap`, `KestrelProfileTests.Limits_By_ProfileGiB`, `TenantScopedCacheTests.MissingKeyPart_Throws`, `TenantScopedCacheTests.TenantsNeverShareEntries`, `TenantScopedCacheTests.BoundedByCount`, `PostgresProfileFilesTests.PoolSumPlusReserve_LE_MaxConnections` (parses the two `.conf` files against `DatabaseOptions` defaults).
4. `test -f deploy/postgresql/animora-2gib.conf && test -f deploy/postgresql/animora-4gib.conf && grep -c "starting point" deploy/postgresql/README.md` ≥ 1.
5. PG-gated (**mandatory G07**): `SnapshotAgeMonitorTests.OldSnapshot_ReportsDegraded`, `QueryBudgetPgTests.StatementTimeout_Enforced`.

## PROGRESS.md update

At the end of this phase: set the `S18` row in "Phase status" to `✅ Done` (add date + one-line summary), advance **"Next phase" → `S19`**,
append one `### S18` entry to "Repo state log" (what exists now: projects, tables/migration ordinal, error codes added, test classes), log any
deviation in the "Decision & deviation log", then commit `feat(server): S18 server observability & weak-server performance (local-only telemetry, bounded budgets)` and sync per the repo git workflow.
