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
# 1. Publish: Release, self-contained, single-file.
#    - self-contained  => bundles the .NET runtime (no install on target PC)
#    - single-file      => one Animora.Desktop.exe (plus required native libs)
#    - EnableAvaloniaXamlCompilation=true => avoids the "No precompiled XAML"
#      startup crash.
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
rem  Animora — launcher (connects to the real server)
rem ---------------------------------------------------------------------------
rem  No installation needed. Just double-click this file.
rem
rem  The app connects to the Animora server below and uses the REAL backend
rem  (real accounts and clinic data served by Animora.Server).
rem
rem  To point this copy at a different server, edit the SET line below.
rem ===========================================================================
setlocal
set "ANIMORA_SERVER_URL=$ServerUrl"
cd /d "%~dp0app"
start "" "Animora.Desktop.exe"
endlocal
"@
# Write as UTF-8 WITHOUT BOM so cmd.exe does not choke on a leading BOM.
[System.IO.File]::WriteAllText($LauncherCmd, ($CmdContent -replace "`r?`n", "`r`n"), (New-Object System.Text.UTF8Encoding($false)))

$LauncherPs1 = Join-Path $PkgDir 'Run-Animora.ps1'
$Ps1Content = @"
# Animora — launcher (connects to the real server). No installation required.
# Sets ANIMORA_SERVER_URL for this process only, then starts the app online.
`$ErrorActionPreference = 'Stop'
`$here = Split-Path -Parent `$MyInvocation.MyCommand.Path
`$exe  = Join-Path `$here 'app\Animora.Desktop.exe'
if (-not (Test-Path `$exe)) { Write-Error "Animora.Desktop.exe not found next to this launcher (`$exe)."; exit 1 }
`$env:ANIMORA_SERVER_URL = '$ServerUrl'
& `$exe
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

CONNECTION TROUBLESHOOTING
  * Make sure the server is running and port 5000 is open in the firewall.
  * From this PC, open $ServerUrl in a browser to check reachability.
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
Write-Host ''
Write-Host ' Give the ZIP (or folder) to the clinic PC. They unzip anywhere and'
Write-Host ' double-click "Run-Animora.cmd" — no install needed. The app connects'
Write-Host ' to the real server and signs in with real accounts.'
Write-Host '====================================================================='
exit 0
