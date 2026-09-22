# Phase C03 — Visit encounter & Vaccination

**Band:** M · **Track:** C · **Depends on:** C02 · **Satisfies:** R-B01, R-B02
**Runnable after this phase:** desktop offline yes · server required no

## Read before starting
- `backend-roadmap/CONVENTIONS.md` §3, §4, §5, §8, §9, §10, §11, §12
- `backend-roadmap/PROGRESS.md` (the **C01 pattern** + C02's `TicketRef` entry)
- `PERFORMANCE.md` §4, §5, §12
- `src/Animora.Contracts/Visits/IVisitEncounterService.cs`, `src/Animora.Contracts/Visits/VisitsModels.cs`
- `src/Animora.Contracts/Vaccination/IVaccinationService.cs`,
  `src/Animora.Contracts/Vaccination/VaccinationModels.cs`,
  `src/Animora.Contracts/Vaccination/VaccinationHistoryModels.cs`
- `src/Animora.Domain/Clinical/Visit.cs`, `src/Animora.Domain/Clinical/VaccinationRecord.cs`,
  `src/Animora.Domain/Clinical/MedicalHistoryEntry.cs`, `src/Animora.Domain/Clinical/VaccineSchedule.cs`
- `src/Animora.Application/Ports/IVisitRepository.cs`, `src/Animora.Application/Ports/IVaccinationRepository.cs`
- `src/Animora.Desktop/App.axaml.cs` (lines registering `IVisitEncounterService`, `IVaccinationService`,
  `VisitEncounterStore`)

## Goal
The encounter screen edits a real `Visit` aggregate (vitals, notes, diagnosis, plan, prescription
lines) with autosave-safe semantics, completing it produces an append-only `MedicalHistoryEntry` plus
a **ready-for-invoice handoff record** that C07 consumes; the vaccination screen runs on real
`VaccinationRecord` rows with computed due dates. `VisitEncounterStore` is deleted.

## Design decisions (pre-made — implement, do not re-derive)
- **Encounter edit model:** each `Save*Async` member is its own command, not a whole-draft save, so a
  partially filled encounter never overwrites a concurrent field. Commands (`UseCases/Visits/`):
  `SaveVisitVitalsCommand`, `SaveVisitNoteCommand(VisitNoteField Field, string Text)` — one command
  covering `History`/`Exam`/`Diagnosis`/`Plan` via the enum, `AddPrescriptionLineCommand`,
  `RemovePrescriptionLineCommand`, `CompleteVisitCommand`.
  `enum VisitNoteField { History, Exam, Diagnosis, Plan }` in `Animora.Domain/Clinical/`.
  `CommandTypes` += `visits.visit.vitals`, `visits.visit.note`, `visits.rx.add`, `visits.rx.remove`,
  `visits.visit.complete` (5). Permission `visits.encounter.edit`; `CompleteVisitCommand` also
  requires `visits.encounter.complete`.
- **Debounce is a client concern, not a command concern:** `VisitEncounterService` coalesces rapid
  note edits with a 600 ms trailing debounce per `(encounterId, field)` held in a bounded
  `DebounceBuffer` (`Animora.Client.Services/Visits/DebounceBuffer.cs`, max 32 entries, flushed on
  `CompleteAsync` and on dispose). The command itself is never debounced server-side.
- **Rx catalog:** `IVisitEncounterService.GetCatalogAsync()` is served by
  `RxCatalogQuery()` → products where `ProductKind` is `Medicine` or `Consumable`, plus service
  definitions, projected to `RxCatalogItem` with `RxCatalogKind` mapped from `ProductKind`. Results
  are cached for the session in a bounded `TenantScopedCache<string, IReadOnlyList<RxCatalogItem>>`
  (`Animora.Client.Services/Caching/TenantScopedCache.cs`, capacity 16, key =
  `tenantId + '|' + userId + "|rxcatalog|v1"` per A-RULE 033) and invalidated by the
  `ProductChangedEvent` outbox handler.
- **Completion → handoff (the C07 seam):** `CompleteVisitCommand` in one transaction:
  1. validates the visit is `InProgress` and has at least a diagnosis **or** one service/Rx line
     (else `VISIT_INCOMPLETE`);
  2. transitions `Visit` to `VisitState.Completed` and stamps `CompletedUtc`;
  3. appends one `MedicalHistoryEntry` (`MedicalHistoryKind.Visit`) — append-only, enforced by
     `AppendOnlyGuard`;
  4. writes a `VisitBillingHandoff` row — **new `[LocalOnly]` entity** in
     `Animora.Persistence.Sqlite/Entities/`: `Id`, `TenantId`, `VisitId` (unique), `OwnerId`, `PetId`,
     `DoctorId`, `CompletedUtc`, `LinesJson` (the billable `VisitServiceLine` + `PrescriptionLine`
     snapshot), `Status` (`HandoffStatus { Ready, Invoiced, Cancelled }`), `InvoiceId?`.
     Index `IX_VisitBillingHandoffs_TenantId_Status_CompletedUtc`, unique
     `IX_VisitBillingHandoffs_VisitId`. Migration `009_VisitBillingHandoff`;
  5. moves the reception ticket to `QueueState.Done`;
  6. raises `VisitCompletedEvent` (A04) and `VisitReadyForInvoiceEvent`.
  `ISalesQueryService.GetReadyEncountersAsync()` (C07) reads exactly this table.
- **Vaccination due computation:** `VaccineSchedule` (A04) is the only authority.
  `VaccinationDueQuery(DateOnly LocalDay)` projects each `VaccinationRecord` with a computed
  `DaysUntilDue = (DueUtc - nowUtc).Days` and maps to `VaccinationStatus`:
  `Overdue` (< 0), `DueSoon` (0–14), `Upcoming` (15–60), `Scheduled` (> 60), `Done`
  (`VaccinationState.Administered`). Thresholds are `public const int` on
  `Animora.Domain/Clinical/VaccinationThresholds.cs` so tests and UI cannot drift.
- **Vaccination commands:** `MarkVaccinationDoneCommand` (administers the dose, appends a
  `MedicalHistoryEntry` of kind `Vaccination`, schedules the next record from `VaccineSchedule` when
  the protocol defines one) and `RecordVaccinationReminderCommand` (records that a reminder text was
  produced — it does **not** send anything). `CommandTypes` += `vaccination.record.administer`,
  `vaccination.record.reminder` (2). Permission `vaccination.record.edit`.
- **SMS is text generation only (A-RULE 016):** `BuildSmsPreviewAsync` renders a stored
  `SmsTemplate` (seeded reference data in `LocalSeeder`, 4 templates) against the record via
  `SmsTemplateRenderer` (`Animora.Client.Services/Vaccination/SmsTemplateRenderer.cs`, token syntax
  `{pet}`, `{owner}`, `{vaccine}`, `{due}`; unknown token → `SMS_TEMPLATE_TOKEN_UNKNOWN`).
  `SendSmsAsync` **does not contact any network**: it executes
  `RecordVaccinationReminderCommand`, marks `SmsState.Queued` and returns. A phase file elsewhere
  never promises delivery; the UI label already reflects "queued".
- **History:** `VaccinationHistoryQuery(VaccinationHistoryFilter)` → `VaccinationHistoryPage`, paged
  (max 200), sorted per `VaccinationHistorySort`, over `MedicalHistoryEntry` rows of kind
  `Vaccination` joined to the pet. Single statement.
- **Sample data:** `Seed/ClinicalSampleData.cs` — 10 completed visits across the C01 pets with
  realistic vitals/notes, and 14 vaccination records spread across all five status buckets.
- **ViewModel changes (exhaustive):** `VaccinationPageViewModel` gains the readiness-gate line.
  `VisitEncounterViewModel` gains the readiness-gate line **and** one `await
  _service.FlushAsync(ct)` call in its existing close/navigate-away path — `FlushAsync` is added to
  the Contracts interface `IVisitEncounterService` as a **new member** (additive, no existing member
  reshaped); the mock is being deleted in this phase so no other implementor exists.
- **New error codes:** `VISIT_NOT_FOUND`, `VISIT_NOT_EDITABLE`, `VISIT_INCOMPLETE`,
  `RX_LINE_NOT_FOUND`, `VACCINATION_NOT_FOUND`, `VACCINATION_ALREADY_DONE`,
  `SMS_TEMPLATE_TOKEN_UNKNOWN`.

## Files to create / modify
| Path | Action | Contents (one line) |
|---|---|---|
| `src/Animora.Domain/Clinical/VisitNoteField.cs` | create | note-field enum |
| `src/Animora.Domain/Clinical/VaccinationThresholds.cs` | create | due-bucket constants |
| `src/Animora.Application/UseCases/Visits/*.cs` | create | 6 encounter commands + `RxCatalogQuery` + `EncounterDraftQuery` |
| `src/Animora.Application/UseCases/Vaccination/*.cs` | create | 3 queries + 2 commands + handlers/validators |
| `src/Animora.Persistence.Sqlite/Entities/VisitBillingHandoff.cs` | create | `[LocalOnly]` handoff row |
| `src/Animora.Persistence.Sqlite/Configurations/Clinical/VisitBillingHandoffConfiguration.cs` | create | mapping + 2 indexes |
| `src/Animora.Persistence.Sqlite/Migrations/*` | create | `009_VisitBillingHandoff` + snapshot |
| `src/Animora.Persistence.Sqlite/Queries/VaccinationQueries.cs` | create | due + history projections |
| `src/Animora.Persistence.Sqlite/Queries/RxCatalogQueries.cs` | create | catalog projection |
| `src/Animora.Persistence.Sqlite/Seed/ClinicalSampleData.cs` | create | visits + vaccination rows |
| `src/Animora.Persistence.Sqlite/Seed/LocalSeeder.cs` | modify | seed 4 `SmsTemplate` rows (reference data) |
| `src/Animora.Client.Services/Caching/TenantScopedCache.cs` | create | bounded tenant-safe cache |
| `src/Animora.Client.Services/Visits/VisitEncounterService.cs` | create | real `IVisitEncounterService` |
| `src/Animora.Client.Services/Visits/DebounceBuffer.cs` | create | trailing debounce for note edits |
| `src/Animora.Client.Services/Vaccination/VaccinationService.cs` | create | real `IVaccinationService` |
| `src/Animora.Client.Services/Vaccination/SmsTemplateRenderer.cs` | create | token rendering |
| `src/Animora.Contracts/Visits/IVisitEncounterService.cs` | modify | add `FlushAsync` (additive) |
| `src/Animora.Contracts.Wire/Commands/CommandTypes.cs` | modify | add 7 command types |
| `src/Animora.Desktop/App.axaml.cs` | modify | swap 2 DI lines; drop `VisitEncounterStore` registration |
| `src/Animora.Desktop/Pages/Visits/VisitEncounterViewModel.cs` | modify | readiness gate + `FlushAsync` on close |
| `src/Animora.Desktop/Pages/Vaccination/VaccinationPageViewModel.cs` | modify | readiness-gate line |
| `src/Animora.Desktop/Mocks/MockVisitEncounterService.cs` | delete | replaced |
| `src/Animora.Desktop/Mocks/VisitEncounterStore.cs` | delete | replaced by the DB |
| `src/Animora.Desktop/Mocks/MockVaccinationService.cs` | delete | replaced |
| `src/Animora.Domain/Results/ErrorCodes.cs` | modify | append 7 codes |
| `tests/Animora.Application.Tests/VisitEncounterUseCaseTests.cs` | create | field commands, completion rules, handoff row |
| `tests/Animora.Application.Tests/VaccinationUseCaseTests.cs` | create | due buckets, administer + next-dose scheduling |
| `tests/Animora.Persistence.Tests/VisitBillingHandoffTests.cs` | create | migration, unique index, append-only history |

## Implementation steps
1. Add `VisitNoteField`, `VaccinationThresholds`, the 7 error codes.
2. Add the `VisitBillingHandoff` entity + configuration + migration `009_VisitBillingHandoff`.
3. Write the 6 encounter commands and `EncounterDraftQuery`/`RxCatalogQuery` with validators,
   permissions and audit attributes; add the `CommandTypes` entries.
4. Implement `CompleteVisitCommand` exactly as the 6-step transaction above; assert the queue
   transition reuses C02's `TicketRef`.
5. Add `TenantScopedCache`, `DebounceBuffer`, `VisitEncounterService` (including `FlushAsync`), and
   the additive Contracts member.
6. Write `VaccinationQueries`, the 3 vaccination queries, the 2 vaccination commands and
   `SmsTemplateRenderer`; seed the 4 SMS templates as reference data.
7. Add `ClinicalSampleData`; wire it into `LocalSeeder` behind `SeedSampleData`.
8. Swap the two DI lines, remove the `VisitEncounterStore` registration, add the ViewModel lines,
   delete the three mock files.
9. Write the three test classes.
10. Verify manually: editing vitals/notes/Rx persists (including after a forced kill mid-edit, thanks
    to `FlushAsync` + per-field commands); completing a visit moves the ticket to done, adds a
    history entry and creates exactly one handoff row; vaccination buckets render correctly and
    "send SMS" queues without any network call.
11. `build.sh full` = 0/0; `test.sh` = `TESTS OK`; `run.sh` = `SMOKE RUN OK`.

## Contracts / schema added
- `Animora.Contracts` **addition**: `IVisitEncounterService.FlushAsync`.
- Domain: `VisitNoteField`, `VaccinationThresholds`.
- Application: 8 visit use cases, 5 vaccination use cases, their read models.
- Persistence: `VisitBillingHandoff` (+ `HandoffStatus`), migration `009_VisitBillingHandoff`,
  `VaccinationQueries`, `RxCatalogQueries`, `ClinicalSampleData`, 4 seeded `SmsTemplate` rows.
- Client services: `VisitEncounterService`, `DebounceBuffer`, `VaccinationService`,
  `SmsTemplateRenderer`, `TenantScopedCache`.
- Wire: 7 `CommandTypes` entries. `ErrorCodes` += 7.
- DI: `IVisitEncounterService` → `VisitEncounterService`; `IVaccinationService` →
  `VaccinationService`; `VisitEncounterStore` registration removed.
  **Mocks deleted: `MockVisitEncounterService.cs`, `VisitEncounterStore.cs`,
  `MockVaccinationService.cs`.**

## Rules that apply here
- **A-RULE 002 / R-B22** — `MedicalHistoryEntry` is append-only; `AppendOnlyGuard` already protects it.
- **A-RULE 016** — no SMS gateway, no network: the "send" path only queues text locally.
- **A-RULE 033 / CONVENTIONS §8** — the Rx catalog cache key includes tenant + user + resource +
  version and the cache is capacity-bounded.
- **PERFORMANCE.md §4** — the debounce never blocks; `FlushAsync` awaits.
- **CONVENTIONS §10** — three mock files deleted in the same commit as the swap.
- **CONVENTIONS §5** — the handoff table and vaccination queries are indexed and paged.

## Out of scope
- Turning a handoff into an invoice → C07 (reads `VisitBillingHandoff`).
- Pet-file display of these visits → already wired by C01's read models; no change here.
- Any real message delivery → permanently out of scope (A-RULE 016).
- Server-side visit commands → D06.

## Acceptance criteria
- Build: `scripts/sandbox/build.sh full` = 0 warnings / 0 errors.
- `MockVisitEncounterService.cs`, `VisitEncounterStore.cs` and `MockVaccinationService.cs` do not
  exist; no DI line references them.
- `VisitEncounterUseCaseTests` green: saving a note for a completed visit returns
  `VISIT_NOT_EDITABLE`; completing a visit with neither diagnosis nor lines returns
  `VISIT_INCOMPLETE`; a successful completion writes exactly one `MedicalHistoryEntry` and exactly
  one `VisitBillingHandoff`; completing twice is rejected, not duplicated.
- `VaccinationUseCaseTests` green: each status bucket boundary (−1, 0, 14, 15, 60, 61 days) maps to
  the documented `VaccinationStatus`; administering a dose with a follow-up protocol creates exactly
  one next record; administering twice returns `VACCINATION_ALREADY_DONE`.
- `VisitBillingHandoffTests` green: migration `009` applies to a DB created at `008`; the unique
  index rejects a second handoff for the same visit; updating a `MedicalHistoryEntry` throws from
  `AppendOnlyGuard`.
- Manual: encounter editing, completion, vaccination buckets and SMS preview all work offline.
- Runnability (`CONVENTIONS.md` §11): `scripts/sandbox/run.sh` = `SMOKE RUN OK`.

## PROGRESS.md update
Mark C03 done; advance "Next phase" to `C04`; append a "Repo state" entry listing:
`VisitBillingHandoff` + migration `009_VisitBillingHandoff` (**C07 consumes it**), the per-field
encounter command design, `TenantScopedCache` (reused by later phases), `VaccinationThresholds`,
`SmsTemplateRenderer` + the "no delivery" decision, the Contracts addition
`IVisitEncounterService.FlushAsync`, the 2 DI swaps + removed `VisitEncounterStore`,
**mocks deleted: `MockVisitEncounterService.cs`, `VisitEncounterStore.cs`,
`MockVaccinationService.cs`**, `CommandTypes` += 7, `ErrorCodes` += 7, the 3 test classes,
deviations.
