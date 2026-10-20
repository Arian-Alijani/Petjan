# Plan — Server multi-core / parallel-processing upgrade

> **Self-contained execution file.** Everything a fresh model needs is distilled below —
> do **not** re-explore the repo to understand the problem. Only open the exact files named
> in each task. Target project = the **.NET** solution (`Animora.sln`). **Never** open `Animora-UI/`.
>
> **LOCKED RULE — push after every completed task:** run `scripts/git/push.sh "msg"`
> after finishing each task (and after any heavy mid-task fix). Not pushing = project death.
> Branch = `genspark_ai_developer`.

---

## 0. Context already established (do not re-derive)

**Product:** Animora — vendor-hosted (SaaS) multi-tenant vet-clinic server. One ASP.NET Core
process (`src/Animora.Server`, **.NET 10**, Kestrel) + PostgreSQL (`src/Animora.Persistence.PostgreSql`).
The complaint: *"the server-side only uses one core."* This is the server, not the desktop app.

**Root-cause investigation result (CONFIRMED — this is the diagnosis, no need to repeat it):**

1. **PRIMARY root cause — Workstation GC.** `src/Animora.Server/Animora.Server.csproj` sets
   `<ServerGarbageCollection>false</ServerGarbageCollection>`. Workstation GC uses **one GC heap +
   one GC thread total**, so under load all managed allocation/collection serializes through a
   single logical heap → the classic "server pegs one core, others idle" symptom. **Server GC**
   creates one heap + one background GC thread **per logical CPU**, which is the standard choice
   for a throughput server and is what unlocks real multi-core scaling. This single flag is the
   biggest lever.

2. **SECONDARY — zero CPU-core awareness anywhere.** The whole scaling model is driven **only** by a
   static memory profile `Database:ProfileGiB` ∈ {2,4} (`DatabaseOptions.ForProfile` →
   `src/Animora.Persistence.PostgreSql/Options/DatabaseOptions.cs`). **Nothing** in the server reads
   `Environment.ProcessorCount`. grep proof: the ONLY `MaxDegreeOfParallelism` in the server is
   `IntakeRecoveryWorker` using the fixed setting `RecoveryParallelism` (default 4), never cores.
   So if the box is upgraded from 2→16 cores, the server does not widen any parallel pool
   automatically — it cannot "automatically use all appropriate hardware" as the task requires.

3. Concurrency knobs today (all fixed numbers, memory-sized, CPU-blind):
   - DB pools (`DatabaseOptions`): `OltpWriteMax` 6/11, `OltpReadMax` 3/5, `WorkerMax` 2/3 (by profile).
   - Worker admission gate `IBackgroundAdmission` = `SemaphoreBackgroundAdmission(clamp(WorkerMax,1,2))`
     — i.e. **at most 1-2 background permits** (sized in
     `src/Animora.Persistence.PostgreSql/DependencyInjection/PersistenceServiceCollectionExtensions.cs` ~line 106).
   - Sync admission gate `ISyncAdmission` = `max(2, OltpWriteMax-2)` (same file ~line 111).
   - `WorkerSettings` (`src/Animora.Server/Hosting/ServerOptions.cs`): `OutboxConsumers` 4,
     `RecoveryParallelism` 4, `OutboxClaimBatch` 10, etc. — fan-out widths, all fixed.
   - Kestrel (`src/Animora.Server/Observability/KestrelProfile.cs`): `MaxConcurrentConnections`
     256/512 by profile — connection ceiling, not a CPU/thread setting (Kestrel/thread-pool already
     scale with cores; this is fine, leave it).

**What "good" looks like (acceptance):**
- Server GC on → .NET spawns per-core GC heaps automatically; managed throughput scales with cores.
- A single, well-documented **CPU-awareness helper** that derives an effective core count
  (respecting cgroup/container CPU limits via `Environment.ProcessorCount`, which .NET already
  honors for cgroup quotas) and is the one place "scale with hardware" is computed.
- The memory profile keeps bounding **DB connections** (Postgres has a hard connection budget — must
  NOT be widened by core count, or you exhaust Postgres). But **CPU-bound fan-out widths** (worker
  admission, outbox consumers, recovery parallelism) auto-scale from cores when left at their
  "auto" sentinel, **clamped by the DB budget** so we never over-subscribe Postgres.
