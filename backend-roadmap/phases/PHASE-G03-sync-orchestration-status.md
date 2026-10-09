# Phase G03 — Sync orchestration & status (scheduler, `SyncCursor` integration, status → `ISyncStatusService`)

**Band:** M · **Track:** G (sync / integration / release) · **Depends on:** G02 · **Next:** G04
**Satisfies:** R-B03 · **Runnable after this phase:** desktop offline yes · server required no
**Produced by:** M00 (regenerated 2026-10-09; supersedes the deleted `PHASE-E04` file).

> Baseline verified 2026-10-09: client migration head `031` (`031_TreatmentPlans`), `CommandTypes` = live `CommandTypes.cs` (88 declared today — never freeze), `ErrorCodes` 161 (+ codes added by earlier S/G phases, incl. G02 `NETWORK_UNAVAILABLE`/`SERVER_BUSY`/`SYNC_INTEGRITY_FAILED`/`SYNC_PROTOCOL_VIOLATION`), `WireContract.Version` = 2 after S06; `src/` projects include `Animora.Sync.Abstractions` (G01) and `Animora.Sync.Custom` (G02) (+ the server-track `S01–S20` projects).
> **Next-free-ordinal rule.** This phase adds **no migration** — it reuses the client `SyncCursor` table (introduced by C11) and adds no column. For any later phase that does: list the physical files of the owning `DbContext`'s `Migrations/` folder and take the next free ordinal (server sequence starts at **101**; client SQLite head `031` is independent). Match on the `NNN` token.

---

## ⚡ Zero-exploration contract (read this, then implement top-to-bottom)

> This file is self-contained and pre-verified. Every existing type, signature, path and command you need is in
> **§Reference pack**; every new name is fixed in **§Design decisions**. Do **not** re-open the repo to "understand" it and
> do not re-derive decisions. If — and only if — a compile error proves a snippet diverges from the tree, fix the smallest
> thing and log it in the `PROGRESS.md` deviation log. Names here are the real ones; there is no alias to resolve.
> Implement exactly this scope: nothing from a later phase, nothing "while we're here".

**Environment (do not probe):**
- Build / test / run only through the sandbox scripts (memory-capped ~985 MiB; never raw `dotnet build`):
  `scripts/sandbox/build.sh [fast|full]` (0 warnings / 0 errors), `scripts/sandbox/test.sh [filter]`,
  `scripts/sandbox/run.sh` (desktop smoke → `SMOKE RUN OK`). Run `scripts/sandbox/install-prereqs.sh` once (idempotent).
- `dotnet-ef` lives at `.sandbox/tools/dotnet-ef` — **never** `dotnet tool install` (not needed here: no migration).
- **Forbidden tech (restated, binding):** Redis, message broker, cloud IdP, cloud telemetry, CDN, EF Core 11,
  per-tenant database, hard-coded TLS version, Native AOT with EF Core. **This phase adds no package** — it wires only already-referenced
  types (`Animora.Sync.Abstractions`, `Animora.Sync.Custom`, `Microsoft.Extensions.Hosting`/`DependencyInjection` already pinned).

**Offline invariant.** With no server configured the G01 `NullSyncEngine` answers `Deferred`; the orchestrator maps that to `SyncState.Offline` (not `Error`) and the scheduler stays dormant — the desktop starts and runs exactly as today (Standing principle 1, CONVENTIONS §11). No socket is opened unless a base address is configured (G02).

**Condensed CONVENTIONS rules used here:** §2 layering (the orchestrator lives in `Animora.Client.Services`, referencing `Animora.Sync.Abstractions` + `Animora.Application` ports only — **never** `System.Net.Http`, never a server project); §4 C# style (`sealed`, nullable honored, `CancellationToken ct = default` + `ConfigureAwait(false)`, no `.Result`/`.Wait()`, expected failures as `Result`, time via injected `IClock`, scheduling delays via a timer/`Task.Delay` driven by `IClock` jitter, never `Thread.Sleep`); §6 two pipelines never merged (this schedules **state pull** only; the command drain stays the existing `LocalSyncStatusService` journal pass — both are surfaced through one status, not merged into one pass); §11 offline-first.

---

