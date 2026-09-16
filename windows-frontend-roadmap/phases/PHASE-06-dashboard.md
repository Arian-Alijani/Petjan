# Phase 06 — Dashboard Screen (role-based)

**Band:** M · **Depends on:** 05 · **Satisfies:** R-4, R-5, R-18

## Read before starting
- `CONVENTIONS.md` §2, §3, §5
- `Animora-UI/dashboard.html` (full — it's tiny, just the shell mount call)
- `Animora-UI/assets/dashboard.js` — full file (478 lines): router/animation
  glue + the quick-actions grid (`href:` array).
- `Animora-UI/assets/dashboard-views.js` — full file (699 lines): the three
  role views `DASH_VIEW_MANAGER`, `DASH_VIEW_CASHIER`, `DASH_VIEW_DOCTOR`.
  This is the main visual/content reference for this phase.

## Goal
Replace the Dashboard placeholder (Phase 05) with the real, role-aware
dashboard screen: hero banner, KPI stat cards, quick actions, and one
role-specific content section (manager/cashier/doctor), matching the three
`DASH_VIEW_*` layouts.

## Contracts to add (`Animora.Contracts/Dashboard/`)
- `IDashboardQueryService` — `Task<DashboardSnapshot> GetSnapshotAsync(UserRole role)`
- `DashboardSnapshot` DTO — holds whatever fields the chosen role view
  needs (KPIs, queue rows, today's appointments, low-stock items, etc.) —
  shape it to match what `DASH_VIEW_MANAGER/CASHIER/DOCTOR` actually render,
  not a generic catch-all.

## Mock to add (`Animora.Desktop/Mocks/`)
- `MockDashboardQueryService` — deterministic in-memory sample data,
  re-derived (not copied) from what each `DASH_VIEW_*` function visually
  displays (KPI numbers, a handful of queue/appointment/stock rows).
  Register in DI (scoped/singleton — decide and note in `PROGRESS.md`).

## View/ViewModel tasks
1. `Pages/Dashboard/DashboardPageViewModel.cs` (extends
   `PageViewModelBase`): injects `IDashboardQueryService` +
   `ICurrentUserContext` (Phase 05); loads the snapshot for the current
   user's role on construction/`OnLoaded`; exposes role-specific bound
   collections/properties the View needs.
2. `Pages/Dashboard/DashboardPageView.axaml(.cs)`: hero banner (matching
   `.hero`/`.hero-inner`/date-chip/CTA), a KPI row using Phase 03's
   `StatCard`, and a role-conditional content region — use three small
   sub-views selected by the current role:
   - `Pages/Dashboard/ManagerDashboardView.axaml` (mirrors `DASH_VIEW_MANAGER`)
   - `Pages/Dashboard/CashierDashboardView.axaml` (mirrors `DASH_VIEW_CASHIER`)
   - `Pages/Dashboard/DoctorDashboardView.axaml` (mirrors `DASH_VIEW_DOCTOR`)
   Each sub-view binds to the same `DashboardPageViewModel` (or a small
   per-role child ViewModel property) — keep it simple: one ViewModel,
   `Content="{Binding}"` swapped via a `DataTemplate`-per-role or a
   `ContentControl` + converter selecting the right sub-view.
3. Quick-actions strip: a small `ItemsControl` bound to a static list
   mirroring `assets/dashboard.js`'s `href:` array (label, icon, target
   nav item) — clicking an action calls `INavigationService.NavigateTo<T>()`
   for the corresponding screen's placeholder (or real ViewModel, if that
   screen's phase has already run — check `PROGRESS.md`).
4. Update `Shell/NavigationMenu.cs`'s `dashboard` entry (and
   `MainWindow`'s default `CurrentViewModel`) to point at
   `DashboardPageViewModel` instead of the Phase 05 placeholder.

## Out of scope
- Any other screen's real content (quick-action targets besides Dashboard
  itself may still point at placeholders if their phase hasn't run yet —
  that's expected and correct at this point in the roadmap).
- Live data/animation/counters beyond a simple static/mock render (the
  reference's `animate()`/`requestAnimationFrame` counters are a nice-to-
  have; a static correct value is acceptable — do not spend phase budget
  animating unless trivial).

## Acceptance criteria
- App builds/runs; navigating to Dashboard (default screen) shows hero +
  KPIs + quick actions + the manager role view content (mock's default
  role) matching `DASH_VIEW_MANAGER`'s sections at a glance.
- Switching `MockCurrentUserContext`'s role (temporarily, for manual
  verification only — no UI role-switcher required) shows the cashier/
  doctor variants render correctly too.

## PROGRESS.md update
Mark Phase 06 done; advance "Next phase" to 07; update "Repo state."
