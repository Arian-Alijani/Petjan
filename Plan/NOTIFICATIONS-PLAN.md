# Plan — صفحهٔ «اعلان‌ها و پیام‌ها» + جایزه/اشتراک ادمین (Animora Desktop)

> این فایل خودبسنده است. یک مدل در سشن کاملاً نو باید بتواند فقط با خواندن همین فایل،
> بدون بررسی دوبارهٔ ریپو، تسک‌ها را پیاده کند. هر تسک «چه‌کاری/کجا/چطور» را دقیق دارد.
> **قانون قفل‌شده:** بعد از اتمام هر تسک → `scripts/git/push.sh "<msg>"` اجرا و پوش شود.
> اگر exit code = 3 بود، یک‌بار ابزار `setup_github_environment` صدا زده و دوباره اجرا کن.
> کار روی برنچ `genspark_ai_developer` انجام شود (نه main).

---

## 0) زمینه و درک سیستم (نتایج آماده — دوباره بررسی نکن)

**محصول:** Animora = نرم‌افزار SaaS مدیریت + حسابداری کلینیک دامپزشکی. یک سرور مرکزی
چندمستاجری (vendor-hosted)، کلاینت دسکتاپ **offline-first** با Avalonia + .NET 10،
فارسی/RTL، تقویم جلالی، معماری MVVM با CommunityToolkit.Mvvm.

**وضعیت مهم (قطعی):** فرانت‌اند دسکتاپ کامل است و روی **بک‌اند محلی واقعی (SQLite)** اجرا می‌شود.
**سرور مرکزی و Datasync هنوز wire نشده‌اند** (Tracks D–E آینده). پس این فیچر باید **کاملاً روی
لایه‌های محلی** ساخته شود: `Contracts` (اینترفیس/DTO) → `Client.Services` (پیاده‌سازی) →
`Persistence.Sqlite` (ذخیرهٔ محلی) → `Desktop` (UI). این دقیقاً الگوی فعلی همهٔ صفحات است.
بدون نیاز به سرور، «نوتیف بدون سینک» یعنی: نوشتن ادمین مستقیماً در همان دیتابیس محلی ذخیره
می‌شود و UI با مکانیزم رویداد درون‌پردازه‌ای (نه polling/sync) بلافاصله به‌روز می‌شود.

**جریان داده‌ی معماری (قانون غیرقابل‌نقض):**
View → ViewModel → اینترفیس `Animora.Contracts` (از طریق constructor DI) → پیاده‌سازی واقعی در
`Animora.Client.Services` → (در صورت نیاز) EF Core SQLite. ViewModelها **فقط** به اینترفیس‌های
Contracts وابسته‌اند، هرگز به EF/SQL یا سرویس concrete. ثبت DI در `src/Animora.Desktop/App.axaml.cs`.

