# Phase D12 — Files: valet key + resumable transfer

**Band:** M · **Track:** D · **Depends on:** D09 · **Satisfies:** R-B26
**Runnable after this phase:** desktop offline yes · server required no

## Read before starting
- `backend-roadmap/CONVENTIONS.md` §1, §2, §3, §4, §5, §7, §8, §9, §12
- `backend-roadmap/PROGRESS.md` (D09 entry — `IOutboxConsumer`, `ConsumedMessages` guard,
  `WorkerBulkhead`, `WorkerOptions`, `OperatorAlert`, `ServerResilience`; D05 entry —
  `IServerUnitOfWork`, the 200-for-settled policy; D04 entry — `AnimoraPolicies`,
  `ResourceAuthorizationService`, `ITenantContextSetter`; D02 entry — `AnimoraServerDbContext`,
  `ServerModel`; D01 entry — `ServerOptions.DataRootPath`, the 32 MB body limit, Data Protection
  key ring; A04 entry — `DocumentReference` + `DocumentKind`; A11 entry — `ILocalFileStore`)
- `ARCHITECTURE.md` §14 (all), §10.8, §17.3 · `STACK.md` §16
- `A-RULE 020`, `A-RULE 025`, `A-RULE 026`, `A-RULE 027`, `A-RULE 016`
- `src/Animora.Persistence.SqlServer/Context/**`, `src/Animora.Server/Security/**`

## Goal
Server-side file storage with the two consistency domains kept honest: an explicit
`Staged → Uploaded → HashVerified → Committed → Available` lifecycle, a **resumable chunked
transfer protocol**, **Valet-Key** access (a grant, never a path), and a **mandatory reconciliation
job** that resolves every DB↔filesystem divergence deterministically.