- Hardware upgrade (more cores) → automatically wider CPU-bound parallelism, no config change,
  no scenario broken. Explicit config always overrides auto (preserve existing "explicit wins" rule).

**Design rule to respect (already in the code, keep it):** two independent admission gates
(worker vs sync) must stay separate (SCALE-ADM-01). DB-pool sums must stay within the validator
budget (12 @2GiB / 20 @4GiB) — `src/Animora.Persistence.PostgreSql/Options/DatabaseOptionsValidator.cs`.
Never let CPU scaling push pool sums over that budget.

**Build/verify (fast, no full test suite):**
`scripts/sandbox/build.sh full` → expect 0 warnings / 0 errors. Do NOT run the long test suite.

---

## TODO LIST

### [x] T1 — Enable Server GC (the primary fix)
**File:** `src/Animora.Server/Animora.Server.csproj`
**Do:** change `<ServerGarbageCollection>false</ServerGarbageCollection>` → `true`. Keep
`<ConcurrentGarbageCollection>true</ConcurrentGarbageCollection>` (background server GC). Keep
`TieredPGO`. Keep the `System.GC.HeapHardLimitPercent=70` / `ConserveMemory=5` host options — Server
GC + a heap-hard-limit is the correct combo for a memory-capped (2/4 GiB) container: per-core heaps
for parallelism, bounded by the hard limit so it can't blow the memory profile. Update the 2-line
comment above the PropertyGroup to say Server GC is deliberate for multi-core throughput under the
memory cap.
**Why:** unlocks per-core GC heaps — the main reason only one core was busy.
**Verify:** `scripts/sandbox/build.sh full` = 0/0.
**PUSH:** `scripts/git/push.sh "perf(server): enable Server GC for multi-core throughput"`

### [x] T2 — Add a single CPU-awareness helper
**New file:** `src/Animora.Server/Hosting/CpuTopology.cs` (internal static).
**Do:** expose `CpuTopology.LogicalCores` = `Math.Max(1, Environment.ProcessorCount)` (this value
already honors container/cgroup CPU quota in .NET — document that in the summary). Add helpers used
by later tasks, e.g. `ScaleByCores(int perCore, int min, int max)` =
`Math.Clamp(perCore * LogicalCores, min, max)`. One XML-doc paragraph explaining it is the single
source of truth for "scale with hardware" and that DB-bound limits must still clamp to the Postgres
budget (so it is intentionally NOT used to size DB pools). Keep it tiny and allocation-free.
**Verify:** build 0/0.
**PUSH:** `scripts/git/push.sh "feat(server): CPU topology helper for hardware-aware scaling"`

### [ ] T3 — Auto-scale CPU-bound worker fan-out from cores (DB-budget-clamped)
**Files:**
- `src/Animora.Server/Hosting/ServerOptions.cs` → in `WorkerSettings`, change the *meaning* of the
  defaults so a sentinel `0` means "auto from cores" for the CPU-bound widths. Concretely: add XML
  doc that `OutboxConsumers` / `RecoveryParallelism` use `0 = auto`. Keep current explicit defaults
  valid (explicit > 0 always wins). **Important:** do NOT change `OutboxClaimBatch` /
  `Recovery*Cap` semantics (those are DB-row budgets, not CPU widths) — leave them.
  *(If changing the default to 0 risks the existing validator ranges 1..8 / 1..16, instead keep the
  property default but compute the effective value at the consumption site — see below — and have the
  validator treat 0 as "auto". Prefer the consumption-site approach to avoid touching the validator's
  range rule; pick whichever keeps build 0/0 and validator semantics intact. Document the choice.)*
- `src/Animora.Server/Workers/OutboxRelayWorker.cs` → where it reads `_settings.OutboxConsumers`,
  resolve an effective width: `effective = OutboxConsumers > 0 ? OutboxConsumers :
  CpuTopology.ScaleByCores(perCore:1, min:2, max: <upper>)`, then **clamp to the worker DB budget**
  so consumers never exceed what `WorkerMax` connections can serve (consumers each open a scope/DB
  work). Use `Math.Min(effective, workerPoolBound)`. Keep the existing fan-out structure; only the
  width number becomes core-aware. (Read `DatabaseOptions.WorkerMax` via `IOptions<DatabaseOptions>`
  injected into the worker — follow the existing DI pattern in that file.)
