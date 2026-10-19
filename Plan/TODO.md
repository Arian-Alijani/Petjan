# Plan/TODO.md — build the live test system

> Read `Plan/KNOWLEDGE.md` first (all distilled facts). This file = ordered task
> list with status. **LOCKED RULE: after completing each task, run
> `scripts/git/push.sh "msg"`.** Status legend: [ ] pending · [~] in progress ·
> [x] done. Voice: caveman-ultra.

Target artifact: `tests/Animora.LiveTest/` — operator-run console tool that drives
the LIVE server `http://188.212.99.234:5000` with REAL requests, for (A) per-feature
correctness/sync and (B) capacity/load with remote hardware monitoring. English reports.

---

## T0 — Branch sync  [x] DONE (pre-plan)
main == genspark_ai_developer == `ec937740`, pushed both. No progress lost.
See KNOWLEDGE §1. (No push needed; already pushed.)

## T1 — Plan folder + knowledge + todo  [x] DONE
- [x] Create `Plan/`.
- [x] Write `Plan/KNOWLEDGE.md` (distilled facts).
- [x] Write `Plan/TODO.md` (this file).
- [x] Pushed.

## T2 — Project scaffold  [x] DONE
Created `tests/Animora.LiveTest/` console project, added to `Animora.sln`, builds 0W/0E.
Real client-stack refs; SSH monitor = shell-out (no new pkg). Reused LoadRunner/
ProfileResult as Npgsql-free `Capacity/LoadHarness.cs` (linking the CI file pulled Npgsql).
`Program.cs` CLI (preflight|correctness|capacity|all) + `Config/LiveTestOptions.cs`.
Detail below retained for reference.
### (original T2 detail)
Goal: new console project wired into solution, builds clean (new project only; no
full-solution long build — rule).
- Create `tests/Animora.LiveTest/Animora.LiveTest.csproj` (net10.0, exe). Reference
  real client stack: `Animora.Sync.Custom`, `Animora.Application`,
  `Animora.Client.Services`, `Animora.Persistence.Sqlite`,
  `Animora.Contracts`, `Animora.Contracts.Wire`, `Animora.Infrastructure.Local`.
  Match central package versions (`Directory.Packages.props`); add `System.Text.Json`,
  an SSH client pkg (prefer `SSH.NET`) only if centrally pinnable — else shell out to
  system `ssh`/`scp` (sandbox-safe, no new pkg). DECIDE: default = shell-out `ssh`,
  zero new dependency.
- Add project to `Animora.sln` (`dotnet sln add`).
- `Program.cs`: CLI `correctness | capacity | all | preflight`, parse
  `LiveTestOptions` from env + optional `livetest.json`.
- `Config/LiveTestOptions.cs`: ServerBaseUrl (default `http://188.212.99.234:5000`),
  TenantCode, Username, Password, DeviceName, Ssh{Host,User,KeyPath,Enabled},
  Concurrency levels, RequestsPerLevel, OutputDir.
- Build ONLY this project: `scripts/sandbox/build.sh` is full — instead
  `dotnet build tests/Animora.LiveTest/Animora.LiveTest.csproj -c Debug`.
- Push: `scripts/git/push.sh "feat(livetest): scaffold live-server test console"`.

## T3 — Live auth + VirtualClient  [x] DONE
Implemented `Client/VirtualClient.cs` (login, device label, refresh, `BuildEnvelope` via real `CommandFingerprint`, `SubmitAsync/SubmitWithRetryAsync/SubmitBatchAsync` via real `HttpCommandTransport`, `PullAsync` via real `SyncFeedClient` into in-memory `ReplicaView`), `MemorySessionStore`, `SubmitOutcome/PullResult`, `Client/Preflight.cs` (health→login→licence→manifest→logout). Wired `preflight` in Program.cs.
Goal: one simulated desktop that logs in to the live server and holds a real session.
- Read exact shapes first (targeted): `src/Animora.Server/Auth/AuthEndpoints.cs`
  (LoginAsync request/response), `ServerEndpointOptions.cs`,
  `BearerAuthHandler.cs`, `SyncCustomServiceCollectionExtensions.cs`.
- `Client/VirtualClient.cs`:
  - Build DI/HttpClient pointed at ServerBaseUrl.
  - Flow: `POST /auth/device/register` (if needed) → `POST /auth/login` → capture
    bearer + refresh → fetch `/auth/capabilities` / `/licensing/status`.
  - Expose `SubmitAsync(CommandEnvelope)` via real `HttpCommandTransport`, and
    `PullAsync()` via `SyncFeedClient` into a throwaway temp SQLite replica
    (`Path.GetTempPath()`, deleted on dispose).
  - Helper to build real envelopes from Application use-case command objects
    (reuse `CommandEnvelope.Create`, compute fingerprint like production does —
    find the production fingerprint helper, reuse it, do NOT re-derive).
