# Plan — حذف صفحهٔ «خرید و تأمین» + بازطراحی کامل «واکسیناسیون» (دارو و واکسن)

> این فایل خودکفاست. تمام مسیرها، یافته‌ها، تصمیم‌ها و الگوهای کد که برای پیاده‌سازی لازم است
> اینجا آمده تا یک مدل در سشنِ تازه بدون بازبینی دوبارهٔ ریپو مستقیم شروع به کد زدن کند.
> **قانون قفل‌شده:** بعد از تکمیل هر تسک، `scripts/git/push.sh "<msg>"` اجرا و پوش شود. اگر کد یک تسک
> سنگین شد، همان‌جا وسطِ تسک هم پوش کن. نبودِ پوش = شکست.

---

## ۰. شناختِ پروژه (خلاصهٔ خالص — لازم نیست دوباره بخوانی)

- **Animora**: اپ دسکتاپ مدیریت کلینیک دامپزشکی. **.NET 10 + Avalonia + MVVM (CommunityToolkit.Mvvm)**. فارسی/RTL، تقویم جلالی.
- معماری لایه‌ای (قانون حاکم بر هر تغییر):
  `View → ViewModel → interface در Animora.Contracts (DI سازنده) → پیاده‌سازی واقعی در Animora.Client.Services → dispatch در Animora.Application → SQLite محلی (Animora.Persistence.Sqlite)`.
  - ViewModelها فقط به **interfaceهای Contracts** وابسته‌اند؛ هرگز به سرویس concrete یا EF/SQL.
  - افزودن نیاز دادهٔ یک صفحه: interface+DTO در `Contracts/<Area>/` → پیاده‌سازی در `Animora.Client.Services/<Area>/` روی dispatcher → ثبت هر دو در `src/Animora.Desktop/App.axaml.cs`.
  - RTL یک‌بار در `App.axaml` ست شده؛ در صفحه ست نکن.
- **Build/verify (sandbox):**
  - سریع (فقط Desktop): `scripts/sandbox/build.sh` → باید **0 warning / 0 error**.
  - کل solution: `scripts/sandbox/build.sh full`.
  - **ممنوع:** full test و اجرای طولانی/smoke مگر لازم. بیلد سریع کافی است؛ اگر DI/Shell/startup دست خورد، یک بار `build.sh` کامل.
- **اسکریپت پوش (قفل):** `scripts/git/push.sh "type(scope): msg"` → خودش `add -A → commit → fetch → rebase → push`. اگر کد ۳ برگشت، ابزار `setup_github_environment` را یک‌بار صدا بزن و دوباره اجرا کن. برنچ کاری: `genspark_ai_developer` (با `-b genspark_ai_developer` در اولین پوش).

---

## ۱. وضعیت فعلی (یافته‌های دقیق — مبنای تصمیم‌ها)

### ۱.الف) «خرید و تأمین» (Purchasing) — دامنهٔ حذف
- در **sidebar اصلاً نیست** (در `NavigationMenu.cs` وجود ندارد). فقط به‌صورت **کاشیِ دسترسی سریع شمارهٔ ۸** در داشبورد است.
- فایل‌های UI اختصاصی (کلِ این‌ها حذف می‌شوند):
  - `src/Animora.Desktop/Pages/Purchasing/` → `PurchasingItemViewModels.cs`, `PurchasingPageView.axaml`, `PurchasingPageView.axaml.cs`, `PurchasingPageViewModel.cs`.
