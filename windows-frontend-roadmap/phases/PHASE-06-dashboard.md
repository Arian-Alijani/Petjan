# Phase 06 — Dashboard Screen (role-based)

**Band:** M · **Depends on:** 05 · **Satisfies:** R-4, R-5, R-18

## Read before starting
- `CONVENTIONS.md` §2, §3, §4a (Fidelity Protocol — MANDATORY), §5, §6
  (Definition of Done incl. fidelity gate + visual verification), §7a
- **Reference CSS (MANDATORY — the source of every measurable value, per
  §4a/§8): `Animora-UI/assets/styles.css` (base: `.hero`, `.card`, `.stat`,
  `.badge`, grid/`.page`) + `Animora-UI/assets/dashboard.css` (dashboard-
  specific: hero banner, KPI grid, the revenue chart, the species donut, the
  doctor live-clock/progress-ring, quick-actions grid, per-role sections).**
  Read the sections these files define for every element listed below and
  transcribe exact paddings/gaps/radii/sizes/fills/shadows/grid templates.
- `Animora-UI/dashboard.html` (full — it's tiny, just the shell mount call)
- `Animora-UI/assets/dashboard.js` — full file (478 lines): router/animation
  glue + the quick-actions grid (`href:` array).
- `Animora-UI/assets/dashboard-views.js` — full file (699 lines): the three
  role views `DASH_VIEW_MANAGER`, `DASH_VIEW_CASHIER`, `DASH_VIEW_DOCTOR`.
  This is the main visual/content reference for this phase.

## Goal
Replace the Dashboard placeholder (Phase 05) with the real, role-aware
dashboard screen that is **visually indistinguishable** from the reference
(§4a). Rebuild, in full: the hero banner, the KPI stat-card row, the
quick-actions grid, **and the complete content of all three role views**
(`DASH_VIEW_MANAGER`, `DASH_VIEW_CASHIER`, `DASH_VIEW_DOCTOR`) — every card,
list, table, badge and **every data visualization** each role renders,
including (non-exhaustive — build whatever the reference actually shows):
the manager's revenue/finance chart and species/breakdown donut, the doctor's
live-clock / countdown progress-ring, and every KPI, queue row and alert list.
Nothing on any of the three role views is omitted, summarized, or replaced with
a substitute — see the Fidelity note below.

## Fidelity (MANDATORY — read `CONVENTIONS.md` §4a in full)
- **Every element on every role view is rebuilt.** The revenue chart, the
  species donut, and the doctor live-clock/progress-ring are **first-class
  deliverables of this phase**, drawn with Avalonia primitives (`Path`/
  `PathGeometry`/`ArcSegment` for the donut & ring, `Polyline`/`Path` +
  `LinearGradientBrush` for the chart) — **they may NOT be declared out of
  scope, "nice-to-have," or deferred.** Only their *motion/animation* may be
  simplified: render the chart/donut/ring at their final computed values and
  the clock at the current time instead of animating, but the elements
  themselves must be present and correct (§4a rule 6).
- **Exact values from `dashboard.css` + `styles.css`:** hero
  padding/gradient/radius, the KPI grid's column template and gaps, each
  stat card's internal spacing, the chart/donut/ring dimensions, and every
  section card's padding/gap/border/radius — transcribed, not eyeballed
  (§4a rule 3), reusing Phase 02 tokens where a value equals a token.
- **Exact Persian text** for every title/subtitle/label/KPI caption/section
  heading, character-for-character; all numbers through the Phase 04 Persian
  digit/grouping helpers (§4a rule 4). State pills use Phase 02 semantic tones
  1:1 (§4a rule 5).

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
   `.hero`/`.hero-inner`/date-chip/CTA with `dashboard.css`'s exact
   padding/gradient/radius), a KPI row using Phase 03's `StatCard` (exact
   grid template + gaps from the CSS), and a role-conditional content region —
   three sub-views selected by the current role, **each rebuilding its role
   view IN FULL**:
   - `Pages/Dashboard/ManagerDashboardView.axaml` (mirrors `DASH_VIEW_MANAGER`
     completely: KPI/section cards + the revenue chart + the species donut +
     every list/alert it renders)
   - `Pages/Dashboard/CashierDashboardView.axaml` (mirrors `DASH_VIEW_CASHIER`
     completely: open invoices, ready-to-bill, and every card/stat it renders)
   - `Pages/Dashboard/DoctorDashboardView.axaml` (mirrors `DASH_VIEW_DOCTOR`
     completely: the live-clock/countdown progress-ring + the queue +
     vaccination reminders + every card it renders)
   Each sub-view binds to the same `DashboardPageViewModel` (or a small
   per-role child ViewModel property) — one ViewModel, `Content="{Binding}"`
   swapped via a `DataTemplate`-per-role or a `ContentControl` + converter
   selecting the right sub-view. Charts/donut/ring are built as small reusable
   Avalonia controls (see the Fidelity note) so later screens can reuse them.
3. Quick-actions strip: a small `ItemsControl` bound to a static list
   mirroring `assets/dashboard.js`'s `href:` array (label, icon, target
   nav item) — clicking an action calls `INavigationService.NavigateTo<T>()`
   for the corresponding screen's placeholder (or real ViewModel, if that
   screen's phase has already run — check `PROGRESS.md`).
4. Update `Shell/NavigationMenu.cs`'s `dashboard` entry (and
   `MainWindow`'s default `CurrentViewModel`) to point at
   `DashboardPageViewModel` instead of the Phase 05 placeholder.

## Out of scope (narrow — does NOT permit dropping any dashboard element)
- Any *other* screen's real content: quick-action targets besides Dashboard
  itself may point at placeholders if that screen's phase hasn't run yet —
  that's expected and correct at this point in the roadmap.
- **Motion only:** the reference's `animate()`/`requestAnimationFrame` easing
  of counters and the ring's sweep may be rendered at their final/current
  values instead of animating (§4a rule 6). **This does NOT permit omitting
  the counter, the chart, the donut, the clock, or the ring — those are
  required deliverables of this phase (see Fidelity note).**

## Acceptance criteria
- App builds/runs; navigating to Dashboard (default screen) renders the hero,
  the KPI row, the quick-actions grid, and the manager role view **in full** —
  including the revenue chart and species donut — matching `DASH_VIEW_MANAGER`
  element-by-element (same boxes, order, sizes, colors, and Persian text).
- Switching `MockCurrentUserContext`'s role (temporarily, for manual
  verification only — no UI role-switcher required) renders the cashier and
  doctor variants **in full**, including the doctor's live-clock/countdown
  progress-ring, each matching its `DASH_VIEW_*` element-by-element.
- **Visual verification (§6.7 + §7a) performed** for all three roles and the
  result recorded in `PROGRESS.md`; every measurable value traces to
  `dashboard.css`/`styles.css`; no reference element is missing.

## PROGRESS.md update
Mark Phase 06 done; advance "Next phase" to 07; update "Repo state."
