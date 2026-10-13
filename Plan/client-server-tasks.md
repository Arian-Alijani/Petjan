# Plan — Client/Server Coherence + Password-Change Removal + Invoice Details

> **برای مدلی که این فایل را در یک سشن تازه می‌خواند:** این فایل کاملاً خوداتکاست.
> همهٔ مسیرها، شماره‌خط‌ها، و نتیجه‌گیری‌ها از قبل بررسی و استخراج شده‌اند. نیازی به
> بررسی دوبارهٔ ریپو نیست — مستقیم طبق هر تسک عمل کن. **قانون قفل‌شده:** بعد از تکمیل
> هر تسک حتماً `scripts/git/push.sh "msg"` اجرا کن وگرنه کار شکست خورده است.

## 0. Context the executor needs (read once, no re-exploration)

- **Repo**: Animora — SaaS دامپزشکی. C#/.NET 10, Avalonia دسکتاپ (کلاینت) + سرور چندمستاجری ASP.NET Core + PostgreSQL. دسکتاپ offline-first روی SQLite محلی.
- **جریان داده**: `View → ViewModel → Contracts interface → Client.Services impl → Application dispatch → SQLite محلی`. نوشتن‌ها به صورت **command** ژورنال می‌شوند و از طریق **outbox/sync** به سرور `POST /api/commands` می‌روند.
- **سرور یک command را چطور می‌پذیرد**: `src/Animora.Server/Commands/CommandPayloadCatalog.cs` → متد `Resolve`:
  - کلیدهای JSON پیلود باید **set-equal** با نام پارامترهای سازندهٔ رکورد command سرور باشند (OrdinalIgnoreCase, camelCase).
  - تطبیق بر اساس `(AggregateType + مجموعهٔ دقیق نام پراپرتی‌ها)`. اگر مجموعه برابر نباشد → `VALIDATION_FAILED "payload shape"` و **command رد می‌شود**.
  - سپس identity چک می‌شود: `CommandTypeName / AggregateTypeName / AggregateIdOrNull` باید با envelope یکی باشند، وگرنه `"payload identity"`.
- **نکتهٔ مهم که قبلاً فیکس شده (commit 79a46d47)**: اعضای ژورنالینگ (`CommandTypeName`,`AggregateTypeName`,`AggregateIdOrNull`) در `src/Animora.Application/Commands/CommandFingerprint.cs` حالا از پیلود canonical حذف می‌شوند (JSON TypeInfo modifier `DropJournalingMembers`). قبلش هر command با `"payload shape; ruleset=1"` رد می‌شد. این فیکس پابرجاست — **خرابش نکن**.
- **پوشش هندلرها**: هر command که کلاینت واقعاً emit می‌کند، روی سرور هندلر دارد. ۵ ثابتِ declared-but-unused فقط رزرو هستند و gap نیستند: `FinanceChequeStatus`, `FinancePeriodClose`, `InventoryReceiptPost`, `PurchasingOrderSubmit`, `SalesAdjustmentAdd` (هیچ‌جای کد emit/handle نمی‌شوند — دست نزن مگر تسک جدید بخواهد).
- **بیلد/تست**: در این sandbox **dotnet نصب نیست**. بیلد/تست به CI موکول است. تغییرات را از نظر صحت کد و قراردادها بررسی کن؛ Full build/test طولانی ممنوع.
- **ساختار پروژه‌ها** (برای پیدا کردن سریع):
  - کلاینت سرویس‌ها: `src/Animora.Client.Services/<Area>/`
  - commandها + payload shape واقعی: `src/Animora.Application/UseCases/<Area>/`
  - هندلر و payload-catalog سرور: `src/Animora.Server/Handlers/ServerHandlerModuleS07..S10.cs`
  - مپینگ replication سرور→کلاینت: `src/Animora.Persistence.PostgreSql/Replication/SyncContractCatalog.cs`

---

## T1 — حذف الزام تغییر رمز بعد از ثبت/ورود اکانت  [x] انجام شد ✅