- نقاطِ ارجاع در UI که باید پاک/اصلاح شوند (دقیقاً این‌ها و نه بیشتر):
  1. `src/Animora.Desktop/Pages/Dashboard/DashboardQuickAction.cs`
     - `using Animora.Desktop.Pages.Purchasing;` (خط ۴) → حذف.
     - آیتم آخر آرایهٔ `All` (کاشیِ «خرید و تأمین» با `typeof(PurchasingPageViewModel)`, AltKey=8) → حذف کامل آن سطر.
  2. `src/Animora.Desktop/App.axaml.cs`
     - خط ~۳۵۰: `services.AddSingleton<IPurchasingQueryService, PurchasingQueryService>();` → حذف.
     - خط ~۳۵۱: `services.AddSingleton<PurchasingPageViewModel>();` → حذف.
     - usingهای مرتبط با Purchasing اگر بعد از حذف بلااستفاده شدند → حذف (مراقب باش `PurchasingQueryService` از namespace `Animora.Client.Services.Purchasing` و interface از `Animora.Contracts.Purchasing` می‌آید).
  3. `src/Animora.Desktop/Shell/NavPermissions.cs`
     - خط ~۶۰: `[typeof(Pages.Purchasing.PurchasingPageViewModel)] = "purchasing",` → حذف.
  4. `src/Animora.Desktop/Shell/ViewFactory.cs`
     - خط ~۱۰۵: `Register<...Purchasing.PurchasingPageViewModel, ...Purchasing.PurchasingPageView>(factories);` → حذف.
  5. `src/Animora.Desktop/Shell/SidebarViewModel.cs`
     - خط ~۶۴ فقط یک **کامنت** است که Purchasing را مثال می‌زند؛ متن کامنت را به مثال دیگری (مثلاً یک صفحهٔ بدون sidebar) تغییر بده یا کلمهٔ Purchasing را از کامنت بردار. تغییرِ رفتاری ندارد.

- **دامنهٔ حذف فقط UI است.** کلاسِ Contracts/Client.Services مربوط به Purchasing (`IPurchasingQueryService`, `PurchasingQueryService`) **فقط** در همین UI مصرف می‌شوند (تأیید شد با grep: تنها مصرف‌کنندهٔ `IPurchasingQueryService`، صفحهٔ Purchasing و ثبتِ DI است). پس می‌توان این دو فایل را هم حذف کرد:
  - `src/Animora.Contracts/Purchasing/IPurchasingQueryService.cs`
  - `src/Animora.Contracts/Purchasing/PurchasingModels.cs` (**فقط اگر** جای دیگری import نشود — قبل حذف `grep -rn "Animora.Contracts.Purchasing" src/` بزن؛ اگر مصرف دیگری نبود حذف کن، در غیر این صورت نگه‌دار.)
  - `src/Animora.Client.Services/Purchasing/PurchasingQueryService.cs`
- **ممنوعِ حذف (خطِ قرمز):** لایهٔ **backend/Domain/Application/Persistence** مربوط به Purchasing/Supplier را **دست نزن**. این‌ها عمیقاً در Finance/Cheques/Statistics/Inventory/migrations تنیده‌اند (۷۵ فایل به `Supplier` ارجاع می‌دهند) و حذفشان migrations و حسابداری را می‌شکند. تسک کاربر «صفحهٔ خرید» است، نه دامنهٔ خرید. این‌ها نگه داشته می‌شوند:
  - `src/Animora.Domain/Purchasing/**`, `src/Animora.Application/UseCases/Purchasing/**`, `src/Animora.Application/Ports/IPurchasing*`, `src/Animora.Persistence.Sqlite/**/Purchasing/**`, و هر ارجاع در Finance/Cheques/Statistics/Inventory.
- **تأیید نهاییِ حذف UI:** بعد از حذف، `grep -rn "Purchasing" src/Animora.Desktop/` نباید هیچ ارجاع کدی به `PurchasingPageViewModel`/`PurchasingPageView`/`IPurchasingQueryService` بدهد (کامنتِ SidebarViewModel اگر اصلاح شد، استثناست). بیلد سریع باید 0/0 شود.

### ۱.ب) «واکسیناسیون» (Vaccination) — وضعیت فعلی
- **صفحه:** `src/Animora.Desktop/Pages/Vaccination/` با چهار تب (segment) در `VaccinationPageView.axaml`:
  - `visits` («نوبت») · `stock` («انبار واکسن») · `settings` («تنظیمات») · `history` («تاریخچه»).
  - ViewModel: `VaccinationPageViewModel.cs` — state با `ActiveSection` و پراپرتی‌های `IsVisitsSection/IsStockSection/IsSettingsSection/IsHistorySection`.
