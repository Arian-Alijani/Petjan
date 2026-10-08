# Phase D08 — PostgreSQL critical state replication surface

**Band:** M · **Track:** D · **Depends on:** D05, D07 · **Satisfies:** R-B03, R-B11, R-B31
**Runnable after this phase:** desktop offline yes · server required no (client implementation E03)

> Filename retained for existing roadmap links; this phase implements the custom journal protocol,
> NOT a Datasync universal table surface. Server and sync do not exist before their owning phases.

## Read before starting
- `ARCHITECTURE.md` §8, §9, §11.4.1, §16-CT–§18-CT, §PG (binding implementation contract).
- `STACK.md` §4, §6, §11–§12; `backend-roadmap/CONVENTIONS.md` §1–§9, §11–§12.
- `backend-roadmap/PROGRESS.md` current state and D02/D04/D05 entries; live domain models,
  `ServerModel`, `ITenantContext`, `EntityVersionCodec`, `WireContract`.
- `A-RULE 001`, `A-RULE 009`, `A-RULE 010`, `A-RULE 014`, `A-RULE 022`, `T-RULE 002`, `T-RULE 007`.

## Goal
PostgreSQL DML changes become a commit-safe tenant-local journal. Authorized HTTP clients receive
frozen multi-entity batches with protected continuation, deterministic versions, retained deletion
keys and recovery identity. State replication remains separate from command settlement.

## Design decisions (binding)
- Launch installs NO Datasync/NSwag dependency or monotonic timestamp service. First-party ASP.NET
  OpenAPI describes `/sync/*`; optional SYNC-SIMPLE adapters are deferred/qualified separately.
- Flat `<Entity>SyncContract` DTOs carry Id, DB UTC UpdatedAt (display), canonical opaque UUID
  Version, Deleted, ContractVersion and allowed payload fields. No TenantId or credential fields
  are serialized. TenantId remains in internal query projections, never inferred from payload.
  Explicit Select/mappers, no EF entity/AutoMapper/reflection hot path.
