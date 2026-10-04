# Plan — Unified History System (2026-10-04)

> **Self-contained execution plan.** A fresh model must implement every task below **without
> re-auditing the repo**. All file paths, exact current shapes, the target architecture, and
> acceptance criteria are embedded here. Read §0–§2 once, then execute §3 tasks top-to-bottom.
>
> **LOCKED RULE — push after every task:** after completing **each** task below, run
> `scripts/git/push.sh "type(scope): message"`. Not pushing = project death. If it exits code `3`
> (no credentials) call the `setup_github_environment` tool once, then re-run. Exit `2` = rebase
> conflict (repo left clean) — resolve then re-run. If a *heavy* fix / large code block is written
> mid-task, push it immediately with a clear message (don't wait for task end).
>
> **The very last task (T13) deletes THIS file only.** Do not touch other docs or roadmap files.

---

## 0. Context (read once)

- **App:** Animora — vet clinic desktop app, **.NET 10, Avalonia + MVVM (CommunityToolkit.Mvvm)**,
  Persian/RTL, Jalali calendar, local encrypted SQLite. No network server.
- **Data-flow rule (never break):** View → ViewModel → `Animora.Contracts` interface (constructor DI)
  → real `Animora.Client.Services` impl → `Animora.Application` dispatch → Domain + SQLite.
  ViewModels never touch EF/SQL. Persistence never references `Animora.Contracts` for entities
  (enum tags stored as strings). Seed data lives in Persistence seeders, never in `Pages/**`.
- **Build/verify (this sandbox):** `scripts/sandbox/build.sh` (Desktop, must be **0 warnings/0 errors**);
  `scripts/sandbox/build.sh full` (whole solution). **No full test run, no long smoke** per user rule —
  rely on the embedded analysis + a targeted `build.sh` after code-heavy tasks.
- **Goal of this plan:** one **unified, standard, optimized, complete** history system. After T12 the
  history system must score 1000/1000 — zero visual or data inconsistency across the main History page
  and the three in-page History tabs (Vaccination, Boarding, Sales/Cashier).

---

## 1. Current architecture — exact facts (verified; do NOT re-derive)

### 1.1 The unified ledger backbone (already exists — this is the standard to converge ON)

The **main History page** reads a single materialized ledger table. This is the correct, scalable
backbone; the fix is to make the three tabs visually + behaviorally consistent with it and to add
record navigation everywhere.

| Layer | File | Role |
|---|---|---|
| Entity | `src/Animora.Persistence.Sqlite/Entities/ActivityEvent.cs` | `[LocalOnly]` materialized feed row: `Id, TenantId, OccurredUtc, LocalDay(int yyyymmdd), Kind(string), Title(ascii key), DetailArgsJson, DetailText(Key=Value lines), SearchText, EntityType, EntityId, AmountMinor, AmountSign(+1/-1/0), Status(string), ActorUserId, ActorName, CorrelationId`. |
| Writer port | `src/Animora.Application/Ports/IActivityFeedWriter.cs` | `WriteAsync(ActivityEventDraft, ct)` — stages a row in the caller's UoW. |
| Writer impl | `src/Animora.Persistence.Sqlite/Activity/ActivityFeedWriter.cs` | writes the row. |
| Event→feed map | `src/Animora.Application/Activity/ActivityFeedMapper.cs` | `Map(DomainEvent)→ActivityEventDraft?` + `Deserialize` for rebuild. Maps **14** event types (see 1.2). |
| Live handler | `src/Animora.Application/Activity/ActivityFeedHandler.cs` | `IDomainEventHandler<DomainEvent>` — calls mapper + writer in the same UoW as the outbox. |
| Keys | `src/Animora.Application/Activity/ActivityEventKeys.cs` | the 14 ascii title keys (`history.invoice.posted`, …). |
| Query (Application) | `src/Animora.Application/UseCases/History/ActivityFeedQuery.cs` | `ActivityRow`, `ActivityPage(Items,TotalCount,IncomeTotal,ExpenseTotal,DayNets)`, `ActivityFeedQuery`/`ActivityExportQuery`, `IActivityFeedQueries` port, DI `AddHistoryUseCases()`. |
| Query (SQLite) | `src/Animora.Persistence.Sqlite/Queries/ActivityFeedQueries.cs` | `IActivityFeedQueries` impl: tenant-scoped, indexed, server-side filter+sort+page, day-net group, export capped 50_001. |
| Contracts | `src/Animora.Contracts/History/HistoryModels.cs` | `HistoryEventKind`(10: Sale,Appointment,Patient,Vaccine,Expense,Inventory,Cheque,Boarding,Visit,Purchase), `HistoryEventStatus`(15), `HistoryEvent(Id,Kind,Date,Time,Title,Detail,Entity,Amount,AmountSign,Status,Actor)`, `HistorySort`(4), `HistoryFilter(FromDate,ToDate,Kinds,Search,Sort,Page,PageSize)`, `HistoryPage(Items,TotalCount,IncomeTotal,ExpenseTotal,DayNets?)`. |
| Service iface | `src/Animora.Contracts/History/IHistoryQueryService.cs` | `GetPageAsync(filter)`, `ExportCsvAsync(filter,labels)`. |
| Service impl | `src/Animora.Client.Services/History/HistoryQueryService.cs` | scoped dispatch; `HistoryMapping.Map(ActivityRow,zone)` → `HistoryEvent`. **`Entity` is built as `EntityType + ":" + EntityId`** (e.g. `"Invoice:01J…"`). |
| Page VM | `src/Animora.Desktop/Pages/History/HistoryPageViewModel.cs` | period chips, 10 kind chips, summary bar, day-grouped feed, CSV export, debounce+generation-token+ReplaceAll. |
| Row/group VMs | `src/Animora.Desktop/Pages/History/HistoryItemViewModels.cs` | `HistoryRangeViewModel`, `HistoryCategoryChipViewModel`, **`HistoryEventRowViewModel`** (title/detail/time/entity/actor/icon/badge/amount/status), `HistoryDayGroupViewModel`. |
| Presentation | `src/Animora.Desktop/Pages/History/HistoryPresentation.cs` | `AllKinds`, `KindLabel/KindIcon/KindColor/KindBg`, `StatusLabel/StatusColor/StatusBg`, `Fill/Money/Fa/FaText/DayLabel`. |
| View | `src/Animora.Desktop/Pages/History/HistoryPageView.axaml` (+`.axaml.cs`) | toolbar + feed. ~140 visual nodes. |
| L10n | `src/Animora.Desktop/Localization/HistoryText.cs` | `Title(key)`, `Detail(resolved)`, `Label(k)` — Persian for the 14 keys + Key labels. |

**The 14 mapped domain events → feed** (`ActivityFeedMapper.Map`): `PetRegisteredEvent`(Patient/New),
`VisitCompletedEvent`(Visit/Done, actor=DoctorUserId), `VaccinationAdministeredEvent`(Vaccine/Done),
`AppointmentBookedEvent`(Appointment/Confirmed), `AppointmentCancelledEvent`(Appointment/Cancelled),
`StayActivatedEvent`(Boarding/CheckIn), `StayCheckedOutEvent`(Boarding/Discharge),
`GoodsReceiptPostedEvent`(Purchase/Done), `StockMovementRecordedEvent`(Inventory/Normal),
`InvoicePostedEvent`(Sale/Unpaid, amount, sign +1, EntityType="Invoice"),
`PaymentRegisteredEvent`(Sale/Paid|Done, EntityType="Payment"),
`InvoiceRefundedEvent`(Sale/Done, sign -1, EntityType="Invoice"),
`ChequeStateChangedEvent`(Cheque), `ExpensePostedEvent`(Expense/Done, sign -1).
**EntityType values present in the feed:** `Pet, Visit, Vaccination, Appointment, Stay, GoodsReceipt,
StockMovement, Invoice, Payment, Cheque, Expense`.

### 1.2 The three in-page History tabs — EACH has its OWN parallel stack (the inconsistency)

Each tab duplicates its own models/service-read/presentation/view instead of reusing the main one.
They are **NOT** backed by `ActivityEvent`; each reads bespoke source tables:

| Tab | Hosted by | Data seam | Contracts | VM / Row VM | Presentation | View (XAML) |
|---|---|---|---|---|---|---|
| **Sales/Cashier** «تاریخچه صندوق» | `SalePageViewModel` (`History` prop, `new SaleHistoryViewModel(...)`, `EnsureLoaded()` on history tab) | `ISalesQueryService.GetHistoryAsync` → `SalesQueries.GetHistoryAsync` (invoice/payment tables) | `src/Animora.Contracts/Sales/CashierHistoryModels.cs` (`CashierHistoryKind`(7:Accepted,Paid,Settled,Cancelled,Refunded,Edited,Deleted), `CashierHistoryEntry`(has **InvoiceId**), `CashierHistoryFilter`, `CashierHistoryPage(Inflow/Outflow)`) | `Pages/Sales/SaleHistoryViewModel.cs` (484 ln), `SaleHistoryItemViewModels.cs` | `Pages/Sales/SaleHistoryPresentation.cs` | `Pages/Sales/SaleHistoryView.axaml` (~124 nodes) |
| **Boarding** | `BoardingPageViewModel` (`History` prop, `new BoardingHistoryViewModel(service)`, `EnsureLoaded()`) | `IBoardingQueryService.GetHistoryAsync` → `BoardingQueries.GetHistoryAsync` (Stay tables) | `src/Animora.Contracts/Boarding/BoardingHistoryModels.cs` (`BoardingHistoryKind`(5:CheckIn,ItemAdded,ItemRemoved,Transferred,CheckOut), `BoardingHistoryEntry`(has **StayId**, Species), `BoardingHistoryFilter`(Species), `BoardingHistoryPage`) | `Pages/Boarding/BoardingHistoryViewModel.cs` (468 ln), `BoardingHistoryItemViewModels.cs` | `Pages/Boarding/BoardingHistoryPresentation.cs` | `Pages/Boarding/BoardingHistoryTabView.axaml` (~121 nodes) |
| **Vaccination** | `VaccinationPageViewModel` (`History` prop, `new VaccinationHistoryViewModel(service)`, `EnsureLoaded()`) | `IVaccinationService.GetHistoryAsync` → `VaccinationQueries.GetHistoryAsync` (`MedicalHistoryEntry` kind=Vaccination) | `src/Animora.Contracts/Vaccination/VaccinationHistoryModels.cs` (`VaccinationHistoryEntry`(has **RecordId**, Actor), `VaccinationHistoryFilter`, `VaccinationHistoryPage`) | `Pages/Vaccination/VaccinationHistoryViewModel.cs` (426 ln), `VaccinationHistoryItemViewModels.cs` | `Pages/Vaccination/VaccinationHistoryPresentation.cs` | `Pages/Vaccination/VaccinationHistoryView.axaml` (~118 nodes) |

> **Appointments** also has a history view (`Pages/Appointments/AppointmentsHistoryTabView.axaml`,
> bound to `AppointmentsCalendarViewModel`). **OUT OF SCOPE** — the user named only Vaccination,
> Boarding, Sales/Cashier. Do not touch Appointments unless a shared helper you create is trivially
> reusable there; never regress it.

### 1.3 Record navigation — the key capability gap

- **Sales tab already has the target pattern:** `SaleHistoryViewModel.OpenInvoice(invoiceId)` runs
  `_dialogs.ShowSheet(new InvoiceViewerViewModel(_service, _toast, _dialogs, _print, invoiceId, OnInvoiceChanged), 1040, 720)`.
  `InvoiceViewerViewModel` (ctor: `ISalesQueryService service, IToastService toast, IDialogService dialogs, IPrintService print, string invoiceId, Action? onChanged=null`) **is the exact same invoice sheet opened from the Cashier/Sale page and the Owner sheet** (used by `Pages/Patients/OwnerSheetViewModel.cs`). This is the "یکپارچه" invoice screen the user wants.
- **Main History page has NO record navigation at all.** `HistoryEventRowViewModel` does not even keep
  `Id/EntityType/EntityId` — rows are inert. `Entity` arrives as `"Type:Id"` from `HistoryMapping.Map`.
- **Boarding & Vaccination tabs have NO open/navigate command** — only filter commands.
- **Record-sheet seam:** `src/Animora.Desktop/Services/IRecordSheetPresenter.cs` →
  `OpenOwnerSheetAsync(ownerId)`, `OpenPetSheetAsync(petId)` (DI singleton, `App.axaml.cs:304`).
- **Navigation seam:** `src/Animora.Desktop/Services/INavigationService.cs` →
  `NavigateTo<TVm>()`, `NavigateTo<TVm>(Action<TVm> configure)`, `NavigateToAsync<TVm>(Func<TVm,Task>)`,
  `NavigateTo(Type, navigationKey?, configure?)`.
- **Destination page VMs** (`Shell/NavigationMenu.cs`): `DashboardPageViewModel`,
  `AppointmentsCalendarViewModel`, `PatientsPageViewModel`, `VisitsTriagePageViewModel`,
  `VaccinationPageViewModel`, `BoardingPageViewModel`, `SalePageViewModel`, `InventoryPageViewModel`,
  `FinancePageViewModel` (expense/reports via `InitialTab`), `ChequesPageViewModel`,
  `HistoryPageViewModel`, `ProfilePageViewModel`.

### 1.4 Cross-cutting facts

- **Visual/behavior duplication is the root of inconsistency:** 4 separate presentation helpers +
  4 separate row/toolbar XAML trees, each ~120 nodes. They share the same *intent* (period chips,
  kind chips, summary bar, day-grouped feed, paging, search, sort, CSV) but drift in spacing, colors,
  status/kind palettes and row layout.
- **Change notification:** pages call `_changes.Notify(DataDomain.History, …)`; the main History page
  reloads on nav. `DataDomain` enum exists and already includes `History`.
- **Performance rules (PERFORMANCE.md):** §5 single `ReplaceAll`, §6 server-side filter/sort/page,
  §12 lazy tab load, §13 debounce keystrokes, §4 never block UI thread. All existing VMs already
  follow these — keep the pattern in anything new.

---

## 2. Target design (the standard to build) — decide once, then just execute §3

**Principle:** ONE canonical visual + behavior for every history surface; tabs keep their
*specialized data source and specialized tools*, but render through the *same* shared presentation and
row template, and every history surface gains **click-to-open the unified record screen**.

### 2.1 Record opening — single shared seam `IHistoryRecordOpener`

Create one Desktop service that, given `(entityType, entityId, secondaryId?)`, opens the **same**
unified screen used elsewhere in the app. Mapping (locked):

| EntityType (feed) / tab row | Opens | How |
|---|---|---|
| `Invoice`, `Payment` | Invoice sheet | `_dialogs.ShowSheet(new InvoiceViewerViewModel(sales, toast, dialogs, print, invoiceId, onChanged), 1040, 720)`. For `Payment` rows the feed's `EntityId` is the payment id — the invoice id must come from the row (see T3: carry `InvoiceId`). |
| `Pet` | Pet file sheet | `IRecordSheetPresenter.OpenPetSheetAsync(petId)` |
| `Owner` | Owner sheet | `IRecordSheetPresenter.OpenOwnerSheetAsync(ownerId)` |
| `Stay` | Boarding page | `INavigationService.NavigateTo<BoardingPageViewModel>()` (deep-link to stay optional; at minimum land on the page) |
| `Vaccination` | Vaccination page | `NavigateTo<VaccinationPageViewModel>()` |
| `Visit` | Visits page | `NavigateTo<VisitsTriagePageViewModel>()` |
| `Appointment` | Appointments | `NavigateTo<AppointmentsCalendarViewModel>()` |
| `Cheque` | Cheques page | `NavigateTo<ChequesPageViewModel>()` |
| `Expense` | Finance (expense tab) | `NavigateTo<FinancePageViewModel>()` (InitialTab expense) |
| `GoodsReceipt` | Inventory/Purchasing | `NavigateTo<InventoryPageViewModel>()` |
| `StockMovement`, `Product` | Inventory | `NavigateTo<InventoryPageViewModel>()` |
| unknown/empty | no-op (row stays inert, no crash) | guard |

Seam shape: `interface IHistoryRecordOpener { Task OpenAsync(string entityType, string entityId, string? invoiceId = null); }`.
Impl resolves `ISalesQueryService, IToastService, IDialogService, IPrintService, IRecordSheetPresenter,
INavigationService` from DI; registered singleton in `App.axaml.cs`. **This is the one place that knows
the invoice-sheet ctor**, so the main page and all tabs reuse it (no duplication).

### 2.2 Shared presentation — `HistoryPresentation` becomes the single palette authority

`Pages/History/HistoryPresentation.cs` is already the richest helper. Make the three tab presentations
**delegate** to it for colors/money/digits/day-labels instead of re-defining them, OR replace per-tab
status/kind color maps with calls into it. Target: identical status-chip and kind-badge palette, identical
`Money`/`Fa`/`FaText`/`DayLabel` output everywhere. Do not fork a 5th copy.

### 2.3 Shared row visual — one row template reused by all four surfaces

Extract the main page's event-row visual (icon badge + title/detail + time + entity + actor + amount +
status chip + **hover/hand cursor + click**) into a reusable template/control so every tab's feed row
looks pixel-identical to the main page. Options (pick the lowest-risk at implementation time):
(a) a shared `DataTemplate` in a `ResourceDictionary` (e.g. `Resources/Controls/HistoryRow.axaml`) keyed
to a shared row interface; or (b) a small templated `HistoryRowControl`. The tabs' row VMs must expose the
same surface the template binds to (title, detail, time, entity, actorName/initials/role, iconKey, badge
brushes, amountText/brush, statusLabel/brush/bg, and an `OpenCommand`).

