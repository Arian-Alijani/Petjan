# Phase C02 — Reception & Visits triage · Execution TODO

> **Self-contained working document.** Everything a fresh session needs to finish C02 without
> re-reading the codebase is captured here. Source of truth is
> `backend-roadmap/phases/PHASE-C02-reception-visits-triage.md` (this plan restates it plus the
> concrete facts discovered from the current tree). **The last task of this list deletes this file.**

---

## 0. Hard rules for this phase (LOCKED — never violate)

- **After completing EVERY task below, run `scripts/git/push.sh "type(scope): message"`.** If the push
  is skipped the phase is a failure. One task = one commit = one push.
- **No full/long builds or full test runs.** Targeted `dotnet build` of a single project or a filtered
  `dotnet test` only when strictly needed; the phase's own `build.sh full` / `test.sh` / `run.sh`
  acceptance gate is the very last verification step and is run once (per user directive: avoid long
  builds/tests during iteration).
- **Investigate only what a task needs** — no aimless file reading.
- Work on branch **`genspark_ai_developer`** (push script switches with `-b` on first push).
- Standard, high-quality, convention-following code — mirror the C01 pattern exactly.

---

## 1. Phase understanding (what & why)

**Goal:** Make the Reception queue screen and the Visits-triage screen run on the real local SQLite DB
(replacing the two mocks), following **the C01 pattern**: Application use cases → a `Client.Services`
implementation of the existing Contracts interface → hand-written mapping → one/two DI lines → mock
deleted.

**The core design decision — the queue is a DERIVED view, never a duplicate:**
- `ReceptionQueueEntry` rows are persisted **only** for walk-ins AND for state an `Appointment` cannot
  carry (admitted/urgent/called/left/no-show).
- For an **appointment-sourced** ticket the queue row is created **lazily on the first state change**
  (`AppointmentId` set, `Source = QueueSource.Appointment`). Until then the ticket is projected
  straight from the `Appointment` and maps to `QueueTicketState.Waiting`.
- `ReceptionQueueQuery` = **one** SQL statement: `LEFT JOIN` today's appointments → `ReceptionQueueEntries`
  `UNION ALL` the walk-in rows (no `AppointmentId`). No N+1, no duplicated appointment data.
- Day filtering is a **UTC-ticks range** `[startUtc, endUtc)` against an index — **never a SQL date
  function**.

**Triage:** starts a real `Visit` aggregate (`VisitState.InProgress`) from a ticket or a new-visit
request; start-visit is **idempotent** (deterministic `OperationId`).

---

## 2. Key facts discovered from the current tree (do NOT re-derive)

### Contracts (already exist — DO NOT modify, only implement/map to them)
- `src/Animora.Contracts/Reception/IReceptionQueueService.cs`: methods
  `GetQueueAsync()`, `AdmitAsync(ticketId)`, `EnterVisitAsync(ticketId)`, `LeaveQueueAsync(ticketId)`,
  `MarkNoShowAsync(ticketId)`, `ToggleUrgentAsync(ticketId)`, `CreateWalkInAsync(WalkInRequest)→QueueTicket`.
- `ReceptionModels.cs`: enums `QueueTicketState { Waiting, Called, InVisit, Done, Left, NoShow, Cancelled }`,
  `QueueTicketSource { Appointment, WalkIn, Urgent }`; records `ReceptionDoctor(Id,Name,Specialty)`,
  `QueueTicket(Id, PatientName, PatientKind, OwnerName, OwnerPhone, DoctorId, DoctorName,
  DoctorSpecialty, DoctorInitials, DoctorAccent, Source, State, IsUrgent, ArrivalTime, DurationMinutes,
  Sequence)`, `WalkInRequest(Name, Owner, Phone, DoctorId, IsUrgent)`.
- `src/Animora.Contracts/Visits/IVisitsTriageService.cs`: `GetDoctorsAsync()→DoctorRailInfo[]`,
  `GetBookedForDoctorAsync(doctorId)→BookedAppointmentRef[]`, **`GetVisitTypes()` is SYNCHRONOUS**
  →`VisitTypeInfo[]`, `GetPatientsAsync()→VisitPatientRef[]`, `StartFromTicketAsync(ticketId)→EncounterDraft`,
  `StartNewAsync(NewVisitRequest)→EncounterDraft`.
