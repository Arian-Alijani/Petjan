# Phase 08 — Patients & Owners — List/Grid

**Band:** M · **Depends on:** 05 · **Satisfies:** R-4, R-5, R-18

## Read before starting
- `CONVENTIONS.md` §2, §3, §5
- `Animora-UI/patients.html` (full)
- `Animora-UI/assets/patients-data.js` — full file (487 lines): sample data
  shape reference only (owners/pets fields) — re-derive DTOs from this, do
  not copy the data itself.
- `Animora-UI/assets/patients-ui.js` — full file (178 lines): small shared
  UI helpers used by the list.
- `Animora-UI/assets/patients-page.js` — **only** lines 1–310 (functions
  `petMatches` through `render`/`bindContent`, i.e. the search/filter,
  species-chip filter, owner cards, pet cards, pets/owners tables, and the
  main `render()`/`bindContent()` dispatch). **Stop reading at line ~310**
  — the rest (owner sheet + pet file) is Phase 09's material.

## Goal
Replace the Patients placeholder with the list/grid screen: search bar,
species filter chips, toggle between "pets" and "owners" table/card views.
Opening a pet/owner (drill-in) is stubbed to Phase 09's ViewModel, added in
that phase.

## Contracts to add (`Animora.Contracts/Patients/`)
- `IPatientsQueryService` — `Task<IReadOnlyList<PetSummary>> GetPetsAsync()`,
  `Task<IReadOnlyList<OwnerSummary>> GetOwnersAsync()`.
- `PetSummary` DTO — mirror fields used by `petCard`/`petsTable` (name,
  species, breed, owner name, age/weight snapshot, last-visit date).
- `OwnerSummary` DTO — mirror fields used by `ownerCard`/`ownersTable`
  (name, phone, pet count, last-activity date).

## Mock to add
- `MockPatientsQueryService` — 8–12 sample pets across species (dog/cat/
  bird/rabbit — matching the reference's species icon set from
  `Icons.axaml`), 5–8 sample owners.

## View/ViewModel tasks
1. `Pages/Patients/PatientsPageViewModel.cs`: injects
   `IPatientsQueryService`; exposes `AllPets`, `AllOwners`,
   `FilteredPets`/`FilteredOwners` (recomputed from a `SearchText` +
   `SelectedSpecies` + `ViewMode` (`Pets|Owners`) — mirrors
   `petMatches`/`ownerMatches`/`sortPets`), `SpeciesChips` (derived counts,
   mirrors `renderSpeciesChips`), and a `ToggleViewMode` command.
2. `Pages/Patients/PatientsPageView.axaml(.cs)`: page header, search box,
   species chip row, a view-mode toggle (pets/owners), and the two lists —
   card layout for pets/owners (mirrors `petCard`/`ownerCard`) with a
   table-mode alternative if the reference clearly supports both
   (`petsTable`/`ownersTable`) — implement both and toggle via a Phase 03
   `Badge`-styled segmented control if reference shows one, else card-only
   is acceptable (verify against `patients.html` markup for a grid/list
   toggle button before deciding).
3. Clicking a pet/owner card calls a `[RelayCommand] OpenPet(PetSummary)` /
   `OpenOwner(OwnerSummary)` on the ViewModel — for **this** phase, these
   commands may simply show a toast ("Pet file — Phase 09") since the
   detail view doesn't exist yet; Phase 09 replaces the toast with real
   navigation to the pet-file/owner-sheet view.
4. Update `Shell/NavigationMenu.cs`'s `patients` nav item to target
   `PatientsPageViewModel`, replacing the Phase 05 placeholder.

## Out of scope
- Owner sheet / pet file detail (Phase 09).
- Add/edit pet or owner forms (introduced in Phase 09 alongside the sheet,
  since the reference's add/edit flow lives inside that same sheet/file UI).

## Acceptance criteria
- App builds/runs; Patients nav item shows the list; search text filters
  visibly; species chips filter visibly; switching pets/owners view mode
  works; clicking a card shows the "Phase 09" toast placeholder without
  error.

## PROGRESS.md update
Mark Phase 08 done; advance "Next phase" to 09; update "Repo state."
