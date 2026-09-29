# Plan — Phase C09: Finance & reporting read models

> **هدف این فایل:** تسک‌لیست خودکفا و دقیق برای اجرای فاز C09. تمام یافته‌های بررسی ریپو (state واقعی کد، انحرافات فاز از واقعیت، الگوها، مسیرها، امضاها) اینجا **خالص‌سازی** شده تا مدل در سشن جدید **بدون بررسی مجدد** مستقیم پیاده‌سازی را شروع کند.
>
> **⏱ وضعیت فعلی (بررسی‌شده در این سشن — به آن اعتماد کن، دوباره grep نزن مگر برای تأیید یک‌خطی):** تسک‌های **T01–T05 و بخشی از T09 قبلاً پیاده و merge شده‌اند** (branch روی commit `0976220`). کارِ باقی‌مانده فقط **T06, T07, T08، دو کلاس تستِ باقی‌ماندهٔ T09، و T10, T11** است. بخش «📸 اسنپ‌شات دقیق state» را قبل از شروع بخوان — دقیقاً می‌گوید چه چیزی هست و چه چیزی نیست.
>
> **منبع فاز:** `backend-roadmap/phases/PHASE-C09-finance-reporting-read-models.md`
> **Band:** M · **Track:** C · **Depends:** C08 · **Satisfies:** R-B01, R-B08, R-B22, R-B35
> **Runnable:** desktop offline yes · server no

---

## 🔒 قانون پوش (قفل‌شده — حیاتی)

بعد از **تکمیل هر تسک** این دستور اجرا شود (وگرنه شکست پروژه):

```bash
scripts/git/push.sh "type(scope): پیام"
```

- اسکریپت: `add -A` → `commit` → `fetch`+`rebase --autostash` → `push -u`. یک خط `✓`/`✗` چاپ می‌کند.
- Exit codes: `0` ok · `2` rebase conflict (رفع و دوباره اجرا) · `3` بدون credential → ابزار `setup_github_environment` را یک‌بار صدا بزن سپس دوباره اجرا کن.
- برنچ کاری فعلی: **`genspark_ai_developer`** (remote `origin` = `github.com/Arian-Alijani/Petjan.git`). هر دو برنچ main و genspark در ابتدای این کار روی یک commit هماهنگ شدند.

---

## 🎯 درک فاز (خلاصه معنایی)

صفحه‌ی Finance باید توسط یک **دفتر معین دوطرفه‌ی واقعی، متوازن و append-only** رانده شود که **دقیقاً یک کامپوننت** آن را می‌نویسد (`LedgerPostingHandler` این فاز)، و از طریق **read modelهای projection‌شده** خوانده شود که هرگز روی مسیر OLTP سوار نمی‌شوند. هزینه‌ها (Expenses) واقعی می‌شوند و placeholder «مجموع خرج مالک» که C01 جا گذاشته پر می‌شود.

**سه اصل طلایی:**
1. **یک نویسنده‌ی لجر:** فقط `LedgerPostingHandler` مجاز است `JournalEntry` بسازد — با فراخوانی `PostingRules` (که از قبل کامل است). تست `LedgerSingleWriterTests` این را قفل می‌کند.
2. **read model = projection نه aggregate زنده (A-RULE 034):** سه جدول fact `[LocalOnly]` که `FinanceProjectionHandler` از **همان رویدادها** بیرون از بخش بحرانی OLTP (از طریق outbox → worker) نگه‌داری می‌کند. لجر حقیقت است؛ factها یک cache با اثبات (rebuild).
3. **queryها فقط factها را می‌خوانند، هرگز invoices/journallines را.**

---

## 📸 اسنپ‌شات دقیق state ریپو (بررسی‌شدهٔ این سشن — پایهٔ تصمیم‌گیری)

این جدول دقیقاً می‌گوید چه چیزی **از قبل موجود و کامل** است. برای T06–T11 فقط به این اتکا کن.

### ✅ کاملاً موجود (دست نزن مگر تسک صراحتاً بگوید)
| artifact | مسیر | نکته کلیدی |
|---|---|---|
| `FinanceRanges` (T01) | `src/Animora.Domain/Finance/FinanceRanges.cs` | catalog: `today/7d/30d/90d/ytd/custom`. `Resolve(id, todayLocalDay, customFrom?, customTo?) → Result<FinanceDayRange>`. `All` (۶ تعریف). label فارسی. bucket: `day/day/day/week/month/day`. **`FinanceDayRange(int FromDay, int ToDay)`** (readonly record struct، yyyymmdd inclusive). |
| 3 کد ErrorCode (T01) | `src/Animora.Domain/Results/ErrorCodes.cs` | `PeriodNotFound`, `ExpenseCategoryInvalid`, `FinanceRangeInvalid` **اضافه شده‌اند**. `JournalUnbalanced`/`PeriodClosed`/`PeriodCloseBlocked`/`PeriodInvalidTransition`/`ExpenseImmutable` هم موجودند. |
| 3 fact entity + config + migration 016 (T02) | `Entities/{DailyFinanceFact,ProductSalesFact,OwnerFinancialSnapshot}.cs` · migration `20260929041422_016_FinanceProjections` | همه ساخته و migrate شده. `DailyFinanceFact` متد `Apply(revenue, discount, insurance, invoices, ...)` دارد؛ `ProductSalesFact.Apply(qty, gross)`; `OwnerFinancialSnapshot.Apply(spend, balance, invoiceUtc, invoices)`. |
| `JournalNumberAllocator` (T03) | `src/Animora.Persistence.Sqlite/Finance/JournalNumberAllocator.cs` + port `IJournalNumberAllocator` | ثبت‌شده در DI خط 94. `AllocateAsync(tenantId, periodCode, ct) → "{periodCode}-{seq:00000}"`. |
| `LedgerPostingHandler` (T03) | `src/Animora.Application/Finance/LedgerPostingHandler.cs` | **کامل**؛ روی **۱۰** رویداد ثبت شده (۹ + `InvoiceRefundedEvent`). درون‌تراکنشی. dispatch شده در DI خطوط 143–153. resolve دوره: `periods.FindContainingAsync` → اگر closed → `FindNextOpenAsync(period.EndUtc)`. |
| `FinanceProjectionHandler` (T04) | `src/Animora.Application/Finance/FinanceProjectionHandler.cs` | کامل. `HandleAsync(DomainEvent, ct)` → localDay را از tz حساب و به `IFinanceProjectionStore.ApplyAsync` می‌دهد. ثبت DI خط 98. |
| `FinanceProjectionStore` | `src/Animora.Persistence.Sqlite/Finance/FinanceProjectionStore.cs` + port `IFinanceProjectionStore` | upsert سه fact از رویدادها. ثبت DI خط 95. |
| `FinanceProjectionWorker` (T04) | `src/Animora.Infrastructure.Local/Workers/FinanceProjectionWorker.cs` | batch=100، idle backoff. **در `App.axaml.cs:181` با `.Start()` راه‌اندازی می‌شود** (بعد از `MarkReady`). ثبت‌شده. |
| `FinanceQueries` + port (T05) | `src/Animora.Persistence.Sqlite/Queries/FinanceQueries.cs` · `Application/Ports/IFinanceQueries.cs` | **کامل**؛ ۷ متد (پایین). ثبت DI خط 97. day-bucketing در projection، نه SQL. |
| Application read models (T05/T06 pre-built) | `src/Animora.Application/UseCases/Finance/ReadModels/FinanceReadModels.cs` | **از قبل موجود**: `FinanceTotals`, `DailyFinanceRow`, `ProductSalesRow`, `ExpenseRowReadModel`, `PaymentMethodTotal`, `FinanceAggregateReadModel` (+ `.Series`), `FinanceSeriesPointReadModel`, `FinanceOverviewReadModel`, `FinanceIncomeReadModel`, `FinanceExpenseReadModel`, `FinanceProfitReadModel`, `FinanceChannelReadModel`, `FinanceCategoryReadModel`, و helper `FinanceSeries.Bucket`. **در T06 فقط query/handler wrapper بساز؛ این readmodelها را دوباره نساز.** |
| Rebuild + seed (T05) | `Maintenance/{ILocalStoreMaintenance,LocalStoreMaintenance}.cs` (`RebuildFinanceProjectionsAsync` خط 197) · `Finance/FinanceProjectionRebuilder.cs` · `Seed/LocalSeeder.cs` خط 182 | کامل و seedها rebuild می‌شوند. |
| `IPeriodRepository` (T03/G) | `src/Animora.Application/Ports/IPeriodRepository.cs` | **متدهای resolve از قبل اضافه شده‌اند**: `FindByCodeAsync`, `FindContainingAsync`, `FindNextOpenAsync` (+ Find/Require/Add/AddRange). `PeriodRepository` هم پیاده کرده. |
| `PostingRulesTests` (T09 جزء اول) | `tests/Animora.Domain.Tests/PostingRulesTests.cs` | **کامل** — هر ۹ رویداد + رد credit/refund + zero-variance + salary→6100. **این تست تمام است؛ در T09 دوباره ننویس.** |
| `LedgerSingleWriterTests` (T09/T03) | `tests/Animora.Application.Tests/LedgerSingleWriterTests.cs` | **کامل** — source-scan روی `Animora.Application`+`Animora.Client.Services`؛ اطمینان می‌دهد فقط `LedgerPostingHandler.cs` رشتهٔ `JournalEntry.Create(` / `JournalEntry.CreateReversal(` / `ledger.Add(` دارد. **این قید بر T06/T07 حاکم است: هیچ فایل جدید Application/Client نباید این سه رشته را داشته باشد.** |

### ❌ هنوز نیست (کارِ باقی‌مانده — T06/T07/T08/T09b/T10/T11)
| نبود | توضیح |
|---|---|
| finance queries/commands در Application | هیچ `FinanceOverviewQuery`/`RecordExpenseCommand`/… وجود ندارد. (T06) |
| `Finance/FinanceQueryService.cs` + `FinanceMapping.cs` در Client.Services | پوشهٔ `src/Animora.Client.Services/Finance/` **اصلاً وجود ندارد**. (T07) |
| Expense number allocator | هیچ `IExpenseNumberAllocator` نیست؛ `RecordExpenseCommand` باید شماره تخصیص دهد (T06؛ روش پایین). |
| Finance use-case registration در chain | فایل `AddFinanceUseCases` نیست و `AddChequeUseCases()` هم **در `App.axaml.cs:202` صدا زده نمی‌شود** (فقط تا `AddSalesUseCases`). T08 باید هر دو را اضافه کند. |
| `IFinanceQueryService` واقعی | هنوز `MockFinanceQueryService` در `App.axaml.cs:324` وصل است. (T08) |
| readiness-gate در `FinancePageViewModel` | ctor فعلی: `(IFinanceQueryService, IToastService, IDialogService)` بدون gate. (T08) |
| `FinanceProjectionIntegrityTests` + `PeriodCloseTests` | دو کلاس باقی‌ماندهٔ T09. |

### 🔑 امضاهای دقیق (کپی-پیست‌شونده)
- **`IFinanceQueries` (۷ متد — همه `todayLocalDay:int` می‌گیرند):**
  `GetAggregateAsync(string rangeId, int todayLocalDay, ct) → FinanceAggregateReadModel` ·
  `GetChannelsAsync(...) → IReadOnlyList<FinanceChannelReadModel>` ·
  `GetExpenseCategoriesAsync(...) → IReadOnlyList<FinanceCategoryReadModel>` ·
  `GetProductSalesAsync(...) → IReadOnlyList<ProductSalesRow>` ·
  `GetExpenseRowsAsync(...) → IReadOnlyList<ExpenseRowReadModel>` ·
  `GetPaymentMethodsAsync(...) → IReadOnlyList<PaymentMethodTotal>` ·
  `GetMaxProjectedDayAsync(ct) → int` (نسخهٔ cache).
