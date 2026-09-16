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
4. Run the app and, screen by screen (17 screens total), open the
   corresponding `Animora-UI/*.html` reference (e.g. via a local static
   server, `python -m http.server` inside `Animora-UI/`, if a visual
   side-by-side is wanted) and confirm: layout structure, color/typography,
   spacing, and primary interactions are comparable. Log any material
   visual gap found as a `PROGRESS.md` decision-log entry with a
   pointer to which phase should own the fix (do **not** silently
   patch a prior phase's file without recording why here).
5. Confirm RTL is correctly applied on every screen (no accidentally
   LTR-only control introduced by a screen phase that forgot to inherit
   the app-wide `FlowDirection`).
6. Confirm no `Animora-UI` file was modified anywhere in the repo
   (`git diff --stat -- Animora-UI` should be empty) and no phase copied
   reference markup/script verbatim (spot-check a couple of the more
   complex screens' `.axaml`/`.cs` for tell-tale copied JS idioms —
   template-literal HTML strings, `document.getElementById`, etc. — none
   should be present in idiomatic Avalonia/C# code).
7. Confirm `dotnet build Animora.sln` is warning-clean (or that any
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
- The full roadmap (`ROADMAP.md` §3 "what done means globally") is
  satisfied.

## PROGRESS.md update
Mark Phase 29 done. Add a final "Roadmap complete" note under "Repo
state" summarizing what exists and pointing out that the **next roadmap**
(backend/sync integration, out of scope here) is the follow-up work item.
Set "Next phase to execute" to "None — Windows frontend roadmap complete;
see a future backend-integration roadmap for next steps."
