# Animora — Remaining-Phases (Track D/E) Professional Review

**Date:** 2026-10-08
**Reviewer role:** Backend / sync architect, independent pass.
**Scope:** The **unbuilt** remaining roadmap only — Track **D** (`D01`–`D13`, server) and Track **E**
(`E01`–`E09`, admin web + sync + licensing). Tracks A–C (local core, pipeline, mock retirement) are
**done** and treated as the trusted baseline, not re-graded here.
**Method:** Static reading + cross-referencing of `backend-roadmap/**` phase files, `ARCHITECTURE.md`,
`STACK.md`, `PERFORMANCE.md`, the live `src/**` tree, `CommandTypes`, the migration set, and the two
prior audits (`docs/Phase-Problem.md`, `docs/BACKEND_SYNC_ARCHITECTURE_AUDIT_2026-10-08.md`).
**No build and no test were run** (per directive). Every finding cites a concrete file / line / anchor.

> **How to read this (AI-optimized):** Each finding is one block —
> `ID · severity · Where · What · Fix · Why`. Severity-sorted inside each section. No prose padding.
> Prior findings already folded into the phase *text* are marked **[TEXT-FIXED / UNBUILT]** so a model
> does not re-flag them as open design gaps; they are now *realization* risks, not *design* defects.

---

## 0. Index

| § | Content |
|---|---|
| 1 | One-paragraph system description + scorecard |
| 2 | Goal 1 — Inconsistencies between remaining phases and with the current repo state |
| 3 | Goal 2 — Non-standard phase mapping / planned implementation |
| 4 | Goal 3 — Server/sync precision & efficiency defects |
| 5 | Goal 4 — Standardization problems in what the phases will implement |
| 6 | Goal 5 — Phase-level improvements for code quality + performance |
| 7 | Goal 6 — What must change for a true 10/10 implementation |
| 8 | Direct answers to the six questions |
| 9 | Severity-ordered action index |

---

## 1. System in one paragraph + scorecard

Animora is an **offline-first veterinary-clinic desktop** (Avalonia/.NET 10) on a real encrypted-capable
local SQLite store, with a **CQRS command pipeline + transactional outbox** already built (Tracks A–C).
The remaining work builds the **server side**: one **ASP.NET Core + SQL Server node per clinic** (D01–D09,
D12–D13), a **two-way sync** between each desktop and its clinic node (two independent transports — a
command pipeline for *intent* and a Datasync replication surface for *state*), plus a **central operator
plane** (subscriptions, seats, licensing) that the vendor runs once for all clinics (D10–D11, E01–E02),
and the **client sync engine / conflict resolution / online auth** that closes the loop (E03–E07). The
design is strong on paper; the entire sync + server track is **0% built** and its hardest pieces
(conflict, transport, encryption-at-rest) are last.

| Dimension | Grade | Basis |
|---|---|---|
| Completed backend (A–C) | **9.0** | Clean layering, single ledger writer, proven stock cache, idempotent journal. Baseline only. |
| Remaining **design** (D/E on paper) | **8.0** | Pre-made decisions, band-limited phases, correct intent≠state split, fail-closed matrices. |
| Remaining **roadmap consistency** | **7.5** | Prior collisions fixed in text; residual second-order drift + unverifiable cross-phase path/count agreements. |
| **Sync system — design** | **8.5** | Non-naïve: idempotent consumer, non-destructive pull, conflict matrix, crash-window coverage. |
| **Sync system — realization readiness** | **5.5** | Entirely unbuilt; risk front-loaded into E03–E06; several decisions proven only "by prose." |
| **Server system — design** | **7.5** | Sound shape; weak-server perf and restart-safety of the monotonic cursor are the soft spots. |
| **Overall remaining program** | **7.5 / 10** | A good blueprint whose grade is capped by unbuilt, front-loaded, high-coupling sync realization. |

---

## 2. GOAL 1 — Inconsistencies (phase↔phase and phase↔current repo)

### G1-01 · 🟠 · `PROGRESS.md` headline store row contradicts its own drift section on migration count
- **Where:** `backend-roadmap/PROGRESS.md:41` (narrates "31 migrations … physical head `031`") vs the
  same file's older Track-C log lines (C11 `:221` "migration `019`") and the Post-C11 drift table
  `:68-81`.
- **What:** The status table was reconciled to 31, but the per-phase *log* entries below still read
  C11-era (`019`), and the binding "next-free-physical-ordinal" rule lives only in the drift section.
  A forward phase that greps a log line instead of the drift section can still pick a stale ordinal.
- **Fix:** Add a single one-line pointer at the top of the status table and at C11's log entry:
  "migration count is authoritative ONLY in Post-C11 drift (currently 31); never read an ordinal from a
  log line." (The repo already half-did this — finish it.)
- **Why:** The migration-collision class (`docs/Phase-Problem.md` §1/§3) re-enters whenever the headline
  contradicts the drift section. One number, one source.

### G1-02 · 🟠 · Replicated-set count (**25**) is load-bearing across D08/E03/E04/E05/E08 but derived independently in each
- **Where:** `PHASE-D08…md:36,57` (25 = 3 writable + 22 read-only), `PHASE-E04…md:33-35`
  (catch-up groups), `PHASE-E05…md` (policy coverage), `PHASE-E03…md:21` ("22 D08 contracts" wording).
