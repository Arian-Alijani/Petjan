# Plan — Phase C08: Cashier & Cheques

> **هدف این فایل:** یک تسک‌لیست خودکفا و دقیق برای اجرای فاز C08. تمام یافته‌های بررسی ریپو (state واقعی کد، انحرافات فاز از واقعیت، الگوها، مسیرها) اینجا خالص‌سازی شده تا مدل در سشن جدید **بدون بررسی مجدد** مستقیم پیاده‌سازی را شروع کند.
>
> **منبع فاز:** `backend-roadmap/phases/PHASE-C08-cashier-cheques.md`
> **Band:** M · **Track:** C · **Depends:** C07 · **Satisfies:** R-B01, R-B02, R-B16

---

## 🔒 قانون پوش (قفل‌شده — حیاتی)

بعد از **تکمیل هر تسک** این دستور اجرا شود (وگرنه شکست پروژه):

```bash
scripts/git/push.sh "type(scope): پیام"
```

- اسکریپت: `add -A` → `commit` → `fetch`+`rebase --autostash` → `push -u`. یک خط `✓`/`✗` چاپ می‌کند.
- Exit codes: `0` ok · `2` rebase conflict (رفع کن و دوباره اجرا) · `3` بدون credential → ابزار `setup_github_environment` را یک‌بار صدا بزن سپس دوباره اجرا کن.
- برنچ کاری فعلی: `main` (remote `origin` = `github.com/Arian-Alijani/Petjan.git`). اگر لازم شد برنچ `genspark_ai_developer` با `-b` ساخته شود.

---

## 🎯 درک فاز (خلاصه معنایی)

دو صفحه:
1. **Cashier** — یک **جلسه صندوق (cash session)** واقعی مدیریت می‌کند (باز/بسته با شمارش کشو و ثبت مغایرت variance)، و فاکتورها را از طریق **کامندهای C07 (بازاستفاده، بدون کد دوم)** تسویه می‌کند.
2. **Cheques** — چرخه‌ی کامل عمر چک، توزیع بانکی، پیش‌بینی جریان نقدی 8 هفته‌ای و هشدارها. رویدادهای clear/bounce به لجر C09 می‌رسند (این فاز فقط event راه می‌اندازد، journal نمی‌نویسد).

**اصل طلایی:** `CashierService` منطق فروش را کپی نمی‌کند؛ **verbatim** کامندهای C07 را dispatch می‌کند. تستی وجود دارد که مطمئن شود زیر `Animora.Client.Services/Cashier/**` هیچ `IUnitOfWork`/repository/`AnimoraLocalDbContext` نیست.

---

## ⚠️ انحرافات کشف‌شده فاز از واقعیت ریپو (بسیار مهم — قبل از شروع بخوان)

متن فاز در چند جا با state واقعی ریپو نمی‌خواند. **این‌ها قطعی و بررسی‌شده‌اند:**

