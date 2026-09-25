> **آرشیو تاریخی — برای قواعد جاری پرفورمنس به `PERFORMANCE.md` (ریشهٔ ریپو) مراجعه کنید.**
> این فایل یک گزارش یک‌بارهٔ مربوط به تاریخ زیر است؛ یافته‌هایش یا اعمال شده‌اند یا در
> `PERFORMANCE.md` به‌صورت قاعدهٔ دائمی ثبت شده‌اند. برای کار روزمره خوانده نشود.

# گزارش بررسی جامع پرفورمنس و معماری — Animora

**تاریخ:** ۲۰۲۶-۰۹-۲۰  
**دامنه:** کل پروژه پیاده‌سازی‌شده (`src/Animora.Desktop`, `src/Animora.Contracts`)  
**استک:** .NET 10 · C# 14 · Avalonia 12.1.2 · CommunityToolkit.Mvvm 8.4.2

---

## بخش ۰ — خلاصهٔ مدیریتی

پروژه از نظر پرفورمنس و معماری **در وضعیت بسیار خوبی** است. یک کمپین بهینه‌سازی گسترده
(بیش از ۲۰ کامیت `perf(...)`) قبلاً انجام شده که تقریباً همهٔ گلوگاه‌های کلاسیک یک اپ
Avalonia/MVVM را پوشش داده است:

| حوزه | وضعیت |
|---|---|
| View resolution بدون reflection (`ViewFactory` + `ViewCache`) | ✅ استاندارد |
| Compiled Bindings سراسری | ✅ فعال |
| Virtualization لیست‌های اصلی (ListBox + MaxHeight) | ✅ انجام شده |
| Batch collection updates (`RangeObservableCollection`) | ✅ انجام شده |
| کش براش/گرادیان bounded (`BrushCache`/`GradientBrushCache`) | ✅ استاندارد |
| کش هندسهٔ چارت‌ها (`WeightChart`/`FinanceCharts`/`DonutRing`) | ✅ انجام شده |
| ایندکس O(1) در ViewModelها و Mockها | ✅ انجام شده |
| Debounce سرچ متمرکز (`Debouncer`) | ✅ انجام شده |
| async load بدون بلاک‌کردن UI | ✅ انجام شده |
| build knobs (R2R, TieredPGO, Workstation GC) | ✅ انجام شده |
| لایه‌بندی Contracts → ViewModel → Mock | ✅ تمیز و استاندارد |

بنابراین یافته‌های باقی‌مانده عمدتاً **متوسط و کم‌اهمیت** هستند و «گلوگاه بحرانی» تقریباً
وجود ندارد. مهم‌ترین‌ها: چند مسیر O(n) در صفحهٔ چک‌ها، یک ریسک کرش واقعی (`double.Parse`)،
یک ناهماهنگی استاندارد در Reception، و الگوی `$parent[Button]` (RelativeSource) که سند رسمی
Avalonia صریحاً توصیه به اجتناب از آن کرده است.

### دربارهٔ سؤال «آیا این مقدار کش استاندارد است؟»
**بله، کاملاً استاندارد و به‌جا است.** چهار کش موجود همگی اصولی پیاده شده‌اند:
- `BrushCache` و `GradientBrushCache`: `ConcurrentDictionary`، bounded (فضای کلید = پالت ثابت
  دیزاین‌سیستم)، lock-free در read، فریز‌شده (immutable). این دقیقاً الگوی توصیه‌شدهٔ Avalonia
  برای جلوگیری از آلوکیشن براش در hot path است.
- `ViewCache`: از `ConditionalWeakTable` استفاده می‌کند تا ViewModelهای transient نشت نکنند —
  انتخابی بسیار حرفه‌ای.
- `LazyTabHost` cache: با تعویض DataContext پاک می‌شود، پس نشت ندارد.

هیچ کش unbounded یا بدون سیاست eviction دیده نشد (مطابق `PERFORMANCE.md §15`). تنها نکته این
است که این کش‌ها **هرگز صریحاً clear نمی‌شوند** — که چون فضای کلیدشان ثابت است مشکلی نیست، اما
باید مستند بماند (پرامت ۹).

---

## بخش ۱ — یافته‌ها بر اساس شدت

### 🔴 بحرانی / تأثیر بالا
- **F-1** — `double.Parse(c.Number)` در مرتب‌سازی چک‌ها → **ریسک کرش واقعی** روی شمارهٔ غیرعددی
  یا ارقام فارسی. (`ChequesPageViewModel.cs:334`)

### 🟠 تأثیر متوسط (با زیاد شدن داده مشکل‌ساز)
- **F-2** — `Filtered()` در `ChequesPageViewModel` روی هر رندر چند بار و به‌صورت O(n·filters)
  اجرا می‌شود؛ `SelectAll` هم یک بار دیگر `Filtered()` را صدا می‌زند. (`ChequesPageViewModel.cs:431,979`)
- **F-3** — `RenderBulk` در چک‌ها از `_cheques.FirstOrDefault(x => x.Id == id)` داخل حلقهٔ
  انتخاب‌ها استفاده می‌کند → O(selected × total). ایندکس id در این ViewModel ساخته نشده
  (برخلاف Patients/Appointments). (`ChequesPageViewModel.cs:649,925,1082`)
