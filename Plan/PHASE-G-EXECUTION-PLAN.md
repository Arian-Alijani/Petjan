# Band G execution plan — G07 · G08 · G09 (validation gates + release engineering)

> **Purpose.** This is the single, self-contained execution task-list for the final band (G07→G08→G09).
> A model opening this file in a **fresh session** can implement top-to-bottom **without re-reading the
> repo**: every fact it needs (real file paths, real test-class/method names that diverge from the phase
> prose, the `PgFact` mechanism, the ARCH §19 gate list, the migration head) is already distilled below.
> Only open a source file when a task explicitly says "edit/append" to it.
>
> **Source phase files** (read once if you want the full prose; NOT required thanks to this plan):
> `backend-roadmap/phases/PHASE-G07-validation-gates-ci-rls-restore.md`,
> `…/PHASE-G08-performance-proof-offline-budget.md`, `…/PHASE-G09-release-engineering.md`.

---

## 🔒 LOCKED RULES (never break)

1. **Push after EVERY completed task.** Run from repo root, on branch `genspark_ai_developer`:
   ```bash
   scripts/git/push.sh -b genspark_ai_developer "ci(gates): <task id> <short desc>"
   ```
   (The `-b` only needs the branch on the first push; subsequent pushes may omit it. Not pushing = failure.)
   If push exits code `3` → call the `setup_github_environment` tool once, re-run. Code `2` = rebase
   conflict (repo left clean) → resolve preferring remote, re-run.
2. **If a heavy/large fix or a large chunk of code is written mid-task, push immediately** with a clear
   message — don't wait for task end.
3. **No `src/**` change in this entire band.** G07/G08/G09 are gate-wiring/release-engineering only.
   Allowed write roots: `.github/**`, `scripts/**`, `tests/**`, `docs/**`, `artifacts/**` (generated),
   `backend-roadmap/PROGRESS.md`. If a gate exposes a real `src/**` bug, DO NOT fix it here — log it as a
   deviation in PROGRESS under the **owning** S/G phase id.
4. **Do NOT run full build / full test suite / long benches.** Verify narrowly (per-task note). The user
   forbids `scripts/sandbox/build.sh full`-as-a-loop and long test runs. CI (the workflows you author)
   is where the heavy lanes actually run.
5. **Last task = delete THIS file only** (`Plan/PHASE-G-EXECUTION-PLAN.md`). Touch nothing else in
   `Plan/`, do not touch the roadmap/phase files beyond the PROGRESS.md updates each phase mandates.

Status legend: `[ ]` todo · `[~]` in progress · `[x]` done.

---

## 📌 VERIFIED REPO FACTS (distilled 2026-10-11 — do not re-derive)

### Environment / tooling
- Build/test ONLY via sandbox scripts (mem-capped ~985 MiB): `scripts/sandbox/build.sh [fast|full]`,
  `scripts/sandbox/test.sh [filter]`, `scripts/sandbox/run.sh` (→ prints `SMOKE RUN OK`),
  `scripts/sandbox/bench.sh [offline|--label L]`.
- `dotnet-ef` lives at `.sandbox/tools/dotnet-ef` (never `dotnet tool install`). Not needed this band (no migration).
- `scripts/sandbox/doctor.sh` exists (health probe — already reports PostgreSQL reachability + build output;
  G07/G09 extend it).
- `scripts/sandbox/install-postgres.sh`, `scripts/sandbox/install-prereqs.sh` exist.
- `scripts/ci/` already exists: `ci-build.sh`, `ci-test.sh`, `ci-publish-desktop.sh`, `gh-ci.sh`, `_projects.sh`, `README.md`.
- `scripts/perf/` and `scripts/release/` do **NOT** exist yet (create them).
- `docs/` does **NOT** exist yet (G09 creates `docs/release/`).
- `.github/workflows/` currently holds ONLY `animora-ci.yml`. `pg-gates.yml`, `perf-gates.yml`,
  `release-gates.yml` do NOT exist yet.
- `scripts/windows/publish-portable.ps1` exists (portable packager — G09 edits it to add `-EmitManifest`).

### PostgreSQL test mechanism (CRITICAL — the phase prose says `[PgGated]` but the REAL mechanism is different)
- File `tests/Animora.Server.Tests/Persistence/PgFixture.cs` defines:
  - class `PgFixture : IAsyncLifetime` — env var `ANIMORA_PG_TEST_CONNECTION` (admin conn that can `CREATE DATABASE`),
    env var `ANIMORA_REQUIRE_PG` (=`"1"` makes a missing connection **throw/fail** instead of skip).
  - `PgFactAttribute : FactAttribute` — this is the real "PG-gated" marker. When `ANIMORA_PG_TEST_CONNECTION`
    is unset **and** `ANIMORA_REQUIRE_PG != 1`, it sets `Skip=…`. Under `ANIMORA_REQUIRE_PG=1` the fixture
    `InitializeAsync()` throws → the test FAILS. **This is the exact skip→fail switch G07 relies on.**
  - **There is NO `[PgGated]` trait in the repo.** G07's `PgGateManifestTests` must enumerate over the
    `PgFactAttribute` usages (grep/reflection), NOT a non-existent `[PgGated]` trait. (Deviation to log.)
- Existing CI (`animora-ci.yml`) already runs PG tests with `postgres:17` and a **superuser** role
  `animora/animora` and sets `ANIMORA_PG_TEST_CONNECTION` + `ANIMORA_REQUIRE_PG` when `with_postgres=true`.
  G07 requires a DISTINCT lane: `postgres:18`, a **non-owner / non-superuser / no-BYPASSRLS** app role
  (separate owner role runs migrations). Do NOT edit `animora-ci.yml`; author the new `pg-gates.yml`.

