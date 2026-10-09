# Phase S14 — Subscription context & operator API — plans, subscription lifecycle, operator identity, shared provisioning engine

**Band:** M · **Track:** S (server) · **Depends on:** S05, S13 · **Next:** S15
**Satisfies:** R-B05, R-B30, R-B29 · **Runnable after this phase:** desktop offline yes · server required no
**Produced by:** M00 (regenerated 2026-10-09; supersedes the deleted `PHASE-D10` file, first half).

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
- `ARCHITECTURE.md` §13.1, §13.3, §13.4, §4 (surfaces table) and §10.7 (audit plane) — only these `§`.
- `src/Animora.Server/Authorization/{TenantContextMiddleware,CoarsePolicies,EntitlementMiddleware}.cs` (S05; edited here), `src/Animora.Server/Hosting/{ServerRoutes,RateLimitPolicies}.cs` (S01).
- `src/Animora.Persistence.PostgreSql/Identity/{TenantProvisioner,SecurityAuditWriter}.cs` (S04; the provisioner is wrapped, not rewritten) and `src/Animora.Server/Workers/*` + `OutboxMessageTypes`/`IOutboxConsumer` (S13).
- `src/Animora.Domain/Identity/Tenant.cs` (`TenantStatus {Active, Suspended, Closed}`, `Tenant.SetStatus`).

(Nothing else. `Animora-UI/**`, `Database-Sampel/**`, other phase files and `windows-frontend-roadmap/phases/**` are out of bounds.)

## Goal

Build the **server-only Subscription bounded context** (`Plan`, `Subscription`, gap-free `BillingPeriod`, append-only `SubscriptionEvent`; seats = limit + derived usage) with a pure, table-driven lifecycle (`Trial → Active → GracePeriod → Expired`, `Suspended`, `Cancelled`), the **operator identity** (a vendor-global account set, separate bearer scheme, never a tenant user), the **operator API** (`/operator/*`: tenants, plans, create / extend / reduce / suspend / reinstate / cancel / renew / seats / plan-change, event log) and the **single provisioning engine** (`ProvisioningEngine`) that creates *tenant + owner + feed state + subscription* in one transaction idempotently — the one code path the website intake (S15) and the human operator both call (ARCH §13.4). Subscription state drives `Tenant.Status` (the existing S05 `TENANT_SUSPENDED` gate); the running clinic never asks Subscription whether it may operate — that is Licensing (S16, R-B30). The server records `BillingPeriod` amounts but **never charges a card**.

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
public enum TenantStatus { Active = 0, Suspended = 1, Closed = 2 }
public sealed class Tenant : AggregateRoot { string DisplayName; string? LegalName; string Timezone; string DefaultCurrency; TenantStatus Status;
    static Tenant Create(string displayName, IClock clock, string? legalName = null, string timezone = "Asia/Tehran", string defaultCurrency = "IRT");
    void SetStatus(TenantStatus s); void Rename(string displayName, string? legalName = null); }
