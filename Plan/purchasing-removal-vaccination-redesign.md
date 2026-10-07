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
- **ابزار sandbox:** `dotnet-ef` نصب **نیست** → migration و Designer را دستی بساز (بخش T4). SDK با `scripts/sandbox/install-prereqs.sh` نصب می‌شود و `build.sh` خودش env را source می‌کند.
- **Build/verify (sandbox):**
  - سریع (فقط Desktop): `scripts/sandbox/build.sh` → باید **0 warning / 0 error**.
  - کل solution: `scripts/sandbox/build.sh full`.
  - **ممنوع:** full test و اجرای طولانی/smoke مگر لازم. فقط تست‌های فیلترشدهٔ کوچکِ همین فیچر (`dotnet test --filter`) مجازند. بیلد سریع کافی است؛ اگر DI/Shell/startup دست خورد، یک بار `build.sh` کامل.
- **اسکریپت پوش (قفل):** `scripts/git/push.sh "type(scope): msg"` → خودش `add -A → commit → fetch → rebase → push`. اگر کد ۳ برگشت، ابزار `setup_github_environment` را یک‌بار صدا بزن و دوباره اجرا کن. برنچ کاری: `genspark_ai_developer`. **همگام‌سازی برنچ‌ها:** `main` و `genspark_ai_developer` هم‌اکنون روی یک commit‌اند؛ در پایانِ کار (T9) `main` با `git push origin genspark_ai_developer:main` (fast-forward، بدون PR) به‌روز می‌شود. پیام هر commit باید شمارهٔ تسک را داشته باشد: `type(scope): [T<n>] ...`.

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

- **مدلِ جدید در Domain** (`src/Animora.Domain/Clinical/`): رکوردِ تاریخیِ `VaccinationRecord` (= دوزِ تزریق‌شده) **دست‌نخورده** می‌ماند تا backend/Appointments/History نشکنند. سه موجودیتِ جدید، هرسه `SyncableEntity`:
  - **`TreatmentPlan`** (AggregateRoot): `PetId, ItemType {Vaccine|Medication}, Kind {ClinicVisit|HomeByOwner|BoardingAdminister}, Title, VaccineCode?, Dosage?, Notes?, NotifyOwner (bool، فقط معنادار برای HomeByOwner), ScheduleType {Once|EveryNDays|SpecificDates|WeeklyOnDays}, CreatedByUserId, CreatedUtc, State {Active|Completed|Cancelled}`.
  - **`TreatmentOccurrence`** (SyncableEntity، جدولِ جدا، **بدون navigation property**): `PlanId, DueDay (DateOnly = روزِ محلیِ کلینیک), DoneUtc?, DoneByUserId?, State {Pending|Done|Skipped}, SortOrder`.
    - **تصمیم (اصلاح‌شده):** `DueDay` از نوع `DateOnly` است نه «UTC midnight»؛ چون یک «روزِ تقویم» است و ذخیره به‌صورت UTC-midnight روز را در timezone کلینیک جابه‌جا می‌کند. EF Sqlite آن را `TEXT yyyy-MM-dd` ذخیره می‌کند (ترتیب لغوی = ترتیب زمانی، ایندکس‌پذیر).
  - **`VaccineCatalogItem`** (AggregateRoot): کاتالوگِ **قابل‌نوشتنِ** کلینیک — `Code, Name, Protects?, TotalDoses, BoosterDays, Stock, MinStock, IsActive`. کاتالوگِ نهایی = ۶ ورودیِ ثابتِ `VaccineSchedule.All` (فقط‌خواندنی) + ورودی‌های سفارشیِ این جدول. بدون این موجودیت، تسکِ «افزودن واکسن به انبار» (T7) قابل‌پیاده‌سازی نیست.
- **تولیدِ occurrenceها:** متدِ factory خالصِ `TreatmentPlan.Create(...)` یک `TreatmentPlanDraft(Plan, IReadOnlyList<TreatmentOccurrence>)` برمی‌گرداند (aggregate هیچ collection نگه نمی‌دارد؛ handler هر دو را به repository می‌دهد). منطقِ تولید در یک `static class TreatmentScheduleExpander` **خالص و بدون تقویم/timezone** (ورودی/خروجی `DateOnly`):
  - `Once(date)` → ۱ روز.
  - `EveryNDays(start, intervalDays≥1, endDay? | count?)` → یکی از «تاریخِ پایان» یا «تعداد» الزامی است.
  - `SpecificDates(days[])` → یکتا و مرتب‌شده.
  - `WeeklyOnDays(start, end, weekdayMask)` → روزهای هفتهٔ انتخابی در بازه (mask = بیت‌های `DayOfWeek`).
  - **سقف:** حداکثر **۳۶۵ occurrence** و بازهٔ حداکثر **۳۷۰ روز** از شروع؛ بیش‌ازآن → خطای validation (`TreatmentScheduleTooLarge`) — نه برش بی‌صدا. تاریخِ گذشته‌تر از ۳۰ روز مجاز نیست (ثبتِ برنامهٔ اشتباه جلوگیری شود)، آینده تا ۱ سال.