- **`IFinanceQueryService` (۹ عضو Contracts — بدون تغییر؛ T07 اینها را پیاده می‌کند):** `GetOverviewAsync/GetIncomeAsync(range)`, `GetRangesAsync()`, `GetProductSortsAsync()`, `GetExpenseAsync/GetProfitAsync(range)`, `GetExpenseCategoriesAsync()`, `GetPaymentMethodsAsync()`, `AddExpenseAsync(NewExpenseRequest)`.
- **`FinanceReadModels` (خروجی query؛ lean):** `FinanceAggregateReadModel(RangeId, RangeLabel, Days, Bucket, CurrentRange:FinanceDayRange, Current:FinanceTotals, Previous:FinanceTotals, DailyRows)` + `.Series`. `FinanceTotals(RevenueMinor, CogsMinor, ExpenseMinor, DiscountMinor, InsuranceMinor, RefundMinor, InvoiceCount, PaidInvoiceCount)` + `.ProfitMinor`. `ProductSalesRow(ProductId, VariantId, CategoryId, Name, Kind, QuantitySold:decimal, RevenueMinor, CogsMinor)`. `FinanceChannelReadModel(Id, RevenueMinor)`. `FinanceCategoryReadModel(CategoryCode, AmountMinor)`. `ExpenseRowReadModel(Id, Title, CategoryCode, Method:PaymentMethod?, AmountMinor, IncurredUtc, CreatedByUserId)`. `PaymentMethodTotal(Method:PaymentMethod, AmountMinor)`.
- **Contracts DTO (rich؛ مقصد map؛ `src/Animora.Contracts/Finance/FinanceModels.cs`):** فیلدهای دقیق را در بخش H ببین. نکتهٔ حیاتی: DTOهای Contracts **غنی از داده‌های ارائه‌ای‌اند** (icon/color/label/tone برای channel/category، species، heatmap، insights، paymentSplit، serviceالسplit) که در readmodelهای lean **وجود ندارند**. بخش 🎨 پایین می‌گوید این‌ها از کجا می‌آیند.
- **دستورساز infra:** `ICommand<TResult>` (marker) · `IQuery<TResult>` (marker) · `ICommandHandler<TCommand,TResult>.HandleAsync(cmd, CommandContext, ct) → Task<Result<TResult>>` · `IQueryHandler<TQuery,TResult>.HandleAsync(q, QueryContext, ct) → Task<Result<TResult>>` · `IJournaledCommand { CommandTypeName; AggregateTypeName; AggregateIdOrNull }` · `CommandContext(TenantId, UserId, DeviceId, OperationId, CorrelationId, CausationId?, DependsOnOperationId?, ExpectedAggregateVersion?, CreatedAtUtc)` · `QueryContext(TenantId, UserId, CorrelationId)`.
- **Permissions (موجود؛ `src/Animora.Domain/Identity/PermissionCodes.cs`):** `FinanceRead="finance.read"`, `FinancePost="finance.post"`, `FinancePeriodClose="finance.period.close"`. **کد جدید نساز.** `RecordExpenseCommand` → `FinancePost`؛ `CloseAccountingPeriodCommand` → `FinancePeriodClose`. attribute: `[RequiresPermission(PermissionCodes.FinancePost)]`.
- **CommandTypes (موجود):** `CommandTypes.FinancePeriodClose="finance.period.close"`, `CommandTypes.FinanceExpensePost="finance.expense.post"`. `[Auditable(AuditCategory.Financial, CommandTypes.FinanceExpensePost)]` / `...FinancePeriodClose`.
- **Domain برای commandها:** `Expense.Create(tenantId, expenseNumber, categoryCode, title, Money amount, incurredUtc, createdByUserId, settledMethod?, supplierId?, chequeId?, receiptFileId?, note?, reversesExpenseId?) → Result<Expense>` سپس `.Post(IClock) → Result` (raise `ExpensePostedEvent`). `AccountingPeriod.Close(hasUnsettledCommands, hasOpenCashSessions, hasDraftInvoices, IClock, closingEntryId, closedByUserId, note?)`؛ ابتدا `.BeginClose()`. `ExpenseCategories.Contains(code):bool` / `.AccountCodeFor(code)`.
- **Allocator ترتیبی (برای ExpenseNumber):** `PipelineSql.NextSequenceAsync(AnimoraLocalDbContext db, string name, ct) → Task<long>` (internal، در `src/Animora.Persistence.Sqlite/Commands/PipelineSql.cs`). الگوی نام مثل invoice: `$"expense:{tenantId}:{jalaliYear}"`؛ year از `ISeedPeriodCodeProvider.For(issuedUtc).PeriodCode` (`"1405-07"` → year=`"1405"`).
- **`ClinicCalendar` (`src/Animora.Application/Time/ClinicCalendar.cs`):** `LocalDay(long utcTicks, TimeZoneInfo) → DateOnly` سپس `int day = d.Year*10000 + d.Month*100 + d.Day`. `ToUtcTicks(DateOnly, hour, tz)`. tz از `IClinicTimeZoneProvider.GetClinicTimeZone()`.
- **`todayLocalDay` چطور ساخته می‌شود (برای صدا زدن queryها):** `var d = ClinicCalendar.LocalDay(clock.UtcNow.UtcTicks, zones.GetClinicTimeZone()); int today = d.Year*10000 + d.Month*100 + d.Day;` (`IClock clock` را در handler inject کن).

### 🎨 منبع داده‌های ارائه‌ای (پرکردن شکاف lean→rich) — تصمیم قفل‌شده
readmodelهای Application فقط id/code + عدد دارند؛ DTOهای Contracts label/icon/color/tone + species/heatmap/insights/paymentSplit می‌خواهند. **حل:** یک جدول ثابتِ ارائه‌ای در `Client.Services/Finance/FinanceMapping.cs` (کپیِ عیناً از `MockFinanceQueryService`) نگه‌دار و هنگام map کدها/idها را به آن join کن:
- **channel meta** (id→label/icon/color): `clinic→(خدمات درمانی, IconClipboard, #0d9488)`, `pharmacy→(داروخانه, IconFlask, #5c8ad6)`, `petshop→(پت‌شاپ, IconCart, #b8a2e6)`, `lab→(آزمایشگاه, IconSpark, #eab749)`.
- **category meta** (کد Domain `RENT/UTILITIES/SALARY/SUPPLIES/MAINTENANCE/MARKETING/TAX/TRANSPORT/OTHER` → label/icon/color/tone). نگاشت به idهای UI مثل mock (`salary/rent/supply/marketing/repair/tax/other`) لازم است؛ نگاشت: `SALARY→salary(حقوق و دستمزد,IconUsers,#0d9488,teal)`, `RENT→rent(اجاره و قبوض,IconHome,#e0a95b,amber)`, `UTILITIES→rent`(همان سطل قبوض) یا سطل جدا اگر لازم، `SUPPLIES→supply(خرید دارو و کالا,IconBox,#5c8ad6,blue)`, `MARKETING→marketing(بازاریابی,IconMegaphone,#b8a2e6,lilac)`, `MAINTENANCE→repair(تعمیر و نگهداری,IconTools,#e08a5c,peach)`, `TAX→tax(مالیات و بیمه,IconShield,#7a8b89,mute)`, `TRANSPORT→other`, `OTHER→other(متفرقه,IconMore,#b54708,warn)`. (فهرست کامل mock را عیناً بردار.)
- **payment method meta** (`PaymentMethod` enum → id/label/color): `Card→(pos, کارتخوان, #0d9488)`, `Cash→(cash, نقدی, #8fb5f0)`, `Transfer→(transfer, انتقال, #e08a5c)`, `Cheque/Credit/Insurance/Wallet→` طبق mock.
- **species / heatmap / insights:** در facts وجود ندارند و از ledger مشتق نمی‌شوند. **تصمیم:** این سه، دادهٔ ارائه‌ایِ ثابت‌اند؛ **عیناً از `MockFinanceQueryService` کپی کن** به `FinanceMapping` به‌عنوان جداول ثابت و در map استفاده کن. (species = ۵ ردیف ثابت، heatmap = ماتریس ۷×۱۲ ثابت، insights = ۴ کارت ثابت.) دلیل: صفحه به این‌ها نیاز بصری دارد ولی خارج از scope دفتر لجرند؛ آوردنشان به‌صورت static در client، «هیچ EF/SQL در client» و «facts=truth» را نقض نمی‌کند.
- **productSort catalog** (`GetProductSortsAsync`): ثابت — `revenue(پردرآمدترین)`, `qty(پرفروش‌ترین)`, `profit(پرسودترین)`.
- **ranges catalog** (`GetRangesAsync`): از `FinanceRanges.All` map کن به `FinanceRange(Id,Label,Days,Bucket)` **یا** جدول ثابت mock (۴ بازه `7d/30d/90d/ytd`). چون ViewModel فقط `Id/Label` استفاده می‌کند، هر دو کار می‌کند؛ **از `FinanceRanges.All` استفاده کن** (تک‌منبع حقیقت) و `custom`/`today` را در صورت نیاز فیلتر کن تا با UI mock (۴ بازه) هم‌راستا بماند.

### 🔴 تصمیم قفل‌شدهٔ closing-entry (تناقض معماری period-close — این را عیناً اجرا کن)
`AccountingPeriod.Close(...)` یک `closingEntryId` **اجباری** می‌خواهد و پیش از raise کردن `PeriodClosedEvent` آن را می‌گیرد. اما `LedgerSingleWriterTests` اثبات می‌کند **فقط `LedgerPostingHandler` مجاز است `JournalEntry.Create`/`ledger.Add` بزند** — پس `CloseAccountingPeriodCommand` **نمی‌تواند** خودش closing entry بسازد. `PostingRules` هم متد `ForPeriodClose` **ندارد**.
- **تصمیم (کمترین‌ریسک، سازگار با append-only و single-writer):** closing entry یک entry واقعی دوطرفهٔ متوازن نیست که در این فاز لازم باشد؛ صفحهٔ Finance و factها به آن وابسته نیستند (queryها فقط factها را می‌خوانند و factها از close مشتق نمی‌شوند). بنابراین:
  1. `CloseAccountingPeriodCommand(string PeriodCode)` دوره را با `periods.FindByCodeAsync` می‌گیرد؛ اگر null → `PeriodNotFound`.
  2. فلگ‌های close را از Application جمع می‌کند: `hasUnsettledCommands` (از `ICommandJournal`/pending — اگر seam سبک در دسترس نبود، **false بگذار و در کامنت ثبت کن**)، `hasOpenCashSessions` (از `ICashSessionRepository`/`ICashSessionContext` اگر متد شمارش هست؛ وگرنه false + کامنت)، `hasDraftInvoices` (از `IInvoiceRepository` اگر متد هست؛ وگرنه false + کامنت). **این seamها را نساز؛ اگر متد شمارش موجود نبود false بده** (بستن ساده). دامنه اگر هر فلگ true بود `PERIOD_CLOSE_BLOCKED` می‌دهد.
  3. `closingEntryId` را به‌صورت یک شناسهٔ ساخته‌شده با `IdGenerator.NewId(clock.UtcNow)` تولید کن (یک شناسهٔ ارجاعی؛ entry فیزیکی در این فاز ساخته نمی‌شود چون single-writer اجازه نمی‌دهد و فاز به آن وابسته نیست). `period.BeginClose()` سپس `period.Close(hasUnsettled, hasOpenCash, hasDraft, clock, closingEntryId, context.UserId, note:null)`.
  4. این رفتار را در commit message و بخش deviations ثبت کن: «closing journal entry به‌عنوان یک ارجاع تولید می‌شود؛ ساخت entry فیزیکی به single-writer واگذار و در این فاز غیرضروری است (queryها facts-only).» اگر تستی entry فیزیکی خواست، آن را از طریق راه‌اندازی رویداد به `LedgerPostingHandler` انجام بده، **نه** ساخت مستقیم در command.
