# Animora — Backend, Architecture & Sync Remaining-Work Audit

**Date:** 2026-10-08
**Author role:** Software Architect review
**Scope:** The *remaining* backend roadmap (Track **D** `D01`–`D13`, Track **E** `E01`–`E09`) plus the architecture/stack they depend on, with a deep focus on the **synchronization system** (`ARCHITECTURE.md` §8, phases `D08`, `E03`–`E06`). Tracks A–C (local core, application pipeline, mock retirement) are complete and are treated as the *baseline*, not re-audited line by line.
**Method:** Static reading and cross-referencing of `backend-roadmap/**`, `ARCHITECTURE.md`, `STACK.md`, `PERFORMANCE.md`, the real `src/**` tree, the live migration set, `CommandTypes.cs`, and the existing audit docs (`docs/Phase-Problem.md`, `CODE_AUDIT_REPORT_2026-10-07.md`). **No build and no test were run** (per directive; the codebase is known to build 0/0 and test clean through C11-scoped verification).
**Consumption note:** This file is optimized for an AI model — stable section anchors, one finding per block (ID · file:line · problem · fix · reason), severity-sorted, no prose padding.

---

## 0. How to read this document

| Section | Content |
|---|---|
| §1 | Executive summary + the single scorecard (incl. the sync-system grade) |
| §2 | System shape after the roadmap completes (deployment & commercial model) — answers the user's Q1/Q2 |
| §3 | 🔴 Critical sync-system problems (findings `SYNC-C-*`) |
| §4 | 🟠 High sync & roadmap-consistency problems (`SYNC-H-*`, `RM-H-*`) |
| §5 | 🟡 Medium design & robustness problems (`SYNC-M-*`, `SRV-M-*`) |
| §6 | Phase-by-phase inconsistencies in the remaining roadmap (`PH-*`) |
| §7 | Scenario stress analysis — what breaks under real conditions (`SC-*`) |
| §8 | Server-side optimization opportunities (`OPT-*`) |
| §9 | Code-level improvements carried into D/E (`CODE-*`) |
| §10 | System-improvement proposals (`IMP-*`) |
| §11 | Severity-ordered action index |
| §12 | Finding-ID cross-reference table |

**Finding block schema (used throughout):**
> **ID** · *severity* · **Where:** `path:line` · **What:** the concrete defect · **Fix:** the concrete change · **Why:** the risk / rationale.

---

## 1. Executive summary & scorecard

The completed part of Animora (Tracks A–C) is **mature and high quality**: a clean Domain←Application←Infrastructure layering, an append-only ledger with a single writer, movement-based stock with a proven cache, a frozen reflection-free dispatcher, idempotent command journaling, and a fully offline-first desktop running on a real encrypted-capable local SQLite store. That baseline is not the problem.

The **remaining** backend/server work (Tracks D/E) is where the risk concentrates, and it splits into two distinct classes:

1. **Roadmap-consistency drift** — the D/E phase files were authored against a snapshot frozen at `C11` (2026-09-30). The project then moved forward 12 migrations (`020`–`031`), 2 new aggregates, ~23 new command types and a new *Statistics* module **without roadmap tracking**. `docs/Phase-Problem.md` already catalogued this and its resolution table claims the phase *text* was patched (migration numbering, command vocabulary, file paths, replication set). This audit **re-verifies** those claims and finds them substantially applied in the phase files, but several *second-order* gaps remain open (see §4, §6).

2. **Sync-system design risk** — the synchronization architecture (`ARCHITECTURE.md` §8) is genuinely well-designed on paper (two independent transports, idempotent consumer, explicit settlement states, a conflict-policy matrix, non-destructive pull, contract versioning). But it is **100% unbuilt** — `Animora.Sync.Abstractions` is still an empty scaffold and every sync phase (`D08`, `E03`–`E06`) is `⬜ Not started`. The design quality is high; the *realization risk* is the entire remaining Track E. Several design decisions also have sharp edges that will bite at execution time (§3, §7).

### 1.1 Scorecard

| Dimension | Grade | One-line basis |
|---|---|---|
| **Completed backend (A–C)** | **9.0 / 10** | Clean layering, single ledger writer, proven caches, idempotent journal; only encryption-off and `ct`-propagation debts. |
| **Remaining roadmap (D/E) design** | **8.0 / 10** | Thorough, pre-made decisions, band-limited phases; but authored against a stale snapshot and carrying the open gaps in §4/§6. |
| **Roadmap internal consistency** | **7.0 / 10** | `Phase-Problem.md` fixed the worst collisions; residual second-order drift (encryption owner, replication-count propagation, C03 stub) remains. |
| **Sync system — design** | **8.5 / 10** | The §8 model is correct and non-naïve: intent≠state, idempotent consumer, non-destructive pull, fail-closed conflict matrix. |
| **Sync system — realization readiness** | **5.5 / 10** | Entirely unbuilt; the hardest phases (conflict, transport, crash-windows) are last; cross-context migration and projection-ordering risks are real. |
| **🔷 Sync system — overall grade** | **7.5 / 10** | *See §3/§7 — a great blueprint whose execution risk is front-loaded into E03–E06.* |

> **The sync-system grade is 7.5/10** (design strong, realization risk high and unproven). Rationale is detailed in §3, §7 and summarized in §4 of my answers at the end.

---

## 2. End-state system shape (answers the user's Q1 & Q2)

This section is derived strictly from `ARCHITECTURE.md` §3, §9.1, §13, §17 and the D/E phase goals — not assumed.

### 2.1 Topology (`ARCHITECTURE.md` §17.1)

