# Pet Profile (پرونده بیمار) — Task Plan

> Self-contained execution plan. A fresh model should implement **without re-investigating** the files
> already distilled here. Only open the exact files/line-ranges named per task.
> **LOCKED RULE:** after finishing EACH task below, run `scripts/git/push.sh "msg"` (commit+sync+push).
> **The commit subject MUST carry the task id** (e.g. `feat(patients): T10b …`) — required.
> If a big fix/refactor happens mid-task, push that too. No push = failure.
> Main app = the **.NET** solution under `src/`. **Never** open `Animora-UI/`.

---

## 0.1 Status & branch sync (updated 2026-10-18)
- **Branches:** `main` and `genspark_ai_developer` were **identical** at commit `7b864a40`
  (`git log origin/main..origin/genspark_ai_developer` and the reverse were both empty). No divergence,
  no progress at risk → **no PR** (per instruction); work is committed **directly on `main`** via `scripts/git/push.sh`.
- **Task number in every commit subject is REQUIRED** (`… : T<n> …`).
- **Local sandbox build/test is FORBIDDEN** → all builds/tests run on GitHub Actions via `scripts/ci/gh-ci.sh`.
- **Progress:** T1–T9 ☑ (committed). **T10a ☑ done + pushed.** **T10b ☑ done + pushed.** Remaining: **T11, T12**.

---

## 0. Architecture facts (already discovered — do NOT re-derive)

**Screen = "Pet File"** (Persian "پرونده بیمار"). Core files:
- View: `src/Animora.Desktop/Pages/Patients/PetFileView.axaml` (1252 ln) + `.axaml.cs`.
- VM: `src/Animora.Desktop/Pages/Patients/PetFileViewModel.cs` (855 ln). **Hub for almost all tasks.**
- Contract DTO + service seam: `src/Animora.Contracts/Patients/` →
  `PatientDetailModels.cs` (records), `IPatientsQueryService.cs` (read/write seam).
- Real service: `src/Animora.Client.Services/Patients/PatientsService.cs` (374 ln) — wraps CQRS dispatcher.
- Read-model↔DTO map: `src/Animora.Client.Services/Patients/PatientsMapping.cs` (`ToDetail`).
- Read models: `src/Animora.Application/UseCases/Patients/ReadModels/PatientReadModels.cs`.
- SQLite query: `src/Animora.Persistence.Sqlite/Queries/PatientDirectoryQueries.cs` (`GetPetAsync`).
- Write commands: `src/Animora.Application/UseCases/Patients/` (`UpdatePetInfoCommand`, `AddWeightEntryCommand`, `CreatePetCommand`…).
- DI: `src/Animora.Desktop/App.axaml.cs` — L446 `PetFileViewModel` (transient), L458 `IVisitsTriageService`,
  L459 `VisitsTriagePageViewModel` (singleton), L486 `VisitEncounterViewModel` (transient).

**Data flow:** PetFileVM.LoadAsync(petId) → `IPatientsQueryService.GetPetAsync` →
PatientsService dispatches `PetDetailQuery` → `PatientDirectoryQueries.GetPetAsync` (SQLite) →
`PetDetailReadModel` → `PatientsMapping.ToDetail()` → `PetDetail` contract → `Apply(pet)` builds all tabs.

**⚠️ CRITICAL GAPS found in `PatientsMapping.ToDetail()` (L39-48):** it **hardcodes**
`Vaccinations: [], Labs: [], Documents: [], Surgeries: []`, `Vitals = new PetVitals(0,0,0,"")`,
`Habitat:"",Behavior:"",Diet:"",BloodType:null,NeuteredDate:null,Insurance:null,Notes:null`,
and **drops `m.Radiology`** entirely (read model has it, map ignores it). So today the UI shows
empty vitals/vax/labs/docs/radiology regardless of DB. Several tasks must widen this chain.

> **Update (after T1/T9/T10a):** `ToDetail` no longer hardcodes `Vitals` (T1), `Vaccinations` (T9), or
> `Labs`/`Radiology` (T10a) — all read real data now. Still hardcoded: **`Documents`** (→ T10b) and
> `Surgeries` (no source anywhere yet — out of scope).

**PetDetailReadModel** (`PatientReadModels.cs` L54-61) ALREADY carries: `Weights`, `Visits`,
`Labs`, `Radiology` (optional). But `PatientDirectoryQueries.GetPetAsync` (L212-235) only fills
`weights` + `visits`; it **never queries Labs/Radiology** (comment says deferred). Tables available
in the DbContext: `Pet`, `Owner`, `WeightEntry`, `Visit` (has `Vitals` = Domain `PetVitals`:
`WeightKg, TemperatureC, HeartRateBpm, RespiratoryRate, MucousMembrane, Hydration` — **no BCS field**),
`VaccinationRecord` (fields incl. `PetId, VaccineCode, VaccineName, DoseNumber, TotalDoses,
AdministeredUtc, NextDueUtc, VisitId, State`), `MedicalHistoryEntry`.