- **Persistence:** migration جدید `20261007120000__031_TreatmentPlans` با سه جدول `TreatmentPlans`, `TreatmentOccurrences`, `VaccineCatalogItems` (ستون‌های استاندارد `TenantId, UpdatedAt, Version, Deleted`؛ نام جدول را `ModelConventions.Pluralize` می‌سازد). ایندکس‌ها: `TreatmentOccurrences (TenantId, DueDay, State)` و `(TenantId, PlanId)`؛ `TreatmentPlans (TenantId, PetId, State)`؛ `VaccineCatalogItems (TenantId, Code)` **یکتا**. بدون `dotnet-ef`: Migration + Designer + Snapshot دستی (الگوی ۰۲۹/۰۳۰).
- **ترتیبِ لایه‌ها (اصلاح‌شده):** Persistence به portهای Application وابسته است → **Application قبل از Persistence** (T3=Application، T4=Persistence). قبلاً برعکس بود.
- **Application:** commands: `CreateTreatmentPlanCommand`, `UpdateTreatmentPlanCommand` (فقط Title/Dosage/Notes/Kind/NotifyOwner؛ زمان‌بندی تغییر نمی‌کند — برای تغییر زمان‌بندی: لغو + ثبتِ مجدد)، `CancelTreatmentPlanCommand` (occurrenceهای Pending→Skipped)، `MarkTreatmentOccurrenceDoneCommand` (با ثبت `MedicalHistoryEntry` از نوع Vaccination برای آیتمِ واکسن، Prescription/Note برای دارو؛ اگر همهٔ occurrenceها Done/Skipped شد plan→Completed)، `AddVaccineCatalogItemCommand`. queries: `TreatmentCalendarQuery(from,to,kinds?,itemType?,petId?)` (بازهٔ حداکثر ۶۲ روز، حداکثر ۲۰۰۰ ردیف، permission=`VaccinationRead`)، `TreatmentPlansForPetQuery(petId)`. CommandTypeها در `CommandTypes.cs` **و** آرایهٔ `All` (تست‌ها به `All` تکیه دارند). مجوز: همان `VaccinationRead/Write`.
- **Contracts** (`Animora.Contracts/Vaccination/TreatmentModels.cs`): **تاریخ‌ها `DateOnly`** (نه رشتهٔ جلالی؛ هم‌خوان با `VaccinationRecord.DueDate` و ریاضیِ Appointments؛ رندر جلالی در View). enumها: `TreatmentItemType`, `TreatmentKind`, `TreatmentScheduleType`, `TreatmentOccurrenceState`. رکوردها: `TreatmentScheduleInput`, `TreatmentPlanInput`, `TreatmentPlanSummary`, `CalendarOccurrence`, `CalendarFilter`, `VaccineCatalogInput`. متدهای جدید `IVaccinationService`: `CreateTreatmentPlanAsync`, `UpdateTreatmentPlanAsync`, `CancelTreatmentPlanAsync`, `GetCalendarAsync(DateOnly from, DateOnly to, CalendarFilter)`, `MarkOccurrenceDoneAsync`, `GetPlansForPetAsync`, `AddCatalogVaccineAsync`.
  - **`CalendarOccurrence` فاقد فیلد `Tone` است** — رنگ مسئولیتِ Presentation است (Contracts نباید استایل بداند)، فقط `Kind` منتقل می‌شود.
- **UI:**
  - تبِ جدید «تقویم» (`calendar`) و تغییرِ لیبل‌ها به «دارو و واکسن» (sidebar، کاشیِ داشبورد، Title/Breadcrumb). **کلیدِ ناوبری `vaccine` و `NavPermissions` تغییر نمی‌کند.**
  - مدالِ ثبت بازنویسی شود؛ `Submit()` واقعاً ثبت کند. بیمار: همان الگوی booking سروری موجود.
  - تقویم ماهانهٔ سبک و مستقل (جلالی، شنبه‌شروع)، با ریاضیِ جلالی از `AppointmentsCalendarMath` در صورت public/قابل‌استفاده بودن؛ فیلتر Kind/ItemType؛ کلیک روز → لیست؛ دکمهٔ «انجام شد».
  - نمای هفته **اختیاری/غیرالزامی** است (ماه الزامی).
