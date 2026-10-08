# Phase S12 — Immutable batch export — REPEATABLE READ frozen batches, paging, retention & GC

**Band:** M · **Track:** S (server) · **Depends on:** S11 · **Next:** S13
**Satisfies:** R-B03, R-B11, R-B31 · **Runnable after this phase:** desktop offline yes · server required no
**Produced by:** M00 (regenerated 2026-10-08; supersedes the deleted `PHASE-D08 (half 2)` file).

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
- `ARCHITECTURE.md` §8.2, §8.6, §16-CT, §17-CT, §18-CT, §PG.4, §PG.6 — only these `§`.
- `src/Animora.Persistence.PostgreSql/Replication/*` (S11) and `src/Animora.Contracts.Wire/*` (edited here).

(Nothing else. `Animora-UI/**`, `Database-Sampel/**`, other phase files and `windows-frontend-roadmap/phases/**` are out of bounds.)

## Goal

Serve the state feed: freeze **immutable, content-addressed, multi-entity batches** from one short-lived REPEATABLE READ snapshot (watermark + rows from the same snapshot), store pages (~256 KiB) as byte-stable files with a DB manifest, expose resumable `/sync/*` endpoints with protected `(BatchId, PageOrdinal)` continuations, the `SyncContractCatalog`-driven `<Entity>SyncContract` DTOs (TenantId never serialized), full-resync snapshot generation, retention (offline window + margin) with crash-atomic GC, epoch fencing and the wire-version gate. Server side only; the desktop consumer is G01–G04.

---

## §Reference pack

```csharp
namespace Animora.Contracts.Wire.Errors;   // (A01)
public sealed record WireApiError(string Code, string? Detail, IReadOnlyList<WireFieldError> Fields, string CorrelationId);
public sealed record WireFieldError(string Field, string Code);
namespace Animora.Contracts.Wire;
public static class WireContract { public const int Version = 1; public const string HeaderName = "X-Animora-Contract-Version"; }
```
```csharp
// S11 (this track) — exact public surface you build on
namespace Animora.Persistence.PostgreSql.Replication;
public sealed record SyncEntityDescriptor(string Name, Type Entity, string Table, IReadOnlyList<string> ChildTables /* … */);
public static class SyncContractCatalog { public static IReadOnlyList<SyncEntityDescriptor> ProjectionOrder { get; } }
public sealed record FeedPosition(long Epoch, long Generation, long LastSequence, long OldestRetainedSequence);
public interface IFeedPosition { Task<FeedPosition> GetAsync(string tenantId, CancellationToken ct = default); }
public interface IFeedEpoch { Task AdvanceAsync(string tenantId, string reason, CancellationToken ct = default); }
// tables: TenantFeedState(TenantId,Epoch,Generation,LastSequence,OldestRetainedSequence) · TenantJournal(TenantId,Id,Entity,EntityId,Op 'U'|'D',Version uuid?,ChangeSequence bigint?,Epoch,CapturedUtc)
// S06: OfflineWindowOptions.MaxOfflineDays = 90 ; WireContract.Version = 2 ; WireContract.IsSupported(int) ; X-Animora-Supported-Versions header
// S01: ServerOptions.DataRootPath ; RateLimitPolicies.Sync ("sync") ; ServerRoutes.Sync ("/sync") ; S13 provides BackgroundAdmission (semaphore) — used here via an interface `IBackgroundAdmission` declared by THIS phase (S13 implements the worker side).
```

## §Design decisions

