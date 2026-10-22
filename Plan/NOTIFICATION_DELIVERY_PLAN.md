# PLAN — سیستم تحویل و گزارش پیام‌های پنل ادمین به کلاینت‌ها

> این فایل یک برنامهٔ اجرایی کامل و خودبسنده است. مدلی که این را می‌خواند باید
> بتواند **بدون بررسی دوبارهٔ ریپو** مستقیماً پیاده‌سازی را شروع کند. تمام نتیجه‌گیری‌ها،
> مسیر فایل‌ها، الگوهای موجود و تصمیمات معماری این‌جا آماده‌اند.
>
> **قانون قفل‌شدهٔ پوش:** بعد از تکمیل هر تسک، حتماً اجرا کن:
> `scripts/git/push.sh "type(scope): message"`  ← نزدن پوش = شکست.
> برنچ کاری: `genspark_ai_developer`.

---

## 0) خلاصهٔ مشکل و ریشهٔ واقعی (RESULT — خالص، نیازی به بررسی دوباره نیست)

**علامت:** در پنل ادمین (Blazor Server، پورت 5000) وقتی پیام ارسال می‌شود،
«ارسال شد» نمایش داده می‌شود (`Receipt.Sent`)، ولی هیچ کلاینتی پیام را دریافت نمی‌کند.

**ریشهٔ قطعی (کشف‌شده با بررسی کد):**
پیام‌ها در سرور ذخیره می‌شوند ولی **هیچ‌وقت به دسکتاپ تحویل داده نمی‌شوند**. این در کد
به‌صراحت به‌صورت TODO رها شده است:

- `src/Animora.Server/Admin/AdminNotificationAdapter.cs:10`
  → `// TODO(sync, Tracks D–E): deliver to the desktop via the state-sync feed once it is wired.`
- `src/Animora.Server/Admin/AdminBroadcastAdapter.cs:9`
  → `// delivery to the desktop stays a TODO(sync, Tracks D–E)`
- `src/Animora.Persistence.PostgreSql/Operator/TenantNotification.cs:13`
  → `// TODO(sync): deliver these rows to the desktop ... until then nothing is pushed to clinics.`

**جریان فعلی (ناقص):**
1. ادمین فرم را پست می‌کند → `IAdminBroadcastCommands.BroadcastAsync` / `IAdminNotificationCommands.SendAsync`.
2. آداپتر → `OperatorNotificationBroadcast.SendAsync` / `OperatorNotificationAdministration.SendAsync`
   (در `src/Animora.Persistence.PostgreSql/Operator/`).
3. یک ردیف `TenantNotification` در جدول `public."TenantNotifications"` ذخیره می‌شود + یک ردیف Audit.
   `Receipt.Sent` فقط یعنی «در DB نوشته شد»، **نه** «کلاینت گرفت».
4. **اینجا تمام می‌شود.** هیچ تحویلی به کلاینت وجود ندارد.

**سمت کلاینت (آماده ولی گرسنه):**
- `src/Animora.Client.Services/Notifications/NotificationService.cs` یک گیرندهٔ
  offline-first است که از جدول محلی SQLite `NotificationRow` می‌خواند
  (`VisibleTo` بر اساس `TenantId` + Audience). کامنت خودش می‌گوید پیام‌ها
  «land in the local store through sync» — ولی **هیچ sync‌ای آنها را نمی‌آورد**.
- جدول محلی: `src/Animora.Persistence.Sqlite/Entities/NotificationRow.cs`
  و `NotificationReceiptRow.cs` (رسید خوانده‌شدن، محلی).

**چرا از مسیر sync feed عمومی نمی‌رود (تصمیم معماری — خالص):**
- sync feed عمومی مبتنی بر journal/trigger است: `SyncContractCatalog.ProjectionOrder`
  (`src/Animora.Persistence.PostgreSql/Replication/SyncContractCatalog.cs`).
  `TenantNotification` در این لیست **نیست**.
- افزودن آن به این مسیر یعنی: نیاز به ستون‌های `Version`/`Deleted`/`UpdatedAt`،
  یک Wire contract به نام `TenantNotificationSyncContract`، migration برای journal trigger،
  ورودی catalog، و یک `EntityMap` در `ReplicaMaterializer`
  (`src/Animora.Sync.Custom/Replica/ReplicaMaterializer.cs`). بسیار سنگین و پرریسک.
