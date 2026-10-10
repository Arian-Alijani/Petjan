# PLAN — Phase S20: Admin Web II (operations pages, live island, insight read-model seam)

> **Self-contained execution plan.** All facts below were verified against the repo on 2026-10-10.
> A fresh model can implement top-to-bottom **without re-exploring** the tree. Source phase file:
> `backend-roadmap/phases/PHASE-S20-admin-web-insight-seams.md` (deleted by the final task T13).
> **PUSH RULE (locked):** after completing EACH task below, run `scripts/git/push.sh "<msg>"`.
> No push = project death. Work on branch `genspark_ai_developer` (first push uses `-b`).

---

## 0. Phase essence (what this phase delivers)

Three things, **no analytics feature is built**:
1. **Operations pages** (Static SSR) for server features that lack UI: provisioning intents + website clients, flagged-file mismatches, audit viewers, system page.
2. **One** `InteractiveServer` island — `LiveStatusPanel` on `/admin` (10 s refresh, bounded circuits). Raises `RenderModePolicy.MaxIslands` 0 → 1.
3. **Insight read-model seam** — port `ITenantInsightQuery` + a **zero-feature** default (`DisabledTenantInsightQuery`, `IsEnabled=false`), a reporting-isolated enabled path (`ReportingInsightQuery`, compiled/registered only when `Insight:Enabled=true`), consent switch via options. **No metric registered, no table, no migration.** Isolation **tests are the real deliverable**.

Out of scope (do NOT build): any metric/chart/aggregate, clinic web client, Redis/SignalR backplane, perf proofs (→G08), PG gates (→G07).

---

## 1. Verified repo facts (do not re-derive)

### Projects / layering
- Insight seam lives in **`Animora.Persistence.PostgreSql`** (`namespace Animora.Persistence.PostgreSql.Insight`). Only this project references Npgsql.
- `Animora.Server` references: `Animora.Web.Admin`, `Animora.Server.Licensing`, `Animora.Domain`, `Animora.Application`, `Animora.Contracts.Wire`, `Animora.Persistence.PostgreSql`. **Never** references client/sqlite/infra.local/desktop.
- `Animora.Web.Admin` references **only** `Animora.Domain` + `FrameworkReference Microsoft.AspNetCore.App`. SDK = `Microsoft.NET.Sdk.Razor`. So ports/pages here can use `Animora.Domain.Results` and `Animora.Web.Admin.Models` only.

### ErrorCodes (`src/Animora.Domain/Results/ErrorCodes.cs`, 762 lines)
- Last region is `#region Files (Phase S17)` ending at line 761 (`#endregion`), after `ValetKeyInvalid` (line 759).
- **ADD** new region after it: `#region Insight (Phase S20)` with exactly one code:
  `public static readonly ErrorCode InsightNotEnabled = new("INSIGHT_NOT_ENABLED", ErrorCode.Entitlement);`
- Acceptance: `grep -c "INSIGHT_NOT_ENABLED" ErrorCodes.cs` = 1.
- `ErrorCode` kinds available: `ErrorCode.Entitlement` (value for this code).

### RenderModePolicy (`src/Animora.Web.Admin/RenderModePolicy.cs`)
- Currently: `public static class RenderModePolicy { public const int MaxIslands=0; }`
- Change to `MaxIslands=1`. **Exactly one** `@rendermode` in whole `src/Animora.Web.Admin` and it must be in `LiveStatusPanel.razor`.

### FileLifecycle (`src/Animora.Persistence.PostgreSql/Files/FileLifecycle.cs`)
- `FileState` enum (`Files/FileState.cs`): `enum FileState : short { Staged, Uploaded, HashVerified, Committed, Available, Abandoned, Mismatch }` → Mismatch = 6, Uploaded = 1, Abandoned = 5.
- Current `Edges` HashSet (9 edges): Staged→Uploaded, Uploaded→HashVerified, Uploaded→Mismatch, HashVerified→Committed, Committed→Available, Committed→Mismatch, Available→Mismatch, Staged→Abandoned, Uploaded→Abandoned.
- **ADD 2 edges**: `(FileState.Mismatch, FileState.Uploaded)`, `(FileState.Mismatch, FileState.Abandoned)`.
- `EnsureAllowed(from,to)` returns `Result.Ok()` if edge present else `Result.Fail(ErrorCodes.FileHashMismatch, "Forbidden file lifecycle transition.")`.
- **Test to edit**: `tests/Animora.Server.Tests/Files/FilePlaneTests.cs` — class `FileLifecycleTests`, test `[Fact] public void AllowedAndForbiddenTransitions_Table()` (lines ~10-19). The `edges` array (lines 14-16) MUST gain the same 2 tuples so the exhaustive `foreach from × foreach to` assertion stays green.

