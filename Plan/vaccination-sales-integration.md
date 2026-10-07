# Plan — Vaccination/Medicine + Sales/Cashier integration & fixes

> **Self-contained execution file.** یک مدل در سشن کاملاً جدید باید بتواند فقط با خواندن
> این فایل کار را ادامه دهد، بدون بررسی مجدد گستردهٔ ریپو. تمام نتایج کشف‌شده، تصمیم‌ها،
> مسیر فایل‌ها و وابستگی‌ها اینجا ثبت شده‌اند.

## ⛔ قانون قفل‌شدهٔ Push (بعد از هر تسک اجباری)
بعد از **تکمیل هر تسک** از این لیست، فوراً اجرا کن:
```bash
scripts/git/push.sh "type(scope): [T<شماره>] توضیح کوتاه"
```
- اگر exit code = `3` → ابزار `setup_github_environment` را یک‌بار صدا بزن، بعد دوباره همان دستور.
- اگر exit code = `2` → تعارض rebase؛ طبق راهنما حل کن و دوباره اجرا کن.
- نبود push = شکست. بعد از هر تسک حتماً push.

## قوانین کلی (از کاربر)
1. بررسی هدفمند؛ فقط فایل‌های لازم.
2. کیفیت استاندارد، کدنویسی تمیز، بدون پیچیدگی/کد اضافی، بدون اعمال بزوری.
3. **Build و Test اجرا/اضافه نشود** (کاربر صریحاً گفته؛ `scripts/sandbox/build.sh` و `tests/` را اجرا نکن).
4. درک کامل تسک قبل از اجرا.
5. Push بعد از هر تسک.
6. آخرین تسک = حذف همین فایل Plan (فقط همین فایل؛ Roadmap/فایل‌های دیگر دست‌نخورده).
7. تغییرات سنگین با یک push تمیز ثبت شوند.

---

## 🧭 نقشهٔ معماری (کشف‌شده — برای اجرا لازم است)

**پروژه:** Animora — اپ دسکتاپ مدیریت کلینیک دامپزشکی. .NET 10، Avalonia + MVVM
(CommunityToolkit.Mvvm)، فارسی/RTL، تقویم جلالی. بک‌اند **Track C کامل است**: همهٔ صفحات
روی SQLite محلی واقعی از طریق `Animora.Client.Services` کار می‌کنند. فاز بعدی D01 (سرور/sync)
به این تسک‌ها ربطی ندارد.

**جریان داده (قانون معماری):**
`View → ViewModel → interface در Animora.Contracts (DI سازنده) → پیاده‌سازی واقعی در
Animora.Client.Services → dispatch در Animora.Application → دامنه Animora.Domain + SQLite`.
- ViewModelها فقط به interfaceهای Contracts وابسته‌اند، نه به سرویس concrete/EF/SQL.
- افزودن نیاز دادهٔ جدید یک صفحه: interface+DTO در `Contracts/<Area>/` → پیاده‌سازی در
  `Animora.Client.Services/<Area>/` روی dispatcher → ثبت در `src/Animora.Desktop/App.axaml.cs`.
- دامنه/اپلیکیشن/persistence: command/query handler + use case.

**مسیرهای کلیدی این تسک‌ها:**

Vaccination (صفحهٔ «دارو و واکسن»):
- `src/Animora.Desktop/Pages/Vaccination/`
  - `VaccinationPageView.axaml` (+`.cs`) و `VaccinationPageViewModel.cs` — صفحهٔ اصلی با تب‌ها.
    - `ActiveSection` یکی از: `visits` | `calendar` | `stock` | `history` | `settings`
      (پراپرتی‌های `IsVisitsSection`/`IsCalendarSection`/... + `OnActiveSectionChanged`).
    - تب فعلی اول = `visits` (مقدار پیش‌فرض `_activeSection = "visits"`، خط ۹۲).
  - `TreatmentCalendarView.axaml` + `TreatmentCalendarViewModel.cs` — «تقویم واکسن» (تقویم ماهانه،
    کلیک روی روز لیست occurrenceها را با اکشن «انجام شد» نشان می‌دهد).
  - `AddVaccineModalView.axaml` + `AddVaccineModalViewModel.cs` — مودال «ثبت دارو/واکسن»:
    انتخاب بیمار (booking-style search)، نوع (vaccine/medication)، kind
    (ClinicVisit/HomeByOwner/BoardingAdminister)، و ۴ نوع زمان‌بندی
    (Once/EveryNDays/SpecificDates/WeeklyOnDays). Submit → `CreateTreatmentPlanAsync`.
    - واکسن از `SelectedVaccine` (کاتالوگ) یا دارو با نام دستی `MedicationName`.
  - `AddCatalogVaccineModalViewModel.cs` — افزودن واکسن به کاتالوگ کلینیک («انبار واکسن»).
  - `PetTreatmentPlansModalViewModel.cs` — پلن‌های یک پت (ویرایش/لغو).
  - `VaccinationHistoryViewModel.cs` + `VaccinationRecordDetailViewModel.cs` — تاریخچه و جزئیات رکورد.
  - `VaccinationPresentation.cs` — رنگ/لیبل/آیکن kind و فرمت فارسی `Fa()`.