**Visit encounter / "start a visit" flow** (task 2 target):
- `VisitsTriagePageViewModel` (`Pages/Visits/VisitsTriagePageViewModel.cs`, 687 ln) is the gate.
  `StartNewAsync()` (L461-485) builds `NewVisitRequest(DoctorId, PatientName, PatientKind, OwnerName,
  OwnerPhone, PatientIconKey, IsNewPatient, TypeId, IsUrgent, Note)` → `IVisitsTriageService.StartNewAsync`
  → returns `EncounterDraft` → `INavigationService.NavigateToAsync<VisitEncounterViewModel>(vm=>vm.LoadAsync(draft))`.
- Doctor rail data: `IVisitsTriageService.GetDoctorsAsync()` → `DoctorRailInfo(Id,Name,Specialty,Initials,
  Accent,AppointmentCount,WalkInCount,HasVisitInProgress)`. Contract: `src/Animora.Contracts/Visits/
  IVisitsTriageService.cs` + `VisitsTriageModels.cs`.
- `EncounterDraft` is the full context record (patient+owner+doctor+type). `NewVisitRequest.IsNewPatient=false`
  for an existing pet.
- **Note:** `NewVisitRequest` has **no PetId** field — the triage service resolves the patient by
  name/owner. Before building the pet-file "register visit", CONFIRM whether StartNewAsync can bind to
  a specific pet id; if not, EITHER add an optional `PatientId` to `NewVisitRequest` + wire it through
  `VisitsTriageService.StartNewAsync` (open `src/Animora.Client.Services/Visits/*` for the impl) OR reuse
  the existing path keyed by patient name+owner. Prefer adding `PatientId?` (cleaner, exact binding).

**Existing PetFileVM commands & state (for reference):**
- Stubs that currently only toast "نمونه طراحی": `AddVisit` (L668-669), `AddVax` (L671-672),
  `DownloadDoc` (L683-685), `UploadDoc` (L687-688). These are the no-op placeholders to replace.
- `AddWeightAsync` (L637-666): parses a **manually typed** Jalali date from `NewWeightDate` TextBox. Task 7 replaces with calendar.
- `SaveInfoAsync` (L605-635): persists only Name/Breed/Color/Birth/Habitat/Behavior/Diet via
  `SavePetInfoAsync`. BUT `UpdatePetInfoCommand` already accepts Allergies+ChronicConditions &
  Neutered — info edit buffers don't expose them. (Task 6.)
- `BuildInfoItems(editable:true)` (L557-567) exposes only 7 edit fields. Read view (L535-556) shows 13.
- `Alerts` built read-only in `BuildAlerts` (L405-435) from `pet.ChronicConditions` + `pet.Allergies` +
  overdue-vax. No add/remove UI. (Task 3.)
- `Vitals` built in `BuildVitals` (L396-403) from `pet.Vitals` → always 0 today. (Task 1/4.)
- Weight chart: `WeightMiniPoints`/`WeightPoints` from `pet.Weights` (`BuildWeight` L359-394). (Task 8.)

**Shared add-pet form:** `PetFormViewModel.cs` (185 ln) + `PetFormView.axaml` → `BuildRequest()` →
`NewPetRequest(Name,Species,Breed,Color,Gender,BirthDate,Habitat,Behavior,Diet,MicrochipCode,
MicrochipDate,Neutered)`. Used by add-pet modal & wizard. (Relevant to task 3 "add alerts at registration".)

**Jalali date picker:** there IS an existing calendar/date-input pattern (other date inputs open a
calendar panel). FIND it before task 7 — grep `Jalali` + `Calendar`/`DatePicker` under
`src/Animora.Desktop/Controls/` and see how `AddPatientWizard`/`VitalsStep` birth-date input works; reuse
that control, do NOT invent a new one. (Needed by tasks 7 & 9 & 3 date fields.)

**DataChangeNotifier:** after any write, call `_changes.Notify(DataDomain.Patients, …)` (see
`SaveInfoAsync` L631) so dashboard/appointments/visits lists refresh. Reuse that pattern for every new write.

**Toast + guarded command pattern:** wrap awaited seam calls; `InvalidOperationException` →
`PatientsPresentation.CommandFailureMessage(ex, fallback)` → `_toast.Show(..., Tone.Danger)`
(see `DeleteConfirmedAsync` L751-773 and VisitsTriage `RunGuardedAsync` L303-313).

**Build/test:** build ONLY the projects you touch (e.g. `dotnet build src/Animora.Desktop/Animora.Desktop.csproj`
and the contracts/app/persistence projects as needed). **Do NOT run the full solution test suite** (locked
rule: no long full test). Fix all errors + warnings you introduce.

---

## Tasks (status: ☐ todo / ☑ done)

