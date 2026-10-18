# پلن بازطراحی کامل پنل ادمین (Operator Console) — Animora

> **این فایل یک تسک‌لیست خودبسنده است.** هر چیزی که یک مدل هوش مصنوعی برای ادامه‌ی کار در یک سشن کاملاً جدید لازم دارد اینجا نوشته شده؛ نیازی به بررسی دوباره‌ی ریپو یا نتیجه‌گیری مجدد نیست. فقط فایل‌های ذکرشده را باز کن و پیاده‌سازی را شروع کن.

---

## ۰. قوانین قفل‌شده (خواندن اجباری قبل از شروع)

1. **قانون پوش (مرگ/زندگی):** بعد از تکمیل **هر تسک** از این لیست، این دستور را اجرا کن:
   ```bash
   cd /home/user/webapp && scripts/git/push.sh "type(scope): خلاصه فارسی/انگلیسی تسک"
   ```
   - اگر exit code = 3 بود → ابزار `setup_github_environment` را یک‌بار صدا بزن، بعد دوباره همان دستور را اجرا کن.
   - اگر exit code = 2 بود → rebase conflict؛ ریپو تمیز مانده، conflict را به نفع remote حل کن و دوباره اجرا کن.
   - اگر **حین** یک تسک فیکس/کد سنگینی نوشتی، همان وسط هم پوش بزن (commit اتمیک با پیام واضح).
2. **برنچ:** کار روی برنچ فعلی. اگر لازم شد `-b genspark_ai_developer`.
3. **دایرکتوری `Animora-UI` را هرگز باز نکن.** (بی‌ربط به این تسک.)
4. **بررسی هدفمند:** فقط فایل‌های لازم را بخوان. بررسی بیهوده ممنوع.
5. **بیلد/تست:** `dotnet` در sandbox **نصب نیست** → build محلی ممکن نیست. Full test / بیلد طولانی **ممنوع**. کد باید از نظر صحت دستی و با دقت نوشته شود (قراردادهای تایپ بین Web.Admin و Server دقیقاً هماهنگ بمانند). صحت با مطابقت دقیق امضاها (signatures) تضمین می‌شود، نه با کامپایل.
6. **زبان:** همه‌چیز باید **فارسی** باشد (از طریق جدول رشته‌ها `AdminStrings`، نه hard-code).
7. **تسک پایانی:** آخرین تسک = حذف همین فایل (`Plan/admin-panel-redesign.md`). به هیچ فایل دیگر و رودمپ دست نزن.

---

## ۱. درک معماری (نتیجه‌گیری نهایی — نیازی به بررسی مجدد نیست)

