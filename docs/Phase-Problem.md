# Animora Backend & Server — Remaining-Phase Consistency Audit

**Date:** 2026-10-07
**Scope:** The remaining, not-yet-executed phases of the backend/server roadmap — Track D (`D01`–`D13`) and Track E (`E01`–`E09`) — plus the roadmap meta-files (`PROGRESS.md`, `ROADMAP.md`, `CONVENTIONS.md`, `00-REQUIREMENTS-LOCK.md`) and the architecture/stack specs (`ARCHITECTURE.md`, `STACK.md`) they depend on.
**Method:** Static reading and cross-referencing of phase files against the real source tree (`src/**`), the real migration set, the real `CommandTypes` catalog, and the architecture/stack rule tables. **No build and no test were run** (per directive; the codebase is known to build and test clean).
**Resolution status (2026-10-07): ✅ ALL FINDINGS RESOLVED** — every item below was fixed in the roadmap/docs in the follow-up session (static edits only; no build, no test). See the resolution table at the end of this file for the per-finding outcome and commit. Two statements in the original findings were found to be **factually wrong** and are corrected in place (§3.3 and §7.1) — marked "**Corrected**".
**Nature of this document:** A findings report only. It does **not** fix anything. Every item names the exact file(s), line anchors where useful, the concrete mismatch, the risk, and a recommended corrective framing. Items are ordered by severity.

---

## 0. Executive summary

The remaining phase files (`D01`–`E09`) are internally detailed and high quality, but they were **authored against a project snapshot frozen at phase `C11` (2026-09-30)**. Since then the project received substantial post-roadmap development — tracked in `Plan/*.md` and `CODE_AUDIT_REPORT_2026-10-07.md`, not in the roadmap — that added **12 new local migrations (`020`–`031`), 2 new domain aggregates, ~23 new command types, and a new "Statistics" desktop module**. None of this drift is reflected in the roadmap's "current state" or in the forward phase files. The result is a set of concrete, mechanical inconsistencies that will cause the remaining phases to fail their own guard tests if executed verbatim.

The drift clusters into five themes:

| # | Theme | Worst instance | Severity |
|---|-------|----------------|----------|
| A | **Client-side migration-number collisions** | `E03` `019`, `E05` `020`, `E07` `021` all collide with existing migrations; real head is `031` | 🔴 Critical |
| B | **Command-vocabulary drift** (server handler phases vs. real `CommandTypes`) | `D06`/`D07` enumerate ~63 command names, most of which do not exist; the real catalog has 86 | 🔴 Critical |
| C | **Stale "current state" / frozen roadmap bookkeeping** | `PROGRESS.md` line 41 claims "19 migrations (001–019)"; reality is 31 | 🟠 High |
| D | **File-path drift inside Track D/E** | `D13` targets `Server/Security/MembershipCache.cs`; `D04` creates it at `Server/Tenancy/MembershipCache.cs` | 🟠 High |
| E | **Replication-surface & stack gaps** | New aggregates `TreatmentPlan`/`RecurringExpense` absent from `D08`'s 22-entity set; client Datasync `10.1.2` pin unverified for .NET 10 | 🟠 High |

The remainder of this report details each finding.

---

## 1. 🔴 CRITICAL — Client-side migration numbers in `E03`/`E05`/`E07` collide with the real migration head

### 1.1 Reality
The local SQLite store (`AnimoraLocalDbContext`) migration directory `src/Animora.Persistence.Sqlite/Migrations/` currently contains migrations up to and including **`031_TreatmentPlans`**:

```
019_SyncCursor          (C11 — last roadmap-tracked migration)
020_OutboxActorProvenance
021_DashboardGeneration
022_DashboardWidgetIndexes
023_PatientsPerfIndexes
024_BoardingPaymentMode
025_ActivityResolvedDetail
026_VariantReorderScaled
027_PersianCategoryNames
028_PersianBoardingServiceTitles
029_RecurringExpenses
030_BoardingPrepaidRoom
031_TreatmentPlans        (current head)
```
(Ordinals `006` and `013` are skipped in the physical sequence — see §3.3.)

### 1.2 The phase assumptions (all wrong)

