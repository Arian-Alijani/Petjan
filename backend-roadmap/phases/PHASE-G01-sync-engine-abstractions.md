# Phase G01 — Sync engine abstractions (`IStateSyncEngine`, `SyncScope`, `SyncResult`) + `NullSyncEngine` offline fallback

**Band:** S · **Track:** G (sync / integration / release) · **Depends on:** S12 · **Next:** G02
**Satisfies:** R-B03, R-B13 · **Runnable after this phase:** desktop offline yes · server required no
**Produced by:** M00 (regenerated 2026-10-09; supersedes the first half of the deleted `PHASE-E03` file).

> Baseline verified 2026-10-09: client migration head `031` (`031_TreatmentPlans`), `CommandTypes` = live `CommandTypes.cs` (88 declared today — never freeze), `ErrorCodes` 161 (+ codes added by earlier S phases), `WireContract.Version` = 1 (S06 bumps to 2, supported {1,2}); `src/` projects = `Animora.Domain`, `Animora.Application`, `Animora.Persistence.Sqlite`, `Animora.Infrastructure.Local`, `Animora.Client.Services`, `Animora.Contracts`, `Animora.Contracts.Wire`, `Animora.Desktop`, `Animora.Sync.Abstractions` (empty scaffold today; filled here) (+ server-track projects added by S01–S20).
> **Next-free-ordinal rule.** This phase adds **no migration** (no schema). For any later phase that does: list the physical files of the owning `DbContext`'s `Migrations/` folder and take the next free ordinal (server sequence starts at **101**; the client SQLite sequence — head `031` today — is independent). Match on the `NNN` token, not a file-name prefix.

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
  `Microsoft.Extensions.DependencyInjection` 10.0.12 (Directory.Packages.props) — never Npgsql, never `CommunityToolkit.Datasync`.**

**Offline invariant.** `NullSyncEngine` is the default registration: with no server, no network and no adapter the desktop
starts and runs exactly as today (Standing principle 1, CONVENTIONS §11). Nothing in this phase opens a socket.

**Condensed CONVENTIONS rules used here (so `CONVENTIONS.md` need not be opened):** §2 layering (`Animora.Sync.Abstractions` → Domain +
Contracts.Wire only); §4 C# style (`sealed` by default, `sealed record` DTOs, nullable honored, every async public method takes
`CancellationToken ct = default`, expected failures are `Result`/`Result<T>` with an `ErrorCode`, never exceptions, time via
injected `IClock`, no `DateTime.UtcNow`); §6 two pipelines never merged (this engine is **state replication only**; business
commands go through `ICommandTransport`, G05); §11 offline-first (a `Null*` fallback is mandatory).

---

## Read before starting
- `backend-roadmap/PROGRESS.md` — top section only.
- `ARCHITECTURE.md` §8.1, §8.2 (pipelines + pull mechanics) and §16-CT.1–CT.2 — only these `§`.
- (Nothing else. `Animora-UI/**`, `Database-Sampel/**`, other phase files and `windows-frontend-roadmap/phases/**` are out of bounds.)

## Goal

Fill the empty `Animora.Sync.Abstractions` scaffold with the **client-side state-replication seam**: the single engine port
`IStateSyncEngine`, its request/response vocabulary (`SyncScope`, `SyncOutcome`, `SyncResult`), the `NullSyncEngine` offline
fallback and one DI entry point that registers the fallback. The project stays free of any transport, any database and any server
reference so G02 (HTTP adapter), G03 (orchestration) and G04 (conflict/apply) plug in behind it without touching callers.

---

## §Reference pack (verbatim from the tree, 2026-10-09)

