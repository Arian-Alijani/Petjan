# Appointments Remediation — Execution Plan

> **Phase source:** `docs/APPOINTMENTS_AUDIT_2026-10-02.md` (29 findings: 7 P1 + 22 P2).
> **Goal of this plan:** a self-contained, task-by-task implementation guide. A fresh model
> must be able to start coding from this file alone, with minimal re-inspection of the repo.
> **Baseline commit audited:** `5a0d544`. Static review only — no build/test/run was done for the audit.

---

## 0. Context the executor MUST hold before touching code

### 0.1 Product shape & layering (strict, enforced by `LayeringRulesTests`)
Avalonia desktop app, offline-first, server-free, local **encrypted SQLite**. Clean architecture.
The **only** data path Desktop may use is `Animora.Contracts` → `Animora.Client.Services`:

```
Avalonia View (.axaml) → ViewModel → Contracts (interfaces/DTOs) → Client.Services
  → Application (dispatcher + 6-behavior pipeline: validation/permission/capability/transaction/journal)
  → Domain (aggregates + state machines) + Persistence.Sqlite (EF Core queries/repos) + Infrastructure.Local
```
- Desktop must **never** reference EF/SQL/domain-rules directly. Client.Services are `sealed`, scope-per-call over the dispatcher.
- Commands go through `IDispatcher.SendAsync(command, commandContext)`; reads via `IQuery<T>` handlers.
- `TransactionBehavior` wraps journal + handler in one UoW. `CommandJournalBehavior` copies `commandContext.ExpectedAggregateVersion` into the durable envelope.
- Domain state machine is the sole authority (a version mismatch never decides legality).

### 0.2 Phase boundary — what is IN scope vs. explicitly NOT a defect
**In scope (shipped local behavior to fix):** everything in findings A01–A29 below.
**NOT defects / do NOT build early:** server/API/SQL Server (D01–D13), global slot settlement (D06),
Datasync/transport/conflict reconciliation (E03–E06), online licensing (E07), release/perf certification
(E08–E09), recurring appointments (excluded by C04). SMS *delivery* absence is not a defect — only
*false delivery claims* are. No tech upgrade, no unrelated refactor.

### 0.3 Existing safeguards — PRESERVE, never "fix" these away
- `SchedulingCommands.cs:BookingWork.EnsureSlotAsync` + `TransactionBehavior` = authoritative transactional slot invariant. Preview defects do not mean this is missing.
- Mutations use `.AsTracking()` repos; reads use `AsNoTracking` projections; tenant-filtered UTC predicates.
- Indexes exist: doctor/start, start/doctor, state/start, patient. Do not assert missing indexes without a query plan.
- Patient picker (`BookingPanelViewModel:400–458`) already cancels/debounces/version-checks; `AppointmentQueries.GetPatientsAsync` rejects <2-char and caps 21. `AppointmentBookingService.ResolvePatientAsync` resolves stable patient ID. Keep all this.
- Export HTML uses `WebUtility.HtmlEncode`, built off-thread (`AppointmentExportViewModel:794`). No HTML-injection defect. Keep brush caching + compiled bindings.

### 0.4 Path aliases (repo-relative). `Alias:line` = exact source line in baseline.
| Alias | Path |
|---|---|
| CVM | `src/Animora.Desktop/Pages/Appointments/AppointmentsCalendarViewModel.cs` (1772 lines) |
| BVM | `src/Animora.Desktop/Pages/Appointments/BookingPanelViewModel.cs` (912) |
| DVM | `src/Animora.Desktop/Pages/Appointments/BookingDetailViewModel.cs` (211) |
| DV | `src/Animora.Desktop/Pages/Appointments/BookingDetailView.axaml` |
| EV | `src/Animora.Desktop/Pages/Appointments/Export/AppointmentExportViewModel.cs` (855) |
| MAP | `src/Animora.Client.Services/Appointments/AppointmentsMapping.cs` |
| BS | `src/Animora.Client.Services/Appointments/AppointmentBookingService.cs` |
| QS | `src/Animora.Client.Services/Appointments/AppointmentsQueryService.cs` |
| SS | `src/Animora.Client.Services/Appointments/AppointmentSettingsService.cs` |
| SQL | `src/Animora.Persistence.Sqlite/Queries/AppointmentQueries.cs` |
| PROJ | `src/Animora.Persistence.Sqlite/Queries/SchedulingProjections.cs` |
| CMD | `src/Animora.Application/UseCases/Scheduling/SchedulingCommands.cs` |
| QUERY | `src/Animora.Application/UseCases/Scheduling/SchedulingQueries.cs` |
| RM | `src/Animora.Application/UseCases/Scheduling/ReadModels/SchedulingReadModels.cs` |
| APT | `src/Animora.Domain/Scheduling/Appointment.cs` |
| COMPLETE | `src/Animora.Application/UseCases/Visits/EncounterCommands.cs` (CompleteVisitCommandHandler @205–239) |
| STARTVISIT | `src/Animora.Application/UseCases/Visits/StartVisitCommands.cs` (@100–104 links appointment) |
| TZ | `src/Animora.Infrastructure.Local/Time/ClinicTimeZoneProvider.cs` |
| BASE | `src/Animora.Desktop/Shell/PageViewModelBase.cs` |
| NOTIFY | `src/Animora.Desktop/Services/DataChangeNotifier.cs` |
| CV | `src/Animora.Desktop/Pages/Appointments/AppointmentsCalendarView.axaml` |
| QV | `src/Animora.Desktop/Pages/Appointments/AppointmentsQueueTabView.axaml` |
| HV | `src/Animora.Desktop/Pages/Appointments/AppointmentsHistoryTabView.axaml` |
| GRID | `src/Animora.Desktop/Pages/Appointments/TimeGridSurface.axaml` |
| PANEL | `src/Animora.Desktop/Controls/TimeGridEventsPanel.cs` |
| ITEM | `src/Animora.Desktop/Pages/Appointments/AppointmentsItemViewModels.cs` (656) |
| PRESENT | `src/Animora.Desktop/Pages/Appointments/AppointmentsPresentation.cs` (171) |
| SETTINGS | `src/Animora.Contracts/Appointments/AppointmentSettingsModels.cs` |
| PAYLOAD | `src/Animora.Contracts/Appointments/AppointmentBookingModels.cs` |
| EVMODEL | `src/Animora.Contracts/Appointments/AppointmentModels.cs` (AppointmentEvent @64) |
| ISETTINGS | `src/Animora.Contracts/Appointments/IAppointmentSettingsService.cs` |
| PERM | `src/Animora.Domain/Identity/PermissionCodes.cs` (SettingsRead @155, SettingsWrite @158) |
| ROLEPERM | `src/Animora.Domain/Identity/DefaultRolePermissions.cs` (Doctor/Receptionist get SettingsRead, not SettingsWrite) |

