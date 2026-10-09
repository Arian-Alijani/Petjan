# Phase S17 — Files — content-addressed storage, valet-key access, resumable transfer & reconciliation

**Band:** M · **Track:** S (server) · **Depends on:** S13 · **Next:** S18
**Satisfies:** R-B26 · **Runnable after this phase:** desktop offline yes · server required no
**Produced by:** M00 (regenerated 2026-10-08; supersedes the deleted `PHASE-D12` file).

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
- `ARCHITECTURE.md` §14 and §17.3 (file content is part of the backup set) — only these `§`.
- `STACK.md` §16 — only this `§`.
- `src/Animora.Domain/Patients/DocumentReference.cs` (the aggregate that stores a FileId; mapped by S07).
- `src/Animora.Server/Workers/*` (S13 worker pattern) and `src/Animora.Server/Authorization/*` (S05).

(Nothing else. `Animora-UI/**`, `Database-Sampel/**`, other phase files and `windows-frontend-roadmap/phases/**` are out of bounds.)

## Goal

Implement the server file plane: SQL metadata + **binary content on the server filesystem, content-addressed by SHA-256**, the mandatory lifecycle `Staged → Uploaded → HashVerified → Committed → Available`, **Valet-Key** (short-lived, scoped, tamper-proof grant — a path is never a credential) authorising every transfer, resumable chunked upload/download, a `DocumentReference`↔`FileId` link, and the reconciliation job (GC of abandoned staged content, metadata/content mismatch flagging).

---

## §Reference pack