### مکان‌های کلیدی کشف‌شده (مسیرهای دقیق)
| چیز | مسیر |
|---|---|
| ثبت DI (composition root) | `src/Animora.Desktop/App.axaml.cs` — متد `ConfigureServices`؛ الگو: `services.AddSingleton<IXxxService, XxxService>(); services.AddSingleton<XxxPageViewModel>();` |
| منوی سایدبار (استاتیک) | `src/Animora.Desktop/Shell/NavigationMenu.cs` — آرایهٔ `NavItem` با گروه‌ها: «مرکز فرمان»، «مدیریت کلینیک»، «کسب و کار»، «سیستم». امضا: `new("key","عنوان","IconXxx", typeof(XxxPageViewModel), "گروه")` |
| گیت دسترسی nav | `src/Animora.Desktop/Shell/NavPermissions.cs` — `ByKey` (key→permission code) و `ByPage` (نوع VM→key). برای صفحهٔ جدید باید هر دو را اضافه کنیم. کدهای مجوز در `Animora.Domain.Identity.PermissionCodes`. |
| هدر/تاپ‌بار | `src/Animora.Desktop/Shell/TopbarView.axaml` + `TopbarViewModel.cs` — دکمهٔ زنگ اعلان با `Command="{Binding OpenNotificationsCommand}"` و یک `Ellipse` قرمز ثابت (نقطهٔ dummy). کامند فعلی فقط `_toast.Show("۵ اعلان تازه دارید.")` است (استاب). باید به ناوبری صفحه + بِج عددی واقعی وصل شود. |
| آیکن زنگ در Icons.axaml | `Data="{StaticResource IconBell}"` حدود خط ۲۶۲ TopbarView |
| پایهٔ ViewModel صفحات | `src/Animora.Desktop/Shell/PageViewModelBase.cs` — ارث‌بری از `ObservableObject`؛ `Title`, `Eyebrow`, `Breadcrumbs`, `IsBusy`, `OnNavigatedTo()`, `OnNavigatedFrom()`, `UseLoadStatus(Func<Task>)`, `RefreshOnDataChange(...)` |
| ناقل تغییر داده درون‌پردازه | `src/Animora.Desktop/Services/IDataChangeNotifier.cs` — enum `DataDomain {Patients,Visits,...}` + `Notify(params DataDomain[])` و `Subscribe(Action<DataDomain>)`. **باید عضو `Notifications` به enum اضافه شود** تا UI بدون sync به‌روز شود. |
| کاربر جاری | `Animora.Contracts.Identity.ICurrentUserContext` — `UserId`, `Name`, `RoleLabel`, `Initials`, `Role` (enum `UserRole`). این «گیرنده»ی پیام است. نقش ادمین اصلی از `Role`/permission تشخیص داده می‌شود. |
| الگوی اینترفیس Contracts | `src/Animora.Contracts/Profile/IUserProfileService.cs` + `ProfileModels.cs` (DTOها همان‌جا، record/class ساده) |
| الگوی سرویس کلاینت | `src/Animora.Client.Services/Profile/UserProfileService.cs` |
| DbContext محلی | `src/Animora.Persistence.Sqlite/Context/AnimoraLocalDbContext.cs` — از `ApplyConfigurationsFromAssembly` استفاده می‌کند (بدون DbSet صریح لازم؛ Entity + IEntityTypeConfiguration کافی است). |
| الگوی Entity | `src/Animora.Persistence.Sqlite/Entities/CommandIdRemap.cs` — کلاس sealed، ctor خصوصی، پراپرتی‌های private set، صفت `[LocalOnly]` برای جداول محلی که replicate نمی‌شوند (از `Animora.Domain.Primitives`). |
| الگوی Configuration | `src/Animora.Persistence.Sqlite/Configurations/Activity/*.cs` — `IEntityTypeConfiguration<T>`: `HasKey`، `HasIndex(...).HasDatabaseName(...)`. طول رشته‌ها توسط `ModelConventions` تنظیم می‌شود. |
| الگوی Migration | `src/Animora.Persistence.Sqlite/Migrations/20261020120000__039_CommandIdRemap.cs` — نام فایل: `<timestamp>__NNN_Name.cs`؛ attributeها `[DbContext(typeof(AnimoraLocalDbContext))]` و `[Migration("<timestamp>__NNN_Name")]`؛ کلاس `: Migration` با `Up`/`Down`. **سرآخر محلی = `039`؛ مایگریشن جدید = `040`.** باید Designer و snapshot هم به‌روز شوند — ترجیحاً با `dotnet ef migrations add` (پایین). |
| ساخت/بیلد (این سندباکس) | `scripts/sandbox/build.sh` (فقط Desktop، انتظار 0/0) · `scripts/sandbox/build.sh full` · `scripts/sandbox/run.sh` (اسموک، چاپ `SMOKE RUN OK`) |

### آیکن‌های موجود در `Resources/Theme/Icons.axaml` (برای تگ‌ها/استایل‌ها — موجودند، نساز)
`IconBell IconMegaphone IconAlert IconSiren IconInfo IconCheck IconCheckCircle IconTag IconGift?`
→ موجودِ قطعی مرتبط: **IconBell, IconMegaphone, IconAlert, IconSiren, IconInfo, IconCheck,
IconCheckCircle, IconTag, IconCake, IconSpark, IconBulb, IconGift(نبود→ از IconCake/IconSpark استفاده کن),
IconClock, IconCalendar, IconCreditCard(برای اشتراک/جایزه), IconShield**.
(لیست کامل آیکن‌ها در همان فایل است؛ اگر آیکنی نبود از نزدیک‌ترین موجود استفاده کن، آیکن جدید نساز مگر لازم.)

### هدف تسک (خلاصهٔ خواسته‌ها)
1. صفحهٔ «اعلان‌ها و پیام‌ها» با استایل حرفه‌ای؛ ردیف پیام‌ها خوانا و منظم.
2. ادمین اصلی (کاربر با نقش/مجوز مدیر) می‌تواند پیام بسازد و به **همه** یا **بخشی از کلینیک‌ها**
   بفرستد (در حالت تک‌مستاجری فعلی: به همهٔ کاربران/نقش‌های همان کلینیک یا زیرمجموعه).