- **Replicated base-entity set plus two read-model projections (binding by manifest).** Every
  catch-up/policy/test count MUST derive from `SyncContractCatalog.ProjectionOrder`, never a
  magic total in E03/E04/E06/E08. Derived from `ServerModel.ReplicatedEntities`
  (D02) minus everything a client must never hold or must obtain only through the command pipeline:
  | Replicated (client needs local reads of these) | Why |
  |---|---|
  | `Owner`, `Pet`, `WeightEntry`, `DocumentReference` | patient directory, offline reads |
  | `Visit`, `VaccinationRecord`, `MedicalHistoryEntry` | clinical history |
  | `TreatmentPlan`, `TreatmentOccurrence`, `VaccineCatalogItem` | treatment plans + vaccine catalog (post-C11, migration `031`) — **read-only**, clinical history like `VaccinationRecord` |
  | `Appointment`, `ReceptionQueueEntry` | calendar + queue |
  | `Room`, `BoardingServiceDef`, `BoardingStay` | boarding board |
  | `UnitOfMeasure`, `ProductCategory`, `Product` | catalog / POS lookups |
  | `Supplier`, `Insurer`, `LedgerAccount`, `AccountingPeriod` | reference data |
  | `Tenant`, `RoleDefinition`, `StaffMembership` | tenant + role display |
  | **Not replicated** | **Why** |
  | `Invoice`, `Payment`, `CashSession`, `JournalEntry`, `Cheque`, `Expense`, `RecurringExpense`, `RecurringExpensePayment`, `StockMovement`, `PurchaseOrder`, `GoodsReceipt` | financial/stock facts are **settled by command**, not replicated as state (R-B13); the client learns them from its own settled commands and from server-computed read models |
  | `User`, `Permission`, `Device`, Identity tables | credentials/authorization material never leaves the server as replicable rows |
  | `IdempotencyRecords`, `ServerAuditEntries`, `StockOnHandProjections`, `ServerBootstrapState`, `TenantSettings` | server-internal (D02/D05/D07 lists) |
  **DECISION (2026-10-07, closes Phase-Problem §6.1):** the two aggregates that post-date C01–C09 are
  classified by the same rule as their siblings — `TreatmentPlan`/`TreatmentOccurrence`/`VaccineCatalogItem`
  are clinical history ⇒ **replicated, read-only** (writes only via `vaccination.treatment.*` /
  `vaccination.catalog.add` commands, D06); `RecurringExpense`/`RecurringExpensePayment` are financial
  facts ⇒ **not replicated** (settled by `finance.recurring.*` commands, D07, exactly like `Expense`).
  Only 3 entities are client-writable; all others, including the two finance projections below,
  are read-only. Total is derived from the manifest (do not preserve the older base-only count). Client-local columns that have no wire
  counterpart — e.g. E05's `MedicalHistoryEntry.IsConflictPreservedCopy` — are **excluded from every
  `<Entity>SyncContract`** and are never pushed or pulled; the `MedicalHistoryEntry` contract carries
  no such field, so the entity stays purely read-only on the wire. **Asserted, not just documented (audit 2026-10-08 · SYNC-H-01):**
  `SyncSurfaceTests` uses reflection to assert the `MedicalHistoryEntrySyncContract` has **no** `IsConflictPreservedCopy` member, and a
  `ReplicaProjector` test asserts a preserved-copy row is never pushed back to the server.
  This split **is** the "replication ≠ intent" line drawn in data. `SyncSurfaceTests` asserts the
  two sets exactly, so no later phase can quietly replicate a financial table.
  **DECISION — cross-device financial read models (audit 2026-10-08 · SYNC-C-05, SYNC-H-02, IMP-03;
  Option A chosen).** Financial *aggregates* (`Invoice`/`Payment`/`StockMovement`/…) stay
  **not-replicated by state** (command-settled) — correct for *writes*. But that leaves a correctness
  hole for *reads*: device B never learns device A's posted invoice, so C09/C10's
  `OwnerFinancialSnapshot.TotalSpendMinor` and the dashboard revenue (built from the **local** ledger =
  this device's commands only) **disagree across devices**. To close it, a **third, distinct class** is
  added to the replicated surface: **read-only, server-computed finance read-model projections**
  `DailyFinanceFact` and `OwnerFinancialSnapshot`, exposed as **read-only** `<Entity>SyncContract`s so
  every device converges on the server's numbers. These are NOT the financial aggregates (which remain
  command-settled); they are server-authoritative *projections* the client only reads and never writes.
  `SyncSurfaceTests` is extended to assert this third set is read-only and contains exactly these two
  projections. These two are already classified as replicated read-models (not server-internal);
  include them in the manifest/capture coverage before exposing this surface. Convergence is asserted in
  the E-track (see E04/E05 acceptance: two devices reach the same `OwnerFinancialSnapshot.TotalSpendMinor`
  after sync). *Rejected alternative (Option B): leave finance reads server-online-only and degrade
  those widgets to "requires connection" offline — rejected because the desktop is offline-first and
  silently-wrong dashboards are worse than a visible staleness marker.*

- Manifest is `SyncContractCatalog`: stable entity code, DTO version, field allowlist, dependency
  ProjectionOrder, underlying table/root-child capture set, writable policy and schema fingerprint.
  Counts derive from the live manifest; finance projections are read-only and included in it.
- Default-deny writes: only Owner/Pet/WeightEntry may use guarded state mutation endpoints;
  clinical/finance/stock/identity changes use business commands. Mutation envelope contains stable
  MutationId, expected Version, protected base evidence, patch and fingerprint. Receipt uniqueness
  `(TenantId,MutationId)` and conditional patch/effects/audit/journal commit atomically; replay with
  different fingerprint is rejected. Reuse receipt primitives, NOT financial operation identity.
