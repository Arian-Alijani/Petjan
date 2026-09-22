# Phase A05 — Domain: Scheduling & Boarding

**Band:** M · **Track:** A · **Depends on:** A04 · **Satisfies:** R-B01, R-B17
**Runnable after this phase:** desktop offline yes · server required no

## Read before starting
- `backend-roadmap/CONVENTIONS.md` §3, §4, §6 (item 9), §9, §11, §12
- `backend-roadmap/PROGRESS.md`
- `ARCHITECTURE.md` §8.5 (appointment slot = domain validation, reject on conflict)
- `src/Animora.Domain/Primitives/*`, `src/Animora.Domain/ValueObjects/*`,
  `src/Animora.Domain/Clinical/Visit.cs`, `src/Animora.Domain/Results/ErrorCodes.cs`
- `src/Animora.Contracts/Appointments/AppointmentModels.cs`,
  `src/Animora.Contracts/Reception/ReceptionModels.cs`,
  `src/Animora.Contracts/Boarding/BoardingStay.cs`, `src/Animora.Contracts/Boarding/BoardingDetail.cs`

## Goal
Model the scheduling and boarding contexts: the appointment aggregate with its state machine and slot
conflict invariant, the reception queue entry, and boarding stays with rooms, services, per-stay items
and the transfer/checkout invariants that hand off to invoicing.

## Design decisions (pre-made — implement, do not re-derive)
- **Folders:** `src/Animora.Domain/Scheduling/`, `src/Animora.Domain/Boarding/`.
- `Scheduling/Appointment.cs` — `sealed class Appointment : AggregateRoot`:
  `PetId`, `OwnerId`, `string? DoctorUserId`, `string AppointmentTypeCode`,
  `DateTimeOffset StartUtc`, `int DurationMinutes`, `AppointmentState State`,
  `AppointmentSource Source`, `string? Note`, `string? VisitId`, `string? CancelReason`,
  `DateTimeOffset? CheckedInUtc`, `string CreatedByUserId`, `int RescheduleCount`.
  Computed: `DateTimeOffset EndUtc => StartUtc.AddMinutes(DurationMinutes)`.
  `enum AppointmentState { Scheduled, Confirmed, CheckedIn, InVisit, Completed, NoShow, Cancelled }`.
  `enum AppointmentSource { Phone, WalkIn, Online, Followup, Recall }`.
  **State machine:**
  ```
  Scheduled  -> Confirmed | CheckedIn | Cancelled | NoShow
  Confirmed  -> CheckedIn | Cancelled | NoShow
  CheckedIn  -> InVisit   | Cancelled | NoShow
  InVisit    -> Completed | Cancelled
  Completed / NoShow / Cancelled -> terminal
  ```
  Methods returning `Result`: `Confirm`, `CheckIn(IClock)`, `StartVisit(string visitId)`,
  `Complete`, `MarkNoShow(IClock)`, `Cancel(string reason, IClock)`,
  `Move(DateTimeOffset newStartUtc, int newDurationMinutes, IClock)`.
  Invariants: `DurationMinutes` ∈ [5, 480] and a multiple of 5; `Move` is forbidden once
  `State >= InVisit` (`APPOINTMENT_NOT_MOVABLE`) and increments `RescheduleCount`;
  `StartVisit` requires `CheckedIn`. Raises `AppointmentBookedEvent`, `AppointmentMovedEvent`,
  `AppointmentCancelledEvent`.
- `Scheduling/AppointmentSlot.cs` — `readonly record struct AppointmentSlot(DateTimeOffset StartUtc,
  int DurationMinutes)` with `EndUtc` and `bool Overlaps(AppointmentSlot other)`
  (half-open interval: `StartUtc < other.EndUtc && other.StartUtc < EndUtc`).
- `Scheduling/SlotConflictPolicy.cs` — static, the **authoritative** conflict rule:
  `static Result EnsureFree(AppointmentSlot candidate, string? doctorUserId,
  IReadOnlyList<ExistingSlot> existing)` where
  `readonly record struct ExistingSlot(string AppointmentId, string? DoctorUserId,
  AppointmentSlot Slot, AppointmentState State)`.
  Rules, in order:
  1. Slots in a terminal state (`Cancelled`, `NoShow`, `Completed`) never conflict.
  2. Two slots conflict only when they overlap **and** share the same non-null `DoctorUserId`.
  3. A `doctorUserId == null` candidate (unassigned) never conflicts — it is triaged at reception.
  4. On conflict return `Result.Fail(ErrorCodes.APPOINTMENT_SLOT_CONFLICT, conflictingAppointmentId)`.
  The candidate list is supplied by the caller (Application/Server) after a tenant-filtered,
  indexed range query — the policy itself is pure and testable.
