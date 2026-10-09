# Execution Plan — S07 + S08 (Server handlers: clinical / scheduling / boarding)

> **Who reads this:** an AI model implementing phases **S07** and **S08** in a *fresh session*.
> This file is **pre-resolved**: every path, type, command-type, permission code, aggregate name,
> migration ordinal and port has already been looked up from the live tree and written below. **Do
> not re-explore to "understand".** Open only the specific file a task names, and only to copy a
> pattern or read a port's members. Implement top-to-bottom.
>
> **Phase source files (read once each, for the acceptance-criteria wording only):**
> `backend-roadmap/phases/PHASE-S07-server-handlers-clinical.md`,
> `backend-roadmap/phases/PHASE-S08-server-handlers-scheduling-boarding.md`.
>
> **🔒 PUSH LAW (locked, non-negotiable):** after **every** task below whose box you tick, run
> `scripts/git/push.sh "<conventional message>"` from the repo root. If a task produced a big/heavy
> change (large handler module, migration, many tests), push it on its own with a clear message.
> Not pushing = project death. If push exits code `3`, call the `setup_github_environment` tool once
> then re-run the same push. Work on branch **`genspark_ai_developer`** (first push:
> `scripts/git/push.sh -b genspark_ai_developer "<msg>"`).

---

## 0. Pre-resolved baseline (verified from the tree on 2026-10-09 — trust these, do not re-derive)

### Environment / build (never raw `dotnet`)
- Build: `scripts/sandbox/build.sh full` (expect **0 warnings / 0 errors**) · fast iter: `scripts/sandbox/build.sh fast`.
- Test: `scripts/sandbox/test.sh Server` (or a tighter filter like `scripts/sandbox/test.sh HandlersS07`).
- Smoke: `scripts/sandbox/run.sh` → must print `SMOKE RUN OK`.
- EF CLI: `.sandbox/tools/dotnet-ef` (NEVER `dotnet tool install`). Run `scripts/sandbox/install-prereqs.sh` once (idempotent) if tools are missing.
- PG tests read `ANIMORA_PG_TEST_CONNECTION`; unset ⇒ **skip with reason** locally, **fail** under `ANIMORA_REQUIRE_PG=1` (CI gate, owned by G07 — do not try to stand up PG yourself unless the env var is already set).
- **Do NOT run full/long test sweeps.** Build + the targeted `Server` filter only.

### Migration ordinals (server sequence, independent of client SQLite `031`)
- Physical head today = **`104_CommandIntake`** (`src/Animora.Persistence.PostgreSql/Migrations/Server/20261009160000_104_CommandIntake.cs`).
- **S07 migration ordinal = `105` → name `105_ClinicalCore`.**
- **S08 migration ordinal = `106` → name `106_SchedulingBoarding`.** (Re-list the folder at S08 start in case something shifted; take the next free ordinal, match on the `NNN` token.)
- Migration file-name convention in this repo: `<EF-timestamp>_<NNN>_<Name>.cs`, class `public partial class _<NNN>_<Name> : Migration`. EF generates `<timestamp>_<Name>`; you **rename** both the file and the class to prepend the ordinal (e.g. `..._ClinicalCore` → `..._105_ClinicalCore`, class `_105_ClinicalCore`). Mirror exactly how `104_CommandIntake` is named.
- After generating a migration, update `src/Animora.Persistence.PostgreSql/ServerSchemaVersion.cs` → `ExpectedHead` (S07 → `"105_ClinicalCore"`, S08 → `"106_SchedulingBoarding"`).

### Command-type inventory (live `CommandTypes.cs`, verified counts — this IS the work list)
S07 (26 wire types): `patients.*` (7), `reception.*` (6), `visits.*` (6), `vaccination.*` (7).
S08 (13 wire types): `scheduling.*` (5), `boarding.*` (8).
At execution start **re-run** to confirm nothing drifted:
```
grep -oE '"(patients|reception|visits|vaccination)\.[^"]*"' src/Animora.Contracts.Wire/Commands/CommandTypes.cs | sort   # S07
grep -oE '"(scheduling|boarding)\.[^"]*"'                   src/Animora.Contracts.Wire/Commands/CommandTypes.cs | sort   # S08
```

### Command → record → result → aggregate → permission map (PRE-RESOLVED — copy into catalog + permission table)