- Contracts: `src/Animora.Contracts/Vaccination/`
  - `IVaccinationService.cs` — شامل: `GetRecordsAsync`, `GetCatalogAsync`, `GetSmsTemplatesAsync`,
    `MarkDoneAsync`, `CreateTreatmentPlanAsync`, `UpdateTreatmentPlanAsync`, `CancelTreatmentPlanAsync`,
    `GetCalendarAsync(from,to,filter)` → `CalendarOccurrence`, `MarkOccurrenceDoneAsync`,
    `GetPlansForPetAsync`, `AddCatalogVaccineAsync`.
  - `TreatmentModels.cs` — `TreatmentItemType{Vaccine,Medication}`, `TreatmentKind`,
    `TreatmentScheduleType`, `CalendarOccurrence(OccurrenceId,PlanId,Day,PetId,PetName,Species,
    OwnerName,Title,Dosage,ItemType,Kind,State,NotifyOwner)`, `TreatmentPlanInput`, `VaccineCatalogInput`.
  - `VaccinationModels.cs` — `VaccinationRecord`, `VaccineCatalogEntry`, `SmsTemplate`.
- Client.Services: `src/Animora.Client.Services/Vaccination/VaccinationService.cs` (+`TreatmentMapping.cs`,
  `VaccinationMapping.cs`). اسکوپ‌به‌ازای‌فراخوان روی dispatcher.
- Application: `src/Animora.Application/UseCases/Vaccination/`
  - `TreatmentQueries.cs` (شامل `TreatmentCalendarQuery`), `TreatmentCommands.cs`,
    `VaccinationQueries.cs` (شامل `VaccinationCatalogQuery`, `VaccinationBoostersRangeQuery`,
    `VaccinationDueQuery`), `VaccinationCommands.cs`, `VaccinationRegistration.cs`.
- Domain: `src/Animora.Domain/Clinical/` — `TreatmentPlan.cs`, `VaccineSchedule.cs`,
  `VaccineCatalogItem.cs`, `VaccinationRecord.cs`, `TreatmentScheduleExpander.cs`.

Appointments (تقویم اصلی «نوبت‌ها»):
- `src/Animora.Desktop/Pages/Appointments/`
  - `AppointmentsCalendarView.axaml` + `AppointmentsCalendarViewModel.cs` — تقویم اصلی. سه تب:
    «تقویم / نوبت‌های پیش رو / تاریخچه» (کامنت خط ۲۷). متدهای کلیدی در VM:
    - `OpenBooking(target)` (~خط ۱۹۷۳): اگر event یک `IsVaccineBooster` باشد →
      `_presenter.OpenPetSheetAsync(petId)` (یعنی الان فقط پروندهٔ پت باز می‌شود، نه پاپ‌آپ جزئیات).
      در غیر این صورت → `ShowDetailAsync(ev)` → `BookingDetailViewModel` در drawer.
  - تب «نوبت‌های پیش رو» = `AppointmentsQueueTabView.axaml` (+`AppointmentsQueueTabView.axaml.cs`) —
    مرجع ظاهری برای تسک 1.3.
  - `BookingDetailViewModel.cs`/`BookingDetailView.axaml` — drawer جزئیات نوبت.
- Contracts: `src/Animora.Contracts/Appointments/`
  - `AppointmentModels.cs` — `AppointmentEvent(... IsVaccineBooster=false, VaccineName ...)`،
    `AppointmentTypeIds.VaccineBooster = "VACCINE_BOOSTER"` (خط ۱۷۹).
  - `IAppointmentsQueryService.cs`, `IAppointmentBookingService.cs`.
- Client.Services: `src/Animora.Client.Services/Appointments/`
  - `AppointmentsQueryService.cs` — `GetForRangeAsync` رویدادهای نوبت + **بوستر واکسن** را می‌دهد:
    `GetBoosterEventsAsync` از `VaccinationBoostersRangeQuery` (additive، خطای آن تقویم را نمی‌شکند).
  - `AppointmentsMapping.cs` — `ToBoosterEvent` (خط ۶۷)، `BoosterType` (خط ۷۹، رنگ `#0E9F6E`).

> **نکتهٔ یکپارچگی مهم (کشف‌شده):** تقویم اصلی فقط **بوسترهای واکسن تکرارشونده**
> (`VaccinationBoostersRangeQuery` از روی `VaccinationRecord`) را نشان می‌دهد. اما
> **پلن‌های درمان/واکسن ثبت‌شده در صفحهٔ واکسن** (`CreateTreatmentPlanAsync` → `TreatmentPlan`
> → `CalendarOccurrence` از `TreatmentCalendarQuery`) یک سیستم **جداگانه** است و در تقویم اصلی
> دیده نمی‌شود. تسک 1.1 دقیقاً همین شکاف را می‌خواهد ببندد.