- **F-4** — `ReceptionPageViewModel.FilterQueue()` روی هر دسترسی به `TotalCount`/`CanGoNext`
  دوباره کل صف را فیلتر + مرتب + `ToList` می‌کند؛ در یک بایندینگ ممکن است چند بار در هر رندر
  اجرا شود. (`ReceptionPageViewModel.cs:56,78,251`)

### 🟡 ناهماهنگی استاندارد / پاکسازی
- **F-5** — `ReceptionPageViewModel.VisibleQueue` از `ObservableCollection` + `Clear()`+`Add`
  در حلقه استفاده می‌کند، درحالی‌که استاندارد پروژه `RangeObservableCollection.ReplaceAll`
  است (`PERFORMANCE.md §5`). ناهماهنگ با بقیهٔ صفحات. (`ReceptionPageViewModel.cs:48,258`)
- **F-6** — الگوی `Stroke="{Binding $parent[Button].Foreground}"` (۸۴ مورد) که سند رسمی
  Avalonia صریحاً توصیه به جایگزینی با attached property + property inheritance کرده است.
- **F-7** — استفاده از `<Run>` برای تنظیم پراپرتی متن (۱۰ مورد در History، ۷ در Reception، …)
  که سند رسمی Avalonia به‌عنوان الگوی «resource-demanding» معرفی کرده است.
- **F-8** — `PageViewModelBase.Breadcrumbs` با `new()` مقداردهی شده در حالی که بقیهٔ پروژه از
  collection expression `[]` استفاده می‌کند؛ همچنین Breadcrumbs همیشه پس از ساخت پر می‌شود و
  هرگز تغییر نمی‌کند، پس نیازی به `ObservableCollection` نیست.

### 🟢 کم‌اهمیت / بدهی فنی مستندشده (نیازی به اقدام فوری نیست)
- **F-9** — تک‌تن‌بودن page ViewModelها + load در سازنده (خودِ `PERFORMANCE.md` به‌عنوان بدهی
  ثبت کرده؛ با mock مشکلی نیست، با بک‌اند واقعی نیاز به `OnNavigatedTo` دارد).
- **F-10** — `FinancePageViewModel.LoadPanelAsync` همیشه هم Overview و هم Income را fetch می‌کند
  حتی وقتی تب فعال Income نیست، و هر تعویض تب کل پنل را دوباره از سرویس می‌گیرد (بدون کش تب).

---

## بخش ۲ — بررسی معماری و استاندارد کدنویسی

**لایه‌بندی: استاندارد و تمیز.** جریان `View → ViewModel → Contracts interface (DI) → Mock`
به‌دقت رعایت شده. هیچ نشت `Mock*` یا دادهٔ hardcode در `Pages/**` دیده نشد. DTOها و اینترفیس‌ها
per-bounded-context در `Animora.Contracts` تفکیک شده‌اند. تنها ملاحظات:

- **A-1** — پروژه‌های `Animora.Domain` / `Animora.Application` / `Animora.Sync.Abstractions`
  خالی (placeholder) هستند. این عمدی و طبق نقشهٔ راه است (بک‌اند track بعدی)، نه نقص.
- **A-2** — چند ViewModel صفحه بسیار بزرگ شده‌اند (`AppointmentsCalendarViewModel` ۱۳۶۶ خط،
  `ChequesPageViewModel` ۱۱۵۶ خط، `FinancePageViewModel` ۸۸۹ خط). این‌ها God-ViewModel نیستند
  چون یک صفحهٔ واقعاً پیچیده را مدیریت می‌کنند، اما تفکیک منطق render/filter به کلاس‌های
  helper (مثل `*Presentation`) خوانایی را بالا می‌برد. (پرامت اختیاری ۸)
- **A-3** — استاندارد نام‌گذاری، nullable enable، implicit usings، compiled bindings و
  `x:DataType` روی تمام Viewها رعایت شده. کیفیت کد یکدست است.

---

## بخش ۳ — پرامت‌های تخصصی (هرکدام برای یک سشن مستقل)

> هر پرامت زیر **خودکفا** است: فرض می‌کند مدل هیچ اطلاعات قبلی از پروژه ندارد. بلوک «زمینهٔ
> پروژه» در هر پرامت تکرار شده تا در سشن جدا قابل استفاده باشد. پرامت‌ها به ترتیب اولویت مرتب
> شده‌اند.

---

### پرامت ۱ — [🔴 بحرانی] رفع ریسک کرش `double.Parse` در مرتب‌سازی چک‌ها

