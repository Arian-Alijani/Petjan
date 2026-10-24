# =============================================================================
# publish-server-connected.ps1  —  Build & publish Animora.Desktop wired to the
#                                   REAL Animora server (online mode).
# =============================================================================
# PURPOSE
#   Produce a fully self-contained, single-file Windows build of Animora.Desktop
#   that connects to the LIVE Animora.Server over the network and uses the real
#   backend (PostgreSQL-backed server) — NOT the offline/local-test mock.
#
#   The server address is baked into the produced launcher as the
#   ANIMORA_SERVER_URL environment variable so a double-click "just works":
#
#       Default server : http://188.212.99.234:5000
#
#   The app reads ANIMORA_SERVER_URL at startup (App.ConfigureServerEndpoint);
#   when it is a valid absolute http(s) URL the desktop wires the HTTP sync
#   engine + online auth/entitlement/device planes and talks to the real server.
#
# WHY THIS IS A SEPARATE SCRIPT (vs. publish-portable.ps1)
#   publish-portable.ps1 launches the app with --local-test-fresh, i.e. an
#   isolated OFFLINE test profile with throwaway data. THIS script does the
#   opposite: it launches the app normally so it reaches the real server and
#   uses real clinic data/accounts served by Animora.Server.
#
# =============================================================================
# USAGE (run on a Windows machine that HAS the .NET 10 SDK — the BUILD machine):
#
#   .\scripts\windows\publish-server-connected.ps1
#       Build for win-x64, server = http://188.212.99.234:5000, and zip it.
#
#   .\scripts\windows\publish-server-connected.ps1 -ServerUrl http://10.0.0.5:5000
#       Target a different server address.
#
#   .\scripts\windows\publish-server-connected.ps1 -Runtime win-arm64
#       Build for ARM64 Windows instead of x64.
#
#   .\scripts\windows\publish-server-connected.ps1 -NoZip
#       Produce only the folder, skip the .zip.
#
#   .\scripts\windows\publish-server-connected.ps1 -OutputRoot D:\share -Force
#       Choose the output location and overwrite without prompting.
#
# THEN
#   Give the produced .zip (or folder) to the clinic PC. They unzip it anywhere
#   and double-click "Run-Animora.cmd". Nothing to install — the app connects to
#   the real server and signs in with real accounts.
#
# =============================================================================
# SAFETY NOTES
#   * This script ONLY publishes into an isolated output folder (default:
#     <repo>\publish\...). It NEVER edits application source and does NOT touch
#     src\**\bin or src\**\obj used by normal dev builds.
#   * It does NOT create/seed/delete any database. The server owns all data.
#   * The app uses its real authentication/entitlement flow against the server.
# =============================================================================

[CmdletBinding()]
param(
    # Only the officially supported desktop RIDs (STACK.md native matrix).
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime = 'win-x64',

    # Absolute http(s) base address of the live Animora server. Baked into the
    # launcher as ANIMORA_SERVER_URL. Default is the production server.
    [string]$ServerUrl = 'http://188.212.99.234:5000',

    # Where to place the output folder + zip. Defaults to <repo>\publish.
    [string]$OutputRoot,

    # Skip zipping (leave just the folder).
    [switch]$NoZip,

    # Overwrite an existing output folder for this runtime without prompting.
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
# Make native command exit codes behave the same on Windows PowerShell 5.1 and 7.
$PSNativeCommandUseErrorActionPreference = $false

# -- Must run on Windows: self-contained win-* publish + native SQLCipher need it.
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    throw 'publish-server-connected.ps1 must be run on Windows (it produces a Windows build). Run it on a PC that has the .NET 10 SDK.'
}

# -- Validate the server URL EARLY so a typo fails before a multi-minute build.
$ServerUrl = $ServerUrl.Trim()
$parsedUri = $null
if (-not [Uri]::TryCreate($ServerUrl, [UriKind]::Absolute, [ref]$parsedUri) -or
    ($parsedUri.Scheme -ne 'http' -and $parsedUri.Scheme -ne 'https')) {
    throw "Invalid -ServerUrl '$ServerUrl'. It must be an absolute http(s) URL, e.g. http://188.212.99.234:5000"
}
# Normalize to the scheme://host:port form the app expects (ServerEndpointOptions
# appends relative paths itself; a trailing slash is fine either way).
$ServerUrl = $parsedUri.GetLeftPart([System.UriPartial]::Authority)

