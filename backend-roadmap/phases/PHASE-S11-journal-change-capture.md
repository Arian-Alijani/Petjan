# Phase S11 — Commit-safe tenant journal & change capture (PostgreSQL critical-state feed, write path)

**Band:** M · **Track:** S (server) · **Depends on:** S06, S10 · **Next:** S12
**Satisfies:** R-B03, R-B11, R-B13 · **Runnable after this phase:** desktop offline yes · server required no
**Produced by:** M00 (regenerated 2026-10-08; regenerates scope-inventory row D08 (half 1; Track-D/E planning files deleted in the 2026-10-08 server-track reset)).

> Baseline verified 2026-10-08: client migration head `031` (`031_TreatmentPlans`), `CommandTypes` = live `CommandTypes.cs` (88 declared today — never freeze), `ErrorCodes` 161, `WireContract.Version` = 1; `src/` projects = `Animora.Domain`, `Animora.Application`, `Animora.Persistence.Sqlite`, `Animora.Infrastructure.Local`, `Animora.Client.Services`, `Animora.Contracts`, `Animora.Contracts.Wire`, `Animora.Desktop`, `Animora.Sync.Abstractions` (+ server-track projects added by earlier S/G phases).
> **Next-free-ordinal rule.** Never hard-code a migration number from this file. List the physical files of the owning `DbContext`'s `Migrations/` folder and take the next free ordinal (server sequence starts at **101**; the client SQLite sequence — head `031` today — is independent and untouched by server phases). Match on the `NNN` token, not a file-name prefix.

---

## ⚡ Zero-exploration contract (read this, then implement top-to-bottom)

> This file is self-contained and pre-verified. Every existing type, signature, path and command you need is in
> **§Reference pack**; every new name is fixed in **§Design decisions**. Do **not** re-open the repo to "understand" it and
> do not re-derive decisions. If — and only if — a compile error proves a snippet diverges from the tree, fix the smallest
> thing and log it in the `PROGRESS.md` deviation log. Names here are the real ones; there is no alias to resolve.
> Implement exactly this scope: nothing from a later phase, nothing "while we're here".

**Environment (do not probe):**
- Build / test / run only through the sandbox scripts (memory-capped ~985 MiB; never raw `dotnet build`):
  `scripts/sandbox/build.sh [fast|full]` (expect 0 warnings / 0 errors), `scripts/sandbox/test.sh [filter]`,
  `scripts/sandbox/run.sh` (desktop smoke → `SMOKE RUN OK`). Run `scripts/sandbox/install-prereqs.sh` once (idempotent).
- `dotnet-ef` lives at `.sandbox/tools/dotnet-ef` — use it; **never** `dotnet tool install`.
- Server stack is locked (`STACK.md` §0/§5/§6/§11): ASP.NET Core 10 + Kestrel, EF Core **10.0.12** line,
  PostgreSQL 18 via `Npgsql.EntityFrameworkCore.PostgreSQL` **10.0.3** / `Npgsql` **10.0.3** (only in `Animora.Persistence.PostgreSql`).
- PostgreSQL integration tests read the connection string from env var `ANIMORA_PG_TEST_CONNECTION`; when unset they
  **skip with a reason** locally (CONVENTIONS §1) and **fail** under `ANIMORA_REQUIRE_PG=1` (mandatory CI/release gate, owned by G07).
- **Forbidden tech (restated, binding):** Redis, message broker, cloud IdP, cloud telemetry, CDN, EF Core 11,
  per-tenant database, hard-coded TLS version (`SslProtocols.None` only), Native AOT with EF Core, `UpdatedAt`/`nextval`/`xmin` sync cursors.

**Offline invariant.** The desktop must keep starting and operating fully offline with no server, no PostgreSQL and no network (Standing principle 1, CONVENTIONS §11). This phase adds nothing the desktop needs at startup.

---

## Read before starting
- `backend-roadmap/PROGRESS.md` — top section only.
- `ARCHITECTURE.md` §8.2, §PG.2, §PG.6, §16-CT.2 — only these `§`.
- `STACK.md` §6.2 — only this `§`.
- `src/Animora.Persistence.PostgreSql/Concurrency/VersionTrigger.cs`, `Conventions/ServerModelConventions.cs` (S02) and the S07–S10 `Configurations/*` (edited: capture triggers attach to the replicated tables).

