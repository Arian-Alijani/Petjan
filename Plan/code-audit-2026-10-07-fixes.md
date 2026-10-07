# Plan — اصلاح یافته‌های CODE_AUDIT_2026-10-07

> **منبع فاز:** `docs/CODE_AUDIT_2026-10-07.md` (گزارش بررسی فنی Animora، ۸ یافته F-01..F-08).
> **هدف فاز:** پیاده‌سازی اصلاحات ۸ یافته به ترتیب اولویت گزارش.
> **این فایل self-contained است:** تمام مسیرها، شماره‌خطوط، snippetها و الگوهای تأییدشده‌ی موجود در کدبیس اینجا استخراج شده‌اند. برای شروع هر تسک نیازی به بررسی مجدد ریپو نیست؛ مستقیم فایل هدف را باز کن و طبق «اقدام» عمل کن.

## زمینه‌ی معماری (برای مدل تازه‌وارد)
- **استک:** .NET 10 · Avalonia UI · EF Core 10 · SQLite(WAL) · Clean Architecture. backend بدون warning کامپایل می‌شود. مشکلات معماری/منطقی‌اند نه کامپایلری.
- **الگوی Result:** عملیات‌ها `Result`/`Result<T>` برمی‌گردانند با `ErrorCode`. کاتالوگ خطا در `src/Animora.Domain/Results/ErrorCodes.cs`. کدهای موجود مرتبط: `ConcurrencyConflict = new("CONCURRENCY_CONFLICT", ErrorCode.Conflict)` و `StoreUnavailable = new("STORE_UNAVAILABLE", ErrorCode.Infrastructure)`. **کد `ConstraintViolation` وجود ندارد.**
- **نگاشت پیام فارسی:** `src/Animora.Desktop/Localization/ErrorMessages.cs` — خط ۸۳ کامنت صریح: کدهای `VALIDATION_FAILED`/`NOT_FOUND`/`CONCURRENCY_CONFLICT` عمداً نگاشت نمی‌شوند (پیام UI جداگانه دارند). خط ۸۵: `["STORE_UNAVAILABLE"] = "پایگاه داده در دسترس نیست؛ کمی بعد دوباره تلاش کنید."`.
- **قانون قفل‌شده‌ی پوش:** بعد از تکمیل هر تسک: `scripts/git/push.sh "type(scope): message"` اجرا شود. اگر خروجی کد ۳ داد → ابزار `setup_github_environment` یک بار صدا زده شود، بعد دوباره push. برنچ کاری باید `genspark_ai_developer` باشد (اولین push با `-b genspark_ai_developer "msg"`).

---

## قواعد اجرای فاز
1. هر تسک = یک commit + push مستقل.
2. ترتیب = اولویت گزارش: F-01 → F-04 → F-02 → F-03 → F-05 → F-07 → F-08 → F-06.
3. بیلد کامل/تست کامل **ممنوع** (محدودیت RAM سندباکس ۹۸۵MB؛ XAML compiler آوالونیا OOM می‌دهد). در صورت نیاز فقط بیلد پروژه‌ی تک (بدون XAML) مجاز است ولی غیرضروری است — تغییرات کوچک و هدفمندند.
4. اگر فیکس سنگین/حجیم شد → همان‌جا commit+push جدا.
5. تسک پایانی: حذف `docs/CODE_AUDIT_2026-10-07.md` و push. (به rebuild/roadmap کاری نداشته باش.)
6. فایل خود این پلن فقط در صورت درخواست صریح حذف شود؛ در این فاز حذف نمی‌شود (دستور کاربر: «در نهایت فقط فایل داخل plan حذف بشه بعد تکمیل اون» ناظر بر فایل فاز قبلی است — اینجا فایل فاز = `docs/CODE_AUDIT_2026-10-07.md`).

---

## [x] T1 — F-01 🔴 نگاشت نقض constraint به ConcurrencyConflict ✅ انجام و push شد
**فایل:** `src/Animora.Persistence.Sqlite/Repositories/LocalUnitOfWork.cs`
**متد هدف:** `StoreAsync` — خطوط **۱۰۰–۱۱۳** (کل بدنه).

**وضعیت فعلی (دقیقاً این است):**
```csharp
private static async Task<Result> StoreAsync(Func<Task> operation)
{
    try
    {
        await operation();
        return Result.Ok();
    }
    catch (DbUpdateConcurrencyException) { return Result.Fail(ErrorCodes.ConcurrencyConflict); }
    catch (DbUpdateException ex) when (ex.InnerException is SqliteException)
    {
        return Result.Fail(ErrorCodes.StoreUnavailable);
    }
    catch (SqliteException) { return Result.Fail(ErrorCodes.StoreUnavailable); }
}
```

