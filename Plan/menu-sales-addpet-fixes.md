# TODO — منوی ناوبری + صفحهٔ فروش و صندوق + مودال افزودن حیوان

> این فایل تنها منبعِ حقیقت برای این مجموعه‌تسک است. نوشته شده تا در یک سشنِ
> کاملاً جدید، مدل بدون بازرسی دوبارهٔ ریپو مستقیماً پیاده‌سازی را شروع کند.
> همهٔ مسیرها، علت‌ها و تصمیم‌ها اینجا «خالص» آمده‌اند.
>
> **قانون قفل‌شدهٔ پوش:** پس از اتمام هر تسک، حتماً:
> `scripts/git/push.sh "پیام"` اجرا شود. نبودِ پوش = شکست.
> اگر exit code=3 شد → ابزار `setup_github_environment` یک‌بار صدا زده و دوباره اجرا شود.
>
> برنچ کاری: **genspark_ai_developer** (الان روی همین برنچ هستیم).
> بیلد سریع فقط Desktop: `scripts/sandbox/build.sh` → باید **0 warning / 0 error**.
> اسموک‌ران (اختیاری، فقط اگر startup/DI/shell دست خورد): `scripts/sandbox/run.sh` → `SMOKE RUN OK`.
> **تست کامل/طولانی ممنوع.** بیلد سریع کافی است.

---

## زمینهٔ مشترک (یک‌بار بخوان — برای همهٔ تسک‌ها لازم است)

- پروژه: **Animora** — اپ دسکتاپ مدیریت کلینیک دامپزشکی، **Avalonia + MVVM (CommunityToolkit.Mvvm)**, .NET 10، فارسی/RTL.
- همهٔ تغییرات UI در `src/Animora.Desktop/` انجام می‌شود.
- جریان داده: View → ViewModel → اینترفیس `Animora.Contracts` → سرویس واقعی `Animora.Client.Services`. ViewModelها فقط به اینترفیس‌های Contracts وابسته‌اند.
- RTL یک‌بار در سطح اپ در `App.axaml` ست شده.

### فایل‌های کلیدی این مجموعه‌تسک (مسیر دقیق)
| موضوع | فایل |
|---|---|
| تعریف آیتم‌های منو (ترتیب/گروه) | `src/Animora.Desktop/Shell/NavigationMenu.cs` |
| رندر سایدبار/منو (XAML) | `src/Animora.Desktop/Shell/SidebarView.axaml` |
| ساخت گروه‌ها از آیتم‌ها | `src/Animora.Desktop/Shell/SidebarViewModel.cs` (`BuildGroups` با `GroupBy(item => item.GroupName)` — ترتیبِ ظهورِ اولین آیتمِ هر گروه، ترتیبِ گروه‌ها را تعیین می‌کند) |
| صفحهٔ فروش و صندوق (VM) | `src/Animora.Desktop/Pages/Sales/SalePageViewModel.cs` |
| صفحهٔ فروش و صندوق (XAML) | `src/Animora.Desktop/Pages/Sales/SalePageView.axaml` |
| مودال انتخاب مشتری | `src/Animora.Desktop/Pages/Sales/OwnerPickerModalViewModel.cs` (نگه داشته می‌شود — در «تعویض مشتری» استفاده می‌شود) |
| سرویس فروش (impl) | `src/Animora.Client.Services/Sales/SalesQueryService.cs` |
| مودال افزودن حیوان (VM) | `src/Animora.Desktop/Pages/Patients/AddPetModalViewModel.cs` |
| مودال افزودن حیوان (XAML) | `src/Animora.Desktop/Pages/Patients/AddPetModalView.axaml` (عرض=760، `.modal.wide`) |
| باز شدن مودال از پروفایل مالک | `src/Animora.Desktop/Pages/Patients/OwnerSheetViewModel.cs` → متد `AddPet()` (~خط 411-428) |
| سرویس دیالوگ | `src/Animora.Desktop/Services/DialogService.cs` (`ShowModal(content, maxWidth=520)`) |
| هاست مودال | `src/Animora.Desktop/Controls/ModalHost.axaml.cs` (`Panel.MaxWidth = maxWidth`) |

---

## Task 1 — جابجایی «فروش و صندوق» به بعد از «نوبت‌ها»
**وضعیت: [x] انجام شد**

