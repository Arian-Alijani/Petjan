# Visits & Pet-file audit — 2026-10-03

## Scope and method

- Baseline: product source at `2d2a48f` (`origin/main` == `origin/genspark_ai_developer`, 0/0 divergence). Static source review only. No build, tests, app run, benchmarks or DB execution. Findings describe code behavior; timing and rendered layout are unmeasured.
- Target surface: the **Visits** flow (triage gate → encounter wizard → done/hand-off) and the **Pet file** (patient record), plus their Contracts, Client.Services, Application use cases and SQLite queries, and the cross-section seams they depend on (Appointments, Reception queue, Sales, Cashier, History, Dashboard).
- Flow under review: View → ViewModel → `Animora.Contracts` interface → `Animora.Client.Services` impl → `Animora.Application` dispatch (permission/capability/validation/journal) → Domain + SQLite.
- Two crashes reported by the operator are reproduced to specific code paths: (1) "program recovered after an unexpected stop" when selecting **certain** doctors; (2) "program stopped after an unexpected stop" when finishing a visit / advancing the final step. Both map to the same root mechanism (V01).
- Priorities: **P1** = crash, lost/incorrect business data, access bypass, or a broken primary flow. **P2** = incorrect secondary behavior, resilience, consistency or maintainability risk. **P3** = polish / minor standard deviation.
- **Total: 21 findings — 9 P1, 9 P2, 3 P3.**

## Path aliases

All paths are repo-relative; `Alias:line` resolves to the audited snapshot.

| Alias | Path |
|---|---|
| TVM | `src/Animora.Desktop/Pages/Visits/VisitsTriagePageViewModel.cs` |
| EVM | `src/Animora.Desktop/Pages/Visits/VisitEncounterViewModel.cs` |
| RXVM | `src/Animora.Desktop/Pages/Visits/RxLineViewModel.cs` |
| PLANV | `src/Animora.Desktop/Pages/Visits/PlanStepView.axaml` |
| DONEV | `src/Animora.Desktop/Pages/Visits/DoneStepView.axaml` |
| EV | `src/Animora.Desktop/Pages/Visits/VisitEncounterView.axaml` |
| TSVC | `src/Animora.Client.Services/Visits/VisitsTriageService.cs` |
| ESVC | `src/Animora.Client.Services/Visits/VisitEncounterService.cs` |
| VMAP | `src/Animora.Client.Services/Visits/VisitsMapping.cs` |
| SALESVC | `src/Animora.Client.Services/Sales/SalesQueryService.cs` |
| PMAP | `src/Animora.Client.Services/Patients/PatientsMapping.cs` |
| PFVM | `src/Animora.Desktop/Pages/Patients/PetFileViewModel.cs` |
| SQLT | `src/Animora.Persistence.Sqlite/Queries/TriageQueries.cs` |
| SQLP | `src/Animora.Persistence.Sqlite/Queries/PatientDirectoryQueries.cs` |
| ENC | `src/Animora.Application/UseCases/Visits/EncounterCommands.cs` |
| START | `src/Animora.Application/UseCases/Visits/StartVisitCommands.cs` |
| TRIQ | `src/Animora.Application/UseCases/Visits/TriageQueries.cs` |
| GEH | `src/Animora.Infrastructure.Local/Startup/GlobalExceptionHandler.cs` |
| BASE | `src/Animora.Desktop/Shell/PageViewModelBase.cs` |
| SALEVM | `src/Animora.Desktop/Pages/Sales/SalePageViewModel.cs` |

## Crash mechanism (shared root cause)

