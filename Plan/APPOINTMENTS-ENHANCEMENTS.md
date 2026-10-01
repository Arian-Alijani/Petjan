# پلن اجرایی — بهبود بخش «نوبت‌ها» (Appointments)

> سند خودبسنده برای مدل هوش مصنوعی مجری. تمام مسیرها، قراردادها و تصمیم‌های
> فنی از پیش استخراج شده‌اند؛ مدل مجری نباید دوباره کل ریپو را بررسی کند — فقط
> فایل‌های نام‌برده در هر تسک را باز کند. بعد از هر تسک، **اجبارى** پوش کنید
> (قانون پوش در انتهای هر تسک).

## فهرست تسک‌ها (نقشهٔ کلی)

| # | عنوان | خروجی اصلی | وضعیت |
|---|---|---|---|
| ۱ | seam تنظیمات نوبت (انواع + حالت تقویم + پیامک) | `IAppointmentSettingsService` + پیاده‌سازی JSON | `[x]` |
| ۲ | تب «تنظیمات» در صفحهٔ نوبت‌ها (UI افزودن نوع/حالت/پیامک) | تب جدید + ViewModel + View | `[ ]` |
| ۳ | رنگ رندومِ متمایز برای انواع و پزشک‌ها + نمایش در تول‌بار و ساخت نوبت | رنگ persist‌شده + bind یکپارچه | `[ ]` |
| ۴ | حالت تقویم روزانه (بدون ساعت) در تقویم و در ثبت نوبت | شاخهٔ day-only در VM/View بوکینگ و تقویم | `[ ]` |
| ۵ | ثبت بیمار جدید با ویزارد اصلی + انتخاب خودکار + حفظ تنظیمات نوبت | nest ویزارد روی sheet + بازگشت بیمار | `[ ]` |
| ۶ | هم‌راستاسازی انواع ساخته‌شده با کاتالوگ در همه‌جا | merge در `GetTypesAsync` + رفرش | `[ ]` |
| ۷ | اصلاح تول‌بار/دکمه‌ها: جهت دکمه‌های تقویم، استایل، آیکن ثبت نوبت، حذف متن drag | XAML تول‌بار و empty-state | `[ ]` |
| ۸ | حذف فایل پلن (تسک پایانی) | حذف این فایل | `[ ]` |

---

## ۰) بافت معماری (خلاصهٔ قطعی — دوباره کشف نکنید)

- **استک:** C# / .NET 10، Avalonia + MVVM (`CommunityToolkit.Mvvm`). UI فارسی/RTL،
  تقویم جلالی. دسکتاپ روی بک‌اند محلی واقعی (SQLite) از طریق لایهٔ
  `Animora.Contracts` → `Animora.Client.Services` اجرا می‌شود. **دسکتاپ هرگز
  مستقیم به EF/SQL دست نمی‌زند.**
- **صفحهٔ «نوبت‌ها» = پوشهٔ `src/Animora.Desktop/Pages/Appointments/`.**
  - کنترلر صفحه: `AppointmentsCalendarViewModel.cs` (تب‌ها: `cal`/`queue`/`hist`،
    نماها: `day`/`week`/`month`، فیلتر نوع/پزشک، تول‌بار).
  - View صفحه: `AppointmentsCalendarView.axaml` (هدر + تول‌بار سه‌ردیفه + بدنهٔ تب‌ها).
  - پنل ثبت/ویرایش نوبت: `BookingPanelViewModel.cs` + `BookingPanelView.axaml`
    (+ `BookingPanelStyles.axaml`). از طریق `IDialogService.ShowSheet` باز می‌شود
    (sheet host، عرض ۱۰۸۰ ارتفاع ۸۶۴).
  - رنگ/پالت: `AppointmentsPresentation.cs` (دیکشنری `TypePalette` ثابت).
  - مبدل رنگ هگز→Brush در XAML: `HexBrushConverter.cs` (کلید `HexBrushConverter`).
- **قرارداد داده (seam):** `src/Animora.Contracts/Appointments/`
  - `AppointmentModels.cs` → رکورد `AppointmentType(Id,Label,ClassKey,Accent,
    Background,Foreground,IconKey,DurationMinutes)`، `AppointmentDoctor(...Accent)`.
  - `IAppointmentsQueryService.cs` → `GetDoctorsAsync` / `GetTypesAsync` /
    `GetForRangeAsync` / ...
  - `IAppointmentBookingService.cs` + `AppointmentBookingModels.cs`
    (`NewAppointmentRequest` دارد `SendSms`, `SendReminder`).
- **پیاده‌سازی واقعی سرویس:** `src/Animora.Client.Services/Appointments/`
  - `AppointmentsQueryService.cs`: `GetTypesAsync()` الان مقدار ثابت
    `AppointmentsMapping.Types` را برمی‌گرداند.
  - `AppointmentsMapping.cs`: `Types` از `AppointmentTypeCatalog.All` ساخته می‌شود
    و برای **همهٔ** انواع `Accent="#5A67D8"` و `classKey = code.ToLowerInvariant()`
    می‌گذارد؛ یعنی رنگِ واقعی انواع عملاً یکسان است.
  - کاتالوگ انواع: `src/Animora.Domain/Scheduling/AppointmentTypeCatalog.cs`
    (ثابت، reference-data، tenant-editable نیست).
  - رنگِ چیپ‌ها در View از `Type.Accent` (هگز) با `HexBrushConverter` می‌آید
    (`AppointmentsCalendarView.axaml` خطوط ~۲۴۳ و ~۲۷۱ برای پزشک `Doctor.Accent`).
- **دیالوگ‌ها:** `src/Animora.Desktop/Services/DialogService.cs` سه host مستقل دارد
  (`ModalHost`/`DrawerHost`/`SheetHost`). **هشدار بحرانی:** `Close()` هر سه host را
  هم‌زمان می‌بندد. پنل بوکینگ روی **SheetHost** است؛ ویزارد بیمار روی **ModalHost**.
