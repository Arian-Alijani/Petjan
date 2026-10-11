# Execution plan — Phase G05 + G06 (client command transport, settlement, online auth/entitlement/device)

> **Purpose of this file.** A pre-researched, self-contained task list for implementing **G05** then **G06**.
> Every fact below was verified against the live tree on 2026-10-11; a fresh model can implement top-to-bottom
> **without re-reading the phase files or re-exploring the repo**. Facts that DIVERGE from the phase text are
> flagged `⚠️ DEVIATION` — trust this file over the phase file on those points (they were checked in-tree).
>
> **PUSH LAW (locked).** After completing **each** task below, run:
> `scripts/git/push.sh -b genspark_ai_developer "<conventional message>"`.
> If it exits code `3`, call the `setup_github_environment` tool once, then re-run. Never leave a task uncommitted.
> If a heavy fix / large code chunk is written mid-task, push it immediately with a clear message too.
>
> **Branch:** all work on `genspark_ai_developer` (task T0 creates/switches it).
> **Build/test law:** NO full test runs, NO long builds. Use targeted `scripts/sandbox/build.sh fast` and
> `scripts/sandbox/test.sh <filter>` only where a task says so. `scripts/sandbox/run.sh` smoke only at the two
> phase-end gates.
> **Last task deletes ONLY this file** (`Plan/G05-G06-PLAN.md`). Do not touch roadmap/phase files beyond the
> PROGRESS.md updates each phase explicitly requires.

---

## 0. Shared ground truth (verified in-tree — do not re-derive)

### 0.1 Environment / build
- Build: `scripts/sandbox/build.sh fast` (quick) / `full` (0 warn/0 err gate). Test: `scripts/sandbox/test.sh <filter>`.
  Smoke: `scripts/sandbox/run.sh` → prints `SMOKE RUN OK`. Prereqs once: `scripts/sandbox/install-prereqs.sh`.
- `dotnet-ef` is at `.sandbox/tools/dotnet-ef` — **never** `dotnet tool install`. Only G06 adds a migration.
- Memory-capped sandbox (~985 MiB); never raw `dotnet build`.

### 0.2 Project map (real names — csproj/namespace)
| Project | Namespace root | Notes |
|---|---|---|
| `src/Animora.Domain` | `Animora.Domain` | zero refs. Has `Results/ErrorCodes.cs`, `Primitives/SettlementState.cs` + `SettlementTransitions.cs`, `Time/IClock.cs`, `Identity/Permission.cs` (`RiskLevel`). |
| `src/Animora.Contracts.Wire` | `Animora.Contracts.Wire` | the wire DTOs (`Commands/`, `Auth/`, `Licensing/`, `Sync/`). **This is the wire project**, distinct from `Animora.Contracts`. |
| `src/Animora.Application` | `Animora.Application` | `Commands/` (ICommandTransport, journal…), `Security/` (IEntitlementGate…), `Abstractions/` (CommandContext/QueryContext). |
| `src/Animora.Sync.Custom` | `Animora.Sync.Custom` | the ONE client project allowed `System.Net.Http` (via `Microsoft.Extensions.Http`). Refs: Sync.Abstractions, Application, Persistence.Sqlite, Contracts.Wire. Has `ServerEndpointOptions`, `SyncFeedClient`, `SyncCustomServiceCollectionExtensions`, `Replica/SqliteSyncApplyStore`. |
| `src/Animora.Client.Services` | `Animora.Client.Services` | real contract impls. Refs: Application, Contracts, Persistence.Sqlite, Infrastructure.Local, Sync.Abstractions. **No Contracts.Wire / Sync.Custom / Server.Licensing ref yet** (G06 adds). |
| `src/Animora.Infrastructure.Local` | `Animora.Infrastructure.Local` | OS protection seam: `Security/ISecretStore.cs` (`Result<byte[]> TryRead/Write/Delete`), `Security/InstallationIdentity.cs` (`ComputeMachineFingerprintHash()`), `Security/CapabilitySnapshotSigner`, `PasswordHashingService`. |
| `src/Animora.Persistence.Sqlite` | `Animora.Persistence.Sqlite` | DbContext = **`AnimoraLocalDbContext`** (⚠️ DEVIATION: phase files say `AnimoraDbContext` — WRONG). Migrations folder head is `032_ReplicaStore`. `Replica/ReplicaBase.cs`, `Replica/ReplicaCheckpoint.cs`. `Context/ILocalTenantContext.cs` (`.TenantId`). |
| `src/Animora.Server.Licensing` | `Animora.Server.Licensing` | `Verification/` = client-reusable half (pure Domain+Wire). Whitelisted client→server ref (G06 only). |
| `src/Animora.Server` | `Animora.Server` | server host. **Client must NOT reference it.** Its `CommandIdMapResponse` is `internal` → client needs its own DTO. |

### 0.3 DI call site
`src/Animora.Desktop/App.axaml.cs` ~lines 402-404 already calls, in order:
```csharp
services.AddAnimoraSyncEngine();        // G01 Null engine (TryAdd)
services.AddAnimoraHttpSyncEngine();    // G02/G04 real engine when configured
services.AddAnimoraSyncOrchestration(); // G03
```
`AddAnimoraLocalInfrastructure(...)` is called ~line 237; `AddAnimoraApplication(...)` ~line 247 (registers `NullCommandTransport` + `AllowAllEntitlementGate` via Application defaults).

