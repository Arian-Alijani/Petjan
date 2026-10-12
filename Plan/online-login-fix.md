# Plan — Animora: استانداردسازی ورود آنلاین و یکپارچه‌سازی کلاینت ویندوز با سرور

> این فایل خودبسنده است. همهٔ اطلاعات، مسیرها، تحلیل‌ها و نتیجه‌گیری‌هایی که برای اجرا لازم است
> این‌جا جمع شده تا یک مدل در یک سشن کاملاً جدید **بدون بررسی دوبارهٔ ریپو** بتواند مستقیماً پیاده‌سازی را شروع کند.
> فقط فایل‌هایی را باز کن که در هر تسک به صراحت نام برده شده‌اند.

---

## 0) قانون پوش (قفل‌شده — حتمی)

- بعد از **تکمیل هر تسک** این اسکریپت را اجرا کن (از ریشهٔ ریپو):
  ```bash
  scripts/git/push.sh -b genspark_ai_developer "type(scope): پیام دقیق"
  ```
  - اگر خروجی کد ۳ داد → ابزار `setup_github_environment` را صدا بزن، بعد دوباره همان دستور.
  - اگر خروجی کد ۲ داد (کانفلیکت rebase) → حل کن (اولویت با کد ریموت)، بعد دوباره.
- اگر وسط یک تسک فیکس سنگین/کد زیاد نوشتی، همان‌جا هم یک پوش جدا با پیام قابل‌فهم بزن.
- **بیلد/تست کامل و طولانی ممنوع.** فقط بیلد سریع Desktop برای صحت کامپایل:
  `scripts/sandbox/build.sh` (انتظار: 0 warning / 0 error). بیلد کل solution فقط اگر لایهٔ سرور/اپلیکیشن را هم دست زدی.
- Branch کاری: `genspark_ai_developer`. شاخهٔ هدف PR: `main`.

---

## 1) معماری در یک نگاه (نتیجهٔ بررسی — دوباره بررسی نکن)

Animora یک محصول SaaS **چندمستاجری (multi-tenant)** است: یک سرور مرکزی (ASP.NET Core 10، `src/Animora.Server`)
+ کلاینت دسکتاپ Avalonia/MVVM (`src/Animora.Desktop`) که آفلاین-فرست است و روی SQLite محلی کار می‌کند.

مسیر داده کلاینت: `View → ViewModel → interface در Animora.Contracts → پیاده‌سازی واقعی در Animora.Client.Services → Animora.Application (dispatch) → SQLite محلی`.

**دو «نشست» جدا وجود دارد و این هستهٔ مشکل است:**
1. **نشست محلی (local session):** با `SignInCommand` از طریق `IAuthenticationService` ساخته می‌شود. فقط محلی است و
   `CommandContext.TenantId` را از `ILocalInstallationIdentity.TenantId` می‌گیرد که مقدارش ثابت `T-LOCAL-DEFAULT` است
   (در لاگ کاربر هم همین دیده می‌شود: `"tenantId":"T-LOCAL-DEFAULT"`).
2. **نشست سرور (server session):** با `IOnlineAuthClient.LoginAsync(tenantCode, user, pass)` → `POST /auth/login` ساخته می‌شود.
   توکن Bearer را در `ProtectedSessionStore` ذخیره می‌کند، دستگاه را register و مجوز را seal می‌کند.
   این نشست است که درخواست‌های `/api/commands`, `/sync/*`, `/licensing/status` را مجاز می‌کند
   (توکن از طریق `BearerAuthHandler` روی همان HttpClient تزریق می‌شود).

---

## 2) ریشهٔ دقیق مشکلات (تحلیل لاگ کاربر — نهایی)

لاگ کاربر این خط‌ها را دارد و معنی دقیق‌شان این است:

- `local sign-in SUCCEEDED for 'petjun'` → نشست **محلی** ساخته شد.
- `server login SKIPPED — ANIMORA_TENANT_CODE is not set` → نشست **سرور** اصلاً ساخته نشد.
  چون `ServerEndpointOptions.TenantCode` خالی بود (پیش‌فرض `DefaultTenantCode = ""` در `App.axaml.cs`).