### A) دامنه CashSession/CashMovement از قبل وجود دارد — با API متفاوت از فاز
- مسیر واقعی: **`src/Animora.Domain/Cashier/CashSession.cs`** و **`src/Animora.Domain/Cashier/CashMovement.cs`** (فاز اشتباهاً `Domain/Sales/` گفته).
- namespace: `Animora.Domain.Cashier`.
- **`CashSessionState`** واقعی: `Open=0, Closing=1, Closed=2, Reconciled=3` (نه فقط Open/Closed). State machine اکیداً forward: `Open → Closing → Closed → Reconciled`.
- **`CashMovementKind`** واقعی: `SalePayment=0, RefundOut=1, PettyCashOut=2, FloatIn=3, BankDropOut=4, CorrectionIn=5, CorrectionOut=6` — **نه** `PayIn/PayOut` که فاز گفته. برای payout/top-up از `PettyCashOut`/`FloatIn` (یا `BankDropOut`) استفاده کن.
- بستن جلسه **دو مرحله‌ای** است در دامنه: `BeginClose(declared, note, clock, hasUnsettled)` → state = `Closing`؛ سپس `Close(clock)` → state = `Closed` و `CashSessionClosedEvent` راه می‌افتد. متد `CloseCashSessionCommand` باید هر دو را پشت‌سرهم صدا بزند.
- `CashSession.Open(tenantId, sessionNumber, cashierUserId, openingFloat, openedUtc, terminalCode?)` → `sessionNumber` باید توسط Application layer اختصاص یابد (شمارنده per-tenant). `CashSessionOpenedEvent` راه می‌افتد.
- Guard تکراری‌نبودن: `CashSession.EnsureNoOpenSession(bool anyOpenExists)` → اگر true باشد `CASH_SESSION_ALREADY_OPEN`. کالر باید ابتدا query کند آیا session باز برای `(TenantId, CashierUserId, TerminalCode)` هست.
- `AddMovement(CashMovement)` فقط در state=Open مجاز؛ وگرنه `CASH_SESSION_CLOSED`.
- `CashMovement.Create(kind, amount, reason, occurredUtc, recordedByUserId, invoiceId?, paymentId?, operationId?)` — sign rule را خودش enforce می‌کند (`MOVEMENT_SIGN_INVALID`): inbound kinds (SalePayment/FloatIn/CorrectionIn) مثبت، بقیه منفی، صفر ممنوع.
- منطق variance در دامنه است: `ExpectedClosing = OpeningFloat + Σ movements`, `Variance = DeclaredClosing − ExpectedClosing`. variance غیرصفر بدون note → `CASH_VARIANCE_NOTE_REQUIRED`.

### B) `Payment.CashSessionId` از قبل وجود دارد (domain + persistence)
- `src/Animora.Domain/Sales/Payment.cs` خط 88: `public string? CashSessionId { get; private init; }` و `Payment.Create(... cashSessionId ...)` خط ~141.
- **منطق CASH_SESSION_REQUIRED از قبل هست** (خط ~160): پرداخت `PaymentMethod.Cash` بدون `cashSessionId` → `ErrorCodes.PaymentMethodReferenceRequired` (نه یک کد جدید جدا). فاز خواسته کد `CASH_SESSION_REQUIRED` جدا باشد؛ اما دامنه از `PAYMENT_METHOD_REFERENCE_REQUIRED` استفاده می‌کند. **تصمیم: کد موجود را نگه‌دار، کد جدید نساز** مگر تست‌های فاز صریحاً `CASH_SESSION_REQUIRED` بخواهند — در آن صورت یک alias/کد جدید اضافه کن و در `RegisterPaymentCommand` برای متد Cash قبل از `Payment.Create` چک صریح بگذار که این کد را برگرداند.
- `RegisterPaymentCommand` از قبل پارامتر `CashSessionId` دارد و به `Payment.Create` پاس می‌دهد (`src/Animora.Application/UseCases/Sales/RegisterPaymentCommand.cs`). **پس تنها تغییر C07 که فاز می‌خواهد تقریباً انجام شده.** فقط باید: (1) resolve خودکار `CashSessionId` از `ICashSessionContext` وقتی کالر ندهد، (2) enforce صریح که پرداخت Cash بدون session باز رد شود، (3) ثبت `CashMovement` در session باز.
- **ستون `Payment.CashSessionId` از قبل در snapshot هست** (`AnimoraLocalDbContextModelSnapshot.cs` خط ~3054, maxLength 26, TEXT). پس ستون در schema موجود است.

### C) Migration numbering — `014` مصرف شده
- آخرین migration: `20260928140000_014_SalesInsurers`. **migration جدید باید `015_CashSessionLink` باشد، نه `014`** (فاز اشتباهاً `014` گفته).
- چون ستون `CashSessionId` از قبل در snapshot/model هست، migration جدید فقط برای **ایندکس روی `(TenantId, CashSessionId)`** روی جدول Payments لازم است (اگر PaymentConfiguration ایندکس اضافه کند، EF یک migration diff می‌سازد). اگر ستون قبلاً کامل mapped است، فقط ایندکس را در `PaymentConfiguration.cs` اضافه کن و migration بساز.