Sales/Cashier («فروش و صندوق»):
- `src/Animora.Desktop/Pages/Sales/` — سه تب: «صندوق» (`IsCashierTab`)، «تاریخچه» (`IsHistoryTab`)،
  «نرخ خدمات» (`IsRatesTab`). در `SalePageView.axaml` خطوط ۳۹۱–۴۳۴ و ۱۲۲۲–۱۲۲۶.
  - `SalePageViewModel.cs` — کنترلر؛ `SelectedInvoice` (نوع `SaleInvoiceViewModel`)، `Rates` (ServiceRates).
    - `DiscountValueText` (خط ۱۷۱) فقط نمایش است.
  - `SaleInvoiceViewModel.cs` — `DiscountText`, `DiscountValue`, `InsuranceLabel`, `HasInsurance`,
    `HasDiscount`, `InsuranceShareText`, `InsuranceShare` — همه فقط **خواندنی** (bind به Detail).
  - `SalePageView.axaml` خطوط ۷۷۶–۸۱۷: باکس «تخفیف فاکتور» و «پوشش بیمه» — **TextBlock خواندنی،
    هیچ کامند/ورودی تعاملی ندارند** → همین دلیل «کار نکردن» در تسک 2.1.
  - `ServiceRatesView.axaml` + `ServiceRatesViewModel.cs` — تب «نرخ خدمات». فعلاً فقط لیست نرخ‌ها؛
    استایل باز و بدون باکس مستقل (تسک 2.2).
  - مودال‌ها: `CatalogModalViewModel.cs`/`CatalogModalView.axaml` — **انتخاب آیتم از کاتالوگ هنگام
    فاکتور** (مرجع ظاهری تسک 1.5)؛ `CustomItemModalViewModel.cs`؛ `PaymentModalViewModel.cs`؛
    `OwnerPickerModalViewModel.cs`.
- Contracts: `src/Animora.Contracts/Sales/`
  - `ISalesQueryService.cs` — **قبلاً دارای** `SetDiscountAsync(invoiceId, DiscountChangeRequest)`
    (خط ۱۳۷) و `SetInsuranceAsync(invoiceId, insurerId?)` (خط ۱۴۰) و `GetInsurersAsync()` (خط ۵۲).
    یعنی بک‌اند آماده است؛ فقط UI وصل نیست.
  - `SalesModels.cs` — `InsurerRef(Id,Name,Percent)` (خط ۲۲۳)، `InvoiceDetail(... InsuranceOrgId,
    InsuranceLabel, DiscountKind, DiscountValue, DiscountAmount, InsuranceShare, HasInsurance,
    HasDiscount ...)` (خط ۲۳۷)، `DiscountChangeRequest(DiscountKind Kind, int Value)` (خط ۳۸۰).
  - `IServiceRateService.cs` — سرویس نرخ خدمات.
- Client.Services: `src/Animora.Client.Services/Sales/SalesQueryService.cs` + `SalesMapping.cs`.

Inventory (انبار — برای تسک 1.5):
- Contracts `src/Animora.Contracts/Inventory/IInventoryQueryService.cs` —
  `GetProductsAsync(ProductListQuery)` → `ProductPage`, `GetCategoriesAsync`, `GetDetailAsync`,
  `AdjustStockAsync` و ... . `InventoryModels.cs` شامل `Product`, `ProductKind{Vaccine,...}`.
- seed: `src/Animora.Persistence.Sqlite/Seed/InventorySampleData.cs` (واکسن‌ها VAC-001..VAC-005).

Dashboard (برای تسک 1.9):
- `src/Animora.Desktop/Pages/Dashboard/DashboardPageViewModel.cs` — `AlertRows`/`HasAlerts` از
  `DashboardSnapshot.Alerts` (بک‌اند). کلیک: `OpenAlertTarget(kind)` (نگاشت kind→صفحه).
- تولید هشدارها در بک‌اند: `src/Animora.Application/UseCases/Dashboard/DashboardSnapshotQuery.cs`
  - `DashboardAlertRow(Kind,Title,Subtitle,DueDay,AmountMinor,Severity,State)` (خط ۳۷).
  - `BuildAlertsAsync` (خط ۱۱۰): بلوک‌های gated (Finance → RecurringExpense، Cheques → Cheque)؛
    هر نوع هشدار جدید = یک بلوک gate‌دار اضافه. `MaxAlerts=8`. مرتب‌سازی: Danger قبل Warn، سپس DueDay.
  - نگاشت kind در دسکتاپ: `OpenAlertTarget` در `DashboardPageViewModel.cs` خط ۲۲۸ (برای kind جدید
    باید مقصد صفحه را هم اضافه کرد — مثلاً باز کردن تب تقویم واکسن یا پروندهٔ پت).