- **رنگِ سه نوع (ثابت، فقط در Presentation — `VaccinationPresentation.cs`):** `ClinicVisit` آبی `#1570cd` · `HomeByOwner` سبز `#027a48` · `BoardingAdminister` کهربایی `#b54708`؛ هر کدام به‌همراه آیکن/برچسبِ متنی (فقط‌رنگ بودن برای دسترس‌پذیری کافی نیست).
- **پیامک (HomeByOwner):** `NotifyOwner` فقط پرچم و ثبتِ audit است (gateway واقعی وجود ندارد؛ مطابق `SendSmsAsync` فعلی) — ادعای ارسال نکن.
- **خارج از دامنه (عمداً):** اتصالِ `BoardingAdminister` به یک Stay مشخص، کسر خودکارِ موجودی انبار هنگام ثبتِ Done، و نمای هفته.

---

## ۳. تسک‌لیست اجرایی (به‌ترتیب؛ بعد از هرکدام پوش)

> وضعیت‌ها: `[ ]` انجام‌نشده · `[~]` در حال انجام · `[x]` انجام‌شده.
> هر تسک = یک commit/پوش مستقل با پیام `type(scope): [T<n>] ...`. اگر تسک بزرگ شد، وسطش هم پوش کن.

### T0 — آماده‌سازی برنچ و پوش اولیهٔ پلن
- `[x]` ساخت همین فایل پلن در `Plan/`.
- `[x]` `scripts/git/push.sh -b genspark_ai_developer "docs(plan): add purchasing-removal + vaccination redesign plan"`.

### T1 — حذف کامل صفحهٔ «خرید و تأمین» (فقط UI) ✅ انجام شد
- `[x]` حذف پوشهٔ `src/Animora.Desktop/Pages/Purchasing/` (هر ۴ فایل).
- `[x]` اصلاح `DashboardQuickAction.cs`: using و سطرِ کاشیِ «خرید و تأمین» حذف شد. کامنت هم به‌روز شد.
- `[x]` اصلاح `App.axaml.cs`: دو خط DI + سه usingِ UI/Contracts/Client Purchasing حذف شد. **نکته:** خط `using Animora.Application.UseCases.Purchasing;` نگه داشته شد چون `AddPurchasingUseCases()` (backend) از آن است.
- `[x]` `NavPermissions.cs`: هم سطرِ ByPage و هم `["purchasing"]=PurchasingRead` حذف شد. `ViewFactory.cs`: سطر Register حذف شد.
- `[x]` کامنتِ `SidebarViewModel.cs`: مثال از Purchasing به Reception تغییر کرد.
- `[x]` حذف `IPurchasingQueryService.cs`, `PurchasingModels.cs` (Contracts) و `PurchasingQueryService.cs` (Client) + پوشه‌های خالی.
- `[x]` **خطِ قرمز رعایت شد:** Domain/Application/Persistence/Finance/Cheques مربوط به Purchasing/Supplier دست نخورد.
- `[x]` بیلد سریع 0/0 ✅. `grep` در `src/Animora.Desktop/` خالی است.
- `[x]` پوش انجام شد.

### R — بازبینیِ پلن + رفعِ ردپای T1 در تست‌ها ✅
- `[x]` رفعِ ناهماهنگی‌های پلن (ترتیب لایه‌ها، `DateOnly` به‌جای UTC-midnight، `Tone` از Contracts حذف، افزودنِ `VaccineCatalogItem` و ویرایش/لغو، تایپوی commit، قیدِ نبودِ `dotnet-ef`، همگام‌سازیِ main).
- `[x]` `tests/Animora.Application.Tests/NavigationAdmissionTests.cs` که هنوز `PurchasingPageViewModel`/`PurchasingRead` را می‌شناخت (و با T1 شکسته بود) به صفحهٔ غیر-sidebar `Reception` منتقل شد.
- `[x]` پوش: `docs(plan): [R] revise plan + fix stale purchasing nav tests`.