- **ویزارد ثبت بیمار اصلی:** `src/Animora.Desktop/Pages/Patients/
  AddPatientWizardViewModel.cs` + `AddPatientWizardView.axaml`. سازنده:
  `(IPatientsQueryService patients, IDialogService dialogs, Action onCompleted)`.
  در پایان `CommitAsync()` → `_dialogs.Close()` سپس `_onCompleted()`. **خروجی
  بیمارِ ساخته‌شده را برنمی‌گرداند** (callback بدون پارامتر). از صفحهٔ بیماران با
  `_dialogs.ShowModal(new AddPatientWizardViewModel(...), maxWidth: 760)` باز می‌شود.
- **persistence سبک محلی موجود:** `ILocalFileStore`
  (`src/Animora.Infrastructure.Local/Storage/LocalFileStore.cs`) فقط blob
  content-addressable است؛ **key-value settings store نیست**. برای تنظیمات جدید
  یک seam جدید لازم است (تسک ۱).
- **DI:** همهٔ سرویس‌ها و ViewModelها در `src/Animora.Desktop/App.axaml.cs` ثبت
  می‌شوند؛ سرویس‌های کلاینت در `AddClientServices`(در
  `src/Animora.Client.Services`) ثبت می‌شوند — الگوی موجود را دنبال کنید.
- **آیکن‌ها:** `src/Animora.Desktop/Resources/Theme/Icons.axaml`. موجود و مفید:
  `IconCalendar`, `IconClock`, `IconSms`, `IconBell`, `IconSyringe`, `IconPlus`,
  `IconCheck`, `IconPalette`, `IconSettings`? (نه — از `IconTools`/`IconPalette`
  استفاده کنید). **`IconCalendarPlus`/`IconCalendarCheck` وجود ندارد.**
- **کارایی (PERFORMANCE.md):** برای کالکشن‌های bind‌شده از
  `RangeObservableCollection.ReplaceAll` استفاده کنید (یک Reset به‌جای storm).
  برای Brush از `BrushCache.Solid(hex)` / `GradientBrushCache` استفاده کنید
  (بدون alloc در حلقهٔ رندر). رنگ رندومِ انواع را **یک‌بار** هنگام ساخت نوع تثبیت
  و persist کنید؛ در هر رندر دوباره تولید نکنید.

### تصمیم معماری قطعی برای «تنظیمات قابل‌ویرایش»
کاتالوگ انواع فعلاً دامنه‌ای/ثابت است و دسکتاپ نباید به persistence دست بزند.
بنابراین یک **seam جدید در Contracts** می‌سازیم:
`IAppointmentSettingsService` (در `Animora.Contracts/Appointments/`)، و
**پیاده‌سازی کلاینت** آن را روی یک فایل JSON محلی (در پوشهٔ دادهٔ برنامه، با
`System.Text.Json`) می‌گذاریم — هم‌راستا با الگوی سرویس‌-seam موجود و بدون نشت
EF/SQL به دسکتاپ. این seam انواع نوبت (با رنگ)، حالت تقویم و تنظیمات پیامک را
نگه می‌دارد. سپس `AppointmentsQueryService.GetTypesAsync()` انواعِ کاربرساخته را
با کاتالوگ ثابت **ادغام** می‌کند (ثابت‌ها حذف نشوند؛ کاربرساخته‌ها اضافه شوند).

> چرا فایل JSON و نه D1/EF: این تنظیمات UI/tenant-preference هستند، نه دادهٔ
> تراکنشی. افزودن جدول/مهاجرت EF برای این‌ها overkill است و با ریسک backend-track
> همراه است. فایل JSON محلی ساده، آفلاین‌فرست و منطبق بر «استاندارد و مناسب، بدون
> اضافی» است. (در صورت وجود الگوی ترجیحِ محلی دیگر، از همان استفاده شود.)

---

## نحوهٔ استفاده از این سند

- تسک‌ها **به ترتیب** اجرا شوند؛ وابستگی‌ها در هر تسک ذکر شده.
- هر تسک: «هدف» → «فایل‌ها» → «گام‌های دقیق» → «پذیرش» → «وضعیت» → «پوش».
- جعبهٔ وضعیت: `[ ]` انجام‌نشده / `[~]` در حال انجام / `[x]` کامل.
- **ممنوع:** Full build/Full test/تست طولانی. فقط build هدفمند همان پروژه‌های
  لمس‌شده (`dotnet build` روی پروژهٔ مربوطه) برای اطمینان از کامپایل. بررسی بیهوده
  ممنوع.

---

## تسک ۱ — seam تنظیمات نوبت (انواع نوبت + حالت تقویم + پیامک)

**هدف:** یک مخزن واحدِ persist‌شده برای: (الف) انواع نوبتِ کاربرساخته با رنگ و
مدت پیش‌فرض، (ب) حالت تقویم (`TimeBased` = پیش‌فرض فعلی بر اساس ساعت / `DayOnly`
= فقط روزانه)، (ج) تنظیمات پیامک (فعال/غیرفعالِ «پیامک ثبت نوبت» و «پیامک یادآوری»
+ بازهٔ یادآوری به ساعت). این تسک فقط لایهٔ داده/seam است؛ UI در تسک ۲.

**وابستگی:** ندارد (اولین تسک).

**فایل‌هایی که می‌سازید:**
- `src/Animora.Contracts/Appointments/AppointmentSettingsModels.cs`
- `src/Animora.Contracts/Appointments/IAppointmentSettingsService.cs`
- `src/Animora.Client.Services/Appointments/AppointmentSettingsService.cs`

