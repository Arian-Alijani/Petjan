# Phase G05 — Command transport & settlement reconciliation (`HttpCommandTransport` replaces `NullCommandTransport`)

**Band:** M · **Track:** G (sync / integration / release) · **Depends on:** G04, S10 · **Next:** G06
**Satisfies:** R-B03, R-B09, R-B16 · **Runnable after this phase:** desktop offline yes · server required no
**Produced by:** M00 (regenerated 2026-10-09; regenerates scope-inventory row E06 (Track-D/E planning files deleted in the 2026-10-08 server-track reset)).

> Baseline verified 2026-10-09: client migration head `031` + the G04 replica migration (verify by listing — the client ordinal moved when G04 landed `ReplicaStore`), `CommandTypes` = live `CommandTypes.cs` (88 declared today — never freeze), `ErrorCodes` 161 (+ codes added by earlier S/G phases, incl. S06's 5 intake codes and G02/G04's sync codes), `WireContract.Version` = 2 after S06 (supported {1,2}); `src/` projects include `Animora.Sync.Abstractions` (G01), `Animora.Sync.Custom` (G02) (+ server-track `S01–S20`).
> **Next-free-ordinal rule.** This phase adds **no migration** (it reuses the B02 client command journal / `PendingCommandRecord` and the G04 id-remap surface; no schema change). If a column is genuinely needed, list `src/Animora.Persistence.Sqlite/Migrations/` and take the next free client ordinal; server sequence (101+) is untouched by this client phase.

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
  per-tenant database, hard-coded TLS version, Native AOT with EF Core. **No new package** — the `HttpClient` is the same `Microsoft.Extensions.Http` G02 added; `HttpCommandTransport` lives in `Animora.Sync.Custom` (the one client project allowed `System.Net.Http`).

**Offline invariant.** `HttpCommandTransport` replaces `NullCommandTransport` **only when a base address is configured** (same guard G02 uses); unconfigured, `NullCommandTransport` stays and the desktop posts commands exactly as today — locally durable in the journal, settling `Deferred` (Standing principle 1, CONVENTIONS §11). A transient network/server fault during submit returns `Deferred`/`Fail` so the command stays in the journal and is retried by the existing processor — never lost, never double-applied (idempotency key `(TenantId, OperationId)`).

**Condensed CONVENTIONS rules used here:** §2 layering (`HttpCommandTransport` in `Animora.Sync.Custom`, implementing `Animora.Application.Commands.ICommandTransport`; **no** server-project ref, **no** Npgsql); §4 C# style (`sealed`, nullable honored, `CancellationToken ct = default` + `ConfigureAwait(false)`, no `.Result`/`.Wait()`, expected failures as `Result` not exceptions, time via `IClock`, ULID ids); §6 **two pipelines never merged** (this is the **command** pipeline — business intent; it is entirely separate from the G01–G04 state-pull engine); §8.7 sync-failure UX contract (every non-settled command answers what/why/financial-effect/who).

---

## Read before starting
- `backend-roadmap/PROGRESS.md` — top section only.
- `ARCHITECTURE.md` §8.1 (two pipelines), §8.3 (command settlement, idempotency key, envelope), §8.7 (the four-question sync-failure UX contract, `FinancialEffect`, responsible role), §18-CT (wire version / mixed fleet) — only these `§`.
- `backend-roadmap/phases/PHASE-S06-command-intake-idempotency.md` §Design decisions (the `POST /api/commands`, `POST /api/commands/batch`, `GET /api/commands/{operationId}/ids` surface this transport calls; the wire-v2 `ExpectedServerVersion`/`RuleSetVersion` fields; the HTTP-200-for-settled-outcomes rule) and `PHASE-G04-...md` (the replica base this transport reads the aggregate version from for `ExpectedServerVersion`).
- `src/Animora.Application/Commands/*` (the `ICommandTransport`, `ICommandJournal`, `NullCommandTransport`, `PendingCommandRecord`, `CommandFingerprint` surfaces — reused, not changed in signature).
- (Nothing else. `Animora-UI/**`, `Database-Sampel/**`, other phase files and `windows-frontend-roadmap/phases/**` are out of bounds.)

## Goal

Implement the real client **command transport** — `HttpCommandTransport : ICommandTransport` — that submits `CommandEnvelope`s to the S06 business API (`POST /api/commands`, with a batch path over `POST /api/commands/batch`), maps every settled HTTP-200 `SettlementResult` back onto the local journal (so the four-question sync-failure UX contract holds: what/why/financial-effect/who), and reconciles server identity: on `Accepted` it records the server→local **id remap** (via `GET /api/commands/{operationId}/ids`) so dependent pending commands carrying provisional local ids are rewritten before they are sent, and it populates the wire-v2 `ExpectedServerVersion` optimistic precondition from the replica base (G04). It **replaces `NullCommandTransport`** when configured; `NullCommandTransport` stays as the documented offline fallback. The command pipeline stays entirely separate from the state-pull engine (two permanent pipelines, §8.1).

