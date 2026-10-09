# S04 — Identity Server — Implementation TODO (self-contained)

> **For the implementing model:** This file carries every decision, signature and path already
> resolved from the repo. Implement top-to-bottom; do **not** re-explore the repo to "understand"
> it. Re-open a source file only when a compile error proves a snippet below diverges from the tree.
> Scope = exactly this phase; nothing from S05.
>
> **🔒 LOCKED PUSH RULE — after completing EACH task below, run `scripts/git/push.sh "<msg>"`.**
> Not pushing = project failure. If push exits code `3`, call the `setup_github_environment` tool once, then re-run.
> If a heavy fix / large code block is written mid-task, push it immediately with a clear message.
>
> **Build/test only via sandbox scripts (never raw `dotnet build`, memory-capped ~985 MiB):**
> `scripts/sandbox/build.sh [fast|full]`, `scripts/sandbox/test.sh [filter]`, `scripts/sandbox/run.sh`.
> `dotnet-ef` = `.sandbox/tools/dotnet-ef` (never `dotnet tool install`). Run `scripts/sandbox/install-prereqs.sh` once (idempotent).
> **No full/long test runs** unless a task explicitly calls for it. Prefer `build.sh fast` during iteration, targeted `test.sh Server` filters at the end.

---

## 0. Goal (what "done" means)

Make the server the sign-in authority using **ASP.NET Core Identity** (local, opaque Data-Protection-protected
bearer + refresh tokens — **no JWT/OIDC/cloud IdP**) over the PostgreSQL context. Deliver:
- Tenant-scoped **Users, StaffMemberships, RoleDefinitions, Devices** mapped from the **existing Domain aggregates** (do NOT fork a second user model).
- Endpoints `POST /auth/login`, `/auth/refresh`, `/auth/logout`, `/auth/device/register` under the `auth` rate-limit policy.
- Account lockout, security-stamp rotation, device binding, refresh-token rotation + reuse detection, append-only `SecurityAudit`.
- **Authentication only.** Authorization policies + tenant-context middleware = S05 (out of scope).

**Offline invariant:** desktop must keep starting fully offline — this phase adds nothing the desktop needs at startup.
**Acceptance (mechanical) — see §A at bottom.**

---

## 1. Pre-resolved repo facts (no need to re-read these files)

**Branch:** work on `genspark_ai_developer` (push script handles sync/rebase). Current repo is on `main`; first push task must use `-b genspark_ai_developer`.

**Projects (`src/`):** `Animora.Domain`, `Animora.Application`, `Animora.Persistence.Sqlite`, `Animora.Infrastructure.Local`,
`Animora.Client.Services`, `Animora.Contracts`, `Animora.Contracts.Wire`, `Animora.Desktop`, `Animora.Sync.Abstractions`,
`Animora.Server` (S01), `Animora.Persistence.PostgreSql` (S02+S03 done).

**Server state:** S01+S02+S03 done. S02 wires DataProtection→PostgreSQL key ring, `AddAnimoraPostgreSql`. S03 adds `ServerUnitOfWork`, `TenantRepository<T>`, `PageRules`, read-context factory.

### 1.1 Domain Identity aggregates (already exist in `src/Animora.Domain/Identity/` — MAP THEM, do not redefine)
Files present: `User.cs`, `StaffMembership.cs`, `RoleDefinition.cs`, `Device.cs`, `StaffRole.cs`, `Tenant.cs`,
`DefaultRolePermissions.cs`, `PermissionCodes.cs`, `Permission.cs`, `CapabilitySnapshot.cs`, `OfflineCapabilityPolicy.cs`.

**`User : AggregateRoot`** (TenantId inherited from `SyncableEntity`): all setters `private set`.
- Props: `string UserName` (lowercased invariant), `string DisplayName`, `string? PasswordHash`, `string SecurityStamp` (ULID), `UserStatus Status` (`Active=0,Disabled=1,Locked=2`), `DateTimeOffset? LastLoginUtc`, `int FailedAttemptCount`, `DateTimeOffset? LockoutEndUtc`, `bool MustChangePassword`. Private field `_firstFailureUtc` (NOT persisted — careful in EF config).
- Consts: `MaxFailedAttempts = 5`, `LockoutDuration = 15min`, `DefaultFailureWindow = 15min`.
- Methods: `static User Create(tenantId, userName, displayName, IClock)`; `bool RegisterFailedAttempt(IClock, threshold=5, TimeSpan? window=null)` (true⇒caused lockout); `void ClearFailures()`; `bool IsLockedOut(DateTimeOffset nowUtc)` (fail-closed: Locked + null end ⇒ locked); `void RegisterSuccessfulLogin(IClock)`; `void RotateSecurityStamp()`; `void SetPasswordHash(string)` (also rotates stamp, clears MustChangePassword); `void RequirePasswordChange()`; `void Disable()`; `void Enable()`.

**`StaffMembership : AggregateRoot`:** `UserId, Role(StaffRole), Status(MembershipStatus), JoinedUtc, EndedUtc?, WorkStartMinute/WorkEndMinute/WorkDaysMask, ExtraPermissionCodes, DeniedPermissionCodes`. `static Create(tenantId, userId, StaffRole, IClock)`; `bool IsActive`; `ChangeRole/Suspend/Reinstate/End(IClock)/GrantExtra(c)/Deny(c)/ClearOverride(c)`.

