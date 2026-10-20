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
    [switch]   $KeepGoing,
    [switch]   $NonInteractive,

    # ── فقط برای capacity ──────────────────────────────────────────────────────
    # -Deep یک پروفایل کامل می‌چیند: رمپ ۱→۱۲۸ کاربر، پنجرهٔ طولانی هر سطح (تا نمونهٔ
    # CPU/RAM کافی جمع شود) و ۶۴ نشست دستگاه. برای عدد نهایی و قابل اعتماد از همین استفاده کن.
    [switch]   $Deep,
    # سطح‌های همزمانی، مثلاً "1,8,16,32,64,128" (خالی = پیش‌فرض تست یا مقدار -Deep).
    [string]   $Levels   = "",
    # تعداد درخواست در هر سطح (بیشتر = پنجرهٔ طولانی‌تر = میانگین منابع دقیق‌تر).
    [int]      $Requests = 0,
    # حداکثر نشست دستگاه (سقف لایسنس را هم در نظر بگیر).
    [int]      $MaxDevices = 0,
    # آستانهٔ p95 برای «ناسالم» (میلی‌ثانیه).
    [int]      $P95LimitMs = 0,
    # رصد منابع سرور (CPU/RAM/اتصال‌های PG) از طریق SSH. بدون این، ستون‌های منابع n/a می‌شوند.
    [switch]   $Monitor,
    [string]   $SshHost = "127.0.0.1",
    [string]   $SshUser = "root",
    [string]   $SshKey  = "",
    [int]      $SshPort = 22
)

$ErrorActionPreference = "Stop"

# فقط وقتی واقعاً می‌توان از کاربر پرسید، منو/پرامپت نشان بده. در اجرای غیرتعاملی
# (CI، اسکریپت‌نویسی، یا وقتی stdin ریدایرکت شده) Read-Host استثنا می‌دهد و کل اجرا
# را می‌شکند؛ در آن حالت باید مقادیر پیش‌فرض/آرگومان‌ها بی‌سروصدا استفاده شوند.
$CanPrompt = (-not $NonInteractive) -and [Environment]::UserInteractive -and (-not [Console]::IsInputRedirected)

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
    if ($CanPrompt) {
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
    else {
        $Mode = "correctness"
        Write-Info "حالت داده نشد و امکان پرسیدن نیست؛ پیش‌فرض 'correctness' انتخاب شد."
    }
}

# ── برای correctness: انتخاب فیلتر قابلیت (اگر داده نشده و تعاملی هستیم) ─────────
if ($Mode -eq "correctness" -and -not [string]::IsNullOrWhiteSpace($Feature) -and $Feature.Trim() -ieq "all") {
    $Feature = ""   # "all" یعنی بدون فیلتر
}
if ($Mode -eq "correctness" -and [string]::IsNullOrWhiteSpace($Feature) -and $CanPrompt) {
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
if ([string]::IsNullOrWhiteSpace($Password) -and $CanPrompt) {
    $sec = Read-Host "رمز کاربر '$User'" -AsSecureString
    $Password = [System.Runtime.InteropServices.Marshal]::PtrToStringAuto(
        [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($sec))
}
if ([string]::IsNullOrWhiteSpace($Password)) {
    Write-Err "رمز خالی است. بدون رمز نمی‌توان وارد شد. (در اجرای غیرتعاملی، -Password را بده.)"
    exit 78
}

# ── پروفایل کامل تست ظرفیت (فقط حالت capacity) ────────────────────────────────
if ($Mode -eq "capacity") {
    if ($Deep) {
        if ([string]::IsNullOrWhiteSpace($Levels)) { $Levels = "1,8,16,24,32,36,40,48,64,96,128" }
        if ($Requests -le 0)   { $Requests = 1200 }   # پنجرهٔ ~۲۰-۳۰ ثانیه در هر سطح
        if ($MaxDevices -le 0) { $MaxDevices = 64 }
    }
    if (-not [string]::IsNullOrWhiteSpace($Levels)) { $env:ANIMORA_LT_LEVELS = $Levels }
    if ($Requests -gt 0)   { $env:ANIMORA_LT_REQUESTS = "$Requests" }
    if ($MaxDevices -gt 0) { $env:ANIMORA_LT_MAX_DEVICES = "$MaxDevices" }
    if ($P95LimitMs -gt 0) { $env:ANIMORA_LT_P95_LIMIT_MS = "$P95LimitMs" }

    if ($Monitor) {
        if ([string]::IsNullOrWhiteSpace($SshKey)) { $SshKey = Join-Path $HOME ".ssh/animora_monitor" }
        if (-not (Test-Path $SshKey)) {
            Write-Err "کلید SSH پیدا نشد: $SshKey"
            Write-Info "روی سرور این را یک‌بار اجرا کن و کلید عمومی را به authorized_keys اضافه کن:"
            Write-Host "  ssh-keygen -t ed25519 -N '' -f ~/.ssh/animora_monitor"
            Write-Host "  cat ~/.ssh/animora_monitor.pub >> ~/.ssh/authorized_keys"
            Write-Info "یا کلید را با -SshKey مسیر بده. بدون رصد، ستون‌های منابع n/a می‌شوند."
            exit 78
        }
        $env:ANIMORA_LT_SSH      = "1"
        $env:ANIMORA_LT_SSH_HOST = $SshHost
        $env:ANIMORA_LT_SSH_USER = $SshUser
        $env:ANIMORA_LT_SSH_KEY  = $SshKey
        $env:ANIMORA_LT_SSH_PORT = "$SshPort"
    }
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
if ($Mode -eq "capacity") {
    Write-Info "سطح‌ها: $(if ($env:ANIMORA_LT_LEVELS) { $env:ANIMORA_LT_LEVELS } else { 'پیش‌فرض (1,2,4,8,16,32)' })"
    Write-Info "درخواست/سطح: $(if ($env:ANIMORA_LT_REQUESTS) { $env:ANIMORA_LT_REQUESTS } else { '60' })   ·   نشست دستگاه: $(if ($env:ANIMORA_LT_MAX_DEVICES) { $env:ANIMORA_LT_MAX_DEVICES } else { '8' })"
    Write-Info "رصد منابع: $(if ($env:ANIMORA_LT_SSH -eq '1') { "فعال (SSH → $env:ANIMORA_LT_SSH_HOST)" } else { 'خاموش — با -Monitor روشن کن' })"
}
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
