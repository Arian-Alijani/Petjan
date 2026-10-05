# Plan — Boarding Settings + Sales/Cashier fixes

> **زبان:** توضیحات به فارسی، مسیر فایل‌ها/کد به انگلیسی.
> **این فایل خودبسنده است:** تمام مسیرهای فایل، شماره خط‌های کلیدی، ریشهٔ هر باگ و روش رفع آن استخراج و اینجا نوشته شده. برای شروع پیاده‌سازی **نیازی به بررسی دوبارهٔ ریپو نیست**؛ فقط فایل‌های ذکرشده را باز کن و تغییر بده.

---

## قانون قفل‌شدهٔ پوش (اجباری)
بعد از **اتمام هر تسک** (یا هر فیکس سنگین/کد زیاد) این دستور را اجرا کن:
```bash
scripts/git/push.sh "feat(scope): شرح کوتاه"
```
- اگر exit code = `3` → ابزار `setup_github_environment` را یک‌بار صدا بزن، سپس همان دستور را دوباره اجرا کن.
- اگر exit code = `2` (کانفلیکت rebase) → حل کن (اولویت با کد remote) و دوباره اجرا کن.
- بعد از هر پوش روی `genspark_ai_developer`، برنچ `main` را هم هماهنگ نگه‌دار (هماهنگی مستقیم هر دو برنچ طبق درخواست کاربر). روش:
  ```bash
  scripts/git/push.sh -b genspark_ai_developer "feat(...): ..."   # کار اصلی اینجا
  # سپس در صورت نیاز merge به main:
  git switch main && git merge --ff-only genspark_ai_developer && scripts/git/push.sh && git switch genspark_ai_developer
  ```
- **پوش نکردن = شکست پروژه.**

---

## بیلد و تست (محدودیت‌ها)
- بعد از تغییر کد: `scripts/sandbox/build.sh` (باید 0/0 باشد). **Full test / بیلد طولانی ممنوع.**
- بررسی هدفمند فایل‌ها؛ بررسی بیهوده ممنوع.

---

## معماری پروژه (خلاصهٔ لازم — دیگر نیازی به کشف نیست)
پروژهٔ **Animora** (مدیریت کلینیک دامپزشکی)، .NET + Avalonia UI، RTL/فارسی. لایه‌ها:
- `src/Animora.Domain/` — Entityها و قواعد دامنه (مثلاً `Room`, `BoardingServiceDef`, `Invoice`).
- `src/Animora.Application/UseCases/<Area>/` — Commands/Queries + Handlerها. Ports در `src/Animora.Application/Ports/`.
- `src/Animora.Contracts/<Area>/` — interfaceهای seam که ViewModelها به آن‌ها وابسته‌اند + مدل‌های قرارداد.
- `src/Animora.Client.Services/<Area>/` — پیاده‌سازی seam (dispatch command/query به Application).
- `src/Animora.Persistence.Sqlite/` — EF Core: `Queries/`, `Repositories/`, `Configurations/`, `Seed/`, `Migrations/`.
- `src/Animora.Desktop/Pages/<Area>/` — هر صفحه: `XxxPageView.axaml` + `XxxPageViewModel.cs` + مودال‌ها.
  - الگوی مودال: `_dialogs.ShowModal(new SomeModalViewModel(...), width)`.
  - دیالوگ تأیید: `Shared.ConfirmDialogViewModel` (نمونه در `SalePageViewModel.VoidInvoice`, خط ~409).
  - پیام‌های خطای فارسی: `src/Animora.Desktop/Localization/ErrorMessages.cs` (map از ErrorCode به متن).
- ثبت DI: هر Area یک `XxxRegistration.cs` در Application + ثبت repository در Persistence.
- آیکن‌ها در XAML: `{StaticResource IconX}`, `IconTrash`, `IconChevronDown`, `IconArrowDownLeft` و… (StaticResource).

---

# تسک‌ها

وضعیت: `[ ]` انجام‌نشده · `[~]` در حال انجام · `[x]` انجام‌شده

