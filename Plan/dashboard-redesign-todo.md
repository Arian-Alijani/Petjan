# پلن بازطراحی لای‌اوت داشبورد (Animora .NET / Avalonia Desktop)

> فایل کاری خودکفا. مدلِ ادامه‌دهنده نباید نیاز به بررسی دوباره داشته باشد — همه نتایجِ کشف‌شده اینجا آمده.
> قانون قفل‌شده: **بعد از تکمیل هر تسک، `scripts/git/push.sh "msg"` اجرا شود** (نبودن push = شکست).
> بیلد/تست ممنوع. بررسی فایل اضافی ممنوع. پوشه `Animora-UI/` و `src/Animora.*` که ربطی ندارد را باز نکن.

---

## 0) زمینه کشف‌شده (حقایق خالص — دیگر نیازی به جستجو نیست)

### فایل‌های هدف (فقط همین‌ها لمس می‌شوند)
| نقش | مسیر |
|---|---|
| توکن رنگی (تم) | `src/Animora.Desktop/Resources/Theme/Colors.axaml` |
| لای‌اوت نقش Manager (باکس‌های اصلی) | `src/Animora.Desktop/Pages/Dashboard/ManagerDashboardView.axaml` |
| wrapper داشبورد (KPI + AlertBox + QuickActions) | `src/Animora.Desktop/Pages/Dashboard/DashboardPageView.axaml` |

> **KPI دست‌نخورده می‌ماند** (ردیف `Stats` در `DashboardPageView.axaml` خط ۱۰۱–۱۲۷). تغییری در آن نده.
> wrapper فقط ممکن است برای جابجایی AlertBox لمس شود (تسک ۲ را ببین).

### معماری رندر داشبورد (کشف‌شده)
- `DashboardPageView.axaml` = پوسته. ترتیب فعلی عمودی: state banners → **AlertBox "هشدارهای فوری"** (خط ۳۴) → ردیف **KPI/Stats** (۴ ستون) → `RoleContentHost` (محتوای نقش) → **QuickActions** "دسترسی سریع".
- `RoleContentHost` بر اساس نقش یکی از این‌ها را لود می‌کند (از `DashboardPageView.axaml.cs` خط ۴۷–۵۱): Manager/Receptionist → `ManagerDashboardView`، Cashier → `CashierDashboardView`، Doctor → `DoctorDashboardView`.
- **باکس‌هایی که کاربر در تصویر منظور دارد در `ManagerDashboardView.axaml` هستند.** این فایل محور کار است.

### محتوای فعلی `ManagerDashboardView.axaml` (کشف‌شده)
StackPanel با Spacing=16، دو ردیف `AdaptiveGrid`:
- **Row 1** (`AdaptiveGrid MinItemWidth=340 MaxColumns=2 Weights="1,1"`):
  - ستون۰: کارت **«روند درآمد ۱۴ روز اخیر»** → `controls:RevenuePulseChart` داخل Border با `Height="196"`. Binding: `RevenuePulse`. (این همان Reports تصویر است.)
  - ستون۱: کارت **«بار کاری پزشکان امروز»** → لیست `DoctorLoads` با progress bar. Binding: `HasDoctorWorkload`.
- **Row 2** (`AdaptiveGrid MinItemWidth=300 MaxColumns=3`):
  - ستون۰: کارت **صف زندهٔ امروز** → عنوان `{Binding PrimaryListTitle}`، لیست `PrimaryList`، خالی `ShowPrimaryEmpty`. (→ نقشِ «فعالیت‌های اخیر»)
  - ستون۱: کارت **«بیماران بر اساس گونه»** → donut `controls:DonutRing` (Slices=`SpeciesSlices`) + medallion مرکزی + legend (`Species`). Binding نمایش: `HasSpecies`. (→ همان Analytics/donut تراکم تصویر)
  - ستون۲: کارت **هشدارهای مدیریتی** → عنوان `{Binding SecondaryListTitle}`، لیست `SecondaryList`، خالی `ShowSecondaryEmpty`. (→ نقشِ «اخطار»)

