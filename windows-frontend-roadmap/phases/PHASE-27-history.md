# Phase 27 — History — Unified Ledger

**Band:** M · **Depends on:** 05 · **Satisfies:** R-4, R-5, R-6, R-18

## Read before starting
- `CONVENTIONS.md` §2, §3, §5
- `Animora-UI/history.html` (full)
- `Animora-UI/assets/history.js` — full file (984 lines): aggregates
  events from all data sources into one chronological ledger, with
  filters and a CSV export action (`HIST.exportCSV`).

## Goal
Replace the History placeholder with the unified event ledger screen:
chronological list of cross-module events (sale, appointment, vaccine,
expense, inventory, cheque), with filters and CSV export.

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

## PROGRESS.md update
Mark Phase 27 done; advance "Next phase" to 28; update "Repo state."
