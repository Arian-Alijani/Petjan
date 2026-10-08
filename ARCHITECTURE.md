# Animora — Software Architecture Specification (AI-optimized)

> **Status:** Architecture baseline **v3.2** (2026-10-08). Single authoritative
> architecture: structure, behavior, boundaries, patterns, flows. Supersedes
> ARCHITECTURE v1 and Animora_ARCHITECTURE_v2. Where they disagreed, the newer
> backend/sync model wins.
>
> **Scope of this revision.** Specification only. Server and sync adapters do not
> exist yet. No build, SQL test, benchmark or deployment is claimed. §19 gates are
> acceptance obligations, **not passed results**. Local SQLite stays; server = PostgreSQL 18 / Npgsql 10 (§PG, binding).
> `STACK.md` owns exact versions and packages. Source-scope and version caveats: §24.1–§24.2.
>
> **Audience.** Engineers, architects, security, QA/chaos, DevOps, AI coding agents.
> An agent must implement any module correctly from this file alone.
>
> **Tags.** **[PATTERN]** named pattern adapted to Animora (never "as-is").
> **[DECISION]** Animora-specific choice. **[INVARIANT]** must never be violated.
> **[DEFERRED]** intentionally not built; trigger stated.
>
> **Methodology.** C4 (structure), arc42 (drivers/decisions), DDD-lite (bounded
> contexts). Checked against Azure Architecture Center patterns (cited at use) and
> WAF pillars (traceability only, never a derivation source). Not Microsoft-certified.

## Agent quick index

| If the task is about… | Read |
|---|---|
| Is it state replication or business intent? | §0.1, §8.3, §8.6 |
| Sync cursor, checkpoint, batches | §8.4, §16-CT, §17-CT, §18-CT |
| Local queue, journal, settlement | §8.5, §8.6.2–§8.6.4, §8.9–§8.10 |
| Conflicts / merge | §8.12–§8.14 |
| Tenant isolation, RLS | §9.2, §9.4 |
| Auth, tokens, offline session | §10.2–§10.4 |
| Ledger, stock, period close | §11.1, §11.2, §11.4.1, §35, §36 |
| Outbox, workers, retry | §12.1–§12.4 |
| Licensing, onboarding | §13 |
| Files | §14, §56–§57 |
| Backup, restore, epoch | §17.3, §17.3.1, §75–§78 |
| PostgreSQL mapping and phases | §PG |
| Rule list for agents | §22 (A-RULE 001–073) |

---

## 0. Architecture at a glance

| Concern | Answer |
|---|---|
| Deployment | Vendor-operated, central, multi-tenant SaaS. Clinics are tenants and never run a server. |
| Topology | Single runtime node/process; DB and file store co-located where practical |
| Style | Modular monolith; no microservices at launch |
| Layering | Clean/Onion + Ports & Adapters; dependencies point inward only |
| Domain split | DDD-lite contexts in one deployable: Identity, Tenant/Clinic, Patient/Owner, Clinical, Scheduling, Sales/POS, Inventory, Finance, Reporting, Files, Subscription, Licensing, Audit; infra: Synchronization, Notification |
| Clients | Split presentation, shared core. Desktop = offline-capable native. Web = online-only. |
| Authority | Server = single global truth. Clients hold provisional local truth until settled. |
| Spine | State replication ≠ business intent settlement. Two pipelines, never merged semantically. |
| Critical sync | Custom Sync Engine on a PostgreSQL transactional change journal (correctness authority) |
| Simple sync | CommunityToolkit.Datasync adapter, only for SYNC-SIMPLE entities |
| Business intent | Durable, idempotent command pipeline ending in server settlement |
| Local durability | Encrypted SQLite + EF Core; Mutation Journal written in the same local transaction as the state change |
| Server change feed | PostgreSQL transactional change journal (replication position) |
| Business events | Transactional Outbox (never merged with the change feed) |
| Row concurrency | Opaque entity-version token: detection only, never a cursor |
| Conflicts | Stale detection → evidence → generic resolvers (non-critical only) → domain policy → server re-validation. Insufficient evidence ⇒ Conflict/NeedsAttention. Never guess. |
| Finance | Append-only ledger + reversal commands; no in-place mutation of posted facts |
| Inventory | Movement-based stock + server settlement policy; never absolute offline overwrite |
| Scheduling | Transactional resource allocation, not a row-version check alone |
| Notifications | Self-hosted SignalR hints + mandatory anti-entropy pull. Never authoritative. |
| Tenancy | App authorization + tenant-aware data access (mandatory) → PostgreSQL RLS (hardening) |
| Security | Three independent planes: Authentication, Authorization, Entitlement |
| Offline authorization | Signed, device-bound capability snapshot with per-capability expiry |
| Reliability | Transactional Outbox, Idempotent Consumer, Competing Consumers, bounded Retry/Bulkhead/Throttling. Every async effect is at-least-once with an idempotent receiver. |
| Files | Bytes and SQL metadata are two consistency domains; resumable transfer; immutable objects; Valet Key access |
| Runtime Internet | None required on any data-plane path |
| Observability | Local and layered; fully functional with zero external connectivity |
| Recovery | Declared SLO + RTO + RPO; ≥2 backups, ≥1 off-host and immutable; one Recovery Point Manifest |
| Scale-out | Gated escape hatch (§16.2). Not required at launch. |
| Main claim | No silent data loss, no duplicate business effect, deterministic conflict outcome, eventual convergence after all mutations settle |

### 0.1 The most important decision

> **Generic state replication and business-intent settlement are different pipelines. Never blur this boundary.**

Patients, catalogs, prices and reference data are **replicated state**: flat,
denormalized contracts with no business meaning in transport. Money, stock,
scheduling commitments and period close are **business intent**: durable,
idempotent commands settled on the server. Ask first: *"is this state replication
or business intent?"* The answer selects the whole pipeline, not one endpoint.
The pipelines **may share transport and local orchestration, never semantics.**

### 0.2 The second most important decision

> **The server is the single global truth. Clients hold provisional local truth. Convergence is policy-driven, not generic.**

Offline action is a product requirement. Nothing done offline is globally true until
the server settles it. "Eventual convergence" means convergence to server business
state under explicit domain policy. It is **not** a CRDT-style claim, because money,
stock and scheduling carry invariants a generic merge cannot decide.

### 0.3 Three kinds of truth (never conflated)

```text
1. Server Business Truth      = globally settled business state
2. Server Replication Truth   = ordered change position used to DELIVER changes (transport position, not business sequence)
3. Client Provisional Truth   = local materialized state + durable unsent intent
```

Pairs that are **never collapsed** (full list §22):

```text
State replication        ≠ business intent settlement
Sync cursor (ChangeSequence) ≠ entity version ≠ OperationId
Transactional change journal ≠ domain event / Outbox
Notification             ≠ synchronization
Idempotency              ≠ ordering
Concurrency detection    ≠ conflict resolution
Provisional state        ≠ posted/settled state
Authentication ≠ authorization ≠ entitlement
Backup exists            ≠ restore proven
```

### 0.4 What "10/10 correctness" means

No distributed offline system can prevent all conflicts. Animora guarantees
conflicts are **never silently resolved incorrectly**. Target negatives:

```text
No silent data loss                     No destructive overwrite of unsent work
No duplicate financial/business effect  No implicit resurrection of deleted records
No cross-tenant data exposure          No permanent unknown settlement
No invalid financial posting           No unbounded retry loop
No sync cursor skip                    No notification-dependent correctness
No tenant can monopolize the runtime   No full resync may destroy the only good local replica
```

Valid outcomes: reject, defer, compensate, or require human review. **Silent
corruption is not valid.** These are design invariants, not a measured score. Eventual
convergence needs restored connectivity, valid authority, compatible contracts,
durable storage and settlement or explicit conflict disposition. It cannot promise
instant visibility across a partition, or recovery of a destroyed never-backed-up disk.

---

## 1. Architecture drivers

Each decision traces to a driver. The table is the **fitness-function contract**:
a change must re-check every row it touches.

| # | Requirement | WAF pillar | Tactic | Fitness check |
|---|---|---|---|---|
| R1 | Windows + macOS + Web from one core | Operational Excellence | Shared Domain/Application/Contracts kernel; split presentation | A business-rule change edits one shared module, not three UIs |
| R2 | High performance on a weak single server | Performance | Client-side compute; delta-only sync; static-first web; bounded background work; workload isolation | Target hardware meets the committed budget (§16) at p95/p99 |
| R3 | Secure, reliable, correct | Security + Reliability | Defense-in-depth tenancy; layered AuthN/AuthZ/entitlement; idempotent commands; outbox; encrypted local store; protected keys | Gates §19 pass |
| R4 | Offline-first desktop, long offline window | Reliability | Durable Mutation Journal; checkpointed pull; explicit settlement states; explicit offline capability | Survives disconnect/crash/reconnect (§19) |
| R5 | Multi-user per clinic, correct convergence | Reliability + Correctness | Versioned state; patch/rebase only where provable; domain conflict policy; settlement; append-only ledger | Two offline devices converge to one correct financial truth |
| R6 | Financial correctness | Integrity | Commands, idempotency, ledger, settlement, period policy | Duplicate / lost-response / period-close tests pass |
| R7 | Inventory correctness | Integrity | Movements + settlement policy + resource constraints | Oversell / concurrency tests pass |
| R8 | Multi-tenancy, subscription/plans | Security + Cost | Shared-DB tenancy with layered isolation; server-derived tenant; independent Subscription/Licensing | Cross-tenant access blocked by a mandatory layer with RLS OFF; no path reaches tenant data outside tenant-aware infrastructure (§9.2) |
| R9 | Direct server DB maintenance propagates to clients | Operability | Journal makes governed DML sync-visible; governed maintenance contract | Gate Y |
| R10 | Near-real-time collaboration | Responsiveness | SignalR hints + checkpointed pull + UI invalidation. Transport never authoritative. | §1.1 latency; disconnected clients catch up (§8.17) |
| R11 | **Zero external-Internet dependency at runtime** | Reliability + Security | No mandatory third-party runtime system; local identity, PKI, observability; packaged assets | Isolation tests (§17.4) |
| R12 | Single node is intentional | Cost + Simplicity | Modular monolith; no broker/cache at launch; explicit DR | Restore drill passes |
| R13 | Mixed client versions | Operability | Contract versioning; capability negotiation; N/N-1/N-2 | Gate M |
| R14 | Long-lived tenant data | Maintainability | Retention policies; projections; migration discipline | Data-growth and restore tests |
| R-DR | Declared recoverability on one node | Reliability | SLO + RTO + RPO + max offline-command age; ≥2 backups, ≥1 off-host + immutable; tested restore | Gate X |

**[INVARIANT — single node is an owned SPoF.]** One node is a single point of
failure. It is a valid cost decision **only** because R-DR states its blast radius
(RTO/RPO plus tested restore). Single-node is never claimed to be highly available.
Redundancy is the escape hatch (§9.1, §16.2).

### 1.1 Launch targets

Architecture targets (accept or adjust by ADR before GA). **Performance/recovery
targets, not correctness assumptions.** Published capacity comes from measurement (§16.3).

```text
Max offline window:                     30 days
Journal retention floor:                offline window + 15 days (minimum)
RPO:                                    <= 5 min
RTO:                                    <= 60 min
Change visibility (healthy server):     p95 <= 1 s, p99 <= 3 s (server commit → B's rendered refresh)
Command settlement (online):            p95 <= 500 ms, excluding local/DB contention
Resource isolation:                     interactive traffic protected from background work
```

Retention floors move together as **one linked decision (ADR-17):**

```text
JournalRetention ≥ MaxOfflineWindow + Margin
TombstoneRetention ≥ MaxOfflineWindow + Margin
AuthorizationRevocation ≥ MaxOfflineWindow + Margin
Contract/License retention must stay compatible with the same window
```

---

## 2. Guiding principles

Resolve ambiguity with these when no explicit rule applies.

1. **Server = only global business authority.** Settlement, conflict resolution and entitlement are server decisions. Never a client, never a majority of clients.
2. **Replication and business intent are different flows.** They may share transport, never semantics (§0.1).
3. **Minimize coordination.** No per-keystroke round trips to a shared coordinator.
4. **Make illegal states unrepresentable.** Financial facts are append-only; inventory is movement-based; contended allocation is transactional; "set absolute value from a stale client" is impossible.
5. **Use the database for what it proves well** (unique keys, FKs, transactions, version tokens, RLS, change tracking). Never the *only* security boundary (§9.2).
6. **Deltas and commands, not dumps.** Never a bulk re-send "to be safe."
7. **Every async effect is at-least-once; every receiver is idempotent.** "Dispatched" ≠ "delivered exactly once."
8. **Every retry has a bounded budget** (attempts, elapsed time, jitter, per-tenant and global limits). No infinite loops.
9. **A notification is never authoritative.** Missing one must never lose data; fall back to checkpointed pull.
10. **A checkpoint advances only after durable local application.**
11. **A full resync is additive/reconstructive, never destructive to unsent work.** Build in a shadow replica; switch atomically.
12. **Defense in depth, with one clearly owned mandatory layer.** Each security/tenancy concern has exactly one layer that is mandatory and sufficient. Hardening is never mistaken for it.
13. **Fail closed, never fail silent.** Every degradation has a defined, visible outcome. "It just didn't sync" is never acceptable for financial effects.
14. **Tenant scope comes from server-side authenticated membership, never client input.** Client-side authorization is UX only.
15. **Critical direct SQL is governed**: audited, and either sync-visible (critical tables) or forbidden (business invariants).
16. **Read models may lie a little; write models never do.**
17. **Observability is local and complete without Internet.**
18. **Every named pattern must earn its place**: a driver in §1 requires it, cited at point of use (§24).
19. **Draw boundaries where change happens independently**, not "for future-proofing."
20. **Prefer one strong single-node transaction to distributed choreography.** No infrastructure for problems measurement has not shown.
21. **A conflict is an expected state; silent corruption is a defect.**

---

## 3. C4 Level 1 — System context

```text
Clinic Staff (Desktop = tenant-scoped, one clinic = one tenant | Web)
        │
        ▼
   ┌──────────────────────────────┐
   │ ANIMORA                      │   ◀── Platform Operator (vendor; operator surface of Subscription, §13.3)
   │ vendor-operated single       │
   │ multi-tenant SaaS node       │
   └──────────────────────────────┘

Separate vendor-side system:
Public Marketing/Subscription Website ──(async, out-of-band, versioned onboarding contract; NOT a runtime dependency)──▶ Subscription context
```

- **Clinic Staff**: reception, cashier, doctor, inventory, manager. Distinguished by RBAC + policy (§10).
- **Clinic Owner/Manager**: a tenant role, not a separate backend.
- **Platform Operator**: vendor staff with controlled cross-tenant authority (subscription, licensing, support). An operator-facing surface of Subscription (§13.3).
- **Public Website**: the commercial front door. Customer registers, picks a plan, pays, downloads. It is an independent system, linked only by a narrow versioned onboarding contract (§13.4). It never shares a DB and is never on the clinic path. This keeps R11 true.
- **No other external system** participates in any runtime path. The absence is stated on purpose (R11).

---

## 4. C4 Level 2 — Containers

```text
Desktop Client (native, per device)
  Presentation (native MVVM) · Local Application Core · Encrypted Store (SQLite+EF Core)
  Mutation Journal · Business Command Queue · Sync Checkpoints · Conflict Inbox
  Sync Coordinator · Secure Device Storage (OS-protected keys, device identity)

Web Client (browser)
  Presentation: static SSR default + small interactive islands · no durable local replica

Both ──HTTPS/LAN──▶ Animora Server Process (single deployable), in dependency order:
  Authentication / Authorization
  Business API (commands, queries, reports)
  Critical Sync API (custom state replication)
  Datasync Compatibility Surface (SYNC-SIMPLE only)
  Notification Hint Surface (optional, non-authoritative)
  Operator / Management APIs
    ──▶ Application/Domain core (bounded contexts, §6)
    ──▶ Background Workers (outbox relay, journal reader, purge, reconciliation, entitlement checks)
    + Reporting / read projections + Infrastructure (data access, file store, crypto, PKI, clock)
    ──▶ Relational DB (tenant data, ledger, outbox, audit, change journal) + File Store (bytes; metadata in DB)

Management plane (out-of-band, never on clinic runtime path):
  provisioning · license issuance · software update · backup export · support
```

| Container | Responsibility | Owns |
|---|---|---|
| Desktop Client | Offline native presentation, local data, sync and command origination | Local encrypted replica, Mutation Journal, pending commands, device identity |
| Web Client | Online-only thin presentation | Nothing persistent |
| Business API | Command intake, queries, reports | Command and query contracts |
| Critical Sync API | Custom replication (push/pull) for SYNC-CRITICAL state | Checkpoints, replication contracts |
| Datasync surface | Library replication for SYNC-SIMPLE only | Datasync controllers and wire format |
| Notification hints | Non-authoritative "scope changed" hints | Hint transport |
| Application/Domain core | Business rules, invariants, workflows | Domain model, context boundaries |
| Background workers | Outbox relay, journal reader, retries, purges, reconciliation | Claims and leases |
| Reporting | Read-only aggregates | Projection state + freshness watermark |
| Infrastructure | Data access, file storage, crypto/PKI | Cross-cutting concerns |
| Database | System of record: tenant data, ledger, movements, outbox, audit, change journal | — |
| File store | Binary content, addressed by hash | File bytes |

### 4.1 Surface invariant

**[INVARIANT]** The following are distinct entry points into one Application/Domain
core. None writes business tables directly, except a governed maintenance operation (§20):

```text
Business API             /business/*      semantic commands and queries
Critical Sync API        /sync/*          custom state synchronization
Datasync adapter         /tables/*        SYNC-SIMPLE compatibility only
Notification hints       /notifications/* optional, non-authoritative
Operator / management    /operator/*
```

Route names may differ. The **semantic separation may not** (§97). OData is a Datasync
detail only (§98).

---

## 5. C4 Level 3 — Components: Clean/Onion + Ports & Adapters

**[PATTERN] Clean/Onion Architecture** (Martin/Microsoft .NET guidance shape):

```text
Presentation (Business API · Sync API · Datasync surface · Hints · Operator APIs)
    │ maps transport → application calls; no business logic
    ▼
Application (use cases, command/query handlers, workflows, idempotency, authZ orchestration,
             sync coordination ports, settlement + conflict orchestration, transaction boundaries)
    │ orchestrates domain; owns application ports
    ▼
Domain (aggregates, entities, value objects, invariants, domain services, domain events)
    │ zero outward dependencies
    ▲ implemented by
Infrastructure (EF Core, PostgreSQL, journal adapter, Datasync adapter, SQLite adapter,
                encrypted store, file store, Data Protection, PKI, outbox dispatcher, notification adapter, clock, key storage)
```

**[INVARIANT] Dependencies point inward only.** Domain depends on nothing. Application
depends only on Domain. Infrastructure and Presentation depend inward.

**Domain knows nothing about:** HTTP, EF Core, Datasync, PostgreSQL, SQLite, Blazor,
Avalonia, tokens, file paths, notification tech, or the journal mechanism. So the sync
engine sits behind an abstraction. A replication library or journal reader is an
Infrastructure concern implementing a Domain/Application port.

**[PATTERN] Ports & Adapters.** Every external capability is a port implemented by an
adapter. Ownership follows the capability:
- Business-meaningful ports (e.g., repositories) may be Domain-owned.
- Technical or orchestration ports (**the sync engine especially**) are **Application-owned, never Domain-owned** (sync is not a business context).

```text
IClock                    ITransaction / IUnitOfWork
IStateSyncEngine          IChangeFeed
IMutationStore            IBusinessCommandTransport
IFileStore                IAuditSink
INotificationHintChannel  ISecureKeyStore
IEntitlementProvider
```

Replacing or forking a third-party dependency (Datasync, or the journal reader) never requires a Domain rewrite (§8.1, §13).

---

## 6. Bounded contexts & context map (DDD-lite)

**[PATTERN] DDD bounded contexts inside a modular monolith.** Logical module
boundaries in one deployable, not microservices. Each context owns its aggregates
and exposes only explicit contracts (commands, queries, events).

| Context | Owns | Publishes | Depends on |
|---|---|---|---|
| **Identity** | Users, credentials, devices, roles | `UserAuthenticated`, `DeviceRegistered`, `CredentialChanged` | — |
| **Tenant/Clinic** | Tenant, clinic profile, membership | `TenantProvisioned`, `MembershipChanged` | Identity |
| **Patient/Owner** | Patients, owners, contacts | `PatientCreated`, `OwnerChanged` | Tenant |
| **Clinical** | Medical history, vaccination, visits | `VisitRecorded`, `VaccinationDue` | Patient |
| **Scheduling** | Appointments, resources, allocations, boarding slots | `AppointmentBooked`, `AppointmentMoved` | Patient |
| **Sales/POS** | Cart, checkout, cashier sessions | `SaleCompleted`, `CashSessionClosed` | Patient, Inventory (query) |
| **Inventory** | Products, lots, movements, derived quantity-on-hand | `StockReceived`, `StockAdjusted`, `LowStockDetected` | Tenant |
| **Finance** | Journal, invoices, payments, cheques, expenses, periods | `InvoicePosted`, `PaymentSettled`, `PeriodClosed` | Sales, Inventory (events) |
| **Reporting** | Read-only aggregates and projections | *(no writes)* | Finance, Inventory, Clinical (read) |
| **Files** | File lifecycle, metadata, storage reconciliation | `FileAvailable`, `FileQuarantined` | Tenant |
| **Subscription** | Plans, seats, billing periods, grace states | `PlanChanged`, `SeatLimitReached`, `SubscriptionSuspended` | Tenant |
| **Licensing** | Signed license verification, installation binding | `LicenseValidated`, `LicenseExpiring` | Subscription |
| **Audit** | Append-only audit of security and finance actions | *(sink; subscribes to all)* | all (read) |
| **Synchronization** *(infra)* | Checkpoints, replication contracts, diagnostics | *(infra-facing)* | Application ports of syncable contexts |
| **Notification** *(infra)* | Delivery hints only | *(infra-facing)* | Sync state |

Synchronization and Notification are **not** business contexts. They are
infrastructure capabilities (§8, §44) that syncable contexts opt into through a port.

### 6.1 Context coupling rules

1. Finance never edits Sales or Inventory records, and never calls them synchronously to *decide* an outcome. It reacts to published events (`SaleCompleted` → draft invoice) or invokes an explicit Application workflow.
2. Reporting never writes business state (CQRS-lite, §11.3).
3. Audit is a pure sink. It never publishes back and nothing waits on it.
4. Synchronization never decides business policy.
5. Notification never owns state.
6. Identity never grants entitlement. Entitlement never authenticates.
7. Tenant context never comes from a client payload (§9.2).
8. Cross-context writes are either atomic in one Application transaction, or explicit eventual consistency with a **visible intermediate status** (§6.2).

### 6.2 Cross-context financial consistency

**[DECISION]** One process and one database: cross-context coordination is **not** a
distributed transaction. Each financial interaction declares **atomic** (one DB
transaction across aggregates via an Application workflow) or **eventual** (event +
outbox with a visible provisional state). Matrix: §38.

**[INVARIANT — no silent financially incomplete gap.]** A not-yet-posted state is an
explicit visible status (e.g., `PendingPosting`). Never a window where a sale looks
complete while Finance has no record and nothing says so (A-RULE 038). An Application
workflow may coordinate several aggregates in one transaction, each keeping domain
ownership. This is not a saga layer (§23).

---

## 7. Multi-platform client architecture

### 7.1 Shared kernel vs. split presentation

**[DECISION]** Two presentation technologies (desktop and web) is a deliberate boundary.

```text
SHARED (single source of truth):
  Domain · Application · Contracts · validation rules · authorization policy definitions
  · localization/formatting · sync ports · DTOs · error codes · command definitions · file-access contracts

SPLIT (presentation only):
  Desktop: native, offline-capable host
  Web:     server-rendered, online-only host
```

**[INVARIANT — shared authorization code is not a shared authority.]** Sharing
policy definitions prevents drift. Client evaluation is **UX only** (hide a button,
pre-validate). A client can be compromised, so **no security invariant may depend on
client-side policy execution**. The server re-evaluates every decision, including
resource-level checks on the real instance (§10.3). *Client says "probably allowed";
server says "allowed."*

**Rationale.** The desktop must be a genuine native offline process (R4, no browser
runtime in the offline path). The web host is tuned for near-zero per-client footprint
on a weak server (R2). Forcing one technology would sacrifice R2 or R4 for code reuse.
A native offline host and a stateless web renderer change for different reasons (§2.19).

### 7.2 Client roles

| Client | Role | Local persistence | Offline | Talks to |
|---|---|---|---|---|
| Desktop | Full offline workstation | Encrypted replica + Mutation Journal + durable command queue | Full, governed (§8, §10.4) | Critical Sync + Business API + SignalR hints (optional Datasync) |
| Web | Thin, always-connected | None | None (non-goal, §23) | Business API (+ sync-backed queries where useful) |
| Admin/Operator | Subscription oversight | None | None | Subscription context only (§13) |

**[INVARIANT]** A web session never assumes a local write cache survives reload.
Every web mutation is a synchronous Business API call. No local queue.

### 7.3 Interaction-pattern routing (web)

**[DECISION]** Route web work by **interaction frequency**, not page type:

```text
Simple read / occasional filter        → static (no-interactivity) render
Per-keystroke search/filter            → small stateful interactive island
Large or typeahead query               → dedicated, debounced, paged query endpoint
Data-heavy or mutating operation       → Business API call, always
```

Rendering technology is a `STACK.md` concern. Detail: §46.

### 7.4 Desktop composition

```text
Native UI (MVVM) → Shared Application Core → Local Repository → Encrypted SQLite/EF Core → Sync Coordinator
```

**[INVARIANT] No browser/WebView in the desktop offline path** (§47).

---

## 8. Offline-first & synchronization (the heart)

The most safety-critical part. Three synchronization mechanisms that **never exchange
semantics implicitly**, on a durable local Mutation Journal with explicit settlement.

### 8.0 The three mechanisms

```text
A. Critical state sync (correctness authority)
     Animora Custom Sync Engine → PostgreSQL transactional change journal
B. Simple replicated state (contained convenience adapter)
     CommunityToolkit.Datasync — ONLY for explicitly classified SYNC-SIMPLE entities
C. Business intent settlement (durable, idempotent)
     Business Command Pipeline → transactional server commit (server-authoritative)
```

A and B replicate state. C is business intent (§0.1). A is authority for all
collaborative and high-integrity state. B is a bounded escape hatch. C is separate.

### 8.1 Sync engine as an isolated Infrastructure adapter

**[PATTERN] Ports & Adapters / Anti-Corruption Layer.** Sync is not a Domain
capability. Its port is Application-owned. Both the custom engine and Datasync sit behind it.

```csharp
public interface IStateSyncEngine
{
    Task<SyncResult> SynchronizeAsync(SyncScope scope, CancellationToken ct);
}
```

**[INVARIANT]** Domain and Application never reference a replication library's
types. Domain has no sync abstraction at all. This keeps the technology swappable (a), the
domain graph clean (§5, b), and a custom engine a pure Infrastructure change (c).

### 8.2 Why a third-party sync library is NOT the universal mechanism

**[DECISION — authoritative core correction.]** Datasync maintains its own queue and a
delta token derived from the **maximum `UpdatedAt`**. Its offline context refuses pulls
for an entity with pending operations. Its resync guidance **removes pending operations**.
That suits simple CRUD. It is not sufficient as correctness authority for critical
collaborative state, because:

- A **wall-clock `UpdatedAt` cursor is unsafe** under concurrent commits, clock skew, retries and paging. Rows written in the same tick, or committed out of timestamp order, can be silently skipped.
- Its client-wins/server-wins resolvers work on whole entities; auto-resolution is off by default. Custom `IConflictResolver` hooks cannot supply Animora's base-history evidence, atomic receipts, or money/stock/scheduling invariants (C01).
- Its resync **deletes pending operations**. Unacceptable for unsent business intent (§11, A-RULE 023).

```text
Datasync            = adapter (containment, not rejection)
Animora Sync Engine = architecture authority
```

### 8.3 Synchronization classification

Every transport surface gets exactly one class. Classify **read-state replication**
separately from **write-intent submission**. An appointment or posted invoice can have a
SYNC-CRITICAL read contract while its mutations are COMMAND-ONLY. This never permits
generic CRUD of settled facts.

```text
SYNC-CRITICAL   Custom Sync Engine + transactional change journal
SYNC-SIMPLE     Datasync adapter (only if it qualifies)
COMMAND-ONLY    Business Command Pipeline; never CRUD sync
PROJECTION-ONLY Server-derived read model; never client-authored
NOT-SYNCED      Local-only / device-local
```

**SYNC-CRITICAL** (A): patient/owner records, clinical mutable metadata, appointment
state, draft documents, operational assignments, inventory operational projections,
financial documents that must be visible offline. Inclusion is per entity or aggregate.

**SYNC-SIMPLE** (B): reference catalogs, low-risk lookups, UI preferences, static config.
Datasync is allowed **only if all** hold:
- timestamp-cursor collision safety passed its technology PoC (`STACK.md` gate), AND
- conflict semantics are genuinely whole-row compatible, AND
- no critical unsent work depends on its pull semantics, AND
- the entity has no meaningful concurrency concern.

If any fails → SYNC-CRITICAL. **Launch default:** the journal path serves every
server-shared replica unless an explicit entity qualification is accepted. No feature
adds a second adapter just because it is low-risk. Device-local preferences stay
NOT-SYNCED. Datasync is an optional retained seam, not speculative second
implementation, and not a dependency of patient sync.

**COMMAND-ONLY** (C): `RegisterPayment`, `PostInvoice`, `ReversePayment`, `AdjustStock`,
`BookAppointment`, `CloseCashSession`, `ClosePeriod`, … Never generic record CRUD.

### 8.4 Replicated-entity contract (state, not intent)

Each syncable entity exposes a client contract (architectural, not technology):

```text
Id              globally unique, stable across replicas
ChangeSequence  optional journal position; NOT a unique row or page id. A transaction
                may produce several journal entries; initial rows may have none.
                ONLY a completed batch's ToVersion (never a row's max) advances a checkpoint (§17-CT).
UpdatedAt       server-stamped display/audit timestamp; NEVER a critical cursor, NEVER an ordering source
Version         opaque concurrency token (entity-version token); changes on every server write; detection only
Deleted         soft-delete flag (tombstone); never a hard delete in transit
```

**[INVARIANT — the critical cursor is `ChangeSequence`, never `UpdatedAt`.]** The
checkpoint holds the journal **completed-range watermark** (`ToVersion`, opaque
`ChangeSequence`) bound to feed epoch and scope (§17-CT). It is **non-decreasing within one
epoch**, not strictly increasing per row. Empty polls may repeat it; one transaction
may change many rows. It is never a wall clock. Timestamp delta pull is **never** safe under
concurrent writes, retries, paging, disconnects and skew. (SYNC-SIMPLE Datasync uses its own
`UpdatedAt` cursor, gated by §8.3.)

```text
Batch.ToVersion = 8471932            ← checkpoint AFTER a complete durable batch
UpdatedAt       = 2026-10-08T08:31Z  ← display/audit only
Version         = <opaque token>     ← row concurrency, not ordering
```

**[INVARIANT]** Server write models are never exposed to clients. Each synced type has
a separate client-facing contract, so server-only fields never leak and the server
can reshape internally.

**[INVARIANT]** `ChangeSequence`, `Version` and the replication operation id are
transport bookkeeping only. They order **delivery**, not **business settlement** (§8.9).

**[INVARIANT — no destructive sync.]** An inbound pull never overwrites unsent business
intent. Drafts, pending payments, in-flight stock transactions and pending appointment
edits are reconciled, never clobbered (A-RULE 017).

### 8.5 Local materialized state & Mutation Journal

The desktop DB is a **replica, not an independent authority**:

```text
MaterializedState   LocalReferenceData   PendingMutationJournal
BusinessCommandQueue  SyncCheckpoints   ConflictInbox
DeviceState         LocalAudit/Diagnostics
```

**[DECISION]** Every offline mutation that must survive a crash is recorded in an
Animora-owned Mutation Journal, even when the transport is Datasync.

```text
MutationId · MutationKind · EntityType · EntityId · AggregateId · BaseVersion ·
Patch / CommandPayload · ChangedFieldSet · CausationId · DependsOnMutationId ·
LocalSequence · CreatedAtUtc · Status · AttemptCount · LastErrorCode · LastErrorAt

MutationKind ∈ { StatePatch, BusinessCommand, DatasyncOperation }
```

#### 8.5.1 Atomic local mutation

**[INVARIANT] Local state and its journal record commit in ONE local transaction.**

```text
BEGIN LOCAL TRANSACTION
  1. validate locally
  2. update local materialized state
  3. insert mutation-journal record
  4. persist provisional status / local invalidation generation
COMMIT   (all or nothing)
AFTER COMMIT: notify UI via Contracts; never publish an uncommitted save
```

This removes both "UI says saved but the queue lost it" and "queued but local state never committed."

### 8.6 Business command architecture (intent, not state)

**Flows here:** post an invoice, register/reverse a payment, receive/adjust stock,
close a cash session, book/move an appointment, close a period, approve a discount,
issue a credit note. Never generic CRUD. Commands are **small and semantic**:
`RegisterPayment` not `UpdatePayment`; `AdjustStock` not `SetQuantity`;
`BookAppointment` not `UpdateAppointmentSlot` (§95). The command states *intent*.

**Desktop canonical flow:**

```text
User action
 → local validation
 → atomic local transaction (durable, provisional) + Mutation Journal record
 → durable PendingCommand
    ├─ connected    → submit to Business API
    │                  → server: authorization → concurrency → invariants → transactional commit
    │                  → Accept / Reject / Conflict → settle locally
    └─ disconnected → stays queued; submitted with priority on reconnect (ahead of routine sync)
```

Each command carries:

```text
OperationId · CommandType · AggregateId · Payload · CommandFingerprint · CausationId ·
DependsOnOperationId · ExpectedAggregateVersion (where applicable) ·
CreatedAtUtc (client-local, informational) · LocalSequence · Status
```

**[INVARIANT — identity is server-derived.]** `TenantId`, `UserId`, `DeviceId` and the
authorization context are resolved **server-side** from the authenticated, device-bound
credential, on every command. They are never read from the payload (same as TenantId
in sync writes, §9.2). A client may stamp an informational local copy, which the server
ignores. `DeviceId` binds to the registered credential or key, not a self-declared field
(§10.4). Codified as A-RULE 036.

**Why a command queue is separate from the replication queue.** Accounting needs
deterministic idempotency, authorization, aggregate-version validation, transactional
posting and explicit settlement, which a generic replication queue does not provide.
Merging them would let replication reordering silently become financial reordering.

#### 8.6.1 Operation identity & idempotency

**[PATTERN] Idempotent Consumer** (Azure Architecture Center). Durable idempotency
record per command so an at-least-once retry never re-applies a financial effect.

```text
OperationId · CommandType · FirstSeenAt · LastAttemptAt · Status ·
ResultHash/ResultReference · AttemptCount · LastErrorCode · CommandFingerprint
```

**[INVARIANT] Unique key `(TenantId, OperationId)`.** Same `OperationId` **and identical
`CommandFingerprint`** → return the stored result, never a second effect.

**[INVARIANT] Same `OperationId` with a different `CommandFingerprint` is an integrity
error, not a replay.** Detect, audit, reject. "Operation exists → return old result" is
valid **only** for an identical **versioned canonical command envelope**. The client
persists the immutable envelope. The server recomputes the hash over command type,
contract version, target, payload and dependency/precondition fields, excluding tokens
and transport headers. JSON property order and credential refresh do not change the
fingerprint. After submission, any payload, base-version or dependency edit needs a **new
operation** that references the old one. Never mutate an unknown operation. Stronger than a simple "already seen" cache.

**[INVARIANT — receipt and effects are one atomic unit (S04/S05/S11).]** The server
inserts the unique `(TenantId, OperationId)` receipt, business state, mandatory audit and
outbox, and the final outcome in **one SQL transaction**. Concurrent duplicates are
arbitrated by the unique constraint, not an in-memory check. The losing request rolls back,
loads the committed receipt in a fresh context, checks fingerprint and caller authority, and
returns that outcome. A receipt never discloses another user's result. `InProgress` never
means settled; durable async admission needs a lease/recovery owner. Apply the same receipt
protocol to critical creates/patches using their stable `MutationId`, so a lost create
response cannot make a second patient or collide silently with an identity.

EF execution-strategy retries replay the **whole transactional delegate** with stable IDs and
fresh scoped contexts. An uncertain commit is verified by reading the receipt on a fresh
connection before re-executing. No notification, file write or external effect runs inside that
retry delegate. MARS is disabled where savepoint recovery is relied on. Cross-context atomic
writes share one connection and transaction (§6.2), not separate `SaveChanges` calls assumed atomic.

**Receipt lifecycle.** Financial receipts (ID, fingerprint, terminal outcome/reference, recovery
epoch) stay durably associated with the retained business/audit fact. A short cache TTL must
never reopen settlement. Response bodies may be compacted, but late replays get the outcome or
an archived-result reference, never execute as unseen. Non-financial receipts and inbox
retention exceed every permitted offline, retry, local-backup-restore and operator-redelivery
window. Expired operations need explicit reconciliation, never blind resubmission. Consumer
dedup key: `(TenantId, ConsumerName, EventId)`, committed atomically with local effects.

**[INVARIANT] Idempotency does not solve ordering.** At-most-once effect for an identical
command says nothing about *when* it applies relative to others (§8.9).

#### 8.6.2 Settlement state machine

```text
Draft → LocallyCommitted → PendingSettlement
                             ├── Settled
                             ├── Rejected
                             ├── Conflict
                             ├── NeedsAttention
                             └── SettlementUnknown
```

**[INVARIANT — "Posted / Settled / Closed" are server words, never client-local states.]**
The pre-settlement local state is `LocallyCommitted`, never "LocallyPosted." *Posted* means an
irreversible server-settled ledger fact, and no client-local state may use that word in any label,
event or report (A-RULE 038). Settlement state is separate from any business lifecycle status: it
answers "did the server accept this intent," not "what phase is this visit."

**[INVARIANT — a reversal is a new command, not a terminal state.]** A reversal references the
original `OperationId` and is a new settled command with its own audit. The original stays
`Settled` forever (§11.1, §35.2).

#### 8.6.3 SettlementUnknown (lost response)

**[INVARIANT] A lost response never creates a new OperationId.** Scenario: client sends `Op123`,
server commits, response lost. The client enters `SettlementUnknown` and **must not** mint a new
ID. It retries or queries the **same `Op123`**, and the server returns the original result. This
gives at-most-one business effect under at-least-once transport (§19 Gate D).

#### 8.6.3a Outcome lookup, deferred work, terminality

**[DECISION] The receipt is the linearization point, not an HTTP status.** Responses carry the
operation id, current feed epoch and a typed disposition. Terminal results have a durable result or
reference. A receipt version is never a replication checkpoint.

| Disposition | Meaning and client transition |
|---|---|
| `Settled / Rejected / Conflict` | Durable terminal outcome for this immutable envelope. Persist locally atomically. Resolution creates a **new** identity linked to the original. |
| `Admitted / Processing` | Durable job exists, not settled. Status reference + bounded poll guidance. **`202` never means `Settled`.** |
| `DeferredDependency / RetryLater` | Non-terminal. Same identity may retry later. 429, deadlock or dependency delay is not business rejection. |
| `NotObservedInCurrentEpoch` | No committed receipt visible. A concurrent submission may still execute. Re-submit only the same immutable identity/envelope; SQL uniqueness arbitrates. |
| `RecoveryRequired / OutcomeUnavailable` | History continuity absent or caller lacks access. Quarantine for reconciliation. **Never a new payment identity.** |

Launch commands settle **synchronously in bounded SQL transactions**. Durable async admission is
reserved for explicitly classified long work: admission + identity/fingerprint commit before `202`;
terminal receipt + effects commit later. In-memory enqueue alone is never `Admitted`. A missing
prerequisite defers **without holding a DB transaction**. A permanently failed prerequisite gives a
reasoned dependent disposition. Bound dependency count and depth; reject cycles across both pipelines
before admission (§8.9).

`SettlementUnknown` is retained until authorized evidence establishes the outcome. "No permanent
unknown" means monitored, owned reconciliation, **not** resolving an outage before contact returns.
Expose age, last attempt and recovery owner. Revoked actors cannot read stored results, but their
work survives for authorized recovery. Logout, expiry and resync never erase financial receipts.

#### 8.6.4 Sync-failure UX contract

**[INVARIANT] No bare "Sync failed" for a critical command.** Whenever a command cannot settle, the
user sees an answer to:

```text
What failed?   Why?   Financial/business effect?   What should the user do?   Who must fix it?
```

### 8.7 Logical sync contract (technology-independent)

Client state: `SyncCheckpoint`, `SyncProfileVersion`, `PendingMutationJournal`, `ConflictInbox`.
Session steps:

```text
1.  Authenticate / refresh credential once, single-flight gate
2.  Validate device / capability / contract / server feed epoch
3.  Establish authorized scope/profile and hint subscription
4.  Reconcile unknown settlements (SettlementUnknown → query server)
5.  Schedule bounded eligible push by dependency (§8.9)
6.  Pull immutable server delta batches fairly; never wait for all push to succeed
7.  Stage all batch pages; validate completeness and relations (§17-CT)
8.  Atomically apply server base + rebase/conflict decisions + checkpoint
9.  After local commit, invalidate affected UI queries through Contracts
10. Continue catch-up fairly; hints and polls coalesce into the same coordinator
```

HTTP route names are not an architectural dependency.

### 8.8 Journal vs. Outbox (two feeds, never merged)

```text
transactional change journal : which replicated STATE rows changed?   (delivery position)
Outbox                       : which domain EVENTS/effects did a transaction produce?
```

A business command's one transaction writes state + audit + outbox event. At commit the
journal also records the affected synced rows. The feeds are **never merged** (A-RULE 029, 038/039).
A client sees "invoice projection changed" via state sync. It does not see the internal `InvoicePosted`
event unless a separate contract requires it (§94).

### 8.9 Two independent transports; explicit dependency ordering

**[INVARIANT]** Replication queue and command queue are independent. Neither is globally ordered with
the other, beyond what it guarantees, nor across devices or aggregates. **Arrival order is never business order.**

A dependent command or mutation carries explicit causal metadata. Two concepts, **never conflated**:

```text
CausationId           provenance: which operation CAUSED this one (audit)
DependsOnOperationId  settlement precondition: which operation MUST settle on the server first
                      (+ expected aggregate version where known)
```

**[INVARIANT — dependencies form a DAG.]** A cycle is detected and rejected **before** execution,
never deadlocked at settlement. Dependencies may reference both a `StateMutation` and a `BusinessCommand`.
A command with an unsatisfied dependency is **deterministically delayed or rejected**, never accepted blindly,
until the prerequisite has **settled on the server** (not merely sent).

```text
M1  CreatePatient (state mutation)
      ↓
C1  CreateInvoice (Op1)        → settles: server knows invoice aggregate/version
      ↓
C2  RegisterPayment (Op2, DependsOnOperationId = Op1, ExpectedInvoiceVersion = v)

Op1 Settled               → evaluate Op2 (version check → Accept / Reject / Conflict)
Op1 Pending / Unknown     → hold Op2 (deterministic delay) or reject (retriable)
Op1 Rejected              → reject Op2 (its precondition never became true)
Op1 Settled, later reversed by Op3 → revalidate current domain state before Op2;
                                     Op1 stays Settled; the reversal is a new command
```

Resolution is **server-side only**. The client cannot reorder helpfully, and a transport retry cannot
smuggle a dependent command ahead of its prerequisite.

### 8.10 Client mutation scheduler

Work partitions into: *Critical Business Commands · Critical State Mutations · Simple Datasync
Operations · Routine Pull · Maintenance.* Rules:

1. Honor dependency edges (DAG, §8.9).
2. Independent aggregates run concurrently **only when provably safe**.
3. Business commands default to conservative serialization.
4. Datasync parallelism stays at 1 unless an entity is proven order-independent.
5. Local journal order is deterministic.
6. Network arrival order is never business order.
7. **Push never starves pull.** Each round: bounded push (count/time), then an eligible pull. A conflict, poison or unknown operation blocks only its **causal dependents**, not independent records or incoming clinic changes.
8. Per profile: one coordinator owns network scheduling; one local writer owns atomic application. Hints, timers, saves, reconnect and resume wake the coordinator. They never start competing `SynchronizeAsync` loops (§8.17).
9. After the retry budget is exhausted, keep the journal durable and expose its state. Later connectivity, manual action or low-frequency probe starts a new bounded episode. Finite attempts never mean abandoning sync.

### 8.11 Contract versioning & rollout

**[INVARIANT] Every replicated contract carries an explicit `ContractVersion`**, independent of
schema migrations. On a wire-incompatible change, an old client must **detect** "unsupported
contract version" and trigger a controlled upgrade or resync. It never silently mis-maps or partially syncs.

**[DECISION]** Staggered mixed-version fleets are the normal case. Keep at least three client
generations (N / N-1 / N-2) working against one current server (§79, Gate M).

### 8.12 Conflict resolution architecture

**[DECISION — convergence, precisely stated.]** *Eventual convergence to server business state under
explicit domain conflict policy.* Never "strong convergence." A version mismatch means "the row changed
under us." It does not know that two payments exceeding an invoice balance is a business conflict.

```text
1. Detect          BaseVersion != CurrentServerVersion ⇒ "stale" (changed under us, NOT "wrong")
2. Classify        which data type / aggregate / policy applies
3. Gather evidence what the server actually knows (§8.13)
4. Apply policy    field- and aggregate-aware per data type (generic resolvers ONLY for non-critical data)
5. Commit/reject   on settlement the server re-runs authZ + concurrency + invariants → Accept / Reject / Conflict
6. Record audit
7. Rebase local state (echo-safe, §52)
```

Idempotent commands (§8.6.1) ensure a retry after any step never double-applies.

### 8.13 Conflict evidence & the conservative rule

**[INVARIANT] Automatic field-level merge only when non-overlap is *proven*.**

```text
Evidence sources: opaque Version · journal operation · retained server mutation evidence (when implemented)
                  · domain mutation history (where kept) · maintenance-origin metadata
```

The change journal says *which row changed, what operation, at which version*. It is **not** a
field-history system (§16-CT). So:

```text
Client changed Phone; server changed Address; proven disjoint      → merge
Client changed Phone; server changed the same row many times,
  history unavailable                                              → do NOT guess
```

**[INVARIANT — conservative rule.]** If the server cannot prove a stale patch is non-overlapping,
it must not merge silently. Result: `Conflict / NeedsAttention`. This matters most when a row changed
several times between the client's base and current version, or a governed maintenance operation changed it outside the command path.

**Evidence binding (S02/S06).** `BaseVersion` is an opaque UUID, not a journal baseline. It is never a
checkpoint and never compared numerically to a sequence. Default patch evidence includes protected
original values of changed fields (or a retained matching base snapshot), proposed values and current
authoritative values. The server checks supported fields, compares base and current values, and
revalidates aggregate invariants in the **same conditional write transaction**. Where edit-away-and-back (ABA)
matters semantically, matching final values is insufficient. Use retained history or require review. PostgreSQL has
no baseline-relative change mask, so Animora retains explicit server mutation evidence with complete interval
coverage, or escalates. Derived fields and inter-field constraints are not independent just because field names differ.

Never refresh the expected version and retry to bypass a business conflict. Client-supplied original
values are **claims, not evidence**. Bind base evidence to tenant, resource, contract, field set and exact
BaseVersion, using a server-retained snapshot or an integrity-protected server-issued token with a canonical
snapshot hash. Verify before any automatic merge. Missing, expired or unverifiable evidence requires review,
never weaker merging. Authentic evidence still cannot bypass current authority or domain rules.

### 8.14 Conflict-policy matrix

| Data type | Default policy |
|---|---|
| UI preferences / display settings | Last-write-wins acceptable |
| Low-risk reference metadata | Merge or server-wins |
| Non-critical free text | Field-aware merge |
| Patient demographic field | Field-aware merge **if proven**; otherwise human review |
| Medical history | Append/preserve; never blind overwrite |
| Appointment slot | Transactional allocation (§37); reject on conflict |
| Draft invoice | Version conflict + user resolution |
| **Posted invoice** | **Immutable → reversal / credit note only** |
| **Payment** | **Idempotent command, never overwritten** |
| **Ledger** | **Append-only, forever** |
| **Inventory movement** | **Append; settlement policy (§36)** |
| **Quantity-on-hand** | **Derived; never a client absolute write** |
| Cash session | Domain workflow (open/close invariants) |
| User roles / entitlements | Server-authoritative, never client-negotiable |
| Audit records | Append-only, forever |
| File object | Immutable versioned object (§56) |

**[INVARIANT] No blind client-wins or server-wins for anything with financial effect.** Resolve with a
compensating or adjustment entry, with reason and audit, never by rewriting a posted value.

**[DECISION — minimize conflict by model.]** Make independent operations additive: prefer separate
`ClinicalEntry` records over one large mutable `MedicalHistoryText` blob (§34).

### 8.15 Echo safety

**[INVARIANT]** A client tolerates receiving its own committed state again. Submit `M1` → commit → settlement →
a later delta contains the same row. The second application must not duplicate a business operation, reset
dirty flags wrongly, duplicate audit, or recreate a pending mutation (Gate AA).

### 8.16 Representative flows

**Normal online write:**

```text
Desktop/Web → Business API: Command(OperationId, Payload, Fingerprint)
Business API → Application: authZ, concurrency, invariants
Application → Domain: apply, produce events
Domain → Infrastructure: persist (one transaction) + outbox write
Business API → Client: Accept (Settled) | Reject | Conflict
```

**Offline write → reconnect:**

```text
Desktop (offline): local validation → atomic local commit + journal → queued
Desktop (reconnect): reconcile SettlementUnknown; priority-submit commands
Server: idempotency → authorization → concurrency → invariants
Server → Desktop: Settled | Rejected | Conflict → local settlement state updates
UI: reflects settlement, never leaves it ambiguous
```

**Adverse interleavings:** every order of push, partial delivery and push failure resolves to the
**same** deterministic outcome. Dependency settlement, not arrival order, decides (§8.9, Gate O).

### 8.17 Clinic collaboration & reconnect contract (A ↔ B)

**[DECISION — S01–S11.]** One server authority, one durable client coordinator, self-hosted SignalR
hints (§44), and mandatory checkpointed anti-entropy pull. **No** peer-to-peer mesh, clinic server,
broker or distributed-cache requirement.

```text
A saves patient P (stable client Id P.Id, MutationId M)
  → local transaction: provisional overlay + durable journal
  → bounded immediate push (online; offline stays queued)
  → server: current authorization + unique receipt + domain checks
  → ONE commit: P + receipt/result + audit + change-hint outbox
  → hint relay via IHubContext to authorized clinic connections
B receives hint → wakes coordinator → checkpointed delta pull
  → complete batch staged → atomic apply / rebase / checkpoint
  → committed invalidation → active list/detail reload via Contracts
A receives receipt and its own later delta → settles once, echo-safe
B's create/update follows the same path back to A
```

**Visibility boundary.** B sees A's record only after A's intent reaches and commits on the server, and
B has permission and scope. If A is offline, A labels it local/pending. B cannot see an unreachable local record.
On reconnect the operation settles and B catches up. If B is offline, the server keeps the settled state,
and B reconnects from its checkpoint even if every hint was missed. The §1.1 target measures commit to B's
**rendered** refresh (relay, network, batch apply and query reload). Local-save-to-visible and reconnect-backlog latency
are separate measurements. A partition or saturation is visibly degraded, not a false real-time promise. The
hosted path may be WAN/VPN, not a clinic LAN.

**Canonical local model.** Keep `ServerBase` (last authoritative replica) separate from the durable
`PendingOverlay` (ordered unresolved intent). UI queries compose them. A pull updates the base, then
rebases the overlay or creates a conflict preserving both values. It **never** re-runs business handlers, outbox
posting, local mutation capture, or stock/payment deltas. Settlement updates journal status and base only with
causal or version evidence. A delayed response must not overwrite a newer base or erase later edits. Opaque versions are
equality tokens, not sortable integers. If a response cannot be ordered safely, persist its outcome and request a fresh
authoritative pull instead of replacing the base. Conflict resolution is a new explicit intent with provenance. Submitted
envelopes stay immutable.

**Coordinator lifecycle.** States: `Offline · Connecting · CatchingUp · Live · Backoff · AuthenticationRequired · ResyncRequired · RecoveringReplica`.
These are distinct from operation settlement states. Reachability is decided by **server authentication and readiness**, not
an OS Internet flag or hub connection. On startup, network recovery or wake:

1. Coalesce signals; authenticate; compare feed, scope and contract identities.
2. Register the current authorized hint subscription, **then** unconditionally pull from the durable checkpoint. This closes the subscribe/pull race. A hint during catch-up sets a dirty generation for a later round.
3. Reconcile unknown operations; fairly interleave dependency-eligible push and pull. Never require a conflicted push to succeed before B sees A's unrelated data. Unsent drafts may rebase. Submitted commands may not change fingerprint or expected version.
4. Persist progress; repeat bounded rounds until caught up to a sampled server watermark. `Live` means caught up to that sample, not "no future changes."
5. Keep anti-entropy polling on even when hints look healthy. Cancellation ends network work, not durable intent. A crash needs no shutdown callback to recover the journal or checkpoint.

**Query and UI boundary.** A durable local generation invalidates the affected query or cache only after commit. Active screens
refresh with coalescing, UI-thread dispatch and cancellation. Stale responses never overwrite a newer generation. Include additions and
removals, not only updated IDs, so lists, counts and related details refresh even for newly created rows. Preserve unsaved editor text,
focus and scroll. Show a conflict banner rather than replacing text under an active editor. Inactive screens query the new generation on
activation. Online-only web screens refetch from the authorized API on hints, with no local offline replication.

| Clinic scenario | Required deterministic outcome |
|---|---|
| Both online, A and B create | Same pipeline both ways; visible refresh within target; no manual reload |
| A offline, B online (or reverse) | Offline work durable and provisional; on reconnect, accepted work reaches the peer; rejected work stays visible to its author |
| Both offline, independent creates | Distinct stable IDs; dependency-ordered settlement; both converge without duplicate create |
| Same field edited, or delete vs. edit | Evidence-bound conflict or resurrection policy; losing intent preserved for review |
| Two employees enter the same animal independently | Transport IDs prevent duplicate replay, not duplicate identity. Flag duplicate candidates for reviewed merge. **Never auto-merge medical histories.** |
| Hint lost, duplicated, delayed, or hub restarted | Checkpointed pull still converges; a hint never advances the cursor |
| Client crashes on last page or after server commit | Staged batch replay or receipt recovery; no skip, no duplicate effect |
| Offline beyond journal retention | Shadow full resync keeps journal and overlay, rebuilds to a consistent watermark |
| Permission or device revoked while offline | Offline authority expires (§10.4); reconnect denied or scope rebuilt; never uploads under another employee |
| One conflict plus unrelated writes | Only causal descendants blocked; pull and independent operations continue |
| Server restore erases acknowledged work | New epoch forces recovery; prior evidence preserved; lost settled operations reconciled (§17.3), never blind financial replay |

