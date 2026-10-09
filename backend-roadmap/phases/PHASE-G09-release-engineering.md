# Phase G09 — Release engineering (topology, install/upgrade/rollback runbook, backup/restore rehearsal, packaging, SBOM)

**Band:** M · **Track:** G (sync / integration / release) · **Depends on:** G08 · **Next:** — (closes the regenerated server track; M00 T9 then hands off to S01)
**Satisfies:** R-B27, R-B31, R-B33 · **Runnable after this phase:** desktop offline yes · server required no
**Produced by:** M00 (regenerated 2026-10-09; supersedes the deleted `PHASE-E09` file).

> Baseline verified 2026-10-09: server migration head = last server ordinal (read `ServerSchemaVersion.ExpectedHead` — never hard-code), client migration head = last client ordinal (list `src/Animora.Persistence.Sqlite/Migrations/`), `CommandTypes` = live `CommandTypes.cs` (88 today — never freeze), `ErrorCodes` = live count, `WireContract.Version` = 2 (supported {1,2}); all server + client sync projects exist (S01–S20, G01–G08) and the CI gate workflows `pg-gates.yml` (G07) + `perf-gates.yml` (G08) are present.
> **Next-free-ordinal rule.** This phase adds **no migration** — like G07/G08 it is a **release-engineering** phase: it writes release runbooks, packaging scripts, SBOM/native-inventory generation and a release-gate aggregator under `docs/release/**`, `scripts/release/**`, `.github/**`, `tests/**` only — **never** a schema change, a runtime `src/**` change, a package pin or a `.sln` edit. A gate that exposes a real defect is fixed by the **owning** S/G phase, not here.

---

## ⚡ Zero-exploration contract (read this, then implement top-to-bottom)

> This file is self-contained and pre-verified. The **mechanisms** G09 operationalises already exist: the rehearsed restore drill
> (`scripts/ci/restore-drill.sh` + `RestoreDrillTests`, G07) proves the restore *mechanism*; the mixed-fleet / zero-Internet gates
> (`perf-gates.yml`, G08) prove N/N-1/N-2 determinism and egress-blocked runtime; the desktop packager (`scripts/windows/publish-portable.ps1`)
> already produces a zero-install portable build. G09 turns these into **release obligations**: a deployment-topology + install/upgrade/
> rollback runbook, a backup/restore runbook with a **named RPO/RTO + off-host durability rule**, the packaging path extended to emit an
> **SBOM + native-binary inventory**, and a single **launch-gate aggregator** workflow that fails the release if any ARCH §19 gate is red.
> Every acceptance criterion is a **script exit code / named test / generated artifact**, never prose. Do not re-open the repo to
> "understand" it; if a snippet diverges, fix the smallest thing and log it. Implement exactly this scope: nothing from G07 (PG CI/RLS)
> or G08 (perf/offline budget) beyond *consuming* their gate outputs in the aggregator.

**Environment (do not probe):**
- Build / test only through the sandbox scripts (memory-capped ~985 MiB; never raw `dotnet build`):
  `scripts/sandbox/build.sh [fast|full]`, `scripts/sandbox/test.sh [filter]`, `scripts/sandbox/run.sh` (→ `SMOKE RUN OK`).
- `dotnet-ef` at `.sandbox/tools/dotnet-ef` — not needed here (no migration).
- **Packaging:** the Windows portable packager is `scripts/windows/publish-portable.ps1` (self-contained single-file win-x64/win-arm64,
  bundles the .NET runtime + Avalonia + native SQLCipher/`e_sqlite3mc`). G09 **extends** it (SBOM + native inventory emit) and adds the
  cross-platform release manifest generator — it does **not** rewrite the publish logic.
- **Forbidden tech (restated):** Redis, message broker, cloud IdP, cloud telemetry, CDN, EF Core 11, per-tenant database, hard-coded TLS
  version, Native AOT with EF Core, object-storage SDK. No external package manager / cloud artifact store is a *runtime* dependency; the
  SBOM tooling runs at **build/release time only** (never a runtime path — A-RULE 016/032).

