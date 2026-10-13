# Plan — ایزولاسیون کلینیک (tenant)، سینک آفلاین، و دکمهٔ خروج

> این فایل **خودکفا** است. تمام یافته‌ها، تصمیم‌های معماری و مسیرهای دقیق فایل‌ها اینجا نوشته شده‌اند.
> مدلی که در یک سشن جدید این را می‌خواند **نباید مجبور باشد دوباره ریپو را کاوش کند** — مستقیم پیاده‌سازی را شروع کند.
> **قانون قفل‌شده:** بعد از اتمام هر تسک، حتماً `scripts/git/push.sh "msg"` اجرا شود. نکردنِ push = شکست.
> آخرین تسک این فایل = حذف همین فایل.

---

## 0) زمینهٔ معماری (حتماً اول این را بخوان — یک‌بار)

- محصول: **Animora** — مدیریت کلینیک دامپزشکی **SaaS چندمستأجره**. سرور مرکزی یکی است؛ هر کلینیک = یک **tenant** با یک **کد کلینیک (tenantCode)**.
- کلاینت دسکتاپ: **.NET 10 + Avalonia + MVVM**. **offline-first**: دیتابیس محلی **SQLite** دارد و با سرور سینک می‌کند.
- جریان داده: `View → ViewModel → Animora.Contracts interface → Animora.Client.Services impl → Animora.Application dispatch → Animora.Persistence.Sqlite (SQLite محلی)`.
- ساخت/تست در سندباکس (از ریشهٔ ریپو):
  - بیلد سریع فقط دسکتاپ: `scripts/sandbox/build.sh` (باید 0 warning / 0 error)
  - بیلد کل راه‌حل: `scripts/sandbox/build.sh full`
  - اسموک‌ران زیر Xvfb: `scripts/sandbox/run.sh` (موفق = چاپ `SMOKE RUN OK`)
  - **تست کامل/طولانی ممنوع** (طبق دستور کاربر). فقط بیلد هدفمند + در صورت نیاز اسموک‌ران یا تست یونیت نقطه‌ای.

### نحوهٔ تعیین «حالت سرور» در کلاینت
- `src/Animora.Desktop/App.axaml.cs` → `ConfigureServerEndpoint()` مقدار `ANIMORA_SERVER_URL` را می‌خواند.
  - **خالی** → `ServerEndpointOptions.BaseAddress == null` → `IOnlineAuthClient` تزریق نمی‌شود → ورود **فقط محلی** (یوزرنیم/پسورد). یک installation = یک کلینیک. **مشکل ۲ رخ نمی‌دهد.**
  - **پر (SaaS)** → `IOnlineAuthClient` فعال → فرم ورود «کد کلینیک + یوزرنیم + پسورد» را می‌خواهد. **اینجا مشکل ۲ رخ می‌دهد.**

---

## 1) ریشهٔ هر سه مشکل (نتیجهٔ بررسی — قطعی)

### مشکل ۲ (اصلی، امنیتی): نشت دادهٔ کلینیک A بعد از ورود با کلینیک B
**علت ریشه‌ای (قطعی):** store محلی **تک‌دیتابیسه با مسیر ثابت و TenantId ثابت** است:
- مسیر دیتابیس ثابت: `src/Animora.Persistence.Sqlite/Options/LocalStoreOptions.cs` → `DatabasePath` →
  `…/LocalApplicationData/Animora/animora.db` (مک: `~/Library/Application Support/Animora/animora.db`). **وابسته به tenant نیست.**
- TenantId محلی ثابت: `src/Animora.Persistence.Sqlite/Context/LocalInstallationState.cs` →
  `DefaultTenantId = "T-LOCAL-DEFAULT"`؛ `SingleTenantContext` (همان فایل‌دایرکتوری `Context/SingleTenantContext.cs`) همین را برمی‌گرداند.
- scope کوئری‌ها از `ILocalInstallationIdentity.TenantId` می‌آید:
  `src/Animora.Application/Security/SessionCurrentUserProvider.cs` → `BuildQueryContext()` و `NewContext()` از `installation.TenantId` استفاده می‌کنند.
  و `src/Animora.Infrastructure.Local/Security/LocalInstallationIdentity.cs` → `TenantId => state.TenantId` (همان `T-LOCAL-DEFAULT`).