### 0.5 Hard facts already confirmed by reading the code (do NOT re-verify)
- **`Appointment` domain (APT)** already stores: `VisitId`, `CheckedInUtc`, `CancelReason`, `CreatedByUserId`,
  `RescheduleCount`, `DoctorUserId` (nullable), full 7-state machine, and public `Complete()` (APT:231) + `MarkNoShow(clock)` (APT:234). `IsTransitionAllowed(from,to)` (APT:293) is the table. **No new domain fields are needed for provenance** — they exist; they are simply dropped by the projection.
- **`AppointmentEventProjection` (PROJ:30–45)** carries: Id, StartUtc, DurationMinutes, State, TypeCode, DoctorId, DoctorName, PetId, PetName, PetSpecies, OwnerId, OwnerName, OwnerPhone, Note. It **omits** VisitId, CheckedInUtc, CancelReason, CreatedByUserId, RescheduleCount, Source — these must be added for A05/A13/A28/A29.
- **`AppointmentEventReadModel` (RM)** mirrors the projection fields; must be extended in lockstep.
- **`AppointmentEvent` contract (EVMODEL:64)** already has `Total`, `Invoice`, `Payment`, `Visit`, `Status`, `Code` fields. `MAP:21–26` hardcodes `Total=0`, `Invoice.None`, `Payment.None`, `Code=row.Id`. `MAP:14–20` compresses 7 states → 4 (`Scheduled→Pending`, `Cancelled→Cancelled`, `Completed/NoShow→Done`, else→Confirmed`) — the core cause of A04.
- **`RescheduleAppointmentCommand` (CMD:86)** = `(AppointmentId, Date, StartMinutes, DurationMinutes)` only. Handler (CMD:93–116) keeps old doctor/type/note/patient. `BS.UpdateAsync` (BS:80–85) sends only this → A02 silent data loss.
- **`CompleteVisitCommandHandler` (COMPLETE:205–239)** has NO appointment repository injected and never calls `Appointment.Complete()` → A03. `StartVisitCommands` (STARTVISIT:100–104) DOES link appointment via `appointment.StartVisit(visit.Id)` inside the same handler — mirror that pattern for completion.
- **`AppointmentSettingsService` (SS)** writes JSON directly (SS:36–99,132–139), no dispatcher/permission/current-user → A17. `ISettings` contract (ISETTINGS) has Get/Save/AddType/RemoveType/SetCalendarMode/SetSms. `CalendarMode.DayOnly` (SETTINGS:9) + `CustomAppointmentType.RequiresDoctor` (SETTINGS:22) + `AppointmentSmsSettings` (SETTINGS:25, defaults both switches ON).
- **`AppointmentQueries.GetHistoryAsync` (SQL:69–76)** returns terminal states, `Take(200)`, and is NOT used by the UI history tab (CVM derives history from the 81-day preload instead) → A01.
- **`GetDayCountsRawAsync` (SQL:35–43)** does `.Select(a=>a.StartUtc).ToListAsync()` then in-memory GroupBy → A20 (materializes every start).
- **`SchedulingQueryWork.InvalidRange` (QUERY:15–16)** rejects `end-start > 62 days` (= 63 inclusive). Export (EV:30) allows 186 days → A14.
- **`BookingWork.EnsureSlotAsync` (CMD:20–35)** is the authoritative slot/working-hours policy using `SlotConflictPolicy`. `BS.GetConflictsAsync` (BS:57–64) is a SEPARATE client-side filter that disagrees → A09. `SchedulingQueryWork.Available` (QUERY:18–32) is a third path for the slot grid.

---

## 1. Execution protocol (LOCKED — read every time)

1. Work on branch **`genspark_ai_developer`**. Both branches are already synced & clean at start.
2. **After completing EACH task below: run `scripts/git/push.sh "<conventional commit msg>"`.** Not pushing = failure.
   - If push exits code 3 → call the `setup_github_environment` tool once, re-run.
   - If a single task produced a large/heavy change mid-way, push it with a clear message before continuing.
3. **No full build / no full test suite / no long benchmarks.** Targeted compile of a touched project only if quick; otherwise rely on static correctness. (User directive: heavy builds/tests forbidden.)
4. Mark the task's checkbox `[x]` in THIS file as you finish it, then push (the checkbox update rides with the task's commit or the next push).
5. Keep changes purposeful — touch only what the task needs.
6. **Final task = delete ONLY this plan file** (`Plan/APPOINTMENTS_REMEDIATION_PLAN.md`). Do not touch other docs, roadmap, or previously-deleted plan files.

---

## 2. Remediation order (from audit §"Remediation architecture and order")
Correctness/security first (A01–A06, A17) → one snapshot/refresh system (A07–A10) → truthful
actions/data (A11–A13, A18–A19, A28) → bounded export (A14–A16) → scale (A20–A22, A27) →
platform/UX (A23–A26, A29). Tasks below are grouped to match, and sized to be independently pushable.

---

## 3. TASKS

Each task block has: **Why/Evidence** (the defect), **Do** (exact change + files), **Acceptance**,
**Commit** (the message to push with). State is tracked by the checkbox. `[ ]`=pending, `[x]`=done.

---

### T01 — [x] Backend data seam: extend projection + read model with provenance & financial linkage
**Covers groundwork for:** A05, A12, A13, A28, A29 (all need fields the projection currently drops).
**Why/Evidence:** PROJ:30–45 omits `VisitId, CheckedInUtc, CancelReason, CreatedByUserId, RescheduleCount, Source`
and any invoice linkage. RM mirrors only the visible fields. Because the read model has no provenance,
A28 (timeline fabricated), A13 (no VisitId to navigate), A05 (doctor identity lost), A29 (no aggregate
version to journal) are all impossible to fix in the UI without this seam.
**Do:**
1. Extend `AppointmentEventProjection` (PROJ) with: `string? VisitId`, `DateTimeOffset? CheckedInUtc`,
   `string? CancelReason`, `string CreatedByUserId`, `int RescheduleCount`, `AppointmentSource Source`.
   Map them straight from the `Appointment` columns in `SchedulingProjections.AppointmentEvent`.
2. For financial linkage (A12): add `long? InvoiceTotalMinor`, `long? InvoicePaidMinor`,
   `string? Currency`, `string? InvoiceState` via a correlated sub-select from the Visit→Invoice chain
   **only if the delivered local backend already exposes it**. Confirm by checking `src/Animora.Persistence.Sqlite/Queries/FinanceQueries.cs` for an invoice-by-visit read; if no direct appointment→invoice link exists, project NULLs and leave A12 numbers to T08 (which will decide label-vs-source). Do NOT invent a join that isn't backed by a real FK.
3. Extend `AppointmentEventReadModel` (RM) with the same new fields (keep positional record; append at end to minimize churn, update all constructor call sites: `AppointmentQueries.Map` SQL:17–20).
4. Add `string? AggregateVersion` to `AppointmentDetailReadModel` (RM). **CONFIRMED:** the version lives on
   `SyncableEntity.Version` (type `string?`, base of `AggregateRoot` via `src/Animora.Domain/Primitives/SyncableEntity.cs:42`).
   Project `appointment.Version`. It feeds A29 (`ExpectedAggregateVersion` is string-shaped in the command context).
**Files:** PROJ, RM, SQL (Map + GetDetailAsync). No migration (all columns already exist on the `Appointments` table).
**Acceptance:** projection/read model compile with new fields populated from real columns; no fabricated values; NULL where no real source exists.
**Commit:** `feat(appointments): carry appointment provenance & linkage through read seam`

---

### T02 — [x] A03 (P1): completing a visit must complete its appointment atomically
**Why/Evidence:** COMPLETE:205–239 completes Visit+history+handoff+queue but never transitions the
appointment. APT:231 `Complete()` has no production caller. Result: finished encounters leave the
appointment `InVisit` forever → wrong history, occupied slot (SQL:85–86 keeps InVisit occupied),
rescheduling rejected, bad completion counts. STARTVISIT:100–104 already shows the linkage pattern.
**Do:**
1. Inject `IAppointmentRepository` into `CompleteVisitCommandHandler` (COMPLETE:205).
2. Inside the same UoW (handler already runs in `TransactionBehavior`), after `visit.Complete(clock)` succeeds:
   use **CONFIRMED** property `Visit.AppointmentId` (`string?`, `src/Animora.Domain/Clinical/Visit.cs:97`; set by StartVisit).
   If non-null/non-empty: `var appt = await repository.FindAsync(visit.AppointmentId, ct);` verify `appt?.VisitId == visit.Id`
   and `appt.State == AppointmentState.InVisit`, then `appt.Complete()` (APT:231); on failure return a failing `Result` so the whole UoW rolls back.
3. If `AppointmentId` is null/empty (walk-in), do nothing — not every visit has an appointment.
**Files:** COMPLETE only. (`IAppointmentRepository.FindAsync` is the same one used by `RescheduleAppointmentCommandHandler` CMD:98.)
**Acceptance:** finishing an appointment-backed visit commits Visit completion + queue closure + `Appointment.Completed` together; injected failure rolls back all three; walk-in visits unaffected.
**Commit:** `fix(appointments): complete linked appointment atomically on visit completion (A03)`

---

### T03 — [x] A04 (P1): represent the full lifecycle; stop collapsing 7 states into 4
**Why/Evidence:** MAP:14–26 maps 7 domain states → 4 contract statuses (`CheckedIn/InVisit→Confirmed`,
`Completed/NoShow→Done`). PRESENT:84–99 labels `Done` as "admitted"; CVM:794–795 counts `Done` as
checked-in; DVM:155–158 derives admission from `Done`; DV:177–210 offers check-in whenever `!IsDone`
and edit/cancel unconditionally. Net: check-in shows "Confirmed" & isn't counted; no-show shows
"admitted" & offers a visit action; InVisit/cancelled rows expose illegal actions. Backend rejects
illegal writes, but the UI affordances/summaries are wrong.
**Do:**
1. Carry the real lifecycle to the UI. Options (pick the lower-churn one): either add the raw
   `AppointmentState` (or a new richer `AppointmentLifecycle` enum covering all 7) onto `AppointmentEvent`
   (EVMODEL), **plus** booleans `IsCheckedIn`, `CanCheckIn`, `CanMove`, `CanCancel`, `CanOpenVisit`
   computed from state+capability. Keep `Status`/`Visit`/`Invoice` for history rendering.
2. Update MAP to populate the new field(s) from `row.State` (do NOT collapse). `CanCheckIn = state ∈ {Scheduled,Confirmed}`;
   `CanMove = state < InVisit`; `CanCancel = state ∈ {Scheduled,Confirmed,CheckedIn,InVisit}` per APT:293 table (CheckedIn→Cancel is blocked by CMD:133, so exclude CheckedIn for cancel to match backend); `CanOpenVisit = VisitId != null && state ∈ {InVisit,Completed}`.
3. Fix PRESENT:84–99 labels (admitted = CheckedIn, not Done). Fix CVM:794–795 count to use `IsCheckedIn`.
   Fix DVM:155–158 admission timeline to use CheckedInUtc/state, not Done. Gate DV:177–210 action
   buttons by the new `Can*` flags (bind `IsVisible`/`IsEnabled`).
4. Keep admission separate from visit-complete/no-show. Keep Application authorization + domain validation.
**Files:** EVMODEL, MAP, PRESENT, CVM (count @794–795), DVM (@155–158), DV (action bindings @177–210).
**Acceptance:** each of the 7 states shows accurate label/count/timeline & only legal actions; no-show never implies attendance; checked-in is counted as admitted.
**Commit:** `fix(appointments): represent full 7-state lifecycle in UI & gate actions (A04)`

---

### T04 — [x] A02 (P1): make the edit contract honest (persist every enabled field, validate the final doctor)
**Why/Evidence:** BVM:618–639,840–847 sends type/doctor/patient/note/notification; PAYLOAD:74–88
declares them; but `BS.UpdateAsync` (BS:80–85) only dispatches `RescheduleAppointmentCommand(id,date,start,dur)`.
CMD:86–114 keeps the OLD doctor and has no type/note/patient update. Slot availability is previewed for the
NEW doctor but the write validates/moves the OLD doctor. Edit shows success while changes are silently lost.
**Do — choose ONE explicit contract (prefer option A, lower risk):**
- **Option A (reschedule-only, honest UI):** in BVM/DV, when editing an existing appointment, disable
  (grey + label "قابل‌ویرایش نیست") the type/doctor/patient fields; keep only date/start/duration editable;
  rename the action to "جابه‌جایی" (reschedule). Remove the notification switches' "sent" implication (see T09).
- **Option B (full update, more work):** add an `UpdateAppointmentCommand(appointmentId, date, start, dur, typeId, doctorId, petId, ownerId, note, expectedVersion)` with a domain mutator on `Appointment` to change doctor/type/note (patient change only via identity, never display name), authorize + journal it, and validate the FINAL doctor/type/slot inside the transaction (reuse `BookingWork.EnsureSlotAsync`). Never store display names as identity.
**Decision rule:** if a product owner has not asked for in-place doctor/type change, implement **Option A** — it is honest and small. Document the choice in the commit body.
**Files (Option A):** BVM (field enable/labels), DV (labels/action text), BS (keep as-is). **(Option B):** CMD, APT (mutators), BS, PAYLOAD, plus version plumbing from T01.
**Acceptance:** every *enabled* edit field persists after reopening; new-doctor conflicts match the committed doctor's schedule; no success toast for a discarded change.
**Commit:** `fix(appointments): honest edit contract — no silent field loss (A02)`

---

### T05 — [ ] A17 (P1): route appointment settings through authorized Application use cases
**Why/Evidence:** SS:36–99,132–139 reads/writes JSON directly with no dispatcher/current-user/permission.
CVM:1201–1260 calls these mutators directly; CV:487–490 exposes the settings tab. ROLEPERM gives
Doctor/Receptionist `SettingsRead` but NOT `SettingsWrite` (PERM:155/158 define both). So a read-only
role can change clinic-wide mode, SMS defaults and the type catalog. Non-journaled storage does not
exempt it from local authorization.
**Do:**
1. Add Application use cases: `GetAppointmentSettingsQuery` ([RequiresPermission(SettingsRead)]) and
   write commands `SaveAppointmentSettingsCommand` / `AddAppointmentTypeCommand` / `RemoveAppointmentTypeCommand`
   / `SetCalendarModeCommand` / `SetAppointmentSmsCommand`, each `[RequiresPermission(SettingsWrite)]`.
   These may be **non-journaled** (preferences, not transactional) — do not add `IJournaledCommand` unless trivial.
   The handlers delegate to a settings port (move the file I/O behind an `IAppointmentSettingsStore` port in Application, implemented by the existing SS logic in Client.Services/Infrastructure).
2. Rewrite `AppointmentSettingsService` (SS) to dispatch these use cases (scope-per-call) instead of touching the file directly — same pattern as the other C-series Client.Services.
3. Bind the settings tab visibility/enabled state (CV:487–490, CVM:1201–1260) to the `SettingsWrite` capability; keep the authoritative permission check below the UI (the attribute enforces it regardless of UI).
**Files:** new Application scheduling settings use cases + registration (`SchedulingRegistration.cs`), new port, SS rewrite, CVM (capability gate), CV (visibility binding).
**Acceptance:** read-only roles cannot mutate settings via the tab OR a direct Contracts call; manager can.
**Commit:** `fix(appointments): authorize settings reads/writes via Application pipeline (A17)`

---

### T06 — [ ] A01 (P1): load the actually-selected range; use the real history query
**Why/Evidence:** CVM:309–312 preloads only `today±40` days; `Step()` (CVM:1357–1375) + queue nav
(CVM:1579–1583) only re-render, never re-query. History (CVM:923–979) derives `all`/custom scopes from
the in-memory `_all` preload; the real `GetHistoryAsync` (SQL:69–75) is unused. Result: appointments
outside ±81 days appear absent; a valid future booking outside the window disappears after save;
history totals/search cover the preload, not the DB.
**Do:**
1. On calendar/queue navigation, query the **visible** date range through `AppointmentRangeQuery`
   (keep C04's 63-inclusive-day guard per request — see A14/T13). `Step()` must trigger a load for the
   new cursor range, not just `Render()`.
2. History tab: query `AppointmentHistoryQuery` (and add server-side scope/filter + pagination +
   independent aggregates — extend QUERY/SQL with a parameterized history query: date range, status,
   paging, and separate COUNT/SUM for totals). Do NOT reuse the last-200 query as an "all archive".
3. Expose three distinct states: loading / failure / loaded-empty (reuse `PageLoadStatus` from BASE).
**Files:** CVM (nav load @309–312,1357–1375,1579–1583; history @923–979), QUERY + SQL (parameterized history), RM (history page/aggregate model).
**Acceptance:** appointments at −120/+120 days stay visible when their date is selected; historical totals cover the requested scope, not the preload; loading/empty/error distinguished.
**Commit:** `fix(appointments): query selected range & real history instead of preload (A01)`

---

### T07 — [ ] A05 (P1): preserve event doctor/type identity for unassigned/inactive/removed references
**Why/Evidence:** APT:98–99 allows `DoctorUserId=null`; MAP:22 turns null into `""`. SQL:145–151 returns
only ACTIVE Doctor memberships; CVM:325–329 builds active IDs from that roster; CVM:449–455 excludes
every other doctor id; CVM:391–392 uses `.First(...)`; DVM:132–133 falls back to `_doctors[0]`. So
unassigned or removed/inactive-doctor appointments vanish from calendar/queue/history, or detail claims
a different doctor / throws on empty roster. Removed TYPE similarly falls back to the first catalog type.
**Do (depends on T01 fields):**
1. Carry the event's own `DoctorId` + `DoctorName` snapshot (PROJ already has DoctorName via sub-select — keep it even when membership inactive; the sub-select is on `User`, not membership, so inactive doctors still resolve a name). Ensure MAP keeps the real id (not `""` → `null`), and the name snapshot.
2. Add explicit buckets for unassigned (`DoctorId==null`) and inactive (id not in active roster) in CVM filtering (CVM:325–329,449–455) instead of excluding them. Never `.First()` a roster (CVM:391–392); use neutral fallback when the id isn't in the active list (DVM:132–133).
3. Type: if `TypeCode` isn't in the catalog, render a neutral missing-reference type, not `catalog[0]`.
**Files:** MAP (null handling), CVM (@325–329,391–392,449–455), DVM (@132–133), PROJ/SQL (ensure DoctorName resolves for inactive).
**Acceptance:** an unassigned or formerly-active doctor's appointment stays accessible & correctly labeled even with zero active doctors; removed type shows neutral label, not the first catalog type.
**Commit:** `fix(appointments): preserve event doctor/type identity for inactive/unassigned (A05)`

---

### T08 — [ ] A06 (P1) + A07/A08/A10 (P2): one coalescing refresh/snapshot coordinator
**Why/Evidence (A06):** CVM:124 subscribes to Visits+Patients; CVM:1531–1543 fires both notifications then
also calls `RefreshAfterChangeAsync()`. NOTIFY:18,35–40 dispatches synchronously on the UI thread;
BASE:95–102 runs tracked loads. CVM:303–312 & 1539–1542 clear/mutate shared `_all` around awaits; only
the tracked path has a guard. → overlapping reads, partial/duplicated snapshots, `Clear()`-then-fail
destroys the backing data, untracked refresh can't report stale/error.
**A07:** CV:442–484 caches tab views; CVM:358–370,1545–1548 render only the active tab; a write while
queue/history is active leaves the cached calendar stale until a calendar action.
**A08:** BVM:330–344 reads mutable `_doc/_date/_dur/_start/_id` across awaits with no generation/cancellation
(patient search BVM:400–458 has it; availability does not) → mixed old/new slot results.
**A10:** BS:16–24 & QS:15–23 reduce failures to `InvalidOperationException(code)`, dropping Detail/fields;
BVM:817–862 & DVM:162–189 have no local result/error handling → business rejections surface as uncaught command failures; no success-before-failed-write guard.
**Do:**
1. Build a single refresh coordinator for external notifications + local writes: build a **complete immutable
   snapshot** off shared state, then commit once on the UI thread. Use a generation/cancellation guard; keep
   the last-good snapshot on failure; mark exactly one pending reload. Remove the duplicate untracked `RefreshAfterChangeAsync()` path (CVM:1531–1543) — fold into the tracked/guarded load.
2. A07: render the selected calendar on activation OR keep per-tab snapshot-version/dirty markers; rebuild only when the version changed.
3. A08: capture one immutable availability request in BVM; cancel/obsolete prior requests; apply only the current generation; disable submit while a result is pending/failed.
4. A10: expose typed success/error at the Contracts seam (or a structured exception adapter preserving code/detail/fields); handle writes locally with busy/finally, localized feedback, retained input; never show a success toast before the write result; separate write-success from a later detail-read failure.
**Files:** CVM (refresh @124,303–312,358–370,1531–1548), BASE (reuse load status), NOTIFY (confirm sync dispatch), BVM (availability generation @330–344; error handling @817–862), DVM (@162–189), BS/QS (error surface @16–24).
**Acceptance:** a write with simultaneous Visits/Patients notifications performs ONE coherent apply; injected read failure preserves previous data + shows retry; switching to calendar after a queue/history write shows new data; rapid doctor/date/duration changes never apply a stale result; conflict/permission/store failures keep the form open with data + recovery action.
**Commit:** `fix(appointments): single coalescing refresh/snapshot coordinator + typed write errors (A06,A07,A08,A10)`

---

### T09 — [ ] A11/A13/A28 + A12 + A18/A19 (truthful actions & data)
**Why/Evidence:**
- **A11 (SMS):** BVM:851–857 shows "confirmation SMS sent"; BS:72–76 ignores the flags; DVM:179–181
  toasts on SendSms; DVM:187 claims owner notification after cancel; DV:157–160 hardcodes a sent-timeline;
  SETTINGS:24–30 defaults both switches ON. No delivery operation exists → false delivery claims.
- **A28 (timeline/provenance):** SQL:52–56 builds detail with `CreatedText=""`; PROJ omits provenance;
  DVM:152–158 renders empty creation text + a guessed 2-state timeline; DV:157–160 fabricates an SMS event.
  `Code` is just `row.Id` (MAP:23), not a human record code.
- **A13 (view visit):** DVM:192–194 implements `ViewVisit()` as a toast only; no VisitId carried (PROJ/MAP).
- **A12 (money):** MAP:23–26 always `Total=0`/Invoice.None/Payment.None; BVM:594 invents `_dur*12000`;
  DVM:150 shows the mapped zero as a price; CVM:851–853 derives Partial receivable as `Total/2`.
- **A18 (RequiresDoctor):** SETTINGS:17–22 persists it; QS:46–47 drops it; BVM:308–310 always picks a doctor; CMD:53–55 requires DoctorId. The option has no effect.
- **A19 (DayOnly):** SETTINGS:9 = date-only; BVM:334–336 hides conflicts; BVM:796–813 picks the first free timed slot & rejects a full day; detail (DVM:142)/print keep the real interval → inconsistent semantics.
**Do:**
1. **A11:** remove all "sent" claims. Delete the hardcoded SMS timeline row (DV:157–160) & the success
   toast (BVM:851–857, DVM:179–181,187). Disable/label the notification switches as "local preference
   only — no delivery" or hide them. Clarify SETTINGS defaults are local intent, not delivery.
2. **A28:** use T01's provenance to render a real timeline from `CreatedByUserId`/`CheckedInUtc`/`CancelReason`/state.
   Fill `AppointmentDetailReadModel.CreatedText` from the real creator (resolve user display name) — or
   render "نامشخص" when unknown; never invent events. Label any raw id used as "Code" explicitly.
3. **A13:** carry `VisitId` (T01) into `AppointmentEvent`; `ViewVisit()` navigates via the existing
   visit-record navigation seam; gate by `CanOpenVisit` (T03). If no visit linked, show a truthful
   unavailable state (no toast-navigation).
4. **A12:** if T01 confirmed a real appointment→invoice link, project actual total/paid/balance/currency/state
   and render those. If NO real link exists, REMOVE the invented price (BVM:594), stop showing the mapped
   zero as a price (DVM:150), and replace the `Total/2` receivable heuristic (CVM:851–853) with the real
   outstanding balance or omit the money entirely. Never fabricate or contradict amounts.
5. **A18:** either hide/disable the `RequiresDoctor` toggle until a supported policy exists, OR carry it
   through QS:46–47 + read models and implement a doctor-optional booking path (bigger — prefer disable
   with a clear "unavailable" label unless asked).
6. **A19:** pick ONE honest DayOnly contract: label it "تخصیص خودکار زمان" and consistently show/hide the
   allocated interval everywhere (BVM:385,592 vs DVM:142 vs print), OR disable DayOnly until genuine
   day-capacity semantics are designed. Do not merely bypass conflicts (BVM:334–336). Preserve timed domain invariants.
**Files:** MAP, BVM (@334–336,385,592,594,796–813,851–857), DVM (@142,150,152–158,179–194), DV (@157–160), CVM (@851–853), QS (@46–47), SETTINGS/SS (defaults & RequiresDoctor), EVMODEL (VisitId), SQL (CreatedText).
**Acceptance:** no action/timeline/toast claims a message was sent; detail timeline matches persisted
timestamps/actor/reason; view-visit opens the linked visit or shows truthful unavailable; a 25%-paid
appointment shows its real invoice & 75% balance (or no money if unlinked); RequiresDoctor & DayOnly each
either change real behavior or are clearly labeled unavailable.
**Commit:** `fix(appointments): remove fabricated sms/price/timeline/nav; honest day-only & requires-doctor (A11,A12,A13,A18,A19,A28)`

---

### T10 — [ ] A14/A15/A16 (P2): complete & bounded export
**Why/Evidence:** EV:30 permits 186 days, EV:545–549 clamps to it, EV:566–575 fetches `missing.Min..Max`
in one request — but QUERY:15–16,43–47 rejects span >62 days (63 inclusive) → A14 (long/sparse export fails).
A15: EV:509–519 changes `_days` + loads without clearing/gating `CanExport`; EV:577–595 sets Warning then
still `Recompute()`; EV:714 ignores IsLoading/failure/completeness; EV:599–615 mutates cache regardless of
`_loadVersion`; EV:619–636 starts another unversioned picker-count fetch. A16: EV:44–45 retains `_byDay/_loaded`
for every visited month (EV:599–615 never evicts); EV:678–705 re-scans `inRange` per chip + per keystroke (EV:183).
**Do:**
1. **A14:** partition selected/missing dates into bounded contiguous windows (≤63 inclusive days each);
   fetch each within the existing QUERY guard, skipping gaps. Keep selected-day-count cap separate from
   the enclosing span. Document the inclusive-day contract (62-diff = 63 days).
2. **A15:** disable output immediately on selection change; track completeness per selection + all
   outstanding reads; capture generation before fetching and apply a whole returned batch only if valid;
   on failure retain a visible failed state + require retry; distinguish known-empty from unloaded;
   snapshot immutable export inputs only after completeness is established.
3. **A16:** bounded LRU/count-only picker cache; cancellation; row/byte output caps (reject oversized
   explicitly); precompute normalized row text; debounce search; compute facet counts + results in one
   pass; chunk output construction. Preserve EV:794 off-thread HTML.
**Files:** EV (@30,44–45,183,509–519,545–636,678–705,714,794), QUERY/SQL (reuse range guard).
**Acceptance:** 186 contiguous selected days AND two dates a year apart export their rows without relaxing
backend bounds; slow/failed/out-of-order reads can't yield an output labeled complete with missing rows;
repeated picker navigation has a fixed memory bound; oversized output rejected explicitly.
**Commit:** `fix(appointments): bounded chunked export with completeness gate & caches (A14,A15,A16)`

---

### T11 — [ ] A20/A21/A22/A27 (P2): scale without speculative rewrites
**Why/Evidence:** A20: SQL:35–42 materializes every `StartUtc` then GroupBy in memory (C04 wants an
aggregate). A21: BVM:330–344 refetches slots+conflicts+per-doctor busy-count+month counts on every slot
click, even start-only (BVM:655–658); BS:60 loads all same-day events for the conflict scan. A22: SQL:24–32
has no row limit; CVM:309–312 preloads 81 days; QV/HV/GRID realize all items; PANEL:25–61 measures every
child & can shrink cards to zero width; CVM:715–730 lane assignment ~O(N²). A27: SQL:123–125 uses
`.Contains` (no prefix range) + sort before Take(21); broad 2-char input may scan much of a large clinic.
**Do:**
1. **A20:** GROUP BY/COUNT in SQL over indexed UTC instants (or precomputed UTC local-day boundaries
   without wrapping the indexed predicate in a date function); merge buckets into clinic days in the handler. Return buckets, not one timestamp per row.
2. **A21:** seed form state before the initial availability load; one bounded availability snapshot with
   grouped doctor counts; start-only change updates selected-slot validation only; duration/doctor/day/month
   changes invalidate only their dependency; cache month aggregates by clinic/month/data-revision with a bound.
3. **A22:** query only active dates; paginate/virtualize queue/history with a finite viewport + independent
   totals; viewport-cull time-grid cards or give a bounded overflow/list; efficient interval lane allocation;
   preserve a readable minimum card width (PANEL:57–59). Batch UI application.
4. **A27:** keep normalization/cancellation/20+1 paging; bound normalized query length/term count; use an
   indexed prefix or a local search projection suited to substring/phone/id; keep stable-id lookup direct.
   (Measuring the generated SQL plan is deferred — note it; do not run heavy benchmarks.)
**Files:** SQL (@24–42,123–125), QUERY, BVM (@330–344,655–658), BS (@60), CVM (@309–312,715–730), QV/HV/GRID, PANEL (@25–61), ITEM.
**Acceptance:** SQL returns aggregate buckets (not one timestamp/row) with correct zone boundaries;
start-only selection doesn't reload roster/month aggregates; dense data doesn't realize every archive card
or render zero-width events; large-clinic search has a documented workload budget; id lookup stays uncapped.
**Commit:** `perf(appointments): sql aggregates, dependency-scoped invalidation, bounded lists & search (A20,A21,A22,A27)`

---

### T12 — [ ] A23/A24/A25/A26/A29 (P2): platform & UX consistency + journal metadata
**Why/Evidence:** A23: SS:36–45,120,137–138 does sync `File.*` before returning completed Tasks; QS:51–54
calls `GetTimeZone()` before first await; TZ:14–17 does sync EF `.FirstOrDefault()` → UI-thread blocking.
A24: CVM:117,310,595–596,768–769,925 / BVM:80,518 / DVM:143 use `DateTime.Today/Now` (device-local) while
queries use clinic tz/IClock → wrong today/history cutoff/live badge off-tz; stale now-markers near midnight/idle.
A25: AppointmentsLayout:12–25 fixes 07:00–22:00; GRID:145,178 fixes 1170px; ITEM:26–29 no clip; CVM:637,781
marks all Fridays closed; StaffMembership:62–68 permits 00:00–24:00 & any weekday; QV:136 fixes 4 doctor
columns; GRID:106 disables horizontal scroll. A26: SS:13,31 stores standalone JSON; SS:124–128 swallows
corrupt JSON silently; LocalStoreMaintenance:42–64 backs up DB only → custom types lost on restore.
A29: BS:20 calls `BuildCommandContextAsync(null,null,null,default)` for every command; PAYLOAD DTOs expose
no version; `CommandJournalBehavior:49–63` copies `ExpectedAggregateVersion` into the envelope but it's always null.
**Do:**
1. **A23:** make the settings store async with serialized writes + actionable error results (part of T05's
   port — ensure real async I/O, no `.Result`); hydrate clinic timezone asynchronously after readiness and
   cache by tenant/settings-revision (TZ); marshal one completed UI batch, no per-item dispatch.
2. **A24:** expose clinic-local current day/minute via a Contracts time/context seam backed by IClock +
   clinic tz; replace `DateTime.Today/Now` usages (CVM/BVM/DVM lines above) with it; add an activation-scoped
   minute/day-boundary timer that refreshes now/next badges & stops on navigation away. Keep Jalali formatting.
3. **A25:** derive grid bounds + working/closed state from the doctor/clinic schedule (or add overflow
   indicators); bind shared geometry instead of duplicate pixel constants; stop assuming Friday closed
   (CVM:637,781) — read the schedule; responsive/scrolling doctor columns with min readable width (QV:136,GRID:106).
4. **A26:** persist clinic configuration through the authorized local store (ties to T05's port) OR package/
   version/restore the JSON with DB backups (LocalStoreMaintenance); quarantine corrupt originals + surface
   an actionable warning (fix SS:124–128 silent swallow) + prevent silent overwrite until acknowledged.
5. **A29:** expose the read aggregate version via detail/update Contracts (T01 added `AggregateVersion` to
   the detail read model); pass it into `BuildCommandContextAsync(expectedVersion,...)` for move/cancel/check-in;
   keep a deliberate null only for new-aggregate booking. Use existing concurrency policy for stale versions. No last-write-wins merging.
**Files:** SS/port + TZ (A23), new time/context seam + CVM/BVM/DVM (A24), AppointmentsLayout/GRID/QV/CVM/ITEM/StaffMembership read (A25), SS + LocalStoreMaintenance (A26), BS/PAYLOAD/CommandContext (A29).
**Acceptance:** no UI-triggered settings/calendar op does sync disk/db on the dispatcher thread; changing
device tz doesn't change the clinic day & badges advance without interaction; 06:00/23:00 & Friday-working
appointments stay visible, 1/4/12 doctors readable; backup/restore reproduces custom types & corrupt settings
are recoverable with a warning; a mutation's envelope carries the caller-observed version, booking keeps new-aggregate null.
**Commit:** `fix(appointments): async io, clinic clock, schedule-derived geometry, durable settings, journal version (A23,A24,A25,A26,A29)`

---

### T13 — [ ] Final: delete this plan file
**Do:** delete ONLY `Plan/APPOINTMENTS_REMEDIATION_PLAN.md`. Do not touch other docs/roadmap/other plan files.
**Commit:** `chore(plan): remove appointments remediation plan after completion`

---

## 4. Notes for the executor
- Acceptance bullets are proposed checks, **not executed tests** — per the no-heavy-test directive, verify by static reasoning + targeted compile only.
- T01 is a prerequisite for T02/T03/T05/T07/T09/T12 (it adds the data fields). Do T01 first.
- If any task balloons into a heavy fix, split & push intermediate progress with a clear message.
- Keep Contracts the only Desktop↔backend seam; never leak EF/SQL/domain rules into Desktop.

