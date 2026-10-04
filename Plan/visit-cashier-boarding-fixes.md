# Plan — Visit→Cashier billing flow + Boarding checkout fixes (2026-10-04)

> **پلن خوداتکا (self-contained).** یک مدل در سشن کاملاً جدید باید بتواند فقط با خواندن همین فایل،
> بدون بررسی مجدد ریپو، هر سه تسک را پیاده‌سازی کند. تمام مسیر فایل‌ها، رفتار واقعیِ کدِ فعلی (verified
> روی کد زنده)، ریشهٔ مشکل، راه‌حل انتخابی، و معیار پذیرش داخل همین فایل آمده. §0 را یک‌بار بخوان،
> سپس تسک‌ها را از بالا به پایین اجرا کن.
>
> **🔒 قانون قفل‌شده — بعد از هر تسک push:** بعد از تکمیل **هر** تسک، فوراً اجرا کن
> `scripts/git/push.sh "type(scope): message"`. **پوش نکردن = مرگ پروژه.**
> - خروجی `3` (نبود credential) → یک‌بار ابزار `setup_github_environment` را صدا بزن، سپس همان دستور را دوباره اجرا کن.
> - خروجی `2` (تعارض rebase، ریپو تمیز می‌ماند) → تعارض را با اولویت remote حل کن، سپس دوباره اجرا کن.
> - روی شاخهٔ `genspark_ai_developer` کار کن (در **اولین** push از `scripts/git/push.sh -b genspark_ai_developer "msg"`
>   استفاده کن تا شاخه ساخته/سوییچ شود؛ push‌های بعدی روی همان شاخه می‌مانند).
>
> **اگر وسط یک تسک، فیکس سنگین یا بلوک کد بزرگی نوشته شد**، همان لحظه با پیام واضح push کن — آن را با کار بعدی batch نکن.
>
> **تسک پایانی (T7) فقط همین فایل `Plan/visit-cashier-boarding-fixes.md` را حذف می‌کند.** به هیچ فایل docs/roadmap دیگری دست نزن.

---

## 0. زمینه (یک‌بار بخوان، دوباره استخراج نکن)

### 0.1 اپ + معماری
- **اپ:** Animora — اپ دسکتاپ مدیریت کلینیک دامپزشکی + حسابداری، self-hosted. .NET 10، Avalonia + MVVM
  (CommunityToolkit.Mvvm)، UI فارسی/RTL، تقویم جلالی، SQLite رمزنگاری‌شدهٔ محلی، offline-first.
- **دامنهٔ این پلن:** جریان **ویزیت (Visits)** → **صندوق/فروش (Sales/Cashier)** و جریان **پانسیون (Boarding)** →
  صندوق، به‌علاوهٔ لایه‌های Contracts / Client.Services / Application / Domain مربوطه.

### 0.2 قانون معماری (هرگز نشکن)
جریان داده: **View → ViewModel → اینترفیس `Animora.Contracts` (constructor DI) → پیاده‌سازی واقعی
`Animora.Client.Services` → dispatch در `Animora.Application` (permission/validation/journal) →
`Animora.Domain` + `Animora.Persistence.Sqlite`.**
- ViewModel فقط به اینترفیس‌های Contracts وابسته است — هرگز به EF/SQL یا سرویس concrete.
- هیچ داده/منطق نمونه (mock/design-time) در `Pages/**` نباشد. seed در Persistence seeder است.
- **یک toast هرگز نباید ادعای موفقیت کند مگر فرمان واقعاً success برگردانده باشد.**

### 0.3 بیلد / تأیید (فقط focused — بدون full test، بدون benchmark طولانی)
- بیلد سریع Desktop: `scripts/sandbox/build.sh` → باید **۰ warning / ۰ error** باشد.
- کل solution: `scripts/sandbox/build.sh full`. اسموک (Xvfb): `scripts/sandbox/run.sh` → چاپ `SMOKE RUN OK`.
- **قانون:** بعد از تسکی که کد کامپایل‌شونده را عوض کرد، بیلد سریع بزن تا ۰/۰ تأیید شود. **full test یا benchmark نزن.**
  اسموک فقط اگر startup/DI/shell را دست زدی.

### 0.4 مکانیزم seam (ریشهٔ مشترک خطاها) — اول این را بفهم
هر دو سرویس `VisitEncounterService` و `SalesQueryService` و `BoardingQueryService` هر `Result` ناموفق را به
exception تبدیل می‌کنند:
- `VisitEncounterService.SendAsync` (خطوط ۹۲–۱۱۱): `if (result.IsFailure) throw new ServiceException(result.Error);`
- در Boarding/Sales الگوی مشابه با `ServiceException` یا `InvalidOperationException`.

