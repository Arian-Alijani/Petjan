# Phase S19 — Admin web I — operator panel core (Blazor Web App, Static-SSR-first): auth shell, dashboard, tenants, subscriptions, plans, licences

**Band:** M · **Track:** S (server) · **Depends on:** S16, S18 · **Next:** S20
**Satisfies:** R-B05, R-B06, R-B29 · **Runnable after this phase:** desktop offline yes · server required no
**Produced by:** M00 (regenerated 2026-10-09; regenerates scope-inventory row E01 (Track-D/E planning files deleted in the 2026-10-08 server-track reset)).

> Baseline verified 2026-10-09: client migration head `031` (`031_TreatmentPlans`), `CommandTypes` = live `CommandTypes.cs` (88 declared today — never freeze), `ErrorCodes` 161 (+ codes added by earlier S phases), `WireContract.Version` = 1 (S06 bumps to 2, supported {1,2}); `src/` projects = `Animora.Domain`, `Animora.Application`, `Animora.Persistence.Sqlite`, `Animora.Infrastructure.Local`, `Animora.Client.Services`, `Animora.Contracts`, `Animora.Contracts.Wire`, `Animora.Desktop`, `Animora.Sync.Abstractions` (+ server-track projects added by earlier S/G phases).
> No migration in this phase (pages read/act through S14–S18 services; the operator cookie is stateless + validated against `OperatorAccounts.SecurityStamp`).

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
- `STACK.md` §3.2 (render modes, circuit cost) and `ARCHITECTURE.md` §7.3 (Static SSR default + selective `InteractiveServer` islands, never global), §13.5, §4 (surfaces), §16.2 — only these `§`.
- `src/Animora.Server/Operator/{OperatorAuth,OperatorAccountService,OperatorEndpoints}.cs`, `src/Animora.Persistence.PostgreSql/Subscriptions/{ISubscriptionService,ITenantAdministration,ISeatUsageReader}.cs`, `src/Animora.Persistence.PostgreSql/Licensing/{LicenseIssuer,LicenseStore}.cs` (S14/S16 — adapters call these, pages never do).
- `src/Animora.Server/Observability/{ServerMetrics,QueryBudget}.cs`, `src/Animora.Server/Hosting/IReadinessProbe.cs`, `src/Animora.Server/Workers/WorkerHeartbeat.cs` (S18/S13 — dashboard reads these in-process).

(Nothing else. `Animora-UI/**`, `Database-Sampel/**`, other phase files and `windows-frontend-roadmap/phases/**` are out of bounds.)

## Goal