**Offline invariant.** G09 adds **no runtime code** and no client dependency — the desktop still builds, starts and runs fully offline
exactly as after G08 (`scripts/sandbox/run.sh` → `SMOKE RUN OK`). Release engineering is a **build/release-time obligation**: deployment
topology, backup cadence, SBOM generation and the launch-gate aggregator all run in CI or on the operator's machine, never on the product's
runtime path. The **zero-Internet gate (Z, G08)** is *promoted here into the launch runbook* as a release blocker — a release cannot ship
if built output references any `cdn…` / `fonts.googleapis…` / external endpoint (ARCH §17.4, R-B27).

**Condensed CONVENTIONS rules used here:** §1 (RTO/capacity numbers are **measured and recorded**, never asserted — the runbook cites the
G07 `restore-rto.txt` + G08 capacity artifact, A-RULE 035/038); §2 layering (release glue lives in `docs/**`, `scripts/release/**`,
`.github/**`, `tests/**` — no `src/**` change); §17 single-operator discipline (one operator must update, back up, restore and relocate the
server without surprises — the runbooks are written to that bar).

---

## Read before starting
- `backend-roadmap/PROGRESS.md` — top section only.
- `ARCHITECTURE.md` §17 (deployment, operability & recovery: §17.1 topology + migration discipline — migrations run before traffic under a
  DDL role, runtime role has no DDL rights, `EnsureCreated` forbidden, server migrations numbered from 101 never edited later; §17.2
  update/upgrade/rollback — single process, stop-migrate-start or blue/green, one-step-back rollback never strands clients because
  migrations are additive + wire is versioned; §17.3 the disaster-recovery matrix — RPO/RTO/off-host/promotion + the **three-part single
  consistency boundary** DB+files+key-ring + SBOM/native-inventory ship with each release; §17.4 zero mandatory external-Internet at
  runtime), §18 (wire-contract versioning & mixed fleets — N/N-1/N-2 deterministic, `WireContract.Version` bumped only by a wire-shape
  phase, additive-only within a major), §19 (the gate table — G09 **aggregates** all gates into the launch bar; it owns no new gate but is
  the phase that declares a release ships only when A/D/C/N/B/L/P/O/K/U/F/S/I/M/R/Z/T/PG are green) — only these `§`.
- `STACK.md` §0 (TL;DR topology: single ASP.NET Core process + PostgreSQL 18 + filesystem tree, Avalonia Native desktop, Data-Protection
  key material), §16 (files & storage packaging + the **native binary inventory** supply-chain decision — a NuGet SBOM alone is
  insufficient; the release manifest records every native artifact `DLL · DYLIB · SQLite · SQLCipher/e_sqlite3mc · OS-native deps` with
  version + RID), and the §11-vicinity supply-chain governance block (SBOM + dependency pin + `NuGetAuditMode=all` + lock-file mode
  mandatory; any vulnerability suppression records advisory ID · justification · affected package · compensating control · expiry · owner;
  **no permanent suppression for native DB/crypto**) — only these `§`.
- The already-shipped mechanisms G09 operationalises (**do not re-author**): `scripts/windows/publish-portable.ps1` (packaging entry
  point), `scripts/ci/restore-drill.sh` + `RestoreDrillTests` + `artifacts/restore-rto.txt` (G07 restore mechanism + measured RTO),
  `.github/workflows/pg-gates.yml` (G07) and `.github/workflows/perf-gates.yml` (G08) (the gate lanes the launch aggregator consumes),
  `WireContract.IsSupported(int)` + `{1,2}` (the mixed-fleet N/N-1/N-2 seam the upgrade/rollback runbook cites).
- The `R-B27`, `R-B31`, `R-B33` rows of `backend-roadmap/00-REQUIREMENTS-LOCK.md`.
- (Nothing else. `src/**`, `Animora-UI/**`, `Database-Sampel/**` are out of bounds — this is a release-engineering phase.)

## Goal

Make "ready to release" an **earned, mechanical** state, not a claim (R-B33). Produce the three operator runbooks the single-operator bar
demands (ARCH §17), the packaging path that emits a release manifest with an **SBOM + native-binary inventory** (STACK §16), and a single
**launch-gate aggregator** that blocks a release unless every ARCH §19 gate is green:

