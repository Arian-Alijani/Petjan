# اجرای ساده‌ی LiveTest — دو مرحله، تمام

این راهنما برای وقتی است که می‌خواهی **بدون درگیری با دسترسی/دیوایس/تنظیمات**، تست‌های
`Animora.LiveTest` را بگیری. فقط دو دستور:

1. یک‌بار روی **سرور دبیان** → ساخت/پاکسازی کامل کاربر و اشتراک و مجوز و دکتر.
2. هر بار روی **ویندوز** → انتخاب تست و گرفتن گزارش.

---

## مرحله ۱ — روی سرور دبیان (یک‌بار، یا هر وقت خواستی از نو تمیز شود)

این را روی همان سروری اجرا کن که `Animora.Server` و PostgreSQL روی آن است:

```bash
cd /path/to/animora-repo

# اگر سرویس systemd داری، رشته‌ی اتصال را از همان بردار:
export DB_CONN='Host=localhost;Port=5432;Database=animora;Username=animora_app;Password=رمزِ-دیتابیس'

bash scripts/perf/setup-livetest.sh
```

این اسکریپت **کاملاً خودکار**:

- اگر Tenant تستی از قبل باشد، **کل داده‌اش را تمیز پاک می‌کند** (همه‌ی جدول‌ها + دستگاه‌ها
  + سشن‌ها + توکن‌ها). برای همین دیگر «مشکل دسترسی/دیوایس» نخواهی داشت.
- Tenant تستی را **از صفر** می‌سازد (لازم نیست اول در برنامه اکانت بسازی).
- اشتراک فعال ۱۰۰ روزه + مجوز فعال می‌سازد.
- کاربر **manager** (برای ورود تست) و کاربر **doctor** فعال (پیش‌شرط سناریوهای ویزیت/نوبت) را می‌سازد.

در پایان، دقیقاً مقادیری که در ویندوز لازم داری را چاپ می‌کند:

```
Tenant   : ANIMORA-LOADTEST
User     : manager
Password : Test-Pass-1234
```

> می‌خواهی نام کاربر/رمز/کد مجموعه فرق کند؟ قبل از اجرا متغیر بده، مثلاً:
> `NEW_USER=qa NEW_PASSWORD='MyPass-123' TENANT_CODE=ANIMORA-TEST bash scripts/perf/setup-livetest.sh`
> (همین‌ها را بعداً به اسکریپت ویندوز هم بده.)

---

## مرحله ۲ — روی ویندوز (هر بار که تست می‌خواهی)

PowerShell را در ریشه‌ی همان ریپو باز کن و اجرا کن:

```powershell
.\scripts\windows\run-livetest.ps1
```

- اگر هیچ آرگومانی ندهی، یک **منوی ساده** نشان می‌دهد تا تست را انتخاب کنی و رمز را امن می‌پرسد.
- یا مستقیم:

```powershell
# همه‌ی تست‌های درستی:
.\scripts\windows\run-livetest.ps1 -Mode correctness -User manager -Password 'Test-Pass-1234'

# فقط یک بخش (مثلاً فروش):
.\scripts\windows\run-livetest.ps1 -Mode correctness -Feature Sales

# فقط بررسی سلامت اتصال (سریع):
.\scripts\windows\run-livetest.ps1 -Mode preflight

# تست ظرفیت:
.\scripts\windows\run-livetest.ps1 -Mode capacity
```

حالت‌ها: `preflight` (سلامت)، `correctness` (درستیِ ۳۶ سناریو)، `capacity` (بار/ظرفیت)،
`all` (اول correctness بعد capacity).

بخش‌های قابل‌فیلتر برای correctness: `Protocol, Patients, Scheduling, Boarding, Inventory,
Purchasing, Sales, Finance`.

گزارش‌ها در پوشه‌ی `artifacts\live-test\` ساخته می‌شوند و مسیرشان در پایان چاپ می‌شود.

> پیش‌نیاز ویندوز: فقط **.NET 10 SDK** (از https://dot.net). اسکریپت خودش چک می‌کند و اگر
> نبود راهنمایی می‌دهد. بار اول چون build می‌شود کمی طول می‌کشد؛ دفعات بعد سریع است.

---

## نکته‌ها

- اگر کد مجموعه/کاربر را روی سرور عوض کردی، همان‌ها را با `-Tenant` و `-User` به اسکریپت
  ویندوز بده تا هماهنگ بماند.
- آدرس سرور پیش‌فرض `http://188.212.99.234:5000` است؛ در صورت نیاز با `-Server` عوضش کن.
- هر وقت خواستی محیط دوباره «نو و تمیز» شود، فقط `setup-livetest.sh` را دوباره اجرا کن.

---

## تست ظرفیت (capacity) — دو نکته‌ی مهم

