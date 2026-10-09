# S06 — Command Intake & Idempotency — Execution Plan (TODO)

> **Self-contained worklist.** Everything a fresh session needs is here. Do NOT re-read the whole repo.
> Source phase file: `backend-roadmap/phases/PHASE-S06-command-intake-idempotency.md` (deleted by the final task).
> **Branch:** `genspark_ai_developer`. **PUSH RULE (locked):** after EVERY task below, run
> `scripts/git/push.sh "feat(server): S06 Txx — <summary>"`. Exit 3 ⇒ call `setup_github_environment` once, re-run.
> Exit 2 ⇒ resolve conflict (prefer remote), re-run. Never advance a task without a green push.

---

## 0. Pre-verified repo facts (do NOT re-derive — audited 2026-10-09)

- **Build/test/run ONLY via sandbox scripts** (mem-capped ~985 MiB; never raw `dotnet build`):
  `scripts/sandbox/build.sh [fast|full]` (expect 0 warn/0 err), `scripts/sandbox/test.sh [filter]`,
  `scripts/sandbox/run.sh` (→ `SMOKE RUN OK`). `scripts/sandbox/install-prereqs.sh` once (idempotent).
- **EF CLI:** `.sandbox/tools/dotnet-ef` ONLY. Never `dotnet tool install`.
- **Next free server migration ordinal = `104`.** Existing server migrations: `101_ServerFoundation`,
  `102_Identity`, `103_SigningKeys` (dir `src/Animora.Persistence.PostgreSql/Migrations/Server/`).
  Phase expects `104_CommandIntake`. `ServerSchemaVersion.ExpectedHead` is currently `"103_SigningKeys"`
  (`src/Animora.Persistence.PostgreSql/ServerSchemaVersion.cs:10`) → bump to `"104_CommandIntake"`.
- **Only `Animora.Persistence.PostgreSql` may reference Npgsql.** `Animora.Server` must NOT reference
  Client.Services / Persistence.Sqlite / Infrastructure.Local / Sync.Custom / Desktop.
- **PG integration tests** read conn string from env `ANIMORA_PG_TEST_CONNECTION`; unset ⇒ **skip with reason**
  (not fail). `ANIMORA_REQUIRE_PG=1` is a CI gate owned by G07 — not our concern here.
- **Offline invariant:** desktop must start/operate with no server/PG/network. This phase adds nothing to desktop
  startup. After the Application edit, confirm desktop untouched: `scripts/sandbox/run.sh` → `SMOKE RUN OK`.
- **Layering/style (binding):** `sealed` by default; `sealed record` DTOs; every async public method takes
  `CancellationToken ct = default` + `.ConfigureAwait(false)` in library projects; no `.Result`/`.Wait()`;
  expected failures return `Result`/`Result<T>` with `ErrorCode`, never exceptions; inject `IClock`/`TimeProvider`
  (no `DateTime.Now/UtcNow`); ids are ULID strings via `IdGenerator`, never `Guid` in business code; EF reads
  `AsNoTracking()`; migrations additive, never edited later; `EnsureCreated` forbidden outside test fixtures.
- **Ship NO domain handler.** With none registered every command settles `Rejected(HANDLER_NOT_REGISTERED)`.
  Concrete handlers = S07–S10. `IServerOutbox` is a **no-op seam** here (real writes = S11/S13).

### Verified existing types/signatures (reuse verbatim — do not redefine)