**S07 — Patients** (registration: `PatientsRegistration.AddPatientsUseCases`, file `src/Animora.Application/UseCases/Patients/*`):
| wire type | record (TResult) | AggregateTypeName | RequiresPermission |
|---|---|---|---|
| `patients.owner.create` | `CreateOwnerCommand`→`string` | `Owner` | `PatientsOwnerEdit` |
| `patients.owner.update` | `UpdateOwnerCommand`→`Unit` | `Owner` | `PatientsOwnerEdit` |
| `patients.owner.delete` | `DeleteOwnerCommand`→`Unit` | `Owner` | `PatientsOwnerEdit` |
| `patients.pet.create` | `CreatePetCommand`→`string` | `Pet` | `PatientsPetEdit` |
| `patients.pet.update` | `UpdatePetInfoCommand`→`Unit` | `Pet` | `PatientsPetEdit` |
| `patients.pet.delete` | `DeletePetCommand`→`Unit` | `Pet` | `PatientsPetEdit` |
| `patients.pet.weight.add` | `AddWeightEntryCommand`→`Unit` | `Pet` | `PatientsPetEdit` |

**S07 — Reception** (`ReceptionRegistration.AddReceptionUseCases`, `src/Animora.Application/UseCases/Reception/*`):
| wire type | record (TResult) | AggregateTypeName | RequiresPermission |
|---|---|---|---|
| `reception.ticket.admit` | `AdmitTicketCommand`→`Unit` | `ReceptionQueueEntry` | `ReceptionManage` |
| `reception.ticket.enter` | `EnterVisitCommand`→`Unit` | `ReceptionQueueEntry` | `ReceptionManage` |
| `reception.ticket.leave` | `LeaveQueueCommand`→`Unit` | `ReceptionQueueEntry` | `ReceptionManage` |
| `reception.ticket.noshow` | `MarkNoShowCommand`→`Unit` | **`Appointment`** ⚠️ | `ReceptionManage` |
| `reception.ticket.urgent` | `ToggleTicketUrgentCommand`→`Unit` | `ReceptionQueueEntry` | `ReceptionManage` |
| `reception.walkin.create` | `CreateWalkInCommand`→`string` | `ReceptionQueueEntry` | `ReceptionManage` |

> ⚠️ **`MarkNoShowCommand.AggregateTypeName == "Appointment"`** (not `ReceptionQueueEntry`). The `Appointment` aggregate/table is **created in S08**, not S07. Consequence: the `Appointment` EF mapping is an S08 deliverable, but `reception.ticket.noshow` is an S07 catalog/permission row. The catalog entry only needs the record type + property-shape (no table); the handler touches reception + appointment state. **Do not try to map `Appointment` in S07.** If the per-command S07 intake test for `reception.ticket.noshow` needs the appointment table to settle end-to-end, it is PG-gated and will be exercised once S08 adds the table — the pure/fake-store intake test (Accepted/Rejected on reception invariants) does not require the PG table. Register the catalog + permission row in S07; note the cross-aggregate dependency in `PROGRESS.md`.

**S07 — Visits** (`VisitsRegistration.AddVisitsUseCases`, `src/Animora.Application/UseCases/Visits/*`):
| wire type | record (TResult) | AggregateTypeName | RequiresPermission |
|---|---|---|---|
| `visits.visit.start` | `StartVisitFromTicketCommand`→`EncounterDraftReadModel` | `Visit` | `VisitsWrite` |
| `visits.visit.start` | `StartNewVisitCommand`→`EncounterDraftReadModel` | `Visit` | `VisitsWrite` |
| `visits.visit.vitals` | `SaveVisitVitalsCommand`→`Unit` | `Visit` | `VisitsWrite` |
| `visits.visit.note` | `SaveVisitNoteCommand`→`Unit` | `Visit` | `VisitsWrite` |
| `visits.rx.add` | `AddPrescriptionLineCommand`→`string` | `Visit` | `VisitsWrite` |
| `visits.rx.remove` | `RemovePrescriptionLineCommand`→`Unit` | `Visit` | `VisitsWrite` |
| `visits.visit.complete` | `CompleteVisitCommand`→`EncounterSummaryReadModel` | `Visit` | `VisitsComplete` |

> ⚠️ **`visits.visit.start` is produced by TWO records** (`StartVisitFromTicketCommand`, `StartNewVisitCommand`) → register **both** as catalog candidates under the same wire type (S06 resolves by payload property-shape; the catalog builder throws if two candidates share the same aggregate + property-set, so the two records must differ in constructor params — they do: `(TicketId)` vs `(DoctorId, PatientName, PatientKind, …)`). One permission row (`VisitsWrite`) for the wire type suffices.

