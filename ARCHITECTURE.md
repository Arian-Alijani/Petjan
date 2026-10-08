# Animora — Architecture Specification

> **Purpose.** The single, authoritative **behavioral & structural** definition
> for Animora — *how the system is organized and made to behave correctly*:
> bounded contexts, layering, the offline-first sync spine, tenancy isolation,
> security planes, financial/inventory invariants, reliability patterns, the
> commercial/licensing split, the deployment & recovery model, the PostgreSQL
> critical-sync design, and the validation gates. Source of truth for AI agents
> and engineers on every **design** decision. Every backend phase file
> (`backend-roadmap/phases/PHASE-*.md`) cites this file by `§`; those citations
> are the contract — keep the section numbers stable.
>
> **Companion file — read the division of labour once.** `STACK.md` owns
> *technology facts* (which library, which version, a technology's own documented
> capability/limitation). This file owns *what Animora does with that technology*.
> **Test:** a sentence true for **any** product on this stack → `STACK.md`. A
> sentence true only because Animora chose to build it this way → here.
> Example: "PostgreSQL has no native Change Tracking equivalent" is a stack fact;
> "our change journal is a per-tenant serialized counter row serialized to COMMIT"
> is an architecture decision (§PG). On conflict, authority order is
> `ARCHITECTURE.md` / `STACK.md` > `backend-roadmap/CONVENTIONS.md` > a phase file.
>
> **Scope note.** The Windows desktop frontend and Tracks A–C (local core,
> application pipeline, mock retirement) are **built** (see
> `backend-roadmap/PROGRESS.md`). This document's forward-looking weight is on the
> **central server and the remaining Tracks D–E** (server host, PostgreSQL store,
> identity, command intake/handlers, the critical-sync surface, subscription,
> licensing, files, observability, the admin web, the client sync engine,
> conflict resolution, online auth/entitlement, gates, release). Statements about
> already-built layers describe the design they must continue to satisfy, not new
> work.
>
> **Revision 3.2 (2026-10-08).** PostgreSQL 18 / Npgsql 10 is the server database
> baseline (replacing an earlier SQL Server baseline). The critical-sync design is
> a project-owned PostgreSQL transactional journal with REPEATABLE READ immutable
> batches (§PG, §16-CT–§18-CT), behind an Application-owned `IStateSyncEngine`.
> This is a **specification**; no server runtime, data-transfer, load or restore
> result is claimed here — those are implementation gates (§19, STACK §12).

---

## §0 Reading guide & claim discipline

- **Read only the `§` your task cites.** This file is large by design (it is the
  behavioral counterpart to a large stack file). Jump to the section a phase file
  names; do not read end to end.
- **Claim tags** mirror `STACK.md`: **[invariant]** a rule that must always hold
  and is tested; **[decision]** a chosen design with a stated rationale;
  **[pattern]** a named industry pattern applied here; **[gate]** a correctness
  property that must be *proven* by a test/PoC before the related capability is
  relied on (the gate suite is §19). A design is not "done" because it is written
  here — it is done when its gate passes.
- **A-RULEs** are the numbered, cross-cutting invariants the phase files and
  `CONVENTIONS.md` cite by number (e.g. `A-RULE 019`). The authoritative one-line
  statement of each lives in the **A-RULE registry (§21)**; the section that owns
  its subject states the full rationale.
- **No divergence from `CONVENTIONS.md` §6 (the condensed sync spine).** §8 here
  is the long form of that list; if wording differs, the invariant meaning is the
  same and §8 governs the *why*, `CONVENTIONS.md` the *obey-don't-rederive*.

---

## §1 Requirement → design mapping

Each product/invariant requirement (`R-B*`, defined in
`backend-roadmap/00-REQUIREMENTS-LOCK.md`) maps to the **design** that satisfies
it. `STACK.md §1` maps the same requirements to *technology*; this is the
behavioral half.

| # | Requirement (short) | Design answer (where) |
|---|---|---|
| R-B01 | Every screen on real data; cross-feature flows wired | Bounded-context aggregates (§3) + two-pipeline spine (§8) + server handlers (§8.3, Track D06/D07) |
| R-B02 | Offline-first desktop, full function server-down | Provisional-local-truth model + local command journal (§8.1, §8.2) |
| R-B03 | Multi-user/device sync, no divergence, long-offline convergence | Critical-sync journal + immutable batches + non-destructive pull + full resync (§8.2, §PG, §16-CT) |
| R-B04 | Login + roles + per-role gating | Three independent planes (§10) + stored RBAC (§10.2) |
| R-B05 | Subscription mgmt + operator admin web | Subscription bounded context (§13.3) + admin web (§13.5) |
| R-B06 | Anti-piracy: signed licence, binding, fail-closed | Licensing split from Subscription (§13.1, §13.2) |
| R-B07 | Weak-server efficiency on 1–3 cores / 2–4 GiB | Bounded pools/admission, work pushed off the request path (§16, §PG.4) |
| R-B08 | Weak-client efficiency | Off-startup work, virtualization, bounded caches (§16.1) |
| R-B09 | Crash resilience, resumable workers, recoverable store | Outbox + atomic claim + local store recovery (§12, §17.3) |
| R-B10 | Clean layering; no client/server mixing | Project/dependency map (§6) enforced by `LayeringRulesTests` |
| R-B11 | Future-ready seams (web clinic, operator insight) | Wire contracts + replication surface + insight port (§8.6, §13.5, §20) |
| R-B13 | Two separate pipelines; replication order ≠ financial order | §8.1 [invariant] |
| R-B14 | Idempotency on `(TenantId, OperationId)`; ≤1 financial effect | §8.3 [invariant] |
| R-B15 | Idempotency ≠ ordering; dependency explicit, server-resolved | §8.4 [invariant] |
| R-B16 | Settlement state machine surfaced; sync-failure UX contract | §8.3, §8.7 [invariant] |
| R-B17 | Version mismatch detects, domain decides; no blind LWW on money | §8.5 [invariant] |
| R-B18 | No destructive sync of unsent local intent | §8.2 [invariant] |
| R-B19 | Tenancy: 3 isolation layers holding with DB hardening OFF | §9 [invariant] |
| R-B20 | AuthN ≠ AuthZ ≠ Entitlement, ordered | §10.1 [invariant] |
| R-B21 | Offline = signed device-bound capability snapshot, fail-closed | §10.4 [invariant] |
| R-B22 | Append-only balanced ledger; posted records immutable | §11.1 [invariant] |
| R-B23 | Movement-based inventory; on-hand derived | §11.2 [invariant] |
| R-B25 | Transactional Outbox + atomic claim + idempotent consumers | §12.1, §12.2 [pattern] |
| R-B26 | File lifecycle + reconciliation + Valet-Key | §14 [pattern] |
| R-B27 | Zero mandatory external Internet dependency at runtime | §17.4 [invariant] |
| R-B29 | Append-only audit + correlation ids end to end | §10.7, §15.1 [invariant] |
| R-B30 | Subscription vs. Licensing architecturally separate | §13.1 [invariant] |
| R-B31 | Explicit wire contract version; mixed-fleet N/N-1/N-2 | §8.6, §18 [invariant] |
| R-B32 | Validation gates executed and recorded | §19 [gate] |
| R-B33 | Release engineering: topology, runbook, backup/restore rehearsed | §17 |
| R-B34 | No mock left behind | §6 (Track C complete; audited) |
| R-B35 | Reporting isolated from OLTP; bounded tenant-safe caches | §16.3 [invariant] |
| R-B36 | Commercial SaaS shape: register→buy→download→login | §3.1, §13.4 |

---

## §2 Architectural style & the five governing forces

**[decision] Animora is a modular monolith** — one deployable server process, one
shared database, internally partitioned into bounded contexts (§3) with explicit
ports. Microservice extraction and a message broker are **non-goals** and require
an ADR (§20, A-RULE 031). The shape is chosen because the whole product must run
on a single weak host (§16) with zero external-Internet dependency (§17.4); a
distributed topology would add operational surface a single vendor-operated box
cannot justify at launch.

Every design decision in this file is traceable to one of five forces; when two
conflict, they are resolved in this priority order:

1. **Correctness of money & identity** — a financial fact is never lost,
   double-applied, or silently overwritten; a tenant never sees another tenant's
   row. Beats everything.
2. **Offline-first** — the desktop is fully functional with the cable pulled;
   local state is provisional but durable.
3. **Zero-Internet runtime** — nothing in a runtime path requires the public
   Internet (§17.4).
4. **Weak-host efficiency** — everything fits 1–3 cores / 2–4 GiB; work is pushed
   off the request path and off app startup.
5. **Operability** — a single operator can update code, back up, restore, and move
   hosts without downtime surprises; manual subscription/tenant management is a
   first-class API, not a script.

---

## §3 Product, topology & operability

### §3.1 The product and its user journey

Animora is a **commercial, vendor-hosted (SaaS)** veterinary clinic management +
accounting platform. One vendor (Animora) runs **one central multi-tenant
server**. A clinic's journey is: **register + buy a subscription on the public
website → download the desktop app → log in → use it.** [decision] Clinics never
run a server; "self-hosted" in these specs means *the vendor self-operates its
one server on its own infrastructure*, never per-clinic hosting.

The **public website is a separate vendor-side system**, outside this solution.
It performs payment/tax/invoicing and then hands the clinic-serving server an
**async, idempotent, authorized provisioning intent** (§13.4) — it is never a
runtime dependency of the running clinic, and no payment-processor SDK exists in
this solution. This keeps force 3 (zero-Internet runtime) intact.

### §3.2 Deployment topology (launch)

```
                         ┌─────────────────────────────────────────────┐
  Clinic desktops        │  ONE vendor-operated host (1–3 cores/2–4 GiB) │
  (Avalonia, offline-    │                                               │
   first, per clinic)    │   Animora.Server (ASP.NET Core 10 / Kestrel)  │
        │  HTTPS          │     ├─ Business command API   /api/commands   │
        ├────────────────▶│     ├─ Critical-sync surface  /sync/*         │
        │                 │     ├─ Identity (local ASP.NET Core Identity) │
   (public website,       │     ├─ Operator API + Admin web (Blazor)      │
    SEPARATE system) ─────▶     ├─ Background workers (outbox, projections)│
    provisioning intent   │     └─ File store (filesystem + SQL metadata) │
                          │                                               │
                          │   PostgreSQL 18  (same host, co-located)      │
                          │   File content on the server filesystem       │
                          └─────────────────────────────────────────────┘
```

[decision] **Single process, single database, co-located files at launch.** No
clustering, no read-replica, no object-storage service, no Redis/broker. The web
runs **single-instance** (a Redis SignalR backplane is a *future deploy switch*,
not a code change — §16.2). The reference hardware (2–4 GiB / 1–3 cores / SSD) is
an architectural *reference only*; real capacity is a measured gate (§19 gate T,
A-RULE 035), never asserted.

### §3.3 Operability requirements (first-class, not afterthoughts)

The vendor-operator must be able to do all of the following **easily**, and every
one is a design constraint that shaped later sections:

| Operability need | How the architecture makes it easy | Section |
|---|---|---|
| **Update code / app** | One self-contained process; deployment migrations run before traffic under a DDL role; runtime startup only checks schema version/readiness; blue/green or stop-migrate-start both supported | §17.1, §17.2 |
| **Back up** | One PostgreSQL logical/physical backup + one filesystem tree + the Data-Protection key ring; a single documented set; rehearsed restore is a gate | §17.3 |
| **Move to another host** | No host-pinned state beyond `DataRootPath`, the DB, and the key ring; a stable `SetApplicationName` means the key ring survives the move; config is env/secret-store, never baked into an image | §13.2, §17.3 |
| **Add/suspend/manage subscriptions manually** | Operator API + admin web expose the *same* create/extend/reduce/suspend/cancel/renew operations the website's provisioning intent uses — one code path, two authorized callers | §13.3, §13.4 |
| **Separate & keep tenants clean** | One shared schema + `TenantId` discriminator + three isolation layers holding with DB hardening off | §9 |
| **See cross-tenant charts later (operator-only)** | A read-model insight seam (`ITenantInsightQuery`) isolated from OLTP, gated by consent/ADR | §13.5, §16.3 |
| **Stand up a web app on this or another host** | Web clinic client is online-only and talks only through the versioned wire contracts + Business API; adding it is a new project, not a server change | §7.2, §20 |

