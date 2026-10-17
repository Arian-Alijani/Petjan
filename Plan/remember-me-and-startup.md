# Plan — «مرا بخاطر بسپار» + استارت‌آپ/ترِی ویندوز

> فایل برنامه‌ریزی قابل‌ادامه در سشن جدید. هر چیزِ لازم برای پیاده‌سازی اینجا هست؛
> مدل بعدی **نباید** دوباره کل ریپو را بررسی کند. فقط فایل‌هایی که در هر تسک نام
> برده شده را باز کند.
>
> **پروژه هدف:** فقط پروژهٔ دات‌نتی روی ریپو (`src/Animora.Desktop` و لایه‌های
> `Animora.*`). به `Animora-UI/` **اصلاً** دست نزن / نگاه نکن.
>
> **قانون قفل‌شده (حتمی):** بعد از اتمام هر تسک، اسکریپت پوش اجرا شود:
> `scripts/git/push.sh -b genspark_ai_developer "type(scope): message"`
> اگر پوش نشود = شکست. تست کامل و بیلد کامل و تست طولانی **ممنوع** (فقط بیلدِ
> هدفمندِ پروژه‌های تغییریافته مجاز است).

---

## ۱) درخواست کاربر (دو فاز)

1. **«مرا بخاطر بسپار»** در صفحهٔ ورود: چک‌باکس استاندارد؛ اگر تیک بخورد کاربر پس از
   بستن برنامه لاگ‌اوت نشود و دفعهٔ بعد مستقیم وارد شِل شود.
2. **سیستم استارت‌آپ + ترِی:** اگر در تنظیمات فعال شد، برنامه با روشن‌شدن ویندوز
   بالا بیاید، خودش را سینک کند، در پس‌زمینه/بخش hidden icons تسک‌بار بماند و از آنجا
   باز شود؛ کلیک‌راست روی آیکون ترِی گزینهٔ باز کردن و بستن داشته باشد — کاملاً
   استاندارد و هماهنگ با ویندوز.

---

## ۲) معماری کشف‌شده (نتایج خالص — دوباره بررسی نکن)

- **استک UI:** Avalonia **12.1.2** (پین‌شده در `Directory.Packages.props`). خودِ
  Avalonia دارای `TrayIcon` + `NativeMenu`/`NativeMenuItem` توکار است؛ **هیچ پکیج
  اضافه‌ای لازم نیست**.
- **TFM:** `net10.0` (نه `-windows`). `Microsoft.Win32.Registry` در شِیردفریمورکِ
  .NET 10 موجود است → بدون `PackageReference` کامپایل می‌شود (NU1510). پس Registry
  مستقیم قابل‌استفاده است و فقط باید با `OperatingSystem.IsWindows()` گارد شود.
- **پایداری نشست:** نشست محلی در جدول SQLite (`LocalSession`) ذخیره می‌شود و بعد از
  restart باقی می‌ماند. `ISessionService.RestoreAsync` اگر نشستِ فعالِ غیرمنقضی
  وجود داشته باشد `SessionSnapshot` با `UserId` غیر‌null برمی‌گرداند؛ نشستِ بی‌کار
  (idle) قفل‌شده (`IsLocked=true`) برمی‌گردد، نشستِ منقضی `SESSION_EXPIRED`.
  - `SessionSnapshot(UserId, DisplayName, Role, IsLocked, IsFirstRun, ExpiresAtUtc)`
    در `src/Animora.Application/Security/ISessionService.cs`.
- **جریان فعلی ورود:** `App.OnLoginOpened` همیشه فرم ورود را نشان می‌دهد؛ **هیچ
  auto-restore ای ندارد**. پس "Remember Me" یعنی: در استارت‌آپ اگر `RememberMe=true`
  و `RestoreAsync` یک نشستِ signed-in و **غیرقفل** و **غیر-firstRun** برگرداند →
  مستقیم شِل؛ وگرنه فرم ورود. وقتی `RememberMe=false` در استارت‌آپ باید نشستِ ذخیره‌شده
  پایان داده شود (تا دیگر auto-restore نشود).
- **auto-sync در استارت‌آپ:** قبلاً حل است. در `OnLoginOpened`،
  `SyncSchedulerHostedService.Start()` و بعدِ نمایش شِل `LocalSyncStatusService.RefreshAsync()`
  اجرا می‌شوند. پس وقتی auto-login رخ دهد همان مسیر سینک را پوشش می‌دهد.
- **فروشگاه تنظیمات کاربر** (`AppSettings` / `IUserProfileService`) **per-user** است و
  نیازمند نشستِ فعال → برای Remember Me و Start-with-Windows **مناسب نیست** (باید قبل
  از ورود خوانده شوند). به همین دلیل یک **فروشگاه سطح-دستگاه** جداگانه داریم (تسک ۱).