# -- Require the .NET SDK on the BUILD machine.
$Dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $Dotnet) {
    throw 'The .NET 10 SDK is required on THIS machine to build the app. Install it from https://dotnet.microsoft.com/download then rerun.'
}

# -- Resolve paths (scripts\windows\ -> repo root).
$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$Proj     = Join-Path $RepoRoot 'src\Animora.Desktop\Animora.Desktop.csproj'
if (-not (Test-Path $Proj -PathType Leaf)) { throw "Cannot find desktop project: $Proj" }

if (-not $OutputRoot) { $OutputRoot = Join-Path $RepoRoot 'publish' }
$Stamp        = Get-Date -Format 'yyyy-MM-dd'
$PkgName      = "Animora-Server-$Runtime-$Stamp"
$PkgDir       = Join-Path $OutputRoot $PkgName
$AppSubDir    = Join-Path $PkgDir 'app'   # the published app lives here; launcher sits beside it

Write-Host '====================================================================='
Write-Host ' Animora — server-connected publish (real backend)'
Write-Host '====================================================================='
Write-Host "==> Repo:     $RepoRoot"
Write-Host "==> Runtime:  $Runtime"
Write-Host "==> Server:   $ServerUrl"
Write-Host "==> Output:   $PkgDir"
Write-Host "==> Behavior: launches ONLINE against the real server (real accounts & data)"
Write-Host ''

# -- Prepare a clean output folder (only ever under the isolated publish dir).
if (Test-Path $PkgDir) {
    if (-not $Force) {
        Write-Host "==> Output folder already exists: $PkgDir"
        $answer = Read-Host '    Delete and recreate it? (y/N)'
        if ($answer -notmatch '^(y|yes)$') { throw 'Aborted: output folder already exists. Use -Force to overwrite, or pass a different -OutputRoot.' }
    }
    Remove-Item -Recurse -Force $PkgDir
}
New-Item -ItemType Directory -Force -Path $AppSubDir | Out-Null

# =============================================================================
# 0. CLEAN the desktop project's intermediate output FIRST.
#    This is the single most important step for avoiding the runtime crash
#      "No precompiled XAML found for Animora.Desktop.App".
#    That crash means the produced assembly was emitted WITHOUT the compiled
#    Avalonia XAML resource — which happens when an INCREMENTAL build reuses a
#    stale obj/ (e.g. a prior Debug or design-time build) and skips CoreCompile
#    (where the Avalonia XAML compiler runs). Deleting bin/ and obj/ for this
#    project forces a full compile, so the XAML is always re-embedded.
#    We only ever delete THIS project's bin/obj (never source, never other
#    projects), so a dev's normal build tree is unaffected beyond a recompile.
# =============================================================================
$ProjDir = Split-Path -Parent $Proj
foreach ($sub in @('bin', 'obj')) {
    $dir = Join-Path $ProjDir $sub
    if (Test-Path $dir) {
        Write-Host "==> Cleaning stale $sub\ ($dir) to guarantee the XAML is recompiled ..."
        Remove-Item -Recurse -Force $dir
    }
}