- در ورود آنلاین، `src/Animora.Desktop/Pages/Login/LoginViewModel.cs`:
  - اولین ورود موفق آنلاین با نقش Manager → `CompleteFirstRunAsync` یک owner محلی با `Tenant.LocalDefaultId` می‌سازد.
  - ورودهای بعدی (کلینیک دوم) → `IsFirstRunAsync` دیگر false است → `EnsureLocalMirrorAsync` یک کاربر محلیِ جدید **در همان** `T-LOCAL-DEFAULT` می‌سازد.
- **نتیجه:** دادهٔ کلینیک A و B در **یک دیتابیس با یک TenantId** مخلوط می‌شوند. سرور آن‌ها را جدا نگه می‌دارد (tenant را از bearer-token می‌خواند؛ رجوع به کامنت `src/Animora.Sync.Custom/HttpCommandTransport.cs` → «re-resolves the tenant from membership»)، اما **store محلی هیچ ایزولاسیونی ندارد** → بعد از ورود B، کل دادهٔ A نمایش داده می‌شود.
- نشت دوم: `src/Animora.Infrastructure.Local/Identity/ProtectedSessionStore.cs` → `ReadAsync` از جدول `ClientSessions` فقط «آخرین row بر اساس UpdatedUtc» را می‌خواند؛ چند کلینیک row جدا دارند و این باعث خوانده‌شدن session نادرست می‌شود.

**تصمیم معماری (استاندارد و امن) — Database-per-server-tenant + wipe-on-mismatch:**
بهترین و امن‌ترین رویکرد (ایزولاسیون فیزیکی کامل، بدون نشت) این است که **هر tenant سرور، فایل دیتابیس و secret-store مخصوص خودش را داشته باشد**. این دقیقاً با مکانیزم موجود `LocalTestProfile` (که `DataDirectory` و `ISecretStore` ایزوله می‌سازد — رجوع به `src/Animora.Desktop/LocalTestProfile.cs`) هم‌خانواده است، پس «استاندارد و با حداقل‌اختراع» است.

**چالش ترتیب راه‌اندازی (باید در پیاده‌سازی رعایت شود):** store محلی **قبل از دانستن tenant** init می‌شود
(`App.axaml.cs` → `OnLoginOpened` → `InitializeLocalStoreAsync`)، ولی `tenantCode` فقط **بعد از ورود موفق آنلاین** معلوم می‌شود. راه‌حل:
> در حالت سرور (SaaS)، store عمومی اولیه فقط برای «انتخاب/ورود» لازم نیست؛ دیتابیسِ tenant باید **پس از ورود آنلاین موفق و پیش از باز شدن shell** باز/مهاجرت/seed شود. یعنی مسیر store باید **قابل تعیین در لحظهٔ ورود** باشد (نه فقط در startup).

**انتخاب پیاده‌سازی (سبک‌ترین مسیرِ امن):**
- یک سرویس singleton جدید «محدودهٔ فعال store» می‌سازیم که مسیر دیتابیس/پوشهٔ دادهٔ فعال را نگه می‌دارد و قابل‌تنظیم است
  (نام پیشنهادی: `ActiveTenantStore` / `ITenantStorePathProvider` در `src/Animora.Infrastructure.Local/Storage/`).
- `LocalStoreOptions.DatabasePath` را از این provider مشتق می‌کنیم (به‌جای مقدار ثابت) **فقط وقتی سرور پیکربندی شده**. در حالت بدون‌سرور، رفتار فعلی دست‌نخورده می‌ماند.
- نام پوشهٔ هر tenant: `…/Animora/tenants/<slug>` که `<slug>` = هش پایدار و بی‌خطرِ `tenantCode` (مثلاً `SHA256(tenantCode)` کوتاه‌شده به hex؛ هیچ کاراکتر مسیر خطرناکی نداشته باشد). **هرگز خود tenantCode خام را در مسیر نگذار.**
- در ورود آنلاین (`LoginViewModel.EstablishServerSessionAsync` موفق شد) و **پیش از** `SignInLocalAsync`:
  1. `tenantCode` را به provider بده → مسیر store فعال را روی پوشهٔ همان tenant تنظیم کن.
  2. store را برای آن tenant init کن (همان `ILocalStoreInitializer.InitializeAsync`): migrate + bootstrap + seed.
  3. تمام cacheها/حالت‌های درون‌حافظه‌ای که ممکن است از tenant قبلی مانده باشند را پاک کن (fail-closed).
