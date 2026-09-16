# Phase 27 — History — Unified Ledger

**Band:** M · **Depends on:** 05 · **Satisfies:** R-4, R-5, R-6, R-18

## Read before starting
- `CONVENTIONS.md` §2, §3, §4a (Fidelity Protocol — MANDATORY), §5, §6 (Definition of Done incl. fidelity gate + visual verification), §7a
- **Reference CSS (MANDATORY — the source of every measurable value, per §4a rule 3 / §8): `styles.css` + `history.css` (unified ledger rows, filter bar, grouping/date headers).** Read the sections these file(s) define for every element this screen renders and transcribe the exact paddings, gaps, radii, borders, font sizes/weights, grid/column templates, fixed/min/max sizes, fills and shadows — reusing Phase 02 tokens where a value equals a token. Do not eyeball or approximate.
- `Animora-UI/history.html` (full)
- `Animora-UI/assets/history.js` — full file (984 lines): aggregates
  events from all data sources into one chronological ledger, with
  filters and a CSV export action (`HIST.exportCSV`).

## Goal
Replace the History placeholder with the unified event ledger screen:
chronological list of cross-module events (sale, appointment, vaccine,
expense, inventory, cheque), with filters and CSV export.

## Fidelity (MANDATORY — read `CONVENTIONS.md` §4a in full)
- **Rebuild every element the reference screen renders — nothing dropped, simplified, merged, summarized, or deferred.** Every card, panel, header, stat/KPI box, chart/donut/gauge/ring/sparkline, badge/pill, tab, sub-nav, table (with the SAME columns in the SAME order), list row, avatar, icon, button, chip, empty-state and caption must exist, in the same place, order, grouping and count as the reference.
- **No visible reference element may be declared "out of scope," "nice-to-have," or "optional" here.** The only permitted simplification is *motion*: an animated value may render at its final/current value instead of animating — the element itself still exists and shows the correct value (§4a rule 6).
- **Exact measurable values** come from the Reference CSS above (§4a rule 3), not from judgement. Proportional CSS grids map to star-sized `Grid` columns/rows; fixed px sizes map to fixed sizes — transcribed, never re-guessed.
- **Exact Persian text** for every label/title/subtitle/column header/button/empty-state/tab, character-for-character; all numbers and dates through the Phase 04 Persian digit / grouping / Jalali helpers (§4a rule 4). State colors/pills map 1:1 to Phase 02 semantic tokens (§4a rule 5).
- **Backend-ready:** the ViewModel binds only to the `Animora.Contracts` interface(s); the mock is the sole swap point for a future real backend — no sample data, mock call, or design-time branch leaks into a View or the ViewModel's public shape (R-18).

## Contracts to add (`Animora.Contracts/History/`)
- `IHistoryQueryService` — `Task<IReadOnlyList<HistoryEvent>>
  GetEventsAsync(HistoryFilter filter)`, `Task<string> ExportCsvAsync
  (HistoryFilter filter)` (returns CSV text; actual file-save dialog is a
  thin platform call in the View/command, not the service).
- `HistoryEvent` DTO (timestamp, module/kind tag, title, amount/detail,
  actor — mirror whatever unifying shape `history.js`'s row renderer
  uses), `HistoryFilter` DTO (date range, module-kind multi-select,
  free-text search).

## Mock to add
- `MockHistoryQueryService` — 20–30 synthetic events spanning all module
  kinds (sale/appointment/vaccine/expense/inventory/cheque), spread over
  the last ~30 days, with a working in-memory filter implementation.

## View/ViewModel tasks
1. `Pages/History/HistoryPageViewModel.cs`: `Filter` (bound to filter UI
   fields), `Events` (filtered/sorted, mirrors the reference's aggregate-
   then-filter pipeline), `ExportCsv` command (writes via .NET's standard
   file-save flow — an Avalonia `IStorageProvider.SaveFilePickerAsync`
   call triggered from the command, not hardcoded to a fixed path).
2. `Pages/History/HistoryPageView.axaml(.cs)`: page header + export
   button, filter bar (date range, module-kind chips, search box),
   chronological event list (module-kind icon/badge per row, using Phase
   03 `Badge` + `Icons.axaml`), using Phase 04's `JalaliDate` for all
   timestamps.
3. Update `Shell/NavigationMenu.cs`'s `history` nav item to target
   `HistoryPageViewModel`.

## Out of scope
- Real cross-module data — this phase's mock is self-contained synthetic
  data; it does **not** attempt to pull live data from Phases 06–26's
  mocks (that kind of cross-context aggregation is exactly what the real
  backend/reporting layer will do later, per `ARCHITECTURE.md` §6's
  Reporting-context read-only rule — a UI-only phase must not simulate
  that wiring prematurely).

## Acceptance criteria
- App builds/runs; History nav item shows the filterable chronological
  list; date-range/module/search filters narrow the list correctly;
  export produces a CSV file via the OS save dialog.
- **Visual verification (§6.7 + §7a) performed** and recorded in `PROGRESS.md`: the rebuilt screen was compared element-by-element against the reference and matches (same boxes/order/sizes/colors/Persian text); every measurable value traces to the Reference CSS; no reference element is missing. Building and running alone does NOT satisfy this phase.

## PROGRESS.md update
Mark Phase 27 done; advance "Next phase" to 28; update "Repo state."
