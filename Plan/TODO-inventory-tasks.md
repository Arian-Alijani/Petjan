# TODO — بخش «انبار و کالا»: سه تسک (legacy note / خط جداکننده سطر / سیستم اسکنر بارکد)

> **برای مدلی که این فایل را در یک سشن تازه می‌خواند:** تمام بررسی‌ها، مسیرها، شماره خطوط،
> و تصمیم‌های معماری در همین فایل آمده است. نیازی به کاوش دوبارهٔ ریپو نیست؛ مستقیم سراغ
> پیاده‌سازی برو. فقط فایل‌های ذکرشده را باز کن (شماره خطوط ممکن است با ادیت‌های قبلی چند خط جابجا شده باشد — با `grep` دوباره لنگر بگیر).
>
> **قانون قفل‌شدهٔ پوش (حیاتی):** بعد از اتمام هر تسک این لیست، حتماً اجرا کن:
> ```
> scripts/git/push.sh "type(scope): message"
> ```
> اگر exit code = 3 بود، ابزار `setup_github_environment` را یک‌بار صدا بزن و دوباره اجرا کن.
> اگر حین یک تسک کد سنگین/زیادی نوشتی، همان‌جا هم یک پوش میانی با پیام واضح بزن.

---

## زمینه / درک محصول (خلاصهٔ خالص)

- Animora = نرم‌افزار مدیریت کلینیک دامپزشکی + حسابداری، SaaS چندمستأجری. کلاینت دسکتاپ
  **Avalonia + .NET 10 + MVVM (CommunityToolkit.Mvvm)**، فارسی/RTL، offline-first روی SQLite محلی.
- الگوی لایه‌ها برای هر feature: **Domain** (entity/VO) → **Application** (Command/Query + validation)
  → **Contracts** (DTO/interface سیم بین UI و سرویس) → **Persistence.Sqlite** (EF + Query) →
  **Client.Services** (پیاده‌سازی Contracts روی Application+Persistence + Mapping) → **Desktop** (UI/VM).
- کدِ تحت کار فقط `src/` است. `Animora-UI/` (رفرنس HTML) را باز نکن. تست‌های full/build طولانی ممنوع.

### فایل‌های کلیدی بخش انبار/کالا (کشف‌شده)
- UI لیست محصولات: `src/Animora.Desktop/Pages/Inventory/InventoryPageView.axaml`
  - `ListBox Classes="records" ItemsSource="{Binding FilteredProducts}"` (حدود خط ۵۳۷)
  - قالب سطر: `DataTemplate x:DataType="inv:InventoryProductRowViewModel"` → ریشه‌اش یک `<StackPanel>` با
    `<Button Classes="prow">` و داخلش `<Grid ColumnDefinitions="32,3*,1.1*,1.1*,0.9*,1.3*,1.4*,1.1*,112">` (حدود خط ۵۴۶).
  - بخش variant گسترش‌یافته: `Border IsVisible="{Binding IsExpanded}"` (حدود خط ۶۶۸).
- Modalهای محصول:
  - `ProductModalView.axaml` + `ProductModalViewModel.cs` (ویرایش محصول موجود؛ تب‌ها، variantها، تاریخچه)
  - `NewProductModalView.axaml` + `NewProductModalViewModel.cs` (ساخت محصول جدید)
  - `ProductModalItemViewModels.cs` (شامل `InventoryVariantRowViewModel` و سطرهای زیرمجموعه)
- Contracts انبار: `src/Animora.Contracts/Inventory/InventoryModels.cs`, `IInventoryQueryService.cs`
- Client.Services انبار: `src/Animora.Client.Services/Inventory/InventoryQueryService.cs` + `InventoryMapping.cs`
- Application commands انبار: `src/Animora.Application/UseCases/Inventory/InventoryCommands.cs`
- Domain: `src/Animora.Domain/Inventory/Product.cs`, `ProductVariant.cs`
- Query محلی: `src/Animora.Persistence.Sqlite/Queries/InventoryQueries.cs`, `SalesQueries.cs`
- فروش/فاکتور: `src/Animora.Desktop/Pages/Sales/CatalogModalViewModel.cs` + `CatalogModalView.axaml`,
  `SalePageViewModel.cs` + `SalePageView.axaml`
