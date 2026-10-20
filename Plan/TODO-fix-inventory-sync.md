# TODO — صندوق/صدور فاکتور + پانسیون: خاکستری‌شدن استایل و کسر موجودی انبار

> **سند خودبسنده و دقیق.** تمام بررسی‌ها انجام و نتیجهٔ خالص اینجا نوشته شده. یک مدل تازه می‌تواند بدون
> بررسی دوبارهٔ ریپو مستقیماً پیاده‌سازی را شروع کند. مسیرها و شماره‌خط‌ها در زمان نگارش معتبرند.
>
> **قانون قفل‌شدهٔ پوش:** بعد از تکمیل هر تسک → `scripts/git/push.sh "type(scope): msg"` (کد خروج ۳ = نبود
> کرِدِنشال → ابزار `setup_github_environment` یک‌بار، سپس دوباره اجرا). شاخه کاری: `genspark_ai_developer`.

---

## پس‌زمینه معماری (حقایق تأییدشده — دوباره بررسی نکن)

**مدل موجودی (Stock):** انبار append-only است؛ `StockMovement` تنها مرجع موجودی
(`src/Animora.Domain/Inventory/StockMovement.cs`). `MovementKind`: `Sale`(-), `Consumption`(-, مصرف داخلی),
`AdjustmentIn/Out`, `ReturnIn`(+) و … . قانون علامت: kindهای inbound مثبت، بقیه منفی (توسط `Create` اجبار می‌شود).
`StockMovement.Create(tenantId, productId, variantId, kind, Quantity, unitCost, occurredUtc, recordedByUserId,
… , sourceEntityType, sourceEntityId, operationId, reason, reversesMovementId, id)`.

**کسر موجودی فقط در POST رخ می‌دهد.** `AddInvoiceLineCommand` فقط خط به پیش‌نویس اضافه می‌کند و یک **gate**
موجودی سروری دارد (جمع تعداد همان variant روی فاکتور + درخواستی ≤ OnHandScaled؛ در غیر این صورت
`InsufficientStock`). کسر واقعی در `PostInvoiceCommand` با `MovementKind.Sale` به ازای **هر خطِ** دارای
ProductId/VariantId انجام می‌شود (شامل خطوط پانسیون!). → `src/Animora.Application/UseCases/Sales/PostInvoiceCommand.cs`.

**ویزیت‌ها هنگام افزودن، موجودی کم نمی‌کنند** (در `EncounterCommands.cs` هیچ `StockMovement` نیست). پس تمام مصرف
در POST است. این یعنی تغییر تسک۲ باید **فقط** خطوط `InvoiceLineSource.Stay` محصولی را در POST رد کند، نه Visit/Catalog.

**کاتالوگ پانسیون = همان کاتالوگ فروش.** `BoardingCatalogQueryHandler` از `sales.GetCatalogAsync()` استفاده می‌کند،
پس `CatalogItemId` پانسیون همان id کاتالوگ فروش است و با `ISalesDraftData.FindCatalogEntryAsync(id)` قابل حل است
و می‌دهد: `SalesCatalogEntry(Id, ProductId?, VariantId?, Title, InvoiceLineKind Kind, long PriceMinor, UnitCode,
bool TrackStock, long OnHandScaled)`. → `src/Animora.Application/Ports/ISalesDraftData.cs`.

**On-hand خواندن:** `IInventoryQueries.GetOnHandScaledAsync(productId, variantId, ct)` → long (×1000).
→ `src/Animora.Application/Ports/IInventoryQueries.cs`. مقیاس stock = **1000L**.

**تبدیل handoff پانسیون به فاکتور:** اقلام پانسیون به `InvoiceLine` با `InvoiceLineSource.Stay` و
`catalog?.ProductId/VariantId` تبدیل می‌شوند؛ خطِ هزینهٔ اتاق هم `Source=Stay` ولی `ProductId=null` است (در POST
به‌خاطر چک `ProductId is null` خودبه‌خود رد می‌شود). → `CreateInvoiceFromStayCommand.cs` (SalesHandoffFactory).

**سینک:** `StockMovement` از مسیر **command journal** (جرنال دستورات) رپلیکیت می‌شود، نه pull-feed. پس حرکتِ جدیدِ
پانسیون هم مثل POST رپلیکیت می‌شود به‌شرطی که id آن **قطعی (deterministic)** باشد:
`IdGenerator.ForOperation(context.OperationId, ordinal, clock.UtcNow)` — تا replay دوبار ننویسد. خودِ stay از pull-feed
می‌آید. → `Plan/reports/06-sync-system.md`.

