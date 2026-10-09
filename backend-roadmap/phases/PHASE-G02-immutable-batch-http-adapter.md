# Phase G02 — Custom immutable-batch HTTP adapter (`Animora.Sync.Custom`) — consume the S12 `/sync/*` feed

**Band:** M · **Track:** G (sync / integration / release) · **Depends on:** G01 · **Next:** G03
**Satisfies:** R-B03, R-B13, R-B31 · **Runnable after this phase:** desktop offline yes · server required no
**Produced by:** M00 (regenerated 2026-10-09; supersedes the second half of the deleted `PHASE-E03` file).

> Baseline verified 2026-10-09: client migration head `031` (`031_TreatmentPlans`), `CommandTypes` = live `CommandTypes.cs` (88 declared today — never freeze), `ErrorCodes` 161 (+ codes added by earlier S phases, e.g. S12 `RESYNC_REQUIRED`/`BATCH_NOT_FOUND`), `WireContract.Version` = 2 after S06 (supported {1,2}; `/sync/*` is a v2 surface); `src/` projects = `Animora.Domain`, `Animora.Application`, `Animora.Persistence.Sqlite`, `Animora.Infrastructure.Local`, `Animora.Client.Services`, `Animora.Contracts`, `Animora.Contracts.Wire`, `Animora.Desktop`, `Animora.Sync.Abstractions` (filled by G01) (+ the server-track `S01–S20` projects).
> **Next-free-ordinal rule.** This phase adds **no migration** (no schema; the local staging store is G04's concern). For any later phase that does: list the physical files of the owning `DbContext`'s `Migrations/` folder and take the next free ordinal (server sequence starts at **101**; the client SQLite sequence — head `031` today — is independent). Match on the `NNN` token, not a file-name prefix.

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
- `dotnet-ef` lives at `.sandbox/tools/dotnet-ef` — **never** `dotnet tool install` (not needed here: no migration).
- **Forbidden tech (restated, binding):** Redis, message broker, cloud IdP, cloud telemetry, CDN, EF Core 11,
  per-tenant database, hard-coded TLS version, Native AOT with EF Core. **This phase adds no package other than the already-pinned
  `Microsoft.Extensions.Http` (typed `HttpClient`/`IHttpClientFactory`) + `Microsoft.Extensions.DependencyInjection` from `Directory.Packages.props` — never Npgsql, never any server-project reference, never `CommunityToolkit.Datasync`.**

**Offline invariant.** `Animora.Sync.Custom` is registered as the real `IStateSyncEngine` **only when a base address is configured**; when `ServerEndpointOptions.BaseAddress` is null/empty the G01 `NullSyncEngine` (TryAdd) stays in force and the desktop starts and runs exactly as today (Standing principle 1, CONVENTIONS §11). A transient network failure during `PullAsync` returns `Result.Ok(SyncResult.Deferred(...))` or `Result.Fail(ErrorCodes.NetworkUnavailable, …)` — it never throws into the UI and never loses local state.

**Condensed CONVENTIONS rules used here (so `CONVENTIONS.md` need not be opened):** §2 layering (`Animora.Sync.Custom` → `Animora.Sync.Abstractions` + `Animora.Application` + `Animora.Persistence.Sqlite` + `Animora.Contracts.Wire` only; **no Npgsql, no server project**); §4 C# style (`sealed` by default, `sealed record` DTOs, nullable honored, every async public method takes `CancellationToken ct = default` + `ConfigureAwait(false)`, no `.Result`/`.Wait()`, expected failures are `Result`/`Result<T>` with an `ErrorCode` not exceptions, time via injected `IClock`, ids are ULID via `IdGenerator`); §6 two pipelines never merged (this adapter is **state replication only**; business commands go through `ICommandTransport`, G05); §11 offline-first (the `Null*` fallback is mandatory and stays the default when unconfigured).

---

## Read before starting
- `backend-roadmap/PROGRESS.md` — top section only.
- `ARCHITECTURE.md` §8.2 (state-pull mechanics, two pipelines), §16-CT, §17-CT, §18-CT (the immutable-batch wire shape + checkpoint discipline), §PG (feed position vocabulary) — only these `§`.
- `STACK.md` §6 (EF Core 10 / client store), §11 (weak-client budgets, bounded memory) — only these `§`.
- `backend-roadmap/phases/PHASE-G01-sync-engine-abstractions.md` §Design decisions (the `IStateSyncEngine` / `SyncScope` / `SyncOutcome` / `SyncResult` contract this adapter implements) — the one sibling phase this phase plugs behind.
- (Nothing else. `Animora-UI/**`, `Database-Sampel/**`, other phase files and `windows-frontend-roadmap/phases/**` are out of bounds.)

## Goal

Create the client-side **`Animora.Sync.Custom`** project: the one real `IStateSyncEngine` that consumes the server's immutable 256-KiB batch surface (introduced by **S12**) over HTTP. It calls `GET /sync/manifest` → `POST /sync/batches` (open) → `GET /sync/batches/{id}/pages/{n}` (resumable, `If-None-Match`/`ETag`), verifies every page hash + the batch `ContentHash`, deserializes `SyncRecord`/`<Entity>SyncContract` pages with a **bounded memory** streaming reader, hands the materialized records to a `ISyncApplyStore` seam (atomic apply + checkpoint persistence is **G04's** implementation — this phase defines the seam and a no-op/buffer default so the download path is testable), and returns a `SyncResult` (`UpToDate` / `Applied` / `ResyncRequired` / `Deferred`). It references **no PostgreSQL driver and no server project**; it is selected over `NullSyncEngine` only when a base address is configured.

