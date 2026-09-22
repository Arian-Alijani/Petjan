# Phase D04 — Tenant context & authorization

**Band:** M · **Track:** D · **Depends on:** D03 · **Satisfies:** R-B04, R-B19, R-B20
**Runnable after this phase:** desktop offline yes · server required no

## Read before starting
- `backend-roadmap/CONVENTIONS.md` §2, §3, §4, §7, §8, §9, §12
- `backend-roadmap/PROGRESS.md` (D02 entry — `ITenantContext`, named filters,
  `TenantContextMissingException`, `TENANT_MISMATCH`; D03 entry — schemes, `/me`, memberships,
  `IServerAuditWriter`; B04 entry — `IPermissionEvaluator`, `PermissionCodes`,
  `AuthorizationSubject`, `AuthorizationDecision`, `AuthorizationPolicies`,
  `IResourceAuthorizationRule<T>`)
- `ARCHITECTURE.md` §9.2, §9.3, §10.1, §10.3, §10.7 · `STACK.md` §6.4
- `A-RULE 012`, `A-RULE 013`, `A-RULE 023`, `A-RULE 033`
- `src/Animora.Application/Security/**`, `src/Animora.Application/Ports/ITenantContext.cs`

## Goal
Every authenticated server request resolves its `TenantId` **from authenticated membership only**,
and every protected endpoint passes the same three-layer authorization check the client uses
(RBAC → policy → resource) through the **same** `IPermissionEvaluator` B04 introduced. Isolation
layer 1 (application authorization) and layer 2 (data-access filter) are both live and proven with
database hardening off.

## Design decisions (pre-made — implement, do not re-derive)
- **Tenant resolution is server-side, membership-derived, and never client-supplied**
  (A-RULE 012). `src/Animora.Server/Tenancy/TenantResolutionMiddleware.cs`, placed **after**
  `UseAuthentication()` and **before** endpoint execution:
  1. If the request is anonymous or in the `/health/*`, `/api/identity/login|refresh|first-run`
     allow-list → no tenant context is established and the middleware does nothing.
  2. Otherwise read the candidate tenant from the **`X-Animora-Tenant` header** *as a selector
     only*, then verify a `StaffMembership` row exists for `(authenticated user, that tenant)` with
     `MembershipStatus.Active`. Verified → establish the context. Not verified → `403` with
     `TENANT_FORBIDDEN` **and** a security audit entry.
  3. If the header is absent and the user has exactly one active membership → that tenant. If the
     user has several and no header → `400` `TENANT_REQUIRED` (never a silent "first membership").
  4. If a request body or query string contains a `tenantId` field at all, it is **ignored** and an
     audit entry records the attempt (`CONVENTIONS.md` §7).
  The selector header is deliberately *not* trusted: it chooses among memberships, it never grants
  one. A test asserts that forging the header for a non-member tenant fails.
- **`HttpTenantContext`** (`src/Animora.Server/Tenancy/HttpTenantContext.cs`) implements D02's
  `ITenantContext` — **scoped**, readable-only, set exactly once per request by the middleware via
  an internal `ITenantContextSetter`. Any second set attempt throws
  `InvalidOperationException` (a defect, not an expected failure). Outside a request (a worker) the
  worker sets it explicitly per unit of work through the same setter — D09 uses that path; a worker
  that forgets throws `TenantContextMissingException` from D02. Never an `AsyncLocal` fallback that
  could leak a prior request's tenant across a pooled thread.
- **Membership cache, tenant-safe and bounded** (A-RULE 033, R-B35):
  `src/Animora.Server/Tenancy/MembershipCache.cs` over `IMemoryCache` with
  key = `$"mem:{userId}:{tenantId}:{securityStamp}:v1"` and a 5-minute sliding window bounded by
  `SizeLimit`. The **security stamp is part of the key**, so a role change, password change or
  membership revocation invalidates the entry automatically instead of relying on eviction. A test
  asserts every key component is present (a missing component is a **test failure**, not a bug
  report — `CONVENTIONS.md` §8).
