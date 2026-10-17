# پلن اجرایی: ریدیزاین KPI های داشبورد مدیر (Manager Dashboard)

> این فایل خودکفا است. تمام اطلاعات، مسیر فایل‌ها، شماره خطوط، و تصمیمات طراحی داخلش هست.
> یک مدل در سشن جدید **بدون بررسی مجدد ریپو** می‌تواند مستقیم پیاده‌سازی را شروع کند.
> پروژه: اپ دسکتاپ **.NET / Avalonia (`.axaml`)**. پوشه `Animora/` و `Animora-UI/` را بررسی **نکن** (مرجع HTML/JS قدیمی).

---

## 0) قانون قفل‌شده پوش (الزامی بعد از هر تسک)
بعد از تکمیل **هر تسک** (و هر فیکس سنگین میان‌تسک):
```bash
scripts/git/push.sh "type(scope): message"
```
- این اسکریپت: `add -A` → `commit` → `fetch`+`rebase` → `push`. یک خط `✓`/`✗` چاپ می‌کند.
- اگر خروجی exit code `3` داد (نبود credential): ابزار `setup_github_environment` را یک‌بار صدا بزن، بعد همان دستور را دوباره اجرا کن.
- نپوش‌کردن = شکست پروژه.

## قوانین کلی
- بیلد و تست کامل **ممنوع** (یک سیستم GitHub Actions برای بیلد/تست وجود دارد؛ خودش اجرا می‌کند). بررسی فایل اضافه ممنوع.
- طراحی باید **مشابه تصویر نمونه** باشد ولی با **توکن رنگی روشن برنامه** (پس‌زمینه کارت سفید، خط مرجع سبز).
- هر کارت: متن طولانی (دو خط اطلاعات) + نمودار sparkline مشابه نمونه + ابعاد هماهنگ با بقیه کارت‌ها.
- افکت هاور سبک روی KPI ها.
- ناوبری: کلیک روی کارت **صف/ویزیت** → صفحه نوبت‌ها؛ کلیک روی بقیه کارت‌ها → صفحه آمار.

---

## اطلاعات کشف‌شده از ریپو (نتایج خالص — نیازی به بررسی مجدد نیست)

### نقشه‌ی جریان داده KPI (مهم‌ترین بخش)
KPI ها از پایین به بالا این‌طور ساخته و رندر می‌شوند:

1. **تولید آمار (منبع حقیقت):**
   `src/Animora.Client.Services/Dashboard/DashboardQueryService.cs` → متد `DashboardMapping.Map(...)` (حدود خط 113-171).
   - آرایه‌ی `stats` بر اساس `role` ساخته می‌شود. بخش **Manager** (`_ => new[]{...}`) در **خطوط 128-130**:
     ```csharp
     _ => new[] { Stat("IconArrowUpRight", "درآمد امروز", today),
         Stat("IconUsers", "ویزیت امروز", visits), RemainingAppointments(slice),
         Stat("IconBox", "هشدار موجودی", slice.LowStockCount) },
     ```
   - متد کمکی `RemainingAppointments(slice)` (خطوط 263-275) کارت **«صف فعال امروز»** را می‌سازد (`Stat("IconClock","صف فعال امروز",slice.QueueCount)` + Delta درصد انجام‌شده + Foot).
   - `Stat(icon,label,value)` در خط 277-278: رکورد `DashboardStat` می‌سازد.
   - فیلتر دسترسی در خطوط 132-144: بر اساس `stat.IconKey` تصمیم می‌گیرد کارت نمایش داده شود یا نه (`switch` روی IconKey). **هر IconKey جدید باید اینجا هم case بگیرد وگرنه وقتی `slice.Access` مقدار دارد حذف می‌شود.**
   - `StatTone(icon)` خطوط 282-289: tone رنگی هر کارت را از روی IconKey تعیین می‌کند.

2. **مدل داده کارت:** `src/Animora.Contracts/Dashboard/DashboardStat` در `src/Animora.Contracts/Dashboard/DashboardModels.cs` خطوط 27-34:
   ```csharp
   public sealed record DashboardStat(string IconKey, string Label, string Value,
       string? Delta, bool IsDeltaPositive, DashboardTone IconTone = DashboardTone.Brand, string? Foot = null);
   ```
   - `DashboardTone` enum خطوط 11-21: Neutral,Danger,Warn,Info,Ok,Brand,Lilac,Peach.

