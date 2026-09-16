# Phase 15 — Boarding — Board & List

**Band:** M · **Depends on:** 05 · **Satisfies:** R-4, R-5, R-18

## Read before starting
- `CONVENTIONS.md` §2, §3, §5
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

## PROGRESS.md update
Mark Phase 15 done; advance "Next phase" to 16; update "Repo state."
