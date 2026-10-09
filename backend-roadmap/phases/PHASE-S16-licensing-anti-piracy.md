# Phase S16 — Licensing & anti-piracy — signed installation-bound licence, grant sync, server entitlement gate

**Band:** M · **Track:** S (server) · **Depends on:** S15 · **Next:** S17
**Satisfies:** R-B06, R-B29, R-B30, R-B36 · **Runnable after this phase:** desktop offline yes · server required no
**Produced by:** M00 (regenerated 2026-10-09; regenerates scope-inventory row D11 (Track-D/E planning files deleted in the 2026-10-08 server-track reset)).

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
- `ARCHITECTURE.md` §13.1, §13.2 (licence format, honest threat model, verifier = client-reusable half), §10.6, §21 (A-RULE 011 monotonic clock) — only these `§`.
- `src/Animora.Server/Signing/{ISnapshotSigner,EcdsaSnapshotSigner,SigningKeyStore}.cs`, `src/Animora.Persistence.PostgreSql/Security/SigningKey.cs` (S05 — `Purpose='license'` rows are created HERE), `src/Animora.Server/Authorization/{EntitlementMiddleware,IServerEntitlementGate}.cs` (S05; gate replaced here).
- `src/Animora.Persistence.PostgreSql/Subscriptions/{SubscriptionService,ILicenseIssuanceScheduler}.cs` (S14; edited here), `src/Animora.Domain/Identity/{Device,OfflineCapabilityPolicy}.cs`, `src/Animora.Application/Commands/CommandFingerprint.cs` (canonical JSON — issuance half only).

(Nothing else. `Animora-UI/**`, `Database-Sampel/**`, other phase files and `windows-frontend-roadmap/phases/**` are out of bounds.)

## Goal