3. **رندر در View:** `src/Animora.Desktop/Pages/Dashboard/DashboardPageView.axaml` خطوط 100-123:
   - `ItemsControl ItemsSource="{Binding Stats}"` با `AdaptiveGrid MinItemWidth="210" MaxColumns="4"`.
   - هر آیتم → کنترل `<controls:StatCard .../>` با bind: Icon,Value,Label,Foot,Delta,IsDeltaPositive,IconTone.
   - `Stats` از `DashboardPageViewModel.Stats` (خط 186) که از `DashboardSnapshot.Stats` می‌آید.

4. **کنترل کارت:** `src/Animora.Desktop/Controls/StatCard.axaml` + `StatCard.axaml.cs`.
   - XAML فعلی: `Border Classes="card" Padding="18,16" MinHeight="132"`، `FlowDirection=RightToLeft`.
     ساختار: Grid بالا (DeltaChip سمت راست + IconTile 42×42 سمت چپ) + StackPanel پایین (ValueText XL ExtraBold + LabelText Sm + FootText Xs مخفی).
   - code-behind: StyledProperty های Icon(Geometry), Value, Label, Foot, Delta, IsDeltaPositive(bool), IconTone(Tone enum).
     متد `UpdateContent()` متن‌ها را ست می‌کند؛ `UpdateIconTone()` رنگ آیکن‌تایل را از tone می‌گیرد؛ `UpdateDeltaBrushes()` رنگ چیپ delta.
   - **StatCard فعلاً کلیک‌پذیر نیست** و Command ندارد.

### داده‌های موجود در `DashboardSlice` (برای KPI های جدید)
تعریف: `src/Animora.Application/UseCases/Dashboard/DashboardSnapshotQuery.cs` خطوط 45-55.
پرشدن: `src/Animora.Persistence.Sqlite/Queries/DashboardQueries.cs`.
- `slice.ActivityTotals` (رکورد `DashboardActivityTotals(long Payments, int Visits, int Vaccinations, int Cheques)`): **`Visits` = تعداد ویزیت‌های تکمیل‌شده امروز**.
- `slice.Appointments` (`IReadOnlyList<DashboardAppointment>`): نوبت‌های امروز با `State` (Scheduled/Confirmed/CheckedIn/InVisit/Completed/Cancelled/NoShow). این فقط **preview** است (محدود، Completed/Cancelled/NoShow حذف شده — به خط 228 نگاه کن) → برای شمارش دقیق مناسب نیست.
- `slice.Workloads` (`IReadOnlyList<DashboardWorkload>(DoctorId,Total,Name,Done)`): جمع `Total` = کل نوبت‌های فعال امروز، جمع `Done` = نوبت‌های تکمیل‌شده. (در `RemainingAppointments` همین‌طور استفاده شده: `slice.Workloads.Sum(w=>w.Total)` و `Sum(w=>Done)`). **منبع خوب برای «نوبت رزرو شده / انجام شده».**
- `slice.QueueCount` (int): تعداد صف فعال (در حال انتظار).
- `slice.RevenueDays` (`IReadOnlyList<DashboardDay>(int LocalDay, long RevenueMinor)`): سری ۱۴ روزه درآمد روزانه. آخرین عضو = درآمد امروز. **تنها سری تاریخی روزانه‌ی موجود — منبع sparkline.**
- `slice.LowStockCount` (int): موجودی کم (کارت هشدار موجودی که حذف می‌شود).
- **اقلام فروخته شده (تعداد آیتم + درآمد):** در slice **موجود نیست** به‌صورت مستقیم. درآمد امروز = `slice.RevenueDays[^1].RevenueMinor` (متغیر `today` در Map خط 115). تعداد اقلام فروخته‌شده نیاز به داده جدید دارد (پایین ببین «تصمیم داده»).