**مشکل:** تمام `SqliteException`‌ها → `StoreUnavailable`. ولی `SqliteErrorCode == 19` (SQLITE_CONSTRAINT = UNIQUE/FK/CHECK) = تعارض داده/باگ منطقی، نه قطعی موقت. کاربر پیام گمراه‌کننده «انبار در دسترس نیست» می‌گیرد و retry بیهوده می‌کند؛ uniqueness race هرگز به `ConcurrencyConflict` بالا نمی‌آید.

**الگوی تأییدشده‌ی موجود در پروژه (همین pattern را تقلید کن):** `src/Animora.Persistence.Sqlite/Maintenance/LocalStoreMaintenance.cs` خط ~۴۱۱:
```csharp
SqliteException { SqliteErrorCode: 11 or 26 } => ErrorCodes.StoreCorrupt, // SQLITE_CORRUPT / SQLITE_NOTADB
```
یعنی تفکیک با `SqliteErrorCode` در پروژه شناخته‌شده و پذیرفته است.

**اقدام:** دو catch اختصاصی برای `SqliteErrorCode: 19` **قبل از** catchهای عمومی اضافه کن و به `ConcurrencyConflict` نگاشت کن (پیشنهاد گزارش خط ۵۸–۶۸؛ از ایجاد کد جدید `ConstraintViolation` صرف‌نظر می‌کنیم تا سطح پیام UI تغییر نکند — `CONCURRENCY_CONFLICT` عمداً نگاشت فارسی ندارد و منطق conflict بالادست را فعال می‌کند). بدنه‌ی نهایی:
```csharp
private static async Task<Result> StoreAsync(Func<Task> operation)
{
    try
    {
        await operation();
        return Result.Ok();
    }
    catch (DbUpdateConcurrencyException) { return Result.Fail(ErrorCodes.ConcurrencyConflict); }
    // SQLITE_CONSTRAINT (19) = UNIQUE/FK/CHECK violation → تعارض داده، نه قطعی موقت انبار.
    catch (DbUpdateException ex) when (ex.InnerException is SqliteException { SqliteErrorCode: 19 })
    {
        return Result.Fail(ErrorCodes.ConcurrencyConflict);
    }
    catch (DbUpdateException ex) when (ex.InnerException is SqliteException)
    {
        return Result.Fail(ErrorCodes.StoreUnavailable);
    }
    catch (SqliteException { SqliteErrorCode: 19 }) { return Result.Fail(ErrorCodes.ConcurrencyConflict); }
    catch (SqliteException) { return Result.Fail(ErrorCodes.StoreUnavailable); }
}
```
`using Microsoft.Data.Sqlite;` از قبل در فایل هست (خط ۷). نیازی به using جدید نیست.

**push:** `scripts/git/push.sh -b genspark_ai_developer "fix(persistence): map SQLite constraint violations to ConcurrencyConflict (F-01)"`

---

## [x] T2 — F-04 🟠 Deserialize امن رویدادهای outbox + عدم drop بی‌صدا ✅ انجام و push شد
**فایل:** `src/Animora.Infrastructure.Local/Workers/FinanceProjectionWorker.cs`
**هدف:** متد `Deserialize` خطوط **۸۰–۹۳** (ده شاخه + `_ => null`).

**وضعیت فعلی:** `JsonSerializer.Deserialize<T>(m.PayloadJson)` بدون options؛ شاخه‌ی `_ => null` یعنی هر `MessageType` ناشناخته **بی‌صدا** مصرف و drain می‌شود (`ProcessAsync` خط ۶۰–۶۵: اگر `domainEvent is null` فقط `CompleteAsync` صدا می‌خورد → پیام حذف می‌شود بدون لاگ). در حوزه‌ی مالی = واگرایی حسابداری بی‌ردپا.

**سمت نوشتن (برای تقارن options، فقط جهت اطلاع):** `src/Animora.Persistence.Sqlite/Outbox/OutboxRelayHandler.cs`:
```csharp
internal static class OutboxPayload
{
    public static string Serialize(object payload) => JsonSerializer.Serialize(payload, payload.GetType());
}
```
هر دو سمت امروز default (case-sensitive, concrete types) هستند → round-trip کار می‌کند. تغییر options الزامی نیست ولی **cache کردن یک `JsonSerializerOptions` مشترک** ریسک آینده را حذف می‌کند.

