# Phase 13 — Appointments — Calendar Views

**Band:** M · **Depends on:** 05 · **Satisfies:** R-4, R-5, R-6, R-18

## Read before starting
- `CONVENTIONS.md` §2, §3, §5
- `Animora-UI/appointments.html` (full)
- `Animora-UI/assets/appointments.js` — full file (1210 lines): Jalali
  date helpers (`jp`/`jMonthStart` — cross-check against Phase 04's
  `JalaliDate`, do not duplicate logic, call into Phase 04's utility
  instead), day/week/month grid rendering, event cards, "now" line,
  chrome/navigation (prev/next/today), and the queue/history tabs at the
  bottom of the file (`setTab`/`renderQueue`/`renderHist`).

## Goal
Replace the Appointments placeholder with the calendar screen: day/week/
month view switch, event rendering on the grid, date navigation chrome,
and the queue/history side tabs. Booking/editing an appointment (the
detail panel) is Phase 14.

## Contracts to add (`Animora.Contracts/Appointments/`)
- `IAppointmentsQueryService` — `Task<IReadOnlyList<AppointmentEvent>>
  GetForRangeAsync(DateOnly start, DateOnly end)`,
  `Task<IReadOnlyList<AppointmentEvent>> GetQueueAsync()`,
  `Task<IReadOnlyList<AppointmentEvent>> GetHistoryAsync()`.
- `AppointmentEvent` DTO (patient/owner, doctor, start/end time, type,
  status — mirrors `evCard`'s fields).

## Mock to add
- `MockAppointmentsQueryService` — a spread of sample events across the
  current week/month, a small queue list, a small history list.

## View/ViewModel tasks
1. `Pages/Appointments/AppointmentsCalendarViewModel.cs`: `ViewMode`
   (`Day|Week|Month`), `CurrentDate`, computed visible-range + events
   (mirrors `scopeDates`/`visible`/`pack`), navigation commands
   (`GoToday`, `StepPrevious`, `StepNext`), `Tab` (`Calendar|Queue|
   History`) switching between the calendar grid and the two list tabs.
2. `Pages/Appointments/AppointmentsCalendarView.axaml(.cs)`: chrome
   (view-mode toggle + prev/next/today + current-range label, mirrors
   `renderChrome`/`label`), and three grid sub-views:
   - `DayGridView.axaml` (mirrors `viewDay`, includes the "now" line)
   - `WeekGridView.axaml` (mirrors `viewWeek`)
   - `MonthGridView.axaml` (mirrors `viewMonth`)
   Each renders `AppointmentEvent`s as small cards (mirrors `evCard`) —
   use Phase 04's `JalaliDate`/`PersianNumberFormatter` for all date/number
   display, never re-deriving Jalali math locally.
3. `Pages/Appointments/AppointmentsQueueTabView.axaml` /
   `AppointmentsHistoryTabView.axaml`: simple list renders (mirrors
   `renderQueue`/`renderHist`).
4. Clicking an event card calls `[RelayCommand] OpenBooking(AppointmentEvent)`
   — for this phase, show a toast ("Booking panel — Phase 14"); Phase 14
   replaces it with the real detail/booking panel.
5. Update `Shell/NavigationMenu.cs`'s `appointments` nav item to target
   `AppointmentsCalendarViewModel`.

## Out of scope
- The booking/detail panel (Phase 14).

## Acceptance criteria
- App builds/runs; Appointments nav item shows the month/week/day toggle
  and correct grid for each mode; navigating prev/next/today updates the
  visible range and events correctly (verified against mock data); queue/
  history tabs render; clicking an event shows the Phase 14 placeholder
  toast without error.

## PROGRESS.md update
Mark Phase 13 done; advance "Next phase" to 14; update "Repo state."
