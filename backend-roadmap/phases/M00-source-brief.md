# M00 source brief (scratch — owned by PHASE-M00, deleted/kept by T9)

> Shared input for M00 authoring tasks T3–T8. Written by T1/T2. Never edit by a non-M00 phase.

## 1. Baseline snapshot (verified 2026-10-08 against the tree + `PROGRESS.md`)

| Fact | Value | How verified |
|---|---|---|
| Client migration head | **031** (`20261007120000__031_TreatmentPlans.cs`); ordinals `001`–`031` all physical | `ls src/Animora.Persistence.Sqlite/Migrations` |
| Next client ordinal | `032` — **but never hard-code**: list the folder and take the next free ordinal on the owning `DbContext` | PROGRESS "Post-C11 drift" |
| Server migration start | **101** (own sequence on the server `DbContext`; client stays on its own) | ROADMAP §3.5 |
| `CommandTypes` | **88 declared** in `Animora.Contracts.Wire.Commands.CommandTypes` (`All` lists 88). PROGRESS prose says 86 — stale; **the live file is the only truth**, phases say "derived from live `CommandTypes.cs` at execution time" and never freeze a count | `grep -c 'const string' CommandTypes.cs` = 88 |
| `CommandTypes` families (live) | sales 19 · finance 12 · inventory 10 · boarding 8 · patients 7 · vaccination 7 · reception 6 · visits 6 · purchasing 5 · scheduling 5 · cashier 3 | script over the file |
| `ErrorCodes` | **161** declared (`Animora.Domain.Results.ErrorCodes`, `static readonly ErrorCode … = new("CODE", ErrorCode.<Kind>)`) | `grep -c 'public static readonly'` |
| `src/` projects (9) | `Animora.Domain`, `Animora.Application`, `Animora.Persistence.Sqlite`, `Animora.Infrastructure.Local`, `Animora.Client.Services`, `Animora.Contracts`, `Animora.Contracts.Wire`, `Animora.Desktop`, `Animora.Sync.Abstractions` (empty scaffold: csproj refs Domain + Contracts.Wire, README only) | `ls src` |
| `tests/` projects (5) | `Animora.Domain.Tests`, `Animora.Application.Tests`, `Animora.Persistence.Tests`, `Animora.Server.Tests` (placeholder, no subject ref), `Animora.Sync.Tests` (placeholder) | `ls tests` |
| Wire version | `WireContract.Version = 1`, header `X-Animora-Contract-Version` | `WireContract.cs` |
| Post-C11 replicated aggregates | `Animora.Domain.Clinical.TreatmentPlan` (+ `TreatmentOccurrence`, `VaccineCatalogItem`) and `Animora.Domain.Finance.RecurringExpense` (+ `RecurringExpensePayment`) | PROGRESS "Post-C11 drift" |
| Post-C11 command families | `vaccination.treatment.*` (create/update/cancel/occurrence.done), `vaccination.catalog.add`, `finance.recurring.*` (add/edit/toggle/pay) | `CommandTypes.cs` |
| Statistics module | desktop-only (`Animora.Contracts/Statistics`); **no server/replication surface** | PROGRESS |
| Server projects | **none exist** — created by the S-phases; solution + `Animora.Server.Tests` reference added by S01 | `ls src` |