**ساختار `Deserialize` فعلی:** `static` و در امضا `OutboxMessage m` می‌گیرد. چون می‌خواهیم در شاخه‌ی ناشناخته لاگ بزنیم، باید به logger دسترسی داشته باشد → از `static` به instance method تبدیل کن (کلاس فیلد `ILogger<FinanceProjectionWorker> logger` را از primary ctor دارد — خط ۱۴). سپس در `ProcessAsync` خط ۶۰ `Deserialize(message)` بدون تغییر امضا (حالا instance call) کار می‌کند.

**اقدام (دقیق):**
1. در بالای کلاس یک options استاتیک cache‌شده اضافه کن (فیلد استاتیک کنار `_stop`):
```csharp
private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
```
> نکته: چون سمت نوشتن default (نه Web) است، برای **تقارن کامل** از `new JsonSerializerOptions()` (یعنی default) استفاده کن — نه Web — تا رفتار round-trip عیناً حفظ شود. انتخاب نهایی: `private static readonly JsonSerializerOptions SerializerOptions = new();`
2. `Deserialize` را instance و لاگ‌دار کن:
```csharp
private DomainEvent? Deserialize(OutboxMessage m) => m.MessageType switch
{
    nameof(InvoicePostedEvent) => JsonSerializer.Deserialize<InvoicePostedEvent>(m.PayloadJson, SerializerOptions),
    nameof(InvoiceVoidedEvent) => JsonSerializer.Deserialize<InvoiceVoidedEvent>(m.PayloadJson, SerializerOptions),
    nameof(PaymentRegisteredEvent) => JsonSerializer.Deserialize<PaymentRegisteredEvent>(m.PayloadJson, SerializerOptions),
    nameof(InvoiceRefundedEvent) => JsonSerializer.Deserialize<InvoiceRefundedEvent>(m.PayloadJson, SerializerOptions),
    nameof(GoodsReceiptPostedEvent) => JsonSerializer.Deserialize<GoodsReceiptPostedEvent>(m.PayloadJson, SerializerOptions),
    nameof(StockMovementRecordedEvent) => JsonSerializer.Deserialize<StockMovementRecordedEvent>(m.PayloadJson, SerializerOptions),
    nameof(ChequeClearedEvent) => JsonSerializer.Deserialize<ChequeClearedEvent>(m.PayloadJson, SerializerOptions),
    nameof(ChequeBouncedEvent) => JsonSerializer.Deserialize<ChequeBouncedEvent>(m.PayloadJson, SerializerOptions),
    nameof(CashSessionClosedEvent) => JsonSerializer.Deserialize<CashSessionClosedEvent>(m.PayloadJson, SerializerOptions),
    nameof(ExpensePostedEvent) => JsonSerializer.Deserialize<ExpensePostedEvent>(m.PayloadJson, SerializerOptions),
    _ => LogAndDrop(m),
};

// نوع ناشناخته هرگز بی‌صدا دور ریخته نشود: هشدار + شمارش قابل‌مشاهده.
private DomainEvent? LogAndDrop(OutboxMessage m)
{
    logger.LogWarning("Outbox message {MessageId} has unregistered type {MessageType}; draining without projection", m.Id, m.MessageType);
    return null;
}
```
> دلیل حفظ `return null`: رفتار drain فعلی عمدی است (کامنت خط ۹۲ «Outbox currently has no other consumer; drain unrelated events as well») — فقط باید **قابل مشاهده** شود، نه مسدود. drop همچنان اتفاق می‌افتد اما با `LogWarning`.
> `using Microsoft.Extensions.Logging;` از قبل هست (خط ۹). `System.Text.Json` از قبل هست (خط ۱).

**push:** `scripts/git/push.sh "fix(outbox): cache serializer options and log unregistered event types instead of silent drop (F-04)"`

---

## [x] T3 — F-02 🟠 Disposal تمیز ServiceProvider و workerها در خروج ✅ انجام و push شد
**فایل:** `src/Animora.Desktop/App.axaml.cs`
**هدف:** متد `OnFrameworkInitializationCompleted` بلوک `if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)` — خطوط **۱۰۳–۱۳۲**.

**مشکل:** `Services` (خط ۸۰، `BuildServiceProvider()` خط ۲۰۶) هرگز dispose نمی‌شود. `FinanceProjectionWorker` و `MaintenanceScheduler` هر دو `IAsyncDisposable` با loop پس‌زمینه و `CancellationTokenSource`‌اند و با خروج فقط kill می‌شوند → احتمال نماندن checkpoint نهایی WAL، loopهای ناتمام، نشت handle در تست چندنمونه‌ای.
- تأیید: `FinanceProjectionWorker : IAsyncDisposable` با `DisposeAsync` که `_stop.CancelAsync()` + `await _loop` می‌کند (خطوط ۹۵–۱۰۰ همان فایل worker).