- `src/Animora.Server/Workers/IntakeRecoveryWorker.cs` → same treatment for
  `_settings.RecoveryParallelism` in the `ParallelOptions { MaxDegreeOfParallelism = ... }` line:
  effective = explicit if >0 else cores, clamped to the worker admission/DB bound.
**Rule:** the resulting width must never exceed the Postgres-connection bound for that path — CPU
widens parallelism only up to what the DB budget allows. Add a 1-line comment at each site stating
"auto-scales with cores, clamped to DB worker budget".
**Verify:** build 0/0.
**PUSH:** `scripts/git/push.sh "perf(server): auto-scale worker fan-out with CPU cores (DB-budget clamped)"`

### [ ] T4 — Make the worker admission gate core-aware (bounded by DB budget)
**File:** `src/Animora.Persistence.PostgreSql/DependencyInjection/PersistenceServiceCollectionExtensions.cs`
(the `IBackgroundAdmission` registration, ~line 106).
**Do:** today it is `SemaphoreBackgroundAdmission(db.WorkerAdmissionMax>0 ? WorkerAdmissionMax :
clamp(WorkerMax,1,2))`. Keep `WorkerAdmissionMax>0` as the explicit override. Change the **auto**
branch so it scales with cores but is **hard-clamped to `WorkerMax`** (the DB connection budget for
background work) — e.g. `Math.Clamp(min(Environment.ProcessorCount, WorkerMax), 1, WorkerMax)`.
This lets a bigger box use more background concurrency **only up to the Postgres budget**, never
beyond. `CpuTopology` lives in `Animora.Server`; this file is in the Persistence project and must
NOT reference Server — so inline `Math.Max(1, Environment.ProcessorCount)` here with a comment
(duplication of one expression is acceptable to preserve layering; note it in the summary).
Leave `ISyncAdmission` sizing as-is (it is correctly sized off the write pool; widening it by cores
would over-subscribe the OLTP write pool — explicitly do NOT touch it, and say why in the summary).
**Verify:** build 0/0.
**PUSH:** `scripts/git/push.sh "perf(server): background admission scales with cores up to DB budget"`

### [ ] T5 — Documentation (keep it accurate, minimal)
**Files:** `src/Animora.Server/Hosting/ServerOptions.cs` XML docs touched in T3 (already done there);
plus the Persian setup guide `راهنمای-راه-اندازی-سرور.md` — add a short subsection stating: Server GC
is enabled; CPU-bound parallelism auto-scales with available cores (honoring container CPU limits);
DB connection pools remain memory-profile-bound by `Database:ProfileGiB` and are intentionally NOT
widened by cores to protect the Postgres connection budget; explicit `Server:Workers:*` /
`Database:*Max` values always override auto.
**Verify:** build 0/0 (docs don't affect build, but run once after T1-T4 combined if not already).
**PUSH:** `scripts/git/push.sh "docs(server): document multi-core / hardware-aware scaling model"`

### [ ] T6 — Final verification sweep
**Do:** one `scripts/sandbox/build.sh full` → confirm 0 warnings / 0 errors. Confirm no new
analyzer warnings. Confirm admission/pool invariants intact (DB pool sums still ≤ validator budget;
gates still separate). Do NOT run the long test suite. If a heavy fix was needed, it was already
pushed per the locked rule.
**PUSH (if anything changed):** `scripts/git/push.sh "chore(server): final multi-core verification"`

### [ ] T7 — Delete this plan file (FINAL task only)
**Do:** `rm Plan/multicore-server-upgrade.md` (and `Plan/` dir if now empty). Touch nothing else —
no roadmap files, no other docs.
**PUSH:** `scripts/git/push.sh "chore: remove completed multi-core upgrade plan"`

---

## Guardrails (read once, obey throughout)
- Only open files named in a task. No blind grepping. `Animora-UI/` is forbidden.
- Keep the two admission gates separate; keep DB pool sums within the validator budget.
- CPU scaling applies to **CPU-bound fan-out only** (GC heaps, worker/consumer widths, recovery
  parallelism), never to DB connection pools (Postgres budget) or the sync gate (OLTP write pool).
- Explicit config always wins over auto — preserve every existing "explicit > 0 wins" check.
- Push after every task. 0/0 build is the quality bar; no full/long test runs.