---

## §Reference pack (verbatim from the tree + S06, 2026-10-09)

```csharp
// Animora.Application.Commands (B02) — the port this phase implements + the journal it reconciles; reuse, never redefine
public interface ICommandTransport { Task<Result<SettlementResult>> SubmitAsync(CommandEnvelope envelope, CancellationToken ct = default); }
public sealed class NullCommandTransport : ICommandTransport { /* returns Ok(Deferred + FinancialEffect.Unknown + SettledAtUtc=UnixEpoch), Detail "No command transport configured (offline)." — REPLACED here when configured */ }
public interface ICommandJournal {
    Task<Result<PendingCommandRecord>> EnqueueAsync(CommandEnvelope e, CancellationToken ct = default);
    Task<Result<PendingCommandRecord?>> FindByOperationIdAsync(string operationId, CancellationToken ct = default);
    Task<Result> ApplySettlementAsync(SettlementResult result, CancellationToken ct = default);
    Task<Result<IReadOnlyList<PendingCommandRecord>>> ClaimBatchAsync(int max, TimeSpan lease, CancellationToken ct = default);
    Task<Result> ReleaseAsync(string operationId, ErrorCode error, TimeSpan backoff, CancellationToken ct = default);
    Task<Result<IReadOnlyList<PendingCommandRecord>>> GetUnsettledForAggregateAsync(string aggregateType, string aggregateId, CancellationToken ct = default);
    Task<Result<PendingCommandSummary>> GetSummaryAsync(CancellationToken ct = default);
    Task<Result<PagedResult<PendingCommandRecord>>> GetUnsettledPageAsync(int page, int pageSize, CancellationToken ct = default);
}
public sealed record PendingCommandRecord(/* … Id, OperationId, TenantId, CommandType, AggregateType, AggregateId, PayloadJson, CommandFingerprint, LocalSequence, SettlementState Status, CausationId, DependsOnOperationId, ExpectedAggregateVersion, …, FinancialEffect, ResponsibleRole, SettledAtUtc, ServerEntityId, ServerVersion, ClaimToken, ClaimedUntilUtc */);
public static class CommandFingerprint { public const char Separator = '\u001f'; public static string Compute(string commandType, string payloadJson); }

// Animora.Contracts.Wire (A01 + S06 v2) — reuse
public static class WireContract { public const int Version = 2; public const string HeaderName = "X-Animora-Contract-Version"; public static bool IsSupported(int v); }
public sealed record CommandEnvelope(int ContractVersion, string OperationId, string TenantId, string UserId, string DeviceId,
    string CommandType, string AggregateType, string? AggregateId, string PayloadJson, string CommandFingerprint,
    DateTimeOffset CreatedAtUtc, long LocalSequence, string? CausationId, string? DependsOnOperationId,
    long? ExpectedAggregateVersion, string? ExpectedServerVersion = null, string? RuleSetVersion = null) { public static CommandEnvelope Create(/* … matching optional trailing params */); }
public sealed record SettlementResult(string OperationId, SettlementOutcome Outcome, string? ErrorCode, string? Detail,
    string? ServerEntityId, string? ServerVersion, DateTimeOffset SettledAtUtc, FinancialEffect FinancialEffect, string? ResponsibleRole);
public enum SettlementOutcome { Accepted, Rejected, Conflict, Deferred }
public enum FinancialEffect  { None, Applied, NotApplied, Unknown }

// ====== S06 server surface this transport consumes (verbatim from PHASE-S06) ======
//   POST /api/commands        body CommandEnvelope    -> ALWAYS HTTP 200 + SettlementResult for a conversation that reached intake
//                                                        (Accepted | Rejected | Conflict | Deferred).
//   POST /api/commands/batch  body CommandEnvelope[]  (max 50) -> SettlementResult[] in REQUEST ORDER, each settled independently.
//   GET  /api/commands/{operationId}/ids              -> CommandIdMapResponse(IReadOnlyList<(int Slot, string EntityType, string ServerEntityId)>)
//   4xx/5xx + WireApiError only for transport/auth/shape: 400 VALIDATION_FAILED, 401/403 (auth), 413 BATCH_TOO_LARGE (>50),
//   426 CONTRACT_VERSION_UNSUPPORTED (+ header X-Animora-Supported-Versions: 1,2). Idempotency key = (TenantId, OperationId):
//   an identical retry returns the STORED result (never re-executes); DEPENDENCY_UNSETTLED / COMMAND_IN_PROGRESS settle Deferred.

// Animora.Domain.Results / Time (A02): Result/ErrorCode/ErrorCodes ; IClock.UtcNow. ULID via IdGenerator.
```