| Phase | File / anchors | Migration it wants to create | Target DbContext | Collides with |
|-------|----------------|------------------------------|------------------|---------------|
| `E03` | `PHASE-E03-isyncengine-datasync-client-adapter.md:65,87,148,173,198,274` | `019_ReplicaTables` | mixed (see §1.3) | existing `019_SyncCursor`; head is `031` |
| `E05` | `PHASE-E05-conflict-resolution-non-destructive-pull.md:68,95,150,168,196,261` | `020_ConflictQueue` | `AnimoraLocalDbContext` (`src/Animora.Persistence.Sqlite/Entities/ConflictQueueEntry.cs`, `Migrations/*`) | existing `020_OutboxActorProvenance` |
| `E07` | `PHASE-E07-online-auth-entitlement-device-lifecycle.md:176,200,221,286` | `021_OnlineIdentity` | `AnimoraLocalDbContext` | existing `021_DashboardGeneration` |

`E03` additionally mis-states the predecessor: `PHASE-E03...md:14` says *"C11 entry — `SyncCursor` + migration `018_SyncCursor`"*. The real C11 migration is **`019_SyncCursor`** (`PROGRESS.md:152-154` confirms the deliberate `018→019` bump because `018_UserPreferences` was already taken). So `E03` is one ordinal behind reality before the post-C11 drift is even counted.

### 1.3 Cross-DbContext impossibility inside `E03`
`E03` states two things that cannot both be true:
- `PHASE-E03...md:61-65`: migration `019_ReplicaTables` is generated **from the new `AnimoraOfflineDbContext`** (the Datasync `OfflineDbContext`-derived context, its own `repl_*` table set and its own migration history table).
- `PHASE-E03...md:87-90`: that same `019_ReplicaTables` migration **"extends C11's `SyncCursor`"** by adding columns (`EntityName`, `Cursor`, `LastPulledAtUtc`, `ConsecutiveFailures`, `LastErrorCode`).

`SyncCursor` is owned by `AnimoraLocalDbContext`, not `AnimoraOfflineDbContext`. A migration generated from the offline context **cannot** alter a table owned by the local context; the two contexts have separate model snapshots and separate `__EFMigrationsHistory` tables. This is a design-level contradiction, independent of the numbering collision.

### 1.4 Impact
Executing any of these phases verbatim produces either a duplicate migration id (EF refuses) or a migration authored against the wrong context. Every forward reference to these migration numbers (status lines, acceptance checks) is also wrong.

### 1.5 Recommended framing (for a later fix session, not this one)
- Renumber all **local-context** migrations forward of the real head: `E05 → 032_ConflictQueue`, `E07 → 033_OnlineIdentity` (verify the head again at execution time — more may land).
- For `E03`: split into (a) a `AnimoraOfflineDbContext` migration numbered **within the offline context's own sequence** (it starts fresh, so `001_ReplicaTables` under that context's migrations assembly), and (b) a separate **local-context** migration for the `SyncCursor` column additions, numbered after the then-current local head.
- Replace every hard-coded migration ordinal in `D`/`E` phase text with the rule *"next free ordinal on the owning context at execution time"*, exactly as the C-phases already learned to do (C06–C11 all record `Dn` deviations for the same reason).

---

## 2. 🔴 CRITICAL — `D06`/`D07` server-handler command vocabulary does not match the real `CommandTypes` catalog

### 2.1 Reality
`src/Animora.Contracts.Wire/Commands/CommandTypes.cs` defines **86** command-type string constants. The actual clinical/scheduling and money/stock vocabulary (verbatim) includes names such as:
`patients.owner.create`, `patients.pet.weight.add`, `reception.ticket.admit`, `reception.ticket.enter`, `reception.walkin.create`, `visits.visit.start`, `visits.visit.vitals`, `visits.rx.add`, `vaccination.record.administer`, `vaccination.treatment.create`, `vaccination.treatment.occurrence.done`, `scheduling.appointment.book`, `scheduling.bookingpatient.create`, `boarding.stay.transfer`, `inventory.receipt.post`, `inventory.variant.add`, `sales.invoice.line.return`, `sales.insurer.create`, `finance.cheque.transition`, `finance.expense.reverse`, `finance.recurring.add`, `finance.recurring.pay`, …

### 2.2 The phase assumptions (a different, mostly non-existent vocabulary)