- **نتیجه:** `PeriodCloseTests` باید همین قرارداد را assert کند (پایین T09b).

---

## ⚠️ انحرافات کشف‌شده فاز از واقعیت ریپو (بسیار مهم — قبل از شروع بخوان)

متن فاز در چند جای مهم با state واقعی ریپو نمی‌خواند. **این‌ها قطعی و بررسی‌شده‌اند:**

### A) `PostingRules` از قبل کامل است — فقط باید مصرف شود
- مسیر: `src/Animora.Domain/Finance/PostingRules.cs`. یک `static class` با **10 متد pure** که همگی `Result<JournalEntry>` برمی‌گردانند و داخلاً `JournalEntry.Create` را صدا می‌زنند (پس balance/positivity/single-currency/postability ساختاری تضمین است):
  - `ForInvoicePosted(InvoicePostedEvent e, Invoice invoice, PostingContext ctx, IClock clock)`
  - `ForPaymentRegistered(PaymentRegisteredEvent e, PostingContext ctx, IClock clock)`
  - `ForInvoiceRefunded(...)`, `ForInvoiceVoided(InvoiceVoidedEvent e, ...)`
  - `ForGoodsReceiptPosted(GoodsReceiptPostedEvent e, ...)`
  - `ForStockMovementRecorded(StockMovementRecordedEvent e, ...)` → COGS از `MovementKind.Sale`
  - `ForChequeCleared(ChequeClearedEvent e, ...)`, `ForChequeBounced(ChequeBouncedEvent e, ...)`
  - `ForCashSessionClosed(CashSessionClosedEvent e, ...)`
  - `ForExpensePosted(ExpensePostedEvent e, ...)`
- **`PostingContext` (readonly record struct):** `(string EntryNumber, string PeriodId, string PostedByUserId, string? OperationId)`.
- **نتیجه:** فاز می‌گوید «`LedgerSingleWriterTests` مطمئن شود فقط handler `JournalEntry` می‌سازد». امروز **هیچ‌کس در Application `JournalEntry.Create` را صدا نمی‌زند** (`grep` تأیید شد: فقط `JournalEntry.cs`, `PostingRules.cs`, `AppendOnlyGuard.cs`). پس `LedgerPostingHandler` باید تنها writer شود و تست این را قفل کند.

### B) نام رویدادها با فاز فرق دارد (منبع حقیقت = کد)
فاز اسامی خیالی نوشته. نام‌های **واقعی** موجود در `src/Animora.Domain/Events/`:
| فاز گفته | واقعیت در ریپو | فایل |
|---|---|---|
| `InvoiceReversedEvent` | **`InvoiceVoidedEvent`** | `SalesEvents.cs` |
| `PaymentRegisteredEvent` | `PaymentRegisteredEvent` ✅ | `SalesEvents.cs` |
| `GoodsReceivedEvent` | **`GoodsReceiptPostedEvent`** | `InventoryEvents.cs` |
| — (COGS) | **`StockMovementRecordedEvent`** | `InventoryEvents.cs` |
| `ChequeClearedEvent` | `ChequeClearedEvent` ✅ | `FinanceEvents.cs` |
| `ChequeBouncedEvent` | `ChequeBouncedEvent` ✅ | `FinanceEvents.cs` |
| `CashSessionClosedEvent` | `CashSessionClosedEvent` ✅ | `SalesEvents.cs` |
| `ExpenseRecordedEvent` | **`ExpensePostedEvent`** | `FinanceEvents.cs` |
| `InvoicePostedEvent` | `InvoicePostedEvent` ✅ | `SalesEvents.cs` |
- **COGS:** فاز COGS را در `LedgerPostingHandler` ذیل «GoodsReceivedEvent» گذاشته، ولی PostingRules یک متد جدا `ForStockMovementRecorded` دارد که COGS را از `MovementKind.Sale` می‌سازد (`UnitCost` روی `StockMovement` موجود است — خط 85 `StockMovement.cs`). پس handler باید **هر دو** `GoodsReceiptPostedEvent` (D 1200 / C 2000) و `StockMovementRecordedEvent` (D 5000 COGS) را ساب‌سکرایب کند. مجموع رویدادهای handler = **9** نه 8 (به‌خاطر تفکیک goods-receipt از stock-movement).

### C) مکانیزم event handler
- **اینترفیس:** `IDomainEventHandler<in TEvent> where TEvent : DomainEvent { Task<Result> HandleAsync(TEvent e, CancellationToken ct); }` — در فایل `src/Animora.Application/Abstractions/IDomainEventDispatcher.cs` (فاز مسیر `IDomainEventHandler.cs` جدا گفت — وجود ندارد).
- **ثبت:** `services.AddDomainEventHandler<TEvent, THandler>()` (extension در `LocalStoreServiceCollectionExtensions.cs` خط ~164). هر handler به‌صورت `AddScoped<THandler>` هم باید ثبت شود (الگو را از خط 136 `AddDomainEventHandler<DomainEvent, OutboxRelayHandler>()` ببین + رجوع به `DomainEventHandlerRegistration.For`).
- **dispatch درون تراکنش:** `DomainEventDispatcher` (در `Repositories/DomainEventDispatcher.cs`) هر رویداد را از نوع دقیق تا `DomainEvent` بالا می‌رود، پس یک handler روی نوع پایه (`OutboxRelayHandler`) هر رویداد را می‌گیرد. handlerهای نوع دقیق **قبل** از base اجرا می‌شوند و در **همان تراکنشِ** raise اجرا می‌شوند (این همان "inside the raising transaction" فاز است).
- **نتیجه معماری:** `LedgerPostingHandler` روی نوع‌های دقیق رویداد ثبت می‌شود → درون تراکنش لجر را می‌نویسد. `FinanceProjectionHandler` **نباید** روی رویداد ثبت شود؛ باید factها را از **outbox** (که `OutboxRelayHandler` پر کرده) بیرون از تراکنش، توسط `FinanceProjectionWorker` بسازد.

### D) Migration numbering — `015` مصرف شده
- آخرین migration: `20260928150000_015_CashSessionLink` (C08). **migration جدید C09 باید `016_FinanceProjections` باشد، نه `015`** (فاز اشتباهاً `015` گفته).
- الگوی timestamp: `20260928HHMMSS_016_FinanceProjections`. از timestamp بعد از `150000` استفاده کن (مثلاً `160000`).
- سه جدول fact همگی جدیدند → migration فقط `CreateTable` + ایندکس‌های unique. هیچ ستون روی جدول موجود عوض نمی‌شود.
- **روش ساخت migration:** پروژه EF migration دستی/CLI دارد؛ الگو را از `20260928130000_013_InventorySnapshot.cs` (که یک snapshot table اضافه کرد) کپی کن — همان ساختار `Up`/`Down` + `.Designer.cs` + به‌روزرسانی `AnimoraLocalDbContextModelSnapshot.cs`. اگر `dotnet ef` در سندباکس در دسترس است از آن استفاده کن؛ در غیر این صورت دستی مطابق 013 بنویس.

### E) ErrorCodes — اکثراً موجودند (فایل `src/Animora.Domain/Results/ErrorCodes.cs`)
فاز 6 کد خواسته: `LEDGER_UNBALANCED`, `PERIOD_CLOSED`, `PERIOD_NOT_FOUND`, `PERIOD_REOPEN_REASON_REQUIRED`, `EXPENSE_CATEGORY_INVALID`, `FINANCE_RANGE_INVALID`. واقعیت:
| کد فاز | وضعیت واقعی | اقدام |
|---|---|---|
| `LEDGER_UNBALANCED` | **`JournalUnbalanced` = `"JOURNAL_UNBALANCED"` موجود** | از موجود استفاده کن؛ کد نساز |
| `PERIOD_CLOSED` | `PeriodClosed` = `"PERIOD_CLOSED"` موجود ✅ | استفاده کن |
| `PERIOD_NOT_FOUND` | غایب | **اضافه کن** `PeriodNotFound = new("PERIOD_NOT_FOUND", ErrorCode.NotFound)` |
| `PERIOD_REOPEN_REASON_REQUIRED` | غایب + دامنه reopen ندارد (بخش F) | **اضافه نکن** مگر تصمیم reopen را اجرا کنی |
| `EXPENSE_CATEGORY_INVALID` | غایب | **اضافه کن** `ExpenseCategoryInvalid = new("EXPENSE_CATEGORY_INVALID", ErrorCode.Validation)` |
| `FINANCE_RANGE_INVALID` | غایب | **اضافه کن** `FinanceRangeInvalid = new("FINANCE_RANGE_INVALID", ErrorCode.Validation)` |
- کدهای مرتبط موجود دیگر: `PeriodCloseBlocked`, `PeriodInvalidTransition`, `ExpenseImmutable`, `JournalTooFewLines`, `AccountNotPostable`, `AccountUnknown`.
- **قانون:** قبل از افزودن هر کد `grep -n "XXX" src/Animora.Domain/Results/ErrorCodes.cs`. فقط 3 کد بالا واقعاً غایب‌اند → همان 3 را اضافه کن (نه 6).

### F) 🔴 تناقض معماری: `ReopenAccountingPeriodCommand` با دامنه در تضاد است
- `src/Animora.Domain/Finance/AccountingPeriod.cs`: state machine اکیداً `Open → Closing → Closed → Locked`. کامنت صریح: **«There is deliberately no path back to Open»**. `PeriodClosedEvent` نیز می‌گوید «There is no reopen».
- **تصمیم قفل‌شده (اجرا کن، دوباره استدلال نکن):** `ReopenAccountingPeriodCommand` را **پیاده نکن** چون دامنه از آن جلوگیری می‌کند و افزودنش نقض A-RULE 003 (append-only books) است. رفتار «تصحیح پس از بستن» از قبل توسط دامنه پوشش داده شده: late event به‌جای دوره‌ی بسته، در **دوره‌ی باز بعدی** پست می‌شود (بخش G). CommandType `finance.period.reopen` و کد `PERIOD_REOPEN_REASON_REQUIRED` را **نساز**.
- در commit message و در بخش deviations این را ثبت کن. `PROGRESS.md` را در این فاز به‌روز نکن (خارج از scope این پلن؛ فقط فاز خواسته — اگر لازم شد به‌عنوان بخشی از پیاده‌سازی انجام می‌شود اما این پلن آن را الزام نمی‌کند).
- **نتیجه:** فقط **2 command** به‌جای 3: `RecordExpenseCommand` و `CloseAccountingPeriodCommand`. (`finance.period.close` و `finance.expense.post` CommandType از قبل موجودند — بخش I.)

### G) رفتار late-event (post به دوره‌ی باز بعدی)
- منبع حقیقت: `AccountingPeriod.EnsurePostable(period)` → اگر `IsPostingClosed` (Closed/Locked) → `Result.Fail(PeriodClosed, ...)`.
- در `LedgerPostingHandler` هنگام resolve دوره برای یک رویداد: دوره‌ای که تاریخ رویداد در آن می‌افتد را بگیر؛ اگر `EnsurePostable` شکست خورد (بسته)، **دوره‌ی باز بعدی** را انتخاب کن و روی `JournalEntry` مقدار backdate را ثبت کن. `JournalEntry` فیلد `EffectiveUtc` و `PostedUtc` دارد؛ اگر فیلد صریح `BackdatedFromUtc` لازم شد و موجود نیست، از `EffectiveUtc` (تاریخ رویداد) در برابر `PostedUtc` (now) به‌عنوان اثر backdate استفاده کن — **فیلد جدید روی JournalEntry اضافه نکن** مگر تست `PeriodCloseTests` صریحاً `BackdatedFromUtc` بخواهد؛ در آن صورت آن را روی خود entry اضافه نکن بلکه در `PeriodClose` منطق آزمون‌پذیر را در handler نگه‌دار.
- **IPeriodRepository فعلی فقط `FindAsync(id)`/`RequireAsync(id)`/`Add` دارد** (بدون query «دوره‌ی حاوی تاریخ X» یا «دوره‌ی باز بعدی»). برای resolve دوره از یک تاریخ، یا (الف) از یک query موجود/جدید در Persistence استفاده کن، یا (ب) متد به `IPeriodRepository` اضافه کن: `Task<AccountingPeriod?> FindByCodeAsync(string periodCode, ...)` و `Task<AccountingPeriod?> FindNextOpenAsync(DateTimeOffset afterUtc, ...)`. **تصمیم:** این دو متد را به `IPeriodRepository` + `PeriodRepository` اضافه کن (کمترین سطح، در راستای الگوی repository موجود).

