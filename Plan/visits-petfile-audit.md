# Plan — Visits & Pet-file audit remediation (2026-10-03)

> **Self-contained execution plan.** A fresh model must implement every task below **without
> re-auditing the repo**. All file paths, exact current code, root-cause analysis, chosen fix,
> and acceptance criteria are embedded here. Read §0 once, then execute tasks top-to-bottom.
>
> **LOCKED RULE — push after every task:** after completing **each** task, run
> `scripts/git/push.sh "type(scope): message"`. Not pushing = project death. If it exits code `3`
> (no credentials) call the `setup_github_environment` tool once, then re-run. Exit `2` = rebase
> conflict (repo left clean) — resolve then re-run.
>
> **Source of this plan:** `docs/VISITS_PETFILE_AUDIT_2026-10-03.md` (21 findings: 9 P1, 9 P2, 3 P3).
> The final task deletes that audit file. Do **not** touch any other docs or roadmap files.

---

## 0. Context (read once)

- **App:** Animora — vet clinic desktop app, .NET 10, Avalonia + MVVM, local encrypted SQLite.
- **Scope:** the **Visits** flow (triage gate → encounter wizard → done/hand-off) and the **Pet file**
  (patient record), plus their Contracts, Client.Services, Application use cases and SQLite queries.
- **Data-flow rule (never break):** View → ViewModel → `Animora.Contracts` interface → real
  `Animora.Client.Services` impl → `Animora.Application` dispatch (permission/capability/validation/
  journal) → Domain + SQLite. ViewModels never touch EF/SQL. Seed data lives in Persistence seeders.
- **No full build / no full test run** per user rule. Make focused edits; rely on the embedded
  analysis. If a *heavy* fix or large code block is written mid-task, push it with a clear message.

### THE CRASH MECHANISM (shared root cause of V01–V05) — understand this first

The Client.Services seam converts **every** failed `Result` into a thrown exception:
`if (result.IsFailure) throw new InvalidOperationException(result.Error.Code);`
- In `VisitsTriageService.cs` (TSVC): lines **44, 46, 56, 58**.
- In `VisitEncounterService.cs` (ESVC): lines **62, 67, 85, 87, 96, 98, 107, 109**.

A ViewModel awaiting these inside an **unguarded `async` RelayCommand** lets the exception escape
the async state machine onto the UI dispatcher → `Dispatcher.UIThread.UnhandledException` →
`GlobalExceptionHandler.HandleUiException` (`src/Animora.Infrastructure.Local/Startup/GlobalExceptionHandler.cs`
lines 46–53) → persists a crash report + raises `CRASH_RECOVERY_REQUIRED` → the Persian
"program recovered/stopped after an unexpected stop" the operator sees.

**The intended pattern already exists in Patients** and MUST be reused:
`src/Animora.Desktop/Pages/Patients/PatientsPresentation.cs`
- `AccessLostMessage` (line 221): `"دسترسی لازم را ندارید یا نشست منقضی شده؛ دوباره وارد شوید"`
- `IsAccessFailure(string code)` (line 225): returns true for codes
  `PERMISSION_DENIED, PERMISSION_UNKNOWN, SESSION_EXPIRED, CAPABILITY_NOT_GRANTED,
  CAPABILITY_SNAPSHOT_MISSING, CAPABILITY_SNAPSHOT_TAMPERED, MEMBERSHIP_NOT_ACTIVE, USER_DISABLED,
  USER_LOCKED_OUT, TENANT_MISMATCH, TENANT_SUSPENDED, DEVICE_REVOKED`.
- `CommandFailureMessage(InvalidOperationException ex, string fallback)` (line 236): returns
  `AccessLostMessage` if `IsAccessFailure(ex.Message)`, else `fallback`.
- `PatientsPageViewModel.cs` line 450 shows the catch pattern:
  `catch (InvalidOperationException ex) when (IsAccessFailure(ex.Message)) { _toast.Show(AccessLostMessage, Tone.Danger); }`
  then a generic `catch (Exception) { _toast.Show("…", Tone.Danger); }`.

### Key file map (confirmed to exist — do NOT re-search)
| Alias | Path |
|---|---|
| EVM | `src/Animora.Desktop/Pages/Visits/VisitEncounterViewModel.cs` |
| TVM | `src/Animora.Desktop/Pages/Visits/VisitsTriagePageViewModel.cs` |
| RXVM | `src/Animora.Desktop/Pages/Visits/RxLineViewModel.cs` |
| PLANV | `src/Animora.Desktop/Pages/Visits/PlanStepView.axaml` |
| DONEV | `src/Animora.Desktop/Pages/Visits/DoneStepView.axaml` |
| TSVC | `src/Animora.Client.Services/Visits/VisitsTriageService.cs` |
| ESVC | `src/Animora.Client.Services/Visits/VisitEncounterService.cs` |
| VMAP | `src/Animora.Client.Services/Visits/VisitsMapping.cs` |
| PMAP | `src/Animora.Client.Services/Patients/PatientsMapping.cs` |
| PFVM | `src/Animora.Desktop/Pages/Patients/PetFileViewModel.cs` |
| SQLT | `src/Animora.Persistence.Sqlite/Queries/TriageQueries.cs` |
| SQLP | `src/Animora.Persistence.Sqlite/Queries/PatientDirectoryQueries.cs` |
| ENC | `src/Animora.Application/UseCases/Visits/EncounterCommands.cs` |
| START | `src/Animora.Application/UseCases/Visits/StartVisitCommands.cs` |
| PRES | `src/Animora.Desktop/Pages/Patients/PatientsPresentation.cs` |

### Facts verified during audit (use directly — no re-derivation needed)
- **EVM unguarded async commands:** `NextStepAsync` (324–335), `PreviousStepAsync` (337–347),
  `SaveDraftAsync` (361–367), `BackToGate` (369–374), `RemoveRxLineAsync` (376–395),
  `NextVisit` (397–402). `CompleteAsync` (417–433) is private, called by NextStep.
  `AddPickedLineAsync` (312–322) is **already** try/catch-guarded — good; keep as model.
- **TVM unguarded async commands:** `SelectDoctorAsync` (238–258), `StartFromTicketAsync` (326–337),
  `StartNewAsync` (339–363). All three await seam calls that throw on failure.