**نکتهٔ «منبع واکسن‌های ثابت» (تسک 1.6):** کاتالوگ «انبار واکسن» در
`VaccinationCatalogQueryHandler` (`VaccinationQueries.cs`) ساخته می‌شود:
`builtIn = VaccineSchedule.All.Select(...)` (پروتکل‌های ثابت مثل DHPP/Rabies/FVRCP از
`src/Animora.Domain/Clinical/VaccineSchedule.cs`) **concat** با کاتالوگ سفارشی کلینیک
(`custom.GetCustomCatalogAsync`). تسک 1.6 می‌خواهد ردیف‌های `builtIn` از نمایش انبار حذف شوند.
⚠️ `VaccineCatalogItem.cs` خط ۷۸ کدهای built-in را «گرفته‌شده» می‌داند — پس اگر فقط نمایش حذف شود،
منطق «کد تکراری» دامنه دست‌نخورده می‌ماند (باید بررسی شود که حذف نمایش با اعتبارسنجی افزودن تداخل نکند).

---

## 📋 TASK LIST (وضعیت: ⬜ انجام‌نشده | ✅ انجام‌شده)

### واکسن و دارو

#### ⬜ T1 — یکپارچگی پلن واکسن/دارو با تقویم اصلی
**چه کاری:** پلن‌های ثبت‌شده در صفحهٔ واکسن/دارو (`CreateTreatmentPlanAsync` → `CalendarOccurrence`)
باید در **تقویم اصلی نوبت‌ها** هم دیده شوند (نه فقط بوسترهای تکرارشوندهٔ فعلی).
**مسیر اجرا (استانداردترین، additive و کم‌پیچیدگی):**
- در `src/Animora.Client.Services/Appointments/AppointmentsQueryService.cs` → `GetForRangeAsync`،
  علاوه بر `GetBoosterEventsAsync`، occurrenceهای پلن درمان را هم از همان لایهٔ Application بخوان و
  به رویدادهای تقویم اضافه کن (الگوی `GetBoosterEventsAsync`: additive، در try/catch، خطا تقویم را نشکند).
  - منبع: `TreatmentCalendarQuery(from,to,...)` (در `TreatmentQueries.cs`) که `CalendarOccurrence` می‌دهد.
  - نگاشت `CalendarOccurrence → AppointmentEvent`: در `AppointmentsMapping.cs` یک متد نگاشت اضافه کن
    (مثل `ToBoosterEvent`). یک `AppointmentTypeId` مناسب بده (می‌توان نوع جدید «پلن درمان/واکسن»
    اضافه کرد یا از نوع موجود مرتبط استفاده کرد — رنگ kind را از `TreatmentKind` بگیر مطابق
    `VaccinationPresentation.KindBrush`). رویداد را **read-only** علامت‌گذاری کن (مثل بوستر، چون
    جزئیات/ویرایش آن در صفحهٔ واکسن است).
- دقت: از دوبار شمردن/تداخل با بوسترها جلوگیری کن (بوستر از `VaccinationRecord` می‌آید، occurrence از
  `TreatmentPlan` — دو منبع متفاوت‌اند؛ بررسی کن که یک آیتم دوبار ظاهر نشود).
- بدون اعمال بزوری: اگر permission واکسن نبود، بلوک باید بی‌صدا رد شود (مثل بوستر).
**وابستگی:** پایهٔ T2 (کلیک روی همین آیتم‌ها).
**تصمیم:** رویداد پلن در تقویم اصلی read-only است؛ کلیکش (T2) پاپ‌آپ جزئیات باز می‌کند.

#### ⬜ T2 — پاپ‌آپ جزئیات واکسن با کلیک روی تقویم (هر دو تقویم)
**چه کاری:** کلیک روی یک واکسن/پلن ثبت‌شده روی تقویم → پنجرهٔ جزئیات آن واکسن باز شود،
**هم در تقویم اصلی** و **هم در تقویم واکسن**.
**وضعیت فعلی (کشف‌شده):**
- تقویم اصلی: `OpenBooking` در `AppointmentsCalendarViewModel.cs` خط ۱۹۹۰ برای بوستر فقط
  `OpenPetSheetAsync` می‌زند (نه پاپ‌آپ جزئیات واکسن).
- تقویم واکسن (`TreatmentCalendarViewModel`): کلیک روی روز لیست occurrence می‌دهد؛ جزئیات اختصاصی
  پاپ‌آپ ندارد (باید بررسی شود آیا detail sheet وجود دارد).
**مسیر اجرا:**
- یک View/VM جزئیات واکسن مشترک بساز یا از `VaccinationRecordDetailViewModel.cs` موجود استفاده کن
  (در `Pages/Vaccination/`). ورودی: شناسهٔ رکورد/occurrence/پت + کد واکسن.
- تقویم اصلی: در `OpenBooking`، به‌جای/علاوه‌بر باز کردن پروندهٔ پت برای آیتم‌های واکسن، پاپ‌آپ جزئیات
  واکسن را باز کن (از طریق `IDialogService`). تصمیم: رفتار قبلی پروندهٔ پت را حفظ کن یا با جزئیات واکسن
  جایگزین کن — انتخاب: **پاپ‌آپ جزئیات واکسن** (خواستهٔ صریح کاربر).
- تقویم واکسن: کلیک روی یک آیتم occurrence → همان پاپ‌آپ جزئیات واکسن.
**وابستگی:** T1 (آیتم‌های پلن روی تقویم اصلی).