Make **Licensing** the runtime entitlement truth, separate from Subscription (R-B30): a **detached-signature licence envelope** (`Base64Url(payload).Base64Url(signature).KeyId`, ECDSA P-256, rotation-aware key set) **bound to one installation**, a **pure client-reusable verification library** (`Animora.Server.Licensing/Verification`: parse, verify, monotonic high-water clock, fail-closed policy matrix with offline grace, device fingerprint function, sealed-cache port), the server-side **issuance** (activate/renew endpoints, `LicenseGrants` kept in sync with Subscription via the `license.issue` outbox path, revocation, key rotation) and the real **server entitlement gate** replacing S05's allow-all. Honest threat model (ARCH §13.2): the signature gives authenticity + tamper-evidence, not an unbreakable boundary; real enforcement of online capabilities is this server gate.

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
namespace Animora.Domain.Identity;     // (A03) — reuse
public enum RiskLevel { Low, Normal, High, Critical }                       // PermissionCodes.RiskOf(code); OfflineCapabilityPolicy.WindowFor(RiskLevel): Low 30d · Normal 14d · High 72h · Critical = TimeSpan.Zero (online-only)
public sealed class Device : AggregateRoot { UserId, DeviceName, InstallationIdHash, Platform, Status /*Pending|Active|Revoked*/, RegisteredUtc, LastContactUtc?, RevokedUtc?; bool IsUsable; }
```
```csharp
// S05 seams reused (exact names)
namespace Animora.Server.Signing;
public interface ISnapshotSigner { /* asymmetric detached signature over canonical bytes, KeyId header; ECDSA P-256 via System.Security.Cryptography */ }
// SigningKeyStore over table SigningKeys(KeyId PK, Purpose 'snapshot'|'license', Algorithm, PublicKeyDer, ProtectedPrivateKey (Data Protection), CreatedUtc, NotAfterUtc?, RetiredUtc?)  — vendor-global, operator/system only
namespace Animora.Server.Authorization;
public interface IServerEntitlementGate { ValueTask<Result> EvaluateAsync(TenantEntitlementContext c, CancellationToken ct); }   // default AllowAllServerEntitlementGate registered with TryAdd — S16 registers the real one with Replace
// EntitlementMiddleware order: AuthN → TenantContext → AuthZ → ENTITLEMENT → endpoint.   Claims: sub|tid|did|sst|role.   RequestTenantContext(TenantId from membership).
// S14: ILicenseIssuanceScheduler { Task ScheduleAsync(string tenantId, string subscriptionId, CancellationToken ct); } + NoopLicenseIssuanceScheduler (TryAdd); AnimoraOperatorDbContext; SubscriptionService (every mutation = one transaction + event); Subscription { Status, CurrentPeriodEndUtc, GraceEndsUtc?, SeatLimit, NextEventSequence }; PlanCode via Plan
// S13: IOutboxConsumer { string MessageType; string Name; Task<Result> ConsumeAsync(OutboxMessage m, CancellationToken ct); }; IServerOutbox.EnqueueAsync (same tx); OutboxMessageTypes ("license.issue" and "license.recheck" were reserved for S16);  ConsumedMessages idempotency
// S01: RateLimitPolicies.Auth ("auth", 10/min/IP), ServerRoutes (+ "/licensing" added here); S04: ISecurityAuditWriter, Device repository
```

## §Design decisions

- **Project `src/Animora.Server.Licensing`** (`Microsoft.NET.Sdk`, **no** `FrameworkReference Microsoft.AspNetCore.App`, **no** Npgsql/EF package; refs `Animora.Domain`, `Animora.Contracts.Wire`, `Animora.Application`). Two folders = two halves: `Verification/` (pure: uses only `System.*`, Domain, Contracts.Wire — a source-scan test forbids `using Animora.Application`, `Microsoft.AspNetCore`, `Npgsql`, `Microsoft.EntityFrameworkCore` inside `Verification/**`) and `Issuance/` (may use Application's `CommandFingerprint.CanonicalJson`). `Animora.Server` references the project; **G06 is the first (and only sanctioned) client-side reference to any `Animora.Server.*` project** — `LayeringRulesTests` (S01) is edited here to whitelist exactly `Animora.Server.Licensing` for `Animora.Client.Services`/`Animora.Desktop`/`Animora.Infrastructure.Local` and keep every other server project forbidden; an assembly-closure test proves the library's transitive references contain no Npgsql/ASP.NET Core assembly.
- **Envelope (`Verification/LicenseEnvelope`).** `token = Base64Url(payloadUtf8) + "." + Base64Url(signature) + "." + KeyId`; `Parse(string) → Result<ParsedLicense(PayloadBytes, Signature, KeyId)>` (max length 4 KiB, exactly 3 segments, strict base64url). Signature = ECDSA P-256 / SHA-256, IEEE P1363 fixed-size (64 bytes), over the **exact payload bytes** (the verifier never re-canonicalises). `LicenseVerifier.Verify(ParsedLicense, ILicenseKeySet keys) → Result<LicensePayload>`: unknown `KeyId` ⇒ `LICENSE_KEY_UNKNOWN`; bad signature or malformed payload ⇒ `LICENSE_INVALID`; key outside its `[NotBeforeUtc, NotAfterUtc]` use window is **still valid for verification** if the licence's `issuedUtc` was inside it (rotation-aware); constant-time compare is inherent to `ECDsa.VerifyData`.
- **Payload (`LicensePayload`, canonical JSON, camelCase, sorted keys, UTC `O` timestamps):** `v` = 1, `licenseId` (ULID), `tenantId`, `tenantCode`, `subscriptionId`, `planCode`, `seatLimit`, `maxDevices`, `installationIdHash` (64 hex — the **one** bound installation), `deviceId`, `sequence` (per-device monotonic long; a client rejects a licence with a lower sequence than the highest it has stored — blob-rollback guard), `issuedUtc`, `notBeforeUtc` (= issuedUtc − 5 min skew), `expiresUtc`, `offlineGraceUntilUtc` (= expiresUtc + `OfflineGraceDays`, default **14**), `features` (string[], reserved, empty today). Unknown future `v` ⇒ `LICENSE_INVALID` (fail closed). Field set is additive-only within v1.
- **Policy matrix (`LicensePolicy.Evaluate(LicensePayload p, DateTimeOffset nowUtc, HighWaterClock clock, string localInstallationIdHash) → LicenseVerdict(State, Reason, RemainingUntilExpiry?, RemainingGrace?)`, pure, table-tested; fail-closed order):** (1) `localInstallationIdHash ≠ p.installationIdHash` ⇒ `Locked / WRONG_INSTALLATION`; (2) effective time `t = max(nowUtc, clock.HighWaterUtc)` (**A-RULE 011** — a backward wall clock never extends a licence); if `nowUtc < clock.HighWaterUtc − ClockTolerance (5 min)` ⇒ `ClockRollbackDetected = true`; (3) `t < p.notBeforeUtc` ⇒ `Locked / NOT_YET_VALID`; (4) `t ≤ p.expiresUtc` ⇒ `Valid` — but if rollback was detected by more than **24 h** ⇒ `ReadOnly / CLOCK_ROLLBACK` until an online refresh resets the clock evidence; (5) `t ≤ p.offlineGraceUntilUtc` ⇒ `Grace`; (6) `t ≤ p.offlineGraceUntilUtc + ReadOnlyWindowDays (60)` ⇒ `ReadOnly / EXPIRED`; (7) beyond ⇒ `Locked / EXPIRED`. Any verifier failure ⇒ `Locked / <ErrorCode>`. **`CapabilityGate.IsAllowed(LicenseState, RiskLevel)`:** `Valid` → all; `Grace` → Low, Normal, High (Critical is online-only anyway); `ReadOnly` → reads only (no command of any risk); `Locked` → nothing except the activation/status UI. These are the only entitlement semantics the desktop needs; wiring them into `IEntitlementGate` is **G06**.
- **`HighWaterClock` (pure record + `Advance(DateTimeOffset nowUtc) → HighWaterClock`):** `HighWaterUtc` only ever increases; advanced by local reads **and** by the authoritative `serverTimeUtc` in every `LicenseResponse` (a server time lower than the stored high-water is ignored, never lowers it). Persistence of the clock evidence = the sealed cache (G06).
- **`DeviceFingerprint.Compute(IReadOnlyList<string> stableComponents) → string` (pure):** SHA-256 hex (lower, 64) over `"animora.installation.v1" + 0x1F + each component trimmed/normalised, ordinal-sorted, joined by 0x1F`; empty/duplicate-only input ⇒ `ArgumentException`. The **raw components never leave the machine**; only the hash is sent as `DeviceInstallationIdHash` (S04) and bound into the licence. Component collection (machine id, OS install id, …) is client infrastructure → **G06**.
- **Sealed entitlement cache (contract only here):** `SealedEntitlement(string LicenseToken, long HighWaterUnixMs, long HighestSequenceSeen, string TenantId, string InstallationIdHash, long WrittenUnixMs)` + port `ISealedEntitlementStore { Task<Result<SealedEntitlement?>> ReadAsync(ct); Task<Result> WriteAsync(SealedEntitlement e, ct); }` in `Verification/`. The implementation (OS-bound protection e.g. DPAPI, tamper-evident, atomic write) is client infrastructure → **G06**; the format + invariants (write only **forward**: never lower `HighWaterUnixMs`/`HighestSequenceSeen`) are fixed here with a pure `SealedEntitlementMerge.Merge(existing, incoming)` function + tests.
- **Grants (`LicenseGrants`, vendor-global, one row per tenant):** the single, denormalised source for issuance **and** the gate, derived from Subscription by the `license.issue` consumer: `TenantId PK`, `SubscriptionId`, `PlanCode`, `SeatLimit`, `MaxDevices` (= `SeatLimit × LicensingOptions.DevicesPerSeat`, default **2**), `ValidUntilUtc` (= `Subscription.GraceEndsUtc ?? CurrentPeriodEndUtc` while status ∈ Trial/Active/GracePeriod), `Status` (`Active=0`, `Suspended=1`, `Revoked=2`; Suspended/Expired subscription ⇒ Suspended, Cancelled ⇒ Revoked), `Sequence bigint` (grant version), `SourceEventSequence int`, `UpdatedUtc`. Upsert is **monotonic** on `SourceEventSequence` (an older message never overwrites a newer grant) ⇒ idempotent under redelivery/reordering.
- **Grant sync path.** `OutboxLicenseIssuanceScheduler : ILicenseIssuanceScheduler` replaces S14's Noop (`Replace`): enqueues `license.issue` (payload tenantId, subscriptionId, sourceEventSequence) through `IServerOutbox` **in the caller's transaction**. S16 edits S14's `SubscriptionService` so **every** successful lifecycle mutation (extend/reduce/suspend/reinstate/cancel/renew/seats/plan and the expiry consumer) calls the scheduler — plus S14's provisioning already does. `LicenseIssueConsumer` / `LicenseRecheckConsumer : IOutboxConsumer` (both call `LicenseGrantProjector.ProjectAsync(tenantId)` which re-reads the subscription and upserts the grant; `license.recheck` is operator-triggered drift repair `POST /operator/tenants/{id}/licenses/recheck`). Unknown/late: a consumer for a deleted tenant acks.
- **Licences table (`Licenses`, tenant-scoped, **not** `SyncableEntity`, `"Tenant"` filter + `TenantId` first in every index like S04 `RefreshTokens`):** `TenantId`, `Id varchar(26)`, `DeviceId`, `InstallationIdHash char(64)`, `Sequence bigint`, `IssuedUtc`, `ExpiresUtc`, `OfflineGraceUntilUtc`, `KeyId varchar(26)`, `PayloadSha256 char(64)` (the token itself is **not** stored), `Status smallint (Active=0, Superseded=1, Revoked=2)`, `RevokedUtc?`, `RevokedByActor varchar(26)?`, `RevokeReason varchar(200)?`, PK `(TenantId, Id)`; **unique partial index** `UX_Licenses_Tenant_Device_Active (TenantId, DeviceId) WHERE Status = 0` (one live licence per device); `IX_Licenses_TenantId_Status`.
- **Issuance (`LicenseIssuer.IssueAsync(IssueKind kind, ...)`, one transaction):** preconditions in order — grant exists and `Status=Active` and `now ≤ ValidUntilUtc` (else `ENTITLEMENT_REQUIRED` / `LICENSE_REVOKED`); the caller's device (from the **`did` claim re-read from the store**, never the body) is `Active`; request `installationIdHash` equals `Devices.InstallationIdHash` (else `LICENSE_DEVICE_MISMATCH` + `SecurityAudit license.device_mismatch`); device-count check on **first** activation of a device: active licences `< MaxDevices` (else `LICENSE_DEVICE_LIMIT_REACHED`); then supersede the device's current Active licence (`Status=Superseded`), insert the new one with `Sequence = previous + 1`, `ExpiresUtc = min(grant.ValidUntilUtc, now + MaxIssueTtlDays)` (**MaxIssueTtlDays = 40**, bounds the exposure after a cancel for a disconnected client), sign via `ILicenseSigner` (key `Purpose='license'`, the newest non-retired key), audit `license.issued` with `licenseId`, `sequence`, `keyId` (never the token). `activate` and `renew` share this method (`IssueKind` only labels the audit).
- **Keys.** `LicenseKeyBootstrapper` (startup, after migrations check) ensures one non-retired `Purpose='license'` key exists (creates ECDSA P-256 through S05's `SigningKeyStore`, private key Data-Protection-protected, never leaves the process). `POST /operator/licensing/keys/rotate` creates the next key, sets the previous key's `NotAfterUtc = now + MaxIssueTtlDays + OfflineGraceDays + ReadOnlyWindowDays` (**120 days**) so every licence it signed stays verifiable. `GET /licensing/keys` (anonymous, rate-limit `auth`) returns the **public** license key set (`LicensePublicKeySet`). Desktop trust anchor = the key set **embedded at build time** (G09 packaging) plus TLS-fetched updates (internal CA) — documented limitation in the threat model.
- **Server entitlement gate (`ServerEntitlementGate : IServerEntitlementGate`, registered `Replace`):** evaluates only for routes that are **not** exempt (`/health/*`, `/auth/*`, `/licensing/*`, `/operator/*`, `/provisioning/*`, `/admin/*` are exempt); otherwise allow iff (a) `LicenseGrants` row of the request tenant is `Active` and `now ≤ ValidUntilUtc`, **and** (b) an `Active` `Licenses` row exists for the request's `did` (a device that never activated, or whose licence was revoked, gets `403 ENTITLEMENT_REQUIRED` with `detail="license_not_active"`; a revoked one reports `LICENSE_REVOKED`). Both reads are PK/partial-index lookups through a narrow read-only `AnimoraEntitlementReadContext` (no tenant filter, explicit `TenantId` parameter, `AsNoTracking`, mapped only to `LicenseGrants` + `Licenses`; allowed only from `Licensing/**` — added to the S14 source-scan whitelist). No cross-request cache at launch (A-RULE 033; revocation is immediate). Order proof test: unauthenticated → 401; no permission → 403 `PERMISSION_DENIED`; only then entitlement.
- **Endpoints (`ServerRoutes.Licensing = "/licensing"`, authenticated tenant principal, device `Active`, rate-limit `auth`):** `POST /licensing/activate`, `POST /licensing/renew` (body `LicenseRequest(installationIdHash, currentSequence?)` → `LicenseResponse(token, expiresUtc, offlineGraceUntilUtc, sequence, serverTimeUtc)`), `GET /licensing/status` → `LicenseStatusResponse(grantStatus, validUntilUtc?, planCode?, seatLimit?, maxDevices?, activeLicenses, serverTimeUtc)`, `GET /licensing/keys`. Operator: `GET /operator/tenants/{id}/licenses`, `POST /operator/tenants/{id}/licenses/{licenseId}/revoke` (body reason; marks Revoked, audited; the device learns at next online contact — an offline copy cannot be recalled, bounded by `MaxIssueTtlDays`), `POST /operator/tenants/{id}/licenses/recheck`, `POST /operator/licensing/keys/rotate`. Wire DTOs in `Animora.Contracts.Wire.Licensing` (additive, **no `WireContract.Version` bump**).
- **Migration (server, next free ordinal, `<NNN>_Licensing`):** `LicenseGrants`, `Licenses`, indexes above, **backfill** `INSERT INTO "LicenseGrants" SELECT … FROM "Subscriptions"` (so tenants provisioned before this phase are not locked out), `UPDATE` nothing else. Update `ServerSchemaVersion.ExpectedHead`.
- **New ErrorCodes (region "Licensing", +8):** `LicenseInvalid ("LICENSE_INVALID", Entitlement)`, `LicenseKeyUnknown ("LICENSE_KEY_UNKNOWN", Entitlement)`, `LicenseExpired ("LICENSE_EXPIRED", Entitlement)`, `LicenseRevoked ("LICENSE_REVOKED", Entitlement)`, `LicenseDeviceMismatch ("LICENSE_DEVICE_MISMATCH", Forbidden)`, `LicenseDeviceLimitReached ("LICENSE_DEVICE_LIMIT_REACHED", Conflict)`, `LicenseClockRollback ("LICENSE_CLOCK_ROLLBACK", Entitlement)`, `LicenseNotGranted ("LICENSE_NOT_GRANTED", Entitlement)`.

## §Implementation plan

1. ErrorCodes +8. Create `src/Animora.Server.Licensing` (csproj, refs, `InternalsVisibleTo Animora.Server.Tests`; add to `Animora.sln`); `Verification/{LicensePayload,LicenseEnvelope,LicenseVerifier,ILicenseKeySet,LicensePublicKey,HighWaterClock,LicenseVerdict,LicensePolicy,CapabilityGate,DeviceFingerprint,SealedEntitlement,ISealedEntitlementStore,SealedEntitlementMerge,Base64Url}.cs`; `Issuance/{ILicenseSigner,EcdsaLicenseSigner,LicenseCanonicalPayload,LicensingOptions}.cs`.
2. Wire DTOs `Animora.Contracts.Wire/Licensing/*.cs`.
3. Persistence: `Licensing/{LicenseGrant,LicenseRecord,LicenseGrantProjector,LicenseIssuer,AnimoraEntitlementReadContext,LicenseStore}.cs` + configurations; map `LicenseGrants` in `AnimoraOperatorDbContext`; migration `.sandbox/tools/dotnet-ef migrations add Licensing --project src/Animora.Persistence.PostgreSql --startup-project src/Animora.Persistence.PostgreSql --context AnimoraServerDbContext --output-dir Migrations/Server`, rename to the next free ordinal, add backfill + partial unique index by hand, update `ServerSchemaVersion.ExpectedHead`.
4. Server: `Licensing/{LicenseEndpoints,ServerEntitlementGate,OutboxLicenseIssuanceScheduler,LicenseIssueConsumer,LicenseRecheckConsumer,LicenseKeyBootstrapper}.cs`, `Operator/{LicenseOperatorEndpoints}.cs`; edit `SubscriptionService` (call scheduler on every mutation), `ServerRoutes` (+Licensing), `Program.cs`/DI `AddAnimoraLicensing()` (Replace gate + scheduler), `tests/Animora.Domain.Tests/LayeringRulesTests.cs` (whitelist).
5. Tests (below); `scripts/sandbox/build.sh full` → 0/0; `scripts/sandbox/test.sh Server` → green; `scripts/sandbox/run.sh` → `SMOKE RUN OK` (desktop unchanged).
6. `PROGRESS.md` per recipe; commit.

## Files to create / modify

| Path | Contents |
|---|---|
| `src/Animora.Server.Licensing/**` (new project) | `Verification/` pure half + `Issuance/` half |
| `src/Animora.Contracts.Wire/Licensing/*.cs` | `LicenseRequest`, `LicenseResponse`, `LicenseStatusResponse`, `LicensePublicKeySet`, `LicensePublicKey` |
| `src/Animora.Domain/Results/ErrorCodes.cs` (edit) | +8 codes |
| `src/Animora.Persistence.PostgreSql/Licensing/*.cs`, `Configurations/License*.cs`, `Migrations/Server/*Licensing*`, `Operator/AnimoraOperatorDbContext.cs` (edit), `Subscriptions/SubscriptionService.cs` (edit) | grants, licences, issuer, projector, read context |
| `src/Animora.Server/Licensing/*.cs`, `Operator/LicenseOperatorEndpoints.cs`, `Hosting/ServerRoutes.cs`, `Program.cs` (edit) | endpoints, gate, consumers, key bootstrap |
| `Animora.sln`, `src/Animora.Server/Animora.Server.csproj`, `tests/Animora.Server.Tests/Animora.Server.Tests.csproj` (edit) | project wiring |
| `tests/Animora.Server.Tests/Licensing/*.cs`, `tests/Animora.Domain.Tests/LayeringRulesTests.cs` (edit) | tests |

## Contracts / schema added

Project `Animora.Server.Licensing`; tables `LicenseGrants`, `Licenses`; endpoints `/licensing/{activate,renew,status,keys}` + operator licence/key endpoints; wire DTOs `Animora.Contracts.Wire.Licensing.*` (additive); seams `LicenseVerifier`, `LicensePolicy`, `CapabilityGate`, `HighWaterClock`, `DeviceFingerprint`, `ISealedEntitlementStore`, `ILicenseSigner`, `ServerEntitlementGate`; ErrorCodes +8.

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
- **Licensing honesty (ARCH §13.2):** never document the signature as an anti-tamper guarantee against the machine owner; every online-touching capability is enforced by `ServerEntitlementGate`; licence/grant state is never trusted from a client claim.
- **Time (A-RULE 011):** all client-side decisions use `max(now, highWater)`; server issues time only from `TimeProvider`.

## Out of scope (owning phase)

- Client wiring: `AllowAllEntitlementGate` replacement, activation UX, credential-mirror fallback, DPAPI-backed `ISealedEntitlementStore`, fingerprint component collection, high-water persistence, key-set embedding → **G06** (G09 for build-time key embedding).
- Operator web screens for licences/keys → **S19**. Subscription lifecycle itself → **S14**. Cross-tenant analytics → deferred (ROADMAP §5).
- Hardware-token licences, obfuscation/anti-debug of the desktop binary, remote kill — not provided (honest threat model).

## Acceptance criteria (mechanical)

1. `scripts/sandbox/build.sh full` = 0/0; `scripts/sandbox/run.sh` = `SMOKE RUN OK`; no file under `src/Animora.Client.Services`, `src/Animora.Desktop`, `src/Animora.Persistence.Sqlite`, `src/Animora.Infrastructure.Local` changed.
2. `grep -rn "Npgsql\|Microsoft.AspNetCore\|Microsoft.EntityFrameworkCore" src/Animora.Server.Licensing` returns nothing; `grep -rn "using Animora.Application" src/Animora.Server.Licensing/Verification` returns nothing; `grep -rn "ECDsa\|HMAC\|new Aes" src --include=*.cs -l` lists only the S05 signer, `Licensing/Issuance`, `Licensing/Verification` and S15's HMAC verifier (no custom crypto).
3. Pure tests pass: `LicenseEnvelopeTests.RoundTrip_AndStrictParsing`, `LicenseVerifierTests.ValidSignature_Accepted`, `…TamperedPayload_Rejected`, `…UnknownKeyId_Rejected`, `…RotatedKey_StillVerifiesOlderLicence`, `LicensePolicyTests.Matrix_ValidGraceReadOnlyLocked_Table`, `LicensePolicyTests.WrongInstallation_Locked`, `LicensePolicyTests.BackwardClock_NeverExtends_AndRollbackOver24h_ReadOnly`, `LicensePolicyTests.FailClosed_OnAnyVerifierFailure`, `CapabilityGateTests.RiskByState_Table`, `HighWaterClockTests.OnlyMovesForward_ServerTimeNeverLowers`, `DeviceFingerprintTests.StableOrderIndependent_RawNeverExposed`, `SealedEntitlementMergeTests.NeverLowersHighWaterOrSequence`, `LicenseIssuerTests.BoundToDeviceHash_FromStore_NotBody`, `…SupersedesPrevious_SequenceMonotonic`, `…TtlCappedAt40Days_AndGrantValidUntil`, `…DeviceLimit_Enforced`, `…RevokedGrant_Denied`, `LicenseGrantProjectorTests.OlderSourceEvent_NeverOverwritesNewer`, `LicenseGrantProjectorTests.StatusMap_SuspendedExpiredCancelled`, `ServerEntitlementGateTests.ExemptRoutes_And_UnactivatedDevice_403_And_Order_PermissionBeforeEntitlement`, `LicenseKeyTests.Bootstrap_CreatesOneKey_RotationKeepsOldVerifiable120Days`, `LicensePayloadTests.CanonicalJson_ParityWithCommandFingerprintCanonicalizer`, `LicensingLayeringTests.VerificationHalf_ClosureHasNoServerAssemblies`, `LicensingLayeringTests.OnlyLicensingProject_WhitelistedForClients`, `LicenseSecretsTests.TokenAndPrivateKey_NeverLoggedOrAudited`.
4. PG-gated (**mandatory G07**): `LicensePersistenceTests.ActivateRenew_OneLiveLicencePerDevice_PartialUnique`, `…ConcurrentRenew_NoDuplicateLive`, `…GrantBackfill_ForPreExistingTenants`, `…RevokedLicence_BlocksCommandsAt_ServerGate`, `…ConsumerRedelivery_Idempotent`, `…CrossTenant_CannotReadOrRevoke`.
5. `ls src/Animora.Persistence.PostgreSql/Migrations/Server | grep -c Licensing` ≥ 1 (next free ordinal); `grep -c "LICENSE_INVALID\|LICENSE_KEY_UNKNOWN\|LICENSE_EXPIRED\|LICENSE_REVOKED\|LICENSE_DEVICE_MISMATCH\|LICENSE_DEVICE_LIMIT_REACHED\|LICENSE_CLOCK_ROLLBACK\|LICENSE_NOT_GRANTED" src/Animora.Domain/Results/ErrorCodes.cs` = 8.

## PROGRESS.md update

At the end of this phase: set the `S16` row in "Phase status" to `✅ Done` (add date + one-line summary), advance **"Next phase" → `S17`**,
append one `### S16` entry to "Repo state log" (what exists now: projects, tables/migration ordinal, error codes added, test classes), log any
deviation in the "Decision & deviation log" (including the single sanctioned client→`Animora.Server.Licensing` reference), then commit `feat(server): S16 licensing & anti-piracy (signed installation-bound licence, grants, entitlement gate)` and sync per the repo git workflow.