**قفل سروری همزمانی مالی:** `ServerFinancialConcurrencyGuard.PrepareAsync` به ازای دستورهای دارای خط فاکتور،
`stock:{ProductId}:{VariantId}` را قفل می‌کند. وقتی `AddStayItemCommand` موجودی را لمس کند، باید قفل stock برای آن هم
گرفته شود تا oversell همزمان رخ ندهد. → `src/Animora.Server/Commands/ServerFinancialConcurrencyGuard.cs`.

---

## T1 — استایل خاکستری نشدنِ محصولِ موجودی=۱ در «صندوق و صدور فاکتور» 🟥

**محل بروز (تأییدشده):** صفحهٔ **صندوق** (Cashier) که از یک **ComboBox/دراپ‌داون** استفاده می‌کند، نه از
CatalogModal. (صفحهٔ Sales از `CatalogModalViewModel` با `_sessionAdded` استفاده می‌کند و در یک session خاکستری
می‌شود؛ مشکل گزارش‌شدهٔ کاربر مربوط به دراپ‌داون صندوق است.)

**ریشهٔ دقیق مشکل:**
- `CashierCatalogOptionViewModel` (در `src/Animora.Desktop/Pages/Cashier/CashierItemViewModels.cs` خطوط ۲۷۴–۲۹۸):
  `IsOutOfStock` و `AvailableUnits` فقط **getter ثابت** هستند که در سازنده از `item.IsOutOfStock`/`AvailableUnits`
  ست می‌شوند. `IsOutOfStock` روی on-hand **فیزیکی** محاسبه می‌شود (`CashierCatalogItem.IsOutOfStock => Kind==Item
  && OnHand<=0` در `src/Animora.Contracts/Cashier/CashierModels.cs`).
- چون `AddInvoiceLineCommand` موجودی فیزیکی را کم نمی‌کند (کسر فقط در POST)، بعد از افزودن محصولِ موجودی=۱ به
  پیش‌نویس، on-hand فیزیکی هنوز ۱ است → `IsOutOfStock=false` → گزینه در دراپ‌داون خاکستری نمی‌شود.
- AXAML (`CashierPageView.axaml` ~خط ۶۴۴): `<Border Classes="stock-option" Classes.out="{Binding IsOutOfStock}" …>`
  و استایل `Border.stock-option.out` (خطوط ۲۸۲/۲۸۵). پس خاکستری‌شدن کاملاً به مقدار **زندهٔ** `IsOutOfStock` وابسته است.
- منطق «باقی‌مانده» از قبل درست محاسبه می‌شود ولی فقط برای گزینهٔ انتخاب‌شده:
  `SelectedRemainingUnits()` = `AvailableUnits - (جمع تعداد خطوط هم‌عنوان روی فاکتور)`. اما این فقط دکمهٔ Add را
  غیرفعال می‌کند؛ ظاهرِ **گزینه‌های دراپ‌داون** زنده نمی‌شود.

**راه‌حل (کم‌هزینه، هماهنگ با طراحی موجود):** `IsOutOfStock` گزینه را **زنده** کن و بعد از هر تغییرِ فاکتور همهٔ
گزینه‌ها را نسبت به خطوط فعلی فاکتور به‌روز کن.

گام‌ها:
1. در `CashierItemViewModels.cs` → `CashierCatalogOptionViewModel`:
   - کلاس را `ObservableObject`/پیاده‌سازیِ `INotifyPropertyChanged` کن (الگوی بقیهٔ VMها در همین فایل را ببین).
   - `IsOutOfStock` را به propertyِ قابل‌تغییر (با raise تغییر) تبدیل کن؛ مقدار اولیه = `item.IsOutOfStock`.
   - یک متد `RefreshStock(int unitsOnInvoice)` اضافه کن: اگر `!TracksStock` → هیچ؛ وگرنه
     `IsOutOfStock = (AvailableUnits - unitsOnInvoice) <= 0`. (نکته: `AvailableUnits` برای سرویس/غیردنبال‌شونده
     `int.MaxValue` است، پس هرگز out نمی‌شود.)
