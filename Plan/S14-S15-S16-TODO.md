# TODO — Server Track S14 → S15 → S16 (Subscription · Onboarding Intake · Licensing)

> **این فایل خودکفاست.** تمام اطلاعات، seam های واقعی ریپو (با مسیر و امضای دقیق)،
> تصمیمات و ترتیب کار از پیش بررسی و استخراج شده‌اند. برای شروع، نیازی به باز کردن
> دوبارهٔ ریپو برای «فهمیدن» نیست — فقط فایل فاز مرجع را برای جزئیات کامل بخوان و
> پیاده‌سازی کن. اسامی اینجا اسامی واقعی درخت کد هستند.
>
> **منابع فاز (در صورت نیاز به جزئیات کامل):**
> - `backend-roadmap/phases/PHASE-S14-subscription-operator-api.md`
> - `backend-roadmap/phases/PHASE-S15-onboarding-intake.md`
> - `backend-roadmap/phases/PHASE-S16-licensing-anti-piracy.md`

---

## 🔒 قانون قفل‌شدهٔ push (الزامی — بدون استثنا)

**بعد از تکمیل هر تسک (هر چک‌باکس `[x]`): بلافاصله اجرا کن:**
```bash
scripts/git/push.sh "type(scope): message"
```
- پیام با فرمت conventional commit. مثال: `feat(server): S14 domain subscription types + error codes`.
- اگر fix سنگین/کد حجمی وسط یک تسک نوشته شد → همان‌جا push جداگانه با پیام واضح.
- exit code 2 = تعارض rebase (حل کن، دوباره اجرا). exit 3 = نبود credential (ابزار `setup_github_environment` را صدا بزن، دوباره اجرا).
- **branch کاری:** `genspark_ai_developer`. اولین push:
  `scripts/git/push.sh -b genspark_ai_developer "chore: start S14-S16 plan"`.

---

## 🧭 Baseline قطعی ریپو (بررسی‌شده 2026-10-10 — بدون کاوش دوباره)

| مورد | مقدار واقعی |
|---|---|
| Server migration head | `111_ServerOutbox` (پوشه `src/Animora.Persistence.PostgreSql/Migrations/Server/`) |
| **Next free ordinal سرور** | S14 → **`112_Subscription`** · S15 → **`113_ProvisioningIntents`** · S16 → **`114_Licensing`** |
| `ServerSchemaVersion.ExpectedHead` | در `src/Animora.Persistence.PostgreSql/ServerSchemaVersion.cs` — هر migration آن را به‌روز کن |
| `ErrorCodes` موجود | 168 کد، آخرین region = `Server command intake (Phase S06)` (خط ~696). **هیچ** کد Subscription/Operator/Provisioning/License وجود ندارد — همه باید اضافه شوند |
| `dotnet-ef` | `.sandbox/tools/dotnet-ef` — فقط همین؛ هرگز `dotnet tool install` |
| build/test/run | فقط `scripts/sandbox/build.sh [fast|full]`، `scripts/sandbox/test.sh [filter]`، `scripts/sandbox/run.sh` (→ `SMOKE RUN OK`). هرگز `dotnet build` خام |
| branch فعلی | `main` → باید روی `genspark_ai_developer` کار شود |
| پروژه‌های `src/` | Domain, Application, Persistence.Sqlite, Infrastructure.Local, Client.Services, Contracts, Contracts.Wire, Desktop, Sync.Abstractions, **Server**, **Persistence.PostgreSql** |
| پروژه‌های `tests/` | Animora.Domain.Tests, Animora.Application.Tests, Animora.Persistence.Tests, Animora.Server.Tests, Animora.Sync.Tests |

### قوانین عمومی حاکم بر هر سه فاز (از فایل‌های فاز — رعایت الزامی)
- **Layering:** `Animora.Domain` به هیچ چیز ارجاع ندارد؛ فقط `Persistence.PostgreSql` می‌تواند Npgsql/`UseNpgsql` داشته باشد؛ `Animora.Server` هرگز به `Client.Services`/`Persistence.Sqlite`/`Infrastructure.Local`/`Sync.Custom`/`Desktop` ارجاع ندهد؛ client هرگز به پروژهٔ server (استثنای تنها: S16 به `Animora.Server.Licensing` whitelist می‌شود، مصرف‌کننده G06 است).
- **C# style:** `sealed` به‌صورت پیش‌فرض؛ `sealed record` برای DTO؛ nullable محترم (بدون `!` بدون توضیح)؛ هر async public با `CancellationToken ct = default` و `ConfigureAwait(false)` در library؛ بدون `.Result`/`.Wait()`؛ خطاهای مورد انتظار → `Result`/`Result<T>` با `ErrorCode` نه exception؛ `IClock`/`TimeProvider` تزریقی (نه `DateTime.Now`)؛ idها ULID از `IdGenerator.NewId()` نه `Guid`.
- **EF:** EF Core 10؛ بدون lazy loading؛ reads `AsNoTracking()` + projection؛ filterهای `"Tenant"` و `"SoftDelete"` جدا؛ paging با max page 200؛ migration افزایشی و هرگز ویرایش بعدی؛ `EnsureCreated` فقط در test fixture.
- **Security:** ترتیب AuthN → AuthZ → Entitlement هرگز ادغام نشود؛ `TenantId` فقط server-side از membership؛ secret هرگز در config/log؛ default fail-closed؛ بدون crypto سفارشی؛ audit append-only برای هر عمل امنیتی/مالی.
- **Forbidden tech:** Redis, message broker, cloud IdP/telemetry, CDN, EF Core 11, per-tenant DB, hard-coded TLS version, Native AOT با EF, `UpdatedAt`/`nextval`/`xmin` sync cursor.
- **Offline invariant:** desktop باید کاملاً offline و بدون سرور/PG/شبکه کار کند. این سه فاز چیزی به startup دسکتاپ اضافه نمی‌کنند.

### ⚠️ تلهٔ مهم (کشف‌شده — در پلن حل شد)
فازها به `IServerOutbox.EnqueueAsync` اشاره می‌کنند، اما seam واقعی موجود در
`src/Animora.Persistence.PostgreSql/Outbox/ServerOutbox.cs` فقط
`Task<Result> AppendAsync(CommandEnvelope, SettlementResult, ct)` دارد (مخصوص command feed).
**نتیجه:** یک متد عمومی enqueue برای پیام outbox دلخواه لازم است. راه استاندارد:
یک متد `EnqueueAsync(string messageType, string tenantId, string payloadJson, string correlationId, ct)`
به `IServerOutbox`/`ServerOutbox` اضافه کن که یک `OutboxMessage` (شکل زیر) را به
`context.Set<OutboxMessage>()` اضافه می‌کند **بدون Save** (در تراکنش caller). این در S14 ساخته می‌شود.
`OutboxMessage` فیلدها: `TenantId, Id, MessageType, PayloadJson, Status(OutboxStatus.Pending), Attempts, NextAttemptUtc, ClaimToken?, ClaimedUntilUtc?, LastError?, CreatedUtc, CompletedUtc?, CorrelationId`.
> توجه: برای پیام‌های vendor-global (operator subscription.expiry و license.issue) `TenantId` مربوط به tenant هدف است (هر پیام یک tenant دارد). `OutboxMessageTypes` از قبل شامل `SubscriptionExpiry="subscription.expiry"` و `LicenseRecheck="license.recheck"` است؛ `license.issue` باید به عنوان ثابت جدید اضافه شود.

---

## 📦 Seam Reference Pack (اسامی واقعی — بدون نیاز به grep دوباره)

### Domain / Results
- `Animora.Domain.Results.ErrorCode(string Code, string Category)` — categories: `Validation|NotFound|Conflict|Forbidden|Integrity|Infrastructure|Entitlement`.
- `Result` / `Result<T>` — `Ok()`, `Fail(ErrorCode, string? detail)`, `IsSuccess/IsFailure`, `Map/Bind/OrElse`.
- `ErrorCodes.cs` در `src/Animora.Domain/Results/`: الگوی افزودن → `#region <Context>` جدید در انتها، هر کد:
  `public static readonly ErrorCode X = new("X_CODE", ErrorCode.<Kind>);`. هرگز rename/remove.
- `Animora.Domain.Time.IClock { DateTimeOffset UtcNow }`.
- `Animora.Domain.Primitives.IdGenerator.NewId()` — ULID 26 کاراکتری (overload `NewId(DateTimeOffset)` هم دارد).

### Domain / Identity
- `Animora.Domain.Identity.Tenant : AggregateRoot` — `DisplayName, LegalName?, Timezone, DefaultCurrency, Status`؛ `TenantStatus { Active=0, Suspended=1, Closed=2 }`؛ `Tenant.Create(displayName, IClock, legalName?, timezone="Asia/Tehran", defaultCurrency="IRT")`؛ `SetStatus(TenantStatus)`؛ `Rename(...)`.
- `Animora.Domain.Identity.Device : AggregateRoot` — `UserId, DeviceName, InstallationIdHash, Platform, Status(DeviceStatus{Pending|Active|Revoked}), RegisteredUtc, LastContactUtc?, RevokedUtc?, IsUsable`.
- `Animora.Domain.Identity.RiskLevel { Low, Normal, High, Critical }`.
- `Animora.Domain.Identity.OfflineCapabilityPolicy.WindowFor(RiskLevel)` — Low 30d, Normal 14d, High 72h, Critical 0.