- **پاکسازی حالت درون‌حافظه هنگام تعویض/ورود (بسیار مهم برای جلوگیری از نشت):**
  - `SessionContextCache.Set(null)` → `src/Animora.Application/Security/SessionContextCache.cs`
  - `CapabilityGateService.Clear()` → `src/Animora.Client.Services/Identity/CapabilityGateService.cs`
  - هر `TenantScopedCache<,>` فعال → `src/Animora.Client.Services/Caching/TenantScopedCache.cs` (کلیدش شامل tenant هست، ولی هنگام تعویض باید `Clear()` شود تا حتی اشتباهاً هم چیزی نماند).
  - `LocalInstallationState` باید برای دیتابیس جدید دوباره bootstrap شود (row نصب در هر دیتابیس tenant جداست).
- **نکتهٔ خیلی مهم دربارهٔ TenantId ردیف‌ها:** چون هر tenant دیتابیس فیزیکی جداست، نیازی نیست `T-LOCAL-DEFAULT` عوض شود؛ ایزولاسیون از «فایل جدا» می‌آید نه از ستون TenantId. این ساده‌ترین و کم‌ریسک‌ترین مسیر است و با `HttpCommandTransport.RebindIdentity` (که tenant سرور را از session می‌زند) کاملاً سازگار است. **این را حفظ کن.**
- **ایزولاسیون session/secret:** `ISecretStore` و در نتیجه `ProtectedSessionStore` باید per-tenant شوند (مثل `LocalTestProfile.CreateSecretStore`). چون جدول `ClientSessions` داخل همان دیتابیس tenant است، با جداشدن فایل، session هر کلینیک هم خودبه‌خود جدا می‌شود؛ فقط باید provider کلید/secret را نیز به پوشهٔ همان tenant مقید کنیم تا توکن‌ها با کلید اشتباه رمزگشایی نشوند.

**ملاک پذیرش مشکل ۲:**
- ورود با کلینیک A، افزودن داده، خروج، ورود با کلینیک B → **هیچ دادهٔ A دیده نشود**؛ B فقط دادهٔ خودش را ببیند؛ بازگشت به A → دادهٔ A دست‌نخورده.
- از نظر امنیتی: فایل‌های دو کلینیک فیزیکی جدا؛ توکن‌ها با کلید tenant دیگر قابل خواندن نباشند.
- حالت بدون‌سرور بدون تغییر رفتار (رگرسیون صفر).

> **اگر در حین پیاده‌سازی مشخص شد که رویکرد database-per-tenant در این ترتیب startup بیش از حد پرریسک است،** مسیر جایگزینِ قابل‌قبول و استاندارد: نگه‌داشتن یک دیتابیس ولی **استفاده از TenantId واقعیِ سرور به‌عنوان tenant محلی** (به‌جای `T-LOCAL-DEFAULT`) + اجبار `ProtectedSessionStore.ReadAsync` به فیلترِ tenant فعال + پاکسازی کامل cache هنگام تعویض. این مسیر ایزولاسیون منطقی می‌دهد (نه فیزیکی) و باید فقط در صورت لزوم انتخاب شود. تصمیم نهایی را در لحظهٔ پیاده‌سازی بر اساس کمترین ریسک/بیشترین امنیت بگیر و در همین‌جا ثبت کن.

### مشکل ۱: سینک درست موارد اضافه‌شدهٔ آفلاین هنگام آنلاین‌شدن
وضعیت فعلی (بررسی‌شده):
- **Push (ارسال تغییرات محلی به سرور):** از طریق outbox/command-journal انجام می‌شود. transport: `src/Animora.Sync.Custom/HttpCommandTransport.cs`
  (`POST /api/commands` و `/batch`). `RebindIdentityAsync` هویت سرور (tenant/user/device) را از session روی envelope می‌زند؛ `OperationId/CommandFingerprint/PayloadJson` دست‌نخورده می‌مانند → **retry idempotent است** (سرور همان نتیجه را برمی‌گرداند، اثر تکراری ایجاد نمی‌شود). نتایج transient → `Deferred` → envelope pending می‌ماند و بعداً دوباره ارسال می‌شود. **این طراحی درست است.**
