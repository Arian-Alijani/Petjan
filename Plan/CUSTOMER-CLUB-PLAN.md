# Plan — صفحه «باشگاه مشتریان» (Customer Club)

> **برای مدل اجراکننده:** این فایل self-contained است. تمام مسیرها، الگوها، امضای
> متدها و تصمیم‌های معماری از قبل استخراج شده‌اند. **بدون بررسی دوبارهٔ ریپو** شروع
> به پیاده‌سازی کن؛ فقط فایل‌هایی را باز کن که هر تسک صریحاً می‌گوید.
>
> **قانون قفل‌شدهٔ پوش:** بعد از تکمیل هر تسک، حتماً اجرا کن:
> `scripts/git/push.sh "type(scope): message"` — اگر exit code = 3 بود، ابزار
> `setup_github_environment` را صدا بزن و دوباره اجرا کن. **پوش نکردن = شکست.**
>
> شاخهٔ کاری: `genspark_ai_developer` (اگر نبودی: `scripts/git/push.sh -b genspark_ai_developer "..."`).

---

## 0) هدف و دامنه

یک تب/صفحهٔ جدید به نام **«باشگاه مشتریان»** در دسکتاپ اضافه می‌شود که شامل:

1. **تولدهای امروز** — بر اساس `Pet.BirthDateUtc` (تاریخ تولد حیوان)، حیواناتی که
   امروز (ماه/روز شمسی یا میلادی clinic-local) تولدشان است، همراه با نام صاحب و تلفن.
   - دکمهٔ **«ارسال پیامک تبریک»** (per-row) — فقط ظاهر + فرمان ViewModel که به یک
     متد seam صدا می‌زند؛ **بک‌اند واقعی ارسال نشود** (NotImplemented/toast «به‌زودی»).
   - سوییچ **«ارسال خودکار پیامک تولد»** — فقط ظاهر + bool در ViewModel، بدون persist.
2. **پرخرج‌ترین مشتریان** (most spending) — leaderboard بر اساس مجموع خرید lifetime.
3. **پرمراجعه‌ترین مشتریان** (most visits) — leaderboard بر اساس تعداد مراجعه/فاکتور.
4. **قابلیت‌های خلاقانه و کاربردی** — KPI cards بالای صفحه + بخش «مشتریان در خطر ریزش»
   (کسانی که مدت‌هاست مراجعه نکرده‌اند) — جزئیات در تسک‌ها.

تمرکز شدید روی **UX/UI**، آیکن‌پک هماهنگ، و یکپارچگی کامل با لایه‌های سرور/سینک.

---

## 1) حقایق معماری استخراج‌شده (ground truth — قبلاً بررسی شد، دوباره نگرد)

### جریان داده (قانون حاکم بر هر تغییر)
`View → ViewModel → Contracts interface (DI) → Client.Services impl → Application dispatch → Persistence (SQLite)`
- ViewModelها فقط به **interfaceهای Contracts** وابسته‌اند، نه سرویس concrete/EF.
- هیچ seed/mock در `Pages/**` نیست.
- افزودن نیاز دادهٔ صفحه = interface+DTO در `Contracts/<Area>/` → پیاده‌سازی در
  `Client.Services/<Area>/` روی dispatcher → ثبت در `App.axaml.cs`.
- RTL یک‌بار app-wide در `App.axaml` تنظیم شده؛ per-screen دوباره ست نکن.

### ساختار per-screen
`Pages/<Area>/`: `<Screen>View.axaml` (+ `.axaml.cs` code-behind باریک) و `<Screen>ViewModel.cs`.

### الگوی مرجع (کپی‌کن): ماژول **Statistics** تقریباً همین کار را می‌کند
`TopClient` و `GetTopClientsAsync` و `ClientStats` از قبل وجود دارند و مستقیماً برای
«پرخرج‌ترین» قابل‌استفاده‌اند. ساختار این ماژول را تقلید کن (نه کپی literal):
- `src/Animora.Contracts/Statistics/IStatisticsQueryService.cs` + `StatisticsModels.cs` + `StatisticsQueryException.cs`
- `src/Animora.Client.Services/Statistics/StatisticsQueryService.cs` (الگوی `QueryAsync`/`SendAsync` با `Task.Run` + scope-per-call) + `StatisticsMapping.cs`
- `src/Animora.Application/UseCases/Finance/StatisticsQueries.cs` (query records + handlers با `[RequiresPermission(...)]`)
- `src/Animora.Application/Ports/IStatisticsQueries.cs` (port read-side)
- `src/Animora.Application/UseCases/Finance/ReadModels/StatisticsReadModels.cs` (read models)
- `src/Animora.Persistence.Sqlite/Queries/StatisticsQueries.cs` (پیاده‌سازی EF)
- `src/Animora.Desktop/Pages/Statistics/*` — به‌ویژه `StatisticsClientsView.axaml` و `StatisticsClientsViewModel.cs` و `StatisticsPageViewModel.cs`