### Persistence.PostgreSql — Tenancy / Context
- `Animora.Persistence.PostgreSql.Tenancy.ITenantContext { string? TenantId; bool IsOperator }`؛ `NullTenantContext` (default، IsOperator=false)؛ S05 `RequestTenantContext`؛ `AuthTenantContext` (IsOperator=false).
- `AnimoraServerDbContext` — tenant-filtered (filterهای "Tenant"+"SoftDelete"؛ tenant null ⇒ throw). migration از **این** context ساخته می‌شود.
- DI: `src/Animora.Persistence.PostgreSql/DependencyInjection/PersistenceServiceCollectionExtensions.cs` (`AddAnimoraPostgreSql`, `TryAddSingleton<ITenantContext, NullTenantContext>`).

### Persistence.PostgreSql — Identity (S04)
- `TenantProvisioner.CreateTenantWithOwnerAsync(...)` → `Result<ProvisionedTenant(TenantId, TenantCode, OwnerUserId)>` — Tenant + system RoleDefinitions + Manager User/StaffMembership + `TenantFeedStateRow` در **یک** UoW. مسیر: `src/Animora.Persistence.PostgreSql/Identity/TenantProvisioner.cs`.
- `ITenantDirectory.FindByCodeAsync(code)`؛ `ISecurityAuditWriter` (append-only `SecurityAudit`).
- `PasswordHasher<T>` از `Microsoft.Extensions.Identity.Core` (pin 10.0.12 موجود).

### Persistence.PostgreSql — Outbox (S13)
- `Outbox/OutboxMessage.cs`: `OutboxStatus : short { Pending, Claimed, Done, DeadLettered }` + کلاس `OutboxMessage` (فیلدها بالا ذکر شد).
- `Outbox/OutboxMessageTypes.cs`: `FeedAdvanced, AuditFlush, LicenseRecheck="license.recheck", SubscriptionExpiry="subscription.expiry"`. **license.issue را اضافه کن.**
- `Outbox/IOutboxConsumer.cs`: `{ string MessageType; string Name; Task<Result> ConsumeAsync(OutboxMessage, CancellationToken ct); }`.
- `Outbox/IServerOutbox.cs` + `ServerOutbox.cs`: فعلاً فقط `AppendAsync` — **EnqueueAsync اضافه کن** (تلهٔ بالا).
- `Outbox/OutboxConsumerRunner.cs`: الگوی fenced consume (تراکنش + `ConsumedMessages` ON CONFLICT DO NOTHING).
- `Outbox/OutboxDbContext.cs` — context جداگانهٔ outbox (همان الگوی AnimoraOperatorDbContext که S14 می‌سازد: map می‌کند، migrate نمی‌کند).

### Server — Authorization (S05)
- `src/Animora.Server/Authorization/`: `CoarsePolicies.cs` (ثابت‌ها: `TenantMember, ManagerOnly, OperatorOnly, DeviceActive`؛ `OperatorOnlyRequirement`/`OperatorOnlyHandler` موجود — **identity اینجا پر می‌شود**)، `TenantContextMiddleware.cs` (متد `internal static bool IsSkipped(PathString)` — **ویرایش: skip هر مسیر زیر `/operator` و `/provisioning` و `/licensing/keys`**)، `EntitlementMiddleware.cs` (مشابه skip)، `ServerEntitlementGate.cs` (`IServerEntitlementGate.EvaluateAsync(TenantEntitlementContext(TenantId,UserId,CommandType?), ct)` + `AllowAllServerEntitlementGate` با TryAdd — **S16 با Replace جایگزین می‌کند**).
- `TenantContextMiddleware.IsSkipped` فعلی فقط `/health`, `/auth/login`, `/auth/refresh`, `/auth/keys` را skip می‌کند. الگوی `path.StartsWithSegments(...)`.

### Server — Signing (S05)
- `src/Animora.Server/Signing/`: `ISnapshotSigner.cs`, `EcdsaSnapshotSigner.cs`, `SigningKeyStore.cs`.
- `src/Animora.Persistence.PostgreSql/Security/SigningKey.cs`: کلاس `SigningKey` با `SigningKey.Purposes { Snapshot="snapshot", License="license" }`؛ فیلدها شامل `KeyId`. جدول `SigningKeys(KeyId PK, Purpose, Algorithm, PublicKeyDer, ProtectedPrivateKey, CreatedUtc, NotAfterUtc?, RetiredUtc?)` — vendor-global. **S16 ردیف `Purpose='license'` را اینجا می‌سازد.**

### Server — Hosting (S01)
- `src/Animora.Server/Hosting/ServerRoutes.cs`: `Commands="/api/commands", Sync="/sync", Auth="/auth", Operator="/operator", Admin="/admin", HealthLive, HealthReady`. **S15 `Provisioning="/provisioning"` و S16 `Licensing="/licensing"` اضافه می‌کند.**
- `RateLimitPolicies` (S01): `Operator` policy ("operator", 30/min)، `Auth` ("auth", 10/min/IP). (مسیر `Hosting/RateLimitPolicies.cs`)
- `SecretGuard` (S01, `Hosting/SecretGuard.cs`): **S14 کلید `Operator:BootstrapPassword` را به لیست forbidden-in-files اضافه می‌کند.**

### Application
- `Animora.Application.Commands.CommandFingerprint`: `Compute(string commandType, string payloadJson)`, `Compute<T>(type, payload)`, `CanonicalJson<T>(payload)` — canonical JSON (S16 Issuance از این استفاده می‌کند).

### Program.cs — ترتیب middleware (خط ~107–145)
```
107 UseMiddleware<GlobalExceptionMiddleware>
118 UseAuthentication
119 UseMiddleware<TenantContextMiddleware>   ← OperatorContextMiddleware کنار این (S14)
120 UseAuthorization
121 UseMiddleware<EntitlementMiddleware>
131 MapAuthEndpoints / 134 MapDeviceEndpoints / 137 MapCommandEndpoints / 138 MapSyncEndpoints
143 /health/live  145 /health/ready
```
Endpointهای operator/provisioning/licensing **بیرون از** پلن tenant/entitlement map می‌شوند (مثل /operator).

---

# ════════════════ FAZ S14 — Subscription & Operator API ════════════════

**هدف:** context محدودِ Subscription (server-only) + operator identity (اسکیم bearer جدا) + operator API (`/operator/*`) + `ProvisioningEngine` (تنها مسیر ساخت tenant+owner+feed+subscription به‌صورت idempotent). وضعیت Subscription → `Tenant.Status` (گیت S05 موجود). سرور هرگز کارت شارژ نمی‌کند.

**وابستگی:** S05, S13 · **بعدی:** S15 · **Satisfies:** R-B05, R-B30, R-B29.

### کدهای خطای جدید S14 (region "Subscription"، دقیقاً +8)
```
SubscriptionNotFound        ("SUBSCRIPTION_NOT_FOUND",        NotFound)
SubscriptionInvalidTransition ("SUBSCRIPTION_INVALID_TRANSITION", Conflict)
SeatLimitExceeded           ("SEAT_LIMIT_EXCEEDED",           Conflict)
PlanNotFound                ("PLAN_NOT_FOUND",                NotFound)
TenantCodeTaken             ("TENANT_CODE_TAKEN",             Conflict)
ProvisioningKeyConflict     ("PROVISIONING_KEY_CONFLICT",     Integrity)
OperatorCredentialsInvalid  ("OPERATOR_CREDENTIALS_INVALID",  Forbidden)
OperatorLockedOut           ("OPERATOR_LOCKED_OUT",           Forbidden)
```

### Domain types (`src/Animora.Domain/Subscriptions/` — pure، بدون EF/DI، هرگز توسط client ref نشود)
- `SubscriptionStatus { Trial=0, Active=1, GracePeriod=2, Suspended=3, Cancelled=4, Expired=5 }`
- `ActorKind { Operator, WebsiteIntent, System }`
- `sealed class Plan` — `Id, Code(unique ^[a-z0-9-]{2,32}$), Name, DefaultSeatLimit, MonthlyAmountMinor(long), Currency("IRT"), TrialDays, IsActive`.
- `sealed class Subscription` — `Id, TenantId, PlanId, Status, SeatLimit, CurrentPeriodStartUtc, CurrentPeriodEndUtc, GraceEndsUtc?, CancelRequestedUtc?, CancelAtPeriodEnd, NextPeriodSequence, NextEventSequence, ConcurrencyStamp(uuid string, concurrency token), CreatedUtc, UpdatedUtc`.
- `sealed record BillingPeriod(SubscriptionId, Sequence, StartUtc, EndUtc, AmountMinor, Currency)`.
- `sealed record SubscriptionEvent(SubscriptionId, Sequence, Type, OccurredUtc, ActorKind, ActorId, PayloadJson)`.
- `SubscriptionLifecycle.EnsureAllowed(from, to) → Result` (table-driven، fail = `SubscriptionInvalidTransition`):
  - `Trial → {Active, Suspended, Cancelled, GracePeriod, Expired}`
  - `Active → {GracePeriod, Suspended, Cancelled, Active(renew/extend)}`
  - `GracePeriod → {Active, Suspended, Cancelled, Expired}`
  - `Suspended → {Active, Cancelled, Expired}`
  - `Expired → {Active(renew), Cancelled}`
  - `Cancelled` = terminal.
- عملیات = متدهای `Subscription` که `Result` می‌دهند و یک `SubscriptionEvent` append می‌کنند:
  `Start(plan, now)`, `Extend(days|untilUtc)`, `Reduce(untilUtc)` (هرگز زودتر از now، هرگز زیر period start؛ کوتاه‌کردن دورهٔ قبلاً بیل‌شده رد می‌شود), `Suspend(reason)`, `Reinstate()`, `Cancel(immediate|atPeriodEnd)`, `Renew(now, plan?)` (BillingPeriod با `Sequence=NextPeriodSequence++`، gap-free), `ChangeSeats(limit, activeSeatsUsed)` (`limit<used` ⇒ `SEAT_LIMIT_EXCEEDED`), `ChangePlan(plan)`, `Tick(now, GraceDays)` (Active/Trial بعد از end ⇒ GracePeriod با `GraceEndsUtc=end+GraceDays` default **7**؛ GracePeriod بعد از grace ⇒ Expired؛ CancelAtPeriodEnd بعد از end ⇒ Cancelled).
