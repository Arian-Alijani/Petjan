<#
.SYNOPSIS
  اجرای آسان LiveTest آنیمورا از روی ویندوز، دقیقاً با همان کاربری که روی سرور ساخته‌ای.

.DESCRIPTION
  این اسکریپت کل کار را برایت انجام می‌دهد:
    • اعتبار ورود (کاربر/رمز/کد مجموعه) را به‌صورت متغیر محیطی به LiveTest می‌دهد.
    • اگر گزینه‌ای ندهی، یک منوی ساده نشان می‌دهد تا تست موردنظرت را انتخاب کنی.
    • خودِ LiveTest را با dotnet اجرا می‌کند و در پایان مسیر گزارش‌ها را نشان می‌دهد.

  هیچ تنظیم دستی دیگری لازم نیست؛ مقادیر پیش‌فرض دقیقاً با خروجی setup-livetest.sh
  روی سرور هماهنگ‌اند. فقط رمز را بده (یا بگذار از تو بپرسد).

.PARAMETER User
  نام کاربری مدیر که روی سرور ساخته شد (پیش‌فرض: manager).

.PARAMETER Password
  رمز همان کاربر. اگر ندهی، امن (مخفی) از تو پرسیده می‌شود.

.PARAMETER Tenant
  کد مجموعه/Tenant (پیش‌فرض: ANIMORA-LOADTEST).

.PARAMETER Server
  آدرس سرور مرکزی (پیش‌فرض: http://188.212.99.234:5000).

.PARAMETER Mode
  یکی از: preflight | correctness | capacity | all
  اگر ندهی، منوی انتخابی نشان داده می‌شود.

.PARAMETER Feature
  فقط برای correctness: فقط سناریوهایی که نامشان شامل این متن است اجرا شوند
  (مثل Patients, Sales, Inventory, Finance, Boarding, Scheduling, Protocol).

.PARAMETER KeepGoing
  فقط برای correctness: بعد از اولین خطا هم ادامه بده و همه را گزارش کن.

.EXAMPLE
  .\run-livetest.ps1 -User manager -Password 'Test-Pass-1234'

.EXAMPLE
  .\run-livetest.ps1 -Mode correctness -Feature Sales -KeepGoing

.EXAMPLE
  # اجرای کامل بدون هیچ آرگومان — منو را نشان می‌دهد و رمز را می‌پرسد:
  .\run-livetest.ps1
#>
[CmdletBinding()]
param(
    [string]   $User     = "manager",
    [string]   $Password = "",
    [string]   $Tenant   = "ANIMORA-LOADTEST",
    [string]   $Server   = "http://188.212.99.234:5000",
    [ValidateSet("preflight", "correctness", "capacity", "all")]
    [string]   $Mode     = "",
    [string]   $Feature  = "",
    [switch]   $KeepGoing
)

$ErrorActionPreference = "Stop"

function Write-Info($m) { Write-Host "• $m" -ForegroundColor Yellow }
function Write-Ok($m)   { Write-Host "✓ $m" -ForegroundColor Green }
function Write-Err($m)  { Write-Host "✗ $m" -ForegroundColor Red }

# ── مسیر ریشهٔ ریپو و پروژهٔ LiveTest ─────────────────────────────────────────
$ScriptDir   = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot    = (Resolve-Path (Join-Path $ScriptDir "..\..")).Path
$ProjectPath = Join-Path $RepoRoot "tests\Animora.LiveTest\Animora.LiveTest.csproj"

if (-not (Test-Path $ProjectPath)) {
    Write-Err "پروژهٔ LiveTest پیدا نشد: $ProjectPath"
    Write-Err "این اسکریپت را از داخل همان ریپوی آنیمورا اجرا کن."
    exit 1
}

# ── بررسی .NET SDK ─────────────────────────────────────────────────────────────
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) {
    Write-Err ".NET SDK روی این ویندوز نصب نیست."
    Write-Info "نصبش کن: https://dot.net  (نسخهٔ .NET 10 SDK)"
    Write-Info "بعد از نصب، یک‌بار پنجرهٔ PowerShell را ببند و دوباره باز کن."
    exit 69
}