- **Pull (دریافت حالت سرور):** `src/Animora.Client.Services/Sync/SyncOrchestrator.cs` → cursor فقط وقتی `Applied/UpToDate` جلو می‌رود؛ `ResyncRequired` cursor را عقب نگه می‌دارد تا rebuild انجام شود. gate نشست دارد (بدون session → `Deferred`). **درست است.**
- زمان‌بند: `src/Animora.Client.Services/Sync/SyncSchedulerHostedService.cs` + `SyncScheduleOptions.cs`.

**کاری که این تسک باید انجام دهد (هدفمند، نه بازنویسی):**
1. **اطمینان از این‌که outbox/command-journal در حالت آفلاین واقعاً نگه می‌دارد و بعد از آنلاین‌شدن drain می‌شود** — مسیرهای درگیر: `src/Animora.Persistence.Sqlite/Commands/CommandJournal.cs`, `src/Animora.Application/Outbox/*`, و processor pass در `src/Animora.Client.Services/Sync/LocalSyncStatusService.cs` (طبق PROGRESS، C11: «serialized processor passes»). بررسی کن drain خودکار پس از بازگشت اتصال تریگر می‌شود (یا توسط `SyncSchedulerHostedService` یا توسط connectivity-change).
2. **تداخل با مشکل ۲ (حیاتی):** envelopeهای آفلاینی که با `T-LOCAL-DEFAULT` و کاربر/دستگاه محلی ساخته شده‌اند، هنگام push با session فعال rebind می‌شوند. **باید مطمئن شوی envelopeهای ساخته‌شده در دیتابیس کلینیک A هرگز با session کلینیک B ارسال نشوند.** چون با Database-per-tenant هر tenant outbox جدا دارد و فقط وقتی session همان tenant فعال است drain می‌شود، این تضمین برقرار می‌شود — ولی باید صریحاً تأیید/تست شود (gate: drain فقط برای tenant فعالِ منطبق با session).
3. اگر نقصی در drain/ماندگاری/ترتیب یا در منطبق‌بودن tenant یافتی، فیکس هدفمند بزن.

**ملاک پذیرش مشکل ۱:**
- آفلاین: چند رکورد در بخش‌های مختلف اضافه کن → در outbox به‌صورت pending بماند.
- آنلاین‌شدن → envelopeها drain شوند، سرور همان‌ها را بپذیرد، idempotent بماند، cursor درست جلو برود.
- هیچ envelope متعلق به tenant دیگر با session نادرست ارسال نشود.

### مشکل ۳: دکمهٔ «خروج از حساب» در تنظیمات
وضعیت فعلی (بررسی‌شده): **هیچ دکمهٔ logout در UI وجود ندارد.**
- صفحهٔ تنظیمات: `src/Animora.Desktop/Pages/Profile/ProfilePageViewModel.cs` + `ProfilePageView.axaml` (+ `ProfileItemViewModels.cs`).
- سرویس احراز هویت موجود: `src/Animora.Client.Services/Identity/AuthenticationService.cs` → `SignOutAsync(reason)` (local session را می‌بندد و `CapabilityGateService.Clear()` می‌کند). قرارداد: `src/Animora.Contracts/Identity/IAuthenticationService.cs`.
- در حالت سرور، logout آنلاین هم هست: `src/Animora.Client.Services/Identity/OnlineAuthClient.cs` → `LogoutAsync` (بهترین‌تلاش به `/auth/logout` + `sessions.ClearAsync`). قرارداد: `IOnlineAuthClient`.
- جریان بازگشت به صفحهٔ ورود: فعلاً `App.axaml.cs` فقط مسیر Login→Shell دارد؛ باید مسیر Shell→Login (بازنمایش `LoginWindow` و بستن شل) اضافه شود. event `LoginViewModel.LoginSucceeded` الگوی موجود است.