```csharp
// src/Animora.Contracts.Wire/Commands/CommandEnvelope.cs  (positional record, 15 params)
public sealed record CommandEnvelope(int ContractVersion, string OperationId, string TenantId, string UserId,
  string DeviceId, string CommandType, string AggregateType, string? AggregateId, string PayloadJson,
  string CommandFingerprint, DateTimeOffset CreatedAtUtc, long LocalSequence, string? CausationId,
  string? DependsOnOperationId, long? ExpectedAggregateVersion)
{ public static CommandEnvelope Create(... 11 required + causationId=null, dependsOnOperationId=null,
    expectedAggregateVersion=null) => new(WireContract.Version, ...); }

// src/Animora.Contracts.Wire/WireContract.cs
public static class WireContract { public const int Version = 1; public const string HeaderName = "X-Animora-Contract-Version"; }

// Settlement vocabulary (Contracts.Wire.Commands) — exists, reuse:
public sealed record SettlementResult(string OperationId, SettlementOutcome Outcome, string? ErrorCode,
  string? Detail, string? ServerEntityId, string? ServerVersion, DateTimeOffset SettledAtUtc,
  FinancialEffect FinancialEffect, string? ResponsibleRole);
public enum SettlementOutcome { Accepted, Rejected, Conflict, Deferred }
public enum FinancialEffect { None, Applied, NotApplied, Unknown }
public static class CommandTypes { /* 88 const today; never freeze */ public static IReadOnlyList<string> All { get; } }

// src/Animora.Application/ApplicationServiceCollectionExtensions.cs  (NOT under Dispatch/)
public static IServiceCollection AddAnimoraApplication(this IServiceCollection services,
  Action<ApplicationRegistrationBuilder>? configure = null)
  // ...TryAdd defaults...; var registry = builder.Build(Dispatcher.Chain); services.AddSingleton(registry);
  // services.TryAddScoped<IDispatcher, Dispatcher>();

// src/Animora.Application/Dispatch/Dispatcher.cs
public static IReadOnlyList<IPipelineBehavior> Chain { get; } =
  [ new CorrelationBehavior(), new ExceptionMappingBehavior(), new ValidationBehavior(),
    new AuthorizationBehavior(), new EntitlementBehavior(), new TransactionBehavior(),
    new CommandJournalBehavior(), new AuditBehavior() ];
// ApplicationRegistrationBuilder.Build(IReadOnlyList<IPipelineBehavior>) is INTERNAL.

// src/Animora.Application/Abstractions/* — ICommand<TResult>, ICommandHandler<,>, CommandContext(record),
//   IUnitOfWork { SaveAsync; ExecuteAsync<T> }, IDispatcher { SendAsync; QueryAsync }, IQuery<TResult>, QueryContext.
// CommandContext(string TenantId, UserId, DeviceId, OperationId, CorrelationId, string? CausationId,
//   string? DependsOnOperationId, long? ExpectedAggregateVersion, DateTimeOffset CreatedAtUtc)

// src/Animora.Domain/Results/ErrorCodes.cs — regions end at line 684 ("History/Dashboard/... (C10)").
//   New region goes AFTER 684. Already-declared reusable codes (DO NOT re-add):
//   ConcurrencyConflict("CONCURRENCY_CONFLICT",Conflict), PermissionDenied, PermissionUnknown,
//   TenantMismatch("TENANT_MISMATCH",Forbidden), IdempotencyFingerprintMismatch(Integrity),
//   DependencyUnsettled("DEPENDENCY_UNSETTLED",Conflict), EntitlementRequired(Entitlement),
//   HandlerNotRegistered, DispatchFailed, ValidationFailed, NotFound, CommandAlreadySettled, Unexpected.
//   ErrorCode categories: Validation|NotFound|Conflict|Forbidden|Integrity|Infrastructure|Entitlement.

// src/Animora.Contracts.Wire/CommandFingerprint.cs — Compute(commandType, payloadJson); CanonicalJson<T>(payload);
//   Separator = '\u001f'. SERVER RE-COMPUTES and compares; never trusts the client's value.

// S04/S05 seams (all in src/Animora.Server/Authorization/, internal):
//   interface ICommandAuthorizer { Task<Result> AuthorizeAsync(string commandType, AuthorizationSubject subject, ct); }
//   interface IServerEntitlementGate { ValueTask<Result> EvaluateAsync(TenantEntitlementContext ctx, ct); }
//     record TenantEntitlementContext(string TenantId, string UserId, string? CommandType);
//   RequestTenantContext : ITenantContext (delegates to AuthTenantContext); TenantId/IsOperator; Bind(tenantId).
//   AuthorizationSubject is in src/Animora.Application/Security/IPermissionEvaluator.cs.
// ITenantContext (src/Animora.Persistence.PostgreSql/Tenancy/ITenantContext.cs): string? TenantId; bool IsOperator.
// ISecurityAuditWriter — S04; impl SecurityAuditWriter in Persistence.PostgreSql/Identity/. Writes SecurityAudit rows.
// ServerUnitOfWork : IUnitOfWork (src/.../Transactions/ServerUnitOfWork.cs) ctor(AnimoraServerDbContext, ITransactionRunner).
// AnimoraServerDbContext — the server DbContext (migrations target --context AnimoraServerDbContext).

// src/Animora.Server/Hosting/: ServerRoutes.Commands = "/api/commands" (internal static).
//   RateLimitPolicies.Commands exists (internal static). Both already wired in S01.
```

---

## §Design decisions (fixed names — implement exactly, no re-derivation)

