# Plan — بازسازی «افزودن کالا/خدمات» پانسیون بر مبنای فروش + رفع مشکل نمایش/قیمت کالاها

> **این فایل خودبسنده است.** تمام نتیجه‌گیری‌ها، مسیرهای فایل، شماره خطوط و تصمیم‌های
> معماری قبلاً استخراج شده‌اند. مدلِ اجراکننده باید بتواند بدون grep مجدد و بدون
> کشفِ دوباره، مستقیم سراغ ویرایش برود. فقط فایل‌هایی که هر Task صریحاً نام می‌برد
> را باز کن. **بیلد/تست کامل ممنوع است** (در این سندباکس `dotnet` نصب نیست؛ صحت را
> با تحلیل کد تضمین کن، نه با build طولانی).

---

## 0) قانونِ قفل‌شدهٔ Push (رعایت اجباری — شکستِ آن = شکست پروژه)

بعد از **هر** Task که وضعیتش به ✅ می‌رسد، بلافاصله اجرا کن:

```bash
scripts/git/push.sh -b genspark_ai_developer "type(scope): پیام دقیق و قابل‌درک"
```

- اگر exit code = 3 بود → ابزار `setup_github_environment` را یک‌بار صدا بزن، بعد همان دستور را دوباره اجرا کن.
- اگر exit code = 2 بود (conflict) → تعارض را به نفع remote حل کن، بعد دوباره push.
- اگر در میانهٔ یک Task فیکس سنگین/کد زیاد نوشتی، همان‌جا هم یک push میانی با پیام درست بزن.

---

## 1) زمینه و معماری (نتیجهٔ بررسی — دوباره بررسی نکن)

پروژه: **Animora** — اپ دسکتاپ دامپزشکی/حسابداری، C# .NET 10، Avalonia + MVVM
(CommunityToolkit.Mvvm)، فارسی/RTL. کل کار در `src/` انجام می‌شود. معماری لایه‌ای:

```
Desktop (ViewModel/axaml)  →  Contracts (اینترفیس سرویس + مدل‌ها)
      ↓                              ↑
Client.Services (پیاده‌سازی seam) → Application (Command/Query + Handler + Port)
      ↓
Persistence.Sqlite (پیاده‌سازی Query/Repository روی EF Core SQLite)  →  Domain (Aggregateها)
```

جریان یک Query: `ViewModel` → `IXxxQueryService` (Contracts) → `XxxQueryService`
(Client.Services) → `IDispatcher.QueryAsync(new XxxQuery(...))` → `XxxQueryHandler`
(Application) → `IXxxQueries` (Port) → پیاده‌سازی در Persistence.Sqlite.

### مفاهیم دامنهٔ کلیدی (استخراج‌شده)
- **`Product`** (`src/Animora.Domain/Inventory/Product.cs`): واحد کاتالوگ. هر Product
  **همیشه ≥۱ `ProductVariant`** دارد (invariant `VARIANT_REQUIRED` وقتی `TrackStock=true`؛
  حتی سرویس‌ها یک variant دارند). فیلدهای مهم: `IsActive`, `Kind` (`ProductKind`),
  `DefaultSalePrice` (قیمت سطحِ محصول)، `Variants` (هرکدام `v.SalePrice`, `v.IsActive`, `v.VariantId`).
- **`ProductKind`**: `Medicine=0, Vaccine=1, Food=2, PetShop=3, Consumable=4, Equipment=5, Service=6`.
- **کالاهای «کالای جدید»** (فرم Inventory) همه با `ProductKind.PetShop` و `TrackStock=true` ساخته می‌شوند
  (`src/Animora.Client.Services/Inventory/InventoryQueryService.cs:146-149`).
- **خدمات کلینیک** («نرخ خدمات صندوق») = `Product` با `Kind=Service`، `TrackStock=false`،
  ساخته‌شده در `CreateServiceRateCommandHandler`
  (`src/Animora.Application/UseCases/Sales/ServiceRateCommands.cs:77-100`): یک variant با
  `SalePrice = price` **و** `Product.DefaultSalePrice = price` (هر دو برابر). پس برای خدمات،
  `DefaultSalePrice == variant.SalePrice` همیشه.
- **خدمات پانسیون** = `BoardingServiceDef` (موجودیت جدا، `UnitPrice`, `IsActive`, `Billing`).

