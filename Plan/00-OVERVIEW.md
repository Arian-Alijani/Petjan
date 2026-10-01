# پلن: رفرش استاندارد + ترنزیشن نرم بین صفحات — Animora Desktop

> سند شاخص. هر مدلی که می‌خواهد کار کند **اول این فایل را کامل بخواند**، سپس
> فقط فایل تسکِ فعال را. تمام اطلاعات لازم برای پیاده‌سازی در همین پوشه است؛
> نیازی به کاوش مجدد ریپو نیست مگر جایی که تسک صریحاً می‌گوید.

---

## ۱. هدف (یک پاراگراف)

برنامه یک اپ دسکتاپ **آفلاین‌فرست** است (Avalonia + MVVM + SQLite لوکال).
«رفرش» یعنی **کوئری مجدد از دیتابیس لوکال** (میلی‌ثانیه)، نه دانلود شبکه.
هدف این پلن: (الف) یکسان‌سازی رفتار رفرش همهٔ ۱۵ صفحهٔ singleton با یک ابزار
مشترک استاندارد، (ب) افزودن حالت‌های صریح بارگذاری/خطا/خالی به همهٔ صفحات،
(ج) اتصال رفرش رویدادمحور بعد از نوشتن به‌جای اتکای کور به ورود مجدد، (د)
افزودن ترنزیشن نرم هنگام جابجایی بین صفحات + لودینگ هماهنگ، (ه) بهینه‌سازی
تا هر بار کل داده‌ها بی‌دلیل بازخوانی نشوند. همه با کمترین تغییر، استاندارد،
بدون شکستن معماری موجود.

---

## ۲. معماری موجود (واقعیتِ تأییدشده از کد — خالص)

جریان داده (قانون معماری حاکم):
`View → ViewModel → Contracts interface (DI) → Client.Services impl → Application dispatch → SQLite لوکال`.
ViewModelها فقط به interface های `Animora.Contracts` وابسته‌اند؛ هرگز به سرویس
بتُن، EF/SQL یا scope. **این لایه‌بندی نباید در هیچ تسکی شکسته شود.**

### چرخهٔ عمر صفحه (مکانیزم فعلی ناوبری)
- `src/Animora.Desktop/Shell/PageViewModelBase.cs` — کلاس پایهٔ همهٔ صفحات.
  دارای: `IsBusy`, `Title`, `Eyebrow`, `Breadcrumbs` و سه قلاب چرخهٔ عمر که
  همگی پیش‌فرض no-op هستند:
  - `OnNavigatedTo()` — هر بار این صفحه current می‌شود صدا زده می‌شود.
  - `OnNavigatedFrom()` — وقتی صفحهٔ دیگری جایش را می‌گیرد.
  - `ApplyNavigationEntry(NavItem)` — ورودی معنایی ناوبری (مثلاً Finance:
    expense در برابر reports).
- `src/Animora.Desktop/Services/NavigationService.cs` — تنها مسیر انتشار صفحه.
  متد `Publish(...)`: اگر صفحهٔ جدید با فعلی یکی باشد فقط `OnNavigatedTo()`؛
  در غیر این صورت `CurrentViewModel?.OnNavigatedFrom()` سپس تعویض. رویداد
  `CurrentViewModelChanged` به شل اطلاع می‌دهد.
- `src/Animora.Desktop/Shell/MainWindowViewModel.cs` — `CurrentPage` را از
  `_navigation.CurrentViewModel` آینه می‌کند.
- `src/Animora.Desktop/Shell/MainWindow.axaml` — خط ۵۸: محتوا در یک
  `<ContentControl Content="{Binding CurrentPage}" />` داخل یک `ScrollViewer`
  نمایش داده می‌شود. **این ContentControl هیچ ترنزیشنی ندارد → تعویض ناگهانی.**
- `src/Animora.Desktop/Shell/ViewCache.cs` — View هر ViewModel با
  `ConditionalWeakTable` کش می‌شود؛ صفحات singleton View خود را تا پایان عمر
  برنامه نگه می‌دارند (بازگشت به صفحه فوری است، اما داده خودکار تازه نمی‌شود).

### وضعیت رفرش فعلی (شکاف اصلی — تأییدشده)
۱۵ صفحهٔ singleton در DI ثبت شده‌اند (`App.axaml.cs`):
Dashboard, Reception, Patients, Visits, Vaccination, Appointments, Boarding,
Inventory, Purchasing, Sales, Cashier, Cheques, Finance, History, Profile.

| وضعیت | صفحات |
|---|---|
| الگوی کامل: Coordinator + timer + state + OnNavigatedTo/From | **Dashboard** (تنها) |
| فقط `OnNavigatedTo` ساده (رفرش روی ورود، بدون state/coordinator) | Patients, Appointments(Calendar), Visits(Triage) |
| **فقط سازندهٔ `_ = LoadAsync()`؛ هرگز رفرش خودکار نمی‌شوند** | Reception, Vaccination, Boarding, Inventory, Purchasing, Sales, Cashier, Cheques, Finance, History, Profile (۱۱ صفحه) |