- **What:** D08 already mandates the `SyncContractCatalog` manifest as the single source and tells later
  phases to derive counts at runtime (good). But the **prose** across phases still mixes numbers:
  E03's goal line says "pulls the 22 D08 contracts" while D08's current decision adds a **third class**
  (read-only finance read-models `DailyFinanceFact`/`OwnerFinancialSnapshot`, `D08:65-79`) that raises
  the read-only set above 22. E03/E04 prose was not updated for that third class.
- **Fix:** Sweep E03/E04/E05 prose to say "every entity in `SyncContractCatalog`" with **no literal
  count**, and move `DailyFinanceFact`/`OwnerFinancialSnapshot` out of D08's "server-internal" row in the
  same edit (D08 says to do this "when this lands" — it has not been reflected in E03/E04 yet).
- **Why:** The finance-read-model class was added to D08 to fix a correctness hole (see G3-05) but the
  consuming phases still describe the pre-fix set — an internal contradiction that will surface as a
  catch-up group that omits the finance projections.

### G1-03 · 🟠 · Post-C11 command families (`vaccination.treatment.*`, `finance.recurring.*`) are listed by D06/D07 but their **server settlement + ledger posting** is undefined
- **Where:** `backend-roadmap/PROGRESS.md:84-89` (86 `CommandTypes`, 2 new aggregates
  `TreatmentPlan`, `RecurringExpense`); `docs/BACKEND_SYNC…AUDIT…md` RM-H-03 / PH-09;
  `PHASE-D06`/`PHASE-D07` (handler counts 39+47=86).
- **What:** The command *vocabulary* was reconciled to 86 and the two new families included as rows, but
  D07 never specifies the `LedgerPostingHandler` extension for `RecurringExpense` (which emits
  expense-like events the **single-writer ledger** must consume), nor the server invariants for
  `TreatmentPlan`. A command the client can emit but the server cannot settle parks forever.
- **Fix:** In D07 add the `finance.recurring.* → LedgerPostingHandler` posting rule (mirror C09's
  `ExpensePostedEvent` path) and in D06 the `TreatmentPlan`/`TreatmentOccurrence` server invariants.
- **Why:** This is the direct trigger for the starvation bug in G3-04/G4-02 — a parked money command.

### G1-04 · 🟡 · Cross-phase file-path agreements (D13/E02/D01/D02/D04) are text-vs-text, unverifiable until the server projects exist
- **Where:** `docs/Phase-Problem.md:154-159`; `PHASE-D13`, `PHASE-E02`, `PHASE-D01/D02/D04`
  (`HealthEndpoints.cs` in `Health/`, `MembershipCache.cs` in `Tenancy/`, `ServerModel.cs` in `Context/`).
- **What:** `Animora.Server` / `Animora.Persistence.SqlServer` **do not exist yet**; the path fixes are
  agreements between two documents, not checks against a real tree. A later author can create a file at a
  third path and nothing fails.
- **Fix:** When D01/D02 land, add `ServerLayoutTests` asserting the canonical folders exist and each
  cross-referencing phase targets a real path (lock it the first time the project is created).
- **Why:** Intra-roadmap path agreement is fragile until files exist; make it a structural test.

### G1-05 · 🟡 · `Statistics` module exists in the repo but is outside the roadmap and has no server/replication surface
- **Where:** `backend-roadmap/PROGRESS.md:90-91` (`src/Animora.Contracts/Statistics/*`, `Pages/Statistics`,
  "not covered by `AGENTS.md`; no server/replication surface").
- **What:** A whole desktop module shipped out-of-band. If any Statistics screen reads cross-device data,
  it inherits the unresolved finance-read-model gap (G3-05) with no phase owning it.
- **Fix:** Add one line to D08's decision: Statistics reads only the already-classified replicated
  read-models (finance projections) or is explicitly online-only; add it to the `SyncContractCatalog`
  coverage assertion so it cannot silently need an unreplicated table.
- **Why:** Untracked modules are exactly how the C11→now drift happened; close the loop before D08.

### G1-06 · 🟡 · Migration-ordinal literals still appear in E05/E07 despite the "next-free-physical" rule
- **Where:** `PHASE-E05…md` (`032_ConflictQueue`), `PHASE-E07…md:187,211` (`033_OnlineIdentity`,
  already annotated "re-verify physical head at execution").
- **What:** E07 was annotated correctly; E05's `032` is still written as a near-literal
  ("next free as of 2026-10-07, head 031"). Two more migrations may land between now and E05 (D-track is
  on the **server** `Persistence.SqlServer` context — a separate sequence — so the *client* head may not
  move, but E03's `AnimoraOfflineDbContext` adds its own sequence; the ordinal arithmetic is no longer a
  single line).
- **Fix:** Replace both with "next free ordinal on the **owning** context, read physically at execution."
  Note explicitly that E03 introduces a **third** EF context with its **own** sequence (see G3-01).
- **Why:** Prevents the collision class returning through a stale parenthetical.

**Verdict on Goal 1:** The *gross* inconsistencies (migration collisions, handler inventory, rate-limit
policy names) were already fixed in text. What remains is **second-order drift** — headline-vs-detail
count mismatches, prose not updated for D08's new finance-read-model class, and undefined settlement for
the two post-C11 command families. None blocks a build; two (G1-02, G1-03) are latent correctness bugs.

---

## 3. GOAL 2 — Non-standard phase mapping / planned implementation

