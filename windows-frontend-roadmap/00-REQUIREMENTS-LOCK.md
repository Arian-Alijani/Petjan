# Requirements Lock — Windows Frontend Roadmap

> Purpose: a single, exhaustive, de-duplicated restatement of every
> requirement given for this roadmap-design session, so nothing gets lost
> when the roadmap is executed across many independent future sessions.
> **This file is read once, by the roadmap's author/reviewer — phase
> execution sessions do NOT need to read this file** (see `README.md`).

## 1. Product & scope

- R-1. Product: Veterinary Management & Accounting Software. Target
  platforms overall: Windows + Web + macOS — but **this roadmap covers
  Windows desktop frontend only**.
- R-2. This session's deliverable is **the roadmap itself**, not code. No
  phase may be implemented now.
- R-3. No phase of any other track (Web frontend, macOS frontend, Backend,
  Sync engine) may appear as an *executable* phase in this roadmap. (They
  may be *mentioned* as future/out-of-scope for clarity only.)

## 2. Fidelity requirement (the actual goal)

- R-4. The Windows frontend must be built to be, on comparison, visually and
  behaviorally indistinguishable from `Animora-UI` — not "inspired by,"
  not "a similar design system," not "tokens extracted." Every screen listed
  in `Animora-UI/*.html` must be rebuilt in full, screen-by-screen, control-
  by-control, interaction-by-interaction.
- R-5. `Animora-UI` source code (HTML/CSS/JS) must **never be copied**.
  Every phase re-implements the equivalent visual/behavioral result from
  scratch, idiomatically, in the project's real stack (Avalonia + C# + MVVM),
  using `Animora-UI` only as a **visual/behavioral reference** (like a
  clickable Figma export — see `Animora-UI/README.md`).
- R-6. Because `Animora-UI` is Persian/RTL, the rebuilt Windows UI must
  preserve RTL layout, Persian digit formatting, and the Persian (Jalali)
  calendar behavior seen in the reference, using Avalonia's own RTL/
  localization facilities — not by porting the reference's JS date-math
  verbatim.

## 3. Stack & architecture preservation

- R-7. `STACK.md` is authoritative and must be followed exactly: Avalonia
  Native (§3.1), `CommunityToolkit.Mvvm` (§3.1), SQLite + EF Core 10 +
  Datasync `OfflineDbContext` for the local store (§3.3), the exact
  `CommunityToolkit.Datasync 10.1.2` pin (§4) — **but note:** per R-3, this
  roadmap's phases build UI/presentation/local-shell only; wiring live
  sync/backend calls is out of scope and deferred to the (future, separate)
  backend-integration roadmap. Where a phase needs a local data source, it
  uses design-time/mock data behind the same interfaces the real data layer
  will implement later (see §7).
- R-8. `ARCHITECTURE.md` is authoritative and must be followed exactly:
  shared kernel vs. split presentation (§7.1), client roles (§7.2), the
  Desktop client's offline-capable role, and all `A-RULE` items that are
  relevant to a presentation-layer client (e.g., A-RULE 001: domain code
  never depends on the replication engine directly; A-RULE 014: server
  write models never exposed directly to clients).
- R-9. No architectural or stack decision may be re-derived, second-guessed,
  or silently changed by any phase. If a phase author believes a documented
  decision is wrong, that is escalated via `PROGRESS.md`'s decision log —
  never overridden unilaterally.

## 4. Phase design constraints (all Locked)

- R-10. **Windows-frontend-only** phases — no phase mixes in Web/macOS/
  Backend work "for convenience."
- R-11. **Every phase must be light.** No heavy/bulky phase is allowed. Token
  and wall-clock cost per phase must stay in a small-to-medium band (see
  `ROADMAP.md` sizing bands). A screen with unusually large scope (e.g. the
  POS/Sale screen, ~1500 reference JS lines) must be **split into multiple
  phases** rather than done as one large phase.
- R-12. **Each phase is independently executable in one fresh Claude Code
  session with zero memory of prior sessions.** This requires:
  - a dedicated phase file with everything needed to execute it;
  - explicit, minimal pointers to the exact `STACK.md`/`ARCHITECTURE.md`
    sections and the exact `Animora-UI` files relevant to that phase only;
  - no dependency on conversational context from another session.
- R-13. **One progress/state file** (`PROGRESS.md`) must track: which phases
  are done, what currently exists in the repo, the next phase to run, and a
  running decision/deviation log — so a new session can orient itself
  without re-reading the whole roadmap or the whole codebase.
- R-14. **One file per phase** (`phases/PHASE-NN-*.md`), each scoped so a
  session only reads that one file (+ `CONVENTIONS.md` + `PROGRESS.md`) and
  avoids reading unrelated material, to minimize token usage.
- R-15. **Tasks per phase must be concrete and mechanical**: exactly which
  files to inspect, which files/classes to create or modify, and what
  behavior/appearance must result — not vague goals.
- R-16. **Phase sizing must stay within a fixed medium-or-below budget**
  (defined once in `ROADMAP.md`, reused by every phase file — no phase
  redefines its own sizing rules).
- R-17. **Phase sequencing must be logical and dependency-respecting**:
  foundational phases (solution scaffold, theme/design tokens, shell/
  navigation, shared component library) precede screen phases; screen
  phases are grouped by the reference's own bounded-context grouping
  (`ARCHITECTURE.md` §6) so each phase only needs one coherent slice of
  `Animora-UI`.
- R-18. **Every phase's output must be extendable later without rework**:
  phase outputs must respect the shared-kernel boundary (`ARCHITECTURE.md`
  §7.1) so that when the backend/sync roadmap runs later, it plugs into
  already-correct seams (ViewModels bound to interfaces/services, not to
  hardcoded mock calls sprinkled through views).
- R-19. **The project must run and be inspectable in Visual Studio at every
  phase boundary** — each phase ends with a state where `dotnet build` /
  opening the `.sln` in Visual Studio and running the desktop app shows the
  incremental result (even if later screens are still placeholders).

## 5. Process requirements for this roadmap-design session itself

- R-20. Investigate the project structure and existing docs efficiently —
  read only what's needed to design the roadmap correctly (done: `STACK.md`,
  `ARCHITECTURE.md` relevant sections, `Animora-UI` inventory/structure).
  Avoid exhaustively reading every reference file line-by-line.
- R-21. Before designing the roadmap, produce a dedicated step that locks in
  100% of the prompt's requirements so nothing is missed — **this file**.
- R-22. Then design the multi-phase Windows-frontend roadmap using the
  locked requirements, the stack, the architecture, the current repo state,
  and the `Animora-UI` reference.
- R-23. Place the roadmap and its supporting files cleanly in the
  repository.
- R-24. In this session, **only create the roadmap and its docs** — do not
  start implementing any phase.

## 6. Roadmap language & AI-execution efficiency

- R-25. The roadmap must be written in **English**.
- R-26. The roadmap must be optimized for AI-model execution: minimal token
  consumption per phase, unambiguous instructions, no filler prose, explicit
  file-level pointers instead of broad reading instructions.

## 7. Traceability note

Every phase file created under `phases/` must, in its own header, restate
(in one line each) which of R-4/R-5/R-7/R-8/R-18/R-19 it satisfies, so a
reviewer can audit compliance per phase without re-reading this file.
