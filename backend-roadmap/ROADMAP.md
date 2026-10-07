# Animora Backend & Server Roadmap

## §0 LOCKED RULE — PHASE WEIGHT

```
LOCKED RULE — PHASE WEIGHT
Every phase must be LOW token/code weight by default, and MEDIUM only when a
cohesive unit genuinely cannot be split further without breaking correctness.
NO phase may be HEAVY. Phase count does not matter; per-phase weight does.

Bands (self-declared in every phase file, enforced by the reviewer):
  S  = 1-4 new/edited files, ~150-450 net lines of code, one concept.
  M  = 5-10 new/edited files, ~450-900 net lines of code, one cohesive slice.
  XL = FORBIDDEN. If a scope exceeds M, split it into two phases.

Total phases: 40-48. Hard cap 50. Never inflate the count with filler phases,
never merge two slices to reduce the count.
```

Every phase file's `**Band:**` line refers to this block. A phase whose realized scope exceeds band M
must be split before implementation, not absorbed.

## §1 Tracks

| Track | Purpose | Phases | Runnable offline at end of each phase |
|---|---|---|---|
| **A — Local core foundation** | Real projects, domain model and encrypted local SQLite store. The desktop keeps running offline on mocks while the foundation appears beneath it. A01–A11 ship behavior with **no tests**; **A12** is the single consolidation phase that authors and runs every Track A test. | A01–A12 (12) | yes |
| **B — Application layer & local platform** | Command/query pipeline, durable local command outbox, real login, authorization + offline capability, audit/logging/crash safety. B03–B05 are each executed as 3 self-contained sessions (a–c) for low per-session weight; each trio counts as one phase. | B01–B05 (5) | yes |
| **C — Mock retirement** | One bounded context per phase: real `Animora.Contracts` implementation over the local DB, mock deleted, screen verified. | C01–C11 (11) | yes |
| **D — Server** | ASP.NET Core 10 host, SQL Server store, tenancy, identity, command intake, handlers, Datasync replication surface, outbox/workers, subscription, licensing, files, observability. | D01–D13 (13) | desktop still starts offline |
| **E — Admin web, integration, release** | Operator admin site, `ISyncEngine` + Datasync adapter, sync orchestration, conflict resolution, command transport, online auth/entitlement, validation gates, release engineering. | E01–E09 (9) | desktop still starts offline |

**Total: 50 phases.** (A 12 + B 5 + C 11 + D 13 + E 9 = 50; at the hard cap of 50.
Deviation from the §7 mandate of 40–48 is a consequence of three mandated splits plus the Track A
test-consolidation phase A12 — see §6.)

## §2 Phase index