### FileObject (`Files/FileObject.cs`)
- Fields: `TenantId, Id, Sha256?, SizeBytes, DeclaredSizeBytes, ContentType, OriginalName, State, CreatedUtc, UpdatedUtc, CreatedByUserId, UploadId?`.
- Mismatch page shows: tenant code, file id, size, **sha prefix 8 chars** (`Sha256?[..8]`), `UpdatedUtc`. **Never** path, never content, never OriginalName path.

### OperatorAudit (`Operator/OperatorAudit.cs`) — vendor-global, append-only
- Fields: `Id, OccurredUtc, OperatorId?, ActorKind, Action(≤64), TargetTenantId?, Outcome(≤16), CorrelationId(≤64), DetailJson(jsonb)`.
- Factory: `OperatorAudit.Create(DateTimeOffset now, ActorRef actor, string action, string? tenantId, string correlationId, string outcome="success", object? detail=null)`.
- `DetailJson` must carry only allow-listed non-secret metadata (e.g. `new { fileId }`), never request DTOs/passwords.
- New file-action audit action strings: `files.mismatch.requeue`, `files.mismatch.abandon`.
- (Reconciliation worker already writes `files.reconcile.<finding>`; audit written in same DB transaction as state change — mirror that for the 2 new transitions.)

### Provisioning (S15) — `Provisioning/`
- `IProvisioningIntentStore` already has: `ListAsync(ProvisioningIntentStatus? status, int page, int size, ct)` → `IReadOnlyList<IntentListItem>`, and `RetryAsync(string id, ActorRef actor, DateTimeOffset now, ct)` → `Result`.
- `IntentListItem(Id, IdempotencyKey, ClientId, Status, Attempts, ErrorCode?, ReceivedUtc, CompletedUtc?)`.
- `ProvisioningIntentStatus : short { Received=0, Processing=1, Completed=2, Failed=3, Dead=4 }`.
- `WebsiteClientStore` registered scoped (`ProvisioningPersistenceExtensions`). `WebsiteClient(Id, KeyId, ProtectedSecret, Status{Active=0,Revoked=1}, CreatedUtc, LastUsedUtc?, Label)`. Create → secret once / revoke; max-3-active rule → surfaces `VALIDATION_FAILED`.
- **S20 pages reuse the SAME store/services** (`IProvisioningIntentStore`, `WebsiteClientStore`). The `/admin/provisioning` Retry wraps `IProvisioningIntentStore.RetryAsync`.

### Operator context whitelist (source-scan test)
- `tests/Animora.Server.Tests/Operator/ServerSourceScanTests.cs` → `OperatorContext_OnlyUsedInOperatorAndSubscriptionNamespaces`: asserts every `src/Animora.Server/**/*.cs` that mentions `AnimoraOperatorDbContext` is under `Operator/` or `Subscriptions/`. **S20 new audit/provisioning/file adapters must NOT reference `AnimoraOperatorDbContext` directly** — they go through the existing persistence stores/queries. If an adapter needs operator-context reads, add `Admin` to the whitelist in this test (phase §Impl plan step 4 says "extend S14 ServerSourceScanTests whitelist (Insight, Admin)"). Simplest path: keep server adapters delegating to persistence-layer query/command classes (no direct DbContext) → no whitelist edit needed for that test; but the phase explicitly asks to extend whitelist for `Insight`/`Admin`, so add both namespaces to be safe.

### Reporting isolation primitives (S18)
- `src/Animora.Server/Observability/QueryBudget.cs`: `internal enum QueryClass { Oltp, Reporting }`, `internal interface IQueryBudget { ValueTask<QueryLease> Acquire(QueryClass, ct) }`. **INTERNAL to Animora.Server.**
- `TenantScopedMemoryCache<TKey,TValue>` + `TenantCacheKey(tenantId,userId,resource,version)` — **INTERNAL to Animora.Server**, bounded 1000, 60 s sliding TTL, all 4 key parts mandatory (throws on null/whitespace).
- Reporting DB access primitive lives in **persistence**: `src/Animora.Persistence.PostgreSql/Options/ReportingQuerySession.cs`:
  - `public sealed class ReportingQuerySession` (`MaxRows=10000`, `TimeoutSeconds=5`, READ ONLY tx, `statement_timeout='5s'`, worker data source).
  - `public interface IReportingQuerySessionFactory { Task<ReportingQuerySession> OpenAsync(ct) }` + `ReportingQuerySessionFactory` (keyed on `WorkerKey` NpgsqlDataSource).
