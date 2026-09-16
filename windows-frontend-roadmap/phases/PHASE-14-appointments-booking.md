# Phase 14 — Appointments — Booking Panel

**Band:** M · **Depends on:** 13 · **Satisfies:** R-4, R-5, R-18

## Read before starting
- `CONVENTIONS.md` §2, §3, §5
- `Animora-UI/assets/appointments-panel.js` — full file (574 lines): the
  booking/detail side panel — patient list/picker, appointment type,
  doctor picker, calendar mini-widget + slot picker, date/extra fields,
  side summary, and the paint/render orchestration functions.

## Goal
Build the booking/detail panel that Phase 13's calendar hands off into:
view an existing appointment's detail, or create a new booking through
the patient → type → doctor → date/slot flow.

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

## PROGRESS.md update
Mark Phase 14 done; advance "Next phase" to 15; update "Repo state."
