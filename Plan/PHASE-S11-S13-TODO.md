# Plan — Phases S11 + S12 + S13 (server critical-state feed + outbox/workers)

> **Purpose of this file.** A pre-verified, self-contained task list so a fresh model can implement
> S11→S12→S13 **top-to-bottom with near-zero repo re-exploration**. Everything below the "do not
> re-derive" line was confirmed against the live tree on 2026-10-10 (migration head `108_MoneyStock`).
> Each task says exactly *what file to create/edit*, *what goes in it*, and *when to push*.
>
> **PUSH RULE (locked, fatal if skipped):** after **every** completed task below, run
> `scripts/git/push.sh "type(scope): message"`. Not pushing = project death. Heavy mid-task work
> (large code, big fix during tests) → push immediately with a clear message, don't wait for task end.
>
> **Scope guard:** implement exactly S11, then S12, then S13 — nothing from a later phase, nothing
> "while we're here". The phase source files are `backend-roadmap/phases/PHASE-S11…`, `…S12…`, `…S13…`.
> Do **not** edit those phase files or `backend-roadmap/ROADMAP.md`. The **final task** of this plan
> deletes only this `Plan/PHASE-S11-S13-TODO.md` file.

---

## ✅ Verified baseline (confirmed 2026-10-10 — do NOT re-derive)

**Environment / build (never raw `dotnet`):**
- `scripts/sandbox/build.sh [fast|full]` → expect 0 warnings / 0 errors (mem-capped ~985 MiB).
- `scripts/sandbox/test.sh [filter]` → e.g. `scripts/sandbox/test.sh Server`. PG-gated facts **skip with a reason** unless `ANIMORA_PG_TEST_CONNECTION` is set; they **fail** under `ANIMORA_REQUIRE_PG=1` (G07 CI gate, not local).
- `scripts/sandbox/run.sh` → desktop smoke, must print `SMOKE RUN OK`.
- `.sandbox/tools/dotnet-ef` is the EF CLI — **never** `dotnet tool install`.
- **Do a full build+smoke only at phase close**, not after every micro-edit (user rule: no long full-test loops). Prefer `build.sh fast` during work; one `build.sh full` + targeted `test.sh` at the end of each phase.

