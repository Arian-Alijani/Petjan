# S05 — Tenant Context & Authorization — Execution TODO (self-contained)

> **Purpose of this file.** A pre-researched, task-by-task checklist to implement phase
> `backend-roadmap/phases/PHASE-S05-tenant-context-authorization.md`. Every fact, signature,
> path, and decision you need is inlined here — **you should not have to re-open the repo to
> "understand" it.** Open a file only to *edit* it, or when a compile error proves a snippet below
> diverges from the tree (then fix the smallest thing). Implement exactly this scope, top to bottom.
>
> **PUSH RULE (locked, non-negotiable):** after finishing each task below, run
> `scripts/git/push.sh "feat(server): S05 — <task summary>"` and only then move on. A heavy fix or a
> large code block mid-task also gets its own push with a clear message. Not pushing = project death.
>
> **Status legend:** `[ ]` pending · `[~]` in progress · `[x]` done.

---

## 0. Context you already have (do not re-derive)

**What S05 delivers (one sentence each):**
1. Bind every protected request to a tenant **only via server-stored active membership** — the token's
   `tid`/`role` claims are never trusted for authority.
2. Expose a request-scoped `ITenantContext` (`RequestTenantContext`) that replaces `AuthTenantContext`.
3. Evaluate authorization from **stored** RBAC (role definition ∪ extras − denies) via the existing
   `PermissionEvaluator` (reused from `Animora.Application`, server-side).
4. Register the named policy set + the `RequiresPermission` → ASP.NET authorization bridge + a frozen
   `CommandPermissionTable` (starts empty).
5. Add the **entitlement slot** (`IServerEntitlementGate`, allow-all until S16).
6. Enforce the three planes in order: **authN → tenant → authZ → entitlement**.
7. Device approve/revoke endpoints + capability-snapshot issuance + public-key endpoint, backed by a new
   `SigningKeys` table (migration) and an asymmetric `ISnapshotSigner` (Ed25519/ECDSA-P256).

**Baseline (verified 2026-10-09 against the tree):**
- Server migration head today = `102_Identity`; `ServerSchemaVersion.ExpectedHead = "102_Identity"`.
  **S05 adds `103_SigningKeys` and bumps `ExpectedHead` to `"103_SigningKeys"`.**
- `Animora.Server` exists (S01/S02/S04 done). Folders today: `Auth/`, `Hosting/`. **S05 adds
  `Authorization/`, `Signing/`, and more files under `Auth/`.**
- `src/` projects exist incl. `Animora.Server`, `Animora.Persistence.PostgreSql`. `Animora.Server.csproj`
  references Domain + Application + Contracts.Wire + Persistence.PostgreSql, and `InternalsVisibleTo
  Animora.Server.Tests`.
- Test project `tests/Animora.Server.Tests` exists (xunit + `Microsoft.AspNetCore.Mvc.Testing`),
  references `Animora.Server` + `Animora.Persistence.PostgreSql`. Existing sub-dirs: `Identity/`,
  `Persistence/`. **S05 adds `Authorization/`.** PG-gated tests follow the `Persistence/PgFixture.cs` +
  `PgGatedTests.cs` pattern (skip locally without `ANIMORA_PG_TEST_CONNECTION`).

**Environment / build (never raw `dotnet build`; memory-capped sandbox):**
- Build: `scripts/sandbox/build.sh fast` (quick) / `scripts/sandbox/build.sh full` (0 warn/0 err gate).
- Test: `scripts/sandbox/test.sh Server` (filter by FQN substring). `scripts/sandbox/test.sh Authorization`
  runs just the new tests.
- Smoke: `scripts/sandbox/run.sh` → must print `SMOKE RUN OK`.
- EF tool: `.sandbox/tools/dotnet-ef` **only** (never `dotnet tool install`).
- **Do not run "Full test"/full build repeatedly** (user rule). Use `fast` + the `Server`/`Authorization`
  filters while iterating; run `full` + smoke once near the end (Task 14).

**Layering rules that will bite you (CONVENTIONS §2):**
- `Animora.Server` must NEVER reference `Animora.Client.Services`, `Animora.Persistence.Sqlite`,
  `Animora.Infrastructure.Local`, `Animora.Sync.Custom`, `Animora.Desktop`. (`ServerLayeringTests`
  enforces this.) ⇒ The server-side RBAC repos must be **new PG implementations**, NOT reused from Sqlite.
- `Animora.Domain` references nothing. `Animora.Persistence.PostgreSql` is the only project that may use
  Npgsql/`UseNpgsql`.
- Acceptance #6: `git diff --stat -- src/Animora.Domain src/Animora.Application src/Animora.Persistence.Sqlite
  src/Animora.Desktop` must be **empty**. So: **touch none of those four projects.** New server-side types
  live in `Animora.Server`; the new table/entity/config/migration live in `Animora.Persistence.PostgreSql`.

**C# style (CONVENTIONS §4):** `sealed` by default; `sealed record` DTOs; nullable honored (no `!`
without a why-comment); every async public library method takes `CancellationToken ct = default` and uses
`ConfigureAwait(false)`; no `.Result`/`.Wait()`; expected failures return `Result`/`Result<T>` with an
`ErrorCode`, never exceptions; inject `IClock`/`TimeProvider` (no `DateTime.Now/UtcNow`); ids are ULID
strings via `IdGenerator`, never `Guid` in business code.

---

## 1. Pre-verified reference pack (exact names/signatures from the tree)

### 1.1 Domain (`Animora.Domain`, DO NOT EDIT)
```csharp
// Animora.Domain.Identity
enum StaffRole { Manager=0, Doctor=1, Receptionist=2, Cashier=3, Accountant=4 }
enum MembershipStatus { Active=0, Suspended=1, Ended=2 }
enum DeviceStatus { Pending=0, Active=1, Revoked=2 }
enum RiskLevel { /* Low, Normal, High, Critical */ }   // namespace Animora.Domain.Identity