#### ⬜ T3 — بازچینش تب‌های صفحهٔ واکسن
**چه کاری:**
1. «تقویم واکسن» (`calendar`) را **تب اول** کن.
2. تب «نوبت واکسن» (`visits`) را **حذف** کن.
3. یک تب جدید «واکسن‌های پیش رو» اضافه کن که **ظاهر کاملاً مشابه** صفحهٔ «نوبت‌های پیش رو» تقویم
   (`AppointmentsQueueTabView.axaml`) باشد و واکسن‌های پیش رو را درست نمایش دهد.
**مسیر اجرا:**
- `VaccinationPageViewModel.cs`:
  - مقدار پیش‌فرض `_activeSection` را از `"visits"` به `"calendar"` تغییر بده (خط ۹۲).
  - پراپرتی‌ها/فلگ‌های `visits` را حذف کن (`IsVisitsSection`, `ShowVisitsTools`) و بخش رکوردهای جدولی
    نوبت را حذف کن (یا به تب جدید منتقل نکن — «نوبت واکسن» کاملاً حذف می‌شود).
  - تب جدید `upcoming` اضافه کن: `IsUpcomingSection` + مقداردهی داده از `GetCalendarAsync` یا رکوردهای
    due/scheduled (`VaccinationRecord` با Status = Due/Scheduled). ظاهر را از
    `AppointmentsQueueTabView.axaml` کپی/تطبیق بده (همان استایل‌ها).
- `VaccinationPageView.axaml`: ترتیب تب‌ها را عوض کن (تقویم اول)، تب visits را بردار، تب upcoming اضافه کن.
- دقت به `OnActiveSectionChanged`: مدیریت `calendar`/`history` حفظ شود؛ برای `upcoming` بارگذاری اضافه شود.
- `OnPlanCreated` فعلاً `ActiveSection = "calendar"` می‌زند — سازگار بماند.
**وابستگی:** مستقل؛ ولی با T1/T2 تداخل فایلی دارد (همان VM/View) → مرتب commit کن.

#### ⬜ T4 — هشدار نوبت در پنجشنبه/جمعه
**چه کاری:** اگر نوبت یک واکسن روی جمعه یا پنجشنبه بیفتد، پیامی نشان بده: «همین‌طور تأیید شود یا به روز
دیگری منتقل شود؟» — همهٔ سناریوها در نظر گرفته شوند تا هیچ‌وقت به مشکل نخوریم.
**نکات کشف‌شده:**
- زمان‌بندی پلن در `AddVaccineModalViewModel.BuildSchedule()` ساخته می‌شود؛ تاریخ‌ها `DateOnly` کلینیک-لوکال‌اند.
- بسط تاریخ‌ها در `src/Animora.Domain/Clinical/TreatmentScheduleExpander.cs`؛ یک پلن می‌تواند چند
  occurrence چندروزه تولید کند (EveryNDays/WeeklyOnDays/SpecificDates).
- جلالی: پنجشنبه/جمعه = `DayOfWeek.Thursday`/`DayOfWeek.Friday` (تعطیلات هفتگی ایران).
**مسیر اجرا (سناریوها):**
- هنگام Submit در `AddVaccineModalViewModel`، پیش از `CreateTreatmentPlanAsync`، تاریخ‌های تولیدشده را
  (یا لااقل تاریخ شروع/تاریخ‌های مشخص) بررسی کن؛ اگر occurrenceای در پنج/جمعه بود، یک **دیالوگ تأیید**
  نشان بده با گزینه‌های: «تأیید همین‌طور» / «انتقال به روز بعد (شنبه)» / «انتقال به روز قبل (چهارشنبه)» /
  «انصراف».
  - برای چند occurrence تعطیل: یا پیام تجمیعی («N نوبت در تعطیلات است») یا جابه‌جایی یکدست با یک قانون
    (مثلاً همه به نزدیک‌ترین روز کاری بعد). تصمیم پیشنهادی: یک قانون یکدست + امکان «تأیید همین‌طور».
  - اگر کاربر انتقال را انتخاب کرد، تاریخ‌ها را قبل از ارسال به سرویس اصلاح کن (همان ساختار `TreatmentScheduleInput`
    یا `SpecificDates` معادل).
- محل منطق روز تعطیل: یک helper کوچک در Localization/Vaccination (بررسی کن
  `src/Animora.Desktop/Localization/` چیزی برای تعطیلات دارد؟). از سخت‌کدکردن اجتناب نشود اما استاندارد بماند.
- بدون اعمال بزوری: پیش‌فرض باید «پرسیدن» باشد، نه جابه‌جایی خودکار اجباری.
**وابستگی:** روی `AddVaccineModalViewModel` (مثل T5).