- `CompleteVisitCommandHandler` requires non-empty diagnosis: ENC line 217–218 returns
  `ErrorCodes.VisitIncomplete` when `string.IsNullOrWhiteSpace(visit.Diagnosis)`.
- EVM gating: `CanEditVisit => _capabilities.Can("visits.write")` (63); `CanFinishVisit() =>
  _capabilities.Can("visits.complete")` (64); `CanAdvance() => !IsLastStep || CanFinishVisit()` (65).
  `DiagnosisText` is `[ObservableProperty]` at EVM line 98. `IsLastStep => CurrentStep == Review` (168).
- START `StartNewVisitCommand` signature (line 127): `(string DoctorId, string PatientName,
  string PatientKind, string OwnerName, string OwnerPhone, string TypeId, bool IsUrgent, string Note)`
  — **PatientKind is already a parameter** but the handler ignores it: pet is created with
  `PetSpecies.Other` (START line 170). Walk-in phone required at START 159–160 (`WalkInPhoneRequired`).
  Existing pet matched by `pets.FindByOwnerAndNameAsync(owner.Id, c.PatientName, ct)` (167).
- TVM `SaveNewPatient` (300–324): only name+kind required (302); builds `VisitPatientRef` with
  `Id = "np-" + Guid[..10]` (309) and **hardcoded `IconKey: "dog"`** (314). `StartNewAsync` (348–358)
  builds `NewVisitRequest` passing `PickedPatient.Kind` as `PatientKind` and `PickedPatient.IconKey`.
- `HandoffToCashier` (EVM 404–415) is a **synchronous** `[RelayCommand] void` that calls
  `_navigation.NavigateTo<SalePageViewModel>(vm => vm.BuildFromEncounterCommand.Execute(_encounterId))`
  — fire-and-forget `.Execute()` of an async command. Toast text line 412 contains "(نمونه طراحی)".
- DONEV line 17: `"پروندهٔ بیمار به‌روزرسانی شد و فاکتور به صندوق ارسال شد. "`; line 18 Run
  `"(نمونه طراحی)"`; line 50 button bound to `HandoffToCashierCommand`.
- **Pet-file visits empty:** `GetPetAsync` (SQLP 185–200) builds `PetDetailReadModel` from pet
  profile + weights only (record has NO Visits field — `PatientReadModels.cs` line 54–58).
  `PatientsMapping.ToDetail(PetDetailReadModel)` (PMAP 38–47) hardcodes `Visits: []` (line 46).
  `PetFileViewModel.BuildVisits` (PFVM 442–454) reads `pet.Visits`; `LastVisitSinceText`/
  `LastVisitTypeText` derived at PFVM 286–288 from `pet.Visits` → always "—"/"بدون سابقه".
  Completion DOES write `MedicalHistoryEntry` kind `Visit` (ENC 223–224).
- **Pet/owner summaries placeholder:** `PetSummaryReadModel` built with `null, null` for
  `LastVisitUtc`/`LastVisitType` at SQLP line 45 (GetPetsAsync) and line 179 (GetOwnerAsync).
  `PMAP.ToSummary` (13–20) passes them through. The owner clinical-activity feed
  (`GetOwnerClinicalActivityAsync`, SQLP 131–161) **does** read `db.Set<Visit>()` → data exists.
- **V04 query shape:** `GetBookedForDoctorAsync` (SQLT 32–52) does a left join (`join row … into rows
  / from queue in rows.DefaultIfEmpty()`, SQLT 39–41) **followed by** inner joins on `pet`/`owner`
  (42–43) then projects (45–50), materialized at `ToListAsync` (51). This left-join-then-join shape
  can fail EF Core SQLite translation. Doctors with zero booked rows never hit the shape → "only
  certain doctors" crash.
- **V15 rail:** `GetDoctorRailAsync` (SQLT 16–30) returns `string.Empty` for specialty (line 23) and
  carries an in-progress flag `db.Set<Visit>().Any(v => … State == VisitState.InProgress)` (27).
  `VisitsMapping.ToContract(DoctorRailReadModel)` (VMAP ~8–9) hardcodes accent `#0d9488`,
  sets AppointmentCount from `row.BookedCount`, passes `0` trailing. In-progress flag unused.
- **V16:** `VisitsMapping.Initial(value) => value.Length == 0 ? "" : value[..1]` (VMAP line 35) —
  splits UTF-16 surrogate pairs.
- **V11 overflow:** `RxLine.LineTotal => UnitPrice * Quantity` (Contracts `VisitsEncounterModels.cs`);
  `RxLineViewModel.LineTotal => Line.UnitPrice * _quantity` (RXVM 51); `QuantityText` clamps only
  lower bound `Math.Max(1, parsed)` (RXVM 79–80), no upper bound. EVM `RecomputeSubtotal` (477–496)
  sums `long` unchecked. Backend uses `checked(...)` (ENC 243–244).
- **V12 vitals:** `ParseDouble` returns `0` on bad input (EVM 529–535); `BuildVitals` casts
  `(int)ParseDouble(...)` (EVM 471–475). `0` means "not recorded" in `Vitals`.
  `SaveVisitVitalsCommand` params are nullable (ESVC 25–27 casts `(decimal)` / passes ints).
- **V13 draft writes:** `SaveNoteAsync` enqueues into `DebounceBuffer` and returns
  `Task.CompletedTask` immediately (ESVC 35–39); actual dispatch runs later via `SendAsync(…, ct)`
  (ESVC 102–110) and throws on failure with no UI path. `CompleteAsync` flushes first (ESVC 75).
- **V17 triage reload flash:** TVM `LoadAsync` (173–206) sets `Doctors.Clear()` (186) **before**
  awaiting the query, then repopulates. `RefreshOnDataChange` fires `_ = ReloadPageAsync()` (97).
  Selection restored at 194–197. `_isLoading` guard prevents re-entry only.
- **V19:** `PatientsPageViewModel` reads `facetsTask.Result`/`pageTask.Result` after
  `await Task.WhenAll(...)` (lines ~432–442). Safe but non-idiomatic.
- **V20:** "(نمونه طراحی)" toasts on real flows: TVM `StartFromTicketAsync` (335),
  `StartNewAsync` (361); EVM `HandoffToCashier` (412).
- **V21:** TVM `SaveNewPatient` hardcodes `IconKey: "dog"` (314) regardless of kind. The synthetic
  id `"np-"+Guid[..10]` (309) is discarded by the backend (re-resolved by phone/name at START 161–172).

