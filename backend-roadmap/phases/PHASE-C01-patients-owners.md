# Phase C01 — Patients & Owners

**Band:** M · **Track:** C · **Depends on:** B05 · **Satisfies:** R-B01, R-B02
**Runnable after this phase:** desktop offline yes · server required no

## Read before starting
- `backend-roadmap/CONVENTIONS.md` §3, §4, §5, §8, §9, §10, §11, §12
- `backend-roadmap/PROGRESS.md`
- `PERFORMANCE.md` §4, §5, §6, §12
- `src/Animora.Contracts/Patients/IPatientsQueryService.cs`,
  `src/Animora.Contracts/Patients/PatientModels.cs`,
  `src/Animora.Contracts/Patients/PatientDetailModels.cs`
- `src/Animora.Domain/Patients/*` (`Owner`, `Pet`, `WeightEntry`, `DocumentReference`)
- `src/Animora.Application/Abstractions/*`, `src/Animora.Application/Ports/IOwnerRepository.cs`,
  `src/Animora.Application/Ports/IPetRepository.cs`
- `src/Animora.Desktop/App.axaml.cs` (lines registering `IPatientsQueryService`)

## Goal
Make the Patients & Owners screens run on the real local database and establish **the wiring pattern
every later Track C phase cites**: use cases → a `Client.Services` implementation of the existing
`Animora.Contracts` interface → mapping layer → one DI line → mock deleted.

## Design decisions (pre-made — implement, do not re-derive)
- **THE TRACK C PATTERN (referenced by C02–C10 as "the C01 pattern"):**
  1. Use cases live in `src/Animora.Application/UseCases/<Context>/`, one file per command/query +
     its handler + its validator. Commands that change business facts are `IJournaledCommand` and
     carry `[RequiresPermission]` + `[Auditable]`; pure reads are queries.
  2. The `Animora.Contracts` implementation lives in
     `src/Animora.Client.Services/<Context>/<Context>Service.cs`, is `sealed`, takes `IDispatcher` +
     `ICurrentUserProvider` + `IOperationIdFactory`, and contains **no EF, no SQL, no business rule** —
     it builds a context, dispatches, and maps.
  3. Contracts DTO ↔ domain/read-model mapping lives in
     `src/Animora.Client.Services/<Context>/<Context>Mapping.cs` as static extension methods. Mapping
     is hand-written (no AutoMapper — not in `STACK.md` §11).
  4. A read that backs a list uses a **projection query handler** returning a purpose-built read model,
     never a loaded aggregate; paging uses `QueryPaging.ToPagedAsync`.
  5. Persian text never appears in Application/Client.Services; a failed `Result` returns an
     `ErrorCode`, and the ViewModel/`NotificationText` maps it.
  6. Seed data for the context goes into `LocalSeeder` as **reference data only**; realistic first-run
     sample business rows go into a per-context `Seed/<Context>SampleData.cs`, executed **only when
     `LocalStoreOptions.SeedSampleData == true`** (default `true` for a fresh install, so the user can
     immediately test screens; a checkbox in first-run setup sets it).
  7. The DI swap in `App.axaml.cs` is one line per interface; the mock file is deleted in the same
     commit; no `.axaml` changes; each ViewModel change is listed explicitly in the phase file.
- **Use cases** (`UseCases/Patients/`):
  - Queries: `PetDirectoryQuery(string? Search, PetSpecies? Species, PetStatus? Status, int Page,
    int PageSize)` → `PagedResult<PetSummaryReadModel>`;
    `OwnerDirectoryQuery(...)` → `PagedResult<OwnerSummaryReadModel>`;
    `OwnerDetailQuery(string OwnerId)` → `OwnerDetailReadModel?`;
    `PetDetailQuery(string PetId)` → `PetDetailReadModel?`.
  - Commands: `CreateOwnerCommand`, `UpdateOwnerCommand`, `CreatePetCommand`, `UpdatePetInfoCommand`,
    `AddWeightEntryCommand`. All are `IJournaledCommand`
    (`CommandTypes.PatientsOwnerCreate` = `"patients.owner.create"`, etc. — 5 new entries in
    `CommandTypes`), `[RequiresPermission("patients.owner.edit" | "patients.pet.edit")]`,
    `[Auditable(AuditCategory.Clinical, "patients.<verb>")]`.
