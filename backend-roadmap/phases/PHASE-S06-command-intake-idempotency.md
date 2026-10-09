# Phase S06 — Business API — command intake, idempotency & server settlement

**Band:** M · **Track:** S (server) · **Depends on:** S05 · **Next:** S07
**Satisfies:** R-B13, R-B14, R-B15, R-B29 · **Runnable after this phase:** desktop offline yes · server required no
**Produced by:** M00 (regenerated 2026-10-08; regenerates scope-inventory row D05 (Track-D/E planning files deleted in the 2026-10-08 server-track reset)).

> Baseline verified 2026-10-08: client migration head `031` (`031_TreatmentPlans`), `CommandTypes` = live `CommandTypes.cs` (88 declared today — never freeze), `ErrorCodes` 161, `WireContract.Version` = 1; `src/` projects = `Animora.Domain`, `Animora.Application`, `Animora.Persistence.Sqlite`, `Animora.Infrastructure.Local`, `Animora.Client.Services`, `Animora.Contracts`, `Animora.Contracts.Wire`, `Animora.Desktop`, `Animora.Sync.Abstractions` (+ server-track projects added by earlier S/G phases).
> **Next-free-ordinal rule.** Never hard-code a migration number from this file. List the physical files of the owning `DbContext`'s `Migrations/` folder and take the next free ordinal (server sequence starts at **101**; the client SQLite sequence — head `031` today — is independent and untouched by server phases). Match on the `NNN` token, not a file-name prefix.

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
- `dotnet-ef` lives at `.sandbox/tools/dotnet-ef` — use it; **never** `dotnet tool install`.
- Server stack is locked (`STACK.md` §0/§5/§6/§11): ASP.NET Core 10 + Kestrel, EF Core **10.0.12** line,
  PostgreSQL 18 via `Npgsql.EntityFrameworkCore.PostgreSQL` **10.0.3** / `Npgsql` **10.0.3** (only in `Animora.Persistence.PostgreSql`).
- PostgreSQL integration tests read the connection string from env var `ANIMORA_PG_TEST_CONNECTION`; when unset they
  **skip with a reason** locally (CONVENTIONS §1) and **fail** under `ANIMORA_REQUIRE_PG=1` (mandatory CI/release gate, owned by G07).
- **Forbidden tech (restated, binding):** Redis, message broker, cloud IdP, cloud telemetry, CDN, EF Core 11,
  per-tenant database, hard-coded TLS version (`SslProtocols.None` only), Native AOT with EF Core, `UpdatedAt`/`nextval`/`xmin` sync cursors.

**Offline invariant.** The desktop must keep starting and operating fully offline with no server, no PostgreSQL and no network (Standing principle 1, CONVENTIONS §11). This phase adds nothing the desktop needs at startup.

---

## Read before starting
- `backend-roadmap/PROGRESS.md` — top section only.
- `ARCHITECTURE.md` §5, §8.3, §8.4, §8.6, §15.1, §PG.7 — only these `§`.
- `src/Animora.Application/Dispatch/ApplicationRegistrationBuilder.cs` (the `Build` visibility) and `ApplicationServiceCollectionExtensions.cs` (edited here).
- `src/Animora.Contracts.Wire/Commands/CommandEnvelope.cs`, `WireContract.cs` (edited here).

(Nothing else. `Animora-UI/**`, `Database-Sampel/**`, other phase files and `windows-frontend-roadmap/phases/**` are out of bounds.)

## Goal

Build the durable, idempotent **command intake** for `POST /api/commands` and `POST /api/commands/batch`: the `(TenantId, OperationId)` idempotency store written in the same transaction as the effect, fingerprint-mismatch integrity audit, fenced-lease crash recovery, server-side dependency resolution (`DependsOnOperationId`) and the optimistic `ExpectedServerVersion` precondition, rule-set/contract-version gating, HTTP-200-for-settled-outcomes, the server dispatcher profile (Application handlers reused, client journal omitted) and the additive wire change (`WireContract.Version` → 2). It ships **no domain handler** — S07–S10 register those; with none registered every command settles `Rejected(HANDLER_NOT_REGISTERED)` deterministically.

---

## §Reference pack