### مسئله
در منوی همبرگری، گزینهٔ «فروش و صندوق» باید بلافاصله بعد از «نوبت‌ها» بیاید.

### اطلاعات خالص (نیازی به بازرسی دوباره نیست)
در `NavigationMenu.cs` آرایهٔ `Items` این ترتیب/گروه‌ها را دارد:
- گروه «مرکز فرمان»: `dashboard`، `appointments` (نوبت‌ها)
- گروه «مدیریت کلینیک»: `patients`، `visits`، `vaccine`، `boarding`، `sale` (فروش و صندوق)
- گروه «کسب و کار»: `inventory`، `expense`، `reports`
- گروه «مالی و خزانه»: `cheques`
- گروه «سیستم»: `history`، `profile`

`SidebarViewModel.BuildGroups()` با `GroupBy(item.GroupName)` گروه‌بندی می‌کند؛ ترتیبِ آیتم‌ها درونِ هر گروه = ترتیبِ آرایه. ترتیبِ خودِ گروه‌ها = ترتیبِ ظهورِ اولین آیتمِ هر گروه.

### تصمیم پیاده‌سازی (خالص)
«نوبت‌ها» در گروه «مرکز فرمان» است و «فروش و صندوق» در گروه «مدیریت کلینیک». «بعد از نوبت‌ها آمدن» یعنی `sale` باید **اولین آیتمِ گروه مدیریت کلینیک** شود (گروه مدیریت کلینیک بلافاصله بعد از مرکز فرمان/نوبت‌هاست، پس اولین آیتمِ آن، درست بعد از نوبت‌ها دیده می‌شود).
- راه‌حل: در آرایهٔ `Items`، خطِ `sale` را از انتهای بلوکِ «مدیریت کلینیک» بردار و به **ابتدای** آن بلوک (قبل از `patients`) منتقل کن. گروهِ `sale` همان «مدیریت کلینیک» بماند.
- چون اولین آیتمی که نامِ گروه «مدیریت کلینیک» را می‌آورد حالا `sale` است، ترتیب گروه‌ها تغییر نمی‌کند (مرکز فرمان همچنان اول است چون `dashboard`/`appointments` قبل‌تر در آرایه‌اند).

### فایل‌ها
- `src/Animora.Desktop/Shell/NavigationMenu.cs`

### تأیید
بیلد سریع 0/0. (اختیاری) اسموک‌ران برای دیدنِ منو.

### پوش
`scripts/git/push.sh "feat(nav): [Task 1] انتقال «فروش و صندوق» به ابتدای گروه مدیریت کلینیک (بعد از نوبت‌ها)"`

---

## Task 2 — راست‌چین و RTL کردن عناوینِ گروه در منو
**وضعیت: [x] انجام شد**

### مسئله
عنوان‌های گروه («کسب و کار»، «مدیریت کلینیک»، «مرکز فرمان»، «مالی و خزانه»، «سیستم») در منو باید سمت راست و راست‌به‌چپ نوشته شوند؛ کاربر می‌گوید الان چپ‌به‌راست‌اند.

### اطلاعات خالص
در `SidebarView.axaml`، داخلِ ItemTemplateِ گروه‌ها، `TextBlock` عنوان این‌هاست:
```
<TextBlock Text="{Binding Title}"
           IsVisible="..."
           HorizontalAlignment="Right"
           TextAlignment="Right"
           ... />
```
`UserControl` روت `FlowDirection="RightToLeft"` دارد اما این `TextBlock` **FlowDirection صریح ندارد** — بر خلافِ `TextBlock`های برچسبِ آیتم‌ها (`nav-label`) که صراحتاً `FlowDirection="RightToLeft"` دارند. همین نبودِ FlowDirectionِ صریح باعث می‌شود شکل‌دهی/جهتِ متنِ فارسیِ عنوان‌ها در عمل چپ‌به‌راست دیده شود.