**D-S06-1 (handler reuse / server pipeline profile).** Server runs the SAME Application handlers via a
*server profile*. The ONLY `Animora.Application` edit (besides additive ports):
- New enum `ApplicationPipelineProfile { Client, Server }` (file `Dispatch/ApplicationPipelineProfile.cs`).
- New `Dispatcher.ServerChain = [Correlation, ExceptionMapping, Validation, Authorization, Entitlement, Transaction, Audit]`
  — **NO `CommandJournal`** (client-only: needs `ICommandJournal`+`ILocalSequenceAllocator`, SQLite). `Chain` UNCHANGED.
- `AddAnimoraApplication(this IServiceCollection, Action<ApplicationRegistrationBuilder>? configure = null,
  ApplicationPipelineProfile profile = ApplicationPipelineProfile.Client)` — pick chain by profile:
  `profile == Server ? Dispatcher.ServerChain : Dispatcher.Chain` → `builder.Build(chain)`. Default = Client so all
  existing client tests keep passing untouched.

**Wire v2 (additive).** `CommandEnvelope` gains TWO trailing optional params: `string? ExpectedServerVersion = null`
(canonical lowercase UUID the client last observed for the aggregate) and `string? RuleSetVersion = null`.
`CommandEnvelope.Create(...)` gains matching optional params AT THE END. `WireContract.Version` → **2**.
Add `WireContract.SupportedContractVersions = {1,2}` and `WireContract.IsSupported(int v)`. `ExpectedAggregateVersion`
(numeric, v1) STAYS but server IGNORES it as concurrency evidence. `SettlementResult.ServerVersion` carries the
applied canonical UUID on Accepted. **Fix every test asserting `WireContract.Version == 1`** (search `tests/**`).

**Endpoints** (under `ServerRoutes.Commands`, rate-limit `RateLimitPolicies.Commands`, `[Authorize]` + tenant ctx S05):
- `POST /api/commands` body `CommandEnvelope` → **ALWAYS HTTP 200 + `SettlementResult`** when the conversation reached
  intake (Accepted/Rejected/Conflict/Deferred). 4xx/5xx + `WireApiError` ONLY for transport/auth/shape:
  `400 VALIDATION_FAILED` (malformed envelope), `401/403` (S05), `426 CONTRACT_VERSION_UNSUPPORTED`
  (version ∉ {1,2}; add header `X-Animora-Supported-Versions: 1,2`), `413 BATCH_TOO_LARGE` (>50).
- `POST /api/commands/batch` body `CommandEnvelope[]` (max **50** else `413 BATCH_TOO_LARGE`) → `SettlementResult[]`
  **in request order**; each settles **independently** (NOT one transaction); never reordered.
- `GET /api/commands/{operationId}/ids` → `CommandIdMapResponse(IReadOnlyList<(int slot, string entityType, string serverEntityId)>)`.

**Identity never trusted from envelope.** Build `CommandContext` from authenticated principal + S05 tenant context
(TenantId/UserId/DeviceId). Envelope TenantId/UserId/DeviceId differing from resolved ⇒ `Rejected(TENANT_MISMATCH)` +
SecurityAudit row `command.tenant_mismatch`. Server recomputes `CommandFingerprint(commandType, payloadJson)`;
mismatch vs envelope ⇒ `Rejected(IDEMPOTENCY_FINGERPRINT_MISMATCH)` + integrity audit.

**Idempotency store — table `CommandIntake`** (tenant-scoped, composite PK `(TenantId, OperationId)`), columns:
`CommandType varchar(64)`, `Fingerprint char(64)`, `Status smallint` (`IntakeStatus {Received=0,InProgress=1,Settled=2,Failed=3}`),
`Outcome smallint?` (`SettlementOutcome`), `ErrorCode varchar(64)?`, `Detail text?` (≤512), `ServerEntityId varchar(26)?`,
`ServerVersion uuid?`, `FinancialEffect smallint`, `ResponsibleRole varchar(32)?`, `ReceivedUtc`, `SettledUtc?`,
fenced lease `AttemptId varchar(26)?`, `LeaseOwner varchar(64)?`, `LeaseUntilUtc?`, `PayloadJson jsonb` (nulled on
compaction), `DependsOnOperationId varchar(26)?`, `CorrelationId`. **NOT a `SyncableEntity`** (no Version/Deleted) but
carries the `"Tenant"` filter. Indexes: PK; `IX_CommandIntake_TenantId_Status_LeaseUntilUtc`;
`IX_CommandIntake_TenantId_DependsOnOperationId`.
**Secondary table `CommandIdMap`** `(TenantId, OperationId, Slot smallint, EntityType varchar(32), ServerEntityId varchar(26))`,
PK `(TenantId, OperationId, Slot)` — records EVERY aggregate id a command created (primary = slot 0).

