# Windows Frontend Roadmap — Index

> **Scope of this roadmap: Avalonia Native Windows desktop client ONLY.**
> No Web, no macOS, no Backend, no Sync engine implementation. Those are
> separate, future roadmaps and are explicitly **out of scope here**.

## What this is

A locked, multi-phase execution plan to rebuild the **Animora-UI** reference
prototype (`Animora-UI/`) as the real **Windows desktop frontend**, using the
project's actual stack (`STACK.md`) and architecture (`ARCHITECTURE.md`) —
pixel/behavior-faithful, but written from scratch, idiomatic to Avalonia +
MVVM. It is **not** a partial rebuild: every screen in `Animora-UI/*.html`
must eventually exist, fully, in the desktop client.

This roadmap only **plans** the work. **No phase is implemented in the
session that authored this roadmap.** Implementation starts the next time
someone opens `PROGRESS.md` and runs Phase 1.

## Files in this package

| File | Purpose |
|---|---|
| `00-REQUIREMENTS-LOCK.md` | The locked, consolidated requirements this roadmap must satisfy. Read once, for context; not needed during phase execution. |
| `ROADMAP.md` | The full phase table, sequencing/dependency rules, and the global Definition of Done. |
| `CONVENTIONS.md` | One-time-read shared engineering conventions (folder layout, naming, MVVM pattern, theming rules) — referenced by every phase instead of repeating it in each phase file. |
| `PROGRESS.md` | **The single source of truth for project state across sessions.** Says which phase is next, what exists on disk already, and any decisions/deviations recorded by prior phases. |
| `phases/PHASE-NN-*.md` | One self-contained instruction file per phase. A session executing phase NN reads **only** this file + `CONVENTIONS.md` + `PROGRESS.md` — nothing else in this package, and only the specific `STACK.md`/`ARCHITECTURE.md` sections the phase file names. |

## How an execution session must operate (mandatory)

Each phase is executed in its own Claude Code session with **zero memory**
of any other session. To stay correct and cheap on tokens:

1. Open `PROGRESS.md` → read the "Next phase" pointer and the state summary.
2. Open exactly one file: `phases/PHASE-<NN>-*.md` for that phase.
3. Open `CONVENTIONS.md` if not already internalized this session (folder
   layout, naming, MVVM/theming rules — needed to place new files correctly).
4. Read **only** the specific `STACK.md` / `ARCHITECTURE.md` sections the
   phase file points to — never the whole file.
5. Look at **only** the `Animora-UI` files the phase file names, for visual
   and behavioral reference — never browse the rest of `Animora-UI`.
6. Implement exactly the phase's "Scope" — nothing from "Out of scope."
7. Verify against the phase's "Acceptance criteria" (must build + run in
   Visual Studio / `dotnet run`, must be visually/behaviorally comparable to
   the referenced `Animora-UI` screen).
8. Update `PROGRESS.md`: mark the phase done, advance "Next phase," append
   any decision/deviation to the log. Do **not** edit other phase files.
9. Commit, per the repository's standard git workflow.

## Non-negotiable constraints (see `00-REQUIREMENTS-LOCK.md` for the full list)

- Windows frontend only. No other track may be started from this roadmap.
- `STACK.md` and `ARCHITECTURE.md` decisions are preserved exactly — never
  re-derived, never contradicted, never "upgraded" opportunistically.
- `Animora-UI` code is reference-only — **never copied**; every line is
  rewritten idiomatically for Avalonia/C#/MVVM.
- Every phase is small/medium, independently buildable and runnable in
  Visual Studio, and leaves the code ready for the (separate, future)
  backend/sync integration without rework.