`D06` (`PHASE-D06-server-command-handlers-i-clinical-scheduling.md:30-37`) enumerates 30 handlers keyed to command names like:
`patients.owner.register`, `pet.register`, `pet.weight.record`, `reception.queue.enqueue`, `queue.call`, `queue.admit`, `visit.open`, `visit.triage.record`, `visit.assign`, `vaccination.record`, `vaccination.schedule`, `appointment.noshow`, `boarding.stay.room.assign`, `stay.cancel`.

`D07` (`PHASE-D07-server-command-handlers-ii-money-stock.md:30-34`) enumerates 33 handlers keyed to:
`inventory.stock.receive`, `stock.transfer`, `product.price.set`, `product.deactivate`, `sales.invoice.draft.create`, `invoice.reverse`, `invoice.creditnote.issue`, `payment.reverse`, `insurer.claim.attach`, `invoice.from.handoff`, `cheques.*` (prefix), `finance.journal.adjust`, `expense.record`.

**Almost none of these strings exist in `CommandTypes.cs`.** Representative mismatches:

| `D06`/`D07` says | Real constant | Note |
|------------------|---------------|------|
| `patients.owner.register` | `patients.owner.create` | verb drift |
| `pet.weight.record` | `patients.pet.weight.add` | verb drift |
| `reception.queue.enqueue/call/admit/cancel` | `reception.ticket.admit/enter/leave/noshow/urgent`, `reception.walkin.create` | **whole vocabulary changed** |
| `visits.visit.open/triage.record/assign` | `visits.visit.start/vitals`, `visits.rx.add/remove` | verb drift + different surface |
| `vaccination.schedule` | *(none)* + 4 new `vaccination.treatment.*` | new aggregate, post-roadmap |
| `scheduling.appointment.noshow` | *(none)*; `scheduling.bookingpatient.create` exists instead | surface changed |
| `boarding.stay.room.assign/cancel` | `boarding.stay.transfer/reschedule`, `boarding.room.create/update`, `boarding.item.add/remove` | surface changed |
| `inventory.stock.receive/transfer`, `product.price.set` | `inventory.receipt.post`, `inventory.stock.set`, `inventory.variant.add`, `inventory.product.update` | surface changed |
| `sales.invoice.draft.create`, `invoice.creditnote.issue`, `payment.reverse` | `sales.invoice.create`, `sales.invoice.refund/void/line.return`, `sales.payment.register` | surface changed |
| `cheques.*` (prefix) | `finance.cheque.*` (prefix) | **prefix changed** |
| `finance.journal.adjust`, `expense.record` | `finance.expense.post/reverse`, `finance.recurring.*` | surface changed + new `recurring` family |

### 2.3 Why this breaks the phases by their own rules
Both phases contain an explicit guard against inventing names:
- `D06:38-39`: *"No new `CommandTypes` constant is invented here — if a name is missing, that is a C-phase defect to log in the decision log, not a new name to coin."*
- `D06:40-45`: a `PayloadContractTests` round-trips **each client command through its wire payload** and "fails on any field drift."
- `D07:35`: *"No new `CommandTypes` constant is coined here."*

Because the phases were authored against a **provisional** command vocabulary that the C-track implementation (and the post-C11 audits) diverged from, a faithful execution of `D06`/`D07` would have to either (a) invent handlers for command names that no client ever emits, or (b) immediately trip the "missing name = C-phase defect" clause for the majority of rows. The `PayloadContractTests` guard cannot pass against payload records named after non-existent commands.

### 2.4 Post-roadmap command families with **no** server handler coverage at all
These command families exist in the client catalog but are entirely absent from `D06`/`D07`'s handler inventories (they post-date C11):
- `vaccination.treatment.create/update/cancel/occurrence.done` (treatment plans; migration `031`, aggregate `src/Animora.Domain/Clinical/TreatmentPlan.cs`).
- `finance.recurring.add/edit/toggle/pay` (recurring expenses; migration `029`, aggregate `src/Animora.Domain/Finance/RecurringExpense.cs`).
- `boarding.stay.transfer/reschedule`, `boarding.room.create/update`, `boarding.item.remove`.
- `inventory.variant.add`, `inventory.category.create/rename/delete`, `inventory.stock.set`.
- `sales.insurer.create/update/delete`, `sales.invoice.line.return/owner.set`.