### نگاشت تصویر نمونه → باکس‌های واقعی (تصمیم‌گرفته‌شده)
تصویر نمونه فقط **لای‌اوت** را می‌دهد؛ محتوای باکس‌ها عوض نمی‌شود، فقط جای‌گذاری طبق تصویر:
- تصویرِ «اخطار» (چپ-بالا)      = کارت **هشدارهای مدیریتی** (`SecondaryList`)
- تصویرِ «فعالیت‌های اخیر» (چپ-پایین) = کارت **صف زندهٔ امروز** (`PrimaryList`)
- تصویرِ «Reports» (راست-پایین عریض) = کارت **روند درآمد** (`RevenuePulseChart`)
- تصویرِ «Analytics donut» (راست-بالا، راست‌ترین) = کارت **بیماران بر اساس گونه** (donut)
- تصویرِ دو باکسِ سمت‌چپِ donut (راست-بالا) = **کارت «بار کاری پزشکان امروز» (موجود) + یک باکس دوم جدید که خودمان طراحی می‌کنیم** (جزئیات در تسک ۶).

### لای‌اوت هدف نهایی (طبق تصویر — RTL، پس «چپِ تصویر» از نظر گرید سمت پایانِ ردیف است)
یک `AdaptiveGrid` بیرونیِ دو ستونه با Weights ≈ "1.1, 2" (ناحیهٔ چپِ باریک + ناحیهٔ اصلی پهن):
```
┌ ناحیه اصلی (پهن، ستون۰ گرید) ─────────────┐  ┌ ستون باریک (ستون۱ گرید) ┐
│  ردیف بالا: [باکس۲ جدید][بار کاری][donut] │  │  ┌ اخطار (هشدار مدیریتی) ┐ │
│  ردیف پایین: [ روند درآمد  — عریض ]        │  │  └───────────────────┘ │
│                                           │  │  ┌ فعالیت اخیر (صف) ────┐ │
└───────────────────────────────────────────┘  │  └───────────────────┘ │
```
> نکته RTL: در Avalonia با FlowDirection=RTL، ستونِ `Grid.Column=0` سمت راستِ نمایش می‌افتد. تصویر هم RTL است (عنوان‌ها فارسی راست‌چین). پس ناحیهٔ اصلیِ پهن = ستون راست نمایش، باکس‌های اخطار/فعالیت = ستون چپ نمایش. این را با ترتیب ستون‌ها در `AdaptiveGrid` تنظیم می‌کنیم: دو آیتم → Weights="2,1" ⇒ آیتم اول پهن، آیتم دوم باریک. در RTL آیتم اول سمت راست می‌نشیند. ✔ مطابق تصویر.

### سیستم رنگ فعلی (تم teal/سبز که باید عوض شود)
`Colors.axaml` توکن‌های Brand الان **teal**:
`Brand900=#0c3631, Brand800=#104a43, Brand700=#0e7566, Brand600=#0d9488, Brand500=#14b8a6, Brand400=#2dd4bf, Brand100=#ccfbf1, Brand50=#f0fdfa`.
همین مجموعه در کل اپ به‌عنوان رنگ برند استفاده می‌شود (آیکون‌ها `Brand700Brush`، چیپ‌ها `Brand50Brush`). تغییرِ فقط همین ۸ مقدار = تغییر تم کل اپ به آبی-بنفش.

