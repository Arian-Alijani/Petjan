# Phase 14 — Appointments — Booking Panel

**Band:** M · **Depends on:** 13 · **Satisfies:** R-4, R-5, R-18

## Read before starting
- `CONVENTIONS.md` §2, §3, §4a (Fidelity Protocol — MANDATORY), §5, §6 (Definition of Done incl. fidelity gate + visual verification), §7a
- **Reference CSS (MANDATORY — the source of every measurable value, per §4a rule 3 / §8): `styles.css` + `appointments.css` + `booking.css` (booking/detail side panel, pickers, mini-calendar + slot grid, side summary).** Read the sections these file(s) define for every element this screen renders and transcribe the exact paddings, gaps, radii, borders, font sizes/weights, grid/column templates, fixed/min/max sizes, fills and shadows — reusing Phase 02 tokens where a value equals a token. Do not eyeball or approximate.
- `Animora-UI/assets/appointments-panel.js` — full file (574 lines): the
  booking/detail side panel — patient list/picker, appointment type,
  doctor picker, calendar mini-widget + slot picker, date/extra fields,
  side summary, and the paint/render orchestration functions.

## Goal
Build the booking/detail panel that Phase 13's calendar hands off into:
view an existing appointment's detail, or create a new booking through
the patient → type → doctor → date/slot flow.

## Fidelity (MANDATORY — read `CONVENTIONS.md` §4a in full)
- **Rebuild every element the reference screen renders — nothing dropped, simplified, merged, summarized, or deferred.** Every card, panel, header, stat/KPI box, chart/donut/gauge/ring/sparkline, badge/pill, tab, sub-nav, table (with the SAME columns in the SAME order), list row, avatar, icon, button, chip, empty-state and caption must exist, in the same place, order, grouping and count as the reference.
- **No visible reference element may be declared "out of scope," "nice-to-have," or "optional" here.** The only permitted simplification is *motion*: an animated value may render at its final/current value instead of animating — the element itself still exists and shows the correct value (§4a rule 6).
- **Exact measurable values** come from the Reference CSS above (§4a rule 3), not from judgement. Proportional CSS grids map to star-sized `Grid` columns/rows; fixed px sizes map to fixed sizes — transcribed, never re-guessed.
- **Exact Persian text** for every label/title/subtitle/column header/button/empty-state/tab, character-for-character; all numbers and dates through the Phase 04 Persian digit / grouping / Jalali helpers (§4a rule 4). State colors/pills map 1:1 to Phase 02 semantic tokens (§4a rule 5).
- **Backend-ready:** the ViewModel binds only to the `Animora.Contracts` interface(s); the mock is the sole swap point for a future real backend — no sample data, mock call, or design-time branch leaks into a View or the ViewModel's public shape (R-18).

## Contracts to add (`Animora.Contracts/Appointments/`)
- Extend with `IAppointmentBookingService` —
  `Task<AppointmentDetail> GetDetailAsync(appointmentId)`,
  `Task<IReadOnlyList<TimeSlot>> GetAvailableSlotsAsync(doctorId, DateOnly date)`,
  `Task<AppointmentEvent> CreateAsync(NewAppointmentRequest)`,
  `Task UpdateAsync(appointmentId, UpdateAppointmentRequest)`,
  `Task CancelAsync(appointmentId)`.
- `AppointmentDetail`, `TimeSlot`, `NewAppointmentRequest`,
  `UpdateAppointmentRequest` DTOs (mirror `detailHtml`/`conflicts`/
  `slotsInner`'s fields).

## Mock to add
- Extend `MockAppointmentsQueryService` (or a sibling
  `MockAppointmentBookingService` sharing the same in-memory event store
  from Phase 13) with the above methods; generate a few available/booked
  slots per doctor/day for a believable picker.

## View/ViewModel tasks
1. `Pages/Appointments/BookingPanelViewModel.cs`: step-like state (patient
   selection/search — mirrors `petList`/`listInner`/`addForm`;
   appointment type — mirrors `typeInner`; doctor — mirrors `docInner`;
   date + slot picker — mirrors `calInner`/`slotsInner`/`dateInner`;
   extra notes — mirrors `extraInner`), a `sideInner`-equivalent summary
   property, commands `SelectPatient`, `SelectType`, `SelectDoctor`,
   `SelectDate`, `SelectSlot`, `Save`, `Cancel`.
2. `Pages/Appointments/BookingPanelView.axaml(.cs)`: opened as a Phase 03
   drawer (`IDialogService.ShowDrawer`) from Phase 13's `OpenBooking`
   command, laid out as the reference's side-panel sections
   (patient/type/doctor/calendar/slots/extra + a running summary sidebar).
3. Wire Phase 13's `OpenBooking(AppointmentEvent?)` (nullable — null means
   "new booking") to open this panel for real instead of the placeholder
   toast; on Save/Cancel, close the panel and refresh Phase 13's visible
   range.

## Out of scope
- None beyond what's listed — this phase completes the Appointments
  screen entirely.

## Acceptance criteria
- App builds/runs; clicking an existing event from the calendar opens this
  panel pre-filled with its detail and allows edit/cancel; clicking "new
  booking" walks through patient→type→doctor→date/slot→save and the new
  event appears back on Phase 13's grid.
- **Visual verification (§6.7 + §7a) performed** and recorded in `PROGRESS.md`: the rebuilt screen was compared element-by-element against the reference and matches (same boxes/order/sizes/colors/Persian text); every measurable value traces to the Reference CSS; no reference element is missing. Building and running alone does NOT satisfy this phase.

## PROGRESS.md update
Mark Phase 14 done; advance "Next phase" to 15; update "Repo state."
