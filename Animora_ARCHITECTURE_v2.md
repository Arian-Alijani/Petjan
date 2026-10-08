# Animora — Software Architecture Specification

> **Status:** Architecture Baseline — v2.0  
> **Audience:** software engineers, architects, security reviewers, QA/chaos-test engineers, DevOps/operations, and AI coding agents  
> **Purpose:** the single authoritative definition of how Animora is structured, how it behaves, how state and business intent move across boundaries, and which invariants every implementation must preserve.
>
> `STACK.md` remains the source of truth for exact technology versions and technology-specific facts. This file is the source of truth for architectural behavior. Where a technology name is unavoidable, it is used only to define an integration boundary or a required capability.
>
> **Core correction from the previous architecture:** CommunityToolkit.Datasync is **not** the universal synchronization mechanism for Animora. Critical state synchronization is owned by Animora through a project-owned Sync Engine backed by SQL Server Change Tracking; Datasync remains an adapter for a deliberately limited class of low-risk, simple replicated state. This preserves the project’s technology choice without making a third-party sync library the correctness authority for financial, collaborative, or high-integrity workflows.
>
> **Provenance:** structural views use C4; documentation follows arc42; domain decomposition uses DDD-lite; named behavioral patterns are validated against Microsoft Azure Architecture Center / Microsoft Learn guidance. The architecture is intentionally technology-aware at integration boundaries, but business behavior remains owned by Animora.

---

# 0. Architecture at a Glance

| Concern | Architectural answer |
|---|---|
| Deployment style | **Vendor-operated, central, multi-tenant SaaS** |
| Launch topology | **Single runtime node / single ASP.NET Core process**, database and file store co-located where practical |
| Architecture style | **Modular Monolith** |
| Internal layering | **Clean/Onion + Ports & Adapters** |
| Domain decomposition | **DDD-lite bounded contexts** inside one deployable |
| Global authority | **Server is the single global authority** |
| Client truth | Desktop holds **provisional local truth**; it becomes global only after server settlement |
| Desktop | Native offline-first client |
| Web | Online-only, thin server-rendered client |
| Critical sync | **Animora Custom Sync Engine + SQL Server Change Tracking** |
| Simple sync | **CommunityToolkit.Datasync 10.1.2 adapter**, only for entities whose correctness model is compatible with Datasync |
| Business intent | Durable, idempotent **Business Command Pipeline** |
| Local durability | **SQLite + EF Core**, encrypted at rest |
| Local mutation durability | **Mutation Journal** in the same local transaction as provisional state changes |
| Server state change feed | **SQL Server Change Tracking** |
| Business event feed | **Transactional Outbox** |
| Row concurrency | **SQL Server `rowversion`** |
| Conflict detection | Base-version mismatch + server change evidence |
| Conflict decision | Domain-specific policy; never generic client-wins/server-wins for critical data |
| Finance | Immutable posted facts + compensating/reversal commands |
| Inventory | Immutable movements + server settlement policy |
| Scheduling | Transactional resource allocation, not row-version checks alone |
| Notifications | Hint only; never authoritative synchronization |
| Tenancy | Application authorization + tenant-aware data access + RLS defense-in-depth |
| Authentication | Local ASP.NET Core Identity; web cookies; desktop bearer/refresh |
| Offline authorization | Signed capability snapshot with explicit expiry |
| Security | Authentication, authorization, entitlement are separate planes |
| Runtime Internet | **No mandatory external Internet dependency** |
| Files | Filesystem/object bytes + SQL metadata; resumable transfer; content-addressed immutable objects |
| Observability | Local structured logs, health, metrics/traces, audit, sync diagnostics |
| Recovery | Declared RPO/RTO; tested backups; DB/files/runtime keys restored as one recovery point |
| Scale-out | Explicit escape hatch via deployment stamps; not required at launch |
| Main correctness claim | **No silent data loss, no duplicate business effect, deterministic conflict outcomes, eventual convergence after all mutations settle** |

## 0.1 The central rule

Animora has three different kinds of truth:

```text
1. Server Business Truth
   = globally authoritative settled business state

2. Server Replication Truth
   = the ordered change position used to deliver server state changes

3. Client Provisional Truth
   = local state + durable unsent intent while offline
```

These are never conflated.

Likewise:

```text
State replication        ≠ business intent settlement
Sync cursor              ≠ entity version
Entity version           ≠ operation identity
Notification             ≠ synchronization
Idempotency              ≠ ordering
Concurrency detection    ≠ conflict resolution
Provisional state        ≠ posted/settled state
Authentication           ≠ authorization
Authorization            ≠ entitlement
Change Tracking          ≠ domain event
Outbox                   ≠ change feed
```

## 0.2 What “10/10” means in Animora

No distributed/offline system can guarantee that conflicts never occur. Animora instead guarantees that conflicts are **never silently resolved incorrectly**.

The architecture target is:

```text
No silent data loss
No duplicate financial effect
No cross-tenant data exposure
No invalid financial posting
No cursor skip
No destructive overwrite of unsent work
No implicit resurrection of deleted records
No permanent unknown settlement
No unbounded retry loop
No notification-dependent correctness
No tenant can monopolize the runtime indefinitely
No full-resync operation can destroy the only good local replica
```

The system may reject, defer, compensate, or require human review. Those are valid outcomes. Silent corruption is not.

---

# 1. Architecture Drivers and Quality-Attribute Scenarios

| ID | Driver | Quality attribute | Architectural response | Verification |
|---|---|---|---|---|
| R1 | Windows + macOS + Web | Portability | Shared Domain/Application/Contracts; native desktop presentation; Blazor web presentation | One rule change affects shared core once |
| R2 | Many users on weak central hardware | Performance efficiency | Push safe computation to clients; delta sync; bounded server work; static SSR; workload isolation | Capacity benchmark on declared hardware/workload |
| R3 | Strong security | Security | Tenant isolation, AuthN/AuthZ/Entitlement separation, local PKI, encrypted local store, protected keys | Security/penetration + isolation gates |
| R4 | Long offline windows | Reliability | Durable local mutation journal, checkpointed delta pull, explicit offline capability | Offline/reconnect/crash matrix |
| R5 | Multi-user simultaneous edits | Reliability/Correctness | Versioned state, patch/rebase where provable, domain-specific conflicts | Concurrent-edit test suite |
| R6 | Financial correctness | Integrity | Commands, idempotency, append-only ledger, server settlement | Duplicate/lost-response/period-close tests |
| R7 | Inventory correctness | Integrity | Movement model + settlement policy + resource constraints where needed | Oversell/concurrency tests |
| R8 | Multi-tenancy | Security | Server-derived tenant context, tenant-aware repository, RLS hardening | Cross-tenant fuzz/attack suite |
| R9 | Direct server DB maintenance must propagate | Operability + consistency | SQL Server Change Tracking for tracked state; governed maintenance contract | Direct-SQL visibility gate |
| R10 | Near-real-time collaboration | Responsiveness | Notification hints + delta pull; notification transport never authoritative | p95 visibility latency |
| R11 | No runtime Internet | Reliability + security | Local DB, local identity, local PKI, local observability, packaged assets | Internet-isolation test |
| R12 | Central single node is intentional | Cost + simplicity | Modular monolith, no broker/cache at launch, explicit DR | Restore drill |
| R13 | Mixed client versions | Operability | Contract versioning, capability negotiation, N/N-1/N-2 support window | Mixed-fleet gate |
| R14 | Long-lived tenant data | Maintainability | Archive/retention policies, reporting projections, migration discipline | Data growth and restore tests |
| R-DR | Node loss must be recoverable | Reliability | Declared RPO/RTO, off-host immutable backup, tested restore | Full restore drill |

### 1.1 Recommended launch targets

These are architecture targets to be accepted or adjusted through an ADR before GA:

```text
Maximum supported offline window:       30 days
Change Tracking retention floor:       offline window + 15 days minimum
Recommended launch RPO:                 <= 5 minutes
Recommended launch RTO:                 <= 60 minutes
Foreground change-visibility target:    p95 <= 3 seconds on healthy LAN
Command settlement target (online):     p95 <= 500 ms excluding local/DB contention
Server resource isolation:              interactive traffic protected from background work
```

A deployment may choose another offline window, but:

```text
ChangeTrackingRetention >= MaxOfflineWindow + SafetyMargin
TombstoneRetention       >= MaxOfflineWindow + SafetyMargin
AuthorizationRevocation  >= MaxOfflineWindow + SafetyMargin
Contract/License retention rules
must remain compatible with the same offline policy.
```

The numeric targets above are **performance/recovery targets**, not correctness assumptions. Any published client capacity is earned from measurement.

---

# 2. Guiding Architecture Principles

1. **Server is the only global business authority.** Clients can work offline but cannot declare global settlement.
2. **State replication and business intent are different flows.** They may share transport and local orchestration, but never semantics.
3. **Minimize coordination.** Independent work should not wait for global coordination.
4. **Make illegal states hard to represent.** Financial facts are append-only; inventory is movement-based; allocation is transactional.
5. **Use the database for the invariants it can prove well.** Unique constraints, foreign keys, transactions, rowversion, RLS and Change Tracking should carry as much correctness as possible.
6. **Every asynchronous effect is at-least-once.** Receiving logic is idempotent.
7. **Every retry has a bounded budget.**
8. **A notification is never authoritative.** Missing notifications must not cause missed data.
9. **A checkpoint advances only after durable application.**
10. **A full resync is additive/reconstructive, never destructive to unsent work.**
11. **Tenant scope is derived from server-side authenticated membership.**
12. **Client-side authorization is UX only.**
13. **Critical direct SQL mutation is governed.**
14. **Observability is local and complete even without Internet.**
15. **Every pattern must earn its place.**
16. **Prefer one strong transaction on a single node to distributed choreography.**
17. **Do not introduce infrastructure to solve a problem that measurement has not demonstrated.**
18. **A conflict is an expected state; silent corruption is a defect.**