### 0.4 Server surface the client consumes (route prefixes from `ServerRoutes`, `internal` → hardcode strings)
- Commands: `POST /api/commands` · `POST /api/commands/batch` (≤50) · `GET /api/commands/{operationId}/ids`.
  - `POST` always HTTP 200 + `SettlementResult` for a reached-intake conversation. Transport/shape errors: 400 VALIDATION_FAILED, 401/403 auth, 413 BATCH_TOO_LARGE, 426 CONTRACT_VERSION_UNSUPPORTED (+ header `X-Animora-Supported-Versions`).
  - `/ids` server response shape (server type is internal): `{ "ids": [ { "slot": int, "entityType": string, "serverEntityId": string } ] }` — client declares its own matching DTO.
- Auth: `POST /auth/login` · `/auth/refresh` · `/auth/logout` · `/auth/device/register`.
- Licensing: `POST /licensing/activate` · `/licensing/renew` · `GET /licensing/status` · `GET /licensing/keys` (anonymous).
- Wire header on every request: `WireContract.HeaderName` = `"X-Animora-Contract-Version"`, value `"2"` (`WireContract.Version`).

### 0.5 Reused types — EXACT signatures (verified)
```csharp
// Animora.Application.Commands
public interface ICommandTransport { Task<Result<SettlementResult>> SubmitAsync(CommandEnvelope e, CancellationToken ct = default); }
public sealed class NullCommandTransport : ICommandTransport { /* Ok(Deferred, FinancialEffect.Unknown, SettledAtUtc=UnixEpoch) */ }
// ICommandJournal: EnqueueAsync, FindByOperationIdAsync, ApplySettlementAsync(SettlementResult),
//   ClaimBatchAsync(int,TimeSpan), ReleaseAsync(string,ErrorCode,TimeSpan), GetUnsettledForAggregateAsync(string,string),
//   GetSummaryAsync, GetUnsettledPageAsync(int,int)  — all Task<Result<...>>
public static class CommandFingerprint {
    public const char Separator = '\u001f';
    public static string Compute(string commandType, string payloadJson);        // UTF8(type + sep + payload) → SHA256 lower-hex
    public static string Compute<T>(string commandType, T payload);
    public static string CanonicalJson<T>(T payload);
}

// Animora.Contracts.Wire.Commands
public sealed record CommandEnvelope(int ContractVersion, string OperationId, string TenantId, string UserId,
    string DeviceId, string CommandType, string AggregateType, string? AggregateId, string PayloadJson,
    string CommandFingerprint, DateTimeOffset CreatedAtUtc, long LocalSequence, string? CausationId,
    string? DependsOnOperationId, long? ExpectedAggregateVersion, string? ExpectedServerVersion = null,
    string? RuleSetVersion = null) { public static CommandEnvelope Create(...trailing optionals...); }
public sealed record SettlementResult(string OperationId, SettlementOutcome Outcome, string? ErrorCode, string? Detail,
    string? ServerEntityId, string? ServerVersion, DateTimeOffset SettledAtUtc, FinancialEffect FinancialEffect, string? ResponsibleRole);
public enum SettlementOutcome { Accepted, Rejected, Conflict, Deferred }
public enum FinancialEffect  { None, Applied, NotApplied, Unknown }

// Animora.Contracts.Wire
public static class WireContract { public const int Version = 2; public const string HeaderName = "X-Animora-Contract-Version";
    public const string SupportedVersionsHeaderName = "X-Animora-Supported-Versions"; public static bool IsSupported(int v); }

// Animora.Domain.Primitives
public enum SettlementState { Draft, LocallyPosted, PendingSettlement, Settled, Rejected, Conflict, Reversed, NeedsAttention }
public static class SettlementTransitions {
    public static Result EnsureAllowed(SettlementState from, SettlementState to); // Fail → ErrorCodes.SettlementInvalidTransition
    public static bool IsAllowed(SettlementState from, SettlementState to);
}
// Domain.Identity.RiskLevel { Low=0 (read), Normal=1, High=2(correctable), Critical=? } — read enum before mapping.

// Animora.Sync.Custom
public sealed record ServerEndpointOptions { public const string SectionName = "Animora:Server"; public string? BaseAddress;
    public bool IsConfigured; public bool TryGetBaseUri(out Uri uri); /* + timeouts/limits */ }
```

### 0.6 ErrorCodes — already present (verified; DO NOT re-add)
`ValidationFailed`, `EntitlementRequired`, `UserLockedOut`, `DeviceRevoked`, `CredentialsInvalid`, `SessionExpired`,
`NetworkUnavailable`, `ServerBusy`, `Unauthorized`, **`BatchTooLarge`** (line ~730), `ContractVersionUnsupported`,
`LicenseInvalid`, `LicenseKeyUnknown`, `LicenseExpired`, `LicenseRevoked`, `LicenseDeviceMismatch`, `LicenseClockRollback`,
`OfflineCapabilityExpired`, `SettlementInvalidTransition`, `DependencyUnsettled`, `SyncShadowValidationFailed`.
→ **G05 adds 0 ErrorCodes** (`BatchTooLarge` exists). **G06 adds exactly 2**: `OfflineCredentialMismatch`,
`OfflineSessionUnavailable` (both absent — verified).

---

## Task list

> Status legend: `[ ]` pending · `[x]` done. Update the box when a task is pushed.
> **Every `[x]` must correspond to a `scripts/git/push.sh -b genspark_ai_developer "..."` that returned `✓`.**

### T0 — Branch setup `[x]`
- Switch/create branch: `scripts/git/push.sh -b genspark_ai_developer "chore(plan): add G05/G06 execution plan"` (this also pushes this plan file).
- (This commit is the plan file itself; subsequent tasks push their own code.)

---

## PHASE G05 — command transport & settlement reconciliation