### 2.4 Tabs keep specialized tools

- **Sales:** keep edit/void/delete via `InvoiceViewerViewModel` (already there) + kind filters
  (Accepted/Paid/Settled/Cancelled/Refunded/Edited/Deleted). This is the "خیلی دقیق فاکتورها را
  مشاهده/بررسی/ادیت" requirement — already satisfied; just align visuals + ensure open works.
- **Boarding:** keep Species filter + stay kinds; add row open → Boarding page / stay.
- **Vaccination:** keep vaccine-title filters; add row open → Vaccination page / pet.

### 2.5 Acceptance (what 1000/1000 means)
1. Main History rows are **clickable** and open the correct unified screen per 2.1 (invoice sheet,
   pet/owner sheet, or the right page) — verified by reading the code paths, not by guesswork.
2. The three tabs are **visually identical** to the main page (same row template, same palette, same
   toolbar chrome) and each row opens its record (Sales=invoice sheet as today; Boarding/Vaccination
   gain open).
3. No duplicated/forked palette logic remains across the 4 presentations (single authority).
4. `scripts/sandbox/build.sh` → **0/0** after every code-heavy task. No regressions in Appointments.

---

## 3. Task list — execute top-to-bottom. Status: ☐ todo · ☑ done. Push after EACH.

> Build only after code-heavy tasks (noted per task). Each task line gives the exact files + edit.
> Push command shown per task.