### §3.4 Bounded contexts (the context map)

**[decision]** The domain is partitioned into bounded contexts; each owns its
aggregates and raises domain events, and cross-context interaction is by explicit
event/handoff, never by reaching into another context's tables. The contexts (the
same names appear across `Pages/`, `Contracts/`, `Animora.Domain/<Context>`):

| Context | Owns (aggregates) | Key cross-context handoff |
|---|---|---|
| **Identity** | Tenant, User, StaffMembership, Device, RoleDefinition, Permission, CapabilitySnapshot | supplies the tenant/role/permission facts every plane checks (§10) |
| **Patients** | Owner, Pet, WeightEntry, DocumentReference | source of the patient directory; replicated state (§8.2) |
| **Clinical** | Visit, VaccinationRecord, MedicalHistoryEntry, TreatmentPlan, TreatmentOccurrence, VaccineCatalogItem | a completed Visit emits a **billing handoff** to Sales |
| **Scheduling** | Appointment, ReceptionQueueEntry | appointment → reception → visit chain |
| **Boarding** | Room, BoardingServiceDef, BoardingStay | a checked-out stay emits a **billing handoff** to Sales |
| **Inventory** | UnitOfMeasure, ProductCategory, Product (+ variants), StockMovement | a sale/receipt writes a movement (§11.2) |
| **Purchasing** | Supplier, PurchaseOrder, GoodsReceipt | a posted receipt writes stock movements + a payable |
| **Sales** | Invoice, Payment, Insurer, InvoiceAdjustment | posting an invoice emits events the ledger consumes |
| **Cashier** | CashSession, CashMovement | settles invoices via Sales commands; closing emits a ledger event |
| **Finance** | JournalEntry, LedgerAccount, AccountingPeriod, Cheque, Expense, RecurringExpense | the **single ledger writer** consumes events from every money/stock context (§11.1) |
| **Subscription** (server-only) | Plan, Subscription, BillingPeriod, Seat, SubscriptionEvent | commercial state; **not** in the runtime entitlement path (§13.1) |

[invariant] A bounded context never reads or writes another context's aggregate
tables directly; the only couplings are (a) a raised **domain event** consumed by
a handler, and (b) an explicit **billing-handoff** record. This is what lets the
server re-derive the ledger from the same `PostingRules` the client used, so the
two can never disagree (§11.1).

---

## §4 Server runtime shape

**[decision] One ASP.NET Core 10 host, several distinct surfaces, one startup
order.** The host exposes four logical surfaces on one Kestrel process, each with
its own route prefix and auth profile:

| Surface | Prefix | Purpose | Auth profile |
|---|---|---|---|
| Business command API | `/api/commands*` | durable, idempotent command intake + settlement (§8.3) | authenticated + per-command policy + entitlement |
| Critical-sync surface | `/sync/*` | pull of frozen, immutable multi-entity state batches (§16-CT) | authenticated + tenant-scoped data-view |
| Identity | `/auth/*` | local sign-in, refresh, device lifecycle (§10) | anonymous→authenticated |
| Operator API + Admin web | `/operator/*`, `/admin` | subscription/tenant management + insight (§13.3, §13.5) | operator role only |

Health (`/health/live`, `/health/ready`) is anonymous and leaks no secret.
`/health/live` is process-only (no DB touch); `/health/ready` checks DB
connectivity + Data-Protection key ring + `DataRootPath` writability — the line
that separates a **no-DB diagnostic boot** from **production authority** (§17.3).

**[invariant] Startup order is fixed and documented as a numbered block in
`Program.cs`:** (1) bind + validate options with `ValidateOnStart` so a
misconfiguration fails at boot not first request; (2) logging; (3) Data
Protection (persisted key ring, stable `ApplicationName`); (4) DbContext
registration; (5) health checks; (6) OpenAPI (first-party, Development + opt-in
flag only); (7) middleware pipeline: global exception → HSTS/HTTPS redirect →
request logging (sampled, skips `/health/*`) → tenant-context resolution →
authN → authZ → entitlement → routing → endpoints. A `GlobalExceptionMiddleware`
maps any unhandled exception to a `WireApiError` carrying a correlation id and
never leaks a stack trace outside Development.

**[decision] Secrets never live in config files.** Connection strings and signing
keys come from environment variables or the OS secret store; a startup
`SecretGuard` refuses to run if a literal connection string appears in a bound
`appsettings*.json` (`SecretInConfigurationException`). This is force 5
(operability: safe host moves) meeting force 1 (no credential leak).

**[decision] TLS protocol is OS-negotiated** (`SslProtocols.None`), never a
hard-coded version, because the support matrix spans OSes without TLS 1.3
(`STACK.md §8.2`, A-RULE 028). The runtime certificate is issued by an internally
operated CA — no public CA client at runtime (force 3).

---

## §5 Request lifecycle & the command path

A clinic action that changes state is **never a raw CRUD call** to the server. It
is a **business command** that travels the same shape whether it settles locally
(offline) or at the server (online):

```
UI gesture
  → ViewModel → Contracts interface → Client.Services (builds CommandEnvelope,
      assigns OperationId, LocalSequence, CommandFingerprint)
  → local command journal (durable, provisional)           ← offline stops here
  → (when online) ICommandTransport → POST /api/commands
      → idempotency gate (TenantId, OperationId)
      → dependency resolution (server-side only)
      → per-command authorization + entitlement
      → server handler (re-validates the domain invariant from scratch)
      → one DB transaction: state change + Outbox row
      → SettlementResult (Accepted | Rejected | Conflict | Deferred)
  → client records settlement, surfaces it in the UI (§8.7)
```

**[invariant] The server re-derives, never trusts.** A command's payload is data,
not authority: the server recomputes totals from `InvoiceTotals`, re-checks slot
conflicts, re-derives stock and re-posts the ledger from `PostingRules`. The
client's local result is **provisional** until the server's `SettlementResult`
confirms it (force 1). The server is the single global authority for settlement
(§8.1 item 2).

A **read** that a client needs offline does not travel this path; it is served
from the client's local replica, which the critical-sync surface keeps current
(§8.2). Reads that are genuinely online-only (operator insight, cross-tenant
charts) are queries against server read models (§16.3).

---

## §6 Project & dependency map (layering)

**[invariant, enforced by `LayeringRulesTests`]** The solution is layered so that
a client problem can never pull in a server type and vice versa, and so a Datasync
or PostgreSQL or HTTP type can never leak into the domain. The full layout is in
`CONVENTIONS.md §1`; the dependency-direction rules that this file governs:

```
Animora.Domain            → references NOTHING (no EF, no DI, no Contracts)
Animora.Application       → Domain + Contracts (DTOs) + Contracts.Wire
Animora.Contracts(.Wire)  → no Animora.* (Wire references Domain for shared ids only)
Animora.Persistence.*     → Application + Domain        (never the reverse)
Animora.Infrastructure.*  → Application + Domain
Animora.Client.Services   → Application + Persistence.Sqlite + Infrastructure.Local + Contracts
Animora.Sync.Abstractions → package-free ports only
Animora.Sync.Custom       → Abstractions + Application + Persistence.Sqlite + Contracts.Wire
Animora.Persistence.PostgreSql → Application + Domain  (the ONLY place Npgsql appears)
Animora.Server            → Application + Domain + Contracts.Wire + Persistence.PostgreSql
                            + Server.Licensing(Issuance) ; NEVER a client project
Animora.Server.Licensing  → Verification half: Domain + Contracts.Wire only (client-reusable)
                            Issuance half: Application + Domain (server-only)
Animora.Web.Admin         → hosted by Animora.Server
Animora.Desktop           → Contracts + Client.Services (composition root only)
```

**[invariant] Client ↔ server isolation.** `Animora.Server` must not reference
`Animora.Client.Services`, `Animora.Persistence.Sqlite`, `Animora.Sync.Custom` or
`Animora.Desktop`; the client must not reference any server project. Shared
meaning travels **only** through `Animora.Domain`, `Animora.Application`,
`Animora.Contracts.Wire`. The licensing project is split **by direction not by
layer**: the `Verification/` half transitively references neither PostgreSQL nor
ASP.NET Core, which is exactly what makes it safe for the desktop to consume
(§13.2).

**[decision] The sync engine sits behind two ports.** Application owns
`IStateSyncEngine` (the state-replication port); `Animora.Sync.Abstractions` owns
the outer client facade `ISyncEngine`. Domain and Application never reference a
concrete adapter (Custom or the optional Datasync). This is the single most
important lever for surviving a sync-library problem: swap the adapter without a
domain rewrite (A-RULE 001, T-RULE 001; `STACK.md §4.1`, D-register D2).

---

## §7 Client model matrix

### §7.1 What is shared vs. per-target

One C# solution, three targets (Windows desktop, macOS desktop, future web),
sharing Domain / Application / Contracts / Contracts.Wire / `IStateSyncEngine`.
**[decision] What is shared is *meaning*, not *implementation***: the desktop
hosts the local encrypted SQLite store and the custom sync engine; the web client
hosts neither (below). The server hosts PostgreSQL and the handlers. No layer
reaches across the client/server line except through the wire contracts.

### §7.2 Desktop = offline-capable; Web = online-only (by platform limit, §8.2)

**[invariant]** The desktop (Avalonia native) is offline-first: it carries the
encrypted SQLite replica + local command journal and keeps full function with the
server down. A future web clinic client is **online-only** — not a policy choice
but a documented platform limit: EF Core + SQLite (the offline store) do not run
in a browser (`STACK.md §3.2`, D-register D3). The web client therefore talks
only through the Business API + critical-sync surface over the versioned wire
contracts, and is a **future-ready seam** (R-B11), not launch scope.

### §7.3 Web rendering strategy (when the web client is built)

**[decision]** Static SSR by default + selective `InteractiveServer` islands,
never global interactivity. A per-session SignalR circuit costs server memory
that the weak host (force 4) cannot spend globally; islands are used only where an
interaction genuinely needs them, with `[PersistentState]` to avoid double-fetch
across the prerender→interactive handoff. Multi-instance web requires a Redis
SignalR backplane — a *future deploy switch* gated by an ADR (§16.2, A-RULE 031),
not launch code.

---

## §8 The offline-first sync spine

> This is the long form of `CONVENTIONS.md §6`. It is the most important design in
> the product and the one with the most ways to be subtly wrong. Read it fully
> before touching any D08/E03–E06 phase.

### §8.1 The two permanently separate pipelines [invariant] (R-B13)

**There are two pipelines, and they are never merged:**

1. **State replication** — generic record replication of flat, denormalized
   entities (patient directory, catalogs, calendar, reference data). Direction:
   **server → client** (and a narrow client → server for the few client-authored
   replicable shapes). Ordering here is a *transport* concern (§8.2).
2. **Business command settlement** — the durable, idempotent command pipeline
   (§8.3) for anything with a financial or inventory effect or a non-trivial
   invariant (invoices, payments, stock movements, cash sessions, ledger,
   cheques, expenses, bookings that must not collide).

**[invariant] Replication ordering never becomes financial ordering.** A row
appearing in a replication batch is not a settled business fact; a financial fact
is only ever established by a settled command. The two use different identity
vocabularies and must never be conflated:

```
replication: UpdatedAt (server display stamp) · Version (opaque concurrency) ·
             ChangeSequence (per-tenant journal counter, §PG) · batch/page ids
command:     OperationId (idempotency key) · LocalSequence (client order) ·
             CausationId / DependsOnOperationId (explicit dependency) ·
             ExpectedServerVersion (optimistic precondition) · CommandFingerprint
```

[invariant] `UpdatedAt` / `Version` / any transport id are **never** read as a
business sequence (A-RULE 009/010, T-RULE 006). The reason a product review keeps
flagging this: a timestamp cursor looks like it could order money, and it cannot
(clock skew, same-ms bursts, retries) — so money is ordered only by the command
pipeline's explicit, server-resolved dependency (§8.4).