**S07 — Vaccination** (`VaccinationRegistration.AddVaccinationUseCases`, `src/Animora.Application/UseCases/Vaccination/*`):
| wire type | record (TResult) | AggregateTypeName | RequiresPermission |
|---|---|---|---|
| `vaccination.record.administer` | `MarkVaccinationDoneCommand`→`string` | `VaccinationRecord` | `VaccinationWrite` |
| `vaccination.record.reminder` | `RecordVaccinationReminderCommand`→`string` | `VaccinationRecord` | `VaccinationWrite` |
| `vaccination.treatment.create` | `CreateTreatmentPlanCommand`→`string` | `TreatmentPlan` | `VaccinationWrite` |
| `vaccination.treatment.update` | `UpdateTreatmentPlanCommand`→`string` | `TreatmentPlan` | `VaccinationWrite` |
| `vaccination.treatment.cancel` | `CancelTreatmentPlanCommand`→`string` | `TreatmentPlan` | `VaccinationWrite` |
| `vaccination.treatment.occurrence.done` | `MarkTreatmentOccurrenceDoneCommand`→`string` | `TreatmentOccurrence` | `VaccinationWrite` |
| `vaccination.catalog.add` | `AddVaccineCatalogItemCommand`→`string` | `VaccineCatalogItem` | `VaccinationWrite` |

**S08 — Scheduling** (`SchedulingRegistration.AddSchedulingUseCases`, `src/Animora.Application/UseCases/Scheduling/*`):
| wire type | record (TResult) | AggregateTypeName | RequiresPermission |
|---|---|---|---|
| `scheduling.appointment.book` | `BookAppointmentCommand`→`string` | `Appointment` | `AppointmentsBook` |
| `scheduling.appointment.move` | `RescheduleAppointmentCommand`→`Unit` | `Appointment` | `AppointmentsMove` |
| `scheduling.appointment.cancel` | `CancelAppointmentCommand`→`Unit` | `Appointment` | `AppointmentsCancel` |
| `scheduling.appointment.checkin` | `CheckInAppointmentCommand`→`Unit` | `Appointment` | `AppointmentsBook` |
| `scheduling.bookingpatient.create` | `CreateBookingPatientCommand`→`string` | **`Pet`** | `AppointmentsBook` |

> ⚠️ **Appointment-settings commands are OUT of the `scheduling.*` wire scope.** `SaveAppointmentSettingsCommand`, `AddAppointmentTypeCommand`, `RemoveAppointmentTypeCommand`, `SetCalendarModeCommand`, `SetAppointmentSmsCommand` are `ISelfTransactedCommand` with **no `CommandTypeName`** (no `scheduling.settings.*` in the wire enum). They are client-UI-only and **must NOT be added to the catalog or permission table**. However the `AppointmentSettings` **table** is still mapped in S08 (the phase lists it) because `IAppointmentSettingsStore` reads/writes it for the scheduling handlers. Register these handlers via `AddSchedulingUseCases` (it already includes them) but add **zero** catalog/permission rows for them.
> ⚠️ **`scheduling.bookingpatient.create` has AggregateTypeName `Pet`** and creates owner+pet **through the S07 ports** (`IOwnerRepository`/`IPetRepository`) — S08 does **not** add a second owner/pet mapping. S08 depends on S07 being done.

**S08 — Boarding** (`BoardingRegistration.AddBoardingUseCases`, `src/Animora.Application/UseCases/Boarding/*`):
| wire type | record (TResult) | AggregateTypeName | RequiresPermission |
|---|---|---|---|
| `boarding.stay.create` | `CreateBoardingStayCommand`→`string` | `BoardingStay` | `BoardingManage` |
| `boarding.item.add` | `AddStayItemCommand`→`Unit` | `BoardingStay` | `BoardingManage` |
| `boarding.item.remove` | `RemoveStayItemCommand`→`Unit` | `BoardingStay` | `BoardingManage` |
| `boarding.stay.transfer` | `TransferStayRoomCommand`→`Unit` | `BoardingStay` | `BoardingManage` |
| `boarding.stay.reschedule` | `RescheduleStayCheckoutCommand`→`Unit` | `BoardingStay` | `BoardingManage` |
| `boarding.stay.checkout` | `CheckoutStayCommand`→`Unit` | `BoardingStay` | `BoardingCheckout` |
| `boarding.room.create` | `CreateRoomCommand`→`string` | `Room` | `SettingsWrite` |
| `boarding.room.update` | `UpdateRoomCommand`→`Unit` | `Room` | `SettingsWrite` |

> `PermissionCodes` live in **`Animora.Domain.Identity.PermissionCodes`** (`src/Animora.Domain/Identity/PermissionCodes.cs`) — all 12 codes above verified present. `[RequiresPermission]` attribute is read off the Application command record (import `using Animora.Domain.Identity;`). **Take the permission from the attribute, never invent.**

### Aggregates → tables → SQLite mirror source (pre-resolved)