### ☐ T1 — Create the shared record-opener seam `IHistoryRecordOpener`
- **New file** `src/Animora.Desktop/Services/IHistoryRecordOpener.cs`:
  `interface IHistoryRecordOpener { Task OpenAsync(string entityType, string entityId, string? invoiceId = null); }`
  with XML-doc explaining it opens the one unified screen per the §2.1 table.
- Rationale: single place that knows the `InvoiceViewerViewModel` ctor + the §2.1 destination map, reused
  by the main page and all tabs (kills duplication, guarantees "same screen as صندوق").
- No build needed (interface only, but may build with T2).
- **Push:** `scripts/git/push.sh "feat(history): add IHistoryRecordOpener seam"`

### ☐ T2 — Implement `HistoryRecordOpener` + register in DI
- **New file** `src/Animora.Desktop/Services/HistoryRecordOpener.cs` implementing §2.1 exactly.
  - Ctor deps (all DI singletons/available): `ISalesQueryService sales, IToastService toast,
    IDialogService dialogs, IPrintService print, IRecordSheetPresenter sheets, INavigationService nav`.
  - `switch (entityType)` (ordinal, case-sensitive — matches feed values in §1.1):
    - `"Invoice"` → `dialogs.ShowSheet(new InvoiceViewerViewModel(sales, toast, dialogs, print, entityId, null), 1040, 720);`
    - `"Payment"` → same but use `invoiceId ?? entityId` as the invoice id.
    - `"Pet"` → `await sheets.OpenPetSheetAsync(entityId);`
    - `"Owner"` → `await sheets.OpenOwnerSheetAsync(entityId);`
    - `"Stay"` → `nav.NavigateTo<BoardingPageViewModel>();`
    - `"Vaccination"` → `nav.NavigateTo<VaccinationPageViewModel>();`
    - `"Visit"` → `nav.NavigateTo<VisitsTriagePageViewModel>();`
    - `"Appointment"` → `nav.NavigateTo<AppointmentsCalendarViewModel>();`
    - `"Cheque"` → `nav.NavigateTo<ChequesPageViewModel>();`
    - `"Expense"` → `nav.NavigateTo<FinancePageViewModel>();`
    - `"GoodsReceipt"`, `"StockMovement"`, `"Product"` → `nav.NavigateTo<InventoryPageViewModel>();`
    - default / null / empty → return (`Task.CompletedTask`), no crash.
  - Guard empty `entityId` for sheet cases (return early).