- **KEY DECISION (resolves the internal-vs-persistence tension):** The insight seam is in persistence, which **cannot** see `Animora.Server`'s internal `IQueryBudget`. Therefore the seam's own reporting budget abstraction must be **defined inside the Insight namespace in persistence**. Introduce a small insight-local budget interface so `ReportingInsightQuery` + isolation tests stay inside persistence:
  - Define `IInsightReportingBudget { ValueTask<ReportingQuerySession> AcquireAsync(CancellationToken ct) }` (persistence, Insight ns) whose default impl wraps `IReportingQuerySessionFactory` with a `SemaphoreSlim(1,1)` → gives the "1 concurrent Reporting lease always acquired" property the test asserts. This keeps the "always acquires a reporting lease" test (§Acceptance 3) fully inside persistence and provable with a stub. The phase's prose "`IQueryBudget.Acquire(QueryClass.Reporting)`" is satisfied semantically (1 concurrent, 5 s, 10 000 rows via `ReportingQuerySession`). Log this as a deviation note in PROGRESS (seam budget abstraction placed in persistence because `IQueryBudget` is `internal` to Animora.Server).

### Admin UI patterns (copy these exactly)
- Pages: `@page "/admin/..."` + `@attribute [Authorize(Policy="AdminOnly")]` + `@inherits AdminPageComponent` + `@inject <Port> X`.
- `AdminPageComponent` (Models/AdminPageComponent.cs): `[CascadingParameter] HttpContext? Http`, `Cancellation => Http?.RequestAborted`, `protected string? Error`, `protected T? Capture<T>(Result<T>)` (sets Error on failure, 412 on concurrency).
- Ports go in `src/Animora.Web.Admin/Ports/AdminPorts.cs` namespace `Animora.Web.Admin.Ports`; all `Task<Result<…>>` with `CancellationToken ct=default`.
- Models record: `AdminPage<T>(IReadOnlyList<T> Items,int Page,int PageSize,long Total,string? Stamp=null)` with `Bounds(page,pageSize)` clamp (pageSize ≤ 200).
- Reusable components: `<FormError Code="@Error" />`, `<Pager Page PageSize Total BaseUrl />`, `<StatusBadge Status />`, `<ConfirmCheckbox>`, `<FormError>`.
- Query params via `[SupplyParameterFromQuery]`. GET filter forms `<form method="get">`; mutating forms `<form method="post">` with `<AntiforgeryToken />`. Secrets: shown **once** in a POST response, never re-shown on GET.
- CSP = `script-src 'self'` only — no inline script, no nonce, no external assets. Blazor framework script served from app (`_framework/blazor.web.js`).
- NavMenu (`Layout/NavMenu.razor`): add links for new pages (`/admin/provisioning`, `/admin/website-clients`, `/admin/files/mismatches`, `/admin/audit`, `/admin/system`).
- `AdminMessages.cs`: auto-maps every `ErrorCode` → default message; test `AdminMessagesTests.EveryAdapterErrorCode_HasAMessage` requires a message for every code an adapter can return. Default fallback already covers unknown codes, but ensure `INSIGHT_NOT_ENABLED` + file-transition codes (`FILE_HASH_MISMATCH`, `VALIDATION_FAILED`) resolve to sensible messages (add explicit branches if the test demands specific text — default fallback likely passes; verify).

### Server DI / host (`src/Animora.Server/`)
- `Admin/AdminServiceCollectionExtensions.cs` → `AddAnimoraAdmin(config)`: builds cookie auth (`OperatorCookie.Scheme/Policy="AdminOnly"`), antiforgery, `AddRazorComponents()`, registers `AdminSession`/`IAdminSession`, `OperatorReadQueries`, `OperatorPlanAdministration`, and scoped adapters (`IAdminStatusReader→AdminStatusReader`, etc).
- `Program.cs`: line 97 `AddAnimoraAdmin`; line 154 `UseMiddleware<AdminSecurityHeadersMiddleware>()`; line 174 `app.MapRazorComponents<Animora.Web.Admin.App>().RequireAuthorization(OperatorCookie.Policy).RequireRateLimiting(RateLimitPolicies.Operator);`.
- Adapters pattern (e.g. `Admin/AdminTenantQueries.cs`): `internal sealed class X(deps) : IPort`, touch `session.OperatorId`, validate lengths, delegate to persistence query/store, map to `Animora.Web.Admin.Models` records, return `Result<...>`.
- `OperatorCookie.Policy` string is **"AdminOnly"**.