```
تو یک مهندس ارشد C#/.NET هستی که روی یک اپ دسکتاپ Avalonia کار می‌کنی.

## زمینهٔ پروژه (سشن جدید — این را اول بخوان)
- ریپو: اپ مدیریت کلینیک دامپزشکی «Animora». استک: .NET 10، C# 14، Avalonia 12.1.2،
  CommunityToolkit.Mvvm. رابط فارسی/RTL با تقویم جلالی.
- معماری: View → ViewModel → اینترفیس در Animora.Contracts (از طریق DI) → پیاده‌سازی Mock.
- استاندارد پرفورمنس تیم در فایل PERFORMANCE.md ریشهٔ ریپو مستند است.
- برای build سریع: `scripts/sandbox/build.sh` (باید 0 warning / 0 error بدهد).

## مشکل
در فایل:
  src/Animora.Desktop/Pages/Cheques/ChequesPageViewModel.cs
داخل متد `Filtered()` (حدود خط ۳۳۴)، شاخهٔ مرتب‌سازی بر اساس شمارهٔ چک این است:
  "no" => list.OrderBy(c => double.Parse(c.Number) * sign).ToList(),
فیلد `Cheque.Number` یک رشته است و می‌تواند شامل ارقام فارسی (۰۱۲۳)، خط تیره، فاصله یا
کاراکتر غیرعددی باشد. `double.Parse` روی چنین ورودی‌ای `FormatException` پرتاب می‌کند و چون
این کد در مسیر رندر UI اجرا می‌شود، کل صفحه هنگام مرتب‌سازی کرش می‌کند. علاوه بر این
`double.Parse` بدون `CultureInfo` به کالچر جاری وابسته است (ناایمن).

## کاری که باید انجام دهی
1. یک متد کمکی خصوصی `static` در همان کلاس بساز که یک رشتهٔ شمارهٔ چک را به‌صورت امن به عدد
   تبدیل کند:
   - اول ارقام فارسی/عربی را به اسکی تبدیل کن. در پروژه از قبل
     `Animora.Desktop.Localization.PersianNumberFormatter.ToAsciiDigits(...)` وجود دارد؛ از
     همان استفاده کن (کلاس دیگری نساز).
   - سپس با `long.TryParse(..., NumberStyles.None, CultureInfo.InvariantCulture, out var n)`
     پارس کن؛ اگر شکست خورد `0` (یا `long.MaxValue` برای پایدارماندن ترتیب) برگردان — هرگز
     throw نکن.
   - چون شمارهٔ چک صحیح و بزرگ است از `long` استفاده کن نه `double`.
2. شاخهٔ "no" را به استفاده از این متد کمکی تغییر بده و ضرب در `sign` را حفظ کن (منطق فعلی
   asc/desc نباید تغییر کند).
3. مطمئن شو `using System.Globalization;` بالای فایل هست.

## محدودیت‌ها
- فقط همین فایل را تغییر بده. رفتار مرتب‌سازی برای ورودی‌های معتبر نباید عوض شود.
- بعد از تغییر حتماً `scripts/sandbox/build.sh` را اجرا کن و مطمئن شو 0/0 است.
- تغییر را کامیت کن با پیام: `fix(cheques): guard cheque-number sort against non-numeric/Persian input`
```

---

### پرامت ۲ — [🟠 متوسط] افزودن ایندکس O(1) و حذف اسکن‌های تکراری در صفحهٔ چک‌ها

