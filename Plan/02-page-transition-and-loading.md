# تسک ۰۲ — ترنزیشن نرم تعویض صفحه + کنترل لودینگ مشترک

**وضعیت:** ✅ DONE
**پیش‌نیاز:** تسک ۰۱ (برای `PageLoadState` در کنترل لودینگ).
**هدف:** (الف) جابجایی بین صفحات با یک ترنزیشن نرم و سبک انجام شود (نه تعویض
ناگهانی فعلی)، (ب) یک کنترل لودینگ/overlay مشترک ساخته شود که صفحات (در تسک ۰۳)
استفاده کنند و با حالت لودینگِ موجود داشبورد **هماهنگ** باشد.

---

## چرا (زمینهٔ خالص — نیازی به کاوش نیست)

- `Shell/MainWindow.axaml` خط ۵۸: محتوا در یک
  `<ContentControl Content="{Binding CurrentPage}" />` داخل یک `ScrollViewer`
  نمایش می‌یابد. **هیچ ترنزیشنی ندارد → تعویض صفحه ناگهانی است.**
- Avalonia کنترل استاندارد `TransitioningContentControl` دارد که با
  `PageTransition` (مثل `CrossFade` یا `PageSlide`) محتوا را هنگام تغییر
  انیمیت می‌کند — راهکار بومی، سبک، بدون کتابخانهٔ اضافی.
- کش View حفظ شده (`ViewCache`) پس تعویض صفحه از نظر ساخت visual tree ارزان
  است؛ ترنزیشن فقط یک fade/slide کوتاه روی محتوای آماده است → هزینهٔ پرفورمنسی
  ناچیز. مدت کوتاه نگه داشته شود (≈۱۸۰–۲۲۰ms) تا «نرم» باشد نه «کند».
- لودینگ فعلی داشبورد (`Pages/Dashboard/DashboardPageView.axaml` خطوط ۱۶–۲۴):
  یک `Border.card` با `ProgressBar IsIndeterminate` + `TextBlock` متن حالت، که
  با `IsStateVisible` و `IsBusy` و `StateText` بایند می‌شود. کنترل مشترک باید
  **همین ظاهر** را بازتولید کند تا داشبورد و بقیهٔ صفحات یکدست دیده شوند.
- `App.axaml` تم FluentTheme + استایل‌های کارت/آیکون را include می‌کند
  (خطوط ۵۵–۲۳۵). استایل کنترل جدید در همان‌جا ثبت می‌شود.

---

## بخش A — ترنزیشن تعویض صفحه

### ویرایش `src/Animora.Desktop/Shell/MainWindow.axaml`
خط ۵۸ را از `ContentControl` به `TransitioningContentControl` تغییر بده و یک
`PageTransition` سبک به آن بده. نمونهٔ دقیق (مقدار مدت و easing را همین بگذار):

```xml
<TransitioningContentControl Content="{Binding CurrentPage}">
    <TransitioningContentControl.PageTransition>
        <CrossFade Duration="0:0:0.18" />
    </TransitioningContentControl.PageTransition>
</TransitioningContentControl>
```

نکات قطعی:
- **چرا CrossFade و نه PageSlide:** صفحات RTL‌اند و slide افقی در RTL می‌تواند
  جهت‌گیری گیج‌کننده بدهد و با عرض متغیر sidebar تداخل بصری کند. یک fade کوتاه
  خنثی، نرم و بی‌دردسر است و با معماری فعلی (یک ContentControl واحد) سازگار.
  اگر خواستی slide امتحان کنی، فقط بعد از تأیید بصری؛ پیش‌فرض این پلن CrossFade.
- `TransitioningContentControl` در namespace پیش‌فرض Avalonia است؛ نیازی به
  `xmlns` جدید نیست (مثل `ContentControl`).
- `ScrollViewer` والد دست‌نخورده بماند. فقط فرزند `ContentControl` → `Transitioning...`.
- **بررسی سازگاری:** `TransitioningContentControl` هنگام تعویض، محتوای قبلی را
  لحظه‌ای کنار محتوای جدید نگه می‌دارد. چون View صفحات کش‌شده و singleton است،
  مطمئن شو تعویض سریع متوالی (مثلاً hotkeyها Alt+1..8) باعث خطای «کنترل والد
  دارد» نشود — `ViewCache.GetOrCreate` از قبل این را با برگرداندن نمونهٔ fresh
  وقتی `Parent != null` مدیریت می‌کند (خطوط ۵۹–۶۴ آن فایل). پس امن است؛ فقط در
  smoke-run تأیید کن.

---

## بخش B — کنترل لودینگ مشترک

### ساخت `src/Animora.Desktop/Controls/PageLoadingState.axaml` (+ `.axaml.cs`)
یک `TemplatedControl` یا سادهٔ `UserControl`-محور که همان بلوک لودینگ داشبورد را
به‌صورت مشترک ارائه می‌کند. **ساده‌ترین مسیر استاندارد:** یک `UserControl` با
این bindableها (StyledProperty) یا یک `ContentControl` سبک:

- ویژگی‌ها (StyledProperty):
  - `bool IsVisible` (از `IsStateVisible` صفحه) — یا از خاصیت پایهٔ Visual.
  - `bool IsBusy` — کنترل نمایش ProgressBar.
  - `string StateText` — متن حالت.
- محتوای بصری دقیقاً معادل داشبورد (کپی ساختار خطوط ۱۶–۲۴ آن view):
  ```xml
  <Border Classes="card" Padding="12,10">
      <StackPanel Spacing="6">
          <ProgressBar IsIndeterminate="True" IsVisible="{Binding IsBusy, ...}"
                       AutomationProperties.Name="در حال دریافت اطلاعات" />
          <TextBlock Text="{Binding StateText, ...}" TextWrapping="Wrap"
                     AutomationProperties.LiveSetting="Polite"
                     Foreground="{StaticResource MutedBrush}" />
      </StackPanel>
  </Border>
  ```
- **توصیهٔ ساده‌تر (ترجیحی):** به‌جای TemplatedControl پیچیده، یک
  `UserControl` با `StyledProperty`های بالا بساز و در code-behind سه property را
  تعریف کن. این کم‌کد و قابل‌فهم است و از افراط جلوگیری می‌کند.

> اگر ساخت یک کنترل اختصاصی بیش از حد لازم به نظر رسید، **جایگزین مجاز و سبک‌تر:**
> صرفاً یک بلوک XAML استاندارد (همان Border/ProgressBar/TextBlock) را در تسک ۰۳
> مستقیماً در هر View صفحه کپی کن و با `PageLoadState` صفحه بایند کن. در این حالت
> این بخش B به یک «قطعهٔ الگوی XAML مرجع» در همین فایل تنزل می‌یابد و کنترل جدا
> ساخته نمی‌شود. **تصمیم نهایی با مجری تسک بر اساس اینکه کدام کم‌تکرار‌تر و
> تمیزتر است** — هدف: حداقل کد، حداکثر یکدستی. (برای ۱۱ صفحه، کنترل مشترک معمولاً
> تمیزتر است.)

### ثبت استایل (اگر کنترل جدا ساخته شد)
اگر کنترل `.axaml` جدا شد و به style نیاز داشت، در `App.axaml` بخش
`Application.Styles` (نزدیک خطوط ۲۲۵–۲۳۵) یک `StyleInclude` اضافه کن. اگر فقط
UserControl با قالب درون‌خطی است، نیازی نیست.

---

## هماهنگی با داشبورد

- داشبورد در این تسک **تغییر نمی‌کند** (بلوک لودینگ خودش را دارد و درست کار
  می‌کند). کنترل مشترک صرفاً **همان ظاهر** را دارد تا وقتی صفحات دیگر در تسک ۰۳
  آن را می‌گیرند، کل برنامه یکدست شود. (مهاجرت اختیاری داشبورد به کنترل مشترک
  خارج از دامنه است.)
- ترنزیشن تعویض صفحه سراسری است و شامل داشبورد هم می‌شود — بنابراین ورود/خروج
  داشبورد هم همان fade نرم را می‌گیرد؛ لودینگ داخلی داشبورد مستقل از آن کار
  می‌کند (ترنزیشن = سطح شل، لودینگ = سطح محتوای صفحه). این دو لایه تداخل ندارند.

---

## تأیید (سریع — بیلد طولانی ممنوع)

```bash
scripts/sandbox/build.sh        # 0 warning / 0 error
scripts/sandbox/run.sh          # چون MainWindow.axaml (shell) لمس شد → باید SMOKE RUN OK
```
در smoke-run مطمئن شو خطای parse/XAML نیست و برنامه بالا می‌آید.

---

## معیار پذیرش (Definition of Done)

- [x] `MainWindow.axaml` از `TransitioningContentControl` با `CrossFade 0.18s`
      استفاده می‌کند؛ `ScrollViewer` دست‌نخورده.
- [x] کنترل/قطعهٔ لودینگ مشترک ساخته شد و ظاهرش با بلوک داشبورد یکسان است.
- [x] `scripts/sandbox/build.sh` → 0/0 و `scripts/sandbox/run.sh` → SMOKE RUN OK.
- [x] هیچ کتابخانهٔ جدیدی اضافه نشد (فقط Avalonia بومی).

---

## قانون پوش (اجباری)

```bash
scripts/git/push.sh "feat(shell): smooth page transition + shared loading state control"
```
کد خروج ۳ → `setup_github_environment` سپس تکرار. کد خروج ۲ → حل کانفلیکت (اولویت
ریموت) سپس تکرار.

پس از پوش موفق: وضعیت این فایل را به ✅ DONE تغییر بده.
