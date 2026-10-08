# Phase S04 — Identity server — ASP.NET Core Identity, bearer/refresh, device-bound sessions

**Band:** M · **Track:** S (server) · **Depends on:** S03 · **Next:** S05
**Satisfies:** R-B04, R-B20 · **Runnable after this phase:** desktop offline yes · server required no
**Produced by:** M00 (regenerated 2026-10-08; supersedes the deleted `PHASE-D03` file).

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
- `ARCHITECTURE.md` §10.1–§10.2, §10.4, §10.6 and §10.7 — only these `§`.
- `STACK.md` §5.3 + §8.1 — only these `§`.
- `src/Animora.Persistence.PostgreSql/Context/AnimoraServerDbContext.cs`, `Concurrency/VersionTrigger.cs`, `Conventions/ServerModelConventions.cs` (S02; edited/used here).

(Nothing else. `Animora-UI/**`, `Database-Sampel/**`, other phase files and `windows-frontend-roadmap/phases/**` are out of bounds.)

## Goal

Make the server the sign-in authority: ASP.NET Core Identity (local, opaque protected bearer + refresh tokens — no JWT/OIDC/cloud IdP) over the PostgreSQL context with tenant-scoped users, memberships, role definitions and devices; endpoints `POST /auth/login`, `/auth/refresh`, `/auth/logout`, `/auth/device/register` under the `auth` rate-limit policy; account lockout, security-stamp rotation, device binding and an append-only security audit table. Authentication only — authorization policies and the tenant-context middleware are S05.

---

## §Reference pack

```csharp
namespace Animora.Domain.Time;  public interface IClock { DateTimeOffset UtcNow { get; } /* + monotonic helpers per A02 */ }
namespace Animora.Domain.Primitives;
public abstract class SyncableEntity { string Id { get; init; }   /* ULID */  string TenantId { get; set; }  DateTimeOffset UpdatedAt { get; set; }  string? Version { get; set; }  bool Deleted { get; set; } }
public abstract class AggregateRoot : SyncableEntity, IHasDomainEvents { IReadOnlyList<DomainEvent> DomainEvents; void ClearDomainEvents(); protected void Raise(DomainEvent e); }
public static class IdGenerator { public static string NewId(); /* 26-char ULID */ }
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
// Domain identity aggregates already in Animora.Domain.Identity (A03) — the server maps THEM (do not fork a second user model)
public enum UserStatus { /* Active, Disabled, … */ }  public enum MembershipStatus { /* Active, Suspended, Ended */ }  public enum DeviceStatus { Pending, Active, Revoked }
public sealed class User : AggregateRoot {   // TenantId inherited
    public const int MaxFailedAttempts = 5; public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15), DefaultFailureWindow = TimeSpan.FromMinutes(15);
    public string UserName; public string DisplayName; public string? PasswordHash; public string SecurityStamp; public UserStatus Status;
    public DateTimeOffset? LastLoginUtc; public int FailedAttemptCount; public DateTimeOffset? LockoutEndUtc; public bool MustChangePassword;  // all { get; private set; }
    public static User Create(string tenantId, string userName, string displayName, IClock clock);
    public bool RegisterFailedAttempt(IClock clock, int threshold = MaxFailedAttempts, TimeSpan? window = null);  // true when it caused lockout
    public void ClearFailures(); public bool IsLockedOut(DateTimeOffset nowUtc); public void RegisterSuccessfulLogin(IClock clock);
    public void RotateSecurityStamp(); public void SetPasswordHash(string passwordHash); public void RequirePasswordChange(); public void Disable(); public void Enable();
}
public sealed class StaffMembership : AggregateRoot {  // UserId, Role (StaffRole), Status, JoinedUtc, EndedUtc?, WorkStartMinute/WorkEndMinute/WorkDaysMask, ExtraPermissionCodes, DeniedPermissionCodes
    public static StaffMembership Create(string tenantId, string userId, StaffRole role, IClock clock); public bool IsActive { get; }
    public void ChangeRole(StaffRole r); public void Suspend(); public void Reinstate(); public void End(IClock clock); public void GrantExtra(string c); public void Deny(string c); public void ClearOverride(string c); }
public sealed class RoleDefinition : AggregateRoot { public StaffRole Role; public string DisplayKey; public bool IsSystem; public IReadOnlySet<string> PermissionCodes;
    public static RoleDefinition CreateSystem(string tenantId, StaffRole role, IClock clock); public void Grant(string c); public void Revoke(string c); public bool Has(string c); }
public enum StaffRole { Manager = 0, Doctor = 1, Receptionist = 2, Cashier = 3 /* + Accountant as declared in StaffRole.cs */ }
public sealed class Device : AggregateRoot { UserId, DeviceName, InstallationIdHash, Platform, Status, RegisteredUtc, LastContactUtc?, RevokedUtc?, RevocationReason?;
    public static Device Register(string tenantId, string userId, string deviceName, string installationIdHash, string platform, IClock clock);
    public bool IsUsable { get; } /* Status==Active */ public void Approve(); public void RecordContact(IClock clock); public void Revoke(string reason, IClock clock); }
public static class DefaultRolePermissions { /* IReadOnlySet<string> For(StaffRole) — seeds RoleDefinition rows */ }
public static class PermissionCodes { /* 42+ const string like PatientsRead="patients.read", SalesPost="sales.post"; RiskOf(code) → RiskLevel */ }
// Application passwords seam (B03a): Animora.Application.Security.IPasswordHasher + PasswordPolicy (client uses Identity PasswordHasher<T>) — the server uses Microsoft's PasswordHasher<TUser> directly; PasswordPolicy.Validate is reused for new passwords.
```
Existing pins: `Microsoft.Extensions.Identity.Core 10.0.12`. S02 provides: `AnimoraServerDbContext`, `ITenantContext`, `VersionTrigger.Apply(MigrationBuilder, table)`, `ServerModelConventions.ApplyTenancy/TenantIndex`, keyed data sources, key-ring Data Protection.

