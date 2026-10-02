# Appointments audit — 2026-10-02

## Scope and phase gate

- Baseline: product source at `5a0d544`; static source review only. No build, tests, app run, benchmarks or database execution. Findings describe code behavior; timing, race frequency and rendered layout remain unmeasured.
- Authority: `backend-roadmap/PROGRESS.md:29–133,568–588`. Tracks A/B and C01–C11 shipped; C06 test debt remains; next phase is **D01, not started**. C04 was delivered without its own build/test verification; C11 scoped acceptance is not exhaustive appointment UI acceptance.
- Current obligation: working, authorized, durable, server-free local appointments over SQLite. Flow: Avalonia View → ViewModel → Contracts → Client.Services → Application dispatcher/permission/capability/transaction/journal → Domain + SQLite.
- Not defects: missing server/API/SQL Server (D01–D13), global slot settlement (D06), Datasync/transport/conflict reconciliation (E03–E06), online licensing (E07), release/performance certification (E08–E09), recurring appointments (explicitly not required by C04). No demand to implement these early. SMS delivery absence alone is not classified as a defect; false delivery claims are.
- Reviewed: calendar, queue, history, settings, booking, detail, export; their Contracts/services/queries/commands, slot/domain state, mappings, indexes, time-zone resolution, refresh and visit-completion dependencies. Excluded: HTML design reference, unrelated pages, historical audit/obsolete plan files.
- **29 findings: 7 P1, 22 P2; no substantiated P0.** P1 = wrong/lost business data, access-control bypass or unreliable primary flow. P2 = incorrect secondary behavior, resilience, scaling or maintainability risk. All apply to shipped local behavior, except A29's explicitly labeled downstream risk from current journal metadata.

## Path aliases

All paths are repo-relative; `Alias:line` resolves to the exact source location. Line references target the audited snapshot.

| Alias | Path |
|---|---|
| CVM | `src/Animora.Desktop/Pages/Appointments/AppointmentsCalendarViewModel.cs` |
| BVM | `src/Animora.Desktop/Pages/Appointments/BookingPanelViewModel.cs` |
| DVM | `src/Animora.Desktop/Pages/Appointments/BookingDetailViewModel.cs` |
| DV | `src/Animora.Desktop/Pages/Appointments/BookingDetailView.axaml` |
| EV | `src/Animora.Desktop/Pages/Appointments/Export/AppointmentExportViewModel.cs` |
| MAP | `src/Animora.Client.Services/Appointments/AppointmentsMapping.cs` |
| BS | `src/Animora.Client.Services/Appointments/AppointmentBookingService.cs` |
| QS | `src/Animora.Client.Services/Appointments/AppointmentsQueryService.cs` |
| SS | `src/Animora.Client.Services/Appointments/AppointmentSettingsService.cs` |
| SQL | `src/Animora.Persistence.Sqlite/Queries/AppointmentQueries.cs` |
| PROJ | `src/Animora.Persistence.Sqlite/Queries/SchedulingProjections.cs` |
| CMD | `src/Animora.Application/UseCases/Scheduling/SchedulingCommands.cs` |
| QUERY | `src/Animora.Application/UseCases/Scheduling/SchedulingQueries.cs` |
| APT | `src/Animora.Domain/Scheduling/Appointment.cs` |
| COMPLETE | `src/Animora.Application/UseCases/Visits/EncounterCommands.cs` |
| TZ | `src/Animora.Infrastructure.Local/Time/ClinicTimeZoneProvider.cs` |
| BASE | `src/Animora.Desktop/Shell/PageViewModelBase.cs` |
| NOTIFY | `src/Animora.Desktop/Services/DataChangeNotifier.cs` |
| CV | `src/Animora.Desktop/Pages/Appointments/AppointmentsCalendarView.axaml` |
| QV | `src/Animora.Desktop/Pages/Appointments/AppointmentsQueueTabView.axaml` |
| HV | `src/Animora.Desktop/Pages/Appointments/AppointmentsHistoryTabView.axaml` |
| GRID | `src/Animora.Desktop/Pages/Appointments/TimeGridSurface.axaml` |
| PANEL | `src/Animora.Desktop/Controls/TimeGridEventsPanel.cs` |
| ITEM | `src/Animora.Desktop/Pages/Appointments/AppointmentsItemViewModels.cs` |
| SETTINGS | `src/Animora.Contracts/Appointments/AppointmentSettingsModels.cs` |
| PAYLOAD | `src/Animora.Contracts/Appointments/AppointmentBookingModels.cs` |

## Findings