- **مدال ثبت:** `AddVaccineModalViewModel.cs` + `AddVaccineModalView.axaml` — انتخاب بیمار (الگوی booking سروری)، نوع واکسن از catalog، dose/تاریخ/دامپزشک/lot، سوییچ ارسال تأیید. **اما `Submit()` فقط toastِ «نمونه طراحی» می‌زند — هیچ داده‌ای ثبت نمی‌شود.**
- **Contracts:** `src/Animora.Contracts/Vaccination/`:
  - `IVaccinationService.cs`: متدها = `GetRecordsAsync`, `GetCatalogAsync`, `GetSmsTemplatesAsync`, `MarkDoneAsync`, `BuildSmsPreviewAsync`, `SendSmsAsync`, `GetSmsHistoryForOwnerAsync`, `GetHistoryAsync`.
    **هیچ متدِ «ثبت/تجویز واکسن یا دارو» وجود ندارد** → باید اضافه شود.
  - `VaccinationModels.cs`: `VaccinationStatus{Due,Overdue,Scheduled,Done}`, `SmsState`, رکورد `VaccinationRecord(...)`, `VaccineCatalogEntry(...)`, `SmsTemplate`, `OwnerSmsReminder`, `SmsPreview`.
    **هیچ مفهومِ «نوع واکسن» (کلینیک/خانگی/پانسیون) و هیچ مفهومِ «دارو» و «زمان‌بندی دوره‌ای/تاریخ‌های خاص» نیست** → باید اضافه شود.
- **Client.Services:** `src/Animora.Client.Services/Vaccination/` → `VaccinationService.cs` (روی dispatcher)، `VaccinationMapping.cs`، `SmsTemplateRenderer.cs`.
- **Application (backend):** `src/Animora.Application/UseCases/Vaccination/`:
  - `VaccinationQueries.cs`: `VaccinationDueQuery`, `VaccinationCatalogQuery` (catalog را از `VaccineSchedule.All` می‌سازد — ثابت و فقط ۶ کد)، `SmsTemplatesQuery`, `VaccinationBoostersRangeQuery` (پروجکشن booster برای تقویم Appointments، بازهٔ حداکثر ۶۲ روز)، `OwnerSmsRemindersQuery`, `VaccinationHistoryQuery`.
  - `VaccinationCommands.cs`: فقط `MarkVaccinationDoneCommand` و `RecordVaccinationReminderCommand`. **هیچ command «ثبت واکسن/دارو جدید» و «زمان‌بندی» نیست.**
- **Domain:** `src/Animora.Domain/Clinical/`:
  - `VaccinationRecord.cs` (AggregateRoot): فیلدها = PetId, VaccineCode, VaccineName, DoseNumber, TotalDoses, AdministeredUtc, AdministeredByUserId, BatchNumber, NextDueUtc, State{Administered,DueSoon,Overdue,Skipped}, VisitId, Notes. متد `Administer(...)` و `StateAt(now)` و `Skip()`. **رکورد فعلاً فقط «دوزِ تزریق‌شده» را مدل می‌کند؛ مفهوم «برنامهٔ آتی با چند تاریخ» را ندارد.**
  - `VaccineSchedule.cs`: جدول ثابتِ کد→بازهٔ روز (DHPP/RABIES/FVRCP/FELV/LEPTO/BORDETELLA). پایانِ هر سری ۳۶۵ روز.
  - `VaccinationThresholds.cs`: سطل‌های Overdue/DueSoon/Upcoming/Scheduled/Done.
