# Phase D13 — Server observability & weak-server performance

> **PostgreSQL baseline (2026-10-08):** Read `ARCHITECTURE.md` §PG and §16-CT–§18-CT / `STACK.md` §6 before implementation. They govern PostgreSQL 18, Npgsql 10.0.3, UUID Version, tenant journal, snapshot/batch atomicity, pool budgets and deployment-only migrations. This phase is a future instruction, not an implementation claim. Mandatory PostgreSQL CI cannot pass by skipping DB tests.

**Band:** M · **Track:** D · **Depends on:** D09 · **Satisfies:** R-B07, R-B27, R-B35
**Runnable after this phase:** desktop offline yes · server required no

## Read before starting
- `backend-roadmap/CONVENTIONS.md` §4, §5, §7, §8, §9, §12
- `backend-roadmap/PROGRESS.md` (D09 entry — `OutboxDiagnostics`, `WorkerBulkhead`,
  `WorkerOptions`, `ServerResilience`, degradation thresholds; D08 entry — `TableOptionsFactory`,
  `replication` readiness check; D05 entry — `CommandIntakeService`, `IdempotencyRecords`; D04 entry
  — `MembershipCache`, `HttpTenantContext`; D03 entry — the deliberately un-rate-limited
  `/api/identity/login` + `refresh` with the prepared `"auth"` policy name; D01 entry —
  `ObservabilityOptions`, `ServerLoggingSetup`, `HealthEndpoints`, `LogRedaction`)
- `ARCHITECTURE.md` §15, §16.1, §16.3, §11.5, §19 gate V · `STACK.md` §9, §10, §6.4
- `A-RULE 016`, `A-RULE 032`, `A-RULE 033`, `A-RULE 034`, `A-RULE 035`
- `PERFORMANCE.md` §1, §15, §16
- `src/Animora.Server/Diagnostics/OutboxDiagnostics.cs`,
  `src/Animora.Server/Configuration/ObservabilityOptions.cs`

## Goal
The server becomes observable and defensible on weak hardware: correlated structured logs end to
end, local-only OpenTelemetry metrics and traces, diagnostics endpoints for sync and command
settlement, a downloadable diagnostics bundle, rate limiting and per-tenant throttling, tuned
connections/queries/caches, and a **published capacity-measurement harness** whose numbers are
measured, never asserted.

## Design decisions (pre-made — implement, do not re-derive)
- **Correlation, one context, four ids** (`ARCHITECTURE.md` §15): `RequestId` (from
  `HttpContext.TraceIdentifier`), `TenantId`, `UserId`/`DeviceId`, and `OperationId` when the request
  carries a command. `src/Animora.Server/Diagnostics/ServerCorrelationMiddleware.cs` builds a
  `ServerCorrelation` record, pushes it into the Serilog `LogContext` **and** into
  `Activity.Current?.SetTag(...)`, and echoes `X-Animora-Correlation` on the response so a client can
  quote it in a support request. B05's `AmbientCorrelation` shape is reused conceptually; the server
  does not reference the client type.