### ۱) سقف دستگاه را بالا ببر

هر بار اجرای capacity چند دیوایس/لایسنس می‌سازد. سقف پیش‌فرض محصول
`SeatLimit × 2` است (پیش‌فرض: ۵ × ۲ = ۱۰) و خیلی زود پر می‌شود
(`LICENSE_DEVICE_LIMIT_REACHED`). برای تست ظرفیت موقع seed:

```bash
MAX_DEVICES=64 bash scripts/perf/setup-livetest.sh
```

### ۲) ریت‌لیمیت‌ها را برای تست بالا ببر

سرور پنج پنجره‌ی ریت‌لیمیت دارد (`Server:RateLimits:*`). پیش‌فرض‌ها برای کلینیک واقعی
اندازه‌گیری شده‌اند، ولی تست ظرفیت:

- از **یک IP** چند دستگاه را همزمان لاگین می‌کند → `auth` (پیش‌فرض **۱۰ در دقیقه به ازای هر IP**)
  زود می‌بندد؛ هر لاگین ۳ درخواست در همین سبد مصرف می‌کند (login + device/register + licensing/activate).
- همه‌ی کاربران مجازی را با **یک حساب** می‌راند → `commands` (۱۲۰/دقیقه) و `sync` (۶۰/دقیقه)
  به ازای «tenant+user» حساب می‌شوند، پس سقف کل تست می‌شوند.

روی **سرور تست** این بلوک را به `/etc/animora/server.env` اضافه کن و سرویس را ری‌استارت کن:

```ini
# LoadTest overrides — limiter still ON, only the ceilings are raised.
Server__RateLimits__auth__PermitLimit=120
Server__RateLimits__commands__PermitLimit=30000
Server__RateLimits__sync__PermitLimit=30000
Server__RateLimits__files__PermitLimit=3000
```

> محدودکننده غیرفعال نمی‌شود؛ فقط سقف‌ها بالا می‌رود و همچنان بالاتر از آن‌ها `429` می‌دهد.
> محافظ اصلیِ ورود در برابر حمله، **قفل حساب** است (۵ تلاش ناموفق → ۱۵ دقیقه)، نه این پنجره.
> برای برگشت به پیش‌فرض، همان بلوک را حذف و سرویس را ری‌استارت کن.

### ۳) اعداد را از نزدیک سرور بگیر

latency هر درخواست شامل مسیر شبکه‌ی کلاینت است. اگر کلاینت پشت پراکسی/VPN/TUN باشد،
هر درخواست می‌تواند صدها میلی‌ثانیه کندتر شود و نتیجه، «ظرفیت سرور» را نشان نمی‌دهد.
برای عدد واقعی، تست را **روی خود سرور** و روی `http://127.0.0.1:5000` اجرا کن:

```bash
cd /opt/animora
ANIMORA_LT_SERVER_URL=http://127.0.0.1:5000 \
ANIMORA_LT_TENANT=animora-loadtest \
ANIMORA_LT_USER=manager \
ANIMORA_LT_PASSWORD='Test-Pass-1234' \
ANIMORA_LT_OUTPUT=/tmp/live-test \
dotnet run --project tests/Animora.LiveTest -c Release -- capacity
```

سطح‌ها و تعداد درخواست را هم می‌توانی با `ANIMORA_LT_LEVELS=1,2,4,8,16,32` و
`ANIMORA_LT_REQUESTS=60` عوض کنی.

---

## تست کامل و دقیق ظرفیت (Deep) — از ویندوز یا از سرور

این همان تستی است که برای «حداکثر کاربر همزمان + مصرف منابع» استفاده می‌شود:

```powershell
.\run-livetest.ps1 -Mode capacity -Deep -Monitor -Password 'Test-Pass-1234'
```

`-Deep` حالا **رمپ خودکار واقع‌گرایانه** است: هیچ عدد ثابتی نمی‌دهی، تست خودش بالا می‌رود تا سرور بشکند.

| پارامتر | مقدار Deep | معنی |
|---|---|---|
| `-AutoRamp` | روشن | به‌جای لیست ثابت، خودش کاربر اضافه می‌کند |
| `-StartLevel` | `5` | از چند کاربر شروع کند |
| `-Step` | `5` | هر سطح چند کاربر اضافه شود |
| `-MaxLevel` | `200` | سقف ایمنی (اگر سرور نشکست، اینجا می‌ایستد) |
| `-Duration` | `20` | ثانیهٔ اندازه‌گیری هر سطح (همه یکسان ⇒ قابل مقایسه) |
| `-ThinkMin` / `-ThinkMax` | `2000` / `9000` | مکث بین دو اقدام کاربر (میلی‌ثانیه) |
| `-AmbientPollSeconds` | `5` | فاصلهٔ پول فید همگام‌سازی در پس‌زمینه |
| `-MaxDevices` | `64` | نشست دستگاه همزمان (سقف لایسنس را با `MAX_DEVICES` بالا ببر) |
| `-P95LimitMs` | `2000` | آستانهٔ «ناسالم» |