توکن‌های وابسته به teal که باید هماهنگ شوند (مقادیر فعلی مایل به سبز دارند):
- surfaces: `Bg=#f6f8f9, Bg2=#f1f5f6` (ته‌مایه سبز) → خنثیِ آبی: `#f6f8fb, #f1f4f9`.
- `RowActive=#f9fbfa, ChartTrack=#f0f4f2, MedallionLine=#e7ecea, MedallionEdge=#fbfdfc` (ته‌سبز) → خنثی/آبی ملایم.
- `ChartAreaTop=#5914B8A6, ChartAreaBottom=#0014B8A6` (مبتنی بر Brand500 teal) → مبتنی بر آبیِ جدید.
- semantic (`Danger/Warn/Info/Ok`)، `Lilac/Peach/Rose/Mint`، `Shot` دست‌نخورده می‌مانند (پالت لهجه‌ایِ خنثی؛ info/lilac/peach با تم آبی هم‌خوان‌اند).

### پالت هدف (استخراج‌شده از تصویر — آبیِ cornflower + indigo، مینیمال و آرام)
Primary blue ≈ `#4a6cf7` (کارت آبیِ وسط، لوگو). مشتقات تیره‌تر/روشن‌تر برای مقیاس Brand:
```
Brand900=#1e2545  Brand800=#283徐(→#2a3a8f)  ... (مقادیر دقیق در تسک ۷)
```
آیکون‌ها/استروک اصلی = آبی تیره‌تر (Brand700 ≈ `#3a56d4`)؛ چیپ‌های نرم = آبی بسیار روشن (Brand50 ≈ `#eef2fe`).

### نمودار درآمد — نکته مهم (کشف‌شده، نیاز به کار ندارد)
`src/Animora.Desktop/Controls/RevenuePulseChart.cs` از قبل گرادیان خطِ آبی→بنفش→magenta دارد
(`#5BA8E9 → #8B85EA → #AB6BE7 → #EE62C9`) و area tint بنفش `#A878E8`. **دقیقاً مانند «Reports» تصویر است.** ⇒ رنگ نمودار تغییر نمی‌کند. فقط `ChartAreaTop/Bottom` در Colors (که استفاده نمی‌شود در این کنترل چون کنترل مقدار hardcode دارد) هماهنگ می‌شود؛ ضروری نیست اما برای یکپارچگی تم انجام می‌شود.

### سیستم متن/RTL (کشف‌شده)
- باکس‌های کارت (اخطار، صف، گونه، بار کاری) متن فارسی را درست نشان می‌دهند (TextBlock معمولی + ارثِ RTL از ریشه). ⇒ **از همین سیستم موجود برای هر متن جدید استفاده شود** (TextBlock با `FontSize={StaticResource ...}`، `Foreground={StaticResource InkBrush/MutedBrush}`). نیازی به FlowDirection دستی نیست.
- فقط `RevenuePulseChart` داخل خودش FlowDirection را دستی مدیریت می‌کند (کارِ ما نیست؛ لمس نشود).

### استایل‌ها/توکن‌های قابل‌استفاده (آماده)
- کارت: `Classes="card"` (Border، شعاع `Radius`، سایه `ShadowCard`، حاشیه `Line2Brush`).
- هدر کارت: `StackPanel/Grid Classes="card-header"` + `Border Classes="card-header-rule"` + `TextBlock Classes="card-title"` / `card-header-meta`.
- فونت‌ها: `FontSizeXs, FontSizeSm, FontSizeMd` (در Typography.axaml). رنگ متن: `InkBrush, Ink2Brush, MutedBrush, Muted2Brush`.
- فاصله‌گذاری استاندارد: Spacing بین کارت‌ها 16، ColumnSpacing/RowSpacing 16، padding کارتِ محتوایی 18 (طبق الگوی موجود). برای چیدمان جدید همین ۱۶/۱۸ حفظ شود.
- `controls:AdaptiveGrid` props: `MinItemWidth, MaxColumns, Weights("a,b"), ColumnSpacing, RowSpacing`. Weights فقط وقتی همه آیتم‌ها در یک ردیف جا شوند اعمال می‌شود.