#### ⬜ T5 — انتخاب قلم از انبار هنگام ثبت دارو/واکسن
**چه کاری:** در ثبت دارو/واکسن بشود قلم را **مستقیم از اقلام انبار** انتخاب کرد (برای یادآوری؛
**موجودی کسر نشود**). همچنین امکان **نوشتن دستی نام** برای اقلام غیرموجود در انبار. صفحهٔ انتخاب اقلام
**مشابه انتخاب اقلام در فروش/صندوق هنگام فاکتور** باشد.
**نکات کشف‌شده:**
- مرجع ظاهری: `src/Animora.Desktop/Pages/Sales/CatalogModalView.axaml` + `CatalogModalViewModel.cs`
  (پیکر کاتالوگ فاکتور: فیلترهای دسته، جست‌وجو، ردیف‌های قابل‌انتخاب).
- منبع اقلام انبار: `IInventoryQueryService.GetProductsAsync(ProductListQuery)` → `ProductPage`
  (در `Contracts/Inventory/`). `ProductKind.Vaccine`/`Medication` برای فیلتر نوع.
- در `AddVaccineModalViewModel`، فعلاً واکسن از `SelectedVaccine` (کاتالوگ واکسن) و دارو با `MedicationName`
  دستی انتخاب می‌شود.
**مسیر اجرا:**
- یک مودال انتخاب قلم از انبار بساز (ظاهر از `CatalogModalView`)، یا `CatalogModalViewModel` را تعمیم بده
  برای حالت «انتخاب برای یادآوری» (بدون قیمت/کسر موجودی).
- در `AddVaccineModalViewModel`: دکمهٔ «انتخاب از انبار» که این پیکر را باز کند؛ نتیجه نام/کد قلم را در
  فیلد مناسب (`MedicationName` یا کد واکسن) پر کند. نوشتن دستی هم باقی بماند.
- **موجودی کسر نشود:** هیچ `AdjustStockAsync`/command کاهش موجودی صدا زده نشود — فقط نام/شناسه برای
  یادآوری استفاده شود. در `CreateTreatmentPlanAsync` هم کسر موجودی اضافه نکن.
- `IInventoryQueryService` را در صورت نیاز به `AddVaccineModalViewModel` از طریق DI تزریق کن
  (ثبت در `App.axaml.cs` را بررسی کن — سرویس قبلاً ثبت است).
**وابستگی:** روی `AddVaccineModalViewModel`.

#### ⬜ T6 — حذف واکسن‌های ثابت از انبار واکسن
**چه کاری:** یک‌سری واکسن که به‌صورت ثابت در «انبار واکسن» نمایش داده می‌شوند حذف شوند.
**منبع دقیق (کشف‌شده):** در `VaccinationCatalogQueryHandler` (`VaccinationQueries.cs`) ردیف‌های
`builtIn = VaccineSchedule.All.Select(...)` به کاتالوگ اضافه می‌شوند. اینها واکسن‌های ثابت‌اند.
**مسیر اجرا:**
- `builtIn` را از خروجی `VaccinationCatalogQuery` حذف کن (فقط `own` = کاتالوگ سفارشی کلینیک بماند).
- ⚠️ بررسی تداخل: `VaccineCatalogItem.cs` خط ۷۸ کدهای `VaccineSchedule.All` را «گرفته‌شده» می‌داند. اگر
  نمایش حذف شد ولی کاربر کد built-in را دوباره بسازد، خطای «کد تکراری» می‌دهد. تصمیم: فقط نمایش انبار
  را تمیز کن؛ اگر خواسته شد که آن کدها آزاد شوند، جدا بررسی شود (در حیطهٔ این تسک: فقط حذف نمایش ثابت).
- اثر جانبی روی T5 و `AddVaccineModal`: اگر کاتالوگ خالی شد، مودال ثبت واکسن باید همچنان با «انتخاب از
  انبار» (T5) یا نام دستی کار کند (بررسی `HasCatalog`).

#### ⬜ T7 — نمایش واکسن انبار هنگام ثبت واکسن + ویرایش کامل رکورد
**چه کاری:** موقع ثبت واکسن جدید در واکسیناسیون، واکسن گفته‌شده در انبار هم نمایش داده شود؛ و با کلیک روی
واکسن ثبت‌شده قابلیت ویرایش، تعداد و ... کاملاً موجود باشد.
**مسیر اجرا:**
- نمایش قلم انبار متناظر هنگام انتخاب واکسن در `AddVaccineModalViewModel` (پیوند با T5: وقتی قلم انبار
  انتخاب شد، اطلاعات/موجودی آن نمایش داده شود — صرفاً اطلاعاتی، بدون کسر).
- ویرایش رکورد ثبت‌شده: از `UpdateTreatmentPlanAsync`/`PetTreatmentPlansModalViewModel` استفاده کن
  (ویرایش kind/title/dosage/notes). «تعداد» → بررسی کن آیا به dose/occurrence مربوط است.
  پاپ‌آپ جزئیات (T2) باید دکمهٔ ویرایش داشته باشد که مودال ویرایش را باز کند.
**وابستگی:** T2 (پاپ‌آپ)، T5 (انتخاب انبار).

#### ⬜ T8 — ثبت درست حالت‌های مختلف واکسن در تاریخچه
**چه کاری:** گزارش‌ها و حالت‌های مختلف واکسن به‌درستی در تاریخچه ثبت شوند.
**نکات کشف‌شده:**
- تاریخچه: `VaccinationHistoryViewModel.cs` از `GetHistoryAsync(VaccinationHistoryFilter)` →
  `VaccinationHistoryPage`. انواع رویداد: administered/scheduled/SMS-sent/rescheduled.
