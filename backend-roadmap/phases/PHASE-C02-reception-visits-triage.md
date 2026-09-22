# Phase C02 — Reception & Visits triage

**Band:** M · **Track:** C · **Depends on:** C01 · **Satisfies:** R-B01, R-B02
**Runnable after this phase:** desktop offline yes · server required no

## Read before starting
- `backend-roadmap/CONVENTIONS.md` §3, §4, §5, §8, §9, §10, §11, §12
- `backend-roadmap/PROGRESS.md` (the **C01 pattern** entry is the template for this phase)
- `backend-roadmap/phases/PHASE-C01-patients-owners.md` §"Design decisions" (pattern only)
- `PERFORMANCE.md` §4, §5, §6
- `src/Animora.Contracts/Reception/IReceptionQueueService.cs`,
  `src/Animora.Contracts/Reception/ReceptionModels.cs`
- `src/Animora.Contracts/Visits/IVisitsTriageService.cs`, `src/Animora.Contracts/Visits/VisitsModels.cs`
- `src/Animora.Domain/Scheduling/ReceptionQueueEntry.cs`, `src/Animora.Domain/Scheduling/Appointment.cs`
- `src/Animora.Domain/Clinical/Visit.cs`, `src/Animora.Domain/Identity/StaffMembership.cs`
- `src/Animora.Application/Ports/IReceptionQueueRepository.cs`,
  `src/Animora.Application/Ports/IAppointmentRepository.cs`
- `src/Animora.Desktop/App.axaml.cs` (lines registering `IReceptionQueueService`, `IVisitsTriageService`)

## Goal
The reception queue and the visits-triage screen run on the real local database. The queue is a
**derived view** over today's appointments plus walk-in `ReceptionQueueEntry` rows — never a second
stored copy of appointments — and triage starts a real `Visit` aggregate from a ticket or a new-visit
request.

## Design decisions (pre-made — implement, do not re-derive)
- **Queue derivation rule (the core decision):** `ReceptionQueueEntry` is persisted **only** for
  walk-ins (`QueueSource.WalkIn`) and for state that an appointment cannot carry
  (`AdmittedUtc`, `IsUrgent`, `CalledUtc`, `LeftUtc`, `NoShowUtc`). For an appointment-sourced ticket
  the row is created **lazily on first state change** with `AppointmentId` set and
  `Source = QueueSource.Appointment`; until then the ticket is projected straight from the
  `Appointment`. `ReceptionQueueQuery` therefore performs a **single** SQL statement: a `LEFT JOIN`
  from today's appointments to `ReceptionQueueEntries` `UNION ALL` the walk-in rows with no
  `AppointmentId`. No N+1, no duplicated appointment data.
- **`TicketId` composition:** the Contracts `QueueTicket.Id` is `"apt:" + AppointmentId` for an
  appointment-sourced ticket and `"wlk:" + QueueEntryId` for a walk-in. `TicketRef` (a
  `readonly record struct` in `Animora.Application/UseCases/Reception/TicketRef.cs`) parses and
  formats it; an unparsable value returns `TICKET_NOT_FOUND`. Every `IReceptionQueueService` member
  that takes a `ticketId` goes through `TicketRef.Parse`.
- **Queries** (`UseCases/Reception/`):
  - `ReceptionQueueQuery(DateOnly LocalDay)` → `IReadOnlyList<QueueTicketReadModel>`; `LocalDay`
    comes from the client as the Jalali-displayed day already converted to a Gregorian `DateOnly` by
    the service layer, and is translated to a `[startUtc, endUtc)` range before the query runs
    (**time comparison is always UTC-ticks range, never a date function in SQL**).
  - `ReceptionDoctorsQuery()` → `IReadOnlyList<ReceptionDoctorReadModel>` from `StaffMembership`
    rows with `StaffRole.Doctor` and `MembershipStatus.Active`.