- **DI composition:** `src/Animora.Desktop/App.axaml.cs` متد `ConfigureServices`.
  سرویس‌های دستگاه بعد از `IDataChangeNotifier` ثبت شده‌اند (تسک ۱).
- **صفحهٔ تنظیمات:** `src/Animora.Desktop/Pages/Profile/` — تبِ «ظاهر و تم» (`look`،
  `IsLookTab`) با مجموعهٔ `LookToggles` (نوع `ProfileToggleViewModel(id,label,sub,isOn)`).
  بارگذاری در `BuildFromSettings` (~خط ۶۳۶)، ذخیره در `SaveSettingsAsync` (~خط ۱۰۰۸).
  توجه: toggleهای Look همگی per-user هستند؛ «اجرای خودکار با ویندوز» را **نباید** داخل
  همان جریانِ per-user قاطی کرد — باید toggleِ مستقلِ سطح-دستگاه باشد که مستقیم روی
  `IDevicePreferencesService` + `IStartupRegistration` می‌نویسد.
- **صفحهٔ ورود:**
  - XAML: `src/Animora.Desktop/Pages/Login/LoginWindow.axaml` (پنلِ فرم Grid.Column=1،
    بعد از دکمهٔ «ورود»). code-behind فقط `DataContext = viewModel`.
  - ViewModel: `src/Animora.Desktop/Pages/Login/LoginViewModel.cs`. سازنده:
    `(IAuthenticationService, IDialogService, Func<FirstRunViewModel>, IServerConnectivityService?, LocalTestProfile?, IOnlineAuthClient?, ServerEndpointOptions?)`.
    سه مسیر موفقیتِ ورود: (الف) پیش‌نمایشِ بکاپ در `InitializeAsync`، (ب) `SignInAsync`
    (مسیر اصلی، در انتها `LoginSucceeded?.Invoke`)، (ج) `OnFirstRunSucceeded`.
    `rememberDevice` فعلاً همه‌جا `false` پاس داده می‌شود — این پارامتر به پایداریِ
    نشستِ محلی مربوط است و دستش نزن؛ Remember Me ما مستقل از آن، در لایهٔ device-prefs و
    مسیرِ restoreِ استارت‌آپ عمل می‌کند.
- **شِل / پنجرهٔ اصلی:** `src/Animora.Desktop/Shell/MainWindow.axaml(.cs)`. بستنِ
  پنجره باید به‌جای خروج، به ترِی مخفی شود (close-to-tray). خروجِ کاملِ برنامه از منوی
  ترِی یا از جای دیگر.
- **Program.cs:** `src/Animora.Desktop/Program.cs` — `Main(string[] args)`. آرگومان‌ها
  لاگ می‌شوند. فلگِ `--startup` (ثابتِ `WindowsStartupRegistration.StartupFlag`) باید
  اینجا/در App تشخیص داده شود تا نمونهٔ بوت‌تایم مخفی در ترِی بالا بیاید.
- **اسکریپت پوش:** `scripts/git/push.sh` — `stage ALL → commit → rebase sync → push`.
  استفاده: `scripts/git/push.sh -b genspark_ai_developer "msg"`.
- **ابزارِ بیلد:** `dotnet` در PATH نیست. مسیر: افزودن به PATH با
  `export PATH="/home/user/.dotnet:$PATH" TMPDIR=/home/user/.dotnet-tmp`
  (چون `/tmp` یک tmpfs کوچک ۴۹۳M است و بیلد/ری‌استور آنجا جا نمی‌شود → از
  `/home/user/.dotnet-tmp` به‌عنوان TMPDIR استفاده کن). اگر SDK نبود، با
  `bash /tmp/dotnet-install.sh --channel 10.0 --install-dir /home/user/.dotnet --no-path`
  و `TMPDIR=/home/user/.dotnet-tmp` نصب کن.

---

## ۳) تسک‌لیست (وضعیت‌دار)

### ✅ تسک ۱ — فروشگاه ترجیحاتِ سطح-دستگاه (انجام و پوش شد)
کامیت `63383edf` روی `origin/genspark_ai_developer`.
- `src/Animora.Contracts/Device/DeviceModels.cs`: رکورد `DevicePreferences`
  (`RememberMe`, `StartWithWindows`, `Default`)، اینترفیس `IDevicePreferencesService`
  (`GetAsync/SaveAsync/SetRememberMeAsync/SetStartWithWindowsAsync`)، اینترفیس
  `IStartupRegistration` (`IsSupported/IsEnabled/SetEnabled`).
- `src/Animora.Client.Services/Device/DevicePreferencesFileStore.cs`: JSON در
  `<DataDirectory>/device-prefs.json`، singleton، gate-dar، fail-safe به Default.
