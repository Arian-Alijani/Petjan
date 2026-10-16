# Plan — Visits: Toolbox nav + Radiology + Lab (Settings-driven)

> Self-contained execution plan. All facts needed are inlined below — do **not**
> re-explore the repo for context already captured here. Open a file only when a
> task's "Files" list names it, and only the region cited.
>
> **LOCKED RULE — push after every completed task:** after finishing each task
> below (and after any heavy/large code change mid-task), run
> `scripts/git/push.sh "type(scope): message"` from repo root. Not pushing = project death.
> If push exits code 3 (no creds) call the `setup_github_environment` tool once, then re-run.
> Branch: work on `genspark_ai_developer` (first push: `scripts/git/push.sh -b genspark_ai_developer "..."`).
>
> **Build check:** `scripts/sandbox/build.sh` must be **0 warnings / 0 errors**.
> Smoke-run only if DI/shell/startup touched: `scripts/sandbox/run.sh` → `SMOKE RUN OK`.
> **Do NOT run full solution test / long builds.** Fast desktop build only unless a backend project changed (then `scripts/sandbox/build.sh full`).

---

## 0. Context the executing model MUST internalize (no re-reading needed)

### 0.1 What this is
Animora = offline-first Avalonia + MVVM (.NET 10) vet-clinic desktop app, Persian/RTL, Jalali.
Data flow (the ONE rule): **View → ViewModel → `Animora.Contracts` interface (ctor DI) →
`Animora.Client.Services` impl → `Animora.Application` dispatcher → `Animora.Persistence.Sqlite` (local SQLite).**
ViewModels depend only on Contracts interfaces. To add a data need: extend interface+DTO in
`Contracts/<Area>/`, implement in `Client.Services/<Area>/`, register both in
`src/Animora.Desktop/App.axaml.cs` + the Application use-case registration.

### 0.2 The Visits "روند ویزیت" (encounter) screen — current shape
- Page VM: `src/Animora.Desktop/Pages/Visits/VisitEncounterViewModel.cs` (the wizard).
  - It is a **single-page form** with two steps enum `EncounterStep { Form, Done }`.
  - Collections: `ServiceLines`, `ItemLines` (ObservableCollection<RxLineViewModel>).
  - Fields: Weight/Temp/Pulse/Respiration/VitalsNote, History, Exam, Diagnosis, Plan text.
  - Opens pickers via `_dialogs.ShowModal(new Sales.CatalogModalViewModel(...), 500)`.
  - `PersistAsync()` saves vitals/history/exam/diagnosis/plan through `_service`.
  - `CompleteAsync()` → `_service.CompleteAsync` → notifies `_changes.Notify(...)`.
  - ctor deps already injected: `IVisitEncounterService _service`, `IStoreReadinessGate`,
    `INavigationService`, `IToastService`, `ICapabilityGate`, `IDataChangeNotifier`, `IDialogService _dialogs`.
- View: `src/Animora.Desktop/Pages/Visits/VisitEncounterView.axaml` (~40KB, the big encounter form).
- Contracts seam: `src/Animora.Contracts/Visits/IVisitEncounterService.cs` +
  `VisitsEncounterModels.cs` (RxCatalogItem, RxLine, Vitals, EncounterSummary) +
  `VisitsTriageModels.cs` (EncounterDraft record at line 112, VisitTypeInfo at line 19).
- Client service impl: `src/Animora.Client.Services/Visits/VisitEncounterService.cs`
  (scoped dispatch per call; maps contract ↔ command/query via `VisitEncounterMapping.cs`).

