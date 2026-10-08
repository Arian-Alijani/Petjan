# Phase E08 — Validation gates & performance proof

**Band:** M · **Track:** E · **Depends on:** E07, D13, E02 · **Satisfies:** R-B07, R-B08, R-B10, R-B19, R-B32
**Runnable after this phase:** desktop offline yes · server required no (gate harness runs headless;
the server-dependent gates are skipped, not failed, when no server/SQL Server is present)

## Read before starting
- `backend-roadmap/CONVENTIONS.md` §1, §2, §9, §11, §12
- `backend-roadmap/PROGRESS.md` (B02 entry — `PendingCommand`, `CommandFingerprint`, settlement
  state machine, `CausationId`/`DependsOnOperationId`, `ExpectedAggregateVersion`; B04 entry —
  `OfflineCapabilityPolicy`, `DeviceCapabilitySnapshot`, `CapabilityEvaluator`; B05 entry —
  `AuditWriter`, correlation-id set (TenantId/OperationId/DeviceId/RequestId); A11 entry —
  `sqlite_version()` startup assertion, `ISecretStore`, encryption provider seam, WAL/backup/restore,
  `InstallationIdentity`; C11 entry — the offline end-to-end scenario script + PERFORMANCE budget
  numbers; D02 entry — named `Tenant`/`SoftDelete` filters, `rowversion`; D04 entry — tenant
  resolution, tenant-safe cache-key composition; D05 entry — idempotency store `(TenantId,
  OperationId)`, fingerprint-mismatch integrity error, `DependsOnOperationId` ordering; D08 entry —
  `TableController<T>`, delta paging, contract-version header + N/N-1/N-2 rejection; D09 entry —
  outbox atomic-claim relay, retry/backoff/jitter/circuit-breaker/bulkhead; D11 entry —
  `LicenseVerifier`, installation binding, monotonic high-water check; D12 entry — file lifecycle +
  reconciliation + valet key; D13 entry — `CapacityHarness` (capacity-measurement harness), local
  OpenTelemetry, `bench.sh`; E02 entry — reporting isolated from OLTP budget; E05 entry —
  non-destructive pull, quarantine/needs-attention, full-resync; E06 entry — sync-failure UX
  contract; E07 entry — offline-session policy, device revocation, entitlement fail-closed)
- `STACK.md` §12 (all — G1, K2, native-SQLite assertion, RLS decision) · `ARCHITECTURE.md` §19
  (gates A–V) · `PERFORMANCE.md` §3, §4, §5, §6, §12, §15
- `scripts/sandbox/bench.sh`, `scripts/sandbox/test.sh`,
  `src/Animora.Infrastructure.Local/Diagnostics/**`, `src/Animora.Server/Diagnostics/**`

## Goal
Turn the architecture's promises into executed, recorded evidence. This phase adds one gate-runner
project that mechanizes `ARCHITECTURE.md` §19 gates A–V and the `STACK.md` §12 technology gates
(G1, K2, native-SQLite assertion, RLS decision), publishes a machine-readable gate report plus the
weak-server load numbers and the client startup/memory budget, and wires a CI-callable
`scripts/sandbox/gates.sh`. It writes **no product feature**; it only proves the ones already built.
A failing gate blocks release and is logged with an owner phase — it is never silently downgraded.

## Design decisions (pre-made — implement, do not re-derive)
- **One new test project `tests/Animora.Gates.Tests/`** (`net10.0`, xUnit, added to `Animora.sln`,
  packages pinned centrally). It is the *only* home for gate mechanization; existing per-layer test
  projects keep their unit tests. It references `Animora.Application`, `Animora.Persistence.Sqlite`,
  `Animora.Sync.Datasync`, `Animora.Server.Licensing/Verification` and (server gates only, behind a
  skip guard) `Animora.Server` + `Animora.Persistence.SqlServer`.