---

# 3. C4 — System Context

```text
Clinic Staff
  ├── Desktop
  └── Web
          │
          ▼
   ┌───────────────────────────────┐
   │          ANIMORA              │
   │ central multi-tenant SaaS     │
   └───────────────────────────────┘
          ▲
          │
Platform Operator
          │
          ▼
Subscription / Management Plane

Separate vendor-side systems:

Public Marketing / Subscription Website
    └── register / buy / download
              │
              └── management-plane onboarding contract
```

## 3.1 Actors

### Clinic Staff
Reception, cashier, veterinarian, inventory staff, manager, etc.

### Clinic Owner / Manager
A role within a tenant, not a separate backend.

### Platform Operator
Vendor staff with controlled cross-tenant management authority, especially subscription, licensing and support operations.

### Public Website
A separate system for customer registration, plan purchase and installer delivery. It never becomes a runtime dependency of the clinic-serving plane.

---

# 4. C4 — Container View

```text
Desktop Client
  Presentation
  Local Application Core
  Local SQLite
  Mutation Journal
  Sync Coordinator
  Secure Device Storage

Web Client
  Blazor Web App
  Static SSR + selective InteractiveServer
  No durable offline replica

Animora Server Process
  Authentication / Authorization
  Business API
  Custom Sync API
  Datasync Compatibility Surface
  Notification Hint Surface
  Application Core
  Domain Modules
  Background Workers
  Reporting/Read Projections
  Infrastructure

Database
  SQL Server 2025

File Store
  Server filesystem / content-addressed objects

Management Plane
  provisioning / licensing issuance / updates / backup/support
```

## 4.1 Invariant

The following surfaces are separate entry points into the same application/domain core:

```text
Business API
Critical Sync API
Datasync adapter surface
Notification hint surface
Operator APIs
```

None bypass the application/domain layer and write directly to business tables except formally defined database maintenance operations.

---

# 5. Internal Layering — Clean/Onion + Ports & Adapters

```text
Presentation
    │
    ▼
Application
    │
    ▼
Domain

Infrastructure implements ports
and points inward.
```

## 5.1 Domain

Contains:

- aggregates
- entities
- value objects
- invariants
- domain services
- domain events
- business policies that belong to the domain

Domain knows nothing about:

- HTTP
- EF Core
- Datasync
- SQL Server
- SQLite
- Blazor
- Avalonia
- authentication tokens
- file paths
- notification technologies

## 5.2 Application

Contains:

- commands/handlers
- queries
- workflows
- authorization orchestration
- idempotency
- sync coordination ports
- mutation settlement orchestration
- transaction boundaries
- conflict-resolution orchestration
- read projection orchestration

## 5.3 Infrastructure

Contains:

- EF Core implementations
- SQL Server integration
- SQL Change Tracking adapter
- Datasync adapter
- SQLite adapter
- encrypted local storage
- file store
- Data Protection
- PKI integration
- outbox dispatcher
- notification adapter
- clock/time source
- device key storage
- diagnostics

## 5.4 Ports

Examples:

```text
IClock
ITransaction
IUnitOfWork
IStateSyncEngine
IChangeFeed
IMutationStore
IBusinessCommandTransport
IFileStore
IAuditSink
INotificationHintChannel
ISecureKeyStore
IEntitlementProvider
```

---

# 6. Bounded Contexts

| Context | Owns | Publishes | Depends on |
|---|---|---|---|
| Identity | users, credentials, devices, roles | UserAuthenticated, DeviceRegistered, CredentialChanged | — |
| Tenant/Clinic | tenant, clinic profile, memberships | TenantProvisioned, MembershipChanged | Identity |
| Patient/Owner | patients, owners, contacts | PatientCreated, OwnerChanged | Tenant |
| Clinical | clinical records, visits, vaccinations | VisitRecorded, VaccinationDue | Patient |
| Scheduling | appointments, resources, allocations | AppointmentBooked, AppointmentMoved | Patient |
| Sales/POS | carts, sales, cash sessions | SaleCompleted, CashSessionClosed | Patient, Inventory |
| Inventory | products, lots, stock movements, balances | StockReceived, StockAdjusted | Tenant |
| Finance | invoices, payments, journal, periods | InvoicePosted, PaymentSettled, PeriodClosed | Sales, Inventory |
| Reporting | read projections and reporting models | none | selected contexts |
| Files | file metadata/lifecycle | FileAvailable, FileQuarantined | Tenant |
| Subscription | plans, seats, billing periods | PlanChanged, SubscriptionSuspended | Tenant |
| Licensing | license verification artifacts and state | LicenseValidated, LicenseExpiring | Subscription |
| Audit | immutable audit records | none | subscribes to all relevant contexts |
| Synchronization | infrastructure coordination only | none | Application contracts |
| Notification | delivery hints only | none | Sync state |

Synchronization and notification are **not business bounded contexts**. They are infrastructure capabilities.

---

# 7. Context Coupling Rules

1. Finance never directly edits Sales or Inventory records.
2. Finance consumes domain events or invokes explicit application workflows.
3. Reporting never writes business state.
4. Audit never acts as a coordination channel.
5. Synchronization never decides business policy.
6. Notification never owns state.
7. Identity never grants entitlement.
8. Entitlement never authenticates.
9. Tenant context never comes from client payload.
10. Cross-context writes are either:
   - atomic within one application transaction, or
   - explicit eventual consistency with a visible intermediate status.

---

# 8. Consistency Classes

Every state in the system must belong to one of these classes:

### C1 — Local Provisional
Can exist offline.

Example:

```text
LocallyCommitted
PendingSettlement
```

### C2 — Server Transactional
Globally authoritative immediately after commit.

Example:

```text
PaymentSettled
JournalPosted
ResourceAllocated
```

### C3 — Eventually Consistent Projection
Derived from authoritative state.

Example:

```text
DashboardSummary
LowStockView
ReportingProjection
```

### C4 — Notification Hint
Not state.

Example:

```text
"Tenant 42 changed"
```

No implementation may silently use C4 as C2.

---

# 9. Multi-Tenancy

## 9.1 Tenant model

Launch:

```text
Shared SQL Server
Shared database
Shared schema
Tenant discriminator
```

Each record that belongs to a tenant carries authoritative tenant ownership.

## 9.2 Tenant resolution

Request boundary:

```text
Credential
  ↓
Authenticated principal
  ↓
Membership
  ↓
ResolvedTenantContext
```

`ResolvedTenantContext` is immutable for the request and all downstream operations.

## 9.3 Isolation layers

### Mandatory

1. Authentication.
2. Server-side tenant membership resolution.
3. Authorization.
4. Tenant-aware application/data-access infrastructure.
5. Replication scope filtering.
6. File authorization.

### Defense-in-depth

7. SQL Server RLS.
8. Tenant-safe caching.

RLS does not become the only security boundary.

Microsoft documents RLS with `SESSION_CONTEXT` for middle-tier applications that share a SQL account, including filter and block predicates. citeturn633126search7turn633126search8

## 9.4 RLS rule

For each database connection:

```text
Set SESSION_CONTEXT(TenantId)
Set SESSION_CONTEXT(RequestId)
Set SESSION_CONTEXT(UserId)
```

with server-derived values.

RLS must be tested with connection pooling, parallel plans and the documented `SESSION_CONTEXT` mitigation. Failure of RLS does not imply failure of application-owned tenant isolation.

## 9.5 Cross-tenant access

A request such as:

```text
Tenant A user → Patient in Tenant B
```

must fail before returning the row.

Failures must not differ in a way that discloses whether a protected record exists.

---

# 10. Identity / Authorization / Entitlement

These are three independent planes.

```text
Authentication
    WHO?

Authorization
    WHAT may they do?

Entitlement
    IS this feature licensed?
```

Order:

```text
Authenticate
   ↓
Resolve tenant/membership
   ↓
Authorize resource/action
   ↓
Check entitlement
   ↓
Execute
```

## 10.1 Browser

Web authentication uses secure cookies.

Unsafe state-changing requests must use ASP.NET Core antiforgery protections as appropriate to the request/form mechanism. ASP.NET Core 10 provides antiforgery services for Blazor/SSR forms and explicitly distinguishes CSRF-protected form flows from JSON APIs. citeturn453365search5turn453365search6

## 10.2 Desktop

Desktop uses the stack-approved local bearer/refresh token mechanism, stored in OS-protected credential storage.

Do not describe this mechanism as a generic OAuth/OIDC provider. If a real OIDC authorization server is introduced later, that is a new architecture boundary and ADR.

## 10.3 Authorization model

Use:

```text
RBAC
+
Policy-based authorization
+
Resource-based authorization
```

Example:

```text
Role = Cashier
Policy = refund below threshold
Resource = this specific cash session
```

The client may hide an operation for UX, but the server always evaluates authorization.

---

# 11. Offline Session and Capability Snapshot

Bearer/refresh credentials do not themselves prove offline authority.

The desktop client receives a signed, device-bound capability snapshot:

```text
CapabilitySnapshot
  Tenant
  User
  Device
  Roles
  Policies
  CapabilityVersion
  IssuedAt
  ExpiresAt
  ServerPolicyVersion
  Signature
```

## 11.1 Offline rules

Every capability declares:

```text
MaximumOfflineDuration
SensitiveOperationPolicy
ExpiryBehavior
Re-authenticationRequirement
```

Recommended baseline:

```text
Low-risk reads:
    long offline window

Routine record editing:
    normal offline window

Cash/payment:
    shorter window

Refund:
    short or zero

Period close:
    zero offline authorization

License/entitlement-sensitive operations:
    explicit license policy
```

## 11.2 Revocation

Offline revocation cannot be instantaneous against a truly disconnected device.

Guarantee:

```text
Online:
    immediate

Offline:
    next trusted contact
```

