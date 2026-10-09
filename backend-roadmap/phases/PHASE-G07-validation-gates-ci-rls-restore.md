# Phase G07 — Validation gates I: mandatory PostgreSQL CI, tenancy/RLS gate, restore gate

**Band:** M · **Track:** G (sync / integration / release) · **Depends on:** G06, S18 · **Next:** G08
**Satisfies:** R-B19, R-B32 · **Runnable after this phase:** desktop offline yes · server required no
**Produced by:** M00 (regenerated 2026-10-09; regenerates scope-inventory row E08 (first half; Track-D/E planning files deleted in the 2026-10-08 server-track reset)).

> Baseline verified 2026-10-09: server migration head = the last server ordinal shipped by S02–S16 (list `src/Animora.Persistence.PostgreSql/Migrations/` and read `ServerSchemaVersion.ExpectedHead` — **never** hard-code), client migration head = the last client ordinal after G04/G06 (list `src/Animora.Persistence.Sqlite/Migrations/`), `CommandTypes` = live `CommandTypes.cs` (88 today — never freeze), `ErrorCodes` = live count; `src/` server projects exist (`Animora.Server`, `Animora.Persistence.PostgreSql`, `Animora.Server.Licensing`, `Animora.Web.Admin`) and client sync projects (`Animora.Sync.Abstractions`, `Animora.Sync.Custom`).
> **Next-free-ordinal rule.** This phase adds **no migration** — it is a **validation-gate** phase: it wires CI, a PG test harness, and a rehearsed restore drill; it writes `.github`/`scripts`/`tests` glue only, never a schema change. If a gate exposes a real bug, the **owning** S/G phase fixes it (not here); G07 only makes the gate run and fail loudly.

---

## ⚡ Zero-exploration contract (read this, then implement top-to-bottom)

> This file is self-contained and pre-verified. The named PG-gated tests it promotes to **mandatory** already exist (authored by
> S02/S03/S11/S12/S18 and marked "skip locally, mandatory in G07"); G07 does **not** re-invent them — it makes the CI lane run them
> under a real PostgreSQL with `ANIMORA_REQUIRE_PG=1` so a skip becomes a **failure**. Every gate here is a **named test / CI exit
> code**, never prose. Do not re-open the repo to "understand" it; if a snippet diverges, fix the smallest thing and log it.
> Implement exactly this scope: nothing from G08 (perf/offline budget) or G09 (release packaging).

**Environment (do not probe):**
- Build / test only through the sandbox scripts (memory-capped ~985 MiB; never raw `dotnet build`):
  `scripts/sandbox/build.sh [fast|full]`, `scripts/sandbox/test.sh [filter]`. The CI lane this phase adds runs the **same**
  entrypoints with a real PostgreSQL reachable and `ANIMORA_REQUIRE_PG=1` set.
- `dotnet-ef` lives at `.sandbox/tools/dotnet-ef` — **never** `dotnet tool install`. Not needed here (no migration).
- **PostgreSQL for the gate:** PostgreSQL **18** + `Npgsql.EntityFrameworkCore.PostgreSQL` **10.0.3** (the pins S02 froze) — in CI a
  service container, runtime role **non-owner / non-superuser / no `BYPASSRLS`** (STACK §12). **Forbidden tech (restated):** Redis,
  message broker, cloud IdP, cloud telemetry, CDN, EF Core 11, per-tenant database, hard-coded TLS version, Native AOT with EF Core.

**Offline invariant.** G07 adds **no runtime code** and no client dependency — the desktop still builds, starts and runs fully
offline exactly as after G06 (`scripts/sandbox/run.sh` → `SMOKE RUN OK`). The PostgreSQL gate is a **CI/release obligation**, not a
runtime path (A-RULE 016/032: zero mandatory external service at runtime). Running the gate requires a server + PostgreSQL, but
*passing the product* does not require either at runtime.