---

## 9. Multi-tenancy

### 9.1 Tenancy model

**[DECISION] One shared PostgreSQL database, one shared schema, tenant discriminator.** Standard,
cost-efficient shape for many small B2B tenants (Azure multitenancy guidance). Each clinic is one tenant.
Many users per tenant. Every tenant-owned record carries authoritative tenant ownership.

**[PATTERN] Deployment Stamps, as the documented escape hatch.** A future mapping
`Tenant → DeploymentStamp → Database` lets a large or noisy tenant move to its own database
without a domain rewrite. Designed for in the data-access abstraction. **[DEFERRED]** until a real tenant needs it (§72).

### 9.2 Isolation — layered ownership

**[INVARIANT] Tenancy correctness belongs to the application boundary, not an optional DB feature.**
Mandatory layers must be **collectively sufficient with RLS disabled**. Authentication alone cannot secure a
query. No layer substitutes for another. Optional RLS adds defense in depth.

```text
MANDATORY (must hold with every hardening layer disabled):
 1. Authentication: every request resolves identity before any operation.
 2. Server-side membership resolution: TenantId comes ONLY from authenticated membership, on every request.
 3. Authorization: RBAC + policy + resource-based, re-evaluated server-side.
 4. Tenant-aware data access: every tenant-scoped query filters by TenantId, and separately by
    soft-delete (two separable filters, so a maintenance job can lift soft-delete without lifting the tenant filter).
    Every write is stamped with the caller's server-resolved TenantId.
 5. Replication scope filtering: every replicated read is tenant-filtered; every replicated write is server-stamped.
 6. File authorization: Valet Key, tenant-checked (§10.8, §57).
 7. Tenant-safe cache whenever caching is on (§10.7). Filtered SQL cannot make an unsafe cache acceptable.

HARDENING (may be disabled without breaking tenancy):
 8. PostgreSQL Row-Level Security: secondary DB enforcement, gated by validation (Gate J) before use.
```

**[INVARIANT] The client-supplied `TenantId` is never trusted.** Resolved only from authenticated membership, on every request.

**[INVARIANT — no raw tenant data access outside tenant-aware infrastructure.]** A global query filter is a
**convenience**, not the security boundary. It is bypassed by raw SQL, bulk update, stored procedures, report
queries, maintenance jobs, cache reads and file access. So: mandatory layers 1–7 together, **plus** a ban on any
code path that reaches tenant data outside approved tenant-aware infrastructure. Each bypass vector is either
routed through that infrastructure or is an explicitly reviewed, audited exception (§20). No unmonitored escape hatch (Gate E/J).

**[INVARIANT — the resolved tenant context is immutable for the request.]** `TenantId` is captured once, at the
request boundary, into an immutable `ResolvedTenantContext`. No downstream layer may change it. It holds across async
continuations, background jobs, worker claims, outbox handlers and scheduled jobs. Each inherits it and never
re-derives or re-trusts a tenant.

**[INVARIANT — required-navigation pitfall.]** A required navigation into a tenant-filtered entity must never silently
drop parent rows when the filtered child disappears. Sensitive navigations are modeled as optional, or with matching
filters on both sides. The data filter is convenience/defense, never the security boundary (layer 3).

### 9.3 Tenant resolution & propagation

```text
Credential → authenticated principal (Identity) → membership (Tenant/Clinic)
  → ResolvedTenantContext (immutable for the request)
  → every downstream call (Domain, Infrastructure, replication view, RLS transaction-local tenant context if enabled)
    receives the SAME TenantId. Never re-derived, never re-trusted.
```

### 9.4 RLS hardening rule (if enabled)

PostgreSQL RLS: `USING` for visible rows, `WITH CHECK` for new or updated rows (S22). RLS stays optional hardening.
**Tenant isolation MUST pass with RLS off.** For an enabled deployment, each tenant-bound read or write runs in an explicit
transaction and first executes the parameterized command `SELECT set_config('animora.tenant_id', @tenantId, true)`,
with the value taken from authenticated membership. Policies compare `TenantId` to
`NULLIF(current_setting('animora.tenant_id', true), '')`. A missing or empty context matches **no rows**. Setup failure denies access.

The setting is **transaction-local**, not connection-local. Reapply after each begin, retry or reconnect. Never run an RLS-protected
query outside that transaction. `No Reset On Close=true` is banned.

Runtime role: non-owner, non-superuser, **no `BYPASSRLS`**, no schema DDL, `TRUNCATE`, trigger-disable or replication-role privilege.
When owners must be covered, `FORCE ROW LEVEL SECURITY` is required. Superusers and BYPASSRLS roles still bypass it. Test A→B→A pool reuse,
cancellation, rollback, exceptions and parallel reads. DbContext is scoped per work unit. Context pooling is deferred until those lifecycle
tests pass. Npgsql data sources are shared. Never share one open connection.

RLS does **not** replace explicit tenant predicates in raw feed queries, key-only change records, tombstones, batches, worker queries or
authorization. Protect those separately. Security-invoker views must preserve the caller's policy. Unique and FK checks can bypass RLS, so
constraints are tenant-qualified and API errors must not reveal another tenant's identities. Cross-tenant operator tasks use separately
reviewed credentials and endpoints, never a client-selectable bypass value. Transaction-local settings are context plumbing, **not** protection
from SQL injection or a compromised application role. Parameterization and authorization stay mandatory.

### 9.5 Cross-tenant access

`Tenant A user → Patient in Tenant B` **fails before returning the row**, at a mandatory layer. Failures must not differ in a way that reveals
whether a protected record exists (no existence oracle).

---

## 10. Security

### 10.1 Three independent planes

**[INVARIANT — never collapsed.]**

```text
Authentication = WHO is the caller?                       (Identity)
Authorization  = WHAT may this caller do?                 (RBAC + policy + resource)
Entitlement    = does THIS deployment license feature X? (Subscription/Licensing, §13)
```

A valid license never authenticates a user. A logged-in user is not automatically entitled. An authorized action does not imply
licensing. Fixed order on every request:

```text
Authenticate → Resolve tenant/membership → Authorize resource/action → Check entitlement → Validate request → Execute
```

No endpoint infers tenant from URL, body, query string, or local client state alone (§99).

### 10.2 Authentication architecture

```text
Web client     → cookie session (server issues and validates; no auth logic in client script)
Desktop client → bearer + refresh token pair in OS-protected credential storage;
                 refresh needs server reachability (§10.4 covers the gap when unreachable)
```

**[INVARIANT — one local identity authority, explicit schemes.]** Cookies and desktop bearer tokens are different
handlers of the same identity subsystem. Each endpoint declares allowed schemes. Cookies cannot silently authenticate a
bearer-only desktop submission and skip antiforgery. No bespoke token format.

**[DECISION — desktop auth is the stack-approved local token mechanism, not generic OIDC.]** The bearer+refresh pair is
issued and validated by Animora's own identity subsystem, stored in OS secure storage, with refresh rotation and device-bound keys
where supported (§10.4). It is **not** described as a generic OAuth/OIDC server. A real external OIDC server would be a new
boundary and a new ADR. A public native client never ships a static client secret.

**Platform limit vs. Animora policy (S25).** Built-in Identity bearer tokens are **opaque protected tokens, not JWTs**. These APIs are not
a full OAuth/OIDC server. The JWT Bearer guide applies only to a future explicit JWT integration. Never attach `JwtBearerHandler` to opaque
Identity tokens. Default issuance does not prove device possession, give immediate access-token revocation, or rotate refresh tokens one-use.
Those need a durable authenticated session/device registry, current membership and security-policy checks on sensitive requests, and reviewed
device-possession verification. A client `DeviceId` string or stale claim is not device binding or current authorization.

One-use refresh rotation with reuse detection, when enabled, is a persisted session-family policy around the platform handler. It consumes
the previous token fingerprint and issues the successor **atomically**. A lost refresh reply is protected against replay. Require
reauthentication rather than allowing unlimited old-token reuse. Never log tokens or results. Stock registration, role and tenant-selection
APIs cannot bypass the controlled provisioning boundary of §13.4.

**[INVARIANT — browser cookie auth is incomplete without anti-forgery (CSRF).]** Every state-changing browser request requires
anti-forgery protection. This is an architecture requirement, not optional hardening. Cookies are `Secure` + `HttpOnly` + `SameSite`,
with Origin/Fetch-metadata checks where appropriate and strict CORS. **CORS is not a substitute for CSRF protection.** The desktop bearer path is not
cookie-ambient, so it is not CSRF-exposed the same way, but it has its own rules (§10.4). Dedicated gate: W-csrf.

### 10.3 Authorization architecture

