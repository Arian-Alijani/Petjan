# Phase 24 — Cheques — Add/Edit Drawer & Bulk Actions

**Band:** M · **Depends on:** 23 · **Satisfies:** R-4, R-5, R-18

## Read before starting
- `CONVENTIONS.md` §2, §3, §5
- `Animora-UI/assets/cheques-ui.js` — full file (477 lines): drawer
  scaffold (`ensureDrawer`), status-pill/tone helpers, modal scaffold
  (`ensureModal`), the add/edit form (`paintForm`/`readForm`/
  `saveCheque`), and the date-picker widget (`dpHtml`/`bindDp`).

## Goal
Complete the Cheques screen: the add/edit drawer form (with its Jalali
date-picker widget) and wire it to Phase 23's "new cheque" / row-menu
"edit" actions and bulk-action bar where applicable.

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

## PROGRESS.md update
Mark Phase 24 done; advance "Next phase" to 25; note the
`JalaliDatePicker` reusability decision; update "Repo state."