### 0.3 Backend persistence of the encounter (how data is stored & synced — CRITICAL for radiology/lab)
- Aggregate root: `src/Animora.Domain/Clinical/Visit.cs`.
  - Owns `_prescriptions` (`PrescriptionLine`) and `_serviceLines` (`VisitServiceLine`), both
    **persisted as JSON columns** (see `VisitConfiguration.cs`:
    `JsonFields.Map<Visit, VisitServiceLine>(builder, "_serviceLines", "ServiceLines")`).
  - `Vitals` is an owned type; `Diagnosis`/`TreatmentPlan`/`DoctorNotes` are string columns.
  - **State machine**: Draft→InProgress→Completed→ReadyForInvoice→Invoiced (+Cancelled).
    `EnsureOpen()` allows edits only in Draft/InProgress. `EnsureLinesEditable()` blocks at ≥Invoiced.
  - **→ Add radiology & lab the same JSON-list way:** add two private `List<T>` fields to `Visit`
    with mutator methods (`AddRadiology`/`RemoveRadiology`, `AddLabResult`/`RemoveLabResult`) guarded by
    `EnsureOpen()`, expose `IReadOnlyList<T>`, and `JsonFields.Map` them in `VisitConfiguration.cs`.
    **CONFIRMED FACT:** `JsonFields.Map<TEntity,TItem>(builder, field, column)` (see
    `Persistence.Sqlite/Conventions/JsonFields.cs`) maps the list to a **NEW `TEXT` column** named
    `column` on the entity's table via `.HasColumnName(column).HasColumnType("TEXT")` + a JSON
    value-converter. So each new JSON list = **one new column on the `Visits` table → an EF migration
    IS required** (one migration adding `Radiology` + `LabResults` TEXT columns, both defaulting to
    an empty JSON array `[]`). See 0.6 for the migration mechanics.
- Application commands: `src/Animora.Application/UseCases/Visits/EncounterCommands.cs`.
  - Every encounter mutation is a `record ... : ICommand<T>, IJournaledCommand` with
    `[RequiresPermission(PermissionCodes.VisitsWrite)]` + `[Auditable(AuditCategory.Clinical, CommandTypes.X)]`
    and `CommandTypeName/AggregateTypeName="Visit"/AggregateIdOrNull=VisitId`.
  - Pattern per command: record + `IValidator<T>` + `ICommandHandler<T,R>` that does
    `visit = await visits.FindAsync(id); ... mutate ...; return Result`.
- Queries: `src/Animora.Application/UseCases/Visits/EncounterQueries.cs` — `EncounterDraftQuery`
  returns `EncounterEditReadModel(Draft, Vitals, History, Exam, Diagnosis, Plan, Lines)`.
  **→ Extend `EncounterEditReadModel` to also carry radiology list + lab list** so the wizard hydrates them.
- Registration: `src/Animora.Application/UseCases/Visits/VisitsRegistration.cs`
  (`.AddCommand<Cmd,Res,Handler>().AddValidator<Cmd,Validator>()` chain). Add new commands here.

### 0.4 SYNC / push-pull (why journaling matters — the task's "هماهنگی سرور و پوش/پول")
- A command implementing **`IJournaledCommand`** is automatically written to the command journal in the
  SAME transaction as the business write (`CommandJournalBehavior`, stage 6) → this is the record that
  gets **pushed** to the central server and **pulled/replayed** on other clients. **This is the entire
  sync mechanism.** So: **make every new radiology/lab mutation command `IJournaledCommand` +
  `[Auditable]` and register its `CommandTypeName` in `CommandTypes` + add it to `CommandTypes.All`**
  (`src/Animora.Contracts.Wire/Commands/CommandTypes.cs`, consts near line 102–109, array near line 144–146).
  Getting these three right = "creator client, server, and polling client all stay in sync" for free.
- Settings (lab types) are **preferences, NOT journaled** — see 0.5.

