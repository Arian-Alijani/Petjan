# گزارش بررسی فنی نرم‌افزار Animora

**نسخه گزارش:** ۱.۰  
**تاریخ:** ۲۰۲۶-۱۰-۰۷  
**دامنه بررسی:** کل سولوشن `Animora.sln` — ۱۰۷۴ فایل `*.cs`، ~۲۲۳ هزار خط کد  
**معماری:** .NET 10 · Avalonia UI · EF Core 10 · SQLite (WAL) · Clean Architecture  
**روش بررسی:** تحلیل ایستا (static analysis) + اعتبارسنجی نتایج build سشن قبلی  

> **یادداشت روش‌شناسی:** طبق درخواست، در این سشن هیچ build یا test جدیدی گرفته نشد. نتایج build از لاگ سشن قبلی استفاده شد:
> - تمام پروژه‌های backend/library با **۰ warning و ۰ error** کامپایل شدند (آنالایزرها فعال).
> - تنها خطای build، `OutOfMemoryException` در **کامپایلر XAML آوالونیا** روی پروژه Desktop بود که صرفاً ناشی از محدودیت RAM سندباکس (۹۸۵MB) است و **نه** یک خطای کد. کد C# پروژه Desktop با `AvaloniaXamlIlCompilation=false` با **۰ warning/۰ error** کامپایل شد.
> - **نتیجه:** مشکلات این کدبیس معماری/منطقی هستند، نه از نوعی که کامپایلر بگیرد.

---

## خلاصه مدیریتی

کدبیس Animora از نظر مهندسی **بالغ و باکیفیت** است: معماری تمیز، مستندسازی قوی (ARCHITECTURE/STACK/PERFORMANCE/AGENTS)، الگوهای async صحیح، WAL/busy_timeout/foreign_keys درست تنظیم‌شده، outbox race-safe و UnitOfWork منسجم. تعداد مشکلات واقعی کم ولی چند مورد **با اثر عملی** وجود دارد.

| شدت | تعداد | عناوین |
|------|-------|--------|
| 🔴 بالا | ۱ | نگاشت نادرست نقض محدودیت SQLite به خطای «انبار در دسترس نیست» |
| 🟠 متوسط | ۴ | نبود disposal در خروج برنامه، هزینه change-tracking فیلدهای JSON، شکنندگی deserialize رویدادها، رشد حافظه ViewModelهای singleton |
| 🟡 پایین | ۳ | کاهش hit-rate کش داشبورد در نوشتن همزمان، ریسک gap در شماره فاکتور خارج از UoW، JSON مبتنی بر reflection برای پروتکل sync آینده |
| ℹ️ اطلاعی | — | مواردی که در لاگ به‌عنوان «باگ» علامت خورده بود ولی در بازبینی **false-positive** بودند (بخش ۹) |

---

## 🔴 شدت بالا

### F-01 — نقض محدودیت‌های SQLite (UNIQUE/FK/CHECK) به‌اشتباه «StoreUnavailable» گزارش می‌شود
**فایل:** `src/Animora.Persistence.Sqlite/Repositories/LocalUnitOfWork.cs` — خطوط **۱۰۰–۱۱۲**

```csharp
private static async Task<Result> StoreAsync(Func<Task> operation)
{
    try { await operation(); return Result.Ok(); }
    catch (DbUpdateConcurrencyException) { return Result.Fail(ErrorCodes.ConcurrencyConflict); }
    catch (DbUpdateException ex) when (ex.InnerException is SqliteException)
    {
        return Result.Fail(ErrorCodes.StoreUnavailable);   // ← همه‌چیز را StoreUnavailable می‌کند
    }
    catch (SqliteException) { return Result.Fail(ErrorCodes.StoreUnavailable); }
}
```

**مشکل:** تمام `SqliteException`‌ها بدون بررسی `SqliteErrorCode` به یک خطای عمومی `StoreUnavailable` نگاشت می‌شوند. ولی:
- `SqliteErrorCode == 19` (SQLITE_CONSTRAINT) → نقض UNIQUE / FOREIGN KEY / CHECK،
- extended codeهای `2067` (UNIQUE) و `787` (FK) و … نشان‌دهنده **تعارض داده / باگ منطقی** هستند، نه قطعی/ناپایداری موقت انبار.

**دلیل اهمیت:**
1. یک ثبت فاکتور با شماره تکراری، یا ثبت رکوردی با FK نامعتبر، به کاربر پیام «انبار در دسترس نیست» می‌دهد — که **گمراه‌کننده** است و ممکن است باعث retry بیهوده شود.
2. مسابقه‌ی یکتایی (uniqueness race) هرگز به‌صورت `ConcurrencyConflict` سطح بالا نمی‌آید، پس منطق retry/conflict بالادست فعال نمی‌شود.
3. در لاگ‌های عملیاتی، باگ‌های واقعی داده زیر برچسب «outage موقت» پنهان می‌مانند.