### توکن‌های رنگی (تم روشن) — `src/Animora.Desktop/Resources/Theme/Colors.axaml`
- `PanelBrush`/`PanelColor` = `#ffffff` (پس‌زمینه کارت).
- `InkBrush`=`#101828` (متن اصلی/مقدار), `Ink2Brush`=`#344054`, `MutedBrush`=`#667085` (لیبل), `Muted2Brush`=`#98a2b3` (ساب).
- `LineBrush`=`#e5eaee`, `Line2Brush`=`#eef2f5` (خطوط/بوردر).
- Ok(سبز)=`#027a48`, `OkBgBrush`=`#ecfdf3`; Info(آبی)=`#1570cd`; Warn=`#b54708`; Lilac=`#a48afb`; Peach; Mint=`#3fa48c`; Brand600=سبز برند.
- خط sparkline مشابه نمونه = **سبز**. از `OkBrush`(#027a48) یا `MintBrush`(#3fa48c) یا `Brand600Brush` استفاده کن. گرادیان خیلی کمرنگ سبز زیر خط.
- کلاس `card` از قبل تعریف شده (در `src/Animora.Desktop/Resources/` استایل‌ها). `FontSizeXl/Lg/Md/Sm/Xs` رزورس‌های StaticResource موجودند.

### کنترل نمودار موجود (مرجع برای ساخت sparkline)
`src/Animora.Desktop/Controls/RevenuePulseChart.cs` — یک `Control` که مستقیم روی `DrawingContext` با `StreamGeometry` + Catmull-Rom smoothing نقاشی می‌کند. نمودار **بزرگ** با محور/گرید/tooltip است. برای KPI باید یک نسخه **sparkline کوچک بدون محور/گرید/tooltip** ساخت یا مستقیم در StatCard یک Control کوچک کشید.
- الگوی smoothing و ساخت StreamGeometry آنجاست؛ کپی ساده‌شده بگیر.
- نمونه‌ی تصویر خط **زیگزاگ تیز (بدون smoothing، LineTo مستقیم)** است → برای sparkline از LineTo ساده استفاده کن (ساده‌تر و شبیه‌تر به نمونه).

### ناوبری (الگوی موجود)
- در `DashboardPageViewModel.cs`: از `_navigation.NavigateTo<TViewModel>()` استفاده می‌شود (مثال‌ها: خط 232 `ChequesPageViewModel`، خط 244 `StatisticsPageViewModel` با `page => page.ShowSectionCommand.Execute(...)`، خط 380 `PatientsPageViewModel`).
- **صفحه نوبت‌ها:** `Animora.Desktop.Pages.Appointments.AppointmentsCalendarViewModel` (تأیید شده از `DashboardQuickAction.cs` خط «نوبت جدید» → `typeof(AppointmentsCalendarViewModel)`).
- **صفحه آمار:** `Animora.Desktop.Pages.Statistics.StatisticsPageViewModel` (دارای `ShowSectionCommand` + enum `StatisticsSection.Overview/Clients/Expenses`).
- `INavigationService` از namespace `Animora.Contracts.Navigation`.

---

## تصمیم داده برای کارت «اقلام فروخته شده» (تسک ۳)
کارت باید نشان دهد: **تعداد اجناس فروخته‌شده** + **درآمد**.
- درآمد امروز از قبل موجود است (`today = slice.RevenueDays[^1].RevenueMinor`).
- تعداد اقلام فروخته‌شده موجود نیست. **تصمیم کم‌هزینه (ترجیح):** فیلد `int SalesItemsCount` (یا نام مشابه) به `DashboardSlice` اضافه کن و در `DashboardQueries.cs` با یک aggregate ساده روی سطرهای فروش امروز پرکن. اگر پیاده‌سازی query ریسک/زمان‌بر شد:
  - **جایگزین مینیمال:** از همان شمارش‌های موجود استفاده کن (مثلاً تعداد فاکتورها یا رویدادهای `Kind=="Sale"` در activity feed) و label را «فروش امروز» بگذار. value = تعداد، Foot = «درآمد: X تومان».
- sparkline این کارت = سری `slice.RevenueDays` (درآمد ۱۴ روزه) — همان منبع، چون روند فروش/درآمد هم‌جهت است.

> ⚠️ اگر مجبور شدی فیلد به `DashboardSlice` و SQL در `DashboardQueries.cs` اضافه کنی = این یک تغییر چندفایلی/سنگین است → **بلافاصله بعد از تکمیلش پوش کن** (قانون فیکس سنگین).

---

## نقشه‌ی KPI نهایی مدیر (بعد از تغییر)
کارت‌های فعلی: [درآمد امروز] [ویزیت امروز] [صف فعال امروز❌] [هشدار موجودی❌]
کارت‌های نهایی (۴ ستون، MaxColumns=4):
1. **درآمد امروز** (موجود، IconArrowUpRight) → ناوبری: آمار. + sparkline درآمد.
2. **ویزیت‌ها** (جدید/بازطراحی، IconUsers یا IconCalendar) → «X نوبت رزرو · Y انجام‌شده». ناوبری: **نوبت‌ها**. + sparkline.
3. **اقلام فروخته شده** (جدید، IconCart/IconReceipt) → «N قلم · درآمد Z تومان». ناوبری: آمار. + sparkline.
4. (اختیاری) اگر فضا/تعادل لازم شد، کارت چهارم را حذف یا با یک KPI معنادار دیگر جایگزین کن تا ۳ یا ۴ ستون متوازن بماند. هدف: صف فعال و هشدار موجودی **حذف**، دو کارت جدید **اضافه**.

> کارت «ویزیت» باید با کلیک به **نوبت‌ها** برود؛ بقیه به **آمار**. این یعنی StatCard باید یک مفهوم «مقصد/کلیک» بگیرد.

---

# تسک‌لیست (به ترتیب اجرا — بعد از هر کدام پوش)

### وضعیت‌ها: ⬜ انجام‌نشده · ✅ انجام‌شده

---

### ✅ تسک T1 — حذف کارت «هشدار موجودی»
**فایل:** `src/Animora.Client.Services/Dashboard/DashboardQueryService.cs`
- در آرایه‌ی Manager (خط ~128-130) عنصر `Stat("IconBox", "هشدار موجودی", slice.LowStockCount)` را حذف کن.
- در فیلتر دسترسی (خطوط 132-144) case مربوط به `"IconBox" => allowed.Inventory` را می‌توانی نگه داری (بی‌ضرر) یا اگر دیگر جایی IconBox در stats مدیر نیست، لازم نیست تغییری بدهی. چیزی را که ممکن است کارت دیگری لازم داشته باشد حذف نکن.
- `StatTone` را دست نزن مگر لازم شود.
> پوش: `fix(dashboard): remove low-stock KPI card`

---

### ✅ تسک T2 — حذف کارت «صف فعال امروز»
**فایل:** `src/Animora.Client.Services/Dashboard/DashboardQueryService.cs`
- در آرایه‌ی Manager، فراخوانی `RemainingAppointments(slice)` را حذف کن.
- متد `RemainingAppointments` (خطوط 263-275) را هم حذف کن **اگر** دیگر هیچ‌جا استفاده نمی‌شود (جستجو کن؛ فقط همین‌جا استفاده شده). اگر مطمئن نیستی، بگذار بماند اما از آرایه حذفش کن.
> نکته: منطق Done/Total این متد در تسک T4 (کارت ویزیت) دوباره لازم می‌شود → می‌توانی به‌جای حذف کامل، آن را بازاستفاده/بازنویسی کنی.
> پوش: `fix(dashboard): remove active-queue KPI card`

> 💡 بهینه: T1 و T2 می‌توانند در **یک کامیت/پوش مشترک** انجام شوند چون هر دو همان فایل و همان آرایه‌اند. در این صورت پیام پوش: `refactor(dashboard): drop low-stock & active-queue KPI cards`.

---

### ✅ تسک T3 — گسترش مدل `DashboardStat` برای sparkline + ناوبری
**فایل:** `src/Animora.Contracts/Dashboard/DashboardModels.cs` (رکورد `DashboardStat` خط 27-34)
- فیلدهای اختیاری اضافه کن (همه با مقدار پیش‌فرض تا سازنده‌های موجود نشکنند):
  - `IReadOnlyList<double>? Spark = null` — سری نرمال‌شده 0..1 برای sparkline (یا `IReadOnlyList<long>? SparkRaw` و نرمال‌سازی در View).
  - `string? NavTarget = null` — کلید مقصد ناوبری: مثلاً `"Appointments"` یا `"Statistics"`.
  - `string? Secondary = null` — خط متن دوم (اگر از `Foot` موجود استفاده نکنی).
- چون رکورد `Stats` را `Freeze` می‌کند، فیلد لیست را هم مثل بقیه `DashboardCollections.Freeze` کن (الگوی خطوط 165-176 را ببین) یا اگر nullable ساده نگه داشتی، در سازنده کپی immutable بگیر. ساده‌ترین: فقط لیست را در Map یک‌بار بساز و دست‌نخورده پاس بده.
> پوش: `feat(contracts): extend DashboardStat with spark series & nav target`

---

### ✅ تسک T4 — ساخت کارت‌های جدید «ویزیت‌ها» و «اقلام فروخته شده» در سرویس
**فایل:** `src/Animora.Client.Services/Dashboard/DashboardQueryService.cs` (متد `Map`, آرایه Manager)
- **کارت ویزیت‌ها:** از `slice.Workloads` → `booked = Workloads.Sum(w=>w.Total)` (یا QueueCount+Done بسته به تعریف «رزرو شده»)، `done = Workloads.Sum(w=>Math.Clamp(w.Done,0,w.Total))`. value = `booked` (تعداد نوبت رزرو شده)، Foot/Secondary = «`done` نوبت انجام شده از `booked`». IconKey = `IconCalendar` یا `IconUsers`. NavTarget=`"Appointments"`. Spark = سری نرمال‌شده‌ی مشتق از RevenueDays یا یک سری ثابت/محاسبه‌شده (اگر سری واقعی ویزیت نداری، از همان `Normalize(slice.RevenueDays...)` یا شکل روند موجود استفاده کن؛ هدف ظاهر نمودار است).
- **کارت اقلام فروخته شده:** طبق «تصمیم داده» بالا. value = تعداد اقلام (یا جایگزین مینیمال)، Foot/Secondary = «درآمد: X تومان» (X = `today` با فرمت `ToPersianDigits`/`N0`). IconKey = `IconCart` یا `IconReceipt`. NavTarget=`"Statistics"`. Spark = سری درآمد `slice.RevenueDays`.
- **کارت درآمد امروز (موجود):** NavTarget=`"Statistics"` + Spark = سری درآمد.
- سری Spark را از `values = slice.RevenueDays.Select(x=>x.RevenueMinor)` بساز و با متد `Normalize` موجود (خطوط 242-249) به 0..1 ببر، یا در View نرمال کن.
- فیلتر دسترسی (خطوط 132-144): برای IconKey های جدید case مناسب اضافه کن:
  - کارت ویزیت (IconCalendar/IconUsers) → `allowed.Appointments` یا `allowed.Visits`.
  - کارت فروش (IconCart/IconReceipt) → `allowed.Sales`.
  در غیر این‌صورت وقتی `slice.Access != null` است این کارت‌ها حذف می‌شوند.
- `StatTone(icon)` (خطوط 282-289) را برای IconKey های جدید بروزرسانی کن تا رنگ آیکن مناسب بگیرند.
> اگر اینجا داده جدید به `DashboardSlice`/`DashboardQueries.cs` اضافه کردی (کارت اقلام) → تغییر چندفایلی = **پوش جدا با پیام واضح**.
> پوش: `feat(dashboard): add visits & items-sold KPI cards with nav + spark`

---

### ✅ تسک T5 — ریدیزاین کامل کنترل `StatCard` (ظاهر مشابه نمونه + sparkline + هاور)
**فایل‌ها:** `src/Animora.Desktop/Controls/StatCard.axaml` + `StatCard.axaml.cs`
طراحی هدف (از تحلیل تصویر نمونه، با توکن روشن برنامه):
- چیدمان کارت: بالا = لیبل کوچک (MutedBrush, FontSizeSm) + یک chevron کوچک `›`/`‹` (RTL → `IconChevronLeft` مثل quick-tile) که «قابل‌کلیک بودن» را نشان دهد. پایین‌چپ = مقدار بزرگ (InkBrush, FontSizeXl, ExtraBold). متن دوم (Secondary/Foot) زیر آن یا کنار (Muted2Brush, Xs). سمت مقابل = **sparkline** که نیمه‌ی عرض کارت را می‌گیرد (مثل نمونه خط سبز زیگزاگ با گرادیان کمرنگ زیرش).
- ابعاد: `MinHeight` فعلی 132؛ در صورت نیاز کمی افزایش بده تا نمودار جا شود، ولی هماهنگ با `AdaptiveGrid MinItemWidth=210`. کارت باید با بقیه هم‌ارتفاع بماند.
- رنگ خط sparkline سبز (`OkBrush`/`MintBrush`/`Brand600Brush`)، گرادیان عمودی خیلی کمرنگ همان سبز تا شفاف زیر خط (الگوی area در RevenuePulseChart خطوط 407-418).
- **افکت هاور سبک:** روی `Border.card` در StatCard یک استایل هاور اضافه کن (pointerover): کمی بالا آمدن (`RenderTransform` translateY -2px) + بوردر کمی پررنگ‌تر (`LineBrush`→`Line2Brush`/براند) + سایه نرم‌تر، با `Transitions` کوتاه (~0.2s CubicEaseOut). الگوی transition در `qt-card` در DashboardPageView خطوط 157-164 موجود است.
- **sparkline رندر:** دو راه:
  1. یک کنترل سبک جدید `SparklineChart : Control` بساز (کپی ساده‌شده‌ی RevenuePulseChart: فقط StreamGeometry خط + area، بدون محور/گرید/tooltip/hover). StyledProperty: `Points (IReadOnlyList<double>)` و `Stroke (IBrush)`. در StatCard یک StyledProperty `Spark` اضافه کن و به این کنترل bind کن.
  2. یا مستقیم در code-behind StatCard یک `Path`/`StreamGeometry` از روی Points بساز و در یک `Canvas`/`Path` نمایش بده. (راه ۱ تمیزتر و قابل‌استفاده‌مجدد است.)
- StyledProperty های جدید StatCard: `Spark (IReadOnlyList<double>?)`، `NavTarget (string?)` یا بهتر `Command`+`CommandParameter`.
- **نکته RTL/نمونه:** نمونه LTR است؛ کارت ما RTL است. چیدمان را آینه کن (لیبل سمت راست، نمودار سمت چپ) تا با برنامه هماهنگ بماند.
> پوش: `feat(ui): redesign StatCard with sparkline, secondary text & hover`

---

### ⬜ تسک T6 — کلیک‌پذیر کردن KPI ها + سیم‌کشی ناوبری
**فایل‌ها:** `DashboardPageView.axaml` (خطوط 100-123) + `DashboardPageViewModel.cs` + `StatCard`
- در `DashboardPageViewModel` یک `[RelayCommand] private void OpenStat(DashboardStat? stat)` اضافه کن:
  - اگر `stat.NavTarget == "Appointments"` → `_navigation.NavigateTo<Animora.Desktop.Pages.Appointments.AppointmentsCalendarViewModel>();`
  - در غیر این‌صورت (پیش‌فرض/`"Statistics"`) → `_navigation.NavigateTo<Animora.Desktop.Pages.Statistics.StatisticsPageViewModel>();` (در صورت نیاز با `page => page.ShowSectionCommand.Execute(StatisticsSection.Overview)` مثل الگوی خط 244).
  - usings لازم: `Animora.Desktop.Pages.Appointments`, `Animora.Desktop.Pages.Statistics` (آمار از قبل import است).
- در DataTemplate (خط 113-121) کل StatCard را در یک `Button` با `Classes` شفاف بپیچ یا به StatCard یک `Command`/`CommandParameter` بده:
  ```xml
  <controls:StatCard ... (bindings فعلی)
      Command="{Binding $parent[UserControl].((dashboard:DashboardPageViewModel)DataContext).OpenStatCommand}"
      CommandParameter="{Binding}" />
  ```
  (اگر StatCard را Button-wrap کردی، همان الگوی AlertBox خط 62-66 را دنبال کن: `Button Background=Transparent BorderThickness=0 Padding=0 Cursor=Hand`.)
- `Cursor="Hand"` روی کارت برای حس کلیک‌پذیری.
> پوش: `feat(dashboard): make KPI cards clickable with routing (appointments/statistics)`

---

### ⬜ تسک T7 — بازبینی نهایی ظاهری و سازگاری
- مطمئن شو ۴ ستون/۳ کارت متوازن است و کارت‌ها هم‌ارتفاع‌اند (sparkline باعث بلندتر شدن یکی نشود).
- مطمئن شو هیچ ارجاع باقی‌مانده به «هشدار موجودی»/«صف فعال امروز» در آرایه Manager نمانده.
- مطمئن شو IconKey های جدید در Icons.axaml موجودند (`IconCalendar`,`IconCart`,`IconReceipt`,`IconUsers`,`IconArrowUpRight`,`IconChevronLeft` — همه از قبل در پروژه استفاده شده‌اند، بنابراین موجودند).
- بیلد/تست محلی نزن؛ به GitHub Actions بسپار.
> پوش: `chore(dashboard): finalize KPI redesign review`

---

### ⬜ تسک T8 (پایانی) — حذف این فایل پلن
- فقط همین فایل `Plan/KPI_REDESIGN_PLAN.md` را حذف کن (به هیچ فایل دیگر و هیچ رودمپ دیگری دست نزن).
- سپس پوش نهایی.
> پوش: `chore: remove KPI redesign plan file`

---

## یادآوری نهایی
- بعد از **هر** تسک بالا: `scripts/git/push.sh "..."`.
- فقط فایل‌های مرتبط با KPI را لمس کن. پوشه `Animora/` و `Animora-UI/` را باز نکن.
- اگر وسط یک تسک کد سنگین/چندفایلی نوشتی، همان‌جا پوش کن.