- `SubscriptionTenantStatusMap`: `Trial|Active|GracePeriod → TenantStatus.Active`؛ `Suspended|Expired → Suspended`؛ `Cancelled → Closed`.
- Event/period sequence از شمارنده‌های ردیف `Subscription` تحت `ConcurrencyStamp` optimistic (retry on conflict ⇒ gap-free، بدون nextval).

### جداول (migration `112_Subscription`، یک migration، vendor-global غیر-SyncableEntity)
- `Plans(Id varchar(26) PK, Code varchar(32) UNIQUE, Name varchar(100), DefaultSeatLimit int, MonthlyAmountMinor bigint, Currency varchar(3), TrialDays int, IsActive bool, CreatedUtc)` + **seed**: `trial`(3 seats, 14 days, 0), `standard`(5 seats, 0 trial, amount از `SubscriptionOptions.StandardMonthlyAmountMinor` default 0).
- `Subscriptions(Id PK, TenantId varchar(26) UNIQUE, PlanId, Status smallint, SeatLimit int, CurrentPeriodStartUtc, CurrentPeriodEndUtc, GraceEndsUtc?, CancelRequestedUtc?, CancelAtPeriodEnd bool, NextPeriodSequence int, NextEventSequence int, ConcurrencyStamp uuid, CreatedUtc, UpdatedUtc)` + `IX_Subscriptions_Status_CurrentPeriodEndUtc`.
- `BillingPeriods(SubscriptionId, Sequence int, StartUtc, EndUtc, AmountMinor bigint, Currency, PK(SubscriptionId,Sequence))`.
- `SubscriptionEvents(SubscriptionId, Sequence int, Type varchar(48), OccurredUtc, ActorKind smallint, ActorId varchar(26)?, PayloadJson jsonb, PK(SubscriptionId,Sequence))` + trigger `trg_SubscriptionEvents_immutable` (raise on UPDATE/DELETE).
- `OperatorAccounts(Id PK, UserName varchar(64), NormalizedUserName UNIQUE, PasswordHash, SecurityStamp, Status smallint, FailedAttempts int, LockoutEndUtc?, MustChangePassword bool, CreatedUtc, LastLoginUtc?)`.
- `OperatorAudit(Id PK, OccurredUtc, OperatorId?, ActorKind smallint, Action varchar(64), TargetTenantId varchar(26)?, Outcome varchar(16), CorrelationId varchar(64), DetailJson jsonb)` + immutable trigger.
- `ProvisioningLedger(IdempotencyKey varchar(128) PK, RequestFingerprint char(64), TenantId varchar(26), SubscriptionId varchar(26), ActorKind smallint, CreatedUtc)`.
> triggerها و plan seed را دستی بعد از `migrations add` اضافه کن. migration از context **اصلی** (`AnimoraServerDbContext`) تولید می‌شود؛ entity classها با configuration به مدل آن اضافه می‌شوند (vendor-global، `ExcludeFromMigrations:false`)؛ `AnimoraOperatorDbContext` فقط map می‌کند.

### Persistence (`Animora.Persistence.PostgreSql/Operator/` و `/Subscriptions/`)
- `Operator/AnimoraOperatorDbContext.cs` (namespace `Animora.Persistence.PostgreSql.Operator`): map جداول بالا + `Tenants` و `StaffMemberships` به‌صورت read-only (استثنا: `Tenants.Status` نوشتنی)؛ **بدون tenant filter**؛ **هرگز migrate نمی‌شود**؛ scoped register؛ فقط از `Operator/**`, `Subscriptions/**` workers/consumers و tests قابل resolve (تست `ServerSourceScanTests.OperatorContext_OnlyUsedInOperatorAndSubscriptionNamespaces`).
- Configurations: `Configurations/{Plan,Subscription(concurrency token=ConcurrencyStamp),BillingPeriod,SubscriptionEvent,OperatorAccount,OperatorAudit,ProvisioningLedger}Configuration.cs`.
- `Subscriptions/{ISubscriptionService,SubscriptionService,ISeatUsageReader,SeatUsageReader,ProvisioningEngine,TenantAdministration,SubscriptionOptions}.cs`.
- `TenantAdministration.SetStatusAsync(tenantId, status, reason, actor)` — تنها مسیر نوشتن `Tenant.Status` (می‌نویسد `SetStatus` + `OperatorAudit`).
- `SeatUsageReader` — تعداد seat فعال مصرف‌شده (شمارش StaffMembership فعال tenant).
- `ILicenseIssuanceScheduler { Task ScheduleAsync(string tenantId, string subscriptionId, CancellationToken ct) }` + `NoopLicenseIssuanceScheduler` (register با **TryAdd**). **S16 با Replace جایگزین می‌کند.** اینجا در namespace `Animora.Persistence.PostgreSql.Subscriptions` تعریف می‌شود.

### ProvisioningEngine (تنها مسیر — ARCH §13.4)
- امضا: `Task<Result<ProvisioningResult>> ProvisionAsync(ProvisioningRequest r, ActorRef actor, CancellationToken ct = default)`.
- `ProvisioningRequest(IdempotencyKey, TenantCode, DisplayName, LegalName?, Timezone?, OwnerUserName, OwnerDisplayName, OwnerInitialPassword?(null⇒generated,once), PlanCode, SeatLimitOverride?, StartTrial)`.
- `ActorRef(ActorKind Kind, string? ActorId)`.
- `ProvisioningResult(TenantId, TenantCode, SubscriptionId, OwnerUserId, OwnerTemporaryPassword?, WasReplay)`.
- الگوریتم در **یک تراکنش ExecuteAsync** (operator context):
  1. validate: `TenantCode ^[a-z0-9-]{3,32}$`؛ کدهای رزرو `admin|operator|api|sync|auth|files|health` رد؛ plan موجود و active.
  2. lookup `ProvisioningLedger` با `IdempotencyKey` — همان fingerprint ⇒ نتیجهٔ ذخیره‌شده با `WasReplay=true` (tenant دوم نساز)؛ fingerprint متفاوت ⇒ `PROVISIONING_KEY_CONFLICT`.
  3. `TenantCode` قبلاً استفاده ⇒ `TENANT_CODE_TAKEN`.
  4. `TenantProvisioner.CreateTenantWithOwnerAsync(...)` (شامل TenantFeedState).
  5. `Subscription.Start(plan, now)` (+ اولین BillingPeriod مگر trial) و event `subscription.created`.
  6. ردیف ledger.
  7. `ILicenseIssuanceScheduler.ScheduleAsync(tenantId, subscriptionId, ct)` (seam؛ Noop فعلاً).
  8. ردیف `OperatorAudit`/`SecurityAudit`.
  - Fingerprint = SHA-256 از canonical JSON درخواست **بدون** idempotency key و password. race arbiter = `ON CONFLICT` روی ledger PK (بازنده مسیر read را retry می‌کند).

### Operator identity
- `OperatorAccount` نه tenant User است نه TenantId دارد.
- اسکیم bearer دوم: `OperatorAuth.Scheme = "Animora.Operator"` via `AddBearerToken(...)` (opaque، access lifetime **30 min**، بدون refresh/sliding → re-login). Claims: `sub`(operator id), `ost`(security stamp), `role=operator`.
- `CoarsePolicies.OperatorOnly` = authenticated روی آن اسکیم **و** `ITenantContext.IsOperator`.
- `OperatorTenantContext : ITenantContext { TenantId=>null; IsOperator=>true }` — توسط `OperatorContextMiddleware` برای `/operator/*` ثبت (کنار slot TenantContextMiddleware؛ هر دو middleware **ویرایش: skip هر مسیر زیر `/operator`**).
- Lockout: 5 failure / 15 min (ثابت‌های Domain). password با `PasswordHasher<OperatorAccount>`. login failure همیشه `401 OPERATOR_CREDENTIALS_INVALID` (constant-time-ish با dummy verify).
- **Bootstrap:** در startup اگر `OperatorAccounts` خالی و env `Operator__BootstrapUserName` **و** `Operator__BootstrapPassword` هر دو ست باشند (هرگز JSON — `SecretGuard` گسترش یابد تا `Operator:BootstrapPassword` در فایل‌ها ممنوع) → یک account با `MustChangePassword=true` بساز و log `operator.bootstrap.created` (بدون secret). با `MustChangePassword` فقط `POST /operator/auth/change-password` مجاز.

### Operator API (`/operator`، rate-limit `operator`، اسکیم `Animora.Operator`، هر call یک `OperatorAudit` در همان تراکنش)
- `POST /operator/auth/login|logout|change-password`.
- `GET /operator/plans`, `POST /operator/plans`, `POST /operator/plans/{id}/deactivate`.
- `GET /operator/tenants?page&pageSize&status&q` (max page 200؛ projection `TenantListItem(Id, Code, DisplayName, TenantStatus, SubscriptionStatus, PeriodEndUtc, SeatLimit, SeatsUsed)`).
- `GET /operator/tenants/{id}` (+ subscription + last 20 events).
- `POST /operator/tenants` (create tenant+owner+subscription — **ProvisioningEngine**؛ header `Idempotency-Key`).
- `POST /operator/tenants/{id}/subscription/extend|reduce|suspend|reinstate|cancel|renew|seats|plan`.
- `GET /operator/tenants/{id}/subscription/events?afterSequence&limit≤200`.
- `POST /operator/tenants/{id}/owner/reset-password` (temp password یک‌بار return؛ rotate SecurityStamp owner؛ audited).
- هر mutation `If-Match: <ConcurrencyStamp>` می‌گیرد (`412` + `CONCURRENCY_CONFLICT` on mismatch).
- DTOها server-internal (`Animora.Server.Operator.Models`)، **نه** wire DTO؛ `WireContract.Version` بدون تغییر.