- D02 Version triggers remain. Migration `106_SyncJournal` (verify next free server ordinal) adds
  logged `sync.ChangeJournal`, `sync.IdentityGraveyard`, batch manifests/pages/build leases,
  trigger registry and DML capture functions. Feed state provisioning is D02. Capture EVERY INSERT/
  UPDATE/DELETE on replicated tables plus child→root invalidation, including finance projection
  writers. No journal/outbox/auth/batch self-capture. No key reuse or tenant reassignment.
- Tenant counter update and journal append are in the same DML transaction. Tenant gate spans
  COMMIT; all writers/maintenance acquire it BEFORE business locks. No nextval/identity/xmin/LSN/
  UpdatedAt allocation for cursor. Missing state/trigger coverage fails readiness.
- API: `POST /sync/batches` for delta from bound checkpoint; `POST /sync/snapshots` for controlled
  full snapshot; `GET /sync/batches/{batchId}/pages/{ordinal}`; guarded
  `POST /sync/mutations` with default-deny catalog; metadata exposes supported scope/contract,
  epoch/profile/generation without raw journal rows. Each route uses authN/authZ/entitlement,
  tenant/device/scope binding and the D13 `tables` admission policy (name retained).
- Export uses one bounded REPEATABLE READ transaction for floor/current/keys/rows/dependencies
  and persisted pages/manifest. Deduplicate latest journal keys; read canonical state in the
  SAME snapshot, authorized tenant predicates before serialization including OLD deletion keys.
  Full snapshots read all base rows, not retained keys alone. Disable only SoftDelete filter.
  One reader at a time; keyset chunks fully consumed before writes. No retry buffering.
- Page default 50 items (max 200), 256 KiB serialized; initial whole export limits 10,000 items,
  16 MiB, 15 s, one export in flight. Ready batch TTL initially 15 min, Building lease 30 s;
  restart expired builds and GC boundedly. Oversized scope/item returns controlled SyncBusy/
  capacity action, never truncation/partial completeness. Bigger full scopes need reviewed export
  capacity before tenant admission. Immutable pages survive process restart.
- From/To sequence decimal strings; epoch + tenant feed generation + scope/profile/contract +
  principal/device bind every checkpoint/page. Missing config ⇒ FeedUnavailable; floor breach ⇒
  CursorExpired; future checkpoint ⇒ InvalidCheckpoint; scope/epoch mismatch ⇒ controlled recovery.
  Hash/ordinal/count validation and exact FromVersion CAS govern local application (E03).
- Current permission checks run on EVERY page; revoked profiles invalidate batches. Mutable
  visibility requires RemoveFromScope transition records or profile bump+rebuild, including
  timer/join-only changes. Never suppress canonical state based on origin device.
- Contract header `X-Animora-Contract-Version` supported N/N-1/N-2 is echoed. Unsupported low/high
  version gets deterministic 409 actions; absent header maps minimum. Serve D05 negotiated WireContract v2 (ExpectedServerVersion addition), with explicit V1
  compatibility; never assume no wire change merely because no server is deployed (§PG.7).
- Retention floor/GC follows §18-CT: >=offline window+15 days, committed-observation marker,
  contiguous prefix delete and floor update under the tenant gate in ONE transaction. No automatic
  dropping history under disk pressure. Graveyard retains used identities; receipts and batch TTL
  have independent horizons. Recovery/DDL/TRUNCATE/feed reset installs fresh epoch/schema.
- `/health/ready` validates manifest/schema/fingerprint, logged journal/batches, indexes, enabled
  triggers and feed state; no expensive full export or pg_amcheck on each probe.

