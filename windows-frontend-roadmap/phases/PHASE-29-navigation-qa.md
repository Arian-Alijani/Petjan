# Phase 29 — Cross-Screen Navigation Wiring & Visual QA

**Band:** M · **Depends on:** 06–28 (all screen phases) · **Satisfies:** R-4, R-19

## Read before starting
- `CONVENTIONS.md` (all sections — this is the final consistency pass)
- `PROGRESS.md` — confirm every phase 06–28 shows `✅ Done`; if any phase
  is not done, **stop and do not run Phase 29** — finish the missing
  phase(s) first (per `ROADMAP.md` §1 dependency rule).
- `Animora-UI/assets/layout.js`'s `NAV_ITEMS` (already read in Phase 05 —
  re-open only if the list needs re-confirming) and every cross-screen
  `navigateTo(...)`/`href` call surfaced during Phases 06–28 (each
  phase file already pointed these out individually — no new reading
  needed beyond `PROGRESS.md`'s decision log for anything flagged).

## Goal
This is not a new-screen phase. It closes out the roadmap by verifying
and fixing any remaining placeholder navigation targets, confirming every
sidebar item and every documented cross-screen quick-action/deep-link
from the reference now points at a real screen, and doing a final
side-by-side visual comparison pass against `Animora-UI`.

## Tasks
1. Grep the codebase for any remaining `PlaceholderPageViewModel`
   references or literal "Phase NN" placeholder toast strings introduced
   by earlier phases (e.g. Phase 10→11 hand-off, Phase 13→14, Phase
   15→16, Phase 17→18, Phase 20→21, Phase 23→24, Phase 25→26). Every one
   of these must now resolve to the real target, since all dependency
   phases are done — fix any that were missed during their own phase's
   wiring step.
2. Re-verify every `Shell/NavigationMenu.cs` entry resolves to its real
   `PageViewModelBase`-derived type (no entry should still construct
   `PlaceholderPageViewModel`).
3. Re-verify every Dashboard quick-action (Phase 06) and every
   documented cross-screen navigation call named in each screen phase
   file (e.g. Visits→Sale/Cashier hand-off, Boarding "goto-cashier")
   now navigates correctly end-to-end.
4. **Final fidelity audit (§4a + §7a) — screen by screen (17 screens).**
   Render each rebuilt screen and its `Animora-UI/*.html` reference
   side-by-side (serve `Animora-UI/` with `python -m http.server 8080` and
   screenshot each page — or reuse `shot_role.py`/`shot_tabs.py` — then
   capture the Avalonia screen per §7a). For each screen, walk an
   **element-by-element checklist** against the reference and confirm ALL of:
   - every box/card/panel/table/chart/donut/gauge/ring/badge/tab the
     reference renders is present, in the same order/grouping/count (nothing
     dropped or simplified — the historical failure mode);
   - column sets/orders in every table match the reference exactly;
   - spacing, radii, borders, font sizes/weights and grid proportions match
     the screen's Reference CSS (§8);
   - every user-facing Persian string matches character-for-character;
   - state colors/tones match 1:1.
   Any mismatch — including a missing chart/donut/ring/gauge or a dropped
   element — is a **defect that must be fixed before Phase 29 can be marked
   done**; record each fix (and which phase's screen it belonged to) in the
   `PROGRESS.md` decision log. A screen that merely "looks close" does not
   pass. Do not mark the roadmap complete while any reference element is
   missing or any measurable value was eyeballed rather than transcribed.
5. Confirm RTL is correctly applied on every screen (no accidentally
   LTR-only control introduced by a screen phase that forgot to inherit
   the app-wide `FlowDirection`).
6. Confirm no `Animora-UI` file was modified anywhere in the repo
   (`git diff --stat -- Animora-UI` should be empty) and no phase copied
   reference markup/script verbatim (spot-check a couple of the more
   complex screens' `.axaml`/`.cs` for tell-tale copied JS idioms —
   template-literal HTML strings, `document.getElementById`, etc. — none
   should be present in idiomatic Avalonia/C# code).
7. **Backend-readiness audit (standing principle 2 — `PROGRESS.md` +
   `CONVENTIONS.md` §5).** This is the seam that lets the future backend/
   database roadmap plug in with zero View/ViewModel rework, so verify it
   once, globally, at the end:
   - Every screen ViewModel depends only on an `Animora.Contracts` interface
     (constructor-injected), never on a `Mock*` type, a concrete data class,
     or a static/singleton data source directly.
   - Every `Animora.Contracts` interface a screen uses has exactly one
     concrete implementation and it lives in `Animora.Desktop/Mocks`
     (`Mock<Interface-without-I>`), registered in the DI container — so the
     backend roadmap swaps one DI line per service, nothing else.
   - No sample datum, in-line seed list, mock call, or `if (design-time)`
     branch leaks into any View (`.axaml`/`.cs`) or into a ViewModel's public
     shape. Grep for hardcoded demo data in `Pages/**` and confirm it is only
     in `Mocks/**`.
   - `Animora.Domain` / `Animora.Application` / `Animora.Sync.Abstractions`
     are still placeholder-only (no UI code leaked into them, no premature
     backend/EF/Datasync code added — that is the next roadmap).
   Any violation is a defect fixed and logged before Phase 29 is marked done.
8. Confirm `dotnet build Animora.sln` is warning-clean (or that any
   remaining warnings are pre-existing/expected and listed in
   `PROGRESS.md`).

## Out of scope
- Any new screen, control, or contract. This phase only wires/verifies
  what already exists.
- Backend/sync integration — explicitly the next, separate roadmap.

## Acceptance criteria
- Every one of the 17 reference screens is reachable from the running
  app through the same navigation paths the reference exposes (sidebar +
  documented quick-actions), with no leftover placeholder anywhere.
- **The final fidelity audit (Task 4) passed for all 17 screens:** each
  rebuilt screen is visually indistinguishable from its reference
  element-by-element (no dropped/simplified elements, all charts/donuts/
  gauges/rings present, exact CSS-derived values, exact Persian text,
  1:1 tones), and every mismatch found during the audit was fixed and
  logged in `PROGRESS.md`.
- **The backend-readiness audit (Task 7) passed:** every screen binds only to
  `Animora.Contracts` interfaces, each has a single `Mocks/**` implementation
  swapped via one DI registration, no data leaks into Views/ViewModels, and
  the Domain/Application/Sync projects are still placeholders — so the future
  backend/database roadmap can plug in with zero View/ViewModel changes.
- The full roadmap (`ROADMAP.md` §3 + §3a "what done means globally" and the
  global fidelity mandate) is satisfied.

## PROGRESS.md update
Mark Phase 29 done. Add a final "Roadmap complete" note under "Repo
state" summarizing what exists and pointing out that the **next roadmap**
(backend/sync integration, out of scope here) is the follow-up work item.
Set "Next phase to execute" to "None — Windows frontend roadmap complete;
see a future backend-integration roadmap for next steps."