### A01 — P1 · Data completeness · Navigation never loads its selected range
- **Evidence:** CVM:309–312,1357–1375,1537–1543 loads only `today.AddDays(-40)`…`today.AddDays(40)`; `Step()` changes `_cursor` then calls `Render()`, not a query. Queue navigation CVM:1579–1583 likewise only renders. History CVM:923–979 derives every scope, including `all` and custom ranges, from `_all`. SQL:69–75's separate history query is not used here.
- **Failure:** existing appointments outside 81 days appear absent; distant queue days and historical selections show false empty states/totals. A valid future booking outside the preload disappears after save. Search covers the preload, not the selected period/database.
- **Fix:** load the visible calendar/queue date range on navigation; query history with server-side filters, pagination and independent aggregates. Keep C04's range guard per request; do not substitute the last-200 history query for an advertised complete archive. Expose loading/failure/loaded-empty distinctly.
- **Acceptance:** appointments at −120/+120 days remain visible when their date is selected; historical totals cover the requested scope, not the preload.

### A02 — P1 · Silent data loss · Edit accepts fields that the service discards
- **Evidence:** BVM:618–639,840–847 sends type, doctor, patient display fields, note and notification switches. PAYLOAD:74–88 declares them. BS:80–84 dispatches only `RescheduleAppointmentCommand(appointmentId, request.Date, request.StartMinutes, request.DurationMinutes)`; CMD:86–114 retains the old doctor and has no type/note/patient update.
- **Failure:** edit shows success while doctor/type/note/patient changes are lost. Slot availability is previewed for the new doctor but the write validates/moves the old doctor's appointment. Patient switching has no stable patient ID in the update payload.
- **Fix:** define the supported edit contract explicitly. Either disable immutable fields and label the operation reschedule, or add identity-based update fields, domain mutators and a single authorized/journaled update command; validate the final doctor/type/patient and slot inside the transaction. Never save display names as identity.
- **Acceptance:** every enabled edit field persists after reopening; new-doctor conflicts match the committed doctor's schedule.

### A03 — P1 · Lifecycle · Completing a visit does not complete its appointment
- **Evidence:** `src/Animora.Application/UseCases/Visits/StartVisitCommands.cs:100–104` calls `appointment.StartVisit(visit.Id)`. COMPLETE:205–238 completes the Visit, history, handoff and queue but has no appointment repository/transition. APT:230–231 defines `Complete()`; no production caller was found. SQL:85–86 keeps InVisit appointments in occupied slots; MAP:18–26 only recognizes Completed as a completed visit.
- **Failure:** a finished encounter leaves its appointment InVisit indefinitely: inaccurate history/outcome, occupied slot, rejected rescheduling and incorrect downstream completion counts.
- **Fix:** in the same completion UoW, resolve `visit.AppointmentId`, verify its linkage/state and apply `Appointment.Complete()`; propagate failure atomically. Use one completion path, not a later UI correction.
- **Acceptance:** finishing an appointment-backed visit commits Visit completion, queue closure and Appointment.Completed together; rollback preserves all three.

### A04 — P1 · State contract · Check-in, in-visit and no-show are misrepresented
- **Evidence:** MAP:14–26 maps Scheduled→Pending, Completed/NoShow→Done, everything else→Confirmed. `src/Animora.Desktop/Pages/Appointments/AppointmentsPresentation.cs:84–99` labels Done as admitted. CVM:794–795 counts Done as checked-in; DVM:155–158 derives admission timeline from Done. DV:177–193 offers check-in whenever `!IsDone`; edit/cancel remain unconditional at DV:194–210.
- **Failure:** check-in still displays Confirmed and is omitted from admitted counts; no-show displays admitted and offers a visit action; InVisit/cancelled rows expose inappropriate check-in/edit actions. The backend rejects illegal writes, but UI affordances and summaries are wrong.
- **Fix:** carry the full lifecycle or explicit `IsCheckedIn`, visit outcome and `CanCheckIn/CanMove/CanCancel/CanOpenVisit` fields. Keep admission separate from visit completion/no-show. Gate actions by state and capability; retain Application authorization/domain validation.
- **Acceptance:** each of the seven domain states has accurate labels/counts/timeline and legal actions; no-show never implies attendance.