```
Clinic LAN (no mandatory external connectivity):
  Desktop clients ──┐
  (future) web      ├──▶  ONE server node  = ASP.NET Core app
  workstations ─────┘        + SQL Server DB + file store + local CA
```

- **Per-clinic self-hosted single node.** `§9.1`: *"shared database, shared schema, tenant-discriminator"* at the data layer, but `§17.2` Production = *"one clinic's self-hosted node; no shared multi-tenant infrastructure crosses clinic-server boundaries at launch."* So the multi-tenant machinery exists in code, but the launch deployment is **one server per clinic**.
- **The desktop never needs the server to function** (offline-first is a standing principle). The server is the authority for *settlement* and the *source* for replication, not a runtime dependency for daily use.

### 2.2 Two separate commercial planes (`ARCHITECTURE.md` §13)

The architecture deliberately separates **Subscription** (billing) from **Licensing** (runtime entitlement):

```
Public website (future, separate system)
   → Subscription context  (plans / seats / billing / grace)      ← D10
   → Licensing context     (signed license, verified OFFLINE,      ← D11
                            bound to one sealed installation)
   → Core app reads entitlement READ-ONLY; never self-grants       ← E07
```

- **Subscription** = the commercial model (plans, seats, billing periods), managed by a **Platform Operator** through an operator-scoped admin web (`E01`/`E02`, `D10`). It can be *entirely manual/offline-managed*.
- **Licensing** = a **signed license artifact** verified **fully offline** against a rotation-aware key set, **bound to one installation's sealed identity** (a byte-copied license fails on a second machine — `§13.2` anti-cloning). No mandatory online license server (`R7`); revocation is approximated by short validity windows + reissuance.

### 2.3 Direct answers

- **Q1 — "Is the end state: one central server managing subscriptions + all app users + the app's own website + app download hosted elsewhere?"**
  **Mostly yes, with one correction.** After the roadmap completes there is:
  - a **Platform-Operator admin web** (`Animora.Web.Admin`, `E01`/`E02`) hosted **inside the Animora.Server process** that manages subscriptions/seats across tenants;
  - the **public marketing/purchase website** is explicitly a **separate system** (`§13.1`, `§17` — *"Future Public Website (separate system)"*), not part of this backend; it only hands a signed license to the Subscription context via an async, versioned, non-runtime-critical contract;
  - the **app installer/download** is a desktop delivery concern (`§18`), signed & rollback-capable, and can indeed be hosted anywhere — it is not coupled to the server.
  So "one central server for subscriptions + users" is accurate for the **operator plane**, but the **public website is intentionally a decoupled system**, and the per-clinic runtime server is a *different* node from any central operator surface.

- **Q2 — "Do I buy ONE main server and users just buy a subscription and use the app, or must each user rent their own server?"**
  **The launch model is one self-hosted server PER CLINIC (tenant), not one global server for all customers, and not per-end-user.** `§17.2` is explicit: Production is *"one clinic's self-hosted node."* Each **clinic** gets a server (bought/hosted by the clinic or by you on their behalf); the clinic's **staff users** only need the desktop app + a seat in that clinic's subscription. Individual *end users* never rent a server — the clinic does. The multi-tenant, shared-DB design means you *could* later host several clinics on one node (the deferred *Deployment Stamps* path, `§9.1` + `§5` deferred table), but that is explicitly **post-launch and gated by an ADR**. So:
  - **You / the operator** run the central **Subscription+Licensing+admin** surface.
  - **Each clinic** runs (or you host for them) **one application server node**.
  - **End users** buy a seat, install the desktop, and work — fully offline-capable, syncing to their clinic's node.

---

## 3. 🔴 CRITICAL — Sync-system problems

> These are the design/plan defects that will cause the sync track to fail its own guards or produce incorrect behavior if executed as written.

### SYNC-C-01 · 🔴 · Cross-context migration contradiction in E03 is "resolved in text" but still structurally fragile
- **Where:** `backend-roadmap/phases/PHASE-E03-isyncengine-datasync-client-adapter.md:69-108,164-165`; `docs/Phase-Problem.md:61-67,275`
- **What:** E03 introduces a **second EF context** (`AnimoraOfflineDbContext`, Datasync `OfflineDbContext`) living in the **same SQLite file** as `AnimoraLocalDbContext`, with its **own** `__EFMigrationsHistory` and its own migration sequence (`001_ReplicaTables`), *plus* a separate local-context migration (`SyncCursorReplicaColumns`) for the `SyncCursor` columns. The Phase-Problem resolution split these correctly in the *text*, but two EF `DbContext`s sharing one physical SQLite file with **two independent migration-history tables** is an operationally delicate arrangement: `MigrateAsync()` ordering at startup, WAL/locking interaction, and the A11 encryption interceptor must be applied identically to **both** contexts or the replica tables silently open unencrypted.
- **Fix:** (1) Make the A11 `SqliteConnectionInterceptor` + key provider explicitly shared by **both** contexts (one connection-string + one key source), asserted by a test that opens `repl_*` and reads `PRAGMA cipher_version`. (2) Define a single deterministic startup order: local-context migrate → offline-context migrate → projection, inside the existing `LocalStoreStartup` step list, not two independent initializers. (3) Add a `TwoContextMigrationTests` fact that both histories advance on a fresh file and neither context sees the other's tables in its model snapshot.
- **Why:** A dual-context single-file store is the single most error-prone decision in the whole sync track; "the text is consistent" does not make the runtime safe. An unencrypted replica table set would defeat the entire encryption story (also see `SYNC-C-02`).