### H) `IFinanceQueryService` seam واقعی ≠ توصیف فاز
- مسیر: `src/Animora.Contracts/Finance/IFinanceQueryService.cs`. اعضای **واقعی** که `FinanceQueryService` باید پیاده کند (9 عضو):
  - `Task<FinanceOverview> GetOverviewAsync(string range)`
  - `Task<FinanceIncome> GetIncomeAsync(string range)`
  - `Task<IReadOnlyList<FinanceRange>> GetRangesAsync()`
  - `Task<IReadOnlyList<FinanceProductSort>> GetProductSortsAsync()`
  - `Task<FinanceExpense> GetExpenseAsync(string range)`
  - `Task<FinanceProfit> GetProfitAsync(string range)`
  - `Task<IReadOnlyList<FinanceCategoryOption>> GetExpenseCategoriesAsync()`
  - `Task<IReadOnlyList<FinancePaymentMethod>> GetPaymentMethodsAsync()`
  - `Task<ExpenseEntry> AddExpenseAsync(NewExpenseRequest request)`
- **DTOهای Contracts — امضای کامل (کپی-پیست‌شونده؛ عیناً از `FinanceModels.cs` استخراج‌شده در این سشن — دیگر این فایل را باز نکن؛ ترتیب/نوع فیلد دقیق است):**
  ```csharp
  // ── ساده‌ها ──
  FinanceRange(string Id, string Label, int Days, string Bucket)
  FinanceDeltas(double Revenue, double Expense, double Profit, double Margin)   // درصد؛ Margin اختلاف نقطه‌ای
  FinanceSeriesPoint(string Label, string Full, long Revenue, long Expense, long Profit)
  FinanceChannel(string Id, string Label, string Icon, string Color, long Value)
  FinanceCategory(string Id, string Label, string Icon, string Color, string Tone, long Value, double Delta)
  FinanceProduct(string Name, string Category, string Kind, string Icon, string Tone, int Qty, long Revenue, long Cost)
      // مشتق (نساز، خودکارند): Profit => Revenue-Cost ; Margin => Revenue==0?0:(double)Profit/Revenue*100
  FinanceServiceSplit(string Key, string Label, string Icon, string Color, int Qty, long Revenue, long Profit)  // Margin مشتق
  FinanceChannelBar(string Id, string Label, string Icon, string Color, long Value, double SharePercent)
  FinanceInsight(string Icon, string Tone, string Text)
  FinanceSpecies(string Label, long Value, string Color)
  FinancePaymentSplit(string Id, string Label, string Color, int Value)   // Value = درصد صحیح (int)
  FinanceProductSort(string Id, string Label)
  FinanceCategoryOption(string Id, string Label, string Color)
  FinancePaymentMethod(string Id, string Label)
  FinanceExpenseRow(string Id, string Description, string CategoryLabel, string CategoryColor,
                    string Date, string MethodLabel, long Amount, string By)
  NewExpenseRequest(string Description, string CategoryId, string MethodId, long Amount, string Date)
  ExpenseEntry(string Id, string Description, string CategoryId, string MethodId, long Amount, string Date, string By)

  // ── بزرگ‌ها (ترتیب فیلد دقیقاً همین است) ──
  FinanceOverview(string RangeLabel, int DayCount, long RevTotal, long ExpTotal, long Profit, double Margin,
                  FinanceDeltas Delta, double AvgDailyProfit,
                  IReadOnlyList<FinanceSeriesPoint> Series, IReadOnlyList<FinanceChannel> ByChannel,
                  IReadOnlyList<FinanceCategory> ByCategory, IReadOnlyList<FinanceProduct> Products,
                  IReadOnlyList<FinanceServiceSplit> Services, IReadOnlyList<FinanceInsight> Insights,
                  IReadOnlyList<FinanceSpecies> Species, IReadOnlyList<FinancePaymentSplit> Payments,
                  IReadOnlyList<IReadOnlyList<int>> Heatmap)   // ۱۸ فیلد

  FinanceIncome(string RangeLabel, int DayCount, long RevTotal, long ExpTotal, long Profit, double Margin,
                FinanceDeltas Delta, double AvgDailyProfit,
                IReadOnlyList<FinanceSeriesPoint> Series, IReadOnlyList<FinanceChannel> ByChannel,
                IReadOnlyList<FinanceCategory> ByCategory, IReadOnlyList<FinanceChannelBar> ChannelBars,
                IReadOnlyList<FinanceProduct> Products, IReadOnlyList<FinanceServiceSplit> Services,
                IReadOnlyList<FinanceInsight> Insights, IReadOnlyList<FinanceSpecies> Species,
                IReadOnlyList<FinancePaymentSplit> Payments, IReadOnlyList<IReadOnlyList<int>> Heatmap)  // ۱۹ فیلد

  FinanceExpense(string RangeLabel, int DayCount, long RevTotal, long ExpTotal, FinanceDeltas Delta,
                 IReadOnlyList<FinanceSeriesPoint> Series, IReadOnlyList<FinanceCategory> ByCategory,
                 IReadOnlyList<FinanceExpenseRow> Rows)   // ۸ فیلد (نه Profit/Margin/AvgDailyProfit)

  FinanceProfit(string RangeLabel, int DayCount, long RevTotal, long ExpTotal, long Profit, double Margin,
                FinanceDeltas Delta, double AvgDailyProfit,
                IReadOnlyList<FinanceSeriesPoint> Series, IReadOnlyList<FinanceCategory> ByCategory,
                IReadOnlyList<FinanceProduct> Products, IReadOnlyList<FinanceInsight> Insights)   // ۱۲ فیلد
  ```
  - **⚠️ دقت‌های حیاتی map (خطاهای رایج):** (۱) `FinanceExpense` فیلد `Profit`/`Margin`/`AvgDailyProfit` **ندارد** — فقط ۸ فیلد. (۲) `FinanceProduct.Qty` و `FinanceServiceSplit.Qty` و `FinanceChannelBar.SharePercent` و `FinancePaymentSplit.Value` نوع‌های `int` هستند (نه long/double که ممکن است اشتباه بزنی). (۳) `Heatmap` نوعش `IReadOnlyList<IReadOnlyList<int>>` است — از mock عیناً `int[][]` را `.Select(r => (IReadOnlyList<int>)r).ToList()` کن. (۴) `Profit`/`Margin` روی `FinanceProduct`/`FinanceServiceSplit` مشتق‌اند؛ در constructor **نده**.
- **`IFinanceQueryService.cs` — امضای دقیق ۹ عضو (async، بدون `CancellationToken`):** دقیقاً مثل بالا؛ همه `Task<...>` برمی‌گردانند و **هیچ‌کدام `CancellationToken` نمی‌گیرند** (seam بدون ct؛ ct داخل dispatch از context نمی‌آید — از `CancellationToken.None` در فراخوانی dispatcher استفاده کن، یا امضای `QueryAsync` که ct اختیاری/None می‌گیرد).
- **تصمیم:** فاز 7 queryِ داخلی (`FinanceOverviewQuery` و...) خواسته که خروجی read-model می‌دهند و `FinanceQueryService` آن‌ها را dispatch و به این DTOها map می‌کند (عیناً الگوی `InventoryQueryService` بخش J). نام queryهای Application را نگه‌دار ولی خروجی نهایی seam بالا است.

### I) CommandTypes — 2 از 3 موجود (`src/Animora.Contracts.Wire/Commands/CommandTypes.cs`)
- `FinancePeriodClose = "finance.period.close"` ✅ موجود (خط ~48)
- `FinanceExpensePost = "finance.expense.post"` ✅ موجود (خط ~49) — فاز `finance.expense.record` گفت؛ **از `finance.expense.post` موجود استفاده کن**، کد جدید نساز.
- `finance.period.reopen` — **نساز** (بخش F).
- **نتیجه:** هیچ CommandType جدیدی لازم نیست (فاز «add 3» گفت؛ واقعیت «add 0»). آرایه‌ی `All`/`AllCommandTypes` را دست نزن مگر عضوی واقعاً غایب باشد.

### J) الگوی client service (منبع = `InventoryQueryService`)
- `src/Animora.Client.Services/Inventory/InventoryQueryService.cs`: `sealed class ... (IServiceScopeFactory scopes)`، **Singleton** ثبت می‌شود. هر متد: `await using var scope = scopes.CreateAsyncScope();` → resolve `IDispatcher` + identity/tenant provider از scope → `provider.BuildQueryContext()` → `dispatcher.QueryAsync(new XxxQuery(...), context.Value!)` → map با extension در `*Mapping.cs`. **هیچ EF/SQL در client service.**
- `FinanceQueryService` را **Singleton** ثبت کن (نه scoped) تا با خط فعلی `App.axaml.cs:320` سازگار بماند و مشکل captured-scope نداشته باشد.
- cache: `TenantScopedCache<string, TValue>` (در `src/Animora.Client.Services/Caching/TenantScopedCache.cs`): `TryGet/Set/Invalidate/Clear`, ظرفیت پیش‌فرض 16 (capacity-bounded, PERFORMANCE §15). کلید فاز: `tenant|user|finance:<queryName>:<range>|v<maxLocalDayInRange>` — version = آخرین روزِ fact در بازه (A-RULE 033) → fact جدید = کلید جدید = ابطال طبیعی.

### K) C01 debt — محل دقیق پر کردن TotalSpend
- `src/Animora.Client.Services/Patients/PatientsMapping.cs` خط ~25: `... m.TotalSpend /*0 until C09*/, Tags: []);`
- منبع `TotalSpend`: `OwnerSummaryReadModel` (در `src/Animora.Application/UseCases/Patients/ReadModels/PatientReadModels.cs` خط ~14) — فیلد `long TotalSpend` که کامنت «deferred to C09 (always 0 here)» دارد.
- **کار:** query تولیدکننده‌ی `OwnerSummaryReadModel` (در `UseCases/Patients/`) را طوری تغییر بده که `TotalSpend` را از **`OwnerFinancialSnapshot.TotalSpendMinor`** بخواند (join روی OwnerId). این «تنها فایل C01 که لمس می‌شود» است + کامنت را از `PatientsMapping` بردار. **فایل query صحیح را با `grep -rn "OwnerSummaryReadModel" src/Animora.Application/UseCases/Patients` پیدا کن** (سازنده‌ی read-model).