---

## تسک 1 — صفحهٔ پانسیون ‹تنظیمات›: اتاق‌ها و خدمات قابل تنظیم شوند  `[x]`

**خواسته:** در تب تنظیمات پانسیون، خدمات پانسیون + اتاق‌ها و نرخ‌هایشان و همهٔ مشخصه‌هایشان قابل ویرایش شوند و بتوان مورد جدید اضافه کرد.

### وضعیت فعلی (کشف‌شده)
- `src/Animora.Desktop/Pages/Boarding/BoardingSettingsTabView.axaml` **فقط read-only** است. کامنت بالای فایل صراحتاً می‌گوید کارت‌های افزودن/ویرایش حذف شده‌اند چون «backend contract برای persist وجود نداشت». **این فرض دیگر درست نیست** (پایین را ببین).
- ViewModel مربوطه: `BoardingPageViewModel` (همین فایل `x:DataType`). property‌ها: `RoomTiles` (از `BoardingRoomTileViewModel`) و `ServiceRows` (از `BoardingServiceRowViewModel`).
- داده از `IBoardingQueryService` می‌آید (`src/Animora.Contracts/Boarding/IBoardingQueryService.cs`): متدهای `GetRoomsAsync()` و `GetServicesAsync()` **فقط read** هستند؛ هیچ mutation برای room/service ندارد.

### نکات کلیدی (صرفه‌جویی توکن)
1. **Domain کامل است:**
   - `src/Animora.Domain/Boarding/Room.cs` → `Room.Create(tenantId, code, displayName, RoomType, capacity, notes)`، `Rename(displayName, notes)`، `SetCapacity(int)` (بازهٔ [1,10])، `SetStatus(RoomStatus)`. enumها: `RoomType{Standard,Vip,Isolation,Cage,Suite}`, `RoomStatus{Available,Occupied,Maintenance,Retired}`. قاعده: `Retired → Available` ممنوع (`ROOM_UNAVAILABLE`).
   - `src/Animora.Domain/Boarding/BoardingServiceDef.cs` → `Create/Update/Activate/Deactivate` + `BoardingServiceBilling`.
2. **خدمات پانسیون از قبل CRUD کامل دارند** و نیاز به کار بک‌اند ندارند:
   - `src/Animora.Application/UseCases/Sales/ServiceRateCommands.cs` شامل: `CreateBoardingServiceRateCommand(Title, PriceMinor, Billing)`، `UpdateServiceRateCommand(Id, Source, Title, PriceMinor, IsActive)` با `Source="Boarding"` → روی `BoardingServiceDef` اعمال می‌شود (`IBoardingServiceRepository`). همچنین `CreateServiceRateCommand` برای خدمات کلینیک.
   - UI آمادهٔ این‌ها: `src/Animora.Desktop/Pages/Sales/ServiceRatesView.axaml` + `ServiceRatesViewModel.cs` + `ServiceRateEditModalViewModel.cs`. این ویرایشگر هم‌اکنون فقط در صفحهٔ Sales استفاده می‌شود (`SalePageView.axaml`).
3. **اتاق‌ها هیچ mutation ندارند** — این بخشِ «واقعاً جدید» تسک است:
   - `Set<Room>` در `src/Animora.Persistence.Sqlite/Queries/BoardingQueries.cs` خوانده می‌شود.
   - Configuration: `src/Animora.Persistence.Sqlite/Configurations/Boarding/RoomConfiguration.cs`.
   - Seed: `src/Animora.Persistence.Sqlite/Seed/BoardingSampleData.cs`.
   - **هیچ `IRoomRepository` / RoomRepository / Room command وجود ندارد.**