### D) ErrorCodes — اکثراً از قبل موجودند
در `src/Animora.Domain/Results/ErrorCodes.cs` این‌ها **از قبل هستند**:
- `CashSessionAlreadyOpen` = `CASH_SESSION_ALREADY_OPEN` ✅
- `CashSessionClosed` = `CASH_SESSION_CLOSED` ✅ (فاز `CASH_SESSION_NOT_OPEN` خواسته — از `CashSessionClosed` موجود استفاده کن، یا اگر تست جدا خواست بساز)
- `CashVarianceNoteRequired` = `CASH_VARIANCE_NOTE_REQUIRED` ✅
- `CashSessionUnsettledCommands` = `CASH_SESSION_UNSETTLED_COMMANDS` ✅
- `ChequeInvalidTransition` = `CHEQUE_INVALID_TRANSITION` ✅ (فاز `CHEQUE_TRANSITION_INVALID` خواسته — نام موجود `CHEQUE_INVALID_TRANSITION` است؛ از همین استفاده کن)
- `ChequeCounterpartyRequired` = `CHEQUE_COUNTERPARTY_REQUIRED` ✅
- `MovementSignInvalid` = `MOVEMENT_SIGN_INVALID` ✅
- `PaymentMethodReferenceRequired` (برای cash/cheque reference) ✅

**کدهایی که واقعاً ممکن است غایب باشند و باید اضافه شوند (فقط اگر لازم شد):**
- `CASH_SESSION_REQUIRED` (اگر تست جدا از `PAYMENT_METHOD_REFERENCE_REQUIRED` می‌خواهد)
- `CHEQUE_NOT_FOUND` (بررسی: `grep ChequeNotFound ErrorCodes.cs` → غایب بود)
- `CHEQUE_NOT_EDITABLE` (بررسی: غایب بود)
> **قانون:** قبل از افزودن هر کد، `grep -n "XXX" ErrorCodes.cs` بزن. کد تکراری نساز. فقط غایب‌ها را اضافه کن.

### E) CommandTypes — بخشی از قبل موجود
در `src/Animora.Contracts.Wire/Commands/CommandTypes.cs` از قبل:
- `CashierSessionOpen = "cashier.session.open"` ✅
- `CashierSessionClose = "cashier.session.close"` ✅
- `FinanceChequeTransition = "finance.cheque.transition"` ✅ (فاز 4 کد چک جدا خواسته `create/update/delete/status`؛ ولی ریپو یک `finance.cheque.transition` عمومی دارد)
**غایب (باید اضافه شوند به const‌ها و به آرایه‌ی All):**
- `cashier.movement.record` (برای RecordCashMovementCommand)
- `finance.cheque.create`, `finance.cheque.update`, `finance.cheque.delete`, `finance.cheque.status` (اگر تصمیم گرفتی به‌جای `transition` عمومی، کامندهای دانه‌ای فاز را بسازی)
> **تصمیم پیشنهادی:** برای هم‌راستایی با فاز، کامندهای دانه‌ای بساز و CommandTypeهای غایب را اضافه کن. `finance.cheque.transition` موجود را دست نزن (ممکن است جای دیگر رفرنس شود).

### F) PermissionCodes — از قبل موجود
`src/Animora.Domain/Identity/PermissionCodes.cs`: `CashierSessionOpen`, `CashierSessionClose` ✅ و `ChequesRead/ChequesManage` در `DefaultRolePermissions.cs`. permission `finance.cheque.manage` و `cashier.session.manage` را با نام‌های موجود نگاشت کن؛ کد جدید فقط اگر غایب بود.

### G) دامنه Cheque state machine ≠ Contracts ChequeStatus (نگاشت 1-to-1 لازم)
- **دامنه** `ChequeState` (`src/Animora.Domain/Finance/Cheque.cs`): `Registered=0, InPortfolio=1, Deposited=2, Cleared=3, Bounced=4, Returned=5, Cancelled=6, Settled=7`.
- **Contracts** `ChequeStatus` (`src/Animora.Contracts/Cheques/ChequeModels.cs`): `InHand, AtBank, Cleared, Bounced, Spent, Void`.
- **نگاشت پیشنهادی در `ChequesMapping`** (دامنه → contract):
  - `Registered` → `InHand`
  - `InPortfolio` → `InHand` (هر دو «در دست/در جریان»)
  - `Deposited` → `AtBank`
  - `Cleared` → `Cleared`
  - `Bounced` → `Bounced`
  - `Returned` → `Spent` (خرج/واگذار — نزدیک‌ترین)
  - `Cancelled` → `Void`
  - `Settled` → `Cleared` (نقداً تسویه شد ≈ وصول)
  > نگاشت را در جدول ثابت داخل `ChequesMapping` بگذار؛ هر دو جهت (برای فیلتر tab).