**اقدام:** داخل همان بلوک `if (... desktop)`، یک هوک `ShutdownRequested` اضافه کن که root provider را dispose کند. چون `ServiceProvider` هم `IDisposable` هم `IAsyncDisposable` است، مسیر async را اول امتحان کن. محل مناسب: بعد از خط ۱۳۱ (`login.Opened += OnLoginOpened;`) و قبل از بسته‌شدن بلوک `}` در خط ۱۳۲:
```csharp
            login.Opened += OnLoginOpened;

            // F-02: روی خروج برنامه، root provider (و همه‌ی IDisposable/IAsyncDisposable singletonها
            // مثل FinanceProjectionWorker و MaintenanceScheduler) تمیز بسته می‌شوند تا WAL checkpoint
            // نهایی و توقف loopهای پس‌زمینه قطعی باشد.
            desktop.ShutdownRequested += (_, _) =>
            {
                switch (Services)
                {
                    case IAsyncDisposable ad: ad.DisposeAsync().AsTask().GetAwaiter().GetResult(); break;
                    case IDisposable d: d.Dispose(); break;
                }
            };
```
> چرا `GetAwaiter().GetResult()` و نه `async (_, _)`: `ShutdownRequested` یک رویداد sync است؛ `async void` اینجا باعث می‌شود disposal قبل از بسته‌شدن process تضمین نشود. در این نقطه‌ی پایانی blocking روی dispose امن است (برنامه در حال خروج است، deadlock UI موضوعیت ندارد چون کار پس‌زمینه است). الگوی `async void` در این فایل فقط برای `OnLoginOpened` (خط ۱۳۸) به‌عنوان مرز lifecycle مجاز شمرده شده — اینجا نباید تکرار شود.
> `using System;` (برای `EventArgs`) و DI usingها از قبل هستند. `IAsyncDisposable`/`IDisposable` در `System` فضای‌نام‌اند و ضمنی موجودند.

**نکته‌ی احتیاط:** مطمئن شو worker فقط وقتی `Start()` شده dispose مشکلی ندارد — `DisposeAsync` worker روی `_loop is null` امن است (خط ۹۸ شرط `if (_loop is not null)`). پس حتی اگر startup شکست خورده باشد، dispose بی‌خطر است.

**push:** `scripts/git/push.sh "fix(desktop): dispose root service provider and async workers on shutdown (F-02)"`

---

## [ ] T4 — F-03 🟠 Cache کردن JsonSerializerOptions در JsonFields + مقایسه‌ی ارزان‌تر
**فایل:** `src/Animora.Persistence.Sqlite/Conventions/JsonFields.cs` — کل فایل (۲۱ خط).

**وضعیت فعلی:** در `ValueComparer` (خطوط ۱۷–۲۰) هر `Equals` تا ۲ serialize، `GetHashCode` ۱ serialize، `Snapshot` یک serialize+deserialize کامل انجام می‌دهد؛ همه با `(JsonSerializerOptions?)null` که هر بار default را resolve می‌کند. روی هر `DetectChanges`/`SaveChanges` برای هر entity دارای فیلد JSON اجرا می‌شود → CPU/GC pressure روی مسیر داغ نوشتن.

**الگوی تأییدشده‌ی ارزان‌تر (در همین پوشه):** `src/Animora.Persistence.Sqlite/Conventions/JsonSets.cs` خطوط ۱۶–۲۰ — `ValueComparer<HashSet<string>>` بدون هیچ serialize:
```csharp
(left, right) => left != null && right != null && left.SetEquals(right),
value => value.OrderBy(code => code, StringComparer.Ordinal).Aggregate(0, (hash, code) => HashCode.Combine(hash, StringComparer.Ordinal.GetHashCode(code))),
value => new HashSet<string>(value, StringComparer.Ordinal)
```
یعنی رویکرد ساختاری (structural) در پروژه پذیرفته است.