3. پیام مناسبتی زمان‌بندی‌شده (ارسال در زمان خاص) + حالت‌های خاص.
4. تگ‌های مختلف + استایل‌های متفاوت برای تگ/نوع پیام (info/warning/success/promo/system...).
5. پیام سیستمی خودکار مثل «نزدیک شدن به اتمام اشتراک».
6. قابلیت «جایزه/اشتراک»: ادمین اصلی اشتراک n-روزه به عنوان جایزه برای یک شخص فعال کند؛
   هم پیام برود و هم اشتراک ثبت شود.
7. دکمهٔ زنگ هدر به این صفحه وصل شود؛ بِج عددیِ تعداد پیام خوانده‌نشده روی آیکن هدر و
   روی آیتم سایدبار؛ با باز شدن پیام، عدد مدیریت/کم شود.
8. نوتیف بلادرنگ هنگام دریافت پیام جدید، **بدون نیاز به sync** (رویداد درون‌پردازه‌ای + توست).
9. مسیر امن و مناسب (مجوزها در Application/سرویس، نه در View).

### تصمیم‌های طراحی قطعی (برای حذف نیاز به فکر دوباره)
- **ذخیره‌سازی:** جدول محلی `[LocalOnly]` به نام `Notifications` (پیام‌های تحویل‌شده به کاربر)
  + جدول `NotificationReceipts` برای وضعیت خوانده‌شدن per-(tenant,user,notification).
  در مدل تک‌مستاجری فعلی، «ارسال به بخشی/همه» با فیلد مخاطب (audience) روی خود پیام مدل می‌شود؛
  نیازی به چند جدول پیچیده نیست. **برای سادگی و بدون سرور:** یک جدول `Notifications` با ستون‌های:
  `Id (ULID string 26)`, `TenantId`, `AudienceKind (enum: AllUsers|Role|SpecificUser)`,
  `AudienceValue (nullable: roleName یا userId)`, `Kind (enum: Info|Success|Warning|Promo|System|Reward)`,
  `Tag (nullable short text)`, `Title`, `Body`, `CreatedByUserId`, `CreatedAtUtc`,
  `ScheduledForUtc (nullable — اگر زمان‌بندی‌شده)`, `PublishedAtUtc (nullable — زمانی که قابل‌نمایش شد)`,
  `ExpiresAtUtc (nullable)`. وضعیت خوانده‌شدن در جدول دوم `NotificationReceipts`:
  `TenantId`, `UserId`, `NotificationId`, `ReadAtUtc (nullable)` — کلید مرکب (TenantId,UserId,NotificationId).
- **تعداد خوانده‌نشده:** = پیام‌هایی که audience شامل کاربر جاری است و `PublishedAtUtc != null` و
  `ExpiresAtUtc` نگذشته و در `NotificationReceipts` رکورد Read ندارند.
- **زمان‌بندی:** یک تایمر سبک در سرویس (یا هنگام هر خواندن) پیام‌هایی که `ScheduledForUtc <= now`
  و `PublishedAtUtc == null` را «منتشر» می‌کند (ست‌کردن `PublishedAtUtc`) و رویداد Notify می‌زند.
  نیازی به ورکر پس‌زمینهٔ سنگین نیست؛ در ساده‌ترین حالت در `OnNavigatedTo` و در زمان ساخت پیام ارزیابی شود،
  به‌علاوه یک `DispatcherTimer` دقیقه‌ای در سرویس اعلان (مثل relative-ticker تاپ‌بار).
- **بلادرنگ بدون sync:** افزودن `DataDomain.Notifications` به enum و فراخوانی `Notify(Notifications)`
  بعد از ساخت/انتشار پیام؛ تاپ‌بار و صفحه از طریق `IDataChangeNotifier.Subscribe` بِج و لیست را به‌روز می‌کنند.
  همچنین یک توست (`IToastService`) هنگام رسیدن پیام جدیدِ منتشر‌شده برای کاربر جاری.
- **جایزه/اشتراک:** چون سرور wire نشده، «ثبت اشتراک» در این فاز به‌صورت **محلی/استابِ امن** انجام شود:
  یک اینترفیس `IRewardGrantService.GrantSubscriptionAsync(targetUserId, days, reason)` که
  (الف) یک رکورد جایزه/اشتراک محلی ثبت می‌کند (جدول `RewardGrants` یا استفادهٔ مجدد از وضعیت حساب در
  `IUserProfileService.GetAccountStatusAsync`) و (ب) یک `Notification` از نوع `Reward` برای آن کاربر می‌سازد.
  **نکته:** گره‌زدن واقعی به Subscription/LicenseGrant سروری کار Tracks D–E است؛ اینجا فقط ثبت محلی + پیام.
  در کامنت‌ها این محدودیت صریح ذکر شود (TODO سرور). مجوز: فقط نقش مدیر/ادمین اصلی.