The Client.Services seam converts every failed query/command `Result` into a thrown exception: `if (result.IsFailure) throw new InvalidOperationException(result.Error.Code);` (TSVC:44,46,56,58; ESVC:62,67,85,87,96,98,107,109). A ViewModel that awaits these inside an **unguarded `async` RelayCommand** lets the exception escape the async state machine onto the UI dispatcher. `Dispatcher.UIThread.UnhandledException` then routes it to `GlobalExceptionHandler.HandleUiException` (GEH:46–53), which persists a crash report and raises the `CRASH_RECOVERY_REQUIRED` notification — the Persian "program recovered/stopped after an unexpected stop" the operator sees. The Patients page proves the intended pattern: it wraps its load in `try { … } catch (InvalidOperationException ex) when (IsAccessFailure(ex.Message))`. The Visits command handlers do not. Findings V01–V05 are instances of this one gap.

## Findings

### V01 — P1 · Crash · Unguarded async RelayCommands surface service failures as fatal UI exceptions
- **Evidence:** EVM `NextStepAsync` (EVM:324–335) awaits `CompleteAsync`→`_service.CompleteAsync` (EVM:417–433) with no try/catch; EVM `PreviousStepAsync` (EVM:337–347), `SaveDraftAsync` (EVM:361–367), `RemoveRxLineAsync` (EVM:376–395), `BackToGate` (EVM:369–374), `NextVisit` (EVM:397–402) are equally unguarded. TVM `SelectDoctorAsync` (TVM:238–258), `StartFromTicketAsync` (TVM:326–337), `StartNewAsync` (TVM:339–363) likewise. Each awaits a seam call that throws `InvalidOperationException` on any `Result` failure (TSVC:46,58; ESVC:87,98). Escape path: GEH:46–53.
- **Failure:** any backend validation/conflict/permission result (`VISIT_INCOMPLETE`, `VISIT_NOT_EDITABLE`, `TICKET_STATE_INVALID`, `WALK_IN_PHONE_REQUIRED`, EF translation error, store I/O) terminates/soft-recovers the app instead of showing a message. This is the operator's reported crash on finishing a visit and on advancing the final step.
- **Fix:** wrap every awaited seam call in these commands in a single try/catch that maps `InvalidOperationException` to a `Tone.Danger` toast (reuse the Patients `IsAccessFailure`/error-code→message convention), leaving unknown exceptions to rethrow only if truly fatal. Prefer a shared helper (e.g. `RunGuardedAsync(Func<Task>, string userMessage)`) on a Visits VM base to avoid per-command duplication.
- **Acceptance:** completing a visit with a backend rejection shows a toast and keeps the wizard open; no crash report is written for a handled domain failure.

### V02 — P1 · Crash · Completing without a diagnosis throws instead of validating
- **Evidence:** backend requires a non-empty diagnosis even for a lines-only visit: ENC:217–218 returns `ErrorCodes.VisitIncomplete` when `string.IsNullOrWhiteSpace(visit.Diagnosis)` (confirmed expected by `VisitEncounterUseCaseTests.CompletingWithoutDiagnosisOrLines_IsIncomplete`). The wizard never enforces this: `DiagnosisText` is a free `TextBox` (PLANV:17) with no required marker/validation, and `NextStepAsync` advances to `CompleteAsync` unconditionally (EVM:324–335). `CanAdvance` (EVM:65) only checks `CanFinishVisit()` permission, not diagnosis presence.
- **Failure:** the common path "reach Review with an empty diagnosis → click صدور فاکتور و پایان ویزیت" throws `VISIT_INCOMPLETE`, which (per V01) crashes. Even once V01 is fixed, the user gets a late, opaque error at the last step.
- **Fix:** gate completion on diagnosis in the VM: add `CanComplete => !IsLastStep || (CanFinishVisit() && !string.IsNullOrWhiteSpace(DiagnosisText))`, re-notify on `DiagnosisText` change, and bind the foot primary button's `CanExecute` to it; surface an inline "تشخیص الزامی است" hint on the Plan step when empty. Keep the backend rule authoritative (V01 still catches late failures).
- **Acceptance:** the final-step button is disabled until a diagnosis is entered; completion never throws `VISIT_INCOMPLETE` on the happy path.