### کارهای لازم (گام‌به‌گام)
**الف) خدمات پانسیون (کم‌هزینه — استفادهٔ مجدد):**
- گزینهٔ ساده و توصیه‌شده: در تب تنظیمات پانسیون، به‌جای `ItemsControl` فقط‌خواندنی، همان `ServiceRatesView` (یا یک نسخهٔ فیلترشده روی `Source=Boarding`) را embed کن، یا یک دکمهٔ «ویرایش/افزودن خدمت» که مودال `ServiceRateEditModalViewModel` را باز می‌کند.
- اگر فیلتر لازم شد: در `ServiceRatesViewModel` منطق نمایش فقط ردیف‌های `Source=="Boarding"` را اضافه کن (پراپرتی/سازندهٔ اختیاری). از ساختن command جدید پرهیز کن — `CreateBoardingServiceRateCommand`/`UpdateServiceRateCommand(Source="Boarding")` کافی است.

**ب) اتاق‌ها (کار جدید بک‌اند + UI):**
1. **Port:** `src/Animora.Application/Ports/IRoomRepository.cs` بساز (الگو از `IBoardingServiceRepository.cs`): `Task<Room?> FindAsync(string id, CancellationToken)`, `void Add(Room)`, و در صورت نیاز `Task<IReadOnlyList<Room>> ListAsync(...)` (یا از query موجود استفاده کن).
2. **Repository:** `src/Animora.Persistence.Sqlite/Repositories/RoomRepository.cs` (الگو از `BoardingServiceRepository.cs`) با `db.Set<Room>()`.
3. **ثبت DI:** RoomRepository را همان‌جایی ثبت کن که `BoardingServiceRepository` ثبت شده (دنبال `AddScoped<IBoardingServiceRepository` در Persistence بگرد).
4. **Commands:** فایل `src/Animora.Application/UseCases/Boarding/RoomCommands.cs` بساز:
   - `CreateRoomCommand(Code, DisplayName, RoomType, Capacity, Notes?)` → `Room.Create(...)` → `rooms.Add(...)`.
   - `UpdateRoomCommand(Id, DisplayName, Capacity, RoomStatus, Notes?)` → `FindAsync` → `Rename` + `SetCapacity` + `SetStatus`.
   - از `[RequiresPermission(PermissionCodes.SettingsWrite)]` و `[Auditable(AuditCategory.Configuration, ...)]` استفاده کن (الگو از `ServiceRateCommands.cs`). Validator هم مثل همان‌جا.
   - این Commandها را در `src/Animora.Application/UseCases/Boarding/BoardingRegistration.cs` با `.AddCommand<...>()` ثبت کن (الگوی موجود در همان فایل).
5. **Contract seam:** به `src/Animora.Contracts/Boarding/IBoardingQueryService.cs` اضافه کن:
   - `Task<RoomInfo> CreateRoomAsync(NewRoomRequest request);`
   - `Task<RoomInfo> UpdateRoomAsync(string roomId, UpdateRoomRequest request);`
   - رکوردهای `NewRoomRequest`/`UpdateRoomRequest` را کنار بقیهٔ رکوردها در Contracts تعریف کن.
6. **پیاده‌سازی seam:** در `src/Animora.Client.Services/Boarding/BoardingQueryService.cs` این متدها را با `SendAsync(new CreateRoomCommand(...))` پیاده کن و `RoomInfo` را برگردان (مثل بقیهٔ متدها). مَپینگ احتمالی در `src/Animora.Client.Services/Boarding/BoardingMapping.cs`.
7. **UI:** در `BoardingSettingsTabView.axaml` برای هر `room-tile` دکمهٔ ویرایش و یک دکمهٔ «+ افزودن اتاق» بگذار؛ یک مودال سادهٔ Room (code/name/type/capacity/status/notes) بساز (الگوی مودال از `ServiceRateEditModalViewModel`). در `BoardingPageViewModel` دو `[RelayCommand]` (Create/Edit room) اضافه کن که مودال را باز و بعد `RoomTiles` را refresh کند.
8. **Migration:** چون جدول `Rooms` از قبل وجود دارد، معمولاً migration جدید لازم نیست مگر فیلدی به Room اضافه کنی (نکن). اگر لازم شد، از الگوی `Migrations/` پیروی کن — ولی ترجیحاً از افزودن ستون پرهیز کن.