### امضاها و جزئیاتی که لازم داری (دقیقاً از کد فعلی)
- **Pet**: `src/Animora.Domain/Patients/Pet.cs` → `public DateTimeOffset? BirthDateUtc`, `public string OwnerId`, `Status` از نوع `PetStatus` (مقدار فعال = `PetStatus.Active`), `Species` (enum)، `Name`.
- **Owner**: `src/Animora.Domain/Patients/Owner.cs` → `FullName`, `PhonePrimary` (string، normalize‌شده), `RegisteredUtc`.
- **TopClientReadModel** (موجود): `(string OwnerId, string FullName, string Phone, long TotalSpendMinor, int InvoiceCount, DateTimeOffset? LastInvoiceUtc)` در `ReadModels/StatisticsReadModels.cs:57`.
- **TopClient** (Contracts DTO موجود): همان شکل در `StatisticsModels.cs:70`.
- **الگوی TopClients در Persistence** (`StatisticsQueries.cs:140`):
  ```csharp
  var top = await FinanceAnalytics.Invoices(db).Where(i => i.OwnerId != null)
      .GroupBy(i => i.OwnerId).Select(g => new { OwnerId = g.Key, Spend = g.Sum(i => i.TotalMinor),
          Count = g.Count(), Last = g.Max(i => i.PostedUtc) })
      .Where(g => g.Spend > 0).OrderByDescending(g => g.Spend).ThenBy(g => g.OwnerId).Take(take).ToListAsync(ct);
  // سپس join با db.Set<Owner>() برای FullName/PhonePrimary
  ```
  - **برای «پرمراجعه‌ترین»**: همین query ولی `OrderByDescending(g => g.Count)`. یعنی یک read-model
    مشترک کافی است؛ فقط ترتیب فرق می‌کند. (ساده‌ترین راه: یک query جدید `TopVisitorsQuery` که
    همان projection را با مرتب‌سازی روی Count برمی‌گرداند — read-model `TopClientReadModel` را
    بازاستفاده کن.)
- **`FinanceAnalytics.Invoices(db)`**: ledger دیدوپلیکیت‌شدهٔ local/server؛ فیلدها شامل `OwnerId`, `TotalMinor`, `PostedUtc`. (در همان فایل Persistence استفاده شده — نیازی به باز کردن FinanceAnalytics نیست مگر امضای دقیق لازم شود.)
- **Dispatch plumbing (کپی عیناً از StatisticsQueryService)**: `QueryAsync<TRead,TOut>` با `Task.Run`, `scopes.CreateAsyncScope()`, `ICurrentUserProvider.BuildQueryContext()`, `IDispatcher.QueryAsync(...)`. دلیل `Task.Run`: Microsoft.Data.Sqlite خواندن async را sync اجرا می‌کند؛ کل کار scoped را از UI thread دور نگه می‌دارد.
- **Exception**: الگوی `StatisticsQueryException` را تقلید کن (یک `CustomerClubQueryException` بساز یا — ترجیحاً برای کم‌کاری — از همان `StatisticsQueryException` استفاده کن اگر namespace اجازه دهد؛ تصمیم در تسک T3).

### PageViewModelBase (helperها — `src/Animora.Desktop/Shell/PageViewModelBase.cs`)
- `abstract partial class PageViewModelBase : ObservableObject`
- `Title`, `Eyebrow`, `Breadcrumbs` (قابل Add)
- `UseLoadStatus(Func<Task> refresh)` — وضعیت لود صفحه
- `RefreshOnDataChange(IDataChangeNotifier notifier, Action refresh, params DataDomain[] domains)` — صفحهٔ نمایان را rebind می‌کند؛ صفحهٔ مخفی را نه.
- `TrackLoadResultAsync(Func<Task<bool>> load)` / `TrackLoadAsync(Func<Task> load)`
- `virtual OnNavigatedTo()` / `OnNavigatedFrom()`
- در viewmodelهای بخش Statistics از `HasData`/`IsEmpty`/`Evict()`/`Invalidate()` استفاده شده (الگوی section). برای این صفحه می‌توان ساده‌تر عمل کرد (یک صفحهٔ واحد با چند کارت، بدون section-switcher) مگر تسک غیر از این بگوید.