### V03 — P1 · Crash · Walk-in visit with no phone throws `WALK_IN_PHONE_REQUIRED`
- **Evidence:** `SaveNewPatient` (TVM:300–324) builds a `PickedPatient` with a possibly-empty `Phone` (only name + kind are required, TVM:302). `StartNewAsync` (TVM:339–363) then sends `NewVisitRequest` and awaits `_service.StartNewAsync`. The command handler rejects an empty phone: START:159–160 returns `ErrorCodes.WalkInPhoneRequired`. Unguarded await (V01) → crash.
- **Failure:** registering a quick walk-in patient without a phone and starting the visit crashes the app at "شروع ویزیت".
- **Fix:** mirror the backend contract in the triage form — require phone in `SaveNewPatient` (and/or disable `StartNewCommand` until `PickedPatient.Phone` is non-empty), and still catch the failure per V01 as a toast.
- **Acceptance:** a walk-in without a phone is blocked with a field message, not a crash.

### V04 — P1 · Crash · Doctor selection fails for doctors that have booked appointments
- **Evidence:** `SelectDoctorAsync` (TVM:254) awaits `GetBookedForDoctorAsync`, whose query composes a left join (`join row … into rows / from queue in rows.DefaultIfEmpty()`, SQLT:39–41) **followed by** two inner joins on `pet` and `owner` (SQLT:42–43) inside one LINQ-to-Entities projection (SQLT:35–51). EF Core SQLite can fail to translate a grouping/`DefaultIfEmpty` followed by further joins/projection, throwing at `ToListAsync` (SQLT:51). Doctors with **zero** booked rows never materialize the problematic shape, which matches the operator's "only certain doctors" report.
- **Failure:** selecting a doctor who has appointments today throws a translation `InvalidOperationException`; unguarded (V01) → crash.
- **Fix:** verify translation and, if it fails, restructure the query to avoid left-join-then-join: split into a base appointment query with inner `pet`/`owner` joins, then a separate left lookup of the queue row keyed by appointment id (or project the queue fields with a correlated subquery). Add a persistence test that selects a doctor with ≥1 booked appointment. Independently, V01 prevents the crash even if a translation edge remains.
- **Acceptance:** `GetBookedForDoctorAsync` returns rows for a doctor with appointments without throwing; selecting any doctor never crashes.

### V05 — P1 · Crash · Hand-off to cashier executes an async command fire-and-forget from a sync command
- **Evidence:** `HandoffToCashier` is a synchronous `[RelayCommand] void` (EVM:404–415, bound at DONEV:50) that calls `_navigation.NavigateTo<SalePageViewModel>(vm => vm.BuildFromEncounterCommand.Execute(_encounterId))`. `BuildFromEncounter` (SALEVM:242–260) awaits `CreateFromEncounterAsync`, which throws `InvalidOperationException("INVOICE_NOT_FOUND")` when neither a visit nor a stay handoff owns the id (SALESVC:172–183). Invoked via `ICommand.Execute` on an async relay command, the task is unobserved; a fault escapes as an unobserved-task/UI exception.
- **Failure:** pressing "رفتن به صندوق" on the done screen can crash (or silently drop the invoice) if the handoff is missing or the create fails, with no user feedback.
- **Fix:** make the hand-off awaitable end-to-end: pass the encounter id into the navigation target and have `SalePageViewModel` run `BuildFromEncounter` inside its own guarded load (try/catch → toast), or expose an `async Task` navigation callback that is awaited. Never drive one async relay command by `.Execute()` from another command's body.
- **Acceptance:** a missing/failed handoff shows a toast on the Sales page; the done screen never crashes on hand-off.