- **OpenTelemetry, local collector only, disabled by default** (A-RULE 016/032, `STACK.md` §9):
  packages `OpenTelemetry.Extensions.Hosting`, `OpenTelemetry.Instrumentation.AspNetCore`,
  `OpenTelemetry.Instrumentation.EntityFrameworkCore`, `OpenTelemetry.Instrumentation.Runtime`,
  `OpenTelemetry.Exporter.OpenTelemetryProtocol` (pinned centrally). Wiring rules:
  - `ObservabilityOptions.OtlpEndpoint` empty → **no exporter is registered at all** (metrics still
    collected in-process for `/diagnostics/metrics`). The app must run identically with no collector.
  - a non-empty endpoint is validated (D01 already requires loopback/LAN) — a public host fails
    startup with `OBSERVABILITY_ENDPOINT_NOT_LOCAL`.
  - a test removes the collector mid-run and asserts zero request failures (gate V's server half).
- **Meter and instruments, named once** (`src/Animora.Server/Diagnostics/AnimoraMetrics.cs`,
  meter `Animora.Server`):
  | Instrument | Type | Tags |
  |---|---|---|
  | `animora.commands.settled` | counter | `outcome`, `command_type` |
  | `animora.commands.duration` | histogram (ms) | `command_type` |
  | `animora.commands.idempotent_replays` | counter | — |
  | `animora.outbox.backlog` | observable gauge (from `OutboxDiagnostics`) | `status` |
  | `animora.outbox.dead_lettered` | counter | `message_type` |
  | `animora.sync.pull` | histogram (ms) | `entity`, `page_size` |
  | `animora.sync.rows_sent` | counter | `entity` |
  | `animora.db.command.duration` | histogram (ms) | `context` |
  | `animora.tenant.requests` | counter | `tenant_hash` |
  **`tenant_hash` is a salted SHA-256 prefix, never a raw `TenantId`** — a metric label is a
  long-lived artifact and must not carry tenant identity (CONVENTIONS §7). No PII, no money amount,
  no patient id is ever a tag; `MetricsHygieneTests` asserts the tag key allow-list.
- **Diagnostics endpoints** (`src/Animora.Server/Endpoints/DiagnosticsEndpoints.cs`, prefix
  `/diagnostics`, all `RequireAuthorization` — `RequireOperator` for the cross-tenant ones,
  a tenant manager permission for the tenant-scoped ones):
  | Route | Scope | Payload |
  |---|---|---|
  | `GET /diagnostics/metrics` | operator | in-process snapshot of every instrument (no Prometheus endpoint — nothing is scraped from outside) |
  | `GET /diagnostics/sync` | tenant | per-entity replication cursor position, rows sent last pass, last error, page size in force |
  | `GET /diagnostics/settlement` | tenant | counts by `SettlementOutcome`, oldest unsettled `OperationId` age, dead-letter count, top 5 failing command types |
  | `GET /diagnostics/outbox` | operator | `OutboxDiagnostics` snapshot + degradation state |
  | `POST /diagnostics/bundle` | operator | builds and returns a `.zip` bundle |
  These are the server twins of B05's local diagnostics and the data source E01/E02 render.
- **Diagnostics bundle** (`src/Animora.Server/Diagnostics/ServerDiagnosticsBundle.cs`): a zip of
  redacted recent log files (through `LogRedaction`), the metrics snapshot, health/readiness output,
  migration + schema version list, option values **with every secret replaced by
  `"[redacted]"`**, worker states, and the last 200 `OperatorAlert` rows. It contains **no**
  business row, no connection string, no key material. `BundleHygieneTests` scans the produced zip
  for the known secret markers and fails on any hit.
- **Rate limiting — one policy set, declared once** (`src/Animora.Server/Security/RateLimiting.cs`,
  `System.Threading.RateLimiting` via ASP.NET Core's built-in middleware; no new third-party
  package):
  | Policy | Applies to | Limit |
  |---|---|---|
  | `auth` | `/api/identity/login`, `/refresh`, `/first-run` | fixed window, 10 req / 1 min **per (IP, username-hash)**, queue 0 |
  | `commands` | `/api/commands*` | token bucket, 120 req / min **per tenant**, burst 40, queue 10 |
  | `tables` | `/sync/*` | concurrency limiter, 8 concurrent **per tenant**, queue 16 |
  | `files` | `/api/files/*` | concurrency limiter, 4 concurrent per tenant, queue 8 |
  | `operator` | `/api/operator/*`, `/diagnostics/*` | fixed window, 120 req / min per operator user |
  `D03`'s prepared `.RequireRateLimiting("auth")` call sites are now activated (that is the whole
  D03 `TODO(D13)`). A rejection returns `429` with a `WireApiError` (`RATE_LIMITED`) and a
  `Retry-After` header — never a bare 429 body. Partition keys are composed with the same
  tenant-safe rule as caches (A-RULE 033).
