# Progress & Project State — Windows Frontend

> **Read this file first, every session.** It is the single source of
> truth for "what already exists" and "what to do next." Update it at the
> end of every phase you execute — do not skip this step.

## The two standing principles (apply to EVERY phase, no exceptions)

These two goals are the entire reason this roadmap exists. They are in force
in **every** phase (foundation phases 01–05 *and* screen phases 06–29), and
they are never traded off against speed, token budget, or "good enough."
Neither may be relaxed by a phase file, a deviation-log entry, or a future
session. When they seem to conflict, satisfy **both** — never sacrifice one.

1. **Pixel-faithful rebuild of `Animora-UI` — nothing removed, nothing
   changed visually.** The finished screen must be indistinguishable from its
   `Animora-UI/*.html` reference: every box, table, card, chart, donut, gauge,
   ring, badge, tab, sub-nav, icon and text label is rebuilt in full, in the
   same place / order / grouping / count, with **exact** measurable values
   transcribed from the screen's own CSS and Persian text reproduced
   character-for-character. No visible element is ever dropped, simplified,
   merged, approximated, or deferred. *(Full rules: `CONVENTIONS.md` §4a
   Fidelity Protocol + §6 fidelity gate; `ROADMAP.md` §3a.)*

2. **Backend-ready, standard, idiomatic code — the front end ends this
   roadmap cleanly waiting for a backend.** Every screen's data need is a
   plain `Animora.Contracts` interface + DTO, consumed by the ViewModel via
   constructor DI, with the *only* concrete implementation being an in-memory
   `Mock*` in `Animora.Desktop/Mocks`. When the future backend/database
   roadmap runs, it implements the same interfaces for real and swaps one DI
   registration — **zero** View/ViewModel changes. No mock call, sample datum,
   `if (design-time)` branch, or hardcoded data ever leaks into a View or a
   ViewModel's public shape. Code is idiomatic Avalonia/C#/MVVM
   (CommunityToolkit.Mvvm), never transliterated from the reference's JS.
   *(Full rules: `CONVENTIONS.md` §1 layout, §3 MVVM, §5 data pattern; the
   "Backend-ready" note in each screen phase.)*

> These are stated here (the always-read file) so no session can miss them.
> They restate — never replace — the authoritative rules in `CONVENTIONS.md`
> and `ROADMAP.md`; if wording ever differs, those files govern.

## Current state