- **Register** in `src/Animora.Desktop/App.axaml.cs` (near line 304, next to `IRecordSheetPresenter`):
  `services.AddSingleton<IHistoryRecordOpener, HistoryRecordOpener>();`
- **Build:** `scripts/sandbox/build.sh` → 0/0.
- **Push:** `scripts/git/push.sh "feat(history): implement HistoryRecordOpener + DI"`

### ☐ T3 — Make main History rows carry identity + an OpenCommand
- **`src/Animora.Contracts/History/HistoryModels.cs`:** the row already has `Entity` as `"Type:Id"`.
  Keep the wire shape but the Desktop must split it. (Do NOT change the record signature unless needed;
  splitting on the client is enough. If cleaner, add `string EntityType`, `string EntityId` params — but
  then update `HistoryMapping.Map` in `HistoryQueryService.cs` which currently packs `EntityType + ":" + EntityId`.)
- **`src/Animora.Desktop/Pages/History/HistoryItemViewModels.cs` → `HistoryEventRowViewModel`:**
  - Parse `ev.Entity` into `EntityType`/`EntityId` (split on first `':'`; if no colon, treat whole as
    EntityId with empty type). Store both.
  - Add `public IAsyncRelayCommand OpenCommand { get; }` (or an `Action`-backed command) that calls back
    into the page VM's opener. Simplest: give the row VM an injected `Func<string,string,Task>` open
    delegate from the page VM, exposed as `OpenCommand`.
  - Add `bool CanOpen` = non-empty EntityType mapped in §2.1 (optional; else command is always enabled and
    opener no-ops on unknown).
