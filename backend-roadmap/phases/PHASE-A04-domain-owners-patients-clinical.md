# Phase A04 — Domain: Owners, Patients, Clinical

**Band:** M · **Track:** A · **Depends on:** A02 · **Satisfies:** R-B01
**Runnable after this phase:** desktop offline yes · server required no

## Read before starting
- `backend-roadmap/CONVENTIONS.md` §3, §4, §6 (item 9), §9, §11, §12
- `backend-roadmap/PROGRESS.md`
- `ARCHITECTURE.md` §6 (bounded contexts), §8.5 (conflict matrix: medical history preserved,
  demographic field = explicit merge)
- `src/Animora.Domain/Primitives/*`, `src/Animora.Domain/ValueObjects/*`,
  `src/Animora.Domain/Results/ErrorCodes.cs`
- `src/Animora.Contracts/Patients/PatientModels.cs`, `src/Animora.Contracts/Patients/PatientDetailModels.cs`
- `src/Animora.Contracts/Visits/VisitsEncounterModels.cs`,
  `src/Animora.Contracts/Vaccination/VaccinationModels.cs`

## Goal
Model the clinical core: owners, pets, weight history, document references, the visit/encounter
aggregate with vitals/diagnosis/plan/prescription lines, vaccination records with the due-date rule, and
the append-only medical history entry. These are the entities every clinical screen will read in Track C.

## Design decisions (pre-made — implement, do not re-derive)
- **Folders:** `src/Animora.Domain/Patients/`, `src/Animora.Domain/Clinical/`.
- **Enums are domain-owned**, defined here even where a similarly-named enum exists in
  `Animora.Contracts` — Track C maps between them. Never reference `Animora.Contracts` from Domain.
- `Patients/Owner.cs` — `sealed class Owner : AggregateRoot`: `FullName`, `PhonePrimary`,
  `PhoneSecondary?`, `NationalId?`, `Email?`, `Address?`, `City?`, `OwnerGender Gender`, `Notes?`,
  `DateTimeOffset RegisteredUtc`, `bool IsBlocked`, `string? BlockReason`.
  `enum OwnerGender { Unspecified, Male, Female }`.
  `PhonePrimary` is normalized (digits only, leading `0` preserved) by a `static string NormalizePhone`.
- `Patients/Pet.cs` — `sealed class Pet : AggregateRoot`: `OwnerId`, `Name`, `PetSpecies Species`,
  `Breed?`, `PetGender Gender`, `DateTimeOffset? BirthDateUtc`, `bool IsNeutered`, `Color?`,
  `MicrochipCode?`, `DateTimeOffset? MicrochipImplantUtc`, `PetStatus Status`, `string? AllergyNotes`,
  `string? ChronicNotes`, `DateTimeOffset RegisteredUtc`.
  `enum PetSpecies { Dog, Cat, Bird, Rodent, Reptile, Other }`,
  `enum PetGender { Unknown, Male, Female }`,
  `enum PetStatus { Active, Inactive, Deceased }`.
  Computed (not stored): `int? AgeMonthsAt(DateTimeOffset nowUtc)`.
  Invariant: `Species` is immutable after creation (a species change is a new record) — enforced by a
  private setter and no mutator.
- `Patients/WeightEntry.cs` — `sealed class WeightEntry : SyncableEntity`: `PetId`,
  `decimal WeightKg` (> 0, ≤ 300), `DateTimeOffset MeasuredUtc`, `string? Note`, `string RecordedByUserId`.
  Append-only: no mutator; a correction is a new entry plus a `CorrectsEntryId?`.
- `Patients/DocumentReference.cs` — `sealed class DocumentReference : SyncableEntity`:
  `OwnerId?`, `PetId?`, `VisitId?`, `Title`, `DocumentKind Kind`, `string FileId`
  (the id of the D12 file metadata row — **never a filesystem path**, A-RULE 020),
  `long SizeBytes`, `string ContentHashSha256`, `DateTimeOffset UploadedUtc`, `string UploadedByUserId`.
  `enum DocumentKind { Lab, Imaging, Prescription, Consent, Invoice, Other }`.
  Invariant: at least one of `OwnerId`/`PetId`/`VisitId` is non-null.
