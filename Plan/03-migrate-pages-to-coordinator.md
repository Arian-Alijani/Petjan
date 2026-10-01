# تسک ۰۳ — مهاجرت صفحات سازنده‌لود به چرخهٔ عمر + حالت استاندارد

**وضعیت:** ✅ DONE
**پیش‌نیاز:** تسک ۰۱ (coordinator/state مشترک)، تسک ۰۲ (کنترل/قطعهٔ لودینگ).
**هدف:** ۱۱ صفحه‌ای که فقط در سازنده یک‌بار لود می‌شوند را به الگوی استاندارد
«لود روی ورود + حالت صریح» برسان، و ۳ صفحهٔ دارای `OnNavigatedTo` ساده را به
همان حالت استاندارد ارتقا بده. نتیجه: همهٔ صفحات رفتار رفرش یکدست دارند و بدهی
فنی `PERFORMANCE.md` خطوط ۱۹۳–۲۰۶ رفع می‌شود.

---

## واقعیت تأییدشدهٔ الگوی فعلی (خالص — نیازی به کاوش نیست)

همهٔ ۱۱ صفحهٔ سازنده‌لود **الگوی یکسان** دارند:
- سازندهٔ ViewModel: `IStoreReadinessGate _readiness` تزریق می‌شود و در انتهای
  سازنده `_ = LoadAsync();` صدا زده می‌شود.
- `private async Task LoadAsync()` که با `await _readiness.WaitAsync();` شروع
  می‌شود، سپس `IsBusy = true` ... کوئری ... `IsBusy = false` و ساخت لیست‌ها.
- بعضی `ReloadAsync` جدا هم دارند (Cashier، Cheques، Finance، History) که از
  callbackهای درون‌صفحه‌ای صدا زده می‌شوند.

این یعنی مهاجرت **مکانیکی و تکرارشونده** است: همان الگو برای همه.

---

## الگوی مهاجرت استاندارد (روی هر صفحه اعمال شود)

برای هر صفحه، این تبدیل را انجام بده (حداقل تغییر، بدون بازنویسی منطق کوئری):

### گام ۱ — حذف لود از سازنده، افزودن قلاب ورود
- در سازنده خط `_ = LoadAsync();` را **حذف** کن.
- متد `OnNavigatedTo` را override کن (مثل الگوی `PatientsPageViewModel` خط
  ۲۶۳–۲۶۶ که ساده و درست است):
  ```csharp
  public override void OnNavigatedTo()
  {
      if (!IsBusy) _ = LoadAsync();
  }
  ```
  این «گارد IsBusy» از لود موازی روی ورودهای سریع پشت‌سرهم جلوگیری می‌کند.

### گام ۲ — افزودن حالت صریح بارگذاری/خطا
هر صفحه باید `PageLoadState` (از تسک ۰۱) را در معرض bind بگذارد. حداقل این
اعضا را اضافه کن (الگوی داشبورد، اما سبک):
- فیلد `private bool _hasData;` که وقتی لود موفق داده‌دار شد `true` می‌شود.
- فیلد/`[ObservableProperty] private string? _loadError;`
- یک ویژگی محاسباتی:
  ```csharp
  public PageLoadState LoadState => PageLoadStateResolver.Resolve(
      _hasData, IsBusy, !string.IsNullOrEmpty(LoadError), IsEmpty);
  public bool IsStateVisible => LoadState != PageLoadState.Ready;
  public string StateText => PageLoadStateResolver.Describe(LoadState);
  ```
  که در آن `IsEmpty` یک bool مخصوص همان صفحه است (مثلاً
  `FilteredProducts.Count == 0` برای Inventory، یا لیست اصلی صفحه).
- وقتی `IsBusy` یا `LoadError` تغییر کرد، `OnPropertyChanged` برای
  `LoadState/IsStateVisible/StateText` را صدا بزن (یک متد کوچک `NotifyLoadState()`).

### گام ۳ — خطا به‌جای فقط toast
صفحاتی که در `catch` فقط toast نشان می‌دهند (مثل Patients خط ۲۸۷) را نگه دار،
اما **علاوه بر آن** `LoadError` را ست کن تا حالت `Stale/Error` در نوار لودینگ
دیده شود. اگر لود قبلیِ موفق وجود داشت → `Stale`؛ اگر اولین لود بود → `Error`.
(resolver این را از `_hasData` خودکار تشخیص می‌دهد.)