### DataDomain (enum — `src/Animora.Desktop/Services/IDataChangeNotifier.cs`)
مقادیر موجود: `Appointments, Boarding, Cashier, Cheques, Dashboard, Finance, History, Inventory, Notifications, Patients, Purchasing, Sales, Vaccination, Visits`.
→ برای این صفحه refresh روی: `DataDomain.Sales, DataDomain.Cashier, DataDomain.Finance, DataDomain.Patients, DataDomain.Visits`.

### Permissions (`src/Animora.Domain/Identity/PermissionCodes.cs`)
- leaderboardهای مالی: `PermissionCodes.ReportsRead` (همان گیت Statistics).
- تولدها (دادهٔ بیمار): `PermissionCodes.PatientsRead`.
- `ICapabilityGate.Can(PermissionCodes.X)` برای گیت‌کردن بخش‌ها (الگو در `StatisticsPageViewModel` خط ۸۷).

### DI registration (`src/Animora.Desktop/App.axaml.cs` ~ خط ۵۳۷–۵۴۲)
الگو:
```csharp
services.AddSingleton<Animora.Contracts.<Area>.I<X>Service, Animora.Client.Services.<Area>.<X>Service>();
services.AddSingleton<Animora.Desktop.Pages.<Area>.<Screen>ViewModel>();
```
Persistence port با `services.TryAddScoped<IStatisticsQueries, StatisticsQueries>();` در
`src/Animora.Persistence.Sqlite/Context/LocalStoreServiceCollectionExtensions.cs:104` ثبت شده.
اگر پورت جدید بسازی، آن‌جا هم ثبت کن (یا به پورت موجود `IStatisticsQueries` متد اضافه کن — **ترجیح**: برای کم‌کردن سطح تغییر، متدهای جدید را به `IStatisticsQueries` موجود اضافه کن).

### Navigation (`src/Animora.Desktop/Shell/NavigationMenu.cs`)
آیتم‌ها static تعریف شده‌اند:
```csharp
new("<id>", "<label>", "<IconKey>", typeof(<X>ViewModel), "<GroupTitle>"),
```
گروه‌های موجود: `"مرکز فرمان"`, `"مدیریت کلینیک"`, `"کسب و کار"`, `"سیستم"`.
→ «باشگاه مشتریان» را در گروه **«کسب و کار»** (کنار «آمار») یا **«مدیریت کلینیک»** بگذار؛
   توصیه: **«مدیریت کلینیک»** چون مشتری‌محور است. آیکن: `IconSpark` یا `IconSmile` یا `IconCake`.
   (تصمیم نهایی در تسک T6.) حتماً `using Animora.Desktop.Pages.CustomerClub;` اضافه کن.
ViewLocator بر اساس نام convention (`<X>ViewModel` → `<X>View`) resolve می‌کند — پوشه را درست نام‌گذاری کن.

### آیکن‌پک موجود (`Resources/Theme/Icons.axaml` — فقط از این‌ها استفاده کن)
مرتبط: `IconCake` (تولد ⭐), `IconSms` (پیامک ⭐), `IconMegaphone`, `IconPhone`, `IconMobile`,
`IconUsers`, `IconUser`, `IconSmile`, `IconSpark`, `IconTrendUp`, `IconTrend`, `IconWallet`,
`IconCoins`, `IconReceipt`, `IconTarget`, `IconBell`, `IconHourglass`, `IconClock`, `IconHeart`?(نبود),
`IconPaw`, `IconCheckCircle`, `IconRefresh`, `IconCalendar`, `IconTag`.
→ آیکن جدید **نساز**؛ فقط از لیست بالا.

### استایل‌ها/کنترل‌های reusable موجود (از StatisticsClientsView)
- `Border.card`, `card-header`, `card-title`, `card-header-rule` (کلاس‌های کارت استاندارد)
- `controls:AdaptiveGrid MinItemWidth=.. MaxColumns=.. ColumnSpacing=.. RowSpacing=..`
- `controls:MetricBar Height=.. Fraction=.. Fill=.. TrackBrush=.. Radius=.. Opacity=..` (نوار رتبه)
- `controls:DonutRing`, `controls:BarChart`, `controls:SegmentedBar`, `BarSegment`, `DonutSlice`, `BarChartPoint`
- Brushها: `InkBrush, Ink2Brush, MutedBrush, Muted2Brush, Brand600Brush, Brand700Brush, Brand400Brush, LineBrush, PanelBrush, Bg2Brush, ChartTrackBrush`
- `{StaticResource IconKeyConverter}` برای `Path.Data="{Binding IconKey, Converter=...}"`
- فرمت‌: `PersianNumberFormatter.ToPersianDigits(...)`, و در Statistics یک helper `StatisticsFormat` هست (`Money`, `Count`, `Percent`, `JalaliShort`). برای این صفحه یک `CustomerClubFormat` کوچک بساز یا از همان الگو استفاده کن.
- تاریخ شمسی: `src/Animora.Desktop/Localization/JalaliCalendar.cs` (`JalaliCalendar.Today`, `AddDays`).

