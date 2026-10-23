# TODO — Fix cross-client deletion propagation + delete history (owner/pet & siblings)

> **Self-contained execution plan.** A fresh model can implement this WITHOUT re-auditing the repo.
> All root-cause facts, exact file paths, line anchors and decisions are inlined below.
> **LOCKED RULE:** after completing *each* task, run `scripts/git/push.sh "<msg>"`. No push = failure.
> **FINAL task:** delete THIS file only (nothing else in `Plan/`, never touch roadmap files).

---

## 0. The reported problem (one sentence)

When a user deletes an **owner (سرپرست) / pet (بیمار) / …** on client A, the deletion does **not**
appear on the other clients of the same tenant, and deletions are **not shown in history**
(who / when / details). Goal: make deletion sync correctly and standards-compliantly across all
layers (domain → app → persistence → wire → server feed → client apply → UI/history), with a
proper audit trail in history.

---

## 1. Ground-truth architecture (verified from live code — do NOT re-investigate)

### 1.1 Two sync pipelines (strictly separated)
- **State sync = pull-only.** `src/Animora.Sync.Custom/HttpStateSyncEngine.cs` downloads verified
  pages → `SqliteSyncApplyStore` stages → `ReplicaMaterializer` projects onto typed tables. It
  NEVER pushes.
- **Writes = command outbox.** A delete is a journaled command that travels the outbox to the
  server; the server applies it, which mutates the PG row, which the feed then replicates to peers.

### 1.2 Deletes are **soft-deletes**, propagated as **UPSERTS carrying `Deleted=true`**, NOT hard tombstones
- `src/Animora.Application/UseCases/Patients/DeletePatientsCommands.cs`:
  - `DeletePetCommandHandler`: `pet.Deleted = true;` (soft delete).
  - `DeleteOwnerCommandHandler`: sets `owner.Deleted = true` AND cascades `pet.Deleted = true` for
    every pet of that owner, in one unit of work.
  - Both commands are `ICommand<Unit>, IJournaledCommand` (so idempotency replay-guard protects
    them — see Application audit APP-01; `Unit` commands ARE protected).
- Server DB journal trigger (`src/Animora.Persistence.PostgreSql/Replication/JournalSqlTemplates.cs`,
  `CaptureFunction`): a soft-delete is a SQL **UPDATE**, so it records `Op='U'` (NOT `'D'`). Only a
  physical `DELETE` produces `Op='D'`. The row stays physically present with `Deleted=true`.
- Server feed projector (`src/Animora.Persistence.PostgreSql/Replication/RecordProjector.cs`):
  the incremental query LEFT JOINs the live table; a soft-deleted row IS found, so it is emitted as
  `SyncRecord(Entity, Id, Op='U', Version, UpdatedAt, Deleted=true, Payload=<full image>)`.
  (A `'D'` tombstone is emitted ONLY when `reader.IsDBNull(1)` — i.e. a real physical delete.)
- Wire DTO: `src/Animora.Contracts.Wire/Sync/SyncRecord.cs` →
  `record SyncRecord(string Entity, string EntityId, char Op, string? Version, DateTimeOffset? UpdatedAt, bool Deleted, JsonElement? Payload)`.
  The whole record (incl. `Deleted`) is serialized to the page (`FeedPageWriter`), so the client
  receives `Deleted=true`.

### 1.3 Client apply path (where soft-delete becomes a local tombstone)
- `src/Animora.Sync.Custom/Replica/SqliteSyncApplyStore.cs` `AcceptPageAsync` (~line 121):
  ```csharp
  var deleted = record.Op == 'D' || record.Deleted;       // true for a soft-deleted upsert
  var payload = deleted ? null : record.Payload?.GetRawText();  // PAYLOAD IS DISCARDED
  ```
  So a soft-delete is staged in `repl_staging` with `Deleted=1, Payload=NULL`.