### Remediation order (dependency-aware — follow this task order)
1. V01 (shared guard helper) — safety net for V02–V05, removes both reported crashes.
2. V04, V02, V03 — eliminate the specific failing inputs.
3. V05, V14 — cashier hand-off awaitable + fix copy.
4. V06, V07 — populate pet-file visits + last-visit summaries (primary cross-section gap).
5. V08 — pet disambiguation/species.
6. V09–V21 — P2/P3 polish.

---

## Tasks

Status legend: `[ ]` pending · `[~]` in progress · `[x]` done.
After finishing a task, mark it `[x]`, then **push** (locked rule).

### Task 1 — V01 · Guard all Visits async commands (crash root cause)
- **Status:** `[x]` — DONE. Added `RunGuardedAsync(Func<Task>, string)` to EVM and TVM (reuses
  `PatientsPresentation.CommandFailureMessage`); wrapped EVM `NextStepAsync/PreviousStepAsync/
  SaveDraftAsync/BackToGate/RemoveRxLineAsync/NextVisit` and TVM `SelectDoctorAsync/StartFromTicketAsync/
  StartNewAsync` (commands now return `RunGuardedAsync(async () => {…}, fallback)`). `using
  Animora.Desktop.Pages.Patients;` added to both VMs.
- **Priority:** P1 · **Push message:** `fix(visits): guard async commands so backend failures toast instead of crashing (V01)`
- **Goal:** No awaited seam call in a Visits RelayCommand may let an `InvalidOperationException`
  escape to the dispatcher. Reuse the Patients pattern.
- **Do:**
  1. Add a shared guard helper. Preferred location: a small `protected async Task RunGuardedAsync(
     Func<Task> action, string fallbackMessage)` on a Visits VM base, OR add it to **both** EVM and
     TVM privately if no shared base is convenient (check if `PageViewModelBase` already has a toast —
     EVM/TVM both hold `IToastService _toast`). The helper body:
     ```csharp
     try { await action(); }
     catch (InvalidOperationException ex) {
         _toast.Show(PatientsPresentation.CommandFailureMessage(ex, fallbackMessage), Tone.Danger);
     }
     ```
     (Reuse `Animora.Desktop.Pages.Patients.PatientsPresentation.CommandFailureMessage` — it already
     maps access-failure codes to `AccessLostMessage` and everything else to the fallback. Add the
     `using` if needed. Do NOT duplicate the code list.)
  2. Wrap EVM commands: `NextStepAsync` (324), `PreviousStepAsync` (337), `SaveDraftAsync` (361),
     `BackToGate` (369), `RemoveRxLineAsync` (376), `NextVisit` (397). Each body becomes
     `await RunGuardedAsync(async () => { …existing body… }, "عملیات ناموفق بود؛ دوباره تلاش کنید");`.
  3. Wrap TVM commands: `SelectDoctorAsync` (238), `StartFromTicketAsync` (326), `StartNewAsync` (339)
     the same way.
  4. Leave `AddPickedLineAsync` (EVM 312) as-is (already guarded).
- **Acceptance:** a backend-rejected completion shows a `Tone.Danger` toast and keeps the wizard open;
  no crash report for a handled domain failure. Access-failure codes show `AccessLostMessage`.

### Task 2 — V04 · Fix `GetBookedForDoctorAsync` query translation
- **Status:** `[x]` — DONE. Removed the GroupJoin+`DefaultIfEmpty` left join; now two inner joins
  (pet/owner) + two correlated subqueries on `ReceptionQueueEntry` (`.Any(q => q.AppointmentId ==
  appointment.Id && q.Priority > 0)` and `.Where(...).Select(q => q.State.ToString()).FirstOrDefault()
  ?? "Waiting"`). Verified `ReceptionQueueEntry.AppointmentId` is `string?`, `State` is `QueueState`,
  `Priority` is int. Ctor arg order unchanged.
- **Priority:** P1 · **Push message:** `fix(visits): restructure booked-for-doctor query to avoid left-join-then-join (V04)`
- **Goal:** Selecting a doctor with ≥1 booked appointment must not throw an EF translation error.
- **Current (SQLT 32–52):** left join `ReceptionQueueEntry` via `.DefaultIfEmpty()` THEN inner joins
  `Pet`/`Owner` THEN projects `BookedAppointmentReadModel` — fragile shape for EF Core SQLite.
- **Do:** restructure to avoid left-join-then-join. Pattern: run the base appointment query with the
  two **inner** `pet`/`owner` joins first (ordered by `StartUtc, Id`), then resolve the optional queue
  row via a **correlated subquery** in the projection instead of a `DefaultIfEmpty` join. Replace the
  `queue != null && queue.Priority > 0` and `queue == null ? "Waiting" : queue.State.ToString()` with
  subquery lookups keyed by `appointment.Id`, e.g.:
  ```csharp
  var booked = from appointment in db.Set<Appointment>().AsNoTracking()
      where appointment.DoctorUserId == doctorId
          && appointment.StartUtc >= startUtc && appointment.StartUtc < endUtc
          && appointment.State != AppointmentState.Cancelled
      join pet in db.Set<Pet>().AsNoTracking() on appointment.PetId equals pet.Id
      join owner in db.Set<Owner>().AsNoTracking() on appointment.OwnerId equals owner.Id
      orderby appointment.StartUtc, appointment.Id
      select new BookedAppointmentReadModel("apt:" + appointment.Id,
          appointment.StartUtc.Hour * 60 + appointment.StartUtc.Minute,
          appointment.DurationMinutes, pet.Id, pet.Name, pet.Species.ToString(),
          owner.FullName, owner.PhonePrimary, appointment.AppointmentTypeCode,
          db.Set<ReceptionQueueEntry>().Any(q => q.AppointmentId == appointment.Id && q.Priority > 0),
          db.Set<ReceptionQueueEntry>().Where(q => q.AppointmentId == appointment.Id)
              .Select(q => q.State.ToString()).FirstOrDefault() ?? "Waiting");
  ```
  Keep the exact `BookedAppointmentReadModel` constructor arg order identical to current (verify
  against lines 45–50). If an existing persistence test project covers triage queries, add a test that
  selects a doctor with ≥1 booked appointment; otherwise skip (no full test run per rule).