**[PATTERN] RBAC + policy-based + resource-based, layered.** Role gates the coarse capability (cashier, doctor, manager). A policy
handler evaluates fine rules (amount thresholds). A resource check evaluates the **specific instance** ("this tenant's invoice, not another
cashier's till").

```text
Role     = Cashier
Policy   = refund below threshold
Resource = this specific cash session
```

**[INVARIANT — RBAC alone is never sufficient for a financial operation.]** Authenticate, resolve tenant, check coarse action policy. Then load
only the minimum resource authorization metadata **through tenant-scoped access**. Invoke resource authorization **before** returning protected
fields or mutating state. `[Authorize]` precedes resource loading and does not replace the imperative check (S25). Revalidate mutable
authority and preconditions at the transactional action boundary. The client may hide actions for UX. **The server always decides**, including
for background work, status lookups and file requests.

### 10.4 Offline-session architecture

**[INVARIANT] Bearer and refresh tokens are not an offline authentication mechanism.** Refresh needs server reachability. Offline continuity is a
separate, explicit, device-bound session policy: a **signed, device-bound capability snapshot** with an explicit expiry. It is distinct from both the online
token and the entitlement license.

```text
CapabilitySnapshot
  Tenant · User · Device · Roles · Policies · CapabilityVersion ·
  IssuedAt · ExpiresAt · ServerPolicyVersion · Signature
```

It answers "what may this device act as, until when, without contacting the server." It is declared **per capability**, not as one blanket number:

```text
MaximumOfflineDuration   per capability, derived from the threat model
SensitiveOperationPolicy refund and period close get short or ZERO offline windows; low-risk reads get longer ones
ExpiryBehavior           FAILS CLOSED: read-only / locked / sensitive operations blocked. Never a silent extension of authority.
Re-authentication        deterministic order: re-auth → capability refresh → pending-command reconciliation
```

Baseline: low-risk reads long; routine record editing normal; cash and payment shorter; refund short or zero; period close **zero** offline authorization;
license-sensitive operations per explicit license policy.

**[INVARIANT — offline revocation is a next-contact guarantee, not an instant one.]** A disconnected device cannot be reached.
Revocation (device revoked, role changed, password or security stamp changed, employee terminated, subscription cancelled) takes effect
**immediately online and at the next trusted contact offline**, never retroactively on a device still offline. The only bound on a
revoked-but-offline device is the snapshot's expiry plus local device protection. This is a stated trade-off of offline-first, **not** "instant revoke."
The architecture limits exposure by keeping sensitive offline windows short or zero.

### 10.5 Transport & local PKI

```text
Animora OFFLINE Root CA            (private key NEVER on the production server, NEVER in a routine server backup; kept offline by the deployment authority)
  → issues → Issuing CA
      → issues → Server certificate (LAN hostname / internal DNS): the ONLY cert + key on the running server
Trust anchor (root public cert) → provisioned into client OS/browser trust store by deployment (installer / MDM / policy), NEVER by application JavaScript or in-app code
```

**[INVARIANT — root key is not a runtime or backup artifact.]** If the root key were in the full server backup, anyone holding a backup could mint
trusted certificates and MITM every client. Offline root + online issuing CA is the standard shape (ADR-22). The recovery matrix (§17.3) backs up
the server certificate and runtime key material, **not** the root key.

**[INVARIANT]** A browser cannot be made to trust a private CA from inside a web app. Trust provisioning is deployment-time, not runtime.

**[INVARIANT — offline revocation checking only.]** Chain validation uses a locally served CRL, or short-lived certificates with controlled renewal.
It never contacts an external OCSP/CRL endpoint (hidden Internet dependency, R11, §17.4).

**[DECISION — protocol policy, not hard-coded version.]** Disable obsolete protocols. Prefer the newest mutually supported protocol. Keep an
interoperability floor for the oldest supported client tier. Let the OS negotiate. Hard-coding one version would silently exclude a supported tier.

### 10.6 Server key material

**[INVARIANT] Stateful key material** (cookie auth, antiforgery, other protected payloads: the ASP.NET Data Protection key ring) is a
first-class, persisted, backed-up artifact. **Never default, per-instance, ephemeral storage.** Losing it silently invalidates every session and
protected payload. It is part of the recovery matrix (§17.3). The key ring needs a stable `ApplicationName`, persistent location, protection at rest,
an ACL, and must survive restart and restore (§64).

**[INVARIANT — root and license issuers stay outside runtime.]** The root CA and **license-signing private keys** never live on the clinic-serving node.
The node holds public verification material only. A protected separate issuer mints certificates and licenses. This does **not** mean the runtime holds no private
keys: TLS, Data Protection, file grants and offline capabilities need runtime keys with their own purposes. Capability or grant keys cannot issue licenses or CA certificates.
Compromise procedures revoke the artifacts and affected sessions or devices. **Never reuse one key across trust domains.**

**[DECISION — key register.]** Because one node co-locates app, DB, files and CA, every key is listed with owner, purpose, generation site, storage,
permitted user, exportability, rotation, revocation, backup and compromise procedure:
TLS/CA keys · server certificate · Data Protection ring · license-signing key (offline) · offline-capability signing key · file-access-grant signing key ·
device identity keys · DB and backup encryption keys. "A single node" is a **compute** decision, not a single trust domain (ADR-22/23).

**Data Protection lifecycle (S26).** Explicit persistence can disable default at-rest encryption. Configure persistence **and** a local at-rest
protector and ACL. Decryptors must be recoverable on replacement hardware. The application discriminator is environment-specific: staging never shares production's
ring. Keep expired keys and wrapping certificates while supported payloads or backups need them. **Expiry is not permission to delete.** Default lifetime 90 days. Expired keys can still
unprotect. Revoked keys are refused by default. Deleting a key makes its payloads irrecoverable. Back up the ring **and** its decryptors. Test old cookies, Identity tokens and
protected grants after restore, and test emergency revocation separately. Restoring keys must not resurrect revoked sessions from an old DB security policy.

### 10.7 Tenant-safe caching

**[INVARIANT] Every cache key for tenant, clinical or financial output includes at least:** tenant scope, user/permission scope, resource identity, and a query or
contract version. For permission-sensitive data, add a permission fingerprint. **[PATTERN] Cache-Aside**, applied only with that key composition. A cache mix-up
is a **security defect**, not a performance bug (Gate L).

Caching applies only where measurement justifies it, and **never before authentication and authorization**. Output caching runs after authN/authZ, but order alone is not
isolation (§59). Default ASP.NET output caching excludes authenticated responses and responses that set cookies (S27). Launch does **not** HTTP-output-cache clinical, financial,
sync, status or file-grant responses. Use `Cache-Control: no-store` where sensitive. Anonymous public immutable content may opt in.

A future authenticated policy needs tenant, current permission/security-policy generation, principal and resource keying, resource authorization **before cache lookup**,
revocation invalidation, and Gate L. A permission TTL cannot promise immediate revocation. Bound internal Cache-Aside stores, invalidate after commit, and never decide
stock, payment, period or authorization preconditions from stale cached state.

### 10.8 File access

**[PATTERN] Valet Key (Azure).** A client never receives or uses a raw filesystem path or URL as a credential. Every read or write:

```text
FileId → application authorization → signed, short-lived grant issued by the server → the GRANT (not the path) is what the client holds
```

**[INVARIANT] Path obscurity is never authorization**, even on a trusted LAN. A file reference that crosses the trust boundary is a logical `FileId`,
never `C:\...`, `\\server\...` or an internal URL (§93). Same for desktop and web (§14, §56–§57).

### 10.9 Threat summary (STRIDE-lite)

| Threat | Primary mitigation |
|---|---|
| Spoofing (identity / tenant) | Authentication plane (§10.2) + device identity + server-resolved TenantId only (§9.2) |
| Tampering (payload / command replay) | TLS, signatures, command fingerprint + idempotency (§8.6.1) |
| Repudiation | Append-only audit (§6, §61); immutable ledger (§11.1) |
| Information disclosure (cross-tenant) | Layered isolation (§9.2) + tenant-safe cache (§10.7) + Valet Key (§10.8) + encrypted local store (§92) |
| Denial of service (weak server) | Bulkhead, throttling, budgets (§12.3, §43, §71); delta sync (§8); static-first rendering (§7.3); bounded retries (§42) |
| Elevation of privilege | RBAC + policy + resource AuthZ (§10.3); entitlement as an independent plane (§10.1) |

**Required security tests (§111):** tenant-ID manipulation, IDOR, cross-tenant sync, stolen device, expired capability, replayed command,
same ID with different fingerprint, malformed patch, oversized payload, path traversal, file-grant forgery, CSRF, session fixation, token replay,
cache confusion, SQL injection, OData abuse on the Datasync boundary.

---

## 11. Domain & data

### 11.0 Consistency classes

**[INVARIANT] Every state belongs to exactly one class. A lower class is never silently used as a higher one.**

```text
C1 Local Provisional       can exist offline (LocallyCommitted, PendingSettlement)
C2 Server Transactional    authoritative at commit (PaymentSettled, JournalPosted, ResourceAllocated)
C3 Eventually consistent   derived from authoritative state (DashboardSummary, LowStockView, ReportingProjection)
C4 Notification hint       NOT state ("tenant 42 changed")
```

Never render C1 as C2. Never use C4 as C2.

### 11.1 Accounting — append-only ledger

**[PATTERN] Append-only ledger** (event-sourcing-lite terminology for financial facts; §24.2). Posted facts are immutable. Corrections are new
compensating entries with reason and audit trail. Never an in-place change to a posted row. A ledger that can be silently edited cannot be trusted after
any conflict, retry or crash (§8.14, §2.13).

**[INVARIANT — append-only alone is not an accounting model.]** A real general ledger also needs:

```text
Double-entry balance     every posted journal: Σ debits = Σ credits
No posted mutation       a posted entry is only reversed, never edited
Open posting period      a posting targets an OPEN FiscalPeriod (below)
Server posting date      accounting date is server-governed, not a client timestamp (A-RULE 036)
Currency + versioned rate  currency explicit; exchange rate versioned
Tax + rounding snapshot  tax calc and rounding policy retained at posting
Immutable dimensions     account dimensions fixed for a posted entry
Source document ref      every posting references its source document
Reversal reference       a reversal points to the original posting
```

Entities: `Journal · JournalLine · PostingBatch · Account · FiscalPeriod · AccountingDate · DocumentDate · Currency · ExchangeRate · Tax · Dimensions · ReversalReference · SourceDocument · PostingUser · PostingTimestamp · OperationReference`.
Chart of accounts and tax rules are ADR-21. Detail: §35.

**[DECISION — period close is a state machine, not a boolean.]**

```text
Open → SoftClose → Review → Closed → (Authorized Reopen / Correction only)
```

**[INVARIANT — offline commands vs. a closed period follow one declared policy.]** A device offline when a period closes may reconnect with
commands dated into it. One stated, auditable policy resolves this: reject, post to the current open period, allow authorized back-posting,
hold for accountant, or create an adjustment. Never silently post into a closed period, never silently drop (Gate S).
**Safe default until configured: hold for accounting review (`NeedsAttention`)** with the original document date preserved. Never auto-back-date
or silently move to the current period. A reviewed adjustment is a new operation. Existing local accounting behavior is unchanged.

### 11.2 Inventory — movement-based stock

**[PATTERN] Movement-based state.** Quantity-on-hand is never stored and overwritten as an absolute number by an offline client. It is **derived** from
an immutable sequence of movements: Receipt, Sale, Consumption, Loss, Adjustment, Transfer, Return. Two offline devices recording different movements never
lose an edit. Overwriting an absolute quantity would drop one.

**[INVARIANT — movement convergence is arithmetic, not business, correctness.]** Movements that are each valid can still sum to a non-permitted outcome. Example: server stock 1,
two offline devices each sell 1 → both movements valid → sum −1. Whether that is allowed is a **policy decision the architecture must state**, not something convergence decides.
Per product class, define:

```text
Negative-on-hand policy   allowed / blocked / allowed-with-alert
Reservation model         does an offline sale reserve stock; how is over-sell reconciled at settlement
Lot / batch / expiry      tracked where clinically required (vaccines, cold chain)
Serial                    tracked where individually identifiable
Location / warehouse      multi-location stock, transfers
Cost layer / valuation    the basis COGS and finance post against
```

Settlement (§8.6) re-validates the policy server-side. An offline movement that breaches it goes to conflict policy (§8.14), never silently accepted
because the arithmetic converged. Values are ADR-19; detail §36.
**Safe default for an unconfigured class: no negative settled stock, no unproven offline reservation.** Local intent stays provisional. An oversell becomes
`NeedsAttention` for explicit reconciliation. Independent products still settle. Controlled lot, expiry or serial requirements must be configured before
settling affected stock. Valuation rules stay authoritative; the sync engine never guesses them.

### 11.3 Read vs. write models (CQRS-lite)

**[PATTERN] CQRS-lite:** command/query separation **without** necessarily separate storage at launch. Client read models (desktop grids, web lists) may be denormalized or
cached for UX. The **authoritative write model stays normalized and server-side** for Finance and Inventory. A desktop grid never mirrors aggregate structure and is never
a source of truth for a write decision.

**[DEFERRED]** A separate reporting database is an explicit future escalation (§16.2). The seam (read-only reporting clearly separated from the transactional path)
is kept so escalation is additive. Launch: OLTP plus incremental read projections (§58).

### 11.4 Concurrency architecture

**[PATTERN] Optimistic concurrency with an opaque `entity-version token`** on every mutable entity. A stale write is **detected**, never silently accepted. It goes to the
conflict pipeline (§8.12). The token **detects** staleness; it never **decides** the business outcome.

**[INVARIANT — version tokens are sufficient for edits, NOT for contended allocation.]** A version token correctly guards "did this row change under me": demographics, invoice
drafts, notes. It cannot protect a **shared resource** that two callers claim from different bases: two clients booking the same room or slot, two tills claiming the same drawer, seat, period or
device registration. Both read a non-conflicting version and both write, producing a double booking that no version check catches.

Such invariants are enforced as a **server-side resource-allocation model inside one transaction** (§37): a uniqueness constraint, a serializable transaction, an application or resource lock, or an explicit
slot-allocation table, chosen per case. Scheduling owns a **Resource Allocation Model** (rooms, staff, boarding capacity). Not just a versioned appointment row. The mechanism per invariant is ADR-20. The guarantee is Gate T.

#### 11.4.1 Transaction and allocation contract (S23/S24)

Launch defaults on real PostgreSQL. Not an implementation menu. `REPEATABLE READ` snapshot export is **not** serializable business allocation.

| Flow | Arbiter / atomic obligation |
|---|---|
| Delta or full export | One explicit `REPEATABLE READ` transaction: snapshot watermark, keys, authorized rows, frozen batch (§17-CT). No reader/writer overlap on one connection. |
| Mutable patch | Tenant-qualified conditional update with original `Version`. Receipt, audit, state, journal and outbox commit together. Never refresh token and retry as overwrite. |
| Invoice, payment or stock checkout | One durable transaction. Acquire the tenant write gate first, then period, resources and rows in canonical order. Re-read preconditions. Write stock, movements, ledger or payment, receipt and outbox together. |
| Appointment or boarding interval | Lock a persistent tenant/resource gate row `FOR UPDATE` **before** checking overlap or capacity and writing the allocation. Unique discrete slots or reviewed exclusion constraints add defense. |
| Fiscal posting / close | Posting locks the period row `FOR SHARE` before checking open. Close takes `FOR UPDATE`. No lock upgrade and no check-then-post race. |
| Report / read projection | Bounded `READ COMMITTED` by default. Explicit `REPEATABLE READ` only for genuinely coupled reads. Never settlement authority. |

Every tenant business writer acquires its `sync.TenantFeedState` gate before business locks (§PG). Maintenance uses the same order. Resource gate rows
exist before use. For absent resources, provision idempotently under the tenant gate. `FOR KEY SHARE` does not block non-key period updates and is **not** the period gate.
An ADR may choose advisory locks, but only transaction-owned `pg_advisory_xact_lock` / `pg_advisory_xact_lock_shared` with stable server-defined keys. No session-level lock in a pooled connection.
Hash collisions may serialize unrelated work but must never miss arbitration. Multi-tenant maintenance splits into tenant-local transactions. **No lock spans HTTP, user input or external files.**
Lock, statement and transaction deadlines are finite.

Intervals are half-open `[StartUtc, EndUtc)` with `StartUtc < EndUtc`. Overlap: `existing.Start < requested.End AND requested.Start < existing.End`.
All commands and maintenance serialize the same resources regardless of requested start. Stock is conditionally decremented on an authoritative derived balance row **under its gate**, with immutable
movements and a balanced journal in the same transaction. Seat and device limits use atomic allocation, not check-then-insert.

**Fault classification.** SQLSTATE `40001` (serialization failure) and `40P01` (deadlock): roll back and replay the whole unit (bounded), with a fresh DbContext, the **unchanged OperationId** and revalidated authority.
Retain protected lock-wait and deadlock logs. Diagnose with `pg_locks` and `pg_stat_activity`. `23505` is interpreted by its **actual constraint**. It is not universally a successful replay.
`55P03` (lock unavailable), `57014` (cancel/timeout), connectivity loss and uncertain COMMIT are classified separately. Verify receipts on a fresh primary connection before replay.
EF concurrency exceptions are domain conflicts, not transient retries. Default maximum **3** whole-transaction attempts with jitter. Never buffer or retry a streaming export as an ORM query. Never blindly retry a stale patch.

Durability: `fsync=on`, `full_page_writes=on`, `synchronous_commit=on`. Required state, receipt, ledger and audit tables are **logged**, never `UNLOGGED`. Acknowledged durability still depends on real storage guarantees.
SQL is parameterized and identifiers are allowlisted. Acceptance runs on the exact production PostgreSQL build, **not** SQLite or EF InMemory.

### 11.5 Reporting isolation

**[INVARIANT] Heavy reporting is a budget separate from OLTP.** It never rides the transaction path. Each heavy report has its own timeout, cancellation and concurrency budget (§43, §58).

---

## 12. Reliability & eventing

### 12.1 Transactional Outbox

**[PATTERN] Transactional Outbox** (Azure). A domain event and the state change that produced it commit in **one transaction**. No lost event, no dual write:

```text
BEGIN
  business state
  domain event / outbox record
  audit (same transaction where financial or security audit requires it)
COMMIT
```

A background relay dispatches later, **at-least-once**.

**[INVARIANT] "Dispatched" never means "delivered exactly once."** Every consumer of a relayed effect is an **Idempotent Consumer** (§8.6.1), keyed on a stable identifier.

**Mandatory relay state machine.** The atomic claim is not optional. A terminal failure path is required:

```text
Pending → Claimed (atomic conditional claim, LeaseExpiry) → Processing
   → Succeeded
   → RetryScheduled (NextAttemptAt backoff) → back to Pending
   → DeadLetter   (retry budget exhausted / poison message)
   → Cancelled    (effect no longer required)
```

Each outbox record carries: `LeaseExpiry · AttemptCount · NextAttemptAt · FirstFailedAt · LastFailedAt · LastException · DeadLetterReason`.

A naive "select unprocessed → process → mark done" **will** double-dispatch once a second worker exists. The atomic claim, with a `LeaseExpiry` so a crashed worker's claim can be reclaimed, makes the database the single arbiter. A poison effect must reach **DeadLetter** with a reason and surface to an operator. Never retried forever, never silently dropped (Gate AD, §106).

### 12.2 Background workers

**[PATTERN] Competing Consumers** (Azure). Several workers may compete for one unit of work, safely, because the claim is atomic and effects are idempotent. Every step is **resumable after a crash mid-step**. Assume a worker can die at any point and re-run without duplicating an effect.

Launch: in-process background-service host with bounded queues, bounded concurrency and priority classes:

```text
P0 Interactive / settlement
P1 State synchronization (journal reader)
P2 Outbox / notifications
P3 Reporting / maintenance
```

**[INVARIANT — Competing Consumers is for independent work; ordered work carries an ordering key.]** Competing consumers give throughput for
independent effects and **no** cross-item ordering. Ordered effects use an explicit key, for example `(TenantId, AggregateId)`, `(TenantId, ResourceId)` or `(TenantId, CashSessionId)`. Items in one group serialize. Different groups run in parallel.
Each work type declares: concurrency class · ordering key · retry policy · dead-letter policy. Launch PostgreSQL business writes use the short tenant gate for commit ordering (§PG). This **measured correctness trade-off overrides** older no-tenant-serialization advice.
Ordering between the two queues (§8.9) is governed by settlement dependency, never by worker scheduling.

#### 12.2.1 Durable work, fenced leases, ordered progress

**[DECISION — S20/S28.]** SQL outbox and job tables are the durable truth. Bounded `Channel<T>` queues carry IDs and wake hints only. Startup and periodic scans recover eligible work even if every channel notification was lost. An acknowledgement follows committed admission. Never queue delegates that capture HttpContext, request services or a DbContext. Hosted services create a fresh DI scope and context per bounded unit. Periodic work does not overlap itself unless independent. Shutdown stops admission, drains finitely, and leaves unfinished leases reclaimable. `StopAsync` is not crash-guaranteed and cannot be the durability mechanism.

Claims carry `LeaseOwner · LeaseToken/ClaimGeneration · LeaseExpiresAt`. Claim pending, due-retry or expired non-terminal work atomically using **DB time**. Renew, complete, retry and dead-letter writes require the **current** owner and token. A paused old worker cannot finish a reclaimed claim. Use an atomic PostgreSQL CTE `UPDATE ... RETURNING` with row-lock eligibility. **`SKIP LOCKED` is for queues only**, not a consistent state feed. Claim transactions are short. Processing happens outside them.

Consumer effects and a unique inbox marker commit together. Fencing cannot undo an old worker's already-sent remote request. Remote effects need a stable receiver dedup key or authoritative verification. Otherwise, expose possible duplication and reconcile. Never claim effectively-once from a lease alone.

Ordered work carries `(TenantId, OrderingKey, Sequence)` and a durable completed predecessor. A claim selects only the **next eligible** item for that key. Memory locks or a single current worker are not restart-safe ordering. A poison predecessor blocks only its key and surfaces to operators. Bypass or compensation is an audited policy. Redelivery keeps the same EventId and consumer identity, never a new dedup-evading ID. Independent keys and incoming state continue. Projection rebuilds use a separate generation and authoritative watermark, never coalesced replicated state as event history.

### 12.3 Resilience

**[PATTERN] Retry + Circuit Breaker + Timeout + Bulkhead** (Azure), at every network boundary:

```text
Sync push/pull (client ↔ Sync Surface)      → retry with backoff; breaker opens on sustained unavailability, so the client degrades to "working offline" instead of hammering the server
Business command submission (client ↔ API)  → same. A command NEVER retries by generating a new OperationId. Retries reuse the original.
Outbox relay dispatch                        → retry with bounded backoff (NextAttemptAt), then DeadLetter. A breaker guards ONLY a genuinely REMOTE effect. For local in-process effects at launch, Retry + Backoff + DeadLetter + operator remediation suffice.
Background workers vs. request threads      → Bulkhead: a background backlog can never starve interactive requests
```

**[INVARIANT — a Circuit Breaker guards remote dependencies only.]** Applied at the client↔server boundary, not on local outbox effects at launch (no added complexity without the failure it prevents).

**HTTP policy ownership (S07/S13).** One configured resilience pipeline per typed HTTP client. No nested automatic retries across UI, sync adapter and journal scheduler. Default unsafe-method retries are **disabled**. Enable command or patch retries only where the immutable envelope and durable receipt protocol are enforced. No hedging or parallel speculative write. Respect `Retry-After` on 429 and 503. One credential refresh on 401, then require login. **403, validation, conflict, incompatible contract and expired cursor are policy transitions, not transient faults.** Cancellation or timeout after transmission is potentially `SettlementUnknown`, not proof of rollback. A 404 status lookup alone does not prove non-execution during an epoch change, race or async admission. Reconcile the same identity under the server's explicit outcome contract. `202 Accepted` means admitted or pending, not settled.

**[INVARIANT — retry is bounded by a budget, not just backoff.]** Each retrying boundary declares: max attempts, max elapsed time, jitter distribution, and per-tenant plus global reconnect limits. A mass reconnect must not become a self-made thundering herd (Gate I, §42). An outage transitions the client to offline mode, not a retry storm. **No unbounded retry episode.** Durable intent stays eligible for a later bounded episode after a connectivity event or a scheduled low-rate probe. Reaching the retry limit never discards intent or requires an app reinstall.

**[PATTERN] Throttling for reconnect.** Resumption after startup or reconnect uses random initial jitter, adaptive or exponential backoff, progressive catch-up, per-tenant and global concurrency limits, and server overload signals. **Never** a fixed interval, which would create a synchronized reconnect storm (Gate I, §88).

### 12.4 Bulkhead / backpressure budgets

**[INVARIANT]** Separate resource budgets for: interactive requests, sync, file transfer, reports, maintenance and outbox. When report work grows, **report throughput decreases, checkout never stops** (§43, §71).

#### 12.4.1 Admission and fairness on the weak node

**[DECISION — S29/S30.]** Partition by server-derived `Tenant + OperationClass`, plus bounded user and device sublimits, plus **global concurrency ceilings**. Bound partition cardinality and idle retention, so arbitrary request IDs cannot create unlimited limiter dictionaries. Anonymous login and provisioning get a small abuse budget before expensive authN. Authenticated limits apply after routing, authN and tenant resolution. Rate limiting alone is not DDoS protection. Budget connections and DB workers, CPU, bytes, file I/O, queue depth and duration, not only requests per second.

P0–P3 are service classes, not strict priority. Use bounded weighted turns with aging, so journal pull, receipt lookup, outbox, backup and cleanup keep progress. Causal ordering still applies. A poisoned financial operation cannot block other aggregates. A report backlog cannot consume checkout permits. Limiter, channel and connection waits count toward **one total deadline**, not several small independent queues that add up to unbounded latency.

| Budget artifact | Required fields / saturation outcome |
|---|---|
| Operation class | Global and per-tenant permits, queue capacity and age, total and SQL deadline, body/item/byte ceilings; typed 429/503 with retry guidance |
| Journal extraction | Active snapshots, rows/bytes/time, tenant and global staging bytes, batch TTL, cleanup cadence, disk reserve; **no partial-ready batch** |
| Files | Chunk/body limit, parallel transfers, tenant storage and transfer quota, disk reserve; resumable backpressure; no whole-file memory buffer |
| Circuits / hints | Connections, per-connection buffers, idle retention, reconnect admission; upgraded WebSockets have separate limits |
| Retry episode | Attempts, elapsed budget, jitter/backoff, owner and error classes; driver, SQL and HTTP retries count together |

Operations owns validated startup configuration and a release capacity report. Unset, non-positive or unbounded budgets **fail readiness**. Measured values (§69, §110) are required before GA, not library-default guesses.
Pages target 256 KB–1 MB **uncompressed**, with a row ceiling. Bound decompressed request size and JSON depth and collection counts. Exhaustion has a typed recovery path, **never** journal deletion or false success.

### 12.5 Graceful degradation

**[INVARIANT] Every overload or failure has one defined outcome.** No undefined "slow or weird." Exceeding a budget means queueing, shedding to a cheaper path, or disconnecting idle sessions, deterministically and observably (§15). Never silent exhaustion.

```text
Database unavailable    → online mutations fail fast; desktop continues offline where policy allows
Sync unavailable        → desktop stays usable; pending mutations stay durable
Authorization changed   → capability refresh + scope update + controlled resync as needed
Cursor expired          → full shadow resync (§50)
File store unavailable  → file op becomes Pending/Unavailable; metadata never falsely says Available
```

---

## 13. Subscription & licensing

### 13.1 Context relationship

```text
Public Website (separate vendor-side system, §3, §17)
   │ (async, versioned, non-runtime-critical onboarding contract, §13.4)
   ▼
Subscription context  → plans, seats, billing periods, grace states
   │ (self-service intake and operator oversight share ONE create-tenant+subscription operation, §13.3/§13.4)
   │ publishes PlanChanged, SeatLimitReached; on provisioning, triggers a signed license artifact
   ▼
Licensing context     → verifies the signed license OFFLINE, binds it to THIS installation, enforces expiry and rotation
   │ publishes LicenseValidated, LicenseExpiring
   ▼
Core application      → reads entitlement as READ-ONLY state; never self-grants; UI toggles features from this state only
```

**[INVARIANT] Subscription (commercial billing) and Licensing (offline deployment entitlement) are never the same mechanism.** The runtime depends only on Licensing. Subscription may be fed by website self-service (§13.4, primary path) or by an operator (§13.3). Either way, the runtime's entitlement check does not change.

### 13.2 Offline license verification

```text
On startup and periodically:
  1. Load the signed license artifact
  2. Verify the signature against a locally shipped, rotation-aware key set (KeyId selects the key)
  3. Verify the license is bound to THIS installation's PROTECTED device identity:
       a key pair generated on-device at install, private key non-exportable where the platform supports it
       (TPM-backed on Windows, Secure Enclave on macOS). The license binds to the PUBLIC key, so a byte-copied
       license fails on a second machine that cannot reproduce the private key.
  4. Verify anti-replay fields (LicenseId, IssueTime, audience/product)
  5. Verify expiry against TRUSTED TIME, not one clock source:
       • server-issued trusted time persisted as a high-water WALL time
       • plus monotonic elapsed-time evidence for drift while running
       • plus rollback detection
       (a monotonic uptime counter measures time since boot, not a calendar; it cannot prove "not past 2027-01-01" alone)
  6. Result: Valid → entitlement snapshot updates
             Clock rollback / anomaly → sensitive offline operations disabled; evidence preserved; recovery needs server contact
             Invalid / Expired → deployment fails closed for gated capabilities per policy; never silently continues fully entitled
```

**[INVARIANT — anti-cloning.]** A signature proves authenticity and integrity. It **never** proves non-cloning by itself. Anti-cloning rests on the **protected device identity** (step 3): a hardware-backed, non-exportable private key where available, with the license bound to its public key. A software-only machine fingerprint plus an encrypted local secret is **explicitly rejected** (both disk and secret can be copied together; A-RULE 030). PKI and device-identity details are ADR-22 and ADR-23.

**[INVARIANT — no online revocation channel.]** No mandatory online license server exists (R11). Revocation is **approximated** by short validity windows and reissuance. A stated design property, never an "instant revoke" claim.

### 13.3 Platform-operator oversight

The Platform Operator sees and manages subscriptions through an **operator-facing surface of Subscription**. It is another authenticated, authorized client of that one context, with an operator-scoped role. Not a separate backend. It never reaches other contexts directly. It sees tenant identity only as far as Subscription needs (which tenant a plan belongs to).

### 13.4 Self-service onboarding contract (register → buy → provision)

The customer journey starts on the public website: **register → choose plan → pay → download app**. The website is an independent vendor-side system. Payment, tax and invoicing happen there. The clinic-serving system holds no payment gateway. The website hands Animora an **onboarding intent**, never a database write:

```text
Public Website ──(async, out-of-band, versioned onboarding contract)──▶
  Subscription context (ONBOARDING INTAKE):
    { requested tenant/clinic identity, plan code, paid billing period, external payment reference, idempotency key }
      │
      ▼
  Subscription provisions a tenant and a Trial/Active subscription (the SAME operation an operator can run by hand, §13.3),
  idempotently keyed so a retried webhook never double-provisions
      │
      ▼
  Licensing issues the signed, installation-bindable license (§13.2)
      │
      ▼
  Customer downloads and installs the desktop app, logs in with credentials set during onboarding, and the app binds to this installation
```

**[INVARIANT — onboarding is a MANAGEMENT-plane transport.]** R11 forbids external dependencies on the **runtime (data) plane**. It does **not** forbid a controlled **management plane**. The two must be separated and named (§13.4a). Provisioning uses at least one transport:

```text
(a) Signed provisioning package       Website → signed, versioned, idempotent package → operator workstation
                                      → delivered into the server by controlled transfer (USB / internal management link) → Subscription
(b) Vendor provisioning queue         Website → vendor-side queue → a CONTROLLED management connection
                                      (not the clinic runtime path) drains it into Subscription
```

Either way, intake is an asynchronous, idempotent, versioned contract on Subscription. It is the same create-tenant plus create-subscription operation the operator uses (§13.3), reached by the website's service identity. The clinic runtime never calls the website, and the website never calls a clinic endpoint. R8 and R11 both hold.

**[INVARIANT — the onboarding message is a signed security contract.]** It carries at least: issuer, audience, contract version, idempotency key, timestamp, expiration, signature (verified **before** any provisioning), external payment reference, and correlation ID. Replay protection applies. Lifecycle:

```text
Paid → Provisioned → Active → RenewalPending → Renewed → Grace → Suspended → Cancelled
```

Refund, chargeback and payment dispute each have a defined effect on entitlement. Never an undefined state.

**[DECISION] One provisioning code path.** Exactly one "create tenant + subscription" operation exists. Self-service and operator are two authenticated callers of it, not two implementations. No second provisioning engine.

### 13.4a Runtime plane vs. management plane

**[INVARIANT — R11 is a runtime-plane constraint, stated per plane.]**

```text
RUNTIME (DATA) PLANE   clinic operation: login, sync, commands, license verification, data access, local observability
                       → external Internet FORBIDDEN (R11, §17.4)

MANAGEMENT / CONTROL   provisioning, subscription renewal, license ISSUANCE, software update, backup export, vendor support
                       → may need controlled connectivity OR a signed offline bundle; NEVER on the clinic runtime path
```

Anything needing connectivity (onboarding §13.4, license issuance §10.6, updates §18) lives on the management plane, out-of-band. Nothing on it is a synchronous dependency of a running clinic. The runtime works with the website fully unavailable (§66).

---

## 14. Files & storage

### 14.1 Two consistency domains

**[INVARIANT] A file's SQL metadata row and its bytes are two separate consistency domains.** No single transaction commits both. An explicit ordered lifecycle is mandatory. Nothing is usable until it completes:

```text
Staged → Uploaded → HashVerified → Committed → Available
```

`HashVerified` gates `Committed`. A hash mismatch fails the upload and can never reach `Available` (§56).

### 14.2 Reconciliation

**[INVARIANT] Reconciliation is a mandatory background job.** The two domains can diverge on crash, retry or restore. Detect and resolve each case deterministically:

```text
DB row without a file      duplicate upload
File without a DB row      interrupted finalization
Incomplete upload          failed restore (DB and files out of step)
```

Each divergence has exactly one outcome: **quarantine, re-verify, garbage-collect the orphan, or resume**. Never silent drift (Gate R).

### 14.3 Transfer protocol

**[INVARIANT] File bytes never travel through the generic record-replication pipeline (§8).** A separate resumable protocol drives the lifecycle:

```text
BeginUpload → UploadChunk → Resume → Complete → VerifyHash → Commit
```

Transfers are idempotent, resumable, hash-verified, cancelable and auditable. These are first-class capabilities, not retrofits.

### 14.4 File identity & access

**[DECISION] Prefer immutable, content-addressed objects.** Replacing a file creates a **new object and a new metadata version** and never overwrites old bytes (§56.2). Access always uses the Valet Key pattern (§10.8). This section defines storage and lifecycle **behind** that access model. §10.8 defines how a client may reach a file at all.

---

## 15. Observability

**[DECISION] Zero *external* telemetry is a product constraint. Zero *observability* is not acceptable.** Every capability below works with the Internet cable disconnected.

**[INVARIANT] Every cross-boundary operation carries correlation identifiers:**

```text
RequestId · CorrelationId · TenantId · UserId · DeviceId · OperationId · MutationId · AggregateId (where applicable)
```

Any transaction must be traceable end-to-end: client → server → worker. A new cross-boundary call must thread the same correlation context.

```text
Structured logs            local, PII-minimized, correlated by the IDs above
Health / readiness         separate Liveness · Readiness · Dependency · Sync · Database · File-store health; answered locally; pollable with no external service
Diagnostics bundle         one local operation exports: app version, schema version, sync state, last checkpoint, pending-mutation metadata,
                           recent conflicts, recent errors, health, sanitized config. NO secrets. Usable for support with no cloud access.
Audit trail                append-only; every security and finance action (Audit context, §6, §61)
Sync diagnostics           per-entity cursor, backlog, resync state: "is my data actually synced?"
Settlement diagnostics     per-command lifecycle: "did this payment actually settle?"
Local metrics              scrapable on the LAN only; removing the collector must never break the app
```

**Minimum metrics (§82.2):** sync backlog · pending-mutation count · settlement latency · conflict rate · full-resync count · cursor-expired count · notification latency · DB latency · query latency · outbox backlog · dead letters · per-tenant resource usage.

**[INVARIANT — reporting freshness is visible.]** Every management-reporting projection exposes `ProjectionGeneratedAt` and `SourceVersion/SourceWatermark`, so users can tell current transactional truth from an eventually consistent report (§83).

---

## 16. Performance & scalability

### 16.1 Weak-server principle

**[DECISION] Push work to the client wherever it can be done safely without weakening §0.2.** Offline clients do local reads, search, sort, form validation, UI computation, cached dashboards and local filtering. The server does authoritative writes, cross-user validation, global reports, shared accounting, authorization and final settlement. This is the largest reduction in server load, and it is why the §8 split exists: replication is cheap and delta-based; settlement is the only work the server must do unconditionally.

Server optimization order (§84): query shape → correct indexes → projection → batch size → connection/DbContext lifetime → caching where safe → background concurrency → compression → runtime tuning → extra infrastructure. **Do not add a broker, cache or AOT because it sounds fast.**

EF Core rules (§85): `AsNoTracking`, explicit projection, pagination, short-lived scoped contexts (pool only after tenant-state proof), compiled or batched queries where measured. Avoid large object graphs, lazy-load loops, `SELECT *`, unbounded queries, N+1 and long transactions.

### 16.2 Scale-out path (deferred, gated)

**[DEFERRED] Launch = one vendor node, one process, no backplane, no broker, no distributed cache.** Each scale-out step is a **gated deployment-time escalation**, triggered by measurement, never speculative (§2.19):

```text
Web app → 2+ instances          needs session affinity + real-time backplane (shared Data Protection keys + SignalR backplane), added as a deployment switch (§73)
Noisy or large tenant           needs Deployment Stamps (§9.1, §72): move that tenant to its own DB/stamp
Heavy reporting outgrows OLTP   needs a physically separate read-model/reporting store (§11.3)
Real broker needed              replace the Outbox+relay boundary (§12.1) with an external broker behind the SAME boundary; business code unchanged (§74)
```

**[INVARIANT]** Each of these requires an **explicit ADR** before implementation. None is added "just in case."

### 16.3 Capacity methodology

**[INVARIANT] A supported concurrent-client or tenant count is never asserted from hardware specs.** It is **earned from measurement**. The benchmark exercises the full path (app memory, DB CPU and memory, session or circuit state, worker cost, filesystem overhead, sync throughput, settlement latency, reconnect behavior) on a defined hardware class under a defined synthetic workload. Only then is a number published. Method: §110. Hardware baseline: §69. SQL edition: §70.

### 16.4 PostgreSQL performance evidence and workload governance

Use bounded `pg_stat_statements` (a deployment-provisioned extension), `pg_stat_activity`, `pg_locks`, `pg_stat_database`, `pg_stat_wal`, `pg_stat_archiver`, table and index statistics, and sampled `EXPLAIN (ANALYZE, BUFFERS)` on a safe test dataset. PostgreSQL has no built-in equivalent of Query Store plan history or Resource Governor workload groups. Capture protected local query and plan evidence, plus API histograms for real p95 and p99. **Aggregate means do not prove endpoint percentiles.** Parameter values and clinical SQL payloads stay out of logs. `EXPLAIN ANALYZE` **executes** the statement. Never diagnose a production mutation with it unless inside a reviewed safe transaction.

On the co-located **2–4 GiB / 1–3 core** host, bound connections and concurrently active work **before** tuning caches (§PG.4). Npgsql pools are a small fixed set per workload, with a **combined** connection budget. A pool per tenant is forbidden. Interactive work has reserved admission. One export and one worker unit at a time initially. Reporting is single-flight, lower priority, with bounded queues, timeouts, bytes and per-tenant quotas.

Monitor RSS and cgroup memory, pool waits, row-gate contention, oldest transactions, MVCC dead tuples and vacuum progress, transaction-ID age, temporary spill and disk, feed retention, batch growth, WAL and archive lag, free space. `work_mem` applies **per sort or hash operation**, not as a global or per-connection cap. Account for the hash multiplier and parallel plans. Autovacuum stays enabled. Tune busy journal, outbox and counter tables from evidence. Long snapshots delay vacuum, so cap total export lifetime. WAL-size settings are not disk caps. Index, statistics, backup and restore have independent admission and disk budgets. Exhaustion yields `SyncBusy` or a retryable overload, **never** shortened retention, disabled durability or silently dropped work.

---

## 17. Deployment & infrastructure

### 17.1 Topology

```text
Vendor-operated server environment (no mandatory external connectivity):
  Clinic A desktop clients ──┐
  Clinic B desktop clients ──┤
  Web-browsing workstations ─┴──▶ SINGLE central server node, MULTI-TENANT
  (all tenants)                    (application + database + file store + local CA + local observability);
                                   one deployment serves every subscribing clinic, isolated by tenant (§9)

Clinics: Desktop/Web → central vendor server. No clinic operates a server.
```

The vendor runs **one** node serving **all** clinics as tenants of one shared database (§9.1). Clinics connect over the network and run no server component. **Zero external-Internet dependency (R11)** is a property of that environment: it must run fully with the Internet cable pulled. No cloud PaaS, no external IdP, no external telemetry. Local PKI and local observability only.

At launch, app, database and file store may share one physical or virtual server. This is a packaging decision, not coupling. The §4 boundaries stay distinct enough that any component can move to its own machine later without redesign. Scaling beyond one node is the gated escape hatch (§9.1, §16), not a launch target.

#### 17.1.1 Host, request pipeline, lifecycle

**[DECISION — S25/S27/S29/S30.]** Generic Host + Kestrel with hosted workers runs as a least-privilege, supervised local service. One process does not mean a globally shared DbContext. Assets are packaged. Operator, health-detail, metrics and OpenAPI surfaces are private or authorized.

```text
trusted forwarded headers (known proxies/networks) → exception handling
 → HTTPS/HSTS + bounded transport and body checks
 → routing → narrowly allowlisted CORS if needed
 → authentication → immutable tenant/session resolution
 → authenticated per-class rate and concurrency admission
 → coarse endpoint authorization
 → antiforgery for unsafe cookie-ambient browser endpoints
 → opt-in PUBLIC output caching only (after authN, authZ and CORS)
 → tenant-scoped minimal resource load → resource authorization + entitlement
 → validated Application command/query → bounded transaction → response
```

Public static files never contain clinic data. Uploads stay outside the web root. GET and HEAD never mutate business state. Unsafe cookie-capable forms and APIs (refresh, logout, upload initiation where applicable) require CSRF. Bearer-only handlers reject cookie auth. CORS defaults to same-origin, **never** wildcard origins with credentials. Grants validate resource, audience, method and expiry, and the current session and permission when instant online revocation is promised. A TTL alone leaves a window. Cached responses never skip resource authorization.

Set transport limits at **proxy/IIS and Kestrel**: regular and upgraded connections, HTTP/2 streams, headers, body, decompressed payload, timeouts, slow or chunked uploads. Upgraded WebSockets do not count against regular Kestrel connections. Cap upgraded connections and SignalR/circuit admission separately. Override body limits before reads. Stream bounded files into verified staging. Async end to end. No `.Result`, `.Wait`, fire-and-forget business action, or `Task.Run` as durability.

**Liveness** is cheap process responsiveness with no dependencies. **Readiness** requires a compatible schema, feed/epoch/scope registry, critical keys and config, and bounded DB and store access. Optional file, report or hint faults cause **surface-specific degradation**, not restart loops. Bounded deep operator checks cover journal, cleanup, worker lag and key decryptability. Never an integrity scan or full sync on every probe. Protect details. Probes are non-cacheable. During recovery or startup, readiness stays closed until required validation passes. Shutdown closes admission and readiness, then drains finitely. Crashes recover through SQL atomicity, durable jobs and client journals.

### 17.2 Environments

```text
Development   full-feature, non-production, disposable data
Staging       mirrors production topology exactly (single node, multi-tenant); rehearses upgrade, rollback and tenant-isolation tests before touching real tenants. Never shares production's Data Protection ring.
Production    the vendor's single multi-tenant node serving all clinics; tenant isolation (§9) is the boundary between clinics, not separate servers
```

### 17.3 Recovery (server and client are different problems)

**[INVARIANT] Server backup and client recovery are different problems and are never conflated.** The server backs up its full state: database, file store, configuration, and **runtime** key material (server certificate and Data Protection ring, **not** the offline root CA or license-signing keys, §10.5/§10.6). A client holds different state: encrypted local replica, pending commands, device identity. Server backup does **not** cover it.

**[INVARIANT — RTO and RPO are declared numbers; backups are tested; copies are isolated.]** The SLO, RTO and RPO are explicit targets (§1, R-DR). A single-node launch is a valid product decision only with those numbers stated and met. Keep **at least two backup copies, at least one off-host and immutable** (ransomware resilience). A restore counts as "recovered" only after a **successful test restore**. "A backup exists" is not recovery. A **Recovery Point Manifest** ties one restore point together so DB, files and keys restore to one consistent point:

```text
BackupSetId · DatabaseIdentity · PostgreSQLSystemIdentifier/Timeline/LSN/BaseBackupManifest ·
DatabaseRecoveryPoint · ServerFeedEpochAtBackup · FileStoreSnapshotVersion/Watermark ·
KeySetVersion/DecryptorReferences · LicenseVerificationKeyVersion · ServerBuildVersion · SchemaVersion ·
SyncContract/ScopeRegistryVersion · ArtifactChecksums · OffHostDurableAt

RecoveryPointId = manifest identity of the selected consistent restored point
ServerFeedEpoch = fresh post-recovery continuity identity, never reused from backup
```

| Incident | Recovery source |
|---|---|
| Server DB corruption | Database backup/restore |
| Server file-store corruption | File-store backup/restore |
| Server total loss | Full server backup (DB + files + **runtime** keys + license *verification* material) restored to one Recovery Point Manifest. Root CA and license-signing keys restored from **separate offline custody** (§10.5/§10.6). |
| Client disk loss | Server resync for replicated state + explicit pending-command recovery policy (below) |
| Client device stolen | Device revocation (§10.4). Offline capability ends at next contact. Until then bounded only by snapshot expiry and local device protection. |
| Corrupt client replica | Quarantine it; rebuild fresh via shadow resync (§50, §77) |
| Client offline past retention | Full resync, not incremental (§18, §50) |

**[INVARIANT — unsent commands and client disk loss, stated honestly.]** A server resync restores only what the server already has. A command created offline that **never reached the server** exists only on the lost disk, so resync cannot recover it. Pick **one** explicit policy:
(a) unsent mutations are protected by an **encrypted local backup or export** that survives disk loss, or
(b) they are declared **unrecoverable**, and the UI warns the user that locally committed, unsynchronized work is lost on disk loss (Gate U, §76).
The system **never** tells users that "queued" means "stored on the server."

**[INVARIANT — restore may move the server's change position backwards.]** A restored server may have a journal version **lower** than a client believes. A client must never assume the version rises monotonically across a disaster restore. After recovery, check `RecoveryPointId`, DB identity, schema version and feed validity. Affected clients may be forced into a controlled full resync when the restore point invalidates their cursor. This is safer than faking continuity across an erased history window (§78).

**Mandatory epoch protocol (S01).** Before serving traffic after a restore, a rollback with data loss, DB replacement, journal trigger disable/re-enable, or feed reconstruction, recovery tooling mints a fresh, unpredictable `ServerFeedEpoch`. It is **not** derived from a backup timestamp or counter. Readiness stays closed until the epoch is installed, runtime keys are verified and feed validation completes. Compare the epoch on **every** sync and settlement handshake. A mismatch forces controlled recovery, even if the restored counter has overtaken the client's old number. A checkpoint greater than the current version, or a missing or disabled journal, is an error, **never** a valid empty batch.

A restore can erase both a committed fact and its idempotency receipt. Clients keep acknowledged operation IDs and outcome evidence through the recovery horizon, and quarantine formerly settled operations in a **recovery inbox**. They must not silently replay them or claim an external payment was rolled back. An audited recovery command reconciles receipt, ledger and any external evidence before choosing restore, reapply, compensate or manual review. A full replica resync repairs replicated state, **not** lost business history. RPO allows loss within its window. Do not advertise at-most-once effects within healthy history as zero-loss or exactly-once across a destructive restore.

#### 17.3.1 Backup, key custody, restore acceptance

Production recovery uses PostgreSQL physical base backups (`pg_basebackup`) plus an unbroken **off-host WAL archive including timeline history**, with PITR tested on replacement hardware (S33/S34). Provision `wal_level=replica`, `archive_mode=on`, a durable authenticated archive command or tool, and initially `archive_timeout=60s`. Full base cadence starts daily and is adjusted to measured restore and disk budgets. Switching out a mostly empty WAL segment still archives a full segment. Budget that cost on this small host. Alert when the newest verified off-host DB, file or key recovery point is older than **five minutes**. Cadence is a target, not proof of RPO. WAL replay does **not** make `pg_dump` a physical backup. Logical dumps serve portability and migration and need `pg_restore` tests.

Archive commands acknowledge success only after durability. They never overwrite a conflicting segment. Identical retries are idempotent. Archive failure can fill `pg_wal`. Monitor continuously and gate writes **before** disk exhaustion. Never delete needed WAL as a repair. Back up `postgresql.conf`, `pg_hba.conf`, role and grant definitions, extension versions and certificates **outside** the WAL chain. PITR installs a fresh application `ServerFeedEpoch` before readiness. Timeline and LSN are recovery provenance, **not** client delta cursors.

Enable data checksums at provisioning. Verify backups with `pg_verifybackup` where applicable. Periodically run real restores. Run `pg_amcheck` as supported. Validate tenant-qualified FKs, balanced ledger, stock and receipt invariants, file hashes, key unprotection and the client recovery handshake. Neither checksums nor manifest verification replace real restore tests and domain-integrity tests. Restore known-good data. Never silently accept corruption or data-loss repair.

Immutable file bytes can satisfy an older DB point only if every referenced hash-verified object and the required decryptor exist. GC preserves all supported recovery points. The file, key and DB manifest, root and license key offline custody, pending-command preservation and receipt reconciliation stay unchanged.

Community PostgreSQL has no SQL Server-style TDE or Always Encrypted. Use protected encrypted volumes, separately encrypted backups and files, and recoverable off-host key custody. Application-level field encryption is deferred until a threat, query, index and recovery review. TLS over TCP uses Npgsql `SSL Mode=VerifyFull` with a trusted root and matching hostname. **Certificate-bypass settings are forbidden.** A local Unix socket may use reviewed OS peer-auth ACLs. PostgreSQL listens only on required private interfaces, with SCRAM authentication and least-privilege runtime, migration, backup and break-glass roles. At-rest encryption never replaces authorization against a legitimate DB or app principal.

### 17.4 Zero-Internet isolation (tested property)

**[INVARIANT] "Zero external-Internet dependency" is a *tested* property, not a claim.** Validate by removing one at a time: DNS, general Internet, external time sync, OCSP/CRL, any CDN, package and update feeds, the telemetry backend, and the public website. After each removal, confirm **Login, Authorization, core reads, Business commands, Critical sync, File access, Local observability and Licensing verification (per offline policy)** still work. This holds **provided the client still has a route to the vendor server**. No cloud, CDN or third-party runtime dependency is required, but clinic-to-vendor connectivity is required for sync. If the only public route is pulled, remote clinics are disconnected: desktop local work continues provisionally, and central collaboration and the online web UI resume when the route returns. This requirement does **not** promise offline peer-to-peer visibility or a working web session with an unreachable server. A certificate-validation path that silently reaches an external revocation service is a hidden dependency and a defect.

---

## 18. Cross-platform consistency & versioning

**[INVARIANT]** The desktop installer's upgrade and rollback behavior and the sync-contract versioning of §8.11 are related but separate. An installer test never replaces a **mixed-fleet compatibility** test. Fleets are staggered by nature. Validate both together (Gate M).

**Server schema uses expand/contract (§79):** expand → deploy compatible server → deploy compatible clients → migrate usage → contract and remove. Do not drop columns or change semantics that older supported clients still need. Supported fleet: **N / N-1 / N-2**. A migration succeeds only if server DB, existing clients, offline replicas and pending mutations all remain compatible across the window (§80).

**[DECISION]** Desktop packaging is a delivery preference, gated by site constraints (peripherals, elevated installers, enterprise policy), not a hard commitment. The only requirement: **signed, verifiable, rollback-capable, offline-installable** delivery on both desktop OSes. macOS builds are signed, notarized and stapled before release. **No runtime dependence on Internet-hosted update feeds** (§113).

---

## 19. Architecture validation gates (fitness functions)

Gates prove the architecture's behavioral promises. Technology-risk PoCs are in `STACK.md`. A gate failure means the **pattern or boundary is wrong**, not merely a bug.

| Gate | Proves | Traces to |
|---|---|---|
| **A — Patient conflict** | Two offline devices editing one non-financial record converge deterministically, with audit, no lost field | §8.12–§8.14 |
| **B — Independent financial commands** | Two offline devices, independent commands, both settle once, correct ledger | §8.6, §11.1 |
| **C — Duplicate command** | Same `OperationId` sent twice → exactly one financial effect | §8.6.1 |
| **D — Lost response** | Commit, response lost, retry same `OperationId` → prior result, no second transaction | §8.6.1, §8.6.3 |
| **E — Cross-tenant attack** | Tenant A requesting a Tenant B id fails at every mandatory layer, no existence oracle | §9.2, §9.5 |
| **F — Critical delta integrity** | Journal-backed paged pull under concurrent writes, retries, disconnects, skew and crashes → zero unrecoverable skip and zero unrecoverable duplicate | §8.4, §16, §17-CT |
| **G — Crash during settlement** | Business transaction atomic; outbox recoverable; retry safe | §12.1 |
| **H — Desktop crash mid-commit** | Durable local state (atomic journal); no command loss; no double effect | §8.5.1, §8.6 |
| **I — Reconnect storm** | Jitter, backoff, progressive catch-up, per-tenant and global limits hold under mass reconnect | §12.3, §88 |
| **J — Tenant isolation with RLS OFF** | Isolation holds with RLS fully disabled | §9.2, §9.4 |
| **K — Local encryption** | Wrong key fails; plain reader fails; create, close, reopen, WAL, crash, rekey, restore, migration all pass | §49, §92 |
| **L — Tenant-safe cache** | Tenant A never gets Tenant B's cached result; a missing key component fails the test | §10.7 |
| **M — Mixed-version fleet** | Old client ↔ new server AND new client ↔ old supported server, across N/N-1/N-2 | §8.11, §18 |
| **O — Cross-pipeline dependency** | Every interleaving of dependent mutation → command → command resolves by settlement, never arrival order | §8.9 |
| **P — Idempotency fingerprint** | Same fingerprint replay = prior result; different fingerprint with same `OperationId` = audited integrity error | §8.6.1 |
| **Q — Offline-session policy** | Max window enforced; revoked device refused on reconnect; credential change invalidates cached session; recovery restores access | §10.4 |
| **R — File lifecycle & reconciliation** | Every divergence resolves deterministically; no `Available` before verified bytes; access always Valet-Key gated | §14, §56–§57 |
| **S — Period-close race** | Device offline across close, reconnecting with commands dated into the closed period, resolves by stated policy, never silently posts | §11.1 |
| **T — Contended allocation** | Two clients booking the same slot or room from different bases → exactly one succeeds, server-side, in a transaction | §11.4, §37 |
| **U — Client disk loss** | Unsent commands follow the declared policy (encrypted backup recovery, or user warned); never assumed synced | §17.3, §76 |
| **V — Delete resurrection & tombstones** | Offline edit to a server-deleted row resolves by domain policy; a device offline the full window still sees every deletion or revocation (retention ≥ window + margin) | §8.14, §18, §19-tomb |
| **W — Initial/full-sync consistency** | First or full sync under concurrent multi-table writes lands on one snapshot watermark, referentially consistent, never patchwork | §16-CT, §50 |
| **W-csrf — Browser anti-forgery** | Every state-changing browser request without valid anti-forgery is refused; CORS alone does not admit it | §10.2 |
| **X — Backup-restore consistency** | DB, files and runtime keys restore to one Recovery Point Manifest within RTO, loss ≤ RPO, from a tested backup; offline root and signing keys from separate custody | §17.3, R-DR |
| **Y — Direct-SQL state visibility** | A governed direct-SQL mutation on a critical-sync table becomes sync-visible via the journal | §20 |
| **Z — Direct-SQL business protection** | Critical invariants cannot be silently bypassed by ordinary SQL | §20 |
| **AA — Echo safety** | A client's own committed change replayed in a later delta applies idempotently | §8.15 |
| **AB — Notification independence** | A lost notification never causes a permanent missed sync | §44–§45 |
| **AC — Tenant QoS / noisy neighbor** | No single tenant can consume all server resources | §12.4, §71 |
| **AD — Poison outbox / cursor expiry** | Permanently failing effect reaches DeadLetter with reason after bounded budget and surfaces to an operator; expired journal cursor always triggers correct full resync | §12.1, §18 |
| **AE — Full-resync safety** | Shadow rebuild cannot destroy the last good local replica, and never deletes unsent mutations | §50, §51 |
| **AF — License / key compromise, anti-clone** | Signature and rotation valid; copied license fails on a second installation; a wound-back clock cannot extend expiry; a compromised runtime cannot mint a license | §10.6, §13.2 |
| **AG — Local observability, Internet pulled** | Every observability capability works with zero external connectivity; removing a local collector never breaks the app | §15, §17.4 |
| **AH — Bidirectional clinic visibility** | A creates or updates; B's active list refreshes; then roles reverse. Target includes durable apply and rendered refresh, not just hint delivery | §1.1, §8.17, §44 |
| **AI — Same-version paging and batch expiry** | Multi-page batch at one version, repeated or out-of-order pages, crash, expired batch: no checkpoint advances over incomplete data | §17-CT |
| **AJ — Pull fairness / editor safety** | Poison or conflicted push cannot block peer changes; delayed receipts do not overwrite newer base or edits; open editor survives committed refresh | §8.10, §8.17 |
| **AK — Recovery epoch / lost receipts** | Restore counter overtakes old cursor; epoch still invalidates it; erased settled receipts route to reconciliation, not blind financial replay | §17.3 |
| **AL — Scope exits, metadata isolation** | Row leaves a predicate, soft or hard delete, grant revoked mid-batch: no stale unauthorized rows; no cross-tenant journal keys or provenance escape | §17-CT.3, §53–§54 |
| **AM — Concurrent duplicate settlement** | Simultaneous same-ID submissions, commit uncertainty and fingerprint mismatch yield one effect and an authorized durable outcome | §8.6.1 |
| **AN — Live edits during shadow rebuild** | User saves during snapshot download and before cutover; every committed journal record survives crash and cutover exactly once as intent | §50 |
| **AO — Batch-chain arbitration** | Reordered or overlapping complete batches cannot regress or skip checkpoints; unknown export commit recovered by manifest | §17-CT.4 |
| **AP — Worker fencing / ordering** | Expire and reclaim a paused worker's lease, then resume the old owner: fenced completion and inbox protect local effects; poison blocks only its key | §12.2.1 |
| **AQ — SQL allocation races** | Unequal-start overlapping intervals, stock = 1 with two sales, posting vs close, multi-resource deadlocks preserve invariants on production SQL | §11.4.1 |
| **AR — Admission / host limits** | Reconnect + slow upload + WebSocket/HTTP2 + reports stay within memory, DB and queue budgets; P0 does not starve backup, outbox or pull | §12.4.1, §17.1.1 |
| **AS — Pooled tenant/session lifecycle** | A→B→A, reconnect, parallel and cancellation with RLS on and off cannot retain the previous tenant or misclassify workload; missing context denies | §9.4, §16.4 |
| **AT — Security / key lifecycle** | Correct opaque-token handler, current revocation, expired-key unprotection, replacement-host decryptor restore; cache cannot bypass resource authZ | §10.2–§10.7 |
| **AU — Recovery / performance evidence** | Real off-host log, file and key restore meets RPO and RTO; pg_stat_statements aggregates are never reported as end-to-end p95/p99 | §16.4, §17.3.1 |

**Session scope.** These are acceptance specifications for future runtime work. **None is asserted executed by this revision.** Do not run builds or tests merely because a file lists gates, when the task forbids it.

**[INVARIANT]** If any gate fails, the affected boundary or pattern is fixed **before** feature work continues on it. Gates protect the architecture's own promises, not a library's behavior. GA (§115) requires every critical gate to pass, no silent-data-loss defect, no open sync-cursor issue, proven backup-restore, native encryption, tenant isolation and mixed-version contracts, and documented RPO/RTO and capacity budget met.

---

## 20. Direct database changes & maintenance governance

Direct DB maintenance is a **first-class requirement** (R9), but it is **governed**. The critical sync feed and business invariants both depend on *how* a direct change is made.

### 20.1 Safe direct state mutation

**[DECISION]** A direct SQL `INSERT` or `UPDATE` on a **critical-sync table** under Animora's maintenance policy is visible to the critical feed, because the PostgreSQL journal captures qualifying DML:

```text
DBA / governed maintenance SQL → PostgreSQL DML → transactional change journal → client delta
```

This holds for the custom critical path, which uses no `UpdatedAt` cursor. It does **not** make arbitrary SQL against a Datasync `SYNC-SIMPLE` table safe. Datasync relies on library metadata (`UpdatedAt`, `Version`, `Deleted`). Direct SQL there is allowed only through a maintenance operation that preserves every Datasync invariant, or after migrating the table to the critical path.

```text
Critical sync table          → governed direct SQL can stay sync-visible
Datasync SYNC-SIMPLE table   → arbitrary direct SQL is FORBIDDEN
```

### 20.2 Direct business mutation is forbidden

**[INVARIANT] Direct SQL must never bypass critical business invariants.** Forbidden as arbitrary SQL:

```text
UPDATE PostedInvoice SET Total = ...      UPDATE JournalEntry SET Amount = ...
UPDATE Payment SET Status = ...           UPDATE StockBalance SET Quantity = ...
UPDATE FiscalPeriod SET Status = Closed ...
```

A required business correction uses an **Administrative Command** or an **Audited Maintenance Procedure**. It calls the same domain or application rules, or a specifically defined controlled correction path. **Never a raw value overwrite** (Gate Z).

### 20.3 DBA access classes & break-glass

```text
ReadOnly Operational → Routine Maintenance → Emergency Break-Glass
```

Break-glass requires: identity, reason, ticket reference, timestamp, operator and audit. **Hard-deleting a synchronized entity is forbidden in ordinary production paths.** It is allowed only through a reviewed maintenance operation that first proves every sync and retention consequence (§19).

---

## 21. Architecture decision log (condensed)

| # | Decision | Status | Driver |
|---|---|---|---|
| AD-01 | Modular monolith over microservices at launch | Accepted | R2, R12, §2.19 |
| AD-02 | State replication and business intent are two permanently separate pipelines | Accepted (spine) | §0.1, R4, R5 |
| AD-03 | Server is the single global authority; clients hold provisional truth only | Accepted (spine) | §0.2, R5 |
| AD-04 | Split client presentation, shared core | Accepted | R1, R2, R4 |
| AD-05 | Shared-DB, tenant-discriminator multitenancy with Deployment Stamps escape hatch | Accepted | R8 |
| AD-06 | Defense-in-depth isolation: app-layer mandatory, SQL RLS hardening only | Accepted | R3, R8 |
| AD-07 | Authentication, authorization and entitlement as three independent planes | Accepted | R8, R3 |
| AD-08 | Explicit, capability-scoped offline-session policy separate from online auth | Accepted | R4, R11 |
| AD-09 | Append-only ledger + movement-based inventory (never absolute offline writes) | Accepted | R5, R6, R7 |
| AD-10 | Idempotent Consumer + Transactional Outbox + Competing Consumers for all async effects | Accepted | R3, R4 |
| AD-11 | Valet Key for all file access; no filesystem path ever reaches a client | Accepted | R3 |
| AD-12 | Fully local, layered observability; zero mandatory external telemetry | Accepted | R11 |
| AD-13 | Single-node deployment at launch; every scale-out step is an explicit, gated ADR | Accepted | R2, R12, §2.19 |
| AD-14 | Local PKI (offline Root CA + online Issuing CA) provisioned by deployment tooling, never app code | Accepted | R11, R3 |
| AD-15 | Licensing (offline entitlement) architecturally independent of Subscription (commercial) | Accepted | R8, R11 |
| AD-16 | Offline revocation is a next-contact guarantee, bounded by snapshot expiry | Accepted | R4, R11, §10.4 |
| AD-17 | Tombstone, journal, authorization and license retention ≥ max offline window + margin (one linked decision) | Accepted | R4, §1.1, §18 |
| AD-18 | **Critical `ChangeSequence` implemented by the PostgreSQL transactional change journal** | **Accepted — replaces the earlier "custom opaque sequence only" baseline** | R4, R5, R9 |
| AD-19 | Inventory concurrency: unconfigured class fails closed to review; tenant negative-stock, reservation and lot policy needs explicit authorization | Accepted safe default; product configuration required | R5, R7, §11.2 |
| AD-20 | Scheduling allocation model (server-transaction allocation, not version tokens alone) | Accepted | R5, §11.4, §37 |
| AD-21 | General-ledger posting model + period-close state machine; unconfigured offline-period collision holds for review, never implicit back-post | Accepted safe default; accounting configuration required | R5, R6, §11.1 |
| AD-21b | Cross-context financial consistency declared per interaction (atomic vs. eventual), no silent gap | Accepted | R5, §6.2, §38 |
| AD-22 | PKI hierarchy: offline Root CA + online Issuing CA; root and signing keys never on the runtime node or in its backup | Accepted | R3, R11, §10.5/§10.6 |
| AD-23 | License-signing key custody: offline issuer; runtime holds verification keys only; device identity hardware-backed where supported | Accepted | R3, R11, §13.2 |
| AD-24 | Offline subscription provisioning over an explicit management plane (signed package or controlled queue), separate from runtime | Accepted | R8, R11, §13.4/§13.4a |
| AD-25 | Backup topology + RTO/RPO/SLO: ≥2 copies, ≥1 off-host + immutable, tested restore, Recovery Point Manifest | Accepted | R-DR, §17.3 |
| AD-26 | Tamper-evident security/financial audit (hash-chained or signed batches, separate audit key) beyond plain append-only | Accepted | R3, §61, §104 |
| AD-27 | Client disk-loss semantics for unsent commands (encrypted local backup, or declared unrecoverable + user warning) | Accepted | R4, §17.3, §76 |
| AD-28 | **Critical Sync Engine is custom and journal-backed** | Accepted | R4, R5, R9 |
| AD-29 | **Datasync restricted to low-risk/simple (SYNC-SIMPLE) replication only** | Accepted | R4, §8.2/§8.3 |
| AD-30 | Client-local Mutation Journal owned by Animora, atomic with local state | Accepted | R4, §8.5 |
| AD-31 | Cross-pipeline causal dependency DAG (`CausationId` ≠ `DependsOnOperationId`) | Accepted | R5, §8.9 |
| AD-32 | Patch-based state mutations where supported, with the conservative-merge rule | Accepted | R5, §8.13 |
| AD-33 | Direct-SQL mutation governance (state-visible on critical tables; forbidden for business invariants) | Accepted | R9, §20 |
| AD-34 | Production central PostgreSQL baseline = PostgreSQL 18, 2–4 GiB / 1–3 cores (capacity target, not proof) | Accepted | R2, §70 |
| AD-35 | Desktop auth uses the stack-approved local Identity token mechanism; no generic OIDC claim | Accepted | R3, §10.2 |
| AD-36 | Notification channel is non-authoritative (hint only) | Accepted | R10, §44/§45 |
| AD-37 | Sync-scope / SyncProfileVersion versioning; scope change may force controlled resync | Accepted | R13, §53/§54 |
| AD-38 | Full resync uses a shadow replica, switched in atomically | Accepted | R4, §50 |
| AD-39 | Full resync never deletes unsent mutations | Accepted | R4, §50/§51 |
| AD-40 | Financial state never enters generic CRUD settlement | Accepted | R5, R6, §8.3 |
| AD-41 | Self-hosted SignalR hints at launch, mandatory anti-entropy pull, no launch backplane | Accepted | R2, R10, §44; S08–S10/S15 |
| AD-42 | One-snapshot immutable paged batches; completed-range checkpoint, not per-row maximum | Accepted | R4, R5, §17-CT; S01/S02 |
| AD-43 | Server base + pending overlay, fair coordinator, committed UI invalidation | Accepted | R4, R5, R10, §8.17 |
| AD-44 | Atomic receipts + effects, canonical fingerprints, retained replay protection | Accepted | R3, R6, §8.6.1; S04/S05/S11 |
| AD-45 | Feed epoch on restore or reset; explicit lost-settlement recovery | Accepted | R-DR, §17.3; S01 |
| AD-46 | Generation-based shadow cutover with independently durable journal | Accepted | R4, §50 |
| AD-47 | Dynamic row-scope exits require membership transitions or scope rebuild; never filtered-away deletion | Accepted | R3, R5, §17-CT.3 |
| AD-48 | Scoped tenant contexts first; pooling gated; RLS connection setup and journal filtering separate | Accepted | R3, R8, §9.4 |
| AD-49 | SQL resource and period gates, deterministic ordering, full durability, typed deadlock and conflict recovery | Accepted launch default | R5–R7, §11.4.1 |
| AD-50 | SQL durable job truth + fenced claims + ordered progress; channels are bounded wake aids | Accepted | R2, R3, §12.2.1 |
| AD-51 | Tenant, class and global admission with weighted progress, host budgets and readiness | Accepted; measured values required | R2, R10, §12.4.1/§17.1.1 |
| AD-52 | No clinical authenticated HTTP output cache; DP ring and decryptors retained; Identity opaque tokens are not JWT | Accepted | R3, §10.2/§10.6/§10.7 |
| AD-53 | pg statistics and protected plans, histograms for percentiles; bounded combined pools and admission; no Governor | Accepted | R2, §16.4 |
| AD-54 | FULL/log/off-host coverage + referenced files and decryptors; backup cadence is not RPO proof | Accepted operational baseline | R-DR, §17.3.1 |

---

## 22. Architecture rules for AI agents (mandatory)

```text
A-RULE 001  Domain code never depends on infrastructure and references no sync abstraction; the sync port is Application-owned; the engine (custom or Datasync) sits behind it in Infrastructure.
A-RULE 002  Domain code never references a replication library (e.g. Datasync) directly.
A-RULE 003  Critical sync uses the Animora Custom Sync Engine (journal-backed); a third-party library is never the authority.
A-RULE 004  Datasync is permitted ONLY for explicitly classified SYNC-SIMPLE entities that pass the qualification gate.
A-RULE 005  Posted accounting and financial facts are immutable.
A-RULE 006  Corrections to posted finance are compensating or reversal commands, never an in-place UPDATE.
A-RULE 007  Inventory is movement-based; a stale client never writes an absolute stock quantity.
A-RULE 008  A contended shared-resource invariant (slot, room, cash drawer, seat, period, device allocation) is enforced in a server transaction, never by an optimistic version token alone.
A-RULE 009  Every business mutation carries a stable OperationId.
A-RULE 010  Same OperationId + same CommandFingerprint = replay → return the prior result; never a second effect.
A-RULE 011  Same OperationId + different CommandFingerprint = security/integrity error; audited and rejected.
A-RULE 012  The server is the final global authority for settlement; a client holds provisional local truth only.
A-RULE 013  A client may commit provisional local state while offline (atomic with its Mutation Journal record).
A-RULE 014  A client-local financial state is never labeled globally posted, settled or closed until server settlement succeeds.
A-RULE 015  TenantId is resolved only from authenticated membership, server-side; never from client input.
A-RULE 016  The resolved tenant context is immutable for the whole request or work item.
A-RULE 017  An inbound sync pull never destructively overwrites unsent local business intent.
A-RULE 018  Authorization is re-evaluated server-side; a client never trusts its own authorization decision.
A-RULE 019  Authentication, Authorization and Entitlement are checked independently, never merged.
A-RULE 020  A notification or real-time channel is never the authoritative sync data channel.
A-RULE 021  The critical delta cursor advances only after durable local apply.
A-RULE 022  Sync is at-least-once; local apply is idempotent (echo-safe).
A-RULE 023  A full resync never deletes Animora-owned unsent mutations, and is built in a shadow replica.
A-RULE 024  The critical delta cursor is the opaque server-issued ChangeSequence (journal version); UpdatedAt is display/audit only, never a critical cursor and never an ordering source.
A-RULE 025  entity-version token is concurrency-detection metadata, never a sync cursor and never a business sequence.
A-RULE 026  Client wall-clock time is never a sync or licensing authority.
A-RULE 027  CausationId (provenance) and DependsOnOperationId (settlement precondition) are distinct; dependency edges form a DAG; no dependent operation settles before its prerequisite settles.
A-RULE 028  The replication queue and the command queue are never assumed globally ordered; network arrival order is never business order.
A-RULE 029  The journal is not a substitute for domain events; the Outbox is not a substitute for state sync; the two feeds are never merged.
A-RULE 030  A license signature proves authenticity, not non-cloning; installation binding MUST use a protected (hardware-backed, non-exportable where supported) device identity. A software-only machine fingerprint is NOT accepted as anti-cloning proof.
A-RULE 031  Adding a backplane, broker, microservice, distributed cache or partitioned tenant requires an explicit ADR.
A-RULE 032  Zero external telemetry is a constraint, not an excuse for zero observability.
A-RULE 033  Every cache key for tenant, clinical or financial data includes tenant + user/permission + resource + version scope; caching runs only after authN/authZ.
A-RULE 034  Heavy reporting and aggregation never ride the OLTP transaction path.
A-RULE 035  A capacity or concurrency claim is earned from measurement, never asserted from specs alone.
A-RULE 036  Client-originated TenantId, UserId, DeviceId, Role, Entitlement, AccountingDate, AuthorizationDecision or settlement/posting state is NEVER authoritative; the server derives all of them from the authenticated, device-bound credential.
A-RULE 037  Every supported offline duration has tombstone, authorization-revocation, journal and licensing retention windows at least as long as that window plus a safety margin.
A-RULE 038  A client-local financial state may be provisional; no UI label, event, report or local state may represent it as globally posted, settled or closed until server settlement succeeds.
A-RULE 039  Trust-minting private keys (Root CA, license-signing) never reside on the runtime server; it holds public verification keys only.
A-RULE 040  Every state-changing browser request requires anti-forgery protection; CORS is not a substitute.
A-RULE 041  A filesystem path is never an access credential: Valet Key only; file availability requires verified bytes.
A-RULE 042  Every async or at-least-once effect has an idempotent consumer; every worker step is resumable after a crash without duplicating effects.
A-RULE 043  Every retry has a bounded budget; a connectivity outage transitions a client to offline mode, never a retry storm.
A-RULE 044  A stale write is detected (version mismatch), never silently overwritten; the mismatch never by itself decides the business conflict.
A-RULE 045  Automatic field-level merge requires provable non-overlap; insufficient evidence ⇒ Conflict/NeedsAttention, never a guess.
A-RULE 046  Critical direct-SQL business mutation is forbidden outside a governed maintenance path; governed direct SQL on a critical-sync table must remain journal-visible.
A-RULE 047  An expired journal cursor causes a controlled full resync: never a guess, a silent skip, or a start from the current version.
A-RULE 048  Hard-delete of a synchronized entity is forbidden in ordinary production paths (tombstone instead).
A-RULE 049  A SyncProfile or scope change may require controlled resynchronization; an old cursor is never reinterpreted as covering a new dataset.
A-RULE 050  Shared-workstation user profiles never share offline authority implicitly; each user gets a logically separate encrypted local replica.
A-RULE 051  The public website is never a clinic runtime dependency; runtime operation continues with zero external Internet.
A-RULE 052  Every architecture boundary has a validation gate (§19); a failing gate blocks feature work on top of it.
A-RULE 053  Journal entries may span many tables per atomic transaction; only a complete immutable batch's ToVersion advances its epoch- and scope-bound checkpoint.
A-RULE 054  No SQL snapshot transaction is kept alive across HTTP page requests; materialize authorized payloads, not just keys, under one bounded snapshot.
A-RULE 055  Authoritative base, overlay/conflict decisions and completed checkpoint commit atomically; UI invalidation follows commit.
A-RULE 056  Push retry and conflict never starve pull; hints and polls wake one coordinator per local profile.
A-RULE 057  Critical state mutations and commands use atomic durable receipts plus effects; concurrency is arbitrated by SQL uniqueness.
A-RULE 058  Restore or feed reset changes ServerFeedEpoch before readiness; erased acknowledged business effects require explicit recovery reconciliation.
A-RULE 059  Scope exit or deletion removes authorized local membership; never rely on current-row filters, RLS or hub groups to secure journal metadata.
A-RULE 060  Shadow resync keeps new UI saves in the independent durable journal and switches base generations under a short local cutover transaction.
A-RULE 061  SignalR launch hints carry no records or authoritative cursor; reconnect reauthorizes or rejoins and unconditionally catches up.
A-RULE 062  An architecture document is not a runtime acceptance result; no unmeasured 10/10 or zero-loss-over-partition claim.
A-RULE 063  Delta apply compare-and-swaps exact FromVersion, epoch, scope and base generation; stale batches never regress a replica.
A-RULE 064  Server-derived origin context is origin evidence, not authorization, history, or a safe blanket echo-suppression predicate.
A-RULE 065  Channels are not durable admission; SQL jobs/outbox, fenced leases and predecessor checks own ordered recovery.
A-RULE 066  Snapshot isolation cannot replace period, stock or interval allocation gates and DB constraints.
A-RULE 067  Pooling explicitly establishes each tenant; OnConfiguring, RLS and Governor never imply per-request state reset.
A-RULE 068  Middleware order cannot secure authenticated output caches; sensitive runtime responses are not output-cached at launch.
A-RULE 069  Built-in Identity tokens are not JWTs; device binding, refresh reuse and current revocation need explicit server policy.
A-RULE 070  Recovery needs off-host log, file and decryptor coverage and actual restore, not FULL mode or backup success alone.
A-RULE 071  pg_stat_statements aggregates are not request p95/p99; percentile claims require local histogram or trace measurements.
A-RULE 072  Root and license keys stay offline; runtime capability, grant, DP and TLS keys have distinct custody and purposes.
A-RULE 073  Strict priority cannot starve pull, outbox or backup; queues, partitions, snapshots and retries have finite budgets.
```

**Distinctions never collapsed:**

```text
Server business truth            ≠ server replication truth ≠ client provisional truth
state replication                ≠ business intent settlement
critical sync                    ≠ Datasync SYNC-SIMPLE CRUD
state mutation                   ≠ business command
transactional change journal     ≠ domain event / Outbox
ChangeSequence (delta cursor)    ≠ entity-version token (concurrency) ≠ UpdatedAt (display/audit)
entity-version token             ≠ OperationId ≠ DatasyncOperationId
concurrency detection            ≠ business conflict resolution
idempotency                      ≠ ordering
at-least-once delivery           ≠ exactly-once side effect
provisional offline state        ≠ global posted/settled/closed truth
LocallyCommitted                 ≠ Posted/Settled (server-authoritative)
notification received            ≠ data synchronized
authentication ≠ authorization ≠ entitlement
RBAC                             ≠ resource-based authorization
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

## 23. Non-goals

- **Microservices at launch.** Modular monolith is the launch architecture. Extraction needs an ADR (§16.2, A-RULE 031).
- **A third-party sync library as the universal or authoritative mechanism.** Datasync is a contained SYNC-SIMPLE adapter. The custom journal engine is the authority (§8.2, A-RULE 003/004).
- **Web offline capability.** The web client is intentionally online-only (§7.2). A boundary decision, not an oversight.
- **Generic CRDT semantics or global last-write-wins for financial data.** Convergence is policy-driven (§0.2, §8.12).
- **A generic distributed-transaction or saga coordinator.** Cross-context coordination uses domain events and the Outbox inside one process (§6.2, §22-A).
- **A mandatory message broker, Redis or distributed cache at launch.** Outbox plus in-process relay is the launch reliability design. Each is an additive, gated future step (§16.2, §74).
- **Any mandatory cloud identity, telemetry, storage or license-validation service at runtime.** Would violate R11.
- **Using the sync transport as a business ordering source.** No feature may reintroduce this (§8.4, A-RULE 024/025).
- **Per-tenant database at launch.** Shared DB is the launch shape. Deployment Stamps are designed for, not built, until a real tenant needs them (§9.1, §72).
- **Arbitrary DBA SQL against financial invariants** (§20), and **exactly-once network delivery**, which is impossible and never claimed (§101).

---

## 24. Pattern catalog & source basis

Sources are grouped by true provenance. **Primary methodologies are not Microsoft artifacts** and must never be cited as such. Patterns are **adapted** to Animora's constraints, not applied as-is.

**Primary methodologies (independent):**
- **C4 model** (Context, Container, Component), Simon Brown, `c4model.com`: §3–§5.
- **arc42**, `arc42.org`: structure behind §1 (drivers) and §21 (decision log).
- **Domain-Driven Design, bounded contexts** (Evans), inside a modular monolith: §6.
- **Clean/Onion Architecture and Ports & Adapters (Hexagonal)**, Martin and Cockburn lineage. Microsoft's .NET architecture guides apply the same shape: §5, §8.1.

**Azure Architecture Center patterns, applied deliberately and cited at point of use:**
- **Transactional Outbox**: §12.1, §39.
- **Idempotent Consumer**: §8.6.1, §12.1.
- **Competing Consumers**: §12.2, §41.
- **Queue-Based Load Leveling, Bulkhead, Throttling** (reliability): §12.3, §12.4, §42, §43, §71. **Circuit Breaker** only at the remote client↔server boundary, not on local in-process outbox effects at launch.
- **Retry**: §12.3, §42.
- **Cache-Aside**: §10.7, §59.
- **Valet Key**: §10.8, §14, §57.
- **Deployment Stamps**: §9.1, §72.
- **Minimize coordination** (design principle): §2.

**Platform and security guidance:** PostgreSQL isolation, row locking, triggers, RLS and WAL recovery (S01–S03, S22–S24, S31–S36, §PG), plus Microsoft EF Core named filters, ASP.NET Core authentication and authorization, antiforgery, Data Protection, and Blazor/SignalR. Vendor guidance is **not** a certification of Animora's custom journal or immutable batch protocol.

### 24.1 Primary-source evidence register (reviewed 2026-10-08)

Database sources are updated for the PostgreSQL revision. §PG.6 lists the PostgreSQL and Npgsql primitive references. S IDs stay stable for existing cross-links. ASP.NET guidance is `aspnetcore-10.0`. .NET and EF 11 features are excluded. Links document **primitives**, not passed implementation or integration tests. Earlier SQL Server details are not PostgreSQL capabilities. No deployed server or data transfer was performed.

| ID | Official source | Evidence applied / local adaptation |
|---|---|---|
| S01 | [PostgreSQL transaction isolation](https://www.postgresql.org/docs/18/transaction-iso.html) | REPEATABLE READ snapshots; sequence increments are nontransactional. The serialized tenant counter plus trigger journal is Animora-owned, not a built-in change-tracking feature. |
| S02 | [PostgreSQL trigger functions](https://www.postgresql.org/docs/18/plpgsql-trigger.html) | BEFORE/AFTER DML, OLD/NEW, rollback-safe logging; no automatic baseline column mask or full field history. |
| S03 | [PostgreSQL routine vacuuming](https://www.postgresql.org/docs/18/routine-vacuuming.html) | MVCC dead tuples, statistics, transaction-ID wraparound; bounded snapshots and journal GC are separate. |
| S04 | [EF Core connection resiliency](https://learn.microsoft.com/en-us/ef/core/miscellaneous/connection-resiliency) | Retry the whole explicit transaction; uncertain commit needs success verification; result buffering has memory cost (§8.6.1, §17-CT). |
| S05 | [EF Core transactions](https://learn.microsoft.com/en-us/ef/core/saving/transactions) | Same connection and transaction for cross-context atomicity; MARS and savepoint incompatibility; no implicit distributed transaction (§8.6.1). |
| S06 | [EF Core concurrency](https://learn.microsoft.com/en-us/ef/core/saving/concurrency) | Token in conditional write; application-specific resolution; original, current and database values; application-managed SQLite tokens (§8.13, §48). |
| S07 | [Resilient HTTP apps with .NET](https://learn.microsoft.com/en-us/dotnet/core/resilience/http-resilience) | One resilience handler; default retry includes unsafe methods; disable or explicitly gate writes; hedging is concurrent (§12.3). |
| S08 | [SignalR .NET client](https://learn.microsoft.com/en-us/aspnet/core/signalr/dotnet-client?view=aspnetcore-10.0) | Automatic reconnect opt-in; default finite attempts; separate initial-start retry; callbacks (§44). |
| S09 | [SignalR configuration](https://learn.microsoft.com/en-us/aspnet/core/signalr/configuration?view=aspnetcore-10.0) | Bounded buffers; timeout and keepalive pairing; optional transient stateful reconnect; .NET 11 auth-refresh APIs excluded (§44). |
| S10 | [SignalR authentication and authorization](https://learn.microsoft.com/en-us/aspnet/core/signalr/authn-and-authz?view=aspnetcore-10.0) | Cached connection principal; role and revocation enforcement; token-expiry closure; browser query-token logging risks (§44). |
| S11 | [Idempotent Consumer](https://learn.microsoft.com/en-us/azure/architecture/patterns/idempotent-consumer) | Stable keys; consumer-qualified inbox; atomic marker and effect; concurrent uniqueness; retention including operator replay; effectively-once, not universal exactly-once (§8.6.1). |
| S12 | [Transactional Outbox guidance](https://learn.microsoft.com/en-us/azure/architecture/databases/guide/transactional-outbox-cosmos) | Same-transaction state and event, later relay. Only the relational principle is adapted; neither Cosmos DB nor Service Bus is introduced (§12.1, §44). |
| S13 | [Retry pattern](https://learn.microsoft.com/en-us/azure/architecture/patterns/retry) | Bounded transient retries; idempotency; avoid nested retry multiplication and permanent-error retries (§12.3). |
| S14 | [Microsoft.Data.Sqlite errors](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/database-errors) | Connection and command not thread-safe; busy or locked retries until timeout; WAL reduces contention (§48). |
| S15 | [SignalR scaling](https://learn.microsoft.com/en-us/aspnet/core/signalr/scale?view=aspnetcore-10.0) and [groups](https://learn.microsoft.com/en-us/aspnet/core/signalr/groups?view=aspnetcore-10.0) | Single process, no backplane; persistent connection cost; affinity exceptions; volatile group membership; explicit revocation (§44, §73). |
| S16 | [EF Core global query filters](https://learn.microsoft.com/en-us/ef/core/querying/filters) | Tenant context; named soft-delete-only disable; required-navigation filtering hazard; not a substitute for explicit journal extraction isolation (§17-CT.3). |
| S17 | [PostgreSQL routine vacuuming](https://www.postgresql.org/docs/18/routine-vacuuming.html) | Long-lived snapshots affect vacuum; monitor dead tuples and oldest transaction; journal GC is separate. Never alter engine catalogs to repair an application feed. |
| S18 | [.NET common web architectures](https://learn.microsoft.com/en-us/dotnet/architecture/modern-web-apps-azure/common-web-application-architectures) | Modular single deployment with inward dependencies and DI; microservices add costs this task does not justify (§0, §5). |
| S19 | [Multitenant storage approaches](https://learn.microsoft.com/en-us/azure/architecture/guide/multitenant/approaches/storage-data) | Shared-schema tenant discriminator is a trade-off, not universally optimal; noisy-neighbor quotas; predictable migration and recovery; no per-tenant tables (§9, §12.4). |
| S20 | [ASP.NET Core hosted services](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services?view=aspnetcore-10.0) | Scope-per-unit workers; bounded channels; non-overlapping periodic work; shutdown callbacks may not run, so correctness lives in durable stores (§12.2, §44, §48). |
| S21 | [EF efficient querying](https://learn.microsoft.com/en-us/ef/core/performance/efficient-querying), [advanced performance](https://learn.microsoft.com/en-us/ef/core/performance/advanced-performance-topics), [multitenancy](https://learn.microsoft.com/en-us/ef/core/miscellaneous/multitenancy) | Projection, indexes, keyset pagination; retry and split-query buffering; context vs. connection pooling and once-only OnConfiguring; scoped tenant-state ownership (§9.4, §16, §17-CT). |
| S22 | [PostgreSQL row security](https://www.postgresql.org/docs/18/ddl-rowsecurity.html) | USING and WITH CHECK; owner and BYPASSRLS behavior; FORCE RLS; referential-integrity and TRUNCATE exceptions; transaction-local tenant settings. |
| S23 | [PostgreSQL explicit locking](https://www.postgresql.org/docs/18/explicit-locking.html) | Row, resource and period locks; consistent order; transaction-owned advisory locks; no session locks in pooled connections. |
| S24 | [PostgreSQL error codes](https://www.postgresql.org/docs/18/errcodes-appendix.html) | 40001 and 40P01 whole-transaction replay; 23505 by constraint; uncertain commits verified by receipt. |
| S25 | [Authentication](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/?view=aspnetcore-10.0), [Identity APIs](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity-api-authorization?view=aspnetcore-10.0), [cookies](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/cookie?view=aspnetcore-10.0), [JWT handler](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/configure-jwt-bearer-authentication?view=aspnetcore-10.0), [policies](https://learn.microsoft.com/en-us/aspnet/core/security/authorization/policies?view=aspnetcore-10.0), [resource authZ](https://learn.microsoft.com/en-us/aspnet/core/security/authorization/resourcebased?view=aspnetcore-10.0), [antiforgery](https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery?view=aspnetcore-10.0) | Explicit handlers and current policies; built-in bearer is not JWT or OIDC; imperative resource checks; browser CSRF. Animora owns session, device and rotation policy (§10.2, §10.3, §17.1.1). |
| S26 | [DP configuration](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/overview?view=aspnetcore-10.0), [default settings](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/default-settings?view=aspnetcore-10.0), [key management](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/implementation/key-management?view=aspnetcore-10.0) | Persistence, ACL and protection are separate; expired vs. revoked vs. deleted keys; recover ring and decryptors (§10.6). |
| S27 | [ASP.NET output caching](https://learn.microsoft.com/en-us/aspnet/core/performance/caching/output?view=aspnetcore-10.0), [Cache-Aside](https://learn.microsoft.com/en-us/azure/architecture/patterns/cache-aside) | After authN, authZ and CORS; default no authenticated or Set-Cookie caching; order alone does not separate principal or resource (§10.7). |
| S28 | [Competing Consumers](https://learn.microsoft.com/en-us/azure/architecture/patterns/competing-consumers), [Queue-Based Load Leveling](https://learn.microsoft.com/en-us/azure/architecture/patterns/queue-based-load-leveling), [Priority Queue](https://learn.microsoft.com/en-us/azure/architecture/patterns/priority-queue) | Independent consumers vs. ordered work; smoothing, not unlimited buffering; Animora adds fenced leases and durable predecessor checks (§12.2.1). |
| S29 | [Rate limiter](https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit?view=aspnetcore-10.0), [Throttling](https://learn.microsoft.com/en-us/azure/architecture/patterns/throttling), [Bulkhead](https://learn.microsoft.com/en-us/azure/architecture/patterns/bulkhead), [Circuit Breaker](https://learn.microsoft.com/en-us/azure/architecture/patterns/circuit-breaker) | Endpoint partitioning; concurrency, not only requests per second; multidimensional quotas; local process isolation is budgeted, not physically guaranteed (§12.3, §12.4.1). |
| S30 | [Host and deploy](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/?view=aspnetcore-10.0), [Kestrel options](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel/options?view=aspnetcore-10.0), [performance best practices](https://learn.microsoft.com/en-us/aspnet/core/performance/performance-best-practices?view=aspnetcore-10.0), [health checks](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks?view=aspnetcore-10.0), [Health Endpoint Monitoring](https://learn.microsoft.com/en-us/azure/architecture/patterns/health-endpoint-monitoring) | Supervision; regular and upgraded limits; async and thread starvation; bounded readiness vs. liveness (§17.1.1). |
| S31 | [PostgreSQL query statistics](https://www.postgresql.org/docs/18/pgstatstatements.html) | Query aggregates plus protected plan capture; real endpoint percentiles need application histograms. |
| S32 | [PostgreSQL memory resources](https://www.postgresql.org/docs/18/runtime-config-resource.html) | Shared buffers; per-operation work_mem; hash multiplier; vacuum memory; no Resource Governor equivalent. |
| S33 | [PostgreSQL continuous archiving / PITR](https://www.postgresql.org/docs/18/continuous-archiving.html) | Physical base plus continuous WAL and timeline coverage; archive_timeout cost; off-host durability; restore gates. |
| S34 | [PostgreSQL backup verification](https://www.postgresql.org/docs/18/app-pgverifybackup.html) | Manifest checks do not replace real restore, pg_amcheck or domain integrity checks. |
| S35 | [Npgsql security](https://www.npgsql.org/doc/security.html) | VerifyFull TLS; volume, backup and field encryption have separate threat boundaries; no assumed community TDE. |
| S36 | [PostgreSQL CREATE TRIGGER](https://www.postgresql.org/docs/18/sql-createtrigger.html) | DML trigger coverage; no automatic DDL or TRUNCATE continuity; readiness validates the trigger and catalog manifest. |

**Community adapter source (not a Microsoft guarantee).** [C01 — Datasync maintainer's offline-client guide](https://communitytoolkit.github.io/Datasync/in-depth/client/): queued operations; `UpdatedAt`-derived delta tokens; custom `IConflictResolver` hooks; built-in entity replacement policies; and a resync example that clears pending operations. These facts justify containment. They do not make custom resolvers impossible. **Never copy the queue-clearing resync recipe onto Animora's durable journal.** Library behavior depends on the pinned release and qualification, not on the living site.

**Application-specific decisions, not Microsoft mandates.** Latency targets, immutable batch format, base/overlay model, fair scheduler and generation cutover are Animora engineering decisions derived from the constraints above. Microsoft does not certify this document. Existing DDD and port boundaries, accounting, security, file and weak-node boundaries are kept. Future server and sync work aligns phase plans with this v3.2 protocol, not with an older universal-Datasync or polling-only assumption.

### 24.2 Corpus triage and release traceability

**Priority belongs to a decision, not to an article's age.** An older maintained reference on snapshot correctness stays P0. A newer cloud sample does not justify new launch infrastructure. PostgreSQL revision: PostgreSQL 18 and Npgsql references; ASP.NET `10.0`. EF pages contain .NET and EF 11 sections, which are excluded. No blanket claim about all 2026 WAF changes. Broken or renamed URLs are not evidence.

| Source track | Disposition | Required architectural output |
|---|---|---|
| 1 — Critical feed primitives | **P0**: S01–S03, S36; PostgreSQL baseline | Transactional tenant counter + trigger journal → REPEATABLE READ watermark, keys, rows → frozen batch. No sequence or xmin cursor. |
| 2 — Locking, transactions, deadlocks | **P0**: S23, S24; never assume snapshot prevents allocation races | SQL transaction and allocation matrix; finite locks and rollback; durable receipt verification (§11.4.1). |
| 3 — EF concurrency, transactions, resiliency, performance, filters | **P0**: S04–S06, S16, S21 | Stable transaction retries; protected base evidence; no extraction retry-buffering; scoped tenant contexts. |
| 3 — EF indexes, generated values, migrations, seeding | **P1 supporting** (indexes, generated values, migrations, management, seeding reviewed) | Tenant-qualified composite constraints; server-generated entity-version token; reviewed expand/contract migrations and seeds; never runtime schema privileges. |
| 4 / 20 — Host, Kestrel, health, zero-Internet | **P0 before server implementation**: S20, S30 | Explicit pipeline, limits, supervision and readiness; packaged assets; route-to-vendor limitation (§17). |
| 5 — Authentication, authZ, CSRF, CORS | **P0 security**: S25 | Schemes and current tenant and device authority; minimal resource load before imperative authorization; CORS is not CSRF. |
| 6 — Data Protection | **P0**: S26 | Persist, protect and back up decryptors; expired, revoked and deleted key outcomes (§10.6). |
| 7 — Hosted and background / Worker Service | **P0 launch**: S20 | SQL durability; scoped bounded worker units; crash-safe leases; a separate Worker Service deployable is not required. |
| 8 / 14 / 26-D — Consumer, retry, breaker, bulkhead, throttle, queues, health | **P0**: S11, S13, S28–S30 | At-least-once with atomic inbox; independent work vs. ordering; fairness and multidimensional budgets (§12). |
| 9 — Transactional Outbox | **P0 concept**: S12. Cloud implementation not copied. | Business, receipt, audit and event as one relational atomic unit; fenced relay. A cloud sample is not production readiness. |
| 10 — CQRS, event sourcing, compensation, materialized views | **P0 CQRS**; **P1 alternatives** | Normal relational write model + immutable ledger, movements and CQRS-lite. "Event-sourcing-lite" is descriptive Animora terminology, not full event-sourced replay or a Microsoft certification. |
| 11 — Cache and files | **P0**: Cache-Aside, output caching (S27), Valet Key. FILESTREAM and blob comparison **P1 deferred**; FileTables **P2** | Tenant-safe bounded internal cache; private filesystem plus SQL metadata; restricted grants. No new blob-storage engine without measured need. |
| 12 / 13 / 21 — Multitenancy, stamps, SaaS | **P0 isolation; P1 scale-out**: S19, S22 | Shared schema with composite tenant keys; measured containment and tenant relocation, not automatic per-clinic hosting. Azure product examples deferred. |
| 15 — Blazor and SignalR | **P0 chosen host correctness**: S08–S10, S15 | Static SSR default; bounded selective circuits; hints never replace catch-up; shared DP ring and backplane only at scale-out. |
| 16 / 26-C — Base backup, WAL, PITR | **P0 before accepting real data**: S33, S34 | Off-host base plus continuous WAL, timeline, file and key manifest; actual restore with fresh feed epoch. |
| 17 — PostgreSQL query, lock, vacuum and integrity evidence | **P1 before GA**: S24, S31, S32, S34 | Real histograms; query aggregates; pg activity; plans; measured maintenance and restore budgets. |
| 18 — Weak-node workload containment | **P0**: S32, §PG.4 | Small combined pools; workload and tenant admission; measured memory and query multiplicity; no Resource Governor assumption. |
| 19 — PostgreSQL encryption and security | **P0**: S35, §PG | VerifyFull, private networking, encrypted volumes, backups and files, separate key custody. Field encryption needs a PoC. |
| 22 — Well-Architected Framework | **P1 quality traceability** | Resilience, security, performance, operations and cost trade-offs map to drivers and gates. No Azure hosting obligation. Release-specific claims stay reference-only until verified. |
| 23 — CommunityToolkit.Datasync | **P1 optional SYNC-SIMPLE only**: C01, server guide, overview | Local DB writes sequential; HTTP may parallelize. No mandatory OfflineDbContext for the custom critical replica. Adapter acceptance depends on the pinned release. |
| 24 — Microsoft Sync Framework | **Excluded / historical**: [1.0 lifecycle](https://learn.microsoft.com/en-us/lifecycle/products/microsoft-sync-framework-10), [2.1 lifecycle](https://learn.microsoft.com/en-us/lifecycle/products/microsoft-sync-framework-21); support ended 2019 / 2021 (component-parent exceptions do not make it a sync platform) | No new dependency or guidance authority. |
| 27 — Custom Animora semantics | **Project-owned; not vendor-supplied** | SettlementUnknown, dependency DAG, capability snapshots, shadow generations and offline financial reconciliation need implementation, model and chaos proofs against official primitives. |

**Release evidence required (not provided by this revision):**

| Artifact / owner | Minimum content and gate mapping |
|---|---|
| Sync implementation / sync team | Entity classification; table and schema registry; trigger manifest and tenant floor probes; extraction and apply budgets; manifest and base-evidence format; failure and recovery cases. Gates F, W, V, AA, AE, AI, AK, AL, AO. |
| Settlement and domain owners | Command fingerprint and receipt schema; terminal vs. deferred outcomes; ordering DAG; real SQL lock and constraint map; period and stock policies. Gates C, D, O, P, S, T, AM, AQ. |
| Server and security team | Scheme, route and pipeline policy; tenant and connection tests with RLS off and on; CSRF, IDOR and cache tests; key, decryptor and session lifecycle. Gates E, J, L, Q, W-csrf, AS, AT. |
| Worker and operations team | Fenced claim, inbox and replay policy; poison and ordered-key remediation; bounded fairness configuration; outage recovery. Gates G, I, AC, AD, AP, AR. |
| Performance and DR operations | Exact PostgreSQL minor, OS, extension and native versions; local percentiles correlated with pg statistics; usable off-host log, file and key manifest; replacement-node restore timings and data loss. Gates X, AG, AU. |

Implementing any affected D or E phase first aligns that phase's instructions with this baseline. Older universal-Datasync plans do not override it. This revision does not rewrite historical phase logs, change package pins, or claim those artifacts exist. `STACK.md` is aligned only where a stale technology decision would contradict the authoritative critical-sync, SignalR or SQL choices.

---

# Part II — Backend & synchronization deep-dive

> Part I (§0–§24) is the self-contained architecture. This part keeps the most
> developed and safety-critical backend detail, with the original section numbers
> (§16-CT, §35–§115) as stable anchors for `STACK.md`, the backend roadmap and
> engineers. Nothing here contradicts Part I. Where a concept already has a Part I
> home, this part cross-links.

## §16-CT. PostgreSQL transactional change journal (critical-state feed)

*The `-CT` labels are kept only for cross-reference. The implementation is not SQL Server Change Tracking. PostgreSQL has no such API. Animora owns a logged-table and trigger protocol whose correctness must be proven on PostgreSQL (D08/E08), not assumed from documentation. No logical-decoding slot, Debezium, broker or per-client WAL consumer is needed.*

### §16-CT.1 Commit-safe tenant sequence (binding)

One `sync.TenantFeedState` row per tenant holds `LastSequence bigint`, `MinValidSequence bigint`, profile and schema identity, and the tenant feed generation.

- Each business writer first locks that row `FOR UPDATE` and holds it to COMMIT.
- Tenant provisioning creates it **before** any replicated row. Missing state = `FeedUnavailable`, never an implicit untracked insert.
- Every replicated-table DML trigger runs `UPDATE ... SET LastSequence=LastSequence+1 ... RETURNING LastSequence` and appends a tenant-qualified record to `sync.ChangeJournal` **in the same transaction**. A mutation gets a sequence. A transaction may get several.
- Rollback cancels counter, journal and business state together. Savepoint rollback also cancels its increments. Only committed state is observable.
- Counter overflow fails closed **before** the write, followed by a governed generation reset. Never wraparound.

The row lock serializes writers **within one tenant through commit**. If T1 holds uncommitted seq 10, T2 in that tenant cannot publish 11 until T1 commits or rolls back. A snapshot sees a committed prefix of the tenant's history. Different tenants write independently; their numbers are not comparable.

**Never use** `BIGSERIAL`, IDENTITY or `nextval()`, `xmin`, transaction IDs, WAL LSN, `MAX(id)`, wall-clock stamps or block reservations as this checkpoint. PostgreSQL sequences are allocated before commit and are not rolled back. A later-numbered writer can commit first and make a naive high-water cursor skip an earlier transaction forever (S01). `xmin` is an internal wrapping identifier, not a durable offline token or cursor.

This is a deliberately conservative launch choice: it trades same-tenant throughput for simple, safe commit ordering on 1–3 cores. Measure gate wait, transaction p95 and admitted mutation volume. A higher-throughput outbox-to-feed sequencer or logical-WAL design needs a separate ADR and full correctness gates. No silent substitution. One command transaction is tenant-local. Bulk maintenance splits by tenant. **Universal lock order: gate → period → resources → business rows.** Triggers are an atomic fallback for governed direct DML. Acquiring the gate late can deadlock, so maintenance **must** take it first and retry an aborted whole unit. No disabled triggers or session replication-role bypass in runtime.

### §16-CT.2 Schema and trigger coverage

`sync.ChangeJournal`: `(TenantId, Sequence)` PK; EntityType (registered stable code); EntityId; Upsert or Delete; opaque Version; schema generation; `RecordedAtUtc` (diagnostic); nullable `FirstSeenCommittedAtUtc` (for GC). It stores **minimal state-change keys**, not clinical payloads or domain events. TenantId and Id are immutable.

- Every replicated aggregate, separately replicated child and replicated finance read projection has INSERT, UPDATE and DELETE capture. An owned child that is not separately replicated must also dirty and log its replicated root.
- Mapping and trigger coverage are **generated and validated from `SyncContractCatalog`**, never from a hand-written count.
- Data Protection, auth secrets, receipts, outbox, and batch, staging, journal and counter tables have **no** capture trigger (no loop).

A BEFORE INSERT/UPDATE trigger generates `Version uuid` via `gen_random_uuid()` and stamps `UpdatedAt` from DB UTC. Supplied values cannot forge them. The Domain `SyncableEntity.Version` stays a string. Map it with an explicit canonical UUID-string ↔ Guid converter to PostgreSQL `uuid`, plus a store-generated `IsConcurrencyToken()` (**not** `IsRowVersion()`). Persistence-only row models may use Guid directly. EF obtains generated tokens via RETURNING. Prove the converter, original-value and CAS round trips. Never duplicate independently tracked version columns. An AFTER trigger increments the tenant counter and appends the key.

Store Version on all mutable server rows, even outside the replicated set. **Soft delete** is an internal Upsert but is serialized as a Delete item. **Governed hard delete** logs the OLD tenant, id and version, and leaves a permanent identity graveyard. Ordinary runtime cannot hard-delete replicated rows or reuse identities. UUID wire codec: canonical lowercase hyphenated string. Tokens are opaque and only equality-tested.

No server data is shipped, but existing local command envelopes **do** carry a numeric expected version. §PG.7 versions that seam. Compatibility and receipt base-token tests are mandatory. Do not claim "no wire change needed."

No native baseline-relative changed-column mask exists. Automatic merge requires stored server mutation evidence tied to BaseVersion (or enough retained base values) and a domain-approved disjointness proof. Otherwise: Conflict or NeedsAttention. Origin metadata is server-derived audit and provenance. It is never an authorization grant, and never a reason to skip canonical echo delivery. TRUNCATE, DDL, trigger replacement, feed reconstruction or restore need a reviewed epoch and schema transition, and invalidate affected ready and building batches.

## §17-CT. Change-feed correctness protocol

### §17-CT.1 One snapshot, one frozen batch

```text
Resolve authenticated tenant/user/device + allowed scope/contract
BEGIN ISOLATION LEVEL REPEATABLE READ   (read-write for staged batch persistence)
  Set transaction-local tenant context if RLS is enabled
  Read epoch, schema, profile and TenantFeedState from THIS snapshot
  Require MinValidSequence <= FromVersion <= LastSequence
  ToVersion = this tenant's committed LastSequence
  Read ChangeJournal WHERE TenantId=@tenant AND FromVersion < Sequence <= ToVersion
  Deduplicate latest entry per (EntityType, EntityId) within the interval
  Authorize keys and tombstones; read complete current rows and dependencies
    from the SAME snapshot (keep tenant filter; disable only soft-delete)
  Materialize bounded, immutable, authorized payload pages + manifest
COMMIT   (only a complete Ready batch becomes visible)
Serve HTTP pages from the persisted batch, NEVER by re-reading changing base tables
```

- Full snapshots read all authorized base rows at the same snapshot watermark, not only retained journal keys.
- Missing trigger, feed state or catalog mismatch → `FeedUnavailable`, **never** an empty success.
- Below the floor → `CursorExpired` and shadow resync. Future position → `InvalidCheckpoint`. Changed epoch, scope or profile → controlled recovery.
- Do not run an extra metadata query before the snapshot to supply the watermark or floor.
- PostgreSQL READ COMMITTED is statement-scoped. It does **not** satisfy this contract.
- Extraction does not lock the tenant writer gate and never waits on HTTP.
- Concurrent cleanup is safe under MVCC for the bounded snapshot. Long snapshots still delay vacuum, so the whole-job budget rejects them.

The reader streams bounded keyset chunks. It exhausts and disposes the reader **before** staging on the same connection. It never commits chunks of one source snapshot separately. No global ORM retry strategy, no ToList buffering. A snapshot or batch failure restarts a complete new extraction from the unchanged completed checkpoint. Admission: one export at a time initially. Default budgets: 50 items per page, cap 200, 256 KiB serialized per page, 10,000 items / 16 MiB / 15 s per whole export. An oversized single item or a full tenant needs a reviewed capacity or export policy. Never partial completeness or live-table paging.

Ready payloads and manifest use logged tables and survive restart. Manifest uniqueness arbitrates duplicate requests. A Building owner lease or TTL permits cleanup. Only Ready serves. Verify an uncertain COMMIT from a fresh primary connection.

### §17-CT.2 Wire identity, paging, durable application

```text
Checkpoint: ServerFeedEpoch · TenantFeedGeneration · ScopeId/ScopeHash · SyncProfileVersion · ContractVersion · CompletedVersion
Batch:      BatchId · same binding · FromVersion · ToVersion · PageCount · ItemCount · ContentHash · ExpiresAt
Page:       BatchId · PageOrdinal · PageHash · Items · integrity-protected continuation
Item:       EntityType · Id · Upsert | Delete | RemoveFromScope · opaque Version · payload
```

- Sequence values travel as **lossless decimal strings**, never JavaScript floats.
- Continuations bind authenticated tenant, user, device, scope, generation, contract, batch, page and expiry. Each page revalidates current authorization. **A token is not an access grant.**
- A repeated page is byte-equivalent. TTL, authority change and reset invalidation are enforced.
- Pages stage idempotently by `(BatchId, PageOrdinal)` in invisible encrypted SQLite storage.
- Verify all counts, hashes, identities, dependencies and contracts. Then **one** local writer transaction updates the server base, safely rebases or preserves the pending overlay and conflicts, records batch completion, and changes the checkpoint together.
- Upserts run parent-before-child. Deletes run in reverse dependency order. Flushes of 200 rows are allowed **inside** that transaction, never as intermediate visible commits.
- A partial download or projection never advances any critical checkpoint. UI invalidation follows commit. Incoming projection emits no new business intent or outbox echo.

Apply requires exact `FromVersion == active.CompletedVersion` and matching identity and generation fields. Do not merge overlapping batches with numeric MAX. An empty complete authorized batch may advance ToVersion over rows excluded for **this** tenant. Other tenants never advance this checkpoint. A repeated completed BatchId is a no-op. An expired or lost incomplete batch discards only its staging, then re-requests from the unchanged checkpoint. An expired floor uses shadow recovery. **A settlement receipt cannot advance a state checkpoint or regress the base.**

### §17-CT.3 Scope exits, deletion, metadata isolation

Every journal query, old-row tombstone, batch lookup and payload join is **explicitly tenant-qualified, even with RLS**. Tenant-consistent `(TenantId, Id)` keys and FKs block foreign parents. Clients get no raw journal endpoint or DB credentials. Hard-purge identity keys stay in the permanent graveyard to reject old resurrection writes. Minimal retained journal tombstones are enough for incremental delivery. No prior clinical payload goes to a user no longer entitled to it.

Default profiles use stable tenant, entity and column permissions. Mutable predicates (doctor, location, status, age windows, joined visibility) need a durable entry **and** `RemoveFromScope` transitions, including timer-driven exits that involve no row DML. Alternatively, bump the profile and rebuild the shadow. Revocation gates online reads immediately on contact. Inaccessible pending intent is quarantined, never discarded or exposed to a new identity. Permissions, table sets and schema fingerprint bind manifests. A feed reset pauses admission, drains building jobs, rotates the generation, invalidates old batches, and reopens readiness only after validation.

### §17-CT.4 Full resync and recovery

Preserve the pending command journal, drafts, local-only preferences and protected conflict entries. Build a separate encrypted **shadow generation** with the full snapshot protocol. Validate relationships. Rebase the overlay under domain policy. Then atomically switch the active-generation pointer and checkpoint. On failure, the old active generation and pending work stay intact. **Never** clear the active replica or pending business operations before a successful replacement. Retired generations are garbage-collected only after a safe switch or rollback window. After restore or data loss, mint an unpredictable fresh `ServerFeedEpoch`. Never infer continuity from a restored counter that later overtakes the old client number.

## §18-CT. Journal retention

`JournalRetention ≥ MaxSupportedOfflineWindow + 15 days` (initial offline policy 30 days ⇒ at least 45 days, subject to measured disk capacity).

GC first marks currently visible committed journal rows with `FirstSeenCommittedAtUtc`, using a maintenance observation timestamp. Unmarked entries are ineligible. Retention counts from that **observation**, not from pre-commit `RecordedAtUtc` or transaction start, so a long-running writer cannot publish already-expired changes. A forward clock step or unexplained observation age stops destructive GC until reviewed.

Per tenant, remove only a bounded **contiguous eligible prefix** up to K, in one transaction that advances `MinValidSequence = K` under the tenant gate. Do not skip newer or unmarked entries. Do not delete holes above the floor. Do not prune by the largest client checkpoint. **Exact validity rule: `FromVersion >= MinValidSequence`.** A GC crash rolls back both the deletes and the floor. Export snapshots see a consistent before or after state. Never hold the gate during VACUUM or off-host I/O.

Batch TTL, journal and tombstones, permanent identity graveyard, offline authority and receipt retention are **distinct** policies. Disk pressure triggers admission control and alerts. It never silently shortens history. Observe journal growth, oldest committed marker, floor, vacuum and WAL lag, and oldest snapshot. Capacity-test the advertised offline window. Application GC manages only its own tables, **never** PostgreSQL system catalogs.

## §19-tomb. Tombstones and delete semantics

Synchronized rows use **logical deletion** wherever clients must learn of a deletion: `Deleted=true · DeletionVersion · DeletedAt · DeletionReason · DeletedBy`. Hard delete is prohibited in ordinary production paths for synchronized entities (§20.3). The journal can report *that* a row was deleted, but it cannot rebuild the prior row for a client that needs a tombstone, hence durable tombstone state. An **offline edit to a server-deleted row** is a distinct **resurrection conflict**, routed to explicit domain policy: reject, restore with audit, create a replacement, or human review. Never auto-applied as if the row still existed (§8.14, Gate V).

## §34. Patient / clinical data modeling

Model independent operations as **additive** records (`ClinicalEntry 1, 2, 3…`), not one large mutable `MedicalHistoryText` blob. This reduces conflicts (§8.14).

## §35. Finance (detail of §11.1)

**§35.1 Ledger.** Posted facts are immutable. `Journal · JournalLine · PostingBatch` obey `Σ Debit = Σ Credit`.
**§35.2 No posted mutation.** Corrections are `Reversal`, `Credit Note` or `Adjustment`. Never `UPDATE posted value`.
**§35.3 Posting requirements.** A posted fact carries or references: `AccountingDate · DocumentDate · Currency · ExchangeRate version · Tax snapshot · Rounding policy · Account/dimensions · Source document · Posting user/device · Posting timestamp · Operation reference`.
**§35.4 Fiscal periods.** `Open → SoftClose → Review → Closed → Authorized Reopen/Correction`. Offline commands dated into a closed period follow one explicit policy: reject, post to current, authorized back-post, hold, or adjustment. Never a silent post into a closed period (§11.1, Gate S).

## §36. Inventory (detail of §11.2)

Movements: `Receipt · Sale · Consumption · Loss · Adjustment · Transfer · Return`. Quantity-on-hand is derived. Two valid offline movements are valid local facts. **The server decides** whether the final outcome is allowed, using the per-product-class policy (negative stock, reservation, backorder, lot/batch, expiry, serial, location, cost layer).
**Forbidden:** `Set QuantityOnHand = 5` from a stale client.
**Allowed:** `StockAdjustment +3` as a command and a movement.

## §37. Scheduling (detail of §11.4)

A row-version check is **not** sufficient for a contended resource (room, doctor slot, boarding capacity, cash drawer, seat, device allocation). Use a server-side transactional allocation: `check allocation + insert or claim + commit`. Implement with a uniqueness constraint, a serializable or appropriate transaction, an allocation table, or a short-lived application lock where justified. **No global lock** (Gate T).

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
| Audit | Same transaction where required for financial or security audit |

**There must be no silent financially incomplete state.**

## §39–§43. Reliability detail

**§39 Transactional Outbox / §40 Outbox state machine / §41 Worker architecture / §42 Retry architecture / §43 Bulkhead/backpressure.** Full detail in Part I §12.1–§12.4. Launch worker host: in-process background service with bounded queues, bounded concurrency and priority classes P0–P3. Every remote retry policy declares max attempts, max elapsed time, backoff, jitter, per-tenant and global budgets. Separate budgets isolate interactive, sync, file, report, maintenance and outbox work. Report overload reduces report throughput but **never stops checkout**.

## §44. Near-real-time collaboration (hints)

**[DECISION — S08–S10/S15.] Self-hosted ASP.NET Core SignalR is the launch hint adapter**, not deferred to scale-out. It runs in the existing server process. One persistent hint connection per authenticated desktop profile. Active web islands may subscribe and refetch. Static SSR is **not** silently turned into a persistent interactive circuit. No Azure SignalR Service, Redis or broker on the launch node. The journal pull is authoritative.

Every ordinary API mutation of replicated state (including a state patch with no domain event) writes a separate `StateInvalidated` hint intent **in the same transaction** as the state and receipt. This integration hint is not a domain event or a journal replacement. A bounded hosted relay uses `IHubContext`, never keeps a Hub, DbContext or HttpContext alive, coalesces by authorized tenant and scope, and sends only `scope dirty / profile invalidated` with an optional opaque generation. It cannot know the committing transaction's journal version before commit. A sampled version is only a wake hint and never a checkpoint. Governed direct SQL with no outbox hint is found by polling. Maintenance may enqueue a hint transactionally for faster visibility (§20).

The committed write returns **without** waiting for a connected recipient or a slow socket. Relay success means *attempted dispatch*, **not** that B applied the data. Hint queues, coalescing maps, per-client buffers and relay concurrency are bounded. On overflow, many hints may collapse into one scope-dirty signal. Persistent outbox effects are **never** dropped with hint traffic. A poisoned hint may dead-letter and slow latency. Polling still repairs state.

**SignalR is routing, not authorization.** Hub admission, tenant-qualified user IDs and group names come from current authenticated membership. Clients cannot pick arbitrary clinic groups. Groups live in memory and are not a security boundary. Reconnect reauthorizes and rejoins. Subscribe-then-pull closes the gap (§8.17). Close affected connections on membership or device revocation. .NET 10 does not refresh a cached hub principal after role changes. Use current-policy checks and token-expiry closure with reconnect. Browser query-string bearer tokens, if used, stay confined to the hub endpoint and are redacted from proxy and application logs. Identity desktop tokens are validated by their matching handler. Do not assume they are JWTs.

**Reconnect control.** `WithAutomaticReconnect` is explicit. The default policy stops after four attempts and does not retry the initial `StartAsync` failure (S08). Own startup and later recovery in the single coordinator, with jittered bounded episodes, one token-refresh gate and no overlapping starts. Stateful reconnect is optional and bounded. Its transient memory replay is **not** an offline durable queue, cannot survive every restart, and never replaces journal catch-up. Sections marked **.NET 11** (`EnableAuthenticationRefresh`, `RefreshAuthenticationAsync`) are not launch .NET 10 APIs (S09/S10).

```text
Hint received / local save     → coalesced wake; initial debounce 50–150 ms
Healthy hub, foreground        → anti-entropy poll every 5–15 s + jitter
Hub unavailable, foreground    → delta poll every 1–3 s + jitter (degraded mode)
Idle                           → 15–30 s + jitter
Background                     → 30–120 s + jitter, subject to OS suspension
App resume / reconnect         → immediate bounded catch-up regardless of hints
```

These are initial tuning values, not benchmark results or hard time guarantees. Fallback polling's worst-case visibility is poll interval plus network and apply time, and need not meet the healthy-hub target. Journal dirty-poll and snapshot extraction are rate-limited per tenant and globally. Hints that arrive during a pull set a dirty generation. After the round, one follow-up is scheduled, so coalescing cannot lose the last wake or cause recursive storms. Backpressure may raise intervals visibly. Settlement and foreground catch-up keep a fair budget above file, report and background work.

Reverse proxies support negotiation and WebSocket upgrade, disable buffering and cache where relevant, and set idle timeouts above keepalives. Server and client timeouts are at least **twice** their paired keepalive interval (S09). A single-process launch needs no backplane or affinity. Scale-out revisits persistent-socket memory, connection admission, session affinity and a self-hosted backplane (§73). The WebSocket-only or SkipNegotiation exception is not assumed while fallback transports are supported.

## §45. Notification failure

If a notification is lost, **no data is lost**. The client eventually polls and synchronizes from its checkpoint. A notification channel can be added, removed or replaced without changing state correctness (Gate AB).

## §46. Web architecture (detail of §7.3)

Blazor Web App: **Static SSR is the default; InteractiveServer is selective.** Static SSR keeps no persistent circuit but still does server and DB work per request. InteractiveServer creates a server-side circuit with per-session memory and connection cost. Rules: never make the whole app InteractiveServer by default. Large read-heavy screens prefer static, paged rendering. Typeahead and search endpoints are dedicated and debounced. Interactive islands stay small. **Server-side authorization is always authoritative.**

## §47. Desktop architecture (detail of §7.4)

`Avalonia → MVVM → Shared Application Core → Local Repository → SQLite/EF Core → Sync Coordinator`. **No browser or WebView in the offline path.**

## §48. Local database concurrency

One local writer and admission gate per encrypted profile serializes **all** local write transactions: UI save plus journal, delta apply, mutation settlement, projection updates, backup coordination and generation cutover. SQLite has one writer. Guarding only the sync writer leaves UI-vs-sync races open. Keep critical sections short. **Never hold the gate or a transaction across HTTP, SignalR, user input or file downloads.** Read queries use independent short-lived contexts. EF and SQLite connection and command objects are **never** shared concurrently. Use a finite busy/locked timeout (S14), bounded backpressure, and visible disk-full and corruption failures. Provider lock retries and higher-level budgets count together. Never infinite `CommandTimeout=0`. SQLite uses application-managed local revision tokens. The server `entity-version token` is stored opaquely as the base token. It is never made by SQLite or confused with a local revision (S06).

## §49. Local WAL and encryption

SQLite uses a WAL-compatible configuration where the encryption provider has proven it. The encrypted DB must pass: create, close, reopen, wrong-key failure, plain-reader failure, migration, WAL, crash recovery, backup, restore and key rotation (Gate K). The native SQLite binary version is **verified at runtime**, not inferred from package metadata.

## §50. Full resynchronization (shadow replica)

A full resync happens when: the cursor expired, the replica is corrupt, the contract is incompatible, scope changed materially, a manual repair is requested, or integrity validation failed.

```text
1.  Enter RecoveringReplica; suspend normal base apply, NOT UI saves
2.  Preserve the independent durable journal, operation outcomes and local drafts
3.  Under one server REPEATABLE READ: capture epoch, scope, watermark; extract all authorized
    initial rows and required references into one immutable batch
4.  Stage pages into an invisible shadow base generation (§17-CT batch protocol)
5.  Validate schema, authorized references, manifest and local storage budget
6.  Catch the shadow up with complete delta batches from its snapshot watermark
7.  Rebase preserved unsent intent; conflicts keep the original payload and evidence
8.  Under the short local writer gate: read the latest journal tail and reconcile
9.  Atomically switch active base generation + overlay/conflicts + checkpoint
10. Commit, invalidate UI, resume normal fair sync; retire old base after recovery
```

**[INVARIANT]** The current local replica stays intact until the shadow is proven valid (Gate AE). A resync never deletes a pending mutation just because the snapshot is newer.

**Launch cutover design.** Keep the journal, drafts, settlement evidence and active-generation pointer in the same encrypted local store. Replicated base tables are generation-keyed. Shadow generations contain base data only. New UI saves keep going into the shared journal while the shadow downloads. The final writer transaction briefly freezes writers, includes every committed journal record through its latest local sequence, rebuilds or preserves the pending overlay, and switches active generation and checkpoint atomically. Future saves compose against the new generation. **No business rule runs a second time** as a side effect of replaying the overlay. Submitted or unknown envelopes keep their exact identity and bytes. Only unsent intent may rebase automatically.

A pointer switch between arbitrary DB files is not transactional with the journal. A future multi-file design needs a recoverable manifest and fencing protocol before replacing this launch design. An interrupted download, expired manifest, revoked scope, corrupt page or insufficient disk leaves the current base and journal intact, with an explicit recovery status. Storage budget covers live base + shadow + journal + WAL. Never delete pending intent to make room. Old-generation cleanup is resumable after successful cutover, and never removes active data or the only recoverable journal. If the snapshot watermark ages past journal retention before shadow catch-up, restart **only** the shadow extraction. Never reset or discard pending work.

## §51. Mutation rebase after full resync

Preserved mutation `BaseVersion = V10`, server snapshot `V20`: try to rebase the V10 patch onto V20. Safe → `PendingSettlement`. Not safe → `Conflict / NeedsAttention`. A resync never deletes a pending mutation merely because the snapshot is newer (A-RULE 023).

## §52. Echo safety (detail of §8.15)

A client tolerates receiving its own committed state again. The second application must not duplicate a business operation, reset dirty flags wrongly, duplicate audit, or recreate a pending mutation (Gate AA).

## §53. Sync scope

A client's sync scope is determined by `Tenant · Device · User/session profile · Role/capabilities · SyncProfileVersion · ContractVersion`. It may include entities, columns, row predicates, history depth, file policy and related references. **Only data the device needs is replicated.**

## §54. Permission and profile changes

If a user's access scope grows or shrinks materially, `SyncProfileVersion` changes. The client may need a controlled resync, so an old cursor is never read as covering a new dataset (A-RULE 049).

## §55. Shared workstation

If several people use one workstation, each user gets a logically separate encrypted profile and replica, unless a specifically designed shared-device model is approved. **No user inherits another's offline authorization by sharing a machine** (A-RULE 050).

## §56–§57. Files (detail of §14)

**§56 lifecycle:** `Staged → Uploaded → HashVerified → Committed → Available`.
**§56.1 transfer:** `BeginUpload · UploadChunk · Resume · Complete · VerifyHash · Commit`. Idempotent, resumable, hash-verified, cancelable, auditable.
**§56.2 identity:** prefer immutable content-addressed objects. Replacing a file creates a new object and a new metadata version.
**§57 access:** Valet Key. `FileId → authorization → short-lived signed grant → client accesses bytes`. A filesystem path is never a credential.

## §58. Reporting (detail of §11.3/§11.5)

Read-only. Launch = OLTP plus incremental read projections (`DailySalesSummary`, `InventorySummary`, `AccountBalanceSummary`, `ClinicKpiSummary`). Heavy reports run under separate budgets. Projections may be eventually consistent and **must expose freshness** (timestamp or version, §83).

## §59. Caching (detail of §10.7)

Cache only where measurement justifies it. Every tenant-sensitive key includes `Tenant · User/Permission scope · Resource · Query/Contract version · Data version`. Never cache authorization-sensitive results before authentication and authorization.

## §60–§64. Security detail

**§60 Threats (STRIDE):** primary mitigations in Part I §10.9.
**§61 Audit** has three layers: *application audit* (security and business actions), *database audit* (break-glass, maintenance, direct SQL), *financial audit* (immutable references tied to accounting). Append-only. Security and financial batches may be hash-chained (§104). Audit never coordinates business processing.
**§62 Keys.** The runtime node holds: server TLS cert and key, Data Protection ring, verification public keys, device public verification material, runtime backup-encryption artifacts. It **must not** hold the offline root CA or license-signing private key. Runtime capability and grant signing keys are separate and least-purpose (§10.6).
**§63 Local PKI:** offline Root CA → Issuing CA → server certificate (§10.5).
**§64 Data Protection:** recovery-critical key ring with stable `ApplicationName`, persisted location, protection at rest and ACL, surviving restart and restore (§10.6).

## §65. Entitlement and licensing (detail of §13)

Subscription is commercial state. Licensing is offline runtime entitlement. They stay separate. License artifacts are signed by an offline protected authority. The runtime holds only verification material. Anti-cloning relies on a device-bound protected identity where supported. A software-only fingerprint is not strong proof.

## §66. Public website / management plane (detail of §13.4, §13.4a)

`Website (register/choose/pay/download) → signed onboarding intent → Subscription → tenant provisioning → license issuance`. The website never takes part in clinic runtime. The production clinic runtime stays fully functional with the website unavailable.

## §67. Zero-Internet runtime (detail of §17.4)

Test each of: DNS, Internet, external time sync, OCSP/CRL, CDN, package feeds, telemetry backend, public website. Confirm Login, Authorization, core reads, Business commands, Critical sync, File access, Local observability and Licensing verification (per offline policy) still work.

## §68–§71. Deployment and capacity (detail of §16, §17)

**§68 Topology:** one vendor node (ASP.NET Core app, PostgreSQL, file store, local certificates and runtime keys, lightweight local observability). Clinics connect. No clinic runs a server.
**§69 Hardware baseline:** 2–4 GiB RAM, 1–3 cores, SSD, co-located app and DB. A capacity target, **not** a proven user count.
**§70 Engine:** PostgreSQL 18, maintained minor; Npgsql and EF Core 10. No commercial edition or Resource Governor dependency.
**§71 Noisy-neighbor isolation:** combined pool, admission, memory, worker, sync, report and file quotas, with per-tenant fairness. See §PG.4 and Gate AC.

## §72–§74. Scale-out detail (detail of §16.2)

**§72 Tenant stamps:** `Tenant → DeploymentStamp → App + DB + FileStore`. Initially all tenants → Stamp 1. Later, a noisy tenant → Stamp 2. Domain and application contracts do not change.
**§73 Multi-instance web:** needs session affinity, shared Data Protection keys and a SignalR backplane. A single node needs no Redis.
**§74 No broker at launch:** database + outbox + background service, not Kafka, RabbitMQ or Service Bus. Introduce a broker only on demonstrated need and an ADR.

## §75–§78. Disaster recovery and client recovery (detail of §17.3)

**§75 Server state:** a recovery point ties DB, file store, runtime keys, application build and schema version through the Recovery Point Manifest. **§75.2:** ≥2 copies, ≥1 off-host, ≥1 immutable; restore tests are mandatory. A never-restored backup is not evidence.
**§76 Client recovery:** server recovery restores server truth, **not** unsent client-only mutations. The desktop protects pending mutations through encrypted local recovery or export, or warns that they may be unrecoverable after total device loss.
**§77 Client disk corruption:** quarantine → preserve journal → re-authenticate → shadow resync → rebase → restore conflict inbox → atomic switch.
**§78 Restore vs. checkpoint:** a restored server may have a lower journal version. Clients validate `RecoveryPointId`, DB identity, schema and feed validity, and may be forced into a controlled full resync.

## §79–§80. Versioning and migration (detail of §18)

**§79:** expand/contract schema discipline; `ContractVersion` and capability negotiation; supported fleet N / N-1 / N-2.
**§80:** a migration succeeds only if server DB, existing clients, offline replicas and pending mutations all stay compatible across the window.

## §81. Error-handling contract (detail of §8.6.4)

No generic "Sync failed" for critical commands. Every failed or conflicted operation states: what failed, why, the financial or business effect, what the user should do, and who must fix it.

## §82–§83. Observability detail (detail of §15)

**§82:** every cross-boundary operation carries `RequestId · CorrelationId · TenantId · UserId · DeviceId · OperationId · MutationId · AggregateId`. Logs are structured, local, PII-minimized. Minimum metrics and separate health probes: §15. The diagnostics bundle exports sanitized state with no secrets.
**§83:** every management report exposes `ProjectionGeneratedAt` and `SourceVersion/SourceWatermark`.

## §84–§90. Performance detail (detail of §16)

**§84** server optimization order and push-safe-work-to-client.
**§85** EF Core rules: `AsNoTracking`, projection, pagination, short-lived scoped context (pooling only after tenant-reset proof), compiled or batched queries where measured. Avoid N+1, `SELECT *`, unbounded queries, long transactions.
**§86** sync endpoints return only changed identities and rows, paged by bounded payload (target 256 KB–1 MB with a row ceiling), with cancel and resume. No entity-graph materialization, no per-row HTTP.
**§87** adaptive catch-up priority: settlement → active-screen state → routine state → background and reporting.
**§88** reconnect-storm control: jitter, adaptive backoff, bounded retry, overload signals, per-tenant and global limits.
**§89** PostgreSQL `pg_stat_statements` and protected sampled plans. Local histograms for real p95 and p99. Protected deadlock logs. `pg_stat_activity` and `pg_locks` for vacuum, MVCC, WAL and journal pressure (§16.4).
**§90** critical sync uses snapshot isolation. Snapshot transactions must be short, because long transactions delay change-tracking cleanup. Full initial syncs are throttled and measured.

## §91. Data retention

Each domain defines operational, audit, financial, sync-tombstone, file and diagnostic retention. Retention must never let a supported offline client miss a required deletion or revocation (§1.1, §18-CT).

## §92–§93. Local data and path security (detail of §10.8)

**§92** desktop local storage is encrypted with an OS-protected key, filesystem ACL, locked credentials, device identity and short sensitive-operation windows. It **fails closed** when the key is unavailable or invalid.
**§93** filesystem paths never cross the client/server trust boundary as credentials. A file reference is a logical `FileId`.

## §94. Domain event vs. replication event (detail of §8.8)

`InvoicePosted` is a domain event. The same transaction also changes rows the journal later exposes. The client may receive "invoice projection changed" through state sync. It does not receive the internal domain event unless a separate contract requires it.

## §95–§96. Command and query shape

**§95** business commands are small and semantic: `RegisterPayment` not `UpdatePayment`; `AdjustStock` not `SetQuantity`; `BookAppointment` not `UpdateAppointmentSlot`.
**§96** queries are read-only, use dedicated query DTOs for high-value screens (not aggregate serialization), and are authorized before any data access.

## §97–§99. API surface and security (detail of §4.1)

**§97** route separation `/business/* · /sync/* · /tables/* · /notifications/* · /operator/*`. Route names may differ; semantic separation may not.
**§98** OData is a Datasync implementation detail only. No business client depends on arbitrary OData semantics.
**§99** every request flows: `TLS → Authentication → tenant resolution → authorization → entitlement → request validation → business action`. No endpoint infers tenant from URL, body, query or local client state alone.

## §100–§104. Validation, idempotency, ordering, allocation, audit integrity

**§100** three validation levels: client UX, application completeness, domain invariants. Server domain validation is authoritative.
**§101** idempotency applies to critical state mutations, business commands, onboarding, file completion, outbox consumers and repeatable maintenance. It does **not** mean exactly-once. Delivery stays at-least-once.
**§102** ordering is explicit through keys (`Tenant+AggregateId`, `Tenant+ResourceId`, `Tenant+CashSessionId`). Never globally serialize a whole tenant when independent aggregates can run in parallel.
**§103** `entity-version token` detects stale edits. It does not prove two clients cannot claim the same slot. A contended resource needs a transactional allocation constraint (§37).
**§104** high-value audit uses `AuditBatch · PreviousBatchHash · CurrentBatchHash · Signature/KeyId`. This is an integrity control, not an execution mechanism.

## §105–§107. Operations and failure semantics

**§105** maintenance tasks (journal cleanup monitoring, outbox retry and dead-letter monitoring, file reconciliation, DB integrity checks, backup verification, projection rebuild, orphan detection, sync diagnostics, license refresh) each carry: scope, tenant context, authorization, rate limit, audit, cancellation and resume semantics.
**§106** a dead letter is **not** a trash can. It holds event or operation identity, tenant, aggregate, attempt count, first and last failure, error code, sanitized detail, payload fingerprint and operator remediation state. Reprocessing needs explicit operator action when appropriate.
**§107** every subsystem has a defined outcome (§12.5).

## §108. Chaos and fault injection

Runtime acceptance must exercise (not executed in this revision): process crash, DB deadlock, transaction rollback, network drop, timeout, lost response, duplicate response, duplicate request, out-of-order request, corrupt local DB, corrupt file, permission change mid-flight, server restart, full node restore, clock anomaly, notification loss.

## §110. Capacity test method (detail of §16.3)

Published capacity comes from measurement. Test at least 25 / 50 / 100 / 250 tenants with realistic user distribution. Measure p50, p95, p99 for API and settlement latency; DB and server CPU, memory, disk I/O; transaction-log throughput; journal backlog; sync MB/s and rows/s; reconnect catch-up time; outbox backlog; deadlocks; locks and waits. The workload includes reads, writes, sync, reports, files, reconnect and background maintenance. Not API micro-benchmarks alone.

## §111–§115. Testing and readiness

**§111 Security testing:** the list in §10.9.
**§112 Supply chain:** every production build records: application build, .NET runtime, EF Core line, Datasync version, native SQLite binary version, RID, OS-native dependencies, schema version, sync contract version and license verification-key version. The release manifest includes native artifacts.
**§113 Upgrade:** server: test → backup → apply → health check → rollback plan. Desktop: signed, verifiable, rollback-capable, offline-installable (macOS signed, notarized and stapled). No runtime dependence on Internet update feeds.
**§114 Deployment plan:** build → static and security analysis → unit → integration → sync and chaos → migration rehearsal → backup and restore rehearsal → mixed-client compatibility → capacity benchmark → release approval → production.
**§115 GA readiness:** all critical gates pass; no unresolved critical security finding; no silent-data-loss defect; no unresolved sync-cursor issue; backup-restore, native encryption, tenant isolation and mixed-version contract proven; documented RPO and RTO met; capacity budget met; offline-window policy proven.

---

## §PG. PostgreSQL migration implementation contract (2026-10-08)

### §PG.1 Scope, versions, readiness honesty

The repository has real local SQLite, Domain and Application layers, but **no** server implementation, installed server schema or deployed sync adapter. This is a **pre-implementation architecture migration**, not a live SQL Server data copy. D01 is next. No D or E phase is marked implemented by these edits. Existing local migration history and desktop behavior are unchanged.

Target **PostgreSQL 18** on a security-maintained minor. Deployment records the exact minor or image digest, OS, checksums, locale and extensions. Pin `Npgsql.EntityFrameworkCore.PostgreSQL=10.0.3` and `Npgsql=10.0.3` when D01 adds consumers. Their published net10.0 nuspec requires EF Core `[10.0.4,11.0.0)` and Npgsql 10.0.3 (NuGet flat container, verified 2026-10-08). That is compatible by declared range with existing EF Core 10.0.12. Restore, build and integration **must still prove** resolution. No unconsumed production package is added just to claim migration. Keep .NET and EF 10 pins. No EF 11, no new broker and no Redis. Npgsql is a third-party provider, not Microsoft first-party. Optional Datasync is deferred from launch. It is not the critical protocol and not a required server or client package.

### §PG.2 Full server data mapping and migrations

| Concept | PostgreSQL mapping / invariant |
|---|---|
| ULID, tenant, FK | `varchar(26) COLLATE "C"`, canonical Crockford ASCII enforced by CHECK. Composite `(TenantId, Id)` PK or equivalent tenant-qualified unique key and FKs. |
| Money | Signed `bigint` minor units + `varchar(3)` uppercase currency. Never floating-point or native money. Validate overflow in domain and DB constraints. |
| Fixed-scale quantity | `bigint` scaled ×1000 + unit identifier, matching the current domain scale; checked bounds. |
| Time | `timestamptz(6)` UTC; Npgsql DateTime UTC or DateTimeOffset Offset=0; date-only uses `date`. Precision is microseconds, not .NET 100-ns ticks. |
| Enums, deletion | `integer` with explicit conversion; `boolean`; bounds or CHECK where stable. |
| Mutable concurrency | DB-generated `uuid Version`, RETURNING, EF store-generated concurrency token, canonical UUID wire string. Neither xmin nor a sequence. |
| Text | UTF-8 `text`, or bounded `varchar(n)` from FieldLengths. Explicit normalized Persian search keys. Do not assume SQL Server CI collation or ILIKE gives Persian normalization. |
| JSON | `jsonb` for validated, queryable structure. Exact canonical command-fingerprint and signature bytes stored separately as `text` or `bytea`, never recomputed from jsonb (which reorders keys). |
| Files, crypto, hashes | `bytea` metadata where needed. File bytes stay in the governed file store, never in oversized JSON rows. |
| FK, unique | Tenant-qualified constraints; restrictive deletes for finance and audit. PostgreSQL does not auto-index referencing FKs: index the real lookup and delete paths. |

Map every live domain aggregate and child explicitly, not by a stale count. One PostgreSQL store owns: Identity, security stamp and refresh grants; device and capability metadata; clinical, scheduling and boarding; catalog, stock and purchasing; invoice, payment, cash, ledger, period, cheque and recurring expense; command receipts and audit; outbox and consumed-message ledger; subscriptions, seats and events; licensing issuance; file transfer and object grants; read projections; sync manifest and checkpoints.
Data Protection uses its own PostgreSQL DbContext and migration history with protected key encryption. A separate context does **not** imply a separate database or backup.

The server assembly is `Animora.Persistence.PostgreSql`, using `UseNpgsql` through a small shared `NpgsqlDataSource` set and scoped contexts. No client project imports Npgsql or server persistence. Server and SQLite configuration and migrations are separate. **Never run SQL Server-generated DDL on PostgreSQL.** The runtime role has no DDL. A reviewed migration bundle runs under deployment credentials before traffic. Development test migration is explicit. Production startup checks the schema version and **never auto-migrates**. Expand/contract, schema fingerprint and mixed-fleet tests stay mandatory.

Tenant-first B-tree indexes serve actual predicates, ordering and composite FKs. `ChangeJournal(TenantId, Sequence)` is the critical delta range index. No `UpdatedAt` index is needed merely as a critical cursor. Normal views rely on base indexes. PostgreSQL has no indexed-view syntax. Large read projections are **incrementally maintained tables**, not scheduled full materialized-view refreshes on checkout. Do not add blanket INCLUDE, GIN, compression or all local indexes without measured read and write cost. PostgreSQL TOAST is not SQL Server PAGE compression. Names are explicit, quoted consistently, kept within the 63-byte identifier limit, deterministically unique, with **no silent truncation collisions**. Migrations validate triggers, privileges, keys, views and catalog coverage in the same release.

### §PG.3 Owner-phase obligations

| Phase | PostgreSQL responsibility / release gate |
|---|---|
| D01 | UseNpgsql and provider pins; validated pooled connection options; private connectivity and health; Data Protection context; deployment-only migrations. Boots live but not ready without DB. Production auth fails closed. |
| D02 | Full mappings; composite tenant keys and FKs; DB UUID version triggers; tenant feed-state provisioning; named filters; trigger coverage seam. No SQL Server compression or types. |
| D03–D04 | Identity, refresh and session persistence in PostgreSQL. Normalized unique login semantics. Tenant membership authorization. Key-ring grants. Raw-SQL tenant guards. |
| D05 | `INSERT ... ON CONFLICT DO NOTHING` receipt claim, then a fresh READ COMMITTED lookup; stable fingerprint equality. Terminal receipt, effects, audit and outbox commit atomically. Lease completion predicates include ClaimToken. |
| D06–D07 | Gate-first command transactions. Stale-version domain conflicts. Resource and period locks. Conditional stock updates and immutable ledger. SQLSTATE classification. Finance projections capture their own changes. |
| D08 | Custom critical journal, snapshot and immutable batch endpoints; trigger and graveyard manifest; exact cursor boundaries and retention. Optional Datasync is **not** installed as launch authority. |
| D09 | Logged transactional Outbox and `FOR UPDATE SKIP LOCKED` CTE claim; RETURNING and fencing; bounded GC and vacuum monitoring. No polling journal as a domain event bus. |
| D10 | Subscription counter row locks (no MAX+1 allocation); seat allocation under tenant or subscription gate; append-only events with unique operation identity. |
| D11–D12 | Licensing key and issuance, file, object and upload-grant metadata mappings; UTC and CAS on PostgreSQL; signature bytes preserved exactly; file recovery manifest unchanged. |
| D13 / E01–E02 | Combined pool budgets; pg statistics; cancellation and overload. Operator reads and reporting projections cannot bypass tenant authorization or starve OLTP. |
| E03–E07 | HTTP Custom adapter into encrypted SQLite staging, base and overlay; exact batch CAS; shadow resync; no partial visible commits or destructive clear; receipt and state checkpoints kept distinct. |
| E08 | **Mandatory** real-PostgreSQL DB CI (absence fails; never a silent skip of a release gate): DML, rollback, reverse commit, tenant metadata, child capture, snapshot cleanup, page, crash, retry, scope and restore tests; measured on 2 and 4 GiB hosts. |
| E09 | Reviewed PostgreSQL migrations and roles; private port 5432; base and WAL restore drills; feed epoch reset; rollback and cutover manifest; separate runtime secrets. |

### §PG.4 Low-resource starting profiles (not capacity promises)

| Setting | 2 GiB / 1 core, co-located | 4 GiB / 2–3 cores, co-located |
|---|---|---|
| shared_buffers | 128 MiB | 256 MiB |
| work_mem / hash_mem_multiplier | 2 MiB / 1.0 | 4 MiB / 1.0 |
| maintenance_work_mem | 32 MiB | 64 MiB |
| autovacuum_work_mem / workers | 16 MiB / 2 | 32 MiB / 2 |
| max_connections | 24 (reserve operational slots) | 32 (reserve operational slots) |
| Combined Npgsql pool max | 12 = interactive 8 + background 2 + export 2 | 20 = interactive 14 + background 3 + export 3 |
| Active admission | interactive 4; export 1; worker 1; report 1 shared with background | interactive 8; export 1; worker 1; report 1 shared with background |
| max_parallel_workers_per_gather | 0 | 0 initially; raise only with load proof |

Minimum pool size 0. No per-tenant pools. No automatic pool growth from varying connection strings. Data Protection shares the interactive pool budget. The read and write reporting seam uses the primary at first. Correctness and receipt verification **never** go to a lagging replica. Keep Npgsql reset-on-close on, parameter logging off, and sensitive error detail off.

Timeouts: connection 5 s; ordinary command 15 s; lock 2 s; idle-in-transaction 15 s; whole application transaction budget 15 s initially. Scoped overrides only for admitted migration and backup jobs. Use a finite PostgreSQL `transaction_timeout` where supported, and validate how job and client deadlines interact. Timeouts are retryable outcomes, not discarded work. Avoid global `EnableRetryOnFailure` buffering. Unit-specific retry policy lives in §11.4.1.

OS and file cache, the ASP.NET heap (no SQLite on server), key and crypto, vacuum workers and peak query multiplication all need measured headroom. Workstation GC is an initial option, not a forced optimum. Keep logs and metrics lightweight. **Do not co-host a full Prometheus, Grafana or trace stack on 2 GiB.** Queue sizes, HTTP bodies, batch bytes, file-transfer concurrency and circuit counts have hard limits. No user count is proven yet. Benchmark realistic clinical, checkout, sync, backup and reconnect load, measuring RSS, latency, lock contention, disk and WAL growth, and recovery. Reduce admission or upgrade hardware if the smallest profile misses SLOs. PostgreSQL alone is not a guaranteed resource reduction for every workload.

### §PG.5 Existing external data and cutover (only when a real source exists)

The sample `.bak` and `scripts/extract-clinic-bak.py` are SQL Server **source-import** artifacts, not the central server. Keep the source parser unchanged. If a separate live SQL Server exists: first inventory and version its schema, rows, constraints, encoding and collation, timestamps, identity keys, money, JSON, receipts, keys and file references. Take and test a source backup. Stage a snapshot export into a new PostgreSQL database and transform it with explicit mappings. Preserve tenant, ULID, OperationId, fingerprint and signature bytes, **not** source concurrency tokens. Reissue versions and a fresh feed epoch.

Validate per-tenant and per-table counts, normalized checksums, FK and unique constraints, money sums, balanced journals, stock derivations, subscription and seat counts, receipt payloads and file hashes. Rehearse full transfer and rollback on copies before a scheduled cutover. Freeze source writes. Export the final consistent snapshot or delta under a reviewed policy. Run all comparisons. Close old workers and routes. Switch secrets and config. Force a controlled client shadow resync that preserves pending intent. **No unsafely dual-active writers.** Keep the read-only source and backup until acceptance. After PostgreSQL accepts new writes, rollback requires reverse-data reconciliation under a new freeze, **not** just a connection-string switch.

A deployed source or user dataset was **not** accessed or moved in this revision. Transfer-tool details and timings need that real dataset before production authorization.

### §PG.6 Primary-source register and verification obligations

- PostgreSQL [isolation and nontransactional sequences](https://www.postgresql.org/docs/18/transaction-iso.html), [row and advisory locks](https://www.postgresql.org/docs/18/explicit-locking.html), [SKIP LOCKED / SELECT](https://www.postgresql.org/docs/18/sql-select.html).
- PostgreSQL [triggers](https://www.postgresql.org/docs/18/plpgsql-trigger.html), [row security](https://www.postgresql.org/docs/18/ddl-rowsecurity.html), [CREATE POLICY](https://www.postgresql.org/docs/18/sql-createpolicy.html), [set_config](https://www.postgresql.org/docs/18/functions-admin.html).
- PostgreSQL [memory](https://www.postgresql.org/docs/18/runtime-config-resource.html), [vacuum](https://www.postgresql.org/docs/18/routine-vacuuming.html), [PITR](https://www.postgresql.org/docs/18/continuous-archiving.html), [pg_verifybackup](https://www.postgresql.org/docs/18/app-pgverifybackup.html), [pg_amcheck](https://www.postgresql.org/docs/18/app-pgamcheck.html).
- Npgsql [concurrency and xmin](https://www.npgsql.org/efcore/modeling/concurrency.html), [UTC and microsecond mapping](https://www.npgsql.org/doc/types/datetime.html), [pool, reset and timeouts](https://www.npgsql.org/doc/connection-string-parameters.html), [provider](https://www.npgsql.org/efcore/), [security](https://www.npgsql.org/doc/security.html).
- NuGet [provider versions](https://api.nuget.org/v3-flatcontainer/npgsql.entityframeworkcore.postgresql/index.json) and [driver versions](https://api.nuget.org/v3-flatcontainer/npgsql/index.json). 10.0.3 nuspec ranges reviewed 2026-10-08.

Sources establish **primitive** behavior. They do **not** establish that Animora's custom protocol is implemented or tested. Release-blocking proofs:
two connections cannot publish the same tenant sequence in reverse order; rollback leaves no phantom journal entry or token; direct DML and owned-child changes are captured; exact tenant tombstones; GC and export race; repeated, missing and expired pages; atomic SQLite projection and checkpoint with pending overlay; restore with counter catch-up but changed epoch; N / N-1 / N-2 wire precision and version support; claim lease expiry and fencing; concurrent last-stock, period, seat and interval allocation; actual memory, RPO and RTO on both profiles.
Local absence may skip developer DB tests **with a stated reason**. Mandatory PostgreSQL CI and release gates treat absence as **failure**.

### §PG.7 Existing contract and type seams (blocking before server integration)

Code inspected: `Domain/Primitives/SyncableEntity.Version` is `string?`. `CommandEnvelope.ExpectedAggregateVersion` is `long?`. `SettlementResult.ServerVersion` is `string?`. Numeric local revisions cannot hold or prove a PostgreSQL UUID server token. They are **not** journal sequences. D02 maps the Domain string Version to `uuid` through an explicit canonical converter. No provider type enters Domain. No SQLite schema or token interpretation changes. D02 also fixes any old source comment that calls UpdatedAt a critical cursor. Local transport columns stay separate from server concurrency authority.

D05 adds **`string? ExpectedServerVersion`** as an additive field to the VERSIONED command envelope, and to the server's command context, effect and receipt (`AppliedServerVersion uuid/string`, not the old numeric `AppliedAggregateVersion`). Keep the old local numeric field for local compatibility **only**. Never compare it to server Version, cast or hash it into a UUID, or accept it as server concurrency evidence. Introduce **WireContract v2** for this semantic change, with explicit V1/V2 compatibility handlers and a support window. D08 and E03 use the negotiated CurrentVersion (v2 once D05 lands), not a literal 1. V1 numeric-only, stale-sensitive writes need controlled upgrade or conflict. Safe additive or idempotent commands may use their defined legacy policy. **No silent client downgrade.** Pre-release absence of a deployed server does not erase already-persisted V1 local pending records.

E03 and E06 persist the immutable server-base token seen **when** an offline edit or command is created, in a dedicated server-token field on journal or replica metadata. It is carried to ExpectedServerVersion. LocalSequence and the local numeric revision keep their local meaning. **E06 never substitutes the latest downloaded UUID at send or retry time**, which would bypass stale-edit detection. Legacy pending work with no protected server evidence: resolve explicit prerequisite receipt references under server policy, or preserve and park it for review or upgrade. **Never drop it or fabricate a matching current token.** The journal schema migration is additive, uses the next free local ordinal, and keeps the old serialized payload and fingerprint verifiable. A receipt stores the canonical ServerVersion. Dependent commands use an explicit prerequisite identity or effect token, **not** arrival order or numeric conversion.

Tests in D05, E03, E06 and E08 must cover: V1 persisted-journal upgrade; no-token and stale-token writes; UUID codec, converter and RETURNING; dependency receipts; retry identity and fingerprint preservation; N / N-1 / N-2 compatible policies. This is specified work, **not** a claim that current `CommandEnvelope` source already has the new field.

### §PG.8 Database business-invariant enforcement and trigger security

EF append-only guards are **not sufficient** against direct SQL. D07's reviewed PostgreSQL migrations also restrict ordinary runtime UPDATE and DELETE on posted finance, audit and stock facts, and install immutable-row and child guards. Guarded correction uses reversal records. Balanced journal lines need a reviewed **deferred constraint trigger** checked at posting transaction completion. A CHECK on one row cannot SUM sibling lines. The trigger also checks tenant, currency and account references, and draft-to-posted rules. Grant only the runtime functions and columns required. Maintenance and break-glass stay a **separately audited** role and must reconcile invariants before reopening traffic. **No direct DML permission is a licence to bypass business command authority.**

Protect the journal, counter and graveyard from arbitrary runtime writes. Trigger and allocator functions are owner-controlled, with EXECUTE revoked from PUBLIC after review. Use a fixed safe `search_path` (`pg_catalog` plus explicit qualified objects), validated tenant keys, and no dynamic identifiers. `SECURITY DEFINER`, if required, uses a least-privilege owner, **never** a superuser. Explicitly test RLS and tenant behavior, because definer rights can bypass row policies. The runtime role has no table ownership, no function replacement, no SET ROLE escalation, and no `session_replication_role` or trigger-disable privilege. **Any schema, function or privilege drift is a readiness and release blocker.**

Provisioning order: the feed-state FK to Tenant, when used, is `DEFERRABLE INITIALLY DEFERRED` for the bootstrap transaction. Create the feed state and gate first, the captured Tenant root second, then children. The FK validates at COMMIT. **No trigger-disable and no untracked bootstrap exception.** D08 depends on D07 for both finance read projections.

---

> **End of specification.** This file is the single source of truth for Animora's software architecture. `STACK.md` is the source of truth for exact technology versions. Keep both self-contained. Do not re-derive one from the other.