### Build / verify (این sandbox)
- کامپایل سریع (Desktop): `scripts/sandbox/build.sh` → باید **0 warning / 0 error**.
- کل solution: `scripts/sandbox/build.sh full`.
- **گرفتن Full test / بیلد طولانی ممنوع.** فقط build سریع بعد از تغییرات مرتبط.

---

## 2) طراحی لایه‌به‌لایه (تصمیم‌های قطعی — فکرشده، آماده)

### دادهٔ جدید لازم: فقط «تولدهای امروز»
«پرخرج‌ترین» از `GetTopClientsAsync` موجود می‌آید. «پرمراجعه‌ترین» = همان projection با مرتب‌سازی Count.
پس **تنها افزودهٔ واقعی دیتا، query تولدها است**. بقیه بازاستفاده/مرتب‌سازی مجدد.

**تصمیم port:** به `IStatisticsQueries` موجود دو متد اضافه کن:
- `Task<IReadOnlyList<TopClientReadModel>> GetTopVisitorsAsync(int take, CancellationToken ct);`
- `Task<IReadOnlyList<BirthdayPetReadModel>> GetTodaysBirthdaysAsync(int todayLocalDay, CancellationToken ct);`

و read-model جدید در `StatisticsReadModels.cs`:
```csharp
public sealed record BirthdayPetReadModel(string PetId, string PetName, string Species,
    string OwnerId, string OwnerName, string Phone, int AgeYears);
```
> منطق تولد: امروز clinic-local را به (month, day) تبدیل کن. چون تقویم رسمی برنامه شمسی
> است ولی `BirthDateUtc` میلادی ذخیره شده، ساده‌ترین و درست‌ترین رویکرد: مقایسهٔ
> **ماه/روز میلادیِ clinic-local امروز** با ماه/روز میلادی `BirthDateUtc` (هر دو در
> timezone کلینیک). از `zones.GetClinicTimeZone()` مثل بقیهٔ queryها استفاده کن.
> `AgeYears` = اختلاف سال‌های میلادی در clinic-local. فقط `Pet.Status == PetStatus.Active`.
> `todayLocalDay` را handler از clock/zone resolve می‌کند (مثل بقیه) — یا داخل خود
> persistence با `IClock`+zones. توصیه: handler پارامتر `todayLocalDay`/today را بسازد و پاس دهد.

**Contracts DTO** (در `StatisticsModels.cs` یا فایل جدید `CustomerClubModels.cs` داخل `Contracts/Statistics` یا پوشهٔ جدید `Contracts/CustomerClub`): 
> تصمیم: برای تمیزی و تفکیک دامنه، **پوشهٔ جدید `Contracts/CustomerClub/`** با یک interface
> اختصاصی `ICustomerClubQueryService`. این interface متدها را از `IStatisticsQueryService`
> صدا نمی‌زند بلکه مستقیم روی dispatcher (مثل StatisticsQueryService) کار می‌کند و به
> queryهای Application وصل می‌شود. DTOها: `BirthdayPet`, و بازاستفادهٔ `TopClient`.

**چرا interface جدا؟** چون یک تب مستقل است و seam تمیزتر؛ ولی برای کم‌کردن کد می‌توان
`TopClient` موجود از `Contracts.Statistics` را import و بازاستفاده کرد (import کن، DTO تکراری نساز).

**Application queries** (در `StatisticsQueries.cs` یا فایل جدید `CustomerClubQueries.cs` کنار آن):
- `TopVisitorsQuery(int Take=10) : IQuery<IReadOnlyList<TopClientReadModel>>` با `[RequiresPermission(ReportsRead)]`
- `TodaysBirthdaysQuery : IQuery<IReadOnlyList<BirthdayPetReadModel>>` با `[RequiresPermission(PatientsRead)]`
  + handler که today را از `IClock`+`IClinicTimeZoneProvider` resolve کرده و به port پاس می‌دهد.