**اقدام (دقیق، دو بخش):**
1. **options استاتیک cache‌شده** جایگزین همه‌ی `(JsonSerializerOptions?)null`‌ها شود (هم در `HasConversion` خط ۱۵–۱۶ هم در comparer). یک فیلد استاتیک:
```csharp
private static readonly JsonSerializerOptions Options = new();
```
2. **comparer ارزان‌تر** برای `List<TItem>`:
   - `Equals`: مقایسه‌ی ساختاری عنصربه‌عنصر با `SequenceEqual` به‌جای دو serialize. چون `TItem` ممکن است EqualityComparer پیش‌فرض نداشته باشد، امن‌ترین معادل ارزان‌تر بدون تغییر رفتار = **یک بار serialize طرفین و مقایسه‌ی رشته** (نصف هزینه‌ی فعلی) یا `SequenceEqual`. برای حفظ رفتار دقیق قبلی (مقایسه بر مبنای JSON canonical) ولی با نصف هزینه:
```csharp
property.Metadata.SetValueComparer(new ValueComparer<List<TItem>>(
    (left, right) => ReferenceEquals(left, right)
        || (left != null && right != null
            && JsonSerializer.Serialize(left, Options) == JsonSerializer.Serialize(right, Options)),
    value => value == null ? 0 : StringComparer.Ordinal.GetHashCode(JsonSerializer.Serialize(value, Options)),
    value => JsonSerializer.Deserialize<List<TItem>>(JsonSerializer.Serialize(value, Options), Options)!));
```
   > بهبود اصلی این تسک = **cache شدن `Options`** (حذف resolve مکرر default) + کوتاه‌مدار `ReferenceEquals`/null-guard که بیشتر فراخوانی‌های بی‌تغییر را بدون serialize رد می‌کند. تغییر Snapshot به clone ارزان‌تر (مثلاً `new List<TItem>(value)`) **فقط اگر** `TItem` immutable باشد امن است؛ چون `TItem` عمومی است و ممکن است mutable باشد، deep-clone از طریق JSON حفظ می‌شود (رفتار فعلی). پس Snapshot را تغییر نده مگر مطمئن.
3. `HasConversion` را هم به `Options` منتقل کن:
```csharp
.HasConversion(value => JsonSerializer.Serialize(value, Options),
    value => JsonSerializer.Deserialize<List<TItem>>(value, Options) ?? new List<TItem>());
```
> `using System.Text.Json;` از قبل خط ۱ هست.

**اختیاری هم‌راستا:** همین cache را در `JsonSets.cs` هم اعمال کن (`(JsonSerializerOptions?)null` در خط ۱۴–۱۵ → `Options`). کم‌هزینه و consistent.

**push:** `scripts/git/push.sh "perf(persistence): cache JsonSerializerOptions and cheapen JSON field change detection (F-03)"`

---

## [ ] T5 — F-05 🟠 آزادسازی حافظه‌ی ViewModelهای صفحه هنگام ترک صفحه
**فایل‌ها:** ثبت singletonها در `src/Animora.Desktop/App.axaml.cs` خطوط ۲۷۸، ۲۸۲، ۲۸۸، ۳۰۹، ۳۱۳، ۳۳۷، ۳۴۳، ۳۵۸، ۳۶۳، ... (همه‌ی `services.AddSingleton<XxxPageViewModel>()`).

**مشکل (طبق گزارش، سطح «ریسک طراحی» نه باگ):** ViewModelهای صفحه singleton‌اند؛ `ObservableCollection`‌های داده‌سنگین پس از اولین بازدید تا پایان عمر برنامه در حافظه می‌مانند. در برنامه‌ی تمام‌روزِ درمانگاهی رشد monotonic حافظه.
- **نکات تأییدشده که این را به ریسک تنزل می‌دهند:** self-reference تایمر نشت واقعی نیست (singleton)، تایمرها در `OnNavigatedFrom` درست `Stop()` می‌شوند، captive dependency وجود ندارد (سرویس‌ها از `IServiceScopeFactory` استفاده می‌کنند).

**پیشنهاد گزارش (دو گزینه):**
- (الف) کم‌ریسک‌ترین: در `OnNavigatedFrom` کالکشن‌های بزرگ `Clear()` شوند.
- (ب) پرریسک‌تر: تبدیل به Transient/Scoped با بازسازی در navigation.

**تصمیم این فاز = گزینه‌ی (الف)** (کم‌ریسک، منطبق با نکته‌ی «تایمرها در `OnNavigatedFrom` مدیریت می‌شوند»، بدون دست‌زدن به چرخه‌ی DI/navigation).