If the server must settle these commands (the whole point of Track D), `D06`/`D07` under-scope the handler set.

### 2.5 Recommended framing
- Rebuild the `D06`/`D07` handler tables **from `CommandTypes.cs` as the source of truth**, grouped by the real prefixes, and reconcile counts (clinical/scheduling vs money/stock) against the real split.
- Add explicit rows (or an ADR) for the post-C11 command families, deciding per family whether the server settles it or it is intentionally client-only.
- Keep the `PayloadContractTests` guard but regenerate the payload-record names from the real command strings.

---

## 3. 🟠 HIGH — Roadmap bookkeeping is frozen at C11 and no longer describes the codebase

### 3.1 `PROGRESS.md` "current state" understates the store by 12 migrations
`backend-roadmap/PROGRESS.md:41` states the local store has *"19 migrations (`001`–`019`)"* and enumerates them stopping at `018_UserPreferences`/`019_SyncCursor`. The real head is `031_TreatmentPlans` (§1.1). `PROGRESS.md:31` openly scopes the table to *"the state as of the last completed phase"* (C11), so this is a **documentation currency** problem rather than a lie — but every forward phase that reads `PROGRESS.md` to pick the "next free migration number" (the stated authoring method) inherits the wrong baseline, which is the root cause of §1.

### 3.2 The post-C11 work is invisible to the roadmap
`Plan/code-audit-2026-10-07-fixes.md`, `Plan/inventory-product-history-audit.md`, `Plan/patients-directory-perf.md`, `Plan/visits-petfile-audit.md`, and `CODE_AUDIT_REPORT_2026-10-07.md` document real shipped changes (new aggregates, migrations `020`–`031`, new command types, a "Statistics" desktop module, perf indexes). None of these appear in `PROGRESS.md`'s status table or "current state." `CODE_AUDIT_REPORT_2026-10-07.md:item 15` itself flags *"documentation drift (AGENTS.md does not cover the Statistics module)."* The roadmap has the same drift.

### 3.3 ~~Skipped migration ordinals~~ — **Corrected: no ordinals are skipped**
*Original claim (wrong):* physical ordinals `006` and `013` do not exist. **Verified false on 2026-10-07:** `src/Animora.Persistence.Sqlite/Migrations/` contains `20260926055039_006_CapabilitySnapshot.cs` and `20260928130000_013_InventorySnapshot.cs`; all ordinals `001`–`031` exist exactly once. The real source of confusion is different and is now documented in `PROGRESS.md` → "Post-C11 drift": (a) the *documented* "19 migrations" count was simply stale, and (b) files `020`, `022` and `024`–`031` carry a **double underscore** (`…__031_TreatmentPlans.cs`), so tooling must match the `NNN` token rather than a fixed filename pattern. Rule kept: numbering automation counts the **physical head**, never a documented count.

### 3.4 Recommended framing
- Add a short "Post-C11 drift" subsection to `PROGRESS.md` listing migrations `020`–`031`, the two new aggregates, the new command families, and the Statistics module, with pointers to the `Plan/*.md` audits — so the forward phases read an accurate baseline.
- Replace every "pick migration `0NN`" instruction in D/E with "pick the next free ordinal on the owning context at execution time (verify the physical head)."

---

## 4. 🟠 HIGH — File-path drift inside Track D/E (a later phase targets a path a prior phase did not create)

These are intra-roadmap inconsistencies — a phase edits a file at a path that the creating phase placed elsewhere. They do not depend on the post-C11 drift.

| Later phase (modify) | Path it uses | Creating phase | Path actually created | Verdict |
|----------------------|--------------|----------------|-----------------------|---------|
| `D13:154` | `src/Animora.Server/HealthEndpoints.cs` | `D01:98` | `src/Animora.Server/Health/HealthEndpoints.cs` | 🟠 **Mismatch** — missing `Health/` segment |
| `D13:155` | `src/Animora.Server/Security/MembershipCache.cs` | `D04:48,104` | `src/Animora.Server/Tenancy/MembershipCache.cs` | 🟠 **Mismatch** — `Security/` vs `Tenancy/` |
| `D13:156` | `src/Animora.Server/Security/EntitlementMiddleware.cs` | `D04:109` / `D11:190` | `src/Animora.Server/Security/EntitlementMiddleware.cs` | ✅ Consistent (listed to avoid a false alarm) |
| `E02:146` | `src/Animora.Persistence.SqlServer/ServerModel.cs` | `D02:107` | `src/Animora.Persistence.SqlServer/Context/ServerModel.cs` | 🟠 **Mismatch** — missing `Context/` segment |

