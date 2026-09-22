# Phase C04 — Appointments (calendar + booking)

**Band:** M · **Track:** C · **Depends on:** C02 · **Satisfies:** R-B01, R-B02
**Runnable after this phase:** desktop offline yes · server required no

## Read before starting
- `backend-roadmap/CONVENTIONS.md` §3, §4, §5, §8, §9, §10, §11, §12
- `backend-roadmap/PROGRESS.md` (the **C01 pattern**, C02's `TicketRef` + derived-queue rule)
- `PERFORMANCE.md` §5, §6, §12
- `src/Animora.Contracts/Appointments/IAppointmentsQueryService.cs`,
  `src/Animora.Contracts/Appointments/IAppointmentBookingService.cs`,
  `src/Animora.Contracts/Appointments/AppointmentModels.cs`,
  `src/Animora.Contracts/Appointments/AppointmentBookingModels.cs`
- `src/Animora.Domain/Scheduling/Appointment.cs`, `src/Animora.Domain/Scheduling/AppointmentSlot.cs`,
  `src/Animora.Domain/Scheduling/SlotConflictPolicy.cs`,
  `src/Animora.Domain/Scheduling/AppointmentTypeCatalog.cs`
- `src/Animora.Application/Ports/IAppointmentRepository.cs`
- `src/Animora.Desktop/App.axaml.cs` (lines registering `AppointmentStore`,
  `IAppointmentsQueryService`, `IAppointmentBookingService`)

## Goal
The calendar and booking panel run on real `Appointment` rows: range queries, day counts, doctor
busy counts, available-slot computation, conflict detection, create/update/cancel/check-in — all
under the A05 slot-conflict invariant. `AppointmentStore` is deleted.

## Design decisions (pre-made — implement, do not re-derive)
- **Time model:** `Appointment.StartUtc`/`EndUtc` are UTC ticks (A09 converter). Every Contracts
  member that takes a `DateOnly` is a **local clinic day**; conversion happens once, in
  `ClinicCalendar` (`Animora.Application/Time/ClinicCalendar.cs`):
  `static (long StartTicks, long EndTicks) DayRangeUtc(DateOnly localDay, TimeZoneInfo tz)` and
  `static int MinutesFromDayStart(long utcTicks, DateOnly localDay, TimeZoneInfo tz)`.
  The clinic time zone is a tenant setting (`Tenant.TimeZoneId`, already on the A03 entity) resolved
  through `IClinicTimeZoneProvider` (new port, default = `Tenant.TimeZoneId`, fallback
  `"Asia/Tehran"`). **No SQL date function is ever used** — all filtering is `>= startTicks &&
  < endTicks` against `IX_Appointments_TenantId_StartUtc`.
- **Queries** (`UseCases/Scheduling/`):
  - `AppointmentRangeQuery(DateOnly Start, DateOnly End, string? DoctorId)` →
    `IReadOnlyList<AppointmentEventReadModel>`; one projection joining pet + owner + doctor display
    names. The range is clamped to **62 days** (`APPOINTMENT_RANGE_TOO_WIDE`) so the calendar can
    never pull a year at once.
  - `AppointmentDayCountsQuery(DateOnly Start, DateOnly End)` → `IReadOnlyDictionary<DateOnly,int>`
    computed by a **single grouped SQL statement** over the UTC range, bucketed client-side by
    `ClinicCalendar` (SQLite cannot group by a time-zone-shifted day).
  - `DoctorBusyCountQuery(string DoctorId, DateOnly LocalDay)` → `int` (`COUNT` only, no rows).
  - `AppointmentDetailQuery(string AppointmentId)` → `AppointmentDetailReadModel?`.
  - `AppointmentQueueQuery()` → today's appointments in `AppointmentState.CheckedIn` or `Booked`,
    reusing the C02 derived-queue projection's appointment half (shared helper
    `SchedulingProjections.AppointmentEvent(IQueryable<Appointment>)` so the two screens can never
    drift).
  - `AppointmentHistoryQuery()` → last 200 appointments in a terminal state, newest first.
  - `AvailableSlotsQuery(string DoctorId, DateOnly LocalDay, int DurationMinutes)` →
    `IReadOnlyList<TimeSlotReadModel>`: the doctor's working window
    (`WorkingHours` — a new `[LocalOnly]`-free value object stored on `StaffMembership` as
    `WorkStartMinute`/`WorkEndMinute`/`WorkDaysMask`, **added in this phase's migration**) sliced at
    a fixed 15-minute grid (`SlotGrid.StepMinutes = 15`), minus busy intervals loaded in **one**
    query, minus `DurationMinutes` overhang past the window.
  - `AppointmentConflictsQuery(string DoctorId, long StartUtc, long EndUtc, string? ExcludeId)` →
    the conflicting events, evaluated by `SlotConflictPolicy` (A05) — the domain policy is the only
    authority; the query merely supplies candidates from the indexed range.
- **Commands** (`UseCases/Scheduling/`, all `IJournaledCommand` + `[RequiresPermission
  ("appointments.book")]` + `[Auditable(AuditCategory.Scheduling, ...)]`):
  `BookAppointmentCommand`, `RescheduleAppointmentCommand`, `CancelAppointmentCommand`,
  `CheckInAppointmentCommand`, `CreateBookingPatientCommand`.
  `CommandTypes` += `scheduling.appointment.book`, `scheduling.appointment.reschedule`,
  `scheduling.appointment.cancel`, `scheduling.appointment.checkin`,
  `scheduling.bookingpatient.create` (5).
- **Conflict handling is a domain rejection, never a merge** (A-RULE 003/004 and ARCH §8.5's
  "appointment slot = domain validation, reject on conflict"): `BookAppointmentCommand` and
  `RescheduleAppointmentCommand` re-run `SlotConflictPolicy` **inside** the transaction, after
  loading the doctor's overlapping appointments with `ExpectedAggregateVersion` recorded on the
  journaled envelope. A conflict returns `APPOINTMENT_SLOT_CONFLICT` carrying the conflicting
  appointment id in `Result.Detail`; the UI already renders the conflict list.
- **Check-in integrates with C02:** `CheckInAppointmentCommand` transitions the appointment to
  `AppointmentState.CheckedIn` **and** creates/updates the lazy `ReceptionQueueEntry`
  (`QueueState.Waiting`, `Source = QueueSource.Appointment`) through the same find-or-create helper
  C02 introduced — extracted in this phase to
  `Animora.Application/UseCases/Reception/QueueEntryResolver.cs` and called from both places.
- **`CreatePatientAsync` (booking panel)** delegates to C01's `CreateOwnerCommand` +
  `CreatePetCommand` inside one `IUnitOfWork.ExecuteAsync`, then returns `BookingPatient`. It does
  not duplicate patient validation.
- **Migration `010_StaffWorkingHours`** — adds `WorkStartMinute` (`int`, default 480),
  `WorkEndMinute` (`int`, default 1200), `WorkDaysMask` (`int`, default `0b0111111` = Sat–Thu) to
  `StaffMemberships`, plus index `IX_Appointments_TenantId_DoctorId_StartUtc`.
- **Sample data:** `Seed/SchedulingSampleData.cs` — 40 appointments spread across −7…+14 days,
  4 doctors, mixed states, including two deliberately adjacent (non-overlapping) slots so the
  conflict test has a realistic near-miss.
- **ViewModel changes (exhaustive):** `AppointmentsCalendarViewModel` and `BookingPanelViewModel`
  each gain the readiness-gate line. No `.axaml` change.
- **New error codes:** `APPOINTMENT_NOT_FOUND`, `APPOINTMENT_SLOT_CONFLICT`,
  `APPOINTMENT_STATE_INVALID`, `APPOINTMENT_RANGE_TOO_WIDE`, `DOCTOR_OUTSIDE_WORKING_HOURS`.

## Files to create / modify
| Path | Action | Contents (one line) |
|---|---|---|
| `src/Animora.Application/Time/ClinicCalendar.cs` | create | local-day ↔ UTC-ticks conversion |
| `src/Animora.Application/Ports/IClinicTimeZoneProvider.cs` | create | tenant time-zone port |
| `src/Animora.Application/UseCases/Scheduling/*.cs` | create | 7 queries + 5 commands + handlers/validators |
| `src/Animora.Application/UseCases/Scheduling/ReadModels/*.cs` | create | 4 read models |
| `src/Animora.Application/UseCases/Scheduling/SlotGrid.cs` | create | 15-minute grid slicing |
| `src/Animora.Application/UseCases/Reception/QueueEntryResolver.cs` | create | shared find-or-create (extracted from C02) |
| `src/Animora.Persistence.Sqlite/Queries/SchedulingProjections.cs` | create | shared appointment-event projection |
| `src/Animora.Persistence.Sqlite/Queries/AppointmentQueries.cs` | create | range/day-count/busy/slots/conflicts |
| `src/Animora.Persistence.Sqlite/Configurations/Identity/StaffMembershipConfiguration.cs` | modify | working-hours columns |
| `src/Animora.Persistence.Sqlite/Migrations/*` | create | `010_StaffWorkingHours` + snapshot |
| `src/Animora.Persistence.Sqlite/Seed/SchedulingSampleData.cs` | create | 40 deterministic appointments |
| `src/Animora.Infrastructure.Local/Time/ClinicTimeZoneProvider.cs` | create | reads `Tenant.TimeZoneId` |
| `src/Animora.Client.Services/Appointments/AppointmentsQueryService.cs` | create | real `IAppointmentsQueryService` |
| `src/Animora.Client.Services/Appointments/AppointmentBookingService.cs` | create | real `IAppointmentBookingService` |
| `src/Animora.Client.Services/Appointments/AppointmentsMapping.cs` | create | read models ↔ Contracts DTOs |
| `src/Animora.Contracts.Wire/Commands/CommandTypes.cs` | modify | add 5 command types |
| `src/Animora.Desktop/App.axaml.cs` | modify | swap 2 DI lines; drop `AppointmentStore` registration |
| `src/Animora.Desktop/Pages/Appointments/AppointmentsCalendarViewModel.cs` | modify | readiness-gate line |
| `src/Animora.Desktop/Pages/Appointments/BookingPanelViewModel.cs` | modify | readiness-gate line |
| `src/Animora.Desktop/Mocks/MockAppointmentsQueryService.cs` | delete | replaced |
| `src/Animora.Desktop/Mocks/MockAppointmentBookingService.cs` | delete | replaced |
| `src/Animora.Desktop/Mocks/AppointmentStore.cs` | delete | replaced by the DB |
| `src/Animora.Domain/Results/ErrorCodes.cs` | modify | append 5 codes |
| `tests/Animora.Application.Tests/AppointmentBookingUseCaseTests.cs` | create | conflict, reschedule, cancel, check-in |
| `tests/Animora.Application.Tests/AvailableSlotsTests.cs` | create | grid, working hours, duration overhang |
| `tests/Animora.Persistence.Tests/AppointmentQueryTests.cs` | create | UTC-range filtering, day buckets, index use |

## Implementation steps
1. Add `ClinicCalendar`, `IClinicTimeZoneProvider` + its implementation, `SlotGrid`, the 5 error codes.
2. Add the working-hours columns + the doctor/start index; generate `010_StaffWorkingHours`.
3. Write `SchedulingProjections` and `AppointmentQueries`; refactor C02's queue projection to call
   the shared helper (no behavior change — assert with C02's existing tests still green).
4. Write the 7 queries with the 62-day clamp and the single-statement day-count grouping.
5. Write the 5 commands; re-run `SlotConflictPolicy` inside the transaction and record
   `ExpectedAggregateVersion` on the envelope.
6. Extract `QueueEntryResolver` from C02 and call it from `CheckInAppointmentCommand`.
7. Write `AppointmentsMapping`, `AppointmentsQueryService`, `AppointmentBookingService`.
8. Add `SchedulingSampleData`; wire it into `LocalSeeder` behind `SeedSampleData`.
9. Swap the two DI lines, remove the `AppointmentStore` registration, add the two ViewModel lines,
   delete the three mock files.
10. Write the three test classes.
11. Verify manually: month/week/day views load, day badges match, booking a conflicting slot is
    rejected with the conflict listed, reschedule and cancel persist, check-in makes the ticket
    appear on the reception screen.
12. `build.sh full` = 0/0; `test.sh` = `TESTS OK`; `run.sh` = `SMOKE RUN OK`.

## Contracts / schema added
- Application: `ClinicCalendar`, `IClinicTimeZoneProvider`, `SlotGrid`, `QueueEntryResolver`,
  7 scheduling queries, 5 scheduling commands, 4 read models.
- Persistence: `SchedulingProjections`, `AppointmentQueries`, working-hours columns + doctor index,
  migration `010_StaffWorkingHours`, `SchedulingSampleData`.
- Infrastructure: `ClinicTimeZoneProvider`.
- Client services: `AppointmentsQueryService`, `AppointmentBookingService`, `AppointmentsMapping`.
- Wire: 5 `CommandTypes` entries. `ErrorCodes` += 5. No `Animora.Contracts` change.
- DI: `IAppointmentsQueryService` → `AppointmentsQueryService`; `IAppointmentBookingService` →
  `AppointmentBookingService`; `AppointmentStore` registration removed.
  **Mocks deleted: `MockAppointmentsQueryService.cs`, `MockAppointmentBookingService.cs`,
  `AppointmentStore.cs`.**

## Rules that apply here
- **A-RULE 003 / 004 / R-B17** — a slot conflict is a domain rejection; no last-write-wins, no merge.
- **A-RULE 005** — book/reschedule/cancel/check-in are journaled with `ExpectedAggregateVersion`, so
  the server (D06) can re-validate the same invariant.
- **CONVENTIONS §5** — every range filter is UTC ticks against an index; no SQL date function; the
  calendar range is clamped.
- **PERFORMANCE.md §6** — day counts and busy counts are aggregates, never materialized row lists.
- **R-B24** — Jalali rendering stays entirely in the Desktop presentation layer; the Application
  layer speaks `DateOnly` + UTC ticks only.
- **CONVENTIONS §10** — three mock files deleted with the swap.

## Out of scope
- Reception queue behavior itself → C02 (this phase only feeds it via check-in).
- Invoicing an appointment → C07. Boarding stays → C05.
- Server-side booking validation → D06.
- Recurring appointments → not a product requirement; not built.

## Acceptance criteria
- Build: `scripts/sandbox/build.sh full` = 0 warnings / 0 errors.
- `MockAppointmentsQueryService.cs`, `MockAppointmentBookingService.cs` and `AppointmentStore.cs`
  do not exist; no DI line references them.
- `AppointmentBookingUseCaseTests` green: an exactly-overlapping slot for the same doctor returns
  `APPOINTMENT_SLOT_CONFLICT` with the conflicting id; an adjacent (touching) slot succeeds; a
  booking outside the doctor's working window returns `DOCTOR_OUTSIDE_WORKING_HOURS`; cancelling a
  checked-in appointment returns `APPOINTMENT_STATE_INVALID`; check-in creates exactly one queue row.
- `AvailableSlotsTests` green: slots step by 15 minutes; a 45-minute duration never returns a slot
  whose end passes the window; busy intervals are excluded.
- `AppointmentQueryTests` green: a 90-day range returns `APPOINTMENT_RANGE_TOO_WIDE`; day counts for
  a DST-free `Asia/Tehran` week match the seeded rows; `ToQueryString()` for the range query has no
  date function and one `SELECT`.
- C02's `ReceptionQueueQueryTests` still green after the projection extraction.
- Manual: calendar, booking, reschedule, cancel and check-in all work offline and survive restart.
- Runnability (`CONVENTIONS.md` §11): `scripts/sandbox/run.sh` = `SMOKE RUN OK`.

## PROGRESS.md update
Mark C04 done; advance "Next phase" to `C05`; append a "Repo state" entry listing: `ClinicCalendar` +
`IClinicTimeZoneProvider` (**every later date-ranged query uses them**), `SlotGrid`, the shared
`SchedulingProjections` and `QueueEntryResolver`, working-hours columns + migration
`010_StaffWorkingHours`, the 7 queries + 5 commands, `SchedulingSampleData`, the 2 client services,
the 2 DI swaps + removed `AppointmentStore`, **mocks deleted:
`MockAppointmentsQueryService.cs`, `MockAppointmentBookingService.cs`, `AppointmentStore.cs`**,
`CommandTypes` += 5, `ErrorCodes` += 5, the 3 test classes, deviations.
