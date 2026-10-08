# Phase-authoring guide — write phases that execute fast and cheap

**Audience:** whoever authors/refines a `phases/PHASE-<ID>-*.md` file (human or AI).
**Goal:** an execution session should implement a phase **top-to-bottom with near-zero repository
exploration and minimal token spend** — the phase file already contains every fact the session would
otherwise have to go dig for.

The execution-session protocol (README §"Execution-session protocol") says the session may read only
what the phase file's *"Read before starting"* lists, and must never browse the repo. That contract
only pays off if the phase file front-loads the facts. This guide is how.

> **North star:** *If an execution session has to open a source file just to learn a type's shape, a
> method's name, a constant's value, a file's path, or which command to run — that fact belonged in
> the phase file.* Every such lookup is wasted budget and a chance to guess wrong.

---

## The one failure mode to design against

The biggest token sink is **ambiguity that forces verification.** Concretely, in a real session the
model spent ~30 shell commands re-reading source, almost all to answer four kinds of question:

1. **Exact shape** of an existing type it must call (properties, method names, constructor params).
2. **A pattern to copy** (an existing repository / EF config / migration / use-case / test double).
3. **A constant / convention** (`FieldLengths`, `IdGenerator`, error-code catalog, `AppendOnlyGuard`).
4. **The environment** (is `dotnet-ef` installed? where? which build script? package versions?).

Worse: the phase's own *"Design decisions"* used **proposed names that didn't match the real code**
(e.g. "add `RecordFailedLogin`" when the method is really `RegisterFailedAttempt`). A separate *"Repo
reality check"* section then corrected it — but the contradiction itself is what forced the model to
open `User.cs` to decide who was right. **Never leave two sections of a phase file disagreeing.**

---

## Required sections for a "ready-to-implement" phase file

Add these to the standard phase template. Keep prose tight; the value is in the concrete facts.

### 1. `⚡ Zero-exploration contract` (top of file)
A short banner that states the rules of engagement:
- "This file is self-contained and pre-verified; do **not** re-open the repo to understand it."
- "Implement top-to-bottom from the implementation plan."
- "Method/type names here are the **real** ones in the tree — no naming ambiguity to resolve."
- "Only if a compile error proves a snippet diverges, fix the smallest thing and log it."
- The **environment facts** the session would otherwise probe: which tools are installed and *where*,
  the exact build/test/run commands, RAM limits, and any tool it must **not** try to install.
- A **condensed copy of the CONVENTIONS `§` rules the phase cites** — so the session need not open
  `CONVENTIONS.md` at all. (Cite the `§` too, for traceability.)

### 2. `§Reference pack` (verbatim, before Design decisions)
The heart of the optimization. For **every existing type the phase calls**, paste the real signature
verbatim — copied from the tree at authoring time, not from memory:
- entities/aggregates: the exact public members, constants, factory and method **names + signatures**,
  with a one-line "use this; don't add a duplicate under another name" note where relevant;
- abstractions/ports the handlers implement (`ICommand`/`IQuery`/handler/`IDispatcher`/`IUnitOfWork`…);
- `Result`/`ErrorCode` shape and the **list of already-declared codes to reuse** vs. add;
- one **copy-me pattern** per repeated artifact kind (repository, EF config, migration column types,
  use-case file layout, validator, test double, DI-registration call);
- the DI entry-point method names and *where in them* to append (never "restructure these files").

Rule of thumb: if the model would grep for it, it goes here.

### 3. `§Design decisions` (merged, contradiction-free)
Merge the old "Design decisions" + "Repo reality check" into **one** section that speaks in terms of
the **real** names from the Reference pack. When an older/naïve name exists, mention it once as an
alias ("this IS `RegisterFailedAttempt`") and move on. State only what's genuinely new to build.
Give exact values (policy numbers, option defaults, error-code strings, enum members).

### 4. `§Implementation plan` with **exact commands**
Numbered, dependency-ordered steps. Any command the session runs (migrations, codegen, scripts) is
written out verbatim, including flags and paths, plus the expected success signal and the one
fallback ("if the tool is absent, run install-prereqs once, then retry").

### 5. Keep the standard closeouts
`Files to create / modify` (with a one-line "Contents" per row and correct paths), `Contracts/schema
added`, `Rules that apply here`, `Out of scope` (with owning phase IDs), `Acceptance criteria`
(objectively checkable — prefer `grep`/test-name assertions), and the `PROGRESS.md update` recipe.

---

## Authoring checklist (run before declaring a phase "ready")

- [ ] Every existing type the phase calls has its **real** signature pasted in `§Reference pack`.
- [ ] No two sections disagree on a name, count, or value. (Search the file for the old proposed
      names and delete them or demote them to a one-word alias.)
- [ ] Every "add a member/method" line says whether an equivalent **already exists** (reuse) or is
      genuinely new (create) — no accidental duplicates.
- [ ] Error codes: the file lists which existing codes to **reuse** and exactly how many to **add**
      (with the SCREAMING_SNAKE string and category for each new one).
- [ ] Every command the session must run is written verbatim with its success signal.
- [ ] Environment assumptions (tools + locations + what *not* to install) are stated.
- [ ] The cited `CONVENTIONS §` rules are condensed inline so `CONVENTIONS.md` need not be opened.
- [ ] `Files to create / modify` paths are real (they resolve in the current tree).
- [ ] Acceptance criteria are mechanical (a `grep`, a test name, a script exit code) — not prose.
- [ ] "Out of scope" names the owning phase for each deferred item.
- [ ] The file was **verified against the tree at authoring time** — the snippets are copies, not
      recollections. Re-verify whenever a dependency phase changes the shapes it references.
- [ ] **Baseline-verified line (audit 2026-10-08 · IMP-06).** At execution time the phase records a
      single line proving it read the true baseline, e.g.
      `> Baseline verified <YYYY-MM-DD>: migration head <NNN>, CommandTypes <N>, projects <list>`
      (consult `PROGRESS.md` → "Maintenance drift ledger" first). One diff of this line detects the
      next drift before it silently poisons migration numbering or the replicated set.

---

## Anti-patterns (don't ship a phase file with these)

- **Two competing name sets** ("Design decisions" proposes X, "reality check" says the real name is
  Y). Pick Y everywhere.
- **"See `Foo.cs` for the shape"** — paste the shape instead; the point is to avoid the open.
- **Vague commands** ("add a migration") — write the exact `dotnet-ef` invocation and flags.
- **Unstated environment** ("make sure ef tools are available") — state that they're at
  `.sandbox/tools`, and that the session must not `dotnet tool install`.
- **Prose acceptance criteria** ("the login should work") — replace with a `grep`/test-name/exit-code.
- **Over-reach** — reference-packing another phase's internals the session shouldn't touch; keep the
  pack scoped to what *this* phase calls.

---

## Why this is worth the authoring cost

Authoring a phase this way is a one-time cost paid by someone who already has the tree open. It is
repaid every execution session by: (a) far fewer exploration turns, (b) far less input-token spend
re-reading source, (c) no wrong guesses from stale/mismatched names, and (d) a session that starts
*implementing* almost immediately instead of *investigating*.
`PHASE-B03a-identity-foundation-tables-hashing-session.md` is the worked reference example of this
format (the un-lettered `PHASE-B03-*.md` is now an index over its B03a–B03c sub-sessions).