### V06 — P1 · Cross-section · Completed visits never appear in the pet file
- **Evidence:** `GetPetAsync` builds `PetDetailReadModel` from pet profile + weight entries only and omits any visit source (SQLP:185–204; the constructor ends at `weights`). `PatientsMapping` hardcodes `Visits: []` with a `C03 placeholder` comment (PMAP:17,46). The pet-file VM renders its "ویزیت‌ها" tab from `pet.Visits` (PFVM:442–453) and derives `LastVisitSinceText`/`LastVisitTypeText` from it (PFVM:286–288). Meanwhile `CompleteVisitCommand` does write a `MedicalHistoryEntry` of kind `Visit` (ENC:223–224).
- **Failure:** the requirement "visit info + invoice must appear correctly in the patient record" is unmet — the Visits tab is always empty and "last visit" always shows "—/بدون سابقه", even though the visit is persisted to medical history. Direct contradiction between the completion write and the pet-file read.
- **Fix:** add a pet-visits projection (read `MedicalHistoryEntry` of kind `Visit` joined to the `Visit`/invoice for diagnosis, plan, lines total and invoice id/state), populate `PetDetailReadModel.Visits`, and map it in `PatientsMapping` instead of `[]`. Include the issued invoice reference so the file can show the visit's فاکتور. Subscribe the pet-file VM to `DataDomain.Visits` refreshes if it can stay open across a completion.
- **Acceptance:** completing a visit for a pet makes that visit (date, type, diagnosis, total, invoice link) appear in the pet file's Visits tab and updates the "last visit" summary.

### V07 — P1 · Cross-section · Owner/species counters and other pet summaries share the same empty-visit placeholder
- **Evidence:** the owner and directory projections also emit `null, null // C03 placeholder — no visit source yet` for `LastVisitUtc`/`LastVisitType` (SQLP:45,179) and `PetSummaryReadModel` passes `null, null` for the same (SQLP:179). The owner clinical activity feed (SQLP:131–160) *does* read visits, so the data exists but is applied inconsistently across pet-centric reads.
- **Failure:** pet cards/lists show no last-visit context while the owner activity feed shows visits — an internally inconsistent view of the same clinical history.
- **Fix:** resolve `LastVisitUtc`/`LastVisitType` from the visit/medical-history source in the pet and pet-summary projections (a correlated `Max(StartedUtc)` + latest reason), consistent with the owner-activity query, and remove the C03 placeholders once V06's projection exists.
- **Acceptance:** pet summaries and the pet file agree on last-visit date/type with the owner activity feed.

### V08 — P1 · Data correctness · `StartNewVisit` matches an existing pet by name and silently reuses it
- **Evidence:** START:167–172 resolves the pet via `pets.FindByOwnerAndNameAsync(owner.Id, c.PatientName, ct)` and only creates a new pet when none matches. Owner is matched by normalized phone (START:161–166).
- **Failure:** two different animals of the same owner sharing a name (e.g. two cats named "Bibi") collapse onto one pet record; the new walk-in's clinical history is attached to the wrong animal. Species is also forced to `Other` for any freshly created pet (START:170), mislabeling the record.
- **Fix:** disambiguate on a stable identifier (chip/code) or require explicit pet selection/creation when multiple same-name pets exist; carry the chosen species from the triage form into `StartNewVisitCommand` instead of defaulting to `Other`.
- **Acceptance:** a walk-in for a new same-named pet creates a distinct record with the correct species; history never merges across animals.

### V09 — P1 · Access control / UX · Finish button silently disabled when completion permission is missing, with no reason
- **Evidence:** `CanAdvance` disables `NextStepCommand` on the last step unless `CanFinishVisit()` (EVM:64–65,324). There is no feedback explaining why; the same command also serves "مرحلهٔ بعد" on earlier steps, so the button's disabled state is ambiguous.
- **Failure:** a user without `visits.complete` reaches Review and finds a dead button with no explanation — indistinguishable from a bug, and no audit of the denied attempt.
- **Fix:** keep the command enabled but short-circuit with a clear "اجازهٔ تکمیل ویزیت را ندارید" toast when `!CanFinishVisit()`, or render a disabled-state tooltip/hint. Align with the capability-gate messaging used elsewhere.
- **Acceptance:** lacking completion permission produces a visible, localized explanation rather than an inert control.

