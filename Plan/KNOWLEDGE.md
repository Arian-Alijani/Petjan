# Plan/KNOWLEDGE.md — distilled facts for the test-system task

> Purpose: give the next model (fresh session) every fact it needs so it can start
> implementation with near-zero repo exploration. Read this file + TODO.md only.
> All facts below were verified against the repo on 2026-10-19 (branches synced:
> main == genspark_ai_developer == `ec937740`). Caveman-ultra voice is ON.

---

## 0. Mission (what we are building)

A **real, network-based test system** that runs **from the user's own machine**
against the **live production server at `188.212.99.234` (HTTP, port 5000)**. Two goals:

1. **Correctness / sync suite** — real auth + real commands + real pull/push for
   EVERY feature (vaccination, appointment, inventory, sales, cashier, finance,
   boarding, patients, purchasing, cheques …). Create + edit scenarios, multi-device
   sync, conflict, offline-queue-then-push, idempotent retry. Find every possible
   desync / error.
2. **Capacity / load suite** — realistic concurrent users driving real requests,
   while **remotely monitoring the server hardware** (CPU, RAM, PostgreSQL
   connections, service health) to learn how many clinics this hardware can serve.

Hard constraints (locked):
- MUST NOT damage the real server/data: no destructive ops on real tenants, no
  security holes. Use a **dedicated test tenant** provisioned for the purpose;
  tear down test data where possible. Read-only/idempotent where possible.
- Reports: tidy, understandable, **English**.
- Run from the user's system (real-world fidelity), not CI-in-memory.
- The .NET app in `src/` is the product under test. Do NOT open `Animora-UI/`.

---

## 1. Branch sync state (Task 0 — DONE)

- Remotes: `origin` = `github.com/Arian-Alijani/Petjan.git`.
- Before: `genspark_ai_developer` was 1 commit ahead of `main` (`ec937740`
  "fix(inventory): preserve aggregate concurrency…"). `main` 0 ahead.
- Action taken: fast-forwarded `main` to `ec937740`, pushed `main`, pushed
  `main:genspark_ai_developer`. Now both == `ec937740`. No progress lost.
- Push protocol (LOCKED): after EVERY completed TODO task run
  `scripts/git/push.sh "type(scope): msg"`. It does add -A → commit → fetch →
  rebase --autostash → push. Exit 3 = creds missing → call
  `setup_github_environment` tool once, re-run. (Creds already set this session.)

---

## 2. The product & the server (verified facts)

- **Animora** — SaaS vet-clinic management + accounting. One central multi-tenant
  **ASP.NET Core 10** server; offline-first **Avalonia desktop** client that syncs.
- Server project: `src/Animora.Server/` (host `Program.cs`). Persistence:
  `src/Animora.Persistence.PostgreSql/` (Npgsql/EF). PostgreSQL 18.
- Live server runtime (from `راهنمای-راه-اندازی-سرور.md`):
  - `ASPNETCORE_URLS=http://0.0.0.0:5000`, `ASPNETCORE_ENVIRONMENT=Production`.
  - systemd service name `animora`; PostgreSQL local on `127.0.0.1:5432`.
  - DB: `animora`; app role `animora_app`, owner role `animora_owner` (migrations
    run as owner; app role never migrates). Pools total 12 (6 write + 3 read +
    1 key ring + 2 worker); `max_connections >= pool sum + 8`. 2 GiB profile = 30
    conns, 4 GiB = 40 (`deploy/postgresql/*.conf`).
  - Firewall: port 5000 open to internet (HTTP, no TLS yet — fine for test).
  - SSH is available to the server (guide uses it throughout) → **remote monitor
    via SSH** is the intended mechanism.

### Server route prefixes — `src/Animora.Server/Hosting/ServerRoutes.cs`
```
/api/commands   (command intake)      /sync      (state pull feed)
/finance        (authoritative $ read)/auth      (login/refresh/logout/device)
/operator  /provisioning  /licensing  /files  /admin
/health/live  /health/ready  /health/startup   (anonymous)
```

### Auth surface — `src/Animora.Server/Auth/*`
- `POST /auth/login`, `/auth/refresh`, `/auth/logout`, `/auth/device/register`
  (rate-limit policy `Auth`). Tokens are **opaque Data-Protection bearer tokens**
  (NOT JWT). `GET /auth/keys` anonymous. `POST /auth/device/{id}/approve|revoke|
  reinstate`, `/auth/capabilities`. Staff: `GET/POST /auth/staff…`.
- Device must be active (`CoarsePolicies.DeviceActive`) + tenant member
  (`CoarsePolicies.TenantMember`) for business routes.

