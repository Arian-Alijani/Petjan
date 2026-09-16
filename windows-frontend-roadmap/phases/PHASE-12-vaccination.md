# Phase 12 — Vaccination Screen

**Band:** S · **Depends on:** 05 · **Satisfies:** R-4, R-5, R-18

## Read before starting
- `CONVENTIONS.md` §2, §3, §4a (Fidelity Protocol — MANDATORY), §5, §6 (Definition of Done incl. fidelity gate + visual verification), §7a
- **Reference CSS (MANDATORY — the source of every measurable value, per §4a rule 3 / §8): `styles.css` + `vaccination.css` (segment filter, due-date cells, table rows, catalog/templates panels, SMS-compose drawer).** Read the sections these file(s) define for every element this screen renders and transcribe the exact paddings, gaps, radii, borders, font sizes/weights, grid/column templates, fixed/min/max sizes, fills and shadows — reusing Phase 02 tokens where a value equals a token. Do not eyeball or approximate.
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

## Fidelity (MANDATORY — read `CONVENTIONS.md` §4a in full)
- **Rebuild every element the reference screen renders — nothing dropped, simplified, merged, summarized, or deferred.** Every card, panel, header, stat/KPI box, chart/donut/gauge/ring/sparkline, badge/pill, tab, sub-nav, table (with the SAME columns in the SAME order), list row, avatar, icon, button, chip, empty-state and caption must exist, in the same place, order, grouping and count as the reference.
- **No visible reference element may be declared "out of scope," "nice-to-have," or "optional" here.** The only permitted simplification is *motion*: an animated value may render at its final/current value instead of animating — the element itself still exists and shows the correct value (§4a rule 6).
- **Exact measurable values** come from the Reference CSS above (§4a rule 3), not from judgement. Proportional CSS grids map to star-sized `Grid` columns/rows; fixed px sizes map to fixed sizes — transcribed, never re-guessed.
- **Exact Persian text** for every label/title/subtitle/column header/button/empty-state/tab, character-for-character; all numbers and dates through the Phase 04 Persian digit / grouping / Jalali helpers (§4a rule 4). State colors/pills map 1:1 to Phase 02 semantic tokens (§4a rule 5).
- **Backend-ready:** the ViewModel binds only to the `Animora.Contracts` interface(s); the mock is the sole swap point for a future real backend — no sample data, mock call, or design-time branch leaks into a View or the ViewModel's public shape (R-18).

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
- **Actually transmitting SMS** (no real gateway) — the send action is a
  mock/no-op that shows the reference's confirmation toast. **However, every
  SMS-related UI the reference renders (the compose drawer, any bulk/"send to
  all overdue" control, recipient lists) MUST be rebuilt in full per §4a — only
  the network transmission is stubbed, never the UI.**

## Acceptance criteria
- App builds/runs; Vaccination nav item shows the filtered table;
  changing segments filters visibly; "mark done" updates a record's
  status; SMS compose drawer opens with a preview and a working Send
  action (toast confirmation).
- **Visual verification (§6.7 + §7a) performed** and recorded in `PROGRESS.md`: the rebuilt screen was compared element-by-element against the reference and matches (same boxes/order/sizes/colors/Persian text); every measurable value traces to the Reference CSS; no reference element is missing. Building and running alone does NOT satisfy this phase.

## PROGRESS.md update
Mark Phase 12 done; advance "Next phase" to 13; update "Repo state."
