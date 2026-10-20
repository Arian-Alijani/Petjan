# Animora — Full-System Audit & Problem-Report Plan

> **Purpose of THIS file.** A self-contained, resumable task list that drives a *code-level audit*
> of the whole Animora product. The deliverable is **problem reports** (findings), NOT fixes and NOT
> a fix plan. Each task inspects one layer/subsystem at the **source-code level**, hunts for the
> defect classes listed below, and **appends** its findings to a report file under `Plan/reports/`.
> A fresh model can resume from any unchecked task without re-exploring: every task carries the exact
> paths, the known baseline facts, the questions to answer, and the report section to write.
>
> **This is an AUDIT plan, not a remediation plan.** Do not change product code to "fix" what you
> find (except when a heavy fix is genuinely unavoidable mid-task — then commit it per the push rule).
> The output is written analysis + reproducible reasoning, in English, professional, detailed.

---

## 0. Ground truth (read once; do not re-derive)

**Product.** Animora = commercial **vendor-hosted (SaaS) multi-tenant** veterinary clinic management
+ accounting. Vendor runs ONE central server; customers register + buy a subscription on the public
site, download the **offline-first Avalonia/.NET 10 desktop app** (Persian/RTL, Jalali), log in, and
sync to the central server. "Self-hosted" in specs = vendor self-operates its server, never per-clinic
hosting. `AGENTS.md` and `ARCHITECTURE.md` are the design-intent source; `PERFORMANCE.md §N` is the
live perf standard that code comments cite.

**The .NET solution is the ONLY code under audit.** `Animora.sln`, 9 `src/` projects + `tests/`.
**NEVER open `Animora-UI/`** (throwaway HTML design reference — explicitly forbidden by the task).
Also skip `Database-Sampel/` (binary dump).

**Layer map (where each concern lives — use to target reads, no blind grep):**
```
src/Animora.Domain/              entities/aggregates/VOs per bounded context; ZERO project refs by design
src/Animora.Application/         command/query dispatch pipeline, behaviors, outbox, ports, security plane
src/Animora.Contracts/           DTO + query/service interfaces (Desktop↔services seam), 24 interfaces
src/Animora.Contracts.Wire/      server↔client transport DTOs (CommandEnvelope, SettlementResult, errors)
src/Animora.Persistence.Sqlite/  LOCAL encrypted EF store: DbContext, Migrations (001–033), repos, queries, seed, maintenance/backup/quarantine
src/Animora.Infrastructure.Local/ OS keystore/encryption, native SQLite assertion, clocks, local file store
src/Animora.Client.Services/     real impls of Contracts over Application+Persistence; + Sync/ orchestrator
src/Animora.Desktop/             Avalonia UI (17 screens + Statistics), DI root App.axaml.cs, Shell, Pages, Controls, Localization
src/Animora.Server/              ASP.NET Core 10 central host: Sync, Commands, Workers, Auth, Authorization, Subscriptions, Provisioning, Licensing, Admin, Operator, Files, Insight, Observability, Resilience, Signing, Hints, Handlers, Hosting
src/Animora.Persistence.PostgreSql/ server EF (Npgsql): Tenancy, Replication, Outbox, Identity, Commands, Subscriptions, Licensing, Operator, Files, migrations (Server 101–111, KeyRing 101)
src/Animora.Server.Licensing/    license issuance (ECDSA-P256 signer) + verification (envelope/payload/policy/capability gate)
src/Animora.Sync.Custom/         client-side sync engine: HttpStateSyncEngine, HttpCommandTransport, SqliteSyncApplyStore, ReplicaMaterializer, SettlementReconciler, ShadowResyncHandler, CommandIdRemapService, SyncFeedClient
src/Animora.Sync.Abstractions/   empty scaffold (ISyncEngine placeholder) — note if still empty
src/Animora.Web.Admin/           Domain-only admin RCL (SSR operator pages)
```

**Known baseline facts (do NOT re-discover; verify only if a finding depends on it):**
- Migrations: client SQLite `001`–`033` (physical head `033_OnlineEntitlement`); `020`,`022`,`024`–`033`
  use a **double** underscore before the ordinal (match on the `NNN` token). Server PG head `111_ServerOutbox`
  (101–111), KeyRing `101`. Several high-number server/PG migrations authored but **unapplied/unexecuted**
  (see PROGRESS "S07–S13 authored … build/test/smoke unexecuted").