### ☑ Task 1 — Summary tab: show LAST-EXAM vitals (not zeros)
**Problem:** "علائم حیاتی آخرین معاینه" panel always shows 0/0/0/empty because `ToDetail` hardcodes
`PetVitals(0,0,0,"")`.
**Fix layers:**
1. Persistence `GetPetAsync`: project the **latest non-cancelled visit's `Vitals`** (WeightKg,
   TemperatureC, HeartRateBpm, RespiratoryRate) into a new field on `PetDetailReadModel` (add
   `LastVitals` read-model record or reuse the top visit already fetched in `GetPetVisitsAsync` —
   the newest visit is `visits[0]`; its `WeightKg`/`TemperatureC` already there, but Pulse/Resp are
   NOT in `PetVisitReadModel` → add `HeartRateBpm`,`RespiratoryRate` to `PetVisitReadModel` OR a
   dedicated `PetVitalsReadModel`). **Decision baked in:** add nullable
   `int? PulseBpm, int? RespRate` to `PetVisitReadModel` (fill from `v.Vitals?.HeartRateBpm/RespiratoryRate`)
   — cheapest, reuses the already-materialized newest visit.
2. `PatientDetailModels.PetVitals` (contract) = `(double Temperature,int Pulse,int Respiration,string BCS)`.
   BCS has **no domain source** → leave BCS "—" (string.Empty) for now; document it.
3. `ToDetail`: set `Vitals` from the newest visit's vitals (visits are newest-first in the read model).
   Map null → 0 but render "—" in VM when the source visit is absent (guard `BuildVitals`:
   if no visits, show "—" not "۰").
4. `BuildVitals` (VM L396-403): when a value is 0 AND there is no exam, display "—".
**Done when:** opening a pet with a completed visit shows that visit's temp/pulse/resp; a pet with no
visit shows "—" cleanly. **Push.**

### ☑ Task 2 — "ثبت ویزیت" button → real, UX-polished visit start with doctor selection
**Problem:** `AddVisit` (VM L668) is a toast stub. Button appears in hero (axaml L188-197) and visits tab
(L786-794).
**Fix:**
1. Inject into `PetFileViewModel`: `IVisitsTriageService` + `INavigationService` (+ `IDialogService`
   already present). Update DI — `PetFileViewModel` is `AddTransient` (App L446), DI resolves ctor args
   automatically, so no registration change if services already registered (they are: L458 triage, and
   `INavigationService` L396).
