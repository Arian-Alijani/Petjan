# Plan — حذف تأیید دستگاه + تغییر رمز کارکنان + ری‌دیزاین پنل ادمین (Operator)

> **این فایل خودبسنده است.** هر مدلی که این را می‌خواند باید بتواند بدون بررسی دوبارهٔ ریپو شروع به
> پیاده‌سازی کند. تمام مسیرها، کلاس‌ها، خطوط کلیدی و تصمیم‌ها اینجا نوشته شده‌اند. فقط فایلی را باز کن که
> این پلن صریحاً می‌گوید باز کن.
>
> **قانون قفل‌شدهٔ پوش:** بعد از اتمام هر تسک (هر چک‌باکس `[x]` که کامل شد) فوراً اجرا کن:
> `scripts/git/push.sh "type(scope): [T<n>] message"` روی شاخهٔ `genspark_ai_developer`.
> اگر پوش نکنی = شکست. اگر فیکس/کد سنگینی وسط یک تسک نوشتی، همان‌جا یک پوش جدا با پیام واضح بزن.
>
> **بیلد/تست:** این سندباکس **SDK دات‌نت ۱۰ ندارد** (طبق `docs/SERVER_REVIEW_REPORT.md` §9) → بیلد/تست
> کامل گرفتن ممنوع و غیرممکن است. فقط صحت نحوی و منطقی کد و سازگاری با الگوهای موجود را دستی تضمین کن.
> **دایرکتوری `Animora-UI/` را باز نکن.** برنامهٔ اصلی = پروژهٔ دات‌نتی در `src/`.

---

## 0) نقشهٔ معماری که کشف شد (پس‌زمینهٔ مشترک همهٔ تسک‌ها)

**Animora** = محصول SaaS مدیریت کلینیک دامپزشکی، چندمستأجری (multi-tenant). «کلینیک» = **Tenant**.

سه لایهٔ درگیر در این تسک‌ها:

1. **سرور مرکزی** `src/Animora.Server/` (ASP.NET Core 10 Minimal API + PostgreSQL). اینجا احراز هویت،
   RBAC، دستگاه‌ها (Device)، و دو «صفحهٔ کنترل» وجود دارد:
   - `Auth/` — لاگین کاربر کلینیک، توکن، ثبت/تأیید/لغو دستگاه، صدور capability snapshot آفلاین.
   - `Operator/` — صفحهٔ **اپراتور فروشنده (Animora)**: مدیریت tenantها، اشتراک، لایسنس، پلن‌ها،
     ریست رمز مالک کلینیک. این «پنل ادمین» موضوع تسک ۳ است.
2. **پنل وب ادمین** `src/Animora.Web.Admin/` (Blazor، Static SSR + یک جزیرهٔ تعاملی). این UIِ همان
   صفحهٔ اپراتور است و با سرور از طریق adapterها در `src/Animora.Server/Admin/*` صحبت می‌کند.
3. **کلاینت دسکتاپ** `src/Animora.Desktop/` (Avalonia/MVVM) = اپ کلینیک. اینجا کاربر کلینیک وارد
   می‌شود؛ مدیر کلینیک از صفحهٔ Profile کارکنان را مدیریت می‌کند (`Pages/Profile/`).

**داده‌ها فقط بین `src/` ویرایش می‌شوند.** لایه‌بندی تمیز (Domain ← Application ← Infra/Presentation).
هیچ EF/SQL/HTTP در View/ViewModel دسکتاپ نباید ظاهر شود.

### سیستم «تأیید دستگاه» (هستهٔ تسک ۱)
«تأییدِ ورودِ کاربر جدید توسط مدیر» در کد به‌صورت **تأیید دستگاه (Device Approval)** پیاده شده، نه تأیید
حساب. زنجیرهٔ کامل:

- `src/Animora.Domain/Identity/Device.cs` — `enum DeviceStatus { Pending=0, Active=1, Revoked=2 }`.
  `Device.Register(...)` دستگاه را با `Status = Pending` می‌سازد. `Approve()` آن را `Active` می‌کند.
  `IsUsable => Status == Active`.
- `src/Animora.Server/Auth/LoginService.cs`:
  - خطوط ۱۲۸–۱۵۶ (`LoginAsync`): دستگاه ناشناس را ثبت می‌کند؛ اگر نقش کاربر **Manager** باشد
    `candidate.Approve()` → auto-approve (مالک قفل نشود)؛ کارکنان عادی **Pending** می‌مانند.
  - خطوط ۲۹۳–۳۷۵ (`RegisterDeviceAsync`): دستگاه جدید؛ باز هم Manager اتوآپرو، بقیه Pending.
    خط ۳۳۷ و ۳۶۴ منطق auto-approve مدیر است.
  - نکتهٔ مهم: **خودِ لاگین برای دستگاه Pending شکست نمی‌خورد** و توکن صادر می‌شود. گیت واقعی جای
    دیگری است (پایین).