**کاری که این تسک باید انجام دهد:**
1. یک دکمهٔ «خروج از حساب کاربری» در تنظیمات (`ProfilePageView` + command در `ProfilePageViewModel`) اضافه کن.
2. command باید:
   - `IAuthenticationService.SignOutAsync("USER_LOGOUT")` را صدا بزند.
   - اگر `IOnlineAuthClient` تزریق شده (حالت سرور)، `LogoutAsync` را هم صدا بزند.
   - caches را پاک کند (SessionContextCache/CapabilityGate از داخل SignOut پاک می‌شوند؛ ولی هر cache دیگر هم باید پاک شود).
   - برنامه را به صفحهٔ ورود برگرداند (shell بسته شود، `LoginWindow` دوباره نشان داده شود). این نیاز به یک seam/سرویس برای درخواست logout در سطح shell دارد (مثلاً event یا `IAppSessionController` در `src/Animora.Desktop/Services/`).
3. تأیید بگیر (dialog تأیید اختیاری ولی توصیه‌شده) تا کاربر اتفاقی خارج نشود.

**ملاک پذیرش مشکل ۳:**
- کاربر در تنظیمات دکمهٔ خروج را می‌زند → session بسته می‌شود → به صفحهٔ ورود برمی‌گردد → ورود مجدد لازم است.
- هیچ cache/حالت کلینیک قبلی پس از خروج باقی نماند (پل بین مشکل ۲ و ۳).

---

## 2) لیست تسک‌ها (به ترتیب؛ بعد از هر تسک: push قفل‌شده)

> نقش هر وضعیت: `[ ]` انجام‌نشده · `[~]` در حال انجام · `[x]` انجام‌شده.
> بعد از هر `[x]` → `scripts/git/push.sh "..."` اجرا و در صورت نیاز setup_github_environment.

- [ ] **T0 — آماده‌سازی برنچ:** روی `genspark_ai_developer` برو (`scripts/git/push.sh -b genspark_ai_developer "chore: start tenant-isolation/sync/logout work"`). بیلد پایه را یک‌بار بگیر تا baseline سبز بودن معلوم شود (`scripts/sandbox/build.sh`). **push.**

- [ ] **T3 — دکمهٔ خروج (مشکل ۳، اول چون مستقل و کم‌ریسک):**
  - seam سطح‌shell برای logout بساز: event/سرویس در `src/Animora.Desktop/Services/` (مثلاً `IAppSessionController` با متد `RequestSignOutAsync`) و در `App.axaml.cs` به آن گوش بده تا shell بسته و `LoginWindow` دوباره نشان داده شود (قرینهٔ هندلر `LoginSucceeded`).
  - در `ProfilePageViewModel` یک `RelayCommand SignOutCommand` اضافه کن که `SignOutAsync` (+ در حالت سرور `IOnlineAuthClient.LogoutAsync`) را صدا بزند، سپس `RequestSignOutAsync`.
  - دکمه را در `ProfilePageView.axaml` (ترجیحاً تب overview/حساب کاربری) با متن فارسی «خروج از حساب کاربری» و استایل هشدار اضافه کن. RTL را از قبل App.axaml تنظیم کرده—دوباره تنظیم نکن.
  - dialog تأیید با `IDialogService` اضافه کن.
  - بیلد: `scripts/sandbox/build.sh` (0/0). اسموک‌ران اگر DI/shell را لمس کردی.
  - **push:** `scripts/git/push.sh "feat(profile): add sign-out action returning to the login screen"`