Environment (restate in every phase): build `scripts/sandbox/build.sh [fast|full]`, tests `scripts/sandbox/test.sh`,
run `scripts/sandbox/run.sh`; `dotnet-ef` at `.sandbox/tools` (never `dotnet tool install`). Server stack:
PostgreSQL 18 + `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3, ASP.NET Core 10, EF Core 10 (pins land in S01/S02).
Forbidden tech: Redis, message broker, cloud IdP, cloud telemetry, CDN, EF Core 11, per-tenant database,
hard-coded TLS version, Native AOT with EF Core.

## 2. Regeneration map (29 phases: S01–S20 server, G01–G09 sync/integration/release)

Bands: all **M** except where marked **S**. No XL. Offline invariant (“desktop offline yes”) holds for every row.
`Depends on` cites only lower S/G IDs or completed A/B/C phases (`C11` = whole client baseline).

| New ID | Title | Band | Track | Depends on | R-B satisfied | Covers (ROADMAP §2 inventory) |
|---|---|---|---|---|---|---|
| S01 | Server host scaffold (`Animora.Server`, config, health, rate-limit policies `auth/commands/tables/files/operator`, solution + test wiring) | M | S | C11 | R-B05, R-B27 | D01 |
| S02 | Server data layer & tenancy I — PostgreSQL `ServerDbContext`, tenant discriminator + global filter, `ITenantContext`, migration **101** | M | S | S01 | R-B03, R-B11, R-B19 | D02 (half 1) |
| S03 | Server data layer & tenancy II — tenant-scoped repositories/UoW, tenancy guard (write-path stamping, cross-tenant tests) | S | S | S02 | R-B11, R-B19 | D02 (half 2, split to hold band) |
| S04 | Identity server — ASP.NET Core Identity, bearer + refresh, device-bound sessions | M | S | S03 | R-B04, R-B20 | D03 |
| S05 | Tenant context & authorization policies (Authn ≠ Authz ≠ Entitlement ordering, permission policies) | M | S | S04 | R-B04, R-B19, R-B20 | D04 |
| S06 | Business API — command intake & idempotency (server settlement, `ON CONFLICT` dedupe, `CommandFingerprint`, numeric→UUID version seam, audit) | M | S | S05 | R-B13, R-B14, R-B15, R-B29 | D05 |
| S07 | Server handlers I-a — patients, reception, visits, vaccination (incl. `TreatmentPlan`) | M | S | S06 | R-B01, R-B17 | D06 (half 1) |
| S08 | Server handlers I-b — scheduling & boarding | M | S | S07 | R-B01, R-B17 | D06 (half 2) |
| S09 | Server handlers II-a — sales & cashier | M | S | S08 | R-B01, R-B17, R-B22 | D07 (half 1) |
| S10 | Server handlers II-b — inventory, purchasing, finance (incl. `RecurringExpense`, cheques, ledger) | M | S | S09 | R-B01, R-B17, R-B22, R-B23 | D07 (half 2) |
| S11 | Commit-safe tenant journal & monotonic position (PostgreSQL critical-state replication write path; `TreatmentPlan` + `RecurringExpense` in the replicated set) | M | S | S06, S10 | R-B03, R-B11, R-B13 | D08 (half 1) |
| S12 | Immutable 256-KiB batch export (REPEATABLE READ), offline window + retention, GC crash atomicity, wire version gate | M | S | S11 | R-B03, R-B11, R-B31 | D08 (half 2) |
| S13 | Outbox, background workers, resilience (atomic claim, retry/breaker/timeout/bulkhead) | M | S | S10 | R-B07, R-B09, R-B25 | D09 |
| S14 | Subscription context & operator API | M | S | S05 | R-B05, R-B30 | D10 (half 1) |
| S15 | Self-service onboarding intake (async idempotent provisioning intent from the vendor website) | S | S | S14 | R-B30, R-B36 | D10 (half 2) |
| S16 | Licensing & anti-piracy (signed license, device fingerprint binding, offline grace, clock-rollback, fail-closed) | M | S | S15 | R-B06, R-B29, R-B30, R-B36 | D11 |
| S17 | Files — valet key + resumable transfer (`Staged→…→Available`, reconciliation job) | M | S | S13 | R-B26 | D12 |
| S18 | Server observability & weak-server performance (bounded pools, `MinPoolSize=0`, loopback OTLP, local-only telemetry) | M | S | S13 | R-B07, R-B27, R-B35 | D13 |
| S19 | Admin web I — operator panel core (`Animora.Web.Admin`, Blazor Web App Static-SSR-first, operator auth, tenant/subscription/license views) | M | S | S16 | R-B05 | E01 |
| S20 | Admin web II — insight seams (`ITenantInsightQuery` read-model port only; cross-tenant analytics deferred) | S | S | S19 | R-B05, R-B11, R-B35 | E02 |
| G01 | `Animora.Sync.Abstractions` — `IStateSyncEngine`/`ISyncEngine`, `SyncScope`, `SyncResult`, `NullSyncEngine` fallback + DI | S | G | S12 | R-B03, R-B13 | E03 (half 1) |
| G02 | Custom immutable-batch HTTP adapter (client refs **no** PostgreSQL driver, **no** server project) | M | G | G01 | R-B03, R-B13, R-B31 | E03 (half 2) |
| G03 | Sync orchestration & status (scheduler, `SyncCursor` integration, status surfaced to `ISyncStatusService`) | M | G | G02 | R-B03 | E04 |
| G04 | Conflict resolution & non-destructive pull (preserve pending intent; posted-invoice conflicts reversal-only) | M | G | G03 | R-B03, R-B17, R-B18 | E05 |
| G05 | Command transport & settlement reconciliation (replaces `NullCommandTransport`) | M | G | G04, S10 | R-B03, R-B09, R-B16 | E06 |
| G06 | Online auth, entitlement & device lifecycle (replaces `AllowAllEntitlementGate`; credential-mirror fallback) | M | G | G05, S16 | R-B04, R-B06, R-B20, R-B21, R-B36 | E07 |
| G07 | Validation gates I — mandatory PostgreSQL CI, tenancy/RLS gate, restore gate (`STACK.md` §12, ARCH §19) | M | G | G06, S18 | R-B19, R-B32 | E08 (half 1) |
| G08 | Validation gates II — performance proof & offline budget (weak-server/weak-client capacity, layering audit) | M | G | G07, S20 | R-B07, R-B08, R-B10, R-B32 | E08 (half 2) |
| G09 | Release engineering (topology, install/upgrade/rollback runbook, backup/restore rehearsal, packaging, SBOM) | M | G | G08 | R-B27, R-B31, R-B33 | E09 |

**Totals:** S01–S20 (20) + G01–G09 (9) = **29 regenerated phases**. Bands: **S = 4** (S03, S15, S20, G01), **M = 25**, **XL = 0**.

**Phase-count note.** ROADMAP §0 caps the *original* 50 phases; M00 rule 1 states the regenerated phase *count is free,
per-phase weight is not*. With A (12) + B (5) + C (11) = 28 complete phases + 29 regenerated = **57 total**. T9 rewrites
ROADMAP §0/§1 totals accordingly and records the deviation (splits: D02, D06, D07, D08, D10, E03, E08 each into two).

### Coverage matrix (ROADMAP §2 D/E row → regenerated phase)

| Old row | New phase(s) | Old row | New phase(s) |
|---|---|---|---|
| D01 host | S01 | E01 admin I | S19 |
| D02 data+tenancy | S02, S03 | E02 insight seams | S20 |
| D03 identity | S04 | E03 engine + HTTP adapter | G01, G02 |
| D04 tenant ctx + authz | S05 | E04 orchestration | G03 |
| D05 command intake | S06 | E05 conflict | G04 |
| D06 handlers I | S07, S08 | E06 transport + settlement | G05 |
| D07 handlers II | S09, S10 | E07 online auth/entitlement/device | G06 |
| D08 journal/batch | S11, S12 | E08 gates + perf | G07, G08 |
| D09 outbox/workers | S13 | E09 release | G09 |
| D10 subscription/operator/onboarding | S14, S15 | | |
| D11 licensing | S16 | | |
| D12 files | S17 | | |
| D13 observability/perf | S18 | | |

Post-C11 drift coverage: `TreatmentPlan` handlers → S07; `RecurringExpense` handlers → S10; both in replicated set → S11/S12;
88-type `CommandTypes` handler inventory split S07 (patients/reception/visits/vaccination), S08 (scheduling/boarding),
S09 (sales/cashier), S10 (inventory/purchasing/finance); Statistics has no server surface (stated in S11 Out of scope).

### Artifact single-introducer table (ROADMAP §3.3 guard for T3–T8)

| Artifact | Introduced by |
|---|---|
| `Animora.Server` project, `ServerDbContext`, migration 101, `ITenantContext` | S01 (project), S02 (context/101/tenant) |
| Tenant repositories/UoW/tenancy guard | S03 |
| Identity tables + token service | S04 (migration = next free server ordinal) |
| Permission policies, entitlement-order middleware | S05 |
| Server command journal table, `ICommandIntake`, settlement store | S06 |
| Server aggregate tables + handlers per context | S07–S10 (each its own next-free migration) |
| `TenantJournal`, position allocator | S11 |
| Batch export endpoint, retention/GC | S12 |
| Server outbox + worker host | S13 |
| `Subscription` aggregate, operator API | S14 |
| `ProvisioningIntent` + intake endpoint | S15 |
| License issuer, device binding, grace | S16 |
| File lifecycle + valet key | S17 |
| Telemetry/pool config | S18 |
| `Animora.Web.Admin` | S19 |
| `ITenantInsightQuery` | S20 |
| `IStateSyncEngine`, `NullSyncEngine` | G01 |
| HTTP adapter | G02 |
| Sync scheduler/status | G03 |
| Conflict policy engine | G04 |
| `HttpCommandTransport` | G05 |
| Online entitlement gate + device lifecycle | G06 |
| CI/RLS/restore gates | G07 |
| Perf proof / budgets | G08 |
| Runbook + packaging | G09 |

---

## Reference pack (shared)

> Verbatim from the tree on 2026-10-08 (XML doc comments stripped; members/signatures untouched). Every entry
> is **already introduced** by the phase noted — **reuse, never redefine**. Authoring tasks T3–T8 paste only the
> entries their phase calls. If a compile error proves one diverges, the executing session fixes the smallest
> thing and logs it in `PROGRESS.md`.

### R1 — Wire seam: `Animora.Contracts.Wire` (project `src/Animora.Contracts.Wire`, introduced by A01/B02)

```csharp
namespace Animora.Contracts.Wire;
public static class WireContract
{
    public const int Version = 1;
    public const string HeaderName = "X-Animora-Contract-Version";
}