**Intake algorithm** (`CommandIntakeService.SubmitAsync(envelope, ct)`, steps in order):
1. version gate (`WireContract.IsSupported`).
2. identity + fingerprint checks (above).
3. `INSERT … ON CONFLICT (TenantId, OperationId) DO NOTHING RETURNING` with `Status=InProgress`, new `AttemptId`,
   `LeaseUntil = now+60s`. If row **already existed**: `Settled` ⇒ return STORED `SettlementResult` (same fingerprint),
   **never re-execute**; stored fingerprint ≠ incoming ⇒ `Rejected` integrity + audit; `InProgress` + unexpired lease ⇒
   `Deferred(COMMAND_IN_PROGRESS)`; `InProgress` + expired lease ⇒ **recovery**; `Failed` ⇒ new attempt (re-lease, re-run).
4. dependency: `DependsOnOperationId` present ⇒ must be `Settled+Accepted` in tenant else `Deferred(DEPENDENCY_UNSETTLED)`;
   stored `Settled+Rejected` dep ⇒ `Rejected(DEPENDENCY_REJECTED)`. (Never use ExpectedAggregateVersion/LocalSequence/timestamps for ordering.)
5. authorization: `ICommandAuthorizer` ⇒ `Rejected(PERMISSION_DENIED|PERMISSION_UNKNOWN)`; entitlement
   `IServerEntitlementGate` ⇒ `Rejected(ENTITLEMENT_REQUIRED)`.
6. deserialize `PayloadJson` via `CommandPayloadCatalog` (CommandType → CLR record; unknown ⇒ `Rejected(HANDLER_NOT_REGISTERED)`);
   run **server dispatcher** (`IDispatcher.SendAsync`) inside ONE DB transaction (Transaction behavior) that ALSO writes
   the `CommandIntake` `Settled` update, `CommandIdMap` rows, and the outbox row (no-op `IServerOutbox` seam) ⇒
   **`Settled` ⇔ effect committed**.
7. map `Result` → `SettlementResult`: success ⇒ `Accepted` (+`FinancialEffect` from handler's `IFinancialEffectReporter`
   when present else `None`); `ConcurrencyConflict`/precondition mismatch ⇒ `Conflict`; other failure ⇒ `Rejected` with
   `ErrorCode.Code`, `Detail`, `ResponsibleRole` from `ErrorResponsibility.For(code)` (small static table, default `Manager`).

**`ExpectedServerVersion` precondition.** If present and the command's aggregate resolves (via
`IAggregateVersionReader.TryReadAsync(aggregateType, aggregateId)`) to a current UUID Version ≠ it ⇒
`Conflict(CONCURRENCY_CONFLICT)` **before** the handler runs. Absent ⇒ no precondition. Unknown aggregate type ⇒ skip.
Per-aggregate readers register in S07–S10; this phase ships only the interface.

**Crash recovery (A-RULE 036).** `IntakeRecoveryService.RecoverAsync(row)`: an `InProgress` row with `LeaseUntilUtc < now`
is never blindly retried — acquire a NEW fenced lease (compare-and-set on `AttemptId`), then ask
`IEffectCommittedProbe` (per command type, default `NoProbe`). Since `Settled` is written in the SAME tx as the effect,
`InProgress` ⇒ effect did NOT commit ⇒ safe to re-run (handlers idempotent by `OperationId`) OR mark `Failed` after
**3 expired leases**. The scheduling worker (`BackgroundService`, 30 s cadence, ≤50 rows) is S13 — ship only the
service + unit-tested method here.

**Retention (A-RULE 040).** `CommandIntakeRetentionOptions { FullRecordDays = 120, ReceiptDays = 3650 }`.
Compaction = idempotent SQL `CommandIntakeCompactor.CompactAsync(cutoff)` (`PayloadJson := NULL`, keeps fingerprint+outcome).
Invoked by the S13 worker; never an early purge. Introduce `OfflineWindowOptions.MaxOfflineDays = 90` here (reused S11/S12/S16).

**Rule-set version (A-RULE 037).** `RuleSetPolicy { Current = 1, SupportedFrom = 1 }`. Envelope `RuleSetVersion`
(int string) absent ⇒ treated as Current; `< SupportedFrom` ⇒ `Rejected(RULESET_UNSUPPORTED)`; in range ⇒ recorded in Detail.

**Audit.** Every intake outcome EXCEPT a replay of a stored success writes `SecurityAudit`/`FinancialAudit` via
`ISecurityAuditWriter` (S04). Replay of stored success writes NOTHING.

