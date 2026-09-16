# Progress & Project State — Windows Frontend

> **Read this file first, every session.** It is the single source of
> truth for "what already exists" and "what to do next." Update it at the
> end of every phase you execute — do not skip this step.

## Current state

- **Next phase to execute:** `06` — Dashboard Screen (role-based)
- **Repo state (Phase 05):** the real app shell now exists.
  `Animora.Contracts` gained `Navigation/NavItem.cs` (nav DTO — Key/Label/
  IconKey/ViewModelType/GroupName/BadgeCount), `Navigation/UserRole.cs`
  (`Manager|Cashier|Doctor`), and `Identity/ICurrentUserContext.cs` (name/
  role/initials seam). `Animora.Desktop/Shell/` adds `PageViewModelBase`
  (Title/Eyebrow/Breadcrumbs/IsBusy — every screen VM derives from it),
  `NavigationMenu` (static groups/items mirroring the reference's `NAV_ITEMS`
  exactly), `SidebarView(+VM)` + `NavGroupViewModel`/`NavEntryViewModel`
  (brand header, grouped nav with active highlight + left accent bar, badge
  pills, upsell CTA + sync-status + user card), `TopbarView(+VM)` (accent
  title/breadcrumbs bound to the current page, visual-only search, quick-
  create, inert bell, user chip), `PlaceholderPageView(+VM)` (single shared
  "not built yet" page every nav item currently targets), `ViewLocator`
  (convention-based VM→View resolution, registered as an app `DataTemplate`),
  `IconKeyConverter` (icon-key string → Geometry, app resource), and
  `MainWindow(+VM)` (Grid: SidebarView column + Topbar-over-ContentControl
  column). `Animora.Desktop/Services/` adds `INavigationService`/
  `NavigationService` (DI-resolved `NavigateTo<T>()`, observable
  `CurrentViewModel`). `Animora.Desktop/Mocks/MockCurrentUserContext`
  returns the reference's default `manager` identity. DI wires the identity/
  navigation/shell singletons; `MainWindow` no longer hosts the Phase 03
  control gallery (kept on disk, dev-only, unreferenced). Every sidebar item
  routes to the placeholder without error, active-highlight tracks the click,
  startup lands on the "داشبورد" slot. `scripts/sandbox/build.sh full` =
  0 warnings / 0 errors; `scripts/sandbox/run.sh` = SMOKE RUN OK.
- **Build verification (Phase 04 session):** installed the real **.NET 10 SDK
  (10.0.401)** and ran `dotnet restore` + `dotnet build Animora.sln` — now
  **0 warnings / 0 errors** across all 5 projects. This was the first session
  with a working `dotnet` toolchain since Phase 01, so it retroactively
  verified the previously-unbuilt Phase 03 code. Two real defects surfaced and
  were fixed (see the Phase 03 deviation log): a `ToastService`
  `TryFindResource` compile error, and the two `AVLN3001` runtime-loader
  warnings on the DI-constructed `MainWindow`/`ControlGalleryView`. Headless
  `dotnet run` reaches Avalonia bootstrap and stops only at `XOpenDisplay`
  (no display in this Linux sandbox) — bootstrap + DI + XAML/resource load all
  succeed; the window itself opens on the Windows target.