### الگوی مرجع — کاتالوگ فروش (Sales) که باید مبنا باشد
- UI/VM مرجع: `src/Animora.Desktop/Pages/Sales/CatalogModalViewModel.cs`
  (+ `CatalogModalView.axaml`). ساختار: جستجو با highlight، چیپ‌های فیلتر با شمارش،
  نوار «اخیر»، ردیف‌های متراکم، دکمهٔ افزودن. قابل‌استفاده‌مجدد با دو seam:
  `_loadCatalog: Func<Task<CatalogSnapshot>>` و `_addItem: Func<CatalogItem, Task>`.
- منبع دادهٔ فروش: `SalesQueries.GetCatalogAsync`
  (`src/Animora.Persistence.Sqlite/Queries/SalesQueries.cs:145-181`). منطق **دقیق**:
  1. **variantRows**: برای هر `Product` با `p.IsActive`، به‌ازای هر `v` در `p.Variants`
     با `v.IsActive` یک ردیف → `Id = v.VariantId`, `Price = v.SalePrice.Minor`,
     `Kind = Service ? "Service" : "Item"`, گروه = نام دستهٔ محصول.
  2. **variantless**: برای هر `Product` فعال که **هیچ variant فعالی ندارد**، یک ردیف
     سطح‌محصول → `Id = p.Id`, `Price = p.DefaultSalePrice.Minor`. (fallback تا محصول گم نشود.)
  3. **boarding**: هر `BoardingServiceDef` با `s.IsActive` → `Id = s.Id`,
     `Price = s.UnitPrice.Minor`, گروه «Boarding».
- مدل‌های Contracts فروش (`src/Animora.Contracts/Sales/SalesModels.cs:279-312`):
  `CatalogItem(Id, Kind, Title, GroupLabel, GroupKey, Price, IconKey)`,
  `CatalogFilterRef(Key, Label, Count)`, `RecentItemRef(Kind, Ref)`,
  `CatalogSnapshot(Items, Filters, Recent)`.
- اینترفیس فروش: `ISalesQueryService.GetCatalogAsync()` و
  `AddLineAsync(invoiceId, CatalogItem, qty)` (`src/Animora.Contracts/Sales/ISalesQueryService.cs:70,78`).

### وضعیت فعلی پانسیون (Boarding) — چه چیزی هست و کجا
- VM فعلی کاتالوگ: `src/Animora.Desktop/Pages/Boarding/BoardingCatalogModalViewModel.cs`
  — **از قبل یک کپیِ دست‌ساز و تقریباً mirror از Sales است** (همان جستجو/فیلتر/اخیر/ردیف).
  اما seamها مستقل‌اند: `IBoardingQueryService.GetSelectableItemsAsync()` و
  `AddItemAsync(stayId, itemRef, qty)`.
- UI فعلی: `src/Animora.Desktop/Pages/Boarding/BoardingCatalogModalView.axaml` (binding سالم:
  `Filters`/`Recent`/`Rows`/`Query`؛ دکمه‌ها `SetFilterCommand`/`AddCommand`/`BackCommand`).
- باز می‌شود از: `StayDetailModalViewModel.OpenCatalog`
  (`src/Animora.Desktop/Pages/Boarding/StayDetailModalViewModel.cs:318-332`) به‌صورت modal مرکزی
  (مثل Sales)، با `onBack = _onReopen`.
- seam پانسیون: `BoardingQueryService`
  (`src/Animora.Client.Services/Boarding/BoardingQueryService.cs:120-121` و `79-83`).
  - `GetSelectableItemsAsync` → `BoardingSelectableItemsQuery` → `BoardingQueries.GetSelectableAsync`.
  - `AddItemAsync` → `AddStayItemCommand` → handler در `BoardingCommands.cs:128-149`.