### V10 — P2 · Resilience · Catalog-picker line add drops its surfaced error on permission denial
- **Evidence:** `AddPickedLine` runs only `if (CanEditVisit)` (EVM:309); when false it silently does nothing. When true, `AddPickedLineAsync` catches and toasts (EVM:312–322) — good — but the picker's `SelectedServiceToAdd/SelectedItemToAdd` is reset to null only inside the change hook (EVM:282–298), not after a failed add, so the dropdown selection and the line list can diverge on error.
- **Failure:** a read-only user picking an item gets no feedback; on a transient add failure the picker shows a selection that was never added.
- **Fix:** toast a permission message when `!CanEditVisit`; reset the picker selection in a `finally` after the add attempt regardless of outcome.
- **Acceptance:** denied/failed adds give feedback and leave the picker and line list consistent.

### V11 — P2 · Correctness · Line total and subtotal can overflow `long` with no clamp
- **Evidence:** `RxLine.LineTotal => UnitPrice * Quantity` (Contracts `VisitsEncounterModels.cs`) and `RxLineViewModel.LineTotal` (RXVM:51) multiply without overflow checks; `QuantityText` clamps only the **lower** bound to ≥1 (RXVM:79) with no upper bound. The backend add path uses `checked(...)` for its own aggregate (ENC:244) but the UI subtotal (EVM:477–496) sums unchecked.
- **Failure:** a very large quantity produces a negative/garbage subtotal in the review totals and an inconsistent figure versus the backend (which would throw `OverflowException` in `checked`, re-entering the V01 crash path on complete).
- **Fix:** clamp quantity to a sane maximum in `QuantityText`, and compute UI totals with `checked`/`decimal` plus guard, surfacing an error instead of overflowing. Keep UI and backend arithmetic consistent (both `checked`).
- **Acceptance:** extreme quantities are clamped/validated; UI subtotal matches the backend and never overflows.

### V12 — P2 · Consistency · Vitals parsing coerces invalid input to 0, conflating "unset" and "bad input"
- **Evidence:** `ParseDouble` returns `0` for any unparseable text (EVM:529–535); `BuildVitals` casts pulse/respiration via `(int)ParseDouble(...)` (EVM:471–475). `Vitals` documents `0` as "not yet recorded".
- **Failure:** a typo (e.g. "12a") is silently stored as 0 = "not recorded", losing the distinction between unmeasured and mistyped vitals; no validation feedback.
- **Fix:** parse into nullable values; treat empty as null (unset) and invalid as a validation message rather than 0; send nulls through `SaveVisitVitalsCommand` (its parameters are already nullable, ENC:29).
- **Acceptance:** empty vitals persist as null; invalid vitals prompt correction instead of silently becoming 0.

### V13 — P2 · Reliability · Draft persistence writes are fire-and-forget through the debounce buffer with no surfaced failure
- **Evidence:** note saves enqueue into `DebounceBuffer` and return `Task.CompletedTask` immediately (ESVC:35–39); the actual dispatch runs later with its own scope and throws on failure (ESVC:102–110) with no path back to the UI. `PersistAsync` awaits the vitals/notes save methods (EVM:461–469) but the note methods complete synchronously before the write happens.
- **Failure:** a failed note/diagnosis/plan write (store error, permission change mid-session) is lost silently; `SaveDraftAsync` reports success (EVM:366) even if the buffered write later fails. `FlushAsync` faults would also surface only via the unobserved-task hook.
- **Fix:** have `FlushAsync` await outstanding writes and propagate failures to callers so `SaveDraftAsync`/`CompleteAsync` can report them; at minimum log+toast on buffered-write failure. Confirm `CompleteAsync` flushes before completing (it does, ESVC:75) and that flush errors are caught per V01.
- **Acceptance:** a failed draft save is reported, not silently dropped; "پیش‌نویس ذخیره شد" only shows after a confirmed write.