- **راه استاندارد و سبک انتخاب‌شده:** دقیقاً مثل مسیر مالی موجود
  (`/finance/ledger`) یک کانال read-only جداگانه بسازیم:
  - سرور: `GET /notifications?since=&take=` (الگوبرداری از `FinanceEndpoints.cs`).
  - کلاینت: یک `HttpPeerNotificationSync` (الگوبرداری از `HttpPeerFinanceSync.cs`)
    که در `SyncOrchestrator.PullPeerFinanceAsync` کنار finance صدا زده می‌شود.
  - رسید تحویل واقعی: `POST /notifications/receipts` که کلاینت بعد از نوشتن
    محلی صدا می‌زند؛ سرور یک ردیف `TenantNotificationDelivery` ثبت می‌کند.
  - گزارش ادمین: صفحهٔ پنل تعداد/زمان تحویل واقعی و دکمهٔ ارسال مجدد را نشان می‌دهد.

**«چند کلاینت گرفت» چطور شمارش می‌شود (خالص):**
هر دستگاه یک ردیف `Device` در سرور دارد
(`src/Animora.Domain/Identity/Device.cs`: `UserId`, `DeviceName`, `Platform`,
`Status` (Pending/Active/Revoked), `LastContactUtc`, + `TenantId` از base).
«دریافت واقعی» = وجود ردیف `TenantNotificationDelivery(NotificationId, DeviceId, UserId, ReceivedUtc)`.
«کلاینت‌هایی که نگرفته‌اند» = دستگاه‌های Active آن tenant که ردیف delivery ندارند.

---

## 1) الگوهای موجود که باید کپی/دنبال شوند (مسیر دقیق فایل‌ها)

| هدف | فایل الگو | نکته |
|---|---|---|
| Endpoint سرور read-only tenant-scoped | `src/Animora.Server/Sync/FinanceEndpoints.cs` | گروه با `CoarsePolicies.TenantMember`+`DeviceActive`+`RateLimitPolicies.Sync`+`SyncTenantBoundFilter`؛ `?since=&take=`؛ try/catch با لاگ correlation و rethrow. |
| ثبت endpoint در bootstrap | `src/Animora.Server/Program.cs` (خط ~232 `MapRazorComponents`؛ جستجوی `MapSyncEndpoints`/`MapFinanceEndpoints`) | همان‌جا `MapNotificationFeedEndpoints()` را اضافه کن. |
| ثابت‌های مسیر | `src/Animora.Server/Hosting/ServerRoutes.cs` | یک const جدید `Notifications = "/notifications"` اضافه کن. |
| کلاینت مصرف‌کننده + cursor | `src/Animora.Sync.Custom/Finance/HttpPeerFinanceSync.cs` | upsert idempotent، cursor در همان transaction، `MaxPagesPerPass`. |
| متد HTTP کلاینت | `src/Animora.Sync.Custom/SyncFeedClient.cs` (خط ~169 `GetFinanceLedgerAsync`) | `NewRequest`+`SendAsync`+`MapStatus`+`ReadJsonAsync` موجودند؛ فقط متد جدید اضافه کن. |
| صدازدن pull از orchestrator | `src/Animora.Client.Services/Sync/SyncOrchestrator.cs` (خط ~107 `PullPeerFinanceAsync`) | بعد از state pull سالم، notification pull را هم صدا بزن (best-effort، failure کل pass را خراب نکند). |
| DI کلاینت | `src/Animora.Sync.Custom/SyncCustomServiceCollectionExtensions.cs` (خط ~66 `TryAddSingleton<IPeerFinanceSync, HttpPeerFinanceSync>()`) | رجیستر سرویس جدید. |
| DI سرور/ادمین | `src/Animora.Server/Admin/AdminServiceCollectionExtensions.cs` (خطوط ~51-52) | رجیستر query/command adapter جدید برای گزارش و ارسال مجدد. |
| صفحهٔ ادمین + Receipt | `src/Animora.Web.Admin/Pages/Notifications.razor` و `TenantNotify.razor` | این‌ها فرم ارسالند؛ بخش گزارش را این‌جا یا صفحهٔ جدید اضافه کن. |
| Port/Model ادمین | `src/Animora.Web.Admin/Ports/AdminPorts.cs` (خط ~61-70)، `src/Animora.Web.Admin/Models/AdminModels.cs` (`BroadcastReceipt` خط 97، `MutationReceipt` خط 66) | interface و رکورد جدید. |
| entity سرور | `src/Animora.Persistence.PostgreSql/Operator/TenantNotification.cs` | الگوی ساخت entity جدید `TenantNotificationDelivery`. |
| config سرور | `src/Animora.Persistence.PostgreSql/Configurations/TenantNotificationConfiguration.cs` | الگوی `IEntityTypeConfiguration`. |
| migration سرور | `src/Animora.Persistence.PostgreSql/Migrations/Server/20261021130000_121_TenantNotifications.cs` | الگوی migration با SQL خام + snapshot. **حتماً** `AnimoraServerDbContextModelSnapshot.cs` هم به‌روز شود. `ServerSchemaVersion.cs` را هم چک کن. |
| entity محلی + config | `src/Animora.Persistence.Sqlite/Entities/NotificationRow.cs`, `NotificationReceiptRow.cs`, `src/Animora.Persistence.Sqlite/Configurations/Notifications/NotificationConfigurations.cs` | محل نوشتن پیام‌های آمده از سرور + cursor محلی. |