- `Scheduling/AppointmentTypeCatalog.cs` — static seed of appointment types:
  `record struct AppointmentTypeDef(string Code, int DefaultDurationMinutes, bool RequiresDoctor)`.
  Seed: `CHECKUP 30 true`, `VACCINE 15 true`, `SURGERY 90 true`, `DENTAL 60 true`,
  `GROOMING 45 false`, `FOLLOWUP 20 true`, `CONSULT 20 true`.
- `Scheduling/ReceptionQueueEntry.cs` — `sealed class ReceptionQueueEntry : AggregateRoot`:
  `PetId`, `OwnerId`, `string? AppointmentId`, `string? DoctorUserId`, `QueueState State`,
  `QueueSource Source`, `int TicketNumber`, `DateTimeOffset EnqueuedUtc`,
  `DateTimeOffset? CalledUtc`, `DateTimeOffset? ClosedUtc`, `int Priority` (0 normal, 1 urgent,
  2 emergency), `string? Note`.
  `enum QueueState { Waiting, Called, InVisit, Done, Left }`,
  `enum QueueSource { Appointment, WalkIn }`.
  **[DECISION] The queue is derived, not duplicated:** an entry exists only for a *present* patient —
  created at appointment check-in or at walk-in registration. Future appointments never create queue
  rows (C02 depends on this). `TicketNumber` is a per-tenant, per-local-day counter assigned by the
  Application layer (B01+), not by the entity.
  Methods: `Call(string doctorUserId, IClock)`, `StartVisit(string visitId)`, `Close(IClock)`,
  `MarkLeft(IClock)`, `SetPriority(int)`.
- `Boarding/Room.cs` — `sealed class Room : AggregateRoot`: `Code`, `DisplayName`, `RoomType Type`,
  `int Capacity`, `RoomStatus Status`, `string? Notes`.
  `enum RoomType { Standard, Vip, Isolation, Cage, Suite }`,
  `enum RoomStatus { Available, Occupied, Maintenance, Retired }`.
  Invariant: `Capacity` ∈ [1, 10]; `Status` may not go `Retired → Available`.
- `Boarding/BoardingServiceDef.cs` — `sealed class BoardingServiceDef : AggregateRoot`:
  `Code`, `Title`, `Money UnitPrice`, `BoardingServiceBilling Billing`, `bool IsActive`.
  `enum BoardingServiceBilling { PerStay, PerDay, PerUse }`.
- `Boarding/BoardingStay.cs` — `sealed class BoardingStay : AggregateRoot`:
  `PetId`, `OwnerId`, `string RoomId`, `DateTimeOffset CheckInUtc`,
  `DateTimeOffset PlannedCheckOutUtc`, `DateTimeOffset? ActualCheckOutUtc`, `StayState State`,
  `Money DailyRate`, `string? CareNote`, `string? DietNote`, `string? InvoiceId`,
  `IReadOnlyList<StayItem> Items`, `IReadOnlyList<StayRoomAssignment> RoomHistory`.
  `enum StayState { Reserved, Active, ReadyForInvoice, Invoiced, CheckedOut, Cancelled }`.
  **State machine:**
  ```
  Reserved        -> Active | Cancelled
  Active          -> ReadyForInvoice | Cancelled
  ReadyForInvoice -> Invoiced
  Invoiced        -> CheckedOut
  CheckedOut / Cancelled -> terminal
  ```
  Methods returning `Result`: `Activate(IClock)`, `AddItem(StayItem)`, `RemoveItem(string itemId)`,
  `TransferRoom(string newRoomId, string reason, IClock)`,
  `RequestCheckout(IClock)` (→ `ReadyForInvoice`), `AttachInvoice(string invoiceId)`,
  `CompleteCheckout(IClock)` (→ `CheckedOut`, sets `ActualCheckOutUtc`),
  `Cancel(string reason, IClock)`.
  Invariants: `PlannedCheckOutUtc > CheckInUtc`; items are immutable once `State >= Invoiced`
  (`STAY_LOCKED`); `TransferRoom` only in `Active` and appends to `RoomHistory`;
  `CompleteCheckout` requires `InvoiceId != null` (**a stay is never checked out unbilled** —
  `STAY_INVOICE_REQUIRED`). Raises `StayActivatedEvent`, `StayReadyForInvoiceEvent`,
  `StayRoomTransferredEvent`, `StayCheckedOutEvent`.
  **Billable-days rule (authoritative):** `int BillableDays(DateTimeOffset endUtc)` =
  `Math.Max(1, ceil((endUtc - CheckInUtc).TotalHours / 24))` — a partial day counts as a full day, a
  same-day stay counts as 1.