### V14 — P2 · Cross-section · Done-screen copy claims invoice was sent to cashier before hand-off runs
- **Evidence:** DONEV:17 states "فاکتور به صندوق ارسال شد" unconditionally on the done scene, but the invoice is only created when the user later presses "رفتن به صندوق" (EVM:404–415 → SALEVM:242). `CompleteVisitCommand` writes a billing *handoff* row (ENC:226), not an invoice.
- **Failure:** the success copy asserts an invoice/cashier state that has not happened, misleading staff about cashier readiness (and contradicting the actual "ready for invoice" handoff model).
- **Fix:** reword to reflect the handoff ("ویزیت آمادهٔ صدور فاکتور در صندوق شد") and/or auto-create the invoice at completion if that is the intended product flow; keep the "(نمونه طراحی)" label only if still a stub.
- **Acceptance:** done-screen copy matches the real handoff/invoice state.

### V15 — P2 · Correctness · Triage doctor rail discards booked/specialty data the tile shows
- **Evidence:** `VisitsMapping.ToContract(DoctorRailReadModel)` passes `row.Specialty` through but hardcodes accent `#0d9488` and sets `AppointmentCount` from `row.BookedCount` while passing `0` as the trailing arg (VMAP:8–9). `DoctorRailReadModel` also carries an in-progress flag (SQLT:27) that the tile never reflects. `DoctorTileViewModel` shows `MetricCount` from `AppointmentCount` (DoctorTileViewModel) but specialty is empty from the DB query (`string.Empty` at SQLT:23).
- **Failure:** every doctor tile shows an empty specialty and identical accent; an in-progress-visit indicator is unused, so the rail cannot show which doctor is mid-visit.
- **Fix:** source specialty from the staff/profile record in the rail query (SQLT:23 currently returns `string.Empty`), map the in-progress flag to a tile badge, and derive a stable per-doctor accent.
- **Acceptance:** tiles show real specialty and an in-visit indicator.

### V16 — P2 · Correctness · `Initial()` splits UTF-16 surrogate pairs for doctor avatars
- **Evidence:** `Initial(value) => value.Length == 0 ? "" : value[..1]` (VMAP:35) takes the first UTF-16 code unit.
- **Failure:** a display name starting with a non-BMP character (emoji, some scripts) yields a broken half-surrogate glyph for the avatar initial.
- **Fix:** take the first text element via `System.Globalization.StringInfo`/`Rune` instead of `value[..1]`.
- **Acceptance:** avatar initials render correctly for non-BMP leading characters.

### V17 — P2 · Reliability · Triage reload clears collections before the query returns, flashing empty state under refresh
- **Evidence:** `LoadAsync` clears `Doctors` then awaits + repopulates (TVM:184–191); `RefreshOnDataChange` fires `_ = ReloadPageAsync()` on Appointments/Patients changes (TVM:97). The `_isLoading` guard (TVM:171–180) prevents re-entry but the clear happens before data arrives.
- **Failure:** a background data-change refresh momentarily empties the doctor rail (and can transiently drop the selected doctor before reselection at TVM:194–197), causing flicker and a brief "no doctor" state.
- **Fix:** build into a local list and swap in one pass (clear+addrange after the await), or diff against existing items; preserve selection without a visible gap.
- **Acceptance:** background refresh updates the rail without flashing empty or losing the current selection visibly.

### V18 — P2 · Maintainability · Client.Services seam loses error category by flattening every failure to `InvalidOperationException(code)`
- **Evidence:** TSVC:44,46,56,58 and ESVC:62,67,85,87,96,98,107,109 throw `InvalidOperationException(result.Error.Code)`, dropping the `ErrorCode` category (Validation/Conflict/Infrastructure/…, see `ErrorCodes.cs`). Callers must string-match the message (as Patients does via `IsAccessFailure`).
- **Failure:** every consumer re-implements fragile string matching to decide whether a failure is user-correctable vs. fatal; categories that should drive retry/permission UX are unavailable.
- **Fix:** introduce a typed `ServiceException(ErrorCode)` (carrying code + category) thrown by the seam; provide a shared mapper from category to UX treatment. Migrate callers off message string matching.
- **Acceptance:** VMs branch on error category, not message text.