**Condensed CONVENTIONS rules used here:** §1 (a PG-dependent test **skips with a reason** locally and **fails** under
`ANIMORA_REQUIRE_PG=1` — the switch G07 flips in CI); §2 layering (gate glue lives in `tests/**`, `scripts/**`, `.github/**` — no
`src/**` change); §12 isolation-with-hardening-OFF (the tenant-isolation gate must pass with RLS **off**, then the optional RLS
hardening gate proves RLS **on** leaks nothing).

---

## Read before starting
- `backend-roadmap/PROGRESS.md` — top section only.
- `STACK.md` §12 (the technology PoC gates: **PostgreSQL RLS + transaction-local context + pooling**, **PostgreSQL
  journal/snapshot/provider integration**, **Native SQLite version assertion**; note G1/K2 are *optional*/*encryption* gates, not
  this phase's mandatory set) — only §12.
- `ARCHITECTURE.md` §9.2 (tenant isolation — three layers that hold with hardening OFF; null context throws; client `TenantId`
  ignored+audited), §10.6 (key-ring recovery invariant — missing key ring refuses readiness/auth), §17.3 (backup/restore/host-move
  disaster-recovery matrix — DB + files + key ring as **one** consistency boundary; RPO/RTO measured; the rehearsed restore drill),
  §19 (the gate table — this phase owns gates **L, K, R, PG**, and the tenant-isolation and RLS gates; G08 owns the perf/offline/
  mixed-fleet/Z/T gates) — only these `§`.
- The already-authored PG-gated test lists (**do not re-derive**, promote to mandatory): `PHASE-S02-...md` §Acceptance
  (`VersionTriggerTests.*`, `MigrationTests.Applies101_OnEmptyDatabase`, `KeyRingTests.KeysSurviveContextRecreation`),
  `PHASE-S03-...md` (`TenantIsolationTests.*`), `PHASE-S11-...md` (`JournalOrderingTests.*`), `PHASE-S12-...md`
  (`BatchFreezerTests.*`, `GcTests.*`, `SyncIsolationTests.*`, `SyncFlowTests.*`), `PHASE-S18-...md`
  (`SnapshotAgeMonitorTests.*`, `QueryBudgetPgTests.*`).
- (Nothing else. `src/**`, `Animora-UI/**`, `Database-Sampel/**` are out of bounds — this is a gate-wiring phase.)

## Goal

Turn the PostgreSQL-dependent correctness invariants that S02–S18 authored (and left "skip locally") into a **mandatory CI lane that
fails** if any of them is skipped or red: a GitHub Actions workflow that spins up PostgreSQL 18 as a service container with a
**non-owner, non-superuser, no-`BYPASSRLS`** runtime role, sets `ANIMORA_REQUIRE_PG=1`, and runs the server/sync test suites so the
journal-ordering, immutable-batch-atomicity, tenant-isolation, key-ring, snapshot-age and query-budget gates are **executed and
recorded** (ARCH §19 gates **L/K/PG** + STACK §12 PG integration). Add the **tenancy/RLS gate** as two distinct lanes — mandatory
application isolation with RLS **off**, and the *optional* RLS-hardening lane proving RLS **on** never leaks under A→B→A pool reuse
(STACK §12; ROADMAP §5 keeps RLS deferred as hardening, but its mechanics are proven here before anyone enables it). Add the
**rehearsed restore gate** (ARCH §17.3): a scripted drill that restores DB + files + Data-Protection key ring onto a clean host to
**readiness + a known tenant**, records elapsed RTO, and proves the key-ring-missing path refuses readiness honestly (gate **K/R**).
Every acceptance criterion is a named test or a CI job exit code — no prose gate.

---

## §Reference pack (verbatim from the authoring phases, 2026-10-09)

```text
# The CONVENTIONS §1 / S02–S18 skip-switch this phase flips (verbatim from every server phase's ⚡ banner):
#   a PG-dependent test "skip with a reason locally (CONVENTIONS §1) and FAIL under ANIMORA_REQUIRE_PG=1
#   (mandatory CI/release gate, owned by G07)."
# => G07's CI lane exports ANIMORA_REQUIRE_PG=1 so SkipException becomes a test failure.

# PG-gated tests promoted to MANDATORY here (owning phase → test class.method), run under ANIMORA_REQUIRE_PG=1:
#  S02  VersionTriggerTests.Version_ChangesOnEveryUpdate_IncludingRawSql
#       MigrationTests.Applies101_OnEmptyDatabase ; KeyRingTests.KeysSurviveContextRecreation
#  S03  TenantIsolationTests.A_CannotRead_B_ById | _List | Write_WithForeignTenant_Throws_TenantMismatch
#       | NullContext_Throws_TenantContextMissing | SoftDeleteLift_StillHidesOtherTenant | PooledConnection_A_B_A_NoLeak
#  S11  JournalOrderingTests.SequenceOrder_EqualsCommitOrder_UnderConcurrency | RollbackConsumesNoSequence_NoGap
#       | ChildOnlyChange_CapturesRoot | Delete_CapturesTombstoneKey | DirectGovernedSql_Captured
#       | TwoTenants_IndependentCounters | EpochAdvance_FencesOldRows  (+ JournalCoverageTests.EveryReplicatedTable_HasCaptureTrigger)
#  S12  BatchFreezerTests.WatermarkAndRows_FromOneSnapshot | SnapshotCommitted_BeforePageMaterialization
#       | SameRange_SameBatchId_ByteStable | SnapshotOverMaxLifetime_AbortsAndRetries
#       GcTests.JournalDeletion_AdvancesFloor_SameTransaction | CrashAfterFileDelete_BeforeRowDelete_Recovers
#       SyncIsolationTests.TenantA_Batch_ContainsNoTenantB_Rows ; SyncFlowTests.OpenDownloadResume_FromLastPage
#  S18  SnapshotAgeMonitorTests.OldSnapshot_ReportsDegraded ; QueryBudgetPgTests.StatementTimeout_Enforced

# Runtime role rules (STACK §12 RLS gate): the CI app role is NOT the DB owner, NOT superuser, has NO BYPASSRLS;
# "FORCE RLS" is set on tenant tables only in the optional RLS lane. Mandatory app isolation must pass with RLS OFF.

# Restore drill consistency boundary (ARCH §17.3): DB + files + Data-Protection key ring = ONE point-in-time snapshot;
# key-ring-missing ⇒ host serves liveness but REFUSES readiness/auth/business (ARCH §10.6, A-RULE 027).

# Gate runners already in the tree: scripts/sandbox/test.sh (filtered test entrypoint), scripts/sandbox/doctor.sh (env probe),
#   scripts/sandbox/_env.sh (shared env). G07 adds scripts/ci/pg-gate.sh + scripts/ci/restore-drill.sh + a GitHub workflow.
```

## §Design decisions

- **CI workflow `.github/workflows/pg-gates.yml` (new).** Job `pg-correctness`: `services: postgres` = `postgres:18` with a
  non-privileged app role created by an init step (`CREATE ROLE animora_app LOGIN NOSUPERUSER NOBYPASSRLS; GRANT …` — owner is a
  separate `animora_owner` that runs migrations; the app role gets only table DML + the journal functions). Steps: checkout →
  `scripts/sandbox/install-prereqs.sh` → apply server migrations as owner (`dotnet-ef database update -c AnimoraServerDbContext`,
  tool at `.sandbox/tools`) → export `ANIMORA_REQUIRE_PG=1` + the app-role connection string → `scripts/ci/pg-gate.sh` (below). The
  job **fails** if any promoted PG test is skipped (because `ANIMORA_REQUIRE_PG=1` turns the skip into a failure) or red. Native
  SQLite ≥ 3.50.2 is asserted by the existing startup probe reused in a `NativeSqliteVersionGateTests` run in the same job (STACK §12).
- **`scripts/ci/pg-gate.sh` (new).** Thin wrapper: runs `scripts/sandbox/test.sh Server` + `scripts/sandbox/test.sh Sync` +
  `scripts/sandbox/test.sh Persistence` with `ANIMORA_REQUIRE_PG=1` already in the environment; sets `set -euo pipefail` so a
  non-zero test exit fails the job; prints a one-line `✓ PG gate: <n> tests, 0 skipped` summary and exits non-zero on any skip. A
  companion assertion `PgGateManifestTests.EveryPgGatedTest_IsInTheRequiredList` (new, in `tests/Animora.Server.Tests`) greps the
  server/sync test assemblies for the `[PgGated]` trait and asserts the set **equals** the Reference-pack list (so a future PG test
  can't silently escape the mandatory lane).
- **Tenancy isolation gate = two lanes.** (1) **Mandatory, RLS OFF** — the S03 `TenantIsolationTests.*` run against the service
  container with the app role and **no** RLS policies enabled; these prove the three application isolation layers (write-path tenant
  stamping, the global `"Tenant"` query filter, the cross-tenant write guard) hold with hardening off — this is the R-B19 gate
  (ARCH §9.2, gate **L**). `NullContext_Throws_TenantContextMissing` proves a null context throws, and a new
  `TenantIsolationTests.ClientSuppliedTenantId_Ignored_AndAudited` proves a client-supplied `TenantId` is ignored and audited
  (A-RULE 012/013). (2) **Optional, RLS ON** — job `pg-rls-hardening` (allowed-to-fail is **not** permitted: it must pass or RLS
  stays off) enables the S03 `RlsTenantSessionInitializer` policies and runs `RlsLeakTests.A_B_A_PoolReuse_NoLeak`,
  `RlsLeakTests.MissingContext_Denies`, `RlsLeakTests.NonOwnerNoBypass_CannotSeeAll`, `RlsLeakTests.RollbackCancellation_NoLeak`
  (STACK §12 RLS PoC). RLS stays **deferred as a hardening switch** (ROADMAP §5); this lane only proves it is *safe to enable*, it
  does not enable it in product.
- **Key-ring + restore gate (`scripts/ci/restore-drill.sh` + `RestoreDrillTests`).** The script, in CI, (a) provisions a tenant +
  a known row + a stored file, (b) takes the **single point-in-time** snapshot of DB + file tree + the Data-Protection key-ring
  DbContext (ARCH §17.3 one consistency boundary), (c) restores all three onto a **clean** container, (d) asserts the server reaches
  **readiness** and the known tenant/row/file are intact, (e) records elapsed restore seconds to `artifacts/restore-rto.txt` (RTO is
  *measured*, never asserted-as-guarantee), and (f) runs the key-ring-missing variant and asserts the server serves **liveness** but
  returns **503 on readiness/auth/business** (gate **K**, ARCH §10.6, A-RULE 027). The test `RestoreDrillTests.Restore_ToReadiness_KnownTenantIntact`
  and `RestoreDrillTests.MissingKeyRing_RefusesReadiness_ServesLiveness` wrap the same logic for the suite.
- **No product-code change.** Every gate consumes code S02–S18/G01–G06 already shipped. If a gate goes red because of a real defect,
  the fix belongs to the **owning** phase (file a deviation in PROGRESS and open the fix under that phase's ID); G07's job is to make
  the gate *run and be mandatory*, not to patch `src/**`.
- **New ErrorCodes:** none (gates assert existing codes/behaviors).
- **Local developer ergonomics.** Without PostgreSQL, `scripts/sandbox/test.sh` keeps skipping the PG tests with a printed reason
  (CONVENTIONS §1) — the gate only bites in the CI lane where `ANIMORA_REQUIRE_PG=1` is set; `scripts/sandbox/doctor.sh` is extended
  to print whether a reachable PostgreSQL + `ANIMORA_REQUIRE_PG` are present so a developer can reproduce the lane locally.

## §Implementation plan

1. `.github/workflows/pg-gates.yml` — `postgres:18` service, non-owner app role init, owner-run migrations, `ANIMORA_REQUIRE_PG=1`,
   jobs `pg-correctness`, `pg-rls-hardening`, `restore-drill`.
2. `scripts/ci/pg-gate.sh` — `set -euo pipefail`; runs the Server/Sync/Persistence suites; fails on any skip; prints the summary.
3. `scripts/ci/restore-drill.sh` — snapshot → restore → readiness + known-tenant assert → RTO artifact → key-ring-missing variant.
4. `tests/Animora.Server.Tests/PgGateManifestTests.cs` (new) — `[PgGated]`-trait set equals the Reference-pack list.
5. `tests/Animora.Server.Tests/RestoreDrillTests.cs` (new) — wraps the drill for the suite; `tests/...TenantIsolationTests` gets the
   `ClientSuppliedTenantId_Ignored_AndAudited` case; `RlsLeakTests` (new, `[PgGated]`, RLS-on lane) added beside S03's isolation tests.
6. `scripts/sandbox/doctor.sh` (edit) — report reachable PostgreSQL + `ANIMORA_REQUIRE_PG` presence.
7. Verify locally: `scripts/sandbox/build.sh full` → 0/0, `scripts/sandbox/run.sh` → `SMOKE RUN OK` (desktop unaffected). The PG lane
   itself runs in CI (documented how to run it locally with a container).
8. `PROGRESS.md` per recipe; commit via `scripts/git/push.sh`.

## Files to create / modify

| Path | Contents |
|---|---|
| `.github/workflows/pg-gates.yml` (new) | PostgreSQL 18 service; non-owner app role; `ANIMORA_REQUIRE_PG=1`; jobs correctness / rls-hardening / restore-drill |
| `scripts/ci/pg-gate.sh` (new) | runs Server/Sync/Persistence suites, fails on any skip, prints summary |
| `scripts/ci/restore-drill.sh` (new) | DB+files+key-ring snapshot→restore→readiness+known-tenant→RTO artifact→key-ring-missing variant |
| `tests/Animora.Server.Tests/PgGateManifestTests.cs` (new) | `[PgGated]` set equals the mandatory list |
| `tests/Animora.Server.Tests/RestoreDrillTests.cs` (new) | restore-to-readiness + missing-key-ring-refuses-readiness |
| `tests/Animora.Server.Tests/RlsLeakTests.cs` (new, `[PgGated]`) | RLS-on A→B→A / missing-context / non-owner / rollback leak proofs |
| `tests/Animora.Server.Tests/*TenantIsolation*` (edit) | add `ClientSuppliedTenantId_Ignored_AndAudited` |
| `scripts/sandbox/doctor.sh` (edit) | report PostgreSQL reachability + `ANIMORA_REQUIRE_PG` |

## Contracts / schema added

No wire DTO, no migration, no runtime type. New CI/test artifacts only: one workflow, two CI scripts, three/four test classes, the
`[PgGated]` trait manifest, and the `artifacts/restore-rto.txt` RTO record. No `WireContract.Version` change.

## Rules that apply here

- **Isolation with hardening OFF (ARCH §9.2, R-B19, gate L):** the mandatory lane proves the three application isolation layers hold
  with RLS off; a null tenant context throws; a client-supplied `TenantId` is ignored and audited — all under a non-owner app role.
- **RLS stays a gated hardening switch (STACK §12, ROADMAP §5):** the RLS-on lane proves A→B→A pool reuse / missing-context /
  non-`BYPASSRLS` never leak, but RLS is **not** enabled in product by this phase; it only becomes *provably safe to enable*.
- **Key-ring recovery (ARCH §10.6, A-RULE 027, gate K):** a missing/unreachable key ring refuses readiness/auth/business while still
  serving liveness — proven, not asserted.
- **Rehearsed restore (ARCH §17.3, R-B33 preview, gate R):** DB + files + key ring are one point-in-time boundary; the drill restores
  to readiness + a known tenant and **measures** RTO (the full release runbook is G09; G07 proves the *mechanism*).
- **Zero-Internet at runtime (A-RULE 016/032):** the gate needs a server + PostgreSQL to *run*; the product does not need either to
  *run offline* — the desktop smoke still passes with no server.

## Out of scope (owning phase)

- Weak-server/weak-client **performance proof**, the offline-budget gate, the mixed-fleet (M) gate, Z (egress-blocked) and T
  (measured capacity) gates, the layering audit → **G08**.
- The full install/upgrade/rollback runbook, PITR/WAL archiving configuration, off-host durability automation, SBOM + native-binary
  inventory → **G09** (G07 proves the restore *mechanism*; G09 operationalises cadence/RPO/off-host/SBOM).
- Enabling RLS in product, cross-tenant analytics → deferred (ROADMAP §5).
- Any `src/**` fix a gate exposes → the **owning** S/G phase (G07 only makes the gate mandatory).

## Acceptance criteria (mechanical)

1. `scripts/sandbox/build.sh full` = 0 warnings / 0 errors; `scripts/sandbox/run.sh` = `SMOKE RUN OK` (desktop unaffected, offline).
2. CI job `pg-correctness` (workflow `pg-gates.yml`) runs with a reachable PostgreSQL 18 + `ANIMORA_REQUIRE_PG=1` and is **green**
   with **0 skipped** across: `JournalOrderingTests.SequenceOrder_EqualsCommitOrder_UnderConcurrency` (+ the other 6 `JournalOrderingTests`),
   `JournalCoverageTests.EveryReplicatedTable_HasCaptureTrigger`, `BatchFreezerTests.*` (4), `GcTests.*` (2),
   `SyncIsolationTests.TenantA_Batch_ContainsNoTenantB_Rows`, `SyncFlowTests.OpenDownloadResume_FromLastPage`,
   `VersionTriggerTests.Version_ChangesOnEveryUpdate_IncludingRawSql`, `MigrationTests.Applies101_OnEmptyDatabase`,
   `KeyRingTests.KeysSurviveContextRecreation`, `SnapshotAgeMonitorTests.OldSnapshot_ReportsDegraded`,
   `QueryBudgetPgTests.StatementTimeout_Enforced`, `NativeSqliteVersionGateTests.RuntimeProbe_AtLeast_3_50_2`.
3. `scripts/ci/pg-gate.sh` exits non-zero if **any** promoted PG test is skipped (prove by running it once with
   `ANIMORA_REQUIRE_PG=1` unset in a check step → it reports the skip and the `pg-correctness` job fails); `PgGateManifestTests.EveryPgGatedTest_IsInTheRequiredList` green.
4. Job `pg-rls-hardening` green with the app role **non-owner, non-superuser, no `BYPASSRLS`**: `RlsLeakTests.A_B_A_PoolReuse_NoLeak`,
   `RlsLeakTests.MissingContext_Denies`, `RlsLeakTests.NonOwnerNoBypass_CannotSeeAll`, `RlsLeakTests.RollbackCancellation_NoLeak`;
   the mandatory RLS-**off** lane still green for all `TenantIsolationTests.*` + `TenantIsolationTests.ClientSuppliedTenantId_Ignored_AndAudited`.
5. Job `restore-drill` green: `RestoreDrillTests.Restore_ToReadiness_KnownTenantIntact` and
   `RestoreDrillTests.MissingKeyRing_RefusesReadiness_ServesLiveness`; `artifacts/restore-rto.txt` is produced with a numeric
   elapsed-seconds value.
6. `grep -rn "ANIMORA_REQUIRE_PG" .github/workflows/pg-gates.yml scripts/ci/pg-gate.sh` ≥ 1; no change under `src/**`
   (`git diff --name-only <base>..HEAD -- src/` is empty for this phase).

## PROGRESS.md update

At the end of this phase: set the `G07` row in "Phase status" to `✅ Done` (add date + one-line summary), advance
**"Next phase" → `G08`**, append one `### G07` entry to "Repo state log" (the `pg-gates.yml` workflow, `pg-gate.sh` +
`restore-drill.sh`, the promoted PG-gated test set now mandatory, the RLS-hardening lane, the restore+key-ring drill, RTO artifact),
log any deviation (and any `src/**` defect a gate exposed, filed under its owning phase), then commit with
`scripts/git/push.sh -b genspark_ai_developer "ci(gates): G07 mandatory PostgreSQL CI, tenancy/RLS & restore gates"`.
