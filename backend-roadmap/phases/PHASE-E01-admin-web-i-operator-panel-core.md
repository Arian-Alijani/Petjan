# Phase E01 — Admin web I: operator panel core

**Band:** M · **Track:** E · **Depends on:** D11 · **Satisfies:** R-B05
**Runnable after this phase:** desktop offline yes · server required yes (for the admin site only)

## Read before starting
- `backend-roadmap/CONVENTIONS.md` §1, §2, §3, §4, §7, §8, §9, §12
- `backend-roadmap/PROGRESS.md` (D03 entry — the cookie scheme `IdentityConstants.ApplicationScheme`
  and its hardening; D04 entry — `AnimoraPolicies.RequireOperator`, `HttpCurrentUserProvider`;
  D10 entry — `ISubscriptionOperations`, `OperatorContext`, `SubscriptionQueries`, the
  `/api/operator/*` routes and every `*View` record, `OperatorAccountService`, the `Operator` role;
  D11 entry — `ILicenseIssuer`, `LicenseEndpoints`, `LicenseIssuance`; D01 entry — `Program.cs`
   startup order, `ServerOptions`, `GlobalExceptionMiddleware`; D13 entry — `TenantCache`,
  `RateLimiting`, `AnimoraMetrics`)
- `ARCHITECTURE.md` §7.2, §7.3, §13.1, §10.1 · `STACK.md` §3.2, §5.1, §16 (zero-CDN packaging)
- `A-RULE 023`, `A-RULE 033`, `T-RULE 005`
- `src/Animora.Server/Program.cs`, `src/Animora.Server/Endpoints/OperatorEndpoints.cs`,
  `src/Animora.Server/Security/AnimoraPolicies.cs`

## Goal
The product owner gets a real operator website: `Animora.Web.Admin`, a Blazor Web App hosted **inside**
`Animora.Server`, Static-SSR by default with three named `InteractiveServer` islands, authenticated by
the D03 cookie scheme and authorized by `AnimoraPolicies.RequireOperator`. It manages tenants,
subscriptions, seats and licences by calling D10/D11 **application services in-process** — never its own
HTTP client, never its own business logic. Every asset ships in the package; nothing loads from a CDN.

## Design decisions (pre-made — implement, do not re-derive)
- **Project:** `src/Animora.Web.Admin/Animora.Web.Admin.csproj`, SDK `Microsoft.NET.Sdk.Razor`,
  `net10.0`. References `Animora.Application`, `Animora.Contracts.Wire`, `Animora.Domain`. It
  **does not** reference `Animora.Persistence.SqlServer` and **does not** reference
  `Animora.Contracts` (that is the desktop seam). `Animora.Server` references
  `Animora.Web.Admin`; the reverse is forbidden (`LayeringRulesTests` gains the fact).
  No new package pin is needed — Blazor ships in the ASP.NET Core shared framework.
- **Hosting model — one process, one port, two surfaces.** `Program.cs` gains, in the D01 documented
  order, after step 6: `builder.Services.AddRazorComponents().AddInteractiveServerComponents();`
  and in the pipeline `app.MapStaticAssets(); app.MapRazorComponents<Admin>()
  .AddInteractiveServerRenderMode();` mapped under the path base **`/admin`** via
  `ServerOptions.AdminPathBase = "/admin"`. The Business API (`/api/*`), replication (`/tables/*`)
  and diagnostics (`/diagnostics/*`) keep their own prefixes; `AdminRouteTests` asserts `/admin/**`
  never reaches an API endpoint and `/api/**` never renders a component.