### تصمیم پیاده‌سازی (خالص)
به همان `TextBlock` عنوانِ گروه، `FlowDirection="RightToLeft"` اضافه کن (دقیقاً مثل الگوی `nav-label`). `HorizontalAlignment="Right"` و `TextAlignment="Right"` از قبل درست‌اند — دست نخورند.
- فاصله‌ها استاندارد بمانند: `Padding="12,14,12,6"` موجود درست است؛ تغییرش نده مگر لازم شود.
- فقط همین TextBlock؛ به بقیهٔ قسمت‌ها (آیتم‌ها، برند، کارت کاربر) دست نزن تا جایی خراب نشود.

### فایل‌ها
- `src/Animora.Desktop/Shell/SidebarView.axaml`

### تأیید
بیلد سریع 0/0. اسموک‌ران اختیاری برای تأیید چشمی راست‌چین بودن.

### پوش
`scripts/git/push.sh "fix(nav): [Task 2] راست‌به‌چپ کردن عناوین گروه منو با FlowDirection=RTL"`

---

## Task 3 — پیش‌فرضِ صفحهٔ فروش و صندوق = فاکتور بازِ مشتری گذری (مهم/پیچیده)
**وضعیت: [x] انجام شد**

### مسئله (دو بخش)
1. با کلیک روی «فروش و صندوق» در منو، به‌جای صفحهٔ خالیِ معمول (empty work surface با دو کارت «فاکتور از ویزیت» / «فروش مستقیم»)، به‌صورت پیش‌فرض یک **فاکتور بازِ آمادهٔ استفاده برای مشتری گذری** نمایش داده شود.
2. با کلیک روی «فاکتور جدید»، دیگر پنل/مودال انتخاب مشتری (`OwnerPickerModalViewModel`) نیاید؛ مستقیماً یک فاکتور جدیدِ مشتری گذری باز شود.

### اطلاعات خالص (نتیجهٔ بررسی — نیازی به بازبینی دوباره نیست)
- متد `CreateBlankAsync()` در `SalesQueryService.cs` **از قبل** فاکتور را به‌عنوان مشتری گذری می‌سازد:
  `new CreateBlankInvoiceCommand(null, GuestSaleName, null)` که `GuestSaleName = "مشتری مهمان"`.
  یعنی فاکتورِ خالی، ذاتاً گذری است و نیازی به انتخاب مالک ندارد.
- `OwnerPickerModalViewModel.SelectGuestAsync` فقط `PickOwnerAsync(id, OwnerPickResult(IsGuest:true, ""))` را صدا می‌زند که همان نامِ گذری را ست می‌کند — پس بازِ پیکر برای حالت گذری کاملاً زائد است.
- در `SalePageViewModel.cs`:
  - `NewBlankInvoice` (command): `CreateBlankAsync()` → سپس `ShowInvoice(detail)` → **و بعد `_dialogs.ShowModal(new OwnerPickerModalViewModel(...))`** را باز می‌کند (خطوط ~346-354). همین باز شدنِ پیکر باید حذف شود.
  - `OwnerPickerModalViewModel` در **دو** جا استفاده می‌شود: (الف) `NewBlankInvoice` (حذف شود)، (ب) `ChangeCustomer` (خطوط ~414-416، **نگه داشته شود** — کاربر همچنان می‌تواند از روی فاکتورِ باز، مشتری را به یک مالک واقعی تغییر دهد). پس **فایل پیکر حذف نمی‌شود.**
  - بارگذاری اولیه: `LoadAsync()` در انتها `ShowEmptyWork()` را صدا می‌زند که `SelectedInvoice=null` و `HasSelectedInvoice=false` ⇒ `ShowWorkEmpty=true` ⇒ صفحهٔ خالی نمایش داده می‌شود.
  - صفحه singleton است؛ `OnNavigatedTo` در هر ورود `ReloadPageAsync` را صدا می‌زند. `ReloadPageAsync` بارِ اول `LoadAsync` و بارهای بعد `RefreshAfterMutationAsync` (که فاکتور باز را حفظ می‌کند) اجرا می‌کند.

### تصمیم پیاده‌سازی (خالص، با مراقبت از سناریوها)
هدف: هم «فاکتور جدید»، هم ورودِ پیش‌فرض به صفحه، بدونِ مودالِ انتخاب مشتری، یک فاکتور گذریِ باز نشان دهند — بدون انباشتِ فاکتورهای خالیِ بی‌استفاده و بدون شکستن جریان‌های دیگر (ساخت فاکتور از ویزیت، انتخاب فاکتورهای موجود، تعویض مشتری).