- `src/Animora.Sync.Custom/Replica/ReplicaMaterializer.cs` `ApplyAsync`:
  - `if (row.Payload is null)` → runs `map.TombstoneSql`:
    ```sql
    UPDATE "<Table>" SET "Deleted"=1, "UpdatedAt"={0}, "Version"={1} WHERE "Id"={2} AND "TenantId"={3}
    ```
  - The EF global `"SoftDelete"` query filter (`Deleted=false`) then hides the row from the UI.
  - **CRITICAL:** `TombstoneSql` is an `UPDATE` — it affects a row **only if it already exists
    locally**. If the local typed row does not exist yet, `affected==0` → `DeferAsync` (retry
    namespace `__animora_pending_typed_projection__`). Because the payload is NULL, the row can
    **never** be inserted-as-deleted, so the deferred tombstone can be stuck forever.
- Pending-overlay rule: a row with an UNSETTLED local command (`LocallyPosted`/`PendingSettlement`
  on the same aggregate) is skipped (`DeferAsync`) until settlement — correct, do not break this.

### 1.4 History/activity feed = **locally derived, NOT server-replicated**
- There is NO server activity-feed table replicated to clients (verified: no `/activity` feed
  endpoint, activity rows are not in `SyncContractCatalog`).
- On the **originating** client: `src/Animora.Persistence.Sqlite/Repositories/LocalUnitOfWork.cs`
  (lines ~56-73) drains `IHasDomainEvents.DomainEvents` after SaveChanges; `ActivityFeedHandler`
  (`src/Animora.Application/Activity/ActivityFeedHandler.cs`) + `ActivityFeedMapper.Map`
  (`src/Animora.Application/Activity/ActivityFeedMapper.cs`) turn a **domain event** into a history
  row. **`ActivityFeedMapper.Map` has NO case for any delete event** → deletes never produce history.
- On **peer** clients: `ReplicaMaterializer.ApplyAsync` re-derives history drafts from the synced
  business rows (it already does this for `Appointment`→"booked" via `TryBuildBookedDraft` and
  `Visit`→"completed" via `VisitCompletedEvent`), appending through
  `SyncedActivityFeed.AppendAsync` (`src/Animora.Persistence.Sqlite/Activity/SyncedActivityFeed.cs`,
  idempotent per `(EntityType, EntityId, Title)`). **There is NO delete draft synthesized here.**
- `AggregateRoot` (`src/Animora.Domain/Primitives/AggregateRoot.cs`): `protected void Raise(DomainEvent e)`
  adds to `_events`; `DomainEvents` / `ClearDomainEvents()` exposed via `IHasDomainEvents`.
- `SyncableEntity` (`src/Animora.Domain/Primitives/SyncableEntity.cs`) has NO `DeletedByUserId` /
  `DeletedUtc` — the "who/when" is NOT currently carried on the wire.
- History event keys live in `src/Animora.Application/Activity/ActivityEventKeys.cs`
  (pattern: `"history.<area>.<verb>"`). Enums `HistoryEventKind` / `HistoryEventStatus` in
  `Animora.Contracts.History`.

---

## 2. Root causes (what is actually broken)

**RC-1 — (PRIMARY) Deletion has NO audit trail / history entry (who, when, details).**
The user's explicit requirement. Delete command handlers raise no domain event, `ActivityFeedMapper`
has no delete mapping, and `ReplicaMaterializer` synthesizes no delete history draft on peers. So
even when the row *does* disappear, nobody can see "User X deleted owner Y at time T".

**RC-2 — Soft-delete propagation is UPDATE-only; a delete can be lost when the local typed row is
absent.** `SqliteSyncApplyStore` discards the payload for `Deleted=true`, and `ReplicaMaterializer`
tombstones via `UPDATE ... WHERE Id=...`. If a peer never materialized the row (created+deleted
while offline, or earlier skipped as poisoned/constraint/overlay), the tombstone updates 0 rows and
is deferred forever with a NULL payload — it can never converge. This is the latent correctness hole
behind "deletion doesn't arrive".

**RC-3 — Owner-delete cascade to pets relies on EACH pet being journaled server-side.** Confirm the
server actually journals the cascaded pet soft-deletes so peers receive the pet tombstones too (the
`owner.Deleted=true` + per-pet `pet.Deleted=true` writes each go through the capture trigger — verify
no bulk/`ExecuteUpdate` path bypasses the EF change tracker + trigger). If a cascade used a
set-based update it would bypass per-row journaling and the pets would never tombstone on peers.