- **امنیت:** متدهای نوشتن (ساخت پیام، اعطای جایزه) در سرویس کلاینت باید مجوز را چک کنند
  (از طریق `ICapabilityGate`/permission مدیریت) و در صورت نبود مجوز `ServiceException` بیندازند
  (`src/Animora.Client.Services/ServiceException.cs`). View هرگز مجوز را تصمیم نمی‌گیرد؛ فقط دکمهٔ ساخت
  را برای غیرمدیر مخفی/غیرفعال می‌کند (UX) اما گارد اصلی در سرویس است.
- **UI/UX:** صفحه با هدر عنوان، نوار فیلتر (همه/خوانده‌نشده + فیلتر بر اساس نوع/تگ)، لیست کارت‌های پیام
  (آیکن نوع، تگ رنگی، عنوان، متن، زمان نسبی جلالی، نشانهٔ خوانده‌نشده)، و برای ادمین یک پنل/دکمهٔ
  «ارسال پیام جدید» (مودال با فیلدهای: مخاطب، نوع، تگ، عنوان، متن، زمان‌بندی اختیاری، انقضا اختیاری)
  و «اعطای جایزه» (مودال: کاربر هدف، تعداد روز، توضیح). از توکن‌های تم موجود
  (`Resources/Theme/*.axaml`: Colors/Typography/Spacing) استفاده شود؛ رنگ تگ‌ها map نوع→برش.

---

## 1) تسک‌لیست (هر تسک بعد از اتمام = push)

> وضعیت‌ها: `[ ]` انجام‌نشده · `[~]` درحال‌انجام · `[x]` انجام‌شده.
> **مسیر زنجیره‌ای:** Contracts → Persistence → Client.Services → DI → Desktop UI → Shell(هدر/سایدبار/بِج) → Reward → Build/Smoke.

### T0 — آماده‌سازی برنچ `[x]`
- `git switch -c genspark_ai_developer` (اگر هست: `git switch genspark_ai_developer`).
  در عمل، اجرای `scripts/git/push.sh -b genspark_ai_developer "chore(plan): add notifications plan"`
  هم برنچ را می‌سازد/سوئیچ می‌کند و هم همین فایل پلن را پوش می‌کند. **این اولین پوش است.**

### T1 — لایهٔ Contracts: اینترفیس + DTOها `[x]`
- پوشهٔ جدید: `src/Animora.Contracts/Notifications/`.
- فایل `NotificationModels.cs`:
  - enum `NotificationKind { Info, Success, Warning, Promo, System, Reward }`.
  - enum `NotificationAudienceKind { AllUsers, Role, SpecificUser }`.
  - record `NotificationDto(string Id, NotificationKind Kind, string? Tag, string Title, string Body,
    DateTimeOffset CreatedAtUtc, DateTimeOffset? ScheduledForUtc, DateTimeOffset? PublishedAtUtc,
    DateTimeOffset? ExpiresAtUtc, bool IsRead)`.
  - record `CreateNotificationRequest(NotificationAudienceKind AudienceKind, string? AudienceValue,
    NotificationKind Kind, string? Tag, string Title, string Body, DateTimeOffset? ScheduledForUtc,
    DateTimeOffset? ExpiresAtUtc)`.
  - record `GrantRewardRequest(string TargetUserId, int Days, string Reason)`.
  - record `NotificationFeed(IReadOnlyList<NotificationDto> Items, int UnreadCount)`.
- فایل `INotificationService.cs`:
  - `Task<NotificationFeed> GetFeedAsync(bool unreadOnly = false, CancellationToken ct = default);`
  - `Task<int> GetUnreadCountAsync(CancellationToken ct = default);`
  - `Task MarkReadAsync(string notificationId, CancellationToken ct = default);`
  - `Task MarkAllReadAsync(CancellationToken ct = default);`
  - `Task CreateAsync(CreateNotificationRequest request, CancellationToken ct = default);` (نیازمند مجوز مدیر)
  - `Task GrantRewardAsync(GrantRewardRequest request, CancellationToken ct = default);` (نیازمند مجوز مدیر)
  - `bool CanManage { get; }` — آیا کاربر جاری اجازهٔ ساخت/جایزه دارد (برای UX).
  - رویداد `event EventHandler? FeedChanged;` — برای به‌روزرسانی بلادرنگ UI (یا از IDataChangeNotifier استفاده شود؛ یکی را انتخاب و سازگار بمان — پیشنهاد: IDataChangeNotifier).