---

## §Reference pack (verbatim from the tree + the S12 phase, 2026-10-09)

```csharp
// Animora.Sync.Abstractions (G01) — the seam this adapter implements; reuse, never redefine
public readonly record struct SyncScope { public string Name { get; } public static readonly SyncScope Default; public static Result<SyncScope> Create(string name); public override string ToString() => Name; }
public enum SyncOutcome { UpToDate = 0, Applied = 1, Deferred = 2, ResyncRequired = 3 }
public sealed record SyncResult(SyncOutcome Outcome, long? Watermark, int BatchesApplied, int RecordsApplied, DateTimeOffset CompletedAtUtc, string? Detail)
{ public static SyncResult Deferred(DateTimeOffset nowUtc, string? detail = null); public static SyncResult UpToDate(long watermark, DateTimeOffset nowUtc);
  public static SyncResult Applied(long watermark, int batches, int records, DateTimeOffset nowUtc); public static SyncResult ResyncRequired(string reason, DateTimeOffset nowUtc); }
public interface IStateSyncEngine { bool IsConfigured { get; } Task<Result<SyncResult>> PullAsync(SyncScope scope, CancellationToken ct = default); }
public sealed class NullSyncEngine(IClock clock) : IStateSyncEngine { /* IsConfigured => false; PullAsync => Deferred */ }
public static class SyncAbstractionsServiceCollectionExtensions { public static IServiceCollection AddAnimoraSyncEngine(this IServiceCollection services); } // TryAddSingleton<IStateSyncEngine, NullSyncEngine>

// Animora.Domain.Results (A02) — reuse
public readonly record struct ErrorCode(string Code, string Category) { public const string Validation="Validation", NotFound="NotFound", Conflict="Conflict", Forbidden="Forbidden", Integrity="Integrity", Infrastructure="Infrastructure", Entitlement="Entitlement"; public static readonly ErrorCode None; public bool IsNone { get; } }
public readonly record struct Result    { IsSuccess, IsFailure, Error, Detail; static Ok(); static Fail(ErrorCode, string? detail = null); }
public readonly record struct Result<T> { IsSuccess, IsFailure, T? Value, Error, Detail; static Ok(T); static Fail(ErrorCode, string? detail = null); implicit operator Result<T>(T); }
// ErrorCodes reused: ValidationFailed, ConcurrencyConflict; S12 added RESYNC_REQUIRED (Conflict), BATCH_NOT_FOUND (NotFound). This phase ADDS the client-transport codes (see §Design decisions).

// Animora.Domain.Time (A02) — time is always injected
public interface IClock { DateTimeOffset UtcNow { get; } }

// Animora.Application.Ports (C11) — the local last-synced marker; G03 integrates it, G02 does NOT touch it
public interface ISyncCursorStore { Task<DateTimeOffset?> ReadAsync(CancellationToken ct = default); Task MarkSyncedAsync(DateTimeOffset whenUtc, CancellationToken ct = default); }

// Animora.Contracts.Wire (A01) — the contract-version header the adapter must send
public static class WireContract { public const int Version = 2 /* after S06 */; public const string HeaderName = "X-Animora-Contract-Version"; }
```

