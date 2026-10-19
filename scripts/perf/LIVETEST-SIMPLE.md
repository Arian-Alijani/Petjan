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