- `Boarding/StayItem.cs` — `sealed record StayItem(string ItemId, StayItemKind Kind,
  string? CatalogItemId, string Title, Quantity Quantity, Money UnitPrice,
  DateTimeOffset ConsumedUtc, string RecordedByUserId)`.
  `enum StayItemKind { BoardingService, ClinicService, Product, Custom }`.
- `Boarding/StayRoomAssignment.cs` — `sealed record StayRoomAssignment(string RoomId,
  DateTimeOffset FromUtc, DateTimeOffset? ToUtc, string? Reason)`.
- **Invoice handoff:** neither `Appointment`, `BoardingStay` nor `Visit` computes totals. Each exposes
  its billable lines (`VisitServiceLine` from A04, `StayItem` + `DailyRate × BillableDays` here) and
  Sales (A07) builds the invoice. Document this in each `RequestCheckout`/`MarkReadyForInvoice` XML doc.
- **Domain events** appended to `src/Animora.Domain/Events/`: `SchedulingEvents.cs`
  (`AppointmentBookedEvent`, `AppointmentMovedEvent`, `AppointmentCancelledEvent`,
  `QueueEntryCreatedEvent`) and `BoardingEvents.cs` (`StayActivatedEvent`,
  `StayReadyForInvoiceEvent`, `StayRoomTransferredEvent`, `StayCheckedOutEvent`).
- **New error codes:** `APPOINTMENT_SLOT_CONFLICT`, `APPOINTMENT_INVALID_TRANSITION`,
  `APPOINTMENT_NOT_MOVABLE`, `APPOINTMENT_DURATION_INVALID`, `QUEUE_INVALID_TRANSITION`,
  `ROOM_UNAVAILABLE`, `ROOM_CAPACITY_INVALID`, `STAY_INVALID_TRANSITION`, `STAY_LOCKED`,
  `STAY_INVOICE_REQUIRED`, `STAY_DATES_INVALID`.

## Files to create / modify
| Path | Action | Contents (one line) |
|---|---|---|
| `src/Animora.Domain/Scheduling/Appointment.cs` | create | appointment aggregate + state machine + move |
| `src/Animora.Domain/Scheduling/AppointmentSlot.cs` | create | half-open slot + overlap test |
| `src/Animora.Domain/Scheduling/SlotConflictPolicy.cs` | create | pure conflict rule + `ExistingSlot` |
| `src/Animora.Domain/Scheduling/AppointmentTypeCatalog.cs` | create | 7 seeded appointment types |
| `src/Animora.Domain/Scheduling/ReceptionQueueEntry.cs` | create | derived queue entry + transitions |
| `src/Animora.Domain/Boarding/Room.cs` | create | room entity + status guard |
| `src/Animora.Domain/Boarding/BoardingServiceDef.cs` | create | boarding service catalog entity |
| `src/Animora.Domain/Boarding/BoardingStay.cs` | create | stay aggregate + state machine + billable days |
| `src/Animora.Domain/Boarding/StayItem.cs` | create | consumed-item value object |
| `src/Animora.Domain/Boarding/StayRoomAssignment.cs` | create | room-history value object |
| `src/Animora.Domain/Events/SchedulingEvents.cs` | create | 4 scheduling events |
| `src/Animora.Domain/Events/BoardingEvents.cs` | create | 4 boarding events |
| `src/Animora.Domain/Results/ErrorCodes.cs` | modify | append the 11 codes |
| `tests/Animora.Domain.Tests/SlotConflictPolicyTests.cs` | create | overlap matrix, terminal states, null doctor, adjacency |
| `tests/Animora.Domain.Tests/AppointmentStateMachineTests.cs` | create | all transitions + move guards + duration validation |
| `tests/Animora.Domain.Tests/BoardingStayTests.cs` | create | transitions, billable days, unbilled-checkout rejection, transfer history |
| `tests/Animora.Domain.Tests/ReceptionQueueTests.cs` | create | transitions + priority + derived-entry rule |

