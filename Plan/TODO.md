# Plan — رفع سه مشکل موجودی/فاکتور + سیستم «در انتظار اعمال روی سرور»

> این فایل خودکفاست. یک مدل در سشن جدید باید فقط همین را بخواند و مستقیم پیاده‌سازی کند،
> بدون بررسی دوبارهٔ ریپو. مسیرها و یافته‌های خام در پایین هر تسک آمده‌اند.
> **قانون قفل‌شده:** بعد از تکمیل هر تسک، حتماً `scripts/git/push.sh "msg"` اجرا شود
> (روی برنچ `genspark_ai_developer`). نبود push = شکست.

---

## ۰) زمینهٔ معماری (لازم برای هر سه تسک — یک‌بار بخوان)

- محصول **Animora**: دسکتاپ Avalonia/.NET 10، آفلاین‌فرست، فارسی/RTL، همگام با سرور مرکزی.
- جریان داده: `View → ViewModel → Contracts interface (DI) → Client.Services impl → Application dispatch → SQLite محلی`.
  ViewModelها فقط به interfaceهای `Animora.Contracts` وابسته‌اند.
- **نوشتن‌ها از مسیر Command Outbox می‌روند** (نه pull). هر نوشتن یک Command در ژورنال محلی SQLite ثبت و
  بعداً به سرور push و «settle» می‌شود. Pull فقط master/clinical data می‌آورد؛
  **Invoice/Payment/Stock عمداً در فید pull نیستند** (`SyncExclusions`).
- **مهم (ریشهٔ تسک ۳):** settlement فقط وضعیت *رکورد Command* را عوض می‌کند
  (`Settled/Rejected/Conflict`)، نه رکورد business‌ای که UI می‌خواند (فاکتور/پرداخت).
  یعنی هیچ تگ «هنوز روی سرور اعمال نشده» در UI وجود ندارد. (منبع: `Plan/reports/06-sync-system.md` یافتهٔ SYN-02)
- **وضعیت همگام‌سازی موجود و آماده (زیرساخت تسک ۲ و ۳):**
  `Animora.Contracts/Sync/ISyncStatusService.cs` → `enum SyncState { Idle, Syncing, Synced, Pending, Error, Offline, NeedsAttention }`
  به‌علاوهٔ `State`, `LastSyncedAt`, `int PendingChangeCount`, `int TerminalFailureCount`, `event StateChanged`, `Task SyncNowAsync(ct)`.
  - پیاده‌سازی: `Animora.Client.Services/Sync/LocalSyncStatusService.cs` (singleton).
    - `SyncState.Offline` وقتی transport = `NullCommandTransport` و pull هم configured نیست (یعنی سروری وصل نیست).
    - `PendingChangeCount` = backlog قابل‌ارسال (Draft+LocallyPosted+PendingSettlement).
    - `TerminalFailureCount` = Rejected+Conflict+NeedsAttention (ردشده توسط سرور).
  - مصرف‌کننده‌ها: `Desktop/Shell/TopbarViewModel.cs` (باکس وضعیت همگام‌سازی)،
    `Desktop/Services/SyncChangeBridge.cs` (بعد از pass موفق، `IDataChangeNotifier` را coarse اعلام می‌کند).
  - **در حال حاضر در سندباکس سروری وصل نیست ⇒ همیشه `Offline`.** پس نمی‌توان روی «آنلاین واقعی» تکیهٔ کامل کرد؛
    معیار آنلاین/آفلاین باید از همین `ISyncStatusService.State` خوانده شود: `State == Offline` ⇒ آفلاین،
    در غیر این صورت آنلاین (می‌توان با سرور حرف زد).
- **DI root:** `src/Animora.Desktop/App.axaml.cs` (همهٔ سرویس‌ها و ViewModelها اینجا register می‌شوند).
- **بیلد سریع فقط دسکتاپ:** `scripts/sandbox/build.sh` (انتظار 0 warning/0 error).
  بیلد کامل: `scripts/sandbox/build.sh full`. **تست کامل/طولانی ممنوع** — فقط build + در صورت لمس startup/DI یک smoke: `scripts/sandbox/run.sh`.
