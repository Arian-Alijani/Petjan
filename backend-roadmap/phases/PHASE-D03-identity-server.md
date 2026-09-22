# Phase D03 — Identity server

**Band:** M · **Track:** D · **Depends on:** D02 · **Satisfies:** R-B04, R-B20
**Runnable after this phase:** desktop offline yes · server required no (the desktop still logs in
locally via B03; it does not yet call the server — E07 wires that)

## Read before starting
- `backend-roadmap/CONVENTIONS.md` §2, §3, §4, §5, §7, §9, §12
- `backend-roadmap/PROGRESS.md` (D01 entry — key ring + `SecretGuard` + health contract; D02 entry —
  `AnimoraServerDbContext`, `ITenantContext`, `TenantProvisioner`, migration `102_ServerCoreSchema`;
  A03/B03 entries — `User`, `StaffMembership`, `RoleDefinition`, `PermissionCodes`,
  `DefaultRolePermissions`, `PasswordPolicy`)
- `ARCHITECTURE.md` §10.1, §10.2, §10.4, §10.6 · `STACK.md` §5.3, §8, §8.1
- `A-RULE 023`, `A-RULE 027`, `A-RULE 029`, `A-RULE 012`
- `src/Animora.Domain/Identity/**` (entity shapes), `src/Animora.Application/Ports/**`
- `src/Animora.Persistence.SqlServer/Context/AnimoraServerDbContext.cs`

## Goal
The server authenticates real users: ASP.NET Core Identity over `AnimoraServerDbContext`, the
built-in bearer/refresh token endpoints for the desktop, cookie auth for the admin surface, the
first-run bootstrap that provisions tenant + owner, and the device registry the offline capability
snapshot (B04) will later be issued against. **Authentication only** — no authorization decision, no
tenant resolution middleware, no entitlement check (D04 / E07 own those).

## Design decisions (pre-made — implement, do not re-derive)
- **Identity is ASP.NET Core Identity, unmodified primitives** (`STACK.md` §5.3): no custom token
  protocol, no custom crypto (`CONVENTIONS.md` §7). Packages pinned centrally:
  `Microsoft.AspNetCore.Identity.EntityFrameworkCore` 10.x,
  `Microsoft.AspNetCore.Authentication.JwtBearer` **not** added (the built-in
  `AddBearerToken(IdentityConstants.BearerScheme)` token handler is used, not a JWT issuer).
- **`AnimoraUser` / `AnimoraRole`** in `src/Animora.Persistence.SqlServer/Identity/`:
  - `sealed class AnimoraUser : IdentityUser<string>` — `Id` is the **same ULID** as the domain
    `User.Id` (identity row and domain row are 1:1 and share the key; the domain entity stays
    persistence-ignorant). Extra columns: `DisplayName`, `DefaultTenantId`, `Status` (`UserStatus`),
    `MustChangePassword`, `CreatedAtUtc`, `LastLoginAtUtc`.
  - `sealed class AnimoraRole : IdentityRole<string>` — one row per `StaffRole` value; the
    **permission** set is **not** stored in Identity claims (it lives in `RoleDefinition`, A03) —
    Identity answers "who", `RoleDefinition` answers "what may they do" (A-RULE 023).
  - `AnimoraServerDbContext` inherits `IdentityDbContext<AnimoraUser, AnimoraRole, string>`; the
    Identity tables are **tenant-agnostic by design** (a user may belong to several tenants through
    `StaffMembership`) and are therefore listed in `ServerModel.NonTenantScopedTables` — a new list
    added beside D02's existing two, asserted by a test so no one bolts a `Tenant` filter onto them.
  - Identity table names are explicitly renamed to `Identity<Name>` (`IdentityUsers`,
    `IdentityRoles`, `IdentityUserClaims`, `IdentityUserLogins`, `IdentityUserTokens`,
    `IdentityRoleClaims`, `IdentityUserRoles`) so no business table name is shadowed.