## §Design decisions

- **Packages (new pins, exact):** `Microsoft.AspNetCore.Identity.EntityFrameworkCore` **10.0.12** (Persistence.PostgreSql). Bearer scheme = framework `AddBearerToken(IdentityConstants.BearerScheme)` (opaque, Data-Protection-protected, key ring from S02). No `Microsoft.AspNetCore.Authentication.JwtBearer`.
- **Identity storage = the domain aggregates, not a parallel model.** `ServerUserStore : IUserStore<ServerIdentityUser>, IUserPasswordStore<…>, IUserSecurityStampStore<…>, IUserLockoutStore<…>` adapts the Domain `User` row. `ServerIdentityUser` is an **adapter DTO** (`Id`, `TenantId`, `UserName`, `NormalizedUserName`, `PasswordHash`, `SecurityStamp`, `LockoutEnd`, `AccessFailedCount`) — it is **not** an EF entity; the store reads/writes `User` through `AnimoraServerDbContext`. Lockout policy = the Domain constants (5 failures / 15-minute window / 15-minute lockout) — `UserManager` options mirror them; failures call `User.RegisterFailedAttempt(clock)`.
- **Tenant-scoped login:** login request carries `TenantCode` (a short slug resolved via `Tenants`, new column `Code varchar(32)` unique — added by this phase's migration) + `UserName` + `Password` + `DeviceInstallationIdHash`. The tenant is resolved **before** user lookup using a dedicated operator-less lookup `ITenantDirectory.FindByCodeAsync(code)` (the only unfiltered read of `Tenants`, implemented with an explicit raw `Where` on the `Tenants` table through a `TenantDirectoryContext` registered **without** the tenant filter and exposing nothing but that method — never `IgnoreQueryFilters()`). Wrong tenant/user/password → the same `401 CREDENTIALS_INVALID` (no user enumeration), constant-time-ish via a dummy hash verify.
- **Tables / migration (server `NNN_Identity`, next free ordinal after `101_ServerFoundation`; expected 102):** `Users` (Domain `User` columns; unique `(TenantId, NormalizedUserName)`), `StaffMemberships` (+ child tables `StaffMembershipExtraPermissions`, `StaffMembershipDeniedPermissions`, composite PK), `RoleDefinitions` (+ `RoleDefinitionPermissions`), `Devices` (unique `(TenantId, InstallationIdHash, UserId)`), `RefreshTokens` (`Id`,`TenantId`,`UserId`,`DeviceId`,`TokenHash` (SHA-256 of the opaque token), `FamilyId`,`IssuedUtc`,`ExpiresUtc`,`RotatedToId?`,`RevokedUtc?`; **append-only except the two nullable rotation columns**), `SecurityAudit` (append-only: `Id`,`TenantId`,`OccurredUtc`,`ActorUserId?`,`DeviceId?`,`Category`,`Action`,`Outcome`,`CorrelationId`,`DetailJson` (no secrets/PII bodies)); also `Tenants.Code` column. All `SyncableEntity` tables call `VersionTrigger.Apply`. `SecurityAudit` and `RefreshTokens` are **not** `SyncableEntity` (no Version/Deleted) but carry `TenantId` first in every index and the `"Tenant"` filter; `SecurityAudit` has a trigger `trg_SecurityAudit_immutable` raising on UPDATE/DELETE (append-only forever).
- **Tokens:** access token lifetime **15 min**, refresh **30 days**, sliding disabled. **Refresh rotation with reuse detection:** each refresh issues a new refresh token (new row, `FamilyId` kept), marks the old `RotatedToId`; presenting an already-rotated token revokes the whole family, rotates the user's `SecurityStamp`, writes a `SecurityAudit` row `auth.refresh.reuse_detected` and returns 401 `SESSION_EXPIRED`. Refresh token wire value = 256-bit random, base64url; only its SHA-256 is stored. Access-token principal claims: `sub`(UserId), `tid`(TenantId), `did`(DeviceId), `sst`(SecurityStamp), `role`(StaffRole) — **claims are identification only; authorization (S05) re-reads the stored membership/role every request** (never trusts a claimed role).
- **Device binding:** `/auth/device/register` (authenticated) creates a `Device` in `Pending`; a Manager approves via S05's command (`devices.manage`); until `Active` the login still succeeds but the access token carries `did` of a *pending* device and S05 limits it to `/auth/*` + read-only capability issuance. A **revoked** device: `/auth/refresh` and `/auth/login` for that `DeviceInstallationIdHash` return 403 `DEVICE_REVOKED` and write `auth.device.revoked_contact` audit. `Device.RecordContact(clock)` on every successful refresh.
- **Endpoints (minimal APIs, `Animora.Server.Auth`, policy `auth` rate limit, anonymous only for login/refresh):** `POST /auth/login` → `LoginResponse(AccessToken, ExpiresInSeconds, RefreshToken, UserId, TenantId, DeviceId, Role, MustChangePassword)`; `POST /auth/refresh` → same shape; `POST /auth/logout` (authenticated; revokes the family); `POST /auth/device/register`. DTOs live in `Animora.Contracts.Wire.Auth` (new, additive; **no `WireContract.Version` bump** — new endpoints only). Failures → `WireApiError` with codes from the existing catalog; HTTP 401/403/423 (lockout → 423 `USER_LOCKED_OUT`) — auth failures are transport-class, not settlement.
- **Seeding:** `TenantProvisioner.CreateTenantWithOwnerAsync(...)` (internal service used by S15/S19 and tests) creates `Tenant` + system `RoleDefinition`s from `DefaultRolePermissions` + Manager `User`/`StaffMembership` in one UoW; there is **no** public sign-up endpoint (R-B36 intake is S15).
- **Audit:** every login success/failure, lockout, refresh, reuse, logout, device registration/revocation writes `SecurityAudit` in the **same transaction** as the state change (failure paths use a short separate transaction). No password, token or hash is ever written.
- **New ErrorCodes:** none (reuse `CredentialsInvalid`, `SessionExpired`, `UserLockedOut`, `UserDisabled`, `DeviceRevoked`, `MembershipNotActive`, `TenantSuspended`, `PasswordPolicyViolation`).

## §Implementation plan

1. Add pin `Microsoft.AspNetCore.Identity.EntityFrameworkCore` 10.0.12; reference it from `Animora.Persistence.PostgreSql`; add `Animora.Contracts.Wire/Auth/*` DTOs (login/refresh/device).
2. Persistence: configurations for `User`, `StaffMembership`(+child tables), `RoleDefinition`(+child), `Device`, `RefreshToken` entity (new, in `Animora.Persistence.PostgreSql.Identity`), `SecurityAuditEntry`; add `DbSet`s; `ServerUserStore`, `ITenantDirectory` + `TenantDirectory`, `RefreshTokenService`, `SecurityAuditWriter`, `TenantProvisioner`.
3. Server: `Auth/AuthEndpoints.cs` (map under `ServerRoutes.Auth` with `RequireRateLimiting(RateLimitPolicies.Auth)`), `Auth/AuthServiceCollectionExtensions.cs` (`AddAnimoraIdentity`), insert `app.UseAuthentication()` in the pipeline slot reserved by S01 (after request logging, before routing).
4. Migration: `.sandbox/tools/dotnet-ef migrations add Identity --project src/Animora.Persistence.PostgreSql --startup-project src/Animora.Persistence.PostgreSql --context AnimoraServerDbContext --output-dir Migrations/Server` — then **rename the generated migration id/class to the next free ordinal** (`list Migrations/Server`, expect `102_Identity`) and hand-add `VersionTrigger.Apply(...)` for each `SyncableEntity` table + the immutable trigger for `SecurityAudit`. Update `ServerSchemaVersion.ExpectedHead`.
5. Tests (pure with in-memory fakes of the store seams where possible; PG-gated for DB behavior): see Acceptance. `PgFixture` from S02 is reused.
6. `scripts/sandbox/build.sh full` → 0/0; `scripts/sandbox/test.sh Server` → green; `scripts/sandbox/run.sh` → `SMOKE RUN OK`.
7. `PROGRESS.md` per recipe (record the migration ordinal, tables, the claim names); commit.

## Files to create / modify

| Path | Contents |
|---|---|
| `Directory.Packages.props`, `src/Animora.Persistence.PostgreSql/*.csproj` (edit) | +1 pin, package reference |
| `src/Animora.Contracts.Wire/Auth/LoginRequest.cs`, `LoginResponse.cs`, `RefreshRequest.cs`, `DeviceRegisterRequest.cs`, `DeviceRegisterResponse.cs` | auth wire DTOs (sealed records) |
| `src/Animora.Persistence.PostgreSql/Identity/ServerUserStore.cs`, `ServerIdentityUser.cs`, `RefreshToken.cs`, `RefreshTokenService.cs`, `SecurityAuditEntry.cs`, `SecurityAuditWriter.cs`, `TenantProvisioner.cs`, `TenantDirectory.cs` | identity data layer |
| `.../Configurations/{User,StaffMembership,RoleDefinition,Device,RefreshToken,SecurityAudit}Configuration.cs` | EF mappings |
| `.../Context/AnimoraServerDbContext.cs` (edit) | DbSets |
| `.../Migrations/Server/*102_Identity*` | migration |
| `src/Animora.Server/Auth/AuthEndpoints.cs`, `AuthServiceCollectionExtensions.cs`, `Program.cs` (edit) | endpoints + wiring |
| `tests/Animora.Server.Tests/Identity/*.cs` | tests |

## Contracts / schema added

Tables: `Users`, `StaffMemberships` (+2 child), `RoleDefinitions` (+1 child), `Devices`, `RefreshTokens`, `SecurityAudit`; column `Tenants.Code`. Wire: `LoginRequest`, `LoginResponse`, `RefreshRequest`, `DeviceRegisterRequest/Response`. Claim names `sub|tid|did|sst|role`. Seams: `ITenantDirectory`, `ISecurityAuditWriter`, `TenantProvisioner`, `RefreshTokenService`.

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

- Authorization policies, stored-RBAC evaluation, tenant-context middleware, `ITenantContext` request binding, entitlement slot → **S05**.
- Device approval command + capability-snapshot signing/issuance endpoint → **S05** (policy) / **G06** (client use) / **S16** (shared signing key infrastructure).
- Public sign-up / provisioning intent → **S15**. Operator login (separate operator identity) → **S14/S19**.
- Passkeys / external IdP / JWT → deferred (ROADMAP §5; never cloud IdP).

## Acceptance criteria (mechanical)

1. `scripts/sandbox/build.sh full` = 0 warnings / 0 errors; `scripts/sandbox/run.sh` = `SMOKE RUN OK`; client projects unchanged (`git diff --stat -- src/Animora.Domain src/Animora.Persistence.Sqlite src/Animora.Desktop` empty).
2. `grep -rn "JwtBearer\|OpenIdConnect" src` returns nothing; `grep -rn "NewGuid()" src/Animora.Server src/Animora.Persistence.PostgreSql | grep -v "gen_random_uuid\|StringGuid\|Version"` returns nothing relevant to ids.
3. `ls src/Animora.Persistence.PostgreSql/Migrations/Server | grep -c Identity` ≥ 1 and its ordinal is the next free one after `101_ServerFoundation`.
4. Pure tests pass: `ServerUserStoreTests.Lockout_After5Failures_For15Min`, `LoginServiceTests.UnknownTenant_UnknownUser_WrongPassword_ReturnSameError`, `RefreshTokenServiceTests.Rotation_IssuesNewToken_StoresOnlyHash`, `RefreshTokenServiceTests.Reuse_RevokesFamily_RotatesSecurityStamp_Audits`, `LoginServiceTests.RevokedDevice_Returns_DEVICE_REVOKED`, `AuthEndpointsTests.Login_RateLimited_Returns429`, `AuthEndpointsTests.Failure_Returns_WireApiError_WithCorrelationId`, `TokenClaimsTests.NoRoleTrustedFromClaim_Marker` (documents that S05 re-reads stored role).
5. PG-gated (mandatory in G07): `IdentityMigrationTests.SecurityAudit_UpdateAndDelete_Raise`, `IdentityMigrationTests.Users_UniquePerTenantUsername`, `AuthFlowTests.Login_Refresh_Logout_EndToEnd`, `AuthFlowTests.CrossTenant_Username_Reuse_Allowed`.
6. No password, token or hash string appears in any log/audit: `AuditRedactionTests.NoSecretsInDetailJson`.

## PROGRESS.md update

At the end of this phase: set the `S04` row in "Phase status" to `✅ Done` (add date + one-line summary), advance **"Next phase" → `S05`**,
append one `### S04` entry to "Repo state log" (what exists now: projects, tables/migration ordinal, error codes added, test classes), log any
deviation in the "Decision & deviation log", then commit `feat(server): S04 identity server` and sync per the repo git workflow.