**اقدام (مرحله‌ی کشف کوچک لازم است — فقط همین تسک):**
1. الگوی navigation را پیدا کن: `grep -rn "OnNavigatedFrom\|INavigationAware\|OnNavigatedTo" src/Animora.Desktop --include=*.cs` تا امضای hook و ViewModelهایی که از قبل override کرده‌اند مشخص شود (گزارش `DashboardPageViewModel` و `AppointmentsCalendarViewModel` را به‌عنوان نمونه‌هایی که تایمر Stop می‌کنند نام برده).
2. برای ViewModelهای **داده‌سنگین** (اولویت: `PatientsPageViewModel`، `InventoryPageViewModel`، `SalePageViewModel`/`CashierPageViewModel`، `HistoryPageViewModel`، `ReceptionPageViewModel`) در `OnNavigatedFrom` بزرگ‌ترین `ObservableCollection`‌ها را `Clear()` کن. کالکشن‌ها با `grep -n "ObservableCollection" <file>` در هر ViewModel پیدا می‌شوند.
3. هیچ تغییری در ثبت DI (`AddSingleton`) نده — فقط Clear در ترک صفحه. در `OnNavigatedTo`/`InitializeAsync` این ViewModelها از قبل داده را بازبارگذاری می‌کنند (دوباره پر می‌شوند) — این را موقع کشف تأیید کن تا Clear باعث صفحه‌ی خالی هنگام بازگشت نشود.

> **هشدار کیفیت:** اگر ViewModelی موقع بازگشت خودکار reload نمی‌کند، برای آن ViewModel Clear نکن (وگرنه رگرسیون UI). فقط آن‌هایی که در `OnNavigatedTo` reload دارند ایمن‌اند.
> اگر این تسک حجیم شد (چند ViewModel)، در پایان یک commit+push جامع بزن.

**push:** `scripts/git/push.sh "perf(desktop): release heavy page view-model collections on navigate-away (F-05)"`

---

## [ ] T6 — F-07 🟡 Guard برای ambient transaction در allocator شماره فاکتور
**فایل‌ها:**
- `src/Animora.Persistence.Sqlite/Sales/InvoiceNumberAllocator.cs` (متد `AllocateAsync`, خطوط ~۱۸–۳۴).
- `src/Animora.Persistence.Sqlite/Commands/PipelineSql.cs` (`NextSequenceAsync`, خطوط ۲۱–۴۱؛ خط ۲۹: `command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();`).

**وضعیت (درست ولی شکننده):** allocator به ambient transaction می‌پیوندد؛ اگر ثبت فاکتور شکست بخورد شماره مصرف/افشا نمی‌شود. **ریسک نهفته:** اگر `NextSequenceAsync` خارج از UoW (یعنی `CurrentTransaction == null`) صدا زده شود، روی اتصال مستقل اجرا و شماره را **بلافاصله** مصرف می‌کند → اگر فراخوان بعداً شکست بخورد، **gap در شماره فاکتور**.

**تفاوت مهم:** `NextSequenceAsync` چندین مصرف‌کننده دارد که **عمداً** می‌توانند بدون transaction اجرا شوند — `LocalSequenceAllocator.NextAsync` (شماره‌ی توالی داخلی outbox/command، خط ۱۹) و `JournalNumberAllocator`/`ExpenseNumberAllocator`. پس **guard نباید در `PipelineSql.NextSequenceAsync` سراسری باشد** (رگرسیون می‌دهد). guard فقط برای مسیر **شماره‌ی فاکتور** که باید حتماً داخل UoW باشد.

**اقدام:** در `InvoiceNumberAllocator.AllocateAsync`، قبل از فراخوانی `PipelineSql.NextSequenceAsync`، یک guard اضافه کن که نبود ambient transaction را صریح کند:
```csharp
// F-07: شماره‌ی فاکتور باید همیشه داخل یک UnitOfWork مصرف شود تا در صورت شکست ثبت،
// شماره‌ی مصرف‌شده gap ایجاد نکند. نبود transaction = نقض قرارداد فراخوان.
System.Diagnostics.Debug.Assert(db.Database.CurrentTransaction is not null,
    "InvoiceNumberAllocator must run inside an ambient UnitOfWork transaction to avoid invoice-number gaps.");
```
> چرا `Debug.Assert` و نه throw: گزارش «Debug.Assert/throw» را پیشنهاد داده. throw در production ریسک شکستن مسیر درست فعلی را دارد اگر جایی ناخواسته صدا بزند؛ `Debug.Assert` در dev/CI قرارداد را enforce می‌کند بدون ریسک رفتار production. اگر تیم سخت‌گیری بیشتر خواست، می‌توان به `throw new InvalidOperationException(...)` ارتقا داد — ولی برای این فاز `Debug.Assert` کافی و کم‌ریسک است.
> `db` همان پارامتر primary ctor کلاس است (`AnimoraLocalDbContext db`). دسترسی به `db.Database.CurrentTransaction` در دسترس است.