**S07 aggregates** (Domain classes; namespaces verified):
| Aggregate | Domain ns | SQLite config to mirror | Server table(s) |
|---|---|---|---|
| `Owner` | `Animora.Domain.Patients` | `Configurations/Patients/OwnerConfiguration.cs` | `Owners` |
| `Pet` | `Animora.Domain.Patients` | `Configurations/Patients/PetConfiguration.cs` | `Pets` |
| `WeightEntry` | `Animora.Domain.Patients` | `Configurations/Patients/WeightEntryConfiguration.cs` | `WeightEntries` |
| `MedicalHistoryEntry` | `Animora.Domain.Clinical` | `Configurations/Clinical/MedicalHistoryEntryConfiguration.cs` | `MedicalHistory` |
| `ReceptionQueueEntry` | `Animora.Domain.Scheduling` | `Configurations/Scheduling/ReceptionQueueEntryConfiguration.cs` | `ReceptionQueueEntries` |
| `DocumentReference` | `Animora.Domain.Patients` | `Configurations/Patients/DocumentReferenceConfiguration.cs` | `DocumentReferences` |
| `Visit` (+ rx lines, handoff lines) | `Animora.Domain.Clinical` | `Configurations/Clinical/VisitConfiguration.cs` + `VisitBillingHandoffConfiguration.cs` | `Visits`, `VisitRxLines`, `VisitHandoffLines` |
| `VaccinationRecord` | `Animora.Domain.Clinical` | `Configurations/Clinical/VaccinationRecordConfiguration.cs` | `VaccinationRecords` |
| `TreatmentPlan` (+ `TreatmentOccurrence`) | `Animora.Domain.Clinical` | `Configurations/Clinical/TreatmentPlanConfigurations.cs` | `TreatmentPlans`, `TreatmentOccurrences` |
| `VaccineCatalogItem` | `Animora.Domain.Clinical` | `Configurations/Clinical/TreatmentPlanConfigurations.cs` (same file) | `VaccineCatalogItems` |

**S08 aggregates:**
| Aggregate | Domain ns | SQLite config to mirror | Server table(s) |
|---|---|---|---|
| `Appointment` | `Animora.Domain.Scheduling` | `Configurations/Scheduling/AppointmentConfiguration.cs` | `Appointments` |
| appointment settings | (see `IAppointmentSettingsStore`/`AppointmentSettings` DTO) | find settings config under `Configurations/Scheduling/` | `AppointmentSettings` |
| `Room` | `Animora.Domain.Boarding` | `Configurations/Boarding/RoomConfiguration.cs` | `Rooms` |
| `BoardingServiceDef` | `Animora.Domain.Boarding` | `Configurations/Boarding/BoardingServiceDefConfiguration.cs` | `BoardingServiceDefs` |
| `BoardingStay` (+ items, handoff) | `Animora.Domain.Boarding` | `Configurations/Boarding/BoardingStayConfiguration.cs` + `BoardingBillingHandoffConfiguration.cs` | `BoardingStays`, `BoardingStayItems`, `BoardingHandoffLines` |

> SQLite configs root: `src/Animora.Persistence.Sqlite/Configurations/`. Server configs root: `src/Animora.Persistence.PostgreSql/Configurations/` (flat folder — existing files like `StaffMembershipConfiguration.cs` use namespace `Animora.Persistence.PostgreSql.Configurations`; keep that flat namespace, you may group by subfolder if you prefer but match the existing flat convention used by S04).

### Ports to implement (PostgreSQL impls of the SAME Application interfaces)
All ports exist in `src/Animora.Application/Ports/`. **Read the one port file per repository** to get its exact members (the port is the contract). Implement in `src/Animora.Persistence.PostgreSql/Repositories/`.

