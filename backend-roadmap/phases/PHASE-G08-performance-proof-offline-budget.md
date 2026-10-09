# Phase G08 — Validation gates II: performance proof & offline budget (weak-server / weak-client capacity, layering audit)

**Band:** M · **Track:** G (sync / integration / release) · **Depends on:** G07, S20 · **Next:** G09
**Satisfies:** R-B07, R-B08, R-B10, R-B32 · **Runnable after this phase:** desktop offline yes · server required no
**Produced by:** M00 (regenerated 2026-10-09; regenerates scope-inventory row E08 (second half; Track-D/E planning files deleted in the 2026-10-08 server-track reset)).

> Baseline verified 2026-10-09: server migration head = last server ordinal (read `ServerSchemaVersion.ExpectedHead` — never hard-code), client migration head = last client ordinal (list `src/Animora.Persistence.Sqlite/Migrations/`), `CommandTypes` = live `CommandTypes.cs` (88 today — never freeze), `ErrorCodes` = live count, `WireContract.Version` = 2 (supported {1,2}); all server + client sync projects exist (S01–S20, G01–G07).
> **Next-free-ordinal rule.** This phase adds **no migration** — like G07 it is a **validation-gate** phase (harness + budget + audit glue in `scripts/**`, `.github/**`, `tests/**`). No schema, no runtime `src/**` change. A gate that exposes a real regression is fixed by the **owning** phase, not here.

---

## ⚡ Zero-exploration contract (read this, then implement top-to-bottom)

> This file is self-contained and pre-verified. The weak-client **offline budget** already has a working harness
> (`scripts/sandbox/bench.sh offline` → `scripts/sandbox/offline-budget.py`, authored in C11) with fixed metrics/budgets and a
> **`>2×` budget = FAIL** rule; G08 promotes it to a **mandatory gate** and adds the **weak-server capacity harness**, the
> **mixed-fleet (N/N-1/N-2)** and **reconnect-storm** proofs, the **zero-Internet egress** gate, and the **layering audit**. Every
> gate here is a **script exit code / numeric threshold / named test**, never prose. Do not re-open the repo to "understand" it; if a
> snippet diverges, fix the smallest thing and log it. Implement exactly this scope: nothing from G07 (PG CI/RLS/restore) or G09
> (release packaging/SBOM).