- پول/تعداد همگی integer-minor و اعتبارسنجی نهایی سمت دامنه است؛ UI فقط گاردِ UX و defense-in-depth می‌گذارد.

---

## تسک ۱ — گارد موجودی: استایل زنده + سقف تعداد در سطر فاکتور

### ۱.۱ خلاصهٔ دو مشکل (طبق خواستهٔ کاربر)
- **مشکل A (استایل):** وقتی با افزودن محصول موجودی به ۰ می‌رسد، باید همان لحظه ردیف/چیپِ آن محصول در
  لیست کاتالوگ خاکستری و غیرفعال شود؛ ولی «در لحظه» کامل خاکستری نمی‌شود.
- **مشکل B (سقف تعداد در سطر فاکتور):** بعد از افزودن محصولی که مثلاً ۲ موجودی دارد، از داخل خودِ سطر فاکتور
  با دکمهٔ `+` می‌توان تعداد را بی‌نهایت بالا برد (فراتر از موجودی). باید محدود شود.

### ۱.۲ یافته‌های خام / ریشه‌یابی (قطعی — دوباره بررسی نکن)
**فایل‌ها:**
- `src/Animora.Desktop/Pages/Sales/CatalogModalViewModel.cs` — منطق گارد افزودن از کاتالوگ.
- `src/Animora.Desktop/Pages/Sales/CatalogModalView.axaml` — bindingها.
- `src/Animora.Desktop/Pages/Sales/InvoiceLineViewModel.cs` — سطر فاکتور + stepper.
- `src/Animora.Desktop/Pages/Sales/SalePageViewModel.cs` — متد `EditLineAsync`، ساخت `InvoiceLineViewModel`، `ShowInvoice`.
- `src/Animora.Contracts/Sales/SalesModels.cs` — `CatalogItem` (فیلدهای `OnHandScaled`, `TracksStock`, `IsOutOfStock`, `AvailableUnits`, `StockScale=1000`) و `InvoiceLine` (دارای `Index`, `Quantity`, `LineId`, `Ref`, `Kind`).
- `src/Animora.Application/UseCases/Sales/AddInvoiceLineCommand.cs` — **گارد موجودی سمت سرور دارد** (خط ~46-57: مجموع `Quantity` همان variant روی فاکتور + requested نباید از `OnHandScaled` بگذرد، وگرنه `ErrorCodes.InsufficientStock`).
- `src/Animora.Application/UseCases/Sales/EditInvoiceLineCommand.cs` — **هیچ گارد موجودی ندارد** (فقط `EditLine` با Quantity جدید؛ Range 0.001..1e6). ← **ریشهٔ مشکل B سمت سرور.**

**ریشهٔ مشکل A (استایل):**
- در `CatalogModalViewModel.AddAsync` بعد از افزودن، برای آیتم stock-tracked:
  `_sessionAdded[id]++` سپس `RefreshAddability(id)` فراخوانی می‌شود که `row.CanAdd`/`chip.CanAdd` را ست می‌کند.
- در `CatalogRowViewModel`/`CatalogRecentChipViewModel`، `CanAdd` یک `[ObservableProperty]` است با
  `[NotifyPropertyChangedFor(nameof(IsUnavailable))]` و (در row) `[NotifyPropertyChangedFor(nameof(AddBackground))]`.
- اما `TextForeground`, `IcoBackground`, `IcoForeground` **propertyهای سادهٔ get هستند و هنگام فلیپ‌شدن `CanAdd`
  نوتیفای نمی‌شوند** و فقط بر اساس `IsOutOfStock` (که در زمان ساخت ثابت است) رنگ می‌گیرند.