یک ViewModel که داخل یک `async RelayCommand` **بدون try/catch** این‌ها را await کند، اجازه می‌دهد exception از
state machine فرار کند → UI dispatcher unhandled → crash-recovery. **الگوی درست guard که در همین ریپو هست و باید
استفاده شود** (`PatientsPresentation`):
```csharp
catch (InvalidOperationException ex) { _toast.Show(PatientsPresentation.CommandFailureMessage(ex, fallback), Tone.Danger); }
```
`PatientsPresentation.CommandFailureMessage(ex, fallback)` اگر `ex.Message` یک کد access/session باشد پیام
«دسترسی لازم را ندارید یا نشست منقضی شده؛ دوباره وارد شوید» را برمی‌گرداند، وگرنه `fallback`.
> نکته: `ServiceException` از `InvalidOperationException` ارث‌بری می‌کند (در catchهای فعلی `InvalidOperationException`
> گرفته می‌شود و کار می‌کند). این را موقع نوشتن guard جدید در نظر بگیر.

### 0.5 Path aliasها (تأیید‌شده موجودند — دوباره جستجو نکن)
| Alias | Path |
|---|---|
| EVM | `src/Animora.Desktop/Pages/Visits/VisitEncounterViewModel.cs` |
| E.VIEW | `src/Animora.Desktop/Pages/Visits/VisitEncounterView.axaml` |
| RX.VIEW | `src/Animora.Desktop/Pages/Visits/RxStepView.axaml` |
| DONE.VIEW | `src/Animora.Desktop/Pages/Visits/DoneStepView.axaml` |
| E.SVC | `src/Animora.Client.Services/Visits/VisitEncounterService.cs` |
| E.CMD | `src/Animora.Application/UseCases/Visits/EncounterCommands.cs` |
| SALE.VM | `src/Animora.Desktop/Pages/Sales/SalePageViewModel.cs` |
| SALE.SVC | `src/Animora.Client.Services/Sales/SalesQueryService.cs` |
| INV.VISIT.CMD | `src/Animora.Application/UseCases/Sales/CreateInvoiceFromVisitCommand.cs` |
| INV.STAY.CMD | `src/Animora.Application/UseCases/Sales/CreateInvoiceFromStayCommand.cs` |
| BRD.VM | `src/Animora.Desktop/Pages/Boarding/BoardingPageViewModel.cs` |
| STAY.VM | `src/Animora.Desktop/Pages/Boarding/StayDetailModalViewModel.cs` |
| STAY.VIEW | `src/Animora.Desktop/Pages/Boarding/StayDetailModalView.axaml` |
| XFER.VM | `src/Animora.Desktop/Pages/Boarding/TransferModalViewModel.cs` |
| CHECKOUT.VM | `src/Animora.Desktop/Pages/Boarding/CheckoutModalViewModel.cs` |
| BRD.SVC | `src/Animora.Client.Services/Boarding/BoardingQueryService.cs` |
| BRD.CMD | `src/Animora.Application/UseCases/Boarding/BoardingCommands.cs` |
| BRD.CONTR | `src/Animora.Contracts/Boarding/IBoardingQueryService.cs` + `BoardingStay.cs` |
| DI | `src/Animora.Desktop/App.axaml.cs` + `src/Animora.Desktop/App.axaml` |

### 0.6 حقایق تأیید‌شده روی کد زنده (مستقیم استفاده کن — دوباره استخراج نکن)

**جریان ویزیت → صندوق (مربوط به T1, T2):**
- `CompleteVisitCommandHandler` (E.CMD خطوط ۲۰۵–۲۵۲): فاکتور **نمی‌سازد**. کارش: visit را `Complete` می‌کند،
  `MedicalHistoryEntry` می‌افزاید، یک **billing handoff** ثبت می‌کند (`handoffs.Add(...)`, خط ۲۲۶)،
  صف پذیرش را می‌بندد، قرار ملاقات را می‌بندد، و `visit.MarkReadyForInvoice()` (خط ۲۴۱). خروجی یک
  `EncounterSummaryReadModel` با subtotal/tax/total است. **پس معماری درست است: دکتر فقط ویزیت را «آمادهٔ صدور» می‌کند.**
- `E.SVC.CompleteAsync` (خطوط ۷۴–۸۰): `CompleteVisitCommand` را می‌فرستد و summary را برمی‌گرداند.
- `EVM.CompleteAsync` (خطوط ۳۷۵–۴۰۰): بعد از complete، `_changes.Notify(..., DataDomain.Cashier, ...)` می‌زند و
  `CurrentStep = EncounterStep.Done` → صفحهٔ «ویزیت ثبت شد» نمایش داده می‌شود. **این درست است.**