**Client.Services**: `src/Animora.Client.Services/CustomerClub/CustomerClubQueryService.cs`
پیاده‌سازی `ICustomerClubQueryService` با **کپی عین plumbing** از `StatisticsQueryService`
(`Task.Run` + scope + context + dispatcher + map). متد SMS:
```csharp
Task SendBirthdayGreetingAsync(string ownerId, string petId, CancellationToken ct = default);
Task SetAutoBirthdaySmsAsync(bool enabled, CancellationToken ct = default); // یا فقط در VM
```
→ **بک‌اند پیاده نشود**: این متدها فعلاً `throw new NotSupportedException()` یا
`Task.CompletedTask` با کامنت `// TODO(backend): wire SMS gateway`. **بهتر**: اصلاً این دو را
به seam دیتا اضافه نکن؛ در ViewModel یک فرمان بگذار که toast «ارسال پیامک به‌زودی فعال می‌شود»
نشان دهد. این دقیقاً خواستهٔ کاربر است («فقط ظاهر»). پس SMS را در لایهٔ دیتا دست نزن.

### Desktop
پوشهٔ جدید: `src/Animora.Desktop/Pages/CustomerClub/`
- `CustomerClubPageViewModel.cs` (: PageViewModelBase)
- `CustomerClubPageView.axaml` (+ `.axaml.cs` باریک — فقط `InitializeComponent()`)
- record rowها: `BirthdayRow`, `TopClientRow` (مثل `StatisticsTopClientRow`)

---

## 3) تسک‌لیست اجرایی (به ترتیب — بعد از هر ✅ پوش کن)

> وضعیت‌ها: `[ ]` انجام‌نشده · `[~]` درحال‌انجام · `[x]` انجام‌شده
> **بعد از هر تسک: `scripts/git/push.sh "..."`**

### [x] T1 — Read-models + Port (Application)
- فایل `src/Animora.Application/UseCases/Finance/ReadModels/StatisticsReadModels.cs`:
  اضافه کن `BirthdayPetReadModel(string PetId, string PetName, string Species, string OwnerId, string OwnerName, string Phone, int AgeYears)`.
- فایل `src/Animora.Application/Ports/IStatisticsQueries.cs`: دو امضای جدید
  `GetTopVisitorsAsync(int take, ct)` و `GetTodaysBirthdaysAsync(int todayLocalDay, ct)` با xml-doc فارسی/انگلیسی کوتاه.
- **build سریع نزن هنوز** (پیاده‌سازی port ناقص است) → مستقیم T2.
- پوش: `scripts/git/push.sh "feat(app): add customer-club read models and query ports"`

### [x] T2 — Persistence (SQLite queries)
- فایل `src/Animora.Persistence.Sqlite/Queries/StatisticsQueries.cs`:
  - `GetTopVisitorsAsync`: کپی بدنهٔ `GetTopClientsAsync` ولی `OrderByDescending(g => g.Count).ThenByDescending(g => g.Spend)` و فیلتر `g.Count > 0`.
  - `GetTodaysBirthdaysAsync(int todayLocalDay, ct)`: 
    - `todayLocalDay` را به (month, day) میلادی تبدیل کن (یا today را از clinic tz بگیر).
    - `db.Set<Pet>().AsNoTracking().Where(p => p.Status == PetStatus.Active && p.BirthDateUtc != null)`
    - مقایسهٔ ماه/روز clinic-local (چون EF روی `DateTimeOffset.Month/.Day` ممکن است ترجمه نشود،
      ایمن‌ترین راه: محدودهٔ لازم را واکشی و در حافظه فیلتر/محاسبهٔ clinic-local با `TimeZoneInfo.ConvertTime`.
      برای جلوگیری از full-scan بزرگ، چون تعداد Petها در یک کلینیک محدود است، واکشی `Id,Name,Species,OwnerId,BirthDateUtc` قابل‌قبول است).
    - join با `Owner` برای `FullName` و `PhonePrimary` (مثل الگوی TopClients).
    - `AgeYears` = سال clinic-local امروز − سال تولد clinic-local.
    - مرتب‌سازی بر اساس نام یا سن؛ `ThenBy(OwnerId)` برای پایداری.
- build سریع: `scripts/sandbox/build.sh` → باید 0/0. اگر خطای ترجمهٔ LINQ، به in-memory filter سوییچ کن.
- پوش: `scripts/git/push.sh "feat(persistence): top-visitors + today's birthdays queries"`