**[invariant, item 2] The server is the single global authority.** An offline
client holds **provisional** truth. Provisional truth is durable (never lost on
crash, §12) and honestly labelled in the UI (§8.7), but it is never treated as
final until settled, and it is never allowed to overwrite another actor's settled
fact.

### §8.2 State replication — non-destructive, immutable-batch pull (R-B03, R-B18)

**[decision] The critical path is a project-owned PostgreSQL transactional
journal with REPEATABLE READ immutable batches** behind `IStateSyncEngine`,
implemented by `Animora.Sync.Custom` (§PG, §16-CT). It is **not** a Datasync
timestamp-cursor client at launch; the optional Datasync `/tables/*` adapter is
deferred and, if ever admitted, is limited to qualified SYNC-SIMPLE entities that
pass gate G1 (§19, `STACK.md §4`).

The replicated **surface** is a fixed manifest (`SyncContractCatalog`), derived at
D08 from the server model minus everything a client must never hold or must obtain
only through commands. Replicated: Owner, Pet, WeightEntry, DocumentReference,
Visit, VaccinationRecord, MedicalHistoryEntry, TreatmentPlan, TreatmentOccurrence,
VaccineCatalogItem, Appointment, ReceptionQueueEntry, Room, BoardingServiceDef,
BoardingStay, UnitOfMeasure, ProductCategory, Product, Supplier, Insurer,
LedgerAccount, AccountingPeriod, Tenant, RoleDefinition, StaffMembership. **Not
replicated** (settled by command or never leaves the server): Invoice, Payment,
CashSession, JournalEntry, Cheque, Expense, Recurring*, StockMovement,
PurchaseOrder, GoodsReceipt, and all credential/authorization tables. [invariant]
Every catch-up/policy/test count derives from `SyncContractCatalog.ProjectionOrder`
— never a magic total in a phase file.

**The replicated DTO shape** (`<Entity>SyncContract`): `Id`, display `UpdatedAt`
(DB-UTC), canonical opaque **UUID `Version`**, `Deleted`, `ContractVersion`, plus
allowed payload fields. **[invariant] `TenantId` is never serialized** in a sync
contract (it stays in server-side query projections and is never inferred from
payload — §9).

**Pull mechanics — all-or-nothing immutable batches:**
- The server freezes a **multi-entity batch** from one REPEATABLE READ snapshot:
  a watermark + the rows at that watermark, stored immutably (§16-CT). A batch is
  identified by `(epoch, tenant feed generation, batch id)` and its continuation
  is a protected `(BatchId, PageOrdinal)`, not a client-chosen offset.
- The client downloads pages into an **invisible staging** generation
  (`repl_*` tables in a separate replica DbContext), resumable by
  `BatchId/PageOrdinal`.
- **[invariant] Application is atomic per batch.** Base + checkpoint advance
  together, or not at all; a page/chunk partial commit can never advance the
  checkpoint (§16-CT, A-RULE 022). If application spans the business projection
  and the checkpoint across two local contexts, they share **one** SQLite
  connection + transaction (`UseTransaction`), never `TransactionScope` or two
  independent commits.

**[invariant] No destructive sync (R-B18, A-RULE 017).** An inbound pull never
overwrites unsent local business intent. The local store keeps three logical
layers: an authoritative **base** (last settled server state), a **pending
overlay** (local provisional commands not yet settled), and the UI reads
base-with-overlay. A pull replaces base; it never touches the overlay. A full
resync builds/validates an **encrypted shadow generation** that preserves the
pending overlay, then **atomically switches** base/checkpoint to it — pending
business operations are never dropped and active state is never cleared before the
replacement succeeds (`CONVENTIONS.md §6` item 15, §17-CT).

### §8.3 Business command settlement — the durable idempotent pipeline (R-B14)

**The command envelope** (`CommandEnvelope`, `Animora.Contracts.Wire`): carries
`OperationId`, `TenantId` (informational on the wire; the server resolves the
authoritative one, §9), `UserId`, `DeviceId`, `CommandType`, `AggregateId`,
`Payload`, informational `CreatedAtUtc`, `LocalSequence`, `Status`,
`CommandFingerprint`, and (V2) `CausationId`/`DependsOnOperationId` +
`ExpectedServerVersion`.

**[invariant] Idempotency key is `(TenantId, OperationId)` (A-RULE 005/019):**
- An identical retry (same key, same `CommandFingerprint`) returns the **stored
  result** — never re-executes, never doubles a financial effect.
- The **same `OperationId` with a *different* `CommandFingerprint`** is a
  security/integrity error: it is **rejected and audited**, never silently
  accepted (a replayed-but-tampered command).
- **At most one financial effect per command** (A-RULE 006). This is enforced by
  the idempotency store being written in the **same transaction** as the effect.

**Server intake** (`POST /api/commands`, `/api/commands/batch`): durably records
`(TenantId, OperationId)` in an `IdempotencyRecords` table (composite PK), with an
`IntakeStatus { Received, InProgress, Settled, Failed }`, resolves dependencies
server-side (§8.4), dispatches to the registered handler, and returns a
`SettlementResult`. The batch endpoint is **not** a transaction: each envelope
settles independently, results return **in request order**, and the batch never
reorders (A-RULE 022); max batch size 50 (`BATCH_TOO_LARGE`).

**[invariant] Intake crash-recovery is explicit (A-RULE 036):** `IdempotencyRecords`
carries a **fenced lease** (`AttemptId`, `LeaseOwner`, `LeaseUntil`) exactly like
the outbox (§12.2). An intake that dies mid-handler leaves a stale `InProgress`
row; on restart it is **never retried blindly** and **never stuck forever**.
Because the settled outcome is written in the **same transaction** as the
financial effect (A-RULE 006), the record is the authority: a row is `Settled`
**iff** the effect committed. Recovery is therefore deterministic — an `InProgress`
row with an expired lease is **not** proof of a committed effect, so the owner (or
a new leaseholder) resolves it by reading the authoritative state, then either
completes the one-and-only transaction or marks it `Failed`; a client retry of the
same `(TenantId, OperationId)` meanwhile returns the stored result if `Settled`,
and is otherwise safely re-leased. There is no stand-alone `InProgress` commit that
can diverge from the effect.

**[invariant] Idempotency retention is explicit (A-RULE 040).** An `IdempotencyRecords`
row is retained **at least as long as the longest supported offline/retry window**
(the same window that sizes journal/tombstone retention, §PG.6 item 5) so a
long-offline device's retry still de-dupes. Past that window the full record may be
compacted to an **archival receipt** (`(TenantId, OperationId)` + `CommandFingerprint`
+ final effect reference) — enough to still answer "already done, here is the
outcome" for a very late retry without carrying the whole payload forever. Retention
is a reviewed policy, not unbounded growth and not an early purge that would re-admit
a duplicate.

**[invariant] HTTP status policy (A-RULE 024):** a *settled* outcome — Accepted,
Rejected, or Conflict — is a **successful conversation → HTTP 200** with the
outcome inside `SettlementResult`. Only transport/auth/shape failures use 4xx/5xx
with a `WireApiError`. A rejected invoice is a business answer, not an HTTP error;
conflating the two is what forces clients to guess.

**The settlement state machine** (defined once, in `Domain/Primitives`, reused by
B02/D05/E06):

```
Draft → LocallyPosted → PendingSettlement → Settled
                                          → Rejected
                                          → Conflict
                                          → Reversed   (terminal)
                                          → NeedsAttention
PendingSettlement → PendingSettlement is allowed (retry).
```
Transitions are an allow-table (`SettlementTransitions`); an illegal transition is
`SETTLEMENT_INVALID_TRANSITION`. The state is **always visible in the UI**
(§8.7), never hidden.

### §8.4 Idempotency ≠ ordering; dependency is explicit & server-resolved (R-B15)