## §Design decisions

- **`HttpCommandTransport : ICommandTransport`** (new, `src/Animora.Sync.Custom/HttpCommandTransport.cs`) over the same named `HttpClient` G02 configured (`"animora-sync"` base address + `WireContract.HeaderName` = `2`). `SubmitAsync(envelope, ct)`: `POST /api/commands` with the envelope as JSON; on **HTTP 200** deserialize `SettlementResult` and return `Result.Ok(settlement)` (the four questions travel with it — the journal's `ApplySettlementAsync` records outcome/errorcode/financial-effect/responsible-role, §8.7); on **426** return `Result.Ok(SettlementResult Deferred with ErrorCode=CONTRACT_VERSION_UNSUPPORTED)` so the command stays pending until the client upgrades (never a silent drop); on **401/403** `Result.Fail(Unauthorized)` (G06 will refresh the token and retry); on **413** `Result.Fail(BatchTooLarge)` (only reachable from the batch path); on **400** `Result.Fail(ValidationFailed, detail)`; on **network/timeout/5xx/429** `Result.Ok(SettlementResult Deferred)` (stays pending, processor retries with backoff) — the key invariant: **a non-settled command is never lost and never double-applied** because `(TenantId, OperationId)` idempotency means a retry returns the stored result.
- **Batch submit** — a new optional capability `ICommandBatchTransport` (declared here, implemented by `HttpCommandTransport`) `Task<Result<IReadOnlyList<SettlementResult>>> SubmitBatchAsync(IReadOnlyList<CommandEnvelope> envelopes, CancellationToken ct = default)` that posts `POST /api/commands/batch` (≤ 50, chunked by the caller/processor) and returns results **in request order**. The existing client command processor (B02) uses the single `ICommandTransport` by default; the batch path is an optimization the processor may opt into when the journal has a run of independent commands. Ordering is never changed; dependent commands (`DependsOnOperationId`) are sent only after their dependency settled `Accepted`.
- **Id remap & dependency rewrite (`CommandIdRemapService`, `Animora.Sync.Custom`)** — the server mints its **own** ULIDs (S06 D-S06-3), so a local provisional id ≠ the server id. On `Accepted` the transport (a) stores `SettlementResult.ServerEntityId` + `ServerVersion` on the journal record (existing `ApplySettlementAsync`), and (b) fetches `GET /api/commands/{operationId}/ids` to build a `(localId → serverId)` map for **every** created aggregate (not just slot 0), persisted in a small `[LocalOnly]` map (reuse the existing journal store if it already carries server ids; otherwise a tiny `IdRemapStore` keyed `(TenantId, LocalId)` — prefer reusing B02's journal columns and only add a side table if a test proves it insufficient). Before a **dependent** pending command is submitted, `CommandIdRemapService.Rewrite(envelope)` replaces any provisional local id appearing in its `AggregateId`/`PayloadJson`/`DependsOnOperationId` with the resolved server id — so the server never sees a dangling client id. **The fingerprint is recomputed after rewrite** (the payload changed) and the journal record updated accordingly, keeping idempotency honest.
- **`ExpectedServerVersion` population** — before submit, for a command whose aggregate is in the replicated set, the transport reads the current server `Version` from the G04 replica base (`ISyncApplyStore`/a `IReplicaVersionReader` seam declared here, default reads `repl_base`) and sets `envelope.ExpectedServerVersion` so the server can detect a concurrency conflict (§8.3). Absent base row ⇒ null (no precondition). This is the only place the client provides the optimistic precondition.
- **Settlement reconciliation pass (`SettlementReconciler`)** — a method (driven by the existing B02 processor / G03 scheduler, not a new worker here) that, for journal records stuck `PendingSettlement`/`Deferred` beyond a grace window, re-queries the server via an idempotent re-submit (same `(TenantId, OperationId)` ⇒ stored result) to pull the authoritative settlement — closing the case where the client lost the response but the server committed. It transitions the local `SettlementState` strictly per `SettlementTransitions.EnsureAllowed` (A02; no illegal jumps) and surfaces `Conflict`/`NeedsAttention` to the UI with the responsible role.
- **New ErrorCodes (+1, region "Command transport"):** `BatchTooLarge = new("BATCH_TOO_LARGE", ErrorCode.Validation)` **only if** S06 did not already introduce it (`grep ErrorCodes.cs` — S06's acceptance mentions `BATCH_TOO_LARGE`; if present, **reuse**, add nothing). `Unauthorized`, `NetworkUnavailable`, `ServerBusy`, `ValidationFailed`, `ContractVersionUnsupported` are **reused** (introduced by A02 / G02 / S06). G05 introduces no new wire DTO.
- **DI** — extend `Animora.Sync.Custom`'s `AddAnimoraHttpSyncEngine` (or a sibling `AddAnimoraHttpCommandTransport`): when configured, `services.Replace(ServiceDescriptor.Singleton<ICommandTransport, HttpCommandTransport>())`, register `ICommandBatchTransport`, `CommandIdRemapService`, `IReplicaVersionReader`, `SettlementReconciler`. Unconfigured ⇒ `NullCommandTransport` (B02 registration) stays. Called from the same `App.axaml.cs` site as G02 — no new top-level call if folded into the existing extension (document which).
- **Not in this phase:** bearer token acquisition + 401-retry refresh on the HTTP client, device-active enforcement (**G06** adds the auth delegating handler the transport benefits from); the state-pull engine (**G01–G04**, separate pipeline); the server intake itself (**S06–S10**). No migration unless a test proves the id remap needs a side table (then next free client ordinal).

## §Implementation plan

1. `src/Animora.Sync.Custom/HttpCommandTransport.cs` — implements `ICommandTransport` + `ICommandBatchTransport`; HTTP-status → `Result<SettlementResult>` map per §Design decisions; `ConfigureAwait(false)` throughout.
2. `src/Animora.Application/Commands/ICommandBatchTransport.cs` (interface, beside `ICommandTransport`); `src/Animora.Sync.Custom/CommandIdRemapService.cs`, `IReplicaVersionReader.cs` (+ default over `repl_base`), `SettlementReconciler.cs`.
3. `src/Animora.Domain/Results/ErrorCodes.cs`: add `BatchTooLarge` only if absent; new `#region Command transport` (may be empty of new codes if all reused — then skip).
4. DI: conditional `Replace` of `ICommandTransport` in the existing `Animora.Sync.Custom` extension; wire the remap/reconciler/version-reader.
5. Tests in `tests/Animora.Sync.Tests/` + `tests/Animora.Application.Tests/` (processor interplay): see Acceptance (over an `HttpMessageHandler` fake + fake `ICommandJournal`).
6. `scripts/sandbox/build.sh full` → 0/0; `scripts/sandbox/test.sh Sync` + `scripts/sandbox/test.sh Application` → green; `scripts/sandbox/run.sh` → `SMOKE RUN OK` (unconfigured ⇒ `NullCommandTransport`).
7. `PROGRESS.md` per recipe; commit via `scripts/git/push.sh`.

## Files to create / modify

| Path | Contents |
|---|---|
| `src/Animora.Sync.Custom/HttpCommandTransport.cs` (new) | real `ICommandTransport`/`ICommandBatchTransport`; HTTP→`Result` map; HTTP-200-settlement |
| `src/Animora.Application/Commands/ICommandBatchTransport.cs` (new) | batch submit port (ordered results) |
| `src/Animora.Sync.Custom/CommandIdRemapService.cs` (new) | server id fetch + dependent-command rewrite + fingerprint recompute |
| `src/Animora.Sync.Custom/IReplicaVersionReader.cs` (new) | reads `repl_base` for `ExpectedServerVersion` |
| `src/Animora.Sync.Custom/SettlementReconciler.cs` (new) | idempotent re-query + strict `SettlementTransitions` |
| `src/Animora.Domain/Results/ErrorCodes.cs` (edit, maybe) | `BatchTooLarge` only if absent |
| `src/Animora.Sync.Custom/SyncCustomServiceCollectionExtensions.cs` (edit) | conditional `Replace` of `ICommandTransport` + registrations |
| `tests/Animora.Sync.Tests/*.cs`, `tests/Animora.Application.Tests/*.cs` (new) | transport + remap + reconcile + DI tests |

## Contracts / schema added

No new wire DTO (reuses S06's). New ports/types: `ICommandBatchTransport`, `HttpCommandTransport`, `CommandIdRemapService`, `IReplicaVersionReader`, `SettlementReconciler`. ErrorCodes +0/1 (`BatchTooLarge` iff absent). Endpoints consumed: `POST /api/commands`, `POST /api/commands/batch`, `GET /api/commands/{operationId}/ids`. No migration (unless a test forces a tiny id-remap side table → next free client ordinal).

## Rules that apply here

- **Two pipelines (§6, ARCH §8.1):** this is the command pipeline; it never touches the state-pull engine (G01–G04) and the engine never submits a command.
- **Idempotency & no loss (ARCH §8.3, R-B09):** key `(TenantId, OperationId)`; a retry returns the stored result; a transient fault leaves the command pending (`Deferred`), never dropped, never double-applied; the server mints its own ids and the client remaps — it never fabricates an id to match.
- **Sync-failure UX (ARCH §8.7, R-B16):** every non-settled command carries what/why/`FinancialEffect`/responsible-role onto the exact record; `FinancialEffect.Unknown` means "confirm before retry".
- **Layering / C# style (§2/§4):** `HttpCommandTransport` in `Animora.Sync.Custom` (the one client project allowed `System.Net.Http`); `sealed`, `Result`, `IClock`, ULID, `ConfigureAwait(false)`; `SettlementTransitions.EnsureAllowed` gates every local state change.
- **Offline-first (§11):** unconfigured ⇒ `NullCommandTransport` stays; the desktop posts and journals exactly as today.

## Out of scope (owning phase)

- Bearer token acquisition, 401-triggered refresh, device-active client enforcement → **G06** (auth delegating handler on the shared `HttpClient`).
- State replication engine, conflict apply, resync → **G01–G04** (separate pipeline). Server command intake/handlers → **S06–S10**.
- Validation gates / perf proof → **G07/G08**. Release → **G09**.
- Optional Datasync adapter → deferred (ROADMAP §5, STACK G1 gate).

## Acceptance criteria (mechanical)

1. `scripts/sandbox/build.sh full` = 0 warnings / 0 errors; `scripts/sandbox/run.sh` = `SMOKE RUN OK` (unconfigured ⇒ `NullCommandTransport`).
2. `scripts/sandbox/test.sh Sync` + `scripts/sandbox/test.sh Application` green with: `HttpCommandTransportTests.Http200Settlement_ReturnsSettlementResult`, `HttpCommandTransportTests.Accepted_RecordsServerIdAndVersion`, `HttpCommandTransportTests.Status426_SettlesDeferred_ContractUnsupported_NotDropped`, `HttpCommandTransportTests.Status401_FailsUnauthorized`, `HttpCommandTransportTests.NetworkFault_SettlesDeferred_StaysPending`, `HttpCommandTransportTests.IdempotentRetry_SameOperationId_UsesStoredResult`, `CommandBatchTransportTests.ResultsInRequestOrder`, `CommandBatchTransportTests.Over50_FailsBatchTooLarge`, `CommandIdRemapTests.RewritesDependentPayload_WithServerId_RecomputesFingerprint`, `CommandIdRemapTests.FetchesAllCreatedIds_NotJustSlot0`, `ReplicaVersionReaderTests.PopulatesExpectedServerVersion_FromBase`, `SettlementReconcilerTests.StuckDeferred_ReQuery_PullsAuthoritativeSettlement`, `SettlementReconcilerTests.IllegalTransition_Rejected_ByEnsureAllowed`, `CommandTransportDiTests.Configured_ReplacesNullTransport`, `CommandTransportDiTests.Unconfigured_KeepsNullTransport`.
3. `grep -rn "Npgsql\|Animora.Server\|Animora.Persistence.PostgreSql" src/Animora.Sync.Custom/HttpCommandTransport.cs` returns nothing; `grep -rn "IStateSyncEngine\|PullAsync" src/Animora.Sync.Custom/HttpCommandTransport.cs` returns nothing (two pipelines kept separate).
4. `grep -rn "SettlementTransitions.EnsureAllowed" src/Animora.Sync.Custom/SettlementReconciler.cs` ≥ 1 (no illegal state jumps); `grep -rn "CommandFingerprint.Compute" src/Animora.Sync.Custom/CommandIdRemapService.cs` ≥ 1 (fingerprint recomputed after rewrite).
5. `tests/Animora.Domain.Tests` `LayeringRulesTests.Desktop_HasNoStoreOrNetworkReferences` still passes; the B02 `NullCommandTransport` contract test still passes (fallback intact).

## PROGRESS.md update

At the end of this phase: set the `G05` row in "Phase status" to `✅ Done` (add date + one-line summary), advance **"Next phase" → `G06`**, append one `### G05` entry to "Repo state log" (`HttpCommandTransport` + batch port + id remap + replica version reader + reconciler, any ErrorCode added, test classes), log any deviation in the "Decision & deviation log", then commit with `scripts/git/push.sh -b genspark_ai_developer "feat(sync): G05 command transport & settlement reconciliation"`.