- **Per-tenant fairness, not just per-route limits** (§16.1): the `commands` and `tables` partitions
  are keyed by tenant, so one busy clinic cannot consume another's share on a shared server. A test
  saturates tenant A and asserts tenant B's latency stays within 2× its unloaded baseline.
- **Connection and query tuning, measured choices not guesses:**
  - `DatabaseOptions` gains workload pool budgets from §PG.4 (combined maximum 12 on 2 GiB, 20 on 4 GiB)
    and MinPoolSize=0. One shared data source per workload; no tenant pools or unconstrained
    read/DP pool. Validate the SUM, reserving DB admin/backup capacity.
  - Read-only paths (diagnostics, operator lists, `/sync/*` pulls) use the
    `ReadConnectionStringName` D02 reserved, with `AsNoTracking` enforced by a
    `QueryTrackingBehavior.NoTracking` default on a second `DbContextOptions` profile.
  - `QueryHygieneTests` asserts: no `ToListAsync()` without a preceding `Take`/paging call in any
    server query file, and every `/diagnostics` and operator query is `AsNoTracking`.
  - EF Core **compiled models are not adopted** (T-RULE 013) — a comment records why.
- **Cache-aside with tenant-safe keys, bounded** (A-RULE 033, PERFORMANCE.md §15):
  `src/Animora.Server/Caching/TenantCache.cs` over `IMemoryCache` with a hard
  `SizeLimit` (`CacheOptions.MaxEntries = 5_000`) and a mandatory key composer
  `CacheKey.For(tenantId, userId, resource, version)` — the constructor is the **only** way to make a
  key, and a null/empty component throws. Existing caches (`MembershipCache` D04, the licence verdict
  cache D11) are migrated to it in this phase (two call-site changes, no behavior change).
  `CacheKeyTests` is `ARCHITECTURE.md` §19 gate L's unit half: tenant A never reads tenant B's entry,
  and omitting a component fails the test.
- **Reporting isolation is enforced, not hoped for** (A-RULE 034, §11.5, R-B35):
  `src/Animora.Server/Diagnostics/QueryBudget.cs` — a small helper that tags a query scope as
  `Oltp` or `Reporting`, applies finite measured timeouts (15 s OLTP, 15 s report initially), and runs Reporting scopes
  through `SemaphoreSlim(1)` sharing background admission/pool budget so aggregation can never occupy the OLTP connection budget.
  Every `/diagnostics` aggregate, every operator list and (later) every E02 insight query must open a
  `Reporting` scope; a test asserts no command handler opens one.
- **Capacity-measurement harness — published methodology, measured numbers** (§16.3, A-RULE 035):
  `scripts/sandbox/loadtest.sh` (new) drives
  `tests/Animora.Server.Tests/Harness/CapacityHarness.cs` — a self-contained client that runs a
  defined synthetic workload against a running server:
  ```
  Workload mix per virtual client per minute:
    12 reads (patient list, appointment day, invoice list)
     3 commands (1 invoice post, 1 payment, 1 stock movement)
     1 replication pull pass (5 entities)
  Ramp: 5 -> 10 -> 25 -> 50 virtual clients, 3 min per step, 1 min cooldown
  Recorded per step: p50/p95/p99 command latency, read p95, pull duration,
    outbox backlog peak, CPU%, working set, SQL wait summary, error rate
  Target hardware classes: 2 GiB / 1 core and 4 GiB / 2–3 cores, PostgreSQL 18 co-resident
  ```
  Output is written to `backend-roadmap/measurements/D13-capacity.md` (same folder C11 created)
  with the exact commit hash, hardware description and raw numbers. The harness **fails** only on an
  error rate > 1% or a p99 above `CapacityBudgets` (command 1500 ms, read 800 ms, pull 5000 ms);
  the capacity *number* is reported, never asserted. E08 re-runs this harness as the release gate.