### چرا این تست «واقعی» است

سه چیز تست را از یک بنچمارک مصنوعی جدا می‌کند:

1. **مکث کاربر (think time).** هر کاربر مجازی بین دو اقدام ۲ تا ۹ ثانیه صبر می‌کند
   (توزیع log-uniform، پس بیشتر مکث‌ها کوتاه‌اند و مکث‌های طولانی نادر). قبلاً کاربر
   مجازی بی‌وقفه درخواست می‌فرستاد — یعنی ربات، نه منشی.
2. **پول پس‌زمینهٔ فید.** هر دسکتاپ هر ~۵ ثانیه یک `sync/pull` می‌زند؛ همان کاری که
   `LiveAccessRefreshService` در کلاینت واقعی می‌کند. بدون این، سرور حدود یک‌دهم ترافیک
   واقعی یک کلینیک را می‌دید.
3. **پنجرهٔ زمانی یکسان.** هر سطح ۲۰ ثانیه اندازه‌گیری می‌شود، نه «۶۰ درخواست». پس سطح
   ۵ کاربر و سطح ۱۵۰ کاربر واقعاً قابل مقایسه‌اند.

نتیجه: عدد نهایی **تعداد دسکتاپ کلینیک** است، نه «کاربر مجازی × ضریب تخمینی».

### توقف خودکار

رمپ وقتی می‌ایستد که سرور **۲ سطح پشت‌سرهم** ناسالم باشد (پیش‌فرض `ANIMORA_LT_BREAK_CONFIRM=2`).
یک سطح ناسالم تنها ممکن است یک وقفهٔ GC یا flush دیسک باشد، نه سقف واقعی.

```powershell
# رمپ خودکار با تنظیمات دلخواه
.\run-livetest.ps1 -Mode capacity -AutoRamp -StartLevel 5 -Step 5 -MaxLevel 120 `
    -Duration 20 -ThinkMin 2000 -ThinkMax 9000 -Monitor -Password 'Test-Pass-1234'
```

روی سرور (بدون شبکه، دقیق‌ترین عدد):

```bash
export ANIMORA_LT_AUTO_RAMP=1 ANIMORA_LT_START_LEVEL=5 ANIMORA_LT_STEP=5 ANIMORA_LT_MAX_LEVEL=200
export ANIMORA_LT_DURATION=20 ANIMORA_LT_THINK_MIN_MS=2000 ANIMORA_LT_THINK_MAX_MS=9000
export ANIMORA_LT_AMBIENT_POLL_SECONDS=5 ANIMORA_LT_MAX_DEVICES=64
bash scripts/perf/live-capacity-auto.sh
```

### رصد منابع (`-Monitor`)

با این سوئیچ، یک نشست SSH **فقط-خواندنی** به سرور باز می‌شود و هر ثانیه
CPU کل ماشین، **CPU خودِ پروسهٔ `Animora.Server`**، RSS آن، RAM آزاد، تعداد اتصال‌های
PostgreSQL و وضعیت سرویس را ثبت می‌کند. پیش‌نیاز یک‌باره روی سرور:

```bash
ssh-keygen -t ed25519 -N '' -f ~/.ssh/animora_monitor
cat ~/.ssh/animora_monitor.pub >> ~/.ssh/authorized_keys
```

سپس از ویندوز:

```powershell
.\run-livetest.ps1 -Mode capacity -Deep -Monitor `
    -SshHost 188.212.99.234 -SshUser root -SshKey "$HOME\.ssh\animora_monitor"
```

اگر کلید جای دیگری است با `-SshKey` بده؛ پورت هم با `-SshPort`.

### خواندن گزارش

در جدول هر پروفایل دو ستون CPU هست:

- **Host CPU avg/peak** — کل ماشین (اگر لود‌جنراتور روی همان سرور باشد، شامل آن هم می‌شود).
- **App CPU avg/peak** — فقط پروسهٔ `Animora.Server`. **برای sizing سرور این را نگاه کن**،
  بعد CPU پستگرس را هم اضافه کن.

اگر یک سطح «BROKEN» شد ولی `App CPU` اشباع نبود، یعنی گلوگاه سخت‌افزاری نبوده
(کلاینت/شبکه/قفل/دیسک). گزارش خودش همین را در بخش Verdict می‌نویسد.

> هر بار اجرای Deep چند هزار ردیف در Tenant تستی می‌سازد. بعد از تست، محیط را با
> `setup-livetest.sh` از نو بساز (با اتصال superuser — بخش «پاک‌سازی» را ببین).