- نکات: فقط DTO و اینترفیس؛ هیچ وابستگی به EF/Desktop. XML-doc فارسی/انگلیسی کوتاه مثل بقیهٔ Contracts.
- **push:** `scripts/git/push.sh "feat(contracts): add notifications service interface + DTOs"`

### T2 — لایهٔ Persistence (SQLite): Entity + Configuration + Migration 040 `[ ]`
- Entity ها در `src/Animora.Persistence.Sqlite/Entities/`:
  - `NotificationRow.cs` — `[LocalOnly] sealed class`, ctor خصوصی + یک factory `Create(...)` یا ctor با پارامتر
    (الگوی موجود: private set properties). ستون‌ها دقیقاً مطابق «تصمیم طراحی» بخش 0:
    `Id,string(26)` · `TenantId,string(26)` · `AudienceKind,int(enum)` · `AudienceValue,string? ` ·
    `Kind,int` · `Tag,string?` · `Title,string` · `Body,string` · `CreatedByUserId,string(26)` ·
    `CreatedAtUtc,DateTimeOffset` · `ScheduledForUtc,DateTimeOffset?` · `PublishedAtUtc,DateTimeOffset?` ·
    `ExpiresAtUtc,DateTimeOffset?`.
  - `NotificationReceiptRow.cs` — `[LocalOnly] sealed class`: `TenantId,string(26)` · `UserId,string(26)` ·
    `NotificationId,string(26)` · `ReadAtUtc,DateTimeOffset?`.
- Configuration در `src/Animora.Persistence.Sqlite/Configurations/Notifications/NotificationConfigurations.cs`
  (می‌توان هر دو config را در یک فایل گذاشت مثل Activity):
  - `NotificationConfiguration : IEntityTypeConfiguration<NotificationRow>`: `HasKey(e=>e.Id)`؛
    ایندکس `(TenantId, PublishedAtUtc)` نام `IX_Notifications_TenantId_PublishedAtUtc`؛
    ایندکس `(TenantId, ScheduledForUtc)` نام `IX_Notifications_TenantId_ScheduledForUtc`.
  - `NotificationReceiptConfiguration : IEntityTypeConfiguration<NotificationReceiptRow>`:
    `HasKey(e => new {e.TenantId, e.UserId, e.NotificationId})`.
- Migration `040`:
  - **ترجیح:** با ابزار EF ساخته شود تا Designer + snapshot درست تولید شوند:
    `cd src/Animora.Persistence.Sqlite && dotnet ef migrations add 040_Notifications` (اگر dotnet-ef نصب نیست:
    `dotnet tool restore` یا استفاده از الگوی دستی زیر). نام فایل باید فرمت `<timestamp>__040_Notifications.cs`
    بگیرد (مطابق 039). اگر EF اسم را بدون `__` ساخت، دستی به الگوی repo هماهنگ کن یا همان خروجی EF را نگه‌دار
    به‌شرط اینکه snapshot/Designer تولید شوند.
  - **اگر دستی ساختی:** سه فایل لازم است: `...__040_Notifications.cs` (Up/Down)،
    `...__040_Notifications.Designer.cs`، و به‌روزرسانی `AnimoraLocalDbContextModelSnapshot.cs`.
    Up: دو `CreateTable` (`Notifications`, `NotificationReceipts`) + دو ایندکس بالا + PKها. Down: دو `DropTable`.
    attributeها: `[DbContext(typeof(AnimoraLocalDbContext))]` + `[Migration("<timestamp>__040_Notifications")]`.
- **صحت‌سنجی:** فقط کامپایل پروژهٔ Persistence (نه full test). `dotnet build src/Animora.Persistence.Sqlite` یا بخشی از `scripts/sandbox/build.sh`.
- **push:** `scripts/git/push.sh "feat(persistence): notifications tables + migration 040"`