- `src/Animora.Server/Authorization/CoarsePolicies.cs` — پالیسی `DeviceActive` (`DeviceActiveRequirement`
  + `DeviceActiveHandler`، خطوط ۲۴–۷۶). شرط: `state.Membership is { DeviceActive: true }`. در شکست
  `ErrorCodes.DeviceRevoked` برمی‌گرداند.
- `src/Animora.Server/Auth/DeviceEndpoints.cs` — `POST /auth/capabilities` با
  `.RequireAuthorization(CoarsePolicies.DeviceActive)` (خط ۲۸). یعنی دستگاه Pending **snapshot
  قابلیت آفلاین نمی‌گیرد**. همچنین `POST /auth/device/{id}/approve|revoke|reinstate` با پرمیشن
  `devices.manage` (خطوط ۲۵–۲۷).
- `src/Animora.Server/Authorization/MembershipResolver.cs` خط ۱۰۴: `DeviceActive = device.IsUsable`.
- **کلاینت**: `src/Animora.Client.Services/Identity/OnlineAuthClient.cs` خطوط ۵۹–۶۷: بعد از لاگین،
  دستگاه را register می‌کند؛ اگر `IsPending(...)` باشد session را پاک می‌کند و با
  `ErrorCodes.DevicePendingApproval` **لاگین را عمداً fail می‌کند** (کاربر عادی با دستگاه تأییدنشده
  اصلاً وارد نمی‌شود). این دقیقاً «ورود باید توسط مدیر تأیید شود» است.
- `src/Animora.Domain/Results/ErrorCodes.cs` خط ۸۲۹:
  `DevicePendingApproval = new("DEVICE_PENDING_APPROVAL", ErrorCode.Forbidden)`.
- پیام فارسی کاربر: `src/Animora.Desktop/Pages/Login/LoginViewModel.cs` خط ۵۰۳
  («دستگاه شما ثبت شد و در انتظار تأیید مدیر کلینیک است.») و
  `src/Animora.Desktop/Pages/Profile/ProfilePageViewModel.cs` خط ۹۷۴.
- UI تأیید در دسکتاپ: `IUserProfileService.ApproveStaffDeviceAsync/RevokeStaffDeviceAsync/
  ReinstateStaffDeviceAsync` (`src/Animora.Contracts/Profile/IUserProfileService.cs`).

### سیستم رمز عبور (هستهٔ تسک ۲)
- هش رمز: `IPasswordHasher<ServerIdentityUser>` (ASP.NET Core Identity، PBKDF2). policy در
  `Animora.Application.Security.PasswordPolicy.Validate(...)`.
- ساخت کاربر کلینیک در سرور: `src/Animora.Server/Auth/StaffProvisioningService.cs` → `CreateAsync`
  (خط ۵۱). رمز را هش می‌کند و کاربر فعال می‌سازد. **متدی برای تغییر/ریست رمزِ یک کاربر دلخواه ندارد.**
- تنها ریست رمز موجود: `src/Animora.Persistence.PostgreSql/Subscriptions/OwnerCredentialAdministration.cs`
  → `ResetAsync(tenantId,...)` که **فقط مالک (اولین Manager)** کلینیک را ریست می‌کند (اپراتور
  فراخوانی می‌کند). endpoint: `POST {Operator}/tenants/{id}/owner/reset-password`
  (`src/Animora.Server/Operator/OperatorEndpoints.cs` خط ۱۳۱).
- سمت کلاینت (مدیر کلینیک): `IUserProfileService` **هیچ متد تغییر رمز کارمند ندارد**.
  `User.SetPasswordHash(...)` و `User.MustChangePassword` در `src/Animora.Domain/Identity/User.cs` موجودند
  (برای الگو بخوان، آن موقع).

### پنل ادمین/اپراتور (هستهٔ تسک ۳)
- UI (Blazor): `src/Animora.Web.Admin/` — صفحات: `Dashboard, Tenants, TenantDetail, TenantNew,
  Provisioning, Plans, LicensingKeys, WebsiteClients, FileMismatches, Audit, System, ChangePassword,
  Login`. ناوبری: `Layout/NavMenu.razor`. استایل: `wwwroot/admin.css`. چندزبانگی **فعلاً وجود ندارد**
  (همهٔ متن‌ها انگلیسی inline). رندرمود: `RenderModePolicy.MaxIslands=1` (فقط یک جزیرهٔ تعاملی مجاز است؛
  `Components/LiveStatusPanel.razor` تنها جزیرهٔ فعلی است).