- **Render-mode policy (`ARCHITECTURE.md` §7.3, T-RULE 005) — exactly three interactive islands,
  named and justified in code comments; everything else is Static SSR:**
  | Island | Why interactive |
  |---|---|
  | `Components/Islands/TenantSearchIsland.razor` | per-keystroke tenant filter |
  | `Components/Islands/SeatAssignmentIsland.razor` | assign/release without a full page post |
  | `Components/Islands/LiveAlertsIsland.razor` | polls `/diagnostics/outbox` every 15 s for the alert badge |
  `@rendermode InteractiveServer` appears **only** in those three files —
  `RenderModePolicyTests` greps the razor tree and fails on a fourth. Global interactivity is
  forbidden: a circuit per operator session is a weak-server cost (`STACK.md` §3.2).
  `[PersistentState]` is used on the tenant list model so the prerender → interactive handoff on the
  search page does not re-query.
- **Authentication.** Cookie only (`IdentityConstants.ApplicationScheme`, D03). Login page
  `Components/Pages/Account/Login.razor` posts to a **new** minimal-API endpoint
  `POST /admin/auth/login` in `src/Animora.Server/Endpoints/AdminAuthEndpoints.cs`, which calls the
  existing `ServerAuthenticationService` with `persistCookie: true` and issues **no bearer token** —
  the browser surface never receives one. `POST /admin/auth/logout` signs out.
  `AddAntiforgery()` + `app.UseAntiforgery()` is registered (required by Razor Components) and every
  form posts the token. Login is rate-limited with D13's `auth` policy (D13 §rate-limit defines exactly five policies — `auth`, `commands`, `tables`, `files`, `operator` — and `auth` is the one covering `/login` + `/refresh`; there is no `identity` policy).
- **Authorization.** `Components/_Imports.razor` carries
  `@attribute [Authorize(Policy = AnimoraPolicies.RequireOperator)]`; the login page is the single
  `[AllowAnonymous]` exception. `AuthorizationTests` asserts a tenant-user cookie (has
  `StaffMembership`, no `Operator` role) gets `403` on every `/admin` route — the operator surface
  is not reachable by a clinic user even with a valid cookie (A-RULE 023: authentication is not
  authorization).
- **Data access is in-process, through D10/D11 ports only.** Pages inject `ISubscriptionOperations`,
  `SubscriptionQueries`, `ILicenseIssuer`, `IServerAuditWriter`. An `OperatorContext` is built per
  request by `src/Animora.Web.Admin/Services/OperatorContextFactory.cs` from
  `HttpCurrentUserProvider` + `AmbientCorrelation` + a fresh `IdGenerator.NewId()` as
  `RequestOperationId`, so every mutation the panel performs is idempotent and audited exactly like
  an API call. **No `HttpClient` is registered in this project** (`AdminLayeringTests` asserts it):
  calling your own process over HTTP would double the work on a weak server.
- **Pages (Static SSR unless noted):**
  | Route | Content |
  |---|---|
  | `/admin` | dashboard stub: tenant count, active/suspended/grace counts, expiring-licence count, alert badge (`LiveAlertsIsland`). Rich metrics are E02. |
  | `/admin/tenants` | `PagedResult<TenantSummaryView>` grid + `TenantSearchIsland` |
  | `/admin/tenants/new` | form → `CreateTenantRequestV1` |
  | `/admin/tenants/{tenantId}` | `SubscriptionView` + `BillingPeriodView` list + `SeatView` list (`SeatAssignmentIsland`) + `SubscriptionEventView` log + licence history |
  | `/admin/tenants/{tenantId}/subscription/{action}` | POST-only handlers for `extend`/`renew`/`seats`/`suspend`/`resume`/`cancel`, each mapped 1:1 to the D10 operation of the same name |
  | `/admin/tenants/{tenantId}/license` | current `LicenseIssuance` + `Issue` / `Reissue` buttons → `ILicenseIssuer`; shows `KeyId`, `NotAfter`, `InstallationBindingHash` prefix (first 8 chars only) |
  | `/admin/plans` | plan catalog read + create/retire |
  | `/admin/alerts` | `PagedResult<OperatorAlertView>` (D09 `OperatorAlerts`) |
  | `/admin/operators` | operator list + create (via `OperatorAccountService`) |
  All grids page at **25 rows** through the existing `PagedResult<T>`; no page ever materializes an
  unbounded list (`CONVENTIONS.md` §8).