(Nothing else. `Animora-UI/**`, `Database-Sampel/**`, other phase files and `windows-frontend-roadmap/phases/**` are out of bounds.)

## Goal

Build the **write path of the project-owned critical-state feed** on PostgreSQL: the `SyncContractCatalog` manifest (the fixed replicated surface and its projection order), a per-tenant/epoch **commit-ordered `ChangeSequence`** assigned by a deferred constraint trigger (never `nextval`, never `UpdatedAt`/`xmin`), journal capture triggers for every replicated root **and its direct children** (including deletes as tombstones), the tenant feed state/epoch table, and the monotonic position API. It is the custom protocol of ARCH §PG/§16-CT — **not** a Datasync `/tables/*` surface. Export (frozen batches, paging, retention/GC) is S12.

---

## §Reference pack

```csharp
namespace Animora.Domain.Time;  public interface IClock { DateTimeOffset UtcNow { get; } /* + monotonic helpers per A02 */ }
namespace Animora.Domain.Primitives;
public abstract class SyncableEntity { string Id { get; init; }   /* ULID */  string TenantId { get; set; }  DateTimeOffset UpdatedAt { get; set; }  string? Version { get; set; }  bool Deleted { get; set; } }
public abstract class AggregateRoot : SyncableEntity, IHasDomainEvents { IReadOnlyList<DomainEvent> DomainEvents; void ClearDomainEvents(); protected void Raise(DomainEvent e); }
public static class IdGenerator { public static string NewId(); /* 26-char ULID */ }
```
```csharp
// S02 seams used here
VersionTrigger.Apply(MigrationBuilder mb, string table); ServerModelConventions.TenantIndex(entity, cols…); ITenantContext { TenantId; IsOperator }
// Domain replicated aggregates (all SyncableEntity): Owner, Pet, WeightEntry, DocumentReference, Visit, VaccinationRecord, MedicalHistoryEntry,
//   TreatmentPlan, TreatmentOccurrence, VaccineCatalogItem, Appointment, ReceptionQueueEntry, Room, BoardingServiceDef, BoardingStay, UnitOfMeasure,
//   ProductCategory, Product, Supplier, Insurer, LedgerAccount, AccountingPeriod, Tenant, RoleDefinition, StaffMembership   (ARCH §8.2 — the manifest)
// NOT replicated (ARCH §8.2): Invoice, Payment, CashSession, JournalEntry, Cheque, Expense, Recurring*, StockMovement, PurchaseOrder, GoodsReceipt, credential/authorization tables.
// S06: OfflineWindowOptions.MaxOfflineDays = 90 ; CommandIntakeRetentionOptions.FullRecordDays = 120
```

## §Design decisions