### A05 — P1 · Record visibility · Unassigned/inactive doctors hide appointments or cause bad lookup
- **Evidence:** APT:98–99 permits `DoctorUserId = null`; MAP:22 converts null to `""`. SQL:145–151 returns active Doctor memberships only. CVM:325–329 builds active IDs solely from that roster; CVM:449–455 excludes every other doctor ID. CVM:391–392 uses `.First(...)`; DVM:132–133 falls back to `_doctors[0]`.
- **Failure:** unassigned appointments and appointments of removed/inactive doctors disappear from calendar/queue/history. Detail may claim a different doctor, or throw if the roster is empty. A removed type similarly falls back to the first catalog type in detail, despite the calendar's neutral fallback.
- **Fix:** distinguish roster choices from historical event identity. Preserve event doctor ID/display name and type snapshot; add unassigned/inactive buckets and neutral missing-reference fallbacks. Never infer the first doctor/type as the actual assignment.
- **Acceptance:** an unassigned or formerly active doctor's appointment remains accessible and correctly labeled even with zero active doctors.

### A06 — P1 · Refresh concurrency · A write starts competing destructive reloads
- **Evidence:** CVM:124 subscribes to Visits and Patients; CVM:1531–1543 notifies both, then independently fires `RefreshAfterChangeAsync()`. NOTIFY:18,35–40 dispatches synchronously on the UI thread; BASE:95–102 starts/repeats tracked loads. Both CVM:303–312 and 1539–1542 clear/mutate shared `_all` around awaits; only the tracked path has a load guard/status.
- **Failure:** one write schedules overlapping reads/renders; interleaving can produce partial/duplicated/mixed snapshots. A failure after `Clear()` destroys the backing snapshot while old visuals remain. The untracked refresh cannot report stale/error through PageLoadStatus.
- **Fix:** one coalescing refresh coordinator for external notifications and local writes. Build a complete immutable snapshot off shared state, then commit once on the UI thread; use a generation/cancellation guard, retain the last-good snapshot on failure, and mark exactly one pending reload.
- **Acceptance:** a write with simultaneous Visits/Patients notifications performs one coherent apply; injected read failure preserves previous data and exposes retry.

### A07 — P2 · Cached UI · Returning to calendar can reveal stale visuals
- **Evidence:** CV:442–484 caches tab views. CVM:358–370 and 1545–1548 render only the active tab after reload/write. CVM:1303–1325 renders queue/history on selection, but does nothing for `key == "cal"`.
- **Failure:** refresh while queue/history is selected updates `_all` and that tab only; returning to the cached calendar displays the previous events until another calendar action renders it.
- **Fix:** render the selected calendar on activation, or maintain per-tab snapshot-version/dirty markers and rebuild only when its version changed.
- **Acceptance:** after a write from queue/history, switching to calendar displays the new data without stepping/refreshing.

### A08 — P2 · Async correctness · Availability reloads have no latest-selection guard
- **Evidence:** BVM:330–344 reads mutable `_doc/_date/_dur/_start/_id` across sequential awaits and writes `_slots/_conflicts/_busyByDoctor`. Separate doctor/type/date/duration/slot commands call this method; there is no shared cancellation/generation or captured request snapshot. Patient search already has its own version/cancellation protection at BVM:400–458, but availability does not.
- **Failure:** changing different controls while a previous reload is in flight can combine old slot results with current labels, conflicts and date badges. Same-command serialization does not serialize different command instances.
- **Fix:** capture one immutable availability request, cancel/obsolete previous requests and atomically apply only the current generation. Disable submit while the current availability result is pending/failed; propagate cancellation through Contracts.
- **Acceptance:** rapid doctor/date/duration changes never apply an earlier selection's result.

### A09 — P2 · Validation · Conflict preview disagrees with the authoritative slot rule
- **Evidence:** BS:57–63 loads every doctor's same-day calendar, then filters Pending/Confirmed locally. QUERY:118–125 loads busy slots without an edit exclusion; SQL:78–90 includes CheckedIn/InVisit and prior-day candidates. BVM:602–613 sets `ShowFreeOk` from `_conflicts.Count == 0` and `CanSubmit = HasSelectedPatient` only.
- **Failure:** false free indication outside working hours, on nonworking days or invalid duration; occupied self-slot appears unavailable while editing. Prior-day overlap candidates can be omitted from preview. Lifecycle compression (A04) makes this worse. Transactional booking remains authoritative and prevents a false-preview success.
- **Fix:** one availability/conflict application query using `SlotConflictPolicy`, working hours, full lifecycle and optional excluded appointment ID. Return selected-slot validity and structured conflicts; drive submit/free labels from that result. Keep the in-transaction re-check.
- **Acceptance:** preview and write agree for self-edit, checked-in/in-visit, adjacent/overlapping slots, nonworking days and out-of-window start times.