- `Clinical/Visit.cs` — `sealed class Visit : AggregateRoot` (the encounter aggregate root):
  `PetId`, `OwnerId`, `string? AppointmentId`, `string DoctorUserId`, `VisitState State`,
  `VisitReason Reason` (enum `Checkup, Illness, Injury, Vaccination, Surgery, Dental, Followup, Other`),
  `string? ChiefComplaint`, `DateTimeOffset StartedUtc`, `DateTimeOffset? CompletedUtc`,
  `PetVitals? Vitals`, `string? Diagnosis`, `string? TreatmentPlan`, `string? DoctorNotes`,
  `string? InvoiceId`, `IReadOnlyList<PrescriptionLine> Prescriptions`,
  `IReadOnlyList<VisitServiceLine> ServiceLines`.
  `enum VisitState { Draft, InProgress, Completed, ReadyForInvoice, Invoiced, Cancelled }`.
  **State machine (the only allowed transitions):**
  ```
  Draft          -> InProgress | Cancelled
  InProgress     -> Completed  | Cancelled
  Completed      -> ReadyForInvoice
  ReadyForInvoice-> Invoiced
  Invoiced       -> (terminal)
  Cancelled      -> (terminal)
  ```
  Methods returning `Result`: `Start`, `SetVitals`, `AddPrescription`, `RemovePrescription`,
  `AddServiceLine`, `RemoveServiceLine`, `Complete(IClock)`, `MarkReadyForInvoice()`,
  `AttachInvoice(string invoiceId)`, `Cancel(string reason, IClock)`.
  Invariants: `Complete` requires a non-empty `Diagnosis`; lines are immutable once `State >= Invoiced`
  (`ErrorCodes.VISIT_LOCKED`); `AttachInvoice` requires `State == ReadyForInvoice`.
  `Complete` raises `VisitCompletedEvent`; `MarkReadyForInvoice` raises `VisitReadyForInvoiceEvent`.
- `Clinical/PetVitals.cs` — `sealed record PetVitals(decimal? WeightKg, decimal? TemperatureC,
  int? HeartRateBpm, int? RespiratoryRate, string? MucousMembrane, string? Hydration)`.
- `Clinical/PrescriptionLine.cs` — `sealed record PrescriptionLine(string LineId, string? ProductId,
  string DrugName, string Dosage, string Frequency, int DurationDays, string? Instructions)`.
  `ProductId` links to inventory when the drug is dispensed from stock (A06).
- `Clinical/VisitServiceLine.cs` — `sealed record VisitServiceLine(string LineId, string? CatalogItemId,
  string Title, Quantity Quantity, Money UnitPrice, VisitLineSource Source)`.
  `enum VisitLineSource { Service, Product, Custom }`. This is the **handoff payload** Sales reads to
  build an invoice (A07/C07) — the visit never computes invoice totals itself.
- `Clinical/VaccinationRecord.cs` — `sealed class VaccinationRecord : AggregateRoot`:
  `PetId`, `string VaccineCode`, `string VaccineName`, `int DoseNumber`, `int? TotalDoses`,
  `DateTimeOffset AdministeredUtc`, `string AdministeredByUserId`, `string? BatchNumber`,
  `DateTimeOffset? NextDueUtc`, `VaccinationState State`, `string? VisitId`, `string? Notes`.
  `enum VaccinationState { Administered, DueSoon, Overdue, Skipped }`.
  **Due-date rule (authoritative):** `NextDueUtc = AdministeredUtc + VaccineSchedule.IntervalFor(
  VaccineCode, DoseNumber)`; when `DoseNumber == TotalDoses`, the interval is the annual booster
  (365 days). `State` is **derived**, never stored-and-drifted: `VaccinationRecord.StateAt(nowUtc)`
  returns `Overdue` when `NextDueUtc < nowUtc`, `DueSoon` within 30 days, else `Administered`.
- `Clinical/VaccineSchedule.cs` — static table `IReadOnlyDictionary<string, int[]>` of
  code → per-dose interval in days. Seed: `"DHPP" → [21,21,365]`, `"RABIES" → [365]`,
  `"FVRCP" → [21,21,365]`, `"FELV" → [21,365]`, `"LEPTO" → [21,365]`, `"BORDETELLA" → [365]`.
  `IntervalFor(code, doseNumber)` clamps to the last entry and defaults to 365 for unknown codes.