### قید ابعاد (برای اینکه در مانیتور استاندارد از کادر پایین بیرون نزند)
- کارت روند درآمد چارت را با `Height="196"` دارد (ثابت). در لای‌اوت جدید عریض‌تر می‌شود ولی ارتفاع چارت را می‌توان نگه داشت یا کمی تنظیم کرد.
- باکس‌های چپ (اخطار/فعالیت) باید مجموعاً با ارتفاع ناحیهٔ اصلی هم‌خوان شوند. راهکار: هر دو را در یک `Grid RowDefinitions="*,*"` مساوی قرار بده تا با قد ستونِ کناری کشیده شوند؛ لیست داخل‌شان با `ScrollViewer`/محدودیت ارتفاع سرریز نکند. از `MaxHeight` روی ItemsControlها یا گذاشتن‌شان داخل سطرهای `*` استفاده شود تا بیرون نزنند.
- `DashboardPageView` پایین صفحه Margin=92 برای دکمهٔ شناور دارد؛ این فضا حفظ شود.

---

## 1) تسک‌ها (وضعیت: ⬜ انجام‌نشده / ✅ انجام‌شده)

### ⬜ T0 — آماده‌سازی ریپو و Plan
- پوشه `Plan/` از قبل هست؛ همین فایل todo ساخته شد. برنچ فعلی `main`.
- **اکشن:** هیچ. (این فایل + ساختار اولیه کافی است.)
- **push:** `scripts/git/push.sh "docs(plan): add dashboard redesign todo"`

### ⬜ T1 — جابجایی باکس «هشدارهای فوری» (AlertBox) از بالا به ستون چپ
- **چرا:** تصویر «اخطار» را سمت چپ و پایین‌ترِ KPI می‌خواهد، نه به‌عنوان نوار بالای صفحه.
- **تصمیم:** خودِ `AlertBox` در `DashboardPageView.axaml` (خط ۳۴–۹۸) یک باکس *سراسریِ cross-role* است. اما باکس «اخطار»ِ تصویر از نظر محتوایی با **کارت هشدارهای مدیریتی داخل ManagerView** هم‌نقش است. برای اجتناب از دوباره‌کاری و حفظ همهٔ نقش‌ها:
  - **AlertBox سراسری در `DashboardPageView` حذف/غیرفعال نشود** (برای Cashier/Doctor لازم است) — اما جای آن در ترتیب عمودی عوض نمی‌شود چون سراسری است.
  - کارِ جابجایی «اخطار به چپ» صرفاً داخل **ManagerDashboardView** با لای‌اوت جدید انجام می‌شود (تسک ۵): کارت هشدارهای مدیریتی به ستون چپِ بالا می‌رود.
  - ⇒ **T1 عملاً داخل T5 ادغام می‌شود.** این تسک فقط تصمیم مستند است. (بدون تغییر کد مجزا.)
- **نکته:** اگر کاربر AlertBox سراسریِ بالای KPI را هم بخواهد پایین بیاید، نیاز به تصمیم جدا دارد؛ فعلاً طبق «استفاده از لای‌اوت نمونه» کانون کار Manager است.
- **push:** بدون تغییر کد ⇒ همراه T5 push می‌شود (این تسک تنها مستندسازی تصمیم است، push مستقل لازم ندارد).

### ⬜ T2 — طراحی دو باکس جدیدِ کنار donut (محتوا/کاربرد)
- **محل:** ردیف بالای ناحیهٔ اصلی، سمت چپِ donut. طبق تصویر دو باکس کوچک.
- **باکس A (موجود، بازاستفاده):** «بار کاری پزشکان امروز» (`DoctorLoads`, `HasDoctorWorkload`). حفظ می‌شود.
- **باکس B (جدید — خودمان):** کاربردِ انتخابی = **«خلاصهٔ امروز کلینیک» (KPI کوچکِ عملیاتی)**. محتوا از bindingهای موجودِ ViewModel که قطعاً وجود دارند استفاده کند تا بدون تغییر ViewModel کار کند. گزینهٔ امنِ بدون ریسک: یک کارت کوچکِ **«میانبر وضعیت»** که از `PrimaryList`/`SecondaryList` شمارش‌ها یا از متن‌های موجود استفاده می‌کند.
  - ⚠️ برای نزدن به ViewModel (کار اضافه/ریسک بیلد)، باکس B از همان bindingهای نمایش‌داده‌شدهٔ موجود (مثل `SpeciesTotal`, `SpeciesTotalLabel`, شمارشِ لیست‌ها) یا از عناصر صرفاً تزئینی/ثابتِ متنی استفاده کند. **ترجیح:** استفاده از داده‌های موجود donut/صف برای یک «کارت نرخ/خلاصه».
  - طراحی: کارت `Classes="card"`، هدر `card-title` «خلاصهٔ امروز»، بدنه دو سطرِ آمارِ کوچک (عدد درشت `InkBrush` + برچسب `MutedBrush`) با چیپ آیکونِ نرم `Brand50Brush`/استروک `Brand700Brush`.