```csharp
namespace Animora.Contracts.Wire.Commands;   // (A01/B02) — reuse verbatim
public sealed record CommandEnvelope(int ContractVersion, string OperationId, string TenantId, string UserId, string DeviceId,
    string CommandType, string AggregateType, string? AggregateId, string PayloadJson, string CommandFingerprint,
    DateTimeOffset CreatedAtUtc, long LocalSequence, string? CausationId, string? DependsOnOperationId, long? ExpectedAggregateVersion);
public sealed record SettlementResult(string OperationId, SettlementOutcome Outcome, string? ErrorCode, string? Detail,
    string? ServerEntityId, string? ServerVersion, DateTimeOffset SettledAtUtc, FinancialEffect FinancialEffect, string? ResponsibleRole);
public enum SettlementOutcome { Accepted, Rejected, Conflict, Deferred }
public enum FinancialEffect  { None, Applied, NotApplied, Unknown }
public static class CommandTypes { /* public const string X = "ctx.aggregate.verb"; ... */ public static IReadOnlyList<string> All { get; } }
```

```csharp
namespace Animora.Application.Abstractions;   // (B01) — the handler seam a server handler implements
public interface ICommand<TResult>;
public interface ICommandHandler<in TCommand, TResult> where TCommand : ICommand<TResult>
{ Task<Result<TResult>> HandleAsync(TCommand command, CommandContext context, CancellationToken ct); }
public sealed record CommandContext(string TenantId, string UserId, string DeviceId, string OperationId, string CorrelationId,
    string? CausationId, string? DependsOnOperationId, long? ExpectedAggregateVersion, DateTimeOffset CreatedAtUtc);
public interface IUnitOfWork { Task<Result> SaveAsync(CancellationToken ct = default);
    Task<Result<T>> ExecuteAsync<T>(Func<CancellationToken, Task<Result<T>>> work, CancellationToken ct = default); }
public interface IDispatcher { Task<Result<TResult>> SendAsync<TResult>(ICommand<TResult> command, CommandContext ctx, CancellationToken ct = default);
                               Task<Result<TResult>> QueryAsync<TResult>(IQuery<TResult> query, QueryContext ctx, CancellationToken ct = default); }
namespace Animora.Application.Dispatch;
public sealed class ApplicationRegistrationBuilder {   // used inside AddAnimoraApplication(configure)
    ApplicationRegistrationBuilder AddCommand<TCommand,TResult,THandler>(); AddQuery<TQuery,TResult,THandler>(); AddValidator<T,TValidator>();
    RequireCommand<TCommand,TResult>(); RequireQuery<TQuery,TResult>(); }
public static class Dispatcher.Chain => [Correlation, ExceptionMapping, Validation, Authorization, Entitlement, Transaction, CommandJournal, Audit];
//   TransactionBehavior resolves IUnitOfWork from DI and wraps commands (skips ISelfTransactedCommand and queries).
//   CommandJournalBehavior runs only for IJournaledCommand and resolves ICommandJournal + ILocalSequenceAllocator (CLIENT-side, SQLite).
```
```csharp
// Animora.Domain.Results (A02) — reuse, never redefine
public readonly record struct ErrorCode(string Code, string Category)   // categories: Validation|NotFound|Conflict|Forbidden|Integrity|Infrastructure|Entitlement
{ public const string Validation="Validation", NotFound="NotFound", Conflict="Conflict", Forbidden="Forbidden", Integrity="Integrity", Infrastructure="Infrastructure", Entitlement="Entitlement";
  public static readonly ErrorCode None; public bool IsNone { get; } }
public readonly record struct Result      { bool IsSuccess, IsFailure; ErrorCode Error; string? Detail; static Result Ok(); static Result Fail(ErrorCode e, string? detail = null); }
public readonly record struct Result<T>   { bool IsSuccess, IsFailure; T? Value; ErrorCode Error; string? Detail; static Result<T> Ok(T v); static Result<T> Fail(ErrorCode e, string? detail = null); Map/Bind/OrElse; implicit operator Result<T>(T) }
// ErrorCodes (161 today): `public static readonly ErrorCode X = new("X_CODE", ErrorCode.<Kind>);` — inside `#region <context>`. Server phases ADD a new #region; never rename/remove.
// Already declared & reusable: ValidationFailed, NotFound, ConcurrencyConflict("CONCURRENCY_CONFLICT",Conflict), PermissionDenied, PermissionUnknown, TenantMismatch("TENANT_MISMATCH",Forbidden),
//   IdempotencyFingerprintMismatch(Integrity), DependencyUnsettled("DEPENDENCY_UNSETTLED",Conflict), EntitlementRequired(Entitlement), TenantSuspended, UserDisabled, UserLockedOut, DeviceRevoked,
//   CredentialsInvalid, SessionExpired, PasswordPolicyViolation, SettlementInvalidTransition, CommandAlreadySettled, HandlerNotRegistered, DispatchFailed, Unexpected
```
```csharp
// CommandFingerprint (B02) — server RE-COMPUTES and compares; never trusts the client's value
public static class CommandFingerprint { public const char Separator = '\u001f'; public static string Compute(string commandType, string payloadJson); public static string CanonicalJson<T>(T payload); }
// Application: ApplicationRegistrationBuilder.Build(IReadOnlyList<IPipelineBehavior> chain) is `internal`; AddAnimoraApplication(IServiceCollection, Action<ApplicationRegistrationBuilder>? configure = null)
//   currently calls `builder.Build(Dispatcher.Chain)` where Dispatcher.Chain = [Correlation, ExceptionMapping, Validation, Authorization, Entitlement, Transaction, CommandJournal, Audit].
//   CommandJournalBehavior is client-only (needs ICommandJournal + ILocalSequenceAllocator). THIS PHASE adds the minimal Application change below.
// Existing codes reusable: IdempotencyFingerprintMismatch(Integrity) "IDEMPOTENCY_FINGERPRINT_MISMATCH", DependencyUnsettled(Conflict) "DEPENDENCY_UNSETTLED", ConcurrencyConflict, TenantMismatch,
//   HandlerNotRegistered, DispatchFailed, CommandAlreadySettled, EntitlementRequired, PermissionDenied/Unknown.
```

## §Design decisions

- **D-S06-1 (handler reuse).** The server executes the **same Application command handlers** through a *server pipeline profile*. Minimal Application change (the **only** `Animora.Application` edit in the server track besides additive ports): `ApplicationPipelineProfile { Client, Server }` and `AddAnimoraApplication(this IServiceCollection, Action<ApplicationRegistrationBuilder>? configure = null, ApplicationPipelineProfile profile = ApplicationPipelineProfile.Client)`. `Dispatcher.Chain` stays the client chain (unchanged behavior, desktop untouched); new `Dispatcher.ServerChain = [Correlation, ExceptionMapping, Validation, Authorization, Entitlement, Transaction, Audit]` (no `CommandJournal`). Registry validation, handler registrations and DI are shared. Existing tests of the client chain keep passing (profile default = Client).
- **Wire v2 (additive, ARCH §PG.7/§8.6):** `CommandEnvelope` gains a trailing optional `string? ExpectedServerVersion = null` (canonical lowercase UUID the client last observed for the target aggregate) and `string? RuleSetVersion = null`; `CommandEnvelope.Create(...)` gains matching optional parameters **at the end**; `WireContract.Version` becomes **2**; `SupportedContractVersions = {1,2}` (N and N-1) in new `WireContract.IsSupported(int)`. `ExpectedAggregateVersion` (numeric, v1) stays but is **ignored by the server as concurrency evidence** (local bookkeeping only). `SettlementResult.ServerVersion` (string) carries the aggregate's **applied** canonical UUID on `Accepted`. Edit any existing test asserting `WireContract.Version == 1` (search `WireContract.Version` in `tests/**`) to the new value; V1 envelopes remain valid.
- **Endpoints (`/api/commands`, rate-limit policy `commands`, `[Authorize]` + tenant context from S05):** `POST /api/commands` body `CommandEnvelope` → **always HTTP 200 + `SettlementResult`** for a conversation that reached the intake (Accepted/Rejected/Conflict/Deferred); 4xx/5xx + `WireApiError` only for transport/auth/shape failures: `400 VALIDATION_FAILED` (malformed envelope), `401/403` (S05), `409`-free (conflicts are settlement), `426 CONTRACT_VERSION_UNSUPPORTED` (version ∉ {1,2}, carries `X-Animora-Supported-Versions: 1,2`), `413 BATCH_TOO_LARGE` (>50). `POST /api/commands/batch` body `CommandEnvelope[]` (max **50**, `BATCH_TOO_LARGE`) → `SettlementResult[]` **in request order**; each settles independently (not one transaction), never reordered.
- **Server resolves identity, never trusts the envelope:** `CommandContext` is built from the authenticated principal + S05 tenant context (`TenantId`, `UserId`, `DeviceId`); an envelope `TenantId`/`UserId`/`DeviceId` that differs → `Rejected(TENANT_MISMATCH)` (+ `SecurityAudit` row `command.tenant_mismatch`); the server recomputes `CommandFingerprint(commandType, payloadJson)` and a mismatch with the envelope's → `Rejected(IDEMPOTENCY_FINGERPRINT_MISMATCH)` + integrity audit.
- **Idempotency store (table `CommandIntake`, tenant-scoped, composite PK `(TenantId, OperationId)`):** columns `CommandType varchar(64)`, `Fingerprint char(64)`, `Status smallint` (`IntakeStatus { Received=0, InProgress=1, Settled=2, Failed=3 }`), `Outcome smallint?` (`SettlementOutcome`), `ErrorCode varchar(64)?`, `Detail text?` (≤512), `ServerEntityId varchar(26)?`, `ServerVersion uuid?`, `FinancialEffect smallint`, `ResponsibleRole varchar(32)?`, `ReceivedUtc`, `SettledUtc?`, **fenced lease** `AttemptId varchar(26)?`, `LeaseOwner varchar(64)?`, `LeaseUntilUtc?`, `PayloadJson jsonb` (nulled → archival receipt after retention, see below), `DependsOnOperationId varchar(26)?`, `CorrelationId`. Indexes: PK; `IX_CommandIntake_TenantId_Status_LeaseUntilUtc` (recovery scan); `IX_CommandIntake_TenantId_DependsOnOperationId`. **Not** a `SyncableEntity` (no Version/Deleted) but carries the `"Tenant"` filter. Secondary table `CommandIdMap(TenantId, OperationId, Slot smallint, EntityType varchar(32), ServerEntityId varchar(26))` PK `(TenantId, OperationId, Slot)` records **every** aggregate id a command created (primary = slot 0) — see D-S06-3.
- **Intake algorithm (one scoped `CommandIntakeService.SubmitAsync(envelope, ct)`; each step cheap, in order):** (1) version gate; (2) identity/fingerprint checks above; (3) `INSERT … ON CONFLICT (TenantId, OperationId) DO NOTHING RETURNING` into `CommandIntake` with `Status=InProgress`, new `AttemptId`, `LeaseUntil = now+60s`; if the row **already existed**: `Settled` ⇒ return the **stored** `SettlementResult` (same fingerprint) — never re-execute; stored fingerprint ≠ incoming ⇒ `Rejected` integrity + audit; `InProgress` with unexpired lease ⇒ `Deferred(COMMAND_IN_PROGRESS)` (client retries); `InProgress` with expired lease ⇒ **recovery** (below); `Failed` ⇒ treat as new attempt (re-lease, re-run); (4) dependency: `DependsOnOperationId` present ⇒ must be `Settled+Accepted` in `CommandIntake` for the tenant else `Deferred(DEPENDENCY_UNSETTLED)` (stored `Settled+Rejected` dependency ⇒ this command `Rejected(DEPENDENCY_REJECTED)`); `ExpectedAggregateVersion`/`LocalSequence`/timestamps are never used for ordering; (5) authorization: `ICommandAuthorizer` (S05) ⇒ `Rejected(PERMISSION_DENIED|PERMISSION_UNKNOWN)`; entitlement via `IServerEntitlementGate` ⇒ `Rejected(ENTITLEMENT_REQUIRED)`; (6) deserialize `PayloadJson` through `CommandPayloadCatalog` (CommandType → CLR command record; unknown type ⇒ `Rejected(HANDLER_NOT_REGISTERED)`), run the **server dispatcher** (`IDispatcher.SendAsync`) inside **one DB transaction** (Transaction behavior) that also writes the `CommandIntake` `Settled` update, `CommandIdMap` rows and the outbox row (S13 seam `IServerOutbox`, a no-op registration here) — so **`Settled` ⇔ the effect committed**; (7) map `Result` → `SettlementResult`: success ⇒ `Accepted` (+`FinancialEffect` from the handler's `IFinancialEffectReporter` when present else `None`); `ConcurrencyConflict`/precondition mismatch ⇒ `Conflict`; any other failure ⇒ `Rejected` with `ErrorCode.Code`, `Detail`, `ResponsibleRole` from `ErrorResponsibility.For(code)` (small static table, default `Manager`).
- **`ExpectedServerVersion` precondition:** if present and the command's `AggregateIdOrNull` resolves to an aggregate whose current UUID `Version` ≠ it ⇒ `Conflict(CONCURRENCY_CONFLICT)` before the handler runs (domain policy of S07–S10 may still resolve additive cases). Absent ⇒ no precondition. Resolution is by `IAggregateVersionReader.TryReadAsync(aggregateType, aggregateId)` (interface here, per-aggregate readers registered by S07–S10; unknown aggregate type ⇒ precondition skipped).
- **Crash recovery (A-RULE 036):** an `InProgress` row with `LeaseUntilUtc < now` is **never blindly retried** and never stuck: `IntakeRecoveryService.RecoverAsync(row)` acquires a new fenced lease (compare-and-set on `AttemptId`), then asks `IEffectCommittedProbe` (per command type, registered by S07–S10; default `NoProbe`) — since `Settled` is written in the same transaction as the effect, an `InProgress` row means **the effect did not commit**: it is safely re-run (handlers are idempotent by `OperationId` for their effect) or marked `Failed` after 3 expired leases. A `IntakeRecoveryWorker` (`BackgroundService`, one pass per 30 s, bounded to 50 rows) lands in S13; this phase ships the service + a unit-tested method.
- **Retention (A-RULE 040):** `CommandIntakeRetentionOptions { FullRecordDays = 120, ReceiptDays = 3650 }` (≥ the longest offline window; the window constant `OfflineWindowOptions.MaxOfflineDays = 90` is introduced here and reused by S11/S12/S16). Compaction (`PayloadJson := NULL`, keeps fingerprint + outcome) is an idempotent SQL method `CommandIntakeCompactor.CompactAsync(cutoff)` invoked by the S13 worker; **never an early purge**.
- **Rule-set version (A-RULE 037):** envelopes for financial command types may carry `RuleSetVersion` (int string). `RuleSetPolicy { Current = 1, SupportedFrom = 1 }`: absent ⇒ treated as `Current`; `< SupportedFrom` ⇒ `Rejected(RULESET_UNSUPPORTED)`; between ⇒ server re-derives (handlers always do, §5) — recorded in `Detail`.
- **Audit:** every intake outcome other than a replay of a stored success writes `SecurityAudit`/`FinancialAudit` rows through `ISecurityAuditWriter` (S04) — tenant mismatch, fingerprint mismatch, rejected-by-permission, entitlement, unsupported version. Replay of stored success writes nothing.
- **New ErrorCodes (declared in `ErrorCodes`, region "Server command intake", +5):** `CommandInProgress` = `new("COMMAND_IN_PROGRESS", ErrorCode.Conflict)`, `DependencyRejected` = `new("DEPENDENCY_REJECTED", ErrorCode.Conflict)`, `BatchTooLarge` = `new("BATCH_TOO_LARGE", ErrorCode.Validation)`, `ContractVersionUnsupported` = `new("CONTRACT_VERSION_UNSUPPORTED", ErrorCode.Validation)`, `RuleSetUnsupported` = `new("RULESET_UNSUPPORTED", ErrorCode.Validation)`. (Domain `ErrorCodes.cs` is the one Domain edit; additive.)
- **D-S06-3 (ids).** The server **allocates its own ULIDs** inside the domain factories (client and server mint different ids for the same command). `SettlementResult.ServerEntityId` = primary created id (slot 0); every other created id is in `CommandIdMap`, returned by `GET /api/commands/{operationId}/ids` → `CommandIdMapResponse(IReadOnlyList<(slot, entityType, serverEntityId)>)`. The client-side id remap (provisional local id → server id, including pending dependent payloads) is **G05**; the state feed (S11/S12) delivers the server rows. Never fabricate an id to match the client.
- **D-S06-4 (payload catalog — verified against the tree 2026-10-08).** The wire `CommandType` does **not** identify one CLR record: 4 command types are shared by several Application command records today (`sales.invoice.create` ← `CreateBlankInvoiceCommand`/`CreateInvoiceFromStayCommand`/`CreateInvoiceFromVisitCommand`; `sales.invoice.line.add` ← `AddInvoiceLineCommand`/`AddCustomInvoiceLineCommand`; `sales.service-rate.create` ← `CreateServiceRateCommand` (aggregate `Product`)/`CreateBoardingServiceRateCommand` (aggregate `BoardingServiceDef`); `visits.visit.start` ← `StartVisitFromTicketCommand`/`StartNewVisitCommand`). Therefore `CommandPayloadCatalog` maps `CommandType` → an **ordered list of candidates** `(Type CommandRecord, string AggregateType, IReadOnlySet<string> PropertyNames)`; resolution = filter by envelope `AggregateType`, then by **exact (case-insensitive) set equality** between the payload JSON's top-level property names and the record's constructor-parameter names; zero matches ⇒ `Rejected(VALIDATION_FAILED, "payload shape")`; two matches ⇒ impossible by construction — `CommandPayloadCatalogTests.NoTwoCandidates_ShareCommandTypeAggregateAndPropertySet` fails the build at registration time (startup validation throws too). Filled by S07–S10 via `CommandPayloadCatalogBuilder.Add<TCommand>(string commandType)` (reads `AggregateTypeName` from a `FormatterServices`-free path: the builder takes the aggregate name explicitly — `Add<TCommand>(commandType, aggregateType)`). Tests: `CommandPayloadCatalogTests.EveryEntry_IsJournaledCommand_WithMatchingTypeName`, `…ResolvesSharedCommandTypes_ByShape` and (S10 flips mandatory) `…CoversEveryCommandTypesAll_ExceptDeclaredReserved`.
- **D-S06-5 (reserved command types).** 5 constants in `CommandTypes.All` have **no Application command record yet** (`sales.adjustment.add`, `inventory.receipt.post`, `purchasing.order.submit`, `finance.period.close`, `finance.cheque.status` — they are wire names reserved for flows currently expressed through other commands/events). The server treats a reserved type exactly like an unknown one: `Rejected(HANDLER_NOT_REGISTERED)` (never silently accepted). The allow-list `ReservedCommandTypes` (in `Animora.Server.Commands`, a `FrozenSet<string>`) lists them; coverage tests in S09/S10 assert `CommandTypes.All == handled ∪ ReservedCommandTypes` and that `ReservedCommandTypes` only **shrinks** (a type present in both sets fails the test). Adding the missing Application command for a reserved type is a *client* feature outside this track (record in the drift ledger).

## §Implementation plan

1. Edit `Animora.Contracts.Wire` (envelope + `WireContract`), `Animora.Domain/Results/ErrorCodes.cs` (+5 codes in a new `#region Server command intake`), `Animora.Application` (`ApplicationPipelineProfile`, `Dispatcher.ServerChain`, the `AddAnimoraApplication` overload). Fix tests asserting the wire version. `scripts/sandbox/build.sh full` must still be 0/0 **before** continuing (desktop untouched check: `scripts/sandbox/run.sh` → `SMOKE RUN OK`).
2. Persistence.PostgreSql: entities `CommandIntakeRecord`, `CommandIdMapEntry` + configurations; `ICommandIntakeStore` + `CommandIntakeStore` (`TryBeginAsync` = single `INSERT … ON CONFLICT DO NOTHING RETURNING`, `FindAsync`, `CompleteAsync` (same-transaction update), `RenewLeaseAsync`, `ListExpiredLeasesAsync(limit ≤ 50)`), `CommandIntakeCompactor`.
3. Migration: `.sandbox/tools/dotnet-ef migrations add CommandIntake --project src/Animora.Persistence.PostgreSql --startup-project src/Animora.Persistence.PostgreSql --context AnimoraServerDbContext --output-dir Migrations/Server`, rename to the **next free server ordinal** (list the folder; expected `104_CommandIntake`), update `ServerSchemaVersion.ExpectedHead`. No `VersionTrigger` (non-syncable tables).
4. Server: `Commands/CommandIntakeService.cs`, `IntakeRecoveryService.cs`, `CommandPayloadCatalog.cs` (+builder, shape resolution), `ReservedCommandTypes.cs`, `SettlementMapper.cs`, `ErrorResponsibility.cs`, `IAggregateVersionReader.cs`, `IEffectCommittedProbe.cs`, `RuleSetPolicy.cs`, `CommandEndpoints.cs`, `AddAnimoraCommandPlane()` (calls `AddAnimoraApplication(configure: _ => {}, ApplicationPipelineProfile.Server)`), map under `ServerRoutes.Commands` with `RequireRateLimiting(RateLimitPolicies.Commands)` + `RequireAuthorization()`.
5. Tests (below). `scripts/sandbox/build.sh full` → 0/0; `scripts/sandbox/test.sh Server` and `scripts/sandbox/test.sh Application` → green; `scripts/sandbox/run.sh` → `SMOKE RUN OK`.
6. `PROGRESS.md` per recipe (record: wire v2, 5 new error codes, migration ordinal, intake table names); commit.

## Files to create / modify

| Path | Contents |
|---|---|
| `src/Animora.Contracts.Wire/Commands/CommandEnvelope.cs`, `WireContract.cs` (edit) | + `ExpectedServerVersion`, `RuleSetVersion`; Version=2; `IsSupported` |
| `src/Animora.Domain/Results/ErrorCodes.cs` (edit) | +5 codes |
| `src/Animora.Application/Dispatch/ApplicationPipelineProfile.cs`, `Dispatcher.cs`, `ApplicationServiceCollectionExtensions.cs` (edit) | server chain profile |
| `src/Animora.Persistence.PostgreSql/Commands/*.cs`, `Configurations/CommandIntake*.cs`, `Migrations/Server/*104_CommandIntake*` | idempotency store + migration |
| `src/Animora.Server/Commands/*.cs` | intake, recovery, catalog, endpoints, mapper |
| `tests/Animora.Server.Tests/Commands/*.cs`, `tests/Animora.Application.Tests/…ServerChainTests.cs` | tests |

## Contracts / schema added

Tables `CommandIntake`, `CommandIdMap`. Wire v2 fields `ExpectedServerVersion`, `RuleSetVersion`. Endpoints `POST /api/commands`, `POST /api/commands/batch`, `GET /api/commands/{operationId}/ids`. Error codes +5. Seams: `CommandIntakeService`, `ICommandIntakeStore`, `CommandPayloadCatalog(+Builder)`, `ReservedCommandTypes`, `IAggregateVersionReader`, `IEffectCommittedProbe`, `ApplicationPipelineProfile`, `OfflineWindowOptions.MaxOfflineDays=90`.

## Rules that apply here

- **Layering (CONVENTIONS §2, `ARCHITECTURE.md` §6):** `Animora.Domain` references nothing; `Animora.Application` → Domain + Contracts + Contracts.Wire;
  only `Animora.Persistence.PostgreSql` may reference Npgsql/`UseNpgsql`; `Animora.Server` must never reference `Animora.Client.Services`,
  `Animora.Persistence.Sqlite`, `Animora.Infrastructure.Local`, `Animora.Sync.Custom` or `Animora.Desktop`; the client never references a server project.
- **C# style (§4):** `sealed` by default; `sealed record` DTOs; nullable honored (no `!` without a why-comment); every async public method takes
  `CancellationToken ct = default` and `ConfigureAwait(false)` in library projects; no `.Result`/`.Wait()`; expected failures return `Result`/`Result<T>`
  with an `ErrorCode`, never exceptions; `IClock`/`TimeProvider` injected (no `DateTime.Now`/`UtcNow`); ids are ULID strings via `IdGenerator`, never `Guid` in business code.
- **EF (§5):** EF Core 10 only; no lazy loading/proxies; reads `AsNoTracking()` + projections; named filters `"Tenant"` and `"SoftDelete"` kept separable;
  paging mandatory (max page 200); migrations additive and never edited later; `EnsureCreated` forbidden outside test fixtures.
- **Security (§7):** Authn → Authz → Entitlement in that order, never merged; `TenantId` resolved server-side from membership only; secrets never in config
  files/logs; defaults fail closed; no custom crypto; append-only audit for every security/financial action.

## Out of scope (owning phase)

- Concrete handlers, ports, aggregate tables, permission rows → **S07–S10**. Journal/outbox writes of domain effects → **S11/S13** (`IServerOutbox` is a no-op seam here).
- Recovery worker scheduling + compaction scheduling → **S13**.
- Client transport, id remap, reconciliation, V1-journal→V2 mapping → **G05**. Client envelope population of `ExpectedServerVersion` → **G05**.
- Entitlement decisions → **S16**.

## Acceptance criteria (mechanical)

1. `scripts/sandbox/build.sh full` = 0/0; `scripts/sandbox/test.sh Application` and `Server` green; `scripts/sandbox/run.sh` = `SMOKE RUN OK`. `grep -rn "Dispatcher.Chain" src/Animora.Application` still lists the unchanged client chain; `grep -n "CommandJournal" src/Animora.Application/Dispatch/Dispatcher.cs` shows it only in `Chain`, not in `ServerChain`.
2. `grep -n "Version = 2" src/Animora.Contracts.Wire/WireContract.cs` matches; `grep -rn "WireContract.Version" tests | grep "== 1\|, 1)"` returns nothing.
3. `ls src/Animora.Persistence.PostgreSql/Migrations/Server | grep -c CommandIntake` ≥ 1 (ordinal = next free).
4. Pure tests pass (fake `ICommandIntakeStore` + fake dispatcher): `CommandIntakeServiceTests.IdenticalRetry_ReturnsStoredResult_DoesNotReexecute`, `…SameOperationId_DifferentFingerprint_Rejected_Audited`, `…TenantMismatch_Rejected_Audited`, `…DependencyUnsettled_Deferred`, `…DependencyRejected_Rejected`, `…ExpectedServerVersionMismatch_Conflict`, `…UnknownCommandType_Rejected_HandlerNotRegistered`, `…UnsupportedContractVersion_426`, `…PermissionDenied_Rejected_BeforeHandler`, `…SettledOutcomes_Return_Http200`, `BatchTests.Over50_Returns_BatchTooLarge`, `BatchTests.ResultsInRequestOrder_NeverReordered`, `IntakeRecoveryServiceTests.ExpiredLease_NotBlindlyRetried_FencedByAttemptId`, `IntakeRecoveryServiceTests.ThreeExpiredLeases_MarksFailed`, `RuleSetPolicyTests.BelowSupportedFrom_Rejected`, `CommandPayloadCatalogTests.EveryEntry_IsJournaledCommand_WithMatchingTypeName`, `CommandPayloadCatalogTests.ResolvesSharedCommandTypes_ByShape`, `CommandPayloadCatalogTests.NoTwoCandidates_ShareCommandTypeAggregateAndPropertySet`, `ReservedCommandTypesTests.Reserved_AreRejected_HandlerNotRegistered`, `ServerChainTests.ServerChain_HasNoCommandJournal_ClientChainUnchanged`.
5. PG-gated (G07-mandatory): `CommandIntakeStoreTests.OnConflictDoNothing_ConcurrentSameKey_SingleWinner`, `…Settled_IffEffectCommitted_RollbackLeavesInProgress`, `CommandIntakeStoreTests.Compaction_KeepsFingerprintAndOutcome`.
6. `grep -rn "ExpectedAggregateVersion" src/Animora.Server` returns nothing (never used as server concurrency evidence).

## PROGRESS.md update

At the end of this phase: set the `S06` row in "Phase status" to `✅ Done` (add date + one-line summary), advance **"Next phase" → `S07`**,
append one `### S06` entry to "Repo state log" (what exists now: projects, tables/migration ordinal, error codes added, test classes), log any
deviation in the "Decision & deviation log", then commit `feat(server): S06 business api` and sync per the repo git workflow.