2. در `CashierPageViewModel.cs`:
   - یک متد خصوصی `RefreshCatalogAvailability()` بساز که روی تمام `CatalogOptions` حلقه بزند و برای هر گزینه،
     جمع تعداد خطوط هم‌عنوانِ فاکتور را (مثل منطق `SelectedRemainingUnits()`: `line.Kind==CashierLineKind.Item &&
     line.Title==option.Title`) حساب و `option.RefreshStock(onInvoice)` را صدا بزند. از `Detail?.Detail.Lines`
     استفاده کن (می‌تواند یک helper مشترک با `SelectedRemainingUnits` داشته باشد).
   - این متد را در انتهای `ShowDetail(detail)` (خط ~۴۳۰) و نیز پس از `ReloadCatalogOptionsAsync()` صدا بزن (چون
     ReplaceAll گزینه‌های تازه می‌سازد که دوباره باید سنجیده شوند).
3. مطمئن شو بعد از add موفق، چون `RunMutationAsync` → `ShowDetail(updated)` صدا می‌خورد، availability خودکار
   به‌روز می‌شود. (نیازی به reload کامل کاتالوگ بعد از هر add نیست؛ فقط RefreshCatalogAvailability کافی است.)

**مسیرهای مشابه که باید چک شوند (طبق خواستهٔ کاربر «بخش‌های مشابه هم این مشکل نباشد»):**
- صفحهٔ **Sales** از `CatalogModalViewModel` استفاده می‌کند که `_sessionAdded`/`RemainingUnits` دارد و در یک session
  خاکستری می‌شود → **درست است**، فقط اگر مودال بین add و re-open بسته/باز شود `AvailableUnits` فیزیکی دوباره کامل
  نشان داده می‌شود؛ ولی چون gate سروری add را رد می‌کند oversell رخ نمی‌دهد. **تغییر لازم نیست** مگر رفتار بصری
  مطلوب نباشد؛ در این تسک فقط صندوق را درست کن و این را فقط یادداشت بگذار.

**وضعیت:** [x] انجام شد — `CashierCatalogOptionViewModel` حالا `ObservableObject` با `[ObservableProperty] IsOutOfStock`
و متد `RefreshStock(unitsOnInvoice)`؛ در `CashierPageViewModel` متد `RefreshCatalogAvailability()` + helper
`UnitsOnInvoice(title)` اضافه و در `ShowDetail` و انتهای `ReloadCatalogOptionsAsync` صدا زده شد. (بیلد محلی: SDK
نصب نیست؛ طبق قانونِ منع بیلد طولانی، صحت با بازبینی کد تأیید شد — CI بیلد می‌کند.)
**پوش بعد از اتمام:** `scripts/git/push.sh "fix(cashier): live grey-out of catalog options when draft consumes the last unit"`

---

## T2 — کسر موجودی انبار هنگام افزودن کالا به پانسیون (و جلوگیری از کسر دوباره در تسویه) 🟥

**ریشهٔ دقیق مشکل:** `AddStayItemCommandHandler(IBoardingRepository, IBoardingQueries, IClock)`
(در `src/Animora.Application/UseCases/Boarding/BoardingCommands.cs` خط ~۱۵۴) **هیچ وابستگی موجودی ندارد** و فقط
`stay.AddItem(item)` را صدا می‌زند. هیچ gate و هیچ کسری نیست → همان کالا بی‌نهایت‌بار قابل افزودن است و موجودی
انبار ثابت می‌ماند. `RemoveStayItemCommandHandler` هم فقط `stay.RemoveItem` را صدا می‌زند.

**رفتار مطلوب کاربر (تأییدشده):**
1. افزودن کالای محصولی به یک stay → **همان لحظه** موجودی انبار کم شود (+ gate ضدِ oversell).
2. حذف همان قلم از stay → موجودی برگردد.
3. در تسویه/POST فاکتورِ پانسیون → **دوباره کم نشود** (چون قبلاً در افزودن کم شده).

> نکتهٔ کلیدی برای جلوگیری از دو‌بار کسر: در `PostInvoiceCommand` خطوطِ محصولیِ با منبع `InvoiceLineSource.Stay`
> باید هم در preflight و هم در حلقهٔ ساخت movement **رد** شوند. خطوط Visit و Catalog مثل قبل در POST کم شوند.
> فقط Stay از قبل کم شده. خطِ اتاق (ProductId=null) خودبه‌خود رد می‌شود.

### گام‌های پیاده‌سازی