- منبع دادهٔ فعلی پانسیون: `BoardingQueries.GetSelectableAsync`
  (`src/Animora.Persistence.Sqlite/Queries/BoardingQueries.cs:58-66`):
  - خدمات پانسیون: `BoardingServiceDef` با `IsActive` → Source `"BoardingService"`, قیمت `UnitPrice`.
  - کالا/خدمات کلینیک: `Product` با `p.IsActive` → `Id = p.Id`,
    **قیمت = `p.DefaultSalePrice.Minor`**, Source = `Kind==Service ? "ClinicService" : "ClinicItem"`.
  - `boarding.Concat(products).OrderBy(Title).ThenBy(Id).Take(500)` — **این الگوی Concat دو
    DbSet در SQLite کار می‌کند** (نمونهٔ موفق مشابه: `ReceptionQueueQueries.cs:52`). پس Concat
    bug نیست.
- `FindSelectableAsync` (`BoardingQueries.cs:68-77`): با `id` می‌گردد — اول `BoardingServiceDef`،
  بعد `Product` (با `p.Id`). خروجی `BoardingSelectableReadModel(Id, Title, PriceMinor, Source)`.
- `AddStayItemCommandHandler` (`BoardingCommands.cs:131-148`): `FindSelectableAsync(CatalogItemId)`،
  map به `StayItemKind` (BoardingService/ClinicService/Product)، ساخت `StayItem` با
  `UnitPrice = Money.Irt(selected.PriceMinor)`. پس **قیمت ثبت‌شده روی اقامت = همان قیمتی که
  `FindSelectableAsync` می‌دهد** = `p.DefaultSalePrice` برای کالا.
- Checkout: `CheckoutStayCommandHandler` (`BoardingCommands.cs:221-248`) اقلام را با
  `UnitPriceMinor = i.UnitPrice.Minor` در payload به billing handoff می‌دهد (انتقال به فاکتور).
- مدل‌های Contracts پانسیون: `src/Animora.Contracts/Boarding/BoardingDetail.cs`
  (`BoardingItemSource` = BoardingService/ClinicService/ClinicItem؛
  `BoardingSelectableItem(Ref, Source, Title, Price)`).

### ویزیت (Visit) — همان ریشهٔ مشکل قیمت
- `RxCatalogQueries.GetAsync` (`src/Animora.Persistence.Sqlite/Queries/RxCatalogQueries.cs:25-32`):
  هر `Product` با `p.IsActive` → `Id = p.Id`, **قیمت = `p.DefaultSalePrice.Minor`**,
  `Kind = Service ? "Service" : "Item"`. سرویس‌ها اول مرتب می‌شوند. کامنت بالای فایل تأیید می‌کند
  که bugِ قدیمی «PetShop دیده نمی‌شد» قبلاً رفع شده (الان همهٔ kindها Item می‌شوند).

---

## 2) ریشهٔ دقیق مشکلات (نتیجهٔ تحلیل — این تشخیص قطعی است)

### مشکل اصلی B — «کالاها/خدمات موقع افزودن در پانسیون/ویزیت دیده نمی‌شوند یا نادرست‌اند»
ریشه، **ناهماهنگی منبع قیمت/دانه‌بندی بین فروش و پانسیون/ویزیت** است:

1. **فروش (درست):** هر کالا به‌ازای هر **variant فعال** یک ردیف با `v.SalePrice` نشان می‌دهد.
2. **پانسیون/ویزیت (ناقص):** هر کالا فقط **یک ردیف سطح‌محصول** با `p.DefaultSalePrice`.
   - **برای کالای تک‌واحدی:** `DefaultSalePrice = Lowest(variants.Price)`
     (`InventoryQueryService.cs:148,246-255`) — چون تنها variant قیمت دارد، درست کار می‌کند.
   - **برای کالای چندنوعی (HasVariants=true):** در فرم، `request.Variants = []` خالی ارسال می‌شود
     (`NewProductModalViewModel.cs:149`)، پس `Lowest([]) = 0` →
     **`Product.DefaultSalePrice = 0`**. نتیجه: چنین کالایی در پانسیون/ویزیت با **قیمت صفر**
     دیده می‌شود (گویی «دیده نمی‌شود»/بی‌استفاده است)، در حالی که در فروش با قیمت درستِ هر
     variant دیده می‌شود. این همان تجربهٔ «کالا را نمی‌بینم» برای کاربر است.
   - اثر جانبی: چون `AddStayItemCommand` قیمت را از همین `FindSelectableAsync`
     (`p.DefaultSalePrice`) می‌گیرد، **قیمت صفر به اقامت و سپس به فاکتور checkout منتقل می‌شود**
     → فاکتور نادرست.