- `DONE.VIEW` دکمهٔ «رفتن به صندوق» دارد که `HandoffToCashierCommand` را صدا می‌زند.
- `EVM.HandoffToCashierAsync` (خطوط ۳۶۶–۳۷۳): مستقیماً `SalePageViewModel.BuildFromEncounterAsync(_encounterId)`
  را صدا می‌زند که = `SALE.SVC.CreateFromEncounterAsync` = فرستادن `CreateInvoiceFromVisitCommand`.
- **🔴 مشکل هماهنگی تسک ۲ (verified):** `INV.VISIT.CMD` و `INV.STAY.CMD` هر دو با
  `[RequiresPermission(PermissionCodes.SalesDraft)]` محافظت شده‌اند (خط ۱۸ هر دو فایل). یعنی وقتی برنامه آنلاین شود و
  **دکتر اکانت جدا بدون permission `SalesDraft`** داشته باشد، کلیک «رفتن به صندوق» در صفحهٔ دکتر → `BuildFromEncounterAsync`
  → fail با PERMISSION_DENIED. در حالت فعلی (تک‌اکانتِ آفلاین) چون کاربر همهٔ permissionها را دارد، ظاهراً کار می‌کند، ولی
  برای مدل آنلاین **نادرست و ناهماهنگ** است. صدور فاکتور باید کار **صندوق‌دار** باشد، نه دکتر.
- `SALE.SVC.CreateFromEncounterAsync` (خطوط ۱۷۰–۱۸۲): اول `CreateInvoiceFromVisitCommand` را امتحان می‌کند؛ اگر id
  خالی بود `CreateInvoiceFromStayCommand` را امتحان می‌کند. **این union دو جریان visit و boarding را پوشش می‌دهد — یعنی
  هم handoff ویزیت و هم handoff پانسیون از همین مسیر «آمادهٔ صدور» در صندوق فاکتور می‌شوند.** (این برای T3 مهم است.)
- `SALE.SVC.GetReadyEncountersAsync` (خطوط ۴۶–۶۱): `ReadyForBillingQuery` را می‌زند که visit-handoff و stay-handoff را
  union می‌کند؛ صندوق‌دار از لیست «آمادهٔ صدور» (`SALE.VM.ReadyList`) آیتم را انتخاب و فاکتور می‌سازد.

**دراپ‌داون ویزیت (مربوط به T1):**
- `RX.VIEW` دو `ComboBox` با `Classes="rx-add"` دارد (خطوط ۲۴–۳۱ خدمات با `ItemsSource={Binding ServiceCatalog}` و
  `SelectedItem={Binding SelectedServiceToAdd}`؛ خطوط ۷۳–۸۰ کالا با `ItemCatalog`/`SelectedItemToAdd`).
- هر ComboBox داخل `Border Classes="field-search"` (در E.VIEW خطوط ۱۷۴–۱۸۱: `Padding=12,8`, `MinWidth=240`,
  `CornerRadius=12`) و درون یک `Grid ColumnDefinitions="Auto,*"` (ستون ۰ یک آیکون Path، ستون ۱ ComboBox) قرار دارد.
- استایل `ComboBox.rx-add` (E.VIEW خطوط ۱۸۲–۱۹۰): `Background=Transparent`, `BorderThickness=0`, `Padding=0`,
  `HorizontalAlignment=Stretch`, `VerticalAlignment=Center`. **هیچ `MaxDropDownHeight` یا دستکاری popup template ندارد.**
- کل محتوا داخل `ScrollViewer` (E.VIEW خط ۵۰۶) و داخل `Border Classes="wz-card"` که **`ClipToBounds=True`** دارد
  (E.VIEW خط ۸۱). ترتیب در بدنه (E.VIEW خطوط ۵۵۲–۵۶۳): VitalsStep → HistoryStep → PlanStep → **RxStep** → ReviewStep،
  همه داخل یک `StackPanel`.
- استایل سراسری `ComboBox` در `App.axaml` (خطوط ۱۵۱–۱۷۰) فقط رنگ/براش را عوض می‌کند و **popup/dropdown template را
  دست‌کاری نمی‌کند**. پس مشکل در template سراسری نیست.
- **🔴 ریشهٔ مشکل تسک ۱ (تحلیل):** توصیف کاربر «دراپ‌دون زیر می‌ماند و نمیشه چیزی انتخاب کرد». دو عامل محتمل:
  (الف) ComboBox با chrome صفرشده (border/padding=0) داخل یک Border با padding کم، و قرارگیری در یک `StackPanel` بلند
  داخل `ScrollViewer`، باعث می‌شود popup باز شود ولی به‌خاطر z-order/clip یا rowهای `ItemsControl` بعدی، لیست زیر بماند
  و کلیک به آیتم نرسد؛ (ب) امکان دارد `ServiceCatalog`/`ItemCatalog` خالی باشند و popup چیزی برای نمایش نداشته باشد
  (این را موقع اجرا تأیید کن، ولی احتمال اصلی z-order/clip است).