```
تو یک مهندس ارشد C#/.NET هستی که روی یک اپ دسکتاپ Avalonia کار می‌کنی.

## زمینهٔ پروژه (سشن جدید — این را اول بخوان)
- ریپو: اپ مدیریت کلینیک دامپزشکی «Animora». استک: .NET 10، C# 14، Avalonia 12.1.2،
  CommunityToolkit.Mvvm. رابط فارسی/RTL.
- معماری: View → ViewModel → اینترفیس Animora.Contracts (DI) → Mock.
- استاندارد پرفورمنس تیم در PERFORMANCE.md است؛ به‌ویژه:
  §2 (کمینه‌سازی آلوکیشن در حلقه‌ها) و §13 (اجتناب از reload/rerender تکراری).
- الگوی مرجعِ ایندکس‌سازی از قبل در پروژه وجود دارد:
  `src/Animora.Desktop/Pages/Patients/PatientsPageViewModel.cs` (متد `RebuildIndexes`)
  و `src/Animora.Desktop/Pages/Appointments/AppointmentsCalendarViewModel.cs`
  (متد `RebuildIndexes`). سبک همان‌ها را دقیقاً دنبال کن.
- build: `scripts/sandbox/build.sh` (باید 0/0 شود).

## مشکل
فایل: src/Animora.Desktop/Pages/Cheques/ChequesPageViewModel.cs
دو مسیر غیربهینه دارد که با رشد تعداد چک‌ها کند می‌شوند:

1) O(n) lookup تکراری با id:
   - خط ~۶۴۹ در متد `RenderBulk`:
       .Select(id => _cheques.FirstOrDefault(x => x.Id == id))
     این داخل انتخاب چک‌های selected اجرا می‌شود → O(selected × total).
   - خط ~۹۲۵ در `OpenDetail` و ~۱۰۸۲ در `RunRowActionAsync`:
       var c = _cheques.FirstOrDefault(x => x.Id == id);
   فیلد `_cheques` یک `List<Cheque>` است (خط ۳۷). هیچ ایندکس id ساخته نشده.

2) اجرای تکراری `Filtered()`:
   - `Filtered()` (خط ۲۶۷) یک لیست کاملاً جدید با LINQ Where/OrderBy می‌سازد.
   - در `SelectAll` (خط ~۹۷۹) دوباره `Filtered()` صدا زده می‌شود، در حالی که همان لیست
     در `RenderTable`/`RenderAll` هم ساخته می‌شود.

## کاری که باید انجام دهی
1. یک `Dictionary<string, Cheque> _chequeById = new(StringComparer.Ordinal);` به فیلدهای
   کلاس اضافه کن (کنار `_cheques`, `_banks`, `_statuses`).
2. یک متد خصوصی `RebuildChequeIndex()` بساز (دقیقاً به سبک `RebuildIndexes` در Patients):
   `_chequeById.Clear(); foreach (var c in _cheques) _chequeById[c.Id] = c;`
3. `RebuildChequeIndex()` را **هرجا `_cheques` بازسازی می‌شود** صدا بزن. این نقاط:
   - در `LoadAsync` بلافاصله بعد از `_cheques.AddRange(await _service.GetAllAsync());` (خط ~۲۰۶)
   - در `ReloadAsync` بلافاصله بعد از `_cheques.AddRange(...)` (خط ~۹۵۱)
4. یک متد کمکی `private Cheque? ChequeById(string id) => _chequeById.GetValueOrDefault(id);`
   اضافه کن و هر سه `_cheques.FirstOrDefault(x => x.Id == id)` (خطوط ~۶۴۹، ۹۲۵، ۱۰۸۲) را با آن
   جایگزین کن. مراقب nullable باش (منطق فعلی که null را رد می‌کند حفظ شود).
5. برای مشکل `Filtered()` تکراری: در `SelectAll` (خط ~۹۷۹) به‌جای صدازدن دوبارهٔ `Filtered()`،
   از همان مجموعهٔ صفحهٔ جاری استفاده کن. سبک ساده و بی‌ریسک: چون `SelectAll` فقط id صفحهٔ
   جاری را می‌خواهد، از کالکشن bound فعلی `TableRows` (که قبلاً محاسبه شده و همان صفحه است)
   id‌ها را بردار به‌جای اجرای مجدد فیلتر. اگر `TableRows` برای این کافی نبود، خروجی
   `Filtered()` را یک بار در فیلدی کش کن که در `RenderAll` ست می‌شود و در `SelectAll` مصرف می‌شود
   (کش را هروقت فیلتر/تب/سرچ عوض شد نامعتبر کن — مطابق §13).

## محدودیت‌ها
- فقط همین فایل. رفتار قابل‌مشاهدهٔ UI نباید تغییر کند؛ فقط پیچیدگی الگوریتمی کاهش یابد.
- سبک کد و کامنت‌ها را با `RebuildIndexes` موجود در Patients/Appointments هماهنگ کن و به
  PERFORMANCE.md §2/§13 در کامنت ارجاع بده (کنوانسیون پروژه).
- بعد از تغییر `scripts/sandbox/build.sh` را اجرا کن (0/0).
- کامیت: `perf(cheques): O(1) id index + drop redundant Filtered() recompute (§2/§13)`
```

---

### پرامت ۳ — [🟠 متوسط] حذف فیلتر تکراری صف در Reception و استانداردسازی کالکشن

```
تو یک مهندس ارشد C#/.NET هستی که روی یک اپ دسکتاپ Avalonia کار می‌کنی.

## زمینهٔ پروژه (سشن جدید — این را اول بخوان)
- ریپو: اپ کلینیک دامپزشکی «Animora». استک .NET 10، C# 14، Avalonia 12.1.2، CommunityToolkit.Mvvm. RTL.
- معماری: View → ViewModel → Contracts (DI) → Mock.
- استاندارد پرفورمنس تیم PERFORMANCE.md است. مهم:
  §5 — بازسازی کالکشن bound باید یک‌جا و با یک Reset انجام شود، نه Clear()+Add در حلقه.
  §13 — از محاسبهٔ تکراری derived state خودداری کن.
- کلاس آمادهٔ استاندارد پروژه برای batch:
  `src/Animora.Desktop/Controls/RangeObservableCollection.cs` (متدهای ReplaceAll/AddRange).
  اکثر صفحات دیگر (Patients، Cheques، Vaccination …) از آن استفاده می‌کنند.
- build: `scripts/sandbox/build.sh` (باید 0/0 شود).

## مشکل
فایل: src/Animora.Desktop/Pages/Reception/ReceptionPageViewModel.cs
دو مسئله:

1) اجرای تکراری فیلتر:
   - متد `FilterQueue()` (خط ۲۷۱) هر بار کل صف را Where + OrderByDescending + ThenBy + ToList
     می‌کند.
   - این متد از چند پراپرتی bound صدا زده می‌شود که در هر رندر ممکن است چند بار ارزیابی شوند:
       `TotalCount` (خط ۵۶)  → FilterQueue().Count()
       `CanGoNext` (خط ۷۸)   → FilterQueue().Count()
     و یک‌بار هم در `ApplyFilter` (خط ۲۵۱). یعنی روی هر عملیات، صف چند بار فیلتر می‌شود.

2) ناهماهنگی استاندارد کالکشن:
   - `VisibleQueue` به‌صورت `ObservableCollection<...>` تعریف شده (خط ۴۸) و در `ApplyFilter`
     (خط ۲۵۸–۲۶۳) با `Clear()` و سپس `Add` در حلقه پر می‌شود → به‌ازای هر آیتم یک
     CollectionChanged. این ناقض §5 و ناهماهنگ با بقیهٔ صفحات است.

## کاری که باید انجام دهی
1. یک فیلد کش برای شمارهٔ نتایج فیلترشده اضافه کن، مثلاً:
   `private int _filteredCount;`
   و در انتهای `ApplyFilter` (که همان‌جا `FilterQueue()` را صدا می‌زند) این فیلد را ست کن:
   `_filteredCount = filtered.Count;`
   سپس `TotalCount` و `CanGoNext` را طوری بازنویسی کن که به‌جای صدازدن مجدد `FilterQueue()`
   از `_filteredCount` استفاده کنند. مطمئن شو `ApplyFilter` قبل از خواندن این پراپرتی‌ها
   حداقل یک‌بار اجرا شده (در سازنده از طریق RefreshAsync → ApplyFilter اجرا می‌شود). هر جا
   `_filteredCount` تغییر می‌کند `OnPropertyChanged(nameof(TotalCount))` و
   `OnPropertyChanged(nameof(CanGoNext))` را صدا بزن (متد `NotifyPageState`/`ApplyFilter` را
   کامل کن).
2. نوع `VisibleQueue` را از `ObservableCollection<ReceptionQueueRowViewModel>` به
   `RangeObservableCollection<ReceptionQueueRowViewModel>` تغییر بده (خط ۴۸) و در `ApplyFilter`
   به‌جای `Clear()`+حلقهٔ `Add`، از `VisibleQueue.ReplaceAll(...)` با یک `Select` استفاده کن که
   دقیقاً همان `ReceptionQueueRowViewModel(ticket, start + index + 1)` فعلی را می‌سازد
   (شماره‌گذاری ردیف حفظ شود). `using Animora.Desktop.Controls;` را در صورت نبود اضافه کن.

## محدودیت‌ها
- فقط همین فایل. خروجی UI (صفحه‌بندی، شماره ردیف، شمارنده‌ها) باید عیناً مثل قبل باشد.
- به §5 و §13 در کامنت‌ها ارجاع بده (کنوانسیون پروژه).
- `scripts/sandbox/build.sh` باید 0/0 بدهد.
- کامیت: `perf(reception): cache filtered count + batch VisibleQueue via RangeObservableCollection (§5/§13)`
```

