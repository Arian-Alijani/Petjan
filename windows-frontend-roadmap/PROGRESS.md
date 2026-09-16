# Progress & Project State — Windows Frontend

> **Read this file first, every session.** It is the single source of
> truth for "what already exists" and "what to do next." Update it at the
> end of every phase you execute — do not skip this step.

## Current state

- **Next phase to execute:** `04` — RTL & Localization Utilities
- **Repo state (Phase 03):** `Animora.Desktop` now carries the shared control
  library: `Resources/Theme/Icons.axaml` (23 `StreamGeometry` icons re-drawn
  from `Animora-UI/assets/icons.js`, covering Phase 05's sidebar set + common
  actions), `Resources/Controls/IconStyle.axaml` (shared `Path.icon` look),
  `Resources/Controls/Button.axaml` (`ControlTheme` for `Button` with
  `primary|outline|ghost|danger-ghost` classes), `Resources/Controls/Card.axaml`
  (`Border.card`/`card-header`/`card-header-rule`/`card-title`/`card-body`
  selector styles). `Controls/` adds composite `UserControl`s: `StatCard`,
  `Badge` (+ shared `Tone` enum), `DataTable` (thin `DataGrid` wrapper styled
  to `.tbl`), `EmptyState`, `ToastHost`, `ModalHost`, `DrawerHost`. Services:
  `IToastService`/`ToastService` and `IDialogService`/`DialogService`
  (`Services/`), both registered as singletons in `App.axaml.cs`'s DI
  container. A temporary `Pages/_ControlGallery/ControlGalleryView`
  instantiates one of each control (4 button variants, card, 2 stat cards,
  5 badge tones, empty state, toast/modal/drawer triggers) and is now
  `MainWindow`'s DI-injected content, replacing the Phase 02 resource smoke
  test. Added `Avalonia.Controls.DataGrid` 12.1.2 package (matches the
  pinned Avalonia 12.1.2 line) to `Directory.Packages.props` +
  `Animora.Desktop.csproj`.
