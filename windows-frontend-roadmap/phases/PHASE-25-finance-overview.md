# Phase 25 — Finance — Overview & Income Tabs

**Band:** M · **Depends on:** 05 · **Satisfies:** R-4, R-5, R-18

## Read before starting
- `CONVENTIONS.md` §2, §3, §5
- `Animora-UI/expense.html` and `Animora-UI/reports.html` (both full —
  both mount the same `assets/finance-page.js` controller with different
  `initFinancePage({ tab, range })` starting args: `expense.html` starts
  on the `expense` tab, `reports.html` starts on `overview`. This means
  **one screen implementation serves both nav entries** — see Task 5.)
- `Animora-UI/assets/finance-data.js` — full file (336 lines): sample
  data shape reference only.
- `Animora-UI/assets/finance-page.js` — **only** lines 1–~530 and
  ~685–830 (module state/number helpers, `renderTabs`, the OVERVIEW and
  INCOME tab panel functions — search for the `PANEL_FN` map and follow
  only the `overview`/`income` entries — plus `renderPanel`'s dispatch
  and the tab/range-switching bind logic). Stop before the
  EXPENSE-tab-specific and PROFIT-tab-specific render functions — those
  are Phase 26's material, along with the expense modal.

## Goal
Replace the Finance-reachable placeholders (both `expense`/`reports` nav
targets) with one shared Finance screen: tab strip (`Overview | Income |
Expense | Profit`) + timeframe range filter, fully implementing the
Overview and Income tab panels now. Expense/Profit tab panels are Phase
26 (shown as a simple placeholder for this phase).

## Contracts to add (`Animora.Contracts/Finance/`)
- `IFinanceQueryService` — `Task<FinanceOverview> GetOverviewAsync(string range)`,
  `Task<FinanceIncome> GetIncomeAsync(string range)`.
- `FinanceOverview` DTO (combined revenue/expense/profit chart series +
  summary KPIs — mirrors the overview panel's chart+KPI fields),
  `FinanceIncome` DTO (channel performance bars + top-products-by-sort —
  mirrors the income panel's fields, including the `PROD_SORTS` sort
  vocabulary: revenue/qty/profit).

## Mock to add
- `MockFinanceQueryService` — plausible time-series + KPI numbers for 2–3
  range options (`7d`/`30d`/etc. — match the reference's exact range
  vocabulary), a small channel-performance list, 6–10 sample products for
  the sort-by list.

## View/ViewModel tasks
1. `Pages/Finance/FinancePageViewModel.cs`: `ActiveTab`
   (`Overview|Income|Expense|Profit`), `Range`, commands `SetTab`,
   `SetRange`; `Overview`/`Income` data loaded per range; `Expense`/
   `Profit` tabs show a Phase 26 placeholder for now.
2. `Pages/Finance/FinancePageView.axaml(.cs)`: tab strip (mirrors
   `renderTabs`) + range control, and a `ContentControl` swapping between
   four tab sub-views:
   - `FinanceOverviewTabView.axaml` (combined chart — a simple
     multi-series line/bar chart control is acceptable; Avalonia has no
     built-in charting, so either (a) build a minimal custom
     `Canvas`-based chart control under `Controls/SimpleChart.axaml(.cs)`
     reused by Income/Expense/Profit too, or (b) render the same data as
     a styled table/KPI-only view if charting is judged too heavy for
     this phase's M budget — **decide once here** and record the choice
     in `PROGRESS.md` since Phase 26 reuses whichever approach is chosen)
   - `FinanceIncomeTabView.axaml` (channel bars + sortable product list,
     mirrors `renderTabs`'s income panel + the sort-by segmented control)
   - `FinanceExpenseTabPlaceholder` / `FinanceProfitTabPlaceholder` (toast
     or simple "Phase 26" text block)
3. Update `Shell/NavigationMenu.cs`: point **both** the `expense` and
   `reports` nav items at `FinancePageViewModel`, but pass a different
   initial `ActiveTab`/`Range` per entry (mirrors the reference's two
   HTML entry points calling `initFinancePage` with different `opts`) —
   implement via a navigation parameter (`INavigationService.NavigateTo
   <FinancePageViewModel>(initialTab, initialRange)` or equivalent
   overload) rather than two separate ViewModels, since it's genuinely
   one screen with two entry configurations.

## Out of scope
- Expense/Profit tab content and the expense-entry modal (Phase 26).

## Acceptance criteria
- App builds/runs; both the `expense` and `reports` sidebar items reach
  the same Finance screen with the correct initial tab/range per entry;
  Overview and Income tabs render correctly and respond to range changes;
  Expense/Profit tabs show the Phase 26 placeholder without error.

## PROGRESS.md update
Mark Phase 25 done; advance "Next phase" to 26; record the charting
approach decision; update "Repo state."