### Expiry processing
- `SubscriptionSweepWorker` (BackgroundService، هر **15 min**، تحت `IBackgroundAdmission`، ≤200 row/pass): select `Subscriptions WHERE Status IN (Trial,Active,GracePeriod) AND (CurrentPeriodEndUtc<=now OR GraceEndsUtc<=now OR CancelAtPeriodEnd AND CurrentPeriodEndUtc<=now)` و برای هر row یک پیام outbox `subscription.expiry` enqueue (key = subscription id + target period end ⇒ idempotent).
- `SubscriptionExpiryConsumer : IOutboxConsumer` — `Subscription.Tick`، اعمال `SubscriptionTenantStatusMap`، نوشتن event + `OperatorAudit(ActorKind.System)`. idempotent با S13 `ConsumedMessages`.
- الگوی worker را از `src/Animora.Server/Workers/*` کپی کن (BackgroundService + PeriodicTimer + WorkerHeartbeat + IBackgroundAdmission).

### Server wiring
- `src/Animora.Server/Operator/{OperatorAuth,OperatorContextMiddleware,OperatorTenantContext,OperatorAccountService,OperatorBootstrapper,OperatorEndpoints,Models/*}.cs`.
- `src/Animora.Server/Workers/SubscriptionSweepWorker.cs`، `src/Animora.Server/Subscriptions/SubscriptionExpiryConsumer.cs`.
- DI: `AddAnimoraSubscriptions()` + `AddAnimoraOperator()`.
- ویرایش: `TenantContextMiddleware`+`EntitlementMiddleware` (skip `/operator`)، `CoarsePolicies.OperatorOnly`، `SecretGuard` (+ operator bootstrap key)، `Program.cs` (slot operator middleware).
- readiness: هیچ probe جدید (جداول operator همان probe `database`).

### Contracts/schema افزوده S14
جداول `Plans, Subscriptions, BillingPeriods, SubscriptionEvents, OperatorAccounts, OperatorAudit, ProvisioningLedger`؛ endpointهای `/operator`؛ seamها `ProvisioningEngine, ISubscriptionService, ITenantAdministration, ISeatUsageReader, ILicenseIssuanceScheduler(+Noop)`؛ ErrorCodes +8؛ بدون wire DTO.

### Out of scope S14
- public sign-up / website intake → S15. license issuance/signing/fingerprint/grace → S16. operator web UI → S19. card/payment/invoice/MFA/email → خارج محصول.

### تست‌های S14 (pure — `tests/Animora.Server.Tests/Subscriptions/` و `/Operator/`)
`SubscriptionLifecycleTests.AllowedAndForbiddenTransitions_Table`, `SubscriptionTests.Extend_Reduce_Renew_Cancel_Rules`, `SubscriptionTests.Reduce_NeverBeforeNow_NeverBelowPeriodStart`, `SubscriptionTests.Renew_BillingPeriodSequence_GapFree`, `SubscriptionTests.EventSequence_GapFree_AppendOnly`, `SubscriptionTests.ChangeSeats_BelowUsage_Rejected`, `SubscriptionTests.Tick_TrialActiveToGraceToExpired_AndCancelAtPeriodEnd`, `SubscriptionTenantStatusMapTests.AllStatuses_Mapped`, `ProvisioningEngineTests.SameKeySameFingerprint_ReplaysWithoutSecondTenant`, `ProvisioningEngineTests.SameKeyDifferentFingerprint_Conflicts`, `ProvisioningEngineTests.ReservedOrTakenCode_Rejected`, `ProvisioningEngineTests.CallsLicenseSchedulerSeam_InsideTransaction`, `OperatorAuthTests.Login_WrongPasswordAndUnknownUser_SameError`, `OperatorAuthTests.Lockout_After5Failures`, `OperatorAuthTests.TenantToken_CannotCallOperator_And_OperatorToken_CannotCallTenantApi`, `OperatorAuthTests.MustChangePassword_BlocksOtherCalls`, `OperatorBootstrapTests.OnlyWhenEmpty_AndEnvOnly`, `SecretGuardTests.OperatorBootstrapPasswordInJson_Refused`, `OperatorEndpointsTests.IfMatchMismatch_412_ConcurrencyConflict`, `ServerSourceScanTests.OperatorContext_OnlyUsedInOperatorAndSubscriptionNamespaces`, `SubscriptionLayeringTests.ClientProjects_DoNotReferenceSubscriptions`.
> PG-gated (G07، در sandbox بدون `ANIMORA_PG_TEST_CONNECTION` skip می‌شوند — بنویس ولی اجرا الزامی نیست): `SubscriptionPersistenceTests.*` (CreateTenantWithSubscription_OneTransaction_RollsBackTogether, ConcurrentProvisionSameKey_OneTenant, SuspendedSubscription_BlocksTenantRequests_403TenantSuspended, EventsAndOperatorAudit_ImmutableTriggers, SweepAndConsumer_Idempotent_OnRedelivery).

### Acceptance S14 (mechanical)
1. `scripts/sandbox/build.sh full` = 0/0؛ `scripts/sandbox/run.sh` = `SMOKE RUN OK`؛ client sources بدون تغییر.
2. `grep -rn "Animora.Domain.Subscriptions" src/Animora.Client.Services src/Animora.Desktop src/Animora.Persistence.Sqlite src/Animora.Infrastructure.Local` خالی؛ `grep -rn "Stripe\|PayPal\|Zarinpal\|SmtpClient" src` خالی.
3. تست‌های pure بالا سبز.
4. `ls src/Animora.Persistence.PostgreSql/Migrations/Server | grep -c Subscription` ≥ 1؛ `grep -c "SUBSCRIPTION_NOT_FOUND\|SUBSCRIPTION_INVALID_TRANSITION\|SEAT_LIMIT_EXCEEDED\|PLAN_NOT_FOUND\|TENANT_CODE_TAKEN\|PROVISIONING_KEY_CONFLICT\|OPERATOR_CREDENTIALS_INVALID\|OPERATOR_LOCKED_OUT" src/Animora.Domain/Results/ErrorCodes.cs` = 8.

---

## ✅ تسک‌لیست اجرایی S14 (بعد از هر چک‌باکس → push)

- [x] **S14-T0 · آماده‌سازی branch.** روی `genspark_ai_developer` سوئیچ/بساز. اجرا: `scripts/git/push.sh -b genspark_ai_developer "chore(plan): S14-S16 execution plan"`.
- [x] **S14-T1 · ErrorCodes +8.** region جدید `#region Subscription & Operator (Phase S14)` در انتهای `src/Animora.Domain/Results/ErrorCodes.cs` با ۸ کد بالا. push: `feat(domain): S14 subscription/operator error codes (+8)`.
- [x] **S14-T2 · Domain Subscriptions.** پوشهٔ `src/Animora.Domain/Subscriptions/` با `SubscriptionStatus, ActorKind, Plan, Subscription, BillingPeriod, SubscriptionEvent, SubscriptionLifecycle, SubscriptionTenantStatusMap`. فقط pure. push: `feat(domain): S14 subscription aggregate, lifecycle table & tenant-status map`.
- [x] **S14-T3 · تست‌های Domain pure.** `SubscriptionLifecycleTests`, `SubscriptionTests` (همه موارد), `SubscriptionTenantStatusMapTests` + `SubscriptionLayeringTests.ClientProjects_DoNotReferenceSubscriptions`. اجرای تست بنا به دستور کاربر انجام نشد (فقط سورس تست نوشته شد). push: `test(domain): S14 subscription lifecycle & rules`.
- [x] **S14-T4 · EnqueueAsync در IServerOutbox.** متد عمومی enqueue به `Outbox/IServerOutbox.cs`+`ServerOutbox.cs` + ثابت `OutboxMessageTypes.LicenseIssue="license.issue"`. push: `feat(persistence): generic outbox enqueue seam + license.issue type`.
- [x] **S14-T5 · Persistence context + configurations + services.** `Operator/AnimoraOperatorDbContext.cs`، `Configurations/*Configuration.cs` (۷ تا)، `Subscriptions/{ISubscriptionService,SubscriptionService,ISeatUsageReader,SeatUsageReader,ProvisioningEngine,TenantAdministration,SubscriptionOptions}.cs`، `ILicenseIssuanceScheduler`+`NoopLicenseIssuanceScheduler`. push: `feat(persistence): S14 operator context, subscription services & provisioning engine`.
- [x] **S14-T6 · Migration 112_Subscription.** مایگریشن و snapshot مدل اصلی دستی نوشته شدند (بدون اجرای EF scaffolding/بیلد) → `112_Subscription`، دستی: ۲ immutable trigger + plan seed، `ServerSchemaVersion.ExpectedHead`. push: `feat(persistence): S14 migration 112_Subscription (tables, triggers, plan seed)`.
- [x] **S14-T7 · Server operator auth + API.** `src/Animora.Server/Operator/{OperatorAuth,OperatorContextMiddleware,OperatorTenantContext,OperatorAccountService,OperatorBootstrapper,OperatorEndpoints,Models/*}.cs` + DI `AddAnimoraOperator()`+`AddAnimoraSubscriptions()` + ویرایش `CoarsePolicies.OperatorOnly`, `TenantContextMiddleware/EntitlementMiddleware` (skip `/operator`), `SecretGuard`, `Program.cs`. push: `feat(server): S14 operator identity, auth scheme & operator API`.
- [x] **S14-T8 · Worker + consumer.** `Workers/SubscriptionSweepWorker.cs` + `Subscriptions/SubscriptionExpiryConsumer.cs` + ثبت heartbeat/consumer. push: `feat(server): S14 subscription sweep worker & expiry consumer`.
- [x] **S14-T9 · تست‌های Server.** `Operator/*` + `Subscriptions/*` (pure list بالا) + `ServerSourceScanTests.OperatorContext_OnlyUsedInOperatorAndSubscriptionNamespaces` + `SecretGuardTests.OperatorBootstrapPasswordInJson_Refused` + PG-gated نوشته (اجرا اختیاری). push: `test(server): S14 operator auth, endpoints & provisioning parity`.
- [x] **S14-T10 · Build/run gate + PROGRESS.** به دستور صریح کاربر هیچ build/test/smoke اجرا نشد؛ وضعیت authored و gateهای اجرا‌نشده در PROGRESS ثبت شد. در `backend-roadmap/PROGRESS.md`: ردیف S14 = ✅ Done + تاریخ + خلاصه، Next phase → S15، یک `### S14` در Repo state log، deviation log در صورت نیاز. push: `feat(server): S14 subscription context, operator API & provisioning engine`.

