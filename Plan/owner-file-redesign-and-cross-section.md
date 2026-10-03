# Plan — Owner File redesign + cross-section coordination (Patients ↔ Vaccination ↔ Appointments ↔ Invoices)

> **Self-contained execution plan.** A model opening this in a fresh session must be able to
> implement every task below **without re-auditing the repo**. All file paths, the exact current
> code shape, the root-cause/data-seam analysis, the chosen design and the concrete fix are
> embedded here. Read top-to-bottom once, then execute in order.
>
> **🔒 LOCKED PUSH RULE — after completing EACH task below, run `scripts/git/push.sh "<msg>"`.**
> Not pushing = project failure. If the script exits `3` (no credentials) call the
> `setup_github_environment` tool once, then re-run. If it exits `2` (rebase conflict) resolve
> preferring remote, then re-run. One task = one (or more, if heavy) commit+push.
>
> **The very LAST task is to delete THIS file only** (leave every other file, incl. the other
> `Plan/*.md`, untouched).

---

## 0. Context (read once — do not re-derive)

- **App:** Animora — self-hosted veterinary clinic management + accounting desktop app.
  .NET 10, **Avalonia + MVVM (CommunityToolkit.Mvvm)**, Persian / RTL, Jalali calendar,
  local encrypted SQLite. Offline-first, no network server wired in.
- **The one architectural rule (never break):**
  `View → ViewModel → Animora.Contracts interface (ctor DI) → real Animora.Client.Services impl → Animora.Application dispatch (IDispatcher QueryAsync/SendAsync) → Animora.Persistence.Sqlite`.
  - ViewModels depend **only** on Contracts interfaces, never on concrete services / EF / SQL / DI scope.
  - To add a screen's data need: extend the interface + DTO in `Contracts/<Area>/`, implement it in
    the matching `Animora.Client.Services/<Area>/` service over the dispatcher (new Query/Command +
    handler in `Animora.Application`, projection in `Animora.Persistence.Sqlite/Queries`), register
    both in `src/Animora.Desktop/App.axaml.cs`. Seed data lives in Persistence seeders, not UI.
  - RTL is set once app-wide in `App.axaml`; never re-set per screen.
- **Performance standard:** `PERFORMANCE.md` numbered `§` rules. Relevant here: §4 (never block UI
  thread; all data via `await`), §5 (batch collection updates / clear+add), §6 (never cross the seam
  with an unbounded result set — filter/sort/page server-side), §13 (avoid redundant reloads).
- **Build / verify (fast, from repo root):**
  - Desktop-only compile: `scripts/sandbox/build.sh` → must be **0 warnings / 0 errors**.
  - Whole solution: `scripts/sandbox/build.sh full`.
  - Smoke-run (only if you touched startup/DI/shell): `scripts/sandbox/run.sh` → prints **SMOKE RUN OK**.
  - **Do NOT run a full/long test suite** (explicit user constraint). Build + targeted smoke only.
- **Data-change fan-out:** `IDataChangeNotifier _changes` + `DataDomain` enum. After a write, call
  `_changes.Notify(DataDomain.X, ...)` so other open screens refresh. Already used in
  `OwnerSheetViewModel.SaveAsync` → `Notify(Patients, Dashboard, Appointments, Visits)`.
- **Localization helpers** (`src/Animora.Desktop/Localization/`): `PersianNumberFormatter.ToPersianDigits`,
  `JalaliDate.FromDateTime(dt).ToString("short")`, `RelativeDateFormatter.Age/Since`.

### Migration facts (confirmed)
- Latest migration = **`023`** (`..._023_PatientsPerfIndexes`). **Next IDs: `024`, `025`, …**
- Migration pattern: file `src/Animora.Persistence.Sqlite/Migrations/<timestamp>_NNN_Name.cs` +
  `.Designer.cs` + update `AnimoraLocalDbContextModelSnapshot.cs`. Tenant/soft-delete filters auto-applied.
