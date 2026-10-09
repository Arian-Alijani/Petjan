# Phase S20 — Admin web II — operations pages, the single live island & the insight read-model seam (`ITenantInsightQuery`, port only)

**Band:** M · **Track:** S (server) · **Depends on:** S19 · **Next:** G01
**Satisfies:** R-B05, R-B11, R-B35 · **Runnable after this phase:** desktop offline yes · server required no
**Produced by:** M00 (regenerated 2026-10-09; supersedes the deleted `PHASE-E02` file).

> Baseline verified 2026-10-09: client migration head `031` (`031_TreatmentPlans`), `CommandTypes` = live `CommandTypes.cs` (88 declared today — never freeze), `ErrorCodes` 161 (+ codes added by earlier S phases), `WireContract.Version` = 1 (S06 bumps to 2, supported {1,2}); `src/` projects = `Animora.Domain`, `Animora.Application`, `Animora.Persistence.Sqlite`, `Animora.Infrastructure.Local`, `Animora.Client.Services`, `Animora.Contracts`, `Animora.Contracts.Wire`, `Animora.Desktop`, `Animora.Sync.Abstractions` (+ server-track projects added by earlier S/G phases).
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
- `ARCHITECTURE.md` §13.5, §16.3 (reporting isolation, `QueryBudget`, bounded tenant-safe caches), §7.3, §20 (insight trigger) — only these `§`.
- `src/Animora.Web.Admin/{Ports,RenderModePolicy.cs,Layout/NavMenu.razor}`, `src/Animora.Server/Admin/AdminServiceCollectionExtensions.cs` (S19; edited here), `src/Animora.Server/Observability/{QueryBudget,TenantScopedMemoryCache,ServerMetrics}.cs` (S18).
- `src/Animora.Persistence.PostgreSql/Provisioning/IProvisioningIntentStore.cs`, `Operator/AnimoraOperatorDbContext.cs`, `src/Animora.Server/Hints/*` + `Workers/WorkerHeartbeat.cs` (S15/S13).

(Nothing else. `Animora-UI/**`, `Database-Sampel/**`, other phase files and `windows-frontend-roadmap/phases/**` are out of bounds.)

## Goal

Finish the operator panel with the **operations pages** the earlier server phases left without a UI (provisioning intents + website clients, flagged file mismatches, audit viewers), add the **one** permitted `InteractiveServer` island (`LiveStatusPanel`, bounded circuits), and land the **insight seam**: the read-model port `ITenantInsightQuery` with a **zero-feature** default implementation, a reporting-isolated read path (`IQueryBudget.Acquire(Reporting)`, `TenantScopedMemoryCache`, never the tenant business context) and the consent switch — so cross-tenant analytics can later be added without redesign. **No analytics feature, chart or aggregate is built** (ROADMAP §5 deferred; trigger = operator demand + explicit consent/ADR).

---

## §Reference pack