### G2-01 · 🔴 · Dual EF context in one SQLite file (E03) is an atypical, high-risk arrangement
- **Where:** `PHASE-E03…md` (`AnimoraOfflineDbContext`, Datasync `OfflineDbContext`, its own
  `__EFMigrationsHistory` + `001_ReplicaTables`, alongside `AnimoraLocalDbContext`);
  prior audit SYNC-C-01 / PH-02 **[TEXT-FIXED / UNBUILT]**.
- **What:** Two `DbContext`s with two independent migration-history tables sharing one physical file is
  non-standard EF practice. The phase text now mandates a shared connection-interceptor + key and a single
  deterministic migrate order, but these are the hardest things to get right and are **asserted only by a
  not-yet-written test**. The A11 encryption interceptor must be applied to **both** or the replica tables
  open plaintext (see G3-02).
- **Fix (keep in plan, make testable):** Make the dual-context store "safe by test, not by prose": one
  `TwoContextMigrationTests` fact (both histories advance on a fresh file; neither context sees the
  other's tables in its snapshot) and one `PRAGMA cipher_version` fact over `repl_*`. Prefer a single
  `LocalStoreStartup` step list over two independent initializers.
- **Why:** This is the single most error-prone structural decision in the whole program; "text is
  consistent" ≠ "runtime is safe."

### G2-02 · 🟠 · Datasync read served through SQL **views** (`vw_Sync<Entity>`) is non-standard for a weak single node
- **Where:** `PHASE-D08…md:83-96` (each contract mapped as a DB view; `IAccessControlProvider` LINQ
  predicate + OData `$filter`); prior SRV-M-02 / OPT-01.
- **What:** Datasync needs an EF entity; the plan maps each contract to a **view**. A view + a data-view
  LINQ predicate + OData `$filter=updatedAt gt …` can produce deeply nested SQL that does not seek on an
  index on a weak node. The plan now requires each view to be **indexable** (indexed/schema-bound view or
  a trivial column subset served by the base-table `IX_<Table>_TenantId_UpdatedAt`) — correct, but it is a
  constraint that is easy to violate silently.
- **Fix:** Make it a **migration invariant** (OPT-06): every replicated base table carries
  `IX_<Table>_TenantId_UpdatedAt`, and D13's weak-server gate runs an EXPLAIN check that the delta query
  seeks (no scan). Prefer persisted projection tables over views where the projection is non-trivial.
- **Why:** Delta pull over an unindexed view on the minimum server spec is the most likely server
  performance cliff (see G4-03, SC-weak-server).

### G2-03 · 🟡 · Operator operations deliberately **bypass** the command pipeline — correct, but it is a *second* write path
- **Where:** `PHASE-D10…md:69-77` (operator ops are "direct, operator-authorized service calls behind
  `ISubscriptionOperations`", not `/api/commands`).
- **What:** This is intentional and justified (operator ops are not tenant/device-scoped), but it means
  the server has **two** write disciplines: the idempotent command journal for tenants, and a separate
  `(SubscriptionId, EventType, RequestOperationId)` idempotency + append-only `SubscriptionEvent` for
  operators. Two idempotency mechanisms is a maintenance and correctness surface.
- **Fix:** No change to the split (it is right). Add a short ADR-style note in D10 that the two
  idempotency schemes are deliberate and list the single guarantee both must meet ("same op key ⇒ at most
  one effect"), with one shared test shape reused by both.
- **Why:** Non-standard-but-justified decisions should be explicitly ADR'd so a future reader does not
  "unify" them and break the operator plane's tenant-independence.

### G2-04 · 🟡 · Licence artifact is a **custom detached-signature envelope**, not a standard token (JWT/PASETO)
- **Where:** `PHASE-D11…md:36-52` (`Base64Url(payload).Base64Url(sig).KeyId`, ECDSA P-256/SHA-256,
  canonical JSON).
- **What:** Rolling a bespoke signed-envelope format is non-standard; the justification (avoid a token
  stack and its clock model, offline verifiability, no custom *crypto primitive*) is sound, and it uses
  first-party `ECDsa`/`HMACSHA256` only. Risk is in canonicalization drift (the payload is hashed as
  canonical JSON) between issuer and verifier.
- **Fix:** Keep the format. Require that **one** canonicalizer (B02's `CommandFingerprint` settings) is
  shared by `Issuance/` and `Verification/`, asserted by a round-trip test (sign on server, verify with
  the client library over the identical bytes). This is stated but must be a test, not a comment.
- **Why:** Every bespoke signed format dies by canonicalization mismatch; pin it with a cross-assembly
  round-trip fact.

**Verdict on Goal 2:** Three deliberate non-standard choices (dual-context store, view-based sync read,
custom licence envelope) and one deliberate second write path. All are *defensible*, but the dual-context
store (G2-01) is genuinely dangerous and the view-based read (G2-02) is the performance risk. None is
"wrong for the sake of it"; each needs a **test that proves the atypical choice is safe**, which today is
promised in prose.

---

## 4. GOAL 3 — Server/sync precision & efficiency

### G3-01 · 🔴 · `MonotonicStampService` restart-safety: delta-cursor correctness rests on strict monotonicity that an in-memory high-water cannot fully guarantee
- **Where:** `PHASE-D08…md:141-162` (strictly-monotonic `UpdatedAt` per (tenant,table), in-memory
  high-water seeded from table max; prior SRV-M-01 / OPT-02 **[TEXT-FIXED / UNBUILT]**).
- **What:** The whole delta-pull correctness argument (Gate G1) depends on `UpdatedAt` never colliding or
  going backwards. The plan now **persists** the high-water per (tenant,table) in a
  `SyncStampHighWaters` row advanced in the same transaction with block reservation — good. But the
  "restart mid-burst" fact is unwritten and the reservation step (`N=1000 ticks`) is a new invariant that
  must survive a crash between reserving and writing.
- **Fix:** Implement persisted high-water + the "restart mid-burst" burst test (100 inserts in one ms,
  tear down + rebuild the service mid-burst, assert zero skipped/dup and strictly increasing). Fold the
  full matrix into E08 Gate G1.
- **Why:** An in-memory seed is one restart away from a skipped/duplicated row — the exact failure the
  service exists to prevent.

### G3-02 · 🔴 · Encryption-at-rest is OFF by default and the new replica/conflict tables are a fresh plaintext PII surface
- **Where:** `src/Animora.Persistence.Sqlite/Options/LocalStoreOptions.cs:13` (`EncryptionEnabled`
  default `false`), `:63` (`EncryptionEnabled && !K2Approved` ⇒ fail); `PHASE-E07…md:152-161` moves
  enablement to E07 gated on E08's K2 PoC **[TEXT-FIXED / UNBUILT]**.
- **What:** Through E03–E06 the replicated `repl_*` tables (server-authoritative owner names/phones,
  preserved clinical history) and E05's `ConflictQueueEntries` sit in plaintext unless E07's enablement
  actually lands and E08's K2 gate passes. The decision is written; the code default is still `false` and
  `MultipleCiphersEncryptionProvider` is "compiled but unreachable."
- **Fix:** Enforce the E07 enablement as an **acceptance test** (release profile opens the store with a
  non-empty `PRAGMA cipher_version`, and `repl_*` + `ConflictQueueEntries` are inside the encrypted
  container), and keep E09 as the release blocker. Do not let E03 merge a plaintext PII surface without the
  cipher test.
- **Why:** `ARCHITECTURE.md` R3 / `STACK.md` promise "encrypted at rest"; a plaintext replication cache of
  server-authoritative patient data is a direct violation and a medical-data exposure.

### G3-03 · 🟠 · Clock authority: 32 raw `DateTime.UtcNow` sites in `Client.Services` will leak into sync/licensing timing
- **Where:** `docs/CODE_AUDIT_REPORT_2026-10-07.md` (32 sites, e.g. `VaccinationService`);
  `CONVENTIONS §6.14` ("client wall-clock is never a sync/licensing authority"); E04 jitter, E06 crash
  windows and E07 licence high-water all assume `IClock`/`IMonotonicClock`. E07 adds a grep-test
  forbidding `DateTime.UtcNow` in `Client.Services`/`Sync.*` **[TEXT-FIXED / UNBUILT]**.
- **What:** A stray `UtcNow` in the sync loop silently reintroduces the clock-rollback vulnerability the
  licence verifier (`max(wall, highWater)`) exists to prevent.
- **Fix:** Close the clock debt **before** E04/E07 (inject `IClock`), and land the grep-test in the first
  E-phase that edits those files — not at the end.
- **Why:** A wound-back wall clock must not revive an expired capability; the fix must precede the phases
  that depend on monotonic time, not trail them.

### G3-04 · 🟠 · "Commands-before-replication, skip-pull-on-failure" must distinguish *transport-down* from *parked command* or replication starves
- **Where:** `PHASE-E06…md:44-55` (pass order 1–6; skip pull on transport failure);
  "Parked ≠ transport-down" note added `:55+` **[TEXT-FIXED / UNBUILT]**; prior SYNC-C-04.
- **What:** If a single command is permanently `Rejected`/`NeedsAttention` and the processor keeps
  attempting it, every pass fails step 2 and the inbound pull never runs — the read store goes stale
  forever while the UI shows "Pending." The plan now says only an *in-flight transport* failure skips the
  pull and a *parked* command does not — correct, but unproven.
- **Fix:** Implement + assert `ReplicationNotStarvedByParkedCommandTests` (one `NeedsAttention` command
  parked ⇒ replication pull still runs next pass). Pair with G3-03's settlement fix so parked money
  commands are rare.
- **Why:** As originally written this is a severe silent data-staleness bug; the text fix is only real
  once the fitness test exists.

### G3-05 · 🟠 · Cross-device financial reads: closed in D08 text (Option A) but not yet reflected in the consuming phases
- **Where:** `PHASE-D08…md:65-82` (adds read-only server-computed `DailyFinanceFact`/
  `OwnerFinancialSnapshot` sync contracts, "Option A"); convergence asserted "in E04/E05"
  **[TEXT-FIXED / UNBUILT]**; prior SYNC-C-05 / SYNC-H-02.
- **What:** Device B otherwise never learns device A's posted invoice (invoices are not replicated), so
  C09/C10 owner-total and dashboard revenue disagree across devices. D08 now replicates the two *server
  projections* read-only. But E04 catch-up groups and E05 policy coverage still describe the pre-fix set
  (see G1-02), and the "two devices converge on the same `TotalSpendMinor`" assertion is not yet an E-phase
  acceptance line.
- **Fix:** Propagate the third class into E04 (catch-up includes the finance projections) and E05
  (policy = `ServerAuthoritative` for the read-model contracts), and add the convergence acceptance to
  E04/E05 explicitly.
- **Why:** The write-side intent≠state split is correct; the read-side loop is only half-closed until the
  consuming phases carry it.

### G3-06 · 🟡 · `SyncCircuit` cannot represent "logically stuck" vs "server down" — green state can hide a parked money command
- **Where:** `PHASE-E04…md` (circuit opens on 5 consecutive pass failures or
  `CONTRACT_VERSION_UNSUPPORTED`); `PHASE-E06…md` backlog counters; prior SYNC-H-03 / IMP-04.
- **What:** A pass that "pulls fine but has a parked command" is not a failure, so the UI shows
  `Synced`/`Pending` while a financial command is permanently `NeedsAttention`. `ARCHITECTURE.md §8.3`
  requires "who must act" to be surfaced.
- **Fix:** Add a distinct `SyncState.AttentionRequired` (or surface a non-zero `NeedsAttentionCommands`
  count in the topbar as a visually distinct state).
- **Why:** "Green = everything synced" becomes a lie; a parked integrity error must be visible at the
  orchestration level, not just in a notice.

### G3-07 · 🟡 · Serial push drain (`ParallelOperations = 1`, batch 20 / 512 KB) is correct but invisible on a multi-day backlog
- **Where:** `PHASE-E03…md` (`ParallelOperations` default 1); `PHASE-E06…md:40` (batch 20 / 512 KB).
- **What:** Single-threaded push is right for ordering, but a clinic offline for days drains serially with
  no progress signal beyond a backlog counter — looks like a hang.
- **Fix:** Keep serialism; surface "draining N of M" from the existing backlog DTO, and add a measured
  drain-rate expectation to E08's gate. Optionally gzip batches > a threshold over the LAN (OPT-07).
- **Why:** Acceptable performance, dishonest UX; make the serial drain visible.

### G3-08 · 🟡 · `ReplicaProjector` delete/skip ordering + FK behavior under a partial-batch crash
- **Where:** `PHASE-E03…md` (`ProjectionOrder` parents-before-children for inserts; `PartiallyCompleted`
  with earlier batches committed; skip-on-pending); prior SYNC-C-03.
- **What:** Insert order is correct for FK creation but **wrong for deletes** (a parent tombstone applied
  before its children's with `foreign_keys=ON` violates FK), and the skip-on-pending rule can leave a
  child pointing at a not-yet-projected parent across batches.
- **Fix:** State deletes/tombstones apply in **reverse** `ProjectionOrder` (or prove soft-delete is
  order-independent because it only flips a flag and no FK cascades); define the skip-cascade rule (parent
  skipped ⇒ its dependents skipped and reported); add `ReplicaProjectionTests` for "parent skipped → child
  skipped" and "tombstone order."
- **Why:** Partial projection + FK-on under a crash mid-pass is exactly where an offline-first store
  corrupts itself.

**Verdict on Goal 3:** The sync/server *efficiency and precision* model is good, but it is only as strong
as a set of **tests that do not exist yet** (monotonic restart, cipher-on-replica, parked≠transport,
convergence, projection-order). Two items are genuine efficiency risks (view-based delta pull G2-02 →
G4-03; serial drain G3-07). The design answers are mostly present; the **proofs** are not.

---

## 5. GOAL 4 — Standardization problems in what gets implemented

### G4-01 · 🟠 · "Documented, not asserted" is the dominant standardization gap across D/E
- **Where:** D08 read-only split, `MedicalHistoryEntry.IsConflictPreservedCopy` exclusion
  (`PHASE-D08…md:60-62`), replicated-set manifest, conflict-policy coverage (`PHASE-E05`), monotonic
  burst (`PHASE-D08`).
- **What:** Many invariants are stated as prose with a *promised* test ("`SyncSurfaceTests` asserts…",
  "a test enumerates…"). The repo's own history (`docs/Phase-Problem.md`) shows that *documented-only*
  rules are how the migration-collision class re-entered.
- **Fix:** Make the following standing **fitness functions** (one test each, must exist before the owning
  phase merges, not after): `SyncContractCatalog` is the single source of the replicated set (IMP-01);
  per-context cipher + migration harness (IMP-05); replication-starvation guard (IMP-08); a "baseline
  verified on `<date>`" header line in every D/E phase recording migration head + `CommandTypes` count +
  project list (IMP-06).
- **Why:** The program's main standardization weakness is not the design — it is that correctness is
  carried in prose a later edit can silently contradict.

### G4-02 · 🟠 · A command the client emits but the server cannot settle has no standard terminal state
- **Where:** `PHASE-D06`/`PHASE-D07` (post-C11 families listed, settlement undefined — G1-03);
  `SettlementState`/`SettlementTransitions` (B02).
- **What:** The settlement state machine is standard and good, but the roadmap does not standardize what
  happens to a command type the server has **no handler** for: it will sit `PendingSettlement`/`Rejected`
  and (per G3-04) can starve replication. There is no "unknown command type ⇒ deterministic `Rejected`
  with `COMMAND_TYPE_UNSUPPORTED`" rule.
- **Fix:** Add to D05 intake a standard rule: an envelope whose `CommandType` has no registered server
  handler is settled `Rejected` with a dedicated code (not left `Pending`), so it parks visibly and does
  not block the pull. Then G1-03 removes the cause by giving the two families handlers.
- **Why:** Standardizing the "no handler" terminal state turns a silent freeze into a visible, bounded
  failure.

### G4-03 · 🟡 · Weak-server performance is a *principle* (`§16.1`) but not yet a *standardized gate*
- **Where:** `PHASE-D13` (observability + weak-server perf); `STACK.md §16.1`; prior OPT-05.
- **What:** "Runs on a weak single node" is asserted as a design principle but there is no standardized,
  repeatable EXPLAIN + time-budget gate on the minimum spec for the heaviest sync + report queries.
- **Fix:** Standardize D13's gate: the 5 heaviest sync/report queries must seek (no scan) and meet a
  time budget on the documented minimum server spec; the delta-pull view (G2-02) is top of the list.
- **Why:** Without a standardized gate, the weak-server promise is unfalsifiable until a clinic hits it.

### G4-04 · 🟡 · Two idempotency schemes (tenant command journal vs operator `RequestOperationId`) lack a single stated contract
- **Where:** B02 command journal (fingerprint + `OperationId`); `PHASE-D10…md:78-86` operator
  `(SubscriptionId, EventType, RequestOperationId)` unique key.
- **What:** Both are correct, but "idempotency" is implemented two ways with no single documented
  guarantee both satisfy; a future author could diverge them.
- **Fix:** State the one contract once ("same operation key ⇒ at most one effect; replay returns the
  stored result") and reuse one test shape for both planes (ties to G2-03).
- **Why:** Standardize the guarantee, not the mechanism.

**Verdict on Goal 4:** The planned implementation is *conventionally* standard (CQRS, append-only ledger,
RBAC, settlement state machine, first-party crypto). The real standardization debt is **proof discipline**
— too much is "documented, not asserted" — plus two unspecified terminal/contract rules (G4-02, G4-04).

---

## 6. GOAL 5 — Phase-level improvements for code quality + performance

> Existing debts from `docs/CODE_AUDIT_REPORT_2026-10-07.md` that specifically endanger D/E and should be
> closed **in or before** the phase that depends on them.

| ID | Where | Problem | Fix | Blocks |
|---|---|---|---|---|
| G5-01 | `LocalStoreOptions.cs:13` | Encryption off by default; provider unreachable | Enable at E07, K2-gate at E08, release-block at E09; assert replica/conflict tables encrypted | E03/E05/E07 |
| G5-02 | `src/Animora.Contracts/**` (36 of 196 `Task` sigs lack `ct`) | `CancellationToken` not propagated | Add `ct = default` to the **new** `ISyncDiagnosticsQuery`/`IConflictQueueService`/`IUnsettledWorkService` from the start; backfill existing | E04/E05/E06 UI responsiveness |
| G5-03 | 4 Statistics VMs (`.Result` ×15 after `WhenAll`) | sync-over-async anti-pattern (`PERFORMANCE §4`) | Replace with `await`; grep-test forbids `.Result` in `Animora.Desktop` | first E-phase that edits Desktop VMs |
| G5-04 | 32 `DateTime.UtcNow` in `Client.Services` | wall-clock in sync/licensing paths | Inject `IClock`/`IClinicClock`; grep-test (G3-03) | E04 jitter, E07 licence high-water |
| G5-05 | `ConfigureAwait(false)` uneven (49/359) in `Client.Services` | UI-thread capture in a background sync loop | Enforce in `Animora.Sync.*` + new sync files | E03–E06 (loop must not capture UI ctx) |
| G5-06 | transient VMs subscribe w/o unsubscribe (15 `+=` / 3 `-=`) | new sync-flyout/conflict VMs add more subscriptions | Make E04/E05/E06 VM additions `IDisposable` + unsubscribe, or weak events | memory leak on screen churn |
| G5-07 | `AllowNegativeStockOnSale = true` default (`LocalStoreOptions.cs:35`) | offline sale can go negative; server re-validation may reject on sync | Decide explicitly; if kept, E05/E06 must surface the negative-stock server rejection to the user | E05/E06 settlement UX |
| G5-08 | `App.axaml.cs` store-start error swallowed (`return false`, no log) | a sync/store init failure is equally invisible | Log `result.Error`; the sync loop must not start on a failed store | E04 lifecycle |
| G5-09 | server-side perf (D05/D09) | per-command intake write (idempotency + outbox) | batch intake writes in **one** round-trip per batch of 20; index server outbox `(Status, NextAttemptUtc)` | reconnect-drain throughput on weak node |
| G5-10 | `MembershipCache` (D13) | per-request RBAC DB hits on weak node | bounded LRU + explicit invalidation on `StaffMembership` change (mirror client `TenantScopedCache`) | weak-server RBAC cost |

**Performance highlights (most impactful first):** G2-02/G4-03 (indexable sync views + EXPLAIN gate) →
removes the delta-pull cliff; G5-09 (batched intake) → faster reconnect drain; G3-01 (persisted monotonic
high-water) → restart-safe cursor; G3-07 + OPT-07 (visible serial drain + gzip) → honest multi-day
backlog; G5-10 (membership LRU) → cheaper RBAC per request.

---

## 7. GOAL 6 — What a true 10/10 requires

The design is ~8/10; the realization is unproven. To make the **phases themselves** deliver a 10/10
system, the following must be *built and asserted*, not documented:

1. **One manifest, derived everywhere.** `SyncContractCatalog` is the single source of the replicated set;
   D08 controllers, E03 projection order, E04 catch-up groups, E05 policy coverage and E08 gates all
   derive from it at runtime. No literal count anywhere. (IMP-01; fixes G1-02, G1-05, G2 counts.)
2. **Safe-by-test dual-context store.** Shared cipher + key across both contexts, deterministic single
   startup migrate order, and `TwoContextMigrationTests` + `PRAGMA cipher_version` on `repl_*`. (G2-01,
   G3-02.)
3. **Encryption on before any replicated PII lands.** Enable at E07, K2-gate at E08, release-block at E09;
   `repl_*` + `ConflictQueueEntries` provably inside the encrypted container. (G3-02, G5-01.)
4. **Replication never starved.** Parked command ≠ transport-down, proven by a standing fitness test;
   a `SyncState.AttentionRequired` makes a parked money command visible. (G3-04, G3-06, G4-02.)
5. **Cross-device financial convergence closed end-to-end.** D08's finance read-model class propagated into
   E04/E05 with a two-device `TotalSpendMinor` convergence acceptance. (G3-05, G1-02.)
6. **Restart-safe monotonic cursor.** Persisted per-(tenant,table) high-water + "restart mid-burst" burst
   test folded into E08 Gate G1. (G3-01.)
7. **Projection ordering correctness.** Reverse-order (or proven order-independent) deletes + skip-cascade
   rule + tests. (G3-08.)
8. **Post-C11 families fully settled.** D06/D07 give `vaccination.treatment.*` and `finance.recurring.*`
   real server invariants + the `RecurringExpense → LedgerPostingHandler` posting rule; D05 settles an
   unknown command type as a deterministic `Rejected`. (G1-03, G4-02.)
9. **Weak-server gate standardized.** D13 EXPLAIN + time-budget gate on the minimum spec for the 5 heaviest
   sync/report queries; indexable views as a migration invariant. (G2-02, G4-03, G5-09, G5-10.)
10. **Code-debt closed before the dependent phase.** Clock injection, `ct` propagation, `.Result` removal,
    `ConfigureAwait`, VM unsubscribe, error logging — all landed *before* E03–E07, each with a grep/fitness
    test. (G5-02…G5-08.)
11. **Baseline-drift killed at the source.** A "baseline verified on `<date>`" header line in every D/E
    phase (migration head + `CommandTypes` count + project list) + the maintenance-drift ledger
    (`PROGRESS.md` already seeded it) as mandatory reading. (G4-01, IMP-02/IMP-06.)

Delivering all eleven moves the remaining program from a 7.5/10 *blueprint* to a 10/10 *built* system; the
blueprint already contains the right decisions for ~9 of them — the gap is execution + proof, not design.

---

## 8. Direct answers to the six questions

**Q1 — Is the planned server/sync system standard?**
**Mostly yes, with two deliberate non-standard choices.** The *shape* is industry-standard: CQRS command
pipeline + transactional outbox, idempotent consumer, append-only ledger with a single writer,
movement-based stock, RBAC + capability snapshots, and — for sync — the correct **intent≠state** split
(a command transport for writes, a Datasync replication surface for reads), a fail-closed conflict-policy
matrix, non-destructive pull, and contract versioning. The two non-standard decisions are (a) **two EF
contexts in one SQLite file** (E03) and (b) **serving the sync read through SQL views** (D08). Both are
*justified* but are the program's two sharpest edges and must be proven safe by test (G2-01, G2-02). The
licence format is a custom-but-reasonable signed envelope using first-party crypto only (G2-04). **Grade:
design standard 8.5/10; the deduction is realization risk and "documented-not-asserted" proofs.**

**Q2 — Does it follow precise programming principles?**
**In design, strongly; in current code, with known debts.** The *principles* are explicit and good:
dependencies inward only, one source of truth per rule, fail-closed security, append-only money, no custom
crypto, offline-first continuity, three separate security planes (auth → authorization → entitlement). The
*debts* that violate "precise" are real and catalogued: 32 raw `DateTime.UtcNow` in client code
(violates "client clock is never a sync authority"), 36 missing `CancellationToken`s, 15 `.Result`
sync-over-async, uneven `ConfigureAwait`, VM event-handler leaks, a swallowed store-start error, and
encryption off by default (G5-01…G5-08). None is in the finished ledger/pipeline core; all sit in client
services and must be closed *before* the sync phases that rely on them. **So: principled by design,
not-yet-precise in a handful of client-side spots that the D/E phases must clean up first.**

**Q3 — How does a customer go from purchase to an active, usable account?**
End-to-end journey (`ARCHITECTURE.md §13.4`; `PHASE-D10`, `PHASE-D11`, `PHASE-E07`):
1. **Buy** on the **public marketing website** (a *separate* vendor system, not in this codebase — it owns
   all payment; `STACK.md §17` — no payment SDK exists here).
2. The website POSTs **one idempotent provisioning intent** to `POST /api/onboarding/provision`
   (`PHASE-D10…md:117-138`), authorized as the `Onboarding` service identity. This calls the **same**
   `ISubscriptionOperations.CreateAsync` the human operator uses — one provisioning path, two callers.
3. The server creates the **tenant + a `Trial`/`Active` subscription + seats**, and the clinic owner user
   is created **disabled-pending-first-login** (the website never learns a password).
4. The clinic **downloads + installs the desktop** (hosted anywhere; signed installer, `§18`).
5. **First online login** (`PHASE-E07`): the desktop authenticates to its clinic server, registers its
   **device** (seat check — `SEAT_LIMIT_REACHED` if over limit), receives a **server-signed capability
   snapshot**, and requests a **signed licence** bound to that installation's sealed identity. The password
   is then **mirrored locally** (hashed) so the clinic can work **offline** afterward.
6. **Daily use is fully offline-capable**; the server is the authority for settlement and the source for
   replication, never a runtime dependency. The licence is re-checked offline against
   `max(wall-clock, monotonic-high-water)` so a wound-back clock cannot extend it; short validity (45 days,
   reissued) is the revocation mechanism (no online revocation channel).
**Caveat:** this entire journey is **unbuilt** (D10/D11/E07 are `⬜ Not started`); it is a correct plan,
not a shipped flow.

**Q4 — Is moving/updating the server and app easy?**
**App update: easy and designed for it.** Signed installer, rollback-capable (`§18`); the desktop keeps
running offline, so a bad server has no impact on clients.
**Server move/update: moderate, with two friction points.** In favor: one self-hosted node per clinic,
EF migrations are sequential and append-only, the command journal is idempotent (resubmits are safe), and
clients degrade to offline during any server downtime (SC-03 in the prior audit: 6 workstations keep
working, server restored from backup, clients resubmit with original `OperationId` → idempotent). Against:
(a) the **in-memory monotonic stamp high-water** makes a restore/restart delicate until it is persisted
(G3-01) — a naive restore can skip/duplicate a sync cursor; (b) the **Data Protection key ring** wraps the
licence signing keys — losing it means no licence can be issued (fail-closed by design, `PHASE-D11…md:59`),
so server migration **must carry the key ring**. **Net: app updates are easy; server migration is
straightforward *if* the key ring travels and the monotonic high-water is persisted first.**

**Q5 — Realistic, principle-based score?**
- **Completed backend (A–C): 9.0/10** — genuinely strong, keep its patterns.
- **Remaining design (D/E on paper): 8.0/10.**
- **Sync system overall: 7.5/10** (design 8.5, realization readiness 5.5).
- **Remaining program as a whole: 7.5/10.**
The ceiling is held down by: everything sync/server being **unbuilt**, risk **front-loaded** into the
hardest phases (E03–E06), and too many invariants being **documented rather than asserted**. The decisions
are right; the proof and execution are the gap. With the eleven items in §7 done, the realistic target is
**9.5–10/10**.

**Q6 — The whole backend, simply.**
Each clinic runs **one small server** (ASP.NET Core + SQL Server) on its own LAN. The clinic's computers
run the **desktop app**, which works **100% offline** on its own encrypted local database. When the
network is up, each desktop talks to its clinic server in **two separate channels**: it **sends its
actions** (invoices, visits…) as idempotent *commands* the server settles authoritatively, and it
**pulls shared data** (patients, catalog, calendar…) as *replicated read copies*. Money and stock are
**never** copied as state — only re-derived from settled commands — which keeps every device's books
consistent. Separately, **you (the vendor) run one central operator service** that sells subscriptions,
manages seats, and issues **signed licences** that each desktop verifies **offline** and that are **locked
to one machine** (anti-piracy). A customer buys on your website → the server provisions their clinic →
they install the app → first online login mirrors their login and licence locally → they work offline
forever after, syncing when they can. **Today: the local app half is done and excellent; the server half
and the sync half are fully planned but not yet built.**

---

## 9. Severity-ordered action index

| Rank | IDs | Direction | Owner phase |
|---|---|---|---|
| 1 | G3-02 / G5-01 | Enable encryption at E07, K2-gate E08, release-block E09; assert replica+conflict tables encrypted | E07/E08/E09 |
| 2 | G2-01 / G3-01 | Make the dual-context single-file store safe-by-test; persist the monotonic high-water | E03/D08 |
| 3 | G3-04 / G3-06 / G4-02 | Parked ≠ transport-down (no starvation); `AttentionRequired` state; unknown-command-type ⇒ deterministic `Rejected` | E04/E06/D05 |
| 4 | G3-05 / G1-02 | Propagate D08's finance read-model class into E04/E05; two-device convergence acceptance | D08/E04/E05 |
| 5 | G1-03 / G4-02 | Give post-C11 command families real server settlement + `RecurringExpense` ledger posting | D06/D07 |
| 6 | G3-08 | Reverse-order deletes + skip-cascade + tests for `ReplicaProjector` | E03 |
| 7 | G2-02 / G4-03 / G5-09 / G5-10 | Indexable sync views (migration invariant) + D13 EXPLAIN gate; batched intake; membership LRU | D08/D13 |
| 8 | G3-03 / G5-02…G5-08 | Close clock / `ct` / `.Result` / `ConfigureAwait` / leak / error-log debts before the dependent E-phase | E03–E07 |
| 9 | G1-01 / G1-04 / G1-06 / G4-01 / G4-04 | One migration-count source; structural path tests; kill literal ordinals; fitness tests over prose; one idempotency contract | PROGRESS/D01/D02 |
| 10 | G3-07 / G2-03 / G2-04 | Visible serial drain (+gzip); ADR the two write paths & idempotency schemes; cross-assembly licence canonicalization test | E06/D10/D11 |

---

*End of report. No build or test was run; every finding cites a real file/line or a roadmap/architecture
anchor and is traceable. This review is intentionally independent of — and complementary to —
`docs/BACKEND_SYNC_ARCHITECTURE_AUDIT_2026-10-08.md`; where that audit's findings have since been folded
into the phase text, they are marked **[TEXT-FIXED / UNBUILT]** here to reflect the current state.*