**push:** `scripts/git/push.sh "fix(sales): assert ambient transaction in InvoiceNumberAllocator to prevent number gaps (F-07)"`

---

## [ ] T7 — F-08 🟡 JsonSerializerContext (source-gen) برای مسیرهای داغ outbox
**فایل‌های مرتبط:** `OutboxRelayHandler.cs` (Serialize), `FinanceProjectionWorker.cs` (Deserialize, بعد از T2). رویدادهای مالی در `Animora.Domain.Events`.

**مشکل:** تمام serializeها reflection-based؛ `JsonSerializerContext` وجود ندارد (تأیید: grep بالا هیچ `JsonSerializerContext` پیدا نکرد). برای desktop فعلی قابل قبول، ولی برای trimming/AOT آینده و پروتکل sync ناسازگار/کند.

**تصمیم این فاز:** کم‌ریسک‌ترین و با بیشترین اثر = source-gen فقط برای **۱۰ نوع رویداد outbox** (مسیر داغ projection مالی). پروتکل sync هنوز وجود ندارد → خارج از دامنه.

**اقدام (دقیق):**
1. یک فایل جدید در پروژه‌ی که رویدادها را می‌شناسد بساز. مکان مناسب کنار worker: `src/Animora.Infrastructure.Local/Workers/OutboxJsonContext.cs` (یا در پروژه‌ی Persistence اگر هر دو سمت باید ببینند — چون Serialize در Persistence و Deserialize در Infrastructure.Local است، context باید در پروژه‌ای باشد که **هر دو** به آن ارجاع دارند یا در هر سمت جداگانه. ساده‌ترین بدون ریسک ارجاع دایره‌ای: context در `Animora.Domain` یا یک پروژه‌ی مشترک که هر دو reference می‌کنند. **اول با `grep` namespaceها و ارجاع پروژه‌ها را چک کن** تا پروژه‌ی درست انتخاب شود — `Animora.Domain.Events` هر دو سمت در دسترس است، پس بهترین میزبان context = پروژه‌ی `Animora.Domain` یا `Animora.Application`).
2. محتوای context:
```csharp
using System.Text.Json.Serialization;
using Animora.Domain.Events;

namespace <namespace-انتخابی>;

[JsonSerializable(typeof(InvoicePostedEvent))]
[JsonSerializable(typeof(InvoiceVoidedEvent))]
[JsonSerializable(typeof(PaymentRegisteredEvent))]
[JsonSerializable(typeof(InvoiceRefundedEvent))]
[JsonSerializable(typeof(GoodsReceiptPostedEvent))]
[JsonSerializable(typeof(StockMovementRecordedEvent))]
[JsonSerializable(typeof(ChequeClearedEvent))]
[JsonSerializable(typeof(ChequeBouncedEvent))]
[JsonSerializable(typeof(CashSessionClosedEvent))]
[JsonSerializable(typeof(ExpensePostedEvent))]
internal sealed partial class OutboxJsonContext : JsonSerializerContext { }
```
3. **سمت Deserialize** (`FinanceProjectionWorker.Deserialize`): از overload typed context استفاده کن، مثلاً `JsonSerializer.Deserialize(m.PayloadJson, OutboxJsonContext.Default.InvoicePostedEvent)`.
4. **سمت Serialize** (`OutboxPayload.Serialize`): چون `payload.GetType()` runtime است، باید به context با `JsonTypeInfo` نگاشت شود یا options دارای context بسازی: `new JsonSerializerOptions { TypeInfoResolver = OutboxJsonContext.Default }` و `JsonSerializer.Serialize(payload, payload.GetType(), options)`. این options را هم cache کن.
5. **سازگاری بحرانی:** source-gen باید **round-trip عیناً سازگار** با داده‌ی قبلی باشد (payloadهای موجود در DB با reflection/default نوشته شده‌اند). چون context بدون `PropertyNamingPolicy` = همان default naming → سازگار. این را با تطبیق نام propertyها (بدون camelCase) تأیید کن.

> **هشدار دامنه/ریسک:** اگر رویدادها constructor-only/record با positional params باشند، source-gen نیاز به `[JsonConstructor]` یا پیکربندی دارد. اگر این تسک به دلیل پیچیدگی record-ها حجیم/پرریسک شد، آن را به حداقل برسان (فقط Deserialize) یا در صورت ریسک رگرسیون، با توضیح در commit، این تسک را به‌صورت «context افزوده ولی مسیر reflection حفظ‌شده به‌عنوان مرحله‌ی اول» ثبت کن. **هرگز سازگاری round-trip را قربانی نکن.**
> این تسک محتمل‌ترین کاندید «فیکس حجیم» است → در پایان حتماً commit+push مستقل با پیام واضح.