### A10 — P2 · Error handling · Expected business rejection becomes an uncaught UI command failure
- **Evidence:** BS:16–24 and QS:15–23 reduce failed Results to `InvalidOperationException(result.Error.Code)`, dropping Detail/field information. BVM:817–862 and DVM:162–189 have no local result/error handling; submit checks only patient selection, not conflict/current validity. Page loads use BASE's error tracking, but these sheet/drawer writes do not.
- **Failure:** ordinary slot conflict, state rejection, forbidden operation or store error cannot render actionable conflict/validation details or a recoverable form state. Error escalation depends on the global handler; no runtime crash claim is made here. Controls are not centrally locked across competing mutation commands.
- **Fix:** expose typed success/error outcomes at the Contracts seam (or a structured exception adapter), preserve code/detail/fields, and handle writes locally with busy/finally, localized feedback and retained user input. Separate write success from a subsequent detail-read failure to avoid implying an already committed booking was not saved.
- **Acceptance:** conflict/permission/store failures keep the form open, preserve data and show the relevant recovery action; no success toast precedes a failed write.

### A11 — P2 · Truthful UX · SMS controls/timeline claim delivery without any delivery operation
- **Evidence:** BVM:851–857 sends notification flags and displays “confirmation SMS sent”; BS:72–76 ignores these flags. DVM:179–181 only emits a toast for SendSms; DVM:187 claims owner notification after cancel. DV:157–160 hardcodes the confirmation-sent timeline. SETTINGS:24–30 defaults both switches on.
- **Failure:** staff assume confirmation/reminders/cancellation notices were delivered, regardless of network/provider/outcome. Per-booking notification preferences are not retained by the booking command.
- **Fix now:** remove delivery claims; disable/label unavailable actions and show only observed delivery states. Clarify whether current switches are local defaults only. Future transport integration may add delivery receipts/persisted intent in its owning scope; do not invent receipts today.
- **Acceptance:** with no delivery integration, no action, timeline or success toast says a message was sent.

### A12 — P2 · Financial data · Appointment prices, balances and invoice fields are fabricated/empty
- **Evidence:** MAP:23–26 always emits `Total = 0`, Invoice.None and Payment.None. BVM:594 invents price with `_dur * 12000L`; DVM:150 displays the mapped zero as a monetary price. CVM:851–853 derives receivables for Partial as `Math.Round(a.Total / 2.0)`.
- **Failure:** booking estimate conflicts with detail/history; invoiced appointments always appear invoice-free; payment filters and revenue cards mislead. A partial payment is not necessarily half. C07/C08/C09 are delivered, so this is not “billing belongs to a later backend phase.”
- **Fix:** project actual visit/invoice linkage and authoritative invoice total/paid/balance/currency through Application read models. Remove the arbitrary price or label a genuinely sourced tariff estimate. Use actual outstanding balance, not a percentage heuristic; aggregate for the selected history scope in SQL/facts.
- **Acceptance:** a 25%-paid appointment shows its real invoice and 75% balance; booking/detail/history do not invent or contradict financial amounts.

### A13 — P2 · Navigation · View visit is a toast-only action
- **Evidence:** DVM:192–194 implements `ViewVisit()` as `_toast.Show(...)`; DV:185–193 presents it as a real visit-record action. PROJ:17–27/MAP:21–26 do not carry `Appointment.VisitId` into the Contracts event.
- **Failure:** an appointment-backed visit exists locally but the action never navigates to it; no-show can expose the same action due to A04.
- **Fix:** carry stable VisitId; use the existing navigation/record presentation seam and state/capability gates. If no visit is linked, show a truthful unavailable state rather than pretending to navigate.
- **Acceptance:** the action opens the linked visit, never an unrelated patient visit or a toast-only substitute.

### A14 — P2 · Export contract · Supported export ranges exceed the query limit
- **Evidence:** EV:30 permits 186 days; EV:545–549 clamps to that limit. EV:566–575 fetches `missing.Min()`…`missing.Max()` in one request, including arbitrarily distant individually picked dates. QUERY:15–16,43–47 rejects end−start >62 days.
- **Failure:** a fresh long-range export or two sparse dates far apart fail with range-too-wide. The cap on selected day count does not bound their enclosing span.
- **Fix:** partition selected/missing dates into bounded contiguous windows; fetch each within the existing query guard, skipping unnecessary gaps. Keep selection/date and row/byte export limits separate. Define the inclusive-day contract explicitly: the current handler allows a 62-day difference, i.e. 63 inclusive days.
- **Acceptance:** 186 contiguous selected days and two dates a year apart export their selected rows without relaxing backend query bounds.