- **Build note:** this sandbox has no `dotnet` SDK installed (no toolchain
  available at all, unlike Phase 01's headless-X11-only limitation) — code
  was written and reviewed manually against Avalonia 12.x API shape
  (`ControlTheme`, `StyledProperty`, `DataGrid`, `StreamGeometry` mini-language)
  but **not compiled in this session**. First session with `dotnet` available
  must run `dotnet build Animora.sln` and fix any errors before starting
  Phase 04.
- **Repo state (Phase 02):** `Animora.Desktop` now carries the shared
  design-token layer under `Resources/Theme/`: `Colors.axaml` (all `:root`
  brand/surface/ink/semantic/accent tokens as `Color` + `SolidColorBrush`),
  `Spacing.axaml` (radii + 4px spacing scale), `Elevation.axaml`
  (`ShadowCard`/`ShadowPop` BoxShadows), `Typography.axaml` (bundled Vazirmatn
  `AppFontFamily` + FontSize scale). All four merged in `App.axaml`, which sets
  the app-wide default `FontFamily` via a `TopLevel` base style. `MainWindow`
  renders a themed panel as a resource smoke test. Vazirmatn is embedded as an
  `AvaloniaResource`. `dotnet build Animora.sln` = 0 warnings / 0 errors. No
  controls/navigation/screens yet.
- **Repo state:** `Animora.sln` (classic format) builds with all 5 projects on
  .NET 10 (`net10.0`, C# 14). Four placeholder class libraries
  (`Animora.Contracts`, `Animora.Domain`, `Animora.Application`,
  `Animora.Sync.Abstractions`) and the Avalonia app `Animora.Desktop`
  (WinExe) exist. `Animora.Desktop` launches an empty 1440×900 `MainWindow`
  ("Animora Desktop — Phase 01 scaffold") via a DI composition root
  (`App.Services`). Central package pins live in `Directory.Packages.props`;
  shared build settings in `Directory.Build.props`. No theming/controls/
  navigation/screens yet.
- **Roadmap status:** Phase 03 done (unverified build — see build note above). 3 of 29 phases implemented.

## Phase checklist

| # | Phase | Status | Notes |
|---|---|---|---|
| 01 | Solution & Project Scaffold | ⚠️ Done with deviation | Avalonia.Diagnostics dropped (no 12.x); Fluent theme package added (see log) |
| 02 | Design Tokens & Theme Resources | ✅ Done | Vazirmatn v33.003 variable TTF bundled (OFL) |
| 03 | Base Control Library | ⚠️ Done with deviation | Not compiled — sandbox has no dotnet SDK (see log) |
| 04 | RTL & Localization Utilities | ⬜ Not started | |
| 05 | App Shell: Sidebar, Topbar, Navigation | ⬜ Not started | |
| 06 | Dashboard Screen | ⬜ Not started | |
| 07 | Reception & Queue Screen | ⬜ Not started | |
| 08 | Patients & Owners — List/Grid | ⬜ Not started | |
| 09 | Patients & Owners — Pet File / Owner Sheet | ⬜ Not started | |
| 10 | Visits — Doctor Rail & Triage | ⬜ Not started | |
| 11 | Visits — Clinical Encounter Wizard | ⬜ Not started | |
| 12 | Vaccination Screen | ⬜ Not started | |
| 13 | Appointments — Calendar Views | ⬜ Not started | |
| 14 | Appointments — Booking Panel | ⬜ Not started | |
| 15 | Boarding — Board & List | ⬜ Not started | |
| 16 | Boarding — Stay Modals | ⬜ Not started | |
| 17 | Inventory — Product List & Settings | ⬜ Not started | |
| 18 | Inventory — Product Modal & Stock Adjust | ⬜ Not started | |
| 19 | Purchase & Suppliers Screen | ⬜ Not started | |
| 20 | Sale/POS — Invoice Workspace | ⬜ Not started | |
| 21 | Sale/POS — Catalog & Payment Modals | ⬜ Not started | |
| 22 | Cashier Screen | ⬜ Not started | |
| 23 | Cheques — List Views & Side Panels | ⬜ Not started | |
| 24 | Cheques — Add/Edit Drawer & Bulk Actions | ⬜ Not started | |
| 25 | Finance — Overview & Income Tabs | ⬜ Not started | |
| 26 | Finance — Expense & Profit Tabs | ⬜ Not started | |
| 27 | History — Unified Ledger | ⬜ Not started | |
| 28 | Profile & Settings Screen | ⬜ Not started | |
| 29 | Cross-Screen Navigation Wiring & Visual QA | ⬜ Not started | |

Status legend: ⬜ Not started · 🔄 In progress · ✅ Done · ⚠️ Done with
deviation (see log below).

## Decision / deviation log

> Append one entry per deviation from the roadmap/conventions, newest on
> top. If a phase followed the plan exactly, no entry is needed.

### Phase 03 — Base Control Library

- **Deviation — build not verified.** This session's sandbox has no
  `dotnet` SDK at all (`dotnet: command not found`), unlike Phase 01's
  headless-X11-only limitation which still allowed `dotnet build`. All
  Phase 03 code (Icons/Button/Card `ControlTheme`s + selectors, `StatCard`,
  `Badge`, `DataTable`, `EmptyState`, `ToastHost`, `ModalHost`, `DrawerHost`,
  `IToastService`/`ToastService`, `IDialogService`/`DialogService`,
  `ControlGalleryView`, DI wiring) was written and manually re-checked
  against Avalonia 12.x API shape, but **`dotnet build Animora.sln` has not
  actually been run**. The next session with a working `dotnet` SDK must
  run the build first and fix anything it flags before starting Phase 04.
- **Icon set — `StreamGeometry`, not `PathIcon`/`DrawingImage` resources.**
  Task 1 allowed any of "Geometry/PathIcon resources (or a DrawingImage
  resource per icon)"; chose plain `StreamGeometry` keyed resources
  (`Icon<Name>`) consumed via `<Path Classes="icon" Data="{StaticResource
  IconX}"/>` — simplest idiomatic option, and every consumer (StatCard,
  EmptyState, ToastHost, future screens) only needs a `Geometry`-typed
  bindable property, not a full `PathIcon` control instance.
  Icon coverage: sidebar keys `dashboard, calendar, users, clipboard,
  syringe, home, cart, box, receipt, chart, cheque, clock, shield` (all
  present) + action keys `plus, check, x, search, edit, trash, more, filter,
  chevronDown, chevronLeft, chevronRight` (all present) = 23 keys total, per
  the phase's minimum list. No additional icons were added.
- **`DataTable` uses Avalonia's `DataGrid`.** Added
  `Avalonia.Controls.DataGrid` 12.1.2 as a new central package pin (matches
  the existing Avalonia 12.1.2 line exactly) rather than an `ItemsControl`
  custom-rows alternative — Task 6 allows either; `DataGrid` is idiomatic
  and gives later screen phases sorting/virtualization for free. Only the
  styling shell is supplied (header bg + bottom rule, flat rows, hover);
  columns are left to consuming screens.
- **Card is a set of `Style` selectors (`Styles` root), not a `ControlTheme`.**
  Task 3 says "a `ControlTheme` for a `Border`-based `Card`" but a `Card` is
  not registered as a distinct control type here — `Classes="card"` is
  applied directly to a plain `Border`, so implicit-key `ControlTheme`
  lookup (which keys off `TargetType`) doesn't apply the same way it does
  for `Button`. Implemented as class selectors on `Border`/`StackPanel`/
  `TextBlock`/`ContentPresenter`/`Panel` instead, functionally equivalent
  for screen authors (`<Border Classes="card">…</Border>`).
- **`IconStyle.axaml` and `Card.axaml` are `Styles`-rooted files included via
  `StyleInclude`; `Icons.axaml`/`Button.axaml` are `ResourceDictionary`-rooted
  and included via `ResourceInclude`.** Avalonia requires unkeyed `Style`
  selectors to live under a `Styles` root (merged into
  `Application.Styles`), while keyed resources (`StreamGeometry`,
  `ControlTheme`) live under `ResourceDictionary` (merged into
  `Application.Resources`) — the phase file's own file-path suggestions
  (`Resources/Controls/*.axaml`) don't distinguish these; split by content
  instead.
- **Toast/Dialog services are attached, not fully DI-wired to a global root.**
  `ToastHost.Attach(ToastService)` and `DialogService.Attach(ModalHost,
  DrawerHost)` are called explicitly by `ControlGalleryView` because Phase 03
  has no permanent shell yet (that's Phase 05). Phase 05 should call the
  same `Attach` methods once, from the real shell's root, instead of
  per-screen.

### Phase 02 — Design Tokens & Theme Resources

- **Font asset source (logged per task 4).** Vazirmatn **v33.003** (matches the
  reference's `@import` of `vazirmatn@v33.003`), the **variable** weight file
  `fonts/variable/Vazirmatn[wght].ttf` from the official OFL-licensed repo
  `rastikerdar/vazirmatn` (via jsDelivr CDN at build time only). Bundled at
  `src/Animora.Desktop/Assets/Fonts/Vazirmatn.ttf` (renamed from the `[wght]`
  filename so the `avares://` URI has no bracket characters); `OFL.txt` kept
  alongside it. Embedded as an `AvaloniaResource` — no runtime CDN fetch, per
  STACK.md's zero-Internet-at-runtime rule. Internal family name is
  `Vazirmatn`, referenced as
  `avares://Animora.Desktop/Assets/Fonts#Vazirmatn`.
- **Note — resource style pattern.** Colors are exposed as both `Color`
  (`…Color`) and `SolidColorBrush` (`…Brush`) keys so later phases can bind
  either a brush (fills) or a raw color (gradients/animations) without
  redefining values.
- **Note — XAML comment constraint.** CSS token names like `--radius` /
  `--shadow-card` cannot appear verbatim in XAML comments (`--` is illegal
  inside an XML comment); comments paraphrase them instead.

### Phase 01 — Solution & Project Scaffold

- **Exact package versions chosen** (STACK.md doesn't pin these two):
  - `CommunityToolkit.Mvvm` = **8.4.2** (latest stable; net10.0-compatible).
  - `Microsoft.Extensions.DependencyInjection` = **10.0.12** (latest stable
    10.x line, aligned with .NET 10).
- **Deviation — `Avalonia.Diagnostics` removed.** The phase listed it as a
  Debug-only *placeholder* ("remove later if unused"). Avalonia 12.x has **no
  `Avalonia.Diagnostics` 12.x release** (the DevTools moved into the core
  `Avalonia` package; highest standalone version is 11.3.x). Pinning it to
  12.1.2 makes `dotnet restore` fail (NU1102). Dropped the package to keep an
  exact-pin, no-floating-range policy; DevTools remain available via the core
  package when a later phase wires `AttachDevTools`.
- **Deviation — `Avalonia.Themes.Fluent` 12.1.2 added.** In Avalonia 12.x the
  Fluent theme is a **separate package** (decoupled from the `Avalonia`
  metapackage); `<FluentTheme />` in `App.axaml` fails to resolve without it
  (AVLN2000). Added the pin so the app has a base theme to render the empty
  window. This is the standard base theme every later phase needs anyway.
- **Note — `Animora.sln` in classic format.** The .NET 10 SDK's
  `dotnet new sln` now emits `.slnx` by default; created with
  `--format sln` to honor the phase's explicit `Animora.sln` filename.
- **Note — launch verified indirectly.** The build sandbox is headless
  Linux, so the WinExe cannot open a window here; a `dotnet run` reaches the
  X11 platform init and fails only on `XOpenDisplay` — confirming bootstrap +
  DI + XAML load run correctly. The actual window opens on Windows (target).

## How to update this file at the end of a phase

1. Change that phase's row: `⬜ Not started` → `✅ Done` (or `⚠️ Done with
   deviation` if you deviated — add a log entry explaining why/what).
2. Update **"Next phase to execute"** to the following phase number.
3. Update **"Repo state"** with a one/two-line factual summary of what now
   exists (e.g. "`src/Animora.Desktop` builds and runs; shows Dashboard +
   Sidebar; Patients/Visits/etc. screens are still placeholders").
4. Do not rewrite history rows for already-`Done` phases.
5. Commit this file together with the phase's code changes.