## Read before starting
- `backend-roadmap/PROGRESS.md` — top section only.
- `ARCHITECTURE.md` §8.1 (two pipelines), §8.2 (pull mechanics, checkpoint discipline), §8.3 (status surfacing) — only these `§`.
- `STACK.md` §4 (client composition / hosted services), §6 (client store) — only these `§`.
- `backend-roadmap/phases/PHASE-G01-sync-engine-abstractions.md` + `PHASE-G02-immutable-batch-http-adapter.md` — the `IStateSyncEngine`/`SyncResult` contract and the `HttpStateSyncEngine` this phase schedules.
- (Nothing else. `Animora-UI/**`, `Database-Sampel/**`, other phase files and `windows-frontend-roadmap/phases/**` are out of bounds.)

## Goal

Add the **client sync orchestration layer**: a scheduler that periodically (and on-demand) drives `IStateSyncEngine.PullAsync` for each active `SyncScope`, integrates the C11 `SyncCursor` / `ISyncCursorStore` "last synced" marker (advanced only on a successful `Applied`/`UpToDate`), and surfaces a single, coherent `SyncState` to the existing `ISyncStatusService` so the UI shows `Idle/Syncing/Synced/Pending/Error/Offline` driven by **both** the state-pull outcome **and** the command-journal backlog — without merging the two pipelines. When no server is configured the scheduler is dormant and status reads `Offline`.

---

## §Reference pack (verbatim from the tree, 2026-10-09)

```csharp
// Animora.Sync.Abstractions (G01) — the engine this phase schedules; reuse, never redefine
public enum SyncOutcome { UpToDate = 0, Applied = 1, Deferred = 2, ResyncRequired = 3 }
public sealed record SyncResult(SyncOutcome Outcome, long? Watermark, int BatchesApplied, int RecordsApplied, DateTimeOffset CompletedAtUtc, string? Detail);
public readonly record struct SyncScope { public string Name { get; } public static readonly SyncScope Default; public static Result<SyncScope> Create(string name); }
public interface IStateSyncEngine { bool IsConfigured { get; } Task<Result<SyncResult>> PullAsync(SyncScope scope, CancellationToken ct = default); }

// Animora.Sync.Custom (G02) — full-resync escalation lives in G04; G03 only recognises the ResyncRequired outcome
// (it does NOT call POST /sync/snapshots here — that is G04, invoked from the resync handler this phase delegates to).

// Animora.Contracts.Sync (frontend seam — unchanged signature; LocalSyncStatusService is extended, not replaced)
public enum SyncState { Idle, Syncing, Synced, Pending, Error, Offline }
public interface ISyncStatusService { SyncState State { get; } DateTimeOffset? LastSyncedAt { get; } event EventHandler? StateChanged; Task SyncNowAsync(CancellationToken ct = default); }

// Animora.Client.Services.Sync.LocalSyncStatusService (C11) — exact ctor + behavior to extend
public sealed class LocalSyncStatusService(IServiceScopeFactory scopes, ICommandTransport transport, IClock clock) : ISyncStatusService
{
    // private record Snapshot(SyncState State, DateTimeOffset? LastSyncedAt); Volatile snapshot; StateChanged fired on change.
    // SyncNowAsync => ExecuteAsync(runPass: true, ct): opens a scope, reads ISyncCursorStore, sets Syncing, runs ONE command-journal
    //   drain pass via ICommandJournal.GetSummaryAsync (+ transport), publishes Synced/Pending/Error; LastSyncedAt from cursor.
    // State/LastSyncedAt are Volatile reads; StateChanged is EventHandler?.
}

// Animora.Application.Ports (C11) — the last-synced marker the orchestrator advances
public interface ISyncCursorStore { Task<DateTimeOffset?> ReadAsync(CancellationToken ct = default); Task MarkSyncedAsync(DateTimeOffset whenUtc, CancellationToken ct = default); }
// Animora.Application.Commands.ICommandJournal.GetSummaryAsync -> Result<PendingCommandSummary> (unsettled count drives SyncState.Pending)

// Animora.Domain.Time (A02): public interface IClock { DateTimeOffset UtcNow { get; } }
// Animora.Domain.Results: Result/Result<T>/ErrorCode/ErrorCodes (reuse; G03 adds NO ErrorCode).
// Microsoft.Extensions.Hosting.IHostedService / BackgroundService — already available in the client host (STACK §4).
```

## §Design decisions