- بعد از T1–T7 (پلن جدید، انتقال تعطیلات، انتخاب از انبار، ویرایش، mark-done) باید رویدادهای متناظر
  در تاریخچه ثبت شوند. بررسی کن هر اکشن جدید رویداد تاریخچه تولید کند
  (در `TreatmentCommands.cs`/`VaccinationCommands.cs` و نگاشت تاریخچه).
**مسیر اجرا:**
- پس از تکمیل T1–T7، مرور کن که آیا رویدادهای جدید (ثبت پلن، انتقال روز تعطیل، ویرایش، انجام occurrence)
  در `VaccinationHistoryQuery` دیده می‌شوند؛ اگر نه، رویداد تاریخچهٔ لازم را در command handler اضافه کن.
**وابستگی:** بعد از T1–T7 اجرا شود.

#### ⬜ T9 — هشدار داشبورد برای واکسن‌های حالت پانسیون در همان روز
**چه کاری:** برای واکسن‌هایی که در حالت پانسیون (`TreatmentKind.BoardingAdminister`) ثبت شده‌اند، اگر
occurrence آن‌ها **در همان روز** باشد، هشدار مناسب در بخش هشدار داشبورد نمایش داده شود.
**نکات کشف‌شده:**
- هشدارهای داشبورد در `DashboardSnapshotQuery.BuildAlertsAsync` (خط ۱۱۰) تولید می‌شوند — بلوک‌های
  gate‌دار append-only؛ `MaxAlerts=8`؛ هر نوع جدید = یک بلوک جدید با permission مربوطه.
- `DashboardAlertRow(Kind,Title,Subtitle,DueDay,AmountMinor,Severity,State)`.
- نگاشت کلیک در دسکتاپ: `DashboardPageViewModel.OpenAlertTarget` (خط ۲۲۸) — برای kind جدید مقصد
  صفحه اضافه کن (مثلاً باز کردن تقویم واکسن یا پروندهٔ پت).
**مسیر اجرا:**
- در `BuildAlertsAsync` یک بلوک جدید gate‌دار (permission واکسن/پانسیون) اضافه کن که occurrenceهای
  امروزِ پلن‌هایِ `BoardingAdminister` را از لایهٔ query بخواند و `DashboardAlertRow` با
  `Kind="BoardingVaccine"` (یا نام مناسب) تولید کند.
- query لازم: occurrenceهای امروز با kind=Boarding و state=Pending — از `TreatmentCalendarQuery`
  (from=to=today, filter kinds=[BoardingAdminister]) یا یک query مخصوص.
- `OpenAlertTarget` را برای kind جدید وصل کن.
**وابستگی:** مستقل (بک‌اند داشبورد + دسکتاپ).

### فروش و صندوق

#### ⬜ T10 — فعال‌سازی تخفیف فاکتور و پوشش بیمه
**چه کاری:** هنگام فاکتور زدن، بخش «تخفیف فاکتور» و «پوشش بیمه» کار کنند و درست در فاکتور ثبت/نمایش شوند.
**نکات کشف‌شده (مهم — بک‌اند آماده است):**
- `ISalesQueryService` از قبل دارد: `SetDiscountAsync(invoiceId, DiscountChangeRequest(Kind,Value))`،
  `SetInsuranceAsync(invoiceId, insurerId?)`، `GetInsurersAsync()` → `InsurerRef(Id,Name,Percent)`.
- `InvoiceDetail` فیلدهای `DiscountKind`, `DiscountValue`, `DiscountAmount`, `InsuranceOrgId`,
  `InsuranceLabel`, `InsuranceShare`, `HasDiscount`, `HasInsurance` را دارد.
- در `SalePageView.axaml` خطوط ۷۷۶–۸۱۷، تخفیف و بیمه فقط **TextBlock خواندنی** هستند؛ هیچ ورودی/کامند
  تعاملی ندارند → دلیل «کار نکردن».
**مسیر اجرا:**
- تخفیف: ورودی تعاملی (درصد/مبلغ با همان toggle «درصد/مبلغ» موجود) + کامند `SetDiscountCommand` در
  `SalePageViewModel` که `_service.SetDiscountAsync(invoiceId, new DiscountChangeRequest(kind,value))`
  را صدا بزند و `SelectedInvoice` را از نتیجه به‌روز کند.
- بیمه: تبدیل باکس «پوشش بیمه» به یک انتخابگر (ComboBox/دکمهٔ انتخاب) از `GetInsurersAsync()`؛ کامند
  `SetInsuranceCommand` → `SetInsuranceAsync(invoiceId, insurerId)`؛ به‌روزرسانی `SelectedInvoice`.
- نمایش: `DiscountText`, `InsuranceShareText`, `HasDiscount`, `HasInsurance` از قبل bind‌اند؛ پس از
  mutate فقط refresh لازم است.