2. Replace `AddVisit` with `async Task AddVisitAsync()`:
   - Load doctors via `_triage.GetDoctorsAsync()`.
   - Show a **doctor-picker modal** (professional UX): a `ConfirmDialog`-style sheet listing doctor tiles
     (avatar initials + name + specialty + today's appt count), single-select. Build a small new
     modal VM/View under `Pages/Patients/` (e.g. `PickDoctorModalView(.axaml/.axaml.cs/ViewModel)`), OR
     reuse `DoctorTileViewModel` (`Pages/Visits/DoctorTileViewModel.cs`) for the tile. Keep it RTL,
     brand-styled, matching the sheet visual language of `PetFileView`.
   - On confirm: build `NewVisitRequest` with this pet (`PatientName=_pet.Name`, `OwnerName`, `OwnerPhone`
     from `_owner`, `PatientKind` from breed/species, `PatientIconKey` via
     `VisitsPresentation.IconKeyForKind`, `IsNewPatient:false`, `TypeId:"visit"` default or let user pick
     a visit type in the same modal, `IsUrgent:false`, `Note:""`). **Bind the exact pet:** add optional
     `string? PatientId` to `NewVisitRequest` + thread through `VisitsTriageService.StartNewAsync`
     (impl in `src/Animora.Client.Services/Visits/VisitsTriageService.cs` — open it) so the encounter is
     tied to THIS pet id, not a name lookup.
   - `var draft = await _triage.StartNewAsync(request);` then **close the pet sheet** (`_dialogs.Close()`)
     and `await _navigation.NavigateToAsync<VisitEncounterViewModel>(vm => vm.LoadAsync(draft));`.
   - Guard with the toast pattern.
3. Rename command binding if needed (`AddVisitCommand` already bound in axaml L190 & L787 — keep name).
**UX bar:** smooth transition, clear doctor cards, selected state, disabled confirm until a doctor is
chosen, loading state while doctors fetch. **Done when:** clicking ثبت ویزیت lets you pick a doctor and
lands in the encounter wizard pre-bound to this pet. **Push.**

### ☑ Task 3 — Clinical alerts (هشدارهای بالینی): make them addable (at registration AND during visit), row-wise
**Current:** alerts are read-only, derived from `pet.Allergies`+`pet.ChronicConditions` (persisted via
`UpdatePetInfoCommand.AllergyNotes/ChronicNotes`, newline-joined — see mapping `Split`/`JoinList`).
**Goal:** add/remove alert rows (allergy / chronic condition) both (a) when creating the pet and
(b) inside the visit encounter flow, and (c) from the pet file summary alerts panel.
**Fix:**
1. **Pet file (edit mode):** in info-edit, add an editable list of allergies + chronic conditions
   (row add/remove, each a text line). On save, pass them through `SaveInfoAsync` → `SavePetInfoAsync`
   (the command already carries both fields — today the VM passes `_pet.Allergies/ChronicConditions`
   unchanged; wire the edited lists instead). Add observable collections `EditAllergies`,
   `EditChronic` + add/remove commands; render as repeatable rows in the info-tab edit UI (axaml).
2. **Summary alerts panel:** add a small "+ هشدار" affordance opening the same row editor (or jump to
   info-edit). Keep `BuildAlerts` as the read renderer.
3. **At registration:** extend `PetFormViewModel` (`BuildRequest`) + `NewPetRequest` with optional
   `IReadOnlyList<string> Allergies, ChronicConditions`; thread through `CreatePetCommand`
   (`src/Animora.Application/UseCases/Patients/CreatePetCommand.cs` — add the two fields and set them on
   the new `Pet`). Add a row-wise alerts section to `PetFormView.axaml`.
4. **During visit:** in `VisitEncounterViewModel` (`Pages/Visits/VisitEncounterViewModel.cs`, 800 ln) —
   find the step that edits patient clinical context; add a row-wise alert add UI that persists to the
   pet (call a new seam method or reuse `SavePetInfoAsync`). CONFIRM the encounter already loads the pet;
   if not, fetch via `IPatientsQueryService.GetPetAsync`.
**Done when:** you can add/remove allergy + chronic-condition rows at pet creation, in the pet file, and in
the visit flow, and they persist + show in the summary alerts panel. **Push** (likely split into
2 pushes: contracts/app/persistence change, then UI).

### ☑ Task 4 — KPI cells get data-entry; integrate with the rest
**Current KPI strip (axaml L301-355):** 4 cells — وزن فعلی, سن, آخرین ویزیت, واکسن بعدی. Plus the vitals
panel (task 1). These are read-only summaries.
**Goal:** "every KPI, when opening the file, has an option to enter its related data, fully integrated."
**Interpretation (baked in):** each KPI/vital should have an inline **entry affordance** that writes to the
real source and refreshes:
- وزن فعلی → opens the "ثبت وزن" flow (task 7 calendar) / jumps to weight tab add-form.
- واکسن بعدی → opens add-vaccination (task 9).
- علائم حیاتی (vitals) → entering vitals belongs to a visit; provide a "ثبت در ویزیت" affordance that
  triggers task 2 (start visit) OR a quick-vitals entry that creates/updates the latest visit's vitals.
  **Decision:** wire vitals entry to task 2 (start visit) to avoid a parallel vitals write path; a
  standalone vitals command is out-of-scope unless trivial.
- سن / آخرین ویزیت → derived, no direct entry (age edits via birth date in info-edit; last visit via task 2).
**Fix:** add small "+"/"ثبت" buttons on the relevant KPI/vital cells, each invoking the correct existing
command (AddWeight / AddVax / AddVisit / EditInfo→birthdate). Keep it visually consistent (brand mini
buttons). **Done when:** each actionable KPI has a working entry affordance tied to the real write path.
**Push.**

### ☑ Task 5 — "واکسن بعدی" (next vaccine) display works correctly
**Current:** `Apply` (VM L314-333) computes next-vax from `pet.Vaccinations.OrderByDescending(NextDate)`
— but `Vaccinations` is **always `[]`** (ToDetail hardcode). So next-vax chip/KPI always "ثبت نشده".
**Fix:** depends on task 9 wiring the real vaccination source. After task 9, verify:
- Hero chip `NextVaxChipText`/`HasNextVaxChip` (axaml L174-183).
- KPI cell `NextVaxDateText`/`NextVaxLabelText` (axaml L343-354).
- Overdue styling (`NextVaxOverdue`).
- Pick the **earliest upcoming** due dose (current code uses `OrderByDescending(NextDate).First()` =
  the LATEST next-date, which is wrong for "next" — change to the nearest future `NextDate`, and for
  overdue pick the most overdue). Fix `BuildAlerts` overdue logic too (L418-428).
**Done when:** next-vaccine reflects the real nearest due dose with correct overdue state. **Push.**

### ☑ Task 6 — Info tab: ALL attributes editable (everything captured at registration)
**Current:** read view shows 13 fields (axaml L535-556 / VM BuildInfoItems L535-556); edit exposes only 7
(Name, Breed, Color, Birth, Habitat, Behavior, Diet — VM L560-567). Missing editable: Gender, Species,
BloodType, Neutered(+date), Insurance, Microchip (code/date/vendor/location).
**Fix:**
1. Decide the authoritative set = every field captured at registration + shown in read view. The write
   command `UpdatePetInfoCommand` currently accepts: Name, Breed, Color, Gender, BirthDate, Neutered,
   AllergyNotes, ChronicNotes. **Missing from the command:** Species, BloodType, Insurance, Habitat,
   Behavior, Diet, Microchip fields → EXTEND `UpdatePetInfoCommand` + its handler + the `Pet` domain
   setters + SQLite mapping to persist these. (Open `UpdatePetInfoCommand.cs`; check `Pet` aggregate in
   `src/Animora.Domain/Patients/Pet.cs` for which fields even exist — Habitat/Behavior/Diet/BloodType/
   Insurance/Microchip vendor+location may NOT exist on the domain yet → add them or scope to existing).
   **⚠️ Reality check first:** `ToDetail` hardcodes Habitat/Behavior/Diet="" and BloodType/Insurance/
   NeuteredDate=null, and `PetDetailReadModel` has **no** such columns → these are NOT persisted anywhere
   today. So "edit all" requires adding domain columns + migration for the truly-missing ones. SCOPE
   realistically: add editable + persisted support for the fields that already exist on `Pet`
   (Gender, Species if present, Neutered+date, Microchip code/date) and add the genuinely-absent ones
   (Habitat/Behavior/Diet/BloodType/Insurance/Microchip vendor+location) as new nullable `Pet` columns
   with a Sqlite migration if the user's intent needs them. Confirm column set by reading `Pet.cs` once.
2. VM: expand `FillInfoBuffers` + edit buffers + `BuildInfoItems(editable:true)` to cover all fields;
   gender/species as dropdowns, neutered as toggle, dates via calendar (task 7 control).
3. axaml info-edit: render inputs for all; keep the 2-col grid, RTL, brand styling.
4. `SaveInfoAsync`: map all buffers into the extended `PetDetail`/command; `_changes.Notify`.
**Done when:** every registration attribute is editable in the info tab and round-trips through the DB.
**Push** (likely 2 pushes: domain/persistence/migration, then UI).

### ☑ Task 7 — "ثبت وزن جدید": date via calendar panel, not manual typing
**Current:** weight tab add-form (axaml L937-966) has a `TextBox` for `NewWeightDate` (typed Jalali). VM
`AddWeightAsync` (L637-666) parses it with `TryParseJalali`.
**Fix:** replace the date `TextBox` with the project's **Jalali calendar picker control** (the same one
other date inputs use — FIND it: grep `Calendar`/`JalaliDate`/`DatePicker` in `Controls/` and reuse the
one used for birth date in the add-pet wizard / vitals step). Bind the picked date to `NewWeightDate` (or
a new `DateTime? NewWeightDateValue`). Keep weight value TextBox. Default to today. Validate ≤ today.
**Done when:** picking a weight date opens the standard calendar panel like every other date field; no raw
typing. **Push.**