- Contracts فروش: `src/Animora.Contracts/Sales/SalesModels.cs` (`CatalogItem`), `ISalesQueryService.cs`
- DI دسکتاپ: `src/Animora.Desktop/App.axaml.cs` (الگو: `services.AddSingleton<IFoo, Foo>()` حول خط ۳۹۰–۴۷۰)

---

## تسک ۱ — رفع «legacy: {"fInActive":false,"fIsDrugStore":0}» در توضیحات محصول

### ریشهٔ مشکل (کشف‌شده — قطعی)
- فیلد Description محصول در UI از `Product.Description` می‌آید (در
  `ProductModalViewModel.cs:451` → `Description = _product.Description;` و در
  contract `Product.Description`). این خودش از **notes محصول هنگام ایمپورت** پر می‌شود.
- منبع آلودگی: `src/Animora.Persistence.PostgreSql/Operator/ClinicBundleImportService.cs`
  - متد helper: `private static string? LegacyNote(Dictionary<string, JsonElement>? legacy) => legacy is { Count: > 0 } ? "legacy: " + JsonSerializer.Serialize(legacy) : null;` (حدود خط ۱۶۳۷).
  - در حلقهٔ ایمپورت محصولات (`// 5. Products` حدود خط ۴۱۹–۴۷۷): `var notes = LegacyNote(prod.Legacy);` (خط ۴۳۵)
    و این `notes` مستقیماً به `Product.Create(..., notes: notes, ...)` و `existing.Describe(name, brand, notes)` می‌رود.
  - برای محصولات، `prod.Legacy` دیکشنری‌ای شامل کلیدهای `fInActive` و `fIsDrugStore` از SQL Server قدیمی است.
    این دو **معنا دارند** و نباید خام در توضیحات بریزند:
    - `fInActive` (bool): اگر `true` → محصول غیرفعال است.
    - `fIsDrugStore` (int 0/1): `1` → دارو (`ProductKind.Medicine`)؛ `0` → کالای فروشگاهی (`ProductKind.PetShop`).
- DTO محصول: `ClinicImportProduct` (حدود خط ۱۷۳۹) دارای `public Dictionary<string, JsonElement>? Legacy { get; set; }`.
- enum دامنه: `src/Animora.Domain/Inventory/Product.cs` → `Medicine=0, Vaccine=1, Food=2, PetShop=3, Consumable=4, Equipment=5, Service=6`.
- `Product.Create(...)` پارامتر `ProductKind kind` می‌گیرد؛ متد `Deactivate()` موجود است (خط ۳۳۳) و `IsActive` قابل تنظیم.

### کاری که باید انجام شود (پیاده‌سازی استاندارد)
در `ClinicBundleImportService.cs` حلقهٔ محصولات (بخش «5. Products»):
1. قبل از ساخت `notes`، کلیدهای شناخته‌شدهٔ `fInActive` و `fIsDrugStore` را از `prod.Legacy` **استخراج و حذف** کن
   تا در LegacyNote نیایند. یک helper کوچک بساز، مثلاً:
   - `bool ExtractBool(dict, key, default)` و `int ExtractInt(dict, key, default)` که مقدار را می‌خوانند و
     کلید را از دیکشنری `Remove` می‌کنند (روی یک کپی یا روی خود `prod.Legacy` چون بعد از این استفادهٔ دیگری ندارد).
   - با `JsonElement` بخوان: برای bool از `.ValueKind == JsonValueKind.True`؛ برای int از `.TryGetInt32(out var n)`.
2. `var isActive = !ExtractBool(prod.Legacy, "fInActive", false);`
   `var kind = ExtractInt(prod.Legacy, "fIsDrugStore", 0) == 1 ? ProductKind.Medicine : ProductKind.PetShop;`
3. سپس `var notes = LegacyNote(prod.Legacy);` اجرا شود — حالا دیکشنری تمیز است؛ اگر خالی شد، `LegacyNote` به‌درستی
   `null` برمی‌گرداند (چون شرط `Count > 0` دارد) و توضیحات خالی/بدون «legacy:…» می‌ماند.
4. `kind` را به‌جای `ProductKind.Medicine` ثابت در `Product.Create(...)` پاس بده (خط ~۴۶۱).
5. اعمال `isActive`: اگر `false` بود، بعد از ساخت موفق `created.Value!.Deactivate()` را صدا بزن
   (و برای مسیر `existing` نیز بر اساس isActive وضعیت را هماهنگ کن — اگر API `Activate()/Deactivate()` موجود است
   از همان استفاده کن؛ متد `Activate` حدود خط ۳۴۲ در `Product.cs` هست با نام احتمالی مشابه — با grep تأیید کن).
