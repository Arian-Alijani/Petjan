# Phase S05 — Tenant context & authorization policies — membership-bound tenant, stored RBAC, entitlement slot

**Band:** M · **Track:** S (server) · **Depends on:** S04 · **Next:** S06
**Satisfies:** R-B04, R-B19, R-B20 · **Runnable after this phase:** desktop offline yes · server required no
**Produced by:** M00 (regenerated 2026-10-08; supersedes the deleted `PHASE-D04` file).

> Baseline verified 2026-10-08: client migration head `031` (`031_TreatmentPlans`), `CommandTypes` = live `CommandTypes.cs` (88 declared today — never freeze), `ErrorCodes` 161, `WireContract.Version` = 1; `src/` projects = `Animora.Domain`, `Animora.Application`, `Animora.Persistence.Sqlite`, `Animora.Infrastructure.Local`, `Animora.Client.Services`, `Animora.Contracts`, `Animora.Contracts.Wire`, `Animora.Desktop`, `Animora.Sync.Abstractions` (+ server-track projects added by earlier S/G phases).
> No migration in this phase.

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
- `ARCHITECTURE.md` §9.2, §10.1, §10.3, §10.4 — only these `§`.
- `src/Animora.Application/Security/IPermissionEvaluator.cs`, `PermissionEvaluator.cs`, `AuthorizationPolicies.cs` (read once — the evaluator is reused server-side).
- `src/Animora.Server/Program.cs`, `src/Animora.Server/Auth/AuthServiceCollectionExtensions.cs` (S04; edited here).

(Nothing else. `Animora-UI/**`, `Database-Sampel/**`, other phase files and `windows-frontend-roadmap/phases/**` are out of bounds.)

## Goal

Bind every protected request to a tenant **only through authenticated, server-stored membership** (the claimed tid/role are never trusted), expose the request-scoped `ITenantContext`, evaluate authorization from the **stored** role definition + per-membership overrides with the existing `PermissionEvaluator`, register the fixed named policy set and the `RequiresPermission` → ASP.NET authorization bridge, add the **entitlement slot** (`IServerEntitlementGate`, allow-all until S16), and enforce the three planes in the documented order. Also adds device approve/revoke and capability-snapshot issuance endpoints.

---