### 0.5 Settings pattern (for "انواع آزمایش configurable in تنظیمات" + where the Toolbox Settings tab leads)
- The appointment-type settings are the exact template:
  - Port: `src/Animora.Application/Ports/IAppointmentSettingsStore.cs` (durable JSON file beside the DB).
  - Use cases: `src/Animora.Application/UseCases/Scheduling/AppointmentSettingsUseCases.cs` —
    `GetAppointmentSettingsQuery` (`[RequiresPermission(AppointmentsRead)]`), and write commands
    `[RequiresPermission(SettingsWrite)] record ... : ICommand<T>, ISelfTransactedCommand` (NOT journaled).
  - Contract DTO lives in `src/Animora.Contracts/Appointments/` (`AppointmentSettings`, `CustomAppointmentType`).
  - Client service: `IAppointmentSettingsService` (used by `VisitsTriageService.GetVisitTypesAsync`).
  - **→ Lab-test types** = a new settings store (`ILabTestCatalogStore`) OR extend the appointment
    settings store with a `LabTestTypes` list. **Prefer a dedicated `ILabTestCatalogStore`** (own JSON
    file) to keep concerns clean, mirroring `IAppointmentSettingsStore` 1:1. Each lab type =
    `{ Id, Name }` (optionally a default unit). `SettingsWrite` for add/remove, `VisitsRead` (or a
    settings-read perm) for get.

### 0.6 EF migration mechanics (REQUIRED — a migration IS needed, see 0.3)
- Migrations dir: `src/Animora.Persistence.Sqlite/Migrations/` — latest is `..._035_PeerInvoiceDetail.cs`.
  Format: `yyyyMMddHHmmss__NNN_Name.cs` (+ `.Designer.cs`) and update
  `AnimoraLocalDbContextModelSnapshot.cs`.
- **Add ONE migration `..._036_VisitRadiologyLab`** adding two `TEXT NOT NULL DEFAULT '[]'` columns to
  `Visits`: `Radiology` and `LabResults` (the JSON-list columns from 0.3). Prefer generating it with the
  EF tooling if available in the sandbox (check `scripts/sandbox/` for an EF/migrations helper); otherwise
  author the `Up`/`Down` (`migrationBuilder.AddColumn<string>(...)` / `DropColumn`) + `.Designer.cs` +
  snapshot entries by copying the shape of the newest existing migration. Columns default `'[]'` so
  existing Visit rows stay valid. Do both radiology (T2) and lab (T5) columns in this single migration to
  avoid two schema bumps — create it in T2 and reuse it in T5.
- Attachments (radiology images): reuse `ILocalFileStore` (`src/Animora.Application/Ports/ILocalFileStore.cs`,
  content-addressed SHA-256 PutAsync/OpenAsync) + `DocumentReference` metadata entity
  (`src/Animora.Domain/Patients/DocumentReference.cs`). Store the file via `ILocalFileStore.PutAsync`,
  keep the returned hash + filename/contentType in the radiology record's attachment list.

### 0.7 Toolbox / page-section switch pattern (Task 0 — the styling reference)
- The **Patients page** is the exact template for a "toolbox with a navbar to switch pages":
  - Enum: `src/Animora.Desktop/Pages/Patients/PatientsPageEnums.cs` →
    `enum PatientsSection { Directory, Settings }`.
  - VM exposes `IsSettingsSection` / `IsDirectory...` bool props + a `SetSectionCommand` (CommandParameter
    "settings"/"directory") — bound in `PatientsPageView.axaml`.
  - XAML: a `.pb-seg` segmented switch — a soft rounded track (`ShadowSegment`) holding pill `Border`s
    with `Classes="segitem" Classes.on="{Binding IsXxxActive}"`, each wrapping a `Button` with
    `Command="{Binding SetSectionCommand}" CommandParameter="..."`. See `PatientsPageView.axaml`
    lines ~11–44 (style), ~552–582 (the three-tab strip owners/pets/settings), ~1262–1270
    (settings section visibility via `IsSettingsSection`).
  - The Settings tab swaps the body `ScrollViewer` (`IsVisible="{Binding IsSettingsSection}"`) and the
    directory body `IsVisible="{Binding ShowOwnersTable}"` etc. — same page, toggled content.
  - Settings sub-VM: `src/Animora.Desktop/Pages/Patients/Settings/PatientsSettingsViewModel.cs`
    exposed as a `Settings` property on the page VM and bound as `{Binding Settings.X}`.
- **Cashier/Sales toolbox** (`SalePageView.axaml`) uses the same segmented-pill look — also acceptable
  visual reference. Match whichever is closer to the Visits page's existing header rhythm.