### T3 — لایهٔ Client.Services: پیاده‌سازی `NotificationService` `[ ]`
- پوشهٔ `src/Animora.Client.Services/Notifications/`.
- `NotificationService.cs : INotificationService` با constructor DI:
  وابستگی‌ها: `ILocalDbContextFactory`/DbContext (الگوی UserProfileService را ببین برای نحوهٔ دسترسی به store)،
  `ICurrentUserContext`، `ICapabilityGate` (برای مجوز)، و یک clock (مثل الگوهای موجود — `IClock`/`IClinicTimeZoneProvider`).
  - `GetFeedAsync`: پیام‌هایی که audience شامل کاربر جاری است، `PublishedAtUtc != null`، منقضی نشده؛
    join با receipts برای `IsRead`؛ مرتب‌سازی نزولی بر اساس `PublishedAtUtc`. `unreadOnly` فیلتر read.
    قبل از خواندن، `PublishPendingScheduled()` را صدا بزن (پیام‌های `ScheduledForUtc<=now && Published==null` را Published کن).
  - `GetUnreadCountAsync`: شمارش همان با فیلتر خوانده‌نشده.
  - `MarkReadAsync`/`MarkAllReadAsync`: upsert در `NotificationReceipts` با `ReadAtUtc=now`؛ سپس Notify(Notifications).
  - `CreateAsync`: **گارد مجوز** (اگر `!CanManage` → `throw new ServiceException(...)`)؛ ساخت `NotificationRow`؛
    اگر `ScheduledForUtc==null` → `PublishedAtUtc=now` (فوری)؛ ذخیره؛ Notify(Notifications)؛ (توست در UI).
  - `GrantRewardAsync`: گارد مجوز؛ ثبت محلی اشتراک/جایزه (فاز فعلی: استاب امن — کامنت TODO سرور Tracks D–E)؛
    سپس یک `Notification` نوع `Reward` برای `TargetUserId` بساز (`AudienceKind=SpecificUser`, `AudienceValue=TargetUserId`,
    عنوان/متن شامل تعداد روز و دلیل)؛ ذخیره؛ Notify.
  - `CanManage`: از `ICapabilityGate.Can(PermissionCodes.SettingsManage یا UsersManage)` یا نقش مدیر.
    (کد مجوز دقیق را از `Animora.Domain.Identity.PermissionCodes` انتخاب کن — نزدیک‌ترین «manage» موجود؛
    اگر مجوز اختصاصی نبود از `SettingsManage`/`UsersManage` استفاده کن.)
  - تمام کوئری‌ها LocalOnly: فیلتر `TenantId` صریح (مثل CommandIdRemap) چون این جداول global filter ندارند.
- رعایت قواعد perf: بدون LINQ غیرضروری در hot-path شدید، `AsNoTracking` برای خواندن.
- **push:** `scripts/git/push.sh "feat(client-services): implement NotificationService (local)"`

### T4 — ثبت DI در `App.axaml.cs` `[ ]`
- در `ConfigureServices` (کنار بقیهٔ سرویس‌ها، الگوی موجود):
  - `services.AddSingleton<INotificationService, Animora.Client.Services.Notifications.NotificationService>();`
  - `services.AddSingleton<NotificationsPageViewModel>();` (بعد از ساخت VM در T5).
  - اگر مودال‌ها VM جدا دارند: `services.AddTransient<ComposeNotificationViewModel>();`,
    `services.AddTransient<GrantRewardViewModel>();`.
- **push:** `scripts/git/push.sh "chore(di): register notification service + page vm"`

### T5 — Desktop UI: صفحهٔ «اعلان‌ها و پیام‌ها» `[ ]`
- پوشهٔ `src/Animora.Desktop/Pages/Notifications/`.
- `NotificationsPageViewModel.cs : PageViewModelBase`:
  - ctor: `INotificationService`, `IToastService`, `IDialogService`, `ICurrentUserContext`, `IDataChangeNotifier`.
  - `Title="اعلان‌ها و پیام‌ها"`, `Eyebrow`/breadcrumb مناسب.
  - `ObservableCollection<NotificationItemVm> Items`؛ `UnreadCount`؛ `CanManage`؛ فیلترها (`ShowUnreadOnly`, فیلتر نوع/تگ).
  - `UseLoadStatus(LoadAsync)` و `RefreshOnDataChange(DataDomain.Notifications)` (الگوی PageViewModelBase).
  - `LoadAsync`: feed را بگیر، به item-vmها map کن (آیکن نوع، برش رنگ تگ، زمان نسبی جلالی مثل تاپ‌بار `FormatRelative`).
  - کامندها: `MarkReadCommand(item)`، `MarkAllReadCommand`، `OpenComposeCommand` (فقط اگر CanManage)،
    `OpenGrantRewardCommand` (فقط CanManage). باز کردن یک پیام → MarkRead + کاهش بِج.
  - subscribe به `IDataChangeNotifier`: با رویداد Notifications، reload و (برای پیام تازهٔ منتشرشده) توست.