- **`SyncOrchestrator` (new, `Animora.Client.Services/Sync/SyncOrchestrator.cs`)** — the single component that owns a pull pass. `sealed class SyncOrchestrator(IStateSyncEngine engine, IServiceScopeFactory scopes, IClock clock, SyncScheduleOptions options)`. Public: `Task<Result<SyncOutcome>> PullOnceAsync(SyncScope scope, CancellationToken ct = default)` — calls `engine.PullAsync(scope, ct)`; on `Ok(Applied|UpToDate)` opens a DI scope and `await ISyncCursorStore.MarkSyncedAsync(result.CompletedAtUtc, ct)` (cursor advances **only** on these two outcomes); on `Ok(Deferred)` leaves the cursor untouched and returns `Deferred`; on `Ok(ResyncRequired)` returns `ResyncRequired` **without** advancing the cursor and invokes the injected `ISyncResyncHandler.HandleAsync(scope, reason, ct)` seam (default no-op `NullSyncResyncHandler` here; G04 supplies the real full-resync via `POST /sync/snapshots`); on `Fail` returns the failure. Never throws for an expected engine result. `PullAllAsync(ct)` iterates the configured scope list (today: `[SyncScope.Default]` — one scope; the list is an option so G-later can add scopes without touching callers).
- **`ISyncResyncHandler` seam** — `public interface ISyncResyncHandler { Task<Result> HandleAsync(SyncScope scope, string reason, CancellationToken ct = default); }` with `NullSyncResyncHandler` (returns `Result.Ok()`, logs a one-line warning) registered `TryAddSingleton` here; **G04 replaces it** (one-introducer: the interface is introduced here; the real impl is G04).
- **`SyncScheduleOptions` (`sealed record`)** — `bool Enabled = true`, `TimeSpan Interval = 5min`, `TimeSpan MinInterval = 30s` (floor), `double JitterFraction = 0.2` (±20 % jitter so a fleet doesn't stampede), `IReadOnlyList<string> Scopes = ["default"]`. Bound from config key `Animora:Sync`.
- **`SyncSchedulerHostedService : BackgroundService`** — the periodic driver. `ExecuteAsync`: if `!engine.IsConfigured || !options.Enabled` ⇒ return immediately (dormant; no loop, no socket). Otherwise loop: `await _status.SyncNowAsync(stoppingToken)` is **not** called from here (avoid re-entrancy); instead it calls the orchestrator's `PullAllAsync` wrapped so the status service observes the outcome (see next bullet), then waits `Interval ± jitter` via `Task.Delay` computed from `IClock` (a deterministic `IDelayProvider` seam is injected so tests advance time without real waiting — `TimeProvider`-based). On `OperationCanceledException` (shutdown) it exits cleanly. Backoff: a `Fail(NetworkUnavailable|ServerBusy)` doubles the next delay up to a `MaxBackoff = 15min` cap (respecting any `Retry-After` detail from G02's `ServerBusy`), reset on the next success.
- **Status integration (extend `LocalSyncStatusService`, do not replace it)** — add an **optional** constructor dependency `IStateSyncEngine? engine = null` and `SyncOrchestrator? orchestrator = null` (nullable so the C11 tests that construct it with the old ctor still compile; prefer a second ctor overload if cleaner). `SyncNowAsync` becomes: set `Syncing`; **(a)** run the existing command-journal drain pass (unchanged); **(b)** if the orchestrator is present and `engine.IsConfigured`, `await orchestrator.PullAllAsync(ct)`; **(c)** compute the final `SyncState` by combining: unsettled-command count `> 0` ⇒ `Pending`; else last pull `Applied/UpToDate` ⇒ `Synced`; last pull `Deferred` or `!IsConfigured` ⇒ `Offline`; any `Fail` or `ResyncRequired` not yet resolved ⇒ `Error`. `LastSyncedAt` reflects the `SyncCursor` (advanced by the orchestrator). The **state-pull and command-drain remain two separate passes** inside one `SyncNowAsync`; they are never merged into one network call (ARCH §8.1). `StateChanged` fires once per snapshot change (existing mechanism reused).
- **Determinism / no wall-clock sleeps** — all delays go through an injected `IDelayProvider` (`Task Delay(TimeSpan, CancellationToken)`, default `TimeProviderDelayProvider` over `TimeProvider.System`); jitter from a seeded `Random` created once (not per-iteration `new Random()`). This lets `SyncSchedulerTests` run many iterations instantly.
- **DI** — `public static class SyncOrchestrationServiceCollectionExtensions { public static IServiceCollection AddAnimoraSyncOrchestration(this IServiceCollection services, Action<SyncScheduleOptions>? configure = null) }`: binds options, `TryAddSingleton<ISyncResyncHandler, NullSyncResyncHandler>`, `AddSingleton<SyncOrchestrator>`, `AddHostedService<SyncSchedulerHostedService>`, and registers `IDelayProvider`. Called from `Animora.Desktop.App.ConfigureServices` **after** `AddAnimoraHttpSyncEngine()` (G02). The hosted service is a no-op when unconfigured, so adding it is always safe.
- **Not in this phase:** the real resync (full-snapshot download + shadow generation) → **G04** (`ISyncResyncHandler` impl); atomic apply / conflict policy → **G04**; command transport replacement → **G05**; auth on the HTTP client → **G06**. No new wire DTO, no migration, no ErrorCode.