**RC-4 — "Who/when deleted" is not on the wire.** For peers to show the correct actor + time in
history, the delete must carry the acting user id + instant. Options (choose in Task 3):
(a) carry it on the originating client's history only + a generic actor on peers, or
(b) add minimal bookkeeping (`DeletedByUserId`, `DeletedUtc`) so peers show the exact actor.
Prefer the **lowest-churn standards-compliant** route — see Task 3 decision.

---

## 3. Design decisions (resolve before coding — these are the "pre-thought" conclusions)

- **D-A (history on originating client):** raise `OwnerDeletedEvent` / `PetDeletedEvent` in the
  delete handlers, carrying `TenantId`, `OwnerId`/`PetId`, the actor (`CommandContext` user id) and
  `OccurredAtUtc` (from `IClock`). Add `ActivityFeedMapper.Map` cases → new keys
  `history.owner.deleted`, `history.pet.deleted` (`HistoryEventKind.Patient`,
  `HistoryEventStatus` = a "Deleted/Removed" status; reuse `Cancelled` if no delete status exists,
  but prefer adding an explicit `Removed` status if the enum is small & central). Cascade: the
  owner handler should raise one `OwnerDeletedEvent` + one `PetDeletedEvent` per cascaded pet (so
  each shows up), OR a single owner-deleted event that lists pet count — **decide for clarity:
  raise owner event + per-pet events** so every entity's own history has its deletion row.
- **D-B (history on peer clients):** in `ReplicaMaterializer`, when a tombstone is applied for
  `Owner`/`Pet` (and the other soft-deletable replicated entities), synthesize the matching delete
  draft and append via `SyncedActivityFeed.AppendAsync` (idempotent). Mirror the existing
  `TryBuildBookedDraft` pattern.
- **D-C (actor on the wire):** to show the *correct* actor on peers, add **minimal** bookkeeping to
  the soft-deletable roots: `DeletedByUserId (string?)` + `DeletedUtc (DateTimeOffset?)`, set in the
  delete handlers, mapped in the entity configs, added to the relevant `*SyncContract`s and
  `ReplicaMaterializer` column maps. If this proves too wide, fall back to D-A/D-B with a neutral
  actor on peers and document the limitation. **Prefer carrying the actor** (the user explicitly
  wants "who did it").
- **D-D (fix lost-delete convergence, RC-2):** keep the payload for a soft-deleted upsert so the
  materializer can UPSERT-as-deleted (insert the row with `Deleted=1`) instead of UPDATE-only. i.e.
  in `SqliteSyncApplyStore.AcceptPageAsync`, when `record.Op != 'D'` but `record.Deleted==true`,
  **retain `record.Payload`** (do NOT null it); in `ReplicaMaterializer`, treat
  `row.Payload != null && row.Deleted` as a normal upsert (its existing `args.Add(row.Deleted?1:0)`
  path already writes `Deleted`). Keep the `row.Payload is null` branch (genuine `Op='D'`) as the
  UPDATE-tombstone for hard deletes. This makes a soft-delete converge even when the peer never had
  the row. **Guard:** a delete must still NOT overwrite an unsettled local overlay (keep the
  `pending.Contains` skip), and a full-snapshot must still not resurrect/delete beyond its scope.
- **D-E (scope):** the fix must be **generic** across all soft-deletable replicated entities in
  `SyncContractCatalog` (Owner, Pet, Product, Supplier, Insurer, catalogs, clinical, boarding, …),
  not Owner/Pet only — the mechanism is shared. History drafts (D-B) only for entities the user
  cares about surfacing (at minimum Owner + Pet; extend where a history kind exists).

---

## 4. Task list (each task = one commit + one push)

> Status legend: `[ ]` pending · `[~]` in progress · `[x]` done.
> After finishing a task, set it `[x]` **and** push with the given message.

### [x] T1 — Delete convergence fix (RC-2 / D-D): retain payload for soft-delete upserts
- **Edit** `src/Animora.Sync.Custom/Replica/SqliteSyncApplyStore.cs` `AcceptPageAsync` (~line 121):
  - Separate a genuine hard tombstone from a soft-delete upsert:
    - `var isHardTombstone = record.Op == 'D';`
    - `var payload = isHardTombstone ? null : record.Payload?.GetRawText();`
    - stage `Deleted = (record.Op == 'D' || record.Deleted) ? 1 : 0` as before, but KEEP the payload
      for the soft-delete case.
  - Keep the payload-length guard; keep `UpdatedAtUtc`/`Version` staging unchanged.