### A15 — P2 · Export integrity · Pending/failed loads can still enable stale or incomplete output
- **Evidence:** EV:509–519 changes `_days` and fires a load without clearing/gating the previous CanExport. EV:577–595 sets Warning on failure, then still Recompute(); EV:714 ignores IsLoading/load failure/completeness. EV:599–615 mutates the cache regardless of `_loadVersion`; version checks only gate later UI updates. EV:619–636 starts another unversioned fetch for picker counts.
- **Failure:** date change may leave print/save enabled with previous `_matches`; failed reads plus IncludeEmptyDays can export an apparently valid empty/partial report. Older overlapping requests may replace newer cache rows even when their final Recompute is suppressed. Clearing an outer IsLoading flag can race picker reads.
- **Fix:** disable output immediately on selection change; track completeness per selection and all outstanding reads. Capture generation before fetching; apply a whole returned cache batch only if valid. Failure must retain a visible failed state and require retry; distinguish known-empty from unloaded. Snapshot immutable export inputs only after completeness is established.
- **Acceptance:** slow/failed/out-of-order reads cannot produce an output labeled complete with missing or previously selected rows.

### A16 — P2 · Export scalability · Date/row caches and per-keystroke scans are unbounded
- **Evidence:** EV:44–45 retains `_byDay/_loaded` for every visited picker month; EV:599–615 never evicts. MaxDays limits a selection, not cache size or row count. EV:678–705 normalizes interpolated row text and scans `inRange` separately for every doctor/type/status chip and final results; EV:183 recomputes on every search keystroke.
- **Risk:** prolonged month browsing retains all fetched event payloads; dense exports create large lists/HTML and UI-thread normalization/scans. Work roughly scales with rows × filter-chip count; no measured threshold is asserted.
- **Fix:** bounded LRU/count-only picker cache, cancellation and row/byte output limits; precompute normalized row text, debounce search and calculate facet counts/results in one pass. Chunk output construction where appropriate. Preserve EV:794's off-thread HTML generation.
- **Acceptance:** repeated picker navigation has a fixed memory bound; oversized outputs are rejected explicitly rather than exhausting memory.

### A17 — P1 · Authorization · Appointment settings bypass the existing permission/capability pipeline
- **Evidence:** SS:36–99 reads/writes directly to JSON; no dispatcher, current-user or permission/capability dependency. CVM:1201–1260 calls these mutators directly; CV:487–490 exposes the settings tab. `src/Animora.Domain/Identity/DefaultRolePermissions.cs:23–46` gives Doctor/Receptionist SettingsRead, not SettingsWrite; `PermissionCodes.cs:155–158` defines both permissions.
- **Failure:** an appointment-reading doctor/receptionist can change clinic-wide mode, notification defaults and type catalog without settings.write. File storage being non-journaled does not exempt settings from local authorization.
- **Fix:** move reads/writes behind authorized Application settings use cases, non-journaled if appropriate; enforce settings.read/write plus the local capability policy. Bind UI visibility/enabled state to capability; retain authoritative checks below the UI.
- **Acceptance:** read-only roles cannot mutate settings through either the tab or direct Contracts calls; manager can.

### A18 — P2 · Unimplemented option · RequiresDoctor is stored but ignored by booking
- **Evidence:** SETTINGS:17–22 persists RequiresDoctor; CVM:1245 supplies it. QS:46–47 builds AppointmentType without the flag; BVM:308–310 always chooses a doctor. CMD:53–55 requires DoctorId and CMD:26–31 always validates Doctor working hours.
- **Failure:** a type marked “doctor not required” still requires an active doctor and consumes doctor slot capacity; the visible option has no functional effect.
- **Fix:** either remove/disable this option pending an explicit supported policy, or carry it through Contracts/read models and implement authorized resource/slot rules for doctor-optional bookings. Do not silently treat a fake doctor ID as an unassigned resource.
- **Acceptance:** toggling the option changes supported behavior, or the UI clearly identifies it as unavailable.

### A19 — P2 · Mode semantics · DayOnly actually allocates hidden timed capacity
- **Evidence:** SETTINGS:9 describes date-only booking with no clock time. BVM:334–336 suppresses conflict display; BVM:796–813 chooses the first free timed slot and rejects a full doctor's day. BVM:385,592 hides time; DVM:142 and print/detail data retain that actual interval.
- **Failure:** staff see “no specified time” but booking/move can fail on hidden slot capacity; detail and booking communicate different semantics. This is a shipped-option inconsistency, not a request for recurring appointments or a new server feature.
- **Fix:** choose one honest current contract: label the mode “automatic time allocation,” explain capacity and consistently hide/show the allocated time, or separately design genuine day-capacity semantics. Preserve current timed domain invariants until that decision; do not merely bypass conflicts.
- **Acceptance:** mode help, summary, error, detail and export describe the same booking/capacity policy.

