# Plan — Phase C09: Finance & reporting read models

> **هدف این فایل:** تسک‌لیست خودکفا و دقیق برای اجرای فاز C09. تمام یافته‌های بررسی ریپو (state واقعی کد، انحرافات فاز از واقعیت، الگوها، مسیرها، امضاها) اینجا **خالص‌سازی** شده تا مدل در سشن جدید **بدون بررسی مجدد** مستقیم پیاده‌سازی را شروع کند.
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
- DTOها در `src/Animora.Contracts/Finance/FinanceModels.cs`: `FinanceRange(Id,Label,Days,Bucket)`, `FinanceOverview(...)`, `FinanceIncome(...)`, `FinanceExpense(...)`, `FinanceProfit(...)`, `FinanceDeltas(Revenue,Expense,Profit,Margin)`, `FinanceSeriesPoint(...)`, `FinanceChannel(...)`, `FinanceCategory(...)`, `FinanceProduct(...)`, `FinanceProductSort(Id,Label)`, `FinanceCategoryOption(Id,Label,Color)`, `FinancePaymentMethod(Id,Label)`, `FinanceExpenseRow(...)`, `NewExpenseRequest(...)`, `ExpenseEntry(...)`. **قبل از map کردن، سرشاخه‌های دقیق این recordها را با یک بار خواندن `FinanceModels.cs` تأیید کن** (تعداد و ترتیب فیلدها).
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
- [ ] `src/Animora.Domain/Finance/FinanceRanges.cs` بساز: کاتالوگ بازه‌ها `today, 7d, 30d, 90d, ytd, custom` (id, label فارسی, days, bucket) + متد resolve به جفت `(int fromDay, int toDay)` بر اساس یک «امروزِ localDay» ورودی. `custom` جفت from/to صریح می‌گیرد. بازه‌ی نامعتبر → کد `FinanceRangeInvalid`. (منطق تقویم Jalali داخل Domain نیاور؛ فقط حساب int روی yyyymmdd یا offset روزها — resolve نهایی به UTC توسط لایه‌ی بالاتر با `ClinicCalendar`.)
- [ ] در `ErrorCodes.cs` سه کد غایب را اضافه کن (فقط این‌ها؛ بخش E): `PeriodNotFound`("PERIOD_NOT_FOUND", NotFound), `ExpenseCategoryInvalid`("EXPENSE_CATEGORY_INVALID", Validation), `FinanceRangeInvalid`("FINANCE_RANGE_INVALID", Validation). قبل از افزودن هرکدام `grep` بزن.
- **Push:** `scripts/git/push.sh "feat(finance): FinanceRanges catalog + 3 finance error codes (C09)"`

### T02 — Persistence: 3 fact entity + configuration + migration 016
- [ ] `src/Animora.Persistence.Sqlite/Entities/DailyFinanceFact.cs` `[LocalOnly]`: `Id, TenantId, LocalDay(int yyyymmdd), RevenueMinor, CogsMinor, ExpenseMinor, DiscountMinor, InsuranceMinor, RefundMinor, InvoiceCount, PaidInvoiceCount`. (الگوی `StockOnHandSnapshot`: private setters + factory `Create` + متدهای mutate.)
- [ ] `src/Animora.Persistence.Sqlite/Entities/ProductSalesFact.cs` `[LocalOnly]`: `Id, TenantId, LocalDay, ProductId, VariantId, QuantitySold, RevenueMinor, CogsMinor`.
- [ ] `src/Animora.Persistence.Sqlite/Entities/OwnerFinancialSnapshot.cs` `[LocalOnly]`: `Id, TenantId, OwnerId, TotalSpendMinor, OpenBalanceMinor, LastInvoiceUtc, InvoiceCount`.
- [ ] `src/Animora.Persistence.Sqlite/Configurations/Finance/*.cs` سه configuration + ایندکس‌های unique: `IX_DailyFinanceFacts_TenantId_LocalDay`, `IX_ProductSalesFacts_TenantId_LocalDay_ProductId_VariantId`, unique روی `OwnerFinancialSnapshot.OwnerId` (per tenant). **ثبت configuration خودکار است** — `AnimoraLocalDbContext.OnModelCreating` (خط ~32) از `modelBuilder.ApplyConfigurationsFromAssembly(...)` استفاده می‌کند، پس صرفاً `IEntityTypeConfiguration<T>` پیاده کن؛ ثبت دستی لازم نیست.
- [ ] migration **`016_FinanceProjections`** (نه 015): سه `CreateTable` + ایندکس‌ها. `.Designer.cs` + به‌روزرسانی `AnimoraLocalDbContextModelSnapshot.cs`. الگوی timestamp/ساختار از `013_InventorySnapshot`. اگر `dotnet ef migrations add` در سندباکس کار می‌کند از آن استفاده کن؛ وگرنه دستی.
- **Push:** `scripts/git/push.sh "feat(finance): DailyFinanceFact/ProductSalesFact/OwnerFinancialSnapshot + migration 016 (C09)"`