### Island specifics (LiveStatusPanel)
- `@rendermode new InteractiveServerRenderMode(prerender: false)`; mounted ONLY on `/admin`.
- Reads **only** `IAdminStatusReader` port — but NOT a DB query per tick. Reads a cached `AdminStatus` snapshot refreshed by one singleton `AdminStatusCache` every 10 s (regardless of panel count). Uses `PeriodicTimer` disposed with component (implement `IDisposable`/`IAsyncDisposable`).
- `AdminStatusCache` = singleton hosted under `WorkerHeartbeat` (register in `Animora.Server`, `PeriodicTimer` 10 s, one refresh regardless of panel count). The panel's `IAdminStatusReader` for the island reads from this cache.
- Program.cs changes: `AddRazorComponents().AddInteractiveServerComponents().AddCircuitOptions(o => { o.DisconnectedCircuitMaxRetained = 2; o.DisconnectedCircuitRetentionPeriod = TimeSpan.FromMinutes(1); o.MaxBufferedUnacknowledgedRenderBatches = 4; })`; `MapRazorComponents<App>()....AddInteractiveServerRenderMode()` inside the authorized `/admin` group so `/_blazor` hub requires `AdminOnly` and refuses anonymous + tenant tokens.

### Insight seam exact contract (persistence, `Insight/` folder)
```csharp
namespace Animora.Persistence.PostgreSql.Insight;   // server-only; never referenced by Domain/Application/clients
public interface ITenantInsightQuery {
    Task<Result<InsightPage<InsightRow>>> QueryAsync(InsightRequest request, CancellationToken ct = default);
    bool IsEnabled { get; }   // false by default
}
public sealed record InsightRequest(string Metric, string? TenantId, DateOnly From, DateOnly To, int Page, int PageSize);
public sealed record InsightRow(string Dimension, string? Label, decimal Value);
public sealed record InsightPage<T>(IReadOnlyList<T> Items, int Page, int PageSize, bool Truncated);
public static class InsightMetrics { public static readonly IReadOnlySet<string> Registered = new HashSet<string>(); } // empty today
public interface IInsightMetricProvider { string Name { get; } Task<Result<IReadOnlyList<InsightRow>>> ExecuteAsync(InsightQueryContext ctx, CancellationToken ct); }
public static class InsightSourceCatalog { /* empty IReadOnlySet<string> of allowed source tables today */ }
```
- `DisabledTenantInsightQuery : ITenantInsightQuery` → `IsEnabled=false`; every `QueryAsync` returns `Result<InsightPage<InsightRow>>.Fail(ErrorCodes.InsightNotEnabled)` **before any DB work**.
- `ReportingInsightQuery : ITenantInsightQuery` (compiled always, registered only when `Insight:Enabled=true`) → `IsEnabled=true`; validate request bounds (PageSize ≤ 200, To-From ≤ 400 days, Metric ∈ `InsightMetrics.Registered`) **before DB**; acquire reporting lease (`IInsightReportingBudget.AcquireAsync`); run provider on worker data source via a read-only **`AnimoraInsightReadContext`** (no `AnimoraServerDbContext`, no tenant business entities; only tables in `InsightSourceCatalog`, empty today); cache via a tenant-scoped cache keyed `(TenantId ?? "*", operatorId, metric, version)`.
- `AnimoraInsightReadContext` = minimal read-only DbContext for projected tables only; **the only DbContext injectable into `Insight/**`** (source scan test asserts this).
- `InsightOptions { bool Enabled=false; string? ConsentAdrReference=null; int CacheTtlSeconds=60; }` section `"Insight"`. `InsightOptionsValidator`: **fails startup** if `Enabled==true && string.IsNullOrWhiteSpace(ConsentAdrReference)` (consent is startup contract). Cross-tenant aggregation requires both `Enabled` + `ConsentAdrReference`.
- `AddAnimoraInsight()` extension (in `src/Animora.Server/Insight/InsightServiceCollectionExtensions.cs`): binds `InsightOptions` + validator; registers `DisabledTenantInsightQuery` by default; registers `ReportingInsightQuery` + `AnimoraInsightReadContext` + budget only when `Enabled=true`. `AddInsightMetric<T>()` extension for future providers.
- **Dashboard** shows one status row: `Insight: disabled (no consent ADR)` — add a tiny `IAdminInsightStatus` port returning enabled/consent flags.