- **Acceptance:** `GetBookedForDoctorAsync` returns rows for a doctor with appointments without throwing.
  (V01 already prevents the crash even if a translation edge remains.)

### Task 3 — V02 · Require diagnosis before completion
- **Status:** `[x]` — DONE. Last-step `CanAdvance` also requires non-empty diagnosis; `OnDiagnosisTextChanged` re-evaluates the command; new `IsDiagnosisMissing` drives an inline «تشخیص الزامی است» hint in PlanStepView.
- **Priority:** P1 · **Push message:** `fix(visits): gate visit completion on non-empty diagnosis (V02)`
- **Goal:** The final-step primary button is disabled until a diagnosis is entered; happy path never
  throws `VISIT_INCOMPLETE`.
- **Do (EVM):**
  1. Change `CanAdvance()` (line 65) so on the last step it also requires a diagnosis:
     `private bool CanAdvance() => !IsLastStep || (CanFinishVisit() && !string.IsNullOrWhiteSpace(DiagnosisText));`
  2. Add `partial void OnDiagnosisTextChanged(string value)` that calls
     `NextStepCommand.NotifyCanExecuteChanged();` (and `OnPropertyChanged(nameof(DiagnosisDisplay));`
     if not already notified). `NextStepCommand` already uses `CanExecute = nameof(CanAdvance)` (324).
  3. In PLANV (`PlanStepView.axaml`) add an inline hint "تشخیص الزامی است" shown when diagnosis is
     empty on the Plan step. Bind visibility to a new EVM bool e.g.
     `public bool IsDiagnosisMissing => IsPlanStep && string.IsNullOrWhiteSpace(DiagnosisText);`
     and notify it in `OnDiagnosisTextChanged` and `NotifyState`. Keep the TextBox at PLANV line 17.
  4. Keep backend rule authoritative (V01 still catches late failures).
- **Acceptance:** last-step button disabled with empty diagnosis; no `VISIT_INCOMPLETE` on happy path.

### Task 4 — V03 · Require phone for walk-in visits
- **Status:** `[x]` — DONE. `SaveNewPatient` now requires name, kind, owner name and phone (backend validator/`WalkInPhoneRequired` parity); `CanStartNew` additionally requires picked patient phone + owner name so the start button stays disabled for incomplete walk-ins.
- **Priority:** P1 · **Push message:** `fix(visits): require phone for walk-in new patient before starting (V03)`
- **Goal:** A walk-in without a phone is blocked with a field message, not a crash.
- **Backend contract:** START 159–160 returns `WalkInPhoneRequired` on empty `OwnerPhone`.
- **Do (TVM):** in `SaveNewPatient` (300–324) add phone to the required check at line 302:
  `if (string.IsNullOrWhiteSpace(NewPatientName) || string.IsNullOrWhiteSpace(NewPatientKind) ||
  string.IsNullOrWhiteSpace(NewPatientPhone)) { _toast.Show("نام، نژاد و شماره تماس حیوان الزامی است.", Tone.Danger); return; }`.
  Optionally also gate `StartNewCommand` via `CanStartNew` to require `PickedPatient.Phone` non-empty
  (CanStartNew is at TVM 133 — currently `PickedPatient is not null`; extend to also require phone).
  V01 still catches the backend failure as a toast.
- **Acceptance:** walk-in without phone blocked with a field message; no crash at "شروع ویزیت".

### Task 5 — V05 + V14 · Awaitable cashier hand-off & correct done copy
- **Status:** `[x]` — DONE. EVM `HandoffToCashierAsync` (guarded) awaits `NavigateToAsync<SalePageViewModel>(vm => vm.BuildFromEncounterAsync(id))`; new public awaitable `SalePageViewModel.BuildFromEncounterAsync` catches backend failures → danger toast on Sales page (the relay command delegates to it). DoneStepView copy reworded and the stub label + stale toast removed (binding name unchanged: `HandoffToCashierCommand`).
- **Priority:** P1 · **Push message:** `fix(visits): make cashier handoff awaitable and correct done-screen copy (V05,V14)`
- **Goal:** A missing/failed handoff shows a toast on the Sales page; the done screen never crashes;
  done copy matches the real handoff state.
- **Do:**
  1. (V05) Replace EVM `HandoffToCashier` (404–415) `.Execute()` fire-and-forget. Make it an
     `async Task` RelayCommand guarded by `RunGuardedAsync` (from Task 1), OR pass the encounter id into
     the navigation target so `SalePageViewModel` runs `BuildFromEncounter` inside its own guarded load
     (try/catch → toast). Never drive one async relay command via `.Execute()` from another command.
     Minimal safe change: convert to `private async Task HandoffToCashierAsync()` and either
     `await _navigation.NavigateToAsync<SalePageViewModel>(vm => vm.BuildFromEncounterAsync(_encounterId))`
     if such an awaitable path exists, or wrap the existing navigation so the `BuildFromEncounter` fault
     surfaces as a toast on the Sales page. Confirm `SalePageViewModel.BuildFromEncounter` is guarded;
     if not, guard it there too (it throws `INVOICE_NOT_FOUND` when no handoff owns the id).
  2. Update DONEV binding at line 50 to the new async command name if renamed.
  3. (V14) Reword DONEV line 17 from `"پروندهٔ بیمار به‌روزرسانی شد و فاکتور به صندوق ارسال شد. "` to
     reflect the handoff, e.g. `"پروندهٔ بیمار به‌روزرسانی شد و ویزیت آمادهٔ صدور فاکتور در صندوق شد. "`.
     Keep the `"(نمونه طراحی)"` Run (line 18) only if a stub genuinely remains — otherwise remove it
     here too (overlaps Task 13/V20).
- **Acceptance:** failed/missing handoff toasts on Sales page; done screen never crashes; copy matches
  the actual "ready for invoice" handoff model.

### Task 6 — V06 · Populate the pet-file Visits tab
- **Status:** `[x]` — DONE. New `PetVisitReadModel` + optional last ctor param `PetDetailReadModel.Visits`; `GetPetAsync` loads newest-50 non-cancelled visits (JSON lines → subtotal via checked arithmetic, doctor names resolved in one bounded query); `PatientsMapping.ToDetail` maps them to `VisitSummaryRef` (new optional `InvoiceId`, Persian reason label shared with owner feed); pet-file visit row shows weight/total/invoice. Mapping unit test added. The pet file is a modal sheet re-loaded on open, so no extra refresh subscription was needed.
- **Priority:** P1 · **Push message:** `feat(patients): populate pet-file visits tab from medical history (V06)`
- **Goal:** Completing a visit makes it (date, type, diagnosis, total, invoice link) appear in the pet
  file's Visits tab and updates the "last visit" summary.