### معیار پذیرش
- در تب تنظیمات پانسیون می‌توان اتاق جدید افزود و اتاق موجود (نام/ظرفیت/وضعیت/نوع) را ویرایش کرد و تغییر persist می‌شود.
- می‌توان خدمت پانسیون جدید افزود و خدمت موجود (عنوان/نرخ/فعال‌بودن) را ویرایش کرد.
- بیلد 0/0.
- **پوش.**

---

## تسک 2 — فروش و صندوق

### 2a — مشتری گذری نباید در تسویه بلاک شود  `[x]`
**خواسته:** در حالت مشتری گذری، تسویه نباید «انتخاب مشتری» را اجبار کند؛ و اطلاعات گذری باید در تاریخچهٔ اصلی و تب تاریخچهٔ صندوق درست ثبت شود.

**ریشهٔ باگ (قطعی، کشف‌شده):**
- فایل: `src/Animora.Application/UseCases/Sales/PostInvoiceCommand.cs` — در `PostInvoiceCommandHandler.HandleAsync`:
  ```csharp
  if (string.IsNullOrWhiteSpace(invoice.OwnerId)) return Result<Unit>.Fail(ErrorCodes.InvoiceOwnerRequired);
  ```
- جریان پرداخت «pay-on-post» است: `SalesQueryService.TakePaymentAsync` ابتدا invoice را Post می‌کند. فاکتور گذری `OwnerId=null` دارد (نام در `GuestName` نگه‌داشته می‌شود) → Post با `InvoiceOwnerRequired` fail می‌شود → پرداخت انجام نمی‌شود.
- پیام فارسی: `ErrorMessages.cs` کلید `INVOICE_OWNER_REQUIRED` = «انتخاب مشتری برای این فاکتور الزامی است.» → همان خطایی که کاربر می‌بیند.
- دامنه از گذری پشتیبانی می‌کند: `src/Animora.Domain/Sales/Invoice.cs` → `OwnerId` nullable (خط ~105)، `GuestName` (خط ~117)، در ساخت گذری `OwnerId=null, GuestName=<name>`.
- تاریخچه از قبل گذری را درست نشان می‌دهد: `src/Animora.Persistence.Sqlite/Queries/CashierQueries.cs` (خط ~76) و `SalesQueries.cs` از الگوی `owner == null ? i.GuestName ?? "" : o.FullName` استفاده می‌کنند. پس وقتی Post موفق شود، ثبت تاریخچه هم خودکار درست است.

**رفع:** شرط را طوری تغییر بده که فقط وقتی **هم OwnerId و هم GuestName خالی‌اند** fail کند:
```csharp
if (string.IsNullOrWhiteSpace(invoice.OwnerId) && string.IsNullOrWhiteSpace(invoice.GuestName))
    return Result<Unit>.Fail(ErrorCodes.InvoiceOwnerRequired);
```
- مطمئن شو `invoice.GuestName` از دامنه قابل‌دسترسی است (public getter دارد — تأیید شده خط ~117).
- اگر جای دیگری هم همین چک تکرار شده، grep کن: `grep -rn "InvoiceOwnerRequired" src/Animora.Application`.

**معیار پذیرش:** یک فاکتور گذری (بدون انتخاب مالک) را می‌توان تسویه کرد؛ در تاریخچهٔ اصلی و تب تاریخچهٔ صندوق با نام «مشتری گذری/مهمان» ثبت می‌شود. بیلد 0/0. **پوش.**

---

### 2b — باکس نام مشتری با فلش، قابل‌کلیک برای تعویض مشتری  `[x]`
**خواسته:** وقتی فاکتور ساخته و مشتری انتخاب شد، نام مشتری در یک باکس با فلش کنارش قرار گیرد؛ با کلیک روی این باکس، پنل سرچ انتخاب بیمار/مالک دوباره باز شود تا بتوان شخص را عوض کرد یا گذری زد (برای وقتی اشتباه انتخاب شده).