**فایل‌هایی که ویرایش می‌کنید:**
- `src/Animora.Client.Services/<DI extension>.cs` (همان فایلی که `AddClientServices`
  یا معادل آن در آن تعریف شده — با `grep -rn "IAppointmentsQueryService"
  src/Animora.Client.Services/*.cs` یا فایل ریشهٔ ثبت سرویس‌ها پیدا کنید و در کنار
  ثبت `AppointmentsQueryService` این سرویس را singleton ثبت کنید).

**گام‌های دقیق:**
1. در `AppointmentSettingsModels.cs` این رکوردها/enum را تعریف کنید:
   ```csharp
   public enum CalendarMode { TimeBased, DayOnly }

   // نوع نوبتِ کاربرساخته. ClassKey برای پالت داخلی، Color هگز رندومِ تثبیت‌شده.
   public sealed record CustomAppointmentType(
       string Id, string Label, string ColorHex, int DurationMinutes, bool RequiresDoctor);

   public sealed record AppointmentSmsSettings(
       bool SendOnBooking, bool SendReminder, int ReminderLeadHours);

   public sealed record AppointmentSettings(
       CalendarMode CalendarMode,
       AppointmentSmsSettings Sms,
       IReadOnlyList<CustomAppointmentType> CustomTypes);
   ```
   پیش‌فرض‌ها: `CalendarMode.TimeBased`، `Sms(true, true, 24)`، `CustomTypes = []`.
2. در `IAppointmentSettingsService.cs`:
   ```csharp
   Task<AppointmentSettings> GetAsync();
   Task SaveAsync(AppointmentSettings settings);
   Task<CustomAppointmentType> AddTypeAsync(string label, int durationMinutes, bool requiresDoctor); // رنگ را خودِ سرویس رندوم و تثبیت می‌کند
   Task RemoveTypeAsync(string typeId);
   Task SetCalendarModeAsync(CalendarMode mode);
   Task SetSmsAsync(AppointmentSmsSettings sms);
   ```
3. در `AppointmentSettingsService.cs` پیاده‌سازی با `System.Text.Json`:
   - مسیر فایل: زیر پوشهٔ دادهٔ برنامه. مسیر دقیق را از الگوی موجود `LocalStoreOptions`
     (در `src/Animora.Infrastructure.Local/Options/`) بگیرید؛ اگر `IOptions<LocalStoreOptions>`
     مسیر ریشهٔ داده دارد، فایل را در `<root>/appointment-settings.json` بنویسید.
     در غیر این صورت از `Environment.SpecialFolder.ApplicationData` + نام برنامه استفاده کنید.
   - خواندن: اگر فایل نبود، مقادیر پیش‌فرض را برگردانید (و فایل را لازم نیست بسازید).
   - نوشتن: atomic (نوشتن در فایل temp سپس `File.Move` با overwrite).
   - **کش در حافظه:** یک فیلد `AppointmentSettings? _cache` نگه دارید تا هر خواندن
     دیسک را hit نکند؛ بعد از Save، کش را به‌روز کنید. thread-safe با `lock` ساده.
   - **تولید رنگ رندومِ متمایز** (`AddTypeAsync`): از یک پالتِ ثابتِ خوش‌رنگ (۱۲ تا
     ۱۶ هگزِ از پیش‌انتخاب‌شده با کنتراست مناسب برای متن سفید/تیره) رنگی را انتخاب
     کنید که با رنگ‌های موجود (هم کاتالوگ ثابت در `AppointmentsPresentation.TypePalette`
     و هم CustomTypesِ موجود) تداخل نداشته باشد؛ اگر همه مصرف شده بود، رنگ تصادفی با
     `Random.Shared` بسازید و فقط یک‌بار تثبیت کنید. **هرگز در هر رندر دوباره رندوم
     نکنید** — رنگ در `ColorHex` ذخیره و persist می‌شود.
   - `Id` نوع جدید: `Guid.NewGuid().ToString("N")[..8]` یا اسلاگِ یکتا.
4. سرویس را singleton در DI ثبت کنید (کش در حافظه وابسته به singleton بودن است).

**پذیرش:**
- `dotnet build src/Animora.Client.Services` بدون خطا کامپایل شود.
- خواندن قبل از هر Save مقادیر پیش‌فرض بدهد؛ بعد از `AddTypeAsync` نوع با رنگ
  غیرتکراری در `GetAsync().CustomTypes` ظاهر شود و پس از ری‌استارت باقی بماند
  (persist). (بررسی منطقی کافی است؛ تست خودکار طولانی لازم نیست.)

**وضعیت:** `[x]` (پس از اتمام `[x]` کنید)

**پوش (اجباری):** از ریشهٔ ریپو:
```bash
scripts/git/push.sh -b genspark_ai_developer "feat(appointments): add appointment-settings seam (types+color, calendar mode, sms)"
```

---

## تسک ۲ — تب «تنظیمات» در صفحهٔ نوبت‌ها

**هدف:** یک تب چهارمِ «تنظیمات» کنار `cal`/`queue`/`hist` در تول‌بار صفحهٔ نوبت‌ها
که از `IAppointmentSettingsService` تغذیه می‌شود و امکان می‌دهد: افزودن/حذف نوع
نوبت (با نمایش رنگ)، انتخاب حالت تقویم (TimeBased/DayOnly)، و تنظیم دو سوئیچ پیامک
(ثبت نوبت + یادآوری) با فیلد ساعتِ یادآوری.

**وابستگی:** تسک ۱.

**فایل‌هایی که می‌سازید:**
- `src/Animora.Desktop/Pages/Appointments/AppointmentsSettingsTabView.axaml` (+ `.axaml.cs`)
- در صورت نیاز: آیتم‌-ViewModel برای ردیف نوع در
  `AppointmentsItemViewModels.cs` (همین فایل موجود را extend کنید، فایل جدید نسازید).

