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
#   .\scripts\windows\clean-run.ps1              # Debug + persistent isolated test DB
#   .\scripts\windows\clean-run.ps1 -Release     # Release + normal clinic DB
#   .\scripts\windows\clean-run.ps1 -NoRun       # clean + build only
#   .\scripts\windows\clean-run.ps1 -Clinic      # Debug + normal clinic DB
#   .\scripts\windows\clean-run.ps1 -LocalTest   # isolated persistent manual test DB
#   .\scripts\windows\clean-run.ps1 -FreshTest   # new isolated DB; never deletes old data
#   .\scripts\windows\clean-run.ps1 -LocalTest -TestProfile sales
#   .\scripts\windows\clean-run.ps1 -ClinicBak  # legacy data preview, automatic real sign-in
# -ClinicBak requires Python 3 (stdlib only). It reads Clinic.bak without restoring/modifying it.
# Private output: .sandbox\clinic-bak\clinic.json, clinic-archive.json, clinic-report.json.
# Uses a dedicated content-addressed profile under %LOCALAPPDATA%\Animora.LocalTests.
# Repeated runs reuse that profile; changed preview data creates a NEW profile (no reset/delete).
# Legacy financial/clinical records are historical notes, not new postings/doses/appointments.
# Never distribute these private JSON files or the preview profile: they contain patient contacts.
#
# Local testing uses the REAL database/services/authentication, with no default password.
# On first run create a Manager account in the setup form (password: >=8 chars, letters+digits).
# The same -TestProfile reopens its data; -FreshTest creates a NEW profile if you forget the login.
# Debug defaults to the persistent manual profile; -Clinic or plain -Release uses the clinic DB.
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
    [switch]$Clinic,
    [switch]$ClinicBak,
    [ValidatePattern('^[a-z0-9_-]{1,40}$')]
    [string]$TestProfile = 'manual'
)

$ErrorActionPreference = 'Stop'

if ($ClinicBak -and ($Clinic -or $LocalTest -or $FreshTest -or $PSBoundParameters.ContainsKey('TestProfile'))) {
    throw '-ClinicBak cannot be combined with other profile options.'
}
if ($Clinic -and ($LocalTest -or $FreshTest -or $PSBoundParameters.ContainsKey('TestProfile'))) {
    throw '-Clinic cannot be combined with local-test options.'
}
if (-not $Release -and -not $Clinic -and -not $FreshTest -and -not $ClinicBak) {
    $LocalTest = $true
}
if ($LocalTest -and $FreshTest) {
    throw 'Choose -LocalTest or -FreshTest, not both.'
}
if ($PSBoundParameters.ContainsKey('TestProfile') -and (-not $LocalTest -or $FreshTest)) {
    throw '-TestProfile requires -LocalTest and cannot be used with -FreshTest.'
}
$AppArgs = @()
if ($ClinicBak) {
    # Extraction is performed below, before any clean/build, using an absolute path.
} elseif ($FreshTest) {
    # Generate the name here so the user can reopen the exact same profile later.
    $TestProfile = 'fresh-' + [Guid]::NewGuid().ToString('N')
    $AppArgs = @("--local-test=$TestProfile")
} elseif ($LocalTest) {
    $AppArgs = @("--local-test=$TestProfile")
} else {
    # Explicit even in Debug: do not redirect a clinic launch to test storage.
    $AppArgs = @('--clinic')
}
if ($LocalTest -or $FreshTest) {
    Write-Host "==> ISOLATED LOCAL TEST: $TestProfile"
    Write-Host "==> Reopen with: .\scripts\windows\clean-run.ps1 -LocalTest -TestProfile $TestProfile"
    Write-Host '==> First run: create your own Manager account. No normal clinic data is used.'
}

# Resolve repo root (two levels up from this script: scripts\windows\ -> repo).
$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
if ($ClinicBak) {
    $Backup = Join-Path $RepoRoot 'db-sample\Clinic.bak'
    $Preview = Join-Path $RepoRoot '.sandbox\clinic-bak\clinic.json'
    $Extractor = Join-Path $RepoRoot 'scripts\extract-clinic-bak.py'
    if (-not (Test-Path $Backup -PathType Leaf)) { throw "Backup not found: $Backup" }
    $Python = Get-Command py -ErrorAction SilentlyContinue
    $PythonArgs = @('-3')
    if (-not $Python) {
        $Python = Get-Command python -ErrorAction SilentlyContinue
        $PythonArgs = @()
    }
    if (-not $Python) { throw 'ClinicBak preview requires Python 3. Install Python, then rerun this same command.' }
    & $Python.Source @PythonArgs $Extractor $Backup $Preview
    if ($LASTEXITCODE -ne 0) { throw 'Clinic.bak extraction failed. No application storage was opened.' }
    $AppArgs = @("--clinic-bak=$Preview")
    Write-Host '==> LEGACY PREVIEW ONLY: dedicated isolated database; automatic Manager sign-in.'
    Write-Host '==> No normal clinic data or other profiles are reset or modified.'
    Write-Host "==> Mapping/exclusions: $(Join-Path $RepoRoot '.sandbox\clinic-bak\clinic-report.json')"
}
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
