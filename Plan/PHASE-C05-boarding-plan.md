# Plan — Phase C05 (Boarding) — Execution TODO

> این فایل تنها منبع کاری برای اجرای فاز C05 است. با خواندن آن، بدون بررسی دوبارهٔ repo مستقیم
> پیاده‌سازی را شروع کن. فایل مرجع فاز: `backend-roadmap/phases/PHASE-C05-boarding.md`.
>
> قانون قفل‌شدهٔ پوش: بعد از تکمیل هر تسک (T#) بلافاصله اجرا کن:
> `scripts/git/push.sh "feat(boarding): <پیام کوتاه>"` — اگر کد 3 برگشت، ابزار
> `setup_github_environment` را یک‌بار صدا بزن و دوباره اجرا کن. پوش نکردن = شکست.

---

## بخش ۰ — درک فاز (خلاصهٔ خالص)

هدف: صفحهٔ Boarding روی دادهٔ واقعی `BoardingStay` / `Room` / `BoardingServiceDef` اجرا شود: اقامت‌ها با
accrual روزانه، آیتم‌های افزوده، انتقال اتاق و checkout که همان **billing handoff** فاز C03 را تولید
می‌کند تا C07 بتواند یک stay را دقیقاً مثل یک visit فاکتور کند.

اصول ثابت هر فاز:
- accrual مشتق است، هرگز به‌صورت total ذخیره نمی‌شود (مثل inventory movement-based).
- هر projection تک‌statement، بدون N+1؛ history صفحه‌بندی با سقف 200.
- هر mutation یک journaled command است (`IJournaledCommand`).
- mock در همان commit سوییچ DI حذف می‌شود (CONVENTIONS §10).
- بدون متن فارسی در Application/Client.Services؛ خطا = `ErrorCode`.
- شب‌ها در تایم‌زون کلینیک از طریق `ClinicCalendar` شمرده می‌شوند، هرگز `DateTime.Now`.

---

## بخش ۱ — انحرافات کلیدی از متن فاز (حتماً اعمال کن — کد واقعی repo حاکم است)

متن فاز C05 قبل از پیاده‌سازی واقعی نوشته شده و با کد فعلی repo اختلاف دارد. این انحرافات را در
PROGRESS.md (تسک T13) به‌عنوان Deviations ثبت کن.

### D1 — شمارهٔ migration: 012 نه 011
متن فاز `011_BoardingHandoff` می‌گوید، اما 011 توسط C04 گرفته شده
(`20260928090000_011_StaffWorkingHours`). آخرین migration = 011. migration جدید باید `012_BoardingHandoff`
باشد. (همان الگوی انحراف C01=009 و C03=010.)

### D2 — نام billing enum فرق دارد
متن فاز: `PerNight/OneTime/PerUnit`. کد واقعی (`BoardingServiceDef.cs`):
`enum BoardingServiceBilling { PerStay=0, PerDay=1, PerUse=2 }`. نگاشت برای calculator:
- `PerDay` ↔ per-night → ضرب در NightsBetween (حداقل 1)
- `PerStay` ↔ one-time → یک‌بار (× Quantity، معمولاً 1)
- `PerUse` ↔ per-unit → × Quantity

### D3 — فیلدهای تاریخ BoardingStay از نوع DateTimeOffset اند نه long
کد واقعی: `CheckInUtc`(DateTimeOffset), `PlannedCheckOutUtc`(DateTimeOffset),
`ActualCheckOutUtc`(DateTimeOffset?). متن فاز `long asOfUtc` گفته. امضای `Accrued` را با
`DateTimeOffset asOf` بگیر؛ برای `NightsBetween` به `UtcTicks` تبدیل کن. `CheckOutUtc ?? asOf` =
`ActualCheckOutUtc ?? asOf`.

### D4 — StayItem فیلدهایش فرق دارد
کد واقعی (record `StayItem`): `ItemId, Kind(StayItemKind), CatalogItemId, Title, Quantity(Quantity VO),
UnitPrice(Money), ConsumedUtc(DateTimeOffset), RecordedByUserId`. هیچ `AddedUtc`/`Id` جدا نیست. برای
«ترتیب پایدار» فاز از `ConsumedUtc` سپس `ItemId` استفاده کن.
`StayItemKind = {BoardingService=0, ClinicService=1, Product=2, Custom=3}`.

### D5 — items و roomHistory به‌صورت JSON column اند نه relational rows
`BoardingStayConfiguration.cs` از `JsonFields.Map(...)` استفاده می‌کند: `_items`→ستون `Items`،
`_roomHistory`→ستون `RoomHistory` (TEXT/JSON). «room-assignment rows» فاز از قبل JSON است؛ جدول جدید لازم
نیست. برای transfer از `BoardingStay.TransferRoom(newRoomId, reason, clock)` استفاده کن که خودش history
را append می‌کند.

### D6 — FrozenTotalMinor وجود ندارد
باید در دامنه اضافه شود: `public long? FrozenTotalMinor { get; private set; }` + متد
`Result FreezeTotalForCheckout(Money total)`. این snapshot صرفاً برای audit است؛ calculator باید همان
مقدار را در CheckOutUtc بازتولید کند (A-RULE 018 / R-B23). روی config نگاشت شود
(`.Property(e => e.FrozenTotalMinor)`).

### D7 — permission موجود است، تازه نساز
متن فاز `boarding.stay.manage`. موجود در `PermissionCodes.cs`: `BoardingRead="boarding.read"`,
`BoardingManage="boarding.manage"`, `BoardingCheckout="boarding.checkout"`. از `BoardingManage` برای
create/item/transfer و `BoardingCheckout` برای checkout استفاده کن. permission جدید نساز (مثل C04).

### D8 — boarding.stay.checkout از قبل در CommandTypes هست
`CommandTypes.BoardingStayCheckout = "boarding.stay.checkout"` از قبل تعریف شده. فقط 4 عدد جدید اضافه کن:
`boarding.stay.create`, `boarding.item.add`, `boarding.item.remove`, `boarding.stay.transfer`. checkout را
دوباره تعریف نکن. در PROGRESS بنویس «CommandTypes += 4 (checkout موجود بود)».

### D9 — IBoardingRepository فعلی حداقلی است
`IBoardingRepository` فقط `FindAsync/RequireAsync/Add/AddRange` برای BoardingStay دارد. نیازها:
- پورت query جدید `IBoardingQueries` (الگوی `IAppointmentQueries`) شامل
  `CountActiveStaysInRoomAsync(roomId, ct)` برای capacity در تراکنش + resolve room helper.
- handoff writer جدا مثل C03: پورت `IBoardingBillingHandoffWriter` (Application/Ports) + پیاده‌سازی
  `BoardingBillingHandoffWriter` (Persistence/Repositories) که همان DbContext اسکوپ‌شدهٔ command را share
  می‌کند. الگوی دقیق: `IVisitBillingHandoffWriter` / `VisitBillingHandoffWriter`.

### D10 — BoardingServiceDef در LocalSeeder سید نمی‌شود
`LocalSeeder.cs` فقط rooms را سید می‌کند (`RoomsKey="boarding.rooms"`, 8 اتاق R-01..R-08 cap 1؛ R-07
Isolation, R-08 Vip = 3 نوع). بلوک جدید `boarding.services` با 6 `BoardingServiceDef.Create(...)` اضافه کن
(الگوی `AddAll(...)` + marker key جدید). seed اتاق‌ها را دست نزن تا migration/snapshot نشکند.

### D11 — enum های دامنه ≠ enum های Contracts
Contracts: `BoardingStatus{Active,Done}`, `BoardingPaymentMode{Prepaid,Postpaid}`,
`BoardingSpecies{Dog,Cat,Bird,Rabbit,Rodent,Other}`, `BoardingRoomType{Standard,Vip}`,
`BoardingItemKind{Service,Item}`, `BoardingItemSource{BoardingService,ClinicService,ClinicItem}`.
دامنه: `StayState{Reserved,Active,ReadyForInvoice,Invoiced,CheckedOut,Cancelled}`,
`RoomType{Standard,Vip,Isolation,Cage,Suite}`, `StayItemKind{BoardingService,ClinicService,Product,Custom}`.
در `BoardingMapping` نگاشت با نام (نه cast عددی). status مشتق: `Active→Active`، بقیه→`Done`.
`Isolation/Cage/Suite` را به نزدیک‌ترین (`Standard`/`Vip`) نگاشت کن و در کامنت توضیح بده.

---

## بخش ۲ — اطلاعات خالص مرجع (لازم نیست دوباره باز کنی)

### مسیرها و ساختار موجود
- Domain Boarding: `src/Animora.Domain/Boarding/` = `BoardingStay.cs`, `Room.cs`, `BoardingServiceDef.cs`,
  `StayItem.cs`, `StayRoomAssignment.cs`.
- Events: `src/Animora.Domain/Events/BoardingEvents.cs` = `StayActivatedEvent`, `StayReadyForInvoiceEvent`,
  `StayRoomTransferredEvent`, `StayCheckedOutEvent`. رویداد `BoardingStayCheckedOutEvent` که فاز می‌خواهد
  = همین `StayCheckedOutEvent` موجود (دامنه در `CompleteCheckout` raise می‌کند).
- ErrorCodes: `src/Animora.Domain/Results/ErrorCodes.cs`. الگو:
  `public static readonly ErrorCode X = new("CODE", ErrorCode.Conflict|Validation|NotFound);`
  موجود مرتبط: `RoomUnavailable`, `RoomCapacityInvalid`, `StayInvalidTransition`, `StayLocked`,
  `StayInvoiceRequired`, `StayDatesInvalid`.
- CommandTypes: `src/Animora.Contracts.Wire/Commands/CommandTypes.cs` — const string + افزودن به لیست `All`.
- Contracts Boarding (فقط بخوان، تغییر نده): `src/Animora.Contracts/Boarding/` = `IBoardingQueryService.cs`,
  `BoardingStay.cs`, `BoardingDetail.cs`, `BoardingHistoryModels.cs`.
- Persistence: `src/Animora.Persistence.Sqlite/` — `Entities/`, `Configurations/Boarding/`, `Queries/`,
  `Seed/`, `Migrations/`, `Repositories/`, `Context/LocalStoreServiceCollectionExtensions.cs`.
- Client.Services: `src/Animora.Client.Services/` — پوشهٔ جدید `Boarding/`.
- Desktop: `src/Animora.Desktop/App.axaml.cs` (DI خط ~281), `Pages/Boarding/BoardingPageViewModel.cs`,
  `Mocks/MockBoardingQueryService.cs` (حذف شود).

### امضای دقیق دامنه BoardingStay (متدهای موجود که باید استفاده شوند)
- `static Result<BoardingStay> Reserve(tenantId, petId, ownerId, roomId, checkInUtc, plannedCheckOutUtc, dailyRate, careNote?, dietNote?)`
- `Result Activate(IClock clock)` — Reserved→Active، اولین room-history، `StayActivatedEvent`.
- `Result AddItem(StayItem item)` — تا Invoiced قفل نشده؛ قفل→`StayLocked`.
- `Result RemoveItem(string itemId)` — حذف با ItemId؛ نبود→`NotFound`.
- `Result TransferRoom(string newRoomId, string reason, IClock clock)` — فقط Active؛ history را می‌بندد/باز
  می‌کند، `RoomId` را ست می‌کند، `StayRoomTransferredEvent`.
- `Result RequestCheckout(IClock clock)` — Active→ReadyForInvoice، `StayReadyForInvoiceEvent(...,BillableDays)`.
- `Result AttachInvoice(string invoiceId)` — ReadyForInvoice→Invoiced.
- `Result CompleteCheckout(IClock clock)` — نیاز به InvoiceId (`StayInvoiceRequired`)، Invoiced→CheckedOut،
  `ActualCheckOutUtc` را می‌زند، history می‌بندد، `StayCheckedOutEvent`.
- `int BillableDays(DateTimeOffset endUtc)` = `Max(1, ceil((endUtc-CheckInUtc).TotalHours/24))`. این «روز»
  است نه «شب کلینیک‌محلی»؛ پس `StayChargeCalculator.NightsBetween` مستقل از این است.

### ClinicCalendar (موجود، `src/Animora.Application/Time/ClinicCalendar.cs`)
static: `DayRangeUtc(DateOnly,tz)`, `ToUtcTicks(DateOnly,minutes,tz)`, `LocalDay(long utcTicks,tz)`,
`MinutesFromDayStart(...)`. `NightsBetween` ندارد → در `StayChargeCalculator` بساز:
`nights = LocalDay(endTicks).DayNumber - LocalDay(startTicks).DayNumber; return Max(1, nights);`
(same-day→0→clamp 1؛ 3 شب→3). چون calculator pure static domain است و نباید به Application وابسته شود،
`tz` را `TimeZoneInfo` پارامتری بگیر و `LocalDay` را inline کن
(`TimeZoneInfo.ConvertTimeFromUtc` + `DateOnly.FromDateTime`). Domain به Application وابسته نمی‌شود (§1).

### الگوی VisitBillingHandoff (C03) — مرجع BoardingBillingHandoff
`src/Animora.Persistence.Sqlite/Entities/VisitBillingHandoff.cs`: `[LocalOnly] sealed class`,
`HandoffStatus{Ready,Invoiced,Cancelled}` (از همین enum موجود استفاده کن)، فیلدها:
`Id, TenantId, VisitId, OwnerId, PetId, DoctorId, CompletedUtc, LinesJson, Status, InvoiceId`،
`const LinesJsonMaxLength=65536`، factory `Create(...)`, متدهای `MarkInvoiced`/`Cancel`. Config: HasKey(Id)؛
LinesJson HasMaxLength+Required؛ index `(TenantId,Status,CompletedUtc)` + unique index روی `VisitId`.
→ `BoardingBillingHandoff` = همان set ستون‌ها با `StayId`(unique) به‌جای `VisitId` (DoctorId را می‌توان
empty گذاشت)؛ `[LocalOnly]`؛ برای C07 همان column set را نگه دار.

### الگوی Command (C04 SchedulingCommands.cs)
یک فایل؛ هر command: `[RequiresPermission(...)]` + `[Auditable(AuditCategory.Clinical, CommandTypes.X)]`
روی record؛ record: `ICommand<TResult>, IJournaledCommand` با `CommandTypeName`,
`AggregateTypeName="BoardingStay"`, `AggregateIdOrNull`. سپس `...CommandValidator : IValidator<TCommand>`
(اختیاری) و `...CommandHandler(deps) : ICommandHandler<TCommand,TResult>` با
`HandleAsync(c, CommandContext context, ct)`. `context.TenantId/UserId` در دسترس. خطا:
`Result<T>.Fail(ErrorCodes.X, detail)`. `AuditCategory` فقط `Clinical` موجود است (Boarding نیست، مثل C04).

### الگوی Query projection (AppointmentQueries.cs)
`sealed class XQueries(AnimoraLocalDbContext db) : IXQueries`؛ `db.Set<T>().AsNoTracking().Where(...)`
تک‌statement؛ tenant filter خودکار (named filter)؛ paging با `QueryPaging.ToPagedAsync` (نیاز OrderBy، size
کلamپ [1,200]). occupancy: `LEFT JOIN`+`COUNT` یا correlated sub-select، هرگز per-room. فقط
`State==Active` را occupied بشمار.

### الگوی Client Service (AppointmentsQueryService.cs)
`sealed class XService(IServiceScopeFactory scopes) : IX`؛ هر call: `scopes.CreateAsyncScope()` →
`ICurrentUserProvider.BuildQueryContext()` (query) یا context دستور → `IDispatcher.QueryAsync/SendAsync` →
map. بدون EF/SQL/rule. Mapping: `Boarding/BoardingMapping.cs` static extension، نگاشت با نام enum، ترتیب
پایدار آیتم‌ها (`ConsumedUtc` سپس `ItemId`).

### قانون RemoveItem با stale-index (تصمیم فاز؛ C07/C08 reuse)
Contracts: `RemoveItemAsync(stayId, int index)` حذف بر اساس موقعیت ترتیبی در لیست detail.
`RemoveStayItemCommand(StayId, int Index, string ExpectedItemId)` — سرویس id را از آخرین detail خوانده‌شده
resolve و هر دو را پاس می‌دهد؛ mismatch بین index→id و ExpectedItemId → `STAY_ITEM_INDEX_STALE` و هیچ چیز
حذف نمی‌شود. تصمیم ساده: در `BoardingQueryService.RemoveItemAsync(stayId, index)` اول `GetDetailAsync(stayId)`،
آیتم index-ام (`ItemId`) را بگیر و ExpectedItemId پاس بده؛ command داخل تراکنش دوباره detail را با همان ترتیب
می‌سازد و چک می‌کند.

### readiness gate (VaccinationPageViewModel، خط ~197)
VM فیلد `IStoreReadinessGate _readiness` می‌گیرد و اولین خط `LoadAsync`: `await _readiness.WaitAsync();`.
`IStoreReadinessGate` از C01 موجود و در DI ثبت است. constructor `BoardingPageViewModel` را با param
`IStoreReadinessGate` گسترش بده و در ابتدای `LoadAsync` (خط ~165) صدا بزن.

### DI (App.axaml.cs خط ~281)
فعلی: `services.AddSingleton<IBoardingQueryService, MockBoardingQueryService>();` → `BoardingQueryService`
(Singleton). `.AddBoardingUseCases()` را در زنجیرهٔ Application اضافه کن. در
`LocalStoreServiceCollectionExtensions.cs` (Persistence):
`AddScoped<IBoardingQueries, BoardingQueries>()` + `AddScoped<IBoardingBillingHandoffWriter, BoardingBillingHandoffWriter>()`.

### tests
- `tests/Animora.Domain.Tests/StayChargeCalculatorTests.cs`: same-day→1 شب؛ per-day 3 شب→×3؛ one-time
  (`PerStay`) صرف‌نظر از شب یک‌بار؛ frozen total = بازتولید در CheckOutUtc.
- `tests/Animora.Application.Tests/BoardingUseCaseTests.cs`: transfer به اتاق پر→`ROOM_AT_CAPACITY`؛ remove
  با index کهنه→`STAY_ITEM_INDEX_STALE` و حذف‌نشدن؛ checkout یک handoff می‌سازد و اتاق آزاد می‌شود؛ checkout
  دوم→`STAY_ALREADY_CHECKED_OUT`.
- `tests/Animora.Persistence.Tests/BoardingQueryTests.cs`: rooms query تک `SELECT` (با `ToQueryString()` مثل
  C01) + occupancy درست؛ history به `BoardingHistorySort` و سقف 200 احترام.
- `FixedClock` در تست‌ها top-level class است، nested نیست (درس C01 defect b). از `LocalStoreTestFixture` و
  doubles الگوی `ReceptionTestDoubles.cs` استفاده کن.

### error codes جدید (6 عدد، در ErrorCodes.cs)
`STAY_NOT_FOUND`(NotFound), `STAY_NOT_EDITABLE`(Conflict), `STAY_ITEM_INDEX_STALE`(Conflict),
`ROOM_AT_CAPACITY`(Conflict), `ROOM_NONE_AVAILABLE`(Conflict), `STAY_ALREADY_CHECKED_OUT`(Conflict).
چند کد نزدیک از قبل هست (`StayInvalidTransition`, `StayLocked`, `RoomUnavailable`) — این 6 جدید و
غیرتکراری‌اند.

### product kinds (برای BoardingSelectableItemsQuery)
`ProductKind{Medicine=0,...,Consumable=4,...,Service}`. sellable = Consumable/Medicine طبق فاز؛ union با
`BoardingServiceDef` و tag با `BoardingItemSource` (BoardingService/ClinicService/ClinicItem).

---

## بخش ۳ — TODO LIST (ترتیب اجرا؛ هر تسک یک پوش)

> بعد از `[x]` شدن هر تسک، فوراً `scripts/git/push.sh "..."`. build/test طولانی نگیر مگر تسک صریحاً بگوید.

- [x] **T1 — StayChargeCalculator + FrozenTotalMinor + 6 error code**
  - بساز `src/Animora.Domain/Boarding/StayChargeCalculator.cs` (pure static):
    `static Money Accrued(BoardingStay stay, IReadOnlyList<BoardingServiceDef> services, DateTimeOffset asOf, TimeZoneInfo tz)`.
    Σ روی `stay.Items`: سرویس متناظر (`CatalogItemId`↔`BoardingServiceDef.Id`)؛ billing=`PerDay`→
    `UnitPrice × NightsBetween(CheckIn, min(asOf, ActualCheckOutUtc ?? asOf))`؛ `PerStay`/`PerUse`→
    `UnitPrice × Quantity`؛ آیتم بدون سرویس (Product/Custom)→`UnitPrice × Quantity`. `NightsBetween` را داخل
    همین فایل بنویس (LocalDay inline، Max(1,diff)). API `Money` را با grep کوتاه فقط همان لحظه چک کن اگر ضرب
    نداشت.
  - در `BoardingStay.cs`: `public long? FrozenTotalMinor { get; private set; }` + متد
    `Result FreezeTotalForCheckout(Money total)` (مقدار مینور را در FrozenTotalMinor می‌ریزد).
  - در `ErrorCodes.cs`: 6 کد جدید (بخش ۲).
  - پوش: `feat(boarding): add StayChargeCalculator, FrozenTotalMinor and 6 error codes`

- [x] **T2 — StayChargeCalculatorTests**
  - `tests/Animora.Domain.Tests/StayChargeCalculatorTests.cs` (4 سناریو بخش ۲). `TimeZoneInfo.Utc` برای
    سادگی مرزها. فقط کامپایل کافی است؛ full test نگیر.
  - پوش: `test(boarding): StayChargeCalculator night/billing/frozen cases`

- [x] **T3 — BoardingBillingHandoff entity + configuration + migration 012**
  - `Entities/BoardingBillingHandoff.cs` `[LocalOnly]` (الگوی C03، StayId unique، از `HandoffStatus` موجود).
  - `Configurations/Boarding/BoardingBillingHandoffConfiguration.cs` — HasKey، LinesJson maxlen+required،
    index `(TenantId,Status,CompletedUtc)` + unique `StayId`.
  - `BoardingStayConfiguration.cs` (modify): `.Property(e => e.FrozenTotalMinor)`.
  - migration `012_BoardingHandoff` با `dotnet ef` (ابزار در `.sandbox/tools`، مثل C03/C04)؛ 001–011
    byte-identical؛ snapshot آپدیت. اگر SDK نبود دستی طبق 010/011 + snapshot دستی، در PROGRESS "ef not run".
  - پوش: `feat(boarding): BoardingBillingHandoff entity, config, migration 012`

- [x] **T4 — پورت‌ها + read models + BoardingQueries + handoff writer**
  - `Application/Ports/IBoardingQueries.cs`: متدهای query (stays list, detail, rooms occupancy, services,
    selectable items, known pets, history) + `CountActiveStaysInRoomAsync` + resolve room helper.
  - read models در `Application/UseCases/Boarding/ReadModels/` (5): `StayListItemReadModel`,
    `StayDetailReadModel`, `RoomOccupancyReadModel`, `BoardingSelectableReadModel`, `BoardingHistoryRowReadModel`.
  - `Application/Ports/IBoardingBillingHandoffWriter.cs` (الگوی IVisitBillingHandoffWriter).
  - `Persistence.Sqlite/Queries/BoardingQueries.cs`: occupancy تک‌statement (فقط State==Active occupied)؛
    stay list با pet/owner name + room label + آیتم‌ها (accrued در handler با calculator، نه SQL)؛ history
    paged ≤200 با sort.
  - `Persistence.Sqlite/Repositories/BoardingBillingHandoffWriter.cs`.
  - پوش: `feat(boarding): query ports, read models, BoardingQueries, handoff writer`

- [x] **T5 — query ها + registration**
  - `Application/UseCases/Boarding/`: `BoardingStayListQuery`, `BoardingStayDetailQuery(StayId)`,
    `BoardingRoomsQuery`, `BoardingServicesQuery`, `BoardingSelectableItemsQuery`,
    `BoardingKnownPetsQuery(Search?)` (reuse C01 `PersianText`/SearchKey), `BoardingHistoryQuery(filter)`.
    هر کدام query record + handler.
  - `BoardingRegistration.cs` با `AddBoardingUseCases()` (الگوی SchedulingRegistration).
  - پوش: `feat(boarding): boarding queries and use-case registration`

- [x] **T6 — 5 command + validators + CommandTypes+4**
  - `CreateBoardingStayCommand`, `AddStayItemCommand`, `RemoveStayItemCommand(StayId, Index, ExpectedItemId)`,
    `TransferStayRoomCommand(StayId, TargetRoomId?)`, `CheckoutStayCommand`. permission:
    create/item/transfer=`BoardingManage`؛ checkout=`BoardingCheckout`. `[Auditable(AuditCategory.Clinical, CommandTypes.X)]`
    + `IJournaledCommand`، `AggregateTypeName="BoardingStay"`.
  - `CommandTypes.cs`: +4 const جدید (D8) + افزودن به `All`.
  - Create: `Reserve(...)` سپس `Activate(clock)` سپس `repo.Add`.
  - Add: `stay.AddItem(new StayItem(...))`؛ stay نبود→`STAY_NOT_FOUND`؛ قفل→`STAY_NOT_EDITABLE`.
  - Remove: detail با ترتیب پایدار، آیتم index-ام؛ `ItemId != ExpectedItemId`→`STAY_ITEM_INDEX_STALE` (حذف
    نکن)؛ سپس `stay.RemoveItem(itemId)`.
  - Transfer: capacity داخل تراکنش با `CountActiveStaysInRoomAsync`؛ پر→`ROOM_AT_CAPACITY`؛ target null→
    "next free room of same RoomType"، نبود→`ROOM_NONE_AVAILABLE`؛ سپس `stay.TransferRoom(target,reason,clock)`.
  - Checkout (تصمیم منتخب، هم‌راستا C03/C07): چون `CompleteCheckout` دامنه به InvoiceId نیاز دارد و invoice
    کار C07 است، C05 در `RequestCheckout(clock)`→`ReadyForInvoice` متوقف می‌شود؛ accrued را محاسبه و
    `FreezeTotalForCheckout` کن؛ یک `BoardingBillingHandoff`(Ready) با `IBoardingBillingHandoffWriter` بنویس؛
    رویداد `StayReadyForInvoiceEvent` کافی است. «آزادی اتاق» = occupancy فقط `State==Active` occupied است پس
    ReadyForInvoice آزاد است. checkout دوم روی stay غیرِ Active→`STAY_ALREADY_CHECKED_OUT`. این انحراف را در
    PROGRESS ثبت کن (checkout واقعی با invoice به C07 موکول).
  - پوش: `feat(boarding): 5 journaled commands, stale-index rule, checkout handoff, CommandTypes+4`

- [x] **T7 — Seed: services reference data + BoardingSampleData**
  - `LocalSeeder.cs`: بلوک جدید `boarding.services` (marker key جدید) با 6 `BoardingServiceDef.Create(...)`.
    seed اتاق‌ها را دست نزن (D10).
  - `Seed/BoardingSampleData.cs`: پشت `LocalStoreOptions.SeedSampleData`، marker `boarding.sample`، guard روی
    pets موجود (مثل ClinicalSampleData). 5 stay فعال با آیتم + 9 stay کامل‌شده. `FixedClock`، بدون IClock param
    بلااستفاده (درس C01 defect c).
  - پوش: `feat(boarding): seed 6 service defs (reference) + BoardingSampleData`

- [x] **T8 — Client.Services: BoardingQueryService + BoardingMapping**
  - `Client.Services/Boarding/BoardingQueryService.cs` — `sealed`, `IServiceScopeFactory`, scope-per-call،
    همهٔ 13 عضو `IBoardingQueryService`. `RemoveItemAsync` طبق stale-index (اول GetDetail، ItemId،
    ExpectedItemId). `TransferAsync(stayId)`→command با `TargetRoomId=null`.
  - `Client.Services/Boarding/BoardingMapping.cs` — static extension، نگاشت enum با نام (D11)، ترتیب پایدار
    آیتم‌ها، accrued/ExtraItemsCost از read model.
  - پوش: `feat(boarding): real BoardingQueryService + BoardingMapping`

- [ ] **T9 — DI swap + ViewModel line + delete mock + register ports**
  - `App.axaml.cs`: `MockBoardingQueryService`→`BoardingQueryService` (Singleton)؛ `.AddBoardingUseCases()`.
  - `LocalStoreServiceCollectionExtensions.cs`: `AddScoped<IBoardingQueries, BoardingQueries>()` +
    `AddScoped<IBoardingBillingHandoffWriter, BoardingBillingHandoffWriter>()`.
  - `BoardingPageViewModel.cs`: param `IStoreReadinessGate` + `await _readiness.WaitAsync();` اول `LoadAsync`.
  - حذف `src/Animora.Desktop/Mocks/MockBoardingQueryService.cs` (§10، همان commit).
  - پوش: `feat(boarding): swap DI to real service, readiness gate, delete mock, register ports`

- [ ] **T10 — BoardingUseCaseTests + BoardingQueryTests**
  - `tests/Animora.Application.Tests/BoardingUseCaseTests.cs` (4 سناریو).
  - `tests/Animora.Persistence.Tests/BoardingQueryTests.cs` (rooms تک‌SELECT با `ToQueryString()`، history
    sort/paging). `LocalStoreTestFixture` + `FixedClock` (top-level).
  - پوش: `test(boarding): use-case (capacity/stale/checkout/double) + query (occupancy/paging)`

- [ ] **T11 — build سبک + رفع خطاهای کامپایل**
  - `scripts/sandbox/build.sh full` یک‌بار (هدف 0/0). خطاهای کامپایل (using گمشده، نام property Money، cast
    enum) را رفع کن. full test/run طولانی نگیر مگر لازم. اگر build نبود، مثل C04 در PROGRESS "build not run".
  - پوش: `fix(boarding): resolve C05 build errors, 0/0`

- [ ] **T12 — تأیید دستی سبک (اختیاری)**
  - اگر build سبز شد، `run.sh` اختیاری (SMOKE RUN OK). اگر منابع اجازه نداد رد کن و در PROGRESS ذکر کن.
  - پوش (اگر تغییری بود): `chore(boarding): smoke verification`

- [ ] **T13 — PROGRESS.md update**
  - entry جدید `### C05 — ...` append کن (قدیمی را بازنویسی/کوتاه نکن). محتوا: StayChargeCalculator +
    derived-accrual؛ BoardingBillingHandoff + migration 012 («C07 هر دو جدول handoff را می‌خواند»)؛
    stale-index (C07/C08 reuse)؛ rooms/services reference data؛ query ها + 5 command؛
    BoardingQueryService/BoardingMapping؛ DI swap؛ mock deleted: MockBoardingQueryService.cs؛ CommandTypes
    += 4 (checkout موجود بود)؛ ErrorCodes += 6؛ 3 کلاس تست؛ همهٔ Deviations D1..D11 + انحراف
    checkout→ReadyForInvoice. جدول Phase status: C05=✅ Done + تاریخ. `Next phase`→`C06`. Current-state table
    به‌روز (Boarding روی backend واقعی).
  - پوش: `docs(progress): mark C05 done, advance to C06, record deviations`

- [ ] **T14 — (تسک پایانی) حذف این فایل پلن**
  - فقط `Plan/PHASE-C05-boarding-plan.md` را حذف کن. به فایل‌های دیگر و roadmap دست نزن.
  - پوش: `chore(plan): remove completed C05 plan file`

---

## بخش ۴ — چک‌لیست Acceptance (تأیید نهایی)
- build full = 0/0. `MockBoardingQueryService.cs` وجود ندارد و هیچ خط DI به آن اشاره نمی‌کند.
- StayChargeCalculatorTests سبز (same-day→1، per-day×3، one-time×1، frozen=recompute در CheckOutUtc).
- BoardingUseCaseTests سبز (ROOM_AT_CAPACITY، STAY_ITEM_INDEX_STALE بدون حذف، checkout یک handoff + اتاق آزاد،
  checkout دوم→STAY_ALREADY_CHECKED_OUT).
- BoardingQueryTests سبز (rooms تک SELECT + occupancy درست؛ history sort + سقف 200).
- offline کار می‌کند و بعد restart باقی می‌ماند؛ run.sh = SMOKE RUN OK (در حد امکان سبک).

## بخش ۵ — یادآوری قوانین
- Domain هرگز به Application/EF/SQL وابسته نمی‌شود (StayChargeCalculator کاملاً pure).
- append-only: handoff و room-history هرگز mutate نمی‌شوند (A-RULE 002).
- بدون متن فارسی در Application/Client.Services؛ خطا = ErrorCode.
- هر تسک = یک پوش با scripts/git/push.sh. build/test طولانی ممنوع مگر T11 صریح.