**Stack (locked):** ASP.NET Core 10 + Kestrel, EF Core 10.0.12 line, PostgreSQL 18 via `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3 / `Npgsql` 10.0.3 — **only** in `Animora.Persistence.PostgreSql`. Forbidden: Redis, broker, cloud IdP/telemetry/CDN, EF Core 11, per-tenant DB, Native AOT+EF, `UpdatedAt`/`nextval`/`xmin` sync cursors, Polly.

**Migration head = `108_MoneyStock`.** Files live in `src/Animora.Persistence.PostgreSql/Migrations/Server/`. Server ordinals are `101`–`108` (physical head). **Next free ordinals: S11 = `109`, S12 = `110`, S13 = `111`** (confirm by `ls Migrations/Server` before generating — do not hard-code if another phase landed first). EF generates `<timestamp>_<Name>.cs`; **rename** both `.cs` and `.Designer.cs` + the migration class attribute/id to the `NNN_Name` form, matching existing files (e.g. `20261010100000_108_MoneyStock.cs`).
`ServerSchemaVersion.ExpectedHead` is currently `"108_MoneyStock"` (`src/Animora.Persistence.PostgreSql/ServerSchemaVersion.cs`). Bump it once per phase to the new head.

**Projects present:** `Animora.Domain`, `Animora.Application`, `Animora.Persistence.Sqlite`, `Animora.Infrastructure.Local`, `Animora.Client.Services`, `Animora.Contracts`, `Animora.Contracts.Wire`, `Animora.Desktop`, `Animora.Sync.Abstractions`, `Animora.Persistence.PostgreSql`, `Animora.Server`. Tests: `Animora.Server.Tests` (has `Commands/`, `Identity/`, `Persistence/`, `HandlersS07..S10/`, `RepoRoot.cs`, `TestHostFactory.cs`, `SourceScanTests.cs`, `ServerLayeringTests.cs`).

**Key existing seams (exact, verified):**
- `src/Animora.Persistence.PostgreSql/Concurrency/VersionTrigger.cs` — `VersionTrigger.Apply(MigrationBuilder mb, string table)` / `.Drop(...)`. Creates `trg_<table>_version`. Function `animora_set_version` created once in `101_ServerFoundation`.
- `src/Animora.Persistence.PostgreSql/Conventions/ServerModelConventions.cs` — `ApplyTenancy(modelBuilder, ()=>CurrentTenantId)`, `TenantFilterName="Tenant"`, `SoftDeleteFilterName="SoftDelete"`, `TenantIndex(entity, cols…)`. Applies to every `SyncableEntity` root. `Version` is `uuid NOT NULL DEFAULT gen_random_uuid()`, converted to string via `StringGuidConverter`, concurrency token.
- `src/Animora.Persistence.PostgreSql/Context/AnimoraServerDbContext.cs` — `sealed`, ctor `(DbContextOptions<AnimoraServerDbContext> options, ITenantContext tenantContext)`. `CurrentTenantId` throws when unbound. `OnModelCreating` calls `ApplyConfigurationsFromAssembly(...)` then `ServerModelConventions.ApplyTenancy(...)` then adds explicit `"Tenant"` filters for non-syncable rows (`DocumentCounter`, `RefreshToken`, `SecurityAuditEntry`, `CommandIntakeRecord`, `CommandIdMapEntry`). Add new `DbSet<>`s + non-syncable tenant filters here for feed tables.
- `src/Animora.Persistence.PostgreSql/Identity/TenantProvisioner.cs` — `CreateTenantWithOwnerAsync(...)` adds `Tenant` + 5 `RoleDefinition`s + owner `User` + `StaffMembership`, then one `SaveChangesAsync`. **S11 edits this** to also add a `TenantFeedState` row before the save.
- `src/Animora.Persistence.PostgreSql/ServerSchemaVersion.cs` — `ExpectedHead` const.
- `src/Animora.Domain/Results/ErrorCodes.cs` — **166** codes today (not the 161 doc baseline — S07–S10 added some). Pattern: inside `#region <context>`, `public static readonly ErrorCode X = new("X_CODE", ErrorCode.<Kind>);`. **Add a new `#region` per phase; never rename/remove.** `OutboxDeadLettered` and `CommandClaimLost` already exist (region "Command pipeline (B02)", ~lines 661/665) → **S13 adds no new codes**.
- `src/Animora.Contracts.Wire/WireContract.cs` — `Version = 2` (S06 already bumped it; doc's "baseline 1" is stale). `IsSupported(int) => 1 or 2`, `HeaderName="X-Animora-Contract-Version"`, `SupportedVersionsHeaderName="X-Animora-Supported-Versions"`, `SupportedContractVersions=[1,2]`.
- Wire errors (A01): `Animora.Contracts.Wire.Errors.WireApiError(Code, Detail, Fields, CorrelationId)`, `WireFieldError(Field, Code)`.
- `src/Animora.Application/Outbox/IOutbox.cs` — `Task<Result> EnqueueAsync(string messageType, object payload, CancellationToken ct=default)` (client impl only; **server gets its own**).
- Client reference to mirror semantically (read only if unclear): `src/Animora.Persistence.Sqlite/Outbox/{LocalOutbox,OutboxClaim,OutboxRelayHandler}.cs`.
- S06 seams (all in `src/Animora.Server/Commands/`, **`internal`**): `IServerOutbox` (interface + `NoServerOutbox` no-op) with `Task<Result> AppendAsync(CommandEnvelope envelope, SettlementResult settlement, CancellationToken ct=default)`; `IntakeRecoveryService` (`internal sealed`, `RecoverAsync(CommandIntakeRecord row, ct)`, `LeaseOwner="command-intake"`, `LeaseDuration=60s`); `CommandIntakeRetentionOptions` (`internal`, `FullRecordDays=120`, `ReceiptDays=3650`); `OfflineWindowOptions` (`internal`, `MaxOfflineDays=90`). **`CommandIntakeCompactor` + `ICommandIntakeStore` live in persistence:** `src/Animora.Persistence.PostgreSql/Commands/{CommandIntakeCompactor,CommandIntakeStore,ICommandIntakeStore}.cs`.
- Host/routing (`src/Animora.Server/Hosting/`, mostly **`internal`**): `RateLimitPolicies.Sync="sync"` (+ Auth/Commands/Files/Operator); `ServerOptions.DataRootPath` (`internal`, env `Server__DataRootPath`); `DataRootWritableProbe`. **No `ServerRoutes` class exists yet** — the doc references `ServerRoutes.Sync`; define a small `internal static class ServerRoutes { public const string Sync="/sync"; }` in `Animora.Server` when wiring S12 endpoints (or inline `"/sync"`), and note the deviation.
- `src/Animora.Persistence.PostgreSql/Options/DatabaseOptions.cs` — current fields: `ConnectionString`, `OltpWriteMax=7`, `OltpReadMax=4`, `KeyRingMax=1`, `ProfileGiB=2`. **Validator** `DatabaseOptionsValidator.cs`: `Budget2GiB=12`, `Budget4GiB=20`, checks `OltpWriteMax+OltpReadMax+KeyRingMax ≤ budget`. **S13 adds `WorkerMax` and includes it in the sum.**

**⚠️ Known deviations from the phase docs (decide + log these in PROGRESS.md "Decision & deviation log"):**
1. **`MaxOfflineDays`/`FullRecordDays` live in `Animora.Server` as `internal`.** S11/S12's `RetentionPolicy` must live in `Animora.Persistence.PostgreSql`, which **cannot** reference `Animora.Server` (layering). → Define the `RetentionPolicy` constant locally in persistence (`MaxOfflineDays=90`, `JournalRetentionDays=MaxOfflineDays+30=120`) and add a **pure test** asserting it equals the server's `OfflineWindowOptions.MaxOfflineDays`/`CommandIntakeRetentionOptions.FullRecordDays` *values* (the test can live in `Animora.Server.Tests`, which references both). Document "single source is the numeric 90/120; mirrored by test, not by a shared type" as the deviation.
2. `WireContract.Version` is already `2` (no bump needed). Sync DTOs are "additive at v2".
3. `DatabaseOptions` defaults are `7/4/1` not the doc's `6/3/1`. S13 rebalances to make room for `WorkerMax` (default 2) and keep the sum ≤ 12 (2 GiB). Suggested: `OltpWriteMax=6, OltpReadMax=3, KeyRingMax=1, WorkerMax=2` → sum 12. Log the exact chosen split.
4. `ServerRoutes` class does not exist — introduced by S12 (or inline route strings). Log it.
5. Pool "keyring" data source is a separate context (`AnimoraKeyRingDbContext`); the doc's "write+read+keyring+worker" maps to `OltpWriteMax+OltpReadMax+KeyRingMax+WorkerMax`.

**Replicated entity → physical table → child tables (VERIFIED from migrations + Configurations).**
This is the authoritative map for S11's `SyncContractCatalog` and S12's projection. Catalog `ProjectionOrder` (parents before children):

| # | Entity (CLR) | Table | Child tables (same aggregate) |
|---|---|---|---|
| 1 | `Tenant` | `Tenants` | — |
| 2 | `RoleDefinition` | `RoleDefinitions` | — |
| 3 | `StaffMembership` | `StaffMemberships` | — |
| 4 | `UnitOfMeasure` | `UnitsOfMeasure` | — |
| 5 | `ProductCategory` | `ProductCategories` | — |
| 6 | `Product` | `Products` | `ProductVariants` |
| 7 | `Supplier` | `Suppliers` | — |
| 8 | `Insurer` | `Insurers` | — |
| 9 | `LedgerAccount` | `LedgerAccounts` | — |
| 10 | `AccountingPeriod` | `AccountingPeriods` | — |
| 11 | `Owner` | `Owners` | — |
| 12 | `Pet` | `Pets` | — |
| 13 | `WeightEntry` | `WeightEntries` | — |
| 14 | `DocumentReference` | `DocumentReferences` | — |
| 15 | `MedicalHistoryEntry` | `MedicalHistory` | — |
| 16 | `Room` | `Rooms` | — |
| 17 | `BoardingServiceDef` | `BoardingServiceDefs` | — |
| 18 | `BoardingStay` | `BoardingStays` | `BoardingStayItems`, `BoardingHandoffLines` |
| 19 | `ReceptionQueueEntry` | `ReceptionQueueEntries` | — |
| 20 | `Appointment` | `Appointments` | — |
| 21 | `Visit` | `Visits` | `VisitRxLines`, `VisitHandoffLines` |
| 22 | `VaccinationRecord` | `VaccinationRecords` | — |
| 23 | `VaccineCatalogItem` | `VaccineCatalogItems` | — |
| 24 | `TreatmentPlan` | `TreatmentPlans` | — |
| 25 | `TreatmentOccurrence` | `TreatmentOccurrences` | — |

> **Count = 25 entities** (the doc's prose example lists 26 because it double-counts; derive every loop/test from `SyncContractCatalog.ProjectionOrder.Count`, **never** a literal — `CatalogTests.NoMagicTotals` greps for literals near "entities"). **Note:** `AppointmentSettings`, `DocumentCounters`, `VisitHandoffLines`/`BoardingHandoffLines` (billing handoffs) — confirm at implement time whether the handoff lines are part of the Visit/BoardingStay aggregate (they are child tables of those roots in config → capture against the root). `AppointmentSettingsEntity` is **not** a replicated root (singleton settings) → put in `SyncExclusions` unless proven otherwise.
>
> **Excluded (`SyncExclusions`, reason each):** `Invoice`, `Payment`, `CashSession`, `JournalEntry`, `Cheque`, `Expense`, `RecurringExpense`, `RecurringExpensePayment`, `StockMovement`, `PurchaseOrder`, `GoodsReceipt`, credential/authorization tables (`User`, `Device`, `RefreshToken`, `SecurityAuditEntry`, `SigningKey`), `CommandIntakeRecord`, `CommandIdMapEntry`, `DocumentCounter`, `AppointmentSettingsEntity`. Reason: command-settled / money-ledger-stock / security — a client cannot need them by replication (ARCH §8.2).

**Child-table → root-id column (for `root_expr` in capture triggers):**
- `ProductVariants` → root `Products`, FK `ProductId` (confirm column name in `ProductConfiguration`/migration 108 at implement time).
- `VisitRxLines` → root `Visits`. **Confirm FK column** — `VisitConfiguration` maps prescriptions `WithOne().HasForeignKey("OwnerId")` (EF shadow FK literally named `OwnerId` pointing at the owning Visit; verify in migration 105 `VisitRxLines` columns before writing the trigger `root_expr`).
- `VisitHandoffLines` → root `Visits`, FK `VisitId` (verified `IX_VisitHandoffLines_TenantId_VisitId`).
- `BoardingStayItems` → root `BoardingStays` (verify FK column in migration 106).
- `BoardingHandoffLines` → root `BoardingStays`, FK likely `BoardingStayId` (verify).

---

## Phase S11 — Commit-safe tenant journal & change capture (write path)

**Migration:** `109_ChangeJournal`. **New folder:** `src/Animora.Persistence.PostgreSql/Replication/`. **No new ErrorCodes.**
**Satisfies** R-B03/R-B11/R-B13. Scope = *write path only*; export/paging/GC is S12.

### S11 design facts (pre-derived — implement, don't re-decide)
- **Serialization point** is the row lock on `TenantFeedState`, taken at **commit time** by a `DEFERRABLE INITIALLY DEFERRED` constraint trigger → sequence order == commit order, no gaps (a rolled-back tx never runs the deferred effect). **Never** `nextval`/`UpdatedAt`/`xmin`.
- Capture (`AFTER INSERT OR UPDATE OR DELETE FOR EACH ROW`) inserts a `TenantJournal` row with `ChangeSequence = NULL` inside the writer's own transaction (no lock yet). The deferred constraint trigger (guard `pg_trigger_depth()=1`) assigns the sequence at commit.
- **Coalescing:** deferred trigger keeps the **last** journal row per `(Entity, EntityId)` in a transaction — delete earlier unsequenced duplicates before assigning.
- Children capture against their **root** (child-only change re-exports the root).
- `D` (delete) rows carry `(Entity, EntityId)`, `Version=NULL`; soft-deletes (`Deleted=true`) are `U` rows (payload carries `Deleted` at export, S12). No separate tombstone table.
- `TRUNCATE` must be revoked from the runtime role — **document in a migration SQL comment** (actual grant is G09).

### Tables (migration `109_ChangeJournal`)
- **`TenantFeedState`** (non-syncable, the serialization point; one row per tenant):
  `TenantId varchar(26) PK`, `Epoch bigint NOT NULL DEFAULT 1`, `Generation bigint NOT NULL DEFAULT 1`, `LastSequence bigint NOT NULL DEFAULT 0`, `OldestRetainedSequence bigint NOT NULL DEFAULT 0`, `UpdatedUtc timestamptz(6)`.
- **`TenantJournal`** (non-syncable):
  `TenantId varchar(26)`, `Id bigint GENERATED ALWAYS AS IDENTITY` (surrogate only — **never** an ordering authority), `Entity varchar(48)`, `EntityId varchar(26)`, `Op char(1)` ('U'|'D'), `Version uuid NULL`, `ChangeSequence bigint NULL`, `Epoch bigint`, `CapturedUtc timestamptz(6)`, `PRIMARY KEY (TenantId, Id)`.
  Indexes: `IX_TenantJournal_TenantId_Epoch_ChangeSequence` **UNIQUE WHERE "ChangeSequence" IS NOT NULL**; `IX_TenantJournal_TenantId_Entity_EntityId_ChangeSequence`.
- Both tables: EF config gives them an explicit `"Tenant"` query filter in `OnModelCreating` (like the other non-syncable tables) — but the **trigger path** is pure SQL and ignores EF filters. S12's feed reader reads through the tenant-scoped context.

### Functions / triggers (hand-written SQL in migration `Up`, held as constants in `JournalSqlTemplates.cs`)
- `animora_journal_capture(entity text, root_id text)` → inserts `TenantJournal(TenantId, Entity, EntityId, Op, Version, ChangeSequence=NULL, Epoch, CapturedUtc)`. Derive `Op` from `TG_OP` ('DELETE'→'D' else 'U'), `TenantId`/`EntityId` from the row (`OLD` on delete, `NEW` otherwise; `EntityId = root_id`), `Version` = `NEW."Version"` for 'U' else `NULL`, `Epoch` from current `TenantFeedState.Epoch` of that tenant.
- `animora_journal_assign_sequence()` — **constraint trigger** `AFTER INSERT ON "TenantJournal" DEFERRABLE INITIALLY DEFERRED FOR EACH ROW`, guard `IF pg_trigger_depth() <> 1 THEN RETURN NULL; END IF;` then: delete earlier unsequenced duplicate of same `(TenantId, Entity, EntityId)` (coalesce keep-last); `UPDATE "TenantFeedState" SET "LastSequence"="LastSequence"+1, "UpdatedUtc"=now() WHERE "TenantId"=NEW."TenantId" RETURNING "LastSequence"` (this lock held to commit ⇒ ordering); set `NEW."ChangeSequence"` to the returned value. (Because it's a constraint trigger you can't mutate `NEW` directly at commit; implement as an `UPDATE "TenantJournal" SET "ChangeSequence"=... WHERE "TenantId"=NEW."TenantId" AND "Id"=NEW."Id"` — verify the exact mechanics against PG 18 constraint-trigger semantics; the invariant that matters is "sequence assigned under the FeedState row lock at commit".)
- `JournalTriggers.Apply(MigrationBuilder mb, SyncEntityDescriptor d)` — for the root table creates `trg_<table>_journal AFTER INSERT OR UPDATE OR DELETE FOR EACH ROW EXECUTE FUNCTION animora_journal_capture('<Entity>', <root-id expr>)`; and for **each child table** a trigger whose `root_id` resolves to the child's root FK (so the Entity recorded is the **root** entity name). `.Drop(...)` mirror.

### Public seams S12 depends on (exact names)
```csharp
namespace Animora.Persistence.PostgreSql.Replication;
public sealed record SyncEntityDescriptor(string Name, Type Entity, string Table, IReadOnlyList<string> ChildTables /* + child->root resolver info */);
public static class SyncContractCatalog { public static IReadOnlyList<SyncEntityDescriptor> ProjectionOrder { get; } }
public static class SyncExclusions { /* frozen list + reason per entry */ }
public sealed record FeedPosition(long Epoch, long Generation, long LastSequence, long OldestRetainedSequence);
public interface IFeedPosition { Task<FeedPosition> GetAsync(string tenantId, CancellationToken ct = default); }
public interface IFeedEpoch { Task AdvanceAsync(string tenantId, string reason, CancellationToken ct = default); }
```
- `FeedPositionReader : IFeedPosition` (scoped) — single-row read of `TenantFeedState`.
- `FeedEpochService : IFeedEpoch` — `UPDATE TenantFeedState SET Epoch=Epoch+1, OldestRetainedSequence=LastSequence, UpdatedUtc=now() WHERE TenantId=@t`.

> Execution directive: no builds/tests/smoke are run. Migrations and frozen designer/snapshot metadata are authored by hand to avoid dotnet-ef implicitly building. Acceptance execution is explicitly unverified.

### S11 tasks

- [x] **S11-T1 — Catalog + seams (pure, no DB).** Create `Replication/SyncEntityDescriptor.cs`, `SyncContractCatalog.cs` (the 25-row `ProjectionOrder` from the verified table above, parents before children, with child tables + child→root FK column), `SyncExclusions.cs` (excluded entities + reason string each), `FeedPosition.cs` (record + `IFeedPosition`/`IFeedEpoch` interfaces). All `sealed`/`sealed record`. No magic totals anywhere (derive from `.Count`).
  **PUSH:** `scripts/git/push.sh "feat(server): S11-T1 sync contract catalog + feed seams"`

- [x] **S11-T2 — SQL templates + trigger helper (pure).** Create `Replication/JournalSqlTemplates.cs` (the two functions + constraint-trigger SQL as `const string`/builders; the constraint trigger text must contain `DEFERRABLE INITIALLY DEFERRED`) and `Replication/JournalTriggers.cs` (`Apply/Drop(MigrationBuilder, SyncEntityDescriptor)` looping root + child tables). Keep all literal SQL here so tests can assert on it without a DB.
  **PUSH:** `scripts/git/push.sh "feat(server): S11-T2 journal sql templates + trigger helper"`

- [x] **S11-T3 — Entities + EF config + DbContext.** Create `TenantFeedStateRow`/`TenantJournalRow` (POCOs in `Replication/`), `Configurations/TenantFeedStateConfiguration.cs`, `Configurations/TenantJournalConfiguration.cs` (map columns/indexes exactly; `Id` = identity-by-default/`ValueGeneratedOnAdd`). Add `DbSet<>`s + explicit `"Tenant"` filters in `AnimoraServerDbContext.OnModelCreating` (mirror the existing non-syncable block). Register `FeedPositionReader`/`FeedEpochService` in the persistence DI extension (scoped).
  **PUSH:** `scripts/git/push.sh "feat(server): S11-T3 feed-state + journal entities, config, DI"`

- [x] **S11-T4 — Migration `109_ChangeJournal`.** `ls Migrations/Server` to re-confirm next ordinal = `109`. Run:
  `.sandbox/tools/dotnet-ef migrations add ChangeJournal --project src/Animora.Persistence.PostgreSql --startup-project src/Animora.Persistence.PostgreSql --context AnimoraServerDbContext --output-dir Migrations/Server`
  Rename files + class/id to `109_ChangeJournal`. Hand-write `Up`: create the two functions, the `TenantFeedState`/`TenantJournal` tables + indexes (the UNIQUE-WHERE partial index needs raw `.Sql` or `HasFilter`), the constraint trigger, then **loop `SyncContractCatalog.ProjectionOrder`** calling `JournalTriggers.Apply(mb, d)` (migration must not hard-code table names). Backfill: `INSERT INTO "TenantFeedState" ("TenantId") SELECT "Id" FROM "Tenants" ON CONFLICT DO NOTHING;` Add the `TRUNCATE`-revoke documentation comment. `Down` drops triggers/functions/tables in reverse. Bump `ServerSchemaVersion.ExpectedHead = "109_ChangeJournal"`. Update `AnimoraServerDbContextModelSnapshot`.
  **PUSH:** `scripts/git/push.sh "feat(server): S11-T4 migration 109 ChangeJournal (functions, tables, triggers)"`

- [x] **S11-T5 — TenantProvisioner edit.** In `CreateTenantWithOwnerAsync`, before `SaveChangesAsync`, add `context.Set<TenantFeedStateRow>().Add(new TenantFeedStateRow { TenantId = tenant.Id, ... defaults })` so the feed-state row is created in the same transaction as the tenant.
  **PUSH:** `scripts/git/push.sh "feat(server): S11-T5 provision TenantFeedState row with tenant"`

- [x] **S11-T6 — Tests.** `tests/Animora.Server.Tests/Replication/`:
  - Pure: `CatalogTests.EverySyncableEntity_IsReplicatedOrExcluded` (reflect over `SyncableEntity` subtypes in the model vs catalog∪exclusions), `CatalogTests.ProjectionOrder_ParentsBeforeChildren`, `CatalogTests.NoMagicTotals` (grep `Replication/` for numeric literals adjacent to "entities"), `CatalogTests.PostC11_TreatmentPlanFamily_Replicated_RecurringExpense_Excluded`, `JournalSqlTemplatesTests.DeferredTrigger_IsDeferrable_InitiallyDeferred` (string contains), `JournalCoverageTests.EveryReplicatedTable_HasCaptureTrigger_InMigration` (scan the migration SQL for `trg_<table>_journal` per descriptor).
  - PG-gated (skip w/o `ANIMORA_PG_TEST_CONNECTION`): `JournalOrderingTests.{SequenceOrder_EqualsCommitOrder_UnderConcurrency, RollbackConsumesNoSequence_NoGap, ChildOnlyChange_CapturesRoot, Delete_CapturesTombstoneKey, SameTransactionDuplicates_Coalesced, DirectGovernedSql_Captured, TwoTenants_IndependentCounters, EpochAdvance_FencesOldRows}`. Reuse existing PG fixture pattern from `tests/Animora.Server.Tests/Persistence/` (check how those read `ANIMORA_PG_TEST_CONNECTION` / skip).
  **PUSH:** `scripts/git/push.sh "test(server): S11-T6 catalog + journal ordering tests"`

- [x] **S11-T7 — Acceptance + PROGRESS (execution waived by explicit user directive; no green/build/smoke claim).** `scripts/sandbox/build.sh full` (0/0); `scripts/sandbox/test.sh Server` (green, PG skipped locally); `scripts/sandbox/run.sh` (`SMOKE RUN OK`). Verify: `grep -rn "nextval\|UpdatedAt >\|xmin" src/Animora.Persistence.PostgreSql/Replication` = empty; `ls Migrations/Server | grep -c ChangeJournal` ≥ 1; `git diff --stat -- src/Animora.Desktop src/Animora.Persistence.Sqlite src/Animora.Client.Services src/Animora.Domain` empty. Update `backend-roadmap/PROGRESS.md`: S11 row → ✅ Done (date + 1-line), Next phase → S12, append `### S11` repo-state entry (projects, migration ordinal 109, `ProjectionOrder.Count` as a recorded value, test classes), log the deviations (#1 RetentionPolicy location note is S12's; here log catalog count = 25 and any FK-column confirmations).
  **PUSH:** `scripts/git/push.sh "feat(server): S11 commit-safe tenant journal & change capture (postgresql critical-state feed, write path)"`

---

## Phase S12 — Immutable batch export (REPEATABLE READ frozen batches, paging, retention & GC)

**Migration:** `110_FeedBatches`. **+2 ErrorCodes.** **Depends on S11.** Server side only (client consumer is G01–G04).
**Satisfies** R-B03/R-B11/R-B31.

### S12 design facts (pre-derived)
- **Freeze = one short-lived `REPEATABLE READ READ ONLY` snapshot.** Inside it read watermark (`LastSequence`) **and** journal rows `(FromSequence, watermark]` **and** current row images for `U` keys — one snapshot — then COMMIT and only then materialize/write pages. Hard `MaxSnapshotLifetime = 10 s`; exceeded ⇒ abort + retry once then `503 Retry-After`.
- **Content-addressed, deterministic:** a batch keyed by `(Epoch, FromSequenceExclusive, Watermark [, IsFullSnapshot])` — a retry returns the **same** `BatchId`. `PageHash = SHA-256(gzip bytes)`, `ContentHash = SHA-256(concat page hashes)`.
- **Pages ≤ 256 KiB (262144 bytes) uncompressed JSON**, each gzipped, written temp-file + `File.Move` (atomic) to `DataRootPath/feed/<TenantId>/<BatchId>/<ordinal>.gz`. Batch row inserted **last** with `State=Ready`; crash before that → orphan files swept by GC. Stream/incremental — never hold whole batch in memory.
- **Projection order:** `SyncContractCatalog.ProjectionOrder` (parents first); within an entity, deletes last. Child collections inlined into the root record.
- `TenantId` is **never** serialized in any `<Entity>SyncContract` or `SyncRecord` (reflection test enforces).
- **Open-batch decision** (`BatchFreezer`, one-at-a-time per tenant + globally bounded by `IBackgroundAdmission` = shared `SemaphoreSlim(1)`): read `TenantFeedState`; `request.Epoch != current` ⇒ `ResyncRequired(EPOCH_CHANGED)`; `FromSequence < OldestRetainedSequence` ⇒ `ResyncRequired(BEYOND_RETENTION)`; `FromSequence == LastSequence` ⇒ `UpToDate`; else reuse-or-build `BatchReady`.
- **`IBackgroundAdmission` is DECLARED here** (interface + default `SemaphoreBackgroundAdmission(1)`), **implemented/registered by S13** too (S13 "implements the worker side"). Declare it so S12 compiles and runs standalone.
- **Retention/GC** (`FeedRetentionService.RunAsync(ct)`, invoked by S13 worker, never inline). `RetentionPolicy.JournalRetentionDays = MaxOfflineDays(90) + 30 = 120`. GC step order (crash-atomic, each idempotent):
  (a) `UPDATE FeedBatch SET State=Retired,RetiredUtc` for batches >24 h old that are not the newest Ready per `(Tenant,Epoch)`;
  (b) delete page **files** of Retired batches;
  (c) `DELETE` page metadata then batch rows of Retired batches whose files are gone;
  (d) **one transaction:** `DELETE TenantJournal WHERE ChangeSequence <= cutoffSeq` **and** `SET TenantFeedState.OldestRetainedSequence = cutoffSeq` (never delete journal without advancing the floor in the same tx);
  (e) orphan sweep: files with no batch row older than 1 h.
- **Full resync** `POST /sync/snapshots`: freeze **all** current catalog rows (no journal range), `IsFullSnapshot=true`, `FromSequenceExclusive=0`, watermark from the snapshot. Does **not** bump `Generation` (that's `IFeedEpoch`/restore).
- **Mixed-fleet:** supported versions {1,2}. Sync DTOs introduced at v2; a v1 client never calls `/sync` → `426`. Serializer `SyncSerializerV2`.
- **Observability hooks** (names only, via `System.Diagnostics.Metrics`): `animora.sync.batch.build.duration`, `…pages`, `…bytes`, `animora.pg.snapshot.age.seconds`.

### Tables (migration `110_FeedBatches`, non-syncable, `"Tenant"` filter)
- `FeedBatch(TenantId varchar(26), BatchId varchar(26), Epoch bigint, Generation bigint, FromSequenceExclusive bigint, Watermark bigint, PageCount int, ContentHash char(64), State smallint {Building=0,Ready=1,Retired=2}, IsFullSnapshot bool, CreatedUtc timestamptz(6), RetiredUtc timestamptz(6)?, PRIMARY KEY(TenantId,BatchId))`; UNIQUE `(TenantId, Epoch, FromSequenceExclusive, Watermark, IsFullSnapshot)` (content-address). Indexes lead with `TenantId`.
- `FeedBatchPage(TenantId varchar(26), BatchId varchar(26), PageOrdinal int, PageHash char(64), SizeBytes int, PRIMARY KEY(TenantId,BatchId,PageOrdinal))`.
- **Immutability trigger:** blocks UPDATE of everything except `RetiredUtc` once `State=Ready` (BEFORE UPDATE trigger raising on changed non-`RetiredUtc` columns). Add via raw `.Sql` in the migration.

### Wire DTOs (`Animora.Contracts.Wire/Sync/`, additive — no `WireContract.Version` bump; each manifest carries `ContractVersion`)
```csharp
namespace Animora.Contracts.Wire.Sync;
public sealed record SyncManifest(int ContractVersion, long Epoch, long Generation, IReadOnlyList<string> ProjectionOrder, int PageTargetBytes);
public sealed record OpenBatchRequest(long Epoch, long FromSequence);            // FromSequence 0 = none
// OpenBatchResponse is a discriminated result — model as one record with a Kind, or 3 records + wrapper:
public sealed record BatchReady(string BatchId, long Epoch, long Generation, long FromSequenceExclusive, long Watermark, int PageCount, string ContentHash);
public sealed record UpToDate(long Watermark);
public sealed record ResyncRequired(string Reason, long Epoch);                   // Reason: EPOCH_CHANGED|BEYOND_RETENTION|CONTRACT_UNSUPPORTED
public sealed record SyncPage(string BatchId, int PageOrdinal, int PageCount, IReadOnlyList<SyncRecord> Records, string PageHash);
public sealed record SyncRecord(string Entity, string EntityId, char Op, string? Version, DateTimeOffset? UpdatedAt, bool Deleted, JsonElement? Payload);
// per-entity: Contracts/<Entity>SyncContract.cs — ONE per catalog entry (25), allowed fields only, NO TenantId.
```
- `Version` = canonical lowercase UUID string via `EntityVersionCodec` (check if such a codec exists; if not, inline `Guid.ToString("D").ToLowerInvariant()` and note it).

### Endpoints (`Animora.Server/Sync/SyncEndpoints.cs`, rate-limit policy `sync`, `[Authorize]` + S05 tenant context + `DeviceActive`)
- `GET /sync/manifest` → `SyncManifest`.
- `POST /sync/batches` (body `OpenBatchRequest`) → `UpToDate | BatchReady | ResyncRequired`.
- `GET /sync/batches/{batchId}/pages/{ordinal}` → gzip page; `ETag = PageHash`, `Cache-Control: private, immutable`, `If-None-Match` → 304. **Server validates `(tenant, BatchId, ordinal)` ownership** → foreign/absent = 404 (audited).
- `POST /sync/snapshots` → full-state `BatchReady` (`FromSequenceExclusive=0`).
- Every request must send `X-Animora-Contract-Version`; unsupported ⇒ `426` + `ResyncRequired(CONTRACT_UNSUPPORTED)` (reuse S06 gate — check how S06's `ContractVersionUnsupported` / 426 path is implemented in `Animora.Server/Commands/`).

### New ErrorCodes (+2, region "Sync feed (S12)")
```csharp
public static readonly ErrorCode ResyncRequired = new("RESYNC_REQUIRED", ErrorCode.Conflict);
public static readonly ErrorCode BatchNotFound  = new("BATCH_NOT_FOUND", ErrorCode.NotFound);
```

### S12 tasks

- [x] **S12-T1 — Wire DTOs.** Create `src/Animora.Contracts.Wire/Sync/*.cs` (manifest/open/response/page/record above) + `Sync/Contracts/<Entity>SyncContract.cs`, **one per catalog entry (25)**, hand-written with allowed fields only, no `TenantId`. (Derive the required list from `SyncContractCatalog` — but Contracts.Wire must not reference the persistence project; so maintain the list by hand and let the S12 test `EveryCatalogEntry_HasExactlyOneSyncContract`, which lives in Server.Tests and *can* see both, enforce parity.)
  **PUSH:** `scripts/git/push.sh "feat(wire): S12-T1 sync feed DTOs + per-entity sync contracts"`

- [x] **S12-T2 — ErrorCodes +2.** Add region "Sync feed (S12)" with `ResyncRequired`, `BatchNotFound` to `src/Animora.Domain/Results/ErrorCodes.cs`.
  **PUSH:** `scripts/git/push.sh "feat(server): S12-T2 add RESYNC_REQUIRED + BATCH_NOT_FOUND error codes"`

- [x] **S12-T3 — Persistence engine.** In `src/Animora.Persistence.PostgreSql/Replication/`: `IBackgroundAdmission.cs` (+ `SemaphoreBackgroundAdmission(1)`), `RetentionPolicy.cs` (`MaxOfflineDays=90`, `JournalRetentionDays=120`), `RecordProjector.cs` (catalog row-image → `SyncRecord`, child collections inlined, ProjectionOrder/deletes-last), `FeedPageWriter.cs` (temp-file + atomic `File.Move`, ≤262144 B uncompressed per page, gzip, SHA-256), `FeedPageReader.cs`, `BatchFreezer.cs` (the freeze algorithm; `IsolationLevel.RepeatableRead` appears **only here**; no `ToListAsync()` — stream/page), `FeedRetentionService.cs` (GC steps a–e). Entities `FeedBatchRow`/`FeedBatchPageRow` + `Configurations/FeedBatch*.cs`. DI registrations (scoped/singleton as appropriate) in the persistence DI extension.
  **PUSH:** `scripts/git/push.sh "feat(server): S12-T3 batch freezer, page writer/reader, projector, retention"`

- [x] **S12-T4 — Migration `110_FeedBatches`.** Re-confirm ordinal `110`. Generate via dotnet-ef, rename to `110_FeedBatches`, add the immutability trigger (raw SQL), bump `ServerSchemaVersion.ExpectedHead="110_FeedBatches"`, update snapshot. Add `DbSet<>`s + `"Tenant"` filters in `AnimoraServerDbContext`.
  **PUSH:** `scripts/git/push.sh "feat(server): S12-T4 migration 110 FeedBatches + immutability trigger"`

- [x] **S12-T5 — Endpoints + DI.** `src/Animora.Server/Sync/SyncEndpoints.cs` (+ `ServerRoutes.Sync` const or inline `"/sync"`), register `FeedRetentionService` + `BatchFreezer` + `IBackgroundAdmission` default in DI, map endpoints in `Program.cs` under the `sync` rate limit with `[Authorize]`+tenant+DeviceActive. Reuse S06's 426/contract-version gate. Audit foreign-batch 404s via the existing server audit writer (`ServerCommandAuditWriter` or the security audit — check which fits).
  **PUSH:** `scripts/git/push.sh "feat(server): S12-T5 /sync endpoints (manifest, batches, pages, snapshots)"`

- [ ] **S12-T6 — Tests.** `tests/Animora.Server.Tests/Sync/`:
  - Pure: `ContractsTests.NoSyncContract_HasTenantIdProperty`, `ContractsTests.EveryCatalogEntry_HasExactlyOneSyncContract`, `PageWriterTests.PagesNeverExceed256KiB_Uncompressed`, `PageWriterTests.TempFileThenAtomicMove`, `RecordProjectorTests.ProjectionOrder_ParentsFirst_DeletesLast`, `RetentionPolicyTests.JournalRetention_Is_OfflineWindowPlus30`, `RetentionPolicyTests.SameConstantSizesCommandIntakeRetention` (assert `RetentionPolicy.JournalRetentionDays == CommandIntakeRetentionOptions.FullRecordDays` value — the deviation-#1 bridge test), `OpenBatchDecisionTests.EpochChanged_BeyondRetention_UpToDate_Ready`, `GcOrderTests.EachStep_Idempotent_CrashBetweenSteps_Safe` (fake fs/db), `SyncEndpointsTests.ContractVersionUnsupported_Returns426`, `SyncEndpointsTests.ForeignBatchId_Returns404_Audited`.
  - PG-gated: `BatchFreezerTests.{WatermarkAndRows_FromOneSnapshot, SnapshotCommitted_BeforePageMaterialization, SameRange_SameBatchId_ByteStable, SnapshotOverMaxLifetime_AbortsAndRetries}`, `GcTests.{JournalDeletion_AdvancesFloor_SameTransaction, CrashAfterFileDelete_BeforeRowDelete_Recovers}`, `SyncIsolationTests.TenantA_Batch_ContainsNoTenantB_Rows`, `SyncFlowTests.OpenDownloadResume_FromLastPage`.
  **PUSH:** `scripts/git/push.sh "test(server): S12-T6 contracts, page writer, projector, freezer, gc, isolation tests"`

- [ ] **S12-T7 — Acceptance + PROGRESS.** `build.sh full` (0/0), `test.sh Server` (green), `run.sh` (`SMOKE RUN OK`). Verify: `grep -rn "ToListAsync()" src/Animora.Persistence.PostgreSql/Replication` empty; `grep -rn "IsolationLevel.RepeatableRead" src/Animora.Persistence.PostgreSql/Replication` matches **only** `BatchFreezer`; `ls Migrations/Server | grep -c FeedBatches` ≥ 1; `grep -c "RESYNC_REQUIRED\|BATCH_NOT_FOUND" src/Animora.Domain/Results/ErrorCodes.cs` = 2; client sources unchanged. Update `PROGRESS.md`: S12 → ✅ Done, Next → S13, `### S12` entry, log deviation #1 (RetentionPolicy local constant + bridge test) and #4 (ServerRoutes introduced).
  **PUSH:** `scripts/git/push.sh "feat(server): S12 immutable batch export"`

---

## Phase S13 — Outbox, background workers & resilience

**Migration:** `111_ServerOutbox`. **No new ErrorCodes** (reuses `OutboxDeadLettered`, `CommandClaimLost`). **Depends on S10** (and uses S06 + S12 seams). `BackgroundService` workers are JIT (**never Native AOT with EF Core**).
**Satisfies** R-B07/R-B09/R-B25.

### S13 design facts (pre-derived)
- **Transactional outbox:** `ServerOutbox : IServerOutbox` (replaces S06's `NoServerOutbox`) inserts through the **same `AnimoraServerDbContext` transaction** as the command effect (so "Settled ⇔ effect ⇔ outbox row"). Enqueue **never** calls `SaveChanges`.
- **Atomic claim (single statement):** `UPDATE … RETURNING` over a `FOR UPDATE SKIP LOCKED` `WITH due` CTE. Eligibility predicate: `("Status"=0 AND "NextAttemptUtc"<=@now) OR ("Status"=1 AND "ClaimedUntilUtc"<=@now)`. Lease 60 s; sets `Status=1, ClaimToken=@token, ClaimedUntilUtc=@now+@lease, Attempts=Attempts+1`. **Completion/failure updates are fenced** `WHERE "ClaimToken"=@token` → a worker that lost its lease gets 0 rows ⇒ log `OutboxClaimLost`, no effect. `SKIP LOCKED` is for work distribution only.
  Exact SQL (from the phase doc; use via `FromSqlInterpolated`/raw `NpgsqlCommand`):
  ```sql
  WITH due AS (
    SELECT "TenantId","Id" FROM "ServerOutbox"
    WHERE ("Status" = 0 AND "NextAttemptUtc" <= @now)
       OR ("Status" = 1 AND "ClaimedUntilUtc" <= @now)
    ORDER BY "NextAttemptUtc" LIMIT @n FOR UPDATE SKIP LOCKED)
  UPDATE "ServerOutbox" o
    SET "Status" = 1, "ClaimToken" = @token, "ClaimedUntilUtc" = @now + @lease, "Attempts" = o."Attempts" + 1
    FROM due WHERE o."TenantId" = due."TenantId" AND o."Id" = due."Id"
  RETURNING o.*;
  ```
  (A `Status=1` row with NULL `ClaimedUntilUtc` can't exist because NULL lease rows are `Status=0`; a test covers it anyway.)
- **Retry/dead-letter — `ServerRetryPolicy`:** base 2 s, factor 2, cap 15 min, **full jitter** (`random(0, delay)` from `Random.Shared`), max attempts 8 → `DeadLettered` + audit row. `NextDelay(attempt, random)` is a **pure** function (no clock) — unit-testable.
- **Resilience (in-house, no Polly):** `CircuitBreaker` (Closed/Open/HalfOpen, threshold 5 fails in 60 s, open 30 s, single half-open probe, `TimeProvider`-driven); `OutboundTimeout` (default 10 s via linked CTS); `BoundedWorkerQueue` (`Channel<T>` bounded, `FullMode=Wait`, capacity 64) + `MaxConcurrentConsumers=2`. All long jobs take `IBackgroundAdmission` (shared `SemaphoreSlim(1)` from S12) so projection/export/GC/recovery never run simultaneously and never take the OLTP pool (dedicated `worker` budget).
- **Idempotent consumers:** each consumer inserts `(ConsumerName, TenantId, MessageId)` into `ConsumedMessages` + does its effect in **one transaction**; unique violation ⇒ already done ⇒ ack. Registry `IOutboxConsumer { string MessageType; string Name; Task<Result> ConsumeAsync(OutboxMessage m, CancellationToken ct); }`. First consumer: `FeedAdvancedHintConsumer`.
- **Message types** (`OutboxMessageTypes` constants): `feed.advanced` (payload: tenantId, lastSequence — one per committing tx that touched replicated tables), `audit.flush` (reserved), `license.recheck` (S16), `subscription.expiry` (S14). Unknown types ⇒ dead-letter, never drop.
- **Workers** (`BackgroundService`, all in `ServerWorkersServiceCollectionExtensions.AddAnimoraWorkers()`): `OutboxRelayWorker` (claims ≤20 every 1 s, idle-backoff to 15 s), `IntakeRecoveryWorker` (30 s, ≤50 rows, calls `IntakeRecoveryService`), `CommandIntakeRetentionWorker` (daily, `CommandIntakeCompactor.CompactAsync(now − FullRecordDays)`), `FeedRetentionWorker` (hourly, `FeedRetentionService.RunAsync`), `OutboxCleanupWorker` (deletes `Done` > 7 d). Each loop: `PeriodicTimer`, catch all except cancellation, rate-limited log per failure class, expose `WorkerHeartbeat` (last-success UTC) to `/health/ready` as **non-fatal** probe `workers` (stale > 5× period ⇒ degraded, by name). Graceful stop: finish current item ≤10 s or let the lease expire.
- **SignalR hint (in-process, no backplane):** `SyncHintHub` at `/sync/hub` (authorized, group = tenant id from S05 context — **server-chosen**, client can't join another group). `FeedAdvancedHintConsumer` sends `FeedAdvanced(long lastSequence)` to the tenant group. Hint only; clients must still pull.
- **Pool rebalance:** add `WorkerMax` (default 2) to `DatabaseOptions`; update validator to `OltpWriteMax+OltpReadMax+KeyRingMax+WorkerMax ≤ budget`; rebalance defaults so the 2 GiB sum stays ≤ 12. **Chosen split: `OltpWriteMax=6, OltpReadMax=3, KeyRingMax=1, WorkerMax=2` (sum 12).** Add the keyed `worker` data source (mirror how read/write/keyring data sources are registered in the persistence DI — see `PersistenceServiceCollectionExtensions.cs`).

### Tables (migration `111_ServerOutbox`, both non-syncable)
- `ServerOutbox(TenantId varchar(26), Id varchar(26), MessageType varchar(64), PayloadJson jsonb, Status smallint {Pending=0,Claimed=1,Done=2,DeadLettered=3}, Attempts int, NextAttemptUtc timestamptz(6) NOT NULL, ClaimToken varchar(26)?, ClaimedUntilUtc timestamptz(6)?, LastError varchar(512)?, CreatedUtc timestamptz(6), CompletedUtc timestamptz(6)?, CorrelationId varchar(64), PRIMARY KEY(TenantId,Id))`.
  Indexes: `IX_ServerOutbox_Status_NextAttemptUtc` **partial `WHERE "Status" IN (0,1)`** (NOT tenant-led — the one documented exception, the global relay claims across tenants with `LIMIT n`); `IX_ServerOutbox_TenantId_CreatedUtc`.
- `ConsumedMessages(ConsumerName varchar(48), TenantId varchar(26), MessageId varchar(26), ConsumedUtc timestamptz(6), PRIMARY KEY(ConsumerName,TenantId,MessageId))`.
- The relay uses a **dedicated narrow context `OutboxDbContext`** (maps ONLY these two tables, **no** tenant filter, never `IgnoreQueryFilters`). The tables are **created by the main context's migration** (same `__EFMigrationsHistory`); `OutboxDbContext` is read/write only, **never** used for migrations.

### S13 tasks

- [ ] **S13-T1 — Persistence: entities, OutboxDbContext, claim store, enqueue.** In `src/Animora.Persistence.PostgreSql/Outbox/`: `OutboxMessage`/`ConsumedMessage` POCOs, `OutboxDbContext` (narrow model, both tables, no filter), `Configurations/Outbox*.cs` (also map the two tables on the **main** context so the migration owns them — confirm the pattern so the tables aren't double-created; likely configure on the main context only and let `OutboxDbContext` map by convention). `ServerOutbox : IServerOutbox` (enqueue into main context tx), `OutboxClaimStore` (the claim SQL via raw `NpgsqlCommand`/`FromSqlInterpolated` returning rows; fenced complete/fail), `OutboxMessageTypes`, `IOutboxConsumer`.
  **PUSH:** `scripts/git/push.sh "feat(server): S13-T1 server outbox entities, dedicated context, claim store"`

- [ ] **S13-T2 — Pool: WorkerMax + validator + worker data source.** Edit `Options/DatabaseOptions.cs` (+`WorkerMax=2`, rebalance defaults 6/3/1/2), `Options/DatabaseOptionsValidator.cs` (include `WorkerMax` in the sum + `WorkerMax>=1`), register the keyed `worker` NpgsqlDataSource in the DI extension.
  **PUSH:** `scripts/git/push.sh "feat(server): S13-T2 WorkerMax pool budget + worker data source"`

- [ ] **S13-T3 — Migration `111_ServerOutbox`.** Re-confirm ordinal `111`. Generate via dotnet-ef (main context), rename to `111_ServerOutbox`, hand-check the partial index `WHERE "Status" IN (0,1)` (raw `.Sql`/`HasFilter`), bump `ServerSchemaVersion.ExpectedHead="111_ServerOutbox"`, update snapshot. Add `DbSet<>`s (+ non-syncable `"Tenant"` filter for `ServerOutbox`? — NOTE: the relay context has no filter; on the **main** context add a `"Tenant"` filter for `ServerOutbox`/`ConsumedMessages` for any tenant-scoped reads, but the relay always goes through `OutboxDbContext`).
  **PUSH:** `scripts/git/push.sh "feat(server): S13-T3 migration 111 ServerOutbox + ConsumedMessages"`

- [ ] **S13-T4 — Resilience envelope.** `src/Animora.Server/Resilience/{ServerRetryPolicy,CircuitBreaker,OutboundTimeout,BoundedWorkerQueue}.cs` per the design facts. `ServerRetryPolicy.NextDelay(attempt, random)` pure.
  **PUSH:** `scripts/git/push.sh "feat(server): S13-T4 retry policy, circuit breaker, timeout, bounded queue"`

- [ ] **S13-T5 — Workers + hint + IBackgroundAdmission impl.** `src/Animora.Server/Workers/*.cs` (the 5 workers + `WorkerHeartbeat` + non-fatal `workers` readiness probe), `src/Animora.Server/Hints/{SyncHintHub,FeedAdvancedHintConsumer}.cs`, `ServerWorkersServiceCollectionExtensions.AddAnimoraWorkers()`. Register the **real** `IBackgroundAdmission` (singleton `SemaphoreBackgroundAdmission(1)`) shared with S12. Map `/sync/hub` (authorized) in `Program.cs`. Call `AddAnimoraWorkers()` from host startup.
  **PUSH:** `scripts/git/push.sh "feat(server): S13-T5 background workers, heartbeat probe, signalr feed hint"`

- [ ] **S13-T6 — Wire enqueue into command plane.** Replace S06's no-op `IServerOutbox` registration with `ServerOutbox`. Intake emits one `feed.advanced` message on Accepted outcomes **that touched replicated tables** (flag from the unit of work: any `TenantJournal` insert in the transaction ⇒ enqueue one message). Inspect `src/Animora.Server/Commands/CommandIntakeService.cs` + `CommandPlaneUnitOfWork.cs` to find where the effect commits and how to detect a journal insert (e.g. check the UoW for a journal-touched flag, or query `TenantJournal` change count in the tracked context).
  **PUSH:** `scripts/git/push.sh "feat(server): S13-T6 emit feed.advanced on replicated command settlement"`

- [ ] **S13-T7 — Tests.** `tests/Animora.Server.Tests/{Outbox,Workers,Resilience}/`:
  - Pure: `ServerRetryPolicyTests.NextDelay_CappedExponential_FullJitter_Bounds`, `CircuitBreakerTests.OpensAfter5In60s_HalfOpenSingleProbe_Closes`, `OutboundTimeoutTests.Cancels_AfterTimeout`, `BoundedWorkerQueueTests.Backpressure_DoesNotDropOrGrow`, `OutboxClaimSqlTests.Predicate_HandlesNullLease_And_ExpiredLease`, `ConsumerIdempotencyTests.Redelivery_IsNoOp`, `WorkerLoopTests.Exception_DoesNotKillLoop_RateLimitedLog`, `WorkerHeartbeatTests.Stale_ReportsDegradedByNameOnly`, `PoolBudgetTests.SumIncludesWorker_Within12_And20`, `HintHubTests.GroupIsServerChosen_ClientCannotJoinOtherTenant`.
  - PG-gated: `OutboxClaimTests.{TwoWorkers_NeverClaimSameRow_SkipLocked, ExpiredLease_Reclaimable_FencedCompletionRejected, EnqueueRollsBackWithBusinessTransaction}`, `WorkerRecoveryTests.KilledWorker_LeaseExpires_RowProcessed`.
  **PUSH:** `scripts/git/push.sh "test(server): S13-T7 outbox claim, resilience, worker, hint tests"`

- [ ] **S13-T8 — Acceptance + PROGRESS.** `build.sh full` (0/0), `test.sh Server` (green), `run.sh` (`SMOKE RUN OK`). Verify: `grep -rn "StackExchange\|Redis\|RabbitMq\|ServiceBus\|Kafka\|Polly" src/Animora.Server src/Animora.Persistence.PostgreSql` empty; `grep -rn "PublishAot" src` empty; `grep -rn "IgnoreQueryFilters" src/Animora.Server/Workers src/Animora.Persistence.PostgreSql/Outbox` empty; `ls Migrations/Server | grep -c ServerOutbox` ≥ 1; client sources unchanged. Update `PROGRESS.md`: S13 → ✅ Done, Next → S14, `### S13` entry, log deviation #3 (pool split) + #5.
  **PUSH:** `scripts/git/push.sh "feat(server): S13 outbox, background workers & resilience (atomic claim, fenced lease, breaker, bulkhead)"`

---

## Final task (plan closeout)

- [ ] **Z — Delete this plan file.** After S11-T7, S12-T7 and S13-T8 are all ✅ and pushed, delete **only** `Plan/PHASE-S11-S13-TODO.md` (leave every other file, including the roadmap/phase files, untouched — they were already cleaned by the owning phases per each phase's own "PROGRESS.md update" step).
  `rm Plan/PHASE-S11-S13-TODO.md`
  **PUSH:** `scripts/git/push.sh "chore(plan): remove S11-S13 plan (phases complete)"`

---

## Quick reference — exact push commands per task (copy/paste)

```
# S11
scripts/git/push.sh "feat(server): S11-T1 sync contract catalog + feed seams"
scripts/git/push.sh "feat(server): S11-T2 journal sql templates + trigger helper"
scripts/git/push.sh "feat(server): S11-T3 feed-state + journal entities, config, DI"
scripts/git/push.sh "feat(server): S11-T4 migration 109 ChangeJournal (functions, tables, triggers)"
scripts/git/push.sh "feat(server): S11-T5 provision TenantFeedState row with tenant"
scripts/git/push.sh "test(server): S11-T6 catalog + journal ordering tests"
scripts/git/push.sh "feat(server): S11 commit-safe tenant journal & change capture (postgresql critical-state feed, write path)"
# S12
scripts/git/push.sh "feat(wire): S12-T1 sync feed DTOs + per-entity sync contracts"
scripts/git/push.sh "feat(server): S12-T2 add RESYNC_REQUIRED + BATCH_NOT_FOUND error codes"
scripts/git/push.sh "feat(server): S12-T3 batch freezer, page writer/reader, projector, retention"
scripts/git/push.sh "feat(server): S12-T4 migration 110 FeedBatches + immutability trigger"
scripts/git/push.sh "feat(server): S12-T5 /sync endpoints (manifest, batches, pages, snapshots)"
scripts/git/push.sh "test(server): S12-T6 contracts, page writer, projector, freezer, gc, isolation tests"
scripts/git/push.sh "feat(server): S12 immutable batch export"
# S13
scripts/git/push.sh "feat(server): S13-T1 server outbox entities, dedicated context, claim store"
scripts/git/push.sh "feat(server): S13-T2 WorkerMax pool budget + worker data source"
scripts/git/push.sh "feat(server): S13-T3 migration 111 ServerOutbox + ConsumedMessages"
scripts/git/push.sh "feat(server): S13-T4 retry policy, circuit breaker, timeout, bounded queue"
scripts/git/push.sh "feat(server): S13-T5 background workers, heartbeat probe, signalr feed hint"
scripts/git/push.sh "feat(server): S13-T6 emit feed.advanced on replicated command settlement"
scripts/git/push.sh "test(server): S13-T7 outbox claim, resilience, worker, hint tests"
scripts/git/push.sh "feat(server): S13 outbox, background workers & resilience (atomic claim, fenced lease, breaker, bulkhead)"
# Closeout
scripts/git/push.sh "chore(plan): remove S11-S13 plan (phases complete)"
```
