# پلن اجرایی — طراحی بخش «مدیریت پیامک» (SMS Management)

> **این فایل self-contained است.** تمام اطلاعاتِ کشف‌شده از ریپو اینجا تقطیر شده؛ یک مدل تازه در سشن جدید
> باید بتواند فقط با خواندن این فایل، پیاده‌سازی را با کمترین بررسیِ مجدد ریپو شروع کند.
> **آخرین تسک این فایل = حذف همین فایل است.**

---

## 0) هدف و دامنه (اسکوپ)

**هدف:** طراحیِ یک صفحهٔ کامل و مینیمالِ «مدیریت پیامک» در دسکتاپ، کاملاً هماهنگ با لایه‌ها
(View → ViewModel → Contracts → Client.Services) و آمادهٔ اتصال به منطق سرور در آینده.

**این یک تسک «طراحی/scaffold» است، نه پیاده‌سازی منطق ارسال.**
- **فقط UI + قراردادها (Contracts/DTO/interface) + پیاده‌سازیِ stub در Client.Services** ساخته می‌شود.
- منطق واقعیِ ارسال پیامک، محاسبهٔ اعتبار، و زمان‌بندیِ سرور **بعداً** نوشته می‌شود؛ ما فقط seam
  (نقطهٔ اتصال) را تمیز و استاندارد آماده می‌کنیم و حالت‌های مختلف را مدل می‌کنیم.
- **هیچ ادعایی مبنی بر «پیامک ارسال شد» نباید واقعی باشد** — داده‌ها نمونه/sample نیستند؛ سرویس stub
  یک نتیجهٔ طراحی برمی‌گرداند (لیست خالی / مقدار پیش‌فرض) و در UI با toast «نمونهٔ طراحی» مشخص می‌شود.

**خارج از اسکوپ (دست نزن):** نقاط اتصال SMS که از قبل در صفحات دیگر پراکنده‌اند، فقط بعداً با این صفحه
هماهنگ می‌شوند و **وظیفهٔ این تسک نیستند**. آن‌ها را تغییر نده:
- `src/Animora.Desktop/Pages/Vaccination/SmsComposeViewModel.cs` و `SmsComposeView.axaml(.cs)`
- `src/Animora.Client.Services/Vaccination/SmsTemplateRenderer.cs`
- `src/Animora.Contracts/Vaccination/VaccinationModels.cs` (`SmsTemplate`, `SmsState`, `SmsPreview`, `OwnerSmsReminder`)
- `src/Animora.Contracts/Appointments/AppointmentSettingsModels.cs` (`AppointmentSmsSettings`)
- `CustomerClub` (`AutoBirthdaySms`, `SendBirthdaySms`)

---

## 1) حقایقِ کشف‌شده از ریپو (لازم‌الاجرا — دوباره بررسی نکن)

### 1.1 معماری و قانون طلایی
- پروژه = **Animora**، یک SaaS دامپزشکی چندمستأجره؛ کلاینت **Avalonia + MVVM (CommunityToolkit.Mvvm)** روی **.NET 10**، فارسی/RTL/تقویم جلالی، offline-first.
- جریان داده: **View → ViewModel → `Animora.Contracts` interface (via constructor DI) → `Animora.Client.Services` impl → `Animora.Application` dispatch → local SQLite.**
- **ViewModelها فقط به interfaceهای Contracts وابسته‌اند** — هرگز به سرویس concrete، EF/SQL، یا DI scope.
- **بدون داده‌ی نمونه/seed یا شاخهٔ `if(design-time)` در `Pages/**`.**
- RTL یک‌بار app-wide در `App.axaml` ست شده؛ per-screen دوباره ست نکن.

### 1.2 الگوی ساختِ یک صفحهٔ کامل (مرجع: CustomerClub، تازه‌ساز و تمیز)
هر صفحه در `src/Animora.Desktop/Pages/<Area>/` با این جفت‌ها:
- `<Screen>View.axaml` + `<Screen>View.axaml.cs` (code-behind حداقلی: فقط `InitializeComponent`)
- `<Screen>ViewModel.cs` که از **`Animora.Desktop.Shell.PageViewModelBase`** ارث می‌برد.
- مدال/تب‌ها = جفت `*View`+`*ViewModel` اضافی در همان پوشه.