plus capability expiry.

After expiry:

```text
read-only / locked / sensitive operations blocked
```

Never silently extend authority.

---

# 12. Core Synchronization Architecture

This is the most important section of the architecture.

## 12.1 Three synchronization mechanisms

Animora has:

### A. Critical State Synchronization

```text
Animora Custom Sync Engine
        ↓
SQL Server Change Tracking
```

### B. Simple Replicated State

```text
CommunityToolkit.Datasync 10.1.2
        ↓
Datasync Adapter
```

Only permitted for explicitly classified low-risk entities.

### C. Business Intent Settlement

```text
Business Command Pipeline
        ↓
Transactional server commit
```

These mechanisms never exchange semantics implicitly.

---

# 13. Why Datasync Is Not Universal

Datasync 10.1.2 maintains its own operation queue and delta token mechanism. Its documented delta token is derived from the maximum `UpdatedAt` value of the pulled set, and its `OfflineDbContext` refuses normal pulls for an entity while that entity still has pending operations. Its resynchronization guidance removes pending operations and delta tokens before a new pull. citeturn292214search0turn292214search2

Those behaviors are useful for a simple occasionally-connected CRUD application, but they are not sufficient as Animora's correctness authority for critical collaborative state.

Therefore:

```text
Datasync = adapter
Animora Sync Engine = architecture authority
```

This is not a rejection of Datasync. It is a containment boundary.

---

# 14. Synchronization Classification

Every synchronizable entity is assigned one of:

```text
SYNC-CRITICAL
SYNC-SIMPLE
NOT-SYNCED
COMMAND-ONLY
PROJECTION-ONLY
```

## 14.1 SYNC-CRITICAL

Examples:

```text
Patient
Owner
Clinical mutable metadata
Appointment state
Draft documents
Operational assignments
Inventory operational projections
Financial documents that must be visible offline
```

Actual inclusion is per entity/aggregate.

Mechanism:

```text
Custom Sync Engine + Change Tracking
```

## 14.2 SYNC-SIMPLE

Examples:

```text
reference catalogs
low-risk lookup data
UI preferences
static configuration
```

Mechanism:

```text
Datasync
```

only if:

- timestamp cursor has passed the technology PoC,
- conflict semantics are genuinely compatible,
- no critical unsent work depends on its pull semantics,
- the entity is safe for whole-row resolution or has no meaningful concurrency concern.

## 14.3 COMMAND-ONLY

Examples:

```text
RegisterPayment
PostInvoice
ReversePayment
AdjustStock
BookAppointment
CloseCashSession
ClosePeriod
```

These are not CRUD sync entities.

---

# 15. Logical Sync Contract

The custom synchronization protocol is technology-independent.

A client has:

```text
SyncCheckpoint
SyncProfileVersion
PendingMutationJournal
ConflictInbox
```

A synchronization session consists of:

```text
1. Authenticate
2. Validate device/capability
3. Establish scope
4. Reconcile unknown settlements
5. Push eligible local mutations
6. Pull server state deltas
7. Apply/rebase locally
8. Commit local transaction
9. Advance checkpoint
10. Emit UI state
```

The exact HTTP route names are not an architectural dependency.

---

# 16. SQL Server Change Tracking as Critical State Feed

SQL Server Change Tracking is a lightweight DML change tracking mechanism designed for synchronization. It is transaction-commit based and provides change versions; Microsoft recommends snapshot isolation for correct, consistent enumeration of change sets. SQL Server 2025 also uses an adaptive shallow cleanup approach for Change Tracking metadata. citeturn453365search0turn453365search1turn751531search0

## 16.1 Concept mapping

```text
Animora ChangeSequence
        =
SQL Server Change Tracking version
```

This is an implementation of the architectural abstraction; application code does not expose SQL Server-specific types.

## 16.2 Cross-tenant changes and the global cursor

The SQL Change Tracking version is global to the database, not tenant-local.

A client may therefore advance its checkpoint across changes that belong to other tenants:

```text
Version 1001 → Tenant A
Version 1002 → Tenant B
Version 1003 → Tenant B
Version 1004 → Tenant A
```

A Tenant A client may safely advance from `1000` to `1004` even though versions `1002` and `1003` were not returned to it, because those changes are outside its authorized replication scope.

This is a deliberate property of the critical sync design:

```text
Global server change position
+
per-request tenant/authorization filtering
=
safe scoped replication
```

A change in authorization/scope invalidates the meaning of the old local dataset and may therefore require a controlled resync as defined in §54.

## 16.3 Change Tracking is not a complete field-history system

Change Tracking answers:

```text
Which row changed?
What operation happened?
At which change version?
```

It does **not** by itself provide an unlimited historical sequence of every prior field value.

Therefore automatic field-level rebase is allowed only when the server has sufficient evidence, such as:

```text
changed-column metadata where correctly applicable
+
domain mutation history where maintained
+
an operation whose non-overlap can be proven
```

If the evidence is insufficient, Animora chooses:

```text
Conflict / NeedsAttention
```

rather than guessing.

This rule is especially important when a row has been updated multiple times between the client's base version and the current version, or when an approved maintenance operation modified the row outside the normal application command path.

## 16.4 Why Change Tracking

It avoids maintaining a custom trigger-heavy synchronization log for basic DML visibility and provides:

- committed change versions
- insert/update/delete awareness
- lightweight overhead
- retention-based cleanup
- standard SQL tooling
- direct visibility of qualifying SQL DML

Microsoft explicitly contrasts it with custom timestamp/trigger/table-based tracking and notes that Change Tracking is based on committed transactions. citeturn453365search0turn453365search1

---

# 17. Change Feed Correctness Protocol

Microsoft documents the following safe sequence:

```text
BEGIN SNAPSHOT TRANSACTION

1. Validate last synchronization version
   CHANGE_TRACKING_MIN_VALID_VERSION()

2. Capture next synchronization version
   CHANGE_TRACKING_CURRENT_VERSION()

3. Enumerate CHANGETABLE(CHANGES...) for all required tables

4. Read current rows / tombstones for those changed identities
   inside the same snapshot

COMMIT
```

This avoids race conditions involving concurrent commits and cleanup. citeturn751531search0turn453365search10

Animora adopts this as the critical delta protocol.

---

# 18. Change Tracking Retention

Change Tracking metadata is retained only for the configured `CHANGE_RETENTION` period. If a client checkpoint falls below `CHANGE_TRACKING_MIN_VALID_VERSION()`, incremental synchronization is no longer valid and the client must perform a controlled full resynchronization. citeturn453365search8

Therefore:

```text
If cursor valid:
    incremental sync

If cursor invalid:
    controlled full resync

Never:
    guess
    silently skip
    start from current version
```

## 18.1 Retention invariant

```text
ChangeTrackingRetention
    >=
MaximumSupportedOfflineWindow
+
SafetyMargin
```

A supported offline duration cannot be published unless the server is configured and capacity-tested for the corresponding retention.

---

# 19. Tombstones and Delete Semantics

Synchronized business rows use logical deletion wherever clients must learn the deletion.

```text
Deleted = true
DeletionVersion
DeletedAt
DeletionReason
DeletedBy
```

Hard-delete is prohibited from ordinary production application paths for synchronized entities.

A hard delete is permitted only through a reviewed maintenance operation that first proves all synchronization/retention implications.

Reason:

Change Tracking can report that a row was deleted, but it cannot reconstruct the full prior row for a client that needs a tombstone. Therefore Animora uses durable tombstone state for synchronization safety.

---

# 20. Direct Database Changes

This is a first-class requirement.

## 20.1 Safe direct-state mutation

A direct SQL `INSERT/UPDATE` on a **critical-sync table governed by Animora's database maintenance policy** is visible to the critical sync feed because SQL Server Change Tracking tracks qualifying DML.

Therefore:

```text
DBA / governed maintenance SQL
      ↓
SQL Server DML
      ↓
Change Tracking
      ↓
Client delta
```

This guarantee applies to the **custom critical sync path**, which does not depend on Datasync's `UpdatedAt` cursor.

It does **not** mean arbitrary SQL against a Datasync `SYNC-SIMPLE` table is automatically safe. A Datasync table may rely on library-specific synchronization metadata such as `UpdatedAt`, `Version`, and `Deleted`. Direct SQL against such a table is allowed only through a maintenance operation that preserves every Datasync invariant, or that table must be migrated to the critical custom sync path.

Therefore:

```text
Critical Sync table
    → governed direct SQL can remain sync-visible

Datasync SYNC-SIMPLE table
    → arbitrary direct SQL is forbidden
```

This distinction prevents a direct database correction from being visible to clients while still leaving the Datasync metadata inconsistent.

## 20.2 Direct business mutation

Direct SQL must **not** be used to bypass critical business invariants.

Examples forbidden as arbitrary SQL:

```text
DBA / maintenance SQL
      ↓
SQL Server DML
      ↓
Change Tracking
      ↓
Client delta
```

## 20.2 Direct business mutation

Direct SQL must **not** be used to bypass critical business invariants.

Examples forbidden as arbitrary SQL:

```text
UPDATE PostedInvoice SET Total = ...
UPDATE JournalEntry SET Amount = ...
UPDATE Payment SET Status = ...
UPDATE StockBalance SET Quantity = ...
UPDATE FiscalPeriod SET Status = Closed ...
```

If a business correction is required, use:

```text
Administrative Command
or
Audited Maintenance Procedure
```

that invokes the same domain/application rules or a specifically defined controlled correction path.

## 20.3 DBA access classes

```text
ReadOnly Operational
        ↓
Routine Maintenance
        ↓
Emergency Break-Glass
```

Break-glass requires:

```text
identity
reason
ticket/reference
timestamp
operator
audit
```

---

# 21. Change Tracking vs Outbox

These are intentionally separate.

```text
Change Tracking:
    Which replicated state rows changed?

Outbox:
    Which domain events/effects were produced by a transaction?
```