- **Health endpoints gain the full check set** (D01 created two): `/health/ready` now reports
  `database`, `dataprotection`, `datapath`, `identity`, `replication`, `outbox`, `licensing`,
  `filestore` — each with `healthy | degraded | critical` and **no** secret in the payload.
  A degraded outbox or filestore keeps the server `200` (it can still serve); a broken database or
  key ring is `503`.
- **New error codes:** `RATE_LIMITED`, `OBSERVABILITY_ENDPOINT_NOT_LOCAL`,
  `DIAGNOSTICS_BUNDLE_FAILED`.

## Files to create / modify
| Path | Action | Contents (one line) |
|---|---|---|
| `src/Animora.Server/Diagnostics/ServerCorrelationMiddleware.cs` | create | 4-id correlation + response header |
| `src/Animora.Server/Diagnostics/AnimoraMetrics.cs` | create | meter + the 9 instruments |
| `src/Animora.Server/Diagnostics/ObservabilitySetup.cs` | create | OTel wiring, exporter only when local endpoint set |
| `src/Animora.Server/Diagnostics/ServerDiagnosticsBundle.cs` | create | redacted zip builder |
| `src/Animora.Server/Diagnostics/QueryBudget.cs` | create | OLTP vs Reporting scopes |
| `src/Animora.Server/Caching/TenantCache.cs` | create | bounded cache + `CacheKey.For` |
| `src/Animora.Server/Security/RateLimiting.cs` | create | the 5 policies + `429` shape |
| `src/Animora.Server/Endpoints/DiagnosticsEndpoints.cs` | create | 5 diagnostics routes |
| `src/Animora.Server/Health/HealthEndpoints.cs` | modify | the 8-check readiness payload (path per D01) |
| `src/Animora.Server/Tenancy/MembershipCache.cs` | modify | use `TenantCache` + `CacheKey.For` (path per D04) |
| `src/Animora.Server/Security/EntitlementMiddleware.cs` | modify | use `TenantCache` for the verdict cache |
| `src/Animora.Server/Endpoints/IdentityEndpoints.cs` | modify | activate `.RequireRateLimiting("auth")` |
| `src/Animora.Server/Endpoints/CommandEndpoints.cs` | modify | `.RequireRateLimiting("commands")` |
| `src/Animora.Server/Tables/TableOptionsFactory.cs` | modify | `.RequireRateLimiting("tables")` on the table group |
| `src/Animora.Server/Configuration/DatabaseOptions.cs` | modify | pool sizes |
| `src/Animora.Server/Configuration/CacheOptions.cs` | create | `MaxEntries`, TTLs |
| `src/Animora.Server/Program.cs` | modify | correlation, OTel, rate limiter, diagnostics, health |
| `Directory.Packages.props` | modify | 5 OpenTelemetry pins |
| `scripts/sandbox/loadtest.sh` | create | drives the capacity harness, writes the measurement file |
| `tests/Animora.Server.Tests/Harness/CapacityHarness.cs` | create | the published workload mix |
| `tests/Animora.Server.Tests/MetricsHygieneTests.cs` | create | tag allow-list, no raw tenant id |
| `tests/Animora.Server.Tests/BundleHygieneTests.cs` | create | no secret, no business row in the zip |
| `tests/Animora.Server.Tests/RateLimitingTests.cs` | create | each policy, `429` shape, tenant fairness |
| `tests/Animora.Server.Tests/CacheKeyTests.cs` | create | gate L unit half |
| `tests/Animora.Server.Tests/QueryHygieneTests.cs` | create | paging + `AsNoTracking` + reporting scope rules |
| `tests/Animora.Server.Tests/ObservabilityIsolationTests.cs` | create | runs with no collector (gate V half) |

## Implementation steps
1. Add the OpenTelemetry pins; write `AnimoraMetrics` and `ObservabilitySetup` (exporter registered
   only when a local endpoint is configured).