**نکات `PageViewModelBase` (مسیر: `src/Animora.Desktop/Shell/PageViewModelBase.cs`):**
- پراپرتی‌های topbar: `Title`, `Eyebrow`, `Breadcrumbs` (ObservableCollection<string>).
- `UseLoadStatus(Func<Task> refresh)` → وضعیت load/failed/stale + دکمهٔ refresh در shell.
- `RefreshOnDataChange(IDataChangeNotifier changes, Action reload, params DataDomain[] domains)` → reload خودکار روی تغییر داده.
- override: `OnNavigatedTo()` (هر بار ورود)، `OnNavigatedFrom()` (خروج → cancel + پاک‌سازی)، `ApplyNavigationEntry(NavItem)`.
- `IsLoadInFlight` و `TrackLoadResultAsync(Func<Task<bool>>)` (در نمونهٔ CustomerClub استفاده شده).
- `RangeObservableCollection<T>` با `.ReplaceAll(...)` برای لیست‌ها (در Controls موجود است).
- Publish روی UI thread: الگوی `PublishOnUiAsync` از CustomerClub را کپی کن (`Dispatcher.UIThread`).

### 1.3 الگوی تب‌دار (مرجع: Statistics) — برای صفحهٔ چند-بخشی ما مناسب است
مسیر: `src/Animora.Desktop/Pages/Statistics/`
- `StatisticsPageView.axaml` + `StatisticsPageViewModel.cs` = میزبانِ تب‌ها (segmented switch).
- هر تب: `StatisticsTabViewModel(section, label)` با `IconKey` و `IsActive`.
- هر بخش محتوا: `Statistics<Section>View.axaml` + `Statistics<Section>ViewModel.cs` (ارث از `StatisticsSectionViewModel`).
- استایل‌های toolbar/segmented/chip قابل‌کپی از `StatisticsPageView.axaml` (`.seg`/`.segitem`/`.segtap`/`.chip`).
- `StatisticsFormat.cs` → هلپرهای فرمتِ فارسی: `Count(int)`, `Money(long minor)`, `JalaliShort(DateTime)`.

### 1.4 کنترل‌های نمودار موجود (مسیر: `src/Animora.Desktop/Controls/`) — **جدید نساز، همین‌ها را استفاده کن**
| کنترل | کاربرد | API کلیدی |
|---|---|---|
| `BarChart` | نمودار میله‌ای تک‌سری (مصرف پیامک در روز/ماه) | `Points` = `IReadOnlyList<BarChartPoint>`؛ `BarChartPoint(double Value, string Label, string Full)`؛ `BarBrush`؛ `IsMoney` (false برای شمارش) |
| `DonutRing` / `DonutGeometry` | نمودار دونات (سهم هر نوع پیامک از کل مصرف) | بررسی امضا هنگام استفاده |
| `SparklineChart` | روند کوچک | `Points` |
| `MetricBar` | نوار افقیِ نسبتی (سهم هر بخش) | `Fraction` (double 0..1)، `Fill`، `TrackBrush`، `Radius` |
| `SegmentedBar` | نوار چندبخشی (ترکیب انواع) | `Segments` |

نمونهٔ استفادهٔ XAML (از `StatisticsSalesView.axaml`):
```xml
xmlns:controls="using:Animora.Desktop.Controls"
<controls:BarChart Height="240" Points="{Binding SmsUsageTrend}" IsMoney="False" IsVisible="{Binding HasUsage}" />
<controls:MetricBar Height="9" Fraction="{Binding Fraction}" Fill="{Binding Color}" />
```
**نیازِ نمودار (الزامِ کاربر):** مصرف پیامک هر بخش باید به‌صورت نمودار نمایش داده شود و نمودار مناسب باشد
(پیشنهاد: `BarChart` روند مصرف در زمان + `DonutRing`/`MetricBar` برای سهم هر بخش).

### 1.5 نقاط اتصالِ SMSِ موجود در ریپو (برای هماهنگی طراحی — دست‌نزدنی)
- **Vaccination** قوی‌ترین مرجع است:
  - `SmsState` enum: `None, Sent, Scheduled, Failed` (الگوی وضعیت پیامک).
  - `SmsTemplate(Id, Name, Channel, Body)` — توکن‌های body: `{owner} {pet} {vaccine} {date} {next}`.
  - `OwnerSmsReminder(Id, When, PetId, PetName, VaccineName, Channel, Body?)`.
  - `SmsPreview(RecordId, TemplateId, Body, CharacterCount, SegmentCount)` — **۷۰ کاراکتر/سگمنت** قاعدهٔ شمارش.
  - `VaccinationHistoryKind.SmsSent` = «پیامک ارسال شد».
  - `SmsTemplateRenderer.Render(...)` رندر لوکال، بدون ارسال واقعی.