1. **Deployment topology & install runbook** (ARCH §17.1) — the one-host shape (single ASP.NET Core process + PostgreSQL 18 + the `DataRootPath`
   filesystem tree + the Data-Protection key ring), the **migration discipline** (an owner/DDL role runs `dotnet-ef database update` *before*
   traffic; the runtime app role has **no DDL rights**; `EnsureCreated` is forbidden outside test fixtures; server migrations are numbered from
   101 and never edited by a later phase), and the first-install checklist (provision roles, run migrations as owner, seed the key ring, verify
   readiness).
2. **Update / upgrade / rollback runbook** (ARCH §17.2, §18) — ship a new version as the **same single process**; deploy via **stop-migrate-start**
   (or blue/green where the host allows); because migrations are **additive** and the wire contract is **versioned (N/N-1/N-2)**, a **one-step-back
   rollback** restores the previous binary and does **not** strand clients; the runbook states the explicit rollback boundary (how far back a
   binary may roll without a schema divergence) and cites `WireContract.IsSupported` as the mixed-fleet guarantee during a rolling upgrade.
3. **Backup / restore / host-move runbook** (ARCH §17.3) — the **single backup set is three mandatory parts captured as one point-in-time
   consistency boundary** (PostgreSQL DB, the `§14` file-content tree, the Data-Protection **key ring** — forgetting the key ring is the classic
   silent disaster), with a **named RPO** (backup cadence: PITR/WAL archiving for a small RPO, scheduled logical dumps otherwise — a measured
   number, never "best effort"), a **measured RTO** (cited from the G07 `restore-rto.txt` drill artifact — the drill *proves* it), an **off-host
   durability rule** (≥1 copy on different hardware from the live host; a co-located backup does **not** count), and the **host-move/promotion
   path** (same restore + repoint config; the stable `ApplicationName` keeps the key ring valid after the move) plus the **named scale-out trigger**
   (the §16.2 switch at which single-host is no longer acceptable — stated so the decision is explicit, not drift).
4. **Packaging + SBOM + native-binary inventory** (STACK §16, ARCH §17.3) — extend the packaging path so every release emits a **release manifest**
   carrying a **NuGet SBOM** (dependency pins + `NuGetAuditMode=all` + lock-file mode) **and** a **native-binary inventory** (`DLL · DYLIB · SQLite ·
   SQLCipher/e_sqlite3mc · OS-native deps` with version + RID — because a NuGet SBOM alone does not cover the native crypto/DB layer), plus a
   **vulnerability-suppression ledger** schema (advisory ID · justification · affected package · compensating control · **expiry** · owner; **no
   permanent suppression for native DB/crypto**).
5. **Launch-gate aggregator** (ARCH §19) — a single workflow / script that **fails the release** unless all gate lanes are green: it consumes the
   G07 `pg-gates.yml` result (A/D/C/N/B/L/K/R/PG + tenancy/RLS), the G08 `perf-gates.yml` result (I/M/Z/T + offline-budget + layering audit), the
   desktop smoke (`run.sh` → `SMOKE RUN OK`), and the SBOM/native-inventory + zero-Internet built-output scan — and records a dated **launch-gate
   report** artifact. No "production-ready"/"10/10" label is *declared*; it is the aggregator's green exit that *earns* it.

Every acceptance criterion is a named test, a CI job exit code, or a generated artifact — no prose gate.

---

## §Reference pack (verbatim from the authoring sources, 2026-10-09)