- **Persistence:** `VaccinationRecordConfiguration.cs`, `Queries/VaccinationQueries.cs`, `Repositories/VaccinationRepository.cs`. آخرین migration = `__030_BoardingPrepaidRoom`. migration بعدی **باید** `__031_...` با زمانِ بزرگ‌تر باشد و snapshot آپدیت شود (با `dotnet ef` یا دستی طبق الگوی ۰۲۹).
- **Permissions:** `VaccinationRead="vaccination.read"`, `VaccinationWrite="vaccination.write"` در `PermissionCodes.cs`. CommandTypes: `vaccination.record.administer`, `vaccination.record.reminder` در `Contracts.Wire/Commands/CommandTypes.cs` (هر دو در آرایهٔ all-list آنجا هم باید اضافه شود اگر CommandType جدید ساختی).
- **کنترل‌های آماده برای استفاده مجدد:**
  - `src/Animora.Desktop/Controls/JalaliDatePicker.axaml(.cs)` — انتخاب تاریخ جلالی.
  - تقویم Appointments: `src/Animora.Desktop/Pages/Appointments/` شامل `MonthGridView.axaml`, `WeekGridView.axaml`, `DayGridView.axaml`, `AppointmentsCalendarMath.cs`, `AppointmentsCalendarViewModel.cs` (۲۳۴۴ خط). این‌ها **الگوی مرجع** برای ساخت تقویم واکسن‌اند — از ریاضیِ جلالی و چیدمانِ ماهانه‌شان ایده بگیر، ولی تقویمِ واکسن مستقل و سبک ساخته شود (استفادهٔ مجدد از `AppointmentsCalendarMath` اگر public/قابل‌استفاده بود مجاز).
  - Localization: `src/Animora.Desktop/Localization/` — `PersianNumberFormatter`, `PersianText`, helperهای جلالی.

### ۱.ج) نیازمندی‌های کاربر برای واکسیناسیون (تفسیر دقیق)
> هدف: صفحهٔ واکسیناسیون را به **«دارو و واکسن»** تبدیل کنیم؛ برای یک بیمارِ انتخابی بشود چند واکسن/دارو ثبت کرد، با سناریوهای زمان‌بندیِ متنوع، و یک **تقویم** که دقیقاً نشان دهد چه روزی چه چیزی باید زده/داده شود.

1. **سه نوع (kind) برای هر واکسن/دارو — هنگام ثبت انتخاب می‌شود:**
   - `ClinicVisit` = بیمار باید برای واکسن به کلینیک مراجعه کند.
   - `HomeByOwner` = مالک در خانه می‌زند → قابلیت ارسال پیامک یادآور.
   - `BoardingAdminister` = بیمار در پانسیون است و باید دارو/واکسن برایش استفاده شود.
   - هر نوع باید در تقویم و لیست با **رنگ/استایلِ خاصِ خودش** متمایز شود (سه tone/رنگ ثابت).
2. **بخش به «دارو و واکسن» تغییر کند:** علاوه بر واکسن، بشود **دارو** هم تعریف/ثبت کرد. یعنی موجودیت ثبت‌شده می‌تواند از نوعِ `Vaccine` یا `Medication` باشد (`ItemType`).
3. **تقویم دقیق و بهینه:** برای هر برنامهٔ ثبت‌شده مشخص کند در چه **روزهایی** باید انجام شود. نماهای ماه/هفته (حداقل ماه). هر رویداد با رنگِ نوعش. امکان فهمیدنِ «برای بیمار X، واکسن/داروی Y در چه روزهایی».
4. **سناریوهای زمان‌بندی (همه باید قابل‌انتخاب باشند — ادمین محدود نشود):**
   - `Once` = یک تاریخِ مشخص.
   - `EveryNDays` = هر N روز یک‌بار بین تاریخ شروع و پایان/یا به تعداد دفعات (مثلاً هر ۲ روز).
   - `SpecificDates` = مجموعه‌ای از تاریخ‌های دستیِ انتخابی.
   - (توصیه: `WeeklyOnDays`/`Protocol` را هم اگر کم‌هزینه بود اضافه کن؛ ولی سه مورد بالا حداقلِ لازم است.)
   - خروجیِ هر زمان‌بندی = مجموعه‌ای از **occurrenceها** (تاریخ + وضعیت انجام‌شده/نشده) که تقویم و لیست از آن تغذیه می‌کنند.
5. **اتصال به انبار:** واکسن‌ها از بخش «انبار واکسن» قابل‌مشاهده باشند و بشود واکسنِ جدید اضافه کرد. (فعلاً catalog از `VaccineSchedule.All` ثابت می‌آید؛ باید افزودنِ آیتم کاتالوگ/انبار ممکن شود — در ساده‌ترین حالتِ قابل‌قبول، افزودنِ واکسن به کاتالوگِ قابل‌ثبت.)
6. **همهٔ سناریوها پوشش داده شوند** — حتی مواردی که کاربر نگفته ولی لازم است (ویرایش/حذف برنامه، علامت‌زدن یک occurrence به‌عنوان انجام‌شده، فیلتر تقویم بر اساس نوع/نوعِ آیتم).