static class PermissionCodes {
  const string DevicesManage = "devices.manage";        // the code guarding device endpoints
  static bool Exists(string code);                      // catalog membership
  static RiskLevel RiskOf(string code);                 // Critical/High sets, else .read=Low else Normal
  // ...88 codes grouped by #region; reference constants, never literals
}
static class OfflineCapabilityPolicy {
  static readonly TimeSpan LowWindow = 30d, NormalWindow = 14d, HighWindow = 72h, CriticalWindow = Zero;
  static TimeSpan WindowFor(RiskLevel risk);
  static TimeSpan WindowForCode(string code);           // = WindowFor(PermissionCodes.RiskOf(code))
}
sealed record CapabilitySnapshot(                         // the DOMAIN record S05's wire payload reuses
  string TenantId, string UserId, string DeviceId, StaffRole Role,
  IReadOnlyDictionary<string,DateTimeOffset> CapabilityExpiry,
  DateTimeOffset IssuedUtc, DateTimeOffset SnapshotExpiryUtc,
  string SecurityStamp, string Signature) {
  bool Allows(string code, DateTimeOffset nowUtc);
  bool IsExpired(DateTimeOffset nowUtc);
  static IReadOnlyDictionary<string,DateTimeOffset> BuildExpiry(IEnumerable<string> codes, DateTimeOffset issuedUtc);
}
sealed class StaffMembership : AggregateRoot {
  string UserId; StaffRole Role; MembershipStatus Status; bool IsActive; // (Active && EndedUtc==null)
  IReadOnlySet<string> ExtraPermissionCodes; IReadOnlySet<string> DeniedPermissionCodes;
  string TenantId; // inherited
}
sealed class RoleDefinition : AggregateRoot {
  StaffRole Role; IReadOnlySet<string> PermissionCodes; bool IsSystem; string TenantId;
}
sealed class Device : AggregateRoot {
  string UserId; string DeviceName; string InstallationIdHash; string Platform;
  DeviceStatus Status; bool IsUsable; // (Status==Active)
  DateTimeOffset RegisteredUtc; DateTimeOffset? LastContactUtc; DateTimeOffset? RevokedUtc; string? RevocationReason;
  void Approve();                      // throws if Revoked
  void Revoke(string reason, IClock clock);  // idempotent
}
sealed class User : AggregateRoot {
  string SecurityStamp;                 // current stamp
  void RotateSecurityStamp();           // => SecurityStamp = IdGenerator.NewId()  (invalidates live tokens)
  // ...
}
```

```csharp
// Animora.Domain.Results — reuse, never add new codes this phase
readonly record struct ErrorCode(string Code, string Category);
readonly record struct Result      { static Result Ok(); static Result Fail(ErrorCode e, string? detail=null); bool IsSuccess/IsFailure; ErrorCode Error; string? Detail; }
readonly record struct Result<T>   { static Result<T> Ok(T v); static Result<T> Fail(ErrorCode e, string? detail=null); T? Value; ... }
// ErrorCodes already declared (reuse): PermissionDenied="PERMISSION_DENIED"(Forbidden),
//   PermissionUnknown="PERMISSION_UNKNOWN"(Forbidden), TenantMismatch="TENANT_MISMATCH"(Forbidden),
//   EntitlementRequired="ENTITLEMENT_REQUIRED"(Entitlement), TenantSuspended="TENANT_SUSPENDED"(Forbidden),
//   UserDisabled="USER_DISABLED"(Forbidden), MembershipNotActive="MEMBERSHIP_NOT_ACTIVE"(Forbidden),
//   DeviceRevoked="DEVICE_REVOKED"(Forbidden), SessionExpired="SESSION_EXPIRED"(Forbidden),
//   ConcurrencyConflict="CONCURRENCY_CONFLICT"(Conflict).
```

### 1.2 Application security plane (`Animora.Application.Security`, DO NOT EDIT — reuse)
```csharp
sealed record AuthorizationSubject(string TenantId, string UserId, IReadOnlySet<string> Permissions,
  StaffRole Role, string DeviceId, bool IsOffline);
sealed record AuthorizationDecision(bool Allowed, string? DeniedByRule, string PermissionCode) {
  static AuthorizationDecision Allow(string code); static AuthorizationDecision Deny(string code, string rule);
}
interface IPermissionEvaluator {
  Task<AuthorizationDecision> EvaluateAsync(string permissionCode, AuthorizationSubject subject, object? resource, CancellationToken ct);
  Task<IReadOnlySet<string>> AllowedAsync(IReadOnlyCollection<string> permissionCodes, AuthorizationSubject subject, CancellationToken ct); // batched
}
// Concrete: sealed class PermissionEvaluator(IStaffMembershipRepository memberships, IRoleDefinitionRepository roles,
//   IResourceAuthorizationRule<Invoice> invoiceRule, IResourceAuthorizationRule<CashSession> cashSessionRule)
//   — recomputes grants from STORE; caller-supplied Permissions/Role are never trusted.
[AttributeUsage(AttributeTargets.Class)] sealed class RequiresPermissionAttribute(string permissionCode) { string PermissionCode; }
interface IResourceAuthorizationRule<in TResource> where TResource:class { string PermissionCode; bool IsSatisfied(AuthorizationSubject s, TResource r); }
interface IEntitlementGate { ValueTask<Result> EvaluateAsync(object command, CommandContext ctx, CancellationToken ct); ValueTask<Result> EvaluateAsync(object query, QueryContext ctx, CancellationToken ct); }
// Application DI already registers (TryAdd, so server can register its own repos first):
//   IPermissionEvaluator -> PermissionEvaluator, IResourceAuthorizationRule<Invoice> -> InvoiceAuthorizationRule,
//   IResourceAuthorizationRule<CashSession> -> CashSessionAuthorizationRule.
// BUT PermissionEvaluator's repo deps IStaffMembershipRepository / IRoleDefinitionRepository have
// NO PostgreSql implementation today (only Sqlite — forbidden to reference). *** S05 must add PG impls. ***
```
Application ports (`Animora.Application.Ports`, DO NOT EDIT):
```csharp
interface IStaffMembershipRepository {
  Task<StaffMembership?> FindAsync(string id, CancellationToken ct=default);
  Task<StaffMembership?> FindActiveAsync(string tenantId, string userId, CancellationToken ct=default);
  void Add(StaffMembership entity);
}
interface IRoleDefinitionRepository {
  Task<RoleDefinition?> FindByRoleAsync(string tenantId, StaffRole role, CancellationToken ct=default);
}
```

### 1.3 Persistence (`Animora.Persistence.PostgreSql`)
```csharp
// Tenancy
interface ITenantContext { string? TenantId; bool IsOperator; }      // namespace ...Tenancy
sealed class NullTenantContext : ITenantContext { null; false; }      // S02 default
sealed class AuthTenantContext : ITenantContext { Bind(string); }     // S04 scoped (S05 REPLACES in the auth-free pipeline)
// AnimoraServerDbContext(options, ITenantContext) : DbContext
//   DbSet<Tenant/User/StaffMembership/RoleDefinition/Device/RefreshToken/SecurityAuditEntry>
//   CurrentTenantId => tenantContext.TenantId ?? throw TenantContextMissingException;
//   OnModelCreating: ApplyConfigurationsFromAssembly + ServerModelConventions.ApplyTenancy + manual filters on RefreshToken/SecurityAuditEntry.
// ServerModelConventions: ApplyTenancy only touches SyncableEntity types; SigningKey is NOT SyncableEntity
//   so it is auto-skipped (no tenant filter) — exactly what we want (global table). Template for a
//   non-tenant global table: see SecurityAuditEntry (but SecurityAuditEntry DOES get a manual tenant filter;
//   SigningKey must NOT — see Task 10).
// ServerSchemaVersion.ExpectedHead = "102_Identity"  (S05 -> "103_SigningKeys")
// PersistenceServiceCollectionExtensions: OltpWriteKey="oltp-write", OltpReadKey="oltp-read", KeyRingKey="keyring";
//   AddAnimoraPostgreSql registers ITenantContext->NullTenantContext (TryAdd), AnimoraServerDbContext over oltp-write pool.
// IdentityPersistenceServiceCollectionExtensions.AddAnimoraIdentityPersistence(): Replaces ITenantContext with
//   scoped AuthTenantContext; registers IIdentityRepository->EfIdentityRepository, ISecurityAuditWriter->SecurityAuditWriter,
//   ITenantDirectory->TenantDirectory, RefreshTokenService, ServerUserStore, TenantProvisioner, IPasswordHasher.
```
```csharp
// Audit (reuse)
interface ISecurityAuditWriter { Task<Result> WriteAsync(SecurityAuditEntry entry, CancellationToken ct=default); } // atomic w/ UoW
sealed class SecurityAuditEntry {
  static SecurityAuditEntry Create(string id, string tenantId, DateTimeOffset occurredUtc, string category,
    string action, string outcome, string correlationId, string? actorUserId=null, string? deviceId=null, string? detailJson=null);
}
static class SecurityAuditActions { const string Category="auth"; OutcomeSuccess="success"; OutcomeFailure="failure";
  DeviceRegistered="auth.device.registered"; /* add new action strings locally in Server, e.g. "security.tenant_hint_ignored" */ }