- **تصمیم نهاییِ محتوای باکس B (قفل):** دو متریکِ «در انتظار» و «تکمیل‌شدهٔ امروز» از شمارشِ `PrimaryList` (صف) — اگر bindingِ آماده نبود، به دو متریکِ donut (`SpeciesTotal` + تعداد گونه‌ها) سوییچ شود. جزئیات دقیق هنگام اجرا با دیدن ViewModel تثبیت می‌شود (فقط اگر لازم شد ViewModel خوانده شود، نه بیشتر).
- **push:** این تسک طراحی/تصمیم است؛ پیاده‌سازی در T5 و همان‌جا push.

### ⬜ T3 — تثبیت نگاشت و قید ابعاد (مستند؛ بدون کد)
- نگاشت باکس‌ها (بخش ۰) و قیدِ «بیرون نزدن از کادر» (بخش ۰، «قید ابعاد») تثبیت شد.
- **اکشن اجرایی در T5:** باکس‌های چپ (اخطار+فعالیت) در `Grid RowDefinitions="*,*" RowSpacing=16` تا با ناحیهٔ اصلی هم‌قد شوند و لیست‌هایشان با `MaxHeight`/کلیپ سرریز نکنند.
- **push:** همراه T5.

### ⬜ T4 — بازنویسی تم رنگی teal → آبی/بنفشِ مینیمال (`Colors.axaml`)
- **فایل:** `src/Animora.Desktop/Resources/Theme/Colors.axaml`
- **تغییرات دقیق (فقط مقادیر؛ کلیدها ثابت):**
  - مقیاس Brand (teal → آبیِ cornflower/indigo):
    ```
    Brand900=#1b2559
    Brand800=#233072
    Brand700=#3a56d4   (استروک آیکونِ اصلی)
    Brand600=#4a6cf7   (primary — کارت آبیِ تصویر)
    Brand500=#5b7cfa
    Brand400=#8aa2fb
    Brand100=#dbe4fe
    Brand50 =#eef2fe   (چیپِ نرم)
    ```
  - surfaces خنثی‌ترِ آبی:
    ```
    Bg=#f6f8fb   Bg2=#eef1f8
    Line=#e6eaf2 Line2=#eef1f7
    ```
  - توکن‌های داشبوردِ ته‌سبز → خنثی/آبی:
    ```
    RowActive=#f7f9fe  ChartTrack=#eef1f8  MedallionLine=#e7eaf3  MedallionEdge=#fbfcff
    ChartAreaTop=#594a6cf7  ChartAreaBottom=#004a6cf7
    ```
  - دست‌نخورده: Ink/Muted، Danger/Warn/Info/Ok، Lilac/Peach/Rose/Mint، Shot، Table/Inset/RowHover/InputHover، OnAccent.
- **خروجی:** کل اپ به تم آبی‌بنفشِ آرام می‌رود؛ KPI و همه چیز هماهنگ.
- **push:** `scripts/git/push.sh "style(theme): switch brand palette teal→blue-indigo (dashboard redesign)"`