- `CommandTypes` = **86** types (D06=39 non-financial, D07=47 money/stock); `ErrorCodes` = 161 codes.
  Live `CommandTypes.cs` / `ErrorCodes.cs` are the single source of truth — never trust a doc table.
- New aggregates beyond the original replicated set: `Clinical.TreatmentPlan` (+occurrences, vaccine
  catalog), `Finance.RecurringExpense` (+payment). New command families `vaccination.treatment.*`(4),
  `finance.recurring.*`(4). New desktop module **Statistics** has no server/replication surface yet.
- Local store has append-only audit sink, buffered/redacted rolling file logging, PII-free diagnostics
  bundle export, maintenance/backup/quarantine (`RebuildStockSnapshots`, `RebuildFinanceProjections`,
  `RebuildActivityFeed`), `FinanceProjectionWorker` draining the B02 outbox off the OLTP path.
- Server implementation status is **mixed**: S01–S06 done; **S07–S13 + licensing/admin S14–S20 "authored"
  but build/test/smoke UNEXECUTED** (per PROGRESS). Treat "authored but unverified" code as a
  first-class risk source for this audit.

**Pre-existing audit/diagnostic docs to mine (read targeted, don't re-run their work — cite + extend):**
- `docs/BACKEND_SYNC_ARCHITECTURE_AUDIT_2026-10-08.md` (sync design gaps: SYNC-C/H/M, SRV-M, PH, RM-H, CODE, OPT, IMP)
- `docs/CLIENT_SERVER_CLINIC_SYNC_DIAGNOSTIC_2026-10-14.md` (client↔server↔clinic sync diagnostic)
- `docs/SERVER_REVIEW_REPORT.md`, `backend-roadmap/PROGRESS.md` "Post-C11 drift" + "Maintenance drift ledger"
- Treat these as **prior art**: an item already recorded there is still re-validated against live code; new
  findings are added; resolved items are not re-flagged.

**Defect classes every task hunts for (the 13 audit targets):**
1. Performance problems + optimization opportunities (HIGH PRIORITY).
2. Bugs (functional defects in current code).
3. Latent bugs — correct-now, breaks-over-time (unbounded growth, time/clock, culture, overflow, ordering, retries).
4. Performance-improvement opportunities (indexes, allocations, N+1, async/sync, caching, batching).
5. Cross-layer inconsistencies (Domain↔App↔Persistence↔Contracts↔UI contracts/mappings drift).
6. Server↔client inconsistencies (wire DTOs, command/error codes, versioning, time, pagination).
7. Sync system — exhaustive push/pull failure scenarios (see Task S for the required scenario matrix).
8. Any defect of any kind.
9. Perf is emphasized — weight it heavily.
10. Multi-tenant clinic/account provisioning (each customer buys an account+clinic from the vendor).
11. The whole sales + accounting system.
12. Correct, complete, standards-compliant **data isolation** between tenants.
13. The backup system (local + server), reviewed in depth.

**Report finding format (use for EVERY finding, keep terse but complete):**
```
### <AREA>-<NN> <short title>
- Severity: Critical | High | Medium | Low
- Class: perf | bug | latent | inconsistency | sync | isolation | provisioning | sales-accounting | backup
- Location: <path>:<line-or-symbol>
- Evidence: <exact code fact — quote the decisive line(s)>
- Why it is a problem: <mechanism; for latent, the trigger/time horizon>
- Scenario / impact: <what breaks, for whom, when>
- Confidence: High | Medium (needs runtime check)
```
Keep each report file focused; put a severity-sorted summary table at the top of each report.

---

## PUSH RULE (LOCKED — non-negotiable)

After completing **each** task below: run `scripts/git/push.sh "audit(<area>): <what the report covers>"`.
If a heavy/large fix or large code artifact is produced mid-task, push it with a clear, sensible message.
**Not pushing = project death / failure.** Mark the task `[x]` only after its push `✓`.
Branch: operate on the current branch; `main` and `genspark_ai_developer` are already in sync (0/0).

---

## TASKS

> Each task = (1) read the listed code, (2) find defects of the 13 classes, (3) append a findings
> section to the named report under `Plan/reports/`, (4) push. Reports are English, detailed, pro.
> Create `Plan/reports/` in Task 1.

- [x] **T1 — Scaffold reports + audit index.** ✓ pushed
  Create `Plan/reports/` and `Plan/reports/00-INDEX.md` (an overview: product one-liner, layer map,
  the 13 audit targets, the finding format, and a table listing each report file T2–T13 with status
  `pending`). Copy the "Ground truth" essentials into `00-INDEX.md` so each report is navigable
  standalone. No code audit yet. **Push.**

- [x] **T2 — Domain layer audit → `Plan/reports/01-domain.md`.** ✓ pushed
  Scope: `src/Animora.Domain/**` only. Verify the "zero project references" invariant actually holds
  (inspect `Animora.Domain.csproj`). Audit aggregates/VOs for: invariant enforcement gaps, money as
  integer-minor vs float/decimal drift, mutable collections leaking, `DateTime` vs `DateTimeOffset`
  and UTC-vs-local correctness, equality/identity bugs, overflow on minor-unit math, missing guards
  that become bugs as data grows (latent). Focus aggregates: `Finance.*` (ledger/cheque/expense,
  `RecurringExpense`), `Sales.*`, `Clinical.TreatmentPlan`, `Inventory.*`. Write findings. **Push.**

- [x] **T3 — Application layer audit → `Plan/reports/02-application.md`.** ✓ pushed
  Scope: `src/Animora.Application/**`. Audit the command/query **dispatch pipeline + behaviors**
  (ordering, validation, security, correlation, idempotency), the **outbox** (`Outbox/IOutbox.cs` +
  impl), ports (`Ports/ISyncCursorStore.cs`, `Ports/IPeerFinanceSync.cs`), use cases. Hunt: missing
  `CancellationToken` propagation, `.Result`/`.Wait()` sync-over-async deadlock risk, behavior order
  letting unvalidated/unauthorized commands through, idempotency key gaps, outbox entries that can be
  lost or double-applied, exception swallowing. Cross-check `CommandTypes` count (86) vs registered
  handlers. Write findings. **Push.**

- [x] **T4 — Contracts + Wire consistency audit → `Plan/reports/03-contracts-wire.md`.** ✓ pushed
  Scope: `src/Animora.Contracts/**` + `src/Animora.Contracts.Wire/**`. Target audit class #5/#6.
  Verify Contracts references no `Animora.*` impl types. For each Wire DTO (CommandEnvelope,
  SettlementResult, error shapes) check it round-trips the server side (compare field-by-field with
  what `Animora.Server/Commands` + `Handlers` produce and what `Animora.Sync.Custom` consumes).
  Flag: error-code enums that differ between client and server, nullable/required mismatches,
  versioning fields absent, enum value drift, DTO fields the UI reads but no mapping fills. Write
  findings. **Push.**

- [x] **T5 — Local persistence (SQLite) audit → `Plan/reports/04-persistence-sqlite.md`.** ✓ pushed
  Scope: `src/Animora.Persistence.Sqlite/**`. Audit DbContext config, **migrations 001–033** (ordering
  integrity, data-only vs schema, missing indexes vs the query patterns), repositories, read queries,
  seed, maintenance. Perf focus (#1/#4/#9): N+1 queries, missing/overlapping indexes, `ToList()` before
  filter, client-eval, no-tracking usage, projection fact-table rebuild cost, unbounded result sets.
  Latent (#3): tables that grow unbounded (ActivityEvent, OutboxMessage, audit sink, SyncCursor,
  ClientSessions) with no retention/compaction. Confirm encryption-at-rest seam is applied to PII
  tables. Write findings. **Push.**

- [x] **T6 — Infrastructure.Local + Client.Services audit → `Plan/reports/05-infra-clientservices.md`.** ✓ pushed
  Scope: `src/Animora.Infrastructure.Local/**` + `src/Animora.Client.Services/**` (EXCLUDING `Sync/`,
  covered in T7). Audit: OS keystore/encryption usage, native SQLite assertion, clocks (single source
  of time? any `DateTime.Now`/`UtcNow` scattered = latent), local file store. In Client.Services audit
  the 24 service impls for: scope-per-call correctness, EF/SQL/rule leaks (must be none), caching
  (`TenantScopedCache` bounds, eviction, tenant-key correctness = isolation #12), debounce buffers
  (flush-on-complete correctness), mapping fidelity vs Contracts. Write findings. **Push.**

- [x] **T7 — Sync system deep audit → `Plan/reports/06-sync-system.md` (audit target #7, CRITICAL).** ✓ pushed
  Scope: `src/Animora.Sync.Custom/**` (client engine) + `src/Animora.Server/Sync`, `Commands`,
  `Workers`, `Handlers` (server side) + `Application/Outbox` + `Persistence.*/Replication`,`Outbox`,
  `Commands` + `Client.Services/Sync/**` (orchestrator/scheduler). First mine
  `docs/BACKEND_SYNC_ARCHITECTURE_AUDIT_2026-10-08.md` and `docs/CLIENT_SERVER_CLINIC_SYNC_DIAGNOSTIC_2026-10-14.md`,
  re-validate each listed gap against live code, then extend. **Produce an explicit push/pull failure
  scenario matrix**, each row: trigger → current behavior → defect → impact → confidence. Required
  scenarios to cover (at minimum):
  - Push: mid-batch crash; duplicate command replay (idempotency); command id remap collision
    (`CommandIdRemapService`); clock skew on command timestamps; offline queue overflow; partial
    outbox drain; settlement rejection reconciliation (`SettlementReconciler`).
  - Pull: page integrity failure (`IPageIntegrity`); snapshot signature mismatch (`Signing`); stale
    cursor / cursor regression; replica materialization mid-failure (`ReplicaMaterializer`,
    `SqliteSyncApplyStore` buffering); shadow resync trigger (`ShadowResyncHandler`); generation switch
    during apply (migration 032 ReplicaStore).
  - Conflict/ordering: two devices same tenant; server-authoritative settlement vs local provisional;
    out-of-order page apply; tombstone/delete propagation; new aggregates (TreatmentPlan,
    RecurringExpense) not in original replicated set — are they synced at all?
  - Auth/entitlement during sync: bearer expiry mid-sync (`BearerAuthHandler`), entitlement revoked
    (migration 033 OnlineEntitlement/SealedEntitlement).
  Write the full matrix + findings. **Push** (this is a large report — push even if partial, then continue).

- [x] **T8 — Server host, auth & authorization audit → `Plan/reports/07-server-auth.md`.** ✓ pushed
  Scope: `src/Animora.Server/{Hosting,Auth,Authorization,Resilience,Signing}` + `Server.Licensing/**`
  + `Persistence.PostgreSql/{Identity,KeyRing,Security}`. Audit: startup/readiness ordering, rate-limit
  policies (the 5 policies, `auth` policy name), bearer-token issuance/validation (opaque DP tokens,
  no JWT), RBAC stored-policy evaluation, tenant-binding middleware, ECDSA snapshot/license signing &
  verification (`LicenseVerifier`, `CapabilityGate`, key rotation). Hunt: authz checks that run after
  side effects, missing tenant scope on a policy, token expiry/refresh races, signature verification
  bypass, timing-unsafe compares. Flag that S04–S06 are "done" but higher phases unexecuted. **Push.**

- [x] **T9 — Multi-tenant provisioning + data isolation audit → `Plan/reports/08-tenancy-isolation.md`** ✓ pushed
      (audit targets #10 + #12, CRITICAL).**
  Scope: `src/Animora.Persistence.PostgreSql/{Tenancy,Provisioning,Context,Conventions}` +
  `src/Animora.Server/Provisioning` + tenant stamping/ignore-filter-ban interceptors +
  `TenantProvisioner`, `TenantDirectory`, `TenantRepository<T>`. This covers "جاسازی کلینیک‌ها و
  اکانت‌هایی که هر مشتری می‌خرد" (clinic/account provisioning) and the standards-compliant tenant data
  separation. Verify: every tenant-scoped entity is stamped + filtered; the "ignore-filter-ban"
  interceptor truly prevents un-scoped queries; provisioning creates a fully isolated tenant (no shared
  mutable defaults, no id reuse); cross-tenant leakage paths (admin/operator reads, Files, Insight,
  backup); client-side tenant key in `TenantScopedCache` matches server tenant. Enumerate every
  entity set and confirm isolation per standard. Write findings. **Push.**

- [x] **T10 — Sales + accounting system audit → `Plan/reports/09-sales-accounting.md`
      (audit target #11).** ✓ pushed
  Scope: Domain `Sales.*`/`Cashier`/`Finance.*`; Application finance use cases + outbox; Persistence
  `FinanceProjections` (DailyFinanceFact, ProductSalesFact, OwnerFinancialSnapshot), allocators
  (Invoice/Journal/Expense number), `FinanceProjectionWorker`/`Rebuilder`; Client.Services
  `Sales/Cashier/Cheques/Finance`; Contracts finance DTOs. This also covers "سیستم فروش و اکانتینگ".
  Hunt: double-posting, projection vs ledger divergence, number-allocator gaps/races (gaps or dup
  invoice numbers), rounding/minor-unit errors, cheque lifecycle state bugs, recurring-expense schedule
  bugs (latent), rebuild idempotency, insurer split correctness, settlement vs local provisional money.
  Write findings. **Push.**

- [x] **T11 — Backup system audit → `Plan/reports/10-backup.md` (audit target #13).** ✓ pushed
  Scope: local — `Persistence.Sqlite` maintenance/backup/quarantine + diagnostics bundle export;
  server — `Persistence.PostgreSql/Operator/DatabaseBackupService.cs`, `Server/Admin/AdminBackupAdapter.cs`,
  `AdminBackupEndpoints.cs`. Audit: backup completeness (does it capture encrypted store + keys needed
  to restore? consistency/point-in-time? WAL/checkpoint handling), restore path correctness, retention,
  backup of per-tenant data vs cross-tenant bleak in a shared backup, auth on backup endpoints, failure
  handling, PII in backups. Latent: backup growth, orphaned backups. Write findings. **Push.**

- [x] **T12 — Desktop UI/DI + cross-layer consistency audit → `Plan/reports/11-desktop-crosslayer.md`
      (audit target #5).** ✓ pushed
  Scope: `src/Animora.Desktop/{App.axaml.cs (DI root),Shell,Services,Pages (spot-check),Controls,Localization}`
  + the Statistics module (new, uncovered by AGENTS.md). Hunt: DI lifetime mistakes (singleton holding
  scoped, captured DbContext), ViewModels touching EF/SQL/concrete services (rule violation), blocking
  the UI thread, unbounded in-memory collections in VMs (latent), RTL/Persian-digit/Jalali conversion
  bugs, `DataTable`/virtualization perf on large lists (#1/#9), missing `CancellationToken` on nav,
  event-handler leaks (latent). Confirm every Contracts interface used by a VM has a real registration.
  Write findings. **Push.**

- [ ] **T13 — Cross-cutting synthesis + performance roll-up → `Plan/reports/12-synthesis.md`.**
  No new file reading beyond re-reading the T2–T12 reports. Produce: (a) a single severity-ranked
  master table of ALL findings across reports with stable IDs; (b) a dedicated **performance roll-up**
  (audit targets #1/#4/#9) grouping every perf finding with estimated impact + cheap vs structural;
  (c) a **server↔client inconsistency** roll-up (#6); (d) a **latent-bug timeline** (#3) ordered by how
  soon each triggers; (e) top-10 highest-risk items. Update `00-INDEX.md` statuses to `done`. **Push.**

- [ ] **T14 — Final cleanup (LAST TASK).**
  Delete **only** `Plan/audit-plan.md` (this file). Do NOT touch `Plan/reports/**`, roadmap files, or
  any other file. Then **push** with message `chore(audit): remove completed audit plan`.
```
rm Plan/audit-plan.md && scripts/git/push.sh "chore(audit): remove completed audit plan"
```

---

## Resumption note for a fresh session
Open `Plan/reports/00-INDEX.md` to see which reports are `done` vs `pending`, then find the first
unchecked `[ ]` task above and execute it using the scope + known-facts already written in that task —
no broad re-exploration needed. Honor the PUSH RULE after every task. Keep caveman/ultracave voice if
it is still active. Never open `Animora-UI/`.
