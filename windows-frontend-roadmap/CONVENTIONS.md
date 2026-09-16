# Shared Conventions — Windows Frontend (Avalonia Desktop)

> Read once per session (it is short). Every phase file assumes these
> conventions instead of restating them. If a phase needs an exception, the
> phase file says so explicitly — otherwise these rules always apply.

## 1. Solution & folder layout (created by Phase 1, then just extended)

```
Animora.sln
Directory.Build.props            # TargetFramework net10.0, LangVersion 14, Nullable enable
Directory.Packages.props         # Central package version pins (STACK.md §11) — exact pins, no floating ranges
/src
  Animora.Desktop/               # Avalonia Native app — THIS roadmap's main deliverable
    App.axaml / App.axaml.cs
    Program.cs
    /Assets                      # fonts, raster images (avatars/mascot) if any
    /Resources                   # Avalonia XAML resource dictionaries (design tokens, control styles)
      Theme/Colors.axaml
      Theme/Typography.axaml
      Theme/Spacing.axaml
      Theme/Elevation.axaml
      Theme/Icons.axaml
      Controls/Button.axaml
      Controls/Card.axaml
      Controls/Badge.axaml
      Controls/Input.axaml
      Controls/...               # one file per control family, added as needed
    /Controls                    # reusable composite Avalonia UserControls (StatCard, DataTable, FilterBar, ...)
    /Shell                       # MainWindow, Sidebar, Topbar, NavigationHost, ToastHost
    /Pages                       # one subfolder per screen/bounded-context slice, e.g. /Pages/Dashboard, /Pages/Patients
      <Area>/<Screen>View.axaml (+ .cs)
      <Area>/<Screen>ViewModel.cs
    /Services                    # Desktop-local service implementations (navigation, dialogs, toast, mock data adapters)
    /Mocks                       # In-memory design-time data implementing the Contracts interfaces (Phase 5+)
    /Localization                # RTL setup, Persian digit + Jalali calendar helpers (Phase 4)
  Animora.Contracts/             # Shared DTOs + query/service interfaces (placeholder now; consumed by future backend)
  Animora.Domain/                # Empty placeholder class library — future backend roadmap fills this in
  Animora.Application/           # Empty placeholder class library — future backend roadmap fills this in
  Animora.Sync.Abstractions/     # ISyncEngine interface placeholder only (ARCHITECTURE.md §8.1) — no implementation
/Animora-UI                      # Existing reference prototype — read-only, never modified, never copied verbatim
/windows-frontend-roadmap         # This roadmap package — read-only during execution except PROGRESS.md
```

**Rule:** UI phases only ever add files under `Animora.Desktop/**`. They may
add an *interface + DTO* to `Animora.Contracts` (never an implementation)
when a screen needs a data contract that doesn't exist yet — the mock
implementation of that interface lives in `Animora.Desktop/Mocks`. This is
what keeps the codebase backend-ready (R-18): the day the backend/sync
roadmap runs, it implements the same `Animora.Contracts` interfaces for
real and the DI registration in `Animora.Desktop/Services` swaps the mock
for the real implementation — no Desktop/View code changes.

## 2. Naming conventions

- Screens (top-level pages reachable from the sidebar): `<Name>PageView` /
  `<Name>PageViewModel` (e.g. `PatientsPageView`, `PatientsPageViewModel`).
- Composite reusable controls: `<Purpose><Kind>` (e.g. `StatCard`,
  `DataTable`, `FilterBar`, `EmptyState`, `ModalHost`, `SideDrawer`).
- Panels/drawers/dialogs specific to one screen live under that screen's
  `/Pages/<Area>/` folder, named `<Name>Panel` / `<Name>Dialog`.
- ViewModels: `CommunityToolkit.Mvvm` source-generated members —
  `[ObservableProperty]` fields are `_camelCase`, generated property is
  `PascalCase`; commands are `[RelayCommand]` methods named `Do<Action>`,
  exposed as `<Action>Command`.
- Every screen ViewModel derives from `PageViewModelBase` (Phase 5): exposes
  `Title`, `Eyebrow`/breadcrumb text, `IsBusy`.
- Mock services: `Mock<Interface-without-I>` (e.g. `IPatientsQueryService`
  → `MockPatientsQueryService`) in `Animora.Desktop/Mocks`.