**تصمیم راه‌حل (قطعی):** منبع دادهٔ پانسیون و ویزیت را با **منطق فروش** هم‌تراز کن:
قیمت کالا باید از **variant فعال** بیاید نه از `DefaultSalePrice`. برای پانسیون، چون باید
«دقیقاً مثل فروش» شود، بهترین کار استفادهٔ مستقیم از `CatalogSnapshot` فروش است (منبع واحد).
برای ویزیت، اصلاح `RxCatalogQueries` تا قیمت را از variant بگیرد (یا در نبود variant فعال،
`DefaultSalePrice`) — همان الگوی دوبخشیِ `GetCatalogAsync`.

### تسک اصلی A — «منطق منوی افزودن پانسیون را حذف و از صفر بر مبنای فروش بساز»
چون `BoardingCatalogModalViewModel` از قبل mirrorِ Sales است، «حذف و بازسازی بر مبنای فروش»
در عمل یعنی: **VM اختصاصی پانسیون را حذف کن و به‌جای آن از `CatalogModalViewModel` فروش
(همان UI/همان منطق) با seamهای پانسیون استفاده کن**، تا منبع واحد و رفتار/ظاهر دقیقاً یکی شود.
این هم خواستهٔ «یکپارچه و استاندارد» را برآورده می‌کند و هم مشکل قیمت را (چون `_loadCatalog`
همان `GetCatalogAsync` فروش + خدمات پانسیون می‌شود) ریشه‌ای حل می‌کند.

**نکتهٔ سازگاری id (حیاتی):** اگر منبع، `CatalogItem` فروش شود، `Id` می‌تواند **VariantId**
باشد (نه ProductId). پس `AddItemAsync`/`FindSelectableAsync`/`AddStayItemCommand` باید بتوانند
یک **VariantId** را هم resolve کنند. امروز `FindSelectableAsync` فقط `Product.Id` و
`BoardingServiceDef.Id` را می‌شناسد → باید شاخهٔ **variant** اضافه شود
(پیدا کردن محصولی که واریانتی با آن `VariantId` دارد، گرفتن `v.SalePrice`).

---

## 3) تصمیم معماری نهایی (این را پیاده کن)

**رویکرد انتخابی: یکپارچه‌سازی پانسیون با کاتالوگ فروش + اصلاح ویزیت.**

به‌جای نگه‌داشتن دو مسیر موازی، پانسیون از همان «snapshot فروش» تغذیه می‌شود و افزودن از طریق
seam خودِ پانسیون انجام می‌گیرد، با resolveِ درستِ VariantId. این:
- خواستهٔ «منطق و ظاهر دقیقاً مثل فروش» را تضمین می‌کند (همان VM و همان axaml فروش).
- خدمات «نرخ خدمات صندوق» + کالاها + خدمات پانسیون را یکجا نشان می‌دهد.
- مشکل قیمت صفرِ کالای چندنوعی را حذف می‌کند (قیمت از variant).
- ثبت در اقامت و انتقال به فاکتور را درست می‌کند (قیمت درست از ابتدا جریان می‌یابد).

جزئیات seam پانسیون پس از تغییر:
- `IBoardingQueryService` یک متد جدید `GetCatalogAsync() : Task<CatalogSnapshot>` می‌گیرد
  (برمی‌گرداند: کالا/خدمات فروش + خدمات پانسیون؛ همان شکل فروش) — یا از `ISalesQueryService`
  موجود استفاده مجدد کن (ترجیح: تزریق `ISalesQueryService` به modal پانسیون برای کاتالوگ، و
  نگه‌داشتن `IBoardingQueryService.AddItemAsync` برای افزودن). **تصمیم ساده‌تر و کم‌ریسک‌تر:**
  داخل `StayDetailModalViewModel.OpenCatalog` از `CatalogModalViewModel` فروش با سازندهٔ
  reusable استفاده کن:
  - `loadCatalog = () => boardingCatalogSnapshotProvider()` (snapshotِ شاملِ خدمات پانسیون)
  - `addItem = item => _boardingService.AddItemAsync(stayId, item.Id, 1)`
  - `onBack = _onReopen`