### 0.8 Modal-over-page pattern (Task says "a page opens over the current one, fill the form")
- `IDialogService` (`src/Animora.Desktop/Services/IDialogService.cs`):
  - `ShowModal(object content, double maxWidth = 520)` — centered modal (used by catalog picker).
  - `ShowSheet(object content, double width = 680, double height = 760)` — a larger sheet over the page.
- **→ Radiology form and Lab form each = a `*SheetViewModel` + `*SheetView.axaml` pair, opened with
  `_dialogs.ShowSheet(new RadiologySheetViewModel(...), ...)`.** On save, the sheet calls back into the
  encounter VM to add the record (same way the catalog picker's `addItem` callback works).

### 0.9 Permissions / capabilities already in use
- `_capabilities.Can("visits.write")` gates editing; `"visits.complete"` gates finishing.
- `PermissionCodes.VisitsWrite / VisitsRead / VisitsComplete / SettingsWrite` exist and are used above.
- Reuse `VisitsWrite` for radiology/lab record mutations; `SettingsWrite` for lab-type settings.

### 0.10 Design/style tokens
- Theme tokens: `src/Animora.Desktop/Resources/Theme/*.axaml` (Colors/Typography/Spacing/Elevation/Icons).
- Reuse existing accent `#0d9488` (teal) family already used by the encounter. Icons via `IconKey` keys
  (e.g. `IconClipboard`, `IconBox`); pick/confirm radiology & lab icon keys from `Resources/Theme/Icons.axaml`.
- Persian digits/Jalali: `src/Animora.Desktop/Localization/PersianNumberFormatter` + the `JalaliDatePicker`
  control (`src/Animora.Desktop/Controls/`) — use `JalaliDatePicker` for "ثبت تاریخ رادیولوژی/آزمایش".

---

## 1. Task list (each task = one push)

Status legend: `[ ]` pending · `[~]` in progress · `[x]` done.

---

### [x] T1 — Task 0: Toolbox navbar on the Visits encounter page (Visit ⇄ Settings)
**Goal:** Add a segmented toolbox (like Patients/Cashier) at the top of the Visits area letting the
user switch between the **ویزیت** (encounter) view and a **تنظیمات** (visits settings) view.
**Decide first:** the toolbox belongs on the Visits *area*. The encounter form (`VisitEncounterView`)
and the triage page (`VisitsTriagePageView`) are separate pages. Put the Visit/Settings switch where
the user lands for Visits — the **triage page** (`VisitsTriagePageView` / `VisitsTriagePageViewModel`)
is the Visits entry screen, so add the toolbox there (and, if the encounter view shares a header
region, mirror it). Confirm by reading the top ~40 lines of `VisitsTriagePageView.axaml`.
**Do:**
1. Add `enum VisitsSection { Visit, Settings }` (new file `Pages/Visits/VisitsPageEnums.cs` or inside
   the triage VM file — match Patients which uses a dedicated enums file).
2. On `VisitsTriagePageViewModel`: add `[ObservableProperty] VisitsSection _section`, computed
   `IsVisitSection`/`IsSettingsSection` bools, and `[RelayCommand] SetSection(string)` setting the enum
   (notify the bool props). Add a `VisitsSettingsViewModel Settings { get; }` property (built in T4).
3. In `VisitsTriagePageView.axaml`: add the segmented pill strip (copy the `.pb-seg`/`segitem`/`on`
   style + markup from `PatientsPageView.axaml` ~11–44 & ~552–582; relabel to «ویزیت» / «تنظیمات»).
   Wrap the existing body in `IsVisible="{Binding IsVisitSection}"`; add an empty settings host
   `ScrollViewer IsVisible="{Binding IsSettingsSection}"` (populated in T4).
**Files:** `Pages/Visits/VisitsTriagePageView.axaml`, `Pages/Visits/VisitsTriagePageViewModel.cs`,
new `Pages/Visits/VisitsPageEnums.cs`. Style source: `Pages/Patients/PatientsPageView.axaml`.
**Verify:** `scripts/sandbox/build.sh` 0/0. (No DI change → smoke-run optional.)
**Push:** `scripts/git/push.sh "feat(visits): add toolbox navbar to switch between visit and settings"`

---

### [ ] T2 — Task 1: Radiology — backend (Domain + Application + persistence + sync)
**Goal:** Record radiology entries on a visit: date, result text, body part/organ, + image/file attachment(s).
**Model (add to `src/Animora.Domain/Clinical/`):** new `RadiologyRecord` value object/record:
`record RadiologyRecord(string Id, DateTimeOffset TakenUtc, string BodyPart, string? Result,
IReadOnlyList<RadiologyAttachment> Attachments)` and
`record RadiologyAttachment(string FileHash, string FileName, string ContentType)`.
**Visit.cs:** add `private readonly List<RadiologyRecord> _radiology = []`, expose
`IReadOnlyList<RadiologyRecord> Radiology`, add `Result AddRadiology(RadiologyRecord)` /
`Result RemoveRadiology(string id)` guarded by `EnsureOpen()` (see 0.3 state rules).
**Persistence:** in `VisitConfiguration.cs` add
`builder.Ignore(e => e.Radiology); JsonFields.Map<Visit, RadiologyRecord>(builder, "_radiology", "Radiology");`
**This adds a new `Radiology` TEXT column (confirmed, 0.3) → create migration `..._036_VisitRadiologyLab`
here** adding BOTH the `Radiology` and `LabResults` columns (reused by T5), each `TEXT NOT NULL DEFAULT '[]'`,
+ `.Designer.cs` + snapshot update (0.6).
**Application (`EncounterCommands.cs` + `VisitsRegistration.cs`):**
- `AddRadiologyCommand(string VisitId, long TakenUtcMs/or DateTimeOffset, string BodyPart, string? Result,
  IReadOnlyList<RadiologyAttachmentDto> Attachments, string? RecordId)` →
  `: ICommand<string>, IJournaledCommand`, `[RequiresPermission(VisitsWrite)]`,
  `[Auditable(AuditCategory.Clinical, CommandTypes.VisitsRadiologyAdd)]`.
- `RemoveRadiologyCommand(string VisitId, string RecordId)` → `ICommand<Unit>, IJournaledCommand`,
  `[Auditable(..., CommandTypes.VisitsRadiologyRemove)]`.
- Validators + handlers (handler: `FindAsync` → `visit.AddRadiology(...)` → Result), deterministic id via
  `IdGenerator.ForOperation(context.OperationId, slot, clock.UtcNow)` when `RecordId` null (mirror
  `AddPrescriptionLineCommandHandler`).
- Register in `VisitsRegistration.cs`.
**Sync (CRITICAL — 0.4):** add consts `VisitsRadiologyAdd = "visits.radiology.add"`,
`VisitsRadiologyRemove = "visits.radiology.remove"` in `CommandTypes.cs` AND add both to the
`CommandTypes.All` array.
**Query:** extend `EncounterEditReadModel` (`EncounterQueries.cs`) with
`IReadOnlyList<RadiologyRecord> Radiology` and populate from `visit.Radiology`.
**Files:** `Domain/Clinical/Visit.cs`, new `Domain/Clinical/RadiologyRecord.cs`,
`Persistence.Sqlite/Configurations/Clinical/VisitConfiguration.cs`,
`Application/UseCases/Visits/EncounterCommands.cs`, `.../EncounterQueries.cs`, `.../VisitsRegistration.cs`,
`Contracts.Wire/Commands/CommandTypes.cs`. (Attachments use `ILocalFileStore` — 0.6.)
**Verify:** backend changed → `scripts/sandbox/build.sh full` 0/0.
**Push:** `scripts/git/push.sh "feat(visits): radiology domain+command+journal+query backend"`

---

### [ ] T3 — Task 1: Radiology — Contracts + Client.Services seam
**Goal:** Expose radiology through the `IVisitEncounterService` seam so the wizard can read/add/remove.
**Do:**
- In `src/Animora.Contracts/Visits/VisitsEncounterModels.cs` add contract records
  `RadiologyEntry(string Id, DateTime TakenAt, string BodyPart, string? Result, IReadOnlyList<RadiologyFile> Files)`
  and `RadiologyFile(string Hash, string Name, string ContentType)`.
- In `IVisitEncounterService.cs` add:
  `Task<string> AddRadiologyAsync(string encounterId, RadiologyEntry entry);`
  `Task RemoveRadiologyAsync(string encounterId, string radiologyId);`
  `Task<string> StoreAttachmentAsync(Stream content, ...)` (or accept file paths) — wrap `ILocalFileStore`.
  Also surface radiology list on the draft/edit model returned by `GetAsync` (extend `EncounterDraft`
  or add a `GetRadiologyAsync`/include in the edit read model mapping).
- Implement in `VisitEncounterService.cs` (map DTO → command, `SendAsync`, mirror `AddRxLineAsync`).
- Update `VisitEncounterMapping.cs` for the new read-model → contract mapping.
**Files:** `Contracts/Visits/VisitsEncounterModels.cs`, `Contracts/Visits/IVisitEncounterService.cs`,
`Client.Services/Visits/VisitEncounterService.cs`, `Client.Services/Visits/VisitEncounterMapping.cs`.
**Verify:** `scripts/sandbox/build.sh full` 0/0.
**Push:** `scripts/git/push.sh "feat(visits): radiology contracts + client-service seam"`

---

### [ ] T4 — Task 2: Lab-test types in Settings (Settings store + commands + settings sub-page) + Task 0 Settings body
**Goal:** (a) make lab-test types configurable in Visits→تنظیمات; (b) fill the Settings tab body added in T1.
**Backend (settings — NOT journaled, 0.5):**
- New port `ILabTestCatalogStore` (`Application/Ports/`) mirroring `IAppointmentSettingsStore`:
  `GetAsync`, `AddTypeAsync(name, defaultUnit?)`, `RemoveTypeAsync(id)`. Contract DTO
  `LabTestType(string Id, string Name, string? DefaultUnit)` in `Contracts/Visits/` (or a
  `Contracts/Settings/`).
- Use cases (`Application/UseCases/Visits/` or `.../Settings/`): `GetLabTestTypesQuery`
  `[RequiresPermission(VisitsRead)]`; `AddLabTestTypeCommand`/`RemoveLabTestTypeCommand`
  `[RequiresPermission(SettingsWrite)] ... ISelfTransactedCommand`. Register them.
- Implement the store in `Animora.Infrastructure.Local/` (JSON file beside DB — copy the
  appointment-settings store implementation shape). Register in that project's DI extension + App.
**Frontend (Settings sub-VM, fills T1's settings host):**
- New `Pages/Visits/Settings/VisitsSettingsViewModel.cs` (template:
  `Pages/Patients/Settings/PatientsSettingsViewModel.cs`): a `RangeObservableCollection<LabTestTypeItemVM>`,
  `NewLabTypeName`, `AddLabTypeCommand`, per-item `DeleteCommand`. Depends on a new client service
  `ILabTestSettingsService` (Contracts) → impl in `Client.Services/Visits/`.
- Expose as `Settings` on `VisitsTriagePageViewModel` (T1 step 2). Build the settings body UI inside the
  `IsSettingsSection` host in `VisitsTriagePageView.axaml` (copy the species/breeds card layout from
  `PatientsPageView.axaml` ~1262–1560, relabel to «انواع آزمایش»).
- Register all new services in `src/Animora.Desktop/App.axaml.cs`.
**Files:** new `Application/Ports/ILabTestCatalogStore.cs`, new use-case file, `VisitsRegistration.cs`,
`Infrastructure.Local/...` (store + DI), `Contracts/...` (DTO + `ILabTestSettingsService`),
`Client.Services/Visits/...`, new `Pages/Visits/Settings/VisitsSettingsViewModel.cs` +
`LabTestTypeItemViewModel.cs`, `VisitsTriagePageView(.axaml)` + VM, `App.axaml.cs`.
**Verify:** DI + backend changed → `scripts/sandbox/build.sh full` 0/0 then `scripts/sandbox/run.sh` → SMOKE RUN OK.
**Push:** `scripts/git/push.sh "feat(visits): configurable lab-test types in settings + settings tab body"`

---

### [ ] T5 — Task 2: Lab results — backend (Domain + Application + persistence + sync)
**Goal:** Record lab results on a visit: date, description/notes box, and per-row {test type + value}.
**Model (`Domain/Clinical/`):** `record LabResult(string Id, DateTimeOffset TakenUtc, string? Description,
IReadOnlyList<LabResultItem> Items)`, `record LabResultItem(string TypeId, string TypeName, string Value, string? Unit)`.
**Visit.cs:** `_labResults` list + `AddLabResult`/`RemoveLabResult` guarded by `EnsureOpen()`; `JsonFields.Map`
in `VisitConfiguration.cs` → the `LabResults` column. **No new migration here — the `LabResults` column was
already created by T2's `..._036_VisitRadiologyLab` migration** (just confirm the mapping matches the column).
**Application:** `AddLabResultCommand` / `RemoveLabResultCommand` → `IJournaledCommand`,
`[RequiresPermission(VisitsWrite)]`, `[Auditable(Clinical, CommandTypes.VisitsLabAdd/Remove)]` + validators +
handlers (mirror radiology). Register in `VisitsRegistration.cs`.
**Sync:** add `VisitsLabAdd="visits.lab.add"`, `VisitsLabRemove="visits.lab.remove"` consts + append to
`CommandTypes.All`.
**Query:** extend `EncounterEditReadModel` with `IReadOnlyList<LabResult> Labs`; populate from `visit.LabResults`.
**Files:** `Domain/Clinical/Visit.cs`, new `Domain/Clinical/LabResult.cs`, `VisitConfiguration.cs`,
`EncounterCommands.cs`, `EncounterQueries.cs`, `VisitsRegistration.cs`, `CommandTypes.cs`.
**Verify:** `scripts/sandbox/build.sh full` 0/0.
**Push:** `scripts/git/push.sh "feat(visits): lab-result domain+command+journal+query backend"`

---

### [ ] T6 — Task 2: Lab results — Contracts + Client.Services seam
**Goal:** Expose lab results via `IVisitEncounterService` (mirror T3).
**Do:** Contract records `LabEntry(Id, TakenAt, Description, IReadOnlyList<LabItem> Items)`,
`LabItem(TypeId, TypeName, Value, Unit)`; interface methods `AddLabAsync`/`RemoveLabAsync`; implement in
`VisitEncounterService.cs`; map read-model in `VisitEncounterMapping.cs`. The lab form will fetch
selectable types via `ILabTestSettingsService` (T4).
**Files:** `Contracts/Visits/VisitsEncounterModels.cs`, `IVisitEncounterService.cs`,
`Client.Services/Visits/VisitEncounterService.cs`, `VisitEncounterMapping.cs`.
**Verify:** `scripts/sandbox/build.sh full` 0/0.
**Push:** `scripts/git/push.sh "feat(visits): lab-result contracts + client-service seam"`

---

### [ ] T7 — Task 1 & 2 UI: Radiology & Lab option buttons + their over-page form sheets
**Goal:** In the encounter form add two option entries «رادیولوژی» and «آزمایشات». Clicking each opens a
sheet over the current page (0.8) with its form; filled data is saved through the seam and listed on the visit.
**Do (on `VisitEncounterViewModel.cs` + `VisitEncounterView.axaml`):**
- Add `ObservableCollection<RadiologyEntryViewModel> RadiologyEntries` and
  `ObservableCollection<LabEntryViewModel> LabEntries`; hydrate them in `LoadAsync` from the extended
  edit read model.
- Add `[RelayCommand] OpenRadiology()` → `_dialogs.ShowSheet(new RadiologySheetViewModel(onSave, _toast, ...))`
  guarded by `CanEditVisit` (mirror `OpenCatalogPicker`). Same for `[RelayCommand] OpenLab()`.
- `RadiologySheetViewModel`/`View`: fields — JalaliDatePicker (تاریخ), BodyPart (عضو), Result (نتیجه),
  attach file button (uses `StoreAttachmentAsync` → shows attached file chips). On save → callback adds via
  `_service.AddRadiologyAsync` then appends to `RadiologyEntries`; `_changes.Notify(DataDomain.Visits, History, Patients)`.
- `LabSheetViewModel`/`View`: JalaliDatePicker (تاریخ), Description box (توضیح), a repeating row editor
  {type ComboBox from `ILabTestSettingsService` + value TextBox}, add-row button. On save → `_service.AddLabAsync`.
- Add the two option entries to the encounter form markup styled consistently with the existing
  service/item "add" affordances (teal accent, icon + label). Place them near the services/rx region.
- File-attachment UI: use a file picker (Avalonia `StorageProvider` via the view) → pass stream to
  `StoreAttachmentAsync`. Display attachments as removable chips; open on click via `ILocalFileStore.OpenAsync`.
**Files:** `Pages/Visits/VisitEncounterViewModel.cs`, `Pages/Visits/VisitEncounterView.axaml`,
new `Pages/Visits/RadiologySheetViewModel.cs` + `RadiologySheetView.axaml(.cs)`,
new `Pages/Visits/LabSheetViewModel.cs` + `LabSheetView.axaml(.cs)`,
new `RadiologyEntryViewModel.cs` + `LabEntryViewModel.cs`. Register sheet VMs if DI-resolved (else `new`).
**Verify:** `scripts/sandbox/build.sh full` 0/0 + `scripts/sandbox/run.sh` → SMOKE RUN OK (UI/DI touched).
**Push:** `scripts/git/push.sh "feat(visits): radiology & lab option sheets wired end-to-end in encounter"`

---

### [ ] T8 — End-to-end consistency & design pass (creator ⇄ server ⇄ polling client)
**Goal:** Confirm the three sync legs and design coherence without heavy testing.
**Checklist (targeted reads only, no blind exploration):**
- Every new mutation command is `IJournaledCommand` + `[Auditable]` and its `CommandTypeName` is in
  `CommandTypes.All` (grep the four new consts) → guarantees push + pull replay.
- `EncounterDraftQuery` returns radiology+lab so a client that **pulls** a synced visit renders them
  (the read model → contract → VM hydration path is complete).
- Lab types settings use `SettingsWrite` and are NOT journaled (preferences), matching 0.5.
- Attachments stored by hash via `ILocalFileStore`; radiology record carries hash+name (survives sync as
  metadata; blob sync is out of scope unless already handled — note it if so).
- Visual: radiology/lab entries + option buttons use the encounter's existing card/pill/teal styling,
  Persian digits, Jalali dates, RTL. Settings tab matches the Patients settings card layout.
**Verify:** final `scripts/sandbox/build.sh full` 0/0 + `scripts/sandbox/run.sh` → SMOKE RUN OK.
**Push:** `scripts/git/push.sh "chore(visits): end-to-end sync + design consistency pass"`

---

### [ ] T9 — FINAL: delete this plan file
**Do:** `git rm Plan/VISITS-RADIOLOGY-LAB-TOOLBOX.md` (keep `Plan/.gitkeep`). Touch no other file / no roadmap.
**Push:** `scripts/git/push.sh "chore(plan): remove completed visits radiology/lab/toolbox plan"`

---

## 2. Execution notes
- Order matters: T2/T3 (radiology backend→seam), T4 (lab settings), T5/T6 (lab backend→seam), then T7 UI.
  T1 (toolbox) first so T4 has a Settings host. Backend-before-UI keeps each build green.
- Keep each command's handler minimal (FindAsync → mutate → Result) — the pipeline (journal, audit,
  transaction, authorization) is applied automatically by attributes/markers.
- If any single task balloons into a large/heavy change mid-way, push immediately with a clear message
  (per LOCKED rule) before continuing.
- Do not read `Animora-UI/**`, `Database-Sampel/**`, or roadmap `phases/**` — not needed here.