- `src/Animora.Contracts/Visits/VisitsTriageModels.cs`: enums `VisitEntryMode`, `BookedAppointmentState
  { Waiting, Called, InVisit }`; records `VisitTypeInfo(Id, Label, IconKey, Accent, DefaultDurationMinutes)`,
  `DoctorRailInfo(Id, Name, Specialty, Initials, Accent, AppointmentCount, WalkInCount)`,
  `BookedAppointmentRef(TicketId, AtMinutes, DurationMinutes, PatientName, PatientKind, PatientIconKey,
  OwnerName, OwnerPhone, TypeId, IsUrgent, State)`, `VisitPatientRef(Id, Name, Kind, OwnerName, Phone,
  IconKey)`, `NewVisitRequest(DoctorId, PatientName, PatientKind, OwnerName, OwnerPhone, PatientIconKey,
  IsNewPatient, TypeId, IsUrgent, Note)`, `EncounterDraft(Id, TicketId, DoctorId, DoctorName,
  DoctorShortName, DoctorSpecialty, DoctorInitials, DoctorAccent, PatientName, PatientKind,
  PatientIconKey, OwnerName, OwnerPhone, TypeId, TypeLabel, TypeAccent, IsUrgent, IsSynthetic, AtMinutes,
  DurationMinutes, Note, SourceTicketId)`.

### Domain (already exist — authoritative, gain no member in C02)
- `src/Animora.Domain/Scheduling/ReceptionQueueEntry.cs`:
  - `QueueState { Waiting=0, Called=1, InVisit=2, Done=3, Left=4 }` (NO `NoShow` member — see note ⚠️),
    `QueueSource { Appointment=0, WalkIn=1 }`.
  - `Create(tenantId, petId, ownerId, source, ticketNumber, clock, appointmentId?, priority?, note?)`,
    mutators `Call(doctorUserId, clock)`, `StartVisit(visitId)`, `Close(clock)`, `MarkLeft(clock)`,
    `SetPriority(int)`; `IsTransitionAllowed(from,to)`; consts `PriorityNormal=0`, `PriorityEmergency=2`.
  - Transition table: `Waiting→Called|Left`, `Called→InVisit|Waiting|Left`, `InVisit→Done`.
  - Props: `PetId, OwnerId, AppointmentId?, DoctorUserId?, State, Source, TicketNumber, EnqueuedUtc,
    CalledUtc?, ClosedUtc?, Priority, Note?, VisitId?`.
- `src/Animora.Domain/Scheduling/Appointment.cs`:
  - `AppointmentState { Scheduled=0, Confirmed=1, CheckedIn=2, InVisit=3, Completed=4, NoShow=5, Cancelled=6 }`,
    `AppointmentSource { Phone, WalkIn, Online, Followup, Recall }`.
  - Props: `PetId, OwnerId, DoctorUserId?, AppointmentTypeCode, StartUtc, DurationMinutes, State, Source,
    Note?, VisitId?`. Uses `AppointmentTypeCatalog` codes (A05).
- `src/Animora.Domain/Clinical/Visit.cs`:
  - `VisitState { Draft=0, InProgress=1, Completed=2, ReadyForInvoice=3, Invoiced=4, Cancelled=5 }`,
    `VisitReason { Checkup, Illness, Injury, Vaccination, Surgery, Dental, Followup, Other }`.
  - `Visit.Open(tenantId, petId, ownerId, doctorUserId, reason, clock, appointmentId?, chiefComplaint?)`
    → returns `Draft`; then `Start()` → `InProgress`. Props incl. `AppointmentId?, DoctorUserId, State`.
- `src/Animora.Domain/Identity/StaffRole.cs`: `enum StaffRole { Manager=0, Doctor=1, Receptionist=2,
  Cashier=3, Accountant=4 }`.
- `src/Animora.Domain/Identity/StaffMembership.cs`: `enum MembershipStatus { Active, Suspended, Ended }`;
  `StaffMembership` props `UserId, Role (StaffRole), Status (MembershipStatus)`, `IsActive`.