- **`src/Animora.Desktop/Pages/History/HistoryPageViewModel.cs`:**
  - Inject `IHistoryRecordOpener _opener` (add ctor param; update DI reg — `HistoryPageViewModel` is DI-built).
  - In `RenderFeed`, pass the open delegate `(type,id) => _opener.OpenAsync(type, id)` into each
    `HistoryEventRowViewModel`.
- **`src/Animora.Desktop/App.axaml.cs`:** ensure `HistoryPageViewModel` registration resolves the new
  `IHistoryRecordOpener` (DI handles it automatically if added to ctor — verify no manual `new`).
- **Build:** `scripts/sandbox/build.sh` → 0/0.
- **Push:** `scripts/git/push.sh "feat(history): main rows carry entity id + open command"`

### ☐ T4 — Wire the click on the main History page view
- **`src/Animora.Desktop/Pages/History/HistoryPageView.axaml`:** wrap each event row in a `Button`
  (or add `Tapped`/`PointerPressed` → command) styled flat (no default chrome), `Cursor="Hand"`, bound to
  `OpenCommand`. Match the existing row look (reuse existing inner layout; only the outer wrapper becomes
  clickable). Keep hover affordance consistent with chip hover styles already in the file.
- Keep RTL + existing spacing. No new literals that need l10n beyond a tooltip (optional «مشاهده جزئیات»).
- **Build:** `scripts/sandbox/build.sh` → 0/0.
- **Push:** `scripts/git/push.sh "feat(history): main feed rows are clickable → open record"`

