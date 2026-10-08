# Animora — Software Architecture Specification

> **Status:** Architecture Baseline — **v3.0 (unified)**. This file supersedes
> and replaces the two prior architecture documents (`ARCHITECTURE.md` v1 and
> `Animora_ARCHITECTURE_v2.md`). It is the **single, authoritative software
> architecture** definition for Animora — **structure, behavior, boundaries,
> patterns and flows**. Everything the two prior files said is preserved here;
> where they disagreed, the newer, more-developed backend/synchronization model
> wins (see the *provenance note* below).
>
> **Audience.** Software engineers, architects, security reviewers, QA/chaos
> engineers, DevOps/operations, and AI coding agents. An agent must be able to
> implement any module correctly from this file alone, without re-deriving
> intent from a technology choice.
>
> **Split from `STACK.md` (read once, do not re-derive elsewhere).**
> `STACK.md` answers "what technology, which version, why chosen". This file
> answers "how the system is structured, how it behaves, which pattern governs
> which flow, why this design". Each file is self-contained; a reader should
> not need to jump between files to understand either one. Where a technology
> name is unavoidable to describe an integration point (e.g. "critical sync is
> backed by SQL Server Change Tracking behind `IStateSyncEngine`"), it is named
> only insofar as the architectural boundary requires it — exact versions,
> package lists and rejection rationale live only in `STACK.md`.
>
> **Product.** Animora — a **commercial, vendor-hosted (SaaS) accounting +
> clinic-management platform** for veterinary clinics. The vendor (Animora)
> runs **one central, multi-tenant server**; each clinic is a tenant that
> **subscribes** and connects to it — clinics never host, operate, or own a
> server of their own. **Targets:** Windows desktop, macOS desktop, Web
> browser, plus a public marketing/subscription website where a customer
> **registers and buys a subscription, then downloads the desktop app** (the
> website is an independent system on the vendor side — its boundary and the
> onboarding contract are in §13/§17).
>
> **Hard architectural constraint.** The system must be **fully operational
> with zero external-Internet connectivity** at runtime, on a **deliberately
> weak, single server node** (the vendor's self-operated server — "self-hosted"
> throughout this document means *the vendor runs the server on its own
> infrastructure, not on a cloud PaaS*; it never means per-clinic hosting),
> while supporting an **offline-first** desktop client with a **long, safe
> offline window**.
>
> **Provenance note — what changed when the two files merged.** The earlier
> `ARCHITECTURE.md` modeled synchronization as a single `ISyncEngine` doing
> delta-token replication, leaning on a third-party replication library as the
> default transport. The newer `Animora_ARCHITECTURE_v2.md` made a **core
> correction** that this unified file adopts as authoritative: **a third-party
> sync library (CommunityToolkit.Datasync) is NOT Animora's universal
> synchronization mechanism.** Critical state synchronization is owned by
> Animora through a **project-owned Custom Sync Engine backed by SQL Server
> Change Tracking**; the Datasync library survives only as an *adapter* for a
> deliberately narrow class of low-risk, simple replicated state. This keeps
> the technology choice without ever making a third-party library the
> correctness authority for financial, collaborative, or high-integrity
> workflows. All the v1 rigor (pattern provenance, threat model, management-
> plane separation, declared RTO/RPO) is retained; all the v2 depth (Mutation
> Journal, SettlementUnknown, Change-Tracking correctness protocol, Shadow-
> Replica resync, Echo safety, direct-SQL governance, RLS/`SESSION_CONTEXT`,
> notification hints, consistency classes) is folded in.
>
> **Methodology (with honest provenance — see §23).** Structural views use the
> **C4 model** (an independent method by Simon Brown, `c4model.com` — *not* a
> Microsoft artifact); driver/decision documentation follows **arc42**
> (`arc42.org`, independent); bounded-context decomposition uses
> **Domain-Driven Design (DDD)-lite** (Evans). *Validated against* Microsoft
> guidance — the **Azure Architecture Center Cloud Design Patterns** catalog
> for behavioral patterns (cited by name at each use) and the **Azure
> Well-Architected Framework (WAF)** five pillars as *quality-attribute
> traceability*. WAF is a quality framework, not an architecture style or a
> deployment prescription: architecture decisions are *checked against* WAF
> quality attributes, never *derived from* a pillar — this ordering is what
> keeps the design from over-engineering a pillar into a mandate.
>
> **Tags.** **[PATTERN]** = a named, doc-backed pattern **adapted** to
> Animora's constraints (never blindly "as-is"); **[DECISION]** = an
> Animora-specific architectural choice; **[INVARIANT]** = a rule that must
> never be violated by any implementation; **[DEFERRED]** = intentionally not
> built now, with an explicit trigger for when it must be revisited.

---

## 0. Architecture at a Glance

| Concern | Architectural answer |
|---|---|
| Deployment style | **Vendor-operated, central, multi-tenant SaaS** — the vendor self-hosts one server; clinics are tenants and never run a server |
| Launch topology | **Single runtime node / single server process**, database and file store co-located where practical |
| Architecture style | **Modular Monolith** — one deployable, strong internal module boundaries, no microservices at launch |
| Internal layering | **Clean/Onion + Ports & Adapters** — Domain → Application → Infrastructure/Presentation, dependencies point inward only |
| Domain decomposition | **DDD-lite bounded contexts** inside one deployable — Identity, Tenant/Clinic, Patient/Owner, Clinical, Scheduling, Sales/POS, Inventory, Finance, Reporting, Files, Subscription, Licensing, Audit, (+ infra: Synchronization, Notification) |
| Client architecture | **Split presentation, shared core** — one native offline-capable desktop client, one online-only web client, sharing Domain/Application/Contracts |
| Global authority | **The server is the single source of global truth**; clients hold **provisional local truth** until the server settles it |
| The spine decision | **State replication ≠ business intent** — generic record sync and business-command settlement are two architecturally distinct pipelines that never merge |
| Critical sync | **Animora Custom Sync Engine backed by SQL Server Change Tracking** (the correctness authority) |
| Simple sync | **CommunityToolkit.Datasync adapter**, only for entities whose correctness model is genuinely Datasync-compatible (SYNC-SIMPLE) |
| Business intent | Durable, idempotent **Business Command Pipeline** ending in **server-authoritative settlement** |
| Local durability | Encrypted local relational store (SQLite + EF Core) with a **Mutation Journal** written in the **same local transaction** as the provisional state change |
| Server change feed | **SQL Server Change Tracking** (replicated-state change position) |
| Business event feed | **Transactional Outbox** (domain events / effects) — never merged with the change feed |
| Row concurrency | Opaque version token (`rowversion`) — detection only, never a sync cursor |
| Conflict architecture | Base-version mismatch + server change *evidence* → generic resolvers (non-critical only) → **domain-specific policy** → server-authoritative re-validation; insufficient evidence ⇒ Conflict/NeedsAttention, never a guess |
| Finance | **Append-only ledger** + compensating/reversal commands — never an in-place mutation of a posted fact |
| Inventory | **Movement-based** stock + server settlement policy — never an absolute-value offline overwrite |
| Scheduling | **Transactional resource allocation**, not a row-version check alone |
| Notifications | **Hint only** — never an authoritative synchronization channel |
| Tenancy | **Defense-in-depth isolation**: application authorization + tenant-aware data access (mandatory) → SQL Server RLS + tenant-safe caching (hardening) |
| Security | **Three independent planes**: Authentication, Authorization, Entitlement — never collapsed; layered transport/PKI; offline-session as its own governed capability |
| Offline authorization | Signed, device-bound **capability snapshot** with explicit per-capability expiry |
| Reliability | **Transactional Outbox** + **Idempotent Consumer** + **Competing Consumers** + **Retry/Circuit-Breaker/Bulkhead/Throttling**; every async effect is at-least-once with an idempotent receiver |
| Files | Filesystem/object bytes + SQL metadata as **two consistency domains**; resumable transfer; content-addressed immutable objects; **Valet-Key** access |
| Runtime Internet | **No mandatory external-Internet dependency** in any runtime (data-plane) path |
| Observability | **Fully local, layered** — logs, health, metrics/traces, audit, sync/command diagnostics — all functioning with zero external connectivity |
| Recovery | Declared **Availability SLO + RTO + RPO**; ≥2 tested backups, ≥1 off-host + immutable; DB/files/runtime keys restored to one **Recovery Point Manifest** |
| Scale-out | Explicit, gated escape hatch (backplane, read replica, deployment stamps); not required at launch |
| Main correctness claim | **No silent data loss, no duplicate business effect, deterministic conflict outcomes, eventual convergence after all mutations settle** |

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
entire pipeline, not just an endpoint. The two pipelines **may share transport
and local orchestration, but never semantics.**

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

### 0.3 The three kinds of truth (never conflated)

```text
1. Server Business Truth
   = globally authoritative settled business state

2. Server Replication Truth
   = the ordered change position (Change Tracking) used to DELIVER
     server state changes — a transport position, not a business sequence

3. Client Provisional Truth
   = local materialized state + durable unsent intent while offline
```

Likewise, the following pairs are **never** collapsed (the full list is §21):

```text
State replication        ≠ business intent settlement
Sync cursor (ChangeSeq)  ≠ entity version (rowversion)
Entity version           ≠ operation identity (OperationId)
Change Tracking          ≠ domain event / Outbox
Notification             ≠ synchronization
Idempotency              ≠ ordering
Concurrency detection    ≠ conflict resolution
Provisional state        ≠ posted/settled state
Authentication           ≠ authorization ≠ entitlement
Backup exists            ≠ restore proven
```

### 0.4 What "10/10 correctness" means in Animora

No distributed/offline system can guarantee that conflicts never occur.
Animora instead guarantees that conflicts are **never silently resolved
incorrectly**. The architecture target, stated as explicit negatives:

```text
No silent data loss
No duplicate financial/business effect
No cross-tenant data exposure
No invalid financial posting
No sync cursor skip
No destructive overwrite of unsent work
No implicit resurrection of deleted records
No permanent unknown settlement
No unbounded retry loop
No notification-dependent correctness
No tenant can monopolize the runtime indefinitely
No full-resync operation can destroy the only good local replica
```

The system may **reject, defer, compensate, or require human review** — those
are valid outcomes. **Silent corruption is not.**

---

## 1. Architecture Drivers (Quality Attribute Scenarios)

Every architectural decision below traces to one of these driving
requirements. This table is the **fitness-function contract**: any future
change must re-check the row(s) it touches.

| # | Requirement (business) | Quality attribute (WAF pillar) | Architectural tactic | Scenario / fitness check |
|---|---|---|---|---|
| R1 | Windows + macOS + Web from one core | Operational Excellence / Portability | Shared Domain/Application/Contracts kernel; split presentation | A change to a business rule requires editing exactly one shared module, not three UIs |
| R2 | High performance on a **weak, single server**, many clients | Performance Efficiency | Push compute to clients; delta-only sync; static-first web rendering; bounded background work; workload isolation | Benchmark: target hardware class handles the committed concurrent-client budget (§16) at defined p95/p99 |
| R3 | Secure, reliable, correct | Security + Reliability | Defense-in-depth tenancy; layered authN/authZ/entitlement; idempotent commands; transactional outbox; encrypted local store; protected keys | Security/chaos gates (§19) all pass |
| R4 | **Offline-first** desktop, robust reconnect, long offline window | Reliability | Durable local Mutation Journal; checkpointed delta pull; explicit settlement states; explicit offline capability | Desktop keeps working and reconciles correctly through disconnect/crash/reconnect (§19) |
| R5 | Multi-user per clinic, correct convergence | Reliability + Correctness | Versioned state; patch/rebase only where provable; domain-specific conflict policy; server-authoritative settlement; append-only ledger | Two offline devices converge to one correct financial truth, never a silent overwrite |
| R6 | Financial correctness | Integrity | Commands, idempotency, append-only ledger, server settlement, period-close policy | Duplicate / lost-response / period-close tests pass |
| R7 | Inventory correctness | Integrity | Movement model + settlement policy + resource constraints where needed | Oversell / concurrency tests pass |
| R8 | Multi-tenancy, subscription/plan management | Security + Cost Optimization | Shared-DB tenancy with layered isolation; server-derived tenant context; independent Subscription/Licensing contexts | Cross-tenant access blocked by a mandatory layer that holds with RLS disabled, AND no code path reaches tenant data outside tenant-aware infrastructure (§9.2) |
| R9 | **Direct server DB maintenance must propagate to clients** | Operability + Consistency | SQL Server Change Tracking makes governed DML visible to the critical sync feed; governed maintenance contract | Direct-SQL visibility gate (§19 Gate Y) passes |
| R10 | Near-real-time collaboration | Responsiveness | Notification *hints* + delta pull; notification transport never authoritative | p95 change-visibility latency target met on a healthy LAN |
| R11 | **Vendor-self-hosted, zero external-Internet dependency at runtime** | Reliability + Security | No mandatory external system in any runtime path; local identity; local PKI; local observability; packaged assets | Isolation test: disconnect the Internet cable, all committed capabilities still function (§17.4) |
| R12 | Central single node is intentional | Cost + Simplicity | Modular monolith, no broker/cache at launch, explicit DR | Restore drill passes |
| R13 | Mixed client versions | Operability | Contract versioning; capability negotiation; N/N-1/N-2 support window | Mixed-fleet gate (§19 Gate M) passes |
| R14 | Long-lived tenant data | Maintainability | Archive/retention policies; reporting projections; migration discipline | Data-growth and restore tests pass |
| R-DR | **Declared recoverability on a single node** | Reliability | Explicit Availability SLO + **RTO** + **RPO** + max offline-command age; ≥2 backup copies, ≥1 off-host + immutable; tested restore | A total-node-loss drill restores within RTO with data loss ≤ RPO, from a tested backup, to one Recovery Point Manifest (§17.3, §19 Gate X) |

**[INVARIANT — single node is an owned SPoF, not an unstated one.]** A single
server node is, by definition, a **single point of failure**; WAF treats
removing SPoFs as a core reliability principle. Choosing single-node at launch
is a legitimate cost/product decision **only because** R-DR makes its blast
radius explicit (declared RTO/RPO + tested restore). The scale-out/redundancy
escape hatch (§9.1, §16.2) is where that SPoF is later removed; it is never
claimed that single-node is itself highly available.

### 1.1 Recommended launch targets

These are architecture targets to be accepted or adjusted through an ADR
before GA. They are **performance/recovery targets, not correctness
assumptions** — any published client capacity is earned from measurement (§16.3).

```text
Maximum supported offline window:       30 days
Change Tracking retention floor:        offline window + 15 days minimum
Recommended launch RPO:                 <= 5 minutes
Recommended launch RTO:                 <= 60 minutes
Foreground change-visibility target:    p95 <= 3 seconds on a healthy LAN
Command settlement target (online):     p95 <= 500 ms excluding local/DB contention
Server resource isolation:              interactive traffic protected from background work
```

A deployment may choose another offline window, but all four retention floors
must move with it as a **single linked decision** (ADR-17):

```text
ChangeTrackingRetention  >= MaxOfflineWindow + SafetyMargin
TombstoneRetention       >= MaxOfflineWindow + SafetyMargin
AuthorizationRevocation  >= MaxOfflineWindow + SafetyMargin
Contract/License retention rules must remain compatible with the same window.
```

---

## 2. Guiding Architecture Principles

These principles resolve ambiguity when a new decision is needed and no
explicit rule below covers it yet.

1. **The server is the only global business authority.** All final
   settlement, conflict resolution and entitlement decisions are
   authoritative on the server — never on a client, never by majority vote
   among clients. Clients can work offline but cannot declare global
   settlement.
2. **State replication and business intent are different flows.** They may
   share transport and local orchestration, but never semantics (§0.1).
3. **Minimize coordination.** *(Azure Architecture Center — minimize
   coordination principle.)* Independent work should not wait for global
   coordination; no chatty per-keystroke round trips to a shared coordinator.
4. **Make illegal states hard to represent.** Financial facts are
   append-only; inventory is movement-based; contended allocation is
   transactional; "set absolute value from a stale client" is unrepresentable.
5. **Use the database for the invariants it can prove well.** Unique
   constraints, foreign keys, transactions, `rowversion`, RLS and Change
   Tracking carry as much correctness as possible — but never become the
   *only* security boundary (§9.2).
6. **Deltas and commands, not dumps.** Every cross-boundary exchange transfers
   either a changed-row delta or an explicit, named business command — never a
   bulk re-send "to be safe."
7. **Every asynchronous effect is at-least-once; every receiver is
   idempotent.** "Dispatched" never means "delivered exactly once."
8. **Every retry has a bounded budget** (max attempts, max elapsed time,
   jitter, per-tenant + global limits) — never an infinite loop.
9. **A notification is never authoritative.** A missing notification must
   never cause missed data; correctness falls back to checkpointed pull.
10. **A checkpoint advances only after durable local application.**
11. **A full resync is additive/reconstructive, never destructive to unsent
    work** — it is built in a shadow replica and switched in atomically.
12. **Defense in depth, with one clearly-owned mandatory layer.** Every
    security- or tenancy-sensitive concern has exactly one layer that is
    *mandatory and sufficient on its own*; every other layer is *hardening*,
    and hardening must never be mistaken for the mandatory layer.
13. **Fail closed, never fail silent.** Every degradation path (offline
    expiry, sync failure, command rejection, overload) has a defined, visible
    outcome. "It just didn't sync" is never an acceptable end state for
    anything with a financial effect.
14. **Tenant scope is derived from server-side authenticated membership**,
    never from client input. **Client-side authorization is UX only.**
15. **Critical direct SQL mutation is governed**, audited, and either
    sync-visible (critical tables) or forbidden (business invariants).
16. **Read models may lie a little; write models never do.** Denormalized,
    cached, or locally-projected views may be imprecise for UX speed; the
    authoritative write model is always normalized, versioned and validated.
17. **Observability is local and complete even without Internet.**
18. **Every named pattern must earn its place** — applied because a driver in
    §1 requires it, cited by name at the point of use (§23), never decoration.
19. **Boundaries are drawn where change happens independently** — not by
    default, not "for future-proofing" alone.
20. **Prefer one strong transaction on a single node to distributed
    choreography; do not introduce infrastructure to solve a problem
    measurement has not demonstrated.**
21. **A conflict is an expected state; silent corruption is a defect.**

---

## 3. C4 Model — System Context (Level 1)

```text
Clinic Staff
  ├── Desktop   (tenant-scoped; each clinic = one tenant)
  └── Web
          │
          ▼
   ┌───────────────────────────────────────┐
   │              ANIMORA                   │
   │  vendor-operated, single central       │
   │  multi-tenant SaaS server node         │
   └───────────────────────────────────────┘
          ▲
          │ (operator-scoped management authority)
Platform Operator (Animora vendor)
          │
          ▼
Subscription / Management Plane

Separate vendor-side systems:

Public Marketing / Subscription Website
    └── register / choose plan / pay / download
              │
              └──(async, out-of-band, versioned onboarding contract;
                  NOT a runtime dependency)──▶ Subscription context
```

- **Clinic Staff (Desktop / Web)** — the primary users: reception, cashier,
  doctor, inventory, manager roles, distinguished by RBAC + policy (§10).
- **Clinic Owner/Manager** — a role within a tenant, not a separate backend;
  elevated authorization within the same tenant.
- **Platform Operator (Animora vendor)** — vendor staff with controlled
  cross-tenant management authority, especially subscription, licensing and
  support. Architecturally this is an **operator-facing surface of the
  Subscription bounded context** (§13.3), not a separate backend.
- **Public Website (the commercial front door)** — where a prospective
  customer **self-registers, picks a plan, pays, and downloads the installer**.
  This is the **first step of the product's own customer journey** (register &
  buy → download → log in & use), not an optional extra. It is an
  **independent vendor-side system** with its own architecture, connected to
  Animora only through a narrow, versioned, asynchronous **onboarding /
  subscription contract boundary** (§13.4, §17) — it hands a *provisioning
  intent* to the Subscription context out-of-band, and never shares a database
  or holds a synchronous runtime dependency on the clinic-serving path (this is
  what keeps R11 true even with the website live). The desktop/web clients a
  customer later runs depend only on the Animora server, never on the website.