**۲-الف) `AddStayItemCommandHandler` — کسر هنگام افزودن + gate.** (BoardingCommands.cs)
- وابستگی‌ها را بیفزا: `ISalesDraftData data, IInventoryQueries inventory, IStockMovementRepository movements`
  (علاوه بر موجودها). امضای مشابه را از `AddInvoiceLineCommandHandler` و `PostInvoiceCommandHandler` الگو بگیر.
- بعد از تعیین `kind`: اگر `kind == StayItemKind.Product`:
  - `var entry = await data.FindCatalogEntryAsync(c.CatalogItemId, ct);`
  - اگر `entry is { TrackStock: true, ProductId: not null, VariantId: not null }`:
    - **gate:** `var onHand = await inventory.GetOnHandScaledAsync(entry.ProductId, entry.VariantId, ct);`
      `var needScaled = (long)Math.Ceiling(c.Quantity * 1000L);` اگر `needScaled > onHand` →
      `return Result<Unit>.Fail(ErrorCodes.InsufficientStock);`
    - **movement (قطعی):** با `MovementKind.Consumption` و مقدار **منفی**:
      ```
      var mv = StockMovement.Create(context.TenantId, entry.ProductId, entry.VariantId,
          MovementKind.Consumption, new Quantity(-c.Quantity, entry.UnitCode ?? "PCS"),
          Money.Irt(0), clock.UtcNow, context.UserId,
          sourceEntityType: "BoardingStay", sourceEntityId: c.StayId,
          operationId: context.OperationId,
          id: IdGenerator.ForOperation(context.OperationId, 1, clock.UtcNow));
      if (mv.IsFailure) return Result<Unit>.Fail(mv.Error, mv.Detail);
      movements.Add(mv.Value!);
      ```
      (ordinal=1 چون ordinal=0 برای id خودِ StayItem استفاده شده: `IdGenerator.ForOperation(context.OperationId,
      0, clock.UtcNow)`. تداخل id ایجاد نکن.)
  - **مهم:** کسر را **قبل** از `stay.AddItem` انجام بده یا بعدش ولی قبل از return موفق؛ اگر `AddItem` شکست خورد،
    movement نباید نوشته شود (چون UoW تراکنشی است، return Fail قبل از commit کافی است — ترتیب را طوری بگذار که
    اگر AddItem fail شد، Fail برگردانی و movement نوشته‌شده در همان تراکنش rollback شود). ساده‌ترین: اول `AddItem`
    را بساز و نتیجه را چک کن، اگر موفق بود gate+movement را اجرا کن، سپس Ok برگردان.
  - هزینهٔ واحد: `Money.Irt(0)` مثل تعدیل داخلی کافی است (مصرف داخلی؛ COGS پانسیون جداگانه در صورت‌حساب لحاظ می‌شود).

**۲-ب) `RemoveStayItemCommandHandler` — بازگرداندن موجودی.** (BoardingCommands.cs)
- وابستگی‌ها: `ISalesDraftData data, IStockMovementRepository movements, IClock clock` بیفزا.
- `current` همان قلم حذفی است؛ `current.Kind == StayItemKind.Product` و `current.CatalogItemId` موجود است
  (نام فیلد را در `src/Animora.Domain/Boarding/StayItem.cs` تأیید کن — StayItem دارای `CatalogItemId` و `Quantity`
  و `Kind` است).
- اگر محصولی بود: `entry = await data.FindCatalogEntryAsync(current.CatalogItemId, ct)` و اگر
  `TrackStock && ProductId!=null && VariantId!=null`، یک movement **برگشتی مثبت** بنویس:
  `MovementKind.ReturnIn` با مقدار `+current.Quantity.Value` و
  `reversesMovementId:` نیازی نیست دقیقاً به id حرکت افزودن اشاره کنی (در دسترس نیست)، ولی برای ردیابی بهتر می‌توان
  گذاشت؛ ساده‌ترین: `ReturnIn` با id قطعی `IdGenerator.ForOperation(context.OperationId, 1, clock.UtcNow)` و
  `sourceEntityType:"BoardingStay", sourceEntityId:c.StayId`.
  - علامت `ReturnIn` باید **مثبت** باشد (inbound) وگرنه `MOVEMENT_SIGN_INVALID`.