**New ErrorCodes (+5, new region "Server command intake" in `ErrorCodes.cs` after line 684):**
`CommandInProgress = new("COMMAND_IN_PROGRESS", ErrorCode.Conflict)`,
`DependencyRejected = new("DEPENDENCY_REJECTED", ErrorCode.Conflict)`,
`BatchTooLarge = new("BATCH_TOO_LARGE", ErrorCode.Validation)`,
`ContractVersionUnsupported = new("CONTRACT_VERSION_UNSUPPORTED", ErrorCode.Validation)`,
`RuleSetUnsupported = new("RULESET_UNSUPPORTED", ErrorCode.Validation)`. (Domain `ErrorCodes.cs` is the one Domain edit; additive.)

**D-S06-3 (ids).** Server allocates its OWN ULIDs in domain factories. `SettlementResult.ServerEntityId` = primary id
(slot 0); all others in `CommandIdMap`, returned by `GET …/ids`. Never fabricate an id to match the client.

**D-S06-4 (payload catalog).** `CommandType` does NOT identify one CLR record (4 shared types today:
`sales.invoice.create`, `sales.invoice.line.add`, `sales.service-rate.create`, `visits.visit.start`). So
`CommandPayloadCatalog` maps `CommandType` → ordered list of candidates `(Type CommandRecord, string AggregateType,
IReadOnlySet<string> PropertyNames)`. Resolution: filter by envelope `AggregateType`, then **exact case-insensitive
set equality** between payload JSON top-level property names and the record's ctor-parameter names. Zero matches ⇒
`Rejected(VALIDATION_FAILED, "payload shape")`; two ⇒ impossible by construction (registration-time test + startup
validation throw). Builder: `CommandPayloadCatalogBuilder.Add<TCommand>(string commandType, string aggregateType)`.
Filled by S07–S10 (empty here).

**D-S06-5 (reserved command types).** 5 wire names have NO Application command record yet:
`sales.adjustment.add`, `inventory.receipt.post`, `purchasing.order.submit`, `finance.period.close`,
`finance.cheque.status`. `ReservedCommandTypes` (in `Animora.Server.Commands`, a `FrozenSet<string>`) lists them;
server treats reserved exactly like unknown ⇒ `Rejected(HANDLER_NOT_REGISTERED)`. Coverage tests (S09/S10) assert
`CommandTypes.All == handled ∪ ReservedCommandTypes` and that the set only shrinks — not our tests here.

### Out of scope (do NOT build)
Concrete handlers/ports/aggregate tables/permission rows (S07–S10); journal/outbox domain-effect writes (S11/S13;
`IServerOutbox` no-op seam only); recovery/compaction worker scheduling (S13); client transport/id-remap/reconciliation
/V1→V2 mapping + client population of `ExpectedServerVersion` (G05); entitlement decisions (S16). Nothing "while we're here".

---

## TASK LIST (do top-to-bottom; push after each)

### T1 — Wire v2 + ErrorCodes + Application server profile  ✅
**Edit:**
- `src/Animora.Contracts.Wire/Commands/CommandEnvelope.cs`: add trailing positional params
  `string? ExpectedServerVersion = null`? → records can't default positional except trailing; add them as the LAST two
  positional members `string? ExpectedServerVersion, string? RuleSetVersion` (no default on positional non-trailing-of-record
  is fine since they are the last two). Add matching trailing optional params `expectedServerVersion = null, ruleSetVersion = null`
  to `Create(...)` and pass through. Update XML docs for the two params.
- `src/Animora.Contracts.Wire/WireContract.cs`: `Version = 2`; add
  `public static IReadOnlyList<int> SupportedContractVersions { get; } = [1, 2];` and
  `public static bool IsSupported(int version) => version is 1 or 2;`.
- `src/Animora.Domain/Results/ErrorCodes.cs`: add new `#region Server command intake (Phase S06)` AFTER line 684
  (`#endregion` of C10) with the +5 codes exactly as in §Design decisions.
- `src/Animora.Application/Dispatch/ApplicationPipelineProfile.cs` (NEW): `public enum ApplicationPipelineProfile { Client, Server }`.
- `src/Animora.Application/Dispatch/Dispatcher.cs`: add `public static IReadOnlyList<IPipelineBehavior> ServerChain { get; } =
  [ new CorrelationBehavior(), new ExceptionMappingBehavior(), new ValidationBehavior(), new AuthorizationBehavior(),
  new EntitlementBehavior(), new TransactionBehavior(), new AuditBehavior() ];` — NO `CommandJournalBehavior`. Leave `Chain` as is.