**وضعیت فعلی (کشف‌شده):**
- باکس مشتری («sw-cust») در `src/Animora.Desktop/Pages/Sales/SalePageView.axaml` حوالی خط ~525–540 وجود دارد و یک آیکن chevron (`IconChevronDown`) هم دارد — **ولی فقط Path است، کلیک‌پذیر نیست** (دکمه/command ندارد).
- زیرساخت کامل آماده است:
  - `SaleInvoiceViewModel` (`.../Sales/SaleInvoiceViewModel.cs`): `CustomerName`, `Phone`, `IsGuest` از قبل ساخته می‌شوند. برای گذری: `CustomerName="مشتری گذری — انتخاب مشتری"`, `Phone="بدون پرونده — برای انتخاب کلیک کنید"`.
  - مودال انتخاب: `OwnerPickerModalViewModel` (`.../Sales/OwnerPickerModalViewModel.cs`) — هم مالک دارای پرونده و هم «گذری» را پشتیبانی می‌کند.
  - متد seam: `SalesQueryService.PickOwnerAsync(invoiceId, OwnerPickResult)` (خط ~240) + `ISalesQueryService.PickOwnerAsync` (خط ~152) از قبل وجود دارند.
  - الگوی باز کردن picker: `SalePageViewModel.NewBlankInvoice` (خط ~352) → `_dialogs.ShowModal(new OwnerPickerModalViewModel(_service,_toast,_dialogs, detail.Id, RefreshAfterMutation), 560);`

**رفع:**
1. در `SalePageViewModel` یک `[RelayCommand]` به‌نام `ChangeCustomer` اضافه کن:
   ```csharp
   if (SelectedInvoice is null) return;
   _dialogs.ShowModal(new OwnerPickerModalViewModel(_service,_toast,_dialogs, SelectedInvoice.Id, RefreshAfterMutation), 560);
   ```
2. در `SalePageView.axaml` باکس «sw-cust» (خط ~525–540) را به یک `Button` (با `Classes` بدون استایل دکمه‌ای مزاحم، یا `Background=Transparent`) تبدیل کن که `Command="{Binding ChangeCustomerCommand}"` دارد و همان محتوای فعلی (نام + chevron) را نگه می‌دارد. chevron را سمت مناسب (در RTL) حفظ کن.
3. مطمئن شو بعد از تعویض، `RefreshAfterMutation` فاکتور را refresh می‌کند (از قبل این الگو کار می‌کند).

**معیار پذیرش:** با کلیک روی باکس نام مشتریِ یک فاکتور باز، picker باز می‌شود و می‌توان مشتری را عوض/گذری کرد و تغییر در فاکتور اعمال می‌شود. بیلد 0/0. **پوش.**

---

### 2c — آیکن ضربدر برای حذف فاکتور باز در پنل چپ  `[ ]`
**خواسته:** در پنل سمت چپ که فاکتورهای باز هستند، روی هر کارت فاکتور بالا سمت چپ یک آیکن ✕ باشد که با کلیک، آن فاکتور باز کاملاً حذف شود؛ قبل از حذف از کاربر تأیید گرفته شود.

**وضعیت فعلی (کشف‌شده):**
- کارت‌های سایدبار فاکتور در `SalePageView.axaml` حوالی خط ~940–1180؛ هر کارت یک `Button` با `SelectInvoiceCommand` است.
- متد seam حذف **از قبل موجود است**: `SalesQueryService.DeleteInvoiceAsync(invoiceId)` (خط ~250) → `DeleteDraftInvoiceCommand` (فقط draft). `ISalesQueryService.DeleteInvoiceAsync` (خط ~125).
- دیالوگ تأیید: `Shared.ConfirmDialogViewModel` — الگوی کامل در `SalePageViewModel.VoidInvoice` (خط ~409) موجود است.
- آیکن: `{StaticResource IconX}` (در همین XAML جای دیگری استفاده شده، خط ~509/863).

