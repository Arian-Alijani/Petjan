# AGENTS.md — read this first, then stop reading

Single onboarding map for AI agents. Goal: understand this repo and reach the
right file **without opening files you don't need**. Read this file fully; open
anything else only when a rule below says to.

## What this repo is (one paragraph)

**Animora** — a self-hosted **veterinary clinic management + accounting**
desktop app. Persian / RTL UI, Jalali calendar. One C# solution on **.NET 10**,
UI is **Avalonia + MVVM (CommunityToolkit.Mvvm)**. Current state: the **Windows
desktop frontend is complete** (all 17 screens built, `dotnet build` = 0/0), with
data behind `Animora.Contracts` interfaces implemented only by in-memory
`Mock*` services. No real backend/DB/sync yet — that is the next, separate track.

## The two top-level folders — which one to touch

| Folder | What it is | When you touch it |
|---|---|---|
| **`src/`** (the ".NET project") | **The real product.** All changes go here. | **Default. Almost every task edits here.** |
| **`Animora-UI/`** | Throwaway HTML/CSS/JS **design reference** (like a clickable Figma). Not the stack, not real data. | **Only when the task explicitly says to inspect/use it, or you need the exact visual/behavioral spec of a screen you are rebuilding.** Never edit it. Never copy its code. |

> If a task doesn't mention design/visual reference, **do not open `Animora-UI/`** — it wastes tokens.

## Where things live in `src/` (so you don't grep blindly)

Five projects. Only the first has code today; the rest are placeholders.

```
src/Animora.Desktop/          ← THE app. 99% of edits happen here.
  App.axaml.cs                ← DI container: every service + ViewModel is registered here.
  Program.cs                  ← entry point (rarely touched).
  Shell/                      ← MainWindow, Sidebar, Topbar, navigation, ViewLocator, PageViewModelBase.
  Pages/<Area>/               ← one folder per screen (Dashboard, Patients, Visits, ...). See map below.
  Controls/                   ← reusable custom controls (DataTable, StatCard, DonutRing, JalaliDatePicker, ...).
  Resources/Theme/            ← design tokens: Colors / Typography / Spacing / Elevation / Icons (.axaml).
  Resources/Controls/         ← XAML styles per control family.
  Services/                   ← Navigation, Dialog, Toast, RecordSheet (interface + impl pairs).
  Mocks/                      ← in-memory Mock* implementations of Contracts interfaces (all sample data lives ONLY here).
  Localization/               ← RTL, Persian digits, Jalali calendar helpers.
src/Animora.Contracts/<Area>/ ← DTOs + query/service interfaces per bounded context. The seam to the future backend.
src/Animora.Domain/           ← placeholder (csproj + README only). Do not fill unless on the backend track.
src/Animora.Application/      ← placeholder.
src/Animora.Sync.Abstractions/← ISyncEngine placeholder only.
```

**Per-screen file pattern** inside `Pages/<Area>/`:
`<Screen>View.axaml` (+ `.axaml.cs` code-behind) and `<Screen>ViewModel.cs`.
Modals/sheets/wizards are extra `*View`+`*ViewModel` pairs in the same folder.

**Bounded contexts / screens** (same names across `Pages/`, `Contracts/`, `Animora-UI/*.html`):
Dashboard, Reception, Patients, Visits, Vaccination, Appointments, Boarding,
Inventory, Purchasing, Sales, Cashier, Cheques, Finance, History, Profile.

## The one architectural rule that governs every change

Data flows: **View → ViewModel → `Animora.Contracts` interface (via constructor DI) → `Mock*` impl in `Mocks/`.**
- ViewModels depend **only** on Contracts interfaces, never on `Mock*` or concrete data.
- All sample/seed data lives **only** in `Mocks/`. Never hardcode data or add `if (design-time)` branches in `Pages/**`.
- To add a screen's data need: add/extend the interface + DTO in `Contracts/<Area>/`, implement it in `Mocks/`, register both in `App.axaml.cs`.
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
| `windows-frontend-roadmap/PROGRESS.md` | authoritative per-phase log of what was built and any deviations. | you need the exact current state / history of a specific screen. |
| `windows-frontend-roadmap/CONVENTIONS.md` | folder layout, naming, MVVM & theming rules (short). | placing new files / matching conventions and you're unsure. |
| `windows-frontend-roadmap/ROADMAP.md`, `phases/`, `00-REQUIREMENTS-LOCK.md` | the original build plan. | historical/planning context only — the roadmap is complete. |
| `Animora-UI/README.md` | what the reference is and how to treat it. | before first using `Animora-UI/` as a spec. |

## Fast task → path recipes

- **Change/fix a screen's UI or behavior** → `src/Animora.Desktop/Pages/<Area>/`. Need the visual target? open `Animora-UI/<area>.html` (+ its `assets/*` referenced there).
- **Change data shape a screen consumes** → interface+DTO in `src/Animora.Contracts/<Area>/`, then its `Mocks/Mock*Service.cs`, then `App.axaml.cs` registration.
- **Add a new screen** → new `Pages/<Area>/` pair + Contracts interface + Mock + DI registration + a `Shell/NavigationMenu.cs` entry.
- **Theme / color / spacing / icon change** → `src/Animora.Desktop/Resources/Theme/*.axaml`.
- **Reusable control** → `src/Animora.Desktop/Controls/` (+ style in `Resources/Controls/`).
- **Navigation / dialogs / toasts** → `src/Animora.Desktop/Services/` and `Shell/`.
- **RTL / Persian digits / Jalali dates** → `src/Animora.Desktop/Localization/`.

**After any code change:** build with `scripts/sandbox/build.sh` (must be 0/0), and smoke-run if you touched startup/DI/shell.