- **Commands** (`UseCases/Reception/`, all `IJournaledCommand` + `[RequiresPermission]` +
  `[Auditable(AuditCategory.Clinical, ...)]`): `AdmitTicketCommand`, `EnterVisitCommand`,
  `LeaveQueueCommand`, `MarkNoShowCommand`, `ToggleTicketUrgentCommand`, `CreateWalkInCommand`.
  New `CommandTypes` entries: `reception.ticket.admit`, `reception.ticket.enter`,
  `reception.ticket.leave`, `reception.ticket.noshow`, `reception.ticket.urgent`,
  `reception.walkin.create` (6). Permission: `reception.queue.manage` for all six.
- **State mapping** — `QueueState` (domain) → `QueueTicketState` (Contracts) is a total switch in
  `ReceptionMapping`; the domain enum is authoritative and gains no member in this phase. An
  appointment-sourced ticket with no queue row maps to `QueueTicketState.Waiting`.
- **`CreateWalkInCommand`** resolves or creates the `Owner`/`Pet` pair: if `WalkInRequest.Phone`
  matches an existing `Owner.SearchKey` (C01) the existing owner is reused and a new `Pet` is created
  only when no pet of that owner matches the name; otherwise both are created. This is the single
  place walk-in registration can create patient records, and it is audited as
  `patients.owner.create`/`patients.pet.create` in addition to `reception.walkin.create`.
- **Triage queries** (`UseCases/Visits/`):
  - `TriageDoctorRailQuery(DateOnly LocalDay)` → per doctor: today's booked count, in-visit flag,
    next free slot minute — computed in one projection over appointments, **not** per doctor.
  - `BookedForDoctorQuery(string DoctorId, DateOnly LocalDay)` → `BookedAppointmentRef` rows.
  - `TriagePatientsQuery(string? Search)` → top 50 recently-seen pets (ordered by last visit desc),
    reusing C01's `SearchKey` index.
  - `IVisitsTriageService.GetVisitTypes()` is **synchronous** in Contracts: it returns a static
    `VisitTypeCatalog` list held in `Animora.Client.Services/Visits/VisitTypeCatalog.cs`, built from
    `AppointmentTypeCatalog` (A05) — no DB call, no blocking call.
- **Encounter start:** `StartVisitFromTicketCommand(string TicketId)` and
  `StartNewVisitCommand(NewVisitRequest)` both create a `Visit` in `VisitState.InProgress`, link
  `PetId`/`OwnerId`/`DoctorId`/`AppointmentId?`, write the queue transition to
  `QueueState.InVisit`, and return `EncounterDraftReadModel`. `CommandTypes` += `visits.visit.start`
  (1). Permission `visits.encounter.edit`. **Idempotency:** starting a visit for a ticket that
  already has an in-progress visit returns that visit (same `OperationId` derived from
  `"visit-start:" + ticketId` via `OperationIdFactory.Deterministic`) instead of creating a second
  one — `Deterministic(string seed)` is **added to `OperationIdFactory`** in this phase.
- **Sample data:** `Seed/ReceptionSampleData.cs` — 6 tickets for "today" (3 appointment-sourced,
  2 walk-ins, 1 no-show) and 4 doctors with distinct specialties, all deterministic.
- **ViewModel changes (exhaustive):** `ReceptionPageViewModel` and `VisitsTriagePageViewModel` each
  gain one `await _readiness.WaitAsync(ct)` line at the top of their load method (the C01 remedy).
  No other change, no `.axaml` change.
- **New error codes:** `TICKET_NOT_FOUND`, `TICKET_STATE_INVALID`, `WALKIN_PHONE_REQUIRED`,
  `DOCTOR_NOT_AVAILABLE`.