> اگر افزودنِ متد کاتالوگ به `IBoardingQueryService` ساده‌تر و تمیزتر بود، همان را بساز
> (`GetCatalogAsync` که داخلش از `ISalesQueries.GetCatalogAsync` + خدمات پانسیون استفاده
> کند و خروجی `CatalogSnapshot` بدهد). هدف: **یک منبع، همان مدل‌های فروش**.

نگاشت `CatalogItem.Kind`/`GroupKey` در پانسیون:
- `Kind == "svc"` → خدمت، `Kind == "item"` → کالا (همان Sales؛ دقت: Sales از رشته‌های
  `"Service"/"Item"` در read-model و `"svc"/"item"` در CatalogItem استفاده می‌کند — نگاشت را
  از `SalesQueryService` موجود بردار، دوباره اختراع نکن).
- خدمات پانسیون باید در snapshot با یک `GroupKey`/`GroupLabel` مخصوص (مثلاً «پانسیون») بیایند
  تا در چیپ فیلتر قابل‌تفکیک باشند (مثل گروه «Boarding» که در `GetCatalogAsync` فروش هست).

---

## 4) تسک‌لیست اجرا (به‌ترتیب؛ بعد از هر ✅ حتماً push)

> هر Task: فقط فایل‌های نام‌برده را باز کن. بعد از اتمام، وضعیت را در همین فایل به ✅ تغییر بده،
> سپس `scripts/git/push.sh` را با پیام دقیق اجرا کن.

### Task 1 — رفع ریشه‌ایِ قیمت کالا در منبع داده (Persistence) — [x] ✅ انجام شد
**هدف:** قیمت کالاها در پانسیون و ویزیت از **variant فعال** بیاید نه `DefaultSalePrice`، تا
کالای چندنوعی دیگر صفر/نامرئی نباشد و قیمت درست به اقامت/فاکتور جریان یابد.

**فایل‌ها و تغییرات دقیق:**
1. `src/Animora.Persistence.Sqlite/Queries/BoardingQueries.cs`
   - `GetSelectableAsync` (خطوط 58-66): بخش `products` را به الگوی دوبخشیِ فروش تبدیل کن:
     - **variantRows:** برای هر `Product` فعال، به‌ازای هر `v` فعال یک
       `BoardingSelectableReadModel(v.VariantId, p.Name, v.SalePrice.Minor, Source)` که
       `Source = p.Kind==Service ? "ClinicService" : "ClinicItem"`.
     - **variantless:** برای هر `Product` فعال بدون variant فعال، یک ردیف با
       `(p.Id, p.Name, p.DefaultSalePrice.Minor, Source)`.
     - سپس `boarding.Concat(variantRows).Concat(variantless)` (یا در حافظه با `ToList` مثل
       `SalesQueries` برای اجتناب از پیچیدگی ترجمهٔ EF روی سه Concat) و `OrderBy(Title)`.
   - `FindSelectableAsync` (خطوط 68-77): شاخهٔ **VariantId** اضافه کن:
     - اول `BoardingServiceDef` با `id`.
     - بعد `Product` که `p.Id == id` (مسیر variantless/سطح‌محصول فعلی).
     - **جدید:** `Product` که `p.Variants.Any(v => v.VariantId == id && v.IsActive)` →
       برگردان `(v.VariantId, p.Name, v.SalePrice.Minor, Source)`.
     - (دقت: خروجی `Id` باید همان id ورودی باشد تا `AddStayItemCommandHandler` همان ref را روی
       `StayItem.CatalogItemId` ذخیره کند.)
2. `src/Animora.Persistence.Sqlite/Queries/RxCatalogQueries.cs`
   - `GetAsync` (خطوط 25-32): همان الگوی دوبخشی (variant فعال با `v.SalePrice`؛ در نبودش
     `p.DefaultSalePrice`). سرویس‌ها همچنان اول مرتب شوند. `Id` باید VariantId باشد وقتی از
     variant می‌آید، وگرنه `p.Id`.
   - **بررسی سازگاری ویزیت:** ببین `VisitEncounterService.AddRxLineAsync` و مسیر
     `AddPrescriptionLineCommand` آیا `CatalogId` را دوباره lookup می‌کنند یا فقط ذخیره.
     `VisitEncounterService.cs:41-48` نشان می‌دهد `line.CatalogId` و `line.UnitPrice` را
     مستقیم پاس می‌دهد (قیمت از UI می‌آید) → پس تغییر id به VariantId در ویزیت بی‌خطر است
     (lookup مجددی که به ProductId وابسته باشد وجود ندارد). **تأیید کن** با باز کردن
     `src/Animora.Application/UseCases/Visits/` فقط در حد یافتن `AddPrescriptionLineCommand`
     که آیا `CatalogId` را برای چیزی resolve می‌کند یا خیر؛ اگر resolve می‌کند و به ProductId
     وابسته است، آن مسیر را هم variant-aware کن.