**نکتهٔ DbContext (خالص):** جدول `TenantNotifications` فیزیکی در همان DB است و با
migration روی `AnimoraServerDbContext` ساخته شده (هرچند DbSet آن در
`AnimoraOperatorDbContext` map شده). پس endpoint سرور که با DbContext
tenant-scoped (`IFeedContextFactory`/`AnimoraServerDbContext` با `CurrentTenantId`)
کار می‌کند، می‌تواند با SQL ساده یا با map کردن entity در server context، ردیف‌های
tenant جاری را بخواند. **ساده‌ترین مسیر کم‌ریسک:** خواندن مستقیم از جدول
`TenantNotifications` با فیلتر `TenantId = @tenant` (مثل RecordProjector که SQL خام می‌زند)،
یا map کردن `TenantNotification` + `TenantNotificationDelivery` در `AnimoraServerDbContext`.

**نکتهٔ لاگ (خالص):** کل اپ از `TraceLog.Info/Warn/Error(msg, category)` استفاده می‌کند
(`Animora.Contracts.Diagnostics`). دسته‌های موجود: `sync`, `sync.finance`, `sync.apply`.
برای این فیچر دستهٔ جدید `sync.notify` را استفاده کن تا در ترمینال
`publish-server-connected.ps1` با grep `sync.notify` قابل رهگیری باشد.
`ANIMORA_NET_LOG=1` و `ANIMORA_TRACE_DEBUG=1` از قبل در لانچر روشن‌اند و خروجی کنسول
را به ترمینال stream می‌کنند — یعنی هر `TraceLog` در ترمینال کلاینت دیده می‌شود.

---

## 2) قرارداد Wire (DTOها) — دقیق

در `src/Animora.Contracts.Wire/Sync/` (کنار `FinanceReadPage`) این DTOها را بساز
(نام دقیق + فیلدها؛ serializer موجود `SyncSerializerV2.Options` است):

```csharp
// صفحهٔ pull پیام‌ها (پاسخ GET /notifications)
public sealed record NotificationFeedPage(
    IReadOnlyList<NotificationFeedItem> Items,
    long NextCursorTicks,
    bool HasMore);

public sealed record NotificationFeedItem(
    string Id, int AudienceKind, string? AudienceValue, int Kind, string? Tag,
    string Title, string Body,
    DateTimeOffset CreatedAtUtc, DateTimeOffset? ScheduledForUtc, DateTimeOffset? ExpiresAtUtc);

// رسید تحویل واقعی (بدنهٔ POST /notifications/receipts)
public sealed record NotificationReceiptReport(IReadOnlyList<string> NotificationIds);
```