- Ports: `src/Animora.Web.Admin/Ports/AdminPorts.cs` (اینترفیس‌ها). Models:
  `src/Animora.Web.Admin/Models/AdminModels.cs`.
- Adapterها (پیاده‌سازی Ports، سمت سرور): `src/Animora.Server/Admin/*` (مثلاً `AdminTenantQueries.cs`,
  `AdminSubscriptionAdapter.cs`). ثبت DI: `src/Animora.Server/Admin/AdminServiceCollectionExtensions.cs`.
- کوئری‌های خواندن tenant: `src/Animora.Persistence.PostgreSql/Operator/OperatorReadQueries.cs`
  (`TenantsAsync`, `TenantAsync` و ...). این DbContext **به همهٔ جدول‌ها دسترسی دارد**
  (`db.StaffMemberships`, `db.Devices`, `db.Users` از طریق context مستأجر، `db.Licenses`, ...).
- وضعیت موجود پنل نسبت به خواسته‌ها:
  - ساخت کلینیک ✅ (`TenantNew.razor` + `CreateTenantAsync`).
  - لیست کلینیک‌ها ✅ (`Tenants.razor`).
  - جزئیات کلینیک ✅ (`TenantDetail.razor`) شامل اشتراک + ریست رمز مالک ✅.
  - **دیدن کارکنان/اکانت‌های یک کلینیک ❌ (نیست).**
  - **دیدن محصولات کلینیک ❌ (نیست).**
  - **ساخت دستی کاربر برای یک کلینیک از پنل ❌ (نیست؛ فقط مالک موقع ساخت tenant ساخته می‌شود).**
  - **تغییر دستی رمز هر کاربر یک کلینیک ❌ (فقط مالک).**
  - **لاگ زندهٔ سرور با فیلتر/سرچ ❌ (فقط `System.razor` و `LiveStatusPanel` شمارنده نشان می‌دهند؛
    صفحهٔ Audit رویدادهای امنیتی DB را صفحه‌بندی می‌کند، نه لاگ لایو سرور).**
  - چندزبانگی فا/انگلیسی ❌.
- لاگینگ سرور: `src/Animora.Server/Observability/LoggingSetup.cs` — Serilog به **فایل** و **کنسول**.
  سطح Information؛ Redaction فعال (مسیر/کوئری/بدنه/هدر خام لاگ نمی‌شود — این محدودیت حریم خصوصی را
  در طراحی لاگ لایو باید حفظ کنیم). فایل‌ها: `{DataRoot}/logs/server-*.log`.

---

## 1) تسک ۱ — حذف سیستم تأیید ورود/دستگاه کاربر جدید

**هدف:** وقتی مدیر کلینیک کاربر جدید می‌سازد، کاربر بدون نیاز به «تأیید ورود توسط مدیر» بتواند
بلافاصله وارد شود. یعنی دستگاهِ کاربر جدید باید **خودکار Active** شود و مسیر `Pending` دیگر مانع ورود
نشود.

**استراتژی (تصمیم‌گرفته‌شده):** به‌جای حذف کل مدل Device (که برای **لغو دستگاه سرقت‌شده** هنوز ارزشمند
است و حذفش ریسک و تغییر گستردهٔ migration دارد)، **auto-approve همهٔ دستگاه‌ها** می‌کنیم. دستگاه از
همان لحظهٔ ثبت `Active` می‌شود؛ مفهوم Pending عملاً از بین می‌رود؛ قابلیت `Revoke/Reinstate` دست‌نخورده
باقی می‌ماند (امنیت حفظ می‌شود). این کمترین تغییر، امن‌ترین و سازگار با migrationهای موجود است.

### کارهای دقیق:
- [x] **سرور — ثبت دستگاه همیشه Active.** در `src/Animora.Server/Auth/LoginService.cs`:
  - در `LoginAsync` (حدود خط ۱۳۵–۱۳۹): به‌جای اینکه فقط Manager را `Approve()` کند، **همیشه**
    `candidate.Approve()` را صدا بزن (قبل از `AddDeviceIdempotentAsync`). کامنت auto-approve را
    به «همهٔ دستگاه‌ها به‌صورت خودکار تأیید می‌شوند؛ تأیید دستی مدیر حذف شد» به‌روز کن.
  - در `RegisterDeviceAsync` (حدود خط ۳۳۰–۳۶۷): منطق `isManager` را حذف/بی‌اثر کن؛ هر دستگاه
    موجودِ Pending را `Approve()` کن و هر دستگاه جدید را با `Approve()` بساز. متغیر `isManager`
    و خط ۳۲۷ (`FindActiveMembershipAsync` فقط برای تشخیص مدیر) در صورت بی‌استفاده شدن پاک شود.
  - ⚠️ `Device.Register` را دست نزن (سایر فراخوانی‌ها/تست‌ها به Pningِ اولیه وابسته‌اند)؛ فقط بعد از
    ساخت، `Approve()` صدا بزن. (`Approve()` روی دستگاه Revoked استثنا می‌دهد — اینجا دستگاه تازه است، امن.)