- **No other external system participates in any runtime path.** This is drawn
  explicitly as an *absence* because R11 is an architectural constraint, not an
  incidental fact.

---

## 4. C4 Model — Container View (Level 2)

```text
Desktop Client Container (native process, per device)
  Presentation (native UI, MVVM)
  Local Application Core (shared)
  Local Encrypted Store (SQLite + EF Core)
  Mutation Journal · Business Command Queue · Sync Checkpoints · Conflict Inbox
  Sync Coordinator
  Secure Device Storage (OS-protected keys / device identity)

Web Client Container (server-rendered, browser)
  Presentation (web UI) — static SSR default + selective interactive islands
  No durable offline replica

Both ──HTTPS/LAN──▶ Animora Server Process (single deployable)
  Desktop sends critical deltas + business commands (+ optional Datasync ops);
  Web sends requests.

  Server Process contains, in dependency order:
    Authentication / Authorization
    Business API Surface (commands/queries/reports)
    Critical Sync API (custom state replication)
    Datasync Compatibility Surface (SYNC-SIMPLE only)
    Notification Hint Surface (optional, non-authoritative)
    Operator / Management APIs
      ──▶ Application / Domain Core (bounded contexts, §6 — DDD-lite)
    Application/Domain Core
      ──▶ Background Workers (Outbox relay, Change-Tracking reader, purge,
          reconciliation, entitlement checks)
      + Reporting / Read Projections
      + Infrastructure (data access, file store, crypto, PKI, clock)
    Background Workers + Infrastructure
      ──▶ Relational Database (tenant data, ledger, outbox, audit, Change
          Tracking) + File Store (binary content, metadata in DB)

Management Plane (out-of-band, never on the clinic runtime path)
  provisioning · license issuance · software update · backup export · support
```

| Container | Responsibility | Talks to | Owns |
|---|---|---|---|
| Desktop Client | Offline-capable native presentation + local data + sync/command origination | Server (HTTPS/LAN) | Local encrypted replica, Mutation Journal, pending command queue, device identity |
| Web Client | Online-only thin presentation | Server (HTTPS/LAN) | Nothing persistent locally |
| Business API Surface | Command intake, queries, reporting | Desktop, Web, Application Core | Command intake contract, query/report contracts |
| Critical Sync API | Custom entity replication (push/pull) for SYNC-CRITICAL state | Desktop, Application Core | Delta cursors (Change-Tracking-backed), replication contracts |
| Datasync Compatibility Surface | Library-driven replication for SYNC-SIMPLE state only | Desktop, Application Core | Datasync table controllers + wire format |
| Notification Hint Surface | Optional "scope changed" hints | Desktop, Application Core | Non-authoritative hint transport |
| Application/Domain Core | Business rules, invariants, workflows | All surfaces, Infrastructure | Domain model, bounded-context boundaries |
| Background Workers | Outbox relay, Change-Tracking reader, retries, purges, reconciliation | Application Core, Database | Worker state (claims/leases) |
| Reporting / Read Projections | Read-only aggregates/read models | Application Core, Database | Projection state + freshness watermark |
| Infrastructure | Data access, file storage, crypto/PKI, external-free integrations | Database, File Store | Cross-cutting infra concerns |
| Database | Tenant data, financial journal, inventory movements, outbox, audit, Change Tracking | — | System of record |
| File Store | Binary content addressed by hash | — | File bytes |

### 4.1 Surface invariant

**[INVARIANT] The following are architecturally distinct entry points into the
same Application/Domain Core**, and none of them bypasses the domain layer to
write directly against business tables (§8), except a **formally defined,
governed database-maintenance operation** (§20):

```text
Business API            (/business/*)   — semantic commands & queries
Critical Sync API       (/sync/*)       — custom state synchronization
Datasync adapter surface (/tables/*)    — SYNC-SIMPLE compatibility only
Notification hint surface (/notifications/*) — optional, non-authoritative
Operator / Management APIs (/operator/*)
```

The actual route names can differ; the separation of **semantics** cannot
(§97). OData is a Datasync implementation detail only and no business client
may depend on arbitrary OData semantics (§98).

---

## 5. C4 Model — Component View: Clean/Onion + Ports & Adapters

**[PATTERN] Clean / Onion Architecture** (Microsoft's ".NET application
architecture guides" — layered/Clean Architecture reference shape):

```text
Presentation (Business API · Critical Sync API · Datasync surface ·
  Notification hints · Operator APIs)
  — maps transport contracts to Application calls; no business logic here
      ▼ depends on
Application (use cases / command & query handlers, workflows, idempotency,
  authZ orchestration, sync coordination ports, settlement orchestration,
  conflict-resolution orchestration, transaction boundaries)
  — orchestrates Domain objects; owns the application-level ports
      ▼ depends on
Domain (aggregates, entities, value objects, invariants, domain services,
  domain events — Finance/Inventory/Scheduling/Clinical/...)
  — zero outward dependencies; the "clean core"
      ▲ implemented by (Infrastructure depends inward on Domain/Application)
Infrastructure (EF Core, SQL Server, Change-Tracking adapter, Datasync
  adapter, SQLite adapter, encrypted local store, file store, Data Protection,
  PKI, outbox dispatcher, notification adapter, clock, device key storage,
  diagnostics)
  — implements interfaces defined by Domain/Application
```

**[INVARIANT] Dependencies point inward only.** Domain depends on nothing.
Application depends only on Domain. Infrastructure and Presentation depend
inward on Application/Domain, never the reverse.

**Domain knows nothing about:** HTTP, EF Core, Datasync, SQL Server, SQLite,
Blazor, Avalonia, authentication tokens, file paths, notification
technologies, or the Change-Tracking mechanism. This is what makes the **sync
engine sit behind an abstraction** architecturally natural rather than an
arbitrary rule: a replication library or a Change-Tracking reader is an
*Infrastructure* concern implementing a Domain/Application-owned interface.

**[PATTERN] Ports & Adapters (Hexagonal) at the infrastructure boundary** —
every external-facing capability is a **port** (interface) implemented by an
**adapter** in Infrastructure. Port *ownership* follows the capability: a
business-meaningful port (e.g. a domain repository) may be Domain-owned; a
purely technical/orchestration port that is **not** a business concern — the
**sync engine especially** (§6: synchronization is not a bounded context) — is
**Application-owned, never Domain-owned**. Representative ports:

```text
IClock                       ITransaction / IUnitOfWork
IStateSyncEngine             IChangeFeed
IMutationStore               IBusinessCommandTransport
IFileStore                   IAuditSink
INotificationHintChannel     ISecureKeyStore
IEntitlementProvider
```

This is *why* replacing or forking a third-party dependency (the Datasync
library, or the Change-Tracking reader) never requires a Domain rewrite
(§8.1, §13).

---

## 6. Bounded Contexts & Context Map (DDD-lite)

**[PATTERN] Domain-Driven Design — bounded contexts inside a Modular
Monolith.** Contexts are **logical module boundaries in one deployable**, not
microservices. Each context owns its aggregates and exposes only explicit
contracts (commands, queries, domain events) to other contexts.

| Bounded context | Owns | Publishes (to other contexts) | Depends on |
|---|---|---|---|
| **Identity** | Users, credentials, devices, roles | `UserAuthenticated`, `DeviceRegistered`, `CredentialChanged` | — |
| **Tenant/Clinic** | Tenant record, clinic profile, staff membership | `TenantProvisioned`, `MembershipChanged` | Identity |
| **Patient/Owner** | Patients, owners/contacts | `PatientCreated`, `OwnerChanged` | Tenant/Clinic |
| **Clinical** | Medical history, vaccination, visit records | `VisitRecorded`, `VaccinationDue` | Patient/Owner |
| **Scheduling** | Appointments, resources, allocations, boarding slots | `AppointmentBooked`, `AppointmentMoved` | Patient/Owner |
| **Sales/POS** | Cart, checkout, cashier sessions | `SaleCompleted`, `CashSessionClosed` | Patient/Owner, Inventory (query) |
| **Inventory** | Products, lots, stock movements, quantity-on-hand (derived) | `StockReceived`, `StockAdjusted`, `LowStockDetected` | Tenant/Clinic |
| **Finance** | Journal, invoices, payments, cheques, expenses, periods | `InvoicePosted`, `PaymentSettled`, `PeriodClosed` | Sales/POS, Inventory (events) |
| **Reporting** | Read-only aggregates/read models/projections | *(no writes)* | Finance, Inventory, Clinical (read-only) |
| **Files** | File lifecycle, metadata, storage reconciliation | `FileAvailable`, `FileQuarantined` | Tenant/Clinic |
| **Subscription** | Plans, seats, billing periods, grace states | `PlanChanged`, `SeatLimitReached`, `SubscriptionSuspended` | Tenant/Clinic |
| **Licensing** | Signed license verification, installation binding | `LicenseValidated`, `LicenseExpiring` | Subscription |
| **Audit** | Append-only audit log of security/finance actions | *(sink only — subscribes to all)* | all contexts (read-only subscriber) |
| **Synchronization** *(infra, not a business context)* | Delta cursors, replication contracts, sync diagnostics | *(infrastructure-facing)* | Application-layer ports of every syncable context |
| **Notification** *(infra, not a business context)* | Delivery *hints* only | *(infrastructure-facing)* | Sync state |

**Synchronization and Notification are NOT business bounded contexts** — they
are **infrastructure capabilities** (§8, §44) that every syncable context's
Application layer opts into via a port; they never own business rules.

### 6.1 Context map — coupling rules

1. **Finance never directly edits Sales or Inventory records**, and never
   calls them synchronously to *decide* a business outcome. It reacts to their
   published domain events (`SaleCompleted` → create draft invoice;
   `StockReceived` → no direct financial effect until a purchase-invoice
   command settles) or invokes an explicit Application workflow. This keeps
   Finance's invariants (append-only, idempotent) from leaking into contexts
   that do not need them.
2. **Reporting never writes** business state. It is a pure read-side consumer
   across contexts — see CQRS-lite (§11.3).
3. **Audit is a pure sink / never a coordination channel.** Every other
   context publishes to it; it never publishes back, and nothing waits on it.
4. **Synchronization never decides business policy.**
5. **Notification never owns state.**
6. **Identity never grants entitlement; Entitlement never authenticates.**
7. **Tenant context never comes from a client payload** (§9.2).
8. **Cross-context writes are either** atomic within one Application
   transaction, **or** explicit eventual consistency with a *visible
   intermediate status* (§6.2).

### 6.2 Cross-context financial consistency (declared per interaction)

**[DECISION]** Because all contexts live in one process and one database
(Modular Monolith), cross-context coordination is **not** a distributed
transaction. For each financial interaction the architecture states explicitly
whether it is **atomic** (one DB transaction spanning multiple aggregates via
an Application-layer workflow) or **eventually consistent** (event + Outbox,
with a visible provisional state). The full matrix is §38.

**[INVARIANT — no silent "financially incomplete" gap.]** Where an interaction
is eventually consistent, the not-yet-posted state is an **explicit, visible
status** (e.g. `PendingPosting`), never a window in which a sale looks complete
while Finance has no record and nothing says so (A-RULE 038). An
Application-layer workflow may coordinate several aggregates in one transaction
while each aggregate's domain ownership stays intact — this is *not* a
saga/distributed-transaction layer (§22).

---

## 7. Multi-Platform Client Architecture

### 7.1 Shared kernel vs. split presentation

**[DECISION]** Using two different presentation technologies for desktop and
web is a deliberate architectural boundary, not duplication:

```text
Shared, single source of truth:
  Domain · Application · Contracts · Validation rules · Authorization policy
  DEFINITIONS · Localization/formatting · Sync ports · DTOs · Error codes ·
  Command definitions · File-access contracts

Split by design (presentation only):
  Desktop presentation  (native, offline-capable host)
  Web presentation       (server-rendered, online-only host)
```

**[INVARIANT — shared authorization code is not a shared security authority.]**
Sharing authorization *policy definitions* keeps client and server from
drifting, but the **client evaluation is only a UX optimization / local
gating** (hide a button, pre-validate a form). A client is compromisable; **no
security invariant may ever depend on the client's execution of a policy.** The
server re-evaluates every authorization decision — including resource-based
checks against the actual resource instance (§10.3) — as the sole authority,
even when it runs byte-identical policy code the client just ran. Client says
"probably allowed"; the server says "allowed."

**Rationale (architectural, not tooling):** the desktop presentation host must
run as a genuine native offline process (no browser runtime in the offline
path — R4 is a hard requirement); the web presentation host is optimized for
near-zero per-client footprint on a weak server. Forcing both into one
presentation technology would subordinate one of R2 or R4 to "maximize code
reuse," which this architecture explicitly rejects (§2, principle 19 — a native
offline host and a stateless web renderer *do* change for different reasons).

### 7.2 Client roles

| Client | Role | Local persistence | Offline capability | Talks to |
|---|---|---|---|---|
| **Desktop client** | Full offline-capable workstation | Encrypted local replica + Mutation Journal + durable pending-command queue | Full, governed (§8, §10.4) | Critical Sync API + Business API (+ Datasync surface, + optional hints) |
| **Web client** | Thin, always-connected workstation | None persistent | None (explicit non-goal, §22) | Business API (+ read via Sync-backed queries where useful) |
| **Admin/Operator surface** | Platform operator's subscription oversight | None persistent | None | Subscription context only (§13) |

**[INVARIANT] A web client session never assumes a local write cache survives
a page reload.** Every web mutation is a synchronous Business API call; there
is no local queue to reconcile.

### 7.3 Interaction-pattern routing (web presentation)

**[DECISION]** Web presentation work is routed by **interaction frequency**,
not just "page type":

```text
Simple read / occasional filter         → no-interactivity (static) render path
Per-keystroke interactive search/filter → stateful interactive island
Large/typeahead data query              → dedicated query endpoint (debounced, paged)
Data-heavy or mutating operation        → Business API call, always
```

This is a *behavioral routing rule*; the exact rendering technology that
implements "no-interactivity" vs. "stateful interactive island" is a
`STACK.md` concern. Web architecture rules are detailed in §46.

### 7.4 Desktop composition

```text
Native UI (MVVM)
  ↓
Shared Application Core
  ↓
Local Repository
  ↓
Encrypted SQLite / EF Core
  ↓
Sync Coordinator
```

**[INVARIANT] No browser/WebView is in the desktop offline path.** (§47)

---

## 8. Offline-First & Synchronization Architecture (the heart)

This is the most safety-critical part of the architecture. It is built from
**three synchronization mechanisms that never exchange semantics implicitly**,
sitting on top of a **durable local Mutation Journal** and an explicit
**settlement lifecycle**.

### 8.0 The three synchronization mechanisms

```text
A. Critical State Synchronization   (the correctness authority)
      Animora Custom Sync Engine
              ↓
      SQL Server Change Tracking

B. Simple Replicated State          (a contained convenience adapter)
      CommunityToolkit.Datasync (adapter)
      — permitted ONLY for explicitly classified SYNC-SIMPLE entities

C. Business Intent Settlement       (durable, idempotent)
      Business Command Pipeline
              ↓
      Transactional server commit (server-authoritative settlement)
```

A and B are *state replication*; C is *business intent* (§0.1). A is the
authority for all collaborative/high-integrity state; B is a strictly-bounded
escape hatch; C is a wholly separate pipeline.

### 8.1 The sync engine as an isolated Infrastructure adapter

**[PATTERN] Ports & Adapters / Anti-Corruption Layer.** Synchronization is
**not** a Domain capability (§6 — it is not a business bounded context), so its
port is owned by the **Application layer**, not Domain. The Domain knows
nothing about sync at all. Both the custom Change-Tracking-backed engine and
the Datasync library are wrapped by Application-owned ports implemented in
Infrastructure:

```csharp
public interface IStateSyncEngine
{
    Task<SyncResult> SynchronizeAsync(SyncScope scope, CancellationToken ct);
}
```

**[INVARIANT] Domain and Application code never reference a replication
library's types directly; Domain references no sync abstraction at all.** The
orchestration interface for sync lives in Application; Domain is entirely
unaware of it. This exists so that (a) the replication technology is swappable
without touching business code, (b) the domain layer's dependency graph stays
clean per §5, and (c) a future custom replacement engine is a pure
Infrastructure change.

### 8.2 Why a third-party sync library is NOT the universal mechanism

**[DECISION — core correction, authoritative in this unified baseline.]** The
Datasync library maintains its own operation queue and delta-token mechanism;
its documented delta token is derived from the **maximum `UpdatedAt`** of the
pulled set, and its offline context refuses normal pulls for an entity while
that entity still has pending operations, and its resync guidance *removes*
pending operations and delta tokens before a new pull. Those behaviors are
fine for a simple occasionally-connected CRUD app, but they are **not
sufficient as Animora's correctness authority** for critical collaborative
state, because:

- A **wall-clock `UpdatedAt` cursor is not safe** under concurrent commits,
  clock skew, retries and paging — two rows written in the same tick, or a row
  whose commit becomes visible out of timestamp order, can be silently skipped.
- Its generic resolvers are **whole-row last-write-wins only** — unacceptable
  for money/stock/scheduling.
- Its resync **deletes pending operations** — unacceptable for unsent business
  intent (§11, A-RULE 023).

Therefore:

```text
Datasync            = adapter (a containment boundary, not a rejection)
Animora Sync Engine = architecture authority
```

### 8.3 Synchronization classification

Every synchronizable entity is assigned exactly one class:

```text
SYNC-CRITICAL     Custom Sync Engine + Change Tracking
SYNC-SIMPLE       Datasync adapter (only if it qualifies, below)
COMMAND-ONLY      Business Command Pipeline, never CRUD sync
PROJECTION-ONLY   server-derived read model (never client-authored)
NOT-SYNCED        local-only / device-local
```

**SYNC-CRITICAL** (mechanism A) — patient/owner records, clinical mutable
metadata, appointment state, draft documents, operational assignments,
inventory operational projections, financial documents that must be visible
offline. Actual inclusion is per entity/aggregate.

