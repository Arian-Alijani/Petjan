# =============================================================================
# publish-portable.ps1  —  Build a ZERO-INSTALL portable Animora.Desktop
# =============================================================================
# PURPOSE
#   Produce a fully self-contained, single-file Windows build of Animora.Desktop
#   that a friend can run on ANY Windows PC WITHOUT installing anything — no
#   .NET SDK/runtime, no Python, no database tools. Everything (the .NET runtime,
#   the Avalonia UI stack, and the native SQLCipher binaries) is bundled into the
#   portable output folder.
#
#   The portable build is wired so that when your friend double-clicks the
#   included launcher, the app behaves EXACTLY like:
#
#       .\scripts\windows\clean-run.ps1 -FreshTest
#
#   i.e. it opens a BRAND-NEW isolated test profile every run, so they land on
#   the first-run setup screen and create a Manager account from scratch, then
#   sign in and test the whole app. No real clinic data is touched.
#
# WHY A FRESH PROFILE EACH RUN
#   `-FreshTest` makes clean-run.ps1 generate `--local-test=fresh-<guid>` and the
#   app routes ALL storage into %LOCALAPPDATA%\Animora.LocalTests\fresh-<guid>.
#   The launcher produced here passes the app's own `--local-test-fresh` flag,
#   which the app (LocalTestProfile.Parse) turns into a new `fresh-<guid>`
#   profile on EVERY launch — the same fresh-start experience, in Release.
#
# =============================================================================
# USAGE (run on a Windows machine that HAS the .NET 10 SDK — i.e. YOUR machine,
#        not your friend's):
#
#   .\scripts\windows\publish-portable.ps1
#       Builds the portable app for win-x64 and zips it.
#
#   .\scripts\windows\publish-portable.ps1 -Runtime win-arm64
#       Build for ARM64 Windows instead of x64.
#
#   .\scripts\windows\publish-portable.ps1 -NoZip
#       Produce only the portable folder, skip creating the .zip.
#
#   .\scripts\windows\publish-portable.ps1 -OutputRoot D:\share
#       Choose where the portable folder/zip are written.
#
#   .\scripts\windows\publish-portable.ps1 -EmitManifest
#       Also write the SBOM + native-binary inventory (scripts/release/make-release-manifest.sh)
#       into <portable>\manifest\ so they ship WITH the package (G09).
#
# THEN
#   Give your friend the produced .zip (or the folder). They unzip it anywhere
#   and double-click "Run-Animora (Fresh Test).cmd". Nothing to install.
#
# =============================================================================
# SAFETY NOTES (important)
#   * This script ONLY publishes into an isolated output folder (default:
#     <repo>\publish\...). It NEVER edits application source, and does NOT touch
#     the src\**\bin or src\**\obj used by your normal dev builds — so it cannot
#     break your existing build or app in any way.
#   * It does NOT create, seed, delete, or open any clinic/test database itself.
#     Every account/data is created fresh by your friend at run time.
#   * Running the produced app uses the app's real authentication/setup flow.
# =============================================================================

[CmdletBinding()]
param(
    # Only the officially supported desktop RIDs (STACK.md native matrix).
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime = 'win-x64',

    # Where to place the portable folder + zip. Defaults to <repo>\publish.
    [string]$OutputRoot,

    # Skip zipping (leave just the folder).
    [switch]$NoZip,

    # Overwrite an existing portable folder for this runtime without prompting.
    [switch]$Force,

    # After publishing, generate the release manifest (CycloneDX SBOM + native-binary inventory +
    # suppression-ledger check) with scripts/release/make-release-manifest.sh and ship it INSIDE the
    # portable package (<portable>\manifest\). Needs bash (Git for Windows / WSL) on the build machine.
    [switch]$EmitManifest
)

$ErrorActionPreference = 'Stop'
# Make native command exit codes behave the same on Windows PowerShell 5.1 and 7.
$PSNativeCommandUseErrorActionPreference = $false

# -- Must run on Windows: self-contained win-* publish + native SQLCipher need it.
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    throw 'publish-portable.ps1 must be run on Windows (it produces a Windows build). Run it on your own PC that has the .NET 10 SDK.'
}

# -- Require the .NET SDK on the BUILD machine (not on your friend's machine).
$Dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $Dotnet) {
    throw 'The .NET 10 SDK is required on THIS machine to build the portable app. Install it from https://dotnet.microsoft.com/download then rerun. (Your friend will NOT need it.)'
}

# -- Resolve paths (scripts\windows\ -> repo root).
$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$Proj     = Join-Path $RepoRoot 'src\Animora.Desktop\Animora.Desktop.csproj'
if (-not (Test-Path $Proj -PathType Leaf)) { throw "Cannot find desktop project: $Proj" }

if (-not $OutputRoot) { $OutputRoot = Join-Path $RepoRoot 'publish' }
$Stamp       = Get-Date -Format 'yyyy-MM-dd'
$PortableName = "Animora-Portable-$Runtime-$Stamp"
$PortableDir = Join-Path $OutputRoot $PortableName
$AppSubDir   = Join-Path $PortableDir 'app'   # the published app lives here; launcher sits beside it

