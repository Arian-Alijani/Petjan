# Phase 11 — Visits — Clinical Encounter Wizard

**Band:** M · **Depends on:** 10 · **Satisfies:** R-4, R-5, R-18

## Read before starting
- `CONVENTIONS.md` §2, §3, §5
- `Animora-UI/assets/visit.js` — **only from `renderWizard` to end**
  (~line 730–1135): wizard ribbon/stepper, vitals/history/plan/rx/review/
  done steps, `computeSubtotal`/`rxLineHtml`/`reviewLineHtml`. Do not
  re-read the triage section — Phase 10 already covered it.

## Goal
Build the multi-step clinical encounter wizard that Phase 10 hands off
into: vitals → history → plan → prescriptions/services (rx) → review →
done, ending with a hand-off to Sale/POS (mirrors the reference's
`location.href = 'cashier.html'` at the end).

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

## PROGRESS.md update
Mark Phase 11 done; advance "Next phase" to 12; log the Cashier-vs-Sale
hand-off clarification; update "Repo state."