- [x] **کلاینت — مسیر Pending را خنثی کن.** در
  `src/Animora.Client.Services/Identity/OnlineAuthClient.cs` خطوط ۵۹–۶۷: بلوک `if (IsPending(...))`
  را حذف کن (چون سرور دیگر Pending برنمی‌گرداند، این مسیر مرده می‌شود). کامنت را به‌روز کن که تأیید
  دستگاه حذف شده. متد `IsPending` اگر بی‌استفاده شد حذف شود.
- [x] **پیام‌های فارسی را به‌روز/بی‌اثر کن.** `DEVICE_PENDING_APPROVAL` دیگر صادر نمی‌شود اما کد خطا را
  **حذف نکن** (ممکن است تست‌ها یا جاهای دیگر ارجاع دهند)؛ فقط مطمئن شو مسیرهای تولیدش بسته‌اند.
  پیام‌های خطی `LoginViewModel.cs:503` و `ProfilePageViewModel.cs:974` را نگه‌دار (بی‌ضرر، مرده).
- [x] **UI تأیید دستگاه در دسکتاپ.** چون دیگر دستگاه Pending تولید نمی‌شود، بخش «تأیید دستگاه کارکنان»
    در Profile عملاً خالی می‌ماند؛ **لازم نیست حذف شود** (approve/revoke/reinstate برای امنیت مفیدند).
    فقط تأیید کن که با auto-approve تداخلی ایجاد نمی‌شود (approveِ یک دستگاهِ از قبل Active بی‌اثر است).
- [x] **بررسی هم‌خوانی تست‌ها.** فقط گرپ کن: `grep -rn "Pending" tests/ | grep -i device` — اگر تستی
    صراحتاً انتظار دارد کارمندِ non-manager دستگاهش Pending بماند، آن انتظار را به Active اصلاح کن
    (بدون اجرای تست؛ فقط هم‌راستاسازی assertion با رفتار جدید). فایل‌های تست مرتبط را فقط همان موقع باز کن.
- [x] **پوش:** `scripts/git/push.sh "feat(auth): [T1] auto-approve devices, remove manager login approval"`

---

## 2) تسک ۲ — تغییر رمز عبور کارکنان توسط مدیر کلینیک + پنل ادمین

**هدف:** مدیر کلینیک (سمت دسکتاپ) و اپراتور (سمت پنل ادمین) بتوانند **به‌راحتی رمز هر کاربر را عوض کنند**.
امنیت بالا؛ رمز نباید قابل کرک باشد (هش PBKDF2 موجود کافی است — فقط باید از همان hasher و PasswordPolicy
استفاده شود؛ رمز خام هرگز لاگ/ذخیره نشود).

### بخش A — سرور: endpoint تغییر رمز کارمند دلخواه (برای مدیر کلینیک)
- [x] **افزودن متد `SetPasswordAsync` به `StaffProvisioningService`**
    (`src/Animora.Server/Auth/StaffProvisioningService.cs`). امضا مشابه `SetActiveAsync`:
    `SetPasswordAsync(ResolvedMembership actor, string membershipId, SetStaffPasswordServerRequest? request, string correlationId, CancellationToken ct)`.
    منطق (کپی الگوی `SetActiveAsync` خطوط ۱۱۰–۱۶۷):
    - اعتبارسنجی ورودی + `PasswordPolicy.Validate(request.NewPassword)`.
    - پیداکردن membership در tenantِ actor؛ اگر Manager بود یا خودِ actor بود، **مجاز** (مدیر می‌تواند
      رمز خودش و دیگران را عوض کند) — ولی برای تغییر رمز یک Manager دیگر، همان گیت `RolesManage` که در
      `CreateAsync` خط ۸۹–۹۷ هست را اعمال کن. تصمیم: تغییر رمزِ هر کارمند با پرمیشن `users.manage`؛
      تغییر رمز یک Manager نیازمند `roles.manage` اضافه.
    - `user.SetPasswordHash(hasher.HashPassword(ServerUserStore.ToAdapter(user), request.NewPassword))`.
    - `user.RotateSecurityStamp()` تا نشست‌های قبلیِ آن کاربر باطل شوند (امنیت).
    - نوشتن audit (اکشن جدید ثابت: `public const string StaffPasswordAction = "auth.staff.password"`).
    - `decisions.InvalidateUser(user.Id)` بعد از موفقیت.
    - **هرگز رمز خام در audit detail قرار نده.**