- **Every gate is one `[Trait("gate","<Letter|G1|K2|NativeSqlite|Rls>")]` xUnit fact/theory**, named
  `Gate_<Id>_<slug>`. Gates reuse the existing simulation seams already built — they do **not** add
  new production seams:
  | Gate | Reused seam / entry point | Pass assertion |
  |---|---|---|
  | A — patient conflict | E05 `ConflictResolver` + two `AnimoraLocalDbContext` files | deterministic converge + audit row |
  | B — independent financial commands | B02 pipeline + D07 handlers (in-proc `TestServer`) | both settle, ledger balanced, no dup |
  | C — duplicate command | D05 idempotency store, same `OperationId` twice | exactly one financial effect |
  | D — lost response | D05 replay path, response dropped then retried | prior result returned, one transaction |
  | E — cross-tenant attack | D04 tenant resolution + D02 filters, Tenant A→B id | refused at every mandatory layer |
  | F — delta integrity | D08 `TableController<T>` under the G1 stress matrix | zero unrecoverable skip AND dup |
  | G — server crash during settlement | D09 outbox, injected mid-commit fault | atomic tx + recoverable outbox |
  | H — desktop crash mid-commit | B02 crash-safe enqueue, kill between write+claim | no loss, no double effect |
  | I — reconnect storm | E04 orchestrator + D09 bulkhead, N simultaneous | jitter/backoff hold, no starvation |
  | J — isolation, hardening OFF | D02 with RLS objects absent | isolation still holds |
  | L — tenant-safe cache | D13 cache-aside, drop one key component | Tenant A never sees B; missing key fails |
  | M — mixed-version fleet | D08 contract-version header across N/N-1/N-2 | deterministic accept/reject |
  | O — sync-failure UX contract | E06 contract on a rejected/conflicted command | what/why/effect/who present, no bare fail |
  | P — dependency ordering | D05 `DependsOnOperationId`, all interleavings | settlement order, never arrival order |
  | Q — fingerprint integrity | D05 same/different `CommandFingerprint` | replay=prior; mismatch=audited integrity error |
  | R — offline-session policy | E07 snapshot expiry + revocation + stamp change | window enforced, revoked refused, recovery works |
  | S — key-material survival | A11 `ISecretStore` restart + D11 key ring loss | payloads survive restart; fail closed w/o key ring |
  | T — file lifecycle & reconciliation | D12 lifecycle + reconciliation job, injected divergence | every divergence class resolves; valet-key only |
  | U — offline licensing & anti-cloning | D11 `LicenseVerifier`, copied artifact + wound-back clock | copy fails; rollback cannot extend |
  | V — local observability, Internet pulled | D13 exporters with the loopback collector removed | every capability works; app never breaks |
  (There is no gate K or N in `ARCHITECTURE.md` §19 — do not invent them.)
- **Skip, never fake, an unavailable dependency.** A gate needing SQL Server or a live server is a
  `SkippableFact` guarded by `GateEnvironment.HasSqlServer` / `.HasLiveServer` (probe once, cache).
  A skipped gate is reported `SKIPPED(reason)` — distinct from `PASS`/`FAIL`; the release checklist
  (E09) treats an unresolved `SKIPPED` on a release build as a blocker.
- **`STACK.md` §12 technology gates, mechanized where the sandbox allows:**
  - **G1 (Datasync delta correctness)** ⇒ realized *as gate F* against the exact pinned `10.1.2`;
    the stress matrix is the §12 G1 list (same-ms bursts, insert-while-paging, delete/update
    interleave, page retry, mid-page disconnect, duplicate page, concurrent writes between pages,
    clock skew). One theory, one row per stressor.
  - **K2 (encrypted-store lifecycle)** ⇒ `Gate_K2_encrypted_lifecycle` executes the §12 K2 sequence
    per available RID through A11's encryption provider seam: create→close→reopen(correct key OK)→
    reopen(wrong key fails)→plain reader cannot read→EF migrate on encrypted DB→WAL+crash-recovery→
    backup→restore(still encrypted)→`PRAGMA rekey`. If the cipher native is absent on the runner it
    is `SKIPPED("cipher-native-absent")` — acceptable on a **developer / CI-without-cipher** runner only.
    **A `SKIPPED` K2 is never sufficient for a release build:** E09's `release-check.sh` treats K2 as
    must-`PASS` on every shipped RID (see E09 "Encryption-at-rest release gate"). Where K2 **passes**
    but the encrypted native is still `< 3.50.2`, E09's native-binary inventory records the residual
    CVE-2025-6965 as a dated accepted risk (`STACK.md` §3.3).
  - **Native-SQLite assertion** ⇒ `Gate_NativeSqlite_runtime_version` calls the A11 startup probe
    directly and asserts `SELECT sqlite_version() >= 3.50.2`; a managed-package restore is explicitly
    *not* accepted as proof (T-RULE 004).
  - **RLS decision** ⇒ `Gate_Rls_decision` records the decision, not an enablement: it asserts gate
    **J** passed (isolation holds with hardening OFF) and emits a `DECISION` line stating RLS stays
    OFF at launch and may only be enabled after this gate re-runs green with trace flag 11042
    (`STACK.md` §12); it never turns RLS on.
- **Performance proof (measured, never asserted — `PERFORMANCE.md` §15, A-RULE 034):**
  - **Weak-server load benchmark** reuses D13's `CapacityHarness` through a new
    `scripts/sandbox/gates.sh bench-server` step: a documented mixed workload (delta pulls + command
    posts at the D08 page sizes) driven at a fixed concurrency against a `~2 GB`/`2-vCPU` cgroup, if
    the runner supports `--cpus`/`--memory`; otherwise it runs unconstrained and stamps the report
    `UNCONSTRAINED`. Output: p50/p95/p99 latency and throughput per operation class, plus the
    reporting-vs-OLTP budget split proving E02's reporting queries stay in their own budget
    (A-RULE 034). Numbers are recorded with the exact methodology; nothing is claimed without a run.
  - **Client startup / memory budget** reuses `bench.sh`'s Xvfb smoke to record cold-start time to
    first interactive frame and peak working set on the C11 offline scenario, compared against the
    `PERFORMANCE.md` §3/§4 budgets restated in C11. A regression beyond budget is a `FAIL`.