- **transition table دامنه** (منبع حقیقت — از `Cheque.IsTransitionAllowed`):
  ```
  Registered  → InPortfolio | Cancelled
  InPortfolio → Deposited | Returned | Cancelled
  Deposited   → Cleared | Bounced
  Bounced     → InPortfolio | Returned | Settled
  Cleared / Returned / Cancelled / Settled → terminal
  ```
- متدهای دامنه: `Register(...)`, `MoveToPortfolio`, `Deposit`, `MarkCleared` (raise `ChequeClearedEvent`), `MarkBounced(reason)` (raise `ChequeBouncedEvent`, reason اجباری), `Return(reason)`, `Cancel(reason)`, `SettleInCash`, `LinkJournalEntry`. هر transition یک `ChequeEvent` append می‌کند و `ChequeStateChangedEvent` raise می‌کند.
- `ChangeChequeStatusCommand` باید target دامنه‌ای را به متد درست map کند (نه صرفاً set state). برای Cleared/Bounced از متدهای اختصاصی که event راه می‌اندازند استفاده کن.

### H) رویدادهایی که C09 مصرف می‌کند (این فاز فقط raise می‌کند)
`ChequeClearedEvent`, `ChequeBouncedEvent`, `CashSessionClosedEvent` — همه از قبل در `Domain/Events/` تعریف‌اند و توسط متدهای دامنه raise می‌شوند. این فاز **journal نمی‌نویسد**.

---

## 📁 مسیرهای کلیدی (بررسی‌شده)

| مورد | مسیر |
|---|---|
| Contracts Cashier | `src/Animora.Contracts/Cashier/{ICashierService,CashierModels}.cs` |
| Contracts Cheques | `src/Animora.Contracts/Cheques/{IChequesQueryService,ChequeModels}.cs` |
| Domain CashSession/Movement | `src/Animora.Domain/Cashier/{CashSession,CashMovement}.cs` (موجود) |
| Domain Cheque/Event | `src/Animora.Domain/Finance/{Cheque,ChequeEvent}.cs` (موجود) |
| Payment domain | `src/Animora.Domain/Sales/Payment.cs` (CashSessionId موجود) |
| Ports | `src/Animora.Application/Ports/{ICashSessionRepository,IChequeRepository}.cs` (موجود) |
| C07 commands | `src/Animora.Application/UseCases/Sales/*.cs` |
| RegisterPayment | `src/Animora.Application/UseCases/Sales/RegisterPaymentCommand.cs` |
| ClinicCalendar | `src/Animora.Application/Time/ClinicCalendar.cs` (API: `DayRangeUtc`, `ToUtcTicks`, `LocalDay`, `MinutesFromDayStart`) |
| Persistence Queries | `src/Animora.Persistence.Sqlite/Queries/` (الگو: `SalesQueries.cs`) |
| PaymentConfiguration | `src/Animora.Persistence.Sqlite/Configurations/Sales/PaymentConfiguration.cs` |
| Migrations | `src/Animora.Persistence.Sqlite/Migrations/` (آخرین: `014_SalesInsurers`) |
| LocalSeeder | `src/Animora.Persistence.Sqlite/Seed/LocalSeeder.cs` (SeedSampleData flag, RunBlockAsync + SampleKey marker) |
| SampleData الگو | `src/Animora.Persistence.Sqlite/Seed/SalesSampleData.cs` (AlreadySeeded/Populate/SampleKey) |
| Client services الگو | `src/Animora.Client.Services/Sales/{SalesQueryService,SalesMapping}.cs` (C01 pattern, `IServiceScopeFactory scopes`, بدون EF/SQL) |
| Mocks (حذف شوند) | `src/Animora.Desktop/Mocks/{MockCashierService,MockChequesQueryService}.cs` |
| DI | `src/Animora.Desktop/App.axaml.cs` خطوط 308 و 313 |
| ViewModels | `src/Animora.Desktop/Pages/Cashier/CashierPageViewModel.cs`, `src/Animora.Desktop/Pages/Cheques/ChequesPageViewModel.cs` |
| ViewModel readiness الگو | `InventoryPageViewModel.cs`: field `IStoreReadinessGate _readiness;` → ctor inject → `await _readiness.WaitAsync();` در load |
| CommandTypes | `src/Animora.Contracts.Wire/Commands/CommandTypes.cs` |
| ErrorCodes | `src/Animora.Domain/Results/ErrorCodes.cs` |

