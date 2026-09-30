# =============================================================================
# clean-run.ps1  —  Windows clean rebuild + launch for Animora.Desktop
# =============================================================================
# Fixes the classic Avalonia startup crash:
#
#   Avalonia.Markup.Xaml.XamlLoadException:
#   No precompiled XAML found for Animora.Desktop.App, make sure to specify
#   x:Class and include your XAML file as AvaloniaResource
#
# That error means the Animora.Desktop.dll on disk was produced by a build that
# SKIPPED the Avalonia XAML compiler (a stale bin/obj, an incremental
# up-to-date hit in Visual Studio, or a corrupted obj/ state). The source is
# fine — the fix is a genuinely CLEAN rebuild, which this script forces.
#
# Usage (from a PowerShell prompt at the repo root):
#   .\scripts\windows\clean-run.ps1              # clean + Debug build + run
#   .\scripts\windows\clean-run.ps1 -Release     # clean + Release build + run
#   .\scripts\windows\clean-run.ps1 -NoRun       # clean + build only
#   .\scripts\windows\clean-run.ps1 -LocalTest   # isolated persistent manual test DB
#   .\scripts\windows\clean-run.ps1 -FreshTest   # new isolated DB; never deletes old data
#   .\scripts\windows\clean-run.ps1 -LocalTest -TestProfile sales
#
# Local testing uses the REAL database/services/authentication, with no default password.
# On first run create a Manager account in the setup form (password: >=8 chars, letters+digits).
# The same -TestProfile reopens its data; -FreshTest creates a NEW profile if you forget the login.
# Without these switches the normal clinic database/login is completely unchanged.
# Test profiles start empty (reference catalogs only); create owners/patients/stock via the UI.
# Test storage: %LOCALAPPDATA%\Animora.LocalTests\<profile> (DB, backups, files, logs, DPAPI secrets).
# Close the app before switching profiles. Never put real patient data in a test profile.
# =============================================================================

[CmdletBinding()]
param(
    [switch]$Release,
    [switch]$NoRun,
    [switch]$LocalTest,
    [switch]$FreshTest,
    [ValidatePattern('^[a-z0-9_-]{1,40}$')]
    [string]$TestProfile = 'manual'
)

$ErrorActionPreference = 'Stop'

if ($LocalTest -and $FreshTest) {
    throw 'Choose -LocalTest or -FreshTest, not both.'
}
if ($PSBoundParameters.ContainsKey('TestProfile') -and (-not $LocalTest -or $FreshTest)) {
    throw '-TestProfile requires -LocalTest and cannot be used with -FreshTest.'
}
$AppArgs = @()
if ($FreshTest) {
    # Generate the name here so the user can reopen the exact same profile later.
    $TestProfile = 'fresh-' + [Guid]::NewGuid().ToString('N')
    $AppArgs = @("--local-test=$TestProfile")
} elseif ($LocalTest) {
    $AppArgs = @("--local-test=$TestProfile")
}
if ($LocalTest -or $FreshTest) {
    Write-Host "==> ISOLATED LOCAL TEST: $TestProfile"
    Write-Host "==> Reopen with: .\scripts\windows\clean-run.ps1 -LocalTest -TestProfile $TestProfile"
    Write-Host '==> First run: create your own Manager account. No normal clinic data is used.'
}

# Resolve repo root (two levels up from this script: scripts\windows\ -> repo).
$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$Config   = if ($Release) { 'Release' } else { 'Debug' }
$Proj     = Join-Path $RepoRoot 'src\Animora.Desktop\Animora.Desktop.csproj'
$Dll      = Join-Path $RepoRoot "src\Animora.Desktop\bin\$Config\net10.0\Animora.Desktop.dll"

Write-Host "==> Repo:   $RepoRoot"
Write-Host "==> Config: $Config"

# 1. Nuke every bin/ and obj/ under src/ — this is what makes the rebuild honest.
Write-Host "==> Deleting all bin/ and obj/ folders under src/ ..."
Get-ChildItem -Path (Join-Path $RepoRoot 'src') -Recurse -Directory -Force `
    -Include 'bin', 'obj' -ErrorAction SilentlyContinue |
    ForEach-Object {
        Write-Host "    - $($_.FullName)"
        Remove-Item -Recurse -Force $_.FullName -ErrorAction SilentlyContinue
    }

# 2. Full clean rebuild. Force the Avalonia XAML compiler on explicitly so a
#    machine-level MSBuild property can never quietly disable it.
Write-Host "==> Building ($Config) ..."
dotnet build $Proj -c $Config `
    -p:EnableAvaloniaXamlCompilation=true `
    --no-incremental
if ($LASTEXITCODE -ne 0) {
    Write-Error "Build FAILED (exit $LASTEXITCODE). The build's own verification " +
        "target will report if compiled XAML is missing — read the error above."
    exit $LASTEXITCODE
}

# 3. Run the app (unless -NoRun).
if ($NoRun) {
    Write-Host "==> Build OK. Skipping run (-NoRun). Output: $Dll"
    exit 0
}

if (-not (Test-Path $Dll)) {
    Write-Error "Expected output not found: $Dll"
    exit 1
}

Write-Host "==> Launching Animora.Desktop ..."
dotnet $Dll @AppArgs
exit $LASTEXITCODE