- **راه‌حل قطعیِ هم‌راستا با خود پروژه:** تیم همین مشکل را در **پانسیون** با **Popup صریح + PlacementTarget +
  IsLightDismissEnabled** حل کرده (STAY.VIEW خطوط ۳۸۶–۴۱۸؛ کامنت: «Avalonia has no native optgroup select»). یعنی
  الگوی تأییدشدهٔ پروژه برای این نوع picker، Popup overlay است. T1 همین الگو را به RxStep می‌آورد (جزئیات در T1).

**جریان پانسیون → صندوق (مربوط به T3) — کشف کلیدی:**
- `STAY.VM.Transfer()` (خطوط ۴۰۴–۴۱۰): `_onTransfer?.Invoke(_stay)` → میزبان dialog، `TransferModalViewModel` را باز می‌کند.
- `XFER.VM.ConfirmAsync` (خطوط ۷۲–۸۸): `_service.TransferAsync(_stay.Id)` را صدا می‌زند و toast
  «فاکتور «...» به مبلغ ... به صندوق منتقل شد» نشان می‌دهد.
- `BRD.SVC.TransferAsync` (خطوط ۹۵–۹۶): `TransferStayRoomCommand(stayId)` را می‌فرستد.
- **🔴 ریشهٔ باگ تسک ۳ (verified):** `TransferStayRoomCommand` در BRD.CMD (خطوط ۱۷۷–۲۱۱) **انتقال اتاق** است، نه
  انتقال فاکتور! Handler یک اتاق خالی هم‌نوع پیدا می‌کند و `stay.TransferRoom(target.Id, ...)` می‌زند. اگر اتاق خالیِ
  هم‌نوع نباشد → `RoomNoneAvailable` fail. permission آن `BoardingManage` است. **پس «انتقال فاکتور به صندوق» در UI
  عملاً فقط اتاق را عوض می‌کند** و toast دروغ می‌گوید، و در کلینیک تک‌اتاقه fail می‌دهد.
- **فاکتور واقعی کجا به صندوق می‌رود؟** فقط در `CheckoutStayCommand` (BRD.CMD خطوط ۲۱۳–۲۴۸): این handler مبلغ را
  محاسبه (`StayChargeCalculator.Accrued`)، total را freeze، و یک **boarding billing handoff** ثبت می‌کند
  (`handoffs.Add(...)`, خط ۲۴۶) با payload شامل اتاق + همهٔ آیتم‌های مصرفی. permission آن `BoardingCheckout`.
  این handoff همان است که `ReadyForBillingQuery` در صندوق union می‌کند و `CreateInvoiceFromStayCommand` از آن فاکتور می‌سازد.
  **پس مدل درست پانسیون: تخلیه (Checkout) → handoff خودکار به صندوق → صندوق از لیست «آمادهٔ صدور» فاکتور صادر می‌کند.**
- `BoardingStay.Transferred` (BRD.CONTR `BoardingStay.cs` خط ۹۴، کامنت خط ۷۲): «آیا فاکتور اقامت به صندوق منتقل شده».
  اما هیچ‌جا در `Client.Services/Boarding` یا `Application/.../ReadModels` این فیلد set/map نمی‌شود (verified: grep
  نتیجهٔ خالی). یعنی مفهوم `Transferred` در جریان فعلی **عملاً مرده/ناسازگار** است.
- `CHECKOUT.VM` (خطوط ۱۶–۶۵): `ShowTransferWarning = !stay.Transferred` (خط ۴۰) → بنر هشدار «فاکتور منتقل نشده» را
  وقتی `Transferred=false` نشان می‌دهد. `ConfirmAsync` → `_service.CheckoutAsync(_stay.Id)` → `CheckoutStayCommand`
  (که خودش handoff می‌سازد). guard با `InvalidOperationException` دارد (درست).
- **🔴 جمع‌بندی ناسازگاری پانسیون:** UI دو مسیر «انتقال» و «تخلیه» را جدا نشان می‌دهد، ولی Domain فقط هنگام Checkout
  فاکتور/handoff می‌سازد. دکمهٔ «انتقال به صندوق» در واقع انتقال اتاق است و toast اشتباه می‌دهد. مفهوم `Transferred`
  هم هرگز true نمی‌شود. این باید هماهنگ شود (جزئیات در T3/T4).

**افزودن خدمت/کالا به اقامت (مربوط به T3):**
- `STAY.VM.AddItemAsync` (خطوط ۳۵۱–۳۷۷): اگر `SelectedAddOption is null` toast خطا؛ وگرنه
  `_service.AddItemAsync(StayId, SelectedAddOption.Item.Ref, qty)` با guard. بعد از موفقیت `Apply(detail)` و
  `_onChanged?.Invoke()`. این متد **درست guard شده**.