- **Migration `103_ServerIdentity`** adds the Identity tables plus
  `IX_IdentityUsers_NormalizedUserName` (unique, Identity's own) and
  `IX_IdentityUsers_DefaultTenantId`. It never edits `102_ServerCoreSchema`.
- **Options** (`src/Animora.Server/Configuration/IdentityOptions.cs` →
  `AnimoraIdentityOptions`, section `Animora:Identity`, validated on start):
  | Member | Default | Meaning |
  |---|---|---|
  | `BearerTokenLifetimeMinutes` | 60 | access-token lifetime |
  | `RefreshTokenLifetimeDays` | 14 | refresh-token lifetime |
  | `MaxFailedAccessAttempts` | 5 | lockout threshold (mirrors B03's local policy) |
  | `LockoutMinutes` | 15 | lockout duration |
  | `RequireConfirmedAccount` | false | no email infrastructure exists (Zero-Internet, A-RULE 016) |
  | `FirstRunEnabled` | true | flipped to false automatically once a tenant exists |
  Password rules come from the **same** `PasswordPolicy` values B03 defined (minimum length 10,
  at least one digit, at least one non-alphanumeric, no user-name substring) — configured onto
  `IdentityOptions.Password` so client and server never disagree about what a valid password is.
- **Two authentication schemes, one mechanism family** (`ARCHITECTURE.md` §10.2):
  - `IdentityConstants.BearerScheme` (default challenge for `/api/**`) — desktop.
  - `IdentityConstants.ApplicationScheme` (cookie) — admin browser surface (E01 consumes it).
  - A `PolicySchemeSelector` picks the scheme per request: `Authorization: Bearer` header present →
    bearer, otherwise cookie. No third path is invented "just for desktop".
  - Cookie settings: `HttpOnly`, `SecurePolicy = Always`, `SameSite = Lax`, sliding expiration off,
    protected by the D01 key ring (fails closed without it, A-RULE 027).
- **Endpoints** (`src/Animora.Server/Endpoints/IdentityEndpoints.cs`, a
  `MapAnimoraIdentity(this IEndpointRouteBuilder)` extension — minimal APIs, one group
  `/api/identity`, all responses `WireApiError` on failure):
  | Method + route | Purpose | Auth |
  |---|---|---|
  | `POST /api/identity/login` | password → bearer + refresh pair; body `LoginRequestV1` | anonymous |
  | `POST /api/identity/refresh` | refresh → new pair | anonymous (refresh token is the credential) |
  | `POST /api/identity/logout` | revokes the current refresh token + cookie | authenticated |
  | `GET /api/identity/me` | `IdentityMeV1` (user id, display name, memberships, roles) | authenticated |
  | `POST /api/identity/password` | change own password; bumps the security stamp | authenticated |
  | `POST /api/identity/devices/register` | `DeviceRegistrationV1` → `DeviceRegistrationResultV1` | authenticated |
  | `POST /api/identity/first-run` | creates tenant + owner user, then disables itself | anonymous, **gated** |
  `MapIdentityApi<T>()` (the library's own endpoint bundle) is **deliberately not used**: it exposes
  registration/email-confirmation/2FA endpoints this product has no infrastructure for. The six
  endpoints above call `SignInManager`/`UserManager` directly, so the primitives are still
  Microsoft's.
- **Wire DTOs** (`src/Animora.Contracts.Wire/Identity/`, all `sealed record`, versioned by name —
  the shared wire project so E07's client half maps to the exact same shapes):
  `LoginRequestV1(string UserName, string Password, string DeviceId, string? DeviceName)`,
  `TokenPairV1(string AccessToken, DateTimeOffset AccessExpiresAtUtc, string RefreshToken, DateTimeOffset RefreshExpiresAtUtc)`,
  `IdentityMeV1(string UserId, string DisplayName, IReadOnlyList<TenantMembershipV1> Memberships, bool MustChangePassword)`,
  `TenantMembershipV1(string TenantId, string TenantName, string Role, IReadOnlyList<string> PermissionCodes)`,
  `DeviceRegistrationV1(string DeviceId, string DeviceName, string Platform, string AppVersion, string InstallationFingerprint)`,
  `DeviceRegistrationResultV1(string DeviceId, DeviceStatus Status, DateTimeOffset RegisteredAtUtc)`,
  `ChangePasswordRequestV1(string CurrentPassword, string NewPassword)`,
  `FirstRunRequestV1(string TenantName, string OwnerUserName, string OwnerDisplayName, string OwnerPassword)`.
  `WireContract.Version` is **not** bumped (these are new shapes, not changed ones — ROADMAP §3.8).
- **`ServerAuthenticationService`** (`src/Animora.Server/Identity/ServerAuthenticationService.cs`) —
  the only place `SignInManager`/`UserManager` are touched; endpoints are thin. It returns
  `Result<T>` (never throws for an expected failure, `CONVENTIONS.md` §4) with these codes:
  `INVALID_CREDENTIALS` (deliberately identical for unknown user *and* wrong password — no user
  enumeration), `ACCOUNT_LOCKED`, `ACCOUNT_DISABLED`, `PASSWORD_CHANGE_REQUIRED`,
  `REFRESH_TOKEN_INVALID`, `DEVICE_REVOKED`, `FIRST_RUN_ALREADY_COMPLETED`.
- **Device registry:** registration upserts the domain `Device` row (A03) for
  `(TenantId, DeviceId)` — `Status = Active` on first sight, and a device whose stored `Status` is
  `Revoked` **cannot re-register itself** (`DEVICE_REVOKED`, A-RULE 029); only an operator action
  (D10) can reactivate it. `InstallationFingerprint` is stored for D11's license binding but is
  **not** verified here (no licensing yet — that is D11's scope).
- **First-run bootstrap** (`src/Animora.Server/Identity/FirstRunService.cs`): allowed only while
  **zero** tenants exist **and** `FirstRunEnabled` is true; it calls D02's `TenantProvisioner`, then
  creates the `AnimoraUser` + `StaffMembership` (role `Manager`) in the **same transaction**, then
  writes a security audit entry and sets `FirstRunEnabled = false` in a persisted
  `ServerBootstrapState` row (a new non-tenant-scoped table in migration `103_ServerIdentity`) so the
  endpoint cannot be replayed even after a restart. A second call returns
  `FIRST_RUN_ALREADY_COMPLETED`.
- **Audit:** every login success/failure, lockout, password change, logout, device registration and
  first-run completion writes a `ServerAuditEntry` row through `IServerAuditWriter`
  (`src/Animora.Server/Diagnostics/ServerAuditWriter.cs`) — append-only, same field set as B05's
  local `AuditEntry` (actor, tenant, device, action, correlation ids), stored in a new
  `ServerAuditEntries` table (migration `103_ServerIdentity`). Never the password, never the token
  (`LogRedaction` from D01 applies).
- **`/health/ready` gains an `identity` check**: the Identity tables exist and the role set is
  seeded. Roles are seeded idempotently at startup from `StaffRole` by `IdentityRoleSeeder`.
- **Rate limiting on `/api/identity/login` and `/refresh`** is explicitly **out of scope here** and
  owned by D13 (one global rate-limiting policy, not a per-endpoint retrofit). The endpoints are
  written so the policy can be attached later with `.RequireRateLimiting("auth")` and a `TODO(D13)`
  comment naming that exact call.

## Files to create / modify
| Path | Action | Contents (one line) |
|---|---|---|
| `src/Animora.Contracts.Wire/Identity/*.cs` | create | 8 wire DTOs |
| `src/Animora.Persistence.SqlServer/Identity/AnimoraUser.cs` | create | Identity user + extra columns |
| `src/Animora.Persistence.SqlServer/Identity/AnimoraRole.cs` | create | Identity role |
| `src/Animora.Persistence.SqlServer/Identity/ServerBootstrapState.cs` | create | first-run latch row |
| `src/Animora.Persistence.SqlServer/Identity/ServerAuditEntry.cs` | create | append-only audit row |
| `src/Animora.Persistence.SqlServer/Context/AnimoraServerDbContext.cs` | modify | inherit `IdentityDbContext`, rename tables, extend `ServerModel` lists |
| `src/Animora.Persistence.SqlServer/Migrations/103_ServerIdentity*` | create | Identity + bootstrap + audit tables |
| `src/Animora.Server/Configuration/AnimoraIdentityOptions.cs` | create | options + validation |
| `src/Animora.Server/Identity/IdentitySetup.cs` | create | `AddAnimoraIdentity(...)`, schemes, policy selector |
| `src/Animora.Server/Identity/ServerAuthenticationService.cs` | create | login/refresh/logout/password/me |
| `src/Animora.Server/Identity/DeviceRegistrationService.cs` | create | device upsert + revoked guard |
| `src/Animora.Server/Identity/FirstRunService.cs` | create | tenant + owner bootstrap, self-disabling |
| `src/Animora.Server/Identity/IdentityRoleSeeder.cs` | create | idempotent role seeding |
| `src/Animora.Server/Endpoints/IdentityEndpoints.cs` | create | the 7 minimal-API endpoints |
| `src/Animora.Server/Diagnostics/ServerAuditWriter.cs` | create | `IServerAuditWriter` + impl |
| `src/Animora.Server/Program.cs` | modify | identity registration, auth middleware, endpoint mapping, readiness check |
| `Directory.Packages.props` | modify | pin `Microsoft.AspNetCore.Identity.EntityFrameworkCore` |
| `tests/Animora.Server.Tests/IdentityLoginTests.cs` | create | login/lockout/refresh/logout/stamp |
| `tests/Animora.Server.Tests/FirstRunTests.cs` | create | bootstrap once, replay refused |
| `tests/Animora.Server.Tests/DeviceRegistrationTests.cs` | create | upsert, revoked device refused |
| `tests/Animora.Server.Tests/IdentityAuditTests.cs` | create | audit rows written, no secret stored |

## Implementation steps
1. Add the wire DTOs; pin the Identity package.
2. Add `AnimoraUser`/`AnimoraRole`/`ServerBootstrapState`/`ServerAuditEntry`; make
   `AnimoraServerDbContext` inherit `IdentityDbContext`, rename the Identity tables, and extend
   `ServerModel` with `NonTenantScopedTables`.
3. Generate migration `103_ServerIdentity`.
4. Write `AnimoraIdentityOptions` + validation; configure Identity with `PasswordPolicy`'s values
   and the lockout numbers.
5. Write `IdentitySetup`: both schemes, the policy scheme selector, cookie hardening.
6. Write `ServerAuthenticationService`, `DeviceRegistrationService`, `FirstRunService`,
   `IdentityRoleSeeder`, `ServerAuditWriter`.
7. Map `IdentityEndpoints`; add `UseAuthentication()` to the pipeline in the D01 documented order
   (after Serilog request logging, before routing's endpoint execution).
8. Add the `identity` readiness check.
9. Write the four test classes (all gated by `SqlServerAvailability`).
10. Verify: `serve.sh` still prints `SERVER READY` with no SQL Server; with SQL Server, first-run →
    login → refresh → `/me` → password change → re-login works end to end.
11. `build.sh full` = 0/0; `test.sh` = `TESTS OK`; desktop `run.sh` = `SMOKE RUN OK`.

## Contracts / schema added
- Wire: `LoginRequestV1`, `TokenPairV1`, `IdentityMeV1`, `TenantMembershipV1`,
  `DeviceRegistrationV1`, `DeviceRegistrationResultV1`, `ChangePasswordRequestV1`,
  `FirstRunRequestV1`. `WireContract.Version` stays `1`.
- Persistence.SqlServer: `AnimoraUser`, `AnimoraRole`, `ServerBootstrapState`, `ServerAuditEntry`,
  `ServerModel.NonTenantScopedTables`, migration `103_ServerIdentity`.
- Server: `AnimoraIdentityOptions`, `IdentitySetup`, `ServerAuthenticationService`,
  `DeviceRegistrationService`, `FirstRunService`, `IdentityRoleSeeder`, `IdentityEndpoints`,
  `IServerAuditWriter`/`ServerAuditWriter`, the `identity` readiness check.
- `ErrorCodes` += `INVALID_CREDENTIALS`, `ACCOUNT_LOCKED`, `ACCOUNT_DISABLED`,
  `REFRESH_TOKEN_INVALID`, `DEVICE_REVOKED`, `FIRST_RUN_ALREADY_COMPLETED`
  (`PASSWORD_CHANGE_REQUIRED` exists from B03 — reuse).
- No `Animora.Contracts` change, no `CommandTypes` entry, no DI change in `App.axaml.cs`.

## Rules that apply here
- **A-RULE 023 / R-B20** — this phase answers **only** "who". No permission is evaluated, no
  entitlement is checked; `RoleDefinition` remains the authorization source (D04).
- **A-RULE 012** — no endpoint reads a `TenantId` from the request body; `/me` returns memberships
  derived from the authenticated user's own `StaffMembership` rows.
- **A-RULE 029** — a revoked device cannot re-register itself; only an operator action can.
- **A-RULE 027** — cookie/bearer protection rides the D01 persisted key ring; missing key ring =
  refuse to start.
- **CONVENTIONS §7** — Identity `PasswordHasher` primitives only, no custom crypto; identical error
  for unknown user and wrong password; every security action audited; secrets never logged.
- **A-RULE 016** — `RequireConfirmedAccount = false`; no email/SMS/external IdP path exists.

## Out of scope
- Tenant resolution middleware, `ITenantContext` implementation, permission policies → D04.
- Capability-snapshot issuance to a device, token↔offline-session bridging → E07.
- Rate limiting on the auth endpoints → D13.
- Operator (cross-tenant) accounts and their role → D10.
- License/entitlement verification of `InstallationFingerprint` → D11.
- Any desktop-side change (the desktop still authenticates locally, B03) → E07.

## Acceptance criteria
- Build: `scripts/sandbox/build.sh full` = 0 warnings / 0 errors.
- `IdentityLoginTests` green (or skipped with reason): valid login returns a `TokenPairV1`; unknown
  user and wrong password both return `INVALID_CREDENTIALS` with **byte-identical** bodies;
  `MaxFailedAccessAttempts` failures → `ACCOUNT_LOCKED` for `LockoutMinutes`; refresh returns a new
  pair and the old refresh token stops working; a password change bumps the security stamp and
  invalidates previously issued tokens; a disabled user gets `ACCOUNT_DISABLED`.
- `FirstRunTests` green: first call provisions tenant + owner + roles + chart of accounts (via
  `TenantProvisioner`) in one transaction; a second call returns `FIRST_RUN_ALREADY_COMPLETED`, and
  it still does after a host restart (the latch is persisted, not in-memory).
- `DeviceRegistrationTests` green: first registration creates an `Active` device; re-registration is
  an idempotent upsert; a `Revoked` device receives `DEVICE_REVOKED` and stays revoked.
- `IdentityAuditTests` green: each of login-success, login-failure, lockout, password-change,
  logout, device-registration and first-run writes exactly one `ServerAuditEntry`; no row and no log
  line contains a password, token or hash.
- `/health/ready` reports the `identity` check; with no SQL Server the server still starts and
  `/health/live` is 200.
- Desktop unaffected: `scripts/sandbox/run.sh` = `SMOKE RUN OK`.

## PROGRESS.md update
Mark D03 done; advance "Next phase" to `D04`; append a "Repo state" entry listing: the 8 wire
identity DTOs (**E07 maps to them**), `AnimoraUser`/`AnimoraRole` + the shared-ULID rule, the
Identity table renames + `ServerModel.NonTenantScopedTables`, migration `103_ServerIdentity`
(Identity + `ServerBootstrapState` + `ServerAuditEntries`), `AnimoraIdentityOptions` and the shared
`PasswordPolicy` values, the two schemes + policy selector (**E01 uses the cookie scheme**), the 7
endpoints and the deliberate rejection of `MapIdentityApi`, `ServerAuthenticationService` error-code
set, `DeviceRegistrationService` + the revoked-device rule (**E07/D11 depend on it**),
`FirstRunService` + the persisted latch, `IServerAuditWriter` (**D05/D11 write through it**), the
`identity` readiness check, the 6 new error codes, the `TODO(D13)` rate-limiting seam, deviations.
