# Phase 05 — App Shell: Sidebar, Topbar, Navigation

**Band:** M · **Depends on:** 02, 03, 04 · **Satisfies:** R-4, R-5, R-18

## Read before starting
- `CONVENTIONS.md` §1, §3 (navigation rule), §5 (DI/mock pattern)
- `Animora-UI/assets/layout.js` — full file (439 lines): this is the
  complete reference for the sidebar (`NAV_ITEMS`, `renderSidebar`), the
  role model (`ROLES`, `roleOf`), and the topbar (search remainder of file
  after `renderSidebar` — topbar markup/behavior, user chip, notification
  bell). This is the one file every screen phase's shell chrome derives
  from — read it in full now so later phases don't need to.
- `Animora-UI/assets/styles.css` — sidebar/topbar rule groups only
  (`.sidebar*`, `.nav-*`, `.brand*`, `.side-*`, `.topbar*`, `.search*`,
  `.icon-btn*`, `.user-chip*`) for visual sizing/spacing reference.

## Goal
Build the persistent app shell (sidebar navigation + topbar) and the
navigation/host mechanism every screen plugs into — the single biggest
"looks like the reference on every screen" element, done once here.

## Tasks
1. `Animora.Contracts/Navigation/NavItem.cs`: DTO — `Key, Label, IconKey,
   Href/ViewModelType, BadgeCount?, GroupName`.
2. `Animora.Contracts/Navigation/UserRole.cs`: enum `Manager, Cashier,
   Doctor` (mirrors reference `ROLES` keys `manager/cashier/doctor`).
3. `src/Animora.Desktop/Services/INavigationService.cs` +
   `NavigationService.cs`: exposes `CurrentViewModel` (observable, for
   binding the shell's content host), `NavigateTo<TViewModel>()` where
   `TViewModel : PageViewModelBase`, resolving instances via the DI
   container (`IServiceProvider`). Register as singleton in Phase 01's
   composition root.
4. `src/Animora.Desktop/Shell/PageViewModelBase.cs`: base
   `ObservableObject` with `Title`, `Eyebrow`, `Breadcrumbs
   (ObservableCollection<string>)`, `IsBusy` — every future screen
   ViewModel derives from this (per `CONVENTIONS.md` §3).
5. `src/Animora.Desktop/Shell/NavigationMenu.cs`: a static definition of
   the nav groups/items mirroring `NAV_ITEMS` exactly (same grouping:
   "مرکز فرمان", "مدیریت کلینیک", "کسب و کار", "مالی و خزانه", "سیستم" and
   same items/order/icons/badges) — this is layout/config, not a screen,
   so it is fine to hardcode here (it changes only when the sidebar itself
   changes, matching the reference's own hardcoded `NAV_ITEMS`).
6. `src/Animora.Desktop/Shell/SidebarView.axaml(.cs)` +
   `SidebarViewModel.cs`: renders brand header (logo tile + "وت‌پلاس" /
   tagline), the grouped nav list (active-item highlight + left accent bar
   matching `.nav-item.active`), badge pills, and the bottom
   upsell-card + user-status area (`.side-cta`, `.side-status`,
   `.side-user`) — reproduce visually; the upsell CTA button and
   collapse toggle can be inert/no-op stubs (log via `IToastService` if
   clicked) since they have no reference behavior beyond visual demo in
   the source either.
7. `src/Animora.Desktop/Shell/TopbarView.axaml(.cs)` + `TopbarViewModel.cs`:
   renders the page title block bound to `PageViewModelBase.Title`/
   `Breadcrumbs`, the search box (visual only, no live search wiring in
   this phase — later screens with real search build their own filter
   logic locally), notification bell icon-button (opens a stub
   `IDialogService` popover or is inert), and the user chip (avatar
   initials + name/role) bound to a simple `ICurrentUserContext`
   (interface in `Animora.Contracts`, mock implementation
   `MockCurrentUserContext` in `Animora.Desktop/Mocks` returning a fixed
   `manager` identity — this is the seam a future auth phase replaces).
8. `src/Animora.Desktop/Shell/MainWindow.axaml`: replace Phase 01's
   placeholder content with the real shell layout — `Grid` with
   `SidebarView` in one column, `TopbarView` + a `ContentControl`
   (`Content="{Binding CurrentViewModel}"`) stacked in the other, matching
   the reference's `.app` / `.sidebar` / `.main` / `.page` structure. Set
   up Avalonia `ViewLocator`/`DataTemplate`s so `ContentControl` resolves
   the correct View for whatever ViewModel `INavigationService` sets as
   current (standard Avalonia MVVM view-resolution pattern).
9. Add one throwaway placeholder screen, `Shell/PlaceholderPageViewModel.cs`
   + `.axaml` ("Screen not built yet — Phase NN"), and register it as the
   default `CurrentViewModel` at startup, and as the target for every
   `NavigationMenu` item that doesn't have a real screen yet (all of them,
   at this point) — so clicking every sidebar item at least navigates
   without crashing, proving the shell/navigation mechanism end-to-end.
   Each subsequent screen phase (06+) replaces exactly one item's target
   with its real ViewModel/View.

## Out of scope
- Any real screen content (Phases 06–28 each replace one placeholder).
- Live search, live notifications, role-switching UI — visual-only stubs
  here; no phase currently plans to wire these further (note in
  `PROGRESS.md` decision log if a later phase decides to).

## Acceptance criteria
- App builds/runs; window shows sidebar (all groups/items/badges matching
  the reference's `NAV_ITEMS`) + topbar (title/breadcrumb/user chip) +
  a placeholder content area; clicking every sidebar item swaps the
  content area to that item's placeholder without error, and the active
  nav item highlight updates correctly.

## PROGRESS.md update
Mark Phase 05 done; advance "Next phase" to 06; update "Repo state" to
note the shell exists and every nav item currently routes to a
placeholder.