```text
# ARCH §17.1 migration discipline (invariant): migrations run BEFORE traffic under a DDL role; runtime startup only checks
#   schema version/readiness — the running app's role has NO DDL rights. EnsureCreated forbidden outside test fixtures.
#   Server migrations numbered from 101, NEVER edited by a later phase; key-ring context has its own migration sequence (§10.6).

# ARCH §17.2 update/rollback: new version = same single process; deploy = stop-migrate-start OR blue/green (admin web + wire
#   tolerate N/N-1/N-2, §8.6). Rollback restores the previous binary; migrations additive + wire versioned => one-step-back
#   rollback does NOT strand clients. Desktop updates verify against the same trust chain (signed/notarized where OS requires).

# ARCH §17.3 disaster-recovery matrix (invariant): single-host is a deliberate choice with a STATED recovery target (A-RULE 038).
#   RPO = max tolerable data loss, set by backup cadence (PITR/WAL for small RPO; scheduled logical dumps otherwise) — a measured,
#     documented number, NEVER "best effort".
#   RTO = max time to readiness on a clean host, proven by the gate-R drill (§19) — the drill records elapsed restore time, measured.
#   Off-host durability = >=1 backup copy on DIFFERENT hardware from the live host; a co-located backup does NOT count.
#   Promotion/failover = host move is the documented path (same restore + repoint config); the spec NAMES the scale-out trigger (§16.2).
#   The single backup set is THREE mandatory things, ONE consistency boundary (not three independent snapshots):
#     1) PostgreSQL DB (logical or physical/PITR), 2) the file-content tree (§14), 3) the Data-Protection key ring (§10.6).
#   Restore is rehearsed, not assumed (§19 gate R). Host move = same op + repoint config; stable ApplicationName keeps key ring valid.
#   SBOM + a native binary inventory ship with EACH release (supply chain; STACK §16).

# ARCH §17.4 zero mandatory external-Internet at runtime (invariant, R-B27): server runs fully with the Internet cable pulled.
#   No runtime path may require the public Internet. All web assets packaged — NO cdn… / fonts.googleapis… in built output
#   (verified by an isolation test — the G08 gate Z; G09 makes it a RELEASE BLOCKER).

# ARCH §18 wire versioning & mixed fleets (R-B31): WireContract.Version bumped ONLY by a wire-shape phase; additive-only within a
#   major; server accepts N/N-1/N-2; unsupported version => controlled resync/upgrade, never a silent mis-map; the contract-version
#   header travels on every request. The upgrade/rollback runbook cites WireContract.IsSupported as the rolling-upgrade guarantee.

# STACK §16 native binary inventory (decision): a NuGet SBOM alone is insufficient. The release manifest MUST also record every
#   native artifact — DLL · DYLIB · SQLite · SQLCipher/e_sqlite3mc · OS-native deps — with its version and RID (§3.3 native/RID matrix).
# STACK §11-vicinity supply-chain governance (decision): SBOM + dependency pin + NuGetAuditMode=all + lock-file mode are mandatory,
#   PLUS the native inventory. Any suppression records: advisory ID · justification · affected package · compensating control ·
#   expiry date · owner. NO permanent suppression for native DB/crypto (SQLCipher / e_sqlite3 / e_sqlite3mc) — time-boxed & re-reviewed.

# Mechanisms already in the tree (consumed, NOT re-authored):
#   scripts/windows/publish-portable.ps1  — zero-install portable packager (win-x64/win-arm64; bundles runtime+Avalonia+SQLCipher native)
#   scripts/ci/restore-drill.sh + RestoreDrillTests + artifacts/restore-rto.txt  — G07 restore mechanism + MEASURED RTO
#   .github/workflows/pg-gates.yml   — G07 gate lanes (pg-correctness / pg-rls-hardening / restore-drill)
#   .github/workflows/perf-gates.yml — G08 gate lanes (offline-budget / server-capacity / sync-behavioral / zero-internet / layering-audit)
#   scripts/sandbox/run.sh → "SMOKE RUN OK"  — the desktop offline smoke the launch aggregator requires green
```

## §Design decisions

- **Three operator runbooks under `docs/release/` (new, documentation).** `01-deployment-topology.md` (ARCH §17.1 one-host shape + the
  migration-discipline first-install checklist — owner/DDL role runs `dotnet-ef database update` before traffic, runtime role has no DDL
  rights, `EnsureCreated` forbidden, migrations from 101 never edited), `02-upgrade-rollback.md` (ARCH §17.2/§18 stop-migrate-start |
  blue/green, the additive-migration + versioned-wire guarantee, the explicit one-step-back rollback boundary, `WireContract.IsSupported`
  as the mixed-fleet guard during a rolling upgrade), `03-backup-restore-hostmove.md` (ARCH §17.3 three-part single consistency boundary,
  the **named RPO cadence table**, the **measured RTO** cited from `artifacts/restore-rto.txt`, the **off-host durability rule**, the
  host-move/promotion path + the **named scale-out trigger**). Each runbook is written to the **single-operator** bar and ends with a copy-
  pasteable command checklist. These are *documentation*, but every operational claim they make is backed by a mechanical gate cited inline.