# ── انتخاب حالت (اگر داده نشده) ────────────────────────────────────────────────
if ([string]::IsNullOrWhiteSpace($Mode)) {
    Write-Host ""
    Write-Host "کدام تست را می‌خواهی بگیری؟" -ForegroundColor Cyan
    Write-Host "  1) preflight    — فقط بررسی سلامت اتصال و ورود (سریع، بی‌خطر)"
    Write-Host "  2) correctness  — تست درستیِ همهٔ قابلیت‌ها + هماهنگی دو دستگاه (۳۶ سناریو)"
    Write-Host "  3) capacity     — تست ظرفیت/بار سرور"
    Write-Host "  4) all          — اول correctness بعد (در صورت موفقیت) capacity"
    Write-Host ""
    $sel = Read-Host "شمارهٔ گزینه (پیش‌فرض 2)"
    switch ($sel.Trim()) {
        "1"     { $Mode = "preflight" }
        "3"     { $Mode = "capacity" }
        "4"     { $Mode = "all" }
        default { $Mode = "correctness" }
    }
}

# ── برای correctness: انتخاب فیلتر قابلیت (اگر داده نشده و تعاملی هستیم) ─────────
if ($Mode -eq "correctness" -and [string]::IsNullOrWhiteSpace($Feature)) {
    Write-Host ""
    Write-Host "کدام بخش را تست کنم؟ (Enter = همهٔ سناریوها)" -ForegroundColor Cyan
    $features = @("Protocol","Patients","Scheduling","Boarding","Inventory","Purchasing","Sales","Finance")
    for ($i = 0; $i -lt $features.Count; $i++) {
        Write-Host ("  {0}) {1}" -f ($i + 1), $features[$i])
    }
    Write-Host "  0) همه (پیش‌فرض)"
    $fsel = Read-Host "شماره"
    $n = 0
    if ([int]::TryParse($fsel.Trim(), [ref]$n) -and $n -ge 1 -and $n -le $features.Count) {
        $Feature = $features[$n - 1]
    }
}

# ── گرفتن رمز به‌صورت امن اگر داده نشده ────────────────────────────────────────
if ([string]::IsNullOrWhiteSpace($Password)) {
    $sec = Read-Host "رمز کاربر '$User'" -AsSecureString
    $Password = [System.Runtime.InteropServices.Marshal]::PtrToStringAuto(
        [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($sec))
}
if ([string]::IsNullOrWhiteSpace($Password)) {
    Write-Err "رمز خالی است. بدون رمز نمی‌توان وارد شد."
    exit 78
}

# ── ست‌کردن متغیرهای محیطی که LiveTest می‌خواند ───────────────────────────────
$env:ANIMORA_LT_SERVER_URL = $Server
$env:ANIMORA_LT_TENANT     = $Tenant
$env:ANIMORA_LT_USER       = $User
$env:ANIMORA_LT_PASSWORD   = $Password
$env:ANIMORA_LT_OUTPUT     = Join-Path $RepoRoot "artifacts\live-test"
if (-not [string]::IsNullOrWhiteSpace($Feature)) { $env:ANIMORA_LT_FILTER = $Feature }
if ($KeepGoing) { $env:ANIMORA_LT_KEEP_GOING = "1" }

# ── نمایش خلاصه و اجرا ─────────────────────────────────────────────────────────
Write-Host ""
Write-Info "سرور : $Server"
Write-Info "مجموعه: $Tenant"
Write-Info "کاربر : $User"
Write-Info "حالت  : $Mode$(if ($Feature) { "  (فیلتر: $Feature)" })"
Write-Host ""
Write-Info "در حال اجرای LiveTest… (بار اول ممکن است برای build کمی طول بکشد)"
Write-Host ""

Push-Location $RepoRoot
try {
    & dotnet run --project $ProjectPath -c Release -- $Mode
    $rc = $LASTEXITCODE
}
finally {
    Pop-Location
    # رمز را از محیط پاک کن تا در جلسهٔ PowerShell باقی نماند.
    Remove-Item Env:ANIMORA_LT_PASSWORD -ErrorAction SilentlyContinue
}

Write-Host ""
$reportDir = $env:ANIMORA_LT_OUTPUT
if (Test-Path $reportDir) {
    Write-Ok "گزارش‌ها این‌جاست:"
    Write-Host "  $reportDir"
    Get-ChildItem $reportDir -File -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 4 |
        ForEach-Object { Write-Host "   • $($_.Name)" }
}

switch ($rc) {
    0  { Write-Ok  "تمام شد — همهٔ تست‌ها سبز بودند." }
    1  { Write-Err "یک یا چند تست شکست خورد / سطح سالمی پیدا نشد. گزارش بالا را ببین." }
    64 { Write-Err "حالت یا فیلتر نامعتبر بود." }
    78 { Write-Err "تنظیمات ناقص است (کاربر/رمز/مجموعه را بررسی کن)." }
    default { Write-Err "LiveTest با کد خروج $rc تمام شد." }
}
exit $rc