namespace Animora.Contracts.Wire.Commands;
public sealed record CommandEnvelope(
    int ContractVersion, string OperationId, string TenantId, string UserId, string DeviceId,
    string CommandType, string AggregateType, string? AggregateId, string PayloadJson,
    string CommandFingerprint, DateTimeOffset CreatedAtUtc, long LocalSequence,
    string? CausationId, string? DependsOnOperationId, long? ExpectedAggregateVersion)
{
    public static CommandEnvelope Create(string operationId, string tenantId, string userId, string deviceId,
        string commandType, string aggregateType, string? aggregateId, string payloadJson,
        string commandFingerprint, DateTimeOffset createdAtUtc, long localSequence,
        string? causationId = null, string? dependsOnOperationId = null, long? expectedAggregateVersion = null);
        // => new(WireContract.Version, …)
}

public sealed record SettlementResult(
    string OperationId, SettlementOutcome Outcome, string? ErrorCode, string? Detail,
    string? ServerEntityId, string? ServerVersion, DateTimeOffset SettledAtUtc,
    FinancialEffect FinancialEffect, string? ResponsibleRole);

public enum SettlementOutcome { Accepted, Rejected, Conflict, Deferred }
public enum FinancialEffect  { None, Applied, NotApplied, Unknown }

namespace Animora.Contracts.Wire.Errors;
public sealed record WireApiError(string Code, string? Detail, IReadOnlyList<WireFieldError> Fields, string CorrelationId);
public sealed record WireFieldError(string Field, string Code);
```

Notes: `ServerVersion` is a **string** on the wire (the numeric→UUID version seam of S06 maps to/from it);
`ExpectedAggregateVersion` is a numeric `long?` client-side. `NullCommandTransport` answers `Deferred` +
`FinancialEffect.Unknown` + `SettledAtUtc = UnixEpoch`.

### R2 — `CommandTypes` (introduced by B02; extended by C03–C09 and post-C11 audit passes)

`public static class Animora.Contracts.Wire.Commands.CommandTypes` — `public const string <Name> = "<ctx>.<aggregate>.<verb>";`
plus `public static IReadOnlyList<string> All { get; }`. Count is **88** today; **never freeze a number in a phase** —
the handler inventory is `CommandTypes.All` at execution time (a test `Server handler registry covers every CommandTypes.All entry`
is the S07–S10 gate). Families (live): sales 19 · finance 12 · inventory 10 · boarding 8 · patients 7 · vaccination 7 ·
reception 6 · visits 6 · purchasing 5 · scheduling 5 · cashier 3. Names are permanent; later phases only **add**.

Representative constants (full list: `grep 'const string' src/Animora.Contracts.Wire/Commands/CommandTypes.cs`):
`SalesInvoicePost="sales.invoice.post"`, `SalesPaymentRegister="sales.payment.register"`,
`CashierSessionOpen`, `InventoryStockAdjust`, `PurchasingOrderSubmit`, `FinancePeriodClose`, `FinanceRecurringPay`,
`SchedulingAppointmentBook`, `BoardingStayCheckout`, `PatientsOwnerCreate`, `VisitsVisitComplete`,
`VaccinationTreatmentOccurrenceDone="vaccination.treatment.occurrence.done"`, `VaccinationCatalogAdd="vaccination.catalog.add"`.

### R3 — Settlement vocabulary (introduced by A02/A07)

```csharp
namespace Animora.Domain.Primitives;
public enum SettlementState { Draft, LocallyPosted, PendingSettlement, Settled, Rejected, Conflict, Reversed, NeedsAttention }

