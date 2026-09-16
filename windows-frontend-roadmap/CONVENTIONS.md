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
- **The reference CSS is authoritative for every measurable visual value.**
  A screen's exact paddings, gaps, corner radii, border widths/colors, font
  sizes/weights, line-heights, column/row template sizes, min/max widths and
  heights, background/gradient fills, shadow offsets/blur, and the number and
  order of every box come from that screen's own `.css` file(s) — not from
  the phase author's judgement, and not approximated. When a screen phase
  runs, it **must** read the CSS file(s) that screen's `.html` `<link>`s
  (§8 lists them per phase) and transcribe those exact values into the view,
  reusing the Phase 02 tokens where a value equals a token and adding a new
  token when a repeated literal has no token yet. "Looks about right" is a
  defect, not a pass — see §8.

## 4a. Fidelity Protocol — the whole point of this roadmap (MANDATORY, read every phase)

The single goal of this roadmap is that a professional designer, shown the
rebuilt Avalonia screen next to its `Animora-UI` reference, **cannot tell
which is which**. Every screen phase (06–28) and the QA phase (29) is bound by
the following non-negotiable rules. They override any softer wording that may
survive elsewhere in a phase file:

1. **Rebuild every element — nothing is dropped, simplified, merged, or
   "summarized."** Every card, panel, header, sub-header, stat/KPI box,
   chart, donut/pie, gauge, progress ring, sparkline, badge/pill, tab,
   sub-nav, table (with the exact same columns in the exact same order),
   list row, avatar, icon, button, chip, empty-state, tooltip, divider and
   caption that the reference screen renders **must exist** in the rebuilt
   screen, in the same place, same order, same grouping, same count. If the
   reference shows a revenue chart, a species donut and a live countdown
   ring, the rebuild has a revenue chart, a species donut and a live
   countdown ring — not a substitute list, not a placeholder, not "omitted
   for budget."
2. **No phase may declare a visible reference element "out of scope,"
   "nice-to-have," "optional," or defer it to a later phase** unless that
   element is *explicitly* the deliverable of another already-listed phase
   (e.g. a modal that its own Part-2 phase owns). Splitting a screen across
   two phases splits *which elements* each phase builds — it never licenses
   dropping an element from the screen entirely. Charts/donuts/gauges/rings
   are first-class deliverables of the phase whose screen shows them, exactly
   like tables and cards; they are implemented with Avalonia drawing
   primitives (`Path`/`PathGeometry`, `Polyline`, arcs via `ArcSegment`,
   `LinearGradientBrush`, etc.), never skipped.
3. **Exact measurable values, not eyeballed ones.** Transcribe the real
   numbers from the screen's CSS (see rule in §4). Two boxes that are equal
   width in the reference are equal width in the rebuild; a 12px gap is 12px,
   not 8 or 16; a 16px radius is 16px. Star-sizing/`Grid` templates must
   reproduce the reference's proportional column/row split (e.g. a
   `grid-template-columns: 2fr 1fr` becomes `ColumnDefinitions="2*,*"`, a
   fixed `320px` rail becomes a fixed `320` column — never re-guessed).
4. **All text is the reference's exact Persian text.** Every label, title,
   subtitle, column header, button caption, empty-state message, tab name
   and helper line is copied character-for-character (including wording,
   spacing and punctuation) from the reference — only the *code* is rewritten,
   never the user-facing copy. Numbers render through the Phase 04 Persian
   digit / grouping / Jalali helpers so they read identically to the
   reference.
5. **Same colors/tones per state.** A row/badge/pill that is danger-red in
   the reference is the danger token in the rebuild; ok/warn/info/muted tones
   map 1:1 to the same Phase 02 semantic tokens. Never invent a tone or
   recolor a state.
6. **Static-but-exact beats animated-but-wrong, but do not delete the
   thing.** Live counters, `requestAnimationFrame` easing, and real-time
   clocks may render at their final/current value instead of animating (that
   is an acceptable simplification of *motion only*) — but the counter, the
   clock face, the ring and its filled arc must still be present and show the
   correct value. Removing the element is never an acceptable substitute for
   skipping its animation.
7. **When the reference is ambiguous, read more of the reference — never
   invent.** If a value or behavior isn't clear from the CSS/JS the phase
   points at, read the exact adjacent CSS/JS rule that defines it. Only if it
   is genuinely undefined in the reference may a phase choose a value, and it
   must then log that choice in `PROGRESS.md`'s deviation log with the reason.
8. **Verify visually before marking done (see §6 step 7).** A phase is not
   done because it builds; it is done when its screen has been rendered and
   compared element-by-element against the reference screenshot and matches.

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
   Studio) and the phase's screen/feature is reachable.
3. No file under `/Animora-UI` or `/windows-frontend-roadmap` (other than
   `PROGRESS.md`) was modified.
4. No `Animora-UI` markup/script/style was copy-pasted; all new code is
   idiomatic Avalonia/C#. (User-facing Persian *text/copy* is reproduced
   exactly, per §4a rule 4 — that is not "copying code," it is matching the
   design.)
5. New shared interfaces (if any) were added to `Animora.Contracts`, not
   hardcoded inside a View or a screen ViewModel. The ViewModel binds only to
   the interface; the mock is the only thing that would be swapped for a real
   backend implementation later (backend-ready seam, R-18) — no mock call,
   sample datum, or `if (design-time)` branch leaks into a View or into the
   ViewModel's public shape.