- `Clinical/MedicalHistoryEntry.cs` — `sealed class MedicalHistoryEntry : SyncableEntity`
  (**append-only, never updated, never soft-deleted for business reasons**):
  `PetId`, `MedicalHistoryKind Kind`, `string Title`, `string? Detail`,
  `DateTimeOffset OccurredUtc`, `string? SourceEntityId`, `string? SourceEntityType`,
  `string RecordedByUserId`. `enum MedicalHistoryKind { Visit, Vaccination, Surgery, Lab, Weight,
  Prescription, Boarding, Note }`. No mutator methods at all.
- **Domain events** in `src/Animora.Domain/Events/`: `VisitCompletedEvent(VisitId, PetId, OwnerId,
  DoctorUserId)`, `VisitReadyForInvoiceEvent(VisitId, PetId, OwnerId)`,
  `VaccinationAdministeredEvent(RecordId, PetId, VaccineCode, NextDueUtc)`,
  `PetRegisteredEvent(PetId, OwnerId)` — all deriving `DomainEvent`.
  Each event's consumers append a `MedicalHistoryEntry` (wired in A10's dispatcher, not here).
- **New error codes appended to `ErrorCodes.cs`:** `PET_SPECIES_IMMUTABLE`, `VISIT_INVALID_TRANSITION`,
  `VISIT_DIAGNOSIS_REQUIRED`, `VISIT_LOCKED`, `WEIGHT_OUT_OF_RANGE`, `DOCUMENT_TARGET_REQUIRED`,
  `VACCINE_DOSE_INVALID`.

## Files to create / modify
| Path | Action | Contents (one line) |
|---|---|---|
| `src/Animora.Domain/Patients/Owner.cs` | create | owner aggregate + phone normalization |
| `src/Animora.Domain/Patients/Pet.cs` | create | pet aggregate + immutable species + age calc |
| `src/Animora.Domain/Patients/WeightEntry.cs` | create | append-only weight measurement |
| `src/Animora.Domain/Patients/DocumentReference.cs` | create | file metadata reference (never a path) |
| `src/Animora.Domain/Clinical/Visit.cs` | create | encounter aggregate + state machine |
| `src/Animora.Domain/Clinical/PetVitals.cs` | create | vitals value object |
| `src/Animora.Domain/Clinical/PrescriptionLine.cs` | create | prescription line value object |
| `src/Animora.Domain/Clinical/VisitServiceLine.cs` | create | invoice-handoff line value object |
| `src/Animora.Domain/Clinical/VaccinationRecord.cs` | create | vaccination aggregate + derived state |
| `src/Animora.Domain/Clinical/VaccineSchedule.cs` | create | code → dose interval table |
| `src/Animora.Domain/Clinical/MedicalHistoryEntry.cs` | create | append-only history entry |
| `src/Animora.Domain/Events/ClinicalEvents.cs` | create | the 4 clinical domain events |
| `src/Animora.Domain/Results/ErrorCodes.cs` | modify | append the 7 clinical codes |
| `tests/Animora.Domain.Tests/VisitStateMachineTests.cs` | create | every legal transition + every illegal one rejected |
| `tests/Animora.Domain.Tests/VisitInvariantTests.cs` | create | diagnosis required; lines locked after invoicing; invoice attach guard |
| `tests/Animora.Domain.Tests/VaccinationDueDateTests.cs` | create | per-dose intervals, booster, unknown code default, derived state boundaries |
| `tests/Animora.Domain.Tests/PatientInvariantTests.cs` | create | species immutability, weight range, document target requirement, phone normalization |

## Implementation steps
1. Create `Patients/` with `Owner` (+ `OwnerGender`, `NormalizePhone`) and `Pet` (+ its two enums,
   immutable `Species`, `AgeMonthsAt`).
2. Add `WeightEntry` (range-guarded factory returning `Result<WeightEntry>`) and `DocumentReference`
   (target-required guard).
3. Create `Clinical/` with the three value objects (`PetVitals`, `PrescriptionLine`,
   `VisitServiceLine`).
