# Phase 26 — Finance — Expense & Profit Tabs

**Band:** M · **Depends on:** 25 · **Satisfies:** R-4, R-5, R-18

## Read before starting
- `CONVENTIONS.md` §2, §3, §5
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

## PROGRESS.md update
Mark Phase 26 done; advance "Next phase" to 27; update "Repo state."