---

## 🧩 الگوی C01 برای Client Service (رعایت شود)

از `SalesQueryService`:
```csharp
public sealed class CashierService(IServiceScopeFactory scopes) : ICashierService
{
    // هیچ EF/SQL/business rule نیست.
    // هر متد: await using scope → resolve(dispatcher, provider/identity, queries) →
    //   BuildQueryContext/BuildCommandContext → dispatch C07 command / query →
    //   map result به Cashier* DTO از طریق CashierMapping.
}
```
- Singleton؛ dispatcher/identity/read-port از یک async scope تازه در هر call، هرگز capture نشود.
- `RecalculateAsync` فقط `InvoiceDetailQuery` را دوباره اجرا می‌کند (هیچ command نمی‌فرستد).
- تست `CashierReusesSalesTests`: هیچ type زیر `Animora.Client.Services/Cashier` نباید `IUnitOfWork`/repository port/`AnimoraLocalDbContext` رفرنس کند.

---

## 🔢 ثوابت دامنه که باید ساخته شوند

- `CashVarianceThresholds` (فاز: `Domain/Sales/`؛ اما چون CashSession در `Domain/Cashier/` است، **پیشنهاد: `src/Animora.Domain/Cashier/CashVarianceThresholds.cs`** — کنار CashSession). const: `RequiresNote = 5_000_000` (minor units). **غایب — باید ساخته شود.**
- `ChequeAlertRules` — `src/Animora.Domain/Finance/ChequeAlertRules.cs`. سه قانون با آستانه‌های const: `DueWithin3Days` (3 روز), `OverdueUncleared`, `BouncedUnresolved`. **غایب.**
- `BankDirectory` — `src/Animora.Domain/Finance/BankDirectory.cs`. لیست ثابت 18 بانک ایرانی با `Key`, `Name` (و برای Contracts `Short/ColorFrom/ColorTo`). `IReadOnlyList` که یک‌بار ساخته شود. **غایب.** (منبع نام‌ها: بانک‌های اصلی ایران — ملی، ملت، صادرات، تجارت، سپه، کشاورزی، مسکن، رفاه، پارسیان، پاسارگاد، سامان، اقتصادنوین، سینا، دی، شهر، آینده، گردشگری، سرمایه.)

---

## ✅ تسک‌لیست اجرا (هر تسک = یک commit + push)

> وضعیت: `[ ]` انجام‌نشده · `[x]` انجام‌شده. بعد از هر `[x]` بلافاصله `scripts/git/push.sh "..."`.

### T01 — ثوابت دامنه + ErrorCodes غایب
- [x] `grep` بزن تا ببینی کدام ErrorCode واقعاً غایب است (`CHEQUE_NOT_FOUND`, `CHEQUE_NOT_EDITABLE`, احتمالاً `CASH_SESSION_REQUIRED`, `CASH_SESSION_NOT_OPEN`). فقط غایب‌ها را در `ErrorCodes.cs` اضافه کن (از `CashSessionClosed`/`ChequeInvalidTransition`/`PaymentMethodReferenceRequired` موجود بازاستفاده کن، تکراری نساز).
- [x] `src/Animora.Domain/Cashier/CashVarianceThresholds.cs` بساز — const `RequiresNote = 5_000_000L`.
- [x] `src/Animora.Domain/Finance/ChequeAlertRules.cs` بساز — const آستانه‌ها + سه rule helper.
- [x] `src/Animora.Domain/Finance/BankDirectory.cs` بساز — 18 بانک، static `IReadOnlyList`.
- **Push:** `scripts/git/push.sh "feat(domain): C08 statics (variance, cheque alerts, bank directory) + error codes"`

