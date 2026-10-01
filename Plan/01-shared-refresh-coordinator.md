# تسک ۰۱ — ابزار رفرش مشترک (`PageRefreshCoordinator` + `PageLoadState`)

**وضعیت:** ⬜ TODO
**پیش‌نیاز:** هیچ (اولین تسک)
**هدف:** تعمیم منطق پختهٔ داشبورد به یک ابزار مشترک، Avalonia-free و
unit-testable، که همهٔ صفحات لیستی بتوانند از آن استفاده کنند — بدون وابستگی به
`DashboardSnapshot`. این پایهٔ یکسان‌سازی رفرش در کل برنامه است.

---

## چرا (زمینهٔ خالص — نیازی به کاوش نیست)

`Pages/Dashboard/DashboardRefreshCoordinator.cs` منطق latest-wins عالی دارد اما
به `DashboardSnapshot` چسبیده است (خط ۱۹/۲۲/۵۶: `Fetch` یک
`Task<DashboardSnapshot>` برمی‌گرداند، `Current` از نوع `DashboardSnapshot?`).
برای استفادهٔ مجدد باید روی داده generic شود. همچنین
`Pages/Dashboard/DashboardViewState.cs` منطق حالت دارد اما enum و resolver آن
به نام «Dashboard» و `DashboardSnapshot` گره خورده‌اند.

**تصمیم:** دو نوع جدید generic و خنثی می‌سازیم. داشبورد در این تسک **دست‌نخورده
می‌ماند** (ریسک صفر)؛ مهاجرت صفحات در تسک ۰۳ انجام می‌شود. داشبورد می‌تواند در
آینده هم روی نسخهٔ خودش بماند یا بعداً مهاجرت کند — خارج از دامنهٔ این پلن.

---

## فایل‌هایی که ساخته می‌شوند

### ۱. `src/Animora.Desktop/Shell/PageRefreshCoordinator.cs`
تعمیم `DashboardRefreshCoordinator<TView>` با این تفاوت‌ها:
- جنریک روی **دو** پارامتر: `PageRefreshCoordinator<TSnapshot, TView>` که در آن
  `TSnapshot` دادهٔ خام از سرویس و `TView` حالت ارائهٔ آمادهٔ منتشرشده است.
- `Fetch` delegate: `delegate Task<TSnapshot> Fetch(CancellationToken ct, bool force);`
- سازنده: `(Fetch fetch, Func<TSnapshot, TView> prepare, Action<TView> publish,
  Action<Exception>? onFailed = null, Action? onLoaded = null)`.
- `Current` از نوع `TSnapshot?` (برای کلاس‌ها) — توجه: چون `TSnapshot` ممکن است
  record/کلاس باشد، قید `where TSnapshot : class` بگذار (مثل نسخهٔ داشبورد که
  روی DTO کلاس کار می‌کند). `TView : class` هم مثل اصل.
- بهینهٔ «cache hit ⇒ عدم republish» با `ReferenceEquals(Current, snapshot)`
  **حفظ شود** (این همان کاری است که اجازه می‌دهد سرویس‌های کش‌دار snapshot یکسان
  برگردانند و UI بی‌دلیل بازسازی نشود — قلب خواستهٔ «هر بار همه‌چیز را از صفر
  حساب نکن»).
- تمام منطق باقی‌مانده **بیت‌به‌بیت از `DashboardRefreshCoordinator` کپی شود**:
  `_gate (Lock)`, `_generation`, `_loadCts`, `_inflight`, `_inflightForced`,
  `_rerunForced`, `_active`, `_lastTick`، و متدهای
  `ActivateAsync / TickAsync / Deactivate / Reset / RefreshAsync / Start /
  CancelLocked / RunAsync / LoadOnceAsync / Dispose`، رویداد `StateChanged`،
  ویژگی‌های `IsBusy / Generation / Current / LastUpdated`.
- **پارامتری‌کردن فاصلهٔ poll:** در داشبورد عدد `TimeSpan.FromMinutes(1)`
  هاردکد است (خط ۷۳). در نسخهٔ مشترک یک پارامتر سازنده‌ی اختیاری
  `TimeSpan? pollInterval = null` اضافه کن؛ اگر `null` بود، `TickAsync` مثل حالا
  سقف یک‌دقیقه را اعمال کند. صفحاتی که poll نمی‌خواهند اصلاً `TickAsync` صدا
  نمی‌زنند (و timer نمی‌سازند) — پس پیش‌فرض رفتار را تغییر نده.
- کامنت بالای کلاس را به زبان خنثی (نه «dashboard») بازنویسی کن، کوتاه.

### ۲. `src/Animora.Desktop/Shell/PageLoadState.cs`
تعمیم `DashboardViewState.cs`:
- enum `PageLoadState { InitialLoading, Refreshing, Ready, Empty, Stale, Error }`
  (دقیقاً همان مقادیر و ترتیب).