**فایل‌هایی که ویرایش می‌کنید:**
- `AppointmentsCalendarViewModel.cs`:
  - تزریق `IAppointmentSettingsService` در سازنده (پارامتر جدید) و ثبت DI آن در
    `App.axaml.cs` را کنترل کنید (اگر این سرویس در لیست ثبت نیست، در تسک ۱ ثبت شد).
  - فلگ `IsSettingsTab`، کلید تب `"settings"` در `SelectTab`، و پراپرتی‌های bind:
    `RangeObservableCollection<AppointmentSettingTypeRowViewModel> SettingTypes`،
    `bool CalendarModeIsDayOnly`، `bool SmsOnBooking`، `bool SmsReminder`،
    `int SmsReminderLeadHours` (+ متن‌های کمکی).
  - کامندها: `AddAppointmentType` (ورودی: label + duration + requiresDoctor از فیلدهای
    فرم تب)، `RemoveAppointmentType(row)`، و persist حالت تقویم/پیامک هنگام تغییر
    (در setterها `_ = _settings.SetCalendarModeAsync(...)` و `SetSmsAsync(...)`).
  - بعد از افزودن/حذف نوع: `_types` را از `_service.GetTypesAsync()` (که در تسک ۶
    merge می‌شود) دوباره بارگیری و `RebuildIndexes()` + `RenderActiveTab()` را صدا
    بزنید تا چیپ‌های تول‌بار و منوی ساخت نوبت فوراً هماهنگ شوند. همچنین
    `_changes.Notify(DataDomain.Appointments)` تا پنل بوکینگ بازِ بعدی تازه باشد.
- `AppointmentsCalendarView.axaml`:
  - در Grid تب‌ها `ColumnDefinitions="*,*,*"` را به `"*,*,*,*"` تغییر دهید و دکمهٔ
    چهارم با `CommandParameter="settings"`، `Classes.is-active="{Binding IsSettingsTab}"`،
    آیکن `IconTools` و متن «تنظیمات» اضافه کنید.
  - یک `LazyTab Key="settings"` با `DataTemplate` حاوی `<ap:AppointmentsSettingsTabView />`
    داخل `LazyTabHost` بیرونی اضافه کنید.
  - توجه: ردیف دوم/سوم تول‌بار با `IsVisible="{Binding ShowCalendarChrome}"` فقط در
    تب تقویم دیده می‌شوند — این درست است (در تب تنظیمات مخفی بمانند).

**محتوای `AppointmentsSettingsTabView.axaml` (سه کارت):**
1. «انواع نوبت»: فرم افزودن (TextBox نام + ComboBox/عددی مدت + ToggleButton «نیاز به
   پزشک») + دکمهٔ افزودن؛ زیرش `ItemsControl` روی `SettingTypes` که هر ردیف:
   دایرهٔ رنگ (bind به `ColorHex` با `HexBrushConverter`) + برچسب + مدت + دکمهٔ حذف.
   انواع ثابتِ کاتالوگ را «پیش‌فرض/غیرقابل‌حذف» نشان دهید (دکمهٔ حذف فقط برای
   CustomTypes).
2. «حالت تقویم»: دو گزینهٔ رادیویی/سگمنت: «بر اساس ساعت» و «فقط روزانه» — bind به
   `CalendarModeIsDayOnly`. متن توضیح کوتاه زیر هر گزینه.
3. «پیامک»: دو `switch-row` (مثل `BookingPanelView.axaml` خطوط ۵۹۷–۶۲۴ که از کلاس
   `switch-row` + `bk-switch` استفاده می‌کند) برای «پیامک ثبت نوبت» و «پیامک یادآوری»
   + یک فیلد عددی «ساعت‌ها قبل از نوبت» برای `SmsReminderLeadHours`. آیکن `IconSms`.

**نکات استایل/کارایی:** از استایل‌ها و براش‌های موجود صفحه استفاده کنید
(`PanelBrush`, `LineBrush`, `Radius`, کلاس‌های `switch-row`/`bk-switch`). از
`ReplaceAll` برای `SettingTypes` استفاده کنید.

**پذیرش:**
- `dotnet build src/Animora.Desktop` بدون خطا.
- تب «تنظیمات» ظاهر و فعال می‌شود؛ افزودن یک نوع جدید بلافاصله در لیست تب و (پس از
  رفرش) در چیپ‌های تب تقویم و در منوی ساخت نوبت دیده می‌شود؛ تغییر سوئیچ‌های پیامک و
  حالت تقویم persist می‌شود.

**وضعیت:** `[ ]`

**پوش (اجباری):**
```bash
scripts/git/push.sh -b genspark_ai_developer "feat(appointments): add Settings tab (types, calendar mode, sms)"
```

---

## تسک ۳ — رنگ رندومِ متمایز برای انواع و پزشک‌ها

**هدف:** هر نوع نوبت یک رنگِ متمایزِ مناسب (رندومِ تثبیت‌شده) بگیرد و این رنگ هم در
تول‌بار (چیپ نوع) و هم در منوی «ساخت نوبت جدید» (کارت انتخاب نوع + خلاصه) دیده شود.
عین همین برای پزشک‌ها/کارکنان (رنگ متمایز به‌جای `#5A67D8` یکسانِ فعلی).

**وابستگی:** تسک ۱ (منبع رنگِ انواع کاربرساخته). برای انواع ثابت و پزشک‌ها رنگ از
یک تولیدکنندهٔ قطعی (deterministic) مشتق می‌شود.

**مسئلهٔ ریشه‌ای (از قبل تحلیل‌شده):**
- در `AppointmentsMapping.cs` (خطوط ۲۹–۵۱) **همهٔ** انواع `Accent="#5A67D8"` و
  همهٔ پزشک‌ها `"#5A67D8"` دارند → رنگ‌ها عملاً یکسان.