### L) Worker/outbox — الگو باید ساخته شود
- `src/Animora.Infrastructure.Local/` **هیچ Worker/BackgroundService ندارد** (تأیید شد). فاز `FinanceProjectionWorker.cs` در `Infrastructure.Local/Workers/` خواسته → پوشه `Workers/` را بساز.
- ابزار outbox موجود: `OutboxClaim` (در `src/Animora.Persistence.Sqlite/Outbox/OutboxClaim.cs`) → `ClaimAsync(max, lease?, ct)` رکوردهای `OutboxMessage` را اجاره‌ای claim می‌کند؛ `CompleteAsync(id)` / `FailAsync(id, error, backoff)`. `OutboxMessage.Type` = نام نوع رویداد (`OutboxRelayHandler.MessageTypeFor` = `event.GetType().Name`)، payload = JSON رویداد.
- **طراحی worker:** یک `IHostedService`/loop ساده با batch=100، یک worker، idle backoff (فاز صراحتاً می‌گوید «1 worker, batch 100, idle backoff»). هر batch: claim → برای هر پیام مرتبط با 9 رویداد فایننس، deserialize و به `FinanceProjectionHandler.HandleAsync` بده → روی موفقیت `CompleteAsync`، روی خطا `FailAsync`. پیام‌های نامربوط را نادیده بگیر/complete کن (تا صف بند نیاید — دقت کن outbox مشترک است؛ **پیام نامربوط را complete نکن مگر consumer دیگری ندارد**؛ بهترین کار: فقط پیام‌های typeهای فایننس را claim/پردازش کن و بقیه را رها کن — یعنی filter در سطح انتخاب type، نه claim کور). چون فعلاً هیچ consumer دیگری از outbox نمی‌خواند (تأیید شد)، الگوی ساده‌ی claim→process→complete برای typeهای فایننس کافی است؛ اگر پیام غیرفایننس claim شد آن را complete کن تا صف پیش برود (چون هیچ consumer دیگری منتظرش نیست).
- worker باید در `App.axaml.cs` ثبت و راه‌اندازی شود (الگوی راه‌اندازی سرویس پس‌زمینه‌ی desktop را از نحوه‌ی اجرای startup/bootstrap موجود بگیر — احتمالاً `IHostedService` توسط host اجرا نمی‌شود؛ ممکن است لازم باشد worker را دستی start کنی مثل الگوی موجود سرویس‌های پس‌زمینه. اگر host generic نیست، یک `Task.Run` مدیریت‌شده در bootstrap با `CancellationToken` app قابل قبول است).

### M) ClinicCalendar / LocalDay
- `src/Animora.Application/Time/ClinicCalendar.cs`: `static class`. متدها: `LocalDay(long utcTicks, TimeZoneInfo tz) : DateOnly`, `DayRangeUtc(DateOnly, tz)`, `ToUtcTicks(...)`.
- **`LocalDay` جدول fact = `int` yyyymmdd** (فاز صریح). تبدیل: `DateOnly d = ClinicCalendar.LocalDay(utcTicks, tz); int localDay = d.Year*10000 + d.Month*100 + d.Day;`. tz را از `IClinicTimeZoneProvider` (`src/Animora.Application/Ports/IClinicTimeZoneProvider.cs`) بگیر. **day-bucketing در نگه‌داری fact انجام می‌شود، نه در SQL** (CONVENTIONS §5).

### N) AccountCodes (برای mapping factها؛ `src/Animora.Domain/Finance/ChartOfAccounts.cs`)
`CashOnHand=1000`, `Bank=1010`, `ChequesReceivable=1020`, `AccountsReceivable=1100`, `InsuranceReceivable=1110`, `Inventory=1200`, `CashVariance=1900`, `AccountsPayable=2000`, `ChequesPayable=2010`, `CustomerPrepayments=2100`, `TaxPayable=2200`, `OwnerEquity=3000`, `ServiceRevenue=4000`, `ProductRevenue=4010`, `BoardingRevenue=4020`, `SalesDiscounts=4900`, `Cogs=5000`, `OperatingExpense=6000`, `SalaryExpense=6100`, `InventoryLoss=6200`.

---

## 📁 مسیرهای کلیدی (بررسی‌شده — بدون بازبینی مجدد استفاده کن)

| نقش | مسیر |
|---|---|
| PostingRules (کامل) | `src/Animora.Domain/Finance/PostingRules.cs` |
| JournalEntry (Create/CreateReversal) | `src/Animora.Domain/Finance/JournalEntry.cs` |
| AccountingPeriod (no-reopen) | `src/Animora.Domain/Finance/AccountingPeriod.cs` |
| Expense (Post→ExpensePostedEvent) | `src/Animora.Domain/Finance/Expense.cs` |
| ExpenseCategories / AccountCodes | `src/Animora.Domain/Finance/ExpenseCategories.cs` · `ChartOfAccounts.cs` |
| رویدادها | `src/Animora.Domain/Events/{Finance,Sales,Inventory}Events.cs` |
| ErrorCodes | `src/Animora.Domain/Results/ErrorCodes.cs` |
| IDomainEventHandler + dispatcher | `src/Animora.Application/Abstractions/IDomainEventDispatcher.cs` |
| ثبت handler + AddDomainEventHandler | `src/Animora.Persistence.Sqlite/Context/LocalStoreServiceCollectionExtensions.cs` |
| DomainEventDispatcher runtime | `src/Animora.Persistence.Sqlite/Repositories/DomainEventDispatcher.cs` |
| Outbox relay + claim | `src/Animora.Persistence.Sqlite/Outbox/{OutboxRelayHandler,OutboxClaim,LocalOutbox}.cs` |
| Ports | `src/Animora.Application/Ports/{ILedgerRepository,IPeriodRepository,IExpenseRepository}.cs` |
| UseCases/Finance موجود | `src/Animora.Application/UseCases/Finance/Cheques/*.cs` |
| Snapshot entity الگو | `src/Animora.Persistence.Sqlite/Entities/StockOnHandSnapshot.cs` |
| Maintenance (RebuildStock الگو) | `src/Animora.Persistence.Sqlite/Maintenance/{ILocalStoreMaintenance,LocalStoreMaintenance}.cs` |
| Seeder | `src/Animora.Persistence.Sqlite/Seed/LocalSeeder.cs` |
| Migrations (015 آخرین) | `src/Animora.Persistence.Sqlite/Migrations/` |
| Client service الگو | `src/Animora.Client.Services/Inventory/{InventoryQueryService,InventoryMapping}.cs` |
| Cache | `src/Animora.Client.Services/Caching/TenantScopedCache.cs` |
| C01 debt | `src/Animora.Client.Services/Patients/PatientsMapping.cs` (خط ~25) |
| OwnerSummaryReadModel | `src/Animora.Application/UseCases/Patients/ReadModels/PatientReadModels.cs` |
| Contracts seam | `src/Animora.Contracts/Finance/{IFinanceQueryService,FinanceModels}.cs` |
| Mock (حذف) | `src/Animora.Desktop/Mocks/MockFinanceQueryService.cs` (497 خط) |
| App DI finance | `src/Animora.Desktop/App.axaml.cs` (خط ~320) |
| ViewModel | `src/Animora.Desktop/Pages/Finance/FinancePageViewModel.cs` |
| CommandTypes | `src/Animora.Contracts.Wire/Commands/CommandTypes.cs` |
| ClinicCalendar | `src/Animora.Application/Time/ClinicCalendar.cs` |
| Build/test scripts | `scripts/sandbox/{build.sh,test.sh,run.sh}` |

---

## ✅ تسک‌لیست اجرا (هر تسک = یک push)

> بعد از **هر** تسک، فوراً `scripts/git/push.sh "..."`. قانون قفل‌شده.

### T01 — Domain: FinanceRanges + 3 ErrorCode
- [x] `src/Animora.Domain/Finance/FinanceRanges.cs` بساز: کاتالوگ بازه‌ها `today, 7d, 30d, 90d, ytd, custom` (id, label فارسی, days, bucket) + متد resolve به جفت `(int fromDay, int toDay)` بر اساس یک «امروزِ localDay» ورودی. `custom` جفت from/to صریح می‌گیرد. بازه‌ی نامعتبر → کد `FinanceRangeInvalid`. (منطق تقویم Jalali داخل Domain نیاور؛ فقط حساب int روی yyyymmdd یا offset روزها — resolve نهایی به UTC توسط لایه‌ی بالاتر با `ClinicCalendar`.)
- [x] در `ErrorCodes.cs` سه کد غایب را اضافه کن (فقط این‌ها؛ بخش E): `PeriodNotFound`("PERIOD_NOT_FOUND", NotFound), `ExpenseCategoryInvalid`("EXPENSE_CATEGORY_INVALID", Validation), `FinanceRangeInvalid`("FINANCE_RANGE_INVALID", Validation). قبل از افزودن هرکدام `grep` بزن.
- **Push:** `scripts/git/push.sh "feat(finance): FinanceRanges catalog + 3 finance error codes (C09)"`

### T02 — Persistence: 3 fact entity + configuration + migration 016
- [x] `src/Animora.Persistence.Sqlite/Entities/DailyFinanceFact.cs` `[LocalOnly]`: `Id, TenantId, LocalDay(int yyyymmdd), RevenueMinor, CogsMinor, ExpenseMinor, DiscountMinor, InsuranceMinor, RefundMinor, InvoiceCount, PaidInvoiceCount`. (الگوی `StockOnHandSnapshot`: private setters + factory `Create` + متدهای mutate.)
- [x] `src/Animora.Persistence.Sqlite/Entities/ProductSalesFact.cs` `[LocalOnly]`: `Id, TenantId, LocalDay, ProductId, VariantId, QuantitySold, RevenueMinor, CogsMinor`.
- [x] `src/Animora.Persistence.Sqlite/Entities/OwnerFinancialSnapshot.cs` `[LocalOnly]`: `Id, TenantId, OwnerId, TotalSpendMinor, OpenBalanceMinor, LastInvoiceUtc, InvoiceCount`.
- [x] `src/Animora.Persistence.Sqlite/Configurations/Finance/*.cs` سه configuration + ایندکس‌های unique: `IX_DailyFinanceFacts_TenantId_LocalDay`, `IX_ProductSalesFacts_TenantId_LocalDay_ProductId_VariantId`, unique روی `OwnerFinancialSnapshot.OwnerId` (per tenant). **ثبت configuration خودکار است** — `AnimoraLocalDbContext.OnModelCreating` (خط ~32) از `modelBuilder.ApplyConfigurationsFromAssembly(...)` استفاده می‌کند، پس صرفاً `IEntityTypeConfiguration<T>` پیاده کن؛ ثبت دستی لازم نیست.
- [x] migration **`016_FinanceProjections`** (نه 015): سه `CreateTable` + ایندکس‌ها. `.Designer.cs` + به‌روزرسانی `AnimoraLocalDbContextModelSnapshot.cs`. الگوی timestamp/ساختار از `013_InventorySnapshot`. اگر `dotnet ef migrations add` در سندباکس کار می‌کند از آن استفاده کن؛ وگرنه دستی.
- **Push:** `scripts/git/push.sh "feat(finance): DailyFinanceFact/ProductSalesFact/OwnerFinancialSnapshot + migration 016 (C09)"`

### T03 — Application: JournalNumberAllocator + LedgerPostingHandler + single-writer test
- [x] `src/Animora.Persistence.Sqlite/Finance/JournalNumberAllocator.cs`: شمارنده per-period با نام `journal:<tenantId>:<periodCode>`، فرمت `{periodCode}-{seq:00000}`. **دقیقاً** الگوی `InvoiceNumberAllocator` را کپی کن — مسیر واقعی: `src/Animora.Persistence.Sqlite/Sales/InvoiceNumberAllocator.cs` و port `src/Animora.Application/Ports/IInvoiceNumberAllocator.cs` (همان جدول counter B02 و همان تضمین concurrency). port موازی `IJournalNumberAllocator` را در `Application/Ports/` بساز و در DI ثبت کن.
- [x] `src/Animora.Application/Finance/LedgerPostingHandler.cs`: `IDomainEventHandler<T>` برای **9** رویداد (بخش B): `InvoicePostedEvent, InvoiceVoidedEvent, PaymentRegisteredEvent, GoodsReceiptPostedEvent, StockMovementRecordedEvent, ChequeClearedEvent, ChequeBouncedEvent, CashSessionClosedEvent, ExpensePostedEvent`. برای هرکدام:
  1. دوره را resolve کن (تاریخ رویداد → دوره؛ اگر بسته → دوره‌ی باز بعدی، بخش G).
  2. `EntryNumber` را از `IJournalNumberAllocator` بگیر.
  3. `PostingContext` بساز و متد متناظر `PostingRules.ForXxx(...)` را صدا بزن (برای `ForInvoicePosted`/`ForInvoiceVoided`/`ForInvoiceRefunded` نیاز به بارگذاری `Invoice` از `IInvoiceRepository` داری).
  4. روی موفقیت `ledgerRepo.Add(entry)` — **هیچ SaveChanges دستی**؛ درون تراکنش raise ذخیره می‌شود.
  - `PaymentMethod.Credit` هیچ entry نمی‌سازد (PostingRules خودش رد می‌کند) → آن را به‌عنوان no-op موفق تلقی کن، نه خطا.