> نکته: در جای دیگری از همین پروژه (`LocalStoreMaintenance.cs:409`) کدهای corruption یعنی `11` و `26` به‌درستی تفکیک شده‌اند — یعنی الگوی تفکیک `SqliteErrorCode` در پروژه شناخته‌شده است و صرفاً اینجا اعمال نشده.

**پیشنهاد اصلاح:** قبل از catch عمومی، یک catch اختصاصی برای constraint اضافه شود:
```csharp
catch (DbUpdateException ex) when (ex.InnerException is SqliteException { SqliteErrorCode: 19 })
{
    return Result.Fail(ErrorCodes.ConcurrencyConflict); // یا یک کد جدید: ConstraintViolation
}
catch (SqliteException { SqliteErrorCode: 19 })
{
    return Result.Fail(ErrorCodes.ConcurrencyConflict);
}
```
(یا افزودن `ErrorCodes.ConstraintViolation` اختصاصی برای تمایز دقیق‌تر.)

---

## 🟠 شدت متوسط

### F-02 — ServiceProvider و workerهای IAsyncDisposable هنگام خروج برنامه dispose نمی‌شوند
**فایل:** `src/Animora.Desktop/App.axaml.cs` — خطوط **۸۰، ۸۶، ۲۰۲–۲۰۶** (و کل چرخه‌ی lifecycle)

```csharp
public static IServiceProvider Services { get; private set; } = default!;   // خط 80
// ...
Services = ConfigureServices();                                             // خط 86
// ...
private static IServiceProvider ConfigureServices()
{
    // ...
    return services.BuildServiceProvider();                                 // خط 206
}
```

**مشکل:** هیچ هوک `desktop.ShutdownRequested`/`OnExit` یا فراخوانی `DisposeAsync()` روی `Services` وجود ندارد. در نتیجه:
- `FinanceProjectionWorker` و `MaintenanceScheduler` (هر دو `IAsyncDisposable` با loop پس‌زمینه و `CancellationTokenSource`) هرگز به‌صورت تمیز dispose نمی‌شوند.
- `ServiceProvider` ریشه dispose نمی‌شود → همه‌ی singletonهای `IDisposable` بسته نمی‌شوند.

**دلیل اهمیت:** هرچند OS با پایان process منابع را آزاد می‌کند، اما:
1. **checkpoint نهایی WAL** و flush تمیز SQLite ممکن است انجام نشود → در خروج‌های سریع احتمال ماندن فایل `-wal` بزرگ.
2. loopهای پس‌زمینه تا لحظه‌ی kill شدن process ادامه می‌یابند (نوشتن/projection ناتمام).
3. در تست‌های یکپارچه یا اجرای چندنمونه‌ای، نشت handle و رفتار نامعین.

**پیشنهاد:** در جایی که `ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop`:
```csharp
desktop.ShutdownRequested += async (_, _) =>
{
    if (Services is IAsyncDisposable ad) await ad.DisposeAsync();
    else if (Services is IDisposable d) d.Dispose();
};
```

---

### F-03 — ValueComparer فیلدهای JSON روی هر SaveChanges سریالایز پرهزینه اجرا می‌کند
**فایل:** `src/Animora.Persistence.Sqlite/Conventions/JsonFields.cs` — خطوط **۱۷–۲۰**

```csharp
property.Metadata.SetValueComparer(new ValueComparer<List<TItem>>(
    (left, right) => JsonSerializer.Serialize(left, (JsonSerializerOptions?)null)
                  == JsonSerializer.Serialize(right, (JsonSerializerOptions?)null),   // Equals: 2× serialize
    value => StringComparer.Ordinal.GetHashCode(
                  JsonSerializer.Serialize(value, (JsonSerializerOptions?)null)),      // GetHashCode: 1× serialize
    value => JsonSerializer.Deserialize<List<TItem>>(
                  JsonSerializer.Serialize(value, (JsonSerializerOptions?)null), ...)  // Snapshot: serialize + deserialize
));
```

**مشکل:** EF Core از این comparer برای **تشخیص تغییر** استفاده می‌کند. در هر `DetectChanges`/`SaveChanges`، برای هر entity که فیلد JSON دارد:
- `Snapshot` یک serialize + deserialize کامل انجام می‌دهد،
- `Equals` تا دو serialize،
- `GetHashCode` یک serialize.