**ملاک درستی (بدون build):** هیچ کوئری‌ای دیگر `p.DefaultSalePrice` را به‌عنوان قیمتِ تنها منبع
برای محصولِ دارای variant فعال استفاده نمی‌کند؛ کالای چندنوعی اکنون یک ردیف per-variant با قیمت
واقعی دارد؛ `FindSelectableAsync` یک VariantId را resolve می‌کند.

**Push بعد از اتمام:** `scripts/git/push.sh -b genspark_ai_developer "fix(boarding,visits): قیمت کالا را از variant فعال بخوان تا کالای چندنوعی صفر/نامرئی نشود"`

---

### Task 2 — یکپارچه‌سازی modal کاتالوگ پانسیون با کاتالوگ فروش — [x] ✅ انجام شد
**هدف:** منطق و ظاهر «افزودن کالا/خدمات» پانسیون دقیقاً همان فروش شود (منبع واحد)، شاملِ
خدمات «نرخ خدمات صندوق» + کالاها + خدمات پانسیون.

**تصمیم:** `BoardingCatalogModalViewModel` و منطق مستقلش را حذف کن و از
`CatalogModalViewModel` فروش (سازندهٔ reusable) استفاده کن.

**فایل‌ها و تغییرات دقیق:**
1. `src/Animora.Contracts/Boarding/IBoardingQueryService.cs`
   - یک متد اضافه کن: `Task<Animora.Contracts.Sales.CatalogSnapshot> GetCatalogAsync();`
     (کاتالوگی که کالا/خدمات کلینیک + خدمات پانسیون را در شکل فروش برمی‌گرداند).
   - متد قدیمی `GetSelectableItemsAsync` را **فقط اگر دیگر هیچ مصرف‌کننده‌ای ندارد** حذف کن
     (grep: `GetSelectableItemsAsync`). اگر جای دیگری استفاده شده، نگه‌دار.
2. `src/Animora.Client.Services/Boarding/BoardingQueryService.cs`
   - پیاده‌سازی `GetCatalogAsync`: به `ISalesQueries.GetCatalogAsync` (یا کوئری معادل) + خدمات
     پانسیون تکیه کن و خروجی `CatalogSnapshot` بساز. **ساده‌ترین مسیر:** از همان
     `SalesQueries.GetCatalogAsync` استفادهٔ مجدد کن (که خدمات پانسیون را هم گروه «Boarding»
     اضافه می‌کند) — یعنی snapshot فروش **از قبل شامل خدمات پانسیون است**
     (`SalesQueries.cs:170-173`). پس احتمالاً کافی است همان snapshot فروش را برگردانی.
     → **تأیید کن** که `GetCatalogAsync` فروش خدمات پانسیون را دارد (دارد، خط 170-173)؛
     در این صورت `IBoardingQueryService.GetCatalogAsync` فقط snapshot فروش را پاس می‌دهد.
   - `AddItemAsync(stayId, itemRef, qty)` بدون تغییر می‌ماند؛ اما چون `itemRef` اکنون می‌تواند
     VariantId باشد، به Task 1 (شاخهٔ variant در `FindSelectableAsync`) متکی است.
