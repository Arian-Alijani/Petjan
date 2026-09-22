# Phase D12 — Files: valet key + resumable transfer

**Band:** M · **Track:** D · **Depends on:** D09 · **Satisfies:** R-B26
**Runnable after this phase:** desktop offline yes · server required no

## Read before starting
- `backend-roadmap/CONVENTIONS.md` §2, §3, §4, §5, §7, §8, §9, §12
- `backend-roadmap/PROGRESS.md` (D09 entry — `IOutboxConsumer`, `ConsumedMessages`,
  `WorkerBulkhead`, `WorkerOptions`, `OperatorAlert`, `ServerResilience`; D05 entry —
  `IServerUnitOfWork`; D04 entry — `HttpTenantContext`, `ITenantContextSetter`,
  `ResourceAuthorizationService`; D02 entry — `AnimoraServerDbContext`, `ServerModel`,
  `RowVersionCodec`; D01 entry — `ServerOptions.DataRootPath`, 32 MB body limit; A04 entry —
  `DocumentReference`; A11 entry — `ILocalFileStore`/`LocalFileStore` content addressing)
- `ARCHITECTURE.md` §14 (all), §10.8, §19 gate T · `STACK.md` §16 (storage facts only)
- `A-RULE 020`, `A-RULE 025`, `A-RULE 026`, `A-RULE 033`
- `src/Animora.Domain/Patients/DocumentReference.cs`,
  `src/Animora.Server/Configuration/ServerOptions.cs`

## Goal
Server-side file storage becomes real and safe: an explicit `Staged → Uploaded → HashVerified →
Committed → Available` lifecycle across the two consistency domains, a chunked resumable transfer API
with idempotent completion, Valet-Key access grants (a path is never a credential), and a
reconciliation worker with one deterministic outcome per divergence class.

## Design decisions (pre-made — implement, do not re-derive)
- **Two consistency domains, explicitly named** (`ARCHITECTURE.md` §14.1): the SQL metadata row
  (`StoredFile`) and the bytes on disk. Nothing outside `Available` is readable by a client; the
  lifecycle is the only bridge and it is one-directional.