- `preflight` command: GET `/health/ready`, login, one no-op read → prints PASS/FAIL
  so user verifies connectivity before heavy runs.
- Safety: uses the dedicated test tenant only. No destructive calls here.
- Push: `scripts/git/push.sh "feat(livetest): live auth + virtual client over real transport"`.

## T4 — Correctness / sync scenarios  [x] DONE
Implemented `Correctness/` (ScenarioContext, Fixtures, 8 scenario files = 36 scenarios: Protocol, Patients, Reception/Appointments/Visits/Vaccination, Boarding, Inventory, Purchasing, Sales/Cashier, Finance/Cheques), `CorrectnessRunner`, `Reporting/CorrectnessReport.cs` (Markdown+JSON), wired `correctness|all` in Program.cs, env `ANIMORA_LT_FILTER` / `ANIMORA_LT_KEEP_GOING`. Project added to CI `core` scope (`scripts/ci/_projects.sh`) so GitHub Actions builds it.
Detail retained below.
Goal: real create+edit per feature, 2-client convergence, conflict, offline-queue,
idempotent retry. Find every desync/error.
- For each feature below, add `Correctness/<Feature>Scenario.cs`. Build the real
  command via the matching Application use-case (copy exact payload from the twin
  test in `tests/Animora.Server.Tests/HandlersS0*/` — targeted read per feature):
  - Patients (create/edit owner+pet), Reception/Visits (triage), Visit encounter +
    **Vaccination** (create appt→administer→SMS template render, no send),
    **Appointments** (book/reschedule/cancel), Boarding (check-in/out),
    **Inventory** (product create/update/delete, stock adjust/set, receipt post —
    also the `ec937740` aggregate-concurrency path: parent+child price update),
    Purchasing (supplier/order create→submit→receipt→cancel), **Sales/POS**
    (invoice create→lines→discount→insurance→post→payment→void/refund),
    **Cashier** (session open→movement→close), **Cheques**, **Finance**
    (expense post/reverse, period close, read `/finance`).
- Scenario pattern (each): clientA submit → assert `SettlementOutcome.Accepted` →
  clientB pull → assert entity converges (same server id/version) → mutate on B →
  A pull → assert convergence. Add:
  - **Conflict**: two clients edit same aggregate with stale `ExpectedServerVersion`
    → assert one settles `Conflict`, loser resyncs.
  - **Offline queue**: queue N commands offline (replica only) → go online → push →
    assert all settle, order preserved, no dup (resubmit same OperationId → stored
    result).
  - **Idempotent retry**: submit, then resubmit identical envelope → same result, no
    second effect (verify via `/api/commands/{op}/ids` or feed count).
- Stop on first UNEXPECTED failure (not on expected Conflict/Deferred/503).
- Namespacing: all created data prefixed `lt-` + run id; where a delete/void verb
  exists, clean up at end. No touching non-`lt-` data.
