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
#   .\scripts\windows\clean-run.ps1 -SeedCount 100  # NEW random-data test profile
#   .\scripts\windows\clean-run.ps1 5000           # same as -SeedCount 5000
#   .\scripts\windows\clean-run.ps1 55             # small profile
#   .\scripts\windows\clean-run.ps1 9999           # large profile
# From scripts\windows: .\clean-run.ps1 -FreshTest -SeedCount 100
# SeedCount accepts ANY number you type (e.g. 55, 9999, or a huge value); it is sanitized to digits and
# CLAMPED into 1..50000 additional linked scenarios + 32 baseline scenarios (a scenario count, not a row count).
# After seeding, the terminal prints the sign-in username; the password is the one you typed while seeding.
# Populates patients, owners, clinical records, vaccination, appointments, boarding,
# inventory, purchasing, invoices, payments, cashier, cheques, expenses, finance/history projections.
# Catalogue/staff/room counts are bounded; each scenario produces multiple linked records.
# Random mode asks for your own Manager username/password in the terminal; sign in normally in the app.
# Requires Windows + .NET 10 SDK only; no Python or external database tools.
# Always creates a NEW profile, even in Release. Cannot combine with Clinic/ClinicBak/TestProfile.
# Existing profiles, real clinic storage and application settings are never seeded/reset/overwritten.
# With -NoRun, builds only: no Manager prompt, database creation or random-data generation.
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
# Without -SeedCount, test profiles start empty (reference catalogs only); create data via the UI.
# Test storage: %LOCALAPPDATA%\Animora.LocalTests\<profile> (DB, backups, files, logs, DPAPI secrets).
# Close the app before switching profiles. Never put real patient data in a test profile.
# =============================================================================

[CmdletBinding(PositionalBinding = $false)]
param(
    [Parameter(Position = 0)]
    [Alias('RandomCount')]
    # Accept ANY number the user types (e.g. 55, 9999, or a huge value like 9636 3624928364).
    # Kept as a raw string so an out-of-int / out-of-range value never throws before we can clamp it;
    # the value is sanitized to digits and clamped into 1..50000 below. Only digits/spaces/_/, allowed.
    [ValidatePattern('^[0-9][0-9 _,]*$')]
    [string]$SeedCount,
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
# Explicit exit-code handling works in Windows PowerShell 5.1 and PowerShell 7.
$PSNativeCommandUseErrorActionPreference = $false
$SeedRequested = $PSBoundParameters.ContainsKey('SeedCount')
if ($SeedRequested) {
    if ($Clinic -or $ClinicBak -or $PSBoundParameters.ContainsKey('TestProfile')) {
        throw '-SeedCount always creates a NEW isolated profile; cannot combine with -Clinic, -ClinicBak or -TestProfile.'
    }
    if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
        throw '-SeedCount requires an interactive Windows PowerShell terminal and Windows DPAPI.'
    }
    # Sanitize to digits only, then clamp into the safe supported range instead of failing.
    # This lets ANY number be entered (55, 9999, 9636362492836...) and mapped to 1..50000.
    $SeedDigits = ($SeedCount -replace '[^0-9]', '')
    if ([string]::IsNullOrEmpty($SeedDigits)) {
        throw 'SeedCount must contain at least one digit, e.g. 55, 250 or 9999.'
    }
    $SeedRequestedBig = [bigint]::Parse($SeedDigits)
    $SeedMin = [bigint]1
    $SeedMax = [bigint]50000
    $SeedClamped = [bigint]::Min([bigint]::Max($SeedRequestedBig, $SeedMin), $SeedMax)
    $SeedCountValue = [int]$SeedClamped
    if ($SeedRequestedBig -ne $SeedClamped) {
        Write-Host "==> Requested $SeedRequestedBig scenarios; clamped to supported range 1..50000 -> $SeedCountValue."
    }
    $LocalTest = $false
    $FreshTest = $true
}

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
    $Prefix = if ($SeedRequested) { 'random-' } else { 'fresh-' }
    $TestProfile = $Prefix + [Guid]::NewGuid().ToString('N')
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
    if ($SeedRequested) {
        Write-Host "==> RANDOM DATA: $SeedCountValue additional scenarios + 32 baseline scenarios; never modifies an existing profile."
        Write-Host '==> After build: create your test Manager in this terminal, then sign in normally in the app.'
    } else {
        Write-Host '==> First run: create your own Manager account. No normal clinic data is used.'
    }
}