### ☐ T5 — Make `HistoryPresentation` the single palette authority
- Audit `Pages/Sales/SaleHistoryPresentation.cs`, `Pages/Boarding/BoardingHistoryPresentation.cs`,
  `Pages/Vaccination/VaccinationHistoryPresentation.cs` for duplicated color/money/digit/day-label logic.
- Refactor each to **delegate** to `HistoryPresentation` for: `Fill`, `Money`, `Fa`, `FaText`, `DayLabel`,
  and the **status-chip + kind-badge palette** (map each tab's status/kind enum → the main
  `HistoryEventStatus`/`HistoryEventKind` palette, or expose shared `Color(hex)` helpers). Keep tab-specific
  labels (Persian) local, but share the *tones/hex* so chips are visually identical.
- If `HistoryPresentation` members are `internal`/private, widen minimally or add a small shared static
  (e.g. `HistoryPalette`) — prefer reusing the existing one.
- **Build:** `scripts/sandbox/build.sh` → 0/0.
- **Push:** `scripts/git/push.sh "refactor(history): single palette authority across tabs"`

### ☐ T6 — Extract one shared history-row visual template
- Create `src/Animora.Desktop/Resources/Controls/HistoryRow.axaml` (ResourceDictionary) with a
  `DataTemplate` (or a small `HistoryRowControl`) rendering: kind icon badge, title+detail, time, entity,
  actor (name/initials/role), signed amount, status chip, **clickable wrapper (Hand cursor)**.
  Bind to a shared interface, e.g. `IHistoryRowView` (new, in `Pages/History/`), with members:
  `Title, Detail, HasDetail, Time, Entity, ActorName, ActorInitials, ActorRole, IconKey, BadgeBrush,
  BadgeIconBrush, CategoryLabel, CategoryColorBrush, CategoryBgBrush, HasAmount, AmountText, AmountBrush,
  StatusLabel, StatusBrush, StatusBgBrush, ICommand OpenCommand`.
