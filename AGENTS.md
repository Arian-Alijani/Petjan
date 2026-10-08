# AGENTS.md — read this first, then stop reading

Single onboarding map for AI agents. Goal: understand this repo and reach the
right file **without opening files you don't need**. Read this file fully; open
anything else only when a rule below says to.

## What this repo is (one paragraph)

**Animora** — a **commercial, vendor-hosted (SaaS) veterinary clinic
management + accounting** product: the vendor (Animora) runs **one central,
multi-tenant server**, customers **register + buy a subscription on the public
website, download the desktop app, log in, and use it** — clinics never run a
server of their own ("self-hosted" in the specs means *the vendor
self-operates its server, not a cloud PaaS* — see `ARCHITECTURE.md` header /
§13.4/§17; it never means per-clinic hosting). The app itself is an
**offline-first desktop client** (Persian / RTL UI, Jalali calendar) that
syncs to that server. One C# solution on **.NET 10**, UI is **Avalonia + MVVM
(CommunityToolkit.Mvvm)**. Current state: the **Windows desktop frontend is
complete** (all 17 screens built) and now runs on the **real local SQLite
backend** — the backend roadmap's **Track C mock purge is finished**, the
`Mocks/` folder was removed entirely, and every screen's `Animora.Contracts`
interface is bound to its real `Animora.Client.Services` implementation over
`Animora.Application` + `Animora.Persistence.Sqlite` (authorized query
pipeline, local encrypted store, activation-scoped refresh — offline-first,
runs with **no server yet**). The remaining backend work — **the central
multi-tenant server, subscription/licensing, and Datasync** in Tracks D–E — is
still ahead and **not** wired in. **Do not state which backend
phase is "current", or that any screen is bug-free, from memory — it changes
every session.** The live answer is always `backend-roadmap/PROGRESS.md`'s
"Next phase" line and its state table; read that before assuming a project's
shape. Exact per-project state is in the table below.

## The top-level folders — which one to touch

| Folder | What it is | When you touch it |
|---|---|---|
| **`src/`** (the ".NET project") | **The real product.** All changes go here. | **Default. Almost every task edits here.** |
| **`Animora-UI/`** | Throwaway HTML/CSS/JS **design reference** (like a clickable Figma). Not the stack, not real data. | **Only when the task explicitly says to inspect/use it, or you need the exact visual/behavioral spec of a screen you are rebuilding.** Never edit it. Never copy its code. Never open its `*.srcmap.json` files (design-tool artifacts, no product info — gitignored). |
| **`backend-roadmap/`** | Execution plan + session protocol for the backend track (Domain/Application/Persistence/server/sync). | **Any backend-track task.** See the dedicated section below — it has its own token-budget protocol; follow it instead of re-deriving one. |
| **`windows-frontend-roadmap/`** | Completed execution plan for the frontend track (all 17 screens). | Historical reference only — the roadmap is done. Never open `phases/**` (superseded by the finished code). |
| **`Database-Sampel/`** | A SQL Server `.bak` sample source-import dump, unrelated to the SQLite backend track. | **Never read** — binary, 11 MB, not tracked in git, no product information. |

> If a task doesn't mention design/visual reference, **do not open `Animora-UI/`** — it wastes tokens.

## Working on the backend track → read `backend-roadmap/`, not this file's memory

The backend track (`src/Animora.Domain`, `Animora.Application`,
`Animora.Persistence.Sqlite`, `Animora.Infrastructure.Local`,
`Animora.Client.Services`, plus the future server/sync) has **its own
self-contained execution protocol** — do not improvise one:

1. Read `backend-roadmap/PROGRESS.md` — **top of file** for the standing
   principles + the current per-area state table, then the **"Next phase"**
   line for which phase ID is active right now.
2. Read **only** `backend-roadmap/phases/PHASE-<ID>-*.md` for that ID — each
   phase file is authored to be self-contained (see
   `backend-roadmap/PHASE-AUTHORING-GUIDE.md`) so it does **not** require
   opening `CONVENTIONS.md`, other phase files, or browsing `src/`.
3. `backend-roadmap/README.md` has the full 9-step session protocol if the
   phase file's own "Zero-exploration contract" banner isn't enough context.
4. **Never** read `Animora-UI/**`, `Database-Sampel/**`, or
   `windows-frontend-roadmap/phases/**` from a backend session — explicitly
   irrelevant and listed as a rule in `backend-roadmap/README.md`.
5. **Never** read another phase's file or `backend-roadmap/PROGRESS.md`'s
   older "Repo state log" / "Decision & deviation log" entries unless the
   phase file you're executing explicitly cites that phase ID as something
   to read — they are cross-session history, not current-task context.

