# Phase 12 — Vaccination Screen

**Band:** S · **Depends on:** 05 · **Satisfies:** R-4, R-5, R-18

## Read before starting
- `CONVENTIONS.md` §2, §3, §5
- `Animora-UI/vaccination.html` (full)
- `Animora-UI/assets/vaccination-data.js` — full file (167 lines): sample
  data shape reference only.
- `Animora-UI/assets/vaccination.js` — full file (508 lines): segment
  filter, due-date cell rendering, table rows, catalog/templates panels,
  SMS-compose drawer, mark-done action.

## Goal
Replace the Vaccination placeholder with the due/overdue list screen: the
segmented filter (all/due/overdue/upcoming — mirrors `S.seg`), the record
table, the vaccine-catalog side panel, and the SMS-reminder compose
drawer.

## Contracts to add (`Animora.Contracts/Vaccination/`)
- `IVaccinationService` — `Task<IReadOnlyList<VaccinationRecord>> GetRecordsAsync()`,
  `Task MarkDoneAsync(recordId)`, `Task<SmsPreview> BuildSmsPreviewAsync(recordId, templateId)`,
  `Task SendSmsAsync(recordId, string message)`.
- `VaccinationRecord` DTO (pet/owner ref, vaccine name, due date, status —
  mirrors `dueCell`/`renderRows` fields).
- `SmsPreview`, plus a small static catalog DTO (`VaccineCatalogEntry`,
  `SmsTemplate`) mirroring `renderCatalog`/`renderTemplates`.

## Mock to add
- `MockVaccinationService` — 6–10 sample records across due/overdue/
  upcoming states; 2–3 catalog entries; 2–3 SMS templates.

## View/ViewModel tasks
1. `Pages/Vaccination/VaccinationPageViewModel.cs`: `Segment` selection,
   `FilteredRecords`, `Catalog`, `Templates`, commands `SetSegment`,
   `MarkDone`, `OpenSmsCompose`, `SendSms`.
2. `Pages/Vaccination/VaccinationPageView.axaml(.cs)`: segmented filter
   control (Phase 03 styling), records table (due-date cell colored by
   urgency, mirrors `dueCell`), a catalog/templates side panel.
3. `Pages/Vaccination/SmsComposeDrawerView.axaml(.cs)`: opened via Phase
   03's `IDialogService` drawer host, showing the built preview + a Send
   action (mirrors `openSmsCompose`/`vaxSendSms`).
4. Update `Shell/NavigationMenu.cs`'s `vaccine` nav item to target
   `VaccinationPageViewModel`.

## Out of scope
- Bulk SMS batch sending UI beyond a simple "send to all overdue" command
  if trivial to add; otherwise single-record send only is sufficient.

## Acceptance criteria
- App builds/runs; Vaccination nav item shows the filtered table;
  changing segments filters visibly; "mark done" updates a record's
  status; SMS compose drawer opens with a preview and a working Send
  action (toast confirmation).

## PROGRESS.md update
Mark Phase 12 done; advance "Next phase" to 13; update "Repo state."