## Design decisions (pre-made — implement, do not re-derive)
- **Two consistency domains, never one transaction** (`ARCHITECTURE.md` §14.1). The SQL metadata row
  and the bytes on disk are written separately and reconciled; **no** code path pretends otherwise.
  The lifecycle enum is the contract:
  `enum FileState { Staged, Uploaded, HashVerified, Committed, Available, Quarantined, Failed }`
  in `Animora.Domain/Files/FileState.cs` with
  `FileStateTransitions.EnsureAllowed(from, to)` (the same static allow-table shape as B02's
  `SettlementTransitions` and D10's `SubscriptionTransitions`):
  ```
  Staged       -> Uploaded | Failed | Quarantined
  Uploaded     -> HashVerified | Failed | Quarantined
  HashVerified -> Committed | Quarantined
  Committed    -> Available | Quarantined
  Available    -> Quarantined            (never back to an earlier state)
  Quarantined  -> Available | Failed      (only after reconciliation proves the bytes)
  Failed       -> terminal
  ```
  **`HashVerified` gates `Committed`** — a hash mismatch can never reach `Available`
  (§14.1 invariant), enforced in the transition table *and* in `FileStore.CompleteAsync`.
- **Entities** (`src/Animora.Persistence.SqlServer/Files/`, migration `110_FileStore`):
  | Entity | Columns | Notes |
  |---|---|---|
  | `StoredFile` | `Id` (ULID PK), `TenantId`, `State` (`FileState`), `ContentHash` (`char(64)`, SHA-256 hex), `SizeBytes`, `ContentType`, `OriginalFileName`, `RelativePath`, `Kind` (`DocumentKind`, A04), `OwnerEntityType`, `OwnerEntityId`, `UploadedByUserId`, `DeviceId`, `CreatedAtUtc`, `CommittedAtUtc`, `LastVerifiedAtUtc`, `QuarantineReason`, `UpdatedAt`, `Version` (`rowversion`), `Deleted` | tenant-scoped, **not replicated** (added to `ServerModel.NonReplicatedProjectionTables`' sibling list `NonReplicatedFileTables`) |
  | `FileUploadSession` | `Id` (ULID PK), `StoredFileId`, `TenantId`, `TotalChunks`, `ChunkSizeBytes`, `ReceivedChunkMask` (`varbinary(1024)` bitmap), `DeclaredHash`, `DeclaredSizeBytes`, `ExpiresAtUtc`, `CreatedAtUtc`, `CompletedAtUtc`, `LastChunkAtUtc` | the resume state; a bitmap, not a row per chunk |
  | `FileAccessGrant` | `Id` (ULID PK), `StoredFileId`, `TenantId`, `UserId`, `Mode` (`Read \| Write`), `IssuedAtUtc`, `ExpiresAtUtc`, `ConsumedAtUtc`, `SingleUse`, `TokenHash` (`char(64)`) | the valet key's **server-side** record; the token itself is never stored |
  Indexes: `IX_StoredFiles_TenantId_State`, `IX_StoredFiles_TenantId_ContentHash`,
  `IX_StoredFiles_TenantId_OwnerEntityType_OwnerEntityId`, `IX_StoredFiles_State_CreatedAtUtc`
  (reconciliation sweep), `IX_FileUploadSessions_TenantId_ExpiresAtUtc`,
  `IX_FileAccessGrants_TokenHash` (unique), `IX_FileAccessGrants_ExpiresAtUtc`.
- **Path layout is derived, never client-supplied** (`STACK.md` §16 — filesystem, no object store):
  `RelativePath = <tenantId>/<yyyy>/<MM>/<contentHash[0..1]>/<contentHash[2..3]>/<fileId>.bin`,
  rooted at `ServerOptions.DataRootPath/files`. `FilePathResolver`
  (`src/Animora.Server/Files/FilePathResolver.cs`) is the **only** type that builds a path; it
  rejects any component that is not `[A-Za-z0-9._-]` and verifies the resolved absolute path is
  still under the root (`Path.GetFullPath` + `StartsWith` with `StringComparison.Ordinal`) —
  traversal is structurally impossible, not filtered. In-flight chunks live under
  `DataRootPath/files/.staging/<sessionId>/<chunkIndex>.part` and are `File.Move`d (atomic, same
  volume) into place on completion.
- **Transfer protocol — resumable by construction** (§14.3), four endpoints in
  `src/Animora.Server/Endpoints/FileEndpoints.cs`, all authenticated + tenant-scoped + policy-gated:
  | Route | Verb | Body | Returns |
  |---|---|---|---|
  | `/api/files/uploads` | POST | `BeginUploadV1(FileName, ContentType, SizeBytes, ContentHash, Kind, OwnerEntityType, OwnerEntityId, ChunkSizeBytes?)` | `UploadSessionV1(SessionId, FileId, ChunkSizeBytes, TotalChunks, MissingChunks, ExpiresAtUtc)` |
  | `/api/files/uploads/{sessionId}/chunks/{index}` | PUT | raw bytes (`application/octet-stream`) | `UploadProgressV1(Received, Total, MissingChunks)` |
  | `/api/files/uploads/{sessionId}/complete` | POST | `CompleteUploadV1(ContentHash)` | `FileDescriptorV1` |
  | `/api/files/uploads/{sessionId}` | GET | — | `UploadSessionV1` (**the resume call** — returns the missing-chunk list) |
  | `/api/files/uploads/{sessionId}` | DELETE | — | `204` (cancel; staging purged, row → `Failed`) |
  Binding values in `FileStoreOptions` (section `Animora:Files`, validated on start):
  `ChunkSizeBytes` 1 MiB (client may request 256 KiB–4 MiB), `MaxFileSizeBytes` 64 MiB,
  `SessionLifetimeHours` 24, `GrantLifetimeMinutes` 10, `ReconciliationHourUtc` 3,
  `OrphanGraceHours` 48, `AllowedContentTypes` (an allow-list: pdf, png, jpeg, webp, plain text,
  dicom-lite `application/octet-stream` only when `Kind == Scan`).
  D01's 32 MB request-body limit is **raised to `ChunkSizeBytes + 64 KiB` for the chunk route only**,
  via `[RequestSizeLimit]` on that single endpoint — the global limit is untouched.
  - **Idempotent by index:** re-uploading chunk *n* with identical bytes is a **no-op success**
    (A-RULE 025); with *different* bytes it is `FILE_CHUNK_CONFLICT` (a resumed client must not
    silently corrupt a file).
  - **Completion is idempotent:** calling `complete` twice returns the same `FileDescriptorV1`;
    the second call performs no work (the row is already `Available`).
  - **De-duplication:** if a `StoredFile` with the same `(TenantId, ContentHash)` is already
    `Available`, `complete` links the new metadata row to the **existing** bytes and skips the move
    (`RelativePath` shared, `SizeBytes` verified). Reference counting is deliberately **not**
    introduced — deletion is soft-delete only, so shared bytes are never removed under a live row;
    the reconciliation job's orphan collection checks for **any** non-deleted referencing row first.
- **Hash verification is mandatory and server-computed** (§14.1): `complete` streams the assembled
  file through `SHA256` (64 KiB buffer, `ArrayPool`, no full-file buffering — PERFORMANCE.md §2/§10)
  and compares to both the declared hash from `begin` and the one in the `complete` body. Any
  mismatch → state `Quarantined`, reason `hash-mismatch`, `FILE_HASH_MISMATCH`, an `OperatorAlert`,
  and an audit entry. The bytes are kept for forensic purposes, never served.
- **Valet Key access — a grant, never a path** (A-RULE 020, §10.8):
  - `POST /api/files/{fileId}/grants` (authenticated, resource-authorized via D04's
    `ResourceAuthorizationService` — same tenant **and** permission for the owning entity) returns
    `FileGrantV1(Token, ExpiresAtUtc, DownloadUrl)`.
  - The **token** is 32 random bytes, base64url, protected with the D01 Data Protection key ring
    (purpose `"Animora.Files.Grant.v1"`), carrying `{fileId, tenantId, userId, mode, expiresAtUtc,
    grantId}`. The server stores only `TokenHash = SHA-256(token)` (constant-time compared on use).
    A grant is `SingleUse` for writes, multi-use-until-expiry for reads.
  - `GET /api/files/content?grant=<token>` is the **only** byte-serving route. It is
    `AllowAnonymous` **by design** — the grant *is* the credential — and it re-validates: unwrap →
    hash lookup → not consumed → not expired → file `Available` → tenant match. It sets
    `ITenantContextSetter` from the grant, never from a header. `Content-Disposition` uses the
    stored original name, sanitized.
  - **`RelativePath` never leaves the server.** `FileDescriptorV1` exposes `FileId`, `SizeBytes`,
    `ContentType`, `OriginalFileName`, `ContentHash`, `State`, `CreatedAtUtc` — and no path. A test
    (`ValetKeyTests.DescriptorNeverExposesPath`) reflects over every wire DTO under
    `Animora.Contracts.Wire/Files/` and fails if any property name contains `Path`, `Directory` or
    `Root`.
- **Reconciliation is a mandatory job, not a hope** (§14.2, R-B26) —
  `src/Animora.Server/Workers/FileReconciliationWorker.cs`, a `BackgroundService` that acquires
  D09's `WorkerBulkhead`, runs daily at `ReconciliationHourUtc` (and on demand via the D10 operator
  rebuild route with target `files`). It enumerates **both** domains in bounded pages and resolves
  each of §14.2's four divergences with exactly **one** deterministic outcome:
  | Divergence | Detection | Deterministic outcome |
  |---|---|---|
  | DB row without a file | `State ∈ {Committed, Available}` and path missing | → `Quarantined`, reason `bytes-missing`, `OperatorAlert` (**never** silently deleted — the row is evidence) |
  | File without a DB row | file under the root with no `StoredFile.RelativePath` match, older than `OrphanGraceHours` | → moved to `.orphans/` and deleted after a second pass; logged and counted |
  | Incomplete upload | `FileUploadSession.ExpiresAtUtc < now` and not completed | → staging purged, `StoredFile` → `Failed`, session deleted |
  | Duplicate upload | two `Available` rows, same `(TenantId, ContentHash)`, distinct paths | → keep the earliest `CommittedAtUtc`, repoint the later row's `RelativePath`, delete the redundant bytes after verifying the hash of the kept copy |
  | Failed restore (DB/files out of step after §17.3 recovery) | a full pass finds >`DriftAlertThreshold` (default 25) divergences | → raise a `files.reconciliation.drift` `OperatorAlert` and **stop** the destructive half of the pass (quarantine-only mode), because a restore skew must be a human decision |
  Every pass writes one `FileReconciliationLog` row (`Id`, `StartedAtUtc`, `CompletedAtUtc`, the five
  counters, `Mode`) — the audit trail §14.2 requires. The worker is **resumable**: it processes
  bounded pages keyed by `Id` and records its cursor, so a kill mid-pass resumes, never restarts
  (A-RULE 026).
- **Outbox integration, the D09 way, with no new machinery:** committing a file enqueues
  `file.committed` on `IServerOutbox` inside the **same transaction** as the metadata row; a new
  `FileLifecycleConsumer` (`src/Animora.Server/Consumers/FileLifecycleConsumer.cs`) — written
  exactly like D09's four, with its `ConsumedMessages` guard — flips `Committed → Available` and
  links the `DocumentReference` (A04) row. This is why "committed" and "available" are two states:
  availability is an **asynchronous, idempotent** effect, not a wish.
- **Deletion is soft, always** (§8.5 / append-only spirit): `DELETE /api/files/{fileId}` sets
  `Deleted = true` and revokes outstanding grants; bytes are removed only by the reconciliation
  job's orphan pass, only after `OrphanGraceHours`, and never while any non-deleted row references
  the same `ContentHash`.
- **Backup awareness** (§17.3): `FileStoreOptions.DataRootPath` is part of the documented backup
  set, and the readiness check `files` verifies (a) the root exists and is writable, (b) free disk
  space ≥ `MinimumFreeSpaceMB` (default 1024) — below it, uploads are refused with
  `FILE_STORE_FULL` **before** bytes are accepted, never mid-stream.
- **Zero-Internet** (A-RULE 016): no object-storage SDK, no CDN, no virus-scanning service call. The
  content-type allow-list plus the hash gate are the shipped controls; anything else would be an
  external dependency and is explicitly not claimed.
- **New error codes:** `FILE_NOT_FOUND`, `FILE_TOO_LARGE`, `FILE_TYPE_NOT_ALLOWED`,
  `FILE_HASH_MISMATCH`, `FILE_CHUNK_CONFLICT`, `FILE_SESSION_EXPIRED`, `FILE_STATE_INVALID`,
  `FILE_GRANT_INVALID`, `FILE_GRANT_EXPIRED`, `FILE_STORE_FULL`, `FILE_QUARANTINED`.

## Files to create / modify
| Path | Action | Contents (one line) |
|---|---|---|
| `src/Animora.Domain/Files/FileState.cs` | create | lifecycle enum |
| `src/Animora.Domain/Files/FileStateTransitions.cs` | create | allow-table + `EnsureAllowed` |
| `src/Animora.Persistence.SqlServer/Files/StoredFile.cs` | create | metadata row |
| `src/Animora.Persistence.SqlServer/Files/FileUploadSession.cs` | create | resumable session + chunk bitmap |
| `src/Animora.Persistence.SqlServer/Files/FileAccessGrant.cs` | create | valet-key record (hash only) |
| `src/Animora.Persistence.SqlServer/Files/FileReconciliationLog.cs` | create | per-pass counters |
| `src/Animora.Persistence.SqlServer/Files/FileConfigurations.cs` | create | 4 configurations + indexes |
| `src/Animora.Persistence.SqlServer/Migrations/110_FileStore*` | create | 4 tables + indexes |
| `src/Animora.Persistence.SqlServer/Context/ServerModel.cs` | modify | add `NonReplicatedFileTables` |
| `src/Animora.Contracts.Wire/Files/*.cs` | create | `BeginUploadV1`, `UploadSessionV1`, `UploadProgressV1`, `CompleteUploadV1`, `FileDescriptorV1`, `FileGrantV1` |
| `src/Animora.Server/Configuration/FileStoreOptions.cs` | create | chunk/size/lifetime/reconciliation options |
| `src/Animora.Server/Files/FilePathResolver.cs` | create | the only path builder; traversal-proof |
| `src/Animora.Server/Files/FileStore.cs` | create | begin/chunk/complete/cancel, hash gate, dedupe |
| `src/Animora.Server/Files/ValetKeyService.cs` | create | grant issue/validate, DP-protected token |
| `src/Animora.Server/Files/ChunkAssembler.cs` | create | streaming assemble + SHA-256, pooled buffers |
| `src/Animora.Server/Endpoints/FileEndpoints.cs` | create | 7 routes incl. the anonymous grant-gated content route |
| `src/Animora.Server/Consumers/FileLifecycleConsumer.cs` | create | `file.committed` → `Available` + `DocumentReference` |
| `src/Animora.Server/Workers/FileReconciliationWorker.cs` | create | the 5 divergences, bulkheaded, resumable |
| `src/Animora.Server/Program.cs` | modify | options, services, consumer, worker, `files` readiness check |
| `src/Animora.Application/Results/ErrorCodes.cs` | modify | += 11 codes |
| `tests/Animora.Server.Tests/FileLifecycleTests.cs` | create | state machine, hash gate, idempotent complete |
| `tests/Animora.Server.Tests/ResumableUploadTests.cs` | create | resume, duplicate chunk, conflicting chunk, cancel |
| `tests/Animora.Server.Tests/ValetKeyTests.cs` | create | no path in DTOs, grant expiry/single-use, wrong tenant |
| `tests/Animora.Server.Tests/FileReconciliationTests.cs` | create | each divergence → its one outcome; drift mode |

## Implementation steps
1. Add `FileState` + `FileStateTransitions` to the domain.
2. Add the four entities, configurations, `NonReplicatedFileTables` and migration `110_FileStore`.
3. Add the wire DTO family (no path property anywhere).
4. Write `FilePathResolver` (traversal-proof) and `ChunkAssembler` (streaming hash, pooled buffers).
5. Write `FileStore`: begin (validate type/size/space, create row + session), chunk (idempotent by
   index, bitmap update), complete (assemble → hash → `HashVerified` → move → `Committed` + outbox
   enqueue **in one transaction**), cancel, resume query.
6. Write `ValetKeyService` (DP-protected token, stored hash, constant-time compare) and
   `FileEndpoints`, with the content route anonymous-but-grant-gated and the per-endpoint size limit.
7. Write `FileLifecycleConsumer` with its `ConsumedMessages` guard.
8. Write `FileReconciliationWorker` with the five divergence handlers, the cursor, the drift
   threshold and the log row.
9. Register everything; add the `files` readiness check and the 11 error codes.
10. Write the four test classes; `ValetKeyTests.DescriptorNeverExposesPath` is a reflection test
    that runs without SQL Server.
11. Verify on a real SQL Server + real disk: a 10 MiB file uploads in chunks, is interrupted at
    chunk 4, resumes from the missing-chunk list and completes with one `Available` row and one file
    on disk; a tampered chunk yields `Quarantined` + alert and is never served; a grant works once,
    then expires; deleting the DB row leaves the bytes until the orphan grace passes; deleting the
    file leaves the row `Quarantined`, never deleted.
12. `build.sh full` = 0/0; `test.sh` = `TESTS OK`; desktop `run.sh` = `SMOKE RUN OK`.

## Contracts / schema added
- Domain: `FileState`, `FileStateTransitions`.
- Persistence.SqlServer: `StoredFile`, `FileUploadSession`, `FileAccessGrant`,
  `FileReconciliationLog`, `FileConfigurations`, `ServerModel.NonReplicatedFileTables`,
  migration `110_FileStore`.
- Wire: `BeginUploadV1`, `UploadSessionV1`, `UploadProgressV1`, `CompleteUploadV1`,
  `FileDescriptorV1`, `FileGrantV1`. `WireContract.Version` stays `1`.
- Server: `FileStoreOptions`, `FilePathResolver`, `FileStore`, `ValetKeyService`, `ChunkAssembler`,
  `FileEndpoints` (7 routes), `FileLifecycleConsumer`, `FileReconciliationWorker`, the `files`
  readiness check.
- `ErrorCodes` += the 11 codes listed above.
- No `Animora.Contracts` change, no `CommandTypes` entry, no DI change in `App.axaml.cs`.

## Rules that apply here
- **§14.1 / R-B26** — two consistency domains; the lifecycle is explicit and `HashVerified` gates
  `Committed`; nothing is usable before `Available`.
- **§14.2 / R-B26** — reconciliation is a **mandatory** job with exactly one deterministic outcome
  per divergence, plus a drift mode that refuses to delete after a suspected bad restore.
- **§14.3** — resume / retry / hash verification / cancellation / idempotent completion are
  first-class capabilities of the protocol, not retrofits.
- **A-RULE 020 / §10.8** — a path is never a credential; the grant is. No wire DTO carries a path,
  and a test enforces it.
- **A-RULE 025 / 026** — the lifecycle consumer is idempotent via `ConsumedMessages`; the worker is
  resumable by cursor; a repeated chunk or repeated `complete` is a no-op.
- **A-RULE 027 / §17.3** — grant tokens use the persisted Data Protection key ring, and the file
  root is part of the documented backup set.
- **A-RULE 016** — no object storage, no CDN, no external scanning service.
- **CONVENTIONS §8** — streaming with pooled buffers, bounded pages in the worker, no full-file
  buffering, no unbounded enumeration of the file tree.

## Out of scope
- The **desktop** side: attaching documents from the UI, background upload/download, local cache
  eviction → E06 wires the transport; A11's `ILocalFileStore` stays as-is until then.
- Virus scanning, OCR, thumbnailing, image transformation → not in this product.
- Object storage / S3-compatible backends → `STACK.md` §16 (filesystem, launch decision).
- Rate limiting and per-tenant upload quotas → D13.
- Gate T (`ARCHITECTURE.md` §19) as a *recorded* validation gate → E08; the behavior and its tests
  ship here.
- Reference counting / hard deletion of shared bytes → deliberately not built; soft-delete plus the
  orphan pass is the launch policy.

## Acceptance criteria
- Build: `scripts/sandbox/build.sh full` = 0 warnings / 0 errors.
- `FileLifecycleTests` green (or skipped with reason): every allowed transition succeeds, every
  other returns `FILE_STATE_INVALID`; a file whose computed hash differs from the declared one can
  **never** reach `Committed`/`Available` and lands `Quarantined` with an alert; calling `complete`
  twice returns the identical descriptor and performs no second effect.
- `ResumableUploadTests` green: an upload interrupted after *k* of *n* chunks resumes from
  `GET /uploads/{id}` and completes; re-sending an identical chunk succeeds as a no-op; re-sending a
  **different** chunk returns `FILE_CHUNK_CONFLICT`; cancel purges staging and leaves no orphan.
- `ValetKeyTests` green: no wire DTO exposes a path/directory/root property; a read grant works
  until expiry and then returns `FILE_GRANT_EXPIRED`; a write grant is single-use; a grant for
  tenant A cannot fetch tenant B's file even with a valid token shape; the content route works with
  **no** `X-Animora-Tenant` header (the grant supplies the context).
- `FileReconciliationTests` green: each of the five divergences produces exactly its documented
  outcome; a pass exceeding `DriftAlertThreshold` switches to quarantine-only mode and raises
  `files.reconciliation.drift`; killing the worker mid-pass resumes from the cursor without
  re-processing completed pages; every pass writes one `FileReconciliationLog` row.
- `/health/ready` reports `files: healthy | low-space | root-unwritable`, and an upload attempted
  below `MinimumFreeSpaceMB` is refused with `FILE_STORE_FULL` **before** any byte is written.
- Desktop unaffected: `scripts/sandbox/run.sh` = `SMOKE RUN OK`.

## PROGRESS.md update
Mark D12 done; advance "Next phase" to `D13`; append a "Repo state" entry listing: `FileState` +
`FileStateTransitions` and the **`HashVerified` gates `Committed`** rule, the four tables +
migration `110_FileStore`, the derived `RelativePath` layout and `FilePathResolver` as the **only**
path builder, the 7 file routes and the chunk-only body-limit override, `FileStoreOptions` defaults
(chunk 1 MiB / max 64 MiB / session 24 h / grant 10 min / orphan grace 48 h), the idempotent-chunk
and idempotent-complete rules, the `(TenantId, ContentHash)` de-duplication policy and the
deliberate absence of reference counting, `ValetKeyService`'s DP-protected token + stored-hash-only
rule (**E06 consumes grants; no path ever crosses the wire**), `FileLifecycleConsumer` (**the fifth
D09-style consumer**), `FileReconciliationWorker`'s five divergence outcomes + drift mode + cursor,
`FileReconciliationLog`, the `files` readiness check + `FILE_STORE_FULL` pre-check, the 11 new error
codes, the explicit "no virus scanning / no object storage" limitation, deviations.