---

### پرامت ۴ — [🟡 استاندارد] جایگزینی `$parent[Button].Foreground` با attached property + inheritance

```
تو یک مهندس ارشد Avalonia هستی که روی یک اپ دسکتاپ RTL کار می‌کنی.

## زمینهٔ پروژه (سشن جدید — این را اول بخوان)
- ریپو: اپ کلینیک دامپزشکی «Animora». استک .NET 10، C# 14، Avalonia 12.1.2. RTL/فارسی.
- آیکن‌ها در سراسر اپ به‌صورت `<Path Classes="icon" .../>` رسم می‌شوند و رنگشان باید رنگِ متن
  دکمهٔ والد را دنبال کند.
- استایل‌های آیکن در `src/Animora.Desktop/Resources/Controls/IconStyle.axaml` هستند.
- build: `scripts/sandbox/build.sh` (باید 0/0 شود). اگر startup/DI/شل تغییر کرد، smoke:
  `scripts/sandbox/run.sh` باید «SMOKE RUN OK» چاپ کند.

## مشکل
در حدود ۸۴ نقطه از فایل‌های XAML، الگوی زیر برای رنگ آیکن داخل دکمه استفاده شده:
    Stroke="{Binding $parent[Button].Foreground}"
`$parent[Type]` در Avalonia معادل RelativeSource FindAncestor است. مستند رسمی پرفورمنس
Avalonia (بخش «Resolve your binding errors») صریحاً توصیه می‌کند از FindAncestor در
DataTemplateها پرهیز شود و به‌جای آن از یک attached property با property inheritance استفاده
شود، چون FindAncestor تا کامل‌شدن قالب درست resolve نمی‌شود و باعث binding-error و افت
پرفورمنس می‌شود.

برای دیدن همهٔ موارد این را اجرا کن:
    grep -rn '\$parent\[Button\]' src --include="*.axaml"

## کاری که باید انجام دهی
راه‌حل استاندارد Avalonia: به‌جای اینکه هر Path به بالا نگاه کند، رنگ را از بالا به پایین
«ارث» بده.
1. بررسی کن که آیا `TextElement.Foreground` روی دکمه‌ها ست می‌شود و ارث‌بری می‌کند. ساده‌ترین و
   idiomaticترین راه در Avalonia: در استایلِ آیکن (`IconStyle.axaml`) مقدار پیش‌فرض
   `Stroke` را به `{Binding $parent[Button].Foreground}` **وابسته نکن**؛ در عوض یک attached
   property تعریف کن (مثلاً `IconTheme.Stroke`) که روی دکمه ست می‌شود و با inheritance به
   Pathها می‌رسد، سپس در استایل آیکن `Stroke` را به آن attached property بایند کن.
   - اگر ساختن attached property سربار زیادی دارد، جایگزین کم‌ریسک‌تر: در استایل‌های دکمه،
     `TextElement.Foreground` را ست کن و در استایلِ `Path.icon` مقدار `Stroke` را با
     `{Binding Path=(TextElement.Foreground), RelativeSource={RelativeSource AncestorType=Button}}`
     جایگزین **نکن** (این هم FindAncestor است). راه درست: تعریف
     `Path.icon { Stroke: {DynamicResource ...} }` وقتی رنگ ثابت است، و فقط برای دکمه‌هایی که
     رنگ متغیر دارند از attached property ارث‌بر استفاده کن.
2. یک الگوی واحد و مستند انتخاب کن و **همهٔ ۸۴ مورد** را یکدست به آن مهاجرت بده. سازگاری بصری
   باید دقیقاً حفظ شود (رنگ آیکن‌ها در حالت عادی/hover/active دکمه‌ها نباید تغییر کند).
3. اگر بعضی موارد در hot path نیستند (مثلاً یک دکمهٔ ثابت در توپ‌بار) و ریسک رگرسیون بصری
   بالاست، می‌توانی آن‌ها را در فهرست استثناها با کامنت مستند کنی؛ اما موارد داخل
   DataTemplate لیست‌ها (آیکن‌های ردیف‌ها/کارت‌ها) اولویت اصلی مهاجرت‌اند.

## محدودیت‌ها
- سازگاری بصری کامل حفظ شود. با DevTools/بازبینی چند صفحه (Cheques, Appointments, Patients)
  چک کن رنگ آیکن‌ها درست است.
- تعداد warningهای binding نباید افزایش یابد (ترجیحاً کاهش یابد).
- `scripts/sandbox/build.sh` = 0/0 و `scripts/sandbox/run.sh` = SMOKE RUN OK.
- کامیت: `perf(xaml): replace $parent[Button] icon bindings with inherited attached property`
```