### T02 — Port ICashSessionContext + پیاده‌سازی + تغییر RegisterPayment
- [x] `src/Animora.Application/Ports/ICashSessionContext.cs` بساز — مثلاً `Task<string?> GetOpenSessionIdAsync(CancellationToken ct)` (id جلسه‌ی باز کاربر فعلی، یا null).
- [x] `src/Animora.Persistence.Sqlite/Cashier/CashSessionContext.cs` بساز — query جلسه‌ی باز `(TenantId, CashierUserId)` state=Open.
- [x] `RegisterPaymentCommand` را تغییر بده: اگر `CashSessionId` خالی است و متد Cash است، از `ICashSessionContext` resolve کن؛ اگر باز هم null بود برای Cash کد مناسب برگردان (`CASH_SESSION_REQUIRED` یا موجود `PaymentMethodReferenceRequired`)؛ متدهای غیر Cash بی‌تأثیر. یک `CashMovement` (Kind=SalePayment) در session ثبت کن. **این تنها تغییر C07 است و additive.**
- [x] DI ثبت `ICashSessionContext` در `ApplicationServiceCollectionExtensions` یا `LocalStoreServiceCollectionExtensions`.
- **Push:** `scripts/git/push.sh "feat(sales): stamp CashSessionId via ICashSessionContext, enforce cash-session on cash payments"`

### T03 — PaymentConfiguration ایندکس + migration 015
- [x] در `PaymentConfiguration.cs` ایندکس `(TenantId, CashSessionId)` اضافه کن (`HasDatabaseName("IX_Payments_TenantId_CashSessionId")`). ستون از قبل mapped است، فقط ایندکس.
- [x] migration بساز با نام `015_CashSessionLink` (نه 014). دستور EF از داخل پروژه persistence؛ اگر dotnet-ef در دسترس نبود، migration و Designer و snapshot diff را دستی مطابق الگوی migrationهای موجود بنویس. timestamp بزرگ‌تر از `20260928140000`.
- **Push:** `scripts/git/push.sh "feat(persistence): 015_CashSessionLink — Payment CashSessionId index"`

### T04 — CashierQueries + 4 کوئری Cashier
- [x] `src/Animora.Persistence.Sqlite/Queries/CashierQueries.cs` بساز.
  - `CashierStatsQuery`: **یک** statement grouped (collected/outstanding/refunded/session variance امروز).
  - `CashierTabCountsQuery`: **یک** grouped COUNT (نه per-tab).
  - list projection paged (از `QueryPaging`/`QueryGuards` موجود استفاده کن).
  - `OpenCashSessionQuery`: جلسه‌ی باز کاربر.
- [x] use caseها در `src/Animora.Application/UseCases/Cashier/`: `CashierInvoiceListQuery(CashierTab)`, `CashierStatsQuery()`, `CashierTabCountsQuery()`, `OpenCashSessionQuery()` + handlerها. read modelها را تعریف کن.
- **Push:** `scripts/git/push.sh "feat(cashier): CashierQueries + list/stats/tab-count/open-session use cases"`

### T05 — 3 کامند جلسه صندوق
- [x] در `src/Animora.Application/UseCases/Cashier/`:
  - `OpenCashSessionCommand(Money OpeningFloat)` — ابتدا `EnsureNoOpenSession` (query جلسه باز)، سپس `CashSession.Open(...)`، sessionNumber را از شمارنده per-tenant بگیر، repo.Add. `CASH_SESSION_ALREADY_OPEN` روی دومین باز.
  - `CloseCashSessionCommand(Money CountedCash, string? Note)` — `BeginClose(counted, note, clock, hasUnsettled)` سپس `Close(clock)`. hasUnsettled را از وضعیت settlement فاکتورهای لینک‌شده بگیر. variance غیرصفر بدون note → `CASH_VARIANCE_NOTE_REQUIRED`.
  - `RecordCashMovementCommand(CashMovementKind Kind, Money Amount, string Reason)` — `CashMovement.Create(...)` + `session.AddMovement(...)`. append-only.
  - CommandTypes: `cashier.movement.record` را اضافه کن. permission `cashier.session.manage`/موجودها.
- **Push:** `scripts/git/push.sh "feat(cashier): open/close/record-movement session commands with variance rule"`