### Application ports (already exist)
- `IReceptionQueueRepository`: `FindAsync`, `RequireAsync`, `Add`, `AddRange`.
- `IAppointmentRepository`: `FindAsync`, `RequireAsync`, `Add`, `AddRange`.
- (A new query port `IReceptionQueueQueries`/`ITriageQueries` will be created mirroring
  `IPatientDirectoryQueries` — see tasks.)

### OperationIdFactory location ⚠️
- **Actual path is `src/Animora.Application/Correlation/OperationIdFactory.cs`** (phase's "Files" table says
  `Commands/` — that path is WRONG; use the real `Correlation/` path). Current body only has
  `New()`. **Add** `public string Deterministic(string seed)` computing a stable id from the seed
  (e.g. SHA-256 of seed → ULID-shaped id; must be pure/deterministic). Keep `New()`.

### Wire / Errors
- `src/Animora.Contracts.Wire/Commands/CommandTypes.cs`: add 7 consts + append them to `All`:
  `reception.ticket.admit`, `reception.ticket.enter`, `reception.ticket.leave`,
  `reception.ticket.noshow`, `reception.ticket.urgent`, `reception.walkin.create`, `visits.visit.start`.
  Existing C01 consts named `PatientsOwnerCreate = "patients.owner.create"` etc. Follow that style
  (`ReceptionTicketAdmit`, …, `VisitsVisitStart`).
- `src/Animora.Domain/Results/ErrorCodes.cs`: append 4 codes as `ErrorCode` statics (existing style
  `public static readonly ErrorCode Xxx = new("CODE", ErrorCode.Category);`). Categories available incl.
  `Conflict`, `Validation`, `Integrity`, `Infrastructure`, `NotFound`(check). Add:
  `TICKET_NOT_FOUND` (NotFound), `TICKET_STATE_INVALID` (Conflict), `WALKIN_PHONE_REQUIRED` (Validation),
  `DOCTOR_NOT_AVAILABLE` (Conflict). Give each a doc comment.

### Permissions ⚠️ IMPORTANT DEVIATION
- Phase text says permissions `reception.queue.manage` (6 cmds) and `visits.encounter.edit`.
  **These constants DO NOT EXIST.** `src/Animora.Domain/Identity/PermissionCodes.cs` actually has:
  `ReceptionManage = "reception.manage"`, `VisitsRead/VisitsWrite/VisitsComplete =
  "visits.read"/"visits.write"/"visits.complete"`.
  **Decision (record in PROGRESS deviations):** use existing `PermissionCodes.ReceptionManage` for the
  6 reception commands and `PermissionCodes.VisitsWrite` for the 2 start-visit commands. Do NOT invent
  new permission strings (would need role-permission seeding + tests, out of scope). Confirm the two
  constants compile before committing.

### DI (App.axaml.cs) — exact lines to change
- Line 182: `services.AddAnimoraApplication(builder => builder.AddIdentityUseCases().AddPatientsUseCases());`
  → add `.AddReceptionUseCases().AddVisitsUseCases()`.
- Line 232: `services.AddSingleton<IReceptionQueueService, MockReceptionQueueService>();`
  → `services.AddSingleton<IReceptionQueueService, ReceptionQueueService>();`
- Line 252: `services.AddSingleton<IVisitsTriageService, MockVisitsTriageService>();`
  → `services.AddSingleton<IVisitsTriageService, VisitsTriageService>();`
- Add `using Animora.Client.Services.Reception; using Animora.Client.Services.Visits;` (and the two
  registration namespaces) as needed.

### Persistence query DI
- Register the two new query classes in
  `src/Animora.Persistence.Sqlite/Context/LocalStoreServiceCollectionExtensions.cs` next to line 72
  (`services.TryAddScoped<IPatientDirectoryQueries, PatientDirectoryQueries>();`):
  `TryAddScoped<IReceptionQueueQueries, ReceptionQueueQueries>()` and
  `TryAddScoped<ITriageQueries, TriageQueries>()`.