Ship the vendor's **operator admin site** hosted by `Animora.Server` under `/admin` as a **Blazor Web App in Static SSR by default** (no circuit per page; forms post and re-render): cookie-based operator sign-in (separate from tenant bearer and from the S14 operator bearer API), a dashboard (system status, expiring subscriptions, dead-lettered work), tenant list/detail with every S14 subscription action (extend/reduce/suspend/reinstate/cancel/renew/seats/plan, owner password reset), plan management, and licence management (list/revoke/recheck, key rotation). **Pages hold no business logic** — they call small ports implemented in `Animora.Server` over the exact services the S14/S16 operator API uses (one path, ARCH §13.4). No clinic-facing web client, no analytics (ROADMAP §5).

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
// S14 / S16 / S18 / S13 seams the ADAPTERS (Animora.Server/Admin/*) call — exact names, reuse
namespace Animora.Persistence.PostgreSql.Subscriptions;
public interface ISubscriptionService { /* Extend/Reduce/Suspend/Reinstate/Cancel/Renew/ChangeSeats/ChangePlan(tenantId, ifMatchStamp, …, ActorRef) : Task<Result<SubscriptionView>> — each = one tx + SubscriptionEvent + OperatorAudit + ILicenseIssuanceScheduler */ }
public interface ITenantAdministration { Task<Result> SetStatusAsync(string tenantId, TenantStatus status, string reason, ActorRef actor, CancellationToken ct = default); /* + ResetOwnerPasswordAsync(tenantId, ActorRef) → one-time password */ }
public interface ISeatUsageReader { Task<int> CountActiveSeatsAsync(string tenantId, CancellationToken ct = default); }
public sealed record ActorRef(ActorKind Kind, string? ActorId);      // ActorKind { Operator, WebsiteIntent, System }
namespace Animora.Domain.Subscriptions;  public enum SubscriptionStatus { Trial, Active, GracePeriod, Suspended, Cancelled, Expired }   // Plan(Id, Code, Name, DefaultSeatLimit, MonthlyAmountMinor, Currency, TrialDays, IsActive)
// S14 operator identity: OperatorAccount(Id, UserName, NormalizedUserName, PasswordHash, SecurityStamp, Status, FailedAttempts, LockoutEndUtc?, MustChangePassword);  OperatorAccountService (login verify w/ dummy-verify, lockout 5/15 min, change-password);
//   OperatorAuth.Scheme = "Animora.Operator" (bearer, API);  CoarsePolicies.OperatorOnly;  OperatorTenantContext (IsOperator=true);  OperatorContextMiddleware;  TenantContextMiddleware/EntitlementMiddleware skip ServerRoutes.Operator
// S16: LicenseIssuer / LicenseStore (list per tenant, Revoke(tenantId, licenseId, reason, ActorRef)), LicenseGrantProjector.ProjectAsync(tenantId), SigningKeyStore + rotation (`POST /operator/licensing/keys/rotate` logic), Licenses/LicenseGrants tables
// S18: ServerMetrics (Meter "Animora.Server": animora.commands.settled|in_progress, animora.outbox.pending|dead_lettered, animora.worker.last_success_age.seconds, animora.pg.pool.*, animora.sync.*, animora.files.reconcile.*); IQueryBudget; TenantScopedMemoryCache; KestrelProfile
// S13: WorkerHeartbeat (name → last success UTC);  S01: IReadinessProbe { string Name; Task<ReadinessResult> CheckAsync(ct) }, ServerRoutes.Admin = "/admin", RateLimitPolicies.Operator ("operator", 30/min), GlobalExceptionMiddleware
// S14: AnimoraOperatorDbContext (vendor-global tables; only from Operator/Subscriptions/Licensing/Admin namespaces)
```

## §Design decisions

- **Projects.** New `src/Animora.Web.Admin` (`Microsoft.NET.Sdk.Razor`, **Razor class library**, `FrameworkReference Microsoft.AspNetCore.App`; refs `Animora.Domain` only). It contains pages, layout, components, **view models and the ports** (`Animora.Web.Admin.Ports`); it references **no** Npgsql, EF Core, `Animora.Persistence.*`, `Animora.Application` or `Animora.Server*` project (`WebAdminLayeringTests`; ARCH §6 "hosted by `Animora.Server`"). `Animora.Server` references it and supplies the adapters in `src/Animora.Server/Admin/*` (this is where services are called). Static assets live in `src/Animora.Web.Admin/wwwroot` (**self-hosted CSS/JS only — no CDN, no web fonts from the Internet, no CSS/JS framework package**; ≤ 30 KiB CSS) and are served via `MapStaticAssets`.
- **Render-mode policy (binding, ARCH §7.3).** `Routes`/`App` use **no global render mode** (Static SSR). Interactivity is allowed **only** as named islands with an explicit `@rendermode InteractiveServer` on the *component*, and S19 ships **zero** islands (S20 may add exactly one, `LiveStatusPanel`). `AddInteractiveServerComponents` is registered only with `CircuitOptions { MaxRetainedDisconnectedCircuits = 2, DisconnectedCircuitRetentionPeriod = 1 min }` when an island exists (S20); in S19 only `AddRazorComponents()` is called. Test `RenderModePolicyTests.NoGlobalRenderMode_AndIslandCountWithinBudget` scans `*.razor` for `@rendermode` and asserts `Routes.razor`/`App.razor`/`MainLayout.razor` contain none and the island count ≤ the phase budget (S19: 0, constant `RenderModePolicy.MaxIslands`, raised to 1 by S20). Forms use enhanced SSR forms (`<EditForm FormName=… method="post">` + `[SupplyParameterFromForm]`, antiforgery token automatic); `[PersistentState]` is not needed without islands.
- **Operator cookie auth (browser) — a third, separate scheme.** `OperatorCookie.Scheme = "Animora.Operator.Cookie"`: cookie name `__Host-animora-op` (Secure, HttpOnly, `SameSite=Strict`, Path `/`, **absolute 30 min, no sliding**, Data-Protection-protected from the S02 key ring). Claims `sub` (operator id), `ost` (security stamp), `role=operator`. `OnValidatePrincipal` re-reads `OperatorAccounts` by PK and rejects when `SecurityStamp ≠ ost`, status ≠ Active or `MustChangePassword` (then only `/admin/account/change-password` + logout are reachable) — **revocation is immediate**. Policy `AdminOnly` = authenticated on that cookie scheme **and** `OperatorOnly` semantics (`ITenantContext.IsOperator` via `OperatorContextMiddleware` extended to `/admin/**`); `TenantContextMiddleware`/`EntitlementMiddleware` skip `ServerRoutes.Admin` (S14 edit pattern). Login = minimal endpoint `POST /admin/account/login` (antiforgery-validated form post, `OperatorAccountService.VerifyAsync` — same lockout/constant-time behavior, **same generic failure text**), `POST /admin/account/logout`, `POST /admin/account/change-password`; rate-limit policy `operator`. Every admin mutation requires the POST antiforgery token **and** the current cookie; GET never mutates. Unauthenticated → 302 to `/admin/login` (never leaks whether a path exists). Response headers for `/admin/**`: `Cache-Control: no-store`, `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`, CSP `default-src 'self'; frame-ancestors 'none'; form-action 'self'` (no inline script; a nonce only if S20 adds the island's framework script). Secrets (temp passwords) are shown **once** in the POST response page and never placed in a URL/query string/log.
- **Ports (`Animora.Web.Admin.Ports`, all methods take `CancellationToken`; return `Result<T>`; paging via `AdminPage<T>(Items, Page, PageSize, Total)` with `PageSize ≤ 200`):** `IAdminStatusReader` (`GetStatusAsync` → `AdminStatus(Readiness: list(name, healthy|degraded|unhealthy), Workers: list(name, lastSuccessAgeSeconds), Counters: {commandsInProgress, outboxPending, outboxDeadLettered, intakeInProgress}, Pg: {poolInUse, poolMax}, Version, UptimeSeconds)`), `IAdminTenantQueries` (`ListAsync(TenantListQuery(status?, search?, page, pageSize))`, `GetAsync(id)` → `TenantDetail(core, subscription(+ConcurrencyStamp), seatsUsed, last 20 events, devices count, licences summary)`), `IAdminSubscriptionCommands` (the 8 S14 operations + `ResetOwnerPassword`, each taking `ifMatchStamp`; plus `CreateTenantAsync(CreateTenantForm, idempotencyKey)` whose adapter calls S14's `ProvisioningEngine.ProvisionAsync` with `ActorRef(Operator, id)` — the only provisioning call site besides S14's operator endpoint and S15's worker), `IAdminPlanCommands` (`ListAsync`, `CreateAsync`, `DeactivateAsync`), `IAdminLicenseCommands` (`ListAsync(tenantId)`, `RevokeAsync`, `RecheckAsync`, `RotateKeyAsync`, `GetKeySetAsync`), `IAdminSession` (current operator id/name, for display + audit actor). Adapters live in `Animora.Server/Admin/*Adapter.cs`, each a thin mapping onto the S14/S16/S18 services (**no SQL, no business rules, `ActorRef(Operator, operatorId)` always from the cookie principal**). `OperatorAudit` is written by the underlying services (already same-transaction); adapters add nothing.
- **Pages (all Static SSR, English LTR, keyboard-accessible, semantic tables, no client-side sorting):** `/admin` Dashboard (status table, "expiring in 14 days", "past due / grace", "dead-lettered outbox" count linking to tenant list filters; **polling is not used** — a manual Refresh link; the S20 island is the only live part), `/admin/tenants` (filter by status + search by code/name, paged), `/admin/tenants/{id}` (summary; subscription card with action forms — each action is its own `<form method="post">` handler with a confirmation checkbox for suspend/cancel/reduce; shows `ConcurrencyStamp`-based `If-Match` as a hidden field → `412` message "someone else changed this, reload"), `/admin/tenants/new` (calls the **provisioning engine** through `IAdminSubscriptionCommands.CreateTenantAsync` with an auto-generated idempotency key stored in the form so a double submit provisions once), `/admin/plans`, `/admin/tenants/{id}/licenses` (list, revoke with reason, recheck), `/admin/licensing/keys` (public key set, rotate with confirmation), `/admin/account/*`. Each page file ≤ 150 lines; shared `Pager`, `StatusBadge`, `ConfirmCheckbox`, `FormError` components; localisation is **not** needed (operator is the vendor).
- **Failure & safety.** Any adapter `Result` failure renders a `FormError` with the `ErrorCode.Code` and a short fixed message table (`AdminMessages`, unit-tested to cover every ErrorCode the adapters can return); unexpected exceptions go through `GlobalExceptionMiddleware` (HTML error page for `/admin/**` with correlation id, no stack trace). Large lists never load unbounded (paging mandatory). No page issues a cross-tenant query other than through `AnimoraOperatorDbContext`-backed services.
- **Performance budget.** Admin is vendor-only and low-traffic; each page ≤ 3 DB round-trips; dashboard counters come from in-process meters/heartbeats plus **one** bounded SQL for "expiring" and "dead-lettered" counts. The `/admin` surface is covered by `RateLimitPolicies.Operator` and does not add a circuit.
- **New ErrorCodes:** none (reuse S14/S16 codes). New server options: `AdminOptions { SessionMinutes = 30, RequireHttps = true }` (section `Admin`).

## §Implementation plan

1. Create `src/Animora.Web.Admin` (Razor SDK csproj; `_Imports.razor`, `Routes.razor`, `App`-level pieces consumed by the host, `Layout/MainLayout.razor` + `NavMenu.razor`, `Components/{Pager,StatusBadge,ConfirmCheckbox,FormError}.razor`, `Pages/{Dashboard,Login,ChangePassword,Tenants,TenantDetail,TenantNew,Plans,TenantLicenses,LicensingKeys}.razor`, `Ports/*.cs`, `Models/*.cs`, `AdminMessages.cs`, `RenderModePolicy.cs`, `wwwroot/admin.css`); add to `Animora.sln`.
2. Server: `src/Animora.Server/Admin/{AdminServiceCollectionExtensions,OperatorCookie,AdminAccountEndpoints,AdminSecurityHeadersMiddleware,AdminStatusReader,AdminTenantQueries,AdminSubscriptionAdapter,AdminPlanAdapter,AdminLicenseAdapter,AdminSession,AdminOptions}.cs`; edit `Program.cs` (add `AddRazorComponents()`, `AddCascadingAuthenticationState` not needed, `UseAntiforgery()` after auth/authz, `MapRazorComponents<…>().AddAdditionalAssemblies(typeof(Animora.Web.Admin.Ports.IAdminSession).Assembly)` under a group `RequireAuthorization("AdminOnly")` with `/admin/login` + `/admin/account/*` anonymous-allowed), `OperatorContextMiddleware` (extend to `/admin`), `TenantContextMiddleware`/`EntitlementMiddleware` (skip `/admin`), `SecretGuard` (no new key). Antiforgery cookie name `__Host-animora-af`.
3. Tests (below). `scripts/sandbox/build.sh full` → 0/0; `scripts/sandbox/test.sh Server` → green; `scripts/sandbox/run.sh` → `SMOKE RUN OK` (desktop unaffected).
4. `PROGRESS.md` per recipe; commit.

## Files to create / modify

| Path | Contents |
|---|---|
| `src/Animora.Web.Admin/**` (new RCL) | pages, layout, components, ports, models, messages, policy constant, css |
| `src/Animora.Server/Admin/**` | cookie scheme, account endpoints, security headers, port adapters, options, DI |
| `src/Animora.Server/Program.cs`, `Operator/OperatorContextMiddleware.cs`, `Authorization/{TenantContextMiddleware,EntitlementMiddleware}.cs`, `Animora.Server.csproj`, `Animora.sln` (edit) | wiring |
| `tests/Animora.Server.Tests/Admin/*.cs`, `tests/Animora.Server.Tests/Animora.Server.Tests.csproj` (edit — adds bUnit-free SSR tests via `WebApplicationFactory` + string assertions; **no new package**) | tests |

## Contracts / schema added

Project `Animora.Web.Admin` (+ `Animora.Web.Admin.Ports`); scheme `Animora.Operator.Cookie`; policy `AdminOnly`; routes under `/admin` (`ServerRoutes.Admin`); `AdminOptions`; no table, no wire DTO, no ErrorCodes.

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
- **Web (STACK §3.2 / ARCH §7.3):** Static SSR default; interactivity only as named islands within `RenderModePolicy.MaxIslands`; no global interactivity; no external asset hosts (zero-Internet runtime).
- **Operator surface:** admin cookie ≠ tenant bearer ≠ operator API bearer; browser state-changing calls are POST + antiforgery; pages call ports only.

## Out of scope (owning phase)

- Operations pages (provisioning intents, website clients, file mismatches, audit viewer), the single live island and the insight seam port → **S20**.
- Clinic-facing web client, cross-tenant analytics/charts, MFA/passkeys, e-mail → deferred (ROADMAP §5).
- Metrics export/dashboards beyond in-process counters → **S18** (done) / G08 evidence.

## Acceptance criteria (mechanical)

1. `scripts/sandbox/build.sh full` = 0/0; `scripts/sandbox/run.sh` = `SMOKE RUN OK`; client sources unchanged.
2. `grep -rn "Npgsql\|EntityFrameworkCore\|Animora.Persistence\|Animora.Application\|Animora.Server" src/Animora.Web.Admin --include=*.csproj --include=*.cs --include=*.razor` returns nothing; `grep -rn "https\?://" src/Animora.Web.Admin --include=*.razor --include=*.css --include=*.cs` returns nothing except `http://www.w3.org` XML namespaces (no CDN/external host).
3. `grep -rn "@rendermode" src/Animora.Web.Admin | wc -l` = 0 (S19 island budget) and `Routes.razor`, `MainLayout.razor` contain no `rendermode`.
4. Pure/host tests pass: `RenderModePolicyTests.NoGlobalRenderMode_AndIslandCountWithinBudget`, `WebAdminLayeringTests.RclReferencesOnlyDomain`, `AdminAuthTests.Anonymous_RedirectsToLogin_ForEveryAdminRoute`, `…TenantBearerToken_CannotAccessAdmin`, `…OperatorApiBearer_CannotAccessAdmin_AndCookie_CannotCallOperatorApi`, `…Login_WrongPasswordAndUnknownUser_SameMessage`, `…Lockout_After5Failures`, `…SecurityStampChange_InvalidatesCookieImmediately`, `…MustChangePassword_OnlyChangePasswordReachable`, `…Cookie_IsHostPrefixed_HttpOnly_SameSiteStrict_Absolute30Min`, `AdminAntiforgeryTests.PostWithoutToken_Rejected_ForEveryMutation`, `AdminAntiforgeryTests.GetNeverMutates`, `AdminHeadersTests.NoStore_CSP_FrameDeny_Nosniff`, `AdminAdaptersTests.ActorAlwaysFromCookiePrincipal`, `AdminAdaptersTests.IfMatchMismatch_Renders412Message`, `AdminAdaptersTests.CreateTenant_DoubleSubmit_SameIdempotencyKey_OneTenant`, `AdminAdaptersTests.TempPassword_ShownOnce_NotInUrlOrLogs`, `AdminMessagesTests.EveryAdapterErrorCode_HasAMessage`, `AdminPagesTests.Lists_Paged_Max200`, `AdminPagesTests.Dashboard_ReadsInProcessCountersOnly`, `AdminStatusReaderTests.DegradedByNameOnly_NoSecrets`.
5. PG-gated (**mandatory G07**): `AdminEndToEndTests.OperatorCreatesTenant_SuspendsIt_TenantRequestsGet403_ReinstateRestores`, `…RevokeLicence_DeviceBlockedAtServerGate`, `…CookieRevokedByOperatorPasswordChange`.

## PROGRESS.md update

At the end of this phase: set the `S19` row in "Phase status" to `✅ Done` (add date + one-line summary), advance **"Next phase" → `S20`**,
append one `### S19` entry to "Repo state log" (what exists now: projects, routes, test classes), log any deviation in the "Decision & deviation log", then commit `feat(server): S19 admin web I — operator panel core (Static SSR, cookie auth, tenants/subscriptions/licences)` and sync per the repo git workflow.