- دراپ‌داون افزودن یک **Popup سفارشی** است (STAY.VIEW خطوط ۳۸۶–۴۱۸) با `IsOpen={Binding IsAddDropdownOpen}`,
  `PlacementTarget={Binding #AddPickerButton}`, `Placement=BottomEdgeAlignedLeft`, `IsLightDismissEnabled=True`.
  آیتم‌ها دکمه‌اند که `SelectAddOptionCommand` را صدا می‌زنند. این الگو **کار می‌کند** و مرجعِ راه‌حل T1 است.
- `BRD.SVC.AddItemAsync` (خطوط ۷۹–۸۳): `AddStayItemCommand` را می‌فرستد، سپس `GetDetailAsync` دوباره می‌خواند.
- `BRD.SVC.RemoveItemAsync` (خطوط ۸۵–۹۳): index-based، با `ExpectedItemId` و خطای `STAY_ITEM_INDEX_STALE` محافظت شده.
- **🟡 باگ احتمالی افزودن (تأیید حین اجرا):** `AddStayItemCommandHandler` (BRD.CMD خطوط ۱۲۸–۱۴۹) فقط وقتی
  `stay.State == StayState.Active` اجازه می‌دهد؛ اگر اقامت `RequestCheckout` شده یا Done باشد `StayNotEditable` می‌دهد.
  نکتهٔ اصلی باگ گزارش‌شده («هنگام اضافه کردن باگ نباشد») احتمالاً به index/refresh بعد از add/remove و هماهنگی با
  checkout برمی‌گردد — هنگام اجرا با سناریوی «افزودن بعد از checkout/transfer» بررسی شود. اگر add روی اقامتی که به صندوق
  رفته مجاز باشد، خطِ فاکتور با handoff ناهمگام می‌شود (چون handoff هنگام checkout snapshot شده).

### 0.7 یادداشت‌های اجرا
- هر تسک = یک تغییر منطقی + یک push. ترتیب: اول دراپ‌داون ویزیت (UI محض، کم‌ریسک)، سپس هماهنگی فاکتور ویزیت، سپس
  هماهنگی پانسیون.
- تسک‌هایی که signature قرارداد/فرمان را عوض می‌کنند (T2، T3) چند لایه را لمس می‌کنند
  (Contracts → Service → Application). UoW را atomic نگه دار.
- **اگر هنگام اجرا، تأیید روی اپ زنده نشان داد ریشهٔ واقعی با تحلیل فرق دارد، راه‌حل را بر اساس ریشهٔ واقعی تنظیم کن** و
  همان را push کن (تحلیل اینجا راه را کوتاه می‌کند، ولی کد زنده مرجع نهایی است).
- Status: `[ ]` todo · `[~]` in progress · `[x]` done. بعد از اتمام + push، باکس را به‌روز کن.

---

## TASKS

### T1 — [ ] رفع دراپ‌داون محصول/خدمات در ویزیت که «زیر می‌ماند و انتخاب نمی‌شود»
**ریشه (از §0.6):** دو `ComboBox.rx-add` در RX.VIEW (خدمات + کالا) داخل `Border.field-search` و در یک `StackPanel`
بلند درون `ScrollViewer` و `Border.wz-card` با `ClipToBounds=True`. chrome ComboBox صفر شده. popup یا زیر rowهای بعدی
می‌رود یا کلیک به آیتم نمی‌رسد.

**مرحلهٔ ۱ — تأیید سریع ریشه (قبل از تغییر):**
- اپ را اسموک کن یا کد را بخوان تا مطمئن شوی `EVM.ServiceCatalog`/`ItemCatalog` واقعاً پر می‌شوند (EVM خطوط ۲۲۱–۲۳۰
  آن‌ها را از catalog پر می‌کند). اگر خالی‌اند، ریشه = لود نشدن کاتالوگ (آن را درست کن)، نه z-order.
- اگر پر هستند (محتمل‌تر)، ریشه = رندر/z-order popup است → مرحلهٔ ۲.

**مرحلهٔ ۲ — راه‌حل انتخابی (هم‌راستا با الگوی تأییدشدهٔ خود پروژه):**
- **گزینهٔ A (کم‌هزینه، اول امتحان کن):** استایل `ComboBox.rx-add` در E.VIEW را حداقلی اصلاح کن تا popup درست باز شود:
  یک `MaxDropDownHeight` معقول (مثلاً ۲۶۰) اضافه کن و مطمئن شو ComboBox یک `MinHeight`/`Padding` کافی دارد تا hit-target
  داشته باشد. اگر صرفاً با این، باز شدن و انتخاب درست شد و clip نداشت، همین کافی است.