- **Destructive actions require typed confirmation.** `Components/Shared/ConfirmAction.razor` — a
  Static-SSR form that requires the operator to type the tenant's name for `suspend`/`cancel`/
  `seats-reduce`/`license-reissue`, and always requires a free-text `Reason` that is forwarded into
  the D10 request record and therefore into the append-only `SubscriptionEvent` log. A blank reason
  is a validation failure, not a silent empty string.
- **Error surface.** `Components/Shared/ResultAlert.razor` renders a `Result`/`WireApiError`
  `ErrorCode` through `src/Animora.Web.Admin/Resources/OperatorErrorText.cs` — an English
  code→message map (the admin panel is operator-facing and **English only**; the Persian/RTL UI
  belongs to the desktop and is untouched). An unmapped code renders the raw code plus the
  correlation id, never a stack trace. `GlobalExceptionMiddleware` (D01) still owns unhandled faults.
- **Zero-CDN, packaged assets (`STACK.md` §16).** `wwwroot/` holds the panel's own `app.css` (plain
  CSS, no framework download) and nothing else; Blazor framework assets are served by
  `MapStaticAssets` from the build output. `AssetIsolationTests` scans every `.razor`, `.css`,
  `.html` and the build output for `http://`, `https://`, `cdn`, `fonts.googleapis`,
  `unpkg`, `jsdelivr` and fails on a hit. This is the E09 zero-Internet test's admin half.
- **Security headers** for `/admin/**` only, in
  `src/Animora.Web.Admin/Security/AdminSecurityHeaders.cs` (middleware):
  `Content-Security-Policy: default-src 'self'; frame-ancestors 'none'; base-uri 'self'`,
  `X-Content-Type-Options: nosniff`, `Referrer-Policy: no-referrer`,
  `Cross-Origin-Opener-Policy: same-origin`. No `unsafe-inline`: the one required Blazor script is
  referenced by path, never inlined.
- **Auditing.** Every mutating page handler writes one `ServerAuditEntry` through
  `IServerAuditWriter` with `Actor = operator user id`, `Action = "admin.<page>.<action>"`,
  `TargetTenantId`, `Reason`, `CorrelationId` — **before** returning the redirect, in the same
  operation as the D10 call's own event log entry (D10 already writes the subscription event; this
  records that the *admin surface* was the actor).
- **Readiness.** `/health/ready` gains an `admin` check: the component assembly loads and the
  `Operator` role exists. Nothing in the desktop path is affected.