**S07 ports (13):** `IOwnerRepository`, `IPetRepository`, `IWeightEntryRepository`, `IMedicalHistoryRepository`, `IReceptionQueueRepository`, `IReceptionQueueQueries`, `ITriageQueries`, `IVisitRepository`, `IVisitBillingHandoffWriter`, `IVaccinationRepository`, `IVaccinationQueries`, `ITreatmentPlanRepository`, `IClinicTimeZoneProvider`.
> **`IStaffMembershipRepository` is ALREADY implemented** by `src/Animora.Server/Authorization/ServerStaffMembershipRepository.cs` (registered in `AuthorizationServiceCollectionExtensions.cs` line 31). **Do NOT re-implement it in S07.** (Phase text says "S04 provides it"; in this tree S05 provides it — note this one-line deviation in `PROGRESS.md`, do nothing else.)
> **`IClinicTimeZoneProvider`:** the existing impl `src/Animora.Infrastructure.Local/Time/ClinicTimeZoneProvider.cs` is **client-only** (`Animora.Server` must never reference `Animora.Infrastructure.Local`). S07 must add a **new server-side** provider (e.g. `src/Animora.Persistence.PostgreSql/Time/ServerClinicTimeZoneProvider.cs`) that reads `Tenant.Timezone` (`Animora.Domain.Identity.Tenant.Timezone`, default `"Asia/Tehran"`) via the server context and falls back to `TimeZoneInfo` Asia/Tehran. Possibly also needed by S08 scheduling queries — if S08 finds it already registered by S07, reuse it.
> `ITreatmentPlanQueries` also exists in Ports; S07 handlers in scope use `IVaccinationQueries`/`ITreatmentPlanRepository`. Implement a port **only if** a handler you register resolves it from DI (check the handler ctor; don't add unused repos). The authoritative list is "ports the in-scope handlers inject".

**S08 ports (8):** `IAppointmentRepository`, `IAppointmentQueries`, `IAppointmentSettingsStore`, `IBoardingRepository`, `IBoardingQueries`, `IBoardingServiceRepository`, `IRoomRepository`, `IBoardingBillingHandoffWriter`.

### Copy-me patterns (exact, from the live tree)
- **PG repository:** `public sealed class OwnerRepository(AnimoraServerDbContext db) : TenantRepository<Owner>(db), IOwnerRepository { … }`. Base `TenantRepository<T>` (`Repositories/TenantRepository.cs`) gives `Set` (tenant+soft-delete filtered), `GetAsync` (tracked), `ReadAsync` (no-track), `Page`, `Add`, `SoftDelete`. Reads use `AsNoTracking()` + `Page(...)`; **never** `IgnoreQueryFilters`.
- **EF mapping:** mirror the SQLite config, swapping SQLite converters for PG types. The shared shape (uuid `Version` + trigger, `timestamptz(6)` `UpdatedAt`, `varchar(26)` `Id`/`TenantId`, `boolean Deleted`, integer enums, named `Tenant`/`SoftDelete` filters) is applied automatically by `ServerModelConventions.ApplyTenancy` to **every `SyncableEntity`** — you do NOT re-declare those columns. In the config you declare only: table name + schema (`builder.ToTable("Owners","public")`), key, child collections (composite PK `(OwnerId, childKey)`, cascade FK), `Money`→`<N>Minor bigint`+`<N>Currency varchar(3)`, `Quantity`→scaled `bigint`(×1000)+unit, and **tenant-first indexes** via `ServerModelConventions.TenantIndex(builder, "<Col>", …)`. Reference existing `src/Animora.Persistence.PostgreSql/Configurations/StaffMembershipConfiguration.cs`.
- **DbSets:** add one `DbSet<T>` per root aggregate to `AnimoraServerDbContext` (`Context/AnimoraServerDbContext.cs`). `modelBuilder.ApplyConfigurationsFromAssembly(...)` already picks up any `IEntityTypeConfiguration<>` in the assembly, so you only add the `DbSet` properties + (if needed) child entity sets. Children reached via owner navigation don't need their own `DbSet`.
- **Migration:** `.sandbox/tools/dotnet-ef migrations add ClinicalCore --project src/Animora.Persistence.PostgreSql --startup-project src/Animora.Persistence.PostgreSql --context AnimoraServerDbContext --output-dir Migrations/Server` → then **rename** file+class to the `105_` ordinal, and in the migration `Up` add `VersionTrigger.Apply(migrationBuilder, "<Table>")` for **every** `SyncableEntity` table created (and `VersionTrigger.Drop` in `Down`). The trigger function itself already exists (made in `101_ServerFoundation`).
- **Registration module:** create `src/Animora.Server/Handlers/ServerHandlerModuleS07.cs` exposing a method that (a) is called from the `configureHandlers` delegate of `AddAnimoraCommandPlane` to call `builder.AddPatientsUseCases().AddReceptionUseCases().AddVisitsUseCases().AddVaccinationUseCases()`, and (b) populates the `CommandPayloadCatalogBuilder` via `catalog.Add<TCommand>(CommandTypes.<Const>, "<Aggregate>")` for each row above. Wire it into `Program.cs` line 56: change `builder.Services.AddAnimoraCommandPlane();` → `builder.Services.AddAnimoraCommandPlane(ServerHandlerModuleS07.AddHandlers, ServerHandlerModuleS07.AddPayloads);` (S08 extends the same two delegates — compose both modules).
- **Permission rows:** edit `src/Animora.Server/Authorization/CommandPermissionTable.cs` → add entries to the `Rows` dictionary `{ CommandTypes.<Const>, PermissionCodes.<Code> }` (keep sorted by command type). `CommandAuthorizer` fails closed for unmapped types.
- **Repository DI:** follow `src/Animora.Persistence.PostgreSql/DependencyInjection/IdentityPersistenceServiceCollectionExtensions.cs` (`services.AddScoped<IPort, Impl>()`). Decide where the new repos register — simplest is a new `ClinicalPersistenceServiceCollectionExtensions.AddAnimoraClinicalPersistence()` called from the server composition (find where `AddAnimoraIdentityPersistence()` / `AddAnimoraPostgreSql()` are invoked in `Program.cs` and add the new call right after). Keep it in `Animora.Persistence.PostgreSql` so Npgsql stays confined there.

### Test harness (reuse — do NOT invent a container)
- Server tests live in `tests/Animora.Server.Tests/`. Put S07 tests in `HandlersS07/`, S08 in `HandlersS08/`.
- Fake-store intake harness already exists: `tests/Animora.Server.Tests/Commands/CommandTestSupport.cs` (`IntakeTestFixture`, `FakeIntakeStore`, `FakeDispatcher`, `FixedClock`, etc.). Per-command intake tests build a `CommandPayloadCatalog` with the real record, make a `CommandEnvelope`, and drive `CommandIntakeService` — mirror `tests/Animora.Server.Tests/Commands/CommandIntakeServiceTests.cs` and `CommandPayloadCatalogTests.cs`.
- Model/mapping tests (pure, no PG) assert: every new table has a TenantId-first index, named `Tenant`+`SoftDelete` filters, uuid concurrency token — build the model from `AnimoraServerDbContext` with an in-memory/`UseNpgsql` design-time options and inspect `IModel`. Mirror any existing test under `tests/Animora.Server.Tests/Persistence/`.
- PG-gated tests (migration-applies, end-to-end round-trip, S08 concurrency) use `ANIMORA_PG_TEST_CONNECTION`; write them so they **skip with a reason** when the env var is absent (mirror the existing PG-gated identity/persistence tests). Do not block on them locally.

### Hard rules (enforced by acceptance criteria — violating = fail)
- `git diff --stat -- src/Animora.Desktop src/Animora.Persistence.Sqlite src/Animora.Client.Services` must stay **empty** (touch none of these).
- `grep -rn "IgnoreQueryFilters" src/Animora.Persistence.PostgreSql/Repositories` must return **nothing**.
- No new `CommandTypes`, no wire change. Only `Animora.Persistence.PostgreSql` may reference Npgsql.
- C# style: `sealed` default; async takes `CancellationToken ct = default` + `ConfigureAwait(false)` in libs; no `.Result`/`.Wait()`; expected failures return `Result`/`Result<T>`; ids via `IdGenerator` (ULID), never `Guid`; `IClock`/`TimeProvider` injected.
- Migrations additive, never edit an already-shipped one. Paging mandatory (max 200 — `PageRules`).

---

## 1. TASK LIST

> Tick `[x]` when done. **Push after every ticked task.** Suggested commit messages are in each task.

### Phase S07 — session a (persistence + migration)

- [ ] **T1 · Confirm inventory + baseline.** Re-run the two `grep` inventory commands (§0) and the migration-folder `ls`; confirm S07 = 26 types, head = `104_CommandIntake`, next ordinal `105`. If counts differ, record the new list — it's the real work list. *(No code change → fold this into T2's push; no standalone push needed.)*