```csharp
namespace Animora.Contracts.Wire.Errors;   // (A01)
public sealed record WireApiError(string Code, string? Detail, IReadOnlyList<WireFieldError> Fields, string CorrelationId);
public sealed record WireFieldError(string Field, string Code);
namespace Animora.Contracts.Wire;
public static class WireContract { public const int Version = 1; public const string HeaderName = "X-Animora-Contract-Version"; }
``````csharp
// Animora.Domain.Results (A02) — reuse, never redefine
public readonly record struct ErrorCode(string Code, string Category)   // categories: Validation|NotFound|Conflict|Forbidden|Integrity|Infrastructure|Entitlement
{ public const string Validation="Validation", NotFound="NotFound", Conflict="Conflict", Forbidden="Forbidden", Integrity="Integrity", Infrastructure="Infrastructure", Entitlement="Entitlement";
  public static readonly ErrorCode None; public bool IsNone { get; } }
public readonly record struct Result      { bool IsSuccess, IsFailure; ErrorCode Error; string? Detail; static Result Ok(); static Result Fail(ErrorCode e, string? detail = null); }
public readonly record struct Result<T>   { bool IsSuccess, IsFailure; T? Value; ErrorCode Error; string? Detail; static Result<T> Ok(T v); static Result<T> Fail(ErrorCode e, string? detail = null); Map/Bind/OrElse; implicit operator Result<T>(T) }
// ErrorCodes (161 today): `public static readonly ErrorCode X = new("X_CODE", ErrorCode.<Kind>);` — inside `#region <context>`. Server phases ADD a new #region; never rename/remove.
// Already declared & reusable: ValidationFailed, NotFound, ConcurrencyConflict("CONCURRENCY_CONFLICT",Conflict), PermissionDenied, PermissionUnknown, TenantMismatch("TENANT_MISMATCH",Forbidden),
//   IdempotencyFingerprintMismatch(Integrity), DependencyUnsettled("DEPENDENCY_UNSETTLED",Conflict), EntitlementRequired(Entitlement), TenantSuspended, UserDisabled, UserLockedOut, DeviceRevoked,
//   CredentialsInvalid, SessionExpired, PasswordPolicyViolation, SettlementInvalidTransition, CommandAlreadySettled, HandlerNotRegistered, DispatchFailed, Unexpected
```
```csharp
// S13 worker pattern: BackgroundService + PeriodicTimer + WorkerHeartbeat + IBackgroundAdmission (SemaphoreSlim(1)); S12: RetentionPolicy / DataRootPath layout `DataRootPath/feed/…` — files use `DataRootPath/files/…` (disjoint)
// S05: ITenantContext, policies via PermissionCodes (use `PatientsWrite` for upload of patient documents, `PatientsRead` for download — DocumentReference belongs to the patients context)
// S01: RateLimitPolicies.Files ("files"), ServerRoutes (add "/files")
// Domain.Patients.DocumentReference (A04): a SyncableEntity referencing a stored file by FileId (never a path) — mapped by S07 as table DocumentReferences
```

## §Design decisions

- **Storage layout:** `DataRootPath/files/staging/<TenantId>/<UploadId>.part` (in-progress), `DataRootPath/files/cas/<sha[0..2]>/<sha[2..4]>/<sha256>` (committed content, **content-addressed, tenant-agnostic bytes**; **access is always tenant-scoped through metadata**: two tenants uploading identical bytes share one blob but each has its own `FileObject` row — blob deletion is reference-counted by `BlobRef`). Writes go temp → `fsync` → atomic `File.Move`; directory fan-out keeps ≤ 256 entries per level.
- **Tables (migration `<next free ordinal>_Files`):** `FileObjects(TenantId, Id varchar(26), Sha256 char(64)?, SizeBytes bigint, DeclaredSizeBytes bigint, ContentType varchar(128), OriginalName varchar(256), State smallint (Staged=0, Uploaded=1, HashVerified=2, Committed=3, Available=4, Abandoned=5, Mismatch=6), CreatedUtc, UpdatedUtc, CreatedByUserId, UploadId varchar(26)?, PRIMARY KEY (TenantId, Id))` (**not** `SyncableEntity` — file bytes/metadata are not on the state feed; `DocumentReference` (replicated) carries `FileId`; the content is fetched on demand with a valet key — ARCH §14); `FileUploadChunks(TenantId, UploadId, Offset bigint, Length int, PRIMARY KEY(TenantId,UploadId,Offset))` (resume bookkeeping), `BlobRefs(Sha256 char(64) PK, RefCount int, SizeBytes bigint, FirstSeenUtc)`. Indexes `IX_FileObjects_TenantId_State_UpdatedUtc`, `IX_FileObjects_TenantId_Sha256`.
- **Lifecycle (state machine `FileLifecycle.EnsureAllowed(from,to)`, table-driven, tested):** `Staged→Uploaded` (all declared bytes present) → `HashVerified` (server streams the staged file once computing SHA-256; client-declared hash, if any, must match, else `Mismatch` + `FILE_HASH_MISMATCH`) → `Committed` (content moved into CAS; `BlobRef` incremented in the same transaction as the state change) → `Available` (set when the owning metadata is visible: immediately after Committed; kept as a distinct state so reconciliation can detect a Committed row whose blob is absent). `Staged|Uploaded → Abandoned` by reconciliation after **24 h** without activity. `Mismatch` is terminal until an operator action (S20).
- **Valet-Key (A-RULE 020):** `ValetKey` = `ITimeLimitedDataProtector` (Data Protection key ring from S02, purpose `"Animora.Files.Valet.v1"`) protecting the canonical JSON `{ v:1, tid, uid, did, fid, op:"upload"|"download", max:long, exp }`; **TTL 5 min**, bound to tenant+user+device+file+operation+max bytes; verified on every transfer request (`X-Animora-Valet` header) **in addition to** normal authN/authZ (the key narrows, never widens). Never contains a path; never logged (redaction list). Tampered/expired ⇒ `403 VALET_KEY_INVALID`. Issued by `POST /files/{id}/valet` (authorized for the matching permission) — rate-limit `files`.
- **Endpoints (`/files`, policy `files`, authorized + tenant + device active):** `POST /files` (declare: name, content-type, size ≤ `MaxFileBytes` = 64 MiB default, optional sha256 → creates `Staged` + `UploadId`, returns first valet key), `PUT /files/uploads/{uploadId}?offset=N` (chunk body ≤ 1 MiB; requires `Content-Range`-free explicit offset; server accepts only `offset == nextExpected` (or a re-send of an already-stored identical chunk ⇒ idempotent 204); out-of-order ⇒ `409 FILE_OFFSET_MISMATCH` with `X-Animora-Next-Offset`), `GET /files/uploads/{uploadId}` (resume status: next offset), `POST /files/uploads/{uploadId}/complete` (verify size + hash → Committed → Available; returns `FileId`), `GET /files/{id}` (download, valet `download`, supports `Range`, `Content-Disposition: attachment` always, `X-Content-Type-Options: nosniff`, `Cache-Control: private, no-store`), `DELETE /files/{id}` (soft: unreferenced check via `DocumentReferences`; `BlobRef.RefCount--`; blob removed by reconciliation at 0 after 24 h grace). Content-type allow-list `FileTypePolicy` (images, PDF, plain text, DICOM-less; executables rejected `FILE_TYPE_NOT_ALLOWED`); size from declared vs actual must match.
- **Document linking:** the existing S07 command that creates a `DocumentReference` (if any) carries a `FileId`; the server validates that `FileObjects` row is `Available` in the **same tenant** (`FILE_NOT_AVAILABLE`) inside the command transaction — done through a new port `IFileExistenceChecker` (Application port added here, implemented in Persistence.PostgreSql) so Application code stays free of EF. If no command creates `DocumentReference` today (verify with `grep -rn DocumentReference src/Animora.Application`) the checker is delivered unused and the link is exercised via the HTTP flow only — record in PROGRESS.
- **Reconciliation (`FileReconciliationWorker`, hourly, under `IBackgroundAdmission`, bounded 200 rows/pass):** (1) `Staged|Uploaded` idle > 24 h ⇒ `Abandoned` + delete `.part`; (2) `Committed|Available` rows whose CAS blob is missing ⇒ `Mismatch` + `files.reconcile.blob_missing` audit; (3) CAS blobs with `RefCount = 0` older than 24 h ⇒ delete file then `BlobRefs` row (crash-safe order: row first marked, file deleted, row removed — idempotent); (4) orphan files without any row older than 1 h ⇒ delete; (5) re-hash sampling of 20 random Available blobs/pass ⇒ mismatch flag (bit-rot detector). Each finding emits an `animora.files.reconcile.*` counter (S18) and an audit row.
- **Backup coupling (ARCH §17.3):** `FileStoreManifest` writer produces `files/manifest.sha256` (path-free list of `sha256 size`) used by G09's restore drill; this phase only exposes `IFileStoreManifest.WriteAsync`.
- **New ErrorCodes (+5, region "Files"):** `FileNotFound` `("FILE_NOT_FOUND",NotFound)`, `FileHashMismatch` `("FILE_HASH_MISMATCH",Integrity)`, `FileTypeNotAllowed` `("FILE_TYPE_NOT_ALLOWED",Validation)`, `FileTooLarge` `("FILE_TOO_LARGE",Validation)`, `ValetKeyInvalid` `("VALET_KEY_INVALID",Forbidden)`; transport-only strings `FILE_OFFSET_MISMATCH`, `FILE_NOT_AVAILABLE` live in `ServerErrorCodes`.

## §Implementation plan

1. Domain/Application: ErrorCodes +5; `Animora.Application.Ports.IFileExistenceChecker { Task<bool> IsAvailableAsync(string tenantId, string fileId, CancellationToken ct); }`.
2. Persistence: entities + configurations + `FileLifecycle`; migration: `.sandbox/tools/dotnet-ef migrations add Files --project src/Animora.Persistence.PostgreSql --startup-project src/Animora.Persistence.PostgreSql --context AnimoraServerDbContext --output-dir Migrations/Server`, rename to the next free ordinal, update `ServerSchemaVersion.ExpectedHead`.
3. Server: `Files/{FileStore (cas+staging, atomic writes), ValetKeyService, FileTypePolicy, FileEndpoints, FileReconciliationWorker, FileStoreManifest}.cs`; DI `AddAnimoraFiles()`; add `"/files"` to `ServerRoutes`; redaction list gains `X-Animora-Valet`; readiness probe `files-root` (writable + free space ≥ 256 MiB else degraded by name).
4. Tests (below). `scripts/sandbox/build.sh full` → 0/0; `scripts/sandbox/test.sh Server` → green; `scripts/sandbox/run.sh` → `SMOKE RUN OK`.
5. `PROGRESS.md` per recipe; commit.

## Files to create / modify

| Path | Contents |
|---|---|
| `src/Animora.Domain/Results/ErrorCodes.cs` (edit), `src/Animora.Application/Ports/IFileExistenceChecker.cs` | codes + port |
| `src/Animora.Persistence.PostgreSql/Files/*.cs`, `Configurations/File*.cs`, `Migrations/Server/*Files*` | metadata, lifecycle, checker, migration |
| `src/Animora.Server/Files/*.cs`, `Program.cs`/`ServerRoutes.cs` (edit) | storage, valet, endpoints, worker |
| `tests/Animora.Server.Tests/Files/*.cs` | tests |

## Contracts / schema added

Tables `FileObjects`, `FileUploadChunks`, `BlobRefs`; endpoints under `/files`; header `X-Animora-Valet`; ErrorCodes +5; port `IFileExistenceChecker`; limits `MaxFileBytes` 64 MiB, chunk ≤ 1 MiB, valet TTL 5 min, abandonment 24 h.

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

- Operator UI + the `Mismatch→Uploaded|Abandoned` operator transitions → **S20** (adds the two `FileLifecycle` edges). Client upload UX/queue and on-demand fetch → future desktop phase (document features are not part of this track; the transfer contract is ready) — record as a G-phase follow-up in the drift ledger (T9).
- Backup/restore execution and file-store restore parity → **G09/G07**. Antivirus scanning → not provided (zero-Internet; document in G09 runbook).
- Metrics export → **S18**.

## Acceptance criteria (mechanical)

1. `scripts/sandbox/build.sh full` = 0/0; `scripts/sandbox/run.sh` = `SMOKE RUN OK`.
2. `grep -rn "Path\b.*Valet\|filePath" src/Animora.Server/Files/ValetKeyService.cs` returns nothing (a path is never in a credential); `grep -rn "AWSSDK\|Azure.Storage\|Minio" src` returns nothing.
3. Pure tests pass: `FileLifecycleTests.AllowedAndForbiddenTransitions_Table`, `ValetKeyServiceTests.RoundTrip_BoundToTenantUserDeviceFileOp`, `ValetKeyServiceTests.Expired_Tampered_WrongTenant_WrongOp_Rejected`, `FileStoreTests.TempThenAtomicMove_NoPartialVisible`, `FileStoreTests.IdenticalBytes_TwoTenants_OneBlob_TwoRows_RefCounted`, `UploadFlowTests.OutOfOrderChunk_409_WithNextOffset`, `UploadFlowTests.ResendStoredChunk_Idempotent`, `UploadFlowTests.HashMismatch_ToMismatchState`, `FileTypePolicyTests.Executable_Rejected`, `DownloadTests.Headers_Attachment_NoSniff_NoStore`, `ReconciliationTests.Abandoned_BlobMissing_ZeroRef_Orphan_Bitrot_AreHandled`, `ReconciliationTests.GcOrder_CrashSafe_Idempotent`.
4. PG-gated (**mandatory G07**): `FileMetadataTests.CrossTenant_CannotDownload_ByGuessingId`, `FileMetadataTests.CommitAndRefCount_SameTransaction`.
5. `ls src/Animora.Persistence.PostgreSql/Migrations/Server | grep -c Files` ≥ 1 (next free ordinal); `grep -c "VALET_KEY_INVALID\|FILE_HASH_MISMATCH\|FILE_TYPE_NOT_ALLOWED\|FILE_TOO_LARGE\|FILE_NOT_FOUND" src/Animora.Domain/Results/ErrorCodes.cs` = 5.

## PROGRESS.md update

At the end of this phase: set the `S17` row in "Phase status" to `✅ Done` (add date + one-line summary), advance **"Next phase" → `S18`**,
append one `### S17` entry to "Repo state log" (what exists now: projects, tables/migration ordinal, error codes added, test classes), log any
deviation in the "Decision & deviation log", then commit `feat(server): S17 files` and sync per the repo git workflow.