- **Authorization reuses the Application layer, it does not reimplement it** (the single most
  important decision here): `src/Animora.Server/Security/ServerAuthorizationHandler.cs` builds an
  `AuthorizationSubject` (user id, tenant id, device id, permission codes from `RoleDefinition`) and
  calls B04's `IPermissionEvaluator`. ASP.NET Core's authorization pipeline is only the *host*:
  - `PermissionRequirement(string PermissionCode)` +
    `PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>` → delegates to
    `IPermissionEvaluator.Evaluate(...)`.
  - `AnimoraPolicies` (`src/Animora.Server/Security/AnimoraPolicies.cs`) — a static registration
    that turns **every** `PermissionCodes` constant into a named policy
    (`policy:<code>`) at startup, so an endpoint writes
    `.RequireAuthorization(AnimoraPolicies.For(PermissionCodes.SalesInvoicePost))` and a typo is a
    **compile-time** error, never a silently-open endpoint.
  - `RequireOperator` — one extra policy for the cross-tenant operator surface (D10 consumes it);
    it requires the `Operator` claim **and** the absence of a tenant context, so an operator token
    can never act inside a tenant's data view by accident.
- **Fail-closed default:** a fallback policy (`AuthorizationOptions.FallbackPolicy`) requires an
  authenticated user, so a newly added endpoint is protected **by default** and must opt out
  explicitly with `.AllowAnonymous()`. `EndpointAuthorizationAuditTests` enumerates every endpoint
  in the routing table and fails if one is anonymous without being in a named allow-list — the
  machine-checkable form of "unknown = deny" (`CONVENTIONS.md` §7).