نگاشت `TenantNotification → NotificationFeedItem` «کپی مستقیم» است (طبق کامنت خود entity
که می‌گوید فیلدها عمداً با `NotificationRow` هم‌معنا طراحی شده‌اند). cursor = `CreatedAtUtc.UtcTicks`
(مثل finance که از ticks استفاده می‌کند). برای tie-break هم‌تیک‌ها، ترتیب ثانویه `Id` و cursor را
«آخرین تیک دیده‌شده» بگیر و در کلاینت ردیف‌های تکراری را با upsert idempotent خنثی کن.

---

## 3) جدول تحویل سرور (entity جدید) — دقیق

`TenantNotificationDelivery` (فایل جدید در `Operator/` یا کنار entityهای server):

```
Id (varchar 26, PK)        // IdGenerator.NewId(now)
TenantId (varchar 26)      // از توکن دستگاه
NotificationId (varchar 26)
DeviceId (varchar 26)
UserId (varchar 26)
ReceivedUtc (timestamptz 6)
UNIQUE(NotificationId, DeviceId)   // idempotent؛ رسید مجدد no-op
INDEX(TenantId, NotificationId)    // برای شمارش گزارش
```

migration جدید (مثلاً `20261022..._122_TenantNotificationDeliveries.cs`) با SQL خام مثل
الگوی 121. **snapshot را حتماً آپدیت کن.** اگر delivery را در `AnimoraServerDbContext` map
می‌کنی، snapshot آن context باید شامل جدول جدید شود.

---

## تسک‌لیست اجرایی (وضعیت هر تسک را به‌روز کن)

> بعد از **هر** تسک: `scripts/git/push.sh "..."` (قفل‌شده).

### Task 1 — قرارداد Wire DTOها  — وضعیت: [x] انجام شد
- فایل: `src/Animora.Contracts.Wire/Sync/NotificationFeedContracts.cs` (جدید).
- محتوا: سه رکورد بخش ۲ را اضافه کن. از namespace موجود `Animora.Contracts.Wire.Sync`.
- چک: نام‌گذاری با بقیهٔ فایل‌های `Sync/` هماهنگ باشد.
- **پوش:** `feat(contracts): add notification feed/receipt wire DTOs`

### Task 2 — جدول تحویل سرور + migration  — وضعیت: [x] انجام شد
- entity `TenantNotificationDelivery` + `IEntityTypeConfiguration` (الگوی Task از بخش ۳).
- map در `AnimoraServerDbContext` (`src/Animora.Persistence.PostgreSql/Context/AnimoraServerDbContext.cs`،
  کنار بقیهٔ DbSetها) + `ApplyConfiguration`.
- migration جدید با SQL خام + به‌روزرسانی `AnimoraServerDbContextModelSnapshot.cs`
  + بررسی/افزایش `ServerSchemaVersion.cs` اگر لازم بود.
- **پوش:** `feat(persistence): add TenantNotificationDelivery table + migration`

### Task 3 — Endpointهای سرور (pull + receipt)  — وضعیت: [x] انجام شد
- فایل جدید: `src/Animora.Server/Sync/NotificationFeedEndpoints.cs` (الگوی `FinanceEndpoints.cs`).
  - `GET /notifications?since=&take=` → خواندن `TenantNotifications` با `TenantId=@tenant`،
    فیلتر: `ScheduledForUtc == null || ScheduledForUtc <= now` (منتشرشده)، و
    `ExpiresAtUtc == null || ExpiresAtUtc > now` (منقضی‌نشده)، `CreatedAtUtc.UtcTicks > since`،
    مرتب‌شده بر `CreatedAtUtc`، `Take(take)`؛ بازگرداندن `NotificationFeedPage`.
  - `POST /notifications/receipts` (بدنه `NotificationReceiptReport`) → برای هر NotificationId
    معتبرِ همین tenant، یک `TenantNotificationDelivery` با `DeviceId`/`UserId` از claims دستگاه
    (از `HttpContext`/auth مثل بقیهٔ endpointها) upsert کن (ON CONFLICT DO NOTHING).
  - گروه با `CoarsePolicies.TenantMember`+`DeviceActive`+`RateLimitPolicies.Sync`+`SyncTenantBoundFilter`.
  - try/catch با `TraceLog`/logger + correlation مثل finance.
