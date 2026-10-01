# تسک ۰۵ — پاک‌سازی نهایی و بستن پلن

**وضعیت:** ⬜ TODO
**پیش‌نیاز:** تسک‌های ۰۱–۰۴ همگی ✅ DONE.
**هدف:** به‌روزرسانی سند بدهی فنی تا با واقعیت جدید بخواند، تأیید نهایی سلامت
بیلد/اجرا، و حذف پوشهٔ `Plan/` به‌عنوان آخرین قدم.

---

## گام ۱ — به‌روزرسانی بدهی فنی در `PERFORMANCE.md`

بخش «Known technical debt» (خطوط ۱۹۳–۲۰۶) می‌گوید «صفحات singleton در سازنده
لود می‌شوند → داده کهنه». حالا این رفع شده. این بند را به‌روزرسانی کن تا دقیق
بخواند:

- متنِ «but the remaining singleton pages still load in their constructor.
  Migrating them is a deliberate future refactor, recorded as debt rather than
  rewritten now.» را بازنویسی کن به اینکه مهاجرت انجام شد: همهٔ صفحات اکنون
  `OnNavigatedTo`-refresh + حالت صریح `PageLoadState` دارند و از
  `PageRefreshCoordinator`/الگوی سبک مشترک استفاده می‌کنند، به‌علاوهٔ invalidate
  رویدادمحور از طریق `IDataChangeNotifier`.
- **هیچ ادعای اغراق‌آمیز نکن** (مثل «بدون باگ»). فقط واقعیت تغییر را ثبت کن،
  مختصر، هم‌سبک بقیهٔ فایل.

> **فقط همین یک بند.** هیچ بخش دیگر `PERFORMANCE.md` و هیچ فایل مستند دیگری
> (ARCHITECTURE/STACK/AGENTS/roadmap) را تغییر نده مگر خطای واقعی ایجاد شده باشد.

---

## گام ۲ — تأیید نهایی سلامت (سریع)

```bash
scripts/sandbox/build.sh        # باید 0 warning / 0 error
scripts/sandbox/run.sh          # باید SMOKE RUN OK
dotnet test tests/Animora.Application.Tests --filter PageLoadState   # تست resolver تسک ۰۱
```
**بیلد/تست کامل طولانی ممنوع.** فقط همین سه مورد سبک.

اگر چیزی قرمز بود → همان‌جا فیکس کن و (اگر فیکس سنگین بود) پوش میانی بزن، سپس
ادامه. تسک تا وقتی هر سه سبز نیستند بسته نمی‌شود.

---

## گام ۳ — حذف پوشهٔ `Plan/`

این **آخرین کار** است. فقط محتوای داخل `Plan/` حذف می‌شود؛ به هیچ فایل دیگری
(از جمله فایل‌های roadmap) دست نزن.

```bash
cd /home/user/webapp
git rm -r Plan/
```

(یا `rm -rf Plan/` اگر برخی فایل‌ها هنوز tracked نیستند، سپس `git add -A`.)

---

## گام ۴ — پوش نهایی (اجباری)

```bash
scripts/git/push.sh "chore(plan): finalize refresh+transition work, update perf debt, remove Plan/"
```
کد خروج ۳ → `setup_github_environment` سپس تکرار. کد خروج ۲ → حل کانفلیکت
(اولویت ریموت) سپس تکرار.

---

## معیار پذیرش (Definition of Done)

- [ ] بند بدهی فنی `PERFORMANCE.md` به‌روز و دقیق است (بدون اغراق).
- [ ] `build.sh` → 0/0، `run.sh` → SMOKE RUN OK، تست resolver سبز.
- [ ] پوشهٔ `Plan/` کاملاً حذف شد؛ هیچ فایل دیگری حذف/تغییر غیرلازم نشد.
- [ ] پوش نهایی موفق انجام شد.

> با اتمام این تسک، کل پلن بسته است و دیگر فایل وضعیتی باقی نمی‌ماند.