- **Wire DTOs (`Animora.Contracts.Wire.Sync`, additive, no `WireContract.Version` bump — new endpoints; each manifest carries `ContractVersion`):** `SyncManifest(int ContractVersion, long Epoch, long Generation, IReadOnlyList<string> ProjectionOrder, int PageTargetBytes)`; `OpenBatchRequest(long Epoch, long FromSequence /* client checkpoint watermark, 0 = none */)`; `OpenBatchResponse` = `BatchReady(string BatchId, long Epoch, long Generation, long FromSequenceExclusive, long Watermark, int PageCount, string ContentHash)` | `UpToDate(long Watermark)` | `ResyncRequired(string Reason /*EPOCH_CHANGED|BEYOND_RETENTION|CONTRACT_UNSUPPORTED*/, long Epoch)`; `SyncPage(string BatchId, int PageOrdinal, int PageCount, IReadOnlyList<SyncRecord> Records, string PageHash)`; `SyncRecord(string Entity, string EntityId, char Op, string? Version, DateTimeOffset? UpdatedAt, bool Deleted, JsonElement? Payload)`; per-entity payload types `<Entity>SyncContract` (e.g. `PetSyncContract`) **generated by hand per catalog entry** with allowed fields only. **`TenantId` is never present in any contract** (test: reflection over `Animora.Contracts.Wire.Sync` asserts no property named `TenantId`). `Version` = canonical lowercase UUID string via `EntityVersionCodec`.
- **Endpoints (rate-limit policy `sync`, `[Authorize]` + S05 tenant context + `DeviceActive`):** `GET /sync/manifest`; `POST /sync/batches` (open: decide **UpToDate | BatchReady | ResyncRequired**); `GET /sync/batches/{batchId}/pages/{ordinal}` (byte-stable, `ETag = PageHash`, `Cache-Control: private, immutable`, supports `Range`-free simple GET + `If-None-Match`); `POST /sync/snapshots` (build a **full-state** batch generation for resync; same BatchReady shape, `FromSequenceExclusive = 0`). Continuation tokens: `BatchId` is a ULID opaque to the client; the **server validates `(tenant, BatchId, ordinal)` ownership** on every GET — a client cannot name another tenant's batch (404, audited). Every request must send `X-Animora-Contract-Version`; unsupported ⇒ `426` + `ResyncRequired(CONTRACT_UNSUPPORTED)` semantics (S06 gate reused).
- **Freeze algorithm (`BatchFreezer`, one at a time per tenant AND globally bounded by `IBackgroundAdmission` — `SemaphoreSlim(1)` shared with other heavy jobs):** (1) read `TenantFeedState`; if request epoch ≠ current epoch ⇒ `ResyncRequired(EPOCH_CHANGED)`; if `FromSequence < OldestRetainedSequence` ⇒ `ResyncRequired(BEYOND_RETENTION)`; if `FromSequence == LastSequence` ⇒ `UpToDate`; (2) **content-address reuse:** if a batch with the same `(Epoch, FromSequenceExclusive, Watermark)` exists ⇒ return it (a retry gets the **same** `BatchId` ⇒ deterministic); (3) **capture transaction** (`REPEATABLE READ READ ONLY`, hard `MaxSnapshotLifetime = 10 s`, exceeded ⇒ abort + restart once then `503 Retry-After`): inside it read `watermark = LastSequence` from `TenantFeedState` **and** the journal rows `(FromSequence, watermark]` **and** the keys/versions — **one snapshot**; coalesce to the last journal row per `(Entity, EntityId)`; read the current row images **in the same transaction** for `U` rows (SELECT by key list in `ProjectionOrder`, batched ≤ 500 keys, child collections included) — then **COMMIT** (snapshot released). Memory bound: stream and write pages incrementally, never hold the whole batch; (4) outside the snapshot: serialize records in `ProjectionOrder` (parents first, deletes last within entity) into pages of **≤ 256 KiB (262144 bytes) uncompressed JSON**, gzip each, compute `PageHash = SHA-256(gzip bytes)` and batch `ContentHash = SHA-256(concat page hashes)`; write page files `DataRootPath/feed/<TenantId>/<BatchId>/<ordinal>.gz` via temp-file + `File.Move` (atomic), then insert metadata rows; (5) the batch row is inserted **last** with `State = Ready`; a crash before this leaves orphan files swept by GC. Tables are immutable after `Ready` (trigger blocks UPDATE of everything except `RetiredUtc`).
- **Tables (migration `<next free ordinal>_FeedBatches`):** `FeedBatch(TenantId, BatchId PK-part, Epoch, Generation, FromSequenceExclusive, Watermark, PageCount, ContentHash char(64), State smallint (Building=0, Ready=1, Retired=2), IsFullSnapshot bool, CreatedUtc, RetiredUtc?)` with unique `(TenantId, Epoch, FromSequenceExclusive, Watermark, IsFullSnapshot)` (content-address); `FeedBatchPage(TenantId, BatchId, PageOrdinal, PageHash char(64), SizeBytes int, PRIMARY KEY(TenantId,BatchId,PageOrdinal))`. Indexes lead with `TenantId`. Non-syncable; `"Tenant"` filter applies.
- **Retention & GC (`FeedRetentionService`, invoked by the S13 worker, never inline):** `JournalRetentionDays = OfflineWindowOptions.MaxOfflineDays (90) + 30 = 120` (≥ the longest supported offline window; **the same constant sizes S06's intake retention** — one source `RetentionPolicy`); GC step order (**crash-atomic**): (a) `UPDATE FeedBatch SET State=Retired, RetiredUtc` for batches older than 24 h that are not the newest Ready per `(Tenant, Epoch)`; (b) delete page **files** of Retired batches; (c) `DELETE` page metadata then batch rows of Retired batches whose files are gone; (d) in one transaction delete `TenantJournal` rows with `ChangeSequence <= cutoffSeq` **and** set `TenantFeedState.OldestRetainedSequence = cutoffSeq` (**never delete journal rows without advancing the floor in the same transaction**); (e) orphan sweep: files with no batch row older than 1 h. Every step is idempotent; a crash between any two steps re-runs safely (named tests below). A client behind `OldestRetainedSequence` is told `ResyncRequired(BEYOND_RETENTION)` — never served a partial delta.
- **Full resync (`POST /sync/snapshots`):** builds a batch of **all** current catalog rows for the tenant (no journal range) frozen the same way (watermark from the snapshot), `IsFullSnapshot=true`; the client builds a **shadow generation** from it (G04) while preserving pending intent. Bumping `Generation` is by `IFeedEpoch`/restore procedures, not by this endpoint.
- **Mixed-fleet (ARCH §8.6):** supported contract versions {1,2} (S06 `WireContract.IsSupported`); the server maps by explicit per-version serializer (`SyncSerializerV2`; V1 clients get the V2 shape because sync DTOs were introduced at v2 — a v1 client never calls `/sync`: `426`). `RuleSetVersion` concerns commands only (S06).
- **Layer 3 isolation (ARCH §9.2):** the freezer reads **only through the tenant-scoped context**; `SyncIsolationTests` (PG-gated) prove a batch for tenant A contains zero tenant-B rows even with identical entity ids.
- **Observability hooks (used by S18):** `animora.sync.batch.build.duration`, `…pages`, `…bytes`, `animora.pg.snapshot.age.seconds` (oldest open RR snapshot) via `System.Diagnostics.Metrics`; a stuck snapshot is a **health warning**, not silent.
- **New ErrorCodes (+2, region "Sync feed"):** `ResyncRequired` = `new("RESYNC_REQUIRED", ErrorCode.Conflict)`, `BatchNotFound` = `new("BATCH_NOT_FOUND", ErrorCode.NotFound)`.

## §Implementation plan

1. Add DTOs in `Animora.Contracts.Wire/Sync/*` (manifest, open/response, page, record, per-entity contracts in `Contracts/*SyncContract.cs`, one per `SyncContractCatalog` entry; derive the list from the catalog at execution time and add a test that every catalog entry has exactly one `<Entity>SyncContract`).
2. Persistence: `FeedBatch`/`FeedBatchPage` entities + configuration + immutability trigger; `IBackgroundAdmission` interface (here) + default `SemaphoreBackgroundAdmission(1)`; `BatchFreezer`, `FeedPageWriter` (temp+atomic move), `FeedPageReader`, `RecordProjector` (catalog → `SyncRecord`, child collections inlined), `FeedRetentionService`, `RetentionPolicy` (single source: `MaxOfflineDays`, `JournalRetentionDays = MaxOfflineDays + 30`, shared with S06).
3. Migration: `.sandbox/tools/dotnet-ef migrations add FeedBatches --project src/Animora.Persistence.PostgreSql --startup-project src/Animora.Persistence.PostgreSql --context AnimoraServerDbContext --output-dir Migrations/Server`; rename to the next free ordinal; add the immutability trigger; update `ServerSchemaVersion.ExpectedHead`.
4. Server: `Sync/SyncEndpoints.cs` under `ServerRoutes.Sync`; register `FeedRetentionService` + `BatchFreezer` in DI; ErrorCodes +2.
5. Tests (below); `scripts/sandbox/build.sh full` → 0/0; `scripts/sandbox/test.sh Server` → green; `scripts/sandbox/run.sh` → `SMOKE RUN OK`.
6. `PROGRESS.md` per recipe; commit.

## Files to create / modify

| Path | Contents |
|---|---|
| `src/Animora.Contracts.Wire/Sync/*.cs` | manifest/open/page/record + `<Entity>SyncContract` per catalog entry |
| `src/Animora.Domain/Results/ErrorCodes.cs` (edit) | +2 codes |
| `src/Animora.Persistence.PostgreSql/Replication/{BatchFreezer,FeedPageWriter,FeedPageReader,RecordProjector,FeedRetentionService,RetentionPolicy,IBackgroundAdmission}.cs`, `Configurations/FeedBatch*.cs`, `Migrations/Server/*FeedBatches*` | export + retention + migration |
| `src/Animora.Server/Sync/SyncEndpoints.cs`, `Program.cs` (edit) | `/sync/*` |
| `tests/Animora.Server.Tests/Sync/*.cs` | tests |

## Contracts / schema added

Tables `FeedBatch`, `FeedBatchPage`; files under `DataRootPath/feed/…`. Wire: `SyncManifest`, `OpenBatchRequest/Response`, `SyncPage`, `SyncRecord`, `<Entity>SyncContract`. Endpoints `GET /sync/manifest`, `POST /sync/batches`, `GET /sync/batches/{id}/pages/{n}`, `POST /sync/snapshots`. Constants: page ≤ 262144 bytes, `MaxSnapshotLifetime` 10 s, `JournalRetentionDays` 120. ErrorCodes +2.

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

- Client staging, atomic apply, shadow-generation switch, checkpoint store → **G02–G04**. Client orchestration/jitter/backoff → **G03**.
- SignalR "feed advanced" hint + worker scheduling of GC → **S13**. Files transfer → **S17**. Capacity numbers → **G08**.
- Datasync `/tables/*` adapter → deferred (ROADMAP §5, G1 gate).

## Acceptance criteria (mechanical)

1. `scripts/sandbox/build.sh full` = 0/0; `scripts/sandbox/run.sh` = `SMOKE RUN OK`; client sources unchanged.
2. Pure tests pass: `ContractsTests.NoSyncContract_HasTenantIdProperty`, `ContractsTests.EveryCatalogEntry_HasExactlyOneSyncContract`, `PageWriterTests.PagesNeverExceed256KiB_Uncompressed`, `PageWriterTests.TempFileThenAtomicMove`, `RecordProjectorTests.ProjectionOrder_ParentsFirst_DeletesLast`, `RetentionPolicyTests.JournalRetention_Is_OfflineWindowPlus30`, `RetentionPolicyTests.SameConstantSizesCommandIntakeRetention`, `OpenBatchDecisionTests.EpochChanged_BeyondRetention_UpToDate_Ready`, `GcOrderTests.EachStep_Idempotent_CrashBetweenSteps_Safe` (fake fs/db), `SyncEndpointsTests.ContractVersionUnsupported_Returns426`, `SyncEndpointsTests.ForeignBatchId_Returns404_Audited`.
3. PG-gated, **mandatory in G07**: `BatchFreezerTests.WatermarkAndRows_FromOneSnapshot`, `BatchFreezerTests.SnapshotCommitted_BeforePageMaterialization`, `BatchFreezerTests.SameRange_SameBatchId_ByteStable`, `BatchFreezerTests.SnapshotOverMaxLifetime_AbortsAndRetries`, `GcTests.JournalDeletion_AdvancesFloor_SameTransaction`, `GcTests.CrashAfterFileDelete_BeforeRowDelete_Recovers`, `SyncIsolationTests.TenantA_Batch_ContainsNoTenantB_Rows`, `SyncFlowTests.OpenDownloadResume_FromLastPage`.
4. `grep -rn "ToListAsync()" src/Animora.Persistence.PostgreSql/Replication` returns nothing (paged/streamed only); `grep -rn "IsolationLevel.RepeatableRead" src/Animora.Persistence.PostgreSql/Replication` matches only `BatchFreezer`.
5. `ls src/Animora.Persistence.PostgreSql/Migrations/Server | grep -c FeedBatches` ≥ 1 (next free ordinal); ErrorCodes grep: `grep -c "RESYNC_REQUIRED\|BATCH_NOT_FOUND" src/Animora.Domain/Results/ErrorCodes.cs` = 2.

## PROGRESS.md update

At the end of this phase: set the `S12` row in "Phase status" to `✅ Done` (add date + one-line summary), advance **"Next phase" → `S13`**,
append one `### S12` entry to "Repo state log" (what exists now: projects, tables/migration ordinal, error codes added, test classes), log any
deviation in the "Decision & deviation log", then commit `feat(server): S12 immutable batch export` and sync per the repo git workflow.