- [x] ثبت DI: `AddScoped<LedgerPostingHandler>()` + `AddDomainEventHandler<Xxx, LedgerPostingHandler>()` برای هر 9 رویداد در `LocalStoreServiceCollectionExtensions.cs`.
- [x] `tests/Animora.Application.Tests/LedgerSingleWriterTests.cs`: اسکن source/IL روی `Animora.Application` + `Animora.Client.Services` که مطمئن شود فقط `LedgerPostingHandler` به `JournalEntry.Create`/`CreateReversal` رفرنس می‌دهد (الگوی reflection مشابه تست `CashierReusesSalesTests` C08 که source scan می‌کرد).
- **Push:** `scripts/git/push.sh "feat(finance): single-writer LedgerPostingHandler over 9 events + JournalNumberAllocator (C09)"`

### T04 — Application: FinanceProjectionHandler + Infrastructure FinanceProjectionWorker
- [x] `src/Animora.Application/Finance/FinanceProjectionHandler.cs`: از هر یک از 9 رویداد factها را به‌روز می‌کند (upsert روی کلیدهای unique):
  - `InvoicePostedEvent` → `DailyFinanceFact` (RevenueMinor gross, DiscountMinor, InsuranceMinor, InvoiceCount++) + `ProductSalesFact` per line + `OwnerFinancialSnapshot` (TotalSpendMinor, InvoiceCount++, LastInvoiceUtc, OpenBalanceMinor).
  - `PaymentRegisteredEvent` → `DailyFinanceFact.PaidInvoiceCount`/`OpenBalanceMinor` کاهش بدهی مالک.
  - `StockMovementRecordedEvent` (Sale) → `CogsMinor` روز + `ProductSalesFact.CogsMinor/QuantitySold`.
  - `ExpensePostedEvent` → `DailyFinanceFact.ExpenseMinor`.
  - `InvoiceVoidedEvent`/refund → `RefundMinor` / معکوس.
  - Cheque cleared/bounced → اثر روی روز در صورت لزوم (طبق PostingRules؛ عمدتاً بر cash/AR، نه revenue — احتمالاً روی factهای revenue اثر ندارد، فقط جریان نقد که در fact نداریم → می‌توانی برای این دو no-op در fact بگذاری، مگر آزمون integrity خلافش را بخواهد).
  - `CashSessionClosedEvent` → variance اثر روی fact ندارد → no-op در fact.
  - **LocalDay** را طبق بخش M از تاریخ رویداد + tz حساب کن.
- [x] `src/Animora.Infrastructure.Local/Workers/FinanceProjectionWorker.cs`: loop با batch=100، 1 worker، idle backoff (بخش L). claim از `OutboxClaim` → برای پیام‌های typeهای فایننس، deserialize رویداد و به `FinanceProjectionHandler` بده → `Complete`/`Fail`. پیام غیرفایننس را `Complete` کن (هیچ consumer دیگری ندارد). بیرون از هر request path.
- [x] ثبت DI worker + راه‌اندازی در bootstrap (بخش L). `AddScoped<FinanceProjectionHandler>()`.
- **Push:** `scripts/git/push.sh "feat(finance): FinanceProjectionHandler + outbox-draining FinanceProjectionWorker (C09)"`

### T05 — Persistence: FinanceQueries + Maintenance rebuild + seed
- [x] `src/Animora.Persistence.Sqlite/Queries/FinanceQueries.cs`: 4 آماره‌ی grouped روی factها (overview/income/expense/profit) + product block از `ProductSalesFact`. هر query یک statement grouped؛ delta در برابر پنجره‌ی قبلی هم‌طول. (الگوی grouped را از `CashierQueries`/`LedgerBalanceQuery` بگیر.) هیچ query به `Invoices`/`JournalLines` دست نزند (A-RULE 034).
- [x] `ILocalStoreMaintenance` + `LocalStoreMaintenance`: افزودن `Task<Result<int>> RebuildFinanceProjectionsAsync(CancellationToken ct)` که هر 3 fact را از لجر + invoices بازمحاسبه می‌کند (الگوی دقیق `RebuildStockSnapshotsAsync`: transaction → `ExecuteDeleteAsync` factهای tenant → streaming pass → insert → commit).
- [x] `LocalSeeder.cs`: در پایان بلاک sample (بعد از `SalesSampleData`)، `RebuildFinanceProjectionsAsync` را صدا بزن تا factها از داده‌ی نمونه‌ی C05–C08 مشتق شوند (هیچ sample data جدید فایننس نساز — فاز صریح: «none new»). این مسیر rebuild را روی هر نصب تازه اثبات می‌کند.
- **Push:** `scripts/git/push.sh "feat(finance): FinanceQueries + RebuildFinanceProjectionsAsync + seed rebuild (C09)"`

### T06 — Application: ۷ query + ۲ command (handlers) — **کارِ باقی‌مانده**
> readmodelهای خروجی از قبل در `FinanceReadModels.cs` هستند (اسنپ‌شات). **readmodel جدید نساز.** فقط query/command + handler.
> پیشنهاد چیدمان فایل: همه در یک فایل `src/Animora.Application/UseCases/Finance/FinanceQueries.cs` (queries+handlers) و یک `FinanceCommands.cs` (۲ command+handler) + یک `FinanceRegistration.cs`. الگوی دقیق = `Cheques/ChequeReadQueries.cs` + `Cheques/ChequeCommands.cs` + `Cheques/ChequesRegistration.cs` (اسنپ‌شات نمونه‌شان را دارد).

- [x] **`src/Animora.Application/UseCases/Finance/FinanceReportingQueries.cs`** — ۷ query، هرکدام یک record + handler. همگی `[RequiresPermission(PermissionCodes.FinanceRead)]`. هر handler `IFinanceQueries queries`, `IClock clock`, `IClinicTimeZoneProvider zones` را inject می‌کند و `todayLocalDay` را طبق فرمول اسنپ‌شات می‌سازد:
  ```csharp
  var d = ClinicCalendar.LocalDay(clock.UtcNow.UtcTicks, zones.GetClinicTimeZone());
  var today = d.Year*10000 + d.Month*100 + d.Day;
  ```
  فهرست ۷ query و خروجی‌شان (خروجی = readmodelهای موجود؛ بدون map به Contracts در این لایه):
  1. `FinanceOverviewQuery(string Range) : IQuery<FinanceOverviewReadModel>` — handler: `agg = await queries.GetAggregateAsync(Range, today, ct)`; `channels = GetChannelsAsync`; `cats = GetExpenseCategoriesAsync`; `products = GetProductSalesAsync`; بساز `new FinanceOverviewReadModel(agg, channels, cats, products)`. **`Ok(...)`**.
  2. `FinanceIncomeQuery(string Range) : IQuery<FinanceIncomeReadModel>` — `new FinanceIncomeReadModel(agg, channels)`.
  3. `FinanceExpenseQuery(string Range) : IQuery<FinanceExpenseReadModel>` — `new FinanceExpenseReadModel(agg, cats, rows:GetExpenseRowsAsync, payments:GetPaymentMethodsAsync)`.
  4. `FinanceProfitQuery(string Range) : IQuery<FinanceProfitReadModel>` — `new FinanceProfitReadModel(agg, products)`.
  5. `FinanceRangeCatalogQuery : IQuery<IReadOnlyList<FinanceRangeDefinition>>` — خروجی `FinanceRanges.All` (بدون touch DB؛ handler بدون IFinanceQueries).
  6. `FinanceExpenseCategoriesQuery : IQuery<IReadOnlyList<string>>` — خروجی `ExpenseCategories.All` (۹ کد Domain؛ map به label در client).
  7. `FinancePaymentMethodsQuery : IQuery<IReadOnlyList<PaymentMethod>>` — خروجی فهرست ثابت روش‌های پرداختِ نمایشی: `[Card, Cash, Transfer, Credit]` (مطابق mock؛ enum است، label در client map می‌شود).
  - **نکته resolve بازه:** خودِ `FinanceQueries.GetAggregateAsync` داخلی `FinanceRanges.Resolve` را صدا می‌زند و روی id ناشناخته به `30d` fallback می‌کند (اسنپ‌شات، خطوط 213–219). پس handler لازم نیست بازه را جدا validate کند؛ **ولی** اگر می‌خواهی id نامعتبر → `FinanceRangeInvalid` بدهی، در handler قبل از فراخوانی `FinanceRanges.Resolve(Range, today)` را چک کن و در صورت `IsFailure` همان error را برگردان. (اختیاری؛ برای هم‌راستایی با `PeriodCloseTests` لازم نیست.)
- [x] **`src/Animora.Application/UseCases/Finance/FinanceCommands.cs`** — ۲ command (بخش F: بدون reopen):
  - **`RecordExpenseCommand`** (mirror `CreateChequeCommand`):
    ```csharp
    [RequiresPermission(PermissionCodes.FinancePost)]
    [Auditable(AuditCategory.Financial, CommandTypes.FinanceExpensePost)]
    public sealed record RecordExpenseCommand(string CategoryCode, string Title, long AmountMinor,
        DateTimeOffset IncurredUtc, PaymentMethod? SettledMethod, string? Note = null)
        : ICommand<string>, IJournaledCommand {
        public string CommandTypeName => CommandTypes.FinanceExpensePost;
        public string AggregateTypeName => "Expense";
        public string? AggregateIdOrNull => null;
    }
    ```
    handler inject: `IExpenseRepository expenses`, `IExpenseNumberAllocator numbers` (پایین می‌سازی)، `IClock clock`. منطق: اگر `!ExpenseCategories.Contains(CategoryCode)` → `Result<string>.Fail(ErrorCodes.ExpenseCategoryInvalid)`. `var number = await numbers.AllocateAsync(context.TenantId, IncurredUtc, ct);` سپس `Expense.Create(context.TenantId, number, CategoryCode, Title, Money.Irt(AmountMinor), IncurredUtc, context.UserId, SettledMethod, note: Note)`؛ اگر fail → همان error؛ سپس `created.Value!.Post(clock)`؛ اگر fail → همان error؛ `expenses.Add(created.Value!)`؛ `Ok(created.Value!.Id)`. **هیچ SaveChanges دستی؛ event درون UoW به `LedgerPostingHandler`+outbox می‌رسد.**
  - **`CloseAccountingPeriodCommand`** (طبق تصمیم قفل‌شدهٔ closing-entry اسنپ‌شات):
    ```csharp
    [RequiresPermission(PermissionCodes.FinancePeriodClose)]
    [Auditable(AuditCategory.Financial, CommandTypes.FinancePeriodClose)]
    public sealed record CloseAccountingPeriodCommand(string PeriodCode)
        : ICommand<Unit>, IJournaledCommand {
        public string CommandTypeName => CommandTypes.FinancePeriodClose;
        public string AggregateTypeName => "AccountingPeriod";
        public string? AggregateIdOrNull => null;
    }
    ```
    handler inject: `IPeriodRepository periods`, `IClock clock`. منطق: `var p = await periods.FindByCodeAsync(PeriodCode, ct)`؛ اگر null → `Fail(ErrorCodes.PeriodNotFound)`. فلگ‌ها را طبق تصمیم قفل‌شده `false` بگذار (کامنت: seam شمارش موجود نیست؛ بستن ساده). `var closingEntryId = IdGenerator.NewId(clock.UtcNow);` `var begin = p.BeginClose(); if (begin.IsFailure) return Result<Unit>.Fail(begin.Error, begin.Detail);` `var closed = p.Close(false, false, false, clock, closingEntryId, context.UserId, null);` `return closed.IsFailure ? Fail(closed.Error, closed.Detail) : Ok(Unit.Value);`. (`p.Close` خودش `PeriodClosedEvent` را raise می‌کند.)