### T03 — Application: JournalNumberAllocator + LedgerPostingHandler + single-writer test
- [ ] `src/Animora.Persistence.Sqlite/Finance/JournalNumberAllocator.cs`: شمارنده per-period با نام `journal:<tenantId>:<periodCode>`، فرمت `{periodCode}-{seq:00000}`. **دقیقاً** الگوی `InvoiceNumberAllocator` را کپی کن — مسیر واقعی: `src/Animora.Persistence.Sqlite/Sales/InvoiceNumberAllocator.cs` و port `src/Animora.Application/Ports/IInvoiceNumberAllocator.cs` (همان جدول counter B02 و همان تضمین concurrency). port موازی `IJournalNumberAllocator` را در `Application/Ports/` بساز و در DI ثبت کن.
- [ ] `src/Animora.Application/Finance/LedgerPostingHandler.cs`: `IDomainEventHandler<T>` برای **9** رویداد (بخش B): `InvoicePostedEvent, InvoiceVoidedEvent, PaymentRegisteredEvent, GoodsReceiptPostedEvent, StockMovementRecordedEvent, ChequeClearedEvent, ChequeBouncedEvent, CashSessionClosedEvent, ExpensePostedEvent`. برای هرکدام:
  1. دوره را resolve کن (تاریخ رویداد → دوره؛ اگر بسته → دوره‌ی باز بعدی، بخش G).
  2. `EntryNumber` را از `IJournalNumberAllocator` بگیر.
  3. `PostingContext` بساز و متد متناظر `PostingRules.ForXxx(...)` را صدا بزن (برای `ForInvoicePosted`/`ForInvoiceVoided`/`ForInvoiceRefunded` نیاز به بارگذاری `Invoice` از `IInvoiceRepository` داری).
  4. روی موفقیت `ledgerRepo.Add(entry)` — **هیچ SaveChanges دستی**؛ درون تراکنش raise ذخیره می‌شود.
  - `PaymentMethod.Credit` هیچ entry نمی‌سازد (PostingRules خودش رد می‌کند) → آن را به‌عنوان no-op موفق تلقی کن، نه خطا.
- [ ] ثبت DI: `AddScoped<LedgerPostingHandler>()` + `AddDomainEventHandler<Xxx, LedgerPostingHandler>()` برای هر 9 رویداد در `LocalStoreServiceCollectionExtensions.cs`.
- [ ] `tests/Animora.Application.Tests/LedgerSingleWriterTests.cs`: اسکن source/IL روی `Animora.Application` + `Animora.Client.Services` که مطمئن شود فقط `LedgerPostingHandler` به `JournalEntry.Create`/`CreateReversal` رفرنس می‌دهد (الگوی reflection مشابه تست `CashierReusesSalesTests` C08 که source scan می‌کرد).
- **Push:** `scripts/git/push.sh "feat(finance): single-writer LedgerPostingHandler over 9 events + JournalNumberAllocator (C09)"`