Write-Host '====================================================================='
Write-Host ' Animora — portable (zero-install) publish'
Write-Host '====================================================================='
Write-Host "==> Repo:     $RepoRoot"
Write-Host "==> Runtime:  $Runtime"
Write-Host "==> Output:   $PortableDir"
Write-Host "==> Behavior: every launch starts a FRESH test profile (== clean-run.ps1 -FreshTest)"
Write-Host ''

# -- Prepare a clean output folder (only ever under the isolated publish dir).
if (Test-Path $PortableDir) {
    if (-not $Force) {
        Write-Host "==> Output folder already exists: $PortableDir"
        $answer = Read-Host '    Delete and recreate it? (y/N)'
        if ($answer -notmatch '^(y|yes)$') { throw 'Aborted: output folder already exists. Use -Force to overwrite, or pass a different -OutputRoot.' }
    }
    Remove-Item -Recurse -Force $PortableDir
}
New-Item -ItemType Directory -Force -Path $AppSubDir | Out-Null

# =============================================================================
# 1. Publish: Release, self-contained, single-file.
#    - self-contained  => bundles the .NET runtime (no install on target PC)
#    - single-file      => one Animora.Desktop.exe (plus required native libs)
#    - EnableAvaloniaXamlCompilation=true => avoids the "No precompiled XAML"
#      startup crash (same guard clean-run.ps1 uses).
#    Native SQLCipher/e_sqlite3 binaries are copied next to the exe, not embedded,
#    so SQLite loads reliably — that is why IncludeNativeLibrariesForSelfExtract
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
    throw "dotnet publish FAILED (exit $PublishExit). The portable build was NOT created. Your source and existing builds are untouched."
}

$ExeName = 'Animora.Desktop.exe'
$ExePath = Join-Path $AppSubDir $ExeName
if (-not (Test-Path $ExePath -PathType Leaf)) {
    throw "Publish finished but '$ExeName' was not found in $AppSubDir. Nothing was shipped."
}

# =============================================================================
# 2. Create the end-user launchers (double-click = fresh-test experience).
#    We ship BOTH a .cmd (most reliable double-click) and a .ps1 helper.
#    The '--local-test-fresh' flag is what makes the app open a brand-new
#    isolated profile every run (first-run setup -> create Manager -> sign in),
#    exactly like clean-run.ps1 -FreshTest.
# =============================================================================
Write-Host '==> Writing end-user launchers ...'

$LauncherCmd = Join-Path $PortableDir 'Run-Animora (Fresh Test).cmd'
$CmdContent = @'
@echo off
rem ===========================================================================
rem  Animora — portable launcher (FRESH TEST mode)
rem ---------------------------------------------------------------------------
rem  No installation needed. Just double-click this file.
rem
rem  Every time you run this, Animora starts a BRAND-NEW empty test profile:
rem  you will see the first-run setup screen, create a Manager account, then
rem  sign in and try the app. Your real/clinic data is never touched.
rem
rem  (This is the same behavior as:  clean-run.ps1 -FreshTest )
rem ===========================================================================
setlocal
cd /d "%~dp0app"
start "" "Animora.Desktop.exe" --local-test-fresh
endlocal
'@
# Write as UTF-8 WITHOUT BOM so cmd.exe does not choke on a leading BOM.
[System.IO.File]::WriteAllText($LauncherCmd, ($CmdContent -replace "`r?`n", "`r`n"), (New-Object System.Text.UTF8Encoding($false)))

$LauncherPs1 = Join-Path $PortableDir 'Run-Animora (Fresh Test).ps1'
$Ps1Content = @'
# Animora — portable launcher (FRESH TEST mode). No installation required.
# Starts a brand-new isolated test profile every run (create Manager -> sign in).
# Equivalent to: clean-run.ps1 -FreshTest
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$exe  = Join-Path $here 'app\Animora.Desktop.exe'
if (-not (Test-Path $exe)) { Write-Error "Animora.Desktop.exe not found next to this launcher ($exe)."; exit 1 }
& $exe --local-test-fresh
'@
[System.IO.File]::WriteAllText($LauncherPs1, ($Ps1Content -replace "`r?`n", "`r`n"), (New-Object System.Text.UTF8Encoding($false)))

# =============================================================================
# 3. Ship a short README for your friend (Persian + English, UTF-8 with BOM so
#    Notepad shows Persian correctly).
# =============================================================================
$Readme = Join-Path $PortableDir 'README-FIRST.txt'
$ReadmeContent = @"
Animora — نسخه قابل‌حمل (بدون نیاز به نصب)
===========================================================================

چطور اجرا کنم؟
  فقط روی فایل زیر دابل‌کلیک کنید:
      Run-Animora (Fresh Test).cmd

  هیچ نرم‌افزاری لازم نیست نصب شود (نه دات‌نت، نه چیز دیگر).