### SYNC-C-02 · 🔴 · Encryption is OFF and no phase enables it for the replica tables either
- **Where:** `src/Animora.Persistence.Sqlite/Options/LocalStoreOptions.cs:13` (`EncryptionEnabled` default `false`); `CODE_AUDIT_REPORT_2026-10-07.md:52-61`; `docs/Phase-Problem.md:200-205,282`; `PHASE-E09-release-engineering.md` (K2 gate)
- **What:** Local DB encryption is disabled by default and `MultipleCiphersEncryptionProvider` is "compiled but unreachable." Phase-Problem §7.2 says **E09 now owns** flipping `EncryptionEnabled`/`K2Approved` for release and makes K2 a release blocker — but the **new `repl_*` replica tables (E03) and the `ConflictQueueEntries` table (E05)** now also store PII (owner names, phones), clinical text (preserved history copies) and server-synced demographics **in the same file**. If encryption enablement is still deferred to E09, then through all of E03–E08 the replicated PII sits in plaintext, and the replica tables are a *new* plaintext surface that did not exist at the time §7.2 was written.
- **Fix:** Pull the encryption-enablement decision **forward to E07** (the first phase with a real server/credential path), gate it on the K2 PoC, and make the E03/E05 acceptance criteria assert the replica + conflict tables are inside the encrypted container. Keep E09 as the *release blocker*, but do not let E03 ship a new plaintext PII surface.
- **Why:** `ARCHITECTURE.md` R3 / `STACK.md:57` promise "encrypted at rest"; shipping a replication cache of synced PII in plaintext is a direct violation and a GDPR/medical-data exposure, worse than the original single-DB concern because it now includes *server-authoritative* patient data.

### SYNC-C-03 · 🔴 · `ReplicaProjector` projection order + referential integrity is under-specified for deletes and partial pulls
- **Where:** `PHASE-E03...md:83-98,271-276` (`ProjectionOrder`, soft-delete, skip-on-pending, 200-row batches)
- **What:** `ProjectionOrder` lists parents before children for **inserts** (Owner before Pet, etc.), which is correct for FK creation. But the order is **wrong for deletes**: a soft-delete tombstone for an `Owner` that arrives before its `Pet` tombstones, applied in insert-order, will try to soft-delete a parent while children still reference it — and with `foreign_keys=ON` (`CONVENTIONS §5`) plus cascade behavior, a partial-batch failure mid-order leaves a **referentially inconsistent business store** between batches (the phase explicitly allows `PartiallyCompleted` with earlier batches committed). The skip-on-pending rule (`b`) also silently drops a child whose parent was skipped, with no stated cascade of the skip.
- **Fix:** (1) State that deletes/tombstones are applied in **reverse `ProjectionOrder`** (children first), or that soft-delete is order-independent because it only flips a flag and never violates an FK (verify the actual FK/cascade config — if children carry a non-nullable `OwnerId` FK, a parent soft-delete must *not* cascade). (2) Define the **skip-cascade rule**: if a parent aggregate is skipped (unsent intent), its dependent rows in the same pass are also skipped and reported, so the business store never gets a child pointing at a not-yet-projected parent. (3) Add `ReplicaProjectionTests` cases for "parent skipped → child skipped" and "tombstone order."
- **Why:** The phase's own acceptance says *"a `Pet` never lands before its `Owner`"* for inserts but says nothing about deletes or skip-cascades. Partial projection + FK-on is exactly where an offline-first store corrupts itself under a crash mid-pass.

### SYNC-C-04 · 🔴 · The "commands-before-replication, skip-pull-on-transport-failure" rule can starve replication indefinitely
- **Where:** `PHASE-E06...md:44-52,112-114` (pass order 1–6; skip steps 3–4 on any `Deferred`/transport failure; transport failure counts into `SyncCircuit`)
- **What:** Every pass submits commands first and **skips replication pull entirely** if command submission reports any failure. If a *single* command is permanently stuck in `Rejected`/`NeedsAttention` but the processor keeps *attempting* it (or a dependency is permanently `DEPENDENCY_UNSETTLED`), every pass fails step 2 and **replication pull never runs** — the local read store goes stale forever while the user sees "Pending," not "Offline." The circuit only opens on *transport* failure, not on a logically-stuck queue, so it will not rescue this state.
- **Fix:** Distinguish **transport failure** (skip pull — correct) from **per-command settlement failure** (`Rejected`/`NeedsAttention`/permanent `IntegrityError`): a command that is *parked* (no longer being submitted) must **not** block replication. Only an *in-flight transport* failure skips the pull. Add a `ReplicationNotStarvedByParkedCommandTests` fact: with one `NeedsAttention` command parked, replication pull still runs next pass.
- **Why:** The stated rationale ("pulling while local intent is stuck maximizes conflict surface") is valid only for *transport-down*, not for a *single parked command*. As written, one unrecoverable command freezes all inbound replication — a severe, silent data-staleness bug.