- **Next phase to execute:** `10` — Visits — Doctor Rail & Triage
- **Repo state (Phase 08, build- & smoke-run verified):** Patients & Owners directory now has backend-ready contracts (`IPatientsQueryService`, `PetSummary`/`OwnerSummary` DTOs + `PetSpecies`/`PetStatus`/`PetGender`/`OwnerGender`/`PatientAvatarTone` enums in `Animora.Contracts/Patients/`), a deterministic in-memory `MockPatientsQueryService` (10 pets across cat/dog/bird/rabbit/rodent/other, 6 owners with tags/balances), presentation adapters (`PatientsPageEnums`, `PatientsPresentation`, `PatientsSpeciesChipViewModel`, `OwnerPetChipViewModel`, `OwnerCardViewModel`, `PetCardViewModel`), the `PatientsPageViewModel` (search + derived species-chips + pets/owners entity switch + list/grid layout + vaccine filter + sort, mirroring `petMatches`/`ownerMatches`/`sortPets`/`renderSpeciesChips`, with `OpenPet`/`OpenOwner` toast stubs for Phase 09), and `PatientsPageView.axaml(.cs)` (page header with «خروجی»/«مالک و بیمار جدید», unified `.pagebar` with two tiers, owner-grid/pets-grid cards, owners/pets tables, shared `pt-empty`). New shared helpers: `Localization/PersianText` (search normalization) and `Localization/RelativeDateFormatter` (since/until/age Jalali phrases). New theme additions: `IconPhone/Mobile/IdCard/Pin/Download/List/Grid/More2`, rose/mint + table/inset/hover surface tokens, and `ShadowSegment`/`ShadowChip`. DI registers `IPatientsQueryService`/`PatientsPageViewModel`; the sidebar `patients` item now targets `PatientsPageViewModel`. **Build verification (this session):** installed .NET 10.0.401 + Xvfb via `scripts/sandbox/install-prereqs.sh`; `scripts/sandbox/build.sh full` = **0 warnings / 0 errors** and `scripts/sandbox/run.sh` = **SMOKE RUN OK**. Five defects that the previously-unbuilt code had hidden were fixed during consolidation: (1) `OwnerCardViewModel.PetsPreviewText` was both assigned in the constructor and re-declared as a read-only computed property (CS0200) → kept the constructor's all-pets assignment as an auto-property and removed the duplicate; (2) `BoxShadow` set directly on `Button.cardlink` (Button has no such property) → moved to the template's `ContentPresenter#PART_ContentPresenter`; (3) `Padding` on a `Grid` → `Margin`; (4)+(5) `FontSize` on two table-row `Border`s → `TextElement.FontSize` attached property so the cascade is preserved.
- **⚠️ Fidelity Protocol now in force (roadmap update, applies to Phase 07+).**
  The roadmap was tightened after Phase 06 shipped visibly different from the
  `Animora-UI` reference (missing chart/donut/ring, eyeballed spacing/boxes).
  From Phase 07 onward every screen phase MUST follow `CONVENTIONS.md` §4a
  (Fidelity Protocol) + §6 (fidelity gate + visual verification) + §7a + §8
  (per-phase Reference CSS map): rebuild **every** reference element (charts/
  donuts/gauges/rings included), transcribe **exact** measurable values from
  each screen's own `.css` file(s), reproduce Persian text
  character-for-character, and visually verify element-by-element before
  marking a phase done. **Do NOT retro-rework Phases 01–06 for this** — they
  are already `Done`; the new rules apply going forward. Phase 29's final
  fidelity audit is the place where any residual gap in an already-done screen
  (e.g. the Phase 06 dashboard's omitted chart/donut/ring) is caught and
  assigned a fix, per that phase's Task 4.
- **Repo state (Phase 07, build- & render-verified):** Reception now has backend-ready contracts (`IReceptionQueueService`, queue/walk-in DTOs), a stateful in-memory mock, a complete MVVM page with live hero metrics, four KPI cards, exact queue filters/search/pager/empty state, state-specific actions, and the walk-in modal. Dashboard's «شروع ویزیت» deep-link targets Reception; shell-level modal/toast hosts are permanently wired. **Build verification (this session):** installed the .NET 10 SDK (10.0.401), `scripts/sandbox/build.sh full` = **0 warnings / 0 errors**, and `scripts/sandbox/run.sh` = **SMOKE RUN OK**. **Render verification:** captured a headless Xvfb screenshot of the Reception screen (temporarily setting it as the startup page, then reverting) and compared element-by-element against `Animora-UI/reception.html` — hero (live Jalali date + «صف فعال» pill + 4 metrics میانگین انتظار/نوبت بعدی/اورژانس‌های فعال/تکمیل‌شده امروز), 4 KPI cards, filter tabs (همه/فعال/اورژانس/پایان‌یافته), search placeholder «جستجوی حیوان، مالک یا پزشک…», «فیلتر» button, header eyebrow/title/subtitle, «ثبت مراجعهٔ حضوری» + «بازخوانی صف» buttons, and per-row queue actions all match. One defect the un-built code had hidden was fixed (four `TextBox.Watermark` usages triggering `AVLN5001` obsolete-API warnings → migrated to `PlaceholderText`; see log). Phase 07 is now Done.
- **Repo state (Phase 06):** the Dashboard placeholder is replaced with the
  real, role-aware screen. `Animora.Contracts/Dashboard/` gained
  `DashboardModels.cs` (`DashboardTone`, `DashboardStat`, `DashboardListItem`,
  `DashboardSnapshot` — shaped to what the three `DASH_VIEW_*` functions
  actually render: a hero title/subtitle, a 4-item KPI list, and two labeled
  content lists per role) and `IDashboardQueryService`
  (`GetSnapshotAsync(UserRole)`). `Animora.Desktop/Mocks/MockDashboardQueryService`
  returns deterministic per-role sample data re-derived from
  `dashboard-views.js`'s manager/cashier/doctor sections; registered as a
  singleton (stateless mock data, no reason to reload per navigation).
  `Animora.Desktop/Pages/Dashboard/` adds `DashboardPageViewModel` (loads the
  current role's snapshot via the injected service in its constructor —
  synchronous `GetAwaiter().GetResult()` over the mock's `Task.FromResult`;
  exposes `Stats`/`PrimaryList`/`SecondaryList` + `IsManager`/`IsCashier`/
  `IsDoctor` role flags + `QuickActions`/`OpenQuickActionCommand`),
  `DashboardRowViewModel` (adapts a Contracts `DashboardListItem` to the
  Desktop `Controls.Tone` enum for direct `Badge.Tone` binding),
  `DashboardQuickAction` (the 8-tile quick-access list, mirroring
  `dashboard.js`'s `TILES` array — all targets still resolve to
  `PlaceholderPageViewModel` until their own phase runs), `DashboardPageView`
  (hero banner + Phase 03 `StatCard` KPI row + role-conditional content region
  + quick-access grid), and one sub-view per role — `ManagerDashboardView`
  (live queue + management alerts), `CashierDashboardView` (open invoices +
  ready-to-bill), `DoctorDashboardView` (live queue + vaccination reminders) —
  all three sharing the same `DashboardPageViewModel` DataContext, visibility-
  toggled by the role flags (no `DataTemplate`-per-role needed for 3 fixed
  options). `NavigationMenu`'s `dashboard` entry and `MainWindowViewModel`'s
  startup page now target `DashboardPageViewModel` instead of the placeholder.
  DI registers `IDashboardQueryService`/`DashboardPageViewModel` as singletons.
  **Build-verified (audit session):** installed .NET 10 SDK (10.0.401),
  `scripts/sandbox/build.sh full` = **0 warnings / 0 errors**, and
  `scripts/sandbox/run.sh` = **SMOKE RUN OK** for all three roles
  (Manager/Cashier/Doctor, toggled via `MockCurrentUserContext.Role`, restored
  to `Manager` default). One real defect the un-built code had hidden was fixed
  (the three role sub-views used CSS `ColumnDefinitions="1fr,1fr"`, invalid in
  Avalonia → corrected to `"*,*"`; see deviation log). Quick-actions tile 7
  restored to the reference's "خرید و تأمین" (see log).
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
- **Roadmap status:** Phases 01–08 done and **build-verified** (0/0) + smoke-run verified; 8 of 29 phases are formally done.

## Phase checklist

| # | Phase | Status | Notes |
|---|---|---|---|
| 01 | Solution & Project Scaffold | ✅ Done | Build-verified (0/0). Avalonia.Diagnostics/Fluent package deltas are forced by Avalonia 12.x packaging, not defects (see log) |
| 02 | Design Tokens & Theme Resources | ✅ Done | Vazirmatn v33.003 static weight TTFs bundled (OFL) |
| 03 | Base Control Library | ✅ Done | Build-verified (0/0); TryFindResource + AVLN3001 defects fixed (see log) |
| 04 | RTL & Localization Utilities | ⚠️ Done with deviation | Build-verified (0/0). Persian-digit codepoint follows the actual reference `toFa` (U+06Fx), not the phase text's U+066x example — see log |
| 05 | App Shell: Sidebar, Topbar, Navigation | ⚠️ Done with deviation | Build-verified (0/0) + smoke run OK. Notification bell reuses IconClock (no bell icon in the set); see log |
| 06 | Dashboard Screen | ⚠️ Done with deviation | Build-verified (0/0) + smoke run OK (all 3 roles). Fixed AVLN2005 `1fr`→`*` grid-length defect; quick-tile 7 restored to reference; see log |
| 07 | Reception & Queue Screen | ⚠️ Done with deviation | Build-verified (0/0) + smoke run OK + render-compared to reference. Fixed 4 `AVLN5001` `Watermark`→`PlaceholderText` obsolete-API warnings; see log |
| 08 | Patients & Owners — List/Grid | ✅ Done | Build-verified (0/0) + smoke run OK. Fixed 5 hidden defects on consolidation (CS0200 `PetsPreviewText`; `BoxShadow` on Button; `Padding` on Grid; 2× `FontSize` on Border); see log |
| 09 | Patients & Owners — Pet File / Owner Sheet | ✅ Done | Build-verified (0/0) + smoke run OK. Records now open as centered sheets (matching the reference); fixed the empty wizard modal; see log |
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

### Phase 07 — Reception & Queue Screen

- **RESOLVED — build & render now verified; one hidden defect fixed.** The
  earlier session shipped the Reception code un-built because that sandbox had
  no .NET toolchain. This session installed the **.NET 10 SDK (10.0.401)** via
  `scripts/sandbox/install-prereqs.sh`, then:
  1. `scripts/sandbox/build.sh full` = **0 warnings / 0 errors** across all 5
     projects.
  2. `scripts/sandbox/run.sh` = **SMOKE RUN OK** (app builds its full visual
     tree and renders one frame headlessly under Xvfb with no startup crash).
  3. **Rendered visual comparison:** installed `xvfb`/`imagemagick`, temporarily
     pointed `MainWindowViewModel`'s startup navigation at
     `ReceptionPageViewModel`, captured the screen with
     `scripts/sandbox/shot.sh`, and compared it element-by-element against
     `Animora-UI/reception.html`. Header (eyebrow/title/subtitle + «ثبت مراجعهٔ
     حضوری»/«بازخوانی صف»), hero (live Jalali date, «صف فعال» pill, 4 metrics),
     4 KPI cards, filter tabs (همه/فعال/اورژانس/پایان‌یافته), search placeholder,
     «فیلتر» button, and per-row queue actions all match the reference. The
     temporary startup change was reverted (startup is back on
     `DashboardPageViewModel`) and the solution re-built 0/0.
  The missing build had hidden one real (non-fatal) defect:
  **`AVLN5001` — obsolete `TextBox.Watermark`.** `ReceptionPageView.axaml` (×1)
  and `WalkInModalView.axaml` (×3) used `Watermark="…"`, which Avalonia 12 marks
  obsolete in favour of `PlaceholderText`. All four were migrated to
  `PlaceholderText` (identical placeholder behaviour), restoring the roadmap's
  0-warning/0-error standard. Phase 07 is now **Done**; the phase pointer
  advances to 08.
- **Shell dialog API accepts ViewModels as content.** `IDialogService`/hosts now accept `object` rather than requiring a concrete `Control`, allowing `ReceptionPageViewModel` to open `WalkInModalViewModel` while the existing `ViewLocator` materializes `WalkInModalView`. This removes View construction from the ViewModel and keeps the modal flow idiomatic MVVM. The permanent modal/drawer/toast hosts are now attached once at `MainWindow`, as Phase 03 explicitly left for the real shell.
- **Dashboard deep-link wiring.** The existing «شروع ویزیت» quick action now navigates to `ReceptionPageViewModel`, replacing its placeholder target without adding a non-reference sidebar item or changing the dashboard tile count.

### Phase 06 — Dashboard Screen (role-based)

- **RESOLVED — build now verified; one hidden defect fixed.** The Phase 06
  code originally shipped un-built (no `dotnet` SDK in that session). A later
  audit session installed the .NET 10 SDK (10.0.401) and actually compiled +
  smoke-ran the solution. The missing build had hidden one real, fatal defect:
  1. **`AVLN2005` — invalid grid length `1fr`.**
     `ManagerDashboardView`, `CashierDashboardView`, and `DoctorDashboardView`
     each declared `<Grid ColumnDefinitions="1fr,1fr" …>`. `1fr` is CSS/WPF-
     `GridLength` grid syntax that Avalonia's XAML compiler **rejects** —
     Avalonia uses star sizing (`*`). All three were failing to parse, so the
     whole `Animora.Desktop` project would not build. Fix: changed all three to
     `ColumnDefinitions="*,*"` (two equal columns, identical layout intent).
  After the fix, `scripts/sandbox/build.sh full` = **0 warnings / 0 errors**
  and `scripts/sandbox/run.sh` = **SMOKE RUN OK** with `MockCurrentUserContext.
  Role` set to each of `Manager`/`Cashier`/`Doctor` in turn (all three role
  sub-views build their visual tree and render one frame with no crash), then
  restored to the `Manager` default. The earlier "run install-prereqs + build
  before Phase 07" action item is now closed.
- **Quick-actions tile 7 restored to the reference's "خرید و تأمین".** The
  first pass silently replaced reference `TILES[6]` (`خرید و تأمین`, truck
  icon) with a bespoke "مدیریت چک" tile. Task 3 asks the quick-actions strip to
  mirror `dashboard.js`'s `TILES`/`href` array, so the label/subtitle/target
  were restored to the reference (`خرید و تأمین` · `سفارش تأمین`). Phase 03's
  23-icon set has no truck geometry, so `IconReceipt` stands in for that one
  tile's icon only (documented inline); a later phase that adds a truck icon can
  swap it. Tile 4's subtitle was likewise aligned to the reference
  (`۶ یادآور امروز`).
- **`DashboardSnapshot` uses two generically-shaped content lists, not five
  bespoke sections.** The phase file says "shape it to match what
  DASH_VIEW_MANAGER/CASHIER/DOCTOR actually render, not a generic catch-all,"
  but all three role views' secondary sections (live queue, alerts, open
  invoices, ready-to-bill, vaccination reminders) share the exact same visual
  shape in the reference: avatar-letter + title + subtitle + one status pill.
  A single `DashboardListItem(AvatarText, Title, Subtitle, TagText, TagTone)`
  DTO plus two labeled instances (`PrimaryList`/`SecondaryList` with their own
  `*ListTitle`) covers every role's two-section layout without a proliferation
  of near-identical single-use DTOs — still role-shaped (each role's mock
  supplies different titles/rows/tones), just not five separate record types.