- XAML (`CatalogModalView.axaml` خطوط ~209-287): `Classes.out="{Binding IsUnavailable}"`, `IsEnabled="{Binding CanAdd}"`,
  لیبل «موجودی ندارد» `IsVisible="{Binding IsUnavailable}"`. پس کلاس `.out` و disable و لیبل **درست فلیپ می‌شوند**،
  ولی رنگ آیکن/متن چون از `IsOutOfStock` ثابت می‌آیند خاکستری نمی‌شوند ⇒ ظاهر «نیمه‌خاکستری/ناقص».
- نتیجه‌گیری تمیز برای پیاده‌سازی: باید رنگ‌ها هم به وضعیت زندهٔ `IsUnavailable` گره بخورند و نوتیفای شوند،
  نه فقط `IsOutOfStock` زمان ساخت.

**ریشهٔ مشکل B (سقف سطر فاکتور):**
- `InvoiceLineViewModel.IncreaseCommand = new AsyncRelayCommand(() => edit!(line, line.Quantity + 1), () => CanEdit)` —
  `canExecute` فقط `CanEdit` است، **هیچ سقف موجودی ندارد**.
- `SalePageViewModel.EditLineAsync` ⇒ `_service.EditLineAsync(...)` ⇒ `EditInvoiceLineCommand` که گارد موجودی ندارد.
- پس حتی اگر UI جلوی کلیک را نگیرد، سرور هم جلوی افزایش بیش از موجودی را نمی‌گیرد.

### ۱.۳ کارهایی که باید انجام شود

**(1-A) استایل زندهٔ کاتالوگ** — در `CatalogModalViewModel.cs`:
- در `CatalogRowViewModel` و `CatalogRecentChipViewModel`:
  - `TextForeground`, `IcoBackground`, `IcoForeground` را از getهای ثابت به propertyهایی تبدیل کن که بر پایهٔ
    `IsUnavailable` (نه فقط `IsOutOfStock`) محاسبه شوند، و با `[NotifyPropertyChangedFor(...)]` روی `CanAdd`
    (و در صورت لزوم افزودن notify دستی در جایی که `IsOutOfStock`/`CanAdd` تغییر می‌کند) هنگام فلیپ `CanAdd` نوتیفای شوند.
    رنگ خاکستری همان `SalePresentation.PlaceholderAvatarBrush()` / `PlaceholderAvatarForeground()` و متنِ `#667085` فعلی باشد.
  - دقت: `IcoBackground`/`IcoForeground` فعلاً در ctor از ورودی `icoBackground/icoForeground` یا placeholder ست می‌شوند؛
    باید دو ورژن (normal/placeholder) را نگه داری و در getter بر اساس `IsUnavailable` یکی را بده.
- هدف: لحظه‌ای که `CanAdd=false` شد، کل ردیف/چیپ (رنگ آیکن + رنگ متن + opacity کلاس `.out` + دکمهٔ غیرفعال + لیبل «موجودی ندارد») یک‌دست خاکستری شود.
- **بدون تغییر XAML هم باید کار کند** (bindingها از قبل به `IsUnavailable`/`CanAdd` وصل‌اند)؛ فقط اگر لازم شد همان bindingها را نگه‌دار.

**(1-B) سقف تعداد در سطر فاکتور** — رویکرد «استاندارد و هماهنگ» (UI gate + server gate):