`backend-roadmap/PROGRESS.md` is large (it accumulates one entry per
completed phase) — its **top section** (principles + state table + Next
phase + Phase status) is the only part every session needs; the log
sections below it are per-phase archives, consulted only when a specific
phase file tells you to.

## Where things live in `src/` (so you don't grep blindly)

**Nine projects.** Desktop/Contracts are complete and now run on the real
local backend (Track C mock purge done); the server/Datasync part of the
backend track (Tracks D–E) is still mid-flight — check
`backend-roadmap/PROGRESS.md` before assuming a project's state.

```
src/Animora.Desktop/            ← THE app UI. Frontend-track edits happen here. Now on the real local backend via Animora.Client.Services (Track C mock purge complete; Mocks/ removed).
  App.axaml.cs                  ← DI container: every service + ViewModel is registered here.
  Program.cs                    ← entry point (rarely touched).
  Shell/                        ← MainWindow, Sidebar, Topbar, navigation, ViewLocator, PageViewModelBase.
  Pages/<Area>/                 ← one folder per screen (Dashboard, Patients, Visits, ...). See map below.
  Controls/                     ← reusable custom controls (DataTable, StatCard, DonutRing, JalaliDatePicker, ...).
  Resources/Theme/               ← design tokens: Colors / Typography / Spacing / Elevation / Icons (.axaml).
  Resources/Controls/            ← XAML styles per control family.
  Services/                      ← Navigation, Dialog, Toast, RecordSheet (interface + impl pairs).
  (Mocks/ removed by Track C C11 — Desktop consumes the real Animora.Client.Services implementations; see PROGRESS.md.)
  Localization/                  ← RTL, Persian digits, Jalali calendar helpers.
src/Animora.Contracts/<Area>/   ← DTOs + query/service interfaces per bounded context, one folder per screen. The seam Desktop ↔ future real services.
src/Animora.Contracts.Wire/     ← backend-track wire DTOs (CommandEnvelope, SettlementResult, error shapes) — server↔client transport, not used by Desktop.
src/Animora.Domain/             ← backend track. Real: entities/aggregates/value objects per bounded context (Boarding, Cashier, Clinical, Finance, Identity, Inventory, Patients, Purchasing, Sales, Scheduling + Primitives/Results/Time/ValueObjects/Events). Zero project references by design (CONVENTIONS §2) — never imports Contracts.
src/Animora.Application/        ← backend track. Real: command/query dispatch pipeline, behaviors, use cases, validation, security-plane ports (Abstractions/Behaviors/Commands/Correlation/Dispatch/Outbox/Ports/Security/UseCases/Validation).
src/Animora.Persistence.Sqlite/ ← backend track. Real: the local EF Core SQLite store — DbContext, migrations, repositories, seed, queries, maintenance. This is the desktop's offline database, reached indirectly through Client.Services (Desktop never references EF directly).
src/Animora.Infrastructure.Local/ ← backend track. Real: OS keystore/encryption, native SQLite assertion, clocks, local file store (Options/Security/Startup/Storage/Time).
src/Animora.Client.Services/    ← backend track. Real: Track-C implementations of the Contracts interfaces over Application + Persistence (Identity, Patients, Reception, Visits, Vaccination, Appointments, Boarding, Inventory, Purchasing, Sales, Cashier, Cheques, Finance, History, Dashboard, Search, Profile, Sync). This is what Desktop's DI now binds to — scope-per-call over the dispatcher, no EF/SQL/rule leaks into Desktop.
src/Animora.Sync.Abstractions/  ← still an empty scaffold (csproj + README) — the future ISyncEngine, added by Track E.
```

**Before touching anything under `src/Animora.Domain`, `Animora.Application`,
`Animora.Persistence.Sqlite`, `Animora.Infrastructure.Local` or
`Animora.Client.Services`: read `backend-roadmap/PROGRESS.md`'s "Next phase"
line first — see the dedicated section below.** These projects are only
partially built and change every session; do not assume yesterday's shape.

**Per-screen file pattern** inside `Pages/<Area>/`:
`<Screen>View.axaml` (+ `.axaml.cs` code-behind) and `<Screen>ViewModel.cs`.
Modals/sheets/wizards are extra `*View`+`*ViewModel` pairs in the same folder.

**Bounded contexts / screens** (same names across `Pages/`, `Contracts/`, `Animora-UI/*.html`):
Dashboard, Reception, Patients, Visits, Vaccination, Appointments, Boarding,
Inventory, Purchasing, Sales, Cashier, Cheques, Finance, History, Profile.

## The one architectural rule that governs every change