### ☑ Task 8 — Weight charts fully integrated (manual add + from-visit + consistent)
**Current:** `BuildWeight` (VM L359-394) reads `pet.Weights` → `WeightEntry` from `WeightEntry` table.
Visits also carry a `WeightKg` vital but those are NOT appended to the weight history today.
**Goal:** the weight chart (summary mini L382 + weight tab L919) must reflect ALL weight sources —
manual `WeightEntry` rows AND weights recorded during a visit — consistently.
**Fix:**
1. Confirm whether completing a visit with a weight vital already inserts a `WeightEntry` row. Open the
   encounter-complete path (`VisitEncounterViewModel` / `IVisitEncounterService` impl in
   `src/Animora.Client.Services/Visits/`). If it does NOT, add: on visit completion with a weight vital,
   also `AddWeightEntryCommand` (so the single source `WeightEntry` stays canonical). This keeps the chart
   source-unified (chart stays reading `pet.Weights`).
2. Verify chart ordering (ascending by date) + delta coloring (`BuildWeight` already does). Ensure manual
   adds (task 7) and visit-sourced weights both appear and chart redraws (it does via `Apply`).
3. `WeightChart` control: ensure it handles 0/1/many points without breaking.
**Done when:** a weight added manually and a weight taken in a visit both show in the chart + history
table, consistently. **Push.**

### ☑ Task 9 — Vaccination history: real history + manual add, integrated with Vaccination module
**Current:** `Vaccinations` always `[]` (ToDetail hardcode). Vax tab (axaml L1020-1081) + `AddVax`
(VM L671) stub. Source exists: `VaccinationRecord` table; query `GetOwnerClinicalActivityAsync`
already joins it; there's `MarkVaccinationDoneCommand` + a `VaccinationHistoryQuery`
(`UseCases/Vaccination/VaccinationQueries.cs`). Vaccination registration commands:
`UseCases/Vaccination/VaccinationRegistration.cs` / `VaccinationCommands.cs`.
**Fix:**
1. **Read path:** add a per-pet vaccination projection to `PetDetailReadModel` (new
   `IReadOnlyList<PetVaccinationReadModel>`), fill it in `PatientDirectoryQueries.GetPetAsync` by querying
   `VaccinationRecord` where `PetId==petId` (fields: VaccineName, AdministeredUtc→Date, NextDueUtc→NextDate,
   DoseNumber/Lot/Site/Vet if present; check the `VaccinationRecord` entity for exact columns). Map to
   contract `VaccinationSummaryRef(Name,Date,NextDate,Lot,Vet,Site)` in `ToDetail` (stop hardcoding `[]`).
   (Lot/Vet/Site may be absent on the record → fill "—" or resolve Vet via administering user.)
