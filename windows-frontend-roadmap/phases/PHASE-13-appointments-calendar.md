# Phase 13 — Appointments — Calendar Views

**Band:** M · **Depends on:** 05 · **Satisfies:** R-4, R-5, R-6, R-18

## Read before starting
- `CONVENTIONS.md` §2, §3, §4a (Fidelity Protocol — MANDATORY), §5, §6 (Definition of Done incl. fidelity gate + visual verification), §7a
- **Reference CSS (MANDATORY — the source of every measurable value, per §4a rule 3 / §8): `styles.css` + `appointments.css` + `booking.css` (day/week/month grids, event cards, the "now" line, chrome/nav, queue/history tabs).** Read the sections these file(s) define for every element this screen renders and transcribe the exact paddings, gaps, radii, borders, font sizes/weights, grid/column templates, fixed/min/max sizes, fills and shadows — reusing Phase 02 tokens where a value equals a token. Do not eyeball or approximate.
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

## Fidelity (MANDATORY — read `CONVENTIONS.md` §4a in full)
- **Rebuild every element the reference screen renders — nothing dropped, simplified, merged, summarized, or deferred.** Every card, panel, header, stat/KPI box, chart/donut/gauge/ring/sparkline, badge/pill, tab, sub-nav, table (with the SAME columns in the SAME order), list row, avatar, icon, button, chip, empty-state and caption must exist, in the same place, order, grouping and count as the reference. The calendar grids (day/week/month), event cards and the live "now" line are first-class deliverables laid out with exact `Grid` templates from the CSS; none may be simplified away.
- **No visible reference element may be declared "out of scope," "nice-to-have," or "optional" here.** The only permitted simplification is *motion*: an animated value may render at its final/current value instead of animating — the element itself still exists and shows the correct value (§4a rule 6).
- **Exact measurable values** come from the Reference CSS above (§4a rule 3), not from judgement. Proportional CSS grids map to star-sized `Grid` columns/rows; fixed px sizes map to fixed sizes — transcribed, never re-guessed.
- **Exact Persian text** for every label/title/subtitle/column header/button/empty-state/tab, character-for-character; all numbers and dates through the Phase 04 Persian digit / grouping / Jalali helpers (§4a rule 4). State colors/pills map 1:1 to Phase 02 semantic tokens (§4a rule 5).
- **Backend-ready:** the ViewModel binds only to the `Animora.Contracts` interface(s); the mock is the sole swap point for a future real backend — no sample data, mock call, or design-time branch leaks into a View or the ViewModel's public shape (R-18).

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
- **Visual verification (§6.7 + §7a) performed** and recorded in `PROGRESS.md`: the rebuilt screen was compared element-by-element against the reference and matches (same boxes/order/sizes/colors/Persian text); every measurable value traces to the Reference CSS; no reference element is missing. Building and running alone does NOT satisfy this phase.

## PROGRESS.md update
Mark Phase 13 done; advance "Next phase" to 14; update "Repo state."