## Files to create / modify
| Path | Action | Contents (one line) |
|---|---|---|
| `src/Animora.Web.Admin/Animora.Web.Admin.csproj` | create | Razor SDK project, 3 project references |
| `src/Animora.Web.Admin/Components/Admin.razor` + `Routes.razor` + `_Imports.razor` | create | root component, router, global `[Authorize]` |
| `src/Animora.Web.Admin/Components/Layout/*.razor` | create | shell layout + nav (Static SSR) |
| `src/Animora.Web.Admin/Components/Pages/**/*.razor` | create | the 9 routes above |
| `src/Animora.Web.Admin/Components/Islands/TenantSearchIsland.razor` | create | interactive island 1 |
| `src/Animora.Web.Admin/Components/Islands/SeatAssignmentIsland.razor` | create | interactive island 2 |
| `src/Animora.Web.Admin/Components/Islands/LiveAlertsIsland.razor` | create | interactive island 3 |
| `src/Animora.Web.Admin/Components/Shared/ConfirmAction.razor` | create | typed confirmation + reason |
| `src/Animora.Web.Admin/Components/Shared/ResultAlert.razor` | create | `ErrorCode` → operator text |
| `src/Animora.Web.Admin/Services/OperatorContextFactory.cs` | create | builds `OperatorContext` per request |
| `src/Animora.Web.Admin/Resources/OperatorErrorText.cs` | create | English code→message map |
| `src/Animora.Web.Admin/Security/AdminSecurityHeaders.cs` | create | CSP + hardening headers |
| `src/Animora.Web.Admin/wwwroot/app.css` | create | the panel's only stylesheet |
| `src/Animora.Server/Endpoints/AdminAuthEndpoints.cs` | create | `/admin/auth/login` + `/logout` |
| `src/Animora.Server/Program.cs` | modify | Razor Components, antiforgery, static assets, `/admin` map, headers, `admin` readiness check |
| `src/Animora.Server/Configuration/ServerOptions.cs` | modify | `AdminPathBase` |
| `Animora.sln` | modify | add `Animora.Web.Admin` |
| `tests/Animora.Server.Tests/AdminRouteTests.cs` | create | route isolation + anonymous redirect |
| `tests/Animora.Server.Tests/AdminAuthorizationTests.cs` | create | operator vs tenant-user vs anonymous |
| `tests/Animora.Server.Tests/RenderModePolicyTests.cs` | create | exactly 3 islands, no global interactivity |
| `tests/Animora.Server.Tests/AssetIsolationTests.cs` | create | zero external asset reference |
| `tests/Animora.Domain.Tests/LayeringRulesTests.cs` | modify | Web.Admin references + no `HttpClient` |

## Implementation steps
1. Create the project with the three references; add it to `Animora.sln`; add `AdminPathBase`.
2. Add `AddRazorComponents().AddInteractiveServerComponents()`, `AddAntiforgery()`,
   `MapStaticAssets()`, `MapRazorComponents<Admin>().AddInteractiveServerRenderMode()` under
   `/admin`, and `UseAntiforgery()` at the D01-documented pipeline position.