> **نتیجهٔ انجام T1 (2026-10-13):** دو فراخوانی `owner.RequirePasswordChange()` حذف شدند:
> `TenantProvisioner.cs` (provisioning اولیهٔ Owner) و `OwnerCredentialAdministration.cs` (reset رمز توسط اپراتور).
> حالا Owner تازه‌ساخته `MustChangePassword=false` دارد و بلافاصله بعد از ورود از همهٔ قابلیت‌ها استفاده می‌کند.
> فراخوانی باقی‌مانده در `SignInCommand.cs:88` (`if (mustChange) user.RequirePasswordChange();`) بی‌خطر است —
> فقط فلگِ از-قبل-true را حین rehash حفظ می‌کند و چون هیچ مسیری دیگر true نمی‌کند، هرگز فعال نمی‌شود.
> سیستم اپراتور (`/admin/*`, `OperatorAccount`) دست‌نخورده. تست `AdminAuthTests.MustChangePassword_*` اپراتوری است و معتبر می‌ماند.

**هدف**: کاربر با هر اکانتی بعد از ورود بتواند مستقیم از قابلیت‌ها استفاده کند، بدون مجبور شدن به تغییر رمز.

### نتیجهٔ بررسی (از قبل انجام شده — دوباره نگرد)
منبع الزام تغییر رمز برای **اکانت کلینیک (Owner/staff)** دقیقاً دو جا است:

1. **`src/Animora.Persistence.PostgreSql/Identity/TenantProvisioner.cs:69`**
   ```csharp
   owner.RequirePasswordChange();   // ← این خط باعث MustChangePassword=true روی Owner جدید می‌شود
   ```
   (در متد `CreateTenantWithOwnerAsync`، بلافاصله بعد از `SetPasswordHash` در خط 68.)

2. **`src/Animora.Persistence.PostgreSql/Subscriptions/OwnerCredentialAdministration.cs:40`**
   ```csharp
   owner.SetPasswordHash(...); owner.RequirePasswordChange();  // ← مسیر reset رمز Owner توسط اپراتور
   ```

- فلگ `User.MustChangePassword` (دامین: `src/Animora.Domain/Identity/User.cs:76,170,175`) از طریق
  `LoginResponse.MustChangePassword` → `OnlineLoginOutcome.MustChangePassword` → فقط **لاگ** می‌شود در
  `src/Animora.Desktop/Pages/Login/LoginViewModel.cs:338`. **هیچ‌جای کلاینت یا سرور این فلگ را برای
  بلاک‌کردن قابلیت‌ها enforce نمی‌کند** (نه middleware، نه gate، نه endpoint اجباری برای کلاینت).
- بنابراین صرفِ **تنظیم‌نشدن فلگ در provisioning/reset** مسئله را کامل حل می‌کند. نیازی به تغییر UI لاگین نیست.

### ⚠️ چیزی که نباید دست بزنی (سیستم جداگانه)
`MustChangePassword` مربوط به **اپراتور/پنل ادمین فروشنده** کاملاً جداست و ربطی به کاربر کلینیک ندارد؛ دست نزن:
- `src/Animora.Server/Operator/OperatorBootstrapper.cs:32`, `OperatorContextMiddleware.cs:43`,
  `src/Animora.Server/Admin/AdminAccountEndpoints.cs:31`, `OperatorCookie.cs:33`,
  `OperatorAccount.cs`, `OperatorReadQueries.cs`, `OperatorAuth.cs`. اینها روی route `/admin/*` و جدول
  `OperatorAccount` کار می‌کنند — بیرون از دامنهٔ این تسک.