- **Edit** `src/Animora.Sync.Custom/Replica/ReplicaMaterializer.cs` `ApplyAsync`:
  - The `if (row.Payload is null)` branch stays the hard-tombstone UPDATE path (unchanged).
  - For `row.Payload != null && row.Deleted == true`: it already flows to the normal upsert which
    writes `Deleted = excluded.Deleted` (verify `BuildUpsert` includes `"Deleted"` — it does).
    Ensure the activity "booked"/"completed" synthesis is gated on `!row.Deleted` (already is) so a
    delete never books a "created" history row.
  - Confirm `TryReadValues` still passes for a soft-deleted row (payload is the full image, so all
    `Required` fields are present → OK).
- **Why:** a soft-deleted peer now converges even when it never had the row (UPSERT-as-deleted).
- **Verify (light, no full build):** open `tests/Animora.Sync.Tests/ReplicaMaterializerTests.cs`
  and `tests/Animora.Sync.Tests/ReplicaTestSupport.cs`; the existing `Tombstone_SoftDeletesLocalRow`
  uses `'D'`. Add/adjust a focused test `SoftDeleteUpsert_WithPayload_WhenRowAbsent_InsertsDeleted`
  that stages `Op='U', Deleted=true, payload=<full owner image>` for an id that was never inserted,
  then asserts the row exists with `Deleted=1` under `IgnoreQueryFilters(["SoftDelete"])`.
- **Push:** `scripts/git/push.sh "fix(sync): converge soft-delete across clients even when peer lacks the row"`

### [ ] T2 — Verify owner-delete cascade journals every pet server-side (RC-3)
- **Read** `src/Animora.Application/UseCases/Patients/DeletePatientsCommands.cs`
  `DeleteOwnerCommandHandler` — it loops `foreach (var pet in await pets.ListByOwnerAsync(...)) pet.Deleted = true;`
  so each pet is a tracked `Modified` entity → each fires the capture trigger. CONFIRM
  `IPetRepository.ListByOwnerAsync` + the UoW path do NOT use a set-based `ExecuteUpdate` that would
  bypass the trigger. (`src/Animora.Application/Ports/IPetRepository.cs` + its PG impl.)
- If any bulk path bypasses journaling, change it to per-entity change-tracked updates so each pet
  tombstone is journaled and reaches peers.
- **Push:** `scripts/git/push.sh "fix(patients): ensure owner-delete cascades journal each pet tombstone for sync"`
  (If no code change is needed, still push a `chore(plan)` status update for T2.)

### [ ] T3 — Carry delete actor/time on the wire (RC-4 / D-C)
- **Domain:** add `DeletedByUserId (string?)` + `DeletedUtc (DateTimeOffset?)` to `SyncableEntity`
  (`src/Animora.Domain/Primitives/SyncableEntity.cs`) OR only to Owner/Pet aggregates if narrower
  scope is preferred. Prefer `SyncableEntity` for uniform convergence of the audit trail. Add a
  domain method e.g. `MarkDeleted(string userId, DateTimeOffset whenUtc)` on the roots that sets
  `Deleted=true`, `DeletedByUserId`, `DeletedUtc` and `Raise(new <X>DeletedEvent(...))`.
- **Delete handlers:** call `MarkDeleted(context.<ActorUserId>, clock.UtcNow)` instead of the bare
  `.Deleted = true` (inject `IClock` + read the actor from `CommandContext`). Confirm the exact
  property name for the acting user on `CommandContext` (grep `CommandContext` in
  `src/Animora.Application/Commands/`), and that handlers can access `IClock`.