- `NotificationItemViewModels.cs`: `NotificationItemVm` (Id, Kind, Tag, Title, Body, RelativeTime, IsRead,
  `IBrush TagBrush`, `IBrush KindAccent`, `string IconKey`). map نوع→رنگ/آیکن:
  Info→IconInfo/آبی · Success→IconCheckCircle/سبز · Warning→IconAlert/کهربایی · Promo→IconMegaphone/بنفش ·
  System→IconShield/خاکستری · Reward→IconCake یا IconSpark/طلایی. از برش‌های تم (`Resources/Theme/Colors.axaml`) استفاده کن؛
  برای مقادیر hex از `Controls.BrushCache.Solid("#...")` (مثل TopbarViewModel).
- `NotificationsPageView.axaml` + `.axaml.cs`:
  - Layout RTL (ارث از App). هدر: عنوان + شمارندهٔ خوانده‌نشده + دکمه‌های «خواندن همه»، «پیام جدید» (CanManage)،
    «اعطای جایزه» (CanManage). نوار فیلتر (همه/خوانده‌نشده). لیست با `ItemsControl`/`ListBox` کارت‌های پیام:
    آیکن نوع (Path با IconKey converter `Shell/IconKeyConverter.cs` یا StaticResource)، تگ رنگی (Border با TagBrush)،
    عنوان bold، متن، زمان نسبی، نقطهٔ خوانده‌نشده. استایل تمیز، فاصله‌گذاری از توکن‌های `Spacing`.
  - خالی‌بودن لیست: یک empty-state مودب.
- مودال‌ها (می‌توان ساده با `IDialogService.ShowModal` مثل CommandPalette در TopbarViewModel):
  - `ComposeNotificationView(.axaml/.cs) + ComposeNotificationViewModel`: فیلدهای مخاطب (ComboBox: همه/نقش/کاربر خاص)،
    نوع (Combo)، تگ (text)، عنوان، متن، زمان‌بندی اختیاری (JalaliDatePicker موجود در Controls + ساعت)، انقضا اختیاری.
    دکمهٔ ارسال → `INotificationService.CreateAsync` → بستن مودال + توست موفقیت.
  - `GrantRewardView + GrantRewardViewModel`: کاربر هدف (userId/نام)، تعداد روز، دلیل → `GrantRewardAsync`.
- **صحت‌سنجی:** `scripts/sandbox/build.sh` باید 0/0 باشد.
- **push:** `scripts/git/push.sh "feat(desktop): notifications & messages page + compose/reward modals"`

### T6 — Shell: اتصال هدر، سایدبار و بِج عددی `[ ]`
- **سایدبار nav:** در `src/Animora.Desktop/Shell/NavigationMenu.cs` یک آیتم اضافه کن، گروه «سیستم»
  (قبل از profile منطقی است): `new("notifications","اعلان‌ها و پیام‌ها","IconBell", typeof(NotificationsPageViewModel), "سیستم")`.
  using مربوط به namespace `Animora.Desktop.Pages.Notifications` را بالای فایل اضافه کن.
- **گیت دسترسی:** در `src/Animora.Desktop/Shell/NavPermissions.cs`:
  - به `ByKey`: `["notifications"] = PermissionCodes.PatientsRead` (خواندن اعلان برای همهٔ کاربرانِ دارای حداقل یک مجوز
    مجاز است؛ از یک read عمومی موجود استفاده کن تا همه ببینند — `PatientsRead` مثل dashboard). ساخت/جایزه جداگانه در سرویس گِیت می‌شود.
  - به `ByPage`: `[typeof(Pages.Notifications.NotificationsPageViewModel)] = "notifications"`.
- **بِج روی سایدبار:** `SidebarViewModel`/`NavEntryViewModel` را بررسی کن؛ اگر پراپرتی بِج ندارد، یک
  `Badge`/`int? BadgeCount` به `NavEntryViewModel` اضافه کن و در `SidebarView.axaml` یک Border کوچک قرمز
  با عدد (Persian digits) نمایش بده که `IsVisible` به `BadgeCount>0` وصل است. مقدار از `INotificationService.GetUnreadCountAsync`
  پر شود و با `IDataChangeNotifier`(Notifications) به‌روز شود.