- **گزینهٔ B (قطعی، اگر A جواب نداد):** هر دو ComboBox را با الگوی **Popup صریح** پانسیون (STAY.VIEW ۳۸۶–۴۱۸) جایگزین
  کن: یک `Button` (نمایش `PlaceholderText`/عنوان انتخاب‌شده) + یک `Popup IsLightDismissEnabled="True"
  Placement="BottomEdgeAlignedLeft"` که `ItemsControl` روی `ServiceCatalog`/`ItemCatalog` دارد و هر آیتم دکمه‌ای است که
  خدمت/کالا را اضافه می‌کند. state باز/بستهٔ popup و آیتم انتخابی را در EVM به شکل property/command اضافه کن (مشابه
  `IsAddDropdownOpen`/`SelectAddOptionCommand` در STAY.VM). منطق افزودن موجود (`OnSelectedServiceToAddChanged`/
  `OnSelectedItemToAddChanged` → `AddLineAsync`, EVM خطوط ۲۵۰–۲۶۱) را حفظ یا به command جدید منتقل کن.
- **مهم:** popup نباید توسط `ClipToBounds` کارت clip شود (Popup در Avalonia به‌صورت overlay در top-level رندر می‌شود و
  از clip والد مصون است — همین دلیل درستیِ گزینهٔ B است).

**Acceptance:** در مرحلهٔ Rx ویزیت، کلیک روی picker خدمت/کالا لیست را **روی** بقیهٔ المان‌ها باز می‌کند، آیتم‌ها قابل کلیک‌اند،
و انتخاب یک آیتم آن را به `ServiceLines`/`ItemLines` اضافه می‌کند و subtotal را به‌روز می‌کند.
**Build:** fast 0/0. **Push (اولین push، شاخه را می‌سازد):**
`scripts/git/push.sh -b genspark_ai_developer "fix(visits): make Rx service/item picker open above layout and selectable"`

### T2 — [ ] هماهنگ‌سازی صدور فاکتور ویزیت با مدل صندوقِ جدا (آمادهٔ آنلاین)
**ریشه (از §0.6):** معماری backend درست است (Complete → handoff → صندوق از «آمادهٔ صدور» فاکتور می‌سازد). اما
`DONE.VIEW` دکمهٔ «رفتن به صندوق» → `EVM.HandoffToCashierAsync` → `BuildFromEncounterAsync` → مستقیماً
`CreateInvoiceFromVisitCommand` را از همان اکانت دکتر می‌فرستد. چون این فرمان `[RequiresPermission(SalesDraft)]` است،
در حالت آنلاین/اکانت دکتر بدون این permission **fail می‌شود**. صدور فاکتور باید کار صندوق‌دار باشد.

**راه‌حل انتخابی (کم‌ریسک، سازگار با هر دو حالت آفلاین/آنلاین):**
1. در `EVM.HandoffToCashierAsync` **صدور مستقیم فاکتور را حذف کن**. به‌جای `BuildFromEncounterAsync`، فقط به صفحهٔ
   صندوق/فروش ناوبری کن و آن را روی **لیست «آمادهٔ صدور» (Ready segment)** باز کن تا صندوق‌دار خودش ویزیتِ آماده را
   انتخاب و فاکتور کند. یعنی: `_navigation.NavigateTo<SalePageViewModel>()` (یا متد باز کردن Ready segment اگر وجود دارد؛
   در SALE.VM بخش `_segment = "ready"`/`IsReadySegment` را ببین و در صورت نبودِ متد عمومی، یک متد عمومی ساده مثل
   `ShowReadyQueue()` اضافه کن که `_segment="ready"` ست و لیست را reload کند).
2. متن دکمه و toastها نباید ادعا کنند «فاکتور صادر شد» توسط دکتر؛ پیام باید «به صف صندوق منتقل شد / در صندوق آمادهٔ صدور
   است» باشد (DONE.VIEW خط ۱۷ همین مفهوم را دارد — حفظ کن).
3. `BuildFromEncounterAsync` را **حذف نکن** — این متد برای استفادهٔ صندوق‌دار از لیست Ready (انتخاب آیتم → صدور) لازم
   است و همان‌جا با permission صندوق‌دار درست کار می‌کند. فقط مسیر فراخوانیِ آن از اکانت دکتر قطع می‌شود.
4. guard موجود `RunGuardedAsync` را حفظ کن.

**نکتهٔ بررسی حین اجرا:** مطمئن شو ناوبری به صفحهٔ صندوق، لیست Ready را refresh می‌کند (چون `EVM.CompleteAsync` قبلاً
`_changes.Notify(DataDomain.Cashier, DataDomain.Sales, ...)` می‌زند و SALE.VM با `RefreshOnDataChange(... DataDomain.Visits,
DataDomain.Cashier)` گوش می‌دهد — SALE.VM خط ۹۲). اگر ناوبری segment را روی Ready نگذاشت، مرحلهٔ ۱ را کامل کن.