## Files to create / modify
| Path | Action | Purpose |
|---|---|---|
| `src/Animora.Contracts.Wire/Sync/*` | create | contracts/catalog, checkpoint/batch/page/mutation envelopes |
| `src/Animora.Persistence.PostgreSql/Sync/*` | create | journal extraction, batch store, explicit mappings, state mutation receipts, trigger registry |
| `src/Animora.Persistence.PostgreSql/Migrations/*_SyncJournal*` | create | logged feed/graveyard/batch schema, capture triggers and indexes |
| `src/Animora.Server/Sync/*` | create | authenticated endpoints, default-deny mutation policy, contract gate |
| `src/Animora.Server/Program.cs` | modify | critical sync DI, OpenAPI, readiness (NO AddDatasyncServices) |
| `tests/Animora.Server.Tests/Sync*Tests.cs` | create | journal/commit/snapshot/GC/tenant/default-deny/contract tests |
| `tests/Animora.Domain.Tests/LayeringRulesTests.cs` | modify | wire/ports contain no Npgsql/EF/OData types |

## Implementation steps
1. Derive catalog including post-C11 clinical and finance read projections from live models.
2. Define DTO/checkpoint/mutation shapes and compatibility tests; canonical UUID codec reuse.
3. Add reviewed migration/capture functions and root invalidations; trigger coverage assertion.
4. Implement gate-first mutation CAS/receipt/audit, direct-DML governance and hard-delete graveyard.
5. Implement snapshot materialization + protected batch paging, scope transitions and budget admission.
6. Implement committed-observation/contiguous-prefix GC and generation recovery; validate readiness.
7. Wire first-party OpenAPI/contract/authorization and write mandatory real-PostgreSQL tests.
8. Build 0/0, tests green and offline desktop smoke; missing DB is a local skip only, CI failure.

## Acceptance criteria (release-blocking, real PostgreSQL 18)
- Two same-tenant writers: T1 allocates then delays, T2 cannot commit a higher position first;
  after T1 COMMIT/ROLLBACK all changes eventually deliver. Different tenants progress independently.
- Rollback/savepoint cancels state, UUID token, journal/counter and receipt together; no phantom delta.
- Direct DML, soft/hard delete and owned-child-only update are captured; disabled/missing capture,
  foreign-parent FK, identity reuse and tenant mutation fail. Internal/batch writes never self-feed.
- Real same-snapshot floor/watermark/keys/payload stays stable through concurrent updates/GC;
  frozen pages are repeatable across restart; missing/corrupt/expired pages never complete.
- Boundary floor/current/negative/future/wrong epoch/scope/contract cases; full snapshot includes
  unchanged rows; empty complete batch may advance only THIS tenant checkpoint.
- Metadata/tombstones/continuation leakage tests with two tenants, RLS OFF and optional ON; unknown
  or read-only entity mutations denied; payload cannot forge tenant/version/time/authority.
- Manifest/root capture and finance read-model convergence checked from catalog, not literal counts.
- GC crash atomicity, recently committed long writer retention, scope exit/timer transitions,
  epoch reset + counter overtaking old cursor, finite rows/bytes/time/RSS budgets all tested.
- `WireContract` mixed versions/header echo; no SQL provider/server persistence reference in client.
- Record reproducible build/tests/smoke; no successful release if DB tests were skipped.

## Out of scope
Client staging/atomic projection/shadow generation → E03; orchestration → E04; conflict decisions →
E05; command HTTP transport → E06; credential refresh → E07; full load/restore chaos gates → E08/E09.
Optional Datasync/OData views are deferred and never replace launch correctness.

## PROGRESS.md update
Only after implementation/acceptance mark D08 done and advance D09. Record manifest, trigger schema,
commit-safe counter, mutations/receipt policy, batch endpoints/budgets, retained floor/graveyard,
read-only finance projections, contract decision and real PostgreSQL evidence for E03/E08.

## Version seam gate
Read ARCHITECTURE §PG.7: existing Domain Version is string?, existing V1 command expected revision
is long?. D05 versions ExpectedServerVersion as V2; canonical UUID codec/converter, captured
server-base evidence and V1 pending-journal compatibility must pass before shipping this phase.
