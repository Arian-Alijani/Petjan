# Phase S15 — Self-service onboarding intake — authenticated, idempotent, async provisioning intent from the vendor website

**Band:** S · **Track:** S (server) · **Depends on:** S14 · **Next:** S16
**Satisfies:** R-B30, R-B36, R-B29 · **Runnable after this phase:** desktop offline yes · server required no
**Produced by:** M00 (regenerated 2026-10-09; supersedes the deleted `PHASE-D10` file, second half).

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
- `ARCHITECTURE.md` §13.4, §3.1 (website is a separate vendor system; no payment inside this solution), §10.7 — only these `§`.
- `src/Animora.Persistence.PostgreSql/Subscriptions/{ProvisioningEngine,ProvisioningRequest}.cs`, `src/Animora.Persistence.PostgreSql/Operator/AnimoraOperatorDbContext.cs`, `src/Animora.Server/Workers/SubscriptionSweepWorker.cs` (S14 — the worker pattern to copy), `src/Animora.Server/Hosting/{ServerRoutes,RateLimitPolicies,SecretGuard}.cs`.

(Nothing else. `Animora-UI/**`, `Database-Sampel/**`, other phase files and `windows-frontend-roadmap/phases/**` are out of bounds.)

## Goal

Receive the public website's "registered + purchased" event as an **async, idempotent, authorized provisioning intent** (ARCH §13.4): a signed `POST /provisioning/intents` is durably recorded (202) and a worker feeds it to S14's unchanged `ProvisioningEngine` as `ActorKind.WebsiteIntent` — one engine, two callers. The website polls `GET /provisioning/intents/{key}` for the outcome. No payment processing, no e-mail, no inbound dependency on the website at runtime.

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
// S14 (exact) — reuse, never redefine
namespace Animora.Persistence.PostgreSql.Subscriptions;
public sealed record ProvisioningRequest(string IdempotencyKey, string TenantCode, string DisplayName, string? LegalName, string? Timezone,
    string OwnerUserName, string OwnerDisplayName, string? OwnerInitialPassword, string PlanCode, int? SeatLimitOverride, bool StartTrial);