Data flows: **View → ViewModel → `Animora.Contracts` interface (via constructor DI) → real `Animora.Client.Services` impl → `Animora.Application` dispatch → local SQLite store.**
- ViewModels depend **only** on Contracts interfaces, never on a concrete service, EF/SQL types, or DI scope. The seam is unchanged; only the implementation behind it is real now (the `Mock*` classes were removed by Track C).
- No sample/seed data or `if (design-time)` branches in `Pages/**`; seed data lives in the Persistence layer's seeders, not the UI.
- To add a screen's data need: add/extend the interface + DTO in `Contracts/<Area>/`, implement it in the matching `Animora.Client.Services/<Area>/` service over the dispatcher, register both in `App.axaml.cs`.
- RTL is set once app-wide in `App.axaml`; don't re-set per screen.

## Build / run / verify (this sandbox)

Run from repo root. Scripts self-configure the SDK/env.
- Compile fast (Desktop only): `scripts/sandbox/build.sh`  → expect **0 warnings / 0 errors**.
- Whole solution: `scripts/sandbox/build.sh full`.
- Smoke-run under Xvfb: `scripts/sandbox/run.sh` → success prints **SMOKE RUN OK**.
- First-time env: `scripts/sandbox/install-prereqs.sh`. Health: `scripts/sandbox/doctor.sh`.
- More detail only if needed: `scripts/sandbox/AI_GUIDE.md`.

## Deeper docs — open ONLY the section you need, never the whole file

These are large. Do not read end-to-end. Grep for the one section/§ you need.

| File | Answers | Open when |
|---|---|---|
| `ARCHITECTURE.md` | how the system is structured/behaves: bounded contexts, layering, patterns, invariants, sync/offline model. | you need the *design intent* behind a module or an invariant. Jump to the relevant §. |
| `STACK.md` | which technology/version and why (Avalonia, .NET 10, packages, DB engine). | you need an exact version, package, or a stack capability/limitation. |
| `PERFORMANCE.md` | the numbered `§` performance rules code comments cite (e.g. `PERFORMANCE.md §4`) — **the live standard.** | writing/reviewing any perf-sensitive code. Do not confuse with the file below. |
| `docs/archive/PERFORMANCE_AUDIT_2026-09-20.md` | a **historical, one-off** audit report (Persian) — findings already actioned or superseded. | only if explicitly asked to review that past audit; never for current rules (use `PERFORMANCE.md`). |
| `windows-frontend-roadmap/PROGRESS.md` | authoritative per-phase log of what was built and any deviations (frontend track, complete). | you need the exact current state / history of a specific screen. |
| `windows-frontend-roadmap/CONVENTIONS.md` | folder layout, naming, MVVM & theming rules (short). | placing new files / matching conventions and you're unsure. |
| `windows-frontend-roadmap/ROADMAP.md`, `phases/`, `00-REQUIREMENTS-LOCK.md` | the original frontend build plan. | historical/planning context only — the roadmap is complete. |
| `backend-roadmap/PROGRESS.md` | current state + next phase for the backend track (see the dedicated section above). | any backend-track task — read this first, every time. |
| `backend-roadmap/phases/PHASE-<ID>-*.md` | the one self-contained instruction file for the active phase. | executing the phase `PROGRESS.md` names as "Next phase". Never another phase's file. |
| `backend-roadmap/CONVENTIONS.md`, `ROADMAP.md`, `00-REQUIREMENTS-LOCK.md` | shared engineering rules / full phase index+dependencies / locked requirements for the backend track. | the active phase file's own condensed rules aren't enough, or you're authoring/auditing a phase. |
| `Animora-UI/README.md` | what the reference is and how to treat it. | before first using `Animora-UI/` as a spec. |

## Fast task → path recipes

- **Change/fix a screen's UI or behavior** → `src/Animora.Desktop/Pages/<Area>/`. Need the visual target? open `Animora-UI/<area>.html` (+ its `assets/*` referenced there).
- **Change data shape a screen consumes** → interface+DTO in `src/Animora.Contracts/<Area>/`, then its real `src/Animora.Client.Services/<Area>/` service (+ mapping), then `App.axaml.cs` registration.
- **Add a new screen** → new `Pages/<Area>/` pair + Contracts interface + real `Animora.Client.Services` impl + DI registration + a `Shell/NavigationMenu.cs` entry.
- **Theme / color / spacing / icon change** → `src/Animora.Desktop/Resources/Theme/*.axaml`.
- **Reusable control** → `src/Animora.Desktop/Controls/` (+ style in `Resources/Controls/`).
- **Navigation / dialogs / toasts** → `src/Animora.Desktop/Services/` and `Shell/`.
- **RTL / Persian digits / Jalali dates** → `src/Animora.Desktop/Localization/`.

**After any code change:** build with `scripts/sandbox/build.sh` (must be 0/0), and smoke-run if you touched startup/DI/shell.