### Command intake — `src/Animora.Server/Commands/CommandEndpoints.cs`
- `POST /api/commands` (single), `POST /api/commands/batch` (≤ **50**, 1 MiB limit,
  sequential, one txn each, order is a contract), `GET /api/commands/{operationId}/ids`.
- Requires auth + rate-limit `Commands`. Idempotency key = `(tenant, OperationId)`
  (A-RULE 005). Re-submitting same OperationId returns stored result, no dup effect.

### Sync pull — `src/Animora.Server/Sync/SyncEndpoints.cs`
- `GET /sync/manifest` → `SyncManifest(version, epoch, generation, projectionNames[],
  pageTargetBytes)`.
- `POST /sync/batches` body `OpenBatchRequest(Epoch, FromSequence)` (incremental).
- `POST /sync/snapshots` (full) → opens a frozen batch.
- `GET /sync/batches/{batchId}/pages/{ordinal}` → page of `SyncRecord[]`.
- Requires TenantMember + DeviceActive; rate-limit `Sync`; back-pressure =
  HTTP 503 + `Retry-After: 1` (`SnapshotLifetimeExceeded` / `AdmissionSaturated`)
  — EXPECTED, not a defect.

### Finance read — `/finance` (`FinanceEndpoints.cs`) authoritative invoices/payments since cursor.

---

## 3. Wire protocol (so the harness speaks the exact real thing)

- **`CommandEnvelope`** (`src/Animora.Contracts.Wire/Commands/CommandEnvelope.cs`):
  fields = ContractVersion, OperationId(ULID), TenantId, UserId, DeviceId,
  CommandType, AggregateType, AggregateId?, PayloadJson, CommandFingerprint
  (SHA-256 hex over `CommandType + sep + canonical payload`, A-RULE 019),
  CreatedAtUtc, LocalSequence, CausationId?, DependsOnOperationId?,
  ExpectedAggregateVersion?, ExpectedServerVersion? (UUID, v2 optimistic concurrency),
  RuleSetVersion?. Build via `CommandEnvelope.Create(...)` (stamps WireContract.Version).
- **Server ignores envelope TenantId** and re-resolves tenant from bearer membership
  (A-RULE 012). Identity fields are NOT in the fingerprint → idempotent retries stay
  byte-identical.
- **~94 command types** in `src/Animora.Contracts.Wire/Commands/CommandTypes*.cs`,
  format `<context>.<aggregate>.<verb>`. Examples:
  `inventory.product.create/update/delete`, `inventory.stock.adjust/set`,
  `inventory.receipt.post`, `sales.invoice.create/post/void/refund/line.add…`,
  `sales.payment.register`, `cashier.session.open/close`, `cashier.movement.record`,
  `purchasing.order.create/submit/cancel`, `purchasing.supplier.create`,
  `finance.expense.post/reverse`, `finance.period.close`, plus patients / visits /
  vaccination / appointments / boarding verbs.

### REUSE, do NOT reimplement the wire client
- `src/Animora.Sync.Custom/` is the **real production client transport**:
  - `HttpCommandTransport` → `POST api/commands` + `api/commands/batch`
    (`MaxBatchSize=50`), implements `ICommandTransport` + `ICommandBatchTransport`.
    Handles bearer auth, rebind of tenant/user/device from session, deferred retry
    on 429/5xx/network, contract-unsupported → resync.
  - `SyncFeedClient` → manifest/batches/snapshots/pages pull.
  - `BearerAuthHandler`, `NetworkLoggingHandler`, `ServerEndpointOptions`
    (set base URL here → `http://188.212.99.234:5000`), `SqliteSyncApplyStore`,
    `ReplicaMaterializer`, `SettlementReconciler`, `ShadowResyncHandler`.
  - DI wire-up: `SyncCustomServiceCollectionExtensions.cs`.
- Command **payload DTOs / factories** live in `src/Animora.Application/UseCases/<Context>/`
  (e.g. `CreateSupplierCommand` used by the existing capacity driver). Use these to
  build real payloads rather than hand-rolling JSON.
- Local store for a realistic client replica: `src/Animora.Persistence.Sqlite/` +
  `src/Animora.Client.Services/` (24 real service impls). A "virtual client" =
  Application dispatch + Sqlite replica + Sync.Custom transport pointed at live server.

---

## 4. What ALREADY exists for testing (reuse, don't duplicate)

- `tests/Animora.Server.Tests/Capacity/`:
  - `CapacityDriver.cs` — **in-process** (WebApplicationFactory) + **local PG
    fixture** capacity driver. Drives intake + state-pull, records throughput,
    p50/p95/p99, peak RSS, PG connections → `artifacts/server-capacity.json`.
    **NOT what the user wants** (it is CI/in-memory, not real network vs live
    server, no remote hardware monitor). But its building blocks are gold:
  - `CapacityProfiles.cs` — **REUSE THESE RECORDS/CLASSES**: `ProfileResult`,
    `CapacityReport`, `LoadRunner.RunAsync(name, concurrency, total, op)`
    (closed-loop worker pool returning `Outcome` {Ok, Unexpected, ExpectedBusy}).
    `ResourceSampler` samples local RSS + PG conns — we need a **remote** variant.