# Resolve repo root (two levels up from this script: scripts\windows\ -> repo).
$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
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
$BuildExitCode = $LASTEXITCODE
if ($BuildExitCode -ne 0) {
    # Parentheses keep '+' inside the message expression (not a positional Write-Error argument).
    # Continue prints the message without masking the native build failure with a terminating exception.
    Write-Error -Message ("Build FAILED (exit $BuildExitCode). The build's own verification " +
        "target will report if compiled XAML is missing — read the error above.") -ErrorAction Continue
    exit $BuildExitCode
}

# 3. Run the app (unless -NoRun).
if ($NoRun) {
    Write-Host "==> Build OK. Skipping run (-NoRun). Output: $Dll"
    exit 0
}

if (-not (Test-Path $Dll)) {
    Write-Error -Message "Expected output not found: $Dll" -ErrorAction Continue
    exit 1
}

if ($SeedRequested) {
    $SeedProject = Join-Path $PSScriptRoot 'test-data\Animora.TestData.csproj'
    $SeedDll = Join-Path $PSScriptRoot "test-data\bin\$Config\net10.0\Animora.TestData.dll"
    Write-Host '==> Building standalone manual-test data tool (not included in the application) ...'
    dotnet build $SeedProject -c $Config --no-incremental
    $SeedBuildExitCode = $LASTEXITCODE
    if ($SeedBuildExitCode -ne 0) {
        Write-Error -Message "Test-data tool build FAILED (exit $SeedBuildExitCode). App was not launched." -ErrorAction Continue
        exit $SeedBuildExitCode
    }
    Write-Host '==> Generating data in the NEW isolated profile only ...'
    dotnet $SeedDll $TestProfile $SeedCountValue
    $SeedExitCode = $LASTEXITCODE
    if ($SeedExitCode -ne 0) {
        Write-Error -Message 'Random-data generation failed. App was not launched; any partial test data was kept.' -ErrorAction Continue
        exit $SeedExitCode
    }
    $ProfileDirectory = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) "Animora.LocalTests\$TestProfile"
    $CompleteMarker = Join-Path $ProfileDirectory 'random-data-complete.json'
    if (-not (Test-Path $CompleteMarker -PathType Leaf)) {
        Write-Error -Message 'Missing completion marker; refusing to launch an incomplete random-data profile.' -ErrorAction Continue
        exit 1
    }
    # Surface the sign-in username again right before launch, after all the build/seed output has scrolled by,
    # so it is easy to read when the login window appears. The password is the one you typed during seeding.
    try {
        $SeedInfo = Get-Content -Raw -LiteralPath $CompleteMarker | ConvertFrom-Json
    } catch { $SeedInfo = $null }
    Write-Host ''
    Write-Host '====================================================================='
    Write-Host ' TEST LOGIN (enter these in the app window that is about to open):'
    if ($SeedInfo -and $SeedInfo.Username) {
        Write-Host "   Username : $($SeedInfo.Username)"
    } else {
        Write-Host '   Username : the one you typed during seeding (default: manager)'
    }
    Write-Host '   Password : the password you typed during seeding (chosen by you).'
    Write-Host '====================================================================='
    Write-Host ''
}

Write-Host "==> Launching Animora.Desktop ..."
dotnet $Dll @AppArgs
exit $LASTEXITCODE