### 4.1 Impact
A session executing `D13`/`E02` to the letter will create a **second** file at the wrong path (duplicate type) or fail to find the file it was told to modify. Low effort to fix in the phase text; high nuisance if missed at execution.

### 4.2 Non-issue verified (to prevent a false positive)
`E02:72` and `D07:80-81` reference `ServerModel.NonReplicatedProjectionTables`, while `D02:48` defines `ServerModel.NonReplicatedLocalTables`. These are **two intentionally distinct lists** — `D07:81` explicitly says the projection list is *"a new list"* separate from the local-tables list. No inconsistency; recorded here so a future reviewer does not re-flag it.

---

## 5. 🟠 HIGH — Rate-limit policy name mismatch between `E01` and `D13`

- `D13` defines the login/refresh limiter policy name as **`"auth"`**: `PHASE-D13...md:12` (*"the prepared `"auth"` policy name"*), `:87` (*"`.RequireRateLimiting("auth")` call sites are now activated"*), `:157` (`IdentityEndpoints.cs` … `.RequireRateLimiting("auth")`).
- `E01` references a policy named **`identity`**: `PHASE-E01...md:59` (*"Login is rate-limited with D13's `identity` policy"*) and `:148` (*"apply the `identity` rate-limit policy"*).

There is no `identity` policy; D13 names it `auth`. 🟠 **Mismatch.** `E01`'s admin login page would wire a non-existent policy. Fix: change `E01` to `auth` (or introduce a distinct admin policy in D13 and reference it consistently).

---

## 6. 🟠 HIGH — Sync/replication-surface gaps vs. the current domain

### 6.1 New aggregates missing from `D08`'s replicated set
`D08` (`PHASE-D08...md:35-50`) fixes the replicated surface at **22 entities** and scopes coverage to *"every `Animora.Domain` aggregate that **C01–C09** persist locally."* Two aggregates now exist that are **outside** that window:
- `src/Animora.Domain/Clinical/TreatmentPlan.cs` (migration `031`).
- `src/Animora.Domain/Finance/RecurringExpense.cs` (migration `029`).

Clinical history and finance reference data are both in `D08`'s replicated families, so the omission is material: either these aggregates must join the replicated set (bumping the "22", the `~34` server configurations in `D02:108`, and the read-only controller count) or `D08` must record an explicit DECISION that they are client-only. Today the phase silently predates them.

### 6.2 `MedicalHistoryEntry` is replicated read-only (`D08`) **and** locally written (`E05`)
`D08:41` replicates `MedicalHistoryEntry` as one of the 19 **read-only** entities (write attempts → `405 REPLICATION_READ_ONLY`, `D08:196`). `E05:67,167,196` adds a **client-written** column `MedicalHistoryEntry.IsConflictPreservedCopy` via local migration `020_ConflictQueue`. This is reconcilable (the conflict-preserved copy lives only in the local store and must never be pushed), but neither phase states the rule explicitly. Without a note, a reader may conclude `E05` writes to a read-only replicated entity. Recommend: `E05` should declare `IsConflictPreservedCopy` as local-only/never-replicated and `D08`/`E03`'s `repl_*` projection should exclude it.

### 6.3 `D02` "~34 configurations" is under-counted
`D02:108,123,139` plans *"~34 `<Entity>Configuration` classes"* mirroring the local column shapes. That count was taken against the C01–C09 aggregate set; the current domain (C10 + post-C11 aggregates) is larger. The "~" softens this, but the server schema (`102_ServerCoreSchema`) and the replicated set should be re-derived from the real domain at execution time, not from the frozen count.

---

## 7. 🟠 HIGH — Stack risk: client Datasync `10.1.2` pin and the residual-encryption CVE