- **Appointments:** `AppointmentSmsSettings(bool SendOnBooking, bool SendReminder, int ReminderLeadHours)`؛ Default همه خاموش.
- **سه حالتِ واکسن/دارو** (مرجع برای بخش «دارو و واکسن» ما — `TreatmentModels.cs`):
  - `TreatmentItemType` = `Vaccine | Medication`
  - `TreatmentKind` = `ClinicVisit` (مراجعه به کلینیک) | `HomeByOwner` (در خانه توسط صاحب — یادآور صاحب اعمال می‌شود) | `BoardingAdminister` (در پانسیون توسط کلینیک)
  - `TreatmentScheduleType` = `Once | EveryNDays | SpecificDates | WeeklyOnDays`

### 1.6 ثبت DI و ناوبری
- **DI:** `src/Animora.Desktop/App.axaml.cs`. الگو (خطوط ~489-544):
  ```csharp
  services.AddSingleton<Animora.Contracts.CustomerClub.ICustomerClubQueryService,
                        Animora.Client.Services.CustomerClub.CustomerClubQueryService>();
  services.AddSingleton<Animora.Desktop.Pages.CustomerClub.CustomerClubPageViewModel>();
  ```
  → سرویس به‌صورت singleton + ViewModel صفحه به‌صورت singleton ثبت می‌شوند.
- **Nav:** `src/Animora.Desktop/Shell/NavigationMenu.cs`. یک `NavItem(key, label, iconKey, typeof(VM), group)` اضافه کن.
  - گروه‌های موجود: «مرکز فرمان»، «مدیریت کلینیک»، «کسب و کار»، «سیستم».
  - **پیشنهاد:** گروه «سیستم»، بعد از «اعلان‌ها و پیام‌ها»؛ label «مدیریت پیامک»، key `sms`، آیکون `IconBell` یا آیکونِ مناسبِ موجود (از `Resources/Theme/Icons.axaml` یک کلید معتبر انتخاب کن — کلید نامعتبر باعث خطا می‌شود).
  - import namespaceِ صفحهٔ جدید را بالای فایل اضافه کن.

### 1.7 بیلد/تأیید (سندباکس)
- بیلد سریع فقط دسکتاپ: `scripts/sandbox/build.sh` → انتظار **0 warning / 0 error**.
- **Full test و بیلد طولانی ممنوع** (قانونِ کاربر). فقط `build.sh` (دسکتاپ) برای تأیید کامپایل.
- اگر startup/DI/shell را دست زدی: smoke-run `scripts/sandbox/run.sh` (success = «SMOKE RUN OK»).

### 1.8 قانونِ قفل‌شدهٔ Git (حتمی)
- بعد از **هر** تسک تکمیل‌شده: `scripts/git/push.sh "type(scope): message"` اجرا و پوش شود.
- اگر exit code = 3 (بدون credential): ابزار `setup_github_environment` را صدا بزن، سپس دوباره اجرا.
- شاخهٔ کاری: `genspark_ai_developer` (با `-b genspark_ai_developer` در اولین push بساز/سوییچ).
- اگر فیکس سنگین یا کد زیادی حین یک تسک نوشته شد، همان‌جا با پیام درست پوش کن.

---

## 2) معماریِ طراحیِ هدف (نقشهٔ فایل‌هایی که ساخته می‌شوند)