- **Release manifest + SBOM + native inventory (`scripts/release/make-release-manifest.sh` + a packaging hook).** A new release script
  generates, for a built output, (a) a **NuGet SBOM** via the first-party tooling (`dotnet` SBOM / CycloneDX task — no cloud call), asserting
  `NuGetAuditMode=all` + lock-file mode are on, (b) a **native-binary inventory** JSON listing every native artifact shipped (`*.dll`,
  `*.dylib`, `*.so`, `e_sqlite3`/`e_sqlite3mc`/SQLCipher) with version + RID, and (c) a **suppression-ledger** file whose schema requires
  `advisoryId, justification, affectedPackage, compensatingControl, expiry, owner` and **rejects** any native-DB/crypto entry without an
  `expiry` (no permanent suppression). `scripts/windows/publish-portable.ps1` is **extended** (not rewritten) with a `-EmitManifest` switch
  that calls the generator against the portable output so the SBOM + native inventory ship **with the package** (ARCH §17.3 "ship with each
  release"). A cross-platform equivalent (`make-release-manifest.sh`) runs in CI for the server tree.
- **Launch-gate aggregator `.github/workflows/release-gates.yml` (new) + `scripts/release/launch-gate.sh` (new).** The aggregator workflow
  depends on the G07 `pg-gates.yml` and G08 `perf-gates.yml` workflows (via `workflow_run` / reusable-workflow `needs`), plus a job that runs
  the desktop smoke (`scripts/sandbox/run.sh` → `SMOKE RUN OK`) and a job that runs `scripts/release/launch-gate.sh`. The launch-gate script
  (`set -euo pipefail`) collects each lane's conclusion + the SBOM/native-inventory presence + a **zero-Internet built-output scan** (grep the
  packaged assets for `cdn`/`fonts.googleapis`/external endpoints — the release-blocker promotion of G08 gate Z) and **exits non-zero** unless
  **every** ARCH §19 gate lane is green; on success it writes a dated `artifacts/launch-gate-report.md` enumerating each gate → PASS with its
  evidence pointer. A companion `LaunchGateManifestTests.EveryArch19Gate_IsCoveredByALane` (new, in `tests/Animora.Server.Tests`) asserts the
  aggregator's gate list **equals** the ARCH §19 gate table (so a future gate can't silently escape the launch bar).