2. **Manual add (`AddVax`):** replace stub with a modal to register a vaccination for this pet. Reuse the
   Vaccination module's registration command (open `VaccinationRegistration.cs` to find
   `RegisterVaccination`-style command: pet id, vaccine code/name, dose, administered date (calendar!),
   next-due date, total doses). Add a seam method on a vaccination service OR inject the dispatcher-backed
   vaccination service the Vaccination page uses. Persist + `_changes.Notify(Patients, Vaccination,
   Dashboard)` + refresh pet.
3. Integrate with the Vaccination page so a dose added here appears there and vice-versa (same table →
   automatic; just ensure change-notify domains match).
**Done when:** vax tab shows the pet's real dose history and you can add a dose manually (date via
calendar), fully consistent with the Vaccination module. This also unblocks tasks 5. **Push** (split:
read path, then add path).

### ☑ Task 10a — Documents READ path: labs + radiology from visits
**Status: DONE + pushed.** Edits (both verified in-tree):
- `PatientDirectoryQueries.cs`: `using System.Globalization;`; `GetPetAsync` now calls new
  `GetPetParaclinicAsync(petId, ct)` which loads the pet's non-cancelled visits (bounded `PetVisitTake`),
  flattens each visit's JSON `LabResults` → `LabSummaryReadModel` (Description as title, items formatted
  `«TypeName: Value Unit»`) and `Radiology` → `RadiologySummaryReadModel` (BodyPart/Result/AttachmentCount),
  newest first; returns them via `Labs: labs, Radiology: radiology`. Jalali date via local
  `JalaliDay(DateTimeOffset)` (`PersianCalendar`, `Asia/Tehran` tz with custom-offset fallback).
- `PatientsMapping.ToDetail`: `Labs: (m.Labs ?? []).Select(ToLabSummary)`, `Radiology:` ctor-arg now
  `(m.Radiology ?? []).Select(ToRadiologySummary)` (was dropped). `Documents` still `[]` → T10b.
**Done:** anything recorded in a visit's paraclinic step now shows in the docs tab. **Pushed.**

### ☑ Task 10b — Documents tab: stored files list + manual add/upload + download
**Status: DONE + pushed.** Implemented option **A** exactly:
- Read model: `PetDocumentReadModel(Id,Title,Kind,SizeBytes,UploadedUtc,ContentHashSha256)` +
  `PetDetailReadModel.Documents`; filled in `PatientDirectoryQueries.GetPetAsync` via new
  `GetPetDocumentsAsync` (`DocumentReferences` where `PetId==petId`, newest first, `PetDocumentTake=100`; soft-delete
  filter automatic — no schema change).
- Contract: `DocumentRef(Name,Size,Date,Id=null,Hash=null)`; mapped by `PatientsMapping.ToDocumentRef` (was `[]`).
- Seams: `IPatientsQueryService.AddPetDocumentAsync` / `OpenPetDocumentAsync`; impl in `PatientsService` buffers the
  stream (exact size), `ILocalFileStore.PutAsync` → hash, then dispatches the journaled `AddPetDocumentCommand`.
- Command: `AddPetDocumentCommand` (+validator, +`CommandTypes.PatientsPetDocumentAdd`, +`PatientsRegistration`,
  +`IDocumentReferenceRepository` port + Sqlite/Postgres repos + both DI regs) calls `DocumentReference.Attach`.
- VM: `DocRowViewModel(Name,Meta,Id,Hash)`; real `UploadDocAsync` (pick → size cap `MaxAttachmentBytes` → upload →
  notify → reload) and `DownloadDocAsync` (hash → temp file → `IFileLauncher`, guarded by `IsViewable`);
  `HumanSize` byte→ Persian label. XAML bindings already existed.
**VERIFIED infra already exists — reuse it, do NOT invent a store:**
- **Metadata table + entity already exist:** `Animora.Domain.Patients.DocumentReference` (fields `OwnerId?`,
  `PetId?`, `VisitId?`, `Title`, `Kind`=enum `DocumentKind{Lab,Imaging,Prescription,Consent,Invoice,Other}`,
  `FileId`, `SizeBytes`, `ContentHashSha256`, `UploadedUtc`, `UploadedByUserId`) with factory
  `DocumentReference.Attach(tenantId, title, kind, fileId, sizeBytes, contentHashSha256, uploadedByUserId,
  clock, ownerId?, petId?, visitId?)`. Mapped in SQLite (`Configurations/Patients/DocumentReferenceConfiguration.cs`,
  table `DocumentReferences`) since migration 001 — **no migration needed**. Replicated
  (`SyncContractCatalog` lists `DocumentReference`; wire `DocumentReferenceSyncContract`).