## 3. MVVM pattern (per STACK.md §3.1)

- `CommunityToolkit.Mvvm` only — source-generated `[ObservableProperty]` /
  `[RelayCommand]` / `[INotifyPropertyChanged]` partial classes. No manual
  `INotifyPropertyChanged` boilerplate, no `ReactiveUI` unless a phase file
  explicitly says a screen needs it (none currently do).
- Views are Avalonia `.axaml` + code-behind limited to
  `InitializeComponent()` — no business logic in code-behind.
- Navigation: a single `INavigationService` (defined Phase 5, implemented
  Phase 6) — screens navigate via `NavigateTo<TViewModel>()`, never via
  direct `Window`/`Control` references.
- Dialogs/drawers/toasts: via `IDialogService` / `IToastService` (Phase 6/7)
  — a screen ViewModel never instantiates a `Window` directly.

## 4. Theming & visual fidelity rules (per `Animora-UI/assets/styles.css`)

- All colors, radii, shadows, spacing are **resources**, defined once in
  `Resources/Theme/*.axaml` (Phase 2) and referenced everywhere — never a
  hardcoded hex/pixel value inside a screen's `.axaml`.
- Color/radius/shadow **values** are read from `Animora-UI/assets/styles.css`
  `:root` (documented once, Phase 2) — but the Avalonia resource dictionary
  is authored from scratch as idiomatic `Color`/`SolidColorBrush`/`CornerRadius`
  resources, never by embedding CSS.
- Typography: Vazirmatn (the reference's font). Phase 2 embeds the font
  family as an Avalonia asset (licensed, self-hosted — never a runtime CDN
  fetch, per `STACK.md` R7 zero-Internet-at-runtime rule, which also governs
  the desktop UI layer, not just data sync).
- RTL: `FlowDirection="RightToLeft"` at the `Window`/root level (Phase 4);
  Persian digit formatting and Jalali calendar conversion are small pure
  utility classes in `Animora.Desktop/Localization`, written from Avalonia/
  .NET idioms — never transliterated from the reference's JS.

## 5. Data pattern for UI-only phases (no backend yet)

- Every screen ViewModel depends on an **interface** (e.g.
  `IPatientsQueryService`) injected via constructor (DI, Phase 1 sets up
  `Microsoft.Extensions.DependencyInjection` or Avalonia's built-in
  composition root — decided once in Phase 1, reused everywhere).
- The interface + its DTOs live in `Animora.Contracts`. The **mock**
  implementation (in-memory, deterministic sample data shaped like the
  reference's `*-data.js` demo data, but **not copied from it** — re-derived
  from what the screen visually needs) lives in `Animora.Desktop/Mocks` and
  is registered in the DI container at startup.
- This mock/interface seam is exactly what later lets a backend/sync phase
  swap in a real Datasync/EF-backed implementation with zero ViewModel or
  View changes (`ARCHITECTURE.md` §7.1, A-RULE 001).

## 6. Definition of Done template (every phase reuses this; phase files only list deltas)

A phase is done when **all** of the following hold:
1. `dotnet build Animora.sln` succeeds with zero errors/warnings introduced
   by this phase.
2. The app runs (`dotnet run --project src/Animora.Desktop`, or F5 in Visual
   Studio) and the phase's screen/feature is reachable and visually/
   behaviorally comparable to the referenced `Animora-UI` file(s).
3. No file under `/Animora-UI` or `/windows-frontend-roadmap` (other than
   `PROGRESS.md`) was modified.
4. No `Animora-UI` markup/script/style was copy-pasted; all new code is
   idiomatic Avalonia/C#.
5. New shared interfaces (if any) were added to `Animora.Contracts`, not
   hardcoded inside a View or a screen ViewModel.
6. `PROGRESS.md` is updated: phase marked done, "Next phase" advanced, any
   deviation/decision logged.

## 7. Token/scope discipline for execution sessions

- Read only the phase file + this file + `PROGRESS.md`, plus the exact
  `STACK.md` / `ARCHITECTURE.md` section numbers and exact `Animora-UI`
  file paths the phase file names. Do not browse the rest of the repo.
- Do not re-litigate stack/architecture decisions; implement them.
- Prefer small, focused diffs; do not refactor prior phases' code unless
  the phase file explicitly asks for it.
