# تسک ۰۴ — invalidate رویدادمحور بین صفحات

**وضعیت:** ✅ DONE
**پیش‌نیاز:** تسک ۰۳ (همهٔ صفحات `OnNavigatedTo`-refresh + `LoadAsync` قابل‌صدا
دارند).
**هدف:** وقتی کاربر در یک صفحه چیزی می‌نویسد (فروش، خرید، ثبت بیمار، ...)،
صفحات **دیگرِ** مرتبط هم مطلع شوند و دادهٔ کهنه نشان ندهند — بدون اتکای صرف به
«ورود مجدد». دکمهٔ رفرش دستی به‌عنوان fallback می‌ماند. این لایهٔ ۲ معماری هدف
است (بخش ۳ سند `00-OVERVIEW.md`).

---

## چرا (زمینهٔ خالص — نیازی به کاوش نیست)

- **هیچ event-bus/messenger در استفاده نیست.** رفرش بعد از نوشتن امروز فقط
  **درون‌صفحه‌ای** است: callbackهای `onSaved / onCompleted / onChanged` که
  همان صفحه را دوباره لود می‌کنند (نمونه‌ها: Patients:225, Cheques:962/991,
  Appointments:1189, Boarding:352, Inventory:365). این برای تغییر داخل یک صفحه
  کار می‌کند اما **بین صفحات پوشش ندارد**: مثلاً یک فروش در صفحهٔ Sales موجودی
  انبار و درآمد مالی را عوض می‌کند، ولی صفحات Inventory/Finance/Dashboard تا
  ورود بعدی کاربر از آن بی‌خبرند.
- چون صفحات singleton و زنده‌اند، می‌توان یک «علامت کهنگی» به آن‌ها رساند تا
  در ورود بعدی حتماً رفرش کنند (یا اگر هم‌اکنون نمایش داده می‌شوند، فوراً).

### روابط invalidate (از سند overlay تحلیل شده — خالص)
| نوشتن در | صفحاتی که کهنه می‌شوند |
|---|---|
| Sales (فروش) / Cashier (پرداخت) | Inventory, Finance, Dashboard |
| Purchasing (خرید) | Inventory, Finance, Cheques, Dashboard |
| Patients (ثبت مالک/حیوان) | Dashboard, Appointments, Visits |
| Visits / Vaccination (ویزیت/واکسن) | Dashboard, History, Patients |
| Cheques (ثبت/وصول چک) | Finance, Dashboard |

---

## طراحی (سبک، استاندارد، لایه‌بندی حفظ‌شده)

یک notifier سبک در **لایهٔ Desktop** (concern رابط کاربری است، نه دامنه). از
`CommunityToolkit.Mvvm.Messaging` **استفاده نکن** مگر خواستی — یک اینترفیس
کوچک اختصاصی تمیزتر، قابل‌کنترل‌تر و بدون وابستگی جانبی است.

### ساخت `src/Animora.Desktop/Services/IDataChangeNotifier.cs`
```csharp
namespace Animora.Desktop.Services;

/// <summary>دامنه‌های داده‌ای که نوشتن‌ها آن‌ها را کهنه می‌کنند.</summary>
public enum DataDomain
{
    Patients, Visits, Vaccination, Appointments, Boarding,
    Inventory, Purchasing, Sales, Cashier, Cheques, Finance, Dashboard,
}

/// <summary>ناقل سبک «فلان دامنه عوض شد» بین صفحات زندهٔ شل (UI thread).</summary>
public interface IDataChangeNotifier
{
    /// <summary>اعلام تغییر در یک یا چند دامنه (پس از یک نوشتن موفق).</summary>
    void Notify(params DataDomain[] domains);

    /// <summary>اشتراک یک شنونده؛ خروجی یک IDisposable برای لغو اشتراک.</summary>
    IDisposable Subscribe(Action<DataDomain> handler);
}
```

### ساخت `src/Animora.Desktop/Services/DataChangeNotifier.cs`
- پیاده‌سازی ساده و thread-safe-سبک. چون همه‌چیز روی UI thread رخ می‌دهد
  (navigation/dialog/toast همگی UI thread‌اند)، یک `List<Action<DataDomain>>`
  با قفل سبک کافی است.