- در نتیجه هیچ توکن Bearer ذخیره نشد. پس هر فراخوانی سرور با **`401 SESSION_EXPIRED`** برمی‌گردد
  (`/sync/manifest`, `/licensing/status`) و ساخت رکورد با **`ENTITLEMENT_REQUIRED`** رد می‌شود
  (چون gate آنلاین مجوز seal‌شده ندارد).
- `The system cannot find the file specified.` در ابتدای launcher → یک خطای نرمِ محیط لانچر است (مربوط به یک فایل/ابزار جانبی که launcher صدا می‌زند)، نه خطای خود برنامه؛ اولویت پایین، در تسک ۳ بررسی می‌شود.

### جمع‌بندی علت اصلی
سیستم ورود **محلی-اول** است: ورود محلی مستقل از سرور موفق می‌شود و ورود سرور «best-effort» و قابل‌skip است.
کاربر هیچ‌جا **کد کلینیک/مستاجر (TenantCode)** را وارد نمی‌کند، و بیلد پیش‌فرض هم آن را ست نمی‌کند ⇒ نشست سرور هرگز برقرار نمی‌شود ⇒ هیچ قابلیت سروری کار نمی‌کند.

**خواستهٔ کاربر:** ورود باید **استاندارد و فقط آنلاین** باشد — کاربر نام‌کاربری/رمز (و کد کلینیک) می‌زند،
ورود و بررسی اشتراک فقط آنلاین انجام می‌شود، و «ورود آفلاین» به‌عنوان مسیر ورود اولیه معنا ندارد.

---

## 3) شواهد کد (فایل‌ها و خطوط کلیدی — برای اجرا لازم است)

- **`src/Animora.Desktop/Pages/Login/LoginViewModel.cs`**
  - `SignInAsync` (≈خط ۱۸۷): اول `_authentication.SignInAsync` (محلی) → در صورت موفقیت `LoginSucceeded` را **بدون توجه به نتیجهٔ سرور** فایر می‌کند.
  - `EstablishServerSessionAsync` (≈خط ۲۳۴): اگر `_onlineAuth is null` یا `TenantCode` خالی باشد **skip** می‌کند و فقط لاگ می‌زند؛ خطای سرور **هرگز** جلوی ورود را نمی‌گیرد. ← این «best-effort» بودن باید عوض شود.
  - `MapError` (≈خط ۲۸۴): نگاشت کد خطا → متن فارسی. از این استفادهٔ مجدد کن؛ کدهای لازم (CREDENTIALS_INVALID, SESSION_EXPIRED, ENTITLEMENT_REQUIRED, LICENSE_*, NETWORK_UNAVAILABLE, DEVICE_REVOKED, TENANT_SUSPENDED, …) همه موجودند. فقط یک کد تازه لازم است: برای «کد کلینیک خالی» می‌توان از `VALIDATION_FAILED` استفاده کرد (نیازی به کد جدید نیست).
  - کانستراکتور: `IOnlineAuthClient? onlineAuth`, `ServerEndpointOptions? serverOptions`, `IServerConnectivityService? connectivity` همه اختیاری‌اند (nullable). در بیلد واقعی که سرور configure شده، DI این‌ها را پر می‌کند.
  - یک شاخهٔ `_localProfile?.ClinicBakPreviewPath` هم هست (مسیر پیش‌نمایش بکاپ) — دست نزن مگر لازم؛ همان الگوی جدید را رعایت کند.

- **`src/Animora.Desktop/Pages/Login/LoginWindow.axaml`**
  - فرم فقط دو فیلد دارد: «نام کاربری» (خط ۱۴۶–۱۴۹) و «رمز عبور» (خط ۱۵۲–۱۵۶). **فیلد «کد کلینیک» وجود ندارد** و باید اضافه شود.
  - بنر خطا (`HasError`/`ErrorMessage`)، دکمهٔ «ورود» (`SignInCommand`)، و اندیکاتور اتصال سرور (`IsServerOnline`/`ConnectivityText`) موجودند.
  - متن پایین فرم (≈خط ۲۰۴–۲۱۴) دربارهٔ «حساب راه‌اندازی اولیه» است؛ در مدل آنلاین باید به متن مناسب تغییر کند (ورود با حساب کلینیک).

