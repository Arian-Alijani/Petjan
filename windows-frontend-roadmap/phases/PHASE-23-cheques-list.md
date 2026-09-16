# Phase 23 — Cheques — List Views & Side Panels

**Band:** M · **Depends on:** 05 · **Satisfies:** R-4, R-5, R-6, R-18

## Read before starting
- `CONVENTIONS.md` §2, §3, §5
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

## PROGRESS.md update
Mark Phase 23 done; advance "Next phase" to 24; update "Repo state."