**`RoleDefinition : AggregateRoot`:** `StaffRole Role; string DisplayKey; bool IsSystem; IReadOnlySet<string> PermissionCodes`. `static CreateSystem(tenantId, StaffRole, IClock)`; `Grant(c)/Revoke(c)/bool Has(c)`.

**`Device : AggregateRoot`:** `UserId, DeviceName, InstallationIdHash, Platform, Status(DeviceStatus: Pending,Active,Revoked), RegisteredUtc, LastContactUtc?, RevokedUtc?, RevocationReason?`. `static Register(tenantId, userId, deviceName, installationIdHash, platform, IClock)`; `bool IsUsable` (Status==Active); `Approve()/RecordContact(IClock)/Revoke(reason, IClock)`.

**`StaffRole` enum:** `Manager=0, Doctor=1, Receptionist=2, Cashier=3, Accountant=4`.

**`DefaultRolePermissions`:** `static IReadOnlySet<string> For(StaffRole)` — used to seed `RoleDefinition` rows.
**`PermissionCodes`:** 42+ const strings (`PatientsRead="patients.read"` etc.), `RiskOf(code)→RiskLevel`.

**`Tenant : AggregateRoot`** (`src/Animora.Domain/Identity/Tenant.cs`): `DisplayName, LegalName?, Timezone(default "Asia/Tehran"), DefaultCurrency, TenantStatus Status(Active=0,Suspended=1,Closed=2)`. `static Create(displayName, IClock, legalName?, timezone, defaultCurrency)` — sets `TenantId == Id`. **NO `Code` property yet** — S04 adds `Tenants.Code` column (see §3.3). Decide: add a `Code` property to the domain `Tenant` (private set + a setter method) OR map it as a shadow property. **DECISION: add `public string? Code { get; private set; }` + `void SetCode(string code)` to `Tenant.cs`** so `TenantProvisioner` can assign it and `TenantDirectory` can read it. This is additive, keeps the local/offline path unaffected (Code stays null locally).

### 1.2 Domain primitives / Results (reuse — never redefine)
- `Animora.Domain.Time.IClock { DateTimeOffset UtcNow { get; } }`.
- `Animora.Domain.Primitives.SyncableEntity { string Id{init}; string TenantId{set}; DateTimeOffset UpdatedAt; string? Version; bool Deleted; }`; `AggregateRoot : SyncableEntity, IHasDomainEvents`.
- `IdGenerator.NewId()` / `NewId(DateTimeOffset)` → 26-char ULID. **Never `Guid.NewGuid()` for business ids.**
- `Animora.Domain.Results`: `ErrorCode(Code,Category)`; `Result` / `Result<T>` with `Ok/Fail/IsSuccess/IsFailure/Error/Detail`.
- **Reusable ErrorCodes (DO NOT add new ones — §Design says none):** `CredentialsInvalid`, `SessionExpired`, `UserLockedOut`, `UserDisabled`, `DeviceRevoked`, `MembershipNotActive`, `TenantSuspended`, `PasswordPolicyViolation`, `ConcurrencyConflict`, `TenantMismatch`. (Confirm exact names exist in `src/Animora.Domain/Results/ErrorCodes.cs` by `grep` when first needed — do not read the whole file.)

### 1.3 Application passwords seam (B03a)
`Animora.Application.Security.IPasswordHasher` + `PasswordPolicy` exist. **Server uses Microsoft's `PasswordHasher<TUser>` directly** (not the client seam) but **reuses `PasswordPolicy.Validate`** for new passwords.

### 1.4 S02/S03 persistence primitives (exact — in `src/Animora.Persistence.PostgreSql/`)
- `Context/AnimoraServerDbContext.cs` — `sealed`, ctor `(DbContextOptions<AnimoraServerDbContext>, ITenantContext)`. Has `public string CurrentTenantId => tenantContext.TenantId ?? throw new TenantContextMissingException();` and `DbSet<Tenant> Tenants`. `OnModelCreating` calls `ApplyConfigurationsFromAssembly(typeof(AnimoraServerDbContext).Assembly)` then `ServerModelConventions.ApplyTenancy(modelBuilder, () => CurrentTenantId)`. **Edit: add new `DbSet`s here.**
- `Conventions/ServerModelConventions.cs`:
  - `const string TenantFilterName = "Tenant"`, `const string SoftDeleteFilterName = "SoftDelete"`.
  - `static void ApplyTenancy(ModelBuilder, Expression<Func<string>> tenantAccessor)` — auto-applies tenant+softdelete filters, uuid Version token, timestamptz(6) UTC, integer enums **to every `SyncableEntity` root**. So `User/StaffMembership/RoleDefinition/Device` get these automatically; only `RefreshToken`/`SecurityAudit` (NOT SyncableEntity) need manual tenant handling.
  - `static IndexBuilder TenantIndex(EntityTypeBuilder, params string[] columns)` → `IX_<Table>_TenantId_<cols>`.
  - Column conventions it sets on SyncableEntity: `Id` = `character varying(26)` maxlen 26; `TenantId` = `character varying(26)` maxlen 26 required; `UpdatedAt` timestamptz(6) UTC; `Deleted` boolean default false; `Version` = `uuid` via `StringGuidConverter`, `gen_random_uuid()` default, `ValueGeneratedOnAddOrUpdate`, concurrency token.