*UI gate* (`InvoiceLineViewModel.cs` + `SalePageViewModel.cs`):
- `InvoiceLineViewModel` باید بداند «حداکثر تعداد مجاز» برای این خط چقدر است.
  - برای خطوط stock-tracked، سقف = واحدهای on-hand آن محصول (همان معیار کاتالوگ: `OnHandScaled/1000` کف‌شده).
  - منبع on-hand: هنگام `ShowInvoice`، `SalePageViewModel` کاتالوگ را دارد/می‌تواند بخواند؛ ساده‌ترین راهِ کم‌هزینه:
    یک‌بار `GetCatalogAsync()` را (یا یک map از `Ref/CatalogItemId → AvailableUnits`) در `ShowInvoice` واکشی/کش کن و
    به هر `InvoiceLineViewModel` به‌صورت «maxQuantity» (یا `int.MaxValue` برای غیرقابل‌ردگیری/خدمت) بده.
    `InvoiceLine.Ref` کلید کاتالوگ است؛ `InvoiceLine.Kind == Item` و وجود on-hand ⇒ ردگیری‌شونده.
  - `IncreaseCommand.canExecute` = `CanEdit && line.Quantity < maxQuantity`. وقتی به سقف رسید، `+` غیرفعال شود.
  - اگر کاربر به سقف رسید، یک toast کوتاه «بیش از موجودی «...» قابل افزودن نیست» (الگوی پیام مثل `CatalogModalViewModel.AddAsync`).
  - **توجه پرفورمنس:** کاتالوگ را فقط یک‌بار در `ShowInvoice` بخوان (نه per-line، نه per-click). Refresh بعد از mutation همان مسیر `RefreshAfterMutationAsync` است که دوباره `ShowInvoice` را صدا می‌زند.

*Server gate* (`src/Animora.Application/UseCases/Sales/EditInvoiceLineCommand.cs`):
- همان منطق گارد `AddInvoiceLineCommand` را به‌صورت متقارن اضافه کن: اگر خطِ در حال ویرایش یک variant با `TrackStock`
  است، مجموع تعداد همان variant روی فاکتور **پس از اعمال تعداد جدید این خط** نباید از `OnHandScaled` بگذرد؛ وگرنه
  `Result<Unit>.Fail(ErrorCodes.InsufficientStock)`.
  - نکته: handler فعلی `EditInvoiceLineCommandHandler` فقط `IInvoiceRepository` دارد؛ برای on-hand باید
    `ISalesDraftData` (همان‌که `AddInvoiceLineCommandHandler` دارد، متد `FindCatalogEntryAsync` با `TrackStock`/`VariantId`/`OnHandScaled`) را به ctor تزریق کنی.
    variant خط را از `found.Value.Lines` که `LineAt(index, expectedLineId)` برمی‌گرداند بگیر (`line.Value.VariantId`).
    محاسبهٔ scaled مثل Add: `ceil((sumOtherSameVariant + newQty) * 1000) > OnHandScaled` ⇒ fail.
    (دقت: تعداد خودِ این خط را با newQty جایگزین کن، نه اضافه.)
- UI وقتی `ServiceException` با کد `INSUFFICIENT_STOCK` بگیرد، پیام مناسب نشان دهد (مسیر `MutateLineAsync`→`ShowFailure` از قبل پیام generic می‌دهد؛ در صورت امکان پیام اختصاصی موجودی).

**(1-C) هماهنگی در همهٔ مسیرهای افزودن محصول:** CatalogModal reusable است و در visit/boarding/encounter هم استفاده می‌شود
(ctorهای `CatalogModalViewModel`). گاردِ Add از قبل مشترک است. برای گاردِ *ویرایش تعداد*، همان سقف باید در
هر سطری که تعداد قابل‌ویرایش است اعمال شود — فعلاً تنها مسیر ویرایش تعداد، سطر فاکتور فروش است (`SalePageViewModel`/`InvoiceViewerViewModel`؟).
  - **قبل از شروع، بررسی هدفمند:** `InvoiceViewerViewModel.cs` (در همان پوشهٔ Sales) هم stepper دارد؟ اگر بله همان سقف را آنجا هم بگذار.
    (فقط همین دو فایل را چک کن؛ grep `IncreaseCommand`/`EditLineAsync` در `src/Animora.Desktop`.)

### ۱.۴ وضعیت
- [x] 1-A استایل زندهٔ کاتالوگ
- [x] 1-B سقف تعداد: UI gate در InvoiceLineViewModel/SalePageViewModel
- [x] 1-B سقف تعداد: Server gate در EditInvoiceLineCommand
- [x] 1-C هماهنگی با InvoiceViewer/سایر مسیرهای ویرایش تعداد
- [x] build سبز (طبق دستور کاربر: بیلد فقط روی گیت‌اکشن) → **push**

