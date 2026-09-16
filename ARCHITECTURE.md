# Animora — Software Architecture Specification

> **Purpose of this file.** The single, authoritative **software
> architecture** definition for Animora — **structure, behavior, boundaries,
> patterns and flows**, not technology names/versions (`STACK.md`) and not
> schemas/feature/UI specifics (out of scope for both). Source of truth for
> AI agents and engineers: an agent must be able to implement any module
> correctly from this file alone, without re-deriving intent from the
> technology choice.
>
> **Split from `STACK.md` (read once, do not re-derive elsewhere).**
> `STACK.md` answers "what technology, which version, why chosen". This file
> answers "how the system is structured, how it behaves, which pattern
> governs which flow, why this design". Each file is self-contained; a
> reader should not need to jump between files to understand either one.
> Where a technology name is unavoidable to describe an integration point
> (e.g. "the sync engine sits behind `ISyncEngine`"), it is named only
> insofar as the architectural boundary requires it — exact versions,
> package lists and rejection rationale live only in `STACK.md`.
>
> **Product.** Animora — self-hosted accounting + clinic-management platform
> for veterinary clinics. **Targets:** Windows desktop, macOS desktop, Web
> browser, plus a future independent public marketing/subscription website
> (out of this system's boundary — see §17).
> **Hard architectural constraint.** The system must be **fully operational
> with zero external-Internet connectivity** at runtime, on a **deliberately
> weak, single, self-hosted server**, while supporting an **offline-first**
> desktop client with a **long, safe offline window**.
>
> **Methodology.** This document follows the **C4 model** (Context →
> Container → Component) for structural views, **arc42-style** driver/decision
> documentation, the **Azure Architecture Center Cloud Design Patterns**
> catalog for behavioral patterns (cited by name at each use), the **Azure
> Well-Architected Framework** five pillars for quality-attribute traceability,
> and **Domain-Driven Design (DDD)-lite** for bounded-context decomposition —
> all as documented on Microsoft Learn / Azure Architecture Center (see §23).
>
> **Tags.** **[PATTERN]** = a named, doc-backed architectural pattern applied
> as-is; **[DECISION]** = an Animora-specific architectural choice;
> **[INVARIANT]** = a rule that must never be violated by any implementation;
> **[DEFERRED]** = intentionally not built now, with an explicit trigger for
> when it must be revisited.

---

## 0. Architecture at a Glance

| Concern | Architectural answer |
|---|---|
| Architecture style | **Modular Monolith** — one deployable server process, strong internal module boundaries, no microservices at launch |
| Internal layering | **Clean/Onion Architecture** — Domain → Application → Infrastructure → Presentation, dependencies point inward only |
| Domain decomposition | **DDD-lite bounded contexts** — Identity, Tenant/Clinic, Patient/Owner, Clinical, Scheduling, Sales/POS, Inventory, Finance, Reporting, Files, Subscription, Licensing, Audit, Synchronization |
| Client architecture | **Split presentation, shared core** — one native offline-capable desktop client, one online-only web client, sharing Domain/Application/Contracts |
| The spine decision | **State replication ≠ business intent** — generic record sync and business-command settlement are two architecturally distinct pipelines that never merge |
| Sync architecture | Adapter-isolated sync engine (`ISyncEngine`) doing **delta-token replication**; a **separate durable command pipeline** doing **idempotent business-command settlement** |
| Conflict architecture | Infra-level version detection → generic resolvers (non-financial only) → **domain-specific conflict policy** → server-authoritative re-validation |
| Tenancy architecture | **Defense-in-depth isolation**: application authorization (mandatory) → data-access filtering (mandatory) → database-level hardening (optional, gated) |
| Security architecture | **Three independent planes**: Authentication, Authorization, Entitlement — never collapsed into one check; layered transport/PKI; offline-session as its own governed capability |
| Reliability architecture | **Transactional Outbox** + **Idempotent Consumer** + **Competing Consumers** (workers) + **Retry/Circuit-Breaker/Bulkhead** resilience patterns |
| Data architecture | **Append-only ledger** (finance) + **movement-based** stock (inventory) — both are architecturally immutable-fact models, never absolute-value overwrites |
| Deployment architecture | **Single self-hosted node** at launch; every scale-out step (backplane, read replica, partitioned tenant) is a **documented, gated escape hatch**, not built early |
| Observability architecture | **Fully local, layered observability** — logs, health, diagnostics bundle, audit, sync/command diagnostics, metrics — all functioning with zero external connectivity |

### 0.1 The single most important architectural decision

> **Generic state replication and business intent settlement are two
> different architectural pipelines, and this boundary must never be
> blurred.**

Everything else in this document is a consequence of this one boundary.
Patient records, catalogs, prices and reference data are **replicated state**
— they flow through the sync engine as flat, denormalized contracts with no
business meaning attached to the transport. Money, stock, scheduling
commitments and period-close are **business intent** — they flow through a
durable, idempotent **command pipeline** that ends in **server-authoritative
settlement**. An agent implementing a new feature must first ask *"is this
state replication, or is this business intent?"* — the answer selects the
entire pipeline, not just an endpoint.

### 0.2 The second most important decision

> **The server is the single source of global truth. Clients hold provisional,
> local truth. Convergence is business-policy-driven, not generic.**

Offline clients are allowed to act — this is a product requirement, not a
compromise — but nothing a client does offline is *globally* true until the
server settles it. "Eventual convergence" in Animora specifically means
*convergence to server-authoritative business state under explicit domain
conflict policy*, never a generic CRDT-style "any order arrives at the same
result" claim, because money/stock/scheduling carry invariants that a generic
merge function cannot decide.

---

## 1. Architecture Drivers (Quality Attribute Scenarios)

Every architectural decision below traces to one of these driving
requirements. This table is the **fitness-function contract**: any future
change must re-check the row(s) it touches.

| # | Requirement (business) | Quality attribute (WAF pillar) | Architectural tactic | Scenario / fitness check |
|---|---|---|---|---|
| R1 | Windows + macOS + Web from one core | Operational Excellence | Shared Domain/Application/Contracts kernel; split presentation | A change to a business rule requires editing exactly one shared module, not three UIs |
| R2 | High performance on a **weak, single server**, many clients | Performance Efficiency | Push compute to clients; delta-only sync; static-first web rendering; bounded background work | Benchmark: target hardware class handles the committed concurrent-client budget (§16) at defined p95/p99 |
| R3 | Secure, reliable, correct | Security + Reliability | Defense-in-depth tenancy; layered authN/authZ/entitlement; idempotent commands; transactional outbox | Security/chaos gates (§19) all pass |
| R4 | **Offline-first** desktop, robust reconnect | Reliability | Durable local command queue; delta-token resumable sync; explicit settlement states | Desktop keeps working and reconciles correctly through disconnect/crash/reconnect (§19 Scenario H/D) |
| R5 | Multi-user per clinic, correct convergence | Reliability + Security | Domain-specific conflict policy; server-authoritative settlement; append-only ledger | Two offline devices converge to one correct financial truth, never a silent overwrite |
| R6 | Multi-tenancy, subscription/plan management | Security + Cost Optimization | Shared-DB tenancy with layered isolation; independent Subscription/Licensing contexts | Cross-tenant access is architecturally impossible even if one layer is disabled |
| R7 | **Self-hosted, zero external-Internet dependency at runtime** | Reliability + Security | No mandatory external system in any runtime path; local PKI; local observability | Isolation test: disconnect the Internet cable, all committed capabilities still function |
| R8 | Current, standard, MS-aligned architecture | Operational Excellence | Named MS patterns applied deliberately, not improvised | Every non-trivial mechanism cites a specific, applicable pattern (§23) |

---

## 2. Guiding Architecture Principles

These principles resolve ambiguity when a new decision is needed and no
explicit rule below covers it yet.

1. **Minimize coordination.** *(Azure Architecture Center — minimize
   coordination principle.)* Clients act independently on local data; the
   server converges state asynchronously. No chatty per-keystroke round trips
   to a shared coordinator.
2. **The server is the single global authority.** All final settlement,
   conflict resolution and entitlement decisions are authoritative on the
   server, never on a client, never by majority vote among clients.
3. **Deltas and commands, not dumps.** Every cross-boundary exchange transfers
   either a changed-row delta or an explicit, named business command — never
   a bulk re-send "to be safe."
4. **Defense in depth, with one clearly-owned mandatory layer.** Every
   security- or tenancy-sensitive concern has exactly one layer that is
   *mandatory and sufficient on its own*; every other layer is *hardening*,
   and hardening must never be mistaken for the mandatory layer.
5. **Fail closed, never fail silent.** Every degradation path (offline
   expiry, sync failure, command rejection, overload) has a defined, visible
   outcome. "It just didn't sync" is never an acceptable end state for
   anything with a financial effect.
6. **Read models may lie a little; write models never do.** Denormalized,
   cached, or locally-projected views are allowed to be imprecise for UX
   speed. The authoritative write model is always normalized, versioned and
   validated.
7. **Boundaries are drawn where change happens independently.** A bounded
   context, a queue, or an abstraction exists only where two things can
   plausibly change on different schedules for different reasons — not by
   default, and not "for future-proofing" alone.
8. **Every named pattern must earn its place.** A Microsoft/Azure Architecture
   Center pattern is applied because a driver in §1 requires it, and is cited
   by name at the point of use (§23) — never adopted as decoration.

---

## 3. C4 Model — System Context (Level 1)

```
Actors ──▶ ANIMORA (self-hosted, single clinic-server node)
  • Clinic Staff — Desktop
  • Clinic Staff — Web
  • Clinic Owner / Manager — Desktop or Web, elevated authorization
  • Platform Operator — Animora vendor; plan/subscription oversight

ANIMORA ──(async, out-of-band; NOT a runtime dependency)──▶
  Future Public Marketing / Subscription Website (separate system — §17)
```

- **Clinic Staff (Desktop / Web)** — the primary users: reception, cashier,
  doctor, inventory, manager roles, distinguished by RBAC + policy (§10).
- **Clinic Owner/Manager** — a role, not a separate system; elevated
  authorization within the same tenant.
- **Platform Operator (Animora vendor)** — needs to *view and adjust*
  subscriptions/plans across tenants. Architecturally this is an
  **operator-facing surface of the Subscription bounded context**, not a
  separate backend (§13).
- **Future Public Website** — sells subscriptions and hosts the installer
  download. It is an **independent system** with its own architecture,
  connected to Animora only through a narrow, versioned, asynchronous
  **licensing/subscription contract boundary** (§13, §17) — never a shared
  database, never a synchronous runtime dependency (this is what keeps R7
  true even after the website exists).
- **No other external system participates in any runtime path.** This is
  drawn explicitly as an *absence* because R7 is a architectural constraint,
  not an incidental fact.

---

## 4. C4 Model — Container View (Level 2)

```
Desktop Client Container (native process, per device)
  Presentation (native UI) · Local Data Store (encrypted) ·
  Sync Engine Adapter · Business Command Outbox
Web Client Container (server-rendered, browser)
  Presentation (web UI) · no local persistent store

Both ──HTTPS/LAN──▶ Animora Server Process (single deployable)
  Desktop sends deltas + commands; Web sends requests.

  Server Process contains, in dependency order:
    Sync Surface (replication endpoints) + Business API Surface
      (commands/queries/reports) ──▶ Application / Domain Core
      (bounded contexts, §6 — DDD-lite)
    Application/Domain Core ──▶ Background Workers (Outbox relay, purge,
      entitlement checks) + Infrastructure (data access, file store,
      crypto, PKI)
    Background Workers + Infrastructure ──▶ Relational Database (tenant
      data, ledger, outbox, audit) + File Store (binary content, metadata
      in DB)
```

| Container | Responsibility | Talks to | Owns |
|---|---|---|---|
| Desktop Client | Offline-capable native presentation + local data + sync/command origination | Server (HTTPS/LAN) | Local encrypted replica, pending command queue, device identity |
| Web Client | Online-only thin presentation | Server (HTTPS/LAN) | Nothing persistent locally |
| Sync Surface | Generic entity replication (push/pull) | Desktop Client, Application Core | Delta cursors, replication contracts |
| Business API Surface | Command intake, queries, reporting | Desktop Client, Web Client, Application Core | Command intake contract, query/report contracts |
| Application/Domain Core | Business rules, invariants, workflows | Both surfaces, Infrastructure | Domain model, bounded-context boundaries |
| Background Workers | Outbox relay, retries, purges, entitlement checks | Application Core, Database | Worker state (claims/leases) |
| Infrastructure | Data access, file storage, crypto/PKI, external-free integrations | Database, File Store | Cross-cutting infra concerns |
| Database | Tenant data, financial journal, inventory movements, outbox, audit | — | System of record |
| File Store | Binary content addressed by hash | — | File bytes |

**[INVARIANT] The Sync Surface and the Business API Surface are architecturally
distinct entry points into the same Application/Domain Core** — they never
bypass the domain layer directly against the database (§8).

---

## 5. C4 Model — Component View: Layered Architecture Inside the Server

**[PATTERN] Clean / Onion Architecture** (Microsoft's ".NET application
architecture guides" — layered/Clean Architecture reference shape):

```
Presentation (Sync Surface · Business API Surface)
  — maps transport contracts to Application calls; no business logic here
      ▼ depends on
Application (use cases / command & query handlers)
  — orchestrates Domain objects, transactions, idempotency, authZ decisions
      ▼ depends on
Domain (entities, value objects, invariants, domain events —
  Finance/Inventory/Scheduling/...)
  — zero outward dependencies; the "clean core"
      ▲ implemented by (Infrastructure depends inward on Domain, not vice versa)
Infrastructure (data access, file storage, crypto, sync-engine adapter,
  outbox relay, PKI, clock)
  — implements interfaces defined by Domain/Application
```

**[INVARIANT] Dependencies point inward only.** Domain depends on nothing.
Application depends only on Domain. Infrastructure and Presentation depend
inward on Application/Domain, never the reverse. This is what makes the
**Sync Engine sit behind an abstraction** architecturally natural rather than
an arbitrary rule: a third-party replication library is an *Infrastructure*
concern implementing a Domain/Application-owned interface — never a thing
Domain code can see.

**[PATTERN] Ports & Adapters (Hexagonal) at the infrastructure boundary** —
every external-facing capability (sync engine, file storage, crypto/PKI,
clock/time source, license verification) is defined as a **port** (interface)
owned by Application/Domain and implemented by an **adapter** in
Infrastructure. This is *why* replacing or forking a third-party dependency
never requires a Domain rewrite (§8.2, §13).

---

## 6. Bounded Contexts & Context Map (DDD-lite)

**[PATTERN] Domain-Driven Design — bounded contexts inside a Modular
Monolith.** Contexts are **logical module boundaries in one deployable**, not
microservices. Each context owns its aggregates and exposes only explicit
contracts (commands, queries, domain events) to other contexts.

| Bounded context | Owns | Publishes (to other contexts) | Depends on |
|---|---|---|---|
| **Identity** | Users, credentials, roles, devices | `UserAuthenticated`, `DeviceRegistered` | — |
| **Tenant/Clinic** | Tenant record, clinic profile, staff membership | `TenantProvisioned`, `MembershipChanged` | Identity |
| **Patient/Owner** | Patients, owners/contacts | `PatientCreated`, `OwnerLinked` | Tenant/Clinic |
| **Clinical** | Medical history, vaccination, visit records | `VisitRecorded`, `VaccinationDue` | Patient/Owner |
| **Scheduling** | Appointments, boarding slots | `AppointmentBooked`, `AppointmentMoved` | Patient/Owner |
| **Sales/POS** | Cart, checkout, cashier sessions | `SaleCompleted`, `CashSessionClosed` | Patient/Owner, Inventory (query) |
| **Inventory** | Products, stock movements, quantity-on-hand (derived) | `StockReceived`, `StockAdjusted`, `LowStockDetected` | Tenant/Clinic |
| **Finance** | Journal, invoices, payments, cheques, expenses | `InvoicePosted`, `PaymentSettled`, `PeriodClosed` | Sales/POS, Inventory (events) |
| **Reporting** | Read-only aggregates/read models | *(no writes)* | Finance, Inventory, Clinical (read-only) |
| **Files** | File lifecycle, metadata, storage reconciliation | `FileAvailable`, `FileQuarantined` | Tenant/Clinic |
| **Subscription** | Plans, seats, billing periods, grace states | `PlanChanged`, `SeatLimitReached` | Tenant/Clinic |
| **Licensing** | Signed license verification, installation binding | `LicenseValidated`, `LicenseExpiring` | Subscription |
| **Audit** | Append-only audit log of security/finance actions | *(sink only — subscribes to all)* | all contexts (read-only subscriber) |
| **Synchronization** | Delta cursors, replication contracts, sync diagnostics | *(infrastructure-facing, not domain-facing)* | Application-layer ports of every syncable context |

**Context map — coupling rules:**

- **Finance never calls Sales/POS or Inventory synchronously to *decide* a
  business outcome.** It reacts to their published domain events
  (`SaleCompleted` → create draft invoice; `StockReceived` → no direct
  financial effect until a purchase invoice command settles). This keeps
  Finance's invariants (append-only, idempotent) from leaking into contexts
  that do not need them.
- **Reporting never writes.** It is a pure read-side consumer across
  contexts — see CQRS-lite (§11.3).
- **Audit is a pure sink.** Every other context publishes to it; it never
  publishes back. This guarantees the audit trail cannot become a hidden
  coordination channel.
- **Synchronization is not a business bounded context.** It is an
  **infrastructure capability** (§8) that every syncable context's
  Application layer opts into via a port — it never owns business rules.

---

## 7. Multi-Platform Client Architecture

### 7.1 Shared kernel vs. split presentation

**[DECISION]** Using two different presentation technologies for desktop and
web is a deliberate architectural boundary, not duplication:

```
Shared, single source of truth:
  Domain · Application · Contracts · Validation rules · Authorization
  policies · Localization/formatting · Sync ports (ISyncEngine) · DTOs ·
  Error codes · Command definitions · File-access contracts

Split by design (presentation only):
  Desktop presentation  (native, offline-capable host)
  Web presentation       (server-rendered, online-only host)
```

**Rationale (architectural, not tooling):** the desktop presentation host
must run as a genuine native offline process (no browser runtime in the
offline path — R4 is a hard requirement); the web presentation host is
optimized for near-zero per-client footprint on a weak server. Forcing both
into one presentation technology would subordinate one of R2 or R4 to
"maximize code reuse," which this architecture explicitly rejects (§2,
principle 7 — boundaries exist where things change for different reasons; a
native offline host and a stateless web renderer *do* change for different
reasons).

### 7.2 Client roles

| Client | Role | Local persistence | Offline capability | Talks to |
|---|---|---|---|---|
| **Desktop client** | Full offline-capable workstation | Encrypted local replica + durable pending-command queue | Full, governed (§8.4, §10.4) | Sync Surface + Business API |
| **Web client** | Thin, always-connected workstation | None persistent | None (explicit non-goal, §17) | Business API (+ read via Sync-backed queries where useful) |
| **Admin/Operator surface** | Platform operator's subscription oversight | None persistent | None | Subscription context only (§13) |

**[INVARIANT] A web client session never assumes a local write cache
survives a page reload.** Every web mutation is a synchronous Business API
call; there is no local queue to reconcile.

### 7.3 Interaction-pattern routing (web presentation)

**[DECISION]** Web presentation work is routed by **interaction frequency**,
not just "page type" — a page that merely displays a list is architecturally
different from one that needs a server round-trip per keystroke:

```
Simple read / occasional filter        → no-interactivity render path
Per-keystroke interactive search/filter → stateful interactive island
Large/typeahead data query             → dedicated query endpoint (debounced, paged)
Data-heavy or mutating operation       → Business API call, always
```

This is a *behavioral routing rule*; the exact rendering technology that
implements "no-interactivity" vs. "stateful interactive island" is a
`STACK.md` concern.

---

## 8. Offline-First & Synchronization Architecture (the heart)

This is the most safety-critical part of the architecture. It has two
architecturally separate pipelines (§0.1): **Generic Record Replication** and
**Business Command Settlement**. They share nothing except the transport
(HTTPS/LAN) and the underlying database.

### 8.1 Sync Engine as an isolated Infrastructure Adapter

**[PATTERN] Ports & Adapters / Anti-Corruption Layer.** The replication
engine is wrapped by a Domain/Application-owned port:

```csharp
public interface ISyncEngine
{
    Task<SyncResult> SynchronizeAsync(SyncScope scope, CancellationToken ct);
}
```

**[INVARIANT] Domain and Application code never reference the replication
library's types directly.** This exists so that (a) the replication
technology is swappable without touching business code, (b) the domain
layer's dependency graph stays clean per §5, and (c) a future custom
replacement engine is a pure Infrastructure change.

### 8.2 Generic Record Replication (state, not intent)

**What flows here:** flat, denormalized entities with no embedded business
workflow — patient demographics, owners/contacts, reference/catalog data,
products, prices, tax configuration, user-profile snapshots, safe appointment
metadata, and server-computed read models.

**Contract shape every syncable entity carries (architectural, not a
technology detail):**

```
Id          — globally unique identifier, stable across replicas
UpdatedAt   — server-stamped watermark; the ONLY basis for delta cursors
Version     — opaque concurrency token; changes on every server write
Deleted     — soft-delete flag (tombstone), never a hard delete in transit
```

**[INVARIANT] Server entity/write models are never exposed directly to
clients.** A dedicated, separate client-facing contract exists for every
synced type, so server-only fields (authorization metadata, internal
computed columns) can never leak, and the server remains free to reshape its
internal model without breaking the wire contract.

**Push/pull architecture:**

```
Client            Push: local write → operation queue (client-local,
                        transport-only) → drained to server, one
                        operation at a time by default (parallelism is an
                        explicit, provably-safe exception, never the default)
                  Pull: incremental, delta-token based; the token is the
                        maximum UpdatedAt seen so far for that entity/query
                        scope; pages are requested until exhausted
Server            Applies pushed operations idempotently at the replication
                  layer; serves pulled pages ordered/filtered by the
                  tenant's data view (§9)
```

**[INVARIANT] `UpdatedAt`/`Version`/the replication operation identifier are
never a global business/event sequence.** They are transport-layer
bookkeeping for *replication correctness* only. Business ordering is a
completely separate concept (§8.4).

**[INVARIANT] A server-issued, strictly-increasing sequence (never a wall
clock) must back the delta cursor wherever timestamp-based delta pull cannot
be proven safe** under concurrent writes, retries, paging, disconnects and
clock skew. A timestamp column is storage; it is not, by itself, proof of
sync-ordering correctness.

**[INVARIANT] No destructive sync.** An inbound pull must never blindly
overwrite unsent local business intent. Draft invoices, draft payments,
in-flight inventory transactions and pending appointment edits are
reconciled, never silently clobbered by a later-arriving replicated row.

### 8.3 Business Command Architecture (intent, not state)

**What flows here:** named business commands with real invariants — post an
invoice, register a payment, reverse a payment, receive/adjust stock, close a
cash session, book/move an appointment, close an accounting period, approve
a discount, issue a credit note. These are never modeled as generic record
CRUD.

**Desktop flow (the canonical shape):**

```
User action
   → local validation
   → local transaction (durable, provisional)
   → durable PendingCommand recorded
        ├─ connected  → submit to Business API
        │                 → server: authorization → concurrency →
        │                   business invariants → transactional commit
        │                 → Accept / Reject / Conflict
        │                 → settle locally
        └─ disconnected → remains queued; submitted with priority on
                           reconnect (ahead of routine background sync)
```

Every command carries:

```
OperationId · TenantId · UserId · DeviceId · CommandType · AggregateId ·
Payload · CreatedAtUtc (client-local, informational only) · LocalSequence ·
Status
```

**Why a command queue is architecturally separate from the replication
operation queue:** accounting needs deterministic idempotency, authorization,
aggregate-version validation, transactional posting and an explicit
settlement lifecycle — properties a generic replication queue does not
provide and should not be burdened with. Merging them would let a
replication reordering silently become a financial reordering.

**[PATTERN] Idempotent Consumer** (Azure Architecture Center). The server
maintains a durable idempotency record per command so an at-least-once
retry can never re-apply a financial effect:

```
Command → Attempt(s) → Settlement → Final result
```

Minimum idempotency-record shape:

```
OperationId · CommandType · FirstSeenAt · LastAttemptAt · Status ·
ResultHash/ResultReference · AttemptCount · LastErrorCode ·
CommandFingerprint
```

**[INVARIANT] Uniqueness key `(TenantId, OperationId)`.** A retry with the
same `OperationId` returns the previously-stored result and never applies a
second effect.

**[INVARIANT] Same `OperationId` with a different `CommandFingerprint`
(payload hash) is a security/integrity error, not a replay.** The server must
detect `same OperationId + different fingerprint` and reject/flag it —
"operation exists → return old result" is only valid for a byte-identical
retry.

**[INVARIANT] Idempotency does not solve ordering.** `(TenantId,
OperationId)` guarantees at-most-one effect for an *identical* command; it
says nothing about *when* it may apply relative to other commands. Ordering
is handled explicitly (§8.4).

**Explicit settlement states, surfaced to the user (never hidden):**

```
Draft → LocallyPosted → PendingSettlement → Settled
                                          → Rejected
                                          → Conflict
                                          → Reversed
                                          → NeedsAttention
```

**[INVARIANT — sync-failure UX contract]** Whenever a command cannot settle,
the architecture guarantees an answer to four questions, surfaced to the
user, not just logged: **what failed? why? does it have a financial effect?
who must act?** "Sync failed" alone is never an acceptable terminal state for
anything with a financial effect.

### 8.4 Two Independent Transports — Explicit Dependency Ordering

**[INVARIANT] The replication operation queue and the business command queue
are two independent transport mechanisms.** Neither is globally ordered with
the other, with itself beyond what it explicitly guarantees, or across
devices/aggregates.

A business command that depends on another command's outcome **must carry
explicit causal metadata**:

```
CausationId / DependsOnOperationId  (+ expected aggregate version, where known)
```

A command with an unsatisfied dependency is **deterministically delayed or
rejected** — never blindly accepted — until the prerequisite has **settled on
the server** (not merely been sent).

**Worked example — payment depending on an invoice:**

```
CreateInvoice(Op1)   ── settles → server now knows the invoice aggregate/version
RegisterPayment(Op2, DependsOnOperationId = Op1, ExpectedInvoiceVersion = v)

Op1 Settled            → evaluate Op2 (version check → Accept/Reject/Conflict)
Op1 Pending/Unknown    → hold Op2 (deterministic delay) or reject with a
                          retriable code
Op1 Rejected/Reversed  → reject Op2 (its precondition never became true)
```

This resolution happens **server-side only** — a client cannot "helpfully"
reorder, and a transport-level retry cannot smuggle a dependent command ahead
of its prerequisite's settlement.

### 8.5 Conflict Resolution Architecture

**[DECISION] Convergence claim, precisely stated:** *eventual convergence to
server-authoritative business state under explicit domain conflict policy* —
never a generic "strong convergence" claim. A version mismatch means "the row
changed under us"; it does not know that two payments summing to more than an
invoice's balance is a business conflict.

**Resolution pipeline (each layer strictly narrower in scope than the last):**

```
1. Infrastructure detection   — a stale write is flagged by version
                                  mismatch, never silently overwritten
2. Generic resolvers          — allowed ONLY for genuine last-write-wins
                                  metadata (UI prefs, display settings);
                                  forbidden for money/stock/scheduling/ledger
3. Domain-specific policy     — field-aware, aggregate-aware logic per data
                                  type (table below)
4. Server-authoritative       — on settlement the server re-runs
   re-validation                 authorization + concurrency + business
                                  invariants and returns Accept/Reject/
                                  Conflict independent of the version outcome
5. Idempotent commands        — guarantee a retry after any of the above
                                  never double-applies an effect
```

**Conflict-policy matrix (business ownership, not a technology choice):**

| Data type | Policy |
|---|---|
| UI preferences / display settings | Last-write-wins allowed |
| Non-critical free text | Field-aware merge |
| Patient demographic field | Explicit merge / human review |
| Medical history | Preserve full history; never silently overwrite |
| Appointment slot | Domain validation; reject on conflict |
| Invoice draft | Version conflict + user resolution |
| **Posted invoice** | **Immutable → reversal / credit note only** |
| **Payment** | **Idempotent command, never overwritten** |
| **Ledger** | **Append-only, forever** |
| **Inventory** | **Movement-based; never an absolute overwrite** |
| Cash session | Domain workflow (open/close invariants) |
| User roles / entitlements | Server authoritative, never client-negotiable |
| Audit records | Append-only, forever |

**[INVARIANT] No blind client-wins/server-wins for anything with a financial
effect.** A financial conflict is resolved by a compensating/adjustment entry
with reason + audit — never by rewriting a posted value.

### 8.6 Sync-Contract Versioning & Rollout Architecture

**[INVARIANT] Every replicated contract carries an explicit contract
version**, independent of ordinary schema migrations. When a synced
contract's shape changes in a wire-incompatible way, an older client must
**detect** "unsupported contract version" and trigger a **controlled
upgrade/resync** — never silently mis-map or partially sync.

**[DECISION] Staggered, mixed-version fleets are the normal operating case,
not an edge case.** The architecture must keep at least three consecutive
client generations (current and two prior) working correctly against one
current server, with deterministic migration and sync behavior across that
window.

### 8.7 Sequence Flows (representative)

**Normal online business write:**

```
Desktop/Web → Business API: Command(OperationId, Payload)
Business API → Application: validate authZ, concurrency, invariants
Application → Domain: apply, produce events
Domain → Infrastructure: persist (single transaction) + Outbox write
Business API → Client: Accept (Settled) | Reject | Conflict
```

**Offline write → reconnect:**

```
Desktop (offline): local validation → local commit → PendingCommand queued
Desktop (reconnect): priority-submit PendingCommand(s) ahead of routine sync
Server: idempotency check → authorization → concurrency → invariants
Server → Desktop: Settled | Rejected | Conflict → local settlement state updates
Desktop UI: reflects settlement state, never leaves it ambiguous
```

**Dependent commands, adverse interleaving (see §8.4 worked example and
§19 Scenario P):** every interleaving of push order, partial delivery, and
push failure must resolve to the *same* deterministic outcome — dependency
settlement, not arrival order, governs acceptance.

---

## 9. Multi-Tenancy Architecture

### 9.1 Tenancy model

**[DECISION] Shared database, shared schema, tenant-discriminator model** —
the standard, cost-efficient shape for many small B2B tenants (Azure
Architecture Center multitenancy guidance: tenancy models). Each clinic is
one tenant; many users per tenant.

**[PATTERN] Deployment Stamps pattern (Azure Architecture Center) as the
documented escape hatch.** A future `Tenant → DeploymentStamp → Database`
mapping lets one large/noisy tenant move to a dedicated database/stamp
without a domain rewrite. This is designed *for* in the data-access
abstraction, and **[DEFERRED]** until a real tenant requires it.

### 9.2 Isolation architecture — layered ownership

**[INVARIANT] Tenancy correctness is owned by the application layer, not the
database.** The mandatory layers below must be *independently sufficient*;
anything below them is hardening only.

```
MANDATORY (must hold even with every layer below disabled):
  1. Application authorization — every request resolves identity → TenantId
     claim before any operation runs; no operation executes without a
     resolved tenant.
  2. Data-access filtering — every tenant-scoped query is filtered by
     TenantId (and independently, soft-delete) as two separable filters —
     so a maintenance job can lift the soft-delete filter without ever
     lifting the tenant filter.
  3. Replication data-view filtering — every replicated read is filtered by
     TenantId; every replicated write is stamped with the caller's TenantId
     server-side, never trusted from the client payload.

HARDENING (defense-in-depth; may be disabled without breaking tenancy):
  4. Database-level row-security hardening — a secondary enforcement layer
     inside the database itself, gated by an explicit go/no-go validation
     (§19 Scenario J) before it is ever relied upon even as hardening.
```

**[INVARIANT] Client-supplied `TenantId` is never trusted.** It is resolved
only from authenticated membership, on every request, server-side.

**[INVARIANT — required-navigation pitfall]** A *required* relationship
navigation into a tenant-filtered entity must never be allowed to silently
drop parent rows because the filtered child disappeared. Sensitive
navigations are modeled as optional, or with matching filters on both sides,
and the data-access filter is treated as convenience/defense — never the
security boundary itself (that role belongs to layer 1).

### 9.3 Tenant context propagation

```
Client authenticates (Identity context)
   → server resolves TenantId from membership (Tenant/Clinic context)
   → TenantId flows as an ambient request context through Application layer
   → every downstream call (Domain, Infrastructure, replication data view,
     database-level hardening if enabled) receives the SAME resolved
     TenantId — never re-derived, never re-trusted from any lower layer
```

**[INVARIANT] Tenant isolation is validated with database-level hardening
completely disabled** (§19 Scenario J) — if isolation would fail without that
hardening, the mandatory layers were not actually sufficient, and hardening
was masking a defect rather than adding depth.

---

## 10. Security Architecture

### 10.1 Three independent planes

**[INVARIANT — never collapsed]**

```
Authentication  = WHO is the caller?             (Identity context)
Authorization   = WHAT may this caller do?        (RBAC + policy + resource)
Entitlement     = does THIS DEPLOYMENT license    (Subscription/Licensing
                   feature/plan X?                 context — §13)
```

A valid license never authenticates a user. A logged-in user is never
automatically entitled to a paid feature. An authorized action never implies
the deployment is licensed for it — each plane is checked independently, in
a fixed order: Authenticate → Authorize → Check entitlement.

### 10.2 Authentication architecture

```
Web client       → cookie-based session (browser-appropriate; the server
                    issues/validates, no auth logic in client script)
Desktop client    → bearer + refresh token pair (native-app-appropriate;
                    refresh requires server reachability — see §10.4 for
                    what covers the gap when it is NOT reachable)
```

**[INVARIANT] There is exactly one authentication mechanism family, used
consistently.** No bespoke token protocol is invented; no second parallel
auth path is added "just for desktop."

### 10.3 Authorization architecture

**[PATTERN] RBAC + Policy-based + Resource-based authorization, layered.**
Role gates the coarse capability (e.g. "cashier," "doctor," "manager"); a
policy handler evaluates fine-grained rules (e.g. amount thresholds); a
resource-based check evaluates the *specific* resource instance (e.g. "own
tenant's invoice, but not another cashier's till"). RBAC alone is never
sufficient for a financial operation.

**[INVARIANT] Every tenant-scoped, row-level authorization decision passes
through the same three-layer check as §9 layer 1 — authorization is
evaluated before any query is composed, never patched on after the fact.**

### 10.4 Offline-session architecture

**[INVARIANT] Bearer/refresh tokens are not, by themselves, an offline
authentication mechanism** — refreshing requires server reachability. Offline
continuity is governed by an explicit, separate, device-bound session policy:

```
A signed, device-bound "capability snapshot" is cached locally with an
EXPLICIT expiry, distinct from both the online token and the entitlement
license. It answers "what may this device act as, until when, without
contacting the server."
```

This policy must define, per capability (not one blanket number):

```
Maximum offline session lifetime      → per-capability, threat-model-derived;
                                          sensitive operations (refund,
                                          period-close) get short or zero
                                          offline windows; low-risk reads get
                                          longer ones
Cached-credential handling            → what is cached, protected how
Device revocation behavior            → a revoked device loses offline
                                          capability at next contact, no
                                          exceptions
Security-stamp/password-change        → invalidates cached offline sessions
  behavior
Behavior after expiry                 → FAILS CLOSED: read-only / locked /
                                          sensitive-ops-blocked — never a
                                          silent extension of authority
Reconnect recovery path               → deterministic re-authentication +
                                          capability refresh + pending-
                                          command reconciliation, in that
                                          order
```

### 10.5 Transport & local PKI architecture

```
Animora Local Certificate Authority
  (private key lives ONLY on the server/deployment authority; NEVER
   distributed to any client)
    → issues → Server certificate (LAN hostname, e.g. internal DNS/hostname)
    → trusted by → Animora-installed clients
        (trust anchor provisioned into the client OS/browser trust store by
         the deployment process — installer / MDM / policy — NEVER by
         application JavaScript or in-app code)
```

**[INVARIANT] A browser cannot be made to trust a private CA from inside a
web application.** Trust provisioning is a deployment-time architectural
concern, not a runtime one.

**[DECISION] Transport security is an OS-matrix-compatible protocol policy,
never a single hard-coded protocol version.** Obsolete protocols are
disabled; the newest mutually-supported protocol is preferred; an
interoperability floor is kept for the oldest officially supported client
tier. The OS is allowed to negotiate the exact version — the architecture
never hard-codes one, because doing so would silently exclude a supported
client tier.

### 10.6 Server key-material architecture

**[INVARIANT] Stateful server cryptographic key material (protecting cookie
authentication, anti-forgery tokens, and other protected payloads) is a
first-class, persisted, backed-up artifact — never left to default,
per-instance, ephemeral storage.** Losing it silently invalidates every
active session and every protected payload. It is part of the disaster
Recovery Matrix (§17.3), not an implementation footnote.

### 10.7 Tenant-safe caching architecture

**[INVARIANT] Every cache key for tenant/clinical/financial output must
include, at minimum: tenant scope, user/permission scope, resource identity,
and a query/contract version** — and for permission-sensitive data, a
permission fingerprint. **[PATTERN] Cache-Aside**, applied only with this key
composition; a cache mix-up (one tenant receiving another tenant's cached
result) is treated as a security defect, not a performance bug, and is a
dedicated gate (§19 Scenario L).

### 10.8 File access architecture

**[PATTERN] Valet Key (Azure Architecture Center).** A client never receives
or uses a raw filesystem path/URL as an access credential. Every file
read/write is:

```
FileId  →  application authorization check  →  a signed, short-lived
access grant issued by the server  →  the grant (not the path) is what the
client actually holds
```

**[INVARIANT] Path obscurity is never authorization**, even on a trusted LAN.
This holds identically for desktop and web clients (§14 covers the full file
lifecycle this access model sits on top of).

### 10.9 Threat-model summary (STRIDE-lite, architectural mitigations only)

| Threat category | Primary architectural mitigation |
|---|---|
| Spoofing (identity/tenant) | Authentication plane (§10.2) + server-resolved TenantId only (§9.2) |
| Tampering (payload/command replay) | Command fingerprint + idempotency subsystem (§8.3) |
| Repudiation | Append-only audit context (§6), immutable ledger (§11.1) |
| Information disclosure (cross-tenant) | Layered isolation (§9.2) + tenant-safe caching (§10.7) + Valet Key file access (§10.8) |
| Denial of service (weak server) | Bulkhead/throttling on background work (§12.3), delta-only sync (§8.2), static-first rendering (§7.3) |
| Elevation of privilege | RBAC + policy + resource-based authZ (§10.3), entitlement as an independent plane (§10.1) |

---

## 11. Domain & Data Architecture

### 11.1 Accounting architecture — append-only ledger

**[PATTERN] Append-only ledger (event-sourcing-lite for financial facts).**
Posted financial facts are immutable forever. Corrections are new,
compensating entries carrying a reason and an audit trail — never an
in-place mutation of a posted row. This is a direct consequence of §8.5's
conflict policy and §2 principle 5 (fail closed): a ledger that can be
silently edited cannot be trusted after any conflict, retry, or crash.

### 11.2 Inventory architecture — movement-based stock

**[PATTERN] Movement-based state (event-sourcing-lite for stock).**
Quantity-on-hand is never stored and overwritten as an absolute number by an
offline client. It is **derived** from an immutable sequence of movements
(Receipt / Sale / Consumption / Loss / Adjustment / Transfer / Return). This
makes offline stock edits safe by construction: two offline devices recording
different movements always converge to a correct total, whereas two offline
devices overwriting an absolute quantity would silently lose one edit.

### 11.3 Read models vs. write models (CQRS-lite)

**[PATTERN] CQRS-lite** — command/query separation **without** necessarily
separate storage at launch. Client-side read models (desktop grids, web
lists) may be denormalized, aggregated, or cached for UX/performance. The
**authoritative write model stays normalized and server-side** for Finance
and Inventory specifically. A desktop grid never needs to mirror the server's
aggregate structure, and it must never be treated as a source of truth for a
write decision.

**[DEFERRED]** A physically separate reporting/read-model database is an
explicit future escalation (§16.2), not a launch requirement — the
architecture keeps the seam (read-only reporting queries clearly separated
from the transactional path) so that escalation is additive, not a rewrite.

### 11.4 Concurrency architecture

**[PATTERN] Optimistic concurrency via an opaque version token** on every
mutable entity. A stale write is detected, never silently accepted; it is
routed into the conflict-resolution pipeline (§8.5) — the version token
*detects* staleness, it never *decides* the correct business outcome.

### 11.5 Reporting/aggregation isolation

**[INVARIANT] Heavy reporting/aggregation load is an architecturally
separate budget from OLTP/transactional load** — it must never ride the
transaction path. Every heavy report carries its own timeout, cancellation,
and concurrency budget, independent of the write path (§16.1).

---

## 12. Reliability & Eventing Architecture

### 12.1 Transactional Outbox

**[PATTERN] Transactional Outbox (Azure Architecture Center).** A domain
event and the business state change that produced it commit in **one
transaction** — atomic, no lost event, no dual-write. A background relay
then dispatches at-least-once.

**[INVARIANT] "Dispatched" never means "delivered exactly once."** Outbox
delivery is at-least-once by construction; every downstream consumer of an
outbox-relayed effect must be an **Idempotent Consumer** (§8.3's pattern,
reused here) keyed on a stable identifier.

**Mandatory relay state machine — the atomic claim is not optional:**

```
Pending → ClaimedUntil (atomic conditional claim) → Processing → Succeeded
                                                               → Retry (NextAttemptAt backoff)
```

A naive "select unprocessed → process → mark done" **will** double-dispatch
the moment a second relay worker exists; the atomic claim is what makes the
database the single arbiter between competing workers.

### 12.2 Background worker architecture

**[PATTERN] Competing Consumers (Azure Architecture Center).** Multiple
worker instances may safely compete for the same unit of work because the
claim protocol (§12.1) is atomic and every effect is idempotent. Every
worker step is designed to be **resumable after a crash mid-step** — the
architecture assumes a worker can die at any point and re-run without
duplicating an effect.

### 12.3 Resilience architecture

**[PATTERN] Retry + Circuit Breaker + Timeout + Bulkhead (transient-fault
handling, Azure Architecture Center reliability patterns), applied at every
network-facing boundary:**

```
Sync push/pull (client ↔ Sync Surface)      → retry with backoff; circuit
                                                breaker opens on sustained
                                                server unavailability, so a
                                                client degrades to "working
                                                offline" instead of hammering
                                                a struggling server
Business command submission (client ↔ API)  → same; a command NEVER retries
                                                by silently generating a new
                                                OperationId — retries reuse
                                                the original OperationId
                                                (idempotency, §8.3)
Outbox relay dispatch                        → retry with backoff via
                                                NextAttemptAt; circuit breaker
                                                on a chronically failing
                                                downstream effect, isolated
                                                per effect type
Background workers vs. request-handling      → Bulkhead: worker resource
  threads                                       usage is isolated so a
                                                background backlog can never
                                                starve interactive request
                                                handling on a weak server
```

**[PATTERN] Throttling**, applied to reconnect behavior specifically: sync
resumption after startup/reconnect uses **jitter + adaptive backoff +
progressive catch-up**, never a deterministic fixed interval — a
deterministic interval would create a synchronized "reconnect storm" the
moment many clients come back online together (§19 Scenario I).

### 12.4 Graceful degradation architecture

**[INVARIANT] Every overload or failure path has one defined outcome; there
is no undefined "it just gets slow/weird" state.** Concretely: exceeding a
resource budget results in queuing, shedding to a cheaper rendering path, or
disconnecting idle sessions — deterministically, and observably (§15) — never
silent resource exhaustion.

---

## 13. Subscription & Licensing Architecture

### 13.1 Context relationship

```
Future Public Website (separate system, §17)
        │  (async, versioned, non-runtime-critical contract:
        │   plan purchase → issues a signed license artifact)
        ▼
Subscription context  ── owns plans/seats/billing periods/grace states
        │  publishes PlanChanged, SeatLimitReached
        ▼
Licensing context      ── verifies the signed license OFFLINE, binds it to
                            THIS installation, enforces expiry/rotation
        │  publishes LicenseValidated, LicenseExpiring
        ▼
Core application       ── reads entitlement as READ-ONLY state; never
                            self-grants; UI toggles features from this
                            state only
```

**[INVARIANT] Subscription (the commercial billing model) and Licensing (the
offline, signed, deployment entitlement) are never the same mechanism.** The
runtime depends only on Licensing; Subscription may be entirely
manual/offline-managed without changing how the running deployment checks its
own entitlement.

### 13.2 Offline license verification flow

```
On startup / periodically:
  1. Load signed license artifact
  2. Verify signature against a locally shipped, rotation-aware key set
     (KeyId selects the verifying key)
  3. Verify the license is bound to THIS installation's sealed identity
     (an identity generated and sealed on-device at install time — a
     byte-for-byte copied license fails this check on a second machine)
  4. Verify anti-replay fields (LicenseId, IssueTime, audience/product)
  5. Verify against a MONOTONIC high-water clock, not the wall clock alone
     (defeats a wound-back system clock)
  6. Result: Valid → entitlement snapshot updates
             Invalid/Expired → deployment fails closed for gated
             capabilities per policy, never silently continues fully entitled
```

**[INVARIANT — anti-cloning]** A signature proves authenticity/integrity; it
never proves non-cloning by itself. The **installation-bound identity check**
(step 3) is the architectural control that actually prevents a license file
being copied verbatim to a second deployment.

**[INVARIANT — no online revocation channel]** Because there is no mandatory
online license server (R7), revocation is **architecturally approximated**
by short validity windows + reissuance — this limitation is a stated design
property, never hidden behind a false "instant revoke" claim.

### 13.3 Platform-operator oversight architecture

The **Platform Operator** (§3) views/manages subscriptions across tenants
through an **operator-facing surface of the Subscription context** —
architecturally just another authenticated, authorized client of that one
bounded context, with an operator-scoped role instead of a tenant-scoped
one. It is not a separate backend, and it never reaches into other bounded
contexts directly (it sees Tenant/Clinic identity only insofar as
Subscription needs to reference which tenant a plan belongs to).

---

## 14. Files & Storage Architecture

### 14.1 Two separate consistency domains

**[INVARIANT] A file's SQL metadata row and its binary bytes are two
separate consistency domains** — they are not committed by a single
transaction, so an explicit, ordered lifecycle is mandatory. Nothing may be
treated as usable until the lifecycle completes:

```
Staged → Uploaded → HashVerified → Committed → Available
```

`HashVerified` gates `Committed`; a hash mismatch fails the upload and can
never reach `Available`.

### 14.2 Reconciliation architecture

**[INVARIANT] Reconciliation is a mandatory background job, not a hope.**
Because the two consistency domains can diverge on crash/retry/restore, the
architecture explicitly detects and deterministically resolves every one of:

```
DB row without a file          duplicate upload
File without a DB row          interrupted finalization
Incomplete upload              failed restore (DB/file out of step)
```

Each divergence has exactly one deterministic outcome: quarantine, re-verify,
garbage-collect the orphan, or resume — never silent drift (§19 Scenario T).

### 14.3 Transfer protocol architecture

**[INVARIANT] File bytes never travel through the generic record-replication
pipeline (§8.2).** A **separate, resumable transfer protocol**
(begin-upload / upload-chunk / complete-upload / verify-hash) drives the
`Staged → Available` lifecycle, with resume, retry, hash verification,
cancellation and idempotent completion as first-class capabilities — not
retrofits.

### 14.4 Access architecture

File access always uses the Valet Key pattern from §10.8 — this section
defines the storage/lifecycle *behind* that access model; §10.8 defines how a
client is ever allowed to reach a file at all.

---

## 15. Observability Architecture

**[DECISION] Zero *external* telemetry is a product constraint; zero
*observability* is not acceptable.** Every observability capability below
must function with the Internet cable physically disconnected.

```
Structured logs                 — correlated by TenantId · OperationId ·
                                   DeviceId · RequestId, so any one
                                   transaction can be traced end-to-end
                                   across client → server → worker
Health / readiness               — liveness and readiness are answered
                                   locally, pollable by the installer/ops
                                   tooling without any external service
Diagnostics bundle                — one operation produces a complete local
                                   export: logs + config snapshot + versions
                                   + recent sync/command state, for support
                                   without cloud access
Audit trail                       — append-only, covering every security-
                                   and finance-relevant action (fed by the
                                   Audit bounded context, §6)
Sync diagnostics                  — per-entity cursor/backlog/resync state,
                                   answering "is my data actually synced?"
Command-settlement diagnostics    — per-command lifecycle/settlement state,
                                   answering "did this payment actually
                                   settle?"
Local metrics                     — scrapable on the LAN only; removing the
                                   local collector must never break the app
```

**[INVARIANT] Correlation identifiers propagate across every boundary drawn
in §4/§8** (client → Sync Surface / Business API → Application Core →
Background Workers) — an agent adding a new cross-boundary call must thread
the same correlation context through it.

---

## 16. Performance & Scalability Architecture

### 16.1 Weak-server design principle

**[DECISION] Push work to the client wherever the client can safely do it
without weakening §0.2 (server as global authority).** Offline-capable
clients perform local reads/search/form validation/UI computation/cached
dashboards; the server is reserved for authoritative writes, cross-user
validation, global reports, shared accounting, authorization and final
settlement. This is the single largest reducer of server load and is why
§8's split (state replication vs. business intent) exists in the first
place — replication is cheap and delta-based; business settlement is the
only work the server must do unconditionally.

### 16.2 Scale-out architecture path (deferred, gated)

**[DEFERRED] Single self-hosted node, single process, no backplane, no
message broker, no distributed cache is the launch architecture.** Each
scale-out step below is a **documented deployment-time escalation**, gated by
a measured need — never introduced speculatively (§2 principle 7):

```
Web app → 2+ instances     requires a session-affinity + real-time-channel
                            backplane, added as a deployment switch, not a
                            code change
Noisy/large tenant          requires the Deployment Stamps escape hatch
                            (§9.1) — move that tenant to its own
                            database/stamp
Heavy reporting outgrows    requires a physically separate read-model/
  the shared OLTP path         reporting store (§11.3's deferred escalation)
Real broker needed           requires replacing the Outbox+relay boundary
  beyond outbox+relay           (§12.1) with an external broker behind the
                                 SAME boundary — business code does not change
```

**[INVARIANT] Any of the above requires an explicit architecture decision
record before implementation** — none is introduced "just in case."

### 16.3 Capacity model methodology

**[INVARIANT] A supported concurrent-client count is never asserted from
hardware specifications alone — it is earned from measurement.** The
benchmark methodology exercises the full path (application memory, database
CPU/memory, session/circuit-equivalent state if any, background-worker cost,
filesystem/OS overhead, sync throughput, command-settlement latency,
reconnect behavior) against a defined target hardware class, at a defined
synthetic workload, before any capacity number is published.

---

## 17. Deployment & Infrastructure Architecture

### 17.1 Deployment topology

```
Clinic LAN (no mandatory external connectivity):
  Desktop clients ──┐
  Web-browsing       ├──▶ Single server node (application + database +
  workstations ──────┘      file store + local CA)
```

At launch, application process, database and file store may co-reside on one
physical/virtual server — this is a deployment-time packaging decision, not
an architectural coupling: the container boundaries in §4 remain distinct
enough that any of them can move to a separate machine later without a
redesign.

### 17.2 Environment strategy

```
Development   — full-feature, non-production, disposable data
Staging       — mirrors the production topology exactly (same single-node
                 shape), used to rehearse upgrade/rollback and isolation
                 tests before they touch a real clinic
Production    — one clinic's self-hosted node; no shared multi-tenant
                 infrastructure crosses clinic-server boundaries at launch
```

### 17.3 Recovery architecture (server vs. client are different problems)

**[INVARIANT] Server backup and client recovery are architecturally
different problems and are never conflated.** The server backs up its full
state (database, file store, configuration, cryptographic key material,
license material). A client holds *different* state (its encrypted local
replica, pending commands, device identity) that server backup does **not**
cover.

| Incident | Recovery source |
|---|---|
| Server database corruption | Database backup/restore |
| Server file-store corruption | File-store backup/restore |
| Server total loss | Full server backup set (DB + files + crypto keys + license material) — a restore that omits the key material is an incomplete restore (§10.6) |
| Client disk loss | Server-driven resync + a defined pending-command-recovery policy |
| Client device stolen | Device revocation (§10.4) — offline capability ends at next contact attempt |
| Corrupt client local replica | Quarantine the local replica; rebuild fresh from server |
| Client offline past its retention window | Full resync, not incremental |

### 17.4 Zero-Internet isolation as a tested architectural property

**[INVARIANT] "Zero external-Internet dependency" is a *tested* property, not
a claim.** The architecture's isolation boundary is validated by deliberately
removing, one at a time: DNS, general Internet reachability, any CDN-style
dependency, time synchronization, certificate-revocation-checking services,
and package/update feeds — and confirming that Login, Sync, Licensing and
core data access still function per policy under every one of these removed
independently. A certificate-validation path that silently reaches an
external revocation service is treated as a hidden Internet dependency and a
defect.

---

## 18. Cross-Platform Consistency & Versioning Architecture

**[INVARIANT] The desktop installer's upgrade/rollback behavior and the
sync-contract versioning of §8.6 are two related but separate concerns.** An
installer upgrade/rollback test alone never substitutes for a mixed-fleet
compatibility test — a fleet is staggered by nature, and both must be
validated together (§19 Scenario M).

**[DECISION] Desktop packaging format is a delivery preference, gated by
per-site constraints (peripherals, elevated installers, enterprise policy),
never a hard architectural commitment** — the architecture only requires that
whichever format is chosen supports signed, verifiable, rollback-capable
delivery on both target desktop OSes.

---

## 19. Architecture Validation Gates (fitness functions)

These gates prove the architecture, not just the technology (`STACK.md` §12
lists the paired technology-risk PoCs; these are the *behavioral* proofs the
architecture itself makes a promise about). A gate failure means the
*pattern or boundary* is wrong, not merely a bug.

| Gate | What it proves | Traces to |
|---|---|---|
| **A — Patient conflict** | Two offline devices editing the same non-financial record converge deterministically, with audit | §8.5 |
| **B — Independent financial commands** | Two offline devices, independent commands, both accepted, no duplication, correct ledger | §8.3, §11.1 |
| **C — Duplicate command** | Same `OperationId` sent twice → exactly one financial effect | §8.3 |
| **D — Lost response** | Server commits, response lost, client retries → server returns prior result, no second transaction | §8.3 |
| **E — Cross-tenant attack** | Tenant A requesting a Tenant B id fails at every mandatory isolation layer | §9.2 |
| **F — Delta integrity** | Paged incremental pull under concurrent writes/retries/disconnects/clock-skew → zero unrecoverable skip AND zero unrecoverable duplicate | §8.2 |
| **G — Server crash during settlement** | Business transaction remains atomic; outbox remains recoverable; retry is safe | §12.1 |
| **H — Desktop crash mid-commit** | Durable local state; no command loss; no double financial effect | §8.3 |
| **I — Reconnect storm** | Jitter + backoff + progressive catch-up hold under a mass simultaneous reconnect | §12.3 |
| **J — Tenant isolation with hardening OFF** | Isolation holds with database-level hardening completely disabled | §9.2 |
| **L — Tenant-safe cache** | Tenant A never receives Tenant B's cached result; a missing key component fails the test | §10.7 |
| **M — Mixed-version fleet** | Old client ↔ new server AND new client ↔ old supported server, across N/N-1/N-2, deterministic | §8.6, §18 |
| **O — Sync-failure UX contract** | A rejected/conflicted command surfaces what/why/financial-effect/who-fixes-it, never a bare failure | §8.3 |
| **P — Two-queue dependency ordering** | Every interleaving of dependent commands resolves deterministically by settlement, never by arrival order | §8.4 |
| **Q — Idempotency-fingerprint integrity** | Same fingerprint replay = prior result; different fingerprint, same OperationId = integrity error, audited | §8.3 |
| **R — Offline-session policy** | Max window enforced; revoked device refused on reconnect; credential change invalidates cached session; recovery path restores access | §10.4 |
| **S — Key-material survival** | Protected payloads survive restart AND full server-loss recovery; fail closed without the key ring | §10.6, §17.3 |
| **T — File lifecycle & reconciliation** | Every divergence between the two consistency domains resolves deterministically; access is always Valet-Key-gated | §14 |
| **U — Offline licensing & anti-cloning** | Signature + rotation valid; a copied license fails on a second installation; a wound-back clock cannot extend expiry | §13.2 |
| **V — Local observability, Internet pulled** | Every observability capability functions with zero external connectivity; removing a local collector never breaks the app | §15 |

**[INVARIANT] If any gate fails, the affected boundary/pattern is fixed
before feature work continues on top of it** — these gates protect the
architecture's own promises, not a specific library's behavior.

---

## 20. Architecture Decision Log (condensed)

| # | Decision | Status | Driver |
|---|---|---|---|
| AD-01 | Modular Monolith over microservices at launch | Accepted | R2, R8, §2 principle 7 |
| AD-02 | State replication and business intent are two permanently separate pipelines | Accepted (spine decision) | §0.1, R4, R5 |
| AD-03 | Server is the single global authority; clients hold provisional truth only | Accepted (spine decision) | §0.2, R5 |
| AD-04 | Split client presentation, shared core | Accepted | R1, R2, R4 |
| AD-05 | Shared-DB, tenant-discriminator multitenancy with Deployment-Stamps escape hatch | Accepted | R6 |
| AD-06 | Defense-in-depth tenant isolation, app-layer mandatory, DB-layer hardening only | Accepted | R3, R6 |
| AD-07 | Authentication/Authorization/Entitlement as three independent planes | Accepted | R6, R3 |
| AD-08 | Explicit, capability-scoped offline-session policy separate from online auth | Accepted | R4, R7 |
| AD-09 | Append-only ledger + movement-based inventory (never absolute-value offline writes) | Accepted | R5 |
| AD-10 | Idempotent Consumer + Transactional Outbox + Competing Consumers for all async effects | Accepted | R3, R4 |
| AD-11 | Valet Key pattern for all file access; no filesystem path ever reaches a client | Accepted | R3 |
| AD-12 | Fully local, layered observability; zero mandatory external telemetry | Accepted | R7 |
| AD-13 | Single-node deployment at launch; every scale-out step is an explicit, gated ADR | Accepted | R2, §2 principle 7 |
| AD-14 | Local PKI (Animora Local CA) provisioned by deployment tooling, never app code | Accepted | R7, R3 |
| AD-15 | Licensing (offline entitlement) is architecturally independent of Subscription (commercial model) | Accepted | R6, R7 |

---

## 21. Architecture Rules for AI Agents (mandatory, architecture-level)

```
A-RULE 001  Domain code never depends on the replication engine; always via ISyncEngine.
A-RULE 002  Posted accounting records are immutable; corrections are compensating entries.
A-RULE 003  Never client-wins/server-wins as the resolution for a financial fact.
A-RULE 004  Row-version conflict detection never substitutes for domain conflict policy.
A-RULE 005  Every business mutation carries a stable OperationId.
A-RULE 006  The same OperationId never produces a second financial effect.
A-RULE 007  A client may commit provisional local state while offline.
A-RULE 008  The server is the final global authority for settlement.
A-RULE 009  UpdatedAt is a delta-cursor basis only, never a business sequence.
A-RULE 010  An opaque version token is concurrency metadata only, never a business sequence.
A-RULE 011  Client wall-clock time is never a synchronization or licensing authority.
A-RULE 012  TenantId is resolved only from authenticated membership, never from client input.
A-RULE 013  Every tenant-scoped query is tenant-filtered at the application layer, unconditionally.
A-RULE 014  Server entity/write models are never exposed directly to clients.
A-RULE 015  A generic real-time channel (if any) is never the authoritative sync data channel.
A-RULE 016  No mandatory external-Internet service exists in any runtime path.
A-RULE 017  An inbound sync pull never destructively overwrites unsent local business intent.
A-RULE 018  Read models may be denormalized; financial write models stay normalized and server-side.
A-RULE 019  Same OperationId + different CommandFingerprint is a security/integrity error, not a replay.
A-RULE 020  A filesystem path is never an access credential — Valet Key only.
A-RULE 021  Idempotency and ordering are separate concerns; dependency is explicit (CausationId).
A-RULE 022  The replication queue and the business command queue are never assumed globally ordered.
A-RULE 023  Authentication, Authorization and Entitlement are checked independently, never merged.
A-RULE 024  Every rejected/conflicted command surfaces what/why/financial-effect/who-fixes-it.
A-RULE 025  Every async/at-least-once effect has an idempotent consumer at the receiving end.
A-RULE 026  A background worker's every step is resumable after a crash, without duplicating effects.
A-RULE 027  Server cryptographic key material is a persisted, backed-up, recovery-tested artifact.
A-RULE 028  Transport security policy is OS-matrix-compatible, never hard-coded to one protocol version.
A-RULE 029  An offline-session policy governs offline continuity; bearer/refresh tokens alone do not.
A-RULE 030  A license signature proves authenticity, not non-cloning; installation-binding is mandatory.
A-RULE 031  Introducing a backplane/broker/microservice/partitioned tenant requires an explicit ADR.
A-RULE 032  Zero external telemetry is a constraint, not an excuse for zero observability.
A-RULE 033  Every cache key for tenant/clinical/financial data includes tenant + user + resource + version scope.
A-RULE 034  Heavy reporting/aggregation load never rides the OLTP transaction path.
A-RULE 035  A capacity/concurrency claim is earned from measurement, never asserted from specs alone.
```

**Distinctions this architecture never collapses:**

```
state replication            ≠ business intent
authentication                ≠ authorization ≠ entitlement
concurrency detection          ≠ business conflict resolution
provisional offline state      ≠ global financial truth
delivery retry                 ≠ exactly-once side effect
replication operation identity ≠ business command identity (OperationId)
idempotency                    ≠ ordering
Subscription (commercial)      ≠ Licensing (offline entitlement)
```

---

## 22. Non-Goals (architecture-level)

- **Microservices at launch** — a Modular Monolith is the launch architecture;
  a future service extraction requires an explicit ADR (§16.2, A-RULE 031).
- **Web offline capability** — architecturally out of scope; the web client
  is intentionally online-only (§7.2). This is a boundary decision, not an
  oversight.
- **A generic distributed-transaction/saga layer** — not needed inside one
  process; cross-context coordination uses domain events + the Outbox, not a
  distributed-transaction coordinator.
- **A mandatory message broker or distributed cache at launch** — Outbox +
  in-process relay is the launch reliability architecture; a broker is an
  additive, gated future step (§16.2), never a launch dependency.
- **Any mandatory cloud identity, telemetry, storage or license-validation
  service** — would directly violate R7 and is therefore never introduced as
  a *required* runtime path, at any point in the roadmap.
- **Treating the sync engine's transport as a business ordering source** — no
  future feature may reintroduce this collapse (§8.2, A-RULE 009/010).
- **Per-tenant database at launch** — the shared-DB model is the launch
  architecture; the Deployment Stamps escape hatch (§9.1) is designed for,
  not built, until a real tenant needs it.

---

## 23. Pattern Catalog & Source Basis

All patterns and structural methods cited above are applied as documented by
Microsoft, on Microsoft Learn / Azure Architecture Center, and are re-checked
against the same authoritative source set `STACK.md` §14 uses for
technology-specific claims:

- **C4 model** (Context/Container/Component) — used for §3–§5 structural views.
- **arc42-style driver/decision documentation** — used for §1 (quality
  attribute scenarios) and §20 (decision log).
- **Domain-Driven Design — bounded contexts inside a modular monolith** — §6.
- **Clean / Onion Architecture, Ports & Adapters (Hexagonal)** — .NET
  application architecture guidance — §5, §8.1.
- **Azure Well-Architected Framework — five pillars** (Reliability, Security,
  Cost Optimization, Operational Excellence, Performance Efficiency) — used
  to trace every driver in §1.
- **Azure Architecture Center — Cloud Design Patterns**, specifically:
  - **Idempotent Consumer** — §8.3, §12.1.
  - **Transactional Outbox** — §12.1.
  - **Competing Consumers** — §12.2.
  - **Retry / Circuit Breaker / Bulkhead / Throttling** (transient-fault
    handling & reliability patterns) — §12.3.
  - **Cache-Aside** — §10.7.
  - **Valet Key** — §10.8, §14.4.
  - **Deployment Stamps** — §9.1.
  - **Minimize coordination** (design principle) — §2.
- **Azure Architecture Center — Multitenancy guidance** (tenancy models,
  isolation strategies) — §9.
- **ASP.NET Core / identity security guidance** — policy-based and
  resource-based authorization — §10.3.
- **Microsoft offline-first product shape (Dynamics 365 / Power Apps
  offline patterns)** — central-authority-plus-local-queue shape referenced
  as the industry-analogue validation for §8's overall design, not copied
  verbatim.

> When this file is revisited, re-validate every **[PATTERN]** citation
> against the then-current Microsoft Learn / Azure Architecture Center
> wording before extending it — never let a pattern name drift from its
> documented definition.