6. **نکتهٔ مهم backward-compat:** چون ممکن است محصولاتی که قبلاً ایمپورت شده‌اند توضیحات آلوده داشته باشند،
   این فیکس فقط ایمپورت‌های جدید را تمیز می‌کند. اگر نیاز به پاک‌سازی دادهٔ موجود بود، در scope این تسک نیست
   (تسک فقط «نوشته شدن پیش‌فرض» را هدف گرفته = مسیر ایمپورت). محدوده را به همین مسیر ایمپورت نگه دار.

### تأیید / تست سبک
- بیلد فقط پروژهٔ `Animora.Persistence.PostgreSql` (نه full solution):
  `cd /home/user/webapp && dotnet build src/Animora.Persistence.PostgreSql/Animora.Persistence.PostgreSql.csproj -clp:ErrorsOnly`
- اگر تست واحد مرتبط برای import موجود بود (`tests/` را grep کن: `ClinicBundleImport`)، همان یک فایل تست را اجرا کن؛
  در غیر این‌صورت فقط بیلد کافی است. تست طولانی/full ممنوع.

### وضعیت: [x] انجام شد — helperهای ExtractLegacyBool/Int اضافه و حلقهٔ محصولات اصلاح شد؛ build PostgreSql موفق (0 error).
### پوش بعد از اتمام: `scripts/git/push.sh "fix(import): map legacy fInActive/fIsDrugStore to product active-state/kind instead of dumping raw json into description"`

---

## تسک ۲ — خط جداکنندهٔ نازک بین سطرهای محصول

### زمینه (کشف‌شده)
- در `InventoryPageView.axaml`، قالب سطر محصول ریشه‌اش `<StackPanel>` است (حدود خط ۵۴۲) و داخلش
  `<Button Classes="prow">` با `<Grid ...>` سطر اصلی و یک `Border IsVisible="{Binding IsExpanded}"` برای variantها.
- سطرها در `ListBox Classes="records"` رندر می‌شوند؛ استایل‌های `ListBox.records ListBoxItem` همهٔ
  border/hover پیش‌فرض را صفر کرده‌اند (حدود خط ۳۳۶–۳۵۲)، پس هیچ جداکنندهٔ بصری بین سطرها نیست و سطرها به هم می‌چسبند.
- برند/رنگ‌ها: منابع استاتیک موجود مثل `{StaticResource LineBrush}` و `{StaticResource Line2Brush}` در همین فایل
  استفاده شده‌اند (مثلاً خط ۴۶۲, ۵۷۴) — برای خط جداکننده از `LineBrush` (نازک‌ترین/کم‌رنگ‌ترین) استفاده کن تا با تم هماهنگ بماند.

### کاری که باید انجام شود
گزینهٔ تمیز و MVVM-friendly (بدون دستکاری ViewModel): یک `Border` نازک جداکننده در پایین قالب هر سطر اضافه کن.
- در `DataTemplate` سطر محصول، داخل همان `<StackPanel>` ریشه و **بعد از** `Border` مربوط به variantهای گسترش‌یافته،
  یک جداکننده اضافه کن:
  ```xml
  <Border Height="1" Background="{StaticResource LineBrush}" Margin="12,0" />
  ```
  - `Margin="12,0"` دو طرف کمی تورفتگی می‌دهد تا خط «نازک و ظریف» دیده شود، نه لبه‌به‌لبه.
  - ارتفاع `1` = خط نازک. اگر در DPIهای بالا محو شد، می‌توان `1` را نگه داشت (Avalonia به DIP رندر می‌کند).
- اگر نمی‌خواهی زیر آخرین سطر هم خط بیفتد، چون داخل ListBox است و معمولاً اشکالی ندارد، ساده نگه‌دار و همهٔ سطرها
  یک جداکننده پایین داشته باشند (استاندارد لیست‌ها). اگر ظاهر بهتر خواستی می‌توان جداکننده را «بالا»ی هر سطر گذاشت
  به‌جز اولی، ولی این به ایندکس نیاز دارد؛ سادگی (جداکنندهٔ پایین هر سطر) ترجیح دارد.

### تأیید / تست سبک
- چون فقط XAML است، بیلد پروژهٔ `Animora.Desktop`:
  `cd /home/user/webapp && dotnet build src/Animora.Desktop/Animora.Desktop.csproj -clp:ErrorsOnly`
  (کامپایل XAML خطای ساختاری را می‌گیرد). اجرای GUI لازم نیست.