## §Reference pack

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
namespace Animora.Application.Security;   // (B04a) — reuse; the SAME evaluator the client uses
public sealed record AuthorizationSubject(string TenantId, string UserId, IReadOnlySet<string> Permissions, StaffRole Role, string DeviceId, bool IsOffline);
public sealed record AuthorizationDecision(bool Allowed, string? DeniedByRule, string PermissionCode) { static Allow(string code); static Deny(string code, string rule); }
public interface IPermissionEvaluator { Task<AuthorizationDecision> EvaluateAsync(string permissionCode, AuthorizationSubject subject, object? resource, CancellationToken ct); }
[AttributeUsage(AttributeTargets.Class)] public sealed class RequiresPermissionAttribute(string permissionCode) : Attribute { public string PermissionCode { get; } }
public interface IEntitlementGate { ValueTask<Result> EvaluateAsync(object command, CommandContext context, CancellationToken ct); ValueTask<Result> EvaluateAsync(object query, QueryContext context, CancellationToken ct); }
public interface IResourceAuthorizationRule<in T> { /* per-entity rule (e.g. InvoiceAuthorizationRule, CashSessionAuthorizationRule) */ }
// Domain: PermissionCodes.* (const string), PermissionCodes.RiskOf(code); DefaultRolePermissions; StaffMembership.ExtraPermissionCodes/DeniedPermissionCodes; RoleDefinition.PermissionCodes
// Domain: CapabilitySnapshot(TenantId, UserId, DeviceId, Role, IReadOnlyDictionary<string,DateTimeOffset> CapabilityExpiry, IssuedUtc, SnapshotExpiryUtc, SecurityStamp, Signature);
//         OfflineCapabilityPolicy.WindowFor(RiskLevel) → Low 30d · Normal 14d · High 72h · Critical = TimeSpan.Zero (online-only)
```
S04 provides claims `sub|tid|did|sst|role`, `ITenantDirectory`, `ISecurityAuditWriter`, repositories over `Users/StaffMemberships/RoleDefinitions/Devices`. S02: `ITenantContext { string? TenantId; bool IsOperator }`, `NullTenantContext`.

## §Design decisions

- **Request pipeline (ARCH §4 fixed, finalizes the S01 slots):** `GlobalException → HSTS/HTTPS → request logging → UseAuthentication (S04) → TenantContextMiddleware → UseAuthorization → EntitlementMiddleware → routing → endpoints`. Health + `/auth/login|refresh` are anonymous and skip tenant/entitlement.
- **`TenantContextMiddleware`:** from the authenticated principal read `sub`,`did`,`sst`; **ignore `tid` for authority** — load the active `StaffMembership` for `(sub)` via `IMembershipResolver.ResolveAsync(userId)` (reads `Users` by id through a membership-bypass lookup keyed on the primary key only, then `StaffMemberships` for that user's `TenantId`). Reject (401 `SESSION_EXPIRED`) when the token `sst` ≠ stored `SecurityStamp`; (403 `MEMBERSHIP_NOT_ACTIVE`/`USER_DISABLED`/`TENANT_SUSPENDED`) when inactive; (403 `DEVICE_REVOKED`) when the device is revoked. Sets scoped `RequestTenantContext : ITenantContext` (`TenantId` = membership's tenant; `IsOperator=false`) — **the** binding for all EF filters. Any inbound header/body/query `TenantId` that differs is **ignored and audited** (`security.tenant_hint_ignored`); a command envelope `TenantId` mismatch is S06's `TENANT_MISMATCH`.
- **`IMembershipResolver` caching:** none beyond the request (no cross-request cache at launch ⇒ revocation is immediate); a bounded `TenantScopedCache` is a post-measurement option (A-RULE 033) not built here.
- **Stored RBAC:** `ServerAuthorizationSubjectFactory` builds `AuthorizationSubject` from `RoleDefinition.PermissionCodes` ∪ `ExtraPermissionCodes` − `DeniedPermissionCodes` (deny wins); unknown permission code → `PERMISSION_UNKNOWN` fail closed; the principal's `role` claim is never read. `ServerPermissionPolicyProvider : IAuthorizationPolicyProvider` creates a policy per `PermissionCodes` const on demand (`PermissionRequirement(code)` + `PermissionAuthorizationHandler` → `IPermissionEvaluator`). Named **coarse** policies (fixed set, registered up-front): `TenantMember`, `ManagerOnly`, `OperatorOnly` (S14 fills the identity), `DeviceActive`.
- **Command-level authorization bridge:** `ICommandAuthorizer.AuthorizeAsync(string commandType, AuthorizationSubject subject, CancellationToken ct)` maps a wire `CommandType` to the required `PermissionCodes.*` through the explicit frozen table `CommandPermissionTable` (`IReadOnlyDictionary<string,string>`, `Animora.Server.Authorization`). A command type absent from the table is **denied** (`PERMISSION_UNKNOWN`, fail closed). The table starts **empty**; S07–S10 each append the rows for the command families they handle (the permission is the one on the matching Application command's `[RequiresPermission(...)]`). Invariant test `CommandPermissionCatalogTests.EveryCommandType_HasARequiredPermission` iterates `CommandTypes.All`; it ships `Skip`ped (reason "enabled by S07, mandatory from S10") and S10 removes the skip.
- **Entitlement slot:** `IServerEntitlementGate : IEntitlementGate`-shaped but request-level: `ValueTask<Result> EvaluateAsync(TenantEntitlementContext c, CancellationToken ct)`; default `AllowAllServerEntitlementGate` (registered `TryAdd`); S16 replaces it. Order is enforced by one integration test (a deny-all gate + a deny-all permission must report **permission** first, then **entitlement**; unauthenticated reports 401 before either).
- **Device approve/revoke + capability snapshots:** `POST /auth/device/{id}/approve` (`devices.manage`) → `Device.Approve()`; `POST /auth/device/{id}/revoke` (`devices.manage`, body reason) → `Device.Revoke(reason, clock)` + rotate the owner's `SecurityStamp` (invalidates live tokens) + audit; `POST /auth/capabilities` (authenticated, device `Active`) → issues a `CapabilitySnapshot` (permission codes with per-capability expiry from `OfflineCapabilityPolicy.WindowForCode`, **Critical omitted**, `SnapshotExpiryUtc` = min over granted windows capped at 30 days) signed with the **server snapshot signing key**: `ISnapshotSigner` (asymmetric detached signature, canonical-JSON payload form, `KeyId` header). S05 ships the interface + `Ed25519`/`ECDSA-P256` implementation via `System.Security.Cryptography` (**no custom crypto**; key held in the Data Protection-protected key store table `SigningKeys` added by migration below) and a public-key endpoint `GET /auth/keys` (public set only, `KeyId`-addressed, rotation-aware). The client verifier is G06.
- **Migration (server, next free ordinal after S04's; expected 103 `SigningKeys`):** `SigningKeys` (`KeyId varchar(26)` PK, `Purpose` (`snapshot`|`license`), `Algorithm`, `PublicKeyDer bytea`, `ProtectedPrivateKey bytea` (Data Protection protected), `CreatedUtc`, `NotAfterUtc?`, `RetiredUtc?`) — **not tenant-scoped** (vendor-global, operator/system only; never exposed through the tenant context). S16 reuses `Purpose='license'`.
- **New ErrorCodes:** none new (reuse `PermissionDenied`, `PermissionUnknown`, `MembershipNotActive`, `TenantSuspended`, `DeviceRevoked`, `SessionExpired`, `EntitlementRequired`).

## §Implementation plan

1. Create `src/Animora.Server/Authorization/` (`TenantContextMiddleware`, `RequestTenantContext`, `IMembershipResolver`/`MembershipResolver`, `ServerAuthorizationSubjectFactory`, `PermissionRequirement`, `PermissionAuthorizationHandler`, `ServerPermissionPolicyProvider`, `CoarsePolicies`, `CommandPermissionTable` (empty), `ICommandAuthorizer`/`CommandAuthorizer`, `IServerEntitlementGate`/`AllowAllServerEntitlementGate`, `EntitlementMiddleware`).
2. Server DI: `AddAnimoraAuthorization()` (registers `PermissionEvaluator` from `Animora.Application` via `AddAnimoraApplication()` is **not** called here — register `IPermissionEvaluator`→`PermissionEvaluator` and its collaborators individually: resolve the exact registrations from `ApplicationServiceCollectionExtensions` lines for `IPermissionEvaluator`, `IResourceAuthorizationRule<Invoice|CashSession>`); replace `NullTenantContext` with `RequestTenantContext` (scoped).
3. Device/snapshot endpoints in `Auth/DeviceEndpoints.cs`; `Signing/ISnapshotSigner.cs`, `EcdsaSnapshotSigner.cs`, `SigningKeyStore.cs`, `Auth/KeysEndpoint.cs`; `SigningKey` entity + configuration (Persistence).
4. Migration: `.sandbox/tools/dotnet-ef migrations add SigningKeys …` then rename to the next free ordinal (expect `103_SigningKeys`) and update `ServerSchemaVersion.ExpectedHead`.
5. Edit `Program.cs` pipeline per the fixed order; remove the S01 comment slots.
6. Tests (below); `scripts/sandbox/build.sh full` → 0/0; `scripts/sandbox/test.sh Server` → green; `scripts/sandbox/run.sh` → `SMOKE RUN OK`. `PROGRESS.md` per recipe; commit.

## Files to create / modify

| Path | Contents |
|---|---|
| `src/Animora.Server/Authorization/*.cs` | tenant binding, stored-RBAC policies, command authorizer, entitlement slot |
| `src/Animora.Server/Auth/DeviceEndpoints.cs`, `KeysEndpoint.cs`, `Signing/*.cs` | device lifecycle + snapshot signing + public keys |
| `src/Animora.Persistence.PostgreSql/Security/SigningKey.cs`, `Configurations/SigningKeyConfiguration.cs`, `Migrations/Server/*103_SigningKeys*` | key table |
| `src/Animora.Server/Program.cs`, `Auth/AuthServiceCollectionExtensions.cs` (edit) | pipeline + DI |
| `tests/Animora.Server.Tests/Authorization/*.cs` | tests |

## Contracts / schema added

Table `SigningKeys` (global). Wire: `CapabilitySnapshot` payload reuses the Domain record (serialised canonical JSON + `KeyId` + `Signature`); `GET /auth/keys` returns `PublicKeySet(IReadOnlyList<PublicKeyInfo(KeyId, Algorithm, PublicKeyDerBase64, NotAfterUtc?)>)` added to `Animora.Contracts.Wire.Auth` (additive). Seams: `ITenantContext` (request-bound), `IMembershipResolver`, `ICommandAuthorizer`, `CommandPermissionTable`, `IServerEntitlementGate`, `ISnapshotSigner`.

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

- Real entitlement/subscription decisions → **S16** (replaces `AllowAllServerEntitlementGate`; consumes S14 `Subscription`).
- Operator identity & `OperatorOnly` population → **S14/S19**.
- Command intake + `TENANT_MISMATCH` for envelopes → **S06**; populating `CommandPermissionTable` rows → **S07–S10**.
- Client-side snapshot verification and offline use → **G06**. License signing use of `SigningKeys` → **S16**.

## Acceptance criteria (mechanical)

1. `scripts/sandbox/build.sh full` = 0 warnings / 0 errors; `scripts/sandbox/run.sh` = `SMOKE RUN OK`; client sources unchanged.
2. `grep -rn "FindFirst(\"role\")\|Claims.*role" src/Animora.Server/Authorization` returns nothing (role claim never used for authority); `grep -rn "tid" src/Animora.Server/Authorization/TenantContextMiddleware.cs` shows it is only compared/audited.
3. Tests pass: `TenantContextMiddlewareTests.IgnoresClaimedTenant_UsesMembership`, `…StaleSecurityStamp_Returns401`, `…InactiveMembership_Returns403`, `…TenantSuspended_Returns403`, `…ForeignTenantHint_IsAuditedAndIgnored`, `AuthorizationTests.DenyWinsOverRoleAndExtra`, `AuthorizationTests.UnknownPermission_FailsClosed`, `PlaneOrderTests.Unauthenticated401_Then_Permission403_Then_Entitlement`, `CapabilitySnapshotTests.CriticalPermissions_Omitted`, `CapabilitySnapshotTests.SignatureVerifiesWithPublicKey_AndTamperFails`, `DeviceEndpointsTests.Revoke_RotatesSecurityStamp_InvalidatesToken`, `KeysEndpointTests.ExposesPublicOnly_NeverPrivate`.
4. `CommandPermissionCatalogTests.EveryCommandType_HasARequiredPermission` exists and is `Skip`-annotated with reason "enabled by S07, mandatory from S10".
5. PG-gated (G07): `MembershipResolverTests.UsesOnlyPrimaryKey_NoCrossTenantEnumeration`, `SigningKeyMigrationTests.Applies`.
6. `git diff --stat -- src/Animora.Domain src/Animora.Application src/Animora.Persistence.Sqlite src/Animora.Desktop` empty.

## PROGRESS.md update

At the end of this phase: set the `S05` row in "Phase status" to `✅ Done` (add date + one-line summary), advance **"Next phase" → `S06`**,
append one `### S05` entry to "Repo state log" (what exists now: projects, tables/migration ordinal, error codes added, test classes), log any
deviation in the "Decision & deviation log", then commit `feat(server): S05 tenant context & authorization policies` and sync per the repo git workflow.