برای aggregateهای بزرگ (مثلاً لیست آیتم‌های فاکتور یا رکوردهای آزمایش)، این یعنی چند بار پیمایش و تخصیص رشته JSON در هر ذخیره — بر خلاف روح `PERFORMANCE.md` (پرهیز از کار تکراری/تخصیص در مسیر داغ).

**دلیل اهمیت:** هزینه‌ی CPU و GC pressure روی مسیر نوشتن که معمولاً روی thread کاربر یا worker اجرا می‌شود.

**پیشنهاد:** یک `JsonSerializerOptions` استاتیک و cache‌شده استفاده شود (به‌جای `null` که هر بار default را resolve می‌کند)، و در صورت امکان مقایسه‌ی ساختاری ارزان‌تر (structural) یا نگه‌داری نسخه‌ی serialize‌شده‌ی قبلی به‌جای serialize مجدد.

---

### F-04 — Deserialize رویدادهای outbox بدون options و با drop بی‌صدای typeهای ناشناخته
**فایل:** `src/Animora.Infrastructure.Local/Workers/FinanceProjectionWorker.cs` — خطوط **۸۰–۸۳+** و `Serialize` در `OutboxRelayHandler.cs`

```csharp
private static DomainEvent? Deserialize(OutboxMessage m) => m.MessageType switch
{
    nameof(InvoicePostedEvent) => JsonSerializer.Deserialize<InvoicePostedEvent>(m.PayloadJson),  // بدون options
    nameof(InvoiceVoidedEvent) => JsonSerializer.Deserialize<InvoiceVoidedEvent>(m.PayloadJson),
    // ...
    _ => null        // ← هر MessageType ناشناخته بی‌صدا null می‌شود و پیام drain می‌شود
};
```
و سمت نوشتن:
```csharp
// OutboxPayload.Serialize
public static string Serialize(object payload) => JsonSerializer.Serialize(payload, payload.GetType());
```

**مشکل:**
1. **عدم تقارن options:** نوشتن با `payload.GetType()` و پیش‌فرض، خواندن با پیش‌فرض (case-sensitive). امروز کار می‌کند چون هر دو پیش‌فرض‌اند و typeها concrete هستند، اما **شکننده** است: اگر رویدادی polymorphism یا property با نام‌گذاری متفاوت پیدا کند، round-trip بی‌صدا شکست می‌خورد.
2. **drop بی‌صدا:** شاخه‌ی `_ => null` به‌معنای این است که اگر نوع رویداد جدیدی اضافه شود ولی اینجا ثبت نشود، پیام **بدون هیچ لاگ/هشداری** مصرف و دور ریخته می‌شود → از دست رفتن projection مالی.

**دلیل اهمیت:** در حوزه‌ی مالی، از دست رفتن بی‌صدای یک رویداد = واگرایی داده‌ی حسابداری بدون ردپا.

**پیشنهاد:** 
- یک `JsonSerializerOptions` مشترک و cache‌شده بین serialize/deserialize.
- در شاخه‌ی `_ =>`، به‌جای `null`، لاگ هشدار + شمارش متریک (یا ارجاع به dead-letter) تا typeهای ثبت‌نشده قابل مشاهده باشند.

---

### F-05 — ViewModelهای صفحه به‌صورت Singleton؛ رشد یکنواخت حافظه با بازدید صفحات
**فایل:** `src/Animora.Desktop/App.axaml.cs` — خطوط **۲۷۸، ۲۸۲، ۲۸۸، ۳۰۹، ۳۱۳ …**

```csharp
services.AddSingleton<DashboardPageViewModel>();
services.AddSingleton<ReceptionPageViewModel>();
services.AddSingleton<PatientsPageViewModel>();
services.AddSingleton<VisitsTriagePageViewModel>();
services.AddSingleton<VaccinationPageViewModel>();
// ...
```

**مشکل:** همه‌ی ViewModelهای صفحه singleton هستند. پیامدها:
- هر `ObservableCollection` و داده‌ی بارگذاری‌شده‌ی هر صفحه پس از اولین بازدید **تا پایان عمر برنامه در حافظه می‌ماند** و هرگز آزاد نمی‌شود.
- در یک برنامه‌ی درمانگاهی که تمام روز باز است و اپراتور ده‌ها بار بین صفحات جابه‌جا می‌شود، مصرف حافظه به‌صورت یکنواخت (monotonic) رشد می‌کند.