- چیپ نوع در `AppointmentsCalendarView.axaml` به `Type.Accent` (خط ~۲۴۴) و چیپ پزشک
  به `Doctor.Accent` (خط ~۲۷۱) bind است. پنل بوکینگ رنگ نوع را از
  `AppointmentsPresentation.TypeBackground/TypeForeground(classKey)` می‌گیرد (پالت
  ثابتِ محدود) — برای انواع ناشناخته به رنگ خاکستری fallback می‌کند.

**فایل‌هایی که ویرایش می‌کنید:**
- `src/Animora.Client.Services/Appointments/AppointmentsMapping.cs`:
  - برای **انواع ثابت**: به‌جای `#5A67D8`، رنگ را از پالتِ `TypePalette`ـمانند یا از
    یک تابع hash قطعی روی `code` مشتق کنید (رنگِ باثبات و متمایز per-code). پیشنهاد:
    یک آرایهٔ ثابت از هگزهای خوش‌رنگ و اندیس = `hash(code) % N`.
  - برای `AppointmentDoctor.ToContract` (خط ۲۹): `Accent` را از hash قطعی روی
    `row.Id`/`row.Name` مشتق کنید (نه رنگ ثابت) تا هر پزشک رنگ متمایز بگیرد.
  - `Background`/`Foreground` نوع را از همان Accent مشتق کنید (مثلاً Accent با آلفای
    کم برای Background، و یک تیره‌تر برای Foreground) یا یک تابع کمکی مشترک بسازید.
- برای **انواع کاربرساخته** (از تسک ۱): `ColorHex` ذخیره‌شده مستقیم استفاده شود
  (در تسک ۶ که merge انجام می‌شود، `Accent = ColorHex`).
- `src/Animora.Desktop/Pages/Appointments/AppointmentsPresentation.cs`:
  - یک تابع عمومی `IBrush AccentFromHex(string hex)`، و توابعی برای مشتق‌کردن
    Background/Foreground از یک Accent هگز (تا پنل بوکینگ بتواند برای انواعِ خارج از
    `TypePalette` هم رنگِ درست نشان دهد، نه fallback خاکستری). این توابع باید از
    `BrushCache` استفاده کنند (بدون alloc).
- `BookingPanelViewModel.cs` (RenderSummary، خطوط ~۵۵۴–۵۵۶): به‌جای
  `TypeBackground(classKey)`/`TypeForeground(classKey)` از رنگِ خودِ
  `AppointmentType.Accent/Background/Foreground` (که اکنون per-type درست پر می‌شود)
  استفاده کنید؛ و کارت انتخاب نوع (`BookingTypeOptionViewModel`) هم رنگ نوع را نشان
  دهد (دایره/نوار رنگی bind به Accent). `BookingTypeOptionViewModels` را در
  `BookingPanelItemViewModels.cs` در صورت نیاز extend کنید.

**پذیرش:**
- `dotnet build src/Animora.Client.Services && dotnet build src/Animora.Desktop`.
- چیپ‌های نوع و پزشک در تول‌بار رنگ‌های متمایز دارند؛ در پنل ساخت نوبت، کارت نوع و
  خلاصه همان رنگ نوع را نشان می‌دهند؛ انواع کاربرساخته رنگِ ذخیره‌شده‌شان را دارند.

**وضعیت:** `[ ]`

**پوش (اجباری):**
```bash
scripts/git/push.sh -b genspark_ai_developer "feat(appointments): distinct per-type & per-doctor colors across toolbar and booking"
```

---

## تسک ۴ — حالت تقویم روزانه (DayOnly) در تقویم و در ثبت نوبت

**هدف:** وقتی `CalendarMode.DayOnly` انتخاب است، نوبت‌دهی بدون ساعتِ خاص باشد: شخص
فقط برای یک «تاریخ/روز» وقت می‌گیرد. هم خودِ تقویم و هم منوی ساخت نوبت باید با این
حالت بهینه و هماهنگ شوند. حالت `TimeBased` دقیقاً مثل الان بماند.

**وابستگی:** تسک ۱ (منبع `CalendarMode`) و بهتر است بعد از تسک ۲ (تا از UI قابل
تغییر باشد).

**تحلیل از پیش‌انجام‌شده:**
- نمای `month` در `AppointmentsCalendarViewModel.RenderMonth()` عملاً نمای «روزانهٔ
  بدون ساعت» است (کارت‌های روز، بدون time-grid). در `DayOnly` باید نمای پیش‌فرض و
  تنها‌نمای معنادار، همین «ماه/روز» باشد و سگمنت روز/هفته (time-grid) پنهان شود.
- در پنل بوکینگ (`BookingPanelViewModel`) بخش ساعت/اسلات (`RenderSlots`,
  `DurationOptions`, `SlotGroups`, `_start`, `FirstFree`) در `DayOnly` بی‌معناست و
  باید پنهان/غیرفعال شود؛ ثبت نوبت فقط با تاریخ + نوع + پزشک + بیمار انجام شود.
- `NewAppointmentRequest` فیلد `StartMinutes`/`DurationMinutes` اجباری دارد؛ در
  `DayOnly` یک مقدار قراردادی بگذارید (مثلاً `StartMinutes = 0` یا یک «all-day»
  marker ثابت، و `DurationMinutes = DefaultDurationMinutes نوع`). **مدل داده را
  تغییر ندهید** — فقط مقدار قراردادی و رفتار UI.

**فایل‌هایی که ویرایش می‌کنید:**
- `AppointmentsCalendarViewModel.cs`:
  - فیلد `CalendarMode _calendarMode` (از `IAppointmentSettingsService.GetAsync()` در
    `LoadAsync`). پراپرتی `bool IsDayOnlyMode`.
  - وقتی `DayOnly`: `_viewMode` را به `"month"` قفل کنید و در `Render`/`SetView`
    سگمنت‌های day/week را نادیده بگیرید؛ در View، سگمنت نما (Border خطوط ~۳۰۱–۳۱۷)
    با `IsVisible="{Binding !IsDayOnlyMode}"` پنهان شود.
  - کلیک روی سلولِ ماه در `DayOnly` → `OpenBooking` با seedِ فقط-تاریخ.