- **Read models** (`UseCases/Patients/ReadModels/`) are shaped to the existing Contracts DTOs so the
  mapping is 1-to-1: `PetSummaryReadModel` carries exactly the fields `PetSummary` needs (including
  the owner's display name and the last-visit date, joined in the projection — **no N+1**),
  `OwnerSummaryReadModel` carries the pet count and total spend (`Invoice` sums are **not** joined
  here; `TotalSpend` is read from a `OwnerFinancialSnapshot` projection added in C09 — until then the
  mapping supplies `0` and the phase notes it in `PROGRESS.md`).
- **Search indexing:** `Owner` and `Pet` gain a computed, persisted `SearchKey` column
  (lowercased, Persian-digit-normalized concatenation of name + phone + microchip), written in
  `SaveChangesAsync` by a small `ISearchKeyProvider` per entity type.
  Indexes `IX_Owners_TenantId_SearchKey`, `IX_Pets_TenantId_SearchKey`. Migration
  `008_PatientsSearchKeys`. Directory queries use `EF.Functions.Like(SearchKey, $"%{term}%")` —
  never a client-side `Contains` over a materialized list.
  Persian-digit normalization lives in `Animora.Application/Text/PersianText.cs`
  (`NormalizeDigits`, `NormalizeArabicLetters`) — digits/letters only, **no** localization strings.
- **`PatientsService`** (`Client.Services/Patients/`) implements all 9 `IPatientsQueryService` members.
  Interface members that return an unpaged `IReadOnlyList<...>` (`GetPetsAsync`, `GetOwnersAsync`)
  request page 1 with the max page size and are documented as such; **no Contracts member is
  reshaped** (CONVENTIONS §10 item 2).
- **Sample data:** `Seed/PatientsSampleData.cs` — 12 owners, 18 pets with varied species/status,
  weight histories and 2 documents each, all with deterministic ULIDs derived from a fixed seed so
  test assertions are stable.
- **ViewModel changes (exhaustive):** none required. `PatientsPageViewModel`, `OwnerSheetViewModel`
  and `PetFileViewModel` already `await` the interface. If a lifecycle issue appears (a load started
  before `LocalStoreStartup` completes), the fix is a single `await
  IStoreReadinessGate.WaitAsync(ct)` call at the top of each VM's load method —
  `IStoreReadinessGate` is added to `Animora.Contracts/Diagnostics/` in this phase and is the
  standard remedy every later C phase reuses.
- **New error codes:** `OWNER_NOT_FOUND`, `PET_NOT_FOUND`, `OWNER_HAS_ACTIVE_PETS`,
  `WEIGHT_ENTRY_INVALID`.

## Files to create / modify
| Path | Action | Contents (one line) |
|---|---|---|
| `src/Animora.Application/Text/PersianText.cs` | create | digit/letter normalization for search keys |
| `src/Animora.Application/UseCases/Patients/*.cs` | create | 4 queries + 5 commands + handlers + validators |
| `src/Animora.Application/UseCases/Patients/ReadModels/*.cs` | create | 4 read models |
| `src/Animora.Persistence.Sqlite/Queries/PatientDirectoryQueries.cs` | create | projection SQL for both directories |
| `src/Animora.Persistence.Sqlite/Search/ISearchKeyProvider.cs` | create | search-key computation seam |
| `src/Animora.Persistence.Sqlite/Configurations/Patients/*.cs` | modify | add `SearchKey` + 2 indexes |
| `src/Animora.Persistence.Sqlite/Migrations/*` | create | `008_PatientsSearchKeys` + snapshot |
| `src/Animora.Persistence.Sqlite/Seed/PatientsSampleData.cs` | create | deterministic first-run sample rows |
| `src/Animora.Persistence.Sqlite/Options/LocalStoreOptions.cs` | modify | add `SeedSampleData` |
| `src/Animora.Contracts/Diagnostics/IStoreReadinessGate.cs` | create | Contracts **addition** (startup gate) |
| `src/Animora.Client.Services/Patients/PatientsService.cs` | create | real `IPatientsQueryService` |
| `src/Animora.Client.Services/Patients/PatientsMapping.cs` | create | read model ↔ Contracts DTO mapping |
| `src/Animora.Client.Services/Diagnostics/StoreReadinessGate.cs` | create | gate implementation over `LocalStoreStartup` |
| `src/Animora.Contracts.Wire/Commands/CommandTypes.cs` | modify | add 5 patients command types |
| `src/Animora.Desktop/App.axaml.cs` | modify | swap `IPatientsQueryService` → `PatientsService`; register the readiness gate |
| `src/Animora.Desktop/Mocks/MockPatientsQueryService.cs` | delete | replaced |
| `src/Animora.Domain/Results/ErrorCodes.cs` | modify | append 4 codes |
| `tests/Animora.Application.Tests/PatientsUseCaseTests.cs` | create | create/update/weight rules + permissions |
| `tests/Animora.Persistence.Tests/PatientDirectoryQueryTests.cs` | create | paging, search-key matching, no N+1 |
| `tests/Animora.Application.Tests/PatientsMappingTests.cs` | create | every Contracts DTO field is populated |

## Implementation steps
1. Add `PersianText`, `ISearchKeyProvider` and the `SearchKey` columns + indexes; generate
   `008_PatientsSearchKeys`. Backfill existing rows inside the migration.
2. Write the four queries with projection handlers in `PatientDirectoryQueries` (single SQL statement
   each; owner name and last-visit date joined, not looked up per row).
3. Write the five commands with validators, permission and audit attributes, and the new
   `CommandTypes` entries.
4. Add `IStoreReadinessGate` to Contracts and `StoreReadinessGate` to Client.Services.
5. Write `PatientsMapping` (read model → `PetSummary`/`OwnerSummary`/`PetDetail`/`OwnerDetail`) and
   `PatientsService` implementing all 9 interface members.
6. Add `PatientsSampleData` and call it from `LocalSeeder` behind `SeedSampleData`.
7. Swap the DI line, register the readiness gate, delete `MockPatientsQueryService.cs`.
8. Append the 4 error codes; write the three test classes.
9. Verify manually: the patients list loads from the DB, search by Persian name and by phone with
   Persian digits both match, creating an owner+pet persists across restart, adding a weight entry
   appears in the pet file chart.
10. `build.sh full` = 0/0; `test.sh` = `TESTS OK`; `run.sh` = `SMOKE RUN OK`.

## Contracts / schema added
- `Animora.Contracts` **addition**: `IStoreReadinessGate`.
- Application: `PersianText`, 4 patients queries + 4 read models, 5 patients commands.
- Persistence: `PatientDirectoryQueries`, `ISearchKeyProvider`, `SearchKey` columns + 2 indexes,
  migration `008_PatientsSearchKeys`, `PatientsSampleData`, `LocalStoreOptions.SeedSampleData`.
- Client services: `PatientsService`, `PatientsMapping`, `StoreReadinessGate`.
- Wire: 5 `CommandTypes` entries. `ErrorCodes` += 4.
- DI: `IPatientsQueryService` → `PatientsService`; `IStoreReadinessGate` registered.
  **Mock deleted: `MockPatientsQueryService.cs`.**

## Rules that apply here
- **CONVENTIONS §10** — the protocol is followed exactly; no context keeps both implementations.
- **PERFORMANCE.md §6 / CONVENTIONS §8** — directory reads are paged and projected; a single SQL
  statement per list; no N+1 for owner names or last-visit dates.
- **PERFORMANCE.md §4** — every service member is async; the readiness gate awaits rather than blocks.
- **CONVENTIONS §5** — the search filter runs in SQL via `Like` over an indexed column, never
  client-side.
- **CONVENTIONS §4** — no Persian string in Application/Client.Services; errors are codes.
- **A-RULE 005 / R-B14** — every mutation is a journaled command with an `OperationId`, so it is
  already settlement-ready before the server exists.

## Out of scope
- Owner financial totals from real invoices → C09 (`OwnerFinancialSnapshot`).
- Documents upload/storage beyond the metadata row → D12 (server) / `LocalFileStore` is used as-is.
- Global search across contexts → C10. Visit/vaccination collections inside the pet file are fed by
  C03's real data; until then they render the rows that exist (empty on a fresh DB).
- Any other mock → its own phase.

## Acceptance criteria
- Build: `scripts/sandbox/build.sh full` = 0 warnings / 0 errors.
- `src/Animora.Desktop/Mocks/MockPatientsQueryService.cs` does not exist; no DI line references it.
- `PatientsUseCaseTests` green: creating an owner without a phone fails validation; creating a pet for
  an unknown owner returns `OWNER_NOT_FOUND`; a weight entry with a non-positive value returns
  `WEIGHT_ENTRY_INVALID`; a user lacking `patients.pet.edit` is denied.
- `PatientDirectoryQueryTests` green: page size is clamped to 200; a search for a Persian name with
  Arabic-form letters and Persian digits matches; each query's `ToQueryString()` contains exactly one
  `SELECT`.
- `PatientsMappingTests` green: every property of `PetSummary`, `OwnerSummary`, `PetDetail` and
  `OwnerDetail` is populated from the read model (a reflection walk asserts no default value where the
  read model has data).
- Manual: patients list, owner sheet, pet file, add-owner and add-pet flows all work against the local
  DB and survive an app restart.
- Runnability (`CONVENTIONS.md` §11): `scripts/sandbox/run.sh` = `SMOKE RUN OK`.

## PROGRESS.md update
Mark C01 done; advance "Next phase" to `C02`; append a "Repo state" entry listing: **the C01 pattern**
(so later phases cite it), the 9 use cases + 4 read models, `PatientDirectoryQueries`, the `SearchKey`
columns + migration `008_PatientsSearchKeys`, `PatientsSampleData` + `SeedSampleData`, the Contracts
addition `IStoreReadinessGate`, `PatientsService`/`PatientsMapping`, the DI swap,
**mock deleted: `MockPatientsQueryService.cs`**, `CommandTypes` += 5, `ErrorCodes` += 4, the 3 test
classes, the deferred `TotalSpend` (owned by C09), deviations.