- **`src/Animora.Desktop/App.axaml.cs`**
  - `ConfigureServerEndpoint` (≈خط ۴۹۰): `BaseAddress` از `ANIMORA_SERVER_URL` یا پیش‌فرض `http://188.212.99.234:5000`. `TenantCode` از `ANIMORA_TENANT_CODE` یا `DefaultTenantCode = ""`.
  - `DefaultServerBaseAddress = "http://188.212.99.234:5000"` (خط ۴۶۹) — درست است، دست نزن.
  - `AddAnimoraOnlineIdentity()` (خط ۴۲۶): فقط وقتی `ServerEndpointOptions.IsConfigured==true` اجزای آنلاین (IOnlineAuthClient, ProtectedSessionStore, OnlineEntitlementGate, IClinicAccountStatusSource) را register می‌کند. چون BaseAddress پیش‌فرض ست است، `IsConfigured==true` است ⇒ `IOnlineAuthClient` در DI هست. مشکل فقط نبودِ `TenantCode` است، نه نبودِ کلاینت.
  - LoginVM در خط ۴۴۵ به‌صورت Transient register شده.

- **`src/Animora.Client.Services/Identity/OnlineAuthClient.cs`** (پیاده‌سازی `/auth/login`)
  - امضا: `LoginAsync(string tenantCode, string userName, string password, ct)` → `Result<OnlineLoginOutcome>`.
  - اگر سرور **پاسخ قطعی رد** بدهد (مثلاً 401) ⇒ `CREDENTIALS_INVALID` و غیره را برمی‌گرداند (نهایی).
  - فقط وقتی سرور **در دسترس نیست** (no response / 408/429/5xx) ⇒ `OfflineSessionAuthorizer` را امتحان می‌کند. این مسیرِ fallbackِ آفلاین برای ورودِ **تکراری** است (کاربری که قبلاً آنلاین وارد شده و verifier/mirror دارد)؛ برای اولین ورود `OFFLINE_SESSION_UNAVAILABLE` می‌دهد.
  - پس از موفقیت: session نوشته، device register، capability mirror، licence seal، verifier نوشته می‌شود.
  - **نتیجهٔ مهم:** منطق آنلاین/آفلاین درست پیاده شده؛ فقط باید در LoginVM **اجباری و مسدودکننده** شود و قبل از نشست محلی/باز شدن shell اجرا شود.

- **`src/Animora.Sync.Custom/ServerEndpointOptions.cs`**: `TenantCode` اینجاست؛ اما بهتر است کد کلینیک **در زمان ورود از فرم** گرفته شود نه از env، چون هر کلینیک کد خودش را دارد (SaaS). env می‌تواند fallback بماند.

- **سمت سرور `src/Animora.Server/Auth/LoginService.cs` (برای فهم قرارداد — تغییرش نده مگر لازم):**
  - ترتیب: resolve tenant از `TenantCode` → bind → user lookup tenant-scoped. tenant/user/pass اشتباه همگی `CREDENTIALS_INVALID` (ضد enumeration).
  - دستگاهِ **Manager** هنگام اولین ورود **auto-approve** می‌شود؛ دستگاه staff در `Pending` می‌ماند تا manager تأیید کند.
  - خروجی `LoginResponse`: AccessToken(900s), RefreshToken(30d), UserId, TenantId, DeviceId, Role, MustChangePassword.
  - endpointها: `POST /auth/login|refresh|logout|device/register` در `AuthEndpoints.cs` تحت rate-limit `auth`.

- **سمت سرور entitlement:** `src/Animora.Application/Behaviors/EntitlementBehavior.cs` درخواست‌های pre-auth را bypass می‌کند؛ بقیه باید مجوز seal‌شده داشته باشند ⇒ بدون نشست سرور، `ENTITLEMENT_REQUIRED`.