For a business command:

```text
Transaction
    ├── business state
    ├── audit
    └── outbox event
```

At commit, SQL Server Change Tracking may also record the affected synchronized rows.

The two feeds are not merged.

---

# 22. Local Materialized State

The desktop local database contains:

```text
MaterializedState
LocalReferenceData
PendingMutationJournal
BusinessCommandQueue
SyncCheckpoints
ConflictInbox
DeviceState
LocalAudit/Diagnostics
```

This is a **replica**, not an independent global authority.

---

# 23. Local Mutation Journal

Every offline mutation that must survive a crash is recorded durably.

Logical fields:

```text
MutationId
MutationKind
EntityType
EntityId
AggregateId
BaseVersion
Patch / CommandPayload
ChangedFieldSet
CausationId
DependsOnMutationId
LocalSequence
CreatedAtUtc
Status
AttemptCount
LastErrorCode
LastErrorAt
```

`MutationKind`:

```text
StatePatch
BusinessCommand
DatasyncOperation
```

The local journal is owned by Animora, even when the actual transport adapter happens to be Datasync.

---

# 24. Atomic Local Mutation

For a state mutation:

```text
BEGIN LOCAL TRANSACTION

1. Validate locally
2. Update local materialized state
3. Insert mutation journal record
4. Update UI-visible provisional state

COMMIT
```

Either all happen or none happen.

This eliminates:

```text
UI says saved
but mutation queue lost
```

and:

```text
mutation exists
but local state never committed
```

---

# 25. Business Command Queue

The business command queue is semantically separate from generic state synchronization.

Each command has:

```text
OperationId
CommandType
AggregateId
Payload
CommandFingerprint
CausationId
DependsOnOperationId
ExpectedAggregateVersion (where applicable)
CreatedAtUtc
LocalSequence
Status
```

---

# 26. Operation Identity

Server uniqueness:

```text
(TenantId, OperationId)
```

A retry with identical fingerprint:

```text
return existing settlement result
```

A retry with the same OperationId and different fingerprint:

```text
integrity violation
audit
reject
```

This is stronger than a simple “already seen” cache.

---

# 27. Settlement State Machine

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

`Posted`, `Settled`, and `Closed` are not local provisional labels.

A client never renders a financial effect as globally settled until the server confirms settlement.

---

# 28. SettlementUnknown

Critical scenario:

```text
Client sends Op123
Server commits
Response is lost
```

Client enters:

```text
SettlementUnknown
```

It must not create a new OperationId to “try again”.

Recovery:

```text
Retry/query Op123
       ↓
server returns original result
```

This guarantees at-most-one business effect under at-least-once transport.

---

# 29. Causality and Dependencies

`CausationId` and `DependsOnOperationId` remain distinct.

```text
CausationId:
    provenance

DependsOnOperationId:
    settlement precondition
```

Dependencies may reference:

```text
StateMutation
BusinessCommand
```

The graph must be a DAG.

Example:

```text
M1 CreatePatient
      ↓
C1 CreateInvoice
      ↓
C2 RegisterPayment
```

A dependent operation cannot settle before its prerequisite is settled.

---

# 30. Mutation Scheduler

The client scheduler partitions work into:

```text
Critical Business Commands
Critical State Mutations
Simple Datasync Operations
Routine Pull
Maintenance
```

Ordering rules:

1. Dependency edges are honored.
2. Operations with independent aggregates may run concurrently only when safe.
3. Business commands default to conservative serialization.
4. Datasync `ParallelOperations` stays at 1 unless a specific entity is proven order-independent.
5. Local mutation journal order is deterministic.
6. Network arrival order is never treated as business order.

---

# 31. Conflict Architecture

Conflict resolution is a pipeline:

```text
1. Detect
2. Classify
3. Gather evidence
4. Apply domain policy
5. Commit or reject
6. Record audit
7. Rebase local state
```

## 31.1 Detection

```text
BaseVersion != CurrentServerVersion
```

means:

```text
stale
```

It does not itself mean:

```text
wrong
```

## 31.2 Evidence

For critical state the engine uses available server change evidence, which may include:

```text
rowversion
Change Tracking operation
changed column metadata where enabled
domain mutation history where available
maintenance-origin metadata
```

## 31.3 Conservative rule

If the server cannot prove that a stale local patch is non-overlapping with authoritative changes, it must not silently merge.

Result:

```text
Conflict / NeedsAttention
```

This is intentionally safer than speculative field merging.

---

# 32. Field-Level Merge

Automatic field-level merge is allowed only when overlap can be proven.

Example:

```text
Client:
Phone changed

Server:
Address changed

Proven disjoint
→ merge
```

Not:

```text
Client:
Phone changed

Server:
same row changed several times,
history unavailable

→ guess
```

Instead:

```text
human review / domain policy
```

---

# 33. Conflict Policy Matrix

| Data | Default policy |
|---|---|
| UI preference | LWW acceptable |
| Display settings | LWW acceptable |
| Low-risk reference metadata | merge or server wins |
| Patient demographic | field-aware merge if proven; otherwise review |
| Medical history | append/preserve history; never blind overwrite |
| Appointment slot | transactional resource allocation |
| Draft invoice | version conflict + user resolution |
| Posted invoice | immutable; reversal/credit only |
| Payment | command + idempotency; no overwrite |
| Ledger | append-only |
| Inventory movement | append movement; settlement policy |
| Quantity-on-hand | derived, never client absolute write |
| User role | server authoritative |
| Entitlement | server authoritative |
| Audit | append-only |
| File object | immutable versioned object |

---

# 34. Patient / Clinical Data Modeling Principle

When possible, design the model to make independent operations additive.

Prefer:

```text
ClinicalEntry 1
ClinicalEntry 2
ClinicalEntry 3
```

over a giant mutable blob:

```text
MedicalHistoryText
```

This naturally reduces conflict frequency.

---

# 35. Finance Architecture

## 35.1 Ledger

Posted facts are immutable.

```text
Journal
JournalLine
PostingBatch
```

must obey:

```text
Σ Debit = Σ Credit
```

## 35.2 No posted mutation

Corrections are:

```text
Reversal
Credit Note
Adjustment
```

never:

```text
UPDATE posted value
```

## 35.3 Posting requirements

A posted financial fact carries or references:

```text
AccountingDate
DocumentDate
Currency
ExchangeRate version
Tax calculation snapshot
Rounding policy
Account/dimensions
Source document
Posting user/device
Posting timestamp
Operation reference
```

## 35.4 Fiscal periods

```text
Open
  ↓
SoftClose
  ↓
Review
  ↓
Closed
  ↓
Authorized Reopen / Correction
```

Offline commands dated into a closed period follow one explicit policy:

```text
Reject
Post to current period
Authorized back-post
Hold for accountant
Adjustment
```

Never silently post into a closed period.

---

# 36. Inventory Architecture

Stock is movement-based:

```text
Receipt
Sale
Consumption
Loss
Adjustment
Transfer
Return
```

Quantity-on-hand is derived.

## 36.1 Concurrent offline movements

Two offline movements can both be valid local facts.

The server decides whether the final outcome is allowed.

Policy dimensions:

```text
Negative stock allowed?
Reservation semantics?
Backorder?
Lot/batch?
Expiry?
Serial?
Location?
Cost layer?
```

## 36.2 No absolute offline stock overwrite

Forbidden:

```text
Set QuantityOnHand = 5
```

from a stale client.

Allowed:

```text
StockAdjustment +3
```

as a business command/movement.

---

# 37. Scheduling Architecture

A row-version check is not sufficient for a contended resource.

Examples:

```text
Room
Doctor slot
Boarding capacity
Cash drawer
Seat
Device allocation
```

Use a server-side transactional allocation invariant:

```text
Check allocation
+
insert/claim
+
commit
```

using appropriate:

```text
unique constraints
serializable/appropriate transaction semantics
resource allocation table
short-lived application lock where justified
```

No global lock.

---

# 38. Cross-Context Consistency

| Interaction | Consistency |
|---|---|
| Sale + stock reduction + payment | Atomic |
| Purchase receipt + payable | Atomic |
| Payment + cash session entry | Atomic |
| Refund + inventory return | Atomic |
| Sale → draft invoice | Eventual, explicit pending state |
| Settled sale → financial posting | Eventual only where intentionally designed |
| Reporting projection | Eventual |
| Audit | same-transaction where required for financial/security audit |

There must be no silent financially incomplete state.

---

# 39. Transactional Outbox

For a domain transaction:

```text
BEGIN

business state
domain event/outbox
audit

COMMIT
```

A background worker later dispatches the event.

This prevents the dual-write failure:

```text
DB committed
event lost
```

Microsoft documents the Transactional Outbox pattern as persisting business state and event information atomically, then processing pending outbox records asynchronously. citeturn633126search4

---

# 40. Outbox State Machine

```text
Pending
   ↓
Claimed
   ↓
Processing
   ├── Succeeded
   ├── RetryScheduled
   ├── DeadLetter
   └── Cancelled
```

Metadata:

```text
LeaseExpiry
AttemptCount
NextAttemptAt
FirstFailedAt
LastFailedAt
LastError
DeadLetterReason
```

A poison event must reach DeadLetter after a bounded retry budget.

---

# 41. Worker Architecture

Launch:

```text
ASP.NET Core BackgroundService
```

with bounded queues/concurrency.

Suggested priority classes:

```text
P0 Interactive / settlement
P1 State synchronization
P2 Outbox / notifications
P3 Reporting / maintenance
```

Background work must not consume all threads/DB connections.

Microsoft's Competing Consumers and Bulkhead patterns support isolating work and balancing variable load while acknowledging that ordering is not guaranteed and consumers must be idempotent. citeturn751531search1turn633126search11

---

# 42. Retry Architecture

Every remote retry policy defines:

```text
maximum attempts
maximum elapsed time
backoff
jitter
per-tenant budget
global budget
```

No infinite retry.

Retrying a business command reuses the same OperationId.

A connectivity outage must cause clients to transition to offline mode rather than create a retry storm.

Microsoft explicitly warns that uncontrolled rapid retries can worsen an outage and create a thundering herd/retry storm. citeturn633126search14

---

# 43. Bulkhead / Backpressure

Separate resource budgets for:

```text
interactive requests
sync
file transfer
reports
maintenance
outbox
```

If report work becomes excessive:

```text
report throughput decreases
```

not:

```text
checkout stops
```

---

# 44. Near-Real-Time Collaboration

Correctness:

```text
Notification → never required
```

Responsiveness:

```text
Foreground client:
    adaptive short polling / hint mechanism

Optional scale-out:
    SignalR notification adapter
```

Recommended launch behavior:

```text
Foreground active screen:
    target 1–3 s visibility latency,
    randomized jitter

Idle screen:
    5–15 s

Background:
    30–120 s
```

The precise intervals are benchmark parameters.

If SignalR is enabled later, it sends only:

```text
Tenant changed
Scope changed
Latest hint/version
```

not full records.

SignalR is inherently a connection-oriented channel, and Microsoft notes that single-process hosting needs no sticky sessions, while scale-out needs session affinity and a backplane or equivalent service. citeturn751531search2turn751531search5

---

# 45. Notification Failure

If a notification is lost:

```text
No data is lost.
```

The client eventually polls/synchronizes from its checkpoint.

A notification channel can therefore be added, removed, or replaced without altering state correctness.

---

# 46. Web Architecture

Blazor Web App:

```text
Static SSR = default
InteractiveServer = selective
```

Static SSR does not hold a persistent circuit, but still executes server/database work per request. InteractiveServer creates a server-side circuit and therefore has per-session memory/connection implications. Microsoft documents Static as the default render mode in current .NET 10 Blazor Web Apps. citeturn408632search1

Rules:

- never make the whole application InteractiveServer by default;
- large read-heavy screens should prefer static/rendered/paged approaches;
- typeahead/search endpoints are dedicated and debounced;
- interactive islands stay small;
- server-side authorization always remains authoritative.

---

# 47. Desktop Architecture

```text
Avalonia
  ↓
MVVM
  ↓
Shared Application Core
  ↓
Local Repository
  ↓
SQLite/EF Core
  ↓
Sync Coordinator
```

No browser/WebView is in the offline path.

---

# 48. Local Database Concurrency

One local synchronization writer at a time.

EF Core DbContext instances are short-lived and are never used concurrently across threads.

The local sync writer serializes:

```text
checkpoint writes
delta application
mutation settlement
conflict state updates
```

Read queries can execute concurrently using independent contexts.

---

# 49. Local WAL and Encryption

SQLite uses a WAL-compatible configuration where proven by the encryption provider.

The encrypted database must pass:

```text
create
close
reopen
wrong key failure
plain-reader failure
migration
WAL
crash recovery
backup
restore
key rotation
```

The native SQLite binary version is verified at runtime, not inferred from package metadata.

---

# 50. Full Resynchronization

A full resync occurs when:

```text
cursor expired
replica corrupted
contract incompatible
scope changed materially
manual repair requested
integrity validation failed
```

Process:

```text
1. Freeze local replication writes, not business UI
2. Preserve mutation journal
3. Establish a consistent server snapshot
4. Build a Shadow Replica
5. Validate schema/relations/checksums
6. Rebase preserved local mutations where policy allows
7. Move irreconcilable mutations to ConflictInbox
8. Atomically switch shadow → live
9. Re-establish checkpoint
10. Resume normal sync
```

The current local replica remains intact until the shadow replica is proven valid.

---

# 51. Mutation Rebase After Full Resync

Preserved local mutation:

```text
BaseVersion = V10
```

Server snapshot:

```text
V20
```

The engine attempts:

```text
rebase V10 patch onto V20
```

If safe:

```text
PendingSettlement
```

If not:

```text
Conflict / NeedsAttention
```

A resync never deletes a pending mutation merely because the snapshot is newer.

---

# 52. Echo Safety

A client must safely tolerate receiving its own committed state again.

Example:

```text
Client submits M1
Server commits M1
Client receives settlement
Later delta includes the resulting row
```

Second application must not:

```text
duplicate a business operation
reset dirty flags incorrectly
create duplicate audit records
recreate a pending mutation
```

---

# 53. Sync Scope

Each client sync scope is determined by:

```text
Tenant
Device
User/session profile
Role/capabilities
SyncProfileVersion
ContractVersion
```

A scope may include:

```text
entities
columns
row predicates
history depth
file policy
related references
```

Only data needed by the device is replicated.

---

# 54. Permission/Profile Changes

If a user's access scope expands or shrinks materially:

```text
SyncProfileVersion changes
```

and the client may require a controlled resynchronization.

This prevents an old delta cursor from being interpreted as if it covered a new dataset.

---

# 55. Shared Workstation

If multiple people use the same physical workstation:

```text
Each user gets a logically separate encrypted local profile/replica
```

unless a specifically designed shared-device model has been approved.

No user may inherit another user's offline authorization simply because they use the same machine.

---

# 56. Files Architecture

File metadata and binary content are two consistency domains.

Lifecycle:

```text
Staged
  ↓
Uploaded
  ↓
HashVerified
  ↓
Committed
  ↓
Available
```

## 56.1 File transfer

Separate resumable protocol:

```text
BeginUpload
UploadChunk
Resume
Complete
VerifyHash
Commit
```

Transfers are:

```text
idempotent
resumable
hash-verified
cancelable
auditable
```

## 56.2 File identity

Prefer immutable content-addressed objects.

Replacing a file creates:

```text
new object
new metadata version
```

rather than overwriting old bytes.

---

# 57. File Access Security

Use a Valet-Key-like model:

```text
FileId
 ↓
authorization
 ↓
short-lived signed access grant
 ↓
client accesses bytes
```

A filesystem path is never itself an authorization credential.

---

# 58. Reporting Architecture

Reporting is read-only.

Launch approach:

```text
OLTP
+
incremental read projections
```

Examples:

```text
DailySalesSummary
InventorySummary
AccountBalanceSummary
ClinicKpiSummary
```

Heavy reports run under separate resource budgets.

Reporting projections may be eventually consistent and must expose their freshness timestamp/version.

---

# 59. Caching

Cache only when measurement justifies it.

Every tenant-sensitive cache key includes:

```text
Tenant
User/Permission scope
Resource
Query/Contract version
Data version
```

Never cache authorization-sensitive results before authentication/authorization.

ASP.NET Core output caching documentation explicitly warns that output caching must run after authentication/authorization in authenticated applications to avoid serving cached authorized content to the wrong user. citeturn453365search2

---

# 60. Security Architecture

## 60.1 Threat classes

```text
Spoofing
Tampering
Repudiation
Information disclosure
Denial of service
Elevation of privilege
```

## 60.2 Primary mitigations

| Threat | Mitigation |
|---|---|
| Spoofing | local identity + device identity |
| Tampering | TLS, signatures, fingerprints, idempotency |
| Repudiation | immutable audit |
| Disclosure | tenant isolation, authorization, encrypted local store |
| DoS | budgets, bulkheads, throttling, bounded retries |
| Elevation | RBAC + policy + resource authorization |

---

# 61. Audit Architecture

Three layers:

### Application audit
Security/business actions.

### Database audit
Break-glass/maintenance/direct-SQL administrative actions.

### Financial audit
Immutable references tied to accounting operations.

Audit records are append-only.

Security/financial audit batches may be hash-chained for tamper evidence.

Audit is not used to coordinate business processing.

---

# 62. Cryptographic Key Architecture

Runtime node may hold:

```text
server TLS certificate/private key
Data Protection key ring
verification public keys
device-related public verification material
backup encryption runtime artifacts as required
```

Runtime node must not hold:

```text
offline root CA private key
license signing private key
other trust-minting private keys
```

Those belong to protected management custody.

---

# 63. Local PKI

```text
Offline Root CA
      ↓
Issuing CA
      ↓
Server certificate
```

Root private key:

```text
never on runtime server
never in ordinary server backup
```

Clients receive the public trust anchor via deployment tooling, not web JavaScript.

Certificate revocation/validity at runtime must not create an Internet dependency.

---

# 64. Data Protection

ASP.NET Core Data Protection key material is a recovery-critical artifact.

Stable:

```text
ApplicationName
KeyRing location
Protection at rest
ACL
```

must survive restart and restore.

Microsoft documents `PersistKeysToFileSystem` and `SetApplicationName` for persistent, shared key rings. citeturn633126search2turn633126search16

---

# 65. Entitlement and Licensing

Subscription:

```text
commercial state
```

Licensing:

```text
offline runtime entitlement
```

remain separate.

License artifacts are signed by an offline protected signing authority.

The runtime server holds only verification material.

Anti-cloning relies on device-bound protected identity where supported; a software-only fingerprint is not considered strong anti-cloning proof.

---

# 66. Public Website / Management Plane

The public website:

```text
register
choose plan
pay
download
```

does not participate in clinic runtime.

Provisioning crosses a versioned management contract:

```text
Website
  ↓
signed onboarding intent
  ↓
Subscription
  ↓
Tenant provisioning
  ↓
License issuance
```

The production clinic runtime remains functional with the website completely unavailable.

---

# 67. Zero-Internet Runtime

The runtime must continue operating if the Internet is physically disconnected.

Test independently:

```text
DNS unavailable
Internet unavailable
external time sync unavailable
OCSP/CRL external access unavailable
CDN unavailable
package feeds unavailable
telemetry backend unavailable
public website unavailable
```

and verify:

```text
Login
Authorization
Core reads
Business commands
Critical sync
File access
Local observability
Licensing verification according to offline policy
```

