# گزارش بررسی و ممیزی کد پروژه Animora

**تاریخ:** ۲۰۲۶-۱۰-۰۷
**نوع بررسی:** بازرسی استاتیک عمیق در تمام لایه‌های کد (Static Code Review)
**دامنه:** کل کدبیس (~۲۲۳٬۰۰۰ خط، ۱۰۷۵ فایل C#، معماری .NET 10 / Avalonia)
**قانون رعایت‌شده:** هیچ Build و هیچ Test اجرا نشد — فقط بازرسی لایه‌به‌لایهٔ کد.

> این گزارش بر پایهٔ شواهد واقعی استخراج‌شده از کد (با ذکر فایل و شماره خط) تهیه شده است. هر مورد با شدت (Severity) و پیشنهاد اصلاح همراه است.

---

## ۰) خلاصهٔ مدیریتی

کدبیس Animora در وضعیت **بالغ و باکیفیت** است. شواهد قوی کیفیت:

- **بدون `.Result`/`.Wait()` خطرناک در مسیر backend** و بدون `GC.Collect`/`Thread.Sleep`.
- **بدون `catch {}` خالی و بدون `catch (Exception)` عام بدون فیلتر** در مسیرهای اصلی.
- **بدون Raw SQL** (ایمن در برابر SQL Injection) — تمام کوئری‌ها LINQ/EF.
- نسبت `AsNoTracking` (۲۷۹) به `ToListAsync` (۱۱۶) نشان‌دهندهٔ کوئری‌های read-only درست.
- فقط **۲ مورد TODO** در کل کدبیس.
- Hash رمز عبور با **PBKDF2 / 210,000 iteration** (استاندارد OWASP).
- State machineهای مالی (`SettlementTransitions`)، `Money` و `Quantity` با محاسبات `checked` و largest-remainder allocation.
- کش `TenantScopedCache` یک LRU محدود و thread-safe است.

با این حال چند موضوع وجود دارد که **مانع رسیدن برنامه به سطح «عالی»** می‌شود و در ادامه با اولویت‌بندی آمده است. مهم‌ترین‌ها: **رمزنگاری دیتابیس غیرفعال است** و **عدم انتشار CancellationToken** در لایهٔ سرویس.

### جدول اولویت

| # | موضوع | شدت | لایه |
|---|-------|-----|------|
| 1 | رمزنگاری دیتابیس (SQLCipher) غیرفعال → PII/داده پزشکی/مالی به‌صورت plaintext | 🔴 بحرانی | Persistence / Infra |
| 2 | عدم انتشار `CancellationToken` در ~۸۰٪ متدهای Contracts | 🟠 بالا | Contracts / Client.Services |
| 3 | الگوی `.Result` در ۴ ViewModel ماژول Statistics | 🟠 بالا | Desktop |
| 4 | استفادهٔ مستقیم از `DateTime.Now/UtcNow` به‌جای `IClock` (۳۲ مورد) | 🟠 بالا | Client.Services / Desktop |
| 5 | عدم استفادهٔ یکدست از `ConfigureAwait(false)` در Client.Services | 🟡 متوسط | Client.Services |
| 6 | عدم تعادل Subscribe/Unsubscribe رویدادها (نشت حافظهٔ بالقوه) | 🟡 متوسط | Desktop |
| 7 | `ObservableCollection` خالص + الگوی Clear+Add (نقض PERF §5) | 🟡 متوسط | Desktop |
| 8 | تنظیمات SMS فقط در حافظه persist می‌شود (UX گمراه‌کننده) | 🟡 متوسط | Desktop |
| 9 | `AllowNegativeStockOnSale = true` به‌صورت پیش‌فرض | 🟡 متوسط | Persistence/Domain |
| 10 | God Object: `AppointmentsCalendarViewModel` (۲۳۹۲ خط) | 🟡 متوسط | Desktop |
| 11 | `FileFallbackSecretStore` — ذخیرهٔ کلید در فایل ناامن | 🟡 متوسط | Infra |
| 12 | timing/user-enumeration جزئی در SignIn | 🟢 جزئی | Application |
| 13 | `Money.ApplyPercent` بدون `checked` | 🟢 جزئی | Domain |
| 14 | QueryFilter مبتنی بر closure تک‌مستأجری (ریسک آیندهٔ multi-tenant) | 🟢 جزئی | Persistence |
| 15 | drift مستندات (AGENTS.md ماژول Statistics را پوشش نمی‌دهد) | 🟢 جزئی | Docs |
| 16 | swallow کردن خطای راه‌اندازی store بدون لاگ/نمایش | 🟢 جزئی | Desktop |

---

## ۱) مشکلات امنیتی

### 🔴 1-1. رمزنگاری دیتابیس غیرفعال است (بحرانی)
**شواهد:** `src/Animora.Persistence.Sqlite/Options/LocalStoreOptions.cs:13`
```csharp
public bool EncryptionEnabled { get; set; }   // پیش‌فرض = false
```
`MultipleCiphersEncryptionProvider` در کد موجود است اما طبق کامنت خودش «Compiled but unreachable … pinned in E08, not here».

**پیامد:** کل دیتابیس SQLite — شامل اطلاعات هویتی مالکان (PII)، سوابق پزشکی حیوانات، و داده‌های مالی/فاکتورها — به‌صورت **plaintext روی دیسک** ذخیره می‌شود. هرکسی با دسترسی فیزیکی یا backup به فایل، همه‌چیز را می‌خواند.

**پیشنهاد:** تکمیل مسیر Track E (فعال‌سازی SQLCipher/cipher bundle)، و تا آن زمان دست‌کم مستندسازی صریح ریسک برای کاربر نهایی و رمزگذاری backupها در لایهٔ فایل.

### 🟡 1-2. `FileFallbackSecretStore` — کلید در فایل ناامن
**شواهد:** `src/Animora.Infrastructure.Local/Security/FileFallbackSecretStore.cs`, `LocalInfrastructureServiceCollectionExtensions.cs:73` (شرط `AllowInsecureKeyFallback`).

اگر OS keystore در دسترس نباشد و `AllowInsecureKeyFallback=true`، کلید در فایل با منبع `KeySource.InsecureFallback` ذخیره می‌شود.

**پیشنهاد:** این مسیر باید فقط در حالت dev فعال باشد؛ در build انتشار باید به‌صورت سخت‌گیرانه غیرفعال یا با warning آشکار همراه شود.

### 🟢 1-3. timing / user-enumeration جزئی در SignIn
**شواهد:** `src/Animora.Application/UseCases/Identity/SignInCommand.cs` (خطوط ~۶۲ و ~۶۶–۶۹).

برای کاربر **ناموجود** یک hash-verify ساختگی اجرا می‌شود (برای برابری زمان)، اما برای کاربر **موجودِ قفل‌شده/غیرفعال** verify انجام نمی‌شود → اختلاف زمان پاسخ، enumeration جزئی را ممکن می‌کند.

**پیشنهاد:** مسیر lockout/disabled را نیز با یک verify ساختگی هم‌زمان کنید تا پروفایل زمانی یکسان شود.

---

## ۲) مشکلات فنی و معماری

### 🟠 2-1. عدم انتشار `CancellationToken` در لایهٔ Contracts
**شواهد:** در `src/Animora.Contracts` فقط **۳۶** اشارهٔ `CancellationToken` در برابر **۱۹۶** امضای متد `Task` وجود دارد. نمونه: `PatientsService.GetPetsPageAsync(PatientsDirectoryQuery query)` بدون `ct`.

**پیامد:** کوئری‌های دیتابیس هنگام خروج/جابه‌جایی سریع کاربر بین صفحات **قابل لغو نیستند**؛ کار بیهوده و فشار منبع ادامه می‌یابد (مشکل responsiveness).

**پیشنهاد:** افزودن `CancellationToken ct = default` به امضای عمومی سرویس‌ها و انتشار آن تا EF. (نکته: ماژول Statistics این کار را درست انجام داده و الگوی خوبی برای بقیه است.)

### 🟠 2-2. الگوی `.Result` در ViewModelهای Statistics
**شواهد:** ۱۵ مورد در ۴ فایل:
`StatisticsClientsViewModel.cs:86-92`, `StatisticsExpensesViewModel.cs:123-125`, `StatisticsOverviewViewModel.cs:61-63`, `StatisticsSalesViewModel.cs:77-81`.

```csharp
await Task.WhenAll(statsTask, speciesTask, topTask, seriesTask);
var stats = statsTask.Result;   // بعد از WhenAll دچار deadlock نمی‌شود، ولی anti-pattern است
```
چون `.Result` **بعد از** `await Task.WhenAll` است، deadlock نمی‌دهد، اما:
- در صورت fault، `AggregateException` پرتاب می‌شود (نه استثنای اصلی) → مدیریت خطا را پیچیده می‌کند.
- نقض صریح PERF §4 (منع `.Result`) و ناسازگاری با باقی کدبیس.

**پیشنهاد:** جایگزینی با `await` مستقیم (مثلاً `var stats = await statsTask;`).

### 🟠 2-3. استفادهٔ مستقیم از `DateTime.Now/UtcNow` به‌جای `IClock`
**شواهد:** **۳۲** مورد در production. مهم‌ترین: `src/Animora.Client.Services/Vaccination/VaccinationService.cs:17,106`:
```csharp
DateOnly.FromDateTime(DateTime.UtcNow)
```
با وجود اینکه انتزاع `IClock` (`src/Animora.Domain/Time/IClock.cs`, `SystemClock`, `IClinicClock`) موجود است.

**پیامد:**
- **ناسازگاری معماری:** لایهٔ backend باید از `IClock` استفاده کند نه ساعت سیستم.
- **باگ بالقوهٔ timezone:** `UtcNow` برای محاسبهٔ تاریخ سررسید واکسن در تقویم محلی/شمسی نادرست است (ممکن است یک روز جابه‌جا شود).
- **Testability:** کد وابسته به ساعت سیستم با زمان ثابت قابل unit-test نیست.

**پیشنهاد:** تزریق `IClock`/`IClinicClock` و استفاده از آن در تمام محاسبات تاریخ در backend. موارد نمایشی صرف در UI اولویت کمتری دارند.

### 🟡 2-4. عدم استفادهٔ یکدست از `ConfigureAwait(false)` در Client.Services
**شواهد:** فقط **۴۹** از **۳۵۹** `await` در `Client.Services` از `ConfigureAwait(false)` استفاده می‌کنند (در `PatientsService` هیچ‌کدام)، در حالی که Application و Persistence آن را به‌کار می‌برند.

**پیامد:** هر continuation به UI thread capture می‌شود؛ overhead غیرضروری و ناسازگاری با باقی backend. (در عمل چون از UI با `_ = LoadAsync()` صدا زده می‌شود deadlock نمی‌دهد.)

**پیشنهاد:** افزودن `ConfigureAwait(false)` در کل `Client.Services` به‌صورت یکدست.

### 🟡 2-5. God Object: `AppointmentsCalendarViewModel`
**شواهد:** ۲۳۹۲ خط (بزرگ‌ترین ViewModel؛ بعدی‌ها ۱۱۳۲ و ۱۰۸۴).

**پیامد:** نگهداری دشوار، ریسک رگرسیون بالا، تست سخت.

**پیشنهاد:** تجزیه به اجزای کوچک‌تر (مثلاً جداسازی منطق چیدمان تقویم، فیلترها، و orchestration بارگذاری).

---

## ۳) مشکلات پرفورمنسی

### 🟡 3-1. `ObservableCollection` خالص + الگوی Clear()+Add() در حلقه (نقض PERF §5)
**شواهد:** **۷۷** property از نوع `ObservableCollection<>` خالص (نه `RangeObservableCollection`) وجود دارد؛ چندین مورد با `Clear()` سپس `Add()` در حلقه پر می‌شوند (نمونه: `ProductModalViewModel.cs:305,314,395,544`).

**پیامد:** هر `Add` یک رویداد `CollectionChanged` و re-layout می‌زند. برای مجموعه‌های کوچک (چند آیتم) ناچیز است، اما برای مجموعه‌های بزرگ‌تر (مثلاً تاریخچه، واریانت‌ها) باعث jank می‌شود.

**پیشنهاد:** مهاجرت مجموعه‌های پرحجم به `RangeObservableCollection` با یک bulk-reset (مطابق الگوی موجود در بقیهٔ پروژه).

### 🟡 3-2. over-fetching ناشی از `AutoInclude()` روی owned collectionها
**شواهد:** `OwnedRows.Map` → `Navigation(...).AutoInclude()` (`Conventions/OwnedRows.cs:23`). مثلاً `Invoice.Lines` همیشه join می‌شود.
**نکتهٔ مثبت:** این N+1 در `FinanceProjectionStore` را حذف می‌کند (نگرانی اولیه رفع شد).
**نکتهٔ منفی:** کوئری‌های «فقط هدر» (مثل لیست فاکتورها) خطوط را نیز می‌کشند مگر projection انجام شود.

**پیشنهاد:** برای کوئری‌های لیستی از projection صریح (`Select`) استفاده شود تا AutoInclude دور زده شود.

### 🟡 3-3. idle cost تایمرها
**شواهد:** چندین `DispatcherTimer` (مثلاً `TopbarViewModel._relativeTicker`, `ToastService`). `AppointmentsCalendarViewModel` در `OnNavigatedTo/From` تایمر را Start/Stop می‌کند (خوب).

**پیشنهاد:** اطمینان از اینکه همهٔ ViewModelهای دارای تایمر، در عدم‌نمایش تایمر را متوقف می‌کنند (PERF §12).

---

## ۴) ریسک‌های آینده (نشت حافظه، پایداری)

### 🟡 4-1. عدم تعادل Subscribe/Unsubscribe رویدادها
**شواهد:** نسبت `*Changed +=` (۱۵) به `*Changed -=` (۳). ViewModelهای **transient** زیر دارای تایمر/اشتراک هستند و در صورت subscribe به رویداد سرویس singleton بدون unsubscribe نشت می‌دهند:
`OwnerSheetViewModel`, `PetFileViewModel`, `BookingPanelViewModel` (۱۰۸۴ خط), `VisitEncounterViewModel`, ...

**نکتهٔ تسکین‌دهنده:** `AppointmentsCalendarViewModel` که به `CapabilitiesChanged` متصل می‌شود **Singleton** است، پس آن مورد خاص نشت ندارد.

**پیشنهاد:** هر ViewModel transient که به رویداد singleton متصل می‌شود باید `IDisposable` را پیاده کند و در تخریب unsubscribe کند؛ یا از weak-event pattern استفاده شود.

### 🟡 4-2. QueryFilter مبتنی بر closure تک‌مستأجری
**شواهد:** `ModelConventions.cs` → `HasQueryFilter("Tenant", e => e.TenantId == context.CurrentTenantId)`.

filter مقدار `context.CurrentTenantId` را در زمان model-building می‌بندد. چون store فعلاً تک‌مستأجری است (`T-LOCAL-DEFAULT`) مشکلی نیست، اما این **فرض پنهان** با معماری سرور چندمستأجری آینده خواهد شکست (نیاز به `IModelCacheKeyFactory`).

**پیشنهاد:** مستندسازی این محدودیت و برنامه‌ریزی برای model-cache key مبتنی بر tenant پیش از مهاجرت به سرور.

---

## ۵) مشکلات عملکردی (Functional)

### 🟡 5-1. تنظیمات SMS فقط در حافظه persist می‌شود — UX گمراه‌کننده
**شواهد:** `src/Animora.Desktop/Pages/Patients/Settings/PatientsSettingsViewModel.cs` (کامنت‌ها: «Persisted only in memory for now»، دو `// TODO: persist …`).

**پیامد:** کاربر toast «ذخیره شد» می‌بیند، اما مقدار پس از restart از بین می‌رود → کاربر فریب می‌خورد.

**پیشنهاد:** حتی اگر gateway واقعی SMS آماده نیست، دست‌کم preference/template را persist کنید؛ یا toast را به «ذخیرهٔ موقت» تغییر دهید.

### 🟡 5-2. `AllowNegativeStockOnSale = true` به‌صورت پیش‌فرض
**شواهد:** `LocalStoreOptions.cs:35`.

**پیامد:** فروش می‌تواند موجودی را منفی کند. اگر عمدی (برای posting آفلاین) است یک trade-off مستند است؛ اگر نه، منجر به ناسازگاری موجودی می‌شود.

**پیشنهاد:** تأیید اینکه این یک تصمیم business آگاهانه است و قرار دادن آن پشت یک تنظیم صریح + هشدار UI هنگام منفی‌شدن موجودی.

---

## ۶) موارد غیراستاندارد / جزئی

### 🟢 6-1. `Money.ApplyPercent` بدون `checked`
**شواهد:** `Money.cs:160` → `var raw = Minor * percent / 100m;` (برخلاف `Add/Subtract/Multiply` که `checked` دارند). چون `decimal` است overflow واقعی بعید است اما ناسازگاری سبک با بقیهٔ کلاس است.

### 🟢 6-2. swallow کردن خطای راه‌اندازی store بدون نمایش
**شواهد:** `App.axaml.cs` → در `InitializeLocalStoreAsync` هنگام `result.IsFailure` فقط `return false` می‌شود؛ `result.Error` لاگ/نمایش داده نمی‌شود. کاربر فقط می‌بیند sign-in غیرفعال است بدون دانستن علت (مشکل diagnostics).

### 🟢 6-3. `catch (Exception)` در warm-up بدون لاگ
**شواهد:** `CapabilityGateService` (مسیر warm-up). توجیه‌شده اما swallow بدون لاگ یک debt است.

### 🟢 6-4. drift مستندات
**شواهد:** `AGENTS.md:12,32` می‌گوید «all 17 screens»، اما ماژول Statistics (feature جدیدتر) در فهرست صفحات AGENTS.md منعکس نشده — در DI درست ثبت شده (`StatisticsPageViewModel`). مستندات نیازمند به‌روزرسانی است.

### 🟢 6-5. `.AsTask().GetAwaiter().GetResult()` در shutdown
**شواهد:** `App.axaml.cs:143`. در مسیر `ControlledApplicationLifetime.Exit` قابل قبول است (برنامه در حال خروج)، اما یک blocking call باقی می‌ماند.

---

## ۷) نقاط قوت (برای حفظ و الگوبرداری)

- **Domain:** `Money`/`Quantity` با `checked`، largest-remainder allocation، و تطبیق واحد/ارز؛ state machine تسویه تمیز.
- **Persistence:** بدون Raw SQL، استفادهٔ گسترده از `AsNoTracking`، converterهای صریح (`FixedScaleQuantityConverter` ورودی >۳ رقم اعشار را **reject** می‌کند نه silent-round)، PRAGMAهای WAL/foreign_keys.
- **Application:** pipeline ثابت (frozen) با ExceptionMapping/Validation/Authorization/Audit؛ PBKDF2 با iteration استاندارد؛ lockout و timing-equalizer در SignIn.
- **Client.Services:** `TenantScopedCache` یک LRU محدود thread-safe؛ استفادهٔ درست از `lock` و بدون static mutable state خطرناک.
- **کیفیت کلی:** فقط ۲ TODO، بدون `GC.Collect`/`Thread.Sleep`، بدون catch خالی.

---

## ۸) برنامهٔ اصلاح پیشنهادی (به‌ترتیب اولویت)

1. **فعال‌سازی رمزنگاری دیتابیس** (Track E) — بحرانی‌ترین مورد امنیتی.
2. **انتشار `CancellationToken`** در Contracts و Client.Services (الگو از Statistics).
3. حذف `.Result` در ViewModelهای Statistics → `await` مستقیم.
4. جایگزینی `DateTime.UtcNow` با `IClock`/`IClinicClock` در backend (به‌ویژه `VaccinationService`).
5. یکدست‌سازی `ConfigureAwait(false)` در Client.Services.
6. افزودن `IDisposable`+unsubscribe به ViewModelهای transient دارای اشتراک رویداد.
7. مهاجرت مجموعه‌های پرحجم UI به `RangeObservableCollection`.
8. persist واقعی تنظیمات SMS یا اصلاح پیام toast.
9. تجزیهٔ `AppointmentsCalendarViewModel`.
10. سخت‌گیری `FileFallbackSecretStore` و `AllowInsecureKeyFallback` در release.
11. اصلاح timing در SignIn برای مسیر lockout/disabled.
12. به‌روزرسانی مستندات (AGENTS.md) برای ماژول Statistics و محدودیت QueryFilter.

---

*این گزارش صرفاً بر پایهٔ بازرسی استاتیک کد در لایه‌های مختلف تهیه شده و — طبق درخواست — هیچ Build یا Test اجرا نشده است. تمام یافته‌ها با ارجاع به فایل و شماره خط قابل ردیابی‌اند.*