**رفع:**
1. در `SalePageViewModel` یک `[RelayCommand]` به‌نام `DeleteOpenInvoice(string? id)` اضافه کن که:
   - ابتدا `ConfirmDialogViewModel` با عنوان «حذف فاکتور» و متن تأیید نمایش دهد (الگوی `VoidInvoice`).
   - روی تأیید: `await _service.DeleteInvoiceAsync(id)` سپس `ReloadListsAsync()` + `RenderSidebar()` + اگر فاکتورِ انتخاب‌شده همان بود، `ShowEmptyWork()` و `SelectedInvoice=null`.
   - توجه: حذف فقط برای draft مجاز است؛ برای فاکتور posted متد `null` برمی‌گرداند — پیام مناسب نشان بده یا دکمه را فقط برای draftها نمایش بده (`IsVisible` بر اساس state).
2. در هر کارت سایدبار (خط ~940–1180)، یک `Button` کوچک با آیکن `IconX` در گوشهٔ بالا-چپ (در RTL: `HorizontalAlignment` مناسب) اضافه کن:
   ```xml
   Command="{Binding $parent[UserControl].((sales:SalePageViewModel)DataContext).DeleteOpenInvoiceCommand}"
   CommandParameter="{Binding InvoiceId}"
   ```
   - با `ZIndex`/`Panel` طوری بچین که کلیک روی ✕ باعث trigger شدن `SelectInvoice` کارت نشود (دکمهٔ جدا روی لایهٔ بالاتر).
   - فقط برای ردیف‌های draft نمایش بده (`SaleSidebarRowViewModel` state دارد).

**معیار پذیرش:** هر فاکتور باز (draft) در سایدبار یک ✕ بالا-چپ دارد؛ کلیک → دیالوگ تأیید → حذف کامل. بیلد 0/0. **پوش.**

---

### 2d — حذف باکس رنگی خالی (آبی) در فاکتور و صف ویزیت  `[ ]`
**خواسته:** وقتی فاکتور باز است، بالا سمت چپ یک باکس آبی/رنگی بدون محتوا هست → کاملاً حذف شود. همچنین روی کارت‌های فاکتور در «بخش باکس فاکتور و صف ویزیت» باکس رنگی مشابهی هست که باید حذف شود. (کاربر تأکید کرده دقیق بفهم چه چیزی را می‌گوید.)

**وضعیت (نیاز به تأیید بصری — تنها مورد مبهم):**
- کاندیدای اصلی در work-surface head: badge وضعیت (`StateBgBrush`) — `SaleInvoiceViewModel.StateBgBrush` از `SalePresentation.StateDotBrush(state)` می‌آید. در `SalePageView.axaml` حوالی خط ~470–500 (ناحیهٔ «شمارهٔ فاکتور» + badge وضعیت، `Grid.Column` سمت چپ در RTL).
- روی کارت‌های سایدبار: نوار/مربع رنگی کوچک (candidate: `Width="6/7"` نوار رنگی یا badge `SourceLabel`). در `SaleItemViewModels.cs` → `SaleSidebarRowViewModel` دارای `SourceLabel`, `SourceIconKey`.
- ناحیهٔ صف ویزیت/صندوق: `src/Animora.Desktop/Pages/Cashier/CashierPageView.axaml` (badgeها با `Brand50Brush`/`Bg2Brush`, نوار `CornerRadius` کوچک حوالی خط ~547/822/825).

**روش رفع (هوشمندانه، کم‌هزینه):**
1. **قبل از حذف، یک‌بار بصری تأیید کن** (چون «آبی بدون محتوا» از روی کد مبهم است): اسکرین‌شات/اجرای صفحه یا پرسش کوتاه از کاربر دربارهٔ محل دقیق. فقط همان المان بی‌محتوا را حذف کن، نه badgeهای دارای متن.
2. المانِ هدف احتمالاً یک `Border` با `Background` رنگی و بدون `Child` معنادار (یا یک نوار تزئینی) است. آن `Border` را از XAML حذف کن و Grid/`ColumnDefinitions` را متناسب اصلاح کن تا layout نشکند.
3. همان المان را هم در کارت فاکتور (سایدبار) و هم در صف ویزیت/صندوق پیدا و حذف کن (grep رنگ: `Background="#...Blue"` یا `Brand*Brush` بی‌محتوا).