### T06 — ChequeQueries + 4 کوئری Cheque
- [x] `src/Animora.Persistence.Sqlite/Queries/ChequeQueries.cs` بساز:
  - list projection (paged) → map به Contracts `Cheque` shape.
  - `ChequeCashflowQuery(int Weeks = 8)`: یک statement، سپس bucket در حافظه به هفته‌های ISO clinic-local از طریق `ClinicCalendar` (نه SQL date function — R-B24). حداکثر 8×2 گروه.
  - `ChequeBankBreakdownQuery()`: یک `GROUP BY BankKey`.
  - `ChequeAlertsQuery()`: از `ChequeAlertRules` (DueWithin3Days/OverdueUncleared/BouncedUnresolved).
- [x] use caseها در `src/Animora.Application/UseCases/Finance/Cheques/` + handlerها + read modelها.
- **Push:** `scripts/git/push.sh "feat(cheques): ChequeQueries + list/cashflow/breakdown/alerts use cases"`

### T07 — 5 کامند چک
- [x] در `src/Animora.Application/UseCases/Finance/Cheques/`:
  - `CreateChequeCommand` — `Cheque.Register(...)` (invariant counterparty، amount مثبت)، شروع در `Registered`.
  - `UpdateChequeCommand` — فقط metadata؛ اگر state ≠ `Registered` → `CHEQUE_NOT_EDITABLE`.
  - `DeleteChequeCommand` — soft delete، فقط از `Registered`.
  - `ChangeChequeStatusCommand(ChequeId, ChequeState Target)` — target را به متد دامنه‌ی درست map کن (MoveToPortfolio/Deposit/MarkCleared/MarkBounced/Return/Cancel/SettleInCash). transition نامعتبر → `CHEQUE_INVALID_TRANSITION`. Cleared/Bounced حتماً از متدهای event-raising استفاده کنند (reason برای Bounced اجباری).
  - `BulkChangeChequeStatusCommand(IReadOnlyList<string> Ids, ChequeState Target)` — در **یک** unit of work، per-cheque command، لیست نتیجه per-id (partial success گزارش شود، نه silent swallow — R-B16/A-RULE024). هر چک یک OperationId مستقل (A-RULE 006).
  - CommandTypes: `finance.cheque.create/update/delete/status` را اضافه کن (به آرایه All هم). permission `finance.cheque.manage`/`ChequesManage` موجود.
- **Push:** `scripts/git/push.sh "feat(cheques): create/update/delete/status/bulk-status lifecycle commands"`

### T08 — Seed: BankDirectory + FinanceSampleData
- [x] `LocalSeeder.cs` را تغییر بده تا `BankDirectory` را seed کند (اگر لازم به persist است؛ در غیر این صورت `GetBanksAsync` مستقیم از static list می‌خواند — طبق فاز، بدون DB round-trip).
- [x] `src/Animora.Persistence.Sqlite/Seed/FinanceSampleData.cs` بساز مطابق الگوی `SalesSampleData` (SampleKey marker, AlreadySeeded, Populate). محتوا: 24 چک (هر دو جهت، همه stateها، پخش‌شده در 8 هفته آینده و 4 هفته گذشته) + 2 جلسه بسته + 1 جلسه باز. پشت `SeedSampleData` flag و RunBlockAsync.
- [x] `FinanceSampleData` را در ctor `LocalSeeder` inject و در بلاک sample صدا بزن.
- **Push:** `scripts/git/push.sh "feat(seed): bank directory + FinanceSampleData (24 cheques, 3 cash sessions)"`

### T09 — Client Services: CashierService/Mapping + ChequesQueryService/Mapping
- [x] `src/Animora.Client.Services/Cashier/CashierMapping.cs` — نگاشت read modelهای C07 → `Cashier*` DTOها.
- [x] `src/Animora.Client.Services/Cashier/CashierService.cs` — 15 عضو `ICashierService`، فقط delegate به کامندهای C07 (الگوی C01). بدون EF/SQL.
- [x] `src/Animora.Client.Services/Cheques/ChequesMapping.cs` — نگاشت دامنه ↔ contract (جدول state از بخش G).
- [x] `src/Animora.Client.Services/Cheques/ChequesQueryService.cs` — پیاده‌سازی `IChequesQueryService`. `GetBanksAsync` از `BankDirectory` static.
- **Push:** `scripts/git/push.sh "feat(client): real CashierService & ChequesQueryService (C01 pattern, reuse C07)"`