**Environment (do not probe):**
- Build / test / bench only through the sandbox scripts (memory-capped ~985 MiB; never raw `dotnet build`):
  `scripts/sandbox/build.sh [fast|full]`, `scripts/sandbox/test.sh [filter]`, `scripts/sandbox/bench.sh [offline|--label L]`
  (the C11 harness: launches the desktop under Xvfb, records TTFUI / peak RSS / CPU; `offline` runs the 60-s idle + the
  `OfflineBudgetMeasurements` query proxies and emits `offline-budget.py`'s table). `scripts/sandbox/offline-budget.py` turns the
  measurements into a markdown table and **exits non-zero only when a metric exceeds 2× its budget**.
- `dotnet-ef` at `.sandbox/tools/dotnet-ef` — not needed here (no migration).
- **Forbidden tech (restated):** Redis, message broker, cloud IdP, cloud telemetry, CDN, EF Core 11, per-tenant database,
  hard-coded TLS version, Native AOT with EF Core. The capacity harness uses only the in-tree server + a local PostgreSQL (G07's
  service container shape) and a `dotnet`-based load driver — **no** third-party load tool pulled into the solution.

**Offline invariant.** G08 adds **no runtime code**; the desktop builds, starts and runs fully offline exactly as after G07 — in
fact the **offline-budget gate is the proof of that**: first visible frame, dashboard data, 60-s idle working set, patients page,
global search and post-invoice are all measured **with no server / no HTTP transport** (the harness runs `OfflineBudgetMeasurements`
against a migrated local DB). The weak-server capacity harness needs a server + PostgreSQL to *run*, but the product never needs
either at runtime (A-RULE 016/032/035).

**Condensed CONVENTIONS rules used here:** §1 (hardware-dependent measurements are recorded, not asserted — only a **>2× budget**
regression fails; A-RULE 035 "measured, not asserted"); §2 layering (the audit asserts `LayeringRulesTests` + the server/sync
reference rules; gate glue lives in `tests/**`/`scripts/**`/`.github/**`, no `src/**` change); §6 two pipelines (the reconnect-storm
and mixed-fleet proofs test the **state** feed and the **command** path independently, never merged).

---

## Read before starting
- `backend-roadmap/PROGRESS.md` — top section only.
- `STACK.md` §10 (weak-host/weak-client .NET performance features — the levers the harness measures) and §11 (capacity framing)
  and §12 (gate framing — this phase owns the **behavioral** perf/offline gates, not the PG technology PoCs) — only these `§`.
- `ARCHITECTURE.md` §16 (performance strategy: delta-only sync, work off the hot path, non-blocking startup, mandatory paging ≤200,
  bounded tenant-safe caches with mandatory key components, measured-not-asserted), §19 (gate table — this phase owns **T** measured
  capacity, **I** reconnect storm, **M** mixed fleet, **Z** zero-Internet, plus the offline-budget and layering audit; G07 owns
  L/K/R/PG), §12.3 (jittered per-device backoff — the reconnect-storm subject), §8.6/§18 (mixed-fleet N/N-1/N-2, `RuleSetVersion`) —
  only these `§`.
- The already-shipped harness: `scripts/sandbox/bench.sh` (the `offline` path + `--label`), `scripts/sandbox/offline-budget.py`
  (the 6 metrics + budgets + `>2×`=FAIL rule), and the `OfflineBudgetMeasurements` test the harness drives (in
  `tests/Animora.Application.Tests`). The mixed-fleet seam: `WireContract.IsSupported(int)` + `{1,2}` (S06); reconnect backoff:
  the S13 jittered per-device scheduler and the G03 `SyncSchedulerHostedService` (jitter ±20% + capped backoff).
- (Nothing else. `src/**`, `Animora-UI/**`, `Database-Sampel/**` are out of bounds — this is a gate-wiring phase.)

## Goal

Earn the capacity/efficiency bar (R-B07/R-B08) and lock the clean-layering bar (R-B10) with **mechanical** gates, not claims.
(1) **Weak-client offline budget** (gate, R-B08): promote the C11 `bench.sh offline` + `offline-budget.py` harness to a **mandatory
CI gate** — the run **fails** if any of the 6 metrics (first visible frame ≤1200 ms, dashboard data ≤2500 ms, 60-s idle working set
≤300 MiB, patients 200-row page ≤120 ms, global-search keystroke ≤80 ms, post-invoice-10-lines ≤250 ms) regresses past **2×** its
budget, and always records the measured table as a build artifact (A-RULE 035). (2) **Weak-server capacity** (gate T, R-B07): a
**published load harness** that drives the command-intake + sync-pull paths against the in-tree server on a **1–3 core / 2–4 GiB**
constrained container (cgroup-limited, mirroring the sandbox memory cap), records real throughput/latency/RSS to an artifact, and
asserts **no crash / no connection-pool exhaustion / bounded p99** — **publishing a capacity number, never guaranteeing a user
count**. (3) **Reconnect storm** (gate I): a fleet of simulated devices restarting together must **not** converge — the jittered
per-device backoff (G03/S13) spreads reconnects (asserted on the arrival histogram). (4) **Mixed fleet** (gate M): N/N-1/N-2
deterministic — a V1 client gets a controlled `426`/resync, a stale `RuleSetVersion` is re-derived or rejected-with-reason, never
force-applied. (5) **Zero-Internet** (gate Z): the built output and a running instance have **no external egress** — grep the
published tree for `cdn…`/external endpoints and run the server+desktop with egress blocked. (6) **Layering audit** (R-B10): the
`LayeringRulesTests` + server/sync reference rules pass (no client project references a server project except the whitelisted
`Animora.Server.Licensing.Verification`; no `Animora.Server` reference to a client/persistence-sqlite project; no Npgsql outside
`Animora.Persistence.PostgreSql`).

---

## §Reference pack (verbatim from the harness + authoring phases, 2026-10-09)

```text
# C11 offline-budget harness (already in the tree — PROMOTE to mandatory; do not rewrite the thresholds):
#   scripts/sandbox/bench.sh offline  -> runs Animora.Application.Tests ~OfflineBudgetMeasurements (60s idle, Xvfb, no server/HTTP)
#   scripts/sandbox/offline-budget.py app.log query.json rss_kb out.md
#     metrics (name, budget, unit):
#       First visible frame (login, Main proxy)      ≤ 1200 ms
#       Dashboard data (query proxy, not render)      ≤ 2500 ms
#       Working set after 60 s idle                   ≤ 300  MiB
#       Patients page (200 rows)                       ≤ 120  ms   (asserts EXACTLY 200 rows)
#       Global search keystroke                        ≤ 80   ms
#       Post invoice (10 lines)                        ≤ 250  ms
#     rule: status = PASS (≤budget) | OVER BUDGET (≤2×) | FAIL (>2×);  exit non-zero ONLY on FAIL (>2×).
#   => G08 wires this into CI so a >2× regression fails the build; the markdown table is always archived.

# Mixed-fleet seam (S06): WireContract.Version = 2 ; WireContract.IsSupported(int) true for {1,2} (N, N-1).
#   A v1 client never calls /sync (sync DTOs were introduced at v2) -> 426 CONTRACT_VERSION_UNSUPPORTED + X-Animora-Supported-Versions.
#   RuleSetPolicy { Current=1, SupportedFrom=1 }: absent=>Current; < SupportedFrom => Rejected(RULESET_UNSUPPORTED); between => server re-derives.

# Reconnect backoff (G03 SyncSchedulerHostedService + S13): jitter ±20% + capped exponential backoff, per-device, via IDelayProvider/TimeProvider.

# Layering audit (A01): tests/Animora.Domain.Tests/LayeringRulesTests.cs — Domain_HasNoForbiddenReferences,
#   Desktop_HasNoStoreOrNetworkReferences, + the S01/S16 server/sync reference rules (client->server only Animora.Server.Licensing.Verification).

# Capacity framing (ARCH §16.4, A-RULE 035, gate T): capacity is proven by a published harness on 1-3 core / 2-4 GiB and RECORDED;
#   NO user-count/throughput number is guaranteed by assertion. Reference hardware is a planning reference only.
```

## §Design decisions

- **Offline-budget gate = CI job `offline-budget` (workflow `.github/workflows/perf-gates.yml`, new).** Steps: build (Debug, the
  budgets were set for Debug), `scripts/sandbox/bench.sh offline`, then `scripts/sandbox/offline-budget.py <app.log> <query.json>
  <rss_kb> artifacts/offline-budget.md`. The job **fails** iff the script exits non-zero (a `>2×` regression). The markdown table is
  uploaded as an artifact every run (pass or fail) so the trend is visible (A-RULE 035). No threshold is changed here — G08 only
  makes the existing harness block the build.
- **Weak-server capacity harness (`scripts/perf/server-capacity.sh` + `tests/Animora.Server.Tests/Capacity/*`, new).** A `dotnet`
  load driver (`CapacityDriver`, xUnit-hosted so it reuses the test infra) runs against the in-tree server bound to a **cgroup-
  constrained** container (`--cpus=2 --memory=2g`, the 1–3 core / 2–4 GiB reference) with PostgreSQL 18 (G07's service shape). It
  drives two independent profiles (§6 two pipelines): **command intake** (`POST /api/commands` + `/batch`, idempotent mix) and
  **state pull** (`/sync/manifest|batches|pages`), ramping concurrency, recording per-profile throughput, p50/p95/p99 latency and
  peak server RSS + PostgreSQL connection count to `artifacts/server-capacity.json` + a markdown summary. **Assertions (mechanical,
  not a user count):** zero 5xx that aren't deliberate, **no connection-pool exhaustion** (active ≤ the S18 bounded pool sum), p99
  bounded under the configured `statement_timeout` (S18), and the server stays alive (readiness green at the end). The published
  number is *recorded capacity on this hardware*, explicitly labelled "planning reference only".
- **Reconnect-storm gate (`ReconnectStormTests`, `tests/Animora.Sync.Tests`).** Spin N simulated devices (fake `IDelayProvider` +
  `TimeProvider`, deterministic) that all become "due" at t0; assert the computed next-attempt times form a **spread** histogram
  (max bucket ≤ a fraction of N — jitter works) and that no two of a large sample share the exact same slot beyond the birthday
  bound — i.e. a fleet restarting together does **not** converge (gate I, ARCH §12.3). Pure/deterministic, no real sleep.
- **Mixed-fleet gate (`MixedFleetTests`, `tests/Animora.Server.Tests`).** `V1Client_CallsSync_Gets426_WithSupportedVersions`
  (sync DTOs are v2-only), `V2Command_Accepted`, `V1Command_StillAccepted` (commands support {1,2}),
  `UnknownVersion_Rejected_426`, `StaleRuleSetVersion_BelowSupportedFrom_RejectedWithReason`,
  `RuleSetVersion_Between_ServerReDerives_NotForceApplied` — deterministic over the S06 `WireContract.IsSupported` + `RuleSetPolicy`.
- **Zero-Internet gate (`scripts/perf/zero-internet.sh` + `ZeroInternetTests`).** (a) A **static grep** of the **published** output
  (`dotnet publish` tree for server + desktop, or the already-built smoke output) for `http(s)://` endpoints that are not loopback/
  LAN/`localhost`/the configured server base — any `cdn…`/external host is a **failure** (gate Z, ARCH §17.4). (b) A runtime leg:
  run `scripts/sandbox/run.sh` with outbound egress blocked (the sandbox already has no egress; the gate asserts `SMOKE RUN OK`
  under that condition) and the server readiness up with only loopback reachable. No external call in any runtime path.
- **Layering audit gate.** Run `scripts/sandbox/test.sh Domain` (which hosts `LayeringRulesTests`) + a new
  `tests/Animora.Server.Tests/ServerLayeringAuditTests` that asserts the server/sync reference rules mechanically:
  `NoClientProjectReferencesAServerProject_ExceptLicensingVerification`, `AnimoraServer_DoesNotReference_PersistenceSqlite_Or_Desktop`,
  `OnlyPersistencePostgreSql_ReferencesNpgsql`, `SyncCustom_HasNoNpgsql_NoServerRef`. These read the compiled assemblies'
  reference sets (same technique as the existing `LayeringRulesTests`), so the audit is a **test**, not a review.
- **No product-code change / no new ErrorCode.** G08 consumes shipped code. A real regression/leak is fixed by the **owning** phase
  (deviation logged under its ID); G08 makes the gate run and be mandatory.
- **Local reproduction.** `scripts/perf/server-capacity.sh` and `scripts/perf/zero-internet.sh` are runnable locally (documented in
  a short `scripts/perf/README.md`); `scripts/sandbox/doctor.sh` already (G07) reports PostgreSQL reachability.

## §Implementation plan

1. `.github/workflows/perf-gates.yml` — jobs `offline-budget`, `server-capacity`, `zero-internet`, `layering-audit`,
   `sync-behavioral` (reconnect-storm + mixed-fleet).
2. `scripts/perf/server-capacity.sh` + `tests/Animora.Server.Tests/Capacity/CapacityDriver.cs` (+ profiles) — cgroup-constrained
   load, artifacts, bounded-pool / alive assertions.
3. `scripts/perf/zero-internet.sh` + `tests/Animora.Server.Tests/ZeroInternetTests.cs` — published-tree grep + egress-blocked run.
4. `tests/Animora.Sync.Tests/ReconnectStormTests.cs` — deterministic jitter spread proof.
5. `tests/Animora.Server.Tests/MixedFleetTests.cs` — N/N-1/N-2 + `RuleSetVersion` determinism.
6. `tests/Animora.Server.Tests/ServerLayeringAuditTests.cs` — mechanical server/sync reference-rule assertions.
7. `scripts/perf/README.md` — how to run each harness locally; wire the offline-budget job to `bench.sh offline` + `offline-budget.py`.
8. Verify: `scripts/sandbox/build.sh full` → 0/0; `scripts/sandbox/run.sh` → `SMOKE RUN OK`; `scripts/sandbox/bench.sh offline` →
   table emitted, exit 0 (no >2× regression). The capacity/zero-internet jobs run in CI (documented local repro).
9. `PROGRESS.md` per recipe; commit via `scripts/git/push.sh`.

## Files to create / modify

| Path | Contents |
|---|---|
| `.github/workflows/perf-gates.yml` (new) | jobs offline-budget / server-capacity / zero-internet / layering-audit / sync-behavioral |
| `scripts/perf/server-capacity.sh` (new) | cgroup-constrained load driver launcher; emits `artifacts/server-capacity.json` + md |
| `tests/Animora.Server.Tests/Capacity/CapacityDriver.cs` (+ profiles) (new) | command-intake + state-pull load profiles, bounded-pool/alive asserts |
| `scripts/perf/zero-internet.sh` (new) | published-tree external-endpoint grep + egress-blocked run |
| `tests/Animora.Server.Tests/ZeroInternetTests.cs` (new) | no external endpoint in output; `SMOKE RUN OK` under blocked egress |
| `tests/Animora.Sync.Tests/ReconnectStormTests.cs` (new) | deterministic jitter-spread (fleet does not converge) |
| `tests/Animora.Server.Tests/MixedFleetTests.cs` (new) | N/N-1/N-2 + `RuleSetVersion` determinism |
| `tests/Animora.Server.Tests/ServerLayeringAuditTests.cs` (new) | mechanical server/sync reference-rule assertions |
| `scripts/perf/README.md` (new) | local repro of each perf/gate harness |

## Contracts / schema added

No wire DTO, no migration, no runtime type, no ErrorCode. New CI/test/harness artifacts only: one workflow, two perf scripts, the
capacity driver, four/five test classes, and the `artifacts/offline-budget.md` + `artifacts/server-capacity.json` records. No
`WireContract.Version` change.

## Rules that apply here

- **Measured, not asserted (ARCH §16.4, A-RULE 035, R-B07, gate T):** the capacity harness **publishes and records** real numbers on
  the 1–3 core / 2–4 GiB reference; it never guarantees a user count. The offline budget fails only on a **>2×** regression.
- **Weak-client efficiency (ARCH §16.1, R-B08):** first-frame/startup is non-blocking and the 6 offline-budget metrics stay within
  budget; the gate is the proof, run with **no server / no HTTP transport**.
- **Two pipelines (§6):** the reconnect-storm and capacity harnesses drive the **state** feed and the **command** path as separate
  profiles; mixed-fleet proves the command vs sync version rules independently.
- **Clean layering (R-B10, CONVENTIONS §2):** the audit is a **test** over compiled reference sets — the only client→server reference
  is `Animora.Server.Licensing.Verification`; `Animora.Server` never references a client/persistence-sqlite project; Npgsql lives only
  in `Animora.Persistence.PostgreSql`.
- **Zero-Internet (ARCH §17.4, A-RULE 016/032, gate Z):** no external endpoint in the published output; the runtime operates with
  egress blocked.

## Out of scope (owning phase)

- PostgreSQL CI / tenancy-RLS / restore + key-ring gates → **G07** (prerequisite; G08 reuses its PostgreSQL service shape).
- The full install/upgrade/rollback runbook, PITR/WAL cadence + off-host durability, desktop packaging, SBOM + native-binary
  inventory → **G09**.
- Enabling RLS in product, Redis SignalR backplane / scale-out, cross-tenant analytics → deferred (ROADMAP §5, §20 triggers).
- Any `src/**` fix a gate exposes → the **owning** S/G phase (G08 only makes the gate mandatory).

## Acceptance criteria (mechanical)

1. `scripts/sandbox/build.sh full` = 0 warnings / 0 errors; `scripts/sandbox/run.sh` = `SMOKE RUN OK`.
2. `scripts/sandbox/bench.sh offline` then `scripts/sandbox/offline-budget.py …` exits **0** (no metric > 2× budget) and writes
   `artifacts/offline-budget.md` containing all 6 rows (first-frame, dashboard, 60-s RSS, patients-200, search, post-invoice); CI job
   `offline-budget` is **red** if the script exits non-zero (prove with a deliberately-tightened local threshold in a dry run).
3. CI job `server-capacity` green: `scripts/perf/server-capacity.sh` runs under `--cpus=2 --memory=2g` + PostgreSQL 18, writes
   `artifacts/server-capacity.json` with numeric throughput/p99/peak-RSS/pool-count, and `CapacityDriver` asserts **no pool
   exhaustion** (active connections ≤ S18 bounded pool sum), zero unexpected 5xx, readiness green at end; the artifact is labelled
   "planning reference only".
4. CI job `sync-behavioral` green: `ReconnectStormTests.FleetRestart_DoesNotConverge_JitterSpreadsReconnects`;
   `MixedFleetTests.V1Client_CallsSync_Gets426_WithSupportedVersions`, `MixedFleetTests.V1Command_StillAccepted`,
   `MixedFleetTests.UnknownVersion_Rejected_426`, `MixedFleetTests.StaleRuleSetVersion_BelowSupportedFrom_RejectedWithReason`,
   `MixedFleetTests.RuleSetVersion_Between_ServerReDerives_NotForceApplied`.
5. CI job `zero-internet` green: `scripts/perf/zero-internet.sh` finds **no** non-loopback/non-configured `http(s)://` endpoint in
   the published output and `ZeroInternetTests.Runtime_OperatesWithEgressBlocked` reports `SMOKE RUN OK`.
6. CI job `layering-audit` green: `scripts/sandbox/test.sh Domain` passes `LayeringRulesTests.*`, and
   `ServerLayeringAuditTests.NoClientProjectReferencesAServerProject_ExceptLicensingVerification`,
   `ServerLayeringAuditTests.AnimoraServer_DoesNotReference_PersistenceSqlite_Or_Desktop`,
   `ServerLayeringAuditTests.OnlyPersistencePostgreSql_ReferencesNpgsql`,
   `ServerLayeringAuditTests.SyncCustom_HasNoNpgsql_NoServerRef` all pass.
7. No change under `src/**` for this phase (`git diff --name-only <base>..HEAD -- src/` empty).

## PROGRESS.md update

At the end of this phase: set the `G08` row in "Phase status" to `✅ Done` (add date + one-line summary), advance
**"Next phase" → `G09`**, append one `### G08` entry to "Repo state log" (the `perf-gates.yml` workflow, the offline-budget gate
promotion, the weak-server capacity harness + artifact, reconnect-storm / mixed-fleet / zero-Internet / layering-audit gates), log
any deviation (and any `src/**` regression a gate exposed, filed under its owning phase), then commit with
`scripts/git/push.sh -b genspark_ai_developer "ci(gates): G08 performance proof & offline budget"`.