```csharp
// ====== S12 server surface this adapter consumes (verbatim from PHASE-S12 §Design decisions — the WIRE contract, not an impl ref) ======
// All live in Animora.Contracts.Wire.Sync (introduced by S12; this client project references Contracts.Wire and REUSES them)
namespace Animora.Contracts.Wire.Sync;
public sealed record SyncManifest(int ContractVersion, long Epoch, long Generation, IReadOnlyList<string> ProjectionOrder, int PageTargetBytes);
public sealed record OpenBatchRequest(long Epoch, long FromSequence /* client checkpoint watermark, 0 = none */);
// OpenBatchResponse is a discriminated shape serialized with a "kind" discriminator — the three cases:
public sealed record BatchReady(string BatchId, long Epoch, long Generation, long FromSequenceExclusive, long Watermark, int PageCount, string ContentHash);
public sealed record UpToDate(long Watermark);
public sealed record ResyncRequired(string Reason /* EPOCH_CHANGED | BEYOND_RETENTION | CONTRACT_UNSUPPORTED */, long Epoch);
public sealed record SyncPage(string BatchId, int PageOrdinal, int PageCount, IReadOnlyList<SyncRecord> Records, string PageHash);
public sealed record SyncRecord(string Entity, string EntityId, char Op /* 'U' | 'D' */, string? Version, DateTimeOffset? UpdatedAt, bool Deleted, System.Text.Json.JsonElement? Payload);
// per-entity payload DTOs: <Entity>SyncContract (PetSyncContract, OwnerSyncContract, …) — one per SyncContractCatalog entry; TenantId NEVER present.

// Endpoints (rate-limit policy "sync", [Authorize] + tenant context + DeviceActive):
//   GET  /sync/manifest                                  -> SyncManifest
//   POST /sync/batches          body OpenBatchRequest    -> OpenBatchResponse (BatchReady | UpToDate | ResyncRequired)
//   GET  /sync/batches/{batchId}/pages/{ordinal}         -> SyncPage ; ETag = PageHash ; Cache-Control: private, immutable ; honours If-None-Match (304)
//   POST /sync/snapshots        body (none / epoch)      -> OpenBatchResponse (BatchReady full-snapshot, FromSequenceExclusive = 0)
// Page files are gzip on the server; the HTTP layer may serve gzip via Content-Encoding (HttpClient auto-decompresses) — the adapter
// verifies PageHash against the DECODED JSON page body it deserializes, matching S12's SyncPage.PageHash semantics as exposed on the wire.
// Unsupported contract version -> 426 (client maps to ResyncRequired(CONTRACT_UNSUPPORTED)). Foreign/absent batch -> 404.
```

```xml
<!-- Project layout facts (verbatim, 2026-10-09) -->
<!-- src/Animora.Sync.Abstractions/*.csproj (G01): refs Domain + Contracts.Wire + Microsoft.Extensions.DependencyInjection -->
<!-- src/Animora.Client.Services/*.csproj (G01 added the Sync.Abstractions ProjectReference) -->
<!-- src/Animora.Desktop/App.axaml.cs ConfigureServices: calls services.AddAnimoraSyncEngine(); (G01) -->
<!-- tests/Animora.Sync.Tests/*.csproj: xunit + Test.Sdk, ProjectReference to Sync.Abstractions (G01) -->
```