## Files to create / modify
| Path | Action | Contents (one line) |
|---|---|---|
| `src/Animora.Application/UseCases/Reception/TicketRef.cs` | create | ticket id parse/format |
| `src/Animora.Application/UseCases/Reception/*.cs` | create | 2 queries + 6 commands + handlers + validators |
| `src/Animora.Application/UseCases/Reception/ReadModels/*.cs` | create | `QueueTicketReadModel`, `ReceptionDoctorReadModel` |
| `src/Animora.Application/UseCases/Visits/*.cs` | create | 3 triage queries + 2 start commands |
| `src/Animora.Application/UseCases/Visits/ReadModels/*.cs` | create | `DoctorRailReadModel`, `EncounterDraftReadModel`, `TriagePatientReadModel` |
| `src/Animora.Application/Commands/OperationIdFactory.cs` | modify | add `Deterministic(string seed)` |
| `src/Animora.Persistence.Sqlite/Queries/ReceptionQueueQueries.cs` | create | the single-statement derived-queue projection |
| `src/Animora.Persistence.Sqlite/Queries/TriageQueries.cs` | create | doctor rail + booked + patients projections |
| `src/Animora.Persistence.Sqlite/Seed/ReceptionSampleData.cs` | create | deterministic tickets + doctors |
| `src/Animora.Client.Services/Reception/ReceptionQueueService.cs` | create | real `IReceptionQueueService` |
| `src/Animora.Client.Services/Reception/ReceptionMapping.cs` | create | read model ↔ Contracts DTOs |
| `src/Animora.Client.Services/Visits/VisitsTriageService.cs` | create | real `IVisitsTriageService` |
| `src/Animora.Client.Services/Visits/VisitTypeCatalog.cs` | create | static visit-type list |
| `src/Animora.Client.Services/Visits/VisitsMapping.cs` | create | read model ↔ `EncounterDraft`/`DoctorRailInfo` |
| `src/Animora.Contracts.Wire/Commands/CommandTypes.cs` | modify | add 7 command types |
| `src/Animora.Desktop/App.axaml.cs` | modify | swap 2 DI lines |
| `src/Animora.Desktop/Pages/Reception/ReceptionPageViewModel.cs` | modify | 1 readiness-gate line |
| `src/Animora.Desktop/Pages/Visits/VisitsTriagePageViewModel.cs` | modify | 1 readiness-gate line |
| `src/Animora.Desktop/Mocks/MockReceptionQueueService.cs` | delete | replaced |
| `src/Animora.Desktop/Mocks/MockVisitsTriageService.cs` | delete | replaced |
| `src/Animora.Domain/Results/ErrorCodes.cs` | modify | append 4 codes |
| `tests/Animora.Application.Tests/ReceptionQueueUseCaseTests.cs` | create | derivation + state transitions + walk-in dedup |
| `tests/Animora.Application.Tests/VisitsTriageUseCaseTests.cs` | create | start-visit idempotency, rail computation |
| `tests/Animora.Persistence.Tests/ReceptionQueueQueryTests.cs` | create | one SELECT, UTC-range filtering, no duplicates |

## Implementation steps
1. Add `TicketRef` and `OperationIdFactory.Deterministic`; append the 4 error codes.
2. Write `ReceptionQueueQueries` as one `LEFT JOIN` + `UNION ALL` statement; assert in a test that
   `ToQueryString()` contains exactly one `SELECT` per part and no date function.
3. Write the 2 reception queries + 6 reception commands with validators, permissions and audit
   attributes; add the `CommandTypes` entries.
4. Implement lazy `ReceptionQueueEntry` creation inside the state-changing handlers (find-or-create
   by `AppointmentId`, inside the unit of work).
5. Write `CreateWalkInCommand` with owner/pet resolve-or-create and its two extra audit records.
6. Write `TriageQueries`, the 3 triage queries and the 2 start-visit commands (deterministic
   `OperationId`, return the existing in-progress visit on replay).
7. Add `VisitTypeCatalog`, both mapping files, `ReceptionQueueService` and `VisitsTriageService`.
8. Add `ReceptionSampleData`; call it from `LocalSeeder` behind `SeedSampleData`.
9. Swap the two DI lines, add the two readiness-gate lines, delete both mocks.
10. Write the three test classes.
11. Verify manually: the queue shows today's appointments plus walk-ins; admitting, entering,
    marking no-show and toggling urgent all persist across restart; a walk-in with an existing
    owner's phone does not create a duplicate owner; triage starts an encounter from a ticket.