- **Bytes store already exists:** `ILocalFileStore` (`PutAsync(stream)→sha256`, `OpenAsync(hash)`, `DeleteAsync`),
  registered `TryAddSingleton<ILocalFileStore, LocalFileStore>` in `LocalInfrastructureServiceCollectionExtensions.cs`
  (App wires local infra). The visit paraclinic path already uses it: `VisitEncounterService.StoreAttachmentAsync/
  OpenAttachmentAsync` (`src/Animora.Client.Services/Visits/VisitEncounterService.cs` L99-118) — **reuse this
  pattern** (open a DI scope → `ILocalFileStore.PutAsync`).
- **UI seams already exist:** `IDialogService.PickFilesAsync(title, patterns, allowMultiple)` → `PickedFile(Name,
  Size, OpenReadAsync)` (`DialogService.cs` L94-122); `IFileLauncher.Open(path)` (App L376). View already has
  `UploadDocCommand`/`DownloadDocCommand` bindings (`PetFileView.axaml` docs tab ~L1489-1564) and rows
  `DocRowViewModel(Name, Meta)`.

**NOTE — `DocumentRef` contract (`PatientDocuments`) is `(string Name, string Size, DateTime Date)` — NO id/hash.**
So download-by-row cannot resolve the payload from the contract today. Two clean options (pick **A**, it is
self-contained and matches the existing seed path):
- **A (chosen): extend the doc contract with a resolvable handle.** Add `string? Id` (and keep Name/Size/Date)
  to `DocumentRef`; thread `DocumentReference.Id` (+ `ContentHashSha256`) through the read model →
  `ToDetail` → row VM → `DownloadDoc`. Add a seam method to fetch the payload by ref id. Requires read-model
  field + mapper + VM row change. (No schema change; entity already stores hash.)
- B: keep contract, add a `GetPetDocumentsAsync(petId)` seam that returns rows already hydrated with the
  openable handle — same effect, one seam call, no read-model change. Use if A causes churn in mappers.

**Implementation steps (A):**
1. **Read model:** add `IReadOnlyList<PetDocumentReadModel>? Documents = null` to `PetDetailReadModel`
   (`PatientReadModels.cs`); define `PetDocumentReadModel(string Id, string Title, int Kind, long SizeBytes,
   DateTimeOffset UploadedUtc, string ContentHashSha256)`. Fill in `PatientDirectoryQueries.GetPetAsync`
   via `db.Set<DocumentReference>().Where(d => d.PetId == petId).OrderByDescending(d => d.UploadedUtc)`
   (soft-delete filter is automatic) — bound with a `const int PetDocumentTake` like the others.
2. **Contract:** extend `DocumentRef` → `record DocumentRef(string Name, string Size, DateTime Date, string? Id = null,
   string? Hash = null)` (append optional params so existing positional callers still compile). Map in
   `PatientsMapping.ToDetail`: `Documents: (m.Documents ?? []).Select(ToDocumentRef)`.
3. **Payload seam:** add `Task<Stream?> OpenDocumentAsync(string documentId)` to `IPatientsQueryService`
   (+ impl in `PatientsService`): look up the `DocumentReference` row by id (new tiny query, e.g.
   `PetDocumentContentQuery` → returns `{ContentHashSha256}`) then `ILocalFileStore.OpenAsync(hash)`. Put the
   `ILocalFileStore` usage in `PatientsService` (Client.Services) via `scopes.CreateAsyncScope()` — mirror
   `VisitEncounterService` (the Application/Contracts layers must not reference the store).
4. **Upload seam:** add `Task<PetDetail> AddPetDocumentAsync(string petId, DocumentKind kind, string title,
   string fileName, Stream content, string contentType)` to `IPatientsQueryService`. Impl: `ILocalFileStore.PutAsync`
   → `IdGenerator`/`CommandContext` for ids/size/hash → dispatch a **new journaled command**
   `AddPetDocumentCommand` (Application/UseCases/Patients) that calls `DocumentReference.Attach(...)`, adds it
   via a new `IDocumentReferenceRepository` (port + Sqlite repo + DI reg, mirror `IWeightEntryRepository`).
   Add `CommandTypes.PatientsPetDocumentAdd = "patients.pet.document.add"` + append to `All`.
   Register in `PatientsRegistration` (`.AddCommand<…>()` + validator). Return `(await GetPetAsync(petId))!`.
   **Wire (sync/journaled) so a peer sees it; bytes are local-only (`ILocalFileStore`), consistent with the
   visit attachment design.**
5. **VM:** `PetFileViewModel` — inject nothing new except the seams above (already has `IPatientsQueryService`,
   `IDialogService`, `IFileLauncher`, `IDataChangeNotifier`, `IToastService`). Replace `UploadDoc` stub with
   `async Task UploadDocAsync()`: `PickFilesAsync("انتخاب مدرک", ParaclinicPresentation.AttachmentPatterns)`
   → for each, `OpenReadAsync()` → `AddPetDocumentAsync(...)` → `_changes.Notify(DataDomain.Patients, …)` →
   reload pet. Replace `DownloadDoc` stub with real open: temp-file copy from `OpenDocumentAsync(hash/id)` then
   `_files.Open(path)` (mirror `VisitEncounterViewModel.OpenAttachmentAsync` L449-486; guard
   `ParaclinicPresentation.IsViewable`). Extend `DocRowViewModel` with `Id`/`Hash` (+ a nicer size text) and
   pass them from `BuildDocuments`. Add `IFileLauncher` to the VM ctor (DI auto-resolves; Registered at App L376).