| ID | Title | Band | Track | Depends on | Satisfies | Runnable offline? |
|---|---|---|---|---|---|---|
| A01 | Backend track scaffold | S | A | — | R-B10, R-B12, R-B31 | yes |
| A02 | Domain kernel | S | A | A01 | R-B10, R-B24 | yes |
| A03 | Domain — Identity, Tenant, Staff, Device | S | A | A02 | R-B04, R-B19, R-B21 | yes |
| A04 | Domain — Owners, Patients, Clinical | M | A | A02 | R-B01 | yes |
| A05 | Domain — Scheduling & Boarding | M | A | A04 | R-B01, R-B17 | yes |
| A06 | Domain — Inventory & Purchasing | M | A | A02 | R-B01, R-B23 | yes |
| A07 | Domain — Sales & Cashier | M | A | A04, A06 | R-B01, R-B17 | yes |
| A08 | Domain — Finance ledger, Cheques, Expenses | M | A | A07 | R-B01, R-B17, R-B22 | yes |
| A09 | Local persistence I — DbContext + core mappings | M | A | A03, A05 | R-B02, R-B24 | yes |
| A10 (a–d) | Local persistence II — business mappings + UoW (4 sessions) | M | A | A08, A09 | R-B02, R-B08 | yes |
| A11 (a–d) | Local store hardening (4 sessions) | M | A | A10 | R-B02, R-B09, R-B27, R-B28 | yes |
| A12 | Track A test consolidation (write + build + test) | M | A | A11 | R-B01, R-B02, R-B24 (verification) | yes |
| B01 | Application layer skeleton | M | B | A12 | R-B08, R-B10 | yes |
| B02 | Local command pipeline & outbox | M | B | B01 | R-B02, R-B09, R-B13, R-B14, R-B15, R-B16, R-B25 | yes |
| B03 (a–c) | Local identity & real login (3 sessions) | M | B | B02 | R-B04, R-B09 | yes |
| B04 (a–c) | Authorization & offline capability (3 sessions) | M | B | B03 | R-B04, R-B20, R-B21 | yes |
| B05 (a–c) | Audit, logging, diagnostics, crash safety (3 sessions) | M | B | B04 | R-B09, R-B29 | yes |
| C01 | Patients & Owners | M | C | B05 | R-B01, R-B02 | yes |
| C02 | Reception & Visits triage | M | C | C01 | R-B01, R-B02 | yes |
| C03 | Visit encounter & Vaccination *(executed; phase file reconstructed as a historical stub — `phases/PHASE-C03-visit-encounter-vaccination.md`)* | M | C | C02 | R-B01, R-B02 | yes |
| C04 | Appointments (calendar + booking) | M | C | C02 | R-B01, R-B02 | yes |
| C05 | Boarding | M | C | C03 | R-B01, R-B02 | yes |
| C06 | Inventory & Purchasing | M | C | C01 | R-B01, R-B02, R-B23 | yes |
| C07 | Sales / POS | M | C | C03, C05, C06 | R-B01, R-B02, R-B16 | yes |
| C08 | Cashier & Cheques | M | C | C07 | R-B01, R-B02, R-B16 | yes |
| C09 | Finance & reporting read models | M | C | C08 | R-B01, R-B08, R-B22, R-B35 | yes |
| C10 | History, Dashboard, Search, Profile | M | C | C09 | R-B01, R-B02 | yes |
| C11 | Mock purge & offline acceptance | M | C | C04, C10 | R-B01, R-B08, R-B10, R-B34 | yes |
| D01 | Server host scaffold | M | D | C11 | R-B05 | desktop offline ok |
| D02 | Server data layer & tenancy | M | D | D01 | R-B03, R-B11, R-B19 | desktop offline ok |
| D03 | Identity server | M | D | D02 | R-B04, R-B20 | desktop offline ok |
| D04 | Tenant context & authorization | M | D | D03 | R-B04, R-B19, R-B20 | desktop offline ok |
| D05 | Business API — command intake & idempotency | M | D | D04 | R-B13, R-B14, R-B15, R-B29 | desktop offline ok |
| D06 | Server command handlers I — clinical & scheduling | M | D | D05 | R-B01, R-B17 | desktop offline ok |
| D07 | Server command handlers II — money & stock | M | D | D06 | R-B01, R-B17, R-B22, R-B23 | desktop offline ok |
| D08 | Datasync replication surface | M | D | D05 | R-B03, R-B11, R-B31 | desktop offline ok |
| D09 | Outbox, workers, resilience | M | D | D07 | R-B07, R-B09, R-B25 | desktop offline ok |
| D10 | Subscription context & operator API | M | D | D04 | R-B05, R-B30 | desktop offline ok |
| D11 | Licensing & anti-piracy | M | D | D10 | R-B06, R-B29, R-B30 | desktop offline ok |
| D12 | Files — valet key + resumable transfer | M | D | D09 | R-B26 | desktop offline ok |
| D13 | Server observability & weak-server performance | M | D | D09 | R-B07, R-B27, R-B35 | desktop offline ok |
| E01 | Admin web I — operator panel core | M | E | D11 | R-B05 | desktop offline ok |
| E02 | Admin web II — insight seams | M | E | E01 | R-B05, R-B11, R-B35 | desktop offline ok |
| E03 | ISyncEngine + Datasync client adapter | M | E | D08 | R-B03, R-B13, R-B31 | desktop offline ok |
| E04 | Sync orchestration & status | M | E | E03 | R-B03 | desktop offline ok |
| E05 | Conflict resolution & non-destructive pull | M | E | E04 | R-B03, R-B17, R-B18 | desktop offline ok |
| E06 | Command transport & settlement reconciliation | M | E | E05, D07 | R-B03, R-B09, R-B16 | desktop offline ok |
| E07 | Online auth, entitlement & device lifecycle | M | E | E06, D11 | R-B04, R-B06, R-B20, R-B21 | desktop offline ok |
| E08 | Validation gates & performance proof | M | E | E07, D13, E02 | R-B07, R-B08, R-B10, R-B19, R-B32 | desktop offline ok |
| E09 | Release engineering | M | E | E08 | R-B27, R-B31, R-B33 | desktop offline ok |

Band tally: **S = 3** (A01, A02, A03) · **M = 47** · **XL = 0**.

## §3 Dependency & ordering rules

1. A phase may depend only on lower-numbered phases (or earlier tracks). No forward dependency exists.
2. Track order A → B → C → D → E is fixed. Within a track, the listed `Depends on` is the only ordering
   constraint; phases with the same dependency may run in either order but **must** run in ID order to
   keep `PROGRESS.md` linear and migrations sequential.
3. A type, table, interface, error code or migration is introduced by exactly one phase. Later phases
   reference it by its exact name and never redefine it.
4. When phase N produces something phase N+k consumes, N's "Contracts / schema added" and N+k's
   "Read before starting" name the same identifiers.
5. Migrations are created in phase order and never edited by a later phase. Local store migrations:
   `001`–`003` (A09, A10, A11), then one per Track B/C phase that adds a table. Server migrations start
   at `101` (D02).
6. Cross-cutting concerns are introduced early and only *used* later: tenancy (A03/D02), ids + clocks +
   money (A02), error codes (A02), idempotency/command envelope (B02), authorization (B04), audit +
   correlation ids (B05). No later phase retrofits them.