6. **Fidelity gate (§4a) — mandatory for every screen phase 06–28 and for 29:**
   - Every visible element the reference screen renders exists in the rebuild,
     in the same order/grouping/count — nothing dropped, merged, simplified,
     or deferred (§4a rules 1–2). This explicitly includes charts, donuts,
     gauges, progress rings and sparklines.
   - Every measurable value (padding, gap, radius, border, font size/weight,
     line-height, column/row template, fixed/min/max sizes, fills, shadows)
     matches the screen's own `.css` file(s) (§4a rule 3), via Phase 02 tokens
     where applicable.
   - All user-facing Persian text matches the reference character-for-character;
     all numbers/dates go through the Phase 04 helpers (§4a rules 4).
   - State colors/tones map 1:1 to Phase 02 semantic tokens (§4a rule 5).
7. **Visual verification performed and recorded.** Before marking the phase
   done, render the screen (see §7a for how in this sandbox) and compare it
   **element-by-element** against the reference screenshot of the same screen.
   Note in `PROGRESS.md` that the visual comparison was done and list any
   value that is intentionally different (with the reference number and the
   reason). "Builds and runs" alone is **not** sufficient to mark a screen
   phase done.
8. `PROGRESS.md` is updated: phase marked done, "Next phase" advanced, any
   deviation/decision logged, and the §6.7 visual-verification note added.

## 7. Token/scope discipline for execution sessions

- Read only the phase file + this file + `PROGRESS.md`, plus the exact
  `STACK.md` / `ARCHITECTURE.md` section numbers and exact `Animora-UI`
  file paths the phase file names. Do not browse the rest of the repo.
- **The `.css` file(s) a screen phase names in "Read before starting" are
  mandatory reading, not optional context** — they carry the exact measurable
  values the Fidelity Protocol (§4a) requires. A screen phase executed without
  reading its CSS cannot satisfy §6.6 and must not be marked done. (§8 is the
  canonical per-phase CSS map; the phase file repeats its own entry.)
- Do not re-litigate stack/architecture decisions; implement them.
- Prefer small, focused diffs; do not refactor prior phases' code unless
  the phase file explicitly asks for it.

## 7a. How to render & visually verify a screen in this sandbox

The desktop app targets Windows, and this Linux sandbox is headless — but the
Fidelity Protocol (§4a) still requires a real visual check before a screen
phase is marked done. Use whichever of these is available; do not skip the
check:

1. **Preferred — render the Avalonia screen.** Build, then run under a virtual
   display so the window actually paints, and capture it:
   `scripts/sandbox/build.sh full` then run the app under `xvfb-run` and grab a
   screenshot of the window (e.g. `xvfb-run -a dotnet run --project
   src/Animora.Desktop` with a screenshot/`import`/`scrot` capture, or
   Avalonia's headless-with-rendering test harness if wired). Compare that
   capture to the reference (step 3).
2. **Fallback — if no rendering is possible at all in the session**, still do a
   *structural* comparison: enumerate every element in the reference screenshot
   and confirm the rebuilt `.axaml` contains a matching element with the same
   text, order and token values. Record in `PROGRESS.md` that only a structural
   (not pixel) check was possible and why.
3. **Get the reference image.** Render the reference screen itself for
   side-by-side comparison: serve `Animora-UI/` with a static server
   (`python -m http.server 8080` from that folder) and screenshot the page, or
   reuse `Animora-UI/shot_role.py` / `shot_tabs.py` (the prototype's own
   Playwright screenshot scripts) which already target `:8080`. Compare
   element-by-element against your rebuild capture.

The check is done when you can point at each reference element and its rebuilt
counterpart and confirm same position, size, spacing, color and text.

## 8. Per-phase reference CSS map (authoritative — the phase file repeats its row)

Every screen phase MUST read `styles.css` (the shared base: layout grid,
`.page`, `.hero`, `.card`, `.btn`, `.stat`, `.tbl`, `.badge`, pills, modal/
drawer, toast) **plus** its screen-specific `.css` below, restricted to the
sections that screen uses. These are the files that screen's `.html` actually
`<link>`s — they are the source of truth for §4a rule 3.

| Phase(s) | Screen | Required CSS (read the sections the screen uses) |
|---|---|---|
| 06 | Dashboard | `styles.css` + `dashboard.css` |
| 07 | Reception | `styles.css` (reception rules live in the shared file) |
| 08, 09 | Patients & Owners | `styles.css` + `patients.css` |
| 10, 11 | Visits | `styles.css` (visit/clinic rules live in the shared file) |
| 12 | Vaccination | `styles.css` + `vaccination.css` |
| 13, 14 | Appointments | `styles.css` + `appointments.css` + `booking.css` |
| 15, 16 | Boarding | `styles.css` + `boarding.css` |
| 17, 18 | Inventory | `styles.css` + `inventory.css` |
| 19 | Purchase | `styles.css` (static markup, no screen-specific CSS) |
| 20, 21 | Sale/POS | `styles.css` + `sale.css` |
| 22 | Cashier | `styles.css` (cashier rules live in the shared file) |
| 23, 24 | Cheques | `styles.css` + `cheques.css` |
| 25, 26 | Finance | `styles.css` + `finance.css` |
| 27 | History | `styles.css` + `history.css` |
| 28 | Profile & Settings | `styles.css` + `profile.css` + `appointments.css` |

> If, while implementing, you find a visual value that none of the listed
> files defines, the value is inherited from `styles.css` — search it there
> before choosing your own. Only a value genuinely absent from all listed CSS
> may be author-chosen, and only with a `PROGRESS.md` deviation-log entry.