### گام ۴ — افزودن نوار لودینگ مشترک به View
در `<Screen>PageView.axaml` همان قطعهٔ لودینگ مشترک تسک ۰۲ را بالای محتوای
اصلی قرار بده و به `IsStateVisible / IsBusy / StateText` بایند کن. اگر صفحه
دکمهٔ رفرش ندارد، یک دکمهٔ «تازه‌سازی» کوچک هم‌سطح داشبورد اضافه کن که
`RefreshCommand` را صدا بزند (راه فرار، نه مکانیزم اصلی).

### گام ۵ — دکمهٔ رفرش دستی (fallback)
یک `[RelayCommand] private Task RefreshAsync() => LoadAsync(force: true);` اضافه
کن. چون سرویس‌های این صفحات کش ندارند، `force` فعلاً بی‌اثر است و فقط `LoadAsync`
را دوباره صدا می‌زند — پارامتر را برای آیندهٔ کش‌دار نگه دار اما لازم نیست سرویس
تغییر کند (قانون `§1`: بهینه‌سازی زودهنگام ممنوع).

### گام ۶ — توقف کار هنگام خروج (فقط صفحات دارای کار پس‌زمینه)
فقط اگر صفحه timer/کار پس‌زمینه دارد (در این دسته ندارند جز احتمالی)،
`OnNavigatedFrom` را override کن و کار را متوقف کن. برای صفحات سادهٔ این تسک
لازم نیست.

> **مهم:** از `PageRefreshCoordinator` کامل (با generation/coalescing) فقط برای
> صفحاتی استفاده کن که واقعاً به آن نیاز دارند (کار همزمان/timer). برای صفحات
> سادهٔ لیستی، **الگوی سبک گام ۱–۵ (همان الگوی Patients) کافی و استاندارد است**
> و افراط نیست. Coordinator کامل را به تسک‌های آینده/داشبورد واگذار کن. هدف:
> یکدستی رفتار، نه تحمیل یک کلاس سنگین به صفحهٔ ساده.

---

## فهرست صفحات و نکات اختصاصی هر کدام

> مسیر همه: `src/Animora.Desktop/Pages/<Area>/<...>PageViewModel.cs` + View متناظر.

### دستهٔ لیست عملیاتی (الگوی سبک گام ۱–۵) — بدون polling
1. **Reception** — `ReceptionPageViewModel.cs` (سازنده خط ۳۸، `_readiness` خط ۱۷،
   WaitAsync خط ۱۳۰). توجه: متد لودش ممکن است نام دیگری داشته باشد؛ همان متد
   موجود را به `OnNavigatedTo` منتقل کن. `IsEmpty` = لیست اصلی پذیرش خالی.
2. **Vaccination** — `VaccinationPageViewModel.cs` (LoadAsync خط ۱۹۴).
3. **Boarding** — `BoardingPageViewModel.cs` (LoadAsync خط ۱۶۸).
4. **Purchasing** — `PurchasingPageViewModel.cs` (LoadAsync خط ۵۶).
5. **Inventory** — `InventoryPageViewModel.cs` (LoadAsync خط ۱۰۱، سازنده خط ۴۵).
   `IsEmpty` = `FilteredProducts.Count == 0` در تب products.
6. **Sales** — `SalePageViewModel.cs` (LoadAsync خط ۴۰۹). صفحهٔ سنگین؛ فقط
   سازنده→OnNavigatedTo را جابه‌جا کن، منطق فاکتور را دست نزن.
7. **Cashier** — `CashierPageViewModel.cs` (LoadAsync خط ۱۵۶، `ReloadAsync` خط
   ۱۷۹ از قبل هست). `OnNavigatedTo` → `LoadAsync`؛ `ReloadAsync` موجود را نگه دار.
   **این صفحه کاندید polling سبک است** (پرتغییر/صف پرداخت) — اما در این تسک فقط
   ورود+دکمه؛ polling اختیاری و فقط اگر لازم شد در تسک ۰۴/آینده.
8. **Cheques** — `ChequesPageViewModel.cs` (LoadAsync خط ۲۰۳، `ReloadAsync` خط
   ۱۰۰۱، callbackها خطوط ۹۶۲/۹۹۱). `OnNavigatedTo`→`LoadAsync`؛ callbackهای
   `ReloadAsync` دست‌نخورده.