### اقدام
1. در `TenantProvisioner.cs` خط 69: حذف `owner.RequirePasswordChange();` (کامنت کوتاه بگذار که چرا حذف شد).
2. در `OwnerCredentialAdministration.cs` خط 40: حذف فراخوانی `owner.RequirePasswordChange();` (فقط `SetPasswordHash` بماند). کامنت کوتاه توضیحی اضافه کن.
3. (اختیاری اگر تستی وجود داشت) اگر تستی انتظار `MustChangePassword==true` بعد از provisioning دارد، آن انتظار را به `false` به‌روزرسانی کن. اول بگرد: `grep -rn "RequirePasswordChange\|MustChangePassword" tests/`.
4. `OnlineLoginOutcome.MustChangePassword`/`LoginResponse.MustChangePassword`/لاگ در LoginViewModel را **نگه‌دار** (بی‌خطرند، همیشه false می‌شوند؛ حذفشان تغییر API بی‌دلیل است).

### پذیرش
- هیچ مسیر provisioning/reset دیگر `MustChangePassword=true` ست نمی‌کند.
- کد سرور/دامین کامپایل‌پذیر می‌ماند (امضاها تغییر نکرده، فقط دو فراخوانی حذف شده).
- سیستم اپراتور دست‌نخورده.

**پس از اتمام → `scripts/git/push.sh "fix(identity): clinic owner no longer forced to change password after provisioning (T1)"` و این تسک را ✅ کن.**

---

## T2 — فاکتورها با جزئیات کامل به سرور بروند (الان انگار خالی می‌روند)  [x] انجام شد ✅

> **نتیجهٔ انجام T2 (2026-10-13):**
> - `CommandFingerprint.DropJournalingMembers` سالم است؛ shape هر ۱۱ command فاکتور با پارامترهای سازنده set-equal است (تست جدید `InvoiceCommandPayloadTests` + `HandlerCoverageTests.InvoiceChainPayloads_ProducedByClient_ResolveInServerCatalog`).
> - **ریشهٔ واقعی «فاکتور خالی» (یافتهٔ جدید):** هندلرها id تولید می‌کردند (`Invoice.Create` → `IdGenerator.NewId()`، و id سطر → `NewId`). کلاینت و سرور هر دو همان هندلر را اجرا می‌کنند، پس سرور فاکتورِ **با id متفاوت** می‌ساخت و commandهای بعدی (`AddLine/Owner/Discount/Post` با `InvoiceId` کلاینت) روی سرور `NotFound` می‌شدند → فاکتور خالی/بدون سطر.
> - **فیکس:** id تعیینی از `OperationId`: `Invoice.Create(..., id:)` پارامتر جدید؛ `CreateBlankInvoice`/`CreateInvoiceFromStay|Visit` فاکتور را با `id = context.OperationId` می‌سازند؛ `AddInvoiceLine`/`AddCustomInvoiceLine` شناسهٔ سطر را `context.OperationId` می‌گذارند (تا Edit/Remove با `ExpectedLineId` روی سرور هم match شوند). OperationId یکتا (ULID) و در retry ثابت است ⇒ idempotent.

**هدف**: اطمینان از اینکه فاکتور با تمام سطرها/مالک/تخفیف/بیمه و اطلاعات صحیح روی سرور ثبت می‌شود، نه خالی.

### نتیجهٔ بررسی (از قبل انجام شده — دوباره نگرد)
- **فاکتور به‌صورت یک پیلود بزرگ نمی‌رود.** `PostInvoiceCommand` فقط `{InvoiceId}` را حمل می‌کند
  (`src/Animora.Application/UseCases/Sales/PostInvoiceCommand.cs:20`). سطرها/مالک/تخفیف/بیمه از طریق
  **commandهای جداگانهٔ قبلی** ساخته می‌شوند که هرکدام باید جداگانه ژورنال و sync شوند:
  - `CreateBlankInvoiceCommand` / `CreateInvoiceFromVisitCommand` / `CreateInvoiceFromStayCommand` → `SalesInvoiceCreate`
  - `AddInvoiceLineCommand {InvoiceId, CatalogItemId, Quantity}` و
    `AddCustomInvoiceLineCommand {InvoiceId, Title, UnitPriceMinor, Quantity}` → هر دو `SalesInvoiceLineAdd`
  - `AssignInvoiceOwnerCommand`, `SetInvoiceDiscountCommand`, `SetInvoiceInsuranceCommand`, `EditInvoiceLineCommand`, `RemoveInvoiceLineCommand`
  - و در پایان `PostInvoiceCommand` → `SalesInvoicePost`