---

## تسک ۲ — بازبینی قبل از ثبت/تسویهٔ فاکتور (آنلاین) + هشدار آفلاین

### ۲.۱ خواسته
هنگام «ثبت فاکتور» (در این برنامه = تسویه/پرداخت از طریق `PaymentModal`، دکمهٔ «پرداخت و تسویه کامل»):
۱) **اگر آنلاین:** اول لیست محصولات/خدمات از سرور بروز شود، سپس بررسی شود آیا از نظر تعداد/موجودیِ اقلام فاکتور
   مشکلی نیست — با در نظر گرفتن سناریوها: **کم‌شدن موجودی**، **حذف‌شدن محصول/خدمت**. در هر سناریو پیام مناسب و واضح
   به ادمین؛ اگر مشکل بود ⇒ اخطار و جلوگیری از ثبت تا اصلاح؛ اگر مشکلی نبود ⇒ فاکتور ثبت/تسویه شود.
۲) **اگر آفلاین:** این مرحله انجام نمی‌شود، ولی موقع ثبت به ادمین گفته شود «الان آفلاینی؛ بهتر است آنلاین شوی تا چیزی
   بهم نریزد». (اجازهٔ ادامه داده شود چون آفلاین‌فرست است، ولی با هشدار صریح.)

### ۲.۲ یافته‌های خام
- نقطهٔ «ثبت/تسویه»: `src/Animora.Desktop/Pages/Sales/PaymentModalViewModel.cs` — متد `PayAsync`
  (خطوط ~340-400، `_service.TakePaymentAsync(...)`). همچنین `SalePageViewModel.TakePayment` این مودال را باز می‌کند.
- آنلاین/آفلاین: از `ISyncStatusService.State` (تزریق به ViewModel؛ در DI موجود است). `State == SyncState.Offline` ⇒ آفلاین.
- بروزرسانی لیست از سرور: مسیر واقعی = `ISyncStatusService.SyncNowAsync(ct)` که هم backlog را flush و هم state pull می‌کند.
  بعد از آن، کاتالوگ تازه را با `ISalesQueryService.GetCatalogAsync()` بخوان و فاکتور را با `GetInvoiceAsync(id)` بخوان.
- بررسی مغایرت: مقایسهٔ خطوط فاکتور (`InvoiceDetail.Lines`) با کاتالوگ تازه:
  - **حذف‌شده:** `line.Ref` دیگر در کاتالوگ نیست و `IsCustom==false` و خط catalog-based است ⇒ «محصول/خدمت «{Title}» دیگر موجود نیست».
  - **کمبود موجودی:** برای خطوط `Kind==Item` و stock-tracked، مجموع تعداد همان `Ref` روی فاکتور > `AvailableUnits` کاتالوگ تازه ⇒ «موجودی «{Title}» کافی نیست (موجود: X، در فاکتور: Y)».
  - خدمات (`Kind==Service`) فقط برای حذف‌شدن بررسی شوند، نه موجودی.
- **توجه:** چون در سندباکس سرور نیست، `SyncNowAsync` اغلب `Offline` برمی‌گرداند؛ پس منطق باید این‌طور باشد:
  اگر `State==Offline` (قبل یا بعد از تلاش) ⇒ مسیر آفلاین (هشدار + اجازهٔ ادامه). اگر آنلاین ⇒ بازبینی کامل.