7. Each Track C phase touches exactly one bounded context plus its DI line(s) in `App.axaml.cs`.
8. The wire contract version constant is introduced in A01 (`WireContract.Version`) and bumped only by a
   phase that actually changes a wire shape (D08, E03 are the only expected bumps).
9. No phase may leave the solution non-building; Tracks A–C additionally may not leave the desktop
   non-runnable offline (CONVENTIONS §11).
10. **Track A test-deferral rule (binding on A05–A12; A01–A04 already shipped with their tests).**
    Phases A05–A11 write **no test** and run **no test suite** (`scripts/sandbox/test.sh` is not invoked
    in them). Each ships behavior only, and its per-phase verification is at most a *lightweight* build
    (`scripts/sandbox/build.sh full` = 0/0) — run only when needed to confirm the new code compiles —
    plus the offline smoke run. Every assertion a phase would have made is recorded, unchanged in rigor,
    in that phase's **"Test coverage owed to A12"** section. **A12** is the single phase that authors all
    those tests, runs the full suite to green, and fixes any defect the tests reveal. This trades no
    coverage and no quality — it only relocates *when* the tests are written and run, so the A05–A11
    sessions stay lean. Non-test correctness gates that are not a test (e.g. `dotnet ef migrations list`
    at design time, `grep` guards) remain in their originating phase. The Global Definition of Done's
    "tests green" clause (§4, CONVENTIONS §12.2) is satisfied for A05–A11 *collectively* by A12; it is
    **not** waived for the track. From Track B onward the normal per-phase testing policy resumes.

## §4 Global Definition of Done

Binding for every phase: `CONVENTIONS.md` §12. In short — build 0 warnings / 0 errors, tests green,
runnability held, no forbidden dependency, no rule violated, every acceptance criterion checked,
`PROGRESS.md` updated, committed per the repo git workflow.

## §5 Explicitly deferred (post-launch, with trigger)

| Deferred item | Trigger that reopens it |
|---|---|
| Web client for clinics (online-only Blazor clinic app) | After E09 ships and a customer requires browser access; seams already exist (`Animora.Contracts.Wire`, server Business API). |
| Cross-tenant business analytics features (per-tenant stock, per-store charts as operator features) | Operator demand + an explicit consent/ADR decision; the read-model seam and `ITenantInsightQuery` port land in E02. |
| Redis backplane / SignalR scale-out | A second web instance is deployed (STACK.md D5); requires the A-RULE 031 ADR. |
| Deployment Stamps (`Tenant → Stamp → Database`) | One tenant's load or data-residency requirement justifies a dedicated database; designed for in D02's data-access abstraction. |
| Separate reporting database / OLAP store | Reporting load measurably breaches the OLTP budget measured in E08 (A-RULE 034). |
| Passkeys / external IdP | Customer security requirement; ASP.NET Core Identity in D03 is the extension point. No cloud IdP ever. |
| SQL RLS enabled as hardening | Only after the STACK.md §12 RLS gate passes in E08 with trace flag 11042; isolation must already hold without it. |
| Message broker / microservice extraction | Never without the A-RULE 031 ADR; the modular monolith is the target shape. |

## §6 Deviations from the mandated phase plan

| Deviation | Justification |
|---|---|
| Track D has 13 phases (D01–D13) as mandated; Track C has 11 (C01–C11) as mandated; the mandated row set sums to **49** (A 11 + B 5 + C 11 + D 13 + E 9). | No row was merged or dropped, per the "you may not merge rows" rule; the soft ceiling yields to the mandated structure. |
| **Track A gains a 12th phase, A12 (test consolidation), taking the total to 50 — exactly the hard cap.** | A deliberate restructuring of Track A: A01–A11 ship behavior with no tests so each session stays low-weight, and A12 authors + runs the whole Track A test suite in one focused pass. It adds coverage rigor (the "owed to A12" sections are exhaustive), not scope creep; A12 itself writes only test files plus any minimal bug-fix, so it holds band M. The hard cap 50 is respected; no further phase may be added to Track A without an ADR. |
| Some rows are executed as lettered sub-sessions (A10 a–d, A11 a–d, B03 a–c, B04 a–c, B05 a–c) but **no row was added to the count** — each lettered trio/quartet is one phase, split only to keep per-session token/code weight low (§0 LOCKED RULE — PHASE WEIGHT). The parent `PHASE-<ID>-*.md` is an index pointing at the self-contained sub-session files; no scope is added or removed by the split. | Splitting execution sessions holds band weight without inflating the phase count; the total stays at the hard cap of 50. Quality and acceptance rigor are unchanged — every original acceptance criterion is preserved and assigned to a sub-session (tests + the runnable gate land in the final letter). |
| `D06`/`D07` titles refined to "clinical & scheduling" / "money & stock". | Clarifies the boundary between the two handler phases; scope unchanged from the mandate. |