```csharp
// Animora.Domain.Results (A02) — reuse, never redefine
public readonly record struct ErrorCode(string Code, string Category)   // categories: Validation|NotFound|Conflict|Forbidden|Integrity|Infrastructure|Entitlement
{ public const string Validation="Validation", NotFound="NotFound", Conflict="Conflict", Forbidden="Forbidden", Integrity="Integrity", Infrastructure="Infrastructure", Entitlement="Entitlement";
  public static readonly ErrorCode None; public bool IsNone { get; } }
public readonly record struct Result      { bool IsSuccess, IsFailure; ErrorCode Error; string? Detail; static Result Ok(); static Result Fail(ErrorCode e, string? detail = null); }
public readonly record struct Result<T>   { bool IsSuccess, IsFailure; T? Value; ErrorCode Error; string? Detail; static Result<T> Ok(T v); static Result<T> Fail(ErrorCode e, string? detail = null); Map/Bind/OrElse; implicit operator Result<T>(T) }
// ErrorCodes: `public static readonly ErrorCode X = new("X_CODE", ErrorCode.<Kind>);` inside `#region <context>`. Server phases ADD a new #region; never rename/remove.
// Already declared & reusable: ValidationFailed, NotFound, ConcurrencyConflict, PermissionDenied, PermissionUnknown, TenantMismatch, IdempotencyFingerprintMismatch(Integrity),
//   EntitlementRequired(Entitlement), TenantSuspended, UserDisabled, UserLockedOut, DeviceRevoked, CredentialsInvalid, SessionExpired, PasswordPolicyViolation, MembershipNotActive, OutboxDeadLettered, Unexpected
namespace Animora.Domain.Time;  public interface IClock { DateTimeOffset UtcNow { get; } }
namespace Animora.Domain.Primitives; public static class IdGenerator { public static string NewId(); /* 26-char ULID */ }
```

```csharp
// S19 (exact) — reuse
namespace Animora.Web.Admin.Ports;   // all Task<Result<…>> with CancellationToken; AdminPage<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total) with PageSize ≤ 200
public interface IAdminStatusReader { Task<Result<AdminStatus>> GetStatusAsync(CancellationToken ct = default); }
public interface IAdminSession { string OperatorId { get; } string UserName { get; } }
public static class RenderModePolicy { public const int MaxIslands = 0; /* S20 raises to 1 */ }
// S19 server: AdminServiceCollectionExtensions.AddAnimoraAdmin() (AddRazorComponents only today), OperatorCookie ("Animora.Operator.Cookie", policy AdminOnly), AdminSecurityHeadersMiddleware (CSP default-src 'self'; form-action 'self'; no inline script), UseAntiforgery(), MapRazorComponents<…> group RequireAuthorization("AdminOnly")
// S15: IProvisioningIntentStore (list by status/page, retry), ProvisioningIntent(Id, IdempotencyKey, ClientId, Status{Received,Processing,Completed,Failed,Dead}, Attempts, ErrorCode, ReceivedUtc, CompletedUtc?), WebsiteClient(Id, KeyId, Status{Active,Revoked}, Label, CreatedUtc, LastUsedUtc?),
//      WebsiteClientService (create → secret once / revoke);  operator endpoints already exist under /operator/website-clients and /operator/provisioning/intents — the pages use the SAME services
// S17: FileObjects(State: …, Mismatch=6 …), FileReconciliationWorker (flags Mismatch + audit `files.reconcile.blob_missing`);  operator action needed: acknowledge/requeue a Mismatch row — NOT implemented in S17 (see Design)
// S04/S14: SecurityAudit (tenant-scoped, append-only), OperatorAudit (vendor-global, append-only);  S13: WorkerHeartbeat, ServerOutbox/ConsumedMessages dead-letter counts
// S18: interface IQueryBudget { ValueTask<QueryLease> Acquire(QueryClass cls, CancellationToken ct); } (QueryClass { Oltp, Reporting }; Reporting = 1 concurrent, 5 s statement timeout, 10 000-row cap, worker data source);
//      TenantScopedMemoryCache<TKey,TValue> key=(TenantId, UserId, Resource, Version) — all four mandatory; bounded 1 000 entries, 60 s sliding TTL;  ServerMetrics
// S01: ServerOptions/SecretGuard, RateLimitPolicies.Operator;  S14: AnimoraOperatorDbContext (only from Operator/Subscriptions/Licensing/Admin/Provisioning/Insight namespaces — whitelist extended here)
```

## §Design decisions

- **Operations pages (Static SSR, same conventions as S19; adapters in `Animora.Server/Admin/`, ports in `Animora.Web.Admin.Ports`):**
  - `/admin/provisioning` — list intents (`IAdminProvisioningQueries.ListAsync(status?, page, pageSize ≤ 200)`), detail, **Retry** (Dead/Failed-transient → Received; POST + confirm; wraps S15's operator retry service). Shows error code + attempts; never the payload or any password. `/admin/website-clients` — list, **Create** (secret displayed **once** in the POST response with a "copy now" notice; no GET ever re-shows it), **Revoke** (POST + confirm); max-3-active rule surfaces `VALIDATION_FAILED`.
  - `/admin/files/mismatches` — lists `FileObjects` with `State=Mismatch` (tenant code, file id, size, sha prefix 8 chars, `UpdatedUtc`; **never** a path, never content) via `IAdminFileQueries` (operator context, read-only, paged); actions via `IAdminFileCommands`: **Requeue verification** (`Mismatch → Uploaded`, so the S17 worker re-hashes) and **Abandon** (`Mismatch → Abandoned`, content GC'd by reconciliation). S17 declared `Mismatch` "terminal until an operator action (S19)" — **this phase owns that action**: `FileLifecycle` gains the two table-driven transitions `Mismatch→Uploaded`, `Mismatch→Abandoned` (S20 edits S17's `FileLifecycle` table + its test `FileLifecycleTests.AllowedAndForbiddenTransitions_Table`), each writing `OperatorAudit` (`files.mismatch.requeue|abandon`) in the same transaction. No other file-state mutation is exposed.
  - `/admin/audit` — two tabs: **Operator audit** (all, filter by action/tenant/date, paged) and **Tenant security audit** (requires choosing a tenant; read via the operator context with an explicit `TenantId` predicate, `AsNoTracking`, paged, projection excludes `DetailJson` bodies > 2 KiB truncated). Read-only. Date range ≤ 90 days per query; `QueryClass.Reporting` is used for the unfiltered operator-audit count.
  - `/admin/system` — worker heartbeats, readiness probe details by name, pool numbers, retention state (`OldestRetainedSequence` per top-N lagging tenants **not** shown — aggregate only), build version. Same data as S19's dashboard in full form.
- **The island (`LiveStatusPanel.razor`, the only `@rendermode InteractiveServer` in `Animora.Web.Admin`):** shows the S19 `AdminStatus` counters refreshed every **10 s** (`PeriodicTimer`, disposed with the component) on `/admin` only. `RenderModePolicy.MaxIslands = 1`. Circuit budget: `AddInteractiveServerComponents().AddCircuitOptions(o => { o.DisconnectedCircuitMaxRetained = 2; o.DisconnectedCircuitRetentionPeriod = TimeSpan.FromMinutes(1); o.MaxBufferedUnacknowledgedRenderBatches = 4; })` and `MaxConcurrentUpgradedConnections` (S18 = 64) already caps circuits; the hub endpoint `/_blazor` is served **only** under the authenticated `/admin` group and refuses anonymous (`RequireAuthorization("AdminOnly")` on `MapBlazorHub` group via `AddInteractiveServerRenderMode()`), rate-limit `operator`. The island reads only the `IAdminStatusReader` port (in-process counters, **no DB query per tick** — it reads the cached `AdminStatus` snapshot refreshed by one singleton `AdminStatusCache` every 10 s regardless of how many panels are open ⇒ cost independent of circuit count). Antiforgery + CSP: framework script is served from the app itself (`_framework/blazor.web.js`), CSP stays `script-src 'self'`; **no nonce, no inline script**. No `[PersistentState]` needed (no prerender handoff: `@rendermode new InteractiveServerRenderMode(prerender: false)`). Multi-instance/backplane: forbidden here (ROADMAP §5, A-RULE 031 ADR) — documented in the component XML-doc and the G09 runbook (single instance).
- **Insight seam (the deliverable of this phase; contract only):**
  ```csharp
  namespace Animora.Persistence.PostgreSql.Insight;                       // server-only; never referenced by Domain/Application/clients
  public interface ITenantInsightQuery
  {
      Task<Result<InsightPage<InsightRow>>> QueryAsync(InsightRequest request, CancellationToken ct = default);   // always Reporting-class, bounded
      bool IsEnabled { get; }                                                                                    // false by default
  }
  public sealed record InsightRequest(string Metric, string? TenantId, DateOnly From, DateOnly To, int Page, int PageSize);   // PageSize ≤ 200; To-From ≤ 400 days; Metric ∈ InsightMetrics.Registered (empty today)
  public sealed record InsightRow(string Dimension, string? Label, decimal Value);
  public sealed record InsightPage<T>(IReadOnlyList<T> Items, int Page, int PageSize, bool Truncated);
  public static class InsightMetrics { public static readonly IReadOnlySet<string> Registered = new HashSet<string>(); }      // NO metric is registered at launch
  ```
  `DisabledTenantInsightQuery` (default, `IsEnabled=false`) returns `Result.Fail(ErrorCodes.InsightNotEnabled)` for every request. The **extension point** for a future metric is `IInsightMetricProvider { string Name; Task<Result<IReadOnlyList<InsightRow>>> ExecuteAsync(InsightQueryContext ctx, CancellationToken ct); }` registered through `AddInsightMetric<T>()`; `ReportingInsightQuery : ITenantInsightQuery` (compiled and registered **only when** `Insight:Enabled=true`) validates the request, acquires `IQueryBudget.Acquire(QueryClass.Reporting)` (1 concurrent, 5 s, 10 000 rows — A-RULE 034), runs the provider on the **`worker` data source** through a **read-only, projected-tables-only context** `AnimoraInsightReadContext` (no `AnimoraServerDbContext`, no tenant business entities — only tables listed in `InsightSourceCatalog`, **empty today**; adding a source = one catalog row + the consent ADR), caches results in `TenantScopedMemoryCache<(…)>` keyed `(TenantId ?? "*", operatorId, metric, version)`. Cross-tenant **aggregation requires** `Insight:ConsentAdrReference` (non-empty string naming the accepted ADR) **and** `Insight:Enabled=true`; options validator fails startup if `Enabled` without `ConsentAdrReference` (consent is a startup-time contract, not a UI toggle). Insight never writes, never joins into OLTP, and is never reachable from a tenant principal.
  - Isolation tests are the deliverable: `InsightIsolationTests` prove (a) the seam compiles to nothing reachable from tenant routes, (b) the default is disabled, (c) no `DbContext` other than `AnimoraInsightReadContext` is injectable into `Insight/**` (source scan), (d) a request with `PageSize > 200` / range > 400 d / unregistered metric is rejected before any DB work, (e) the reporting lease is **always** acquired (a stub budget asserts it).
- **Admin extras that the seam enables but does not build:** the dashboard shows an "Insight: disabled (no consent ADR)" status row only.
- **New ErrorCodes (region "Insight", +1):** `InsightNotEnabled ("INSIGHT_NOT_ENABLED", Entitlement)`. New options `InsightOptions { Enabled=false, ConsentAdrReference=null, CacheTtlSeconds=60 }` (section `Insight`).

## §Implementation plan

1. ErrorCodes +1; `RenderModePolicy.MaxIslands = 1`; `FileLifecycle` + 2 transitions (S17 file, test updated).
2. Persistence: `Insight/{ITenantInsightQuery,InsightRequest,InsightRow,InsightPage,InsightMetrics,IInsightMetricProvider,InsightSourceCatalog,AnimoraInsightReadContext,ReportingInsightQuery,DisabledTenantInsightQuery,InsightOptions,InsightOptionsValidator}.cs`; `Files/FileMismatchService.cs` (requeue/abandon, operator context, audit); `Provisioning/ProvisioningQueries.cs`, `Audit/AuditQueries.cs` (read-only paged). **No migration** (no table).
3. Web.Admin: ports `IAdminProvisioningQueries/Commands`, `IAdminWebsiteClients`, `IAdminFileQueries/Commands`, `IAdminAuditQueries`, `IAdminInsightStatus`; pages `Provisioning`, `WebsiteClients`, `FileMismatches`, `Audit`, `System`; component `Components/LiveStatusPanel.razor` (+ `NavMenu` links); `AdminMessages` additions (INSIGHT_NOT_ENABLED, file transitions).
4. Server: adapters for the new ports in `Animora.Server/Admin/`, `AdminStatusCache` (singleton, `PeriodicTimer` 10 s, under `WorkerHeartbeat`), `AddInteractiveServerComponents().AddCircuitOptions(...)`, `MapRazorComponents<…>().AddInteractiveServerRenderMode()` inside the authorized `/admin` group; `AddAnimoraInsight()` (registers Disabled by default, Reporting when enabled); extend S14 `ServerSourceScanTests` whitelist (`Insight`, `Admin`).
5. Tests (below). `scripts/sandbox/build.sh full` → 0/0; `scripts/sandbox/test.sh Server` → green; `scripts/sandbox/run.sh` → `SMOKE RUN OK`.
6. `PROGRESS.md` per recipe; commit.

## Files to create / modify

| Path | Contents |
|---|---|
| `src/Animora.Domain/Results/ErrorCodes.cs` (edit) | +1 code |
| `src/Animora.Persistence.PostgreSql/Insight/*.cs`, `Files/{FileLifecycle(edit),FileMismatchService}.cs`, `Provisioning/ProvisioningQueries.cs`, `Audit/AuditQueries.cs` | seam + operations read/write services |
| `src/Animora.Web.Admin/Ports/*`, `Pages/{Provisioning,WebsiteClients,FileMismatches,Audit,System}.razor`, `Components/LiveStatusPanel.razor`, `RenderModePolicy.cs`, `AdminMessages.cs`, `Layout/NavMenu.razor` | UI |
| `src/Animora.Server/Admin/*Adapter.cs`, `AdminStatusCache.cs`, `AdminServiceCollectionExtensions.cs`, `Program.cs` (edit), `Insight/InsightServiceCollectionExtensions.cs` | adapters, island host, DI |
| `tests/Animora.Server.Tests/Admin/*.cs`, `tests/Animora.Server.Tests/Insight/*.cs`, `tests/Animora.Server.Tests/Files/FileLifecycleTests.cs` (edit) | tests |

## Contracts / schema added

`ITenantInsightQuery` (+ `InsightRequest/Row/Page`, `IInsightMetricProvider`, `InsightSourceCatalog`, `InsightOptions`); the one island `LiveStatusPanel`; new admin ports/pages; `FileLifecycle` transitions `Mismatch→Uploaded|Abandoned`; ErrorCodes +1. No table, no wire DTO.

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
- **Reporting isolation (ARCH §16.3, A-RULE 033/034):** insight/aggregation never rides the OLTP transaction or the tenant business context; every cache key carries tenant + user + resource + version; budget lease mandatory.
- **Web:** one island maximum (`RenderModePolicy.MaxIslands`), no global interactivity, no backplane, no external assets.

## Out of scope (owning phase)

- Any actual cross-tenant metric, chart, per-store stock view, or aggregate table → **deferred** (ROADMAP §5; trigger: operator demand + accepted consent ADR; first step = register an `IInsightMetricProvider` + `InsightSourceCatalog` row).
- Clinic-facing web client → deferred (ROADMAP §5). Redis/SignalR backplane, multi-instance admin → deferred (A-RULE 031 ADR).
- Performance proof of the admin/insight budgets → **G08**; PG-gated gates → **G07**.

## Acceptance criteria (mechanical)

1. `scripts/sandbox/build.sh full` = 0/0; `scripts/sandbox/run.sh` = `SMOKE RUN OK`; client sources unchanged.
2. `grep -rn "@rendermode" src/Animora.Web.Admin | wc -l` = 1 and it is in `LiveStatusPanel.razor`; `RenderModePolicy.MaxIslands` = 1; `grep -rn "DbContext" src/Animora.Persistence.PostgreSql/Insight | grep -v AnimoraInsightReadContext` returns nothing.
3. Pure/host tests pass: `RenderModePolicyTests.NoGlobalRenderMode_AndIslandCountWithinBudget` (budget 1), `LiveStatusPanelTests.OnlyMountedOnDashboard_Disposes_TimerAndUsesCachedSnapshot`, `AdminStatusCacheTests.OneRefreshRegardlessOfPanelCount`, `AdminAuthTests.BlazorHub_RefusesAnonymous_AndTenantToken`, `AdminProvisioningPagesTests.Retry_PostOnly_Confirm_NoPayloadOrPasswordShown`, `AdminWebsiteClientPagesTests.SecretShownOnce_NeverOnGet`, `AdminFilesPagesTests.Requeue_And_Abandon_OnlyFromMismatch_Audited_NoPathOrContent`, `FileLifecycleTests.AllowedAndForbiddenTransitions_Table` (now includes the 2 new edges), `AdminAuditPagesTests.TenantAudit_RequiresTenant_RangeCap90d_Paged`, `AdminMessagesTests.EveryAdapterErrorCode_HasAMessage`, `InsightIsolationTests.DefaultDisabled_ReturnsInsightNotEnabled`, `…EnabledWithoutConsentAdr_FailsStartup`, `…RequestBounds_RejectedBeforeDb`, `…AlwaysAcquiresReportingLease`, `…NoOtherDbContextInjectableInInsight`, `…CacheKeyHasTenantUserResourceVersion`, `…NotReachableFromTenantRoutes`, `InsightSourceCatalogTests.EmptyAtLaunch`, `InsightMetricsTests.NoMetricRegisteredAtLaunch`.
4. PG-gated (**mandatory G07**): `FileMismatchServiceTests.Requeue_ReHashedByWorker`, `…Abandon_GcsContent`, `AuditQueriesTests.TenantAudit_NeverLeaksOtherTenant`, `InsightReadContextTests.Reporting_Uses_WorkerDataSource_5sTimeout`.
5. `grep -c "INSIGHT_NOT_ENABLED" src/Animora.Domain/Results/ErrorCodes.cs` = 1.

## PROGRESS.md update

At the end of this phase: set the `S20` row in "Phase status" to `✅ Done` (add date + one-line summary), advance **"Next phase" → `G01`** — the **server track is complete**; the client-side sync/release track starts —,
append one `### S20` entry to "Repo state log" (what exists now: pages, island, seam, test classes), log any deviation in the "Decision & deviation log", then commit `feat(server): S20 admin web II — operations pages, live island, insight read-model seam` and sync per the repo git workflow.