- **Root cause chain:** `PetDetailReadModel` (`PatientReadModels.cs` 54–58) has NO Visits field →
  `GetPetAsync` (SQLP 185–200) never reads visits → `PMAP.ToDetail` (PMAP 38–47) hardcodes
  `Visits: []` (line 46) → `PetFileViewModel.BuildVisits` (PFVM 442–454) renders nothing. But
  completion DOES persist a `MedicalHistoryEntry` kind `Visit` (ENC 223–224) and a `Visit` aggregate.
- **Data available to project:** `Visit` entity fields (`src/Animora.Domain/Clinical/Visit.cs`):
  `PetId, OwnerId, DoctorUserId, State, Reason (VisitReason), ChiefComplaint, StartedUtc, CompletedUtc,
  Vitals (PetVitals?), Diagnosis, TreatmentPlan, DoctorNotes, InvoiceId`. `VisitState` enum exists.
  Contract target is `VisitSummaryRef` (`src/Animora.Contracts/Patients/PatientDetailModels.cs` 49–60):
  `(string Id, string Type, DateTime Date, string Vet, string Reason, string Exam, string Diagnosis,
  IReadOnlyList<string> Prescriptions, double WeightKg, double Temperature, long Cost)`.
- **Do:**
  1. Add `IReadOnlyList<PetVisitReadModel> Visits` to `PetDetailReadModel` (append as the LAST ctor
     param to minimize churn; create a `PetVisitReadModel` record mirroring the fields `VisitSummaryRef`
     needs: Id, VisitType/Reason, CompletedUtc (fallback StartedUtc), doctor display name, exam/notes,
     Diagnosis, prescription titles, weight, temperature, Cost/total, InvoiceId).
  2. In `GetPetAsync` (SQLP 185–200) add a projection reading completed visits for the pet. Prefer
     joining `db.Set<Visit>()` where `v.PetId == petId && v.State != VisitState.Cancelled` ordered by
     `CompletedUtc ?? StartedUtc` descending. Resolve doctor display name via the `User` set
     (see SQLT 21–23 join pattern). Compute total from `visit.ServiceLines`/`Prescriptions` if
     projectable, else carry `InvoiceId` and leave total 0 with a follow-up note. Keep it index-backed
     and bounded (Take a sane N, e.g. 50). Include `InvoiceId` so the file can show the فاکتور.
  3. In `PMAP.ToDetail` (PMAP 46) replace `Visits: []` with
     `m.Visits.Select(v => new VisitSummaryRef(...)).ToList()` mapping each field.
  4. `PetFileViewModel.BuildVisits` (PFVM 442–454) already reads `pet.Visits` — no change needed once
     the list is non-empty. `LastVisitSinceText`/`LastVisitTypeText` (PFVM 286–288) derive from it.
  5. If the pet file can stay open across a completion, subscribe its VM to `DataDomain.Patients`/
     `DataDomain.Visits` refresh (completion already notifies both — EVM 424–426).
- **Acceptance:** after completing a visit for a pet, that visit appears in the Visits tab with date,
  type, diagnosis, total and invoice link; the "last visit" summary updates.

### Task 7 — V07 · Resolve last-visit for pet/owner summaries
- **Status:** `[x]` — DONE. `GetPetsAsync` and `GetOwnerAsync` now project LastVisitUtc/LastVisitType via correlated top-1 subqueries over non-cancelled visits (same source as the owner activity feed); `ToSummary` maps the reason code to its Persian label; C03 placeholders removed.
- **Priority:** P1 · **Push message:** `fix(patients): resolve last-visit date/type for pet summaries (V07)`
- **Goal:** Pet summaries and the pet file agree on last-visit date/type with the owner activity feed.
- **Current:** `PetSummaryReadModel` built with `null, null` for `LastVisitUtc`/`LastVisitType` at
  SQLP line 45 (`GetPetsAsync`) and line 179 (`GetOwnerAsync`). The owner clinical-activity query
  (`GetOwnerClinicalActivityAsync`, SQLP 131–161) already reads `db.Set<Visit>()` → the source exists.
- **Do:** replace the two `null, null` placeholders with correlated subqueries resolving the latest
  visit for the pet, consistent with the owner-activity query:
  ```csharp
  db.Set<Visit>().Where(v => v.PetId == p.Id && v.State != VisitState.Cancelled)
      .OrderByDescending(v => v.StartedUtc).Select(v => (DateTimeOffset?)v.StartedUtc).FirstOrDefault(),
  db.Set<Visit>().Where(v => v.PetId == p.Id && v.State != VisitState.Cancelled)
      .OrderByDescending(v => v.StartedUtc).Select(v => v.Reason.ToString()).FirstOrDefault(),
  ```
  Apply at BOTH SQLP 45 and SQLP 179. Remove the C03 placeholder comments. `PMAP.ToSummary`
  (PMAP 13–20) already passes them through — the client comment at PMAP 17–19 can stay or be trimmed.
- **Acceptance:** pet cards/lists show real last-visit date/type matching the owner activity feed.

### Task 8 — V08 · Walk-in pet disambiguation & species
- **Status:** `[x]` — DONE. `StartNewVisitCommandHandler` now maps `PatientKind` → `PetSpecies` (new internal `PetSpeciesParser`, Persian/English, default Other) and reuses an existing pet only for an unambiguous single same-name match of the same species (new `IPetRepository.ListByOwnerAndNameAsync`, impl + test doubles); otherwise a distinct pet is registered. Chip/code selector remains a UI follow-up.
- **Priority:** P1 · **Push message:** `fix(visits): carry species and stop silent same-name pet reuse for walk-ins (V08)`
- **Goal:** A walk-in for a new same-named pet creates a distinct record with the correct species;
  history never merges across animals.
- **Current:** `StartNewVisitCommandHandler` (START 167–172) matches an existing pet by
  `FindByOwnerAndNameAsync(owner.Id, c.PatientName, ct)` and forces `PetSpecies.Other` (170) for new
  pets. `StartNewVisitCommand` already carries `PatientKind` (START 127) but ignores it.