---

## ۲. تصمیم‌های معماریِ قطعی‌شده (تا مدل دوباره فکر نکند)

- **مدلِ جدید در Domain:** یک aggregate جدید **`TreatmentPlan`** (برنامهٔ دارو/واکسن) در `src/Animora.Domain/Clinical/` ساخته می‌شود تا رکوردِ تاریخیِ موجود (`VaccinationRecord` = دوزِ تزریق‌شده) دست‌نخورده بماند و حسابِ backendِ فعلی نشکند.
  - `TreatmentPlan`: `Id, TenantId, PetId, ItemType {Vaccine|Medication}, Kind {ClinicVisit|HomeByOwner|BoardingAdminister}, Title (نام واکسن/دارو), VaccineCode? , Dosage?, Notes?, Schedule (نوع+پارامترها), CreatedByUserId, CreatedUtc, State {Active|Completed|Cancelled}`.
  - occurrenceها در جدولِ جدا **`TreatmentOccurrence`**: `Id, PlanId, TenantId, DueDateUtc (DateOnly→UTC midnight), DoneUtc?, DoneByUserId?, State {Pending|Done|Skipped}, SortOrder`.
  - تولید occurrenceها هنگام ساختِ plan (از روی schedule) انجام می‌شود و با یک سقف (مثلاً حداکثر ۳۶۵ occurrence یا بازهٔ حداکثر یک سال) محدود شود تا رشدِ بی‌نهایت نگیرد.
- **چرا aggregate جدا و نه گسترشِ VaccinationRecord؟** چون `VaccinationRecord` معنایِ «دوزِ تزریق‌شده و تاریخِ آتی مشتق‌شده» دارد و منطقِ `Administer/StateAt` به آن گره خورده؛ افزودنِ «برنامهٔ چند-تاریخه + نوع + دارو» به آن، منطقِ موجود و مصرف‌کنندگانِ Appointments/History را می‌شکند. aggregate جدا، ریسک را صفر می‌کند و تقویمِ جدید از آن تغذیه می‌شود.
- **Persistence:** migration جدید `__031_TreatmentPlans` با دو جدول `TreatmentPlans` و `TreatmentOccurrences` طبق الگوی `__029_RecurringExpenses` (ستون‌های استاندارد: `TenantId, UpdatedAt, Version, Deleted`). snapshot آپدیت شود. Configuration + Repository + Queries جدید.
- **Contracts جدید (در `Animora.Contracts/Vaccination/`):** enumهای `TreatmentItemType`, `TreatmentKind`, `TreatmentScheduleType`; رکوردهای `TreatmentPlanInput` (ورودیِ ثبت از مدال), `TreatmentPlanSummary`, `CalendarOccurrence(Date, PlanId, PetName, Title, ItemType, Kind, State, Tone)`. متدهای جدید در `IVaccinationService`:
  - `Task<string> CreateTreatmentPlanAsync(TreatmentPlanInput input)`
  - `Task<IReadOnlyList<CalendarOccurrence>> GetCalendarAsync(string fromJalali, string toJalali, filters...)`
  - `Task MarkOccurrenceDoneAsync(string occurrenceId)`
  - `Task CancelPlanAsync(string planId)` (و در صورت نیاز ویرایش)
  - افزودنِ واکسن به کاتالوگ: `Task AddCatalogVaccineAsync(...)` یا گسترشِ catalog به منبعِ قابل‌نوشتن.