- **هدر (TopbarViewModel.cs + TopbarView.axaml):**
  - ctor تاپ‌بار: `INotificationService` و `IDataChangeNotifier` را تزریق کن.
  - پراپرتی `[ObservableProperty] int _unreadCount;` و `bool HasUnread => UnreadCount>0;`.
  - `OpenNotifications()` را از `_toast.Show(...)` به ناوبری واقعی تغییر بده:
    `_navigation.NavigateTo<NotificationsPageViewModel>()` (امضای دقیق NavigateTo را از `INavigationService` بردار؛
    الگوی ناوبری موجود را در NavigationService ببین). پس از ناوبری، بِج توسط صفحه مدیریت می‌شود.
  - subscribe به `IDataChangeNotifier`(Notifications) → `UnreadCount = await GetUnreadCountAsync()` (marshal به UI thread مثل الگوهای موجود تاپ‌بار).
  - در `TopbarView.axaml` دکمهٔ زنگ: `Ellipse` قرمز ثابت فعلی را به یک **بِج عددی** تبدیل کن:
    Border کوچک گوشهٔ بالا-راست با `IsVisible="{Binding HasUnread}"` و `TextBlock` متن = عدد فارسیِ UnreadCount
    (مثلاً از `PersianNumberFormatter.ToPersianDigits`). استایل هماهنگ با تم (DangerBrush پس‌زمینه، متن سفید، گرد).
- **صحت‌سنجی:** `scripts/sandbox/build.sh` 0/0 + در صورت دست‌زدن به DI/شل یک اسموک `scripts/sandbox/run.sh` → `SMOKE RUN OK`.
- **push:** `scripts/git/push.sh "feat(shell): wire header bell + sidebar entry + unread badge"`

### T7 — پیام سیستمی «نزدیک شدن به اتمام اشتراک» `[ ]`
- در `NotificationService` یک متد `EnsureSubscriptionExpiryNoticesAsync()`:
  وضعیت اشتراک را از `IUserProfileService.GetAccountStatusAsync()` بگیر (الگوی موجود در Contracts/Profile).
  اگر تاریخ انقضا نزدیک است (مثلاً ≤ ۷ روز) و پیام سیستمیِ متناظر هنوز ساخته نشده (برای جلوگیری از تکرار
  یک کلید/تگ یکتا مثل `system:sub-expiry:<yyyyMMdd>` چک شود)، یک `Notification` نوع `System` بساز.
  این متد در `GetFeedAsync`/`GetUnreadCountAsync` یا تایمر دقیقه‌ای فراخوانی شود (idempotent).
- اگر `GetAccountStatusAsync` فیلد انقضا ندارد یا نامشخص است، این تسک را به حداقل برسان (کامنت TODO + گارد null)
  تا بیلد نشکند؛ هستهٔ مکانیزم مهم‌تر از داده واقعی است (سرور هنوز نیست).
- **push:** `scripts/git/push.sh "feat(notifications): system subscription-expiry notices"`

### T8 — بیلد نهایی + اسموک + جمع‌بندی `[ ]`
- `scripts/sandbox/build.sh` (Desktop) → انتظار **0 warning / 0 error**. اگر لازم شد `build.sh full`.
- `scripts/sandbox/run.sh` → انتظار `SMOKE RUN OK` (چون شل/DI تغییر کرده).
- رفع هر خطا/هشدار. **اگر فیکس سنگین شد، همان‌جا یک push جدا با پیام واضح بزن.**
- **push:** `scripts/git/push.sh "test(desktop): build 0/0 + smoke ok for notifications feature"`

### T9 — (تسک پایانی) حذف همین فایل پلن `[ ]`
- فقط همین فایل حذف شود: `rm Plan/NOTIFICATIONS-PLAN.md`.
  (به فایل‌های دیگر و `Plan/reports/**` و هیچ roadmap دیگری دست نزن.)
- **push:** `scripts/git/push.sh "chore(plan): remove completed notifications plan"`

---

## 2) یادآوری‌های اجرایی (قفل‌شده)
- بعد از **هر** تسک: `scripts/git/push.sh "<msg مناسب>"` — بدون استثنا.
- فیکس سنگین/کد زیاد حین یک تسک = یک push جداگانهٔ به‌موقع با پیام قابل‌فهم.
- بررسی هدفمند: فقط فایل‌های لازم را باز کن؛ full test/build طولانی ممنوع (همان build.sh/run.sh کافی است).
- کیفیت استاندارد: MVVM، وابستگی ViewModel فقط به Contracts، گارد مجوز در سرویس نه در View، RTL/تم/ارقام فارسی.
- اگر کد مجوز یا امضای دقیق (مثل `NavigateTo`) در لحظه فرق داشت، نزدیک‌ترین معادل موجود در همان فایل‌ها را
  انتخاب کن؛ آیکن/مجوز جدید نساز مگر واقعاً نبود.