- `Concurrency/VersionTrigger.cs`: `static void Apply(MigrationBuilder, string table)` creates `trg_<table>_version BEFORE INSERT OR UPDATE ... EXECUTE FUNCTION animora_set_version()`; `static void Drop(...)`. Function `animora_set_version` created in `101_ServerFoundation`. **Call `Apply` for each SyncableEntity table in the migration Up; `Drop` in Down.**
- `Concurrency/StringGuidConverter.cs` — string↔uuid converter (reuse for Version; tables that need uuid).
- `Configurations/TenantConfiguration.cs` — pattern reference for new config classes: `ToTable("<Name>","public")`, `HasKey`, property mappings, `ServerModelConventions.TenantIndex(builder, ...)`. **Edit: add `Code` mapping here** — `builder.Property(e => e.Code).HasColumnType("character varying(32)").HasMaxLength(32);` + unique index `builder.HasIndex(e => e.Code).IsUnique().HasFilter("\"Code\" IS NOT NULL");` (partial unique so local null-code tenants don't clash — but server tenants always have code).
- `ServerSchemaVersion.cs` — `public const string ExpectedHead = "101_ServerFoundation";` **Edit → `"102_Identity"`.**
- `Context/ServerReadContextFactory.cs` + `IServerReadContextFactory.cs` (S03) — AsNoTracking read contexts.
- `Repositories/TenantRepository.cs`, `Repositories/PageRules.cs` (S03).
- `Transactions/ServerUnitOfWork` (S03) — use for same-transaction audit writes.
- `Migrations/Server/` currently: `20261009093518_101_ServerFoundation{,.Designer}.cs` + `AnimoraServerDbContextModelSnapshot.cs`. **Next free ordinal = `102`.** `Migrations/KeyRing/` is the independent key-ring context (do not touch).

### 1.5 Server hosting primitives (exact — `src/Animora.Server/Hosting/`)
- `ServerRoutes.cs` (internal static): `Auth = "/auth"`, `HealthLive`, `HealthReady`, etc. Map auth endpoints under `ServerRoutes.Auth`.
- `RateLimitPolicies.cs` (internal static): `Auth = "auth"` (10/min per IP). Use `.RequireRateLimiting(RateLimitPolicies.Auth)`.
- `ServerErrorCodes.cs` (internal static): transport codes `Unexpected`, `RateLimited`. (Auth failures use Domain ErrorCodes string values in `WireApiError.Code`.)
- `Program.cs` — fixed numbered startup. **Edit points:**
  - After step 4 (`AddAnimoraPostgreSql`): add `builder.Services.AddAnimoraIdentity(builder.Configuration);` (new extension).
  - In the middleware pipeline, the comment reserves the slot: *"Request logging ... → [tenant/authN/authZ/entitlement slots] → S04/S05"*. **Insert `app.UseAuthentication();` AFTER request-logging placeholder, BEFORE `app.UseRateLimiter();`** (authN slot; tenant/authZ/entitlement remain S05). Add `app.MapAuthEndpoints();` (or map inside extension) near health endpoints.
  - `public partial class Program;` already exposed for `WebApplicationFactory<Program>` tests.
- `appsettings.json` / `appsettings.Development.json` — token lifetime options can default in code; no secrets in JSON (`SecretGuard` enforces).

### 1.6 Wire contracts (`src/Animora.Contracts.Wire/`)
- `WireContract.Version = 1` — **do NOT bump** (new endpoints only, additive).
- `Errors/WireApiError.cs`: `sealed record WireApiError(string Code, string? Detail, IReadOnlyList<WireFieldError> Fields, string CorrelationId)`. All auth failures project to this.
- `Errors/WireFieldError.cs` exists.
- New DTOs go in **new** folder `src/Animora.Contracts.Wire/Auth/` as `sealed record`s.

### 1.7 Tests (`tests/Animora.Server.Tests/`)
- `Persistence/PgFixture.cs`: `PgFixture : IAsyncLifetime` (creates/drops a private DB from `ANIMORA_PG_TEST_CONNECTION`). `[PgFact]` attribute **skips** when the env var is unset and `ANIMORA_REQUIRE_PG != 1` (fails if required). **Use `[PgFact]` for all DB-touching tests; use plain `[Fact]` for pure in-memory store/service tests.**
- Existing patterns to mirror: `HostSmokeTests.cs` (uses `WebApplicationFactory<Program>`), `Persistence/ServerModelTests.cs`, `Persistence/PgGatedTests.cs`.
- Put new tests under `tests/Animora.Server.Tests/Identity/`.

---

## 2. Packages / project wiring — **TASK 1**

**Do:**
1. Edit `Directory.Packages.props`: add pin `<PackageVersion Include="Microsoft.AspNetCore.Identity.EntityFrameworkCore" Version="10.0.12" />`. (Pin `Microsoft.Extensions.Identity.Core 10.0.12` already exists.)
2. Edit `src/Animora.Persistence.PostgreSql/Animora.Persistence.PostgreSql.csproj`: add `<PackageReference Include="Microsoft.AspNetCore.Identity.EntityFrameworkCore" />`.
3. **No** `Microsoft.AspNetCore.Authentication.JwtBearer` (forbidden). Bearer scheme = framework `AddBearerToken(IdentityConstants.BearerScheme)` (comes with ASP.NET Core, available to `Animora.Server`).
4. `scripts/sandbox/build.sh fast` — expect it still builds (no code yet depends on the new pin).

**Verify:** `grep -rn "JwtBearer\|OpenIdConnect" src` returns nothing.
**Push:** `scripts/git/push.sh -b genspark_ai_developer "chore(server): S04 task1 — add Identity.EntityFrameworkCore pin + package ref"`
**Status:** ✅ DONE (build full 0/0)

---

## 3. Persistence layer — data + stores + services

### TASK 2 — Add `Tenant.Code` to Domain + wire DTOs
1. Edit `src/Animora.Domain/Identity/Tenant.cs`: add `public string? Code { get; private set; }` and `public void SetCode(string code) { ArgumentException.ThrowIfNullOrWhiteSpace(code); Code = code.Trim(); }`. (Additive; local path leaves it null.)
2. Create `src/Animora.Contracts.Wire/Auth/` DTOs (all `sealed record`, in `namespace Animora.Contracts.Wire.Auth`):
   - `LoginRequest(string TenantCode, string UserName, string Password, string DeviceInstallationIdHash)`
   - `LoginResponse(string AccessToken, int ExpiresInSeconds, string RefreshToken, string UserId, string TenantId, string DeviceId, string Role, bool MustChangePassword)`
   - `RefreshRequest(string RefreshToken)`
   - `DeviceRegisterRequest(string DeviceName, string InstallationIdHash, string Platform)`
   - `DeviceRegisterResponse(string DeviceId, string Status)`  // Status = Device.Status.ToString()
   (refresh returns the same `LoginResponse` shape per §Design.)
3. `build.sh fast`.
**Push:** `scripts/git/push.sh "feat(contracts): S04 task2 — auth wire DTOs + Tenant.Code"`
**Status:** ✅ DONE (build fast 0/0)

### TASK 3 — RefreshToken + SecurityAudit entities + adapter DTO
Create in `src/Animora.Persistence.PostgreSql/Identity/`:
1. `ServerIdentityUser.cs` — **adapter DTO, NOT an EF entity** (never add a DbSet for it). Plain class with `Id, TenantId, UserName, NormalizedUserName, PasswordHash, SecurityStamp, LockoutEnd (DateTimeOffset?), AccessFailedCount`. Used as `TUser` for `UserManager`/`PasswordHasher<ServerIdentityUser>`.
2. `RefreshToken.cs` — **plain entity, NOT SyncableEntity** (no Version/Deleted). Props: `string Id (ULID), string TenantId, string UserId, string DeviceId, string TokenHash (SHA-256 of opaque token, base64/hex), string FamilyId, DateTimeOffset IssuedUtc, DateTimeOffset ExpiresUtc, string? RotatedToId, DateTimeOffset? RevokedUtc`. **Append-only except the two nullable rotation columns** (`RotatedToId`, `RevokedUtc`). Factory `static RefreshToken Issue(tenantId,userId,deviceId,tokenHash,familyId,issuedUtc,expiresUtc)`; methods `MarkRotated(string newId, DateTimeOffset)`, `Revoke(DateTimeOffset)`.
3. `SecurityAuditEntry.cs` — **plain entity, NOT SyncableEntity, append-only forever.** Props: `string Id (ULID), string TenantId, DateTimeOffset OccurredUtc, string? ActorUserId, string? DeviceId, string Category, string Action, string Outcome, string CorrelationId, string? DetailJson`. **DetailJson carries NO secrets/PII bodies / no password/token/hash.**
   - Audit action constants (static class `SecurityAuditActions`): `auth.login.success`, `auth.login.failure`, `auth.login.locked`, `auth.refresh.success`, `auth.refresh.reuse_detected`, `auth.logout`, `auth.device.registered`, `auth.device.revoked_contact`.
**Push:** `scripts/git/push.sh "feat(persistence): S04 task3 — RefreshToken/SecurityAudit entities + ServerIdentityUser adapter"`
**Status:** ✅ DONE (build full 0/0). Added `SecurityAuditActions` constants + `RefreshToken.Issue` takes explicit `id` (ULID generated by caller). StaffMembership/RoleDefinition permission collections are `private readonly HashSet<string>` backing fields exposed as `IReadOnlySet<string>` — TASK 4 must map them as child tables via navigation to the backing field (EF primitive-collection-to-table needs a backing-field collection navigation, not `HasMany`).

### TASK 4 — EF Configurations
Create in `src/Animora.Persistence.PostgreSql/Configurations/` (all `IEntityTypeConfiguration<T>`, `public sealed`, pattern = `TenantConfiguration.cs`):
1. `UserConfiguration` → table `"Users"`. `HasKey(Id)`. `UserName` required maxlen 128; `DisplayName` required maxlen 200; `PasswordHash` maxlen 256 nullable; `SecurityStamp` required `character varying(26)`; `Status` int enum (convention handles); `LastLoginUtc`/`LockoutEndUtc` timestamptz(6) nullable; `FailedAttemptCount` int; `MustChangePassword` bool. **Ignore the private `_firstFailureUtc` field** (`builder.Ignore(...)` — it has no public property, so EF won't map it; confirm no shadow prop is generated; if EF complains, map it as a shadow/backing column OR ignore). Unique index `(TenantId, NormalizedUserName)` — **note:** domain `User.UserName` is already lowercased; store a normalized column. Simplest: add unique index on `(TenantId, UserName)` via `ServerModelConventions.TenantIndex(builder, nameof(User.UserName)).IsUnique();` (UserName is normalized lowercase at Create). Convention already sets Id/TenantId/Version/UpdatedAt/Deleted + Tenant/SoftDelete filters since `User : SyncableEntity`.
2. `StaffMembershipConfiguration` → `"StaffMemberships"` + child tables `StaffMembershipExtraPermissions` / `StaffMembershipDeniedPermissions` (composite PK: `(StaffMembershipId, PermissionCode)`). Map `ExtraPermissionCodes`/`DeniedPermissionCodes` as owned/collection or secondary tables — simplest: `builder.OwnsMany` or a value-collection table. Index `(TenantId, UserId)`.
3. `RoleDefinitionConfiguration` → `"RoleDefinitions"` + child `RoleDefinitionPermissions` (PK `(RoleDefinitionId, PermissionCode)`). Index `(TenantId, Role)` unique.
4. `DeviceConfiguration` → `"Devices"`. Unique index `(TenantId, InstallationIdHash, UserId)`. `Platform` maxlen 64, `DeviceName` maxlen 128, `RevocationReason` maxlen 256 nullable.
5. `RefreshTokenConfiguration` → `"RefreshTokens"`. **NOT SyncableEntity** — manually: `HasKey(Id)`; `Id`/`TenantId`/`UserId`/`DeviceId`/`FamilyId`/`RotatedToId` = `character varying(26)`; `TokenHash` = `character varying(64)` (SHA-256 hex) unique; timestamptz(6) for `IssuedUtc`/`ExpiresUtc`/`RevokedUtc`. **Manually add tenant query filter** `HasQueryFilter(ServerModelConventions.TenantFilterName, ...)` scoping to context tenant — BUT the convention only auto-filters SyncableEntity; so set the filter here referencing `AnimoraServerDbContext.CurrentTenantId`. Easiest: expose an overload or add this filter in `OnModelCreating` after `ApplyTenancy`. **DECISION:** add these two non-syncable filters in `AnimoraServerDbContext.OnModelCreating` after `ApplyTenancy` (so the accessor `() => CurrentTenantId` stays rooted on the context). Indexes: `(TenantId, UserId)`, `(TenantId, FamilyId)`, `(TenantId, TokenHash)`.
6. `SecurityAuditConfiguration` → `"SecurityAudit"`. **NOT SyncableEntity, append-only.** `HasKey(Id)`; all id cols `character varying(26)`; `Category`/`Action`/`Outcome` maxlen 64; `CorrelationId` maxlen 64; `DetailJson` `jsonb` nullable (or text). Index `(TenantId, OccurredUtc)`. Tenant filter added in context (same as RefreshTokens).
**Push:** `scripts/git/push.sh "feat(persistence): S04 task4 — EF configurations for identity tables"`
**Status:** ⬜ TODO

### TASK 5 — DbContext DbSets + non-syncable tenant filters
Edit `src/Animora.Persistence.PostgreSql/Context/AnimoraServerDbContext.cs`:
1. Add DbSets: `DbSet<User> Users`, `DbSet<StaffMembership> StaffMemberships`, `DbSet<RoleDefinition> RoleDefinitions`, `DbSet<Device> Devices`, `DbSet<RefreshToken> RefreshTokens`, `DbSet<SecurityAuditEntry> SecurityAudit`.
2. In `OnModelCreating`, AFTER `ServerModelConventions.ApplyTenancy(...)`, add tenant query filters for the two non-syncable entities: `modelBuilder.Entity<RefreshToken>().HasQueryFilter(ServerModelConventions.TenantFilterName, e => e.TenantId == CurrentTenantId);` and same for `SecurityAuditEntry`. (`DeviceStatus`/`MembershipStatus`/`StaffRole`/`UserStatus` enums map to int via convention since they're on SyncableEntity; RefreshToken/SecurityAudit have none.)
3. `build.sh fast`.
**Push:** `scripts/git/push.sh "feat(persistence): S04 task5 — identity DbSets + non-syncable tenant filters"`
**Status:** ⬜ TODO

### TASK 6 — ServerUserStore + ITenantDirectory + RefreshTokenService + SecurityAuditWriter + TenantProvisioner
Create in `src/Animora.Persistence.PostgreSql/Identity/`:
1. `ServerUserStore.cs` — `sealed class ServerUserStore : IUserStore<ServerIdentityUser>, IUserPasswordStore<ServerIdentityUser>, IUserSecurityStampStore<ServerIdentityUser>, IUserLockoutStore<ServerIdentityUser>`. Reads/writes Domain `User` via `AnimoraServerDbContext`. Maps `User` ↔ `ServerIdentityUser`. Lockout policy mirrors domain constants (5 / 15min / 15min); failures call `User.RegisterFailedAttempt(clock)`. All async methods: `CancellationToken ct = default`, `ConfigureAwait(false)`. `GetLockoutEndDateAsync`/`IncrementAccessFailedCountAsync`/`ResetAccessFailedCountAsync`/`GetAccessFailedCountAsync`/`SetLockoutEndDateAsync`/`GetSecurityStampAsync`/`SetSecurityStampAsync`/`FindByNameAsync` etc. **Lookup is tenant-scoped via the context's tenant filter** (the login service binds tenant before user lookup — see §4).
2. `ITenantDirectory.cs` + `TenantDirectory.cs` — `interface ITenantDirectory { Task<Result<Tenant>> FindByCodeAsync(string code, CancellationToken ct = default); }`. **The ONLY unfiltered read of `Tenants`.** Implemented via a dedicated `TenantDirectoryContext` registered **WITHOUT the tenant filter** (own `DbContext` subclass that maps `Tenant` with no tenant query filter, exposing only this lookup). **NEVER `IgnoreQueryFilters()`** (the S02 interceptor bans it). Resolve tenant BEFORE user lookup. Returns `Result.Fail(CredentialsInvalid)` on miss (no enumeration).
3. `RefreshTokenService.cs` — `sealed`. Issues 256-bit random base64url token; stores only SHA-256. `IssueAsync(tenantId,userId,deviceId,familyId?,ct)` → `(string wireToken, RefreshToken row)`; `RotateAsync(presentedToken, ...)` — looks up by hash; if already rotated (`RotatedToId != null`) ⇒ **reuse:** revoke whole family (all rows with FamilyId), caller rotates `User.SecurityStamp`, writes `auth.refresh.reuse_detected`, returns `Result.Fail(SessionExpired)`; else mark old `RotatedToId`, issue new row (same FamilyId). Lifetimes: access **15 min**, refresh **30 days**, sliding disabled.
4. `SecurityAuditWriter.cs` — `sealed class SecurityAuditWriter : ISecurityAuditWriter`. `interface ISecurityAuditWriter { Task WriteAsync(SecurityAuditEntry entry, CancellationToken ct = default); }` (define seam in same file or `Animora.Application`? — keep in `Animora.Persistence.PostgreSql.Identity` to avoid layering churn; §Design lists `ISecurityAuditWriter` seam). Writes in the **same transaction** as the state change (accept the DbContext/UoW; failure paths use a short separate transaction). **Never writes a password/token/hash.**
5. `TenantProvisioner.cs` — `internal`/`public` service used by S15/S19 and tests: `CreateTenantWithOwnerAsync(displayName, tenantCode, ownerUserName, ownerDisplayName, ownerPassword, IClock, ct)` → creates `Tenant` (+ `SetCode`), system `RoleDefinition`s from `DefaultRolePermissions.For(role)` for all `StaffRole`s, Manager `User` (password hashed via `PasswordHasher<ServerIdentityUser>`, validated via `PasswordPolicy.Validate`) + Manager `StaffMembership`, all in ONE UoW. **No public sign-up endpoint** (R-B36 intake is S15).
**Push:** `scripts/git/push.sh "feat(persistence): S04 task6 — ServerUserStore, TenantDirectory, RefreshTokenService, SecurityAuditWriter, TenantProvisioner"`
**Status:** ⬜ TODO

### TASK 7 — Migration `102_Identity`
1. `.sandbox/tools/dotnet-ef migrations add Identity --project src/Animora.Persistence.PostgreSql --startup-project src/Animora.Persistence.PostgreSql --context AnimoraServerDbContext --output-dir Migrations/Server`
2. **Rename** generated files/class/id to next free ordinal: `ls src/Animora.Persistence.PostgreSql/Migrations/Server` → rename to `<timestamp>_102_Identity.cs/.Designer.cs`, class `_102_Identity`, and the `[Migration("..._102_Identity")]` id. (Match on the `NNN` token.)
3. **Hand-add in Up():** `VersionTrigger.Apply(migrationBuilder, "Users"/"StaffMemberships"/"RoleDefinitions"/"Devices")` (the 4 SyncableEntity tables — NOT RefreshTokens/SecurityAudit). Hand-add the **immutable trigger** for `SecurityAudit`: `CREATE TRIGGER trg_SecurityAudit_immutable BEFORE UPDATE OR DELETE ON "SecurityAudit" FOR EACH ROW EXECUTE FUNCTION ...` — need a function that `RAISE EXCEPTION`. Create a small immutable guard function in this migration (e.g. `animora_securityaudit_immutable()` raising `'SecurityAudit is append-only'`). In Down(): `VersionTrigger.Drop(...)` for the 4 tables + `DROP TRIGGER/FUNCTION` for the immutable guard. Also `Tenants.Code` column add (EF generates it; confirm partial unique index present).
4. Edit `ServerSchemaVersion.cs`: `ExpectedHead = "102_Identity"`.
5. `build.sh fast`.
**Push:** `scripts/git/push.sh "feat(persistence): S04 task7 — 102_Identity migration + version/immutable triggers + schema head"`
**Status:** ⬜ TODO

---

## 4. Server layer — endpoints + wiring — **TASK 8**
Create in `src/Animora.Server/Auth/`:
1. `AuthServiceCollectionExtensions.cs` — `public static IServiceCollection AddAnimoraIdentity(this IServiceCollection, IConfiguration)`:
   - `AddIdentityCore<ServerIdentityUser>(opts => { opts.Lockout.MaxFailedAccessAttempts = User.MaxFailedAttempts; opts.Lockout.DefaultLockoutTimeSpan = User.LockoutDuration; opts.Lockout.AllowedForNewUsers = true; ... })` with `.AddUserStore<ServerUserStore>()`.
   - `AddAuthentication(IdentityConstants.BearerScheme).AddBearerToken(IdentityConstants.BearerScheme, o => { o.BearerTokenExpiration = TimeSpan.FromMinutes(15); o.RefreshTokenExpiration = TimeSpan.FromDays(30); })` — opaque, Data-Protection-protected (key ring from S02). **No JwtBearer.** Access-token principal claims: `sub`(UserId), `tid`(TenantId), `did`(DeviceId), `sst`(SecurityStamp), `role`(StaffRole). **Claims are identification only** — S05 re-reads stored role.
   - Register `ITenantDirectory`→`TenantDirectory`, `RefreshTokenService`, `ISecurityAuditWriter`→`SecurityAuditWriter`, `TenantProvisioner`, `PasswordHasher<ServerIdentityUser>`, a `LoginService`.
2. `AuthEndpoints.cs` — `public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)`:
   - Group `app.MapGroup(ServerRoutes.Auth).RequireRateLimiting(RateLimitPolicies.Auth)`.
   - `POST /auth/login` (AllowAnonymous): resolve tenant by `TenantCode` via `ITenantDirectory` → bind tenant → lookup user → verify password (constant-time-ish: dummy hash verify on miss, **same `401 CREDENTIALS_INVALID`** for wrong tenant/user/password, no enumeration). Checks: tenant `Suspended`→`TENANT_SUSPENDED`; user `Disabled`→`USER_DISABLED`; `IsLockedOut`→**HTTP 423** `USER_LOCKED_OUT`; device by `InstallationIdHash` `Revoked`→**403** `DEVICE_REVOKED` (+ `auth.device.revoked_contact`). On success: issue access+refresh, `User.RegisterSuccessfulLogin`, `Device.RecordContact`, audit `auth.login.success`, return `LoginResponse` with active membership `Role`. Failure: `User.RegisterFailedAttempt`, audit `auth.login.failure`/`auth.login.locked`.
   - `POST /auth/refresh` (AllowAnonymous): rotate via `RefreshTokenService`; reuse ⇒ 401 `SESSION_EXPIRED` + family revoke + stamp rotate + `auth.refresh.reuse_detected`; revoked device ⇒ 403 `DEVICE_REVOKED`; `Device.RecordContact` on success; same `LoginResponse` shape; audit `auth.refresh.success`.
   - `POST /auth/logout` (authenticated): revoke the family of the presented/active token; audit `auth.logout`.
   - `POST /auth/device/register` (authenticated): create `Device` in `Pending`; audit `auth.device.registered`; return `DeviceRegisterResponse`.
   - All failures → `WireApiError(code, detail, [], correlationId)`; HTTP 401/403/423; lockout=423. Auth failures are transport-class (not settlement).
3. Edit `src/Animora.Server/Program.cs`: add `builder.Services.AddAnimoraIdentity(builder.Configuration);` after step 4; add `app.UseAuthentication();` in the reserved authN slot (after request logging, before `app.UseRateLimiter()`); add `app.MapAuthEndpoints();`.
4. `build.sh full` → **0 warnings / 0 errors**. `run.sh` → `SMOKE RUN OK`.
**Push:** `scripts/git/push.sh "feat(server): S04 task8 — auth endpoints + AddAnimoraIdentity + pipeline wiring"`
**Status:** ⬜ TODO

---

## 5. Tests — **TASK 9**
Create under `tests/Animora.Server.Tests/Identity/`. Pure tests use in-memory fakes of store seams; DB behavior uses `[PgFact]` + `PgFixture`.
**Pure (`[Fact]`):**
- `ServerUserStoreTests.Lockout_After5Failures_For15Min`
- `LoginServiceTests.UnknownTenant_UnknownUser_WrongPassword_ReturnSameError` (all 3 ⇒ identical `CREDENTIALS_INVALID`)
- `RefreshTokenServiceTests.Rotation_IssuesNewToken_StoresOnlyHash`
- `RefreshTokenServiceTests.Reuse_RevokesFamily_RotatesSecurityStamp_Audits`
- `LoginServiceTests.RevokedDevice_Returns_DEVICE_REVOKED`
- `AuthEndpointsTests.Login_RateLimited_Returns429` (WebApplicationFactory<Program>)
- `AuthEndpointsTests.Failure_Returns_WireApiError_WithCorrelationId`
- `TokenClaimsTests.NoRoleTrustedFromClaim_Marker` (documents S05 re-reads stored role)
- `AuditRedactionTests.NoSecretsInDetailJson` (no password/token/hash in any audit DetailJson)
**PG-gated (`[PgFact]`):**
- `IdentityMigrationTests.SecurityAudit_UpdateAndDelete_Raise`
- `IdentityMigrationTests.Users_UniquePerTenantUsername`
- `AuthFlowTests.Login_Refresh_Logout_EndToEnd`
- `AuthFlowTests.CrossTenant_Username_Reuse_Allowed`

Run: `scripts/sandbox/test.sh Server` → green (PG-gated skip locally with reason unless `ANIMORA_PG_TEST_CONNECTION` set).
**Push:** `scripts/git/push.sh "test(server): S04 task9 — identity pure + PG-gated tests"`
**Status:** ⬜ TODO

---

## 6. Final verification + PROGRESS — **TASK 10**
1. Run acceptance checks §A below.
2. Edit `backend-roadmap/PROGRESS.md`:
   - "Phase status": set `S04` row → `✅ Done` (add date `2026-10-09` + one-line summary).
   - Advance "Next phase" → `S05`.
   - Append one `### S04` entry to "Repo state log": projects touched, tables (`Users, StaffMemberships(+2 child), RoleDefinitions(+1 child), Devices, RefreshTokens, SecurityAudit`, `Tenants.Code`), migration ordinal `102_Identity`, error codes added (none — reused), claim names (`sub|tid|did|sst|role`), seams (`ITenantDirectory, ISecurityAuditWriter, TenantProvisioner, RefreshTokenService`), test classes.
   - Log any deviation in "Decision & deviation log".
   - Update the `src/Animora.Persistence.PostgreSql/` and `src/Animora.Server/` rows of "Current state" to mention S04.
**Push:** `scripts/git/push.sh "feat(server): S04 identity server"`
**Status:** ⬜ TODO

---

## 7. Delete this plan file — **TASK 11 (final)**
`rm /home/user/webapp/Plan/S04-identity-server-todo.md` (do **not** touch the roadmap phase file or any other file).
**Push:** `scripts/git/push.sh "chore: remove S04 plan file (phase complete)"`
**Status:** ⬜ TODO

---

## §A. Acceptance criteria (mechanical — verify before TASK 10 push)
1. `scripts/sandbox/build.sh full` = 0 warnings / 0 errors; `run.sh` = `SMOKE RUN OK`; client projects unchanged: `git diff --stat -- src/Animora.Domain src/Animora.Persistence.Sqlite src/Animora.Desktop` — **only `Tenant.cs` under Domain changes** (the `Code` addition — acceptable, additive; if reviewer insists Domain stays byte-identical, instead map `Code` as a shadow property and drop the Tenant.cs edit — but shadow props can't be read by `TenantDirectory` cleanly, so Tenant.cs edit is the chosen path). Persistence.Sqlite + Desktop must be empty.
2. `grep -rn "JwtBearer\|OpenIdConnect" src` → nothing. `grep -rn "NewGuid()" src/Animora.Server src/Animora.Persistence.PostgreSql | grep -v "gen_random_uuid\|StringGuid\|Version"` → nothing relevant to ids (RefreshToken wire token uses `RandomNumberGenerator`, not Guid).
3. `ls src/Animora.Persistence.PostgreSql/Migrations/Server | grep -c Identity` ≥ 1, ordinal `102` (next free after `101_ServerFoundation`).
4. The 8 pure tests pass.
5. The 4 PG-gated tests pass under PG (skip locally with reason).
6. `AuditRedactionTests.NoSecretsInDetailJson` passes.

## §B. Guardrails (binding)
- **Forbidden:** Redis, broker, cloud IdP, cloud telemetry, CDN, EF Core 11, per-tenant DB, hard-coded TLS, Native AOT+EF, JWT/OIDC, `UpdatedAt`/`nextval`/`xmin` sync cursors.
- **Layering:** `Animora.Server` must NOT reference `Client.Services`/`Persistence.Sqlite`/`Infrastructure.Local`/`Sync.Custom`/`Desktop`. Only `Persistence.PostgreSql` references Npgsql.
- **C# style:** `sealed` default; `sealed record` DTOs; nullable honored (no `!` w/o why-comment); async public ⇒ `CancellationToken ct = default` + `ConfigureAwait(false)` in libs; no `.Result`/`.Wait()`; expected failures ⇒ `Result`/`Result<T>` + `ErrorCode`, never exceptions; `IClock` injected (no `DateTime.Now`); ULID ids via `IdGenerator`, never `Guid` in business code.
- **EF:** EF Core 10; no lazy loading; reads `AsNoTracking()`+projections; filters `"Tenant"`/`"SoftDelete"` separable; paging max 200; migrations additive, never edited later; no `EnsureCreated` outside test fixtures.
- **Security:** AuthN→AuthZ→Entitlement order (this phase = AuthN only); `TenantId` server-side from membership; secrets never in config/logs; fail closed; no custom crypto; append-only audit for every security action; **no password/token/hash ever logged or in audit DetailJson.**

## §C. Out of scope (do NOT implement — S05/S15/etc.)
Authorization policies, stored-RBAC eval, tenant-context middleware, `ITenantContext` request binding, entitlement slot (S05); device approval command + capability-snapshot signing (S05/G06/S16); public sign-up/provisioning intent (S15); operator login (S14/S19); passkeys/external IdP/JWT (deferred).