- `src/Animora.Client.Services/Device/WindowsStartupRegistration.cs`: کلید
  `HKCU\...\Run` با نامِ `Animora`، فرمانِ `"<exe>" --startup`؛ non-Windows = no-op.
  ثابت `StartupFlag = "--startup"`.
- DI در `App.axaml.cs` ثبت شد (singleton هر دو).
- **نکته:** این تسک قبلاً پوش شده؛ دوباره انجام نده.

### ✅ تسک ۲ — چک‌باکس «مرا بخاطر بسپار» + منطق VM (انجام و پوش شد؛ کامیت `ad051b6c`)
فایل‌ها: `LoginViewModel.cs`, `LoginWindow.axaml`.
- به `LoginViewModel` تزریقِ `IDevicePreferencesService` اضافه کن (پارامتر سازنده بعد از
  `IAuthenticationService` — این سرویس همیشه ثبت شده پس required است، نه optional).
- پراپرتی observable `RememberMe` (پیش‌فرض از `device-prefs` در `InitializeAsync`
  خوانده شود؛ خواندن نباید استارت‌آپ را بلاک کند — fire-and-forget یا await کوتاه).
- در هر مسیر موفقیتِ ورود (SignIn اصلی + first-run)، `SetRememberMeAsync(RememberMe)`
  را persist کن (قبل از `LoginSucceeded?.Invoke`). مسیرِ پیش‌نمایشِ بکاپ را دست نزن.
- XAML: یک `CheckBox` با `Content="مرا به خاطر بسپار"` بایند به `RememberMe` (TwoWay)
  زیرِ دکمهٔ «ورود» (قبل از نوار وضعیتِ اتصال)، هماهنگ با سبکِ RTL موجود.
- پوش:
  `scripts/git/push.sh -b genspark_ai_developer "feat(login): add 'remember me' checkbox + device-pref persistence"`

### ✅ تسک ۳ — بازیابیِ نشست در استارت‌آپ (auto-login) (انجام و پوش شد؛ کامیت `3c83b6ec`)
فایل‌ها: `App.axaml.cs` (و در صورت نیاز یک متد کمکی روی `IAuthenticationService` یا
استفادهٔ مستقیم از `CurrentSessionQuery` از طریق سرویس موجود).
- در `OnLoginOpened` بعد از `InitializeLocalStoreAsync` و قبل از نمایشِ فرم:
  `prefs = await devicePrefs.GetAsync()`.
  - اگر `prefs.RememberMe == true`: نشست را restore کن
    (`CurrentSessionQuery`/`RestoreAsync`). اگر snapshot، `UserId != null` و
    `!IsLocked` و `!IsFirstRun` بود → همان مسیرِ `LoginSucceeded`/`OnSucceeded` را تریگر
    کن تا مستقیم شِل باز شود (بدون نمایشِ فرم). در غیر این‌صورت فرم را نشان بده.
  - اگر `prefs.RememberMe == false`: اگر نشستِ فعالی هست آن را پایان بده
    (`IAuthenticationService.SignOutAsync("remember_me_disabled")` یا معادلِ امن) تا
    auto-restore رخ ندهد؛ سپس فرم ورود.
- مراقب thread: UI روی `Dispatcher.UIThread`؛ restore روی مسیرِ موجود.
- هماهنگی با sign-out: در `SignOutAsync` کاربر (ProfilePageViewModel) بعد از خروج،
  `RememberMe` پاک شود (`SetRememberMeAsync(false)`) تا دفعهٔ بعد auto-login نشود.
  (اگر ساده‌تر است، این پاک‌سازی را در مسیرِ `OnProfileSignedOut` در `App.axaml.cs`
  انجام بده.)
- پوش:
  `scripts/git/push.sh -b genspark_ai_developer "feat(startup): restore session on launch when 'remember me' is on"`

### ✅ تسک ۴ — سرویس استارت‌آپ ویندوز + toggle در تنظیمات (انجام و پوش شد؛ کامیت `2b87efe0`)
فایل‌ها: `ProfilePageViewModel.cs`, `ProfilePageView.axaml` (تبِ «ظاهر و تم»/look).
- `IStartupRegistration` + `IDevicePreferencesService` را به `ProfilePageViewModel`
  تزریق کن (optional تا تستِ VM نشکند).
- یک پراپرتی observable مستقل `StartWithWindows` (نه داخلِ `LookToggles`ِ per-user).
  هنگامِ ساخت VM مقدارِ اولیه از `devicePrefs.GetAsync()` و/یا `IStartupRegistration.IsEnabled()`.
  پراپرتی `IsStartupSupported => registration.IsSupported` برای مخفی‌کردنِ کنترل در
  غیر-ویندوز.
