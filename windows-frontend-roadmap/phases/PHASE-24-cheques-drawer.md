# Phase 24 — Cheques — Add/Edit Drawer & Bulk Actions

**Band:** M · **Depends on:** 23 · **Satisfies:** R-4, R-5, R-18

## Read before starting
- `CONVENTIONS.md` §2, §3, §4a (Fidelity Protocol — MANDATORY), §5, §6 (Definition of Done incl. fidelity gate + visual verification), §7a
- **Reference CSS (MANDATORY — the source of every measurable value, per §4a rule 3 / §8): `styles.css` + `cheques.css` (add/edit drawer scaffold + form, status-pill helpers, modal scaffold, date-picker widget).** Read the sections these file(s) define for every element this screen renders and transcribe the exact paddings, gaps, radii, borders, font sizes/weights, grid/column templates, fixed/min/max sizes, fills and shadows — reusing Phase 02 tokens where a value equals a token. Do not eyeball or approximate.
- `Animora-UI/assets/cheques-ui.js` — full file (477 lines): drawer
  scaffold (`ensureDrawer`), status-pill/tone helpers, modal scaffold
  (`ensureModal`), the add/edit form (`paintForm`/`readForm`/
  `saveCheque`), and the date-picker widget (`dpHtml`/`bindDp`).

## Goal
Complete the Cheques screen: the add/edit drawer form (with its Jalali
date-picker widget) and wire it to Phase 23's "new cheque" / row-menu
"edit" actions and bulk-action bar where applicable.

## Fidelity (MANDATORY — read `CONVENTIONS.md` §4a in full)
- **Rebuild every element the reference screen renders — nothing dropped, simplified, merged, summarized, or deferred.** Every card, panel, header, stat/KPI box, chart/donut/gauge/ring/sparkline, badge/pill, tab, sub-nav, table (with the SAME columns in the SAME order), list row, avatar, icon, button, chip, empty-state and caption must exist, in the same place, order, grouping and count as the reference.
- **No visible reference element may be declared "out of scope," "nice-to-have," or "optional" here.** The only permitted simplification is *motion*: an animated value may render at its final/current value instead of animating — the element itself still exists and shows the correct value (§4a rule 6).
- **Exact measurable values** come from the Reference CSS above (§4a rule 3), not from judgement. Proportional CSS grids map to star-sized `Grid` columns/rows; fixed px sizes map to fixed sizes — transcribed, never re-guessed.
- **Exact Persian text** for every label/title/subtitle/column header/button/empty-state/tab, character-for-character; all numbers and dates through the Phase 04 Persian digit / grouping / Jalali helpers (§4a rule 4). State colors/pills map 1:1 to Phase 02 semantic tokens (§4a rule 5).
- **Backend-ready:** the ViewModel binds only to the `Animora.Contracts` interface(s); the mock is the sole swap point for a future real backend — no sample data, mock call, or design-time branch leaks into a View or the ViewModel's public shape (R-18).

## Contracts to add/extend (`Animora.Contracts/Cheques/`)
- Extend `IChequesQueryService` with:
  `Task<Cheque> CreateAsync(NewChequeRequest)`,
  `Task UpdateAsync(chequeId, UpdateChequeRequest)`,
  `Task DeleteAsync(chequeId)`,
  `Task BulkUpdateStatusAsync(IReadOnlyList<Guid> ids, string newStatus)`.
- `NewChequeRequest`, `UpdateChequeRequest` DTOs (mirror `readForm`'s
  field set: bank, amount, due date, payee/payer, status, note).

## Mock to add
- Extend `MockChequesQueryService` with the above, backed by Phase 23's
  in-memory cheque list.

## View/ViewModel tasks
1. `Controls/JalaliDatePicker.axaml(.cs)`: a small reusable date-picker
   control (mirrors `dpHtml`/`bindDp`'s calendar-popup behavior) built on
   Phase 04's `JalaliDate` — place under `Animora.Desktop/Controls` since
   it's generically reusable (Appointments/Boarding/Patients-weight also
   need Jalali date entry; if those earlier phases used an inline/simpler
   date field, that's acceptable — this phase does not retroactively
   refactor them unless trivial to do so; note any such follow-up in
   `PROGRESS.md` rather than performing a wide refactor now).
2. `Pages/Cheques/ChequeDrawerViewModel.cs` + `.axaml`: full add/edit form
   (mirrors `paintForm`), using `JalaliDatePicker` for the due-date field,
   Save/Cancel commands (mirrors `saveCheque`) — opened via Phase 03
   `IDialogService.ShowDrawer` from Phase 23's "new cheque" button and
   row-menu "Edit" entry (both currently toast placeholders — replace
   both).
3. Wire Phase 23's bulk-action bar entries that map to
   `BulkUpdateStatusAsync`/delete (only those with a clear reference
   counterpart in `cheques-ui.js`/`cheques.js`'s `renderBulk` — do not
   invent bulk actions the reference doesn't have).

## Out of scope
- None — this phase completes the Cheques screen entirely.

## Acceptance criteria
- App builds/runs; "new cheque" opens the drawer, saves, and the new
  cheque appears in Phase 23's list/grid/calendar/timeline views; editing
  an existing cheque via the row menu updates it correctly; bulk actions
  that map to a reference behavior work.
- **Visual verification (§6.7 + §7a) performed** and recorded in `PROGRESS.md`: the rebuilt screen was compared element-by-element against the reference and matches (same boxes/order/sizes/colors/Persian text); every measurable value traces to the Reference CSS; no reference element is missing. Building and running alone does NOT satisfy this phase.

## PROGRESS.md update
Mark Phase 24 done; advance "Next phase" to 25; note the
`JalaliDatePicker` reusability decision; update "Repo state."