still work.

---

# 68. Deployment Topology

Launch:

```text
Vendor infrastructure
        │
        ├── ASP.NET Core application
        ├── SQL Server 2025 Standard
        ├── File Store
        ├── Local certificates/runtime keys
        └── Local observability
```

Clinics:

```text
Desktop/Web
   ↓
central vendor server
```

No clinic operates a server.

---

# 69. Server Hardware Baseline

For capacity certification, use a deliberately modest baseline such as:

```text
4 logical CPU
16 GB RAM
SSD/NVMe
SQL Server 2025 Standard
```

The architecture never claims a user count from hardware alone.

A smaller 2–4 CPU machine may be used in pilot deployments, but production capacity must be measured.

---

# 70. SQL Server Edition Decision

For the **central multi-tenant production service**, SQL Server 2025 Standard is the preferred baseline.

Express remains valid for:

```text
development
pilot
very-small bounded deployments
```

but its SQL Server 2025 limits include at most 4 cores, 1,410 MB buffer pool and 50 GB relational database size. Standard raises the buffer pool ceiling to 256 GB, compute to 32 cores/4 sockets and provides Resource Governor in SQL Server 2025. citeturn633126search1turn633126search10turn633126search3

This is a capacity/cost decision, not a domain architecture dependency.

---

# 71. Noisy Neighbor Isolation

For central SaaS:

```text
Tenant A
```

must not be capable of consuming all:

```text
CPU
memory
DB worker capacity
sync worker capacity
report capacity
file I/O
```

Use:

```text
application rate/concurrency budgets
+
background work quotas
+
SQL Server Resource Governor where justified
```

SQL Server 2025 Standard makes Resource Governor available to control CPU, memory and physical I/O consumption by workload groups. citeturn633126search3turn633126search0

---

# 72. Scale-Out Escape Hatch

The first scale-out dimension is tenant stamps:

```text
Tenant
  ↓
DeploymentStamp
  ↓
Application + DB + FileStore
```

Initially:

```text
all tenants → Stamp 1
```

Later:

```text
Tenant A/B/C → Stamp 1
Tenant D → Stamp 2
```

Domain/application contracts do not change.

---

# 73. Multi-Instance Web

If multiple server instances are introduced and InteractiveServer/SignalR is used:

```text
session affinity
+
shared Data Protection keys
+
SignalR backplane
```

are required as appropriate.

The current single-node deployment does not need Redis.

Microsoft states that single-server SignalR does not require sticky sessions; multi-server deployments generally do, and Redis is the self-hosted scale-out backplane. citeturn751531search2turn751531search5

---

# 74. No Broker at Launch

Launch uses:

```text
Database
+
Outbox
+
BackgroundService
```

rather than Kafka/RabbitMQ/Service Bus.

A broker is introduced only if measurable need demonstrates:

```text
throughput bottleneck
multi-process fan-out
cross-stamp messaging
operational need for external durable queues
```

and requires an ADR.

---

# 75. Disaster Recovery

## 75.1 Server state

Recovery point must tie:

```text
Database
FileStore
Runtime key material
Application build
Schema version
```

together.

Recovery Point Manifest:

```text
BackupSetId
Database backup version
Database recovery point
File store version/watermark
Runtime key version
Application build
Schema version
```

## 75.2 Backup copies

At least:

```text
2 copies
1 off-host
1 immutable
```

Restore tests are mandatory.

A backup file that has never been restored successfully is not evidence of recoverability.

---

# 76. Client Recovery

Server recovery restores:

```text
server truth
```

It cannot restore:

```text
unsent client-only mutation
```

Therefore the desktop policy is:

```text
pending mutations are protected by encrypted local recovery/export
```

where product/security policy permits; otherwise the UI must explicitly warn that a locally committed but unsent operation may be unrecoverable after total device loss.

The system never tells users that “queued” means “stored on server.”

---

# 77. Client Disk Corruption

Procedure:

```text
1. Quarantine local replica
2. Preserve pending mutation journal
3. Authenticate/re-authorize
4. Full shadow resync
5. Rebase pending mutations
6. Restore conflict inbox
7. Atomic switch
```

---

# 78. Backup / Restore and Sync Checkpoint

A restored server may have a Change Tracking version lower than a client believes.

Client must never assume:

```text
server version always moves monotonically across disaster restore
```

without validating the restored server identity/recovery point.

After server recovery:

```text
RecoveryPointId
Database identity
Schema version
Sync feed validity
```

are checked.

Affected clients may be forced into controlled full resync if the restore point invalidates their cursor.

This is safer than trying to fake continuity across an erased history window.

---

# 79. Versioning and Deployment

## 79.1 Server schema

Use expand/contract discipline:

```text
Expand
  ↓
Deploy compatible server
  ↓
Deploy compatible clients
  ↓
Migrate usage
  ↓
Contract/remove
```

Do not immediately remove columns or change semantics required by older supported clients.

## 79.2 Sync contract

Each contract carries:

```text
ContractVersion
```

and server/client capability negotiation.

## 79.3 Supported fleet

Maintain at least:

```text
N
N-1
N-2
```

for normal operations unless an explicit support policy changes.

---

# 80. Migration Safety

A migration is successful only if:

```text
server database
+
existing clients
+
offline replicas
+
pending mutations
```

remain compatible according to the supported window.

---

# 81. Error Handling Contract

No generic:

```text
Sync failed
```

state may be shown for critical commands.

For every failed/conflicted operation, UI/diagnostics must answer:

```text
What failed?
Why?
Does it have a financial/business effect?
What should the user do?
Who must fix it?
```

---

# 82. Observability

Every cross-boundary operation carries:

```text
RequestId
CorrelationId
TenantId
UserId
DeviceId
OperationId
MutationId
AggregateId where applicable
```

## 82.1 Logs

Structured, local, PII-minimized.

## 82.2 Metrics

At minimum:

```text
sync backlog
pending mutation count
settlement latency
conflict rate
full-resync count
cursor-expired count
notification latency
DB latency
query latency
outbox backlog
dead letters
tenant resource usage
```

## 82.3 Health

Separate:

```text
Liveness
Readiness
Dependency health
Sync health
Database health
File-store health
```

## 82.4 Diagnostics bundle

One local operation exports:

```text
application version
schema version
sync state
last checkpoint
pending mutations metadata
recent conflicts
recent errors
health state
configuration snapshot (sanitized)
```

No secrets.

---

# 83. Reporting Freshness

Every read projection used for management reporting exposes:

```text
ProjectionGeneratedAt
SourceVersion / SourceWatermark
```

Users can distinguish:

```text
current transactional truth
```

from:

```text
eventual report projection
```

---

# 84. Performance Architecture

## 84.1 Server

Optimize in this order:

```text
1. Query shape
2. Correct indexes
3. Projection
4. Batch size
5. Connection/DbContext lifetime
6. Caching where safe
7. Background concurrency
8. Compression
9. Runtime-level optimization
10. Additional infrastructure
```

Do not introduce a broker/cache/AOT just because it sounds fast.

## 84.2 Client

Push safe work to desktop:

```text
search
sorting
form validation
cached dashboards
presentation calculations
local filtering
```

Server handles:

```text
authorization
global invariants
settlement
global reporting
final accounting
cross-user conflicts
```

---

# 85. EF Core Performance Rules

Use:

```text
AsNoTracking
explicit projection
pagination
short-lived DbContext
context pooling where measured
compiled queries where measured
batching
appropriate indexes
```

Avoid:

```text
large object graphs
lazy-loading loops
SELECT *
unbounded queries
per-row round trips
N+1 patterns
long transactions
```

---

# 86. Sync Performance

Sync endpoints must:

```text
return only required fields
return only changed identities/rows
page by bounded payload
support cancellation
support resume
avoid entity graph materialization
avoid per-row HTTP requests
```

Suggested target page payload:

```text
256 KB – 1 MB
```

with a row-count ceiling as a second guard.

---

# 87. Adaptive Catch-up

When a client has a large backlog:

```text
Priority 1: business settlement
Priority 2: critical state needed by active screens
Priority 3: routine state
Priority 4: background/reporting
```

Do not make the client perform a giant full-rate catch-up in one burst.

---

# 88. Reconnect Storm Control

Each client uses:

```text
random initial jitter
exponential/adaptive backoff
bounded retry
server overload signals
per-tenant concurrency limit
global concurrency limit
```

A mass reconnect must not turn into a synchronized request storm.

---

# 89. Database Performance / Query Governance

SQL Server Query Store should be part of operational performance diagnosis.

Investigate:

```text
p95/p99 latency
CPU
logical reads
query plan regressions
lock waits
deadlocks
tempdb pressure
version store pressure
transaction log pressure
```

---

# 90. Snapshot/Version-Store Discipline

Critical sync uses snapshot isolation.

Therefore:

```text
snapshot transactions must be short
```

except the exceptional controlled full-snapshot operation.

Long-lived transactions are monitored because SQL Server documentation notes that open snapshot transactions can delay safe cleanup of change-tracking/versioned data. citeturn751531search0turn453365search8

Full initial syncs are throttled and measured.

---

# 91. Data Retention

Each domain defines:

```text
operational retention
audit retention
financial retention
sync tombstone retention
file retention
diagnostic retention
```

Retention policies must never cause a supported offline client to miss a required deletion/revocation.

---

# 92. Security of Local Data

Desktop local storage contains sensitive tenant data.

Therefore:

```text
encrypted DB
OS-protected key
filesystem ACL
locked credentials
device identity
short offline sensitive-operation windows
```

are required.

The encrypted database must fail closed when the key is unavailable or invalid.

---

# 93. Filesystem / Path Security

Filesystem paths never cross the client/server trust boundary as credentials.

A file reference is:

```text
logical FileId
```

not:

```text
C:\...
\\server\...
https://internal-path/...
```

---

# 94. Domain Event vs Replication Event