## Implementation steps
1. Create `Scheduling/` and add `AppointmentSlot` (half-open overlap) and `AppointmentTypeCatalog`.
2. Implement `Appointment` with a private `Result Transition(AppointmentState)` helper, the duration
   invariant, `Move` guard + `RescheduleCount`, and the three events.
3. Implement `SlotConflictPolicy` as a pure static with the four ordered rules.
4. Implement `ReceptionQueueEntry` with its transitions; document the derived-queue decision in XML docs.
5. Create `Boarding/` and add `Room`, `BoardingServiceDef`, `StayItem`, `StayRoomAssignment`.
6. Implement `BoardingStay` with the state machine, `BillableDays`, `TransferRoom` appending to
   `RoomHistory`, and the `CompleteCheckout` invoice guard.
7. Add the two event files and append the 11 error codes.
8. Write the four test classes. `SlotConflictPolicyTests` must include: exact overlap, partial overlap,
   **adjacent slots (end == start) do NOT conflict**, different doctor no conflict, cancelled/no-show
   existing slot no conflict, null-doctor candidate no conflict.
   `BoardingStayTests` must include: same-day stay = 1 billable day, 25 hours = 2 days,
   `CompleteCheckout` without an invoice → `STAY_INVOICE_REQUIRED`.
9. `build.sh full` = 0/0; `test.sh` = `TESTS OK`; `run.sh` = `SMOKE RUN OK`.

## Contracts / schema added
- Entities: `Appointment`, `ReceptionQueueEntry`, `Room`, `BoardingServiceDef`, `BoardingStay`.
- Value objects: `AppointmentSlot`, `ExistingSlot`, `AppointmentTypeDef`, `StayItem`,
  `StayRoomAssignment`.
- Enums: `AppointmentState`, `AppointmentSource`, `QueueState`, `QueueSource`, `RoomType`,
  `RoomStatus`, `BoardingServiceBilling`, `StayState`, `StayItemKind`.
- Statics: `SlotConflictPolicy`, `AppointmentTypeCatalog`. Events: 8 new records.
- `ErrorCodes` += 11. No migration, no `Animora.Contracts` change.

## Rules that apply here
- **ARCHITECTURE.md §8.5** — an appointment slot conflict is decided by `SlotConflictPolicy`
  (domain validation, reject), never by a row-version or a last-write-wins resolver.
- **A-RULE 004** — the state machines are the deciders; concurrency detection only flags.
- **CONVENTIONS §5** — the slot range query the policy consumes must be indexed on
  `(TenantId, DoctorUserId, StartUtc)`; note this for A09 (it creates the index).
- **CONVENTIONS §4** — `IClock` injected into every time-stamping method.
- **CONVENTIONS §2** — `Animora.Domain` gains no reference.
- **PERFORMANCE.md §2** — `SlotConflictPolicy.EnsureFree` iterates with a `for` loop over the supplied
  list, no LINQ chain, no allocation per candidate.

## Out of scope
- Inventory/product catalog entities → A06. Invoice/payment/cash session → A07.
- Ledger postings from a boarding invoice → A08. EF mappings + indexes + migration → A09.
- Ticket-number assignment and queue projection → B01/C02.
- `IAppointmentsQueryService`/`IAppointmentBookingService`/`IReceptionQueueService`/
  `IBoardingQueryService` implementations → C02, C04, C05.
- Jalali date handling — presentation only, stays in `Animora.Desktop/Localization`.

## Acceptance criteria
- Build: `scripts/sandbox/build.sh full` = 0 warnings / 0 errors.
- `SlotConflictPolicyTests` green, including the adjacency case (`end == start` is free) and the
  terminal-state exclusions.
- `AppointmentStateMachineTests` covers all `AppointmentState` pairs; only the tabled transitions
  succeed; a 7-minute duration returns `APPOINTMENT_DURATION_INVALID`.
- `BoardingStayTests` green: billable-day boundaries (1h → 1, 24h → 1, 25h → 2) and
  `STAY_INVOICE_REQUIRED` on an unbilled checkout.
- `Animora.Domain.csproj` still has zero references.
- Runnability (`CONVENTIONS.md` §11): `scripts/sandbox/run.sh` = `SMOKE RUN OK`.

## PROGRESS.md update
Mark A05 done; advance "Next phase" to `A06`; append a "Repo state" entry listing: the 12 new files, the
entities/enums/events added, the slot-conflict and billable-days rules, `ErrorCodes` += 11, the 4 test
classes, the index requirement noted for A09, no DI change, no mock deleted, no migration, deviations.
