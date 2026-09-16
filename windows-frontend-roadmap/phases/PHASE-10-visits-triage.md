# Phase 10 — Visits — Doctor Rail & Triage

**Band:** M · **Depends on:** 05 · **Satisfies:** R-4, R-5, R-18

## Read before starting
- `CONVENTIONS.md` §2, §3, §5
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

## PROGRESS.md update
Mark Phase 10 done; advance "Next phase" to 11; update "Repo state."