### وضعیت: [x] انجام شد — Border جداکنندهٔ 1px با LineBrush در انتهای قالب سطر اضافه شد.
### پوش بعد از اتمام: `scripts/git/push.sh "feat(inventory-ui): add thin divider line between product rows for readability"`

---

## تسک ۳ — سیستم اسکنر بارکد یکپارچه (مهم‌ترین و بزرگ‌ترین تسک)

### وضعیت فعلی بارکد در کدبیس (کشف‌شده — قطعی)
- **Domain:** `ProductVariant` دارای فیلد `string? Barcode` است (`src/Animora.Domain/Inventory/ProductVariant.cs:37`,
  کامنت: "Scanner barcode; null when the variant has none").
- **Application:** commandها بارکد را می‌پذیرند:
  - `CreateProductCommand` → ورودی variantها `Barcode = null` (`InventoryCommands.cs:30`)
  - `UpdateProductInfoCommand` → بارکد در ساخت/به‌روزرسانی variant پاس می‌شود (خطوط ۲۳۶, ۲۴۵)
  - `AddProductVariantCommand` → `string? Barcode = null` (خط ۲۷۷)
- **ReadModel Application:** `InventoryReadModels.cs:16` فیلد `string? Barcode` دارد.
- **Persistence.Sqlite:** `InventoryQueries.cs:238` بارکد را به read-model map می‌کند (`v.Barcode, v.IsActive, scaled`).
- **اما Contract DTO ناقص است:** `src/Animora.Contracts/Inventory/InventoryModels.cs` →
  رکورد `ProductVariant` (خط ۵۶) فیلد **Barcode ندارد**. یعنی بارکد از دامنه تا UI سیم نشده.
- **UI محصول بارکد ندارد:** هیچ ورودی/نمایش بارکد در `ProductModalView.axaml`, `NewProductModalView.axaml`,
  `ProductModalViewModel.cs`, `NewProductModalViewModel.cs`, `ProductModalItemViewModels.cs` نیست (grep خالی برگشت).
- **فروش/فاکتور:** `CatalogItem` (`SalesModels.cs:284`) بارکد ندارد. catalog با `VariantId` کلید می‌شود
  (`SalesQueries.cs:369-385` → `SalesCatalogItemReadModel(v.VariantId, ...)`). `ISalesQueryService.GetCatalogAsync()`
  کل catalog را با cap می‌دهد؛ `AddLineAsync(invoiceId, CatalogItem item, int qty)` خط اضافه می‌کند.
  `CatalogModalViewModel` جستجوی متنی دارد (`_all`, `_visible`) ولی matching بارکد ندارد.

### معماری هدف (تصمیم‌گرفته‌شده — این را پیاده کن)
یک «سیستم اسکنر» استاندارد، لایه‌لایه، با سه نقطهٔ ورود کاربر:

#### (الف) زیرساخت تشخیص اسکنر — سرویس keyboard-wedge (استاندارد صنعتی)
اسکنرهای بارکد USB رایج به‌صورت **HID keyboard-wedge** عمل می‌کنند: کاراکترها را خیلی سریع تایپ می‌کنند و با
`Enter` تمام می‌کنند. تشخیص استاندارد = «رشته‌ای از keypressها با فاصلهٔ زمانی بسیار کوتاه که به Enter ختم می‌شود».
- یک سرویس دسکتاپ بساز: `src/Animora.Desktop/Services/BarcodeScannerService.cs`
  - کلاس `BarcodeScannerService` (singleton) که یک رویداد `event Action<string>? BarcodeScanned;` دارد.
  - متد کمکی برای اتصال به یک `InputElement`/پنجره: `IDisposable Attach(InputElement target)` که
    به `KeyDown` گوش می‌دهد (Avalonia: `target.AddHandler(InputElement.KeyDownEvent, handler, RoutingStrategies.Tunnel)`).
  - منطق تشخیص: یک `StringBuilder buffer` + timestamp آخرین کلید. اگر فاصلهٔ بین کلیدها `< ~50ms` بود، کاراکتر را
    به buffer اضافه کن؛ اگر فاصله زیاد شد buffer را ریست کن. وقتی `Key.Enter` آمد و `buffer.Length >= حداقل (مثلاً ۴)`
    و میانگین فاصله‌ها کوتاه بود → این یک «اسکن» است: `BarcodeScanned?.Invoke(buffer.ToString())` و رویداد را
    `handled` کن تا به کنترل متنی نشت نکند. اگر کند تایپ شد (انسان) → دخالت نکن.
  - آستانه‌ها را ثابت و خوانا کن: `const int MaxInterKeyMs = 50; const int MinLength = 3;`. کاراکترها را از
    `KeyEventArgs` با `e.KeySymbol` یا نگاشت `e.Key` به رقم/حرف بگیر (بارکدها معمولاً رقمی‌اند؛ رقم‌ها Key.D0..D9
    و NumPad0..9).
  - این سرویس مستقل از UI است و در چند صفحه قابل استفاده (انبار + فروش).