### Seed hook
- `src/Animora.Persistence.Sqlite/Seed/LocalSeeder.cs` ctor takes `PatientsSampleData sampleData`; the
  sample block runs behind `storeOptions.Value.SeedSampleData` (~line 82) via
  `RunBlockAsync(PatientsSampleData.SampleKey, …)`. Add `ReceptionSampleData` the same way: inject it
  into the ctor, add a second `RunBlockAsync(ReceptionSampleData.SampleKey, …)` inside the same
  `if (SeedSampleData)` block. `ReceptionSampleData` mirrors `PatientsSampleData` shape:
  `public const string SampleKey = "reception.sample";`, `bool AlreadySeeded()`, `void Populate()`,
  ctor `(AnimoraLocalDbContext db, ILocalTenantContext tenant)`, deterministic `FixedClock` seed
  instant. Needs owners/pets present → reuse the ones `PatientsSampleData` seeds (query them) OR create
  its own; **must produce 6 "today" tickets (3 appointment-sourced, 2 walk-ins, 1 no-show) + 4 doctors
  (StaffMembership rows, distinct specialties)**, all deterministic. "today" = build times relative to
  the seed clock's date so the query's UTC-range picks them up — use the injected clock's `UtcNow.Date`
  when seeding tickets so they land in "today"'s range at runtime.

### Client service pattern (copy from C01 `PatientsService`)
- Singleton over `IServiceScopeFactory`; per call `await using var scope = scopes.CreateAsyncScope();`
  resolve `IDispatcher` + `ICurrentUserProvider`; queries via `provider.BuildQueryContext()` +
  `dispatcher.QueryAsync(...)`; commands via `await provider.BuildCommandContextAsync(null,null,null,default)`
  + `dispatcher.SendAsync(...)`; on failure `throw new InvalidOperationException(r.Error.Code)`.
  Mapping is a hand-written static extension class (`ReceptionMapping`/`VisitsMapping`) —
  read-model/domain enum → Contracts DTO **by NAME**, never a numeric cast (Domain vs Contracts enum
  values differ; C01 `PatientsMappingTests` pins this rule).

### ViewModel readiness gate (the C01 remedy — 1 line each, no .axaml change)
- `src/Animora.Desktop/Pages/Reception/ReceptionPageViewModel.cs`: load method is
  `RefreshAsync()` (line ~116). Add `await _readiness.WaitAsync(ct)` (or the store-readiness gate the
  ViewModel already has access to) as the first line. **Check what C01 injected** — look at how
  `IStoreReadinessGate`/`_readiness` is used in a C01 ViewModel (e.g. the Patients page VM) and mirror
  exactly (constructor inject `IStoreReadinessGate`, call `WaitAsync` first). If the VM has no ct, use
  `CancellationToken.None` / the field the VM already holds.
- `src/Animora.Desktop/Pages/Visits/VisitsTriagePageViewModel.cs`: load method `LoadAsync()` (line ~144).
  Same one-line gate at the top.

### Tests (xUnit; mirror existing C01 test files' style)
- `tests/Animora.Application.Tests/ReceptionQueueUseCaseTests.cs`
- `tests/Animora.Application.Tests/VisitsTriageUseCaseTests.cs`
- `tests/Animora.Persistence.Tests/ReceptionQueueQueryTests.cs`
  Existing references: `tests/Animora.Application.Tests/PatientsUseCaseTests.cs`,
  `PatientsMappingTests.cs`, `tests/Animora.Persistence.Tests/PatientDirectoryQueryTests.cs`.

---

## 3. Files to create / modify (consolidated, with corrections)