**Acceptance:** بعد از «صدور فاکتور و پایان ویزیت»، صفحهٔ Done ظاهر می‌شود؛ «رفتن به صندوق» کاربر را به صفحهٔ صندوق روی
صف «آمادهٔ صدور» می‌برد که ویزیتِ تمام‌شده در آن دیده می‌شود؛ هیچ فاکتوری از اکانت دکتر مستقیم صادر نمی‌شود؛ صندوق‌دار با
permission `SalesDraft` از همان لیست فاکتور را صادر می‌کند. در حالت آفلاین تک‌اکانت هم جریان یکسان و بدون خطا کار می‌کند.
**Build:** fast 0/0. **Push:** `scripts/git/push.sh "fix(visits): route completed visit to cashier ready-queue instead of issuing invoice from doctor account"`

### T3 — [ ] هماهنگ‌سازی تخلیهٔ پانسیون و انتقال فاکتورش به صندوق + رفع باگ افزودن خدمت
**ریشه (از §0.6):** دکمهٔ «انتقال به صندوق» پانسیون در واقع `TransferStayRoomCommand` = **انتقال اتاق** را صدا می‌زند و
toast دروغ می‌دهد؛ در کلینیک تک‌اتاقه `RoomNoneAvailable` fail می‌کند. فاکتور واقعی فقط هنگام `CheckoutStayCommand`
(تخلیه) به‌صورت handoff به صندوق می‌رود. مفهوم `Transferred` هرگز set نمی‌شود.

**راه‌حل انتخابی (مدل درست = تخلیه فاکتور را به صندوق می‌فرستد):**
1. **جداسازی معنا در UI:** دکمه/مودالی که الان با عنوان «انتقال به **صندوق**» است و به `TransferAsync`(=انتقال اتاق) وصل
   است را اصلاح کن:
   - اگر مقصود «جابجایی اتاق» است، عنوان/toast را به «انتقال اتاق» تغییر بده (XFER.VM و STAY.VM `Transfer()`) تا با
     `TransferStayRoomCommand` همخوان شود و دیگر ادعای فاکتور نکند.
   - «انتقال فاکتور به صندوق» را به جریان **Checkout** واگذار کن (که واقعاً handoff می‌سازد). یعنی عملاً تنها راهِ فرستادن
     فاکتور به صندوق، «پایان اقامت/تخلیه» است.
2. **رفع مفهوم `Transferred`:** چون فاکتور هنگام Checkout به صندوق می‌رود (نه قبل از آن)، بنر `ShowTransferWarning` در
   CHECKOUT.VM (خط ۴۰) بر اساس `!stay.Transferred` بی‌معنی است (همیشه true). یا:
   - `ShowTransferWarning` را حذف کن (چون خودِ Checkout همان انتقال است)، یا
   - متن بنر را به یک توضیح درست تبدیل کن: «با تخلیه، فاکتور اقامت به‌صورت پیش‌نویس در صندوق ایجاد می‌شود».
   توست موفقیت Checkout (CHECKOUT.VM خط ۶۳) درست است؛ در صورت نیاز متمم کن که «و فاکتور به صندوق منتقل شد».
3. **تأیید رسیدن به صندوق:** مطمئن شو handoffِ ساخته‌شده در `CheckoutStayCommand` در لیست «آمادهٔ صدور» صندوق ظاهر می‌شود
   (مسیر: `ReadyForBillingQuery` → `SALE.SVC.GetReadyEncountersAsync` → `SALE.VM.ReadyList`؛ و صدور با
   `CreateInvoiceFromStayCommand` از طریق `CreateFromEncounterAsync` union — §0.6). اگر CheckoutModal بعد از تخلیه کاربر
   را به صندوق راهنمایی نمی‌کند، مثل T2 یک مسیر «رفتن به صندوق» بعد از تخلیه فراهم کن (اختیاری، اگر UX لازم داشت).
4. **رفع باگ افزودن خدمت (تأیید حین اجرا، §0.6 «افزودن»):** سناریوهای زیر را روی اپ/کد بررسی و در صورت باگ اصلاح کن:
   - افزودن آیتم بعد از `Apply(detail)` باید فهرست و جمع‌ها را بدون خطای index به‌روز کند (STAY.VM `AddItemAsync`/
     `RemoveItemAsync` را دنبال کن؛ `RemoveItemAsync` index-based است و با `STAY_ITEM_INDEX_STALE` محافظت شده — اگر بعد از
     add، index ردیف‌ها جابجا شود و remove روی index قدیمی بزند، باگ همین‌جاست → بعد از هر add/remove حتماً از `detail`
     تازه bind شود، که الان `Apply(detail)` انجام می‌دهد — تأیید کن کار می‌کند).
   - افزودن روی اقامتی که `RequestCheckout`/Done شده باید تمیز `StayNotEditable` بدهد و toast مناسب (نه crash). STAY.VM
     `IsEditable = stay.Status == Active` (خط ۲۸۱) ردیف افزودن را مخفی می‌کند — تأیید کن که بعد از Checkout، UI دیگر ردیف
     افزودن را نشان نمی‌دهد.
   - اگر باگ واقعی چیز دیگری بود (مثلاً دراپ‌داون افزودن باز نمی‌شود، یا quantity نامعتبر)، همان را دقیق اصلاح کن.