- const مسیر در `ServerRoutes.cs`.
- ثبت در `Program.cs` کنار `MapFinanceEndpoints()`.
- **DeviceId/UserId از کجا (قطعی — کشف‌شده):** در handler endpoint، سرویس scoped
  `IRequestAuthorizationState` را inject کن؛ `state.Membership` یک رکورد
  `ResolvedMembership(string TenantId, StaffRole Role, string UserId, string DeviceId, bool DeviceActive)`
  است (`src/Animora.Server/Authorization/IMembershipResolver.cs`،
  `CoarsePolicies.cs:60` همین را برای `DeviceActive` استفاده می‌کند).
  یعنی: `state.Membership.DeviceId`, `state.Membership.UserId`, `state.Membership.TenantId`.
  اگر `Membership` null بود → 401 (ولی policyها قبلش تضمین می‌کنند non-null است).
  TenantId را می‌توانی از `ITenantContext`/DbContext `CurrentTenantId` هم بگیری.
- **پوش:** `feat(server): add notification pull + delivery-receipt endpoints`

### Task 4 — متد HTTP کلاینت در SyncFeedClient  — وضعیت: [x] انجام شد
- فایل: `src/Animora.Sync.Custom/SyncFeedClient.cs` (کنار `GetFinanceLedgerAsync` خط ~169).
- دو متد:
  - `GetNotificationFeedAsync(long sinceTicks, int take, ct)` → `Result<NotificationFeedPage>`.
  - `ReportNotificationReceiptsAsync(IReadOnlyList<string> ids, ct)` → `Result` (POST JSON).
- از `NewRequest`+`SendAsync`+`MapStatus`+`ReadJsonAsync`/serialize موجود استفاده کن.
- **پوش:** `feat(sync): SyncFeedClient notification pull + receipt calls`

### Task 5 — مصرف‌کنندهٔ کلاینت (HttpPeerNotificationSync)  — وضعیت: [x] انجام شد
- فایل جدید: `src/Animora.Sync.Custom/Notifications/HttpPeerNotificationSync.cs`
  (الگوی `Finance/HttpPeerFinanceSync.cs`).
- Port جدید در Application (الگوی `src/Animora.Application/Ports/IPeerFinanceSync.cs`):
  `IPeerNotificationSync { Task<Result<int>> PullAsync(ct); }`.
- منطق:
  1. cursor محلی را بخوان (یک entity cursor محلی جدید — الگوی `PeerFinanceCursor`؛
     یا ذخیره در یک ردیف state محلی). ticks.
  2. `client.GetNotificationFeedAsync(cursor, PageSize, ct)`.
  3. هر item را در جدول محلی `NotificationRow` **upsert idempotent** بنویس
     (کلید `Id`؛ اگر هست و قدیمی‌تر، overwrite نکن). **مهم:** `PublishedAtUtc` را طوری
     تنظیم کن که `NotificationService.VisibleTo` آن را ببیند — یعنی ردیف‌های آمده از سرور
     که scheduled نیستند باید `PublishedAtUtc != null` باشند (سرور فقط منتشرشده‌ها را می‌دهد؛
     ولی برای scheduledهای محلی، sweep موجود در `NotificationService` کار می‌کند).
     ساده‌ترین: اگر item `ScheduledForUtc` آینده ندارد، `PublishedAtUtc = CreatedAtUtc`؛
     در غیر این صورت `ScheduledForUtc` را بگذار و `PublishedAtUtc=null` تا sweep منتشرش کند.
  4. cursor را در همان transaction جلو ببر.
  5. idها را جمع کن و `client.ReportNotificationReceiptsAsync(ids, ct)` را صدا بزن؛
     شکست رسید نباید pull را خراب کند (لاگ `sync.notify` و تلاش مجدد پاس بعد).
  6. بعد از نوشتن، معادل `FeedChanged` را تحریک کن تا UI رفرش شود — بررسی کن که آیا
     `NotificationService.FeedChanged` از بیرون قابل تحریک است؛ اگر نه، چون `NotificationService`
     روی هر read دوباره می‌خواند، کافی است ردیف‌ها در DB باشند و badge در تیک بعدی آپدیت شود.
     (در صورت امکان، یک hook ساده برای raise کردن FeedChanged اضافه کن.)