- **You may not need a migration at all** — see §3 analysis: the owner/pet IDs required for
  cross-linking already exist on the domain entities and in SQLite; the gap is at the **Contracts +
  Client.Services projection** layer, not the schema. Only add a migration if you add an index for a
  new owner-scoped query and profiling warrants it (optional, low priority).

---

## 1. The two user tasks (verbatim intent, de-ambiguated)

**TASK A — Owner File (پرونده مالک) full redesign.**
1. Enlarge the owner-file page to the **same scale as the Pet File** (currently much smaller).
2. **Remove** the «تماس» (Call) and «پیامک» (Sms) action buttons.
3. Every value shown must come from **real app data** (what the software actually stores across its
   sections) and be presented with clean, practical, **minimal & modern** UX/UI.
4. **Copy-on-click:** clicking a value copies it to the clipboard (with a toast confirmation).
5. **Show SMS messages sent to this owner** — coordinated with the Vaccination section (reminders).
6. **Show the invoices recorded for this person.**

**TASK B — Cross-section coordination (Patients/Profile ↔ Vaccination ↔ Appointments ↔ Invoices).**
- Admin/user must be able to **see, from the owner (and pet) file, every invoice** registered for
  that owner, and every piece of activity registered across sections (sent reminder SMS, recorded
  visit, recorded vaccine, …).
- **Vaccination ↔ Appointments** must stay coordinated: when a vaccine with a **recurrence / next
  due date** is recorded for a patient, it must appear correctly in the **Appointments** section
  with proper styling. Model **real-world scenarios** and wire everything consistently.

---

## 2. Current state — exact code shape (confirmed, do not re-read unless editing)

### 2.1 Owner File = "Owner Sheet" (NOT a modal — it is routed as a page)
- VM: `src/Animora.Desktop/Pages/Patients/OwnerSheetViewModel.cs` (308 lines).
  - Ctor deps: `IPatientsQueryService _patients`, `IRecordSheetPresenter _presenter`,
    `IDialogService _dialogs`, `IToastService _toast`, `IDataChangeNotifier _changes`.
  - `LoadAsync(ownerId)` → `_patients.GetOwnerAsync(id)` → `Apply(OwnerDetail)`.
  - Exposes identity/contact/address view strings + edit buffers (inline edit mode),
    `Pets` (`ObservableCollection<OwnerSheetPetRowViewModel>`), and a 3-stat activity block
    (`VisitsCountText` hard-coded `"۰"`, `PetsCountText`, `LastVisitText`).
  - **Commands to DELETE in Task A:** `Call()` (line ~261) and `Sms()` (line ~269). `Remind()`
    (line ~272) is a toast-only stub — **remove it too** unless you wire it to the real reminder
    pipeline; it is currently fake and violates "real data only".
  - `OpenPet(row)` → `_presenter.OpenPetSheetAsync(row.Pet.Id)` (keep).
- View: `src/Animora.Desktop/Pages/Patients/OwnerSheetView.axaml` (425 lines).
  - Root `Grid RowDefinitions="Auto,*,Auto"` → sheet-head (44×44 avatar) / ScrollViewer body
    (`StackPanel MaxWidth="680"`) / sheet-foot.
  - **The `od-cta` grid (lines ~117-145)** holds the 3 buttons تماس / پیامک / یادآوری → **this whole
    `<Grid … IsVisible="{Binding IsViewing}">` block is removed in Task A.**
  - Sections: identity, contact, address+referral, pets list, "خلاصه فعالیت" (3 stats).
- Code-behind: `OwnerSheetView.axaml.cs` (trivial `InitializeComponent`).
- Routing: `src/Animora.Desktop/Services/RecordSheetPresenter.cs` `OpenOwnerSheetAsync(ownerId)`
  resolves `OwnerSheetViewModel` from DI, calls `LoadAsync`, navigates. Registered in
  `src/Animora.Desktop/Shell/ViewFactory.cs:100` (VM→View map) and in `App.axaml.cs` DI.