### V19 — P3 · Standards · `.Result` read on tasks in the patients load (acceptable but non-idiomatic)
- **Evidence:** PatientsPageViewModel reads `facetsTask.Result`/`pageTask.Result` after `await Task.WhenAll(...)` (lines ~432–442). Safe because the tasks are completed, but `.Result` re-wraps exceptions in `AggregateException` and reads as a blocking call.
- **Failure:** none at runtime; a future refactor that moves the read before the await would deadlock/block.
- **Fix:** await each task (`var facets = await facetsTask;`) after `WhenAll`, or capture results from `WhenAll` directly.
- **Acceptance:** no `.Result`/`.Wait()` on potentially-incomplete tasks in UI code.

### V20 — P3 · Consistency · Success toasts labeled "(نمونه طراحی)" on real backend flows
- **Evidence:** EVM/ TVM toasts carry "(نمونه طراحی)" ("design sample") — e.g. visit started (TVM:335,361) and hand-off (EVM:412) — although these now run on the real local backend per the Track C purge.
- **Failure:** operators are told real actions are samples, undermining trust in persisted results.
- **Fix:** remove "(نمونه طراحی)" from flows backed by real services; keep it only where a stub genuinely remains.
- **Acceptance:** real persisted actions report real success copy.

### V21 — P3 · Consistency · Triage new-patient record uses a non-persistable synthetic id and fixed icon
- **Evidence:** `SaveNewPatient` sets `Id = "np-" + Guid` truncated to 10 chars and `IconKey = "dog"` regardless of the entered kind (TVM:308–314). This synthetic ref is only used to build `NewVisitRequest`, which the backend re-resolves by phone/name (START:161–172), so the id is discarded — but the fixed "dog" icon flows into the draft.
- **Failure:** a new walk-in cat/bird shows a dog icon in the picked-patient card until the draft is reloaded; the synthetic id is dead weight.
- **Fix:** derive the icon from the entered kind (reuse `VisitsMapping.Icon`), and drop the unused synthetic id or document why it exists.
- **Acceptance:** the picked-patient card shows an icon consistent with the entered species.

## Cross-section verification summary

| Seam | Expected | Current code state | Verdict |
|---|---|---|---|
| Visit complete → appointment status | appointment-backed visit completes its appointment in the same UoW | `CompleteVisitCommand` loads the appointment and calls `appointment.Complete()` when `State == InVisit` (ENC:229–240) | OK (supersedes the older appointments-audit A03) |
| Visit complete → reception queue | in-visit queue entry closes | `queue.FindByVisitAsync` → `entry.Close(clock)` when `InVisit` (ENC:227–228) | OK |
| Visit complete → history ledger | a `Visit` medical-history entry is written | ENC:223–224 writes it | OK |
| Visit complete → pet file | visit + invoice shown in the patient record | pet-file read omits visits; mapping returns `Visits: []` (SQLP:185–204, PMAP:46) | **Broken (V06/V07)** |
| Visit complete → cashier/invoice | invoice routed to cashier | a billing *handoff* is written (ENC:226); invoice created only on manual "رفتن به صندوق" via a fragile fire-and-forget (V05); done copy over-claims (V14) | **Partial / crash-prone (V05, V14)** |
| Visit complete → dashboard | counts refresh | `_changes.Notify(Visits, Sales, Cashier, History, Patients, Dashboard)` (EVM:424–426) | OK |

## Suggested remediation order

1. V01 (wrap all Visits async commands) — removes both reported crashes immediately and is the safety net for V02–V05.
2. V04 (verify/restructure `GetBookedForDoctorAsync`) and V02/V03 (pre-validate diagnosis and walk-in phone) — eliminate the specific failing inputs.
3. V05/V14 (make cashier hand-off awaitable and fix its copy).
4. V06/V07 (populate pet-file visits + last-visit summaries) — closes the primary cross-section gap.
5. V08 (pet disambiguation/species) then P2/P3 polish.