**SYNC-SIMPLE** (mechanism B) — reference catalogs, low-risk lookup data, UI
preferences, static configuration. The Datasync adapter is permitted **only
if** all of:

- the timestamp-cursor collision safety has passed its technology PoC
  (`STACK.md` gate), AND
- conflict semantics are genuinely whole-row-resolution compatible, AND
- no critical unsent work depends on its pull semantics, AND
- the entity has no meaningful concurrency concern.

If any condition fails, the entity is SYNC-CRITICAL instead.

**COMMAND-ONLY** (mechanism C) — `RegisterPayment`, `PostInvoice`,
`ReversePayment`, `AdjustStock`, `BookAppointment`, `CloseCashSession`,
`ClosePeriod`, … These are **never** modeled as generic record CRUD.

### 8.4 Replicated-entity contract shape (state, not intent)

Every syncable entity carries a client-facing contract with (architectural,
not a technology detail):

```text
Id            — globally unique identifier, stable across replicas
ChangeSequence— server-issued, strictly-increasing, opaque change cursor
                (per sync scope); the ONLY value a client cursors on.
                Implemented by the SQL Server Change Tracking version (§16),
                but application code never exposes SQL-specific types.
UpdatedAt     — server-stamped business/display timestamp; informational
                only — NEVER a critical delta cursor and NEVER an ordering source
Version       — opaque concurrency token (rowversion); changes on every
                server write; detection only, never a cursor
Deleted       — soft-delete flag (tombstone), never a hard delete in transit
```

**[INVARIANT — the critical cursor is `ChangeSequence`, never `UpdatedAt`.]**
The single value a client advances its critical delta cursor on is the
server-issued, strictly-increasing, opaque `ChangeSequence` (a monotonic
server change-log position, never a wall clock). `UpdatedAt` is retained for
business display/auditing only. This is unconditional for SYNC-CRITICAL state;
timestamp-based delta pull is *never* proven safe under concurrent writes,
retries, paging, disconnects and clock skew. (The SYNC-SIMPLE Datasync path
uses the library's own `UpdatedAt` cursor and is therefore gated to entities
where that is proven safe, §8.3.)

```text
ChangeSequence = 8471932            ← client cursors on THIS (critical path)
UpdatedAt      = 2026-10-08T08:31Z  ← display/audit only
Version        = <opaque rowversion> ← row concurrency, not ordering
```

**[INVARIANT] Server entity/write models are never exposed directly to
clients.** A dedicated, separate client-facing contract exists for every
synced type, so server-only fields (authorization metadata, internal computed
columns) can never leak, and the server stays free to reshape its internal
model without breaking the wire contract.

**[INVARIANT] `ChangeSequence`/`Version`/the replication operation identifier
are never a global business/event sequence.** They are transport-layer
bookkeeping for *replication correctness* only — `ChangeSequence` orders
*replication delivery* within a sync scope, not *business settlement* order.
Business ordering is a completely separate concept (§8.9).

**[INVARIANT] No destructive sync.** An inbound pull must never blindly
overwrite unsent local business intent. Draft invoices, draft payments,
in-flight inventory transactions and pending appointment edits are reconciled,
never silently clobbered by a later-arriving replicated row (A-RULE 017).

### 8.5 Local materialized state & the Mutation Journal

The desktop local database is a **replica, not an independent global
authority**. It contains:

```text
MaterializedState        LocalReferenceData        PendingMutationJournal
BusinessCommandQueue     SyncCheckpoints           ConflictInbox
DeviceState              LocalAudit/Diagnostics
```

**[DECISION] Every offline mutation that must survive a crash is recorded
durably in a local Mutation Journal, owned by Animora** — even when the actual
transport adapter happens to be Datasync. Logical fields:

```text
MutationId · MutationKind · EntityType · EntityId · AggregateId · BaseVersion ·
Patch / CommandPayload · ChangedFieldSet · CausationId · DependsOnMutationId ·
LocalSequence · CreatedAtUtc · Status · AttemptCount · LastErrorCode · LastErrorAt

MutationKind ∈ { StatePatch, BusinessCommand, DatasyncOperation }
```

#### 8.5.1 Atomic local mutation

**[INVARIANT] Local state and its journal record commit in ONE local
transaction.** For a state mutation:

```text
BEGIN LOCAL TRANSACTION
  1. Validate locally
  2. Update local materialized state
  3. Insert mutation journal record
  4. Update UI-visible provisional state
COMMIT   — either all happen or none happen
```

This eliminates both "UI says saved but the mutation queue lost it" and
"mutation exists but local state never committed."

### 8.6 Business Command Architecture (intent, not state)

**What flows here:** named business commands with real invariants — post an
invoice, register/reverse a payment, receive/adjust stock, close a cash
session, book/move an appointment, close an accounting period, approve a
discount, issue a credit note. These are never modeled as generic record CRUD,
and they are **small and semantic**: prefer `RegisterPayment` over
`UpdatePayment`, `AdjustStock` over `SetQuantity`, `BookAppointment` over
`UpdateAppointmentSlot` (§95) — the command says *what the user intends*, not
merely which rows to mutate.

**Desktop flow (the canonical shape):**

```text
User action
   → local validation
   → atomic local transaction (durable, provisional) + Mutation Journal record
   → durable PendingCommand recorded
        ├─ connected    → submit to Business API
        │                   → server: authorization → concurrency →
        │                     business invariants → transactional commit
        │                   → Accept / Reject / Conflict
        │                   → settle locally
        └─ disconnected → remains queued; submitted with priority on reconnect
                           (ahead of routine background sync)
```

Each command carries:

```text
OperationId · CommandType · AggregateId · Payload · CommandFingerprint ·
CausationId · DependsOnOperationId · ExpectedAggregateVersion (where applicable) ·
CreatedAtUtc (client-local, informational only) · LocalSequence · Status
```

**[INVARIANT — identity is server-derived, never client-asserted.]**
`TenantId`, `UserId`, `DeviceId` and the full authorization context are
**resolved server-side from the authenticated, device-bound credential on
every command** — they are *not* read from the command payload, exactly as
`TenantId` is never trusted from a sync write (§9.2). A client may stamp a
local, informational copy for its own queue bookkeeping, but the server ignores
it and binds the authoritative values itself; `DeviceId` in particular is bound
to the device's registered credential/key, not a self-declared field (§10.4).
This is codified as A-RULE 036.

**Why a command queue is architecturally separate from the replication
operation queue:** accounting needs deterministic idempotency, authorization,
aggregate-version validation, transactional posting and an explicit settlement
lifecycle — properties a generic replication queue does not provide and should
not be burdened with. Merging them would let a replication reordering silently
become a financial reordering.

#### 8.6.1 Operation identity & idempotency

**[PATTERN] Idempotent Consumer** (Azure Architecture Center). The server
keeps a durable idempotency record per command so an at-least-once retry never
re-applies a financial effect. Minimum shape:

```text
OperationId · CommandType · FirstSeenAt · LastAttemptAt · Status ·
ResultHash/ResultReference · AttemptCount · LastErrorCode · CommandFingerprint
```

**[INVARIANT] Server uniqueness key `(TenantId, OperationId)`.** A retry with
the same `OperationId` **and identical `CommandFingerprint`** returns the
previously-stored result and never applies a second effect.

**[INVARIANT] Same `OperationId` with a *different* `CommandFingerprint`
(payload hash) is a security/integrity error, not a replay** — the server
detects it, audits it, and rejects it. "Operation exists → return old result"
is valid *only* for a byte-identical retry. This is stronger than a simple
"already seen" cache.

**[INVARIANT] Idempotency does not solve ordering.** `(TenantId, OperationId)`
guarantees at-most-one effect for an *identical* command; it says nothing about
*when* it may apply relative to other commands. Ordering is handled explicitly
(§8.9).

#### 8.6.2 Settlement state machine

```text
Draft
  ↓
LocallyCommitted
  ↓
PendingSettlement
  ├── Settled
  ├── Rejected
  ├── Conflict
  ├── NeedsAttention
  └── SettlementUnknown
```

**[INVARIANT — "Posted/Settled/Closed" are server/accounting words, never
client-local states.]** The pre-settlement local state is `LocallyCommitted`,
deliberately **not** "LocallyPosted": *posted* means an irreversible
server-settled ledger fact, and no client-local provisional state may borrow
that word in a label, event, or report (A-RULE 038). The settlement lifecycle
is also kept distinct from any business lifecycle status an aggregate carries —
a command's *settlement* state answers "did the server accept this intent," not
"what phase is this sale/visit in."

**[INVARIANT — a reversal is a new command, not a terminal state.]** Reversing
a settled effect is itself a *new* business command referencing the original
`OperationId`; it is never a state the original decays into. The original
settlement stays `Settled` forever; a separate settled reversal command records
the compensating effect with its own audit trail (§11.1, §35.2).

#### 8.6.3 SettlementUnknown (the lost-response case)

**[INVARIANT] A lost response never becomes a new OperationId.** Critical
scenario: client sends `Op123` → server commits → the response is lost. The
client enters `SettlementUnknown` and **must not** mint a new `OperationId` to
"try again." Recovery is to **retry/query the same `Op123`**; the server
returns the original result. This is what guarantees **at-most-one business
effect under at-least-once transport** (§19 Gate D).

#### 8.6.4 Sync-failure UX contract

**[INVARIANT] No bare "Sync failed" for a critical command.** Whenever a
command cannot settle, the architecture guarantees an answer, surfaced to the
user (not merely logged), to:

```text
What failed?   Why?   Does it have a financial/business effect?
What should the user do?   Who must fix it?
```

### 8.7 The logical sync contract (technology-independent)

A client holds `SyncCheckpoint`, `SyncProfileVersion`, `PendingMutationJournal`
and a `ConflictInbox`. A synchronization session is:

```text
1.  Authenticate
2.  Validate device / capability
3.  Establish scope
4.  Reconcile unknown settlements (SettlementUnknown → query server)
5.  Push eligible local mutations (commands first, by dependency; §8.9)
6.  Pull server state deltas (critical change feed; §16/§17)
7.  Apply / rebase locally (echo-safe; §52)
8.  Commit local transaction
9.  Advance checkpoint (only after durable apply)
10. Emit UI state
```

The exact HTTP route names are not an architectural dependency.

### 8.8 Change Tracking vs. Outbox (two feeds, never merged)

```text
Change Tracking: which replicated STATE rows changed? (delivery position)
Outbox:          which domain EVENTS/effects did a transaction produce?
```

For a business command, one transaction writes *business state + audit +
outbox event*; at commit, Change Tracking also records the affected
synchronized rows. **The two feeds are not merged** (A-RULE 038/039). A client
receives "Invoice projection changed" via critical state sync — it does **not**
receive the internal `InvoicePosted` domain event unless a separately defined
contract explicitly requires it (§94).

### 8.9 Two independent transports — explicit dependency ordering

**[INVARIANT] The replication operation queue and the business command queue
are two independent transport mechanisms.** Neither is globally ordered with
the other, with itself beyond what it explicitly guarantees, or across
devices/aggregates. Network arrival order is never business order.

A business command (or a state mutation) that depends on another's outcome
**must carry explicit causal metadata**. `CausationId` and
`DependsOnOperationId` are **two distinct concepts, never conflated**:

```text
CausationId          — provenance: which operation/event CAUSED this one to
                        exist (audit/traceability)
DependsOnOperationId — a hard settlement precondition: which operation MUST
                        settle on the server before this one may be evaluated
                        (+ expected aggregate version, where known)
```

**[INVARIANT — the dependency graph is a DAG.]** `DependsOnOperationId` edges
must form a directed acyclic graph; a cycle is detected and rejected *before*
execution, never deadlocked at settlement. Dependencies may reference both a
`StateMutation` and a `BusinessCommand`.

A command with an unsatisfied dependency is **deterministically delayed or
rejected** — never blindly accepted — until the prerequisite has **settled on
the server** (not merely been sent).

**Worked example:**

```text
M1 CreatePatient (state mutation)
      ↓
C1 CreateInvoice (Op1)   ── settles → server knows invoice aggregate/version
      ↓
C2 RegisterPayment (Op2, DependsOnOperationId = Op1, ExpectedInvoiceVersion = v)

Op1 Settled            → evaluate Op2 (version check → Accept/Reject/Conflict)
Op1 Pending/Unknown    → hold Op2 (deterministic delay) or reject (retriable)
Op1 Rejected/Reversed  → reject Op2 (its precondition never became true)
```

This resolution happens **server-side only** — a client cannot "helpfully"
reorder, and a transport-level retry cannot smuggle a dependent command ahead
of its prerequisite's settlement.

### 8.10 Mutation scheduler (client)

The client scheduler partitions work into: *Critical Business Commands ·
Critical State Mutations · Simple Datasync Operations · Routine Pull ·
Maintenance.* Ordering rules:

1. Dependency edges are honored (DAG, §8.9).
2. Independent aggregates may run concurrently **only when provably safe**.
3. Business commands default to conservative serialization.
4. Datasync parallelism stays at 1 unless a specific entity is proven
   order-independent.
5. Local Mutation Journal order is deterministic.
6. Network arrival order is never treated as business order.

### 8.11 Sync-contract versioning & rollout

**[INVARIANT] Every replicated contract carries an explicit `ContractVersion`**,
independent of ordinary schema migrations. When a synced contract's shape
changes in a wire-incompatible way, an older client must **detect**
"unsupported contract version" and trigger a **controlled upgrade/resync** —
never silently mis-map or partially sync.

**[DECISION] Staggered, mixed-version fleets are the normal operating case.**
The architecture keeps at least three consecutive client generations (N / N-1 /
N-2) working correctly against one current server, with deterministic migration
and sync behavior across that window (§79, §19 Gate M).


### 8.12 Conflict resolution architecture

**[DECISION] Convergence claim, precisely stated:** *eventual convergence to
server-authoritative business state under explicit domain conflict policy* —
never a generic "strong convergence" claim. A version mismatch means "the row
changed under us"; it does not know that two payments summing to more than an
invoice's balance is a business conflict.

**Resolution pipeline (each layer strictly narrower than the last):**

```text
1. Detect            — BaseVersion != CurrentServerVersion ⇒ "stale"
                       (stale means "the row changed under us", NOT "wrong")
2. Classify          — which data type / aggregate / policy applies
3. Gather evidence   — the server collects what it actually knows (§8.13)
4. Apply domain      — field-aware, aggregate-aware policy per data type
   policy               (generic resolvers allowed ONLY for non-critical data)
5. Commit or reject  — on settlement the server re-runs authorization +
                       concurrency + business invariants and returns
                       Accept/Reject/Conflict independent of the version outcome
6. Record audit
7. Rebase local state (echo-safe; §52)
```

Idempotent commands (§8.6.1) guarantee a retry after any of the above never
double-applies an effect.

### 8.13 Conflict evidence & the conservative rule

**[INVARIANT] Automatic field-level merge is allowed ONLY when non-overlap can
be *proven*.** For critical state the engine may use available server change
evidence:

```text
rowversion · Change Tracking operation · changed-column metadata (where enabled)
· domain mutation history (where maintained) · maintenance-origin metadata
```

Change Tracking answers *which row changed, what operation, at which version* —
it is **not** a complete field-history system (§16.3). So:

```text
Client changed Phone; server changed Address; proven disjoint     → merge
Client changed Phone; server changed the same row several times,
  history unavailable                                             → do NOT guess
```

**[INVARIANT — conservative rule]** If the server **cannot prove** that a stale
local patch is non-overlapping with authoritative changes, it must **not**
silently merge; the result is `Conflict / NeedsAttention`. This is
intentionally safer than speculative field merging, and is especially important
when a row changed multiple times between the client's base version and the
current version, or when a governed maintenance operation modified the row
outside the normal command path.

### 8.14 Conflict-policy matrix (business ownership, not a technology choice)

| Data type | Default policy |
|---|---|
| UI preferences / display settings | Last-write-wins acceptable |
| Low-risk reference metadata | Merge or server-wins |
| Non-critical free text | Field-aware merge |
| Patient demographic field | Field-aware merge **if proven**; otherwise human review |
| Medical history | Append/preserve history; never blind overwrite |
| Appointment slot | Transactional resource allocation (§37); reject on conflict |
| Draft invoice | Version conflict + user resolution |
| **Posted invoice** | **Immutable → reversal / credit note only** |
| **Payment** | **Idempotent command, never overwritten** |
| **Ledger** | **Append-only, forever** |
| **Inventory movement** | **Append movement; settlement policy (§36)** |
| **Quantity-on-hand** | **Derived; never a client absolute write** |
| Cash session | Domain workflow (open/close invariants) |
| User roles / entitlements | Server authoritative, never client-negotiable |
| Audit records | Append-only, forever |
| File object | Immutable versioned object (§56) |

**[INVARIANT] No blind client-wins/server-wins for anything with a financial
effect.** A financial conflict is resolved by a compensating/adjustment entry
with reason + audit — never by rewriting a posted value.

**[DECISION — model to minimize conflict.]** Where possible, design the model
so independent operations are *additive* (prefer separate `ClinicalEntry`
records over one giant mutable `MedicalHistoryText` blob). This naturally
reduces conflict frequency (§34).

### 8.15 Echo safety

**[INVARIANT] A client must safely tolerate receiving its own committed state
again.** Client submits `M1` → server commits → client receives settlement →
a later delta includes the resulting row. The second application must **not**
duplicate a business operation, reset dirty flags incorrectly, create duplicate
audit records, or recreate a pending mutation (§19 Gate AA).

### 8.16 Representative sequence flows

**Normal online business write:**

```text
Desktop/Web → Business API: Command(OperationId, Payload, Fingerprint)
Business API → Application: validate authZ, concurrency, invariants
Application → Domain: apply, produce events
Domain → Infrastructure: persist (single transaction) + Outbox write
Business API → Client: Accept (Settled) | Reject | Conflict
```

**Offline write → reconnect:**

```text
Desktop (offline): local validation → atomic local commit + journal → queued
Desktop (reconnect): reconcile SettlementUnknown; priority-submit commands
Server: idempotency check → authorization → concurrency → invariants
Server → Desktop: Settled | Rejected | Conflict → local settlement state updates
Desktop UI: reflects settlement state, never leaves it ambiguous
```

**Dependent commands, adverse interleaving:** every interleaving of push order,
partial delivery, and push failure must resolve to the *same* deterministic
outcome — dependency settlement, not arrival order, governs acceptance
(§8.9, §19 Gate O).

---

## 9. Multi-Tenancy Architecture

### 9.1 Tenancy model

**[DECISION] Shared SQL Server, shared database, shared schema, tenant-
discriminator model** — the standard, cost-efficient shape for many small B2B
tenants (Azure Architecture Center multitenancy guidance). Each clinic is one
tenant; many users per tenant; every tenant-owned record carries authoritative
tenant ownership.

**[PATTERN] Deployment Stamps pattern as the documented escape hatch.** A
future `Tenant → DeploymentStamp → Database` mapping lets one large/noisy
tenant move to a dedicated database/stamp without a domain rewrite. This is
designed *for* in the data-access abstraction, and **[DEFERRED]** until a real
tenant requires it (§72).

### 9.2 Isolation architecture — layered ownership