- [ ] **T2 · S07 EF configurations + DbSets.** Add `IEntityTypeConfiguration<>` files under `src/Animora.Persistence.PostgreSql/Configurations/` for: `Owner`, `Pet`, `WeightEntry`, `MedicalHistoryEntry`, `ReceptionQueueEntry`, `DocumentReference`, `Visit` (+`VisitRxLines`,`VisitHandoffLines`), `VaccinationRecord`, `TreatmentPlan` (+`TreatmentOccurrence`), `VaccineCatalogItem` — mirroring the SQLite configs named in §0, declaring only table/schema/key/children/Money/Quantity/tenant-indexes (conventions supply the rest). Add the root `DbSet<T>` properties to `AnimoraServerDbContext`. **Do NOT map `Appointment` here** (S08). Build `fast` until the model compiles. **Push:** `feat(server): S07 clinical EF mappings + DbSets`.

- [ ] **T3 · S07 migration `105_ClinicalCore`.** Generate with `.sandbox/tools/dotnet-ef migrations add ClinicalCore …` (command in §0), rename file+class to `105_ClinicalCore`, add `VersionTrigger.Apply(..., "<Table>")` for every new `SyncableEntity` table in `Up` (+ `Drop` in `Down`), update `ServerSchemaVersion.ExpectedHead = "105_ClinicalCore"`. Verify the snapshot regenerated. Build `full` = 0/0. **Push (own commit — heavy):** `feat(server): S07 migration 105_ClinicalCore + version triggers`.

- [ ] **T4 · S07 model tests (pure).** Add `tests/Animora.Server.Tests/HandlersS07/` model tests: each new table has TenantId-first index, named `Tenant`+`SoftDelete` filters, uuid concurrency token; `ReplicatedSetTests.TreatmentPlan_AndVaccineCatalog_AreMapped_WithTenantFilter` (asserts `TreatmentPlan`, `TreatmentOccurrence`, `VaccineCatalogItem` exist in the model). Run `scripts/sandbox/test.sh HandlersS07` (model tests only). **Push:** `test(server): S07 model/replicated-set tests`.

### Phase S07 — session b (ports + registration + handler tests)