- در `App.axaml.cs` ثبت DI: `services.AddSingleton<BarcodeScannerService>();` (الگوی سایر singletonها حول خط ۳۹۵).

> نکته: اگر پیاده‌سازی keyboard-wedge پیچیده شد، یک مسیر سادهٔ قابل‌قبول هم این است که سرویس صرفاً یک API
> «شروع ضبط / پایان» ارائه دهد و تشخیص سرعت را روی همان TextBox بارکد انجام دهی. اما مسیر ترجیحی همان
> سرویس مرکزی بالاست تا در فروش هم بدون فوکوس روی فیلد خاص کار کند.

#### (ب) بارکد هنگام ساخت/ویرایش محصول (سیم‌کشی کامل لایه‌ها)
1. **Contract:** به رکورد `ProductVariant` در `InventoryModels.cs` فیلد `string? Barcode = null` اضافه کن
   (آخر لیست پارامترها، با مقدار پیش‌فرض تا سازگاری حفظ شود). همین رکورد در `NewProductRequest` و
   `ProductInfoUpdate` به‌عنوان لیست variant استفاده می‌شود، پس بارکد خودکار منتقل می‌شود.
2. **Client.Services / Mapping:** در `InventoryMapping.cs` هر جا `contract ProductVariant` ساخته می‌شود،
   `Barcode` را از read-model (`InventoryReadModels` که بارکد دارد) پر کن؛ و هر جا از contract به ورودی command
   تبدیل می‌شود، `Barcode` را پاس بده. (در `InventoryQueryService.cs` متدهای `SaveProductInfoAsync` و
   `CreateProductAsync` → ورودی commandها را با `Barcode` پر کن. commandها از قبل فیلد دارند.)
   - با grep لنگر بگیر: `grep -n "ProductVariant\|Barcode\|new Animora.Application" src/Animora.Client.Services/Inventory/*.cs`
3. **Query:** `InventoryQueries.cs:238` از قبل `v.Barcode` را به read-model می‌دهد — تأیید کن که read-model
   مصرف‌کننده در mapping بارکد را به contract می‌رساند.
4. **UI — ویرایش محصول (`ProductModalView.axaml` + `ProductModalItemViewModels.cs`):**
   - در `InventoryVariantRowViewModel` یک `[ObservableProperty] string? _barcode;` اضافه کن و در بارگذاری از
     contract variant مقداردهی کن؛ در ذخیره (ساخت `ProductInfoUpdate`) آن را برگردان.
     (محل مقداردهی: `ProductModalViewModel.cs` حوالی خط ۴۵۱ که فیلدها از `_product` پر می‌شوند و خط ~۶۱۹ که
     `Description.Trim()` و variantها برای ذخیره بسته می‌شوند.)
   - در XAML سطر variant، یک `TextBox` بارکد با placeholder «بارکد» اضافه کن، کنارش یک دکمهٔ کوچک «اسکن»
     (آیکن) که وقتی زده شد فوکوس را به همان TextBox می‌دهد و با سرویس اسکنر مقدار را می‌گیرد — یا ساده‌تر:
     فیلد همیشه آمادهٔ اسکن است (keyboard-wedge خودش داخل TextBox تایپ می‌کند و Enter می‌زند؛ کافی است فوکوس آنجا باشد).
     برای UX هماهنگ، از همان استایل‌های input موجود در این فایل استفاده کن.