- `Notify` روی هر دامنه، همهٔ شنونده‌ها را صدا می‌زند. اگر از thread پس‌زمینه
  صدا زده شد، با `Dispatcher.UIThread.Post` به UI thread marshal کن (قانون
  `§14`). امن‌ترین کار: در `Notify` همیشه marshal کن مگر مطمئنی روی UI thread‌ای.
- `Subscribe` یک توکن `IDisposable` برگرداند که در dispose خودش را از لیست حذف
  کند (جلوگیری از نشت — صفحات singleton عمر طولانی دارند اما drill-in های
  transient نه).
- **بدون cache، بدون state انباشته** — فقط pub/sub لحظه‌ای. (قانون `§15`: فضای
  کلید notifier محدود است چون شنونده‌ها = تعداد ثابت صفحات.)

### ثبت در DI — `src/Animora.Desktop/App.axaml.cs`
```csharp
services.AddSingleton<IDataChangeNotifier, DataChangeNotifier>();
```
کنار بقیهٔ سرویس‌های Desktop (Navigation/Dialog/Toast).

---

## اتصال: تولیدکننده‌ها (Notify پس از نوشتن)

در نقاطی که یک نوشتن موفق رخ می‌دهد، بعد از موفقیت `Notify` را صدا بزن. این
نقاط همان callbackهای موجودند؛ کافی است صفحه/ViewModel نوشتن یک
`IDataChangeNotifier` تزریق‌شده داشته باشد و در پایان عمل موفق صدا بزند.

**نقاط تأییدشده برای افزودن `Notify` (خالص):**
- `Pages/Sales/SalePageViewModel.cs` — پس از ثبت موفق فاکتور/پرداخت:
  `Notify(DataDomain.Sales, DataDomain.Inventory, DataDomain.Finance, DataDomain.Dashboard)`.
- `Pages/Cashier/CashierPageViewModel.cs` — پس از پرداخت موفق:
  `Notify(DataDomain.Cashier, DataDomain.Finance, DataDomain.Dashboard)`.
- `Pages/Purchasing/PurchasingPageViewModel.cs` — پس از ثبت خرید:
  `Notify(DataDomain.Purchasing, DataDomain.Inventory, DataDomain.Finance, DataDomain.Cheques, DataDomain.Dashboard)`.
- `Pages/Patients/PatientsPageViewModel.cs` (callback خط ۲۲۵ و
  `AddPatientWizardViewModel._onCompleted`) — پس از ثبت مالک/حیوان:
  `Notify(DataDomain.Patients, DataDomain.Dashboard, DataDomain.Appointments, DataDomain.Visits)`.
- `Pages/Visits/VisitsTriagePageViewModel.cs` + `Pages/Vaccination/VaccinationPageViewModel.cs`
  — پس از ثبت ویزیت/واکسن: دامنه‌های `Visits`/`Vaccination` + `Dashboard, History, Patients`.
- `Pages/Cheques/ChequesPageViewModel.cs` (callbackهای خط ۹۶۲/۹۹۱) — پس از
  ثبت/وصول چک: `Notify(DataDomain.Cheques, DataDomain.Finance, DataDomain.Dashboard)`.

> **روش کم‌مداخله:** callbackهای موجود (`onSaved/onCompleted/onChanged`) را نگه
> دار (رفرش درون‌صفحه‌ای فوری کاربر)، و **در همان محل** یک `Notify(...)` برای
> دامنه‌های بیرونی اضافه کن. دامنهٔ خودِ صفحه را هم در Notify بگنجان تا یکدست
> باشد (شنوندهٔ خودش آن را نادیده می‌گیرد چون همین حالا رفرش کرده — گارد IsBusy).

---

## اتصال: مصرف‌کننده‌ها (Subscribe + علامت کهنگی)

هر صفحهٔ لیستی در سازنده به دامنهٔ خودش subscribe می‌کند و یک «پرچم کهنگی»
نگه می‌دارد:

```csharp
private bool _isStale;            // داده‌ام ممکن است کهنه باشد
private readonly IDisposable _sub;

// در سازنده:
_sub = notifier.Subscribe(domain =>
{
    if (domain != DataDomain.Inventory) return;   // فقط دامنهٔ خودم
    if (ReferenceEquals(_navigation.CurrentViewModel, this))
        _ = LoadAsync();          // اگر همین حالا نمایش داده می‌شوم: فوری رفرش
    else
        _isStale = true;          // وگرنه: علامت بزن، در ورود بعدی رفرش کن
});
```

و در `OnNavigatedTo`:
```csharp
public override void OnNavigatedTo()
{
    if (_isStale || !_hasData) { _isStale = false; if (!IsBusy) _ = LoadAsync(); }
    else if (!IsBusy) _ = LoadAsync();   // سیاستِ تسک ۰۳؛ یا فقط وقتی stale اگر خواستی بهینه‌تر
}
```

> **بهینه‌سازی اختیاری و استاندارد (هدف «هر بار همه‌چیز را از صفر نخوان»):** اگر
> خواستی ورود‌های پیاپی بی‌دلیل رفرش نکنند، منطق `OnNavigatedTo` را به «فقط اگر
> `_isStale || !_hasData`» محدود کن. این یعنی تا وقتی رویدادی صفحه را کهنه
> نکرده، ورود مجدد کوئری نمی‌زند → صرفه‌جویی واقعی. **برای صفحات دستهٔ لیست
> عملیاتی این رویکرد ترجیح داده می‌شود**؛ برای Dashboard/Cashier (پرتغییر) رفرشِ
> همیشگیِ ورود بماند. تصمیم per-page طبق دستهٔ بخش ۴ سند overview.

- شنوندهٔ دامنه را فقط برای دامنه‌های مرتبطِ همان صفحه فیلتر کن (یک صفحه ممکن
  است به چند دامنه گوش دهد؛ مثلاً Dashboard به تقریباً همه، Finance به
  `Finance` و منابع آن). نگاشت در جدول «روابط invalidate» بالا.
- drill-in/transient VMها اگر subscribe کردند، در dispose حتماً `_sub.Dispose()`.

### Dashboard
داشبورد از قبل timer + ActivateAsync دارد. فقط یک subscribe سبک اضافه کن که اگر
داشبورد نمایش داده می‌شود و رویدادی از دامنه‌های منبع آمد،
`_ = _refresh.RefreshAsync(force: true)` را صدا بزند. اگر نمایش داده نمی‌شود،
`ActivateAsync` ورود بعدی خودش refresh می‌کند — نیازی به پرچم جدا نیست.

---

## تأیید (سریع)

```bash
scripts/sandbox/build.sh        # 0 warning / 0 error
scripts/sandbox/run.sh          # چون App.axaml.cs (DI) لمس شد → SMOKE RUN OK
```

---

## معیار پذیرش (Definition of Done)

- [x] `IDataChangeNotifier` + `DataChangeNotifier` ساخته و در DI ثبت شد.
- [x] تمام نقاط نوشتن جدول «تولیدکننده‌ها» `Notify(...)` را صدا می‌زنند
      (بدون حذف callbackهای درون‌صفحه‌ای موجود).
- [x] صفحات مصرف‌کننده subscribe دارند + پرچم کهنگی + رفرش فوری/تأخیری درست.
- [x] drill-in/transient VMهای subscribe‌کننده در dispose لغو اشتراک می‌کنند.
- [x] لایه‌بندی حفظ شد (notifier فقط در Desktop؛ Contracts/Application دست‌نخورده).
- [x] بیلد 0/0 و smoke-run OK.

---

## قانون پوش (اجباری)

```bash
scripts/git/push.sh "feat(desktop): event-driven cross-page data invalidation"
```
اگر وسط کار حجم زیاد شد (اتصال همهٔ تولیدکننده‌ها)، یک پوش میانی هم بزن.
کد خروج ۳ → `setup_github_environment` سپس تکرار. کد خروج ۲ → حل کانفلیکت
(اولویت ریموت) سپس تکرار.

پس از پوش نهایی موفق: وضعیت این فایل را به ✅ DONE تغییر بده.