## §Implementation plan

1. Create under `src/Animora.Client.Services/Sync/`: `SyncScheduleOptions.cs`, `IDelayProvider.cs` (+ `TimeProviderDelayProvider`), `ISyncResyncHandler.cs` (+ `NullSyncResyncHandler`), `SyncOrchestrator.cs`, `SyncSchedulerHostedService.cs`, `SyncOrchestrationServiceCollectionExtensions.cs` — per §Design decisions (one-line XML doc each public member; `ConfigureAwait(false)`).
2. Edit `src/Animora.Client.Services/Sync/LocalSyncStatusService.cs`: add the optional engine/orchestrator dependency and the combined-state logic in `SyncNowAsync` exactly as above; keep the existing command-drain pass and `StateChanged` mechanics untouched in signature.
3. `src/Animora.Desktop/App.axaml.cs` `ConfigureServices`: add `services.AddAnimoraSyncOrchestration();` directly below `services.AddAnimoraHttpSyncEngine();` (+ `using Animora.Client.Services.Sync;` if not already present). Change no other line.
4. Tests in `tests/Animora.Sync.Tests/` (or `Animora.Application.Tests` if the status seam lives there — keep with the other Sync tests): `SyncOrchestratorTests`, `SyncSchedulerTests` (fake `IDelayProvider`/`IStateSyncEngine`), `SyncStatusIntegrationTests`, `SyncOrchestrationDiTests`. Plus a layering assertion that `Animora.Client.Services` sync code references no `System.Net.Http` (the HTTP is in `Sync.Custom`).
5. `scripts/sandbox/build.sh full` → 0/0; `scripts/sandbox/test.sh Sync` → green; `scripts/sandbox/run.sh` → `SMOKE RUN OK` (scheduler dormant when unconfigured).
6. `PROGRESS.md` per recipe; commit via `scripts/git/push.sh`.

## Files to create / modify

| Path | Contents |
|---|---|
| `src/Animora.Client.Services/Sync/SyncScheduleOptions.cs` (new) | enabled/interval/min/jitter/scopes |
| `src/Animora.Client.Services/Sync/IDelayProvider.cs` (new) | delay seam + `TimeProvider` default |
| `src/Animora.Client.Services/Sync/ISyncResyncHandler.cs` (new) | resync seam + `NullSyncResyncHandler` (G04 replaces) |
| `src/Animora.Client.Services/Sync/SyncOrchestrator.cs` (new) | `PullOnceAsync`/`PullAllAsync`; cursor advance on Applied/UpToDate only |
| `src/Animora.Client.Services/Sync/SyncSchedulerHostedService.cs` (new) | periodic driver; dormant when unconfigured; jitter+backoff |
| `src/Animora.Client.Services/Sync/SyncOrchestrationServiceCollectionExtensions.cs` (new) | `AddAnimoraSyncOrchestration` |
| `src/Animora.Client.Services/Sync/LocalSyncStatusService.cs` (edit) | combine state-pull + command-backlog into one SyncState; cursor-driven LastSyncedAt |
| `src/Animora.Desktop/App.axaml.cs` (edit) | one registration line |
| `tests/Animora.Sync.Tests/*.cs` (new) | the four test classes + layering assertion |

## Contracts / schema added

No schema, no migration, no wire DTO, no ErrorCode. New public API: `SyncOrchestrator`, `SyncScheduleOptions`, `IDelayProvider` (+ default), `ISyncResyncHandler` (+ `NullSyncResyncHandler`), `SyncSchedulerHostedService`, `AddAnimoraSyncOrchestration`. `ISyncStatusService`/`SyncState` unchanged (behaviour of `LocalSyncStatusService` extended).

## Rules that apply here