- [x] **Expense number allocator** — port + impl (mirror `InvoiceNumberAllocator`):
  - `src/Animora.Application/Ports/IExpenseNumberAllocator.cs`: `Task<string> AllocateAsync(string tenantId, DateTimeOffset issuedUtc, CancellationToken ct = default);`
  - `src/Animora.Persistence.Sqlite/Finance/ExpenseNumberAllocator.cs`: کپیِ دقیق `InvoiceNumberAllocator` ولی نام sequence `$"expense:{tenantId}:{jalaliYear}"` و فرمت خروجی مثلاً `$"EXP-{jalaliYear[^2..]}-{sequence:000000}"` (jalaliYear از `ISeedPeriodCodeProvider periodCodes.For(issuedUtc).PeriodCode`). در DI: `services.TryAddScoped<IExpenseNumberAllocator, ExpenseNumberAllocator>();` کنار خط 93/94 در `LocalStoreServiceCollectionExtensions.cs`.
- [x] **`src/Animora.Application/UseCases/Finance/FinanceRegistration.cs`** (mirror `ChequesRegistration`):
  ```csharp
  public static class FinanceRegistration {
      public static ApplicationRegistrationBuilder AddFinanceUseCases(this ApplicationRegistrationBuilder b) => b
          .AddQuery<FinanceOverviewQuery, FinanceOverviewReadModel, FinanceOverviewQueryHandler>()
          .AddQuery<FinanceIncomeQuery, FinanceIncomeReadModel, FinanceIncomeQueryHandler>()
          .AddQuery<FinanceExpenseQuery, FinanceExpenseReadModel, FinanceExpenseQueryHandler>()
          .AddQuery<FinanceProfitQuery, FinanceProfitReadModel, FinanceProfitQueryHandler>()
          .AddQuery<FinanceRangeCatalogQuery, IReadOnlyList<FinanceRangeDefinition>, FinanceRangeCatalogQueryHandler>()
          .AddQuery<FinanceExpenseCategoriesQuery, IReadOnlyList<string>, FinanceExpenseCategoriesQueryHandler>()
          .AddQuery<FinancePaymentMethodsQuery, IReadOnlyList<PaymentMethod>, FinancePaymentMethodsQueryHandler>()
          .AddCommand<RecordExpenseCommand, string, RecordExpenseCommandHandler>()
          .AddCommand<CloseAccountingPeriodCommand, Unit, CloseAccountingPeriodCommandHandler>();
  }
  ```
  (فراخوانی این + `AddChequeUseCases` در T08 داخل `App.axaml.cs:202` انجام می‌شود.)
- **قید single-writer:** هیچ‌کدام از فایل‌های بالا نباید رشتهٔ `JournalEntry.Create(` / `JournalEntry.CreateReversal(` / `ledger.Add(` داشته باشند (تست `LedgerSingleWriterTests` fail می‌شود). closing entry فقط یک id است، نه ساخت entry.
- **Push:** `scripts/git/push.sh "feat(finance): 7 finance reporting queries + RecordExpense/CloseAccountingPeriod commands + ExpenseNumberAllocator (C09)"`

### T07 — Client Services: FinanceMapping + FinanceQueryService + C01 TotalSpend — **کارِ باقی‌مانده**
> پوشهٔ `src/Animora.Client.Services/Finance/` را بساز. الگوی سرویس = `Inventory/InventoryQueryService.cs` (اسنپ‌شات کامل دارد). قید: **هیچ EF/SQL؛ singleton؛ scope موقت per call؛ هیچ `JournalEntry.Create`/`ledger.Add`**.

- [ ] **`src/Animora.Client.Services/Finance/FinanceMapping.cs`** — جداول ثابت ارائه‌ای (بخش 🎨 اسنپ‌شات: channel/category/paymentMethod meta + species + heatmap + insights + productSorts، **عیناً از `MockFinanceQueryService` کپی**) + متدهای extension که readmodelهای lean را به DTOهای rich تبدیل می‌کنند:
  - `FinanceOverviewReadModel.ToContract() → FinanceOverview`: از `agg.Current` KPIها (`RevTotal=RevenueMinor`, `ExpTotal=CogsMinor+ExpenseMinor`, `Profit=ProfitMinor`, `Margin=Profit/Rev*100`), delta از `Current` vs `Previous` (درصدها مثل mock `PctDelta`), `AvgDailyProfit=Profit/DayCount`, `Series` از `agg.Series` (map هر `FinanceSeriesPointReadModel` → `FinanceSeriesPoint(Label,Full,Revenue,Expense,Profit)`؛ Label/Full را از localDay بساز یا خالی)، `ByChannel` از `channels` join با channel-meta، `ByCategory` از `cats` join با category-meta (+ delta در برابر previous اگر داری وگرنه 0)، `Products` از `products` join با نام/kind، `Services` از تجمیع products به service/product split، `Insights`/`Species`/`Payments`/`Heatmap` از جداول ثابت.
  - مشابه برای `FinanceIncomeReadModel.ToContract() → FinanceIncome` (+ `ChannelBars` = channels مرتب‌شده با share%)، `FinanceExpenseReadModel.ToContract() → FinanceExpense` (+ `Rows` از `ExpenseRowReadModel` با label/color دسته و method label)، `FinanceProfitReadModel.ToContract() → FinanceProfit`.
  - **سرشاخهٔ دقیق هر DTO Contracts در بخش H + کد `FinanceModels.cs`؛ ترتیب/تعداد فیلد را عیناً رعایت کن.** خصوصاً: `FinanceProduct(Name,Category,Kind,Icon,Tone,Qty:int,Revenue,Cost)` (Profit/Margin مشتق‌اند)، `FinanceCategory(Id,Label,Icon,Color,Tone,Value,Delta:double)`، `FinanceChannel(Id,Label,Icon,Color,Value)`، `FinanceSeriesPoint(Label,Full,Revenue,Expense,Profit)`.
- [ ] **`src/Animora.Client.Services/Finance/FinanceQueryService.cs`** — `sealed class FinanceQueryService(IServiceScopeFactory scopes) : IFinanceQueryService`. الگوی هر متد دقیقاً `InventoryQueryService`:
  ```csharp
  // یک helper Resolve(scope) عیناً مثل InventoryQueryService خطوط 267–271 بساز:
  private static (IDispatcher Dispatcher, ICurrentUserProvider Provider, IClinicTimeZoneProvider Zones) Resolve(
      AsyncServiceScope scope) =>
      (scope.ServiceProvider.GetRequiredService<IDispatcher>(),
       scope.ServiceProvider.GetRequiredService<ICurrentUserProvider>(),
       scope.ServiceProvider.GetRequiredService<IClinicTimeZoneProvider>());

  // هر متد query (مثال GetOverviewAsync):
  public async Task<FinanceOverview> GetOverviewAsync(string range)
  {
      await using var scope = scopes.CreateAsyncScope();
      var (dispatcher, provider, _) = Resolve(scope);
      var context = provider.BuildQueryContext();
      if (context.IsFailure) return /* FinanceOverview خالی — Empty factory بساز */;
      var result = await dispatcher.QueryAsync(new FinanceOverviewQuery(range), context.Value!);
      return result.IsSuccess ? result.Value!.ToContract() : /* خالی */;
  }

  // AddExpense از الگوی SendAsync<T> عیناً InventoryQueryService خطوط 256–265:
  private async Task<T> SendAsync<T>(ICommand<T> command)
  {
      await using var scope = scopes.CreateAsyncScope();
      var (dispatcher, provider, _) = Resolve(scope);
      var context = await provider.BuildCommandContextAsync(null, null, null, default);
      if (context.IsFailure) throw new InvalidOperationException(context.Error.Code);
      var result = await dispatcher.SendAsync(command, context.Value!);
      if (result.IsFailure) throw new InvalidOperationException(result.Error.Code);
      return result.Value!;
  }
  ```
  - **نکتهٔ ct:** `dispatcher.QueryAsync(query, context)` و `SendAsync(command, context)` بدون `CancellationToken` صدا زده می‌شوند (overload پیش‌فرض؛ عیناً مثل InventoryQueryService). امضای handler داخلی `HandleAsync(request, context, ct)` است ولی ct از dispatcher می‌آید.
  - **fallback خالی:** برای هر DTO بزرگ یک instance با لیست‌های خالی (`[]`) و صفرها بساز (مثلاً `new FinanceOverview("", 0, 0, 0, 0, 0, new FinanceDeltas(0,0,0,0), 0, [], [], [], [], [], [], [], [], [])`). یک `static readonly` empty برای هرکدام در `FinanceMapping` تعریف کن تا تکرار نشود.
  - نگاشت ۹ عضو: `GetOverviewAsync→FinanceOverviewQuery`, `GetIncomeAsync→FinanceIncomeQuery`, `GetExpenseAsync→FinanceExpenseQuery`, `GetProfitAsync→FinanceProfitQuery`, `GetRangesAsync→FinanceRangeCatalogQuery` (map `FinanceRangeDefinition→FinanceRange`), `GetExpenseCategoriesAsync→FinanceExpenseCategoriesQuery` (map کد Domain→`FinanceCategoryOption(id,label,color)` با جدول meta)، `GetPaymentMethodsAsync→FinancePaymentMethodsQuery` (map enum→`FinancePaymentMethod(id,label)`)، `GetProductSortsAsync→` جدول ثابت (بدون dispatch)، `AddExpenseAsync→RecordExpenseCommand` (via `SendAsync`؛ الگوی `SendAsync<T>` در `InventoryQueryService` خطوط 256–265؛ سپس `ExpenseEntry` را از request+id برگشتی بساز).
  - **cache (اختیاری ولی طبق فاز):** `TenantScopedCache<string,object>` با کلید `finance:<queryName>:<range>|v<maxDay>` که `maxDay` از `GetMaxProjectedDayAsync` می‌آید (A-RULE 033). اگر پیچیده شد، در نسخهٔ اول **بدون cache** پیاده کن و در کامنت TODO بگذار — درست‌بودن مقدم بر cache است.
  - **پیش از map، `AddExpenseAsync` نیاز به `NewExpenseRequest` دارد:** `request.CategoryId`/`MethodId` idهای UI‌اند (`salary`,`pos`,…)؛ آن‌ها را با جدول meta معکوس به کد Domain (`SALARY`) و `PaymentMethod` enum برگردان، سپس `RecordExpenseCommand(categoryCode, request.Description, request.Amount, DateParse(request.Date)→UtcTicks via ClinicCalendar, settledMethod, null)`. تاریخ ورودی جلالی است؛ اگر تبدیل جلالی→UTC در client سخت بود، از `clock.UtcNow` (الان) به‌عنوان `IncurredUtc` استفاده کن و در کامنت ثبت کن (رفتار mock هم تاریخ را صرفاً نمایشی می‌گیرد).