این دقیقاً «بدهی فنی» است که در `PERFORMANCE.md` خطوط ۱۹۳–۲۰۶ ثبت شده:
«صفحات singleton در سازنده لود می‌شوند → داده کهنه تا ری‌استارت».

### الگوی مرجع داشبورد (منبع تعمیم)
- `Pages/Dashboard/DashboardRefreshCoordinator.cs` — هستهٔ Avalonia-free،
  latest-wins با generation، coalescing، لغو کار قدیمی، re-run forced، و
  بهینه‌سازی «cache hit ⇒ عدم republish» (`ReferenceEquals(Current, snapshot)`).
  متدها: `ActivateAsync()`, `TickAsync(now)`, `Deactivate()`, `Reset()`,
  `RefreshAsync(force)`, رویداد `StateChanged`, ویژگی `IsBusy/Current/LastUpdated`.
- `Pages/Dashboard/DashboardViewState.cs` — enum حالت‌ها
  (`InitialLoading/Refreshing/Ready/Empty/Stale/Error`) + `DashboardViewStateResolver`
  (منطق Avalonia-free و unit-testable).
- داشبورد از `DispatcherTimer` یک‌دقیقه‌ای فقط-وقتی-فعال استفاده می‌کند.

### لایهٔ سرویس (cache)
- فقط `Animora.Client.Services/Dashboard/DashboardQueryService.cs` کش دارد:
  جفت‌متد `GetSnapshotAsync` (کش‌خوان) + `RefreshSnapshotAsync` (force) روی
  یک `TenantScopedCache` با TTL ۶۰ ثانیه و کلید source-generation.
- بقیهٔ query serviceها (Patients, Inventory, ...) **کش ندارند**؛ هر فراخوانی
  مستقیم به دیتابیس می‌رود. چون SQLite لوکال است این «به‌قدر کافی سریع» است و
  نباید زودهنگام کش اضافه کرد (قانون `PERFORMANCE.md §1`).

### پیام‌رسانی رویدادمحور
- **هیچ event-bus/messenger در حال استفاده نیست.** رفرش بعد از نوشتن امروز با
  **callback دستی** انجام می‌شود (نمونه‌ها: `PatientsPageViewModel` خط ۲۲۵
  `() => _ = LoadAsync()`؛ `ChequesPageViewModel` خط ۹۶۲ `onSaved: () => _ = ReloadAsync()`؛
  `AddPatientWizardViewModel._onCompleted`). این الگوی درون‌صفحه‌ای کار می‌کند
  اما بین صفحات (مثلاً فروش → انبار) پوشش ندارد.

---

## ۳. معماری هدف (سه لایه، به ترتیب اولویت پیاده‌سازی)

1. **یکسان‌سازی (اولویت بالا):** یک `PageRefreshCoordinator` مشترک (تعمیمِ
   دقیق منطق داشبورد، بدون وابستگی به DTO داشبورد) + یک enum/resolver حالتِ
   مشترک `PageLoadState`. سپس همهٔ صفحات لیستی به `OnNavigatedTo`-refresh +
   حالت‌های استاندارد مجهز شوند. → رفع ناسازگاری فعلی و بدهی فنی.
2. **رویدادمحور کردن (اولویت متوسط):** یک کانال invalidate سبک بین صفحات
   (`IDataChangeNotifier`) تا بعد از هر نوشتن، فقط صفحاتِ مرتبط invalidate شوند
   (به‌جای رفرش کامل هر ورود). دکمهٔ رفرش دستی به‌عنوان fallback می‌ماند.
3. **ترنزیشن + لودینگ (اولویت بالا برای UX):** ترنزیشن نرم روی تعویض صفحه در
   شل + یک کنترل loading/overlay مشترک که با حالت داشبورد موجود هماهنگ است.

### تصمیمات طراحی قطعی (برای جلوگیری از فکر مجدد مدل)
- **polling یک‌دقیقه‌ای فقط برای صفحات پرتغییر/چندکاربره** (Dashboard — که دارد،
  و در آینده Appointments/Cashier اگر لازم شد). صفحاتی که فقط با عمل خود کاربر
  عوض می‌شوند (Inventory, Finance, Profile, ...) **polling نمی‌گیرند** —
  اتلاف است. رفرش آن‌ها = ورود + رویداد + دکمهٔ دستی.
- **رفرش کامل لوکال فعلاً قابل قبول است** (کوئری دیسکی ارزان). دلتا/بهینه‌سازی
  ذره‌ای فقط جایی که تسک صریحاً می‌گوید؛ بهینه‌سازی زودهنگام ممنوع (`§1`).
- **Coordinator مشترک باید Avalonia-free و unit-testable بماند** (مثل نسخهٔ
  داشبورد) تا تست‌های موجود الگو را پوشش دهند.