2. Write `ServerCorrelationMiddleware` and place it first after the exception handler.
3. Instrument the existing call sites with **one line each**: `CommandIntakeService` (settled,
   duration, replays), the table controllers' pull path (`sync.*`), `OutboxDiagnostics` → the
   observable gauges. No handler logic changes.
4. Write `TenantCache` + `CacheKey.For` + `CacheOptions`; migrate `MembershipCache` and the licence
   verdict cache to them.
5. Write `RateLimiting` and attach the policies to the five route groups (D03's prepared call sites
   included).
6. Write `QueryBudget`; open `Reporting` scopes in the diagnostics and operator queries.
7. Add the pool-size options and the no-tracking read profile.
8. Write `DiagnosticsEndpoints` and `ServerDiagnosticsBundle`; extend `/health/ready` to eight
   checks.
9. Write `CapacityHarness` + `scripts/sandbox/loadtest.sh`; run one full ramp and commit
   `backend-roadmap/measurements/D13-capacity.md` with the real numbers and the hardware line.
10. Write the six test classes.
11. `build.sh full` = 0/0; `test.sh` = `TESTS OK`; `run.sh` = `SMOKE RUN OK`.

## Contracts / schema added
- Server: `ServerCorrelationMiddleware`, `ServerCorrelation`, `AnimoraMetrics` (9 instruments),
  `ObservabilitySetup`, `ServerDiagnosticsBundle`, `QueryBudget`, `TenantCache`, `CacheKey`,
  `CacheOptions`, `RateLimiting` (5 policies), `DiagnosticsEndpoints` (5 routes), the 8-check
  readiness payload, `DatabaseOptions.MaxPoolSize`/`MinPoolSize`.
- Scripts: `scripts/sandbox/loadtest.sh`.
- Docs: `backend-roadmap/measurements/D13-capacity.md` (measured numbers + methodology).
- Packages: 5 OpenTelemetry pins (local exporter only).
- `ErrorCodes` += 3. **No migration**, no `Animora.Contracts` change, no `CommandTypes` entry, no DI
  change in `App.axaml.cs`.

## Rules that apply here
- **A-RULE 016 / 032 / R-B27** — no exporter without a configured local endpoint; removing the
  collector must not break a single request; nothing leaves the box.
- **A-RULE 033** — `CacheKey.For` is the only key factory; a missing component throws; metric tags
  carry a salted tenant hash, never a raw id.
- **A-RULE 034 / R-B35** — `QueryBudget` physically separates reporting from OLTP (own timeout, own
  semaphore); no command handler may open a `Reporting` scope.
- **A-RULE 035 / R-B07 / §16.3** — the capacity number comes from `loadtest.sh` with a published
  workload, ramp and hardware class; the harness records, the budgets gate.
- **§16.1** — rate-limit partitions are per tenant so one clinic cannot starve another on the 1–3-core
  box; the worker bulkhead from D09 remains in force.
- **PERFORMANCE.md §16 / CONVENTIONS §7** — no hot-path logging; the bundle and logs are redacted;
  no secret and no business row appears in any diagnostic output.
- **T-RULE 013** — compiled models stay unadopted; the decision is recorded in code.

## Out of scope
- Client-side diagnostics and bundle export → already B05; unchanged here.
- The operator UI that renders these diagnostics → E01/E02.
- Formal recording of `ARCHITECTURE.md` §19 gates and the release-blocking capacity verdict → E08
  (this phase supplies the harness and the gate L / gate V unit halves).
- Horizontal scale-out, SignalR backplane, separate reporting database → deferred
  (`ROADMAP.md` §5).
- Any external metrics backend or APM — permanently excluded.

## Acceptance criteria
- Build: `scripts/sandbox/build.sh full` = 0 warnings / 0 errors.
- **Bounded `MembershipCache` (audit 2026-10-08 · OPT-08):** the in-memory `MembershipCache` is a **bounded LRU** (configured max entries,
  sliding expiry) so a many-tenant node cannot grow it without limit; a test asserts eviction at the cap and no unbounded growth.