**Acceptance:** «انتقال اتاق» دیگر ادعای فاکتور نمی‌کند و در صورت نبود اتاق خطای واضح می‌دهد؛ «تخلیه/پایان اقامت» فاکتور
اقامت (اتاق + همهٔ خدمات/کالاهای افزوده‌شده) را به‌درستی به صف «آمادهٔ صدور» صندوق می‌فرستد و صندوق‌دار می‌تواند آن را
فاکتور/تسویه کند؛ افزودن خدمت/کالا به اقامت فعال بدون باگ و با جمع‌های درست کار می‌کند و بعد از تخلیه قابل ویرایش نیست.
**Build:** fast 0/0. **Push:** `scripts/git/push.sh "fix(boarding): align room-transfer vs checkout-to-cashier semantics and fix item add flow"`

### T4 — [ ] پاک‌سازی باقیماندهٔ مفهوم Transferred و پیام‌های ناهماهنگ (اگر در T3 کامل نشد)
**هدف:** اگر بعد از T3 هنوز جایی از UI/toast به «Transferred» یا «انتقال فاکتور» به شکل ناهماهنگ اشاره دارد، آن را با
مدل درست (فاکتور هنگام Checkout) هماهنگ کن. گزینه‌ها:
- اگر مفهوم `Transferred` در Contract/Domain دیگر استفادهٔ معناداری ندارد، در UI به آن تکیه نکن (نمایش‌های وابسته به آن را
  حذف/اصلاح کن). **Contract/Domain را فقط اگر لازم شد و atomic دست بزن** (پرهیز از تغییر گستردهٔ بی‌دلیل).
- مطمئن شو هیچ toast «موفقیت» دروغ نمی‌گوید (قانون §0.2).
**نکته:** اگر T3 همهٔ این موارد را پوشش داد، این تسک فقط «تأیید» است → یک commit کوچک یا اگر هیچ تغییری لازم نبود،
این تسک را رد کن و در باکس `[x]` با یادداشت «subsumed by T3» بگذار (نیازی به push خالی نیست).
**Build:** fast 0/0 (اگر کدی عوض شد). **Push (اگر تغییر بود):** `scripts/git/push.sh "chore(boarding): remove stale Transferred UI remnants"`

### T5 — [ ] تأیید نهایی هماهنگی سه جریان (focused، بدون full test)
**هدف:** با خواندن کد (نه full test)، این زنجیره‌ها را end-to-end تأیید کن:
- ویزیت: Rx picker کار می‌کند (T1) → Complete → Done → «رفتن به صندوق» → صف Ready صندوق (T2).
- پانسیون: افزودن خدمت (T3) → تخلیه → handoff → صف Ready صندوق → فاکتور (T3).
- هر دو جریان از **همان** مسیر `CreateFromEncounterAsync`/`ReadyForBillingQuery` در صندوق فاکتور می‌شوند (§0.6) → یعنی
  معماری برای اکانت صندوقِ جدا در حالت آنلاین هماهنگ و استاندارد است.
**اگر در تأیید، ناهماهنگی باقی‌مانده پیدا شد**، آن را اصلاح و push کن (پیام واضح).
**Build:** fast 0/0 نهایی. **Push (اگر تغییر بود):** `scripts/git/push.sh "test(flows): verify visit+boarding cashier handoff coherence"`

### T6 — [ ] بیلد سریع نهایی و اطمینان از ۰/۰
`scripts/sandbox/build.sh` → باید ۰ warning / ۰ error. اگر startup/DI/shell لمس شده بود، `scripts/sandbox/run.sh`
اسموک بزن تا `SMOKE RUN OK`. هر اصلاح لازم را push کن.
**Push (اگر تغییر بود):** `scripts/git/push.sh "chore: fast build green after visit/cashier/boarding fixes"`

### T7 — [ ] حذف همین فایل پلن (تسک پایانی)
`rm Plan/visit-cashier-boarding-fixes.md` — **فقط همین فایل.** به فایل‌های دیگر Plan/ و هیچ docs/roadmap دست نزن.
**Push:** `scripts/git/push.sh "chore(plan): remove completed visit/cashier/boarding fixes plan"`