Example:

```text
InvoicePosted
```

is a domain event.

The same transaction also changes rows that Change Tracking later exposes.

The client may receive:

```text
Invoice projection changed
```

through critical state sync.

It does not receive the internal domain event unless a separately defined contract explicitly requires it.

---

# 95. Business Commands Must Be Small and Semantic

Prefer:

```text
RegisterPayment
```

over:

```text
UpdatePayment
```

Prefer:

```text
AdjustStock
```

over:

```text
SetQuantity
```

Prefer:

```text
BookAppointment
```

over:

```text
UpdateAppointmentSlot
```

The command should say **what the user intends**, not merely which rows to mutate.

---

# 96. Query Architecture

Queries are read-only.

For high-value screens:

```text
dedicated query DTO
```

rather than domain aggregate serialization.

Queries are authorized before data access.

---

# 97. API Surface Separation

```text
/business/*
    semantic commands/queries

/sync/*
    critical state synchronization

/tables/*
    Datasync compatibility surface only

/notifications/*
    optional hint transport

/operator/*
    management-plane operations
```

The actual route names can differ; the separation of semantics cannot.

---

# 98. OData Boundary

OData is a Datasync implementation detail only.

No business client is allowed to depend on arbitrary OData semantics.

Business clients depend on:

```text
typed contract
versioned DTO
application-owned query/command semantics
```

---

# 99. API Security

Every request:

```text
TLS
→ Authentication
→ tenant resolution
→ authorization
→ entitlement
→ request validation
→ business/application action
```

No endpoint infers tenant from:

```text
URL alone
body
query string alone
local client state
```

---

# 100. Validation

Validation has three levels:

### Client validation
UX and early feedback.

### Application validation
Request/command completeness.

### Domain validation
Business invariants.

Server domain validation is authoritative.

---

# 101. Idempotency

Idempotency applies to:

```text
business commands
onboarding
file completion
outbox consumers
maintenance operations where repeated submission is possible
```

It does not mean “everything is exactly once”.

The delivery semantics remain at-least-once.

---

# 102. Ordering

Ordering is explicit.

Possible ordering keys:

```text
Tenant + AggregateId
Tenant + ResourceId
Tenant + CashSessionId
```

Do not globally serialize the entire tenant if independent aggregates can safely proceed in parallel.

---

# 103. Allocation vs Row Concurrency

`rowversion` detects stale entity edits.

It does not prove:

```text
two clients cannot claim same slot
```

For a contended resource:

```text
transactional allocation constraint
```

is required.

---

# 104. Audit Tamper Evidence

For high-value audit, use:

```text
AuditBatch
PreviousBatchHash
CurrentBatchHash
Signature / KeyId where required
```

This is an audit-integrity control, not an execution mechanism.

---

# 105. Operational Maintenance

Maintenance tasks include:

```text
Change Tracking cleanup monitoring
outbox retry/dead-letter monitoring
file reconciliation
database integrity checks
backup verification
projection rebuild
orphan detection
sync diagnostics
license refresh
```

Every maintenance task has:

```text
scope
tenant context
authorization
rate limit
audit
cancelation
resume semantics
```

---

# 106. Dead Letter Handling

A dead letter is not a “trash can”.

It must include:

```text
event/operation identity
tenant
aggregate
attempt count
first failure
last failure
error code
error detail (sanitized)
payload fingerprint
operator remediation state
```

Reprocessing requires explicit operator action when appropriate.

---

# 107. Failure Semantics

Every subsystem has a defined outcome.

Examples:

### Database unavailable
```text
online mutations fail fast
desktop continues offline where policy permits
```

### Sync unavailable
```text
desktop remains locally usable
pending mutations remain durable
```

### Authorization changed
```text
capability refresh
scope update
controlled resync as needed
```

### Cursor expired
```text
full shadow resync
```

### File store unavailable
```text
file operation becomes Pending/Unavailable
metadata does not falsely say Available
```

---

# 108. Chaos / Fault Injection

The architecture must be tested with injected failures:

```text
process crash
database deadlock
transaction rollback
network drop
timeout
lost response
duplicate response
duplicate request
out-of-order request
corrupt local DB
corrupt file
permission change mid-flight
server restart
full node restore
clock anomaly
notification loss
```

---

# 109. Architecture Validation Gates

## Gate A — Patient conflict
Two offline clients edit same record; deterministic policy; no lost field.

## Gate B — Independent financial commands
Two offline commands both settle once.

## Gate C — Duplicate command
Same OperationId twice → one effect.

## Gate D — Lost response
Commit + lost response + retry → one effect, original result.

## Gate E — Cross-tenant access
Tenant A cannot read Tenant B by any supported identifier.

## Gate F — Critical delta correctness
Change Tracking pull under concurrent writes/paging/crash/retry → no unrecoverable skip.

## Gate G — Server crash during settlement
Business transaction and outbox remain consistent.

## Gate H — Desktop crash
No mutation loss, no double application.

## Gate I — Reconnect storm
Large simultaneous reconnect stays within resource budget.

## Gate J — RLS hardening
No leak under pooled connections and parallel plans.

## Gate K — Local encryption
Wrong key fails; crash/WAL/rekey/restore pass.

## Gate L — Cache isolation
No cross-tenant/user cache leakage.

## Gate M — Mixed version
N/N-1/N-2 protocol compatibility.

## Gate N — Sync failure UX
Every critical failure answers what/why/effect/action.

## Gate O — Cross-pipeline dependency
State mutation → command → command dependency order deterministic.

## Gate P — Idempotency fingerprint
Same ID + same fingerprint is replay; same ID + different fingerprint is integrity error.

## Gate Q — Offline capability
Expiry/revocation/policy change behaves correctly.

## Gate R — File lifecycle
No Available state before verified bytes.

## Gate S — Period close
Offline command across close never posts silently to wrong period.

## Gate T — Contended allocation
Exactly one booking/resource claim wins where policy says unique.

## Gate U — Client disk loss
Pending local work follows declared recovery policy.

## Gate V — Delete resurrection
Offline edit against server-deleted entity is explicit conflict.

## Gate W — Initial snapshot
Full sync is referentially consistent.

## Gate X — Restore
DB/files/key material restore to one Recovery Point Manifest within RTO/RPO.

## Gate Y — Direct SQL state change
Governed direct SQL mutation becomes sync-visible.

## Gate Z — Direct SQL business protection
Critical financial/business invariants cannot be silently bypassed by ordinary SQL.

## Gate AA — Echo safety
Own committed change replay is idempotent.

## Gate AB — Notification independence
Lost notification never causes permanent missed sync.

## Gate AC — Tenant QoS
No single tenant can consume all server resources.

## Gate AD — Cursor expiration
Expired Change Tracking cursor always triggers correct full resync.

## Gate AE — Full resync safety
Shadow replica rebuild cannot destroy the last good local replica.

---

# 110. Capacity Test Method

The published capacity number must come from measurement.

At minimum test:

```text
25 tenants
50 tenants
100 tenants
250 tenants
```

with a realistic user distribution.

Measure:

```text
p50/p95/p99 API latency
p50/p95/p99 settlement latency
DB CPU
server CPU
memory
disk I/O
transaction log throughput
Change Tracking backlog
sync MB/s
sync rows/s
reconnect catch-up time
outbox backlog
deadlocks
locks/waits
```

The benchmark workload must include:

```text
reads
writes
sync
reports
files
reconnect
background maintenance
```

not only API microbenchmarks.

---

# 111. Security Testing

Required:

```text
tenant ID manipulation
IDOR
cross-tenant synchronization
stolen device
expired capability
replayed command
same ID/different fingerprint
malformed patch
oversized payload
file path traversal
file grant forgery
CSRF
session fixation
token replay
cache confusion
SQL injection
OData abuse on the Datasync boundary
```

---

# 112. Supply-Chain / Deployment Rules

The architecture depends on the Stack’s pinning/SBOM policies.

Every production build must record:

```text
application build
.NET runtime version
EF Core exact line
Datasync exact version
native SQLite binary version
RID
OS-native dependencies
schema version
sync contract version
license verification-key version
```

The release manifest must contain native artifacts as well as managed dependencies.

---

# 113. Upgrade Strategy

## Server

Patch-level upgrades:

```text
test
backup
apply
health-check
rollback plan
```

Major technology changes require dependency review.

## Desktop

Updates are:

```text
signed
verifiable
rollback-capable
offline-installable
```

Mac builds are:

```text
signed
notarized
stapled
```

before release.

No runtime dependence on Internet-hosted update feeds.

---

# 114. Deployment Plan

```text
Build
 ↓
Static/security analysis
 ↓
Unit tests
 ↓
Integration tests
 ↓
Sync/chaos suite
 ↓
Database migration rehearsal
 ↓
Backup/restore rehearsal
 ↓
Mixed-client compatibility
 ↓
Capacity benchmark
 ↓
Release approval
 ↓
Production
```

---

# 115. Production Readiness Criteria

The system is not declared GA-ready until:

```text
all Critical Gates pass
no unresolved critical security finding
no silent-data-loss defect
no unresolved sync cursor correctness issue
backup restore proven
native encryption proven
tenant isolation proven
mixed-version contract proven
documented RPO/RTO met
capacity budget met
offline window policy proven
```

---

# 116. Architecture Decision Log