- **App/Application:** command‌های جدید `CreateTreatmentPlanCommand`, `MarkTreatmentOccurrenceDoneCommand`, `CancelTreatmentPlanCommand` + queryهای `TreatmentCalendarRangeQuery`, `TreatmentPlansForPetQuery`. CommandTypeهای جدید در `CommandTypes.cs` (و all-list). permissionها: از همان `VaccinationWrite/Read` استفاده شود (آیتمِ دارو هم زیرِ همین مجوز منطقی است) مگر لازم شد مجوز جدید.
- **UI صفحه:**
  - تبِ جدید **«تقویم»** اضافه شود (`calendar`) + عنوانِ صفحه/منوها به «دارو و واکسن» به‌روزرسانی شود (لیبلِ sidebar در `NavigationMenu.cs` از «واکسیناسیون» به «دارو و واکسن» و کاشیِ داشبورد مشابه — این تغییرِ لیبل ساده است و با state جدید هماهنگ).
  - مدالِ ثبت (`AddVaccineModalViewModel`) بازنویسی شود تا: انتخابِ `ItemType` (واکسن/دارو)، `Kind` (سه‌گانه با رنگ)، عنوان/دوز، و **زمان‌بندی** (Once با JalaliDatePicker / EveryNDays با N و بازه / SpecificDates با افزودن چند تاریخ) و در صورتِ `HomeByOwner` گزینهٔ پیامک. `Submit()` واقعاً `CreateTreatmentPlanAsync` را صدا بزند و toast واقعی بدهد + notify داده‌ها.
  - تقویم: کنترل/نمای ماهانهٔ سبک که `CalendarOccurrence`ها را با رنگِ `Kind` رندر کند؛ کلیک روی روز → لیست occurrenceهای آن روز؛ علامتِ «انجام شد».
  - تبِ «انبار واکسن»: افزودنِ واکسن جدید به کاتالوگ فعال شود (دکمهٔ «ثبت واکسن جدید» موجود → به ثبتِ کاتالوگ وصل شود، نه فقط toast).
- **رنگِ سه نوع (ثابت، در Presentation):**
  - `ClinicVisit` → آبی/برند (مثلاً `#1570cd`)
  - `HomeByOwner` → سبز (`#027a48`)
  - `BoardingAdminister` → نارنجی/کهربایی (`#b54708`)
  - این‌ها در `VaccinationPresentation.cs` به‌صورت brush/tone اضافه شوند (الگوی موجودِ همان فایل).

---

## ۳. تسک‌لیست اجرایی (به‌ترتیب؛ بعد از هرکدام پوش)

> وضعیت‌ها: `[ ]` انجام‌نشده · `[~]` در حال انجام · `[x]` انجام‌شده.
> هر تسک = یک commit/پوش مستقل با پیام مشخص. اگر تسک بزرگ شد، وسطش هم پوش کن.

### T0 — آماده‌سازی برنچ و پوش اولیهٔ پلن
- `[x]` ساخت همین فایل پلن در `Plan/`.
- `[x]` `scripts/git/push.sh -b genspark_ai_developer "docs(plan): add purchasing-removal + vaccination redesign plan"` (اگر کد ۳ → `setup_github_environment` سپس دوباره).

### T1 — حذف کامل صفحهٔ «خرید و تأمین» (فقط UI) ✅ انجام شد
- `[x]` حذف پوشهٔ `src/Animora.Desktop/Pages/Purchasing/` (هر ۴ فایل).
- `[x]` اصلاح `DashboardQuickAction.cs`: using و سطرِ کاشیِ «خرید و تأمین» حذف شد. کامنت هم به‌روز شد.
- `[x]` اصلاح `App.axaml.cs`: دو خط DI + سه usingِ UI/Contracts/Client Purchasing حذف شد. **نکته:** خط `using Animora.Application.UseCases.Purchasing;` نگه داشته شد چون `AddPurchasingUseCases()` (backend) از آن است.
- `[x]` `NavPermissions.cs`: هم سطرِ ByPage و هم `["purchasing"]=PurchasingRead` حذف شد. `ViewFactory.cs`: سطر Register حذف شد.
- `[x]` کامنتِ `SidebarViewModel.cs`: مثال از Purchasing به Reception تغییر کرد.
- `[x]` حذف `IPurchasingQueryService.cs`, `PurchasingModels.cs` (Contracts) و `PurchasingQueryService.cs` (Client) + پوشه‌های خالی. تأیید شد `Animora.Contracts.Purchasing` فقط در همین‌ها مصرف می‌شد.
- `[x]` **خطِ قرمز رعایت شد:** Domain/Application/Persistence/Finance/Cheques مربوط به Purchasing/Supplier دست نخورد.
- `[x]` بیلد سریع 0/0 ✅. `grep` در `src/Animora.Desktop/` برای انواع حذف‌شده خالی است.
- `[x]` پوش انجام شد.