### A20 — P2 · Query efficiency · Day counts materialize every appointment start
- **Evidence:** SQL:35–42 uses `.Select(a => a.StartUtc).ToListAsync(ct)` then GroupBy in memory; QUERY:61–64 groups again into local days. C04:40–43,160 requires an aggregate query, not materialized starts.
- **Risk:** badge computation allocates O(N) timestamps and transfers every row in the range instead of a small count result. A 42-day month bounds days, not appointment density.
- **Fix:** group/count in SQL over indexed UTC instants and merge buckets into clinic days, or aggregate by precomputed UTC local-day boundaries without wrapping the indexed predicate in a SQL date function. Verify actual statement/projection counts when execution is allowed.
- **Acceptance:** SQL returns aggregate buckets, not one timestamp per appointment; local-day counts retain correct zone boundaries.

### A21 — P2 · Read amplification · Every slot click reloads roster counts and month badges
- **Evidence:** BVM:330–344 always fetches slots, conflicts, one sequential busy-count query per doctor, then month day counts; BVM:655–658 does so for a start-only change. BS:60 loads all same-day events for the conflict scan. BVM:300–315 plus 892–910 performs initial and then seeded availability loads before showing the panel.
- **Risk:** a start-only click performs at least slots + conflicts + D busy-count calls + month-count call, plus repeated timezone resolution. Opening also repeats work for the unseeded date. Delay grows with doctor roster size and appointment density.
- **Fix:** seed form state before initial load; query a bounded availability snapshot with grouped doctor counts. Start changes update selected-slot validation only; duration/doctor/day/month changes invalidate only their dependencies. Cache month aggregate results by clinic/month/data revision with a bound; avoid merely parallelizing D scope/database calls.
- **Acceptance:** start-only selection does not reload roster/month aggregates; panel opens with one seeded snapshot request.

### A22 — P2 · Rendering scalability · Unbounded event lists are eagerly realized and packed
- **Evidence:** SQL:24–32 has no row limit; CVM:309–312 preloads 81 days. QV:133–145 and its nested card ItemsControl, HV:261–367 nested group/row ItemsControls, GRID:175–180 custom panel realize all supplied items. PANEL:25–35,49–61 measures/arranges every child. CVM:715–730 uses `lanes.FindIndex(...)` for each clustered event.
- **Risk:** one dense day/week/history can create thousands of visuals. In overlapping multi-doctor week clusters, lane assignment can approach O(N²); as lane count rises, PANEL:57–59 makes cards narrower and can reduce width to zero. Lazy tabs reduce first activation only, not active-list cost.
- **Fix:** query only active dates; paginate/virtualize queue/history with a finite viewport, independently aggregate their totals. For time grids, viewport-cull cards or provide a bounded overflow/list representation; use efficient interval lane allocation for dense clusters and preserve readable minimum width. Batch UI application.
- **Acceptance:** dense data does not realize every archive card or render zero-width inaccessible events; visual count and packing cost have explicit bounds.

### A23 — P2 · UI responsiveness · Task-shaped APIs execute synchronous I/O
- **Evidence:** SS:36–45 locks and calls File.ReadAllText/File.WriteAllText/File.Move at SS:120,137–138 before returning completed Tasks. QS:51–54 calls `GetTimeZone()` before its first await; TZ:14–17 performs synchronous EF `.FirstOrDefault()`. Handlers and counts call the same resolver repeatedly.
- **Risk:** UI entry paths can block on disk/database contention; an async signature/Task.Yield does not move this work off-thread. Settings-write failures can escape a property setter via CVM:1208. Duplicate zone queries inflate A21.
- **Fix:** async settings repository with serialized writes and actionable error results; asynchronously hydrate clinic timezone after readiness and cache it by tenant/settings revision. Preserve scope isolation; never add `.Result`/blocking waits. Avoid per-item dispatch; marshal one completed UI batch.
- **Acceptance:** UI-triggered settings/calendar operations do not synchronously read/write disk or execute a database query on the dispatcher thread.

### A24 — P2 · Time correctness · Device-local/frozen time differs from clinic time
- **Evidence:** queries use clinic timezone/IClock at QUERY:94–99 and MAP:12–13. CVM:117,310,595–596,768–769,925; BVM:80,518; DVM:143 use DateTime.Today/Now. CVM computes now-marker/live badges only during render; no appointment activation timer exists. EV:41 captures `_today` once for the dialog.
- **Failure:** devices outside clinic timezone show/select the wrong today/history cutoff/live badge; near midnight or during a long idle session now markers/queue highlights become stale. Reload can query the wrong device-day range.
- **Fix:** expose clinic-local current day/minute via a Contracts time/context seam backed by IClock + clinic timezone. Refresh time-derived visible state with an activation-scoped minute/day-boundary timer; stop it on navigation away. Preserve Jalali formatting in Desktop.
- **Acceptance:** changing device timezone does not change the clinic day; now/next badges and date boundaries advance without user interaction.