---

# ════════════════ FAZ S15 — Self-service Onboarding Intake ════════════════

**هدف:** دریافت رویداد «ثبت‌نام + خرید» از وب‌سایت فروشنده به‌صورت intent ناهمگام، idempotent و authorized (ARCH §13.4): `POST /provisioning/intents` امضاشده (202) durably ثبت می‌شود و یک worker آن را به `ProvisioningEngine` (بدون تغییر S14) با `ActorKind.WebsiteIntent` می‌دهد — یک engine، دو caller. وب‌سایت با `GET /provisioning/intents/{key}` نتیجه را poll می‌کند. بدون payment/email/وابستگی runtime به وب‌سایت.

**وابستگی:** S14 · **بعدی:** S16 · **Satisfies:** R-B30, R-B36, R-B29.

### کدهای خطای جدید S15 (region "Provisioning"، دقیقاً +2)
```
ProvisioningSignatureInvalid ("PROVISIONING_SIGNATURE_INVALID", Forbidden)
ProvisioningIntentNotFound   ("PROVISIONING_INTENT_NOT_FOUND",  NotFound)
```
> کدهای `TenantCodeTaken, ProvisioningKeyConflict, PlanNotFound, ValidationFailed` از S14/قبل موجودند — reuse.

### Website service identity
- جدول `WebsiteClients(Id varchar(26) PK, KeyId varchar(32) UNIQUE, ProtectedSecret bytea (Data Protection purpose "Animora.Provisioning.Client.v1"), Status smallint (Active=0,Revoked=1), CreatedUtc, LastUsedUtc?, Label varchar(100))`.
- secret فقط توسط operator ساخته/rotate می‌شود: `POST /operator/website-clients` (secret یک‌بار return؛ S14 operator auth + audit) و `POST /operator/website-clients/{id}/revoke`. بدون env bootstrap. حداکثر **3** client فعال.

### Request authentication = HMAC-SHA256 request signing
- `System.Security.Cryptography.HMACSHA256` (بدون crypto سفارشی، بدون JWT/IdP).
- headers: `X-Animora-Key`(KeyId), `X-Animora-Timestamp`(unix sec), `X-Animora-Signature`(hex از `HMAC(secret, METHOD + "\n" + path + "\n" + timestamp + "\n" + sha256hex(body))`), `Idempotency-Key`.
- verify با `CryptographicOperations.FixedTimeEquals`؛ skew ≤ **5 min**؛ replay `(KeyId, signature)` داخل پنجره با `ReplayWindow` in-memory bounded (size 4096، تک-host — documented؛ dedupe دائمی = idempotency key).
- failure ⇒ `401 PROVISIONING_SIGNATURE_INVALID` (بدنهٔ یکسان برای key ناشناس/امضای بد/skew) + `OperatorAudit`(outcome `denied`، بدون secret).
- transport = HTTPS (internal CA)؛ HTTP ساده خارج از Development رد.

### Intent record (`ProvisioningIntents`)
`Id varchar(26) PK, IdempotencyKey varchar(128) UNIQUE, ClientId, RequestFingerprint char(64), PayloadProtected bytea (request JSON شامل پسورد اولیهٔ اختیاری، protect با purpose "Animora.Provisioning.Intent.v1"؛ plaintext پسورد هرگز log/clear نشود), Status smallint (Received=0,Processing=1,Completed=2,Failed=3,Dead=4), Attempts int, NextAttemptUtc, ClaimToken varchar(26)?, ClaimedUntilUtc?, ResultJson jsonb? (tenant id/code/subscription id — بدون secret), ProtectedTempPassword bytea? + TempPasswordExpiresUtc? (72h، wipe در اولین read موفق یا expiry), ErrorCode varchar(64)?, ReceivedUtc, CompletedUtc?` + index `IX_ProvisioningIntents_Status_NextAttemptUtc`.

### Endpoints (anonymous scheme، signature-authenticated، rate-limit `operator`، body ≤ 8 KiB، route `ServerRoutes.Provisioning="/provisioning"` اینجا اضافه)
- `POST /provisioning/intents` → فقط shape validate (engine خودش business validate)؛ insert با `INSERT … ON CONFLICT (IdempotencyKey) DO NOTHING`؛ conflict: همان fingerprint ⇒ **202 + id موجود** (replay)، fingerprint متفاوت ⇒ **409 PROVISIONING_KEY_CONFLICT**؛ return `202 { intentId, status }`.
- `GET /provisioning/intents/{idempotencyKey}` → `{ status, tenantCode?, tenantId?, subscriptionId?, ownerTemporaryPassword? (once), errorCode? }`؛ temp password فقط در اولین read `Completed` و سپس wipe؛ owner `MustChangePassword=true`. client فقط intent خودش را می‌خواند.

### Worker `ProvisioningIntentWorker`
BackgroundService، poll 2s با idle-backoff تا 15s، تحت `IBackgroundAdmission`، claim ≤5 با `FOR UPDATE SKIP LOCKED` + 60s fenced lease (دقیقاً مثل S13): decrypt payload → `ProvisioningEngine.ProvisionAsync(request, new ActorRef(ActorKind.WebsiteIntent, clientId))`. موفق (شامل `WasReplay`) ⇒ `Completed` + result؛ failure **دائمی** (`TENANT_CODE_TAKEN, PLAN_NOT_FOUND, VALIDATION_FAILED, PROVISIONING_KEY_CONFLICT`) ⇒ `Failed` با کد (بدون retry)؛ transient ⇒ retry با S13 `ServerRetryPolicy` تا 8 بار ⇒ `Dead` + audit. چون engine روی همان key idempotent است، crash بین commit engine و update intent امن re-run می‌شود (`WasReplay=true`).

### Retention & operator visibility
- `ProvisioningIntentRetentionWorker` (daily، تحت admission، ≤500 row/pass): `PayloadProtected` بعد از **7 روز** null، ردیف 400 روز نگه‌داشته.
- `GET /operator/provisioning/intents?status&page&pageSize` (max 200) + `POST /operator/provisioning/intents/{id}/retry` (Dead/Failed-transient → Received) — read model برای S19.

### Server wiring S15
- `src/Animora.Server/Provisioning/{RequestSignatureVerifier,ReplayWindow,WebsiteClientService,ProvisioningEndpoints,ProvisioningIntentWorker,ProvisioningIntentRetentionWorker}.cs`.
- operator endpoints: `Operator/WebsiteClientEndpoints.cs`, `Operator/ProvisioningIntentEndpoints.cs`.
- DI `AddAnimoraProvisioning()` + heartbeat registrations + `Program.cs` mapping (بیرون از tenant/entitlement middleware مثل `/operator`).
- Persistence: `Provisioning/{WebsiteClient,ProvisioningIntent}.cs` + configurations + `IProvisioningIntentStore` (insert-on-conflict، claim SQL، fenced complete/fail، wipe-password)؛ map در `AnimoraOperatorDbContext`؛ migration `113_ProvisioningIntents`.

### Out of scope S15
- business validation/creation → S14 (engine بدون تغییر). license issuance → S16. وب‌سایت/payment/receipt/email/SMS → vendor-side. operator web UI → S19. first login/device activation → G06.

### تست‌های S15 (pure — `tests/Animora.Server.Tests/Provisioning/`)
`RequestSignatureVerifierTests.ValidSignature_Accepted`, `…TamperedBody_Rejected`, `…SkewBeyond5Min_Rejected`, `…UnknownKeyAndBadSignature_SameError`, `…ReplayInsideWindow_Rejected`, `ReplayWindowTests.BoundedAndExpires`, `ProvisioningEndpointsTests.Post_ReturnsAccepted_AndDuplicateSameFingerprint_ReplaysSameId`, `…DuplicateDifferentFingerprint_409`, `…OversizeBody_413`, `…ClientCannotReadOthersIntent`, `ProvisioningEndpointsTests.TempPassword_ReturnedOnceThenWiped`, `ProvisioningIntentWorkerTests.PermanentFailure_NoRetry`, `…TransientFailure_RetriesWithJitter_ThenDead`, `…CrashAfterEngineCommit_RerunIsReplayNoSecondTenant`, `…UsesWebsiteIntentActor`, `ProvisioningParityTests.WebsiteAndOperatorPaths_ProduceSameState`, `WebsiteClientServiceTests.SecretShownOnce_MaxThreeActive_Revocation`, `ProvisioningSecretsTests.PayloadAndPassword_NeverInLogsOrAudit`.
> PG-gated: `ProvisioningIntentStoreTests.*` (InsertOnConflict_Race_OneRow, TwoWorkers_NeverClaimSameIntent, ExpiredLease_Reclaimable_FencedCompletionRejected), `ProvisioningRetentionTests.PayloadWiped_After7Days`.

