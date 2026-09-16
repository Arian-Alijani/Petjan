# Phase 10 — Visits — Doctor Rail & Triage

**Band:** M · **Depends on:** 05 · **Satisfies:** R-4, R-5, R-18

## Read before starting
- `CONVENTIONS.md` §2, §3, §4a (Fidelity Protocol — MANDATORY), §5, §6 (Definition of Done incl. fidelity gate + visual verification), §7a
- **Reference CSS (MANDATORY — the source of every measurable value, per §4a rule 3 / §8): `styles.css` (the visits scene/gate, doctor rail, workspace, booked-list vs. new-visit form, and patient picker are styled in the shared file).** Read the sections these file(s) define for every element this screen renders and transcribe the exact paddings, gaps, radii, borders, font sizes/weights, grid/column templates, fixed/min/max sizes, fills and shadows — reusing Phase 02 tokens where a value equals a token. Do not eyeball or approximate.
- `Animora-UI/visits.html` (full)
- `Animora-UI/assets/visit.js` — **only** lines 1–~730** (up to, not
  including, `renderWizard`): scene/gate rendering, doctor rail
  (`docApptCount`/`docWalkinCount`/`renderDocRail`/`selectDoctor`),
  workspace (booked-list vs. new-visit form), patient picker block, and
  `startVisitFromTicket`/`startVisitFromNew`/`buildEncounter`. Stop before
  the wizard section — that is Phase 11.

## Goal
Replace the Visits placeholder with the pre-encounter screen: doctor
selection rail, each doctor's booked-appointment list vs. new walk-in
visit form, and the transition into starting an encounter (which Phase 11
will render).

## Fidelity (MANDATORY — read `CONVENTIONS.md` §4a in full)
- **Rebuild every element the reference screen renders — nothing dropped, simplified, merged, summarized, or deferred.** Every card, panel, header, stat/KPI box, chart/donut/gauge/ring/sparkline, badge/pill, tab, sub-nav, table (with the SAME columns in the SAME order), list row, avatar, icon, button, chip, empty-state and caption must exist, in the same place, order, grouping and count as the reference.
- **No visible reference element may be declared "out of scope," "nice-to-have," or "optional" here.** The only permitted simplification is *motion*: an animated value may render at its final/current value instead of animating — the element itself still exists and shows the correct value (§4a rule 6).
- **Exact measurable values** come from the Reference CSS above (§4a rule 3), not from judgement. Proportional CSS grids map to star-sized `Grid` columns/rows; fixed px sizes map to fixed sizes — transcribed, never re-guessed.
- **Exact Persian text** for every label/title/subtitle/column header/button/empty-state/tab, character-for-character; all numbers and dates through the Phase 04 Persian digit / grouping / Jalali helpers (§4a rule 4). State colors/pills map 1:1 to Phase 02 semantic tokens (§4a rule 5).
- **Backend-ready:** the ViewModel binds only to the `Animora.Contracts` interface(s); the mock is the sole swap point for a future real backend — no sample data, mock call, or design-time branch leaks into a View or the ViewModel's public shape (R-18).

## Contracts to add (`Animora.Contracts/Visits/`)
- `IVisitsTriageService` — `Task<IReadOnlyList<DoctorRailInfo>> GetDoctorsAsync()`,
  `Task<IReadOnlyList<BookedAppointmentRef>> GetBookedForDoctorAsync(doctorId)`,
  `Task<EncounterDraft> StartFromTicketAsync(ticketId)`,
  `Task<EncounterDraft> StartNewAsync(NewVisitRequest)`.
- `DoctorRailInfo` DTO (id, name, appointment count, walk-in count —
  mirrors `docApptCount`/`docWalkinCount`).
- `BookedAppointmentRef`, `NewVisitRequest`, `EncounterDraft` DTOs (mirror
  `buildEncounter`'s shape — patient, type, priority, note, source ticket).

## Mock to add
- `MockVisitsTriageService` — 2–3 sample doctors with a handful of booked
  appointments each; `StartFromTicketAsync`/`StartNewAsync` produce a new
  `EncounterDraft` with a fresh id.

## View/ViewModel tasks
1. `Pages/Visits/VisitsTriagePageViewModel.cs`: `ObservableCollection
   <DoctorRailInfo> Doctors`, `SelectedDoctor`, `SelectedDoctorBookings`,
   `Mode` (`Booked|NewVisit`), new-visit form fields (patient picker
   result, type, priority, note — mirrors the `S.nb` state in the
   reference), commands `SelectDoctor`, `SetMode`, `StartVisitFromTicket`,
   `StartVisitFromNew`.
2. `Pages/Visits/VisitsTriagePageView.axaml(.cs)`: doctor rail (left strip
   of doctor tiles with counts, mirrors `renderDocRail`), workspace panel
   (booked list **or** new-visit form depending on `Mode`, mirrors
   `renderWorkspace`/`bookedListHtml`/`newVisitFormHtml`), an empty-state
   when no doctor is selected (mirrors `emptyStateHtml`).
3. Starting a visit (`StartVisitFromTicket`/`StartVisitFromNew`) calls
   `INavigationService.NavigateTo<VisitEncounterViewModel>()` — for
   **this** phase, since the encounter wizard doesn't exist yet, target a
   toast placeholder ("Encounter wizard — Phase 11") instead; Phase 11
   replaces it with real navigation, passing the `EncounterDraft`.
4. Update `Shell/NavigationMenu.cs`'s `visits` nav item to target
   `VisitsTriagePageViewModel`.

## Out of scope
- The clinical encounter wizard itself (Phase 11).

## Acceptance criteria
- App builds/runs; Visits nav item shows the doctor rail; selecting a
  doctor shows their booked list; switching to "new visit" shows the
  patient-pick + type/priority/note form; starting a visit (either path)
  shows the Phase 11 placeholder toast without error.
- **Visual verification (§6.7 + §7a) performed** and recorded in `PROGRESS.md`: the rebuilt screen was compared element-by-element against the reference and matches (same boxes/order/sizes/colors/Persian text); every measurable value traces to the Reference CSS; no reference element is missing. Building and running alone does NOT satisfy this phase.

## PROGRESS.md update
Mark Phase 10 done; advance "Next phase" to 11; update "Repo state."