| Path | Action |
|---|---|
| `src/Animora.Application/UseCases/Reception/TicketRef.cs` | create — `readonly record struct`, parse/format `"apt:"+AppointmentId` / `"wlk:"+QueueEntryId`; unparsable → callers return `TICKET_NOT_FOUND` |
| `src/Animora.Application/UseCases/Reception/ReadModels/ReceptionReadModels.cs` | create — `QueueTicketReadModel`, `ReceptionDoctorReadModel` |
| `src/Animora.Application/UseCases/Reception/ReceptionQueueQuery.cs` + doctors query | create — 2 queries + handlers |
| `src/Animora.Application/UseCases/Reception/*Command.cs` (6) | create — Admit/EnterVisit/LeaveQueue/MarkNoShow/ToggleUrgent/CreateWalkIn + handlers + validators |
| `src/Animora.Application/UseCases/Reception/ReceptionRegistration.cs` | create — `AddReceptionUseCases()` |
| `src/Animora.Application/UseCases/Reception/ReceptionMapping.cs`? | NO — mapping lives in Client.Services (per Files table it's `Animora.Client.Services/Reception/ReceptionMapping.cs`). Domain→ReadModel state map (`QueueState`→`QueueTicketState`) is a total switch: put it where the query projection needs it (a small `ReceptionStateMap` in Application, or inline in the projection). Keep the Contracts mapping in Client.Services. |
| `src/Animora.Application/UseCases/Visits/ReadModels/VisitsReadModels.cs` | create — `DoctorRailReadModel`, `EncounterDraftReadModel`, `TriagePatientReadModel` |
| `src/Animora.Application/UseCases/Visits/*.cs` | create — 3 triage queries + `StartVisitFromTicketCommand` + `StartNewVisitCommand` + handlers/validators |
| `src/Animora.Application/UseCases/Visits/VisitsRegistration.cs` | create — `AddVisitsUseCases()` |
| `src/Animora.Application/Correlation/OperationIdFactory.cs` | modify — add `Deterministic(string seed)` |
| `src/Animora.Application/Ports/IReceptionQueueQueries.cs` | create — query port |
| `src/Animora.Application/Ports/ITriageQueries.cs` | create — query port |
| `src/Animora.Persistence.Sqlite/Queries/ReceptionQueueQueries.cs` | create — single-statement LEFT JOIN + UNION ALL, UTC-range day filter |
| `src/Animora.Persistence.Sqlite/Queries/TriageQueries.cs` | create — doctor rail + booked + patients projections |
| `src/Animora.Persistence.Sqlite/Seed/ReceptionSampleData.cs` | create — 6 tickets + 4 doctors, deterministic |
| `src/Animora.Persistence.Sqlite/Seed/LocalSeeder.cs` | modify — inject + run `ReceptionSampleData` |
| `src/Animora.Persistence.Sqlite/Context/LocalStoreServiceCollectionExtensions.cs` | modify — register 2 query ports |
| `src/Animora.Client.Services/Reception/ReceptionQueueService.cs` | create |
| `src/Animora.Client.Services/Reception/ReceptionMapping.cs` | create |
| `src/Animora.Client.Services/Visits/VisitsTriageService.cs` | create |
| `src/Animora.Client.Services/Visits/VisitTypeCatalog.cs` | create — static list built from `AppointmentTypeCatalog` (A05) |
| `src/Animora.Client.Services/Visits/VisitsMapping.cs` | create |
| `src/Animora.Contracts.Wire/Commands/CommandTypes.cs` | modify — +7 consts + `All` |
| `src/Animora.Desktop/App.axaml.cs` | modify — 2 DI swaps + 2 registration calls |
| `src/Animora.Desktop/Pages/Reception/ReceptionPageViewModel.cs` | modify — 1 readiness line |
| `src/Animora.Desktop/Pages/Visits/VisitsTriagePageViewModel.cs` | modify — 1 readiness line |
| `src/Animora.Desktop/Mocks/MockReceptionQueueService.cs` | delete |
| `src/Animora.Desktop/Mocks/MockVisitsTriageService.cs` | delete |
| `src/Animora.Domain/Results/ErrorCodes.cs` | modify — +4 codes |
| `tests/Animora.Application.Tests/ReceptionQueueUseCaseTests.cs` | create |
| `tests/Animora.Application.Tests/VisitsTriageUseCaseTests.cs` | create |
| `tests/Animora.Persistence.Tests/ReceptionQueueQueryTests.cs` | create |

---

## 4. TASK LIST (execute top-to-bottom; push after EACH task)

> Status legend: `[ ]` not started · `[~]` in progress · `[x]` done & pushed.

- [x] **T01 — Foundations: TicketRef + OperationIdFactory.Deterministic + 4 error codes**
  - Create `UseCases/Reception/TicketRef.cs` (`readonly record struct TicketRef`): fields for kind
    (Appointment/WalkIn) + id; `static bool TryParse(string, out TicketRef)`, `Format()`,
    `FromAppointment(id)`, `FromWalkIn(id)`. Format = `"apt:"+id` / `"wlk:"+id`.
  - Add `Deterministic(string seed)` to `Correlation/OperationIdFactory.cs` (pure, stable).
  - Append 4 `ErrorCode`s to `Domain/Results/ErrorCodes.cs`.
  - Build only the two touched projects (`dotnet build src/Animora.Application` +
    `src/Animora.Domain`) — targeted, quick.
  - **PUSH:** `feat(reception): C02 TicketRef, deterministic OperationId, 4 error codes`

- [x] **T02 — Wire CommandTypes (+7)**
  - Add 7 consts + extend `All` in `Contracts.Wire/Commands/CommandTypes.cs`.
  - **PUSH:** `feat(reception): C02 add 7 command types`

- [x] **T03 — Query ports + read models (Application)**
  - Create `Ports/IReceptionQueueQueries.cs`, `Ports/ITriageQueries.cs`.
  - Create `UseCases/Reception/ReadModels/ReceptionReadModels.cs`
    (`QueueTicketReadModel`, `ReceptionDoctorReadModel`).
  - Create `UseCases/Visits/ReadModels/VisitsReadModels.cs`
    (`DoctorRailReadModel`, `EncounterDraftReadModel`, `TriagePatientReadModel`).
  - Design read models to carry every field the Contracts DTOs need (see §2 Contracts lists) EXCEPT
    presentation-only bits derived in mapping (initials/accent/icon can be computed in mapping).
  - **PUSH:** `feat(reception): C02 query ports + read models`

- [x] **T04 — Reception queries (2) + commands (6) + validators + registration**
  - `ReceptionQueueQuery(DateOnly LocalDay)` → `IReadOnlyList<QueueTicketReadModel>` (delegates to
    `IReceptionQueueQueries`). `ReceptionDoctorsQuery()` → `IReadOnlyList<ReceptionDoctorReadModel>`
    (from `StaffMembership` where `Role==Doctor && Status==Active`).
  - 6 commands: `AdmitTicketCommand`, `EnterVisitCommand`, `LeaveQueueCommand`, `MarkNoShowCommand`,
    `ToggleTicketUrgentCommand`, `CreateWalkInCommand` — all `IJournaledCommand`,
    `[RequiresPermission(PermissionCodes.ReceptionManage)]`,
    `[Auditable(AuditCategory.Clinical, "<commandtype>")]`. Each parses `ticketId` via
    `TicketRef.TryParse` → `TICKET_NOT_FOUND` on failure.
  - Handlers implement **lazy `ReceptionQueueEntry` creation**: for an appointment-sourced ticket,
    find-or-create the row by `AppointmentId` inside the unit of work, then apply the domain mutator
    (`Call`/`StartVisit`/`Close`/`MarkLeft`/`SetPriority`). Map invalid domain transitions →
    `TICKET_STATE_INVALID`. NoShow: there is no `QueueState.NoShow` — model no-show as the appointment
    going `NoShow` and/or the queue row `MarkLeft` + a flag; **decision:** set `Appointment.MarkNoShow`
    (verify method name in Appointment.cs) and if a queue row exists `MarkLeft` it; the read model's
    no-show state comes from the appointment state. Record this in deviations.
  - `CreateWalkInCommand`: validate phone present else `WALKIN_PHONE_REQUIRED`; resolve-or-create
    Owner by `Owner.SearchKey` match on phone (reuse existing owner; create pet only if none of that
    owner matches the name); create `ReceptionQueueEntry` with `Source=WalkIn`; extra audit records
    `patients.owner.create`/`patients.pet.create` when it creates them.
  - `ReceptionRegistration.AddReceptionUseCases()` wiring (mirror `PatientsRegistration`).
  - Build `src/Animora.Application` (targeted).
  - **PUSH:** `feat(reception): C02 reception queries, commands, validators`

- [x] **T05 — Reception persistence query (single-statement derived queue)**
  - `Queries/ReceptionQueueQueries.cs implements IReceptionQueueQueries`.
  - `GetQueueAsync(startUtc, endUtc, ct)`: ONE statement — `LEFT JOIN` today's `Appointment`
    (StartUtc in `[startUtc,endUtc)`) to `ReceptionQueueEntries` (on `AppointmentId`) `UNION ALL`
    walk-in `ReceptionQueueEntries` (AppointmentId == null, EnqueuedUtc in range). No date function;
    range on ticks/`DateTimeOffset` compared to the two bounds. Project to `QueueTicketReadModel`
    (ticket id via `"apt:"`/`"wlk:"`, state derived: appt with no queue row → Waiting).
  - `GetDoctorsAsync(ct)`: `StaffMembership` Doctor+Active → `ReceptionDoctorReadModel`.
  - **PUSH:** `feat(reception): C02 single-statement derived-queue projection`

- [x] **T06 — Triage queries (3) + start-visit commands (2) + registration**
  - Queries in `UseCases/Visits/`: `TriageDoctorRailQuery(DateOnly LocalDay)` (per-doctor booked count,
    in-visit flag, next-free-slot-minute in ONE projection), `BookedForDoctorQuery(DoctorId, LocalDay)`,
    `TriagePatientsQuery(string? Search)` (top 50 recently-seen pets by last-visit desc, reuse
    `SearchKey` index — note: last-visit source may be absent pre-C03; if so order by pet name/last
    weight and record deviation).
  - Commands: `StartVisitFromTicketCommand(string TicketId)`, `StartNewVisitCommand(NewVisitRequest)` —
    both `IJournaledCommand`, `[RequiresPermission(PermissionCodes.VisitsWrite)]`, `[Auditable(Clinical,
    "visits.visit.start")]`. Create `Visit` via `Visit.Open(...)` then `Start()` → `InProgress`; link
    Pet/Owner/Doctor/Appointment?; write queue transition to `InVisit`; return
    `EncounterDraftReadModel`. **Idempotency:** `OperationId = OperationIdFactory.Deterministic("visit-start:"+ticketId)`;
    if a ticket already has an in-progress visit, return it (same OperationId) — do not create a second.
  - `TriageQueries` port impl (`ITriageQueries`).
  - `VisitsRegistration.AddVisitsUseCases()`.
  - Build `src/Animora.Application` (targeted).
  - **PUSH:** `feat(visits): C02 triage queries + idempotent start-visit commands`

- [x] **T07 — Triage persistence query**
  - `Queries/TriageQueries.cs implements ITriageQueries`: doctor rail (one projection over
    appointments), booked-for-doctor rows, recently-seen patients. UTC-range day filter, no N+1.
  - **PUSH:** `feat(visits): C02 triage projections`

- [x] **T08 — Client services + mappings + visit-type catalog**
  - `Client.Services/Visits/VisitTypeCatalog.cs`: static `IReadOnlyList<VisitTypeInfo>` built from
    `AppointmentTypeCatalog` (A05) — no DB/blocking.
  - `Client.Services/Reception/ReceptionMapping.cs`, `Client.Services/Visits/VisitsMapping.cs`:
    read-model/domain-enum → Contracts DTO **by name**; compute initials/accent/icon deterministically.
  - `Client.Services/Reception/ReceptionQueueService.cs` (real `IReceptionQueueService`),
    `Client.Services/Visits/VisitsTriageService.cs` (real `IVisitsTriageService`; `GetVisitTypes()`
    returns `VisitTypeCatalog.All` synchronously). Mirror C01 `PatientsService` scope/dispatch pattern.
    Service layer converts the client's Jalali-displayed day to a Gregorian `DateOnly` before passing
    to the query.
  - Build `src/Animora.Client.Services` (targeted).
  - **PUSH:** `feat(reception): C02 client services, mappings, visit-type catalog`

- [ ] **T09 — Sample data + seeder hook**
  - `Persistence.Sqlite/Seed/ReceptionSampleData.cs` (mirror `PatientsSampleData`): 4 doctor
    `StaffMembership` rows (distinct specialties), 6 "today" tickets (3 appointment-sourced via
    `Appointment` rows, 2 walk-in `ReceptionQueueEntry`, 1 no-show). Deterministic ids/clock; tickets
    dated to the seed clock's current day so the UTC-range query picks them up.
  - Wire into `LocalSeeder` behind `SeedSampleData` (inject + second `RunBlockAsync`,
    `SampleKey="reception.sample"`).
  - **PUSH:** `feat(reception): C02 deterministic sample tickets + doctors`

- [ ] **T10 — DI wiring + query-port registration + ViewModel gates + delete mocks**
  - `App.axaml.cs`: line 182 add `.AddReceptionUseCases().AddVisitsUseCases()`; line 232 swap to
    `ReceptionQueueService`; line 252 swap to `VisitsTriageService`; add usings.
  - `LocalStoreServiceCollectionExtensions.cs`: register `IReceptionQueueQueries`/`ITriageQueries`.
  - `ReceptionPageViewModel.RefreshAsync` + `VisitsTriagePageViewModel.LoadAsync`: add one readiness-gate
    line each (mirror the C01 VM injection of `IStoreReadinessGate`; check a C01 page VM first).
  - Delete `Mocks/MockReceptionQueueService.cs` + `Mocks/MockVisitsTriageService.cs`; ensure no DI or
    using references them.
  - Build `src/Animora.Desktop` (targeted) to confirm no dangling refs.
  - **PUSH:** `refactor(desktop): C02 swap DI to real services, gate VMs, delete 2 mocks`

- [ ] **T11 — Tests (3 classes)**
  - `ReceptionQueueUseCaseTests`: appt with no queue row projects Waiting; first state change creates
    exactly ONE `ReceptionQueueEntry`; second admit of same ticket → `TICKET_STATE_INVALID`; walk-in
    whose phone matches existing owner reuses that owner (no duplicate).
  - `VisitsTriageUseCaseTests`: `StartVisitFromTicketCommand` twice → one `Visit`, same `OperationId`;
    doctor rail booked counts match seeded appointments.
  - `ReceptionQueueQueryTests`: projection emits no duplicate ticket for an appt that also has a queue
    row; day filter uses a UTC-ticks range (assert `ToQueryString()` has the expected SELECT shape and
    no date function). Mirror `PatientDirectoryQueryTests` (tolerate EF `.param set`/`.param` preamble).
  - Run ONLY these filtered tests (`dotnet test --filter "FullyQualifiedName~ReceptionQueue|...
    VisitsTriage"`), not the whole suite.
  - **PUSH:** `test(reception): C02 reception + triage + query tests`

- [ ] **T12 — Acceptance gate + PROGRESS.md update**
  - Run the phase gate ONCE: `scripts/sandbox/build.sh full` (0/0), `scripts/sandbox/test.sh`
    (`TESTS OK`), `scripts/sandbox/run.sh` (`SMOKE RUN OK`). Fix anything that fails.
  - Update `backend-roadmap/PROGRESS.md`: mark C02 ✅ Done in the status table (row `| C02 |`), set
    "Next phase" to C03, append a "Repo state log" `### C02` entry listing the derived-queue rule +
    `TicketRef` format, 5 queries + 8 commands, `OperationIdFactory.Deterministic`,
    `ReceptionQueueQueries`/`TriageQueries`, `ReceptionSampleData`, 2 client services + 2 mappings +
    `VisitTypeCatalog`, 2 DI swaps, 2 ViewModel readiness lines, **mocks deleted**, `CommandTypes` += 7,
    `ErrorCodes` += 4, the 3 test classes, and **all deviations** (OperationIdFactory path corrected to
    `Correlation/`; permission constants reused `ReceptionManage`/`VisitsWrite` instead of the
    non-existent `reception.queue.manage`/`visits.encounter.edit`; no-show modeling; any
    last-visit-source substitution in `TriagePatientsQuery`).
  - **PUSH:** `docs(progress): C02 done — reception & visits triage on real DB`

- [ ] **T13 — Remove this plan file**
  - `git rm Plan/C02-reception-visits-triage-TODO.md` (and `Plan/` if now empty).
  - **PUSH:** `chore(plan): remove completed C02 plan`

---

## 5. Acceptance criteria (from the phase — verify at T12)
- `build.sh full` = 0 warnings / 0 errors.
- Neither mock exists; no DI line references them.
- `ReceptionQueueUseCaseTests`, `VisitsTriageUseCaseTests`, `ReceptionQueueQueryTests` green.
- Reception + triage screens operate against the local DB and survive restart.
- `run.sh` = `SMOKE RUN OK`.

## 6. Out of scope (do NOT touch)
- Encounter editing (vitals/notes/Rx) & completion → C03.
- Appointment creation/move/cancel & calendar → C04.
- Invoice creation → C07.
- SMS/notifications → never.