### [x] T3 — Application query records + handlers
- فایل جدید `src/Animora.Application/UseCases/Finance/CustomerClubQueries.cs` (کنار StatisticsQueries):
  - `TopVisitorsQuery(int Take=10) : IQuery<IReadOnlyList<TopClientReadModel>>` + handler (مثل `TopClientsQueryHandler`) با `[RequiresPermission(PermissionCodes.ReportsRead)]`.
  - `TodaysBirthdaysQuery : IQuery<IReadOnlyList<BirthdayPetReadModel>>` + handler با `[RequiresPermission(PermissionCodes.PatientsRead)]` که today را از `IClock`+`IClinicTimeZoneProvider` resolve و به port پاس می‌دهد (از `StatisticsWindowResolver`/`StatisticsQuerySupport` الگو بگیر یا today-day را مستقیم بساز).
- اطمینان از اینکه handlerها auto-register می‌شوند (الگوی موجود: handlerهای همین فایل Finance خودکار توسط dispatcher پیدا می‌شوند؛ اگر `FinanceRegistration.cs` دستی ثبت می‌کند، آن‌جا اضافه کن — **چک کن** `src/Animora.Application/UseCases/Finance/FinanceRegistration.cs` فقط همین یک فایل).
- build سریع → 0/0.
- پوش: `scripts/git/push.sh "feat(app): customer-club dispatcher queries + handlers"`

### [x] T4 — Contracts (seam دیتا)
- پوشهٔ جدید `src/Animora.Contracts/CustomerClub/`:
  - `CustomerClubModels.cs`: `BirthdayPet(string PetId, string PetName, string Species, string OwnerId, string OwnerName, string Phone, int AgeYears)`. `TopClient` را از `Animora.Contracts.Statistics` import و بازاستفاده کن (DTO تکراری نساز).
  - `ICustomerClubQueryService.cs`:
    ```csharp
    Task<IReadOnlyList<BirthdayPet>> GetTodaysBirthdaysAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Statistics.TopClient>> GetTopSpendersAsync(int take, CancellationToken ct = default);
    Task<IReadOnlyList<Statistics.TopClient>> GetTopVisitorsAsync(int take, CancellationToken ct = default);
    ```
  - exception: از `Animora.Contracts.Statistics.StatisticsQueryException` استفاده کن (import) تا کد تکراری نسازی؛ یا در صورت namespace نامناسب، یک `CustomerClubQueryException` کپی‌کن. **ترجیح: بازاستفاده از StatisticsQueryException.**
- پوش: `scripts/git/push.sh "feat(contracts): customer-club query seam"`

### [x] T5 — Client.Services (adapter روی dispatcher)
- پوشهٔ جدید `src/Animora.Client.Services/CustomerClub/CustomerClubQueryService.cs`:
  - پیاده‌سازی `ICustomerClubQueryService`.
  - plumbing `QueryAsync` را **عیناً** از `StatisticsQueryService` کپی کن (`Task.Run`, `scopes.CreateAsyncScope`, `ICurrentUserProvider.BuildQueryContext`, `IDispatcher.QueryAsync`, map، و `Failure/Unexpected` با `ErrorMapping.FromException`).
  - map:
    - `GetTodaysBirthdaysAsync` → `TodaysBirthdaysQuery()` → map `BirthdayPetReadModel`→`BirthdayPet`.
    - `GetTopSpendersAsync(take)` → `TopClientsQuery(take)` (موجود) → map `TopClientReadModel`→`TopClient` (mapping موجود `StatisticsMapping` را import/استفاده کن یا دستی map کن).
    - `GetTopVisitorsAsync(take)` → `TopVisitorsQuery(take)` → همان map.
- build سریع → 0/0.
- پوش: `scripts/git/push.sh "feat(client-services): customer-club query service"`

### [x] T6 — DI registration + Navigation
- `src/Animora.Desktop/App.axaml.cs` (نزدیک خط ۵۳۷، بخش Statistics):
  ```csharp
  services.AddSingleton<Animora.Contracts.CustomerClub.ICustomerClubQueryService, Animora.Client.Services.CustomerClub.CustomerClubQueryService>();
  services.AddSingleton<Animora.Desktop.Pages.CustomerClub.CustomerClubPageViewModel>();
  ```
- `src/Animora.Persistence.Sqlite/...LocalStoreServiceCollectionExtensions.cs`: نیازی به ثبت جدید نیست (پورت موجود `IStatisticsQueries` استفاده شد). **تأیید کن** چیزی لازم نیست.
- `src/Animora.Desktop/Shell/NavigationMenu.cs`:
  - `using Animora.Desktop.Pages.CustomerClub;`
  - آیتم: `new("customer-club", "باشگاه مشتریان", "IconSpark", typeof(CustomerClubPageViewModel), "مدیریت کلینیک"),`
    (بعد از «پانسیون» یا جای مناسب در گروه «مدیریت کلینیک»).