**[invariant]** Two commands that must apply in order declare it **explicitly** —
`CausationId`/`DependsOnOperationId` plus an optional `ExpectedServerVersion`
precondition — and the dependency is resolved **server-side only** (A-RULE 021).
The client never decides server order from its `LocalSequence` (that is only the
client's own emission order) and never from any timestamp. A command whose
declared dependency is not yet settled is **deferred** (`DEPENDENCY_UNSETTLED`),
not guessed. `ExpectedServerVersion` is checked against the current UUID `Version`
of the target aggregate; a mismatch is a **conflict** the domain resolves (§8.5),
not a silent overwrite.

> Why separate from idempotency: idempotency answers "have I already done this
> one?"; ordering answers "may I do this one *yet*?". Merging them is a classic
> defect that lets a retried-out-of-order command apply a stale financial change.

### §8.5 Conflict policy — detect generically, decide by domain (R-B17)

**[invariant]** A version mismatch (the optimistic UUID `Version` precondition
fails) **detects** a conflict; **domain policy decides** the outcome (A-RULE 004).
There is no blanket client-wins / server-wins on business facts.

| Data class | Conflict resolution |
|---|---|
| UI preferences / display settings | generic **Last-Write-Wins** is allowed (the *only* place) |
| Posted invoice / payment | **immutable**; a change is a reversal / credit note / compensating entry (A-RULE 002/003) — never an in-place edit |
| Ledger & audit | **append-only forever**; a correction is a new balanced reversing entry (§11.1) — by construction cannot conflict |
| Inventory | **movement-based**; two concurrent movements **both apply** (additive convergence), on-hand re-derived (§11.2) |
| Appointment slot | **domain validation**; a colliding slot is **rejected**, not merged (`APPOINTMENT_SLOT_CONFLICT`) |
| Owner/pet demographics | explicit field-level **merge** through the single update funnel, never a whole-row LWW |

[decision] This is why the Datasync built-in resolvers (`ClientWins`/`ServerWins`,
whole-row LWW — `STACK.md §4.4`) are usable **only** for the UI-preference class;
everything financial routes through the command pipeline where the domain decides.

### §8.6 Contract versioning & mixed-fleet behavior (R-B31)

**[invariant]** Every replicated and every commanded contract carries an explicit
`ContractVersion`. The fleet is never homogeneous (desktops update at different
times), so the server supports **N / N-1 / N-2** deterministically:
- An **unsupported** version triggers a **controlled resync / upgrade prompt**,
  never a silent mis-map.
- Wire DTOs are **additive-only** within a major version; a breaking change is a
  new `V<N+1>` DTO and a bump of `WireContract.Version`, expected only at D08 and
  E03 (§18).
- The server maps an older command DTO onto the current handler explicitly; it
  never reflection-maps across versions.

**[invariant] Wire compatibility ≠ business-semantic compatibility (A-RULE 037).**
`ContractVersion` only proves the *shape* is readable; it does not prove the
*meaning* still matches. A long-offline desktop may post against a stale
`PostingRules` / pricing / tax / rounding / discount / valuation policy. So a
command that computes a financial effect carries a **`RuleSetVersion`** (the
monotonic version of the posting/pricing/tax policy it was computed under) beside
`ContractVersion`. On intake the server decides by policy window, not silently: a
**current/recent** rule set is accepted; a **superseded-but-migratable** one is
**transparently re-derived** by the server's authoritative rules (the client's
numbers are provisional, §8.1/§11.1, so the server's re-derivation is the truth);
an **out-of-support** rule set is **rejected with an explicit upgrade reason**
(`RULESET_UNSUPPORTED`), never force-applied under new semantics. The supported
`RuleSetVersion` window is a reviewed policy, sized to the longest supported
offline window (§PG.6 item 5).

### §8.7 The sync-failure UX contract (R-B16, A-RULE 024)

**[invariant] "Sync failed" alone is a defect.** Every non-settled command must
let the UI answer four questions, and the `SettlementResult` carries all four:

1. **What** failed (which command / aggregate, by id)?
2. **Why** (a domain `ErrorCode`, not a stack trace or a generic message)?
3. **Does it have a financial effect?** (`FinancialEffect { None, Applied,
   NotApplied, Unknown }` — `Unknown` is itself actionable: it means "confirm
   before retrying").
4. **Who must act** (the responsible role — cashier, manager, etc.)?

The desktop surfaces this on the exact record (invoice/payment), with the
settlement state (§8.3) and a correlation id for support. This is the contract the
Cashier/Sales screens (C07/C08) and the sync orchestrator (E06) implement.

---

## §9 Multi-tenancy & isolation (R-B19)

> One server, all clinics. Clean separation of each clinic's data is a force-1
> correctness requirement, not a convenience.

### §9.1 Shape: shared database, shared schema, `TenantId` discriminator

**[decision] One shared PostgreSQL database, one shared schema, a `TenantId`
discriminator on every tenant-scoped row.** No per-tenant database at launch
(operational cost on a weak single host; §17). `TenantId` is `varchar(26)` (ULID)
and is the **leading column of every tenant-scoped index**, no exceptions — that
is what keeps a tenant's working set contiguous and the weak host fast (force 4).
Deployment Stamps (`Tenant → Stamp → Database`) are a *designed-for* future for a
single tenant whose load or data-residency justifies a dedicated DB (§20); the
D02 data-access abstraction leaves room for it without a rewrite.

### §9.2 Three isolation layers that hold with database hardening OFF [invariant]

**The correctness bar: tenant isolation must be proven with PostgreSQL RLS
switched off.** RLS is *defense in depth*, never the only line. The three
mandatory layers, each independently tested (§19 gate L):

1. **Application authorization** — a request is bound to a tenant only via
   authenticated membership; a caller cannot name a tenant it is not a member of.
2. **Data-access tenant filter** — the EF Core `"Tenant"` **named query filter**
   (separate from `"SoftDelete"`, EF Core 10 feature) scopes every read/write to
   the ambient `ITenantContext.TenantId`. `IgnoreQueryFilters()` with **no
   argument** is banned and detected by a source scan + a runtime interceptor; a
   maintenance path may lift `"SoftDelete"` **only**, never `"Tenant"`.
3. **Replication data-view filter + server-side write stamping** — the sync
   surface projects only the caller's tenant rows, and `SaveChangesAsync` stamps
   `TenantId` **from the context, never from the incoming entity**.

**[invariant] Client-supplied `TenantId` is never trusted (A-RULE 012/013):** the
server resolves the authoritative `TenantId` from membership on every request; a
mismatch between an incoming entity's `TenantId` and the context raises
`TENANT_MISMATCH` and writes a **security audit** entry. A query executed with a
null tenant context **throws** `TenantContextMissingException` — it never silently
returns cross-tenant rows.

**[invariant] Required-navigation pitfall mitigation.** A required navigation into
a tenant-filtered entity would emit an `INNER JOIN` that can silently drop parent
rows (`STACK.md §6.4`); navigations into filtered entities are therefore modeled
**optional**, or both sides carry matching filters.

### §9.3 RLS as gated hardening

**[decision] PostgreSQL RLS is optional, gated hardening** (§PG.3), enabled only
after the §19/STACK gate proves transaction-local tenant context + non-owner
runtime + connection-pool-reuse safety — and only *after* isolation already holds
without it. When enabled: each transaction sets a server-resolved tenant via
`set_config('animora.tenant_id', @tenantId, true)` (transaction-local,
parameterized), re-applied on every begin/retry; a missing/empty setting **denies**;
the runtime role is non-owner, non-superuser, without BYPASSRLS, with FORCE RLS on
protected tables. RLS is never relied on to *replace* layer 2.

### §9.4 What crosses the tenant boundary (operator only)

Only an **operator** identity may query across tenants, and only through the
isolated read-model insight seam (§13.5, §16.3), never through the business
DbContext's tenant-scoped path. Cross-tenant analytics is deferred, consent/ADR
gated (§20).

---

## §10 Security planes & identity (R-B04, R-B20, R-B21, R-B29)

### §10.1 Three planes, independent and ordered [invariant] (A-RULE 023)

```
Authentication   — WHO are you?           (valid session / token)
     ↓   (never skipped, never merged)
Authorization    — MAY you do this action? (RBAC + policy + resource rule)
     ↓
Entitlement      — is your TENANT LICENSED for this capability? (§13.2)
```

**[invariant]** All three are checked, in this order, on every protected request.
They are never merged or short-circuited: a user may be authenticated and
authorized yet blocked by entitlement (expired subscription → read-only), and that
is a *different* answer than "not permitted". The server pipeline runs them as
distinct middleware/behaviors; the desktop mirrors the first two offline via the
capability snapshot (§10.4).

### §10.2 Authentication

**[decision] Local ASP.NET Core Identity**, opaque protected bearer/refresh tokens
for desktop, cookies for web (Microsoft's browser recommendation). No cloud IdP,
no JWT/OIDC dependency, no custom crypto — passwords use the framework
`PasswordHasher<T>` only. Passkeys are a supported-but-post-launch option. The
server is the sign-in authority; the desktop caches a session and a capability
snapshot for offline use (§10.4). Device registration binds a user to an
installation (fingerprint hash; the raw identity never leaves the device).

### §10.3 Authorization — stored RBAC + policy + resource rules

**[decision]** Three first-class ASP.NET Core authorization styles, evaluated in
order by a single `PermissionEvaluator`:
1. **RBAC** from the **stored** role definition (role ∪ extra − denied; deny
   wins) — the caller's self-claimed role/permissions are **never trusted**; an
   unknown permission code is `PERMISSION_UNKNOWN` (fail closed).
2. **Policy** predicates (a fixed, named set) for coarse action gates.
3. **Resource rules** (`IResourceAuthorizationRule<T>`) for per-entity checks
   (e.g. a posted invoice is not editable; only the owner may close a cash
   session).
Roles (manager/owner, doctor, receptionist, cashier, accountant) are
**extensible**: role definitions are data, and per-role screen/action gating is
driven by the same permission catalog the shell uses to filter navigation.

### §10.4 Offline session = signed, device-bound capability snapshot (R-B21)

**[invariant, A-RULE 029]** An offline client's authority is a **signed,
device-bound capability snapshot**: the set of permission codes the user holds,
each with a **per-capability offline expiry window** keyed to its risk level
(Low 30 d, Normal 14 d, High 72 h, Critical = online-only). It is bound to
`(tenant, user, device)` and the user's security stamp.

**[decision] The snapshot is sealed with an asymmetric detached signature, never
an HMAC.** The server signs with a private key; the offline desktop verifies with
a **public** key only. HMAC is symmetric, so offline verification would force the
secret onto the untrusted client, where its owner could extract it and mint any
capability — it cannot satisfy *offline verify + untrusted client* at once. The
snapshot therefore reuses the **exact licence trust model** (§13.2): the same
canonical-JSON payload form, `KeyId`-selected rotation-aware public key set, and
detached-signature envelope. The private half lives only in server `Issuance/`;
the desktop consumes the client-reusable verifier half (§13.2) and holds no
signing secret.

Fail-closed rules: before warm-up, on sign-out, on a locked/expired/absent
session, or on a missing/tampered/mismatched snapshot → **deny**. A tampered
snapshot is dropped whole, never re-signed. Expiry is evaluated against
`max(now, monotonic high-water)` so a backward clock change cannot revive an
expired capability (A-RULE 011). **A revoked device loses offline capability at
its next server contact** — revocation is honoured the moment the device can be
told.

### §10.5 Transport & certificate trust

HTTPS via Kestrel with an **internally issued** certificate (no public CA at
runtime, force 3). TLS version is OS-negotiated (§4). The certificate
trust-provisioning process (installer/MDM) is an operations runbook item (§17).

### §10.6 Key material & its recovery invariant [invariant]

**ASP.NET Core Data Protection** key ring persisted to the database (its own tiny
`DbContext`, so it survives a business-schema rebuild) with a **stable
`ApplicationName`**. **Recovery invariant:** if the key ring is lost or
regenerated, all existing cookies/antiforgery/protected tokens become
undecryptable — therefore the key ring is **on the disaster-recovery matrix**
(§17.3) and is part of every backup and every host move (§3.3). A missing/
unreachable key ring → the host serves liveness but **refuses** readiness/auth/
business routes (no in-memory substitute key ring, A-RULE 027).

### §10.7 Audit plane [invariant] (R-B29)

Every **security-relevant** and every **financial** action writes an
**append-only** audit entry (actor, tenant, device, operation, before/after
reference, correlation ids), on both client and server. Audit is append-only
forever (§11.1 class). Correlation ids (`TenantId / OperationId / DeviceId /
RequestId`) flow end to end (§15.1). Audit rows never contain a secret or a full
PII/financial row body (`UnsafeEntityLogging = false`).

---

## §11 Financial & inventory invariants (R-B22, R-B23)

### §11.1 The ledger: one writer, append-only, balanced [invariant]

**[invariant, A-RULE 002]** Accounting is a **double-entry, append-only, always
balanced** ledger:
- **Exactly one component constructs a `JournalEntry`** — the `LedgerPostingHandler`,
  which subscribes to the domain events every money/stock context raises
  (invoice posted/refunded/voided, payment registered, stock movement, goods
  receipt, cheque cleared/bounced, cash session closed, expense posted). This is
  asserted by a test that scans the solution for any other `JournalEntry`
  constructor.
- **Balance is structural:** every entry is built through `PostingRules` and
  `JournalEntry.Create`, which refuses `Σdebit ≠ Σcredit` (`LEDGER_UNBALANCED`),
  `< 2` lines, a non-positive amount, a mixed currency, or a non-postable account.
- **Posted records are immutable.** A posted invoice, a payment, a journal entry
  are never edited or deleted; a correction is a **compensating entry** — a
  reversal journal, a credit note / `InvoiceAdjustment`, a negative `Payment`
  paired with its adjustment (A-RULE 003). This is also *why* a posted financial
  fact can never produce a sync conflict (§8.5): there is nothing to overwrite.
- **Client and server post from the same `PostingRules`**, so a locally posted
  provisional entry and the server's authoritative entry are computed identically
  — the server re-derives, it does not trust the client's journal (§5).
- **Accounting periods** are forward-only (`Open → Closing → Closed → Locked`);
  a late event after close posts into the **next open period** (never back into a
  closed one, `PERIOD_CLOSED`).

**Money representation** (R-B24): integer **minor units + currency code**,
currency-specific scale (JPY 0, most 2); the high-precision domain `Money` type
and its single rounding boundary (`ApplyPercent`, away-from-zero) own all tax /
discount / allocation math. All stored time is **UTC**; Jalali/Persian digits
exist only in Desktop presentation.

### §11.2 Inventory: movement-based, on-hand derived [invariant] (R-B23)

**[invariant]** There is **no stored absolute on-hand** that business code
overwrites. The only authority is the **append-only `StockMovements` ledger**;
on-hand is a **pure derivation** (`StockCalculator`: signed sum + moving weighted
average cost). A maintained `StockOnHandSnapshot` **cache** exists for O(1) grid
reads, but it is a projection with a proof: it is updated inside the *same
transaction* as every movement from every path, and a `RebuildStockSnapshots`
maintenance op recomputes it from the ledger (restore / quarantine-rebuild call
it). The ledger wins on any disagreement. **Convergence:** two concurrent stock
movements both apply (additive, §8.5) — inventory never needs a conflict winner.

### §11.3 Why these two classes drive the whole sync design

The ledger's append-only immutability and inventory's additive convergence are
exactly what make it **safe** for money/stock to ride the command pipeline
(§8.3) rather than generic replication: there is never a "which version wins"
question on a financial fact, only "has this command settled, and if not, what's
the honest status" (§8.7). This is the unifying idea behind forces 1 and 2.

### §11.4 Read models for reporting [invariant] (R-B35)

Financial/operational reporting reads **projected fact tables**
(`DailyFinanceFact`, `ProductSalesFact`, `OwnerFinancialSnapshot`, activity feed),
maintained **off the OLTP path** by a background worker draining the outbox, and
rebuildable from the ledger. Reporting never rides the OLTP transaction
(A-RULE 034) and every reporting cache is bounded and tenant-safe (key = tenant +
user + resource + version, A-RULE 033). §11.4.1 (the server projection of these
facts) is owned by D07/D13; it re-derives the same facts the client computes.

#### §11.4.1 Server projection & bounded DB retry [invariant]

**[invariant]** The server maintains the same fact tables from its own authoritative
ledger (the client-side projection and the server-side projection apply the identical
`PostingRules`, so cross-device dashboards converge, §18-CT). The projection runs on a
background worker draining the server Outbox (§12.1), never inside an OLTP request.
**Database retry is bounded and whole-unit:** a transient PostgreSQL failure is
classified by **SQLSTATE** and retried as a **complete unit of work** (the whole
command/handler re-runs idempotently, §8.3), never as a mid-stream resume; there is
**no global ORM buffering and no stale-token replay** of a half-applied transaction.
The retry budget is bounded (`EnableRetryOnFailure` + a low `MaxRetryCount`) so a
sick DB fails fast into the resilience envelope (§12.3) rather than amplifying load
on the weak host.

#### §11.5 Reporting isolation is enforced, not hoped for [invariant] (R-B35)

**[invariant]** Reporting/aggregation isolation is a **tested** property, not a
convention: E02/D13 insight queries execute under the **`Reporting` half of
`QueryBudget`** (§PG.4), can never occupy the OLTP connection budget, and read only
projected fact tables (§11.4) — never a live join across OLTP aggregates
(A-RULE 034). Every reporting cache key carries tenant + user + resource + version
and a missing component is a **test failure** (A-RULE 033, §16.3). This is the
behavioral guarantee D13 wires and E02 fills through the operator insight seam
(§13.5).

---

## §12 Reliability patterns (R-B09, R-B25)

### §12.1 Transactional Outbox [pattern]

**[invariant]** A state change and the message that announces it are written in
**one** database transaction (the business row + an `Outbox` row), so an effect
is never published without its state, and state is never committed without its
effect being scheduled. A `BackgroundService` relay drains the outbox.

### §12.2 Atomic claim, idempotent consumers, fencing [pattern]

- **Atomic claim:** the relay claims due rows with a single
  `UPDATE … RETURNING` over `FOR UPDATE SKIP LOCKED`, with due/expired
  eligibility and a **fenced lease token** — two workers never process the same
  row, and a dead worker's lease is reclaimable. A nullable initial lease is not
  accidentally excluded by a bare `< now` predicate.
- **Idempotent consumers:** a consumer records a uniqueness marker + its effects
  in one transaction, so a redelivered message is a no-op.
- **[invariant]** `SKIP LOCKED` is used for *work distribution*, never as the
  *state-feed* cursor (that is the journal, §PG). The two are different problems.

### §12.3 Resilience envelope

Retry with capped exponential backoff **+ jitter**; circuit breaker into an honest
"working offline" state; timeouts on every outbound call; **bulkhead** — workers
run under bounded concurrency + a queue and cannot starve request handling
(force 4). The client sync loop has **no fixed interval**: its delay is a pure
function of idle/recent-change/catch-up state × `2^failures` capped, × ±25%
per-device jitter seeded from the installation id — so a fleet restarting together
does not converge into a reconnect storm (§19 gate I).

### §12.4 Crash resilience [invariant] (R-B09)

No data loss on kill / crash / power loss: the local store is WAL + busy-timeout
with startup `quick_check`, corrupt-DB **quarantine-and-rebuild** (move, never
delete) and backup/restore; workers are resumable (claim leases); provisional
local commands are durable in the journal and replayed on restart. The server's
Data-Protection recovery invariant is §10.6.

---

## §13 Commercial model: Subscription vs. Licensing (R-B05, R-B06, R-B30, R-B36)

### §13.1 The architectural split [invariant]

**[invariant, A-RULE: R-B30] Subscription and Licensing are separate mechanisms,
and the *runtime* depends only on Licensing.**

```
Subscription (server bounded context)   Licensing (signed artifact)
  the commercial truth:                   the runtime entitlement truth:
  plans, seats, billing periods,          a signed, offline-verifiable licence
  grace, cancel, renew, event log         bound to one installation, fail-closed
         │                                         ▲
         └──── issues a licence on ─────────────────┘
               provisioning / renewal
```

The running clinic **never calls the Subscription context to decide if it may
operate** — it verifies its **licence** (§13.2) entirely offline. This is what
keeps force 3 (zero-Internet runtime) and force 2 (offline-first) intact while
still giving the vendor full commercial control.

### §13.2 Licensing — signed, offline-verifiable, installation-bound (R-B06)

**[decision]** The licence is a **detached-signature envelope** (not a JWT — a JWT
would drag in a token/clock stack this product avoids):
`Base64Url(payloadJson) + "." + Base64Url(signature) + "." + KeyId`, where
`payloadJson` is canonical JSON (the same canonicalizer as `CommandFingerprint`).
It is verified **entirely offline** against a **rotation-aware** public key set
(`KeyId` selects the key), bound to **one sealed installation identity**, and
checked against the **monotonic high-water clock** (a backward wall clock cannot
extend it, A-RULE 011). The policy matrix is **fail-closed**: unverifiable /
expired / wrong-installation / tampered → gated capabilities off (read-only or
locked, by capability risk).

**[decision] Honest threat model.** The signature guarantees **artifact authenticity
and tamper-detection** — a forged or altered licence is rejected. It is **not** an
unbreakable anti-piracy boundary: the binary runs on hardware the customer owns, so
a determined owner can patch the verifier out (OWASP: client-side gating is never a
hard security boundary). Real enforcement of an entitlement therefore lives
**server-side** for any online-touching capability (§13.3); a *truly offline*
capability cannot be absolutely enforced against the machine's owner, and the design
does not claim otherwise. Licensing's job is to make the honest path easy and
tampering evident, not to defeat a motivated local attacker.

**[decision] The verifier is a client-reusable library half.**
`Animora.Server.Licensing/Verification/` references only `Animora.Domain` +
`Animora.Contracts.Wire` (no PostgreSQL, no ASP.NET Core), so the desktop (E07)
consumes it without pulling a server type. `Issuance/` is server-only.

### §13.3 Subscription bounded context (server-only)

Aggregates: `Plan`, `Subscription` (status/seat limit/grace/cancel), `BillingPeriod`
(gap-free sequence), `Seat`, and an **append-only `SubscriptionEvent`** log
(gap-free per subscription). The operator API exposes
**create / extend / reduce / suspend / cancel / renew** + seat management +
deferred operator maintenance actions. [decision] The clinic-serving server
**records** `BillingPeriod` amounts for reference but **never charges a card** —
payment is the public website's job (§3.1).

### §13.4 Self-service onboarding = one code path, two callers [invariant] (R-B36)

The public website's "register + buy" step is received by the server as an
**async, idempotent, authorized provisioning intent** that **reuses the exact same
create-tenant + subscription operation** the human operator uses. **[invariant]
One provisioning engine, two authorized callers** (a human operator vs. the
website's service identity) — never two code paths that can drift. The intent is
idempotent (a retried webhook provisions once) and authorized (only the website's
service identity may call it). Licence issuance (§13.2) is the downstream leg; the
customer then downloads, logs in, and the device activates (E07).

### §13.5 Operator admin web & insight seam (R-B05, R-B11)

A **Blazor admin web**, hosted by the same server, operator-role only, drives the
operator API. [decision] A read-model **insight seam** (`ITenantInsightQuery`)
lets the operator later see per-tenant stock / per-store charts — **isolated from
OLTP** (§16.3), consent/ADR gated (§20), and never through the tenant-scoped
business DbContext. This is the "admin wants charts later" operability need (§3.3)
turned into a seam that exists now and is filled later.

---

## §14 Files (R-B26)

**[pattern] Valet-Key + a reconciled lifecycle.**
- **Storage:** SQL metadata row (ordinary columns) + **binary content on the
  server filesystem** (content-addressed by SHA-256). No object-storage SDK at
  launch; large blobs never live inside SQL rows (row/backup-size).
- **Lifecycle [invariant]:** `Staged → Uploaded → HashVerified → Committed →
  Available`, with a **mandatory reconciliation job** that garbage-collects
  staged-but-abandoned content and flags metadata/content mismatches. A document
  reference stores a **FileId**, never a filesystem path (A-RULE 020).
- **Access [invariant]:** a **Valet-Key** (a short-lived, scoped grant) authorizes
  each transfer; **a path is never a credential**. Resumable transfer is supported
  for large files. Files sync as metadata (replicated) + on-demand content fetch
  (valet-keyed), never as SQL blobs on the state feed.

---

## §15 Observability (R-B27, R-B29)

### §15.1 Structured logging, correlation, health

**[invariant] Local-only observability.** Structured logging (Serilog over
`Microsoft.Extensions.Logging`) writes to **local rolling files** with
**redaction** (no secret, no full PII/financial body); **no console sink outside
Development, no external sink, ever** (A-RULE 032/016). Correlation ids
(`TenantId / OperationId / DeviceId / RequestId`) are enriched onto every event
and flow end to end, so one gesture is traceable across client → command → server
→ outbox. Health endpoints are §4. A PII-free **diagnostics bundle** can be
exported for support.

### §15.2 Metrics & traces — optional, local collector only

**[invariant]** OpenTelemetry exports **only to a local LAN collector**, never a
mandatory external endpoint; **removing the collector must not break the app** (no
default external exporter is wired). No Application Insights, no cloud log sink, no
external metrics backend is a runtime dependency (force 3).

---

## §16 Performance strategy (weak host & weak client) (R-B07, R-B08, R-B35)

### §16.1 The strategy (the concrete .NET features are `STACK.md §10`)

**[decision] Push work to where it is cheap, and off the hot path:**
- **Sync is delta-only** and server-frozen (§16-CT); a client never pulls the
  whole dataset after the first catch-up.
- **A request never does work a background worker can do** — projections, outbox
  relay, reconciliation all run on bounded-concurrency workers (§12.3).
- **Client startup is non-blocking:** the desktop reaches first frame without
  awaiting DB migration, a sync pass, or any licence round-trip; store init,
  seeding and rebuilds run off-thread behind a readiness gate.
- **Grids virtualize; paging is mandatory** (max page size 200); no unbounded
  `ToListAsync()` on a business table; every grid/calendar/lookup query has a
  covering `IX_<Table>_<Cols>` index led by `TenantId`.
- **Computation is pushed to clients** where safe (totals, formatting), with the
  server re-deriving the authoritative result (§5).

### §16.2 Scale-out is a future deploy switch, not launch code

**[decision]** Launch is single-instance. The only pre-designed scale-out is a
**Redis SignalR backplane** if/when a second web instance is deployed — a
**deploy switch** (a package + config), not an architecture change, and gated by
an ADR (A-RULE 031). A message broker, a read replica, a separate reporting/OLAP
store, and Deployment Stamps are all deferred with explicit triggers (§20).

### §16.3 Reporting isolation & bounded tenant-safe caches [invariant] (R-B35)

Reporting/aggregation never rides the OLTP transaction (A-RULE 034): it reads
projected fact tables (§11.4) under a **separate budget**. Every cache is
**bounded and tenant-safe** — the key always includes **tenant + user + resource +
version** (A-RULE 033); a missing key component is a **test failure**, not a bug
report. The operator insight seam (§13.5) obeys the same isolation.

### §16.4 Measured, not asserted [invariant] (A-RULE 035)

**No user-count or throughput number is guaranteed by assertion.** Capacity on the
1–3 core / 2–4 GiB host is proven by a **published load harness** and recorded
(§19 gate T). The reference hardware is a planning reference only.

---

## §17 Deployment, operability & recovery (R-B33)

> This is the force-5 section: a single operator must update, back up, restore and
> relocate the server without surprises.

### §17.1 Deployment topology & migration discipline

One self-contained server process + PostgreSQL + a filesystem tree, on one host
(§3.2). **[invariant] Migrations run before traffic under a DDL role; runtime
startup only checks schema version/readiness** — the running app's role has no
DDL rights. `EnsureCreated` is forbidden outside test fixtures. Server migrations
are numbered from `101` and are **never edited by a later phase**; the
Data-Protection key-ring context has its own migration sequence (§10.6).

### §17.2 Update / upgrade / rollback

A new version ships as the same single process; deployment is **stop-migrate-start
or blue/green** (the admin web + wire contracts tolerate N/N-1/N-2 fleets, §8.6).
A rollback restores the previous binary; because migrations are additive and the
wire contract is versioned, a one-step-back rollback does not strand clients.
Desktop updates verify against the same trust chain (signed/notarized where the OS
requires it).

### §17.3 Backup, restore & host move — the disaster-recovery matrix [invariant]

**[invariant] Single-host is a deliberate choice with a *stated* recovery target,
not an unbounded one (A-RULE 038).** Single-host (§3.2) means a host failure makes
**all tenants unavailable** until restore — backup enables disaster *recovery*, not
*availability*. The spec therefore publishes explicit objectives the operator runbook
must meet and the restore drill must prove:
- **RPO** — the maximum tolerable data loss, set by backup cadence (PITR/WAL
  archiving for a small RPO; scheduled logical dumps otherwise). The chosen cadence
  is a measured, documented number, never "best effort".
- **RTO** — the maximum time to readiness on a clean host, proven by the gate-R
  drill (§19) — the drill records elapsed restore time, so RTO is *measured*.
- **Off-host durability** — at least one backup copy lives on **different hardware
  from the live host**; a backup co-located with the data it protects does not count
  toward the durability target.
- **Promotion/failover** — host move is the documented promotion path (same restore
  + repoint config); the spec names the **trigger** at which single-host is no longer
  acceptable (the scale-out switch, §16.2) so the decision is explicit, not drift.

**The single backup set is three things, and all three are mandatory:**
1. the **PostgreSQL** database (logical or physical/PITR),
2. the **file content** tree (§14),
3. the **Data-Protection key ring** (§10.6) — *forgetting this is the classic
   silent disaster: the DB restores but every token is undecryptable*.

**[invariant] The three parts are one consistency boundary, not three independent
snapshots.** The DB and the filesystem are captured as a **single point-in-time
set** — a write-freeze (or an equivalent PITR/LSN + file-manifest pairing) brackets
the capture so the DB backup point and the file snapshot describe the *same* instant
(§PG.5). Content-addressing (§14) makes the common drift benign in one direction — a
committed `FileId` is immutable, so a DB that references it restores correctly as
long as that content is in the tree — but the freeze is what guarantees the other
direction (no DB restored *behind* its files, no orphaned/missing content). A restore
proves file↔metadata parity, not just that each part loaded.

**[invariant] Restore is rehearsed, not assumed** (§19 gate R): a periodic drill
restores all three onto a clean host and proves the server reaches readiness and
serves a known tenant. A **host move** is the same operation plus repointing
config (env/secret-store) — no host-pinned state exists beyond `DataRootPath`, the
DB, and the key ring, and the stable `ApplicationName` means the key ring remains
valid after the move (§3.3). SBOM + a **native binary inventory** ship with each
release (supply chain; `STACK.md §16`).

### §17.4 Zero mandatory external-Internet dependency at runtime [invariant] (R-B27)

**The server must run fully with the Internet cable pulled.** No runtime path may
require the public Internet: the database is local, identity is local, the CA is
internal, licence verification is offline, observability is local-only, and the
public website is explicitly *not* a runtime dependency (§3.1, §13.1). All web
assets (JS/CSS/fonts/icons + Blazor framework files) are packaged — **no `cdn…` /
`fonts.googleapis…`** reference exists in built output (verified by an isolation
test). This is a hard product rule, enforced here by never wiring a default
external endpoint anywhere (§15.2, A-RULE 016/032).

---

## §18 Wire contract versioning & mixed fleets (R-B31)

(The spine rule is §8.6; this is the deployment-facing elaboration.)
`WireContract.Version` is bumped **only** by a phase that changes a wire shape
(expected: D08 state contracts, E03 the client adapter). Within a major version,
DTOs are **additive-only**. The server accepts **N / N-1 / N-2**; an unsupported
version is a **controlled resync/upgrade**, never a silent mis-map. Mixed-fleet
behavior (a clinic running N-1 while another runs N) is **deterministic** and
tested (§19 gate V). The contract-version header travels on every request so the
server can branch mapping explicitly.

---

## §19 Validation gates (the earned-quality bar)

> No "production-ready" / "10/10" label is **declared** — it is **earned** when
> these gates pass. These are behavioral/architectural gates; the narrow
> technology PoCs (Datasync qualification, SQLCipher, native-SQLite assertion,
> RLS mechanics) are `STACK.md §12` and are not duplicated here. A gate is an
> **obligation**, not a result of writing this document.

| Gate | Proves |
|---|---|
| **A — Idempotency & financial-effect-once** | identical retry returns stored result; same `OperationId`+different fingerprint is audited; never two financial effects; a crash mid-`InProgress` recovers deterministically via the fenced lease, never double-applies, never sticks (§8.3) |
| **D — Dependency & ordering** | explicit dependency resolved server-side; out-of-order/unsettled dependency is deferred, never mis-applied (§8.4) |
| **C — Conflict policy** | version mismatch detects, domain decides; LWW only for UI prefs; posted invoice immutable; movements converge additively (§8.5) |
| **N — Non-destructive sync** | a pull never overwrites unsent intent; full resync preserves the pending overlay and switches atomically (§8.2) |
| **B — Immutable batch atomicity** | base+checkpoint advance all-or-nothing; a partial page can't advance the checkpoint (§16-CT) |
| **L — Tenant isolation with hardening OFF** | all three isolation layers hold with RLS off; null tenant context throws; client `TenantId` ignored+audited (§9.2) |
| **P — Three planes** | AuthN/AuthZ/Entitlement independent and ordered; expired entitlement ≠ "forbidden" (§10.1) |
| **O — Offline capability** | asymmetrically-signed device-bound snapshot verifies with a public key only (no client-side secret); fails closed at expiry; clock rollback can't revive; revoked device loses offline at next contact (§10.4) |
| **K — Key-ring recovery** | missing key ring refuses readiness/auth; restore with the key ring succeeds; without it, tokens fail honestly (§10.6, §17.3) |
| **U — Licence authenticity & fail-closed** | offline verify, installation binding, tamper + clock-rollback *detection*, fail-closed matrix; claims authenticity/tamper-evidence, **not** unbreakable anti-piracy — online capabilities enforce server-side (§13.2) |
| **F — File lifecycle** | Staged→…→Available with reconciliation; Valet-Key required; a path is never a credential (§14) |
| **S — Subscription single-engine** | website intent and operator action share one idempotent provisioning path (§13.4) |
| **I — Reconnect storm** | jittered per-device backoff; a fleet restarting together does not converge (§12.3) |
| **M — Mixed fleet** | N/N-1/N-2 deterministic; unsupported version → controlled resync; a stale `RuleSetVersion` is re-derived or rejected-with-reason, never force-applied under new semantics (§8.6, §18) |
| **R — Rehearsed restore** | DB + files + key ring restore onto a clean host to readiness + a known tenant (§17.3) |
| **Z — Zero-Internet** | the whole runtime operates with egress blocked; no `cdn…`/external endpoint in built output (§17.4) |
| **T — Measured capacity** | published harness records real capacity on 1–3 core / 2–4 GiB; no asserted user count (§16.4) |
| **PG — Journal/snapshot integrity** | commit-safe tenant counter ordering, child/direct DML capture, retention, same-snapshot watermark/rows, bounded immutable extraction (§PG.6) |

Gate coverage is owned by E08 (+ the PG gates alongside the D08 journal work);
every gate maps to a requirement in §1.

---

## §PG PostgreSQL critical-sync design (the state-feed engine)

> This is the server half of §8.2 and the single most-cited section by the D/E
> phases. It is *architecture*, not stack: `STACK.md §6` owns the technology facts
> (PostgreSQL 18, Npgsql 10.0.3, named query filters, RLS primitives, PITR); this
> section owns **what Animora builds with them**. Read it before D02/D08/D13.

### §PG.1 Source basis & baseline [decision]

The server database is **PostgreSQL 18** via `Npgsql.EntityFrameworkCore.PostgreSQL
10.0.3` on EF Core 10. This section's design was reviewed **2026-10-08** against the
primary documentation bodies that govern its correctness — registered here as the
authoritative source set so a later revision re-checks the same corpus:

1. **PostgreSQL isolation & locking** — REPEATABLE READ snapshot semantics,
   `SELECT … FOR UPDATE SKIP LOCKED`, advisory locks, serialization-failure retry.
2. **PostgreSQL triggers & functions** — `BEFORE` row triggers, `gen_random_uuid()`.
3. **PostgreSQL Row-Level Security** — `USING`/`WITH CHECK`, `FORCE ROW LEVEL
   SECURITY`, `BYPASSRLS`, `set_config`/`current_setting` transaction-local GUCs.
4. **Npgsql** — timestamp/`timestamptz` mapping, connection pooling & multiplexing,
   concurrency tokens, `xmin`, PITR/physical backup guidance.

No PostgreSQL commercial edition, extension, logical-replication slot, or broker is
a dependency (force 3/4). The security-maintained minor is recorded in the
deployment manifest, never hard-coded in app logic.

### §PG.2 Server concurrency token: UUID `Version` [invariant]

**[invariant]** The server's mutable concurrency token `Version` is a PostgreSQL
`uuid`, **regenerated on every INSERT and UPDATE by a database `BEFORE` row
trigger** (`gen_random_uuid()`), so *every* write path — EF Core, a governed direct
SQL maintenance statement, a trigger-driven cascade — advances it. The design
consequences, all binding:

- **EF mapping:** `ValueGeneratedOnAddOrUpdate()` + `IsConcurrencyToken()`, value
  retrieved via `RETURNING`; **never `IsRowVersion()`** (that is a provider-specific
  byte token with different semantics). A Domain `string` ↔ `Guid`↔`uuid` converter
  (`EntityVersionCodec`) keeps the Domain free of a database type; persistence-only
  models may hold `Guid` directly.
- **Wire form:** canonical lowercase hyphenated UUID **string**, compared by
  **equality only** — never ordered, never parsed for recency. A UUID here is
  *opaque concurrency evidence*, not a business identifier and not a sequence
  (A-RULE 009/010).
- A conditional update that matches **zero rows** surfaces as an EF concurrency
  conflict; **domain policy decides the outcome** (§8.5), the data layer never
  auto-retries a business write by blindly re-reading.
- `xmin` is **not** exposed as a durable offline version or a feed cursor (its
  wraparound and vacuum semantics make it unsafe as a client-held token).

### §PG.3 Row-Level Security = gated hardening, never the only line [decision]

RLS is **defense in depth** layered *on top of* the three application isolation
layers (§9.2), which must be proven to hold with RLS **off** (gate L). The full
behavioral rule is §9.3; the PostgreSQL-specific mechanics this section fixes:

- Policies use `USING` (read visibility) + `WITH CHECK` (write admission); the
  tenant is injected per transaction with parameterized
  `set_config('animora.tenant_id', @tenantId, true)` (transaction-local, **not**
  connection-global), **re-applied on every `BEGIN`/retry**. A missing/empty
  `current_setting('animora.tenant_id', true)` **denies** (fail closed).
- The runtime role is **non-owner, non-superuser, without `BYPASSRLS`**;
  `FORCE ROW LEVEL SECURITY` is set on protected tables (a table owner otherwise
  bypasses its own policies). **Caveat recorded:** a superuser still bypasses RLS,
  and `TRUNCATE`/FK existence checks are not ordinary row-policy paths — hence RLS
  can never *replace* layer 2.
- The journal, tombstone/deleted-key tables and frozen batches are protected by
  policy **explicitly** too — a tenant must not read another tenant's change feed.
- Npgsql **pool reset stays on**; enabling RLS is gated by passing A→B→A reuse,
  mid-transaction cancellation/rollback, and parallel-plan tests (`STACK.md §6.3`),
  so a pooled connection can never leak one tenant's GUC into another's request.

### §PG.4 Weak-host resource governance [invariant] (R-B07)

**[invariant] The whole server — app + PostgreSQL + file store — co-locates on one
1–3 core / 2–4 GiB host, and the connection budget is bounded and summed, not
per-tenant.** The `DatabaseOptions` profile:

- **One shared Npgsql data source per *workload*** (OLTP write, OLTP read/no-track,
  Data-Protection key-ring), never a pool per tenant and never an unconstrained
  read or DP pool. `MinPoolSize = 0` so idle pools release memory.
- **Combined pool maximum is a validated sum: ≤ 12 on a 2 GiB host, ≤ 20 on 4
  GiB**, with headroom explicitly reserved for DB admin/backup connections. Startup
  validation refuses a configuration whose pool sum exceeds the profile.
- **One background worker / one export at a time:** journal extraction, projection
  rebuild and reporting run under a `SemaphoreSlim(1)` that shares a background
  admission budget, so aggregation **can never occupy the OLTP connection budget**
  (bulkhead, §12.3). A `QueryBudget` splits **OLTP vs. Reporting**; E02 insight
  queries must use `Reporting` (A-RULE 034).
- **Bounded REPEATABLE READ snapshot lifetime (A-RULE 039):** a frozen-batch snapshot
  (§16-CT.1) is held **only long enough to capture**, never for the whole
  materialize/compress/page/store cycle. The capture reads the watermark + the row
  keys/versions inside the short-lived RR transaction and **commits**; heavy
  materialization, compression and paging happen **outside** the snapshot off the
  immutable, content-addressed batch. A `MaxSnapshotLifetime` budget bounds any open
  RR transaction; exceeding it **aborts and restarts** the capture rather than
  pinning old tuples. The **oldest open snapshot age is monitored** — on this weak
  host a long RR transaction would defer VACUUM and bloat the tables, so a stuck
  snapshot is an alert, not an invisible cost.
- Conservative PostgreSQL memory: small `shared_buffers`, per-operation `work_mem`
  (not global), autovacuum enabled, WAL/durability protected, OS/app headroom
  measured. **No number here is a guarantee** — capacity is proven by the §PG.6 /
  §16.4 harness on both the 2 GiB and 4 GiB profiles; an unconstrained or SKIPPED
  result may never authorize a constrained release (A-RULE 035).

### §PG.5 Backup, restore & migration discipline (PostgreSQL-specific) [invariant]

- **Migrations run before traffic under a DDL-capable role; the runtime role has no
  DDL rights** and startup only checks the schema version (§17.1). `EnsureCreated`
  is forbidden outside test fixtures. Server migrations are numbered from `101`,
  **never edited by a later phase**; the Data-Protection key-ring context carries
  its own independent migration sequence (§10.6).
- The journal triggers, direct-child/root capture rules, tombstone tables and
  retention windows are **reviewed migrations**, not runtime-mutable config.
- **Backup is the three-part set of §17.3** (DB + file content + key ring). The
  PostgreSQL leg is logical dump or physical/PITR; a restore drill
  (§PG.6 / gate R) proves staging/count/hash/FK/ledger/stock/file/receipt parity
  against the source and rehearses write-freeze + rollback.

### §PG.6 Journal/snapshot integrity obligations [gate] (gate PG)

The journal and immutable-batch design (§16-CT) is only *trusted* once these are
proven on a **real PostgreSQL 18** instance under both §PG.4 profiles (no mocked
DB, no SKIPPED DB test can pass CI):

1. **Commit-ordered tenant counter** — `ChangeSequence` is assigned so its visible
   order matches **commit** order, never statement/`now()` order, with no gaps a
   consumer could mistake for a lost change (§16-CT.2).
2. **Complete capture** — direct child-row and root DML, governed direct SQL, and
   cascade effects all produce journal rows; a tombstone retains the deletion key.
3. **Same-snapshot watermark + rows** — a batch's watermark and its rows come from
   **one** REPEATABLE READ snapshot (never a watermark read in a second
   transaction).
4. **Bounded immutable extraction** — extraction is paged, memory-bounded, and the
   frozen batch is never re-materialized differently on a re-read.
5. **Retention** — tombstone/journal retention is long enough to serve the longest
   supported offline window; a client past retention is forced into a full resync
   (§17-CT), never served a silently-incomplete delta. The **same window** sizes
   `IdempotencyRecords` retention and the `RuleSetVersion` support window (A-RULE
   040/037) so de-dup and rule-set acceptance stay consistent with the feed.

### §PG.7 Server `Version` ↔ wire mapping & command ordering token [invariant]

**[invariant]** The command envelope's ordering precondition and the journal's
concurrency evidence are the **same opaque UUID token**, mapped explicitly, never
reflection-mapped:

- The **client-side** local revision (a small numeric counter in the SQLite
  replica) is **local bookkeeping only** — it is *never* sent as server concurrency
  evidence and never compared to a server token.
- The wire `CommandEnvelope` V2 carries `ExpectedServerVersion` as an **additive,
  optional `string?`** holding the canonical UUID the client last observed for the
  target aggregate. Introducing it bumped `WireContract.Version` to **v2** at D05;
  it is **additive-only** (V1 payloads remain valid, a compatibility DTO suffix
  `V1` does **not** reset the envelope version). All later D/E phases keep
  `WireContract.Version = v2` unless they change a wire shape (§8.6, §18).
- On intake, the server compares `ExpectedServerVersion` to the target aggregate's
  **current** UUID `Version`: present and **≠** current → `Conflict` (domain
  resolves, §8.5); absent → the command carries no optimistic precondition. This is
  the precondition half of §8.4; it is orthogonal to idempotency (§8.3).
- **E06** implements the V1-journal → V2-wire mapping: it captures the
  server-base UUID token on pull, sends `DependsOnOperationId` + the captured
  `ExpectedServerVersion` on the dependent command, and records the
  `AppliedServerVersion` the server returns on settlement.

---

## §16-CT Critical-sync transport: immutable multi-entity batches

> The "CT" sections (16-CT–18-CT) are the **transport & client-apply** contract
> for the state feed whose server source is §PG. They are numbered `-CT` so they
> sit next to the performance section (§16) they are governed by, without renumbering
> the cross-cited §17/§18. Owning phases: **D08** (server side), **E03/E04/E06**
> (client side).

### §16-CT.1 Why a frozen batch, not a live timestamp cursor [decision]

**[decision]** A catch-up pull reads a **batch frozen from one snapshot**, not a
live `WHERE UpdatedAt > cursor` query against the moving table. A timestamp cursor
against live data is the classic source of **silent gaps and torn reads**: rows
committed out of timestamp order, same-microsecond bursts, and a long-running page
that straddles concurrent writes. Freezing a batch from a single REPEATABLE READ
snapshot (§PG.6 item 3) makes the delta **self-consistent and reproducible**: the
same `BatchId` always yields the same rows, so a resumed or retried download is
deterministic. The snapshot is held **only for the capture**, not for the whole
materialize/page/store cycle (§PG.4, A-RULE 039) — once the batch's watermark and
row set are captured, the batch is immutable and content-addressed, so paging and
compression run off it with the RR transaction already committed.

### §16-CT.2 Batch identity, watermark & ordering [invariant]

A batch is identified by **`(epoch, tenant feed generation, BatchId)`** and
carries a **watermark** = the `ChangeSequence` high-water of the snapshot it was
frozen from. Rules:

- **`ChangeSequence` is the only ordering authority** for the feed: a per-tenant,
  per-epoch **serialized transactional counter** assigned in **commit order**
  (§PG.6 item 1), distinct from `Version` (opaque concurrency) and `UpdatedAt`
  (display only). A client's "where am I" is a `(watermark, BatchId, PageOrdinal)`
  checkpoint, **never** a wall-clock time and never a client-chosen offset.
- **`epoch` fences a feed reset.** If server retention/rebuild invalidates a
  tenant's continuity (§PG.6 item 5), the `epoch` advances; a client presenting a
  stale epoch is told to **full-resync** (§17-CT), never served an incongruent
  delta.
- A batch is **immutable once frozen**: pages are stored and re-served byte-stable;
  the server never re-materializes a different row set for the same `BatchId`.

### §16-CT.3 Paging & resumable download [invariant]

- Continuation is a **protected `(BatchId, PageOrdinal)`** issued by the server,
  not a client-supplied LIMIT/OFFSET — the client cannot skip, reorder, or peek
  past its tenant scope.
- Download is **memory-bounded** on both ends (§PG.4): the server streams a page at
  a time from the frozen batch; the client writes each page into an **invisible
  staging generation** (`repl_*` tables in a **separate replica DbContext**),
  resumable from the last durably-staged `PageOrdinal` after a crash or reconnect.
- Page size is bounded and derived from the manifest/profile, never an unbounded
  `ToListAsync()` (§16.1).

### §16-CT.4 Atomic apply — base + checkpoint move together [invariant]

**[invariant] A batch applies all-or-nothing (gate B, A-RULE 022).** When a batch
is fully staged, the client applies it in **one** local transaction that advances
**both** the business base rows **and** the sync checkpoint:

- A partially-downloaded or partially-applied batch **can never advance the
  checkpoint** — on failure the staging generation is discarded and the checkpoint
  is unchanged, so the next attempt re-pulls the same `BatchId` deterministically.
- If application spans the business projection context and the checkpoint store,
  they **share one SQLite connection + transaction** (`DbContext.Database
  .UseTransaction`), never `TransactionScope` and never two independent commits.
- Apply **replaces base rows only**; it **never touches the pending overlay**
  (§8.2) — unsent local intent is structurally untouchable by an inbound pull.

---

## §17-CT Full resync — the shadow-generation switch

> Catch-up (§16-CT) is the steady state. A **full resync** is the recovery path
> when a client is past retention (§PG.6 item 5), fails an integrity check, or
> crosses an `epoch` fence. It is the design that makes "rebuild my whole local
> replica" safe on an offline-first client that may hold unsent money.

### §17-CT.1 Build-validate-switch, never clear-then-fill [invariant]

**[invariant] A full resync never clears the live replica before its replacement
is proven complete (A-RULE 017, §8.2).** The sequence is:

1. **Build** an **encrypted shadow generation** (a parallel set of `repl_*` /
   base tables in the replica DbContext) from fresh frozen batches, while the live
   base keeps serving the UI read-through.
2. **Preserve the pending overlay:** the shadow is built as *base only*; the
   pending overlay (unsettled local commands) is carried across unchanged — a
   resync is a base rebuild, **not** a wipe of local business intent.
3. **Validate** the shadow (expected manifest entity set present, counts/hashes,
   FK integrity, ledger/stock/file/receipt parity per gate R / §PG.5).
4. **Atomically switch** base + checkpoint to the shadow generation in one
   transaction; the old generation is retired only *after* the switch commits.

At no point is active state cleared before the replacement succeeds; a failure at
any step leaves the live replica and its overlay intact, and the resync simply
retries.

### §17-CT.2 Encryption & crash safety across the switch [invariant]

The shadow generation lives in the **same SQLCipher-encrypted store** (`STACK.md`
client-DB section) under WAL; the switch is a committed transaction, so a crash
mid-resync leaves either the old or the new generation fully intact — never a torn
half. Corruption discovered at validation routes to the corrupt-DB
**quarantine-and-rebuild** path (§12.4), never a silent delete.

---

## §18-CT Contract & manifest evolution on the state feed

> The wire-version rule is §8.6/§18; this is the **state-feed-specific** elaboration
> that D08 and the client phases must obey.

- **The replicated surface is a manifest, not a hand-count.** `SyncContractCatalog
  .ProjectionOrder` is the single source of the replicated entity set and its apply
  order; every catch-up/policy/test count derives from it (§8.2). No phase may
  introduce a "magic total" of entities (A-RULE 014); `SyncSurfaceTests` asserts
  the replicated vs. not-replicated sets exactly so a later phase cannot quietly
  add a financial table to the feed.
- **Three classes on the feed, enforced:** (a) replicated read-only reference &
  clinical data; (b) the **3** client-writable replicable shapes; (c) **read-only,
  server-computed finance read-model projections** (`DailyFinanceFact`,
  `OwnerFinancialSnapshot`) added so every device converges on the server's money
  numbers without replicating the command-settled financial *aggregates* (§11.4,
  §8.1). A client-local-only column (e.g. `MedicalHistoryEntry
  .IsConflictPreservedCopy`) is **excluded from every `<Entity>SyncContract`** and
  is never pushed or pulled (asserted by reflection in `SyncSurfaceTests`).
- **Each contract carries `ContractVersion`;** the feed tolerates **N/N-1/N-2**
  with a controlled resync for anything older (§8.6). A breaking change to a sync
  contract is a new `V<N+1>` DTO + a `WireContract.Version` bump — expected only at
  D08/E03 — never an in-place edit of a shipped contract (§18).

---

## §20 Future seams & their explicit triggers (what is designed-for, not built)

> Operability force 5 includes *growing without a rewrite*. Each item below is a
> **seam that exists now** (a port, an abstraction, a config switch) and a
> **named trigger** that promotes it from future to built. Every promotion is an
> **ADR** (A-RULE 031); none is launch scope.

| Seam (exists now) | Trigger to build it | Where the seam lives |
|---|---|---|
| **Web clinic client** | a browser-delivered clinic UI is commercially required | online-only, talks only through versioned wire contracts + Business/critical-sync API; a *new project*, not a server change (§7.2) |
| **Multi-instance web + Redis SignalR backplane** | a second web instance is deployed | a package + config *deploy switch*, not an architecture change (§7.3, §16.2) |
| **Deployment Stamps (tenant → stamp → DB)** | one tenant's load or data-residency justifies a dedicated DB | the D02 data-access abstraction + `TenantId`-led indexing already leave room (§9.1) |
| **Operator cross-tenant analytics** | the admin wants fleet-wide charts | the `ITenantInsightQuery` read-model seam, isolated from OLTP, consent/ADR gated (§9.4, §13.5, §16.3) |
| **Datasync `/tables/*` adapter for SYNC-SIMPLE entities** | a qualified entity passes gate G1 | the `IStateSyncEngine` port swaps adapter without a domain rewrite (§6, §8.2) |
| **Message broker / separate OLAP store / read replica** | measured OLTP contention the bounded-worker model cannot absorb | the Outbox + projected fact tables already decouple reads from the OLTP path (§11.4, §12.1) |
| **Passkeys** | post-launch auth hardening | ASP.NET Core Identity supports it without a model change (§10.2) |

**[invariant] A future seam is never a reason to over-build now.** Each row ships
as the minimal seam (interface/switch), and the trigger — not a guess about scale —
authorizes the work, recorded as an ADR with the measurement that justified it.

---

## §21 A-RULE registry (authoritative one-line statements)

> The numbered cross-cutting invariants every phase file and `CONVENTIONS.md §6`
> cite by number. **This table is the authoritative one-liner;** the owning section
> states the full rationale. (Gaps — e.g. 007/008/015 — are intentional: retired or
> reserved numbers are not reused.)

| A-RULE | One-line invariant | Owning § |
|---|---|---|
| 001 | The sync engine sits behind a port; domain/repository code never binds a concrete sync/Datasync type. | §6 |
| 002 | Accounting is double-entry, append-only, always balanced; exactly one component constructs a `JournalEntry`. | §11.1 |
| 003 | Posted financial records are immutable; a correction is a compensating entry, never an in-place edit. | §11.1 |
| 004 | A version mismatch **detects** a conflict; **domain policy decides** the outcome — no blanket LWW on business facts. | §8.5 |
| 005 | Idempotency key is `(TenantId, OperationId)`; an identical retry returns the stored result. | §8.3 |
| 006 | At most one financial effect per command, enforced by writing the idempotency record in the effect's transaction. | §8.3 |
| 009 | Transport identifiers (`UpdatedAt`, `Version`) are never read as a business sequence. | §8.1, §PG.2 |
| 010 | The opaque UUID `Version` is concurrency evidence only — compared by equality, never ordered. | §8.1, §PG.2 |
| 011 | Client wall-clock time is never a sync or licensing authority; expiry uses a monotonic high-water. | §10.4, §13.2 |
| 012 | A client-supplied `TenantId` is never trusted; the server resolves it from membership, and a mismatch is audited. | §9.2 |
| 013 | `SaveChangesAsync` stamps `TenantId` from the context, never from the incoming entity. | §9.2 |
| 014 | The replicated surface is a manifest (`SyncContractCatalog.ProjectionOrder`); no phase uses a magic entity total. | §18-CT, §8.2 |
| 016 | No external/cloud log or telemetry sink is ever a runtime dependency; removing the local collector must not break the app. | §15.2, §17.4 |
| 017 | No destructive sync: an inbound pull never overwrites unsent local intent; full resync preserves the overlay. | §8.2, §17-CT |
| 018 | `IgnoreQueryFilters()` with no argument is banned; a maintenance path may lift only the `SoftDelete` filter, never `Tenant`. | §9.2 |
| 019 | Same `OperationId` + different `CommandFingerprint` is a security/integrity error — rejected and audited. | §8.3 |
| 020 | A filesystem path is never a credential; file access is Valet-Key gated and a reference stores a `FileId`. | §14 |
| 021 | Idempotency ≠ ordering; a command's dependency is explicit and resolved server-side only. | §8.4 |
| 022 | A batch/command stream never reorders; a partial page can never advance the sync checkpoint. | §8.3, §16-CT.4 |
| 023 | AuthN, AuthZ and Entitlement are independent and evaluated in that order — never merged or short-circuited. | §10.1 |
| 024 | A settled outcome is HTTP 200 with the result inside `SettlementResult`; "sync failed" alone is a defect. | §8.3, §8.7 |
| 025 | Outbox: a state change and its announcing message are written in one transaction. | §12.1 |
| 026 | The outbox relay claims rows with `FOR UPDATE SKIP LOCKED` + a fenced lease; consumers are idempotent. | §12.2 |
| 027 | A missing/unreachable Data-Protection key ring refuses readiness/auth — no in-memory substitute key ring. | §10.6 |
| 028 | TLS protocol is OS-negotiated (`SslProtocols.None`), never a hard-coded version. | §4, §10.5 |
| 029 | Offline authority is an **asymmetrically signed** (never HMAC), device-bound capability snapshot with per-capability expiry; the client verifies with a public key only; fail closed. | §10.4 |
| 030 | Subscription (commercial truth) and Licensing (runtime entitlement) are separate; the runtime depends only on Licensing. | §13.1 |
| 031 | Microservice extraction, a broker, and any scale-out switch require an ADR; they are non-goals at launch. | §2, §16.2, §20 |
| 032 | No console sink outside Development and no external sink ever; logs are local rolling files with redaction. | §15.1 |
| 033 | Every cache key includes tenant + user + resource + version; a missing component is a test failure. | §16.3 |
| 034 | Reporting/aggregation never rides the OLTP transaction path; it reads projected fact tables under a separate budget. | §11.4, §16.3 |
| 035 | Capacity numbers are measured with a published harness on the real profiles, never asserted. | §16.4, §PG.4 |
| 036 | Command intake carries a fenced lease; a crashed `InProgress` is recovered deterministically — the settled record (written in the effect's transaction) is the sole proof of a committed effect, never retried blindly, never stuck. | §8.3 |
| 037 | Wire compatibility ≠ business-semantic compatibility; a financial command carries a `RuleSetVersion` the server accepts / re-derives / rejects-with-upgrade-reason — a stale rule set is never force-applied under new semantics. | §8.6 |
| 038 | Single-host is deliberate but carries stated RPO/RTO + off-host backup durability + a named scale-out trigger; the restore drill *measures* RTO, never asserts availability. | §17.3 |
| 039 | A REPEATABLE READ feed snapshot is held only for capture, bounded by `MaxSnapshotLifetime` and monitored; materialization runs outside it so a long snapshot never defers VACUUM / bloats the weak host. | §PG.4, §16-CT.1 |
| 040 | `IdempotencyRecords` retention ≥ the longest supported offline window; past it, compact to an archival receipt (key + fingerprint + effect), never a purge that re-admits a duplicate. | §8.3 |

---

## §22 Glossary (terms with a precise meaning here)

- **Base / Pending overlay** — the two logical layers of the client replica: *base*
  = last settled server state (replaced by pulls); *overlay* = unsettled local
  commands (never touched by a pull). The UI reads base-with-overlay (§8.2).
- **ChangeSequence** — per-tenant, per-epoch serialized commit-ordered counter; the
  only ordering authority of the state feed (§16-CT.2). Not `Version`, not
  `UpdatedAt`.
- **CommandEnvelope / SettlementResult** — the durable request/response of the
  command pipeline; the result answers what/why/financial-effect/who (§8.3, §8.7).
- **Epoch** — a feed-reset fence; a stale epoch forces a full resync (§16-CT.2).
- **Entitlement** — the tenant-licence capability check, the third security plane,
  distinct from authorization (§10.1).
- **Frozen batch** — an immutable multi-entity delta captured from one REPEATABLE
  READ snapshot (§16-CT.1).
- **IStateSyncEngine / ISyncEngine** — the Application-owned state-replication port
  and the outer client facade; the seam that survives a sync-library swap (§6).
- **Licence vs. Subscription** — offline-verifiable runtime entitlement artifact vs.
  the server-side commercial bounded context (§13.1).
- **Provisional truth** — a locally-settled result that is durable but not final
  until the server's `SettlementResult` confirms it (§5, §8.1).
- **Valet-Key** — a short-lived, scoped grant authorizing one file transfer; a path
  is never a credential (§14).
- **Version (UUID)** — opaque DB-generated concurrency token; equality-compared,
  never ordered (§PG.2).

---

## §23 Cross-reference index (phase citation → section)

> Fast lookup for the `§` a phase file names. Section numbers are a **contract** —
> keep them stable; add new sections rather than renumbering.

| Topic a phase cites | Section |
|---|---|
| Two pipelines; replication ≠ financial order | §8.1 |
| State replication surface, DTO shape, non-destructive pull | §8.2 |
| Command envelope, idempotency, settlement state machine, HTTP-200 policy | §8.3 |
| Explicit dependency / ordering, `ExpectedServerVersion` | §8.4 |
| Conflict policy by data class | §8.5 |
| Contract versioning, mixed fleet | §8.6, §18, §18-CT |
| Sync-failure UX contract | §8.7 |
| Tenancy isolation (3 layers, hardening-off) | §9.1–§9.3 |
| Cross-tenant (operator only) | §9.4 |
| Three security planes | §10.1 |
| AuthN / RBAC / offline capability snapshot | §10.2–§10.4 |
| Key-ring recovery invariant | §10.6 |
| Audit plane | §10.7 |
| Ledger / money representation | §11.1 |
| Inventory movement model | §11.2 |
| Reporting read models / server projection §11.4.1 | §11.4 |
| Outbox + atomic claim + resilience + crash | §12.1–§12.4 |
| Subscription / Licensing split; onboarding; admin web | §13.1–§13.5 |
| Files / Valet-Key lifecycle | §14 |
| Observability / correlation | §15.1–§15.2 |
| Performance strategy / reporting isolation / measured capacity | §16.1–§16.4 |
| Deployment, backup/restore, zero-Internet | §17.1–§17.4 |
| Validation gates | §19 |
| PostgreSQL token/RLS/pool budgets/journal integrity/wire mapping | §PG.1–§PG.7 |
| Immutable batch transport & atomic apply | §16-CT |
| Full resync shadow switch | §17-CT |
| A-RULE one-liners | §21 |

---

## §24 One-line summary

> Animora is an **offline-first modular monolith** on a single weak, zero-Internet
> host: a desktop keeps a durable encrypted replica and **provisional** truth; a
> central ASP.NET Core + PostgreSQL server is the **single global authority** that
> settles **idempotent, dependency-ordered business commands** and feeds clients
> **immutable, non-destructive state batches**; money is append-only and
> re-derived, tenancy is isolated in three layers that hold with hardening off,
> entitlement is a separate signed offline licence, and every "production-ready"
> claim is an **earned gate (§19)**, never an assertion.