- `Reporting`: per-scenario PASS/FAIL table (English) + full detail on failure
  (op id, command type, settlement outcome, error code, both clients' versions).
- Push: `scripts/git/push.sh "feat(livetest): per-feature correctness + sync scenarios"`.

## T5 — Remote hardware monitor  [x] DONE
Implemented `Capacity/RemoteMonitor.cs` (one long-lived read-only `ssh sh -s` session streaming CPU/load/RAM/swap/established sockets/service RSS/PG conns by state), `MonitorModels.cs`, `MonitorAnalysis.cs` (`HealthProbe` for `/health/ready` without SSH + window `Summarize`). Failure of the monitor never fails a load run.
Goal: watch the server's real hardware during load, read-only, non-invasive.
- `Capacity/RemoteMonitor.cs`: if `Ssh.Enabled`, spawn background sampler that every
  ~1s runs over `ssh user@188.212.99.234`:
  - CPU: 2 reads of `/proc/stat` → % busy (or `top -bn1 | grep Cpu`).
  - RAM: `free -m` (total/used/available), swap.
  - Sockets: `ss -s` (estab count).
  - PG: `sudo -u postgres psql -tAc "select count(*),state from pg_stat_activity
    group by state"` (conn count by state) + `max_connections`.
  - Service: `systemctl is-active animora` + GET `/health/ready` status.
  - Load avg: `/proc/loadavg`.
  Record timeseries + peaks (peak RSS of `animora`, peak PG conns, peak CPU, min
  free RAM). Guard: tiny commands, reuse one ssh ControlMaster if possible; never
  write to server; abort cleanly on monitor error (don't fail the load run).
- Fallback (Ssh.Enabled=false): client-observed latency/throughput + `/health/ready`
  only; report notes "server-side metrics unavailable".
- Push: `scripts/git/push.sh "feat(livetest): read-only remote server hardware monitor"`.

## T6 — Capacity load profiles + report  [x] DONE
Implemented `Capacity/LoadProfiles.cs` (4 profiles: reception-day, sales-rush, inventory-receipt, sync-pull; device pool, shared sessions beyond licence cap), `CapacityAnalysis.cs` (health judge + CPU/RAM/PG-conn limiter diagnosis + "upgrade X" hint), `CapacityRunner.cs` (ramp, cooldown, stop-at-first-broken-level, test-tenant guard, partial report on Ctrl+C), `Reporting/CapacityReport.cs` (English MD + JSON with time series), `capacity`/`all` wired in Program.cs. Back-pressure 503/429 = ExpectedBusy.
Goal: realistic concurrent load → capacity reference, correlated with hardware.
- `Capacity/LoadProfiles.cs`: reuse `LoadRunner` + `ProfileResult` + `CapacityReport`
  from `tests/Animora.Server.Tests/Capacity/CapacityProfiles.cs` (link the file via
  `<Compile Include>` or copy the 3 records — prefer link to avoid drift). Define
  realistic MIXED profiles, each = weighted real operations via VirtualClient:
  - "reception-day": patient lookups + visit create + appointment book (read-heavy).
  - "sales-rush": invoice create→post→payment + cashier movement (write-heavy $).
  - "inventory-receipt": product update + stock adjust + receipt post.
  - "sync-pull": full snapshot + incremental batches + pages (state feed).
  Ramp concurrency levels (e.g. 1,2,4,8,16,32 — configurable), N requests each.
  Each virtual user = own VirtualClient (own session/device/replica).
- Wrap the whole run with `RemoteMonitor` active; attach peaks + timeseries to report.
- Honor server back-pressure: 503+Retry-After = `Outcome.ExpectedBusy`, retry per
  header, NOT a failure. Bound total load; ramp; cooldown between levels.
- `Reporting/ReportWriter.cs`: English Markdown + JSON to `artifacts/live-test/`:
  per-profile throughput + p50/p95/p99, server peak CPU/RAM/PG-conns timeseries,
  the level where latency/err breaks → **recommended max concurrent clients** and
  **"upgrade X" hint** (CPU-bound vs RAM-bound vs PG-conn-bound). Clearly labelled
  "planning reference only" (A-RULE 035).
- Safety: capacity data uses the test tenant; created rows prefixed `cap-`; optional
  post-run cleanup. Never run against real-clinic tenants.
- Push: `scripts/git/push.sh "feat(livetest): realistic load profiles + correlated capacity report"`.

## T7 — Runner scripts + README  [x] DONE
Added `scripts/perf/live-correctness.sh`, `live-capacity.sh`, shared `_livetest-common.sh`, and English `tests/Animora.LiveTest/README.md`. Per rule: no build/test run in sandbox; CI builds the project (scope `core`).
- `scripts/perf/live-capacity.sh` + `scripts/perf/live-correctness.sh`: thin wrappers
  (`dotnet run --project tests/Animora.LiveTest -- <mode>`), read env, print artifact
  paths. English comments.
- `tests/Animora.LiveTest/README.md` (English): prerequisites (.NET 10 SDK, SSH key,
  test tenant via `grant-test-subscription.sh`), env vars, how to run `preflight`
  then `correctness` then `capacity`, how to read the report, safety notes.
- Build only the new project to confirm green (no full/long build).
- Push: `scripts/git/push.sh "docs(livetest): runner scripts + English README"`.

## T8 — Final cleanup  [x] DONE
- Delete `Plan/TODO.md` AND `Plan/KNOWLEDGE.md` (remove the whole `Plan/` working
  set). Touch nothing else; no roadmap edits.
- Push: `scripts/git/push.sh "chore(plan): remove completed live-test plan files"`.

---

### Status tracker
| Task | State |
|---|---|
| T0 branch sync | x |
| T1 plan files | x |
| T2 scaffold | x |
| T3 auth+client | x |
| T4 correctness | x |
| T5 monitor | x |
| T6 load+report | x |
| T7 scripts+readme | x |
| T8 cleanup | x |