## §Design decisions

- **New project `Animora.Sync.Custom`** (ARCH §6 naming lock, brief §Naming lock). `src/Animora.Sync.Custom/Animora.Sync.Custom.csproj` references **exactly four** projects: `Animora.Sync.Abstractions`, `Animora.Application`, `Animora.Persistence.Sqlite`, `Animora.Contracts.Wire`; packages: `Microsoft.Extensions.Http` + `Microsoft.Extensions.DependencyInjection` (versions from `Directory.Packages.props`). **No `Npgsql`, no `CommunityToolkit.Datasync`, no reference to any `Animora.Server*` / `Animora.Persistence.PostgreSql` project.** Added to `Animora.sln`. `tests/Animora.Sync.Tests` gains a `ProjectReference` to `Animora.Sync.Custom`.
- **`ServerEndpointOptions`** (`sealed record`): `string? BaseAddress`, `TimeSpan RequestTimeout = 30s`, `int MaxBatchPages = 4096`, `int MaxPageBytes = 262144` (mirrors S12's 256-KiB target; a page exceeding it is rejected `Result.Fail(SyncProtocolViolation)`). `IsConfigured => !string.IsNullOrWhiteSpace(BaseAddress)`. Bound from configuration key `Animora:Server`.
- **Typed client `SyncFeedClient`** over an injected `HttpClient` (named `"animora-sync"` via `IHttpClientFactory`): sets `BaseAddress`, the `WireContract.HeaderName` request header = `WireContract.Version`, `Accept: application/json`, and `AutomaticDecompression = GZip` on the handler. Three methods, each returns `Result<T>` (never throws for an expected HTTP status): `Task<Result<SyncManifest>> GetManifestAsync(ct)`; `Task<Result<OpenBatchOutcome>> OpenBatchAsync(long epoch, long fromSequence, ct)` where `OpenBatchOutcome` is a local `sealed record`/union mapping the three S12 cases (`Ready(BatchReady)`, `UpToDate(long watermark)`, `Resync(string reason, long epoch)`); `Task<Result<SyncPage?>> GetPageAsync(string batchId, int ordinal, string? ifNoneMatch, ct)` (returns `Ok(null)` on `304 Not Modified`). HTTP status mapping: `401/403` → `Fail(Unauthorized)`; `404` → `Fail(ErrorCodes.BatchNotFound)` (reuse S12's code); `426` → `OpenBatchOutcome.Resync("CONTRACT_UNSUPPORTED", epoch)`; `429`/`503` with `Retry-After` → `Fail(ServerBusy, detail=retry-after)`; network/timeout/`HttpRequestException`/`TaskCanceledException`(non-user) → `Fail(NetworkUnavailable)`.
- **`IPageIntegrity`** — `static` SHA-256 helper `ComputePageHash(ReadOnlySpan<byte> decodedJsonUtf8)` and `ComputeContentHash(IReadOnlyList<string> pageHashes)` (SHA-256 of the concatenated lowercase-hex page hashes), matching S12's definitions. The adapter **verifies** each downloaded page's hash against the server-declared `SyncPage.PageHash` and the running content hash against `BatchReady.ContentHash`; any mismatch ⇒ `Result.Fail(SyncIntegrityFailed)` and the batch is abandoned (no partial apply).
- **`ISyncApplyStore`** — the seam between "download + verify" (this phase) and "atomic apply + checkpoint" (G04). Declared here:
  ```csharp
  public interface ISyncApplyStore
  {
      /// <summary>The client's current checkpoint for the scope: (Epoch, Watermark) it has fully applied; (0,0) if never.</summary>
      Task<Result<SyncCheckpoint>> ReadCheckpointAsync(SyncScope scope, CancellationToken ct = default);
      /// <summary>Begin a batch apply session; returns a sink pages are streamed into (bounded — never holds the whole batch).</summary>
      Task<Result<ISyncApplySession>> BeginAsync(SyncScope scope, BatchReady batch, CancellationToken ct = default);
  }
  public interface ISyncApplySession : IAsyncDisposable
  {
      Task<Result> AcceptPageAsync(SyncPage page, CancellationToken ct = default);   // apply/stage one verified page
      Task<Result<SyncCheckpoint>> CommitAsync(CancellationToken ct = default);       // atomic: base + checkpoint advance together
      Task<Result> AbortAsync(CancellationToken ct = default);                        // discard staged work, checkpoint unchanged
  }
  public readonly record struct SyncCheckpoint(long Epoch, long Watermark);
  ```
  **Default impl `BufferingSyncApplyStore`** (this phase): verifies + counts records, holds the **last checkpoint in memory only**, never mutates the SQLite store — enough to make `PullAsync` return a real `SyncResult` and to test the download/verify/resume path without G04. `BeginAsync` streams pages (bounded) and `CommitAsync` returns `(batch.Epoch, batch.Watermark)`. G04 replaces this with the real atomic-apply store via `Replace`/`AddSingleton` (documented in G04 Out-of-scope note); the engine never changes.
- **`HttpStateSyncEngine : IStateSyncEngine`** (`sealed`): `IsConfigured => _options.IsConfigured`. `PullAsync(scope, ct)` algorithm (checkpoint-disciplined, ARCH §8.2 / §17-CT): (1) if not configured ⇒ `Result.Ok(SyncResult.Deferred(clock.UtcNow, "No server endpoint configured (offline)."))` — defensive; the DI wiring means a configured engine is only registered when `IsConfigured`; (2) read checkpoint `(epoch0, from0)` via `ISyncApplyStore.ReadCheckpointAsync`; (3) `GetManifestAsync` — if `manifest.Epoch != epoch0 && epoch0 != 0` ⇒ treat as resync below; (4) `OpenBatchAsync(manifest.Epoch, from0)` → **UpToDate** ⇒ `SyncResult.UpToDate(watermark, now)`; **Resync(reason)** ⇒ `SyncResult.ResyncRequired(reason, now)` (orchestrator G03 escalates to `POST /sync/snapshots` via G04 — not called here); **Ready(batch)** ⇒ open an apply session, loop `ordinal = 0..PageCount-1` calling `GetPageAsync` (resumable: a 304 reuses the already-staged page by its ETag; retry a transient page failure up to 3× with jittered backoff from `IClock` — no `Thread.Sleep`), `AcceptPageAsync` each verified page, verify running content hash == `batch.ContentHash` after the last page, then `CommitAsync` ⇒ `SyncResult.Applied(checkpoint.Watermark, batchesApplied: 1, recordsApplied, now)`. Any `Fail` aborts the session (`AbortAsync`) and returns the failure as `Result.Fail(...)` (checkpoint unchanged — never a torn apply). Honours `ct` throughout; a user cancel surfaces as `OperationCanceledException` only if the caller cancels (never swallowed).
- **No `PushAsync`, no command path, no money/stock effect** ever travels through this engine (ARCH §8.1 two permanent pipelines). Business intent is G05's `ICommandTransport`.
- **New ErrorCodes (+4, region "Sync transport"):** `NetworkUnavailable = new("NETWORK_UNAVAILABLE", ErrorCode.Infrastructure)`, `ServerBusy = new("SERVER_BUSY", ErrorCode.Infrastructure)`, `SyncIntegrityFailed = new("SYNC_INTEGRITY_FAILED", ErrorCode.Integrity)`, `SyncProtocolViolation = new("SYNC_PROTOCOL_VIOLATION", ErrorCode.Integrity)`. `Unauthorized` is reused if it already exists (`grep ErrorCodes.cs`); if not, add `Unauthorized = new("UNAUTHORIZED", ErrorCode.Forbidden)` in the same region. `BatchNotFound`/`ResyncRequired` are **reused** from S12 (one-introducer rule — never re-declared).
- **DI** — `public static class SyncCustomServiceCollectionExtensions { public static IServiceCollection AddAnimoraHttpSyncEngine(this IServiceCollection services, Action<ServerEndpointOptions>? configure = null) }`: binds `ServerEndpointOptions`, adds the named `HttpClient`, registers `SyncFeedClient`, `TryAddSingleton<ISyncApplyStore, BufferingSyncApplyStore>`, then **conditionally** `services.AddSingleton<IStateSyncEngine, HttpStateSyncEngine>()` **only if** `options.IsConfigured` — otherwise it leaves the G01 `NullSyncEngine` (TryAdd) in place. Called from `Animora.Desktop.App.ConfigureServices` **after** `AddAnimoraSyncEngine()` (so the Null fallback is registered first and this replaces it only when configured). `Animora.Desktop` gains **no** direct store/HTTP type — it reaches `Animora.Sync.Custom` through a single `ProjectReference` added here (Desktop → Sync.Custom), and the HTTP lives entirely inside Sync.Custom.
- **Not in this phase:** real atomic apply into SQLite + the persisted checkpoint table (**G04** `ISyncApplyStore` impl + migration), the scheduler / jitter / `ISyncStatusService` wiring (**G03**), conflict resolution & non-destructive overlay (**G04**), the full-resync `POST /sync/snapshots` call orchestration (**G04** triggered by G03), auth token acquisition on the `HttpClient` (**G06** adds the bearer handler), command transport (**G05**).

## §Implementation plan

1. `dotnet` solution: add project `src/Animora.Sync.Custom/Animora.Sync.Custom.csproj` (SDK `Microsoft.NET.Sdk`, four `ProjectReference`s + the two package refs). Add to `Animora.sln` beside `Animora.Sync.Abstractions`.
2. Create, under `src/Animora.Sync.Custom/`: `ServerEndpointOptions.cs`, `SyncFeedClient.cs` (typed client + `OpenBatchOutcome` union), `IPageIntegrity.cs` (static SHA-256 helpers), `ISyncApplyStore.cs` (+ `ISyncApplySession`, `SyncCheckpoint`), `BufferingSyncApplyStore.cs`, `HttpStateSyncEngine.cs`, `SyncCustomServiceCollectionExtensions.cs` — exactly per §Design decisions (XML-doc each public member in one line, `ConfigureAwait(false)` throughout).
3. `src/Animora.Domain/Results/ErrorCodes.cs`: add the 4 (or `Unauthorized` 5) codes in a new `#region Sync transport`; never rename existing.
4. `src/Animora.Desktop/App.axaml.cs` `ConfigureServices`: add `services.AddAnimoraHttpSyncEngine();` directly **below** the existing `services.AddAnimoraSyncEngine();` (+ `using Animora.Sync.Custom;`). Add the `Animora.Desktop → Animora.Sync.Custom` `ProjectReference` in `Animora.Desktop.csproj`. Change no other line.
5. `tests/Animora.Sync.Tests/Animora.Sync.Tests.csproj`: add `ProjectReference` to `Animora.Sync.Custom`. Tests (names in Acceptance): `SyncFeedClientTests` (over a `HttpMessageHandler` fake — 200/304/404/426/429/503/network), `PageIntegrityTests`, `HttpStateSyncEnginePullTests` (UpToDate / Ready-download-apply / Resync / resume-from-304 / integrity-mismatch-abort / unconfigured-deferred), `SyncCustomDiTests` (configured ⇒ Http engine wins; unconfigured ⇒ NullSyncEngine stays), `SyncCustomLayeringTests`.
6. `scripts/sandbox/build.sh full` → 0 warnings / 0 errors; `scripts/sandbox/test.sh Sync` → green; `scripts/sandbox/run.sh` → `SMOKE RUN OK` (desktop still starts: unconfigured ⇒ Null engine).
7. `PROGRESS.md` per recipe below; commit via `scripts/git/push.sh`.

## Files to create / modify

| Path | Contents |
|---|---|
| `src/Animora.Sync.Custom/Animora.Sync.Custom.csproj` (new) | four ProjectRefs (Sync.Abstractions, Application, Persistence.Sqlite, Contracts.Wire) + Http/DI packages; no Npgsql/server ref |
| `src/Animora.Sync.Custom/ServerEndpointOptions.cs` (new) | `BaseAddress`, timeout, page limits, `IsConfigured` |
| `src/Animora.Sync.Custom/SyncFeedClient.cs` (new) | typed `HttpClient` wrapper; manifest/open/page; `OpenBatchOutcome` union; HTTP-status→`Result` map |
| `src/Animora.Sync.Custom/IPageIntegrity.cs` (new) | SHA-256 page-hash + content-hash helpers |
| `src/Animora.Sync.Custom/ISyncApplyStore.cs` (new) | `ISyncApplyStore`, `ISyncApplySession`, `SyncCheckpoint` seam |
| `src/Animora.Sync.Custom/BufferingSyncApplyStore.cs` (new) | in-memory default (no SQLite write) — G04 replaces |
| `src/Animora.Sync.Custom/HttpStateSyncEngine.cs` (new) | real `IStateSyncEngine`: manifest→open→pages→verify→apply→`SyncResult` |
| `src/Animora.Sync.Custom/SyncCustomServiceCollectionExtensions.cs` (new) | `AddAnimoraHttpSyncEngine` (conditional real-engine registration) |
| `src/Animora.Domain/Results/ErrorCodes.cs` (edit) | +4/5 codes, new `#region Sync transport` |
| `src/Animora.Desktop/App.axaml.cs`, `src/Animora.Desktop/Animora.Desktop.csproj` (edit) | one registration line + using; one ProjectReference |
| `Animora.sln` (edit) | add `Animora.Sync.Custom` |
| `tests/Animora.Sync.Tests/Animora.Sync.Tests.csproj` + `*.cs` (edit/new) | ProjectReference + the five test classes |

## Contracts / schema added

No schema, no migration, no wire DTO (reuses S12's `Animora.Contracts.Wire.Sync` types). New public API: `Animora.Sync.Custom.{ServerEndpointOptions, SyncFeedClient, OpenBatchOutcome, IPageIntegrity, ISyncApplyStore, ISyncApplySession, SyncCheckpoint, BufferingSyncApplyStore, HttpStateSyncEngine, AddAnimoraHttpSyncEngine}`. ErrorCodes +4 (+1 if `Unauthorized` absent). Endpoints consumed (not introduced): `GET /sync/manifest`, `POST /sync/batches`, `GET /sync/batches/{id}/pages/{n}`.

## Rules that apply here

- **Layering (CONVENTIONS §2, `ARCHITECTURE.md` §6):** `Animora.Sync.Custom` references **only** `Animora.Sync.Abstractions` + `Animora.Application` + `Animora.Persistence.Sqlite` + `Animora.Contracts.Wire` (+ Http/DI packages); **never** Npgsql, EF-PostgreSQL, any `Animora.Server*`/`Animora.Persistence.PostgreSql` project, or `Animora.Desktop`. `Animora.Desktop`'s layering test (`Desktop_HasNoStoreOrNetworkReferences`) is satisfied because the `HttpClient` lives inside `Sync.Custom`, not Desktop — confirm the test's intent still holds (Desktop may reference Sync.Custom as a composition edge; the test asserts no *direct* `System.Net.Http`/EF type in Desktop code, which stays true).
- **C# style (§4):** `sealed` by default; `sealed record` DTOs; nullable honored; async public methods take `CancellationToken ct = default` + `ConfigureAwait(false)`; no `.Result`/`.Wait()`; expected failures as `Result` with an `ErrorCode` (HTTP faults mapped, never thrown to UI); time via injected `IClock` (backoff jitter derives from it, not `Thread.Sleep`); ids ULID via `IdGenerator`.
- **Two pipelines (§6, `ARCHITECTURE.md` §8.1):** state replication only — no `ICommandTransport`, no money/stock effect, no `PushAsync`.
- **Offline-first (§11):** unconfigured ⇒ Null engine stays; network faults ⇒ `Deferred`/`Fail` surfaced to status, never a crash, never lost local state.

## Out of scope (owning phase)

- Real atomic apply into SQLite + persisted checkpoint table + non-destructive overlay/shadow-generation + conflict policy → **G04** (replaces `BufferingSyncApplyStore`; the `POST /sync/snapshots` full-resync materialization lives there, triggered by the orchestrator).
- Scheduler / jitter policy / `SyncCursor` integration / `ISyncStatusService` wiring → **G03**.
- Bearer token acquisition + refresh on the `HttpClient`, device-active enforcement client-side → **G06**.
- Command transport + settlement → **G05**. Server batch export itself → **S12** (introduced there; reused here).
- Optional Datasync `/tables/*` adapter → deferred (ROADMAP §5, STACK G1 gate).

## Acceptance criteria (mechanical)

1. `scripts/sandbox/build.sh full` = 0 warnings / 0 errors; `scripts/sandbox/run.sh` = `SMOKE RUN OK` (desktop starts with the Null engine when unconfigured).
2. `scripts/sandbox/test.sh Sync` green with these named tests: `SyncFeedClientTests.Manifest_Returns_SyncManifest`, `SyncFeedClientTests.Open_MapsUpToDate_Ready_Resync`, `SyncFeedClientTests.Page_304_ReturnsNullOk`, `SyncFeedClientTests.Status404_MapsBatchNotFound`, `SyncFeedClientTests.Status426_MapsResyncContractUnsupported`, `SyncFeedClientTests.Status503_WithRetryAfter_MapsServerBusy`, `SyncFeedClientTests.NetworkFault_MapsNetworkUnavailable`, `PageIntegrityTests.ContentHash_IsSha256OfConcatenatedPageHashes`, `HttpStateSyncEnginePullTests.UpToDate_ReturnsUpToDate`, `HttpStateSyncEnginePullTests.Ready_DownloadsAppliesAll_ReturnsApplied`, `HttpStateSyncEnginePullTests.Resume_304Page_ReusesStaged`, `HttpStateSyncEnginePullTests.IntegrityMismatch_AbortsSession_NoCheckpointAdvance`, `HttpStateSyncEnginePullTests.Resync_ReturnsResyncRequired`, `HttpStateSyncEnginePullTests.Unconfigured_ReturnsDeferred`, `SyncCustomDiTests.Configured_HttpEngineWins`, `SyncCustomDiTests.Unconfigured_NullEngineStays`.
3. `grep -rn "Npgsql\|CommunityToolkit.Datasync\|Animora.Server\|Animora.Persistence.PostgreSql" src/Animora.Sync.Custom` returns nothing; `grep -c "ProjectReference" src/Animora.Sync.Custom/Animora.Sync.Custom.csproj` = 4.
4. `grep -c "NETWORK_UNAVAILABLE\|SERVER_BUSY\|SYNC_INTEGRITY_FAILED\|SYNC_PROTOCOL_VIOLATION" src/Animora.Domain/Results/ErrorCodes.cs` = 4; `grep -rn "RESYNC_REQUIRED\|BATCH_NOT_FOUND" src/Animora.Sync.Custom` shows **references only** (no `new(` declaration — reused from S12).
5. `tests/Animora.Domain.Tests` `LayeringRulesTests.Desktop_HasNoStoreOrNetworkReferences` still passes; `grep -rn "PushAsync" src/Animora.Sync.Custom` returns nothing (pull-only engine).

## PROGRESS.md update

At the end of this phase: set the `G02` row in "Phase status" to `✅ Done` (add date + one-line summary), advance **"Next phase" → `G03`**, append one `### G02` entry to "Repo state log" (what exists now: the `Animora.Sync.Custom` project + its public types, the DI call site, the 4 new ErrorCodes, the test classes), log any deviation in the "Decision & deviation log", then commit with `scripts/git/push.sh -b genspark_ai_developer "feat(sync): G02 immutable-batch HTTP adapter"`.