- **همهٔ commandهای Sales، `IJournaledCommand` هستند** (بررسی شد: ۱۵+ فایل در `UseCases/Sales/` همه journaled). پس سطرها در اصل باید منتقل شوند.
- **علت احتمالی «خالی رفتن» فاکتور**: روی سرور، هر command بر اساس **set-equality دقیق کلیدهای پیلود با نام
  پارامترهای سازنده** تطبیق می‌خورد (`CommandPayloadCatalog.Resolve`). اگر پیلودِ یک command سطر، شکلش دقیقاً
  با سازندهٔ رکورد سرور یکی نباشد → `VALIDATION_FAILED "payload shape"` و **فقط آن command سطر رد می‌شود**،
  در حالی که `SalesInvoicePost` ممکن است بعداً بیاید و فاکتور بدون سطر ثبت/نمایش داده شود → **فاکتور خالی**.
- **دو ثبت برای یک CommandType** در `ServerHandlerModuleS09.cs:17-18` (هم `AddInvoiceLineCommand` هم
  `AddCustomInvoiceLineCommand` روی `SalesInvoiceLineAdd`). این **عمداً و درست** است چون shapeهایشان متفاوت‌اند
  (builder در خط 67-68 اگر دو shape یکسان شوند throw می‌کند). پس خطرش فقط وقتی است که پیلودِ کلاینت با **هیچ‌کدام**
  set-equal نشود.
- **فیکس BUG1 (commit 79a46d47)** قبلاً اعضای ژورنالینگ را از پیلود حذف کرده؛ پس اصلی‌ترین علت تاریخی خالی‌رفتن
  رفع شده. این تسک = **تأیید نهایی + پوشش هر shape باقی‌مانده**.

### اقدام (هدفمند، بدون بررسی بیهوده)
1. **تأیید حذف اعضای ژورنالینگ هنوز کار می‌کند**: `src/Animora.Application/Commands/CommandFingerprint.cs`
   — متد `DropJournalingMembers` و آرایهٔ `JournalingMemberNames` باید موجود باشند. اگر merge چیزی خراب کرده، بازگردان.
2. **برای هر command فاکتور، set-equality را بررسی کن**: نام پارامترهای سازندهٔ رکورد (در `UseCases/Sales/*.cs`)
   را با آنچه سرور در `ServerHandlerModuleS09.cs` ثبت کرده (همان رکوردها) تطبیق بده — چون سرور از **همان نوع رکورد
   Application** استفاده می‌کند، نام‌ها ذاتاً یکی‌اند. **ریسک واقعی**: فقط اگر کلاینت پیلود را با نامی غیر از
   پارامتر سازنده سریالایز کند. نقطهٔ سریال‌سازی کلاینت: `src/Animora.Persistence.Sqlite/Commands/CommandJournal.cs`
   و مسیر push در `src/Animora.Client.Services/Sync/SyncOrchestrator.cs`. مطمئن شو پیلودِ ذخیره‌شده = همان canonical
   payload از `CommandFingerprint` (یعنی از روی خود رکورد، با camelCase، بدون اعضای ژورنالینگ).
3. **تست سریال‌سازی موجود**: commit fd888e8e یک تست «payload excludes journaling keys» اضافه کرده. بگرد
   `grep -rn "payload" tests/ | grep -i journal` و اگر لازم شد یک تست کوچک برای `AddCustomInvoiceLineCommand`
   (shape دوم) اضافه کن که set-equal با سازندهٔ سرور می‌شود. (تست‌نویسی سبک مجاز است؛ run سنگین نه.)