- **Layering (CONVENTIONS §2, `ARCHITECTURE.md` §6):** the orchestrator is in `Animora.Client.Services` and references `Animora.Sync.Abstractions` + `Animora.Application` ports + the already-present `Animora.Sync.Custom` (transitively, for the configured engine) — **never** `System.Net.Http` directly, never Npgsql, never a server project. `Animora.Desktop` keeps passing `Desktop_HasNoStoreOrNetworkReferences`.
- **C# style (§4):** `sealed`; nullable honored; async public methods take `CancellationToken ct = default` + `ConfigureAwait(false)`; no `.Result`/`.Wait()`; expected failures as `Result`; time via `IClock`; delays via `IDelayProvider`/`TimeProvider` (no `Thread.Sleep`, no per-call `new Random()`).
- **Two pipelines (§6, `ARCHITECTURE.md` §8.1):** state-pull and command-drain are two passes surfaced through one status; never one merged network round-trip.
- **Offline-first (§11):** unconfigured ⇒ scheduler dormant, status `Offline`, no socket; a transient fault degrades to `Error`/backoff, never a crash, never a lost cursor.

## Out of scope (owning phase)

- Real full-resync (shadow generation, `POST /sync/snapshots` orchestration) + atomic apply + conflict policy → **G04** (supplies the real `ISyncResyncHandler`/`ISyncApplyStore`).
- Command transport + settlement reconciliation → **G05**. Online auth / entitlement / device + bearer on the HTTP client → **G06**.
- The engine + HTTP batch download themselves → **G01/G02** (reused). Server feed → **S11/S12**.
- Optional Datasync adapter → deferred (ROADMAP §5, STACK G1 gate).

## Acceptance criteria (mechanical)

1. `scripts/sandbox/build.sh full` = 0 warnings / 0 errors; `scripts/sandbox/run.sh` = `SMOKE RUN OK` (scheduler dormant unconfigured).
2. `scripts/sandbox/test.sh Sync` green with: `SyncOrchestratorTests.PullApplied_AdvancesCursor`, `SyncOrchestratorTests.PullUpToDate_AdvancesCursor`, `SyncOrchestratorTests.PullDeferred_DoesNotAdvanceCursor`, `SyncOrchestratorTests.ResyncRequired_InvokesHandler_NoCursorAdvance`, `SyncOrchestratorTests.PullFail_Surfaces_NoCursorAdvance`, `SyncSchedulerTests.Dormant_WhenUnconfigured`, `SyncSchedulerTests.RunsOnInterval_WithJitter`, `SyncSchedulerTests.Backoff_DoublesOnFault_CapsAtMax_ResetsOnSuccess`, `SyncSchedulerTests.CancellationExitsCleanly`, `SyncStatusIntegrationTests.PendingCommands_MapToPending`, `SyncStatusIntegrationTests.Applied_NoBacklog_MapsSynced`, `SyncStatusIntegrationTests.Unconfigured_MapsOffline`, `SyncStatusIntegrationTests.Fail_MapsError`, `SyncOrchestrationDiTests.RegistersSchedulerAndNullResyncHandler`.
3. `grep -rn "System.Net.Http\|HttpClient\|Npgsql" src/Animora.Client.Services/Sync` returns nothing (HTTP stays in `Sync.Custom`); `grep -rn "Thread.Sleep\|new Random()" src/Animora.Client.Services/Sync` returns nothing.
4. `grep -rn "MarkSyncedAsync" src/Animora.Client.Services/Sync/SyncOrchestrator.cs` appears only inside the `Applied`/`UpToDate` branch (one-call cursor advance); `grep -rn "snapshots" src/Animora.Client.Services/Sync` returns nothing (full-resync is G04).
5. `tests/Animora.Domain.Tests` `LayeringRulesTests.Desktop_HasNoStoreOrNetworkReferences` still passes; the C11 `LocalSyncStatusService` existing tests still compile and pass (ctor compatibility).

## PROGRESS.md update

At the end of this phase: set the `G03` row in "Phase status" to `✅ Done` (add date + one-line summary), advance **"Next phase" → `G04`**, append one `### G03` entry to "Repo state log" (the orchestrator + scheduler + the extended status mapping + the `ISyncResyncHandler` seam + test classes), log any deviation in the "Decision & deviation log", then commit with `scripts/git/push.sh -b genspark_ai_developer "feat(sync): G03 sync orchestration & status"`.