- `TraceLog.Info/Warn` با دستهٔ `sync.notify` در تمام مراحل (fetch/applied/receipt).
- **پوش:** `feat(sync): client notification pull/apply + real receipt report`

### Task 6 — اتصال به Orchestrator + DI کلاینت  — وضعیت: [x] انجام شد
- `src/Animora.Client.Services/Sync/SyncOrchestrator.cs`: بعد از
  `PullPeerFinanceAsync` (خط ~103) یک `PullPeerNotificationsAsync(ct)` مشابه اضافه کن
  (همان gate/session/best-effort pattern).
- `src/Animora.Sync.Custom/SyncCustomServiceCollectionExtensions.cs` خط ~66:
  `services.TryAddSingleton<IPeerNotificationSync, HttpPeerNotificationSync>();`
- اگر cursor/جدول محلی جدید اضافه شد، در `AnimoraLocalDbContext` و config محلی map کن
  (`src/Animora.Persistence.Sqlite/Context/AnimoraLocalDbContext.cs`).
- **پوش:** `feat(sync): wire notification pull into the sync orchestrator`

### Task 7 — گزارش ادمین + ارسال مجدد (Port/Model/Adapter/UI)  — وضعیت: [x] انجام شد
- **Model** (`AdminModels.cs`):
  - `NotificationDeliveryReportRow(string NotificationId, string Title, int Kind, DateTimeOffset CreatedAtUtc, int TargetDevices, int DeliveredDevices, DateTimeOffset? FirstDeliveredUtc, DateTimeOffset? LastDeliveredUtc)`.
  - شاید `BroadcastReceipt` را با تعداد tenant/هدف غنی کن (اختیاری، سازگار با UI فعلی بمان).
- **Port** (`AdminPorts.cs`): `IAdminNotificationReport`:
  - `Task<Result<IReadOnlyList<NotificationDeliveryReportRow>>> RecentAsync(string? tenantId, int take, ct)`.
  - `Task<Result<MutationReceipt>> ResendUndeliveredAsync(string notificationId, ct)` — ارسال مجدد
    به دستگاه‌هایی که delivery ندارند (در عمل یعنی مطمئن‌شدن از قابل‌pull بودن؛ چون مدل pull است،
    «ارسال مجدد» = هیچ کار سروری لازم نیست جز اطمینان از اینکه ردیف هنوز منتشر/منقضی‌نشده است.
    کافی‌ترین پیاده‌سازی مفید: نمایش دستگاه‌های تحویل‌نشده و یک دکمه که در صورت نیاز
    `ExpiresAtUtc` را تمدید می‌کند یا یک کپی تازه از پیام با همان متن می‌سازد تا cursor کلاینت‌های
    عقب‌مانده دوباره آن را بیاورد). **تصمیم:** ساده‌ترین «ارسال مجدد» واقعی = ساختن یک
    `TenantNotification` جدید با همان محتوا فقط برای همان tenant/audience (کلاینت‌هایی که
    نگرفته بودند در pull بعدی می‌گیرند؛ کلاینت‌هایی که گرفته بودند چون `Id` جدید است یک کپی
    می‌گیرند — برای جلوگیری از تکرار، فقط وقتی نشان بده که delivered < target).
- **Adapter** سرور: `AdminNotificationReportAdapter` که با DbContext،
  `TenantNotifications` را با `TenantNotificationDelivery` join/شمارش می‌کند و تعداد دستگاه‌های
  هدف را از `Devices` (Status=Active، tenant) می‌گیرد. رجیستر در
  `AdminServiceCollectionExtensions.cs`.
- **UI**: در `Notifications.razor` (یا صفحهٔ جدید `/admin/notifications/report`):
  یک جدول با ستون‌های: عنوان، نوع، زمان ارسال، «هدف N دستگاه»، «تحویل‌شده M»، اولین/آخرین تحویل،
  و دکمهٔ «ارسال مجدد به تحویل‌نشده‌ها» وقتی `M < N`. از `Loc.T` و استایل card موجود استفاده کن.
  رشته‌های محلی‌سازی را در `AdminStrings.cs` اضافه کن (کلیدهای `notify.report.*`).
- **پوش:** `feat(admin): delivery report + resend-to-undelivered for messages`