- `src/Animora.Application/ApplicationServiceCollectionExtensions.cs`: add `ApplicationPipelineProfile profile =
  ApplicationPipelineProfile.Client` trailing param; replace `builder.Build(Dispatcher.Chain)` with
  `builder.Build(profile == ApplicationPipelineProfile.Server ? Dispatcher.ServerChain : Dispatcher.Chain)`.
- **Fix wire-version tests:** `grep -rn "WireContract.Version" tests` and any `== 1` / `, 1)` assertion → update to 2 /
  use `IsSupported`. V1 envelopes remain valid.
**Verify:** `scripts/sandbox/build.sh full` = 0/0; `scripts/sandbox/run.sh` = `SMOKE RUN OK`
(desktop untouched; default profile = Client). `grep -n "CommandJournal" .../Dispatcher.cs` shows it only in `Chain`.
**Push:** `scripts/git/push.sh "feat(server): S06 T1 — wire v2, +5 error codes, Application server pipeline profile"`

### T2 — Persistence.PostgreSql entities + store + compactor  ✅
**Create under `src/Animora.Persistence.PostgreSql/Commands/`:**
- `CommandIntakeRecord.cs` (entity, all columns above; `IntakeStatus` enum here or shared — put `IntakeStatus` in this
  project under `Commands/IntakeStatus.cs`). NOT syncable; carries `"Tenant"` filter via config.
- `CommandIdMapEntry.cs` (entity: TenantId, OperationId, Slot, EntityType, ServerEntityId).
- `ICommandIntakeStore.cs` + `CommandIntakeStore.cs`:
  - `TryBeginAsync(...)` = single `INSERT … ON CONFLICT (TenantId, OperationId) DO NOTHING RETURNING` (returns whether
    inserted + the existing row if conflict). Use raw SQL / `FromSql` since EF has no native upsert-returning.
  - `FindAsync(tenantId, operationId, ct)` (`AsNoTracking`).
  - `CompleteAsync(...)` same-transaction `Settled` update + `CommandIdMap` rows (called inside the UoW tx).
  - `RenewLeaseAsync(...)` compare-and-set on `AttemptId` (fenced).
  - `ListExpiredLeasesAsync(limit)` with `limit ≤ 50`.
- `CommandIntakeCompactor.cs` with `CompactAsync(DateTimeOffset cutoff, ct)` idempotent SQL (`PayloadJson := NULL`).
**Create under `Configurations/`:** `CommandIntakeConfiguration.cs`, `CommandIdMapConfiguration.cs` (PKs, indexes, column
types `jsonb`/`uuid`/`char(64)`/`varchar(…)`, the `"Tenant"` named filter on `CommandIntake`). Register both in
`AnimoraServerDbContext` (DbSets + `ApplyConfiguration`).
**Verify:** `scripts/sandbox/build.sh full` = 0/0 (no migration yet; that's T3).
**Push:** `scripts/git/push.sh "feat(server): S06 T2 — CommandIntake/CommandIdMap entities, store, compactor"`

### T3 — Migration 104_CommandIntake + schema head bump  ✅
**Run:** `.sandbox/tools/dotnet-ef migrations add CommandIntake --project src/Animora.Persistence.PostgreSql
--startup-project src/Animora.Persistence.PostgreSql --context AnimoraServerDbContext --output-dir Migrations/Server`.
Then **rename** the generated files' ordinal to `104_CommandIntake` (match the `NNN` token; follow the existing
`20261009######_10N_Name.cs` + `.Designer.cs` naming pattern — keep the timestamp prefix EF generated, just ensure the
logical id is `104_CommandIntake`; verify against the migration `Migration]` attribute name). No `VersionTrigger`
(non-syncable tables). Update `src/Animora.Persistence.PostgreSql/ServerSchemaVersion.cs:10` → `ExpectedHead = "104_CommandIntake"`.
**Verify:** `ls src/Animora.Persistence.PostgreSql/Migrations/Server | grep -c CommandIntake` ≥ 1;
`scripts/sandbox/build.sh full` = 0/0.
**Push:** `scripts/git/push.sh "feat(server): S06 T3 — migration 104_CommandIntake, schema head bump"`

### T4 — Server command plane (intake, catalog, mapper, endpoints)  ✅
**Create under `src/Animora.Server/Commands/`:**
- `CommandPayloadCatalog.cs` (+ `CommandPayloadCatalogBuilder` with `Add<TCommand>(commandType, aggregateType)` reading
  ctor-param names; shape resolution = filter by AggregateType then exact case-insensitive property-name set equality;
  startup validation throws on two candidates sharing type+aggregate+propertyset). Empty catalog here (S07–S10 fill).