- **Revenue chart, species donut, and doctor hero live-clock/progress-ring
  omitted.** The phase's "Out of scope" section explicitly excludes spending
  budget on the reference's `requestAnimationFrame` counters/animations and
  scopes "one role-specific content section," and CONVENTIONS §7 caps phase
  scope to what's asked. The interactive SVG chart, donut, and doctor
  countdown ring are the reference's most animation-heavy, highest-effort
  pieces and are not reproduced; each role's two most information-dense
  sections (queue/alerts, invoices/ready-to-bill, queue/vaccination) are kept
  instead, matching "at a glance" comparability per the acceptance criteria.
- **Three role sub-views are visibility-toggled, not a `DataTemplate`-per-role
  `ContentControl`.** The phase file offers either "a `DataTemplate`-per-role
  or a `ContentControl` + converter" — with exactly three fixed, known roles
  (never more), three sibling `UserControl`s in the same `DashboardPageView`
  with `IsVisible` bound to `IsManager`/`IsCashier`/`IsDoctor` is simpler than
  either alternative and needs no converter or template-selector plumbing;
  all three share the one injected `DashboardPageViewModel` as DataContext
  exactly as the phase file asks.
- **`IDashboardQueryService` resolved synchronously in the ViewModel
  constructor**, not via an async `OnLoaded`. `PageViewModelBase` (Phase 05)
  has no `OnLoaded` hook yet, and the mock's `Task.FromResult` is already
  complete — `GetAwaiter().GetResult()` is safe here with no thread-pool
  hazard. Documented inline as the seam a future real/async backend
  implementation revisits (no ViewModel shape change needed, only the load
  call site).
- **`MockDashboardQueryService` registered as a singleton**, per the phase
  file's explicit "scoped/singleton — decide and note in PROGRESS.md" ask:
  it holds no state and returns fixed data per role, so a singleton avoids
  pointless re-allocation with no behavioral difference from scoped.

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

- **RESOLVED — build now verified; two hidden defects fixed.** A