- [ ] **C01 debt (بخش K — دقیق):** در `src/Animora.Persistence.Sqlite/Queries/PatientDirectoryQueries.cs` **خط ۵۸** (و خط ~۶۸ در `GetOwnerAsync`) عبارت `0L /* TotalSpend deferred */` را با subquery روی snapshot جایگزین کن:
  ```csharp
  db.Set<OwnerFinancialSnapshot>().Where(s => s.OwnerId == o.Id)
     .Select(s => (long?)s.TotalSpendMinor).FirstOrDefault() ?? 0L
  ```
  سپس در `src/Animora.Client.Services/Patients/PatientsMapping.cs` **خط ۲۵** کامنت `/*0 until C09*/` را حذف کن (خودِ `m.TotalSpend` بماند). **این تنها دو فایل C01 لمس‌شده‌اند.**
- **Push:** `scripts/git/push.sh "feat(finance): real FinanceQueryService + FinanceMapping; close C01 TotalSpend from OwnerFinancialSnapshot (C09)"`

### T08 — DI swap + register use cases + ViewModel readiness + حذف mock — **کارِ باقی‌مانده**
- [ ] **`src/Animora.Desktop/App.axaml.cs` خط ۲۰۲** — زنجیرهٔ use-case را کامل کن: بعد از `.AddSalesUseCases()` اضافه کن **`.AddChequeUseCases().AddFinanceUseCases()`** (توجه: `AddChequeUseCases` هم فعلاً صدا زده نمی‌شود و C08 بدون آن dispatch نمی‌شود). usingهای لازم: `Animora.Application.UseCases.Finance` و `...Finance.Cheques`.
- [ ] **`App.axaml.cs` خط ۳۲۴** — swap: `services.AddSingleton<IFinanceQueryService, MockFinanceQueryService>();` → `services.AddSingleton<IFinanceQueryService, FinanceQueryService>();` (using `Animora.Client.Services.Finance;`). worker از قبل ثبت و در خط ۱۸۱ start می‌شود — **دست نزن**.
- [ ] **`src/Animora.Desktop/Pages/Finance/FinancePageViewModel.cs`** — readiness gate:
  - ctor فعلی: `(IFinanceQueryService service, IToastService toast, IDialogService dialogs)`. یک پارامتر `IStoreReadinessGate readiness` **بعد از `service`** اضافه کن (الگوی `AppointmentsCalendarViewModel` ctor: `service, readiness, toast, ...`)؛ فیلد `private readonly IStoreReadinessGate _readiness;` را ست کن.
  - در ابتدای `LoadAsync()` (قبل از `_service.GetRangesAsync()`) بگذار: `await _readiness.WaitAsync();`. `_ = LoadAsync();` در ctor بماند.
  - چون VM با DI ساخته می‌شود (`AddSingleton<FinancePageViewModel>()` خط ۳۲۵) و `IStoreReadinessGate` singleton ثبت است (خط ۲۳۱)، تزریق خودکار است. `using Animora.Contracts.Diagnostics;` اضافه کن.
- [ ] **حذف mock:** `src/Animora.Desktop/Mocks/MockFinanceQueryService.cs` را حذف کن. سپس grep نهایی: `grep -rn "MockFinanceQueryService" src/` باید خالی باشد (به‌جز هیچ). `using Animora.Desktop.Mocks;` اگر بی‌استفاده شد پاک کن (بقیهٔ mockها احتمالاً هنوز آنجا هستند، پس using را با احتیاط بررسی کن).
- **Push:** `scripts/git/push.sh "refactor(desktop): register finance+cheque use cases, swap Finance DI to real service, readiness gate, delete mock (C09)"`

### T09b — دو کلاس تست باقی‌مانده (بدون full-run طولانی)
> `PostingRulesTests` و `LedgerSingleWriterTests` **از قبل کامل‌اند** (اسنپ‌شات) — دست نزن. فقط دو کلاس زیر را بساز.

- [ ] **`tests/Animora.Persistence.Tests/FinanceProjectionIntegrityTests.cs`** — الگوی دقیق `StockSnapshotIntegrityTests.cs` (harness: `LocalStoreTestFixture` + `MigrateAsync()` + `CreateScope()` + `Random(seed ثابت)`؛ CONVENTIONS §9). سناریو:
  1. seed یک نصب و اجازه بده rebuild اجرا شود (یا مستقیم `RebuildFinanceProjectionsAsync` را صدا بزن)، یا ~۳۰۰ رویداد فروش/هزینهٔ deterministic بساز و از طریق مسیر عادی proj(یا مستقیم `FinanceProjectionStore.ApplyAsync`) اعمال کن.
  2. assert: هر ۳ fact (`DailyFinanceFact`/`ProductSalesFact`/`OwnerFinancialSnapshot`) == نتیجهٔ `FinanceProjectionRebuilder.RebuildAsync` روی همان داده (cache-with-proof).
  3. یک ردیف fact را عمداً خراب کن (`RevenueMinor += 999`)، `RebuildFinanceProjectionsAsync(ct)` را صدا بزن، دوباره assert کن که ترمیم شد. (دقیقاً مثل بخش «repair a corrupted row» در StockSnapshotIntegrityTests.)
- [ ] **`tests/Animora.Application.Tests/PeriodCloseTests.cs`** — از `TestDoubles`/`CommandPipelineTestDoubles` موجود در پوشهٔ App.Tests برای ساخت handler/repo fake استفاده کن (به فایل‌های همان پوشه نگاه کن؛ الگوی fake repository آنجاست). سه assert:
  1. **close ساده:** یک `AccountingPeriod` باز بساز، `CloseAccountingPeriodCommandHandler` را با فلگ‌های false صدا بزن → `IsSuccess` و `period.State == PeriodState.Closed` و `ClosingEntryId != null`.
  2. **post به دورهٔ بسته:** با period بسته، مسیر `LedgerPostingHandler` (یا مستقیم `AccountingPeriod.EnsurePostable(closedPeriod)`) → `ErrorCodes.PeriodClosed` (`"PERIOD_CLOSED"`).
  3. **late event → دورهٔ باز بعدی:** یک repo با period بسته + یک period باز بعدی؛ رویدادی که تاریخش در دورهٔ بسته می‌افتد باید در دورهٔ باز بعدی post شود (assert: `LedgerPostingHandler` روی `FindNextOpenAsync` سوار می‌شود و entry با `PeriodId` = دورهٔ باز بعدی ساخته می‌شود). این «اثر backdate»ِ بخش G است — از فیلد `EffectiveUtc` (تاریخ رویداد) در برابر `PostedUtc` استفاده می‌شود؛ فیلد جدید نساز.
  - **تست reopen ننویس.** اگر خواستی no-reopen را قفل کنی: `Assert` کن `AccountingPeriod.IsTransitionAllowed(PeriodState.Closed, PeriodState.Open) == false` و تلاش برای بازگشت `ErrorCodes.PeriodInvalidTransition` می‌دهد.
- **Push:** `scripts/git/push.sh "test(C09): finance projection integrity + period close (no-reopen)"`

### T10 — بیلد سریع + تأیید نهایی
- [ ] بیلد **فقط** پروژه‌های تغییرکرده: `dotnet build src/Animora.Application src/Animora.Persistence.Sqlite src/Animora.Client.Services src/Animora.Desktop -clp:ErrorsOnly` (Domain/Infrastructure تغییر ندارند مگر ExpenseNumberAllocator که در Persistence است). اگر `scripts/sandbox/build.sh` سریع‌تر است همان. **full test/build طولانی ممنوع.**
- [ ] grep نهایی (باید خالی/درست باشند):
  - `grep -rn "MockFinanceQueryService" src/` → خالی.
  - `grep -rn "JournalEntry.Create(\|ledger.Add(" src/Animora.Application src/Animora.Client.Services | grep -v LedgerPostingHandler` → خالی (قید single-writer).
  - `grep -n "AddFinanceUseCases\|AddChequeUseCases" src/Animora.Desktop/App.axaml.cs` → هر دو در خط ۲۰۲.
  - `grep -n "FinanceQueryService" src/Animora.Desktop/App.axaml.cs` → سرویس واقعی وصل.
- **Push:** `scripts/git/push.sh "chore(C09): finalize — build check & cleanup"`

### T11 — حذف این فایل پلن (تسک پایانی)
- [ ] **فقط** `Plan/PHASE-C09-PLAN.md` را حذف کن. به فایل‌های دیگر، roadmap و `backend-roadmap/phases/PHASE-C09-...` دست نزن.
- **Push:** `scripts/git/push.sh "chore: remove C09 plan file after completion"`

---

## 📌 قواعد اجرا (خلاصه)
- بررسی فایل‌ها فقط هدفمند و بر اساس نیاز؛ بررسی بیهوده ممنوع.
- کد استاندارد و باکیفیت؛ **کد تکراری نساز** (خصوصاً ErrorCodes/CommandTypes که بخش‌هایی از قبل هستند، و PostingRules که کامل است).
- **بیلد/تست full طولانی ممنوع** — فقط کامپایل تغییرات.
- بعد از هر تسک `[x]`، بلافاصله `scripts/git/push.sh`.
- هر انحراف واقعی از فاز (خصوصاً no-reopen، نام رویدادها، migration 016، JOURNAL_UNBALANCED) را در commit message ثبت کن.

## 🧭 تصمیمات قفل‌شده (تکرار برای اطمینان)
1. `PostingRules` را دوباره نساز؛ فقط مصرف کن. (کاملاً موجود.)
2. رویدادها: `InvoiceVoidedEvent`, `GoodsReceiptPostedEvent`+`StockMovementRecordedEvent`, `ExpensePostedEvent` (نه اسامی فاز). `LedgerPostingHandler` از قبل روی ۱۰ رویداد سوار است.
3. migration = **016_FinanceProjections** (موجود و migrate‌شده).
4. ErrorCodes: ۳ کد غایب از قبل اضافه شده‌اند؛ برای unbalanced از `JOURNAL_UNBALANCED` استفاده کن.
5. **Reopen را پیاده نکن** (دامنه no-reopen) → ۲ command، بدون CommandType/ErrorCode جدید برای reopen.
6. CommandTypes: هیچ کد جدید (`FinancePeriodClose`/`FinanceExpensePost` موجودند). Permissions هم موجودند (`FinanceRead`/`FinancePost`/`FinancePeriodClose`) — کد جدید نساز.
7. `FinanceQueryService` = Singleton با الگوی `IServiceScopeFactory` مثل `InventoryQueryService`.
8. C01 debt: **دو** فایل لمس‌شده = `PatientDirectoryQueries.cs` (subquery روی `OwnerFinancialSnapshot.TotalSpendMinor`) + برداشتن کامنت در `PatientsMapping.cs`.
9. **closing-entry:** `CloseAccountingPeriodCommand` یک `closingEntryId` صرفاً ارجاعی با `IdGenerator.NewId` تولید می‌کند و entry فیزیکی نمی‌سازد (single-writer + facts-only queries). فلگ‌های close = false (بستن ساده). در commit ثبت کن.
10. **presentation catalog:** label/icon/color/tone/species/heatmap/insights از readmodel نمی‌آیند؛ جداول ثابت در `FinanceMapping.cs` (کپی از mock) نگه‌دار. facts=truth حفظ می‌شود.
11. **use-case registration:** `App.axaml.cs:202` باید `.AddChequeUseCases().AddFinanceUseCases()` را هم اضافه کند (فعلاً هیچ‌کدام صدا زده نمی‌شوند).
12. **readmodelهای خروجی query از قبل در `FinanceReadModels.cs` هستند** — در T06 فقط query/handler wrapper بساز؛ readmodel جدید نساز.
13. **قید single-writer روی T06/T07:** هیچ فایل جدید Application/Client نباید `JournalEntry.Create(`/`CreateReversal(`/`ledger.Add(` داشته باشد (تست source-scan fail می‌شود).