1. **حذف بازِ پیکر از `NewBlankInvoice`** (در `SalePageViewModel.cs`):
   - بلوکِ `_dialogs.ShowModal(new OwnerPickerModalViewModel(... detail.Id ...), 560);` و کامنتِ بالای آن را حذف کن. بقیهٔ متد (ساخت blank، `ShowInvoice`, رفرش KPI) بماند. حالا دکمهٔ «فاکتور جدید» مستقیماً یک فاکتور گذریِ باز می‌سازد و نشان می‌دهد.

2. **پیش‌فرضِ ورود به صفحه = فاکتور گذریِ باز** (در `LoadAsync` یا منطق ورود):
   - در پایانِ `LoadAsync`، به‌جای `ShowEmptyWork()`، یک متدِ جدید مثلِ `EnsureDefaultGuestInvoiceAsync()` صدا بزن:
     - منطق: اگر از قبل فاکتورِ بازی (Draft/Partial) در `_invoices` هست و منطقی است که همان ادامه یابد، **لازم نیست** فاکتور جدید بسازیم؛ اما سادگی و خواستهٔ کاربر: «به‌صورت پیش‌فرض یک فاکتور باز برای مشتری گذری باز شود».
     - تصمیم ساده و امن برای جلوگیری از انباشت: اگر هیچ فاکتور Draft گذریِ خالی‌ای وجود ندارد، یک `CreateBlankAsync()` بساز و `ShowInvoice` کن؛ اگر هست، آخرین draft را باز کن. اما چون تشخیص «خالیِ گذری» پیچیدگی و کوئری اضافی می‌آورد، رویکردِ قطعی و سرراست:
       - **در بارِ اولِ ورود به صفحه، یک `CreateBlankAsync()` بساز و نمایش بده.** (یک فاکتور گذریِ باز = خواستهٔ دقیقِ کاربر.)
       - در ورودهای بعدی (صفحه singleton)، چون `ReloadPageAsync` بارِ اول‌بودن را با `_pageLoaded` تشخیص می‌دهد و بعد از آن مسیرِ `RefreshAfterMutationAsync` می‌رود، فاکتورِ باز حفظ می‌شود و فاکتور جدیدِ اضافه ساخته نمی‌شود. پس انباشت رخ نمی‌دهد.
   - پیاده‌سازی پیشنهادی:
     ```csharp
     private async Task LoadAsync()
     {
         await _readiness.WaitAsync();
         IsBusy = true;
         try
         {
             await ReloadListsAsync();
             RenderSidebar();
             await RefreshKpisAsync();
             await OpenDefaultGuestInvoiceAsync(); // به‌جای ShowEmptyWork()
         }
         finally { IsBusy = false; }
     }

     private async Task OpenDefaultGuestInvoiceAsync()
     {
         try
         {
             var detail = await _service.CreateBlankAsync(); // ذاتاً گذری
             NotifyInvoicesChanged();
             _segment = "invoices";
             OnPropertyChanged(nameof(IsReadySegment));
             OnPropertyChanged(nameof(IsInvoicesSegment));
             await ReloadListsAsync();
             RenderSidebar();
             ShowInvoice(detail);
         }
         catch (InvalidOperationException)
         {
             // اگر ساخت نشد، به صفحهٔ خالی برگرد تا کاربر دستی اقدام کند
             ShowEmptyWork();
         }
     }
     ```
   - توجه: `ShowEmptyWork()` و کارت‌های empty-surface **حذف نمی‌شوند** — مسیرِ fallback و دکمهٔ «فاکتور از ویزیت» (`FunnelToVisits`) هنوز لازم‌اند. فقط دیگر مسیرِ پیش‌فرض نیستند.

3. **عدم‌شکستنِ جریان‌های دیگر (چک‌لیست سناریو):**
   - ساخت فاکتور از ویزیت (`BuildFromEncounterAsync`) — دست‌نخورده؛ همچنان کار می‌کند.
   - انتخاب فاکتور موجود از سایدبار (`SelectInvoice`) — دست‌نخورده.
   - تعویض مشتری روی فاکتور باز (`ChangeCustomer` → `OwnerPickerModalViewModel`) — دست‌نخورده؛ کاربر می‌تواند از گذری به مالک واقعی برود.
   - تسویهٔ فاکتور گذری — قبلاً در Task 2a ریپو درست شده (بدون اجبار انتخاب مالک)؛ تداخلی ایجاد نکن.
   - حذف/ابطال فاکتور — دست‌نخورده.