### ⬜ T5 — بازچینش لای‌اوت `ManagerDashboardView.axaml` طبق تصویر
- **فایل:** `src/Animora.Desktop/Pages/Dashboard/ManagerDashboardView.axaml`
- **ساختار جدید:** StackPanel بیرونی → یک `controls:AdaptiveGrid MinItemWidth=... MaxColumns=2 Weights="2,1" ColumnSpacing=16 RowSpacing=16`:
  - **آیتم ۱ (ناحیه اصلیِ پهن):** `StackPanel Spacing=16`
    - **ردیف بالا:** `AdaptiveGrid MaxColumns=3 ColumnSpacing=16`:
      - کارت **باکس B جدید** (T2)
      - کارت **بار کاری پزشکان** (موجود؛ منتقل‌شده)
      - کارت **بیماران بر اساس گونه / donut** (موجود؛ منتقل‌شده)
    - **ردیف پایین:** کارت **روند درآمد** (عریض، تمام‌عرضِ ناحیهٔ اصلی). ارتفاع چارت ~196 حفظ؛ در صورت نیاز 180.
  - **آیتم ۲ (ستون باریک):** `Grid RowDefinitions="*,*" RowSpacing=16`:
    - سطر۰: کارت **هشدارهای مدیریتی** (اخطار)
    - سطر۱: کارت **صف زندهٔ امروز** (فعالیت اخیر)
    - هر دو هم‌اندازه (`*`) و هم‌قد؛ لیست‌ها با `MaxHeight` یا کلیپ تا از پایین بیرون نزنند.
- **حفظِ همهٔ bindingها و IsVisibleها دقیقاً مثل قبل** (فقط جابجایی container؛ منطق عوض نشود).
- **استایل‌های `UserControl.Styles`** (legend/donut/dc-*) دست‌نخورده بمانند.
- **فاصله‌گذاری:** 16 بین همه، padding کارت‌ها طبق الگوی موجود (18).
- **push:** `scripts/git/push.sh "feat(dashboard): new layout — alerts+queue left column, donut/workload/summary top, revenue wide bottom"`

### ⬜ T6 — بازبینی نهاییِ بصری/هم‌خوانی (بدون بیلد)
- چک‌لیست چشمی روی دیفِ دو فایل:
  - [ ] هیچ `StaticResource`ِ نامشخص اضافه نشده (همه کلیدها در Colors/Spacing/Typography موجودند).
  - [ ] هیچ bindingی حذف/نام‌عوض نشده نسبت به نسخهٔ قبلِ ManagerView.
  - [ ] KPI و QuickActions و AlertBox سراسری در `DashboardPageView` دست‌نخورده.
  - [ ] فاصله‌ها ۱۶/۱۸ یکدست.
  - [ ] donut و medallion و legend binding‌ها سرجای خود.
- **push:** اگر اصلاحی لازم شد: `scripts/git/push.sh "fix(dashboard): spacing/binding polish"`؛ اگر نه، push مستقل لازم نیست.

### ⬜ T7 — حذف فایل پلن (تسک پایانی)
- `rm Plan/dashboard-redesign-todo.md`
- فقط همین فایل حذف شود؛ به بقیه فایل‌ها و رودمپ‌ها کاری نداشته باش.
- **push:** `scripts/git/push.sh "chore(plan): remove completed dashboard redesign todo"`

---

## 2) یادآوری قوانین قفل‌شده
- بعد هر تسکِ دارای تغییر کد → فوراً `scripts/git/push.sh "<msg>"`. اگر exit code = 3 → ابزار `setup_github_environment` یک بار صدا زده شود، سپس دوباره push.
- بیلد/تست ممنوع. بررسی فایلِ بی‌هدف ممنوع. `Animora-UI/` باز نشود.
- KPIها تغییر نکنند. سیستم متنِ موجود برای متن‌های جدید استفاده شود.
- در صورت فیکس/کدِ سنگین حین هر تسک → همان‌جا push جداگانه با پیام واضح.