**معیار پذیرش:** باکس رنگی بی‌محتوا از work-surface و از کارت‌های فاکتور/صف ویزیت حذف شده؛ layout سالم است. بیلد 0/0. **پوش.**

---

### 2e — کالاها/خدمات هنگام افزودن اقلام دیده نمی‌شوند  `[ ]`
**خواسته:** وقتی کالا اضافه می‌کنم، هنگام افزودن اقلام در پانسیون/ویزیت/فروش آن کالا (یا حتی خدمات) را نمی‌بینم. همهٔ موارد این‌چنینی بررسی و رفع شوند.

**کشفیات (مهم — بخشی از مشکل قبلاً حل شده):**
- Catalog query **درست است** و fallback دارد: `src/Animora.Persistence.Sqlite/Queries/SalesQueries.cs` → `GetCatalogAsync` (خط ~145–185):
  - ردیف‌های per-variant برای محصولات دارای variant فعال.
  - **plus** یک ردیف product-level برای هر محصول فعالِ بدون variant فعال (کامنت: «Without it those products silently vanished from the drawer»). یعنی محصول بدون variant هم نمایش داده می‌شود.
  - خدمات پانسیون: از `BoardingServiceDef` با `IsActive` (خط ~172).
- مَپینگ Kind درست است: `SalesMapping.ToContract` روی `SalesCatalogReadModel` → `"Service"→"svc"`, `"Item"→"item"` (خط ~202–260)؛ فیلتر CatalogModal هم با همین سازگار است. پس **فیلتر/دسته‌بندی باگ ندارد.**
- مودال catalog: `src/Animora.Desktop/Pages/Sales/CatalogModalViewModel.cs` یک‌بار در `LoadAsync` snapshot را می‌گیرد.

**علل محتمل باقی‌مانده (به‌ترتیب اولویت):**
1. **کالای تازه‌افزوده بلافاصله دیده نمی‌شود چون catalog تازه‌سازی نمی‌شود / cache است.** بررسی کن آیا `GetCatalogAsync` نتیجه را cache می‌کند یا هر بار تازه می‌خواند؛ و آیا بعد از ساخت محصول در Inventory، snapshot فروش/پانسیون/ویزیت باطل می‌شود. اگر cache هست → invalidation بعد از mutation محصول اضافه کن یا هر بار باز شدن مودال تازه بخوان.
2. **NewProductModal همیشه `ProductKind.PetShop` می‌سازد (نه Service).** در `src/Animora.Client.Services/Inventory/InventoryQueryService.cs` → `CreateProductAsync` (خط ~119–200):
   ```csharp
   new CreateProductCommand(code, request.Name, request.CategoryId, "PCS", ProductKind.PetShop, ...)  // همیشه PetShop
   ```
   - اثر: محصولی که کاربر به‌عنوان «خدمت» می‌سازد، به‌جای Service به‌عنوان کالای فیزیکی ثبت می‌شود (در فیلتر svc دیده نمی‌شود، در item دیده می‌شود). این «گم‌شدن خدمت» را توضیح می‌دهد.
   - رفع: اگر `NewProductRequest` فیلدی برای نوع (کالا/خدمت) دارد یا باید اضافه شود، آن را به `ProductKind` درست map کن. خدمات کلینیک مسیر درست خودشان را دارند (`CreateServiceRateCommand` → `ProductKind.Service`, `trackStock:false`) — بهتر است افزودن «خدمت» از مسیر ServiceRate انجام شود، نه NewProductModal.