- `BookingPanelViewModel.cs`:
  - پراپرتی `bool IsDayOnly` (از settings در `LoadAsync`). وقتی true:
    - بخش ۴ (تقویم + اسلات): فقط تقویمِ انتخاب تاریخ بماند؛ گروه اسلات‌ها و
      انتخاب duration و «اولین زمان آزاد» پنهان شوند (`IsVisible` binding).
    - در `Submit`/`CreateAsync`: `StartMinutes` = مقدار قراردادی، `DurationMinutes`
      = `TypeOf(_type).DurationMinutes`. منطق conflict (`GetConflictsAsync`) را در
      `DayOnly` رد کنید (تداخل ساعتی بی‌معناست) — فقط یک شمارش «تعداد نوبت آن روز»
      نشان دهید.
  - خلاصه (`RenderSummary`): به‌جای «ساعت»، در `DayOnly` فقط «تاریخ» و «نوع» و شمارِ
    روز را نشان دهید.
- `BookingPanelView.axaml`: بخش اسلات/مدت را با binding `IsVisible` به یک پراپرتی
  معکوس (`!IsDayOnly`) مشروط کنید؛ متن‌های راهنمای ساعتی را در این حالت پنهان کنید.

**پذیرش:**
- `dotnet build src/Animora.Desktop`.
- با `TimeBased` رفتار دقیقاً مثل قبل است. با `DayOnly`: تقویم فقط نمای ماه/روز را
  نشان می‌دهد، پنل ساخت نوبت ساعت/اسلات ندارد و نوبت فقط با تاریخ ثبت می‌شود و در
  تقویم همان روز ظاهر می‌شود.

**وضعیت:** `[ ]`

**پوش (اجباری):**
```bash
scripts/git/push.sh -b genspark_ai_developer "feat(appointments): day-only calendar mode (no time slots) in calendar and booking"
```

---

## تسک ۵ — ثبت بیمار جدید با ویزارد اصلی + انتخاب خودکار + حفظ تنظیمات نوبت

**هدف:** در منوی «ثبت نوبت جدید»، دکمهٔ «بیمار جدید» به‌جای فرم inlineِ ساده، **منوی
اصلی ثبت بیمار** (`AddPatientWizard`) را باز کند؛ پس از ثبت، آن منو خودکار بسته شود،
بیمارِ تازه‌ساخته‌شده **به‌طور خودکار در پنل نوبت انتخاب** شود، و تمام تنظیماتی که
کاربر قبل از این در پنل نوبت انتخاب کرده بود (نوع، پزشک، تاریخ، ساعت، یادداشت،
سوئیچ‌های پیامک) **حفظ** شوند.

**وابستگی:** تسک ۳ (برای رنگ درست کارت بیمار؛ اختیاری) — عملاً مستقل.

**موانع فنی از پیش‌شناسایی‌شده (بحرانی):**
1. `IDialogService.Close()` **هر سه host را می‌بندد**. پنل بوکینگ روی SheetHost و
   ویزارد روی ModalHost است. اگر ویزارد در پایان `Close()` بزند، پنل بوکینگ هم بسته
   می‌شود → تنظیمات از بین می‌رود. **راه‌حل:** یک متد بستنِ فقط-مودال به
   `IDialogService` اضافه کنید: `void CloseModal();` که فقط `_modalHost.SetContent(null)`
   را صدا بزند (در `DialogService.cs`). ویزارد وقتی به‌عنوان زیرمنوی بوکینگ باز می‌شود
   باید از `CloseModal` استفاده کند، نه `Close`.
2. `AddPatientWizardViewModel._onCompleted` **بیمار ساخته‌شده را برنمی‌گرداند**
   (`Action` بدون پارامتر) و `CommitAsync` خروجی `CreateOwnerAsync/CreatePetAsync`
   را دور می‌ریزد. برای انتخاب خودکار، باید بیمارِ ساخته‌شده شناسایی شود.

**فایل‌هایی که ویرایش می‌کنید:**
- `src/Animora.Desktop/Services/IDialogService.cs` + `DialogService.cs`:
  - افزودن `void CloseModal();` (فقط ModalHost را پاک کند؛ fallbackWindow را هم اگر
    باز است ببندد).
- `src/Animora.Desktop/Pages/Patients/AddPatientWizardViewModel.cs`:
  - یک overload/ctor دوم یا پارامتر اختیاری که به‌جای `Action onCompleted` یک
    `Action<WizardResult>?` بپذیرد و پرچمی `bool closeModalOnly`. در `CommitAsync`،
    خروجی `CreateOwnerAsync`/`CreatePetAsync` (که `OwnerDetail`/`PetDetail` برمی‌گرداند
    — در `IPatientsQueryService` خطوط ۳۴/۳۷ تأیید شد) را نگه دارید و در callback پاس
    دهید. اگر `closeModalOnly` بود، به‌جای `_dialogs.Close()` از `_dialogs.CloseModal()`
    استفاده کنید. `WizardResult` می‌تواند شامل نام حیوان/گونه/نام مالک/شماره باشد تا
    پنل بوکینگ بدون جست‌وجوی مجدد بتواند `BookingPatient` بسازد یا با آن جست‌وجو کند.
  - **سازگاری عقب‌رو:** مسیر فعلی صفحهٔ بیماران (`PatientsPageViewModel.AddNewOwnerPet`)
    باید دست‌نخورده کار کند — از همان ctor با رفتار پیش‌فرض (Close کامل) استفاده کند.