### Acceptance S15 (mechanical)
1. build full 0/0؛ run SMOKE RUN OK؛ client بدون تغییر.
2. `grep -rn "ProvisioningEngine" src/Animora.Server/Provisioning src/Animora.Server/Operator` = دقیقاً یک `ProvisionAsync` call site per caller type (worker + operator create-tenant)؛ `grep -rn "Tenant.Create(\|Subscription.Start(" src/Animora.Server` خالی.
3. تست‌های pure بالا سبز.
4. `ls src/Animora.Persistence.PostgreSql/Migrations/Server | grep -c ProvisioningIntents` ≥ 1؛ `grep -c "PROVISIONING_SIGNATURE_INVALID\|PROVISIONING_INTENT_NOT_FOUND" src/Animora.Domain/Results/ErrorCodes.cs` = 2.

## ✅ تسک‌لیست اجرایی S15 (بعد از هر چک‌باکس → push)

- [x] **S15-T1 · ErrorCodes +2 + route.** region `#region Provisioning (Phase S15)` با ۲ کد + `ServerRoutes.Provisioning="/provisioning"`. push: `feat(server): S15 provisioning error codes (+2) & route`.
- [x] **S15-T2 · Persistence entities + store + migration.** `Provisioning/{WebsiteClient,ProvisioningIntent}.cs` + configurations + `IProvisioningIntentStore`/impl + map در `AnimoraOperatorDbContext` + migration `113_ProvisioningIntents` + `ServerSchemaVersion.ExpectedHead`. push: `feat(persistence): S15 website clients, provisioning intents store & migration 113`.
- [x] **S15-T3 · Signature verifier + replay window + client service.** `Provisioning/{RequestSignatureVerifier,ReplayWindow,WebsiteClientService}.cs`. push: `feat(server): S15 HMAC request signing, replay window & website client service`.
- [x] **S15-T4 · Endpoints + workers + operator views.** `Provisioning/{ProvisioningEndpoints,ProvisioningIntentWorker,ProvisioningIntentRetentionWorker}.cs` + `Operator/{WebsiteClientEndpoints,ProvisioningIntentEndpoints}.cs` + DI `AddAnimoraProvisioning()` + `Program.cs`. push: `feat(server): S15 provisioning intake endpoints, workers & operator views`.
- [x] **S15-T5 · تست‌ها.** کل pure list بالا + PG-gated نوشته. سورس regression و PG-gated نوشته شد؛ طبق دستور کاربر هیچ تستی اجرا نشد. push: `test(server): S15 signature, intent worker & website/operator parity`.
- [x] **S15-T6 · Build/run gate + PROGRESS.** طبق دستور کاربر build/test/smoke اجرا نشد؛ وضعیت authored و gateهای اجرا‌نشده ثبت شد. PROGRESS: S15 ✅ Done، Next → S16، `### S15` entry. push: `feat(server): S15 self-service onboarding intake (signed, idempotent, async provisioning intent)`.

---

# ════════════════ FAZ S16 — Licensing & Anti-piracy ════════════════

**هدف:** Licensing = منبع حقیقت runtime entitlement، جدا از Subscription (R-B30): envelope امضای جداشده (`Base64Url(payload).Base64Url(signature).KeyId`، ECDSA P-256، rotation-aware) bound به یک installation؛ کتابخانهٔ verification قابل‌استفادهٔ client (`Animora.Server.Licensing/Verification`: parse, verify, high-water clock, policy matrix با offline grace, device fingerprint, sealed-cache port)؛ issuance سمت سرور (activate/renew، `LicenseGrants` sync با Subscription از مسیر outbox `license.issue`، revocation، key rotation)؛ و **ServerEntitlementGate واقعی** جایگزین allow-all S05. Threat model صادقانه (ARCH §13.2): امضا = authenticity + tamper-evidence، نه مرز نفوذناپذیر؛ enforcement واقعی online = این gate.

**وابستگی:** S15 · **بعدی:** S17 · **Satisfies:** R-B06, R-B29, R-B30, R-B36.

### کدهای خطای جدید S16 (region "Licensing"، دقیقاً +8)
```
LicenseInvalid           ("LICENSE_INVALID",             Entitlement)
LicenseKeyUnknown        ("LICENSE_KEY_UNKNOWN",         Entitlement)
LicenseExpired           ("LICENSE_EXPIRED",             Entitlement)
LicenseRevoked           ("LICENSE_REVOKED",             Entitlement)
LicenseDeviceMismatch    ("LICENSE_DEVICE_MISMATCH",     Forbidden)
LicenseDeviceLimitReached("LICENSE_DEVICE_LIMIT_REACHED",Conflict)
LicenseClockRollback     ("LICENSE_CLOCK_ROLLBACK",      Entitlement)
LicenseNotGranted        ("LICENSE_NOT_GRANTED",         Entitlement)
```

### پروژهٔ جدید `src/Animora.Server.Licensing`
- `Microsoft.NET.Sdk`، **بدون** `FrameworkReference Microsoft.AspNetCore.App`، **بدون** Npgsql/EF package؛ refs: `Animora.Domain`, `Animora.Contracts.Wire`, `Animora.Application`.
- دو پوشه = دو نیمه:
  - `Verification/` (pure: فقط `System.*`, Domain, Contracts.Wire — تست source-scan ممنوع می‌کند `using Animora.Application`, `Microsoft.AspNetCore`, `Npgsql`, `Microsoft.EntityFrameworkCore` در `Verification/**`).
  - `Issuance/` (می‌تواند `CommandFingerprint.CanonicalJson` از Application استفاده کند).
- `Animora.Server` این پروژه را ref می‌کند. **G06 تنها ref مجاز client به هر `Animora.Server.*`** — `LayeringRulesTests` (S01، در `tests/Animora.Domain.Tests/LayeringRulesTests.cs`) ویرایش: دقیقاً `Animora.Server.Licensing` را برای `Client.Services`/`Desktop`/`Infrastructure.Local` whitelist و بقیهٔ server project ممنوع؛ تست assembly-closure ثابت کند transitive refs کتابخانه هیچ Npgsql/ASP.NET Core ندارد.
- `InternalsVisibleTo Animora.Server.Tests`؛ به `Animora.sln` اضافه.

### Envelope (`Verification/LicenseEnvelope`)
- `token = Base64Url(payloadUtf8) + "." + Base64Url(signature) + "." + KeyId`.
- `Parse(string) → Result<ParsedLicense(PayloadBytes, Signature, KeyId)>` (max 4 KiB، دقیقاً 3 segment، strict base64url).
- signature = ECDSA P-256 / SHA-256، IEEE P1363 fixed-size (64 bytes)، روی **بایت‌های دقیق payload** (verifier هرگز re-canonicalise نمی‌کند).
- `LicenseVerifier.Verify(ParsedLicense, ILicenseKeySet keys) → Result<LicensePayload>`: KeyId ناشناس ⇒ `LICENSE_KEY_UNKNOWN`؛ امضای بد/payload خراب ⇒ `LICENSE_INVALID`؛ key خارج از پنجرهٔ `[NotBeforeUtc, NotAfterUtc]` اگر `issuedUtc` لایسنس داخل آن بوده **هنوز معتبر** (rotation-aware)؛ constant-time ذاتی `ECDsa.VerifyData`.

### Payload (`LicensePayload`، canonical JSON، camelCase، sorted keys، UTC `O`)
`v=1, licenseId(ULID), tenantId, tenantCode, subscriptionId, planCode, seatLimit, maxDevices, installationIdHash(64 hex — تنها installation), deviceId, sequence(per-device monotonic long؛ client لایسنس با sequence کمتر از بالاترین ذخیره‌شده را رد می‌کند — blob-rollback guard), issuedUtc, notBeforeUtc(=issuedUtc−5min skew), expiresUtc, offlineGraceUntilUtc(=expiresUtc+OfflineGraceDays default **14**), features(string[]، reserved، فعلاً خالی)`. `v` آیندهٔ ناشناس ⇒ `LICENSE_INVALID` (fail closed). field set افزایشی-only در v1.

### Policy matrix (`LicensePolicy.Evaluate(LicensePayload p, DateTimeOffset nowUtc, HighWaterClock clock, string localInstallationIdHash) → LicenseVerdict(State, Reason, RemainingUntilExpiry?, RemainingGrace?)`، pure، table-tested، fail-closed order)
1. `localInstallationIdHash ≠ p.installationIdHash` ⇒ `Locked / WRONG_INSTALLATION`.
2. effective time `t = max(nowUtc, clock.HighWaterUtc)` (**A-RULE 011** — ساعت عقب هرگز لایسنس را تمدید نمی‌کند)؛ اگر `nowUtc < clock.HighWaterUtc − ClockTolerance(5min)` ⇒ `ClockRollbackDetected=true`.
3. `t < p.notBeforeUtc` ⇒ `Locked / NOT_YET_VALID`.
4. `t ≤ p.expiresUtc` ⇒ `Valid` — اما اگر rollback بیش از **24h** ⇒ `ReadOnly / CLOCK_ROLLBACK` تا refresh آنلاین.
5. `t ≤ p.offlineGraceUntilUtc` ⇒ `Grace`.
6. `t ≤ p.offlineGraceUntilUtc + ReadOnlyWindowDays(60)` ⇒ `ReadOnly / EXPIRED`.
7. فراتر ⇒ `Locked / EXPIRED`. هر failure verifier ⇒ `Locked / <ErrorCode>`.
- `CapabilityGate.IsAllowed(LicenseState, RiskLevel)`: `Valid`→همه؛ `Grace`→Low,Normal,High (Critical آنلاین-only)؛ `ReadOnly`→فقط read؛ `Locked`→هیچ جز UI activation/status. wiring در `IEntitlementGate` = G06.