### ۲.۳ کارها
- در `PaymentModalViewModel` (یا یک helper مشترک تا هماهنگ بماند):
  - تزریق `ISyncStatusService` (و `ISalesQueryService` که از قبل هست).
  - قبل از `TakePaymentAsync`:
    - اگر `State != Offline`: `await SyncNowAsync()` → سپس کاتالوگ+فاکتور تازه بخوان → تابع بازبینی اجرا کن.
      - اگر مغایرت بود: toast/دیالوگ خطا با پیام دقیقِ هر سناریو و **جلوگیری از ثبت** (return). ادمین باید فاکتور را اصلاح کند (حذف/کم‌کردن خط) و دوباره بزند.
      - اگر OK: ادامه به پرداخت.
    - اگر `State == Offline`: یک هشدار صریح (toast طولانی یا تاییدِ سبک) «در حال حاضر آفلاین هستید؛ برای جلوگیری از ناهماهنگی بهتر است آنلاین شوید» سپس اجازهٔ ادامهٔ ثبت (آفلاین‌فرست).
  - **پرفورمنس:** بازبینی فقط هنگام کلیک ثبت؛ یک‌بار کاتالوگ خوانده شود؛ فشار اضافی روی سرور/برنامه نیاید
    (از `SyncNowAsync` موجود استفاده کن، pass جدید اختراع نکن).
- پیام‌ها فارسی، واضح، و تفکیک‌شده بر اساس سناریو (حذف‌شده / کمبود موجودی / چند مورد هم‌زمان خلاصه شود).
- **هماهنگی:** اگر مسیر دیگری هم مستقیماً تسویه می‌کند (grep `TakePaymentAsync` در Desktop) همان گارد را اعمال کن.

### ۲.۴ وضعیت
- [ ] تزریق ISyncStatusService و منطق آنلاین/آفلاین در PaymentModalViewModel
- [ ] تابع بازبینی مغایرت (حذف‌شده/کمبود موجودی) با پیام هر سناریو
- [ ] هشدار آفلاین + اجازهٔ ادامه
- [ ] هماهنگی سایر مسیرهای TakePaymentAsync
- [ ] build سبز → **push**

---

## تسک ۳ — سیستم یکپارچهٔ «هنوز روی سرور اعمال نشده» در تاریخچه‌ها

### ۳.۱ خواسته
هر چیزی که ممکن است آفلاین ثبت شود (مثلاً فاکتورها) باید در **تاریخچهٔ اصلی + تاریخچهٔ خودِ آن مورد** یک حالت/پیام
نشان دهد که «این مورد هنوز روی سرور اعمال نشده و فقط محلی در صف است؛ بعد از اولین اتصال اعمال می‌شود».
- بعد از اعمال واقعی روی سرور ⇒ وضعیت «اعمال شد».
- اگر اعمال باعث خطا/ناهماهنگی شد ⇒ تگ «اختلال/ناهماهنگی».
- اگر به‌خاطر اینترنت ناموفق ماند ⇒ تگ مناسب، و بازمی‌گردد در صف منتظر اتصال.
- هدف: UX بهتر؛ کارکنان نفهمند «ثبت شد = روی سرور است» در حالی که نیست.
- باید **استاندارد، یکپارچه، و کم‌فشار** باشد (پرفورمنس مهم).

### ۳.۲ یافته‌های خام (ریشه‌یابی)
- **منبع حقیقت وضعیت صف:** ژورنال Commandها. هر نوشتن یک رکورد Command دارد با وضعیت
  `Draft/LocallyPosted/PendingSettlement` (قابل‌ارسال/در صف) یا terminal `Rejected/Conflict/NeedsAttention`.
  جمع‌بندی در `PendingCommandSummary` (`src/Animora.Application/Commands/ICommandJournal.cs`,
  `src/Animora.Persistence.Sqlite/Commands/CommandJournal.cs`, `UseCases/Sync/PendingCommandSummaryQuery.cs`).
- **خلأ اصلی (SYN-02):** settlement فقط رکورد Command را آپدیت می‌کند، نه رکورد فاکتور/پرداختی که تاریخچه می‌خواند.
  هیچ نگاشتی از «این فاکتور/این ردیف تاریخچه ⇄ کدام Command و چه وضعیتی» در Contracts/UI نیست.