برنامه‌ی اصلی یک راه‌حل **.NET (C#)** است. پنل ادمین یک اپ **Blazor (Interactive Server + SSR)** به نام **Operator Console** است.

### دو لایه‌ی کلیدی و قرارداد بین آن‌ها
- **`src/Animora.Web.Admin/`** — پروژه‌ی Razor Class Library (UI). فقط به `Animora.Domain` وابسته است. شامل:
  - `Ports/AdminPorts.cs` → **اینترفیس‌های قرارداد** (مثل `IAdminTenantQueries`). UI فقط این‌ها را inject می‌کند.
  - `Models/AdminModels.cs` → **رکوردها/فرم‌های DTO** که بین UI و Server رد و بدل می‌شوند.
  - `Pages/*.razor`, `Components/*.razor`, `Layout/*.razor` → صفحات و اجزا.
  - `Localization/AdminStrings.cs` (جدول fa/en), `AdminCulture.cs` (انتخاب زبان از کوکی `admin_lang`, پیش‌فرض fa, RTL).
  - `wwwroot/admin.css` → تنها استایل (۷۵ خط فعلی، بدون JS، RTL-aware با logical properties).
- **`src/Animora.Server/Admin/`** — **پیاده‌سازی (Adapter)** اینترفیس‌های بالا. هر adapter به لایه‌ی persistence وصل می‌شود:
  - منبع اصلی خواندن داده: **`src/Animora.Persistence.PostgreSql/Operator/OperatorReadQueries.cs`** (۱۲۳ خط). متدها: `TenantsAsync`, `TenantAsync`, `DashboardAsync`, `PlansAsync`, `LicensesAsync`, `StaffAsync`, `ProductsAsync`.
  - ثبت DI: `src/Animora.Server/Admin/AdminServiceCollectionExtensions.cs` (متد `AddAnimoraAdmin`). **هر سرویس/adapter جدید باید اینجا register شود.**
  - اتصال در `src/Animora.Server/Program.cs`: خط ~228 `MapAdminAccountEndpoints()`، خط ~230 `MapRazorComponents<Animora.Web.Admin.App>()...RequireAuthorization(OperatorCookie.Policy)`.
  - account endpoints (login/logout/lang/change-password): `AdminAccountEndpoints.cs`.

### احراز هویت و امنیت (موجود — دست‌نخورده بماند مگر لازم)
- کوکی `OperatorCookie` با نقش `operator`، session مطلق ۳۰ دقیقه، claim های `sub`, `ost`, `op_exp`.
- `AdminSession` → `OperatorId`, `Name`, `Actor` (ActorKind.Operator).
- adapterهایی که از circuit خوانده می‌شوند (`AdminStatusReader`, `AdminLiveLogAdapter`) روی **هر** read کوکی و `op_exp` را دوباره اعتبارسنجی می‌کنند (circuit از HttpContext عمر بیشتری دارد).
- antiforgery: کوکی `__Host-animora-af`, SameSite=Strict. فرم‌های POST از `<AntiforgeryToken />` / `EditForm` استفاده می‌کنند.
- **هر مسیر و داده‌ی جدید باید همین الگوی امنیتی را رعایت کند** (Authorize Policy="AdminOnly" روی صفحات، re-validate روی circuitها).

### الگوی صفحه (باید حفظ شود)
- صفحات از `AdminPageComponent` ارث می‌برند (`Models/AdminPageComponent.cs`): property محافظت‌شده `Error`, متد `Capture<T>(Result<T>)` که خطا را در `Error` می‌گذارد و روی ConcurrencyConflict کد 412 ست می‌کند، و `Cancellation` (از `HttpContext.RequestAborted`).
- خطاها با کامپوننت `<FormError Code="@Error" />` نمایش داده می‌شوند (کلید ترجمه = کد خطا).
- صفحات `@inject` اینترفیس پورت می‌کنند و در `OnInitializedAsync`/`OnParametersSetAsync` داده را `Capture` می‌کنند.
- نوع `Result<T>` از `Animora.Domain.Results`؛ کدهای خطا در `ErrorCodes` (مثل `ValidationFailed`, `NotFound`, `Forbidden`, `ConcurrencyConflict`).

### صفحات موجود (۲۴ razor) و وضعیت فعلی
Dashboard, Tenants, TenantDetail, TenantStaff, TenantProducts, TenantLicenses, TenantNew, Plans, LicensingKeys, Provisioning, WebsiteClients, FileMismatches, Audit, System, Logs, Login, ChangePassword.
- **مشکل اصلی (مطابق گزارش کاربر):** UI بسیار خام/مینیمال است (CSS فقط ۷۵ خط)، UX ضعیف، و **منطق برگرداندن داده ناقص است** — به‌خصوص:
  - جست‌وجوی کلینیک‌ها فقط Code/DisplayName، مرتب‌سازی فقط بر اساس Code (`OperatorReadQueries.TenantsAsync` خطوط ۲۰–۳۲).
  - در جدول Tenants ستون اول `row.Code` به‌عنوان لینک است؛ اگر Code خالی باشد لینک نامرئی/گیج‌کننده می‌شود (`Pages/Tenants.razor`).
  - مرتب‌سازی محصولات بر اساس **بیشترین فروش** وجود ندارد (`ProductsAsync` فقط `OrderBy(Name)`).
  - قابلیت **بکاپ کامل دیتابیس وجود ندارد** (جست‌وجوی `backup`/`pg_dump` در سرور نتیجه‌ای نداد).
  - حالت لاگ زنده موجود است (`LiveLogPanel.razor` + `AdminLiveLogAdapter` + `RingBufferLogSink`) اما نیاز به بهبود UX/دیزاین.

---

## ۲. دامنه‌ی کار (آنچه کاربر خواسته)

بازطراحی کامل پنل ادمین — هم **منطق برگرداندن داده** و هم **ظاهر/UX** — یکپارچه با سرور و کلاینت و همه لایه‌ها:

1. دیزاین سبک، کاربردی، RTL فارسی، UX مناسب، کاملاً هماهنگ با برنامه.
2. حالت لاگ زنده‌ی سرور (بهبود موجود).
3. مدیریت کامل کاربران/کلینیک‌ها: مشاهده کلینیک‌ها و کاربران هر کلینیک؛ تغییر رمز؛ فعال/غیرفعال‌سازی؛ ساخت یوزر آماده؛ اشتراک + حداکثر کاربران؛ ساخت اکانت دمو/معمولی؛ مشاهده محصولات و موجودی؛ مرتب‌سازی بر اساس بیشترین فروش؛ مدیریت جز‌به‌جز.
4. بکاپ کامل دیتابیس.
5. امن و یکپارچه؛ منطق برگرداندن اطلاعات بازنویسی شود تا همه‌چیز درست نمایش داده شود و با سرچ نام کار کند.
6. همه‌چیز فارسی.

---

## ۳. تسک‌لیست دقیق

> وضعیت‌ها: `[ ]` در انتظار · `[~]` در حال انجام · `[x]` انجام‌شده.
> بعد از هر تسک `[x]` → **اجرای اجباری `scripts/git/push.sh`**.

### T0 — راه‌اندازی برنچ و این پلن `[x]`
- مطمئن شو روی برنچ درست هستی. این فایل پلن را commit و push کن (اولین پوش = تست سلامت اسکریپت/کردنشیال).
- **پوش:** `scripts/git/push.sh "docs(plan): add admin panel redesign plan"`

---

### T1 — بازنویسی منطق خواندن داده‌ی کلینیک‌ها (سرور) `[x]`
**فایل:** `src/Animora.Persistence.PostgreSql/Operator/OperatorReadQueries.cs` → متد `TenantsAsync` (خطوط ۲۰–۳۲).
**مشکل فعلی:** سرچ فقط `Code.Contains` و `DisplayName.Contains` (case-sensitive در PG)، مرتب فقط `OrderBy(Code)`.
**کار دقیق:**
- سرچ را case-insensitive کن با `EF.Functions.ILike(..., "%"+term+"%")` روی `DisplayName` و `Code` (الگوی موجود در `ProductsAsync` خط ۱۰۰). این مشکل «باید دقیقاً با اسم سرچ کرد» را حل می‌کند.
- مرتب‌سازی: اول `OrderBy(t=>t.DisplayName)` سپس `ThenBy(t=>t.Code)` سپس `ThenBy(t=>t.Id)` (نام برای اپراتور معنادارتر از کد است).
- `DefaultIfEmpty`/null-safety روی `Code` حفظ شود.
- **هیچ امضای public تغییر نکند** (adapter و UI به همین امضا وابسته‌اند).
- **پوش.**

---

### T2 — افزودن مرتب‌سازی محصولات بر اساس فروش + بهبود خواندن محصولات `[x]`
**فایل‌ها:**
- `src/Animora.Persistence.PostgreSql/Operator/OperatorReadQueries.cs` → `ProductsAsync` (خطوط ۹۳–۱۲۲) و رکورد `OperatorProductRow`/`OperatorProductFilter` (خطوط ۱۲–۱۵).
- `src/Animora.Web.Admin/Models/AdminModels.cs` → `ProductSummary`, `ProductListQuery`.
- `src/Animora.Server/Admin/AdminProductAdapter.cs`.
- `src/Animora.Web.Admin/Pages/TenantProducts.razor`.

**کار دقیق:**
- به `OperatorProductFilter` و `ProductListQuery` فیلد `string? Sort` اضافه کن (مقادیر مجاز: `name` پیش‌فرض، `sales` بیشترین فروش، `stock-asc` کمترین موجودی، `price-desc`).
- در `ProductsAsync`: مقدار فروش از ledger محاسبه می‌شود. فروش = مجموع حرکت‌های **خروج/فروش** انبار. `tenant.StockMovementSet` موجود است (خط ۱۰۶ آن را برای OnHand جمع می‌زند). برای «فروش» باید حرکت‌های منفی/از نوع فروش را جمع زد. **قبل از کد زدن، enum/فیلد نوع حرکت را از مدل `StockMovement` بخوان** (یک grep هدفمند: `grep -rn "class StockMovement\|enum.*Movement\|MovementKind\|MovementType" src/Animora.Domain src/Animora.Persistence.PostgreSql`). اگر نوع فروش قابل تشخیص نبود، از «مجموع قدرمطلق خروجی‌ها = مقدارهای منفی» به‌عنوان تقریب فروش استفاده کن و در کامنت ذکر کن.
- محاسبه‌ی `SalesQty` (decimal) را به projection اضافه کن و در `OperatorProductRow` نگه‌دار؛ `ProductSummary` یک فیلد `decimal SalesQty` بگیرد.
- `switch` مرتب‌سازی: `sales => OrderByDescending(SalesQty)`, `stock-asc => OrderBy(OnHand)`, `price-desc => OrderByDescending(Price)`, پیش‌فرض `OrderBy(Name)`.
- adapter: `Sort` را validate و pass کن (طول کوتاه، مقدار از whitelist).
- UI: در `TenantProducts.razor` یک `<select name="sort">` با گزینه‌های فارسی اضافه کن و ستون «فروش» را در جدول نشان بده.
- **هماهنگی تایپ:** هر سه لایه (Operator record ↔ ProductSummary ↔ UI) باید فیلد جدید را داشته باشند.
- **پوش.**

---

### T3 — قابلیت فعال/غیرفعال‌سازی و مدیریت کامل کاربر کلینیک (staff) `[x]`
**هدف:** کاربر خواست بتواند کاربران را فعال/غیرفعال کند و «یک یوزر کاملاً آماده» بسازد. فعلاً فقط Create + ResetPassword وجود دارد (`IAdminStaffCommands`).
**ابتدا بررسی هدفمند:** سرویس زیربنایی `ClinicStaffAdministration` را بخوان تا ببینی چه متدهایی برای enable/disable دارد:
`grep -rn "class ClinicStaffAdministration" src && sed -n ... ` سپس فایل را باز کن.
**کار دقیق:**
- اگر `ClinicStaffAdministration` متد enable/disable دارد → به `IAdminStaffCommands` (در `Ports/AdminPorts.cs`) متد `SetStaffActiveAsync(string tenantId, SetStaffActiveForm form, ct)` اضافه کن، فرم `SetStaffActiveForm { UserId, bool Active, bool Confirmed }` در `AdminModels.cs`، پیاده‌سازی در `AdminStaffCommandsAdapter.cs`.
- اگر متد زیربنایی وجود ندارد → **این بخش را به T-بعدی موکول نکن**؛ در عوض در همین تسک فقط ResetPassword و Create را از نظر UX بهبود بده و در پلن یادداشت کن که enable/disable نیاز به متد دامنه دارد (و اگر زیرساخت staff endpoint عمومی دارد، از آن استفاده کن). تصمیم را در کامنت کد مستند کن.
- UI `TenantStaff.razor`: دکمه‌ی فعال/غیرفعال کنار هر ردیف (EditForm با ConfirmCheckbox)، نمایش بهتر وضعیت.
- ترجمه‌های فارسی لازم را به `AdminStrings` اضافه کن.
- **پوش.**

---

### T4 — صفحه‌ی ساخت کلینیک + اکانت دمو/معمولی (TenantNew) `[x]`
**فایل‌ها:** `src/Animora.Web.Admin/Pages/TenantNew.razor`, `Models/AdminModels.cs` (`CreateTenantForm`), `AdminSubscriptionAdapter.CreateTenantAsync`.
**موجود:** `CreateTenantForm` فیلدهای Code, Name, OwnerUserName, OwnerName, PlanCode, Seats, StartTrial, IdempotencyKey دارد؛ adapter به `ProvisioningEngine.ProvisionAsync` وصل است و رمز موقت owner را یک‌بار برمی‌گرداند.
**کار دقیق:**
- فرم را کامل و کاربرپسند کن: انتخاب پلن از لیست پلن‌های واقعی (inject `IAdminPlanCommands.ListAsync`)، سوئیچ «اکانت دمو» (StartTrial=true + شاید seats محدود) در مقابل «معمولی».
- نمایش واضح رمز موقت owner بعد از ساخت (الگوی `.secret` موجود).
- validation سمت UI (کد/نام الزامی) و نمایش خطا با FormError.
- ترجمه‌های فارسی.
- **پوش.**

---

### T5 — قابلیت بکاپ کامل دیتابیس (سرور + UI) `[x]`
**وضعیت:** هیچ قابلیتی وجود ندارد — باید از صفر ساخته شود.
**ابتدا بررسی هدفمند:** رشته‌ی اتصال و provider را بفهم:
`grep -rn "ConnectionString\|UseNpgsql\|DatabaseOptions" src/Animora.Server/Program.cs src/Animora.Persistence.PostgreSql/Options` (فایل `DatabaseOptions` قبلاً در `AdminStatusSnapshotSource` استفاده شده).
**کار دقیق (طراحی امن):**
- یک پورت جدید `IAdminBackup` در `Ports/AdminPorts.cs`:
  ```csharp
  public interface IAdminBackup {
      Task<Result<BackupReceipt>> CreateAsync(BackupRequestForm form, CancellationToken ct=default);
  }
  ```
  مدل‌ها در `AdminModels.cs`: `BackupRequestForm { bool Confirmed }`, `BackupReceipt(string FileName, long SizeBytes, DateTimeOffset CreatedUtc)` یا در صورت دانلود مستقیم، stream.
- پیاده‌سازی `AdminBackupAdapter` در `src/Animora.Server/Admin/`:
  - روش امن و قابل‌اتکا: اجرای `pg_dump` از طریق `System.Diagnostics.Process` با رشته‌ی اتصال خوانده‌شده از `DatabaseOptions`/configuration، خروجی به یک فایل موقت با فرمت custom (`-Fc`) یا plain SQL فشرده.
  - **امنیت:** فقط operator مجاز؛ Confirmed اجباری؛ هیچ ورودی کاربر مستقیماً وارد command line نشود (پارامترها از config، نه از فرم)؛ از نشت رمز اتصال در لاگ جلوگیری کن.
  - ثبت در audit (اگر `OperatorAudit` در دسترس است — بررسی هدفمند `OperatorAudit.cs`).
- دانلود: یک endpoint در `AdminAccountEndpoints` یا یک گروه جدید `/admin/backup/download` با `RequireAuthorization(OperatorCookie.Policy)` + antiforgery، که فایل بکاپ را به‌صورت `Results.File(...)` با content-disposition می‌دهد.
- register در `AddAnimoraAdmin`.
- صفحه‌ی جدید `Pages/Backup.razor` (مسیر `/admin/backup`) + لینک در `NavMenu.razor` + ترجمه‌ها. دکمه با ConfirmCheckbox، نمایش نتیجه و لینک دانلود.
- **اگر `pg_dump` در محیط اجرا در دسترس نیست:** fallback ذکر شود (پیام خطای واضح فارسی به اپراتور). این را در کامنت مستند کن.
- **پوش** (این تسک سنگین است؛ اگر وسط کار کد زیادی شد، میان‌پوش هم بزن).

---

### T6 — بازطراحی کامل CSS/UX (shell، جداول، کارت‌ها، فرم‌ها، لاگ) `[x]`
**فایل:** `src/Animora.Web.Admin/wwwroot/admin.css` (بازنویسی/گسترش از ۷۵ خط).
**اصول:** سبک، بدون وابستگی خارجی، بدون JS اضافی، RTL-aware با **logical properties** (همان سبک فعلی)، فونت Vazirmatn موجود، پالت موجود (`--brand`, `--ok`, `--warn`, `--danger`).
**کار دقیق:**
- سایدبار و topbar مدرن‌تر (آیکون‌های متنی/Unicode سبک مجاز، بدون asset جدید)، حالت فعال لینک.
- جداول: zebra striping، hover بهتر، ریسپانسیو (اسکرول افقی روی موبایل).
- کارت‌های داشبورد: کنتراست و سلسله‌مراتب بصری بهتر.
- فرم‌ها: فاصله‌گذاری، گروه‌بندی fieldset، دکمه‌های اولیه/ثانویه/خطرناک (`.danger`).
- badgeها برای وضعیت‌های فارسی (Active/Suspended/...).
- پنل لاگ زنده: خوانایی بهتر، رنگ‌بندی سطح لاگ، چسبندگی auto-scroll (قبلاً column-reverse دارد).
- دسترس‌پذیری: focus-visible حفظ شود، کنتراست AA.
- **هیچ ساختار DOM صفحه‌ای که کلاس جدید می‌خواهد را بدون افزودن همان کلاس در CSS رها نکن.**
- **پوش.**

---

### T7 — بهبود منطق نمایش در صفحات (هماهنگی UI با داده) `[x]`
**هدف:** رفع مشکلات «برگردوندن کاربران و نام کلینیک‌ها» در سطح UI.
**فایل‌ها:** `Pages/Tenants.razor`, `TenantDetail.razor`, `TenantStaff.razor`, `TenantProducts.razor`, `TenantLicenses.razor`, `Dashboard.razor`, `Audit.razor`.
**کار دقیق:**
- `Tenants.razor`: ستون اول را به **نام کلینیک** (لینک) تبدیل کن و کد را ستون جداگانه (نه لینک روی کد خالی). نمایش «موردی یافت نشد» با `common.none` وقتی `Rows.Items` خالی است. وضعیت‌ها با badge فارسی.
- `TenantDetail.razor`: نمایش کامل‌تر اطلاعات کلینیک (seats used/limit، devices، licenses، وضعیت اشتراک با تاریخ شمسی‌خوان یا فرمت واضح، رویدادها). عملیات اشتراک (extend/reduce/suspend/...) با لیبل‌های فارسی به‌جای نمایش نام خام عملیات.
- `TenantStaff.razor`: نمایش وضعیت فعال/غیرفعال واضح، تعداد دستگاه، نقش فارسی (از قبل `role.*` دارد).
- status/عملیات‌هایی که الان نام enum خام انگلیسی نشان می‌دهند → از کلید ترجمه استفاده کن.
- **هیچ کلید ترجمه‌ی گم‌شده باقی نماند** (گرنه کلید خام نمایش داده می‌شود). همه‌ی کلیدهای جدید را به `AdminStrings` اضافه کن.
- **پوش.**

---

### T8 — تکمیل جدول ترجمه فارسی + یکدست‌سازی زبان `[x]`
**فایل:** `src/Animora.Web.Admin/Localization/AdminStrings.cs`.
**کار دقیق:**
- کل جدول را مرور کن؛ هر کلیدی که در صفحات/کامپوننت‌های تغییریافته استفاده شده باید مدخل fa داشته باشد.
- متن‌های hard-code انگلیسی باقیمانده در razorها (مثل `System.razor`: "Uptime (seconds)", "Worker heartbeat", "Insight: enabled/disabled", "Single server instance only..."؛ و لیبل‌های خام عملیات در `TenantDetail`) را به کلید ترجمه منتقل کن.
- اطمینان: پیش‌فرض زبان fa است (`AdminCulture`), سوئیچ زبان کار می‌کند.
- **پوش.**

---

### T9 — ثبت نهایی DI و بازبینی هماهنگی لایه‌ها `[x]`
**فایل:** `src/Animora.Server/Admin/AdminServiceCollectionExtensions.cs` و در صورت نیاز `Program.cs` / `NavMenu.razor`.
**کار دقیق:**
- مطمئن شو هر پورت/adapter جدید (`IAdminBackup` و هر چیز اضافه‌شده) در `AddAnimoraAdmin` register شده.
- مطمئن شو هر صفحه‌ی جدید در `NavMenu.razor` لینک دارد و route صحیح است و تحت `OperatorCookie.Policy` محافظت می‌شود (از طریق `MapRazorComponents...RequireAuthorization` موجود).
- بازبینی نهایی: امضای **هر** متد پورت دقیقاً با adapter یکی است؛ هر DTO جدید در هر دو لایه یکسان است؛ هیچ `using` گم‌شده نیست.
- یک grep نهایی برای یافتن ارجاع‌های شکسته: نام هر symbol جدید را grep کن تا مطمئن شوی هم در تعریف و هم در استفاده هماهنگ است.
- **پوش.**

---

### T10 — بازبینی صحت کامپایل به‌صورت دستی (بدون build) `[ ]`
**چون `dotnet` نیست:** صحت را دستی چک کن:
- هر فایل تغییریافته را دوباره بخوان و مطابقت تایپ‌ها/امضاها را تأیید کن.
- چک‌لیست: namespaceها درست، recordها immutable و سازگار، nullability رعایت‌شده، `EF.Functions.ILike` فقط روی PG استفاده شده (provider موجود PostgreSQL است)، هیچ API ناموجود صدا زده نشده.
- اگر هر ناسازگاری یافتی → همان‌جا فیکس و **میان‌پوش**.
- **پوش.**

---

### T11 — تسک پایانی: حذف این فایل پلن `[ ]`
- `rm /home/user/webapp/Plan/admin-panel-redesign.md`
- به هیچ فایل دیگر (شامل `.gitkeep` و رودمپ‌ها) دست نزن.
- **پوش:** `scripts/git/push.sh "chore(plan): remove completed admin redesign plan"`

---

## ۴. چک‌لیست هماهنگی سریع (قبل از هر پوش)
- [ ] امضای پورت == امضای adapter؟
- [ ] DTO جدید در هر دو لایه یکسان؟
- [ ] سرویس/adapter جدید در `AddAnimoraAdmin` register شد؟
- [ ] صفحه‌ی جدید در `NavMenu` + route + Authorize؟
- [ ] همه‌ی رشته‌های UI از `AdminStrings` (فارسی) می‌آیند؟
- [ ] کلاس‌های CSS جدیدِ استفاده‌شده در `admin.css` تعریف شده‌اند؟
- [ ] الگوی امنیتی (Confirmed, antiforgery, operator-only) رعایت شد؟

## ۵. نکات ریسک
- `EF.Functions.ILike` فقط Npgsql؛ اگر جایی Sqlite هم مسیر دارد بررسی کن (این مسیرها زیر `Operator/` فقط PG هستند — مطمئن از PG بودن).
- تغییر رکوردهای `AdminModels` یعنی سازنده‌ها در adapter و razor باید همگی به‌روز شوند (grep نام رکورد).
- بکاپ: اجرای process باید خطاها را graceful مدیریت کند و رمز اتصال را لو ندهد.