```
src/Animora.Contracts/Messaging/
  SmsModels.cs            ← enumها + DTOها (credit, usage, report row, settings per-event, chart point)
  ISmsService.cs          ← interface تک‌seam صفحهٔ مدیریت پیامک (اعتبار/گزارش/تنظیمات/مصرف)

src/Animora.Client.Services/Messaging/
  SmsService.cs           ← پیاده‌سازی stub: مقادیر پیش‌فرض/لیست خالی؛ هیچ ارسال/محاسبهٔ واقعی. آمادهٔ اتصال به Application بعداً.

src/Animora.Desktop/Pages/Messaging/
  SmsPageView.axaml(.cs)              ← میزبان تب‌ها (segmented): نمای کلی/اعتبار | گزارش | تنظیمات
  SmsPageViewModel.cs                ← PageViewModelBase؛ load اعتبار + مصرف + نمودارها
  SmsOverviewView.axaml(.cs) + VM    ← کارت اعتبار باقی‌مانده + نمودار مصرف (BarChart روند + Donut/MetricBar سهم بخش‌ها)
  SmsReportView.axaml(.cs) + VM      ← جدول پیامک‌های ارسالی (DataTable موجود) + فیلتر بازه/بخش
  SmsSettingsView.axaml(.cs) + VM    ← تنظیماتِ per-event (هر بخش قابل‌تنظیم) + قالب متن + پیش‌نمایش
```
> namespaceها: `Animora.Contracts.Messaging`, `Animora.Client.Services.Messaging`, `Animora.Desktop.Pages.Messaging`.
> اگر تب‌دارها بیش از حد شدند، می‌توان Report/Settings را در یک View با چند کارت جمع کرد؛ اما تب‌ها تمیزتر است.

---

## 3) مدلِ داده و حالت‌ها (قراردادها) — طراحی دقیق

### 3.1 enumها
- `SmsEventType` — نوعِ رویدادی که پیامک برایش ارسال می‌شود (هستهٔ تنظیماتِ per-event):
  `AppointmentBooked` (ثبت نوبت)، `AppointmentReminder` (یادآوری نوبت)، `PurchaseThankYou` (تشکر خرید بعد از فاکتور)،
  `FileCreated` (ایجاد پرونده)، `VaccineReminder` (یادآوری واکسن/دارو).
- `SmsDeliveryState` — **از `Vaccination.SmsState` تقلید کن** برای سازگاری: `None, Sent, Scheduled, Failed`
  (یک enum مستقل در Messaging بساز با همین اعضا تا Messaging به Vaccination وابسته نشود).
- `SmsRecipientChannel` یا استفاده از `string Channel` ساده (مثل `SmsTemplate.Channel`) — ساده نگه‌دار.

### 3.2 اعتبار پیامک (تسک ۱)
```csharp
public sealed record SmsCredit(
    long RemainingCount,      // تعداد پیامک باقی‌مانده (یا منفی اگر اعتبار پولی باشد → RemainingCreditMinor)
    long RemainingCreditMinor,// اعتبار ریالی باقی‌مانده (minor units) — یکی از دو مدل شمارش
    DateTime? AsOfUtc);       // آخرین به‌روزرسانی؛ null = نامشخص (stub)
```
> در stub: مقدار صفر/پیش‌فرض بازگردان؛ UI باید «نامشخص/در انتظار اتصال» را تمیز نشان دهد.

### 3.3 گزارش پیامک‌های ارسالی (تسک ۲)
```csharp
public sealed record SentSmsRow(
    string Id, DateTime WhenUtc, SmsEventType EventType,
    string Recipient,      // نام صاحب/بیمار
    string Phone,
    string Body,
    int SegmentCount,      // ۷۰ کاراکتر/سگمنت
    SmsDeliveryState State);
```
> فیلتر: بازهٔ زمانی + نوع رویداد. صفحه‌بندی server-side (الگوی History/Statistics). stub → لیست خالی.

### 3.4 تنظیماتِ per-event (تسک ۳ و ۶)
یک رکوردِ تنظیماتِ کلی که برای هر `SmsEventType` یک بلوک دارد:
```csharp
public sealed record SmsEventSetting(
    SmsEventType EventType,
    bool Enabled,
    string TemplateBody);   // متن با توکن‌ها: {owner} {pet} {date} {amount} {clinic} ...
```
**یادآوری نوبت (`AppointmentReminder`) جزئیاتِ زمان‌بندی دارد:**
```csharp
public sealed record ReminderSchedule(
    IReadOnlyList<int> LeadHoursList,  // چند مرحله: مثلاً [24, 2] = ۲۴ ساعت و ۲ ساعت مانده
    IReadOnlyList<int> SendAtHours);   // ساعت‌های مجاز ارسال در روز (مثلاً 9..20) — خارج از این بازه به بعد موکول
```
**یادآوری واکسن/دارو (`VaccineReminder`) — بسیار مهم، سه حالت:** برای هر `TreatmentKind`
(`ClinicVisit`/`HomeByOwner`/`BoardingAdminister`) باید جداگانه قابل‌تنظیم باشد:
```csharp
public sealed record VaccineReminderSetting(
    TreatmentKind Kind,                 // سه حالت
    bool Enabled,
    IReadOnlyList<int> LeadDaysList,    // چند یادآوری: مثلاً [7, 1] = ۷ روز و ۱ روز مانده
    IReadOnlyList<int> SendAtHours,     // ساعت‌های ارسال
    string TemplateBody);
```
> `TreatmentKind` از `Animora.Contracts.Vaccination` است؛ Messaging می‌تواند به آن reference بدهد
> (Contracts می‌تواند به Contracts reference بدهد) **یا** یک enum معادلِ مستقل بسازد تا کوپلینگ کم شود.
> تصمیم هنگام پیاده‌سازی بر اساس سادگی؛ ترجیح: enum مستقل `SmsVaccineKind` با همان سه عضو (کامنتِ معادل‌بودن بنویس).