3. `src/Animora.Desktop/Pages/Boarding/StayDetailModalViewModel.cs`
   - `OpenCatalog` (خطوط 318-332): به‌جای `new BoardingCatalogModalViewModel(...)`،
     `new CatalogModalViewModel(loadCatalog, addItem, _toast, _dialogs, onChanged, initialFilter)` بساز:
     - `loadCatalog = _boardingService.GetCatalogAsync`
     - `addItem = item => _boardingService.AddItemAsync(StayId, item.Id, 1)`
     - `onChanged = _onChanged`
     - **بازگشت/بستن:** `CatalogModalViewModel` فعلی `Close()` دارد اما `onBack`/`Back` ندارد
       (برعکس نسخهٔ پانسیون). دو گزینه:
       - (ترجیح) به سازندهٔ reusable `CatalogModalViewModel` یک پارامتر اختیاری
         `Action? onBack = null` اضافه کن و در axaml فروش دکمهٔ بازگشت را فقط وقتی `onBack != null`
         نشان بده؛ `CloseCommand` وقتی `onBack` هست آن را صدا بزند. (کمترین تغییر، سازگار با فروش.)
       - یا بعد از افزودن، مثل امروز `onChanged` صفحه را refresh کند و بستن = بازگشت به
         stay-detail از طریق `_onReopen`.
     - `maxWidth: 500` را حفظ کن.
4. حذف فایل‌های VM/axaml مستقل پانسیون **فقط اگر بعد از مرحلهٔ ۳ بی‌مصرف شدند**:
   - `src/Animora.Desktop/Pages/Boarding/BoardingCatalogModalViewModel.cs`
   - `src/Animora.Desktop/Pages/Boarding/BoardingCatalogModalView.axaml` (+ `.axaml.cs`)
   - و ثبت DI آن‌ها در `src/Animora.Desktop/App.axaml.cs` (grep: `BoardingCatalogModal`).
   - و `ViewLocator`/`DataTemplates` اگر رجوعی دارند (grep: `BoardingCatalogModal`).
   - **احتیاط:** قبل از حذف، grep کن `BoardingCatalogModal` در کل `src/` تا رفرنس آویزان نماند.

**ملاک درستی (بدون build):** هیچ رفرنسی به `BoardingCatalogModalViewModel`/`BoardingCatalogModalView`
باقی نمانده؛ `StayDetailModalViewModel.OpenCatalog` از `CatalogModalViewModel` فروش استفاده می‌کند؛
`IBoardingQueryService.GetCatalogAsync` snapshot فروش (شامل خدمات پانسیون) را می‌دهد؛ افزودن از
طریق `AddItemAsync(stayId, item.Id, 1)` با resolve درست VariantId کار می‌کند.

**Push بعد از اتمام:** `scripts/git/push.sh -b genspark_ai_developer "feat(boarding): استفاده از کاتالوگ فروش برای افزودن کالا/خدمات پانسیون (منطق و ظاهر یکپارچه)"`

---

### Task 3 — صحت‌سنجی نگاشت ثبت در اقامت و انتقال به فاکتور — [ ] در انتظار
**هدف:** اطمینان از اینکه آیتم افزوده‌شده با قیمت/عنوان/نوعِ درست روی اقامت ثبت و در checkout
به فاکتور منتقل می‌شود (خواستهٔ «در فاکتور به‌درستی ثبت شود و بعد از انتقال صحیح باشد»).

**فایل‌ها (فقط خواندن/تأیید + اصلاح در صورت نیاز):**
1. `src/Animora.Application/UseCases/Boarding/BoardingCommands.cs:131-148` (`AddStayItemCommandHandler`):
   - تأیید: `kind` از `selected.Source` درست map می‌شود
     (`BoardingService`→`BoardingService`, `ClinicService`→`ClinicService`, سایر→`Product`).
     بعد از Task 1، کالاها `ClinicItem` → `_` → `StayItemKind.Product` (درست).
   - تأیید: `UnitPrice = Money.Irt(selected.PriceMinor)` اکنون = قیمت variant (درست).
2. `src/Animora.Application/UseCases/Boarding/BoardingCommands.cs:221-248` (`CheckoutStayCommandHandler`):
   - تأیید: payload اقلام با `UnitPriceMinor = i.UnitPrice.Minor` و `CatalogItemId` ساخته می‌شود.
   - دنبال کن که billing handoff چگونه به فاکتور تبدیل می‌شود: grep `IBoardingBillingHandoffWriter`
     و مصرف‌کنندهٔ payload (فقط در حد اطمینان از اینکه `UnitPriceMinor`/`Title`/`Quantity` به
     خطوط فاکتور می‌روند). اگر مصرف‌کننده روی `CatalogItemId` دوباره lookup قیمت می‌کند و به
     ProductId وابسته است (نه VariantId)، آن lookup را هم variant-aware کن یا از قیمتِ
     snapshot‌شدهٔ payload استفاده کن (ترجیح: قیمتِ frozenِ payload، نه lookup مجدد).