```csharp
// Animora.Domain.Results (A02) — reuse, never redefine
public readonly record struct ErrorCode(string Code, string Category) { public const string Validation="Validation", NotFound="NotFound", Conflict="Conflict", Forbidden="Forbidden", Integrity="Integrity", Infrastructure="Infrastructure", Entitlement="Entitlement"; public static readonly ErrorCode None = new(string.Empty, string.Empty); public bool IsNone { get; } }
public readonly record struct Result      { IsSuccess, IsFailure, Error, Detail; static Ok(); static Fail(ErrorCode, string? detail = null); Bind(…) }
public readonly record struct Result<T>   { IsSuccess, IsFailure, T? Value, Error, Detail; static Ok(T); static Fail(ErrorCode, string? detail = null); implicit operator Result<T>(T); ToResult(); Map<TOut>(…); Bind<TOut>(…); OrElse(T) }
// ErrorCodes reused here: ErrorCodes.ValidationFailed = new("VALIDATION_FAILED", ErrorCode.Validation)

// Animora.Domain.Time (A02) — time is always injected
public interface IClock { DateTimeOffset UtcNow { get; } }

// Animora.Application.Ports (C11) — the local last-synced marker; G03 integrates it, G01 does NOT touch it
public interface ISyncCursorStore { Task<DateTimeOffset?> ReadAsync(CancellationToken ct = default); Task MarkSyncedAsync(DateTimeOffset whenUtc, CancellationToken ct = default); }
// Animora.Persistence.Sqlite.Entities.SyncCursor (C11): [LocalOnly] (Id, TenantId, Scope = "default", LastSyncedAtUtc?) ; const string DefaultScope = "default"

// Animora.Contracts.Sync (frontend) — the UI seam a later phase (G03) drives; unchanged by G01 except one doc comment
public enum SyncState { Idle, Syncing, Synced, Pending, Error, Offline }
public interface ISyncStatusService { SyncState State { get; } DateTimeOffset? LastSyncedAt { get; } event EventHandler? StateChanged; Task SyncNowAsync(CancellationToken ct = default); }

// The offline-fallback pattern to mirror (Animora.Application.Commands.NullCommandTransport, B02): a sealed class that returns Result.Ok(<Deferred outcome>)
// with Detail "No command transport configured (offline)." — never an error — registered with services.TryAddSingleton<ICommandTransport, NullCommandTransport>().
```

```xml
<!-- src/Animora.Sync.Abstractions/Animora.Sync.Abstractions.csproj (A01) — today: ONLY these two refs, no PackageReference, no .cs files -->
<ProjectReference Include="..\Animora.Domain\Animora.Domain.csproj" />
<ProjectReference Include="..\Animora.Contracts.Wire\Animora.Contracts.Wire.csproj" />
<!-- tests/Animora.Sync.Tests/Animora.Sync.Tests.csproj (A01): xunit + Test.Sdk, ONE ProjectReference to Sync.Abstractions, PlaceholderTests.cs only -->
```

## §Design decisions