### G07 referenced tests — REAL class→file→method map (phase prose names diverge; use THESE)
All under `tests/Animora.Server.Tests/` unless noted. All already exist & are `[PgFact]`:
| Phase-prose name | Real class | Real file | Methods (real) |
|---|---|---|---|
| `VersionTriggerTests.Version_ChangesOnEveryUpdate_IncludingRawSql` | `PgGatedTests` | `Persistence/PgGatedTests.cs` | `Version_ChangesOnEveryUpdate_IncludingRawSql` |
| `MigrationTests.Applies101_OnEmptyDatabase` | `PgGatedTests` | `Persistence/PgGatedTests.cs` | `Applies101_OnEmptyDatabase` |
| `KeyRingTests.KeysSurviveContextRecreation` | `PgGatedTests` | `Persistence/PgGatedTests.cs` | `KeysSurviveContextRecreation` |
| `TenantIsolationTests.*` | `TenantIsolationTests` | `Persistence/TenantIsolationTests.cs` | `A_CannotRead_B_ById`, `A_CannotRead_B_List`, `Write_WithForeignTenant_Throws_TenantMismatch`, `NullContext_Throws_TenantContextMissing`, `SoftDeleteLift_StillHidesOtherTenant`, `PooledConnection_A_B_A_NoLeak` |
| `JournalOrderingTests.*` | `JournalOrderingTests` | `Replication/JournalOrderingTests.cs` | `SequenceOrder_EqualsCommitOrder_UnderConcurrency`, `RollbackConsumesNoSequence_NoGap`, `ChildOnlyChange_CapturesRoot`, `Delete_CapturesTombstoneKey`, `SameTransactionDuplicates_Coalesced`, `DirectGovernedSql_Captured`, `TwoTenants_IndependentCounters`, `EpochAdvance_FencesOldRows` |
| `JournalCoverageTests.EveryReplicatedTable_HasCaptureTrigger` | `JournalSqlTemplatesTests` | `Replication/JournalSqlTemplatesTests.cs` | `EveryReplicatedTable_HasCaptureTrigger_InMigration` (NOTE: `[Fact]` not `[PgFact]` — pure migration-text check) |
| `BatchFreezerTests.*` | `BatchFreezerTests` | `Sync/BatchFreezerTests.cs` | `WatermarkAndRows_FromOneSnapshot`, `SnapshotCommitted_BeforePageMaterialization`, `SameRange_SameBatchId_ByteStable`, `SnapshotOverMaxLifetime_AbortsAndRetries` |
| `GcTests.*` | `GcTests` | `Sync/BatchFreezerTests.cs` (same file!) | `JournalDeletion_AdvancesFloor_SameTransaction`, `CrashAfterFileDelete_BeforeRowDelete_Recovers` |
| `SyncIsolationTests.TenantA_Batch_ContainsNoTenantB_Rows` | `SyncIsolationTests` | `Sync/BatchFreezerTests.cs` (same file!) | `TenantA_Batch_ContainsNoTenantB_Rows` |
| `SyncFlowTests.OpenDownloadResume_FromLastPage` | `SyncFlowTests` | `Sync/BatchFreezerTests.cs` (same file!) | `OpenDownloadResume_FromLastPage` |
| `SnapshotAgeMonitorTests.OldSnapshot_ReportsDegraded` | `SnapshotAgeMonitorTests` | `Observability/SnapshotAgeMonitorTests.cs` | `OldSnapshot_ReportsDegraded` |
| `QueryBudgetPgTests.StatementTimeout_Enforced` | `QueryBudgetPgTests` | `Observability/QueryBudgetPgTests.cs` | `StatementTimeout_Enforced` (+ `Reporting_Concurrency1_Timeout5s_RowCap`) |
| `NativeSqliteVersionGateTests.RuntimeProbe_AtLeast_3_50_2` | `NativeSqliteVersionTests` | `tests/Animora.Persistence.Tests/NativeSqliteVersionTests.cs` | `LoadedNativeVersion_IsAtLeastTheMinimum` (asserts ≥ 3.50.2) — this is in the **Persistence.Tests** project, NOT Server.Tests |

### Tenancy / RLS reality
- `src/Animora.Persistence.PostgreSql/Tenancy/ITenantSessionInitializer.cs` exists; there is **NO**
  `RlsTenantSessionInitializer` and **NO** RLS policy SQL in the tree today. RLS is deferred (ROADMAP §5).
- Therefore `RlsLeakTests` (G07 new) must create its own RLS policies in the test (CREATE POLICY / ALTER
  TABLE … FORCE ROW LEVEL SECURITY) against the migrated schema, under the non-owner app role, and prove
  no leak across A→B→A pool reuse. It is a `[PgFact]`-gated test (so it only runs in the RLS CI lane).

### Layering tests already present (G08 reuses/extends, does NOT duplicate)
- `tests/Animora.Domain.Tests/LayeringRulesTests.cs`: `Domain_HasNoForbiddenReferences`,
  `Application_HasNoInfrastructureReferences`, `Contracts_ReferencesNoAnimoraAssembly`,
  `IdentityPackage_IsReferencedOnlyByInfrastructureLocal`, `NoMockNamespaceExists`.
- `tests/Animora.Server.Tests/ServerLayeringTests.cs`: `Server_DoesNotReferenceClientProjects`,
  `Server_ReferencesOnlyAllowedAnimoraAssemblies`, `Server_HasNoDirectNpgsqlReference`.
- `tests/Animora.Sync.Tests/SyncLayeringTests.cs`, `SyncCustomLayeringTests.cs` exist.
- → G08's new `ServerLayeringAuditTests` adds the 4 named methods from the phase (they overlap existing
  ones but the phase wants them under that class name; implement by reading compiled assembly reference
  sets, same technique as `ServerLayeringTests`).

### Wire-contract / mixed-fleet reality (G08 MixedFleetTests)
- `src/Animora.Contracts.Wire/WireContract.cs`: `Version = 2`; `SupportedContractVersions = [1,2]`;
  `IsSupported(int v) => v is 1 or 2`; header consts `HeaderName="X-Animora-Contract-Version"`,
  `SupportedVersionsHeaderName="X-Animora-Supported-Versions"`.