# =============================================================================
# 1. Publish: Release, self-contained, single-file.
#    - self-contained  => bundles the .NET runtime (no install on target PC)
#    - single-file      => one Animora.Desktop.exe (plus required native libs)
#    - EnableAvaloniaXamlCompilation=true => avoids the "No precompiled XAML"
#      startup crash.
#    A full (non-incremental) compile is GUARANTEED by step 0 above, which
#    deletes this project's bin/ and obj/ before we publish — so there is no
#    stale output left for an incremental build to reuse, and CoreCompile (where
#    the Avalonia XAML compiler runs) always runs. The csproj's
#    VerifyCompiledAvaloniaXaml target then hard-fails the build if (despite all
#    this) the XAML resource is still missing, so a broken package can never be
#    shipped.
#    NOTE: we intentionally do NOT pass the `dotnet build` switch
#    `--no-incremental` here. `dotnet publish` does not accept it on every SDK
#    (SDK 10.0.4xx leaks it straight to MSBuild, which rejects it with
#    "MSB1001: Unknown switch" and aborts the publish). The bin/obj clean above
#    already provides the exact same "never reuse a stale compile" guarantee.
#    Native SQLCipher/e_sqlite3 binaries are copied next to the exe (not
#    embedded) so SQLite loads reliably — IncludeNativeLibrariesForSelfExtract
#    is intentionally left OFF.
# =============================================================================
Write-Host '==> Publishing (Release, self-contained, single-file). This can take a few minutes...'
& $Dotnet.Source publish $Proj `
    -c Release `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:EnableAvaloniaXamlCompilation=true `
    -p:DebugType=none `
    -p:DebugSymbols=false `
    -o $AppSubDir
$PublishExit = $LASTEXITCODE
if ($PublishExit -ne 0) {
    throw "dotnet publish FAILED (exit $PublishExit). The build was NOT created. Your source and existing builds are untouched."
}

$ExeName = 'Animora.Desktop.exe'
$ExePath = Join-Path $AppSubDir $ExeName
if (-not (Test-Path $ExePath -PathType Leaf)) {
    throw "Publish finished but '$ExeName' was not found in $AppSubDir. Nothing was shipped."
}

# =============================================================================
# 2. Create the end-user launcher (double-click = connect to the real server).
#    We ship BOTH a .cmd (most reliable double-click) and a .ps1 helper. Both
#    set ANIMORA_SERVER_URL for the child process ONLY (no machine-wide change),
#    then start the app normally (no --local-test flag => real backend).
# =============================================================================
Write-Host '==> Writing end-user launcher ...'

$LauncherCmd = Join-Path $PkgDir 'Run-Animora.cmd'
$CmdContent = @"
@echo off
rem ===========================================================================
rem  Animora - launcher (connects to the real server, WITH LIVE SERVER TRACE)
rem ---------------------------------------------------------------------------
rem  No installation needed. Just double-click this file.
rem
rem  This window STAYS OPEN and prints a detailed, step-by-step trace of the
rem  app's conversation with the server: every request it sends, every response
rem  that comes back (status + timing + body), and every connection error. Use
rem  it to diagnose server-connection problems. Keep this window open while you
rem  use the app; close it (or press a key after the app exits) when done.
rem
rem  FULL PATIENTS/OWNERS TRACE: with ANIMORA_TRACE_DEBUG=1 below, the SAME
rem  window also prints the complete end-to-end load path for the "سرپرستان"
rem  (owners) and "بیماران" (patients) pages: the page ViewModel load, the query
rem  service, the dispatcher, the LOCAL SQL projection, and the sync pull/apply
rem  (including whether a synced Owner/Pet row was actually materialised into the
rem  local tables). Categories to grep: patients, svc.patients, sql.patients,
rem  dispatch, sync, sync.apply, sync.push, notify, sync.notify.
rem  Message delivery status (admin-panel messages reaching this PC): look for the
rem  sync.notify lines - fetch / applied / receipt / skip.
rem
rem  To point this copy at a different server, edit the SET line below.
rem  To run WITHOUT the server trace, set ANIMORA_NET_LOG=0.
rem  The app ALSO writes its own activity log (startup, clicks, navigation,
rem  errors) to: %LOCALAPPDATA%\Animora\logs\animora-<date>.log
rem  Set ANIMORA_TRACE_DEBUG=1 for extra-verbose app diagnostics (already on).
rem ===========================================================================
setlocal
chcp 65001 >nul
set "ANIMORA_SERVER_URL=$ServerUrl"
set "ANIMORA_NET_LOG=1"
set "ANIMORA_TRACE_DEBUG=1"
echo =====================================================================
echo  Animora - server-connected launch (LIVE TRACE)
echo  Server : %ANIMORA_SERVER_URL%
echo  This window shows every request/response to/from the server, plus the
echo  app's own activity (startup, clicks, navigation, errors) AND the full
echo  patients/owners load trace (patients, sql.patients, sync.apply, ...).
echo  Message delivery status: look for sync.notify lines - fetch / applied / receipt / skip.
echo  App log file: %LOCALAPPDATA%\Animora\logs\animora-<date>.log
echo  Leave this window open while you use the app.
echo =====================================================================
echo.
cd /d "%~dp0app"
rem Run in the FOREGROUND (no 'start') so the app's console output streams into
rem THIS window and the window does not close while the app is running.
"Animora.Desktop.exe"
echo.
echo =====================================================================
echo  Animora has exited (exit code %ERRORLEVEL%).
echo  The full server trace is shown above. Press any key to close.
echo =====================================================================
pause >nul
endlocal
"@
# Write as UTF-8 WITHOUT BOM so cmd.exe does not choke on a leading BOM.
[System.IO.File]::WriteAllText($LauncherCmd, ($CmdContent -replace "`r?`n", "`r`n"), (New-Object System.Text.UTF8Encoding($false)))

$LauncherPs1 = Join-Path $PkgDir 'Run-Animora.ps1'
$Ps1Content = @"
# Animora — launcher (connects to the real server, WITH LIVE SERVER TRACE).
# No installation required. This window stays open and prints a detailed,
# step-by-step trace of every request/response between the app and the server,
# plus any connection error — use it to diagnose server-connection problems.
# Sets the variables for THIS process only (no machine-wide change).
# To run without the trace: set `$env:ANIMORA_NET_LOG = '0' before launching.
#
# FULL PATIENTS/OWNERS TRACE: with ANIMORA_TRACE_DEBUG=1 below, the SAME window
# also prints the complete end-to-end load path for the "سرپرستان" (owners) and
# "بیماران" (patients) pages: the page ViewModel load, the query service, the
# dispatcher, the LOCAL SQL projection, and the sync pull/apply (including
# whether a synced Owner/Pet row was actually materialised into the local
# tables). Categories to grep: patients, svc.patients, sql.patients, dispatch,
# sync, sync.apply, sync.push, notify, sync.notify.
# Message delivery status (admin-panel messages reaching this PC): look for the
# sync.notify lines - fetch / applied / receipt / skip.
`$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }
`$here = Split-Path -Parent `$MyInvocation.MyCommand.Path
`$exe  = Join-Path `$here 'app\Animora.Desktop.exe'
if (-not (Test-Path `$exe)) { Write-Error "Animora.Desktop.exe not found next to this launcher (`$exe)."; exit 1 }
`$env:ANIMORA_SERVER_URL = '$ServerUrl'
if (-not `$env:ANIMORA_NET_LOG) { `$env:ANIMORA_NET_LOG = '1' }
if (-not `$env:ANIMORA_TRACE_DEBUG) { `$env:ANIMORA_TRACE_DEBUG = '1' }
Write-Host '====================================================================='
Write-Host ' Animora - server-connected launch (LIVE TRACE)'
Write-Host "  Server : `$(`$env:ANIMORA_SERVER_URL)"
Write-Host '  This window shows every request/response to/from the server, plus the'
Write-Host '  app activity log (startup, clicks, navigation, errors) AND the full'
Write-Host '  patients/owners load trace (patients, sql.patients, sync.apply, ...).'
Write-Host '  Message delivery status: look for sync.notify lines - fetch / applied / receipt / skip.'
Write-Host "  App log file: `$env:LOCALAPPDATA\Animora\logs\animora-<date>.log"
Write-Host '  Leave it open while you use the app.'
Write-Host '====================================================================='
Write-Host ''
# Run in the FOREGROUND so the app's console trace streams into THIS window and
# the window does not close while the app is running.
& `$exe
`$code = `$LASTEXITCODE
Write-Host ''
Write-Host '====================================================================='
Write-Host "  Animora has exited (exit code `$code). Full server trace is above."
Write-Host '====================================================================='
Read-Host 'Press Enter to close'
"@
[System.IO.File]::WriteAllText($LauncherPs1, ($Ps1Content -replace "`r?`n", "`r`n"), (New-Object System.Text.UTF8Encoding($false)))

# =============================================================================
# 3. Ship a short README (Persian + English, UTF-8 with BOM so Notepad shows
#    Persian correctly).
# =============================================================================
$Readme = Join-Path $PkgDir 'README-FIRST.txt'
$ReadmeContent = @"
Animora — نسخهٔ متصل به سرور (بدون نیاز به نصب)
===========================================================================

چطور اجرا کنم؟
  فقط روی فایل زیر دابل‌کلیک کنید:
      Run-Animora.cmd

  هیچ نرم‌افزاری لازم نیست نصب شود (نه دات‌نت، نه چیز دیگر).

یک پنجرهٔ مشکی (ترمینال) باز می‌ماند — این طبیعی است:
  این پنجره تمام رفت‌وآمد برنامه با سرور را نشان می‌دهد: هر درخواستی که فرستاده
  می‌شود، هر پاسخی که از سرور برمی‌گردد (کد وضعیت، زمان پاسخ و بدنهٔ پاسخ) و هر
  خطای اتصال. برای عیب‌یابی مشکلات ارتباط با سرور، این پنجره را باز نگه دارید.
  وقتی کارتان تمام شد، برنامه را ببندید و سپس در این پنجره یک کلید بزنید.

  علاوه بر این، این پنجره «مسیر کامل بارگذاری» صفحهٔ سرپرستان/بیماران را هم
  چاپ می‌کند: بارگذاری ViewModel صفحه، سرویس کوئری، دیسپچر، پروجکشن SQL محلی، و
  مرحلهٔ pull/apply همگام‌سازی — از جمله اینکه آیا ردیف سرپرست/بیمارِ گرفته‌شده از
  سرور واقعاً در جدول‌های محلی ساخته شده است یا خیر. برای پیدا کردن این خطوط از
  این دسته‌ها استفاده کنید: patients، svc.patients، sql.patients، dispatch،
  sync، sync.apply، sync.push، notify، sync.notify. همهٔ این‌ها در پنجرهٔ ترمینال و همچنین در فایل
  لاگ برنامه ثبت می‌شوند.
  برای دیدن «وضعیت دریافت پیام‌ها» (پیام‌های پنل ادمین) در ترمینال، دنبال خطوط
  sync.notify بگردید: fetch (دریافت از سرور) / applied (ذخیره در دستگاه) /
  receipt (گزارش دریافت به سرور) / skip (پیام تکراری). لاگ‌ها در فایل زیر هم هستند:

برنامه به کدام سرور وصل می‌شود؟
      $ServerUrl

  برنامه از سرور واقعی استفاده می‌کند (حساب‌ها و داده‌های واقعی کلینیک که روی
  سرور Animora.Server نگهداری می‌شوند). با حساب واقعی خود وارد شوید.

اگر خواستید سرور دیگری را هدف بگیرید:
  فایل Run-Animora.cmd را با Notepad باز کنید و مقدار خط زیر را عوض کنید:
      set "ANIMORA_SERVER_URL=$ServerUrl"

اگر ویندوز هشدار SmartScreen داد:
  روی "More info" و سپس "Run anyway" کلیک کنید (این فایل امضای تجاری ندارد).

عیب‌یابی اتصال:
  * مطمئن شوید سرور روشن است و پورت ۵۰۰۰ در فایروال باز است.
  * از همین کامپیوتر این آدرس را در مرورگر باز کنید تا در دسترس بودن سرور را
    بررسی کنید:  $ServerUrl
  * به پنجرهٔ ترمینال نگاه کنید: خطوط سبز یعنی پاسخ موفق از سرور، زرد/قرمز یعنی
    خطا (مثلاً TIMEOUT، TRANSPORT ERROR، یا کد ۴۰۱/۵۰۰). متن کامل این پنجره را
    می‌توانید برای تیم فنی کپی کنید (توکن‌ها و رمزها به‌صورت خودکار پنهان شده‌اند).
  * برای اجرا بدون نمایش لاگ، در فایل Run-Animora.cmd مقدار را به
    set "ANIMORA_NET_LOG=0" تغییر دهید.

---------------------------------------------------------------------------
Animora — server-connected build (no installation required)

HOW TO RUN
  Just double-click:   Run-Animora.cmd
  Nothing to install (no .NET, no tools).

WHICH SERVER
  The app connects to:  $ServerUrl
  It uses the REAL backend (real accounts and clinic data served by
  Animora.Server). Sign in with your real account.

TO TARGET A DIFFERENT SERVER
  Open Run-Animora.cmd in Notepad and change:
      set "ANIMORA_SERVER_URL=$ServerUrl"

If Windows SmartScreen warns you, click "More info" -> "Run anyway".

A TERMINAL WINDOW STAYS OPEN — THIS IS NORMAL
  It shows the full, step-by-step conversation with the server: every request
  sent, every response (status + time + body) and every connection error. Keep
  it open while using the app to diagnose server-connection problems. When done,
  close the app, then press a key in that window.

  On top of that, the same window prints the COMPLETE end-to-end load path for
  the "سرپرستان" (owners) and "بیماران" (patients) pages: the page ViewModel
  load, the query service, the dispatcher, the LOCAL SQL projection, and the
  sync pull/apply step — including whether a synced Owner/Pet row was actually
  materialised into the local tables. Grep these categories: patients,
  svc.patients, sql.patients, dispatch, sync, sync.apply, sync.push, notify,
  sync.notify. To see message delivery status (admin-panel messages reaching
  this PC) look for the sync.notify lines: fetch / applied / receipt / skip.
  Everything is also written to the app log file:

CONNECTION TROUBLESHOOTING
  * Make sure the server is running and port 5000 is open in the firewall.
  * From this PC, open $ServerUrl in a browser to check reachability.
  * Watch the terminal: GREEN lines are successful responses; YELLOW/RED are
    errors (TIMEOUT, TRANSPORT ERROR, or HTTP 401/500). You can copy the whole
    window text for the support team — tokens and passwords are auto-redacted.
  * To run without the trace, open Run-Animora.cmd and set ANIMORA_NET_LOG=0.
===========================================================================
"@
[System.IO.File]::WriteAllText($Readme, $ReadmeContent, (New-Object System.Text.UTF8Encoding($true)))

# =============================================================================
# 4. Zip it up for easy sharing (unless -NoZip).
# =============================================================================
if (-not $NoZip) {
    $ZipPath = Join-Path $OutputRoot "$PkgName.zip"
    if (Test-Path $ZipPath) { Remove-Item -Force $ZipPath }
    Write-Host "==> Creating zip: $ZipPath"
    Compress-Archive -Path (Join-Path $PkgDir '*') -DestinationPath $ZipPath -Force
}

# =============================================================================
# 5. Summary.
# =============================================================================
$ExeSize = [math]::Round(((Get-Item $ExePath).Length / 1MB), 1)
Write-Host ''
Write-Host '====================================================================='
Write-Host ' DONE — server-connected build ready'
Write-Host '====================================================================='
Write-Host "   Folder : $PkgDir"
if (-not $NoZip) { Write-Host "   Zip    : $(Join-Path $OutputRoot "$PkgName.zip")" }
Write-Host "   Exe    : app\$ExeName ($ExeSize MB, self-contained)"
Write-Host "   Server : $ServerUrl  (baked into Run-Animora.cmd)"
Write-Host "   Trace  : ON — Run-Animora.cmd keeps a terminal open and logs every"
Write-Host "            request/response/error to/from the server (ANIMORA_NET_LOG=1)"
Write-Host "            PLUS the full patients/owners load trace (ANIMORA_TRACE_DEBUG=1):"
Write-Host "            patients, svc.patients, sql.patients, dispatch, sync, sync.apply,"
Write-Host "            sync.push, notify, sync.notify — every step, query, read/return and error on this page."
Write-Host "            Message delivery status: grep sync.notify (fetch/applied/receipt/skip)."
Write-Host ''
Write-Host ' Give the ZIP (or folder) to the clinic PC. They unzip anywhere and'
Write-Host ' double-click "Run-Animora.cmd" — no install needed. The app connects'
Write-Host ' to the real server and signs in with real accounts. A terminal window'
Write-Host ' stays open showing the live server trace AND the full owners/patients'
Write-Host ' load path (for diagnosing why data does not appear).'
Write-Host '====================================================================='
exit 0
