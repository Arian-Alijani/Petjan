# TODO — Execute `docs/BACKEND_SYNC_ARCHITECTURE_AUDIT_2026-10-08.md`

> **For the executing AI model.** This file is self-contained. All the reading,
> cross-referencing and conclusions have already been done and distilled here.
> Do **not** re-audit the repo. Open only the exact `file:line` each task names,
> make the stated edit, verify, mark the task `[x]`, then **run the push script**
> (see "Push rule"). Work top-to-bottom.

---

## 0. Context you must hold (read once, then act)

**What this audit is.** A *roadmap / spec hardening* audit. The synchronization
system and the whole server (Tracks **D01–D13**, **E01–E09**) are **100% unbuilt**
— `src/Animora.Sync.Abstractions/` is an empty scaffold; `Animora.Server` /
`Animora.Persistence.SqlServer` / admin web **do not exist yet**. The desktop
(Tracks A–C) is complete and is the baseline.

**Therefore every task below is a DOCUMENTATION edit** to the roadmap phase files
(`backend-roadmap/phases/PHASE-*.md`), the roadmap meta files
(`backend-roadmap/PROGRESS.md`, `ROADMAP.md`, `CONVENTIONS.md`), or the specs
(`ARCHITECTURE.md`, `STACK.md`). We are **carrying each audit finding forward**
into the phase that will execute it (adding a design clause + an acceptance /
test criterion), exactly as the audit's §10 (IMP-*) and §11 action index prescribe.
**No C#/code is written, no build, no test is run** (there is nothing built to
run against, and the directive forbids full builds anyway). If a task's natural
home is a *test* that cannot exist yet, the task is to **write that test as a
named acceptance criterion inside the owning phase file**, not to author the test.