- **Do:**
  1. Map `c.PatientKind` → `PetSpecies` in the handler and pass it to `Pet.Register(...)` instead of
     the hardcoded `PetSpecies.Other` (START 170). Add a small kind→species parser (reuse any existing
     species mapping in the codebase if present; otherwise map the known Persian/English kind strings,
     default `Other`). The TVM `NewPatientKind` free-text flows here as `PatientKind`.
  2. Disambiguation: when the same owner already has ≥1 pet with the same name, do NOT silently reuse.
     Minimal correct fix given the current UI: only reuse an existing pet when it is an unambiguous
     single match AND the chosen species matches; otherwise create a new distinct pet record. (A full
     chip/code selector is out of scope — document that as a follow-up in the commit body if skipped.)
- **Acceptance:** a walk-in for a new same-named pet creates a distinct record with the correct species.

### Task 9 — V09 · Explain disabled finish button (permission)
- **Status:** `[x]` — DONE. Permission gate moved from `CanAdvance` (now diagnosis-only on the last step) to a run-time check in `CompleteAsync` that toasts «اجازهٔ تکمیل ویزیت را ندارید.» before touching the seam.
- **Priority:** P1 · **Push message:** `fix(visits): explain missing completion permission instead of inert button (V09)`
- **Goal:** Lacking `visits.complete` produces a visible localized explanation, not a dead button.
- **Current:** `CanAdvance` (EVM 65) disables `NextStepCommand` on the last step unless
  `CanFinishVisit()`; no feedback. Same command serves "مرحلهٔ بعد" earlier, so disabled is ambiguous.
- **Do (EVM):** keep the command enabled for the permission dimension but short-circuit inside
  `NextStepAsync`/`CompleteAsync`: when `IsLastStep && !CanFinishVisit()`, show
  `_toast.Show("اجازهٔ تکمیل ویزیت را ندارید.", Tone.Danger);` and return before awaiting the seam.
  Note interaction with Task 3: the diagnosis gate can stay in `CanExecute`; the *permission* gate
  moves to a runtime toast so the user gets a reason. Adjust `CanAdvance` to
  `!IsLastStep || !string.IsNullOrWhiteSpace(DiagnosisText)` (drop the permission check from CanExecute),
  and enforce permission via the toast short-circuit. Re-verify Task 3 acceptance still holds.
- **Acceptance:** a user without `visits.complete` sees a localized explanation at the final step.