### SYNC-C-05 · 🔴 · No defined behavior when a replicated parent is financial-not-replicated but a read-model needs it
- **Where:** `PHASE-D08...md:48-51` (Invoice/Payment/StockMovement/… **not replicated**); `PHASE-E03...md:86-90` (projection set has no financial aggregates)
- **What:** Financial/stock facts are deliberately **not replicated** (settled by command only). The client therefore has **no replicated `Invoice`/`Payment`/`StockMovement` rows** — it only has what *its own* settled commands produced locally. But several read surfaces that C09/C10 built (owner total spend, dashboard revenue, finance facts) depend on the **full** financial picture, which on a multi-device clinic includes invoices *other devices* created. Nothing in D08/E03 defines how device B learns about device A's posted invoice: it is neither replicated (not in the set) nor a command B issued. The architecture says the client "learns them from server-computed read models" (`D08:49`), but **no phase builds a replicated financial read-model surface** — `E02` is operator reporting, not per-clinic device sync of finance facts.
- **Fix:** Add an explicit decision/phase: either (a) replicate a **server-computed finance read-model projection** (`DailyFinanceFact`/`OwnerFinancialSnapshot`) as read-only sync contracts so every device converges on the same numbers, or (b) document that multi-device financial read models are **server-online-only** (device B must be online and query the Business API to see device A's finance), and make the desktop degrade those widgets to "requires connection" offline. Today it silently shows only the local device's partial financials.
- **Why:** This is the quiet correctness hole of the "intent≠state" split: it is correct for *writes*, but the roadmap never closes the loop for *cross-device financial reads*. On a 2-device clinic the dashboards will disagree with no phase owning the reconciliation.

---

## 4. 🟠 HIGH — Sync & roadmap-consistency problems

### SYNC-H-01 · 🟠 · `MedicalHistoryEntry.IsConflictPreservedCopy` is read-only-replicated yet locally written — still only half-reconciled
- **Where:** `PHASE-D08...md:57-60` (read-only); `PHASE-E05...md:67-68,152,169-170,198` (adds local column via `032_ConflictQueue`); `docs/Phase-Problem.md:186-188,280`
- **What:** D08 replicates `MedicalHistoryEntry` **read-only**; E05 adds a **client-written** `IsConflictPreservedCopy` bool. Phase-Problem §6.2 flagged this and the resolution says E05 declares the column local-only and D08's contract omits it. That is applied in the phase *text*, but there is no **machine guard** that the projector never pushes a row where `IsConflictPreservedCopy = true`, nor that the `repl_MedicalHistoryEntry` table lacks the column. A future edit could reintroduce it to the contract silently.
- **Fix:** Add a `SyncSurfaceTests` fact: the `MedicalHistoryEntry` sync contract has no `IsConflictPreservedCopy` member (reflection), and `ReplicaProjector` never includes a preserved-copy row in a push set.
- **Why:** The rule is correct but unenforced; "documented, not asserted" is exactly how the migration-collision class of bug re-entered before.

### SYNC-H-02 · 🟠 · Cross-device financial read models absent from D08 but owner-total/dashboard depend on them (companion to SYNC-C-05)
- **Where:** `PROGRESS.md` C09/C10 entries (`OwnerFinancialSnapshot`, `DailyFinanceFact`, dashboard); `PHASE-D08...md:49-51`
- **What:** The C-track built `OwnerFinancialSnapshot.TotalSpendMinor` and dashboard revenue reading **local** fact tables. Those facts are rebuilt from the **local** ledger, which only contains this device's settled commands. D08 does not replicate them.
- **Fix:** Tie this to `SYNC-C-05`; decide replicate-read-model vs online-only and propagate the `TotalSpend`/dashboard semantics into the E-phase acceptance.
- **Why:** Without this, "owner total spend" differs per device — a visible financial inconsistency on any multi-seat clinic.

### SYNC-H-03 · 🟠 · `SyncCircuit` cannot represent "logically stuck" vs "server down"
- **Where:** `PHASE-E04...md:67-72`; `PHASE-E06...md:112-116`
- **What:** The circuit opens on 5 consecutive *pass* failures or a `CONTRACT_VERSION_UNSUPPORTED`. A pass that "succeeds at replication but has a parked command" is not a failure, so the UI shows `Pending`/`Synced` while a financial command is permanently `NeedsAttention`. Combined with `SYNC-C-04`, the user's mental model ("green = everything synced") is wrong.
- **Fix:** Add a distinct `SyncState.AttentionRequired` (or reuse the backlog counters surfaced in the topbar from E06) so a non-zero `NeedsAttentionCommands` is visually distinct from a healthy `Synced`.
- **Why:** The §8.3 sync-failure UX contract requires "who must act" to be *surfaced* — a parked integrity error hidden behind a green state violates it at the orchestration level, not just the notice level.

### RM-H-01 · 🟠 · `PROGRESS.md` status table still says "19 migrations (001–019)" in its narrative row
- **Where:** `PROGRESS.md:37-41` (store row text) vs `PROGRESS.md:52-66,82,277` (Post-C11 drift section says 31)
- **What:** The drift section (added 2026-10-07) correctly lists `020`–`031` and the binding "next-free-physical-ordinal" rule, but the **status-table store row** (line 41) still narrates "19 migrations (`001`–`019`)" with an inline enumeration stopping at `019`. Phase-Problem §3.1 flagged this; the resolution table (`:277`) claims the store row was "updated to 31 migrations," but the long narrative line 41 still reads as C11-era.
- **Fix:** Reconcile line 41's count with the drift section (state "31 migrations; see Post-C11 drift"), or trim the stale enumeration — one number, one source of truth.
- **Why:** Any forward phase that reads line 41 (not the drift section) to pick a migration number re-enters the §1 collision class; the whole point of the drift section is defeated if the headline row contradicts it.

### RM-H-02 · 🟠 · File-path drift fixes (D13/E02) are not re-verifiable without the server projects existing
- **Where:** `docs/Phase-Problem.md:154-159,278`; `PHASE-D13`, `PHASE-E02`, `PHASE-D01/D04/D02`
- **What:** The `HealthEndpoints.cs` (`Health/`), `MembershipCache.cs` (`Tenancy/`), `ServerModel.cs` (`Context/`) path mismatches were patched in the phase text. But `Animora.Server` / `Animora.Persistence.SqlServer` do **not exist yet** (D01/D02 create them), so the fix is a text-vs-text agreement, not a path-vs-reality check. A later author can still create the file at a third path.
- **Fix:** When D01/D02/D04 execute, add a `ServerLayoutTests` fact that asserts the canonical folders (`Health/`, `Tenancy/`, `Security/`, `Context/`) and that each cross-referencing phase targets an existing path.
- **Why:** Intra-roadmap path agreement is fragile until the files exist; lock it with a structural test the first time the server project is created.

### RM-H-03 · 🟠 · D06/D07 handler inventories were rebuilt from `CommandTypes` but the post-C11 families' *server settlement* semantics are undefined
- **Where:** `docs/Phase-Problem.md:118-124,276`; `PHASE-D06`/`PHASE-D07` (39 + 47 = 86)
- **What:** The command *vocabulary* was reconciled to 86 and the `treatment`/`recurring` families included. But *whether and how the server settles* `vaccination.treatment.*` and `finance.recurring.*` (which touch the two post-C11 aggregates `TreatmentPlan`, `RecurringExpense`) is only named, not designed — D06/D07 list them as rows without the invariant/ledger-posting detail the other families get.
- **Fix:** For each post-C11 family, add the server-side invariant + (for `finance.recurring.*`) the `LedgerPostingHandler` extension, since `RecurringExpense` emits expense-like events the single-writer ledger must consume.
- **Why:** A command the client can emit but the server cannot settle is a permanent `Rejected`/`PendingSettlement` — exactly the `SYNC-C-04` starvation trigger.

---

## 5. 🟡 MEDIUM — Design & robustness

### SYNC-M-01 · 🟡 · Progressive catch-up `MaxCatchUpPasses = 20` with 25 entities can under-converge
- **Where:** `PHASE-E04...md:33-35` (catch-up: 13 ref entities, then 9 transactional, repeat; cap 20 passes/wake)
- **What:** The head lists 13 reference + 9 transactional = 22 entities but D08 replicates **25** (post-C11 added `TreatmentPlan`/`TreatmentOccurrence`/`VaccineCatalogItem`). The catch-up narrowing text predates the 25-count and the pass cap may yield before a very-stale client finishes.
- **Fix:** Re-derive the catch-up groupings from `SyncContractCatalog.ProjectionOrder` (25), and make the pass cap a *budget* that resumes next wake (the phase says it does, but the group arithmetic is stale).
- **Why:** A client offline past the retention window is already the worst case; an under-counted catch-up plan risks never fully converging without a manual full-resync.

### SYNC-M-02 · 🟡 · Clock authority: 32 `DateTime.UtcNow` sites in Client.Services will leak into sync/licensing
- **Where:** `CODE_AUDIT_REPORT_2026-10-07.md:102-114` (32 sites, e.g. `VaccinationService.cs:17,106`); `CONVENTIONS §6.14` (client wall-clock is never a sync/licensing authority); `ARCHITECTURE §13.2` (monotonic high-water clock)
- **What:** The existing code already violates "client wall-clock is never a sync/licensing authority" in 32 places. E04 jitter, E07 license high-water and E06 crash windows all assume `IClock`/`IMonotonicClock`; mixing in raw `UtcNow` will produce subtle timezone/rollback bugs in exactly the security-critical paths.
- **Fix:** Close `CODE-AUDIT item 4` **before** E04/E07 (inject `IClock`/`IClinicClock`), and add a layering grep-test forbidding `DateTime.Now/UtcNow` in `Client.Services`/`Animora.Sync.*`.
- **Why:** A wound-back wall clock must not revive an expired capability (`§13.2` step 5) — but a stray `UtcNow` in the sync loop silently reintroduces the vulnerability.

### SYNC-M-03 · 🟡 · `ParallelOperations = 1` push is safe but makes a large offline backlog slow to drain
- **Where:** `PHASE-E03...md:112-114` (`ParallelOperations` default 1); `PHASE-E06...md:42-43` (batch 20 / 512 KB)
- **What:** Replication push and command submit are single-threaded by design (correct for ordering). For a clinic that was offline for days, the drain is serial. No backpressure/progress signal beyond the backlog counter.
- **Fix:** Keep serialism (correctness), but add a measured drain-rate expectation to E08's gate and a UI "draining N of M" progress from the existing backlog DTO so the serial drain is *honest*, not *invisible*.
- **Why:** Acceptable performance, but a multi-day backlog draining silently looks like a hang; surface it.

### SRV-M-01 · 🟡 · `MonotonicStampService` is in-memory per (tenant,table) — a server restart reseeds from `MAX(UpdatedAt)` and can collide with an in-flight cursor
- **Where:** `PHASE-D08...md:107-116` (in-memory high-water seeded from table max on first use)
- **What:** After a server restart, the stamp high-water reseeds from `MAX(UpdatedAt)`. If a client holds a delta cursor equal to that max and a new write lands in the same millisecond as the reseed boundary, the "+1 tick" guarantee resets relative to the client's last-seen value only if the reseed read the committed max *including* that write. There is a narrow restart window where monotonicity is only as strong as the read.
- **Fix:** Seed from `MAX(UpdatedAt) + 1 tick` **and** persist the high-water per (tenant,table) so a restart never rereads a racing max; make the G1 burst test also cover "restart mid-burst."
- **Why:** The whole G1 delta-correctness argument rests on strict monotonicity; an in-memory seed is one restart away from a skipped/duplicated row, which is the exact failure the service exists to prevent.

### SRV-M-02 · 🟡 · Datasync read via SQL **views** (`vw_Sync<Entity>`) + `IAccessControlProvider` data-view filter may not translate/perform on SQL Server
- **Where:** `PHASE-D08...md:63-91` (views `vw_Sync<Entity>` as EF entities; `GetDataView()` returns `e => e.TenantId == ...`)
- **What:** Mapping each sync contract to a **database view** and then layering a LINQ data-view predicate plus Datasync's OData `$filter` can produce heavy nested SQL on a *weak* server (`§16.1` weak-server principle). Views also complicate the `TenantId`+`UpdatedAt` index guarantee the readiness check asserts (indexes on a view vs base table).
- **Fix:** Confirm each `vw_Sync<Entity>` is **indexable** (SQL Server indexed/materialized view, or push the projection to a persisted computed set) and that the `IX_<Table>_TenantId_UpdatedAt` the delta cursor needs exists on the **base** table the view reads; add an EXPLAIN-plan check to D13's weak-server perf gate.
- **Why:** Delta pull over an unindexed view on a weak single-node SQL Server is the most likely server-side performance cliff.

---

## 6. Phase-by-phase remaining-roadmap inconsistencies

| ID | Phase | Where | Problem | Fix | Severity |
|---|---|---|---|---|---|
| PH-01 | E09/E07/E08 | `Phase-Problem §7.2`; `PHASE-E09` | Encryption enablement owned by E09 only; new E03/E05 PII tables ship plaintext until then | Pull enablement to E07, gate on K2, assert replica+conflict tables encrypted (see `SYNC-C-02`) | 🔴 |
| PH-02 | E03 | `PHASE-E03...md:69-108` | Dual-context single-file store; encryption/migration/locking across two `__EFMigrationsHistory` tables | Shared interceptor+key, deterministic migrate order, `TwoContextMigrationTests` (see `SYNC-C-01`) | 🔴 |
| PH-03 | E05 | `PHASE-E05...md:95-97` | `032_ConflictQueue` is "next free as of 2026-10-07 (head 031)" — still a literal; head may move | Keep the formula, re-read physical head at execution, never trust the parenthetical `032` | 🟠 |
| PH-04 | E07 | `PHASE-E07...md` (`033_OnlineIdentity`) | Same literal-ordinal risk after E05 lands another migration | Next-free-on-local-context at execution time | 🟠 |
| PH-05 | D08 | `PHASE-D08...md:36,135` | "25 entities" count now load-bearing across D08/E03/E04 catch-up groups | Single `SyncContractCatalog` manifest asserted by `SyncSurfaceTests`; derive all counts from it | 🟠 |
| PH-06 | D02 | `Phase-Problem §6.3,280` | "~34 → ~39 configurations" still indicative, not derived | Re-derive from the live domain at D02 execution; assert config count == replicated+local entity count | 🟠 |
| PH-07 | C03 | `Phase-Problem §9`; `ROADMAP.md:60` | C03 phase file is a reconstructed historical stub only | Acceptable (C03 is done); keep the stub + the `ROADMAP` note | 🟢 |
| PH-08 | E01 | `Phase-Problem §5,279` | `identity`→`auth` rate-limit policy name fixed in text | Verify at D13/E01 execution that only `auth, commands, tables, files, operator` exist | 🟢 |
| PH-09 | D07 | `Phase-Problem §8,283` | Handler-group→folder mapping (15+17+15=47) stated; `RecurringExpense` ledger path not detailed | Add the `finance.recurring.*` → `LedgerPostingHandler` extension (see `RM-H-03`) | 🟠 |
| PH-10 | D08 | `PHASE-D08...md:66-74` | Read-only enforcement is per-controller override returning 405 | Add the enumeration test (exists in text) AND a base-class default-deny so a new controller is read-only unless opted in | 🟡 |

---

## 7. Scenario stress analysis — what breaks under real conditions

> Each scenario is traced through the *planned* sync design to find the failure mode.

### SC-01 · Two devices edit the same owner's phone offline, then both reconnect
- **Path:** `Owner` is client-writable + replicated; `Phone` is a **demographic identity field** → `HumanReview` policy (`PHASE-E05...md:39`). Both pushes reach the server; the second gets a version mismatch on pull → `SyncConflict(VersionMismatch)` → `Quarantine`.
- **Verdict:** ✅ Handled by design — quarantine entry, both values visible, user picks one. **But** `SYNC-C-03` skip-cascade must be correct or a quarantined `Owner` could orphan a concurrently-pulled `Pet`.

### SC-02 · Device A posts an invoice offline; device B (online) renders the dashboard
- **Path:** Invoice is **not replicated** (`D08:49`). Device B has no row and no command. No phase pushes A's finance to B.
- **Verdict:** ❌ **Broken / undefined** — this is `SYNC-C-05`/`SYNC-H-02`. Device B's dashboard is silently wrong until it goes online and the (unbuilt) read-model sync fills the gap.

### SC-03 · Clinic server dies mid-day; 6 workstations keep working, server restored from backup 2 h later
- **Path:** Offline-first keeps all desktops writable (correct). On restore, `MonotonicStampService` reseeds from table max (`SRV-M-01`). Clients resubmit commands with original `OperationId` → `Duplicate`/`Settled` (idempotent — correct, `E06`). Replication resumes.
- **Verdict:** 🟡 Mostly handled, but the `SRV-M-01` restart-window and the `SYNC-C-04` command-starvation interaction (if any command was rejected pre-crash) must be closed, or some desktops show stale reads post-restore.

### SC-04 · One workstation offline 3 weeks, past retention window
- **Path:** Server pull returns the retention error → `SyncOrchestrator` raises `run-full-resync` notification, never self-triggers (`E05:128-134`). User runs full resync → refuses if unsent intent exists (`E03:130-135`) → audited → clear `repl_*` + cursors → pull → rebuild.
- **Verdict:** ✅ Well-designed. **Risk:** the catch-up/full-resync entity set is the stale 22 vs real 25 (`SYNC-M-01`); re-derive.

### SC-05 · Kill the desktop between command send and settlement response
- **Path:** `E06:117-123` — journal row is `PendingSettlement` before send, settlement applied in a separate transaction after; next pass resubmits same `OperationId` → `Duplicate` → exactly one financial effect. `CrashDuringSubmitTests` covers all three windows.
- **Verdict:** ✅ Correct by design — the strongest part of the sync plan.

### SC-06 · Server is a weak single node; a clinic has 50k historical rows to first-sync a new device
- **Path:** Delta pull over `vw_Sync<Entity>` views with per-page 50, serial entities, tenant data-view filter.
- **Verdict:** 🟡 `SRV-M-02` — view indexability + weak-server plan is the performance cliff; needs D13 EXPLAIN gate.

### SC-07 · A command exists on the client that the server has no handler for (post-C11 family)
- **Path:** `vaccination.treatment.*` / `finance.recurring.*` emitted by the client; if D06/D07 only *name* them without settlement logic (`RM-H-03`), the server rejects/defers → command parks → `SYNC-C-04` starves replication.
- **Verdict:** ❌ **Latent break** — two independent findings (`RM-H-03` + `SYNC-C-04`) compound into a frozen sync.

---

## 8. Server-side optimization opportunities

| ID | Where | Opportunity | Expected benefit |
|---|---|---|---|
| OPT-01 | `PHASE-D08` views | Use SQL Server **indexed views** or persisted projection tables for `vw_Sync<Entity>`, with a covering index on `(TenantId, UpdatedAt)` | Removes the weak-server delta-pull cliff (`SRV-M-02`); O(log n) cursor seeks |
| OPT-02 | `PHASE-D08` stamping | Persist `MonotonicStampService` high-water per (tenant,table) (`SRV-M-01`) | Restart-safe monotonic cursor; eliminates the skip/dup restart window |
| OPT-03 | `PHASE-D05/D09` | Batch command intake writes (idempotency record + outbox) in **one** round-trip per batch (20) instead of per-command | Fewer transactions on a weak node; higher reconnect-drain throughput |
| OPT-04 | `PHASE-D09` outbox | Index the outbox on `(Status, NextAttemptUtc)` (the local store already does this — mirror it server-side) | Cheap claim scans under load |
| OPT-05 | `PHASE-D13` | Add an EXPLAIN-plan / query-time budget gate for the 5 heaviest sync + report queries on the *minimum* server spec | Catches the weak-server regression before a clinic does |
| OPT-06 | `PHASE-D02` | Ensure every replicated base table has `IX_<Table>_TenantId_UpdatedAt` (the readiness check asserts presence — make it a *migration* invariant, not a runtime check) | Guarantees the cursor index exists before first pull |
| OPT-07 | `PHASE-E06` transport | Compress command batches (gzip) over LAN when payload > a threshold; 512 KB JSON batches compress well | Lower bandwidth on slow clinic LANs; faster drain |
| OPT-08 | `PHASE-D13` caching | The `MembershipCache` (tenant/role) should be a bounded LRU with explicit invalidation on `StaffMembership` change, mirroring the client's `TenantScopedCache` | Avoids per-request RBAC DB hits on the weak node |

---

## 9. Code-level improvements to carry into D/E

> These are existing debts (from `CODE_AUDIT_REPORT_2026-10-07.md`) that specifically endanger the sync/server tracks and should be closed *before or during* the phase that depends on them.

| ID | Where | Problem | Fix | Blocks |
|---|---|---|---|---|
| CODE-01 | `LocalStoreOptions.cs:13` | Encryption off by default | Enable in E07, K2-gate in E09, assert replica/conflict tables encrypted | `SYNC-C-02`, E03/E05 |
| CODE-02 | `src/Animora.Contracts/**` (36 `ct` of 196 Task sigs) | `CancellationToken` not propagated | Add `ct = default` to the new `ISyncDiagnosticsQuery`/`IConflictQueueService`/`IUnsettledWorkService` from the start; backfill existing | E04/E05/E06 UI responsiveness |
| CODE-03 | 4 Statistics VMs (`.Result` ×15) | `.Result` after `WhenAll` (anti-pattern) | Replace with `await`; add a grep-test forbidding `.Result` in Desktop | PERF §4 compliance before E-phase VM edits |
| CODE-04 | 32 `DateTime.UtcNow` in Client.Services | Wall-clock in sync/licensing paths | Inject `IClock`/`IClinicClock`; grep-test (`SYNC-M-02`) | E04 jitter, E07 license high-water |
| CODE-05 | `ConfigureAwait(false)` uneven in Client.Services (49/359) | UI-thread capture in new sync services | Enforce in `Animora.Sync.*` + new Client.Services sync files | E03–E06 (background loop must not capture UI ctx) |
| CODE-06 | transient VMs subscribe without unsubscribe (15 `+=` / 3 `-=`) | The new sync-flyout/conflict VMs add more subscriptions | Make the E04/E05/E06 VM additions `IDisposable` + unsubscribe; or weak events | memory leak on screen churn |
| CODE-07 | `AllowNegativeStockOnSale = true` default | Offline sale can go negative; on sync, a server re-validation may reject it | Decide explicitly; if kept, the `IdempotentCommandOnly` conflict path (E05) must surface the negative-stock rejection to the user | E05/E06 settlement UX |
| CODE-08 | `App.axaml.cs` store-start error swallowed (`return false`, no log) | A sync/store init failure will be equally invisible | Log `result.Error`; the sync loop must not start on a failed store | E04 lifecycle |

---

## 10. System-improvement proposals (beyond fixing defects)

| ID | Proposal | Rationale |
|---|---|---|
| IMP-01 | **Single `SyncContractCatalog` manifest as the one source of the replicated set**, asserted by `SyncSurfaceTests`, driving D08 controllers, E03 projection order, E04 catch-up groups and E05 policy coverage | Closes the "22 vs 25" / "replicate-by-accident" class permanently (`PH-05`, `Phase-Problem §12.4`) |
| IMP-02 | **A "maintenance drift ledger"** the D/E phases must consult (fold the `Plan/*.md` audits into it) | Removes the root cause of the entire `Phase-Problem.md` drift class; forward phases read an accurate baseline |
| IMP-03 | **A replicated server-computed finance read-model surface** (read-only sync contracts over `DailyFinanceFact`/`OwnerFinancialSnapshot`) | Closes `SYNC-C-05`/`SYNC-H-02` so multi-device dashboards converge |
| IMP-04 | **`SyncState.AttentionRequired`** distinct from `Synced`/`Pending`/`Offline`/`Error` | Makes a parked financial command visible per the §8.3 UX contract (`SYNC-H-03`) |
| IMP-05 | **Per-context encryption + migration test harness** run on every phase that touches the store | Makes the dual-context store (`SYNC-C-01`) safe-by-test, not safe-by-prose |
| IMP-06 | **"Baseline verified on <date>" line in every D/E phase** recording migration head, `CommandTypes` count, project list | A single diff detects the next drift (`Phase-Problem §12.3`) |
| IMP-07 | **Weak-server perf gate (EXPLAIN + time budget) in D13 covering the sync delta queries** | Turns `§16.1` "weak-server principle" into a tested property (`OPT-05`) |
| IMP-08 | **Replication starvation guard test** (parked command ≠ blocked pull) as a standing fitness function | Prevents `SYNC-C-04` regressions forever |

---

## 11. Severity-ordered action index

| Rank | ID(s) | One-line direction | Owner phase |
|---|---|---|---|
| 1 | SYNC-C-02 / PH-01 / CODE-01 | Enable encryption at E07, K2-gate at E09, assert replica+conflict tables encrypted | E07/E08/E09 |
| 2 | SYNC-C-01 / PH-02 / IMP-05 | Make the dual-context single-file store safe-by-test (shared cipher, ordered migrate) | E03 |
| 3 | SYNC-C-04 / SYNC-H-03 / IMP-04 / IMP-08 | Separate "parked command" from "transport down"; don't starve replication; surface attention state | E04/E06 |
| 4 | SYNC-C-05 / SYNC-H-02 / IMP-03 | Decide + build cross-device financial read-model sync (or document online-only) | D08/E02/new |
| 5 | SYNC-C-03 | Define delete-order + skip-cascade + FK behavior for projection | E03 |
| 6 | RM-H-03 / PH-09 / SC-07 | Give post-C11 command families real server settlement + ledger posting | D06/D07 |
| 7 | SRV-M-01 / SRV-M-02 / OPT-01 / OPT-02 | Persist monotonic high-water; index the sync views for the weak server | D08/D13 |
| 8 | RM-H-01 / PH-05 / PH-06 / IMP-01 / IMP-02 | One migration-count source; one replicated-set manifest; a drift ledger | PROGRESS/D02/D08 |
| 9 | CODE-02…CODE-08 | Close the ct/clock/ConfigureAwait/leak/error-swallow debts before the phase that relies on them | E03–E07 |
| 10 | SYNC-M-01 / SYNC-M-03 / OPT-03…08 | Catch-up re-derive; drain progress; server throughput optimizations | E04/D05/D09/D13 |

---

## 12. Finding-ID cross-reference

- **Critical (🔴):** SYNC-C-01, SYNC-C-02, SYNC-C-03, SYNC-C-04, SYNC-C-05, PH-01, PH-02
- **High (🟠):** SYNC-H-01, SYNC-H-02, SYNC-H-03, RM-H-01, RM-H-02, RM-H-03, PH-03, PH-04, PH-05, PH-06, PH-09
- **Medium (🟡):** SYNC-M-01, SYNC-M-02, SYNC-M-03, SRV-M-01, SRV-M-02, PH-10
- **Low (🟢):** PH-07, PH-08
- **Optimizations:** OPT-01…OPT-08
- **Code debts:** CODE-01…CODE-08
- **Scenarios:** SC-01…SC-07
- **Improvements:** IMP-01…IMP-08

---

## 13. Final verdict (condensed)

- The **completed backend** is excellent (9/10). Keep its patterns.
- The **remaining roadmap** is well-authored but front-loads all realization risk into the **unbuilt sync track**, and still carries second-order drift the `Phase-Problem.md` resolution did not fully close.
- The **sync system scores 7.5/10**: a correct, non-naïve blueprint (intent≠state, idempotent consumer, non-destructive pull, fail-closed conflict matrix, crash-window coverage) whose grade is held down by five concrete critical risks — plaintext replica PII (`SYNC-C-02`), the dual-context single-file store (`SYNC-C-01`), replication starvation by a parked command (`SYNC-C-04`), the undefined cross-device financial read path (`SYNC-C-05`), and projection delete/skip ordering (`SYNC-C-03`) — none of which is yet proven because the track is entirely unbuilt.

*End of report. No build or test was run; every finding cites a real file/line or a roadmap/architecture anchor and is traceable.*