- [ ] **T2 — ایزولاسیون کلینیک/tenant (مشکل ۲، هستهٔ اصلی):**
  - provider مسیر store فعال را بساز (`src/Animora.Infrastructure.Local/Storage/` — singleton، thread-safe) که مسیر دیتابیس/پوشهٔ داده + کلید secret را per-tenant می‌دهد؛ slug = هش پایدار tenantCode.
  - `LocalStoreOptions.DatabasePath` را در حالت سرور از این provider مشتق کن (بدون‌سرور: دست‌نخورده). جایی که `AddAnimoraLocalStore`/`AddAnimoraLocalInfrastructure` و options را می‌سازد: `src/Animora.Persistence.Sqlite/Context/LocalStoreServiceCollectionExtensions.cs` و `src/Animora.Infrastructure.Local/LocalInfrastructureServiceCollectionExtensions.cs`؛ wiring دسکتاپ در `App.axaml.cs` (~خط 250+).
  - `ISecretStore`/`ProtectedSessionStore` را per-tenant کن (الگوی `LocalTestProfile.CreateSecretStore`).
  - در `LoginViewModel`: بعد از ورود آنلاین موفق و **قبل از** `SignInLocalAsync`، provider را روی tenant فعال ست کن و store آن tenant را init کن (`ILocalStoreInitializer`), و تمام حالت درون‌حافظه را پاک کن (`SessionContextCache.Set(null)`, `CapabilityGateService.Clear()`, هر `TenantScopedCache.Clear()`, `LocalInstallationState` دوبارهٔ bootstrap).
  - اطمینان از re-bootstrap `LocalInstallationState` برای دیتابیس جدید (row نصب هر دیتابیس جداست).
  - بیلد: `scripts/sandbox/build.sh full` (چون چند لایه را لمس می‌کند) باید 0/0. اسموک‌ران.
  - اگر فیکس سنگین شد، همان‌جا با پیام واضح push کن (قانون کاربر).
  - **push (نهایی تسک):** `scripts/git/push.sh "fix(identity): isolate each clinic's local store per server tenant (security)"`

- [ ] **T1 — صحت سینک آفلاین (مشکل ۱):**
  - تأیید کن outbox/command-journal در آفلاین نگه می‌دارد و بعد از آنلاین‌شدن drain می‌شود (مسیرها در §1 مشکل ۱). محل drain/processor: `LocalSyncStatusService` + `SyncSchedulerHostedService`.
  - تأیید کن drain فقط برای tenant فعالِ منطبق با session اجرا می‌شود (پل با T2 — جلوگیری از ارسال envelope کلینیک A با session B). چون per-tenant DB است، هر outbox جداست؛ فقط gate منطبق‌بودن session/tenant را صریح کن/تست کن.
  - اگر نقص drain/ماندگاری/ترتیب/تطبیق‌tenant یافتی، فیکس هدفمند بزن.
  - بیلد: `scripts/sandbox/build.sh` (0/0) و در صورت لمس sync، `full`.
  - **push:** `scripts/git/push.sh "fix(sync): ensure offline-queued changes drain correctly and stay tenant-scoped"`

- [ ] **T4 — بازبینی یکپارچه + رگرسیون سبک:**
  - سناریوی A→خروج→B→بازگشت‌A را از نظر منطق کد (و در صورت امکان یک تست یونیت نقطه‌ای کوچک، نه full-suite) بازبینی کن.
  - `scripts/sandbox/build.sh full` نهایی (0/0) + `scripts/sandbox/run.sh` (SMOKE RUN OK).
  - **push:** `scripts/git/push.sh "test(identity): verify tenant isolation + logout + offline sync"`

- [ ] **T5 — پاکسازی نهایی:** این فایل (`Plan/TODO-tenant-isolation-sync-logout.md`) را حذف کن. به هیچ فایل دیگر و فایل roadmap دست نزن.
  - **push:** `scripts/git/push.sh "chore(plan): remove completed tenant-isolation/sync/logout plan"`

---

## 3) نکات قفل‌شده / قیود
- بررسی فایل‌ها فقط هدفمند و بر اساس نیاز؛ کاوش بیهوده ممنوع.
- استاندارد و باکیفیت پیاده‌سازی کن؛ هیچ راه غیراستانداردِ «به‌زور».
- تست کامل/بیلد طولانی ممنوع؛ فقط بیلد هدفمند + اسموک/یونیت نقطه‌ای.
- بعد از هر تسک حتماً push (`scripts/git/push.sh`). اگر exit code=3 شد → `setup_github_environment` سپس دوباره.
- رفتار حالت «بدون سرور» نباید تغییر کند (رگرسیون صفر).
- فقط همین فایل Plan در انتها حذف شود؛ به roadmapها و بقیه کاری نداشته باش.