4. **بازسازی فاکتور روی سرور (projector)**: مطمئن شو هندلر سرور بعد از apply شدن commandها، سطرها را روی
   aggregate فاکتور نگه می‌دارد (سرور همان `AddInvoiceLineCommandHandler` را از `AddSalesUseCases()` اجرا می‌کند —
   `ServerHandlerModuleS09.cs:11`). پس منطق دامین مشترک است؛ اگر سطرها روی کلاینت درست‌اند، روی سرور هم درست‌اند
   **مشروط بر پذیرفته‌شدن پیلود**. تمرکز اصلی = مرحلهٔ 2 (پذیرش پیلود).
5. اگر shape ناسازگاری پیدا شد، **کلاینت را با سازندهٔ سرور هماهنگ کن** (یا برعکس)، نه با hack در سرور.

### پذیرش
- پیلودِ canonical هر command فاکتور (`SalesInvoiceCreate`, `SalesInvoiceLineAdd` هر دو shape, `SalesInvoiceOwnerSet`, `SalesInvoiceDiscountSet`, `SalesInvoiceInsuranceSet`, `SalesInvoicePost`) با نام پارامترهای سازندهٔ رکورد دقیقاً set-equal است و اعضای ژورنالینگ ندارد.
- یک تست (موجود یا جدید سبک) اثبات می‌کند پیلود سطرِ custom و معمولی هر دو توسط catalog پذیرفته می‌شوند (payload shape نمی‌خورد).

**پس از اتمام → `scripts/git/push.sh "fix(sales): ensure invoice line/owner/discount commands reach server with correct payload shape (T2)"` و این تسک را ✅ کن.**

---

## T3 — انسجام کلاینت↔سرور برای ماژول‌های لیست‌شده  [ ] در حال انجام / [ ] انجام شد

**ماژول‌ها**: دارو/واکسن، پانسیون (Boarding)، انبار (Inventory)، بیماران/سرپرستان (Patients/Owners)،
چک (Cheques)، تاریخچه (History)، ویزیت/پرونده (Visits/PetFile).

**هدف**: اطمینان از اینکه برای هر ماژول، هر نوشتنِ کلاینت (command) روی سرور هندلر + payload درست دارد،
و داده‌های read از سرور به کلاینت replicate می‌شوند — استاندارد و بدون قطع ارتباط.

### نتیجهٔ بررسی (از قبل انجام شده — دوباره نگرد)
- **پوشش command سرور کامل است**: ۸۳ CommandType هندلر دارند؛ هر command که کلاینت emit می‌کند هندلر دارد.
  نگاشت ماژول→CommandType (از `grep CommandTypes.* در ServerHandlers`):
  - **واکسن/درمان**: `VaccinationCatalogAdd`, `VaccinationRecordAdminister`, `VaccinationRecordReminder`,
    `VaccinationTreatmentCreate/Update/Cancel`, `VaccinationTreatmentOccurrenceDone` ✓
  - **ویزیت/پرونده**: `VisitsVisitStart/Note/Vitals/Complete`, `VisitsRxAdd/Remove` ✓ (دارو در نسخه = `VisitsRx*`)
  - **پانسیون**: `BoardingRoomCreate/Update`, `BoardingStayCreate/Checkout/Reschedule/Transfer`, `BoardingItemAdd/Remove` ✓
  - **انبار**: `InventoryCategoryCreate/Rename/Delete`, `InventoryProductCreate/Update/Delete`,
    `InventoryVariantAdd`, `InventoryStockAdjust/StockSet` ✓ + خرید: `PurchasingOrderCreate/Cancel`,
    `PurchasingReceiptRecord`, `PurchasingSupplierCreate` ✓
  - **بیماران/سرپرستان**: `PatientsOwnerCreate/Update/Delete`, `PatientsPetCreate/Update/Delete`, `PatientsPetWeightAdd` ✓
  - **چک**: `FinanceChequeCreate/Update/Transition/Delete` ✓ + هزینه/دوره‌ای: `FinanceExpensePost/Reverse`, `FinanceRecurring*` ✓
  - **تاریخچه (History)**: نوشتن ندارد؛ **read-only** است (از جدول `ActivityEvent`/replica ساخته می‌شود). روی کلاینت
    `src/Animora.Client.Services/History/HistoryQueryService.cs`. انسجامش = خوانده‌شدن درست از داده‌های replicate‌شده.