- **اشتراک/صفحهٔ پروفایل:** `ProfilePageViewModel.RefreshAccountStatus` از `IClinicAccountStatusSource` (یعنی `ServerClinicAccountStatusSource` → `GET /licensing/status`) می‌خواند. وقتی نشست سرور برقرار شد، این هم کار می‌کند.

---

## 4) تصمیم طراحی نهایی (این را پیاده کن — روی این فکر نکن)

**ورود استاندارد فقط-آنلاین با سه ورودی: کد کلینیک + نام کاربری + رمز.**

جریان جدید `SignInAsync` در LoginViewModel:
1. اعتبارسنجی ورودی‌ها (هر سه لازم‌اند؛ کد کلینیک خالی ⇒ خطای فارسی مناسب، بدون تماس سرور).
2. **اول نشست سرور:** `EstablishServerSessionAsync` را به `Result`-محور و **مسدودکننده** تبدیل کن:
   - اگر `_onlineAuth` یا `_serverOptions` null باشد (بیلد آفلاین‌ساخته) ⇒ خطای «این نسخه بدون سرور است» و توقف. (در بیلد واقعی هیچ‌وقت null نیست.)
   - `tenantCode` را از **فیلد فرم** بگیر (نه از env؛ env فقط پیش‌فرض/fallback). 
   - `result = await _onlineAuth.LoginAsync(tenantCode, user, pass, ct)`.
   - اگر `result.IsSuccess == false` ⇒ `ErrorMessage = MapError(result.Error.Code)` و **توقف** (ورود انجام نمی‌شود، shell باز نمی‌شود).
   - `MustChangePassword` یا `IsOffline` را لاگ کن (رفتار فعلی نگه داشته شود؛ IsOffline یعنی سرور لحظه‌ای در دسترس نبود ولی mirror معتبر بود — این حالت فقط برای ورود تکراری ممکن است و مجاز است).
3. **بعد نشست محلی:** فقط در صورت موفقیت سرور، `_authentication.SignInAsync(user, pass, rememberDevice, ct)` را صدا بزن تا نشست محلی (برای `CommandContext.UserId`) ساخته شود.
   - نکته: نام‌کاربری/رمزِ محلی باید با حساب کلینیک یکی باشد. در اولین ورودِ یک نصبِ تازه ممکن است هنوز کاربر محلی ساخته نشده باشد؛ بررسی کن که آیا `SignInAsync` محلی روی نصب تازه شکست می‌خورد یا first-run لازم است. (پایین، بخش «ریسک‌ها».)
4. اگر هر دو موفق ⇒ `LoginSucceeded` را فایر کن (باز شدن shell).

**حذف مفهوم «ورود آفلاینِ اولیه»:** دیگر ورود محلیِ مستقل از سرور نباید shell را باز کند. تنها استثنای مجاز: ورود تکراری که سرور لحظه‌ای down است و `OnlineAuthClient` خودش از mirror معتبر (ورود قبلیِ آنلاین) استفاده می‌کند — این داخل `LoginAsync` هندل می‌شود و `IsSuccess` برمی‌گرداند، پس نیازی به کد اضافه در VM نیست.

**first-run (ساخت کلینیک محلی) در مدل SaaS:** کلینیک روی سرور provision می‌شود (نه در دسکتاپ). بنابراین مسیر first-runِ فعلی (ساخت owner محلی) برای مدل آنلاین نادرست است و نباید shell را بدون نشست سرور باز کند. ← در تسک ۲ تصمیم دقیق: یا first-run را فقط برای حالت test/offline نگه دار، یا بعد از ساخت owner محلی هم **نشست سرور را اجباری کن**. تصمیم اجرایی: first-run فقط کاربر محلی را می‌سازد و سپس **همان مسیر ورود آنلاین اجباری** باید طی شود؛ اگر سرور configure است و نشست سرور برقرار نشود، shell باز نشود.