**[INVARIANT] Tenancy correctness is owned by the application layer, not the
database.** The mandatory layers below must be *independently sufficient*;
anything below them is hardening only.

```text
MANDATORY (must hold even with every hardening layer below disabled):
  1. Authentication — every request resolves identity before any operation.
  2. Server-side tenant membership resolution — TenantId comes ONLY from
     authenticated membership, on every request.
  3. Authorization — RBAC + policy + resource-based, re-evaluated server-side.
  4. Tenant-aware application/data-access infrastructure — every tenant-scoped
     query is filtered by TenantId (and independently by soft-delete, as two
     separable filters so a maintenance job can lift soft-delete without ever
     lifting the tenant filter); every write is stamped with the caller's
     server-resolved TenantId.
  5. Replication scope filtering — every replicated read is tenant-filtered;
     every replicated write is server-stamped with the caller's TenantId.
  6. File authorization — Valet-Key, tenant-checked (§10.8, §57).

HARDENING (defense-in-depth; may be disabled without breaking tenancy):
  7. SQL Server Row-Level Security (RLS) — a secondary in-database enforcement,
     gated by an explicit go/no-go validation (§19 Gate J) before it is ever
     relied upon even as hardening.
  8. Tenant-safe caching (§10.7).
```

**[INVARIANT] Client-supplied `TenantId` is never trusted.** It is resolved
only from authenticated membership, on every request, server-side.

**[INVARIANT — no raw data access outside tenant-aware infrastructure.]** A
global query filter is a *convenience* layer, not the security boundary, and is
bypassable by raw SQL, bulk update, stored procedures, report queries,
maintenance jobs, cache reads and file access. Isolation is therefore enforced
by making **mandatory layers 1–3 independently sufficient** AND forbidding any
code path that reaches tenant data outside approved tenant-aware
infrastructure. Every bypass vector is either routed through tenant-aware
infrastructure or is an explicitly reviewed, audited exception (§20) — never an
unmonitored escape hatch (§19 Gate E/J).

**[INVARIANT — the resolved tenant context is immutable for the request.]**
`TenantId` is captured once at the request boundary into an **immutable**
`ResolvedTenantContext`; no downstream layer may mutate it. This is enforced
across async continuations, background jobs, worker claims, outbox handlers and
scheduled jobs — each inherits the same immutable context, never re-derives or
re-trusts a tenant from a lower layer.

**[INVARIANT — required-navigation pitfall]** A *required* relationship
navigation into a tenant-filtered entity must never silently drop parent rows
because the filtered child disappeared. Sensitive navigations are modeled as
optional, or with matching filters on both sides; the data-access filter is
treated as convenience/defense — never the security boundary itself (layer 3).

### 9.3 Tenant resolution & propagation

```text
Credential
  ↓
Authenticated principal (Identity context)
  ↓
Membership (Tenant/Clinic context)
  ↓
ResolvedTenantContext (immutable for the request)
  ↓
every downstream call (Domain, Infrastructure, replication data view,
RLS SESSION_CONTEXT if enabled) receives the SAME resolved TenantId —
never re-derived, never re-trusted from any lower layer
```

### 9.4 RLS hardening rule (if enabled)

Microsoft documents RLS with `SESSION_CONTEXT` for middle-tier applications
that share a SQL account, including filter and block predicates. If RLS is
enabled, for each database connection the application sets, from **server-
derived** values:

```text
SESSION_CONTEXT(TenantId)
SESSION_CONTEXT(RequestId)
SESSION_CONTEXT(UserId)
```