رکوردِ ریشه:
```csharp
public sealed record SmsSettings(
    IReadOnlyList<SmsEventSetting> Events,
    ReminderSchedule AppointmentReminder,
    IReadOnlyList<VaccineReminderSetting> VaccineReminders)   // سه آیتم، یکی per TreatmentKind
{
    public static SmsSettings Default { get; } = /* همه Enabled=false، لیست‌های خالی، متن‌های پیش‌فرض فارسیِ نمونه */;
}
```

### 3.5 مصرف برای نمودار (تسک: نمودار مصرف per-section)
```csharp
public sealed record SmsUsagePoint(DateTime Day, int Count);          // روند مصرف در زمان → BarChart
public sealed record SmsUsageBySection(SmsEventType EventType, int Count); // سهم هر بخش → Donut/MetricBar
public sealed record SmsUsageReport(
    IReadOnlyList<SmsUsagePoint> Trend,
    IReadOnlyList<SmsUsageBySection> BySection,
    int TotalCount);
```

### 3.6 interface
```csharp
public interface ISmsService
{
    Task<SmsCredit> GetCreditAsync(CancellationToken ct = default);
    Task<SmsUsageReport> GetUsageAsync(/* range */ CancellationToken ct = default);
    Task<IReadOnlyList<SentSmsRow>> GetSentAsync(/* filter + paging */ CancellationToken ct = default);
    Task<SmsSettings> GetSettingsAsync(CancellationToken ct = default);
    Task SaveSettingsAsync(SmsSettings settings, CancellationToken ct = default);
}
```
> قرارداد خطا: cancel → `OperationCanceledException`؛ سایر خطاها → یک exception مشترک (الگوی
> `StatisticsQueryException` را ببین؛ می‌توان همان را reuse کرد یا `SmsServiceException` ساخت).

---

## 4) تسک‌لیست (وضعیت‌دار) — هر تسک بعد از تکمیل → `scripts/git/push.sh`

> نماد وضعیت: `[ ]` انجام‌نشده · `[~]` در حال انجام · `[x]` انجام‌شده.
> **قانون قفل‌شده:** بعد از هر `[x]` شدن یک تسک، بلافاصله push اجرا شود (وگرنه شکست).

- [x] **T0 — آماده‌سازی شاخه.** سوییچ/ساخت شاخهٔ `genspark_ai_developer`
  (اولین push با `scripts/git/push.sh -b genspark_ai_developer "chore(plan): add SMS management plan"`).
  همین فایل پلن را در همین push ببر.

- [x] **T1 — قراردادها (Contracts/Messaging).** بساز `src/Animora.Contracts/Messaging/SmsModels.cs`
  (تمام enum/record بخش ۳) و `ISmsService.cs`. XML-doc فارسی/انگلیسیِ کوتاه و دقیق. بدون منطق.
  → push: `feat(contracts): add SMS messaging models and ISmsService seam`.

- [x] **T2 — پیاده‌سازی stub (Client.Services/Messaging).** `SmsService.cs` با مقادیر پیش‌فرض/لیست خالی،
  بدون ارسال/محاسبهٔ واقعی؛ کامنت واضح «آمادهٔ اتصال به Application/سرور». رعایت no-LINQ در hot-path لازم نیست چون stub است اما تمیز بنویس.
  → push: `feat(client-services): add stub SmsService for design wiring`.

- [ ] **T3 — صفحه + تب‌ها (Pages/Messaging).** `SmsPageView(.axaml/.cs)` + `SmsPageViewModel`
  (ارث PageViewModelBase، تب‌های segmented مثل Statistics: «نمای کلی»/«گزارش»/«تنظیمات»).
  Title=«مدیریت پیامک»، Eyebrow مناسب، Breadcrumbs=[«سیستم»،«مدیریت پیامک»].
  → push: `feat(ui): add SMS management page shell with tabs`.