- `ReservedCommandTypes.cs` (`FrozenSet<string>` of the 5 names).
- `SettlementMapper.cs` (`Result`/`Result<T>` → `SettlementResult`; outcome/financial-effect/responsible-role mapping).
- `ErrorResponsibility.cs` (static `For(string code)` table → role string, default `"Manager"`).
- `IAggregateVersionReader.cs` (`Task<Result<string?>> TryReadAsync(string aggregateType, string aggregateId, ct)` seam;
  no concrete reader — S07–S10). Default no-op registration returning null (precondition skipped).
- `IEffectCommittedProbe.cs` + `NoProbe` default.
- `IServerOutbox.cs` + no-op default (real = S13).
- `RuleSetPolicy.cs` (`Current=1, SupportedFrom=1`) + `OfflineWindowOptions.cs` (`MaxOfflineDays = 90`) +
  `CommandIntakeRetentionOptions.cs` (`FullRecordDays=120, ReceiptDays=3650`).
- `CommandIntakeService.cs` (scoped; `SubmitAsync(envelope, ct)` implementing the 7-step algorithm; depends on
  `ICommandIntakeStore`, `IDispatcher`, `ICommandAuthorizer`, `IServerEntitlementGate`, `ITenantContext`/
  `RequestTenantContext`, `ISecurityAuditWriter`, `CommandPayloadCatalog`, `IAggregateVersionReader`, `RuleSetPolicy`,
  `TimeProvider`/`IClock`, `CommandFingerprint`). Build `CommandContext` from resolved identity, never the envelope.
- `IntakeRecoveryService.cs` (`RecoverAsync(row)` fenced-lease + probe + 3-expired-leases→Failed; no worker).
- `CommandEndpoints.cs`: map `POST /api/commands`, `POST /api/commands/batch` (max 50 → 413 `BATCH_TOO_LARGE`),
  `GET /api/commands/{operationId}/ids` under `ServerRoutes.Commands` with
  `.RequireRateLimiting(RateLimitPolicies.Commands).RequireAuthorization()`. HTTP-200-for-settled; 426 + header
  `X-Animora-Supported-Versions: 1,2` for unsupported version; `WireApiError` for transport/shape/auth failures.
  Add `CommandIdMapResponse` DTO.
- `AddAnimoraCommandPlane()` DI extension: calls `AddAnimoraApplication(configure: _ => {},
  ApplicationPipelineProfile.Server)`; `TryAdd`-registers the store/service/catalog/seams; register `ServerUnitOfWork`
  as `IUnitOfWork` for the server scope if not already. Wire into `Program.cs` startup + map endpoints.
**Constraint checks:** `grep -rn "ExpectedAggregateVersion" src/Animora.Server` must return NOTHING (never used as
server concurrency evidence). `Animora.Server` adds no forbidden project ref.
**Verify:** `scripts/sandbox/build.sh full` = 0/0; `scripts/sandbox/run.sh` = `SMOKE RUN OK`.
**Push:** `scripts/git/push.sh "feat(server): S06 T4 — command intake service, payload catalog, endpoints, seams"`
> If T4 grows large or needs a heavy fix mid-way, push a sensible intermediate commit (clear message) rather than batching.

### T5 — Tests (pure + PG-gated)  ✅
**Create `tests/Animora.Server.Tests/Commands/*.cs`** (pure, fake `ICommandIntakeStore` + fake `IDispatcher`):
`CommandIntakeServiceTests`: `IdenticalRetry_ReturnsStoredResult_DoesNotReexecute`,
`SameOperationId_DifferentFingerprint_Rejected_Audited`, `TenantMismatch_Rejected_Audited`,
`DependencyUnsettled_Deferred`, `DependencyRejected_Rejected`, `ExpectedServerVersionMismatch_Conflict`,
`UnknownCommandType_Rejected_HandlerNotRegistered`, `UnsupportedContractVersion_426`,
`PermissionDenied_Rejected_BeforeHandler`, `SettledOutcomes_Return_Http200`.
`BatchTests`: `Over50_Returns_BatchTooLarge`, `ResultsInRequestOrder_NeverReordered`.
`IntakeRecoveryServiceTests`: `ExpiredLease_NotBlindlyRetried_FencedByAttemptId`, `ThreeExpiredLeases_MarksFailed`.
`RuleSetPolicyTests`: `BelowSupportedFrom_Rejected`.
`CommandPayloadCatalogTests`: `EveryEntry_IsJournaledCommand_WithMatchingTypeName`, `ResolvesSharedCommandTypes_ByShape`,
`NoTwoCandidates_ShareCommandTypeAggregateAndPropertySet`.
`ReservedCommandTypesTests`: `Reserved_AreRejected_HandlerNotRegistered`.
**Create `tests/Animora.Application.Tests/…ServerChainTests.cs`**:
`ServerChain_HasNoCommandJournal_ClientChainUnchanged`.
**Create PG-gated `CommandIntakeStoreTests`** (skip when `ANIMORA_PG_TEST_CONNECTION` unset):
`OnConflictDoNothing_ConcurrentSameKey_SingleWinner`, `Settled_IffEffectCommitted_RollbackLeavesInProgress`,
`Compaction_KeepsFingerprintAndOutcome`.
**Verify (targeted, NOT full suite):** `scripts/sandbox/build.sh full` = 0/0;
`scripts/sandbox/test.sh Server` green; `scripts/sandbox/test.sh Application` green; `scripts/sandbox/run.sh` = `SMOKE RUN OK`.
**Push:** `scripts/git/push.sh "feat(server): S06 T5 — intake/batch/recovery/catalog/server-chain tests"`