9. **Finance** — `FinancePageViewModel.cs` (LoadAsync خطوط ۵۵/۱۰۰/۲۰۶ — دو
   فراخوانی: یکی سازنده، یکی تغییر تب). سازنده را حذف کن؛ فراخوانی تغییر تب
   (خط ۱۰۰) را نگه دار. `OnNavigatedTo`→`LoadAsync`.
10. **History** — `HistoryPageViewModel.cs` (سازنده خط ۵۶، `ReloadAsync(bool
    resetPage)` خط ۳۵۳؛ ظاهراً لود سازنده هم همین را صدا می‌زند). لود اولیه را
    به `OnNavigatedTo` ببر؛ منطق صفحه‌بندی را دست نزن.

### دستهٔ تقریباً ثابت
11. **Profile** — `ProfilePageViewModel.cs` (LoadAsync خط ۳۰۵، سازنده خط ۲۲).
    فقط `OnNavigatedTo`→`LoadAsync` + دکمهٔ دستی. حالت خطا سبک کافی است؛
    `IsEmpty` معمولاً false (همیشه یک پروفایل هست).

### صفحات دارای `OnNavigatedTo` موجود (فقط ارتقای حالت، نه جابه‌جایی لود)
این سه از قبل روی ورود رفرش می‌کنند؛ فقط گام ۲–۵ (حالت صریح + نوار لودینگ +
دکمهٔ دستی اگر ندارند) را اضافه کن. **لود سازنده/ورود موجود را دست نزن.**
12. **Patients** — `PatientsPageViewModel.cs` (OnNavigatedTo خط ۲۶۳، `_loadGate`
    خط ۲۶۸، LoadAsync خط ۲۷۰). از قبل `_loadGate` و گارد دارد؛ فقط `LoadError`
    + `LoadState` را اضافه کن و نوار لودینگ را به View بیفزا.
13. **Visits** — `VisitsTriagePageViewModel.cs` (OnNavigatedTo موجود).
14. **Appointments** — `AppointmentsCalendarViewModel.cs` (OnNavigatedTo موجود).

> **داشبورد (Dashboard) اصلاً در این تسک لمس نمی‌شود** — الگوی کامل خودش را دارد.

---

## ترتیب پیشنهادی اجرا (برای پوش‌های میانی منطقی)

به‌علت حجم، صفحات را گروهی انجام بده و **بعد از هر گروه که بیلد سبز شد، پوش کن**
(قانون پوش برای کد حجیم میانی هم صدق می‌کند):
- گروه ۱: Inventory, Purchasing, Finance, History (مرتبط با انبار/مالی).
- گروه ۲: Sales, Cashier, Cheques (مرتبط با فروش/پول).
- گروه ۳: Reception, Vaccination, Boarding, Profile.
- گروه ۴: ارتقای حالت Patients, Visits, Appointments.

هر گروه: ویرایش → `scripts/sandbox/build.sh` (0/0) → پوش با پیام گروه.

---

## تأیید (سریع)

```bash
scripts/sandbox/build.sh        # بعد از هر گروه: 0 warning / 0 error
scripts/sandbox/run.sh          # یک‌بار در پایان تسک: SMOKE RUN OK
```
**بیلد/تست کامل طولانی ممنوع.** فقط بیلد سریع Desktop + یک smoke-run پایانی.

---

## معیار پذیرش (Definition of Done)

- [x] هیچ صفحهٔ singleone دیگر `_ = LoadAsync()` در سازنده ندارد (مگر فراخوانی
      مشروط تغییر-تب Finance که عمدی است).
- [x] هر ۱۴ صفحه (به‌جز داشبورد) `OnNavigatedTo` دارند و روی ورود رفرش می‌شوند.
- [x] هر صفحه `LoadState`/`IsStateVisible`/`StateText` + نوار لودینگ مشترک دارد.
- [x] هر صفحه دکمهٔ رفرش دستی (fallback) دارد.
- [x] لایه‌بندی و منطق کوئری هیچ صفحه‌ای تغییر نکرده؛ فقط چرخهٔ عمر + حالت.
- [x] بیلد 0/0 و smoke-run OK.

---

## قانون پوش (اجباری)

بعد از هر گروه یک پوش، و پوش نهایی تسک:
```bash
scripts/git/push.sh "refactor(pages): lifecycle-aware refresh + explicit load state (group N)"
```
کد خروج ۳ → `setup_github_environment` سپس تکرار. کد خروج ۲ → حل کانفلیکت
(اولویت ریموت) سپس تکرار.

پس از پوش نهایی موفق: وضعیت این فایل را به ✅ DONE تغییر بده.