- **هنوز build نزن** (View/VM وجود ندارند) → T7.
- پوش: `scripts/git/push.sh "feat(desktop): register customer-club page + nav entry"`

### [x] T7 — ViewModel (Desktop)
- `src/Animora.Desktop/Pages/CustomerClub/CustomerClubPageViewModel.cs`:
  - `: PageViewModelBase`، ctor تزریق: `ICustomerClubQueryService service, IStoreReadinessGate readiness, IToastService toast, IDataChangeNotifier changes, ICapabilityGate capabilities`.
  - `Title="باشگاه مشتریان"`, `Eyebrow="وفاداری و ارتباط با مشتری"`, Breadcrumbs `"مدیریت کلینیک"` + `"باشگاه مشتریان"`.
  - کالکشن‌ها: `RangeObservableCollection<BirthdayRow> Birthdays`, `TopClientRow` برای `TopSpenders` و `TopVisitors`.
  - rowها (record):
    - `BirthdayRow(string PetName, string Species, string IconKey, string OwnerName, string Phone, string Age, string PetId, string OwnerId)`
    - `TopClientRow(string Rank, string Name, string Phone, string Primary /*spend یا visits*/, string Secondary, double Fraction)`
  - `[ObservableProperty] bool _autoBirthdaySms;` (فقط UI؛ persist نشود) + کامنت `// UI-only, backend TODO`.
  - KPI props: `BirthdaysTodayCount`, `TopSpenderName`, `TopVisitorName`, و... (string فرمت‌شده).
  - `[RelayCommand] void SendBirthdaySms(BirthdayRow row)` → `toast.Show("ارسال پیامک تبریک به‌زودی فعال می‌شود.", Tone.Info)` یا مشابه. **بدون صدا زدن بک‌اند.**
  - `[RelayCommand] Task Retry()` → reload.
  - لود: `UseLoadStatus(() => ReloadAsync())`؛ در `ReloadAsync` سه read را concurrent با `Task.WhenAll` بگیر (`GetTodaysBirthdaysAsync`, `GetTopSpendersAsync(10)`, `GetTopVisitorsAsync(10)`) و در UI publish کن (مثل `StatisticsClientsViewModel.LoadCoreAsync`). خطا → toast + حفظ دادهٔ قبلی.
  - گیت capability: اگر `!capabilities.Can(ReportsRead)` بخش‌های مالی را مخفی کن (bool prop)؛ تولدها نیازمند PatientsRead.
  - `RefreshOnDataChange(changes, () => _ = ReloadAsync(), DataDomain.Sales, DataDomain.Cashier, DataDomain.Finance, DataDomain.Patients, DataDomain.Visits);`
  - `OnNavigatedTo` → اگر in-flight نیست reload.
  - فرمت اعداد/پول: از `PersianNumberFormatter` و الگوی `StatisticsFormat` (import یا helper کوچک محلی `CustomerClubFormat`). برای species→(نام فارسی+آیکن) از `SpeciesPresentation.For(...)` که در `Pages/Dashboard` استفاده شده (در `StatisticsClientsViewModel` با `using Animora.Desktop.Pages.Dashboard;`). همان را بازاستفاده کن.
- پوش: `scripts/git/push.sh "feat(desktop): customer-club view-model"`