### فایل‌ها
- `src/Animora.Desktop/Pages/Sales/SalePageViewModel.cs` (حذف بازِ پیکر در `NewBlankInvoice` + متدِ ورودِ پیش‌فرض)
- (احتمالاً نیازی به تغییر XAML نیست؛ `ShowWorkEmpty` خودش بایند است.)

### نکتهٔ کدِ زائد
- `OwnerPickerModalViewModel`/`OwnerPickerModalView` **حذف نشوند** (در ChangeCustomer لازم‌اند).
- اگر بعد از حذفِ بازِ پیکر، importِ بلااستفاده‌ای ماند، پاک‌سازی کن تا 0 warning بماند.

### تأیید
بیلد سریع 0/0. اسموک‌ران (این تسک startup/shell را دست نمی‌زند ولی VM بارگذاری را تغییر می‌دهد → اسموک‌ران توصیه می‌شود) → `SMOKE RUN OK`.

### پوش (به‌خاطر حجم/اهمیت، همین‌جا پوش شود)
`scripts/git/push.sh "feat(sales): [Task 3] پیش‌فرضِ صندوق = فاکتور بازِ مشتری گذری + حذف مودال انتخاب مشتری در فاکتور جدید"`

---

## Task 4 — رفعِ لود نصفهٔ مودال «افزودن حیوان» از پروفایل مالک
**وضعیت: [x] انجام شد**

### مسئله
وقتی از پروفایلِ یک مالک، «افزودن حیوان» زده می‌شود، مودالِ افزودن نصفه/ناقص لود می‌شود.

### اطلاعات خالص (ریشهٔ دقیقِ باگ — پیدا شده)
- `AddPetModalView.axaml` محتوایش `Width="760" MaxWidth="760"` است (`.modal.wide`).
- `ModalHost.SetContent(content, maxWidth)` مقدار `Panel.MaxWidth = maxWidth` را ست می‌کند؛ `ShowModal` به‌صورت پیش‌فرض `maxWidth = 520` دارد.
- در `PatientsPageViewModel` (ساخت از صفحهٔ بیماران) درست `maxWidth: 760` پاس می‌شود (خط ~361) ⇒ آنجا سالم است.
- اما در `OwnerSheetViewModel.AddPet()` (خطوط ~411-428) `ShowModal(new AddPetModalViewModel(...))` **بدونِ پاس‌دادنِ عرض** صدا زده می‌شود ⇒ هاست روی 520 می‌ماند در حالی‌که محتوا 760 می‌خواهد ⇒ مودال بریده/نصفه دیده می‌شود.

### تصمیم پیاده‌سازی (خالص)
در `OwnerSheetViewModel.AddPet()`، به انتهای فراخوانیِ `_dialogs.ShowModal(...)` آرگومانِ `760` (یا `maxWidth: 760`) اضافه کن — دقیقاً مثلِ `PatientsPageViewModel`. همین کافی است.
```csharp
_dialogs.ShowModal(new AddPetModalViewModel(
    owner,
    async req => { ... },
    _dialogs, _toast), 760);
```

### فایل‌ها
- `src/Animora.Desktop/Pages/Patients/OwnerSheetViewModel.cs`

### تأیید
بیلد سریع 0/0. اسموک‌ران لازم نیست (فقط یک آرگومان عددی).

### پوش
`scripts/git/push.sh "fix(patients): [Task 4] باز شدن کامل مودال افزودن حیوان از پروفایل مالک (عرض 760)"`

---

## Task پایانی — حذف این فایل پلن
**وضعیت: [x] انجام شد**

پس از اتمام و پوشِ هر چهار تسک بالا، این فایل (`Plan/menu-sales-addpet-fixes.md`) را حذف کن و پوش کن.
به هیچ فایلِ دیگری در `Plan/` یا نقشه‌راه‌ها دست نزن.
`scripts/git/push.sh "chore(plan): [Task پایانی] حذف پلن تکمیل‌شدهٔ menu-sales-addpet-fixes"`