- **Repo state (Phase 04):** `Animora.Desktop/Localization/` now holds the shared
  RTL + localization utilities: `PersianMonthNames.cs` (12 Jalali month
  constants فروردین..اسفند as a `static string[]` + `Get(month)`),
  `PersianNumberFormatter.cs` (pure `ToPersianDigits(object?)` and
  `FormatGrouped(long)`/`GroupAscii(long)` — re-derived from `toFa` and
  `group()` in .NET string idioms, not ported regex), `JalaliDate.cs`
  (`readonly struct { Year, Month, Day }` with `FromDateTime`/`ToDateTime`/
  `Today` backed by `System.Globalization.PersianCalendar`, and
  `ToString("long"|"medium"|"short")` producing e.g. "۱۴ مهر ۱۴۰۴"), and
  `Converters.cs` (`PersianDigitsConverter` + `JalaliDateConverter`,
  one-way `IValueConverter`s). `App.axaml` sets `FlowDirection="RightToLeft"`
  once on the `TopLevel` base style (single top-level RTL location per
  CONVENTIONS.md §4) and registers both converters as
  `PersianDigitsConverter`/`JalaliDateConverter` resource keys. The temporary
  control gallery gained a "محلی‌سازی — فاز ۰۴" card with two proof
  `TextBlock`s (grouped Persian number + today's Jalali date) bound through the
  converters. `dotnet build Animora.sln` = 0 warnings / 0 errors; conversion
  spot-checked (2025-10-06 → ۱۴ مهر ۱۴۰۴).
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
- **Build note (RESOLVED):** Phase 03 was originally written without a `dotnet`
  SDK in the sandbox and shipped uncompiled. A later session installed the
  .NET 10 SDK and compiled the solution: `dotnet build Animora.sln` now reports
  **0 warnings / 0 errors**. The two defects the missing build had hidden
  (`ToastService.TryFindResource` and the `AVLN3001` warnings) are fixed. No
  outstanding build blocker before Phase 04.
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
- **Roadmap status:** Phases 01–04 done and **build-verified** (0/0). 4 of 29
  phases implemented.

## Phase checklist

| # | Phase | Status | Notes |
|---|---|---|---|
| 01 | Solution & Project Scaffold | ✅ Done | Build-verified (0/0). Avalonia.Diagnostics/Fluent package deltas are forced by Avalonia 12.x packaging, not defects (see log) |
| 02 | Design Tokens & Theme Resources | ✅ Done | Vazirmatn v33.003 variable TTF bundled (OFL) |
| 03 | Base Control Library | ✅ Done | Build-verified (0/0); TryFindResource + AVLN3001 defects fixed (see log) |
| 04 | RTL & Localization Utilities | ⚠️ Done with deviation | Build-verified (0/0). Persian-digit codepoint follows the actual reference `toFa` (U+06Fx), not the phase text's U+066x example — see log |
| 05 | App Shell: Sidebar, Topbar, Navigation | ⚠️ Done with deviation | Build-verified (0/0) + smoke run OK. Notification bell reuses IconClock (no bell icon in the set); see log |
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

### Phase 05 — App Shell: Sidebar, Topbar, Navigation

- **Notification bell reuses `IconClock`.** Phase 03's icon set (23 keys) has
  no dedicated "bell" geometry, and CONVENTIONS.md §7 scopes this phase to
  not re-open earlier phases. The topbar bell is a visual/inert stub anyway
  (Task 7), so it borrows `IconClock` + the red dot for the visual demo. A
  later phase that wires real notifications can add a proper bell icon.
- **`NavItem.ViewModelType` is a `System.Type`, targets resolved via DI.**
  Task 1 listed "Href/ViewModelType"; since this is a native MVVM client (no
  URLs), the DTO carries the `PageViewModelBase`-derived `Type` and
  `INavigationService.NavigateTo(Type)` resolves the instance from the
  container — the idiomatic Avalonia navigation seam per CONVENTIONS.md §3.
- **Single shared `PlaceholderPageViewModel` (registered singleton).** Every
  nav item targets the same placeholder instance; the sidebar calls
  `ConfigureFor(label)` on navigation so the topbar title/breadcrumb/content
  reflect the clicked item. Each screen phase (06+) registers its own page VM
  and swaps that one item's `NavigationMenu` target.
- **View resolution via a convention `ViewLocator` `IDataTemplate`.** Instead
  of per-VM `DataTemplate`s, an app-level `ViewLocator` maps `…ViewModel` →
  `…View` by name and constructs the View parameterless (DataContext = the
  injected VM). This is the standard Avalonia MVVM pattern and keeps Views
  DI-free while ViewModels stay DI-injected.
- **`MainWindowViewModel` mirrors `INavigationService.CurrentViewModel`.** The
  content `ContentControl` binds to a `CurrentPage` observable on the shell VM
  (updated from `CurrentViewModelChanged`) rather than to the interface
  property directly, because compiled bindings observe change notification off
  the concrete VM, not the notification-free service interface.
- **Nav command binding uses `$parent[UserControl]`.** The nav `Button` lives
  two `ItemsControl` levels deep (groups → entries); its `NavigateCommand`
  binds through `$parent[UserControl]`'s `SidebarViewModel` DataContext, since
  the nearest `ItemsControl` ancestor's DataContext is the `NavGroupViewModel`.
- **Phase 03 control gallery left on disk, unreferenced.** `MainWindow` now
  hosts the real shell; `Pages/_ControlGallery/*` is no longer registered in
  DI or shown, but kept as a dev-only reference (deleting it was out of this
  phase's scope).

### Phase 04 — RTL & Localization Utilities

- **Persian-digit codepoint follows the reference behavior, not the phase
  text's example.** Task 2 wrote the target digits as "٠١٢٣٤٥٦٧٨٩"
  (Arabic-Indic, U+0660..U+0669), but the actual `toFa` reference it points to
  (`Animora-UI/assets/layout.js`) maps to "۰۱۲۳۴۵۶۷۸۹" (Persian / Extended
  Arabic-Indic, U+06F0..U+06F9), and every other reference helper (`window.fa`,
  `J.long`) uses the same U+06Fx set. Since the phase explicitly says "matching
  `toFa`'s behavior," `PersianNumberFormatter.ToPersianDigits` emits U+06Fx —
  the correct Persian digits users expect — rather than the example's
  Arabic-Indic glyphs.
- **Thousands separator = U+066C (٬), matching `group()`.** `FormatGrouped`
  inserts U+066C exactly as the reference's `group()` regex does; grouping is
  computed with plain .NET string math (modulo-3 first group), not a ported JS
  regex.
- **`JalaliDate.ToString` supports three formats.** Beyond the required
  "long" shape ("۱۴ مهر ۱۴۰۴"), it also accepts "medium" ("۱۴ مهر") and
  "short" ("۱۴۰۴/۰۷/۱۴"), mirroring the reference's `J.long`/`J.medium`
  plus a numeric form later phases (e.g. Phase 13/23 pickers) will want.
  Default (null/empty) = "long".
- **Converters are one-way + also exposed as `.Instance` singletons.**
  `PersianDigitsConverter`/`JalaliDateConverter` throw on `ConvertBack`
  (display-only) and are registered both as `App.axaml` resource keys (for
  `{StaticResource ...}` bindings, per Task 5) and as static `Instance`
  fields (for `x:Static`/code use). `PersianDigitsConverter` honors a
  `ConverterParameter=grouped` to switch integral values to the grouped form;
  `JalaliDateConverter` takes the format name ("long"/"medium"/"short") as its
  parameter.
- **Gallery proof needed `x:DataType`.** Adding `{Binding}` proof `TextBlock`s
  to `ControlGalleryView` triggered `AVLN2100` (compiled bindings require an
  explicit data type); added `x:DataType="gallery:ControlGalleryView"` and set
  `DataContext = this` in code-behind so the two proof bindings compile. This
  is a temporary dev-only view (Phase 05 replaces MainWindow's content).

### Phase 03 — Base Control Library

- **RESOLVED — build now verified; two hidden defects fixed.** A later
  session installed the .NET 10 SDK (10.0.401) and actually compiled the
  solution. `dotnet build Animora.sln` is now **0 warnings / 0 errors**. The
  previously-unverified build had hidden two real defects, both now fixed:
  1. **`ToastService.TryFindResource` compile error.**
     `Services/ToastService.cs` called `Application.Current?.TryFindResource(
     iconKey, out var iconResource)` but only imported `Avalonia`,
     `Avalonia.Media`, `Avalonia.Threading`. In Avalonia, `TryFindResource` is
     an **extension method on `IResourceHost`** defined in
     `Avalonia.Controls.ResourceNodeExtensions`; without `using
     Avalonia.Controls;` the compiler reported *"'Application' does not
     contain a definition for 'TryFindResource'"* (this is exactly the
     Windows-side error the user hit). Fix: added `using Avalonia.Controls;`
     and rewrote the lookup as a guarded `if (Application.Current is { } app &&
     app.TryFindResource(iconKey, out var iconResource))` so the bool return
     value is actually used and the nullable `out` is handled. `Application`
     implements `IResourceHost`, and the icon `StreamGeometry`s live in
     `Application.Resources` (merged from `Resources/Theme/Icons.axaml`), so the
     app-level lookup resolves correctly.
  2. **`AVLN3001` runtime-loader warnings.** `MainWindow` and
     `ControlGalleryView` only have DI (constructor-injected) constructors and
     no public parameterless constructor, so Avalonia warned they *"won't be
     reachable via runtime loader."* This is expected for a DI-first app —
     these views are always created through the DI container, never via the
     runtime XAML loader. Rather than add fake parameterless constructors that
     would break the DI contract, suppressed `AVLN3001` in
     `Animora.Desktop.csproj` (`<NoWarn>$(NoWarn);AVLN3001</NoWarn>`) with an
     explanatory comment, restoring the Phase 02 "0 warnings / 0 errors" bar.
  Headless `dotnet run` still stops at `XOpenDisplay` (no display in the Linux
  sandbox), which only proves the window can't render here; bootstrap, DI and
  XAML/resource load all execute. The original "run the build and fix anything
  it flags before Phase 04" action item is now closed.
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

- **Status upgraded to ✅ Done (build-verified).** The Phase 01 "deviations"
  below (`Avalonia.Diagnostics` dropped, `Avalonia.Themes.Fluent` added,
  classic `.sln` format) are all **forced by Avalonia 12.x packaging or by the
  explicit `Animora.sln` filename requirement**, not implementation defects —
  the scaffold builds exactly as intended. Retained here as rationale, but they
  no longer warrant a ⚠️ status now that `dotnet build Animora.sln` passes
  0/0 on the real .NET 10 SDK.
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