**What was ALREADY fixed by the last approved PR (do NOT redo).** The recently
merged **Phase-Problem** work (PR #222, commits `3b0a847`..`dcaf303`) already
applied these to the roadmap text — the 2026-10-08 audit *re-verified* them as
applied, so they are **out of scope**:
- §1 migration-number collisions E03/E05/E07 + E03 cross-context split → fixed.
- §2 D06/D07 handler inventories rebuilt from real `CommandTypes` (39 + 47 = 86) → fixed.
- §3 `PROGRESS.md` "Post-C11 drift" section + next-free-physical-ordinal rule → added.
- §4/§5 D13/E02/E01 file-path & `identity`→`auth` rate-limit-policy drift → fixed.
- §6 TreatmentPlan/RecurringExpense classification, `IsConflictPreservedCopy`
  local-only, "25 contracts" → fixed in D08/D02/E03 text.
- §7 Datasync `10.1.2` pin verified; encryption-at-rest enablement + K2 gate
  assigned to **E09** → done (NOTE: the new audit **moves encryption earlier**,
  see T1 — that is a *new* finding, not a redo).
- §9 PHASE-C03 historical stub reconstructed → done.

The 2026-10-08 audit's findings are the **second-order gaps** that remained open
*after* Phase-Problem, plus the sync-design sharp edges. Those are the tasks below.

**Finding → task map** (so nothing is lost): audit §11 "Severity-ordered action
index" is the backbone. Tasks T1–T10 below follow it 1:1. T0 is the drift-ledger
(IMP-02) that must exist first. Low/already-verified items (PH-07 C03 stub,
PH-08 rate-limit names) need **no edit** — they are confirmed resolved; T11 only
records that verification so a future reader does not re-flag them.

**Editing conventions (match the house style of these files):**
- Phase files use `### <ID> · <severity> · <title>` finding blocks and
  `**Acceptance:**` / `**Tests:**` / table rows. Mirror the surrounding format.
- When adding an acceptance criterion, add it to the phase's existing
  **Acceptance** / **Tests** section (grep the file for `## ` headings first).
- Tag every inserted clause with its audit finding id, e.g.
  `(audit 2026-10-08 · SYNC-C-02)` so it is traceable and the next drift diff finds it.
- Keep edits surgical and additive. Do **not** rewrite whole phase files.
- Persian/English: these docs are English; keep English.

**Push rule (LOCKED — failure to follow = project death):** after completing
**each** task below, run from repo root:
```
scripts/git/push.sh "docs(roadmap): <short task summary> (audit 2026-10-08 · <IDs>)"
```
If it exits `3` (no creds) call the `setup_github_environment` tool once, then
re-run the same command. One task → one push. If a single task turns into a large
multi-file edit, still push it as one coherent commit with a clear message.

---

## TASKS

### [x] T0 — Add the maintenance drift ledger (IMP-02) + baseline-verified line (IMP-06)
- **Audit refs:** IMP-02, IMP-06, §8 row for drift.
- **Why first:** every later phase edit should point the forward reader at one
  accurate baseline; this is the structural fix that stops the whole Phase-Problem
  drift class from recurring.
- **Edit 1 (IMP-02):** In `backend-roadmap/PROGRESS.md`, directly **after** the
  existing "## Post-C11 drift" section (it currently ends with the
  double-underscore filename note), add a short subsection
  **"## Maintenance drift ledger (consult before any D/E phase)"** that lists, as
  a table, the out-of-roadmap sources a forward phase MUST read as the true
  baseline: `Plan/*.md` audits (now historical), `CODE_AUDIT_REPORT_2026-10-07.md`,
  `docs/Phase-Problem.md` (resolved), and **this** 2026-10-08 audit. One row each:
  *source · what drift it records · status*. Keep it ≤12 lines.
- **Edit 2 (IMP-06):** Add a one-line **"Baseline verified"** template to
  `backend-roadmap/PHASE-AUTHORING-GUIDE.md` (grep it for a "## " heading to place
  it under authoring rules): instruct that every D/E phase, at execution, records a
  single line `> Baseline verified <date>: migration head <NNN>, CommandTypes <N>,
  projects <list>` so one diff detects the next drift.
- **Verify:** `grep -n "Maintenance drift ledger" backend-roadmap/PROGRESS.md` and
  `grep -n "Baseline verified" backend-roadmap/PHASE-AUTHORING-GUIDE.md` both hit.
- **Push:** `scripts/git/push.sh "docs(roadmap): add maintenance drift ledger + baseline-verified rule (audit 2026-10-08 · IMP-02,IMP-06)"`

---

### [x] T1 — Pull encryption enablement forward to E07; assert replica+conflict tables encrypted (rank 1)
- **Audit refs:** SYNC-C-02, PH-01, CODE-01. Owner phases: **E07** (enable), **E09** (release blocker), **E03/E05** (assert).
- **Ground truth already verified:** `src/Animora.Persistence.Sqlite/Options/LocalStoreOptions.cs:13`
  has `EncryptionEnabled` default `false`; Phase-Problem assigned enablement to **E09 only**;
  the new `repl_*` (E03) and `ConflictQueueEntries` (E05) tables are a **new plaintext PII surface**.
- **Edits (all additive, each tagged `(audit 2026-10-08 · SYNC-C-02)`):**
  1. `backend-roadmap/phases/PHASE-E07-online-auth-entitlement-device-lifecycle.md`
     — add a task + acceptance: "E07 sets `LocalStoreOptions.EncryptionEnabled = true`
     for release builds, gated on the K2 PoC; this is the first phase with a real
     server/credential path so it owns enablement." (grep E07 for its Acceptance/Tasks heading).
  2. `backend-roadmap/phases/PHASE-E03-isyncengine-datasync-client-adapter.md`
     — add acceptance: "the `repl_*` replica tables are created **inside the encrypted
     container**; a test opens a `repl_*` table and asserts `PRAGMA cipher_version`
     is non-empty." (ties to T2's shared-interceptor assertion).
  3. `backend-roadmap/phases/PHASE-E05-conflict-resolution-non-destructive-pull.md`
     — add acceptance: "`ConflictQueueEntries` lives in the encrypted container (same `PRAGMA` assertion)."
  4. `backend-roadmap/phases/PHASE-E09-release-engineering.md` — keep K2 as the
     **release blocker**, but add one line: "enablement itself happened in E07; E09
     only verifies the gate is green and no shipped build has `EncryptionEnabled=false`."
- **Verify:** `grep -rn "SYNC-C-02" backend-roadmap/phases/` shows ≥4 hits (E03,E05,E07,E09).
- **Push:** `scripts/git/push.sh "docs(roadmap): move encryption enablement to E07, assert replica+conflict tables encrypted (audit 2026-10-08 · SYNC-C-02,PH-01,CODE-01)"`

---

### [x] T2 — Make the dual-context single-file store safe-by-test (rank 2)
- **Audit refs:** SYNC-C-01, PH-02, IMP-05. Owner phase: **E03**.
- **Ground truth:** `PHASE-E03...md:69-108,164-165` introduces `AnimoraOfflineDbContext`
  (Datasync `OfflineDbContext`) in the **same SQLite file** as `AnimoraLocalDbContext`,
  each with its own `__EFMigrationsHistory`. Operational hazards: startup migrate
  ordering, WAL/locking, and the A11 encryption interceptor must apply to **both**.
- **Edits to `PHASE-E03-...md` (tag `· SYNC-C-01`):** add three acceptance/design clauses —
  1. **Shared cipher/key:** the A11 `SqliteConnectionInterceptor` + key provider are
     explicitly shared by **both** contexts (one connection string, one key source);
     asserted by opening `repl_*` and reading `PRAGMA cipher_version` (reuse T1's assertion).
  2. **Deterministic startup order:** one step list inside the existing `LocalStoreStartup` —
     local-context migrate → offline-context migrate → projection — **not** two independent initializers.
  3. **`TwoContextMigrationTests` acceptance:** on a fresh file both `__EFMigrationsHistory`
     tables advance, and neither context sees the other's tables in its model snapshot.
- **Verify:** `grep -n "TwoContextMigrationTests\|SYNC-C-01" backend-roadmap/phases/PHASE-E03-*.md` hits.
- **Push:** `scripts/git/push.sh "docs(roadmap): E03 dual-context store safe-by-test — shared cipher, ordered migrate, TwoContextMigrationTests (audit 2026-10-08 · SYNC-C-01,PH-02,IMP-05)"`

---

### [x] T3 — Separate "parked command" from "transport down"; surface attention state (rank 3)
- **Audit refs:** SYNC-C-04, SYNC-H-03, IMP-04, IMP-08. Owner phases: **E04**, **E06**.
- **Ground truth:** `PHASE-E06...md:44-52,112-114` = pass order 1–6, skips replication
  pull on *any* command-submit failure, and only a **transport** failure feeds `SyncCircuit`.
  A single permanently-`Rejected`/`NeedsAttention` command then starves replication forever
  while UI shows green. `PHASE-E04...md:67-72` circuit cannot represent "logically stuck".
- **Edits:**
  1. `PHASE-E06-...md` (tag `· SYNC-C-04`): add design clause — distinguish
     **transport failure** (skip pull; correct) from **per-command settlement failure**
     (`Rejected`/`NeedsAttention`/permanent `IntegrityError`). A **parked** command
     (no longer being submitted) must NOT block the replication pull. Add acceptance
     **`ReplicationNotStarvedByParkedCommandTests`**: one `NeedsAttention` command parked
     ⇒ replication pull still runs next pass. (IMP-08 standing fitness function.)
  2. `PHASE-E04-...md` (tag `· SYNC-H-03`, IMP-04): add a distinct
     **`SyncState.AttentionRequired`** (or surface the E06 backlog `NeedsAttentionCommands`
     counter) so a non-zero parked count is visually distinct from healthy `Synced`;
     add acceptance that a parked integrity error maps to `AttentionRequired`, not `Synced`.
- **Verify:** `grep -rn "ReplicationNotStarvedByParkedCommandTests\|AttentionRequired\|SYNC-C-04" backend-roadmap/phases/` hits E04 and E06.
- **Push:** `scripts/git/push.sh "docs(roadmap): E04/E06 parked-command ≠ transport-down, AttentionRequired state, starvation guard test (audit 2026-10-08 · SYNC-C-04,SYNC-H-03,IMP-04,IMP-08)"`

---

### [x] T4 — Decide + document cross-device financial read-model sync (rank 4)
- **Audit refs:** SYNC-C-05, SYNC-H-02, IMP-03, scenario SC-02. Owner: **D08** + a decision note (new) + E02 cross-ref.
- **Ground truth:** `PHASE-D08...md:48-51` lists `Invoice/Payment/StockMovement/...`
  as **not replicated**; `PHASE-E03...md:86-90` projection set has no financial aggregates.
  But C09/C10 built `OwnerFinancialSnapshot.TotalSpendMinor` + dashboard revenue from the
  **local** ledger (this device's commands only). On a 2-device clinic, device B never
  learns device A's posted invoice ⇒ dashboards silently disagree. No phase owns this.
- **Edit — record an explicit decision** (do NOT invent server code; just pick + document
  one of the two audit-sanctioned options, tag `· SYNC-C-05`):
  - **Option A (recommended by audit IMP-03):** add a read-only, server-computed
    finance read-model sync surface (`DailyFinanceFact` / `OwnerFinancialSnapshot`)
    as replication contracts so every device converges. If chosen, add it to D08's
    replicated-set discussion as a **read-model** class (distinct from the "not
    replicated by state" financial *aggregates*) and add an E-phase acceptance that
    multi-device owner-total/dashboard converge.
  - **Option B:** document that multi-device financial reads are **server-online-only**;
    desktop degrades those widgets to "requires connection" offline.
  - **What to actually write:** In `PHASE-D08-...md` near lines 48–56, add a clause
    naming the chosen option (pick **Option A**; it closes the hole, matches IMP-03).
    Add the read-model contracts to the replicated set as **read-only server-computed
    projections** and note they are NOT the financial aggregates (which stay command-settled).
    Add acceptance in `PHASE-E04`/`E05` (whichever owns convergence) that two devices
    reach the same `OwnerFinancialSnapshot.TotalSpendMinor` after sync.
- **Verify:** `grep -rn "SYNC-C-05\|finance read-model\|DailyFinanceFact" backend-roadmap/phases/PHASE-D08-*.md` hits.
- **Push:** `scripts/git/push.sh "docs(roadmap): D08 cross-device finance read-model sync decision (Option A), multi-device convergence acceptance (audit 2026-10-08 · SYNC-C-05,SYNC-H-02,IMP-03)"`

---

### [x] T5 — Define projection delete-order + skip-cascade + FK behavior (rank 5)
- **Audit refs:** SYNC-C-03, scenario SC-01. Owner phase: **E03**.
- **Ground truth:** `PHASE-E03...md:83-98,271-276` — `ProjectionOrder` lists parents
  before children (correct for inserts) but says nothing about **deletes** (tombstones)
  or **skip-cascade**; with `foreign_keys=ON` (`CONVENTIONS §5`) a parent soft-delete /
  partial batch can leave a referentially inconsistent store between the allowed
  `PartiallyCompleted` batches.
- **Edits to `PHASE-E03-...md` (tag `· SYNC-C-03`):**
  1. State deletes/tombstones apply in **reverse `ProjectionOrder`** (children first);
     OR verify-and-state that soft-delete only flips a flag and is order-independent
     (confirm children's `OwnerId` FK config — a parent soft-delete must NOT cascade-delete children).
  2. Define the **skip-cascade rule:** if a parent aggregate is skipped (unsent intent),
     its dependent rows in the same pass are also skipped and reported — the business
     store never gets a child pointing at a not-yet-projected parent.
  3. Add acceptance **`ReplicaProjectionTests`** cases: "parent skipped → child skipped"
     and "tombstone applied in reverse order."
- **Verify:** `grep -n "reverse ProjectionOrder\|skip-cascade\|ReplicaProjectionTests\|SYNC-C-03" backend-roadmap/phases/PHASE-E03-*.md` hits.
- **Push:** `scripts/git/push.sh "docs(roadmap): E03 projection delete-order + skip-cascade + FK rules, ReplicaProjectionTests (audit 2026-10-08 · SYNC-C-03)"`

---

### [x] T6 — Give post-C11 command families real server settlement + ledger posting (rank 6)
- **Audit refs:** RM-H-03, PH-09, scenario SC-07. Owner phases: **D06**, **D07**.
- **Ground truth:** Phase-Problem rebuilt the command vocabulary to 86 and included
  `vaccination.treatment.*` (D06) and `finance.recurring.*` (D07) as *rows*, but their
  **server settlement semantics** (invariants, ledger posting) are only named, not designed.
  A command the client emits but the server can't settle ⇒ permanent `Rejected`/`Pending`
  ⇒ the exact `SYNC-C-04` starvation trigger.
- **Edits (tag `· RM-H-03`):**
  1. `PHASE-D06-...md` — for `vaccination.treatment.create/update/cancel/occurrence.done`
     (aggregate `TreatmentPlan`), add the server-side invariant per family (what each
     settles, what it rejects), matching the detail the other D06 families already carry.
  2. `PHASE-D07-...md` — for `finance.recurring.add/pay` (aggregate `RecurringExpense`),
     add the server invariant **and** the `LedgerPostingHandler` extension, because
     `RecurringExpense` emits expense-like events the single-writer ledger must consume
     (same path as `Expense`). (PH-09.)
- **Verify:** `grep -n "RM-H-03\|LedgerPostingHandler\|TreatmentPlan" backend-roadmap/phases/PHASE-D0[67]-*.md` hits both.
- **Push:** `scripts/git/push.sh "docs(roadmap): D06/D07 server settlement for treatment & recurring-expense families + ledger posting (audit 2026-10-08 · RM-H-03,PH-09)"`

---

### [x] T7 — Persist monotonic high-water; index the sync views for the weak server (rank 7)
- **Audit refs:** SRV-M-01, SRV-M-02, OPT-01, OPT-02, OPT-06, scenario SC-03/SC-06. Owner phases: **D08**, **D13**, cross-ref **D02**.
- **Ground truth:** `PHASE-D08...md:107-116` `MonotonicStampService` is in-memory per
  (tenant,table), reseeded from `MAX(UpdatedAt)` on restart (narrow skip/dup window).
  `PHASE-D08...md:63-91` maps each contract to a SQL **view** `vw_Sync<Entity>` + a
  LINQ data-view predicate + OData `$filter` ⇒ heavy nested SQL / index ambiguity on a weak node.
- **Edits:**
  1. `PHASE-D08-...md` (tag `· SRV-M-01`, OPT-02): seed from `MAX(UpdatedAt) + 1 tick`
     **and persist** the high-water per (tenant,table) so a restart never rereads a
     racing max; add acceptance that the G1 burst test also covers "restart mid-burst."
  2. `PHASE-D08-...md` (tag `· SRV-M-02`, OPT-01): require each `vw_Sync<Entity>` to be
     **indexable** (SQL Server indexed/materialized view or persisted projection) and
     that `IX_<Table>_TenantId_UpdatedAt` exists on the **base** table the view reads.
  3. `PHASE-D13-...md` (OPT-05): add an **EXPLAIN-plan / query-time budget gate** for
     the 5 heaviest sync+report queries on the *minimum* server spec.
  4. `PHASE-D02-...md` (OPT-06): make `IX_<Table>_TenantId_UpdatedAt` a **migration
     invariant** on every replicated base table (not only a runtime readiness check).
- **Verify:** `grep -rn "SRV-M-01\|SRV-M-02\|EXPLAIN\|IX_.*TenantId_UpdatedAt" backend-roadmap/phases/PHASE-D0[28]-*.md backend-roadmap/phases/PHASE-D13-*.md` hits.
- **Push:** `scripts/git/push.sh "docs(roadmap): D08/D13/D02 persist monotonic high-water + indexable sync views + weak-server EXPLAIN gate (audit 2026-10-08 · SRV-M-01,SRV-M-02,OPT-01,OPT-02,OPT-06)"`

---

### [x] T8 — One migration-count source; one replicated-set manifest (rank 8)
- **Audit refs:** RM-H-01, PH-05, PH-06, IMP-01. Owner: **PROGRESS.md**, **D08**, **D02**.
- **Ground truth already verified:**
  - **RM-H-01:** `PROGRESS.md` store-row narrative (the `src/Animora.Persistence.Sqlite/...`
    row) still says *"31 migrations (`001`–`031`...)"* but then enumerates C11-era
    `001`–`019` inline; the audit flags the **old enumeration line** as still reading
    C11-era against the Post-C11 drift section. **Action:** reconcile that row so it
    points to the drift section as the single source — trim/annotate the stale `001`–`019`
    enumeration so no forward phase picks a migration number from it. (One number, one truth.)
  - **PH-05 / IMP-01:** D08 **already** states "25 entities" with `SyncContractCatalog`
    + `SyncSurfaceTests` (verified `PHASE-D08...md:36,57-62`). **Action:** only add a
    one-line note that the "25" count and all catch-up/policy counts MUST be *derived*
    from `SyncContractCatalog.ProjectionOrder`, never re-typed as literals (this is
    what T9 fixes in E04). Do NOT re-introduce a count — reference the manifest.
  - **PH-06:** `Phase-Problem §6.3` — D02's "~34 → ~39 configurations" is still
    *indicative*. **Action:** in `PHASE-D02-...md` add acceptance: re-derive the EF
    configuration count from the live domain at execution; assert config count ==
    replicated + local entity count (no magic number).
- **Edits:** the three actions above (tag `· RM-H-01` / `· PH-05` / `· PH-06`).
- **Verify:** `grep -n "see Post-C11 drift\|RM-H-01" backend-roadmap/PROGRESS.md` and
  `grep -n "PH-06\|config count" backend-roadmap/phases/PHASE-D02-*.md` hit.
- **Push:** `scripts/git/push.sh "docs(roadmap): single migration-count source + manifest-derived counts + D02 config-count assertion (audit 2026-10-08 · RM-H-01,PH-05,PH-06,IMP-01)"`

---

### [ ] T9 — Re-derive E04 catch-up groups from the 25-entity manifest (SYNC-M-01) + drain progress + minor code-debt pointers
- **Audit refs:** SYNC-M-01, SYNC-M-03, SYNC-M-02, CODE-02..CODE-08, OPT-03,04,07,08. Owner phases: **E04**, plus pointers.
- **Ground truth already verified:** `PHASE-E04...md:39-41` says catch-up uses
  **13 reference + 9 transactional = 22** entities and caps at `MaxCatchUpPasses = 20`,
  but D08 replicates **25** (post-C11 added `TreatmentPlan`/`TreatmentOccurrence`/
  `VaccineCatalogItem`). The group arithmetic is stale.
- **Edits:**
  1. `PHASE-E04-...md:38-42` (tag `· SYNC-M-01`): replace the hard-coded "13 + 9"
     with "derive the catch-up groupings from `SyncContractCatalog.ProjectionOrder`
     (currently 25)"; make `MaxCatchUpPasses` an explicit **per-wake budget that
     resumes next wake** (clarify, the text already implies resumability).
  2. `PHASE-E04-...md` or `PHASE-E06` (tag `· SYNC-M-03`): add a UI "draining N of M"
     progress from the existing backlog DTO so a multi-day serial drain is honest, not
     a silent hang; add a measured drain-rate expectation to **E08**'s gate.
  3. **Code-debt pointers (CODE-02..08, SYNC-M-02):** these are existing debts that
     endanger D/E. For each, add a one-line "blocking debt" note into the acceptance of
     the phase the audit §9 names as the blocker — do NOT fix code now, just record the
     gate so the phase author closes it first:
     - CODE-01 → already T1.
     - CODE-02 (`ct` propagation) → note in E04/E05/E06: new sync query/service
       interfaces get `ct = default` from the start.
     - CODE-03 (`.Result` ×15 in Statistics VMs) → note in the first E-phase that edits
       Desktop VMs: replace `.Result` with `await`; add grep-test forbidding `.Result` in Desktop.
     - CODE-04 / SYNC-M-02 (32 `DateTime.UtcNow` in Client.Services) → note in E04/E07:
       inject `IClock`/`IClinicClock`; grep-test forbidding `DateTime.Now/UtcNow` in
       `Client.Services` / `Animora.Sync.*` (clock authority, `CONVENTIONS §6.14`).
     - CODE-05 (`ConfigureAwait(false)`) → note in E03–E06: enforce in `Animora.Sync.*`.
     - CODE-06 (VM subscribe without unsubscribe) → note E04/E05/E06: new sync-flyout/
       conflict VMs are `IDisposable` + unsubscribe.
     - CODE-07 (`AllowNegativeStockOnSale=true`) → note E05/E06: the negative-stock
       server rejection must surface via the conflict path.
     - CODE-08 (`App.axaml.cs` store-start error swallowed) → note E04: log
       `result.Error`; sync loop must not start on a failed store.
  4. **Server throughput opts (optional, low-rank):** add OPT-03 (batch intake) to
     D05/D09, OPT-04 (outbox index) to D09, OPT-07 (gzip batches) to E06, OPT-08
     (bounded LRU MembershipCache) to D13 — one bullet each in the relevant phase.
- **Verify:** `grep -n "SyncContractCatalog.ProjectionOrder\|SYNC-M-01\|draining N of M" backend-roadmap/phases/PHASE-E04-*.md` hits;
  `grep -rn "CODE-0[2-8]\|SYNC-M-02" backend-roadmap/phases/` shows the pointers landed.
- **Push:** `scripts/git/push.sh "docs(roadmap): E04 catch-up derived from 25-entity manifest, drain progress, code-debt gates + server throughput opts (audit 2026-10-08 · SYNC-M-01,SYNC-M-03,SYNC-M-02,CODE-02..08,OPT-03,04,07,08)"`

---

### [ ] T10 — Default-deny read-only controllers + remaining low/medium phase items
- **Audit refs:** PH-10, SYNC-H-01, PH-03, PH-04. Owner phases: **D08**, **E03/E05**, **E05**, **E07**.
- **Edits:**
  1. **PH-10** (`PHASE-D08...md:66-74`, tag `· PH-10`): read-only enforcement is a
     per-controller 405 override. Add a **base-class default-deny** so a new
     `TableController` is read-only **unless it opts in** (only `Owner/Pet/WeightEntry`
     opt into writes); keep the enumeration test.
  2. **SYNC-H-01** (`PHASE-D08...md:57-60` + `PHASE-E05...md:67-68`, tag `· SYNC-H-01`):
     add a `SyncSurfaceTests` acceptance — the `MedicalHistoryEntry` sync contract has
     **no** `IsConflictPreservedCopy` member (reflection), and `ReplicaProjector` never
     pushes a preserved-copy row. (Documented by Phase-Problem; this makes it *asserted*.)
  3. **PH-03 / PH-04** (literal-ordinal risk): in `PHASE-E05-...md` (`032_ConflictQueue`)
     and `PHASE-E07-...md` (`033_OnlineIdentity`), add one line each: "the parenthetical
     ordinal is indicative only — at execution read the physical head of the owning
     context and take the next free ordinal (PROGRESS.md Post-C11 rule); never trust the literal."
- **Verify:** `grep -rn "PH-10\|SYNC-H-01\|default-deny\|next free ordinal" backend-roadmap/phases/PHASE-D08-*.md backend-roadmap/phases/PHASE-E05-*.md backend-roadmap/phases/PHASE-E07-*.md` hits.
- **Push:** `scripts/git/push.sh "docs(roadmap): D08 default-deny controllers + SYNC-H-01 contract assertion + E05/E07 next-free-ordinal guards (audit 2026-10-08 · PH-10,SYNC-H-01,PH-03,PH-04)"`

---

### [ ] T11 — Record verified-resolved low items so they are not re-flagged (PH-07, PH-08)
- **Audit refs:** PH-07 (C03 stub), PH-08 (rate-limit policy names). These need **no
  code/spec change** — they are already resolved; this task only prevents re-flagging.
- **Edit:** append a short "## Audit 2026-10-08 — verified-no-action items" note to
  the **bottom** of `docs/BACKEND_SYNC_ARCHITECTURE_AUDIT_2026-10-08.md` is NOT allowed
  (T12 deletes that file). Instead add two rows to the **drift ledger created in T0**
  (`backend-roadmap/PROGRESS.md`), stating: PH-07 C03 stub is acceptable (C03 done,
  stub + ROADMAP note exist); PH-08 verify at D13/E01 exec that only
  `auth, commands, tables, files, operator` rate-limit policies exist. One line each.
- **Verify:** `grep -n "PH-07\|PH-08" backend-roadmap/PROGRESS.md` hits.
- **Push:** `scripts/git/push.sh "docs(roadmap): record PH-07/PH-08 as verified-no-action in drift ledger (audit 2026-10-08)"`

---

### [ ] T12 — FINAL: delete this plan file only
- **Precondition:** T0–T11 all `[x]` and each pushed.
- **Action:** delete **only** `Plan/backend-sync-audit-2026-10-08-todo.md`.
  Do **NOT** touch any other file, the roadmap, or the audit doc
  (`docs/BACKEND_SYNC_ARCHITECTURE_AUDIT_2026-10-08.md` stays — other tasks did not
  remove it; leaving it as the historical record is fine, the user only asked to
  delete the plan file).
- **Push:** `scripts/git/push.sh "chore(plan): remove completed backend-sync audit todo (audit 2026-10-08 done)"`

---

## Quick reference — exact anchors (so you never re-grep)

| Finding | File:line | One-line fact |
|---|---|---|
| SYNC-C-02 | `src/Animora.Persistence.Sqlite/Options/LocalStoreOptions.cs:13` | `EncryptionEnabled` default `false` |
| SYNC-C-01 | `PHASE-E03...md:69-108,164-165` | two EF contexts, one SQLite file, two `__EFMigrationsHistory` |
| SYNC-C-03 | `PHASE-E03...md:83-98,271-276` | `ProjectionOrder` insert-only; deletes/skip undefined |
| SYNC-C-04 | `PHASE-E06...md:44-52,112-114` | skips pull on any submit failure; circuit only on transport |
| SYNC-C-05 | `PHASE-D08...md:48-51`; `PHASE-E03...md:86-90` | finance not replicated; no cross-device read path |
| SYNC-H-01 | `PHASE-D08...md:57-60`; `PHASE-E05...md:67-68` | `IsConflictPreservedCopy` local-only, unasserted |
| SYNC-H-03 | `PHASE-E04...md:67-72`; `PHASE-E06...md:112-116` | circuit can't show "logically stuck" |
| RM-H-01 | `PROGRESS.md` store row vs Post-C11 drift | row still enumerates `001`–`019` |
| RM-H-03 | `PHASE-D06`/`D07` | post-C11 families named, settlement undesigned |
| SYNC-M-01 | `PHASE-E04...md:38-42` | catch-up "13+9=22" vs real **25** |
| SRV-M-01 | `PHASE-D08...md:107-116` | in-memory high-water, restart reseed window |
| SRV-M-02 | `PHASE-D08...md:63-91` | `vw_Sync<Entity>` view indexability on weak server |
| PH-05 | `PHASE-D08...md:36,57-62` | "25" + `SyncContractCatalog` already present → derive, don't retype |
| PH-10 | `PHASE-D08...md:66-74` | per-controller 405 → want base-class default-deny |

**Reminder:** every task = documentation edit only; after each → run `scripts/git/push.sh "<msg>"`. The push is LOCKED.