- [ ] **T5 · S07 repository/query port impls.** In `src/Animora.Persistence.PostgreSql/Repositories/`, implement the 12 S07 ports (list in §0; **skip `IStaffMembershipRepository`** — already done). Read each port file for its exact members. Add the server `IClinicTimeZoneProvider` impl reading `Tenant.Timezone`. Add a `ClinicalPersistenceServiceCollectionExtensions.AddAnimoraClinicalPersistence()` registering them `AddScoped<IPort,Impl>()`, and call it from `Program.cs` right after the existing persistence registrations. Build `fast`. **Push:** `feat(server): S07 PostgreSQL repositories + clinic timezone provider`.

- [ ] **T6 · S07 handler module + catalog + permission rows.** Create `src/Animora.Server/Handlers/ServerHandlerModuleS07.cs` with `AddHandlers(ApplicationRegistrationBuilder)` (calls the 4 `AddXxxUseCases`) and `AddPayloads(CommandPayloadCatalogBuilder)` (`catalog.Add<TCommand>(CommandTypes.<Const>, "<Aggregate>")` for every S07 row incl. BOTH `visits.visit.start` records and `reception.ticket.noshow` as `Appointment`). Wire both into `AddAnimoraCommandPlane(...)` in `Program.cs`. Add the S07 rows to `CommandPermissionTable.Rows`. Register an `IAggregateVersionReader` per in-scope aggregate (or keep `NoAggregateVersionReader` if acceptable — note choice). Build `full` = 0/0. **Push:** `feat(server): S07 handler registration + payload catalog + permission rows`.

