# Phase 15 — Boarding — Board & List

**Band:** M · **Depends on:** 05 · **Satisfies:** R-4, R-5, R-18

## Read before starting
- `CONVENTIONS.md` §2, §3, §4a (Fidelity Protocol — MANDATORY), §5, §6 (Definition of Done incl. fidelity gate + visual verification), §7a
- **Reference CSS (MANDATORY — the source of every measurable value, per §4a rule 3 / §8): `styles.css` + `boarding.css` (board cards, tab counts, tone/status pills, species dropdown, section switcher).** Read the sections these file(s) define for every element this screen renders and transcribe the exact paddings, gaps, radii, borders, font sizes/weights, grid/column templates, fixed/min/max sizes, fills and shadows — reusing Phase 02 tokens where a value equals a token. Do not eyeball or approximate.
- `Animora-UI/boarding.html` (full)
- `Animora-UI/assets/boarding.js` — **only** lines 1–~300 and ~928–1055
  (tone helpers, `statusMeta`/`payLabel`/`payTone`, `tabMatches`/
  `filtered`, `renderTabCounts`, `renderCards`, and the section switcher
  `switchSection`/`initSpeciesDropdown`/`mountReveal`/`renderAll` at the
  bottom). Skip lines ~300–928 (detail/transfer/checkout/new-stay modals
  — Phase 16's material) and the history/settings sub-renders
  (`renderHistory`/`renderSettingsRooms`/`renderSettingsSvcs` — also
  Phase 16, since they're opened from the same modal system).

## Goal
Replace the Boarding placeholder with the board/list screen: status-tab
filter (mirrors `tabMatches`), stay cards grid (mirrors `renderCards`),
and the section switcher between "Board", "History", "Settings" tabs at
the page-chrome level (the detail content of History/Settings tabs is
built in Phase 16 alongside the modals they share rendering code with).

## Fidelity (MANDATORY — read `CONVENTIONS.md` §4a in full)
- **Rebuild every element the reference screen renders — nothing dropped, simplified, merged, summarized, or deferred.** Every card, panel, header, stat/KPI box, chart/donut/gauge/ring/sparkline, badge/pill, tab, sub-nav, table (with the SAME columns in the SAME order), list row, avatar, icon, button, chip, empty-state and caption must exist, in the same place, order, grouping and count as the reference.
- **No visible reference element may be declared "out of scope," "nice-to-have," or "optional" here.** The only permitted simplification is *motion*: an animated value may render at its final/current value instead of animating — the element itself still exists and shows the correct value (§4a rule 6).
- **Exact measurable values** come from the Reference CSS above (§4a rule 3), not from judgement. Proportional CSS grids map to star-sized `Grid` columns/rows; fixed px sizes map to fixed sizes — transcribed, never re-guessed.
- **Exact Persian text** for every label/title/subtitle/column header/button/empty-state/tab, character-for-character; all numbers and dates through the Phase 04 Persian digit / grouping / Jalali helpers (§4a rule 4). State colors/pills map 1:1 to Phase 02 semantic tokens (§4a rule 5).
- **Backend-ready:** the ViewModel binds only to the `Animora.Contracts` interface(s); the mock is the sole swap point for a future real backend — no sample data, mock call, or design-time branch leaks into a View or the ViewModel's public shape (R-18).

## Contracts to add (`Animora.Contracts/Boarding/`)
- `IBoardingQueryService` — `Task<IReadOnlyList<BoardingStay>> GetStaysAsync()`.
- `BoardingStay` DTO — pet/owner ref, room, check-in/out dates, status,
  payment mode (mirrors `statusMeta`/`payLabel` fields).

## Mock to add
- `MockBoardingQueryService` — 6–10 sample stays across different
  statuses (active/upcoming/completed — verify exact status vocabulary
  against `statusMeta` before naming the DTO's status enum).

## View/ViewModel tasks
1. `Pages/Boarding/BoardingPageViewModel.cs`: `ActiveSection`
   (`Board|History|Settings`), `ActiveTab` (status filter, mirrors
   `tabMatches`), `FilteredStays`, `TabCounts` (mirrors
   `renderTabCounts`), commands `SetSection`, `SetTab`,
   `OpenStayDetail(BoardingStay)` (placeholder toast → "Phase 16" for
   now), `OpenNewStay()` (placeholder toast → "Phase 16" for now).
2. `Pages/Boarding/BoardingPageView.axaml(.cs)`: section tab strip, status
   tab strip + counts, stay cards grid (color-toned by species/status,
   mirrors `petTone`/`renderCards`'s visual pattern) using Phase 03
   `Card`. History/Settings sections render a simple placeholder
   ("built in Phase 16") — acceptable since their real content shares
   render logic with Phase 16's modals per the reference's own code
   organization.
3. Update `Shell/NavigationMenu.cs`'s `boarding` nav item to target
   `BoardingPageViewModel`.

## Out of scope
- Stay detail/transfer/checkout/new-stay modals (Phase 16).
- Real History/Settings tab content (Phase 16).

## Acceptance criteria
- App builds/runs; Boarding nav item shows the board with status tabs
  filtering the stay cards correctly and counts matching the filtered
  set; History/Settings sections show their Phase 16 placeholder without
  error.
- **Visual verification (§6.7 + §7a) performed** and recorded in `PROGRESS.md`: the rebuilt screen was compared element-by-element against the reference and matches (same boxes/order/sizes/colors/Persian text); every measurable value traces to the Reference CSS; no reference element is missing. Building and running alone does NOT satisfy this phase.

## PROGRESS.md update
Mark Phase 15 done; advance "Next phase" to 16; update "Repo state."