### Task 8 — لاگ‌های publish-server-connected.ps1  — وضعیت: [x] انجام شد
- فایل: `scripts/windows/publish-server-connected.ps1`.
- کاری که این اسکریپت می‌کند: build/publish دسکتاپ و ساخت `Run-Animora.cmd`/`.ps1` که
  ترمینال را باز نگه می‌دارند و با `ANIMORA_NET_LOG=1`+`ANIMORA_TRACE_DEBUG=1`
  خروجی کنسول (شامل همهٔ `TraceLog`) را stream می‌کنند.
- **تغییر لازم (کم و هدفمند):** متن راهنمای داخل `$CmdContent`، `$Ps1Content`،
  `$ReadmeContent` و بخش Summary را طوری به‌روزرسانی کن که دستهٔ لاگ جدید پیام‌ها را هم
  معرفی کند: به لیست دسته‌های قابل grep، `notify` و `sync.notify` را اضافه کن
  (الان: `patients, svc.patients, sql.patients, dispatch, sync, sync.apply, sync.push`).
- همچنین یک خط توضیح کوتاه اضافه کن که برای دیدن «وضعیت دریافت پیام‌ها» در ترمینال،
  دنبال خطوط `sync.notify` بگردند (fetch/applied/receipt/skip).
- **بدون** تغییر منطق build. فقط رشته‌های راهنما/trace.
- **پوش:** `docs(publish): surface notification (sync.notify) trace in launcher logs`

### Task 9 — بیلد هدفمند پروژه‌های تغییرکرده (بدون full/long test)  — وضعیت: [x] انجام شد (بیلد GitHub Actions سبز: Linux + Windows)
- فقط پروژه‌های دست‌خورده را build کن تا کامپایل سالم باشد (نه کل solution، نه تست کامل):
  `dotnet build src/Animora.Contracts.Wire/Animora.Contracts.Wire.csproj`،
  `dotnet build src/Animora.Persistence.PostgreSql/...`،
  `dotnet build src/Animora.Server/...`،
  `dotnet build src/Animora.Sync.Custom/...`،
  `dotnet build src/Animora.Client.Services/...`.
- اگر SDK/دات‌نت در سندباکس نبود، این تسک را skip و در فایل یادداشت کن که build باید
  روی ماشین ویندوز/CI تأیید شود؛ ولی صحت نحوی کد را با بازخوانی تضمین کن.
- خطاهای کامپایل را فیکس کن. اگر فیکس سنگین شد، همان‌جا پوش جداگانه بزن.
- **پوش:** `fix: resolve build issues for notification delivery feature` (در صورت نیاز)

### Task 10 — تسک پایانی: حذف فایل Plan  — وضعیت: [ ] در انتظار
- **فقط** این فایل (`Plan/NOTIFICATION_DELIVERY_PLAN.md`) را حذف کن.
  (به `Plan/reports/*` و هیچ فایل دیگری دست نزن.)
- **پوش:** `chore(plan): remove completed notification-delivery plan`

---

## 4) ریسک‌ها و نکات نهایی (خالص)
- **idempotency همه‌جا:** رسید با `UNIQUE(NotificationId,DeviceId)`؛ apply کلاینت با upsert بر `Id`؛
  پیام تکراری نباید دوبار نوشته/شمرده شود.
- **audience filter محلی از قبل درست است** (`NotificationService.VisibleTo`)؛ کافی است ردیف
  با `AudienceKind`/`AudienceValue` درست در `NotificationRow` بنشیند.
- **DeviceActive claim:** قبل از نوشتن endpoint، دقیقاً پیدا کن device/user id از کجا خوانده می‌شود
  (grep `DeviceActive`, `DeviceId`, claim). این تنها جای مبهم است؛ بقیه قطعی است.
- **snapshot migration:** فراموش نکردن به‌روزرسانی `AnimoraServerDbContextModelSnapshot.cs`
  وگرنه migration بعدی/بیلد خطا می‌دهد.
- **UI سازگاری:** `Receipt` فعلی (`Sent/Skipped`) را نشکن؛ گزارش را به‌صورت افزودنی بیاور.
- هر فیکس/کد سنگین حین هر تسک → پوش جداگانه با پیام قابل‌فهم (قانون).