- [ ] **T7 · S07 handler + re-derive + coverage tests.** Add to `HandlersS07/`: `HandlerCoverageTests.EveryS07CommandType_HasHandler_CatalogEntry_AndPermissionRow` (iterate `CommandTypes.All` filtered to the 4 prefixes; assert registry + catalog + permission row; `ReservedCommandTypes` members must instead reject `HANDLER_NOT_REGISTERED` — none of the S07 prefixes are reserved today, assert accordingly); one `Settles_<CommandName>` fact per type via `IntakeTestFixture` (Accepted on valid payload; Rejected with the Domain `ErrorCode` on the family's primary invariant; idempotent replay returns stored result); `ServerS07RederiveTests` for the re-derive rules (pet species immutable `PET_SPECIES_IMMUTABLE`; owner-with-active-pets `OWNER_HAS_ACTIVE_PETS`; weight bounds `WEIGHT_OUT_OF_RANGE`; visit diagnosis-required `VISIT_DIAGNOSIS_REQUIRED` + locked `VISIT_LOCKED`; treatment schedule `TREATMENT_SCHEDULE_INVALID|TOO_LARGE`, occurrence idempotency `TREATMENT_OCCURRENCE_ALREADY_DONE`; `VACCINE_CATALOG_CODE_TAKEN`). Add PG-gated stubs `S07MigrationTests.Applies_OnTopOfPreviousHead` + `S07EndToEndTests.CommandsRoundTrip_ThroughIntake_WithTenantIsolation` (skip-with-reason when no PG). `scripts/sandbox/test.sh Server` green (PG ones skip). **Push:** `test(server): S07 coverage + per-command + re-derive tests`.

- [ ] **T8 · S07 close-out.** `scripts/sandbox/build.sh full` = 0/0; `scripts/sandbox/run.sh` = `SMOKE RUN OK`; confirm the forbidden `git diff --stat` is empty and the `IgnoreQueryFilters` grep is empty. Update `backend-roadmap/PROGRESS.md`: set `S07` row → `✅ Done` (date + one-line), advance **Next phase → `S08`**, append a `### S07` Repo-state-log entry (projects, tables `Owners…VaccineCatalogItems`, migration `105_ClinicalCore`, error codes referenced, test classes), log deviations (the `IStaffMembershipRepository`=S05 note; the `reception.ticket.noshow`→`Appointment` cross-aggregate note). **Push:** `feat(server): S07 server handlers i-a` *(this is the phase's canonical commit message)*.

### Phase S08 — session a (persistence + migration)

- [ ] **T9 · S08 inventory + baseline.** Re-run the `scheduling|boarding` grep (expect 13) and `ls Migrations/Server` (expect head now `105_ClinicalCore`, next ordinal `106`). Read `src/Animora.Domain/Scheduling/SlotConflictPolicy.cs` once (pure, reused unchanged). *(No standalone push; fold into T10.)*

- [ ] **T10 · S08 EF configurations + DbSets.** Add configs for `Appointment`, `AppointmentSettings`, `Room`, `BoardingServiceDef`, `BoardingStay` (+`BoardingStayItems`,`BoardingHandoffLines`) mirroring the SQLite configs in §0. Add root `DbSet`s to `AnimoraServerDbContext`. Reuse S07's `Owner`/`Pet` mapping (no duplication). Build `fast`. **Push:** `feat(server): S08 scheduling/boarding EF mappings + DbSets`.

- [ ] **T11 · S08 migration `106_SchedulingBoarding`.** Generate (`migrations add SchedulingBoarding …`), rename to `106_`, add `VersionTrigger.Apply` for every new `SyncableEntity` table, update `ServerSchemaVersion.ExpectedHead = "106_SchedulingBoarding"`. Build `full` = 0/0. **Push (own commit — heavy):** `feat(server): S08 migration 106_SchedulingBoarding + version triggers`.

- [ ] **T12 · S08 model tests (pure).** `HandlersS08/` model tests: tenant-first indexes, named filters, uuid token for the new tables. `scripts/sandbox/test.sh HandlersS08`. **Push:** `test(server): S08 model tests`.

### Phase S08 — session b (ports + registration + handler tests)

- [ ] **T13 · S08 port impls + DI.** Implement the 8 S08 ports (§0) in `Repositories/`; `IBoardingBillingHandoffWriter` writes `BoardingHandoffLines` (read later by S09). Room-capacity contention uses the optimistic uuid `Version` + retry in `ServerUnitOfWork` — **no** `SELECT … FOR UPDATE`/`FromSql`. Register via a `AddAnimoraSchedulingBoardingPersistence()` extension (or extend the S07 clinical one) and call from `Program.cs`. Build `fast`. **Push:** `feat(server): S08 PostgreSQL repositories + billing hand-off writer`.

- [ ] **T14 · S08 handler module + catalog + permission rows.** Create `src/Animora.Server/Handlers/ServerHandlerModuleS08.cs` (`AddHandlers` → `AddSchedulingUseCases().AddBoardingUseCases()`; `AddPayloads` → the 13 S08 rows — **exclude the 5 appointment-settings `ISelfTransactedCommand` records**). Compose with S07 in the `AddAnimoraCommandPlane(...)` call in `Program.cs` (both modules' `AddHandlers`/`AddPayloads`). Add S08 rows to `CommandPermissionTable.Rows`. Register `IAggregateVersionReader` for `Appointment`/`Room`/`BoardingStay`. Build `full` = 0/0. **Push:** `feat(server): S08 handler registration + payload catalog + permission rows`.

- [ ] **T15 · S08 handler + slot + coverage tests.** `HandlersS08/`: `HandlerCoverageTests.EveryS08CommandType_HasHandler_CatalogEntry_AndPermissionRow` (prefixes `scheduling.*`,`boarding.*`; reserved→`HANDLER_NOT_REGISTERED`); `Settles_<CommandName>` per type; `ServerS08RederiveTests` (boarding totals re-derive); `SlotConflictServerTests.Overlap_Rejected_Adjacent_Allowed` (pure, must pass — asserts `APPOINTMENT_SLOT_CONFLICT` on overlap, adjacent allowed; `DOCTOR_OUTSIDE_WORKING_HOURS` reading `StaffMembership` hours); boarding invariants `STAY_INVOICE_REQUIRED`, `ROOM_AT_CAPACITY`. PG-gated stubs (skip w/ reason): `S08ConcurrencyTests.TwoDevices_SameSlot_OneWins`, `S08MigrationTests.Applies_OnTopOfPreviousHead`, `S08EndToEndTests.CommandsRoundTrip_ThroughIntake_WithTenantIsolation`. `scripts/sandbox/test.sh Server` green. **Push:** `test(server): S08 coverage + per-command + slot-conflict tests`.

- [ ] **T16 · S08 close-out.** `build.sh full` = 0/0; `run.sh` = `SMOKE RUN OK`; forbidden `git diff --stat` empty; `IgnoreQueryFilters` grep empty. Update `PROGRESS.md`: `S08` row → `✅ Done`, Next phase → `S09`, append `### S08` Repo-state-log entry (tables `Appointments…BoardingServiceDefs`, migration `106_SchedulingBoarding`, test classes), log any deviation. **Push:** `feat(server): S08 server handlers i-b`.

### Finalize

- [ ] **T17 · Delete this plan file.** `git rm Plan/S07-S08-server-handlers-clinical-scheduling-boarding.md` (do NOT touch any other file, do NOT edit the roadmap phase files). **Push:** `chore(plan): remove completed S07-S08 execution plan`.

---

## 2. Quick deviation-logging reminder
If a snippet here diverges from the tree (compile error proves it), fix the **smallest** thing and add one line to `PROGRESS.md` "Decision & deviation log". Do not re-architect. Known deviations already captured above: (1) `IStaffMembershipRepository` provided by S05 not S04; (2) `reception.ticket.noshow` aggregate = `Appointment` (table born in S08); (3) appointment-settings commands are out of the `scheduling.*` wire scope (handlers registered, no catalog/permission rows).