- هنگام toggle: `registration.SetEnabled(value)` + `devicePrefs.SetStartWithWindowsAsync(value)`
  (هر دو؛ رجیستری منبعِ حقیقتِ OS، prefs برای نمایش/هماهنگی). خطا را toast کن.
- XAML: در تبِ look یک `ToggleSwitch` بایند به `StartWithWindows` با
  `IsVisible="{Binding IsStartupSupported}"`، عنوان «اجرای خودکار هنگام روشن شدن ویندوز»،
  زیرنویسِ مناسب.
- پوش:
  `scripts/git/push.sh -b genspark_ai_developer "feat(settings): start-with-windows toggle wired to HKCU Run key"`

### ⬜ تسک ۵ — TrayIcon + close-to-tray + بوتِ --startup مخفی
فایل‌ها: `App.axaml` (+ `App.axaml.cs`), `Program.cs`, `MainWindow.axaml.cs`.
- `TrayIcon` توکارِ Avalonia را در `App.axaml` تعریف کن (آیکون = همان
  `avares://Animora.Desktop/Assets/Brand/vetplus.ico`) با `NativeMenu` شاملِ:
  «باز کردن وت‌پلاس» (show/activate) و «خروج» (shutdown کاملِ برنامه).
  ToolTip مناسب. کلیک‌راست منو را نشان دهد (رفتارِ استانداردِ ترِی).
- بستنِ `MainWindow` → به‌جای خروج، `e.Cancel = true` + `Hide()` (close-to-tray). خروجِ
  واقعی فقط از منوی ترِی (یک فلگِ `_exiting` تا بستنِ نهایی مجاز شود).
- استفاده از ترِی را با `desktop.ShutdownMode = OnExplicitShutdown` هماهنگ کن تا با
  مخفی‌شدنِ پنجره، برنامه نبندد (در حال حاضر بستنِ آخرین پنجره برنامه را می‌بندد).
  **احتیاط:** این روی مسیرِ login→shell و sign-out→login اثر دارد؛ مطمئن شو خروجِ
  نهایی و بازگشت به login درست کار کند.
- بوت `--startup`: در `Program.Main`/`App` اگر `args` شاملِ `--startup` بود، نمونه باید
  مخفی در ترِی بالا بیاید (پنجرهٔ اصلی `Show` نشود تا کاربر از ترِی باز کند)، و auto-sync
  مثلِ معمول اجرا شود. در این حالت هم مسیرِ Remember-Me باید رعایت شود (اگر remember
  نبود، همان فرم ورود — ولی مخفی؟ تصمیم: در حالتِ `--startup` اگر remember نبود، پنجرهٔ
  ورود نرمال نشان داده شود چون بدون ورود چیزی برای سینک نیست).
- پوش:
  `scripts/git/push.sh -b genspark_ai_developer "feat(tray): system tray icon, close-to-tray, hidden --startup boot"`

### ⬜ تسک ۶ — بیلدِ هدفمند + رفعِ خطاها + پوش نهایی
- فقط بیلدِ پروژه‌های تغییریافته (Desktop + Client.Services + Contracts). تستِ کامل
  نگیر. خطاهای کامپایل را رفع کن.
- دستور:
  `export PATH="/home/user/.dotnet:$PATH" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 TMPDIR=/home/user/.dotnet-tmp && dotnet build src/Animora.Desktop/Animora.Desktop.csproj -v q`
- پوش:
  `scripts/git/push.sh -b genspark_ai_developer "fix: resolve build issues for remember-me + startup/tray features"`

### ⬜ تسک ۷ (پایانی) — حذفِ همین فایلِ Plan
- فقط همین فایل (`Plan/remember-me-and-startup.md`) حذف شود. به فایل‌های دیگر و
  roadmap کاری نداشته باش.
- پوش:
  `scripts/git/push.sh -b genspark_ai_developer "chore(plan): remove completed remember-me + startup/tray plan"`

---

## ۴) یادداشت‌های ریز (برای جلوگیری از دوباره‌کاری)
- شاخهٔ کار: `genspark_ai_developer` (قبلاً sync با `origin/main`).
- `rememberDevice` در `SignInCommand` را دستکاری نکن؛ ربطی به Remember-Me ما ندارد.
- فروشگاهِ device-prefs singleton است و cache دارد؛ بعد از نوشتن، خواندن فوراً مقدار
  جدید را می‌دهد.
- `IStartupRegistration` در non-Windows بی‌اثر و `IsSupported=false` است؛ UI باید
  کنترل را در آن حالت مخفی کند.
- متن‌های UI فارسی و RTL، هماهنگ با سبک‌های موجودِ هر صفحه.