12. `build.sh full` = 0/0; `test.sh` = `TESTS OK`; `run.sh` = `SMOKE RUN OK`.

## Contracts / schema added
- Application: `TicketRef`, 5 queries, 8 commands, 5 read models,
  `OperationIdFactory.Deterministic`.
- Persistence: `ReceptionQueueQueries`, `TriageQueries`, `ReceptionSampleData`. **No migration** —
  A09 already mapped `ReceptionQueueEntry` and `Visit`.
- Client services: `ReceptionQueueService`, `ReceptionMapping`, `VisitsTriageService`,
  `VisitsMapping`, `VisitTypeCatalog`.
- Wire: 7 `CommandTypes` entries. `ErrorCodes` += 4.
- DI: `IReceptionQueueService` → `ReceptionQueueService`; `IVisitsTriageService` →
  `VisitsTriageService`. **Mocks deleted: `MockReceptionQueueService.cs`,
  `MockVisitsTriageService.cs`.**

## Rules that apply here
- **CONVENTIONS §10** — both contexts swap and delete in this phase.
- **CONVENTIONS §5 / PERFORMANCE.md §6** — the queue is one projection; the doctor rail is one
  projection; no per-doctor or per-ticket round trip.
- **CONVENTIONS §5** — day filtering is a UTC-ticks range against an index, never a SQL date function.
- **A-RULE 005 / 006** — every queue mutation is a journaled command; start-visit is deterministic so
  a retry cannot create a second `Visit`.
- **A-RULE 018** — the queue read model is derived/denormalized; the `Appointment` write model stays
  the single source of appointment truth.
- **CONVENTIONS §4** — `GetVisitTypes()` stays synchronous but does no I/O (static catalog).

## Out of scope
- Encounter editing (vitals/notes/Rx) and completion → C03.
- Appointment creation/move/cancel and the calendar → C04.
- Invoice creation from a completed encounter → C07.
- SMS/notification delivery → never in this roadmap (no external service, A-RULE 016).

## Acceptance criteria
- Build: `scripts/sandbox/build.sh full` = 0 warnings / 0 errors.
- Neither `MockReceptionQueueService.cs` nor `MockVisitsTriageService.cs` exists; no DI line
  references them.
- `ReceptionQueueUseCaseTests` green: an appointment with no queue row projects as `Waiting`; the
  first state change creates exactly one `ReceptionQueueEntry`; a second admit of the same ticket
  returns `TICKET_STATE_INVALID`; a walk-in whose phone matches an existing owner reuses that owner.
- `VisitsTriageUseCaseTests` green: `StartVisitFromTicketCommand` executed twice for the same ticket
  yields one `Visit` and the same `OperationId`; the doctor rail's booked counts match the seeded
  appointments.
- `ReceptionQueueQueryTests` green: the projection emits no duplicate ticket for an appointment that
  also has a queue row; the day filter uses a UTC-ticks range.
- Manual: reception screen and triage screen operate fully against the local DB and survive restart.
- Runnability (`CONVENTIONS.md` §11): `scripts/sandbox/run.sh` = `SMOKE RUN OK`.

## PROGRESS.md update
Mark C02 done; advance "Next phase" to `C03`; append a "Repo state" entry listing: the derived-queue
rule and `TicketRef` format (C04 and C07 depend on it), the 5 queries + 8 commands,
`OperationIdFactory.Deterministic`, `ReceptionQueueQueries`/`TriageQueries`, `ReceptionSampleData`,
the 2 client services + 2 mappings + `VisitTypeCatalog`, the 2 DI swaps, the 2 ViewModel
readiness lines, **mocks deleted: `MockReceptionQueueService.cs`, `MockVisitsTriageService.cs`**,
`CommandTypes` += 7, `ErrorCodes` += 4, the 3 test classes, deviations.