- **`StoredFile` entity** (`src/Animora.Persistence.SqlServer/Files/StoredFile.cs`, migration
  `110_FileStore`), tenant-scoped (so D02's `Tenant` filter applies):
  `Id` (ULID) · `TenantId` · `FileLifecycleState State` · `OriginalFileName` · `ContentType`
  (validated against `FileOptions.AllowedContentTypes`) · `DeclaredSizeBytes` ·
  `ReceivedSizeBytes` · `DeclaredSha256` · `ComputedSha256?` · `StorageRelativePath?` ·
  `UploadSessionId?` · `CreatedByUserId` · `CreatedAtUtc` · `CommittedAtUtc?` ·
  `AttachedToType?` / `AttachedToId?` (e.g. `Pet`/`Visit`) · `Deleted` · `RowVersion`.
  `enum FileLifecycleState { Staged, Uploaded, HashVerified, Committed, Available, Quarantined, Failed }`
  — `Quarantined`/`Failed` are terminal diagnostic states, never silently reused.
  Transitions are an allow-table in `src/Animora.Domain/Files/FileLifecycle.cs`
  (Domain owns the vocabulary, same pattern as `SettlementTransitions`):
  ```
  Staged       -> Uploaded | Failed
  Uploaded     -> HashVerified | Failed | Quarantined
  HashVerified -> Committed | Quarantined
  Committed    -> Available | Quarantined
  Available    -> Quarantined        (never back to an earlier state)
  ```
  `static Result EnsureAllowed(FileLifecycleState from, FileLifecycleState to)` →
  `FILE_INVALID_TRANSITION`. **`HashVerified` gates `Committed`** — a mismatch can never reach
  `Available` (§14.1 invariant).
- **`UploadSession` entity** (same migration): `Id` (ULID) · `TenantId` · `StoredFileId` ·
  `ChunkSizeBytes` · `TotalChunks` · `ReceivedChunkMask` (`byte[]`, one bit per chunk) ·
  `ExpiresAtUtc` · `CompletedAtUtc?` · `CreatedByUserId` · `CreatedAtUtc`.
  A bitmask (not a child table) keeps resume state in one row, so a resume is a single read.
  Indexes: `IX_UploadSessions_ExpiresAtUtc` (the sweep), `UX_UploadSessions_StoredFileId`.
- **On-disk layout, content-addressed after verification, session-addressed before**
  (`FileOptions.RootPath`, defaulting to `<ServerOptions.DataRootPath>/files`):
  ```
  staging/<uploadSessionId>/<chunkIndex>.part      during transfer
  content/<sha256[0..1]>/<sha256[2..3]>/<sha256>   after HashVerified (two-level fan-out)
  quarantine/<yyyyMMdd>/<storedFileId>             on any divergence
  ```
  Committing = an atomic `File.Move` from staging to `content/…` **after** the hash matches, then the
  metadata update in a transaction. If the same `sha256` already exists, the move is skipped and the
  existing blob is referenced (natural de-duplication); a test covers this.
- **Chunked resumable transfer API** (`src/Animora.Server/Endpoints/FileEndpoints.cs`, prefix
  `/api/files`, every route authenticated + tenant-scoped, all failures `WireApiError`):
  | Route | Body / result | Notes |
  |---|---|---|
  | `POST /api/files/uploads` | `BeginUploadRequestV1(OriginalFileName, ContentType, SizeBytes, Sha256, AttachedToType?, AttachedToId?)` → `BeginUploadResultV1(StoredFileId, UploadSessionId, ChunkSizeBytes, TotalChunks, MissingChunks)` | creates `StoredFile(Staged)` + session; **idempotent** per `(TenantId, DeclaredSha256, DeclaredSizeBytes, CreatedByUserId)` while a session is unexpired → returns the existing session and its `MissingChunks`, which is what makes resume trivial |
  | `PUT /api/files/uploads/{sessionId}/chunks/{index}` | raw bytes → `ChunkAckV1(Index, ReceivedSizeBytes, MissingChunks)` | writing an already-received chunk is a **no-op ack** (A-RULE 025); out-of-range index → `FILE_CHUNK_OUT_OF_RANGE` |
  | `POST /api/files/uploads/{sessionId}/complete` | → `FileStatusV1(StoredFileId, State, Sha256, SizeBytes)` | runs `Uploaded → HashVerified → Committed → Available`; **idempotent** — a second call on an `Available` file returns the same result without re-hashing |
  | `DELETE /api/files/uploads/{sessionId}` | → `204` | cancels: deletes staging chunks, marks the `StoredFile` `Failed` |
  | `POST /api/files/{fileId}/grants` | `GrantRequestV1(TtlSeconds?)` → `FileAccessGrantV1(GrantToken, ExpiresAtUtc)` | the Valet Key; authorization happens **here**, once |
  | `GET /api/files/content?grant=<token>` | the bytes (streamed) | the only download route; takes **no** file id and **no** path |
  `FileOptions`: `ChunkSizeBytes = 1_048_576` (1 MiB), `MaxFileSizeBytes = 64 * 1024 * 1024`,
  `SessionTtlMinutes = 120`, `GrantTtlSeconds = 300`, `MaxGrantTtlSeconds = 900`,
  `AllowedContentTypes` (pdf, png, jpeg, webp, plain text, csv). The chunk route raises its own body
  limit to `ChunkSizeBytes + 4 KiB` via `[RequestSizeLimit]`, leaving D01's global 32 MB alone.
- **Valet Key, implemented with Data Protection, not with a path** (A-RULE 020, §10.8):
  `src/Animora.Server/Files/FileAccessGrantService.cs` protects a
  `sealed record FileAccessGrant(string FileId, string TenantId, string UserId, string Sha256,
  DateTimeOffset ExpiresAtUtc, string Purpose)` with
  `IDataProtectionProvider.CreateProtector("Animora.Files.Grant.v1")` and a `TimeLimitedDataProtector`
  for the TTL. `GET /api/files/content` unprotects the token, re-checks
  `grant.TenantId == ambient tenant` **and** that the file is `Available`, then streams from
  `content/…`. The relative path exists **only** inside the server process — it never appears in a
  response body, a log line, or a grant payload. A test greps every response DTO and log template for
  `StorageRelativePath` and fails if it is projected anywhere.
- **Authorization is resource-based, once, at grant time**: `FileAccessRule : IResourceAuthorizationRule<StoredFile>`
  (registered through D04's `AuthorizationPolicies`) requires same-tenant plus the read permission of
  the attached aggregate (`AttachedToType` → permission code map, one `FrozenDictionary`), so a
  receptionist cannot pull a financial attachment. The download route performs **no** business
  authorization — the grant already is the decision, which is the entire point of the pattern.
- **Reconciliation worker — one deterministic outcome per divergence class** (§14.2, R-B26):
  `src/Animora.Server/Workers/FileReconciliationWorker.cs`, hourly, under D09's `WorkerBulkhead`:
  | Divergence | Detection | Deterministic outcome |
  |---|---|---|
  | Row `Available`, blob missing | metadata scan vs. `File.Exists` | → `Quarantined` + `OperatorAlert` `file.blob.missing` (never delete the row) |
  | Blob with no row | content-dir scan vs. metadata | after `OrphanGraceHours` (24) → moved to `quarantine/` (garbage-collect, never unlink) |
  | Incomplete upload, session expired | `UploadSession.ExpiresAtUtc < now`, not completed | staging chunks deleted, `StoredFile` → `Failed` |
  | Row `Committed` but not `Available` | interrupted finalization | re-verify hash → `Available`, or → `Quarantined` on mismatch (**resume**) |
  | Row `HashVerified`, blob still in staging | crash between move and update | redo the move idempotently → `Committed` → `Available` |
  | Hash mismatch found on re-verify | periodic sampled re-hash (`ReverifySamplePerPass` = 20) | → `Quarantined` + alert `file.hash.mismatch` |
  Every pass logs one structured line with the per-class counts; **nothing is ever silently
  deleted** — quarantine is the terminal action for anything ambiguous. Each step is resumable and
  idempotent (A-RULE 026): the worker claims nothing and holds no in-memory state between files.
- **Attachment is not a file operation.** A `StoredFile` becoming `Available` enqueues
  `file.available` through `IServerOutbox` in the same transaction; `FileAttachmentConsumer`
  (`IOutboxConsumer`, D09's shape + `ConsumedMessages` guard) writes/updates the
  `DocumentReference` row for `AttachedToType`/`AttachedToId`. So clinical metadata never depends on
  the transfer completing synchronously, and the two domains stay decoupled.
- **Files never travel the replication pipeline** (§14.3 invariant): `StoredFiles` and
  `UploadSessions` are added to `ServerModel.NonReplicatedProjectionTables`. `DocumentReference`
  **is** replicated (D08 already lists it) — the client learns *that* a document exists and requests
  a grant when it wants the bytes. A test asserts no `TableController` exists for `StoredFile`.
- **Retention:** D09's `RetentionWorker` gains `UploadSessions` (completed/expired, older than
  `RetentionDays`) to its allow-list. `StoredFiles` and quarantined blobs are **never** purged
  automatically — the allow-list test from D09 is extended to prove it.
- **New error codes:** `FILE_INVALID_TRANSITION`, `FILE_TOO_LARGE`, `FILE_TYPE_NOT_ALLOWED`,
  `FILE_HASH_MISMATCH`, `FILE_CHUNK_OUT_OF_RANGE`, `FILE_SESSION_EXPIRED`, `FILE_NOT_AVAILABLE`,
  `FILE_GRANT_INVALID`, `FILE_GRANT_EXPIRED`.

## Files to create / modify
| Path | Action | Contents (one line) |
|---|---|---|
| `src/Animora.Domain/Files/FileLifecycle.cs` | create | state enum + transition allow-table |
| `src/Animora.Contracts.Wire/Files/*.cs` | create | `BeginUploadRequestV1`, `BeginUploadResultV1`, `ChunkAckV1`, `FileStatusV1`, `GrantRequestV1`, `FileAccessGrantV1` |
| `src/Animora.Persistence.SqlServer/Files/StoredFile.cs` | create | metadata entity |
| `src/Animora.Persistence.SqlServer/Files/UploadSession.cs` | create | session + chunk bitmask |
| `src/Animora.Persistence.SqlServer/Files/StoredFileConfiguration.cs` | create | 2 EF configurations + indexes |
| `src/Animora.Persistence.SqlServer/Migrations/110_FileStore*` | create | 2 tables |
| `src/Animora.Persistence.SqlServer/Context/ServerModel.cs` | modify | add both to non-replicated |
| `src/Animora.Server/Configuration/FileOptions.cs` | create | sizes, TTLs, allowed types |
| `src/Animora.Server/Files/FileStorageLayout.cs` | create | staging/content/quarantine path rules |
| `src/Animora.Server/Files/FileUploadService.cs` | create | begin / chunk / complete, idempotent |
| `src/Animora.Server/Files/FileHashVerifier.cs` | create | streaming SHA-256 over chunks |
| `src/Animora.Server/Files/FileAccessGrantService.cs` | create | Valet-Key issue/verify |
| `src/Animora.Server/Files/FileAccessRule.cs` | create | resource authorization at grant time |
| `src/Animora.Server/Endpoints/FileEndpoints.cs` | create | the 6 routes |
| `src/Animora.Server/Consumers/FileAttachmentConsumer.cs` | create | `file.available` → `DocumentReference` |
| `src/Animora.Server/Workers/FileReconciliationWorker.cs` | create | the 6 divergence classes |
| `src/Animora.Server/Workers/RetentionWorker.cs` | modify | add `UploadSessions` to the allow-list |
| `src/Animora.Server/Program.cs` | modify | register options, services, endpoints, worker, consumer |
| `tests/Animora.Domain.Tests/FileLifecycleTests.cs` | create | allow-table, `HashVerified` gates `Committed` |
| `tests/Animora.Server.Tests/FileUploadResumeTests.cs` | create | resume, duplicate chunk, idempotent complete |
| `tests/Animora.Server.Tests/FileValetKeyTests.cs` | create | no path leaks, grant expiry, cross-tenant denial |
| `tests/Animora.Server.Tests/FileReconciliationTests.cs` | create | one outcome per divergence class (gate T) |

## Implementation steps
1. Add `FileLifecycle` (Domain) and the wire records.
2. Add `StoredFile`, `UploadSession`, their configuration, migration `110_FileStore`, and the
   `ServerModel` non-replicated entries.
3. Write `FileOptions` + `FileStorageLayout` (every path in the product comes from this one type).
4. Write `FileUploadService`: begin (idempotent per declared hash), chunk write with bitmask update,
   complete = verify → move → commit → available → outbox `file.available`, all transitions through
   `FileLifecycle.EnsureAllowed` inside `IServerUnitOfWork.ExecuteAsync`.
5. Write `FileHashVerifier` streaming over the chunk files (never loading the whole file in memory —
   CONVENTIONS §8).
6. Write `FileAccessGrantService` + `FileAccessRule`; map `FileEndpoints`.
7. Write `FileAttachmentConsumer`.
8. Write `FileReconciliationWorker` with one private method per divergence class and a single
   structured summary log line per pass.
9. Extend `RetentionWorker`'s allow-list and D09's `RetentionSafetyTests` accordingly.
10. Write the four test classes; `FileReconciliationTests` **is** `ARCHITECTURE.md` §19 gate T.
11. `build.sh full` = 0/0; `test.sh` = `TESTS OK`; `run.sh` = `SMOKE RUN OK`.

## Contracts / schema added
- Domain: `FileLifecycleState`, `FileLifecycle`.
- Wire: `BeginUploadRequestV1`, `BeginUploadResultV1`, `ChunkAckV1`, `FileStatusV1`,
  `GrantRequestV1`, `FileAccessGrantV1`. `WireContract.Version` stays `1`.
- Persistence.SqlServer: `StoredFile`, `UploadSession`, 2 configurations, migration `110_FileStore`,
  `ServerModel` extended.
- Server: `FileOptions`, `FileStorageLayout`, `FileUploadService`, `FileHashVerifier`,
  `FileAccessGrantService`, `FileAccessGrant`, `FileAccessRule`, `FileEndpoints` (6 routes),
  `FileAttachmentConsumer`, `FileReconciliationWorker`.
- Outbox message type: `file.available`. Alert types: `file.blob.missing`, `file.hash.mismatch`.
- `ErrorCodes` += 9. No `Animora.Contracts` change, no `CommandTypes` entry, no DI change in
  `App.axaml.cs`.

## Rules that apply here
- **R-B26 / §14.1** — the five-state lifecycle is mandatory and ordered; `HashVerified` gates
  `Committed`; a mismatch can never reach `Available`.
- **§14.2** — reconciliation is a job, not a hope: six named divergence classes, each with exactly
  one deterministic outcome, nothing silently deleted.
- **§14.3** — file bytes never enter the replication pipeline; no `TableController` for `StoredFile`.
- **A-RULE 020 / §10.8** — a path is never a credential; authorization happens once at grant time and
  `StorageRelativePath` is never projected into a response or a log.
- **A-RULE 025** — re-sending a chunk and re-calling `complete` are no-ops; the attachment consumer
  is guarded by `ConsumedMessages`.
- **A-RULE 026** — every lifecycle step and every reconciliation step is resumable after a crash
  without duplicating an effect.
- **A-RULE 033** — grants embed tenant + user + file + version scope; a grant from another tenant
  fails even if the token is intact.
- **CONVENTIONS §8** — hashing and streaming are chunked; no whole-file buffering; the reconciliation
  worker runs under the bulkhead.

## Out of scope
- Client-side document upload/download UI and the desktop's use of grants → E07 uses the same
  endpoints; screen work stays within C-phase contracts (`DocumentReference` is already surfaced).
- Antivirus/content scanning — no such technology is in `STACK.md`; quarantine is the only
  containment mechanism here.
- Backup/restore of the file store as a rehearsed runbook → E09.
- Gate T's formal recording alongside the other gates → E08.
- Object storage / CDN — permanently excluded (`STACK.md` §16, §17).

## Acceptance criteria
- Build: `scripts/sandbox/build.sh full` = 0 warnings / 0 errors.
- `FileLifecycleTests` green: every allowed transition passes, every other pair returns
  `FILE_INVALID_TRANSITION`; `Committed` is unreachable without passing `HashVerified`;
  `Available → Uploaded` is impossible.
- `FileUploadResumeTests` green (or skipped with reason): a 5-chunk upload interrupted after chunk 3
  resumes from `MissingChunks = [3,4]` after re-calling `POST /uploads` with the same declared hash;
  re-sending chunk 1 acks without changing `ReceivedSizeBytes`; calling `complete` twice yields the
  same `FileStatusV1` and exactly one `file.available` outbox message; a corrupted chunk makes
  `complete` fail `FILE_HASH_MISMATCH` and the file never reaches `Available`; two uploads of
  identical content share one blob.
- `FileValetKeyTests` green: `GET /api/files/content` without a grant returns `FILE_GRANT_INVALID`;
  with an expired grant `FILE_GRANT_EXPIRED`; with another tenant's grant a `403`; a receptionist's
  grant request for a financial attachment is denied; **no response body or log template contains
  `StorageRelativePath`** (asserted).
- `FileReconciliationTests` green — **gate T**: each of the six divergence classes is constructed and
  produces exactly its tabled outcome; the worker deletes no `StoredFile` row and no quarantined
  blob; a second pass over the same state is a no-op.
- `RetentionSafetyTests` (D09, extended) still green: only completed/expired `UploadSessions` are
  purged; `StoredFiles` and quarantine are untouched.
- Desktop unaffected: `scripts/sandbox/run.sh` = `SMOKE RUN OK`.

## PROGRESS.md update
Mark D12 done; advance "Next phase" to `D13`; append a "Repo state" entry listing: `FileLifecycle`
(**the only transition authority**), `StoredFile` + `UploadSession` + the chunk bitmask (migration
`110_FileStore`), `FileStorageLayout`'s three directories, the six `/api/files` routes and their
idempotency keys (**E07 consumes them**), `FileOptions` numbers, `FileAccessGrantService` as the
Valet-Key implementation and the "path never leaves the process" rule, `FileAccessRule`,
`FileAttachmentConsumer` (`file.available` → `DocumentReference`), `FileReconciliationWorker`'s six
divergence classes and their outcomes, the two new alert types, the `RetentionWorker` allow-list
addition, the gate T test name, the 9 new error codes, deviations.