---

### پرامت ۵ — [🟡 استاندارد] کاهش `<Run>` در TextBlockها

```
تو یک مهندس ارشد Avalonia هستی که روی یک اپ دسکتاپ RTL/فارسی کار می‌کنی.

## زمینهٔ پروژه (سشن جدید — این را اول بخوان)
- ریپو: اپ کلینیک دامپزشکی «Animora». استک .NET 10، C# 14، Avalonia 12.1.2. RTL/فارسی، ارقام فارسی.
- Viewها با x:DataType و Compiled Bindings نوشته شده‌اند.
- build: `scripts/sandbox/build.sh` (باید 0/0 شود).

## مشکل
مستند رسمی پرفورمنس Avalonia (بخش «Minimize use of Run for setting text properties»)
می‌گوید استفاده از `<Run>` داخل `<TextBlock>` برای چسباندن چند تکه متن، عملیات
resource-demanding است و بهتر است متن یک‌جا (ترجیحاً به‌صورت یک پراپرتی از پیش‌ساختهٔ ViewModel)
تنظیم شود. بیشترین موارد در این فایل‌ها هستند:
    src/Animora.Desktop/Pages/History/HistoryPageView.axaml        (۱۰ مورد)
    src/Animora.Desktop/Pages/Reception/ReceptionPageView.axaml    (۷ مورد)
    src/Animora.Desktop/Pages/Cheques/ChequeDrawerView.axaml       (۴ مورد)
برای فهرست کامل:
    grep -rn "<Run " src --include="*.axaml"

نمونهٔ رایج:
    <TextBlock><Run Text="نمایش "/><Run Text="{Binding X}" FontWeight="Bold"/><Run Text=" از "/>...
که سه/چهار Run فقط برای یک جملهٔ ثابت + یک عدد است.

## کاری که باید انجام دهی
برای هر موردی که Runها فقط یک رشتهٔ نهایی می‌سازند (نه استایل واقعاً متفاوت روی هر تکه):
1. یک پراپرتی رشته‌ای در ViewModel مربوطه اضافه کن که کل متن نهایی را یک‌بار (هنگام تغییر
   ورودی) می‌سازد — دقیقاً مطابق الگوی موجود در پروژه که display stringها در سازندهٔ item VMها
   از پیش ساخته می‌شوند (مثلاً `ChequesPresentation.Fa(...)` / `Money(...)`). اگر ViewModel از
   قبل چنین پراپرتی‌ای دارد از آن استفاده کن.
2. در XAML، آن TextBlockِ چند-Run را با `Text="{Binding NewComposedText}"` جایگزین کن.
3. **استثنا:** جاهایی که واقعاً هر Run استایل بصری متفاوتی دارد (مثلاً یک تکه Bold با رنگ
   متفاوت که نمی‌شود در یک رشته گنجاند) را دست نزن — این‌ها کاربرد مشروع Run هستند. فقط
   Runهایی را حذف کن که صرفاً برای الحاق رشته‌اند.
- ابتدا فقط فایل History (پرتکرارترین) را انجام بده، build کن، سپس Reception و بقیه.

## محدودیت‌ها
- متن نهایی نمایش‌داده‌شده (شامل ارقام فارسی و فاصله‌ها) باید عیناً حفظ شود.
- منطق ساخت رشته در ViewModel باشد، نه در code-behind یا Mock (کنوانسیون معماری پروژه).
- `scripts/sandbox/build.sh` = 0/0.
- کامیت: `perf(xaml): collapse concatenation-only <Run> into precomposed VM strings`
```

---

### پرامت ۶ — [🟡 استاندارد] بهینه‌سازی fetch در FinancePageViewModel