- **Gate report artifact:** `GateReportWriter` emits `artifacts/gates/gate-report.json`
  (`{ generatedAtUtc, commit, phasesCovered, gates:[{ id, name, status, tracesTo, detail,
  metrics? }], performance:{ server:{...}, client:{...}, constrained:bool } }`) and a human
  `gate-report.md` summary. The writer lives in the gate project; it produces artifacts only, and
  writes nothing under `src/`.
- **`scripts/sandbox/gates.sh`** (new): `all` (default) runs every gate + both benchmarks and writes
  both reports; `gates` runs only §19+§12 gates; `bench-server` / `bench-client` run one benchmark;
  exit code is non-zero if any gate is `FAIL` (a `SKIPPED` alone does not fail the script but is
  listed). It calls `dotnet test tests/Animora.Gates.Tests` with the trait filter and then the
  bench steps; it never rebuilds product code differently from `build.sh`.
- **Owner mapping:** each gate's `tracesTo` names the phase that owns the proven boundary (the table
  above). A `FAIL` is logged in `PROGRESS.md`'s deviation log with that owner phase; the fix belongs
  to that phase's boundary, re-run here — E08 never patches the product to make a gate pass.
- **No new error codes, no wire change, no migration, no DI change, no mock (none remain post-C11).**

## Files to create / modify
| Path | Action | Contents (one line) |
|---|---|---|
| `tests/Animora.Gates.Tests/Animora.Gates.Tests.csproj` | create | gate project; refs Application/Persistence.Sqlite/Sync.Datasync/Licensing.Verification + guarded server refs |
| `tests/Animora.Gates.Tests/GateEnvironment.cs` | create | `HasSqlServer`/`HasLiveServer`/`SupportsCgroupLimits` probes, cached |
| `tests/Animora.Gates.Tests/ArchitectureGates.cs` | create | gates A,B,C,D,E,G,H,J,L,O,P,Q,R,S,T,U,V as `[Trait("gate",...)]` facts |
| `tests/Animora.Gates.Tests/DeltaIntegrityGate.cs` | create | gate F / G1 stress-matrix theory against pinned `10.1.2` |
| `tests/Animora.Gates.Tests/ReconnectStormGate.cs` | create | gate I under D09 bulkhead |
| `tests/Animora.Gates.Tests/MixedFleetGate.cs` | create | gate M across N/N-1/N-2 contract versions |
| `tests/Animora.Gates.Tests/TechnologyGates.cs` | create | K2, native-SQLite assertion, RLS decision |
| `tests/Animora.Gates.Tests/GateReportWriter.cs` | create | `gate-report.json` + `.md` emitter |
| `tests/Animora.Gates.Tests/PerformanceBudget.cs` | create | client startup/memory budget assertions on the C11 scenario |
| `scripts/sandbox/gates.sh` | create | `all\|gates\|bench-server\|bench-client`; non-zero on any FAIL |
| `Animora.sln` | modify | add `Animora.Gates.Tests` |
| `Directory.Packages.props` | modify | pin `Xunit.SkippableFact` (only new test-only package) |

## Implementation steps
1. Create `Animora.Gates.Tests` with guarded references; add to `Animora.sln`; pin the one new
   test-only package centrally.
2. Write `GateEnvironment` probes (SQL Server reachable, live server reachable, cgroup-limit support)
   and cache the results for the run.
3. Implement gates A–V in the four gate files, each reusing the existing seam from the table; mark
   server/SQL-dependent gates `SkippableFact`.
4. Implement F/G1 as the stress-matrix theory against the pinned `10.1.2`; assert zero unrecoverable
   skip and zero unrecoverable duplicate for every row.
5. Implement K2, the native-SQLite runtime probe, and the RLS `DECISION` record (asserting J, never
   enabling RLS).