- **One engine port, one name.** The port is **`IStateSyncEngine`** (the name `STACK.md` §4/§6 and `ARCHITECTURE.md` §8.2 fix). The older
  doc-comment alias `ISyncEngine` (in `ISyncStatusService`'s XML doc) is **not** a second type — it is corrected to `IStateSyncEngine`
  in that comment; no `ISyncEngine` type exists or is added.
- **Namespace `Animora.Sync.Abstractions`**, files under `src/Animora.Sync.Abstractions/`. References stay **Domain + Contracts.Wire only**; add
  `<PackageReference Include="Microsoft.Extensions.DependencyInjection" />` (version from `Directory.Packages.props`, 10.0.12) solely for the DI entry point.
- **`SyncScope`** — `public readonly record struct SyncScope`: `string Name { get; }`; `public static readonly SyncScope Default = new("default")`
  (**equals** `SyncCursor.DefaultScope`, asserted by a test string-literal match because Sync.Abstractions must not reference Persistence);
  `public static Result<SyncScope> Create(string name)` — trims; rejects null/whitespace, length > 64, or any char outside `[a-z0-9._-]` with
  `Result.Fail(ErrorCodes.ValidationFailed, "Sync scope must be 1–64 chars of a-z 0-9 . _ -.")`; the private/default-constructed struct is normalised to `Default` via `Name` getter returning `"default"` when the backing field is null (so `default(SyncScope)` is valid, never a null scope). `ToString() => Name`.
- **`SyncOutcome`** — `public enum SyncOutcome { UpToDate, Applied, Deferred, ResyncRequired }` (values fixed, explicit ints 0..3).
  `Deferred` = the engine is not configured / offline (the `NullSyncEngine` answer, **success**, mirrors `NullCommandTransport`);
  `ResyncRequired` = the server told the client its checkpoint is unusable (epoch change / beyond retention / contract unsupported, S12) — the
  orchestrator (G03) escalates to the full-resync path (G04). Genuine failures (network, integrity, cancellation) are `Result.Fail`, **not** an outcome.
- **`SyncResult`** — `public sealed record SyncResult(SyncOutcome Outcome, long? Watermark, int BatchesApplied, int RecordsApplied, DateTimeOffset CompletedAtUtc, string? Detail)`;
  `Watermark` is the server `ChangeSequence` high-water the local base now reflects (null when nothing was applied / deferred); counts are `>= 0`
  (the constructor body does not throw — callers construct via the named factories): `static SyncResult Deferred(DateTimeOffset nowUtc, string? detail = null)`,
  `static SyncResult UpToDate(long watermark, DateTimeOffset nowUtc)`, `static SyncResult Applied(long watermark, int batches, int records, DateTimeOffset nowUtc)`,
  `static SyncResult ResyncRequired(string reason, DateTimeOffset nowUtc)`. `ChangeSequence` is **never** read as a business sequence (§8.1).
- **`IStateSyncEngine`**:
  ```csharp
  public interface IStateSyncEngine
  {
      /// <summary>False for NullSyncEngine (no endpoint configured); true for a real adapter. Lets status UI say "offline" instead of "error".</summary>
      bool IsConfigured { get; }
      /// <summary>Pull-only in G01–G04: download immutable batches for <paramref name="scope"/> and apply them atomically (base + checkpoint). Never touches the pending overlay (ARCH §8.2).</summary>
      Task<Result<SyncResult>> PullAsync(SyncScope scope, CancellationToken ct = default);
  }
  ```
  No `PushAsync`: business intent leaves **only** through `ICommandTransport` (ARCH §8.1, two permanent pipelines; the 3 client-writable replicable shapes ride commands in G05).
  No server type, no `HttpClient`, no EF type appears in the interface — it is implementable by the HTTP adapter (G02), by a fake in tests, or by the optional Datasync adapter (deferred, ROADMAP §5).
- **`NullSyncEngine`** — `public sealed class NullSyncEngine(IClock clock) : IStateSyncEngine`: `IsConfigured => false`; `PullAsync` returns
  `Task.FromResult(Result<SyncResult>.Ok(SyncResult.Deferred(clock.UtcNow, "No sync engine configured (offline).")))`; honors a pre-cancelled token
  (`ct.ThrowIfCancellationRequested()`); `scope` accepted for any value.
- **DI** — `public static class SyncAbstractionsServiceCollectionExtensions { public static IServiceCollection AddAnimoraSyncEngine(this IServiceCollection services) }`:
  `ArgumentNullException.ThrowIfNull`; `services.TryAddSingleton<IStateSyncEngine, NullSyncEngine>()` (**TryAdd** — a real adapter registered earlier or later with `Replace`/`AddSingleton` wins; G02 owns the real registration). Returns `services`.
  Called from `Animora.Desktop.App.ConfigureServices` once (next to the existing `LocalSyncStatusService` registration) — `Animora.Desktop` gains **no** new project reference (it reaches `Sync.Abstractions` transitively through `Animora.Client.Services`, which gains the `ProjectReference` here).
- **Not in this phase:** consuming the engine from `LocalSyncStatusService` (G03), the checkpoint store, staging/apply (G02/G04), any wire DTO (S12 owns `Animora.Contracts.Wire.Sync.*`), any HTTP (G02).

## §Implementation plan

1. `src/Animora.Sync.Abstractions/Animora.Sync.Abstractions.csproj`: add `<ItemGroup><PackageReference Include="Microsoft.Extensions.DependencyInjection" /></ItemGroup>`. Delete the placeholder wording in `README.md` and replace it with a 6-line description (purpose, the 3 types, "no transport/database refs — see G02").
2. Create `SyncScope.cs`, `SyncOutcome.cs`, `SyncResult.cs`, `IStateSyncEngine.cs`, `NullSyncEngine.cs`, `SyncAbstractionsServiceCollectionExtensions.cs` exactly per §Design decisions (XML-doc every public member in one line).
3. `src/Animora.Client.Services/Animora.Client.Services.csproj`: add `<ProjectReference Include="..\Animora.Sync.Abstractions\Animora.Sync.Abstractions.csproj" />`.
4. `src/Animora.Desktop/App.axaml.cs` `ConfigureServices`: add `services.AddAnimoraSyncEngine();` directly above `services.AddSingleton<LocalSyncStatusService>();` (+ `using Animora.Sync.Abstractions;`). Do **not** change any other line.
5. `src/Animora.Contracts/Sync/ISyncStatusService.cs`: in the `<b>Backend-ready seam.</b>` XML doc replace `Animora.Sync.Abstractions.ISyncEngine` with `Animora.Sync.Abstractions.IStateSyncEngine` (comment only; the Contracts project gains **no** reference).
6. Tests in `tests/Animora.Sync.Tests/` (delete `PlaceholderTests.cs`): `SyncScopeTests`, `SyncResultTests`, `NullSyncEngineTests`, `SyncDiTests`, `SyncLayeringTests` (names below).
7. `scripts/sandbox/build.sh full` → 0 warnings / 0 errors; `scripts/sandbox/test.sh Sync` → green; `scripts/sandbox/run.sh` → `SMOKE RUN OK` (desktop still starts with the null engine).
8. `PROGRESS.md` per recipe below; commit via `scripts/git/push.sh`.

## Files to create / modify

| Path | Contents |
|---|---|
| `src/Animora.Sync.Abstractions/SyncScope.cs` (new) | `readonly record struct SyncScope`, `Default`, `Create` (validation as above) |
| `src/Animora.Sync.Abstractions/SyncOutcome.cs` (new) | enum `UpToDate=0, Applied=1, Deferred=2, ResyncRequired=3` |
| `src/Animora.Sync.Abstractions/SyncResult.cs` (new) | sealed record + four factories |
| `src/Animora.Sync.Abstractions/IStateSyncEngine.cs` (new) | `IsConfigured`, `PullAsync` |
| `src/Animora.Sync.Abstractions/NullSyncEngine.cs` (new) | offline fallback (clock-injected) |
| `src/Animora.Sync.Abstractions/SyncAbstractionsServiceCollectionExtensions.cs` (new) | `AddAnimoraSyncEngine` (TryAddSingleton) |
| `src/Animora.Sync.Abstractions/Animora.Sync.Abstractions.csproj`, `README.md` (edit) | DI package ref; real README |
| `src/Animora.Client.Services/Animora.Client.Services.csproj` (edit) | `ProjectReference` to Sync.Abstractions |
| `src/Animora.Desktop/App.axaml.cs` (edit) | one registration line + using |
| `src/Animora.Contracts/Sync/ISyncStatusService.cs` (edit) | doc-comment name fix only |
| `tests/Animora.Sync.Tests/*.cs` (new; placeholder removed) | tests below |

## Contracts / schema added

Public API only: `IStateSyncEngine`, `SyncScope`, `SyncOutcome`, `SyncResult`, `NullSyncEngine`, `AddAnimoraSyncEngine`. No schema, no migration, no wire DTO, no ErrorCode (reuses `ErrorCodes.ValidationFailed`).

## Rules that apply here

- **Layering (CONVENTIONS §2, `ARCHITECTURE.md` §6):** `Animora.Sync.Abstractions` references **only** `Animora.Domain` + `Animora.Contracts.Wire` (+ the pinned DI package); no Npgsql, no EF, no `System.Net.Http`, no server project, no `Animora.Application`/`Persistence.Sqlite`. `Animora.Desktop` must keep passing `Desktop_HasNoStoreOrNetworkReferences` (no `System.Net.Http`, no EF).
- **C# style (§4):** `sealed` by default; nullable honored; async public methods take `CancellationToken ct = default`; `ConfigureAwait(false)` in library code; expected failures as `Result`; time through `IClock`.
- **Two pipelines (§6, `ARCHITECTURE.md` §8.1):** the engine is state replication only — no money/stock effect ever travels through `IStateSyncEngine`.

## Out of scope (owning phase)

- Real HTTP adapter `Animora.Sync.Custom`, page download, staging → **G02**. Scheduler, `SyncCursor` integration, `ISyncStatusService` wiring → **G03**.
- Atomic apply, overlay preservation, conflict policy, full-resync shadow switch → **G04**. Command transport → **G05**. Auth/entitlement → **G06**.
- Optional Datasync adapter → deferred (ROADMAP §5, STACK G1 gate).

## Acceptance criteria (mechanical)

1. `scripts/sandbox/build.sh full` = 0 warnings / 0 errors; `scripts/sandbox/run.sh` = `SMOKE RUN OK`.
2. `scripts/sandbox/test.sh Sync` green with these named tests: `SyncScopeTests.Default_EqualsLiteralDefault_MatchingSyncCursorDefaultScope`, `SyncScopeTests.DefaultStruct_IsDefaultScope_NeverNull`, `SyncScopeTests.Create_RejectsEmptyTooLongAndIllegalChars`, `SyncResultTests.Factories_SetOutcomeAndWatermark`, `SyncResultTests.Deferred_HasNoWatermark`, `NullSyncEngineTests.Pull_ReturnsDeferredSuccess_NotConfigured`, `NullSyncEngineTests.Pull_PreCancelledToken_Throws`, `NullSyncEngineTests.Pull_UsesInjectedClock`, `SyncDiTests.AddAnimoraSyncEngine_RegistersNullEngine_AndRealRegistrationWins`, `SyncLayeringTests.SyncAbstractions_ReferencesOnlyDomainWireAndFramework`.
3. `grep -rn "Npgsql\|EntityFramework\|System.Net.Http\|Datasync" src/Animora.Sync.Abstractions` returns nothing; `grep -c "ProjectReference" src/Animora.Sync.Abstractions/Animora.Sync.Abstractions.csproj` = 2.
4. `grep -rn "ISyncEngine" src tests` returns nothing; `grep -rn "IStateSyncEngine" src --include=*.cs -l` lists exactly `IStateSyncEngine.cs`, `NullSyncEngine.cs`, `SyncAbstractionsServiceCollectionExtensions.cs`, `ISyncStatusService.cs` (comment).
5. `tests/Animora.Domain.Tests` `LayeringRulesTests.Desktop_HasNoStoreOrNetworkReferences` and `Contracts_HaveExactlyOneRealRegistration` still pass (via `scripts/sandbox/test.sh Layering`).

## PROGRESS.md update

At the end of this phase: set the `G01` row in "Phase status" to `✅ Done` (add date + one-line summary), advance **"Next phase" → `G02`**,
append one `### G01` entry to "Repo state log" (what exists now: the 6 types, DI call site, test classes), log any deviation in the "Decision & deviation log", then commit with `scripts/git/push.sh -b genspark_ai_developer "feat(sync): G01 sync engine abstractions + NullSyncEngine"`.