public enum StaffRole { /* Manager, … */ }
```
```csharp
// S02/S03/S04/S05/S13 seams this phase consumes (exact names)
namespace Animora.Persistence.PostgreSql.Tenancy;  public interface ITenantContext { string? TenantId { get; } bool IsOperator { get; } }   // NullTenantContext = default; S05 RequestTenantContext
// AnimoraServerDbContext (tenant-filtered, "Tenant"+"SoftDelete" named filters; a null tenant THROWS TenantContextMissingException) — never used for operator reads.
// S04: TenantProvisioner.CreateTenantWithOwnerAsync(...) (Tenant + system RoleDefinitions + Manager User/StaffMembership in one UoW);  ITenantDirectory.FindByCodeAsync(code);  ISecurityAuditWriter (append-only SecurityAudit)
// S05: CoarsePolicies.OperatorOnly (name registered, identity filled HERE);  TenantContextMiddleware / EntitlementMiddleware (edited: skip /operator);  Microsoft.Extensions.Identity.Core PasswordHasher (pin 10.0.12 exists)
// S01: ServerRoutes.Operator = "/operator";  RateLimitPolicies.Operator = "operator" (30/min);  ServerErrorCodes (transport-only strings)
// S13: IOutboxConsumer { string MessageType; string Name; Task<Result> ConsumeAsync(OutboxMessage m, CancellationToken ct); };  OutboxMessageTypes.SubscriptionExpiry = "subscription.expiry";  IServerOutbox.EnqueueAsync;  IBackgroundAdmission;  BackgroundService + PeriodicTimer + WorkerHeartbeat pattern
```

## §Design decisions

- **Placement.** Pure domain types in `src/Animora.Domain/Subscriptions/` (no EF/DI; compiled into Domain but **never referenced by any client project** — enforced by `SubscriptionLayeringTests`); persistence + services in `Animora.Persistence.PostgreSql/Subscriptions/` and `/Operator/`; endpoints in `Animora.Server/Operator/`. New narrow context `AnimoraOperatorDbContext` (namespace `Animora.Persistence.PostgreSql.Operator`): maps the vendor-global tables below **plus `Tenants` and `StaffMemberships` read-only/status-write**, has **no tenant filter**, is **never migrated** (tables belong to the main context's migration, same pattern as S13 `OutboxDbContext`), is registered scoped, and may be resolved **only** from `Animora.Server/Operator/**`, `Subscriptions/**` workers/consumers and tests (`ServerSourceScanTests.OperatorContext_OnlyUsedInOperatorAndSubscriptionNamespaces`). The S02 `IgnoreQueryFilters()` ban is untouched — this context simply has no filters, which is why its reach is small and scanned. Tenant-status writes go through exactly one method `TenantAdministration.SetStatusAsync(tenantId, status, reason, actor)` (writes `Tenant.SetStatus` + `OperatorAudit`).
- **Domain types (`Animora.Domain.Subscriptions`).** `enum SubscriptionStatus { Trial=0, Active=1, GracePeriod=2, Suspended=3, Cancelled=4, Expired=5 }`; `sealed class Plan` (`Id`, `Code` (unique, `^[a-z0-9-]{2,32}$`), `Name`, `DefaultSeatLimit`, `MonthlyAmountMinor` (long) + `Currency` ("IRT"), `TrialDays`, `IsActive`); `sealed class Subscription` (`Id`, `TenantId`, `PlanId`, `Status`, `SeatLimit`, `CurrentPeriodStartUtc`, `CurrentPeriodEndUtc`, `GraceEndsUtc?`, `CancelRequestedUtc?`, `CancelAtPeriodEnd`, `NextPeriodSequence`, `NextEventSequence`, `ConcurrencyStamp` (uuid string, concurrency token), `CreatedUtc`, `UpdatedUtc`); `sealed record BillingPeriod(SubscriptionId, Sequence, StartUtc, EndUtc, AmountMinor, Currency)`; `sealed record SubscriptionEvent(SubscriptionId, Sequence, Type, OccurredUtc, ActorKind, ActorId, PayloadJson)`; `enum ActorKind { Operator, WebsiteIntent, System }`; `SubscriptionLifecycle.EnsureAllowed(from, to)` (table-driven, returns `Result`, fails `ErrorCodes.SubscriptionInvalidTransition`): `Trial→{Active, Suspended, Cancelled, GracePeriod, Expired}`, `Active→{GracePeriod, Suspended, Cancelled, Active(renew/extend)}`, `GracePeriod→{Active, Suspended, Cancelled, Expired}`, `Suspended→{Active, Cancelled, Expired}`, `Expired→{Active (renew), Cancelled}`, `Cancelled` terminal. Operations are **methods on `Subscription` returning `Result`** and appending a `SubscriptionEvent`: `Start(plan, now)`, `Extend(days|untilUtc)`, `Reduce(untilUtc)` (never earlier than `now`, never below the period start; shortening past an already-billed period is rejected), `Suspend(reason)`, `Reinstate()`, `Cancel(immediate|atPeriodEnd)`, `Renew(now, plan?)` (opens `BillingPeriod` with `Sequence = NextPeriodSequence++`, **gap-free**), `ChangeSeats(limit, activeSeatsUsed)` (`limit < used` ⇒ `SEAT_LIMIT_EXCEEDED`), `ChangePlan(plan)`, `Tick(now, GraceDays)` (Active/Trial past end ⇒ `GracePeriod` with `GraceEndsUtc = end + GraceDays` (default **7**); GracePeriod past `GraceEndsUtc` ⇒ `Expired`; `CancelAtPeriodEnd` past end ⇒ `Cancelled`). **Seats** are not a table: limit lives on `Subscription`, usage = count of `StaffMemberships` with `Status=Active` (derived by `ISeatUsageReader`); every limit change is an event.
- **Subscription ⇒ tenant status (the only enforcement bridge).** `SubscriptionTenantStatusMap`: `Trial|Active|GracePeriod → TenantStatus.Active` (grace keeps the clinic working — the commercial courtesy window; licensing's own grace is S16's), `Suspended|Expired → Suspended`, `Cancelled → Closed`. Applied in the **same transaction** as the transition. The S05 middleware already turns `Suspended`/`Closed` into 403 `TENANT_SUSPENDED`; there is no second gate.
- **Tables (migration `<next free ordinal>_Subscription`, one migration, non-`SyncableEntity` vendor-global):** `Plans(Id varchar(26) PK, Code varchar(32) UNIQUE, Name varchar(100), DefaultSeatLimit int, MonthlyAmountMinor bigint, Currency varchar(3), TrialDays int, IsActive bool, CreatedUtc)` + **seed rows** `trial` (3 seats, 14 days, 0) and `standard` (5 seats, 0 trial days, amount from option `SubscriptionOptions.StandardMonthlyAmountMinor`, default `0` = "set by operator"); `Subscriptions(Id PK, TenantId varchar(26) UNIQUE, PlanId, Status smallint, SeatLimit int, CurrentPeriodStartUtc, CurrentPeriodEndUtc, GraceEndsUtc?, CancelRequestedUtc?, CancelAtPeriodEnd bool, NextPeriodSequence int, NextEventSequence int, ConcurrencyStamp uuid, CreatedUtc, UpdatedUtc)` + `IX_Subscriptions_Status_CurrentPeriodEndUtc`; `BillingPeriods(SubscriptionId, Sequence int, StartUtc, EndUtc, AmountMinor bigint, Currency, PK(SubscriptionId, Sequence))`; `SubscriptionEvents(SubscriptionId, Sequence int, Type varchar(48), OccurredUtc, ActorKind smallint, ActorId varchar(26)?, PayloadJson jsonb, PK(SubscriptionId, Sequence))` + trigger `trg_SubscriptionEvents_immutable` (raises on UPDATE/DELETE); `OperatorAccounts(Id PK, UserName varchar(64), NormalizedUserName UNIQUE, PasswordHash, SecurityStamp, Status smallint, FailedAttempts int, LockoutEndUtc?, MustChangePassword bool, CreatedUtc, LastLoginUtc?)`; `OperatorAudit(Id PK, OccurredUtc, OperatorId?, ActorKind smallint, Action varchar(64), TargetTenantId varchar(26)?, Outcome varchar(16), CorrelationId varchar(64), DetailJson jsonb)` + immutable trigger; `ProvisioningLedger(IdempotencyKey varchar(128) PK, RequestFingerprint char(64), TenantId varchar(26), SubscriptionId varchar(26), ActorKind smallint, CreatedUtc)`. Event/period sequences are allocated from the `Subscription` row counters under its optimistic `ConcurrencyStamp` (retry on conflict ⇒ gap-free, no `nextval`).
- **Operator identity.** `OperatorAccount` is **not** a tenant `User` and has no `TenantId`. Second bearer scheme `OperatorAuth.Scheme = "Animora.Operator"` via `AddBearerToken(...)` (opaque, Data-Protection purpose is scheme-specific automatically; **access lifetime 30 min, no refresh, no sliding** → re-login). Claims: `sub` (operator id), `ost` (security stamp), `role=operator`. `CoarsePolicies.OperatorOnly` = authenticated on that scheme **and** `ITenantContext.IsOperator`; `OperatorTenantContext : ITenantContext { TenantId => null; IsOperator => true }` is registered for `/operator/*` requests by a tiny `OperatorContextMiddleware` placed where S05's `TenantContextMiddleware` sits (that middleware is **edited to skip any path under `ServerRoutes.Operator`**, as is `EntitlementMiddleware`). Lockout reuses the Domain constants (5 failures / 15 min). Passwords hashed by `PasswordHasher<OperatorAccount>`; login failures are constant-time-ish (dummy verify) and always `401 OPERATOR_CREDENTIALS_INVALID`. **Bootstrap:** on startup, if `OperatorAccounts` is empty and env `Operator__BootstrapUserName` **and** `Operator__BootstrapPassword` are both set (never JSON — `SecretGuard` extended to forbid `Operator:BootstrapPassword` in files), create one account with `MustChangePassword=true` and log `operator.bootstrap.created` (no secret). With `MustChangePassword` the token may call only `POST /operator/auth/change-password`. MFA/passkeys are deferred (ROADMAP §5).
- **Operator API (`/operator`, rate-limit policy `operator`, scheme `Animora.Operator`, every call writes `OperatorAudit` in the same transaction as its effect):** `POST /operator/auth/login|logout|change-password`; `GET /operator/plans`, `POST /operator/plans`, `POST /operator/plans/{id}/deactivate`; `GET /operator/tenants?page&pageSize&status&q` (max page 200; projection `TenantListItem(Id, Code, DisplayName, TenantStatus, SubscriptionStatus, PeriodEndUtc, SeatLimit, SeatsUsed)`); `GET /operator/tenants/{id}` (+ subscription + last 20 events); `POST /operator/tenants` (create tenant + owner + subscription — **calls `ProvisioningEngine`**, requires header `Idempotency-Key`); `POST /operator/tenants/{id}/subscription/extend|reduce|suspend|reinstate|cancel|renew|seats|plan`; `GET /operator/tenants/{id}/subscription/events?afterSequence&limit≤200`; `POST /operator/tenants/{id}/owner/reset-password` (new temp password returned **once**, rotates the owner's `SecurityStamp`, audited). Every mutation takes `If-Match: <ConcurrencyStamp>` (`412` + `CONCURRENCY_CONFLICT` on mismatch). DTOs are server-internal (`Animora.Server.Operator.Models`), **not** wire DTOs — the client never calls these; `WireContract.Version` unchanged.
- **`ProvisioningEngine` (the single path, ARCH §13.4).** `Task<Result<ProvisioningResult>> ProvisionAsync(ProvisioningRequest r, ActorRef actor, CancellationToken ct)`; `ProvisioningRequest(IdempotencyKey, TenantCode, DisplayName, LegalName?, Timezone?, OwnerUserName, OwnerDisplayName, OwnerInitialPassword? (null ⇒ generated, returned once), PlanCode, SeatLimitOverride?, StartTrial)`; `ActorRef(ActorKind, ActorId?)`; `ProvisioningResult(TenantId, TenantCode, SubscriptionId, OwnerUserId, OwnerTemporaryPassword?, WasReplay)`. Algorithm in **one `ExecuteAsync` transaction** (operator context): (1) validate (`TenantCode` `^[a-z0-9-]{3,32}$`, reserved codes `admin|operator|api|sync|auth|files|health` rejected, plan exists & active); (2) look up `ProvisioningLedger` by `IdempotencyKey` — same fingerprint ⇒ return the stored result with `WasReplay=true` (**no second tenant**), different fingerprint ⇒ `PROVISIONING_KEY_CONFLICT`; (3) `TenantCode` already used ⇒ `TENANT_CODE_TAKEN`; (4) `TenantProvisioner.CreateTenantWithOwnerAsync(...)` (S04; also creates the S11 `TenantFeedState` row there); (5) `Subscription.Start(plan, now)` (+ first `BillingPeriod` unless trial) and event `subscription.created`; (6) ledger row; (7) `ILicenseIssuanceScheduler.ScheduleAsync(tenantId, subscriptionId, ct)` — a seam **declared here** (`Animora.Persistence.PostgreSql.Subscriptions`), default `NoopLicenseIssuanceScheduler` (registered `TryAdd`); **S16 replaces it** with an implementation that enqueues the `license.issue` outbox message in the same transaction — so no outbox message type is emitted before its consumer exists; (8) `OperatorAudit`/`SecurityAudit` row. Fingerprint = SHA-256 of canonical JSON of the request **without** the idempotency key and password. A retried call after a crash between steps never yields a half-created tenant (single transaction, `ON CONFLICT` on the ledger PK as the race arbiter; loser retries the read path).
- **Expiry processing.** `SubscriptionSweepWorker` (BackgroundService, every **15 min**, under `IBackgroundAdmission`, ≤ 200 rows/pass) selects `Subscriptions WHERE Status IN (Trial, Active, GracePeriod) AND (CurrentPeriodEndUtc <= now OR GraceEndsUtc <= now OR CancelAtPeriodEnd AND CurrentPeriodEndUtc <= now)` and enqueues one `subscription.expiry` outbox message per row (key = subscription id + target period end ⇒ idempotent); `SubscriptionExpiryConsumer : IOutboxConsumer` runs `Subscription.Tick`, applies `SubscriptionTenantStatusMap`, writes the event + `OperatorAudit(ActorKind.System)`. Idempotent by S13 `ConsumedMessages`.
- **No payment processing, no e-mail, no Internet.** Amounts are recorded; invoices to customers are the website's job (§3.1). Notification of expiry to the vendor = the admin dashboard (S19) reading `Subscriptions`.
- **New ErrorCodes (region "Subscription", +8):** `SubscriptionNotFound ("SUBSCRIPTION_NOT_FOUND", NotFound)`, `SubscriptionInvalidTransition ("SUBSCRIPTION_INVALID_TRANSITION", Conflict)`, `SeatLimitExceeded ("SEAT_LIMIT_EXCEEDED", Conflict)`, `PlanNotFound ("PLAN_NOT_FOUND", NotFound)`, `TenantCodeTaken ("TENANT_CODE_TAKEN", Conflict)`, `ProvisioningKeyConflict ("PROVISIONING_KEY_CONFLICT", Integrity)`, `OperatorCredentialsInvalid ("OPERATOR_CREDENTIALS_INVALID", Forbidden)`, `OperatorLockedOut ("OPERATOR_LOCKED_OUT", Forbidden)`.

## §Implementation plan

1. Domain: `Subscriptions/{SubscriptionStatus,Plan,Subscription,BillingPeriod,SubscriptionEvent,ActorKind,SubscriptionLifecycle,SubscriptionTenantStatusMap}.cs`; ErrorCodes +8 (new `#region Subscription`).
2. Persistence: `Operator/AnimoraOperatorDbContext.cs` + configurations (`Plan`, `Subscription` (concurrency token `ConcurrencyStamp`), `BillingPeriod`, `SubscriptionEvent`, `OperatorAccount`, `OperatorAudit`, `ProvisioningLedger`; `Tenants`/`StaffMemberships` mapped read-only except `Tenants.Status`); `Subscriptions/{ISubscriptionService,SubscriptionService,ISeatUsageReader,SeatUsageReader,ProvisioningEngine,TenantAdministration,SubscriptionOptions}.cs`; migration: `.sandbox/tools/dotnet-ef migrations add Subscription --project src/Animora.Persistence.PostgreSql --startup-project src/Animora.Persistence.PostgreSql --context AnimoraServerDbContext --output-dir Migrations/Server`, rename to the next free ordinal, hand-add the two immutability triggers + plan seed, update `ServerSchemaVersion.ExpectedHead`. (The migration is generated from the **main** context — add the entity classes to its model via `modelBuilder.ApplyConfigurationsFromAssembly` with the vendor-global configurations marked `ExcludeFromMigrations: false`; the operator context only **maps** them.)
3. Server: `Operator/{OperatorAuth,OperatorContextMiddleware,OperatorTenantContext,OperatorAccountService,OperatorBootstrapper,OperatorEndpoints,Models/*}.cs`, `Workers/SubscriptionSweepWorker.cs`, `Subscriptions/SubscriptionExpiryConsumer.cs`; DI `AddAnimoraSubscriptions()` + `AddAnimoraOperator()`; edit `TenantContextMiddleware` + `EntitlementMiddleware` (skip `/operator`), `CoarsePolicies.OperatorOnly`, `SecretGuard` (+ operator bootstrap key), `Program.cs` pipeline (operator middleware slot), readiness: none new (operator tables share `database` probe).
4. Tests (below). `scripts/sandbox/build.sh full` → 0/0; `scripts/sandbox/test.sh Server` → green; `scripts/sandbox/run.sh` → `SMOKE RUN OK`.
5. `PROGRESS.md` per recipe; commit.

## Files to create / modify

| Path | Contents |
|---|---|
| `src/Animora.Domain/Subscriptions/*.cs`, `Results/ErrorCodes.cs` (edit) | pure subscription domain + lifecycle table + codes |
| `src/Animora.Persistence.PostgreSql/Operator/*.cs`, `Subscriptions/*.cs`, `Configurations/{Plan,Subscription,BillingPeriod,SubscriptionEvent,OperatorAccount,OperatorAudit,ProvisioningLedger}Configuration.cs`, `Migrations/Server/*Subscription*` | operator context, services, provisioning engine, tables |
| `src/Animora.Server/Operator/**`, `Workers/SubscriptionSweepWorker.cs`, `Subscriptions/SubscriptionExpiryConsumer.cs` | operator auth + API + worker + consumer |
| `src/Animora.Server/Authorization/{TenantContextMiddleware,EntitlementMiddleware,CoarsePolicies}.cs`, `Hosting/SecretGuard.cs`, `Program.cs` (edit) | operator bypass + policy + wiring |
| `tests/Animora.Server.Tests/Subscriptions/*.cs`, `tests/Animora.Server.Tests/Operator/*.cs` | tests |

## Contracts / schema added

Tables `Plans`, `Subscriptions`, `BillingPeriods`, `SubscriptionEvents`, `OperatorAccounts`, `OperatorAudit`, `ProvisioningLedger`; endpoints under `/operator` (scheme `Animora.Operator`); seams `ProvisioningEngine`, `ISubscriptionService`, `ITenantAdministration`, `ISeatUsageReader`, `ILicenseIssuanceScheduler` (+ `NoopLicenseIssuanceScheduler`); ErrorCodes +8; no wire DTO, `WireContract.Version` unchanged.

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
- **Operator surface (ARCH §4/§13.5):** operator-only, separate scheme, never a tenant principal; vendor-global tables are reachable only through `AnimoraOperatorDbContext`; operator reads never go through the tenant-scoped business context.

## Out of scope (owning phase)

- Public sign-up / website provisioning **intake** endpoint + `ProvisioningIntent` record → **S15** (calls `ProvisioningEngine` unchanged).
- License issuance, signing, device fingerprint, offline grace → **S16** (replaces `ILicenseIssuanceScheduler`, owns `license.issue`); entitlement gate replacing `AllowAllServerEntitlementGate` → **S16**.
- Operator web UI → **S19**; insight/analytics → **S20** / deferred (ROADMAP §5). Client online entitlement/activation → **G06**.
- Card/payment processing, invoices to customers, MFA/passkeys, dunning e-mail — not in this product (ROADMAP §5 / ARCH §3.1).

## Acceptance criteria (mechanical)

1. `scripts/sandbox/build.sh full` = 0/0; `scripts/sandbox/run.sh` = `SMOKE RUN OK`; client sources unchanged.
2. `grep -rn "Animora.Domain.Subscriptions" src/Animora.Client.Services src/Animora.Desktop src/Animora.Persistence.Sqlite src/Animora.Infrastructure.Local` returns nothing; `grep -rn "Stripe\|PayPal\|Zarinpal\|SmtpClient" src` returns nothing.
3. Pure tests pass: `SubscriptionLifecycleTests.AllowedAndForbiddenTransitions_Table`, `SubscriptionTests.Extend_Reduce_Renew_Cancel_Rules`, `SubscriptionTests.Reduce_NeverBeforeNow_NeverBelowPeriodStart`, `SubscriptionTests.Renew_BillingPeriodSequence_GapFree`, `SubscriptionTests.EventSequence_GapFree_AppendOnly`, `SubscriptionTests.ChangeSeats_BelowUsage_Rejected`, `SubscriptionTests.Tick_TrialActiveToGraceToExpired_AndCancelAtPeriodEnd`, `SubscriptionTenantStatusMapTests.AllStatuses_Mapped`, `ProvisioningEngineTests.SameKeySameFingerprint_ReplaysWithoutSecondTenant`, `ProvisioningEngineTests.SameKeyDifferentFingerprint_Conflicts`, `ProvisioningEngineTests.ReservedOrTakenCode_Rejected`, `ProvisioningEngineTests.CallsLicenseSchedulerSeam_InsideTransaction`, `OperatorAuthTests.Login_WrongPasswordAndUnknownUser_SameError`, `OperatorAuthTests.Lockout_After5Failures`, `OperatorAuthTests.TenantToken_CannotCallOperator_And_OperatorToken_CannotCallTenantApi`, `OperatorAuthTests.MustChangePassword_BlocksOtherCalls`, `OperatorBootstrapTests.OnlyWhenEmpty_AndEnvOnly`, `SecretGuardTests.OperatorBootstrapPasswordInJson_Refused`, `OperatorEndpointsTests.IfMatchMismatch_412_ConcurrencyConflict`, `ServerSourceScanTests.OperatorContext_OnlyUsedInOperatorAndSubscriptionNamespaces`, `SubscriptionLayeringTests.ClientProjects_DoNotReferenceSubscriptions`.
4. PG-gated (**mandatory G07**): `SubscriptionPersistenceTests.CreateTenantWithSubscription_OneTransaction_RollsBackTogether`, `SubscriptionPersistenceTests.ConcurrentProvisionSameKey_OneTenant`, `SubscriptionPersistenceTests.SuspendedSubscription_BlocksTenantRequests_403TenantSuspended`, `SubscriptionPersistenceTests.EventsAndOperatorAudit_ImmutableTriggers`, `SubscriptionPersistenceTests.SweepAndConsumer_Idempotent_OnRedelivery`.
5. `ls src/Animora.Persistence.PostgreSql/Migrations/Server | grep -c Subscription` ≥ 1 (next free ordinal); `grep -c "SUBSCRIPTION_NOT_FOUND\|SUBSCRIPTION_INVALID_TRANSITION\|SEAT_LIMIT_EXCEEDED\|PLAN_NOT_FOUND\|TENANT_CODE_TAKEN\|PROVISIONING_KEY_CONFLICT\|OPERATOR_CREDENTIALS_INVALID\|OPERATOR_LOCKED_OUT" src/Animora.Domain/Results/ErrorCodes.cs` = 8.

## PROGRESS.md update

At the end of this phase: set the `S14` row in "Phase status" to `✅ Done` (add date + one-line summary), advance **"Next phase" → `S15`**,
append one `### S14` entry to "Repo state log" (what exists now: projects, tables/migration ordinal, error codes added, test classes), log any
deviation in the "Decision & deviation log", then commit `feat(server): S14 subscription context, operator API & provisioning engine` and sync per the repo git workflow.