> **Scope:** real `HttpCommandTransport` replacing `NullCommandTransport` only when `ServerEndpointOptions.IsConfigured`.
> All new code lives in `Animora.Sync.Custom` except the one new interface in `Animora.Application/Commands`.
> **No migration. No new ErrorCode** (`BatchTooLarge` already exists). **Two pipelines stay separate:** this file must
> NOT reference `IStateSyncEngine`/`PullAsync` (acceptance grep #3).

### G05 design decisions (locked — implement exactly)
- `HttpCommandTransport : ICommandTransport, ICommandBatchTransport` in `src/Animora.Sync.Custom/HttpCommandTransport.cs`.
  Construct over `IHttpClientFactory` client named `SyncFeedClient.HttpClientName` (reuse the SAME named client G02 configured)
  + `ServerEndpointOptions` + `IClock`. Attach header `WireContract.HeaderName: "2"` on each request.
  - `SubmitAsync(envelope, ct)`: `POST /api/commands` JSON body = envelope. Status map:
    - **200** → deserialize `SettlementResult` → `Result.Ok(settlement)`.
    - **426** → `Result.Ok(new SettlementResult(op, Deferred, ErrorCode=ErrorCodes.ContractVersionUnsupported.Code, detail, null,null, clock.UtcNow, FinancialEffect.Unknown, null))` — NOT dropped.
    - **401/403** → `Result.Fail(ErrorCodes.Unauthorized)`.
    - **413** → `Result.Fail(ErrorCodes.BatchTooLarge)` (only from batch path).
    - **400** → `Result.Fail(ErrorCodes.ValidationFailed, detail)`.
    - **network/timeout/5xx/429** → `Result.Ok(SettlementResult Deferred)` (stays pending, processor retries). Catch `HttpRequestException`/`TaskCanceledException` (not caller ct) → Deferred.
  - `ConfigureAwait(false)` everywhere; `sealed`; JSON via `System.Text.Json` (web defaults, camelCase matches wire).
- `ICommandBatchTransport` (NEW) in `src/Animora.Application/Commands/ICommandBatchTransport.cs`:
  `Task<Result<IReadOnlyList<SettlementResult>>> SubmitBatchAsync(IReadOnlyList<CommandEnvelope> envelopes, CancellationToken ct = default);`
  Impl in `HttpCommandTransport`: if `> 50` → `Result.Fail(BatchTooLarge)` (client-side guard, don't even POST). Else `POST /api/commands/batch`, deserialize `SettlementResult[]` returned **in request order**.
- `CommandIdRemapService` in `src/Animora.Sync.Custom/CommandIdRemapService.cs`:
  - `GET /api/commands/{operationId}/ids` → client DTO `CommandIdMapResponse(IReadOnlyList<CommandIdMapItem>)`, `CommandIdMapItem(int Slot, string EntityType, string ServerEntityId)` (declare locally — server's is internal).
  - Holds a `(TenantId, localId) → serverId` map. ⚠️ Per phase: prefer reusing journal `ServerEntityId` columns; **only add a side table if a test proves it insufficient**. Default impl: in-memory `ConcurrentDictionary` seeded from `ApplySettlementAsync`/`/ids` results (no persistence table unless forced) — keep it a seam so persistence can be added later.
  - `Rewrite(CommandEnvelope e)`: replaces any known provisional localId in `AggregateId`, `PayloadJson` (string replace of the id token), `DependsOnOperationId` with the resolved serverId; then **recompute** `CommandFingerprint.Compute(e.CommandType, newPayloadJson)` and return `e with { AggregateId=…, PayloadJson=…, DependsOnOperationId=…, CommandFingerprint=newFp }`. **Must call `CommandFingerprint.Compute`** (acceptance grep #4).
- `IReplicaVersionReader` + default in `src/Animora.Sync.Custom/IReplicaVersionReader.cs`:
  - `Task<string?> ReadServerVersionAsync(string aggregateType, string aggregateId, CancellationToken ct = default);`
  - Default `ReplicaVersionReader` reads `ReplicaBase.Version` for `(TenantId=ILocalTenantContext.TenantId, Generation=active from ReplicaCheckpoint, Entity=aggregateType, EntityId=aggregateId)` via a scoped `AnimoraLocalDbContext` (use `IServiceScopeFactory` like `SqliteSyncApplyStore`). Absent row ⇒ `null` ⇒ no precondition. Caller sets `envelope.ExpectedServerVersion` before submit.
- `SettlementReconciler` in `src/Animora.Sync.Custom/SettlementReconciler.cs`:
  - A method (driven by the existing B02 processor / G03 scheduler — NOT a new worker): for journal records stuck `PendingSettlement`/`Deferred` beyond a grace window, idempotently re-submit the SAME `(TenantId, OperationId)` envelope (server returns stored result) and apply the authoritative settlement. Every local state change goes through `SettlementTransitions.EnsureAllowed(from,to)` (acceptance grep #4 requires ≥1 call). Takes `ICommandJournal` + `ICommandTransport` + `IClock`.
- **DI** in `src/Animora.Sync.Custom/SyncCustomServiceCollectionExtensions.cs` (`AddAnimoraHttpSyncEngine`, inside the existing `if (options.IsConfigured)` block): `services.Replace(ServiceDescriptor.Singleton<ICommandTransport, HttpCommandTransport>())`; register `ICommandBatchTransport` (same singleton instance via factory resolving the HttpCommandTransport), `CommandIdRemapService`, `IReplicaVersionReader`→`ReplicaVersionReader`, `SettlementReconciler`. Unconfigured ⇒ untouched (NullCommandTransport stays).

### T1 — `ICommandBatchTransport` interface `[x]`
- Create `src/Animora.Application/Commands/ICommandBatchTransport.cs` (signature above). XML-doc: ordered results, batch optimization.
- `scripts/sandbox/build.sh fast` (quick compile check).
- **PUSH**: `scripts/git/push.sh -b genspark_ai_developer "feat(sync): G05 add ICommandBatchTransport port"`

### T2 — `HttpCommandTransport` (+ batch) `[x]`
- Create `src/Animora.Sync.Custom/HttpCommandTransport.cs` implementing both interfaces, per the status map above. Declare the local `CommandIdMapResponse`/`CommandIdMapItem` DTOs here or in CommandIdRemapService (shared).
- Guards: `grep` must find NO `Npgsql`/`Animora.Server`/`Animora.Persistence.PostgreSql`/`IStateSyncEngine`/`PullAsync` in this file.
- `scripts/sandbox/build.sh fast`.
- **PUSH**: `scripts/git/push.sh -b genspark_ai_developer "feat(sync): G05 HttpCommandTransport with HTTP→settlement map + batch"`

### T3 — `CommandIdRemapService` + `IReplicaVersionReader` + `SettlementReconciler` `[x]`
- Create the three files above. `ReplicaVersionReader` uses `IServiceScopeFactory`+`AnimoraLocalDbContext`+`ILocalTenantContext` (read active generation from `ReplicaCheckpoint` then `ReplicaBase.Version`).
- `SettlementReconciler` must call `SettlementTransitions.EnsureAllowed`; `CommandIdRemapService.Rewrite` must call `CommandFingerprint.Compute`.
- `scripts/sandbox/build.sh fast`.
- **PUSH**: `scripts/git/push.sh -b genspark_ai_developer "feat(sync): G05 id-remap, replica version reader, settlement reconciler"`

### T4 — DI wiring (conditional Replace) `[x]`
- Edit `SyncCustomServiceCollectionExtensions.AddAnimoraHttpSyncEngine`: inside `if (options.IsConfigured)` add the `Replace`+registrations (above). Keep `using Microsoft.Extensions.DependencyInjection.Extensions;`.
- No change to `App.axaml.cs` (folded into the already-called `AddAnimoraHttpSyncEngine`). Document this in the DI XML-doc comment.
- `scripts/sandbox/build.sh fast`.
- **PUSH**: `scripts/git/push.sh -b genspark_ai_developer "feat(sync): G05 conditional Replace of ICommandTransport + registrations"`

### T5 — Tests (Sync + Application) `[x]`
- `tests/Animora.Sync.Tests/` — add classes using a fake `HttpMessageHandler` + fake `ICommandJournal`:
  - `HttpCommandTransportTests`: `Http200Settlement_ReturnsSettlementResult`, `Accepted_RecordsServerIdAndVersion`, `Status426_SettlesDeferred_ContractUnsupported_NotDropped`, `Status401_FailsUnauthorized`, `NetworkFault_SettlesDeferred_StaysPending`, `IdempotentRetry_SameOperationId_UsesStoredResult`.
  - `CommandBatchTransportTests`: `ResultsInRequestOrder`, `Over50_FailsBatchTooLarge`.
  - `CommandIdRemapTests`: `RewritesDependentPayload_WithServerId_RecomputesFingerprint`, `FetchesAllCreatedIds_NotJustSlot0`.
  - `ReplicaVersionReaderTests`: `PopulatesExpectedServerVersion_FromBase` (seed a `ReplicaBase` row via in-memory/SQLite test support — reuse `ReplicaTestSupport.cs` pattern already in Sync.Tests).
  - `SettlementReconcilerTests`: `StuckDeferred_ReQuery_PullsAuthoritativeSettlement`, `IllegalTransition_Rejected_ByEnsureAllowed`.
  - `CommandTransportDiTests`: `Configured_ReplacesNullTransport`, `Unconfigured_KeepsNullTransport`.
- Run ONLY: `scripts/sandbox/test.sh Sync` (green). If a test needs Application-side interplay, add to `tests/Animora.Application.Tests/` and run `scripts/sandbox/test.sh Application`.
- **PUSH**: `scripts/git/push.sh -b genspark_ai_developer "test(sync): G05 transport/remap/reconcile/DI tests"`

### T6 — G05 verification gate `[x]`
- `scripts/sandbox/build.sh full` (0/0). `scripts/sandbox/run.sh` (`SMOKE RUN OK`, unconfigured ⇒ NullCommandTransport stays).
- Acceptance greps: `grep -rn "Npgsql\|Animora.Server\|Animora.Persistence.PostgreSql" src/Animora.Sync.Custom/HttpCommandTransport.cs` = empty; `grep -rn "IStateSyncEngine\|PullAsync" src/Animora.Sync.Custom/HttpCommandTransport.cs` = empty; `grep -rn "SettlementTransitions.EnsureAllowed" src/Animora.Sync.Custom/SettlementReconciler.cs` ≥1; `grep -rn "CommandFingerprint.Compute" src/Animora.Sync.Custom/CommandIdRemapService.cs` ≥1.
- If a grep fails, fix the smallest thing. **PUSH** any fix immediately.
- **PUSH**: `scripts/git/push.sh -b genspark_ai_developer "chore(sync): G05 full build + smoke verified"`

### T7 — G05 PROGRESS.md update `[x]`
- In `backend-roadmap/PROGRESS.md`: set `G05` status row → `✅ Done` (date `2026-10-11` + one-line summary); advance **"Next phase" → `G06`** (the prose line ~144 AND any status pointer); append one `### G05` entry to "Repo state log" (HttpCommandTransport + batch port + id remap + replica version reader + reconciler, "ErrorCodes +0 (BatchTooLarge reused)", test class list); log deviations (DbContext name note is G06-only; here note "no new ErrorCode — BatchTooLarge already present from S06").
- **PUSH**: `scripts/git/push.sh -b genspark_ai_developer "feat(sync): G05 command transport & settlement reconciliation"`

---

## PHASE G06 — online auth, entitlement & device lifecycle

> **Scope:** light the online legs of all three identity planes; keep desktop fully offline-capable. Real
> `OnlineEntitlementGate` replaces `AllowAllEntitlementGate` **only when configured**, deciding from the sealed
> local cache (never a live server call). Adds **one client migration** (`033_OnlineEntitlement`) + **2 ErrorCodes**.
> Only sanctioned client→server ref is `Animora.Server.Licensing.Verification` (already whitelisted in LayeringRulesTests).

### G06 — CRITICAL verified deviations from the phase file (trust THIS)
1. ⚠️ **DbContext is `AnimoraLocalDbContext`**, not `AnimoraDbContext`. EF command:
   `.sandbox/tools/dotnet-ef migrations add <ts>__033_OnlineEntitlement -c AnimoraLocalDbContext -p src/Animora.Persistence.Sqlite -s src/Animora.Persistence.Sqlite` (match the existing migration file-name convention `YYYYMMDDHHMMSS__033_OnlineEntitlement.cs` — see `032_ReplicaStore`). **Next free ordinal = `033`** (head is `032_ReplicaStore`).
2. ⚠️ **`ICapabilityStore` signature** is NOT `ReadAsync()/ReplaceAsync()`. Real:
   `Task<Result<StoredCapabilitySnapshot?>> GetAsync(string tenantId, string userId, string deviceId, ct)`,
   `Task<Result> ReplaceAsync(StoredCapabilitySnapshot snapshot, ct)`, `Task<Result> RevokeAsync(tenantId,userId,deviceId,revokedAtUtc,ct)`.
   `StoredCapabilitySnapshot(Id, TenantId, UserId, DeviceId, PermissionsJson, IssuedAtUtc, PerCapabilityExpiryJson, AbsoluteExpiryUtc, SnapshotSignature, SecurityStamp, RevokedAtUtc?)`.
3. ⚠️ **`CapabilityEvaluator.Evaluate`** takes `(snapshot, code, tenantId, userId, deviceId, currentSecurityStamp, nowUtc, highWaterUtc)` — 8 args incl. `highWaterUtc` (not the 7 in the phase pack). Returns `CapabilityEvaluation(State, Result, DiscardSnapshot)`.
4. ⚠️ **`SealedEntitlementMerge.Merge(existing, incoming)` returns `Result<SealedEntitlement>`** (not a bare `SealedEntitlement`). Handle `.IsFailure`.
5. ⚠️ **`LicensePolicy.Evaluate(LicensePayload p, DateTimeOffset nowUtc, HighWaterClock clock, string localInstallationIdHash)`** → `LicenseVerdict(State,…)`. `LicenseState { Valid, Grace, ReadOnly, Locked }`. `HighWaterClock(DateTimeOffset HighWaterUtc)` is a **record** with `.Advance(nowUtc)`.
6. ⚠️ **`CapabilityGate.IsAllowed(LicenseState state, RiskLevel risk, bool isRead = false)`** — 3 params. `ReadOnly` ⇒ only `isRead`. `Valid` ⇒ any defined risk. `Grace` ⇒ Low/Normal/High (not Critical). `Locked`/default ⇒ false.
7. ⚠️ **`LicenseVerifier.Verify(ParsedLicense, ILicenseKeySet)`** where `ILicenseKeySet.Find(keyId) → LicensePublicKey(KeyId, byte[] PublicKeyDer, NotBeforeUtc, NotAfterUtc?)` — the **Verification-half** `LicensePublicKey` (byte[] DER), NOT the wire `LicensePublicKey(PublicKeyDerBase64)`. Client must map wire `LicensePublicKeySet` (from `GET /licensing/keys`) → `LicenseKeySet(IReadOnlyList<Verification.LicensePublicKey>)` by Base64-decoding `PublicKeyDerBase64`. `LicenseEnvelope.Parse(token) → Result<ParsedLicense>`.
8. ⚠️ **OS protection seam** is `Animora.Infrastructure.Local.Security.ISecretStore` (`Result<byte[]> TryRead/Write/Delete(name)`, name must match `^[a-z0-9-]+$`), NOT a generic DPAPI class. Use it to protect tokens / sealed token. Device components from `InstallationIdentity.ComputeMachineFingerprintHash()` (already gathers machine GUID / OS install / volume) — wrap its raw components; only the `DeviceFingerprint.Compute(...)` 64-hex hash leaves the machine.
9. Password verifier: use existing `Animora.Application.Security.IPasswordHasher` (`Hash(pw)`, `Verify(hash,pw)→PasswordVerificationOutcome`) for the offline credential-mirror verifier.

### G06 design decisions (locked)
- **`IOnlineAuthClient` / `OnlineAuthClient`** in `src/Animora.Client.Services/Identity/`. Over the shared named `HttpClient` (`SyncFeedClient.HttpClientName`) + wire header `2`.
  - `LoginAsync(tenantCode, userName, password, ct)`: compute `DeviceInstallationIdHash = DeviceFingerprint.Compute(IDeviceFingerprintSource.Collect())`; `POST /auth/login` body `LoginRequest(tenantCode,userName,password,hash)`. 200 → persist session (`ISessionStore`), write credential-mirror, `POST /licensing/activate` → seal. 401 `CredentialsInvalid` / 423 `UserLockedOut` / 403 `DeviceRevoked` → `Result.Fail(<code>)`. Unreachable server → try offline credential-mirror path (below) else `Result.Fail(NetworkUnavailable)`.
  - `RefreshAsync(ct)`: `POST /auth/refresh` `RefreshRequest(refresh)`; 401 `SessionExpired` ⇒ clear session. `LogoutAsync(ct)`: `POST /auth/logout`.
- **`ISessionStore` / `ProtectedSessionStore` / `ClientSession`** in `src/Animora.Client.Services/Identity/`.
  ⚠️ **Put `ISessionStore` + `ClientSession` where BOTH `Animora.Client.Services` and `Animora.Sync.Custom` can reference them WITHOUT a cycle.** Client.Services → (adds) Sync.Custom would conflict with BearerAuthHandler needing ISessionStore. **Resolution: declare `ISessionStore` + `ClientSession` in `Animora.Application/Security/` (or a new `Animora.Application/Identity/`)** so `BearerAuthHandler` (Sync.Custom) and `ProtectedSessionStore` (Client.Services) both depend only on Application. `ClientSession(AccessToken, AccessExpiresUtc, RefreshToken, UserId, TenantId, DeviceId, Role, SecurityStamp)`. Tokens OS-protected via `ISecretStore`; row in new `ClientSessions` table, unique `(TenantId,UserId,DeviceId)`.
- **`BearerAuthHandler : DelegatingHandler`** in `src/Animora.Sync.Custom/`. Attaches `Authorization: Bearer <access>` from `ISessionStore` (skip for login/refresh/keys routes). On 401 (non-auth route): single-flight refresh via `SemaphoreSlim(1,1)`, retry once; second 401 or `SESSION_EXPIRED` ⇒ surface `Unauthorized`, no loop. Added to the named HttpClient pipeline only when configured.
- **`OnlineEntitlementGate : IEntitlementGate`** in `src/Animora.Client.Services/Security/`. `EvaluateAsync(command|query, context, ct)`:
  (a) read `SealedEntitlement` via `ISealedEntitlementStore.ReadAsync`; `LicenseEnvelope.Parse(token)` → `LicenseVerifier.Verify(parsed, keySet)` (keySet from cached/embedded `LicensePublicKeySet`); (b) `LicensePolicy.Evaluate(payload, clock.HighWaterUtc→read max(IClock.UtcNow, highWater), clock, localInstallationIdHash)` → `LicenseVerdict.State`; (c) map command/query → `RiskLevel` (command: default Normal unless a richer map exists; query: read-class, `isRead:true`); return `Result.Ok()` iff `CapabilityGate.IsAllowed(State, risk, isRead)` else `Result.Fail(EntitlementRequired, "<state>:<reason>")`. **NEVER calls server** (acceptance: fake handler never hit during Evaluate). Registered `Replace` only when configured.
- **`IDeviceFingerprintSource` / `DeviceFingerprintSource`** in `src/Animora.Infrastructure.Local/Identity/`: `Collect() → IReadOnlyList<string>` from stable machine components (reuse `InstallationIdentity` internals); raw values never sent — only `DeviceFingerprint.Compute` hash.
- **`SealedEntitlementStore : ISealedEntitlementStore`** in `src/Animora.Infrastructure.Local/Identity/`: atomic (temp-file+rename or DB row) OS-protected store; **forward-only via `SealedEntitlementMerge.Merge` (handle Result)**; one row per tenant in new `SealedEntitlements` table.
- **`DeviceLifecycleService`** in `src/Animora.Client.Services/Identity/`: `POST /auth/device/register` on first online login; on 403 `DEVICE_REVOKED` → clear session + wipe sealed cache + capability snapshot (offline authority gone at next contact).
- **Credential-mirror:** on successful online login mirror (OS-protected): capability snapshot (`ICapabilityStore.ReplaceAsync`), sealed entitlement, one-way password verifier (`IPasswordHasher.Hash` stored as `OfflineCredentialVerifier` in `ClientSessions`). Cold offline start: `LoginAsync` detects unreachable + matching mirror + snapshot/seal in-window + `IPasswordHasher.Verify` ⇒ grant offline session. Fail-closed otherwise (`OfflineCredentialMismatch` / `OfflineSessionUnavailable`).
- **New ErrorCodes** (region "Online entitlement"): `OfflineCredentialMismatch = new("OFFLINE_CREDENTIAL_MISMATCH", ErrorCode.Forbidden)`, `OfflineSessionUnavailable = new("OFFLINE_SESSION_UNAVAILABLE", ErrorCode.Forbidden)` — both absent, add both.
- **New tables (migration `033_OnlineEntitlement`, both `[LocalOnly]`):** `ClientSessions(Id, TenantId, UserId, DeviceId, AccessToken, AccessExpiresUtc, RefreshToken, Role, SecurityStamp, OfflineCredentialVerifier, UpdatedUtc)` unique `(TenantId,UserId,DeviceId)`; `SealedEntitlements(TenantId PK, LicenseToken, HighWaterUnixMs, HighestSequenceSeen, InstallationIdHash, WrittenUnixMs)`. Mirror the `[LocalOnly]` + EF-config pattern of `ReplicaBase`/`ReplicaCheckpoint` (see `Configurations/Sync/ReplicaConfigurations.cs`, `Conventions/ModelConventions.cs`). No `Version`/`Deleted` columns (so `LocalOnlyTablesTest` passes).
- **DI `AddAnimoraOnlineIdentity`** (fold into `Animora.Sync.Custom` ext or a sibling in `Client.Services` — document which; recommended: a new `Client.Services` extension called from `App.axaml.cs` right after `AddAnimoraSyncOrchestration()`, plus `BearerAuthHandler` added to the named HttpClient inside `AddAnimoraHttpSyncEngine` when configured so the one HttpClient has it): when `ServerEndpointOptions.IsConfigured` ⇒ `services.Replace(ServiceDescriptor.Singleton<IEntitlementGate, OnlineEntitlementGate>())` + register `IOnlineAuthClient`, `ISessionStore`, `ISealedEntitlementStore`, `IDeviceFingerprintSource`, `DeviceLifecycleService`. Unconfigured ⇒ `AllowAllEntitlementGate` stays, no handler.

### G06 csproj edits (required — verified refs)
- `Animora.Client.Services.csproj`: add `ProjectReference` to `Animora.Contracts.Wire`, `Animora.Sync.Custom`, and `Animora.Server.Licensing`. (Whitelist already allows `Animora.Server.Licensing` for `Animora.Client.Services`/`Desktop`/`Infrastructure.Local`.)
- `Animora.Sync.Custom.csproj`: BearerAuthHandler needs `ISessionStore` from Application (already referenced) — no new ref if ISessionStore lives in Application.
- `Animora.Infrastructure.Local.csproj`: add ref to `Animora.Server.Licensing` (for `DeviceFingerprint`, `SealedEntitlement`) and `Animora.Contracts.Wire` if not present.

### T8 — ErrorCodes (2) + `ISessionStore`/`ClientSession` in Application `[x]`
- Edit `src/Animora.Domain/Results/ErrorCodes.cs`: add `#region Online entitlement (G06)` with the 2 codes (both absent — verified).
- Create `src/Animora.Application/Security/ISessionStore.cs` with `ClientSession` record + `ISessionStore` (`Task<Result<ClientSession?>> ReadAsync(ct)`, `Task<Result> WriteAsync(ClientSession, ct)`, `Task<Result> ClearAsync(ct)`).
- `scripts/sandbox/build.sh fast`.
- **PUSH**: `scripts/git/push.sh -b genspark_ai_developer "feat(identity): G06 add 2 ErrorCodes + ISessionStore port"`

### T9 — EF tables + migration `033_OnlineEntitlement` `[ ]`
- Add `[LocalOnly]` entities + EF configs for `ClientSessions` + `SealedEntitlements` in `Animora.Persistence.Sqlite` (mirror ReplicaBase pattern; no Version/Deleted).
- Generate migration: `.sandbox/tools/dotnet-ef migrations add <timestamp>__033_OnlineEntitlement -c AnimoraLocalDbContext -p src/Animora.Persistence.Sqlite -s src/Animora.Persistence.Sqlite` (confirm ordinal `033` is free: `ls src/Animora.Persistence.Sqlite/Migrations | tail`).
- `scripts/sandbox/build.sh fast`.
- **PUSH**: `scripts/git/push.sh -b genspark_ai_developer "feat(persistence): G06 ClientSessions + SealedEntitlements [LocalOnly] + migration 033"`

### T10 — Infrastructure.Local: fingerprint source + sealed store `[ ]`
- `src/Animora.Infrastructure.Local/Identity/DeviceFingerprintSource.cs` (`IDeviceFingerprintSource.Collect()` — raw never leaves).
- `src/Animora.Infrastructure.Local/Identity/SealedEntitlementStore.cs` (`ISealedEntitlementStore`, atomic + OS-protected via `ISecretStore`, forward-only via `SealedEntitlementMerge.Merge` handling `Result`).
- Add csproj refs (Server.Licensing, Contracts.Wire) if missing.
- `scripts/sandbox/build.sh fast`.
- **PUSH**: `scripts/git/push.sh -b genspark_ai_developer "feat(local): G06 device fingerprint source + sealed entitlement store"`

### T11 — Client.Services: ProtectedSessionStore + OnlineAuthClient + DeviceLifecycle `[ ]`
- Add csproj refs (Contracts.Wire, Sync.Custom, Server.Licensing).
- `Identity/ProtectedSessionStore.cs` (`ISessionStore` over `ClientSessions`, tokens via `ISecretStore`).
- `Identity/OnlineAuthClient.cs` (+ `IOnlineAuthClient`): login/refresh/logout + offline credential-mirror path.
- `Identity/DeviceLifecycleService.cs`: register + `DEVICE_REVOKED` wipe.
- `scripts/sandbox/build.sh fast`.
- **PUSH**: `scripts/git/push.sh -b genspark_ai_developer "feat(identity): G06 session store, online auth client, device lifecycle"`

### T12 — BearerAuthHandler (Sync.Custom) + OnlineEntitlementGate (Client.Services) `[ ]`
- `src/Animora.Sync.Custom/BearerAuthHandler.cs`: single-flight 401 refresh delegating handler.
- `src/Animora.Client.Services/Security/OnlineEntitlementGate.cs`: sealed-cache-only decision (never server). Must `grep` show `SealedEntitlementMerge.Merge`/`HighWaterClock` usage across Client.Services+Infrastructure.Local (acceptance #4) — the store (T10) covers Merge; gate uses `HighWaterClock`.
- `scripts/sandbox/build.sh fast`.
- **PUSH**: `scripts/git/push.sh -b genspark_ai_developer "feat(identity): G06 bearer auth handler + online entitlement gate"`

### T13 — DI wiring (`AddAnimoraOnlineIdentity` + handler) `[ ]`
- Add the extension (conditional `Replace` of `IEntitlementGate` + registrations). Add `BearerAuthHandler` to the named HttpClient in `AddAnimoraHttpSyncEngine` when configured. Call `AddAnimoraOnlineIdentity()` from `App.axaml.cs` after `AddAnimoraSyncOrchestration()`.
- Must `grep` show `ServerEndpointOptions.IsConfigured` + `Replace(ServiceDescriptor` in Sync.Custom/Client.Services (acceptance #4).
- `scripts/sandbox/build.sh fast`.
- **PUSH**: `scripts/git/push.sh -b genspark_ai_developer "feat(identity): G06 conditional online-identity DI + bearer handler wiring"`

### T14 — Tests (Application + Sync + Persistence) `[ ]`
- `tests/Animora.Application.Tests/`: `OnlineEntitlementGateTests` — `ValidLicense_AllowsByRisk`, `Grace_AllowsLowNormalHigh_DeniesReadOnly`, `ReadOnly_AllowsQuery_DeniesCommand`, `Locked_DeniesEverything`, `DecidesWithoutServerCall` (fake handler never hit), `BackwardClock_DoesNotReviveExpired`.
- `tests/Animora.Sync.Tests/`: `BearerAuthHandlerTests` — `Attaches_Bearer`, `On401_SingleFlightRefresh_RetriesOnce`, `RefreshSessionExpired_SurfacesUnauthorized_NoLoop`; `OnlineAuthClientTests` — `Login200_PersistsSession_AndSealsEntitlement`, `ServerUnreachable_UsesCredentialMirror_WhenWindowsValid`, `DeviceRevoked_WipesOfflineAuthority`; `DeviceFingerprintSourceTests.RawComponentsNeverLeave_OnlyHashSent`; `OnlineIdentityDiTests` — `Configured_ReplacesAllowAllGate`, `Unconfigured_KeepsAllowAllGate`.
- `tests/Animora.Persistence.Tests/`: `SealedEntitlementStoreTests.Write_IsForwardOnly_IgnoresLowerHighWaterOrSequence`; `LocalOnlyTablesTest` asserting `ClientSessions`/`SealedEntitlements` are LocalOnly (no sync manifest / no Version/Deleted).
- Run ONLY the needed filters: `scripts/sandbox/test.sh Application`, `scripts/sandbox/test.sh Sync`, `scripts/sandbox/test.sh Persistence`.
- **PUSH**: `scripts/git/push.sh -b genspark_ai_developer "test(identity): G06 gate/handler/auth/device/sealed-cache tests"`

### T15 — G06 verification gate `[ ]`
- `scripts/sandbox/build.sh full` (0/0). `scripts/sandbox/run.sh` (`SMOKE RUN OK`, unconfigured ⇒ AllowAllEntitlementGate stays).
- Acceptance greps: `grep -rn "Npgsql\|Animora.Server\b\|Animora.Persistence.PostgreSql" src/Animora.Client.Services src/Animora.Sync.Custom` = empty; only `Animora.Server.*` client ref is `Animora.Server.Licensing.Verification`; `grep -rn "SealedEntitlementMerge.Merge\|HighWaterClock" src/Animora.Client.Services src/Animora.Infrastructure.Local` ≥1; `grep -rn "ServerEndpointOptions.IsConfigured\|Replace(ServiceDescriptor" src/Animora.Sync.Custom src/Animora.Client.Services` ≥1; migration `033` is last in folder; run `scripts/sandbox/test.sh Domain` to confirm `LayeringRulesTests` still green.
- Fix smallest thing on any failure; **PUSH** immediately.
- **PUSH**: `scripts/git/push.sh -b genspark_ai_developer "chore(identity): G06 full build + smoke + layering verified"`

### T16 — G06 PROGRESS.md update `[ ]`
- `backend-roadmap/PROGRESS.md`: set `G06` → `✅ Done` (date + summary); advance **"Next phase" → `G07`**; append one `### G06` entry (online auth client + bearer handler + OnlineEntitlementGate + device lifecycle + sealed cache + credential-mirror, migration ordinal `033`, "+2 ErrorCodes", test classes); log deviations (list the 9 verified deviations above — esp. DbContext name, ICapabilityStore/CapabilityEvaluator signatures, Merge returning Result, ISessionStore placed in Application). Also update the "Post-C11 drift" migration note if it enumerates heads (set head `033`).
- **PUSH**: `scripts/git/push.sh -b genspark_ai_developer "feat(sync): G06 online auth, entitlement & device lifecycle"`

---

### T17 — FINAL: delete this plan file `[ ]`
- Delete ONLY `Plan/G05-G06-PLAN.md`. Do not touch any other file.
- **PUSH**: `scripts/git/push.sh -b genspark_ai_developer "chore(plan): remove completed G05/G06 plan"`

---

## Quick reference — do NOT re-explore for these
- Named HttpClient: `SyncFeedClient.HttpClientName` (reuse; adds no new client). Base address from `ServerEndpointOptions`.
- Scoped DB access pattern: copy `SqliteSyncApplyStore` (`IServiceScopeFactory` → `AnimoraLocalDbContext` + `ILocalTenantContext`).
- Test support for Replica rows already exists: `tests/Animora.Sync.Tests/ReplicaTestSupport.cs`.
- `scripts/git/push.sh` exit 3 ⇒ call `setup_github_environment` tool once, re-run.
- Keep every new type `sealed`, honor nullable, `CancellationToken ct = default`, `ConfigureAwait(false)`, no `.Result`/`.Wait()`, time via `IClock`, expected failures as `Result`.
