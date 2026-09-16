# Phase 08 — Patients & Owners — List/Grid

**Band:** M · **Depends on:** 05 · **Satisfies:** R-4, R-5, R-18

## Read before starting
- `CONVENTIONS.md` §2, §3, §4a (Fidelity Protocol — MANDATORY), §5, §6 (Definition of Done incl. fidelity gate + visual verification), §7a
- **Reference CSS (MANDATORY — the source of every measurable value, per §4a rule 3 / §8): `styles.css` + `patients.css` (list/grid cards, search/filter bar, owner/pet card layout, badges).** Read the sections these file(s) define for every element this screen renders and transcribe the exact paddings, gaps, radii, borders, font sizes/weights, grid/column templates, fixed/min/max sizes, fills and shadows — reusing Phase 02 tokens where a value equals a token. Do not eyeball or approximate.
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

## Fidelity (MANDATORY — read `CONVENTIONS.md` §4a in full)
- **Rebuild every element the reference screen renders — nothing dropped, simplified, merged, summarized, or deferred.** Every card, panel, header, stat/KPI box, chart/donut/gauge/ring/sparkline, badge/pill, tab, sub-nav, table (with the SAME columns in the SAME order), list row, avatar, icon, button, chip, empty-state and caption must exist, in the same place, order, grouping and count as the reference.
- **No visible reference element may be declared "out of scope," "nice-to-have," or "optional" here.** The only permitted simplification is *motion*: an animated value may render at its final/current value instead of animating — the element itself still exists and shows the correct value (§4a rule 6).
- **Exact measurable values** come from the Reference CSS above (§4a rule 3), not from judgement. Proportional CSS grids map to star-sized `Grid` columns/rows; fixed px sizes map to fixed sizes — transcribed, never re-guessed.
- **Exact Persian text** for every label/title/subtitle/column header/button/empty-state/tab, character-for-character; all numbers and dates through the Phase 04 Persian digit / grouping / Jalali helpers (§4a rule 4). State colors/pills map 1:1 to Phase 02 semantic tokens (§4a rule 5).
- **Backend-ready:** the ViewModel binds only to the `Animora.Contracts` interface(s); the mock is the sole swap point for a future real backend — no sample data, mock call, or design-time branch leaks into a View or the ViewModel's public shape (R-18).

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
- **Visual verification (§6.7 + §7a) performed** and recorded in `PROGRESS.md`: the rebuilt screen was compared element-by-element against the reference and matches (same boxes/order/sizes/colors/Persian text); every measurable value traces to the Reference CSS; no reference element is missing. Building and running alone does NOT satisfy this phase.

## PROGRESS.md update
Mark Phase 08 done; advance "Next phase" to 09; update "Repo state."