### T04 — Application: FinanceProjectionHandler + Infrastructure FinanceProjectionWorker
- [ ] `src/Animora.Application/Finance/FinanceProjectionHandler.cs`: از هر یک از 9 رویداد factها را به‌روز می‌کند (upsert روی کلیدهای unique):
  - `InvoicePostedEvent` → `DailyFinanceFact` (RevenueMinor gross, DiscountMinor, InsuranceMinor, InvoiceCount++) + `ProductSalesFact` per line + `OwnerFinancialSnapshot` (TotalSpendMinor, InvoiceCount++, LastInvoiceUtc, OpenBalanceMinor).
  - `PaymentRegisteredEvent` → `DailyFinanceFact.PaidInvoiceCount`/`OpenBalanceMinor` کاهش بدهی مالک.
  - `StockMovementRecordedEvent` (Sale) → `CogsMinor` روز + `ProductSalesFact.CogsMinor/QuantitySold`.
  - `ExpensePostedEvent` → `DailyFinanceFact.ExpenseMinor`.
  - `InvoiceVoidedEvent`/refund → `RefundMinor` / معکوس.
  - Cheque cleared/bounced → اثر روی روز در صورت لزوم (طبق PostingRules؛ عمدتاً بر cash/AR، نه revenue — احتمالاً روی factهای revenue اثر ندارد، فقط جریان نقد که در fact نداریم → می‌توانی برای این دو no-op در fact بگذاری، مگر آزمون integrity خلافش را بخواهد).
  - `CashSessionClosedEvent` → variance اثر روی fact ندارد → no-op در fact.
  - **LocalDay** را طبق بخش M از تاریخ رویداد + tz حساب کن.
- [ ] `src/Animora.Infrastructure.Local/Workers/FinanceProjectionWorker.cs`: loop با batch=100، 1 worker، idle backoff (بخش L). claim از `OutboxClaim` → برای پیام‌های typeهای فایننس، deserialize رویداد و به `FinanceProjectionHandler` بده → `Complete`/`Fail`. پیام غیرفایننس را `Complete` کن (هیچ consumer دیگری ندارد). بیرون از هر request path.
- [ ] ثبت DI worker + راه‌اندازی در bootstrap (بخش L). `AddScoped<FinanceProjectionHandler>()`.
- **Push:** `scripts/git/push.sh "feat(finance): FinanceProjectionHandler + outbox-draining FinanceProjectionWorker (C09)"`

### T05 — Persistence: FinanceQueries + Maintenance rebuild + seed
- [ ] `src/Animora.Persistence.Sqlite/Queries/FinanceQueries.cs`: 4 آماره‌ی grouped روی factها (overview/income/expense/profit) + product block از `ProductSalesFact`. هر query یک statement grouped؛ delta در برابر پنجره‌ی قبلی هم‌طول. (الگوی grouped را از `CashierQueries`/`LedgerBalanceQuery` بگیر.) هیچ query به `Invoices`/`JournalLines` دست نزند (A-RULE 034).
- [ ] `ILocalStoreMaintenance` + `LocalStoreMaintenance`: افزودن `Task<Result<int>> RebuildFinanceProjectionsAsync(CancellationToken ct)` که هر 3 fact را از لجر + invoices بازمحاسبه می‌کند (الگوی دقیق `RebuildStockSnapshotsAsync`: transaction → `ExecuteDeleteAsync` factهای tenant → streaming pass → insert → commit).
- [ ] `LocalSeeder.cs`: در پایان بلاک sample (بعد از `SalesSampleData`)، `RebuildFinanceProjectionsAsync` را صدا بزن تا factها از داده‌ی نمونه‌ی C05–C08 مشتق شوند (هیچ sample data جدید فایننس نساز — فاز صریح: «none new»). این مسیر rebuild را روی هر نصب تازه اثبات می‌کند.
- **Push:** `scripts/git/push.sh "feat(finance): FinanceQueries + RebuildFinanceProjectionsAsync + seed rebuild (C09)"`