- `src/Animora.Desktop/Pages/Appointments/BookingPanelViewModel.cs`:
  - تزریق `IPatientsQueryService` در سازنده (پارامتر جدید) — و در هرجا
    `new BookingPanelViewModel(...)` ساخته می‌شود (در `AppointmentsCalendarViewModel`
    خطوط ~۱۱۹۴ و ~۱۲۱۷) آرگومان جدید را پاس دهید؛ لذا `AppointmentsCalendarViewModel`
    هم باید `IPatientsQueryService` را تزریق کند (و DI در `App.axaml.cs`).
  - کامند `BeginAddPatient` را تغییر دهید: به‌جای `_adding = true` (فرم inline)،
    `AddPatientWizardViewModel` را بسازید و با `_dialogs.ShowModal(..., maxWidth:760)`
    باز کنید؛ callback آن `closeModalOnly=true` و پس از دریافت `WizardResult`:
    - `_dialogs.CloseModal()` (پنل بوکینگ روی sheet دست‌نخورده می‌ماند → همهٔ
      `_type/_doc/_date/_start/_dur/_note/_sms/_remind` حفظ می‌شوند).
    - بیمارِ جدید را انتخاب کنید: یا از `WizardResult` یک `BookingPatient` بسازید، یا
      `SearchPatientsAsync` را با شماره/نامِ برگشتی صدا بزنید و اولین نتیجه را
      `_selectedPatient` کنید؛ سپس `_picking=false`، `RenderPatient()` + `RenderSummary()`.
    - توست موفقیت مثل مسیر فعلی `SavePatient`.
  - **فرم inlineِ قدیمی** (`_adding`, `SavePatient`, بخش `pat-add` در XAML خطوط
    ۳۰۳–۳۵۴) را می‌توانید حذف کنید یا نگه دارید؛ برای «بدون اضافی» توصیه: حذف فرم
    inline و کامند `SavePatient`/`CancelAddPatient`/فیلدهای `NewName...` و بخش XAML
    `pat-add`، چون دیگر از ویزارد اصلی استفاده می‌شود. دکمهٔ `pat-new` (خط ۳۵۶) به
    `BeginAddPatientCommand` وصل می‌ماند.

**پذیرش:**
- `dotnet build src/Animora.Desktop`.
- مسیر صفحهٔ بیماران (افزودن مالک/حیوان) بدون تغییر رفتار کار کند.
- در پنل ساخت نوبت: انتخاب نوع/پزشک/تاریخ → کلیک «بیمار جدید» → ویزارد اصلی باز شود →
  ثبت → ویزارد بسته و بیمار جدید در پنل انتخاب‌شده باشد و نوع/پزشک/تاریخ/یادداشت/پیامک
  دست‌نخورده بماند.

**وضعیت:** `[ ]`

**پوش (اجباری):**
```bash
scripts/git/push.sh -b genspark_ai_developer "feat(appointments): open main patient wizard from booking, auto-select, preserve booking state"
```

---

## تسک ۶ — هم‌راستاسازی انواع ساخته‌شده با کاتالوگ در همه‌جا

**هدف:** انواع نوبتِ ساخته‌شده از تب تنظیمات، همه‌جا (چیپ تول‌بار، فیلتر، کارت نوع در
ساخت نوبت، خلاصه، تاریخچه) به‌عنوان نوعِ معتبر شناخته و با رنگ خودشان نمایش داده شوند.

**وابستگی:** تسک ۱، ۲، ۳.

**تحلیل از پیش‌انجام‌شده:** تنها منبعِ انواع برای UI، `IAppointmentsQueryService.
GetTypesAsync()` است که در `AppointmentsQueryService.cs` (خط ۳۴) مقدار ثابت
`AppointmentsMapping.Types` را می‌دهد. کافی است این متد، انواع ثابت را با
`CustomTypes` از `IAppointmentSettingsService` **merge** کند.

**فایل‌هایی که ویرایش می‌کنید:**
- `src/Animora.Client.Services/Appointments/AppointmentsQueryService.cs`:
  - `IAppointmentSettingsService` را تزریق کنید (پارامتر سازنده؛ DI قبلاً ثبت شده).
  - `GetTypesAsync` را async کنید: `var custom = (await _settings.GetAsync()).CustomTypes;`
    و خروجی = `AppointmentsMapping.Types` + `custom.Select(c => new AppointmentType(
    c.Id, c.Label, c.Id /*classKey یکتا*/, c.ColorHex, <bgمشتق>, <fgمشتق>, "IconCalendar",
    c.DurationMinutes))`. از توابع مشتق رنگِ تسک ۳ برای bg/fg استفاده کنید.
  - مراقب باشید انواعِ ثابت حذف نشوند و idها یکتا بمانند (پیشوند برای custom اگر لازم).
- اطمینان از رفرش: در `BookingPanelViewModel.LoadAsync` و
  `AppointmentsCalendarViewModel.LoadAsync` انواع از همین متد خوانده می‌شوند؛ چون
  تسک ۲ پس از افزودن نوع، reload + `RebuildIndexes` + `Notify(Appointments)` می‌زند،
  هماهنگی تضمین می‌شود. فقط بررسی کنید `RefreshOnDataChange` در
  `AppointmentsCalendarViewModel` (خط ۱۰۵) دامنهٔ `DataDomain.Appointments` را هم
  پوشش دهد؛ اگر نه، اضافه کنید.

**پذیرش:**
- `dotnet build src/Animora.Client.Services`.
- نوعِ ساخته‌شده در تب تنظیمات، بلافاصله در چیپ تول‌بار و در کارت انتخاب نوعِ پنل
  ساخت نوبت (با رنگ خودش) ظاهر می‌شود و نوبتِ ثبت‌شده با آن نوع، در تقویم درست رنگ
  می‌گیرد.

**وضعیت:** `[ ]`

**پوش (اجباری):**
```bash
scripts/git/push.sh -b genspark_ai_developer "feat(appointments): merge custom types into GetTypesAsync so new types work everywhere"
```

---