- [ ] **T4 — بخش نمای کلی + اعتبار + نمودار (تسک‌های ۱ و نمودار).** `SmsOverviewView/VM`:
  کارتِ «اعتبار باقی‌مانده»، `BarChart` روند مصرف (`IsMoney=False`)، `DonutRing`/`MetricBar` سهم هر بخش.
  حالتِ خالی/در انتظار اتصال تمیز نمایش داده شود.
  → push: `feat(ui): add SMS overview with credit card and usage charts`.

- [ ] **T5 — بخش گزارش (تسک ۲).** `SmsReportView/VM`: جدولِ پیامک‌های ارسالی (DataTable موجود در Controls)
  + فیلتر بازه/نوع رویداد + ستون وضعیت (pill با SmsDeliveryState). stub → خالی با پیام مناسب.
  → push: `feat(ui): add SMS sent-report table with filters`.

- [ ] **T6 — بخش تنظیمات (تسک‌های ۳ و ۶).** `SmsSettingsView/VM`: برای هر `SmsEventType` یک کارت
  (فعال/غیرفعال + متن قالب + پیش‌نمایش با شمارندهٔ کاراکتر/سگمنت ۷۰تایی).
  - یادآوری نوبت: چند مرحلهٔ lead (۲۴/۲ ساعت) + ساعت‌های ارسال.
  - **یادآوری واکسن/دارو (مهم):** سه بلوک per `TreatmentKind` با چند مرحلهٔ lead-days + ساعت ارسال + متن.
  دکمهٔ ذخیره → `SaveSettingsAsync` (stub). همه پیش‌فرض خاموش؛ هیچ ادعای ارسالِ واقعی.
  → push: `feat(ui): add per-event SMS settings incl. 3-mode vaccine reminders`.

- [ ] **T7 — ثبت DI + ناوبری + یکپارچگی (تسک‌های ۴ و ۵).**
  - DI در `App.axaml.cs`: ثبت `ISmsService→SmsService` (singleton) + `SmsPageViewModel` (singleton).
  - Nav در `Shell/NavigationMenu.cs`: `NavItem("sms","مدیریت پیامک","<آیکون معتبر>", typeof(SmsPageViewModel),"سیستم")` + import namespace.
  - اطمینان از اینکه آیکون‌کلید در `Resources/Theme/Icons.axaml` واقعاً وجود دارد.
  → push: `feat(ui): register SMS page in DI and sidebar navigation`.

- [ ] **T8 — بیلد و تأیید نهایی.** فقط `scripts/sandbox/build.sh` (0/0). چون DI/shell دست خورد،
  در صورت امکان `scripts/sandbox/run.sh` (SMOKE RUN OK). رفعِ هر warning/error.
  → push: `fix(ui): resolve build issues for SMS management (0 warn/0 err)` (اگر فیکسی لازم شد).

- [ ] **T9 — حذف فایل پلن (تسکِ پایانی).** فقط همین فایل `Plan/SMS-MANAGEMENT-PLAN.md` را حذف کن.
  **به بقیهٔ فایل‌ها، `Plan/reports/**`، و فایل‌های رودمپ دست نزن.**
  → push: `chore(plan): remove completed SMS management plan`.

---

## 5) چک‌لیست کیفیت (هنگام هر تسک)
- طراحی **مینیمال، تمیز، کاربردی**؛ هم‌سبک با بقیهٔ صفحات (کارت‌ها، segmented، chipها، توکن‌های theme).
- فارسی/RTL/تقویم جلالی؛ ارقام فارسی با هلپرهای `Localization` و `StatisticsFormat`.
- بدون داده‌ی نمونه در UI؛ stub در Client.Services نتیجهٔ طراحی برمی‌گرداند.
- ViewModel فقط به `ISmsService` (Contracts) وابسته باشد.
- بررسی فایل‌ها فقط هدفمند و بر اساس نیاز؛ بررسی بیهوده ممنوع.
- بدون Full test / بیلد طولانی؛ فقط `build.sh` دسکتاپ.
- نمودارها: از کنترل‌های موجود استفاده شود؛ نمودارِ مناسب برای مصرفِ هر بخش.