### Task 10 — V10 · Catalog-picker add: feedback + consistent reset
- **Status:** `[x]` — DONE. `AddPickedLine` toasts «اجازهٔ ویرایش ویزیت را ندارید.» when denied; both picker change hooks reset the selection in `finally` so it is cleared regardless of outcome (the async add's failure is already toasted by `AddPickedLineAsync`).
- **Priority:** P2 · **Push message:** `fix(visits): toast on denied line add and reset picker in finally (V10)`
- **Goal:** Denied/failed adds give feedback and leave the picker and line list consistent.
- **Current:** `AddPickedLine` (EVM 307–310) runs only `if (CanEditVisit)` → read-only users get no
  feedback. Picker reset happens only in the change hooks (EVM 282–298, set to null), not after a
  failed add → selection/line list can diverge on error.
- **Do (EVM):**
  1. In `AddPickedLine` (307), when `!CanEditVisit`, toast a permission message instead of silently
     doing nothing: `_toast.Show("اجازهٔ ویرایش ویزیت را ندارید.", Tone.Danger);`.
  2. Ensure the picker selection is reset in a `finally` regardless of outcome. Currently
     `SelectedServiceToAdd = null` / `SelectedItemToAdd = null` happen in the `OnSelected…Changed`
     hooks right after calling `AddPickedLine`. Move/guarantee the reset so it always runs even when
     `AddPickedLineAsync` (312–322) catches an error.
- **Acceptance:** denied/failed adds give feedback; picker and line list stay consistent.

### Task 11 — V11 · Clamp quantity & checked UI subtotal
- **Status:** `[x]` — DONE. `RxLineViewModel.QuantityText` clamps to 1..9999 (`MaxQuantity`, oversized digit strings → max); `LineTotal` uses checked arithmetic (saturating); EVM `RecomputeSubtotal` sums/taxes inside `checked` and on overflow toasts + keeps the previous subtotal.
- **Priority:** P2 · **Push message:** `fix(visits): clamp quantity and compute checked subtotal to prevent overflow (V11)`
- **Goal:** Extreme quantities are clamped/validated; UI subtotal matches backend and never overflows.
- **Current:** `RxLineViewModel.QuantityText` setter clamps only lower bound `Math.Max(1, parsed)`
  (RXVM 79–80); `LineTotal => Line.UnitPrice * _quantity` (RXVM 51) unchecked. EVM `RecomputeSubtotal`
  (477–496) sums `long` unchecked. Backend uses `checked(...)` (ENC 243–244) → would throw on overflow.
- **Do:**
  1. In RXVM `QuantityText` setter add an upper clamp, e.g. `var clamped = Math.Clamp(parsed, 1, 9999);`
     (pick a sane max consistent with business; 9999 is safe for `long` arithmetic).
  2. In EVM `RecomputeSubtotal` (477–496) compute with `checked` and guard overflow: wrap the sum in
     `try { checked { … } } catch (OverflowException) { toast + skip }` or accumulate into `decimal`
     then validate before casting. Keep UI and backend arithmetic consistent (both `checked`).
- **Acceptance:** extreme quantities clamped; UI subtotal matches backend; no overflow.

### Task 12 — V12 · Nullable vitals parsing
- **Status:** `[x]` — DONE. Contracts `Vitals` fields are now nullable (null = not recorded); EVM `TryBuildVitals`/`TryParseNullable` map empty→null, reject invalid/negative input with a Persian toast (`PersistAsync` now returns bool and callers stop), and `VisitEncounterService.SaveVitalsAsync` forwards nulls to `SaveVisitVitalsCommand`.
- **Priority:** P2 · **Push message:** `fix(visits): parse vitals as nullable to distinguish unset from invalid (V12)`
- **Goal:** Empty vitals persist as null; invalid vitals prompt correction instead of silently 0.
- **Current:** `ParseDouble` returns `0` for unparseable text (EVM 529–535); `BuildVitals` casts
  `(int)ParseDouble(...)` (EVM 471–475). `0` means "not recorded" in `Vitals`. `SaveVisitVitalsCommand`
  parameters are already nullable-capable (ESVC 22–28 — currently casts `(decimal)`/passes ints).
- **Do:**
  1. Add a nullable parser, e.g. `private static double? ParseNullableDouble(string text)` returning
     `null` for empty, the value for valid, and signaling invalid separately (e.g. return `null` + set
     a flag, or validate in `BuildVitals`).
  2. In `BuildVitals` (471–475) treat empty → null (unset), invalid → a validation toast
     ("مقدار علائم حیاتی نامعتبر است") rather than 0. Send nulls through the save path — adjust
     `ESVC.SaveVitalsAsync` (ESVC 22–28) and `SaveVisitVitalsCommand` call so nullable weight/temp/
     pulse/resp flow through (confirm the command's params are nullable; the audit notes ENC:29
     parameters are nullable).
- **Acceptance:** empty vitals persist as null; invalid vitals prompt correction.

### Task 13 — V13 · Surface draft-persistence failures
- **Status:** `[x]` — DONE. `DebounceBuffer.FlushAsync` now observes every running/pending write, attempts all of them, and rethrows the first failure with its original type (via ExceptionDispatchInfo) so `SaveDraftAsync`/`CompleteAsync` (already behind the V01 guard) toast instead of showing «ذخیره شد»; faulted background writes stay tracked until observed; shutdown `DisposeAsync` flush is best-effort.
- **Priority:** P2 · **Push message:** `fix(visits): propagate buffered draft-save failures to the UI (V13)`
- **Goal:** A failed draft save is reported, not silently dropped; "پیش‌نویس ذخیره شد" only shows
  after a confirmed write.
- **Current:** `SaveNoteAsync` enqueues into `DebounceBuffer` and returns `Task.CompletedTask`
  immediately (ESVC 35–39); the real dispatch runs later (`SendAsync(…, ct)`, ESVC 102–110) and throws
  on failure with no UI path. `PersistAsync` (EVM 461–469) awaits save methods that complete before the
  write happens. `SaveDraftAsync` (EVM 361–367) calls `FlushAsync` then toasts success. `CompleteAsync`
  flushes first (ESVC 75).
- **Do:** make `FlushAsync` (ESVC 41) await outstanding writes and propagate failures so
  `SaveDraftAsync`/`CompleteAsync` can observe them. Since `SaveDraftAsync` already awaits
  `_service.FlushAsync()` before the success toast (EVM 365–366), a thrown failure there is now caught
  by Task 1's guard → toast, and the success toast won't show. Verify `DebounceBuffer.FlushAsync`
  actually awaits the queued write tasks and rethrows; if it swallows, fix it to aggregate/rethrow.
  At minimum log+toast on buffered-write failure.
- **Acceptance:** a failed draft save is reported; success copy only after a confirmed write.

### Task 14 — V15 · Triage doctor rail: real specialty + in-visit badge
- **Status:** `[x]` — DONE. No specialty/title field exists on User/StaffMembership, so none is invented: the rail query still yields blank and the tile falls back to the role label «پزشک». `DoctorRailInfo` gained optional `HasVisitInProgress` (mapped from the existing query flag) → «در ویزیت» badge on the tile; accent is now a stable FNV-1a id-hash palette pick (`VisitsMapping.AccentFor`, also used by the encounter draft).
- **Priority:** P2 · **Push message:** `fix(visits): show real specialty and in-visit indicator on doctor rail (V15)`
- **Goal:** Tiles show real specialty and an in-visit indicator; stable per-doctor accent.
- **Current:** `GetDoctorRailAsync` (SQLT 16–30) returns `string.Empty` for specialty (line 23) and
  carries an in-progress flag `db.Set<Visit>().Any(v => … State == VisitState.InProgress)` (27) that
  the tile never reflects. `VisitsMapping.ToContract(DoctorRailReadModel)` (VMAP ~8–9) hardcodes accent
  `#0d9488`, sets AppointmentCount from `row.BookedCount`, passes `0` trailing.
- **Do:**
  1. Source specialty in the rail query (SQLT 23) from the staff/profile record. Check `StaffMembership`
     / `User` for a specialty/title field; if one exists, project it instead of `string.Empty`. If no
     such field exists, document that in the commit body and leave specialty blank (do not invent data).
  2. Map the in-progress flag (SQLT 27, already in `DoctorRailReadModel`) to a tile badge in
     `VisitsMapping.ToContract` and the tile VM (`DoctorTileViewModel`).
  3. Derive a stable per-doctor accent (e.g. hash of doctor id → palette) instead of the fixed
     `#0d9488`.
- **Acceptance:** tiles show real specialty (if available) and an in-visit indicator.

### Task 15 — V16 · Unicode-safe avatar initial
- **Status:** `[x]` — DONE. `VisitsMapping.Initial` now returns the first text element via `StringInfo.GetTextElementEnumerator` (surrogate-pair / combining-mark safe).
- **Priority:** P2 · **Push message:** `fix(visits): take first text element for avatar initial to avoid surrogate split (V16)`
- **Goal:** Avatar initials render correctly for non-BMP leading characters.
- **Current:** `VisitsMapping.Initial(value) => value.Length == 0 ? "" : value[..1]` (VMAP line 35) —
  takes the first UTF-16 code unit, splitting surrogate pairs.
- **Do:** replace with the first text element via `System.Globalization.StringInfo`:
  `var e = System.Globalization.StringInfo.GetTextElementEnumerator(value); return e.MoveNext() ? (string)e.Current : "";`
  (or `value.EnumerateRunes().FirstOrDefault().ToString()` handling empty). Apply at VMAP 35.
- **Acceptance:** avatar initials render correctly for non-BMP leading characters.

### Task 16 — V17 · Triage reload without empty flash
- **Status:** `[x]` — DONE. `LoadAsync` now awaits doctors + patients first, builds the new tile list locally (re-marking the restored selection as selected) and swaps the collection in one synchronous pass with no await between Clear and re-add; `_isLoading` guard kept.
- **Priority:** P2 · **Push message:** `fix(visits): swap doctor rail in one pass to avoid empty flash on refresh (V17)`
- **Goal:** Background refresh updates the rail without flashing empty or visibly losing selection.
- **Current:** `LoadAsync` (TVM 173–206) calls `Doctors.Clear()` (186) BEFORE the await returns, then
  repopulates; selection restored at 194–197. `RefreshOnDataChange` fires `_ = ReloadPageAsync()` (97).
- **Do (TVM):** build into a local list first, then swap in one pass AFTER the await — i.e. await
  `_service.GetDoctorsAsync()` into a local, then `Doctors.Clear(); foreach add;` (or diff). Preserve
  the selected doctor id (already captured at 185) without a visible gap. Keep the `_isLoading` guard.
  This aligns with PERFORMANCE.md §5 (batch collection updates).
- **Acceptance:** background refresh updates the rail without flashing empty or losing selection.

### Task 17 — V18 · Typed service exception carrying error category
- **Status:** `[x]` — DONE. New `Animora.Client.Services.ServiceException : InvalidOperationException` (Code + Category, Message stays the bare code) is now thrown by every seam `throw new InvalidOperationException(x.Error.Code)` site (12 services, ~66 sites) so all existing catches/string matches keep working; `PatientsPresentation.CommandFailureMessage` (used by the Visits guards) also branches on `ServiceException.Category` (Forbidden → access-lost copy; other categories keep the caller's contextual fallback).
- **Priority:** P2 · **Push message:** `refactor(services): throw typed ServiceException with error category from seam (V18)`
- **Goal:** VMs branch on error category, not message text.
- **Current:** TSVC (44,46,56,58) and ESVC (62,67,85,87,96,98,107,109) throw
  `InvalidOperationException(result.Error.Code)`, dropping the `ErrorCode` category
  (Validation/Conflict/Infrastructure/…, see `ErrorCodes.cs`). Callers string-match (Patients
  `IsAccessFailure`).
- **Do:** introduce a typed `ServiceException(ErrorCode)` (carry code + category) thrown by the seam;
  provide a shared category→UX mapper. Migrate Visits VM guards (Task 1) and Patients to the typed
  path. **Scope note:** this is a cross-cutting refactor — if it risks touching many callers, keep the
  `InvalidOperationException` throw for backward compat but ALSO expose the category (e.g. derive a new
  exception type that `is InvalidOperationException` is false — then update all catch sites). Given the
  "no heavy churn" preference, a safe minimal version: add `ServiceException : InvalidOperationException`
  carrying `ErrorCode`, throw it from the seam (so existing `catch (InvalidOperationException)` still
  works), and let new code branch on `ex is ServiceException se ? se.Category : …`. Push with a clear
  message since this is a larger change.
- **Acceptance:** VMs can branch on error category; existing message-matching still works.

### Task 18 — V19 · Await tasks instead of `.Result` in Patients load
- **Status:** `[x]` — DONE. Both branches now `await facetsTask`/`await pageTask` after `WhenAll` instead of reading `.Result`.
- **Priority:** P3 · **Push message:** `refactor(patients): await tasks after WhenAll instead of .Result (V19)`
- **Goal:** No `.Result`/`.Wait()` on potentially-incomplete tasks in UI code.
- **Current:** `PatientsPageViewModel` reads `facetsTask.Result`/`pageTask.Result` after
  `await Task.WhenAll(...)` (lines ~431–442, both the owners and pets branches).
- **Do:** replace `facetsTask.Result` → `await facetsTask;` and `pageTask.Result` → `await pageTask;`
  after the `WhenAll` (tasks already complete, so no extra latency), in both branches (owners ~432–433,
  pets ~441–442).
- **Acceptance:** no `.Result` on potentially-incomplete tasks.

### Task 19 — V20 + V21 · Remove "(نمونه طراحی)" on real flows & fix new-patient icon
- **Status:** `[x]` — DONE. Removed «(نمونه طراحی)» from the two triage start toasts (cashier hand-off + done copy were already cleaned in Task 5); new `VisitsPresentation.IconKeyForKind` derives the new-patient icon from the entered kind (replacing hardcoded "dog"); synthetic `np-` id kept with a comment that the backend discards it.
- **Priority:** P3 · **Push message:** `fix(visits): drop design-sample labels on real flows and derive new-patient icon from kind (V20,V21)`
- **Goal:** Real persisted actions report real success copy; the picked-patient card shows an icon
  consistent with the entered species.
- **Do:**
  1. (V20) Remove "(نمونه طراحی)" from real-backed toasts: TVM `StartFromTicketAsync` (335),
     `StartNewAsync` (361), EVM `HandoffToCashier` toast (412 — may already be handled in Task 5).
     Also the DONEV Run (line 18) if not already removed in Task 5. Keep the label ONLY where a genuine
     stub remains.
  2. (V21) In TVM `SaveNewPatient` (308–314) derive `IconKey` from the entered `NewPatientKind` instead
     of hardcoded `"dog"` (314). Reuse `VisitsMapping.Icon` (the kind→icon helper) if it exists; else
     map known kinds. Drop the unused synthetic `Id = "np-"+Guid[..10]` (309) OR leave a one-line
     comment explaining it is discarded by the backend (START 161–172 re-resolves by phone/name).
- **Acceptance:** real actions report real copy; picked-patient card icon matches entered species.

### Task 20 — FINAL · Delete the audit file
- **Status:** `[x]` — DONE. Deleted `docs/VISITS_PETFILE_AUDIT_2026-10-03.md`; nothing else touched.
- **Priority:** closing · **Push message:** `chore(docs): remove completed visits/pet-file audit (2026-10-03)`
- **Goal:** Remove the now-implemented audit document. Touch NOTHING else (no other docs, no roadmap).
- **Do:** `rm docs/VISITS_PETFILE_AUDIT_2026-10-03.md` then push. (This Plan file stays until the user
  decides otherwise — the user's instruction is to delete only the audit file as the final task.)
- **Acceptance:** `docs/VISITS_PETFILE_AUDIT_2026-10-03.md` no longer exists; nothing else changed.

---

## Execution checklist (per task)
1. Implement the task exactly as specified above (consult the embedded code/line refs — avoid
   re-reading unless a line number drifted after an earlier edit).
2. Mark its `- **Status:**` line `[x]` in this file.
3. Run `scripts/git/push.sh "<the task's Push message>"`. (If larger/heavier than expected, the
   message already describes it; push anyway.)
4. On exit code 3 → call `setup_github_environment` tool → re-run. On exit code 2 → resolve the rebase
   conflict (prefer remote) → re-run.
5. Move to the next task. Do not batch multiple tasks into one push.