---

## 2. Files to create / modify (authoritative)

| # | Path | Action |
|---|---|---|
| ErrorCodes | `src/Animora.Domain/Results/ErrorCodes.cs` | edit: +region Insight, +1 code |
| Render | `src/Animora.Web.Admin/RenderModePolicy.cs` | edit: MaxIslands=1 |
| FileLifecycle | `src/Animora.Persistence.PostgreSql/Files/FileLifecycle.cs` | edit: +2 edges |
| FileLifecycle test | `tests/Animora.Server.Tests/Files/FilePlaneTests.cs` | edit: +2 edges in `edges[]` |
| Insight seam | `src/Animora.Persistence.PostgreSql/Insight/*.cs` | new: ITenantInsightQuery, InsightRequest, InsightRow, InsightPage, InsightMetrics, IInsightMetricProvider, InsightQueryContext, InsightSourceCatalog, AnimoraInsightReadContext, DisabledTenantInsightQuery, ReportingInsightQuery, IInsightReportingBudget(+impl), InsightOptions, InsightOptionsValidator |
| File mismatch svc | `src/Animora.Persistence.PostgreSql/Files/FileMismatchService.cs` | new: Requeue (Mismatch→Uploaded) + Abandon (Mismatch→Abandoned), operator ctx, audit in same tx, paged read of Mismatch rows |
| Provisioning queries | `src/Animora.Persistence.PostgreSql/Provisioning/ProvisioningQueries.cs` | new (read-only paged) — may reuse `IProvisioningIntentStore.ListAsync` |
| Audit queries | `src/Animora.Persistence.PostgreSql/Audit/AuditQueries.cs` | new: operator audit (paged, filter), tenant security audit (explicit TenantId, AsNoTracking, range ≤90 d, DetailJson>2KiB truncated) |
| Admin ports | `src/Animora.Web.Admin/Ports/AdminPorts.cs` | edit: + IAdminProvisioningQueries, IAdminProvisioningCommands, IAdminWebsiteClients, IAdminFileQueries, IAdminFileCommands, IAdminAuditQueries, IAdminInsightStatus + supporting Models |
| Admin models | `src/Animora.Web.Admin/Models/AdminModels.cs` | edit: + records for intents/clients/mismatches/audit rows/insight status + forms |
| Pages | `src/Animora.Web.Admin/Pages/{Provisioning,WebsiteClients,FileMismatches,Audit,System}.razor` | new |
| Island | `src/Animora.Web.Admin/Components/LiveStatusPanel.razor` | new (only @rendermode) |
| NavMenu | `src/Animora.Web.Admin/Layout/NavMenu.razor` | edit: +links |
| Dashboard | `src/Animora.Web.Admin/Pages/Dashboard.razor` | edit: mount `<LiveStatusPanel/>`, show insight status row |
| AdminMessages | `src/Animora.Web.Admin/AdminMessages.cs` | edit if needed (ensure new codes have messages) |
| Server adapters | `src/Animora.Server/Admin/*Adapter.cs` | new: provisioning, website-clients, files, audit, insight-status adapters |
| Status cache | `src/Animora.Server/Admin/AdminStatusCache.cs` | new singleton + island reader |
| Admin DI | `src/Animora.Server/Admin/AdminServiceCollectionExtensions.cs` | edit: register new adapters + InteractiveServerComponents + CircuitOptions |
| Insight DI | `src/Animora.Server/Insight/InsightServiceCollectionExtensions.cs` | new: AddAnimoraInsight |
| Program | `src/Animora.Server/Program.cs` | edit: AddAnimoraInsight, AddInteractiveServerRenderMode on /admin group, status cache hosted svc |
| Source-scan | `tests/Animora.Server.Tests/Operator/ServerSourceScanTests.cs` | edit: whitelist Insight/Admin if needed |
| Tests | `tests/Animora.Server.Tests/Admin/*.cs`, `tests/Animora.Server.Tests/Insight/*.cs` | new (see task T11/T12) |
| PROGRESS | `backend-roadmap/PROGRESS.md` | edit at end (T13-prep) |

**No migration** (Insight has no table). Do NOT pick a migration number.

---