### T10 — DI swap + ViewModel readiness + حذف mocks
- [ ] `App.axaml.cs`: خط 308 `MockCashierService` → `CashierService`؛ خط 313 `MockChequesQueryService` → `ChequesQueryService`. namespaceها را import کن.
- [ ] `CashierPageViewModel.cs` و `ChequesPageViewModel.cs`: خط readiness-gate اضافه کن (field `IStoreReadinessGate _readiness` + ctor inject + `await _readiness.WaitAsync();` در ابتدای load — الگوی `InventoryPageViewModel`).
- [ ] حذف `src/Animora.Desktop/Mocks/MockCashierService.cs` و `MockChequesQueryService.cs`. مطمئن شو هیچ DI/using به آن‌ها رفرنس نمی‌دهد.
- **Push:** `scripts/git/push.sh "refactor(desktop): swap DI to real services, add readiness gate, delete cashier/cheque mocks"`

### T11 — تست‌ها (3 کلاس)
- [ ] `tests/Animora.Application.Tests/CashSessionUseCaseTests.cs`: دومین باز → `CASH_SESSION_ALREADY_OPEN`؛ پرداخت cash بدون session باز → کد مربوطه در حالی که card موفق؛ بستن با counted → variance دقیق ثبت؛ variance بزرگ بدون note → `CASH_VARIANCE_NOTE_REQUIRED`؛ payment stamping.
- [ ] `tests/Animora.Application.Tests/ChequeLifecycleTests.cs`: هر transition مجاز موفق + دقیقاً یک `ChequeEvent`؛ نامعتبر → `CHEQUE_INVALID_TRANSITION`؛ edit چک cleared → `CHEQUE_NOT_EDITABLE`؛ bulk 3تا با 1 نامعتبر → 2 اعمال + 1 fail گزارش؛ clear → دقیقاً یک `ChequeClearedEvent`.
- [ ] `tests/Animora.Application.Tests/CashierReusesSalesTests.cs`: هیچ type زیر `Animora.Client.Services/Cashier` به `IUnitOfWork`/repository/`AnimoraLocalDbContext` رفرنس ندهد (reflection/source scan).
- **Push:** `scripts/git/push.sh "test(C08): cash session, cheque lifecycle, cashier-reuses-sales"`

### T12 — بیلد سریع + تأیید نهایی
- [ ] بیلد سریع فقط پروژه‌های تغییرکرده (نه full طولانی): `dotnet build src/Animora.Domain src/Animora.Application ...` یا `scripts/sandbox/build.sh` اگر سریع است. **گرفتن full test/build طولانی ممنوع** — فقط کامپایل‌شدن تغییرات را تأیید کن.
- [ ] مطمئن شو mockها حذف و DI درست است (grep نهایی).
- **Push:** `scripts/git/push.sh "chore(C08): finalize — build check & cleanup"`

### T13 — حذف این فایل پلن (تسک پایانی)
- [ ] فقط `Plan/PHASE-C08-PLAN.md` را حذف کن (به فایل‌های دیگر و roadmap دست نزن؛ فایل فاز `backend-roadmap/.../PHASE-C08...` را هم دست نزن مگر خود فاز اجرایش را جزو کار خواسته باشد — این پلن فقط خودش را حذف می‌کند).
- **Push:** `scripts/git/push.sh "chore: remove C08 plan file after completion"`

---

## 📌 قواعد اجرا (خلاصه)
- بررسی فایل‌ها فقط هدفمند و بر اساس نیاز.
- کد استاندارد و باکیفیت؛ کد تکراری نساز (خصوصاً ErrorCodes/CommandTypes/Payment که بخش‌هایی از قبل هستند).
- **بیلد/تست full طولانی ممنوع** — فقط کامپایل تغییرات.
- بعد از هر تسک `[x]`، بلافاصله `scripts/git/push.sh`.
- هر انحراف واقعی از فاز را در commit message ثبت کن (برای PROGRESS.md آینده C09).