- Register the dictionary in `App.axaml` (merged dictionaries) so all views can reference the template.
- Make `HistoryEventRowViewModel` implement `IHistoryRowView` (it already has every member except
  `OpenCommand` from T3).
- **Build:** `scripts/sandbox/build.sh` → 0/0.
- **Push:** `scripts/git/push.sh "feat(history): shared clickable history-row template"`

### ☐ T7 — Sales tab: reuse the shared row template + confirm invoice open via the seam
- **`Pages/Sales/SaleHistoryItemViewModels.cs` → `SaleHistoryRowViewModel`:** implement `IHistoryRowView`
  (add any missing members: actor initials/role, badge brushes via `HistoryPresentation`); expose
  `OpenCommand` that routes through `IHistoryRecordOpener.OpenAsync("Invoice", InvoiceId)` **OR** keep the
  existing `OpenInvoice` (which already opens the same `InvoiceViewerViewModel`) — but route it through the
  seam so there's one code path. Pass the opener delegate from `SaleHistoryViewModel`.
- **`Pages/Sales/SaleHistoryViewModel.cs`:** accept an opener (it already receives `service/toast/dialogs/
  print`; either inject `IHistoryRecordOpener` into `SalePageViewModel` and pass it in, or keep local
  `OpenInvoice` — but ensure the row click opens the **same** invoice sheet). Prefer the seam for
  consistency.
- **`Pages/Sales/SaleHistoryView.axaml`:** replace the bespoke row markup with the shared
  `HistoryRow` template; keep the Sales-specific toolbar (kind chips incl. Edited/Deleted, search, sort,
  CSV if present). Visual result must match the main page row-for-row.
- **Build:** `scripts/sandbox/build.sh` → 0/0.
- **Push:** `scripts/git/push.sh "feat(history): sales tab uses shared row + seam open"`

### ☐ T8 — Boarding tab: shared row template + row open
- **`Pages/Boarding/BoardingHistoryItemViewModels.cs` → row VM:** implement `IHistoryRowView`
  (badge/actor/amount/status via `HistoryPresentation`); add `OpenCommand` routing through
  `IHistoryRecordOpener.OpenAsync("Stay", StayId)`.