```
تو یک مهندس ارشد C#/.NET هستی که روی یک اپ دسکتاپ Avalonia کار می‌کنی.

## زمینهٔ پروژه (سشن جدید — این را اول بخوان)
- ریپو: اپ کلینیک دامپزشکی «Animora». استک .NET 10، C# 14، Avalonia 12.1.2، CommunityToolkit.Mvvm. RTL.
- معماری: View → ViewModel → اینترفیس Animora.Contracts (DI) → Mock.
- PERFORMANCE.md §13: از reload تکراری و کار غیرلازم خودداری کن.
- build: `scripts/sandbox/build.sh` (باید 0/0 شود).

## مشکل
فایل: src/Animora.Desktop/Pages/Finance/FinancePageViewModel.cs، متد `LoadPanelAsync`.
در مسیر overview/income (وقتی تب expense/profit نیست) همیشه هم Overview و هم Income را fetch
می‌کند:
    var overview = await _service.GetOverviewAsync(_range);
    var income   = await _service.GetIncomeAsync(_range);
ولی `income` فقط وقتی `ActiveTab == "income"` مصرف می‌شود (`BuildIncomeExtras(income)`). یعنی
در تب «نمای کلی» یک fetch اضافه انجام می‌شود.

## کاری که باید انجام دهی
1. فراخوانی `GetIncomeAsync` را به داخل شاخهٔ `if (ActiveTab == "income")` منتقل کن تا فقط وقتی
   واقعاً لازم است اجرا شود:
       IncomeData income;
       ... // overview را همیشه بگیر
       if (ActiveTab == "income")
       {
           income = await _service.GetIncomeAsync(_range);
           BuildIncomeExtras(income);
           BuildChart(...);
       }
   (نوع دقیق مقدار بازگشتی `GetIncomeAsync` را از اینترفیس
   `src/Animora.Contracts/Finance/IFinanceQueryService.cs` بخوان و همان را استفاده کن.)
2. بقیهٔ منطق (ساخت KPIها، دسته‌ها، محصولات، دونات‌ها و ...) دقیقاً بدون تغییر بماند.

## نکتهٔ اختیاری (فقط اگر بی‌ریسک بود)
تعویض تب در این صفحه هر بار کل پنل را از سرویس دوباره می‌گیرد. چون سرویس فعلی Mock و سریع است
این مشکل حادی نیست و در PERFORMANCE.md به‌عنوان بدهی قابل‌قبول تلقی می‌شود؛ **آن را در این تسک
تغییر نده** مگر اینکه کش‌کردن per-(tab,range) کاملاً بی‌ریسک باشد.

## محدودیت‌ها
- فقط همین فایل. خروجی UI هر تب باید عیناً مثل قبل باشد.
- `scripts/sandbox/build.sh` = 0/0.
- کامیت: `perf(finance): fetch income panel data only when income tab is active (§13)`
```

---

### پرامت ۷ — [🟢 پاکسازی] استانداردسازی PageViewModelBase.Breadcrumbs

```
تو یک مهندس ارشد C#/.NET هستی که روی یک اپ دسکتاپ Avalonia کار می‌کنی.

## زمینهٔ پروژه (سشن جدید — این را اول بخوان)
- ریپو: اپ کلینیک دامپزشکی «Animora». استک .NET 10، C# 14، Avalonia 12.1.2، CommunityToolkit.Mvvm. RTL.
- `PageViewModelBase` کلاس پایهٔ همهٔ ViewModelهای صفحه است و توپ‌بار به Title/Eyebrow/Breadcrumbs
  آن bind می‌شود.
- build: `scripts/sandbox/build.sh` (باید 0/0 شود). چون شل تغییر می‌کند، smoke هم لازم است:
  `scripts/sandbox/run.sh` باید «SMOKE RUN OK» بدهد.

## مشکل
فایل: src/Animora.Desktop/Shell/PageViewModelBase.cs (خط ۲۵):
    public ObservableCollection<string> Breadcrumbs { get; } = new();
دو نکته:
1) در همهٔ ViewModelها، `Breadcrumbs` فقط یک‌بار در سازنده با `.Add(...)` پر می‌شود و بعد از آن
   هرگز تغییر نمی‌کند. بنابراین `ObservableCollection` (که سربار INotifyCollectionChanged دارد)
   ضروری نیست و می‌تواند به یک `IReadOnlyList<string>` سبک‌تر تبدیل شود — اما این یک ریفکتور
   گسترده است (باید امضای همهٔ ViewModelها بررسی شود).
2) استاندارد پروژه استفاده از collection expression `[]` است، نه `new()`.

## کاری که باید انجام دهی
گام محافظه‌کارانه و بی‌ریسک (این تسک فقط همین را انجام دهد):
1. `new()` را به `[]` تغییر بده تا با کنوانسیون بقیهٔ کدبیس یکدست شود:
       public ObservableCollection<string> Breadcrumbs { get; } = [];
2. بررسی کن هیچ ViewModel دیگری در زمان اجرا به Breadcrumbs بعد از ساخت اضافه/حذف نمی‌کند
   (grep بزن: `grep -rn "Breadcrumbs" src --include="*.cs"`). اگر تأیید شد که فقط در سازنده‌ها
   `.Add` می‌شود، یک کامنت کوتاه اضافه کن که این کالکشن پس از ساخت تغییرناپذیر است (تا مهندس
   بعدی بداند می‌توان بعداً آن را به IReadOnlyList تبدیل کرد).

## محدودیت‌ها
- تبدیل نوع به IReadOnlyList را در این تسک انجام **نده** (ریسک/دامنهٔ بزرگ).
- `scripts/sandbox/build.sh` = 0/0 و `scripts/sandbox/run.sh` = SMOKE RUN OK.
- کامیت: `style(shell): use collection expression for Breadcrumbs to match convention`
```