### 7.1 Client Datasync package version — **Corrected: verified present and net10.0**
*Original concern:* the client `10.1.2` might not exist / target .NET 10. **Verified 2026-10-07 against the NuGet flat-container API:** `CommunityToolkit.Datasync.Client`, `.Server` and `.Server.EntityFrameworkCore` all publish `10.0.0, 10.0.1, 10.1.0, 10.1.1, 10.1.2` (plus `9.0.5`); the client `10.1.2` nuspec targets `net10.0` with `Microsoft.EntityFrameworkCore(.Sqlite/.Relational) 10.0.9`. The upstream issue cited (`…Server.OpenApi 9.0.5` vs ASP.NET Core 10) concerns a package this solution does not use. The pin stays; `E03` now carries the verification record plus an execution-time restore guard, and `STACK.md` was annotated.

### 7.2 Encryption is OFF with no phase owning production enablement
`CODE_AUDIT_REPORT_2026-10-07.md:item 1` (and `STACK.md:213,235,711,731`) establish that local DB encryption is **disabled by default** (`LocalStoreOptions.EncryptionEnabled = false`), the `MultipleCiphersEncryptionProvider` is *"compiled but unreachable … pinned in E08,"* and the encrypted native still carries **CVE-2025-6965** until the §12 **K2** gate freezes a safe native matrix. But:
- `E08` only **proves** the K2 lifecycle and is allowed to `SKIP` it if the cipher native is absent on the runner (`PHASE-E08...md:79-82`).
- `E09` (release engineering) does **not** flip encryption on for production, nor gate release on K2 passing.

Net effect: on the current plan the product can reach `E09` "release-gated" with **PII / medical / financial data stored in plaintext** and no phase that actually enables encryption for shipped builds. This is an architecture-vs-phase gap, not a wording nit: `ARCHITECTURE.md` (R3) and `STACK.md:57` ("encrypted at rest") promise encryption that no remaining phase delivers. Recommend an explicit owner phase (likely `E07`/`E08`) that sets `EncryptionEnabled = true` for release once K2 passes, and makes K2 a release blocker in `E09`.

---

## 8. 🟡 MEDIUM — `D06`/`D07` handler-count arithmetic is self-inconsistent even before the drift

Independent of §2, the two phases' own numbers do not cleanly reconcile:
- `D06:46` asserts *"identical for all **30**"* handlers. Its group table (`:32-36`) lists 5 + 8 + 7 + 5 + 5 = **30** ✅ (internally consistent, but against the stale vocabulary).
- `D07:26-34` groups Inventory 7 + Purchasing 4 + Sales 11 + Cashier/Cheques 8 + Finance 3 = **33** commands, while the files-table (summary) groups handlers by payload folder as 11 (Inventory) + 11 (Sales) + 11 (Finance). The folder grouping (11/11/11) only equals 33 if Purchasing folds into Inventory and Cashier/Cheques folds into Finance — which the phase never states. A reader cannot derive the folder layout from the command groups without this hidden mapping. Recommend stating the folder→group mapping explicitly.

---

## 9. 🟡 MEDIUM — Missing `C03` phase file (historical, low impact)

`backend-roadmap/phases/` jumps `PHASE-C02-reception-visits-triage.md` → `PHASE-C04-appointments-calendar-booking.md`; there is **no `PHASE-C03-*.md`**, yet `PROGRESS.md:103` records C03 ("Visit encounter & vaccination") as **Done** with a full deviation log, and `ROADMAP.md:151` counts Track C as 11 phases (C01–C11). The work shipped (migration `010_C03Schema`, the vaccination surface), so this is a **traceability** gap only: the C03 phase file was not preserved in the repo. Low impact because C03 is complete, but it breaks the "every phase has a self-contained file" invariant that D/E reviewers rely on. Recommend reconstructing a stub `PHASE-C03` index pointing at the `PROGRESS.md` C03 entry, or noting in `ROADMAP.md` that C03 was executed inline with C02's session.

---

## 10. ✅ Verified-consistent items (recorded to prevent re-flagging)

These were checked and found **correct**; listing them saves a future reviewer the re-verification cost:

1. **Server-side migrations `101`–`111`** (`D01`–`E02`) are on the **separate** `AnimoraServerDbContext` (SQL Server) and do **not** collide with the local `001`–`031` sequence. The `1xx` numbering is a deliberate, consistent server namespace.
2. **`WireContract.Version` stays `1`** across `D03:95,174` and `D08` — matches the real `src/Animora.Contracts.Wire/WireContract.cs:Version = 1`. `D03` correctly **adds** `MinSupportedVersion`/`SupportedVersions` (new members, not assumed-existing).
3. **A-RULE / T-RULE references in range:** phases cite up to `A-RULE 035` and `T-RULE 013`; `ARCHITECTURE.md` defines `A-RULE 001`–`035` and `STACK.md` defines `T-RULE 001`–`015`.
4. **§19 behavioral gates (A–V, no K/N) vs. STACK §12 PoC gates (G1, K2):** `E08`'s reference to "gate K / K2" is the **STACK §12** encryption PoC (`STACK.md:699-715`), correctly distinct from the `ARCHITECTURE.md §19` A–V set. Not an inconsistency.
5. **`NonReplicatedLocalTables` (D02) vs `NonReplicatedProjectionTables` (D07/E02)** are two intentionally distinct lists (`D07:81`), not a rename.
6. **`Animora.Web.Admin`** (`E01`) is a legitimate **new project** (`src/Animora.Web.Admin/…`, `E01:28-33,119,135`) referenced by `Animora.Server`; the "hosted inside `Animora.Server`" phrasing (`E01:21`) refers to the hosting process, not the project location.
7. **Placeholder projects exist as expected:** `Animora.Sync.Abstractions` (csproj + README only) and the solution already declares `Animora.Server.Tests` / `Animora.Sync.Tests` (with `PlaceholderTests.cs`). The not-yet-created product projects (`Animora.Server`, `Animora.Persistence.SqlServer`, `Animora.Sync.Datasync`, `Animora.Web.Admin`) are correctly absent — D/E create them.

---

## 11. Severity-ordered action index (for the fix session that follows this audit)

| Rank | Finding | Phase(s) | One-line fix direction |
|------|---------|----------|------------------------|
| 1 | §1 Migration-number collisions + cross-context migration | `E03`, `E05`, `E07` | Renumber to next free ordinal on the correct context; split E03's cross-context migration |
| 2 | §2 Command-vocabulary drift + missing families | `D06`, `D07` | Rebuild handler tables from `CommandTypes.cs`; add treatment/recurring/etc. rows or an ADR |
| 3 | §3 Frozen `PROGRESS.md` baseline | `PROGRESS.md` | Add a post-C11 drift section; change "pick migration 0NN" to "next free ordinal at execution" |
| 4 | §4 File-path drift | `D13`, `E02` | Fix `HealthEndpoints`/`MembershipCache`/`ServerModel` paths to match D01/D04/D02 |
| 5 | §5 Rate-limit policy name | `E01` | `identity` → `auth` |
| 6 | §6 Replication surface gaps | `D08`, `D02` | Add/decide `TreatmentPlan`/`RecurringExpense`; mark `IsConflictPreservedCopy` local-only; re-derive config count |
| 7 | §7.1 Datasync client pin | `E03`, `D08`, `STACK.md` | Verify a `net10.0` client `10.1.2` exists; ADR if not |
| 8 | §7.2 Encryption never enabled for release | `E07/E08/E09` | Add an owner phase that flips `EncryptionEnabled` post-K2; make K2 a release blocker |
| 9 | §8 Handler-count arithmetic | `D07` | State the command-group → payload-folder mapping explicitly |
| 10 | §9 Missing C03 phase file | `ROADMAP.md`/`phases/` | Reconstruct a C03 index stub or note inline execution |

---

## 12. "10/10" phase-quality improvements (beyond fixing the inconsistencies)

The phases are already thorough; these raise them to flawless-against-reality:

1. **Make every migration ordinal a formula, not a literal** across D/E ("`next free on <context>`"), and have each phase's acceptance step re-read the physical head. This structurally prevents the §1 class of bug from ever recurring.
2. **Treat `CommandTypes.cs` (and `ErrorCodes.cs`, 161 codes in `src/Animora.Domain/Results/ErrorCodes.cs`) as the single source of truth** for D06/D07/handlers. Add a one-line acceptance check per phase: "handler set == `CommandTypes` prefix slice; `PayloadContractTests` round-trips all N."
3. **Add a per-phase "Baseline verified on <date>" line** that records the migration head, `CommandTypes` count, and project list the phase was authored against — so the next drift is detectable by a single diff.
4. **`D08` should enumerate its replicated set from a committed manifest** (e.g., an attribute or a single list asserted by `SyncSurfaceTests`) rather than a prose table, so adding a domain aggregate forces a conscious include/exclude decision (closes §6.1 permanently).
5. **Give encryption an explicit lifecycle across E07→E09** (enable → K2 gate → release block) so the `STACK.md` "encrypted at rest" promise is owned by a phase (closes §7.2).
6. **Reconcile the roadmap with the `Plan/` audit track**: either fold the post-C11 audits into numbered maintenance phases, or add a standing "Maintenance drift ledger" the D/E phases must consult. This removes the root cause behind §2, §3, and §6.