### 2.2 Pet File = the size/UX target to match
- View: `src/Animora.Desktop/Pages/Patients/PetFileView.axaml` (84 KB).
  - Root is a full-page `ScrollViewer` (line 112) → `StackPanel Margin="22,20,22,24" MaxWidth="1120"`
    (line 113). Avatar **84×84** (line 127). Tabbed: summary / info / visits / vaccinations /
    documents (IsVisible-switched `StackPanel`s, e.g. `IsVisitsTab` at line 758).
  - **This is the scale to match for the owner file: `MaxWidth` ≈ **1120** (vs the sheet's 680),
    larger avatar (~72-84), same page margins, section cards grid.** Do NOT copy its code; use it as
    the visual/behaviour spec only.

### 2.3 Patients data seam (owner/pet)
- Interface: `src/Animora.Contracts/Patients/IPatientsQueryService.cs`.
  - Owner read: `Task<OwnerDetail?> GetOwnerAsync(string id)`; write: `SaveOwnerAsync(OwnerDetail)`.
  - **No** invoice / SMS / activity methods today.
- Models: `src/Animora.Contracts/Patients/PatientDetailModels.cs`.
  - `OwnerDetail(Id, File, FirstName, LastName, Gender, Mobile1, Mobile2?, Phone?, NationalId,
    Address?, Referral?, Since, Tone, Tags, Pets:IReadOnlyList<PetSummary>)` — **no invoices, no SMS,
    no activity.**
  - `PetDetail(... Visits, Vaccinations, Labs, Documents, Surgeries ...)` — pet already aggregates
    visit/vaccine refs; see `VisitSummaryRef`, `VaccinationSummaryRef` (strings, incl. cost).
- Impl: `src/Animora.Client.Services/Patients/` (PatientsService + mapping over dispatcher queries).
- Domain: `src/Animora.Domain/Patients/{Owner,Pet,WeightEntry}.cs`. Owner has `Id`; Pet has `Id`,
  `OwnerId`.

### 2.4 Invoices seam (Sales)
- Interface: `src/Animora.Contracts/Sales/ISalesQueryService.cs`.
  - `Task<IReadOnlyList<InvoiceSummary>> GetInvoicesAsync()` — **returns ALL invoices, no owner
    filter.** `GetInvoiceAsync(id)`. Owner picker via `GetOwnersAsync(query)` / `PickOwnerAsync`.
- Models: `src/Animora.Contracts/Sales/SalesModels.cs`.
  - `InvoiceSummary(Id, Kind, State, OwnerName, PetName, Phone, Total, Due, AvatarTone, PetInitial)`
    — **owner is a display string, NO OwnerId.**
  - `InvoiceDetail(... OwnerName, PetName, Phone ...)` — also **no OwnerId**.
- **Domain truth (the key enabler):** `src/Animora.Domain/Sales/Invoice.cs` **DOES** store
  `public string? OwnerId` (line 105) and `public string? PetId` (line 108), set on create
  (line 233/235) and on `PickOwner` (line 455). **So invoices ARE linked to an owner id in SQLite —
  it is just not projected to the Contracts layer.**
- Impl: `src/Animora.Client.Services/Sales/{SalesQueryService,SalesMapping}.cs` (dispatcher queries).

### 2.5 Vaccination seam (records + SMS reminders)
- Interface: `src/Animora.Contracts/Vaccination/IVaccinationService.cs`.
  - `GetRecordsAsync()` (due/overdue list), `GetSmsTemplatesAsync()`, `MarkDoneAsync(id)`,
    `BuildSmsPreviewAsync(recordId, templateId)`, **`SendSmsAsync(recordId, message)`** (sets
    `SmsState = Sent`; network send is stubbed — persistence of the sent state is real),
    `GetHistoryAsync(filter)` (ledger: administered / scheduled / SMS-sent / rescheduled).
- Models: `src/Animora.Contracts/Vaccination/VaccinationModels.cs`.
  - `VaccinationRecord(Id, PetName, Species, OwnerName, OwnerMobile, VaccineId, Dose, LastDate?,
    DueDate, VetName, Lot, Site, Tone, Status, SmsState)` — **owner/pet are strings, NO OwnerId/PetId.**
  - `enum VaccinationStatus`, `enum SmsState`, `SmsTemplate`, `SmsPreview`.
- **Domain truth:** `src/Animora.Domain/Clinical/VaccinationRecord.cs` stores `PetId` (line 44),
  `VaccineName`, `DoseNumber` (53), `TotalDoses?` (56), `AdministeredUtc` (59), **`NextDueUtc?`**
  (68) — i.e. the **recurrence / next-due** that Appointments must reflect. It carries `PetId` (so
  owner is reachable via Pet.OwnerId) but **no OwnerId directly.** `VisitId?` links to a visit.
- Impl: `src/Animora.Client.Services/Vaccination/{VaccinationService,VaccinationMapping,SmsTemplateRenderer}.cs`.
  - `GetRecordsAsync()` → dispatcher `VaccinationDueQuery`. The SMS "sent" state is real in SQLite.
- **SMS storage reality:** the only persisted SMS fact today is the **per-vaccination-record
  `SmsState` (Sent/…)** plus the history ledger entry when `SendSmsAsync` runs. There is **no
  general per-owner SMS outbox table**. See §3 for how Task A §5 ("show SMS sent to owner") is
  satisfied from this.

### 2.6 Appointments seam
- Models: `src/Animora.Contracts/Appointments/AppointmentModels.cs`.
  - `AppointmentEvent(Id, Date, StartMinutes, DurationMinutes, TypeId, DoctorId, PetName, PetKind,
    OwnerName, OwnerPhone, AvatarTone, SpeciesIconKey, Status, Note, Code, Total, Invoice, Payment,
    Visit, Lifecycle, VisitId?, CheckedInUtc?, CancelReason?, CreatedByUserId, RescheduleCount,
    DoctorName?, InvoiceTotalMinor?, InvoicePaidMinor?)` — owner/pet are **strings, no IDs**;
    `TypeId` resolved through `AppointmentCatalog` (`AppointmentType` has id/label/color/icon).
  - **There is NO "vaccination booster" appointment kind / source today.** Appointments are not
    generated from vaccination `NextDueUtc`. This is the gap Task B must close.
- Interfaces: `IAppointmentsQueryService` (calendar reads), `IAppointmentBookingService` (create/
  check-in/cancel), `IAppointmentSettingsService` (types/doctors/`SendReminder`+`ReminderLeadHours`).

### 2.7 Clipboard (copy-on-click) — does NOT exist yet
- `grep` confirms **no** `Clipboard.SetTextAsync` call anywhere in `src/Animora.Desktop`.
- The proven pattern already in the codebase (`src/Animora.Desktop/Services/DialogService.cs:61`)
  is `TopLevel.GetTopLevel(host)?.StorageProvider` — i.e. reach the window via
  `TopLevel.GetTopLevel(control)`. For clipboard use `TopLevel.GetTopLevel(control)?.Clipboard?.SetTextAsync(text)`.
- **Decision:** add a tiny reusable copy helper (see Task A4) rather than scatter TopLevel lookups.

---

## 3. Root-cause / design decisions (the thinking already done — DO NOT re-derive)

**Why Task A §5/§6 and all of Task B cannot be done in the View alone:** the display DTOs that the
owner file would need (owner's invoices, owner's sent SMS, owner's activity) **are not exposed by any
Contracts interface**, even though the underlying IDs (`Invoice.OwnerId`, `VaccinationRecord.PetId`,
`Pet.OwnerId`, `NextDueUtc`) **do exist in the domain + SQLite**. So the work is: **project the
existing IDs across the seam** (Contracts + a new owner-scoped query + Client.Services impl), then
consume them in the redesigned owner file. No schema change is required for the read side.

**D1 — Owner's invoices (Task A6 / B):** Add `GetInvoicesForOwnerAsync(ownerId)` to
`ISalesQueryService`, backed by a new dispatcher query that filters `Invoice.OwnerId == ownerId`
(the column already exists + is indexed by PK/owner FK; add an index only if profiling shows need).
Return `InvoiceSummary` rows (already has Id/Kind/State/Total/Due/PetName) — **no new DTO needed**;
the existing summary is enough for an owner-file invoice list. The owner file calls this with the
loaded owner's `Id`.

**D2 — Owner's sent SMS (Task A5):** The only real SMS fact is the vaccination reminder sent-state +
its history-ledger entry. **Do not invent a fake SMS inbox.** Expose the owner's SMS reminder
history by filtering the **existing Vaccination history ledger** (`GetHistoryAsync`) to SMS-sent
entries for this owner. To filter by owner we need an owner id on the ledger rows → add owner id to
the vaccination history projection (reachable via `Pet.OwnerId`). Surface via a new
`GetSmsHistoryForOwnerAsync(ownerId)` on `IVaccinationService` returning a small
`OwnerSmsReminder(When, PetName, VaccineName, Body?, Channel)` DTO. Keep it strictly to what is
really persisted (date, pet, vaccine, template/channel). If a field isn't stored, don't show it.

**D3 — Owner's cross-section activity (Task B "every activity"):** Compose a unified, read-only
**activity feed** for the owner from the already-available sources, each tagged by kind:
  - Invoices (D1) → "فاکتور" entries (amount, state, date).
  - Vaccination SMS reminders (D2) → "پیامک یادآوری" entries.
  - Recorded vaccinations (from the owner's pets' `PetDetail.Vaccinations`, already available via
    `GetPetAsync`, or better a new owner-scoped vaccination query) → "واکسن" entries.
  - Visits (from pets' `PetDetail.Visits`) → "ویزیت" entries.
  Simplest correct implementation: a new `GetOwnerActivityAsync(ownerId)` on `IPatientsQueryService`
  that the Client.Services PatientsService assembles by querying the sales + vaccination + visits
  projections for the owner's pet ids and merging+sorting by date (server-side, bounded — §6).
  Return `IReadOnlyList<OwnerActivityEntry(DateTime When, OwnerActivityKind Kind, string Title,
  string Subtitle, string? Amount)>`. **This is the cleanest single seam the owner file consumes.**
  (If time-boxed: ship D1+D2 first, then D3 as the merge over them — D3 reuses D1/D2 outputs.)

**D4 — Recurring vaccine → Appointment (Task B):** When a vaccination with a `NextDueUtc` (recurrence)
is recorded, the Appointments section must show it. Two valid approaches — **pick the lighter one that
fits the existing calendar**:
  - **(Preferred, read-time projection, no new table):** extend the Appointments calendar query to
    **also surface vaccination `NextDueUtc` rows as synthetic "booster due" calendar entries** for the
    day they fall on. Add a visually distinct style (a new `AppointmentType`-like "واکسن یادآور"
    category OR a dedicated badge/color on `AppointmentEvent`). This keeps a single source of truth
    (the vaccination record) and avoids double-booking bugs.
  - **(Alternative, write-time):** on `MarkDoneAsync`/administer with `NextDueUtc`, create a real
    scheduled `Appointment` of a "booster" type. Heavier (needs idempotency so re-marking doesn't
    duplicate) — only choose if the calendar cannot render synthetic rows cleanly.
  Whichever is chosen, add the **distinct styling** (icon = vaccine/syringe, accent color) so a
  booster-due stands out from a normal visit, and make clicking it open the pet file / vaccination
  record. Confirm the chosen approach against `IAppointmentsQueryService` before coding.

**D5 — Keep ViewModels thin:** all merging/filtering happens in Client.Services (over the dispatcher),
never in the ViewModel (§6 + architecture rule). The owner file VM just awaits the new methods and
clears+adds into `ObservableCollection`s (§5).

**D6 — "Real data only":** remove every toast-only stub (`Call`, `Sms`, `Remind`). Any value the DB
does not have must render a muted "ثبت نشده" / "—", never fabricated. `VisitsCountText` currently
hard-coded `"۰"` must become the real count from D3 (or the owner's pets' visit totals).

---

## 4. Task list (execute top-to-bottom; push after each; update the Status box)

> Status legend: `[ ]` todo · `[~]` in progress · `[x]` done. Keep this list current as you work.

### Phase 1 — Owner File redesign (Task A, UI-first, no seam changes yet)

- [x] **T1 — Remove fake actions.**
  In `OwnerSheetViewModel.cs` delete the `Call`, `Sms` and `Remind` `[RelayCommand]` methods.
  In `OwnerSheetView.axaml` delete the entire `od-cta` button grid (the
  `<Grid ColumnDefinitions="*,*,*" … IsVisible="{Binding IsViewing}">` block, ~lines 117-145).
  Remove any now-unused `IconSms`/`IconPhone`/`IconBell` references in that block only.
  Build desktop (`scripts/sandbox/build.sh`, 0/0). **Push:** `fix(owner-file): remove fake call/sms/remind actions [T1]`.

- [x] **T2 — Enlarge to Pet-File scale.**
  In `OwnerSheetView.axaml` raise the body `StackPanel MaxWidth="680"` → **≈1120** to match
  `PetFileView` (`MaxWidth="1120"`), enlarge the header avatar (44→~72), bump section header /
  value font sizes a step, and widen the section grid to use the extra width (e.g. identity+contact
  side-by-side already; let address/pets/activity breathe). Keep the head/body/foot `Grid` shell and
  the inline-edit behaviour intact. Minimal & modern: generous spacing, soft card borders
  (`Line2Brush`), rounded (`CornerRadius=14`), muted labels + bold values (existing `odk`/`odv`
  styles). Build 0/0. **Push:** `feat(owner-file): resize to pet-file scale, modern layout [T2]`.

- [x] **T3 — Copy-on-click helper + wiring.**
  Add a reusable copy affordance. Implement with the codebase-proven TopLevel pattern:
  `TopLevel.GetTopLevel(control)?.Clipboard?.SetTextAsync(text)`. Cleanest: a small attached
  behaviour or a code-behind handler in `OwnerSheetView.axaml.cs` that, on click/tap of a value,
  copies its text and calls the VM's toast (`_toast.Show("کپی شد", Tone.Ok)` — expose a
  `CopyAsync(string)` `[RelayCommand]` on the VM that does the toast, and do the actual
  `SetTextAsync` in code-behind using the clicked control, since clipboard needs the visual tree).
  Make copyable: national id, mobiles, phone, address, file number. Add a subtle copy icon or
  hover cursor so it's discoverable. Build 0/0. **Push:** `feat(owner-file): click-to-copy fields with toast [T3]`.

### Phase 2 — Invoices across sections (Task A6 + Task B invoices)

- [x] **T4 — Expose owner-scoped invoices (seam).**
  - `ISalesQueryService`: add `Task<IReadOnlyList<InvoiceSummary>> GetInvoicesForOwnerAsync(string ownerId)`.
  - `Animora.Application`: add a query (e.g. `OwnerInvoicesQuery(ownerId)`) + handler that filters by
    `Invoice.OwnerId == ownerId` (column exists — `src/Animora.Domain/Sales/Invoice.cs:105`).
  - `Animora.Persistence.Sqlite/Queries`: projection filtering on `OwnerId`, newest first, **bounded**
    (§6). Reuse the existing `InvoiceSummary` projection shape.
  - `Animora.Client.Services/Sales/SalesQueryService.cs`: implement the new method over the dispatcher.
  - Register nothing new in DI (interface already registered). Build full? No — desktop build is enough
    unless Application/Persistence need it: run `scripts/sandbox/build.sh full` **once** here since you
    touched non-Desktop projects, expect 0/0.
  **Push:** `feat(sales): GetInvoicesForOwnerAsync owner-scoped invoice query [T4]`.

- [x] **T5 — Owner File invoices section (consume T4).**
  The owner file VM takes `ISalesQueryService` via ctor DI (add the parameter + App.axaml.cs is
  already wiring `OwnerSheetViewModel`'s deps — verify it resolves `ISalesQueryService`, which is
  registered). In `LoadAsync` (after owner loads) call `GetInvoicesForOwnerAsync(owner.Id)` and
  clear+add into a new `ObservableCollection<OwnerInvoiceRowViewModel>` (wrap `InvoiceSummary`:
  formatted total/due via `PersianNumberFormatter`, state badge via existing tone mapping, pet name,
  a chevron). Add an "فاکتورها" card to the view; clicking a row opens the invoice (navigate to the
  Sales invoice viewer if a presenter exists, else a read-only summary — check
  `IRecordSheetPresenter`/Sales navigation before adding). Empty state: "فاکتوری ثبت نشده". Build 0/0.
  **Push:** `feat(owner-file): invoices section wired to owner-scoped query [T5]`.

### Phase 3 — SMS reminders across sections (Task A5)

- [ ] **T6 — Expose owner-scoped SMS reminder history (seam).**
  - `IVaccinationService`: add `Task<IReadOnlyList<OwnerSmsReminder>> GetSmsHistoryForOwnerAsync(string ownerId)`
    and a new DTO `OwnerSmsReminder(DateTime When, string PetName, string VaccineName, string Channel, string? Body)`
    in `VaccinationModels.cs`.
  - Add owner id reachability to the vaccination history projection: the ledger rows come from
    `VaccinationRecord` which has `PetId`; join to `Pet.OwnerId` in the Persistence query to filter by
    owner, and restrict to SMS-sent entries (the `SmsState`/history-ledger "SMS-sent" kind).
  - `Animora.Application` query + handler; `Animora.Persistence.Sqlite/Queries` projection (bounded,
    newest first); `Animora.Client.Services/Vaccination/VaccinationService.cs` impl.
  - `scripts/sandbox/build.sh full` once (non-Desktop touched), 0/0.
  **Push:** `feat(vaccination): GetSmsHistoryForOwnerAsync owner reminder history [T6]`.

- [ ] **T7 — Owner File "پیامک‌های ارسال‌شده" section (consume T6).**
  Inject `IVaccinationService` into the owner file VM (verify DI registration exists). In `LoadAsync`
  call `GetSmsHistoryForOwnerAsync(owner.Id)`, clear+add into
  `ObservableCollection<OwnerSmsRowViewModel>` (Jalali date via `JalaliDate`, pet + vaccine, channel
  chip). Add a card to the view; empty state "پیامکی ارسال نشده". Build 0/0.
  **Push:** `feat(owner-file): sent-SMS reminders section (vaccination-linked) [T7]`.

### Phase 4 — Unified activity + Vaccination↔Appointments (Task B core)

- [ ] **T8 — Owner activity feed (seam, merges T4/T6 + visits/vaccines).**
  Add `Task<IReadOnlyList<OwnerActivityEntry>> GetOwnerActivityAsync(string ownerId)` to
  `IPatientsQueryService` + DTO `OwnerActivityEntry(DateTime When, OwnerActivityKind Kind, string Title,
  string Subtitle, string? Amount)` and `enum OwnerActivityKind { Invoice, Sms, Vaccine, Visit }`.
  Implement in `Animora.Client.Services/Patients/PatientsService.cs` by querying the owner's pet ids
  and merging: invoices (T4 projection), SMS reminders (T6), recorded vaccinations + visits (owner's
  pets). Sort desc by date, bounded (§6 — cap to a sane N, e.g. 50). Keep the merge in Client.Services,
  not the VM. `scripts/sandbox/build.sh full` once, 0/0.
  **Push:** `feat(patients): GetOwnerActivityAsync unified cross-section owner feed [T8]`.

- [ ] **T9 — Owner File "خلاصه فعالیت" → real activity feed (consume T8).**
  Replace the fake 3-stat block (incl. hard-coded `VisitsCountText="۰"`) with: real counts
  (invoices, visits, pets, last activity) **and** a compact recent-activity list from T8 (icon per
  `OwnerActivityKind`, title/subtitle/amount, Jalali date). Minimal & modern. Build 0/0.
  **Push:** `feat(owner-file): real activity feed replacing stub stats [T9]`.

- [ ] **T10 — Recurring vaccine shows in Appointments (seam + styling).**
  Implement D4 (prefer the read-time synthetic "booster due" projection from `VaccinationRecord.NextDueUtc`).
  Steps: (a) confirm `IAppointmentsQueryService` calendar query entry point; (b) surface due boosters
  as calendar entries for their due day with a **distinct style** — add a "واکسن یادآور" category/badge
  (syringe/vaccine icon, distinct accent) on `AppointmentEvent` or via an `AppointmentType`;
  (c) ensure no duplication with real appointments; (d) clicking a booster-due entry opens the pet
  file / vaccination record. Persistence/Application query + Client.Services impl + Desktop styling.
  `scripts/sandbox/build.sh full` once, 0/0. If startup/shell/DI touched, `scripts/sandbox/run.sh` →
  SMOKE RUN OK. **Push:** `feat(appointments): surface recurring-vaccine boosters with distinct styling [T10]`.

- [ ] **T11 — Consistency pass (Task B real-world scenarios).**
  Verify the full loop with real seeded data: record a recurring vaccine → it appears in Appointments
  (T10) → the reminder SMS appears in the owner file (T7) → invoices for the owner appear (T5) →
  the activity feed reflects all of it (T9). Fix any `DataChangeNotifier` gaps so sections refresh
  (e.g. after a vaccine/SMS/invoice write, `Notify(Patients, Appointments, Vaccination, Sales,
  Dashboard)` where appropriate). Build 0/0; smoke if shell touched.
  **Push:** `fix: cross-section data-change fan-out + consistency pass [T11]`.

### Phase 5 — Verify & clean up

- [ ] **T12 — Final build + targeted smoke.**
  `scripts/sandbox/build.sh full` → 0 warnings / 0 errors. `scripts/sandbox/run.sh` → SMOKE RUN OK
  (owner file opens, sections populate, copy works, no binding errors in console). Fix any fallout.
  **Push:** `test: full build + smoke green after owner-file + cross-section work [T12]`.

- [ ] **T13 — DELETE THIS PLAN FILE ONLY.**
  `git rm Plan/owner-file-redesign-and-cross-section.md` (leave every other file, incl.
  `Plan/patients-directory-perf.md`, untouched — do NOT touch any roadmap file).
  **Push:** `chore(plan): remove completed owner-file + cross-section plan [T13]`.

---

## 5. Guardrails (apply to every task)

- **Push after each task** via `scripts/git/push.sh "<conventional message> [Tn]"`. Non-negotiable.
  If a single task produces a heavy/large change mid-way, push that too (don't batch huge diffs).
- **No full/long test runs.** Desktop build after UI-only tasks; `build.sh full` once per task that
  touches Application/Persistence/Client.Services; smoke-run only when startup/DI/shell changes.
- **Never break the data-flow rule.** New data = extend Contracts + Client.Services + Application +
  Persistence; ViewModels stay on interfaces. No seed/sample data in `Pages/**`.
- **Real data only.** No toast-only fakes; missing values render "ثبت نشده"/"—".
- **Perf:** clear+add collections (§5); never cross the seam unbounded (§6); never block UI (§4).
- **Scope discipline:** only open files this plan names unless a change forces a neighbouring edit.
  Do **not** open `Animora-UI/**` (design ref) unless you need the exact visual spec of the owner
  file; do **not** open backend-roadmap phase files (this is not a backend-roadmap phase).
- **DI check:** `OwnerSheetViewModel` gains `ISalesQueryService` + `IVaccinationService` deps — both
  interfaces are already DI-registered; just add ctor params and confirm resolution in `App.axaml.cs`.