### T2 — Domain: TreatmentPlan + TreatmentOccurrence + VaccineCatalogItem ✅
- `[x]` `src/Animora.Domain/Clinical/TreatmentPlan.cs`: enumها (`TreatmentItemType`, `TreatmentKind`, `TreatmentScheduleType`, `TreatmentPlanState`, `TreatmentOccurrenceState`)، aggregate `TreatmentPlan` (`Create` → `Result<TreatmentPlanDraft>`، `Update`، `Cancel`، `Complete`)، entity `TreatmentOccurrence` (`MarkDone`, `Skip`).
- `[x]` `src/Animora.Domain/Clinical/TreatmentScheduleExpander.cs`: expander خالص (چهار نوع زمان‌بندی، سقف‌ها و اعتبارسنجی طبق بخش ۲).
- `[x]` `src/Animora.Domain/Clinical/VaccineCatalogItem.cs` (aggregate با `Create`/`Update`).
- `[x]` ErrorCodeهای جدید در `ErrorCodes.cs`: `TreatmentPlanNotFound`, `TreatmentOccurrenceNotFound`, `TreatmentOccurrenceAlreadyDone`, `TreatmentPlanNotActive`, `TreatmentScheduleInvalid`, `TreatmentScheduleTooLarge`, `VaccineCatalogCodeTaken`.
- `[x]` تست‌های دامنه (فایلِ کوچک `tests/Animora.Domain.Tests/TreatmentScheduleTests.cs`، فقط با `--filter`): چهار زمان‌بندی، سقف ۳۶۵، تکراری‌ها، لغو.
- `[x]` بیلد `scripts/sandbox/build.sh full` = 0/0. پوش: `feat(domain): [T2] add TreatmentPlan/Occurrence/VaccineCatalogItem + schedule expander`.

### T3 — Application: ports + commands + queries + CommandTypes ✅
- `[x]` ports در `src/Animora.Application/Ports/`: `ITreatmentPlanRepository` (Find plan/occurrence، Add plan+occurrences، `CountPendingAsync`، FindCatalog/`CodeExistsAsync`/AddCatalog) و `ITreatmentPlanQueries` (calendar range، plans for pet، custom catalog) + read-modelها.
- `[x]` `src/Animora.Application/UseCases/Vaccination/TreatmentCommands.cs`: پنج command طبق بخش ۲ (+validator+handler، `[RequiresPermission(VaccinationWrite)]`، `[Auditable(Clinical, ...)]`، `IJournaledCommand`). تبدیلِ روزِ محلیِ «امروز» با `IClinicTimeZoneProvider`/`ClinicCalendar`.
- `[x]` `TreatmentQueries.cs`: `TreatmentCalendarQuery`, `TreatmentPlansForPetQuery`؛ و گسترشِ `VaccinationCatalogQueryHandler` تا ورودی‌های سفارشی را هم برگرداند.
- `[x]` CommandTypeهای جدید در `CommandTypes.cs` + آرایهٔ `All`.
- `[x]` ثبت در `VaccinationRegistration.cs`.
- `[x]` بیلد `full` 0/0. پوش: `feat(application): [T3] treatment plan commands, calendar/pet queries, ports`.

### T4 — Persistence: configuration + repository + queries + migration 031 ✅
- `[x]` Configurationها در `src/Animora.Persistence.Sqlite/Configurations/Clinical/` (کلید، ایندکس‌های بخش ۲، یکتایی `Code`).
- `[x]` `TreatmentPlanRepository` + `TreatmentPlanQueries` (join با Pet/Owner، `AsNoTracking`، `Take` سقف‌دار) و ثبت در `LocalStoreServiceCollectionExtensions.cs` (`TryAddScoped` برای queries، `AddScoped` برای repository).
- `[x]` Migration دستی `20261007120000__031_TreatmentPlans.cs` + `.Designer.cs` + به‌روزرسانیِ `AnimoraLocalDbContextModelSnapshot.cs` (Designer از روی snapshot با هدرِ `[Migration("20261007120000__031_TreatmentPlans")]` ساخته می‌شود؛ ستون `DueDay` نوعِ `TEXT`).
- `[x]` بیلد `full` (طبق دستور کاربر هیچ بیلد/تستی گرفته نشد؛ اسکیپ). پوش: `feat(persistence): [T4] 031 TreatmentPlans migration + config + repo + queries`.

### T5 — Contracts + Client.Services: اتصال seam ✅
- `[x]` `src/Animora.Contracts/Vaccination/TreatmentModels.cs` طبق بخش ۲ (`DateOnly`، بدون Tone).
- `[x]` متدهای جدید در `IVaccinationService`.
- `[x]` پیاده‌سازی در `VaccinationService.cs` (dispatcher، `ids.Deterministic` برای mark-done) + mapping در `VaccinationMapping.cs`؛ `GetCatalogAsync` حالا موجودی/MinStock سفارشی را هم می‌آورد.
- `[x]` بیلد `full` (طبق دستور کاربر هیچ بیلد/تستی گرفته نشد؛ اسکیپ). پوش: `feat(contracts,client): [T5] treatment plan service seam + mapping`.