**۲-ج) `PostInvoiceCommand` — رد کردن خطوط Stayِ محصولی.** (PostInvoiceCommand.cs)
- در **هر دو** حلقه (preflight خطوط ۶۷–۸۳ و حلقهٔ ساخت movement خطوط ۱۰۶–۱۱۷)، شرط رد را اضافه کن:
  ```
  if (line.ProductId is null || line.VariantId is null) continue;
  if (line.Source == InvoiceLineSource.Stay) continue; // قبلاً هنگام افزودن به پانسیون مصرف شده
  ```
  - دقت: `InvoiceLine.Source` را از `src/Animora.Domain/Sales/InvoiceLine.cs` (enum `InvoiceLineSource`) بخوان؛
    مقادیر: `Catalog`, `Visit`, `Stay` (و احتمالاً بیشتر). فقط `Stay` را رد کن.
  - این تضمین می‌کند تسویهٔ پانسیون موجودی را دوباره کم نمی‌کند و oversold preflight هم برای این خطوط فعال نمی‌شود.

**۲-د) ثبت وابستگی‌های DI.** هندلرها با DI ساخته می‌شوند. چون سازندهٔ هندلرها پارامترهای جدید گرفت، مطمئن شو
`ISalesDraftData`, `IInventoryQueries`, `IStockMovementRepository` در کانتینر ثبت‌اند (قطعاً هستند؛ از
PostInvoice/AddInvoiceLine استفاده می‌شوند). نیازی به ثبت دستی هندلر نیست اگر از اسکن خودکار استفاده می‌شود؛ بررسی
کوتاه: همان‌طور که `AddInvoiceLineCommandHandler(IInvoiceRepository, ISalesDraftData)` کار می‌کند، اینجا هم کار می‌کند.

**۲-ه) قفل همزمانی سروری.** در `src/Animora.Server/Commands/ServerFinancialConcurrencyGuard.cs` (متد
`PrepareAsync`، بخش ساخت لیست `stock`)، برای `AddStayItemCommand` قفل `stock:{ProductId}:{VariantId}` را اضافه کن.
چون در Guard فقط `CatalogItemId` در دست است و نه ProductId/VariantId، ساده‌ترین و امن‌ترین کار: برای
`AddStayItemCommand` و `RemoveStayItemCommand` قفل را بر پایهٔ **CatalogItemId** بگیر (`stock-cat:{CatalogItemId}`)
یا اگر الگوی قبلی ProductId می‌خواهد، از `db` برای resolve استفاده کن (الگوی `invoice` در همان فایل خطوط ۴۱–۴۶ را
ببین که از `db` می‌خواند). **اگر پیچیده شد و وقت‌گیر بود، این گام (۲-ه) را به‌عنوان بهبود اختیاری علامت بزن و در
متنِ commit ذکر کن** — چون gate اپلیکیشنی (۲-الف) اصل محافظت است و قفل سروری فقط race هم‌زمان را می‌بندد.

### سینک (خواستهٔ کاربر #۳)
- `StockMovement`های جدید (Consumption/ReturnIn) چون id قطعی دارند، از طریق command journal به بقیهٔ کلاینت‌ها
  رپلیکیت می‌شوند — مثل POST. خودِ stay از pull-feed می‌آید. پس هماهنگی حفظ می‌شود. **هیچ کار اضافهٔ سینک لازم نیست**
  به‌شرط رعایت id قطعی.
- چون هندلرِ Application مشترک است، همین کسر روی **سرور** هم اجرا می‌شود (دستور BoardingItemAdd در سرور هم همین
  هندلر را دارد) → سازگاری کلاینت/سرور حفظ می‌شود.

**وضعیت:** [ ] انجام نشده
**پوش بعد از اتمام:** `scripts/git/push.sh "feat(boarding): consume warehouse stock on add-item (gate+movement), restore on remove, skip double-decrement at post"`

---

## بیلد و تست (هدفمند — تست کامل/طولانی ممنوع)
- [ ] بیلد پروژه‌های متأثر فقط: `dotnet build src/Animora.Application` و `src/Animora.Desktop` و `src/Animora.Server`
  (یا `dotnet build Animora.sln -clp:ErrorsOnly` اگر سریع‌تر بود). از full test suite پرهیز کن.
- اگر بیلد خطا داد، فقط همان را رفع کن.

## تسک پایانی (الزامی)
- [ ] **T-FINAL:** پس از اتمام و پوش T1 و T2، **فقط همین فایل** (`Plan/TODO-fix-inventory-sync.md`) را حذف کن.
  به هیچ فایل دیگری در `Plan/` (گزارش‌ها) و هیچ فایل roadmap دست نزن.
  سپس: `scripts/git/push.sh "chore(plan): remove completed TODO plan"`
