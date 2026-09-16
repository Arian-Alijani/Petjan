# Phase 09 — Patients & Owners — Pet File / Owner Sheet

**Band:** M · **Depends on:** 08 · **Satisfies:** R-4, R-5, R-18

## Read before starting
- `CONVENTIONS.md` §2, §3, §5
- `Animora-UI/assets/patients-page.js` — **only** lines ~311–end (owner
  sheet rendering/edit-save, pet weight helpers, pet-file tabs: info/
  visits/weight/vaccinations/documents, and the add-pet/add-owner wizard
  functions `setWizMode`/`renderWizOwnerList`/`paintWiz` onward). Do not
  re-read lines 1–310 (already covered by Phase 08).

## Goal
Add the drill-in detail views: the Owner Sheet (owner info + edit + their
pets) and the Pet File (tabbed: info/visits/weight/vaccinations/documents),
plus the add-pet/add-owner wizard, replacing Phase 08's placeholder toasts
with real navigation.

## Contracts to add/extend (`Animora.Contracts/Patients/`)
- Extend `IPatientsQueryService`: `Task<OwnerDetail> GetOwnerAsync(id)`,
  `Task<PetDetail> GetPetAsync(id)`, `Task SaveOwnerAsync(OwnerDetail)`,
  `Task SavePetInfoAsync(PetDetail)`, `Task AddWeightEntryAsync(petId, WeightEntry)`,
  `Task<PetDetail> CreatePetAsync(NewPetRequest)`,
  `Task<OwnerDetail> CreateOwnerAsync(NewOwnerRequest)`.
- `OwnerDetail` DTO (full profile + list of `PetSummary`), `PetDetail` DTO
  (full profile + `WeightEntry[]` + visit/vaccination/document summaries —
  reuse/reference other contexts' summary DTOs where reasonable, e.g. a
  minimal `VisitSummaryRef`/`VaccinationSummaryRef` rather than duplicating
  full Visit/Vaccination contracts prematurely).
- `WeightEntry`, `NewPetRequest`, `NewOwnerRequest` DTOs.

## Mock to add
- Extend `MockPatientsQueryService` with the detail/save/create methods,
  operating on the same in-memory list seeded in Phase 08.

## View/ViewModel tasks
1. `Pages/Patients/OwnerSheetViewModel.cs` + `.axaml`: owner info block
   (editable fields + Save), and that owner's pets list (cards, clickable
   into the Pet File).
2. `Pages/Patients/PetFileViewModel.cs` + `.axaml`: tab strip (`Info`,
   `Visits`, `Weight`, `Vaccinations`, `Documents` — mirrors
   `petInfoTab/petVisitsTab/petWeightTab/petVaxTab/petDocsTab`), each tab
   a small sub-view/`DataTemplate`:
   - Info tab: editable profile fields + Save (`savePetInfo`).
   - Weight tab: a simple line/points chart or table of `WeightEntry`
     (mirrors `weightSeries`/`weightRows`) + an "add weight" mini-form
     (mirrors `addWeight`, including Jalali-date input via Phase 04's
     `JalaliDate`).
   - Visits/Vaccinations/Documents tabs: read-only summary lists (can be
     empty-state via Phase 03 `EmptyState` if mock data has none yet —
     acceptable since Visits/Vaccination screens are built in later
     phases and this phase must not depend on them).
3. `Pages/Patients/AddPatientWizardViewModel.cs` + `.axaml`: the add-pet/
   add-owner flow (mirrors `setWizMode`/`renderWizOwnerList`/`paintWiz`) —
   pick existing owner or create new, then fill pet fields; opened as a
   modal via Phase 03 `IDialogService` from the Patients list's "add"
   button (Phase 08 may have stubbed this button to a toast — replace it
   here).
4. Wire Phase 08's `OpenPet`/`OpenOwner` commands to real navigation
   (`INavigationService.NavigateTo<PetFileViewModel>()` /
   `OwnerSheetViewModel`, passing the selected id) instead of the toast
   placeholder.

## Out of scope
- Real visit/vaccination data — these tabs read whatever the Visits/
  Vaccination phases' mocks eventually provide; for now, empty-state is
  correct and expected.

## Acceptance criteria
- App builds/runs; from the Patients list, clicking a pet opens the Pet
  File with working tabs and a working weight-add mini-form; clicking an
  owner opens the Owner Sheet with editable fields that Save correctly;
  the add-patient wizard opens, completes, and the new pet/owner appears
  back in Phase 08's list.

## PROGRESS.md update
Mark Phase 09 done; advance "Next phase" to 10; update "Repo state."