### T06 — Application: 7 query + 2 command (+ handlers/validators) + read models
- [ ] `src/Animora.Application/UseCases/Finance/ReadModels/*.cs`: 5 read model (خروجی 7 query — overview/income/expense/profit + range catalog + product sorts + categories/payment-methods طبق نیاز seam بخش H).
- [ ] `src/Animora.Application/UseCases/Finance/*.cs`:
  - queryها: `FinanceRangeCatalogQuery`, `FinanceOverviewQuery(string Range)`, `FinanceIncomeQuery(string Range)`, `FinanceExpenseQuery(string Range)`, `FinanceProfitQuery(string Range)`, `ExpenseCategoriesQuery`, `PaymentMethodsQuery` — هرکدام `IQuery<...>` + handler که `FinanceQueries` را می‌خواند. بازه با `FinanceRanges` resolve و در صورت نامعتبر `FinanceRangeInvalid`.
  - commandها (فقط 2 — بخش F): `RecordExpenseCommand(category, Money amount, occurredUtc, PaymentMethod, note)` → `[RequiresPermission("finance.post")]` (permission موجود؛ `finance.expense.record` وجود ندارد — از `FinancePost` استفاده کن یا اگر تست کد دقیق خواست بررسی کن) + `[Auditable(AuditCategory.Financial, "finance.expense.post")]`؛ `Expense.Create`→`Post`→raise `ExpensePostedEvent`؛ category نامعتبر → `ExpenseCategoryInvalid`. `CloseAccountingPeriodCommand(string PeriodCode)` → `[RequiresPermission("finance.period.close")]`؛ validate settle/ack همه‌ی source docها، `BeginClose`+`Close` (raise `PeriodClosedEvent`)؛ دوره یافت نشد → `PeriodNotFound`.
- **Push:** `scripts/git/push.sh "feat(finance): 7 finance queries + RecordExpense/CloseAccountingPeriod commands (C09)"`

### T07 — Client Services: FinanceMapping + FinanceQueryService + C01 TotalSpend
- [ ] `src/Animora.Client.Services/Finance/FinanceMapping.cs`: read model ↔ Contracts DTO (بخش H؛ سرشاخه‌های `FinanceModels.cs` را یک بار تأیید کن).
- [ ] `src/Animora.Client.Services/Finance/FinanceQueryService.cs`: پیاده‌سازی 9 عضو `IFinanceQueryService` با الگوی `InventoryQueryService` (بخش J): `IServiceScopeFactory` + `CreateAsyncScope` + `dispatcher.QueryAsync`/`SendAsync`. cache با کلید `tenant|user|finance:<queryName>:<range>|v<maxLocalDayInRange>`. `AddExpenseAsync` → `RecordExpenseCommand`.
- [ ] C01 debt (بخش K): query سازنده‌ی `OwnerSummaryReadModel` را طوری تغییر بده که `TotalSpend` از `OwnerFinancialSnapshot.TotalSpendMinor` بیاید؛ کامنت `/*0 until C09*/` را از `PatientsMapping.cs` بردار.
- **Push:** `scripts/git/push.sh "feat(finance): real FinanceQueryService + FinanceMapping; close C01 TotalSpend (C09)"`