### سایر pure types (`Verification/`)
- `HighWaterClock` (record + `Advance(DateTimeOffset nowUtc) → HighWaterClock`): `HighWaterUtc` فقط افزایش؛ advance با read محلی **و** `serverTimeUtc` هر `LicenseResponse` (serverTime کمتر از high-water نادیده). persistence = sealed cache (G06).
- `DeviceFingerprint.Compute(IReadOnlyList<string> stableComponents) → string` (pure): SHA-256 hex (lower, 64) روی `"animora.installation.v1" + 0x1F + هر component trim/normalise، ordinal-sorted، joined 0x1F`؛ ورودی خالی/تکراری ⇒ `ArgumentException`. componentهای خام هرگز machine را ترک نمی‌کنند؛ فقط hash به عنوان `DeviceInstallationIdHash` (S04). جمع‌آوری component = G06.
- Sealed entitlement cache (contract-only اینجا): `SealedEntitlement(string LicenseToken, long HighWaterUnixMs, long HighestSequenceSeen, string TenantId, string InstallationIdHash, long WrittenUnixMs)` + port `ISealedEntitlementStore { Task<Result<SealedEntitlement?>> ReadAsync(ct); Task<Result> WriteAsync(SealedEntitlement e, ct); }`. impl = G06. format + invariants (write فقط forward؛ هرگز HighWaterUnixMs/HighestSequenceSeen را پایین نیار) + تابع pure `SealedEntitlementMerge.Merge(existing, incoming)` + تست اینجا.
- `Base64Url` helper.

### Issuance (`Issuance/`)
- `ILicenseSigner` + `EcdsaLicenseSigner` (key `Purpose='license'`، جدیدترین non-retired)، `LicenseCanonicalPayload`, `LicensingOptions`.

### Wire DTOs (`src/Animora.Contracts.Wire/Licensing/*.cs`، additive، **بدون** bump `WireContract.Version`)
`LicenseRequest(installationIdHash, currentSequence?)`, `LicenseResponse(token, expiresUtc, offlineGraceUntilUtc, sequence, serverTimeUtc)`, `LicenseStatusResponse(grantStatus, validUntilUtc?, planCode?, seatLimit?, maxDevices?, activeLicenses, serverTimeUtc)`, `LicensePublicKeySet`, `LicensePublicKey`.

### Grants (`LicenseGrants`، vendor-global، یک row per tenant)
منبع denormalised واحد برای issuance و gate، مشتق از Subscription توسط consumer `license.issue`:
`TenantId PK, SubscriptionId, PlanCode, SeatLimit, MaxDevices(=SeatLimit×LicensingOptions.DevicesPerSeat default **2**), ValidUntilUtc(=Subscription.GraceEndsUtc ?? CurrentPeriodEndUtc وقتی status∈Trial/Active/GracePeriod), Status(Active=0,Suspended=1,Revoked=2؛ Suspended/Expired⇒Suspended، Cancelled⇒Revoked), Sequence bigint(grant version), SourceEventSequence int, UpdatedUtc`. upsert **monotonic** روی `SourceEventSequence` (پیام قدیمی‌تر grant جدیدتر را بازنویسی نمی‌کند) ⇒ idempotent.

### Grant sync path
- `OutboxLicenseIssuanceScheduler : ILicenseIssuanceScheduler` جایگزین Noop S14 (**Replace**): `license.issue` (payload: tenantId, subscriptionId, sourceEventSequence) از `IServerOutbox` **در تراکنش caller** enqueue.
- S16 ویرایش می‌کند S14 `SubscriptionService` را تا **هر** mutation موفق lifecycle (extend/reduce/suspend/reinstate/cancel/renew/seats/plan و expiry consumer) scheduler را صدا بزند — provisioning S14 از قبل می‌زند.
- `LicenseIssueConsumer` / `LicenseRecheckConsumer : IOutboxConsumer` (هر دو `LicenseGrantProjector.ProjectAsync(tenantId)` را صدا می‌زنند: subscription را re-read و grant را upsert)؛ `license.recheck` = operator-triggered drift repair. consumer برای tenant حذف‌شده ⇒ ack.

### Licences table (`Licenses`، tenant-scoped، غیر-SyncableEntity، filter `"Tenant"` + `TenantId` اول هر index مثل S04 RefreshTokens)
`TenantId, Id varchar(26), DeviceId, InstallationIdHash char(64), Sequence bigint, IssuedUtc, ExpiresUtc, OfflineGraceUntilUtc, KeyId varchar(26), PayloadSha256 char(64) (token ذخیره **نمی‌شود**), Status smallint(Active=0,Superseded=1,Revoked=2), RevokedUtc?, RevokedByActor varchar(26)?, RevokeReason varchar(200)?, PK(TenantId,Id)`؛ **unique partial index** `UX_Licenses_Tenant_Device_Active (TenantId,DeviceId) WHERE Status=0` (یک لایسنس live per device)؛ `IX_Licenses_TenantId_Status`.

### Issuance (`LicenseIssuer.IssueAsync(IssueKind kind, ...)`، یک تراکنش)
precondition به ترتیب: grant موجود و `Status=Active` و `now ≤ ValidUntilUtc` (وگرنه `ENTITLEMENT_REQUIRED`/`LICENSE_REVOKED`)؛ device caller (از claim **`did` که از store re-read می‌شود**، نه body) = `Active`؛ `installationIdHash` درخواست = `Devices.InstallationIdHash` (وگرنه `LICENSE_DEVICE_MISMATCH` + `SecurityAudit license.device_mismatch`)؛ device-count در **اولین** activation: لایسنس‌های active `< MaxDevices` (وگرنه `LICENSE_DEVICE_LIMIT_REACHED`)؛ سپس supersede لایسنس Active فعلی device (`Status=Superseded`)، insert جدید با `Sequence=previous+1`، `ExpiresUtc=min(grant.ValidUntilUtc, now+MaxIssueTtlDays)` (**MaxIssueTtlDays=40**)، sign با `ILicenseSigner`، audit `license.issued` با licenseId/sequence/keyId (هرگز token). `activate` و `renew` این متد را share می‌کنند (IssueKind فقط label audit).

### Keys
- `LicenseKeyBootstrapper` (startup بعد از migrations check): یک key non-retired `Purpose='license'` تضمین (ECDSA P-256 از `SigningKeyStore` S05، private Data-Protection، هرگز process را ترک نمی‌کند).
- `POST /operator/licensing/keys/rotate`: key بعدی را می‌سازد، `NotAfterUtc` قبلی = `now + MaxIssueTtlDays + OfflineGraceDays + ReadOnlyWindowDays` (**120 روز**) تا هر لایسنس امضاشده verifiable بماند.
- `GET /licensing/keys` (anonymous، rate-limit `auth`) → key set عمومی (`LicensePublicKeySet`).

### Server entitlement gate (`ServerEntitlementGate : IServerEntitlementGate`، register **Replace**)
فقط برای routeهای **غیر-exempt** evaluate (`/health/*`, `/auth/*`, `/licensing/*`, `/operator/*`, `/provisioning/*`, `/admin/*` exempt)؛ وگرنه allow اگر: (a) `LicenseGrants` tenant درخواست `Active` و `now ≤ ValidUntilUtc`، **و** (b) ردیف `Active` `Licenses` برای `did` درخواست وجود دارد (device بدون activation یا لایسنس revoked ⇒ `403 ENTITLEMENT_REQUIRED` با `detail="license_not_active"`؛ revoked ⇒ `LICENSE_REVOKED`). هر دو read = PK/partial-index lookup از context read-only باریک `AnimoraEntitlementReadContext` (بدون tenant filter، پارامتر `TenantId` صریح، `AsNoTracking`، map فقط به `LicenseGrants`+`Licenses`؛ فقط از `Licensing/**` — به whitelist source-scan S14 اضافه). بدون cache cross-request (A-RULE 033؛ revocation فوری). تست order: unauthenticated→401؛ no permission→403 `PERMISSION_DENIED`؛ سپس entitlement.

### Endpoints (`ServerRoutes.Licensing="/licensing"`، authenticated tenant principal، device `Active`، rate-limit `auth`)
- `POST /licensing/activate`, `POST /licensing/renew` (body `LicenseRequest` → `LicenseResponse`).
- `GET /licensing/status` → `LicenseStatusResponse`.
- `GET /licensing/keys`.
- Operator: `GET /operator/tenants/{id}/licenses`, `POST /operator/tenants/{id}/licenses/{licenseId}/revoke` (body reason؛ Revoked، audited)، `POST /operator/tenants/{id}/licenses/recheck`، `POST /operator/licensing/keys/rotate`.

### Migration (`114_Licensing`)
`LicenseGrants`, `Licenses`, indexes بالا، **backfill** `INSERT INTO "LicenseGrants" SELECT … FROM "Subscriptions"` (tenantهای قبل از این فاز قفل نشوند). partial unique index و backfill را دستی اضافه کن. `ServerSchemaVersion.ExpectedHead` update.