- **دکمهٔ رفرش حذف نمی‌شود**؛ از «مکانیزم اصلی» به «راه فرار» تنزل می‌یابد.
- **لایه‌بندی شکسته نشود**: کانال invalidate در Desktop می‌ماند (UI concern)،
  نه در Contracts/Application. ViewModelها فقط آن را مصرف می‌کنند.

---

## ۴. دسته‌بندی صفحات (رویکرد مناسب هر صفحه — خالص)

| دسته | صفحات | رویکرد |
|---|---|---|
| پرتغییر / آیندهٔ چندکاربره | Dashboard, Appointments, Cashier | ورود + رویداد + polling سبک |
| لیست عملیاتی | Patients, Visits, Vaccination, Boarding, Inventory, Purchasing, Sales, Cheques, Finance, History, Reception | ورود + invalidate رویدادمحور (بدون polling) |
| تقریباً ثابت | Profile | فقط دکمهٔ دستی + رفرش ورود سبک |

روابط invalidate بین‌صفحه‌ای (چه نوشتنی چه صفحه‌ای را کهنه می‌کند — برای تسک ۰۴):
- Sales/Cashier (فروش/پرداخت) → Inventory (موجودی), Finance (درآمد), Dashboard.
- Purchasing (خرید) → Inventory, Finance, Cheques, Dashboard.
- Patients (ثبت مالک/حیوان) → Dashboard (species/counts), Appointments, Visits.
- Visits/Vaccination (ویزیت/واکسن) → Dashboard, History, Patients.
- Cheques (ثبت/وصول چک) → Finance, Dashboard.

---

## ۵. قانون پوش (اجباری — انتهای هر تسک)

پس از اتمام **هر تسک**، به‌صورت اجباری و فوری با اسکریپت پوش ریپو، گیت‌پوش کامل
انجام شود (از ریشهٔ ریپو):

```bash
scripts/git/push.sh "type(scope): <خلاصهٔ فارسی/انگلیسی تسک>"
```

- اگر کد خروج `3` برگشت: ابزار `setup_github_environment` را یک‌بار صدا بزن،
  سپس همان دستور را دوباره اجرا کن.
- اگر کد خروج `2` (کانفلیکت rebase): حل کن (اولویت با کد ریموت)، سپس دوباره اجرا.
- **علاوه بر انتهای تسک:** اگر وسط یک تسک، فیکس سنگین یا کد حجیم نوشته شد، همان‌جا
  هم یک پوش با پیام درست و قابل‌فهم بزن (نگذار تغییر بزرگ بدون پوش بماند).

---

## ۶. قواعد مهم (حاکم بر همهٔ تسک‌ها)

- هیچ چیز اضافی: فقط استاندارد و لازم. بررسی فایل‌ها فقط هدفمند و بر اساس نیاز.
- **بیلد سریع** پس از تغییر: `scripts/sandbox/build.sh` → باید `0 warning / 0 error`.
  اگر startup/DI/shell لمس شد، smoke-run: `scripts/sandbox/run.sh` (باید
  `SMOKE RUN OK`). **Full test / بیلد کامل طولانی ممنوع** مگر صریحاً لازم.
- لایه‌بندی معماری شکسته نشود (بخش ۲). RTL یک‌بار app-wide ست شده؛ تکرار نشود.
- کدها باکیفیت، کم‌تخصیص در حلقه‌ها (`§2`)، batch collection updates (`§5`)،
  بدون بلاک‌کردن UI thread (`§4`)، marshal درست (`§14`).
- کامنت‌ها مختصر و هدفمند؛ از سبک کامنت‌های موجود (چرایی، نه چیستی) پیروی کن.

---

## ۷. فهرست تسک‌ها و ترتیب اجرا

ترتیب الزامی است (هر تسک روی خروجی تسک قبل بنا می‌شود):

| # | فایل | عنوان | خروجی کلیدی |
|---|---|---|---|
| 01 | `01-shared-refresh-coordinator.md` | ابزار رفرش مشترک | `PageRefreshCoordinator<T>` + `PageLoadState` + resolver (Avalonia-free) |
| 02 | `02-page-transition-and-loading.md` | ترنزیشن + لودینگ شل | ترنزیشن نرم ContentControl + کنترل loading مشترک هماهنگ با داشبورد |
| 03 | `03-migrate-pages-to-coordinator.md` | مهاجرت صفحات | ۱۱ صفحهٔ سازنده‌لود → OnNavigatedTo + coordinator + state |
| 04 | `04-event-driven-invalidation.md` | invalidate رویدادمحور | `IDataChangeNotifier` + اتصال نوشتن‌ها و شنونده‌ها |
| 05 | `05-cleanup-and-finalize.md` | پاک‌سازی نهایی | به‌روزرسانی بدهی فنی PERFORMANCE.md + حذف پوشهٔ Plan |

> هر فایل تسک بخش «وضعیت» دارد: `⬜ TODO / 🔄 IN-PROGRESS / ✅ DONE`. مدل پس از
> اتمام، وضعیت را به ✅ تغییر می‌دهد و طبق قانون پوش عمل می‌کند.