public static class SettlementTransitions
{
    public static Result EnsureAllowed(SettlementState from, SettlementState to); // fails ErrorCodes.SettlementInvalidTransition, detail "{from} -> {to}"
    public static bool IsAllowed(SettlementState from, SettlementState to);
}
```
Transition table (verbatim semantics): `Draft→{LocallyPosted,Rejected}` · `LocallyPosted→{PendingSettlement,Rejected,Reversed}` ·
`PendingSettlement→{Settled,Rejected,Conflict,NeedsAttention,PendingSettlement(retry re-entry)}` ·
`Conflict→{NeedsAttention,Reversed,Settled}` · `Rejected→{NeedsAttention,Reversed}` ·
`NeedsAttention→{Reversed,Settled,Rejected}` · `Settled→{Reversed}` · `Reversed` terminal.
(`Animora.Domain.Sales.SettlementStatus` is a separate 8-state **invoice** settlement enum from A07 — do not conflate.)

### R4 — Application ports a server handler / the sync client calls (`Animora.Application`, introduced by B01/B02)

```csharp
namespace Animora.Application.Abstractions;
public interface ICommand<TResult>;                       // marker
public interface ICommandHandler<in TCommand, TResult> where TCommand : ICommand<TResult>
{ Task<Result<TResult>> HandleAsync(TCommand command, CommandContext context, CancellationToken ct); }
public interface IDispatcher
{
    Task<Result<TResult>> SendAsync<TResult>(ICommand<TResult> command, CommandContext ctx, CancellationToken ct = default);
    Task<Result<TResult>> QueryAsync<TResult>(IQuery<TResult> query, QueryContext ctx, CancellationToken ct = default);
}
public interface IUnitOfWork
{
    Task<Result> SaveAsync(CancellationToken ct = default);
    Task<Result<T>> ExecuteAsync<T>(Func<CancellationToken, Task<Result<T>>> work, CancellationToken ct = default);
}
public sealed record CommandContext(string TenantId, string UserId, string DeviceId, string OperationId,
    string CorrelationId, string? CausationId, string? DependsOnOperationId, long? ExpectedAggregateVersion,
    DateTimeOffset CreatedAtUtc);