## تسک ۷ — اصلاح تول‌بار و دکمه‌ها (دیزاین)

**هدف (چند موردِ مشخص):**
1. **جهتِ دکمه‌های ناوبری تقویم برعکس است.** در RTL، «بازهٔ قبلی» باید فلشِ راست و
   «بازهٔ بعدی» فلشِ چپ باشد (یا بالعکسِ درستِ بصری). الان در
   `AppointmentsCalendarView.axaml` خطوط ۳۲۱–۳۳۲: `StepPrevious` از `IconChevronLeft`
   و `StepNext` از `IconChevronRight` استفاده می‌کند که در RTL حسِ معکوس می‌دهد.
   **اصلاح:** آیکن‌ها را جابه‌جا کنید (`StepPrevious` → `IconChevronRight`،
   `StepNext` → `IconChevronLeft`) تا جهت با منطق قبلی/بعدیِ RTL هم‌خوان شود. ترتیب
   قرارگیری دو دکمه در `StackPanel` را هم بررسی و در صورت لزوم اصلاح کنید.
2. **استایل بدِ دکمه‌ها:** دکمه‌های `ap-icon` (نویگیشن) و دکمهٔ «امروز» را بهبود دهید:
   هماهنگی padding/corner، استیت hover واضح‌تر، پس‌زمینهٔ قاب دکمه‌های فلش
   (Border خط ۳۱۹) ظریف‌تر و منظم‌تر. مقادیر را با سایر چیپ‌ها/سگمنت‌ها هماهنگ کنید
   (از همان توکن‌های `Bg2Brush`/`PanelBrush`/`LineBrush`/`Radius`). هیچ رنگ/عدد خارج
   از توکن‌های موجود اضافه نکنید مگر لازم.
3. **آیکن دکمهٔ «ثبت نوبت جدید»** (خطوط ۱۴۲–۱۴۸) الان `IconPlus` است و «خوب نیست».
   **اصلاح:** از آیکنِ معنادارترِ تقویم استفاده کنید. چون `IconCalendarPlus` وجود
   ندارد، یکی از این دو:
   - (ترجیحی) یک آیکن جدید `IconCalendarPlus` به
     `src/Animora.Desktop/Resources/Theme/Icons.axaml` اضافه کنید (path دادهٔ یک
     تقویم با علامت + داخلش؛ سبک stroke مثل بقیهٔ آیکن‌ها، `viewBox` ۲۴).
   - یا ترکیبِ `IconCalendar` به‌تنهایی به‌عنوان آیکن دکمه.
   توصیه: افزودن `IconCalendarPlus` تمیزتر است.
4. **حذف کامل متن «روی تقویم کلیک کنید یا بکشید …»** در دو محل:
   - `src/Animora.Desktop/Pages/Appointments/CalendarEmptyState.axaml` خطوط ~۴۲–۵۰:
     کلِ `Border` حاوی آن `TextBlock` را حذف کنید (یا فقط آن جمله را). چون این
     drag/drop در دسکتاپ پیاده نشده، متن گمراه‌کننده است.
   - `src/Animora.Desktop/Pages/Appointments/BookingPanelView.axaml` خط ۶۵۷:
     `TextBlock` با متن «روی تقویم کلیک کنید یا بکشید …» را حذف کنید؛ اگر `Border`
     `bk-tip` بعد از حذف خالی می‌شود، کل آن tip را حذف کنید.

**فایل‌هایی که ویرایش می‌کنید:** `AppointmentsCalendarView.axaml`،
`CalendarEmptyState.axaml`، `BookingPanelView.axaml`، و در صورت افزودن آیکن:
`Resources/Theme/Icons.axaml`.

**پذیرش:**
- `dotnet build src/Animora.Desktop`.
- جهت فلش‌های قبلی/بعدی در RTL درست است؛ دکمه‌ها منظم‌تر؛ دکمهٔ ثبت نوبت آیکن تقویمی
  دارد؛ هیچ‌جای صفحهٔ نوبت‌ها جملهٔ «کلیک کنید یا بکشید» دیده نمی‌شود.

**وضعیت:** `[ ]`

**پوش (اجباری):**
```bash
scripts/git/push.sh -b genspark_ai_developer "fix(appointments): rtl nav arrows, toolbar button styling, booking icon, remove drag hint"
```

---

## تسک ۸ — حذف فایل پلن (تسک پایانی)

**هدف:** پس از تکمیل و `[x]` شدن تسک‌های ۱ تا ۷، این فایل پلن حذف شود. **به هیچ فایل
دیگری و هیچ road-map دیگری دست نزنید.**

**گام:**
```bash
cd /home/user/webapp && rm -f Plan/APPOINTMENTS-ENHANCEMENTS.md
# اگر پوشهٔ Plan خالی شد، آن را هم حذف کنید:
rmdir Plan 2>/dev/null || true
```

**وضعیت:** `[ ]`

**پوش (اجباری):**
```bash
scripts/git/push.sh -b genspark_ai_developer "chore(appointments): remove completed plan file"
```

---

## یادآوری‌های نهایی برای مدل مجری

- بعد از **هر** تسک، اسکریپت پوش را اجرا کنید (قانون پوش). اگر خروجی کد ۳ داد،
  ابزار `setup_github_environment` را صدا بزنید و دوباره همان را اجرا کنید؛ اگر کد ۲
  (conflict) داد، ریپو تمیز می‌ماند — حل کنید و دوباره اجرا کنید.
- اگر در یک تسک فیکس/کدِ سنگین نوشتید، همان‌جا یک پوشِ میانیِ با پیام واضح بزنید.
- فقط پروژهٔ لمس‌شده را build کنید؛ Full build/Full test/تست طولانی ممنوع.
- بررسی فقط فایل‌های نام‌برده؛ بررسی بیهوده ممنوع. استاندارد و بهینه، بدون اضافی.