چه اتفاقی می‌افتد؟
  هر بار که اجرا می‌کنید، برنامه یک پروفایل تستی کاملاً تازه و خالی می‌سازد:
    ۱) ابتدا صفحهٔ راه‌اندازی اولیه نمایش داده می‌شود.
    ۲) یک حساب «مدیر» (Manager) بسازید (رمز حداقل ۸ کاراکتر، شامل حرف و عدد).
    ۳) سپس با همان حساب وارد شوید و برنامه را آزمایش کنید.

  هیچ دادهٔ واقعی کلینیک استفاده یا تغییر داده نمی‌شود — همه‌چیز تستی است.
  داده‌های تست در این مسیر ذخیره می‌شوند:
      %LOCALAPPDATA%\Animora.LocalTests\

اگر ویندوز هشدار SmartScreen داد:
  روی "More info" و سپس "Run anyway" کلیک کنید (این فایل امضای تجاری ندارد).

---------------------------------------------------------------------------
Animora — portable build (no installation required)

HOW TO RUN
  Just double-click:   Run-Animora (Fresh Test).cmd
  Nothing to install (no .NET, no tools).

WHAT HAPPENS
  Each run creates a brand-new, empty TEST profile:
    1) You see the first-run setup screen.
    2) Create a Manager account (password: 8+ chars, letters + digits).
    3) Sign in with it and try the app.
  No real clinic data is used or changed. Test data lives under
      %LOCALAPPDATA%\Animora.LocalTests\

If Windows SmartScreen warns you, click "More info" -> "Run anyway".
===========================================================================
"@
[System.IO.File]::WriteAllText($Readme, $ReadmeContent, (New-Object System.Text.UTF8Encoding($true)))

# =============================================================================
# 3b. (-EmitManifest) SBOM + native-binary inventory + suppression-ledger check, shipped WITH the
#     package. Done BEFORE zipping so the manifest is inside the zip. Reuses the bash generator
#     (single source of truth); a violation (e.g. native-DB suppression without expiry) FAILS the publish.
# =============================================================================
if ($EmitManifest) {
    $Bash = Get-Command bash -ErrorAction SilentlyContinue
    if (-not $Bash) { throw '-EmitManifest needs bash on PATH (Git for Windows or WSL) to run scripts/release/make-release-manifest.sh.' }
    $ManifestScript = Join-Path $RepoRoot 'scripts\release\make-release-manifest.sh'
    if (-not (Test-Path $ManifestScript -PathType Leaf)) { throw "Cannot find manifest generator: $ManifestScript" }
    $ManifestDir = Join-Path $PortableDir 'manifest'
    New-Item -ItemType Directory -Force -Path $ManifestDir | Out-Null
    Write-Host '==> Generating release manifest (SBOM + native inventory) ...'
    # Forward slashes: bash understands them on Git-for-Windows and WSL (via the path as given by Resolve-Path).
    $toBash = { param($p) ($p -replace '\\', '/') }
    Push-Location $RepoRoot
    try {
        & $Bash.Source 'scripts/release/make-release-manifest.sh' `
            --output-dir (& $toBash $AppSubDir) `
            --project 'src/Animora.Desktop/Animora.Desktop.csproj' `
            --rid $Runtime `
            --out (& $toBash $ManifestDir)
        $ManifestExit = $LASTEXITCODE
    } finally { Pop-Location }
    if ($ManifestExit -ne 0) {
        throw "Release manifest generation FAILED (exit $ManifestExit). The package was NOT finalized."
    }
    Write-Host "==> Manifest written to: $ManifestDir"
}

# =============================================================================
# 4. Zip it up for easy sharing (unless -NoZip).
# =============================================================================
if (-not $NoZip) {
    $ZipPath = Join-Path $OutputRoot "$PortableName.zip"
    if (Test-Path $ZipPath) { Remove-Item -Force $ZipPath }
    Write-Host "==> Creating zip: $ZipPath"
    Compress-Archive -Path (Join-Path $PortableDir '*') -DestinationPath $ZipPath -Force
}

# =============================================================================
# 5. Summary.
# =============================================================================
$ExeSize = [math]::Round(((Get-Item $ExePath).Length / 1MB), 1)
Write-Host ''
Write-Host '====================================================================='
Write-Host ' DONE — portable (zero-install) build ready'
Write-Host '====================================================================='
Write-Host "   Folder : $PortableDir"
if (-not $NoZip) { Write-Host "   Zip    : $(Join-Path $OutputRoot "$PortableName.zip")" }
if ($EmitManifest) { Write-Host "   SBOM   : manifest\sbom.cdx.json + manifest\native-inventory.json (shipped in the package)" }
Write-Host "   Exe    : app\$ExeName ($ExeSize MB, self-contained)"
Write-Host ''
Write-Host ' Give your friend the ZIP (or the folder). They unzip anywhere and'
Write-Host ' double-click "Run-Animora (Fresh Test).cmd" — no install needed.'
Write-Host ' Every launch starts fresh at the setup screen (== clean-run.ps1 -FreshTest).'
Write-Host '====================================================================='
exit 0