namespace Animora.Application.Commands;
public interface IJournaledCommand { string CommandTypeName { get; } string AggregateTypeName { get; } string? AggregateIdOrNull { get; } }

public interface ICommandTransport   // client → server; real impl = G05 (HttpCommandTransport); fallback NullCommandTransport
{ Task<Result<SettlementResult>> SubmitAsync(CommandEnvelope envelope, CancellationToken ct = default); }

public interface ICommandJournal     // local durable journal (SQLite); server has its OWN intake store (S06), never this port
{
    Task<Result<PendingCommandRecord>> EnqueueAsync(CommandEnvelope envelope, CancellationToken ct = default);
    Task<Result<PendingCommandRecord?>> FindByOperationIdAsync(string operationId, CancellationToken ct = default);
    Task<Result> ApplySettlementAsync(SettlementResult result, CancellationToken ct = default);
    Task<Result<IReadOnlyList<PendingCommandRecord>>> ClaimBatchAsync(int max, TimeSpan lease, CancellationToken ct = default);
    Task<Result> ReleaseAsync(string operationId, ErrorCode error, TimeSpan backoff, CancellationToken ct = default);
    Task<Result<IReadOnlyList<PendingCommandRecord>>> GetUnsettledForAggregateAsync(string aggregateType, string aggregateId, CancellationToken ct = default);
    Task<Result<PendingCommandSummary>> GetSummaryAsync(CancellationToken ct = default);
    Task<Result<PagedResult<PendingCommandRecord>>> GetUnsettledPageAsync(int page, int pageSize, CancellationToken ct = default);
}