**[INVARIANT] Tenant isolation is validated with RLS completely disabled**
(§19 Gate J) — if isolation would fail without it, the mandatory layers were
not actually sufficient and RLS was masking a defect rather than adding depth.
RLS must additionally be tested under connection pooling and parallel plans
(a pooled connection must never inherit a prior request's `SESSION_CONTEXT`;
a parallel-plan read must never return another tenant's rows). If that cannot
be reliably proven, RLS stays **off** and the mandatory layers carry isolation
alone.

### 9.5 Cross-tenant access

A request such as `Tenant A user → Patient in Tenant B` must **fail before
returning the row**, at a mandatory layer. Failures must not differ in a way
that discloses whether a protected record exists (no existence oracle).


---

## 10. Security Architecture

### 10.1 Three independent planes

**[INVARIANT — never collapsed]**

```text
Authentication  = WHO is the caller?             (Identity context)
Authorization   = WHAT may this caller do?        (RBAC + policy + resource)
Entitlement     = does THIS DEPLOYMENT license    (Subscription/Licensing, §13)
                   feature/plan X?
```

A valid license never authenticates a user. A logged-in user is never
automatically entitled to a paid feature. An authorized action never implies
the deployment is licensed for it. Fixed order on every request:

```text
Authenticate → Resolve tenant/membership → Authorize resource/action →
Check entitlement → Validate request → Execute
```

No endpoint infers tenant from a URL, body, query string, or local client
state alone (§99).

### 10.2 Authentication architecture

```text
Web client       → cookie-based session (browser-appropriate; server issues/
                    validates; no auth logic in client script)
Desktop client    → bearer + refresh token pair, stored in OS-protected
                    credential storage; refresh requires server reachability
                    (see §10.4 for what covers the gap when it is NOT reachable)
```

**[INVARIANT] There is exactly one authentication mechanism family, used
consistently.** No bespoke token protocol is invented; no second parallel auth
path is added "just for desktop."

**[DECISION — desktop auth is the stack-approved local token mechanism, not a
generic OIDC provider.]** The desktop's bearer+refresh pair is issued and
validated by Animora's own local identity subsystem and stored in OS secure
storage, with refresh-token rotation and device-bound keys where supported
(§10.4). It is **not** described as a generic OAuth/OIDC authorization server;
if a real external OIDC server is ever introduced, that is a new architecture
boundary and a new ADR. A public native client never ships a static client
secret.

**[INVARIANT — browser cookie auth is incomplete without anti-forgery.]**
Because a browser attaches the session cookie automatically, **every
state-changing (unsafe) browser request requires anti-forgery (CSRF)
protection** — an architecture-level requirement, not optional hardening.
Cookies are issued `Secure` + `HttpOnly` + `SameSite`, with Origin/Fetch-
metadata checks where appropriate and strict CORS. **CORS is not a substitute
for CSRF protection.** The desktop's bearer path is not cookie-ambient and so
is not CSRF-exposed the same way, but it has its own rules (§10.4). This is a
dedicated gate (§19 Gate W-csrf).

### 10.3 Authorization architecture

**[PATTERN] RBAC + Policy-based + Resource-based authorization, layered.** Role
gates the coarse capability ("cashier," "doctor," "manager"); a policy handler
evaluates fine-grained rules (amount thresholds); a resource-based check
evaluates the *specific* resource instance ("own tenant's invoice, but not
another cashier's till"). Example:

```text
Role     = Cashier
Policy   = refund below threshold
Resource = this specific cash session
```

**[INVARIANT] RBAC alone is never sufficient for a financial operation.** Every
tenant-scoped, row-level authorization decision passes through the same
three-layer check as §9 layer 3 — evaluated before any query is composed, never
patched on after the fact. The client may hide an operation for UX, but the
**server always evaluates authorization** and is the sole authority (§7.1).

### 10.4 Offline-session architecture

**[INVARIANT] Bearer/refresh tokens are not, by themselves, an offline
authentication mechanism** — refreshing requires server reachability. Offline
continuity is governed by an explicit, separate, device-bound session policy: a
signed, device-bound **capability snapshot** cached locally with an explicit
expiry, distinct from both the online token and the entitlement license:

```text
CapabilitySnapshot
  Tenant · User · Device · Roles · Policies · CapabilityVersion ·
  IssuedAt · ExpiresAt · ServerPolicyVersion · Signature
```

It answers "what may this device act as, until when, without contacting the
server." Per capability (not one blanket number) it declares:

```text
MaximumOfflineDuration   → per-capability, threat-model-derived
SensitiveOperationPolicy → refund/period-close get short or ZERO offline windows;
                            low-risk reads get longer ones
ExpiryBehavior           → FAILS CLOSED: read-only / locked / sensitive-ops
                            blocked — never a silent extension of authority
Re-authenticationReq.    → deterministic re-auth + capability refresh +
                            pending-command reconciliation, in that order
```

Recommended baseline: low-risk reads = long window; routine record editing =
normal; cash/payment = shorter; refund = short or zero; period close = zero
offline authorization; license-sensitive operations = explicit license policy.

**[INVARIANT — offline revocation is a next-contact guarantee, not an instant
one.]** A genuinely disconnected device cannot be told anything; the server has
no channel to it. Therefore revocation (device revoked, role changed,
password/security-stamp changed, employee terminated, subscription cancelled)
**takes effect immediately online and at the next trusted contact offline** —
never retroactively on a device that is still offline. The *only* bound on a
revoked-but-still-offline device is the capability snapshot's own expiry plus
local device protection. This is an inherent property of offline-first, a
stated trade-off in the customer risk model, not a defect, and never claimed as
"instant revoke." The architecture controls the *size* of this exposure by
keeping sensitive capabilities' offline windows short or zero, not by
pretending an offline device can be reached.

### 10.5 Transport & local PKI architecture

```text
Animora OFFLINE Root CA
  (root private key is NEVER on the production server and NEVER in a normal
   server backup; kept offline by the deployment authority)
    → issues → Issuing CA
        → issues → Server certificate (LAN hostname / internal DNS)
                    — this, and only this, lives on the running server
    → trust anchor (root public cert) → provisioned into the client OS/browser
        trust store by the deployment process — installer / MDM / policy —
        NEVER by application JavaScript or in-app code
```

**[INVARIANT — root key is not a runtime or backup artifact.]** The Root CA
private key lives **offline**, outside the production server and outside the
routine full-server backup set. Only the issued server certificate (and its own
private key) sits on the running node. If the root CA key were inside the §17.3
full-server backup, anyone who obtained a backup could mint trusted certificates
and MITM every client. An offline root CA with an online issuing CA is the
standard hardening shape (ADR-22). The §17.3 recovery matrix accordingly backs
up the *server certificate and runtime key material*, **not** the root CA key.

**[INVARIANT] A browser cannot be made to trust a private CA from inside a web
application.** Trust provisioning is a deployment-time concern, not a runtime
one.

**[INVARIANT — offline revocation checking only.]** Certificate chain
validation at runtime uses **offline revocation mode** (a locally served CRL,
or short-lived certificates with controlled renewal). It must never reach an
external OCSP/CRL endpoint, since that would be a hidden Internet dependency
and violate R11 (§17.4).

**[DECISION] Transport security is an OS-matrix-compatible protocol policy,
never a single hard-coded protocol version.** Obsolete protocols are disabled;
the newest mutually-supported protocol is preferred; an interoperability floor
is kept for the oldest officially supported client tier. The OS negotiates the
exact version — hard-coding one would silently exclude a supported client tier.

### 10.6 Server key-material architecture

**[INVARIANT] Stateful server cryptographic key material** (protecting cookie
authentication, anti-forgery tokens, and other protected payloads — the
ASP.NET Core Data Protection key ring) **is a first-class, persisted,
backed-up artifact — never left to default, per-instance, ephemeral storage.**
Losing it silently invalidates every active session and every protected
payload. It is part of the disaster Recovery Matrix (§17.3), not a footnote.
The key ring must have a stable `ApplicationName`, a persistent key-ring
location, protection-at-rest and an ACL, and must survive restart and restore
(§64).

**[INVARIANT — signing keys are not runtime-server residents.]** Keys that
*mint* trust — the Root CA private key (§10.5) and the **license-signing
private key** — never live on the running clinic-serving server. The runtime
server holds only **public verification keys** (to verify licenses and the cert
chain) and its own issued server certificate. A compromised runtime node must
not be able to issue valid licenses or trusted certificates. The license issuer
is a separate, protected, offline signing authority (part of the vendor's
management plane, §3), not a runtime component.

**[DECISION — key register as a first-class artifact.]** Because a single node
co-locates app/DB/files/CA, every distinct key is enumerated with its owner,
purpose, generation site, storage, who may use it, exportability, rotation,
revocation, backup and compromise procedure — TLS/CA keys, server certificate,
Data-Protection key ring, license-signing key (offline), offline-capability
signing key, file-access-grant signing key, device identity keys, and DB/backup
encryption keys. "A single node" is a *compute* decision; it is **not** a single
trust domain — trust-minting keys stay outside the runtime (ADR-22/ADR-23).

### 10.7 Tenant-safe caching architecture

**[INVARIANT] Every cache key for tenant/clinical/financial output must include,
at minimum: tenant scope, user/permission scope, resource identity, and a
query/contract version** — and for permission-sensitive data, a permission
fingerprint. **[PATTERN] Cache-Aside**, applied only with this key composition.
A cache mix-up (one tenant receiving another tenant's cached result) is a
security defect, not a performance bug (§19 Gate L). **Caching is applied only
where measurement justifies it, and never before authentication/authorization**
— output caching must run *after* authZ so a cached authorized result can never
be served to the wrong user (§59).

### 10.8 File access architecture

**[PATTERN] Valet Key (Azure Architecture Center).** A client never receives or
uses a raw filesystem path/URL as an access credential. Every file read/write:

```text
FileId → application authorization check → a signed, short-lived access grant
issued by the server → the grant (not the path) is what the client holds
```

**[INVARIANT] Path obscurity is never authorization**, even on a trusted LAN. A
file reference that crosses the trust boundary is a logical `FileId`, never
`C:\...`, `\\server\...`, or `https://internal-path/...` (§93). This holds
identically for desktop and web clients (§14/§56–§57 cover the file lifecycle
this access model sits on top of).

### 10.9 Threat-model summary (STRIDE-lite, architectural mitigations)

| Threat category | Primary architectural mitigation |
|---|---|
| Spoofing (identity/tenant) | Authentication plane (§10.2) + device identity + server-resolved TenantId only (§9.2) |
| Tampering (payload/command replay) | TLS, signatures, command fingerprint + idempotency subsystem (§8.6.1) |
| Repudiation | Append-only audit context (§6, §61), immutable ledger (§11.1) |
| Information disclosure (cross-tenant) | Layered isolation (§9.2) + tenant-safe caching (§10.7) + Valet-Key file access (§10.8) + encrypted local store (§92) |
| Denial of service (weak server) | Bulkhead/throttling/budgets on background work (§12.3, §43, §71), delta-only sync (§8), static-first rendering (§7.3), bounded retries (§42) |
| Elevation of privilege | RBAC + policy + resource-based authZ (§10.3), entitlement as an independent plane (§10.1) |

Required security tests (§111): tenant-ID manipulation, IDOR, cross-tenant
sync, stolen device, expired capability, replayed command, same-ID/different-
fingerprint, malformed patch, oversized payload, file-path traversal, file-
grant forgery, CSRF, session fixation, token replay, cache confusion, SQL
injection, OData abuse on the Datasync boundary.

---

## 11. Domain & Data Architecture

### 11.0 Consistency classes

**[INVARIANT] Every state in the system belongs to exactly one class, and a
lower class is never silently used as a higher one:**

```text
C1 — Local Provisional          can exist offline (LocallyCommitted,
                                PendingSettlement)
C2 — Server Transactional       globally authoritative at commit (PaymentSettled,
                                JournalPosted, ResourceAllocated)
C3 — Eventually Consistent      derived from authoritative state (DashboardSummary,
     Projection                 LowStockView, ReportingProjection)
C4 — Notification Hint          NOT state ("Tenant 42 changed")
```

No implementation may silently use C4 as C2, or render C1 as C2.

### 11.1 Accounting architecture — append-only ledger

**[PATTERN] Append-only ledger (event-sourcing-lite for financial facts).**
Posted financial facts are immutable forever. Corrections are new, compensating
entries carrying a reason and an audit trail — never an in-place mutation of a
posted row. This is a direct consequence of §8.14's conflict policy and §2
principle 13 (fail closed): a ledger that can be silently edited cannot be
trusted after any conflict, retry, or crash.

**[INVARIANT — append-only alone is not an accounting model.]** A real general
ledger carries these architectural invariants:

```text
Double-entry balance     — every posted journal: Σ debits = Σ credits
No posted mutation       — a posted entry is never edited; only reversed
Posting period is open   — a posting targets an OPEN FiscalPeriod (below)
Server posting date      — the accounting date is server-governed, NOT an
                           arbitrary client timestamp (A-RULE 036)
Currency + versioned rate— currency explicit; exchange rate versioned
Tax + rounding snapshot  — tax calc and rounding policy retained at posting
Immutable dimensions     — account dimensions fixed for a posted entry
Source document ref      — every posting references its source document
Reversal references orig — a reversal points back at the original posting
```

Entities carried: `Journal · JournalLine · PostingBatch · Account ·
FiscalPeriod · AccountingDate · DocumentDate · Currency · ExchangeRate · Tax ·
Dimensions · ReversalReference · SourceDocument · PostingUser · PostingTimestamp
· OperationReference`. The precise chart-of-accounts and tax rules are an ADR
(ADR-21). Finance detail is §35.

**[DECISION — period close is a state machine, not a boolean.]**

```text
Open → SoftClose → Review → Closed → (Authorized Reopen / Correction only)
```

**[INVARIANT — offline commands vs. a closed period have a declared policy.]**
A device offline when a period closes may reconnect holding commands dated into
that now-closed period. This collision is **resolved by one stated, auditable
policy** — reject / post into the current open period / allow authorized
back-posting / hold for an accountant / create an adjustment — never silently
posted into a closed period and never silently dropped (§19 Gate S).

### 11.2 Inventory architecture — movement-based stock

**[PATTERN] Movement-based state (event-sourcing-lite for stock).**
Quantity-on-hand is never stored and overwritten as an absolute number by an
offline client. It is **derived** from an immutable sequence of movements
(Receipt / Sale / Consumption / Loss / Adjustment / Transfer / Return). This
makes the *arithmetic* safe by construction: two offline devices recording
different movements never lose an edit, whereas overwriting an absolute quantity
would silently drop one.

**[INVARIANT — movement convergence is arithmetic, not business, correctness.]**
Movement-based stock guarantees independent movements *sum* without losing a
write. It does **not** guarantee the total is a *permitted business outcome*.
With server stock = 1, two offline devices each selling 1 both record valid
movements that sum to −1 on hand. Whether that is allowed is a **policy decision
the architecture must state, not something convergence decides.** Inventory
defines, explicitly, per product class:

```text
Negative-on-hand policy   — allowed / blocked / allowed-with-alert
Reservation model         — does an offline sale reserve stock; how is an
                             over-sell reconciled at settlement
Lot / batch / expiry      — tracked where clinically required (vaccines, cold-chain)
Serial                    — tracked where individually identifiable
Location / warehouse      — multi-location stock, transfers
Cost layer / valuation    — the basis COGS and finance post against
```

Settlement (§8.6) re-validates the chosen policy server-side; an offline
movement that would breach it is routed to conflict policy (§8.14), never
silently accepted just because the arithmetic converged. Values are an ADR
(ADR-19). Inventory detail is §36.

### 11.3 Read models vs. write models (CQRS-lite)

**[PATTERN] CQRS-lite** — command/query separation **without** necessarily
separate storage at launch. Client-side read models (desktop grids, web lists)
may be denormalized, aggregated, or cached for UX/performance. The
**authoritative write model stays normalized and server-side** for Finance and
Inventory specifically. A desktop grid never mirrors the server's aggregate
structure and is never a source of truth for a write decision.

**[DEFERRED]** A physically separate reporting/read-model database is an
explicit future escalation (§16.2), not a launch requirement — the architecture
keeps the seam (read-only reporting queries clearly separated from the
transactional path) so escalation is additive, not a rewrite. Launch uses OLTP
+ incremental read projections (§58).

### 11.4 Concurrency architecture

**[PATTERN] Optimistic concurrency via an opaque version token (`rowversion`)**
on every mutable entity. A stale write is *detected*, never silently accepted;
it is routed into the conflict-resolution pipeline (§8.12) — the version token
*detects* staleness, it never *decides* the correct business outcome.

**[INVARIANT — version tokens are sufficient for edits, NOT for contended
allocation.]** An opaque version token correctly guards "did this row change
under me" edits (patient demographics, invoice drafts, notes). It is **not**
sufficient for business invariants about a *shared resource* two callers may
claim from different base versions — two clients booking the same room/slot, two
tills claiming the same cash drawer, or seat/period/device-registration
allocation. Both can read a non-conflicting version and still both write,
producing a double-book no version check catches.

Such invariants are enforced as a **server-side resource-allocation model inside
a single transaction** (§37), not by optimistic concurrency alone — using, per
case, a uniqueness constraint, a serializable transaction, an
application/resource lock, or an explicit slot-allocation table. Scheduling owns
a **Resource Allocation Model** (rooms, staff, boarding capacity as allocatable
resources), not merely a versioned appointment row. The mechanism per invariant
is an ADR (ADR-20); the guarantee is a fitness gate (§19 Gate T).

### 11.5 Reporting/aggregation isolation

**[INVARIANT] Heavy reporting/aggregation load is an architecturally separate
budget from OLTP/transactional load** — it must never ride the transaction
path. Every heavy report carries its own timeout, cancellation and concurrency
budget, independent of the write path (§43, §58).


---

## 12. Reliability & Eventing Architecture

### 12.1 Transactional Outbox

**[PATTERN] Transactional Outbox (Azure Architecture Center).** A domain event
and the business state change that produced it commit in **one transaction** —
atomic, no lost event, no dual-write:

```text
BEGIN
  business state
  domain event / outbox record
  audit (same transaction where required for financial/security audit)
COMMIT
```

A background relay later dispatches at-least-once.

**[INVARIANT] "Dispatched" never means "delivered exactly once."** Outbox
delivery is at-least-once by construction; every downstream consumer of an
outbox-relayed effect must be an **Idempotent Consumer** (§8.6.1's pattern,
reused) keyed on a stable identifier.

**Mandatory relay state machine — the atomic claim is not optional, and a
terminal failure path is required:**

```text
Pending → Claimed (atomic conditional claim, LeaseExpiry) → Processing
   → Succeeded
   → RetryScheduled (NextAttemptAt backoff)  ──loops back to Pending
   → DeadLetter   (retry budget exhausted / poison message)
   → Cancelled    (effect no longer required)
```

Every outbox record carries: `LeaseExpiry · AttemptCount · NextAttemptAt ·
FirstFailedAt · LastFailedAt · LastException · DeadLetterReason`.

A naive "select unprocessed → process → mark done" **will** double-dispatch the
moment a second relay worker exists; the atomic claim (with a `LeaseExpiry` so a
crashed worker's claim is reclaimable) makes the database the single arbiter
between competing workers. A permanently failing (poison) effect must reach
**DeadLetter** with a reason and surface to an operator — never retried forever
and never silently dropped (§19 Gate AD, §106).

### 12.2 Background worker architecture

**[PATTERN] Competing Consumers (Azure Architecture Center).** Multiple worker
instances may safely compete for the same unit of work because the claim
protocol (§12.1) is atomic and every effect is idempotent. Every worker step is
**resumable after a crash mid-step** — the architecture assumes a worker can die
at any point and re-run without duplicating an effect.

Launch uses an in-process background-service host with bounded queues/
concurrency and suggested priority classes:

```text
P0 Interactive / settlement
P1 State synchronization (Change-Tracking reader)
P2 Outbox / notifications
P3 Reporting / maintenance
```

**[INVARIANT — Competing Consumers is for independent work; ordered work
carries an ordering key.]** Competing Consumers gives throughput for
*independent* effects but makes **no** cross-item ordering promise. Any effect
that must apply in order relative to a sibling is grouped by an explicit
**ordering key** (e.g. `(TenantId, AggregateId)`, `(TenantId, ResourceId)`,
`(TenantId, CashSessionId)`) so items in the same group serialize while
different groups still run in parallel. Each work type declares: concurrency
class · ordering key · retry policy · dead-letter policy. **Do not globally
serialize an entire tenant** if independent aggregates can safely proceed in
parallel (§102). Ordering between the two queues (§8.9) is still governed by
settlement dependency, never by worker scheduling.

### 12.3 Resilience architecture

**[PATTERN] Retry + Circuit Breaker + Timeout + Bulkhead (transient-fault
handling, Azure Architecture Center), applied at every network-facing
boundary:**

```text
Sync push/pull (client ↔ Sync Surface)      → retry with backoff; circuit
                                                breaker opens on sustained
                                                server unavailability, so a
                                                client degrades to "working
                                                offline" instead of hammering
                                                a struggling server
Business command submission (client ↔ API)  → same; a command NEVER retries by
                                                silently generating a new
                                                OperationId — retries reuse the
                                                original OperationId (idempotency)
Outbox relay dispatch                        → retry with bounded backoff via
                                                NextAttemptAt, then DeadLetter.
                                                A circuit breaker guards ONLY a
                                                genuinely REMOTE downstream
                                                effect; for in-process/local
                                                outbox effects at launch, Retry +
                                                Backoff + DeadLetter + operator
                                                remediation are the right tools
Background workers vs. request threads       → Bulkhead: worker resource usage
                                                is isolated so a background
                                                backlog can never starve
                                                interactive request handling
```

**[INVARIANT — a Circuit Breaker guards remote dependencies only.]** The
breaker is applied at the client↔server boundary (a real remote dependency), not
on local in-process outbox effects at launch, where it would add complexity
without the failure it prevents.

**[INVARIANT — retry is bounded by a budget, not just backoff.]** Every
retrying boundary declares a **retry budget**: max attempts, max elapsed time,
jitter distribution, and per-tenant + global reconnect limits — so a mass
reconnect cannot become a self-inflicted thundering herd (§19 Gate I, §42). A
connectivity outage makes a client transition to offline mode rather than
create a retry storm. **No infinite retry.**

**[PATTERN] Throttling**, applied to reconnect specifically: sync resumption
after startup/reconnect uses **random initial jitter + adaptive/exponential
backoff + progressive catch-up + per-tenant & global concurrency limits + server
overload signals**, never a deterministic fixed interval — which would create a
synchronized "reconnect storm" the moment many clients return together
(§19 Gate I, §88).

### 12.4 Bulkhead / backpressure budgets

**[INVARIANT] Separate resource budgets** for interactive requests, sync, file
transfer, reports, maintenance and outbox. If report work becomes excessive,
*report throughput decreases* — **checkout never stops** (§43, §71).

### 12.5 Graceful degradation & failure semantics

**[INVARIANT] Every overload or failure path has one defined outcome; there is
no undefined "it just gets slow/weird" state.** Exceeding a resource budget
results in queuing, shedding to a cheaper rendering path, or disconnecting idle
sessions — deterministically and observably (§15) — never silent resource
exhaustion. Representative defined outcomes (§107):

```text
Database unavailable   → online mutations fail fast; desktop continues offline
                         where policy permits
Sync unavailable       → desktop remains locally usable; pending mutations stay durable
Authorization changed  → capability refresh + scope update + controlled resync as needed
Cursor expired         → full shadow resync (§50)
File store unavailable → file op becomes Pending/Unavailable; metadata never
                         falsely says Available
```

---

## 13. Subscription & Licensing Architecture

### 13.1 Context relationship

```text
Public Website (separate vendor-side system, §3, §17)
        │  (async, versioned, non-runtime-critical onboarding contract, §13.4:
        │   customer register + plan purchase → provisioning intent)
        ▼
Subscription context  ── owns plans/seats/billing periods/grace states
        │  (self-service onboarding intake + operator oversight share one
        │   create-tenant+subscription operation — §13.3/§13.4)
        │  publishes PlanChanged, SeatLimitReached; on provisioning,
        │  triggers a signed license artifact
        ▼
Licensing context      ── verifies the signed license OFFLINE, binds it to
                            THIS installation, enforces expiry/rotation
        │  publishes LicenseValidated, LicenseExpiring
        ▼
Core application       ── reads entitlement as READ-ONLY state; never
                            self-grants; UI toggles features from this state only
```

**[INVARIANT] Subscription (the commercial billing model) and Licensing (the
offline, signed, deployment entitlement) are never the same mechanism.** The
runtime depends only on Licensing; Subscription may be fed by self-service
website onboarding (§13.4, the primary commercial path) **or** by a manual
operator action (§13.3) — either way, how the running deployment checks its own
entitlement does not change.

### 13.2 Offline license verification flow

```text
On startup / periodically:
  1. Load signed license artifact
  2. Verify signature against a locally shipped, rotation-aware key set
     (KeyId selects the verifying key)
  3. Verify the license is bound to THIS installation's PROTECTED device
     identity (a key pair generated on-device at install time whose private
     key is non-exportable where the platform supports it — TPM-backed on
     Windows, Secure Enclave on macOS; the license is bound to the PUBLIC key,
     so a byte-for-byte copied license fails on a second machine that cannot
     reproduce the private key)
  4. Verify anti-replay fields (LicenseId, IssueTime, audience/product)
  5. Verify expiry against TRUSTED TIME, not a single clock source:
       • server-issued trusted time persisted as a high-water WALL time, plus
       • monotonic elapsed-time evidence for drift while running, plus
       • rollback detection
     (a monotonic uptime counter alone measures time-since-boot, not a
      calendar — it cannot by itself prove "not past 2027-01-01")
  6. Result: Valid → entitlement snapshot updates
             Clock rollback / anomaly → sensitive offline operations disabled,
             local evidence preserved, recovery requires server contact
             Invalid/Expired → deployment fails closed for gated capabilities
             per policy, never silently continues fully entitled
```

**[INVARIANT — anti-cloning]** A signature proves authenticity/integrity; it
never proves non-cloning by itself. Anti-cloning rests on the **protected device
identity** (step 3): a hardware-backed, non-exportable private key where the
platform provides one, with the license bound to its public key. A software-only
machine fingerprint plus an encrypted local secret is **explicitly rejected** as
anti-cloning proof — both the disk and the secret can be copied together
(A-RULE 030). PKI/key-custody and device-identity details are an ADR
(ADR-22/ADR-23).

**[INVARIANT — no online revocation channel]** Because there is no mandatory
online license server (R11), revocation is **architecturally approximated** by
short validity windows + reissuance — a stated design property, never hidden
behind a false "instant revoke" claim.

### 13.3 Platform-operator oversight architecture

The **Platform Operator** (§3) views/manages subscriptions across tenants
through an **operator-facing surface of the Subscription context** —
architecturally just another authenticated, authorized client of that one
bounded context, with an operator-scoped role instead of a tenant-scoped one. It
is not a separate backend, and it never reaches into other bounded contexts
directly (it sees Tenant/Clinic identity only insofar as Subscription needs to
reference which tenant a plan belongs to).

### 13.4 Self-service onboarding contract (register-and-buy → provisioning)

The product's customer journey begins on the public website (§3, §17):
**register → choose a plan → pay → download the app**. That website is an
independent vendor-side system; payment collection, tax and invoicing happen
there, not inside Animora (the clinic-serving system holds no payment gateway).
What the website hands to Animora is an **onboarding intent**, never a database
write:

```text
Public Website  ──(async, out-of-band, versioned onboarding contract)──▶
  Subscription context (ONBOARDING INTAKE):
    { requested tenant/clinic identity, plan code, paid billing period,
      external payment reference, idempotency key }
        │
        ▼
  Subscription context provisions a tenant + a Trial/Active subscription
  (the SAME operation the Platform Operator can perform by hand — §13.3),
  idempotently keyed so a retried webhook never double-provisions
        │
        ▼
  Licensing context issues the signed, installation-bindable license (§13.2)
        │
        ▼
  Customer downloads + installs the desktop app, logs in with the credentials
  established during onboarding, and the app binds to this installation
```

**[INVARIANT — onboarding is a MANAGEMENT-plane transport, defined explicitly.]**
R11 forbids external Internet dependencies on the **runtime (data) plane** — the
clinic-serving path. It does **not** forbid a controlled **management plane**; it
requires the two be separated and named (§13.4a). Provisioning crosses the
management plane via one of two concrete transports, and the architecture commits
to at least one being available:

```text
(a) Signed provisioning package          (b) Vendor provisioning queue
    Website → signed, versioned,              Website → vendor-side queue →
    idempotent package → operator/            a CONTROLLED management
    admin workstation → delivered             connection (not the clinic
    into the server via controlled            runtime path) drains it into
    transfer (e.g. USB / internal             the Subscription context
    management link) → Subscription
```

Either way the intake is an asynchronous, idempotent, versioned contract on the
Subscription context — the same create-tenant + create-subscription operation
the operator surface exposes (§13.3), reached by the website's service identity
instead of a human operator. The clinic *runtime* path never calls the website
and the website never calls a clinic-serving endpoint, so R8 and R11 both hold.

**[INVARIANT — the onboarding message is a signed security contract.]** It
carries, at minimum: issuer identity, audience, contract version, idempotency
key, timestamp, expiration, a signature (verified before any provisioning), an
external payment reference, and a correlation id — with replay protection. Its
entitlement lifecycle is explicit:

```text
Paid → Provisioned → Active → RenewalPending → Renewed → Grace
                                            → Suspended → Cancelled
```

Refund / chargeback / payment dispute each have a defined effect on entitlement
— never an undefined state.

**[DECISION] Self-service and operator-driven provisioning share one code
path.** There is exactly one "create tenant + subscription" operation; the
website's automated onboarding and the operator's manual action are two
authenticated callers of it, not two implementations. No second provisioning
engine is ever built.

### 13.4a Runtime plane vs. management plane (what R11 does and does not forbid)

**[INVARIANT — R11 is a *runtime-plane* constraint, stated per plane.]**

```text
RUNTIME (DATA) PLANE  — clinic operation: login, sync, commands, licensing
                         verification, data access, local observability
                         → external Internet FORBIDDEN (R11, §17.4)

MANAGEMENT / CONTROL  — provisioning, subscription renewal, license ISSUANCE,
PLANE                    software update, backup export, vendor support
                         → MAY require controlled connectivity OR a signed
                           offline bundle; NEVER on the clinic runtime path
```

Any capability that needs connectivity (onboarding §13.4, license issuance
§10.6, updates §18) lives on the management plane and is reached out-of-band;
nothing on the management plane is a synchronous dependency of a running clinic.
The production clinic runtime remains functional with the website completely
unavailable (§66).

---

## 14. Files & Storage Architecture

### 14.1 Two separate consistency domains

**[INVARIANT] A file's SQL metadata row and its binary bytes are two separate
consistency domains** — not committed by a single transaction, so an explicit,
ordered lifecycle is mandatory. Nothing is usable until the lifecycle completes:

```text
Staged → Uploaded → HashVerified → Committed → Available
```

`HashVerified` gates `Committed`; a hash mismatch fails the upload and can never
reach `Available` (§56).

### 14.2 Reconciliation architecture

**[INVARIANT] Reconciliation is a mandatory background job, not a hope.** The
two domains can diverge on crash/retry/restore; the architecture explicitly
detects and deterministically resolves every one of:

```text
DB row without a file          duplicate upload
File without a DB row          interrupted finalization
Incomplete upload              failed restore (DB/file out of step)
```

Each divergence has exactly one deterministic outcome: quarantine, re-verify,
garbage-collect the orphan, or resume — never silent drift (§19 Gate R).

### 14.3 Transfer protocol architecture

**[INVARIANT] File bytes never travel through the generic record-replication
pipeline (§8).** A **separate, resumable transfer protocol** drives the lifecycle:

```text
BeginUpload → UploadChunk → Resume → Complete → VerifyHash → Commit
```

Transfers are **idempotent, resumable, hash-verified, cancelable, auditable** —
first-class capabilities, not retrofits.

### 14.4 File identity & access

**[DECISION] Prefer immutable, content-addressed objects.** Replacing a file
creates a **new object + new metadata version** rather than overwriting old
bytes (§56.2). File access always uses the **Valet Key** pattern (§10.8): this
section defines the storage/lifecycle *behind* that access model; §10.8 defines
how a client is ever allowed to reach a file at all.

---

## 15. Observability Architecture

**[DECISION] Zero *external* telemetry is a product constraint; zero
*observability* is not acceptable.** Every observability capability below must
function with the Internet cable physically disconnected.

**[INVARIANT] Every cross-boundary operation carries correlation identifiers:**

```text
RequestId · CorrelationId · TenantId · UserId · DeviceId · OperationId ·
MutationId · AggregateId (where applicable)
```

so any one transaction can be traced end-to-end across client → server → worker.
An agent adding a new cross-boundary call must thread the same correlation
context through it.

```text
Structured logs                 — local, PII-minimized, correlated by the IDs above
Health / readiness               — separate Liveness · Readiness · Dependency ·
                                   Sync · Database · File-store health, answered
                                   locally, pollable without any external service
Diagnostics bundle                — one local operation exports: app version,
                                   schema version, sync state, last checkpoint,
                                   pending-mutation metadata, recent conflicts,
                                   recent errors, health state, sanitized config —
                                   NO secrets — for support without cloud access
Audit trail                       — append-only, every security/finance action
                                   (fed by the Audit bounded context, §6, §61)
Sync diagnostics                  — per-entity cursor/backlog/resync state:
                                   "is my data actually synced?"
Command-settlement diagnostics    — per-command lifecycle/settlement state:
                                   "did this payment actually settle?"
Local metrics                     — scrapable on the LAN only; removing the local
                                   collector must never break the app
```

**Minimum metrics (§82.2):** sync backlog · pending-mutation count · settlement
latency · conflict rate · full-resync count · cursor-expired count ·
notification latency · DB latency · query latency · outbox backlog · dead
letters · per-tenant resource usage.

**[INVARIANT] Reporting freshness is visible.** Every read projection used for
management reporting exposes `ProjectionGeneratedAt` and
`SourceVersion/SourceWatermark` so users can distinguish current transactional
truth from an eventual report projection (§83).


---

## 16. Performance & Scalability Architecture

### 16.1 Weak-server design principle

**[DECISION] Push work to the client wherever the client can safely do it
without weakening §0.2 (server as global authority).** Offline-capable clients
perform local reads/search/sort/form-validation/UI-computation/cached
dashboards/local filtering; the server is reserved for authoritative writes,
cross-user validation, global reports, shared accounting, authorization and
final settlement. This is the single largest reducer of server load and is why
§8's split (state replication vs. business intent) exists in the first place —
replication is cheap and delta-based; business settlement is the only work the
server must do unconditionally.

Server-side optimization order (§84): query shape → correct indexes →
projection → batch size → connection/DbContext lifetime → caching where safe →
background concurrency → compression → runtime-level optimization → additional
infrastructure. **Do not introduce a broker/cache/AOT just because it sounds
fast.** EF Core rules (§85): `AsNoTracking`, explicit projection, pagination,
short-lived pooled contexts, compiled/batched queries where measured; avoid
large object graphs, lazy-load loops, `SELECT *`, unbounded queries, N+1, long
transactions.

### 16.2 Scale-out architecture path (deferred, gated)

**[DEFERRED] A single vendor-operated node, single process, no backplane, no
message broker, no distributed cache is the launch architecture.** Each
scale-out step is a **documented deployment-time escalation**, gated by a
measured need — never introduced speculatively (§2 principle 19):

```text
Web app → 2+ instances     requires a session-affinity + real-time-channel
                            backplane (shared Data Protection keys + SignalR
                            backplane), added as a deployment switch (§73)
Noisy/large tenant          requires the Deployment Stamps escape hatch (§9.1,
                            §72) — move that tenant to its own database/stamp
Heavy reporting outgrows    requires a physically separate read-model/reporting
  the shared OLTP path         store (§11.3's deferred escalation)
Real broker needed           requires replacing the Outbox+relay boundary
  beyond outbox+relay           (§12.1) with an external broker behind the SAME
                                 boundary — business code does not change (§74)
```

**[INVARIANT] Any of the above requires an explicit ADR before implementation**
— none is introduced "just in case."

### 16.3 Capacity model methodology

**[INVARIANT] A supported concurrent-client/tenant count is never asserted from
hardware specifications alone — it is earned from measurement.** The benchmark
exercises the full path (application memory, DB CPU/memory, session/circuit
state, background-worker cost, filesystem/OS overhead, sync throughput,
command-settlement latency, reconnect behavior) against a defined target
hardware class, at a defined synthetic workload, before any capacity number is
published. The method (tenant tiers, metrics, mixed workload) is §110; the
hardware baseline is §69; the SQL edition decision is §70.

---

## 17. Deployment & Infrastructure Architecture

### 17.1 Deployment topology

```text
Vendor-operated server environment (no mandatory external connectivity):
  Clinic A desktop clients ──┐
  Clinic B desktop clients ──┤
  Web-browsing workstations  ├──▶ Single central server node, MULTI-TENANT
  (all tenants) ─────────────┘      (application + database + file store +
                                     local CA + local observability) — one
                                     deployment serves every subscribing clinic,
                                     isolated by tenant (§9)

Clinics: Desktop/Web → central vendor server.  No clinic operates a server.
```

The vendor runs **one** server node that serves **all** subscribing clinics as
tenants of a shared database (§9.1); clinics reach it over the network and **do
not run any server component themselves**. "Zero external-Internet dependency"
(R11) is a property of that vendor server environment: it must run fully even
with the Internet cable pulled (no cloud PaaS, no external IdP, no external
telemetry), using local PKI and local observability only.

At launch, application process, database and file store may co-reside on one
physical/virtual server — a packaging decision, not an architectural coupling:
the container boundaries in §4 remain distinct enough that any of them can move
to a separate machine later without a redesign. Scaling beyond one node is the
gated escape hatch in §9.1/§16, not a launch target.

### 17.2 Environment strategy

```text
Development   — full-feature, non-production, disposable data
Staging       — mirrors the production topology exactly (same single-node,
                 multi-tenant shape), used to rehearse upgrade/rollback and
                 tenant-isolation tests before they touch real tenants
Production    — the vendor's single central multi-tenant node serving all
                 subscribing clinics; tenant isolation (§9) is the boundary
                 between clinics, not separate per-clinic servers
```

### 17.3 Recovery architecture (server vs. client are different problems)

**[INVARIANT] Server backup and client recovery are architecturally different
problems and are never conflated.** The server backs up its full state
(database, file store, configuration, **runtime** cryptographic key material —
the server certificate + Data-Protection key ring, **not** the offline Root CA
or license-signing keys per §10.5/§10.6). A client holds *different* state (its
encrypted local replica, pending commands, device identity) that server backup
does **not** cover.

**[INVARIANT — RTO/RPO are declared numbers, backups are tested, copies are
isolated.]** The Availability SLO, **RTO** and **RPO** are explicit targets in
§1 (R-DR), not implicit — a single-node launch is a cost/product decision only
once those numbers are stated and met. There are at least **two backup copies,
≥1 off-host and immutable** (ransomware-resilient), and a restore is only
"recovered" once a **test restore has succeeded** — "a backup exists" is not
recovery. A **Recovery Point Manifest** ties a restore point together so DB,
file store and key material restore to one consistent point:

```text
BackupSetId · DatabaseBackupVersion · DatabaseRecoveryPoint ·
FileStoreSnapshotVersion/Watermark · KeySetVersion · LicenseKeyVersion ·
ServerBuildVersion · SchemaVersion
```

| Incident | Recovery source |
|---|---|
| Server database corruption | Database backup/restore |
| Server file-store corruption | File-store backup/restore |
| Server total loss | Full server backup set (DB + files + **runtime** key material + license *verification* material) restored to one Recovery Point Manifest; the offline Root CA / license-signing keys restored from their **separate offline custody** (§10.5/§10.6) |
| Client disk loss | Server-driven resync for replicated state + **explicit pending-command-recovery policy (below)** |
| Client device stolen | Device revocation (§10.4) — offline capability ends at next contact attempt; until then bounded only by snapshot expiry + local device protection |
| Corrupt client local replica | Quarantine the local replica; rebuild fresh via shadow resync (§50, §77) |
| Client offline past retention | Full resync, not incremental (§18, §50) |

**[INVARIANT — unsent commands and client disk loss, stated honestly.]** A
server-driven resync can only restore what the server already has. A
`PendingCommand`/mutation created offline that **never reached the server**
exists *only* on the lost disk, so resync cannot recover it. The architecture
picks one explicit policy and never leaves this ambiguous: either (a) unsent
mutations are protected by an **encrypted local backup/export** so they survive
disk loss, or (b) they are declared **unrecoverable** and the UI must warn the
user that locally-committed, not-yet-synchronized work is lost on disk loss
(§19 Gate U, §76). The system never tells users that "queued" means "stored on
the server."

**[INVARIANT — restore may move the server's change position backwards.]** A
restored server may have a Change Tracking version *lower* than a client
believes. A client must never assume the server's version moves monotonically
across a disaster restore. After recovery, `RecoveryPointId`, database identity,
schema version and sync-feed validity are checked; affected clients may be
forced into a controlled full resync if the restore point invalidates their
cursor — safer than faking continuity across an erased history window (§78).

### 17.4 Zero-Internet isolation as a tested architectural property

**[INVARIANT] "Zero external-Internet dependency" is a *tested* property, not a
claim.** The isolation boundary is validated by deliberately removing, one at a
time: DNS, general Internet reachability, external time sync, OCSP/CRL external
access, any CDN-style dependency, package/update feeds, telemetry backend, and
the public website — and confirming that **Login, Authorization, core reads,
Business commands, Critical sync, File access, Local observability, and
Licensing verification (per offline policy)** still function under every one of
these removed independently (§67). A certificate-validation path that silently
reaches an external revocation service is a hidden Internet dependency and a
defect.

---

## 18. Cross-Platform Consistency & Versioning Architecture

**[INVARIANT] The desktop installer's upgrade/rollback behavior and the
sync-contract versioning of §8.11 are two related but separate concerns.** An
installer upgrade/rollback test alone never substitutes for a mixed-fleet
compatibility test — a fleet is staggered by nature, and both must be validated
together (§19 Gate M).

**Server schema uses expand/contract discipline (§79):** expand → deploy
compatible server → deploy compatible clients → migrate usage → contract/remove.
Do not immediately remove columns or change semantics required by older
supported clients. The supported fleet is **N / N-1 / N-2**. A migration is
successful only if server DB + existing clients + offline replicas + pending
mutations all remain compatible across the supported window (§80).

**[DECISION] Desktop packaging format is a delivery preference, gated by
per-site constraints** (peripherals, elevated installers, enterprise policy),
never a hard architectural commitment — the architecture only requires that
whichever format is chosen supports **signed, verifiable, rollback-capable,
offline-installable** delivery on both target desktop OSes (macOS builds signed,
notarized and stapled before release), with **no runtime dependence on
Internet-hosted update feeds** (§113).

---

## 19. Architecture Validation Gates (fitness functions)

These gates prove the architecture, not just the technology (`STACK.md` lists
the paired technology-risk PoCs; these are the *behavioral* proofs the
architecture itself makes a promise about). A gate failure means the *pattern or
boundary* is wrong, not merely a bug.

| Gate | What it proves | Traces to |
|---|---|---|
| **A — Patient conflict** | Two offline devices editing the same non-financial record converge deterministically, with audit, no lost field | §8.12–§8.14 |
| **B — Independent financial commands** | Two offline devices, independent commands, both settle once, correct ledger | §8.6, §11.1 |
| **C — Duplicate command** | Same `OperationId` sent twice → exactly one financial effect | §8.6.1 |
| **D — Lost response** | Server commits, response lost, client retries same `OperationId` → prior result, no second transaction | §8.6.1, §8.6.3 |
| **E — Cross-tenant attack** | Tenant A requesting a Tenant B id fails at every mandatory isolation layer, no existence oracle | §9.2, §9.5 |
| **F — Critical delta integrity** | Change-Tracking paged pull under concurrent writes/retries/disconnects/clock-skew/crash → zero unrecoverable skip AND zero unrecoverable duplicate | §8.4, §16, §17- CT |
| **G — Server crash during settlement** | Business transaction remains atomic; outbox remains recoverable; retry is safe | §12.1 |
| **H — Desktop crash mid-commit** | Durable local state (atomic journal); no command loss; no double financial effect | §8.5.1, §8.6 |
| **I — Reconnect storm** | Jitter + backoff + progressive catch-up + per-tenant/global limits hold under mass simultaneous reconnect | §12.3, §88 |
| **J — Tenant isolation with RLS OFF** | Isolation holds with database-level RLS completely disabled | §9.2, §9.4 |
| **K — Local encryption** | Wrong key fails; plain reader fails; create/close/reopen/WAL/crash/rekey/restore/migration all pass | §49, §92 |
| **L — Tenant-safe cache** | Tenant A never receives Tenant B's cached result; a missing key component fails the test | §10.7 |
| **M — Mixed-version fleet** | Old client ↔ new server AND new client ↔ old supported server, across N/N-1/N-2, deterministic | §8.11, §18 |
| **O — Two-queue/cross-pipeline dependency** | Every interleaving of dependent state-mutation → command → command resolves deterministically by settlement, never by arrival order | §8.9 |
| **P — Idempotency-fingerprint integrity** | Same fingerprint replay = prior result; different fingerprint, same `OperationId` = integrity error, audited | §8.6.1 |
| **Q — Offline-session policy** | Max window enforced; revoked device refused on reconnect; credential change invalidates cached session; recovery path restores access | §10.4 |
| **R — File lifecycle & reconciliation** | Every divergence between the two consistency domains resolves deterministically; no `Available` before verified bytes; access always Valet-Key-gated | §14, §56–§57 |
| **S — Period-close race** | A device offline across a period close, reconnecting with pending commands dated into the closed period, resolves by the stated policy — never silently posts into a closed period | §11.1 |
| **T — Contended allocation** | Two clients booking the same slot/room (or claiming the same drawer/seat) from different base versions → exactly one succeeds, server-side, in a transaction | §11.4, §37 |
| **U — Client disk loss** | Unsent locally-committed commands follow the declared policy — recovered from encrypted local backup, or the user is warned they are unrecoverable; never silently assumed synced | §17.3, §76 |
| **V — Delete resurrection & tombstone retention** | An offline edit to a server-deleted row resolves by domain policy; a device offline the full supported window still observes every deletion/revocation (retention ≥ window + margin) | §8.14, §18, §19-tomb |
| **W — Initial/full-sync consistency** | A first/full sync under concurrent multi-table writes lands on one snapshot watermark, referentially consistent, never a per-table patchwork | §16-CT, §50 |
| **W-csrf — Browser anti-forgery** | Every state-changing browser request without valid anti-forgery is refused; CORS alone does not admit it | §10.2 |
| **X — Backup-restore consistency** | DB + file store + runtime key material restore to one Recovery Point Manifest within RTO, data loss ≤ RPO, from a tested backup; offline root/signing keys restored from separate custody | §17.3, R-DR |
| **Y — Direct-SQL state visibility** | A governed direct-SQL mutation on a critical-sync table becomes sync-visible via Change Tracking | §20 |
| **Z — Direct-SQL business protection** | Critical financial/business invariants cannot be silently bypassed by ordinary SQL | §20 |
| **AA — Echo safety** | A client's own committed change replayed in a later delta is applied idempotently | §8.15 |
| **AB — Notification independence** | A lost notification never causes a permanent missed sync | §44–§45 |
| **AC — Tenant QoS / noisy neighbor** | No single tenant can consume all server resources | §12.4, §71 |
| **AD — Poison outbox / cursor expiry** | A permanently failing effect reaches DeadLetter with a reason after a bounded budget and surfaces to an operator; an expired Change-Tracking cursor always triggers a correct full resync | §12.1, §18 |
| **AE — Full-resync safety** | A shadow-replica rebuild cannot destroy the last good local replica, and never deletes unsent mutations | §50, §51 |
| **AF — License / key compromise & anti-clone** | Signature + rotation valid; a copied license fails on a second installation; a wound-back clock cannot extend expiry; a compromised runtime node cannot mint a valid license (signing key not present) | §10.6, §13.2 |
| **AG — Local observability, Internet pulled** | Every observability capability functions with zero external connectivity; removing a local collector never breaks the app | §15, §17.4 |

**[INVARIANT] If any gate fails, the affected boundary/pattern is fixed before
feature work continues on top of it** — these gates protect the architecture's
own promises, not a specific library's behavior. GA readiness (§115) requires
all critical gates pass, no silent-data-loss defect, no unresolved sync-cursor
issue, proven backup-restore / native encryption / tenant isolation /
mixed-version contract, and documented RPO/RTO + capacity budget met.


---

## 20. Direct Database Changes & Maintenance Governance

Direct server DB maintenance is a **first-class requirement** (R9), not an
afterthought — but it is **governed**, because the critical sync feed and the
business invariants both depend on *how* a direct change is made.

### 20.1 Safe direct state mutation (critical-sync tables)

**[DECISION]** A direct SQL `INSERT/UPDATE` on a **critical-sync table governed
by Animora's maintenance policy** is visible to the critical sync feed because
SQL Server Change Tracking tracks qualifying DML:

```text
DBA / governed maintenance SQL → SQL Server DML → Change Tracking → Client delta
```

This guarantee applies to the **custom critical sync path** (which does not
depend on any `UpdatedAt` cursor). It does **not** mean arbitrary SQL against a
Datasync `SYNC-SIMPLE` table is safe — a Datasync table relies on library
metadata (`UpdatedAt`, `Version`, `Deleted`), so direct SQL against it is
allowed only through a maintenance operation that preserves every Datasync
invariant, or that table is migrated to the critical custom path.

```text
Critical Sync table        → governed direct SQL can remain sync-visible
Datasync SYNC-SIMPLE table → arbitrary direct SQL is FORBIDDEN
```

### 20.2 Direct business mutation is forbidden

**[INVARIANT] Direct SQL must never bypass critical business invariants.**
Forbidden as arbitrary SQL:

```text
UPDATE PostedInvoice SET Total = ...     UPDATE JournalEntry SET Amount = ...
UPDATE Payment SET Status = ...          UPDATE StockBalance SET Quantity = ...
UPDATE FiscalPeriod SET Status = Closed ...
```

A required business correction uses an **Administrative Command** or an
**Audited Maintenance Procedure** that invokes the same domain/application
rules (or a specifically defined controlled correction path) — never a raw
value overwrite (§19 Gate Z).

### 20.3 DBA access classes & break-glass

```text
ReadOnly Operational → Routine Maintenance → Emergency Break-Glass
```

Break-glass requires identity · reason · ticket/reference · timestamp ·
operator · audit. Hard-delete of a synchronized entity is forbidden from
ordinary production paths and permitted only through a reviewed maintenance
operation that first proves all synchronization/retention implications (§19).

---

## 21. Architecture Decision Log (condensed)

| # | Decision | Status | Driver |
|---|---|---|---|
| AD-01 | Modular Monolith over microservices at launch | Accepted | R2, R12, §2 principle 19 |
| AD-02 | State replication and business intent are two permanently separate pipelines | Accepted (spine) | §0.1, R4, R5 |
| AD-03 | Server is the single global authority; clients hold provisional truth only | Accepted (spine) | §0.2, R5 |
| AD-04 | Split client presentation, shared core | Accepted | R1, R2, R4 |
| AD-05 | Shared-DB, tenant-discriminator multitenancy with Deployment-Stamps escape hatch | Accepted | R8 |
| AD-06 | Defense-in-depth tenant isolation: app-layer mandatory, SQL RLS hardening only | Accepted | R3, R8 |
| AD-07 | Authentication/Authorization/Entitlement as three independent planes | Accepted | R8, R3 |
| AD-08 | Explicit, capability-scoped offline-session policy separate from online auth | Accepted | R4, R11 |
| AD-09 | Append-only ledger + movement-based inventory (never absolute-value offline writes) | Accepted | R5, R6, R7 |
| AD-10 | Idempotent Consumer + Transactional Outbox + Competing Consumers for all async effects | Accepted | R3, R4 |
| AD-11 | Valet Key for all file access; no filesystem path ever reaches a client | Accepted | R3 |
| AD-12 | Fully local, layered observability; zero mandatory external telemetry | Accepted | R11 |
| AD-13 | Single-node deployment at launch; every scale-out step is an explicit, gated ADR | Accepted | R2, R12, §2 principle 19 |
| AD-14 | Local PKI (offline Root CA + online Issuing CA) provisioned by deployment tooling, never app code | Accepted | R11, R3 |
| AD-15 | Licensing (offline entitlement) is architecturally independent of Subscription (commercial model) | Accepted | R8, R11 |
| AD-16 | Offline revocation is a next-contact guarantee, bounded by capability-snapshot expiry | Accepted | R4, R11, §10.4 |
| AD-17 | Tombstone/CT/authorization/license retention ≥ max supported offline window + margin (one linked decision) | Accepted | R4, §1.1, §18 |
| AD-18 | **Critical `ChangeSequence` is implemented by SQL Server Change Tracking** | **Accepted — replaces the earlier "custom opaque sequence only" baseline** | R4, R5, R9 |
| AD-19 | Inventory concurrency policy (negative-stock, reservation, lot/expiry/serial) — convergence is arithmetic, not business, correctness | Open (pre-code) | R5, R7, §11.2 |
| AD-20 | Scheduling allocation model (server-transaction resource allocation, not version tokens alone) | Accepted | R5, §11.4, §37 |
| AD-21 | General-ledger posting model + period-close state machine + offline-period-close policy | Accepted (values open) | R5, R6, §11.1 |
| AD-21b | Cross-context financial consistency declared per interaction (atomic vs. eventually consistent), no silent gap | Accepted | R5, §6.2, §38 |
| AD-22 | PKI hierarchy: offline Root CA + online Issuing CA; root/signing keys never on the runtime node or in its backup | Accepted | R3, R11, §10.5/§10.6 |
| AD-23 | License-signing key custody: offline issuer, runtime holds verification keys only; device identity hardware-backed where supported | Accepted | R3, R11, §13.2 |
| AD-24 | Offline subscription provisioning over an explicit management plane (signed package or controlled queue), separate from the runtime plane | Accepted | R8, R11, §13.4/§13.4a |
| AD-25 | Backup topology + declared RTO/RPO/SLO: ≥2 copies, ≥1 off-host + immutable, tested restore, Recovery Point Manifest | Accepted | R-DR, §17.3 |
| AD-26 | Tamper-evident security/financial audit (hash-chained/signed batches, separate audit key) beyond plain append-only | Accepted | R3, §61, §104 |
| AD-27 | Client disk-loss semantics for unsent commands (encrypted local backup, or declared unrecoverable + user warning) | Accepted | R4, §17.3, §76 |
| AD-28 | **Critical Sync Engine is custom and Change-Tracking-backed** | Accepted | R4, R5, R9 |
| AD-29 | **Datasync restricted to low-risk/simple (SYNC-SIMPLE) replication only** | Accepted | R4, §8.2/§8.3 |
| AD-30 | Client-local Mutation Journal owned by Animora, atomic with local state | Accepted | R4, §8.5 |
| AD-31 | Cross-pipeline causal dependency DAG (`CausationId` ≠ `DependsOnOperationId`) | Accepted | R5, §8.9 |
| AD-32 | Patch-based state mutations where supported, with the conservative-merge rule | Accepted | R5, §8.13, §32 |
| AD-33 | Direct-SQL mutation governance (state-visible on critical tables; forbidden for business invariants) | Accepted | R9, §20 |
| AD-34 | Production central SQL Server baseline = Standard (capacity/cost, not domain) | Accepted | R2, §70 |
| AD-35 | Desktop auth uses the stack-approved local Identity token mechanism; no generic OIDC claim | Accepted | R3, §10.2 |
| AD-36 | Notification channel is non-authoritative (hint only) | Accepted | R10, §44/§45 |
| AD-37 | Sync-scope / SyncProfileVersion versioning; scope change may force controlled resync | Accepted | R13, §53/§54 |
| AD-38 | Full resync uses a shadow replica, switched in atomically | Accepted | R4, §50 |
| AD-39 | Full resync never deletes unsent mutations | Accepted | R4, §50/§51 |
| AD-40 | Financial state never enters generic CRUD settlement | Accepted | R5, R6, §8.3 |

---

## 22. Architecture Rules for AI Agents (mandatory, architecture-level)

```text
A-RULE 001  Domain code never depends directly on infrastructure, and references no sync abstraction at all; the sync port is Application-owned and the engine (custom or Datasync) sits behind it in Infrastructure.
A-RULE 002  Domain code never references a replication library (e.g. Datasync) directly.
A-RULE 003  Critical synchronization uses the Animora Custom Sync Engine (Change-Tracking-backed), never a third-party library as the authority.
A-RULE 004  Datasync is permitted ONLY for explicitly classified SYNC-SIMPLE entities that pass the qualification gate.
A-RULE 005  Posted accounting/financial facts are immutable.
A-RULE 006  Corrections to posted finance are compensating/reversal commands, never an in-place UPDATE.
A-RULE 007  Inventory is movement-based; a stale client never writes an absolute stock quantity.
A-RULE 008  A contended shared-resource invariant (slot/room/cash-drawer/seat/period/device allocation) is enforced in a server transaction, never by an optimistic version token alone.
A-RULE 009  Every business mutation carries a stable OperationId.
A-RULE 010  Same OperationId + same CommandFingerprint = replay → return the prior result; never a second effect.
A-RULE 011  Same OperationId + different CommandFingerprint = security/integrity error, audited and rejected.
A-RULE 012  The server is the final global authority for settlement; a client holds provisional local truth only.
A-RULE 013  A client may commit provisional local state while offline (atomic with its Mutation Journal record).
A-RULE 014  A client-local financial state is never labeled globally posted/settled/closed until server settlement succeeds.
A-RULE 015  TenantId is resolved only from authenticated membership, server-side; never from client input.
A-RULE 016  The resolved tenant context is immutable for the whole request/work item.
A-RULE 017  An inbound sync pull never destructively overwrites unsent local business intent.
A-RULE 018  Authorization is re-evaluated server-side; a client never trusts its own authorization decision.
A-RULE 019  Authentication, Authorization and Entitlement are checked independently, never merged.
A-RULE 020  A notification/real-time channel is never the authoritative sync data channel.
A-RULE 021  The critical delta cursor advances only after durable local apply.
A-RULE 022  Sync is at-least-once; local apply is idempotent (echo-safe).
A-RULE 023  A full resync never deletes Animora-owned unsent mutations, and is built in a shadow replica.
A-RULE 024  The critical delta cursor is the opaque server-issued ChangeSequence (SQL Server Change Tracking version); UpdatedAt is display/audit only, never a critical cursor and never an ordering source.
A-RULE 025  rowversion is concurrency-detection metadata, never a synchronization cursor and never a business sequence.
A-RULE 026  Client wall-clock time is never a synchronization or licensing authority.
A-RULE 027  CausationId (provenance) and DependsOnOperationId (settlement precondition) are distinct; dependency edges form a DAG; no dependent operation settles before its prerequisite settles.
A-RULE 028  The replication queue and the business command queue are never assumed globally ordered; network arrival order is never business order.
A-RULE 029  Change Tracking is not a substitute for domain events; the Outbox is not a substitute for state synchronization; the two feeds are never merged.
A-RULE 030  A license signature proves authenticity, not non-cloning; installation binding MUST use a protected (hardware-backed, non-exportable where supported) device identity — a software-only machine fingerprint is NOT accepted as anti-cloning proof.
A-RULE 031  Introducing a backplane/broker/microservice/distributed-cache/partitioned-tenant requires an explicit ADR.
A-RULE 032  Zero external telemetry is a constraint, not an excuse for zero observability.
A-RULE 033  Every cache key for tenant/clinical/financial data includes tenant + user/permission + resource + version scope; caching runs only after authN/authZ.
A-RULE 034  Heavy reporting/aggregation load never rides the OLTP transaction path.
A-RULE 035  A capacity/concurrency claim is earned from measurement, never asserted from specs alone.
A-RULE 036  Client-originated TenantId, UserId, DeviceId, Role, Entitlement, AccountingDate, AuthorizationDecision or settlement/posting state is NEVER authoritative; the server derives all of them from the authenticated, device-bound credential.
A-RULE 037  Every supported offline duration has tombstone, authorization-revocation, Change-Tracking and licensing retention windows at least as long as that offline window plus an explicit safety margin.
A-RULE 038  A client-local financial state may be provisional, but no UI label, event, report or local state may represent it as globally posted/settled/closed until server settlement succeeds.
A-RULE 039  Trust-minting private keys (Root CA, license-signing) never reside on the runtime server; it holds public verification keys only.
A-RULE 040  Every state-changing browser request requires anti-forgery protection; CORS is not a substitute.
A-RULE 041  A filesystem path is never an access credential — Valet Key only; file availability requires verified bytes.
A-RULE 042  Every async/at-least-once effect has an idempotent consumer; every worker step is resumable after a crash without duplicating effects.
A-RULE 043  Every retry has a bounded budget; a connectivity outage transitions a client to offline mode, never a retry storm.
A-RULE 044  A stale write is detected (version mismatch), never silently overwritten, and the mismatch never by itself decides the business conflict.
A-RULE 045  Automatic field-level merge requires provable non-overlap; insufficient evidence ⇒ Conflict/NeedsAttention, never a guess.
A-RULE 046  Critical direct-SQL business mutation is forbidden outside a governed maintenance path; governed direct-SQL on a critical-sync table must remain Change-Tracking-visible.
A-RULE 047  An expired Change-Tracking cursor causes a controlled full resync — never a guess, a silent skip, or a start-from-current-version.
A-RULE 048  Hard-delete of a synchronized entity is forbidden in ordinary production paths (tombstone instead).
A-RULE 049  A SyncProfile/scope change may require a controlled resynchronization; an old cursor is never reinterpreted as covering a new dataset.
A-RULE 050  Shared-workstation user profiles never share offline authority implicitly; each user gets a logically separate encrypted local replica.
A-RULE 051  The public website is never a clinic runtime dependency; runtime operation continues with zero external Internet.
A-RULE 052  Every architecture boundary has a validation gate (§19); a failing gate blocks feature work on top of it.
```

**Distinctions this architecture never collapses:**

```text
Server business truth           ≠ server replication truth ≠ client provisional truth
state replication               ≠ business intent settlement
critical sync                   ≠ Datasync SYNC-SIMPLE CRUD
state mutation                  ≠ business command
Change Tracking                 ≠ domain event / Outbox
ChangeSequence (delta cursor)   ≠ rowversion (concurrency) ≠ UpdatedAt (display/audit)
rowversion                      ≠ OperationId ≠ DatasyncOperationId
concurrency detection            ≠ business conflict resolution
idempotency                      ≠ ordering
at-least-once delivery           ≠ exactly-once side effect
provisional offline state        ≠ global posted/settled/closed truth
LocallyCommitted                 ≠ Posted/Settled (server-authoritative)
notification received            ≠ data synchronized
authentication                   ≠ authorization ≠ entitlement
RBAC                            ≠ resource-based authorization
Subscription (commercial)        ≠ Licensing (offline entitlement)
license signature                ≠ anti-cloning proof
server-reachable revocation      ≠ offline (next-contact) revocation
runtime verification keys        ≠ offline trust-minting keys
CausationId (provenance)         ≠ DependsOnOperationId (settlement precondition)
version-token staleness          ≠ contended-resource allocation
tenant filter (convenience)      ≠ full security boundary
RLS (hardening)                  ≠ application authorization (mandatory)
read projection                  ≠ write truth
quantity-on-hand projection      ≠ stock movement
audit                            ≠ coordination channel
backup exists                    ≠ restore proven
```

---

## 23. Non-Goals (architecture-level)

- **Microservices at launch** — a Modular Monolith is the launch architecture;
  a future service extraction requires an explicit ADR (§16.2, A-RULE 031).
- **A third-party sync library as the universal/authoritative sync mechanism**
  — Datasync is a contained SYNC-SIMPLE adapter only; the custom
  Change-Tracking-backed engine is the authority (§8.2, A-RULE 003/004).
- **Web offline capability** — the web client is intentionally online-only
  (§7.2); a boundary decision, not an oversight.
- **Generic CRDT semantics / global last-write-wins for financial/business
  data** — convergence is domain-policy-driven, not a generic merge (§0.2,
  §8.12).
- **A generic distributed-transaction/saga coordinator** — cross-context
  coordination uses domain events + the Outbox inside one process, not a
  distributed-transaction layer (§6.2, §22-A).
- **A mandatory message broker, Redis, or distributed cache at launch** —
  Outbox + in-process relay is the launch reliability architecture; each is an
  additive, gated future step (§16.2, §74), never a launch dependency.
- **Any mandatory cloud identity, telemetry, storage or license-validation
  service at runtime** — would directly violate R11; never introduced as a
  *required* runtime path at any point in the roadmap.
- **Treating the sync engine's transport as a business ordering source** — no
  future feature may reintroduce this collapse (§8.4, A-RULE 024/025).
- **Per-tenant database at launch** — the shared-DB model is the launch shape;
  the Deployment Stamps escape hatch (§9.1, §72) is designed for, not built,
  until a real tenant needs it.
- **Arbitrary DBA SQL against financial invariants** (§20), and
  **exactly-once network delivery** (an impossibility the architecture never
  claims — §101).


---

## 24. Pattern Catalog & Source Basis

Sources are grouped by their **true provenance** — primary methodologies are
*not* Microsoft artifacts and must never be cited as such — and are re-checked
against the same authoritative set `STACK.md` uses for technology claims.
Patterns are **adapted to Animora's deployment and reliability constraints**,
not "applied as-is."

**Primary methodologies (independent sources, NOT Microsoft):**

- **C4 model** (Context/Container/Component) — Simon Brown, `c4model.com` —
  §3–§5 structural views.
- **arc42** — `arc42.org` — the independent documentation structure behind §1
  (quality-attribute scenarios) and §21 (decision log).
- **Domain-Driven Design — bounded contexts** (Evans) inside a modular
  monolith — §6.
- **Clean / Onion Architecture & Ports & Adapters (Hexagonal)** — Martin /
  Cockburn lineage; Microsoft's ".NET application architecture" guides apply
  the same shape — §5, §8.1.

**Microsoft / Azure Architecture Center patterns, applied deliberately and
cited at the point of use:**

- **Transactional Outbox** — §12.1, §39.
- **Idempotent Consumer** — §8.6.1, §12.1.
- **Competing Consumers** — §12.2, §41.
- **Queue-Based Load Leveling / Bulkhead / Throttling** (reliability) — §12.3,
  §12.4, §42, §43, §71. **Circuit Breaker** is adopted *only* at the remote
  client↔server boundary, **not** on local in-process outbox effects at launch.
- **Retry** (transient-fault handling) — §12.3, §42.
- **Cache-Aside** — §10.7, §59.
- **Valet Key** — §10.8, §14, §57.
- **Deployment Stamps** — §9.1, §72.
- **Minimize coordination** (design principle) — §2.

**Microsoft platform/security guidance** (applied against the methodologies
above): SQL Server **Change Tracking** (critical change feed, §16-CT/§17-CT);
SQL Server **Row-Level Security / `SESSION_CONTEXT`** (§9.4); EF Core named
query filters; ASP.NET Core authentication/authorization; ASP.NET Core
**antiforgery** (§10.2); ASP.NET Core **Data Protection** (§10.6, §64); Blazor
render modes (§46); **SignalR** scaling (§44, §73); SQL Server **Resource
Governor** (§71).

> When this file is revisited, re-validate every **[PATTERN]** citation against
> the then-current Microsoft Learn / Azure Architecture Center wording before
> extending it — never let a pattern name drift from its documented definition.

---

# Part II — Backend & Synchronization Deep-Dive Appendix

> **Why this appendix exists.** Part I (§0–§24) is the self-contained,
> technology-aware-at-boundaries architecture. This appendix preserves the
> **deeper backend/synchronization detail** (the part of the design that is the
> most developed and most safety-critical) at full fidelity, with the original
> backend section numbers retained as stable anchors (§16-CT, §35–§113) so that
> `STACK.md`, the backend roadmap, and engineers keep working cross-references.
> Nothing here contradicts Part I; it elaborates it. Where a concept already
> has a Part I home, the appendix cross-links rather than restates the rule.

## §16-CT. SQL Server Change Tracking as the critical-state feed

SQL Server Change Tracking is a lightweight, transaction-commit-based DML change
tracking mechanism designed for synchronization; it provides committed change
versions and recommends snapshot isolation for correct, consistent enumeration
of change sets.

**Concept mapping:** `Animora ChangeSequence = SQL Server Change Tracking
version` (§8.4). This is an *implementation* of the architectural abstraction;
application code never exposes SQL-specific types.

**Cross-tenant changes and the global cursor.** The Change Tracking version is
**global to the database, not tenant-local**. A Tenant A client may therefore
advance its checkpoint across versions that belong to other tenants:

```text
Version 1001 → Tenant A
Version 1002 → Tenant B   (outside A's authorized scope)
Version 1003 → Tenant B   (outside A's authorized scope)
Version 1004 → Tenant A

A client may safely advance 1000 → 1004 even though 1002/1003 were never
returned to it, because those changes are outside its authorized replication
scope. Global change position + per-request tenant/authorization filtering =
safe scoped replication.
```

A change in authorization/scope invalidates the meaning of the old local
dataset and may require a controlled resync (§54).

**Change Tracking is not a complete field-history system (§16.3 cross-ref).**
It answers *which row changed, what operation, at which version* — not an
unlimited history of every prior field value. Automatic field-level rebase is
allowed only with sufficient evidence (changed-column metadata where
applicable + domain mutation history where maintained + a provably non-
overlapping operation); otherwise `Conflict / NeedsAttention` (§8.13, §32).

## §17-CT. Change-feed correctness protocol

The critical delta protocol follows the documented safe sequence, adopted
verbatim as Animora's critical pull:

```text
BEGIN SNAPSHOT TRANSACTION
  1. Validate last synchronization version  (CHANGE_TRACKING_MIN_VALID_VERSION)
  2. Capture next synchronization version    (CHANGE_TRACKING_CURRENT_VERSION)
  3. Enumerate CHANGETABLE(CHANGES ...) for all required tables
  4. Read current rows / tombstones for those changed identities
     inside the SAME snapshot
COMMIT
```

This avoids race conditions involving concurrent commits and metadata cleanup
(§19 Gate F).

## §18-CT. Change Tracking retention

Change Tracking metadata is retained only for the configured `CHANGE_RETENTION`.
If a client checkpoint falls below `CHANGE_TRACKING_MIN_VALID_VERSION()`,
incremental sync is no longer valid and the client must perform a **controlled
full resync** (§50) — never guess, silently skip, or start from the current
version (A-RULE 047). Retention invariant:
`ChangeTrackingRetention ≥ MaxSupportedOfflineWindow + SafetyMargin` (§1.1). A
supported offline duration cannot be published unless the server is configured
and capacity-tested for the corresponding retention.

## §19-tomb. Tombstones & delete semantics

Synchronized business rows use **logical deletion** wherever clients must learn
the deletion: `Deleted=true · DeletionVersion · DeletedAt · DeletionReason ·
DeletedBy`. Hard-delete is prohibited from ordinary production paths for
synchronized entities (§20.3). Change Tracking can report *that* a row was
deleted but cannot reconstruct the full prior row for a client that needs a
tombstone — hence durable tombstone state. An **offline edit to a server-deleted
row** is a distinct *resurrection conflict* routed to explicit domain policy
(reject / restore-with-audit / create-replacement / human review), never
auto-applied as if the row still existed (§8.14, §19 Gate V).

## §34. Patient / clinical data modeling principle

When possible, model independent operations as **additive** (`ClinicalEntry 1,
2, 3 …`) rather than one giant mutable `MedicalHistoryText` blob — this
naturally reduces conflict frequency (§8.14).

## §35. Finance architecture (detail of §11.1)

**§35.1 Ledger.** Posted facts are immutable; `Journal · JournalLine ·
PostingBatch` obey `Σ Debit = Σ Credit`.
**§35.2 No posted mutation.** Corrections are `Reversal / Credit Note /
Adjustment`, never `UPDATE posted value`.
**§35.3 Posting requirements.** A posted fact carries/references `AccountingDate
· DocumentDate · Currency · ExchangeRate version · Tax snapshot · Rounding
policy · Account/dimensions · Source document · Posting user/device · Posting
timestamp · Operation reference`.
**§35.4 Fiscal periods.** `Open → SoftClose → Review → Closed → Authorized
Reopen/Correction`. Offline commands dated into a closed period follow one
explicit policy (reject / post-to-current / authorized back-post / hold /
adjustment) — never a silent post into a closed period (§11.1, §19 Gate S).

## §36. Inventory architecture (detail of §11.2)

Movements: `Receipt · Sale · Consumption · Loss · Adjustment · Transfer ·
Return`; quantity-on-hand is derived. Two offline movements can both be valid
local facts; the **server decides whether the final outcome is allowed** via the
per-product-class policy (negative stock? reservation? backorder? lot/batch?
expiry? serial? location? cost layer?). Forbidden: `Set QuantityOnHand = 5` from
a stale client. Allowed: `StockAdjustment +3` as a command/movement.

## §37. Scheduling architecture (detail of §11.4)

A row-version check is **not** sufficient for a contended resource (room, doctor
slot, boarding capacity, cash drawer, seat, device allocation). Use a
server-side transactional allocation invariant — `check allocation + insert/
claim + commit` — via a uniqueness constraint, serializable/appropriate
transaction, resource-allocation table, or a short-lived application lock where
justified. **No global lock** (§19 Gate T).

## §38. Cross-context consistency matrix (detail of §6.2)

| Interaction | Consistency |
|---|---|
| Sale + stock reduction + payment at checkout | Atomic |
| Purchase receipt + payable | Atomic |
| Payment + cash-session entry | Atomic |
| Refund + inventory return | Atomic |
| Sale → draft invoice | Eventual, explicit pending state |
| Settled sale → financial posting | Eventual only where intentionally designed |
| Reporting projection | Eventual |
| Audit | Same-transaction where required for financial/security audit |

There must be **no silent financially incomplete state**.

## §39–§43. Reliability detail

**§39 Transactional Outbox / §40 Outbox state machine / §41 Worker architecture
/ §42 Retry architecture / §43 Bulkhead/backpressure** — the full detail is in
Part I §12.1–§12.4. Launch worker host = in-process background service with
bounded queues/concurrency and priority classes P0–P3; every remote retry
policy declares max attempts, max elapsed time, backoff, jitter, per-tenant and
global budgets; separate resource budgets isolate interactive / sync / file /
reports / maintenance / outbox so report overload reduces report throughput but
never stops checkout.

## §44. Near-real-time collaboration (notifications as hints)

**[INVARIANT] Correctness never depends on a notification.** Responsiveness:

```text
Foreground active screen → adaptive short polling / hint; target 1–3 s
                           visibility latency, randomized jitter
Idle screen              → 5–15 s
Background               → 30–120 s
Optional scale-out       → SignalR notification adapter (hint only)
```

Precise intervals are benchmark parameters. If SignalR is enabled later it sends
only "tenant changed / scope changed / latest hint-version" — never full
records — and scale-out then needs session affinity + a backplane (§73).

## §45. Notification failure

If a notification is lost, **no data is lost**: the client eventually
polls/synchronizes from its checkpoint. A notification channel can be added,
removed, or replaced without altering state correctness (§19 Gate AB).

## §46. Web architecture (detail of §7.3)

Blazor Web App: **Static SSR = default; InteractiveServer = selective.** Static
SSR holds no persistent circuit but still runs server/DB work per request;
InteractiveServer creates a server-side circuit with per-session memory/
connection implications. Rules: never make the whole app InteractiveServer by
default; large read-heavy screens prefer static/paged; typeahead/search
endpoints are dedicated and debounced; interactive islands stay small; **server-
side authorization is always authoritative.**

## §47. Desktop architecture (detail of §7.4)

`Avalonia → MVVM → Shared Application Core → Local Repository → SQLite/EF Core →
Sync Coordinator`. **No browser/WebView in the offline path.**

## §48. Local database concurrency

One local synchronization writer at a time. EF Core `DbContext` instances are
short-lived and never used concurrently across threads. The local sync writer
serializes checkpoint writes, delta application, mutation settlement and
conflict-state updates; read queries run concurrently on independent contexts.

## §49. Local WAL & encryption

SQLite uses a WAL-compatible configuration where proven by the encryption
provider. The encrypted database must pass: create · close · reopen · wrong-key
failure · plain-reader failure · migration · WAL · crash recovery · backup ·
restore · key rotation (§19 Gate K). The native SQLite binary version is
verified at runtime, not inferred from package metadata.

## §50. Full resynchronization (shadow replica)

A full resync occurs when the cursor expired, the replica is corrupted, the
contract is incompatible, scope changed materially, a manual repair is
requested, or integrity validation failed. Process:

```text
1.  Freeze local replication writes (NOT the business UI)
2.  Preserve the mutation journal
3.  Establish a consistent server snapshot
4.  Build a Shadow Replica
5.  Validate schema / relations / checksums
6.  Rebase preserved local mutations where policy allows
7.  Move irreconcilable mutations to ConflictInbox
8.  Atomically switch shadow → live
9.  Re-establish checkpoint
10. Resume normal sync
```

**[INVARIANT] The current local replica stays intact until the shadow replica
is proven valid** (§19 Gate AE); a resync never deletes a pending mutation
merely because the snapshot is newer.

## §51. Mutation rebase after full resync

Preserved mutation `BaseVersion = V10`, server snapshot `V20`: the engine
attempts to rebase the V10 patch onto V20. Safe ⇒ `PendingSettlement`; not safe
⇒ `Conflict / NeedsAttention`. A resync never deletes a pending mutation merely
because the snapshot is newer (A-RULE 023).

## §52. Echo safety (detail of §8.15)

A client safely tolerates receiving its own committed state again; the second
application must not duplicate a business operation, reset dirty flags wrongly,
create duplicate audit records, or recreate a pending mutation (§19 Gate AA).

## §53. Sync scope

A client sync scope is determined by `Tenant · Device · User/session profile ·
Role/capabilities · SyncProfileVersion · ContractVersion`, and may include
entities, columns, row predicates, history depth, file policy and related
references. **Only data the device needs is replicated.**

## §54. Permission/profile changes

If a user's access scope expands or shrinks materially, `SyncProfileVersion`
changes and the client may require a controlled resync — preventing an old delta
cursor from being interpreted as if it covered a new dataset (A-RULE 049).

## §55. Shared workstation

If multiple people use one physical workstation, each user gets a logically
separate encrypted local profile/replica unless a specifically designed
shared-device model is approved. **No user inherits another's offline
authorization by sharing a machine** (A-RULE 050).

## §56–§57. Files (detail of §14)

**§56 lifecycle** `Staged → Uploaded → HashVerified → Committed → Available`;
**§56.1 transfer** `BeginUpload · UploadChunk · Resume · Complete · VerifyHash ·
Commit` (idempotent, resumable, hash-verified, cancelable, auditable);
**§56.2 identity** prefer immutable content-addressed objects — replacing a file
creates a new object + new metadata version. **§57 access** Valet-Key: `FileId →
authorization → short-lived signed grant → client accesses bytes`; a filesystem
path is never an authorization credential.

## §58. Reporting architecture (detail of §11.3/§11.5)

Reporting is read-only. Launch = OLTP + incremental read projections
(`DailySalesSummary`, `InventorySummary`, `AccountBalanceSummary`,
`ClinicKpiSummary`). Heavy reports run under separate resource budgets.
Projections may be eventually consistent and **must expose their freshness
timestamp/version** (§83).

## §59. Caching (detail of §10.7)

Cache only when measurement justifies it; every tenant-sensitive key includes
`Tenant · User/Permission scope · Resource · Query/Contract version · Data
version`; never cache authorization-sensitive results before
authentication/authorization (output caching runs *after* authZ).

## §60–§64. Security detail

**§60 Threat classes** (STRIDE) and **primary mitigations** — Part I §10.9.
**§61 Audit** has three layers — *application audit* (security/business
actions), *database audit* (break-glass/maintenance/direct-SQL), *financial
audit* (immutable references tied to accounting). Append-only; security/
financial batches may be hash-chained for tamper evidence (§104); audit never
coordinates business processing. **§62 Cryptographic keys** — runtime node holds
server TLS cert/key, Data Protection key ring, verification public keys, device
public verification material, and runtime backup-encryption artifacts; it must
**not** hold the offline root CA private key, license-signing private key, or
any other trust-minting private key (§10.6). **§63 Local PKI** — offline Root CA
→ Issuing CA → server certificate (§10.5). **§64 Data Protection** — the key
ring is recovery-critical with a stable `ApplicationName`, persisted key-ring
location, protection-at-rest and ACL, surviving restart and restore (§10.6).

## §65. Entitlement & licensing (detail of §13)

Subscription = commercial state; Licensing = offline runtime entitlement — kept
separate. License artifacts are signed by an offline protected signing
authority; the runtime server holds only verification material. Anti-cloning
relies on a device-bound protected identity where supported; a software-only
fingerprint is not strong anti-cloning proof.

## §66. Public website / management plane (detail of §13.4/§13.4a)

`Website (register/choose/pay/download) → signed onboarding intent →
Subscription → Tenant provisioning → License issuance`. The website never
participates in clinic runtime; the production clinic runtime remains functional
with the website completely unavailable.

## §67. Zero-Internet runtime (detail of §17.4)

Test independently with each of DNS / Internet / external time sync / OCSP-CRL /
CDN / package feeds / telemetry backend / public website unavailable, and verify
Login · Authorization · Core reads · Business commands · Critical sync · File
access · Local observability · Licensing verification (per offline policy) still
work.

## §68–§71. Deployment & capacity (detail of §16/§17)

**§68 Topology** — one vendor node (ASP.NET Core app + SQL Server + file store +
local certs/runtime keys + local observability); clinics connect; no clinic runs
a server. **§69 Hardware baseline** — a deliberately modest reference (e.g.
4 vCPU / 16 GB / SSD-NVMe); never claim a user count from hardware alone.
**§70 SQL edition** — Standard is the production baseline (buffer-pool/compute
ceilings + Resource Governor); Express for dev/pilot/very-small only; a
capacity/cost decision, not a domain dependency. **§71 Noisy-neighbor
isolation** — no single tenant may consume all CPU/memory/DB-worker/sync/report/
file-I/O; use application rate/concurrency budgets + background-work quotas + SQL
Server Resource Governor where justified (§19 Gate AC).

## §72–§74. Scale-out detail (detail of §16.2)

**§72 Tenant stamps** `Tenant → DeploymentStamp → App+DB+FileStore`; initially
all tenants → Stamp 1; later a noisy tenant → Stamp 2; domain/application
contracts do not change. **§73 Multi-instance web** needs session affinity +
shared Data Protection keys + a SignalR backplane; single-node does not need
Redis. **§74 No broker at launch** — Database + Outbox + background service, not
Kafka/RabbitMQ/Service Bus; a broker is introduced only on demonstrated need and
an ADR.

## §75–§78. Disaster recovery & client recovery detail (detail of §17.3)

**§75 Server state** — a recovery point ties Database + FileStore + Runtime key
material + Application build + Schema version via the Recovery Point Manifest;
**§75.2** ≥2 copies, ≥1 off-host, ≥1 immutable; restore tests mandatory (a
never-restored backup is not evidence of recoverability). **§76 Client
recovery** — server recovery restores server truth, not unsent client-only
mutation; desktop policy protects pending mutations by encrypted local
recovery/export, or the UI warns they may be unrecoverable after total device
loss. **§77 Client disk corruption** — quarantine → preserve journal →
re-authenticate → shadow resync → rebase → restore conflict inbox → atomic
switch. **§78 Restore vs. checkpoint** — a restored server may have a lower
Change Tracking version; clients validate `RecoveryPointId` / DB identity /
schema / sync-feed validity and may be forced into controlled full resync.

## §79–§80. Versioning & migration (detail of §18)

**§79** expand/contract schema discipline; `ContractVersion` + capability
negotiation; supported fleet N / N-1 / N-2. **§80** a migration is successful
only if server DB + existing clients + offline replicas + pending mutations all
remain compatible across the supported window.

## §81. Error-handling contract (detail of §8.6.4)

No generic "Sync failed" for critical commands; every failed/conflicted
operation answers *what failed / why / financial-business effect / what the user
should do / who must fix it*.

## §82–§83. Observability detail (detail of §15)

**§82** every cross-boundary operation carries `RequestId · CorrelationId ·
TenantId · UserId · DeviceId · OperationId · MutationId · AggregateId`; logs are
structured, local, PII-minimized; the minimum metric set and the separate
health probes are listed in §15; the diagnostics bundle exports sanitized state
with no secrets. **§83** every management-reporting projection exposes
`ProjectionGeneratedAt` and `SourceVersion/SourceWatermark`.

## §84–§90. Performance detail (detail of §16)

**§84** server optimization order + push-safe-work-to-client; **§85** EF Core
rules (`AsNoTracking`, projection, pagination, short-lived pooled context,
compiled/batched queries where measured; avoid N+1 / `SELECT *` / unbounded /
long transactions); **§86** sync endpoints return only changed identities/rows,
page by bounded payload (target 256 KB – 1 MB with a row-count ceiling), support
cancel/resume, avoid entity-graph materialization and per-row HTTP; **§87**
adaptive catch-up prioritizes settlement → active-screen state → routine state →
background/reporting; **§88** reconnect-storm control (jitter + adaptive backoff
+ bounded retry + overload signals + per-tenant & global limits); **§89** SQL
Server Query Store for p95/p99 latency, CPU, logical reads, plan regressions,
lock waits, deadlocks, tempdb/version-store/log pressure; **§90** critical sync
uses snapshot isolation, so snapshot transactions must be short (long-lived
transactions delay change-tracking cleanup) and full initial syncs are throttled
and measured.

## §91. Data retention

Each domain defines operational / audit / financial / sync-tombstone / file /
diagnostic retention; retention must never cause a supported offline client to
miss a required deletion/revocation (§1.1, §18-CT).

## §92–§93. Local-data & path security (detail of §10.8)

**§92** desktop local storage is encrypted with an OS-protected key, filesystem
ACL, locked credentials, device identity and short sensitive-operation windows;
it fails closed when the key is unavailable/invalid. **§93** filesystem paths
never cross the client/server trust boundary as credentials — a file reference
is a logical `FileId`.

## §94. Domain event vs. replication event (detail of §8.8)

`InvoicePosted` is a domain event; the same transaction also changes rows that
Change Tracking later exposes. The client may receive "Invoice projection
changed" via critical state sync; it does not receive the internal domain event
unless a separately defined contract requires it.

## §95–§96. Command & query shape

**§95** business commands are small and semantic (`RegisterPayment` not
`UpdatePayment`; `AdjustStock` not `SetQuantity`; `BookAppointment` not
`UpdateAppointmentSlot`). **§96** queries are read-only, use dedicated query DTOs
for high-value screens (not domain-aggregate serialization), and are authorized
before data access.

## §97–§99. API surface & security (detail of §4.1)

**§97** surface separation `/business/* · /sync/* · /tables/* · /notifications/*
· /operator/*` (route names may differ; semantic separation may not). **§98**
OData is a Datasync implementation detail only; no business client depends on
arbitrary OData semantics. **§99** every request flows `TLS → Authentication →
tenant resolution → authorization → entitlement → request validation →
business/application action`; no endpoint infers tenant from URL/body/query/
local-client-state alone.

## §100–§104. Validation, idempotency, ordering, allocation, audit integrity

**§100** three validation levels (client UX / application completeness / domain
invariants); server domain validation is authoritative. **§101** idempotency
applies to business commands, onboarding, file completion, outbox consumers and
repeatable maintenance — it does not mean "exactly once"; delivery stays
at-least-once. **§102** ordering is explicit via keys (`Tenant+AggregateId`,
`Tenant+ResourceId`, `Tenant+CashSessionId`); do not globally serialize a whole
tenant when independent aggregates can proceed in parallel. **§103**
`rowversion` detects stale edits but does not prove two clients cannot claim the
same slot — a contended resource needs a transactional allocation constraint
(§37). **§104** high-value audit uses `AuditBatch · PreviousBatchHash ·
CurrentBatchHash · Signature/KeyId` — an integrity control, not an execution
mechanism.

## §105–§107. Operations & failure semantics

**§105** maintenance tasks (Change-Tracking cleanup monitoring, outbox retry/
dead-letter monitoring, file reconciliation, DB integrity checks, backup
verification, projection rebuild, orphan detection, sync diagnostics, license
refresh) each carry scope · tenant context · authorization · rate limit · audit
· cancellation · resume semantics. **§106** a dead letter is not a trash can —
it carries event/operation identity · tenant · aggregate · attempt count · first/
last failure · error code · sanitized detail · payload fingerprint · operator
remediation state; reprocessing requires explicit operator action when
appropriate. **§107** every subsystem has a defined outcome (§12.5).

## §108. Chaos / fault injection

The architecture is tested with injected failures: process crash · DB deadlock ·
transaction rollback · network drop · timeout · lost response · duplicate
response · duplicate request · out-of-order request · corrupt local DB · corrupt
file · permission change mid-flight · server restart · full node restore · clock
anomaly · notification loss.

## §110. Capacity test method (detail of §16.3)

Published capacity comes from measurement. Test at least 25 / 50 / 100 / 250
tenants with a realistic user distribution; measure p50/p95/p99 API and
settlement latency, DB & server CPU, memory, disk I/O, transaction-log
throughput, Change-Tracking backlog, sync MB/s and rows/s, reconnect catch-up
time, outbox backlog, deadlocks, locks/waits. The benchmark workload must
include reads, writes, sync, reports, files, reconnect and background
maintenance — not only API microbenchmarks.

## §111–§115. Testing & readiness

**§111 Security testing** — see §10.9 list. **§112 Supply-chain** — every
production build records application build · .NET runtime · EF Core line ·
Datasync version · native SQLite binary version · RID · OS-native dependencies ·
schema version · sync contract version · license verification-key version; the
release manifest includes native artifacts. **§113 Upgrade** — server: test →
backup → apply → health-check → rollback plan; desktop: signed · verifiable ·
rollback-capable · offline-installable (macOS signed/notarized/stapled); no
runtime dependence on Internet update feeds. **§114 Deployment plan** — build →
static/security analysis → unit → integration → sync/chaos → migration rehearsal
→ backup/restore rehearsal → mixed-client compatibility → capacity benchmark →
release approval → production. **§115 GA readiness** — all critical gates pass,
no unresolved critical security finding, no silent-data-loss defect, no
unresolved sync-cursor issue, backup-restore proven, native encryption proven,
tenant isolation proven, mixed-version contract proven, documented RPO/RTO met,
capacity budget met, offline-window policy proven.

> **End of specification.** This unified file is the single source of truth for
> Animora's software architecture; `STACK.md` remains the source of truth for
> exact technology versions. Keep both self-contained; do not re-derive one
> from the other.
