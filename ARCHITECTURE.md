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
  statement of each lives in the **A-RULE registry (§23)**; the section that owns
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
(Low 30 d, Normal 14 d, High 72 h, Critical = online-only). It is HMAC-signed with
a server/keystore key, bound to `(tenant, user, device)` and the user's security
stamp.

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

**The single backup set is three things, and all three are mandatory:**
1. the **PostgreSQL** database (logical or physical/PITR),
2. the **file content** tree (§14),
3. the **Data-Protection key ring** (§10.6) — *forgetting this is the classic
   silent disaster: the DB restores but every token is undecryptable*.

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
| **A — Idempotency & financial-effect-once** | identical retry returns stored result; same `OperationId`+different fingerprint is audited; never two financial effects (§8.3) |
| **D — Dependency & ordering** | explicit dependency resolved server-side; out-of-order/unsettled dependency is deferred, never mis-applied (§8.4) |
| **C — Conflict policy** | version mismatch detects, domain decides; LWW only for UI prefs; posted invoice immutable; movements converge additively (§8.5) |
| **N — Non-destructive sync** | a pull never overwrites unsent intent; full resync preserves the pending overlay and switches atomically (§8.2) |
| **B — Immutable batch atomicity** | base+checkpoint advance all-or-nothing; a partial page can't advance the checkpoint (§16-CT) |
| **L — Tenant isolation with hardening OFF** | all three isolation layers hold with RLS off; null tenant context throws; client `TenantId` ignored+audited (§9.2) |
| **P — Three planes** | AuthN/AuthZ/Entitlement independent and ordered; expired entitlement ≠ "forbidden" (§10.1) |
| **O — Offline capability** | signed device-bound snapshot fails closed at expiry; clock rollback can't revive; revoked device loses offline at next contact (§10.4) |
| **K — Key-ring recovery** | missing key ring refuses readiness/auth; restore with the key ring succeeds; without it, tokens fail honestly (§10.6, §17.3) |
| **U — Licensing anti-piracy** | offline verify, installation binding, tamper + clock-rollback resistance, fail-closed matrix (§13.2) |
| **F — File lifecycle** | Staged→…→Available with reconciliation; Valet-Key required; a path is never a credential (§14) |
| **S — Subscription single-engine** | website intent and operator action share one idempotent provisioning path (§13.4) |
| **I — Reconnect storm** | jittered per-device backoff; a fleet restarting together does not converge (§12.3) |
| **M — Mixed fleet** | N/N-1/N-2 deterministic; unsupported version → controlled resync (§8.6, §18) |
| **R — Rehearsed restore** | DB + files + key ring restore onto a clean host to readiness + a known tenant (§17.3) |
| **Z — Zero-Internet** | the whole runtime operates with egress blocked; no `cdn…`/external endpoint in built output (§17.4) |
| **T — Measured capacity** | published harness records real capacity on 1–3 core / 2–4 GiB; no asserted user count (§16.4) |
| **PG — Journal/snapshot integrity** | commit-safe tenant counter ordering, child/direct DML capture, retention, same-snapshot watermark/rows, bounded immutable extraction (§PG.6) |

Gate coverage is owned by E08 (+ the PG gates alongside the D08 journal work);
every gate maps to a requirement in §1.