- **تاریخچه‌ها:**
  - تاریخچهٔ اصلی: `src/Animora.Contracts/History/HistoryModels.cs` + `IHistoryQueryService.cs`، صفحهٔ `Desktop/Pages/History/`.
    `enum HistoryEventStatus` دارد (Paid/Partial/Pending/New/...). ← جای افزودن یک وضعیت/نشانهٔ «در صف سرور» و «ناهماهنگ».
  - تاریخچهٔ خودِ فاکتور: `InvoiceDetail.Audit` (`InvoiceAuditEntry`) + `InvoiceAuditKind` در `SalesModels.cs`؛
    و لیست فاکتورها `InvoiceSummary`. صفحهٔ `Sales` (SaleHistory/SaleInvoiceLedger).
- **زیرساخت آمادهٔ مصرف در UI:** `ISyncStatusService` با `State`, `PendingChangeCount`, `TerminalFailureCount`, `StateChanged`.
  `SyncChangeBridge` بعد از pass موفق، `IDataChangeNotifier` را coarse اعلام می‌کند ⇒ صفحات باز رفرش می‌شوند.

### ۳.۳ رویکرد پیشنهادی (استاندارد، کم‌هزینه، بدون سنگین‌کردن سرور)

**اصل طراحی:** از آنجا که نگاشت دقیق «هر رکورد business ⇄ وضعیت Command» گران و فعلاً موجود نیست، یک لایهٔ
**«آیا این مورد ممکن است هنوز در صف باشد»** بساز که بر پایهٔ دادهٔ موجود کار کند و بیش‌ازحد مهندسی نشود:

- **سطح رکورد (ترجیحی اگر ارزان بود):** اگر `CommandJournal` بتواند به‌ازای یک aggregateId (مثلاً InvoiceId)
  وضعیت صف را بدهد، یک متد سبک `GetPendingStateForAggregateAsync(type, id)` یا یک «مجموعهٔ aggregateIdهای در صف/ناهماهنگ»
  در یک فراخوانی اضافه کن (نه per-row کوئری — **یک کوئری که set را برگرداند**، سپس UI با lookup از همان set استفاده کند).
  - بررسی هدفمند لازم: `CommandJournal` چه فیلدهایی دارد؟ آیا `AggregateId`/`AggregateType` و `State` روی رکوردها هست؟
    (grep در `src/Animora.Persistence.Sqlite/Commands/CommandJournal.cs` و مدل رکورد ژورنال.) اگر هست ⇒ متد set-returning اضافه کن.
  - این متد را در `ISalesQueryService`/`IHistoryQueryService` (یا یک interface سبک جدید `ISyncPendingLookup` در Contracts.Sync)
    expose کن تا ViewModelها بتوانند «این فاکتور/این رویداد در صف است؟» را O(1) از روی set چک کنند.
- **سطح سراسری (حداقل قابل‌قبول و حتماً انجام شود):** اگر set-per-aggregate پرهزینه/پیچیده بود، حداقل:
  - یک **بنر/نشانهٔ سراسری** بالای تاریخچه‌ها و لیست فاکتورها که وقتی `PendingChangeCount > 0` باشد پیام بدهد
    «N مورد هنوز روی سرور اعمال نشده و در صف ارسال است» و وقتی `TerminalFailureCount > 0` ⇒ «M مورد نیازمند بررسی/ناهماهنگ».
  - این حداقل، مستقیماً از `ISyncStatusService` می‌آید و **هیچ فشار اضافه‌ای روی سرور ندارد**.

**نمایش در UI (یکپارچه):**
- تاریخچهٔ اصلی (`History`): یک حالت/چیپ «در صف سرور» (tone = warn/info) برای رویدادهایی که aggregateشان در set صف است،
  و «ناهماهنگ» (tone = danger) برای setِ terminal. اگر فقط بنر سراسری انتخاب شد، بنر بالای لیست.