3. **محصولات چند-variant با قیمت 0:** در `CreateProductAsync` وقتی `HasVariants=true`، variantها با قیمت 0 ساخته می‌شوند (مودال قیمت per-variant نمی‌گیرد). این‌ها در catalog دیده می‌شوند ولی با قیمت 0 — مشکل ثانویه؛ در صورت نیاز قیمت پیش‌فرض/اجبار قیمت اضافه کن.

**روش رفع (هدفمند):**
1. اول **بازتولید** کن: کدام مورد؟ (الف) کالای معمولی تازه دیده نمی‌شود، یا (ب) خدمت دیده نمی‌شود. با توجه به کشفیات، (ب) تقریباً قطعاً به‌خاطر `ProductKind.PetShop` ثابت در NewProductModal است و (الف) به‌خاطر نبود refresh/cache.
2. **رفع refresh:** مطمئن شو catalog هنگام باز شدن picker در Sales/Boarding/Visits تازه از DB خوانده می‌شود (یا بعد از ساخت محصول invalidate می‌شود). `GetCatalogAsync` در seamها: `SalesQueryService.GetCatalogAsync` و `IBoardingQueryService.GetCatalogAsync` (همان snapshot را می‌دهد).
3. **رفع نوع محصول:** مسیر ساخت «خدمت» را به `ProductKind.Service` درست کن (ترجیحاً از ServiceRate editor؛ یا در NewProductModal انتخاب نوع اضافه کن و در `CreateProductAsync` map کن).
4. picker پانسیون و ویزیت همان `CatalogSnapshot` فروش را مصرف می‌کنند (`IBoardingQueryService.GetCatalogAsync` صراحتاً همان snapshot فروش است) — پس رفع در منبع، هر سه سطح را درست می‌کند.

**معیار پذیرش:** کالای تازه‌افزوده و خدمت تازه‌افزوده بلافاصله در picker افزودن اقلام (فروش/پانسیون/ویزیت) دیده می‌شوند و با قیمت درست افزوده می‌شوند. بیلد 0/0. **پوش.**

---

## تسک پایانی — حذف این فایل پلن  `[ ]`
بعد از اتمام و پوش همهٔ تسک‌های بالا، **فقط همین فایل** (`Plan/sales-boarding-settings-fixes.md`) را حذف کن (به سایر فایل‌های Plan و roadmap دست نزن)، سپس پوش کن:
```bash
rm Plan/sales-boarding-settings-fixes.md
scripts/git/push.sh "chore(plan): remove completed sales-boarding plan"
```

---

## جمع‌بندی سریع برای مدل بعدی (TL;DR)
| تسک | ریشه/محل | کار اصلی | زیرساخت آماده؟ |
|---|---|---|---|
| 1 خدمات پانسیون | `ServiceRateCommands.cs` + `ServiceRatesView` | embed/دکمهٔ ویرایش | بله، کامل |
| 1 اتاق‌ها | `Room.cs` آماده، repo/command نیست | ساخت IRoomRepository+RoomRepository+RoomCommands+seam+UI | دامنه آماده |
| 2a گذری | `PostInvoiceCommand.cs` چک `OwnerId` | شرط را به `OwnerId && GuestName` خالی تغییر بده | بله |
| 2b تعویض مشتری | `SalePageView.axaml` باکس sw-cust + `PickOwnerAsync` | باکس را Button + command کن | بله، کامل |
| 2c حذف فاکتور | `DeleteInvoiceAsync` + `ConfirmDialogViewModel` | ✕ روی کارت + command + تأیید | بله، کامل |
| 2d باکس آبی خالی | work-surface head + کارت‌ها + صف صندوق | تأیید بصری سپس حذف Border بی‌محتوا | — (مبهم، تأیید بصری) |
| 2e دیده‌نشدن کالا/خدمت | cache/refresh + `ProductKind.PetShop` ثابت در NewProductModal | refresh catalog + fix نوع محصول | بخشی حل‌شده (fallback query) |

**به‌خاطر بسپار: بعد از هر تسک → `scripts/git/push.sh` و هماهنگی هر دو برنچ.**