### T2 — Domain: aggregate جدید TreatmentPlan + Occurrence
- `[ ]` ساخت `src/Animora.Domain/Clinical/TreatmentPlan.cs` (AggregateRoot) با enumها (`TreatmentItemType`, `TreatmentKind`, `TreatmentScheduleType`, `TreatmentPlanState`, `TreatmentOccurrenceState`) و متدِ factory که از schedule، occurrenceها را تولید می‌کند (با سقفِ ۳۶۵/۱سال) و `MarkOccurrenceDone`, `Cancel`.
- `[ ]` ساخت `src/Animora.Domain/Clinical/TreatmentOccurrence.cs` (entity زیرِ aggregate یا child).
- `[ ]` (در صورت نیاز) ErrorCode‌های جدید در `src/Animora.Domain/Results/ErrorCodes.cs`.
- `[ ]` بیلد سریع 0/0 (Domain صفر-ارجاع است؛ ساده کامپایل می‌شود). پوش: `feat(domain): add TreatmentPlan + TreatmentOccurrence aggregate for medication/vaccine scheduling`.

### T3 — Persistence: migration 031 + Configuration + Repository + Queries
- `[ ]` Configuration برای دو موجودیت در `src/Animora.Persistence.Sqlite/Configurations/Clinical/`.
- `[ ]` ثبت DbSet/entity در DbContext و extension مربوطه (`Context/LocalStoreServiceCollectionExtensions.cs` را ببین که repository/query چطور register می‌شوند).
- `[ ]` Repository `TreatmentPlanRepository` + Queries `TreatmentPlanQueries` (port در Application).
- `[ ]` migration `__031_TreatmentPlans` (ترجیحاً `dotnet ef migrations add` از طریق الگوی scripts؛ اگر در sandbox سخت بود، دستی طبق `__029_RecurringExpenses.cs` + آپدیتِ `AnimoraLocalDbContextModelSnapshot.cs`). نام‌گذاری زمانی بزرگ‌تر از 030.
- `[ ]` بیلد `scripts/sandbox/build.sh full` (چون Persistence دست خورد). 0/0. پوش: `feat(persistence): add 031 TreatmentPlans migration + config + repo + queries`.

### T4 — Application: commands + queries + ports
- `[ ]` port جدید `ITreatmentPlanRepository` + `ITreatmentPlanQueries` در `src/Animora.Application/Ports/`.
- `[ ]` `src/Animora.Application/UseCases/Vaccination/TreatmentCommands.cs`: `CreateTreatmentPlanCommand`, `MarkTreatmentOccurrenceDoneCommand`, `CancelTreatmentPlanCommand` (+validator+handler) با `[RequiresPermission(VaccinationWrite)]` و `[Auditable(Clinical, ...)]`.
- `[ ]` `TreatmentQueries.cs`: `TreatmentCalendarRangeQuery(from,to,filters)`, `TreatmentPlansForPetQuery(petId)`.
- `[ ]` CommandTypeهای جدید در `Contracts.Wire/Commands/CommandTypes.cs` + all-list.
- `[ ]` ثبتِ handlerها (الگوی `VaccinationRegistration.cs`).
- `[ ]` بیلد `full` 0/0. پوش: `feat(application): treatment plan commands + calendar/pet queries`.

### T5 — Contracts + Client.Services: اتصال seam
- `[ ]` enum/record‌های جدید در `src/Animora.Contracts/Vaccination/` (`TreatmentModels.cs`).
- `[ ]` افزودن متدهای جدید به `IVaccinationService` (Create/Calendar/MarkOccurrence/CancelPlan/PlansForPet + AddCatalogVaccine در صورت تصمیم).
- `[ ]` پیاده‌سازی در `src/Animora.Client.Services/Vaccination/VaccinationService.cs` روی dispatcher + mapping در `VaccinationMapping.cs`.
- `[ ]` بیلد `full` 0/0. پوش: `feat(contracts,client): treatment plan service seam + calendar mapping`.