### [ ] T8 — View (XAML) با تمرکز شدید روی UX/UI
- `src/Animora.Desktop/Pages/CustomerClub/CustomerClubPageView.axaml` (+ `.axaml.cs` باریک).
- چیدمان (RTL، از بالا به پایین)، همه با `Border.card` و header استاندارد:
  1. **نوار KPI** (`AdaptiveGrid MinItemWidth=220 MaxColumns=4`): «تولدهای امروز» (IconCake)، «پرخرج‌ترین مشتری» (IconWallet/IconCoins)، «پرمراجعه‌ترین» (IconTrendUp)، «کل اعضا/مشتریان فعال» (IconUsers). هر کارت: عدد بزرگ + برچسب + آیکن رنگی.
  2. **کارت «تولدهای امروز 🎂»** (IconCake در header):
     - toolbar کارت: سوییچ «ارسال خودکار پیامک تولد» (ToggleSwitch bind به `AutoBirthdaySms`) + برچسب.
     - اگر خالی: متن «امروز تولدی ثبت نشده است.» + حالت تصویری ملایم.
     - لیست `ItemsControl` روی `Birthdays`: هر row → آیکن species، نام حیوان + (species)، نام صاحب، تلفن (Persian digits)، سن، و **دکمهٔ «ارسال پیامک تبریک»** (IconSms، `Command=SendBirthdaySmsCommand`, `CommandParameter={Binding}`). استایل دکمه هماهنگ با دکمه‌های برنامه (کلاس دکمهٔ brand موجود — از یک صفحهٔ دیگر مثل Patients الگو بگیر؛ اگر کلاس آماده نبود از `Button` ساده با استایل inline هماهنگ با tokenها).
  3. **AdaptiveGrid دو ستونه** (`MaxColumns=2`):
     - کارت **«پرخرج‌ترین مشتریان»** (IconWallet): لیست با `MetricBar` (Fraction بر اساس نسبت به max)، rank، نام، تلفن، مبلغ، تعداد فاکتور، آخرین خرید — عیناً الگوی `StatisticsClientsView` «برترین مراجعین بر اساس مجموع خرید».
     - کارت **«پرمراجعه‌ترین مشتریان»** (IconTrendUp): همان الگو ولی مقدار اصلی = تعداد مراجعه/فاکتور.
  4. (خلاقانه، اختیاری ولی توصیه‌شده اگر وقت/بودجه اجازه داد — در غیر این‌صورت حذف): کارت راهنمای کوچک «قابلیت‌های باشگاه مشتریان» یا نشان‌های وفاداری (badge) کنار نام مشتری برتر. **اگر پیاده نشد، در XAML نگذار.**
- حالت‌های empty/loading هر بخش را مثل Statistics هندل کن (`IsVisible` روی boolها).
- فقط از Brush/Icon/Controls لیست‌شده در بخش ۱ استفاده کن.
- build سریع `scripts/sandbox/build.sh` → **0/0**. اگر لازم شد smoke-run (چون DI/nav تغییر کرد): `scripts/sandbox/run.sh` → `SMOKE RUN OK`.
- پوش: `scripts/git/push.sh "feat(desktop): customer-club page UI (birthdays, top spenders/visitors)"`

### [ ] T9 — بازبینی یکپارچگی + build نهایی سبک
- چک‌لیست سریع (بدون full test):
  - [ ] ViewModel فقط به interface Contracts وابسته است (نه concrete/EF).
  - [ ] آیکن‌ها همه از Icons.axaml موجودند (گرپ سریع روی keyهای استفاده‌شده).
  - [ ] permission gating درست (ReportsRead برای leaderboards، PatientsRead برای تولدها).
  - [ ] SMS هیچ بک‌اندی صدا نمی‌زند (فقط toast/UI).
  - [ ] nav entry و DI ثبت شده‌اند؛ ViewLocator نام‌گذاری convention رعایت شده.
  - [ ] `scripts/sandbox/build.sh` = 0/0.
- اگر در این مرحله فیکس سنگینی شد، جداگانه پوش کن با پیام واضح.
- پوش: `scripts/git/push.sh "chore(desktop): customer-club integration polish"`

### [ ] T10 — تسک پایانی: حذف این فایل پلن
- فقط همین فایل `Plan/CUSTOMER-CLUB-PLAN.md` را حذف کن. **به فایل‌های دیگر Plan/ و roadmap دست نزن.**
- `rm Plan/CUSTOMER-CLUB-PLAN.md`
- پوش: `scripts/git/push.sh "chore: remove customer-club plan after completion"`

---

## 4) یادداشت‌های ریسک/دام‌چاله (از قبل حل‌شده)
- **LINQ translation تولد**: مقایسهٔ Month/Day روی `DateTimeOffset` در SQLite ممکن است ترجمه
  نشود → اگر build/runtime خطا داد، Petها را با فیلدهای لازم واکشی و در حافظه clinic-local فیلتر کن.
- **`Task.Run` الزامی است** در Client.Services (SQLite async = sync). حذفش نکن.
- **DTO تکراری نساز**: `TopClient` و `StatisticsQueryException` موجود را import/بازاستفاده کن.
- **پورت جدید در DI**: چون به `IStatisticsQueries` موجود متد اضافه کردیم، ثبت DI پرسیستنس تغییر نمی‌کند.
- **View code-behind** باید فقط `InitializeComponent()` باشد (الگوی `StatisticsClientsView.axaml.cs`).
- **صفحه singleton** است (AddSingleton) → مثل Statistics مدیریت reload/OnNavigatedTo را رعایت کن.
- **Species presentation**: برای نام فارسی و آیکن species از `SpeciesPresentation.For(...)` در `Pages/Dashboard` (همان که Statistics استفاده می‌کند).