- [x] **Wire DTO.** در `src/Animora.Contracts.Wire/Auth/StaffWire.cs` رکورد اضافه کن:
    `SetStaffPasswordServerRequest(string MembershipId, string NewPassword, string? ExpectedChangeToken)`.
    (اول این فایل را بخوان تا سبک رکوردها را رعایت کنی.)
- [x] **Endpoint.** در `src/Animora.Server/Auth/StaffEndpoints.cs` یک
    `group.MapPost("/staff/{id}/password", ...)` با `.RequireAuthorization(PermissionCodes.UsersManage)`
    و `.RequireAuthorization(CoarsePolicies.DeviceActive)` (مطابق بقیهٔ staff endpointها، خط ۳۴)
    اضافه کن که `SetPasswordAsync` را صدا بزند. (اول فایل را بخوان تا الگوی map/fail/ok را کپی کنی.)
- [x] **پوش:** `scripts/git/push.sh "feat(auth): [T2a] server endpoint to set a staff member's password"`

### بخش B — کلاینت دسکتاپ: UI تغییر رمز کارمند (مدیر)
- [x] **افزودن متد به seam.** در `src/Animora.Contracts/Profile/IUserProfileService.cs` اضافه کن:
    `Task SetStaffPasswordAsync(string membershipId, string newPassword, string? expectedChangeToken = null);`
    (کنار `SetStaffActiveAsync`).
- [x] **پیاده‌سازی.** در `src/Animora.Client.Services/Profile/UserProfileService.cs` این متد را پیاده
    کن تا endpoint جدید `/staff/{id}/password` را صدا بزند (الگوی `SetStaffActiveAsync` همان فایل را
    اول بخوان و کپی کن؛ اگر آنلاین‌کلاینت جدا دارد، همان مسیر). اگر پیاده‌سازی فعلاً صرفاً آفلاین/لوکال
    است، حداقل مسیر لوکال `ProvisionLocalMirrorUser`/`SetPasswordHash` را هم‌راستا کن (اول فایل را بخوان).
- [x] **UI.** در صفحهٔ Profile (`src/Animora.Desktop/Pages/Profile/ProfilePageView.axaml` +
    `ProfilePageViewModel.cs`) کنار هر عضو کارکنان (`Staff` / `SelectedStaff`) یک کنش «تغییر رمز»
    با دو فیلد رمز جدید + تأیید و دکمه اضافه کن. از `IToastService`/دیالوگ موجود برای نتیجه استفاده کن.
    اعتبارسنجی حداقلی سمت کلاینت (طول/تطابق) + نمایش خطای سرور از `ErrorMessages`. اول VM را بخوان تا
    الگوی `AddStaffAsync`/`NewMemberError` را دنبال کنی. فقط برای دارندهٔ پرمیشن `users.manage` نمایش بده.
- [x] **پوش:** `scripts/git/push.sh "feat(profile): [T2b] manager can change staff passwords"`

### بخش C — پنل ادمین/اپراتور: تغییر دستی رمز هر کاربر یک کلینیک
> این بخش با تسک ۳.۶ یکی است و آنجا پیاده می‌شود (endpoint عمومی ریست رمزِ هر کاربر + UI). اینجا فقط
> اشاره: ریست رمز مالک از قبل هست؛ باید به «هر کاربر» تعمیم یابد. جزئیات در §۳ پایین.

---

## 3) تسک ۳ — ری‌دیزاین کامل پنل ادمین (Operator) : `src/Animora.Web.Admin/` + adapter/endpointهای سرور

**محدودهٔ پنل = اپراتور فروشنده.** «کلینیک» = tenant. اصول: استاندارد پنل‌های ادمین مشابه، UX تمیز، سبک،
امن، فا/انگلیسی. رندرمود فعلی Static SSR + حداکثر ۱ جزیرهٔ تعاملی را حفظ کن (برای لاگ لایو از همان ۱
جزیره استفاده می‌شود؛ `RenderModePolicy.MaxIslands` را فقط در صورت نیاز واقعی به ۲ افزایش بده و دلیلش را
کامنت کن — ترجیح: لاگ لایو همان تک‌جزیره باشد و `LiveStatusPanel` در داشبورد بماند، یا هر دو را در یک
کامپوننت ادغام کن).

### ۳.۱ — زیرساخت چندزبانگی فا/انگلیسی
- [x] یک سرویس سادهٔ منابع متن بساز: `src/Animora.Web.Admin/Localization/AdminStrings.cs` با دیکشنری
    `en`/`fa` و یک `AdminCulture` (کوکی `admin_lang`، پیش‌فرض `fa`, RTL وقتی fa). از پیچیدگی `.resx`
    پرهیز کن (سبک بماند). یک متد `string T(string key)` که از culture فعلی می‌خواند.