### T6 — Acceptance gate sweep  ✅
Run each mechanical check from the phase (fix minimally if any fail, then re-push):
- `grep -rn "Dispatcher.Chain" src/Animora.Application` still lists the unchanged client chain.
- `grep -n "CommandJournal" src/Animora.Application/Dispatch/Dispatcher.cs` only in `Chain`, not `ServerChain`.
- `grep -n "Version = 2" src/Animora.Contracts.Wire/WireContract.cs` matches.
- `grep -rn "WireContract.Version" tests | grep "== 1\|, 1)"` returns NOTHING.
- `ls src/Animora.Persistence.PostgreSql/Migrations/Server | grep -c CommandIntake` ≥ 1.
- `grep -rn "ExpectedAggregateVersion" src/Animora.Server` returns NOTHING.
**Push (if any fix):** `scripts/git/push.sh "fix(server): S06 T6 — acceptance gate fixes"` (skip push if nothing changed).

### T7 — PROGRESS.md update  ✅
Edit `backend-roadmap/PROGRESS.md`:
- "Phase status" table row `S06` → `✅ Done` + date (today) + one-line summary.
- "Next phase" → `S07` (update line 144 and any status pointer; S07 file =
  `phases/PHASE-S07-server-handlers-clinical.md`).
- Append ONE `### S06` entry to "Repo state log" (what exists now: wire v2, 5 new error codes, migration ordinal
  `104_CommandIntake`, tables `CommandIntake`/`CommandIdMap`, new seams/services/endpoints, test classes). Also update the
  `src/Animora.Server/` + `src/Animora.Persistence.PostgreSql/` current-state rows to mention S06.
- Log any deviation in "Decision & deviation log".
**Push:** `scripts/git/push.sh "docs(server): S06 done — PROGRESS.md, next S07"`

### T8 — Delete the source phase file (FINAL)  ⬜
Delete ONLY `backend-roadmap/phases/PHASE-S06-command-intake-idempotency.md`. Do NOT touch any other roadmap/phase file.
Then delete THIS plan file (`Plan/S06-command-intake-idempotency.plan.md`)? → **No**: the user requirement is to delete
the roadmap phase file as the closing task; this plan file lives under `Plan/` and is removed only if the user's
"آخرین مرحله حذف این فایل" refers to the plan. **Interpretation lock:** the final todo is to delete the **phase roadmap
file** (`backend-roadmap/phases/PHASE-S06…`). Leave the `Plan/` file in place unless separately told; if ambiguity, the
instruction "فقط فایل داخل plan حذف بشه" ⇒ delete the Plan file too at the very end. So: delete the phase file first,
then delete this Plan file as the absolute last action.
**Push:** `scripts/git/push.sh "chore(server): S06 complete — remove S06 phase file and plan"`

---

## Progress log (update as you go)
- T1 ✅  T2 ✅  T3 ✅  T4 ✅  T5 ✅  T6 ✅  T7 ✅  T8 ⬜

- T4 implemented: fenced intake/recovery, catalog, settlement mapping, server pipeline adapters, atomic receipt/effect/id-map/outbox, endpoints and DI. Build/test/smoke deliberately NOT executed per user instruction. Intermediate T4 pushes followed by task completion push.
- T5 implemented: pure intake/batch/recovery/rules/catalog/reserved/server-chain cases and PostgreSQL-gated concurrent winner, atomic rollback, fencing and compaction cases. Test sources written only; no test, build, smoke or database migration executed.
- T6 verified by grep gates only (no build/test): chain unchanged, ServerChain has no CommandJournal, Version = 2, no stale wire-version test asserts, migration present, no ExpectedAggregateVersion in Animora.Server.
