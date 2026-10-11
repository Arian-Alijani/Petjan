# Execution Plan — Phases G03 + G04 (client sync orchestration, status, non-destructive apply, conflict policy)

> **Audience: the implementing AI model.** This file is self-contained and pre-resolved against the live tree
> (2026-10-10). Every divergence between the two phase files' "reference pack" and the *actual* code has already
> been chased down and is written below as **hard fact** — do **not** re-open those seams to "verify"; trust
> this file, implement top-to-bottom. Only re-read a file if a *compile error* contradicts a snippet here (then
> fix the smallest thing + log it in the phase's PROGRESS deviation note).
>
> **Source phase files** (read once for the prose rationale, then follow THIS plan):
> `backend-roadmap/phases/PHASE-G03-sync-orchestration-status.md`, `…/PHASE-G04-conflict-resolution-non-destructive-pull.md`.
>
> **LOCKED RULE (project-critical): after finishing EACH task below, run `scripts/git/push.sh "<msg>"` on branch
> `genspark_ai_developer`. Not pushing = project death.** The very first push must create/switch the branch:
> `scripts/git/push.sh -b genspark_ai_developer "<msg>"`; every later push can omit `-b` (already on it). If push
> exits `3` → call the `setup_github_environment` tool once, re-run. If it exits `2` → resolve rebase, re-run.

---

## 0. Pre-resolved baseline facts (DO NOT re-derive — these are the real tree, verified 2026-10-10)

### 0.1 Build / test / run (memory-capped ~985 MiB — NEVER raw `dotnet build`)
- `scripts/sandbox/build.sh full`  → must print 0 warnings / 0 errors.
- `scripts/sandbox/test.sh <filter>` → `<filter>` matches test **FQN substring** (e.g. `Sync`, `Persistence`, `Layering`). Prints `TESTS OK`.
- `scripts/sandbox/run.sh` → desktop smoke, prints `SMOKE RUN OK`.
- `scripts/sandbox/install-prereqs.sh` → run once if tools missing (idempotent).
- `dotnet-ef` is at `.sandbox/tools/dotnet-ef` — **only** for the G04 migration. Never `dotnet tool install`.
- **Keep it cheap**: run `build.sh fast` during iteration; run `build.sh full` + targeted `test.sh` only at a task's acceptance gate. No full-suite run unless a task says so.

### 0.2 Project reference graph (this governs WHERE every new type may live)
| Project | References (project) | Key consequence |
|---|---|---|
| `Animora.Sync.Abstractions` | `Animora.Domain` only | Lowest sync layer. Visible to BOTH Client.Services and Sync.Custom. |
| `Animora.Sync.Custom` | Sync.Abstractions, Application, Persistence.Sqlite, Contracts.Wire | Has EF/SQLite + HTTP. **Does NOT reference Client.Services.** |
| `Animora.Client.Services` | Application, Contracts, Persistence.Sqlite, Infrastructure.Local, **Sync.Abstractions** | **Does NOT reference Sync.Custom** (no `System.Net.Http`). |
| `Animora.Application` | Domain, Contracts, Contracts.Wire | No sync ref at all. |
| `tests/Animora.Sync.Tests` | Sync.Abstractions, Sync.Custom | **Cannot see Client.Services.** |
| `tests/Animora.Application.Tests` | Application, Infrastructure.Local, Persistence.Sqlite, **Client.Services**, Persistence.Tests | Sees Client.Services + (transitively) Sync.Abstractions. |
| `tests/Animora.Persistence.Tests` | (persistence stack) | Hosts SQLite fixtures. |

> **Decision forced by the graph (overrides both phase files' "put tests in Animora.Sync.Tests"):**
> - `SyncOrchestrator` + scheduler + the extended `LocalSyncStatusService` live in **`Animora.Client.Services`**, so **their tests MUST live in `tests/Animora.Application.Tests`** (the only test project that references Client.Services). Put them in a new folder `tests/Animora.Application.Tests/Sync/`.
> - `SqliteSyncApplyStore` + `ShadowResyncHandler` live in Sync.Custom → their tests go in **`tests/Animora.Sync.Tests`** (which refs Sync.Custom). The replica-store SQLite fixture: Sync.Tests already builds; if it needs a live encrypted-SQLite fixture, add the `Animora.Persistence.Sqlite` ProjectReference to `tests/Animora.Sync.Tests/Animora.Sync.Tests.csproj` (it is a sibling layer, allowed). Conflict-policy tests (`ConflictResolver`, pure) can go in `tests/Animora.Application.Tests/Sync/` since the resolver lives in Application.

> **Decision forced by the graph (overrides G03/G04 wording "ISyncResyncHandler in Client.Services"):**
> `ISyncResyncHandler` + `NullSyncResyncHandler` MUST be introduced in **`Animora.Sync.Abstractions`**, NOT in Client.Services. Reason: G04's real `ShadowResyncHandler` lives in `Sync.Custom`, which cannot reference Client.Services, but both Client.Services and Sync.Custom reference Sync.Abstractions. This is the only placement that lets G03 introduce the seam and G04 supply the impl without a new cross-reference. (One-introducer rule preserved: the interface is introduced ONCE, in Abstractions.)

### 0.3 Exact existing types (verbatim — reuse, never redefine)
```csharp
// Animora.Sync.Abstractions
public enum SyncOutcome { UpToDate=0, Applied=1, Deferred=2, ResyncRequired=3 }
public sealed record SyncResult(SyncOutcome Outcome, long? Watermark, int BatchesApplied, int RecordsApplied, DateTimeOffset CompletedAtUtc, string? Detail)
{   // factories exist: SyncResult.Deferred(now,detail?), .UpToDate(wm,now), .Applied(wm,batches,records,now), .ResyncRequired(reason,now)
}
public readonly record struct SyncScope { public string Name {get;} /* default→"default" */ public static readonly SyncScope Default; public static Result<SyncScope> Create(string name); }
public interface IStateSyncEngine { bool IsConfigured {get;} Task<Result<SyncResult>> PullAsync(SyncScope scope, CancellationToken ct = default); }
public sealed class NullSyncEngine(IClock clock) : IStateSyncEngine { /* IsConfigured=false, PullAsync→Deferred */ }

// Animora.Contracts.Sync (frontend seam — UNCHANGED signature)
public enum SyncState { Idle, Syncing, Synced, Pending, Error, Offline }
public interface ISyncStatusService { SyncState State {get;} DateTimeOffset? LastSyncedAt {get;} event EventHandler? StateChanged; Task SyncNowAsync(CancellationToken ct = default); }

// Animora.Application.Ports.ISyncCursorStore (C11)
public interface ISyncCursorStore { Task<DateTimeOffset?> ReadAsync(CancellationToken ct=default); Task MarkSyncedAsync(DateTimeOffset whenUtc, CancellationToken ct=default); }

// Animora.Application.UseCases.Sync.PendingCommandSummary (fields matter for status mapping!)
public sealed record PendingCommandSummary(int Total, int PendingSettlement, int Rejected, int Conflict, int NeedsAttention, DateTimeOffset? OldestCreatedUtc);
// Animora.Application.Commands.ICommandJournal.GetSummaryAsync() -> Result<PendingCommandSummary>  (Total>0 ⇒ Pending)

// Animora.Application.Commands.LocalCommandProcessor (the REAL drain used by LocalSyncStatusService today)
public sealed record CommandProcessorPass(int Claimed, int Settled, int Deferred, int Failed, int SkippedUnsettledDependency);
public sealed class LocalCommandProcessor(ICommandJournal journal, ICommandTransport transport) { Task<Result<CommandProcessorPass>> RunPassAsync(CancellationToken ct=default); }

// Animora.Domain.Time.IClock { DateTimeOffset UtcNow {get;} }
// Animora.Domain.Results: Result, Result<T>, ErrorCode, ErrorCodes
```

```csharp
// Animora.Client.Services.Sync.LocalSyncStatusService — THE REAL ctor + body today (G03 EXTENDS this file):
public sealed class LocalSyncStatusService(IServiceScopeFactory scopes, ICommandTransport transport, IClock clock)
    : ISyncStatusService, IDisposable
{
    // SemaphoreSlim _pass(1,1); private sealed record Snapshot(SyncState State, DateTimeOffset? LastSyncedAt);
    // initial snapshot = (transport is NullCommandTransport ? Offline : Idle, null)
    // State/LastSyncedAt are Volatile reads; StateChanged fires on snapshot change (Interlocked.Exchange).
    // RefreshAsync => ExecuteAsync(runPass:false); SyncNowAsync => ExecuteAsync(runPass:true).
    // ExecuteAsync: await _pass.WaitAsync(ct); open AsyncScope; read ISyncCursorStore.
    //   if runPass: Publish(Syncing,last); result = LocalCommandProcessor.RunPassAsync(ct);
    //       if result.IsFailure || result.Value!.Failed>0 ⇒ Publish(Error,last); return;
    //       await cursor.MarkSyncedAsync(clock.UtcNow, ct);
    //   last = cursor.ReadAsync; summary = ICommandJournal.GetSummaryAsync;
    //   state = summary.IsFailure ? Error : transport is NullCommandTransport ? Offline : summary.Value!.Total>0 ? Pending : Synced;
    //   Publish(state,last). Catches OperationCanceledException→Publish(previous); other Exception→Publish(Error,last).
}
```
> **NOTE the divergence from G03's reference pack:** the file does NOT drain via a separate `ICommandJournal` call; it drains via `LocalCommandProcessor.RunPassAsync` and reads `PendingCommandSummary.Total`. Keep that real mechanism; just add the state-pull pass alongside it.

```csharp
// Animora.Sync.Custom (G02) — the seams G04 FILLS; reuse, never redefine
public readonly record struct SyncCheckpoint(long Epoch, long Watermark);   // (0,0)=never applied
public interface ISyncApplyStore {
    Task<Result<SyncCheckpoint>> ReadCheckpointAsync(SyncScope scope, CancellationToken ct=default);
    Task<Result<ISyncApplySession>> BeginAsync(SyncScope scope, BatchReady batch, CancellationToken ct=default);
}
public interface ISyncApplySession : IAsyncDisposable {
    Task<Result> AcceptPageAsync(SyncPage page, CancellationToken ct=default);
    Task<Result<SyncCheckpoint>> CommitAsync(CancellationToken ct=default);
    Task<Result> AbortAsync(CancellationToken ct=default);
}
public sealed class BufferingSyncApplyStore : ISyncApplyStore { /* G02 in-memory default — G04 REPLACES via DI Replace */ }
// SyncFeedClient real methods (NO OpenSnapshotAsync!):
//   Task<Result<SyncManifest>>     GetManifestAsync(ct)
//   Task<Result<OpenBatchOutcome>> OpenBatchAsync(long epoch, long fromSequence, ct)   // full snapshot = fromSequence:0
//   Task<Result<SyncPage?>>        GetPageAsync(string batchId, int ordinal, string? ifNoneMatch, ct)
public enum OpenBatchKind { Ready=0, Current=1, Resync=2 }
public sealed record OpenBatchOutcome(OpenBatchKind Kind, BatchReady? Batch, long Watermark, string? Reason, long Epoch);
public sealed record ServerEndpointOptions { public string? BaseAddress {get;set;} public bool IsConfigured {get;} /* +timeouts/caps */ public const string SectionName="Animora:Server"; }

// Animora.Contracts.Wire.Sync — record shapes (note the exact member names)
public sealed record SyncRecord(string Entity, string EntityId, char Op /* 'U'|'D' */, string? Version, DateTimeOffset? UpdatedAt, bool Deleted, JsonElement? Payload);
public sealed record SyncPage(string BatchId, int PageOrdinal, int PageCount, IReadOnlyList<SyncRecord> Records, string PageHash);  // PageOrdinal (NOT Ordinal)
public sealed record BatchReady(string BatchId, long Epoch, long Generation, long FromSequenceExclusive, long Watermark, int PageCount, string ContentHash);

// Animora.Persistence.Sqlite.Context.AnimoraLocalDbContext(DbContextOptions<AnimoraLocalDbContext>, ILocalTenantContext tenant, IClock clock)
//   Design-time factory: LocalDbContextFactory : IDesignTimeDbContextFactory<AnimoraLocalDbContext>.
// Animora.Persistence.Sqlite.Entities.SyncCursor [LocalOnly] — C11, G04 does NOT touch it.
//   SyncCursor.DefaultScope == "default". Store: SyncCursorStore : ISyncCursorStore (atomic upsert, never lowers).
// Animora.Domain.Primitives.LocalOnlyAttribute ; IdGenerator.NewId() (ULID).
```

### 0.4 ErrorCodes — pre-checked
- Existing (reuse, do NOT re-add): `NetworkUnavailable` (`NETWORK_UNAVAILABLE`, Infrastructure), `ServerBusy` (`SERVER_BUSY`, Infrastructure), `SyncIntegrityFailed` (Integrity), `SyncProtocolViolation` (Integrity), `ResyncRequired` (`RESYNC_REQUIRED`, Conflict), `BatchNotFound` (NotFound).
- **`AppointmentSlotConflict` ALREADY EXISTS** at `src/Animora.Domain/Results/ErrorCodes.cs` line ~233 (`APPOINTMENT_SLOT_CONFLICT`). → **G04 adds exactly TWO codes** (`SyncApplyConflict`=`SYNC_APPLY_CONFLICT`/Conflict, `SyncShadowValidationFailed`=`SYNC_SHADOW_VALIDATION_FAILED`/Integrity). This exactly satisfies G04 acceptance criterion 4 (`grep -c … = 2`). Reuse `AppointmentSlotConflict` (one-introducer).
- `ErrorCode` severity consts: `ErrorCode.Validation`, `ErrorCode.Conflict`, `ErrorCode.Integrity`, `ErrorCode.Infrastructure`, `ErrorCode.NotFound` (strings).

### 0.5 Migration ordinal (G04)
- Physical head is `031` (`20261007120000__031_TreatmentPlans.cs`). Migrations `020,022,024–031` use a **double** underscore before `NNN` — match on the `NNN` token. **Next free client ordinal = `032`.** Server sequence (101+) is untouched.
- Migration is on **`AnimoraLocalDbContext`** (the client context). The plan files say `--context AnimoraDbContext`; the real class is **`AnimoraLocalDbContext`** — use that.

### 0.6 Desktop composition root (G03 wiring point)
`src/Animora.Desktop/App.axaml.cs` `ConfigureServices(IServiceCollection, LocalTestProfile?)` already has (≈ lines 401–404):
```csharp
services.AddAnimoraSyncEngine();        // G01 (Null engine, TryAdd)
services.AddAnimoraHttpSyncEngine();    // G02
services.AddSingleton<LocalSyncStatusService>();
services.AddSingleton<ISyncStatusService>(sp => sp.GetRequiredService<LocalSyncStatusService>());
```
G03 adds **one** line `services.AddAnimoraSyncOrchestration();` directly below `AddAnimoraHttpSyncEngine();`. `using Animora.Client.Services.Sync;` is already imported (line 48). G04 adds **no** new call site (the two `Replace` calls ride inside the existing `AddAnimoraHttpSyncEngine`/`AddAnimoraSyncOrchestration`).

### 0.7 PROGRESS.md
- `backend-roadmap/PROGRESS.md`: "Next phase" line ~144 currently says `G03`. Status rows: line ~228 `G03 ⬜ Not started`, line ~229 `G04 ⬜ Not started`. "Repo state log" is append-only; add one `### G03` then one `### G04` entry. Never rewrite older entries. Read only the top section + the one `### <id>` you touch.

---

## G03 — Sync orchestration & status

> **Scope:** a scheduler that periodically + on-demand drives `IStateSyncEngine.PullAsync` per scope; advances the C11
> `SyncCursor` (via `ISyncCursorStore.MarkSyncedAsync`) ONLY on `Applied`/`UpToDate`; recognises `ResyncRequired` and
> delegates to the `ISyncResyncHandler` seam (no-op here, G04 fills it); extends `LocalSyncStatusService` to surface ONE
> `SyncState` from BOTH state-pull outcome AND command backlog without merging the two pipelines; dormant + `Offline`
> when unconfigured. **No migration, no wire DTO, no ErrorCode.**

### G03 new files (all in `src/Animora.Client.Services/Sync/` unless noted)
Each new public member gets a one-line XML doc. All async publics: `CancellationToken ct = default` + `.ConfigureAwait(false)`. `sealed`. No `Thread.Sleep`, no per-call `new Random()`, no `.Result`/`.Wait()`.

1. **`src/Animora.Sync.Abstractions/ISyncResyncHandler.cs`** (NOT Client.Services — see §0.2):
   ```csharp
   public interface ISyncResyncHandler { Task<Result> HandleAsync(SyncScope scope, string reason, CancellationToken ct = default); }
   public sealed class NullSyncResyncHandler(/* optional ILogger */) : ISyncResyncHandler {
       public Task<Result> HandleAsync(SyncScope scope, string reason, CancellationToken ct = default) => Task.FromResult(Result.Ok()); // + one-line warn log if a logger is injected
   }
   ```
   (Uses `Animora.Domain.Results.Result` + `SyncScope` — both already in Abstractions/Domain. No new ref.)
2. **`Sync/SyncScheduleOptions.cs`** — `sealed record`: `bool Enabled = true`, `TimeSpan Interval = TimeSpan.FromMinutes(5)`, `TimeSpan MinInterval = TimeSpan.FromSeconds(30)`, `double JitterFraction = 0.2`, `TimeSpan MaxBackoff = TimeSpan.FromMinutes(15)`, `IReadOnlyList<string> Scopes = ["default"]`. Config section const `= "Animora:Sync"`.
3. **`Sync/IDelayProvider.cs`** — `public interface IDelayProvider { Task Delay(TimeSpan delay, CancellationToken ct = default); }` + `public sealed class TimeProviderDelayProvider(TimeProvider? timeProvider = null) : IDelayProvider` delegating to `(_timeProvider ?? TimeProvider.System).Delay`-style via `Task.Delay(delay, timeProvider, ct)`.
4. **`Sync/SyncOrchestrator.cs`** — `sealed class SyncOrchestrator(IStateSyncEngine engine, IServiceScopeFactory scopes, IClock clock, SyncScheduleOptions options, ISyncResyncHandler resync)`:
   - `Task<Result<SyncOutcome>> PullOnceAsync(SyncScope scope, CancellationToken ct = default)`:
     `var r = await engine.PullAsync(scope, ct);` if `r.IsFailure` → return `Result<SyncOutcome>.Fail(r.Error, r.Detail)`.
     switch `r.Value!.Outcome`:
       - `Applied` or `UpToDate` → open DI scope, `await scope.ServiceProvider.GetRequiredService<ISyncCursorStore>().MarkSyncedAsync(r.Value.CompletedAtUtc, ct)`; return `Ok(outcome)`. **(the cursor advance appears ONLY here — acceptance crit 4.)**
       - `Deferred` → return `Ok(Deferred)` (no cursor touch).
       - `ResyncRequired` → `await resync.HandleAsync(scope, r.Value.Detail ?? "resync", ct)` (do not advance cursor); return `Ok(ResyncRequired)` (surface the handler failure as the result only if you choose — keep it simple: return `Ok(ResyncRequired)` so status maps Error until resolved; if handler failed, still `Ok(ResyncRequired)`).
     Never throws for an expected engine result.
   - `Task<Result<SyncOutcome>> PullAllAsync(CancellationToken ct = default)`: iterate `options.Scopes` → `SyncScope.Create(name)` (fallback `SyncScope.Default` if the configured list is the single "default"); call `PullOnceAsync`; return the **worst** outcome (precedence for status: a `Fail` or `ResyncRequired` dominates `Deferred` dominates `Applied`/`UpToDate`) so the status mapper has one signal. Expose the last outcome for the status service (a volatile field `LastOutcome` + `LastResult` is acceptable, OR return it and let the caller store it).
5. **`Sync/SyncSchedulerHostedService.cs`** — `sealed class SyncSchedulerHostedService(IStateSyncEngine engine, SyncOrchestrator orchestrator, SyncScheduleOptions options, IDelayProvider delay, IClock clock) : BackgroundService`:
   - `ExecuteAsync(stoppingToken)`: if `!engine.IsConfigured || !options.Enabled` → `return;` (dormant, no loop, no socket).
   - else loop until cancelled: `var r = await orchestrator.PullAllAsync(stoppingToken);` compute next wait: base `Interval`, clamped to `>= MinInterval`; on `Fail` whose `Error` is `NetworkUnavailable`/`ServerBusy` **double** the current backoff up to `MaxBackoff` (reset to `Interval` on any success); apply ±`JitterFraction` jitter from a single seeded `Random` created once in the ctor (`new Random(…)` ONCE; never per-iteration); `await delay.Delay(wait, stoppingToken)`.
   - Catch `OperationCanceledException` when `stoppingToken.IsCancellationRequested` → exit cleanly.
   - (If G02's `ServerBusy` carries a `Retry-After` in the Result detail, honour it as a floor for the next wait — best-effort parse; not required if detail is absent.)
6. **`Sync/SyncOrchestrationServiceCollectionExtensions.cs`**:
   ```csharp
   public static IServiceCollection AddAnimoraSyncOrchestration(this IServiceCollection services, Action<SyncScheduleOptions>? configure = null)
   {
       var options = new SyncScheduleOptions(); configure?.Invoke(options);  // (record is immutable → build via 'with' or a mutable builder; simplest: a mutable options class OR bind then store a single instance)
       services.AddSingleton(options);
       services.TryAddSingleton<IDelayProvider, TimeProviderDelayProvider>();
       services.TryAddSingleton<ISyncResyncHandler, NullSyncResyncHandler>();   // G04 Replaces this
       services.AddSingleton<SyncOrchestrator>();
       services.AddHostedService<SyncSchedulerHostedService>();
       return services;
   }
   ```
   > If `SyncScheduleOptions` stays a `record` with init-only props, the `configure` delegate can't mutate it; either make it a plain `sealed class` with settable props (simplest, matches `ServerEndpointOptions` pattern) OR bind via `IOptions`. **Pick the `sealed class` with mutable props + sensible defaults** to mirror the existing `ServerEndpointOptions` idiom and keep DI trivial.

### G03 edit
7. **`src/Animora.Client.Services/Sync/LocalSyncStatusService.cs`** — extend without breaking the C11 ctor:
   - Add a **second constructor** (keep the existing 3-arg one intact so C11 tests compile) OR add optional params `IStateSyncEngine? engine = null, SyncOrchestrator? orchestrator = null`. **Prefer optional params on the primary ctor** (`(IServiceScopeFactory scopes, ICommandTransport transport, IClock clock, IStateSyncEngine? engine = null, SyncOrchestrator? orchestrator = null)`) — one ctor, back-compatible.
   - In `ExecuteAsync(runPass:true)`, after the existing `LocalCommandProcessor.RunPassAsync` drain pass and BEFORE computing final state: if `orchestrator is not null && engine is { IsConfigured: true }` → `var pull = await orchestrator.PullAllAsync(ct);` capture the outcome.
   - Final state mapping (combine both pipelines, two passes, never merged):
     1. If `summary.IsFailure` OR the command drain failed (`result.IsFailure || result.Value!.Failed > 0`) → `Error` (existing behaviour; keep it first).
     2. Else if the pull was a `Fail` or returned `ResyncRequired` → `Error`.
     3. Else if `transport is NullCommandTransport && (engine is null || !engine.IsConfigured)` → `Offline`.
     4. Else if `summary.Value!.Total > 0` → `Pending`.
     5. Else if last pull was `Applied`/`UpToDate` (or no engine but transport configured) → `Synced`.
     6. Else (`Deferred`/unconfigured engine) → `Offline`.
   - `LastSyncedAt` = cursor value (the orchestrator advanced it on `Applied`/`UpToDate`; the existing command pass also calls `MarkSyncedAsync` — fine, upsert never lowers).
   - Keep `StateChanged` + `Volatile`/`Interlocked` mechanics byte-for-byte. Keep the `catch` blocks.

### G03 tests → **`tests/Animora.Application.Tests/Sync/`** (NOT Sync.Tests — see §0.2)
Use fakes: a `FakeStateSyncEngine` (returns a scripted `Result<SyncResult>` sequence, `IsConfigured` togg/configurable), a `FakeDelayProvider` (records requested delays, completes instantly or on a manual pump), a `RecordingResyncHandler`. Reuse the existing in-memory SQLite / scope-factory test harness patterns already in `Animora.Application.Tests`/`Animora.Persistence.Tests` for `ISyncCursorStore` + `ICommandJournal`.
- `SyncOrchestratorTests.PullApplied_AdvancesCursor`
- `SyncOrchestratorTests.PullUpToDate_AdvancesCursor`
- `SyncOrchestratorTests.PullDeferred_DoesNotAdvanceCursor`
- `SyncOrchestratorTests.ResyncRequired_InvokesHandler_NoCursorAdvance`
- `SyncOrchestratorTests.PullFail_Surfaces_NoCursorAdvance`
- `SyncSchedulerTests.Dormant_WhenUnconfigured` (engine.IsConfigured=false ⇒ ExecuteAsync returns, zero pulls, zero delays)
- `SyncSchedulerTests.RunsOnInterval_WithJitter`
- `SyncSchedulerTests.Backoff_DoublesOnFault_CapsAtMax_ResetsOnSuccess`
- `SyncSchedulerTests.CancellationExitsCleanly`
- `SyncStatusIntegrationTests.PendingCommands_MapToPending`
- `SyncStatusIntegrationTests.Applied_NoBacklog_MapsSynced`
- `SyncStatusIntegrationTests.Unconfigured_MapsOffline`
- `SyncStatusIntegrationTests.Fail_MapsError`
- `SyncOrchestrationDiTests.RegistersSchedulerAndNullResyncHandler` (build a ServiceCollection, call `AddAnimoraSyncOrchestration`, assert `SyncOrchestrator`, a hosted service of type `SyncSchedulerHostedService`, and `ISyncResyncHandler`→`NullSyncResyncHandler` resolve).

### G03 mechanical acceptance (run ONLY at the G03 gate task)
- `scripts/sandbox/build.sh full` = 0/0; `scripts/sandbox/run.sh` = `SMOKE RUN OK` (scheduler dormant unconfigured).
- `scripts/sandbox/test.sh Sync` green (the Sync.Custom/Abstractions tests) AND the new Application.Tests Sync classes green — run `scripts/sandbox/test.sh Sync` then `scripts/sandbox/test.sh SyncOrchestrator` / `SyncScheduler` / `SyncStatus` (FQN substrings) to hit the new ones cheaply.
- `grep -rn "System.Net.Http\|HttpClient\|Npgsql" src/Animora.Client.Services/Sync` → nothing.
- `grep -rn "Thread.Sleep\|new Random()" src/Animora.Client.Services/Sync` → nothing (the single seeded `Random` is `new Random(seed)` with an arg, so `new Random()` with empty parens must not appear — construct it as `new Random(unchecked(...))` or `Random.Shared` is NOT allowed for determinism; use `new Random(seedValue)`).
- `grep -rn "MarkSyncedAsync" src/Animora.Client.Services/Sync/SyncOrchestrator.cs` → appears only in the Applied/UpToDate branch.
- `grep -rn "snapshots" src/Animora.Client.Services/Sync` → nothing (full-resync is G04).
- `tests/Animora.Domain.Tests` `LayeringRulesTests.Desktop_HasNoStoreOrNetworkReferences` still green; C11 `LocalSyncStatusService` existing tests still compile + pass.

---

## G04 — Conflict resolution & non-destructive pull

> **Scope:** real three-layer client replica store (`repl_base` / `repl_staging` / `repl_checkpoint`, all `[LocalOnly]`,
> one new migration `032`); `SqliteSyncApplyStore : ISyncApplyStore` applying a batch onto base + advancing checkpoint
> **atomically on ONE connection + transaction** (replaces `BufferingSyncApplyStore`); `ConflictResolver` encoding
> ARCH §8.5 (posted invoice/payment = reversal-only, UI-prefs = LWW only, inventory additive, slot rejected, demographics
> field-merge, ledger append-only); `ShadowResyncHandler : ISyncResyncHandler` building an encrypted shadow generation
> from a full snapshot then atomically switching (replaces `NullSyncResyncHandler`). +2 ErrorCodes. Pending overlay
> (the B02 command journal) is NEVER touched. Unconfigured ⇒ G02/G03 no-op defaults stay; replica store untouched.

### G04 ordering inside the phase (do these as sub-tasks T8–T12 below)
1. Replica entities + DbContext + configs (`src/Animora.Persistence.Sqlite/Replica/`), all `[LocalOnly]`, `repl_*` table prefix:
   - `ReplicaBase` — generic row keyed `(Entity, EntityId)`; cols: `Entity`, `EntityId`, `Version` (string?, opaque), `Payload` (string/JSON), `Deleted` (bool), `UpdatedAtUtc` (DateTimeOffset?). PK = composite `(Entity,EntityId)`.
   - `ReplicaStaging` — same shape + `BatchId` + `PageOrdinal`; holds the in-flight batch only.
   - `ReplicaCheckpoint` — `Scope` (PK), `Epoch` (long), `Watermark` (long), `Generation` (long).
   - Register `AnimoraReplicaDbContext(DbContextOptions<AnimoraReplicaDbContext>, ILocalTenantContext, IClock)` on the **same SQLCipher connection factory** as `AnimoraLocalDbContext` (reuse `LocalDbContextFactory` pattern + add a design-time factory `ReplicaDbContextFactory`). The staging + base + shadow generation all live in the **same encrypted DB file**.
   - `repl_*` tables must NOT collide with business tables and must be excluded from the business tenant filter (they're `[LocalOnly]`, use an explicit `Scope`/tenant predicate like `SyncCursorStore` does).
2. **Migration `032`**: `.sandbox/tools/dotnet-ef migrations add ReplicaStore --project src/Animora.Persistence.Sqlite --context AnimoraLocalDbContext --output-dir Migrations` → rename the generated file to the `__032_ReplicaStore` convention (match the double-underscore+NNN token used by 020/022/024–031; copy the exact timestamp-prefix style of `031`). **Verify by listing** `src/Animora.Persistence.Sqlite/Migrations/` that `032` is free before generating. The `repl_*` tables are created on the client context's model — so the entities above must be added to `AnimoraLocalDbContext`'s model OR the migration is generated on a context that includes them. **Simplest correct path:** put the three `repl_*` entities' `DbSet`s + configs on `AnimoraLocalDbContext` (one encrypted file, one migration chain) and keep a thin `AnimoraReplicaDbContext` only if a separate `DbContext` is genuinely needed for the atomic cross-context transaction; if one context suffices for the atomic apply, prefer ONE context (`AnimoraLocalDbContext`) and drop the separate replica context — fewer moving parts, still `[LocalOnly]`. Update any `ExpectedHead`/schema-version assertion the client uses (grep `ExpectedHead`/`031`/`MigrationsAssembly` in Persistence + tests and bump to `032`).
3. **`SqliteSyncApplyStore : ISyncApplyStore`** (`src/Animora.Sync.Custom/`? — NO: it needs EF/SQLite and lives with the store → put it in `src/Animora.Persistence.Sqlite/Replica/SqliteSyncApplyStore.cs`. Sync.Custom references Persistence.Sqlite, so DI can register it from the Sync.Custom extension.):
   - `ReadCheckpointAsync(scope)` → read `repl_checkpoint` row for scope; `(0,0)` if absent.
   - `BeginAsync(scope, batch)` → open a session over ONE `DbConnection` + `BeginTransaction`. Session streams pages.
   - `AcceptPageAsync(page)` → verify `page.BatchId`/`PageOrdinal` ordering (reuse the `BufferingSyncApplyStore` ordering/`SyncProtocolViolation` checks), upsert each `SyncRecord` into `repl_staging` (bounded per page — never hold the whole batch in memory).
   - `CommitAsync()` → in the SAME transaction: apply staging onto `repl_base` (`Op=='U'` upsert payload/version/updatedAt/Deleted=false; `Op=='D'` or `Deleted` ⇒ tombstone), advance `repl_checkpoint` to `(batch.Epoch, batch.Watermark, batch.Generation)`, clear this batch's staging, **then COMMIT**. A crash before COMMIT leaves base + checkpoint unchanged (A-RULE 022).
   - `AbortAsync()` → roll back the transaction; checkpoint unchanged.
   - **Never** touch any business table or the command journal (pending overlay untouched).
4. **`ConflictResolver` + policy catalog** (`src/Animora.Application/Sync/`): pure (no EF/HTTP) so it lives in Application and is unit-testable + usable from Application.Tests.
   - `ConflictClass.cs` enum: `UiPreference, PostedFinancial, AppendOnlyLedger, InventoryMovement, AppointmentSlot, Demographics`.
   - `ConflictPolicyCatalog.cs` — single source mapping `Entity` (the `SyncRecord.Entity` string) → `ConflictClass`, built from ARCH §8.5. (Map the known replicated entity names: owner/pet demographics → Demographics; inventory movement entities → InventoryMovement; ledger/journal → AppendOnlyLedger; posted invoice/payment → PostedFinancial; appointment slot → AppointmentSlot; UI prefs/display → UiPreference. Default for an unknown entity: pick the safest = treat as **non-LWW** / `Demographics`-style field-merge or simply "base applied, overlay untouched" — document the default.)
   - `ConflictResolver.cs` + `ConflictDecision` record: `Decide(SyncRecord inbound, <pending overlay info>) -> ConflictDecision`. Decision outcomes per class:
     - `UiPreference` → LWW (newer `UpdatedAt` wins).
     - `PostedFinancial` → **reversal-only**: mark the local post `NeedsAttention` / `FinancialEffect.Unknown`; correction must be a NEW reversing command. **NEVER** delete/overwrite the posted record. (No `DELETE`/`Remove`/`Overwrite` code path on this branch — acceptance crit 3.)
     - `AppendOnlyLedger` → cannot conflict (base applied, overlay untouched).
     - `InventoryMovement` → both apply (additive), on-hand re-derived.
     - `AppointmentSlot` → reject colliding local intent with `ErrorCodes.AppointmentSlotConflict`.
     - `Demographics` → field-level merge (base wins for fields the local command didn't touch; the command's fields stay pending). Not whole-row LWW.
   - The resolver returns `ConflictDecision`s; it NEVER mutates money/stock in place — financial corrections route back through the command pipeline (two-pipeline rule).
5. **`ShadowResyncHandler : ISyncResyncHandler`** (`src/Animora.Sync.Custom/ShadowResyncHandler.cs` — needs `SyncFeedClient` + the apply store):
   - `HandleAsync(scope, reason, ct)`: (1) `await _feed.OpenBatchAsync(epoch, fromSequence: 0, ct)` to open a **full snapshot** (there is NO `OpenSnapshotAsync`; a full snapshot IS `OpenBatchAsync` with `fromSequence: 0`; pick `epoch` from the current checkpoint via `ISyncApplyStore.ReadCheckpointAsync` or `0`). Expect `OpenBatchKind.Ready` with `batch.FromSequenceExclusive == 0` (full snapshot).
   - (2) download + verify ALL pages (`_feed.GetPageAsync(batchId, ordinal, null, ct)` loop 0..PageCount-1) into a **fresh shadow generation** (`repl_base` rows under a NEW `Generation` id, isolated like staging) while the current base + pending overlay stay live/readable.
   - (3) validate the shadow (row counts vs `batch.PageCount`/manifest, content hash `batch.ContentHash`, page ordering). On any mismatch → discard shadow, `Result.Fail(ErrorCodes.SyncShadowValidationFailed, …)`, live state untouched.
   - (4) **atomically switch** `repl_checkpoint` + active base generation to the shadow in ONE transaction — pending overlay preserved, active state never cleared before the replacement succeeds. Failure at any step → discard shadow, live base/overlay/checkpoint untouched.
6. **ErrorCodes** (`src/Animora.Domain/Results/ErrorCodes.cs`): add a `#region Sync apply/conflict (G04)` with EXACTLY:
   ```csharp
   public static readonly ErrorCode SyncApplyConflict = new("SYNC_APPLY_CONFLICT", ErrorCode.Conflict);
   public static readonly ErrorCode SyncShadowValidationFailed = new("SYNC_SHADOW_VALIDATION_FAILED", ErrorCode.Integrity);
   ```
   **Do NOT add `AppointmentSlotConflict`** (already exists ~line 233) — reuse it.
7. **DI (conditional on configured endpoint — same guard G02 uses, `options.IsConfigured`)**:
   - In `src/Animora.Sync.Custom/SyncCustomServiceCollectionExtensions.cs` `AddAnimoraHttpSyncEngine`: when `options.IsConfigured`, `services.Replace(ServiceDescriptor.Singleton<ISyncApplyStore, SqliteSyncApplyStore>())` and register `ConflictResolver` (+ `ConflictPolicyCatalog`). Register `AnimoraReplicaDbContext` (if kept) on the shared connection factory. When NOT configured → leave `BufferingSyncApplyStore` (the `TryAddSingleton` already present). `using Microsoft.Extensions.DependencyInjection.Extensions;` is present.
   - In `src/Animora.Client.Services/Sync/SyncOrchestrationServiceCollectionExtensions.cs` `AddAnimoraSyncOrchestration`: **problem** — the `Replace<ISyncResyncHandler, ShadowResyncHandler>` wants `ShadowResyncHandler` which is in `Sync.Custom`, NOT referenceable from Client.Services. **Resolution:** do the `Replace` of `ISyncResyncHandler` in the **Sync.Custom** `AddAnimoraHttpSyncEngine` extension instead (Sync.Custom references Sync.Abstractions where the interface lives, and can see its own `ShadowResyncHandler`). `AddAnimoraHttpSyncEngine` runs before `AddAnimoraSyncOrchestration` registers the `TryAddSingleton<ISyncResyncHandler, NullSyncResyncHandler>` — but `TryAdd` won't overwrite an already-present registration, so **register the real handler in the Sync.Custom extension AFTER the Null default is in place → use `services.Replace(...)` (unconditional overwrite) guarded by `options.IsConfigured`, and ensure `AddAnimoraSyncOrchestration` uses `TryAddSingleton` for the Null default so ordering is safe either way.** Net: G03's extension keeps only the `TryAdd` Null default; G04's `Replace` lives entirely in Sync.Custom's `AddAnimoraHttpSyncEngine`. This keeps Client.Services free of any Sync.Custom reference. **Update the G04 "Files to modify" accordingly: edit ONLY `SyncCustomServiceCollectionExtensions.cs` for both Replaces; do NOT edit the Client.Services extension for G04.**
8. **Tests**:
   - `tests/Animora.Sync.Tests/` (refs Sync.Custom; add Persistence.Sqlite ProjectReference if a live encrypted-SQLite fixture is needed): `SqliteSyncApplyStoreTests.ApplyBatch_AdvancesBaseAndCheckpoint_Atomically`, `…CrashBeforeCommit_LeavesBaseAndCheckpointUnchanged`, `…PartialPage_NeverAdvancesCheckpoint`, `…Pull_DoesNotTouchPendingOverlay`, `ShadowResyncHandlerTests.BuildsShadow_PreservesOverlay_SwitchesAtomically`, `ShadowResyncHandlerTests.ShadowValidationFailure_LeavesLiveStateUntouched`, `SyncApplyDiTests.Configured_ReplacesBufferingAndNullResync`, `SyncApplyDiTests.Unconfigured_KeepsNoOpDefaults`.
   - `tests/Animora.Application.Tests/Sync/` (pure resolver): `ConflictResolverTests.UiPreference_LastWriteWins`, `…PostedFinancial_IsReversalOnly_NeverInPlace`, `…InventoryMovements_BothApply_Additive`, `…AppointmentSlotCollision_Rejected`, `…Demographics_FieldLevelMerge_NotWholeRowLww`, `…LedgerAppendOnly_CannotConflict`.
   - (The apply-store SQLite fixture: reuse the encrypted/in-memory SQLite harness already in `tests/Animora.Persistence.Tests` — e.g. its `CommandJournalTests`/`BackupRestoreTests` setup — as the fixture template.)

### G04 mechanical acceptance (run ONLY at the G04 gate task)
- `scripts/sandbox/build.sh full` = 0/0; `scripts/sandbox/run.sh` = `SMOKE RUN OK`; desktop starts unconfigured with G02/G03 no-op defaults untouched.
- `scripts/sandbox/test.sh Sync` + `scripts/sandbox/test.sh Persistence` + `scripts/sandbox/test.sh ConflictResolver` green with all named tests above.
- `grep -rn "System.Net.Http\|HttpClient\|Npgsql" src/Animora.Persistence.Sqlite/Replica` → nothing.
- `grep -rn "DELETE\|Remove\|Overwrite" src/Animora.Application/Sync/ConflictResolver.cs` → no path deletes/overwrites a `PostedFinancial` record (enforced by the reversal-only test).
- `ls src/Animora.Persistence.Sqlite/Migrations | grep -c ReplicaStore` ≥ 1; `grep -c "SYNC_APPLY_CONFLICT\|SYNC_SHADOW_VALIDATION_FAILED" src/Animora.Domain/Results/ErrorCodes.cs` = 2.
- `LayeringRulesTests.Desktop_HasNoStoreOrNetworkReferences` still green; C11 `SyncCursor` tests still green (untouched).

---

## TASK LIST (do in order; **push after EVERY task** — locked rule §top). `[ ]`=todo `[x]`=done.

> Status legend: **PENDING** / **IN PROGRESS** / **DONE**. Update the status token in-place as you go.

- [x] **T1 — DONE** — *G03 seam in Abstractions.* Create `src/Animora.Sync.Abstractions/ISyncResyncHandler.cs` (`ISyncResyncHandler` + `NullSyncResyncHandler`, per §G03-new-files-1). `build.sh fast` compiles. → push `feat(sync): G03 ISyncResyncHandler seam + Null default (TASK T1)`.
- [x] **T2 — DONE** — *G03 options + delay seam.* Create `SyncScheduleOptions.cs` (sealed class, mutable props) + `IDelayProvider.cs` (+`TimeProviderDelayProvider`). `build.sh fast`. → push `feat(sync): G03 schedule options + IDelayProvider (TASK T2)`.
- [x] **T3 — DONE** — *G03 orchestrator.* Create `SyncOrchestrator.cs` (`PullOnceAsync`/`PullAllAsync`; cursor advance ONLY on Applied/UpToDate; ResyncRequired→handler; no cursor on Deferred/Fail). `build.sh fast`. → push `feat(sync): G03 SyncOrchestrator (TASK T3)`.
- [x] **T4 — DONE** — *G03 scheduler.* Create `SyncSchedulerHostedService.cs` (dormant when unconfigured; interval+jitter via single seeded Random; capped exponential backoff on NetworkUnavailable/ServerBusy; clean cancel). `build.sh fast`. → push `feat(sync): G03 scheduler hosted service (TASK T4)`.
- [x] **T5 — DONE** — *G03 DI + status extension + Desktop wiring.* Create `SyncOrchestrationServiceCollectionExtensions.cs`; extend `LocalSyncStatusService.cs` (optional engine/orchestrator params + combined-state mapping per §G03-edit-7, C11 ctor stays compatible); add the one line `services.AddAnimoraSyncOrchestration();` under `AddAnimoraHttpSyncEngine();` in `App.axaml.cs`. `build.sh fast`. → push `feat(sync): G03 DI + status mapping + desktop wiring (TASK T5)`.
- [x] **T6 — DONE** — *G03 tests.* Add `tests/Animora.Application.Tests/Sync/` with the 14 named test classes/methods + fakes (§G03-tests). `build.sh fast`. → push `test(sync): G03 orchestrator/scheduler/status/DI tests (TASK T6)`.
- [x] **T7 — DONE** — *G03 GREEN GATE + PROGRESS.* `scripts/sandbox/build.sh full` (0/0), `scripts/sandbox/test.sh Sync` + the new-test FQN filters green, `scripts/sandbox/run.sh` = SMOKE RUN OK, run the five G03 grep checks (§G03-acceptance). Append `### G03` to PROGRESS Repo-state-log, flip G03 status row to `✅ Done` + date, set "Next phase → G04". Fix-and-log any deviation. → push `feat(sync): G03 sync orchestration & status — build/test green (TASK T7)`. **(If a heavy fix was needed mid-task, push it separately with a clear message before this gate.)**
- [ ] **T8 — PENDING** — *G04 replica entities + context + migration 032.* Create `src/Animora.Persistence.Sqlite/Replica/` (`ReplicaBase`/`ReplicaStaging`/`ReplicaCheckpoint` + configs, `[LocalOnly]`, `repl_*` prefix) on `AnimoraLocalDbContext` (prefer single context — §G04-1/2); generate migration via `.sandbox/tools/dotnet-ef … --context AnimoraLocalDbContext`, rename to `__032_ReplicaStore`; bump any `ExpectedHead` to `032`. `build.sh fast`. → push `feat(sync): G04 replica store entities + migration 032 (TASK T8)`.
- [ ] **T9 — PENDING** — *G04 atomic apply store.* Create `src/Animora.Persistence.Sqlite/Replica/SqliteSyncApplyStore.cs` (one connection+tx atomic base+checkpoint apply, streamed pages, A-RULE 022, overlay untouched). `build.sh fast`. → push `feat(sync): G04 SqliteSyncApplyStore atomic apply (TASK T9)`.
- [ ] **T10 — PENDING** — *G04 conflict engine.* Create `src/Animora.Application/Sync/{ConflictClass,ConflictPolicyCatalog,ConflictResolver}.cs` (+ `ConflictDecision`) per ARCH §8.5 (§G04-4). `build.sh fast`. → push `feat(sync): G04 conflict policy engine (reversal-only posted) (TASK T10)`.
- [ ] **T11 — PENDING** — *G04 shadow resync + ErrorCodes + DI.* Create `src/Animora.Sync.Custom/ShadowResyncHandler.cs` (full snapshot via `OpenBatchAsync(epoch, fromSequence:0)`, shadow build+validate+atomic switch); add the 2 ErrorCodes (region, reuse `AppointmentSlotConflict`); in `SyncCustomServiceCollectionExtensions.cs` conditionally `Replace` `ISyncApplyStore`→`SqliteSyncApplyStore` AND `ISyncResyncHandler`→`ShadowResyncHandler` + register `ConflictResolver`/catalog (§G04-7 — all Replaces live in Sync.Custom, Client.Services untouched). `build.sh fast`. → push `feat(sync): G04 shadow resync + errorcodes + conditional DI (TASK T11)`.
- [ ] **T12 — PENDING** — *G04 tests.* Add the apply/shadow/DI tests to `tests/Animora.Sync.Tests/` (add Persistence.Sqlite ref if needed) and the 6 `ConflictResolver` tests to `tests/Animora.Application.Tests/Sync/`. `build.sh fast`. → push `test(sync): G04 apply/conflict/resync/DI tests (TASK T12)`.
- [ ] **T13 — PENDING** — *G04 GREEN GATE + PROGRESS.* `build.sh full` (0/0), `test.sh Sync` + `test.sh Persistence` + `test.sh ConflictResolver` green, `run.sh` = SMOKE RUN OK, run all G04 grep/ls checks (§G04-acceptance, esp. ErrorCodes count = 2 and `ReplicaStore` migration present). Append `### G04` to PROGRESS, flip G04 row to `✅ Done` + date, set "Next phase → G05". Fix-and-log deviations. → push `feat(sync): G04 conflict resolution & non-destructive pull — build/test green (TASK T13)`. **(Heavy mid-task fixes → separate push with a clear message.)**
- [ ] **T14 — PENDING (FINAL)** — Delete **only** this plan file `Plan/G03-G04-PLAN.md` (do NOT touch the roadmap phase files or any other file). → push `chore(plan): remove completed G03+G04 plan (TASK T14)`.

### Standing rules for every task
- **Push after each task** (`scripts/git/push.sh "<msg> (TASK Tn)"`). Exit `3`→`setup_github_environment` then re-run; exit `2`→resolve rebase then re-run. **Never skip a push.**
- If, mid-task, you write a large/heavy block of code or apply a non-trivial fix (e.g. a migration regenerate, a cross-cutting refactor), **push it immediately with its own clear message** before continuing — don't batch heavy work into one giant commit.
- Targeted work only: read a file only when this plan's fact is contradicted by a compile error. No full-suite test runs except the two GREEN GATE tasks (T7, T13), and even there use the narrowest `test.sh <filter>` that covers the phase's named tests.
- `sealed`, nullable honored, `ct = default` + `ConfigureAwait(false)`, `Result` for expected failures, `IClock` for time, `IDelayProvider`/`TimeProvider` for delays, ULID ids, EF Core 10 `AsNoTracking` reads, additive migration. No new package. No forbidden tech.