- [x] در `Layout/MainLayout.razor` کلید سوییچ زبان (fa/en) + ست‌کردن `dir="rtl"`/`ltr` و `lang` روی
    `<html>`/wrapper. کوکی را Static SSR سازگار ست کن (فرم POST کوچک یا query).
- [x] تمام متن‌های inline صفحات/ناوبری را با `@T("key")` جایگزین کن (به‌تدریج در هر صفحه‌ای که در
    تسک‌های بعدی لمس می‌شود؛ برای صفحات لمس‌نشده حداقل عنوان و ناوبری ترجمه شود).
- [x] `wwwroot/admin.css`: استایل RTL (jer جهت، فاصله‌ها) + تمیزکاری ظاهری (کارت، جدول، بج وضعیت،
    فاصله‌گذاری). سبک و بدون وابستگی سنگین.
- [x] **پوش:** `scripts/git/push.sh "feat(admin): [T3.1] fa/en localization + RTL + css cleanup"`

### ۳.۲ — صفحهٔ کارکنان/اکانت‌های یک کلینیک (مشاهده)
- [x] **کوئری سرور.** در `OperatorReadQueries.cs` متد
    `StaffAsync(string tenantId, int page, int size, CancellationToken ct)` اضافه کن که از
    `db.StaffMemberships` + join به `db.Users` (توجه: Users تحت فیلتر tenant است؛ الگوی tenant-bound
    context را از `OwnerCredentialAdministration.ResetAsync` خطوط ۳۱–۳۷ کپی کن) روستر را برگرداند:
    (UserId, MembershipId, DisplayName, UserName, Role, Active, DeviceCount). صفحه‌بندی مثل بقیه.
- [x] **Model + Port.** در `AdminModels.cs`: `record StaffAccountSummary(string MembershipId,string UserId,
    string Name,string UserName,string Role,bool Active,int Devices)`. در `AdminPorts.cs` اینترفیس
    `IAdminStaffQueries { Task<Result<AdminPage<StaffAccountSummary>>> ListAsync(string tenantId,int page,int pageSize,CancellationToken ct=default); }`.
- [x] **Adapter + DI.** `src/Animora.Server/Admin/AdminStaffAdapter.cs` که `IAdminStaffQueries` را روی
    `OperatorReadQueries` پیاده کند؛ ثبت در `AdminServiceCollectionExtensions.cs` (الگوی
    `AdminTenantQueries` را کپی کن).
- [x] **UI.** زیرصفحه/تب در `TenantDetail.razor` (یا صفحهٔ جدید `/admin/tenants/{id}/staff`) که جدول
    کارکنان را با فیلتر نقش و جست‌وجوی نام نشان دهد + دکمهٔ هر ردیف برای «ریست رمز» (۳.۶) و وضعیت دستگاه.
- [x] **پوش:** `scripts/git/push.sh "feat(admin): [T3.2] per-clinic staff roster view"`

### ۳.۳ — ساخت دستی کاربر برای یک کلینیک از پنل
- [x] **Endpoint سرور.** یک مسیر اپراتوری در `OperatorEndpoints.cs`:
    `POST {Operator}/tenants/{id}/staff` که کاربر+عضویت در آن tenant بسازد. منطق را از
    `StaffProvisioningService.CreateAsync` بردار اما چون اینجا **actor اپراتور است نه کاربر کلینیک**،
    یک سرویس موازیِ ساده در persistence بساز (الگوی `OwnerCredentialAdministration` با tenant-bound
    context خطوط ۳۱–۳۷): ساخت `User.Create` + `SetPasswordHash` (رمز موقت تولیدشده با
    `ProvisioningEngine.GenerateTemporaryPassword()`) + `StaffMembership.Create(role)` + audit
    (`SecurityAuditEntry` + `OperatorAudit`). رمز موقت را **یک‌بار** در پاسخ برگردان.
- [x] **Port/Model/Adapter.** `IAdminStaffCommands { Task<Result<MutationReceipt>> CreateStaffAsync(string tenantId, CreateStaffAdminForm form, CancellationToken ct=default); }`
    با فرم `CreateStaffAdminForm(string Name,string UserName,string Role)`. `MutationReceipt.TemporaryPassword`
    از قبل وجود دارد. Adapter در `src/Animora.Server/Admin/`.
- [x] **UI.** فرم ساخت کاربر در صفحهٔ staff کلینیک (ConfirmCheckbox، FormError، نمایش رمز موقت یک‌بار
    مثل `TenantDetail.razor` خط ۷).
- [x] **پوش:** `scripts/git/push.sh "feat(admin): [T3.3] create clinic staff from admin panel"`