- **replication سرور→کلاینت**: `SyncContractCatalog.cs` شامل Owners, Pets, Products, ProductVariants,
  ProductCategories, Rooms, BoardingStays(+Items, HandoffLines), BoardingServiceDefs, Insurers, Appointments,
  MedicalHistory(Entry), ReceptionQueueEntries, Suppliers, LedgerAccounts, AccountingPeriods, StaffMemberships,
  RoleDefinitions, Tenant, DocumentReferences و … پس aggregateهای اصلی همه replicate می‌شوند.
- **جمع‌بندی**: ساختار از نظر پوشش **منسجم است**. این تسک = **ممیزی دقیق جزئیات** هر ماژول برای یافتن
  ناسازگاری‌های ریز shape/نام‌پراپرتی که مثل T2 باعث رد خاموش command می‌شوند، یا فیلدی که کلاینت می‌فرستد ولی
  هندلر سرور نادیده می‌گیرد.

### اقدام (هدفمند — برای هر ماژول همین چک‌لیست کوتاه)
برای هر ماژول از لیست، فقط این ۳ چیز را تطبیق بده (بدون خواندن کل ماژول):
1. **Shape command**: نام پارامترهای سازندهٔ رکورد در `src/Animora.Application/UseCases/<Area>/*.cs`
   = ثبت catalog در `ServerHandlerModuleS07..S10.cs` (چون سرور همان رکورد را ثبت می‌کند، معمولاً یکی است —
   فقط dual-registrationها و commandهای چندسازنده‌ای را با دقت نگاه کن، مثل T2).
2. **هندلر سرور فیلدها را استفاده می‌کند**: هندلر از `AddXxxUseCases()` همان هندلر Application است
   (هندلر دامین مشترک کلاینت/سرور). پس اگر فیلدی در هندلر مصرف می‌شود، روی سرور هم مصرف می‌شود. فقط بررسی کن
   هیچ فیلد ورودی‌ای در سازنده نیست که هندلر بی‌خیالش شده باشد.
3. **Replication read**: aggregate مربوط به ماژول در `SyncContractCatalog.cs` حاضر است (لیست بالا).
   اگر aggregate جدیدی (مثل `TreatmentPlan`, `VaccineCatalogItem`, `RecurringExpense`) در لیست replication
   **غایب** بود، آن را به‌عنوان یافته ثبت کن و اگر واقعاً باید به کلاینت بیاید، اضافه کن.
   - **نقطهٔ بررسی ویژه**: `TreatmentPlan`/`TreatmentOccurrence`/`VaccineCatalogItem` (migration 031) و
     `RecurringExpense` (migration 029) aggregateهای post-C11 هستند؛ چک کن در `SyncContractCatalog` هستند یا نه.
     (در لیست grep بالا دیده نشدند — **اولویت بررسی اینجاست**.)