**نکات مثبت (که این را از «باگ» به «ریسک طراحی» تنزل می‌دهد):**
- چون singleton است، self-reference تایمر (`_refreshTimer.Tick += OnRefreshTick`) **نشت واقعی نیست**.
- تایمرها در `OnNavigatedFrom` درست `Stop()` می‌شوند (تأییدشده در `DashboardPageViewModel`/`AppointmentsCalendarViewModel`).
- captive dependency وجود ندارد: ViewModel singleton سرویس singleton‌ای مثل `IDashboardQueryService` می‌گیرد که داخلاً از `IServiceScopeFactory` برای کار DB استفاده می‌کند — طراحی درست.

**پیشنهاد:** برای صفحات داده‌سنگین، در `OnNavigatedFrom` کالکشن‌های بزرگ `Clear()` شوند، یا این ViewModelها به `Transient`/`Scoped` با بازسازی در navigation تبدیل شوند تا داده پس از ترک صفحه آزاد شود.

---

## 🟡 شدت پایین

### F-06 — کاهش hit-rate کش داشبورد هنگام نوشتن همزمان (نه باگ، بلکه trade-off)
**فایل:** `src/Animora.Client.Services/Dashboard/DashboardQueryService.cs` — خطوط **۷۰–۸۴**

منطق: probe با `version = VersionAsync(...)` کلید ساخته می‌شود (خط ۷۳) ولی ذخیره با `result.Value.Generation` (خط ۸۴). این **عمدی و درست** است (کامنت خط ۸۲–۸۳): اگر بین probe و read یک write بیفتد، entry زیر نسل جدیدتر ذخیره می‌شود تا داده‌ی stale تحویل نشود. 

تنها اثر جانبی: در بار نوشتن مکرر همزمان، `version != Generation` و cache miss رخ می‌دهد (کش عملاً دور زده می‌شود). در حالت عادی hit رخ می‌دهد. 

**پیشنهاد:** صرفاً مستند بماند؛ در صورت نیاز، یک متریک برای نرخ miss داشبورد افزوده شود تا اگر زیر بار سنگین نوشتن کش بی‌اثر شد، قابل مشاهده باشد. (کش `TenantScopedCache` یک LRU با `capacity=16` و bounded است — خلاف برداشت اولیه، نشت/رشد نامحدود ندارد.)

---

### F-07 — ریسک gap در شماره فاکتور اگر allocator خارج از UnitOfWork فراخوانی شود
**فایل:** `src/Animora.Persistence.Sqlite/Sales/InvoiceNumberAllocator.cs` (خط ۱۱، ۲۷) و `src/Animora.Persistence.Sqlite/Commands/LocalSequenceAllocator.cs` (خط ۱۱، ۱۹)، SQL در `PipelineSql.NextSequenceAsync`

**وضعیت فعلی (درست):** کامنت تصریح می‌کند allocator به **ambient transaction** می‌پیوندد؛ پس اگر ثبت فاکتور شکست بخورد، شماره مصرف/افشا نمی‌شود. upsert نیز race-safe است.

**ریسک نهفته:** `NextSequenceAsync` وقتی `db.Database.CurrentTransaction == null` باشد، روی اتصال مستقل اجرا می‌شود و شماره را **بلافاصله** مصرف می‌کند. اگر در آینده این allocator **خارج از** یک UoW صدا زده شود و فراخوان بعداً شکست بخورد، یک **gap در شماره فاکتور** به‌وجود می‌آید.

**پیشنهاد:** افزودن یک guard (مثلاً `Debug.Assert`/throw) که در صورت نبود ambient transaction هشدار دهد، تا این قرارداد به‌صورت صریح enforce شود.

---

### F-08 — سریالایز JSON مبتنی بر reflection در سراسر پروژه (۵۳ مورد) بدون source-generation
**فایل‌ها:** پراکنده — از جمله `src/Animora.Application/Activity/ActivityFeedMapper.cs`, `src/Animora.Persistence.Sqlite/Conventions/JsonFields.cs`, `JsonSets.cs`, `OutboxRelayHandler.cs` و …

**مشکل:** تمام سریالایزها reflection-based هستند و `JsonSerializerContext` (source-gen) وجود ندارد. برای یک برنامه‌ی desktop فعلی قابل قبول است، اما:
- اگر در آینده trimming/AOT فعال شود (برای کاهش حجم/حافظه)، reflection ناسازگار است.
- پروتکل wire برنامه‌ریزی‌شده‌ی sync با reflection کندتر و پرتخصیص‌تر خواهد بود.

**پیشنهاد:** برای مسیرهای داغ (outbox payload، JSON fields، و به‌ویژه پروتکل sync آینده) یک `JsonSerializerContext` با source-generation تعریف شود.

---

