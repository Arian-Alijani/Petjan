# backend-roadmap — what this package is

An execution package that takes Animora from "complete Avalonia frontend on in-memory mocks" to a
production-grade product: real encrypted local database, real domain/application layers, offline-first
behavior, multi-user sync, a self-hosted server, identity + roles, subscription + licensing +
anti-piracy, and an operator admin website — with every existing screen functionally alive.

**One phase = one AI session with zero memory of any other session.** Everything a session needs is in
its phase file plus the four package files below. Nothing else may be assumed.

## Files

| File | Role |
|---|---|
| `PROGRESS.md` | **Single source of truth for cross-session state.** Read first, update last. Holds the "Next phase" pointer. |
| `ROADMAP.md` | Locked phase-weight rule, track overview, full phase index, dependency rules, global DoD, deferred list. |
| `CONVENTIONS.md` | Every shared engineering rule. Read once per session. Phases cite it by `§`. |
| `00-REQUIREMENTS-LOCK.md` | Locked requirements `R-B01`–`R-B35`; each phase traces to IDs here. |
| `phases/PHASE-<ID>-<slug>.md` | One file per phase — the only implementation instruction a session follows. |

## Execution-session protocol (9 steps)

1. Read `backend-roadmap/PROGRESS.md`. Take the phase ID from its **"Next phase"** pointer.
2. Read `backend-roadmap/CONVENTIONS.md` — at minimum the `§` sections your phase file names.
3. Read **only** `backend-roadmap/phases/PHASE-<ID>-*.md`. Do not read other phase files.
4. Read **only** the documentation sections and repo files that phase file's "Read before starting"
   lists. Nothing else. Do not browse the repo.
5. Implement exactly the phase's scope — its "Files to create / modify" table and "Implementation
   steps". Its "Design decisions" are already made: implement them, never re-derive or improve them.
6. Add the tests the phase's acceptance criteria require. **Exception — Track A phases A05–A11
   (ROADMAP §3.10, CONVENTIONS §9):** write **no** tests; instead fill the phase's "Test coverage owed
   to A12" section. All Track A tests are authored later, in phase **A12**.
7. Verify every acceptance criterion: `scripts/sandbox/build.sh full` = 0 warnings / 0 errors,
   `scripts/sandbox/test.sh` green, and the runnability rule (`CONVENTIONS.md` §11). **For A05–A11 the
   only verification is a lightweight `build.sh full` (run if needed) plus the offline smoke run — do
   not invoke `test.sh`; A12 runs the full suite.**
8. Update `PROGRESS.md`: mark the phase done, advance "Next phase", append a "Repo state" entry, log any
   deviation in the decision log.
9. Commit per the repo git workflow (branch `genspark_ai_developer`, conventional commit message).

## Rules for an execution session

- **Never read** `Animora-UI/**`, `Database-Sampel/**`, or any file under
  `windows-frontend-roadmap/phases/**`. They are irrelevant to this track and waste the budget.
  (`windows-frontend-roadmap/PROGRESS.md` may be cited as frontend history only.)
- **Never read** another phase's file — its state is summarized in `PROGRESS.md`.
- **Never re-litigate** a stack or architecture decision. `STACK.md` and `ARCHITECTURE.md` govern;
  pinned versions are not "upgraded".
- **Never refactor** a prior phase's output unless your phase file explicitly instructs it.
- **Never exceed** the phase's scope. Work that belongs to a later phase is listed in its
  "Out of scope" section with the owning phase ID — leave it there.
- **Never leave** the solution non-building, and (Tracks A–C) never leave the desktop non-runnable
  offline.
- **Never introduce** a technology outside `STACK.md` §11 (no Redis, broker, cloud IdP, cloud telemetry,
  CDN, EF Core 11, per-tenant database, hard-coded TLS version, Native AOT with EF Core).

## Start here

`PROGRESS.md` → "Next phase". At authoring time that is **A01**.