public sealed record PendingCommandRecord(string Id, string OperationId, string TenantId, string UserId, string DeviceId,
    string CommandType, string AggregateType, string? AggregateId, string PayloadJson, string CommandFingerprint,
    long LocalSequence, SettlementState Status, string? CausationId, string? DependsOnOperationId,
    long? ExpectedAggregateVersion, DateTimeOffset CreatedAtUtc, DateTimeOffset? LastAttemptUtc, int Attempts,
    DateTimeOffset? NextAttemptUtc, string? LastErrorCode, string? LastErrorDetail, FinancialEffect FinancialEffect,
    string? ResponsibleRole, DateTimeOffset? SettledAtUtc, string? ServerEntityId, string? ServerVersion,
    string? ClaimToken, DateTimeOffset? ClaimedUntilUtc);

public static class CommandFingerprint   // SHA-256 hex (lower) over commandType + '\u001f' + canonical JSON (sorted keys, camelCase)
{
    public const char Separator = '\u001f';
    public static string Compute(string commandType, string payloadJson);
    public static string Compute<T>(string commandType, T payload);
    public static string CanonicalJson<T>(T payload);
}

namespace Animora.Application.Outbox;
public interface IOutbox { Task<Result> EnqueueAsync(string messageType, object payload, CancellationToken ct = default); }

namespace Animora.Application.Security;
public interface IEntitlementGate   // fallback today: AllowAllEntitlementGate (always Result.Ok()); real = G06
{
    ValueTask<Result> EvaluateAsync(object command, CommandContext context, CancellationToken ct);
    ValueTask<Result> EvaluateAsync(object query, QueryContext context, CancellationToken ct);
}

namespace Animora.Application.Ports;
public interface ISyncCursorStore   // C11 — tenant/scope "last synced" marker
{ Task<DateTimeOffset?> ReadAsync(CancellationToken ct = default); Task MarkSyncedAsync(DateTimeOffset whenUtc, CancellationToken ct = default); }
```

Pipeline behaviors that already exist (client side, `Animora.Application/Behaviors`): `Correlation`, `Authorization`,
`Entitlement`, `Validation`, `Transaction`, `CommandJournal`, `Audit`, `ExceptionMapping`. A server reuses the handler
abstractions and the dispatcher only where a phase's Reference pack says so; **`Animora.Server` must never reference
`Animora.Persistence.Sqlite`, `Animora.Infrastructure.Local`, `Animora.Client.Services` or `Animora.Desktop`** (layering rule, A01 `LayeringRulesTests`).

### R5 — `Result` / `ErrorCodes` (introduced by A02)

```csharp
namespace Animora.Domain.Results;
public readonly record struct ErrorCode(string Code, string Category)
{
    public const string Validation="Validation", NotFound="NotFound", Conflict="Conflict", Forbidden="Forbidden",
                        Integrity="Integrity", Infrastructure="Infrastructure", Entitlement="Entitlement";
    public static readonly ErrorCode None = new(string.Empty, string.Empty);
    public bool IsNone { get; }
}
public readonly record struct Result      { IsSuccess, IsFailure, Error, Detail; static Ok(); static Fail(ErrorCode, string? detail = null); Bind(…) }
public readonly record struct Result<T>   { IsSuccess, IsFailure, T? Value, Error, Detail; static Ok(T); static Fail(ErrorCode, string? detail = null);
                                            implicit operator Result<T>(T); ToResult(); Map<TOut>(…); Bind<TOut>(…); OrElse(T) }