### A25 — P2 · Layout correctness · Grid bounds/closed days ignore supported working hours
- **Evidence:** `src/Animora.Desktop/Pages/Appointments/AppointmentsLayout.cs:12–25` fixes 07:00–22:00; GRID:145,178 fixes 1170px. ITEM:26–29 computes card offsets without clipping to the visible window. CVM:637,781 marks all Fridays closed. `src/Animora.Domain/Identity/StaffMembership.cs:62–68` permits 00:00–24:00 and any weekday mask. QV:136 fixes four doctor columns; GRID:106 disables horizontal scrolling.
- **Failure/risk:** valid early/late appointments can lie outside the calendar's visible grid. Friday-working doctors are displayed against a closed-day assumption. Large doctor rosters compress day columns; queue retains a four-column design regardless of count/window width. Rendered clipping/responsiveness has not been measured.
- **Fix:** derive grid bounds and working/closed state from the relevant doctor/clinic schedule or provide overflow indicators; bind shared geometry rather than duplicate pixel constants. Use responsive columns or horizontal scrolling with minimum readable widths.
- **Acceptance:** 06:00/23:00 and Friday appointments remain visible; 1/4/12 doctors have readable, reachable columns at supported window sizes.

### A26 — P2 · Durability · Custom type metadata is outside database backup and silently resets
- **Evidence:** SS:13,31 stores a standalone `appointment-settings.json`; SS:124–128 catches unreadable/corrupt JSON and caches defaults without reporting/quarantine. `src/Animora.Persistence.Sqlite/Maintenance/LocalStoreMaintenance.cs:42–64` backs up the database only (`source.BackupDatabase(destination)`). Appointment rows retain custom type IDs but labels/colors/duration/defaults exist only in this file.
- **Failure:** restoring a clinic database on a new installation loses custom type definitions/mode/preferences; unreadable preferences appear reset and the next write can overwrite the original file. Calendar's neutral fallback prevents a crash, not metadata loss.
- **Fix:** persist clinic configuration through the authorized local store, or explicitly package/version/restore the JSON with backups. Quarantine damaged originals, surface recovery status, and prevent silent overwrite until the outcome is acknowledged. Define clinic configuration versus user/device preference scope.
- **Acceptance:** backup/restore reproduces custom type metadata and mode; corrupt settings remain recoverable and generate an actionable warning.

### A27 — P2 · Search scaling · A bounded result count does not bound patient search work
- **Evidence:** SQL:123–125 ORs `.Contains(term)` over pet/owner SearchKey and Pet.Id; SQL:136–140 sorts joined matches before Take(≤21). Each whitespace term adds another predicate. Normalization/minimum two characters and result limits are present.
- **Risk:** substring predicates do not provide a B-tree prefix range; broad two-character input may scan much of a large clinic and sort matches despite returning 21 rows. Cancellation/result bounds are useful, but do not prove indexed search or constant query cost.
- **Fix:** retain normalization, cancellation and 20+1 UI paging; measure generated SQL/query plan when allowed. Use bounded normalized query length/term count and an indexed prefix or local full-text/search projection appropriate to required substring/phone/ID behavior. Do not replace it with a patient preload.
- **Acceptance:** large-clinic search has a documented query plan and workload budget; stable-ID lookup remains direct and uncapped by search results.

### A28 — P2 · Detail integrity · Timeline/provenance is not backed by stored events
- **Evidence:** SQL:52–56 constructs `AppointmentDetailReadModel(Map(row), "")`; PROJ:17–27 omits CreatedByUserId, CheckedInUtc, CancelReason and VisitId. DVM:152–158 renders empty creation text and a two-state guessed timeline. DV:157–160 contains a fabricated SMS event (also A11).
- **Failure:** detail cannot explain who booked/check-in/cancelled or the actual cancellation reason; cancelled rows can say “awaiting admission.” Appointment code is simply row.Id in MAP:23, not a distinct human record code.
- **Fix:** project actual provenance/linkage/timestamps and build the timeline from persisted state/audit/delivery facts. Render absent information as unknown/unavailable, not an invented event; explicitly label any full technical ID used as code.
- **Acceptance:** booking/check-in/cancel detail matches persisted timestamps/reason/actor, without hardcoded successful events.