public sealed record ActorRef(ActorKind Kind, string? ActorId);                 // ActorKind { Operator, WebsiteIntent, System }  (Animora.Domain.Subscriptions)
public sealed record ProvisioningResult(string TenantId, string TenantCode, string SubscriptionId, string OwnerUserId, string? OwnerTemporaryPassword, bool WasReplay);
public sealed class ProvisioningEngine { Task<Result<ProvisioningResult>> ProvisionAsync(ProvisioningRequest r, ActorRef actor, CancellationToken ct = default); }
// ErrorCodes already declared by S14: TenantCodeTaken, ProvisioningKeyConflict, PlanNotFound, ValidationFailed.   ProvisioningLedger(IdempotencyKey PK, RequestFingerprint, …) is the engine's dedupe table.
// S13: IOutboxConsumer / BackgroundService + PeriodicTimer + WorkerHeartbeat + IBackgroundAdmission;  S01: RateLimitPolicies.Operator ("operator", 30/min), ServerRoutes, SecretGuard, ServerErrorCodes
// S02: Data Protection key ring (IDataProtectionProvider);  S14: AnimoraOperatorDbContext (vendor-global tables, no tenant filter; only from Operator/Subscriptions/Provisioning namespaces)
```

## §Design decisions

- **Website service identity (never a tenant user, never an operator):** table `WebsiteClients(Id varchar(26) PK, KeyId varchar(32) UNIQUE, ProtectedSecret bytea (Data Protection, purpose "Animora.Provisioning.Client.v1"), Status smallint (Active=0, Revoked=1), CreatedUtc, LastUsedUtc?, Label varchar(100))`. Secrets are created/rotated **only** by an operator call `POST /operator/website-clients` (returns the secret **once**; S14 operator auth + audit) and `POST /operator/website-clients/{id}/revoke`. Env-only bootstrap is not offered. Max 3 active clients (rotation window).
- **Request authentication = HMAC-SHA256 request signing** (`System.Security.Cryptography.HMACSHA256`, no custom crypto, no JWT/IdP): headers `X-Animora-Key` (KeyId), `X-Animora-Timestamp` (unix seconds), `X-Animora-Signature` (hex of `HMAC(secret, METHOD + "\n" + path + "\n" + timestamp + "\n" + sha256hex(body))`), `Idempotency-Key`. Verified with `CryptographicOperations.FixedTimeEquals`; timestamp skew ≤ **5 min**; a `(KeyId, signature)` replay inside the skew window is rejected by an in-memory bounded `ReplayWindow` (size 4096, single host — documented; the durable dedupe is the idempotency key). Failure ⇒ `401 PROVISIONING_SIGNATURE_INVALID` (same body for unknown key / bad signature / skew) + `OperatorAudit` (outcome `denied`, no secret). Transport must be HTTPS (internal CA, ARCH §10.5); plain HTTP refused outside Development.
- **Intent record (`ProvisioningIntents`):** `Id varchar(26) PK`, `IdempotencyKey varchar(128) UNIQUE`, `ClientId`, `RequestFingerprint char(64)`, `PayloadProtected bytea` (the request JSON incl. optional initial password, protected via Data Protection purpose `"Animora.Provisioning.Intent.v1"`; the plaintext password is **never** logged or stored in the clear), `Status smallint (Received=0, Processing=1, Completed=2, Failed=3, Dead=4)`, `Attempts int`, `NextAttemptUtc`, `ClaimToken varchar(26)?`, `ClaimedUntilUtc?`, `ResultJson jsonb?` (tenant id/code/subscription id — **no secrets**), `ProtectedTempPassword bytea?` + `TempPasswordExpiresUtc?` (72 h, wiped on first successful read or at expiry), `ErrorCode varchar(64)?`, `ReceivedUtc`, `CompletedUtc?`. Index `IX_ProvisioningIntents_Status_NextAttemptUtc`.
- **Endpoints (anonymous scheme, signature-authenticated, rate-limit `operator`, body ≤ 8 KiB, route constant `ServerRoutes.Provisioning = "/provisioning"` added here):** `POST /provisioning/intents` → validates shape only (the engine owns business validation), inserts the intent (`INSERT … ON CONFLICT (IdempotencyKey) DO NOTHING`; conflict ⇒ same fingerprint **202 + existing id** (replay), different fingerprint **409 PROVISIONING_KEY_CONFLICT**), returns `202 { intentId, status }`; `GET /provisioning/intents/{idempotencyKey}` → `{ status, tenantCode?, tenantId?, subscriptionId?, ownerTemporaryPassword? (once), errorCode? }` (the temporary owner password is returned on the first `Completed` read **only** and wiped; owner has `MustChangePassword=true`). A client may only read intents it created.
- **Worker `ProvisioningIntentWorker`** (BackgroundService, poll 2 s idle-backoff to 15 s, under `IBackgroundAdmission`, claim ≤ 5 via `FOR UPDATE SKIP LOCKED` + 60 s fenced lease exactly like S13): decrypt payload → `ProvisioningEngine.ProvisionAsync(request, new ActorRef(ActorKind.WebsiteIntent, clientId))`. Engine success (incl. `WasReplay`) ⇒ `Completed` + result; **permanent** failures (`TENANT_CODE_TAKEN`, `PLAN_NOT_FOUND`, `VALIDATION_FAILED`, `PROVISIONING_KEY_CONFLICT`) ⇒ `Failed` with the code (no retry); transient failures ⇒ retry with S13 `ServerRetryPolicy` up to 8 attempts ⇒ `Dead` + audit. Because the engine is itself idempotent on the same key, a crash between engine commit and intent update re-runs safely (`WasReplay=true`). **One engine, two callers — a test asserts the worker and `POST /operator/tenants` produce identical tenant/subscription/ledger state for the same request.**
- **Retention:** completed/failed intents' `PayloadProtected` is nulled after **7 days**, rows kept 400 days (`ProvisioningIntentRetentionWorker`, daily, under admission, ≤ 500 rows/pass).
- **Operator visibility:** `GET /operator/provisioning/intents?status&page&pageSize` (max 200) + `POST /operator/provisioning/intents/{id}/retry` (Dead/Failed-transient → Received) — read model for S19.
- **New ErrorCodes (region "Provisioning", +2):** `ProvisioningSignatureInvalid ("PROVISIONING_SIGNATURE_INVALID", Forbidden)`, `ProvisioningIntentNotFound ("PROVISIONING_INTENT_NOT_FOUND", NotFound)`.

## §Implementation plan

1. ErrorCodes +2; `ServerRoutes.Provisioning`.
2. Persistence: `Provisioning/{WebsiteClient,ProvisioningIntent}.cs` + configurations + `IProvisioningIntentStore` (insert-on-conflict, claim SQL, fenced complete/fail, wipe-password); map in `AnimoraOperatorDbContext`; migration `.sandbox/tools/dotnet-ef migrations add ProvisioningIntents --project src/Animora.Persistence.PostgreSql --startup-project src/Animora.Persistence.PostgreSql --context AnimoraServerDbContext --output-dir Migrations/Server`, rename to the next free ordinal, update `ServerSchemaVersion.ExpectedHead`.
3. Server: `Provisioning/{RequestSignatureVerifier,ReplayWindow,WebsiteClientService,ProvisioningEndpoints,ProvisioningIntentWorker,ProvisioningIntentRetentionWorker}.cs`; operator endpoints `Operator/WebsiteClientEndpoints.cs`, `Operator/ProvisioningIntentEndpoints.cs`; DI `AddAnimoraProvisioning()`; heartbeat registrations; `Program.cs` mapping (outside tenant/entitlement middleware like `/operator`).
4. Tests (below); `scripts/sandbox/build.sh full` → 0/0; `scripts/sandbox/test.sh Server` → green; `scripts/sandbox/run.sh` → `SMOKE RUN OK`.
5. `PROGRESS.md` per recipe; commit.

## Files to create / modify

| Path | Contents |
|---|---|
| `src/Animora.Domain/Results/ErrorCodes.cs` (edit) | +2 codes |
| `src/Animora.Persistence.PostgreSql/Provisioning/*.cs`, `Configurations/{WebsiteClient,ProvisioningIntent}Configuration.cs`, `Migrations/Server/*ProvisioningIntents*` | tables + store |
| `src/Animora.Server/Provisioning/*.cs`, `Operator/{WebsiteClientEndpoints,ProvisioningIntentEndpoints}.cs`, `Hosting/ServerRoutes.cs`, `Program.cs` (edit) | intake, workers, operator views |
| `tests/Animora.Server.Tests/Provisioning/*.cs` | tests |

## Contracts / schema added

Tables `WebsiteClients`, `ProvisioningIntents`; endpoints `POST|GET /provisioning/intents[/{key}]`, operator `website-clients` + `provisioning/intents`; headers `X-Animora-Key|Timestamp|Signature`, `Idempotency-Key`; ErrorCodes +2; no `WireContract` change (website contract is documented HTTP, not a client wire DTO).

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
- **Secrets (CONVENTIONS §7):** website secrets and the temporary password exist only Data-Protection-protected at rest and in the single response that returns them; never in logs, audit detail or config files.

## Out of scope (owning phase)

- Business validation/creation logic → **S14** (`ProvisioningEngine`, reused unchanged). License issuance after provisioning → **S16** (replaces `ILicenseIssuanceScheduler`).
- The website itself, payment, receipts, e-mail/SMS of credentials → vendor-side system, not in this solution (ARCH §3.1). Operator web UI for intents → **S19**.
- Customer first login / device activation → **G06**.

## Acceptance criteria (mechanical)

1. `scripts/sandbox/build.sh full` = 0/0; `scripts/sandbox/run.sh` = `SMOKE RUN OK`; client sources unchanged.
2. `grep -rn "ProvisioningEngine" src/Animora.Server/Provisioning src/Animora.Server/Operator` shows exactly **one** `ProvisionAsync` call site per caller type (worker + operator create-tenant) and no tenant/subscription construction outside S14's engine (`grep -rn "Tenant.Create(\|Subscription.Start(" src/Animora.Server` returns nothing).
3. Pure tests pass: `RequestSignatureVerifierTests.ValidSignature_Accepted`, `…TamperedBody_Rejected`, `…SkewBeyond5Min_Rejected`, `…UnknownKeyAndBadSignature_SameError`, `…ReplayInsideWindow_Rejected`, `ReplayWindowTests.BoundedAndExpires`, `ProvisioningEndpointsTests.Post_ReturnsAccepted_AndDuplicateSameFingerprint_ReplaysSameId`, `…DuplicateDifferentFingerprint_409`, `…OversizeBody_413`, `…ClientCannotReadOthersIntent`, `ProvisioningEndpointsTests.TempPassword_ReturnedOnceThenWiped`, `ProvisioningIntentWorkerTests.PermanentFailure_NoRetry`, `…TransientFailure_RetriesWithJitter_ThenDead`, `…CrashAfterEngineCommit_RerunIsReplayNoSecondTenant`, `…UsesWebsiteIntentActor`, `ProvisioningParityTests.WebsiteAndOperatorPaths_ProduceSameState`, `WebsiteClientServiceTests.SecretShownOnce_MaxThreeActive_Revocation`, `ProvisioningSecretsTests.PayloadAndPassword_NeverInLogsOrAudit`.
4. PG-gated (**mandatory G07**): `ProvisioningIntentStoreTests.InsertOnConflict_Race_OneRow`, `…TwoWorkers_NeverClaimSameIntent`, `…ExpiredLease_Reclaimable_FencedCompletionRejected`, `ProvisioningRetentionTests.PayloadWiped_After7Days`.
5. `ls src/Animora.Persistence.PostgreSql/Migrations/Server | grep -c ProvisioningIntents` ≥ 1 (next free ordinal); `grep -c "PROVISIONING_SIGNATURE_INVALID\|PROVISIONING_INTENT_NOT_FOUND" src/Animora.Domain/Results/ErrorCodes.cs` = 2.

## PROGRESS.md update

At the end of this phase: set the `S15` row in "Phase status" to `✅ Done` (add date + one-line summary), advance **"Next phase" → `S16`**,
append one `### S15` entry to "Repo state log" (what exists now: projects, tables/migration ordinal, error codes added, test classes), log any
deviation in the "Decision & deviation log", then commit `feat(server): S15 self-service onboarding intake (signed, idempotent, async provisioning intent)` and sync per the repo git workflow.