---

## 5) تسک‌ها (وضعیت: ⬜ انجام‌نشده / ✅ انجام‌شده)

### ✅ TASK-A — فیلد «کد کلینیک» در فرم ورود + پراپرتی در ViewModel
**هدف:** کاربر بتواند کد کلینیک را وارد کند (ورودی لازمِ `/auth/login`).
**فایل‌ها:**
- `src/Animora.Desktop/Pages/Login/LoginViewModel.cs`:
  - یک `[ObservableProperty] private string _clinicCode = string.Empty;` با `[NotifyCanExecuteChangedFor(nameof(SignInCommand))]` اضافه کن.
  - مقدار اولیه: اگر `_serverOptions?.TenantCode` پر بود، `ClinicCode` را با آن مقداردهی کن (fallback از env/بیلد).
  - `CanSignIn` را طوری به‌روز کن که `ClinicCode` خالی نباشد **فقط وقتی سرور configure است** (یعنی `_onlineAuth != null`). در بیلد آفلاین فیلد لازم نیست.
- `src/Animora.Desktop/Pages/Login/LoginWindow.axaml`:
  - قبل از فیلد «نام کاربری»، یک بلوک «کد کلینیک» با `TextBox Classes="field"` و `Text="{Binding ClinicCode, Mode=TwoWay}"` اضافه کن. placeholder مناسب فارسی. فقط وقتی سرور configure است visible باشد (یک پراپرتی bool مثل `RequiresClinicCode` در VM که `=> _onlineAuth != null` بسازی و `IsVisible` را به آن ببندی).
  - متن پایین فرم (≈خط ۲۰۴–۲۱۴) را به متن مدل آنلاین تغییر بده (مثلاً: «با کد کلینیک و حساب کاربری خود وارد شوید»).
**پذیرش:** بیلد Desktop سریع 0/0. فیلد در UI دیده شود.
**پوش:** `scripts/git/push.sh -b genspark_ai_developer "feat(login): add clinic/tenant code field to online sign-in form"`

### ✅ TASK-B — ورود فقط-آنلاین و اجباری در `SignInAsync`
**هدف:** نشست سرور اجباری و مسدودکننده شود؛ shell فقط با نشست سرور موفق باز شود.
**فایل:** `src/Animora.Desktop/Pages/Login/LoginViewModel.cs`
- `EstablishServerSessionAsync` را بازنویسی کن تا **`Task<bool>` (یا `Result`)** برگرداند و `ErrorMessage` را در شکست ست کند:
  - `_onlineAuth == null` ⇒ (بیلد آفلاین) ← رفتار: در این حالت مدل آنلاین اعمال نمی‌شود؛ `return true` تا رفتار آفلاینِ خالص حفظ شود (این فقط بیلد بدون سرور است). **اما** چون بیلد واقعی سرور را configure دارد، این شاخه در عمل اجرا نمی‌شود.
  - `tenantCode` را از `ClinicCode.Trim()` بگیر؛ اگر خالی ⇒ `ErrorMessage = MapError("VALIDATION_FAILED")` (یا متن مخصوص «کد کلینیک را وارد کنید») و `return false`.
  - `LoginAsync` را صدا بزن؛ در شکست `ErrorMessage = MapError(result.Error.Code)` و `return false`؛ در موفقیت لاگ فعلی را نگه‌دار و `return true`.
- `SignInAsync` را بازترتیب بده: **اول** `EstablishServerSessionAsync`؛ اگر false ⇒ `Password=string.Empty; return;` (shell باز نشود). سپس `_authentication.SignInAsync` محلی؛ در شکست پیام بده. فقط اگر هر دو ok ⇒ `LoginSucceeded`.
- شاخهٔ `ClinicBakPreviewPath` را هم با همین ترتیب هماهنگ کن (اول سرور اگر configure، بعد محلی) — یا اگر این مسیر صرفاً برای پیش‌نمایش آفلاین است، دست‌نخورده با توضیح باقی بماند. تصمیم: چون این مسیر isolated/offline preview است، آن را تغییر نده ولی مطمئن شو crash نمی‌کند.
**پذیرش:** بیلد Desktop سریع 0/0. منطقاً: کد کلینیک درست + حساب معتبر ⇒ ورود؛ کد/حساب غلط ⇒ پیام خطا و عدم ورود.
**پوش:** `fix(login): require server session (online-only) before opening shell`

