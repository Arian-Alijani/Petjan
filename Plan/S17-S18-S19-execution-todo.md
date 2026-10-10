# Execution TODO — Phases S17 + S18 + S19

> **Purpose.** This is a self-contained execution checklist for implementing the three server-track phases
> **S17 (Files/Valet-Key)**, **S18 (Observability/Weak-server perf)** and **S19 (Admin web I)** back-to-back.
> It already contains every repository fact a fresh model needs (verified 2026-10-10) so you can start
> implementing **without re-exploring** the tree. Read a task, do exactly it, run the git push, move on.
>
> **Source of truth** remains the three phase files
> (`backend-roadmap/phases/PHASE-S17-files-valet-key.md`, `…S18…`, `…S19…`). If a snippet here ever
> contradicts a compile reality, fix the smallest thing and log it in `backend-roadmap/PROGRESS.md`.

---

## 🔒 LOCKED GIT RULE (non-negotiable)
After completing **each** checkbox task below, immediately run:

```bash
scripts/git/push.sh "type(scope): message"
```

It stages all → commits → rebases on remote → pushes, printing one `✓`/`✗` line.
- Exit `3` ⇒ call the `setup_github_environment` tool once, then re-run the same command.
- Exit `2` ⇒ rebase conflict (repo left clean); resolve preferring remote, re-run.
- **Do not advance to the next task until the push prints `✓`.** Not pushing = project death.
- Any heavy mid-task fix / large code block ⇒ push it too, with a clear message.

## ⚙️ Environment rules (do not probe, binding for all three phases)
- Build/test/run **only** via sandbox scripts (mem-capped ~985 MiB; never raw `dotnet build`):
  `scripts/sandbox/build.sh [fast|full]` (expect 0 warn / 0 err), `scripts/sandbox/test.sh [filter]`,
  `scripts/sandbox/run.sh` (→ `SMOKE RUN OK`). `scripts/sandbox/install-prereqs.sh` once (idempotent).
- **No Full test / no long test runs** unless a task explicitly says so. Prefer `build.sh fast` during dev;
  run `build.sh full` + targeted `test.sh <Filter>` only at a phase's final verification task.
- `dotnet-ef` lives at `.sandbox/tools/dotnet-ef` — use it; **never** `dotnet tool install`.
- Stack locked: ASP.NET Core 10 + Kestrel, EF Core 10.0.12, PostgreSQL 18 via Npgsql 10.0.3
  (only in `Animora.Persistence.PostgreSql`). Forbidden: Redis, broker, cloud IdP/telemetry/CDN, EF 11,
  per-tenant DB, hard-coded TLS, Native AOT+EF, `UpdatedAt`/`nextval`/`xmin` cursors.
- **Offline invariant:** desktop must keep starting/operating fully offline; none of these phases add a
  desktop-startup dependency. Client SQLite sequence (head `031`) is **untouched** by these phases.
- PG tests: read conn string from `ANIMORA_PG_TEST_CONNECTION`; unset ⇒ **skip with reason** locally,
  **fail** under `ANIMORA_REQUIRE_PG=1` (CI gate owned by G07). The `[PgGated]` tests here are authored,
  **not executed locally** (consistent with S10–S16 "implementation authored, execution deferred").

---

## 📌 Verified baseline (2026-10-10) — shared by all three phases
These were confirmed against the live tree; **do not re-derive**:

- `src/` projects (12): `Animora.Domain`, `Animora.Application`, `Animora.Persistence.Sqlite`,
  `Animora.Infrastructure.Local`, `Animora.Client.Services`, `Animora.Contracts`, `Animora.Contracts.Wire`,
  `Animora.Desktop`, `Animora.Sync.Abstractions`, **`Animora.Persistence.PostgreSql`**, **`Animora.Server`**,
  **`Animora.Server.Licensing`**. (No `Animora.Web.Admin` yet — S19 creates it.)
- **PROGRESS state:** Next phase = `S17`. S01–S16 rows are "Done/authored (execution gates deferred)".
  S17/S18/S19 rows = `⬜ Not started`. ErrorCodes live count = **186** (PROGRESS prose "161" is stale baseline).
- **PG server migrations** under `src/Animora.Persistence.PostgreSql/Migrations/Server/`, head = **`114_Licensing`**
  (sequence: 101_ServerFoundation, 102_Identity, 103_SigningKeys, 104_CommandIntake, 105_ClinicalCore,
  106_SchedulingBoarding, 107_SalesCashier, 108_MoneyStock, 109_ChangeJournal, 110_FeedBatches,
  111_ServerOutbox, 112_Subscription, 113_ProvisioningIntents, 114_Licensing).
  ⇒ **S17's next free server ordinal = `115`** (name it `115_Files`). KeyRing context sequence is separate (`101_KeyRing`).