### ۳.۴ — مشاهدهٔ محصولات یک کلینیک + فیلتر بر اساس شاخص‌ها
- [ ] **اکتشاف محدود (همان‌موقع):** مدل محصول در سرور/PG کجاست؟ گرپ هدفمند:
    `grep -rln "class Product\|Products\|Inventory" src/Animora.Persistence.PostgreSql/ src/Animora.Domain/Inventory/`.
    جدول محصولات tenant-scoped است؛ خواندنش را با tenant-bound context (الگوی ۳۱–۳۷ بالا) انجام بده.
    **اگر محصولات هنوز به سرور replicate نشده‌اند** (طبق roadmap ممکن است Track D/E ناتمام باشد)، آنگاه
    این زیرتسک را به «نمایش شمارش/خلاصهٔ موجود در snapshot/replica سرور» محدود کن و در پلن یادداشت بگذار
    که دادهٔ کامل محصولات سمت سرور در دسترس نیست. **قبل از پیاده‌سازی این را بررسی و تصمیم بگیر.**
- [ ] کوئری + Model + Port + Adapter + UI مشابه ۳.۲، با فیلترهای: نام/کد، دسته، وضعیت موجودی
    (کم‌موجود/ناموجود)، بازهٔ قیمت. صفحه‌بندی.
- [ ] **پوش:** `scripts/git/push.sh "feat(admin): [T3.4] per-clinic products view with filters"`

### ۳.۵ — مشاهدهٔ «تمام اطلاعات ثبت‌شدهٔ» کلینیک + فیلتر عمومی
- [ ] با استفاده از صفحهٔ Audit موجود (`Pages/Audit.razor` + `IAdminAuditQueries.TenantAsync`) یک نمای
    جامع‌تر برای یک tenant بده: فیلتر بر اساس action، بازهٔ زمانی، actor. اگر `AdminAuditQuery` کفایت
    می‌کند فقط UI را به tenant گره بزن (لینک از TenantDetail). کوئری جدید فقط در صورت کمبود فیلد.
- [ ] **پوش:** `scripts/git/push.sh "feat(admin): [T3.5] clinic-scoped audit/data explorer with filters"`

### ۳.۶ — ریست/تغییر دستی رمز هر کاربر کلینیک (مالک و کارکنان) از پنل
- [ ] **تعمیم `OwnerCredentialAdministration`.** یک متد عمومی‌تر اضافه کن (یا سرویس جدید
    `StaffCredentialAdministration`) `ResetUserAsync(string tenantId, string userId, ActorRef actor, ct)`
    که به‌جای «اولین Manager»، کاربرِ مشخص‌شده با `userId` را در آن tenant ریست کند (الگوی
    `ResetAsync` خطوط ۲۰–۴۹ را کپی و پارامتریک کن؛ `expectedStamp`/concurrency را برای کاربر مناسب
    تطبیق بده یا با قفل `FOR UPDATE` روی خود user ساده‌اش کن). رمز موقت تولید کن، `SetPasswordHash`،
    `RotateSecurityStamp`، audit امنیتی + اپراتور. رمز خام هرگز لاگ نشود.
- [ ] **Endpoint.** `POST {Operator}/tenants/{id}/staff/{userId}/reset-password` در `OperatorEndpoints.cs`.
- [ ] **Port/Adapter/UI.** به `IAdminStaffCommands` متد `ResetStaffPasswordAsync(tenantId,userId,...)`
    اضافه کن؛ در جدول کارکنان (۳.۲) دکمهٔ ریست هر ردیف → نمایش رمز موقت یک‌بار.
- [ ] **پوش:** `scripts/git/push.sh "feat(admin): [T3.6] reset password for any clinic user"`

### ۳.۷ — لاگ زندهٔ سرور با فیلتر و سرچ
> بزرگ‌ترین زیرتسک. امنیت/سبکی حیاتی است: **فقط اپراتور (`AdminOnly`/`OperatorOnly`) دسترسی داشته باشد؛
> هیچ داده حساس (رمز/توکن/PII) نشت نکند** — Redaction موجود (`RedactingEnricher`, `SecretDestructuringPolicy`
> در `LoggingSetup.cs`) باید در مسیر لاگ لایو هم اعمال شود.
- [ ] **سینک حلقوی در‌حافظه.** یک Serilog sink جدید `src/Animora.Server/Observability/RingBufferLogSink.cs`
    که آخرین N رویداد (مثلاً ۱۰۰۰، سقف ثابت و سبک) را در یک ring buffer نگه دارد و یک رویداد/کانال
    (`System.Threading.Channels`) برای مشترکان لایو منتشر کند. **بعد از** `RedactingEnricher` و
    `SecretDestructuringPolicy` قرار بگیرد (در `LoggingSetup.CreateLogger` مثل `ConsoleSink` با
    `config.WriteTo.Sink(...)`). هر رکورد: timestamp, level, category/source, renderedMessage (بدون
    مسیر/هدر/بدنهٔ خام — همان سیاست موجود).