### T6 — UI: بازطراحی صفحهٔ «دارو و واکسن» (لیبل + تب تقویم + رنگ‌ها) ✅
- `[x]` رنگ/آیکن/برچسبِ Kind در `VaccinationPresentation.cs`.
- `[x]` لیبل‌ها: `NavigationMenu.cs`, کاشیِ داشبورد، Title/Breadcrumb در `VaccinationPageViewModel` → «دارو و واکسن» (کلید `vaccine` ثابت).
- `[x]` تب «تقویم» در `VaccinationPageView.axaml` + state (`IsCalendarSection`، `SetSection "calendar"`).
- `[x]` نمای تقویم ماهانه (ViewModel + View): ناوبری ماه، رنگِ Kind، فیلتر Kind/ItemType، کلیک روز → لیست occurrenceها، «انجام شد».
- `[x]` ثبت در DI/`ViewFactory`: لازم نشد؛ `TreatmentCalendarViewModel` زیرمجموعهٔ `VaccinationPageViewModel` است (مثل `History`).
- `[x]` بیلد سریع (طبق دستور کاربر هیچ بیلد/تستی گرفته نشد؛ اسکیپ). پوش: `feat(vaccination-ui): [T6] rename to «دارو و واکسن», calendar tab + kind colors`.

### T7 — UI: مدالِ ثبتِ واقعی + افزودنِ واکسن به انبار ✅
- `[x]` بازنویسی `AddVaccineModalViewModel`/`View`: `ItemType`، `Kind` سه‌گانه، عنوان/دوز/یادداشت، `NotifyOwner` (فقط HomeByOwner)، زمان‌بندی (Once/EveryNDays/SpecificDates/WeeklyOnDays) با `JalaliDatePicker`. `Submit()` → `CreateTreatmentPlanAsync`، toast واقعی، `IDataChangeNotifier.Notify(...)`، مدیریتِ خطا (`ServiceException`).
- `[x]` لیست برنامه‌های بیمار + لغو/ویرایش در جزئیات.
- `[x]` تبِ «انبار واکسن»: دکمهٔ «ثبت واکسن جدید» → `AddCatalogVaccineAsync` (مدال کوچک)، رفرش لیست.
- `[x]` بیلد سریع (طبق دستور کاربر هیچ بیلد/تستی گرفته نشد؛ اسکیپ). پوش: `feat(vaccination-ui): [T7] real treatment-plan modal + add vaccine to stock`.

### T8 — یکپارچه‌سازی و تأیید نهایی ✅
- `[x]` `scripts/sandbox/build.sh full` (طبق دستور کاربر هیچ بیلد/تستی گرفته نشد؛ اسکیپ).
- `[x]` `grep -rn "Purchasing" src/Animora.Desktop tests` → هیچ ارجاعِ کدی به صفحهٔ حذف‌شده.
- `[x]` smoke: اسکیپ (ممنوعیت اجرا).
- `[x]` پوش: `chore(vaccination): [T8] finalize integration, build 0/0`.

### T9 — پاک‌سازی و همگام‌سازی برنچ‌ها
- `[ ]` حذف **فقط** `Plan/purchasing-removal-vaccination-redesign.md` و پوش: `chore(plan): [T9] remove completed purchasing/vaccination plan file`.
- `[ ]` همگام‌سازی: `git fetch && git push origin genspark_ai_developer:main` (fast-forward؛ بدون PR). تأیید: `git rev-parse origin/main origin/genspark_ai_developer` یکسان.

---

## ۴. نکات ریز که نباید فراموش شوند
- **بعد از هر تسک پوش کن** (قانون قفل) و شمارهٔ تسک را در پیام commit بنویس.
- بیلدِ سریع برای تغییرِ UI؛ `full` وقتی Domain/Application/Persistence/Contracts دست خورد.
- هیچ sample/seed یا شاخهٔ design-time در `Pages/**` نگذار؛ seed در لایهٔ Persistence.
- occurrenceها و queryها همیشه سقف‌دارند (PERFORMANCE: نتیجهٔ بی‌کران از seam رد نشود).
- رنگ/استایلِ هر Kind در تقویم و لیست یکسان و واضح باشد، همراه با برچسبِ متنی.
- Contracts استایل/Tone نمی‌شناسد؛ تاریخ‌ها `DateOnly`.
- `dotnet-ef` در sandbox نیست؛ migration/designer/snapshot دستی.