- **`Pages/Boarding/BoardingHistoryViewModel.cs`:** receive the opener (inject `IHistoryRecordOpener` into
  `BoardingPageViewModel` and pass to `new BoardingHistoryViewModel(service, opener)`), wire the delegate.
- **`Pages/Boarding/BoardingHistoryTabView.axaml`:** swap bespoke row markup for the shared template;
  keep Boarding-specific Species filter + stay kinds in the toolbar.
- **Build:** `scripts/sandbox/build.sh` → 0/0.
- **Push:** `scripts/git/push.sh "feat(history): boarding tab uses shared row + open"`

### ☐ T9 — Vaccination tab: shared row template + row open
- **`Pages/Vaccination/VaccinationHistoryItemViewModels.cs` → row VM:** implement `IHistoryRowView`;
  add `OpenCommand` routing through `IHistoryRecordOpener.OpenAsync("Vaccination", RecordId)` (or
  `"Pet"` + petId if the pet file is the more useful unified target — pick Pet file if RecordId has no
  dedicated screen; confirm from the row's available ids).
- **`Pages/Vaccination/VaccinationHistoryViewModel.cs`:** receive the opener (inject into
  `VaccinationPageViewModel`, pass to `new VaccinationHistoryViewModel(service, opener)`).
- **`Pages/Vaccination/VaccinationHistoryView.axaml`:** swap to shared row template; keep vaccine-title
  filters.
- **Build:** `scripts/sandbox/build.sh` → 0/0.
- **Push:** `scripts/git/push.sh "feat(history): vaccination tab uses shared row + open"`

### ☐ T10 — Align the three tab toolbars/chrome with the main page
- Make the period chips, kind chips, summary bar, pager, search box and sort/page-size selects on all
  three tabs visually match `HistoryPageView.axaml` (same classes/styles, spacing, RTL). Extract common
  chip/toolbar styles into `Resources/Controls/HistoryRow.axaml` (or a sibling `History.axaml`) if that
  reduces drift; otherwise copy the main styles verbatim into each tab. Preserve each tab's extra tools
  (Species, Edited/Deleted kinds, etc.).
- **Build:** `scripts/sandbox/build.sh` → 0/0.
- **Push:** `scripts/git/push.sh "style(history): unify tab toolbars with main page"`

### ☐ T11 — Consistency + regression sweep (targeted, no full test run)
- Re-read each touched view/VM to confirm: (a) rows clickable everywhere, (b) palettes identical,
  (c) no leftover forked color/money helpers, (d) Appointments history untouched/unbroken,
  (e) DI resolves (no manual `new HistoryPageViewModel`), (f) `EnsureLoaded`/`Refresh`/`OnInvoiceChanged`
  flows still fire after edits via the invoice sheet.
- Run `scripts/sandbox/build.sh full` once → 0/0 (whole solution, since Contracts/Application may be touched).
- If any heavy fix is applied here, push it immediately with a clear message before finishing.
- **Push:** `scripts/git/push.sh "test(history): consistency + regression sweep"`

### ☐ T12 — Final visual/data coherence verification
- Confirm against §2.5 acceptance list, item by item (read the code paths; no long smoke).
- Fix any residual mismatch found; push each fix.
- **Push:** `scripts/git/push.sh "chore(history): finalize unified history system"`

### ☐ T13 — Delete this plan file (FINAL task, nothing else)
- `rm Plan/history-system-unification.md`
- **Push:** `scripts/git/push.sh "chore(plan): remove completed history-unification plan"`

---

## 4. Guardrails (apply to every task)
- Never break the data-flow rule (§0). ViewModels stay off EF/SQL.
- Keep PERFORMANCE.md §5/§6/§12/§13/§4 patterns in anything new.
- Case-sensitive, ordinal `entityType` matching in the opener.
- No new 5th palette copy; converge on `HistoryPresentation`.
- Persian/RTL preserved; new user-facing strings localized (reuse `HistoryText` where possible).
- If a task's edit turns out larger than expected, still push it (locked rule) with a clear message.