---

### پرامت ۸ — [🟢 اختیاری/معماری] تفکیک منطق render/filter از ViewModelهای بسیار بزرگ

```
تو یک مهندس ارشد C#/.NET با تمرکز بر خوانایی و معماری MVVM هستی.

## زمینهٔ پروژه (سشن جدید — این را اول بخوان)
- ریپو: اپ کلینیک دامپزشکی «Animora». استک .NET 10، C# 14، Avalonia 12.1.2، CommunityToolkit.Mvvm. RTL.
- معماری: View → ViewModel → Contracts (DI) → Mock. یک الگوی helper به‌نام `*Presentation`
  از قبل در پروژه هست (مثلاً `ChequesPresentation.cs`, `FinancePresentation.cs`,
  `PatientsPresentation.cs`) که فرمت‌کردن/براش/لیبل را از ViewModel جدا می‌کند.
- build: `scripts/sandbox/build.sh` (باید 0/0 شود).

## مشکل (بدهی خوانایی، نه باگ)
چند ViewModel صفحه بسیار بزرگ شده‌اند:
    src/Animora.Desktop/Pages/Appointments/AppointmentsCalendarViewModel.cs  (~۱۳۶۶ خط)
    src/Animora.Desktop/Pages/Cheques/ChequesPageViewModel.cs                (~۱۱۵۶ خط)
    src/Animora.Desktop/Pages/Finance/FinancePageViewModel.cs                (~۸۸۹ خط)
این‌ها God-ViewModel نیستند (یک صفحهٔ واقعاً پیچیده را مدیریت می‌کنند) اما منطق
filter/sort/render درون‌شان قابل انتقال به helperهای خالص و تست‌پذیر است.

## کاری که باید انجام دهی (فقط یکی را در این سشن، ترجیحاً Cheques)
1. منطق **خالص** (بدون وابستگی به state قابل‌مشاهده) را به یک کلاس helper استاتیک منتقل کن:
   - در Cheques: متد `Filtered()` و کمکی‌های `IsOpen`/`DaysLeft` و بلوک‌های ساخت
     KPI/timeline که وابسته به فیلد observable نیستند را به `ChequesPresentation` یا یک کلاس
     جدید `ChequesQueryLogic` منتقل کن، به‌طوری‌که ورودی/خروجی صریح بگیرند (مثلاً لیست چک‌ها +
     پارامترهای فیلتر → لیست خروجی).
2. ViewModel فقط state و فراخوانی helper و به‌روزرسانی کالکشن‌های bound را نگه دارد.
3. رفتار قابل‌مشاهده باید **دقیقاً** حفظ شود؛ این صرفاً بازآرایی داخلی است.
4. اگر پروژه پروژهٔ تست دارد، برای helper منتقل‌شده چند تست واحد ساده اضافه کن (اختیاری).

## محدودیت‌ها
- فقط یک صفحه در این سشن. هیچ تغییر رفتاری/بصری مجاز نیست.
- ساختار فایل/نام‌گذاری را با helperهای `*Presentation` موجود هماهنگ کن (CONVENTIONS.md).
- `scripts/sandbox/build.sh` = 0/0.
- کامیت: `refactor(cheques): extract pure filter/render logic into a testable helper`
```

---

## بخش ۴ — چیزهایی که بررسی شد و مشکلی نداشت (تا دوباره‌کاری نشود)

- `.Result` / `.Wait()` / `.GetAwaiter().GetResult()` روی UI thread: **یافت نشد** (فقط در کامنت‌ها).
- `async void`: **یافت نشد**.
- براش/گرادیانِ per-render در کنترل‌های سفارشی: هویست‌شده به `static readonly` یا کش‌شده.
- هندسهٔ چارت‌ها: با `EnsureCache` و مقایسهٔ ReferenceEquals فقط روی تغییر ورودی بازساخته می‌شود.
- لیست‌های اصلی بزرگ (Cashier/Cheques/Inventory/Purchasing/History/Boarding/Patients):
  Virtualize شده با `ListBox` + `MaxHeight` (کامنت‌های داخلی دلیل MaxHeight را توضیح داده‌اند).
- Vaccination/Reception: با pager صفحه‌بندی شده‌اند (PageSize=8) پس تعداد ردیف realize محدود است.
- ViewModel resolution: بدون reflection، dictionary lookup + سازندهٔ مستقیم (`ViewFactory`).
- Debounce سرچ: یک `DispatcherTimer` واحد و بازاستفاده‌شده.
- build: R2R + TieredPGO + Workstation GC + Concurrent GC (درست برای یک کلاینت دسکتاپ).
- کش‌ها: همگی bounded، lock-free، immutable — **مقدار و سبک کش استاندارد و به‌جاست**.
