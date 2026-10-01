# بررسی تأییدی داشبورد و تودوی اجرایی اصلاحات

## وضعیت اجرای اصلاحات — 2026-09-30

- ادامه اجرای 2026-10-01 طبق درخواست جدید کاربر: تسک‌های باز به‌صورت مستقل، همراه regression محدود، ثبت شواهد و اجرای `scripts/git/push.sh` دنبال می‌شوند. مبنا main پیشرفته شامل PR #181؛ PR ادامه [#182](https://github.com/Arian-Alijani/Petjan/pull/182). گزارش 2026-09-30 زیر تاریخی است.
- **40 از 44 تسک انجام‌شده؛ 4 تسک باز: D34، D35، D42، D44.**
- PR این اجرا: [#181](https://github.com/Arian-Alijani/Petjan/pull/181)، branch: `genspark_ai_developer`؛ ادامه برنچ پیشرفته D01–D03 که در PR تاریخی [#180](https://github.com/Arian-Alijani/Petjan/pull/180) تحویل شده بود. بعد از هر تسک evidence/checkbox ثبت، با main sync و اسکریپت `scripts/git/push.sh` اجرا شد. برای تحویل نهایی فقط commitهای همین اجرا یک‌جا squash شدند؛ commitهای remote قبلی حفظ شدند و پوش نهایی با lease امن از همان اسکریپت انجام شد.
- خروجی این جلسه: دریافت signed از Payment و علامت صحیح refund، قاعده مشترک غیرCredit داشبورد/صندوق با currency و عنوان صریح، low-stock مشترک product/variant در SQL، عنوان دقیق عملیات چک، حذف اعداد ساختگی/pulse، freeze collections snapshot و حذف presentation مرده. بررسی نهایی فقط `DashboardBudgetTests|DashboardAuthorizationTests|ActivityFeedTests` با `--no-build --no-restore`: **45 passed، 0 failed، 0 skipped، 18 ثانیه اجرای تست** (guard 60 ثانیه). build فقط Desktop: **0 warning/0 error، 96 ثانیه**. warning تاریخی CS8625 در compilation پروژه تست باقی است؛ Full build/Full suite و Windows/GUI اجرا نشده‌اند.
- باقی‌مانده: D34، D35، D42، D44. پذیرش GUI/Windows بدون شاهد واقعی بسته نمی‌شود.
- متن بخش «دامنه/روش/تفاوت baseline» در ادامه، **گزارش تاریخی تدوین اولیه پلن** است؛ عبارت‌های «تغییر صرفاً سندی» و مانع قدیمی کامپایل، وضعیت PR اجرای #181 را توصیف نمی‌کنند. fixture قدیمی ناسازگار در اجرای قبلی فقط به‌اندازه لازم برای کامپایل regression اصلاح شد؛ نه workflow بیماران. اسکریپت‌های Windows/sandbox/git، migrationها و دیتابیس واقعی کاربر در این جلسه تغییر نکردند.

## دامنه و نتیجه مرحله تدوین اولیه (تاریخی)

- تاریخ: 2026-09-30؛ مبنای کد: `0aac7d4`، همان commit گزارش ارسالی `pasted-text-1790782590378.txt`.
- خروجی این مرحله: تطبیق گزارش با کد و طراحی تودوی قابل‌اجرا؛ **در این کار اصلاح کد برنامه انجام نشده است**. همه checkboxهای پذیرش اصلاحی عمداً باز هستند. commit این سند به معنی رفع مشکلات نیست؛ تغییرات قبلی remote در بخش تفاوت نسخه جدا شده‌اند.
- برنامه در مرحله تست و اصلاح است، نه بهره‌برداری کاربران واقعی. اولویت‌های P0/P1 ترتیب رسیدگی‌اند؛ به معنی وقوع افشای اطلاعات در محیط واقعی نیستند.
- `backend-roadmap/PROGRESS.md` وضعیت واقعی را نشان می‌دهد: Track C تمام شده، Desktop به SQLite و سرویس‌های واقعی وصل است، Next phase برابر D01 است و Server/Sync آینده هنوز تکمیل نشده‌اند. این تودو یک مسیر اصلاح محلی مستقل است؛ اجرای D01 یا طراحی سرور پیش‌نیاز آن نیست.
- commitهای این کار فقط همین سند را تغییر می‌دهند؛ هیچ تغییر کد برنامه، migration، تنظیم seed یا اسکریپت اجرای لوکال توسط این کار اعمال نشده است. PR مشترک شامل تغییرات قبلی دیگر نیز هست؛ این عبارت ادعای عدم تغییر کل PR نیست.

## روش و شواهد محدود این جلسه

فقط فایل‌های ارجاع‌شده در گزارش، مسیرهای متناظر auth/cache/navigation و read modelهای لازم برای مقایسه باز شدند؛ تاریخچه فازها و مرجع HTML بررسی نشدند.

1. بررسی مستقیم: Contracts → Client.Services → Application/authorization → SQLite، ViewModel/XAML، تست‌های داشبورد/ActivityFeed و شواهد C11.
2. SDK نسخه `10.0.401` به‌صورت جدا در `.sandbox/sdk` آماده شد؛ اسکریپت‌های ریپو تغییر نکردند.
3. روی baseline `0aac7d4` فقط دو کلاس موجود `DashboardBudgetTests` و `ActivityFeedTests` اجرا شدند: **20 passed، 0 failed، 0 skipped؛ زمان اجرای تست‌ها 11 ثانیه**. وابستگی‌های همان پروژه تست کامپایل شدند؛ Full build، Full test، build دسکتاپ و تست GUI اجرا نشدند. پس از sync با remote، اجرای محدود دوم پیش از شروع تست‌ها به خطای کامپایل خارج از داشبورد برخورد کرد؛ نتیجه baseline را نباید نتیجه head جدید دانست.
4. در کامپایل وابستگی تست، warning موجود `CS8625` در `tests/Animora.Persistence.Tests/AppointmentQueryTests.cs:57` دیده شد؛ بنابراین ادعای build با صفر warning نداریم. این warning خارج از دامنه داشبورد است و تغییر نکرد.
5. بازتولید محاسباتی کوتاه، با داده ساختگی در حافظه و مطابق الگوریتم baseline گزارش:
   - یک نوبت + 80 رویداد متأخر انبار → صفر ردیف نوبت در preview.
   - دریافت 100 و refund برابر 30 → جمع magnitude فعلی 130، در مقابل خالص 70.
   - `[100,-50] → [1,-0.5] → [1,0]`؛ `[-100,-50] → [2,1] → [1,1]`؛ صفرها نیز بررسی شدند.
   - gap پیش‌فرض donut برابر `4.058451°` است؛ سهم‌های تا حدود `1.127348%` رسم نمی‌شوند؛ سهم 1% بازتولید شد.
6. `EXPLAIN QUERY PLAN` روی SQLite در حافظه با 3000 ردیف و سه ایندکس متناظر فعلی انجام شد. probe آخرین Id و نمونه فیلتر day/actor، هر دو `USE TEMP B-TREE FOR ORDER BY` داشتند. **SQL نمونه بود، نه SQL استخراج‌شده EF و نه دیتابیس واقعی برنامه**؛ کندی production اثبات نشده است.
7. سبز بودن تست‌های فعلی اثبات صحت همه مشکلات گزارش نیست؛ بسیاری از سناریوهای منفی اصلاً در آن‌ها وجود ندارند.

فرمان دقیق بررسی محدود (فقط در sandbox این جلسه):

```bash
export DOTNET_ROOT="$PWD/.sandbox/sdk"
source scripts/sandbox/_env.sh
# guard زمانی برای جلوگیری از اجرای طولانی؛ پس از restore فقط همین دو کلاس اجرا می‌شوند.
timeout 150 dotnet test tests/Animora.Application.Tests/Animora.Application.Tests.csproj \
  --filter 'FullyQualifiedName~DashboardBudgetTests|FullyQualifiedName~ActivityFeedTests' \
  --verbosity minimal "${MSBUILD_LOWMEM[@]}"
```

این تنظیم sandbox نباید به اسکریپت Windows یا مسیر SDK سیستم کاربر منتقل شود.

## تفکیک نتیجه تأیید از شدت ادعا

- **قطعی کد**: مسیر، محاسبه یا قرارداد نادرست مستقیماً دیده شده است؛ الزاماً رخداد واقعی برای کاربر را اثبات نمی‌کند.
- **ریسک طراحی**: سازوکار آسیب‌پذیر وجود دارد، ولی رخداد concurrent write، تغییر هویت، خطای mapping یا render در این جلسه مشاهده نشده است.
- **ریسک عملکرد**: پوشش ناکافی index دیده شده و plan نمونه آن را پشتیبانی می‌کند؛ latency واقعی نیازمند اندازه‌گیری محدود است.
- **شکاف سنجش**: سند/تست موجود معیار موردنظر را نمی‌سنجد؛ خودِ کند بودن داشبورد ثابت نشده است.

### نکات اصلاحی نسبت به گزارش اولیه

- موارد 1 و 5 اثبات دسترسی ناشناس/حمله شبکه‌ای یا موفقیت فرمان ممنوع backend نیستند. مورد 1 اعتماد backend به نقش presentation و مورد 5 ناسازگاری کنترل ورود UI است.
- حسابدار به layout مدیر نگاشت شده است؛ این به‌خودی‌خود باگ امنیتی نیست. استفاده از همان enum برای تصمیم خواندن اطلاعات، باگ است.
- پذیرش می‌تواند layout شبیه مدیر داشته باشد، اما بدون مجوز مالی نباید DTO درآمد دریافت کند؛ مخفی‌سازی XAML کافی نیست.
- KPI فعلی «چک‌ها» قطعاً eventهای چک را می‌شمارد، نه موجودیت چک. انتخاب ساده و بدون توسعه دامنه در این تودو: عنوان دقیق «تغییر وضعیت چک امروز».
- «فاکتورهای باز» داشبورد فقط Posted/PartiallyPaid را می‌شمارد؛ `CashierQueries`، Draft را هم Open می‌داند. این اختلاف در D10 باید با تعریف صفحه صندوق حل شود، نه با حدس یک تعریف تازه.
- reuse کورکورانه `CashierQueries.GetStatsAsync` مشکل Credit را حل نمی‌کند: این query نیز `Payment`ها را بدون فیلتر Credit جمع می‌کند. D14 باید قاعده مشترک صحیح بسازد، نه query غلط دیگری را کپی کند.
- `DailyFinanceFact` در وضعیت فعلی جایگزین آماده «خالص دریافت» نیست؛ `RevenueMinor` درآمد فروش است، نه receipt. D13/D14 باید مبنای Payment معتبر یا projection مشخص receipt داشته باشند، بدون ledger writer جدید.
- مورد کد مرده: `ParseColor` در `DashboardDoctorLoadViewModel` وجود دارد؛ در نسخه فعلی `DashboardSpeciesViewModel` متد `ParseColor` وجود ندارد. عضو واقعاً بدون مصرف آن فایل، gradient/`FillBrush` است.
- موارد 4، 19، 20، 21، 27، 29 و 39 رخداد runtime اثبات‌شده نیستند. فقدان lifecycle/invalidation یا ساختار غیراتمیک قطعی است؛ وقوع و شدت پیامد باید با تست کوچک همان تسک سنجیده شود.
- اعداد startup/render/memory گزارش، تاریخی و دارای proxy هستند؛ با اندازه‌گیری این جلسه یا نتایج روی Windows اشتباه نشوند.

## تفاوت baseline با branch مشترک هنگام پوش

اسکریپت آماده در مرحله fetch/rebase، هشت commit قبلی branch `genspark_ai_developer` را حفظ و دریافت کرد؛ head کد پیش از سند `3fdd3f6` است. PR موجود **[#179](https://github.com/Arian-Alijani/Petjan/pull/179)** قبلاً برای اصلاحات dashboard/appointments/patients/local-test ساخته شده بود. title/body آن بازنویسی نشده و تغییرات دیگران حذف یا squash نشده‌اند. فقط diff فایل‌های داشبورد نسبت به `0aac7d4` دوباره بررسی شد.

**شواهد خط/وضعیت در بدنه 44 تسک، baseline گزارش را شرح می‌دهند. جدول زیر بر آن‌ها برای اجرای نسخه جدید اولویت دارد؛ کاری که remote انجام داده دوباره پیاده نشود.**

| تسک | تغییر موجود در remote `3fdd3f6` | کار باقی‌مانده برای نسخه جدید |
|---|---|---|
| D06 | فهرست primary مدیر/پزشک از Appointment روز + نام بیمار/مالک/پزشک و `Take(8)` ساخته می‌شود. | بخش نوبت را از صفر بازنویسی نکنید؛ cashier primary هنوز Sale events است، secondary پزشک هنوز Vaccine/Visit events است، ready-to-bill/reminder state معتبر کامل نشده است. |
| D07 | primary پزشک اکنون از appointments با DoctorUserId می‌آید. | KPI visits/vaccinations و secondary همچنان از feed با ActorUserId هستند؛ فقط این scopeهای باقی‌مانده اصلاح/تست شوند. |
| D09 | limit مستقل preview نوبت وجود دارد. | regression نوبت حفظ شود؛ primary صندوق و secondary پزشک هنوز زیر سقف مشترک feed هستند. limit مستقل لازم برای widgets عملیاتی باقی‌مانده را بررسی کنید. |
| D10 | queue count اکنون Scheduled/Confirmed/CheckedIn را می‌شمارد، نه فقط Scheduled. | هنوز appointment-count است، نه derived queue دارای walk-in/entry؛ عنوان صف پرداخت نیز تغییر نکرده. عبارت «فقط Scheduled» دیگر توصیف head جدید نیست. |
| D12 | workload از User.DisplayName و Done=Completed می‌آید؛ Cancelled/NoShow از total کنار گذاشته شده‌اند. | نقص اصلی نام/Done در کد remote اصلاح شده؛ این تسک اکنون بررسی پذیرش و فقط gap اثبات‌شده است. تست واقعی User/وضعیت‌ها و initial text-element/ترتیب پایدار بررسی شود؛ Done=0 ثابت دوباره ادعا نشود. |
| D16 | IconKey معنایی در DTO افزوده؛ Desktop، HistoryText را برای activity ترجمه می‌کند؛ subtitle دیگر EntityId خام نیست؛ نوبت نام/status دارد. | بخش رفع‌شده حفظ شود؛ کامل بودن state rows صندوق/یادآور طبق D06 و حذف fallback شکننده مبتنی بر متن بررسی شود؛ گفته «همه titleها فنی و iconها عمومی‌اند» دیگر درست نیست. |
| D17 | SpeciesName فارسی و چند رنگ متمایز اضافه شده‌اند. | deterministic order/typed key هنوز نیست؛ Rodent/Reptile/Other خاکستری مشترک دارند و icon resolver هنوز نام ترکیبی قدیمی خرگوش/جوندگان را انتظار دارد. همه گونه‌ها یک رنگ نیستند. |

برای D01/D02/D03/D13/D14، role trust، receptionist manager branch، cache bypass، sign=+1 و نبود فیلتر Credit در diff remote تغییری نکرده‌اند؛ یافته‌های اصلی همچنان برقرارند. normalization/clamp نمودار منفی، lifecycle و refresh، version=ActivityEvent.Id، low-stock با threshold صفر و quick counts ثابت نیز در فایل‌های مربوطه تغییر نکرده‌اند.

### مانع جدید بررسی محدود روی head دریافت‌شده

اجرای دوباره همان دو کلاس با `--no-restore` و guard 120ثانیه، **پیش از test execution** در این خط متوقف شد:

```text
tests/Animora.Application.Tests/PatientsUseCaseTests.cs(306,22): error CS1729:
'OwnerSummaryReadModel' does not contain a constructor that takes 10 arguments
```

این خطا در تغییرات قبلی remote مربوط به contracts/read model بیماران است، نه commit سند. مطابق ممنوعیت کار نامرتبط، برای اصلاح آن به سایر workflowهای بیماران ورود نشده است. نتیجه این اجرا **موفق نیست**، حتی اگر wrapper خروجی exit_code=0 نشان دهد؛ معیار وجود خطای کامپایل و عدم اجرای تست‌هاست. ابتدا مالک تغییر مربوطه compatibility fixture را اصلاح کند، سپس فقط همین فیلتر دوباره اجرا شود. تا آن زمان، تأیید تست‌های نسخه جدید blocked است؛ 20/20 فقط به baseline تعلق دارد.

PR مشترک پیش از این کار `scripts/windows/clean-run.ps1` را هم تغییر داده بود؛ این کار آن تغییر را دست نزده و سلامت اسکریپت Windows جدید را تأیید نکرده است. برای تصمیم merge، کل PR با یک commit صرفاً سندی اشتباه نشود.

## قرارداد اجرای تمام تسک‌ها

1. هر تسک دقیقاً یک مشکل این گزارش را هدف می‌گیرد. تست regression همان مشکل جزئی از همان تسک است؛ برای آن فاز جداگانه یا Full suite نسازید.
2. فقط فایل‌های «شواهد/مسیر» تسک و وابستگی مستقیم آن باز شوند. اگر تغییر قرارداد لازم شد، تنها مصرف‌کننده‌های آن عضو با جست‌وجوی دقیق بررسی شوند.
3. UI فقط به Contracts متکی بماند؛ EF/SQL/DI scope یا قواعد امنیتی وارد View/ViewModel نشوند. readها `AsNoTracking`، sequential روی یک DbContext و bounded باقی بمانند. loading از constructor برنگردد.
4. مجوزها از مرجع معتبر membership/role definition و مسیر authorization موجود استفاده کنند. enum نمایشی، cache key و sidebar جای authorization نیستند. bypass برای حساب تست یا allow-all ممنوع است.
5. offline-first و local-first حفظ شوند؛ auth محلی موجود کافی است. برای این اصلاحات server، API شبکه، sync کامل یا session واقعی خارجی لازم نیست.
6. `scripts/windows/clean-run.ps1`، `scripts/windows/test-data/**`، `scripts/sandbox/**` و `scripts/git/push.sh` بدون ضرورت اثبات‌شده تغییر نکنند. defaultهای startup، seed، encryption و مسیر دیتابیس کاربر عوض نشوند.
7. هر تغییر schema فقط migration افزایشی + Designer/snapshot متناظر؛ migration قبلی ویرایش نشود. پیش از migration، امکان استفاده از اطلاعات پایدار فعلی هدفمند بررسی شود. database واقعی کاربر reset/rebuild/delete نشود؛ آزمون روی store موقت ایزوله باشد.
8. Full build، Full test، bench کامل، تست endurance/soak و بررسی طولانی ممنوع‌اند. حد بررسی: فیلتر همان regression و یک build پروژه اثرپذیر، با سقف زمانی مشخص و بدون build کل solution. تغییر صرفاً سندی، build برنامه لازم ندارد.
9. اگر regression هدف‌دار شکست خورد، تسک باز می‌ماند؛ اگر GUI/Windows در دسترس نبود، بخش دستی «تأییدنشده» ثبت شود، نه Done. شواهد اندازه‌گیری قدیمی، جای تأیید جدید نیست.
10. انتهای هر تسک: معیار پذیرش بررسی، checkbox و نتیجه واقعی به‌روز، `git diff --check`، sync با main، سپس اجرای **اسکریپت آماده پوش**؛ PR ایجاد/به‌روزرسانی و URL گزارش شود. هیچ پوش مستقیم جایگزین اسکریپت نشود.

### چرخه مشترک git برای هر تسک

روی branch `genspark_ai_developer` کار شود. وضعیت کثیف نامرتبط با `add -A` اسکریپت stage می‌شود؛ پیش از استفاده حتماً `git status --short` بررسی شود.

```bash
git diff --check
git status --short
git fetch origin main
git rebase origin/main
bash scripts/git/push.sh "fix(dashboard): DXX <one verified issue>"
# برای سند از docs(dashboard) استفاده شود.
# PR موجود به‌روز یا PR جدید به main ساخته و لینک آن گزارش شود.
```

خود اسکریپت همه تغییرات را commit می‌کند و با branch جاری sync و push می‌کند؛ **sync با main را خودش تضمین نمی‌کند**، بنابراین مرحله بالا لازم است. در conflict، کار متوقف و merge با حفظ منطق remote حل و regression مرتبط دوباره اجرا شود؛ تغییرات دیگران با reset/force push کور حذف نشوند. خطای احراز هویت یا push، «Done» محسوب نشود.

برای PR تازه، commitهای همین کار مطابق سیاست ریپو در یک commit جامع جمع شوند؛ تغییرات remote squash نشوند. اگر branch قبلاً منتشر شده و squash لازم شد، فقط پس از fetch و کنترل remote، lease امن تنظیم و دوباره همان اسکریپت اجرا شود؛ force بدون lease ممنوع است.

## ترتیب اجرای کم‌هزینه

شناسه D در این سند مخفف **Dashboard fix** است، نه فاز D01 سرور. شناسه‌ها شماره گزارش را حفظ می‌کنند و ترتیب اجرا با آن‌ها الزاماً یکی نیست.

- موج A — مرز داده و عدد غلط: D01 → D02 → D03؛ D13 → D14؛ D05؛ D32.
- موج B — state معتبر: D06 → D07 → D09 → D10؛ D08؛ D11؛ D12؛ D15؛ D16؛ D17؛ D18 → D30.
- موج C — lifecycle و تازگی: D25 → D37 → D38 → D04 → D20؛ D26 → D27 → D28؛ D19 → D21.
- موج D — performance/اثبات: بعد از queryهای نهایی D22 → D23 → D24 → D34؛ D35 به‌صورت سنجش کوتاه، نه پروژه بهینه‌سازی startup کل برنامه.
- موج E — UI و نگهداری: D33 پس از D05؛ D29؛ D31؛ D39؛ D40؛ D41؛ D42؛ D43؛ D44؛ D36 در پایان برای تطبیق با رفتار نهایی.

وابستگی‌ها محدودند؛ تسک‌های مستقل می‌توانند جلوتر انجام شوند. در هر لحظه فقط یک تغییر معنادار در حال اجرا باشد.

## تودو — یک مشکل در هر تسک

### D01 — نقش ارسالی، مرجع دسترسی داده است
- [x] وضعیت: انجام‌شده | P0 | گزارش 1 | نتیجه: دسترسی از session و membership معتبر؛ نقش UI مرجع داده نیست.
- شواهد اجرای 2026-09-30: `DashboardAuthorizationTests` با session محلی، dispatcher، RBAC ذخیره‌شده و SQLite واقعی، درخواست Manager توسط Doctor/Receptionist/Accountant، grant/deny و نقش ناشناخته را بررسی می‌کند. فیلتر همین کلاس + `DashboardBudgetTests`: **18 passed، 0 failed، 12 ثانیه**؛ Full build/test اجرا نشد. fixture ناسازگار `OwnerSummaryReadModel` فقط با حذف آرگومان حذف‌شده اصلاح شد تا پروژه regression کامپایل شود. warning تاریخی CS8625 خارج از داشبورد باقی است. policy زنده پیش از cache نیز اجرا می‌شود؛ پذیرش اختصاصی D02/D03 هنوز مستقل است.
- شواهد: `IDashboardQueryService.GetSnapshotAsync`؛ `DashboardQueryService.cs:22–45`؛ `DashboardSnapshotQuery.cs:36–51`؛ `DashboardQueries.cs:20–26`؛ `PermissionAuthorizationGate.cs:79–87`.
- اقدام: نقش امنیتی/هویت از session معتبر و membership فعلی تعیین شود؛ نقش layout جدا بماند. Application مجوزهای مؤثر هر بخش را با منطق RBAC موجود تعیین کند و پیش از query، داده غیرمجاز را حذف کند. `RequiresPermission` عمومی به‌تنهایی جای مجوز هر widget نیست؛ پزشک پیش‌فرض `ReportsRead` هم دارد، پس آن مجوز به‌تنهایی نباید درآمد مالی را آزاد کند.
- پذیرش/بررسی محدود: session پزشک با درخواست Manager در مسیر service → dispatcher هیچ مقدار مالی، workload مدیریتی یا aggregate خارج از scope خود نگیرد؛ override grant/deny و نقش نامعتبر fail-closed آزموده شوند؛ افزودن Accountants به layout، مجوز همه داده‌ها ایجاد نکند.
- پایان تسک: پس از بررسی محدود و sync با main، حتماً اجرا شود: `bash scripts/git/push.sh "fix(dashboard): D01 derive data access from trusted identity"`؛ سپس PR به‌روزرسانی و لینک گزارش شود.

### D02 — پذیرش داده‌های مدیریتی غیرمجاز دریافت می‌کند
- [x] وضعیت: انجام‌شده | P0 | گزارش 2 | نتیجه: widget و DTO مستقل از layout و permission-based | وابستگی: D01.
- شواهد اجرا: کارت مالی/موجودی فاقد مجوز از Stats حذف می‌شود (نه صفر)، rich widget فاقد مجوز null است و View آن را نمایش نمی‌دهد. SQL مالی/انبار به statementهای مستقل guarded تفکیک شد؛ interceptor مسیر واقعی session → service → dispatcher → SQLite در `Receptionist_ReceivesNoFakeFinancialZero_AndForbiddenSourcesAreNotQueried` عدم query ممنوع و grant/deny را تأیید کرد. **19 regression passed، 0 failed، 12 ثانیه**؛ build فقط Desktop **0 warning/0 error، 64 ثانیه**. budget persistence-only با افزایش مستند یک statement مجاز به‌روز شد؛ D24 هنوز باز است.
- شواهد: `DashboardQueries.cs:22,59–66`؛ `DashboardQueryService.cs:70–96`؛ `DashboardPageView.axaml.cs:31`؛ `DefaultRolePermissions.cs:36–46`.
- اقدام: مشترک بودن layout با مدیر حفظ‌پذیر است، اما widget visibility/availability و DTO مستقل و permission-based شوند. «نداشتن اجازه» به صفر مالی جعلی تبدیل نشود؛ query مالی/موجودی غیرمجاز اصلاً اجرا نشود.
- پذیرش/بررسی محدود: پذیرش پیش‌فرض درآمد و low-stock دریافت نکند؛ تست یک grant مالی معتبر و یک deny صریح، بدون وابستگی به مخفی‌سازی sidebar؛ شمارنده SQL ثابت کند بخش ممنوع خوانده نشده است.
- پایان تسک: پس از بررسی محدود و sync با main، حتماً اجرا شود: `bash scripts/git/push.sh "fix(dashboard): D02 isolate receptionist widget permissions"`؛ سپس PR به‌روزرسانی و لینک گزارش شود.

### D03 — cache-hit از authorization جاری عبور نمی‌کند
- [x] وضعیت: انجام‌شده | P1 | گزارش 3 | نتیجه: هر cache-hit از policy زنده مشترک عبور می‌کند | وابستگی: D01.
- شواهد اجرا: کلید cache شامل tenant/user، نقش معتبر و مجوز مؤثر تمام بخش‌ها، روز و نسخه است. `WarmCache_RechecksLiveAuthorization` روی authorization واقعی و SQLite، suspension/revoke/lock/expiry/signout را پس از cache گرم رد می‌کند؛ تست grant/deny D01/D02 داده قدیمی را پس نمی‌دهد. `AuthorizedConcurrentCacheHits_RemainCoalescedAndDayScoped` هشت درخواست را با یک finance read و snapshot یکسان و تغییر روز با ساعت تزریقی بررسی کرد. **12 تست کلاس authorization passed، 0 failed، 13 ثانیه**. تنها warning جدید analyzer با overload استاندارد Assert.Single اصلاح شد؛ CS8625 تاریخی باقی است.
- شواهد: `DashboardQueryService.cs:32–43`؛ `SessionCurrentUserProvider.RequireActive`؛ `PermissionAuthorizationGate.cs:76–87`.
- اقدام: پیش از هر cache response، session/membership و مجوز مؤثر معتبر بررسی شوند؛ هویت/نسخه مجوز جزئی از اعتبار cache باشد و تغییر آن invalidate کند. policy واحد reuse شود؛ دور زدن dispatcher با auth ساختگی ممنوع. بررسی زنده مجوز به cache 60ثانیه‌ای تکیه نکند.
- پذیرش/بررسی محدود: cache گرم، سپس suspension/revoke/deny/lock/expire؛ فراخوانی بعدی داده قبلی را ندهد. آزمون با authorization واقعی، نه `CacheDispatcher`، انجام شود؛ cache-hit مجاز همچنان bounded و coalesced بماند.
- پایان تسک: پس از بررسی محدود و sync با main، حتماً اجرا شود: `bash scripts/git/push.sh "fix(dashboard): D03 authorize cached responses"`؛ سپس PR به‌روزرسانی و لینک گزارش شود.

### D04 — singleton داشبورد هویت/نقش قدیمی را نگه می‌دارد
- [x] وضعیت: انجام‌شده | P1 | گزارش 4 | نتیجه: نقص lifecycle قطعی؛ سناریوی GUI تأییدنشده | وابستگی: D01، D25، D37، D38.
- شواهد اجرای 2026-10-01: ViewModel سینگلتون اکنون در هر activation هویت (`UserId|Role`) را با `ICurrentUserContext` مقایسه می‌کند؛ تغییر هویت/نقش، coordinator را `Reset` می‌کند (درخواست in-flight هویت قبلی cancel و پاسخ دیررس آن هرگز اعمال نمی‌شود، snapshot منتشرشده فراموش می‌شود) و تمام collections/chart/species/عنوان‌ها/خطا پاک و `Role` (اکنون ObservableProperty) به‌روز می‌شود. `DashboardPageView` به تغییر property نقش (نه فقط DataContext) پاسخ می‌دهد، زیرنمای مناسب را می‌سازد، و subscription در detach/attach دقیقاً یک‌بار برقرار می‌شود. layout نقش از authorization جداست (D01–D03 دست‌نخورده). تست‌های `IdentityReset_*` به `DashboardLifecycleTests` اضافه شدند: `DashboardLifecycleTests|NavigationAdmissionTests` **21 passed، 0 failed، 8 ثانیه**؛ build فقط Desktop **0 warning/0 error، 47 ثانیه**. سناریوی GUI واقعی (Doctor→Receptionist در پنجره) در این sandbox بدون نمایشگر اجرا نشد؛ منطق reset/late-response با تست و wiring view با build تأیید شد.
- شواهد: `DashboardPageViewModel.cs:41–46,92`؛ DI در `App.axaml.cs:281`؛ `CurrentUserContextService.cs:33`؛ `RoleMapping.cs:15–17`؛ view فقط `DataContextChanged` را می‌شنود.
- اقدام: تغییر هویت/نقش snapshot، collections و درخواست قبلی را reset کند؛ layout از نقش presentation و permission مستقل باشد. View به تغییر state/layout پاسخ دهد، نه فقط reference DataContext. subscribe/unsubscribe دارای lifecycle باشد؛ بدون ساخت backend احراز هویت جدید.
- پذیرش/بررسی محدود: در همان process با هویت تست محلی Doctor → Receptionist/Accountant، زیرنمای درست و فقط داده مجاز نمایش یابد؛ پاسخ دیررس هویت قبلی اعمال نشود. تغییر layout با DataContext ثابت هم آزموده شود.
- پایان تسک: پس از بررسی محدود و sync با main، حتماً اجرا شود: `bash scripts/git/push.sh "fix(dashboard): D04 reset state on identity changes"`؛ سپس PR به‌روزرسانی و لینک گزارش شود.

### D05 — quick action و hotkey کنترل ورود sidebar را ندارند
- [x] وضعیت: انجام‌شده | P1 | گزارش 5 | نتیجه: policy مشترک typed page/key برای تمام overloadهای navigation.
- شواهد 2026-10-01: gate پیش از resolve/configure؛ unknown و key نامنطبق fail-closed؛ پس از await/configure مجدداً gate؛ denial با null و toast امن بدون exception. Purchasing/Reception/Cashier با permission واقعی catalog صریح map شدند؛ Finance expense/reports با semantic key مستقل‌اند. `NavigationAdmissionTests`: **6 passed، 0 failed، 82ms**، production source لینک‌شده با pageهای inert فقط برای حذف startup GUI؛ تست backend auth مستقل دست‌نخورده. build فقط Desktop: **0 warning/0 error، 55 ثانیه**؛ warning تاریخی CS8625 در compilation تست باقی است. اسکریپت پوش اجرا و PR [#182](https://github.com/Arian-Alijani/Petjan/pull/182) ثبت شد. Full build/test اجرا نشد.
- شواهد: `DashboardPageViewModel.OpenQuickAction`؛ `DashboardQuickAction.All`؛ `MainWindowViewModel.cs:93–119`؛ تمام overloadهای `NavigationService`؛ مقایسه `SidebarViewModel.cs:56–64` و `NavPermissions`.
- اقدام: policy مرکزی permission→target در navigation موجود؛ sidebar/tile/hotkey/palette و overloadهای configure/async همگی همان تصمیم را استفاده کنند. denial به‌صورت قابل‌فهم و بدون exception کنترل‌نشده باشد؛ قبل از resolve/configure صفحه ممنوع gate انجام شود. targetهای خارج sidebar مثل Purchasing/Reception/Cashier صریحاً map شوند؛ صفحه ناشناخته fail-closed بماند.
- پذیرش/بررسی محدود: پذیرش به Inventory/Finance/Purchasing از هیچ ورودی وارد نشود؛ grant/deny رفتار یکسان ایجاد کند؛ به‌هنگام await configure و revoke دوباره gate شود. رفتار landing لوکال مجاز حفظ شود؛ backend مستقل باقی بماند.
- پایان تسک: پس از بررسی محدود و sync با main، حتماً اجرا شود: `bash scripts/git/push.sh "fix(navigation): D05 centralize page admission policy"`؛ سپس PR به‌روزرسانی و لینک گزارش شود.

### D06 — فهرست عملیاتی از رویداد ساخته شده، نه وضعیت جاری
- [x] وضعیت: انجام‌شده | P1 | گزارش 6 | نتیجه: قطعی مدل داده | وابستگی: D01/D02.
- شواهد اجرای 2026-10-01: فهرست‌های عملیاتی از وضعیت جاری خوانده می‌شوند (Appointment روز، OpenInvoices، ReadyBillingProjection، یادآور واکسن بدون MedicalHistory انجام‌شده) و ActivityFeed فقط در بخش «فعالیت‌ها»ی مدیر/پذیرش می‌ماند. تست `OperationalLists_ReadCurrentState_NotRecentEvents` (رزرو ثبت‌شده قبلی برای امروز، لغوشده حذف، دو پرداخت یک فاکتور = یک ردیف PartiallyPaid، handoff آماده برابر query مرجع فروش، یادآور واقعی، store خالی و deny فروش، 90 رویداد نامرتبط) + کلاس‌های Budget/Authorization: **39 passed، 0 failed، 19 ثانیه**. هر لیست در SQL با `Take(8)` محدود است؛ Full build/test اجرا نشد.
- شواهد: `DashboardQueries.cs:43–55`؛ `DashboardQueryService.cs:74–85`؛ read modelهای موجود `ReceptionQueueQueries`، `SalesQueries.GetReadyForBillingAsync/GetInvoicesAsync` و `VaccinationQueries.GetDueAsync`.
- اقدام: projection اختصاصی bounded برای نوبت/صف امروز، فاکتور باز، آماده صدور و یادآور لازم تعریف شود؛ قواعد موجود reuse یا به projection مشترک استخراج شوند، نه fetch کل صفحه و filter حافظه. ActivityFeed فقط در بخش صریح «فعالیت‌ها» استفاده شود؛ هیچ تغییر Command/Domain غیرلازم انجام نشود.
- پذیرش/بررسی محدود: رزرو دیروز برای امروز حاضر باشد؛ cancelled از صف فعال حذف شود؛ چند پرداخت یک فاکتور را چند ردیف فاکتور نکند؛ ready handoff و reminder واقعی دیده شوند؛ empty store لیست خالی صحیح بدهد؛ اندازه هر لیست در SQL محدود باشد.
- پایان تسک: پس از بررسی محدود و sync با main، حتماً اجرا شود: `bash scripts/git/push.sh "fix(dashboard): D06 read operational lists from current state"`؛ سپس PR به‌روزرسانی و لینک گزارش شود.

### D07 — داده پزشک با actor به‌جای assigned doctor فیلتر می‌شود
- [x] وضعیت: انجام‌شده | P1 | گزارش 7 | نتیجه: قطعی کد | وابستگی: D06.
- شواهد اجرای 2026-10-01: KPI «ویزیت‌های من/واکسن‌ها» پزشک اکنون از Visit.DoctorUserId (ویزیت تکمیل‌شده امروز) و VaccinationRecord (پزشک تزریق‌کننده یا پزشک Visit مرتبط) خوانده می‌شود؛ ActorUserId فقط برای audit باقی مانده و feed برای پزشک خوانده/نمایش داده نمی‌شود (primary از Appointment.DoctorUserId و یادآور از نوبت‌های واگذارشده بود). تست `DoctorKpis_UseAssignedDoctorAndAdministeringDoctor_NotAuditActor`: رویدادهای actor=پذیرش برای doc-A و doc-B، doc-A=1 ویزیت/2 واکسن، doc-B=1/1، خود actor=0 ویزیت؛ actor/name برای مدیر دست‌نخورده. کلاس‌های Budget/Authorization: **39 passed، 0 failed، 19 ثانیه**؛ Full build/test اجرا نشد.
- شواهد: `DashboardQueries.cs:45` در مقابل فیلتر appointment در خط 71؛ `ActivityFeedWriter.cs:19–20`؛ `BookAppointmentCommandHandler`؛ actor فرمان و `DoctorId` دو مفهوم مستقل‌اند.
- اقدام: هر فهرست/KPI «من» به پزشک مسئول موجودیت بالینی/نوبت/صف مرتبط شود؛ actor فقط برای audit/activity باقی بماند. `ActorUserId = DoctorId` ننویسید؛ درباره واکسن هم administering doctor با مسئولیت آینده یکی فرض نشود.
- پذیرش/بررسی محدود: نوبت ثبت‌شده توسط پذیرش برای doctor-A در داشبورد A دیده شود، در B نباشد؛ انتقال مسئولیت و walk-in assigned بررسی شوند؛ نام/actor audit دست‌نخورده بماند.
- پایان تسک: پس از بررسی محدود و sync با main، حتماً اجرا شود: `bash scripts/git/push.sh "fix(dashboard): D07 scope clinical work by assigned doctor"`؛ سپس PR به‌روزرسانی و لینک گزارش شود.

### D08 — rebuild، actor واقعی live را حفظ نمی‌کند
- [x] وضعیت: انجام‌شده | P1 | گزارش 8 | نتیجه: قطعی کد؛ برابری کل مسیر فعلی اثبات نشده.
- شواهد اجرای 2026-10-01: ActorUserId معتبر session در همان transaction روی OutboxMessage ذخیره می‌شود (migration افزایشی _020 + Designer/snapshot؛ migrationهای قبلی دست‌نخورده) و RebuildActivityFeedAsync همان actor/correlation/tenant را از outbox مصرف می‌کند (ترتیب CreatedAtUtc,Id قطعی). سیاست legacy: outbox بدون actor ⇒ actor نامعلوم، هرگز پزشک event حدس زده نمی‌شود. تست `ActivityActor_IsDurableInOutbox_AndRebuildMatchesLiveWriter` روی DB موقت: live writer→save، rebuild، rebuild دوم deterministic، live==rebuilt، ردیف legacy بدون actor. ActivityFeed+Budget+Authorization: **63 passed، 0 failed**؛ دیتابیس واقعی کاربر rebuild/reset نشد.
- شواهد: `ActivityFeedWriter.cs:19–34`؛ `LocalStoreMaintenance.cs:232–243`؛ `ActivityFeedMapper.cs:42–44`؛ `OutboxMessage` دارای CorrelationId ولی بدون ActorUserId است؛ تست purity مسیر writer را اجرا نمی‌کند.
- اقدام: provenance actor/tenant از correlation معتبر در همان transaction outbox پایدار شود و rebuild همان metadata را مصرف کند. ابتدا امکان بازیابی مطمئن از metadata موجود بررسی شود؛ اگر نیست migration افزایشی کوچک. برای outbox قدیمی فاقد provenance، actor «نامعلوم» باشد؛ پزشک event را به‌عنوان actor حدس نزنید. rebuild خودکار دیتابیس کاربر برای آزمون ممنوع است.
- پذیرش/بررسی محدود: با command actor پذیرش و event doctor دیگر، live writer→save و maintenance rebuild روی DB موقت، actor/correlation/tenant یکسان تولید کنند؛ اجرای rebuild دوباره deterministic باشد؛ سیاست legacy صریح تست شود.
- پایان تسک: پس از بررسی محدود و sync با main، حتماً اجرا شود: `bash scripts/git/push.sh "fix(activity): D08 preserve durable actor provenance"`؛ سپس PR به‌روزرسانی و لینک گزارش شود.

### D09 — سقف مشترک 80 رویداد یک widget دیگر را حذف می‌کند
- [x] وضعیت: انجام‌شده | P1 | گزارش 9 | نتیجه: قطعی + بازتولید حافظه | وابستگی: D06.
- شواهد اجرای 2026-10-01: regression `WidgetPreviews_AreBoundedIndependently_AndAggregatesIgnorePreviewLimits`: 12 نوبت + 12 فاکتور + 12 handoff + 120 رویداد نامرتبط؛ هر preview در SQL حداکثر 8 ردیف با ترتیب پایدار (StartUtc,Id / IssuedUtc,Id / CompletedUtc,Id)، سقف 80 فقط مال بخش «فعالیت‌ها»، و QueueCount/OpenInvoices aggregate مستقل از preview (12). مسیر معیوب filter+Take(8) روی feed در D06/D07 حذف شده بود؛ این تسک تأیید حذف وابستگی است. کلاس Budget: **28 passed، 0 failed، 16 ثانیه**.
- شواهد: `DashboardQueries.cs:53` و filter سپس `Take(8)` در `DashboardQueryService.cs:74–85`.
- اقدام: limit/sort مستقل در SQL هر widget؛ limit کلی feed فقط برای preview همان فعالیت‌ها. total/HasMore در صورت لازم مستقل از preview باشد؛ ترتیب دارای tie-breaker ثابت باشد. اگر D06 مسیر معیوب را حذف کرد، این تسک فقط regression و تأیید حذف وابستگی است، نه پیاده‌سازی تکراری.
- پذیرش/بررسی محدود: 1 نوبت + 80 رویداد نامرتبط، نوبت را حذف نکند؛ هر لیست حداکثر اندازه مصوب داشته باشد؛ aggregate روز از preview مشتق نشود.
- پایان تسک: پس از بررسی محدود و sync با main، حتماً اجرا شود: `bash scripts/git/push.sh "fix(dashboard): D09 bound widgets independently"`؛ سپس PR به‌روزرسانی و لینک گزارش شود.

### D10 — شمارنده صف معنای صف واقعی/پرداخت را ندارد
- [x] وضعیت: انجام‌شده | P1 | گزارش 10 | نتیجه: قطعی منطق | وابستگی: D06/D07.
- شواهد اجرای 2026-10-01: QueueCount اکنون برابر صف مشتق پذیرش است (Appointment LEFT JOIN ReceptionQueueEntry + walk-in): بدون entry فقط Scheduled/Confirmed/CheckedIn/InVisit، با entry فقط Waiting/Called/InVisit؛ Done/Left/Cancelled/NoShow حذف؛ scope پزشک با DoctorUserId. «صف پرداخت» حذف و با «در انتظار پرداخت» (Posted+PartiallyPaid) جایگزین شد؛ «فاکتورهای باز (شامل پیش‌نویس)» با تعریف صفحه صندوق (Draft/Posted/PartiallyPaid) یکی است و با `GetTabCountsAsync.Open` برابر تست شد. تست `QueueAndPaymentCounters_MatchAuthoritativeStates_NotScheduledAppointments` (walk-in/appointment/entry Done، cancelled، پزشک دیگر، Draft/Posted/Partial/Paid) و مقایسه با خروجی `ReceptionQueueQueries`. Budget: دو aggregate جدید (queue، walk-in) و ادغام شمارش فاکتورها؛ سقف command: Manager/Cashier/Receptionist 12، Doctor 8 (D22/D24 دوباره می‌سنجند). Budget+Authorization: **41 passed، 0 failed، 21 ثانیه**. مرز روز با ClinicCalendar window (نه UTC date) است.
- شواهد: `DashboardQueries.cs:69–81`؛ عنوان cashier در mapping خط 68؛ `ReceptionQueueQueries.cs:19–55`؛ `QueueState`؛ `CashierQueries.GetInvoicesAsync/GetTabCountsAsync`؛ handoffهای آماده billing.
- اقدام: شمارنده صف فعال پذیرش/پزشک از derived queue مشترک؛ فاکتورهای در انتظار پرداخت و ready-to-bill جدا. تعریف «فاکتور باز» با صفحه صندوق از جمله Draft یکسان و عنوان دقیق شود؛ موعد روز با timezone کلینیک باشد، نه UTC date.
- پذیرش/بررسی محدود: walk-in، appointment با entry جدید، Waiting/Called/InVisit/Done/Left، cancelled/no-show، draft/partial/paid و handoff آماده fixture کوچک داشته باشند؛ عدد dashboard با query مرجع همان صفحه برابر باشد؛ Scheduled به‌عنوان صف پرداخت نمایش داده نشود.
- پایان تسک: پس از بررسی محدود و sync با main، حتماً اجرا شود: `bash scripts/git/push.sh "fix(dashboard): D10 align queue counters with authoritative state"`؛ سپس PR به‌روزرسانی و لینک گزارش شود.

### D11 — low-stock با reorder level و active بودن سازگار نیست
- [x] وضعیت: انجام‌شده | P1 | گزارش 11 | نتیجه: count محصولات فعال با variant فعال در/زیر reorder در SQL مشترک.
- شواهد اجرای جدید: `InventoryStockQuery.LowStockProducts` در catalog کمبود و dashboard reuse شد؛ CountAsync بدون materialize محصولات، absent snapshot=0، دو variant کمبود=یک محصول و inactive product/variant حذف می‌شوند. regression واقعی `LowStock_CountsActiveProductsOnce_AtVariantReorderThreshold` همراه چهار budget role: **5 passed، 0 failed، 7 ثانیه**. آزمون اول مشکل مقایسه TEXT decimal در SQLite برای مقدار برابر را آشکار کرد و cast عددی صریح آن را اصلاح کرد؛ این شکست پنهان نشد. تغییرات پوش و PR [#181](https://github.com/Arian-Alijani/Petjan/pull/181) به‌روز شد؛ migration/ledger/reset دیتابیس کاربر انجام نشد.
- شواهد: `DashboardQueries.cs:59–60`؛ `InventoryQueries.cs:164–169,182–192`.
- اقدام: low-stock مشترک برای active product دارای حداقل یک active variant با `onHand <= reorderLevel`؛ نبود snapshot معادل صفر. واحد پیشنهادی مطابق inventory «کالای دارای کمبود» است، نه تعداد snapshotها. projection aggregate در SQL یا read model maintained؛ خواندن تمام محصولات برای شمارش ممنوع.
- پذیرش/بررسی محدود: stock=2/reorder=10، مقدار برابر حد، absent snapshot، inactive product/variant و دو variant کمبود یک محصول؛ تعداد کالای یکتا و همان نتیجه inventory بررسی شود.
- پایان تسک: پس از بررسی محدود و sync با main، حتماً اجرا شود: `bash scripts/git/push.sh "fix(dashboard): D11 share the low stock definition"`؛ سپس PR به‌روزرسانی و لینک گزارش شود.

### D12 — workload نام و completed واقعی ندارد
- [x] وضعیت: انجام‌شده | P1 | گزارش 12 | نتیجه: قطعی | وابستگی: D06.
- شواهد اجرای 2026-10-01: پذیرش کد remote بررسی و gapهای واقعی بسته شد: ترتیب پایدار (نام ordinal سپس DoctorId؛ بدون‌نام آخر)، پزشک حذف‌شده/بدون‌نام «پزشک نامشخص»، initial با text-element (نه نیمه surrogate)، Done کلمپ به Total، و specialty جعلی «پزشک» حذف شد (خالی). تست `DoctorWorkload_UsesRealNamesAndCompletedState_WithStableOrdering` با User واقعی، Completed/Scheduled/Cancelled، پزشک حذف‌شده، نوبت بدون پزشک، نسبت صفر/نیمه/کامل و نام emoji. Budget+Authorization: **42 passed، 0 failed، 21 ثانیه**.
- شواهد: `DashboardQueries.cs:73–78`؛ mapping خط 94–95؛ `ManagerDashboardView.axaml:179–227`؛ User.DisplayName و AppointmentState موجودند.
- اقدام: نام User و initial از text element صحیح؛ Done از Completed واقعی؛ denominator با تعریف صریح workload، بدون cancelled/no-show. تعداد/نام در projection bounded مشترک استخراج شوند؛ specialty ناموجود جعل نشود و وضعیت doctor حذف‌شده نمایش روشن داشته باشد.
- پذیرش/بررسی محدود: done/total با چند وضعیت معتبر درست، نام به‌جای Id، doctor بدون کار و doctor بدون نام معتبر سیاست مشخص؛ تست نسبت صفر/کامل. هیچ Done=0 ثابت نماند.
- پایان تسک: پس از بررسی محدود و sync با main، حتماً اجرا شود: `bash scripts/git/push.sh "fix(dashboard): D12 show real doctor workload"`؛ سپس PR به‌روزرسانی و لینک گزارش شود.

### D13 — refund به دریافت اضافه می‌شود
- [x] وضعیت: انجام‌شده | P0 | گزارش 13 | نتیجه: دریافت signed از Payment معتبر و refund با علامت صحیح.
- شواهد اجرای جدید: `Receipts_UseRealSignedPayments_AndRefundLiveRebuildAgree` از Domain واقعی Invoice.ApplyRefund، Payment مثبت/منفی، writer زنده و maintenance rebuild روی SQLite موقت استفاده می‌کند؛ gross=100/refund=30/net=70، refund روز بعد، مرز نیمه‌باز روز کلینیک و حذف USD از جمع IRT تأیید شد. مقدار gross/refund/currency در read model و عنوان تومان/خالص در KPI حفظ شد؛ adjustment دوباره شمرده نمی‌شود. فیلتر `DashboardBudgetTests|ActivityFeedTests`: **22 passed، 0 failed، 11 ثانیه**؛ warning تاریخی CS8625 باقی است؛ Full build/test اجرا نشد. commit کد `8187355` پوش شد و PR [#181](https://github.com/Arian-Alijani/Petjan/pull/181) ایجاد شد. D14 مستقل باقی است.
- شواهد: `ActivityFeedMapper.cs:61–62,83–87`؛ `DashboardQueries.cs:48`؛ `Invoice.cs:668–670` رویداد payment منفی تولید می‌کند.
- اقدام: magnitude+sign projection صحیح حفظ شود و KPI از Payment/read model معتبر، signed aggregation بگیرد؛ gross receipt، refund و net با اسم روشن. مبلغ refund invoice و reversing payment دو بار در net جمع نشوند؛ ledger/Payment append-only تغییر نکنند. اصلاح داده projection قبلی فقط با rebuild ایزوله و مسیر maintenance موجود، نه update ledger.
- پذیرش/بررسی محدود: receipt=100/refund=30 → gross=100، refund=30، net=70؛ refund روز دیگر، چند پرداخت، واحد پول و مرز روز بررسی شوند؛ live/rebuild علامت یکسان بدهند. تست Domain refund واقعی و query لازم است، نه فقط عدد دستی feed.
- پایان تسک: پس از بررسی محدود و sync با main، حتماً اجرا شود: `bash scripts/git/push.sh "fix(dashboard): D13 preserve signed payment refunds"`؛ سپس PR به‌روزرسانی و لینک گزارش شود.

### D14 — Credit به‌عنوان دریافت واقعی شمرده می‌شود
- [x] وضعیت: انجام‌شده | P1 | گزارش 14 | نتیجه: قاعده مشترک signed غیرCredit در داشبورد/صندوق | وابستگی: D13.
- شواهد اجرای جدید: `NonCreditSettlements_DashboardAndCashierShareSignedCurrencyRule` روی SQLite واقعی هر شش روش Cash/Card/Transfer/Cheque/Wallet/Insurance را با +100/-30 و Credit/unknown را با صفر تأیید کرد. `PaymentReceiptQuery.NonCreditSettlements` currency=IRT و بازه نیمه‌باز مشترک دارد. عنوان KPI «خالص تسویه غیرنسیه امروز (تومان)» است: Cheque شامل چک وصول‌نشده، Wallet مصرف پیش‌پرداخت و Insurance ثبت تسویه بیمه است؛ هیچ‌کدام به‌عنوان ورود نقد صندوق ادعا نمی‌شود. method/currency در args فعالیت حفظ شدند. فیلتر `DashboardBudgetTests|DashboardAuthorizationTests`: **35 passed، 0 failed، 17 ثانیه**؛ warning تاریخی CS8625 باقی است. کد پوش و PR [#181](https://github.com/Arian-Alijani/Petjan/pull/181) به‌روز شد؛ ledger writer/worker اضافه نشد.
- شواهد: `Payment.cs:22–23`؛ `DashboardQueries.cs:48`؛ mapper روش را حذف می‌کند؛ `FinanceProjectionStore.cs:51` Credit را کنار می‌گذارد؛ `CashierQueries.GetStatsAsync` نیز فعلاً همه روش‌ها را جمع می‌کند.
- اقدام: تعریف مشترک receipt از Payment معتبر که Credit را دریافت نمی‌داند؛ method/currency لازم در read model حفظ شوند. معنای Cheque وصول‌نشده، Wallet و Insurance صریح و عنوان KPI متناسب باشد؛ «ثبت تسویه غیرCredit» با «ورود نقد به صندوق» یکی فرض نشود. درآمد فروش جای receipt استفاده نشود.
- پذیرش/بررسی محدود: فقط Credit → دریافت واقعی صفر؛ Cash/Card/Transfer و reverse معتبر نتیجه signed صحیح؛ روش‌های غیرنقدی بر اساس تعریف مصوب تست شوند؛ KPI داشبورد/صندوق قاعده مشترک داشته باشند، بدون ledger writer یا worker دوم.
- پایان تسک: پس از بررسی محدود و sync با main، حتماً اجرا شود: `bash scripts/git/push.sh "fix(dashboard): D14 exclude credit from real collections"`؛ سپس PR به‌روزرسانی و لینک گزارش شود.

### D15 — عنوان «چک‌ها» برای شمارش event مبهم است
- [x] وضعیت: انجام‌شده | P2 | گزارش 15 | نتیجه: KPI با عنوان «تغییر وضعیت چک امروز».
- شواهد اجرای جدید: regression واقعی `ChequeKpi_CountsTodayStateChanges_NotDistinctCheques_AndRequiresPermission` یک چک با سه event امروز + یکی روز قبل را به عدد 3 و عنوان دقیق نگاشت کرد؛ deny مجوز، widget و شمارش چک را حذف کرد. **1 passed، 0 failed، 4 ثانیه**؛ بازه clinic day موجود حفظ شد و KPI سررسید جدید ساخته نشد. کد و پلن با اسکریپت پوش، PR [#181](https://github.com/Arian-Alijani/Petjan/pull/181).
- شواهد: `DashboardQueries.cs:51`؛ `DashboardQueryService.cs:69`؛ یک چک چند `ChequeStateChangedEvent` می‌سازد.
- اقدام کم‌هزینه این فاز: حفظ شمارش عملیات روز و تغییر عنوان به «تغییر وضعیت چک امروز»، permission مربوطه و روز کلینیک. KPI سررسید/pending جدید فقط در صورت درخواست مستقل ساخته شود؛ بی‌دلیل دامنه توسعه نیابد.
- پذیرش/بررسی محدود: یک چک با سه تغییر وضعیت → 3 تغییر، نه 3 چک؛ عنوان و caption دقیق؛ تغییر روز قبل وارد عدد امروز نشود؛ کاربر بدون ChequesRead widget نگیرد.
- پایان تسک: پس از بررسی محدود و sync با main، حتماً اجرا شود: `bash scripts/git/push.sh "fix(dashboard): D15 label cheque activity accurately"`؛ سپس PR به‌روزرسانی و لینک گزارش شود.

### D16 — ردیف‌ها کلید فنی/Id و icon مبتنی بر متن دارند
- [x] وضعیت: انجام‌شده | P1 | گزارش 16 | نتیجه: قطعی presentation | وابستگی: D06.
- شواهد اجرای 2026-10-01: DashboardListItem اکنون Kind/Status/TitleKey پایدار دارد؛ Desktop با `HistoryText.TryTitle` ترجمه می‌کند و icon را از Kind انتخاب می‌کند (resolver مبتنی بر Contains متن فارسی حذف شد). عنوان fallback هرگز کلید `history.*` یا Id داخلی نیست («رویداد …»)؛ نام بیمار/مالک ناموجود «بیمار نامشخص/مالک نامشخص»؛ نوع ناشناخته/نام خالی crash نمی‌کند و status واقعی (Cancelled/Posted/Paid/Due) در DTO می‌ماند. تست `Rows_CarryTypedKindStatusAndNeverExposeKeysOrInternalIds` + بروزرسانی mapping test؛ Budget+Authorization: **43 passed، 0 failed، 20 ثانیه**؛ build فقط Desktop: **0 warning/0 error**.
- شواهد: `DashboardQueryService.cs:101–103`؛ `DashboardRowViewModel.cs:41–74`؛ `Desktop/Localization/HistoryText.cs` الگوی کلید ترجمه دارد، اما Detail فعلی می‌تواند Id خام نشان دهد.
- اقدام: DTO typed با kind/status، reference نمایشی، نام بیمار/مالک/شماره فاکتور و translation key؛ ترجمه و icon mapping در Desktop با نوع ثابت، نه Contains متن فارسی. برای نام ناموجود fallback امن و قابل‌فهم، نه internal Id؛ status واقعی از read model عملیات یا event در activity بیاید.
- پذیرش/بررسی محدود: cancelled/paid/unpaid و چند نوع ردیف، فارسی خوانا و icon صحیح؛ تغییر زبان title، icon را تغییر ندهد؛ null/طول بلند و unknown type crash نکنند؛ کلید `history.*` و Id داخلی پیش‌فرض روی UI دیده نشوند.
- پایان تسک: پس از بررسی محدود و sync با main، حتماً اجرا شود: `bash scripts/git/push.sh "fix(dashboard): D16 render typed localized list rows"`؛ سپس PR به‌روزرسانی و لینک گزارش شود.

### D17 — گونه‌ها نام/icon و رنگ متمایز ندارند
- [x] وضعیت: انجام‌شده | P2 | گزارش 17 | نتیجه: قطعی presentation.
- شواهد اجرای 2026-10-01: DashboardSpecies اکنون Key پایدار دارد؛ نام فارسی/icon/رنگ در Desktop از `SpeciesPresentation` (Dog/Cat/Bird/Rabbit/Rodent/Reptile هر یک رنگ و icon مستقل؛ Other/ناشناخته خاکستری خنثی با fallback name) می‌آید و resolver قدیمی مبتنی بر نام ترکیبی «خرگوش و جوندگان» حذف شد. ترتیب legend/ring قطعی است (count نزولی سپس key ordinal) و به ترتیب ورودی وابسته نیست. تست `Species_UseStableKeyPalette_AndDeterministicOrder` (شش رنگ متمایز، unknown، ترتیب forward/reversed، empty و single). Budget+Authorization: **44 passed، 0 failed، 22 ثانیه**؛ build فقط Desktop: **0 warning/0 error**.
- شواهد: `DashboardQueries.cs:64–66`؛ mapping خط 96؛ `DashboardSpeciesViewModel.cs:33–34,87–94`.
- اقدام: species key مستقل از نام فارسی، icon و palette ثابت از presentation/tokens؛ ترتیب deterministic با سیاست گونه‌های ناشناخته. backend متن فارسی یا hex را تعیین نکند؛ aggregation active pet و order legend/ring یکسان حفظ شوند.
- پذیرش/بررسی محدود: Dog/Cat/Bird/Rodent/Other فارسی و icon مشخص داشته باشند؛ رنگ گونه‌های متفاوت متمایز؛ ترتیب با جابه‌جایی ترتیب ورودی عوض نشود؛ unknown species و empty list آزموده شوند.
- پایان تسک: پس از بررسی محدود و sync با main، حتماً اجرا شود: `bash scripts/git/push.sh "fix(dashboard): D17 map species to a stable localized palette"`؛ سپس PR به‌روزرسانی و لینک گزارش شود.

### D18 — chart درآمد منفی را حذف/تحریف می‌کند
- [x] وضعیت: انجام‌شده | P1 | گزارش 18 | نتیجه: قطعی + بازتولید محاسباتی.
- شواهد اجرای 2026-10-01: نرمال‌سازی نمودار اکنون روی دامنه signed شامل صفر انجام می‌شود (`DashboardMapping.Normalize`)؛ مقدار واقعی هر روز در `RevenuePulse.Values` (frozen) و `ZeroLevel` در قرارداد حفظ شد، چیدمان در `RevenueChartMath` (بدون clamp اطلاعات، NaN/Infinity → عدم رسم)، area تا خط صفر بسته می‌شود و خط صفر صریح در View رسم شد. تست‌ها: mixed-sign، all-negative، all-zero، single point، مقدار ±9e18، ترتیب/مکان نسبت به خط صفر، empty و NaN؛ Budget+Authorization: **50 passed، 0 failed، 20 ثانیه**؛ build فقط Desktop: **0 warning/0 error**. screenshot GUI گرفته نشد (تأییدنشده).
- شواهد: mapping خطوط 87–93؛ `DashboardPageViewModel.cs:249–290`؛ reversal در `FinanceProjectionStore.cs:36–42` ممکن است revenue منفی بدهد.
- اقدام: مبلغ واقعی signed در قرارداد حفظ و domain نمودار از min/max با احتساب صفر تعیین شود؛ خط صفر مشخص، area به baseline صفر بسته شود؛ کل series منفی/صفر بدون divide-by-zero و بدون clamp اطلاعات مالی رسم شود. geometry فقط هنگام تغییر داده ساخته شود.
- پذیرش/بررسی محدود: mixed-sign، all-negative، all-zero، single/empty point، مقدار بزرگ؛ ترتیب و مکان نسبت به خط صفر و عدم NaN/Infinity تست شود؛ نمودار منفی نباید شبیه مثبت/صفر شود. screenshot کوتاه فقط برای همین chart، بدون bench کامل.
- پایان تسک: پس از بررسی محدود و sync با main، حتماً اجرا شود: `bash scripts/git/push.sh "fix(dashboard): D18 render signed revenue around zero"`؛ سپس PR به‌روزرسانی و لینک گزارش شود.

### D19 — cache version همه منابع و projection مالی را پوشش نمی‌دهد
- [x] وضعیت: انجام‌شده | P1 | گزارش 19 | نتیجه: فقدان invalidation قطعی؛ race محتمل، در runtime این جلسه بازتولید نشده | وابستگی: D06/D13/D14.
- شواهد اجرای 2026-10-01: migration افزایشی `021_DashboardGeneration` (جدول `DashboardGenerations` با کلید TenantId + Designer/snapshot؛ migrationهای قبلی دست‌نخورده) و triggerهای SQLite روی insert/update/delete همه منابع داشبورد (facts، ActivityEvents، Payments، Invoices، Appointments، ReceptionQueue، Visits، Vaccination، MedicalHistory، billing handoffs، Owners/Pets/Users، Products/Variants/StockOnHandSnapshots) نسل tenant را داخل همان transaction جلو می‌برند؛ rollback نسل را تغییر نمی‌دهد و نوشتن تأخیری worker/rebuild آن را جلو می‌برد. `VersionAsync` دیگر آخرین ActivityEvent.Id نیست، بلکه lookup کلید اصلی نسل است. snapshot اکنون `Freshness` (زمان تولید، تعداد outbox/projection در انتظار، وضعیت dead-letter) را حمل می‌کند تا facts عقب‌مانده «آماده» جلوه نکنند. تست‌های جدید `DashboardGenerationTests` (4 مورد: facts و OLTP بدون feed، tenant isolation، rollback، cache invalidation با service واقعی، lag/failed): **4 passed**؛ `DashboardBudgetTests|DashboardAuthorizationTests|DashboardGenerationTests` **59 از 60 passed** در اجرای نخست و مورد ناموفق (فرضِ تست درباره no-op fact) اصلاح و مجدداً **4/4 passed**؛ `LocalMigrationTests|StoreStartupTests`: **14 passed، 0 failed**. budget Manager/Receptionist از 12 به 13 رسید (یک probe lag outbox، bounded). Full build/test اجرا نشد.
- شواهد: `DashboardQueries.VersionAsync` فقط آخرین ActivityEvent.Id؛ `DashboardQueryService.cs:39–46`؛ `FinanceProjectionWorker.cs:35–64` fact را بعداً بدون ActivityEvent جدید ذخیره می‌کند.
- اقدام: generation پایدار tenant-scoped برای منابع مؤثر، شامل تغییر/rebuild projection مالی، با افزایش داخل همان transaction؛ schema فقط در صورت ضرورت. snapshot زمان داده و lag/وضعیت projection را حمل کند. watermark از highest Id/آخرین event به‌تنهایی ساخته نشود؛ پردازش ممکن است out-of-order باشد. polling دائمی سنگین یا hash کل جداول ممنوع.
- پذیرش/بررسی محدود: عملیات مالی commit، snapshot قبل worker، فقط یک pass کنترل‌شده worker، سپس همان روز/هویت بدون event جدید؛ cache داده تازه دهد. تغییر appointment/stock/pet بدون feed جدید و rebuild مربوطه cache را invalid کند؛ rollback generation را جلو نبرد؛ خطای worker با داده آماده اشتباه نشود.
- پایان تسک: پس از بررسی محدود و sync با main، حتماً اجرا شود: `bash scripts/git/push.sh "fix(dashboard): D19 invalidate cache on all source generations"`؛ سپس PR به‌روزرسانی و لینک گزارش شود.

### D20 — TTL بدون refresh، UI را تازه نمی‌کند
- [x] وضعیت: انجام‌شده | P1 | گزارش 20 | نتیجه: نبود policy قطعی؛ ماندن ساعت‌ها در GUI مشاهده نشده | وابستگی: D04/D25/D37/D38.
- شواهد اجرای 2026-10-01: `RefreshSnapshotAsync` cache داده را دور می‌زند، اما policy زنده همچنان اجرا می‌شود؛ دکمه همیشگی refresh، timer یک‌دقیقه‌ای فقط هنگام activation و توقف/cancel هنگام خروج، و زمان آخرین دریافت موفق اضافه شد. `DashboardLifecycleTests`: **14 passed، 0 failed، 8 ثانیه** شامل timestamp تزریقی عبور روز، coalescing و غیرفعال‌شدن tick، و service واقعی با cache گرم. build فقط Desktop: **0 warning/0 error، 57 ثانیه**. warning تاریخی CS8625 تست باقی است. Full build/test اجرا نشد. PR [#185](https://github.com/Arian-Alijani/Petjan/pull/185).
- شواهد: `DashboardPageViewModel.cs:61–89`؛ TTL خط 46 service؛ XAML فقط retry در error دارد.
- اقدام: manual refresh همیشگی، activation refresh و policy محدود هنگام active بودن؛ tick/data-change coalesced، عبور روز کلینیک و last-updated. timer/subscription هنگام خروج متوقف شود. refresh دستی و freshness از TTL مستقل باشند و راهی برای درخواست تازه واقعی داشته باشند؛ worker/UI polling بی‌حد ساخته نشود.
- پذیرش/بررسی محدود: ساعت/زمان تزریقی یا fake scheduler، بدون wait واقعی 60ثانیه؛ tick فعال و عبور نیمه‌شب refresh کنند، deactivated نکند؛ چند trigger concurrent یک بار query؛ manual refresh cache معتبر اما قدیمی را نیز با policy درست تازه کند.
- پایان تسک: پس از بررسی محدود و sync با main، حتماً اجرا شود: `bash scripts/git/push.sh "fix(dashboard): D20 add an activation scoped refresh policy"`؛ سپس PR به‌روزرسانی و لینک گزارش شود.

### D21 — چند statement snapshot یک لحظه مشترک ندارند
- [x] وضعیت: انجام‌شده | P2 | گزارش 21 | نتیجه: ریسک طراحی قطعی، ناهمخوانی واقعی concurrent در این جلسه مشاهده نشده | وابستگی: D19 و queryهای نهایی.
- شواهد اجرای 2026-10-01: `DashboardQueries.GetSliceAsync` اکنون همه readها و generation را داخل یک read transaction کوتاه SQLite (`BEGIN DEFERRED`، نه IMMEDIATE؛ بدون قفل نوشتن و بدون مسدودکردن worker/command) اجرا می‌کند؛ snapshot از اولین read (generation) ثابت می‌شود و transaction هرگز commit نمی‌شود، فقط در پایان/خطا/cancel رها می‌شود. در صورت transaction بیرونی همان استفاده می‌شود (بدون nest). `DashboardSlice.Generation` نسل همان state را حمل می‌کند و service نتیجه را زیر همان نسل cache می‌کند (نه مقدار probe قدیمی)؛ lag projection جدا در `Freshness` گزارش می‌شود (transaction lag را درمان نمی‌کند). تست‌ها با دو connection واقعی و barrier روی statement سوم (بدون sleep): writer وسط reads هم fact و هم بیمار جدید commit می‌کند؛ slice اول کاملاً pre-write و با generation قبلی است (بدون ترکیب دو commit)، probe بعدی نسل جدید را می‌بیند و slice بعدی کاملاً post-write است؛ cancel و exception transaction را رها می‌کنند (`CurrentTransaction == null`)، writer بعدی قفل نمی‌شود و ambient transaction nest نمی‌شود. `DashboardGenerationTests|DashboardBudgetTests|DashboardAuthorizationTests|DashboardLifecycleTests`: **84 passed، 0 failed، 30 ثانیه** (budget هر نقش +1 statement برای generation داخل snapshot). مسیر WAL/startup تغییر نکرد؛ Full build/test اجرا نشد.
- شواهد: `DashboardQueries.GetSliceAsync`؛ `TransactionBehavior.cs:24–25` query را transaction نمی‌کند.
- اقدام: read transaction کوتاه در لایه persistence برای مجموعه reads و generation همان snapshot، با semantics واقعی SQLite بررسی‌شده؛ در صورت transaction بیرونی، آن را بی‌دلیل nest نکنید. بین انتظار UI/lock/semaphore و DB transaction همپوشانی طولانی نباشد. OLTP و fact عقب‌مانده با metadata lag مشخص شوند؛ transaction به‌تنهایی lag را درمان نمی‌کند.
- پذیرش/بررسی محدود: دو connection DB موقت، writer کنترل‌شده بین دو read؛ snapshot از دو commit مخلوط نباشد و metadata نسل همان data باشد. اینترلیو با barrier، نه sleep/soak؛ cancellation/exception transaction را release کنند و مسیر WAL local startup عوض نشود.
- پایان تسک: پس از بررسی محدود و sync با main، حتماً اجرا شود: `bash scripts/git/push.sh "fix(dashboard): D21 read consistent short lived snapshots"`؛ سپس PR به‌روزرسانی و لینک گزارش شود.

### D22 — probe نسخه با تاریخچه رشد می‌کند
- [x] وضعیت: انجام‌شده | P2 | گزارش 22 | نتیجه: ریسک عملکرد + plan نمونه؛ کندی واقعی اثبات نشده | وابستگی: D19/D21.
- شواهد اجرای 2026-10-01: با D19 probe تاریخچه حذف شد: `VersionAsync` یک statement روی `DashboardGenerations` است (کلید اصلی TenantId) و دیگر ActivityEvents را نمی‌خواند؛ index جدید لازم نبود. شاهد plan از SQL واقعی EF (interceptor) روی fixture قطعی دو‌tenant با 10000 ردیف تاریخچه: دقیقاً یک statement، بدون ORDER BY، plan برابر `SEARCH ... USING INDEX sqlite_autoindex_DashboardGenerations (TenantId=?)` و بدون SCAN/TEMP B-TREE؛ نسخه دقیقاً برابر 5000 insert همان tenant. قبل: `SELECT Id FROM ActivityEvents ... ORDER BY Id DESC` با temp sort (plan نمونه تاریخی همین پلن)؛ بعد: lookup کلید اصلی. این شاهد plan است، نه ادعای latency production. تست `VersionProbe_IsOneStatement_AndAPrimaryKeySearch_IndependentOfHistorySize`: `DashboardGenerationTests` **5 passed، 0 failed، 12 ثانیه**.
- شواهد: `DashboardQueries.cs:15–17`؛ `ActivityConfigurations.cs:18–23`؛ plan نمونه این جلسه temp sort داشت.
- اقدام: اگر D19 version row ایجاد کرد، probe history حذف شود و این تسک فقط اثبات مسیر bounded نهایی باشد. در غیراین‌صورت SQL واقعی EF استخراج، index مناسب `(TenantId,Id)` با توجیه و migration افزایشی بررسی شود؛ index بلااستفاده پس از تغییر معماری اضافه نشود.
- پذیرش/بررسی محدود: EXPLAIN روی SQL واقعی probe نهایی، fixture deterministic کوچک و حداکثر 10000 ردیف؛ no tenant-wide sort/scan برای probe، بدون long benchmark. وضعیت قبل/بعد و شمار statement ثبت شود؛ synthetic plan به‌عنوان production latency معرفی نشود.
- پایان تسک: پس از بررسی محدود و sync با main، حتماً اجرا شود: `bash scripts/git/push.sh "perf(dashboard): D22 bound the cache version probe"`؛ سپس PR به‌روزرسانی و لینک گزارش شود.

### D23 — indexها با day/doctor query نهایی هماهنگ نیستند
- [x] وضعیت: انجام‌شده | P2 | گزارش 23 | نتیجه: ریسک عملکرد/پوشش index | وابستگی: D06/D07/D09/D10/D12/D22.
- شواهد اجرای 2026-10-01: SQL واقعی EF همه statementهای widget هر چهار نقش با interceptor گرفته و با EXPLAIN QUERY PLAN روی fixture قطعی دو‌tenant بررسی شد (قبل: preview/totals فعالیت روی ایندکس OccurredUtc با USE TEMP B-TREE FOR ORDER BY، queue با join بدون ایندکس، outbox SCAN، handoff آماده SCAN و snapshot انبار SCAN). اصلاح: migration افزایشی `_022_DashboardWidgetIndexes` با ۵ ایندکس هدف‌دار (ActivityEvents (TenantId,LocalDay,Id)؛ Visits (TenantId,DoctorUserId,CompletedUtc)؛ VaccinationRecords (TenantId,AdministeredUtc)؛ ReceptionQueueEntries (TenantId,AppointmentId) فقط برای AppointmentId غیرنال تا walk-in همچنان از ایندکس State/EnqueuedUtc استفاده کند؛ MedicalHistoryEntries (TenantId,SourceEntityId)) + Designer/snapshot؛ فیلتر tenant به snapshot انبار و handoffهای آماده افزوده شد و probe outbox از `<> Succeeded` به لیست ثابت وضعیت‌های باز تبدیل شد (SEARCH روی ایندکس). نتیجه plan: preview بدون TEMP B-TREE و فقط SEARCH روی (TenantId, LocalDay)؛ هیچ SCAN جدولی در هیچ نقشی (فقط co-routine محدود UNION)؛ پزشک روی ایندکس‌های doctor/time. `DashboardPlanTests` (۴ نقش + isolation tenant/day با ۶۰۰ ردیف دو‌tenant): **5 passed**؛ `Dashboard*|ActivityFeed|Sales|Inventory` در Application.Tests: **105 passed، 0 failed**؛ Persistence (migration/handoff/queue): **13 passed**. این شاهد plan است نه latency production؛ index قدیمی تغییر نکرد.
- شواهد: activity query فعلی روی LocalDay/ActorUserId و Id است؛ سه index موجود روی OccurredUtc/Kind/entity. actor index پیشنهادی قدیمی بعد از D07 الزاماً لازم نیست.
- اقدام: فقط SQL نهایی widgets بررسی شود؛ predicateهای tenant/day/assigned doctor/state/order با index مناسب، نه اضافه‌کردن همه ترکیبات احتمالی. query UTC range و indexهای موجود scheduling/queue ابتدا ارزیابی شوند؛ migration افزایشی و مدل یکسان، بدون بازنویسی indexهای تاریخی.
- پذیرش/بررسی محدود: EF `ToQueryString`/interceptor و EXPLAIN قبل/بعد روی fixture bounded چندtenant/doctor؛ no full history scan برای widget روز، sort فقط روی نتیجه bounded در صورت توجیه؛ چک translation و tenant isolation در همان regression. load test حجیم/طولانی ممنوع.
- پایان تسک: پس از بررسی محدود و sync با main، حتماً اجرا شود: `bash scripts/git/push.sh "perf(dashboard): D23 index the final widget read paths"`؛ سپس PR به‌روزرسانی و لینک گزارش شود.

### D24 — تست budget مسیر واقعی service/auth را نمی‌شمارد
- [x] وضعیت: انجام‌شده | P2 | گزارش 24 | نتیجه: نقص قطعی test coverage | وابستگی: D01/D03 و queryهای نهایی.
- شواهد اجرای 2026-10-01: تست جدید `DashboardPipelineBudgetTests` کل مسیر تولیدی (DashboardQueryService ← session محلی ← مجوز زنده membership/role ← probe نسل ← dispatcher ← queries) را با host واقعی و interceptor روی همه scope/connectionهای یک call می‌شمارد؛ seed بیرون از پنجره سنجش است و auth حذف نشده. سنجش واقعی نشان داد هر call مجوز ویجت‌ها را با ۹ بار خواندن membership+role می‌گرفت (قبل: miss ‏52–54، hit ‏20–22 دستور). اصلاح: `IPermissionEvaluator.AllowedAsync` (یک خواندن ذخیره‌شده برای همه کدها با همان قواعد fail-closed و grant/deny؛ پیاده‌سازی پیش‌فرض interface حلقه‌ای است) و استفاده در `ResolveAccessAsync`. بعد: miss ‏20–24، hit ‏6 دستور؛ سقف‌ها per-role در تست (Doctor 23، Cashier 24، Receptionist 22، Manager 26؛ hit ≤ 6)، hit ثابت در فراخوانی‌های تکراری (بدون N+1)، hit فاقد خواندن ActivityEvents/Appointments، و membership در budget. زمان‌ها فقط ثبت می‌شوند و assertion نیستند (نمونه: hit ‏≈11–17ms، miss ‏≈70–410ms روی sandbox). `BatchedPermissions_MatchPerCodeDecisions` برابری با evaluator تک‌کدی را با grant/deny و کد ناشناخته تأیید می‌کند. `Dashboard*|Authorization|Permission|Capability|Staff`: **148 passed، 0 failed**. Full build/test اجرا نشد.
- شواهد: `DashboardBudgetTests.cs:57–65` مستقیماً queries را صدا می‌زند؛ production membership read در `PermissionAuthorizationGate` و repository اضافه می‌شود.
- اقدام: budget test محدود با DashboardQueryService، dispatcher، session محلی، RBAC و DI واقعی؛ interceptor تمام scope/connectionهای همان call را بشمارد. cache-hit/miss و roleهای واقعی جدا؛ هم command count و هم timestamp مراحل ثبت شوند. عدد قدیمی 8 بدون سنجش به مسیر کامل تحمیل نشود؛ حذف auth برای رسیدن به budget ممنوع.
- پذیرش/بررسی محدود: fixture تولیدی کوچکی که در production auth fail کند، در تست هم fail شود؛ hit و miss counts دارای bound مشخص و فاقد N+1؛ setup/seed از measurement کنار باشد. latency به‌تنهایی بر host اشتراکی assertion شکننده نشود؛ evidence محدود ثبت گردد.
- پایان تسک: پس از بررسی محدود و sync با main، حتماً اجرا شود: `bash scripts/git/push.sh "test(dashboard): D24 measure the production query pipeline"`؛ سپس PR به‌روزرسانی و لینک گزارش شود.

### D25 — cancellation/lifecycle تا DB منتقل نمی‌شود
- [x] وضعیت: انجام‌شده | P2 | گزارش 25 | نتیجه: نقص قطعی قرارداد.
- شواهد اجرای 2026-10-01: token اختیاری backward-compatible در `IDashboardQueryService.GetSnapshotAsync` به semaphore، `Task.Run`، access policy، version probe، dispatcher و EF منتقل شد؛ `PageViewModelBase.OnNavigatedFrom` و مسیر مرکزی `NavigationService.Publish` (هر سه overload) خروج قبلی را اعلام می‌کند و `DashboardRefreshCoordinator` نتیجه generation قدیمی را هرگز اعمال نمی‌کند (OCE بی‌صدا، busy نسل جدید با finally قدیمی پاک نمی‌شود). `DashboardLifecycleTests` (کنترل با TaskCompletionSource/barrier، بدون sleep) cancel پیش از کار، حین انتظار semaphore (بدون over-release، سریال‌بودن بعدی اثبات شد) و حین query را می‌آزماید. regression: `DashboardLifecycleTests|NavigationAdmissionTests` **19 passed، 0 failed، 8 ثانیه**؛ build فقط Desktop **0 warning/0 error، 51 ثانیه**؛ Full build/test اجرا نشد.
- شواهد: `IDashboardQueryService.cs:14` token ندارد؛ service انتظار semaphore با default؛ ViewModel نیز readiness/query بدون token؛ `PageViewModelBase` فقط OnNavigatedTo دارد.
- اقدام: token اختیاری backward-compatible در Contracts، انتقال به readiness/gate/semaphore/dispatcher/EF؛ activation/deactivation hook و generation latest-wins. NavigationService تمام overloadها خروج قبلی را اعلام کند؛ `Task.Run` برای SQLite sync از UI حفظ شود. SQLite cancellation ممکن است فوراً statement در حال اجرا را متوقف نکند، اما نتیجه قدیمی نباید اعمال شود.
- پذیرش/بررسی محدود: token canceled پیش از کار و هنگام انتظار semaphore؛ خروج و ورود مجدد در query کنترل‌شده؛ نتیجه فقط برای activation فعلی. OCE پیام failure نسازد؛ semaphore در مسیر cancel پیش از acquire over-release نشود؛ subscribers و CTS نشت نکنند.
- پایان تسک: پس از بررسی محدود و sync با main، حتماً اجرا شود: `bash scripts/git/push.sh "fix(dashboard): D25 propagate cancellation through activation"`؛ سپس PR به‌روزرسانی و لینک گزارش شود.

### D26 — failureهای typed به یک پیام عمومی تبدیل می‌شوند
- [x] وضعیت: انجام‌شده | P2 | گزارش 26 | نتیجه: قطعی | وابستگی: D25.
- شواهد اجرای 2026-10-01: `DashboardQueryException` در Contracts با session/permission/store/unexpected و correlation opaque؛ OCE دست‌نخورده. service failureهای context/policy/dispatcher/probe/mapping را با دسته امن log می‌کند؛ exception/payload/SQL/PII وارد log یا پیام نمی‌شود. denial/expiry داده قدیمی را پاک می‌کند؛ retry تنها store recovery. `DashboardLifecycleTests|DashboardAuthorizationTests`: **32 passed، 0 failed** (log محدود همین اجرا)، شامل 5 دسته پیام/unknown امن، session واقعی، revoke cache گرم و cancellation. guard نخست 60ثانیه هنگام compilation خاتمه یافت؛ incremental rerun با سقف 90ثانیه موفق بود، نه full test. PR [#185](https://github.com/Arian-Alijani/Petjan/pull/185).
- شواهد: `DashboardQueryService.cs:34,44`؛ `DashboardPageViewModel.cs:82–85`؛ version/mapping بیرون dispatcher هستند.
- اقدام: outcome typed یا exception مشخص با error code امن از Client.Services به Contracts؛ session expiry/permission/store/cancel/unexpected جدا. logging ساختاریافته با correlation برای probe/mapping failures، نه render/timer/per-item؛ خطای برنامه‌نویسی فقط retry generic پنهان نشود. JSON/Id/PII/stack خام به UI نرود.
- پذیرش/بررسی محدود: پنج حالت خطا پیام/رفتار متفاوت و correlation ثبت‌شده مناسب داشته باشند؛ cancel بی‌صدا؛ raw detail مالی/هویتی یا exception داخلی در پیام کاربر/log بدون redaction نباشد؛ retry فقط برای موارد قابل‌بازیابی.
- پایان تسک: پس از بررسی محدود و sync با main، حتماً اجرا شود: `bash scripts/git/push.sh "fix(dashboard): D26 preserve typed failure semantics"`؛ سپس PR به‌روزرسانی و لینک گزارش شود.

### D27 — آماده‌سازی snapshot می‌تواند UI را نیمه‌به‌روز بگذارد
- [x] وضعیت: انجام‌شده | P2 | گزارش 27 | نتیجه: ریسک ساختار Apply، شکست واقعی مشاهده نشده | وابستگی: D16/D17/D18.
- شواهد اجرای 2026-10-01: آماده‌سازی کامل presentation (rows، species، geometry، tooltip، totals، slices) پیش از هر تغییر state در `Prepare` انجام می‌شود و `Publish` با backing fieldها و یک notification (`OnPropertyChanged(string.Empty)`) state یکپارچه منتشر می‌کند؛ خطای preparation state و marker قبلی را حفظ می‌کند و cache-hit (همان snapshot) بازسازی/انتشار ندارد. تست `PreparationFailure_KeepsEntirePublishedStateAndAppliedMarker_ThenValidStateReplacesOnce` در `DashboardLifecycleTests`: **21 passed، 0 failed، 8 ثانیه** (فیلتر کلاس، بدون Full build/test). کد در PR #185 merge شده بود؛ این اجرا فقط شاهد و تیک پلن را ثبت می‌کند.
- شواهد: `DashboardPageViewModel.cs:222–244,293–315`؛ ساخت brush/geometry بعد از تغییر چند collection انجام می‌شود.
- اقدام: تمام presentation state، rows، geometry، species و formatted totals ابتدا آماده/validate شوند؛ سپس روی UI dispatcher با state واحد یا batch coherent منتشر شوند. ReplaceAll و reuse snapshot حفظ شوند. صرف چند assignment پشت‌سرهم ادعای atomic publication کامل نیست؛ برای observerها نیز ترتیب/یکپارچگی state تعریف شود.
- پذیرش/بررسی محدود: خطای preparation تزریق‌شده، تمام state قبلی و applied marker را سالم نگه دارد؛ state جدید valid کامل منتشر شود؛ cache-hit بدون تغییر snapshot rows/geometry تازه نسازد؛ stale marker در failure مشخص باشد.
- پایان تسک: پس از بررسی محدود و sync با main، حتماً اجرا شود: `bash scripts/git/push.sh "fix(dashboard): D27 prepare and publish coherent view state"`؛ سپس PR به‌روزرسانی و لینک گزارش شود.

### D28 — loading/empty/stale از صفر واقعی قابل‌تشخیص نیست
- [x] وضعیت: انجام‌شده | P2 | گزارش 28 | نتیجه: state صریح InitialLoading/Refreshing/Ready/Empty/Stale/Error؛ صفر واقعی از loading/خطا جدا شد. | وابستگی: D20/D26/D27.
- شواهد اجرای 2026-10-01: enum و resolver خالص `DashboardViewState` (Avalonia-free) + ViewModel: `HasData` (placeholder پس از reset/revocation داده نیست)، `ViewState/StateText`، اعلان موفقیت coordinator (`onLoaded`، شامل cache-hit) که LoadError را پاک می‌کند؛ خطای refresh با داده قبلی نگه داشته می‌شود و Stale (نه تازه) نمایش می‌یابد، خطای اولیه Error است. View: نوار وضعیت با ProgressBar/متن LiveSetting=Polite و empty-template برای همه فهرست‌ها/ورکلود/گونه (فقط پس از HasData). `DashboardViewStateTests` + `DashboardLifecycleTests`: **28 passed، 0 failed، 9 ثانیه**؛ build فقط Desktop: **0 warning/0 error، 57 ثانیه**. بررسی GUI واقعی (نمایشگر) در sandbox اجرا نشد.
- شواهد: `DashboardPageView.axaml:9–46`؛ IsBusy بدون نمایش؛ ItemsControlهای Manager/Doctor/Cashier empty template ندارند؛ error banner موجود است ولی انواع state را جدا نمی‌کند.
- اقدام: state صریح InitialLoading/Refreshing/Ready/Empty/Stale/Error و last-updated؛ خطای refresh داده قبلی را نگه دارد اما آن را تازه جلوه ندهد. empty/unavailable/no-permission هر widget با هم و با عدد صفر یکی نباشند؛ متن و progress accessible و بدون animation بی‌حد باشند.
- پذیرش/بررسی محدود: store خالی موفق → Empty، startup pending → Loading، refresh failure با data → Stale، initial failure → Error؛ صفر واقعی بعد از موفقیت ظاهر شود، نه پیش از query. فقط همین stateها با fake readiness/service و یک بررسی UI کوتاه.
- پایان تسک: پس از بررسی محدود و sync با main، حتماً اجرا شود: `bash scripts/git/push.sh "fix(dashboard): D28 expose loading empty and stale states"`؛ سپس PR به‌روزرسانی و لینک گزارش شود.

### D29 — layout در عرض کم/متن بلند reflow ندارد
- [x] وضعیت: انجام‌شده | P2 | گزارش 29 | نتیجه: reflow واقعی بر پایه عرض محتوا (پس از sidebar)؛ شدت DPI/clipping در GUI تأییدنشده.
- شواهد اجرای 2026-10-01: کنترل `AdaptiveGrid` + `AdaptiveGridMath` (خالص): تعداد ستون از عرض واقعی محتوا و MinItemWidth، wrap به ردیف بعد، وزن 1.3:1 فقط وقتی همه در یک ردیف‌اند؛ KPI (۴→۳→۲→۱)، tileهای دسترسی سریع، دو ردیف Manager و کارت‌های Doctor/Cashier stack می‌شوند. متن Title/Subtitle/Name/Label wrap می‌شود، سطر عدد درآمد WrapPanel شد، و MainWindow حداقل 960×600 دارد (scroll افقی روشن نشد). RTL/theme و صفحات دیگر دست‌نخورده. `DashboardResponsiveLayoutTests` (عرض‌های 1440/1100/960 با sidebar باز/بسته، clamp، عرض نامتناهی، وزن‌ها، وجود AdaptiveGrid در XAML) + `DashboardViewStateTests`: **22 passed، 0 failed**؛ build فقط Desktop: **0 warning/0 error، 56 ثانیه**. **تأییدنشده:** بررسی بصری در Windows 100%/150%/200% DPI و نام فارسی بلند واقعی (GUI/Windows در دسترس نبود) — پذیرش DPI باز.
- شواهد: UniformGrid چهارستونه در `DashboardPageView.axaml:23,61`؛ Gridهای ثابت Manager؛ donut عرض 200؛ MainWindow بدون MinWidth و horizontal scroll غیرفعال.
- اقدام: breakpoint/reflow براساس عرض واقعی content پس از sidebar؛ KPI/tileها کمترستونه و ردیف‌های Manager stack شوند. wrapping/trimming و حداقل عرض منطقی تعریف شوند؛ فقط افزایش MinWidth یا روشن کردن scroll افقی درمان کامل نیست. RTL/theme و layout سایر صفحات تغییر نکنند.
- پذیرش/بررسی محدود: سه عرض نماینده narrow/medium/wide، sidebar باز/بسته و یک نام فارسی بلند/مبلغ بزرگ؛ یک بررسی Windows 100%/150%/200% هنگام دسترسی. اگر Windows نیست، DPI acceptance باز بماند؛ screenshots فرضی را تأیید نکنید. Full screen sweep ممنوع.
- پایان تسک: پس از بررسی محدود و sync با main، حتماً اجرا شود: `bash scripts/git/push.sh "fix(dashboard): D29 reflow cards for constrained widths"`؛ سپس PR به‌روزرسانی و لینک گزارش شود.

### D30 — range tabs ظاهری و caption/value ناسازگارند
- [x] وضعیت: انجام‌شده | P2 | گزارش 30 | نتیجه: قطعی | وابستگی: D18.
- شواهد اجرای 2026-10-01: تب‌های inert روز/هفته/ماه/سال حذف شدند؛ عنوان «روند درآمد ۱۴ روز اخیر» و headline «درآمد امروز (تومان)» جدا از مجموع 14 روز است. تاریخ جلالی و مبلغ signed هر نقطه در DTO (`DayLabels/Values/Summary`) حفظ و به tooltip و AutomationProperties.Name متنی وصل شد. تست `RevenuePulse_ExposesActual14DayRange_...`: 14 روز zero-filled با پنجره روز کلینیک، تاریخ جلالی ابتدا/انتها، مبلغ منفی، caption و نبود تب inert در XAML. Budget+Authorization: **51 passed، 0 failed، 21 ثانیه**؛ build فقط Desktop: **0 warning/0 error**.
- شواهد: `ManagerDashboardView.axaml:106–137` inert Borderها؛ query همیشه 14روز؛ Pulse.Value امروز ولی Caption «14 روز اخیر»؛ قرارداد Points تاریخ ندارد.
- اقدام کم‌هزینه این فاز: tabs روز/هفته/ماه/سال حذف، بازه واقعی «روند درآمد ۱۴ روز اخیر» و مقدار headline «درآمد امروز» با واحد روشن نمایش یابد. تاریخ/مقدار واقعی points در DTO حفظ و tooltip/معادل متنی کوتاه داده شود؛ range selector جدید و queries چهار بازه بدون درخواست لازم نیست.
- پذیرش/بررسی محدود: هیچ کنترل غیرفعال شبیه interactive selection نماند؛ headline امروز با مجموع 14روز اشتباه نشود؛ 14 روز zero-filled با timezone و تاریخ درست؛ tooltip/متن واحد پول و مبلغ signed مطابق داده واقعی باشند.
- پایان تسک: پس از بررسی محدود و sync با main، حتماً اجرا شود: `bash scripts/git/push.sh "fix(dashboard): D30 expose the actual revenue chart range"`؛ سپس PR به‌روزرسانی و لینک گزارش شود.

### D31 — donut drill-in و focus دسترس‌پذیر نیستند
- [x] وضعیت: انجام‌شده | P2 | گزارش 31 | نتیجه: drill-in دونات Button/Command با نام automation و Enter/Space؛ فقط کلیک چپ؛ focus/blur کیبورد؛ screen-reader تأییدنشده. | وابستگی: D05/D17.
- شواهد اجرای 2026-10-01: دونات اکنون `Button` با `OpenSpeciesPatientsCommand` است (Tab/Enter/Space؛ PointerPressed خام و ناوبری با راست‌کلیک حذف شد) و مسیر ناوبری همان gate مرکزی D05 است. `AutomationProperties.Name` متنی (مجموع + حداکثر ۶ گونه با تعداد/درصد، سقف‌دار) برای دونات و نام تعداد/درصد برای هر ردیف legend؛ GotFocus/LostFocus دونات و legend همان readout hover را نشان/بازمی‌گردانند و Escape blur می‌کند. `DashboardDonutAccessibilityTests` + `NavigationAdmissionTests`: **13 passed، 0 failed**؛ build فقط Desktop: **0 warning/0 error، 57 ثانیه**. **تأییدنشده:** آزمون واقعی screen reader/Narrator و Tab/Enter روی Windows (نمایشگر/Windows در دسترس نبود) — پذیرش screen-reader باز.
- شواهد: `ManagerDashboardView.axaml:298–306,335–340`؛ code-behind pointer-press بدون left/right check؛ legend Button focus و blur keyboard متناظر ندارد.
- اقدام: drill-in روی Button/command با نام automation و Enter/Space؛ pointer interaction فقط left action معتبر. legend focus/hover/blur رفتار متوازن و textual counts/percent باشد؛ donut به‌تنهایی تنها راه دسترسی نباشد. navigation مجوز مرکزی D05 را طی کند؛ chart خوانا بدون hover هم باشد.
- پذیرش/بررسی محدود: Tab/Enter/Space و خروج focus، right-click بدون navigate، escape/blur برای state و نام automation؛ screen reader یک کنترل و legend کوتاه روی Windows در دسترس، نه کل اپ. در نبود ابزار، screen-reader acceptance باز و صریح باشد.
- پایان تسک: پس از بررسی محدود و sync با main، حتماً اجرا شود: `bash scripts/git/push.sh "fix(dashboard): D31 make donut interactions keyboard accessible"`؛ سپس PR به‌روزرسانی و لینک گزارش شود.

### D32 — اعداد عملیاتی ثابت production هستند
- [x] وضعیت: انجام‌شده | P1 | گزارش 32 | نتیجه: اعداد عملیاتی ساختگی حذف شدند.
- شواهد اجرای جدید: subtitle واکسیناسیون «یادآور و سوابق» و badge ثابت انبار حذف شد. بررسی هدفمند Python روی quick actions وجود دقیقاً 8 target، AltKeyهای 1..8 و نبود «۶ یادآور»/Badge «۳» را تأیید کرد؛ query/seed/mock تازه اضافه نشد. تغییرات با اسکریپت پوش ارسال و PR [#181](https://github.com/Arian-Alijani/Petjan/pull/181) به‌روز شد.
- شواهد: `DashboardQuickAction.cs:48–50`؛ «۶ یادآور امروز» و Badge «۳» ثابت‌اند.
- اقدام کم‌هزینه: عدد/ادعای عملیاتی ثابت حذف و subtitle عمومی واقعی مثل «یادآور و سوابق» باقی بماند؛ badge فقط وقتی source معتبر مستقل موجود شد. seed یا mock به Pages وارد نشود؛ برای تزئین tile query اضافی نسازید.
- پذیرش/بررسی محدود: DB خالی هیچ «۶ یادآور» یا «۳ کمبود» نداشته باشد؛ AltKeyها/targetها و navigation ثابت بمانند؛ جست‌وجوی هدفمند production XAML/quick actions ادعای count ساختگی باقی‌مانده نیابد.
- پایان تسک: پس از بررسی محدود و sync با main، حتماً اجرا شود: `bash scripts/git/push.sh "fix(dashboard): D32 remove fabricated operational counts"`؛ سپس PR به‌روزرسانی و لینک گزارش شود.

### D33 — tile، active sidebar را sync نمی‌کند
- [x] وضعیت: انجام‌شده | P2 | گزارش 33 | نتیجه: active sidebar از state مرکزی navigation مشتق می‌شود (کلید semantic)؛ highlight همه ورودی‌ها یکسان. | وابستگی: D05.
- شواهد اجرای 2026-10-01: `INavigationService.CurrentNavigationKey` (کلید semantic مانند expense/reports/purchasing) در تمام overloadها پس از admission ثبت می‌شود و `SidebarViewModel` با یک اشتراک دائمی روی `CurrentViewModelChanged` highlight را مشتق می‌کند؛ `SetActive` دستی در Sidebar/MainWindow و کپی `finance.Configure` حذف شد. tab/range ورودی هر entry از `PageViewModelBase.ApplyNavigationEntry` برای همه callerها (tile/hotkey/palette/sidebar) تحویل می‌شود؛ تغییر expense↔reports روی ViewModel مشترک رویداد می‌دهد؛ صفحه بدون sidebar (Purchasing) highlight را پاک می‌کند؛ ناوبری ردشده کلید/فعال را تغییر نمی‌دهد. `NavigationAdmissionTests`: **12 passed، 0 failed**؛ build فقط Desktop: **0 warning/0 error، 57 ثانیه**.
- شواهد: `DashboardPageViewModel.cs:170`؛ shell در `MainWindowViewModel.cs:119–129` دستی SetActive می‌کند؛ `SidebarViewModel` listener navigation ندارد.
- اقدام: active selection از CurrentViewModelChanged/navigation descriptor مرکزی مشتق شود، نه از callerهای متفاوت. Finance دو entry با InitialTab/Range دارد؛ مقصد semantic entry هم منتقل شود و فقط first matching Type انتخاب نشود. target بدون sidebar مثل Purchasing highlight اشتباه روی dashboard باقی نگذارد.
- پذیرش/بررسی محدود: tile/sidebar/hotkey/palette/drill-in نتیجه highlight یکسان؛ expense/reports با ViewModel مشترک درست؛ navigation denied active state را تغییر ندهد؛ subscription فقط یک بار و lifecycle درست باشد.
- پایان تسک: پس از بررسی محدود و sync با main، حتماً اجرا شود: `bash scripts/git/push.sh "fix(navigation): D33 synchronize sidebar from navigation state"`؛ سپس PR به‌روزرسانی و لینک گزارش شود.

### D34 — تست‌ها مسیرهای واقعی خطادار را پوشش نمی‌دهند
- [ ] وضعیت: باز | P1 | گزارش 34 | نتیجه: شکاف قطعی coverage | وابستگی: اصلاحات A/B/C و D24.
- شواهد: `DashboardBudgetTests.cs:94–109,133–169,233–308`؛ `ActivityFeedTests.cs:170–179`؛ پزشک/actor دستی، CacheDispatcher و دوبار فراخوانی mapper.
- اقدام: تست‌های کوچک افزوده‌شده داخل هر fix مرور و gap باقی‌مانده با fixture واقعی production پوشش داده شود: auth escalation/revoke، assigned doctor≠actor، receipt/refund/Credit، writer→maintenance rebuild، lifecycle/state/keyboard. test-double فقط برای کنترل زمان/خطای UI، نه حذف auth/store مسیر مورد آزمون. purity test به‌عنوان purity بماند، نه rebuild integration.
- پذیرش/بررسی محدود: هر regression روی رفتار معیوب قبلی fail و روی fix pass شود؛ فقط فیلتر کلاس‌های جدید/متأثر اجرا؛ coverage table به Dهای مربوط link دهد. seed دستی که اشتباه actor را پنهان کند، معیار پذیرش نیست؛ اجرای تمام suite ممنوع.
- پایان تسک: پس از بررسی محدود و sync با main، حتماً اجرا شود: `bash scripts/git/push.sh "test(dashboard): D34 close production path regression gaps"`؛ سپس PR به‌روزرسانی و لینک گزارش شود.

### D35 — شواهد فعلی عملکرد UI داشبورد را اثبات نمی‌کنند
- [ ] وضعیت: باز | P2 | گزارش 35 | نتیجه: شکاف سنجش، نه کندی قطعی | وابستگی: تثبیت lifecycle/query/UI و D24.
- شواهد: `backend-roadmap/measurements/C11-offline-budget.md:7–45`؛ dashboard=warm data query، RSS=login، first-frame=login proxy؛ startupهای تاریخی 1564 و 6215ms.
- اقدام در محدودیت این فاز: measurement کوتاه و opt-in از login/activation تا dashboard ready/render، یک cold و یک warm و hit/miss روی DB موقت؛ نام marker، build config/host/حجم داده و scope دقیق ثبت شود. فقط یک write+یک projection pass کنترل‌شده برای latency سازگاری. سراغ بهینه‌سازی کل startup یا idle60s/soak نروید؛ آن معیارها فعلاً acceptance باز دارند.
- پذیرش/بررسی محدود: query time به‌جای render و login RSS به‌جای shell گزارش نشوند؛ process measurement پس از ready bounded خاتمه یابد و instrumentation تولیدی/سکریپت لوکال را عوض نکند. در نبود GUI مناسب، همین gap مستند بماند؛ عدد ساختگی/قدیمی موجب Done نشود. این تسک اثبات سنجش است؛ هر bottleneck جدید، تسک مجزای شواهد‌دار می‌خواهد.
- پایان تسک: پس از بررسی محدود و sync با main، حتماً اجرا شود: `bash scripts/git/push.sh "test(dashboard): D35 record scoped authenticated render evidence"`؛ سپس PR به‌روزرسانی و لینک گزارش شود.

### D36 — docs/comments هنوز از Mock و نبود freshness می‌گویند
- [x] وضعیت: انجام‌شده | P3 | گزارش 36 | نتیجه: مستندات جاری با backend محلی واقعی هم‌تراز شد.
- شواهد اجرای 2026-10-01: پاراگراف «What this repo is» و بخش‌های `src/` و «one architectural rule» و «task → path recipes» در `AGENTS.md` بازنویسی شدند تا حذف کامل Track C mocks، اتصال Desktop به `Animora.Client.Services` روی SQLite محلی و ارجاع به `backend-roadmap/PROGRESS.md` به‌عنوان مرجع زنده را توصیف کنند؛ قاعده معماری seam (وابستگی ViewModel فقط به Contracts) حفظ شد و حذف نشد. کامنت DI داشبورد در `App.axaml.cs` از «stateless/pure mock, no reload until a real backend adds freshness» به توصیف scope-per-call، data cache مبتنی بر source generation (D19)، و refresh/activation واقعی (D04/D20/D37/D38) اصلاح شد. در `PERFORMANCE.md` جمله «mock services resolve synchronously» و note بدهی constructor-load با وضعیت واقعی (await روی SQLite، استثنای Dashboard که به مدل activation/refresh منتقل شده) به‌روز شد؛ ادعای بی‌مشکل‌بودن همه صفحات اضافه نشد و بخش‌های archive/تاریخی دست‌نخورده ماندند. `IDashboardQueryService.cs` از قبل دقیق بود (production reads local SQLite through the authorized query pipeline)، تغییر لازم نداشت. جست‌وجوی هدفمند «Mock/mock/freshness» در این بخش‌های جاری تناقض باقی‌مانده‌ای نشان نداد. تغییر صرفاً سندی/کامنتی بود؛ طبق قاعده ۸ build برنامه لازم نبود و اجرا نشد.
- شواهد: `AGENTS.md:9–21,69–91,110–115,147–149`؛ `IDashboardQueryService.cs:6–9` (بدون تغییر، دقیق)؛ `App.axaml.cs:277–285`؛ `PERFORMANCE.md` §4 و بخش Known technical debt.
- اقدام: current-state به `backend-roadmap/PROGRESS.md` ارجاع دهد؛ onboarding و commentهای dashboard اتصال محلی/activation/cache/refresh واقعی را توصیف کنند. وضعیت همه صفحات را بدون شاهد به‌روز/بی‌مشکل اعلام نکنید؛ archiveهای قدیمی و log فازها بازنویسی نشوند. no-mock rule با حذف بی‌مورد architecture rule اشتباه نشود.
- پذیرش/بررسی محدود: جست‌وجوی دقیق «Mock/constructor/freshness» در همین بخش‌های جاری، تناقض فعلی نداشته باشد؛ تاریخچه برچسب تاریخی داشته باشد؛ هیچ build برنامه برای تغییر صرفاً doc لازم نیست.
- پایان تسک: پس از بررسی محدود و sync با main، حتماً اجرا شود: `bash scripts/git/push.sh "docs(dashboard): D36 align documentation with the real local backend"`؛ سپس PR به‌روزرسانی و لینک گزارش شود.

## تودوی بدهی‌های کوچک‌تر گزارش — جدا از مشکلات بالا

### D37 — activation هنگام IsBusy نادیده گرفته می‌شود
- [x] وضعیت: انجام‌شده | P2 | بدهی اول گزارش | نتیجه: قطعی مسیر/ریسک تازگی | وابستگی: D25.
- شواهد اجرای 2026-10-01: activation در حین loading دیگر نادیده گرفته نمی‌شود: `ActivateAsync` همیشه generation جدید می‌سازد و load قبلی را cancel می‌کند؛ trigger همزمان `RefreshAsync` یک query مشترک دارد و refresh اجباری حین load دقیقاً یک re-run نگه می‌دارد. تست‌های `ReturnDuringLoading_*`، `LateResultOfSupersededLoad_*`، `ConcurrentRefreshTriggers_*`، `ForcedRefreshDuringLoad_*` و `NonCancellationFailure_*` (همان اجرای 19 passed) IsBusy نسل جدید را با finally نسل قدیمی false نمی‌کنند.
- شواهد: `DashboardPageViewModel.cs:61–64`، `if (!IsBusy)` و async command پیش‌فرض.
- اقدام: activation جدید cancel/supersede یا refresh pending coalesced داشته باشد؛ force کردن AllowConcurrentExecutions بدون generation صحیح ممنوع. تمام triggerها از یک policy latest-wins/serialized عبور کنند؛ این تسک regression سناریوی «بازگشت حین loading» است، نه تکرار D25.
- پذیرش/بررسی محدود: leave→return قبل completion، فقط نتیجه activation آخر با refresh تضمین‌شده؛ تأخیر fake task، بدون wait طولانی؛ IsBusy درخواست جدید با finally درخواست قدیمی false نشود.
- پایان تسک: پس از بررسی محدود و sync با main، حتماً اجرا شود: `bash scripts/git/push.sh "fix(dashboard): D37 retain refresh requests during loading"`؛ سپس PR به‌روزرسانی و لینک گزارش شود.

### D38 — navigate به همان singleton activation ایجاد نمی‌کند
- [x] وضعیت: انجام‌شده | P2 | بدهی دوم گزارش | نتیجه: قطعی طراحی equality setter | وابستگی: D25/D37.
- شواهد اجرای 2026-10-01: activation از equality property جدا شد: `NavigationService.Publish` برای مقصد یکسان فقط `OnNavigatedTo` را یک‌بار صدا می‌زند (بدون leave/recreate و بدون `CurrentViewModelChanged` اضافه) و برای صفحه دیگر ابتدا `OnNavigatedFrom` سپس publish. تست‌های `SameSingletonDestination_*`، `LeavingAndReturning_*`، `DeniedNavigation_*` (در همان 19 passed) هر چهار overload، deny و بازگشت را پوشش می‌دهند.
- شواهد: `NavigationService.cs:51–62`؛ setter تولیدی ObservableProperty با equality تغییر reference را شرط می‌کند؛ `MainWindowViewModel.NavigateHome`.
- اقدام: activation از change reference جدا، در یک مسیر مرکزی هر overload navigation صریح انجام شود؛ هنگام مقصد یکسان deactivation/recreate بیهوده نشود. configured/async overload دوبار activate نکنند؛ CurrentViewModelChanged همچنان معنای دقیق داشته باشد.
- پذیرش/بررسی محدود: Ctrl+Shift+D روی خود dashboard، refresh یک‌بار؛ navigation به صفحه دیگر و برگشت یک‌بار؛ deny، activation نداشته باشد؛ lifetime سایر singletonها آسیب نبیند.
- پایان تسک: پس از بررسی محدود و sync با main، حتماً اجرا شود: `bash scripts/git/push.sh "fix(navigation): D38 activate repeated singleton destinations"`؛ سپس PR به‌روزرسانی و لینک گزارش شود.

### D39 — snapshot فقط ظاهراً read-only است
- [x] وضعیت: انجام‌شده | P3 | بدهی سوم گزارش | نتیجه: collections ورودی و nested points در مرز DTO freeze شدند.
- شواهد اجرای جدید: `DashboardCollections.Freeze` copy خصوصی + ReadOnlyCollection می‌سازد؛ constructor و init/with برای Stats/Primary/Secondary/DoctorLoads/Species/Points همگی محافظت می‌شوند. `Snapshot_FreezesInputAndNestedCollections_IncludingRecordInitializers` mutation آرایه ورودی، cast به IList و nested/empty را رد کرد؛ cache-hit واقعی authorization همان reference را حفظ می‌کند. فیلتر این تست + `AuthorizedConcurrentCacheHits`: **2 passed، 0 failed، 6 ثانیه**. package/copy در cache-hit اضافه نشد. کد و پلن پوش، PR [#181](https://github.com/Arian-Alijani/Petjan/pull/181).
- شواهد: `DashboardModels.cs:93–106`؛ mapper آرایه‌ها را پشت IReadOnlyList منتشر و cache همان reference را share می‌کند.
- اقدام: freeze/copy collections در یک مرز مشخص با immutable collection واقعی یا wrapper غیرقابل‌تغییر بدون دسترسی به backing array. nested points/species/loads هم پوشش داده شوند؛ از copy مجدد در cache-hit اجتناب شود. تغییر بی‌دلیل کل public API یا افزودن package سنگین ممنوع.
- پذیرش/بررسی محدود: تغییر آرایه ورودی بعد construction و cast/mutation خروجی نتواند cached snapshot را خراب کند؛ hit همان reference immutable را بدهد و geometry rebuilt نشود؛ empty/nested collections نیز آزموده شوند.
- پایان تسک: پس از بررسی محدود و sync با main، حتماً اجرا شود: `bash scripts/git/push.sh "fix(dashboard): D39 freeze cached snapshot collections"`؛ سپس PR به‌روزرسانی و لینک گزارش شود.

### D40 — gap ثابت donut سهم کوچک را حذف می‌کند
- [x] وضعیت: انجام‌شده | P2 | بدهی چهارم گزارش | نتیجه: قطعی هندسه + بازتولید؛ سهم 1% ناپدید می‌شود.
- شواهد اجرای 2026-10-01: منطق زاویه‌ای DonutRing به `DonutGeometry` خالص منتقل شد و render و hit-test هر دو از آن استفاده می‌کنند: gap حداکثر 50٪ sweep همان قطعه است (سهم 1٪، 0.1٪ و 0.001٪ با gap پیش‌فرض 4.06° حذف نمی‌شوند)، صفر/منفی/NaN در total و قطعه نقش ندارند. تنظیم Finance (gap و RoundCaps خودش) دست‌نخورده است و فقط fallback قطعه‌های ریز تغییر می‌کند. تست‌ها: 1 از 100/1000/100000، عدم overlap، hit-test وسط قطعه، تک‌قطعه 360°، gap صفر، empty/zero/negative. Budget+Authorization: **55 passed، 0 failed، 20 ثانیه**؛ build فقط Desktop: **0 warning/0 error**. screenshot GUI گرفته نشد.
- شواهد: `DonutRing.cs:178,284–293`؛ پیش‌فرض GapArc=3.4 و Radius=48.
- اقدام: gap محدود نسبت به sweep مثبت؛ segment مثبت حذف نشود، صفر/negative policy روشن. render و `SliceIndexAt` با geometry جدید هماهنگ؛ shared control Finance نیز با تنظیمات خودش محفوظ بماند. cache geometry/render allocation بدون ضرورت عوض نشوند.
- پذیرش/بررسی محدود: سهم 1 از 100 و 1 از 1000، تک‌گونه/صفر، dashboard flat و Finance round caps؛ segment و hit-test معتبر بدون overlap ناموجه؛ unit geometry و یک screenshot کوچک، نه بازبینی کل Finance.
- پایان تسک: پس از بررسی محدود و sync با main، حتماً اجرا شود: `bash scripts/git/push.sh "fix(charts): D40 preserve small donut slices"`؛ سپس PR به‌روزرسانی و لینک گزارش شود.

### D41 — pulse بی‌نهایت بدون reduced-motion policy
- [x] وضعیت: انجام‌شده | P3 | بدهی پنجم گزارش | نتیجه: animation دائمی badge حذف شد | وابستگی: D32.
- شواهد اجرای جدید: style pulse و ScaleTransform/Animation آن از XAML حذف شد؛ optional badge برای منبع واقعی آینده static باقی است. parse XML و assertion نبود INFINITE/Style.Animations و باقی بودن binding badge موفق بود. هیچ timer/task/setting جدید یا ادعای کاهش ms/RSS اضافه نشد. تغییرات با اسکریپت پوش و PR [#181](https://github.com/Arian-Alijani/Petjan/pull/181) ارسال شدند.
- شواهد: `DashboardPageView.axaml:129–158`، Animation با IterationCount=INFINITE.
- اقدام کم‌هزینه: pulse دائمی غیرضروری badge حذف شود؛ اگر به‌دلیل نیاز مصوب باقی ماند، opt-in/reduced-motion و pause هنگام inactive لازم است. چون D32 badge ساختگی را حذف می‌کند، animation بی‌مصرف نیز باید حذف شود؛ animation engine یا setting پیچیده بی‌دلیل نسازید.
- پذیرش/بررسی محدود: در داشبورد بدون badge، animation loop وجود نداشته باشد؛ badge واقعی static خوانا باشد؛ خروج صفحه task/render-loop باقی نگذارد. ادعای کاهش ms/RSS بدون اندازه‌گیری نشود.
- پایان تسک: پس از بررسی محدود و sync با main، حتماً اجرا شود: `bash scripts/git/push.sh "fix(dashboard): D41 remove unnecessary perpetual badge motion"`؛ سپس PR به‌روزرسانی و لینک گزارش شود.

### D42 — ReflectionBinding/styleهای tile تکراری‌اند
- [ ] وضعیت: باز | P3 | بدهی ششم گزارش | نتیجه: بدهی قطعی نگهداری، کندی اثبات نشده.
- شواهد: `DashboardPageView.axaml:77–170` style داخل هر DataTemplate و ReflectionBindingهای accent/soft.
- اقدام: style/control reusable مناسب داخل Desktop، compiled binding یا property seam قابل‌کامپایل؛ visual و AltKey ثابت حفظ شوند. برای فقط 8 tile virtualization/framework جدید لازم نیست؛ perf claim باید سنجیده شود.
- پذیرش/بررسی محدود: build فقط Desktop و binding/style check، یک hover/focus کوتاه دو رنگ tile؛ duplication حذف و هیچ binding error جدید نباشد؛ مسیر startup/DI/سرویس‌های local عوض نشود.
- پایان تسک: پس از بررسی محدود و sync با main، حتماً اجرا شود: `bash scripts/git/push.sh "refactor(dashboard): D42 reuse typed quick action styling"`؛ سپس PR به‌روزرسانی و لینک گزارش شود.

### D43 — presentation مرده یا متکی به index ثابت است
- [x] وضعیت: انجام‌شده | P3 | بدهی هفتم گزارش | نتیجه: اعضای واقعاً بدون مصرف و allocation مرده حذف شدند؛ وابستگی index2 حذف شد.
- شواهد اجرای جدید: جست‌وجوی دقیق نشان داد HeroTitle/HeroSubtitle/PrimaryAction در dashboard view مصرف ندارند؛ فقط اعضای ViewModel حذف و public DTO حفظ شد. `QuickActions[2]`/CTA بی‌مصرف، `DashboardDoctorLoadViewModel.ParseColor` و gradient/FillBrush بی‌مصرف گونه حذف شدند؛ SwatchBrush و palette/target/AltKey دست‌نخورده‌اند. assertion هدفمند نبود تمام اعضای حذف‌شده در dashboard و build فقط Desktop: **0 warning، 0 error، 96 ثانیه**؛ Full solution build/test اجرا نشد. حذف دسترسی positional مستقل از تمام‌شدن D05/D16/D17 است و آن تسک‌ها را Done نمی‌کند. کد و پلن با اسکریپت پوش و PR [#181](https://github.com/Arian-Alijani/Petjan/pull/181) ارسال شدند.
- شواهد: HeroTitle/HeroSubtitle در View مصرف ندارند؛ `PrimaryAction => QuickActions[2]`؛ `DashboardDoctorLoadViewModel.ParseColor` استفاده نمی‌شود؛ `DashboardSpeciesViewModel.FillBrush`/gradient ساخته می‌شود اما legend SwatchBrush را bind می‌کند.
- اقدام: فقط اعضای واقعاً بدون مصرف و تخصیص مرده حذف/جایگزین؛ action با شناسه معنایی یا حذف CTA ناموجود. پس از filter permission ممکن است index2 دیگر وجود نداشته باشد؛ index ثابت ممنوع. عضو public Contracts فقط پس از بررسی مصرف‌کننده‌های دقیق تغییر کند؛ refactor کل ViewModel لازم نیست.
- پذیرش/بررسی محدود: لیست quick actions با کمتر از 3 action crash نکند؛ target و hotkey ثابت؛ جست‌وجوی دقیق اعضای حذف‌شده مصرف باقی‌مانده نداشته باشد؛ حذف gradient باعث تغییر visuals موجود نشود.
- پایان تسک: پس از بررسی محدود و sync با main، حتماً اجرا شود: `bash scripts/git/push.sh "refactor(dashboard): D43 remove dead presentation work"`؛ سپس PR به‌روزرسانی و لینک گزارش شود.

### D44 — literalهای theme مشترک خارج از tokens هستند
- [ ] وضعیت: باز | P3 | بدهی هشتم گزارش | نتیجه: بدهی قطعی نگهداری؛ contrast rendered تأییدنشده.
- شواهد: inline brush/size در `ManagerDashboardView.axaml`، quick-tile و `DashboardPageViewModel.cs:312`؛ theme در `Resources/Theme` موجود است.
- اقدام: فقط literalهای مشترک/semantic با tokens موجود جایگزین، در صورت نبود token کوچک مناسب ایجاد شود؛ palette هر گونه معنای مستقل خود را حفظ کند و به یک رنگ brand فروکاسته نشود. UI state مالی/خطر تنها به رنگ وابسته نباشد؛ اندازه‌های مختص geometry با توکن typography اشتباه نشوند.
- پذیرش/بررسی محدود: static-resource resolution/build محدود Desktop، خوانایی متن/status و focused control در یک نمای dashboard؛ light/dark اگر محصول واقعاً پشتیبانی می‌کند. بدون GUI، contrast acceptance باز بماند؛ تغییر theme عمومی با sweep همه صفحات ممنوع.
- پایان تسک: پس از بررسی محدود و sync با main، حتماً اجرا شود: `bash scripts/git/push.sh "refactor(dashboard): D44 use semantic theme tokens"`؛ سپس PR به‌روزرسانی و لینک گزارش شود.

## شرط پایان واقعی اصلاحات

- 36 مورد شماره‌دار + 8 بدهی کوچک‌تر، دقیقاً در 44 تسک پوشش داده شده‌اند؛ هر تسک evidence، اقدام، معیار پذیرش محدود و دستور push مستقل دارد.
- checkbox فقط وقتی بسته شود که مشکل رفع یا با شاهد معتبر از مسیر نهایی حذف شده، regression مرتبط pass، تغییرات committed/pushed و PR ثبت شده باشد. حذف مسیر معیوب در تسک وابسته، مجوز علامت‌زدن خودکار تسک بعدی نیست؛ شاهد/تست آن باید ثبت شود.
- موارد نیازمند GUI/Windows/contrast/performance در نبود ابزار «تأییدنشده/blocked» باقی بمانند. هیچ Full build/Full test یا تست طولانی برای بستن مصنوعی این فهرست اجرا نشود.
- baseline local startup و offline behavior حفظ شوند؛ اسکریپت‌های Windows/sandbox/git و دیتابیس کاربر دست‌نخورده، مگر تغییر ضروری جداگانه و اثبات‌شده. ledger/domain invariants و roadmap سرور به‌خاطر dashboard بازنویسی نشوند.
- این سند همچنان برنامه 44 اصلاح است، نه ادعای اجرای همه آن‌ها. وضعیت اجرای جدید در ابتدای سند و evidence هر checkbox ثبت شده است؛ گزارش صرفاً سندی اولیه، تغییرات remote قبلی و مانع تاریخی تست در بخش‌های تاریخی تفکیک شده‌اند.