- `static class PageLoadStateResolver` با متد:
  `Resolve(bool hasData, bool isBusy, bool hasError, bool isEmpty)` — **منطق
  همان `DashboardViewStateResolver.Resolve` است** اما `isEmpty` به‌جای
  `DashboardSnapshot? snapshot` پاس داده می‌شود (تا به DTO داشبورد وابسته نباشد؛
  محاسبهٔ «خالی بودن» را هر صفحه خودش انجام می‌دهد و bool می‌فرستد).
  منطق دقیق:
  ```
  if (!hasData) return hasError && !isBusy ? Error : InitialLoading;
  if (hasError && !isBusy) return Stale;
  if (isBusy) return Refreshing;
  return isEmpty ? Empty : Ready;
  ```
- `Describe(PageLoadState)` با همان متن‌های فارسی عمومی داشبورد (خطوط ۴۶–۵۴ آن
  فایل)، اما «داشبورد» را به عبارت خنثی تبدیل کن:
  - InitialLoading → «در حال بارگذاری…»
  - Refreshing → «در حال به‌روزرسانی…»
  - Empty → «هنوز داده‌ای برای نمایش ثبت نشده است.»
  - Stale → «به‌روزرسانی ناموفق بود؛ آخرین داده نمایش داده می‌شود و ممکن است قدیمی باشد.»
  - Error → «بارگذاری انجام نشد.»
  - پیش‌فرض → `string.Empty`.

---

## فایلی که ویرایش می‌شود

### `tests/Animora.Application.Tests/Animora.Application.Tests.csproj`
تست‌ها فایل‌های Desktop را با `<Compile Include .../>` لینک می‌کنند (نه
ProjectReference). دو فایل جدید را دقیقاً با همان الگوی خطوط ۳۰–۴۳ اضافه کن:
```xml
<Compile Include="../../src/Animora.Desktop/Shell/PageRefreshCoordinator.cs" Link="Navigation/PageRefreshCoordinator.cs" />
<Compile Include="../../src/Animora.Desktop/Shell/PageLoadState.cs" Link="Navigation/PageLoadState.cs" />
```
این لازم است تا تست تسک (پایین) کامپایل شود و تا تسک‌های بعد که صفحات مهاجرت
می‌کنند، لینک شکسته نشود.

---

## تست (سبک — فقط resolver، نه full test)

یک فایل تست کوچک بساز:
`tests/Animora.Application.Tests/PageLoadStateTests.cs` — آینهٔ
`DashboardViewStateTests.cs` اما روی `PageLoadStateResolver.Resolve(hasData,
isBusy, hasError, isEmpty)`. حداقل این موارد (xUnit، مثل فایل مرجع):
- `Resolve(false, true, false, false) == InitialLoading`
- `Resolve(false, false, true, false) == Error`
- `Resolve(true, false, false, true) == Empty`
- `Resolve(true, false, false, false) == Ready`
- `Resolve(true, false, true, false) == Stale`
- `Resolve(true, true, false, false) == Refreshing`
- `Describe` برای همهٔ مقادیر enum غیرخالی برنگرداند جز حالت‌های Ready
  (Ready می‌تواند خالی باشد).

> Coordinator منطق همزمانی دارد؛ **نیازی به تست جدید همزمانی نیست** — کپی بیت‌به‌بیت
> از نسخهٔ داشبوردِ آزموده‌شده است و تست چرخهٔ عمر داشبورد (`DashboardLifecycleTests`)
> همان منطق را پوشش می‌دهد. فقط resolver را تست کن (ارزان و قطعی).

---

## تأیید (سریع — بیلد طولانی ممنوع)

```bash
scripts/sandbox/build.sh        # باید 0 warning / 0 error
dotnet test tests/Animora.Application.Tests --filter PageLoadState   # فقط تست جدید
```
اگر `dotnet test` در سندباکس کند بود، تنها بیلد موفق Desktop کافی است و تست
resolver را می‌توان در تسک ۰۳ همراه مهاجرت اجرا کرد (اما ترجیح: همین‌جا اجرا).

---

## معیار پذیرش (Definition of Done)

- [ ] `PageRefreshCoordinator<TSnapshot, TView>` ساخته شد و Avalonia-free است
      (هیچ `using Avalonia`؛ فقط System/Threading/Tasks).
- [ ] `PageLoadState` + `PageLoadStateResolver` ساخته شد، بدون وابستگی به DTO داشبورد.
- [ ] هر دو فایل در csproj تست لینک شدند.
- [ ] تست `PageLoadStateTests` سبز است (یا بیلد موفق + یادداشت اجرای تست در تسک ۰۳).
- [ ] داشبورد و بقیهٔ کد **بدون تغییر**؛ `scripts/sandbox/build.sh` → 0/0.

---

## قانون پوش (اجباری)

```bash
scripts/git/push.sh "feat(shell): add shared PageRefreshCoordinator + PageLoadState"
```
کد خروج ۳ → `setup_github_environment` سپس تکرار. کد خروج ۲ → حل کانفلیکت
(اولویت ریموت) سپس تکرار.

پس از پوش موفق: وضعیت این فایل را به ✅ DONE تغییر بده.