5. **UI — ساخت محصول (`NewProductModalView.axaml` + `NewProductModalViewModel.cs`):**
   - اگر مدل تک‌واریانت است، یک فیلد بارکد در فرم اصلی؛ اگر چند‌واریانت، در هر سطر واریانت. حداقل: برای حالت
     سادهٔ تک‌واریانت یک TextBox بارکد اضافه کن و در ساخت `NewProductRequest` → `Variants[0].Barcode` پر شود.
     (محل ساخت request: `NewProductModalViewModel.cs:170` حوالی `code, name, brand, categoryId, Description.Trim(), ...`.)
   - دکمهٔ «اسکن» هماهنگ با مورد بالا.

#### (ج) تغییر/افزودن بارکد بعداً
- همان مسیر ویرایش محصول در (ب)-۴ این را پوشش می‌دهد: کاربر modal محصول را باز می‌کند، بارکد واریانت را
  تایپ/اسکن می‌کند، ذخیره → `UpdateProductInfoCommand` بارکد را persist می‌کند. چیز جدیدی لازم نیست جز اینکه
  مطمئن شوی مسیر save بارکد را واقعاً به command می‌رساند (در (ب)-۲ و (ب)-۴).

#### (د) اسکن هنگام فاکتور زدن (افزودن سریع کالا به فاکتور)
هدف: اپراتور در صفحهٔ فروش بارکد را اسکن کند و کالا مستقیم به فاکتور اضافه شود، بدون جستجوی دستی.
1. **لایهٔ داده — resolve بارکد به catalog item:**
   - به `ISalesQueryService` (`src/Animora.Contracts/Sales/ISalesQueryService.cs`) یک متد اضافه کن:
     `Task<CatalogItem?> FindCatalogItemByBarcodeAsync(string barcode);`
   - پیاده‌سازی در `src/Animora.Client.Services/Sales/SalesQueryService.cs`: یک query جدید که واریانت با آن
     بارکد را پیدا کند و به `CatalogItem` تبدیل کند (همان شکل `SalesCatalogItemReadModel.ToContract`). برای این
     query یک متد در `SalesQueries.cs` بنویس شبیه حلقهٔ catalog (خط ۳۶۱–۳۷۵) اما با فیلتر
     `where v.Barcode == barcode` و فقط اولین نتیجهٔ فعال. Id نتیجه = `v.VariantId` (هماهنگ با catalog).
   - اگر ترجیح می‌دهی لایهٔ Application را رعایت کنی، یک Query سبک در `UseCases/Sales` اضافه کن؛ اما چون
     catalog موجود هم مستقیماً در `SalesQueries` ساخته می‌شود، افزودن یک متد query محلی و فراخوانی از
     `SalesQueryService` سازگار با سبک فعلی است. سبک فعلی فایل را با grep ببین و از همان پیروی کن.
2. **UI فروش (`SalePageViewModel.cs` + `SalePageView.axaml`):**
   - `BarcodeScannerService` را به constructor این VM تزریق کن (یا در code-behind ویو attach کن).
   - یک handler: وقتی `BarcodeScanned` با یک رشته آمد → `FindCatalogItemByBarcodeAsync` → اگر پیدا شد
     `AddLineAsync(currentInvoiceId, item, 1)` (همان مسیری که catalog modal استفاده می‌کند؛ ببین VM چطور خط اضافه
     می‌کند و از همان متد/command استفاده کن) و یک Toast موفقیت؛ اگر پیدا نشد Toast «کالایی با این بارکد یافت نشد».
   - attach کردن سرویس: در `SalePageView.axaml.cs` در `OnAttachedToVisualTree`، `scanner.Attach(this)` و در
     `OnDetachedFromVisualTree` dispose کن. سرویس را از DataContext/VM یا از DI بگیر (الگوی code-behind سایر ویوها
     را ببین؛ اکثر `*.axaml.cs` اینجا فقط `InitializeComponent` دارند، پس تزریق از طریق VM تمیزتر است — رویداد را
     در VM هندل کن و VM خودش `Attach` را روی یک target بگیرد یا سرویس سراسری گوش بدهد).
   - **تمرکز UX:** اسکن باید در هر جای صفحهٔ فروش کار کند، نه فقط وقتی فیلد خاصی فوکوس دارد — به همین دلیل سرویس
     سراسری keyboard-wedge (الف) انتخاب شد. مطمئن شو که اسکن داخل یک TextBox متنی باعث تایپ شدن بارکد در آن فیلد
     نشود (رویداد را handled کن وقتی تشخیص «اسکن» داده شد).
