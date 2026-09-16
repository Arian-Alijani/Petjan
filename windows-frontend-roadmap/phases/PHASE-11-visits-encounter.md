# Phase 11 — Visits — Clinical Encounter Wizard

**Band:** M · **Depends on:** 10 · **Satisfies:** R-4, R-5, R-18

## Read before starting
- `CONVENTIONS.md` §2, §3, §4a (Fidelity Protocol — MANDATORY), §5, §6 (Definition of Done incl. fidelity gate + visual verification), §7a
- **Reference CSS (MANDATORY — the source of every measurable value, per §4a rule 3 / §8): `styles.css` (the encounter wizard ribbon/stepper and the vitals/history/plan/rx/review/done steps are styled in the shared file).** Read the sections these file(s) define for every element this screen renders and transcribe the exact paddings, gaps, radii, borders, font sizes/weights, grid/column templates, fixed/min/max sizes, fills and shadows — reusing Phase 02 tokens where a value equals a token. Do not eyeball or approximate.
- `Animora-UI/assets/visit.js` — **only from `renderWizard` to end**
  (~line 730–1135): wizard ribbon/stepper, vitals/history/plan/rx/review/
  done steps, `computeSubtotal`/`rxLineHtml`/`reviewLineHtml`. Do not
  re-read the triage section — Phase 10 already covered it.

## Goal
Build the multi-step clinical encounter wizard that Phase 10 hands off
into: vitals → history → plan → prescriptions/services (rx) → review →
done, ending with a hand-off to Sale/POS (mirrors the reference's
`location.href = 'cashier.html'` at the end).

## Fidelity (MANDATORY — read `CONVENTIONS.md` §4a in full)
- **Rebuild every element the reference screen renders — nothing dropped, simplified, merged, summarized, or deferred.** Every card, panel, header, stat/KPI box, chart/donut/gauge/ring/sparkline, badge/pill, tab, sub-nav, table (with the SAME columns in the SAME order), list row, avatar, icon, button, chip, empty-state and caption must exist, in the same place, order, grouping and count as the reference.
- **No visible reference element may be declared "out of scope," "nice-to-have," or "optional" here.** The only permitted simplification is *motion*: an animated value may render at its final/current value instead of animating — the element itself still exists and shows the correct value (§4a rule 6).
- **Exact measurable values** come from the Reference CSS above (§4a rule 3), not from judgement. Proportional CSS grids map to star-sized `Grid` columns/rows; fixed px sizes map to fixed sizes — transcribed, never re-guessed.
- **Exact Persian text** for every label/title/subtitle/column header/button/empty-state/tab, character-for-character; all numbers and dates through the Phase 04 Persian digit / grouping / Jalali helpers (§4a rule 4). State colors/pills map 1:1 to Phase 02 semantic tokens (§4a rule 5).
- **Backend-ready:** the ViewModel binds only to the `Animora.Contracts` interface(s); the mock is the sole swap point for a future real backend — no sample data, mock call, or design-time branch leaks into a View or the ViewModel's public shape (R-18).

## Contracts to add (`Animora.Contracts/Visits/`)
- Extend with `IVisitEncounterService` —
  `Task<EncounterDraft> GetAsync(encounterId)`,
  `Task SaveVitalsAsync(encounterId, Vitals)`,
  `Task SaveHistoryNoteAsync(encounterId, string note)`,
  `Task SavePlanAsync(encounterId, string plan)`,
  `Task AddRxLineAsync(encounterId, RxLine)`,
  `Task RemoveRxLineAsync(encounterId, rxLineId)`,
  `Task<EncounterSummary> CompleteAsync(encounterId)`.
- `Vitals`, `RxLine`, `EncounterSummary` DTOs (mirror `renderVitals`'s
  fields, `rxLineHtml`'s kind/entry shape, and `computeSubtotal`'s total).

## Mock to add
- Extend `MockVisitsTriageService` (or a sibling
  `MockVisitEncounterService` sharing the same in-memory encounter store)
  with the above methods; subtotal computed the same way as
  `computeSubtotal` (re-derived logic, not copied).

## View/ViewModel tasks
1. `Pages/Visits/VisitEncounterViewModel.cs`: `CurrentStep` enum
   (`Vitals, History, Plan, Rx, Review, Done`), step navigation commands
   (`NextStep`, `PreviousStep`, mirrors `renderStepper`/`renderFoot`),
   bound fields per step, `RxLines` collection + `Subtotal` (recomputed on
   change), `Complete` command.
2. `Pages/Visits/VisitEncounterView.axaml(.cs)`: ribbon/patient-sidebar
   (mirrors `renderRibbon`/`renderPatientSidebar`), stepper header, and a
   `ContentControl` swapping between five step sub-views:
   - `VitalsStepView.axaml`
   - `HistoryStepView.axaml`
   - `PlanStepView.axaml`
   - `RxStepView.axaml` (line list + add/remove + running subtotal)
   - `ReviewStepView.axaml` (final summary, mirrors `reviewLineHtml`)
   - `DoneStepView.axaml` (completion confirmation)
3. On `Complete`, call `INavigationService.NavigateTo<...>()` — since
   Sale/POS (Phase 20) doesn't exist yet, target the current placeholder
   for that nav item, or show a toast ("Handoff to Sale/POS — Phase 20")
   if no placeholder route is convenient; Phase 20 replaces this with a
   real navigation call passing the completed encounter's id (mirrors the
   reference's `cashier.html` redirect — note: the reference redirects to
   *Cashier*, but per `ROADMAP.md`'s screen split this product flow is
   satisfied by either Cashier (Phase 22) or Sale/POS (Phase 20); use
   Sale/POS as the target since `sale-page.js`'s `buildFromEncounter`
   is the actual encounter-to-invoice function in the reference — record
   this small clarification in `PROGRESS.md`'s decision log).
4. Wire Phase 10's `StartVisitFromTicket`/`StartVisitFromNew` placeholder
   toast to real navigation into this ViewModel now that it exists.

## Out of scope
- Sale/POS invoice building itself (Phase 20/21 — this phase only ends at
  a hand-off point).

## Acceptance criteria
- App builds/runs; starting a visit from Phase 10 lands in this wizard;
  all five steps navigate forward/back correctly; adding/removing Rx
  lines updates the subtotal; completing shows the Done step then the
  hand-off placeholder without error.
- **Visual verification (§6.7 + §7a) performed** and recorded in `PROGRESS.md`: the rebuilt screen was compared element-by-element against the reference and matches (same boxes/order/sizes/colors/Persian text); every measurable value traces to the Reference CSS; no reference element is missing. Building and running alone does NOT satisfy this phase.

## PROGRESS.md update
Mark Phase 11 done; advance "Next phase" to 12; log the Cashier-vs-Sale
hand-off clarification; update "Repo state."