static class SecurityAuditDetail { static string? Create(params (string Key,string? Value)[] entries); } // rejects secret-like keys
// Identity repo (tenant-scoped, tracked reads):
interface IIdentityRepository {
  Task<User?> FindUserByIdAsync(string userId, CancellationToken ct=default);
  Task<StaffMembership?> FindActiveMembershipAsync(string userId, CancellationToken ct=default);
  Task<Device?> FindDeviceByIdAsync(string deviceId, CancellationToken ct=default);
  Task<Result> SaveAsync(CancellationToken ct=default); /* +FindUserByNameAsync, FindDeviceByInstallationAsync, AddDevice */
}
```

### 1.4 Server (`Animora.Server`)
```csharp
// Auth/ (S04)
static class AuthClaimTypes { Sub="sub"; Tid="tid"; Did="did"; Sst="sst"; Role="role"; }  // internal
sealed record AuthenticatedCaller(string UserId, string TenantId, string DeviceId, string SecurityStamp) {
  static AuthenticatedCaller? TryRead(ClaimsPrincipal?); // null if anonymous/incomplete
}
static class AuthServiceCollectionExtensions { AddAnimoraIdentity(services, config); static readonly TimeSpan AccessTokenLifetime=15min; } // internal
static class AuthEndpoints { MapAuthEndpoints(app); static int StatusFor(ErrorCode); static IResult Fail(HttpContext, ErrorCode); } // internal
// Hosting/
static class ServerRoutes { Auth="/auth"; Commands="/api/commands"; HealthLive/HealthReady; ... }
sealed class ServerClock(TimeProvider) : IClock { }  // internal, in Auth/
// Program.cs pipeline today (order): GlobalExceptionMiddleware -> (HSTS/HTTPS if !Dev) -> UseAuthentication()
//   -> UseRateLimiter() -> [OpenAPI] -> MapAuthEndpoints -> health endpoints -> Run.
//   *** S05 inserts: TenantContextMiddleware after UseAuthentication(), UseAuthorization() + EntitlementMiddleware
//       after it, before UseRateLimiter(). Health + /auth/login|refresh stay anonymous & skip tenant/entitlement. ***
```

### 1.5 Wire contracts (`Animora.Contracts.Wire.Auth`)
Existing files: `LoginRequest/LoginResponse/RefreshRequest/DeviceRegisterRequest/DeviceRegisterResponse`.
**S05 ADDS (additive) to namespace `Animora.Contracts.Wire.Auth`:**
`PublicKeySet(IReadOnlyList<PublicKeyInfo> Keys)` and
`PublicKeyInfo(string KeyId, string Algorithm, string PublicKeyDerBase64, DateTimeOffset? NotAfterUtc)`.
> These are Wire contracts and live in `Animora.Contracts.Wire` (allowed — Server references it). They do
> NOT count against the acceptance-#6 "untouched projects" list (that list is Domain/Application/Sqlite/Desktop only).

---

## 2. Design decisions (fixed — copy these names/shapes exactly)

**New namespaces:** `Animora.Server.Authorization`, `Animora.Server.Signing`.

### 2.1 Tenant binding (`TenantContextMiddleware`)
- Reads `sub`,`did`,`sst` from the authenticated principal (via `AuthenticatedCaller.TryRead` or directly).
  **Ignore `tid` for authority.** Load the active membership for the user via `IMembershipResolver`.
- `IMembershipResolver.ResolveAsync(string userId, CancellationToken ct)` returns a small record carrying
  the resolved `TenantId`, the `User` (for stamp + disabled/stamp checks), the active `StaffMembership`,
  the `Device`, and the tenant status — enough to decide accept/reject without further reads. It uses a
  **membership-bypass user lookup keyed on the primary key only** (no cross-tenant enumeration). See Task 5.
- Rejections (status + reused `ErrorCodes`):
  - token `sst` ≠ stored `User.SecurityStamp` → **401** `SESSION_EXPIRED`.
  - no active membership → **403** `MEMBERSHIP_NOT_ACTIVE`.
  - user disabled → **403** `USER_DISABLED`.
  - tenant suspended → **403** `TENANT_SUSPENDED`.
  - device revoked (Status==Revoked / not usable) → **403** `DEVICE_REVOKED`.
  - Write the `WireApiError` body the same way `AuthEndpoints.Fail` does (code + correlation id, no detail).
- On success: set the scoped `RequestTenantContext` (`TenantId` = membership tenant; `IsOperator=false`).
- Any inbound header/body/query `TenantId` that differs from the resolved tenant is **ignored and audited**
  with action `"security.tenant_hint_ignored"` (category `"security"`, outcome success) — don't reject.
  (A command-envelope `TenantId` mismatch is S06's `TENANT_MISMATCH`, not ours.)
- **Skip** the middleware for: health endpoints (`/health/*`), `/auth/login`, `/auth/refresh`, and any
  request with no authenticated principal that targets an anonymous endpoint. Simplest robust rule: skip
  when `context.User` is not authenticated OR the path starts with `/health` or equals `/auth/login`
  `/auth/refresh`. (Authenticated `/auth/logout`, `/auth/device/*`, `/auth/capabilities` DO run it.)

### 2.2 `RequestTenantContext`
```csharp
// namespace Animora.Server.Authorization — scoped; set once by the middleware.
internal sealed class RequestTenantContext : ITenantContext {
  public string? TenantId { get; private set; }
  public bool IsOperator => false;
  public void Bind(string tenantId); // guard: throw if re-bound to a different tenant (mirror AuthTenantContext)
}
```
Registered scoped and used to `Replace` the `ITenantContext` registration (it must win over `AuthTenantContext`
in the server composition). See Task 11 DI.

### 2.3 Stored RBAC
- `ServerAuthorizationSubjectFactory` builds an `AuthorizationSubject` from stored facts:
  `RoleDefinition.PermissionCodes ∪ membership.ExtraPermissionCodes − membership.DeniedPermissionCodes`
  (deny wins; ordinal). The principal's `role` claim is **never read**. Unknown permission code handling is
  the evaluator's job (fail closed → `PERMISSION_UNKNOWN`). The `Permissions` set it fills is advisory only
  (the `PermissionEvaluator` recomputes from store anyway) — set `Role` = membership.Role, `IsOffline=false`.
- `PermissionRequirement(string permissionCode) : IAuthorizationRequirement`.
- `PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>` → resolves
  `IPermissionEvaluator` + the request-scoped subject, calls `EvaluateAsync(code, subject, resource:null, ct)`;
  `Succeed`/`Fail` accordingly.
- `ServerPermissionPolicyProvider : IAuthorizationPolicyProvider` — for a policy name that equals a
  `PermissionCodes.*` value, builds a policy on demand = `new AuthorizationPolicyBuilder()
  .AddRequirements(new PermissionRequirement(name)).Build()`. Fall back to the default provider
  (`DefaultAuthorizationPolicyProvider`) for the coarse named policies + framework policies.
- **Coarse named policies (fixed set, registered up-front via `AddAuthorizationBuilder`):**
  `TenantMember`, `ManagerOnly`, `OperatorOnly` (S14 fills identity — register a placeholder that denies all
  for now, or requires an operator claim that is never issued yet → effectively deny; document it),
  `DeviceActive`. Put the names in a `static class CoarsePolicies`.
  - `TenantMember`: authenticated + tenant bound (the middleware already guarantees this for protected routes)
    → `RequireAuthenticatedUser()`.
  - `ManagerOnly`: requires the stored role == Manager. Implement as a `PermissionRequirement`-style check is
    wrong (it's role, not permission) — instead use an assertion that reads the resolved subject's role.
    Simplest: a dedicated `ManagerOnlyRequirement` + handler that reads the scoped subject/role. (If the
    scoped subject is only built lazily, have the handler resolve `IMembershipResolver`result cached on the
    `RequestTenantContext`/an accessor — see Task 6 for the accessor seam.)
  - `OperatorOnly`: deny by default (no operator identity until S14).
  - `DeviceActive`: device status Active (the middleware already enforces "not revoked"; `DeviceActive`
    additionally rejects `Pending`). Use the resolved device cached on the request accessor.

### 2.4 Command-level authorization bridge
```csharp
// namespace Animora.Server.Authorization
interface ICommandAuthorizer { Task<Result> AuthorizeAsync(string commandType, AuthorizationSubject subject, CancellationToken ct); }
// CommandPermissionTable: IReadOnlyDictionary<string,string> mapping wire CommandType -> required PermissionCodes.*.
//   STARTS EMPTY this phase (S07–S10 append rows). A command type ABSENT from the table is DENIED
//   (PERMISSION_UNKNOWN, fail closed).
sealed class CommandAuthorizer(IPermissionEvaluator evaluator) : ICommandAuthorizer {
  // look up required code in CommandPermissionTable; if absent -> Result.Fail(PermissionUnknown);
  // else evaluator.EvaluateAsync(code, subject, null, ct); map Deny -> PermissionDenied/PermissionUnknown.
}
```
- Invariant test `CommandPermissionCatalogTests.EveryCommandType_HasARequiredPermission` iterates
  `Animora.Contracts.Wire.Commands.CommandTypes.All` and asserts each has a table row. **It ships
  `[Fact(Skip="enabled by S07, mandatory from S10")]`** (S10 removes the skip). Write the test body so that
  when un-skipped it would actually iterate + assert (don't leave an empty body).

### 2.5 Entitlement slot
```csharp
// namespace Animora.Server.Authorization
sealed record TenantEntitlementContext(string TenantId, string UserId, string? CommandType);
interface IServerEntitlementGate { ValueTask<Result> EvaluateAsync(TenantEntitlementContext c, CancellationToken ct); }
sealed class AllowAllServerEntitlementGate : IServerEntitlementGate { => ValueTask<Result>(Result.Ok()); } // registered TryAdd; S16 replaces
```
- `EntitlementMiddleware`: runs AFTER `UseAuthorization()`. For protected routes, call the gate; on failure
  write a `WireApiError` with `ENTITLEMENT_REQUIRED` (coerce any non-success to this code), status 402 or
  403 — **use 403** (consistent with the Forbidden-family; the phase doesn't mandate 402 and `WireApiError`
  carries the stable code). Allow-all means this never trips until S16.
- **Plane order (the single ordering invariant, tested):** unauthenticated → 401 (from `UseAuthentication`
  + endpoint auth) BEFORE any permission check; a denied permission → 403 `PERMISSION_*` BEFORE entitlement;
  entitlement last. `PlaneOrderTests` asserts: deny-all permission + deny-all gate ⇒ reports **permission**
  first; unauthenticated ⇒ 401 before either.

### 2.6 Device endpoints + capability snapshot + keys
Add to `Auth/DeviceEndpoints.cs` (new) mapped under `/auth`:
- `POST /auth/device/{id}/approve` — policy `devices.manage` (i.e. `PermissionCodes.DevicesManage`).
  Load device (tenant-scoped via `IIdentityRepository.FindDeviceByIdAsync`), `device.Approve()`, save, audit.
- `POST /auth/device/{id}/revoke` — policy `devices.manage`, body carries `reason`.
  `device.Revoke(reason, clock)` + **rotate the owner `User.SecurityStamp`** (`user.RotateSecurityStamp()`,
  invalidates live tokens) + save both in one UoW + audit. (Load the owner by `device.UserId`.)
- `POST /auth/capabilities` — authenticated + device `Active` (coarse `DeviceActive`). Issues a
  `CapabilitySnapshot` (the **Domain** record): permission codes from stored RBAC with per-capability
  expiry via `OfflineCapabilityPolicy.WindowForCode`, **Critical codes OMITTED** (zero window never grants),
  `SnapshotExpiryUtc` = min over granted windows **capped at 30 days** from issuance. Sign with the server
  snapshot signing key via `ISnapshotSigner`. Return the snapshot payload (canonical JSON) + `KeyId` +
  `Signature`. (Client verifier is G06 — not here.)
- `GET /auth/keys` — public set only, `KeyId`-addressed, rotation-aware → returns `PublicKeySet`. **Never**
  expose private key material. Anonymous or authenticated is fine; keep it anonymous+skip-tenant like health
  (it's a public key set) — but it must NOT require tenant binding. Add it to the skip list in 2.1, OR map it
  outside the protected group. Simplest: map it anonymous alongside health in `Program.cs` and list its path
  in the middleware skip rule.

### 2.7 `ISnapshotSigner` (asymmetric, server-only)
```csharp
// namespace Animora.Server.Signing
interface ISnapshotSigner {
  string KeyId { get; }                              // active signing key id (ULID, varchar(26))
  Result<string> Sign(ReadOnlySpan<byte> canonicalPayload);   // base64 detached signature
  // (verification is the client's job in G06; server may expose a Verify for tests)
}
sealed class EcdsaSnapshotSigner : ISnapshotSigner { /* ECDSA-P256 or Ed25519 via System.Security.Cryptography; NO custom crypto */ }
```
- Keys live in the new `SigningKeys` table; the private key is **Data-Protection protected** bytes. Use the
  existing DataProtection provider (already registered in `Program.cs`: `AddDataProtection().SetApplicationName("Animora.Server")`).
  Create an `IDataProtector` with a fixed purpose string (e.g. `"Animora.Server.SigningKeys.v1"`).
- `SigningKeyStore` (in `Auth/Signing/` or `Signing/`): loads the active key (newest non-retired, `NotAfterUtc`
  in future or null), decrypts the private key via the protector, exposes the public key DER + algorithm;
  can create the first key if none exists (operator/system only; the table is global). Canonical-JSON payload
  form for the snapshot: serialize the `CapabilitySnapshot` fields deterministically (sorted keys / fixed
  property order, UTC `o` timestamps). Keep the canonicalization in one private helper reused by sign + the
  test's verify.

### 2.8 `SigningKey` entity + table (`Animora.Persistence.PostgreSql`)
```csharp
// src/Animora.Persistence.PostgreSql/Security/SigningKey.cs — NOT a SyncableEntity (global, no tenant).
public sealed class SigningKey {
  public string KeyId { get; private set; }        // varchar(26) PK (ULID)
  public string Purpose { get; private set; }      // "snapshot" | "license"  (S16 reuses "license")
  public string Algorithm { get; private set; }    // e.g. "ECDSA-P256" or "Ed25519"
  public byte[] PublicKeyDer { get; private set; }  // bytea
  public byte[] ProtectedPrivateKey { get; private set; } // bytea (Data-Protection protected)
  public DateTimeOffset CreatedUtc { get; private set; }
  public DateTimeOffset? NotAfterUtc { get; private set; }
  public DateTimeOffset? RetiredUtc { get; private set; }
  private SigningKey() {}  // EF
  public static SigningKey Create(...);  // factory
}
```
- `Configurations/SigningKeyConfiguration.cs`: `ToTable("SigningKeys","public")`, `HasKey(KeyId)`,
  `KeyId` char varying(26), `Purpose`/`Algorithm` with sane max lengths, `bytea` columns, `timestamptz(6)`.
  **No tenant filter, no soft-delete filter** (it's not a `SyncableEntity`, so the convention skips it — just
  don't add a manual tenant filter like SecurityAuditEntry has).
- Add `DbSet<SigningKey> SigningKeys` to `AnimoraServerDbContext`.
- Migration `103_SigningKeys` (next ordinal after 102). Bump `ServerSchemaVersion.ExpectedHead`.

---

## 3. Task list (do in order; push after each)

> After EVERY task: `scripts/git/push.sh "feat(server): S05 — <summary>"`. Mark the box `[x]`.

- [x] **T1 — Read-first + baseline sanity.**
  Read only: `backend-roadmap/PROGRESS.md` (top up to & incl. "Phase status" — grep `^### S04` if you need
  S04 detail), `ARCHITECTURE.md` §9.2/§10.1/§10.3/§10.4 (only these). You already have the code facts above.
  Confirm head: `grep -n ExpectedHead src/Animora.Persistence.PostgreSql/ServerSchemaVersion.cs` → `102_Identity`.
  No code change. (This task's "push" is a no-op; skip the push if nothing changed — proceed to T2.)

- [x] **T2 — `SigningKey` entity + EF config + DbSet (Persistence.PostgreSql).**
  Create `src/Animora.Persistence.PostgreSql/Security/SigningKey.cs` (per 2.8) and
  `Configurations/SigningKeyConfiguration.cs`. Add `DbSet<SigningKey> SigningKeys => Set<SigningKey>();` to
  `AnimoraServerDbContext`. Do NOT add any tenant filter for it. Build: `scripts/sandbox/build.sh fast`.
  **Push:** `feat(server): S05 — SigningKey entity + configuration`.

- [x] **T3 — Migration `103_SigningKeys` + bump ExpectedHead.**
  Generate with the sandbox EF tool against `AnimoraServerDbContext` (server migrations live in
  `Migrations/Server`). Command pattern (verify exact factory usage from existing migrations — there is an
  `AnimoraServerDbContextFactory`):
  `.sandbox/tools/dotnet-ef migrations add SigningKeys --project src/Animora.Persistence.PostgreSql --context AnimoraServerDbContext --output-dir Migrations/Server`.
  Then RENAME the generated files + class to the `103_SigningKeys` ordinal convention (match `102_Identity`:
  file `<timestamp>_103_SigningKeys.cs`, class `_103_SigningKeys`). Update
  `ServerSchemaVersion.ExpectedHead = "103_SigningKeys"`. Migrations are additive and never edited later.
  Verify only the `SigningKeys` table is created (no accidental model drift from the new DbSet). Build fast.
  **Push:** `feat(server): S05 — migration 103_SigningKeys + ExpectedHead bump`.
  > Heavy-change note: if the migration diff is large/unexpected, push it separately with a clear message
  > before continuing.

- [x] **T4 — Server-side RBAC repositories (PG impls of the Application ports).**
  `Animora.Server` cannot reference Sqlite, and PG has no impl yet. Add, **in `Animora.Server/Authorization/`**
  (server-owned; they depend on `AnimoraServerDbContext` which Server may use via the Persistence reference):
  `ServerStaffMembershipRepository : IStaffMembershipRepository` and
  `ServerRoleDefinitionRepository : IRoleDefinitionRepository` over `AnimoraServerDbContext`.
  - `FindActiveAsync(tenantId,userId)`: `AsNoTracking` read — but the DbContext tenant filter already scopes
    to the bound tenant; still filter explicitly by userId + `Status==Active && EndedUtc==null`.
  - `FindByRoleAsync(tenantId,role)`: `AsNoTracking` by role (tenant filter handles tenant).
  - `FindAsync(id)` / `Add(...)`: implement minimally (Add can throw `NotSupportedException` with a
    why-comment — the server authorization path never adds memberships; or implement trivially). Keep it
    honest: these are read-only for authorization.
  > Rationale to record in a code comment: the Sqlite repos are in a forbidden project; the server needs its
  > own EF-over-PG implementation. Build fast.
  **Push:** `feat(server): S05 — server RBAC repositories over PostgreSQL`.

- [x] **T5 — `IMembershipResolver` + `MembershipResolver`.**
  In `Animora.Server/Authorization/`. Signature:
  `Task<Result<ResolvedMembership>> ResolveAsync(string userId, string deviceId, string securityStamp, CancellationToken ct)`
  where `ResolvedMembership(string TenantId, StaffRole Role, string UserId, string DeviceId, bool DeviceActive)`.
  Steps (fail closed, reuse `ErrorCodes`):
  1. Look up the `User` by **primary key only** (no tenant filter): the tenant isn't known yet, and the
     `AnimoraServerDbContext` tenant filter throws without a bound tenant. Use a **membership-bypass user
     lookup** — a dedicated read that bypasses the tenant query filter via a keyless/read context like
     `TenantDirectoryContext` does, OR a small raw lookup that selects only `(Id, TenantId, SecurityStamp,
     IsDisabled)` from `Users` with `.IgnoreQueryFilters()` **is banned** by the S02 interceptor for the
     main context — so add a tiny **keyless read view** for `Users` (mirror `TenantDirectoryContext`'s
     `ExcludeFromMigrations` pattern) selecting only the minimal user columns by Id. Name it e.g.
     `UserLookupContext` in `Animora.Persistence.PostgreSql/Identity/` (keyless, `ExcludeFromMigrations`,
     `AsNoTracking`, filter by `Id` only). This satisfies the acceptance test
     `MembershipResolverTests.UsesOnlyPrimaryKey_NoCrossTenantEnumeration`.
  2. If stamp ≠ user stamp → `Result.Fail(SessionExpired)`.
  3. If user disabled → `Result.Fail(UserDisabled)`.
  4. Bind the resolved tenant on the `RequestTenantContext` (so the next reads are tenant-filtered), then
     load active membership (`IIdentityRepository.FindActiveMembershipAsync(userId)` — tenant-scoped) →
     none/inactive → `Result.Fail(MembershipNotActive)`.
  5. Check tenant status (via `ITenantDirectory.FindByIdAsync(tenantId)`) → suspended → `Result.Fail(TenantSuspended)`.
  6. Load device by id (`IIdentityRepository.FindDeviceByIdAsync`) → revoked → `Result.Fail(DeviceRevoked)`;
     record `DeviceActive = device.IsUsable`.
  7. Else `Result.Ok(new ResolvedMembership(...))`.
  **No cross-request cache** (revocation is immediate). Build fast.
  > Note: binding the tenant mid-resolve requires the resolver to receive the `RequestTenantContext` (or an
  > `Action<string> bindTenant`). Prefer injecting `RequestTenantContext` directly (scoped) and calling
  > `.Bind(tenantId)` once the user's tenant is known, before step 4's tenant-scoped reads. Document this
  > ordering in a comment.
  **Push:** `feat(server): S05 — IMembershipResolver + keyless user lookup`.

- [x] **T6 — `RequestTenantContext` + request accessor for the resolved subject/device.**
  Add `RequestTenantContext` (per 2.2). Add a scoped `IRequestAuthorizationState` accessor that caches the
  `ResolvedMembership` + the built `AuthorizationSubject` for the request, so the authZ handlers
  (`ManagerOnly`, `DeviceActive`, `PermissionAuthorizationHandler`) and `EntitlementMiddleware` reuse the
  middleware's single resolve (no second DB hit). Shape:
  ```csharp
  internal interface IRequestAuthorizationState { ResolvedMembership? Membership { get; } AuthorizationSubject? Subject { get; } void Set(ResolvedMembership m, AuthorizationSubject s); }
  internal sealed class RequestAuthorizationState : IRequestAuthorizationState { ... } // scoped
  ```
  Build fast. **Push:** `feat(server): S05 — RequestTenantContext + request authorization state`.

- [ ] **T7 — `ServerAuthorizationSubjectFactory`.**
  In `Authorization/`. `Task<AuthorizationSubject> BuildAsync(ResolvedMembership m, CancellationToken ct)`:
  load `RoleDefinition` via `IRoleDefinitionRepository.FindByRoleAsync(m.TenantId, m.Role)`; compute grants
  = role.PermissionCodes ∪ membership.ExtraPermissionCodes − membership.DeniedPermissionCodes (ordinal;
  fetch the membership once — you may fold this into the resolver to avoid a reload, or reload via the repo).
  Return `new AuthorizationSubject(m.TenantId, m.UserId, grants.ToFrozenSet(Ordinal), m.Role, m.DeviceId,
  IsOffline:false)`. (The `PermissionEvaluator` recomputes from store regardless; this subject is the input.)
  Build fast. **Push:** `feat(server): S05 — ServerAuthorizationSubjectFactory`.

- [ ] **T8 — `TenantContextMiddleware`.**
  In `Authorization/`. Implements `IMiddleware` (register scoped) OR conventional ctor middleware. Logic per
  2.1: skip rule (health, `/auth/login`, `/auth/refresh`, `/auth/keys`, unauthenticated); read caller via
  `AuthenticatedCaller.TryRead(context.User)` — if null on a protected route, 401 `SESSION_EXPIRED`; call
  `IMembershipResolver.ResolveAsync(sub,did,sst)`; on failure write `WireApiError` with the error code +
  correlation id + mapped status (reuse `AuthEndpoints.StatusFor` — it already maps all these codes:
  SESSION_EXPIRED→401, MEMBERSHIP_NOT_ACTIVE/USER_DISABLED/TENANT_SUSPENDED/DEVICE_REVOKED→403); on success
  the tenant is already bound (resolver bound it), build the subject via the factory and store both on
  `IRequestAuthorizationState`; detect a differing inbound `TenantId` hint (header/query/body) and audit
  `security.tenant_hint_ignored` (never reject). **Only compare/audit `tid` — never use it for authority**
  (acceptance #2 greps this file). Build fast.
  > `grep -rn "FindFirst(\"role\")\|Claims.*role" src/Animora.Server/Authorization` must stay EMPTY.
  **Push:** `feat(server): S05 — TenantContextMiddleware (membership-bound tenant)`.

- [ ] **T9 — RBAC policy infra: requirement/handler/provider/coarse policies + command authorizer + table.**
  Add in `Authorization/`: `PermissionRequirement`, `PermissionAuthorizationHandler`,
  `ServerPermissionPolicyProvider`, `CoarsePolicies` (+ `ManagerOnlyRequirement`/handler, `DeviceActive`
  handling reading `IRequestAuthorizationState`), `CommandPermissionTable` (empty `FrozenDictionary<string,
  string>`), `ICommandAuthorizer` + `CommandAuthorizer`. Per 2.3 + 2.4. Build fast.
  **Push:** `feat(server): S05 — stored-RBAC policies + command authorizer (empty table)`.

- [ ] **T10 — Entitlement slot + middleware.**
  Add `TenantEntitlementContext`, `IServerEntitlementGate`, `AllowAllServerEntitlementGate`,
  `EntitlementMiddleware` (per 2.5). Build fast.
  **Push:** `feat(server): S05 — entitlement slot + EntitlementMiddleware (allow-all)`.

- [ ] **T11 — `ISnapshotSigner` + `EcdsaSnapshotSigner` + `SigningKeyStore` + canonical payload.**
  Add `Signing/ISnapshotSigner.cs`, `Signing/EcdsaSnapshotSigner.cs`, `Signing/SigningKeyStore.cs` (per
  2.7). Use `System.Security.Cryptography` (ECDSA P-256 is simplest cross-platform: `ECDsa.Create(ECCurve.NamedCurves.nistP256)`,
  `SignData(..., HashAlgorithmName.SHA256)`); store `ExportSubjectPublicKeyInfo()` as `PublicKeyDer` and the
  Data-Protection-protected `ExportPkcs8PrivateKey()` as `ProtectedPrivateKey`. `KeyId` = ULID. Provide a
  test-only `Verify(payload, signatureBase64)` so `CapabilitySnapshotTests.SignatureVerifiesWithPublicKey_AndTamperFails`
  can assert. Canonicalize `CapabilitySnapshot` deterministically (fixed field order, sorted `CapabilityExpiry`
  keys, UTC `o` format). Build fast.
  **Push:** `feat(server): S05 — ISnapshotSigner (ECDSA-P256) + SigningKeyStore`.

- [ ] **T12 — Device/capabilities/keys endpoints + Wire `PublicKeySet`.**
  Add Wire records `PublicKeySet` + `PublicKeyInfo` to `src/Animora.Contracts.Wire/Auth/`. Add
  `Auth/DeviceEndpoints.cs` (approve/revoke/capabilities) and `Auth/KeysEndpoint.cs` (`GET /auth/keys`) per
  2.6. Capability issuance: build the Domain `CapabilitySnapshot` from stored RBAC, omit Critical codes,
  `SnapshotExpiryUtc = min(granted expiries) capped at issued+30d`, sign via `ISnapshotSigner`, return
  payload + `KeyId` + `Signature`. Map approve/revoke behind the `devices.manage` policy (so
  `ServerPermissionPolicyProvider` resolves it). Revoke rotates the owner's `SecurityStamp` + audits.
  Wire the new endpoint groups in `Program.cs` (device/capabilities under `/auth` protected;
  `/auth/keys` anonymous). Build fast.
  **Push:** `feat(server): S05 — device approve/revoke, capabilities, /auth/keys + PublicKeySet wire`.

- [ ] **T13 — DI (`AddAnimoraAuthorization`) + `Program.cs` pipeline.**
  Add `Authorization/AuthorizationServiceCollectionExtensions.cs` with
  `AddAnimoraAuthorization(this IServiceCollection)`:
  - Register `RequestTenantContext` scoped and `services.Replace(ScopedITenantContext -> RequestTenantContext)`
    (wins over S04's `AuthTenantContext`).
  - Register the PG RBAC repos (`IStaffMembershipRepository`→`ServerStaffMembershipRepository`,
    `IRoleDefinitionRepository`→`ServerRoleDefinitionRepository`), `IPermissionEvaluator`→`PermissionEvaluator`
    **and its collaborators individually** (do NOT call `AddAnimoraApplication()`): also
    `IResourceAuthorizationRule<Invoice>`→`InvoiceAuthorizationRule`,
    `IResourceAuthorizationRule<CashSession>`→`CashSessionAuthorizationRule` (resolve exact types from
    `ApplicationServiceCollectionExtensions` — they are `Animora.Application.Security.Rules.*`).
  - Register `IMembershipResolver`, `ServerAuthorizationSubjectFactory`, `IRequestAuthorizationState`,
    `TenantContextMiddleware`, `ICommandAuthorizer`→`CommandAuthorizer`, `CommandPermissionTable` (singleton),
    `IServerEntitlementGate`→`AllowAllServerEntitlementGate` (TryAdd), `EntitlementMiddleware`,
    the authZ handlers (`PermissionAuthorizationHandler`, `ManagerOnlyRequirement` handler),
    `IAuthorizationPolicyProvider`→`ServerPermissionPolicyProvider`, the keyless `UserLookupContext`,
    `ISnapshotSigner`→`EcdsaSnapshotSigner`, `SigningKeyStore`.
  - `services.AddAuthorizationBuilder()` with the coarse named policies (`TenantMember`, `ManagerOnly`,
    `OperatorOnly`, `DeviceActive`).
  - Call `AddAnimoraAuthorization()` in `Program.cs` after `AddAnimoraIdentity`.
  - **Pipeline (fixed order, per ARCH §4 — edit `Program.cs`):**
    `GlobalException → (HSTS/HTTPS if !Dev) → request logging → UseAuthentication() → UseMiddleware<TenantContextMiddleware>()
    → UseAuthorization() → UseMiddleware<EntitlementMiddleware>() → UseRateLimiter() → routing → endpoints`.
    Remove the S01 `[tenant/authZ/entitlement slots]` comment placeholders. Keep health + `/auth/login|refresh`
    + `/auth/keys` anonymous.
  Build fast. **Push:** `feat(server): S05 — AddAnimoraAuthorization DI + pipeline wiring`.

- [ ] **T14 — Tests (`tests/Animora.Server.Tests/Authorization/`).**
  Create the dir + test classes. Use the existing patterns: `TestHostFactory`/`WebApplicationFactory<Program>`
  for endpoint/pipeline tests; `PgFixture`/`PgGatedTests` + `ANIMORA_PG_TEST_CONNECTION` skip for DB-backed
  tests; in-memory fakes for pure unit tests (fake `IMembershipResolver`/repos). Required tests (acceptance
  #3/#4/#5):
  - `TenantContextMiddlewareTests`: `IgnoresClaimedTenant_UsesMembership`, `StaleSecurityStamp_Returns401`,
    `InactiveMembership_Returns403`, `TenantSuspended_Returns403`, `ForeignTenantHint_IsAuditedAndIgnored`.
  - `AuthorizationTests`: `DenyWinsOverRoleAndExtra`, `UnknownPermission_FailsClosed`.
  - `PlaneOrderTests`: `Unauthenticated401_Then_Permission403_Then_Entitlement`.
  - `CapabilitySnapshotTests`: `CriticalPermissions_Omitted`, `SignatureVerifiesWithPublicKey_AndTamperFails`.
  - `DeviceEndpointsTests`: `Revoke_RotatesSecurityStamp_InvalidatesToken`.
  - `KeysEndpointTests`: `ExposesPublicOnly_NeverPrivate`.
  - `CommandPermissionCatalogTests.EveryCommandType_HasARequiredPermission` — `[Fact(Skip="enabled by S07, mandatory from S10")]`,
    body iterates `CommandTypes.All` asserting each has a `CommandPermissionTable` row.
  - PG-gated: `MembershipResolverTests.UsesOnlyPrimaryKey_NoCrossTenantEnumeration`,
    `SigningKeyMigrationTests.Applies`.
  Run: `scripts/sandbox/test.sh Authorization` (iterate). Then the acceptance gate ONCE:
  `scripts/sandbox/build.sh full` (0/0) → `scripts/sandbox/test.sh Server` (green) → `scripts/sandbox/run.sh`
  (`SMOKE RUN OK`).
  **Push:** `feat(server): S05 — authorization/tenant/snapshot/plane-order tests`.
  > Heavy-fix note: any non-trivial fix uncovered by tests (e.g. pipeline reorder, interceptor interaction)
  > gets its own push with a clear message.

- [ ] **T15 — Acceptance self-check (mechanical).**
  Verify all of acceptance §Acceptance criteria:
  1. `scripts/sandbox/build.sh full` = 0/0; `run.sh` = `SMOKE RUN OK`; client sources unchanged.
  2. `grep -rn "FindFirst(\"role\")\|Claims.*role" src/Animora.Server/Authorization` → empty;
     `grep -rn "tid" src/Animora.Server/Authorization/TenantContextMiddleware.cs` → only compared/audited.
  6. `git diff --stat -- src/Animora.Domain src/Animora.Application src/Animora.Persistence.Sqlite src/Animora.Desktop`
     → **empty** (if not, you edited a forbidden project — move the code into `Animora.Server`).
  Fix anything failing, pushing each fix. **Push (if any fix):** `fix(server): S05 — acceptance corrections`.

- [ ] **T16 — PROGRESS.md update.**
  Per the phase's "PROGRESS.md update" recipe: set the `S05` row in "Phase status" to `✅ Done` (+ date +
  one-line summary); advance **"Next phase" → `S06`**; append one `### S05` entry to "Repo state log"
  (what exists now: `Animora.Server/Authorization` + `Signing`, `SigningKeys` table / migration `103_SigningKeys`,
  no new error codes, the new test classes); log any deviation in "Decision & deviation log".
  **Push:** `feat(server): S05 tenant context & authorization policies` (the phase's canonical commit message).

- [ ] **T17 — Delete this plan file (FINAL task).**
  `rm Plan/S05-tenant-context-authorization.todo.md` (leave the `Plan/` directory). Touch nothing else — do
  NOT edit the roadmap phase file or any other file in this task (the phase file deletion, if any, is the
  roadmap's own concern per PROGRESS, not this plan's — we only remove this plan artifact).
  **Push:** `chore(plan): remove completed S05 plan`.

---

## 4. Gotchas captured during research (so you don't rediscover them)

1. **Sqlite RBAC repos are off-limits.** `IStaffMembershipRepository`/`IRoleDefinitionRepository` only have
   Sqlite implementations today; the server cannot reference that project. You MUST add PG impls (T4).
2. **Tenant filter throws without a bound tenant.** `AnimoraServerDbContext.CurrentTenantId` throws
   `TenantContextMissingException` when `ITenantContext.TenantId` is null, and `.IgnoreQueryFilters()` is
   **banned** by `NoUnnamedIgnoreFiltersInterceptor`. So the initial user-by-id lookup (before tenant is
   known) needs a **keyless read context** like `TenantDirectoryContext` (T5's `UserLookupContext`).
3. **`ISnapshotSigner` ≠ the B04b `ICapabilitySnapshotSigner`.** The latter is HMAC and lives in the forbidden
   `Animora.Infrastructure.Local`. S05's signer is a NEW asymmetric server-only interface in `Animora.Server.Signing`.
4. **S05 reuses the Domain `CapabilitySnapshot` record** for the wire payload — NOT the Application's
   `StoredCapabilitySnapshot` (that's the local-store shape).
5. **No new ErrorCodes.** Every rejection reuses an existing `ErrorCodes.*` (listed in 1.1). Do not touch
   `Animora.Domain` (acceptance #6).
6. **`AuthEndpoints.StatusFor` already maps every code you need** (401/403/…). Reuse it for the middleware's
   failure responses; don't duplicate the mapping. (It's `internal` — same assembly, fine.)
7. **`AddAnimoraApplication()` must NOT be called server-side** (it would pull the whole use-case registry +
   dispatcher). Register `IPermissionEvaluator` + the two resource rules + the two repos individually (T13).
8. **Plane order test** is the one ordering guarantee: permission denial is reported before entitlement;
   unauthenticated 401 before both. Allow-all gate means entitlement never trips until S16.
9. **Migration ordinal**: the physical file keeps EF's timestamp prefix; the roadmap-visible name is
   `103_SigningKeys` and the class is `_103_SigningKeys` (match `_102_Identity`). `ExpectedHead` compares on
   the `_<name>` suffix.
10. **Push after every task.** If a single task produces a large/heavy change (e.g. the migration, or a big
    test file), it already has its own push; split further if a mid-task fix is substantial.