## 3. Environment (do not probe)
- Build/test/run ONLY via sandbox scripts (never raw dotnet):
  `scripts/sandbox/build.sh fast` (quick), `scripts/sandbox/build.sh full` (0/0 required for acceptance), `scripts/sandbox/test.sh Server`, `scripts/sandbox/run.sh` (→ `SMOKE RUN OK`). Run `scripts/sandbox/install-prereqs.sh` once (idempotent) if the first build fails for missing tools.
- `dotnet-ef` at `.sandbox/tools/dotnet-ef` (unused this phase — no migration).
- **Do NOT run full test suite repeatedly.** Use targeted `test.sh` filters (e.g. `Insight`, `FileLifecycle`, `Admin`). Full build/test only at the final verification task.

---

## 4. TASK LIST (push after EACH task)

> Status legend: `[ ]` pending · `[~]` in progress · `[x]` done. Commit msg suggestions in backticks.

- [ ] **T0 — Branch + plan committed.** Ensure on `genspark_ai_developer`; this plan file is in `Plan/`. Push with `-b`.
  `scripts/git/push.sh -b genspark_ai_developer "docs(plan): S20 execution plan"`

- [ ] **T1 — ErrorCodes +1 & RenderModePolicy.** Add `#region Insight (Phase S20)` + `InsightNotEnabled ("INSIGHT_NOT_ENABLED", Entitlement)` after the Files region (`#endregion` line 761). Set `MaxIslands=1`. Verify `grep -c INSIGHT_NOT_ENABLED` = 1.
  `scripts/git/push.sh "feat(server): S20 add InsightNotEnabled code, raise island budget to 1"`

- [ ] **T2 — FileLifecycle +2 edges & test.** Add `(FileState.Mismatch, FileState.Uploaded)` and `(FileState.Mismatch, FileState.Abandoned)` to `Edges` in `FileLifecycle.cs` AND to the `edges[]` array in `FilePlaneTests.cs` `AllowedAndForbiddenTransitions_Table`. Run `scripts/sandbox/test.sh FileLifecycle`.
  `scripts/git/push.sh "feat(server): S20 FileLifecycle Mismatch->Uploaded|Abandoned transitions"`

- [ ] **T3 — Insight seam contract (persistence, no DB logic yet).** Create in `src/Animora.Persistence.PostgreSql/Insight/`: `ITenantInsightQuery.cs`, `InsightRequest.cs`, `InsightRow.cs`, `InsightPage.cs`, `InsightMetrics.cs` (empty set), `IInsightMetricProvider.cs` + `InsightQueryContext.cs`, `InsightSourceCatalog.cs` (empty set), `InsightOptions.cs` + `InsightOptionsValidator.cs`, `DisabledTenantInsightQuery.cs` (returns `InsightNotEnabled`). All `sealed`/`sealed record`, nullable honored, `ConfigureAwait(false)` for async, `CancellationToken ct=default`. Build fast.
  `scripts/git/push.sh "feat(server): S20 insight read-model seam contract + disabled default"`