- `scripts/perf/server-capacity.sh` — runs the in-process driver in a cgroup
  container. Reference for report shape only.
- `scripts/grant-test-subscription.sh` — run ON the server; creates an active
  100-day `standard` plan + Subscription + LicenseGrant for a tenant code, and
  optionally a Manager user. **This is how we license the dedicated test tenant.**
- `tests/Animora.Server.Tests/` dirs: HandlersS07/S08/S10, Sync, Persistence,
  Authorization, Commands, Files — real handler tests (local PG), reference for
  per-feature command/payload shapes + expected settlement outcomes.
- `.NET SDK is NOT on PATH in this sandbox` — guide installs to `/usr/share/dotnet`.
  The harness is meant to RUN ON THE USER'S machine; in sandbox we can still
  `dotnet build` once SDK is restored via `scripts/sandbox/build.sh` (uses
  `require_dotnet`). Do NOT run full/long builds (rule). Build only the new project.

---

## 5. Design decision (the chosen architecture)

Build a **new console app** `tests/Animora.LiveTest/` (xUnit NOT suitable — this is
an operator-run tool, not a gated unit test). Structure:

```
tests/Animora.LiveTest/
  Animora.LiveTest.csproj      refs: Animora.Sync.Custom, Animora.Application,
                               Animora.Client.Services, Animora.Persistence.Sqlite,
                               Animora.Contracts(.Wire). (Real client stack.)
  Program.cs                   CLI: `correctness` | `capacity` | `all`; reads config.
  Config/LiveTestOptions.cs    server URL(188.212.99.234:5000), tenant code, creds,
                               SSH host/user/key for monitor, concurrency levels.
  Client/VirtualClient.cs      one simulated desktop: login→device register→session,
                               wraps HttpCommandTransport + SyncFeedClient + a
                               throwaway temp SQLite replica. Byte-real requests.
  Correctness/*Scenario.cs     one file per feature: build real command via
                               Application factory → submit → pull on a 2nd client →
                               assert convergence; conflict + offline-queue + retry.
  Capacity/RemoteMonitor.cs    SSH into 188.212.99.234: samples `free -m`, CPU via
                               /proc/stat or `top -bn1`, `ss -s`, and PG
                               `SELECT count(*),state FROM pg_stat_activity`. Peak +
                               timeseries. NON-INVASIVE, read-only.
  Capacity/LoadProfiles.cs     realistic mixes (reception day, sales rush, inventory
                               receipt, month-end finance). REUSE LoadRunner/
                               ProfileResult/CapacityReport from the Capacity tests
                               (link the files or copy the records).
  Reporting/ReportWriter.cs    English Markdown + JSON to artifacts/live-test/.
  README.md                    how the user runs it from their machine (English).
```

Safety: dedicated tenant (e.g. code `ANIMORA-LOADTEST`), licensed via
`grant-test-subscription.sh`. All created entities tagged/namespaced
(`cap-…`, `lt-…`). No deletes on non-test data. Load bounded + ramped;
honor 503/Retry-After; stop on first unexpected 5xx in correctness mode.

---

## 6. Open items to confirm at implementation time (cheap, targeted)

- Exact `POST /auth/login` request/response record names → read
  `src/Animora.Server/Auth/AuthEndpoints.cs` (LoginAsync) + the request DTO.
- `ServerEndpointOptions` fields → `src/Animora.Sync.Custom/ServerEndpointOptions.cs`.
- A minimal real payload per feature → copy from matching
  `tests/Animora.Server.Tests/HandlersS0*/` test (they build the exact command).
- SSH credentials for the monitor: the harness reads them from env/config; the
  USER supplies them at run time (do NOT hardcode). If SSH is unavailable, monitor
  degrades to client-observed latency + the server's own `/health/ready` probe
  counters only (documented fallback).

---

## 7. Rules recap (do not violate)

- After each TODO task: `scripts/git/push.sh "msg"` (LOCKED). Skipping = failure.
- Targeted reads only; no blind exploration; no full/long builds; no `Animora-UI/`.
- Standard, high-quality, integrated, token-efficient output; English reports.
- Final TODO task: delete `Plan/TODO.md` (and this file is removed with it — see
  TODO.md final step). Touch nothing else, no roadmap edits.
- Heavy fixes / large code written mid-task → push with a clear message.