- **KNOWN DRIFT (fix in S17):** `src/Animora.Persistence.PostgreSql/ServerSchemaVersion.cs` `ExpectedHead`
  is still `"113_ProvisioningIntents"` (S16 authored but didn't bump it). S17 sets it to the new `115_Files`
  migration id. (Do not "fix" 114 separately — just advance to the S17 head; log the two-step in PROGRESS deviation.)
- **`src/Animora.Server/Hosting/ServerRoutes.cs`** already has: `Commands /api/commands`, `Sync /sync`,
  `Auth /auth`, `Operator /operator`, `Provisioning /provisioning`, `Licensing /licensing`,
  **`Admin /admin`** (already present — S19 does NOT add it), `HealthLive`, `HealthReady`.
  ⇒ **S17 must ADD** `public const string Files = "/files";`.
- **`RateLimitPolicies`** already declares `Files = "files"` and `Operator = "operator"` and includes both in `.All`.
  ⇒ S17/S19 reuse them; nothing to add there.
- **`ServerErrorCodes.cs`** (transport-only, internal static) currently has `Unexpected`, `RateLimited`.
  ⇒ **S17 must ADD** `FILE_OFFSET_MISMATCH` and `FILE_NOT_AVAILABLE` here (transport strings, NOT domain ErrorCodes).
- **`ErrorCodes.cs`** pattern: `public static readonly ErrorCode X = new("X_CODE", ErrorCode.<Kind>);` inside
  `#region <context>`. Last regions: …`Server command intake (Phase S06)`, `Subscription & Operator (Phase S14)`,
  `Provisioning (Phase S15)`, `Licensing (Phase S16)`. ⇒ **S17 ADDS a new `#region Files (Phase S17)`** with +5 codes.
  S18/S19 add **no** ErrorCodes.
- **`DocumentReference`** exists: `src/Animora.Domain/Patients/DocumentReference.cs` (domain),
  mapped by `src/Animora.Persistence.PostgreSql/Configurations/DocumentReferenceConfiguration.cs`.
  **No server-side Application command creates a DocumentReference** (only client `ILocalFileStore` references the
  type). ⇒ S17's `IFileExistenceChecker` is delivered **unused by any command**; exercised via HTTP flow only.
  Record this in PROGRESS (matches the phase's explicit instruction).
- Existing server dirs: `Auth/ Authorization/ Commands/ Handlers/ Hints/ Hosting/ Licensing/ Operator/
  Provisioning/ Resilience/ Signing/ Subscriptions/ Sync/ Workers/`. **No `Files/` and no `Observability/` yet.**
- Worker pattern (S13) present in `src/Animora.Server/Workers/` (`ResilientWorker`, `WorkerHeartbeat`,
  `ServerWorkersServiceCollectionExtensions`, + concrete workers). `IBackgroundAdmission` = SemaphoreSlim(1) gate.
- Readiness probes in `src/Animora.Server/Hosting/` (`IReadinessProbe`, `DataRootWritableProbe`, `DatabaseProbe`,
  `KeyRingProbe`, `SchemaProbe`). `ServerOptions.DataRootPath` is the storage root.
- S19 seams verified present: `src/Animora.Persistence.PostgreSql/Subscriptions/{ISubscriptionService,
  ITenantAdministration (file TenantAdministration.cs), ISeatUsageReader}.cs`;
  `…/Licensing/{LicenseIssuer,LicenseStore,LicenseAdministration,LicenseKeyManager,LicenseGrantProjector}.cs`;
  `src/Animora.Server/Operator/{OperatorAuth,OperatorAccountService,OperatorEndpoints,OperatorContextMiddleware,
  OperatorTenantContext}.cs`. (S18's `ServerMetrics`/`QueryBudget`/`TenantScopedMemoryCache` are created by S18 and
  then consumed by S19 — do S18 before S19; dependency order respected.)
- `tests/Animora.Server.Tests/` exists with per-area folders (Authorization, Commands, HandlersS07.., Identity,
  Licensing, Operator, Outbox, Persistence, Provisioning, Replication, Resilience, Subscriptions, Sync, Workers,
  `TestHostFactory.cs`, `RepoRoot.cs`). ⇒ Add `Files/`, `Observability/`, `Admin/` subfolders here.
- `src/Animora.Server/Program.cs` ≈ 184 lines, numbered startup block (S01). Edited by S17 (routes+DI+probe+redaction),
  S18 (logging/telemetry/pipeline/Kestrel), S19 (razor components/antiforgery/auth group).

---

# ════════════ PHASE S17 — Files, content-addressed storage, valet-key ════════════
> Depends on S13 · Next S18 · Satisfies R-B26 · adds migration `115_Files`, +5 ErrorCodes, port `IFileExistenceChecker`.
> **Goal:** server file plane — SQL metadata + binary content on server FS content-addressed by SHA-256; lifecycle
> `Staged→Uploaded→HashVerified→Committed→Available`; Valet-Key (short-lived scoped tamper-proof grant, a path is never
> a credential) on every transfer; resumable chunked up/download; `DocumentReference↔FileId` link port; reconciliation GC.

### S17 design facts (pre-decided — implement verbatim)
- **Storage layout** (under `ServerOptions.DataRootPath`, disjoint from S12 `feed/`):
  - staging: `files/staging/<TenantId>/<UploadId>.part`
  - CAS (committed, content-addressed, **tenant-agnostic bytes**): `files/cas/<sha[0..2]>/<sha[2..4]>/<sha256>`
  - Writes: temp file → `fsync` → atomic `File.Move`. Fan-out ≤ 256 entries/level.
  - Identical bytes from two tenants ⇒ **one blob, two `FileObject` rows**, ref-counted via `BlobRef`.
- **Tables (migration `115_Files`, context `AnimoraServerDbContext`):**
  - `FileObjects(TenantId, Id varchar(26), Sha256 char(64)?, SizeBytes bigint, DeclaredSizeBytes bigint,
    ContentType varchar(128), OriginalName varchar(256), State smallint, CreatedUtc, UpdatedUtc,
    CreatedByUserId, UploadId varchar(26)?, PK(TenantId,Id))` — **NOT** `SyncableEntity` (file bytes/metadata are not
    on the state feed; `DocumentReference` carries `FileId`, content fetched on demand via valet key — ARCH §14).
    `State` enum: Staged=0, Uploaded=1, HashVerified=2, Committed=3, Available=4, Abandoned=5, Mismatch=6.
  - `FileUploadChunks(TenantId, UploadId, Offset bigint, Length int, PK(TenantId,UploadId,Offset))` — resume bookkeeping.
  - `BlobRefs(Sha256 char(64) PK, RefCount int, SizeBytes bigint, FirstSeenUtc)`.
  - Indexes: `IX_FileObjects_TenantId_State_UpdatedUtc`, `IX_FileObjects_TenantId_Sha256`.
- **Lifecycle** (`FileLifecycle.EnsureAllowed(from,to)`, table-driven, tested):
  `Staged→Uploaded` (all declared bytes present) → `HashVerified` (server streams staged file once computing SHA-256;
  client-declared hash if any must match else `Mismatch` + `FILE_HASH_MISMATCH`) → `Committed` (content moved into CAS;
  `BlobRef` incremented **in the same transaction** as state change) → `Available` (set immediately after Committed;
  distinct state so reconciliation can detect a Committed row whose blob is absent).
  `Staged|Uploaded → Abandoned` by reconciliation after **24 h** idle. `Mismatch` is terminal until operator (S20 — do NOT
  add the Mismatch→Uploaded/Abandoned edges here).
- **Valet-Key (A-RULE 020):** `ValetKey` = `ITimeLimitedDataProtector` (S02 Data Protection key ring,
  purpose `"Animora.Files.Valet.v1"`) protecting canonical JSON `{ v:1, tid, uid, did, fid, op:"upload"|"download", max:long, exp }`;
  **TTL 5 min**; bound to tenant+user+device+file+operation+maxBytes; verified on every transfer via header
  **`X-Animora-Valet`** *in addition to* normal authN/authZ (narrows, never widens). **Never contains a path; never logged.**
  Tampered/expired ⇒ `403 VALET_KEY_INVALID`. Issued by `POST /files/{id}/valet` (authorized for the matching permission).
- **Permissions (S05):** upload ⇒ `PatientsWrite`; download ⇒ `PatientsRead` (DocumentReference ∈ patients context).
- **Endpoints** (prefix `/files`, policy `files`, require authorized + tenant + device active):
  - `POST /files` — declare {name, content-type, size ≤ `MaxFileBytes`=64 MiB, optional sha256} → create `Staged` + `UploadId`,
    return first valet key.
  - `PUT /files/uploads/{uploadId}?offset=N` — chunk body ≤ **1 MiB**; explicit `offset` query (no `Content-Range`);
    accept only `offset == nextExpected` (re-send of identical stored chunk ⇒ idempotent **204**); out-of-order ⇒
    `409 FILE_OFFSET_MISMATCH` with header `X-Animora-Next-Offset`.
  - `GET /files/uploads/{uploadId}` — resume status (next offset).
  - `POST /files/uploads/{uploadId}/complete` — verify size + hash → Committed → Available; returns `FileId`.
  - `GET /files/{id}` — download, valet `download`, supports `Range`; always `Content-Disposition: attachment`,
    `X-Content-Type-Options: nosniff`, `Cache-Control: private, no-store`.
  - `DELETE /files/{id}` — soft: unreferenced check via `DocumentReferences`; `BlobRef.RefCount--`; blob removed by
    reconciliation at 0 after 24 h grace.
  - Content-type allow-list `FileTypePolicy` (images, PDF, plain text; NO executables → `FILE_TYPE_NOT_ALLOWED`).
    Declared vs actual size must match.
- **Document linking:** `Animora.Application.Ports.IFileExistenceChecker { Task<bool> IsAvailableAsync(string tenantId,
  string fileId, CancellationToken ct); }`, implemented in `Animora.Persistence.PostgreSql` (keeps Application free of EF).
  (Confirmed no command creates DocumentReference today ⇒ checker is unused by commands; HTTP flow only.)
- **Reconciliation** (`FileReconciliationWorker`, hourly, under `IBackgroundAdmission`, ≤ 200 rows/pass):
  (1) `Staged|Uploaded` idle > 24 h ⇒ `Abandoned` + delete `.part`;
  (2) `Committed|Available` whose CAS blob missing ⇒ `Mismatch` + `files.reconcile.blob_missing` audit;
  (3) CAS blobs `RefCount = 0` older than 24 h ⇒ delete file then `BlobRefs` row (crash-safe order: mark row, delete file,
      remove row — idempotent);
  (4) orphan files without a row older than 1 h ⇒ delete;
  (5) re-hash 20 random Available blobs/pass ⇒ mismatch flag (bit-rot).
  Each finding emits `animora.files.reconcile.*` counter (**S18 defines the Meter; S17 just increments names**) + audit row.
- **Backup coupling (ARCH §17.3):** `IFileStoreManifest.WriteAsync` writes `files/manifest.sha256` (path-free list of
  `sha256 size`). This phase only exposes the writer (used by G09 restore drill).
- **New ErrorCodes (+5, `#region Files (Phase S17)`):**
  `FileNotFound("FILE_NOT_FOUND",NotFound)`, `FileHashMismatch("FILE_HASH_MISMATCH",Integrity)`,
  `FileTypeNotAllowed("FILE_TYPE_NOT_ALLOWED",Validation)`, `FileTooLarge("FILE_TOO_LARGE",Validation)`,
  `ValetKeyInvalid("VALET_KEY_INVALID",Forbidden)`. Transport strings `FILE_OFFSET_MISMATCH`, `FILE_NOT_AVAILABLE`
  go in `ServerErrorCodes`.
- **Readiness probe** `files-root`: writable + free space ≥ 256 MiB, else *degraded by name*.
- **Redaction list** gains `X-Animora-Valet` (header never logged).

### S17 tasks

- [x] **S17-T1 — Domain/Application primitives.**
  Existing implementation preserved from main; build/test execution deferred by user directive (2026-10-10).
  Edit `src/Animora.Domain/Results/ErrorCodes.cs`: add `#region Files (Phase S17)` with the 5 codes above.
  Create `src/Animora.Application/Ports/IFileExistenceChecker.cs` (interface as specified; async, `CancellationToken ct = default`).
  `build.sh fast` the two projects compile-clean. → **push** `feat(server): S17 file errorcodes + IFileExistenceChecker port`.

- [x] **S17-T2 — Persistence: entities + configs + lifecycle + checker.**
  Create under `src/Animora.Persistence.PostgreSql/Files/`: `FileObject`, `FileUploadChunk`, `BlobRef` entities;
  `FileState` enum (0..6); `FileLifecycle` (table-driven `EnsureAllowed(from,to)` returning `Result`; edges exactly as
  listed; Mismatch terminal); `FileExistenceChecker : IFileExistenceChecker` (AsNoTracking query: row Available in tenant).
  Create `Configurations/FileObjectConfiguration.cs`, `FileUploadChunkConfiguration.cs`, `BlobRefConfiguration.cs`
  (column types + PKs + the two indexes). Register `DbSet`s on `AnimoraServerDbContext` + apply configs. **No EF changes
  leak to Application.** → **push** `feat(server): S17 file metadata entities, lifecycle, existence checker`.

- [x] **S17-T3 — Migration `115_Files` + schema head bump.**
  `.sandbox/tools/dotnet-ef migrations add Files --project src/Animora.Persistence.PostgreSql --startup-project
  src/Animora.Persistence.PostgreSql --context AnimoraServerDbContext --output-dir Migrations/Server`.
  Rename generated file's `NNN` token to **115** (`115_Files`). Edit `ServerSchemaVersion.ExpectedHead = "115_Files"`
  (closes the 113→115 drift; log in PROGRESS deviation). Migration must be additive; never edit later.
  `build.sh fast` compiles. → **push** `feat(server): S17 migration 115_Files + ExpectedHead bump`.

- [~] **S17-T4 — Server Files plane.**
  Create under `src/Animora.Server/Files/`: `FileStore` (CAS + staging, temp→fsync→atomic move, fan-out, ref-counted
  blob delete), `ValetKeyService` (ITimeLimitedDataProtector, purpose `"Animora.Files.Valet.v1"`, canonical JSON, TTL 5 min,
  bind tid/uid/did/fid/op/max; **no path anywhere**), `FileTypePolicy` (allow-list; executables rejected),
  `FileEndpoints` (the 7 endpoints, header semantics, 409+`X-Animora-Next-Offset`, idempotent 204, download headers),
  `FileReconciliationWorker` (5 rules, hourly, `IBackgroundAdmission`, ≤200/pass, crash-safe GC order),
  `FileStoreManifest : IFileStoreManifest`, `FilesServiceCollectionExtensions.AddAnimoraFiles()`.
  Edit: `ServerRoutes.cs` add `Files = "/files"`; map `/files` endpoints in `Program.cs` with policy `files`;
  add `FILE_OFFSET_MISMATCH` + `FILE_NOT_AVAILABLE` to `ServerErrorCodes`; add `X-Animora-Valet` to the redaction list
  (wherever S01/S06 keeps redacted header names); add `files-root` readiness probe (`Hosting/` or `Files/`).
  Wire `AddAnimoraFiles()` + worker registration in `Program.cs`.
  `build.sh fast` compiles. → **push** `feat(server): S17 file store, valet key, endpoints, reconciliation worker`.

- [~] **S17-T5 — Tests (authored; pure run locally, PG-gated authored-not-run).**
  Create `tests/Animora.Server.Tests/Files/`:
  pure — `FileLifecycleTests.AllowedAndForbiddenTransitions_Table`,
  `ValetKeyServiceTests.RoundTrip_BoundToTenantUserDeviceFileOp`,
  `ValetKeyServiceTests.Expired_Tampered_WrongTenant_WrongOp_Rejected`,
  `FileStoreTests.TempThenAtomicMove_NoPartialVisible`,
  `FileStoreTests.IdenticalBytes_TwoTenants_OneBlob_TwoRows_RefCounted`,
  `UploadFlowTests.OutOfOrderChunk_409_WithNextOffset`, `UploadFlowTests.ResendStoredChunk_Idempotent`,
  `UploadFlowTests.HashMismatch_ToMismatchState`, `FileTypePolicyTests.Executable_Rejected`,
  `DownloadTests.Headers_Attachment_NoSniff_NoStore`,
  `ReconciliationTests.Abandoned_BlobMissing_ZeroRef_Orphan_Bitrot_AreHandled`,
  `ReconciliationTests.GcOrder_CrashSafe_Idempotent`;
  PG-gated (`[PgGated]`, skip-unless-conn) — `FileMetadataTests.CrossTenant_CannotDownload_ByGuessingId`,
  `FileMetadataTests.CommitAndRefCount_SameTransaction`.
  → **push** `test(server): S17 files tests (pure + pg-gated authored)`.

- [x] **S17-T6 — Verify + PROGRESS + commit.**
  Run `scripts/sandbox/build.sh full` (expect 0/0), `scripts/sandbox/test.sh Files` (pure green; PG skip-with-reason),
  `scripts/sandbox/run.sh` (→ `SMOKE RUN OK`). Acceptance greps:
  `grep -rn "Path\b.*Valet\|filePath" src/Animora.Server/Files/ValetKeyService.cs` → nothing;
  `grep -rn "AWSSDK\|Azure.Storage\|Minio" src` → nothing;
  `ls src/Animora.Persistence.PostgreSql/Migrations/Server | grep -c Files` ≥ 1;
  `grep -c "VALET_KEY_INVALID\|FILE_HASH_MISMATCH\|FILE_TYPE_NOT_ALLOWED\|FILE_TOO_LARGE\|FILE_NOT_FOUND"
  src/Animora.Domain/Results/ErrorCodes.cs` = 5.
  Update `backend-roadmap/PROGRESS.md`: S17 row → `✅ Done` (date + one-liner); **Next phase → S18**; append one
  `### S17` Repo-state-log entry (projects/tables 115_Files/+5 codes/test classes/`IFileExistenceChecker` delivered unused +
  ExpectedHead 113→115 drift note); record deviations.
  → **push** `feat(server): S17 files — content-addressed storage, valet-key, resumable transfer & reconciliation`.

---

# ════════════ PHASE S18 — Observability & weak-server performance ════════════
> Depends on S13 · Next S19 · Satisfies R-B07,R-B27,R-B35 · **No migration · No ErrorCodes.**
> **Goal:** run on a 1–3 core / 2–4 GiB host with **no external service**: redacted local rolling-file logging +
> correlation ids; **optional** loopback-only OpenTelemetry; `QueryBudget`; Kestrel/admission limits; PG starting-profile
> artifacts (2/4 GiB); the metrics catalogue the other phases named; bounded tenant-safe cache. Claims **no** capacity number.

### S18 design facts (pre-decided — implement verbatim)
- **Logging (ARCH §15.1):** Serilog over MEL. **Reuse existing pins** `Serilog 4.2.0`, `Serilog.Extensions.Logging 9.0.0`,
  `Serilog.Sinks.File 6.0.0` (already in `Directory.Packages.props`, used by `Animora.Infrastructure.Local`).
  Add `Serilog.AspNetCore` **only if** it resolves on those pins; else wire `AddSerilog` manually via
  `Serilog.Extensions.Logging` — **record the choice in PROGRESS**. Sinks: rolling file only under `DataRootPath/logs`
  (daily roll, 14 files, 50 MiB/file, shared=false, buffered 1 s flush); **console sink only when `IsDevelopment()`**;
  **no** network/cloud sink in the build. Request logging: 100% for status ≥ 400, 5% for success, skip `/health/*`.
  **Redaction** (`RedactingEnricher` + destructuring policy) removes/masks property & header names:
  `password|token|refreshToken|authorization|valet|x-animora-valet|connectionString|secret|licenseKey|payloadJson|detailJson`;
  request/response **bodies never logged**; entities logged by **id only**. Enrich every event with:
  `TenantId` (S05 context when present), `OperationId` (command intake), `DeviceId`, `RequestId`, `CorrelationId`.
- **Metrics/traces (STACK §9):** built-in `System.Diagnostics.Metrics` meter `Meter("Animora.Server")` is **always on & free**.
  OpenTelemetry SDK is **optional**: add **new exact pins** (latest stable `1.x` of `OpenTelemetry.Extensions.Hosting`,
  `OpenTelemetry.Instrumentation.AspNetCore`, `OpenTelemetry.Instrumentation.Runtime`,
  `OpenTelemetry.Exporter.OpenTelemetryProtocol`, verified to target net10.0). Resolve the four exact versions once
  (`dotnet package search OpenTelemetry.Extensions.Hosting --exact-match --format json` if feed reachable; else use
  versions already in `~/.nuget/packages/opentelemetry*` and record "offline-resolved"). **Record the four versions** in
  `Directory.Packages.props` **and** PROGRESS. Reference from `Animora.Server` **only**.
  `TelemetryOptions { Enabled=false, OtlpEndpoint="http://127.0.0.1:4317", AllowLanCollector=false }` validated
  (`ValidateOnStart`): host must be **loopback** unless `AllowLanCollector=true` (then must be private RFC1918/ULA);
  **public hosts rejected at startup**. No exporter when `Enabled=false`. Exporter failures swallowed with rate-limited
  warning (removing collector never breaks the app). Instrumentation: ASP.NET Core + runtime + custom meters;
  **no EF/Npgsql command text in traces** (`SetDbStatementForText=false`).
- **Metrics catalogue** (all in one `ServerMetrics`): `animora.http.requests`/`duration` (instrumentation),
  `animora.commands.settled` (counter tagged `outcome`,`commandType` — **NO tenant label**),
  `animora.commands.in_progress`, `animora.outbox.pending`/`dead_lettered`,
  `animora.worker.last_success_age.seconds`, `animora.pg.pool.in_use` (per data source),
  `animora.pg.snapshot.age.seconds`, `animora.sync.batch.*`, `animora.files.reconcile.*`,
  `animora.ratelimit.rejected`. **Tenant dimension is NEVER a metric label** (cardinality; per-tenant detail in logs).
- **`QueryBudget` (A-RULE 034):** `enum QueryClass { Oltp, Reporting }`; `IQueryBudget.Acquire(QueryClass, ct)` → disposable
  lease. `Oltp` = unlimited by this gate (pool-bounded). `Reporting` = **1 concurrent**, **5 s statement timeout**
  (`SET LOCAL statement_timeout` on its connection), **max 10 000 rows**, executed on the dedicated **`worker`** data source
  (never OLTP pool). (S20 insight queries acquire `Reporting`.)
- **Kestrel & admission** (from `DatabaseOptions.ProfileGiB` 2|4):
  `MaxConcurrentConnections = 256 (2 GiB) / 512 (4 GiB)`, `MaxConcurrentUpgradedConnections = 64`,
  `Limits.MaxRequestBodySize = 2 MiB` default with per-endpoint overrides (`/files` chunk 1 MiB + overhead;
  `/api/commands/batch` 1 MiB), `RequestHeadersTimeout 15 s`, `KeepAliveTimeout 60 s`,
  `MinRequestBodyDataRate` 240 B/s grace 10 s. GC: **workstation concurrent** (Server GC **off**),
  `GCHeapHardLimitPercent = 70` + `System.GC.ConserveMemory=5` documented in runtimeconfig; `TieredPGO` on;
  `ReadyToRun` **off** (JIT; AOT forbidden w/ EF). Response compression: JSON API only (Brotli fastest, ≥ 1 KiB);
  **never** `/sync` pages (already gzip) or `/files`.
- **PostgreSQL profile artifacts** (reviewed, NOT runtime config) — create `deploy/postgresql/` (dir does not exist yet):
  `animora-2gib.conf` / `animora-4gib.conf` with (per ARCH §PG.4):
  `shared_buffers` 256MB/768MB, `work_mem` 4MB/8MB, `maintenance_work_mem` 64MB/128MB,
  `effective_cache_size` 768MB/2GB, `max_connections` 30/40 (= pools + admin/backup reserve ≥ 8),
  `wal_compression=on`, `synchronous_commit=on`, `full_page_writes=on`, `autovacuum=on` + `autovacuum_naptime=30s`,
  `idle_in_transaction_session_timeout=30s`, `lock_timeout=5s`, `statement_timeout=30s` (app role),
  `log_min_duration_statement=500ms`, `track_io_timing=on`. Plus `deploy/postgresql/README.md` stating every value is a
  **"starting point"** proven only by G07/G08, never a guarantee.
  `SchemaProbe`/`DatabaseProbe` additionally warns (*degraded*, name only) when `SHOW max_connections` < pool sum + 8
  (via new `IDatabaseCapabilityProbe` hook on `DatabaseOptions`).
- **Snapshot-age monitor:** `SnapshotAgeMonitor` (worker, 15 s) reads `pg_stat_activity` oldest `xact_start` of the
  server's own `RepeatableRead` sessions (application_name `Animora.Server/feed`) → gauge + degraded readiness at
  `> MaxSnapshotLifetime × 3`.
- **Bounded tenant-safe cache:** `TenantScopedMemoryCache<TKey,TValue>` (server mirror of client `TenantScopedCache`):
  key = `(TenantId, UserId, Resource, Version)` mandatory (ctor throws if any empty), bounded by entry count
  (default 1 000) + sliding 60 s TTL, LRU eviction, `MemoryCache` with `SizeLimit`. Used by later phases; **no cache on the
  OLTP command path at launch.**
- **No new ErrorCodes. No migration.**

### S18 tasks

- [x] **S18-T1 — Package pins.**
  Resolve the four OTel exact versions (feed or offline-resolved — record which). Add to `Directory.Packages.props`;
  add `<PackageReference>`s to `src/Animora.Server/Animora.Server.csproj` only. Confirm Serilog.AspNetCore resolves
  (or decide manual) — record. `build.sh fast` restores clean. → **push** `build(server): S18 OpenTelemetry pins (+serilog choice)`.

- [~] **S18-T2 — Observability code.**
  Create `src/Animora.Server/Observability/`: `ServerMetrics` (the full catalogue, one place, no tenant label),
  `LoggingSetup`, `RedactingEnricher` (+ destructuring policy for the secret property/header names),
  `TelemetryOptions` + `TelemetryOptionsValidator` (loopback/LAN/public rule, ValidateOnStart), `TelemetrySetup`
  (optional, failure-swallowing exporter), `SnapshotAgeMonitor`, `QueryBudget` (+`QueryClass`, Reporting lease on `worker`
  data source), `KestrelProfile` (limits by `ProfileGiB`), `TenantScopedMemoryCache<TKey,TValue>`.
  `build.sh fast` compiles. → **push** `feat(server): S18 observability — metrics, logging, telemetry, query budget, cache`.

- [x] **S18-T3 — Program.cs + DatabaseOptions wiring.**
  Edit `Program.cs`: steps 1–2 logging/options; step-7 pipeline request logging with sampling; register telemetry
  (only when enabled); apply `KestrelProfile`; response compression (JSON only, exclude `/sync` & `/files`); set
  runtimeconfig GC knobs (via `.csproj`/`runtimeconfig.template.json`). Edit `src/Animora.Persistence.PostgreSql/Options/
  DatabaseOptions*.cs`: add `IDatabaseCapabilityProbe` hook (max_connections advisory) + `worker` data source consumption
  by `QueryBudget`. `build.sh fast` compiles. → **push** `feat(server): S18 wire logging/telemetry/Kestrel + DB capability probe`.

- [x] **S18-T4 — PG profile artifacts.**
  Create `deploy/postgresql/animora-2gib.conf`, `animora-4gib.conf`, `README.md` (values + "starting point" disclaimer).
  → **push** `docs(deploy): S18 PostgreSQL 2/4 GiB starting profiles`.

- [~] **S18-T5 — Tests.**
  Create `tests/Animora.Server.Tests/Observability/`:
  `TelemetryOptionsTests.PublicHost_Rejected_Loopback_Allowed_LanOnlyWithOptIn`,
  `TelemetryTests.Disabled_ConfiguresNoExporter`, `TelemetryTests.UnreachableCollector_DoesNotAffectRequests`,
  `RedactionTests.SecretPropertiesAndHeaders_NeverLogged`, `RedactionTests.BodiesNeverLogged`,
  `LoggingTests.ConsoleSink_OnlyInDevelopment`, `LoggingTests.RollingFile_Limits`,
  `MetricsTests.NoTenantLabelOnAnyInstrument`, `QueryBudgetTests.Reporting_Concurrency1_Timeout5s_RowCap`,
  `KestrelProfileTests.Limits_By_ProfileGiB`, `TenantScopedCacheTests.MissingKeyPart_Throws`,
  `TenantScopedCacheTests.TenantsNeverShareEntries`, `TenantScopedCacheTests.BoundedByCount`,
  `PostgresProfileFilesTests.PoolSumPlusReserve_LE_MaxConnections` (parses the two `.conf` files vs `DatabaseOptions`
  defaults); PG-gated — `SnapshotAgeMonitorTests.OldSnapshot_ReportsDegraded`,
  `QueryBudgetPgTests.StatementTimeout_Enforced`. → **push** `test(server): S18 observability tests`.

- [x] **S18-T6 — Verify + PROGRESS + commit.**
  `build.sh full` (0/0); `test.sh Observability` (pure green; PG skip); `run.sh` (SMOKE RUN OK). Acceptance greps:
  `grep -rniE "Seq|Splunk|ApplicationInsights|Datadog|Loki|Elasticsearch|NewRelic" src --include=*.cs --include=*.csproj`
  → nothing; `grep -rn "SslProtocols\|PublishAot\|ServerGarbageCollector>true" src/Animora.Server` → nothing;
  `test -f deploy/postgresql/animora-2gib.conf && test -f deploy/postgresql/animora-4gib.conf &&
  grep -c "starting point" deploy/postgresql/README.md` ≥ 1.
  PROGRESS: S18 row → `✅ Done`; **Next phase → S19**; append `### S18` (record the 4 OTel versions + Serilog.AspNetCore
  yes/no); deviations. → **push** `feat(server): S18 server observability & weak-server performance (local-only telemetry, bounded budgets)`.

---

# ════════════ PHASE S19 — Admin web I: operator panel core ════════════
> Depends on S16, S18 · Next S20 · Satisfies R-B05,R-B06,R-B29 · **No migration · No ErrorCodes.**
> **Goal:** vendor **operator admin site** hosted by `Animora.Server` under `/admin`, a **Blazor Web App in Static SSR by
> default** (no per-page circuit; forms post + re-render): cookie operator sign-in (separate from tenant bearer AND from
> the S14 operator API bearer), dashboard, tenant list/detail with all S14 subscription actions, plan mgmt, licence mgmt.
> **Pages hold no business logic** — they call small ports implemented in `Animora.Server` over the same S14/S16/S18 services.

### S19 design facts (pre-decided — implement verbatim)
- **New project** `src/Animora.Web.Admin` = `Microsoft.NET.Sdk.Razor`, **Razor class library**,
  `FrameworkReference Microsoft.AspNetCore.App`, references **`Animora.Domain` only**. Holds pages/layout/components/
  view-models and the **ports** (`Animora.Web.Admin.Ports`). References **NO** Npgsql/EF/`Animora.Persistence.*`/
  `Animora.Application`/`Animora.Server*`. Static assets in `wwwroot` (self-hosted CSS/JS only — **no CDN/web fonts/
  framework package**; ≤ 30 KiB CSS), served via `MapStaticAssets`. Add project to `Animora.sln`.
  `Animora.Server` references it and supplies adapters in `src/Animora.Server/Admin/*` (the only place services are called).
- **Render-mode policy (binding, ARCH §7.3):** `Routes`/`App` use **no global render mode** (Static SSR). S19 ships
  **zero** interactive islands. Only `AddRazorComponents()` in S19 (no `AddInteractiveServerComponents` — that + CircuitOptions
  is S20). `RenderModePolicy.MaxIslands` constant = 0 (S20 raises to 1). Forms = enhanced SSR
  (`<EditForm FormName=… method="post">` + `[SupplyParameterFromForm]`, automatic antiforgery).
- **Operator cookie auth — a THIRD, separate scheme** `OperatorCookie.Scheme = "Animora.Operator.Cookie"`:
  cookie `__Host-animora-op` (Secure, HttpOnly, `SameSite=Strict`, Path `/`, **absolute 30 min, no sliding**,
  Data-Protection-protected from S02 key ring). Claims `sub` (operator id), `ost` (security stamp), `role=operator`.
  `OnValidatePrincipal` re-reads `OperatorAccounts` by PK and rejects when `SecurityStamp ≠ ost`, status ≠ Active, or
  `MustChangePassword` (then only `/admin/account/change-password` + logout reachable) ⇒ **revocation immediate**.
  Policy `AdminOnly` = authenticated on that cookie scheme **and** `OperatorOnly` semantics (`ITenantContext.IsOperator`
  via `OperatorContextMiddleware` extended to `/admin/**`). `TenantContextMiddleware`/`EntitlementMiddleware` **skip**
  `ServerRoutes.Admin`. Login endpoints (minimal APIs, antiforgery-validated form posts, rate-limit `operator`):
  `POST /admin/account/login` (`OperatorAccountService.VerifyAsync` — same lockout/constant-time, **same generic failure
  text**), `POST /admin/account/logout`, `POST /admin/account/change-password`. Every admin mutation requires POST
  antiforgery token **and** the cookie; GET never mutates. Unauthenticated → **302 `/admin/login`** (never leaks path
  existence). `/admin/**` response headers: `Cache-Control: no-store`, `X-Content-Type-Options: nosniff`,
  `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`, CSP `default-src 'self'; frame-ancestors 'none'; form-action 'self'`.
  Temp passwords shown **once** in the POST response page — never in URL/query/log. Antiforgery cookie `__Host-animora-af`.
- **Ports (`Animora.Web.Admin.Ports`; all take `CancellationToken`; return `Result<T>`; paging via
  `AdminPage<T>(Items,Page,PageSize,Total)`, `PageSize ≤ 200`):**
  - `IAdminStatusReader.GetStatusAsync` → `AdminStatus(Readiness: list(name,healthy|degraded|unhealthy),
    Workers: list(name,lastSuccessAgeSeconds), Counters:{commandsInProgress,outboxPending,outboxDeadLettered,intakeInProgress},
    Pg:{poolInUse,poolMax}, Version, UptimeSeconds)`.
  - `IAdminTenantQueries.ListAsync(TenantListQuery(status?,search?,page,pageSize))`, `GetAsync(id)` →
    `TenantDetail(core, subscription(+ConcurrencyStamp), seatsUsed, last 20 events, devices count, licences summary)`.
  - `IAdminSubscriptionCommands`: the 8 S14 ops (Extend/Reduce/Suspend/Reinstate/Cancel/Renew/ChangeSeats/ChangePlan)
    + `ResetOwnerPassword`, each taking `ifMatchStamp`; plus `CreateTenantAsync(CreateTenantForm, idempotencyKey)`
    whose adapter calls S14 `ProvisioningEngine.ProvisionAsync` with `ActorRef(Operator, id)`.
  - `IAdminPlanCommands`: `ListAsync`, `CreateAsync`, `DeactivateAsync`.
  - `IAdminLicenseCommands`: `ListAsync(tenantId)`, `RevokeAsync`, `RecheckAsync`, `RotateKeyAsync`, `GetKeySetAsync`.
  - `IAdminSession`: current operator id/name (display + audit actor).
  Adapters in `src/Animora.Server/Admin/*Adapter.cs` = thin mapping onto S14/S16/S18 services (**no SQL/business rules;
  `ActorRef(Operator, operatorId)` always from the cookie principal**). `OperatorAudit` written by the underlying services
  (same-transaction) — adapters add nothing.
- **Pages (all Static SSR, English LTR, keyboard-accessible, semantic tables, no client-side sorting; each file ≤ 150 lines):**
  `/admin` Dashboard (status table; "expiring in 14 days"; "past due / grace"; "dead-lettered outbox" count linking to
  tenant-list filters; **no polling** — manual Refresh link), `/admin/tenants` (filter by status + search by code/name,
  paged), `/admin/tenants/{id}` (summary + subscription card; each action its own `<form method="post">` with confirmation
  checkbox for suspend/cancel/reduce; `ConcurrencyStamp`-based `If-Match` hidden field → 412 "someone else changed this,
  reload"), `/admin/tenants/new` (provisioning via `CreateTenantAsync` with auto idempotency key stored in the form so
  double-submit provisions once), `/admin/plans`, `/admin/tenants/{id}/licenses` (list, revoke w/ reason, recheck),
  `/admin/licensing/keys` (public key set, rotate w/ confirmation), `/admin/account/*`.
  Shared components: `Pager`, `StatusBadge`, `ConfirmCheckbox`, `FormError`. No localisation (operator is the vendor).
- **Failure & safety:** adapter `Result` failure → `FormError` with `ErrorCode.Code` + fixed message table
  `AdminMessages` (unit-tested to cover every ErrorCode the adapters can return). Unexpected → `GlobalExceptionMiddleware`
  (HTML error page for `/admin/**` with correlation id, no stack trace). Paging mandatory. No cross-tenant query except
  through `AnimoraOperatorDbContext`-backed services.
- **Performance budget:** each page ≤ 3 DB round-trips; dashboard counters from in-process meters/heartbeats + **one**
  bounded SQL for expiring/dead-lettered counts. `/admin` under `RateLimitPolicies.Operator`; no circuit.
- **New server options:** `AdminOptions { SessionMinutes = 30, RequireHttps = true }` (section `Admin`). **No ErrorCodes.**
- **S19 reuse seams (verified present):** `ISubscriptionService`, `ITenantAdministration`, `ISeatUsageReader`
  (`Persistence.PostgreSql/Subscriptions/`); `ProvisioningEngine.ProvisionAsync`; `LicenseIssuer`/`LicenseStore`/
  `LicenseAdministration`/`LicenseKeyManager`/`LicenseGrantProjector` (`…/Licensing/`); `OperatorAccountService`,
  `OperatorAuth`, `OperatorContextMiddleware`, `OperatorTenantContext` (`Server/Operator/`); `ServerMetrics`, `IQueryBudget`,
  `WorkerHeartbeat`, `IReadinessProbe` (S18/S13). `ServerRoutes.Admin = "/admin"` already exists;
  `RateLimitPolicies.Operator = "operator"` already exists.

### S19 tasks

- [x] **S19-T1 — RCL project `Animora.Web.Admin`.**
  Create `src/Animora.Web.Admin/Animora.Web.Admin.csproj` (`Microsoft.NET.Sdk.Razor`, `FrameworkReference
  Microsoft.AspNetCore.App`, ProjectReference `Animora.Domain` only). Add `_Imports.razor`, `Routes.razor`, App pieces,
  `Layout/{MainLayout,NavMenu}.razor`, `Components/{Pager,StatusBadge,ConfirmCheckbox,FormError}.razor`,
  `Pages/{Dashboard,Login,ChangePassword,Tenants,TenantDetail,TenantNew,Plans,TenantLicenses,LicensingKeys}.razor`,
  `Ports/*.cs`, `Models/*.cs`, `AdminMessages.cs`, `RenderModePolicy.cs` (const `MaxIslands = 0`), `wwwroot/admin.css`.
  Add project to `Animora.sln`. `build.sh fast` compiles the RCL. → **push** `feat(web-admin): S19 RCL scaffold, ports, pages, components`.

- [x] **S19-T2 — Server Admin wiring + adapters.**
  Create `src/Animora.Server/Admin/`: `AdminOptions`, `OperatorCookie` (scheme + cookie config + `OnValidatePrincipal`),
  `AdminAccountEndpoints` (login/logout/change-password minimal APIs), `AdminSecurityHeadersMiddleware` (the 5 headers + CSP),
  `AdminStatusReader`, `AdminTenantQueries`, `AdminSubscriptionAdapter`, `AdminPlanAdapter`, `AdminLicenseAdapter`,
  `AdminSession`, `AdminServiceCollectionExtensions`. Each adapter = thin mapping, `ActorRef(Operator, id)` from cookie.
  Edit `Program.cs`: `Animora.Server.csproj` ref to `Animora.Web.Admin`; `AddRazorComponents()`; `UseAntiforgery()` after
  auth/authz; `MapRazorComponents<App>().AddAdditionalAssemblies(typeof(Animora.Web.Admin.Ports.IAdminSession).Assembly)`
  in a group `RequireAuthorization("AdminOnly")` with `/admin/login` + `/admin/account/*` anonymous-allowed; extend
  `OperatorContextMiddleware` to `/admin`; make `TenantContextMiddleware`/`EntitlementMiddleware` skip `/admin`; register
  `AdminOptions`, `OperatorCookie` scheme, `AdminOnly` policy, admin security-headers middleware.
  `build.sh fast` compiles. → **push** `feat(server): S19 admin cookie auth, endpoints, security headers, port adapters, wiring`.

- [~] **S19-T3 — Tests (SSR via WebApplicationFactory + string assertions; NO new test package).**
  Create `tests/Animora.Server.Tests/Admin/`:
  `RenderModePolicyTests.NoGlobalRenderMode_AndIslandCountWithinBudget`,
  `WebAdminLayeringTests.RclReferencesOnlyDomain`, `AdminAuthTests.Anonymous_RedirectsToLogin_ForEveryAdminRoute`,
  `AdminAuthTests.TenantBearerToken_CannotAccessAdmin`,
  `AdminAuthTests.OperatorApiBearer_CannotAccessAdmin_AndCookie_CannotCallOperatorApi`,
  `AdminAuthTests.Login_WrongPasswordAndUnknownUser_SameMessage`, `AdminAuthTests.Lockout_After5Failures`,
  `AdminAuthTests.SecurityStampChange_InvalidatesCookieImmediately`,
  `AdminAuthTests.MustChangePassword_OnlyChangePasswordReachable`,
  `AdminAuthTests.Cookie_IsHostPrefixed_HttpOnly_SameSiteStrict_Absolute30Min`,
  `AdminAntiforgeryTests.PostWithoutToken_Rejected_ForEveryMutation`, `AdminAntiforgeryTests.GetNeverMutates`,
  `AdminHeadersTests.NoStore_CSP_FrameDeny_Nosniff`, `AdminAdaptersTests.ActorAlwaysFromCookiePrincipal`,
  `AdminAdaptersTests.IfMatchMismatch_Renders412Message`,
  `AdminAdaptersTests.CreateTenant_DoubleSubmit_SameIdempotencyKey_OneTenant`,
  `AdminAdaptersTests.TempPassword_ShownOnce_NotInUrlOrLogs`, `AdminMessagesTests.EveryAdapterErrorCode_HasAMessage`,
  `AdminPagesTests.Lists_Paged_Max200`, `AdminPagesTests.Dashboard_ReadsInProcessCountersOnly`,
  `AdminStatusReaderTests.DegradedByNameOnly_NoSecrets`;
  PG-gated — `AdminEndToEndTests.OperatorCreatesTenant_SuspendsIt_TenantRequestsGet403_ReinstateRestores`,
  `AdminEndToEndTests.RevokeLicence_DeviceBlockedAtServerGate`,
  `AdminEndToEndTests.CookieRevokedByOperatorPasswordChange`.
  Edit `tests/Animora.Server.Tests/Animora.Server.Tests.csproj` only if a ProjectReference to `Animora.Web.Admin` is needed
  (no new NuGet). → **push** `test(server): S19 admin web tests (auth, antiforgery, headers, adapters, pages)`.

- [ ] **S19-T4 — Verify + PROGRESS + commit.**
  `build.sh full` (0/0); `test.sh Admin` (pure/host green; PG skip); `run.sh` (SMOKE RUN OK; client sources unchanged).
  Acceptance greps:
  `grep -rn "Npgsql\|EntityFrameworkCore\|Animora.Persistence\|Animora.Application\|Animora.Server"
  src/Animora.Web.Admin --include=*.csproj --include=*.cs --include=*.razor` → nothing;
  `grep -rn "https\?://" src/Animora.Web.Admin --include=*.razor --include=*.css --include=*.cs` → nothing except
  `http://www.w3.org` XML namespaces; `grep -rn "@rendermode" src/Animora.Web.Admin | wc -l` = 0 and `Routes.razor`,
  `MainLayout.razor` contain no `rendermode`.
  PROGRESS: S19 row → `✅ Done`; **Next phase → S20**; append `### S19` (project `Animora.Web.Admin`, routes under `/admin`,
  scheme `Animora.Operator.Cookie`, policy `AdminOnly`, test classes); deviations.
  → **push** `feat(server): S19 admin web I — operator panel core (Static SSR, cookie auth, tenants/subscriptions/licences)`.

---

## 🧹 Final task (do LAST, only after S17-T6, S18-T6 and S19-T4 all pushed `✓`)

- [ ] **FINAL — delete this plan file.**
  `git rm Plan/S17-S18-S19-execution-todo.md` (do NOT touch the roadmap phase files or any other file).
  → **push** `chore(plan): remove S17–S19 execution TODO after completion`.

---

### Status legend
`[ ]` not started · `[~]` in progress · `[x]` done (pushed `✓`). Keep this file's checkboxes current as you go —
it is the only live progress record for a resumed session until each phase's PROGRESS.md entry is written.

Execution policy: all builds, tests and smoke runs are deferred by explicit user directive (2026-10-10); checkmarks denote authored deliverables, not executed acceptance.

## Checkpoint — user-requested wrap-up (2026-10-10)

Implementation stopped at the user's wrap-up instruction, not at full-plan acceptance. This file is intentionally retained.
All source changes have been committed and pushed. No build, test, smoke run, restore or migration application was executed.

Remaining work before all checkboxes can truthfully be completed:
- S17-T4: finish handling aged CAS publisher `.tmp` orphans (currently unknown/operator files are deliberately preserved).
- S17-T5: complete real HTTP upload offset/header/download assertions and explicit orphan/zero-ref reconciliation cases; current tests include direct PG transfer/ref-count and crash-retry coverage.
- S18-T2: complete sync-byte metric production and review the entire catalogue's producer coverage (catalogue and major producers are authored).
- S18-T5: fill remaining requested behavioral coverage beyond the authored options/privacy/cache/limits/budget/snapshot regressions.
- S19-T3: complete full HTTP suspension/reinstatement flow (current test checks stored tenant status), valid bearer-scheme isolation cases and degraded-status secrecy behavioral coverage.
- S19-T4: final phase documentation/acceptance checkpoint has not been completed; Next phase remains S19.
- FINAL: do not delete this plan until those implementation/coverage items are complete.

Execution gates remain deferred under the no-build/no-test instruction, regardless of authored checkbox status.