## ℹ️ موارد بررسی‌شده و سالم (برای اطمینان)

این بخش نشان می‌دهد چه چیزهایی بررسی و **تأیید سلامت** شدند — تا دامنه‌ی بررسی روشن باشد:

- **PRAGMAها:** `journal_mode=WAL`, `synchronous=NORMAL`, `busy_timeout` (clamp شده به ۰–۶۰۰۰۰)، `foreign_keys=ON` — درست (`SqliteConnectionInterceptor.cs`). تنها نکته‌ی جزئی: `journal_mode=WAL` روی هر باز شدن اتصال ست می‌شود (بی‌ضرر).
- **فراخوانی‌های blocking (`.Result`/`.Wait()`):** موارد موجود (`StatisticsClientsViewModel.cs:86`, `StatisticsSalesViewModel.cs:79-81`) همگی **بعد از** `await Task.WhenAll(...)` هستند → تسک‌ها قطعاً کامل شده‌اند، deadlock ندارند. (نکته‌ی جزئی code-quality: در تسک faulted، `.Result` استثنا را در `AggregateException` می‌پیچد.)
- **`async void`:** فقط در مرز رویداد lifecycle آوالونیا (`App.axaml.cs:138`) و event handlerهای UI — کنترل‌شده.
- **catch خالی:** همگی برای استثناهای خوش‌خیم/مورد انتظار (`OperationCanceledException` روی لغو، `ObjectDisposedException` روی teardown تایمر، `IOException` در `LocalFileStore`) — قابل قبول.
- **N+1:** در لایه‌ی persistence و client-services الگوی `await` داخل حلقه یا فیلتر client-side (`.ToList().Where()`) یافت نشد.
- **Outbox claim:** منطق lease/claim race-safe است (`OutboxClaim.cs`). تنها ریسک نهفته برای حالت چندنمونه‌ای (multi-instance) که فعلاً تک‌worker است.
- **UnitOfWork:** مدیریت transaction و dispatch رویداد منسجم است (تنها ایراد F-01).
- **Captive dependency:** یافت نشد؛ singletonها از `IServiceScopeFactory` برای کار DB استفاده می‌کنند.

---

## ℹ️ تصحیح برداشت‌های لاگ سشن قبلی (False-Positiveها)

برای صداقت فنی، مواردی که در لاگ سشن قبلی به‌عنوان «باگ/مشکل» علامت خورده بودند ولی در بازبینی دقیق **نادرست** بودند:

1. **«باگ cache key در DashboardQueryService»** → در واقع الگوی **anti-stale عمدی و درست** است (F-06). باگ نیست.
2. **«TenantScopedCache بدون bound → رشد نامحدود حافظه»** → نادرست. این کلاس یک **LRU با capacity=16** است (`TenantScopedCache.cs`)، کاملاً bounded.

---

## اولویت‌بندی اصلاحات پیشنهادی

| # | یافته | شدت | تلاش | اولویت |
|---|-------|------|------|--------|
| F-01 | تفکیک نقض constraint از StoreUnavailable | 🔴 بالا | کم | **۱** |
| F-04 | deserialize امن + لاگ drop رویداد مالی | 🟠 متوسط | کم | **۲** |
| F-02 | disposal تمیز در خروج برنامه | 🟠 متوسط | کم | **۳** |
| F-03 | cache کردن options و کاهش serialize تغییر‌یابی | 🟠 متوسط | متوسط | ۴ |
| F-05 | آزادسازی حافظه ViewModelهای صفحه | 🟠 متوسط | متوسط | ۵ |
| F-07 | guard برای ambient transaction در allocator | 🟡 پایین | کم | ۶ |
| F-08 | source-gen JSON برای مسیرهای داغ/sync آینده | 🟡 پایین | متوسط | ۷ |
| F-06 | متریک نرخ miss کش داشبورد | 🟡 پایین | کم | ۸ |

---

## جمع‌بندی

نرم‌افزار Animora از نظر کیفیت مهندسی در سطح بالایی قرار دارد؛ backend کاملاً warning-free کامپایل می‌شود و الگوهای concurrency/async/persistence عمدتاً درست پیاده شده‌اند. **تنها یافته‌ی با شدت بالا F-01** است که اثر عملی مستقیم بر تجربه‌ی کاربر و قابلیت اتکای لاگ‌های عملیاتی دارد و با تغییری کوچک قابل رفع است. چهار یافته‌ی متوسط عمدتاً به پایداری بلندمدت (حافظه، خاموشی تمیز، سلامت داده‌ی مالی) مربوط‌اند و بار فنی پایینی برای رفع دارند.