### A29 — P2 · Journal metadata · Appointment mutations always omit expected aggregate version
- **Evidence:** BS:20 calls `BuildCommandContextAsync(null, null, null, default)` for every command; PAYLOAD detail/update DTOs expose no version. `src/Animora.Application/Behaviors/CommandJournalBehavior.cs:49–63` copies `commandContext.ExpectedAggregateVersion` into the envelope. `backend-roadmap/phases/PHASE-C04-appointments-calendar-booking.md:67–72,154–157` requires this metadata for scheduling commands.
- **Current defect / downstream risk:** the delivered journal records no caller-observed version for move/cancel/check-in. This is a current C04 metadata gap, **not a claim that unimplemented D06/E06 validation is broken**, and does not negate today's transaction-scoped slot check.
- **Fix:** expose the read aggregate version through detail/update Contracts and pass it into the existing command-context/journal seam. Handle missing/stale versions using the existing domain/concurrency policy; retain a deliberate null only for new aggregates. Do not introduce last-write-wins slot merging.
- **Acceptance:** a mutation's durable envelope carries the version observed by its caller; creation retains the documented new-aggregate behavior.

## Existing safeguards — preserve, do not report as missing

- Command authority: CMD:20–34 checks duration, active-doctor hours/weekdays and `SlotConflictPolicy`; `TransactionBehavior.cs:27–32` encloses journal + handler in a UoW. Commands have permission attributes. Preview defects do not imply the transactional slot invariant is absent.
- Persistence: explicit `.AsTracking()` in `Repositories/AppointmentRepository.cs:10–11` for mutations; AsNoTracking projections for reads; tenant-filtered UTC predicates. `Configurations/Scheduling/AppointmentConfiguration.cs:12–16` has doctor/start, start/doctor, state/start and patient indexes. No missing-index assertion without query-plan evidence.
- Read bounds: QUERY:15–16 bounds ranges; SQL:74 bounds the standalone terminal-history query to 200. These do not bound row density or satisfy UI “all history” semantics.
- Patient picker: BVM:400–458 cancels/debounces/version-checks requests; SQL:108–110 rejects short unscoped searches and SQL:139 caps output to 21. BS:93–100 resolves stable patient ID directly, avoiding duplicate-name/old-500-row-window bugs.
- UI: CVM:399–435 preindexes date/doctor/type/search text; ReplaceAll batches calendar/queue/history notifications. CV:442–484 lazily creates tabs; month preview limits cards to three (CVM:686–693). Keep these while fixing A07/A22. Week/day search deliberately dims nonmatches; do not label this alone a broken filter.
- Export: `AppointmentPrintDocument.cs:561` uses WebUtility.HtmlEncode; HTML construction is off-thread (EV:794). No observed HTML-injection defect. Brush caching and compiled bindings exist; do not replace with per-row mutable brush allocation/runtime reflection.
- NoTracking update-loss, recurrence support and remote multi-device booking races are not asserted: mutation tracking is explicit; recurrence is excluded; global scheduling validation belongs to D06/E06.

## Remediation architecture and order

1. **Correctness/security first:** A01–A06, A17. Keep local UoW/journal/permission authority. Repair visit→appointment completion atomically; make the edit contract honest; preserve full state, identities, missing-reference metadata and caller version (A29).
2. **One snapshot/refresh system:** split calendar, queue, history and booking coordination responsibilities from the 1,772-line CVM. Contracts remain the only Desktop data seam. Use typed filters/read models, cancellable bounded queries, immutable snapshots, latest-generation application, last-good state and activation/version-aware lazy rendering (A07–A10).
3. **Truthful actions/data:** remove fabricated SMS/price/timeline/navigation outcomes (A11–A13,A18–A19,A28). Add actual local invoice/visit projections where the delivered local backend already supplies them; clearly label unavailable delivery features rather than implementing future networking.
4. **Complete, bounded export:** bounded date chunks + completeness gate + atomic cache batches, row/byte caps, bounded count caches and immutable output snapshots (A14–A16).
5. **Scale without speculative rewrites:** SQL aggregates, dependency-based invalidation, grouped counts, indexed measured search, bounded/virtualized lists and efficient dense-lane handling (A20–A22,A27). Preserve UTC range indexes, batch collections, lazy tabs and existing picker safeguards.
6. **Platform/UX consistency:** authorized durable settings, asynchronous I/O, clinic clock/timers, schedule-derived geometry and responsive doctor columns (A23–A26). No server/sync stack change, no technology upgrade, no unrelated refactor.

Acceptance bullets above are proposed follow-up checks, **not executed tests**. This report changes documentation only; roadmap progress and product code remain untouched.