- **No product-code change.** Every runbook, manifest and aggregator consumes mechanisms S01–S20/G01–G08 already shipped. If the launch-gate
  aggregator goes red because of a real defect, the fix belongs to the **owning** phase (file a deviation in PROGRESS and open the fix under
  that phase's ID); G09's job is to make "release" gated and the supply-chain artifacts shipped, not to patch `src/**`.
- **New ErrorCodes:** none (release engineering touches no runtime wire/command surface).
- **Local developer ergonomics.** `scripts/release/launch-gate.sh --dry-run` prints which lanes it would require and which artifacts it
  expects, so an operator can see the launch bar without a full CI run; `scripts/sandbox/doctor.sh` is extended to report whether the two gate
  workflows + the packaging script are present.

## §Implementation plan

1. `docs/release/01-deployment-topology.md`, `docs/release/02-upgrade-rollback.md`, `docs/release/03-backup-restore-hostmove.md` — the three
   single-operator runbooks (ARCH §17.1 / §17.2+§18 / §17.3), each ending with a copy-pasteable command checklist.
2. `scripts/release/make-release-manifest.sh` (new) — generate SBOM (NuGet/CycloneDX, no cloud) + native-binary inventory JSON (version + RID)
   + suppression-ledger schema (rejects native-DB/crypto entries without `expiry`).
3. `scripts/windows/publish-portable.ps1` (edit) — add `-EmitManifest` that calls the generator against the portable output so SBOM + native
   inventory ship with the package (no rewrite of the publish logic).
4. `scripts/release/launch-gate.sh` (new) — `set -euo pipefail`; collect pg-gates + perf-gates conclusions + desktop smoke + SBOM/native-
   inventory presence + zero-Internet built-output scan; fail unless every ARCH §19 lane is green; emit `artifacts/launch-gate-report.md`.
5. `.github/workflows/release-gates.yml` (new) — `needs`/`workflow_run` on `pg-gates.yml` + `perf-gates.yml`; a smoke job; a `launch-gate` job
   running `scripts/release/launch-gate.sh`; archive the launch-gate report.
6. `tests/Animora.Server.Tests/LaunchGateManifestTests.cs` (new) — the aggregator's gate list equals the ARCH §19 gate table.
7. `scripts/sandbox/doctor.sh` (edit) — report presence of `pg-gates.yml`, `perf-gates.yml`, `publish-portable.ps1`.
8. Verify locally: `scripts/sandbox/build.sh full` → 0/0, `scripts/sandbox/run.sh` → `SMOKE RUN OK` (desktop unaffected, offline); run
   `scripts/release/launch-gate.sh --dry-run` → lists every ARCH §19 lane. The full aggregator runs in CI.
9. `PROGRESS.md` per recipe; commit via `scripts/git/push.sh`.

## Files to create / modify

| Path | Contents |
|---|---|
| `docs/release/01-deployment-topology.md` (new) | one-host topology + migration-discipline first-install checklist (ARCH §17.1) |
| `docs/release/02-upgrade-rollback.md` (new) | stop-migrate-start / blue-green; additive-migration + versioned-wire guarantee; one-step-back rollback boundary; `WireContract.IsSupported` (ARCH §17.2, §18) |
| `docs/release/03-backup-restore-hostmove.md` (new) | three-part single consistency boundary; named RPO cadence; measured RTO (cites `restore-rto.txt`); off-host durability; host-move + scale-out trigger (ARCH §17.3) |
| `scripts/release/make-release-manifest.sh` (new) | SBOM (NuGet/CycloneDX, no cloud) + native-binary inventory (version+RID) + suppression-ledger schema (no permanent native-DB/crypto suppression) |
| `scripts/windows/publish-portable.ps1` (edit) | add `-EmitManifest` to call the generator against the portable output |
| `scripts/release/launch-gate.sh` (new) | aggregate pg-gates + perf-gates + smoke + SBOM/native-inventory + zero-Internet scan; fail unless all ARCH §19 lanes green; emit launch-gate report |
| `.github/workflows/release-gates.yml` (new) | `needs`/`workflow_run` on the two gate workflows + smoke + launch-gate job; archive report |
| `tests/Animora.Server.Tests/LaunchGateManifestTests.cs` (new) | aggregator gate list equals ARCH §19 gate table |
| `scripts/sandbox/doctor.sh` (edit) | report the two gate workflows + packaging script presence |

## Contracts / schema added

No wire DTO, no migration, no runtime type, no ErrorCode. New release/test artifacts only: three runbooks, two release scripts, one CI
workflow, one test class, plus the generated `artifacts/launch-gate-report.md`, the SBOM, the native-binary-inventory JSON and the
suppression-ledger. No `WireContract.Version` change.

## Rules that apply here

- **Migration discipline before traffic (ARCH §17.1, invariant):** the deployment runbook mandates an owner/DDL role runs migrations
  **before** traffic; the runtime app role has **no DDL rights**; `EnsureCreated` is forbidden; server migrations are numbered from 101 and
  never edited by a later phase.
- **One-step-back rollback never strands clients (ARCH §17.2/§18, R-B31):** because migrations are additive and the wire contract is
  versioned (N/N-1/N-2 via `WireContract.IsSupported`), a rollback to the previous binary keeps mixed fleets deterministic — the runbook
  states the exact rollback boundary.
- **Three-part single consistency boundary + measured RPO/RTO + off-host (ARCH §17.3, A-RULE 038, R-B33):** DB + files + key ring are one
  point-in-time set; RPO is a documented cadence number (never "best effort"); RTO is **measured** by the G07 drill (`restore-rto.txt`);
  ≥1 backup copy lives on different hardware; the host-move path and the named scale-out trigger are explicit.
- **SBOM + native-binary inventory ship with each release (STACK §16, R-B33):** a NuGet SBOM alone is insufficient; the manifest records
  every native artifact with version + RID; suppressions are time-boxed with a full ledger and **never permanent** for native DB/crypto.
- **Zero-Internet is a release blocker (ARCH §17.4, R-B27):** the launch-gate scan fails the release if packaged output references any
  `cdn…`/`fonts.googleapis…`/external endpoint — the G08 gate Z promoted to a ship-blocker.
- **"Production-ready" is earned, not declared (ARCH §19):** the launch-gate aggregator's green exit is the only thing that authorises a
  release; no numerical/label claim is made in prose.

## Out of scope (owning phase)

- Authoring or fixing any individual gate lane → **G07** (PG CI / RLS / restore mechanism) and **G08** (perf / offline-budget / mixed-fleet /
  Z / T / layering audit); G09 only **aggregates** their results into the launch bar.
- Any `src/**` defect the launch-gate aggregator exposes → the **owning** S/G phase (G09 only makes "release" gated).
- Enabling RLS in product, cross-tenant analytics, a scale-out/clustered topology, object-storage, external artifact store, passkeys/external
  IdP → deferred (ROADMAP §5); the scale-out **trigger** is *named* in the runbook, not implemented here.
- Signing/notarization infrastructure beyond citing the ARCH §17.2 trust-chain requirement (the desktop packager already bundles; the
  signing *pipeline* identity/secrets are an operator-environment concern, not a repo artifact).

## Acceptance criteria (mechanical)

1. `scripts/sandbox/build.sh full` = 0 warnings / 0 errors; `scripts/sandbox/run.sh` = `SMOKE RUN OK` (desktop unaffected, offline).
2. `docs/release/01-deployment-topology.md`, `docs/release/02-upgrade-rollback.md`, `docs/release/03-backup-restore-hostmove.md` all exist;
   `03-…` names a concrete **RPO cadence**, cites the **measured RTO** from `artifacts/restore-rto.txt`, states the **off-host durability**
   rule and the **named scale-out trigger** (grep each term present).
3. `scripts/release/make-release-manifest.sh` runs against a built output and produces (a) a NuGet SBOM, (b) a native-binary-inventory JSON
   listing `e_sqlite3`/`e_sqlite3mc`/SQLCipher with version + RID, and (c) a suppression-ledger that **exits non-zero** if a native-DB/crypto
   entry lacks an `expiry` field.
4. `scripts/windows/publish-portable.ps1 -EmitManifest` invokes the generator against the portable output (the SBOM + native inventory are
   written into the package folder) — asserted by `grep -n "EmitManifest" scripts/windows/publish-portable.ps1`.
5. `scripts/release/launch-gate.sh` exits **non-zero** when any required lane artifact is missing/red and **zero** only when every ARCH §19
   lane is green; on success it writes `artifacts/launch-gate-report.md` enumerating every gate → PASS with an evidence pointer;
   `launch-gate.sh --dry-run` lists every ARCH §19 lane.
6. `.github/workflows/release-gates.yml` declares `needs`/`workflow_run` on `pg-gates.yml` + `perf-gates.yml`, a smoke job, and a `launch-gate`
   job; it archives `artifacts/launch-gate-report.md`.
7. `LaunchGateManifestTests.EveryArch19Gate_IsCoveredByALane` is green (the aggregator's gate list equals the ARCH §19 gate table).
8. No change under `src/**` (`git diff --name-only <base>..HEAD -- src/` is empty for this phase); no migration, no package pin, no `.sln` edit.

## PROGRESS.md update

At the end of this phase: set the `G09` row in "Phase status" to `✅ Done` (add date + one-line summary), append one `### G09` entry to
"Repo state log" (the three `docs/release/*` runbooks, `scripts/release/make-release-manifest.sh` + the `publish-portable.ps1` `-EmitManifest`
hook, `scripts/release/launch-gate.sh` + `.github/workflows/release-gates.yml`, the SBOM/native-inventory + suppression-ledger, the
`LaunchGateManifestTests` + launch-gate report), log any deviation (and any `src/**` defect a gate exposed, filed under its owning phase).
**Leave "Next phase" as `M00`** — the T8 batch is now complete, but it is **T9** (the final M00 task) that reconciles the regenerated set,
rewrites `ROADMAP.md` §1/§2, flips the `M00` row to `✅ Done` and advances **"Next phase" → `S01`**. Commit with
`scripts/git/push.sh -b genspark_ai_developer "docs(release): G09 release engineering (topology, upgrade/rollback, backup/restore, packaging, SBOM)"`.