- `src/Animora.Server/Commands/RuleSetPolicy.cs`: **`internal sealed class RuleSetPolicy`** with
  `Current=1`, `SupportedFrom=1`, and a `Resolve(...)` returning `Result<int>` (absent→Current;
  `< SupportedFrom`→`RULESET_UNSUPPORTED`; between→re-derived). Because it's `internal`, `MixedFleetTests`
  must use the existing `InternalsVisibleTo` (Server.Tests already tests internal types — confirm the
  csproj has it; if a test already references internal Server types, it's enabled) OR drive the behavior
  through the public `POST /api/commands` endpoint + `TestHostFactory` (preferred: endpoint-level, so no
  visibility change needed). Error code `RULESET_UNSUPPORTED` / `CONTRACT_VERSION_UNSUPPORTED` + HTTP 426
  live in `src/Animora.Server/Commands/`.
- Server test host helper: `tests/Animora.Server.Tests/TestHostFactory.cs` (reuse it).

### Offline-budget harness reality (G08 offline-budget gate)
- `scripts/sandbox/bench.sh offline` → runs `Animora.Application.Tests` filtered to
  `OfflineBudgetMeasurements`, 60-s Xvfb idle, then calls
  `scripts/sandbox/offline-budget.py <app.log> <query.json> <rss_kb> <out.md>`.
- `offline-budget.py` writes its table to **`backend-roadmap/measurements/C11-offline-budget.md`** (that's
  where bench.sh points it), and **exits non-zero ONLY on a >2× budget regression**. 6 metrics & budgets:
  first visible frame ≤1200 ms · dashboard data ≤2500 ms · 60-s idle working set ≤300 MiB · patients
  200-row page ≤120 ms · global-search keystroke ≤80 ms · post-invoice-10-lines ≤250 ms.
- **Do NOT change thresholds.** G08 only wires this into a CI job that fails on the non-zero exit and
  archives the markdown table as an artifact. The phase's `artifacts/offline-budget.md` path is just the
  CI artifact copy — copy/point the bench output there in the job.

### ARCH §19 gate list (G09 LaunchGateManifestTests enumerates EXACTLY these 18)
From `ARCHITECTURE.md` §19 table (lines ~1183–1203). Gate letters + one-line meaning:
`A` idempotency/financial-effect-once · `D` dependency&ordering · `C` conflict policy ·
`N` non-destructive sync · `B` immutable batch atomicity · `L` tenant isolation (hardening OFF) ·
`P` three planes · `O` offline capability · `K` key-ring recovery · `U` licence authenticity ·
`F` file lifecycle · `S` subscription single-engine · `I` reconnect storm · `M` mixed fleet ·
`R` rehearsed restore · `Z` zero-Internet · `T` measured capacity · `PG` journal/snapshot integrity.
**That's the exact set (18) `LaunchGateManifestTests.EveryArch19Gate_IsCoveredByALane` must assert.**

### Migration / version heads (never hard-code elsewhere; here for reference only)
- Server migration head: `115_Files` (`src/Animora.Persistence.PostgreSql/ServerSchemaVersion.cs` →
  `ExpectedHead = "115_Files"`; migrations `101`–`115`). Key-ring context: separate sequence, head `101_KeyRing`.
- Client (SQLite) head: `033_OnlineEntitlement` (per PROGRESS).
- `WireContract.Version = 2`. `CommandTypes` live count 88 (never freeze). Both DbContexts migrate with EF.

### PROGRESS.md update locations
- `## Next phase` block ≈ line 144–146 (currently "Next phase: `G07`").
- `## Phase status` table ≈ line 157; rows `G07`/`G08`/`G09` currently `⬜ Not started` (≈ lines 234–236).
- "Repo state log" = append-only `### <PhaseID>` sections lower in the file (append, never rewrite old).
- Per phase: flip its status row to `✅ Done` + date + 1-line summary; advance Next phase; append one
  `### G0x` repo-state-log entry; log deviations. **G09 special:** leave "Next phase" as `M00` (T9 closed
  M00 already per PROGRESS; just append the G09 entry + flip G09 row — do NOT advance beyond).

---

# ═══════════════════════════════════════════════════════════════════════
# PHASE G07 — Validation gates I: mandatory PostgreSQL CI, tenancy/RLS, restore
# ═══════════════════════════════════════════════════════════════════════
# Commit prefix: `ci(gates): G07 …`  · Final commit msg (phase close):
#   "ci(gates): G07 mandatory PostgreSQL CI, tenancy/RLS & restore gates"
# Scope: ONLY .github/**, scripts/ci/**, scripts/sandbox/doctor.sh, tests/Animora.Server.Tests/**.
# NO migration, NO src/** change, NO ErrorCode.

- [x] **G07-T1 — `scripts/ci/pg-gate.sh` (new)**
  - `#!/usr/bin/env bash` + `set -euo pipefail`.
  - Assumes `ANIMORA_REQUIRE_PG=1` + `ANIMORA_PG_TEST_CONNECTION` already exported by the workflow (don't set them here).
  - Runs the PG-dependent suites via the sandbox entrypoint: `scripts/sandbox/test.sh Server` then
    `scripts/sandbox/test.sh Sync` then `scripts/sandbox/test.sh Persistence` (each inherits the env).
    (These three filters cover every class in the G07 real-map table.)
  - A non-zero test exit must fail the script (that's automatic with `set -e` + propagating the exit code;
    capture each RC and exit non-zero on first failure).
  - After success, parse the test logs under `$LOG_DIR` (see `scripts/sandbox/_env.sh` for `LOG_DIR`) for a
    "Skipped: N" count; if any skip > 0 → print `✗ PG gate: <n> skipped` and exit non-zero. Else print
    `✓ PG gate: <n> tests, 0 skipped`. (Under REQUIRE_PG=1 PgFact can't skip, so a skip means a real escape.)
  - Verify: `bash -n scripts/ci/pg-gate.sh` (syntax only — do NOT execute, no PG here).
  - **PUSH.**

- [x] **G07-T2 — `scripts/ci/restore-drill.sh` (new)**
  - `set -euo pipefail`. Implements ARCH §17.3 one-consistency-boundary drill. Steps (all against the CI
    PostgreSQL + a temp `DataRootPath`):
    (a) migrate schema as OWNER; provision 1 tenant + 1 known row + 1 stored file;
    (b) snapshot the THREE parts as one point-in-time set: `pg_dump` the server DB, `tar` the file tree,
        `pg_dump` the key-ring DB (`AnimoraKeyRingDbContext`, history table `__EFMigrationsHistory_KeyRing`);
    (c) restore all three onto a CLEAN database/dir;
    (d) start the server (or call `TestHostFactory` via the test — see G07-T4) and assert readiness `200` +
        the known tenant/row/file intact;
    (e) record elapsed restore seconds (numeric) to `artifacts/restore-rto.txt` (mkdir -p artifacts);
    (f) key-ring-missing variant: restore DB+files but NOT the key ring → assert `/health/live` = 200 but
        `/health/ready` = 503 and auth/business refused (ARCH §10.6, A-RULE 027).
  - Keep it operable both in CI and locally with a container; gate the actual DB ops behind
    `ANIMORA_PG_TEST_CONNECTION` being set (skip-print if unset so local `bash -n` style checks pass).
  - Verify: `bash -n scripts/ci/restore-drill.sh`. **PUSH.**

- [x] **G07-T3 — `tests/Animora.Server.Tests/RlsLeakTests.cs` (new, `[PgFact]`)**
  - Class `RlsLeakTests(PgFixture pg) : IClassFixture<PgFixture>` (mirror `TenantIsolationTests` setup).
  - Since no `RlsTenantSessionInitializer` exists, the test itself: migrates schema, creates the non-owner
    app role context, enables RLS on a tenant table (`ALTER TABLE … ENABLE ROW LEVEL SECURITY; … FORCE ROW
    LEVEL SECURITY; CREATE POLICY … USING (current_setting('app.tenant_id')= "TenantId") WITH CHECK (…)`),
    sets the tenant GUC per transaction, then proves no leak.
  - Methods: `A_B_A_PoolReuse_NoLeak`, `MissingContext_Denies`, `NonOwnerNoBypass_CannotSeeAll`,
    `RollbackCancellation_NoLeak`.
  - **If authoring full RLS SQL proves infeasible without src/** help, STOP, log a deviation in PROGRESS
    under G07, and still ship the file with the tests `[PgFact]` + a clear skip reason** (so the lane is
    wired and the gap is documented) rather than touching `src/**`.
  - Verify: `scripts/sandbox/build.sh fast` compiles Server.Tests (narrow; OK). **PUSH.**

- [x] **G07-T4 — `tests/Animora.Server.Tests/RestoreDrillTests.cs` (new)**
  - Class wrapping the drill logic for the suite: `Restore_ToReadiness_KnownTenantIntact` and
    `MissingKeyRing_RefusesReadiness_ServesLiveness`. `[PgFact]`-gated. Reuse `TestHostFactory` +
    `PgFixture`. May shell out to `scripts/ci/restore-drill.sh` OR replicate its asserts in-process
    (in-process preferred for determinism). Verify compiles. **PUSH.**

- [x] **G07-T5 — `tests/Animora.Server.Tests/Persistence/TenantIsolationTests.cs` (edit)**
  - Append one `[PgFact]` method `ClientSuppliedTenantId_Ignored_AndAudited` proving a client-supplied
    `TenantId` on a write is ignored (server stamps its own) and an audit entry is written (A-RULE 012/013).
    Model it on the existing `Write_WithForeignTenant_Throws_TenantMismatch` + the stamping interceptor.
  - Verify compiles. **PUSH.**

- [x] **G07-T6 — `tests/Animora.Server.Tests/PgGateManifestTests.cs` (new)**
  - **DEVIATION (log it):** phase says enumerate a `[PgGated]` trait; the repo uses `PgFactAttribute`.
    Implement `EveryPgGatedTest_IsInTheRequiredList` to: reflect over the loaded test assemblies
    (`Animora.Server.Tests`, `Animora.Sync.Tests`, `Animora.Persistence.Tests`), collect every method
    decorated with `PgFactAttribute` (and the named Native-SQLite gate), and assert the discovered set
    **equals** the required list hard-coded from the G07 real-map table above (FQN strings). This stops a
    future PG test silently escaping the mandatory lane. Pure reflection — not PG-gated itself.
  - Verify compiles. **PUSH.**

- [x] **G07-T7 — `scripts/sandbox/doctor.sh` (edit)**
  - It already prints a `postgres:` line (reachability). Add a line reporting whether `ANIMORA_REQUIRE_PG`
    is set (so a dev can tell they're in the strict lane). Keep it informational (never fails the shell).
  - Verify: `bash -n scripts/sandbox/doctor.sh`. **PUSH.**

- [x] **G07-T8 — `.github/workflows/pg-gates.yml` (new)**
  - Model the PG service on `animora-ci.yml`'s `linux` job BUT: `image: postgres:18`; create a SEPARATE
    non-owner app role in an init step:
    ```
    CREATE ROLE animora_owner LOGIN PASSWORD '…';           -- runs migrations (DDL)
    CREATE ROLE animora_app   LOGIN PASSWORD '…' NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE;
    GRANT (table DML + journal function EXECUTE) TO animora_app;  -- NO DDL, NO BYPASSRLS
    ```
  - Steps: checkout → `actions/setup-dotnet@v4` (DOTNET_VERSION from the other workflow) →
    `scripts/sandbox/install-prereqs.sh` → apply server migrations AS OWNER
    (`.sandbox/tools/dotnet-ef database update -c AnimoraServerDbContext` + the key-ring context) →
    export `ANIMORA_REQUIRE_PG=1` and `ANIMORA_PG_TEST_CONNECTION` (pointing at the **app role**) →
    then jobs:
    - job `pg-correctness`: run `scripts/ci/pg-gate.sh`; upload logs.
    - job `pg-rls-hardening`: app role non-owner/non-super/no-BYPASSRLS; run `scripts/sandbox/test.sh`
      filtered to `RlsLeakTests`; must be GREEN (not allowed-to-fail). Also run the RLS-OFF mandatory
      `TenantIsolationTests` here or in correctness.
    - job `restore-drill`: run `scripts/ci/restore-drill.sh`; upload `artifacts/restore-rto.txt`.
  - Ensure `grep -rn "ANIMORA_REQUIRE_PG" .github/workflows/pg-gates.yml scripts/ci/pg-gate.sh` ≥ 1.
  - Verify YAML: `python3 -c "import yaml,sys; yaml.safe_load(open('.github/workflows/pg-gates.yml'))"`.
  - **PUSH.**

- [x] **G07-T9 — Local sanity (NARROW, no full build/test)**
  - `scripts/sandbox/build.sh fast` (compiles the touched test project) — must be 0/0 for the files you
    added. If it's heavy, limit to confirming the new test files compile. Do NOT run the whole suite.
  - Do NOT run `run.sh` repeatedly; the desktop is unaffected (no src change) — a single optional
    `scripts/sandbox/run.sh` → `SMOKE RUN OK` is acceptable but skip if time-costly.
  - **PUSH** any fixups.

- [x] **G07-T10 — PROGRESS.md (G07 close)**
  - Flip `G07` row → `✅ Done` (+ 2026 date + 1-line summary). Advance `## Next phase` → `G08`.
  - Append one `### G07` repo-state-log entry: pg-gates.yml (3 jobs), pg-gate.sh + restore-drill.sh,
    the promoted PG-gated set now mandatory (list the real class names), RlsLeakTests hardening lane,
    restore+key-ring drill + restore-rto.txt artifact, PgGateManifestTests.
  - Log the two deviations: (1) `[PgGated]`→`PgFactAttribute`; (2) RLS SQL authored in-test (no src RLS
    initializer). Commit:
    `scripts/git/push.sh -b genspark_ai_developer "ci(gates): G07 mandatory PostgreSQL CI, tenancy/RLS & restore gates"`.

### G07 acceptance self-check (mechanical — confirm each before closing)
1. No `src/**` diff: `git diff --name-only origin/main..HEAD -- src/` empty.
2. `grep -rn "ANIMORA_REQUIRE_PG" .github/workflows/pg-gates.yml scripts/ci/pg-gate.sh` ≥ 1.
3. `pg-gates.yml` has jobs `pg-correctness`, `pg-rls-hardening`, `restore-drill` + `postgres:18` + a
   non-owner app role init.
4. New test files compile; `PgGateManifestTests` lists the real class→method set.
5. `artifacts/restore-rto.txt` is produced by the drill (numeric seconds) — verified by the job, not locally.

# ═══════════════════════════════════════════════════════════════════════
# PHASE G08 — Validation gates II: performance proof & offline budget
# ═══════════════════════════════════════════════════════════════════════
# Final commit msg (phase close): "ci(gates): G08 performance proof & offline budget"
# Scope: ONLY .github/**, scripts/perf/**, tests/**. NO migration, NO src/**, NO ErrorCode.
# Prereq: G07 done (reuses its postgres:18 service shape).

- [x] **G08-T1 — `scripts/perf/README.md` + `scripts/perf/` dir (new)**
  - `mkdir -p scripts/perf`. README: how to run each harness locally (offline-budget, server-capacity,
    zero-internet) + note doctor.sh reports PG reachability (added in G07). **PUSH.**

- [x] **G08-T2 — `scripts/perf/server-capacity.sh` (new)**
  - `set -euo pipefail`. Launches the in-tree server in a cgroup-constrained container
    (`--cpus=2 --memory=2g`, the 1–3 core / 2–4 GiB reference) with PostgreSQL 18 (G07 service shape), then
    runs the xUnit-hosted `CapacityDriver` (G08-T3) which drives two independent profiles (§6 two pipelines):
    command-intake (`POST /api/commands` + `/batch`) and state-pull (`/sync/manifest|batches|pages`).
  - Writes `artifacts/server-capacity.json` + a markdown summary with throughput, p50/p95/p99, peak server
    RSS, PostgreSQL connection count. Label the artifact **"planning reference only"** (A-RULE 035 — recorded,
    never a guaranteed user count).
  - Verify `bash -n`. **PUSH.**

- [x] **G08-T3 — `tests/Animora.Server.Tests/Capacity/CapacityDriver.cs` (+ profiles) (new)**
  - xUnit-hosted load driver reusing `TestHostFactory` + `PgFixture`. Ramps concurrency on the two profiles;
    records per-profile metrics to the artifact. **Mechanical assertions (not a user count):** zero
    unexpected 5xx; NO connection-pool exhaustion (active conns ≤ the S18 bounded pool sum — pool sums are
    12/20 per PROGRESS S18 note; read the actual bound from the server's pool config if accessible, else
    assert ≤ a documented constant and note it); p99 bounded under the configured `statement_timeout` (S18);
    readiness green at end. `[PgFact]`-gated. Verify compiles. **PUSH.**

- [x] **G08-T4 — `tests/Animora.Sync.Tests/ReconnectStormTests.cs` (new)**
  - Deterministic (fake `IDelayProvider` + `TimeProvider`, no real sleep). Spin N simulated devices all
    "due" at t0 using the G03 `SyncSchedulerHostedService` jitter (±20% + capped exponential backoff, per
    device). Method `FleetRestart_DoesNotConverge_JitterSpreadsReconnects`: assert next-attempt times form a
    SPREAD histogram (max bucket ≤ a fraction of N) — a fleet restarting together does NOT converge (gate I,
    ARCH §12.3). Pure, fast. Verify compiles. **PUSH.**

- [x] **G08-T5 — `tests/Animora.Server.Tests/MixedFleetTests.cs` (new)**
  - Drive through the public endpoint via `TestHostFactory` (avoids needing `RuleSetPolicy` internals).
    Uses `WireContract` ({1,2}) + `RuleSetPolicy` (Current=1, SupportedFrom=1). Methods:
    `V1Client_CallsSync_Gets426_WithSupportedVersions` (sync DTOs are v2-only; expect 426 +
    `X-Animora-Supported-Versions` header), `V2Command_Accepted`, `V1Command_StillAccepted` (commands
    support {1,2}), `UnknownVersion_Rejected_426`, `StaleRuleSetVersion_BelowSupportedFrom_RejectedWithReason`
    (→ `RULESET_UNSUPPORTED`), `RuleSetVersion_Between_ServerReDerives_NotForceApplied`.
  - Some are `[PgFact]` if they need the DB; version-header rejection paths may be pure host tests. Verify
    compiles. **PUSH.**

- [x] **G08-T6 — `tests/Animora.Server.Tests/ServerLayeringAuditTests.cs` (new)**
  - Read compiled-assembly reference sets (same technique as existing `ServerLayeringTests`). Methods:
    `NoClientProjectReferencesAServerProject_ExceptLicensingVerification`,
    `AnimoraServer_DoesNotReference_PersistenceSqlite_Or_Desktop`,
    `OnlyPersistencePostgreSql_ReferencesNpgsql`,
    `SyncCustom_HasNoNpgsql_NoServerRef`. (Overlaps existing layering tests but the phase wants this class.)
  - Verify compiles. **PUSH.**

- [ ] **G08-T7 — `scripts/perf/zero-internet.sh` + `tests/Animora.Server.Tests/ZeroInternetTests.cs` (new)**
  - Script: (a) STATIC grep of the published/built output for `http(s)://` endpoints NOT loopback/LAN/
    localhost/the configured server base; any `cdn…`/`fonts.googleapis…`/external host = FAILURE (gate Z,
    ARCH §17.4). (b) runtime leg: run `scripts/sandbox/run.sh` with egress blocked (sandbox already has no
    egress) → assert `SMOKE RUN OK`.
  - Test `ZeroInternetTests.Runtime_OperatesWithEgressBlocked` reports `SMOKE RUN OK`. Verify `bash -n` +
    test compiles. **PUSH.**

- [ ] **G08-T8 — `.github/workflows/perf-gates.yml` (new)**
  - Jobs: `offline-budget`, `server-capacity`, `zero-internet`, `layering-audit`, `sync-behavioral`.
  - `offline-budget` job: build Debug → `scripts/sandbox/bench.sh offline` → run
    `scripts/sandbox/offline-budget.py` and copy its table to `artifacts/offline-budget.md`; job RED iff the
    script exits non-zero (>2× regression). Upload the table every run (pass or fail). **Don't change thresholds.**
    NOTE: bench.sh already invokes offline-budget.py writing to `backend-roadmap/measurements/C11-offline-budget.md`;
    in the job, after `bench.sh offline`, copy that file to `artifacts/offline-budget.md` (mkdir -p artifacts).
  - `server-capacity`: `--cpus=2 --memory=2g` + postgres:18 → `scripts/perf/server-capacity.sh` → upload
    `artifacts/server-capacity.json`.
  - `zero-internet`: `scripts/perf/zero-internet.sh`.
  - `layering-audit`: `scripts/sandbox/test.sh Domain` (hosts `LayeringRulesTests`) + `ServerLayeringAuditTests`.
  - `sync-behavioral`: `ReconnectStormTests` + `MixedFleetTests`.
  - Verify YAML parses (`python3 -c "import yaml; yaml.safe_load(open(...))"`). **PUSH.**

- [ ] **G08-T9 — Local sanity (NARROW)**
  - `scripts/sandbox/build.sh fast` to confirm new test files compile (0/0 for touched projects). Optionally
    one `scripts/sandbox/bench.sh offline` to confirm it emits the table + exit 0 (ONLY if it's not too slow;
    it's a 60-s idle run — acceptable once, skip if constrained). Do NOT run full suites. **PUSH** fixups.

- [ ] **G08-T10 — PROGRESS.md (G08 close)**
  - Flip `G08` → `✅ Done` (+date+summary). Advance `## Next phase` → `G09`. Append `### G08` repo-state-log
    entry (perf-gates.yml 5 jobs, offline-budget promotion, capacity harness+artifact, reconnect-storm,
    mixed-fleet, zero-internet, layering-audit). Log deviations (offline-budget output path copy; capacity
    pool-bound constant source). Commit:
    `scripts/git/push.sh -b genspark_ai_developer "ci(gates): G08 performance proof & offline budget"`.

### G08 acceptance self-check
1. No `src/**` diff for the phase. 2. `perf-gates.yml` has the 5 named jobs + valid YAML. 3. New test
classes compile with the named methods. 4. offline-budget job wires `bench.sh offline` + `offline-budget.py`
and archives `artifacts/offline-budget.md`. 5. capacity artifact labelled "planning reference only".

# ═══════════════════════════════════════════════════════════════════════
# PHASE G09 — Release engineering (topology, runbooks, backup/restore, packaging, SBOM, launch-gate)
# ═══════════════════════════════════════════════════════════════════════
# Final commit msg (phase close):
#   "docs(release): G09 release engineering (topology, upgrade/rollback, backup/restore, packaging, SBOM)"
# Scope: ONLY docs/release/**, scripts/release/**, .github/**, scripts/windows/publish-portable.ps1 (edit),
#   scripts/sandbox/doctor.sh (edit), tests/Animora.Server.Tests/**.
# NO migration, NO src/**, NO package pin, NO .sln edit, NO ErrorCode. Prereq: G07 + G08 done.

- [ ] **G09-T1 — `docs/release/01-deployment-topology.md` (new)**
  - `mkdir -p docs/release`. One-host topology (single ASP.NET Core process + PostgreSQL 18 + the
    `DataRootPath` file tree + the Data-Protection key ring). Migration discipline (ARCH §17.1): an
    owner/DDL role runs `dotnet-ef database update` BEFORE traffic; runtime app role has NO DDL rights;
    `EnsureCreated` forbidden outside test fixtures; server migrations numbered from 101 (head `115_Files`),
    never edited later. First-install checklist (provision roles, migrate as owner, seed key ring, verify
    readiness) ending in copy-pasteable commands. **PUSH.**

- [ ] **G09-T2 — `docs/release/02-upgrade-rollback.md` (new)**
  - Same single process; deploy via stop-migrate-start OR blue/green. Because migrations are additive and
    the wire contract is versioned (N/N-1/N-2 via `WireContract.IsSupported`, currently {1,2}), a
    one-step-back rollback restores the previous binary and does NOT strand clients. State the explicit
    rollback boundary (how far back a binary rolls without schema divergence). Cite `WireContract.IsSupported`
    as the rolling-upgrade mixed-fleet guarantee. Copy-pasteable checklist. **PUSH.**

- [ ] **G09-T3 — `docs/release/03-backup-restore-hostmove.md` (new)**
  - THREE-part single consistency boundary captured as ONE point-in-time set: (1) PostgreSQL DB, (2) the §14
    file-content tree, (3) the Data-Protection key ring (forgetting the key ring = silent disaster). Must
    include, as greppable terms: a named **RPO cadence** (e.g. PITR/WAL archiving for small RPO, else
    scheduled logical dumps — a concrete number, never "best effort"); a **measured RTO** that CITES
    `artifacts/restore-rto.txt` (produced by the G07 drill); the **off-host durability** rule (≥1 copy on
    different hardware; co-located doesn't count); the **host-move/promotion** path (same restore + repoint
    config; stable `ApplicationName` keeps the key ring valid); and the **named scale-out trigger** (the
    §16.2 switch at which single-host is no longer acceptable). Copy-pasteable checklist.
  - Self-check grep: `grep -Eo "RPO|RTO|restore-rto.txt|off-host|scale-out" docs/release/03-backup-restore-hostmove.md`.
  - **PUSH.**

- [ ] **G09-T4 — `scripts/release/make-release-manifest.sh` (new)**
  - `mkdir -p scripts/release`. `set -euo pipefail`. Against a built output, generates:
    (a) a NuGet **SBOM** via first-party tooling (`dotnet` SBOM / CycloneDX task — NO cloud call); assert
        `NuGetAuditMode=all` + lock-file mode are on (check `Directory.Build.props` / `Directory.Packages.props`
        — they exist at repo root; just read/report, do NOT edit them);
    (b) a **native-binary inventory** JSON listing every native artifact (`*.dll`, `*.dylib`, `*.so`,
        `e_sqlite3`/`e_sqlite3mc`/SQLCipher) with version + RID (STACK §16 — a NuGet SBOM alone is insufficient);
    (c) a **suppression-ledger** file whose schema requires `advisoryId, justification, affectedPackage,
        compensatingControl, expiry, owner`, and that **exits non-zero** if any native-DB/crypto entry
        (`sqlcipher`/`e_sqlite3`/`e_sqlite3mc`) lacks an `expiry` (no permanent suppression).
  - Verify `bash -n`. **PUSH.**

- [ ] **G09-T5 — `scripts/windows/publish-portable.ps1` (edit)**
  - Add a `-EmitManifest` switch (PowerShell param) that, after the portable publish, calls the manifest
    generator (G09-T4 logic — invoke the bash script via `bash` or replicate minimally in PS) against the
    portable output folder so the SBOM + native inventory ship WITH the package. **Do NOT rewrite the
    existing publish logic** — only add the switch + the call. Verify the token exists:
    `grep -n "EmitManifest" scripts/windows/publish-portable.ps1`. **PUSH.**

- [ ] **G09-T6 — `scripts/release/launch-gate.sh` (new)**
  - `set -euo pipefail`. Aggregates every ARCH §19 lane: collects the G07 `pg-gates.yml` conclusion
    (gives A/D/C/N/B/L/K/R/PG + tenancy/RLS), the G08 `perf-gates.yml` conclusion (I/M/Z/T + offline-budget
    + layering), the desktop smoke (`scripts/sandbox/run.sh` → `SMOKE RUN OK`), SBOM + native-inventory
    presence, and a **zero-Internet built-output scan** (grep packaged assets for `cdn`/`fonts.googleapis`/
    external endpoints — the G08 gate Z PROMOTED to a ship-blocker). **Exits non-zero** unless EVERY lane is
    green. On success writes a dated `artifacts/launch-gate-report.md` enumerating each gate → PASS + evidence
    pointer. Also support `--dry-run` which just lists every ARCH §19 lane it would require (no execution).
  - The 18 §19 gates to enumerate: A D C N B L P O K U F S I M R Z T PG (see VERIFIED FACTS).
  - Verify `bash -n` and `scripts/release/launch-gate.sh --dry-run` lists all 18 lanes. **PUSH.**

- [ ] **G09-T7 — `.github/workflows/release-gates.yml` (new)**
  - Depends on `pg-gates.yml` + `perf-gates.yml` (via `workflow_run` or reusable-workflow `needs`). A smoke
    job (`run.sh` → `SMOKE RUN OK`) + a `launch-gate` job running `scripts/release/launch-gate.sh`; archive
    `artifacts/launch-gate-report.md`. Verify YAML parses. **PUSH.**

- [ ] **G09-T8 — `tests/Animora.Server.Tests/LaunchGateManifestTests.cs` (new)**
  - `EveryArch19Gate_IsCoveredByALane`: assert the aggregator's gate list (parse `launch-gate.sh` for the
    letters, or hard-code the expected set) **equals** the ARCH §19 table set = {A,D,C,N,B,L,P,O,K,U,F,S,I,
    M,R,Z,T,PG} (18). Stops a future gate escaping the launch bar. Pure test. Verify compiles. **PUSH.**

- [ ] **G09-T9 — `scripts/sandbox/doctor.sh` (edit)**
  - Add a line reporting presence of `pg-gates.yml`, `perf-gates.yml`, `publish-portable.ps1`. Informational,
    never fails. Verify `bash -n`. **PUSH.**

- [ ] **G09-T10 — Local sanity (NARROW)**
  - `scripts/sandbox/build.sh fast` to confirm `LaunchGateManifestTests` compiles. Run
    `scripts/release/launch-gate.sh --dry-run` → must list all 18 lanes. Do NOT run full suites/aggregator
    locally (it needs CI lane artifacts). **PUSH** fixups.

- [ ] **G09-T11 — PROGRESS.md (G09 close)**
  - Flip `G09` → `✅ Done` (+date+summary). **Leave `## Next phase` as `M00`** (per the G09 phase file —
    T9 already closed M00 historically; if "Next phase" already says `S01` from T9, leave it; do NOT regress
    it. The rule is: do not advance it FORWARD past where it is — just close G09). Append `### G09`
    repo-state-log entry (the 3 runbooks, make-release-manifest.sh + publish-portable.ps1 -EmitManifest,
    launch-gate.sh + release-gates.yml, SBOM/native-inventory + suppression-ledger, LaunchGateManifestTests +
    launch-gate report). Log deviations. Commit:
    `scripts/git/push.sh -b genspark_ai_developer "docs(release): G09 release engineering (topology, upgrade/rollback, backup/restore, packaging, SBOM)"`.

### G09 acceptance self-check
1. 3 runbooks exist; `03-…` names RPO cadence, cites `restore-rto.txt`, off-host rule, scale-out trigger
   (grep). 2. `make-release-manifest.sh` emits SBOM + native-inventory JSON (lists e_sqlite3/e_sqlite3mc/
   SQLCipher w/ version+RID) + rejects native-crypto suppression without `expiry`. 3.
   `grep -n "EmitManifest" scripts/windows/publish-portable.ps1` ≥ 1. 4. `launch-gate.sh --dry-run` lists
   all 18 §19 lanes; exits non-zero on missing/red lane. 5. `release-gates.yml` needs both gate workflows +
   smoke + launch-gate job + archives the report. 6. `LaunchGateManifestTests` green. 7. no `src/**` diff.

# ═══════════════════════════════════════════════════════════════════════
# FINAL TASK — delete this plan file
# ═══════════════════════════════════════════════════════════════════════
- [ ] **FINAL — delete `Plan/PHASE-G-EXECUTION-PLAN.md` ONLY** (leave `Plan/.gitkeep` and everything else).
  `git rm Plan/PHASE-G-EXECUTION-PLAN.md` then
  `scripts/git/push.sh -b genspark_ai_developer "chore(plan): remove completed Band-G execution plan"`.
  Do NOT touch any roadmap/phase files beyond the PROGRESS.md updates already done per phase.

---

## 🏁 AFTER G09 — "what's ready" & "can I ship to a server?" (answer for the user)

**What becomes ready when G07→G08→G09 are green:**
- A full **validation-gate suite wired into CI**: mandatory PostgreSQL correctness lane (journal/snapshot/
  batch/tenant-isolation/key-ring/snapshot-age/query-budget), RLS-hardening proof, rehearsed restore drill
  with a measured RTO, offline-budget gate, weak-server capacity harness, reconnect-storm, mixed-fleet,
  zero-Internet, layering audit — plus a **launch-gate aggregator** that refuses "release" unless every
  ARCH §19 gate is green, and a packaging path that emits an SBOM + native-binary inventory + suppression
  ledger. Three operator runbooks (deploy / upgrade-rollback / backup-restore) are written to the
  single-operator bar.

**Can the program go on a server after G09? — Honest answer: NOT automatically / not yet by itself.**
- G07–G09 are **gate-wiring and release-engineering phases** — they add NO runtime code (`src/**` untouched
  all band). They make "ready to release" a MECHANICAL, earned state; they do not themselves execute the
  server end-to-end.
- The server implementation track **S01–S20 is authored but its execution was never claimed** (PROGRESS: "server-wide
  execution, PostgreSQL migration application, restore and performance acceptance are not claimed"). The
  desktop runs fully offline; the server is code-complete-on-paper but unproven at runtime.
- **The real green-light is: run the three new CI workflows (`pg-gates.yml`, `perf-gates.yml`,
  `release-gates.yml`) against a real PostgreSQL 18 and have the launch-gate aggregator exit GREEN.** Only
  that aggregator's green exit "earns" production-readiness (ARCH §19 — no label is declared in prose).
- So the sequence to actually ship: (1) finish G07–G09 (this plan) → (2) trigger the gate workflows in CI
  with the PostgreSQL service → (3) fix any `src/**` defect a gate exposes UNDER ITS OWNING S/G PHASE (not
  in G07–G09) until all lanes are green → (4) the launch-gate report shows every §19 gate PASS → (5) follow
  `docs/release/01/02/03` runbooks to deploy the single ASP.NET Core process + PostgreSQL 18 + file tree +
  key ring on the host.
- **Bottom line for the user:** after G09 the *machinery and runbooks to ship and to PROVE shippability
  exist and the desktop is offline-ready; the server can be deployed by following the G09 runbooks, but you
  should only trust it in production once the launch-gate aggregator runs green in CI against a real
  PostgreSQL (that run — not merely finishing G09's files — is what certifies it).**