### T08 — DI swap + ViewModel readiness + حذف mock
- [ ] `App.axaml.cs` خط ~320: `AddSingleton<IFinanceQueryService, MockFinanceQueryService>()` → `FinanceQueryService`؛ using namespace را اصلاح کن؛ worker را ثبت/راه‌اندازی کن (بخش L).
- [ ] `FinancePageViewModel.cs`: خط readiness-gate اضافه کن (الگوی `InventoryPageViewModel`/C08 ViewModels: field `IStoreReadinessGate` + ctor inject + `await _readiness.WaitAsync()` ابتدای load). نام دقیق gate را با grep تأیید کن.
- [ ] حذف `src/Animora.Desktop/Mocks/MockFinanceQueryService.cs`؛ مطمئن شو هیچ DI/using به آن رفرنس نمی‌دهد (grep نهایی).
- **Push:** `scripts/git/push.sh "refactor(desktop): swap Finance DI to real service, readiness gate, delete mock (C09)"`

### T09 — تست‌ها (4 کلاس؛ بدون full-run طولانی)
- [ ] `tests/Animora.Domain.Tests/PostingRulesTests.cs`: هر یک از 9 رویداد → entry متوازن با کدهای حساب و علامت درست؛ ساخت نامتوازن → `JOURNAL_UNBALANCED` (بخش E؛ نه LEDGER_UNBALANCED).
- [ ] `tests/Animora.Application.Tests/LedgerSingleWriterTests.cs`: (اگر در T03 ساخته شد اینجا فقط تأیید/تکمیل). فقط `LedgerPostingHandler` سازنده‌ی `JournalEntry` است.
- [ ] `tests/Animora.Persistence.Tests/FinanceProjectionIntegrityTests.cs`: بعد از ~300 رویداد تصادفی، 3 fact == بازمحاسبه؛ `RebuildFinanceProjectionsAsync` یک fact عمداً خراب را ترمیم می‌کند (الگوی C06 stock snapshot integrity).
- [ ] `tests/Animora.Application.Tests/PeriodCloseTests.cs`: post به دوره‌ی بسته → `PERIOD_CLOSED`؛ late event در دوره‌ی باز بعدی می‌افتد (اثر backdate، بخش G). **تست reopen را ننویس** (reopen پیاده نمی‌شود — بخش F؛ اگر اسکلت تست فاز آن را می‌طلبید، به‌جای reopen، «تلاش برای بازگشت به Open رد می‌شود» را assert کن با `PeriodInvalidTransition`).
- **Push:** `scripts/git/push.sh "test(C09): posting rules, single-writer, projection integrity, period close"`

### T10 — بیلد سریع + تأیید نهایی
- [ ] بیلد **فقط** پروژه‌های تغییرکرده (نه full طولانی): `dotnet build src/Animora.Domain src/Animora.Application src/Animora.Persistence.Sqlite src/Animora.Infrastructure.Local src/Animora.Client.Services src/Animora.Desktop -clp:ErrorsOnly` یا اگر `scripts/sandbox/build.sh` سریع است همان. **full test/build طولانی ممنوع** — فقط کامپایل‌شدن تغییرات را تأیید کن.
- [ ] grep نهایی: mock حذف، DI درست، هیچ رفرنس به `MockFinanceQueryService`، فقط `LedgerPostingHandler` به `JournalEntry.Create`.
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
1. `PostingRules` را دوباره نساز؛ فقط مصرف کن.
2. رویدادها: `InvoiceVoidedEvent`, `GoodsReceiptPostedEvent`+`StockMovementRecordedEvent`, `ExpensePostedEvent` (نه اسامی فاز).
3. migration = **016_FinanceProjections**.
4. ErrorCodes: فقط 3 کد غایب را اضافه کن؛ برای unbalanced از `JOURNAL_UNBALANCED` موجود استفاده کن.
5. **Reopen را پیاده نکن** (دامنه no-reopen است) → 2 command، بدون CommandType/ErrorCode جدید برای reopen.
6. CommandTypes: هیچ کد جدید (هر دو موجودند).
7. `FinanceQueryService` = Singleton با الگوی `IServiceScopeFactory` مثل `InventoryQueryService`.
8. C01 debt: تنها فایل C01 لمس‌شده = query سازنده‌ی `OwnerSummaryReadModel` + برداشتن کامنت در `PatientsMapping`.
