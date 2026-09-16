# Phase 09 — Patients & Owners — Pet File / Owner Sheet

**Band:** M · **Depends on:** 08 · **Satisfies:** R-4, R-5, R-18

## Read before starting
- `CONVENTIONS.md` §2, §3, §4a (Fidelity Protocol — MANDATORY), §5, §6 (Definition of Done incl. fidelity gate + visual verification), §7a
- **Reference CSS (MANDATORY — the source of every measurable value, per §4a rule 3 / §8): `styles.css` + `patients.css` (owner sheet, pet-file tabs, weight/vaccination/document sub-panels, add-pet/owner wizard).** Read the sections these file(s) define for every element this screen renders and transcribe the exact paddings, gaps, radii, borders, font sizes/weights, grid/column templates, fixed/min/max sizes, fills and shadows — reusing Phase 02 tokens where a value equals a token. Do not eyeball or approximate.
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

## Fidelity (MANDATORY — read `CONVENTIONS.md` §4a in full)
- **Rebuild every element the reference screen renders — nothing dropped, simplified, merged, summarized, or deferred.** Every card, panel, header, stat/KPI box, chart/donut/gauge/ring/sparkline, badge/pill, tab, sub-nav, table (with the SAME columns in the SAME order), list row, avatar, icon, button, chip, empty-state and caption must exist, in the same place, order, grouping and count as the reference.
- **No visible reference element may be declared "out of scope," "nice-to-have," or "optional" here.** The only permitted simplification is *motion*: an animated value may render at its final/current value instead of animating — the element itself still exists and shows the correct value (§4a rule 6).
- **Exact measurable values** come from the Reference CSS above (§4a rule 3), not from judgement. Proportional CSS grids map to star-sized `Grid` columns/rows; fixed px sizes map to fixed sizes — transcribed, never re-guessed.
- **Exact Persian text** for every label/title/subtitle/column header/button/empty-state/tab, character-for-character; all numbers and dates through the Phase 04 Persian digit / grouping / Jalali helpers (§4a rule 4). State colors/pills map 1:1 to Phase 02 semantic tokens (§4a rule 5).
- **Backend-ready:** the ViewModel binds only to the `Animora.Contracts` interface(s); the mock is the sole swap point for a future real backend — no sample data, mock call, or design-time branch leaks into a View or the ViewModel's public shape (R-18).

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
- Real visit/vaccination *data* — these tabs read whatever the Visits/
  Vaccination phases' mocks eventually provide; for now their rows are empty.
  **The tabs, their headers, and the reference's exact empty-state markup MUST
  still be fully built per §4a — "out of scope" here means the data is absent,
  NOT that the tab/empty-state UI may be skipped or simplified.**

## Acceptance criteria
- App builds/runs; from the Patients list, clicking a pet opens the Pet
  File with working tabs and a working weight-add mini-form; clicking an
  owner opens the Owner Sheet with editable fields that Save correctly;
  the add-patient wizard opens, completes, and the new pet/owner appears
  back in Phase 08's list.
- **Visual verification (§6.7 + §7a) performed** and recorded in `PROGRESS.md`: the rebuilt screen was compared element-by-element against the reference and matches (same boxes/order/sizes/colors/Persian text); every measurable value traces to the Reference CSS; no reference element is missing. Building and running alone does NOT satisfy this phase.

## PROGRESS.md update
Mark Phase 09 done; advance "Next phase" to 10; update "Repo state."