### Out of scope S16
- client wiring (جایگزین `AllowAllEntitlementGate`، activation UX، credential-mirror، DPAPI `ISealedEntitlementStore`، fingerprint component، high-water persistence، key-set embedding) → G06 (G09 برای embed). operator web screens → S19. subscription lifecycle → S14. hardware-token/obfuscation/remote-kill → ارائه نمی‌شود.

### تست‌های S16 (pure — `tests/Animora.Server.Tests/Licensing/`)
`LicenseEnvelopeTests.RoundTrip_AndStrictParsing`, `LicenseVerifierTests.ValidSignature_Accepted`, `…TamperedPayload_Rejected`, `…UnknownKeyId_Rejected`, `…RotatedKey_StillVerifiesOlderLicence`, `LicensePolicyTests.Matrix_ValidGraceReadOnlyLocked_Table`, `LicensePolicyTests.WrongInstallation_Locked`, `LicensePolicyTests.BackwardClock_NeverExtends_AndRollbackOver24h_ReadOnly`, `LicensePolicyTests.FailClosed_OnAnyVerifierFailure`, `CapabilityGateTests.RiskByState_Table`, `HighWaterClockTests.OnlyMovesForward_ServerTimeNeverLowers`, `DeviceFingerprintTests.StableOrderIndependent_RawNeverExposed`, `SealedEntitlementMergeTests.NeverLowersHighWaterOrSequence`, `LicenseIssuerTests.BoundToDeviceHash_FromStore_NotBody`, `…SupersedesPrevious_SequenceMonotonic`, `…TtlCappedAt40Days_AndGrantValidUntil`, `…DeviceLimit_Enforced`, `…RevokedGrant_Denied`, `LicenseGrantProjectorTests.OlderSourceEvent_NeverOverwritesNewer`, `LicenseGrantProjectorTests.StatusMap_SuspendedExpiredCancelled`, `ServerEntitlementGateTests.ExemptRoutes_And_UnactivatedDevice_403_And_Order_PermissionBeforeEntitlement`, `LicenseKeyTests.Bootstrap_CreatesOneKey_RotationKeepsOldVerifiable120Days`, `LicensePayloadTests.CanonicalJson_ParityWithCommandFingerprintCanonicalizer`, `LicensingLayeringTests.VerificationHalf_ClosureHasNoServerAssemblies`, `LicensingLayeringTests.OnlyLicensingProject_WhitelistedForClients`, `LicenseSecretsTests.TokenAndPrivateKey_NeverLoggedOrAudited`.
> PG-gated: `LicensePersistenceTests.*` (ActivateRenew_OneLiveLicencePerDevice_PartialUnique, ConcurrentRenew_NoDuplicateLive, GrantBackfill_ForPreExistingTenants, RevokedLicence_BlocksCommandsAt_ServerGate, ConsumerRedelivery_Idempotent, CrossTenant_CannotReadOrRevoke).

### Acceptance S16 (mechanical)
1. build full 0/0؛ run SMOKE RUN OK؛ هیچ فایل زیر `Client.Services`/`Desktop`/`Persistence.Sqlite`/`Infrastructure.Local` تغییر نکرد.
2. `grep -rn "Npgsql\|Microsoft.AspNetCore\|Microsoft.EntityFrameworkCore" src/Animora.Server.Licensing` خالی؛ `grep -rn "using Animora.Application" src/Animora.Server.Licensing/Verification` خالی؛ `grep -rn "ECDsa\|HMAC\|new Aes" src --include=*.cs -l` فقط S05 signer، `Licensing/Issuance`، `Licensing/Verification` و HMAC verifier S15 را لیست کند.
3. تست‌های pure بالا سبز.
4. `ls src/Animora.Persistence.PostgreSql/Migrations/Server | grep -c Licensing` ≥ 1؛ `grep -c "LICENSE_INVALID\|LICENSE_KEY_UNKNOWN\|LICENSE_EXPIRED\|LICENSE_REVOKED\|LICENSE_DEVICE_MISMATCH\|LICENSE_DEVICE_LIMIT_REACHED\|LICENSE_CLOCK_ROLLBACK\|LICENSE_NOT_GRANTED" src/Animora.Domain/Results/ErrorCodes.cs` = 8.

## ✅ تسک‌لیست اجرایی S16 (بعد از هر چک‌باکس → push)

- [x] **S16-T1 · ErrorCodes +8 + route + outbox type.** region `#region Licensing (Phase S16)` با ۸ کد + `ServerRoutes.Licensing="/licensing"`. (`license.issue` در S14-T4 اضافه شد.) push: `feat(server): S16 licensing error codes (+8) & route`.
- [ ] **S16-T2 · پروژهٔ Animora.Server.Licensing (Verification half).** csproj + refs + `InternalsVisibleTo` + افزودن به sln؛ `Verification/{LicensePayload,LicenseEnvelope,LicenseVerifier,ILicenseKeySet,LicensePublicKey,HighWaterClock,LicenseVerdict,LicensePolicy,CapabilityGate,DeviceFingerprint,SealedEntitlement,ISealedEntitlementStore,SealedEntitlementMerge,Base64Url}.cs`. push: `feat(licensing): S16 verification library (envelope, verifier, policy, fingerprint)`.
- [ ] **S16-T3 · Issuance half + Wire DTOs.** `Issuance/{ILicenseSigner,EcdsaLicenseSigner,LicenseCanonicalPayload,LicensingOptions}.cs` + `src/Animora.Contracts.Wire/Licensing/*.cs`. push: `feat(licensing): S16 issuance signer, canonical payload & wire DTOs`.
- [ ] **S16-T4 · تست‌های pure verification.** envelope/verifier/policy/capability/highwater/fingerprint/merge/canonical-parity + `LicensingLayeringTests` + ویرایش `LayeringRulesTests` whitelist. اجرا `scripts/sandbox/test.sh Licens`. push: `test(licensing): S16 verification matrix, fingerprint & layering`.
- [ ] **S16-T5 · Persistence grants/licences/projector/issuer/read-context + migration.** `Licensing/{LicenseGrant,LicenseRecord,LicenseGrantProjector,LicenseIssuer,AnimoraEntitlementReadContext,LicenseStore}.cs` + configurations + map `LicenseGrants` در `AnimoraOperatorDbContext` + ویرایش `SubscriptionService` (scheduler در هر mutation) + migration `114_Licensing` (backfill + partial unique دستی) + `ServerSchemaVersion.ExpectedHead`. push: `feat(persistence): S16 license grants/records, projector, issuer & migration 114`.
- [ ] **S16-T6 · Server endpoints + gate + consumers + bootstrap.** `Licensing/{LicenseEndpoints,ServerEntitlementGate,OutboxLicenseIssuanceScheduler,LicenseIssueConsumer,LicenseRecheckConsumer,LicenseKeyBootstrapper}.cs` + `Operator/LicenseOperatorEndpoints.cs` + DI `AddAnimoraLicensing()` (Replace gate + scheduler) + ویرایش `ServerRoutes`, `Program.cs`, csproj refs. push: `feat(server): S16 license endpoints, real entitlement gate & grant consumers`.
- [ ] **S16-T7 · تست‌های Server + PG-gated نوشته.** `ServerEntitlementGateTests`, `LicenseIssuerTests`, `LicenseGrantProjectorTests`, `LicenseKeyTests`, `LicenseSecretsTests` + PG-gated. push: `test(server): S16 entitlement gate order, issuer rules & projector monotonicity`.
- [ ] **S16-T8 · Build/run gate + PROGRESS.** build full 0/0 → run SMOKE RUN OK → acceptance grepها (شامل بند 2 crypto scan). PROGRESS: S16 ✅ Done، Next → S17، `### S16` entry (شامل تک ref مجاز client→Licensing)، deviation log. push: `feat(server): S16 licensing & anti-piracy (signed installation-bound licence, grants, entitlement gate)`.

---

# 🏁 تسک پایانی (فقط بعد از اتمام کامل S14+S15+S16)

- [ ] **FINAL · حذف این فایل پلن.** فقط `Plan/S14-S15-S16-TODO.md` را حذف کن (به هیچ فایل دیگر یا roadmap دست نزن). اجرا:
  `git rm Plan/S14-S15-S16-TODO.md && scripts/git/push.sh "chore(plan): remove completed S14-S16 execution plan"`.

---

## 📌 یادداشت‌های پیوسته برای مدل ادامه‌دهنده
- ترتیب الزامی: S14 → S15 → S16 (وابستگی خطی). هیچ تسک فاز بعدی را قبل از اتمام فاز قبلی شروع نکن.
- در sandbox تست‌های PG-gated بدون `ANIMORA_PG_TEST_CONNECTION` با reason skip می‌شوند — این **قبول** است؛ آن‌ها را بنویس ولی اجرای‌شان لازم نیست.
- Full test طولانی ممنوع — فقط filter هدفمند (`test.sh <filter>`). build فقط `full` در gateهای پایانی هر فاز؛ در میانه در صورت نیاز `fast`.
- migration هرگز بعد از ساخت ویرایش نشود؛ اگر اشتباه شد، migration جدید.
- هر seam که فاز به آن اشاره می‌کند و در Reference Pack بالا هست = اسم واقعی؛ grep دوباره لازم نیست مگر compile error خلافش را ثابت کند (آنگاه کوچک‌ترین fix + ثبت در deviation log PROGRESS).
- اگر fix سنگین/refactor حجمی وسط تسک رخ داد → push جداگانه با پیام واضح، سپس ادامهٔ تسک.