| ADR | Decision | Status |
|---|---|---|
| AD-01 | Modular Monolith at launch | Accepted |
| AD-02 | State replication and business intent remain separate | Accepted |
| AD-03 | Server is single global authority | Accepted |
| AD-04 | Split desktop/web presentation, shared core | Accepted |
| AD-05 | Shared DB multi-tenancy | Accepted |
| AD-06 | Application isolation + RLS hardening | Accepted |
| AD-07 | AuthN/AuthZ/Entitlement separation | Accepted |
| AD-08 | Capability-based offline session | Accepted |
| AD-09 | Append-only ledger | Accepted |
| AD-10 | Movement-based inventory | Accepted |
| AD-11 | Idempotent command settlement | Accepted |
| AD-12 | Transactional Outbox | Accepted |
| AD-13 | Local observability, no mandatory external telemetry | Accepted |
| AD-14 | Local PKI | Accepted |
| AD-15 | Subscription ≠ Licensing | Accepted |
| AD-16 | Offline revocation = next contact | Accepted |
| AD-17 | Retention linked to offline window | Accepted |
| AD-18 | Critical ChangeSequence implemented by SQL Change Tracking | **Accepted — replaces previous “custom sequence only” baseline** |
| AD-19 | Inventory concurrency policy | Accepted subject to domain ADR |
| AD-20 | Scheduling allocation model | Accepted |
| AD-21 | Ledger/period-close policy | Accepted |
| AD-22 | Offline root CA / runtime key separation | Accepted |
| AD-23 | License-signing key offline custody | Accepted |
| AD-24 | Management-plane onboarding contract | Accepted |
| AD-25 | Backup/RTO/RPO/Recovery Manifest | Accepted |
| AD-26 | Tamper-evident security/financial audit | Accepted |
| AD-27 | Client disk-loss policy | Accepted |
| AD-28 | Critical Sync Engine is custom and Change-Tracking-backed | Accepted |
| AD-29 | Datasync restricted to low-risk/simple replication | Accepted |
| AD-30 | Client-local Mutation Journal owned by Animora | Accepted |
| AD-31 | Cross-pipeline causal dependency DAG | Accepted |
| AD-32 | Patch-based state mutations where supported | Accepted |
| AD-33 | Direct SQL mutation governance | Accepted |
| AD-34 | Production central SQL Server baseline = Standard | Accepted |
| AD-35 | Desktop auth uses stack-approved local Identity token mode; no PKCE claim | Accepted |
| AD-36 | Notification channel is non-authoritative | Accepted |
| AD-37 | Sync scope versioning | Accepted |
| AD-38 | Full resync uses shadow replica | Accepted |
| AD-39 | Full resync never deletes unsent mutations | Accepted |
| AD-40 | Financial state never enters generic CRUD settlement | Accepted |

---

# 117. Architecture Rules for AI Agents

```text
A-RULE 001  Domain code never depends directly on infrastructure.
A-RULE 002  Domain code never depends directly on Datasync.
A-RULE 003  Critical synchronization uses Animora Sync Engine.
A-RULE 004  Datasync is permitted only for explicitly classified SYNC-SIMPLE entities.
A-RULE 005  Financial posted facts are immutable.
A-RULE 006  Corrections to posted finance are compensating/reversal operations.
A-RULE 007  Inventory is movement-based; no stale absolute stock overwrite.
A-RULE 008  Shared-resource allocation is transactional.
A-RULE 009  Every business mutation carries a stable OperationId.
A-RULE 010  Same OperationId + same fingerprint = replay.
A-RULE 011  Same OperationId + different fingerprint = integrity error.
A-RULE 012  Server is final authority for settlement.
A-RULE 013  Client may create provisional local state offline.
A-RULE 014  Client-local financial state is never labeled globally posted.
A-RULE 015  TenantId is resolved server-side only.
A-RULE 016  Tenant context is immutable during a request/work item.
A-RULE 017  Cross-tenant identifiers never bypass authorization.
A-RULE 018  Authorization is re-evaluated server-side.
A-RULE 019  Entitlement is separate from authorization.
A-RULE 020  Notification is never the authoritative data channel.
A-RULE 021  Critical cursor advances only after durable local apply.
A-RULE 022  Sync is at-least-once and local apply is idempotent.
A-RULE 023  Full resync never deletes Animora-owned unsent mutations.
A-RULE 024  Full resync is built in a shadow replica.
A-RULE 025  Change Tracking version is synchronization position, not business sequence.
A-RULE 026  rowversion is concurrency metadata, not synchronization cursor.
A-RULE 027  UpdatedAt is not a critical synchronization cursor.
A-RULE 028  Client wall-clock time is never a synchronization authority.
A-RULE 029  CausationId and DependsOnOperationId remain distinct.
A-RULE 030  Dependency graph is a DAG.
A-RULE 031  No dependent operation settles before its prerequisite settles.
A-RULE 032  Critical direct SQL business mutation is forbidden outside governed maintenance.
A-RULE 033  Direct SQL state mutation on governed sync tables must remain Change-Tracking-visible.
A-RULE 034  Hard-delete of synchronized entities is forbidden in ordinary production paths.
A-RULE 035  Tombstone retention is linked to maximum offline duration.
A-RULE 036  Change Tracking retention is linked to maximum offline duration.
A-RULE 037  Expired Change Tracking cursor causes controlled full resync.
A-RULE 038  Database Change Tracking is not a substitute for domain events.
A-RULE 039  Outbox is not a substitute for state synchronization.
A-RULE 040  Every at-least-once async consumer is idempotent.
A-RULE 041  Every retry has a bounded retry budget.
A-RULE 042  Background work cannot exhaust interactive capacity.
A-RULE 043  Tenant resource budgets are separate from global budgets.
A-RULE 044  A client does not trust its own authorization decision.
A-RULE 045  Cached tenant-sensitive results include tenant and permission scope.
A-RULE 046  File path is never an authorization credential.
A-RULE 047  File availability requires verified bytes.
A-RULE 048  Runtime trust-minting private keys never live on the production node.
A-RULE 049  Public website is never a clinic runtime dependency.
A-RULE 050  Runtime operation continues without external Internet.
A-RULE 051  Sync profile changes may require controlled resynchronization.
A-RULE 052  Shared workstation user profiles must not share offline authority implicitly.
A-RULE 053  Notification loss never causes permanent data loss.
A-RULE 054  Echoed server state is applied idempotently.
A-RULE 055  A stale write is detected, not silently overwritten.
A-RULE 056  Version mismatch never decides business conflict by itself.
A-RULE 057  Automatic merge requires provable non-overlap.
A-RULE 058  If conflict evidence is insufficient, choose safety over automatic merge.
A-RULE 059  Query/reporting code never writes transactional state.
A-RULE 060  Every architecture boundary has a validation gate.
```

---

# 118. Distinctions That Must Never Be Collapsed

```text
Server business truth              ≠ client provisional truth
Critical sync                     ≠ Datasync CRUD
State mutation                    ≠ business command
Change Tracking                   ≠ Outbox
ChangeSequence                    ≠ rowversion
rowversion                        ≠ OperationId
OperationId                       ≠ DatasyncOperationId
CausationId                       ≠ DependsOnOperationId
Notification                      ≠ Sync
At-least-once                     ≠ exactly-once transport
Idempotency                       ≠ ordering
Concurrency detection              ≠ conflict resolution
RBAC                              ≠ resource authorization
Authentication                    ≠ authorization
Authorization                     ≠ entitlement
Subscription                      ≠ license
License signature                 ≠ anti-cloning proof
Offline revocation                ≠ immediate revocation
Local commit                      ≠ server settlement
Draft                             ≠ posted
Quantity projection               ≠ stock movement
Audit                             ≠ coordination
Read projection                   ≠ write truth
Tenant filter                     ≠ full security boundary
RLS                               ≠ application authorization
Backup exists                     ≠ restore proven
Notification received             ≠ data synchronized
```

---

# 119. Pattern Catalog and Standards Basis

## Structural

- C4 Model — Simon Brown
- arc42
- DDD bounded contexts — Eric Evans lineage
- Clean/Onion Architecture
- Ports & Adapters / Hexagonal Architecture

## Microsoft / Azure Architecture Center patterns used deliberately

- Transactional Outbox
- Idempotent Consumer
- Competing Consumers
- Queue-Based Load Leveling
- Bulkhead
- Retry
- Throttling
- Cache-Aside
- Valet Key
- Deployment Stamps
- Minimize coordination

Microsoft's current documentation emphasizes idempotency under at-least-once processing, dead-letter handling for poison work, and bounded load leveling for bursty workloads. citeturn751531search1turn751531search3

## Microsoft platform/security guidance

- SQL Server Change Tracking
- SQL Server RLS / `SESSION_CONTEXT`
- EF Core named query filters
- ASP.NET Core authentication/authorization
- ASP.NET Core antiforgery
- ASP.NET Core Data Protection
- Blazor render modes
- SignalR scaling
- SQL Server Resource Governor

---

# 120. Non-Goals

- Microservices at launch.
- External message broker at launch.
- Redis at launch.
- External cloud identity at runtime.
- External telemetry dependency at runtime.
- Browser offline web client.
- Generic CRDT semantics for financial/business data.
- Global LWW conflict resolution.
- Generic distributed transaction coordinator.
- Treating Datasync as the source of global business truth.
- Per-tenant database at launch.
- Arbitrary DBA SQL against financial invariants.
- Exactly-once network delivery.
- Instant remote revocation of a physically disconnected device.

---

# 121. Final Architecture Statement

Animora is a:

> **Vendor-operated, single-node, modular-monolith, multi-tenant, offline-first veterinary accounting and clinic-management platform in which the server is the sole global business authority, desktop clients maintain encrypted local replicas and durable provisional intent, critical state synchronization is driven by SQL Server Change Tracking through an Animora-owned Sync Engine, low-risk simple replication may use CommunityToolkit.Datasync behind an adapter, business effects settle through idempotent semantic commands, finance is append-only, inventory is movement-based, scheduling uses transactional allocation, authorization is server-authoritative, tenant isolation is defense-in-depth, notifications are hints rather than truth, runtime operation has no mandatory external Internet dependency, and every failure mode has an explicit deterministic outcome and validation gate.**

The architecture is considered **10/10 only when the gates in this document pass on the pinned production stack and the published RPO/RTO/offline/capacity targets are demonstrated under realistic fault and concurrency workloads.**