3. اگر هیچ اصلاحی لازم نبود، این Task فقط یک تأیید است (بدون تغییر کد) — اما **اگر فیکسی لازم شد
   و کد زیادی نوشتی، push میانی بزن**.

**ملاک درستی:** مسیر add→stay→checkout→invoice قیمت/عنوان/نوع را حفظ می‌کند و هیچ lineٔ صفر یا
گمشده تولید نمی‌شود؛ هیچ lookupِ قیمتِ وابسته به ProductId که با VariantId بشکند باقی نمانده.

**Push بعد از اتمام:** `scripts/git/push.sh -b genspark_ai_developer "fix(boarding): انتقال صحیح قیمت/نوع اقلام از اقامت به فاکتور در checkout"`

---

### Task 4 — پاکسازی، هماهنگی DI/ViewLocator و صحت‌سنجی نهایی کد — [ ] در انتظار
**هدف:** نبودِ رفرنس آویزان، ثبت DI درست، و یکدستی نام‌ها/کامنت‌ها.

**کارها:**
1. `grep -rn "BoardingCatalogModal" src/` → باید خالی باشد (جز اگر عمداً نگه داشته شد).
2. `grep -rn "GetSelectableItemsAsync" src/` → اگر حذف شد، هیچ مصرف‌کننده‌ای نماند؛ اگر ماند،
   دلیلش را مطمئن شو.
3. `src/Animora.Desktop/App.axaml.cs`: ثبت `CatalogModalViewModel` برای استفاده در پانسیون
   کافی است (اگر با `new` ساخته می‌شود نیازی به DI اضافه نیست؛ الگوی فعلی Sales را دنبال کن —
   در `SalePageViewModel.cs:376` با `new CatalogModalViewModel(...)` ساخته می‌شود، پس DI لازم نیست).
4. کامنت‌های XML قدیمیِ ارجاع‌دهنده به مدلِ حذف‌شده را به‌روزرسانی کن (مثلاً داک‌کامنت
   `StayDetailModalViewModel.OpenCatalog`).
5. یک مرور چشمیِ نهایی: تمام مسیرهای تغییر‌یافته idها/قیمت‌ها/kindها سازگارند.

**Push بعد از اتمام:** `scripts/git/push.sh -b genspark_ai_developer "chore(boarding): پاکسازی رفرنس‌های مدل حذف‌شده و هماهنگی نهایی"`

---

### Task 5 — حذف این فایل Plan (تسک پایانی) — [ ] در انتظار
**فقط** این فایل را حذف کن (`Plan/boarding-catalog-rebuild-and-catalog-visibility-fix.md`).
به هیچ فایل دیگرِ داخل `Plan/` و به هیچ فایل roadmap دست نزن.

```bash
rm "/home/user/webapp/Plan/boarding-catalog-rebuild-and-catalog-visibility-fix.md"
```

**Push بعد از اتمام:** `scripts/git/push.sh -b genspark_ai_developer "chore(plan): حذف پلن پس از اتمام بازسازی کاتالوگ پانسیون"`

---

## 5) نکات محافظتی (قبل از شروع بخوان)
- فقط فایل‌های نام‌برده در هر Task را باز کن؛ گشتنِ بی‌هدف ممنوع.
- بیلد/تستِ کامل نگیر (`dotnet` نصب نیست و قانون کاربر هم ممنوع کرده). صحت را با تحلیل کد تضمین کن.
- الگوهای موجودِ فروش را دوباره اختراع نکن؛ همان نگاشت‌های `SalesQueryService`/`SalesQueries`
  را استفاده مجدد کن (به‌ویژه نگاشت `Service/Item`↔`svc/item` و `IconKey`).
- اصل یکپارچگی: **یک منبع کاتالوگ** (فروش) برای فروش/صندوق/پانسیون؛ ویزیت هم باید قیمت را از
  variant بگیرد.
- ترتیب Taskها را رعایت کن: Task 1 (منبع داده/resolve) پیش‌نیازِ Task 2 (استفاده از id variant) است.
- بعد از هر ✅، push قفل‌شده را فراموش نکن.