### ⬜ TASK-C — همسوسازی first-run با مدل آنلاین
**هدف:** first-run نباید shell را بدون نشست سرور باز کند (وقتی سرور configure است).
**فایل‌ها:** `LoginViewModel.cs` (`OnFirstRunSucceeded`)، و در صورت نیاز `FirstRunViewModel.cs`.
- بعد از موفقیت first-run محلی، اگر سرور configure است (`_onlineAuth != null`)، باید کاربر از همان مسیر ورود آنلاین اجباری رد شود. ساده‌ترین راهِ درست: پس از first-run، فرم ورود را با username پرشده نشان بده و از کاربر بخواه کد کلینیک + رمز را برای ورود آنلاین بزند (به‌جای باز کردن مستقیم shell). یعنی `OnFirstRunSucceeded` دیگر مستقیماً `LoginSucceeded` نزند مگر سرور configure نباشد.
- نتیجهٔ مورد انتظار: در نصب تازهٔ متصل به سرور، کاربر با حسابِ از-قبل-provision‌شدهٔ کلینیک وارد می‌شود؛ first-runِ محلی فقط برای حالت بدون‌سرور/تست معنا دارد.
- **قبل از پیاده‌سازی**، این را تأیید کن: آیا در نصب تازهٔ متصل‌به‌سرور، جریان درست «ورود مستقیم» است (نه first-run)؟ با منطق `IAuthenticationService.IsFirstRunAsync` (که فقط وضعیت محلی را می‌بیند) بررسی کن که first-run محلی مزاحم ورود آنلاین نشود. اگر مزاحم است، وقتی سرور configure است first-run را نشان نده و مستقیم فرم ورود آنلاین را بگذار.
**پذیرش:** بیلد 0/0؛ منطق ورود تازه با سرور = ورود آنلاین مستقیم.
**پوش:** `fix(login): align first-run with online-only model (no local-only shell on server builds)`

### ⬜ TASK-D — یکپارچگی کلاینت↔سرور و صحت کامپایل انتها به انتها
**هدف:** مطمئن شو تغییرات با قراردادهای سرور (`/auth/login`, `/licensing/status`, `/api/commands`, `/sync/*`) هماهنگ است و چیزی نشکسته.
**کارها (هدفمند، بدون بررسی بیهوده):**
- تأیید کن `IOnlineAuthClient`/`ServerEndpointOptions`/`IClinicAccountStatusSource` در بیلد پیش‌فرض (BaseAddress ست) register می‌شوند (از `OnlineIdentityServiceCollectionExtensions` و `App.axaml.cs`؛ قبلاً تأیید شده). اگر LoginVM را وادار کردی که وقتی `_onlineAuth==null` است در بیلد configure هرگز رخ ندهد، نیازی به تغییر DI نیست.
- `grep` کوتاه برای هر جای دیگری که به «ورود موفق ⇒ باز شدن shell بدون سرور» وابسته است (مثلاً تست‌هایی که به رفتار قبلی LoginVM تکیه دارند) و در صورت لازم هماهنگ کن. اگر تستی به‌خاطر امضای تغییریافتهٔ private method نمی‌شکند، بگذار بماند.
- بیلد سریع Desktop: `scripts/sandbox/build.sh` باید 0/0 باشد. اگر لایهٔ Client.Services/Contracts را دست زدی، `scripts/sandbox/build.sh full`.
- اگر تستی برای LoginViewModel وجود دارد (`tests/**`)، فقط همان را نگاه کن و در صورت لزوم به مدل جدید به‌روز کن. تست کامل/طولانی نگیر.
**پذیرش:** کامپایل 0/0؛ منطق ورود→نشست سرور→مجاز شدن `/api/commands` و `/licensing/status` از نظر کدی درست و سازگار.
**پوش:** `chore(login): verify client/server integration for online-only auth`