6. **VM built docs:** `BuildDocuments` — `Documents.ReplaceAll(pet.Documents.Select(d => new DocRowViewModel(
   d.Name, $"{HumanSize(d.Size)} · {JalaliDate.FromDateTime(d.Date).ToString("short")}", d.Id, d.Hash)))`.
   Emit a Persian size ("۱۲ KB"/"۱٫۲ MB") — small local helper; do NOT depend on `d.Size` being a label.
7. **Reuse constants:** file patterns/type policy live in `ParaclinicPresentation` (Desktop.Pages.Visits) —
   reuse `AttachmentPatterns`, `ContentTypeFor`, `IsViewable`, `SafeFileName`, `MaxAttachmentBytes`. Enforce
   `MaxAttachmentBytes` before upload.
**Guarded-command + toast:** wrap awaited seam calls; `InvalidOperationException` →
   `PatientsPresentation.CommandFailureMessage(ex, fallback)` → `_toast.Show(…, Tone.Danger)`.
**Done when:** a file uploaded here persists as a `DocumentReference` (+ bytes in the local store), shows in
the docs list, opens via the OS viewer, and the read path surfaces it after reload. Lab/radiology rows
(uploaded in a visit) already show via T10a. **Push** (scope: `--scope core` build to catch Contracts/App/Persistence;
then `--scope desktop`).

### ☐ Task 11 — Build/verify via GitHub Actions (local build FORBIDDEN)
- **Run all builds/tests ONLY on GitHub Actions:** `scripts/ci/gh-ci.sh` (writes `ci.params`, pushes to a
  scratch `ci/**` branch, workflow `animora-ci.yml` runs it). Desktop Avalonia XAML errors only surface on a
  real build, so this IS the check.
- After T10b: `scripts/ci/gh-ci.sh --scope core --action build-only` (Contracts/Application/Persistence/Client.Services),
  then `scripts/ci/gh-ci.sh --scope desktop --action build-only` (Avalonia/XAML + VM), then
  `scripts/ci/gh-ci.sh --scope all --action build-and-test --filter Patients` for the patient tests.
  Light + fast where possible; skip the PostgreSQL lane unless a test needs it (`--postgres false`).
- Resolve ALL new errors + warnings the run reports; re-run until green. **No full-solution local test.**
- `scripts/git/push.sh "fix(patients): T11 …"` for each fix round.
**Done when:** CI green for the touched scopes. **Push.**

### ☐ Task 12 (FINAL) — Delete this plan file
- `rm Plan/PET-PROFILE-TASKS.md` (leave the `Plan/.gitkeep`). Touch nothing else, no roadmap edits.
- `scripts/git/push.sh "chore(plan): T12 remove completed pet-profile plan"`. **Push.**

---

## Push cheatsheet (LOCKED)
```
scripts/git/push.sh "feat(patients): T<N> <task summary>"
```
If exit code 3 → call `setup_github_environment` tool once, re-run. One push per completed task minimum;
extra push for any large mid-task fix. **Task id in the subject is REQUIRED.** No PR (branches identical;
work lands directly on `main`). **No local build/test** — CI only (`scripts/ci/gh-ci.sh`).

## Remaining-work map (post T1–T10a)
- **DONE + pushed:** T1–T9 (see git log), T10a read path (`PatientDirectoryQueries` + `PatientsMapping`).
- **T10b (only real code left):** docs list + upload/download. Files:
  `PatientReadModels.cs` (add `PetDocumentReadModel` + `Documents` field) ·
  `PatientDirectoryQueries.cs` (fill docs) · `PatientDetailModels.cs` (`DocumentRef` +Id/+Hash) ·
  `PatientsMapping.cs` (map docs) · `IPatientsQueryService.cs` + `PatientsService.cs` (upload/download seams) ·
  new `AddPetDocumentCommand` + `IDocumentReferenceRepository` (port + Sqlite repo + DI) ·
  `CommandTypes.cs` (add `patients.pet.document.add`) · `PatientsRegistration.cs` ·
  `PetFileViewModel.cs` (upload/download + row Id/Hash) · `PetFileView.axaml` (bindings already exist).
  Reuse: `DocumentReference.Attach`, `ILocalFileStore`, `IDialogService.PickFilesAsync`, `IFileLauncher`,
  `ParaclinicPresentation` (patterns/type/size caps). **No migration** (table 001).
- **T11:** CI-only build/test (`gh-ci.sh --scope core` → `--scope desktop` → `--scope all --filter Patients`).
- **T12:** `rm Plan/PET-PROFILE-TASKS.md`, push.