- **Resource-based layer:** `src/Animora.Server/Security/ResourceAuthorizationService.cs` —
  `Task<Result> AuthorizeResourceAsync<T>(T resource, string permissionCode, CancellationToken ct)`
  resolves B04's `IResourceAuthorizationRule<T>` implementations and runs them after the RBAC/policy
  layers. Rules registered here: `SameTenantRule<T>` (the resource's `TenantId` must equal the
  ambient one — the belt to the query filter's braces) and `OwnCashSessionRule` (a cashier may act
  only on their own open session, mirroring B04's client-side rule so the two never diverge).
  RBAC alone is **never** sufficient for a financial operation (`ARCHITECTURE.md` §10.3).
- **Three planes stay separate** (A-RULE 023): the middleware order is fixed and documented as a
  numbered comment in `Program.cs` —
  `UseAuthentication()` → `TenantResolutionMiddleware` → `UseAuthorization()` →
  `EntitlementMiddleware` (**registered as a no-op pass-through here**, replaced by D11/E07).
  Registering the placeholder now means no later phase has to insert a middleware into a hot
  pipeline — it only replaces an implementation.
- **`ICurrentUserProvider` server implementation:**
  `src/Animora.Server/Security/HttpCurrentUserProvider.cs` builds B01's `CommandContext` /
  `QueryContext` from the authenticated principal + tenant context + `X-Animora-Device` header, so
  D05's command intake reuses the **exact** application pipeline the client uses. `OperationId` is
  taken from the command envelope (never regenerated server-side — A-RULE 005/006).
- **Correlation:** the middleware adds `TenantId`, `UserId`, `DeviceId` and `RequestId` to the
  Serilog `LogContext` and to B05's `AmbientCorrelation`, so a single transaction is traceable
  client → server → worker (`ARCHITECTURE.md` §15, R-B29).
- **New error codes:** `TENANT_FORBIDDEN`, `TENANT_REQUIRED`, `OPERATOR_SCOPE_REQUIRED`
  (`PERMISSION_DENIED` and `TENANT_MISMATCH` already exist — reuse).

## Files to create / modify
| Path | Action | Contents (one line) |
|---|---|---|
| `src/Animora.Server/Tenancy/TenantResolutionMiddleware.cs` | create | membership-verified tenant resolution |
| `src/Animora.Server/Tenancy/HttpTenantContext.cs` | create | scoped, set-once `ITenantContext` |
| `src/Animora.Server/Tenancy/ITenantContextSetter.cs` | create | the only write path (request + worker) |
| `src/Animora.Server/Tenancy/MembershipCache.cs` | create | bounded, stamp-keyed membership cache |
| `src/Animora.Server/Security/PermissionRequirement.cs` | create | requirement + handler over `IPermissionEvaluator` |
| `src/Animora.Server/Security/AnimoraPolicies.cs` | create | one policy per `PermissionCodes` + `RequireOperator` |
| `src/Animora.Server/Security/ResourceAuthorizationService.cs` | create | resource-rule layer |
| `src/Animora.Server/Security/HttpCurrentUserProvider.cs` | create | builds `CommandContext`/`QueryContext` |
| `src/Animora.Server/Security/EntitlementMiddleware.cs` | create | no-op placeholder (D11/E07 replace) |
| `src/Animora.Server/Program.cs` | modify | the fixed 4-step security pipeline + fallback policy |
| `src/Animora.Application/Security/AuthorizationPolicies.cs` | modify | add the two server-side resource rules' registration hook |
| `tests/Animora.Server.Tests/TenantResolutionTests.cs` | create | header-as-selector, forged header, ambiguity, ignored body field |
| `tests/Animora.Server.Tests/AuthorizationPolicyTests.cs` | create | RBAC + policy + resource layering, fail-closed default |
| `tests/Animora.Server.Tests/EndpointAuthorizationAuditTests.cs` | create | every endpoint protected or allow-listed |
| `tests/Animora.Server.Tests/MembershipCacheTests.cs` | create | key composition, stamp invalidation, bound |

## Implementation steps
1. Add `ITenantContextSetter` + `HttpTenantContext`; register scoped; delete D02's test double from
   the production graph (it stays in the test project only).
2. Write `MembershipCache` with the full key composition and a `SizeLimit`.
3. Write `TenantResolutionMiddleware` implementing the four resolution rules plus the audit hooks.
4. Write `PermissionRequirement` + handler delegating to `IPermissionEvaluator`; generate one policy
   per `PermissionCodes` constant in `AnimoraPolicies`; add `RequireOperator`.
5. Write `ResourceAuthorizationService` and register `SameTenantRule<T>` + `OwnCashSessionRule`.
6. Write `HttpCurrentUserProvider`; write the no-op `EntitlementMiddleware`.
7. Wire the fixed pipeline order in `Program.cs`, set the fallback policy, apply the policy to the
   existing D03 endpoints (`/me`, `/password`, `/devices/register` become authorized; `login`,
   `refresh`, `first-run`, `health` are the explicit allow-list).
8. Add the correlation enrichment.
9. Write the four test classes.
10. Verify: a request with a forged `X-Animora-Tenant` for a non-member tenant is `403` + audited; a
    user with two memberships and no header is `400`; a cashier cannot post to another cashier's
    session even with the right permission code.
11. `build.sh full` = 0/0; `test.sh` = `TESTS OK`; desktop `run.sh` = `SMOKE RUN OK`.

## Contracts / schema added
- Server: `TenantResolutionMiddleware`, `HttpTenantContext`, `ITenantContextSetter`,
  `MembershipCache`, `PermissionRequirement` + handler, `AnimoraPolicies`,
  `ResourceAuthorizationService`, `HttpCurrentUserProvider`, `EntitlementMiddleware` (placeholder).
- Application: `SameTenantRule<T>`, `OwnCashSessionRule` registered through
  `AuthorizationPolicies`.
- Headers (documented contract): `X-Animora-Tenant` (selector), `X-Animora-Device`.
- `ErrorCodes` += `TENANT_FORBIDDEN`, `TENANT_REQUIRED`, `OPERATOR_SCOPE_REQUIRED`.
- No migration, no `Animora.Contracts` change, no `CommandTypes` entry, no DI change in
  `App.axaml.cs`. `WireContract.Version` stays `1`.

## Rules that apply here
- **A-RULE 012 / R-B19** — `TenantId` never comes from client input; the header selects among
  **verified** memberships and a forged value is refused and audited.
- **A-RULE 013** — isolation layer 1 (authorization) and layer 2 (D02's filter) are both active and
  independent; neither substitutes for the other.
- **A-RULE 023 / R-B20** — authentication (D03), authorization (here) and entitlement (placeholder,
  D11/E07) are three separate middlewares in a fixed order; none short-circuits another.
- **A-RULE 033 / R-B35** — the membership cache key includes tenant + user + resource + version
  (security stamp), and the cache is size-bounded.
- **`ARCHITECTURE.md` §10.3** — RBAC alone never authorizes a financial action; the resource layer
  always runs.
- **CONVENTIONS §7** — defaults fail closed: the fallback policy requires authentication and
  `EndpointAuthorizationAuditTests` enforces it.

## Out of scope
- Any business endpoint or command handler → D05/D06/D07.
- Real entitlement evaluation inside `EntitlementMiddleware` → D11 (server) / E07 (client).
- Datasync `IAccessControlProvider<T>` tenant scoping (isolation layer 3) → D08.
- Operator account creation and the operator API surface → D10.
- SQL RLS → deferred (E08 gate only).
- Any desktop-side change → E07.

## Acceptance criteria
- Build: `scripts/sandbox/build.sh full` = 0 warnings / 0 errors.
- `TenantResolutionTests` green (or skipped with reason): a member's header resolves; a non-member's
  header → `403` `TENANT_FORBIDDEN` + one audit row; two memberships with no header → `400`
  `TENANT_REQUIRED`; one membership with no header resolves silently; a `tenantId` field in the body
  is ignored and audited.
- `AuthorizationPolicyTests` green: a missing permission → `PERMISSION_DENIED`; a correct permission
  but wrong-tenant resource → denied by `SameTenantRule`; a cashier acting on another cashier's
  session → denied by `OwnCashSessionRule`; an operator token inside a tenant context →
  `OPERATOR_SCOPE_REQUIRED`.
- `EndpointAuthorizationAuditTests` green: every mapped endpoint either carries an authorization
  policy or appears in the explicit anonymous allow-list (`login`, `refresh`, `first-run`,
  `health/live`, `health/ready`).
- `MembershipCacheTests` green: the key contains user + tenant + security stamp + version; changing
  the security stamp produces a cache miss; the cache honors its `SizeLimit`.
- Tenant isolation still holds **with RLS off** — D02's `TenantIsolationTests` still green with the
  real `HttpTenantContext` in place.
- Desktop unaffected: `scripts/sandbox/run.sh` = `SMOKE RUN OK`.

## PROGRESS.md update
Mark D04 done; advance "Next phase" to `D05`; append a "Repo state" entry listing:
`TenantResolutionMiddleware` + the four resolution rules + the `X-Animora-Tenant`
**selector-not-grant** semantics (**every later Track D/E phase relies on it**), `HttpTenantContext`
+ `ITenantContextSetter` (**D09 workers set it explicitly**), `MembershipCache` key composition,
`AnimoraPolicies.For(...)` compile-safe policy naming (**D05–D12 use it on every endpoint**),
`RequireOperator` (**D10/E01 use it**), `ResourceAuthorizationService` + the two rules,
`HttpCurrentUserProvider` (**D05 builds contexts through it**), the no-op `EntitlementMiddleware`
placeholder (**D11/E07 replace, never insert**), the fail-closed fallback policy + endpoint audit
test, the fixed 4-step pipeline order, the 3 new error codes, deviations.