- [ ] **سرویس + پورت.** سینک را singleton ثبت کن؛ یک `IAdminLiveLog` در `AdminPorts.cs`:
    `IReadOnlyList<LogLineModel> Snapshot(LogFilter filter)` + یک روش subscribe برای جزیرهٔ تعاملی
    (مثلاً `IAsyncEnumerable<LogLineModel> Stream(CancellationToken)` یا الگوی `PeriodicTimer` مثل
    `LiveStatusPanel`). Model: `LogLineModel(DateTimeOffset Ts,string Level,string Source,string Message)`
    و `LogFilter(string? Level,string? Search)`.
- [ ] **فعال/غیرفعال‌سازی لایو.** طبق خواسته «هنگام فعال کردن» → یک دکمه Start/Stop در UI؛ وقتی
    غیرفعال است هیچ polling/stream نرود (سبکی سرور). ring buffer همیشه پر می‌شود (ارزان) اما stream فقط
    با فعال‌سازی وصل شود.
- [ ] **UI (جزیرهٔ تعاملی).** `src/Animora.Web.Admin/Pages/Logs.razor` + کامپوننت تعاملی
    `Components/LiveLogPanel.razor` (الگوی `LiveStatusPanel.razor`: `@rendermode InteractiveServerRenderMode(prerender:false)`,
    `PeriodicTimer`, `IAsyncDisposable`). امکانات: سوییچ Start/Stop، فیلتر سطح (Info/Warn/Error)،
    باکس سرچ متن (client-side روی بافر یا server-side روی snapshot)، auto-scroll. چون `MaxIslands=1`،
    یا این را تنها جزیره کن (و LiveStatus را به polling SSR/حذف از island تبدیل کن) یا `MaxIslands` را
    به ۲ ببر با کامنت توجیهی. **تصمیم پیش‌فرض:** `MaxIslands=2` با کامنت «status + live log».
- [ ] **ناوبری.** لینک «لاگ‌ها / Logs» در `NavMenu.razor` با `@T(...)`. صفحه `[Authorize(Policy="AdminOnly")]`.
- [ ] **پوش:** `scripts/git/push.sh "feat(admin): [T3.7] live server log viewer with filter & search"`

### ۳.۸ — داشبورد تمیز + بازبینی نهایی UX/امنیت
- [ ] داشبورد (`Pages/Dashboard.razor`) را مرتب کن: کارت‌های خلاصه (tenantها، expiring, past-due از
    `AdminStatus`) + لینک‌های سریع. ترجمه‌شده. `LiveStatusPanel` را نگه‌دار.
- [ ] بازبینی امنیت: همهٔ صفحات/endpointهای جدید `AdminOnly`/`OperatorOnly` + rate limit
    (`RateLimitPolicies.Operator`) + AntiforgeryToken روی فرم‌ها + عدم نشت رمز/توکن در لاگ و UI.
- [ ] بازبینی سبکی: کوئری‌ها صفحه‌بندی‌شده، بدون N+1 واضح، لاگ لایو سقف‌دار.
- [ ] **پوش:** `scripts/git/push.sh "feat(admin): [T3.8] dashboard polish + security/perf review"`

---

## 4) تسک پایانی — حذف این فایل پلن
- [ ] فقط همین فایل `Plan/admin-auth-overhaul.md` را حذف کن (`git rm Plan/admin-auth-overhaul.md`).
    به هیچ فایل دیگر یا رودمپ دست نزن.
- [ ] **پوش:** `scripts/git/push.sh "chore(plan): remove completed admin-auth-overhaul plan"`

---

## 5) یادآوری‌های اجرایی (برای مدل ادامه‌دهنده)
- شاخهٔ کاری: `genspark_ai_developer`. اگر اعتبار گیت نبود، ابزار `setup_github_environment` را صدا بزن
  و دوباره پوش کن.
- بعد از **هر** چک‌باکس کامل‌شده پوش کن. پیام‌ها conventional + پیشوند `[T<n>]`.
- فقط فایلی را باز کن که این پلن اسم برده؛ بررسی بیهوده ممنوع. `Animora-UI/` و `Database-Sampel/` را
  هرگز باز نکن.
- بیلد/تست کامل نگیر (SDK نیست). صحت را دستی و با تطبیق الگوهای موجود تضمین کن.
- وقتی تمام چک‌باکس‌های تسک‌های ۱ تا ۳ و تسک پایانی `[x]` شدند، کار تمام است.