### ⬜ TASK-E — (پایانی) حذف همین فایل پلن
- فقط `Plan/online-login-fix.md` را حذف کن (به `.gitkeep` و بقیهٔ ریپو و رودمپ کاری نداشته باش).
- `scripts/git/push.sh -b genspark_ai_developer "chore(plan): remove completed online-login fix plan"`

---

## 6) ریسک‌ها و نکاتی که مدلِ مجری باید بداند (تا دوباره کشف نکند)

- **هماهنگیِ حسابِ محلی و سرور:** نشست محلی `UserId` را برای `CommandContext` می‌دهد و نشست سرور توکن Bearer را.
  در نصب تازهٔ متصل‌به‌سرور، کاربر محلی هنوز وجود ندارد. دو راه:
  (الف) پس از نشست سرورِ موفق، اگر `IsFirstRunAsync` محلی true بود، یک کاربر محلی معادل بساز/سینک کن؛
  (ب) یا نشست محلی را از روی خروجی سرور hydrate کن. **سادگی و درستی:** در این فاز، پس از نشست سرور موفق،
  `_authentication.SignInAsync` محلی را صدا بزن؛ اگر محلی `CREDENTIALS_INVALID`/first-run داد یعنی کاربر محلی نیست —
  در آن صورت از همان رمز، کاربر محلی را از طریق مسیر موجود (first-run/CompleteFirstRun یا یک مسیر provision محلی) بساز.
  **قبل از کدنویسی TASK-B/C این نکته را با خواندن `SignInCommand`/`CompleteFirstRunSetupCommand` دقیق کن** تا بن‌بست «سرور ok ولی محلی نه» پیش نیاید. این تنها نقطه‌ای است که ممکن است نیاز به بررسی کوتاه اضافه باشد.
- **`IsOffline==true` از `LoginAsync`:** یعنی سرور لحظه‌ای down بود ولی mirrorِ یک ورود آنلاینِ قبلی معتبر است. این مجاز است (ورود تکراری)؛ جلویش را نگیر.
- **env vars:** `ANIMORA_SERVER_URL` (پیش‌فرض `http://188.212.99.234:5000`)، `ANIMORA_TENANT_CODE` (حالا fallbackِ فیلد فرم)، `ANIMORA_NET_LOG=1` (ترِیس شبکه)، `ANIMORA_SERVER_TIMEOUT_SECONDS`.
- **`The system cannot find the file specified.`** در لاگ: از launcher است نه برنامه؛ اگر وقت شد در TASK-D یک نگاه کوتاه به اسکریپت launcher (`scripts/windows/` یا `deploy/`) بینداز، ولی اولویت پایین و خارج از هستهٔ فیکس است. برنامه را بلاک نمی‌کند.
- **نوع خروجی `MapError`:** `internal static` است و در `FirstRunViewModel` هم استفاده می‌شود؛ امضایش را عوض نکن.
- **بیلد:** `scripts/sandbox/build.sh` (Desktop سریع). full فقط وقتی لایه‌های backend را دست زدی. CI روی GitHub Actions هست (`.github/workflows/animora-ci.yml`) — بعد از پوش خودش اجرا می‌شود؛ منتظرش نمان.

## 7) تعریف «تمام‌شده» کل کار
- ورود فقط-آنلاین با کد کلینیک + نام‌کاربری + رمز، نشست سرور اجباری، shell فقط با نشست سرور.
- خطاهای سرور (کد/رمز/کلینیک غلط، مجوز، اشتراک) با پیام فارسی درست نمایش داده شوند.
- پس از ورود، `/api/commands`, `/sync/*`, `/licensing/status` با توکن Bearer مجاز شوند و قابلیت‌ها (ساخت بیمار، فروش، …) کار کنند.
- کامپایل 0/0. هر تسک پوش شده. در پایان این فایل حذف شده.