**push:** `scripts/git/push.sh "perf(outbox): add source-generated JsonSerializerContext for finance events (F-08)"`

---

## [ ] T8 — F-06 🟡 متریک نرخ miss کش داشبورد (مستندسازی trade-off)
**فایل:** `src/Animora.Client.Services/Dashboard/DashboardQueryService.cs` — خطوط **۷۰–۸۴**.

**وضعیت (نه باگ، trade-off عمدی و درست):** probe با `version = VersionAsync(...)` (خط ۷۰) کلید می‌سازد (`KeyFor(version)`, خط ۷۴) ولی ذخیره با `result.Value.Generation` (خط ۸۳–۸۴). اگر بین probe و read یک write بیفتد، entry زیر نسل جدیدتر ذخیره می‌شود (anti-stale عمدی، کامنت خطوط ۸۲–۸۳). عارضه‌ی جانبی: زیر بار نوشتن مکرر همزمان، `version != Generation` → cache miss (کش عملاً دور زده می‌شود). کش `TenantScopedCache` یک LRU با `capacity=16` و bounded است (نشت ندارد).

**پیشنهاد گزارش:** صرفاً مستند بماند؛ در صورت نیاز یک متریک نرخ miss افزوده شود تا اگر زیر بار سنگین کش بی‌اثر شد قابل مشاهده باشد.

**تصمیم این فاز = کم‌ریسک:** یک کامنت مستندساز صریح بالای بلوک cache (خطوط ۷۳–۷۴) اضافه کن که trade-off و قابلیت افزودن متریک آینده را توضیح دهد. **افزودن متریک واقعی فقط اگر زیرساخت متریک موجود باشد** — اول `grep -rn "IMetrics\|Meter\|Counter<\|Metrics" src/Animora.Client.Services src/Animora.Application --include=*.cs` تا ببینی آیا abstraction متریک وجود دارد.
- اگر زیرساخت متریک **موجود** بود: یک counter ساده‌ی miss/hit اضافه کن.
- اگر **نبود**: فقط کامنت مستندساز (بدون ساختن زیرساخت جدید — خارج از دامنه‌ی این یافته‌ی 🟡).

**اقدام حداقلی (کامنت):** کنار خط ۷۴ (`var key = KeyFor(version);`) توضیح بده:
```csharp
// F-06 (trade-off عمدی): کلید probe با version ساخته می‌شود ولی ذخیره با Generation واقعیِ خوانده‌شده
// (anti-stale). پیامد: زیر بار نوشتن همزمانِ سنگین، version != Generation و cache miss رخ می‌دهد؛ این
// صحت را بر hit-rate مقدم می‌داند. کش TenantScopedCache یک LRU با capacity=16 و bounded است.
// در صورت نیاز به رصد، یک counter نرخ miss اینجا قابل افزودن است.
```

**push:** `scripts/git/push.sh "docs(dashboard): document deliberate cache miss trade-off under concurrent writes (F-06)"`

---

## [ ] T9 — تسک پایانی: حذف فایل فاز و push
**اقدام:**
1. `rm docs/CODE_AUDIT_2026-10-07.md`
2. این پلن (`Plan/code-audit-2026-10-07-fixes.md`) و سایر فایل‌ها دست‌نخورده بمانند.
3. push نهایی: `scripts/git/push.sh "chore: remove completed code-audit phase document (CODE_AUDIT_2026-10-07)"`
4. در صورت نیاز، PR از `genspark_ai_developer` به `main` ایجاد/به‌روزرسانی و لینک به کاربر داده شود.

---

## چک‌لیست وضعیت (برای ادامه در سشن جدید)
- [x] T1 F-01 — push شد ✅
- [x] T2 F-04 — push شد ✅
- [x] T3 F-02 — push شد ✅
- [ ] T4 F-03 — push شد؟
- [ ] T5 F-05 — push شد؟
- [ ] T6 F-07 — push شد؟
- [ ] T7 F-08 — push شد؟
- [ ] T8 F-06 — push شد؟
- [ ] T9 حذف فایل فاز — push شد؟

> هر تسک که تمام شد، `[ ]` را به `[x]` تبدیل کن و خط «push شد؟» را تیک بزن تا سشن بعدی وضعیت را بداند.
