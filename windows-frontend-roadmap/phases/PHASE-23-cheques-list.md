# Phase 23 — Cheques — List Views & Side Panels

**Band:** M · **Depends on:** 05 · **Satisfies:** R-4, R-5, R-6, R-18

## Read before starting
- `CONVENTIONS.md` §2, §3, §4a (Fidelity Protocol — MANDATORY), §5, §6 (Definition of Done incl. fidelity gate + visual verification), §7a
- **Reference CSS (MANDATORY — the source of every measurable value, per §4a rule 3 / §8): `styles.css` + `cheques.css` (KPIs, chips, list views, side panels, status pills).** Read the sections these file(s) define for every element this screen renders and transcribe the exact paddings, gaps, radii, borders, font sizes/weights, grid/column templates, fixed/min/max sizes, fills and shadows — reusing Phase 02 tokens where a value equals a token. Do not eyeball or approximate.
- `Animora-UI/cheques.html` (full)
- `Animora-UI/assets/cheques-data.js` — full file (248 lines): includes
  the shared `window.J` Jalali helper (already covered conceptually by
  Phase 04 — do not re-derive date math here, reuse Phase 04's
  `JalaliDate`) plus the cheques sample-data shape (reference only).
- `Animora-UI/assets/cheques.js` — full file (810 lines): KPIs, chips,
  sync-controls, table/grid/calendar/timeline render modes, side cards
  (cashflow/banks/alerts), bulk-actions bar, row-menu. This is the bulk
  of this phase's visual reference.

## Goal
Replace the Cheques placeholder with the list/board screen: KPI row,
filter chips, a view-mode switch across **table / grid / calendar /
timeline** render modes, side info cards (cashflow, banks, alerts), and
bulk-selection actions. Adding/editing a cheque (the drawer) is Phase 24.

## Fidelity (MANDATORY — read `CONVENTIONS.md` §4a in full)
- **Rebuild every element the reference screen renders — nothing dropped, simplified, merged, summarized, or deferred.** Every card, panel, header, stat/KPI box, chart/donut/gauge/ring/sparkline, badge/pill, tab, sub-nav, table (with the SAME columns in the SAME order), list row, avatar, icon, button, chip, empty-state and caption must exist, in the same place, order, grouping and count as the reference.
- **No visible reference element may be declared "out of scope," "nice-to-have," or "optional" here.** The only permitted simplification is *motion*: an animated value may render at its final/current value instead of animating — the element itself still exists and shows the correct value (§4a rule 6).
- **Exact measurable values** come from the Reference CSS above (§4a rule 3), not from judgement. Proportional CSS grids map to star-sized `Grid` columns/rows; fixed px sizes map to fixed sizes — transcribed, never re-guessed.
- **Exact Persian text** for every label/title/subtitle/column header/button/empty-state/tab, character-for-character; all numbers and dates through the Phase 04 Persian digit / grouping / Jalali helpers (§4a rule 4). State colors/pills map 1:1 to Phase 02 semantic tokens (§4a rule 5).
- **Backend-ready:** the ViewModel binds only to the `Animora.Contracts` interface(s); the mock is the sole swap point for a future real backend — no sample data, mock call, or design-time branch leaks into a View or the ViewModel's public shape (R-18).

## Contracts to add (`Animora.Contracts/Cheques/`)
- `IChequesQueryService` — `Task<IReadOnlyList<Cheque>> GetAllAsync()`,
  `Task<CashflowSummary> GetCashflowAsync()`,
  `Task<IReadOnlyList<BankSummary>> GetBankBreakdownAsync()`,
  `Task<IReadOnlyList<ChequeAlert>> GetAlertsAsync()`.
- `Cheque` DTO (bank, amount, due date, status, days-left — mirrors
  `daysLeft`/`cdInfo`/`statusPill` fields), `CashflowSummary`,
  `BankSummary`, `ChequeAlert` DTOs.

## Mock to add
- `MockChequesQueryService` — 12–18 sample cheques spanning multiple
  banks/statuses/due dates; a cashflow summary; 2–3 bank breakdown rows;
  2–3 alerts.

## View/ViewModel tasks
1. `Pages/Cheques/ChequesPageViewModel.cs`: `ActiveChip` filter (mirrors
   `renderChips`/`filtered`), `ViewMode` (`Table|Grid|Calendar|Timeline`),
   `FilteredCheques`, `SelectedIds` (bulk selection), `Kpis`, `Cashflow`,
   `Banks`, `Alerts`, commands `SetChip`, `SetViewMode`, `ToggleSelect`,
   `OpenBulkActions` (mirrors `renderBulk`).
2. `Pages/Cheques/ChequesPageView.axaml(.cs)`: KPI row (mirrors
   `renderKpis`, including the small sparkline — a simple `Polyline`-based
   mini chart is sufficient, mirrors `sparkSvg`), chip filter row, a
   view-mode segmented toggle, and four render-mode sub-views:
   - `ChequesTableView.axaml` (mirrors `renderTable`/`pager`)
   - `ChequesGridView.axaml` (mirrors `renderGrid`)
   - `ChequesCalendarView.axaml` (mirrors `renderCalendar`, using Phase
     04's `JalaliDate` for all day/month labels)
   - `ChequesTimelineView.axaml` (mirrors `renderTimeline`)
   Plus the side column: `ChequesSideView.axaml` (mirrors `renderSide`/
   `cardCashflow`/`cardBanks`/`cardAlerts`/`bindSide`).
3. Bulk-selection action bar (mirrors `renderBulk`) — actions may be
   toast-only stubs except any that clearly map to Phase 24's
   add/edit-drawer capabilities (defer those to Phase 24's wiring step).
4. Row-level "more" menu (mirrors `openRowMenu`/`closeRowMenu`) — a small
   `Popup`/`Flyout` with Edit/Delete/etc. entries; Edit is wired to
   Phase 24's drawer once that phase exists (for now, toast placeholder
   "Edit — Phase 24").
5. Update `Shell/NavigationMenu.cs`'s `cheques` nav item to target
   `ChequesPageViewModel`.

## Out of scope
- Add/edit cheque drawer (Phase 24).

## Acceptance criteria
- App builds/runs; Cheques nav item shows KPIs/chips/side cards; all four
  view modes render the same filtered cheque set correctly; bulk
  selection and the row menu work (Edit shows the Phase 24 placeholder).
- **Visual verification (§6.7 + §7a) performed** and recorded in `PROGRESS.md`: the rebuilt screen was compared element-by-element against the reference and matches (same boxes/order/sizes/colors/Persian text); every measurable value traces to the Reference CSS; no reference element is missing. Building and running alone does NOT satisfy this phase.

## PROGRESS.md update
Mark Phase 23 done; advance "Next phase" to 24; update "Repo state."