3. Write `AdminAuthEndpoints` (cookie login/logout over `ServerAuthenticationService`) and the login
   page; apply the `auth` rate-limit policy (D13's login/refresh limiter).
4. Write the layout, `_Imports.razor` with the global `RequireOperator` attribute, and
   `OperatorContextFactory`.
5. Write the tenant list + search island + `[PersistentState]` handoff.
6. Write the tenant detail page (subscription, periods, seats + island, event log, licence history).
7. Write the six subscription action handlers, each through `ISubscriptionOperations`, each behind
   `ConfirmAction` with a mandatory reason, each writing a `ServerAuditEntry`.
8. Write the licence page over `ILicenseIssuer` (issue / reissue), the plans page, the alerts page
   and the operators page.
9. Write `AdminSecurityHeaders`, `OperatorErrorText`, `ResultAlert`, `app.css`; add the `admin`
   readiness check.
10. Write the four test classes; extend `LayeringRulesTests`.
11. Verify manually against a real SQL Server: operator login → create tenant → extend → reduce
    seats below assigned (rejected) → suspend with reason → issue licence → the event log and audit
    trail both show the action.
12. `build.sh full` = 0/0; `test.sh` = `TESTS OK`; desktop `run.sh` = `SMOKE RUN OK`.

## Contracts / schema added
- Project `Animora.Web.Admin` (components, 3 islands, `OperatorContextFactory`,
  `OperatorErrorText`, `AdminSecurityHeaders`).
- Server: `AdminAuthEndpoints` (2 routes), `ServerOptions.AdminPathBase`, the Razor Components
  registration, the `admin` readiness check.
- **No migration, no new wire contract, no `Animora.Contracts` change, no `CommandTypes` entry, no
  `ErrorCodes` addition, no DI change in `App.axaml.cs`.** `WireContract.Version` stays `1`.
- The panel adds **zero** business logic: every mutation is an existing D10/D11 operation.

## Rules that apply here
- **A-RULE 023 / R-B20** — cookie authentication, `RequireOperator` authorization and licence
  entitlement stay three independent checks; the panel never infers one from another.
- **T-RULE 005 / `ARCHITECTURE.md` §7.3** — Static SSR is the default; exactly three interactive
  islands exist, each justified by per-keystroke or live interaction, asserted by a test.
- **`STACK.md` §16 / R-B27** — every asset is packaged; the asset scan proves no external fetch.
- **A-RULE 033 / R-B35** — any cached read uses D13's `TenantCache` + `CacheKey.For`; the operator
  scope is part of the key, so an operator's cached page can never be served to a tenant user.
- **`ARCHITECTURE.md` §13.1 / R-B30** — the panel is an *authorized client* of the Subscription
  context and a *requester* of Licensing; it never signs a licence or bypasses `ILicenseIssuer`.
- **CONVENTIONS §8** — 25-row paging on every grid; no unbounded query; one circuit only inside the
  three islands.
- **CONVENTIONS §2** — `Animora.Web.Admin` never references a persistence project and holds no
  `HttpClient`.

## Out of scope
- Operator dashboards with real usage metrics, the tenant-insight read-model seam and charts → E02.
- Any cross-tenant *business* data (stock, patients, revenue) → deferred (ROADMAP §5), seam in E02.
- A clinic-facing web client → deferred (ROADMAP §5).
- File download UI for diagnostics bundles → E02 (it renders D13's `POST /diagnostics/bundle`).
- Desktop-side licence consumption and entitlement gating → E07.

## Acceptance criteria
- Build: `scripts/sandbox/build.sh full` = 0 warnings / 0 errors.
- `AdminRouteTests` green: anonymous `/admin/tenants` redirects to `/admin/auth/login`;
  `/admin/**` never resolves an API endpoint and `/api/**` never renders a component;
  `/admin` responds while `/tables/*` and `/api/commands` keep working unchanged.
- `AdminAuthorizationTests` green: operator cookie = `200`; tenant-user cookie = `403` on every
  `/admin` route; a bearer token alone (no cookie) = `401` on `/admin`.
- `RenderModePolicyTests` green: `@rendermode InteractiveServer` occurs in exactly the three island
  files; no `AddInteractiveServerRenderMode` applied globally to `Routes.razor`.
- `AssetIsolationTests` green: no `.razor`/`.css`/`.html`/build-output reference to an external host.
- `LayeringRulesTests` green: `Animora.Web.Admin` references only Application/Domain/Contracts.Wire;
  `Animora.Web.Admin` does not reference `Animora.Persistence.SqlServer`, `Animora.Contracts`, or
  any `System.Net.Http` client registration.
- Manual (real SQL Server): create tenant → extend → seat reduction below assigned rejected with
  `SEAT_REDUCTION_BLOCKED` → suspend requires typed name + reason → licence issue/reissue succeeds
  and appears in `LicenseIssuances`; each action produced one `SubscriptionEvent` **and** one
  `ServerAuditEntry` carrying the same `CorrelationId`.
- `/health/ready` reports the `admin` check.
- Desktop unaffected: `scripts/sandbox/run.sh` = `SMOKE RUN OK`; no file under
  `src/Animora.Desktop/` changed.

## PROGRESS.md update
Mark E01 done; advance "Next phase" to `E02`; append a "Repo state" entry listing: the
`Animora.Web.Admin` project + its reference set, the `/admin` path base and `ServerOptions.AdminPathBase`,
`AdminAuthEndpoints` (cookie-only), the global `RequireOperator` attribute, the **three** named
interactive islands and the policy test that caps them (**E02 must reuse them, not add a fourth**),
`OperatorContextFactory` (**E02 uses it**), the 9 routes, `ConfirmAction` + the mandatory-reason rule,
`OperatorErrorText`, `AdminSecurityHeaders` + the CSP value, the zero-CDN asset scan (**E09's
zero-Internet test extends it**), the `admin` readiness check, the extended layering facts, deviations.