- [ ] **T4 — Insight enabled path (persistence).** Create `IInsightReportingBudget.cs` (+ default impl wrapping `IReportingQuerySessionFactory` with `SemaphoreSlim(1,1)`), `AnimoraInsightReadContext.cs` (read-only, projected tables only — empty DbSet set today, worker data source via keyed `WorkerKey`), `ReportingInsightQuery.cs` (validate bounds before DB, acquire lease, run provider, cache). Use an insight-local tenant-scoped cache keyed `(TenantId ?? "*", operatorId, metric, version)` — implement a tiny internal cache in persistence (do NOT depend on Server's internal `TenantScopedMemoryCache`). Build fast.
  `scripts/git/push.sh "feat(server): S20 insight enabled reporting path (isolated read context + budget)"`

- [ ] **T5 — FileMismatchService (persistence).** Operator-context read of `FileObjects` where `State=Mismatch` (paged, `AsNoTracking`, projection: TenantId, Id, SizeBytes, Sha256 prefix 8, UpdatedUtc — never path/content). Commands: `RequeueAsync` (Mismatch→Uploaded via `FileLifecycle.EnsureAllowed`, write `files.mismatch.requeue` OperatorAudit in same tx) + `AbandonAsync` (Mismatch→Abandoned, audit `files.mismatch.abandon`). Return `Result`.
  `scripts/git/push.sh "feat(server): S20 file mismatch requeue/abandon service with operator audit"`

- [ ] **T6 — Provisioning + Audit read queries (persistence).** `Provisioning/ProvisioningQueries.cs` (paged list reusing `IProvisioningIntentStore.ListAsync`; detail excludes payload/password). `Audit/AuditQueries.cs`: operator audit (paged, filter action/tenant/date, `QueryClass.Reporting`-equivalent for unfiltered count via reporting session), tenant security audit (requires explicit TenantId predicate, `AsNoTracking`, range ≤90 d, truncate DetailJson >2 KiB). Build fast.
  `scripts/git/push.sh "feat(server): S20 provisioning + audit read-only paged queries"`

- [ ] **T7 — Admin ports + models (Web.Admin).** Extend `Ports/AdminPorts.cs`: `IAdminProvisioningQueries.ListAsync(status?,page,pageSize)` + `IAdminProvisioningCommands.RetryAsync(id,confirm)`; `IAdminWebsiteClients` (List/Create→secret once/Revoke); `IAdminFileQueries.ListMismatchesAsync` + `IAdminFileCommands.RequeueAsync/AbandonAsync`; `IAdminAuditQueries` (operator + tenant tabs); `IAdminInsightStatus.GetAsync` (enabled/consent). Add matching Models records/forms in `Models/AdminModels.cs`. Build fast (Web.Admin only references Domain — ports stay DTO-only).
  `scripts/git/push.sh "feat(server): S20 admin ports + view models for ops pages"`

- [ ] **T8 — Admin pages + NavMenu + Dashboard.** Create `Pages/{Provisioning,WebsiteClients,FileMismatches,Audit,System}.razor` (Static SSR, S19 conventions: `[Authorize(AdminOnly)]`, `AdminPageComponent`, GET filter + POST confirm forms, `<FormError>`/`<Pager>`/`<ConfirmCheckbox>`; secrets shown once; no payload/password/path/content ever rendered; audit tenant tab requires choosing a tenant + range ≤90 d). Add NavMenu links. Edit `Dashboard.razor`: mount `<LiveStatusPanel/>` and an "Insight: disabled (no consent ADR)" status row. Build fast.
  `scripts/git/push.sh "feat(server): S20 operations pages (provisioning, clients, files, audit, system)"`

- [ ] **T9 — LiveStatusPanel island + AdminStatusCache.** `Components/LiveStatusPanel.razor` — the ONLY `@rendermode new InteractiveServerRenderMode(prerender:false)`, `PeriodicTimer` 10 s disposed with component, reads cached snapshot from `IAdminStatusReader` backed by `AdminStatusCache`. XML-doc: single-instance only, no backplane (A-RULE 031). Create `src/Animora.Server/Admin/AdminStatusCache.cs` (singleton hosted service, one 10 s refresh regardless of panel count). Verify `grep -rn "@rendermode" src/Animora.Web.Admin | wc -l` = 1.
  `scripts/git/push.sh "feat(server): S20 LiveStatusPanel island + singleton AdminStatusCache"`

- [ ] **T10 — Server adapters + DI wiring.** Create adapters in `src/Animora.Server/Admin/` for each new port (delegate to persistence stores/queries; touch `session.OperatorId`; map to Models; validate bounds). Create `src/Animora.Server/Insight/InsightServiceCollectionExtensions.cs` (`AddAnimoraInsight` + `AddInsightMetric<T>`). Edit `AdminServiceCollectionExtensions.cs`: register new adapters + `AddInteractiveServerComponents().AddCircuitOptions(...)`. Edit `Program.cs`: call `AddAnimoraInsight`, add `AddInteractiveServerRenderMode()` on the authorized `/admin` MapRazorComponents group, register `AdminStatusCache` hosted service. If any server adapter references `AnimoraOperatorDbContext`, add `Admin` to `ServerSourceScanTests` whitelist. Build **full** (0/0).
  `scripts/git/push.sh "feat(server): S20 wire admin adapters, insight DI, interactive server island host"`

- [ ] **T11 — Insight isolation tests.** `tests/Animora.Server.Tests/Insight/*.cs`: `InsightIsolationTests` — DefaultDisabled_ReturnsInsightNotEnabled; EnabledWithoutConsentAdr_FailsStartup; RequestBounds_RejectedBeforeDb (PageSize>200 / range>400 d / unregistered metric); AlwaysAcquiresReportingLease (stub budget asserts acquisition); NoOtherDbContextInjectableInInsight (source scan of `Insight/**` → only `AnimoraInsightReadContext`); NotReachableFromTenantRoutes; CacheKeyHasTenantUserResourceVersion. Plus `InsightSourceCatalogTests.EmptyAtLaunch`, `InsightMetricsTests.NoMetricRegisteredAtLaunch`. Run `scripts/sandbox/test.sh Insight`.
  `scripts/git/push.sh "test(server): S20 insight isolation + source-scan + bounds tests"`

- [ ] **T12 — Admin/host tests.** `tests/Animora.Server.Tests/Admin/*.cs`: RenderModePolicyTests.NoGlobalRenderMode_AndIslandCountWithinBudget (budget 1); LiveStatusPanelTests.OnlyMountedOnDashboard_Disposes_TimerAndUsesCachedSnapshot; AdminStatusCacheTests.OneRefreshRegardlessOfPanelCount; AdminAuthTests.BlazorHub_RefusesAnonymous_AndTenantToken; AdminProvisioningPagesTests.Retry_PostOnly_Confirm_NoPayloadOrPasswordShown; AdminWebsiteClientPagesTests.SecretShownOnce_NeverOnGet; AdminFilesPagesTests.Requeue_And_Abandon_OnlyFromMismatch_Audited_NoPathOrContent; AdminAuditPagesTests.TenantAudit_RequiresTenant_RangeCap90d_Paged; AdminMessagesTests.EveryAdapterErrorCode_HasAMessage. Run `scripts/sandbox/test.sh Admin`. Then **final gate**: `scripts/sandbox/build.sh full` (0/0) + `scripts/sandbox/run.sh` (`SMOKE RUN OK`) + verify acceptance greps: `@rendermode` count=1, `grep -rn "DbContext" Insight | grep -v AnimoraInsightReadContext` empty, `grep -c INSIGHT_NOT_ENABLED` =1.
  `scripts/git/push.sh "test(server): S20 admin pages/island/auth tests + full build green"`

- [ ] **T13 — PROGRESS.md update + plan cleanup (FINAL).** Edit `backend-roadmap/PROGRESS.md`: set S20 row to `✅ Done` (date + 1-line summary), advance "Next phase" → `G01` (server track complete; client sync/release track starts), append one `### S20` entry to "Repo state log" (pages, island, seam, test classes), log the insight-budget-in-persistence deviation in "Decision & deviation log". **Then delete `backend-roadmap/phases/PHASE-S20-admin-web-insight-seams.md` AND delete this plan file `Plan/S20-PLAN.md`.** Do not touch other roadmap/phase files.
  `scripts/git/push.sh "feat(server): S20 admin web II — operations pages, live island, insight read-model seam"`

---

## 5. Acceptance checklist (verify at T12/T13)
1. `scripts/sandbox/build.sh full` = 0/0; `scripts/sandbox/run.sh` = `SMOKE RUN OK`; client sources untouched.
2. `grep -rn "@rendermode" src/Animora.Web.Admin | wc -l` = 1 (in LiveStatusPanel.razor); `MaxIslands` = 1; `grep -rn "DbContext" src/Animora.Persistence.PostgreSql/Insight | grep -v AnimoraInsightReadContext` empty.
3. All pure/host tests in T11/T12 pass.
4. PG-gated tests (G07, NOT run here): FileMismatchServiceTests, AuditQueriesTests, InsightReadContextTests — author them so they **skip** without `ANIMORA_PG_TEST_CONNECTION` (CONVENTIONS §1), do not block this phase.
5. `grep -c "INSIGHT_NOT_ENABLED" src/Animora.Domain/Results/ErrorCodes.cs` = 1.

## 6. Hard rules (binding)
- Layering: Domain refs nothing; Persistence.PostgreSql is the only Npgsql project; Server never refs client/sqlite/infra.local/desktop.
- C#: `sealed` default, `sealed record` DTOs, nullable honored (no `!` without why), every async public method `CancellationToken ct=default` + `ConfigureAwait(false)` in libraries, no `.Result`/`.Wait()`, expected failures → `Result`/`Result<T>` not exceptions, `IClock`/`TimeProvider` injected (no `DateTime.Now`), ULID ids via `IdGenerator`.
- EF: EF Core 10, no lazy loading, reads `AsNoTracking()` + projections, paging ≤200, migrations additive (none here), no `EnsureCreated` outside tests.
- Security: authN→authZ→entitlement separate; TenantId server-side only; secrets never in config/logs; append-only audit for every security/financial action; fail closed.
- Reporting isolation: insight never on OLTP tx/business context; cache key carries tenant+user+resource+version; reporting lease mandatory.
- Web: one island max, no global interactivity, no backplane, no external assets, CSP `script-src 'self'`.