### وضعیت اجرا (به‌روز)
- [x] **T3a Patients/Owners** — یافته: همان ریشهٔ T2 (id تولیدی در هندلر ⇒ کلاینت/سرور id متفاوت). فیکس: `IdGenerator.ForOperation(operationId, slot, at)` (slot0 = خود OperationId، slotهای بعدی hash قطعی) + پارامتر `id:` در `Owner.Register`/`Pet.Register`/`WeightEntry.Record`؛ `CreateOwner` (owner=slot0, pet=slot1)، `CreatePet`، `AddWeightEntry` از آن استفاده می‌کنند.
- [x] **T3b Visits/PetFile (+ Reception/Scheduling)** — همان ریشهٔ T2/T3a: id تولیدی در هندلر. فیکس: پارامتر `id:` در `Visit.Open`/`ReceptionQueueEntry.Create`/`Appointment.Book`/`MedicalHistoryEntry`؛ `StartNewVisit` (visit=slot0, queue=1, owner=2, pet=3)، `StartVisitFromTicket` (visit=0, queue=1)، `AddPrescriptionLine` (line=slot0 وقتی LineId نیست)، `CompleteVisit` (history=slot0)، `CreateWalkIn`، `BookAppointment`، `CreateBookingPatient`، `QueueEntryResolver` همگی از `IdGenerator.ForOperation` استفاده می‌کنند. TreatmentPlan/Occurrence/VaccineCatalogItem در SyncContractCatalog حاضرند ✓
- [x] **T3c Vaccination** — همان ریشه: id تولیدی. فیکس: پارامتر `id:` در `VaccinationRecord.Administer`، `TreatmentPlan.Create` (+ `occurrenceId` برای هر occurrence)، `TreatmentOccurrence.Create`، `VaccineCatalogItem.Create`؛ `CreateTreatmentPlan` (plan=slot0, occurrenceها=1..n, ledger=slotهای ≥1_000_000)، `AddVaccineCatalogItem` (slot0)، `MarkVaccinationDone` (record=0, history=1)، `RecordVaccinationReminder`/`MarkTreatmentOccurrenceDone`/ledger سایر commandها نیز قطعی. VaccineCatalogItem/TreatmentPlan/Occurrence در SyncContractCatalog حاضرند ✓
- [ ] T3d Boarding (Room/Stay/StayItem id قطعی)
- [ ] T3e Inventory + Purchasing (Product/Variant/Supplier/Order/Receipt id قطعی)
- [ ] T3f Cheques + RecurringExpense (RecurringExpense در SyncContractCatalog **غایب** است ← بررسی/افزودن)
- [ ] T3g History (read-only، فقط تأیید)

### ترتیب پیشنهادی (هر کدام یک push جدا)
- T3a: Patients/Owners  → push
- T3b: Visits/PetFile (+ Rx + TreatmentPlan/Occurrence replication) → push
- T3c: Vaccination (+ VaccineCatalogItem replication) → push
- T3d: Boarding → push
- T3e: Inventory + Purchasing → push
- T3f: Cheques + RecurringExpense → push
- T3g: History (read coherence از replica) → push

> اگر یک ماژول واقعاً مشکلی نداشت، صرفاً یافتهٔ «coherent — no change» را در همین فایل زیر آن ماژول بنویس و push بزن
> (commit docs). لازم نیست تغییر کد الکی بزنی.

### پذیرش کل T3
- برای هر ۷ ماژول، سه چک بالا انجام و نتیجه (fix یا coherent) زیر همان بخش ثبت شده.
- aggregateهای post-C11 (TreatmentPlan/VaccineCatalogItem/RecurringExpense) وضعیت replicationشان مشخص و در صورت نیاز اصلاح شده.

**پس از اتمام هر زیرتسک → push جدا با پیام واضح. در پایان کل T3 این تسک را ✅ کن.**

---

## قوانین کلی اجرا (برای هر تسک)
- بعد از **هر** تسک/زیرتسک تکمیل‌شده → `scripts/git/push.sh "type(scope): msg"` (قفل‌شده، حتمی).
- اگر وسط یک تسک فیکس سنگین/کد زیاد نوشتی → همان‌جا push جدا با پیام درست.
- بررسی فایل‌ها فقط هدفمند و بر اساس نیاز. Full build/test طولانی ممنوع (dotnet در sandbox نیست؛ CI چک می‌کند).
- تغییرات استاندارد و باکیفیت؛ قراردادهای `AGENTS.md` / `CONVENTIONS.md` رعایت شوند (ViewModel فقط به Contracts
  وابسته، EF/SQL/HTTP در Desktop نیاید، سیستم اپراتور دست‌نخورده بماند).

## T-END — حذف این فایل (آخرین تسک)  [ ]
وقتی T1، T2، T3 همه ✅ شدند:
1. `rm Plan/client-server-tasks.md`
2. `scripts/git/push.sh "chore(plan): remove completed client-server-tasks plan (final task)"`
(فقط همین فایل حذف شود؛ به فایل‌های دیگر و roadmap کاری نداشته باش.)