public static class ErrorCodes   // 161 today: `public static readonly ErrorCode X = new("X_CODE", ErrorCode.<Kind>);` grouped in #region per context
{ ValidationFailed="VALIDATION_FAILED"(Validation), NotFound="NOT_FOUND", ConcurrencyConflict="CONCURRENCY_CONFLICT"(Conflict),
  PermissionDenied="PERMISSION_DENIED"(Forbidden), SettlementInvalidTransition, … }
```
Rule: server phases **add** codes in a new `#region` (never rename); wire maps `ErrorCode.Code` → `WireApiError.Code` /
`SettlementResult.ErrorCode`. Count is read from the file at execution time (`grep -c 'public static readonly ErrorCode'`).

### R6 — Sync scaffold (`src/Animora.Sync.Abstractions`, scaffold by A01; filled by **G01**)

`Animora.Sync.Abstractions.csproj` = `<ProjectReference>` to `Animora.Domain` and `Animora.Contracts.Wire` only, no
packages; `README.md` placeholder text; **no `.cs` files**. Client-side status seam already live: `Animora.Contracts.Sync.ISyncStatusService`
(implemented by `Animora.Client.Services.Sync.LocalSyncStatusService`, reads `ISyncCursorStore` + `ICommandJournal`).
`tests/Animora.Sync.Tests` and `tests/Animora.Server.Tests` are placeholder projects (`PlaceholderTests.cs`, no subject
reference; S01 / G01 add the project references).


---

## Naming lock (T3 — resolves a T1 inconsistency; authority `ARCHITECTURE.md` §6 > `CONVENTIONS.md` §1 > phase)

| Project | Created by | Notes |
|---|---|---|
| `Animora.Server` | S01 | ASP.NET Core 10 host (Business API `/api/commands*`, `/sync/*`, `/auth/*`, `/operator/*`, `/admin`, workers) |
| `Animora.Persistence.PostgreSql` | S02 | the **only** project that references Npgsql / `UseNpgsql`; `AnimoraServerDbContext` |
| `Animora.Server.Licensing` | S16 | `Verification/` half = Domain + Contracts.Wire only (client-reusable); `Issuance/` half = Application + Domain (server-only) |
| `Animora.Web.Admin` | S19 | Blazor Web App operator panel, hosted by `Animora.Server` (T1 wrote `Animora.AdminWeb` — corrected to the ARCH §6 name) |
| `Animora.Sync.Custom` | G02 | immutable-batch HTTP adapter; references Sync.Abstractions + Application + Persistence.Sqlite + Contracts.Wire; **no** Npgsql |
| `tests/Animora.Server.Tests` | exists (placeholder) → S01 adds `ProjectReference` to `Animora.Server` | |
| `tests/Animora.Sync.Tests` | exists (placeholder) → G01 adds `ProjectReference` to `Animora.Sync.Abstractions` | |

Rate-limit policy names (supersedes the PH-08 text `tables`): **`auth`, `commands`, `sync`, `files`, `operator`** — `/tables/*` (Datasync) is deferred (STACK §5.2 optional, G1 gate), the
critical feed lives under `/sync/*` (ARCH §4), so the policy is named `sync`. T9 records this in the PROGRESS drift ledger.

Server data-protection key ring: **database-backed on its own tiny `DbContext`** (`AnimoraKeyRingDbContext`, ARCH §10.6) introduced by S02 with its own migration history table; S01 needs no key ring (no cookies/tokens yet).