3. **(اختیاری ولی مطلوب) در catalog modal فروش هم:** اگر drawer باز است، اسکن می‌تواند مستقیم همان آیتم را اضافه کند.
   اگر وقت/پیچیدگی اجازه نداد، حداقل مسیر صفحهٔ فروش (بند ۲) را کامل کن.

#### (ه) پوش/سینک روی سرور
- چون بارکد روی `ProductVariant` دامنه ذخیره می‌شود و از طریق commandها (`UpdateProductInfoCommand` و غیره)
  تغییر می‌کند، مکانیزم outbox/sync موجود این تغییرات را مثل هر تغییر محصول دیگری به سرور می‌برد — **نیازی به کار
  سینک اضافه نیست**، فقط مطمئن شو بارکد در مسیر command واقعاً ست می‌شود (قبلاً تأیید شد commandها فیلد دارند).
  لازم نیست چیزی در لایهٔ Sync/Server دست بزنی مگر اینکه بیلد خطایی نشان دهد. این را در گزارش ذهن داشته باش ولی
  وقت روی آن نگذار.

### ترتیب پیاده‌سازی پیشنهادی تسک ۳ (برای پوش‌های میانی تمیز)
1. سیم‌کشی بارکد در لایه‌ها: Contract `ProductVariant.Barcode` + Mapping + پر شدن در command/read-model.
   → بیلد `Animora.Contracts` + `Animora.Client.Services`. **پوش میانی.**
2. UI محصول (ساخت + ویرایش): فیلد بارکد + دکمهٔ اسکن. → بیلد `Animora.Desktop`. **پوش میانی.**
3. سرویس `BarcodeScannerService` + ثبت DI. → بیلد `Animora.Desktop`. **پوش میانی.**
4. مسیر اسکن هنگام فاکتور: `FindCatalogItemByBarcodeAsync` در Contract/Service/Query + هندل در صفحهٔ فروش.
   → بیلد پروژه‌های متأثر. **پوش میانی/نهایی.**

> چون این تسک بزرگ است، هر یک از ۴ گام بالا که کامل شد **فوراً پوش کن** (قانون قفل‌شده + «کد سنگین ⇒ پوش»).

### تأیید / تست سبک
- بعد از هر گام فقط پروژه‌های متأثر را بیلد کن (نه full solution، نه تست full):
  - `dotnet build src/Animora.Contracts/Animora.Contracts.csproj -clp:ErrorsOnly`
  - `dotnet build src/Animora.Client.Services/Animora.Client.Services.csproj -clp:ErrorsOnly`
  - `dotnet build src/Animora.Desktop/Animora.Desktop.csproj -clp:ErrorsOnly`
- اگر تست واحد سریع مرتبط موجود بود همان یکی را اجرا کن؛ در غیر این‌صورت بیلد کافی است.

### وضعیت: [ ] انجام نشده
### پوش‌ها (نمونه پیام‌ها):
- `scripts/git/push.sh "feat(inventory): wire variant barcode through contracts/mapping/commands"`
- `scripts/git/push.sh "feat(inventory-ui): add barcode field + scan button to product create/edit modals"`
- `scripts/git/push.sh "feat(desktop): add central keyboard-wedge BarcodeScannerService + DI"`
- `scripts/git/push.sh "feat(sales): scan barcode to add catalog item to invoice (FindCatalogItemByBarcode)"`

---

## نکات سراسری (رعایت هنگام کل کار)
- بررسی فایل فقط هدفمند؛ کاوش بیهوده ممنوع. `Animora-UI/`, `Database-Sampel/`, phase فایل‌های قدیمی را باز نکن.
- بیلد full / تست full / اجرای طولانی ممنوع — فقط پروژهٔ متأثر.
- استاندارد و باکیفیت؛ هماهنگ با سبک موجود (رنگ‌ها/استایل‌های StaticResource، الگوی MVVM با CommunityToolkit).
- **بعد از اتمام هر تسک (و هر گام سنگین میانی) حتماً `scripts/git/push.sh "..."` — در غیر این‌صورت شکست.**

---

## تسک پایانی — حذف همین فایل پلن
- فقط و فقط این فایل (`Plan/TODO-inventory-tasks.md`) را حذف کن. به `Plan/reports/**` و بقیهٔ فایل‌ها/roadmap دست نزن.
- سپس آخرین پوش: `scripts/git/push.sh "chore(plan): remove completed inventory tasks plan"`
### وضعیت: [ ] انجام نشده