- **Catalog (`SyncContractCatalog`, `Animora.Persistence.PostgreSql.Replication`):** frozen, code-declared list of `SyncEntityDescriptor(string Name, Type Entity, string Table, string[] ChildTables, Func<…> RootResolverForChild)` in **`ProjectionOrder`** = dependency order (parents before children, e.g. `Tenant, RoleDefinition, StaffMembership, UnitOfMeasure, ProductCategory, Product, Supplier, Insurer, LedgerAccount, AccountingPeriod, Owner, Pet, WeightEntry, DocumentReference, MedicalHistoryEntry, Room, BoardingServiceDef, BoardingStay, ReceptionQueueEntry, Appointment, Visit, VaccinationRecord, VaccineCatalogItem, TreatmentPlan, TreatmentOccurrence`). **Every count, loop and test derives from `SyncContractCatalog.ProjectionOrder.Count` — no literal total anywhere** (CI test `CatalogTests.NoMagicTotals` greps `src/Animora.Persistence.PostgreSql/Replication` and phase docs for numeric literals next to "entities"). Excluded set is a second frozen list `SyncExclusions` with a reason per entry; `CatalogTests.EverySyncableEntity_IsReplicatedOrExcluded` fails when a new `SyncableEntity` appears in the model unclassified (this is the guard for future aggregates). Invariant: an entity is replicated **iff** a client must hold it and cannot obtain it only by command settlement.
- **Tables (server migration, next free ordinal — expected after S10's; name `<ordinal>_ChangeJournal`):**
  - `TenantFeedState(TenantId varchar(26) PK, Epoch bigint NOT NULL DEFAULT 1, Generation bigint NOT NULL DEFAULT 1, LastSequence bigint NOT NULL DEFAULT 0, OldestRetainedSequence bigint NOT NULL DEFAULT 0, UpdatedUtc timestamptz(6))` — the **serialization point**; one row per tenant, created by `TenantProvisioner`/migration backfill.
  - `TenantJournal(TenantId varchar(26), Id bigint GENERATED ALWAYS AS IDENTITY (**surrogate only, never an ordering authority**), Entity varchar(48), EntityId varchar(26), Op char(1) ('U' upsert | 'D' delete), Version uuid NULL (NULL for D), ChangeSequence bigint NULL, Epoch bigint, CapturedUtc timestamptz(6), PRIMARY KEY (TenantId, Id))`; indexes `IX_TenantJournal_TenantId_Epoch_ChangeSequence` (UNIQUE where `ChangeSequence IS NOT NULL`), `IX_TenantJournal_TenantId_Entity_EntityId_ChangeSequence`.
  - `JournalTombstone` is **not** a separate table: a `D` row retains `(Entity, EntityId)`; `Deleted=true` soft deletes on `SyncableEntity` are `U` rows with `Deleted` carried in the payload at export time (hard deletes are impossible via the repository base — S03 — but governed direct SQL deletes are still captured).
- **Capture (the trigger design — each reviewed, in the migration):** per replicated table `trg_<table>_journal AFTER INSERT OR UPDATE OR DELETE FOR EACH ROW` calls `animora_journal_capture(entity text, root_expr)`; **child tables** capture against their **root** (`VisitRxLines` → `Visit`) so a child-only change re-exports the root; function inserts `TenantJournal(TenantId, Entity, EntityId, Op, Version, Epoch)` with `ChangeSequence = NULL` **inside the writer's transaction** (no lock yet). A **`CONSTRAINT TRIGGER … AFTER INSERT ON TenantJournal DEFERRABLE INITIALLY DEFERRED`** (`animora_journal_assign_sequence`) runs at **commit time** (guarded with `pg_trigger_depth() = 1`): it executes `UPDATE TenantFeedState SET LastSequence = LastSequence + 1 WHERE TenantId = NEW."TenantId" RETURNING LastSequence` — this row lock is held until the transaction's commit completes, so a later transaction **cannot** receive a sequence until the earlier one is committed ⇒ **sequence order = commit order, with no gaps** (a rolled-back transaction never executes the deferred trigger's effects). The trigger then sets `ChangeSequence` on the captured row. Multiple captured rows of one transaction get consecutive sequence numbers in capture order. Coalescing: the deferred trigger skips a row if the same `(Entity, EntityId)` is captured later in the same transaction (keep last) — expressed as `DELETE` of the earlier unsequenced duplicate before assignment.
- **Why not `nextval`/`UpdatedAt`/`xmin`:** sequences are assigned at call time and are not commit-ordered; timestamps tie and skew; xmin wraps (STACK §6.2). The feed never exposes them. `Version` (uuid, S02) is opaque per-row concurrency evidence only.
- **Position API (`IFeedPosition`, scoped):** `Task<FeedPosition> GetAsync(tenantId)` → `FeedPosition(Epoch, Generation, LastSequence, OldestRetainedSequence)` read from `TenantFeedState` (single-row read; consistent with committed journal because assignment happens under the same row lock). `FeedPosition.WatermarkAtSnapshot` is captured by S12 inside its REPEATABLE READ snapshot, never here.
- **Retention floor:** `OldestRetainedSequence` is advanced only by S12's GC; capture never prunes.
- **Epoch fencing:** `TenantFeedState.Epoch` bumps (and `LastSequence` continues, `OldestRetainedSequence = LastSequence`) via `IFeedEpoch.AdvanceAsync(tenantId, reason)` — used by restore/rebuild procedures (G09) and S12 when retention invalidates continuity; the journal rows carry the epoch they were captured under.
- **Governed direct SQL:** any approved operator SQL path must run through the same tables, so triggers fire; `TRUNCATE` is revoked from the runtime role (documented in the migration comment; role grants are G09) and the model test `JournalCoverageTests.EveryReplicatedTable_HasCaptureTrigger` queries `pg_trigger` (PG-gated) / inspects the migration SQL (pure).
- **Scope exclusions stated:** `RecurringExpense`/`RecurringExpensePayment` are command-settled (not in the catalog; ARCH §8.2). **Statistics has no server/replication surface.** Money/ledger/stock tables are not captured at all.
- **No new ErrorCodes** (feed errors are transport-level in S12).

## §Implementation plan

1. Create `Replication/SyncContractCatalog.cs` (+ `SyncEntityDescriptor`, `SyncExclusions`), `Replication/FeedPosition.cs`, `IFeedPosition`/`FeedPositionReader`, `IFeedEpoch`/`FeedEpochService`, `Replication/JournalSqlTemplates.cs` (the trigger SQL as constants used by the migration + tests).
2. Entities `TenantFeedStateRow`, `TenantJournalRow` + configurations (no query filters on journal tables for the *trigger* path, but the `"Tenant"` filter applies to EF reads; the feed reader of S12 reads through a tenant-scoped context).
3. Generate the migration: `.sandbox/tools/dotnet-ef migrations add ChangeJournal --project src/Animora.Persistence.PostgreSql --startup-project src/Animora.Persistence.PostgreSql --context AnimoraServerDbContext --output-dir Migrations/Server`, rename to the next free ordinal, and hand-write in `Up`: the two functions, the constraint trigger, and for **each descriptor in `SyncContractCatalog.ProjectionOrder`** (loop in the migration code over the catalog — the migration must not hard-code table names that can drift) `JournalTriggers.Apply(migrationBuilder, descriptor)`; backfill one `TenantFeedState` row per existing `Tenants` row; `Down` drops triggers/functions/tables. Update `ServerSchemaVersion.ExpectedHead`.
4. `TenantProvisioner` (S04) edit: create the `TenantFeedState` row in the same transaction as the tenant.
5. Tests: pure — `CatalogTests.EverySyncableEntity_IsReplicatedOrExcluded`, `CatalogTests.ProjectionOrder_ParentsBeforeChildren`, `CatalogTests.NoMagicTotals`, `CatalogTests.PostC11_TreatmentPlanFamily_Replicated_RecurringExpense_Excluded`, `JournalSqlTemplatesTests.DeferredTrigger_IsDeferrable_InitiallyDeferred`, `JournalCoverageTests.EveryReplicatedTable_HasCaptureTrigger_InMigration`; PG-gated (**G07-mandatory, the §PG.6 gate**) — `JournalOrderingTests.SequenceOrder_EqualsCommitOrder_UnderConcurrency`, `…RollbackConsumesNoSequence_NoGap`, `…ChildOnlyChange_CapturesRoot`, `…Delete_CapturesTombstoneKey`, `…SameTransactionDuplicates_Coalesced`, `…DirectGovernedSql_Captured`, `…TwoTenants_IndependentCounters`, `…EpochAdvance_FencesOldRows`.
6. `scripts/sandbox/build.sh full` → 0/0; `scripts/sandbox/test.sh Server` → green (PG facts skipped locally with reason); `scripts/sandbox/run.sh` → `SMOKE RUN OK`.
7. `PROGRESS.md` per recipe (migration ordinal, catalog `ProjectionOrder.Count` **as a recorded value**, not a spec); commit.

## Files to create / modify

| Path | Contents |
|---|---|
| `src/Animora.Persistence.PostgreSql/Replication/SyncContractCatalog.cs`, `SyncEntityDescriptor.cs`, `SyncExclusions.cs` | manifest + projection order |
| `.../Replication/FeedPosition.cs`, `FeedPositionReader.cs`, `FeedEpochService.cs`, `JournalSqlTemplates.cs`, `JournalTriggers.cs` | position, epoch, trigger SQL |
| `.../Configurations/TenantFeedStateConfiguration.cs`, `TenantJournalConfiguration.cs`, `.../Context/AnimoraServerDbContext.cs` (edit) | tables |
| `.../Migrations/Server/*ChangeJournal*` | migration (triggers looped from the catalog) |
| `.../Identity/TenantProvisioner.cs` (edit) | feed-state row |
| `tests/Animora.Server.Tests/Replication/*.cs` | tests |

## Contracts / schema added

Tables `TenantFeedState`, `TenantJournal`; functions `animora_journal_capture`, `animora_journal_assign_sequence`; constraint trigger per journal insert; per-replicated-table `trg_<table>_journal`. Public seams: `SyncContractCatalog.ProjectionOrder`, `SyncExclusions`, `IFeedPosition`, `IFeedEpoch`, `FeedPosition`. No wire DTO yet (S12).

## Rules that apply here

- **Layering (CONVENTIONS §2, `ARCHITECTURE.md` §6):** `Animora.Domain` references nothing; `Animora.Application` → Domain + Contracts + Contracts.Wire;
  only `Animora.Persistence.PostgreSql` may reference Npgsql/`UseNpgsql`; `Animora.Server` must never reference `Animora.Client.Services`,
  `Animora.Persistence.Sqlite`, `Animora.Infrastructure.Local`, `Animora.Sync.Custom` or `Animora.Desktop`; the client never references a server project.
- **C# style (§4):** `sealed` by default; `sealed record` DTOs; nullable honored (no `!` without a why-comment); every async public method takes
  `CancellationToken ct = default` and `ConfigureAwait(false)` in library projects; no `.Result`/`.Wait()`; expected failures return `Result`/`Result<T>`
  with an `ErrorCode`, never exceptions; `IClock`/`TimeProvider` injected (no `DateTime.Now`/`UtcNow`); ids are ULID strings via `IdGenerator`, never `Guid` in business code.
- **EF (§5):** EF Core 10 only; no lazy loading/proxies; reads `AsNoTracking()` + projections; named filters `"Tenant"` and `"SoftDelete"` kept separable;
  paging mandatory (max page 200); migrations additive and never edited later; `EnsureCreated` forbidden outside test fixtures.
- **Security (§7):** Authn → Authz → Entitlement in that order, never merged; `TenantId` resolved server-side from membership only; secrets never in config
  files/logs; defaults fail closed; no custom crypto; append-only audit for every security/financial action.

## Out of scope (owning phase)

- Frozen REPEATABLE READ batches, paging, content-addressed storage, retention/GC, `/sync/*` endpoints, `<Entity>SyncContract` DTOs, wire gate → **S12**.
- Outbox/hints/workers → **S13**. Client application of batches, staging, atomic apply → **G02–G04**. Datasync adapter → deferred (G1 gate, ROADMAP §5).
- PostgreSQL CI/RLS/restore gating → **G07**; WAL/PITR runbook → **G09**.

## Acceptance criteria (mechanical)

1. `scripts/sandbox/build.sh full` = 0 warnings / 0 errors; `scripts/sandbox/run.sh` = `SMOKE RUN OK`; client sources unchanged.
2. `grep -rn "nextval\|UpdatedAt >\|xmin" src/Animora.Persistence.PostgreSql/Replication` returns nothing.
3. Pure tests pass: `CatalogTests.EverySyncableEntity_IsReplicatedOrExcluded`, `CatalogTests.ProjectionOrder_ParentsBeforeChildren`, `CatalogTests.PostC11_TreatmentPlanFamily_Replicated_RecurringExpense_Excluded`, `CatalogTests.NoMagicTotals`, `JournalSqlTemplatesTests.DeferredTrigger_IsDeferrable_InitiallyDeferred`, `JournalCoverageTests.EveryReplicatedTable_HasCaptureTrigger_InMigration`.
4. PG-gated, **mandatory in G07** (the commit-ordering invariants): `JournalOrderingTests.SequenceOrder_EqualsCommitOrder_UnderConcurrency`, `JournalOrderingTests.RollbackConsumesNoSequence_NoGap`, `JournalOrderingTests.ChildOnlyChange_CapturesRoot`, `JournalOrderingTests.Delete_CapturesTombstoneKey`, `JournalOrderingTests.DirectGovernedSql_Captured`, `JournalOrderingTests.TwoTenants_IndependentCounters`, `JournalOrderingTests.EpochAdvance_FencesOldRows`.
5. `ls src/Animora.Persistence.PostgreSql/Migrations/Server | grep -c ChangeJournal` ≥ 1 and the ordinal is the next free one.
6. `git diff --stat -- src/Animora.Desktop src/Animora.Persistence.Sqlite src/Animora.Client.Services src/Animora.Domain` empty.

## PROGRESS.md update

At the end of this phase: set the `S11` row in "Phase status" to `✅ Done` (add date + one-line summary), advance **"Next phase" → `S12`**,
append one `### S11` entry to "Repo state log" (what exists now: projects, tables/migration ordinal, error codes added, test classes), log any
deviation in the "Decision & deviation log", then commit `feat(server): S11 commit-safe tenant journal & change capture (postgresql critical-state feed, write path)` and sync per the repo git workflow.