- **Persistence (server + client EF configs):** map the two new columns in the Owner/Pet (and, if
  on `SyncableEntity`, every root's) `*Configuration`. Add migrations:
  - Client SQLite: new migration after head `033_OnlineEntitlement` (naming uses double underscore
    before ordinal for 020,022,024–033 — follow the existing `034__…` style; verify current head in
    `src/Animora.Persistence.Sqlite/Migrations/`).
  - Server PG: new migration after head `111_ServerOutbox` (follow 101–111 naming).
  - Columns are nullable → additive, safe for existing rows.
- **Wire:** add `DeletedByUserId` / `DeletedUtc` to `OwnerSyncContract` + `PetSyncContract`
  (`src/Animora.Contracts.Wire/Sync/Contracts/`) so `RecordProjector.Project` emits them (it only
  emits contract properties present on the row image).
- **Client apply:** add the two columns to the Owner/Pet `EntityMap` column list in
  `ReplicaMaterializer` (`Kind.Text` + `Kind.Time`) so the actor/time land in the typed row.
- **Push:** `scripts/git/push.sh "feat(sync): carry delete actor and time on the replication wire"`

### [ ] T4 — History on the ORIGINATING client (RC-1 / D-A)
- **Domain events:** add `OwnerDeletedEvent` / `PetDeletedEvent` records in a Patients events file
  under `src/Animora.Domain/Events/` (follow `PetRegisteredEvent` shape: `EventId, TenantId,
  OccurredAtUtc, <ids>, ActorUserId`). Raise them from `MarkDeleted` (T3).
- **Mapper:** add cases to `ActivityFeedMapper.Map` → new keys in
  `src/Animora.Application/Activity/ActivityEventKeys.cs`:
  `OwnerDeleted = "history.owner.deleted"`, `PetDeleted = "history.pet.deleted"`. Use
  `HistoryEventKind.Patient`; set the actor from the event. Also add the two events to
  `ActivityFeedMapper.Deserialize` (for rebuild) AND to the outbox JSON context
  (`src/Animora.Domain/Events/OutboxEventJsonContext.cs`) so they serialize for the durable outbox.
- **Status enum:** if `HistoryEventStatus` lacks a delete/removed value, add `Removed` (central
  enum in `Animora.Contracts.History`) and localize its label; else reuse `Cancelled`.
- **Localization:** add Persian/RTL strings for the two new `history.*.deleted` keys wherever the
  other `history.*` keys are localized (grep `history.pet.registered` in
  `src/Animora.Desktop/Localization/` or the resource store).
- **Push:** `scripts/git/push.sh "feat(history): record owner/pet deletion with actor and time"`

### [ ] T5 — History on PEER clients (RC-1 / D-B)
- **Edit** `src/Animora.Sync.Custom/Replica/ReplicaMaterializer.cs`: when a tombstone/soft-delete is
  applied for `Owner`/`Pet` (and extend to other entities with a history kind as feasible),
  synthesize the delete draft (mirror `TryBuildBookedDraft`): build `OwnerDeletedEvent` /
  `PetDeletedEvent` using `DeletedByUserId`/`DeletedUtc` from the row (if present via T3; else a
  neutral actor + `UpdatedAt`), map via `ActivityFeedMapper.Map`, and `SyncedActivityFeed.AppendAsync`
  (idempotent per `(EntityType, EntityId, Title)`). Add to the `booked` list and save as the code
  already does.
- Gate correctly: only on `row.Deleted == true` and when the delete was actually applied
  (`affected > 0` for the upsert path, or `tombstoned > 0` for the hard-tombstone path).
- **Verify (light):** extend `ReplicaMaterializerTests` with a case asserting a soft-delete of an
  Owner appends exactly one `history.owner.deleted` activity row, and is idempotent on re-apply.
- **Push:** `scripts/git/push.sh "feat(history): surface synced deletions in peer-client history"`

### [ ] T6 — Cross-check wire/contract/version counts & docs consistency
- Confirm no `CommandTypes`/`ErrorCodes` additions are needed (delete command types already exist:
  `CommandTypes.PatientsOwnerDelete`, `CommandTypes.PatientsPetDelete`; handlers registered in
  `src/Animora.Server/Handlers/ServerHandlerModuleS07.cs`). If you added a new wire field/contract,
  ensure any `SyncContractCatalog`/contract-count assertions in tests
  (`tests/Animora.Server.Tests/Sync/PureFeedTests.cs`, `PgGateManifestTests.cs`) still pass; update
  expected counts if a test pins them.
- Ensure `RecordProjector` still never emits a false `'D'` (D17 guard intact: the only `'D'` is the
  `reader.IsDBNull(1)` physical-delete case — do NOT change that).
- **Push:** `scripts/git/push.sh "test(sync): align delete-sync contract/count assertions"`

### [ ] T7 — Targeted build/test of the touched projects ONLY (no full-solution long test)
- Build + run ONLY the directly affected test projects (fast, scoped):
  - `dotnet build src/Animora.Sync.Custom src/Animora.Persistence.Sqlite src/Animora.Application`
    (and server/persistence.pg if T3 touched them).
  - `dotnet test tests/Animora.Sync.Tests` and the specific added tests. Avoid the full LiveTest
    suite and full-solution test runs (explicitly forbidden by the task constraints).
- Fix any compile/test failures introduced by T1–T6. **If a heavy/large fix is required mid-task,
  push it immediately** with a clear message, then continue.
- **Push:** `scripts/git/push.sh "test(sync): verify delete propagation + history on affected projects"`

### [ ] T8 — FINAL: remove this plan file
- Delete ONLY `Plan/TODO-delete-sync-propagation.md` (leave `Plan/reports/**` and all roadmap/docs
  files untouched).
- **Push:** `scripts/git/push.sh "chore(plan): remove completed delete-sync propagation plan"`

---

## 5. Guardrails / invariants that MUST NOT be broken

- **G04 non-destructive pull:** a pull must never edit the pending-command overlay; a soft-delete
  must still be DEFERRED (not applied) while an UNSETTLED local command overlays the same aggregate
  (`pending.Contains(Key(...))` skip in `ReplicaMaterializer`). Keep it.
- **Full-snapshot safety:** a full snapshot (`FromSequenceExclusive == 0`) must NOT delete local
  rows merely absent from the snapshot (local-first data the server hasn't seen). Only an explicit
  `Deleted=true` / `Op='D'` record deletes. Do not change this.
- **D17 guard:** never turn a faulty/unparseable live row into a `'D'` tombstone in
  `RecordProjector` — keep the "skip, never tombstone" behavior; the only genuine `'D'` is
  `reader.IsDBNull(1)`.
- **Idempotency:** `SyncedActivityFeed.AppendAsync` is keyed on `(EntityType, EntityId, Title)` — a
  re-applied delete must not duplicate the history row. Use a stable title key.
- **Money/stock state stays command-settled** (excluded from pull) — do not add any money entity to
  the state feed.
- **No full build / no long full-suite test.** Scope compilation/tests to touched projects only.

## 6. Quick file index (anchors used above)
- Delete commands: `src/Animora.Application/UseCases/Patients/DeletePatientsCommands.cs`
- Client stage: `src/Animora.Sync.Custom/Replica/SqliteSyncApplyStore.cs` (`AcceptPageAsync` ~L121)
- Client project: `src/Animora.Sync.Custom/Replica/ReplicaMaterializer.cs` (`ApplyAsync`, `BuildUpsert`, `TombstoneSql`)
- Wire DTO: `src/Animora.Contracts.Wire/Sync/SyncRecord.cs`; contracts in `.../Sync/Contracts/*SyncContract.cs`
- Server projector: `src/Animora.Persistence.PostgreSql/Replication/RecordProjector.cs`
- Server journal trigger: `src/Animora.Persistence.PostgreSql/Replication/JournalSqlTemplates.cs`
- Server stamping: `src/Animora.Persistence.PostgreSql/Tenancy/TenantStampingInterceptor.cs`
- Server handler reg: `src/Animora.Server/Handlers/ServerHandlerModuleS07.cs`
- History mapping: `src/Animora.Application/Activity/{ActivityFeedMapper,ActivityEventKeys,ActivityFeedHandler}.cs`
- Peer history sink: `src/Animora.Persistence.Sqlite/Activity/SyncedActivityFeed.cs`
- Event base: `src/Animora.Domain/Primitives/{AggregateRoot,SyncableEntity}.cs`; events in `src/Animora.Domain/Events/`
- Catalog: `src/Animora.Persistence.PostgreSql/Replication/SyncContractCatalog.cs`
- Tests: `tests/Animora.Sync.Tests/{ReplicaMaterializerTests,ReplicaTestSupport,SqliteSyncApplyStoreTests}.cs`