- لیست/تاریخچهٔ فاکتور (`Sales`): همان چیپ/بنر روی کارت فاکتور یا بالای لیست.
- پیام‌ها فارسی و شفاف:
  - در صف: «این مورد فقط به‌صورت محلی ثبت شده و پس از اتصال به سرور اعمال می‌شود.»
  - اعمال‌شد: نیازی به چیپ نیست (نبودِ چیپ = اعمال‌شده)، یا یک‌بار toast هنگام اعمال (از `SyncChangeBridge`/`StateChanged`).
  - ناهماهنگ/ردشده: «اعمال روی سرور با خطا مواجه شد؛ نیاز به بررسی.»
- **به‌روز شدن زنده:** به `ISyncStatusService.StateChanged` و `IDataChangeNotifier` گوش بده (الگوی `RefreshOnDataChange`
  در `PageViewModelBase` و `SyncChangeBridge` موجود است) تا بعد از اعمال واقعی، چیپ‌ها/بنر به‌صورت زنده عوض شوند.

**پرفورمنس (الزامی):**
- هرگز per-row به ژورنال کوئری نزن. فقط یک بار per-refresh یک set/شمارش بگیر.
- از subscriptionهای موجود (`StateChanged`, `IDataChangeNotifier`) استفاده کن؛ تایمر/پولینگ جدید اضافه نکن.

### ۳.۴ تصمیم پیاده‌سازی (قبل از کد، در همین تسک نهایی کن)
- ابتدا `CommandJournal` و مدل رکوردش را grep کن (۵ دقیقه، هدفمند). اگر `AggregateId+AggregateType+State` در دسترس بود ⇒
  مسیر «set-per-aggregate» (دقیق‌تر، UX بهتر). اگر نه ⇒ مسیر «بنر سراسری از ISyncStatusService» (سریع، استاندارد، کم‌ریسک).
- هر کدام را انتخاب کردی، **در هر دو جا (تاریخچهٔ اصلی + تاریخچهٔ/لیست فاکتور) یکپارچه** پیاده کن.

### ۳.۵ وضعیت
- [ ] بررسی هدفمند CommandJournal برای تصمیم set-per-aggregate vs بنر سراسری
- [ ] expose وضعیت صف/ناهماهنگ به UI (Contract + Client.Services) بدون per-row query
- [ ] نمایش چیپ/بنر «در صف سرور» و «ناهماهنگ» در تاریخچهٔ اصلی
- [ ] نمایش هماهنگ در لیست/تاریخچهٔ فاکتور
- [ ] به‌روزرسانی زنده با StateChanged/IDataChangeNotifier
- [ ] build سبز → **push**

---

## قوانین اجرا (برای مدل پیاده‌ساز)
1. روی برنچ `genspark_ai_developer` کار کن: اولین push → `scripts/git/push.sh -b genspark_ai_developer "msg"`.
2. **بعد از تکمیل هر تسک** (یا هر فیکس سنگین/کد زیاد حین یک تسک): `scripts/git/push.sh "type(scope): msg"`.
   اگر exit code = 3 ⇒ ابزار `setup_github_environment` را صدا بزن و دوباره اجرا کن.
3. بیلد با `scripts/sandbox/build.sh` (0/0)؛ تست کامل/طولانی نزن. اگر startup/DI لمس شد smoke: `scripts/sandbox/run.sh`.
4. بررسی فایل‌ها فقط هدفمند و بر اساس نیاز؛ بررسی بیهوده ممنوع.
5. **تسک پایانی (بعد از ۱،۲،۳ و pushهاشان):** همین فایل `Plan/TODO.md` را حذف کن و push کن.
   به هیچ فایل دیگری (رودمپ‌ها و ...) دست نزن.

### وضعیت کلی
- [x] تسک ۱ کامل + push
- [ ] تسک ۲ کامل + push
- [ ] تسک ۳ کامل + push
- [ ] حذف `Plan/TODO.md` + push (تسک پایانی)