---

*End of report. This document reports findings only; no source, migration, phase file, or configuration was modified in its production, and no build or test was executed.*

---

## 13. Resolution record (2026-10-07)

| § | Finding | Resolution | Files changed |
|---|---------|------------|---------------|
| 1 | Migration-number collisions; E03 cross-context migration | E03: `repl_*` tables → `001_ReplicaTables` on the **offline context's own sequence**; `SyncCursor` columns → separate local-context migration `SyncCursorReplicaColumns` at next free ordinal; `018_SyncCursor` typo → `019`. E05 → `032_ConflictQueue`, E07 → `033_OnlineIdentity`, each with a "re-verify the physical head at execution time" note | `PHASE-E03/E05/E07` |
| 2 | Command-vocabulary drift | D06 rebuilt from live `CommandTypes` (**39**: patients 7, reception 6, visits 6, vaccination 7, scheduling 5, boarding 8); D07 rebuilt (**47**: inventory 10, purchasing 5, sales 17, cashier 3, finance 12). 39 + 47 = 86. Treatment & recurring families included; stale names (`invoice.reverse`, `payment.reverse`, `cheques.*`, …) replaced; payload/handler file tables re-counted. The `ReversesInvoiceId` claim was checked against the live domain (absent) and annotated | `PHASE-D06`, `PHASE-D07` |
| 3 | Frozen `PROGRESS.md` baseline | New "Post-C11 drift" section: migrations `020`–`031` table, new aggregates/commands/Statistics module, binding **next-free-physical-ordinal** rule; status-table store row updated to 31 migrations | `PROGRESS.md` |
| 4 | File-path drift | D13 `Health/HealthEndpoints.cs` + `Tenancy/MembershipCache.cs`; E02 `Context/ServerModel.cs` | `PHASE-D13`, `PHASE-E02` |
| 5 | Rate-limit policy name | E01 `identity` → `auth` (D13 defines exactly `auth, commands, tables, files, operator`) | `PHASE-E01` |
| 6 | Replication-surface gaps | D08 DECISION: `TreatmentPlan`/`TreatmentOccurrence`/`VaccineCatalogItem` replicated **read-only**; `RecurringExpense`/`RecurringExpensePayment` **not replicated** → replicated set **25** (3 writable + 22 read-only), counts propagated to D08/E03; `IsConflictPreservedCopy` declared local-only and excluded from every contract; D02 coverage now "derived from the live domain" and `~34` → `~39` (indicative) | `PHASE-D08`, `PHASE-D02`, `PHASE-E03` |
| 7.1 | Datasync client pin | Verified on NuGet (see corrected §7.1); verification record + restore guard in E03; STACK.md annotated | `PHASE-E03`, `STACK.md` |
| 7.2 | Encryption never enabled | **E09 owns it**: `release-check.sh` requires K2 `PASS` per shipped RID (a `SKIPPED` K2 is a release blocker), and the release profile sets `EncryptionEnabled`/`K2Approved` (single declared `src/` exception); E08 wording changed so `SKIPPED` is dev-only | `PHASE-E09`, `PHASE-E08` |
| 8 | Handler-count arithmetic | Command-group → payload-folder mapping stated explicitly in D07 (15 + 17 + 15 = 47) | `PHASE-D07` |
| 9 | Missing C03 phase file | Historical stub `PHASE-C03-visit-encounter-vaccination.md` (facts only from `PROGRESS.md`) + `ROADMAP.md` row note | `phases/PHASE-C03-*`, `ROADMAP.md` |

**Not changed (by design):** no source, migration, test or build artefact was touched, and no build/test was run. Items in §10 (verified-consistent) and §12 (optional quality improvements) are unchanged.