- **`QueryPlanBudgetTests` — EXPLAIN-plan / query-time budget gate (audit 2026-10-08 · OPT-05):** the
  five heaviest sync + report queries (the `Pet`/`Visit` delta pull via `vw_Sync<Entity>`, the
  `Invoice` delta pull, the `DailyFinanceFacts` range read, the owner-statement query, the
  low-stock report) run against a seeded 100k-row tenant on the **minimum** server spec; each must
  show an index **seek** on `IX_<Table>_TenantId_UpdatedAt` (or its report index) — no table/clustered
  scan, no key-lookup storm — and finish under its recorded time budget. A regression fails the gate.
- `ObservabilityIsolationTests` green — **gate V (server half)**: with `OtlpEndpoint` empty the
  server starts, serves, logs and exposes `/diagnostics/metrics`; with an endpoint configured and the
  collector absent, request success rate stays 100% and no unhandled exception is logged; a non-local
  endpoint fails startup with `OBSERVABILITY_ENDPOINT_NOT_LOCAL`.
- `MetricsHygieneTests` green: every instrument's tag keys are inside the allow-list; no tag value
  equals a raw `TenantId`, user id, patient id or money amount.
- `BundleHygieneTests` green: the produced zip contains the expected sections and **zero** matches
  for connection-string, key-material or password markers, and no business table row.
- `RateLimitingTests` green: each of the five policies rejects at its limit with `429` +
  `RATE_LIMITED` + `Retry-After`; saturating tenant A keeps tenant B's p95 within 2× baseline; the
  auth policy partitions by (IP, username-hash).
- `CacheKeyTests` green — **gate L (unit half)**: tenant A never receives tenant B's cached value;
  omitting tenant, user, resource or version from a key throws.
- `QueryHygieneTests` green: no unbounded `ToListAsync()` in a server query; every diagnostics and
  operator query is `AsNoTracking` and opens a `Reporting` scope; no command handler opens one.
- `/health/ready` returns all eight checks with per-check status, `503` only for database or key-ring
  failure.
- `scripts/sandbox/loadtest.sh` completes a full 5→50 ramp, writes
  `backend-roadmap/measurements/D13-capacity.md` with p50/p95/p99, resource use and the hardware
  line, and exits non-zero only on the documented budget/error-rate breach.
- Desktop unaffected: `scripts/sandbox/run.sh` = `SMOKE RUN OK`.

## PROGRESS.md update
Mark D13 done; advance "Next phase" to `E01`; append a "Repo state" entry listing:
`ServerCorrelationMiddleware` + the 4 correlation ids + `X-Animora-Correlation`, `AnimoraMetrics`'s 9
instruments and the **salted `tenant_hash`** rule, `ObservabilitySetup`'s no-endpoint-no-exporter
rule, the 5 `/diagnostics` routes (**E01/E02 render them**), `ServerDiagnosticsBundle` hygiene,
`RateLimiting`'s 5 policies and per-tenant partitions (**D03's `auth` TODO is now closed**),
`TenantCache` + `CacheKey.For` as the only key factory (**every later cache must use it**),
`QueryBudget`'s OLTP/Reporting split (**E02 insight queries must use `Reporting`**), the pool sizes,
the 8-check readiness payload, `loadtest.sh` + `CapacityHarness` + the committed
`measurements/D13-capacity.md` (**E08 re-runs it**), the gate L/V unit halves, the 3 new error codes,
deviations.

## PostgreSQL resource evidence
Use pg_stat_statements/pg_stat_activity/pg_locks/vacuum/WAL/archive metrics, sampled protected plans
and local request histograms. Measure combined DB+app+OS memory, queue/pool/gate waits, backlog and
disk growth. Exact constrained profiles are §PG.4; unconstrained evidence cannot pass the smallest
production profile. No full observability stack co-hosted on 2 GiB, and no disabled vacuum/durability.