### T6 — UI: بازطراحی صفحهٔ «دارو و واکسن»
- `[ ]` تغییر لیبل‌ها: `NavigationMenu.cs` («واکسیناسیون»→«دارو و واکسن») و کاشیِ داشبورد + عنوان صفحه در `VaccinationPageViewModel` (Title/Breadcrumbs).
- `[ ]` افزودن تبِ «تقویم» به `VaccinationPageView.axaml` + state در ViewModel (`IsCalendarSection` و `SetSection "calendar"`).
- `[ ]` ساخت نمای تقویم ماهانه (کنترل یا partial view) که `CalendarOccurrence`ها را با رنگِ Kind رندر کند؛ ناوبری ماه قبل/بعد؛ کلیک روی روز → occurrenceهای آن روز؛ دکمهٔ «انجام شد».
- `[ ]` رنگ‌های سه‌گانهٔ Kind در `VaccinationPresentation.cs`.
- `[ ]` بیلد سریع 0/0. پوش: `feat(vaccination-ui): rename to dawo-vaccine, add calendar tab + kind colors`.

### T7 — UI: مدالِ ثبتِ دارو/واکسن واقعی + افزودن واکسن به انبار
- `[ ]` بازنویسی `AddVaccineModalViewModel`/`View`: انتخاب `ItemType` (واکسن/دارو)، `Kind` سه‌گانه، عنوان/دوز، و بخشِ **زمان‌بندی** (Once/EveryNDays/SpecificDates) با `JalaliDatePicker` و افزودن چند تاریخ؛ `HomeByOwner`→گزینهٔ پیامک. `Submit()` واقعاً `CreateTreatmentPlanAsync` را صدا بزند، toast واقعی، و `IDataChangeNotifier.Notify(Vaccination, Dashboard, ...)`.
- `[ ]` تبِ «انبار واکسن»: دکمهٔ افزودن واکسن جدید به کاتالوگ را به سرویس وصل کن (ثبت واقعی/یا حداقلِ قابل‌قبولِ افزودن به کاتالوگ).
- `[ ]` بیلد سریع 0/0. پوش: `feat(vaccination-ui): real treatment-plan creation modal (type/kind/schedule) + add vaccine to stock`.

### T8 — یکپارچه‌سازی و تأیید نهایی
- `[ ]` بیلد کامل `scripts/sandbox/build.sh full` = **0/0**. اگر DI/Shell/startup دست خورد، یک بار smoke: `scripts/sandbox/run.sh` → `SMOKE RUN OK` (اختیاری، فقط اگر startup/DI لمس شد).
- `[ ]` چک کن تقویم occurrenceها را درست و با رنگِ Kind نشان می‌دهد؛ برای یک بیمار بشود واکسن/دارو ثبت و تاریخ‌ها را دید؛ صفحهٔ Purchasing کاملاً رفته.
- `[ ]` پوش: `chore(vaccination): finalize integration, build 0/0`.

### T9 — پاک‌سازی (تسکِ پایانی)
- `[ ]` **فقط همین فایل پلن** را حذف کن: `rm Plan/purchasing-removal-vaccination-redesign.md`. (به فایل‌های دیگرِ Plan و roadmapها دست نزن.)
- `[ ]` پوش: `chore(plan): remove completed purchasing/vaccination plan file`.

---

## ۴. نکات ریز که نباید فراموش شوند
- **بعد از هر تسک پوش کن** (قانون قفل). پیام commit معنادار و conventional.
- بیلدِ سریع برای تغییرِ UI؛ بیلدِ `full` وقتی Domain/Application/Persistence/Contracts دست خورد.
- هیچ sample/seed یا شاخهٔ design-time در `Pages/**` نگذار؛ seed در لایهٔ Persistence.
- occurrenceها را محدود کن (سقفِ تعداد/بازه) تا رشدِ بی‌نهایت نگیرد (PERFORMANCE: نتیجهٔ بی‌کران از seam رد نشود — pageبندی/بازهٔ تقویم).
- رنگ/استایلِ هر Kind باید در تقویم و لیست یکسان و واضح باشد.
- اگر هر فیکسِ سنگینِ میان‌راهی لازم شد، همان لحظه پوش با پیام قابل‌فهم.
- اگر `dotnet ef` در sandbox مشکل داشت، migration و snapshot را دستی بساز (الگوی `__029_RecurringExpenses.cs`).