4. Implement `Visit`: private collections exposed as `IReadOnlyList<T>`, a single private
   `Result Transition(VisitState target)` helper encoding the transition table, and the public methods
   delegating to it. Raise the two events.
5. Add `VaccineSchedule`, then `VaccinationRecord` with the due-date factory and the derived
   `StateAt(nowUtc)` method (30-day `DueSoon` window as a named constant).
6. Add `MedicalHistoryEntry` — constructor-only, zero mutators.
7. Add `Events/ClinicalEvents.cs` with the four records.
8. Append the 7 error codes.
9. Write the four test classes. `VisitStateMachineTests` enumerates all `VisitState` pairs and asserts
   exactly the tabled transitions succeed. `VaccinationDueDateTests` covers DHPP dose 1→+21d,
   dose 3→+365d, an unknown code→+365d, and the `DueSoon`/`Overdue` boundaries at ±1 second.
10. `build.sh full` = 0/0; `test.sh` = `TESTS OK`; `run.sh` = `SMOKE RUN OK`.

## Contracts / schema added
- Entities: `Owner`, `Pet`, `WeightEntry`, `DocumentReference`, `Visit`, `VaccinationRecord`,
  `MedicalHistoryEntry`.
- Value objects: `PetVitals`, `PrescriptionLine`, `VisitServiceLine`.
- Enums: `OwnerGender`, `PetSpecies`, `PetGender`, `PetStatus`, `DocumentKind`, `VisitState`,
  `VisitReason`, `VisitLineSource`, `VaccinationState`, `MedicalHistoryKind`.
- Statics: `VaccineSchedule`. Events: `VisitCompletedEvent`, `VisitReadyForInvoiceEvent`,
  `VaccinationAdministeredEvent`, `PetRegisteredEvent`.
- `ErrorCodes` += 7. No migration (A09 maps these), no `Animora.Contracts` change.

## Rules that apply here
- **ARCHITECTURE.md §8.5** — medical history is preserved in full and never overwritten:
  `MedicalHistoryEntry` has no mutator; corrections are new rows.
- **A-RULE 020** — `DocumentReference` stores a `FileId` + hash, never a filesystem path.
- **CONVENTIONS §6 item 9** — the `Visit` state machine decides; a version mismatch never decides.
- **CONVENTIONS §4** — `IClock` passed into `Complete`/`Cancel`; no `DateTimeOffset.UtcNow`.
- **CONVENTIONS §2** — `Animora.Domain` gains no reference; domain enums are duplicated, not imported
  from `Animora.Contracts`.
- **CONVENTIONS §4** — collections exposed as `IReadOnlyList<T>`; `Result` for expected failures.

## Out of scope
- Appointments and boarding → A05. Inventory/product links → A06. Invoice creation → A07.
- EF mappings + migration → A09. Domain-event dispatch to `MedicalHistoryEntry` → A10.
- `IPatientsQueryService`/`IVisitEncounterService`/`IVaccinationService` implementations → C01, C03.
- Lab results as a first-class aggregate → not in this roadmap; `DocumentKind.Lab` covers today's need.

## Acceptance criteria
- Build: `scripts/sandbox/build.sh full` = 0 warnings / 0 errors.
- `VisitStateMachineTests` covers all 36 `VisitState` pairs; only the 8 tabled transitions succeed.
- `VisitInvariantTests`: `Complete` without a diagnosis returns `VISIT_DIAGNOSIS_REQUIRED`;
  `AddServiceLine` after `Invoiced` returns `VISIT_LOCKED`.
- `VaccinationDueDateTests` green on all six seeded vaccine codes.
- `MedicalHistoryEntry` has no public setter and no method other than its constructor (assert by
  reflection in `PatientInvariantTests`).
- `Animora.Domain.csproj` still has zero references.
- Runnability (`CONVENTIONS.md` §11): `scripts/sandbox/run.sh` = `SMOKE RUN OK`.

## PROGRESS.md update
Mark A04 done; advance "Next phase" to `A05`; append a "Repo state" entry listing: the 12 new domain
files, the entity/enum/event names added, `ErrorCodes` += 7, the 4 test classes, no DI change, no mock
deleted, no migration created, deviations.