- در فاکتور چاپی/خلاصه هم درست منعکس شود (`SalesPrintDocuments.cs` را بررسی کن که تخفیف/بیمه را نشان دهد).
**وابستگی:** مستقل.

#### ⬜ T11 — بازطراحی «نرخ خدمات» + افزودن مدیریت پوشش بیمه
**چه کاری:**
1. استایل تب «نرخ خدمات» خیلی بد و نامرتب است و کل صفحه به نرخ خدمات محدود شده؛ تمیزش کن و «نرخ خدمات»
   باکس مستقل خودش را داشته باشد، تمیز و قابل‌استفاده.
2. یک قسمت برای **اضافه کردن پوشش بیمه‌ها** و درصد یا مبلغ پوششی آن‌ها اضافه کن. افزودن بیمه در سناریوهای
   مختلف درست کار کند و همه‌چیز یکپارچه باشد.
**نکات کشف‌شده:**
- `ServiceRatesView.axaml` فعلاً یک `StackPanel` ساده با toolbar + لیست ردیف‌ها؛ باکس مستقل ندارد.
- بیمه‌ها: `InsurerRef(Id,Name,Percent)` + `GetInsurersAsync` (منبع: `InsurersQuery` در بک‌اند،
  نگاشت `InsurerReadModel.ToContract` در `SalesMapping.cs` خط ۲۵۴).
- **تأیید شد:** `IServiceRateService` فقط `GetAsync`/`CreateAsync`/`UpdateAsync` نرخ خدمات دارد و
  **هیچ CRUD بیمه‌گری ندارد**. پس برای «افزودن بیمه» باید متد جدید
  (`AddInsurerAsync`/`UpdateInsurerAsync`/`DeleteInsurerAsync`) در `IServiceRateService` (یا
  `ISalesQueryService`) اضافه شود + پیاده‌سازی Client.Services + command در Application + ذخیره در
  Domain/Persistence. یکپارچه با `InsurersQuery`/`GetInsurersAsync` و `SetInsuranceAsync` (T10).
**مسیر اجرا:**
- بازطراحی `ServiceRatesView.axaml`: «نرخ خدمات» را در یک باکس/کارت مستقل بگذار (Border با عنوان)،
  استایل تمیز و منظم (هماهنگ با سایر کارت‌های اپ). از توکن‌های تم `Resources/Theme` استفاده کن.
- یک باکس دوم «پوشش بیمه‌ها» اضافه کن: لیست بیمه‌گرها + دکمهٔ «افزودن بیمه» + ورودی درصد/مبلغ پوشش.
  - اگر CRUD بیمه در سرویس نیست: متد `AddInsurerAsync`/`UpdateInsurerAsync`/`DeleteInsurerAsync` را در
    `IServiceRateService` یا سرویس مناسب اضافه کن، پیاده‌سازی در Client.Services + Application command +
    Domain/Persistence. استاندارد و یکپارچه با `GetInsurersAsync` و `SetInsuranceAsync` (T10).
- یکپارچگی: بیمه‌گرهای اضافه‌شده باید در انتخابگر بیمهٔ فاکتور (T10) ظاهر شوند و درصد/مبلغ پوشش در محاسبهٔ
  `InsuranceShare` درست اعمال شود (بررسی محاسبه در `SalesMapping`/Domain).
**وابستگی:** به T10 گره خورده (هر دو بیمه را لمس می‌کنند) — T10 را اول انجام بده، بعد T11.

---

## 🔚 تسک پایانی (اجباری، آخرین مرحله)

#### ⬜ T12 — حذف همین فایل Plan
**چه کاری:** پس از اتمام T1–T11، **فقط همین فایل** (`Plan/vaccination-sales-integration.md`) حذف شود.
فایل‌های دیگر Plan و Roadmap دست‌نخورده بمانند. سپس push نهایی.
```bash
git rm Plan/vaccination-sales-integration.md
scripts/git/push.sh "chore(plan): [T12] remove completed vaccination/sales plan file"
```

---

## 📝 یادداشت‌های اجرایی برای مدل بعدی
- هر تسک = یک commit + push مستقل (قانون قفل‌شده). تغییرات سنگین → push تمیز.
- Build/Test اجرا **نکن** (خواستهٔ کاربر).
- بخش‌های Contracts/Client.Services/Application/Domain/Persistence را فقط وقتی لمس کن که تسک لازم دارد
  (T1, T9, احتمالاً T11). بقیه عمدتاً Desktop (View/ViewModel) هستند.
- هنگام افزودن DTO/interface جدید: ثبت DI در `src/Animora.Desktop/App.axaml.cs` را فراموش نکن.
- فارسی/RTL/جلالی را رعایت کن؛ رقم‌ها با `PersianNumberFormatter`/`VaccinationPresentation.Fa`.
- ترتیب پیشنهادی اجرا: T6 → T1 → T2 → T3 → T5 → T7 → T4 → T9 → T8 → T10 → T11 → T12
  (شروع از کم‌وابسته‌ها؛ T8 بعد از تغییرات واکسن؛ T11 بعد از T10).
