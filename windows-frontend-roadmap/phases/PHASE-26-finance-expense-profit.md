# Phase 26 — Finance — Expense & Profit Tabs

**Band:** M · **Depends on:** 25 · **Satisfies:** R-4, R-5, R-18

## Read before starting
- `CONVENTIONS.md` §2, §3, §4a (Fidelity Protocol — MANDATORY), §5, §6 (Definition of Done incl. fidelity gate + visual verification), §7a
- **Reference CSS (MANDATORY — the source of every measurable value, per §4a rule 3 / §8): `styles.css` + `finance.css` (expense table card, the margin gauge + per-bucket profit mini-bars, most-profitable-products, expense-entry modal).** Read the sections these file(s) define for every element this screen renders and transcribe the exact paddings, gaps, radii, borders, font sizes/weights, grid/column templates, fixed/min/max sizes, fills and shadows — reusing Phase 02 tokens where a value equals a token. Do not eyeball or approximate.
- `Animora-UI/assets/finance-page.js` — **only** the EXPENSE-tab and
  PROFIT-tab entries in `PANEL_FN` (`expenseTableCard`, the margin-gauge/
  per-bucket profit mini-bars function, the most-profitable-products
  function — search around lines ~530–685 and ~557–590 not already read
  in Phase 25), plus the expense-entry modal logic (search for the modal
  open/save function near the highlight-new-row comment, ~line 900–966),
  and the keyboard tab-nav (`1..4` shortcut, ~line 966) if trivial to
  include.

## Goal
Complete the Finance screen: Expense tab (expense table + add-expense
modal) and Profit tab (margin gauge + profit mini-bars + most-profitable
products), replacing Phase 25's placeholders.

## Fidelity (MANDATORY — read `CONVENTIONS.md` §4a in full)
- **Rebuild every element the reference screen renders — nothing dropped, simplified, merged, summarized, or deferred.** Every card, panel, header, stat/KPI box, chart/donut/gauge/ring/sparkline, badge/pill, tab, sub-nav, table (with the SAME columns in the SAME order), list row, avatar, icon, button, chip, empty-state and caption must exist, in the same place, order, grouping and count as the reference. The margin gauge and the per-bucket profit mini-bars are first-class deliverables — draw them with Avalonia primitives (`Path`/`ArcSegment` for the gauge, `Rectangle`/`Border` bars); they may NOT be omitted or replaced with plain text.
- **No visible reference element may be declared "out of scope," "nice-to-have," or "optional" here.** The only permitted simplification is *motion*: an animated value may render at its final/current value instead of animating — the element itself still exists and shows the correct value (§4a rule 6).
- **Exact measurable values** come from the Reference CSS above (§4a rule 3), not from judgement. Proportional CSS grids map to star-sized `Grid` columns/rows; fixed px sizes map to fixed sizes — transcribed, never re-guessed.
- **Exact Persian text** for every label/title/subtitle/column header/button/empty-state/tab, character-for-character; all numbers and dates through the Phase 04 Persian digit / grouping / Jalali helpers (§4a rule 4). State colors/pills map 1:1 to Phase 02 semantic tokens (§4a rule 5).
- **Backend-ready:** the ViewModel binds only to the `Animora.Contracts` interface(s); the mock is the sole swap point for a future real backend — no sample data, mock call, or design-time branch leaks into a View or the ViewModel's public shape (R-18).

## Contracts to add/extend (`Animora.Contracts/Finance/`)
- Extend `IFinanceQueryService` with:
  `Task<FinanceExpense> GetExpenseAsync(string range)`,
  `Task<FinanceProfit> GetProfitAsync(string range)`,
  `Task<ExpenseEntry> AddExpenseAsync(NewExpenseRequest)`.
- `FinanceExpense` DTO (expense table rows, mirrors `expenseTableCard`),
  `FinanceProfit` DTO (margin value, per-bucket mini-bar series,
  most-profitable-products list), `NewExpenseRequest` DTO.

## Mock to add
- Extend `MockFinanceQueryService` with the above, backed by Phase 25's
  range-keyed mock data approach (reuse the same range vocabulary).

## View/ViewModel tasks
1. `Pages/Finance/FinanceExpenseTabView.axaml(.cs)`: expense table
   (Phase 03 `DataTable`) + "add expense" button opening
   `Pages/Finance/AddExpenseModalViewModel.cs` + `.axaml` (mirrors the
   modal's form fields) via Phase 03 `IDialogService.ShowModal`; newly
   added expenses highlight briefly in the table (mirrors the
   highlight-new-row behavior — a simple brief style-class toggle is
   sufficient, not a pixel-exact animation).
2. `Pages/Finance/FinanceProfitTabView.axaml(.cs)`: margin gauge (reuse
   Phase 25's chosen chart/visualization approach — a simple radial or
   bar-based gauge is acceptable), per-bucket profit mini-bars, and the
   most-profitable-products list (reuses the same `ProductSort`
   vocabulary as Phase 25's Income tab — `revenue|qty|profit`).
3. Replace Phase 25's `FinanceExpenseTabPlaceholder`/
   `FinanceProfitTabPlaceholder` with these real sub-views in
   `FinancePageViewModel`'s tab dispatch.
4. (Optional, only if trivial) Add number-key `1`–`4` tab-switch shortcut
   on the Finance screen (mirrors the reference's keyboard nav) — skip
   without penalty if it complicates the phase.

## Out of scope
- None — this phase completes the Finance screen entirely.

## Acceptance criteria
- App builds/runs; Expense tab shows the table and the add-expense modal
  works end-to-end (new row appears); Profit tab shows the gauge/mini-bars/
  product list and responds to range changes consistently with the
  Overview/Income tabs from Phase 25.
- **Visual verification (§6.7 + §7a) performed** and recorded in `PROGRESS.md`: the rebuilt screen was compared element-by-element against the reference and matches (same boxes/order/sizes/colors/Persian text); every measurable value traces to the Reference CSS; no reference element is missing. Building and running alone does NOT satisfy this phase.

## PROGRESS.md update
Mark Phase 26 done; advance "Next phase" to 27; update "Repo state."