6. Write `GateReportWriter`; have every gate append a structured result; produce both report files.
7. Write `PerformanceBudget` over the C11 offline scenario for cold-start and peak working set.
8. Add `scripts/sandbox/gates.sh` orchestrating gates + `bench-server` (via D13's harness) +
   `bench-client`; wire the constrained-vs-unconstrained stamp.
9. Run `gates.sh all` locally: confirm every non-skipped gate is `PASS`, the report lists any
   `SKIPPED` with a reason, and the performance numbers are recorded with methodology.
10. For every `FAIL`, log it in `PROGRESS.md`'s deviation log against the owner phase and stop —
    the fix is that phase's, re-run here; do not patch product code from E08.
11. `build.sh full` = 0/0; `test.sh` = `TESTS OK`; `run.sh` = `SMOKE RUN OK` with no server.

## Contracts / schema added
- No production contract, wire shape, entity, migration, DI registration or error code changes.
- New test project `Animora.Gates.Tests` with the gate traits `A`–`V`, `G1`/F, `K2`,
  `NativeSqlite`, `Rls`; artifacts `artifacts/gates/gate-report.json` + `gate-report.md`.
- New script `scripts/sandbox/gates.sh`. One new test-only pin (`Xunit.SkippableFact`).

## Rules that apply here
- **R-B32** — all `STACK.md` §12 gates and `ARCHITECTURE.md` §19 gates A–V are executed and recorded;
  a skip is reported explicitly and blocks a release build (E09), never silently passes.
- **A-RULE 034 / R-B35** — the reporting-vs-OLTP budget split is measured in the server benchmark;
  reporting load never draws from the OLTP budget.
- **PERFORMANCE.md §15 / R-B07 / R-B08** — capacity is measured with a published methodology, never
  asserted; weak-server numbers and the client startup/memory budget are recorded, not claimed.
- **T-RULE 004** — the native-SQLite gate uses the runtime `sqlite_version()` probe; a managed-package
  restore is not accepted as proof.
- **A-RULE 013 / R-B19** — gate J proves tenant isolation with database hardening OFF; the RLS gate
  records a decision and never enables RLS.
- **A-RULE 016 / R-B27** — gate V proves observability works with the Internet pulled; the gate
  harness itself contacts no external host.
- **CONVENTIONS §12 / R-B10** — this phase adds tests and a script only; it writes nothing under
  `src/` and introduces no forbidden dependency.

## Out of scope
- Fixing a failing gate's underlying boundary → the owning phase named in the gate's `tracesTo`.
- Enabling SQL RLS or trace flag 11042 → deferred (ROADMAP §5); this phase only records the decision.
- Deployment, packaging, backup-restore rehearsal, SBOM → E09.
- New product features, seams, migrations or wire changes → none; E08 proves, never builds.

## Acceptance criteria
- Build: `scripts/sandbox/build.sh full` = 0 warnings / 0 errors.
- **Measured drain rate (audit 2026-10-08 · SYNC-M-03):** the gate reports a measured catch-up drain rate (rows/min on the minimum
  server spec over a throttled link) and the projected time to drain a full 25-entity tenant, so the "draining N of M" UX promise is backed by a number.
- `scripts/sandbox/gates.sh all` runs to completion and writes `gate-report.json` + `gate-report.md`;
  every non-skipped `ARCHITECTURE.md` §19 gate (A–V) and every `STACK.md` §12 gate reports `PASS`;
  each `SKIPPED` carries a reason and a re-run condition.
- Gate F/G1 is green against the pinned `10.1.2` for every stressor row: zero unrecoverable skip and
  zero unrecoverable duplicate.
- Gate J is green with database hardening OFF; the RLS `DECISION` line records "OFF at launch,
  enable only after re-run green with 11042".
- The native-SQLite gate asserts a **runtime** `sqlite_version() >= 3.50.2`, not a package version.
- The performance section records weak-server p50/p95/p99 + throughput per operation class with the
  constrained/unconstrained stamp, the reporting-vs-OLTP budget split, and the client
  cold-start/peak-working-set numbers against the C11 scenario, each within the `PERFORMANCE.md`
  §3/§4 budget.
- Tests: `tests/Animora.Gates.Tests` green under `test.sh`; any `FAIL` is logged in `PROGRESS.md`'s
  deviation log with its owner phase.
- Runnability: `scripts/sandbox/run.sh` = `SMOKE RUN OK` with no server, no SQL Server, no network.

## PROGRESS.md update
Mark E08 done; advance "Next phase" to `E09`; append a "Repo state" entry listing: the new
`Animora.Gates.Tests` project + the gate-trait catalog (A–V, G1/F, K2, native-SQLite, RLS), the
`gate-report.json`/`.md` artifacts, `scripts/sandbox/gates.sh`, the one new test-only pin
(`Xunit.SkippableFact`), the recorded weak-server numbers + methodology + constrained/unconstrained
stamp, the reporting-vs-OLTP budget split, the client startup/memory budget results, the RLS
`DECISION` (OFF at launch), every gate's `PASS`/`SKIPPED(reason)` status, and — for any `FAIL` — a
deviation-log entry naming the owner phase (E09 must treat unresolved `SKIPPED`/`FAIL` as release
blockers). No `src/` change, no migration, no DI change, no wire bump.
