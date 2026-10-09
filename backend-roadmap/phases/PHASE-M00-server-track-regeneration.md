# Phase M00 — Server-track regeneration (meta-phase)

**Band:** M (meta) · **Track:** M (meta) · **Depends on:** C11
**Produces:** `phases/PHASE-S##-*.md` and `phases/PHASE-G##-*.md` (fresh server/sync/admin phase files)
**Runnable after this phase:** desktop offline yes · server required no — **M00 writes documentation
only: no C#, no migration, no package pin, no solution change.**

---

## ⚡ Zero-exploration contract (read this, then the one TODO task you own)

This is a **meta-phase**: it does not implement the server. Its job is to **author the remaining
server / sync / admin phase files** that Tracks D/E used to hold, regenerated from scratch against the
*current* PostgreSQL / .NET 10 architecture so no stale baseline leaks in. The old `D01`–`D13` /
`E01`–`E09` files were deleted on 2026-10-08 (planning-only, drifted, no code) — do not look for them.

**How M00 is executed — the only workflow that matters:**

1. This file carries a **TODO list** (`## TODO`). Each task is a checkbox `- [ ]`.
2. One execution session = **exactly one unchecked task**, top to bottom. Do its work, tick its box
   (`- [x]`), append its one-line evidence under it, update `PROGRESS.md`, commit **with the mandated
   push script** (see "Commit rule" below), **stop.**
3. Every task is written to be **resumable with zero memory of any other session**: it re-states what
   to read and what to produce. A later session never needs to know what an earlier one "meant".
4. M00 is **done** when every box in `## TODO` is `[x]`. At that point its last task flips
   `PROGRESS.md` → "Next phase" to **`S01`** and normal per-phase execution resumes on the regenerated
   files. Until then, "Next phase" stays `M00`.

**M00 never writes production code.** It only creates/edits `backend-roadmap/phases/*.md` (and ticks
its own boxes + the `PROGRESS.md` row). If a task tempts you to touch `src/**`, a migration, a package
pin or the `.sln`, you are out of scope — that belongs to a regenerated `S##`/`G##` phase, not here.

**Environment facts (so no session probes for them):**
- This repo is one .NET 10 solution; the backend roadmap governs `src/Animora.Domain`,
  `Animora.Application`, `Animora.Persistence.Sqlite`, `Animora.Infrastructure.Local`,
  `Animora.Client.Services`, `Animora.Sync.Abstractions`, plus the not-yet-created server projects.
- Build/test/run scripts: `scripts/sandbox/build.sh [fast|full]`, `scripts/sandbox/test.sh`,
  `scripts/sandbox/run.sh`. **M00 tasks do not run them** (no code changes); a regenerated phase file
  must cite them as *its* gates.
- `dotnet-ef` lives at `.sandbox/tools`; a regenerated phase must say "use it; never `dotnet tool
  install`". Server is PostgreSQL 18 + `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3 (pins land in the
  first server phase, not here). Forbidden tech (state it in every server phase): Redis, message
  broker, cloud IdP, cloud telemetry, CDN, EF Core 11, per-tenant database, hard-coded TLS version,
  Native AOT with EF Core.

---

## Commit rule — the ONE sanctioned way every M00 task commits & pushes (binding)

Every M00 task ends its session by committing through the **repo's push script**, never with raw
`git add/commit/push`. The script lives in the scripts directory at **`scripts/git/push.sh`**
(documented in `scripts/git/README.md`). It performs, in order and silently: `add -A` → `commit` →
`fetch` + `rebase --autostash` → `push -u`, printing exactly one `✓`/`✗` line.

**Invocation (from anywhere in the repo), used verbatim at the end of each task:**

```bash
scripts/git/push.sh -b genspark_ai_developer "docs(backend-roadmap): M00 <Task> — <summary>"
```

- Always pass branch `genspark_ai_developer` (the script switches/creates it) and a conventional commit
  message naming the task just finished.
- **Exit codes you must handle:** `0` = done; `2` = rebase conflict (repo left clean — resolve, then
  re-run the same command); `3` = no credentials (call the `setup_github_environment` tool once, then
  re-run the same command). Never fall back to hand-rolled `git` commands to work around a non-zero exit.
- This single rule replaces any ad-hoc `git` sequence mentioned elsewhere in this file: wherever a task
  says "commit", it means "run `scripts/git/push.sh` as above".

---

## Read before starting (every M00 task reads exactly this, nothing else)

A task re-reads this list each session — it is the whole context an M00 task needs:

- **This file** (`PHASE-M00-server-track-regeneration.md`) — the TODO list + the authoring rules below.
- `backend-roadmap/PROGRESS.md` — **top section only** (principles + "Current state" table + "Post-C11
  drift" + "Maintenance drift ledger" + "Next phase"). This is the single source of truth for the
  *current* project shape: migration head, `CommandTypes` count, which projects exist, replicated-set
  additions. **Never pick a migration number or command count from memory — read it here.**
- `backend-roadmap/ROADMAP.md` §0 (phase-weight rule), §1 (tracks), §2 (the D/E **scope inventory** the
  regenerated phases must cover), §3 (ordering rules), §5 (deferred list — do not regenerate these).
- `backend-roadmap/PHASE-AUTHORING-GUIDE.md` — the required sections + authoring checklist every
  regenerated phase file must satisfy. The worked example is `PHASE-B03a-*.md` (an already-shipped,
  reference-quality phase — read it once as the format template; never copy its scope).
- `backend-roadmap/00-REQUIREMENTS-LOCK.md` — the `R-B##` IDs each regenerated phase traces to.
- `backend-roadmap/CONVENTIONS.md` — only the `§` a task's "References pack facts" step tells it to
  condense into the phases it is authoring.
- `STACK.md` and `ARCHITECTURE.md` — **only the `§` the task names** (grep for the one section; never
  read end-to-end). The server/sync facts live in `STACK.md` §0, §5, §6, §11, §12 and `ARCHITECTURE.md`
  §8, §9, §PG, §16-CT–§18-CT, §17, §19.

> Do **not** open `src/**`, `Animora-UI/**`, `Database-Sampel/**`, or any other phase file while doing
> an M00 task — M00 authors *documentation* from the specs above. A regenerated phase file, by
> contrast, front-loads `src/**` facts; but that packing is done by reading the specs + the
> `PROGRESS.md` state table, not by browsing the tree during M00. If a concrete `src` signature is
> genuinely needed to make a regenerated phase self-contained, the authoring task will say so and scope
> the read to the single file named.

---

## Why this meta-phase exists (the one paragraph of rationale)

Tracks A–C delivered a complete, offline-first desktop on a real encrypted local SQLite backend
(domain + application pipeline + 24 real `Animora.Contracts` services, no mocks). The product is **not
shippable** until the vendor's central multi-tenant server, subscription/licensing, and two-way sync
exist (`ARCHITECTURE.md` §17: vendor-hosted SaaS; clinics connect, never self-host). That remaining
work was planned once as D/E but drifted across a SQL-Server→PostgreSQL revision and several
out-of-roadmap audit passes. Rather than patch 22 inconsistent files one by one, M00 regenerates them
in small, mutually-consistent batches against today's locked baseline — so each regenerated phase is
accurate on day one and the set stays coherent.

---

## Authoring rules — binding on EVERY regenerated `S##`/`G##` phase file

A task below authors phase files. Each file it writes MUST obey all of these (this is the quality bar;
a task is not "done" until its files pass this checklist):

1. **Weight (ROADMAP §0, non-negotiable).** Band **S** (1–4 files, ~150–450 net LOC, one concept) or
   **M** (5–10 files, ~450–900 net LOC, one cohesive slice). **No XL.** If a slice would exceed M,
   the authoring task splits it into two numbered phases — the phase *count* is free, per-phase weight
   is not. Declare the band on the `**Band:**` line.
2. **Self-contained (PHASE-AUTHORING-GUIDE).** Each file has, in order: `⚡ Zero-exploration contract`
   banner → `## Read before starting` (only the specs/`src` files that phase truly needs) → `## Goal`
   (one paragraph) → `§Reference pack` (verbatim real signatures of every existing type the phase
   calls — copied, not recalled) → `§Design decisions` (contradiction-free, real names, exact values)
   → `§Implementation plan` (numbered, dependency-ordered, exact commands with success signals) →
   `Files to create / modify` (real paths + one-line contents each) → `Contracts / schema added` →
   `Rules that apply here` (condensed CONVENTIONS `§` inline + cited) → `Out of scope` (with owning
   phase ID) → `Acceptance criteria` (mechanical: a `grep`, a test name, a script exit code) →
   `PROGRESS.md update` recipe.
3. **Baseline accuracy.** Every migration number, `CommandTypes` count, replicated-aggregate list and
   project name comes from `PROGRESS.md` ("Current state" + "Post-C11 drift") at authoring time, never
   from the deleted D/E text. Each file carries the mandated **baseline-verified line**
   (`> Baseline verified <date>: migration head <NNN>, CommandTypes <N>, projects <list>`) and the
   **next-free-ordinal rule** for any new migration (list physical files, take the next free ordinal on
   the owning `DbContext`; server migrations start at **101**, client stays on its own sequence).
4. **ID scheme.** Server phases are `S01, S02, …`; sync/integration/release phases are `G01, G02, …`.
   Slugs are kebab-case: `PHASE-S01-server-host-scaffold.md`. IDs are assigned densely in dependency
   order with no gaps. A `PHASE-S<NN>-*.md` "`Depends on:`" cites only lower-numbered `S`/`G` phases or
   completed A/B/C phases — never a forward dependency, never a deleted `D`/`E` ID.
5. **Stack lock.** Technology = exactly what `STACK.md` §0/§6/§11 fixes (PostgreSQL 18, Npgsql 10.0.3,
   ASP.NET Core 10, EF Core 10, ASP.NET Core Identity bearer/refresh, in-process SignalR, Blazor Web
   App Static-SSR-first, project-owned `IStateSyncEngine` with the PostgreSQL commit-safe tenant
   journal as the critical path; the Datasync toolkit is an optional qualified adapter only). The
   forbidden-tech list (§Environment facts above) is restated in every server phase. Never re-litigate
   or "upgrade" a pin.
6. **Offline invariant.** Every phase states "Runnable after: desktop offline yes" — introducing the
   server must never make the desktop require it (CONVENTIONS §11, Standing principle 1). Sync phases
   land a `Null*` fallback so the desktop still starts with no server.
7. **One introducer (ROADMAP §3.3).** A type/table/interface/error-code/migration is introduced by
   exactly one phase and referenced by exact name thereafter. When authoring a batch, cross-check that
   two sibling phases don't both introduce the same artifact.
8. **Scope inventory coverage.** Collectively, the regenerated `S##`/`G##` phases must cover **every**
   capability the ROADMAP §2 D/E inventory rows listed (server host, data layer + tenancy, identity,
   tenant-context authorization, command intake + idempotency, server command handlers I/II, PostgreSQL
   journal / immutable-batch replication surface, outbox/workers/resilience, subscription + operator
   API + onboarding intake, licensing + anti-piracy, files/valet-key transfer, server observability +
   weak-server performance, operator admin web I/II, `IStateSyncEngine` + HTTP adapter, sync
   orchestration + status, conflict resolution + non-destructive pull, command transport + settlement
   reconciliation, online auth + entitlement + device lifecycle, validation gates + performance proof,
   release engineering) — **and** the post-C11 drift (the 2 new replicated aggregates `TreatmentPlan` +
   `RecurringExpense`, the 86-type `CommandTypes`, the `Statistics` module having no server surface).
   Nothing from ROADMAP §5 "Explicitly deferred" is regenerated.
9. **English + tight prose.** Files are English, high-signal, low-token: concrete facts over narration;
   tables over paragraphs where they carry the same information.

---

## TODO — do the first unchecked task, tick it, update PROGRESS, stop

> **Ordering is binding.** Tasks run top-to-bottom. T1/T2 build the shared source brief the later
> authoring tasks reuse (so each authoring task is cheap and consistent). T3–T7 author the server `S##`
> phase files, and T8a–T8i author one `G##` sync/release phase each — all in dependency order. T9 is the
> final reconciliation that closes M00. **Each task below fully
> re-states its own inputs and outputs — a fresh session can execute any single unchecked task without
> reading the others.** When a task says "append to the source brief", it means
> `backend-roadmap/phases/M00-source-brief.md` (a scratch doc M00 owns; create it in T1).

### - [x] T1 — Lock the regeneration map (IDs, slugs, dependencies, bands, scope→phase matrix)
**Read:** this file (Authoring rules + Read-before list); `PROGRESS.md` top section; `ROADMAP.md`
§0/§1/§2/§3/§5; `00-REQUIREMENTS-LOCK.md`.
**Produce:** create `backend-roadmap/phases/M00-source-brief.md` and in it write the **regeneration
map**: a table with one row per regenerated phase — `New ID | Title | Band | Track(S/G) | Depends on |
R-B## satisfied | Covers which ROADMAP §2 D/E inventory row(s)`. Rules: assign `S01…S<n>` then
`G01…G<n>` in dependency order; every §2 D/E inventory capability maps to ≥1 new phase; no XL (split
any slice that would exceed band M, and show the split in the table); no forward deps; cite the real
`R-B##` IDs. Also record, in the same file, a short **"baseline snapshot"** block copied from
`PROGRESS.md` at this moment: current migration head ordinal, `CommandTypes` count, the exact list of
`src/` projects that exist, and the 2 post-C11 replicated aggregates — so later authoring tasks paste
these exact values instead of re-deriving them.
**Done when:** `M00-source-brief.md` exists with (a) the full phase map covering every §2 inventory row
and the post-C11 drift, (b) the baseline snapshot block. Tick the box with a one-line evidence note
(e.g. `S01–S13, G01–G09 mapped; head NNN, CommandTypes 86`).

**Evidence (T1):** 29 phases mapped (S01–S20, G01–G09; S03/S15/S20/G01 band S, rest M); baseline: client migration head 031, CommandTypes 88 live (PROGRESS prose '86' is stale), ErrorCodes 161; full coverage matrix in phases/M00-source-brief.md.

### - [x] T2 — Build the shared Reference-pack source brief (verbatim seams the server/sync phases call)
**Read:** this file; `M00-source-brief.md` (from T1); `PROGRESS.md` top section; the specific `src`
files named below (this is the **only** M00 task that reads `src/**`, and only to copy signatures into
the brief so the authoring tasks never re-open the tree).
**Produce:** append a **`## Reference pack (shared)`** section to `M00-source-brief.md` containing the
verbatim real signatures the server/sync phases will call: the wire seam
(`Animora.Contracts.Wire`: `CommandEnvelope`, `SettlementResult`/`SettlementOutcome`,
`CommandTypes`, `WireApiError`/`WireFieldError`, `WireContract.Version`); the settlement vocabulary
(`SettlementState`, `SettlementTransitions`); the application ports a server handler or the sync client
implements (`ICommandTransport`, `ICommandJournal`, `IOutbox`, `IEntitlementGate`, the dispatcher/
`IUnitOfWork` shapes); the `Result`/`ErrorCodes` shape + current count; and the empty
`Animora.Sync.Abstractions` scaffold. For each, paste the exact public members. Keep it scoped to what
the regenerated phases actually call (authoring guide: don't over-pack). Note next to each "introduced
by <phase>; reuse, don't redefine".
**Done when:** `M00-source-brief.md` has a `## Reference pack (shared)` whose every entry is a verbatim
copy (not a recollection) with its introducing phase noted. Tick + one-line evidence.


**Evidence (T2):** Reference pack (R1–R6: wire seam, CommandTypes rule, settlement vocabulary, application ports, Result/ErrorCodes, sync scaffold) copied verbatim into phases/M00-source-brief.md; live counts CommandTypes 88 / ErrorCodes 161.

### - [x] T3 — Author the server foundation phases (host → data layer + tenancy → identity → authz)
**Read:** this file; `M00-source-brief.md`; `STACK.md` §0/§5/§6/§11/§12; `ARCHITECTURE.md`
§PG/§16-CT–§18-CT/§9/§17/§19; the `R-B##` rows for server host, data/tenancy, identity, authorization.
**Produce:** the first batch of server phase files per the T1 map — the **foundation** slice: server
host scaffold; server data layer + tenancy (first server migration `101`); identity server
(ASP.NET Core Identity bearer/refresh); tenant-context + authorization policies. Author each to the
full Authoring-rules checklist. Split any into two if it would exceed band M.
**Done when:** each foundation phase file exists, passes the authoring checklist (self-contained,
baseline-verified line present, mechanical acceptance criteria, no forward dep), and no two introduce
the same artifact. Tick + list the file names created.


**Evidence (T3):** Authored PHASE-S01-server-host-scaffold, S02-server-data-layer-tenancy-i (migration 101), S03-server-data-layer-tenancy-ii, S04-identity-server, S05-tenant-context-authorization (+ naming lock Animora.Web.Admin / Persistence.PostgreSql / Sync.Custom in source brief); passes authoring checklist, no forward deps.

### - [x] T4 — Author the command-plane phases (intake + idempotency → handlers I → handlers II)
**Read:** this file; `M00-source-brief.md` (wire + settlement + `CommandTypes` seams); `ARCHITECTURE.md`
§8.3/§8.4/§8.6/§15; `STACK.md` §5.1; the `R-B##` rows for command intake and the two handler slices.
**Produce:** the server **command-plane** phases: business-API command intake + idempotency (server-side
settlement, `ON CONFLICT` dedupe, numeric→UUID version seam); server command handlers I (clinical &
scheduling); server command handlers II (money & stock) — the handler inventory derived from the live
`CommandTypes` (86), **not** from any deleted table; include the 2 post-C11 command families
(`vaccination.treatment.*`, `finance.recurring.*`). Split handlers if a single phase would exceed M.
**Done when:** each command-plane phase file exists and passes the checklist; the handler inventory is
stated as "derived from the live `CommandTypes.cs` at execution time" (never a frozen count); replicated
aggregates note `TreatmentPlan` + `RecurringExpense`. Tick + file names.


**Evidence (T4):** Authored PHASE-S06-command-intake-idempotency (wire v2, CommandIntake/CommandIdMap, +5 ErrorCodes, server chain profile), S07-server-handlers-clinical (incl. TreatmentPlan), S08-server-handlers-scheduling-boarding, S09-server-handlers-sales-cashier, S10-server-handlers-money-stock (incl. RecurringExpense); inventory derived from live CommandTypes.All (never frozen), coverage tests mandatory from S10.

### - [x] T5 — Author the replication + resilience phases (journal/immutable-batch → outbox/workers → files → observability/perf)
**Read:** this file; `M00-source-brief.md`; `ARCHITECTURE.md` §8/§9/§11.4.1/§16-CT–§18-CT/§PG;
`STACK.md` §6/§11/§12; the `R-B##` rows for replication, outbox/workers, files, observability.
**Produce:** the PostgreSQL **critical-state replication surface** (commit-safe tenant journal,
immutable 256-KiB batch export under REPEATABLE READ, monotonic position, GC crash atomicity — the
custom protocol, *not* a Datasync universal table surface); outbox + background workers + resilience;
files (valet-key + resumable transfer); server observability + weak-server performance (bounded pools,
`MinPoolSize=0`, OTLP loopback-only). Split as needed to hold band M.
**Done when:** each file exists and passes the checklist; the replication phase's acceptance criteria
are mechanical (ordering/atomicity invariants expressed as named tests), and it states the batch size,
offline window + retention, and that the surface is the project-owned journal. Tick + file names.


**Evidence (T5):** Authored PHASE-S11-journal-change-capture (commit-ordered ChangeSequence via deferred constraint trigger, SyncContractCatalog), S12-immutable-batch-export (RR frozen 256-KiB batches, retention 90d+30, crash-atomic GC), S13-outbox-workers-resilience (SKIP LOCKED fenced claim, breaker, bulkhead, SignalR hint), S17-files-valet-key, S18-observability-weak-server-performance (loopback-only OTLP, MinPoolSize=0, pool sum 12/20); PG-gated invariants are named tests mandatory in G07.

### - [x] T6 — Author the commerce phases (subscription + operator API + onboarding → licensing + anti-piracy)
**Read:** this file; `M00-source-brief.md`; `ARCHITECTURE.md` §17 (SaaS/vendor-hosted), §10.5/§10.6
(key material/TLS rationale); `STACK.md` §0; the `R-B##` rows for subscription, operator, licensing.
**Produce:** the **commerce** phases: subscription context + operator API + self-service onboarding
intake; licensing + anti-piracy (device fingerprint + sealed local entitlement cache + the offline
grace window). Keep them server-side; the client entitlement fallback is a later `G##` concern — cite
it in Out of scope with its owning phase ID. Split if over band M.
**Done when:** each file exists and passes the checklist; subscription/licensing types introduced once;
Out of scope points the client entitlement wiring at its `G##` owner. Tick + file names.

**Evidence (T6):** Authored PHASE-S14-subscription-operator-api (Plan/Subscription/BillingPeriod/SubscriptionEvent, operator identity + `/operator` API, single `ProvisioningEngine`, `ILicenseIssuanceScheduler` seam), S15-onboarding-intake (HMAC-signed idempotent async provisioning intent → same engine), S16-licensing-anti-piracy (`Animora.Server.Licensing` Verification/Issuance halves, signed installation-bound licence, fail-closed policy matrix + grace + high-water clock, grants, `ServerEntitlementGate`; client wiring → G06).

### - [x] T7 — Author the admin-web phases (operator panel core → insight seams)
**Read:** this file; `M00-source-brief.md`; `STACK.md` §0 (Blazor Web App — Static SSR default +
selective `InteractiveServer` islands, never global); `ARCHITECTURE.md` §17/§19; the `R-B##` rows for
the operator admin site; ROADMAP §5 (cross-tenant analytics is **deferred** — only the read-model seam
`ITenantInsightQuery` is in scope, the features are not).
**Produce:** the **admin web** phases: operator panel core (`Animora.AdminWeb` Blazor project, operator
auth, tenant/subscription/license management views) and insight seams (the read-model ports only). No
clinic-facing web client (ROADMAP §5 deferred). Split if over band M.
**Done when:** each file exists and passes the checklist; render-mode policy (Static SSR first) is
stated; deferred analytics features are explicitly out of scope with the §5 trigger noted. Tick + file
names.

**Evidence (T7):** Authored PHASE-S19-admin-web-operator-core (`Animora.Web.Admin` RCL, Static SSR, `__Host-` cookie auth, port/adapter split, zero islands) and PHASE-S20-admin-web-insight-seams (ops pages, the single `LiveStatusPanel` island, `ITenantInsightQuery` disabled-by-default seam, consent-ADR startup contract); S15/S20 re-banded M (weight honest), deferred analytics explicit in Out of scope.

> **T8 was split into one task per G-phase (T8a–T8i).** Each `G##` is now authored by its own
> self-contained, independently-resumable task — one execution session authors exactly one `G##` file
> (band permitting). The G01–G09 map (IDs, titles, bands, `Depends on`, `R-B##`, §2 coverage) is frozen
> in `M00-source-brief.md` §2 "Regeneration map"; every T8x task below **re-uses that row verbatim** and
> never re-derives it. All T8x tasks share the same read-set and the same binding rule set, re-stated in
> each so a fresh session can run any single one cold:
>
> - **Read (every T8x):** this file (Authoring rules + Commit rule + Read-before list); `M00-source-brief.md`
>   (§1 baseline snapshot, §2 map row for the `G##` being authored, `## Reference pack (shared)` — esp.
>   R6 the `Animora.Sync.Abstractions` scaffold, R4 the application ports, R1 the wire seam); then **only
>   the `§` named in that task** of `ARCHITECTURE.md` / `STACK.md`, plus the `R-B##` rows the map row cites.
> - **Offline invariant (every T8x, non-negotiable):** the authored phase MUST state "Runnable after:
>   desktop offline yes" and keep the desktop startable with no server via its `Null*`/local-cache
>   fallback (CONVENTIONS §11, Standing principle 1). A `G##` that removes a `Null*` shim replaces it with
>   a real impl **and** a documented offline fallback — never a hard server dependency.
> - **One introducer (every T8x):** a type/interface/error-code introduced by this `G##` is introduced
>   here once and referenced by exact name thereafter; cross-check the sibling `G##` rows in the map so
>   two phases don't both introduce the same artifact.
> - **Checklist + commit (every T8x):** author to the full Authoring-rules checklist (§ above); then
>   commit via the **Commit rule** push script (`scripts/git/push.sh -b genspark_ai_developer "docs(backend-roadmap): M00 T8x — <summary>"`).
> - **Done when (every T8x):** the single `PHASE-G<NN>-*.md` file exists, passes the authoring checklist
>   (self-contained, baseline-verified line present, mechanical acceptance criteria, no forward dep, offline
>   line present), introduces no artifact a sibling already owns. Tick the box + one-line evidence naming
>   the file. Then **stop** (next session picks up the next unchecked T8x).

### - [x] T8a — Author G01 (sync engine abstractions + `NullSyncEngine` fallback)
**Map row (verbatim from brief §2):** `G01 | Animora.Sync.Abstractions — IStateSyncEngine/ISyncEngine,
SyncScope, SyncResult, NullSyncEngine fallback + DI | band S | Track G | Depends on: S12 | R-B03, R-B13 |
covers E03 (half 1)`.
**Read (in addition to the shared read-set above):** `ARCHITECTURE.md` §8/§16-CT; the `R-B03`, `R-B13`
rows.
**Produce:** `PHASE-G01-sync-engine-abstractions.md` — fills the empty `Animora.Sync.Abstractions`
scaffold (brief Reference pack R6): `IStateSyncEngine`/`ISyncEngine`, `SyncScope`, `SyncResult`, the
`NullSyncEngine` offline fallback, and DI registration. Client references **no** PostgreSQL driver and
**no** server project. Band **S** (per map).
**Done when:** `PHASE-G01-sync-engine-abstractions.md` exists and passes the shared Done-when checklist;
`NullSyncEngine` keeps the desktop offline-runnable. Tick + file name.

**Evidence (T8a):** Authored PHASE-G01-sync-engine-abstractions (band S): `IStateSyncEngine` (single name; `ISyncEngine` doc alias corrected), `SyncScope`/`SyncOutcome`/`SyncResult`, `NullSyncEngine` offline fallback, `AddAnimoraSyncEngine` TryAdd DI; Sync.Abstractions refs Domain + Contracts.Wire only (grep-checked).

### - [x] T8b — Author G02 (custom immutable-batch HTTP adapter)
**Map row (verbatim):** `G02 | Custom immutable-batch HTTP adapter (client refs no PostgreSQL driver, no
server project) | band M | Track G | Depends on: G01 | R-B03, R-B13, R-B31 | covers E03 (half 2)`.
**Read (plus shared):** `ARCHITECTURE.md` §16-CT–§18-CT/§PG (batch wire shape); `STACK.md` §6/§11; the
`R-B03`, `R-B13`, `R-B31` rows.
**Produce:** `PHASE-G02-immutable-batch-http-adapter.md` — the client-side `Animora.Sync.Custom` adapter
that consumes the server's immutable 256-KiB batch surface (introduced by S12) over HTTP. References
`Sync.Abstractions` + `Application` + `Persistence.Sqlite` + `Contracts.Wire`; **no** Npgsql, **no**
server-project reference (brief Reference pack R6 project table).
**Done when:** `PHASE-G02-immutable-batch-http-adapter.md` exists and passes the checklist; the "no
PostgreSQL driver / no server ref" constraint is a mechanical acceptance criterion (grep the csproj).
Tick + file name.

**Evidence (T8b):** Authored PHASE-G02-immutable-batch-http-adapter (band M): new `Animora.Sync.Custom` project (4 ProjectRefs only — Sync.Abstractions/Application/Persistence.Sqlite/Contracts.Wire, no Npgsql/server ref, grep-gated), `HttpStateSyncEngine` consuming the S12 `/sync/manifest|batches|pages|snapshots` surface with page-hash + content-hash verification, resumable `If-None-Match`/304, `ISyncApplyStore`/`ISyncApplySession` seam + `BufferingSyncApplyStore` default (real atomic apply → G04), conditional DI (Http engine wins only when `ServerEndpointOptions.IsConfigured`, else G01 `NullSyncEngine` stays), +4 ErrorCodes (Sync transport), BatchNotFound/ResyncRequired reused from S12; pull-only (no PushAsync).

### - [x] T8c — Author G03 (sync orchestration & status)
**Map row (verbatim):** `G03 | Sync orchestration & status (scheduler, SyncCursor integration, status
surfaced to ISyncStatusService) | band M | Track G | Depends on: G02 | R-B03 | covers E04`.
**Read (plus shared):** `ARCHITECTURE.md` §8; `STACK.md` §4/§6; the `R-B03` row.
**Produce:** `PHASE-G03-sync-orchestration-status.md` — the scheduler that drives the G02 adapter,
`SyncCursor` integration, and status surfaced to `ISyncStatusService`. Offline fallback explicit.
**Done when:** `PHASE-G03-sync-orchestration-status.md` exists and passes the checklist. Tick + file name.

**Evidence (T8c):** Authored PHASE-G03-sync-orchestration-status (band M): `SyncOrchestrator` (PullOnce/PullAll, advances `SyncCursor` via `ISyncCursorStore` only on Applied/UpToDate), `SyncSchedulerHostedService` (dormant when unconfigured, jitter ±20% + capped backoff, `IDelayProvider`/`TimeProvider` for deterministic tests — no Thread.Sleep), `ISyncResyncHandler` seam + `NullSyncResyncHandler` (real full-resync → G04), `LocalSyncStatusService` extended to combine state-pull outcome + command backlog into one `SyncState` (two passes, never merged), `AddAnimoraSyncOrchestration` DI; no HTTP in Client.Services (grep-gated), no migration/ErrorCode.

### - [x] T8d — Author G04 (conflict resolution & non-destructive pull)
**Map row (verbatim):** `G04 | Conflict resolution & non-destructive pull (preserve pending intent;
posted-invoice conflicts reversal-only) | band M | Track G | Depends on: G03 | R-B03, R-B17, R-B18 |
covers E05`.
**Read (plus shared):** `ARCHITECTURE.md` §8/§16-CT–§18-CT (conflict + reversal-only posting rule); the
`R-B03`, `R-B17`, `R-B18` rows.
**Produce:** `PHASE-G04-conflict-resolution-non-destructive-pull.md` — non-destructive pull that
preserves pending intent; posted-invoice conflicts are **reversal-only** per `ARCHITECTURE.md` (never a
destructive overwrite). Introduce the conflict-policy types once.
**Done when:** `PHASE-G04-conflict-resolution-non-destructive-pull.md` exists and passes the checklist;
the reversal-only invariant is stated as a mechanical acceptance criterion. Tick + file name.

**Evidence (T8d):** Authored PHASE-G04-conflict-resolution-non-destructive-pull (band M): three-layer store (`repl_base`/`repl_staging`/`repl_checkpoint`, one client migration next-free ordinal, all `[LocalOnly]`), `SqliteSyncApplyStore` replacing G02's `BufferingSyncApplyStore` (atomic base+checkpoint on one connection/transaction, A-RULE 022 — partial page never advances checkpoint, overlay never touched), `ConflictResolver`+`ConflictPolicyCatalog` encoding ARCH §8.5 by data class (posted invoice/payment **reversal-only, never in-place** — test-enforced; UI-prefs LWW only; inventory additive; slot rejected; demographics field-merge), `ShadowResyncHandler` replacing G03's `NullSyncResyncHandler` (encrypted shadow generation preserves overlay then atomic switch), +2/3 ErrorCodes; conditional DI Replace, no-op defaults stay when unconfigured.

### - [x] T8e — Author G05 (command transport & settlement reconciliation)
**Map row (verbatim):** `G05 | Command transport & settlement reconciliation (replaces
NullCommandTransport) | band M | Track G | Depends on: G04, S10 | R-B03, R-B09, R-B16 | covers E06`.
**Read (plus shared):** `ARCHITECTURE.md` §8 (command transport + settlement); the brief Reference pack
R1 (wire seam) + R4 (`ICommandTransport`) + the settlement vocabulary; the `R-B03`, `R-B09`, `R-B16` rows.
**Produce:** `PHASE-G05-command-transport-settlement.md` — the real `HttpCommandTransport` that **replaces
`NullCommandTransport`** (brief R4 notes the fallback), plus settlement reconciliation against the server.
`NullCommandTransport` stays as the documented offline fallback.
**Done when:** `PHASE-G05-command-transport-settlement.md` exists and passes the checklist; "replaces
`NullCommandTransport`, keeps it as offline fallback" is explicit. Tick + file name.

**Evidence (T8e):** Authored PHASE-G05-command-transport-settlement (band M): `HttpCommandTransport : ICommandTransport` (in Animora.Sync.Custom) consuming the S06 `POST /api/commands[/batch]` + `GET /api/commands/{op}/ids` surface, HTTP-200-for-settled-outcomes mapped onto the journal (four-question §8.7 UX), 426 settles Deferred (never dropped), transient faults stay pending (idempotency key (TenantId,OperationId) — never lost/double-applied), `ICommandBatchTransport` ordered results, `CommandIdRemapService` (server-minted-id remap + dependent-payload rewrite + fingerprint recompute), `IReplicaVersionReader` populating `ExpectedServerVersion` from G04 base, `SettlementReconciler` with strict `SettlementTransitions.EnsureAllowed`; replaces `NullCommandTransport` only when configured (fallback intact); command pipeline kept separate from the state engine (grep-gated).

### - [ ] T8f — Author G06 (online auth, entitlement & device lifecycle)
**Map row (verbatim):** `G06 | Online auth, entitlement & device lifecycle (replaces
AllowAllEntitlementGate; credential-mirror fallback) | band M | Track G | Depends on: G05, S16 | R-B04,
R-B06, R-B20, R-B21, R-B36 | covers E07`.
**Read (plus shared):** `ARCHITECTURE.md` §48–§50 (auth/entitlement/device); the brief Reference pack R4
(`IEntitlementGate`, fallback `AllowAllEntitlementGate`); the `R-B04`, `R-B06`, `R-B20`, `R-B21`, `R-B36`
rows. This phase consumes the S16 licensing surface.
**Produce:** `PHASE-G06-online-auth-entitlement-device.md` — online auth + the real entitlement gate that
**replaces `AllowAllEntitlementGate`**, device lifecycle, and the **local credential-mirror fallback** so
the desktop still authenticates offline.
**Done when:** `PHASE-G06-online-auth-entitlement-device.md` exists and passes the checklist; the
credential-mirror offline fallback is explicit. Tick + file name.

### - [ ] T8g — Author G07 (validation gates I — PostgreSQL CI, tenancy/RLS, restore)
**Map row (verbatim):** `G07 | Validation gates I — mandatory PostgreSQL CI, tenancy/RLS gate, restore
gate (STACK.md §12, ARCH §19) | band M | Track G | Depends on: G06, S18 | R-B19, R-B32 | covers E08
(half 1)`.
**Read (plus shared):** `STACK.md` §12; `ARCHITECTURE.md` §19; the `R-B19`, `R-B32` rows. The named PG-gated
invariant tests from S11/S12/S18 become mandatory here (see those phases' Out-of-scope → G07 notes).
**Produce:** `PHASE-G07-validation-gates-ci-rls-restore.md` — mandatory PostgreSQL CI, tenancy/RLS gate,
and restore gate, expressed as **named, mechanical** CI checks (not prose).
**Done when:** `PHASE-G07-validation-gates-ci-rls-restore.md` exists and passes the checklist; acceptance
criteria are named tests / CI exit codes. Tick + file name.

### - [ ] T8h — Author G08 (validation gates II — performance proof & offline budget)
**Map row (verbatim):** `G08 | Validation gates II — performance proof & offline budget (weak-server/
weak-client capacity, layering audit) | band M | Track G | Depends on: G07, S20 | R-B07, R-B08, R-B10,
R-B32 | covers E08 (half 2)`.
**Read (plus shared):** `STACK.md` §11/§12; `ARCHITECTURE.md` §19; the `R-B07`, `R-B08`, `R-B10`, `R-B32`
rows; `scripts/sandbox/bench.sh` + `scripts/sandbox/offline-budget.py` (cite as the gate runners).
**Produce:** `PHASE-G08-performance-proof-offline-budget.md` — weak-server/weak-client capacity proof,
offline-budget gate, and the layering audit, as mechanical gates.
**Done when:** `PHASE-G08-performance-proof-offline-budget.md` exists and passes the checklist; the
offline budget is a script exit code / numeric threshold, not prose. Tick + file name.

### - [ ] T8i — Author G09 (release engineering) — final T8 task
**Map row (verbatim):** `G09 | Release engineering (topology, install/upgrade/rollback runbook,
backup/restore rehearsal, packaging, SBOM) | band M | Track G | Depends on: G08 | R-B27, R-B31, R-B33 |
covers E09`.
**Read (plus shared):** `ARCHITECTURE.md` §17/§19; `STACK.md` §0; the `R-B27`, `R-B31`, `R-B33` rows;
`scripts/windows/publish-portable.ps1` (cite as the packaging entry point).
**Produce:** `PHASE-G09-release-engineering.md` — deployment topology, install/upgrade/rollback runbook,
backup/restore rehearsal, packaging, and SBOM. Its acceptance criteria are the **real launch gates** (not
prose).
**Done when:** `PHASE-G09-release-engineering.md` exists and passes the checklist; launch gates are
mechanical. Tick + file name. **This closes the T8 batch; the next unchecked task is T9.**

### - [ ] T9 — Reconcile the set and hand off to S01
**Read:** this file; `M00-source-brief.md`; every `PHASE-S##-*.md` / `PHASE-G##-*.md` created in
T3–T7 and T8a–T8i (this is the one task that reads the sibling phase files — to cross-check them); `ROADMAP.md`
§1/§2; `PROGRESS.md` top section.
**Do:** (a) Verify the regenerated set collectively covers the full ROADMAP §2 D/E scope inventory +
post-C11 drift with no gap and no duplicate introducer (use the T1 matrix as the checklist); fix any
phase file that fails the authoring checklist. (b) Rewrite `ROADMAP.md` §1 (Tracks) and §2 (phase
index) so the live tables list the **regenerated `S##`/`G##` phases** (replacing the retained D/E
scope-inventory rows), with correct bands/deps/`R-B##`; update the band tally and total; keep §5
deferred list. (c) In `PROGRESS.md`: add the `S##`/`G##` rows to the "Phase status" table as
`⬜ Not started`, mark the `M00` row `✅ Done`, and set **"Next phase" → `S01`**. (d) Delete the scratch
`M00-source-brief.md` only if its content is now fully reflected in the phase files + ROADMAP (else keep
it and say why). (e) Confirm no `D##`/`E##` phase-file reference remains anywhere
(`grep -rn "PHASE-[DE]0" backend-roadmap` returns nothing but historical PROGRESS log prose).
**Done when:** ROADMAP §1/§2 + PROGRESS reflect the regenerated set, "Next phase" is `S01`, the
coverage matrix shows full §2 coverage, and the grep check is clean. Tick the box — **M00 is now
complete.**

---

## PROGRESS.md update recipe (run at the end of EVERY M00 task)

Minimal, append-only to the `M00` row's Notes — never rewrite an older entry:
- Keep `PROGRESS.md` "Next phase" = `M00` for every task T1–T7 and T8a–T8i; T9 is the only task that
  advances it to `S01`.
- **Commit** at the end of the task via the **Commit rule** push script, not raw `git`:
  `scripts/git/push.sh -b genspark_ai_developer "docs(backend-roadmap): M00 <Task> — <summary>"`.
- In the "Phase status" table, update the `M00` row Notes with `Tx done: <one line>` for the task just
  finished (so a fresh session sees progress without opening this file). Leave the row status `🟨 In
  progress` until T9 flips it to `✅ Done`.
- Commit via the **Commit rule** push script (never raw `git`) on branch `genspark_ai_developer` with a
  conventional message, e.g. `scripts/git/push.sh -b genspark_ai_developer "docs(backend-roadmap): M00 Tx — <summary>"`.

## Acceptance criteria (for M00 as a whole — checked by T9)
- `ls backend-roadmap/phases/PHASE-S*.md backend-roadmap/phases/PHASE-G*.md` lists the full regenerated
  set; each passes the PHASE-AUTHORING-GUIDE checklist.
- The T1 coverage matrix maps every ROADMAP §2 D/E inventory capability + the post-C11 drift to ≥1
  regenerated phase, with no artifact introduced twice.
- `grep -rn "PHASE-D0\|PHASE-D1\|PHASE-E0" backend-roadmap/phases` returns nothing.
- `ROADMAP.md` §1/§2 and `PROGRESS.md` "Phase status" + "Next phase" reference only `A/B/C/M00` and the
  regenerated `S##`/`G##` IDs; "Next phase" = `S01`.
- No change under `src/**`, no migration, no package pin, no `.sln` edit was made by any M00 task.

## Out of scope (owning phase)
- Any server/sync/admin **implementation** (C#, migrations, pins) — owned by the regenerated
  `S##`/`G##` phases, never by M00.
- Clinic-facing web client, cross-tenant analytics features, Redis/broker, deployment stamps, separate
  OLAP store, passkeys/external IdP, SQL RLS-as-hardening — all **deferred** (ROADMAP §5); not
  regenerated.
- Tracks A–C files — complete; M00 does not touch them.
